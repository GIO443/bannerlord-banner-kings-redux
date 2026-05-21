using System;
using System.IO;
using System.Text;
using System.Threading;

namespace BannerKings.Utils
{
    /// <summary>
    /// Diagnostic instrumentation for tracing freeze locations: appends to a
    /// dedicated file with immediate flush, so when the game hangs the last
    /// line in the file pinpoints the method that entered but never exited.
    ///
    /// One-shot per session: each launch truncates the file so the trace
    /// reflects only the freezing session. Cheap; uses a lock to serialize
    /// writes from any thread. No buffering, no async queue — flush latency
    /// is the whole point.
    /// </summary>
    internal static class BKFreezeTrace
    {
        private static readonly object _lock = new object();
        private static string _path;
        private static bool _initialized;

        private static void EnsureInit()
        {
            if (_initialized) return;
            lock (_lock)
            {
                if (_initialized) return;
                _initialized = true;
                try
                {
                    var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    var dir = Path.Combine(docs, "Mount and Blade II Bannerlord", "Configs", "ModLogs");
                    Directory.CreateDirectory(dir);
                    _path = Path.Combine(dir, "BK_freeze_trace.txt");
                    File.WriteAllText(_path,
                        "=== BK freeze trace started " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===" + Environment.NewLine);
                }
                catch { _path = null; }
            }
        }

        public static void Log(string line)
        {
            EnsureInit();
            if (_path == null) return;
            try
            {
                lock (_lock)
                {
                    File.AppendAllText(_path,
                        "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] T" + Thread.CurrentThread.ManagedThreadId + " " + line + Environment.NewLine);
                }
            }
            catch { /* never throw from diagnostic */ }
        }

        public static void Enter(string tag) => Log("ENTER " + tag);
        public static void Exit(string tag) => Log("EXIT  " + tag);
    }
}
