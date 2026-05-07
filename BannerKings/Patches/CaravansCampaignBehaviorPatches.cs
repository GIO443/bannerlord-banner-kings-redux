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

    /// <summary>
    /// Skip vanilla CaravansCampaignBehavior.HourlyTickParty entirely.
    /// Per the design memo "BK decides, vanilla executes," vanilla's
    /// HourlyTickParty is pure decision logic — it calls vanilla
    /// ThinkNextDestination (which has NO port-filter for naval caravans)
    /// and SetPartyAiAction.GetActionForVisitingSettlement with
    /// bestNavigationType. For a Convoy (HasNavalNavigationCapability=true)
    /// this can pick an inland town and Default navigation, sending the
    /// boat sprite onto land tiles — the visible "convoy beached" bug.
    ///
    /// BK's BKCaravansBehavior.HourlyTickPartyImpl is the sole owner of
    /// caravan target decisions. It port-filters for naval caravans in
    /// ThinkNextDestination → FindNextDestinationForCaravan
    /// (BKCaravansBehavior.cs:1062 `if (naval && !town.Settlement.HasPort)
    /// continue;`) and uses NavigationType.Naval / aimAtPort=true at every
    /// SetMove site for naval caravans.
    ///
    /// What we KEEP from vanilla CaravansCampaignBehavior:
    ///   - OnSettlementEntered / OnSettlementLeft  — bookkeeping
    ///     (_caravanLastHomeTownVisitTime, prohibited-kingdom tracking)
    ///   - DailyTick / DailyTickHero               — price-data cache,
    ///     home-visit timer pruning
    ///   - OnNewGameCreatedPartialFollowUpEndEvent — initial spawn
    ///   - OnMobilePartyDestroyed / OnMobilePartyCreated — registry
    ///   - OnMapEventEnded / OnLootDistributedToParty — combat aftermath
    ///   - OnSiegeEventStarted                     — siege handling
    ///   - OnGameLoadFinished                      — cache rebuild
    ///   - OnKingdomDestroyed                      — prohibited-list cleanup
    /// All pure mechanics / state management — no decisions. They
    /// continue to run vanilla. BK only owns the per-hour target decision.
    ///
    /// What was lost: vanilla's BuyGoods call inside HourlyTickParty.
    /// BK already mirrors it in BKCaravansBehavior.HourlyTickPartyImpl
    /// (BKCaravansBehavior.cs:781 `BuyGoods(caravanParty, town);`) so the
    /// behavior is preserved. No new BK code needed.
    /// </summary>
    [HarmonyPatch(typeof(CaravansCampaignBehavior), "HourlyTickParty")]
    internal class CaravansCampaignBehavior_HourlyTickParty_Skip
    {
        // Gate the skip on BKCaravansBehavior actually being registered.
        // If BK fails to register (mod-load order conflict, another mod
        // strips it, etc.) and vanilla is silenced, caravans freeze on
        // stale targets — strictly worse than vanilla-only. Falling back
        // to vanilla in that case keeps the world ticking even when BK's
        // own caravan AI is missing. The lookup is cheap (dictionary
        // probe) and its result doesn't change at runtime.
        [HarmonyPrefix]
        private static bool Prefix()
        {
            try
            {
                var bk = TaleWorlds.CampaignSystem.Campaign.Current?
                    .GetCampaignBehavior<BannerKings.Behaviours.BKCaravansBehavior>();
                if (bk == null) return true;   // BK absent — let vanilla run
            }
            catch
            {
                return true;                   // probe failed — be conservative
            }
            return false;                      // BK present — skip vanilla
        }
    }
}
