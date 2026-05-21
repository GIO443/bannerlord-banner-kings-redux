using BannerKings.Behaviours;
using BannerKings.Behaviours.Criminality;
using BannerKings.CampaignContent.Economy.Layered;
using BannerKings.Behaviours.Raids;
using BannerKings.Behaviours.Diplomacy;
using BannerKings.Behaviours.Feasts;
using BannerKings.Behaviours.Marriage;
using BannerKings.Behaviours.PartyNeeds;
using BannerKings.Behaviours.Retainer;
using BannerKings.Behaviours.Mercenary;
using BannerKings.Behaviours.Workshops;
using BannerKings.Managers.Buildings;
using BannerKings.Managers.Innovations;
using BannerKings.Managers.Kingdoms.Policies;
using BannerKings.Managers.Skills;
using BannerKings.Models.Vanilla;
using BannerKings.Settings;
using BannerKings.UI;
using BannerKings.Utils;
using Bannerlord.UIExtenderEx;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using BannerKings.Managers.Innovations.Eras;
using BannerKings.Behaviours.Innovations;
using BannerKings.Behaviours.Shipping;
using BannerKings.Behaviours.Relations;

namespace BannerKings
{
    public class Main : MBSubModuleBase
    {
        private static readonly UIExtender Xtender = new(typeof(Main).Namespace!);

