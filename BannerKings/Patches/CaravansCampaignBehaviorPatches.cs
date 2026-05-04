using HarmonyLib;
using TaleWorlds.CampaignSystem.CampaignBehaviors;

namespace BannerKings.Patches
{
    /// <summary>
    /// Vanilla CaravansCampaignBehavior is the engine's caravan AI driver:
    /// HourlyTickParty, ThinkNextDestination, OnSettlementEntered/Left, and
    /// most importantly the implicit boarding/disembarking that the engine's
    /// NavalPartyNavigationModel transitions hook into. BK previously
    /// removed the entire behavior on new-game/load to defang one specific
    /// AccessViolation in DoInitialTradeRuns — the cure was worse than the
    /// disease, since stripping the whole pipeline left BK reimplementing
    /// half the engine's caravan-state machinery (often imperfectly: the
    /// boat-on-mountain and walking-water visual bugs trace back to BK
    /// shadowing state vanilla would have managed correctly).
    ///
    /// Per the "BK decides, vanilla executes" principle, this patch skips
    /// just the offending method (DoInitialTradeRuns) and leaves the rest
    /// of CaravansCampaignBehavior intact. BK runs its own
    /// DoInitialTradeRuns from BKCaravansBehavior.OnNewGameCreatedPartial-
    /// FollowUpEndEvent — that one uses Euclidean distance, not the
    /// NavalDLCMapDistanceModel.GetDistance call that crashed.
    /// </summary>
    [HarmonyPatch(typeof(CaravansCampaignBehavior), "DoInitialTradeRuns")]
    internal class CaravansCampaignBehavior_DoInitialTradeRuns_Skip
    {
        [HarmonyPrefix]
        private static bool Prefix() => false;
    }
}
