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
            // Watchdog-instrumented: this runs on the AI army-evaluation path
            // (Kingdom.CreateArmy prefix, CanLordCreateArmy member filter,
            // CalculatePartyInfluenceCost) right after BKArmy.AiHourlyTick —
            // the neighbourhood two BK_freeze.txt captures fingered.
            BannerKings.Utils.FreezeWatchdog.Enter("BKArmy.CanCreateArmy", BannerKings.Utils.TickTrace.IdOf(armyLeader));
            try {
            if (armyLeader.Clan == null) return false;

            // Player agency: the Legions doctrine delegates ARMY FORMATION to
            // appointed Legion Commanders for AI realms (see ArmyLegion below),
            // but the PLAYER can always raise an army regardless of legatus status
            // — rallying your own clan to war is yours to command, not a privilege
            // the realm grants you. Vanilla's CanLordCreateArmy still applies its
            // own eligibility (party count, etc.), so this only removes BK's
            // law-based block for the player.
            if (armyLeader == Hero.MainHero) return true;

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
            } finally { BannerKings.Utils.FreezeWatchdog.Exit(); }
        }

        // Bannerlord 1.4 removed ArmyManagementCalculationModel.
        // GetMobilePartiesToCallToArmy; the call-to-army candidate list is now
        // the out-parameter of CanLordCreateArmy. BK only filters that
        // candidate list (it does not change whether the lord may create an
        // army), so the base bool is returned unchanged.
        public override bool CanLordCreateArmy(MobileParty leaderParty, out TaleWorlds.Library.MBList<MobileParty> possibleArmyMembers)
        {
            BannerKings.Utils.FreezeWatchdog.Enter("BKArmy.CanLordCreateArmy", BannerKings.Utils.TickTrace.IdOf(leaderParty));
            try {
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
                        if (p != leaderParty && p.LeaderHero != null
                            // Your own clan always answers your call — the Legions
                            // strip (which keeps other potential commanders out of a
                            // legion) must not bar a leader from rallying their own
                            // family/retinue parties.
                            && p.ActualClan != leaderParty.ActualClan
                            && CanCreateArmy(p.LeaderHero))
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

            // Honest eligibility — the 1-party-army generation fix. Vanilla
            // decided `canCreate` from its OWN pre-filter candidate list; BK then
            // removes unreachable / at-sea / mercenary-mismatched parties. If that
            // leaves NO party that can actually reach the leader to gather, the
            // lord would still form a degenerate ONE-PARTY army (just the leader)
            // — which vanilla disperses for "not enough parties" within hours, and
            // the disband strands the freed party (the reproducible "1-party army
            // disband freeze"). Refuse formation unless at least one party is
            // reachable: a doomed army should never be created in the first place.
            // GetDistance is the hang-safe oracle (huge for unreachable, never
            // hangs); the loop breaks on the first reachable party so it is cheap.
            //
            // Consequence (intentional, for now): reachability is tested with
            // Default (land) nav, so an ALL-NAVAL army whose every member is only
            // sea-reachable is REFUSED. That's deliberate until the escort-join
            // path (SetMoveEscortParty) is made sea-aware — a member that can only
            // reach the leader by sea would hang the escort pathfind, so forming
            // the fleet-army would freeze anyway. A mixed army with any one
            // land-reachable member still forms.
            if (canCreate && possibleArmyMembers.Count > 0)
            {
                var anchor = leaderParty.CurrentSettlement ?? leaderParty.LeaderHero?.HomeSettlement;
                bool anyReachable = false;
                foreach (var p in possibleArmyMembers)
                {
                    if (p == null || p == leaderParty) continue;
                    bool pAtSea = false; try { pAtSea = p.IsCurrentlyAtSea; } catch { }
                    if (pAtSea) continue;
                    if (anchor == null) { anyReachable = true; break; } // can't verify → don't block
                    float gd;
                    try { gd = Campaign.Current.Models.MapDistanceModel.GetDistance(p, anchor, false, MobileParty.NavigationType.Default, out _); }
                    catch { gd = float.MaxValue; }
                    if (gd > 0f && gd < 50000f) { anyReachable = true; break; }
                }
                if (!anyReachable)
                {
                    possibleArmyMembers.Clear();
                    return false;
                }
            }
            else if (canCreate && possibleArmyMembers.Count == 0)
            {
                // No callable party at all → would be a 1-party army. Refuse.
                return false;
            }

            return canCreate;
            } finally { BannerKings.Utils.FreezeWatchdog.Exit(); }
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
            BannerKings.Utils.FreezeWatchdog.Enter("BKArmy.AddInfluenceBonusParties", BannerKings.Utils.TickTrace.IdOf(leaderParty));
            try {
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
                // Straight-line distance, NOT a pathfind. The previous call
                // routed through GetDistanceBetweenMobilePartyToMobileParty(...,
                // NavigationCapability, ...), which runs a NATIVE map pathfind
                // per war party. On a large late-game kingdom (100+ war parties),
                // called repeatedly during AI army evaluation, one of those
                // native queries hung the main thread — BK_freeze.txt captured
                // the signature twice: campaign thread frozen, GC counters
                // stopped (no managed allocation = stuck in native code), the
                // watchdog thread still alive, last handler BKArmy.AiHourlyTick.
                // This is only an invite-range heuristic for up to 3 bonus
                // slots — vanilla still does the real gathering and pathing — so
                // a cheap Euclidean distance is entirely adequate and cannot
                // hang or flood the pathfinder.
                if (leaderParty.GetPosition2D.Distance(party.GetPosition2D) >= maxDistance) continue;

                // Reachability gate (the straight-line check above is only a
                // cheap pre-filter). Don't invite a party that can't actually
                // reach the army by land: it never gathers (stranded across
                // water / at sea), the army disperses for inactivity, and the
                // freed party's path-home pathfind HANGS the campaign thread —
                // the "freeze on disbanding an AI army formed with influence"
                // report. GetDistance is the hang-safe oracle (huge for
                // unreachable, never hangs). Run it only on the straight-line-
                // close candidates (bounded), against the leader's settlement
                // anchor; skip the check only if the leader has no settlement
                // reference to test against.
                bool atSea = false;
                try { atSea = party.IsCurrentlyAtSea; } catch { }
                if (atSea) continue;
                var anchor = leaderParty.CurrentSettlement ?? leaderParty.LeaderHero?.HomeSettlement;
                if (anchor != null)
                {
                    float gd;
                    try { gd = Campaign.Current.Models.MapDistanceModel.GetDistance(party, anchor, false, MobileParty.NavigationType.Default, out _); }
                    catch { gd = float.MaxValue; }
                    if (gd >= 50000f) continue; // land-unreachable from the leader — don't summon
                }

                candidates.Add(party);
            }

            candidates.Sort((a, b) => b.Party.EstimatedStrength.CompareTo(a.Party.EstimatedStrength));

            for (int i = 0; i < bonusSlots && i < candidates.Count; i++)
                possibleArmyMembers.Add(candidates[i]);
            } finally { BannerKings.Utils.FreezeWatchdog.Exit(); }
        }

        // Hang-safe land-reachability check for "can this party reach the army
        // leader to gather?" Used by the army-formation paths (CallBannersGoal,
        // SummonGentry, JoinArmies) before Army.AddElementToArmy, so a party that
        // can't reach the leader by land — at sea, or across a water gap — is
        // never pulled into the army. (A member that can't arrive leaves the army
        // a degenerate 1-party army that vanilla disperses for NotEnoughParty;
        // for an AI besieger that produces the start-siege/abandon-siege loop, and
        // the freed party's path-home pathfind can hang.) GetDistance(Default) is
        // the hang-safe oracle (huge for unreachable, never hangs); anchor on the
        // leader's settlement, and when there's nothing to anchor against, allow
        // (can't verify → don't block, same discipline as CanLordCreateArmy).
        public static bool IsLandReachableForGather(MobileParty party, MobileParty leaderParty,
            TaleWorlds.CampaignSystem.Settlements.Settlement anchorOverride = null)
        {
            if (party == null || leaderParty == null) return false;
            bool atSea = false;
            try { atSea = party.IsCurrentlyAtSea; } catch { }
            if (atSea) return false;
            // Prefer the explicit gather point when the caller knows it (the army
            // gathers THERE, not at the leader's home), else fall back to the
            // leader's settlement / home.
            var anchor = anchorOverride ?? leaderParty.CurrentSettlement ?? leaderParty.LeaderHero?.HomeSettlement;
            if (anchor == null) return true; // can't verify → don't block
            float gd;
            try { gd = Campaign.Current.Models.MapDistanceModel.GetDistance(party, anchor, false, MobileParty.NavigationType.Default, out _); }
            catch { return false; }
            return gd > 0f && gd < 50000f;
        }

        // CalculateDailyCohesionChange and DailyBeingAtArmyInfluenceAward moved to
        // Harmony Postfixes in VanillaModelTweakPatches.

        public override int CalculatePartyInfluenceCost(MobileParty armyLeaderParty, MobileParty party)
        {
            // Also on the AI army-evaluation hot path (calls CanCreateArmy +
            // CalculateAllVassals title traversal). Wrapped so the next capture
            // can name it if anything here stalls.
            BannerKings.Utils.FreezeWatchdog.Enter("BKArmy.CalculatePartyInfluenceCost", BannerKings.Utils.TickTrace.IdOf(armyLeaderParty));
            try {
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
            } finally { BannerKings.Utils.FreezeWatchdog.Exit(); }
        }
    }
}