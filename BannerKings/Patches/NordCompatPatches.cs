using BannerKings.Behaviours;
using BannerKings.Managers;
using HarmonyLib;
using TaleWorlds.CampaignSystem.Settlements;

namespace BannerKings.Patches
{
    internal class NordCompatPatches
    {
        // Skip the daily tick entirely for any settlement whose OwnerClan is null.
        // War Sails Nord settlements always have an owner clan, but this guard prevents
        // crashes if another mod or mid-campaign conquest leaves a settlement clan-less.
        [HarmonyPatch(typeof(BKSettlementBehavior), "DailySettlementTick")]
        internal class DailySettlementTickNullGuard
        {
            [HarmonyPrefix]
            private static bool Prefix(Settlement settlement)
            {
                return settlement?.OwnerClan != null;
            }
        }

        // Return null without throwing if the settlement has no title yet.
        // The existing TitleManager.GetTitle already catches exceptions, but a
        // prefix is cheaper than exception handling for frequently-called paths.
        [HarmonyPatch(typeof(TitleManager), "GetTitle", typeof(Settlement))]
        internal class GetTitleNullGuard
        {
            [HarmonyPrefix]
            private static bool Prefix(Settlement settlement, ref BannerKings.Managers.Titles.FeudalTitle __result)
            {
                if (settlement == null || settlement.OwnerClan == null)
                {
                    __result = null;
                    return false;
                }
                return true;
            }
        }
    }
}
