using BannerKings.CampaignContent.Traits;
using BannerKings.Extensions;
using BannerKings.Managers.Court;
using BannerKings.Managers.Education;
using BannerKings.Managers.Kingdoms.Policies;
using BannerKings.Managers.Skills;
using BannerKings.Managers.Titles;
using BannerKings.Managers.Titles.Laws;
using BannerKings.Models.Vanilla.Abstract;
using BannerKings.Settings;
using BannerKings.Utils.Extensions;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Localization;

namespace BannerKings.Models.Vanilla
{
    public class BKArmyManagementModel : ArmyModel
    {
        // Fraction of a clan's influence cap that constitutes the "can afford to
        // form an army" floor. Shared by BKArmyBehavior.EvaluateCreateArmy (the
        // daily push gate) and AddInfluenceBonusParties (the surplus-scaled
        // invite boost) so the two never drift apart.
        public const float ArmyFormationInfluenceFloorFactor = 0.4f;

        public bool CanHeroRecruitHero(Hero recruiter, Hero recruited)
        {
            return true;
        }

        public bool CanHeroRecruitMercs(Hero recruiter, Hero partyLeader) => 
            (recruiter.MapFaction.IsKingdomFaction && recruiter.MapFaction.Leader == recruiter)
            || (recruiter.Clan.IsUnderMercenaryService && partyLeader != null && partyLeader.Clan == recruiter.Clan);

        // CheckPartyEligibility moved to a Harmony Postfix in VanillaModelTweakPatches.

        public override bool CanCreateArmy(Hero armyLeader)
        {
            if (armyLeader.Clan == null) return false;

            var kingdom = armyLeader.Clan.Kingdom;
            if (kingdom != null)
            {
                if (kingdom.Leader == armyLeader) return true;

                if (armyLeader.Clan.IsUnderMercenaryService) return true;

                // Gate sub-Dukedom secondary armies on whether the kingdom
                // still has UNCOMMITTED war parties to fill them. The prior
                // failure mode (BK_army_formation_audit.txt 2026-05-08) was
                // baron-tier armies firing CREATE, attracting zero JOINs
                // (everyone's already in the king's army), then DISPERSING for
                // Inactivity within hours — wasting influence. But the old
                // guard keyed on `Armies.Count > 0`, which hard-capped the
                // WHOLE kingdom at one army regardless of how many idle parties
                // (or how much influence) it had — so a rich, large realm could
                // never field a second army. The real predictor of a doomed
                // army is "no free parties to join it", not "an army exists".
                //
                // Vanilla's CanLordCreateArmy invites at most
                // ceil(warParties * 0.7) - partiesAlreadyInArmies parties. If
                // that pool is < 2, a new army would starve for JOINs, so we
                // keep the duke-only restriction; otherwise any eligible clan
                // may form an additional army. King and mercenaries bypass via
                // the early returns above; the player (Hero.MainHero) is exempt
                // — player agency over AI-tuning heuristics — and the law-based
                // privilege checks below still apply to everyone normally.
                if (armyLeader != Hero.MainHero
                    && kingdom.Armies != null && kingdom.Armies.Count > 0)
                {
                    int warParties = kingdom.WarPartyComponents != null ? kingdom.WarPartyComponents.Count : 0;
                    int inArmies = 0;
                    foreach (var existingArmy in kingdom.Armies)
                        inArmies += existingArmy.Parties != null ? existingArmy.Parties.Count : 0;
                    int uncommitted = (int) System.Math.Ceiling(warParties * 0.7f) - inArmies;

                    if (uncommitted < 2)
                    {
                        var existingTitle = BannerKingsConfig.Instance.TitleManager.GetHighestTitle(armyLeader);
                        if (existingTitle == null || existingTitle.TitleType > TitleType.Dukedom)
                            return false;
                    }
                }

                CouncilData council = BannerKingsConfig.Instance.CourtManager.GetCouncil(kingdom.RulingClan);
                if (council.GetHeroPositions(armyLeader).Any(x => x.Privileges.Contains(CouncilPrivileges.ARMY_PRIVILEGE)))
                    return true;

                FeudalTitle kingdomTitle = BannerKingsConfig.Instance.TitleManager.GetSovereignTitle(kingdom);
                FeudalTitle heroTitle = BannerKingsConfig.Instance.TitleManager.GetHighestTitle(armyLeader);
                if (kingdomTitle != null)
                {
                    if (kingdomTitle.Contract.IsLawEnacted(DefaultDemesneLaws.Instance.ArmyPrivate) && heroTitle != null)
                    {
                        if (kingdom.ActivePolicies.Contains(BKPolicies.Instance.LimitedArmyPrivilege))
                            return heroTitle.TitleType <= TitleType.Dukedom;
                        else return heroTitle.TitleType < TitleType.Lordship;     
                    }
                    else if (kingdomTitle.Contract.IsLawEnacted(DefaultDemesneLaws.Instance.ArmyHorde))
                        return armyLeader.IsClanLeader();
                } else return armyLeader.IsClanLeader();
            }

            return false;
        }

