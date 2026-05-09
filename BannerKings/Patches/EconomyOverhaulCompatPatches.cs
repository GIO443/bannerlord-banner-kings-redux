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
        private const string BLM_VillageProductionModelType =
            "Bannerlord.Economy_Overhaul.Models.BLM_VillageProductionModel";

        private const string VillageAddonsBehaviorType =
            "Bannerlord.Economy_Overhaul.Behavior.VillageAddonsBehavior";

        private const string BLM_FarmFunctionType =
            "Bannerlord.Economy_Overhaul.BLM_FarmFunction";

        // Adds an estate-workforce utilization factor to EOF's per-item village
        // production. Saturation 1.0 ≈ baseline, clamped to [0.85, 1.20] so
        // even underpopulated or overpopulated estates only swing output by
        // ±20% — preserves EOF's intended balance while keeping BK estate
        // workforce relevant to the village's primary product.
        [HarmonyPatch]
        internal static class BLM_VillageProductionEstateWorkforcePatch
        {
            private static Type _modelType;

            private static bool Prepare()
            {
                if (!ModCompat.EconomyOverhaul) return false;
                // The estate-workforce factor only matters when BK estates are
                // an active gameplay system. Under EOF the estate loop is
                // currently paused (see BKFeatureGates.EstatesEnabled) pending
                // the estate-as-village-workshop redesign, so don't bother
                // patching village production for a system that isn't running.
                if (!BKFeatureGates.EstatesEnabled) return false;
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
                    var data = BannerKingsConfig.Instance?.PopulationManager?.GetPopData(village.Settlement);
                    var estates = data?.EstateData?.Estates;
                    if (estates == null || estates.Count == 0) return;

                    float saturationSum = 0f;
                    int saturationCount = 0;
                    foreach (var estate in estates)
                    {
                        float sat;
                        try { sat = estate.WorkforceSaturation; }
                        catch { continue; }
                        if (float.IsNaN(sat) || float.IsInfinity(sat) || sat <= 0f) continue;
                        saturationSum += sat;
                        saturationCount++;
                    }
                    if (saturationCount == 0) return;

                    float averageSaturation = saturationSum / saturationCount;
                    float clamped = MathF.Clamp(averageSaturation, 0.85f, 1.20f);
                    float factor = clamped - 1f;
                    if (MathF.Abs(factor) < 0.001f) return;

                    __result.AddFactor(factor, new TextObject("{=BK_EstateWorkforce}Estate workforce"));
                }
                catch
                {
                    // Defensive: a BK lookup hiccup must never break village production.
                }
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

        // Gates EOF's TryBuyOneLand by the village's title's Estate Tenure law.
        // Fee Tail prohibits buying land in fiefs you don't already hold (estates
        // are entailed within the holder's lineage); Quia Emptores and Allodial
        // permit free purchase. Permissive default if no tenure law is enacted.
        [HarmonyPatch]
        internal static class TryBuyOneLandTenureGate
        {
            private static Type _addonsBehaviorType;

            private static bool Prepare()
            {
                if (!ModCompat.EconomyOverhaul) return false;
                _addonsBehaviorType = AccessTools.TypeByName(VillageAddonsBehaviorType);
                return _addonsBehaviorType != null
                    && AccessTools.Method(_addonsBehaviorType, "TryBuyOneLand",
                        new[] { typeof(Village) }) != null;
            }

            private static MethodBase TargetMethod()
                => AccessTools.Method(_addonsBehaviorType, "TryBuyOneLand",
                    new[] { typeof(Village) });

            private static bool Prefix(Village v, ref bool __result)
            {
                if (v?.Settlement == null) return true;
                try
                {
                    var title = BannerKingsConfig.Instance?.TitleManager?.GetTitle(v.Settlement);
                    if (title?.Contract == null) return true;
                    if (title.Contract.IsLawEnacted(DefaultDemesneLaws.Instance.EstateTenureFeeTail))
                    {
                        var lord = BKLandGrantBehavior.ResolveBoundTownLord(v);
                        bool playerIsLord = lord == Hero.MainHero;
                        bool playerIsBloodKin = lord != null && AreImmediateKin(Hero.MainHero, lord);
                        if (!playerIsLord && !playerIsBloodKin)
                        {
                            MBInformationManager.AddQuickInformation(new TextObject(
                                "{=BK_FeeTailBlock}Fee Tail tenure restricts land purchases to the holder's lineage."));
                            __result = false;
                            return false;
                        }
                    }
                }
                catch { /* permissive on lookup failure */ }
                return true;
            }

            private static bool AreImmediateKin(Hero a, Hero b)
            {
                if (a == null || b == null) return false;
                if (a == b) return true;
                if (a.Father == b || a.Mother == b) return true;
                if (b.Father == a || b.Mother == a) return true;
                if (a.Father != null && a.Father == b.Father) return true;
                if (a.Mother != null && a.Mother == b.Mother) return true;
                if (a.Spouse == b) return true;
                return false;
            }
        }
    }
}
