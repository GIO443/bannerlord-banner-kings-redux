using BannerKings.Behaviours.Diplomacy;
using BannerKings.Behaviours.Diplomacy.Groups;
using BannerKings.Behaviours.Diplomacy.Groups.Demands;
using BannerKings.CampaignContent.Traits;
using BannerKings.Managers.Court;
using BannerKings.Managers.Titles;
using BannerKings.Managers.Titles.Governments;
using BannerKings.Models.BKModels.Abstract;
using BannerKings.Settings;
using BannerKings.Utils.Extensions;
using BannerKings.Utils.Models;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BannerKings.Models.BKModels
{
    public class BKInterestGroupsModel : GroupsModel
    {
        public override bool WillHeroCreateGroup(DiplomacyGroup group, Hero hero, KingdomDiplomacy diplomacy)
        {
            if (hero == Hero.MainHero || !CanHeroCreateAGroup(hero, diplomacy)) return false;

            if (diplomacy.RadicalGroups.Any(x => x.Equals(group) && x.IsGroupActive)) return false;

            // Pretender / secessionist factions only form when the realm
            // actually conditions them: predicted support (PushScore — the same
            // 0..1 value the UI bar shows: legitimacy, war fatigue, crown
            // authority, etc.) must be at least 40%. Without this an AI spins up
            // doomed low-support factions that occupy the single per-type slot
            // and block the player from starting their own, and a slot that just
            // dissolved at 0 radicalism gets re-formed immediately.
            if (group is RadicalGroup radical && radical.PushScore < 0.4f) return false;

            if (group.CanHeroJoin(hero, diplomacy))
            {
                if (group is RadicalGroup)
                {
                    float chance = CalculateHeroJoinChance(hero, group, diplomacy).ResultNumber;
                    if (chance > 0f && chance < MBRandom.RandomFloat)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public ExplainedNumber CalculateFinancialCompromiseCost(Hero fulfiller, int minimumCost, float factor, bool explanations = false)
        {
            ExplainedNumber result = new ExplainedNumber(minimumCost, explanations);
            ExplainedNumber income = BannerKingsConfig.Instance.ClanFinanceModel.CalculateClanIncome(fulfiller.Clan);

            result.Add(income.ResultNumber * 10f, new TextObject("{=Ssi15mFy}Revenues of {CLAN}")
                .SetTextVariable("CLAN", fulfiller.Clan.Name));
            result.AddFactor(factor - 1f, new TextObject("{=ZQgfSkQ8}Generosity of the group leader"));
            return result;
        }

        public ExplainedNumber CalculateLeverageInfluenceCost(Hero fulfiller, int minimumInfluence, float factor, bool explanations = false)
        {
            ExplainedNumber result = new ExplainedNumber(minimumInfluence, explanations);

            return result;
        }

        public BKExplainedNumber CalculateGroupInfluence(InterestGroup group, bool explanations = false)
        {
            var result = new BKExplainedNumber(0f, explanations);
            result.LimitMin(0f);
            result.LimitMax(1f);

            KingdomDiplomacy diplomacy = group.KingdomDiplomacy;
            float totalPower = 0;
            foreach (var settlement in diplomacy.Kingdom.Settlements)
                if (settlement.Notables != null)
                    foreach (var notable in settlement.Notables)
                        totalPower += notable.Power;

            Dictionary<Clan, float> clanInfluences = new Dictionary<Clan, float>();
            float totalClanInfluence = 0f;
            foreach (var clan in diplomacy.Kingdom.Clans)
            {
                float f = CalculateClanInfluence(clan, diplomacy).ResultNumber;
                totalClanInfluence += f;
                clanInfluences.Add(clan, f);
            }

            int notables = 0;
            float notableInfluence = 0f;
            foreach (var member in group.Members)
            {
                if (member.IsNotable)
                {
                    notableInfluence += 0.25f * (member.Power / totalPower);
                    notables++;
                }

                if (member.Clan != null && member.IsClanLeader())
                {
                    if (!clanInfluences.ContainsKey(member.Clan))
                    {
                        continue;
                    }

                    result.Add(0.75f * (clanInfluences[member.Clan] / totalClanInfluence), member.Clan.Name);
                }
            }

            if (notables > 0) result.Add(notableInfluence, new TextObject("{=Ce2gcy3j}Dignataries (x{MEMBERS})")
                    .SetTextVariable("MEMBERS", notables));

            foreach (var outcome in group.RecentOucomes)
                if (outcome.Success && outcome.Enabled)
                    result.Add(-0.1f, outcome.Explanation);

            if (group.StringId == DefaultInterestGroup.Instance.Commoners.StringId)
                foreach (var fief in diplomacy.Kingdom.Fiefs)
                    if (fief.Loyalty <= 25f) result.Add(CalculateTownInfluence(fief).ResultNumber / MathF.Max(1f, (float)diplomacy.Kingdom.Fiefs.Count),
                            new TextObject("{=K0pRPse7}{TOWN}'s loyalty is low").SetTextVariable("TOWN", fief.Name));

            return result;
        }

        public BKExplainedNumber CalculateClanInfluence(Clan clan, KingdomDiplomacy diplomacy, bool explanations = false)
        {
            var result = new BKExplainedNumber(0f, explanations);
            result.LimitMin(0f);
            result.Add(TaleWorlds.CampaignSystem.Campaign.Current.Models.DiplomacyModel.GetClanStrength(clan), GameTexts.FindText("str_notable_power"));
           
            if (clan.Gold > 0)
            {
                result.Add(clan.Gold / 10f, GameTexts.FindText("str_wealth"));
            }

            result.Add(clan.Influence * 5f, 
                new TextObject("{=wwYABLRd}Clan Influence Limit"));

            return result;
        }

        public BKExplainedNumber CalculateTownInfluence(Town town, bool explanations = false)
        {
            var result = new BKExplainedNumber(0f, explanations);
            result.LimitMin(0f);
            result.Add(town.Prosperity);

            return result;
        }

        public BKExplainedNumber CalculateGroupSupport(InterestGroup group, bool explanations = false)
        {
            var result = new BKExplainedNumber(0f, explanations);
            result.LimitMin(0f);
            result.LimitMax(1f);
            KingdomDiplomacy diplomacy = group.KingdomDiplomacy;
            Hero sovereign = diplomacy.Kingdom.Leader;

            result.Add(diplomacy.Legitimacy * group.LegitimacyFactor, new TextObject("Legitimacy"));

            if (group.Leader != null)
            {
                result.Add(0.25f * group.Leader.GetRelation(sovereign) * 0.01f, new TextObject("{=uYDaqbt6}Approval by {HERO}")
                    .SetTextVariable("HERO", group.Leader.Name));
            }

            float approval = 0f;
            float notableApproval = 0f;
            int notables = 0;
            int otherMembers = 0;
            foreach (var member in group.Members)
            {
                if (member != group.Leader)
                {
                    float approvalResult = (0.25f / group.Members.Count) * member.GetRelation(sovereign) * 0.01f;
                    if (member.IsNotable)
                    {
                        notableApproval += approvalResult;
                        notables++;
                    }
                    else
                    {
                        approval += approvalResult;
                        otherMembers++;
                    }
                }
            }

            foreach (var outcome in group.RecentOucomes)
            {
                result.Add(outcome.Success ? 0.15f : -0.15f, outcome.Explanation);
            }

            if (otherMembers > 0)
            {
                result.Add(approval, new TextObject("{=ShSqfhkh}Approval by nobility members (x{MEMBERS})")
                    .SetTextVariable("MEMBERS", otherMembers));
            }

            if (notables > 0)
            {
                result.Add(notableApproval, new TextObject("{=JXfGqamr}Approval by dignataries (x{MEMBERS})")
                    .SetTextVariable("MEMBERS", notables));
            }

            float supportedPolicies = 0f;
            int supportedPoliciesCount = 0;
            foreach (var policy in group.SupportedPolicies)
            {
                if (diplomacy.Kingdom.ActivePolicies.Contains(policy))
                {
                    supportedPolicies += 0.25f / group.SupportedPolicies.Count;
                    supportedPoliciesCount++;
                }
            }
            result.Add(supportedPolicies, new TextObject("{=hxOViTwY}Endorsed policies active (x{COUNT})")
                .SetTextVariable("COUNT", supportedPoliciesCount));

            float shunnedPolicies = 0f;
            int shunnedPoliciesCount = 0;
            foreach (var policy in group.ShunnedPolicies)
            {
                if (diplomacy.Kingdom.ActivePolicies.Contains(policy))
                {
                    shunnedPolicies += -0.25f / group.ShunnedPolicies.Count;
                    shunnedPoliciesCount++;
                }
            }
            result.Add(-shunnedPolicies, new TextObject("{=ETikwXjV}Shunned policies active (x{COUNT})")
                .SetTextVariable("COUNT", shunnedPoliciesCount));

            FeudalTitle title = BannerKingsConfig.Instance.TitleManager.GetSovereignTitle(diplomacy.Kingdom);
            if (title != null)
            {
                float supportedLaws = 0f;
                int supportedLawsCount = 0;
                foreach (var law in group.SupportedLaws)
                {
                    if (title.Contract.IsLawEnacted(law))
                    {
                        supportedLaws += 0.25f / group.SupportedLaws.Count;
                        supportedLawsCount++;
                    }
                }
                result.Add(supportedLaws, new TextObject("{=MP5kk91f}Endorsed laws active (x{COUNT})")
                    .SetTextVariable("COUNT", supportedLawsCount));

                float shunnedLaws = 0f;
                int shunnedLawsCount = 0;
                foreach (var law in group.ShunnedLaws)
                {
                    if (title.Contract.IsLawEnacted(law))
                    {
                        shunnedLaws += 0.25f / group.ShunnedLaws.Count;
                        shunnedLawsCount++;
                    }
                }
                result.Add(-shunnedLaws, new TextObject("{=4UXFSw4t}Shunned laws active (x{COUNT})")
                    .SetTextVariable("COUNT", shunnedLawsCount));
            }

            var rel = BannerKingsConfig.Instance.ReligionsManager.GetHeroReligion(sovereign);
            bool matchingReligion = false;
            if (group.KingdomDiplomacy.Religion != null)
                if (rel != null && rel.Equals(group.KingdomDiplomacy.Religion))
                    matchingReligion = true;

            if (group.StringId == DefaultInterestGroup.Instance.Traditionalists.StringId)
            {
                if (sovereign.Culture == group.KingdomDiplomacy.Kingdom.Culture)
                    result.Add(0.12f, new TextObject("{=O8tcdKZ9}{HERO} is of traditional culture")
                        .SetTextVariable("HERO", sovereign.Name));

                if (matchingReligion) result.Add(0.12f, new TextObject("{=v3kM9Awv}{HERO} is of traditional faith")
                        .SetTextVariable("HERO", sovereign.Name));
            }

            if (group.StringId == DefaultInterestGroup.Instance.Zealots.StringId)
            {
                if (!matchingReligion) result.Add(-0.4f, new TextObject("{=xL5afAGh}{HERO} is not of traditional faith")
                        .SetTextVariable("HERO", sovereign.Name));
                else
                {
                    foreach (var tuple in rel.Faith.Traits)
                    {
                        TraitObject trait = tuple.Key;
                        int traitLevel = sovereign.GetTraitLevel(trait);
                        if (traitLevel != 0)
                        {
                            result.Add(traitLevel * 0.1f * (tuple.Value ? 1f : -1f), trait.Name);
                        }
                    }
                }
            }

            return result;
        }

        public BKExplainedNumber CalculateHeroInfluence(DiplomacyGroup group, KingdomDiplomacy diplomacy,
            Hero hero, bool explanations = false)
        {
            var result = new BKExplainedNumber(0f, explanations);
            float totalPower = 0;
            foreach (var settlement in diplomacy.Kingdom.Settlements)
            {
                if (settlement.Notables != null)
                {
                    foreach (var notable in settlement.Notables)
                    {
                        totalPower += notable.Power;
                    }
                }
            }

            if (hero.IsNotable)
            {
                result.Add((hero.Power / totalPower), GameTexts.FindText("str_notable_power"));
            }

            if (hero.Clan != null)
            {
                result.Add((hero.IsClanLeader() ? 1f : 0.1f) * CalculateClanInfluence(hero.Clan, diplomacy).ResultNumber, hero.Clan.Name);
            }

            return result;
        }

        public bool CanHeroJoinAGroup(Hero hero, KingdomDiplomacy diplomacy)
        {
            if (diplomacy.Kingdom != hero.MapFaction) return false;
            
            if (hero.IsLord && hero.MapFaction.IsKingdomFaction && hero.MapFaction.Leader == hero) return false;

            if (hero.IsChild || hero.IsDead) return false;

            if (hero.Clan != null && hero.Clan.IsUnderMercenaryService) return false;

            return true;
        }

        public BKExplainedNumber InviteToGroupInfluenceCost(DiplomacyGroup group, Hero invitee, KingdomDiplomacy diplomacy, bool explanations = false)
        {
            BKExplainedNumber result = new BKExplainedNumber(75f, explanations);

            if (invitee.Clan != null)
            {
                Dictionary<Clan, float> clanInfluences = new Dictionary<Clan, float>();
                float totalClanInfluence = 0f;
                foreach (var clan in diplomacy.Kingdom.Clans)
                {
                    float f = CalculateClanInfluence(clan, diplomacy).ResultNumber;
                    totalClanInfluence += f;
                    clanInfluences.Add(clan, f);
                }

                result.Add(200f * (clanInfluences[invitee.Clan] / totalClanInfluence), new TextObject("{=8JtaP3Ak}Political relevance of {CLAN}")
                    .SetTextVariable("CLAN", invitee.Clan.Name));

                float willingness = CalculateHeroJoinChance(invitee, group, diplomacy).ResultNumber;
                result.AddFactor(-willingness * 0.5f, new TextObject("{=JHcib2AV}Willingness to join this group"));
            }

            float leaderCap = BannerKingsConfig.Instance.InfluenceModel.CalculateInfluenceCap(group.Leader.Clan).ResultNumber;
            result.Add(leaderCap * 0.07f, new TextObject("{=1RD1OWYP}Influence limit of {CLAN}")
                .SetTextVariable("CLAN", group.Leader.Clan.Name));      

            return result;
        }

        public bool CanHeroJoinARadicalGroup(Hero hero, KingdomDiplomacy diplomacy) => CanHeroJoinAGroup(hero, diplomacy) &&
            hero.IsClanLeader() &&
            diplomacy.GetHeroRadicalGroup(hero) == null;

        // Per-group radical-membership gate. Pretender / Secession remain
        // clan-only (those are dynastic-power factions; a notable's vote on
        // who holds the crown is meaningless). The constitutional radicals —
        // Republican Movement and Imperial Restoration — historically seed in
        // the cities, so urban notables (merchants, artisans, preachers, gang
        // leaders / rural-armoury types) qualify. A notable's Power feeds
        // PowerProportion at the same ¼× weight already used by interest
        // groups, so a city-led Republican faction reads as half as militarily
        // potent as a clan-led one of equivalent count.
        public bool CanHeroJoinARadicalGroup(Hero hero, RadicalGroup group, KingdomDiplomacy diplomacy)
        {
            if (group == null) return CanHeroJoinARadicalGroup(hero, diplomacy);
            if (!CanHeroJoinAGroup(hero, diplomacy)) return false;
            if (diplomacy.GetHeroRadicalGroup(hero) != null) return false;

            // The constitutional radicals accept urban notables.
            bool isConstitutional =
                group.StringId == DefaultRadicalGroups.Instance.RepublicanMovement.StringId ||
                group.StringId == DefaultRadicalGroups.Instance.ImperialRestoration.StringId;

            if (isConstitutional && hero.IsNotable)
                return IsUrbanProfessional(hero);

            return hero.IsClanLeader();
        }

        // Urban-professional notable occupations — those whose interests live
        // in the chartered city and would politically support / oppose a
        // constitutional change. Headmen (rural commoners) and ordinary
        // RuralNotables are pointedly excluded; they push civic demands
        // through interest groups, not through constitutional radicals.
        private static bool IsUrbanProfessional(Hero hero)
        {
            var occ = hero.Occupation;
            return occ == Occupation.Merchant
                || occ == Occupation.Artisan
                || occ == Occupation.Preacher
                || occ == Occupation.GangLeader;
        }

        // --- Notable politics integration ---------------------------------
        //
        // A notable contributes to their interest group's TensionPressure
        // every tick based on the mood of the settlement they live in
        // (loyalty / security / prosperity) PLUS how badly the realm's
        // slavery + economic laws clash with their occupation profile. The
        // result is signed: a content notable in a sympathetic realm eases
        // tension; a restless notable in a hostile realm builds it. Caller
        // clamps the per-tick total at the group level.
        //
        // Per-occupation law profiles are intentionally narrow — we only
        // tilt notables on slavery + economic / civic legislation, since
        // those are the laws a city-dwelling notable would actually feel.
        // Higher-order constitutional questions (Crown Authority, government
        // form) are the clan layer's concern, not the notable's.

        // Daily mood contribution from a single notable, in the [-0.6..+0.6]
        // range before any per-tick clamping at the caller.
        public float CalculateNotableMood(Hero notable, KingdomDiplomacy diplomacy)
        {
            if (notable == null || !notable.IsNotable) return 0f;
            var settlement = notable.CurrentSettlement ?? notable.HomeSettlement;
            if (settlement == null || settlement.Town == null) return 0f;

            // Base mood from the settlement's headline numbers. Centred so a
            // mid-prosperity, mid-loyalty, mid-security town reads 0.
            float loyaltyTerm = (settlement.Town.Loyalty - 50f) / 100f;     // -0.5..+0.5
            float securityTerm = (settlement.Town.Security - 50f) / 100f;   // -0.5..+0.5
            float prosperityTerm = (settlement.Town.Prosperity - 5000f) / 10000f; // -0.5..+0.5
            float baseMood = (loyaltyTerm + securityTerm + prosperityTerm) / 3f;

            // Per-notable amplifier from the realm's current slavery +
            // economic laws clashing with the notable's occupation profile.
            // mismatch ∈ [0..1]; we mix it as a mild signed bump.
            float mismatch = GetNotableLawStanceMismatch(notable, diplomacy);
            // A restless-law realm tips a notable AWAY from content (mood
            // becomes more negative) by up to 0.3.
            return MathF.Clamp(baseMood - mismatch * 0.3f, -0.6f, 0.6f);
        }

        // Share of the realm's slavery + economic / civic laws that the
        // notable's occupation profile actively shuns, 0..1. The profile
        // is occupation-keyed and intentionally narrow — see comment above.
        public float GetNotableLawStanceMismatch(Hero notable, KingdomDiplomacy diplomacy)
        {
            if (notable == null || diplomacy?.Kingdom == null) return 0f;
            var profile = GetOccupationProfile(notable.Occupation);
            if (profile.ShunnedLawIds.Count == 0 && profile.ShunnedPolicyIds.Count == 0) return 0f;

            // Active laws come from BK title contracts; active policies from
            // vanilla Kingdom.ActivePolicies. We score the realm by counting
            // shunned items that are currently in force.
            int mismatches = 0;
            int total = 0;

            // Sovereign-title demesne laws
            var sovereign = BannerKingsConfig.Instance.TitleManager?.GetSovereignTitle(diplomacy.Kingdom);
            var activeLaws = sovereign?.Contract?.DemesneLaws;
            if (activeLaws != null)
            {
                foreach (var law in activeLaws)
                {
                    if (law == null) continue;
                    total++;
                    if (profile.ShunnedLawIds.Contains(law.StringId)) mismatches++;
                }
            }

            // Realm policies
            foreach (var policy in diplomacy.Kingdom.ActivePolicies)
            {
                if (policy == null) continue;
                total++;
                if (profile.ShunnedPolicyIds.Contains(policy.StringId)) mismatches++;
            }

            return total > 0 ? (float)mismatches / total : 0f;
        }

        // Static occupation -> {shunned laws, shunned policies} table. Built
        // once on first call. Authored against the existing BK demesne-law
        // and vanilla-policy IDs — keep this list narrow (slavery + economic
        // / civic) so notable politics doesn't quietly subsume the clan-layer
        // policy debate.
        private struct OccupationProfile
        {
            public HashSet<string> ShunnedLawIds;
            public HashSet<string> ShunnedPolicyIds;
            public static OccupationProfile Empty => new OccupationProfile
            {
                ShunnedLawIds = new HashSet<string>(),
                ShunnedPolicyIds = new HashSet<string>(),
            };
        }

        private static Dictionary<Occupation, OccupationProfile> _occupationProfiles;
        private static OccupationProfile GetOccupationProfile(Occupation occupation)
        {
            if (_occupationProfiles == null) _occupationProfiles = BuildOccupationProfiles();
            return _occupationProfiles.TryGetValue(occupation, out var p) ? p : OccupationProfile.Empty;
        }

        private static Dictionary<Occupation, OccupationProfile> BuildOccupationProfiles()
        {
            // Merchant — chartered trade interest. Slavery shunned (slaves
            // undercut paid labour and depress trade volume); road tolls and
            // war taxes shunned (they choke commerce). Serfdom shunned for
            // the same reason — bound peasants don't move where the roads do.
            var merchant = new OccupationProfile
            {
                ShunnedLawIds = new HashSet<string>
                {
                    "slavery_standard", "slavery_aserai", "slavery_vlandia",
                    "serfs_agriculture_duties", "serfs_military_service_duties",
                    "craftsmen_tax_duties", "craftsmen_military_service_duties",
                },
                ShunnedPolicyIds = new HashSet<string>
                {
                    "policy_road_tolls", "policy_war_tax", "policy_serfdom",
                    "policy_state_monopolies",
                },
            };

            // Artisan — guild and workshop interest. Slavery especially
            // shunned (slave artisans undercut their trade outright). Heavy
            // craftsmen duties hurt them directly.
            var artisan = new OccupationProfile
            {
                ShunnedLawIds = new HashSet<string>
                {
                    "slavery_standard", "slavery_aserai", "slavery_vlandia",
                    "craftsmen_tax_duties", "craftsmen_military_service_duties",
                },
                ShunnedPolicyIds = new HashSet<string>
                {
                    "policy_war_tax", "policy_serfdom",
                },
            };

            // Preacher — religion-driven. Slavery uniformly shunned; heavy
            // military-service duties on common people (drawing congregants
            // into the host) shunned.
            var preacher = new OccupationProfile
            {
                ShunnedLawIds = new HashSet<string>
                {
                    "slavery_standard", "slavery_aserai", "slavery_vlandia",
                    "serfs_military_service_duties", "craftsmen_military_service_duties",
                },
                ShunnedPolicyIds = new HashSet<string>
                {
                    "policy_war_tax",
                },
            };

            // Headman — rural commoner voice. Heavy serf duties + slavery
            // both shunned. Note: headmen are deliberately NOT eligible for
            // the constitutional radicals; this profile only feeds the
            // interest-group tension path.
            var headman = new OccupationProfile
            {
                ShunnedLawIds = new HashSet<string>
                {
                    "slavery_standard", "slavery_aserai", "slavery_vlandia",
                    "serfs_agriculture_duties", "serfs_military_service_duties",
                },
                ShunnedPolicyIds = new HashSet<string>
                {
                    "policy_road_tolls", "policy_serfdom",
                },
            };

            // GangLeader — the "armed urban" voice (city-armoury, militia
            // captains). They shun policies that strengthen central state
            // monopoly on force (royal guard, sacred majesty) and shun
            // serfdom (their recruitment pool is the unbound poor).
            var gang = new OccupationProfile
            {
                ShunnedLawIds = new HashSet<string>
                {
                    "serfs_agriculture_duties",
                },
                ShunnedPolicyIds = new HashSet<string>
                {
                    "policy_sacred_majesty", "policy_state_monopolies",
                    "policy_royal_guard", "policy_serfdom",
                },
            };

            return new Dictionary<Occupation, OccupationProfile>
            {
                { Occupation.Merchant, merchant },
                { Occupation.Artisan, artisan },
                { Occupation.Preacher, preacher },
                { Occupation.Headman, headman },
                { Occupation.GangLeader, gang },
            };
        }
        
        public bool CanHeroCreateAGroup(Hero hero, KingdomDiplomacy diplomacy)
        {
            bool peerage = false;
            CouncilData council = BannerKingsConfig.Instance.CourtManager.GetCouncil(hero.Clan);
            if (council.Peerage != null && council.Peerage.CanVote) peerage = true;

            return CanHeroJoinAGroup(hero, diplomacy) && hero.IsClanLeader() && diplomacy.Kingdom.Leader != hero &&
            hero.Clan.Fiefs.Count > 0 && peerage;
        }

        public override BKExplainedNumber CalculateHeroJoinChance(Hero hero, DiplomacyGroup group, KingdomDiplomacy diplomacy, bool explanations = false)
        {
            var result = new BKExplainedNumber(0f, explanations);
            result.LimitMin(-1f);
            result.LimitMax(1f);
            if (!CanHeroJoinAGroup(hero, diplomacy))
            {
                return result;
            }
            
            return group.IsInterestGroup ? CalculateHeroJoinInterestGroup(hero, (InterestGroup)group, diplomacy, ref result) :
               CalculateHeroJoinRadicalGroup(hero, (RadicalGroup)group, diplomacy, ref result);
        }

        public override BKExplainedNumber CalculateHeroJoinRadicalGroup(Hero hero, RadicalGroup group, KingdomDiplomacy diplomacy, ref BKExplainedNumber result)
        {
            if ((Campaign.Current.Models.CampaignTimeModel.CampaignStartTime + CampaignTime.Years(BannerKingsSettings.Instance.RadicalGroupYears)).IsFuture)
                result.Add(-1000f, new TextObject("{=!}Rebels Starting Years Offset MCM Setting"));

            Dictionary<Clan, float> clanInfluences = new Dictionary<Clan, float>();
            float totalClanInfluence = 0f;
            foreach (var clan in diplomacy.Kingdom.Clans)
            {
                float f = CalculateClanInfluence(clan, diplomacy).ResultNumber;
                totalClanInfluence += f;
                clanInfluences.Add(clan, f);
            }
            
            result.Add(-BannerKingsSettings.Instance.RadicalGroup + (clanInfluences[hero.Clan] / totalClanInfluence), new TextObject("{=!}Reluctance"));
            Hero ruler = diplomacy.Kingdom.Leader;
            float support = -MBMath.Map(diplomacy.Legitimacy, 0f, 1f, -0.25f, 0.25f);
            result.Add(support, new TextObject("{=KDH6VoKQ}Legitimacy of {HERO}")
                .SetTextVariable("HERO", ruler.Name));

            float relation = -MBMath.Map(hero.GetRelation(ruler), -100f, 100f, -0.4f, 0.4f);
            result.Add(relation, new TextObject("{=nnYfQnWv}{HERO1}`s opinion of {HERO2}")
                    .SetTextVariable("HERO1", hero.Name)
                    .SetTextVariable("HERO2", ruler.Name));

            InterestGroup interestGroup = diplomacy.GetHeroGroup(hero);
            if (interestGroup != null)
            {
                float groupSupport = -MBMath.Map(interestGroup.Support.ResultNumber, 0f, 1f, -0.1f, 0.1f);
                result.Add(groupSupport, new TextObject("{=!}Support from interest group ({GROUP})")
                        .SetTextVariable("GROUP", interestGroup.Name));
            }

            if (group.Leader != null && hero != group.Leader)
            {
                float relationLeader = -MBMath.Map(hero.GetRelation(group.Leader), -100f, 100f, -0.15f, 0.15f);
                result.Add(relationLeader, new TextObject("{=nnYfQnWv}{HERO1}`s opinion of {HERO2}")
                    .SetTextVariable("HERO1", hero.Name)
                    .SetTextVariable("HERO2", group.Leader.Name));
            }

            bool positiveResult = result.ResultNumber > 0f;
            float honor = hero.GetTraitLevel(DefaultTraits.Honor);
            result.AddFactor(honor * 0.2f * (positiveResult ? -1 : 1), DefaultTraits.Honor.Name);

            float ambition = hero.GetTraitLevel(BKTraits.Instance.Ambitious);
            result.AddFactor(ambition * 0.3f * (positiveResult ? 1 : -1), BKTraits.Instance.Ambitious.Name);

            if (group.StringId == DefaultRadicalGroups.Instance.Pretender.StringId)
            {
                ClaimantDemand demand = (ClaimantDemand)group.CurrentDemand;
                if (demand.Claimant != null)
                {
                    if (demand.Claimant != hero)
                    {
                        float relationClaimant = MBMath.Map(hero.GetRelation(demand.Claimant), -100f, 100f, -0.25f, 0.25f);
                        result.Add(relationClaimant, new TextObject("{=nnYfQnWv}{HERO1}`s opinion of {HERO2}")
                            .SetTextVariable("HERO1", hero.Name)
                            .SetTextVariable("HERO2", demand.Claimant.Name));
                    }
                    else
                    {
                        result.Add(0.2f + (ambition * 0.1f), new TextObject("{=s7sxJgWg}{HERO1}`s ambition of ruling")
                            .SetTextVariable("HERO1", hero.Name)
                                                    .SetTextVariable("HERO2", demand.Claimant.Name));
                    }
                }
            }
         
            return result;
        }

        public BKExplainedNumber CalculateHeroJoinInterestGroup(Hero hero, InterestGroup group, KingdomDiplomacy diplomacy, ref BKExplainedNumber result)
        {
            if (hero.IsLord && !group.AllowsNobles)
            {
                return result;
            }

            if (!hero.IsLord && !group.AllowsCommoners)
            {
                return result;
            }

            if (group.Equals(DefaultInterestGroup.Instance.Royalists))
            {
                Hero leader = hero.MapFaction.Leader;
                float relation = hero.GetRelation(leader);
                result.Add(relation * 0.003f);
            }

            if (group.Equals(DefaultInterestGroup.Instance.Traditionalists))
            {
                Hero leader = hero.MapFaction.Leader;
                float relation = hero.GetRelation(leader);
                result.Add(relation * 0.001f);

                if (hero.Clan != null)
                {
                    result.Add(hero.Clan.Tier * 0.02f);
                }

                if (hero.Culture == hero.MapFaction.Culture)
                {
                    result.Add(0.1f);
                }
            }

            if (group.Equals(DefaultInterestGroup.Instance.Oligarchists))
            {
                result.Add(hero.Clan.Tier * 0.05f);
                var title = BannerKingsConfig.Instance.TitleManager.GetHighestTitle(hero);
                if (title != null)
                {
                    result.Add((5f - (int)title.TitleType) * 0.25f);
                }
            }

            if (group.PreferredOccupations.Contains(hero.Occupation))
            {
                result.Add(0.2f);
            }

            var rel = BannerKingsConfig.Instance.ReligionsManager.GetHeroReligion(hero);
            if (rel != null && rel.Equals(diplomacy.Religion))
            {
                result.Add(0.075f);
            }

            result.Add(hero.GetTraitLevel(group.MainTrait) * 0.15f);
            return result;
        }

        public bool IsGroupAdequateForKingdom(KingdomDiplomacy diplomacy, InterestGroup group)
        {
            var title = BannerKingsConfig.Instance.TitleManager.GetSovereignTitle(diplomacy.Kingdom);
            if (title != null)
            {
                if (group.Equals(DefaultInterestGroup.Instance.Royalists) && (title.Contract.Government == DefaultGovernments.Instance.Feudal 
                    || title.Contract.Government == DefaultGovernments.Instance.Imperial))
                {
                    return true;
                }

                if (group.Equals(DefaultInterestGroup.Instance.Traditionalists) && title.Contract.Government == DefaultGovernments.Instance.Tribal)
                {
                    return true;
                }
            }

            if (group.Equals(DefaultInterestGroup.Instance.Commoners) || group.Equals(DefaultInterestGroup.Instance.Oligarchists))
            {
                return true;
            }

            if (group.Equals(DefaultInterestGroup.Instance.Zealots))
            {
                return diplomacy.Religion != null;
            }

            return false;
        }
    }
}