        // Bannerlord 1.4 removed ArmyManagementCalculationModel.
        // GetMobilePartiesToCallToArmy; the call-to-army candidate list is now
        // the out-parameter of CanLordCreateArmy. BK only filters that
        // candidate list (it does not change whether the lord may create an
        // army), so the base bool is returned unchanged.
        public override bool CanLordCreateArmy(MobileParty leaderParty, out TaleWorlds.Library.MBList<MobileParty> possibleArmyMembers)
        {
            bool canCreate = base.CanLordCreateArmy(leaderParty, out possibleArmyMembers);
            if (possibleArmyMembers == null) return canCreate;

            List<MobileParty> toRemove = new List<MobileParty>();
            var kingdom = leaderParty.LeaderHero?.Clan?.Kingdom;
            if (kingdom != null)
            {
                FeudalTitle kingdomTitle = BannerKingsConfig.Instance.TitleManager.GetSovereignTitle(kingdom);
                if (kingdomTitle != null && kingdomTitle.Contract.IsLawEnacted(DefaultDemesneLaws.Instance.ArmyLegion))
                {
                    foreach (MobileParty p in possibleArmyMembers)
                        if (p != leaderParty && p.LeaderHero != null && CanCreateArmy(p.LeaderHero))
                            toRemove.Add(p);
                }

                foreach (var party in leaderParty.LeaderHero.Clan.WarPartyComponents)
                {
                    if (!possibleArmyMembers.Contains(party.MobileParty) &&
                        party.MobileParty != leaderParty &&
                        party.MobileParty.IsAvailableForArmies())
                    {
                        possibleArmyMembers.Add(party.MobileParty);
                    }
                }

                // Influence-scaled invite boost. Vanilla caps the invite list
                // at ceil(warParties * 0.7) - partiesInArmies and IGNORES the
                // leader's influence beyond the 100 floor (its
                // GetInfluenceBudgetWhileCreatingArmy result is computed and
                // discarded). So a clan sitting on thousands of influence
                // fields exactly the same army as one with 200 — banked
                // influence buys nothing. Grant a modest number of extra invite
                // slots scaled by surplus influence above the formation floor,
                // and fill them with otherwise-eligible kingdom war parties the
                // vanilla trim left on the table. Capped at +3 so a war chest
                // can't over-mobilize the realm and strip the fiefs.
                AddInfluenceBonusParties(leaderParty, kingdom, possibleArmyMembers);

                foreach (MobileParty p in possibleArmyMembers)
                {
                    if (p.LeaderHero.Clan.IsUnderMercenaryService && !CanHeroRecruitMercs(leaderParty.LeaderHero, p.LeaderHero))
                        toRemove.Add(p);

                    if (leaderParty.LeaderHero.Clan.IsUnderMercenaryService && !p.LeaderHero.Clan.IsUnderMercenaryService)
                        toRemove.Add(p);
                }
            }

            foreach (MobileParty p in toRemove)
                possibleArmyMembers.Remove(p);

            return canCreate;
        }

