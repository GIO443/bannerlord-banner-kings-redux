using System.Collections.Generic;
using BannerKings.Managers.Institutions.Religions;
using BannerKings.UI;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.CampaignSystem.ViewModelCollection.Map.MapBar;
using TaleWorlds.Library.Information;

namespace BannerKings.Patches
{
    // Surfaces piety in the campaign HUD's right-side info bar by adding a
    // MapInfoItemVM("piety", ...) to MapInfoVM.PrimaryInfoItems. Replaces
    // the older MapBarMixin / MapBarExtension scheme — that pair tried to
    // bind extra DataSourceProperty fields on MapInfoVM and inject XML
    // alongside the existing TopInfoBar/BottomInfoBar ListPanels, but
    // 1.3.x's right panel is a templated data-bound list and additions
    // belong in the data source (PrimaryInfoItems), not as XML siblings.
    //
    // RefreshValues postfix handles initial setup AND any vanilla rebuild
    // (vanilla clears + recreates the items list inside RefreshValues).
    // UpdatePlayerInfo postfix tracks the per-tick value/warning state.
    internal static class MapInfoPietyPatches
    {
        // Per-VM piety item handle. ConditionalWeakTable would be cleaner
        // but a simple field works — there's only ever one MapInfoVM live
        // (campaign map HUD).
        private static MapInfoItemVM _pietyItem;

        [HarmonyPatch(typeof(MapInfoVM), nameof(MapInfoVM.RefreshValues))]
        public static class RefreshValuesPatch
        {
            public static void Postfix(MapInfoVM __instance)
            {
                if (BannerKingsConfig.Instance?.ReligionsManager == null) return;
                if (Hero.MainHero == null) return;

                var religion = BannerKingsConfig.Instance.ReligionsManager.GetHeroReligion(Hero.MainHero);
                if (religion == null)
                {
                    // Faithless — drop any stale item from a previous save.
                    if (_pietyItem != null && __instance.PrimaryInfoItems != null
                        && __instance.PrimaryInfoItems.Contains(_pietyItem))
                    {
                        __instance.PrimaryInfoItems.Remove(_pietyItem);
                    }
                    _pietyItem = null;
                    return;
                }

                // Vanilla RefreshValues clears+repopulates the lists, so the
                // old _pietyItem reference (if any) was just cleared. Build
                // a fresh one and append to the primary row.
                int initialPiety = (int)BannerKingsConfig.Instance.ReligionsManager.GetPiety(religion, Hero.MainHero);
                _pietyItem = new MapInfoItemVM("piety", () =>
                {
                    var rel = BannerKingsConfig.Instance.ReligionsManager.GetHeroReligion(Hero.MainHero);
                    int current = rel != null
                        ? (int)BannerKingsConfig.Instance.ReligionsManager.GetPiety(rel, Hero.MainHero)
                        : 0;
                    return UIHelper.GetPietyTooltip(rel, Hero.MainHero, current);
                });
                _pietyItem.IntValue = initialPiety;
                _pietyItem.Value = CampaignUIHelper.GetAbbreviatedValueTextFromValue(initialPiety);
                _pietyItem.HasWarning = initialPiety < 0;

                if (__instance.PrimaryInfoItems != null
                    && !__instance.PrimaryInfoItems.Contains(_pietyItem))
                {
                    __instance.PrimaryInfoItems.Add(_pietyItem);
                }
            }
        }

        [HarmonyPatch(typeof(MapInfoVM), "UpdatePlayerInfo")]
        public static class UpdatePlayerInfoPatch
        {
            public static void Postfix(MapInfoVM __instance, bool updateForced)
            {
                if (_pietyItem == null) return;
                if (BannerKingsConfig.Instance?.ReligionsManager == null) return;
                if (Hero.MainHero == null) return;

                var religion = BannerKingsConfig.Instance.ReligionsManager.GetHeroReligion(Hero.MainHero);
                if (religion == null)
                {
                    if (__instance.PrimaryInfoItems != null && __instance.PrimaryInfoItems.Contains(_pietyItem))
                        __instance.PrimaryInfoItems.Remove(_pietyItem);
                    _pietyItem = null;
                    return;
                }

                int piety = (int)BannerKingsConfig.Instance.ReligionsManager.GetPiety(religion, Hero.MainHero);
                if (_pietyItem.IntValue != piety || updateForced)
                {
                    _pietyItem.IntValue = piety;
                    _pietyItem.Value = CampaignUIHelper.GetAbbreviatedValueTextFromValue(piety);
                }
                _pietyItem.HasWarning = piety < 0;
            }
        }
    }
}
