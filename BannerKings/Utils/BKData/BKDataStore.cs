using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

namespace BannerKings.Utils.BKData
{
    /// <summary>
    /// Cross-module XML data store for BK flavor content. Other mods drop files
    /// under their own <c>ModuleData/BKData/*.xml</c>; BK enumerates every loaded
    /// module in load order and merges rows by <c>(category, id)</c>, last
    /// writer wins. A setting-overhaul mod can therefore replace any BK row
    /// without recompiling BK.
    ///
    /// Wire-up: <see cref="Scan"/> is called once at the top of
    /// <c>BannerKingsConfig.Initialize()</c>, before any <c>Default*.Initialize()</c>.
    /// Subsequent <c>Default*</c> consumers read rows via <see cref="GetRows"/>.
    ///
    /// File shape (one category per file, root element names the category):
    /// <code>
    /// &lt;faiths&gt;
    ///   &lt;faith id="darusosian" flavor="Henotheistic"&gt; ... &lt;/faith&gt;
    /// &lt;/faiths&gt;
    /// </code>
    /// </summary>
    public sealed class BKDataStore
    {
        private static BKDataStore _instance;
        public static BKDataStore Instance => _instance ??= new BKDataStore();

        // category -> id -> element. Dictionary key collisions intentionally
        // overwrite — that's how load-order override works.
        private readonly Dictionary<string, Dictionary<string, XElement>> _rows
            = new Dictionary<string, Dictionary<string, XElement>>(StringComparer.OrdinalIgnoreCase);

        private readonly List<string> _diagnostics = new List<string>();
        private bool _scanned;

        /// <summary>Diagnostic messages from the last <see cref="Scan"/> (load errors,
        /// schema warnings). Read by <c>BKDataPatch</c>-style code if you want to
        /// surface them in the log; cheap to ignore otherwise.</summary>
        public IReadOnlyList<string> Diagnostics => _diagnostics;

        /// <summary>Append a diagnostic from a consumer's resolve pass — e.g.
        /// <c>DefaultFaiths</c> noting an unknown rite key. Centralised so a
        /// future "surface BK load issues at boot" UI has one list to read.</summary>
        public void AddDiagnostic(string message)
        {
            if (!string.IsNullOrEmpty(message)) _diagnostics.Add(message);
        }

        /// <summary>Walk every loaded module's <c>ModuleData/BKData/</c> directory,
        /// parse every <c>*.xml</c>, merge rows by <c>(category, id)</c>. Idempotent;
        /// repeated calls re-scan from disk (cheap — pilot has &lt; 50 rows total).</summary>
        public void Scan()
        {
            _rows.Clear();
            _diagnostics.Clear();
            _scanned = true;

            foreach (var moduleDir in EnumerateModuleDirectories())
            {
                var bkDataDir = Path.Combine(moduleDir, "ModuleData", "BKData");
                if (!Directory.Exists(bkDataDir)) continue;
                foreach (var xmlPath in Directory.GetFiles(bkDataDir, "*.xml"))
                {
                    LoadFile(xmlPath);
                }
            }
        }

        /// <summary>All rows in the given category, in arbitrary order. Empty if
        /// the category was never declared by any module.</summary>
        public IEnumerable<XElement> GetRows(string category)
        {
            EnsureScanned();
            if (_rows.TryGetValue(category, out var dict))
                return dict.Values;
            return Enumerable.Empty<XElement>();
        }

        /// <summary>Single row by id, or null. Useful when one entity references
        /// another by id and you want to resolve eagerly.</summary>
        public XElement GetRow(string category, string id)
        {
            EnsureScanned();
            if (_rows.TryGetValue(category, out var dict) &&
                dict.TryGetValue(id, out var row))
                return row;
            return null;
        }

        public bool HasCategory(string category)
        {
            EnsureScanned();
            return _rows.ContainsKey(category);
        }

