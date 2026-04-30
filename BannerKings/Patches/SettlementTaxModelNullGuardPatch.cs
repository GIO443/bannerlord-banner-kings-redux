using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Settlements;

namespace BannerKings.Patches
{
    /// <summary>
    /// Vanilla 1.3.x DefaultSettlementTaxModel.GetVillageTaxRatio NREs when
    /// called against villages with incomplete state (no Bound, no Town on
    /// Bound, etc.) Each NRE is caught somewhere upstream so the game
    /// keeps running, but at the rate fired during heavy raid / caravan
    /// activity (~400 per hourly tick observed) the first-chance exception
    /// machinery — stack walk + BUTR's BEW listener writing to disk —
    /// becomes a measurable frame-drop source. This guard short-circuits
    /// to a sensible default when the inputs would crash, so the NRE
    /// never fires.
    ///
    /// Manual install from Main.OnSubModuleLoad (no [HarmonyPatch] attr)
    /// so the guard is up before any caller runs.
    /// </summary>
    internal static class SettlementTaxModelNullGuardPatch
    {
        public static MethodBase TargetMethod()
            => AccessTools.Method(typeof(DefaultSettlementTaxModel), "GetVillageTaxRatio");

        public static bool Prefix(Village village, ref float __result)
        {
            // Run the original only when the village has the structure
            // GetVillageTaxRatio dereferences. Otherwise return a neutral
            // 1.0 ratio (no tax adjustment) and skip the original.
            try
            {
                if (village == null) { __result = 1f; return false; }
                if (village.Settlement == null) { __result = 1f; return false; }
                if (village.Bound == null || village.Bound.Town == null) { __result = 1f; return false; }
                if (village.VillageType == null) { __result = 1f; return false; }
            }
            catch
            {
                __result = 1f;
                return false;
            }
            return true;
        }
    }
}
