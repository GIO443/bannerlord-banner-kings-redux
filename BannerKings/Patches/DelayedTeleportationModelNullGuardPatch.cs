using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;

namespace BannerKings.Patches
{
    /// <summary>
    /// Vanilla DefaultDelayedTeleportationModel.GetTeleportationDelayAsHours
    /// reads teleportingHero.Clan.HasNavalNavigationCapability without a null
    /// check the moment Hero.GetMapPoint() returns non-null. A clanless hero
    /// in the player clan's governor candidate list (or any hero passed via
    /// the same tooltip path) NREs through CampaignUIHelper.GetTeleportationDelayText
    /// → CampaignUIHelper.GetHeroGovernorEffectsTooltip → BasicTooltipViewModel.ExecuteBeginHint,
    /// crashing the game on hover in the Replace Governor flow.
    ///
    /// Manual install from Main.OnSubModuleLoad (no [HarmonyPatch] attr) so
    /// the guard is up before any tooltip can fire.
    /// </summary>
    internal static class DelayedTeleportationModelNullGuardPatch
    {
        public static MethodBase TargetMethod()
            => AccessTools.Method(typeof(DefaultDelayedTeleportationModel),
                nameof(DefaultDelayedTeleportationModel.GetTeleportationDelayAsHours));

        public static bool Prefix(Hero teleportingHero, PartyBase target, ref ExplainedNumber __result)
        {
            try
            {
                if (teleportingHero == null || target == null)
                {
                    __result = new ExplainedNumber(0f);
                    return false;
                }

                // The exact field vanilla NREs on. If Clan is null the original
                // method would dereference it as soon as GetMapPoint() returns
                // non-null. Skip and return a zero delay so the tooltip renders.
                if (teleportingHero.Clan == null)
                {
                    __result = new ExplainedNumber(0f);
                    return false;
                }

                if (Campaign.Current == null)
                {
                    __result = new ExplainedNumber(0f);
                    return false;
                }
            }
            catch
            {
                __result = new ExplainedNumber(0f);
                return false;
            }
            return true;
        }
    }
}
