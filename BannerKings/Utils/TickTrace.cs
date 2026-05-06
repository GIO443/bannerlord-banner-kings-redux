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
        // Hang-safe label helpers. Use these for INLINE TraceEnter sub-traces
        // (where the wrapper itself isn't in play). They read StringId only —
        // Name accessors on Hero / PartyComponent / Settlement chain through
        // patched property getters or vanilla components that the firstchance
        // log shows can NRE; if any one of those hangs (rather than throws)
        // the trace label construction blocks the whole tick. StringId is a
        // plain readonly string field, never hangs, never throws.
        public static string IdOf(Hero h)
        { try { return h?.StringId ?? "?"; } catch { return "?"; } }
        public static string IdOf(Clan c)
        { try { return c?.StringId ?? "?"; } catch { return "?"; } }
        public static string IdOf(MobileParty p)
        { try { return p?.StringId ?? "?"; } catch { return "?"; } }
        public static string IdOf(Settlement s)
        { try { return s?.StringId ?? "?"; } catch { return "?"; } }
        public static string IdOf(Town t)
        { try { return t?.Settlement?.StringId ?? "?"; } catch { return "?"; } }

        public static Action Wrap(string handlerName, Action body) => () =>
        {
            var sw = BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceEnter(handlerName);
            try { body(); }
            finally { BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceExit(handlerName, sw); }
        };

        public static Action<Hero> WrapHero(string handlerName, Action<Hero> body) => h =>
        {
            // Use StringId only. h.Name chains through the patched Hero.Name
            // getter (UIManager.cs:189), which has a 10% RNG-rebuild branch
            // that walks TitleManager state. v1.6.9.20 wrapped that path in
            // try/catch but try/catch CAN'T CATCH A HANG, only an exception.
            // If vanilla / BK / another mod's chain inside the Name getter
            // hangs on bad state, the wrapper's label-construction blocks
            // BEFORE TraceEnter fires — explaining freezes that show clean
            // BK ENTER/EXIT pairs but no next-subscriber ENTER.
            // StringId is a plain readonly string field. No patches, no
            // chained accessors, cannot hang.
            string label = null;
            try { label = h?.StringId; } catch { }
            var sw = BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceEnter(
                !string.IsNullOrEmpty(label) ? handlerName + ":" + label : handlerName);
            try { body(h); }
            finally { BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceExit(handlerName, sw); }
        };

        public static Action<Clan> WrapClan(string handlerName, Action<Clan> body) => c =>
        {
            string label = null;
            try { label = c?.StringId; } catch { }
            var sw = BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceEnter(
                !string.IsNullOrEmpty(label) ? handlerName + ":" + label : handlerName);
            try { body(c); }
            finally { BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceExit(handlerName, sw); }
        };

        public static Action<MobileParty> WrapParty(string handlerName, Action<MobileParty> body) => p =>
        {
            // RAW-ENTER probe — emitted BEFORE StringId access. Distinguishes
            // three failure modes when the trace ends just before this
            // handler's normal ENTER:
            //   1. No RAW-ENTER for this handler → dispatch never reached us;
            //      hang is in vanilla MbEvent.InvokeList iteration or in an
            //      untraced subscriber registered before us at this event.
            //   2. RAW-ENTER but no ENTER → hang is in StringId access (rare
            //      but possible if a mod patches the StringId getter).
            //   3. ENTER but no EXIT → hang is in body.
            // No matching exit by design — RAW is asymmetric on purpose so
            // the unmatched line is the localizing signal.
            BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceEnter(handlerName + ".RAW");
            // StringId only. p.Name → PartyComponent.Name → for bandit
            // parties this is BanditPartyComponent.get_Name() which the
            // firstchance log shows NRE'ing intermittently — and if it
            // hangs instead of throwing on a bad state, try/catch can't
            // save us. StringId is plain string, never hangs.
            string label = null;
            try { label = p?.StringId; } catch { }
            var sw = BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceEnter(
                !string.IsNullOrEmpty(label) ? handlerName + ":" + label : handlerName);
            try { body(p); }
            finally { BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceExit(handlerName, sw); }
        };

        public static Action<Settlement> WrapSettlement(string handlerName, Action<Settlement> body) => s =>
        {
            string label = null;
            try { label = s?.StringId; } catch { }
            var sw = BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceEnter(
                !string.IsNullOrEmpty(label) ? handlerName + ":" + label : handlerName);
            try { body(s); }
            finally { BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceExit(handlerName, sw); }
        };

        public static Action<Town> WrapTown(string handlerName, Action<Town> body) => t =>
        {
            string label = null;
            try { label = t?.Settlement?.StringId; } catch { }
            var sw = BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceEnter(
                !string.IsNullOrEmpty(label) ? handlerName + ":" + label : handlerName);
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
            string label = null;
            try { label = settlement?.StringId; } catch { }
            var sw = BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceEnter(
                !string.IsNullOrEmpty(label) ? handlerName + ":" + label : handlerName);
            try { body(settlement, openToClaim, newOwner, oldOwner, capturerHero, detail); }
            finally { BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceExit(handlerName, sw); }
        };
    }
}
