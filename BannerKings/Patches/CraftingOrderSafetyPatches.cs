using System.Collections;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.CraftingSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace BannerKings.Patches
{
    // Vanilla CraftingCampaignBehavior is fragile around its private
    // `_craftingOrders` dict (Town → CraftingOrderSlots). If a notable
    // hero's CurrentSettlement points to a town that isn't in the dict
    // (un-registered, removed, or cross-mod settlement quirk), the
    // dict indexer throws KeyNotFoundException and the daily-tick stack
    // unwinds — observed crash:
    //
    //   System.Collections.Generic.Dictionary`2.get_Item(TKey key)
    //   CraftingCampaignBehavior.CreateTownOrder(Hero, Int32)
    //   CraftingCampaignBehavior.ReplaceCraftingOrder(Town, CraftingOrder)
    //   CraftingCampaignBehavior.DailyTick()
    //
    // BK doesn't touch the crafting system, but BK-aware saves can carry
    // notables whose CurrentSettlement was reassigned by a behaviour BK
    // *does* own (lord-property, settlement-rebellion, mod-compat
    // settlement add). Since the cost of a missed crafting-order slot
    // for one tick is zero and the cost of a daily-tick crash is the
    // whole game session, gate both vanilla methods on the dict membership
    // and silently bail when the key would be missing.
    //
    // The skip is logged to BK_crafting_safety.txt so we can audit how
    // often this fires and trace the upstream cause if it becomes
    // frequent.
    [HarmonyPatch]
    internal static class CraftingOrderSafetyPatches
    {
        private static FieldInfo _craftingOrdersField;

        private static bool Prepare()
        {
            _craftingOrdersField = AccessTools.Field(
                typeof(CraftingCampaignBehavior), "_craftingOrders");
            return _craftingOrdersField != null;
        }

        // Common dict-membership probe. Returns true if the dict contains
        // the key — caller proceeds. Returns false if missing or any
        // reflection failure — caller bails.
        private static bool DictHasTown(CraftingCampaignBehavior behavior, Town town)
        {
            if (behavior == null || town == null) return false;
            try
            {
                var dict = _craftingOrdersField.GetValue(behavior) as IDictionary;
                return dict != null && dict.Contains(town);
            }
            catch { return false; }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(CraftingCampaignBehavior), "CreateTownOrder")]
        private static bool CreateTownOrder_Prefix(
            CraftingCampaignBehavior __instance, Hero orderOwner, int orderSlot)
        {
            try
            {
                var town = orderOwner?.CurrentSettlement?.Town;
                if (town == null)
                {
                    Log($"CreateTownOrder skip: orderOwner={orderOwner?.StringId ?? "null"} town=null");
                    return false; // skip vanilla, no order created this tick
                }
                if (!DictHasTown(__instance, town))
                {
                    Log($"CreateTownOrder skip: orderOwner={orderOwner?.StringId} town={town.Settlement?.StringId ?? "?"} not in _craftingOrders");
                    return false;
                }
            }
            catch
            {
                // Probe failure → safest is to skip vanilla rather than risk
                // a partial state mutation followed by a downstream crash.
                return false;
            }
            return true; // dict has the key → vanilla can run
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(CraftingCampaignBehavior), "ReplaceCraftingOrder")]
        private static bool ReplaceCraftingOrder_Prefix(
            CraftingCampaignBehavior __instance, Town town, CraftingOrder order)
        {
            try
            {
                if (town == null) { Log("ReplaceCraftingOrder skip: town=null"); return false; }
                if (!DictHasTown(__instance, town))
                {
                    Log($"ReplaceCraftingOrder skip: town={town.Settlement?.StringId ?? "?"} not in _craftingOrders");
                    return false;
                }
            }
            catch { return false; }
            return true;
        }

        private static void Log(string msg)
        {
            try
            {
                BannerKings.BannerKingsCheats.AppendDiagnosticLine(
                    "crafting_safety.txt",
                    $"{TaleWorlds.CampaignSystem.CampaignTime.Now}  {msg}");
            }
            catch { /* never throw out of a logger */ }
        }
    }
}
