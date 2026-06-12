using System;
using System.IO;
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
    // Output (BK_freeze.txt) — four line kinds:
    //   ARMED ...                                    once, when detection turns on
    //   alive — heap NMB ...; last handler X         heartbeat every ~15s (proof of
    //                                                life + memory trend + black box)
    //   STUCK <handler>:<entity> running Ns ...      an instrumented BK handler is
    //                                                stuck right now
    //   RUNTIME STALL — ... frozen for Ns ...        EVERY managed thread (incl. this
    //                                                watchdog) was suspended — a full
    //                                                GC or OS suspension, not a single
    //                                                handler. Logged when it recovers.
    // Why heartbeat + stall detection: a freeze can be INVISIBLE to the STUCK
    // path two ways — (1) it is not inside an instrumented Enter/Exit handler
    // (a Harmony patch, a replaced model, vanilla decision/save-load), so the
    // marker is null; or (2) it is a runtime-wide GC/suspension that freezes
    // the watchdog thread too, so it cannot write during the stall. The
    // heartbeat's GAP localizes (1) and (2) in real time even across a force-
    // quit, the RUNTIME STALL line catches (2) on recovery, and the memory
    // figures expose the unbounded-allocation growth behind a late-campaign
    // GC storm.
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

        // True when no bracketed handler is currently in-flight. Lets a low-level
        // bracket (e.g. the navmesh FindReachablePoint guard) set its marker ONLY
        // when called from untracked code — so it names the otherwise-invisible
        // untracked hang without clobbering an outer BK handler that called it.
        public static bool IsIdle => _handler == null;

        // When true, a CONFIRMED hang (a handler stuck past CrashSeconds while
        // the GC is frozen — proof the campaign thread is wedged in native code,
        // not merely slow) is escalated to a hard process crash: a full HTML
        // diagnostic report is written to the Crashes folder and the process is
        // terminated via Environment.FailFast. This converts a silent infinite
        // freeze (force-quit, lose the session, get one log line) into a
        // recoverable crash with a report that pinpoints the move that hung.
        // Rides on _enabled (no watchdog thread = nothing to escalate).
        private static volatile bool _crashOnFreeze = true;
        public static void SetCrashOnFreeze(bool on) => _crashOnFreeze = on;

        // Rich, human-readable context for the move currently in progress
        // (party, target, position, gate, nav type, GetDistance values). Set
        // by the SetMove* prefixes ONLY when _enabled, on the campaign thread
        // (safe). The watchdog dumps it verbatim into the crash HTM, so the
        // report explains WHY the move hung without the watchdog thread ever
        // touching game state. Cleared on Exit.
        private static volatile string _diagnostic;
        public static void SetMoveContext(string diagnostic)
        {
            if (_enabled) _diagnostic = diagnostic;
        }

        // Stuck past this (seconds) WITH a frozen GC → confirmed native hang →
        // crash. Far above any legitimate handler and above the STUCK warn
        // threshold, so a slow-but-alive tick (which keeps allocating and
        // advancing gen0) never trips it.
        private const double CrashSeconds = 20.0;
        // One-shot guard so only the first detector wins the crash.
        private static volatile bool _crashing;
        // Captured when a key first crosses the STUCK warn threshold: the gen0
        // GC count and the timestamp, so the sampler can tell a frozen GC
        // (native hang) from an advancing one (slow but alive).
        private static int _stuckStartGen0;
        private static string _stuckGen0Key;

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
        // Serializes the synchronous file writes (watchdog thread + the
        // campaign thread via WatchSlow can both write).
        private static readonly object _fileLock = new object();

        // Synchronous, direct-to-disk writer. Bypasses the buffered + async
        // diagnostic queue ON PURPOSE: a freeze almost always ends in a
        // force-quit, which kills the background writer thread before its
        // queued lines reach disk — losing exactly the STUCK line we need.
        // The watchdog runs on its own thread so a blocking File.AppendAllText
        // here never touches the (frozen) campaign thread. WatchSlow calls
        // this too so BK_slow.txt is equally force-quit-durable.
        public static void WriteLineDirect(string filename, string line)
        {
            try
            {
                string dir = BannerKings.BannerKingsCheats.DiagnosticDir;
                string path = Path.Combine(dir, "BK_" + filename);
                string stamped = $"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}";
                lock (_fileLock)
                {
                    try { if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); } catch { }
                    File.AppendAllText(path, stamped);
                }
            }
            catch { /* a freeze diagnostic must never crash its caller */ }
        }

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
        // Heartbeat cadence — proof-of-life + memory trend + force-quit black
        // box. Coarse enough to keep the file small over a long session.
        private const double HeartbeatSeconds = 15.0;
        // If two consecutive samples are more than this far apart in real
        // time, the Timer thread itself was suspended — i.e. every managed
        // thread was frozen (full GC or OS suspension). The period is 1s, so
        // any gap past a few seconds is a genuine runtime stall.
        private const double RuntimeStallSeconds = 4.0;

        // Hot-path state. volatile so the watchdog thread sees writes from
        // the campaign thread without a lock. We accept benign races: the
        // worst case is the watchdog reading a half-updated (handler,entity,
        // start) triple for one sample, which at most mislabels a single
        // 1s sample of a real multi-second stall — self-corrects next tick.
        private static volatile string _handler;   // compile-time constant, no alloc
        private static volatile string _entity;     // StringId field ref, no alloc
        private static long _startTicks;            // Stopwatch.GetTimestamp at Enter
        // Last handler that STARTED, kept through Exit (unlike _handler which
        // clears on Exit). Gives the heartbeat / stall lines context: "what
        // BK code ran most recently before the freeze". _lastEntity carries the
        // StringId so the heartbeat names the exact entity (e.g. which party's
        // hourly think hung), not just the handler.
        private static volatile string _lastHandler;
        private static volatile string _lastEntity;

        // Watchdog bookkeeping (touched only by the watchdog thread, except
        // the reset on enable).
        private static long _lastLoggedTicks;
        private static string _lastLoggedKey;
        private static long _lastSampleTicks;       // for runtime-stall detection
        private static long _lastHeartbeatTicks;
        private static int _lastGen2;               // gen2 GC count at previous sample
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
                    // Reset cadence baselines so the first sample after enabling
                    // doesn't read a stale _lastSampleTicks from a prior enable
                    // and false-fire a RUNTIME STALL.
                    _lastSampleTicks = 0;
                    _lastHeartbeatTicks = 0;
                    _stuckGen0Key = null;
                    try { _lastGen2 = GC.CollectionCount(2); } catch { _lastGen2 = 0; }
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
                    _lastHandler = null;
                    _lastEntity = null;
                    _lastLoggedKey = null;
                    if (_timer != null)
                    {
                        try { _timer.Dispose(); } catch { }
                        _timer = null;
                    }
                }
            }
            // Write an "armed" line (outside the lock) whenever detection is
            // enabled — both when the user flips the toggle on AND at each
            // session start (the load hook calls SetEnabled every load, after
            // the per-save log wipe). This guarantees BK_freeze.txt EXISTS as
            // soon as detection is on, so the user can confirm it's active and
            // find the file. If a freeze never happens it just keeps this line.
            if (on)
                WriteLineDirect("freeze.txt",
                    "freeze watchdog ARMED — this file records any BK tick handler stuck over 5s. " +
                    "If it only ever has this line, no BK handler froze (the cause is elsewhere).");
        }

        // Clear any stale in-flight marker (e.g. on game unload) without
        // changing the enabled state.
        public static void Reset()
        {
            _handler = null;
            _entity = null;
            _lastHandler = null;
            _lastEntity = null;
            _lastLoggedKey = null;
        }

        public static void Enter(string handlerName, string entityId)
        {
            if (!_enabled) return;
            _entity = entityId;
            _startTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            _lastHandler = handlerName; // sticky: kept through Exit for context
            _lastEntity = entityId;     // sticky entity for the heartbeat
            _handler = handlerName; // set last: watchdog gates on _handler != null
        }

        // Compact memory snapshot for the diagnostic lines. Managed heap +
        // process working set (catches native growth too) + GC counts. Called
        // only from the watchdog thread at heartbeat/stall cadence, so the
        // Process allocation is irrelevant.
        private static string MemStats()
        {
            try
            {
                long heapMb = GC.GetTotalMemory(false) / 1048576;
                long wsMb = 0;
                try { using (var p = System.Diagnostics.Process.GetCurrentProcess()) wsMb = p.WorkingSet64 / 1048576; } catch { }
                return $"heap {heapMb}MB, workingSet {wsMb}MB, GCs g0={GC.CollectionCount(0)} g1={GC.CollectionCount(1)} g2={GC.CollectionCount(2)}";
            }
            catch { return "mem stats unavailable"; }
        }

        public static void Exit()
        {
            if (!_enabled) return;
            _handler = null; // idle: nothing in progress
            _diagnostic = null;
        }

        private static void Sample(object _)
        {
            try
            {
                if (!_enabled) return;
                double freq = (double)System.Diagnostics.Stopwatch.Frequency;
                long now = System.Diagnostics.Stopwatch.GetTimestamp();

                // (1) RUNTIME STALL — the Timer is set to fire every ~1s. If
                // the gap since the previous sample is far larger, this thread
                // (and therefore every managed thread, including the campaign
                // thread) was suspended for that long: a full GC or an OS
                // process suspension, NOT a single slow handler. Fires on the
                // first sample to run after the freeze releases.
                if (_lastSampleTicks != 0)
                {
                    double gap = (now - _lastSampleTicks) / freq;
                    if (gap > RuntimeStallSeconds)
                    {
                        int g2now;
                        try { g2now = GC.CollectionCount(2); } catch { g2now = _lastGen2; }
                        WriteLineDirect("freeze.txt",
                            $"RUNTIME STALL — every managed thread was frozen for {gap:0}s " +
                            $"(full GC or OS suspension, NOT a single handler). gen2 GCs +{g2now - _lastGen2} during the stall. " +
                            $"{MemStats()}. Last BK handler before stall: {(_lastHandler == null ? "(none)" : (_lastEntity != null ? _lastHandler + ":" + _lastEntity : _lastHandler))}.");
                    }
                }
                _lastSampleTicks = now;
                try { _lastGen2 = GC.CollectionCount(2); } catch { }

                // (2) In-flight STUCK — an instrumented BK handler that is
                // taking too long right now (its Exit hasn't run).
                string handler = _handler;
                if (handler != null)
                {
                    double sec = (now - _startTicks) / freq;
                    if (sec >= WarnSeconds)
                    {
                        string entity = _entity;
                        string key = entity != null ? handler + ":" + entity : handler;
                        bool firstForKey = key != _lastLoggedKey;
                        double sinceLast = (now - _lastLoggedTicks) / freq;
                        if (firstForKey || sinceLast >= RepeatSeconds)
                        {
                            _lastLoggedKey = key;
                            _lastLoggedTicks = now;
                            WriteLineDirect("freeze.txt",
                                $"STUCK {key} running {sec:0}s — campaign thread not progressing. {MemStats()}");
                        }

                        // Capture the gen0 baseline the first time THIS key is
                        // seen stuck, so we can tell a frozen GC (native hang)
                        // from one that's still advancing (slow but alive).
                        if (key != _stuckGen0Key)
                        {
                            _stuckGen0Key = key;
                            try { _stuckStartGen0 = GC.CollectionCount(0); } catch { _stuckStartGen0 = -1; }
                        }

                        // Crash escalation: stuck past CrashSeconds AND gen0 has
                        // not advanced since it first went stuck → the campaign
                        // thread is wedged in native code (a slow-but-alive tick
                        // would have allocated and bumped gen0). Write the HTM
                        // and hard-crash.
                        if (_crashOnFreeze && !_crashing && sec >= CrashSeconds)
                        {
                            int g0now;
                            try { g0now = GC.CollectionCount(0); } catch { g0now = _stuckStartGen0; }
                            // Frozen GC = native hang. A live tick does DOZENS of
                            // gen0 GCs over this window (see heartbeat g0 deltas),
                            // so allow a drift of 1 for a stray background
                            // allocation while still being unambiguous.
                            if (_stuckStartGen0 >= 0 && (g0now - _stuckStartGen0) <= 1)
                            {
                                _crashing = true;
                                CrashWithReport(key, sec, MemStats());
                            }
                        }
                    }
                }

                // (3) Heartbeat — proof of life, memory trend, and the force-
                // quit black box (its GAP localizes a freeze that the STUCK
                // path can't see because it's outside an instrumented handler).
                if (_lastHeartbeatTicks == 0 || (now - _lastHeartbeatTicks) / freq >= HeartbeatSeconds)
                {
                    _lastHeartbeatTicks = now;
                    string curDesc = handler == null ? "(idle)"
                        : (_entity != null ? handler + ":" + _entity : handler);
                    string lastDesc = _lastHandler == null ? "(none)"
                        : (_lastEntity != null ? _lastHandler + ":" + _lastEntity : _lastHandler);
                    WriteLineDirect("freeze.txt",
                        $"alive — {MemStats()}; current {curDesc}; last {lastDesc}");
                }
            }
            catch { /* a freeze diagnostic must never crash the watchdog */ }
        }

        // Write a self-contained HTML diagnostic report for a confirmed hang,
        // then hard-terminate the process. Runs on the watchdog thread (the
        // campaign thread is wedged), so it touches NO game state — everything
        // it reports was captured on the campaign thread into volatile fields
        // (handler/entity at Enter, the rich move snapshot via SetMoveContext).
        // We generate our own HTM rather than route through the game's crash
        // window because that window needs a managed throw on the main thread,
        // which is frozen.
        private static void CrashWithReport(string key, double sec, string mem)
        {
            string htmPath = null;
            try
            {
                string dir = BannerKings.BannerKingsCheats.DiagnosticDir;
                try { if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); } catch { }
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                htmPath = Path.Combine(dir, "BK_freeze_crash_" + stamp + ".htm");

                string diag = _diagnostic;
                string lastDesc = _lastHandler == null ? "(none)"
                    : (_lastEntity != null ? _lastHandler + ":" + _lastEntity : _lastHandler);

                var sb = new System.Text.StringBuilder(2048);
                sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>Banner Kings — Freeze Auto-Crash</title>");
                sb.Append("<style>body{font-family:Segoe UI,Arial,sans-serif;background:#1b1b1b;color:#ddd;margin:24px;}");
                sb.Append("h1{color:#e0a040;}h2{color:#8ab4f8;border-bottom:1px solid #444;padding-bottom:4px;margin-top:24px;}");
                sb.Append("code,pre{background:#262626;color:#cfe;padding:2px 6px;border-radius:3px;white-space:pre-wrap;}");
                sb.Append("table{border-collapse:collapse;}td{padding:3px 12px 3px 0;vertical-align:top;}.k{color:#999;}</style></head><body>");
                sb.Append("<h1>Banner Kings — Freeze Auto-Crash</h1>");
                sb.Append("<p>The Banner Kings freeze watchdog detected the campaign thread wedged inside a single "
                    + "operation with the garbage collector frozen — a genuine native hang that would never recover. "
                    + "Rather than leave the game frozen, BK wrote this report and terminated the process so you can "
                    + "restart and send this file with your bug report.</p>");

                sb.Append("<h2>Summary</h2><table>");
                Row(sb, "Time", System.Security.SecurityElement.Escape(DateTime.Now.ToString("u")));
                Row(sb, "Stuck operation", System.Security.SecurityElement.Escape(key));
                Row(sb, "Stuck duration", sec.ToString("0") + " s (GC frozen the whole time)");
                Row(sb, "Last BK handler before hang", System.Security.SecurityElement.Escape(lastDesc));
                Row(sb, "Memory", System.Security.SecurityElement.Escape(mem));
                sb.Append("</table>");

                sb.Append("<h2>Move context</h2>");
                if (string.IsNullOrEmpty(diag))
                    sb.Append("<p><i>No move snapshot was captured — the hang was not inside an instrumented SetMove* call "
                        + "(it is in another handler / vanilla decision / save-load). The stuck-operation name above still "
                        + "identifies it.</i></p>");
                else
                    sb.Append("<pre>" + System.Security.SecurityElement.Escape(diag) + "</pre>");

                sb.Append("<h2>What to do</h2><p>Send <code>" + System.Security.SecurityElement.Escape(Path.GetFileName(htmPath))
                    + "</code> (and <code>BK_freeze.txt</code>) from <code>%LOCALAPPDATA%\\BannerKings\\ModLogs</code> "
                    + "(this file's own folder). The move context above names the "
                    + "party and target whose pathfind hung — that is exactly what is needed to fix the root cause.</p>");
                sb.Append("</body></html>");

                lock (_fileLock)
                {
                    File.WriteAllText(htmPath, sb.ToString());
                }

                WriteLineDirect("freeze.txt",
                    "FATAL — confirmed native hang on " + key + " (" + sec.ToString("0") + "s, GC frozen). "
                    + "Hard-crashing with diagnostic report: " + Path.GetFileName(htmPath));
            }
            catch { /* never let report generation prevent the crash itself */ }

            // Hard crash. FailFast terminates immediately from this (watchdog)
            // thread regardless of the wedged campaign thread, surfacing a
            // crash the user can restart from instead of an infinite freeze.
            try
            {
                Environment.FailFast("Banner Kings freeze watchdog: confirmed native hang on " + key
                    + (htmPath != null ? " — see " + htmPath : ""));
            }
            catch { }
        }

        private static void Row(System.Text.StringBuilder sb, string k, string v)
        {
            sb.Append("<tr><td class=\"k\">").Append(k).Append("</td><td>").Append(v).Append("</td></tr>");
        }
    }
}
