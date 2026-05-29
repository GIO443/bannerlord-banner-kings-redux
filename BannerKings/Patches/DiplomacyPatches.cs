using BannerKings.Behaviours.Diplomacy.Wars;
using BannerKings.Behaviours.Diplomacy;
using BannerKings.Models.Vanilla;
using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Diplomacy;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Party;
using SandBox.View.Map;
using TaleWorlds.CampaignSystem.BarterSystem.Barterables;
using TaleWorlds.CampaignSystem.CampaignBehaviors.BarterBehaviors;
using BannerKings.Utils;
using BannerKings.Managers.Institutions.Religions;

namespace BannerKings.Patches
{
    internal class DiplomacyPatches
    {
        //AI companion dialogue fixes
        [HarmonyPatch(typeof(FactionManager))]
        internal class LordDialoguePatches
        {
            // DeclareAlliance removed from FactionManager in 1.3.x (alliances removed)

            [HarmonyPostfix]
            [HarmonyPatch("SetNeutral")]
            private static void SetNeutral(IFaction faction1, IFaction faction2)
            {
                if (faction1 != faction2 && !faction1.IsBanditFaction && !faction2.IsBanditFaction)
                {
                    StanceLink link = faction1.GetStanceWith(faction2);
                    // IsAllied removed in 1.3.x
                }

                UpdateVisuals(faction1, faction2);
            }

            [HarmonyPrefix]
            [HarmonyPatch("DeclareWar")]
            private static bool DeclareWar(IFaction faction1, IFaction faction2)
            {
                if (faction1 != faction2 && !faction1.IsBanditFaction && !faction2.IsBanditFaction)
                {
                    StanceLink link = faction1.GetStanceWith(faction2);
                    // IsAllied removed in 1.3.x
                }

                return true;
            }

            private static void UpdateVisuals(IFaction faction1, IFaction faction2)
            {
                if (CharacterObject.PlayerCharacter != null && Hero.MainHero != null && (faction1 == Hero.MainHero.MapFaction || faction2 == Hero.MainHero.MapFaction))
                {
                    IFaction dirtySide = (faction1 == Hero.MainHero.MapFaction) ? faction2 : faction1;
                    foreach (Settlement settlement in Settlement.All.Where((Settlement party) => party.IsVisible && party.MapFaction == dirtySide))
                        settlement.Party.SetVisualAsDirty();

                    foreach (MobileParty mobileParty in MobileParty.All.Where((MobileParty party) => party.IsVisible && party.MapFaction == dirtySide))
                        mobileParty.Party.SetVisualAsDirty();
                }
            }
        }

        /*[HarmonyPatch(typeof(Clan), "MapFaction", MethodType.Getter)]
        internal class ClanFactionPatch
        {
            private static BKDiplomacyBehavior behavior;
            private static BKDiplomacyBehavior Behavior
            {
                get
                {
                    if (behavior == null)
                    {
                        behavior = TaleWorlds.CampaignSystem.Campaign.Current.GetCampaignBehavior<BKDiplomacyBehavior>();
                    }

                    return behavior;
                }
            }
            private static bool Prefix(Clan __instance, ref IFaction __result)
            {
                if (__instance.Kingdom != null)
                {
                    if (Behavior.IsRebelling(__instance.Kingdom))
                    {
                        __result = __instance;
                        return false;
                    }
                }

                return true;
            }
        }*/

