using System.Collections.Generic;
using System.Linq;
using BannerKings.Behaviours.Diplomacy;
using BannerKings.Behaviours.Diplomacy.Groups;
using BannerKings.Utils;
using BannerKings.Managers.Skills;
using BannerKings.Managers.Titles;
using BannerKings.Managers.Titles.Laws;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Policies;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using static TaleWorlds.CampaignSystem.Election.KingSelectionKingdomDecision;

namespace BannerKings.Patches
{
    namespace Recruitment
    {
        [HarmonyPatch(typeof(RecruitmentCampaignBehavior))]
        internal class RecruitmentApplyInternalPatch
        {
            [HarmonyPostfix]
            [HarmonyPatch("ApplyInternal", MethodType.Normal)]
            private static void ApplyInternalPostfix(MobileParty side1Party, Settlement settlement, Hero individual,
                CharacterObject troop, int number, int bitCode, RecruitmentCampaignBehavior.RecruitingDetail detail)
            {
                if (settlement == null)
                {
                    return;
                }

                var data = BannerKingsConfig.Instance.PopulationManager.GetPopData(settlement);
                if (data != null)
                {
                    data.MilitaryData.DeduceManpower(data, number, troop, individual);
                }
            }

            /*[HarmonyPrefix]
            [HarmonyPatch("UpdateVolunteersOfNotablesInSettlement", MethodType.Normal)]
            private static bool UpdateVolunteersPrefix(Settlement settlement)
            {
                if ((settlement.Town != null && !settlement.Town.InRebelliousState && settlement.Notables != null) || 
                    (settlement.IsVillage && !settlement.Village.Bound.Town.InRebelliousState))
                {
                    var data = BannerKingsConfig.Instance.PopulationManager.GetPopData(settlement);
                    if (data == null)
                    {
                        return true;
                    }

                    foreach (Hero hero in settlement.Notables)
                    {
                        if (hero.CanHaveRecruits)
                        {
                            bool flag = false;
                            CharacterObject basicVolunteer = TaleWorlds.CampaignSystem.Campaign.Current.Models.VolunteerModel.GetBasicVolunteer(hero);

                            for (int i = 0; i < hero.VolunteerTypes.Length; i++)
                            {
                                if (MBRandom.RandomFloat < TaleWorlds.CampaignSystem.Campaign.Current.Models.VolunteerModel.GetDailyVolunteerProductionProbability(hero, i, settlement))
                                {
                                    CharacterObject characterObject = hero.VolunteerTypes[i];
                                    if (characterObject == null)
                                    {
                                        hero.VolunteerTypes[i] = basicVolunteer;
                                        flag = true;
                                    }
                                    else if (characterObject.UpgradeTargets != null && characterObject.UpgradeTargets.Length != 0 && characterObject.Tier <= 3)
                                    {
                                        float num = MathF.Log(hero.Power / (float)characterObject.Tier, 2f) * 0.01f;
                                        if (MBRandom.RandomFloat < num)
                                        {
                                            hero.VolunteerTypes[i] = characterObject.UpgradeTargets[MBRandom.RandomInt(characterObject.UpgradeTargets.Length)];
                                            flag = true;
                                        }
                                    }
                                }
                            }
                            if (flag)
                            {
                                CharacterObject[] volunteerTypes = hero.VolunteerTypes;
                                for (int j = 1; j < volunteerTypes.Length; j++)
                                {
                                    CharacterObject characterObject2 = volunteerTypes[j];
                                    if (characterObject2 != null)
                                    {
                                        int num2 = 0;
                                        int num3 = j - 1;
                                        CharacterObject characterObject3 = volunteerTypes[num3];
                                        while (num3 >= 0 && (characterObject3 == null || (float)characterObject2.Level + (characterObject2.IsMounted ? 0.5f : 0f) < (float)characterObject3.Level + (characterObject3.IsMounted ? 0.5f : 0f)))
                                        {
                                            if (characterObject3 == null)
                                            {
                                                num3--;
                                                num2++;
                                                if (num3 >= 0)
                                                {
                                                    characterObject3 = volunteerTypes[num3];
                                                }
                                            }
                                            else
                                            {
                                                volunteerTypes[num3 + 1 + num2] = characterObject3;
                                                num3--;
                                                num2 = 0;
                                                if (num3 >= 0)
                                                {
                                                    characterObject3 = volunteerTypes[num3];
                                                }
                                            }
                                        }
                                        volunteerTypes[num3 + 1 + num2] = characterObject2;
                                    }
                                }
                            }
                        }
                    }
                }

                return false;
            }

            [HarmonyPrefix]
            [HarmonyPatch("RecruitVolunteersFromNotable", MethodType.Normal)]
            private static bool RecruitVolunteersFromNotablePrefix(RecruitmentCampaignBehavior __instance, MobileParty mobileParty, Settlement settlement)
            {
                // RecruitEverywhere replaces this whole flow — let vanilla run so its
                // postfix sees normal state.
                if (ModCompat.RecruitEverywhere) return true;

                if (mobileParty.ActualClan != null && mobileParty.ActualClan.IsClanTypeMercenary)
                {
                    Console.Write("");
                }

                if (((float)mobileParty.Party.NumberOfAllMembers + 0.5f) / (float)mobileParty.Party.PartySizeLimit <= 1f)
                {
                    foreach (Hero hero in settlement.Notables)
                    {
                        if (hero.IsAlive)
                        {
                            if (mobileParty.IsWageLimitExceeded())
                            {
                                break;
                            }
                            int num = MBRandom.RandomInt(6);
                            int num2 = Campaign.Current.Models.VolunteerModel.MaximumIndexHeroCanRecruitFromHero(mobileParty.IsGarrison ? mobileParty.Party.Owner : mobileParty.LeaderHero, hero, -101);
                            for (int i = num; i < num + 6; i++)
                            {
                                int num3 = i % 6;
                                if (num3 >= num2)
                                {
                                    break;
                                }
                                int num4 = (mobileParty.LeaderHero != null) ? ((int)MathF.Sqrt((float)mobileParty.LeaderHero.Gold / 10000f)) : 0;
                                float num5 = MBRandom.RandomFloat;
                                for (int j = 0; j < num4; j++)
                                {
                                    float randomFloat = MBRandom.RandomFloat;
                                    if (randomFloat > num5)
                                    {
                                        num5 = randomFloat;
                                    }
                                }
                                if (mobileParty.Army != null)
                                {
                                    float y = (mobileParty.Army.LeaderParty == mobileParty) ? 0.5f : 0.67f;
                                    num5 = MathF.Pow(num5, y);
                                }
                                float num6 = (float)mobileParty.Party.NumberOfAllMembers / (float)mobileParty.Party.PartySizeLimit;
                                if (num5 > num6 - 0.1f)
                                {
                                    CharacterObject characterObject = hero.VolunteerTypes[num3];
                                    if (characterObject != null && mobileParty.LeaderHero.Gold > Campaign.Current.Models.PartyWageModel.GetTroopRecruitmentCost(characterObject, mobileParty.LeaderHero, false) && mobileParty.PaymentLimit >= mobileParty.TotalWage + Campaign.Current.Models.PartyWageModel.GetCharacterWage(characterObject))
                                    {
                                        MethodInfo recruit = __instance.GetType().GetMethod("GetRecruitVolunteerFromIndividual", BindingFlags.NonPublic | BindingFlags.Instance);
                                        recruit.Invoke(__instance, new object[] { mobileParty, characterObject, hero, num3 });
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }

                return false;
            }*/
        }
    }

