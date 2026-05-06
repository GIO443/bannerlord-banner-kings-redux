using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
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
            // Build label defensively. h.Name chains into the BK Hero.Name
            // getter postfix (UIManager.cs:189), which has a 10% RNG-rebuild
            // branch that walks TitleManager state. After a settlement owner
            // change (siege capture) the title graph is freshly mutated for
            // a few seconds; a rebuild on that transient state is the
            // suspected source of the user-reported freezes that show clean
            // BK ENTER/EXIT pairs but no next-subscriber ENTER. Try/catch
            // and StringId fallback ensures a hung Name access doesn't
            // also block our trace logging which is itself the only way
            // we can pin the failure.
            string label = null;
            try { label = h?.Name?.ToString(); } catch { }
            if (string.IsNullOrEmpty(label)) { try { label = h?.StringId; } catch { } }
            var sw = BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceEnter(
                !string.IsNullOrEmpty(label) ? handlerName + ":" + label : handlerName);
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
            // Same defensive label-build as WrapHero. p.Name routes through
            // PartyComponent.Name which for lord parties chains into the
            // patched Hero.Name getter — the suspected freeze entry point.
            string label = null;
            try { label = p?.Name?.ToString(); } catch { }
            if (string.IsNullOrEmpty(label)) { try { label = p?.StringId; } catch { } }
            var sw = BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceEnter(
                !string.IsNullOrEmpty(label) ? handlerName + ":" + label : handlerName);
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

        // OnSettlementOwnerChangedEvent has 6 args: settlement, openToClaim,
        // newOwner, oldOwner, capturerHero, detail. Bracket the call so a
        // freeze in the post-mutation window names which BK subscriber +
        // settlement was being processed. State-mutation events are the
        // strongest correlate of freezes in the Lebanese-* and the user's
        // own-save logs (rebellion / siege capture immediately precede
        // every BK-trace-clean freeze observed in this session).
        public static Action<Settlement, bool, Hero, Hero, Hero, ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail>
            WrapOwnerChanged(string handlerName,
                Action<Settlement, bool, Hero, Hero, Hero, ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail> body)
            => (settlement, openToClaim, newOwner, oldOwner, capturerHero, detail) =>
        {
            var label = settlement?.Name?.ToString();
            var sw = BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceEnter(
                label != null ? handlerName + ":" + label : handlerName);
            try { body(settlement, openToClaim, newOwner, oldOwner, capturerHero, detail); }
            finally { BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceExit(handlerName, sw); }
        };
    }
}
