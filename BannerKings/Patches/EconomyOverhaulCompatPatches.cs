using System;
using System.Reflection;
using BannerKings.Behaviours.Estates;
using BannerKings.Managers.Titles.Laws;
using BannerKings.Utils;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BannerKings.Patches
{
    /// <summary>
    /// Harmony patches that bridge BK's labor / population layer onto Economy
    /// Overhaul Framework's capital / hearth-driven economy.
    ///
    /// Conceptual frame: vanilla Hearth = village development (capital), BK
    /// Population = labor / demographics. EOF uses Hearth alone for per-item
    /// village production; we overlay a small estate-workforce factor so
    /// labor abundance / scarcity nudges output ±20% without breaking EOF
    /// balance. See docs/wiki/Player-Guide.md for the player-facing framing.
    ///
    /// Targets are resolved reflectively via AccessTools.TypeByName so BK
    /// builds without referencing the EOF assembly. Each Prepare() returns
    /// false when EOF isn't installed, so the patches are no-ops in that
    /// configuration.
    /// </summary>
    internal static class EconomyOverhaulCompatPatches
    {
        // EOF's BLM_Function has a fragile static initializer that reads
        // DefaultItemCategories.Grain (and 10 others) at type-load time.
        // DefaultItemCategories.X resolves to Game.Current.DefaultItemCategories.
        // _itemCategoryX; if that singleton or any backing field is null when
        // BLM_Function's cctor runs, the cctor throws NRE and the type is
        // permanently poisoned for the AppDomain — every later EOF call paths
        // (Town.FoodChange, village production, etc.) throws
        // TypeInitializationException. Observed in v1.8.10.6 cctor probe.
        //
        // These finalizers wrap EOF's two highest-traffic external entry
        // points and swallow TypeInitializationException, returning a
        // zero ExplainedNumber so the calling code (kingdom screen,
        // village production tooltip, etc.) gets a benign value instead of
        // propagating the exception. Other EOF code paths can still throw,
        // but the kingdom-screen-on-FoodChange crash/freeze loop is broken.
        //
        // Finalizer pattern: Harmony invokes finalizers AFTER prefix/postfix
        // with the thrown exception in __exception. Returning null clears the
        // exception and the result is whatever __result currently holds (or
        // the prefix-set default). For these two methods we set __result to
        // a zero ExplainedNumber and return null.
        [HarmonyPatch]
        internal static class BLM_TownFoodStocksChangeSafetyFinalizer
        {
            private static Type _modelType;

            private static bool Prepare()
            {
                if (!ModCompat.EconomyOverhaul) return false;
                _modelType = AccessTools.TypeByName(
                    "Bannerlord.Economy_Overhaul.Models.BLM_TownProductionModel");
                return _modelType != null
                    && AccessTools.Method(_modelType, "CalculateTownFoodStocksChange",
                        new[] { typeof(Town), typeof(bool), typeof(bool) }) != null;
            }

            private static MethodBase TargetMethod()
                => AccessTools.Method(_modelType, "CalculateTownFoodStocksChange",
                    new[] { typeof(Town), typeof(bool), typeof(bool) });

            private static System.Exception Finalizer(System.Exception __exception,
                ref ExplainedNumber __result)
            {
                if (__exception == null) return null;
                if (__exception is System.TypeInitializationException
                    || (__exception.InnerException is System.TypeInitializationException))
                {
                    __result = new ExplainedNumber(0f);
                    return null; // swallow
                }
                return __exception; // re-throw unknown failures
            }
        }

        [HarmonyPatch]
        internal static class BLM_VillageDailyProductionSafetyFinalizer
        {
            private static Type _modelType;

            private static bool Prepare()
            {
                if (!ModCompat.EconomyOverhaul) return false;
                _modelType = AccessTools.TypeByName(BLM_VillageProductionModelType);
                return _modelType != null
                    && AccessTools.Method(_modelType, "CalculateDailyProductionAmount",
                        new[] { typeof(Village), typeof(ItemObject) }) != null;
            }

            private static MethodBase TargetMethod()
                => AccessTools.Method(_modelType, "CalculateDailyProductionAmount",
                    new[] { typeof(Village), typeof(ItemObject) });

            private static System.Exception Finalizer(System.Exception __exception,
                ref ExplainedNumber __result)
            {
                if (__exception == null) return null;
                if (__exception is System.TypeInitializationException
                    || (__exception.InnerException is System.TypeInitializationException))
                {
                    __result = new ExplainedNumber(0f);
                    return null;
                }
                return __exception;
            }
        }

        // Crash-13 (2026-05-11): hard NRE in
        // BuildingsCampaignBehavior.DailyTickSettlement_Patch1 — i.e. the
        // vanilla building-progress daily tick *after* a Harmony patch was
        // applied by a cooperator mod (EOF on this user's load list). No
        // inner exception, source MonoMod.Utils — same family as the
        // BLM_Function cctor poisoning the sibling finalizers already cover,
        // but on a different patch site (this one isn't an ExplainedNumber
        // method, so the existing finalizers can't catch it).
        //
        // Add a defensive finalizer on vanilla's DailyTickSettlement that
        // swallows TypeInitializationException + NRE only when EOF is loaded.
        // Cost of swallowing: the affected settlement skips ONE day of
        // vanilla building progress. Cost of not swallowing: campaign-ending
        // crash every day-tick. Easy trade.
        [HarmonyPatch]
        internal static class VanillaBuildingsDailyTickSafetyFinalizer
        {
            private static Type _behType;

            private static bool Prepare()
            {
                // Gate on EOF: this is here purely because an EOF patch on
                // this method is throwing. Without EOF the surface doesn't
                // exist and the finalizer is wasted overhead.
                if (!ModCompat.EconomyOverhaul) return false;
                _behType = AccessTools.TypeByName(
                    "TaleWorlds.CampaignSystem.CampaignBehaviors.BuildingsCampaignBehavior");
                return _behType != null
                    && AccessTools.Method(_behType, "DailyTickSettlement",
                        new[] { typeof(Settlement) }) != null;
            }

            private static MethodBase TargetMethod()
                => AccessTools.Method(_behType, "DailyTickSettlement",
                    new[] { typeof(Settlement) });

            private static System.Exception Finalizer(System.Exception __exception)
            {
                if (__exception == null) return null;
                if (__exception is System.TypeInitializationException
                    || (__exception.InnerException is System.TypeInitializationException)
                    || __exception is System.NullReferenceException
                    || __exception.InnerException is System.NullReferenceException)
                {
                    return null; // swallow — daily building tick skipped for this settlement
                }
                return __exception;
            }
        }

        private const string BLM_VillageProductionModelType =
            "Bannerlord.Economy_Overhaul.Models.BLM_VillageProductionModel";

        private const string VillageAddonsBehaviorType =
            "Bannerlord.Economy_Overhaul.Behavior.VillageAddonsBehavior";

        private const string BLM_FarmFunctionType =
            "Bannerlord.Economy_Overhaul.BLM_FarmFunction";

        private const string BLM_PopulationFunctionType =
            "Bannerlord.Economy_Overhaul.BLM_PopulationFunction";

        // Adds a population-workforce utilization factor to EOF's per-item
        // village production. EOF derives per-item output from vanilla Hearth
        // alone; without this overlay, a player village with a large BK
        // labour pool produces the same as a barely-populated one of the
        // same hearth size, which players experience as EOF "undertuning"
        // every fief. Saturation 1.0 ≈ baseline, clamped to [0.75, 2.25]:
        // population scarcity drags output -25% at the floor, an abundant
        // workforce can lift output up to +125% above EOF's hearth baseline.
        // The widened ceiling lets labor-rich villages meaningfully recover
        // EOF's compounded animal/civilian category penalties (mountable
        // ×0.2 × animal ×0.3 × sheep ×0.3 stacks down to ~0.09× otherwise).
        //
        // Saturation source: per-estate average WorkforceSaturation when
        // estates exist, otherwise falls back to LandData.WorkforceSaturation
        // (settlement-wide labour pool vs farmland+pasture required-labour).
        // This means the boost still applies under EOF even though the BK
        // estate gameplay loop is currently paused there.
        [HarmonyPatch]
        internal static class BLM_VillageProductionEstateWorkforcePatch
        {
            private static Type _modelType;

            private static bool Prepare()
            {
                if (!ModCompat.EconomyOverhaul) return false;
                _modelType = AccessTools.TypeByName(BLM_VillageProductionModelType);
                return _modelType != null
                    && AccessTools.Method(_modelType, "CalculateDailyProductionAmount",
                        new[] { typeof(Village), typeof(ItemObject) }) != null;
            }

            private static MethodBase TargetMethod()
                => AccessTools.Method(_modelType, "CalculateDailyProductionAmount",
                    new[] { typeof(Village), typeof(ItemObject) });

            private static void Postfix(Village village, ItemObject item, ref ExplainedNumber __result)
            {
                if (village?.Settlement == null) return;
                if (__result.ResultNumber <= 0f) return;

                try
                {
                    float baseNum = __result.BaseNumber;
                    if (baseNum <= 0f) return;

                    // Flat BK-side baseline multiplier on EOF village output.
                    // Layered HERE (not by mutating EOF's MCM slider) so the
                    // user's manual EOF setting is left untouched and the two
                    // values stack predictably. Disabled when the multiplier
                    // is at 1.0 (no-op).
                    float bkMult = BannerKings.Settings.BannerKingsSettings.Instance.EofVillageProductionBoost;
                    if (bkMult > 1.0001f)
                    {
                        // ExplainedNumber math: to multiply ResultNumber by
                        // `bkMult`, AddFactor((bkMult-1) * ResultNumber/BaseNumber).
                        // See workforce-factor block below for derivation.
                        float scale0 = __result.ResultNumber / baseNum;
                        float baseFactor = (bkMult - 1f) * scale0;
                        __result.AddFactor(baseFactor,
                            new TextObject("{=BK_EofBaselineBoost}BK village production boost"));
                    }

                    var data = BannerKingsConfig.Instance?.PopulationManager?.GetPopData(village.Settlement);
                    if (data == null) return;

                    float saturation = float.NaN;

                    // Prefer estate-level saturation when the estate loop is
                    // active. When estates are paused under EOF the list will
                    // typically be empty, so fall through to LandData.
                    var estates = data.EstateData?.Estates;
                    if (estates != null && estates.Count > 0)
                    {
                        float sum = 0f;
                        int count = 0;
                        foreach (var estate in estates)
                        {
                            float sat;
                            try { sat = estate.WorkforceSaturation; }
                            catch { continue; }
                            if (float.IsNaN(sat) || float.IsInfinity(sat) || sat <= 0f) continue;
                            sum += sat;
                            count++;
                        }
                        if (count > 0) saturation = sum / count;
                    }

                    if (float.IsNaN(saturation))
                    {
                        // Settlement-wide fallback. LandData.WorkforceSaturation
                        // can divide by zero on freshly-spawned settlements
                        // before farmland/pasture is computed; guard that.
                        try
                        {
                            var land = data.LandData;
                            if (land != null)
                            {
                                float s = land.WorkforceSaturation;
                                if (!float.IsNaN(s) && !float.IsInfinity(s) && s > 0f)
                                    saturation = s;
                            }
                        }
                        catch { /* fall through to no-op */ }
                    }

                    if (float.IsNaN(saturation) || saturation <= 0f) return;

                    float clamped = MathF.Clamp(saturation, 0.75f, 2.25f);
                    if (MathF.Abs(clamped - 1f) < 0.001f) return;

                    // ExplainedNumber math: ResultNumber = BaseNumber * (1 + SumOfFactors).
                    // To multiply ResultNumber by `clamped`, the new sum-of-factors must
                    // become clamped * (1 + S0) - 1, so the delta we AddFactor is
                    // (clamped - 1) * (1 + S0) = (clamped - 1) * (ResultNumber / BaseNumber).
                    // EOF already pushes a large factor (~num2 - 1) into SumOfFactors before
                    // we run, so a naive AddFactor(clamped - 1) would deliver only a few
                    // percent instead of the intended (clamped - 1) × 100% lift.
                    // Read ResultNumber AFTER the baseline boost so the workforce
                    // factor stacks multiplicatively on top of it.
                    float currentScale = __result.ResultNumber / baseNum;
                    float factor = (clamped - 1f) * currentScale;
                    __result.AddFactor(factor, new TextObject("{=BK_PopulationWorkforce}BK population workforce"));
                }
                catch
                {
                    // Defensive: a BK lookup hiccup must never break village production.
                }
            }
        }

        // Softens EOF's sheep/fur double-penalty. EOF applies a generic
        // animal ×0.3 factor to all animal-category items inside
        // BLM_Function.CalculateVillageInternalProduction, then an additional
        // ×0.3 specifically for Sheep and Fur — compounding to 0.09× of the
        // raw factor. The intent is clearly "animals produce less than grain"
        // (the ×0.3 already does that); the second tap is excessive and
        // leaves pastoral primary-sheep villages producing <1 unit per day.
        // This postfix multiplies the result by 0.50/0.30 = 1.667× for sheep
        // and fur only, lifting the effective stack to 0.15× rather than 0.09×.
        // Generic animal balance (cow, hog, horses) is untouched.
        //
        // Target: BLM_VillageProductionModel.CalculateDailyProductionAmount —
        // NOT BLM_Function.CalculateVillageInternalProduction directly, even
        // though that's where the double-penalty originates. Patching
        // BLM_Function pulled its type loading into Harmony's PatchAll sweep
        // at OnGameStart, which fired the static cctor before
        // Game.Current.DefaultItemCategories was wired; cctor permanently
        // poisoned, every later EOF code path threw TypeInitializationException.
        // (See BK_eof_cctor_probe.txt diagnostic from v1.8.10.7 — get_Grain
        // NRE inside the ItemClamps HashSet builder at BLM_Function.cs:46.)
        // The wrapper model's CalculateDailyProductionAmount just returns
        // BLM_Function.CalculateVillageInternalProduction's result, so the
        // postfix runs at the same logical layer with the same __result and
        // item args — no behavior change vs the original patch — without
        // forcing BLM_Function to load.
        [HarmonyPatch]
        internal static class BLM_SheepFurDoublePenaltySoftener
        {
            private static Type _modelType;

            private static bool Prepare()
            {
                if (!ModCompat.EconomyOverhaul) return false;
                _modelType = AccessTools.TypeByName(BLM_VillageProductionModelType);
                return _modelType != null
                    && AccessTools.Method(_modelType, "CalculateDailyProductionAmount",
                        new[] { typeof(Village), typeof(ItemObject) }) != null;
            }

            private static MethodBase TargetMethod()
                => AccessTools.Method(_modelType, "CalculateDailyProductionAmount",
                    new[] { typeof(Village), typeof(ItemObject) });

            private static void Postfix(Village village, ItemObject item, ref ExplainedNumber __result)
            {
                if (item?.ItemCategory == null) return;
                if (__result.ResultNumber <= 0f) return;
                if (item.ItemCategory != DefaultItemCategories.Sheep
                    && item.ItemCategory != DefaultItemCategories.Fur) return;

                // Multiply ResultNumber by 0.50 / 0.30 = 1.6667 — restores EOF's
                // sheep/fur secondary factor from 0.30 to an effective 0.50.
                // Naive AddFactor(0.6667) would be additively-merged with EOF's
                // existing big factor in SumOfFactors and deliver only a few
                // percent; the correct delta is (mult - 1) × (ResultNumber / BaseNumber).
                const float mult = 1.6667f;
                float baseNum = __result.BaseNumber;
                if (baseNum <= 0f) return;
                float currentScale = __result.ResultNumber / baseNum;
                __result.AddFactor((mult - 1f) * currentScale,
                    new TextObject("{=BK_AnimalRecovery}BK animal recovery"));
            }
        }

        // -----------------------------------------------------------------------
        // Reflective bridge to EOF's VillageAddonsBehavior. Cached after first
        // use so per-tick calls don't re-resolve. All members no-op when EOF
        // isn't loaded.
        // -----------------------------------------------------------------------
        internal static class EofLandsBridge
        {
            private static Type _addonsBehaviorType;
            private static MethodInfo _getLordLandsOwnedMethod;
            private static Type _farmFunctionType;
            private static MethodInfo _calcLordProductionMethod;
            private static bool _resolved;

            private static void EnsureResolved()
            {
                if (_resolved) return;
                _resolved = true;
                if (!ModCompat.EconomyOverhaul) return;
                _addonsBehaviorType = AccessTools.TypeByName(VillageAddonsBehaviorType);
                if (_addonsBehaviorType != null)
                {
                    _getLordLandsOwnedMethod = AccessTools.Method(
                        _addonsBehaviorType, "GetLordLandsOwned", new[] { typeof(Village) });
                }
                _farmFunctionType = AccessTools.TypeByName(BLM_FarmFunctionType);
                if (_farmFunctionType != null)
                {
                    _calcLordProductionMethod = AccessTools.Method(
                        _farmFunctionType, "CalculateVillagePrimaryProductionForLord",
                        new[] { typeof(Village), typeof(ItemObject), typeof(int), typeof(int) });
                }
            }

            public static int GetLordLandsOwned(Village v)
            {
                if (v?.Settlement == null) return 0;
                EnsureResolved();
                if (_getLordLandsOwnedMethod == null) return 0;
                var beh = GetVillageAddonsBehavior();
                if (beh == null) return 0;
                try
                {
                    return (int)_getLordLandsOwnedMethod.Invoke(beh, new object[] { v });
                }
                catch { return 0; }
            }

            public static float CalculateLordProduction(Village v, ItemObject item, int lordLands, int factor)
            {
                if (v == null || item == null) return 0f;
                EnsureResolved();
                if (_calcLordProductionMethod == null) return 0f;
                try
                {
                    return (float)_calcLordProductionMethod.Invoke(null, new object[] { v, item, lordLands, factor });
                }
                catch { return 0f; }
            }

            // Warehouse access for the supply auto-refill behavior. EOF's
            // GetOrCreateWarehouseRoster is private; we reflect into it once
            // and cache the MethodInfo. HasWarehouse and the consumption
            // accessors are public on VillageAddonsBehavior.
            private static MethodInfo _hasWarehouseMethod;
            private static MethodInfo _getOrCreateWarehouseMethod;
            private static MethodInfo _getWeeklyToolsMethod;
            private static MethodInfo _getWeeklyHorsesMethod;

            public static bool HasWarehouse(Settlement s)
            {
                if (s == null) return false;
                EnsureResolved();
                if (_addonsBehaviorType == null) return false;
                if (_hasWarehouseMethod == null)
                    _hasWarehouseMethod = AccessTools.Method(_addonsBehaviorType, "HasWarehouse",
                        new[] { typeof(Settlement) });
                if (_hasWarehouseMethod == null) return false;
                var beh = GetVillageAddonsBehavior();
                if (beh == null) return false;
                try { return (bool)_hasWarehouseMethod.Invoke(beh, new object[] { s }); }
                catch { return false; }
            }

            public static TaleWorlds.CampaignSystem.Roster.ItemRoster GetWarehouseRoster(Settlement s)
            {
                if (s == null) return null;
                EnsureResolved();
                if (_addonsBehaviorType == null) return null;
                if (_getOrCreateWarehouseMethod == null)
                    _getOrCreateWarehouseMethod = AccessTools.Method(_addonsBehaviorType,
                        "GetOrCreateWarehouseRoster", new[] { typeof(Settlement) });
                if (_getOrCreateWarehouseMethod == null) return null;
                var beh = GetVillageAddonsBehavior();
                if (beh == null) return null;
                try
                {
                    return _getOrCreateWarehouseMethod.Invoke(beh, new object[] { s })
                        as TaleWorlds.CampaignSystem.Roster.ItemRoster;
                }
                catch { return null; }
            }

            public static int GetWeeklyToolsConsumption(Settlement s)
            {
                if (s == null) return 0;
                EnsureResolved();
                if (_addonsBehaviorType == null) return 0;
                if (_getWeeklyToolsMethod == null)
                    _getWeeklyToolsMethod = AccessTools.Method(_addonsBehaviorType,
                        "GetWeeklyToolsConsumption", new[] { typeof(Settlement) });
                if (_getWeeklyToolsMethod == null) return 0;
                var beh = GetVillageAddonsBehavior();
                if (beh == null) return 0;
                try { return (int)_getWeeklyToolsMethod.Invoke(beh, new object[] { s }); }
                catch { return 0; }
            }

            public static int GetWeeklyHorseConsumption(Settlement s)
            {
                if (s == null) return 0;
                EnsureResolved();
                if (_addonsBehaviorType == null) return 0;
                if (_getWeeklyHorsesMethod == null)
                    _getWeeklyHorsesMethod = AccessTools.Method(_addonsBehaviorType,
                        "GetWeeklyHorseConsumption", new[] { typeof(Settlement) });
                if (_getWeeklyHorsesMethod == null) return 0;
                var beh = GetVillageAddonsBehavior();
                if (beh == null) return 0;
                try { return (int)_getWeeklyHorsesMethod.Invoke(beh, new object[] { s }); }
                catch { return 0; }
            }

            private static object _cachedBehavior;
            private static Campaign _cachedCampaign;
            private static MethodInfo _getCampaignBehaviorGeneric;
            private static object GetVillageAddonsBehavior()
            {
                if (_addonsBehaviorType == null || Campaign.Current == null) return null;
                // Reference equality on Campaign.Current — GetHashCode is not
                // guaranteed unique across object lifetimes, so a new game
                // could collide with a stale cached behavior pointer.
                if (_cachedBehavior != null && ReferenceEquals(_cachedCampaign, Campaign.Current))
                    return _cachedBehavior;
                // Campaign.GetCampaignBehavior<T>() is generic; we know T only at
                // runtime so we resolve via MakeGenericMethod once and cache the
                // resulting closed instance via _cachedBehavior.
                if (_getCampaignBehaviorGeneric == null)
                {
                    _getCampaignBehaviorGeneric = typeof(Campaign).GetMethod(
                        "GetCampaignBehavior", BindingFlags.Public | BindingFlags.Instance,
                        null, Type.EmptyTypes, null);
                }
                if (_getCampaignBehaviorGeneric == null) return null;
                try
                {
                    var closed = _getCampaignBehaviorGeneric.MakeGenericMethod(_addonsBehaviorType);
                    var beh = closed.Invoke(Campaign.Current, null);
                    if (beh != null)
                    {
                        _cachedBehavior = beh;
                        _cachedCampaign = Campaign.Current;
                    }
                    return beh;
                }
                catch { return null; }
            }
        }

        // Redirects EOF's daily lord-lands payout to BK grantees, applying the
        // tenancy-law tax skim back to the liege. EOF's item-production side
        // effects (filling town/village inventory) run untouched — only gold
        // ownership changes here.
        //
        // Net per grantee per day:
        //   share = (granted/total) * EOF's full payout
        //   liege keeps share * taxRate, grantee gets share * (1 - taxRate)
        //
        // When the bound-town lord is the player, EOF's payout method skips the
        // gold step entirely (EOF only pays AI lords). In that case we still pay
        // the grantee but without subtracting from the player.
        [HarmonyPatch]
        internal static class CreateLordLandsProductPayoutRedirect
        {
            private static Type _addonsBehaviorType;

            private static bool Prepare()
            {
                if (!ModCompat.EconomyOverhaul) return false;
                _addonsBehaviorType = AccessTools.TypeByName(VillageAddonsBehaviorType);
                return _addonsBehaviorType != null
                    && AccessTools.Method(_addonsBehaviorType, "CreateLordLandsProductAndPayLord",
                        new[] { typeof(Village) }) != null;
            }

            private static MethodBase TargetMethod()
                => AccessTools.Method(_addonsBehaviorType, "CreateLordLandsProductAndPayLord",
                    new[] { typeof(Village) });

            private static void Postfix(Village v)
            {
                if (v?.Settlement == null) return;
                if (BKLandGrantBehavior.Instance == null) return;
                try
                {
                    var grantees = BKLandGrantBehavior.Instance.GetGranteesForVillage(v);
                    if (grantees.Count == 0) return;

                    int totalLordLands = EofLandsBridge.GetLordLandsOwned(v);
                    if (totalLordLands <= 0) return;

                    var productions = v.VillageType?.Productions;
                    if (productions == null || productions.Count == 0) return;
                    var item1 = productions[0].Item1;
                    var item2 = productions.Count > 1 ? productions[1].Item1 : null;
                    if (item1?.ItemCategory == null) return;
                    if (item2 != null && item2.ItemCategory == null) item2 = null;

                    // Recompute EOF's full daily payout from the lord-lands.
                    int prod1 = (int)EofLandsBridge.CalculateLordProduction(v, item1, totalLordLands, 8);
                    int prod2 = item2 != null
                        ? (int)EofLandsBridge.CalculateLordProduction(v, item2, totalLordLands, 4)
                        : 0;
                    int totalPayout = item1.Value * prod1 + (item2 != null ? item2.Value * prod2 : 0);
                    if (totalPayout <= 0) return;

                    var lord = BKLandGrantBehavior.ResolveBoundTownLord(v);
                    if (lord == null) return;
                    bool lordWasPaid = lord != Hero.MainHero; // EOF skips player payout
                    float taxRate = BKLandGrantBehavior.Instance.GetTaxRate(v.Settlement);

                    foreach (var (grantee, grantedCount) in grantees)
                    {
                        if (grantee == null || grantee.IsDead || grantee == lord) continue;
                        // Stale-grant guard: when bound-town ownership flips (war
                        // capture, sale, etc.), grants made by the old liege may
                        // point at heroes who are no longer in the new lord's
                        // clan. Skip those — the new lord shouldn't pay out to
                        // the old lord's vassals. A periodic cleanup behavior
                        // should reconcile the dict, but the postfix gate
                        // prevents bad payouts in the meantime.
                        if (grantee.Clan == null || grantee.Clan != lord.Clan) continue;
                        float fraction = (float)grantedCount / totalLordLands;
                        int shareGross = (int)(totalPayout * fraction);
                        if (shareGross <= 0) continue;
                        int lordSkim = (int)(shareGross * taxRate);
                        int granteeNet = shareGross - lordSkim;

                        if (lordWasPaid)
                        {
                            // EOF paid lord shareGross. Move (shareGross - lordSkim) to grantee.
                            if (granteeNet > 0)
                            {
                                lord.ChangeHeroGold(-granteeNet);
                                grantee.ChangeHeroGold(granteeNet);
                            }
                        }
                        else
                        {
                            // EOF paid lord nothing (lord is player). Pay grantee shareGross
                            // outright; the player-as-liege "tax" is implicit (they didn't
                            // pay out any income, so no skim to take back).
                            if (shareGross > 0) grantee.ChangeHeroGold(shareGross);
                        }
                    }
                }
                catch
                {
                    // Defensive: failed redistribution must not break the daily tick.
                }
            }
        }

        // Rewires EOF's town population panel to display BK's class breakdown.
        //
        // EOF's BLM_PopulationFunction.CalculateClasses derives "poor /
        // affluent / noble" from prosperity / 3 split by static percentages
        // tied to the town's policy. The numbers are display-only with no
        // demographic depth; players running BK see BK's panel (real serfs/
        // slaves/freemen/craftsmen/nobles with growth, food, tax) and EOF's
        // panel side-by-side, with totals that can't be reconciled.
        //
        // This postfix overwrites the out parameters with values mapped from
        // BK's PopulationData classes:
        //   poor      = Serfs + Slaves
        //   affluent  = Craftsmen + Tenants
        //   noble     = Nobles
        // EOF's policy bonuses (loyalty/security/prosperity) still apply
        // because they read policyIndex, not the class counts.
        [HarmonyPatch]
        internal static class BLM_PopulationCalculateClassesBKPanelPatch
        {
            private static System.Type _funcType;

            private static bool Prepare()
            {
                if (!ModCompat.EconomyOverhaul) return false;
                _funcType = AccessTools.TypeByName(BLM_PopulationFunctionType);
                return _funcType != null
                    && AccessTools.Method(_funcType, "CalculateClasses",
                        new[] { typeof(Town), typeof(int),
                                typeof(int).MakeByRefType(),
                                typeof(int).MakeByRefType(),
                                typeof(int).MakeByRefType() }) != null;
            }

            private static MethodBase TargetMethod()
                => AccessTools.Method(_funcType, "CalculateClasses",
                    new[] { typeof(Town), typeof(int),
                            typeof(int).MakeByRefType(),
                            typeof(int).MakeByRefType(),
                            typeof(int).MakeByRefType() });

            private static void Postfix(Town town, int policyIndex,
                                        ref int poor, ref int affluent, ref int noble)
            {
                if (town?.Settlement == null) return;
                try
                {
                    var data = BannerKingsConfig.Instance?.PopulationManager?.GetPopData(town.Settlement);
                    if (data == null) return;
                    int serfs = data.GetTypeCount(BannerKings.Managers.PopulationManager.PopType.Serfs);
                    int slaves = data.GetTypeCount(BannerKings.Managers.PopulationManager.PopType.Slaves);
                    int craftsmen = data.GetTypeCount(BannerKings.Managers.PopulationManager.PopType.Craftsmen);
                    int tenants = data.GetTypeCount(BannerKings.Managers.PopulationManager.PopType.Tenants);
                    int nobles = data.GetTypeCount(BannerKings.Managers.PopulationManager.PopType.Nobles);

                    int total = serfs + slaves + craftsmen + tenants + nobles;
                    if (total <= 0) return;

                    poor = serfs + slaves;
                    affluent = craftsmen + tenants;
                    noble = nobles;
                }
                catch
                {
                    // Defensive: panel display falling back to EOF's static
                    // values is preferable to crashing the panel render.
                }
            }
        }

        // Appends BK's village population breakdown to EOF's village panel.
        //
        // EOF's VillageAddonsVM displays Hearth, Militia, lord-lands prison
        // count, lord-lands totals, etc. — but no demographic data. BK has
        // the actual class breakdown (Serfs / Slaves / Craftsmen / Tenants /
        // Nobles) on `PopulationData`, and players otherwise have no way to
        // see it without opening BK's separate settlement panel.
        //
        // This postfix runs after EOF's RefreshValues writes its fields and
        // appends "(S:N Sf:N C:N T:N N:N)" to `HearthText` — the field
        // adjacent to hearth/development is the natural home for population
        // since the two are conceptually paired. The cheap parenthetical
        // format avoids needing a UIExtender XML overlay for a separate row.
        [HarmonyPatch]
        internal static class BLM_VillageAddonsVMPopulationPatch
        {
            private const string VillageAddonsVMType =
                "Bannerlord.Economy_Overhaul.View_Model.VillageAddonsVM";
            private static Type _vmType;
            private static MethodInfo _hearthSetter;

            private static bool Prepare()
            {
                if (!ModCompat.EconomyOverhaul) return false;
                _vmType = AccessTools.TypeByName(VillageAddonsVMType);
                if (_vmType == null) return false;
                _hearthSetter = AccessTools.PropertySetter(_vmType, "HearthText");
                return _hearthSetter != null
                    && AccessTools.Method(_vmType, "RefreshValues", Type.EmptyTypes) != null;
            }

            private static MethodBase TargetMethod()
                => AccessTools.Method(_vmType, "RefreshValues", Type.EmptyTypes);

            private static void Postfix(object __instance)
            {
                if (__instance == null) return;
                try
                {
                    var settlement = Settlement.CurrentSettlement;
                    if (settlement?.Village == null) return;
                    var data = BannerKingsConfig.Instance?.PopulationManager?.GetPopData(settlement);
                    if (data == null) return;
                    int serfs = data.GetTypeCount(BannerKings.Managers.PopulationManager.PopType.Serfs);
                    int slaves = data.GetTypeCount(BannerKings.Managers.PopulationManager.PopType.Slaves);
                    int craftsmen = data.GetTypeCount(BannerKings.Managers.PopulationManager.PopType.Craftsmen);
                    int tenants = data.GetTypeCount(BannerKings.Managers.PopulationManager.PopType.Tenants);
                    int nobles = data.GetTypeCount(BannerKings.Managers.PopulationManager.PopType.Nobles);
                    int total = serfs + slaves + craftsmen + tenants + nobles;
                    if (total <= 0) return;

                    int hearth = (int)settlement.Village.Hearth;
                    string composed = string.Format(
                        "{0:N0}  (Pop {1:N0} — S:{2} Sf:{3} C:{4} T:{5} N:{6})",
                        hearth, total, slaves, serfs, craftsmen, tenants, nobles);
                    _hearthSetter.Invoke(__instance, new object[] { composed });
                }
                catch
                {
                    // Defensive: a BK lookup hiccup must not break panel render.
                }
            }
        }

        // Gates EOF's TryBuyOneLand by the village's title's Estate Tenure law.
        // (As of v1.8.7.0 Fee Tail is removed from active law options; this
        // prefix is kept for forward compatibility in case a future tenure
        // law needs a similar gate, but the Prepare() now returns false so
        // it's a no-op until a new gate is wired in.)
        // Fee Tail prohibits buying land in fiefs you don't already hold (estates
        // are entailed within the holder's lineage); Quia Emptores and Allodial
        // permit free purchase. Permissive default if no tenure law is enacted.
        [HarmonyPatch]
        internal static class TryBuyOneLandTenureGate
        {
            private static Type _addonsBehaviorType;

            // Prepare always false post-v1.8.7.0 — no active gate. Method
            // resolution and stub kept so the structure is in place to add
            // future tenure-based purchase gates without rewriting the
            // patch class.
            private static bool Prepare() => false;

            private static MethodBase TargetMethod()
                => AccessTools.Method(_addonsBehaviorType, "TryBuyOneLand",
                    new[] { typeof(Village) });

            private static bool Prefix(Village v, ref bool __result) => true;
        }
    }
}