    namespace Peerage
    {
        [HarmonyPatch(typeof(KingdomDecision))]
        internal class DetermineSupportersPatch
        {
            [HarmonyPrefix]
            [HarmonyPatch("DetermineSupporters")]
            private static bool DetermineSupportersPrefix(KingdomDecision __instance, ref IEnumerable<Supporter> __result)
            {
                // Mod-compat: Diplomacy / AIInfluence rebuild kingdom
                // decision support computation. Silently replacing vanilla
                // here fights those mods' parallel pipelines.
                if (BannerKings.Utils.ModCompat.DiplomacyMod || BannerKings.Utils.ModCompat.AIInfluence) return true;
                if (__instance == null || __instance.Kingdom == null) return true;
                var list = new List<Supporter>();
                foreach (Clan clan in __instance.Kingdom.Clans)
                {
                    var council = BannerKingsConfig.Instance?.CourtManager?.GetCouncil(clan);
                    if (council != null && council.Peerage != null && !clan.IsUnderMercenaryService)
                    {
                        if (council.Peerage.CanVote)
                        {
                            list.Add(new Supporter(clan));
                        }
                    }
                }

                __result = list;
                return false;
            }

            [HarmonyPrefix]
            [HarmonyPatch("IsPlayerParticipant", MethodType.Getter)]
            private static bool IsPlayerParticipantPrefix(KingdomDecision __instance, ref bool __result)
            {
                // Mod-compat for the same reason as DetermineSupporters
                // above: don't replace vanilla on the participant gate
                // when Diplomacy/AIInfluence own this pipeline.
                if (BannerKings.Utils.ModCompat.DiplomacyMod || BannerKings.Utils.ModCompat.AIInfluence) return true;
                // Was: dereferenced council.Peerage without null-checking
                // council. Fall through to vanilla on early-game / no-
                // kingdom / mercenary state where GetCouncil legitimately
                // returns null (PlayerClan with no clan, etc).
                if (Clan.PlayerClan == null)
                {
                    return true;
                }
                var council = BannerKingsConfig.Instance?.CourtManager?.GetCouncil(Clan.PlayerClan);
                if (council == null)
                {
                    return true;
                }
                __result = __instance.Kingdom == Clan.PlayerClan.Kingdom && !Clan.PlayerClan.IsUnderMercenaryService &&
                    council.Peerage != null && council.Peerage.CanVote;
                return false;
            }
        }