        // Adds up to 3 extra eligible kingdom war parties to the army invite
        // list, scaled by how far the leader's influence exceeds the formation
        // floor (0.4 * influence cap, matching BKArmyBehavior.EvaluateCreateArmy).
        // Each additional 30% of the cap in surplus influence buys one slot.
        // Parties must pass the same availability test BK uses elsewhere and be
        // within vanilla's MaximumDistanceToCallToArmy of the leader, ranked by
        // strength so the boost pulls the most useful idle parties first.
        private void AddInfluenceBonusParties(MobileParty leaderParty, Kingdom kingdom,
            TaleWorlds.Library.MBList<MobileParty> possibleArmyMembers)
        {
            var clan = leaderParty.LeaderHero?.Clan;
            if (clan == null) return;

            float cap = BannerKingsConfig.Instance.InfluenceModel.CalculateInfluenceCap(clan).ResultNumber;
            if (cap <= 0f) return;

            float surplus = clan.Influence - cap * ArmyFormationInfluenceFloorFactor;
            int bonusSlots = (int) (surplus / (cap * 0.3f));
            if (bonusSlots <= 0) return;
            if (bonusSlots > 3) bonusSlots = 3;

            float maxDistance = Campaign.Current.Models.ArmyManagementCalculationModel.MaximumDistanceToCallToArmy;

            var candidates = new List<MobileParty>();
            foreach (var component in kingdom.WarPartyComponents)
            {
                var party = component.MobileParty;
                if (party == null || party == leaderParty) continue;
                if (possibleArmyMembers.Contains(party)) continue;
                if (!party.IsAvailableForArmies()) continue;
                if (party.LeaderHero?.Clan != null && party.LeaderHero.Clan.IsUnderMercenaryService) continue;
                if (Helpers.DistanceHelper.GetDistanceBetweenMobilePartyToMobileParty(party, leaderParty,
                        party.NavigationCapability, out _) >= maxDistance) continue;

                candidates.Add(party);
            }

            candidates.Sort((a, b) => b.Party.EstimatedStrength.CompareTo(a.Party.EstimatedStrength));

            for (int i = 0; i < bonusSlots && i < candidates.Count; i++)
                possibleArmyMembers.Add(candidates[i]);
        }

        // CalculateDailyCohesionChange and DailyBeingAtArmyInfluenceAward moved to
        // Harmony Postfixes in VanillaModelTweakPatches.

        public override int CalculatePartyInfluenceCost(MobileParty armyLeaderParty, MobileParty party)
        {
            if (party.LeaderHero == null || armyLeaderParty.LeaderHero == null)
            {
                return base.AverageCallToArmyCost;
            }

            if (armyLeaderParty.ActualClan == party.ActualClan)
            {
                return 0;
            }

            float result = base.CalculatePartyInfluenceCost(armyLeaderParty, party);
            if (!party.ActualClan.IsUnderMercenaryService)
            {
                var vassals = BannerKingsConfig.Instance.TitleManager.CalculateAllVassals(armyLeaderParty.ActualClan);
                if (!vassals.Contains(party.LeaderHero))
                {
                    result *= 1.5f;
                }
            }

            //result += BannerKingsConfig.Instance.InfluenceModel.CalculateInfluenceCap(armyLeaderParty.LeaderHero.Clan).ResultNumber * 0.01f;

            var kingdom = armyLeaderParty.LeaderHero?.Clan?.Kingdom;
            if (kingdom != null && CanCreateArmy(party.LeaderHero))
            {
                FeudalTitle kingdomTitle = BannerKingsConfig.Instance.TitleManager.GetSovereignTitle(kingdom);
                if (kingdomTitle != null)
                {
                    if (kingdomTitle.Contract.IsLawEnacted(DefaultDemesneLaws.Instance.ArmyPrivate))
                    {
                        result *= 2f;
                    }
                    else if (kingdomTitle.Contract.IsLawEnacted(DefaultDemesneLaws.Instance.ArmyLegion))
                    {
                        result *= 5f;
                    }
                }
            }

            return (int) result;
        }
    }
}