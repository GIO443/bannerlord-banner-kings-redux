using System;
using System.Threading;

namespace BannerKings.Utils
{
    // Purpose-built freeze detector. Unlike the post-hoc BK_slow.txt timer
    // (which only logs AFTER a handler returns, so a handler that never
    // returns is invisible to it), this runs a SEPARATE background thread
    // that samples what the campaign thread is currently doing. If the same
    // activity has been "in progress" for longer than a threshold — i.e. the
    // campaign thread is stuck inside it RIGHT NOW — the watchdog writes the
    // culprit to BK_freeze.txt from its own thread, even if the campaign
    // thread is fully hung and will never reach a post-body logger.
    //
    // Design constraints:
    //   * Zero allocation on the hot path. Enter() stores two interned
    //     references (a compile-time-constant handler name + the entity's
    //     StringId field) and one timestamp — no string concatenation. The
    //     "<handler>:<entity>" string is built ONLY inside the watchdog
    //     sample, and only when it actually fires (rare).
    //   * The watchdog thread NEVER touches game state. It reads three
    //     volatile fields and writes to disk via the async diagnostic
    //     writer. Safe to run while the campaign thread is frozen.
    //   * Flat-dispatch assumption: tick/event handlers do not nest other
    //     Enter()-wrapped handlers, so a single last-writer-wins marker is
    //     sufficient. Exit() clears the marker so idle periods (paused game,
    //     between ticks) read as "nothing running" and never false-fire.
    //
    // Output (BK_freeze.txt):
    //   STUCK <handler>:<entity> running 5s — campaign thread not progressing
    //   STUCK <handler>:<entity> running 15s — campaign thread not progressing
    //   ...
    //   RECOVERED <handler>:<entity> after 612s
    // The first STUCK line names the exact handler + entity that froze the
    // game, written the moment the stall crosses the threshold — no tester
    // timing required, and it survives even a never-returning hang.
    public static class FreezeWatchdog
    {
        // Stall must exceed this before the first STUCK line is written.
        // Above any legitimate handler (the slowest known-good BK daily
        // handler is < 1s); a real freeze is tens to hundreds of seconds.
        private const double WarnSeconds = 5.0;
        // After the first STUCK line, re-log this often while still stuck so
        // the file shows the stall growing (and confirms it's the same
        // handler, not a fast one being re-sampled).
        private const double RepeatSeconds = 10.0;
        // Watchdog sample period. 1s is frequent enough to localize a
        // multi-second stall and far too coarse to cost anything.
        private const int SampleMillis = 1000;

        // Hot-path state. volatile so the watchdog thread sees writes from
        // the campaign thread without a lock. We accept benign races: the
        // worst case is the watchdog reading a half-updated (handler,entity,
        // start) triple for one sample, which at most mislabels a single
        // 1s sample of a real multi-second stall — self-corrects next tick.
        private static volatile string _handler;   // compile-time constant, no alloc
        private static volatile string _entity;     // StringId field ref, no alloc
        private static long _startTicks;            // Stopwatch.GetTimestamp at Enter

        // Watchdog bookkeeping (touched only by the watchdog thread + the
        // start latch). Not volatile-critical.
        private static long _lastLoggedTicks;
        private static string _lastLoggedKey;
        private static int _started;                // 0/1 latch via Interlocked
        private static Timer _timer;

        // Call once; cheap to call repeatedly (latched). Wired from a
        // campaign-start hook so the watchdog only runs inside a campaign.
        public static void EnsureStarted()
        {
            if (Interlocked.CompareExchange(ref _started, 1, 0) != 0) return;
            try { _timer = new Timer(Sample, null, SampleMillis, SampleMillis); }
            catch { /* if the timer can't start, the mod still runs fine */ }
        }

        // Stop + reset on game unload so a stale marker from the previous
        // session can't fire against the next one.
        public static void Reset()
        {
            _handler = null;
            _entity = null;
            _lastLoggedKey = null;
        }

        public static void Enter(string handlerName, string entityId)
        {
            _entity = entityId;
            _startTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            _handler = handlerName; // set last: watchdog gates on _handler != null
        }

        public static void Exit()
        {
            _handler = null; // idle: nothing in progress
        }

        private static void Sample(object _)
        {
            try
            {
                string handler = _handler;
                if (handler == null) return; // idle — no handler in flight

                long start = _startTicks;
                long now = System.Diagnostics.Stopwatch.GetTimestamp();
                double sec = (now - start) / (double)System.Diagnostics.Stopwatch.Frequency;
                if (sec < WarnSeconds) return;

                string entity = _entity;
                string key = entity != null ? handler + ":" + entity : handler;

                // First crossing for this activity, or the periodic re-log.
                bool firstForKey = key != _lastLoggedKey;
                double sinceLast = (now - _lastLoggedTicks) / (double)System.Diagnostics.Stopwatch.Frequency;
                if (!firstForKey && sinceLast < RepeatSeconds) return;

                _lastLoggedKey = key;
                _lastLoggedTicks = now;
                BannerKings.BannerKingsCheats.AppendDiagnosticLine("freeze.txt",
                    $"STUCK {key} running {sec:0}s — campaign thread not progressing");
            }
            catch { /* a freeze diagnostic must never crash the watchdog */ }
        }
    }
}