        [HarmonyPatch(typeof(KingdomPoliciesVM), "GetCanProposeOrDisavowPolicyWithReason")]
        internal class GetCanProposeOrDisavowPolicyWithReasonPatch
        {
            private static bool Prefix(KingdomPoliciesVM __instance, bool hasUnresolvedDecision, ref bool __result, out TextObject disabledReason)
            {
                TextObject textObject;
                if (!CampaignUIHelper.GetMapScreenActionIsEnabledWithReason(out textObject))
                {
                    disabledReason = textObject;
                    __result = false;
                    return false;
                }
                if (Clan.PlayerClan.IsUnderMercenaryService)
                {
                    disabledReason = GameTexts.FindText("str_mercenaries_cannot_propose_policies", null);
                    __result = false;
                    return false;
                }
                if (!hasUnresolvedDecision && Clan.PlayerClan.Influence < (float)__instance.ProposalAndDisavowalCost)
                {
                    disabledReason = GameTexts.FindText("str_warning_you_dont_have_enough_influence", null);
                    __result = false;
                    return false;
                }

                var council = BannerKingsConfig.Instance.CourtManager.GetCouncil(Clan.PlayerClan);
                if (council != null)
                {
                    if (council.Peerage == null || (council.Peerage != null && !council.Peerage.CanStartElection))
                    {
                        disabledReason = new TextObject("{=RDDOdoeR}The Peerage of {CLAN} does not allow starting elections.")
                            .SetTextVariable("CLAN", Clan.PlayerClan.Name);
                        __result = false;
                        return false;
                    }
                }

                 disabledReason = TextObject.GetEmpty();
                __result = true;
                return false;
            }
        }
    }

    namespace Armies
    {
        [HarmonyPatch(typeof(Army), "UpdateName")]
        internal class ArmyUpdateNamePatch
        {
            // Cache once: previously did AccessTools.Property(...) on every
            // invocation. Army.UpdateName is called whenever army membership
            // changes — many times per campaign hour during war.
            private static readonly System.Reflection.PropertyInfo _armyNameProp =
                AccessTools.Property(typeof(Army), "Name");

            private static bool Prefix(Army __instance)
            {
                FeudalTitle title = BannerKingsConfig.Instance.TitleManager.GetSovereignTitle(__instance.Kingdom);
                TextObject leaderName = __instance.ArmyOwner != null ?
                    __instance.ArmyOwner.Name : ((__instance.LeaderParty?.PartyComponent?.PartyOwner != null) ?
                    __instance.LeaderParty.PartyComponent.PartyOwner.Name : TextObject.GetEmpty());
                TextObject result = new TextObject("{=nbmctMLk}{LEADER_NAME}{.o} Army");
                if (title != null)
                {
                    if (title.Contract.IsLawEnacted(DefaultDemesneLaws.Instance.ArmyHorde))
                    {
                        result = new TextObject("{=HCWYbPOa}{LEADER_NAME}{.o} Horde");
                    }
                    else if (title.Contract.IsLawEnacted(DefaultDemesneLaws.Instance.ArmyLegion))
                    {
                        result = new TextObject("{=4ubaOxe2}{LEADER_NAME}{.o} Legion");
                    }
                }

                _armyNameProp?.SetValue(__instance,
                    result.SetTextVariable("LEADER_NAME", leaderName));
                return false;
            }
        }
    }

    namespace Perks
    {

        [HarmonyPatch(typeof(MapEventParty), "ContributionToBattle", MethodType.Getter)]
        internal class ContributionToBattlePatch
        {
            private static void Postfix(MapEventParty __instance, ref int __result)
            {
                var leader = __instance.Party.LeaderHero;
                if (leader == null)
                {
                    return;
                }

                var education = BannerKingsConfig.Instance.EducationManager.GetHeroEducation(leader);
                if (education.HasPerk(BKPerks.Instance.MercenaryRansacker))
                {
                    __result = (int)(__result * 1.1f);
                }
            }
        }
    }

