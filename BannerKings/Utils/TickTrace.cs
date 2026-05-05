using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace BannerKings.Utils
{
    // Wraps daily-tick / hourly-tick subscriber delegates with the existing
    // BKShippingBehavior.TraceEnter/TraceExit pair. Used for handlers BK
    // didn't bracket directly in their behaviour bodies. Zero behaviour
    // change — when LogHourlyTickPerf MCM toggle is off, TraceEnter returns
    // null and TraceExit no-ops, so the wrapper costs one extra delegate
    // invocation per tick (negligible).
    //
    // Why wrap at the registration site instead of editing each method?
    // Two reasons:
    //   1. Diff is one line per subscription instead of try/finally
    //      surgery on every body, with their early returns.
    //   2. The wrapper names appear in tick_trace.txt with the entity
    //      being processed (hero/party/town/clan name), so when a freeze
    //      lands the last unmatched ENTER points directly at "Behaviour X
    //      ticking entity Y" — no guessing about which subscriber hung.
    //
    // Names are per-arg-type (WrapHero, WrapClan, …) instead of overloaded
    // Wrap to keep method-group conversions unambiguous at the call site.
    public static class TickTrace
    {
        public static Action Wrap(string handlerName, Action body) => () =>
        {
            var sw = BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceEnter(handlerName);
            try { body(); }
            finally { BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceExit(handlerName, sw); }
        };

        public static Action<Hero> WrapHero(string handlerName, Action<Hero> body) => h =>
        {
            var label = h?.Name?.ToString();
            var sw = BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceEnter(
                label != null ? handlerName + ":" + label : handlerName);
            try { body(h); }
            finally { BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceExit(handlerName, sw); }
        };

        public static Action<Clan> WrapClan(string handlerName, Action<Clan> body) => c =>
        {
            var label = c?.Name?.ToString();
            var sw = BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceEnter(
                label != null ? handlerName + ":" + label : handlerName);
            try { body(c); }
            finally { BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceExit(handlerName, sw); }
        };

        public static Action<MobileParty> WrapParty(string handlerName, Action<MobileParty> body) => p =>
        {
            var label = p?.Name?.ToString();
            var sw = BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceEnter(
                label != null ? handlerName + ":" + label : handlerName);
            try { body(p); }
            finally { BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceExit(handlerName, sw); }
        };

        public static Action<Settlement> WrapSettlement(string handlerName, Action<Settlement> body) => s =>
        {
            var label = s?.Name?.ToString();
            var sw = BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceEnter(
                label != null ? handlerName + ":" + label : handlerName);
            try { body(s); }
            finally { BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceExit(handlerName, sw); }
        };

        public static Action<Town> WrapTown(string handlerName, Action<Town> body) => t =>
        {
            var label = t?.Settlement?.Name?.ToString();
            var sw = BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceEnter(
                label != null ? handlerName + ":" + label : handlerName);
            try { body(t); }
            finally { BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceExit(handlerName, sw); }
        };
    }
}