        // Phase G: BK no longer fully suppresses vanilla's war-consideration
        // pipeline. Vanilla decides WHEN to propose war (cadence, scoring,
        // influence cost, deduplication via _kingdomDecisionsList).
        // BK augments the decision after vanilla constructs it:
        //
        //   1. If BK has a truce with the picked target (natural post-peace
        //      window OR paid extension), nullify the decision so vanilla's
        //      caller skips AddDecision.
        //   2. Otherwise, if BK has an applicable CasusBelli, replace the
        //      plain DeclareWarDecision with a BKDeclareWarDecision carrying
        //      the CB.
        //   3. If no CB applies, leave vanilla's decision alone — war can
        //      still be proposed without a BK justification.
        //
        // Hooked at GetRandomWarDecision (private static helper that returns
        // the chosen decision before AddDecision is called). Postfix can
        // mutate __result. Single source of truth: vanilla owns proposal
        // cadence; BK owns justification + truce gate.
        [HarmonyPatch(typeof(KingdomDecisionProposalBehavior), "GetRandomWarDecision")]
        internal class GetRandomWarDecisionPatch
        {
            private static void Postfix(Clan clan, ref TaleWorlds.CampaignSystem.Election.KingdomDecision __result)
            {
                if (__result == null) return;
                if (clan == null || clan.Kingdom == null) return;

                if (!(__result is DeclareWarDecision dwd)) return;
                if (dwd is BKDeclareWarDecision) return;

                Kingdom target = dwd.FactionToDeclareWarOn as Kingdom;
                if (target == null) return;

                BKDiplomacyBehavior bkBehavior;
                try { bkBehavior = TaleWorlds.CampaignSystem.Campaign.Current.GetCampaignBehavior<BKDiplomacyBehavior>(); }
                catch { return; }
                if (bkBehavior == null) return;

                KingdomDiplomacy diplomacy = bkBehavior.GetKingdomDiplomacy(clan.Kingdom);
                if (diplomacy == null) return;

                // Truce gate: BK 1-year natural truce + paid extensions.
                // Vanilla's only truce-equivalent is a 20-day check on
                // PeaceDeclarationDate inside GetRandomWarDecision —
                // BK's gate extends that to a full year by default.
                if (diplomacy.IsInTruce(target))
                {
                    __result = null;
                    return;
                }

                // CB upgrade. Pick a random adequate CB for this target.
                //
                // Scoring-asymmetry note: vanilla's ConsiderWar already
                // approved this proposal using DeclareWarBarterable +
                // a KingdomElection on the plain DeclareWarDecision. After
                // upgrade, the actual kingdom vote runs on
                // BKDeclareWarDecision.DetermineSupport which uses
                // BKWarBarterable(CB, ...). Different scoring than what
                // gated approval. In practice both reflect the same
                // war-suitability signal (BK's CB scoring is additive
                // around vanilla's barterable value), so the asymmetry
                // doesn't typically produce a vote that contradicts the
                // gate. Worth noting if vote outcomes ever look wrong.
                try
                {
                    var availableCB = diplomacy.GetAvailableCasusBelli(target);
                    if (availableCB != null && availableCB.Count > 0)
                    {
                        var picked = availableCB.GetRandomElement();
                        if (picked != null)
                        {
                            __result = new BKDeclareWarDecision(picked, clan, target);
                        }
                    }
                    // No applicable CB: leave vanilla's plain
                    // DeclareWarDecision unchanged. War can still be
                    // declared without a BK justification — BK piety /
                    // doctrine effects skip but the war itself is
                    // legitimate vanilla state.
                }
                catch
                {
                    // Defensive: never crash a proposal tick on the
                    // upgrade. Vanilla decision passes through.
                }
            }
        }

        [HarmonyPatch(typeof(DiplomaticBartersBehavior), "ConsiderClanLeaveAsMercenary")]
        internal class ConsiderClanLeaveAsMercenaryPatch
        {
            private static bool Prefix(Clan clan)
            {
                // Mod-compat: Diplomacy overhauls the entire mercenary /
                // leave-kingdom barter pipeline. BK silently replacing
                // vanilla here would starve Diplomacy's overhaul. Match
                // the convention in the sibling DeclareWarVMPatch below.
                if (ModCompat.DiplomacyMod) return true;
                if (clan?.Leader == null || clan.Kingdom == null) return true;
                LeaveKingdomAsClanBarterable leaveKingdomAsClanBarterable = new LeaveKingdomAsClanBarterable(clan.Leader, null);
                MercenaryJoinKingdomBarterable mercenaryJoinKingdomBarterable = new MercenaryJoinKingdomBarterable(clan.Leader, null, clan.Kingdom);
                if (leaveKingdomAsClanBarterable.GetValueForFaction(clan) > mercenaryJoinKingdomBarterable.GetValueForFaction(clan))
                {
                    leaveKingdomAsClanBarterable.Apply();
                }

                return false;
            }
        }

