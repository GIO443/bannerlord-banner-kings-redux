using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;

namespace BannerKings.Utils
{
    /// <summary>
    /// Diagnostic logger that captures every first-chance exception touching BK
    /// or BannerKings-namespaced code, BEFORE any TargetInvocationException wrap.
    /// Output: %USERPROFILE%/Documents/Mount and Blade II Bannerlord/Configs/ModLogs/bk-firstchance.log
    /// </summary>
    public static class BKDiagnostics
    {
        private static int _installed;
        private static string _path;
        private const long MaxBytes = 4 * 1024 * 1024; // 4 MB cap; truncate older

        // Async write queue. The first-chance handler runs ON THE THROWING
        // THREAD — for vanilla / BK code that's the campaign thread. Doing
        // synchronous File.AppendAllText (with a possible rotation read+write)
        // there means every first-chance exception costs the campaign loop
        // however long the disk takes to flush, multiplied by however many
        // exceptions storm in a single tick. Earlier sessions captured 871
        // copies of the same vanilla NRE in seconds — at OneDrive-sync /
        // antivirus latency that's tens of seconds of campaign-thread freeze
        // attributable just to the diagnostic logger.
        //
        // The queue + background drainer pattern matches BannerKingsCheats's
        // tick_trace writer: thread-side enqueue is concurrent and lock-free,
        // the drainer thread does the file I/O off the campaign thread.
        private static readonly ConcurrentQueue<string> _queue = new ConcurrentQueue<string>();
        private static volatile bool _writerStarted;
        private static readonly object _writerStartLock = new object();
        private static readonly ManualResetEventSlim _writerWake = new ManualResetEventSlim(false);

        // Per-signature suppression so a single repeating exception (vanilla
        // or otherwise) cannot saturate the queue or the log. Top-of-stack
        // frame + exception type identifies a signature; we keep a count and
        // drop entries past the cap, periodically resetting to allow drift.
        private static readonly ConcurrentDictionary<string, int> _signatureCounts
            = new ConcurrentDictionary<string, int>();
        private const int MaxPerSignature = 32;
        private static long _lastSignatureResetTicks = DateTime.UtcNow.Ticks;
        private static readonly TimeSpan SignatureResetInterval = TimeSpan.FromMinutes(10);

        public static void Install()
        {
            if (Interlocked.Exchange(ref _installed, 1) != 0) return;
            try
            {
                var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                var dir = Path.Combine(docs, "Mount and Blade II Bannerlord", "Configs", "ModLogs");
                Directory.CreateDirectory(dir);
                _path = Path.Combine(dir, "bk-firstchance.log");

                Enqueue("=== BK FirstChance logger installed at " + DateTime.Now.ToString("o") + " ===" + Environment.NewLine);
                AppDomain.CurrentDomain.FirstChanceException += OnFirstChance;
            }
            catch
            {
                // Diagnostics must never throw.
            }
        }

        private static void OnFirstChance(object sender, FirstChanceExceptionEventArgs e)
        {
            try
            {
                if (e?.Exception == null) return;
                var ex = e.Exception;
                var stack = ex.StackTrace ?? string.Empty;
                var msg = ex.Message ?? string.Empty;

                // Tight relevance filter: log only when BK code is on the
                // stack (or in any inner exception's stack), or when the
                // message itself names BK. The previous filter used catch-all
                // type checks (`is NullReferenceException` etc.), which match
                // every NRE in the process — vanilla, NavalDLC, every other
                // mod — and were the source of the 871-NRE storms. Vanilla's
                // own try/caught NREs are not BK's problem and shouldn't
                // freeze the campaign loop on disk I/O.
                if (!StackTouchesBK(ex, stack, msg)) return;

                // Per-signature suppression. A signature is the exception
                // type + the first BannerKings frame on the outer stack
                // (falls back to the raw top frame). When the same
                // signature has been logged MaxPerSignature times within
                // the reset interval, further occurrences are dropped.
                MaybeResetSignatureCounts();
                string sig = BuildSignature(ex, stack);
                int newCount = _signatureCounts.AddOrUpdate(sig, 1, (_, v) => v + 1);
                if (newCount > MaxPerSignature)
                {
                    if (newCount == MaxPerSignature + 1)
                    {
                        // One-time notice that this signature is now suppressed.
                        Enqueue("[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] [suppressed past " + MaxPerSignature + "] " + sig + Environment.NewLine);
                    }
                    return;
                }

                var sb = new StringBuilder(2048);
                sb.Append("[").Append(DateTime.Now.ToString("HH:mm:ss.fff")).Append("] ");
                sb.Append(ex.GetType().FullName).Append(": ").AppendLine(msg);
                AppendInner(sb, ex.InnerException, depth: 1);
                if (!string.IsNullOrEmpty(stack))
                {
                    sb.AppendLine(stack);
                }
                sb.AppendLine();
                Enqueue(sb.ToString());
            }
            catch
            {
                // never propagate
            }
        }