    namespace Government
    {
        [HarmonyPatch(typeof(KingdomPolicyDecision))]
        internal class KingdomPolicyDecisionPatches
        {
            // Was [HarmonyPostfix] on a method named Prefix returning bool
            // — Harmony treats it as a postfix and ignores the bool return,
            // so the skip-original `return false` was a silent no-op (the
            // method ran AFTER vanilla every time, overriding __result).
            // The original intent is a prefix that replaces vanilla when
            // BK governs the policy whitelist; restore that by switching
            // to [HarmonyPrefix].
            //
            // Second fix: the outer [HarmonyPatch] was typeof(KingSelectionKingdomDecision)
            // — copy-paste of the KingSelectionKingdomDecisionPatches attribute
            // below. Both prefix and postfix here are typed for KingdomPolicyDecision
            // (__instance, .Policy accessor) and the class is literally named
            // KingdomPolicyDecisionPatches; the wrong outer type meant Harmony
            // attached both methods to KingSelectionKingdomDecision instead.
            // Net effect was that the BK government-prohibition gate AND the
            // ±80 interest-group voting push on policy decisions never fired,
            // so AI clans scored policies on pure vanilla heuristics and
            // rarely mustered the vote threshold — visible as "kingdoms with
            // plenty of influence never seem to enact laws."
            [HarmonyPrefix]
            [HarmonyPatch("IsAllowed", MethodType.Normal)]
            private static bool Prefix(ref bool __result, KingdomPolicyDecision __instance)
            {
                if (BannerKingsConfig.Instance.TitleManager != null)
                {
                    var sovereign = BannerKingsConfig.Instance.TitleManager.GetSovereignTitle(__instance.Kingdom);
                    if (sovereign != null)
                    {
                        __result = !sovereign.Contract.Government.ProhibitedPolicies.Contains(__instance.Policy);
                        return false;
                    }
                }

                return true;
            }

            [HarmonyPostfix]
            [HarmonyPatch("DetermineSupport", MethodType.Normal)]
            private static void OutcomeMeritPostfix(ref float __result, KingdomPolicyDecision __instance,
                Clan clan, DecisionOutcome possibleOutcome)
            {
                // Skip if mods like Diplomacy own this surface, or if the
                // outcome isn't a policy outcome (mod-introduced subclass)
                // — the previous code dereferenced policyDecisionOutcome
                // without checking the cast.
                if (BannerKings.Utils.ModCompat.DiplomacyMod) return;
                KingdomPolicyDecision.PolicyDecisionOutcome policyDecisionOutcome =
                    possibleOutcome as KingdomPolicyDecision.PolicyDecisionOutcome;
                if (policyDecisionOutcome == null) return;
                if (clan?.Kingdom == null || clan.Leader == null) return;
                BKDiplomacyBehavior behavior = TaleWorlds.CampaignSystem.Campaign.Current.GetCampaignBehavior<BKDiplomacyBehavior>();
                if (behavior == null) return;
                KingdomDiplomacy diplomacy = behavior.GetKingdomDiplomacy(clan.Kingdom);

                if (diplomacy != null)
                {
                    InterestGroup group = diplomacy.GetHeroGroup(clan.Leader);
                    if (group != null)
                    {
                        bool neutral = true;
                        bool supports = false;
                        if (group.SupportedPolicies.Contains(__instance.Policy))
                        {
                            neutral = false;
                            supports = policyDecisionOutcome.ShouldDecisionBeEnforced;
                        }
                        else if (group.ShunnedPolicies.Contains(__instance.Policy))
                        {
                            neutral = false;
                            supports = !policyDecisionOutcome.ShouldDecisionBeEnforced;
                        }

                        if (!neutral) __result += supports ? 80f : -80;
                    }
                }
            }
        }

        [HarmonyPatch(typeof(KingSelectionKingdomDecision))]
        internal class KingSelectionKingdomDecisionPatches
        {
            [HarmonyPostfix]
            [HarmonyPatch("ApplyChosenOutcome", MethodType.Normal)]
            private static void ApplyChosenOutcomePostfix(KingSelectionKingdomDecision __instance, DecisionOutcome chosenOutcome)
            {
                var title = BannerKingsConfig.Instance.TitleManager.GetSovereignTitle(__instance.Kingdom);
                if (title != null)
                {
                    var deJure = title.deJure;
                    var king = ((KingSelectionDecisionOutcome) chosenOutcome).King;
                    if (deJure != king)
                    {
                        BannerKingsConfig.Instance.TitleManager.InheritTitle(deJure, king, title);
                    }
                }
            }