        [HarmonyPatch(typeof(KingdomDiplomacyVM))]
        internal class DeclareWarVMPatch
        {
            // War support is a displayed approval %. RefreshDiplomacyList calls
            // CalculateWarSupport once per kingdom, and the kingdom screen
            // refreshes many times per open — uncached, BK's full KingdomElection
            // simulation ran thousands of times per screen open (a 30-90s stall).
            // Cache per faction, rebuilt once per in-game day. CalculateWarSupport
            // is a UI-thread VM method, so a plain dictionary is race-free here.
            private static readonly Dictionary<IFaction, int> _warSupportCache = new Dictionary<IFaction, int>();
            private static int _warSupportCacheDay = -1;

            [HarmonyPrefix]
            [HarmonyPatch("CalculateWarSupport")]
            private static bool CalculateWarSupportText(KingdomDiplomacyVM __instance, IFaction faction, ref int __result)
            {
                if (ModCompat.DiplomacyMod) return true;
                if (faction == null)
                {
                    __result = 0;
                    return false;
                }

                int today = (int)CampaignTime.Now.ToDays;
                if (today != _warSupportCacheDay)
                {
                    _warSupportCache.Clear();
                    _warSupportCacheDay = today;
                }

                if (!_warSupportCache.TryGetValue(faction, out var support))
                {
                    support = MathF.Round(new KingdomElection(
                        new BKDeclareWarDecision(null, Clan.PlayerClan, faction)).GetLikelihoodForSponsor(Clan.PlayerClan) * 100f);
                    _warSupportCache[faction] = support;
                }

                __result = support;
                return false;
            }

            [HarmonyPrefix]
            [HarmonyPatch("GetIsProposingWarEnabledWithReason")]
            private static bool ButtonCLickable(KingdomDiplomacyVM __instance, KingdomTruceItemVM item, float actionInfluenceCost,
                 ref TextObject disabledReason, ref bool __result)
            {
                if (ModCompat.DiplomacyMod) return true;
                disabledReason = TextObject.GetEmpty();
                __result = true;
                return false;
            }