        protected override void OnGameStart(Game game, IGameStarter gameStarter)
        {
            base.OnGameStart(game, gameStarter);
            if (gameStarter is not CampaignGameStarter campaignStarter)
            {
                return;
            }

            // PatchAll defers to here. By OnGameStart, Game.Current is set and
            // GameTexts is fully initialized, so vanilla cctors triggered as a
            // side effect of patching (DefaultClanFinanceModel, CampaignUIHelper,
            // etc. — they call Game.Current.GameTextManager.FindText) complete
            // cleanly. Patching at OnSubModuleLoad or OnBeforeInitialModuleScreenSetAsRoot
            // hits null Game.Current and permanently breaks those types via
            // TypeInitializationException. The _patchesInstalled flag ensures we
            // only do this once per AppDomain even if OnGameStart fires multiple
            // times (player exits to main menu and starts another campaign).
            if (!_patchesInstalled)
            {
                _patchesInstalled = true;
                // Per-class patching with isolation: if one [HarmonyPatch]-decorated
                // type fails (e.g. ambiguous method match because vanilla has
                // multiple overloads, missing target method on a 1.3.x signature
                // change, etc.), it logs and the remaining patches still apply.
                // PatchAll() throws on the first failure and aborts every other
                // patch — that's how v1.5.4.0/4.1 took out the whole mod for one
                // dead-code postfix.
                var harmony = new Harmony("BannerKings");
                foreach (var t in typeof(Main).Assembly.GetTypes())
                {
                    try
                    {
                        harmony.CreateClassProcessor(t).Patch();
                    }
                    catch (System.Exception ex)
                    {
                        TaleWorlds.Library.Debug.Print(
                            $"[BK] Skipped Harmony patches on {t.FullName}: {ex.GetType().Name}: {ex.Message}",
                            color: TaleWorlds.Library.Debug.DebugColor.Yellow);
                    }
                }
                Xtender.Register(typeof(Main).Assembly);
                Xtender.Enable();

                // Install reflective trace on every CampaignBehaviorBase
                // OnDailyTickParty / DailyTickParty / OnPartyDailyTick(MobileParty)
                // method outside BK's own assembly. Diagnoses freezes that sit
                // in vanilla / NavalDLC / other-mod per-party tick subscribers.
                try { BannerKings.Patches.Diag.TraceVanillaDailyTickParty.Install(harmony); }
                catch (System.Exception ex)
                {
                    TaleWorlds.Library.Debug.Print(
                        $"[BK] TraceVanillaDailyTickParty install failed: {ex.GetType().Name}: {ex.Message}",
                        color: TaleWorlds.Library.Debug.DebugColor.Yellow);
                }
            }

            // Register the BK icon placeholders globally so any TextObject in BK
            // strings ("...{GOLD}{GOLD_ICON}...") renders the icon instead of
            // the literal "{GOLD_ICON}" placeholder. Many BK consumers expect
            // these set globally; previously only a couple of dialog paths did
            // it inline, which left building-completion messages, gentry-buy
            // offers, mercenary contracts, smithy fees, military-aid duty
            // payouts, council-position swap requests, merge-army inquiry list
            // entries, etc. showing the raw placeholder.
            GameTexts.SetVariable("GOLD_ICON", BannerKings.Utils.TextHelper.GOLD_ICON);
            GameTexts.SetVariable("INFLUENCE_ICON", BannerKings.Utils.TextHelper.INFLUENCE_ICON);
            GameTexts.SetVariable("PIETY_ICON", BannerKings.Utils.TextHelper.PIETY_ICON);
            GameTexts.SetVariable("MORALE_ICON", BannerKings.Utils.TextHelper.MORALE_ICON);
            GameTexts.SetVariable("FOOD_ICON", BannerKings.Utils.TextHelper.FOOD_ICON);
            GameTexts.SetVariable("PARTY_ICON", BannerKings.Utils.TextHelper.PARTY_ICON);
            GameTexts.SetVariable("SPEED_ICON", BannerKings.Utils.TextHelper.SPEED_ICON);

            campaignStarter.AddBehavior(new BKManagerBehavior());
            // Layered village/estate/town economy rework — Phase 1+4+6+8.
            campaignStarter.AddBehavior(new LayeredEconomyAssignmentBehavior());
            campaignStarter.AddBehavior(new ClusterFoodTracker());
            if (BannerKings.Utils.BKFeatureGates.EstatesEnabled)
                campaignStarter.AddBehavior(new EstatePolicyAI());
            campaignStarter.AddBehavior(new VillageDecreeManager());
            campaignStarter.AddBehavior(new BKEducationBehavior());
            campaignStarter.AddBehavior(new BKSettlementActions());
            campaignStarter.AddBehavior(new BKKnighthoodBehavior());
            campaignStarter.AddBehavior(new BKTournamentBehavior());
            campaignStarter.AddBehavior(new BKRepublicBehavior());
            campaignStarter.AddBehavior(new BKPartyBehavior());
            campaignStarter.AddBehavior(new BKClanBehavior());
            campaignStarter.AddBehavior(new BKArmyBehavior());
            campaignStarter.AddBehavior(new BKRansomBehavior());
            campaignStarter.AddBehavior(new BKTitleBehavior());
            campaignStarter.AddBehavior(new BKNotableBehavior());
            // Religion campaign behavior gated behind the MCM Religion
            // master toggle. When off, no preacher dialogue, no daily piety
            // tick, no faith inheritance / induction events fire. The
            // behavior holds all the religion event subscriptions; not
            // adding it is the cleanest way to make the system silent.
            if (BannerKings.Settings.BannerKingsSettings.Instance.EnableReligion)
            {
                campaignStarter.AddBehavior(new BKReligionsBehavior());
            }
            campaignStarter.AddBehavior(new BKSkillBehavior());
            campaignStarter.AddBehavior(new BKLordPropertyBehavior());
            campaignStarter.AddBehavior(new BKInnovationsBehavior());
            campaignStarter.AddBehavior(new BKLifestyleBehavior());
            campaignStarter.AddBehavior(new BKCampaignStartBehavior());
            campaignStarter.AddBehavior(new BKGoalBehavior());
            campaignStarter.AddBehavior(new BKBuildingsBehavior());
            campaignStarter.AddBehavior(new BKGovernorBehavior());
            campaignStarter.AddBehavior(new BKTradeGoodsFixesBehavior());
            campaignStarter.AddBehavior(new BKCapitalBehavior());
            campaignStarter.AddBehavior(new BKMarriageBehavior());
            campaignStarter.AddBehavior(new BKRetainerBehavior());
            // Legacy estate retinue behavior is part of the paused estate
            // loop. Its implementation files (BKEstateRetinueBehavior /
            // BKEstateRetinueModel) were never committed in v1.6.x, so the
            // registration line that used to reference them broke every
            // release CI from v1.7.0.0 through v1.8.8.0. Removing the
            // registration entirely; estates are in spirit only pending the
            // estate-as-village-workshop redesign and the retinue mechanic
            // will be revisited then.
            campaignStarter.AddBehavior(new BKFeastBehavior());

            // BK's workshop system is retired — Bannerlord Living Economy owns
            // workshop production (WorkshopProductionPatch). BKWorkshopBehavior
            // is no longer registered; BKWorkshopModel is no longer added as a
            // game model (below).
            campaignStarter.AddBehavior(new BKGentryBehavior());
            campaignStarter.AddBehavior(new BKBanditBehavior());
            campaignStarter.AddBehavior(new BKDiplomacyBehavior());
            campaignStarter.AddBehavior(new BKImperialLoyaltyBehavior());
            campaignStarter.AddBehavior(new BKRepublicLegislationBehavior());
            campaignStarter.AddBehavior(new BKCriminalityBehavior());
            campaignStarter.AddBehavior(new BKTraitBehavior());
            campaignStarter.AddBehavior(new BKPartyNeedsBehavior());
            campaignStarter.AddBehavior(new BKShippingBehavior());
            campaignStarter.AddBehavior(new BannerKings.Behaviours.Shipping.BKLordShippingBehavior());
            campaignStarter.AddBehavior(new BKMercenaryCareerBehavior());
            campaignStarter.AddBehavior(new BKRelationsBehavior());
            campaignStarter.AddBehavior(new BKSettlementBehavior());
            // CaravanOrdersBehavior must register before BKCaravansBehavior so
            // the scorer/buy-filter hooks in BKCaravansBehavior can read order
            // state via CaravanOrdersBehavior.Instance during the same tick.
            campaignStarter.AddBehavior(new BannerKings.Behaviours.Caravans.CaravanOrdersBehavior());
            campaignStarter.AddBehavior(new BKCaravansBehavior());
            campaignStarter.AddBehavior(new BKMercenaryCompanyBehavior());
            campaignStarter.AddBehavior(new BKAIVisitSettlementBehavior());
            campaignStarter.AddBehavior(new BKRaidCaptureBehavior());
            if (BannerKings.Utils.BKFeatureGates.EstatesEnabled)
                campaignStarter.AddBehavior(new BKEstateIncomeBehavior());
            // Vassal-knight land grants on top of EOF's lord-lands system.
            // Only registered when EOF is loaded; without EOF the behavior has
            // no income source to redirect and the grant table sits unused.
            if (BannerKings.Utils.ModCompat.EconomyOverhaul)
            {
                campaignStarter.AddBehavior(new BannerKings.Behaviours.Estates.BKLandGrantBehavior());
                campaignStarter.AddBehavior(new BannerKings.Behaviours.Estates.BKVillageSupplyAutoBehavior());
                campaignStarter.AddBehavior(new BannerKings.Behaviours.Estates.BKLandsLaborBehavior());
                campaignStarter.AddBehavior(new BannerKings.Behaviours.Estates.BKAIKnighthoodBehavior());
            }
            campaignStarter.AddBehavior(new BannerKings.Behaviours.Diag.AiDecisionTraceBehavior());
            campaignStarter.AddBehavior(new BannerKings.Behaviours.Diag.RecruitmentAuditBehavior());
            campaignStarter.AddBehavior(new BannerKings.Behaviours.Diag.ArmyFormationAuditBehavior());
            //campaignStarter.RemoveBehavior(campaignStarter.CampaignBehaviors.First(x => x.GetType() == typeof(CaravansCampaignBehavior)));


            // Models registered as full GameModel replacements where BK genuinely
            // restructures the math. Pure-tweak overrides (single override that just
            // calls base + adds a small modifier) used to live alongside these but
            // are now Harmony Postfix patches in
            // BannerKings.Patches.VanillaModelTweakPatches — vanilla model runs and
            // BK adjustments show up in the ExplainedNumber tooltip alongside vanilla
            // factors.
            campaignStarter.AddModel(new BKPrisonerModel());
            campaignStarter.AddModel(BannerKingsConfig.Instance.CompanionModel);
            // Economy Overhaul Framework hard-replaces Prosperity, Loyalty and
            // SettlementFood — its model subclasses don't take a baseModel ref and
            // call base.* (vanilla), bypassing BK's overrides entirely. Skip
            // registering BK's competing models when EOF is loaded so we don't
            // pay the perf cost of a model whose output gets discarded. Other
            // BK economy models (PriceFactor, ClanFinance, Construction, Tax,
            // Economy, VillageProduction) stay registered — EOF wraps + delegates
            // those, so BK's logic survives stacked underneath.
            // Bannerlord Living Economy (compat=0) owns the Prosperity,
            // VillageProduction, SettlementEconomy and item-price model slots.
            // BK yields all four so the two mods don't double-register a slot.
            if (!ModCompat.EconomyOverhaul && !ModCompat.BetterEconomy)
                campaignStarter.AddModel(BannerKingsConfig.Instance.ProsperityModel);
            campaignStarter.AddModel(BannerKingsConfig.Instance.TaxModel);
            if (!ModCompat.EconomyOverhaul)
                campaignStarter.AddModel(new BKFoodModel());
            campaignStarter.AddModel(BannerKingsConfig.Instance.ConstructionModel);
            campaignStarter.AddModel(new BKMilitiaModel());
            // Defer to AI Influence (AI Diplomacy) when present — that mod owns the
            // vanilla InfluenceModel slot. BK's internal queries (caps, costs) still
            // resolve via BannerKingsConfig.Instance.InfluenceModel directly, so the
            // config-level instance is intentionally not unregistered.
            if (!ModCompat.AIInfluence)
                campaignStarter.AddModel(BannerKingsConfig.Instance.InfluenceModel);
            if (!ModCompat.EconomyOverhaul)
                campaignStarter.AddModel(new BKLoyaltyModel());
            if (!ModCompat.BetterEconomy)
                campaignStarter.AddModel(BannerKingsConfig.Instance.VillageProductionModel);
            if (!ModCompat.BetterEconomy)
                campaignStarter.AddModel(BannerKingsConfig.Instance.EconomyModel);
            if (!ModCompat.BetterEconomy)
                campaignStarter.AddModel(new BKPriceFactorModel());
            // BK's workshop system is retired — Bannerlord Living Economy owns
            // workshops. BKWorkshopModel is no longer registered; the vanilla /
            // BetterEconomy workshop model holds the slot.
            campaignStarter.AddModel(BannerKingsConfig.Instance.ClanFinanceModel);
            campaignStarter.AddModel(BannerKingsConfig.Instance.ArmyManagementModel);
            campaignStarter.AddModel(BannerKingsConfig.Instance.VolunteerModel);
            // Defer garrison sizing to ImprovedGarrisons when present.
            if (!ModCompat.ImprovedGarrisons)
                campaignStarter.AddModel(new BKGarrisonModel());
            campaignStarter.AddModel(new BKPartyWageModel());
            // BK's smithing overhaul (smelting yield caps, custom stamina costs,
            // armor crafting UI mode, botching, hourly smithing fee) is opt-out
            // via the MCM "BK Smithing System" toggle. When disabled, vanilla
            // DefaultSmithingModel runs unmodified — players who only want the
            // rest of BK without the crafting changes can flip this off.
            if (BannerKingsSettings.Instance.BKSmithingEnabled)
                campaignStarter.AddModel(BannerKingsConfig.Instance.SmithingModel);
            campaignStarter.AddModel(new BKPartyConsumptionModel());
            campaignStarter.AddModel(BannerKingsConfig.Instance.LearningModel);
            campaignStarter.AddModel(BannerKingsConfig.Instance.KingdomDecisionModel);
            campaignStarter.AddModel(new BKPartyTrainningModel());
            // Defer to Diplomacy mod when present — it owns kingdom-decision tuning.
            // BK's internal pacts/casus belli still calculate via the BK config-level
            // model (BKDiplomacyModel instance held by BannerKingsConfig).
            if (!ModCompat.DiplomacyMod)
                campaignStarter.AddModel(new BKDiplomacyModel());
            campaignStarter.AddModel(new BKPartyBuyingFoodModel());
            // Defer to MarryAnyone when present — it removes vanilla restrictions.
            // BK still consults the config-level model for title/dowry logic.
            if (!ModCompat.MarryAnyone)
                campaignStarter.AddModel(BannerKingsConfig.Instance.MarriageModel);
            // BKTargetScoreModel was deleted in v1.5.5.0 — its overrides are postfixes
            // in VanillaModelTweakPatches now (RaidingFactor property included).

            BKAttributes.Instance.Initialize();
            BKSkills.Instance.Initialize();
            BKPerks.Instance.Initialize();   
            BKPolicies.Instance.Initialize();
            DefaultEras.Instance.Initialize();
            DefaultInnovations.Instance.Initialize();
            BKBuildings.Instance.Initialize();

            DefaultMercenaryPrivileges.Instance.Initialize();
            DefaultCustomTroopPresets.Instance.Initialize();

            UIManager.Instance.SetScreen(new BannerKingsScreen());
            //TaleWorlds.CampaignSystem.Campaign.Current.TournamentManager = new BKTournamentManager();
        }