        private void EnsureScanned()
        {
            // First-touch lazy scan covers code paths that read the store before
            // BannerKingsConfig.Initialize() has run (none today, but a save
            // load that bypasses the normal init order shouldn't NRE).
            if (!_scanned) Scan();
        }

        private void LoadFile(string path)
        {
            try
            {
                var doc = XDocument.Load(path);
                var root = doc.Root;
                if (root == null)
                {
                    _diagnostics.Add($"[BKData] {path}: empty document");
                    return;
                }

                // File-level module gate. A file whose root element declares
                // requires_module="<moduleId>" is loaded only when that module
                // is present per BannerKings.Utils.ModCompat. This is how
                // total-conversion compat data (Shokuho faiths/governments/etc.)
                // stays registered on the TC campaign but inert on a vanilla
                // Calradia campaign — without this, governments and successions
                // (which have no culture-resolution gate of their own) would
                // appear in the vanilla government picker. Files with no
                // requires_module attribute load unconditionally, preserving
                // existing BK file behaviour.
                var requiresModule = (string)root.Attribute("requires_module");
                if (!string.IsNullOrEmpty(requiresModule) &&
                    !BannerKings.Utils.ModCompat.IsLoaded(requiresModule))
                {
                    _diagnostics.Add($"[BKData] {path}: skipped (requires module '{requiresModule}', not loaded)");
                    return;
                }

                var category = root.Name.LocalName;
                if (!_rows.TryGetValue(category, out var dict))
                {
                    dict = new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);
                    _rows[category] = dict;
                }

                foreach (var entry in root.Elements())
                {
                    var id = (string)entry.Attribute("id");
                    if (string.IsNullOrEmpty(id))
                    {
                        _diagnostics.Add($"[BKData] {path}: <{entry.Name.LocalName}> missing id attribute, skipped");
                        continue;
                    }
                    // Last writer wins — flavor-mod modules load after BK so
                    // their rows replace BK's defaults when ids collide.
                    dict[id] = entry;
                }
            }
            catch (Exception ex)
            {
                _diagnostics.Add($"[BKData] {path}: parse failed — {ex.Message}");
            }
        }

        /// <summary>Module directories to scan, in load order.
        ///
        /// BK's own module directory is derived from this assembly's on-disk
        /// location and yielded first — that needs neither the module-manager
        /// API nor BasePath, so BK's own data ALWAYS loads even if every
        /// reflective probe fails. Other loaded modules follow (for cross-mod
        /// overrides); being later, they win on id collision.</summary>
        private IEnumerable<string> EnumerateModuleDirectories()
        {
            var dirs = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Add(string d)
            {
                if (string.IsNullOrEmpty(d)) return;
                string full;
                try { full = Path.GetFullPath(d); }
                catch { return; }
                if (seen.Add(full)) dirs.Add(full);
            }

            // BK's own module root. <base>/Modules/<BK>/bin/Win64_Shipping_Client/
            // BannerKings.dll → up three directories. Bulletproof: no API, no
            // hardcoded folder name (this fork's folder is "BannerKings.Redux",
            // not "BannerKings").
            string ownModule = TryGetOwnModuleDir();
            Add(ownModule);

            // Base = the Modules parent. Prefer deriving it from the known-good
            // own-module path; fall back to the BasePath probe.
            string baseDir = null;
            if (!string.IsNullOrEmpty(ownModule))
            {
                var modulesDir = Path.GetDirectoryName(ownModule);
                baseDir = modulesDir != null ? Path.GetDirectoryName(modulesDir) : null;
            }
            if (string.IsNullOrEmpty(baseDir)) baseDir = TryGetBasePath();

            // Other loaded modules, in load order — for cross-mod overrides.
            if (!string.IsNullOrEmpty(baseDir))
            {
                foreach (var id in TryGetLoadedModuleIds())
                {
                    if (!string.IsNullOrEmpty(id))
                        Add(Path.Combine(baseDir, "Modules", id));
                }
            }

            return dirs;
        }