            [HarmonyPrefix]
            [HarmonyPatch("CalculateMeritOfOutcomeForClan", MethodType.Normal)]
            private static bool CalculateMeritOfOutcomeForClanPrefix(KingSelectionKingdomDecision __instance, Clan clan, 
                DecisionOutcome candidateOutcome, ref float __result)
            {
                var title = BannerKingsConfig.Instance.TitleManager.GetSovereignTitle(__instance.Kingdom);
                if (title != null)
                {
                    Hero king = ((KingSelectionDecisionOutcome)candidateOutcome).King;
                    __result = BannerKingsConfig.Instance.TitleModel.GetSuccessionHeirScore(king, clan.Leader, title).ResultNumber;
                    return false;
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(SettlementClaimantDecision))]
        internal class FiefOwnerPatches
        {
            [HarmonyPrefix]
            [HarmonyPatch("DetermineInitialCandidates")]
            private static bool DetermineInitialCandidatesPrefix(SettlementClaimantDecision __instance,
                ref IEnumerable<DecisionOutcome> __result)
            {
                Kingdom kingdom = (Kingdom)__instance.Settlement.MapFaction;
                List<SettlementClaimantDecision.ClanAsDecisionOutcome> list = new List<SettlementClaimantDecision.ClanAsDecisionOutcome>();
                List<SettlementClaimantDecision.ClanAsDecisionOutcome> fallback = new List<SettlementClaimantDecision.ClanAsDecisionOutcome>();
                foreach (Clan clan in kingdom.Clans)
                {
                    if (clan != __instance.ClanToExclude && !clan.IsUnderMercenaryService && !clan.IsEliminated && !clan.Leader.IsDead)
                    {
                        // v1.9.10.41 — fallback list of all otherwise-eligible
                        // clans (mercs / eliminated / dead leaders still excluded)
                        // used when the peerage filter empties the main list.
                        // Without this, a small kingdom or a freshly-captured
                        // fief whose only peerage-capable clans are filtered
                        // out by ClanToExclude leaves the decision with zero
                        // candidates → IsAllowed false → no vote → settlement
                        // stays IsOwnerUnassigned forever.
                        fallback.Add(new SettlementClaimantDecision.ClanAsDecisionOutcome(clan));

                        var peerage = BannerKingsConfig.Instance.CourtManager.GetCouncil(clan).Peerage;
                        if (peerage == null || !peerage.CanHaveFief) continue;

                        list.Add(new SettlementClaimantDecision.ClanAsDecisionOutcome(clan));
                    }
                }
                __result = list.Count > 0 ? (IEnumerable<DecisionOutcome>)list : fallback;
                return false;
            }

            [HarmonyPrefix]
            [HarmonyPatch("CalculateMeritOfOutcome")]
            private static bool CalculateMeritOfOutcomePrefix(SettlementClaimantDecision __instance,
               DecisionOutcome candidateOutcome, ref float __result)
            {
                SettlementClaimantDecision.ClanAsDecisionOutcome clanAsDecisionOutcome = (SettlementClaimantDecision.ClanAsDecisionOutcome)candidateOutcome;  
                Settlement s = __instance.Settlement;
                ExplainedNumber result = BannerKingsConfig.Instance.DiplomacyModel.CalculateHeroFiefScore(s,
                    clanAsDecisionOutcome.Clan.Leader);

                __result = result.ResultNumber;
                return false;
            }

            [HarmonyPrefix]
            [HarmonyPatch("IsAllowed")]
            private static bool IsAllowedPrefix(SettlementClaimantDecision __instance, ref bool __result)
            {
                // v1.9.10.41 — was `> 2`, which blocked the vote in every
                // small-kingdom scenario plus any 2-candidate edge after
                // ClanToExclude filtering. Now `>= 1`: as long as the
                // candidate list has someone, the decision proceeds. The
                // candidate-list fallback in DetermineInitialCandidates
                // guarantees a list of at least 1 whenever any non-merc,
                // non-eliminated, living-leader clan exists in the kingdom.
                __result = __instance.DetermineInitialCandidates().Count() >= 1;
                return false;
            }

            /* [HarmonyPostfix]
             [HarmonyPatch("ShouldBeCancelledInternal")]
             private static void ShouldBeCancelledInternalPostfix(SettlementClaimantDecision __instance, ref bool __result)
             {
                 if (!__instance.Settlement.Town.IsOwnerUnassigned)
                 {
                     __result = true;
                 }
             }*/
        }
    }
}