        private bool _patchesInstalled;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            BKDiagnostics.Install();

            // Bannerlord Living Economy owns population + economy in this build.
            // Flip its BannerKings-compatibility mode to 0 so it runs its full
            // simulation (its own population, prosperity, village production,
            // item prices) instead of deferring to BK. BK is ordered to load
            // after BetterEconomy, so this set lands after its SettingsLoader
            // and wins. (Its runtime settings-reload hotkey can revert this —
            // acceptable for now; see the BetterEconomy integration TODO.)
            try
            {
                BetterEconomy.Config.BetterEconomySettings.BannerKingsCompatibilityMode = 0;
            }
            catch
            {
            }

            // ContainerLoadData.FillObject Finalizer for the religion dup-key
            // backstop. Installs first so it's covering the very first save
            // load. The internal target type is resolved reflectively via
            // AccessTools.TypeByName.
            try
            {
                BannerKings.Patches.ContainerLoadDataDupKeyBackstop.Install(
                    new Harmony("BannerKings.ContainerLoadDataDupKey"));
            }
            catch
            {
            }

            // GameTexts null-guard installs here so it's the first thing in place.
            // Cheap and self-contained; doesn't trigger vanilla cctors.
            try
            {
                var harmony = new Harmony("BannerKings");
                var target = BannerKings.Patches.GameTextsNullGuardPatch.TargetMethod();
                var prefix = AccessTools.Method(
                    typeof(BannerKings.Patches.GameTextsNullGuardPatch),
                    nameof(BannerKings.Patches.GameTextsNullGuardPatch.Prefix));
                if (target != null && prefix != null)
                {
                    harmony.Patch(target, prefix: new HarmonyMethod(prefix));
                }
            }
            catch
            {
            }

