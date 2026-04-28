using BannerKings.Behaviours;
using BannerKings.Managers;
using HarmonyLib;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace BannerKings.Patches
{
    internal class NordCompatPatches
    {
        // Vanilla's WorkshopsCampaignBehavior.ConsumeInputFromTownMarket on a fresh
        // game subtracts more from a per-element stock than the element actually
        // holds (BK's economy seeding leaves stocks lower than vanilla expects).
        // ItemRosterElement.set_Amount throws on a negative value, killing the
        // OnNewGameCreated chain. Clamp the setter to zero instead of throwing —
        // the over-subtraction was a benign accounting glitch, not a real bug.
        [HarmonyPatch(typeof(ItemRosterElement))]
        internal class ItemRosterElementAmountClamp
        {
            [HarmonyPrefix]
            [HarmonyPatch("set_Amount")]
            private static void SetAmountPrefix(ref int value)
            {
                if (value < 0) value = 0;
            }
        }

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
