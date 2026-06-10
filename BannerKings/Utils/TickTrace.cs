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

        // ---- Always-on slow-handler self-detector --------------------------
        // Independent of the LogHourlyTickPerf toggle. Every Wrap* body is
        // timed by a dedicated Stopwatch (one StartNew + Stop per call —
        // nanoseconds, no allocation beyond the stopwatch struct's box).
        // If a SINGLE call exceeds SlowHandlerThresholdMs, we append one line
        // to BK_slow.txt naming the handler and the entity StringId. Nothing
        // is written below the threshold, so steady-state cost is just the
        // timer; the file is created only when a genuinely slow tick happens.
        //
        // Why this exists: the intermittent ~10-minute "day → night" freeze
        // never landed in any tester's manual tick_trace.txt capture (they
        // kept submitting windows that missed it). A freeze that eventually
        // RECOVERS is, by definition, a handler that eventually RETURNS — so
        // a post-body timer captures it with zero tester effort. The rare/
        // intermittent nature points at one pathological entity hitting a
        // super-linear branch, which is exactly what handler:StringId names.
        //
        // Limitation: a true infinite deadlock (never returns) produces
        // nothing here — but a 10-min freeze that the user plays through is
        // a slow-completion, not a deadlock. Vanilla handlers BK doesn't
        // wrap (e.g. KingdomDecisionProposalBehavior) are also out of scope;
        // a clean BK_slow.txt during a freeze itself narrows the cause to
        // non-BK code.
        private const long SlowHandlerThresholdMs = 3000;

        public static void WatchSlow(string handlerName, string label, System.Diagnostics.Stopwatch watch)
        {
            if (watch == null) return;
            try
            {
                watch.Stop();
                long ms = watch.ElapsedMilliseconds;
                if (ms < SlowHandlerThresholdMs) return;
                string who = string.IsNullOrEmpty(label) ? handlerName : handlerName + ":" + label;
                BannerKings.BannerKingsCheats.AppendDiagnosticLine("slow.txt",
                    $"SLOW {who} took {ms} ms");
            }
            catch { /* defensive: never throw out of a tick wrapper */ }
        }

        public static Action Wrap(string handlerName, Action body) => () =>
        {
            var sw = BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceEnter(handlerName);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            FreezeWatchdog.Enter(handlerName, null);
            try { body(); }
            finally
            {
                FreezeWatchdog.Exit();
                BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceExit(handlerName, sw);
                WatchSlow(handlerName, null, watch);
            }
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
            var watch = System.Diagnostics.Stopwatch.StartNew();
            FreezeWatchdog.Enter(handlerName, label);
            try { body(h); }
            finally
            {
                FreezeWatchdog.Exit();
                BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceExit(handlerName, sw);
                WatchSlow(handlerName, label, watch);
            }
        };

        public static Action<Clan> WrapClan(string handlerName, Action<Clan> body) => c =>
        {
            string label = null;
            try { label = c?.StringId; } catch { }
            var sw = BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceEnter(
                !string.IsNullOrEmpty(label) ? handlerName + ":" + label : handlerName);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            FreezeWatchdog.Enter(handlerName, label);
            try { body(c); }
            finally
            {
                FreezeWatchdog.Exit();
                BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceExit(handlerName, sw);
                WatchSlow(handlerName, label, watch);
            }
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
            var watch = System.Diagnostics.Stopwatch.StartNew();
            FreezeWatchdog.Enter(handlerName, label);
            try { body(p); }
            finally
            {
                FreezeWatchdog.Exit();
                BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceExit(handlerName, sw);
                WatchSlow(handlerName, label, watch);
            }
        };

        public static Action<Settlement> WrapSettlement(string handlerName, Action<Settlement> body) => s =>
        {
            string label = null;
            try { label = s?.StringId; } catch { }
            var sw = BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceEnter(
                !string.IsNullOrEmpty(label) ? handlerName + ":" + label : handlerName);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            FreezeWatchdog.Enter(handlerName, label);
            try { body(s); }
            finally
            {
                FreezeWatchdog.Exit();
                BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceExit(handlerName, sw);
                WatchSlow(handlerName, label, watch);
            }
        };

        public static Action<Town> WrapTown(string handlerName, Action<Town> body) => t =>
        {
            string label = null;
            try { label = t?.Settlement?.StringId; } catch { }
            var sw = BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceEnter(
                !string.IsNullOrEmpty(label) ? handlerName + ":" + label : handlerName);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            FreezeWatchdog.Enter(handlerName, label);
            try { body(t); }
            finally
            {
                FreezeWatchdog.Exit();
                BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceExit(handlerName, sw);
                WatchSlow(handlerName, label, watch);
            }
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
            var watch = System.Diagnostics.Stopwatch.StartNew();
            FreezeWatchdog.Enter(handlerName, label);
            try { body(settlement, openToClaim, newOwner, oldOwner, capturerHero, detail); }
            finally
            {
                FreezeWatchdog.Exit();
                BannerKings.Behaviours.Shipping.BKShippingBehavior.TraceExit(handlerName, sw);
                WatchSlow(handlerName, label, watch);
            }
        };
    }
}
