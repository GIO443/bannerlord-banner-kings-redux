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
    // The first STUCK line names the exact handler + entity that froze the
    // game, written the moment the stall crosses the threshold — no tester
    // timing required, and it survives even a never-returning hang.
    //
    // OPT-IN. Off by default. Gated behind the MCM toggle
    // "Diagnostics → Enable Freeze Detection". When disabled, NO background
    // thread exists (the Timer is never created / is disposed), Enter/Exit
    // no-op on a single volatile-bool read, and the per-handler stopwatches
    // that feed it are not even started. The toggle's setter calls
    // SetEnabled() so flipping it in-game starts/stops the watchdog live, no
    // restart needed.
    public static class FreezeWatchdog
    {
        // Master gate. Read on the hot path (Enter/Exit) and by the sampler.
        private static volatile bool _enabled;
        public static bool Enabled => _enabled;

        // Per-handler stopwatch gate: time a tick body when EITHER the freeze
        // detector or the hourly-perf logger wants it. Mirrors the cost of the
        // pre-existing LogHourlyTickPerf check (one singleton property read),
        // so when both are off the handler pays only this branch — no
        // Stopwatch is started.
        public static bool TimingWanted
        {
            get
            {
                if (_enabled) return true;
                try { return BannerKings.Settings.BannerKingsSettings.Instance.LogHourlyTickPerf; }
                catch { return false; }
            }
        }
        // Serializes timer create/dispose against concurrent SetEnabled calls.
        private static readonly object _lifecycle = new object();

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

        // Watchdog bookkeeping (touched only by the watchdog thread).
        private static long _lastLoggedTicks;
        private static string _lastLoggedKey;
        private static Timer _timer;

        // Turn the watchdog on/off. Driven by the MCM toggle's setter (live)
        // and synced on game load. When turning off we dispose the Timer so
        // NO background thread lingers; when turning on we create it. Safe to
        // call repeatedly and from any thread.
        public static void SetEnabled(bool on)
        {
            lock (_lifecycle)
            {
                if (on)
                {
                    _enabled = true;
                    if (_timer == null)
                    {
                        try { _timer = new Timer(Sample, null, SampleMillis, SampleMillis); }
                        catch { /* if the timer can't start, the mod still runs fine */ }
                    }
                }
                else
                {
                    _enabled = false;
                    _handler = null;
                    _lastLoggedKey = null;
                    if (_timer != null)
                    {
                        try { _timer.Dispose(); } catch { }
                        _timer = null;
                    }
                }
            }
        }

        // Clear any stale in-flight marker (e.g. on game unload) without
        // changing the enabled state.
        public static void Reset()
        {
            _handler = null;
            _entity = null;
            _lastLoggedKey = null;
        }

        public static void Enter(string handlerName, string entityId)
        {
            if (!_enabled) return;
            _entity = entityId;
            _startTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            _handler = handlerName; // set last: watchdog gates on _handler != null
        }

        public static void Exit()
        {
            if (!_enabled) return;
            _handler = null; // idle: nothing in progress
        }

        private static void Sample(object _)
        {
            try
            {
                if (!_enabled) return;
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