            // SettlementTax null-guard — silences the 1.3.x NRE storm in
            // DefaultSettlementTaxModel.GetVillageTaxRatio that fires hundreds
            // of times per hourly tick during heavy caravan/raid activity.
            // Same install pattern as the GameTexts guard.
            try
            {
                var harmony = new Harmony("BannerKings.SettlementTax");
                var target = BannerKings.Patches.SettlementTaxModelNullGuardPatch.TargetMethod();
                var prefix = AccessTools.Method(
                    typeof(BannerKings.Patches.SettlementTaxModelNullGuardPatch),
                    nameof(BannerKings.Patches.SettlementTaxModelNullGuardPatch.Prefix));
                if (target != null && prefix != null)
                {
                    harmony.Patch(target, prefix: new HarmonyMethod(prefix));
                }
            }
            catch
            {
            }

            // DelayedTeleportation null-guard — vanilla NREs on
            // teleportingHero.Clan.HasNavalNavigationCapability when a clanless
            // hero is passed to the governor-pick tooltip. Crashes "Replace
            // Governor" on hover for affected heroes. Same install pattern.
            try
            {
                var harmony = new Harmony("BannerKings.DelayedTeleportation");
                var target = BannerKings.Patches.DelayedTeleportationModelNullGuardPatch.TargetMethod();
                var prefix = AccessTools.Method(
                    typeof(BannerKings.Patches.DelayedTeleportationModelNullGuardPatch),
                    nameof(BannerKings.Patches.DelayedTeleportationModelNullGuardPatch.Prefix));
                if (target != null && prefix != null)
                {
                    harmony.Patch(target, prefix: new HarmonyMethod(prefix));
                }
            }
            catch
            {
            }

            // PatchAll is DEFERRED to OnBeforeInitialModuleScreenSetAsRoot.
            // Reason: PatchAll triggers static cctor of every patched vanilla type.
            // In 1.3.x several of those cctors (DefaultClanFinanceModel, CampaignUIHelper,
            // etc.) call Game.Current.GameTextManager.FindText(...) — but Game.Current
            // is still null at OnSubModuleLoad time, so the callvirt NREs. Once a
            // cctor throws, the type is permanently broken (TypeInitializationException
            // forever). Deferring until OnBeforeInitialModuleScreenSetAsRoot ensures
            // Game.Current and GameTexts are both initialized before any cctor fires.
        }


        public override void OnGameEnd(Game game)
        {
            base.OnGameEnd(game);
            if (UIManager.Instance.BKScreen != null)
            {
                UIManager.Instance.BKScreen.OnFinalize();
            }
        }
    }
}