            [HarmonyPrefix]
            [HarmonyPatch("OnDeclareWar")]
            private static bool ButtonPopup(KingdomDiplomacyVM __instance, KingdomTruceItemVM item)
            {
                // Diplomacy mod owns this VM's war/alliance/pact UI flow.
                if (ModCompat.DiplomacyMod) return true;
                IFaction enemy = item.Faction2;
                if (!enemy.IsKingdomFaction) return true;

                Kingdom enemyKingdom = enemy as Kingdom;
                Kingdom kingdom = item.Faction1 as Kingdom;
                KingdomDiplomacy diplomacy = TaleWorlds.CampaignSystem.Campaign.Current.GetCampaignBehavior<BKDiplomacyBehavior>().GetKingdomDiplomacy(kingdom);
                if (diplomacy == null) return true;
                
                if (kingdom.UnresolvedDecisions.Any(x => x is DeclareWarDecision || x is BKDeclareWarDecision))
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        new TextObject("{=ZdWBHwQL}A war declaration is being voted upon concerning the {FACTION}.")
                        .SetTextVariable("FACTION", enemyKingdom.Name)
                        .ToString()));
                }
                else
                {
                    var list = new List<InquiryElement>();
                    BKKingdomDecisionModel model = new BKKingdomDecisionModel();
                    Action<KingdomDiplomacy, Kingdom, KingdomDiplomacyVM> makeWar = ShowWarOptions;
                    TextObject warHint;
                    bool warPossible = model.IsWarDecisionAllowedBetweenKingdoms(kingdom, enemyKingdom, out warHint);
                    list.Add(new InquiryElement(makeWar,
                        new TextObject("{=eQNY5hgE}Declare War ({INFLUENCE}{INFLUENCE_ICON})")
                        .SetTextVariable("INFLUENCE", BannerKingsConfig.Instance.DiplomacyModel.GetInfluenceCostOfProposingWar(Clan.PlayerClan))
                        .SetTextVariable("INFLUENCE_ICON", Utils.TextHelper.INFLUENCE_ICON)
                        .ToString(),
                        null,
                        warPossible,
                        warHint.ToString()));

                    Action<KingdomDiplomacy, Kingdom, KingdomDiplomacyVM> makeAlliance = ShowAlliance;
                    TextObject allianceHint;
                    bool alliancePossible = model.IsAllianceAllowed(kingdom, enemyKingdom, out allianceHint);       
                    list.Add(new InquiryElement(makeAlliance,
                        new TextObject("{=EBhcYsfJ}Propose Alliance ({DENARS} {GOLD_ICON})")
                        .SetTextVariable("DENARS", 
                        MBRandom.RoundRandomized(BannerKingsConfig.Instance.DiplomacyModel.GetAllianceDenarCost(diplomacy.Kingdom, enemyKingdom).ResultNumber))
                        .ToString(),
                        null,
                        alliancePossible,
                        new TextObject("{=8Z0e830N}Propose a truce between both realms. A truce is a period of a certain amount of years in which both realms formally agree to not declare wars upon each other, in mutual benefit. The proposing realm is assumed to be the major beneficiary of this agreement, and thus is required a fee. The proposed realm is more likely to accept and offer better terms relative to how advantageous a truce is for them.\n\n{POSSIBLE}")
                        .SetTextVariable("POSSIBLE", allianceHint)
                        .ToString()));

                    bool playerRuler = Hero.MainHero == Clan.PlayerClan.Kingdom.RulingClan.Leader;
                    Action<KingdomDiplomacy, Kingdom, KingdomDiplomacyVM> makeTruce = ShowTruce;
                    TextObject truceHint;
                    bool trucePossible = model.IsTruceAllowed(kingdom, enemyKingdom, out truceHint);
                    list.Add(new InquiryElement(makeTruce,
                        new TextObject("{=K177C8ia}Propose Truce ({DENARS} {GOLD_ICON})")
                        .SetTextVariable("DENARS",
                        MBRandom.RoundRandomized(BannerKingsConfig.Instance.DiplomacyModel.GetTruceDenarCost(diplomacy.Kingdom, enemyKingdom).ResultNumber))
                        .ToString(),
                        null,
                        trucePossible && playerRuler,
                        new TextObject("{=8Z0e830N}Propose a truce between both realms. A truce is a period of a certain amount of years in which both realms formally agree to not declare wars upon each other, in mutual benefit. The proposing realm is assumed to be the major beneficiary of this agreement, and thus is required a fee. The proposed realm is more likely to accept and offer better terms relative to how advantageous a truce is for them.\n\n{POSSIBLE}")
                        .SetTextVariable("POSSIBLE", truceHint)
                        .ToString()));

                    Action<KingdomDiplomacy, Kingdom, KingdomDiplomacyVM> makePact = ShowTradePact;
                    TextObject tradeHint;
                    bool tradePossible = model.IsTradePactAllowed(kingdom, enemyKingdom, out tradeHint);
                    list.Add(new InquiryElement(makePact,
                        new TextObject("{=jHuXS5zK}Propose Trade Pact ({INFLUENCE}{INFLUENCE_ICON})")
                        .SetTextVariable("INFLUENCE", 
                        MBRandom.RoundRandomized(BannerKingsConfig.Instance.DiplomacyModel.GetTradePactInfluenceCost(diplomacy.Kingdom, enemyKingdom).ResultNumber))
                        .SetTextVariable("INFLUENCE_ICON", Utils.TextHelper.INFLUENCE_ICON)
                        .ToString(),
                        null,
                        tradePossible && playerRuler,
                        new TextObject("{=qEYgKaNs}Propose a trade pact between both realms. A trade access pact establishes the exemptions of caravan tariffs between both realms, meaning that their caravans will not pay entry fees in your realm's fiefs, nor will your realm's caravans pay in theirs. The absence of fees stimulates caravans to circulate in these fiefs, strengthening mercantilism, prosperity and supply of different goods between both sides, while also diverging trade from other realms. A trade pact does not necessarily bring any revenue to lords. In fact, it may incur in some revenue loss due to the caravan fee exemptions.\n\n{POSSIBLE}")
                        .SetTextVariable("POSSIBLE", tradeHint)
                        .ToString()));

                    if (!tradePossible)
                    {
                        if (diplomacy.HasTradePact(enemyKingdom))
                        {
                            Action<KingdomDiplomacy, Kingdom, KingdomDiplomacyVM> undoPact = ShowDissolveTradePact;
                            list.Add(new InquiryElement(makePact,
                                new TextObject("{=!}Undo Trade Pact").ToString(),
                                null,
                                playerRuler,
                                new TextObject("{=!}Dissolve the trade access between your realm and the {KINGDOM}. This will discourage trade between both realms and make lords more likely to accept wars between them. {LEADER} may be displeased with this choice.")
                                .SetTextVariable("KINGDOM", enemyKingdom.Name)
                                .SetTextVariable("LEADER", enemyKingdom.RulingClan.Leader.Name)
                                .ToString()));
                        }
                    }

                    MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                        new TextObject("{=7OCs6wMk}Diplomatic Action").ToString(),
                        new TextObject("{=aAOEkjCF}A diplomatic action significantly changes the relationship between your realm and the target realm.").ToString(),
                        list,
                        true,
                        1,
                        1,
                        GameTexts.FindText("str_accept").ToString(),
                        GameTexts.FindText("str_selection_widget_cancel").ToString(),
                        (List<InquiryElement> list) =>
                        {
                            Action<KingdomDiplomacy, Kingdom, KingdomDiplomacyVM> action = (Action<KingdomDiplomacy, Kingdom, KingdomDiplomacyVM>)
                            list[0].Identifier;
                            action.Invoke(diplomacy, enemyKingdom, __instance);
                        },
                        null));
                }

                return false;
            }

            private static void ShowAlliance(KingdomDiplomacy diplomacy, Kingdom newAlly, KingdomDiplomacyVM __instance)
            {
                // Route through vanilla's StartAllianceDecision pipeline.
                // BK's BKDiplomacyBehavior.MakeAlliance was a no-op stub
                // (FactionManager.DeclareAlliance was removed in 1.3.x and
                // BK never replaced it). Vanilla 1.3.x added a complete
                // alliance system (AllianceCampaignBehavior, AllianceModel,
                // StartAllianceDecision, 84-day max alliance duration with
                // expiration tracking). Add the decision to the player
                // kingdom's queue so vanilla's election + apply chain
                // handles the rest.
                InformationManager.ShowInquiry(new InquiryData(new TextObject("{=cG3R7J1D}Propose Alliance").ToString(),
                    new TextObject("{=kqy4fUCu}{LEADER} is interested in accepting an alliance between your rulerships. The proposal will be put to a vote among your kingdom's lords.")
                    .SetTextVariable("LEADER", newAlly.RulingClan.Leader.Name)
                    .ToString(),
                    true,
                    true,
                    GameTexts.FindText("str_policy_propose").ToString(),
                    GameTexts.FindText("str_selection_widget_cancel").ToString(),
                    () =>
                    {
                        try
                        {
                            // 1.4 removed StartAllianceDecision.GetProposerClanFor
                            // PlayerKingdom. The player initiates this from their
                            // own diplomacy screen, so the player clan proposes.
                            var proposer = Clan.PlayerClan;
                            if (proposer != null)
                            {
                                Clan.PlayerClan.Kingdom.AddDecision(
                                    new TaleWorlds.CampaignSystem.Election.StartAllianceDecision(proposer, newAlly),
                                    ignoreInfluenceCost: false);
                            }
                            // proposer == null: vanilla refused to nominate a
                            // sponsor (no eligible non-ruler clan, etc.).
                            // Inquiry already closed; surfacing an inline
                            // error here would interleave dialogs. Player
                            // can re-attempt; the UI gate stays trustworthy.
                        }
                        catch
                        {
                            // Defensive: vanilla's decision ctor rejects
                            // already-allied / same-kingdom edge cases.
                        }
                        __instance.RefreshValues();
                    },
                    null));
            }

            private static void ShowTruce(KingdomDiplomacy diplomacy, Kingdom enemyKingdom, KingdomDiplomacyVM __instance)
            {
                int denars = MBRandom.RoundRandomized(BannerKingsConfig.Instance.DiplomacyModel.GetTruceDenarCost(diplomacy.Kingdom,
                    enemyKingdom)
                    .ResultNumber);

                InformationManager.ShowInquiry(new InquiryData(new TextObject("{=oQ9z60ex}Propose Truce").ToString(),
                    new TextObject("{=5J6SrvjG}{LEADER} is interested in accepting a truce proposal of 3 years. In order to formalize it, they request {DENARS}{GOLD_ICON}.")
                    .SetTextVariable("DENARS", denars)
                    .SetTextVariable("LEADER", enemyKingdom.RulingClan.Leader.Name)
                    .ToString(),
                    Hero.MainHero.Gold >= denars,
                    true,
                    GameTexts.FindText("str_policy_propose").ToString(),
                    GameTexts.FindText("str_selection_widget_cancel").ToString(),
                    () =>
                    {
                        TaleWorlds.CampaignSystem.Campaign.Current.GetCampaignBehavior<BKDiplomacyBehavior>().MakeTruce(diplomacy.Kingdom, enemyKingdom, 3f);
                        __instance.RefreshValues();
                    },
                    null));
            }

            private static void ShowTradePact(KingdomDiplomacy diplomacy, Kingdom enemyKingdom, KingdomDiplomacyVM __instance)
            {
                int influence = MBRandom.RoundRandomized(BannerKingsConfig.Instance.DiplomacyModel.GetTradePactInfluenceCost(diplomacy.Kingdom,
                    enemyKingdom)
                    .ResultNumber);

                InformationManager.ShowInquiry(new InquiryData(new TextObject("{=BSDCg6uz}Propose Trade Access").ToString(),
                    new TextObject("{=W8HQ7SFG}{LEADER} is interested in accepting a trade pact that provides bilateral access indefinitely. Trading caravans will be allowed access to fiefs without paying tariffs, diverging trade from enemies or competitors while strengthening trade between both realms, likely increasing consumption satisfactions and consequently, overall prosperity. Pressing this proposal would cost {INFLUENCE} influence due to all the Peers within your realm that may be affected due to tariffs loss.\n Sustaining trade access pacts will each also reduce your family's influence cap. Trade pacts faciliate making truces and take effect for an indefinite amount of time so long peace between both sides is upheld.")
                    .SetTextVariable("INFLUENCE", influence)
                    .SetTextVariable("LEADER", enemyKingdom.RulingClan.Leader.Name)
                    .ToString(),
                    Clan.PlayerClan.Influence >= influence,
                    true,
                    GameTexts.FindText("str_policy_propose").ToString(),
                    GameTexts.FindText("str_selection_widget_cancel").ToString(),
                    () =>
                    {
                        TaleWorlds.CampaignSystem.Campaign.Current.GetCampaignBehavior<BKDiplomacyBehavior>().MakeTradePact(diplomacy.Kingdom, enemyKingdom);
                        __instance.RefreshValues();
                    },
                    null));
            }

            private static void ShowDissolveTradePact(KingdomDiplomacy diplomacy, Kingdom enemyKingdom, KingdomDiplomacyVM __instance)
            {
                InformationManager.ShowInquiry(new InquiryData(new TextObject("{=!}Undo Trade Access").ToString(),
                    new TextObject("{=!}Dissolve the trade access between your realm and the {KINGDOM}. This will discourage trade between both realms and make lords more likely to accept wars between them. {LEADER} may be displeased with this choice.")
                    .SetTextVariable("KINGDOM", enemyKingdom.Name)
                    .SetTextVariable("LEADER", enemyKingdom.RulingClan.Leader.Name)
                    .ToString(),
                    true,
                    true,
                    GameTexts.FindText("str_policy_propose").ToString(),
                    GameTexts.FindText("str_selection_widget_cancel").ToString(),
                    () =>
                    {
                        diplomacy.DissolveTradePactForcefully(enemyKingdom);
                        __instance.RefreshValues();
                    },
                    null));
            }

            private static void ShowWarOptions(KingdomDiplomacy diplomacy, Kingdom enemyKingdom, KingdomDiplomacyVM __instance)
            {
                var list = new List<InquiryElement>();
                float influenceCost = BannerKingsConfig.Instance.DiplomacyModel.GetInfluenceCostOfProposingWar(Clan.PlayerClan);
                bool enabled = Clan.PlayerClan.Influence >= influenceCost;

                Religion religion = BannerKingsConfig.Instance.ReligionsManager.GetHeroReligion(Hero.MainHero);
                foreach (var casusBelli in diplomacy.GetAvailableCasusBelli(enemyKingdom))
                {
                    float support = new KingdomElection(new BKDeclareWarDecision(casusBelli,
                        Clan.PlayerClan,
                        enemyKingdom)).GetLikelihoodForSponsor(Clan.PlayerClan);

                    bool isReligious = religion != null && religion.Faith.WarDoctrine.AcceptsJustification(casusBelli);
                    TextObject piety = isReligious ? new TextObject("{=!}{PIETY}{PIETY_ICON}")
                        .SetTextVariable("PIETY", religion.Faith.WarDoctrine.GetPietyCost(casusBelli))
                        .SetTextVariable("PIETY_ICON", TextHelper.PIETY_ICON)
                        :
                        TextObject.GetEmpty();

                    if (enabled && isReligious)
                        enabled = religion.Faith.WarDoctrine.HeroHasPiety(Hero.MainHero, casusBelli);

                    list.Add(new InquiryElement(casusBelli,
                    new TextObject("{=!}{NAME} - {INFLUENCE}{INFLUENCE_ICON} {PIETY} ({CHANCE}% approval)")
                    .SetTextVariable("INFLUENCE", MBRandom.RoundRandomized(influenceCost))
                    .SetTextVariable("INFLUENCE_ICON", TextHelper.INFLUENCE_ICON)
                    .SetTextVariable("PIETY", piety)
                    .SetTextVariable("NAME", casusBelli.QueryNameText)
                    .SetTextVariable("CHANCE", (support * 100).ToString("0.00")).ToString(),
                    null,
                    enabled,
                    casusBelli.GetDescriptionWithModifers().ToString()));
                }

                list.Add(new InquiryElement(null, new TextObject("{=mFVyMjXz}No Casus Belli").ToString(), null, enabled, null));
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    new TextObject("{=Fs2NR9Os}Casus Belli").ToString(),
                    new TextObject("{=fLc7tO0n}Select a justification for war.").ToString(),
                    list,
                    true,
                    1,
                    1,
                    GameTexts.FindText("str_accept").ToString(),
                    GameTexts.FindText("str_selection_widget_cancel").ToString(),
                    (List<InquiryElement> list) =>
                    {
                        object identifier = list[0].Identifier;
                        if (identifier != null)
                        {
                            CasusBelli casusBelli = (CasusBelli)identifier;
                            var decision = new BKDeclareWarDecision(casusBelli, Clan.PlayerClan, enemyKingdom);
                            Clan.PlayerClan.Kingdom.AddDecision(decision, false);
                        }
                        else
                        {
                            DeclareWarDecision declareWarDecision = new DeclareWarDecision(Clan.PlayerClan, enemyKingdom);
                            Clan.PlayerClan.Kingdom.AddDecision(declareWarDecision, false);
                        }
                        __instance.RefreshValues();
                    },
                    null));
            }
        }

        // v1.9.10.2 — Peace-vote / war-support UI disconnect fix.
        //
        // BK's "war support %" UI (CalculateWarSupport above) simulates
        // a fresh BKDeclareWarDecision and reports the % chance the
        // kingdom would vote FOR war. When that number is 0, the player
        // sees "nobody wants this war" and pushes a peace proposal. The
        // proposal queues fine. Then the actual vote runs on vanilla
        // MakePeaceKingdomDecision.DetermineSupport which has no idea
        // about BK's war fatigue, war score, or CB-expiry signals — it
        // scores peace on pure vanilla heuristics (kingdom strength,
        // fief threat). Result: every clan votes "stay at war", peace
        // fails, players see "war support 0% but everyone voted no" and
        // report forever wars.
        //
        // Mirror BK's signal into the actual vote. Loss factor combines
        // war fatigue (KingdomDiplomacy.Fatigue, 0..1) and BK war score
        // (negative = losing) into an additive push that ramps with
        // both. Symmetric: the WINNING side keeps voting against peace
        // under vanilla math (loss clamped at 0), which is intended —
        // only the losing kingdom gets nudged toward peace.
        [HarmonyPatch(typeof(MakePeaceKingdomDecision))]
        internal class MakePeaceKingdomDecisionPatches
        {
            [HarmonyPostfix]
            [HarmonyPatch("DetermineSupport", MethodType.Normal)]
            private static void DetermineSupportPostfix(MakePeaceKingdomDecision __instance,
                Clan clan, DecisionOutcome possibleOutcome, ref float __result)
            {
                if (ModCompat.DiplomacyMod) return;
                if (clan?.Kingdom == null || __instance == null) return;
                var outcome = possibleOutcome as MakePeaceKingdomDecision.MakePeaceDecisionOutcome;
                if (outcome == null) return;

                var bk = TaleWorlds.CampaignSystem.Campaign.Current
                    .GetCampaignBehavior<BKDiplomacyBehavior>();
                if (bk == null) return;

                War war = bk.GetWar(clan.Kingdom, __instance.FactionToMakePeaceWith);
                if (war == null) return;

                KingdomDiplomacy diplo = bk.GetKingdomDiplomacy(clan.Kingdom);
                if (diplo == null) return;

                float fatigue = diplo.Fatigue;
                float score;
                try { score = war.CalculateWarScore(clan.Kingdom, false).ResultNumber; }
                catch { return; }

                // Loss has two terms:
                //   exhaustion: pure fatigue past 0.5 — handles stalemates
                //     where two evenly-matched kingdoms grind without a
                //     decisive war score; both sides accumulate fatigue,
                //     both sides get a mild push toward peace.
                //   defeat:     -score past -0.3 — handles decisive losers
                //     with a strong push proportional to how badly they're
                //     losing.
                // Mirrors the two-condition proposer gate in
                // ForceProposePeaceFromLosingSide: anything that queues a
                // peace proposal also gets at least a mild voter push.
                float exhaustion = MathF.Max(0f, fatigue - 0.5f);
                float defeat = MathF.Max(0f, -score - 0.3f);
                float loss = exhaustion + defeat;
                if (loss <= 0f) return;

                // v1.9.10.7 — user reports votes still not carrying
                // ("every day every war is put to vote, none ending in
                // peace"). Old `80f * loss` gave stalemate (loss≈0.2)
                // only push ≈ 16, swing 32 — easily dominated by
                // vanilla's strong "stay at war" merit. Two-term curve:
                // flat 40 base once loss > 0 (any qualifying war gets a
                // meaningful nudge) plus 80*loss ramp (decisive losers
                // get the strong push). Capped at 120 so we never
                // single-handedly override every other vote signal.
                float push = MathF.Min(120f, 40f + 80f * loss);
                __result += outcome.ShouldPeaceBeDeclared ? push : -push;
            }
        }
    }
}
