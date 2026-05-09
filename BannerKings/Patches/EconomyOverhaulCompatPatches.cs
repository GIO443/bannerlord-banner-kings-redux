using System;
using System.Reflection;
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
    }
}
