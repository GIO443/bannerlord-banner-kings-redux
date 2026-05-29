using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem.Inventory;

namespace BannerKings.Patches
{
    /// <summary>
    /// v1.9.10.5 — defensive shim for the inventory pricing path.
    ///
    /// User crash 2026-05-29 (Debug.htm): ArgumentNullException at
    /// Dictionary.FindEntry inside TownMarketData.GetCategoryData
    /// because the looked-up ItemCategory key is null. Path:
    ///   SPInventoryVM.set_EquipmentMode →
    ///     InitializeCharacterEquipmentSlot →
    ///       InventoryLogic.GetItemPrice →
    ///         TownMarketData.GetPrice →
    ///           GetCategoryData(item.ItemCategory ← null)
    ///
    /// Some installed-mod item (the user's mod list includes several
    /// content packs adding custom equipment) ships with no ItemCategory
    /// set, so equipping it surfaces a null category at the price lookup
    /// and the inventory screen dies the moment the player toggles
    /// equip-mode on that character.
    ///
    /// Finalizer at the outermost BK-touchable entry (InventoryLogic
    /// GetItemPrice — returns int). Swallow only ArgumentNullException
    /// and return 0 (no displayed price for that one slot). Rest of
    /// the inventory renders normally. Every other exception class
    /// propagates unchanged so unrelated bugs stay visible.
    /// </summary>
    [HarmonyPatch(typeof(InventoryLogic))]
    internal static class InventoryPriceSafetyPatch
    {
        [HarmonyFinalizer]
        [HarmonyPatch(nameof(InventoryLogic.GetItemPrice))]
        private static Exception GetItemPriceFinalizer(Exception __exception, ref int __result)
        {
            if (__exception is ArgumentNullException)
            {
                __result = 0;
                return null;
            }
            return __exception;
        }
    }
}