        private static bool StackTouchesBK(Exception ex, string stack, string msg)
        {
            if (stack.IndexOf("BannerKings", StringComparison.Ordinal) >= 0) return true;
            if (msg.IndexOf("BannerKings", StringComparison.Ordinal) >= 0) return true;
            // Walk inner stacks — TargetInvocationException wraps the real
            // inner method, and its inner.StackTrace is where BK-patched
            // method bodies show up.
            var inner = ex.InnerException;
            int depth = 0;
            while (inner != null && depth++ < 8)
            {
                var innerStack = inner.StackTrace;
                if (!string.IsNullOrEmpty(innerStack)
                    && innerStack.IndexOf("BannerKings", StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
                inner = inner.InnerException;
            }
            return false;
        }

        private static string BuildSignature(Exception ex, string stack)
        {
            string topFrame = "?";
            if (!string.IsNullOrEmpty(stack))
            {
                // Prefer the first frame mentioning BannerKings; falls back to
                // the raw first frame. Keeps signatures stable across re-throws
                // that prepend the same vanilla glue.
                int bkAt = stack.IndexOf("BannerKings", StringComparison.Ordinal);
                if (bkAt >= 0)
                {
                    int eol = stack.IndexOf('\n', bkAt);
                    topFrame = stack.Substring(bkAt, (eol < 0 ? stack.Length : eol) - bkAt).Trim();
                }
                else
                {
                    int eol = stack.IndexOf('\n');
                    topFrame = stack.Substring(0, eol < 0 ? stack.Length : eol).Trim();
                }
            }
            return ex.GetType().FullName + " @ " + topFrame;
        }

        private static void MaybeResetSignatureCounts()
        {
            long nowTicks = DateTime.UtcNow.Ticks;
            long last = Interlocked.Read(ref _lastSignatureResetTicks);
            if (new TimeSpan(nowTicks - last) < SignatureResetInterval) return;
            // CAS so only one caller resets per window.
            if (Interlocked.CompareExchange(ref _lastSignatureResetTicks, nowTicks, last) == last)
            {
                _signatureCounts.Clear();
            }
        }

        private static void AppendInner(StringBuilder sb, Exception inner, int depth)
        {
            int safety = 0;
            while (inner != null && safety++ < 8)
            {
                sb.Append("  -> [inner ").Append(depth).Append("] ");
                sb.Append(inner.GetType().FullName).Append(": ").AppendLine(inner.Message ?? string.Empty);
                if (!string.IsNullOrEmpty(inner.StackTrace)) sb.AppendLine(inner.StackTrace);
                inner = inner.InnerException;
                depth++;
            }
        }

        private static void Enqueue(string text)
        {
            if (string.IsNullOrEmpty(text) || _path == null) return;
            EnsureWriter();
            _queue.Enqueue(text);
            try { _writerWake.Set(); } catch { }
        }

        private static void EnsureWriter()
        {
            if (_writerStarted) return;
            lock (_writerStartLock)
            {
                if (_writerStarted) return;
                var t = new Thread(WriterLoop)
                {
                    IsBackground = true,
                    Name = "BK FirstChance Writer",
                };
                t.Start();
                _writerStarted = true;
            }
        }

        private static void WriterLoop()
        {
            while (true)
            {
                try
                {
                    if (_queue.TryDequeue(out var text))
                    {
                        DoWrite(text);
                    }
                    else
                    {
                        _writerWake.Wait(200);
                        _writerWake.Reset();
                    }
                }
                catch
                {
                    try { Thread.Sleep(100); } catch { }
                }
            }
        }

        private static void DoWrite(string text)
        {
            if (_path == null) return;
            try
            {
                if (File.Exists(_path))
                {
                    var fi = new FileInfo(_path);
                    if (fi.Length > MaxBytes)
                    {
                        // simple rotation: truncate to last 1 MB
                        using (var fs = new FileStream(_path, FileMode.Open, FileAccess.ReadWrite))
                        {
                            long keep = 1024 * 1024;
                            long start = fs.Length - keep;
                            if (start > 0)
                            {
                                fs.Position = start;
                                var buf = new byte[keep];
                                int read = fs.Read(buf, 0, buf.Length);
                                fs.SetLength(0);
                                fs.Position = 0;
                                fs.Write(buf, 0, read);
                            }
                        }
                    }
                }
                File.AppendAllText(_path, text);
            }
            catch
            {
                // disk error — drop, don't crash the writer
            }
        }
    }
}
