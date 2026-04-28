using System;
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
        private static readonly object _lock = new object();
        private static string _path;
        private const long MaxBytes = 4 * 1024 * 1024; // 4 MB cap; truncate older

        public static void Install()
        {
            if (Interlocked.Exchange(ref _installed, 1) != 0) return;
            try
            {
                var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                var dir = Path.Combine(docs, "Mount and Blade II Bannerlord", "Configs", "ModLogs");
                Directory.CreateDirectory(dir);
                _path = Path.Combine(dir, "bk-firstchance.log");

                Append("=== BK FirstChance logger installed at " + DateTime.Now.ToString("o") + " ===");
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
                var stack = e.Exception.StackTrace ?? string.Empty;
                var msg = e.Exception.Message ?? string.Empty;

                // Filter to exceptions that touch BK or are TargetInvocationException
                // wrappers around something that does. Otherwise the log floods with
                // benign vanilla try/catch noise.
                bool relevant =
                    stack.IndexOf("BannerKings", StringComparison.Ordinal) >= 0 ||
                    msg.IndexOf("BannerKings", StringComparison.Ordinal) >= 0 ||
                    e.Exception is System.Reflection.TargetInvocationException ||
                    e.Exception is NullReferenceException ||
                    e.Exception is ArgumentNullException ||
                    e.Exception is MissingMethodException ||
                    e.Exception is MissingFieldException ||
                    e.Exception is TypeLoadException ||
                    e.Exception is TypeInitializationException;
                if (!relevant) return;

                var sb = new StringBuilder(2048);
                sb.Append("[").Append(DateTime.Now.ToString("HH:mm:ss.fff")).Append("] ");
                sb.Append(e.Exception.GetType().FullName).Append(": ").AppendLine(msg);
                AppendInner(sb, e.Exception.InnerException, depth: 1);
                if (!string.IsNullOrEmpty(stack))
                {
                    sb.AppendLine(stack);
                }
                sb.AppendLine();
                Append(sb.ToString());
            }
            catch
            {
                // never propagate
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

        private static void Append(string text)
        {
            if (_path == null) return;
            lock (_lock)
            {
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
                    File.AppendAllText(_path, text + Environment.NewLine);
                }
                catch
                {
                }
            }
        }
    }
}
