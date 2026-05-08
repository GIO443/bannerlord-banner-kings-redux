using BannerKings.Behaviours;
using BannerKings.Managers.Items;
using BannerKings.Settings;
using HarmonyLib;
using Helpers;
using SandBox.View.Map;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Extensions;
using TaleWorlds.CampaignSystem.Inventory;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BannerKings.Patches
{
    internal class FixesPatches
    {
        [HarmonyPatch(typeof(CompanionsCampaignBehavior))]
        internal class CompanionsCampaignBehaviorPatches
        {
            [HarmonyPrefix]
            [HarmonyPatch("_desiredTotalCompanionCount", MethodType.Getter)]
            private static bool DesiredTotalPrefix(ref float __result)
            {
                __result = Town.AllTowns.Count * BannerKingsSettings.Instance.WorldCompanions;
                return false;
            }
        }

        [HarmonyPatch(typeof(MapScreen))]
        internal class MapScreenPatches
        {
            [HarmonyPrefix]
            [HarmonyPatch("OnExitToMainMenu")]
            private static bool OnExitToMainMenu()
            {
                BKManagerBehavior behavior = TaleWorlds.CampaignSystem.Campaign.Current.GetCampaignBehavior<BKManagerBehavior>();
                behavior.NullManagers();
                return true;
            }
        }

        [HarmonyPatch(typeof(NameGenerator))]
        internal class NameGeneratorPatch
        {
            [HarmonyPrefix]
            [HarmonyPatch("GenerateHeroFullName")]
            private static bool GenerateHeroFullNamePrefix(ref TextObject __result, Hero hero, TextObject heroFirstName,
                bool useDeterministicValues = true)
            {
                var parent = hero.IsFemale ? hero.Mother : hero.Father;
                if (parent == null)
                {
                    return true;
                }

                if (BannerKingsConfig.Instance.TitleManager.IsHeroKnighted(parent) && hero.IsWanderer)
                {
                    var textObject = heroFirstName;
                    textObject.SetTextVariable("FEMALE", hero.IsFemale ? 1 : 0);
                    textObject.SetTextVariable("IMPERIAL", hero.Culture.StringId == "empire" ? 1 : 0);
                    textObject.SetTextVariable("COASTAL",
                        hero.Culture.StringId is "empire" or "vlandia" ? 1 : 0);
                    textObject.SetTextVariable("NORTHERN",
                        hero.Culture.StringId is "battania" or "sturgia" ? 1 : 0);
                    textObject.SetCharacterProperties("HERO", hero.CharacterObject);
                    textObject.SetTextVariable("FIRSTNAME", heroFirstName);
                    __result = textObject;
                    return false;
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(InventoryLogic))]
        internal class InventoryLogicPatches
        {
            [HarmonyPrefix]
            [HarmonyPatch("SlaughterItem")]
            private static bool SlaughterItemPrefix(ItemRosterElement itemRosterElement)
            {
                // SlaughterItem is invoked from contexts where the item may
                // not have a HorseComponent (modded non-livestock items
                // tagged as slaughterable, edge-case event consumables).
                // The unguarded `equipmentElement.Item.HorseComponent.MeatCount`
                // NREs and skips the slaughter UI entirely. Treat
                // missing-component or zero-meat as "not slaughterable" and
                // skip the original method (return false), matching the
                // existing behavior for MeatCount == 0.
                var item = itemRosterElement.EquipmentElement.Item;
                if (item == null) return false;
                var hc = item.HorseComponent;
                if (hc == null || hc.MeatCount == 0)
                {
                    return false;
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(DefaultItems))]
        internal class RegisterItemsAndCategories
        {
            [HarmonyPostfix]
            [HarmonyPatch("InitializeAll")]
            private static void InitializeAllPostfix()
            {
                BKItemCategories.Instance.Initialize();
                BKItems.Instance.Initialize();
            }
        }

        [HarmonyPatch(typeof(FoodConsumptionBehavior))]
        internal class FoodConsumptionBehaviorPatches
        {
            [HarmonyPrefix]
            [HarmonyPatch("MakeFoodConsumption")]
            private static bool MakeFoodConsumptionPrefix(MobileParty party, ref int partyRemainingFoodPercentage)
            {
                ItemRoster itemRoster = party.ItemRoster;
                int num = 0;
                for (int i = 0; i < itemRoster.Count; i++)
                {
                    if (itemRoster.GetItemAtIndex(i).IsFood)
                    {
                        num++;
                    }
                }
                bool flag = false;
                int count = 0;
                while (num > 0 && partyRemainingFoodPercentage < 0)
                {
                    count++;
                    if (count > 5000)
                        break;
                    int num2 = MBRandom.RandomInt(num);
                    bool flag2 = false;
                    int num3 = 0;
                    for (int i = itemRoster.Count - 1; i >= 0 && !flag2; i--)
                    {
                        if (itemRoster.GetItemAtIndex(i).IsFood)
                        {
                            int elementNumber = itemRoster.GetElementNumber(i);
                            if (elementNumber > 0)
                            {
                                num3++;
                                if (num2 < num3)
                                {
                                    itemRoster.AddToCounts(itemRoster.GetItemAtIndex(i), -1);
                                    partyRemainingFoodPercentage += 100;
                                    if (elementNumber == 1)
                                    {
                                        num--;
                                    }
                                    flag2 = true;
                                    flag = true;
                                }
                            }
                        }
                    }
                    if (flag)
                    {
                        party.Party.OnConsumedFood();
                    }
                }
                return false;
            }

            [HarmonyPrefix]
            [HarmonyPatch("SlaughterLivestock")]
            private static bool SlaughterLivestockPrefix(MobileParty party, int partyRemainingFoodPercentage, ref bool __result)
            {
                // v1.6.9.31: freeze pinned here on starving lord parties.
                //
                // Two bugs in the original prefix:
                //   1. The inner `while (num * 100 < -partyRemainingFoodPercentage)`
                //      loop had no progress guard. If a livestock item had
                //      `HorseComponent.MeatCount <= 0` (mod-added animal,
                //      misconfigured XML, race condition during cleanup),
                //      `num` never advanced and the loop spun forever.
                //   2. `foreach (var element in itemRoster)` while
                //      `itemRoster.AddToCounts(itemAtIndex, -1)` mutates the
                //      same roster — undefined enumerator behavior. A
                //      consistent way to crash this is a starving party with
                //      a single 1-count livestock item: AddToCounts removes
                //      it during enumeration, the foreach reads a stale slot,
                //      next iteration's `element.EquipmentElement.Item` may
                //      hit a zombie reference or wrap to a wrong item.
                //
                // Fix: snapshot the livestock items first, then iterate the
                // snapshot and skip any livestock with non-positive MeatCount.
                // The inner while gets an explicit-progress guard: if a
                // single AddToCounts didn't reduce the roster's index for
                // this item below the prior step (auto-remove didn't fire AND
                // MeatCount was zero), break out instead of spinning.
                int num = 0;
                ItemRoster itemRoster = party.ItemRoster;

                var livestock = new List<ItemObject>();
                foreach (var element in itemRoster)
                {
                    ItemObject item = element.EquipmentElement.Item;
                    var hc = item?.HorseComponent;
                    if (hc != null && hc.IsLiveStock && hc.MeatCount > 0)
                    {
                        livestock.Add(item);
                    }
                }

                foreach (var item in livestock)
                {
                    int meatPerHead = item.HorseComponent.MeatCount;
                    int safety = 0;
                    while (num * 100 < -partyRemainingFoodPercentage)
                    {
                        if (++safety > 10000) break;          // hard stop — can't legitimately need 10k slaughter ops
                        if (itemRoster.FindIndexOfItem(item) == -1) break;
                        itemRoster.AddToCounts(item, -1);
                        num += meatPerHead;
                    }
                }

                if (num > 0)
                {
                    itemRoster.AddToCounts(DefaultItems.Meat, num);
                    __result = true;
                }
                else __result = false;

                return false;
            }
        }
    }
}