        /// <summary>BK's own module directory, from the executing assembly's
        /// file path. Null only if the assembly has no on-disk location.</summary>
        private static string TryGetOwnModuleDir()
        {
            try
            {
                var loc = Assembly.GetExecutingAssembly().Location;
                if (string.IsNullOrEmpty(loc)) return null;
                // BannerKings.dll → /bin/Win64_Shipping_Client → /bin → module root
                var d = Path.GetDirectoryName(loc);
                d = d != null ? Path.GetDirectoryName(d) : null;
                d = d != null ? Path.GetDirectoryName(d) : null;
                return (!string.IsNullOrEmpty(d) && Directory.Exists(d)) ? d : null;
            }
            catch { return null; }
        }

        private static string TryGetBasePath()
        {
            // TaleWorlds.Library.BasePath.Name resolves to the game install dir
            // with a trailing slash. Path.Combine handles either form, but we
            // strip the slash so the resulting path is canonical.
            try
            {
                var t = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType("TaleWorlds.Library.BasePath", throwOnError: false))
                    .FirstOrDefault(x => x != null);
                if (t != null)
                {
                    var p = t.GetProperty("Name", BindingFlags.Public | BindingFlags.Static);
                    if (p != null)
                    {
                        var v = p.GetValue(null) as string;
                        if (!string.IsNullOrEmpty(v))
                            return v.TrimEnd('\\', '/');
                    }
                }
            }
            catch { }
            // Last-ditch: walk up from the executing assembly until we find a
            // "Modules" sibling. Not pretty but means BK never crash-boots
            // because the BasePath API moved.
            try
            {
                var asm = Assembly.GetExecutingAssembly().Location;
                var dir = Path.GetDirectoryName(asm);
                while (!string.IsNullOrEmpty(dir))
                {
                    if (Directory.Exists(Path.Combine(dir, "Modules")))
                        return dir;
                    dir = Path.GetDirectoryName(dir);
                }
            }
            catch { }
            return string.Empty;
        }

        private static MethodInfo _getModulesMethod;
        private static bool _moduleApiResolved;

        private static IEnumerable<string> TryGetLoadedModuleIds()
        {
            ResolveModuleApi();
            if (_getModulesMethod == null) yield break;

            System.Collections.IEnumerable modules = null;
            try { modules = _getModulesMethod.Invoke(null, null) as System.Collections.IEnumerable; }
            catch { }
            if (modules == null) yield break;

            foreach (var m in modules)
            {
                string id = null;
                try
                {
                    var idProp = m.GetType().GetProperty("Id");
                    if (idProp != null) id = idProp.GetValue(m) as string;
                }
                catch { }
                if (!string.IsNullOrEmpty(id))
                    yield return id;
            }
        }

        private static void ResolveModuleApi()
        {
            if (_moduleApiResolved) return;
            _moduleApiResolved = true;

            // Mirrors ModCompat.ResolveModuleApi(): the type name has moved
            // between ModuleInfo and ModuleHelper across 1.x patch levels.
            string[] typeNames =
            {
                "TaleWorlds.ModuleManager.ModuleInfo",
                "TaleWorlds.ModuleManager.ModuleHelper",
            };
            string[] methodNames = { "GetModules", "GetLoadedModules" };

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var typeName in typeNames)
                {
                    Type t;
                    try { t = asm.GetType(typeName, throwOnError: false); }
                    catch { continue; }
                    if (t == null) continue;
                    foreach (var method in methodNames)
                    {
                        MethodInfo mi;
                        try { mi = t.GetMethod(method, BindingFlags.Public | BindingFlags.Static); }
                        catch { continue; }
                        if (mi != null && mi.GetParameters().Length == 0)
                        {
                            _getModulesMethod = mi;
                            return;
                        }
                    }
                }
            }
        }
    }
}
