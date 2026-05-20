using System;
using System.Collections.Generic;
using BannerKings.Managers.Institutions.Religions.Faiths.Rites;
using BannerKings.Managers.Institutions.Religions.Faiths.Rites.Battania;
using BannerKings.Managers.Institutions.Religions.Faiths.Rites.Empire;
using BannerKings.Managers.Institutions.Religions.Faiths.Rites.Northern;
using BannerKings.Managers.Institutions.Religions.Faiths.Rites.Vlandia;

namespace BannerKings.Utils.BKData
{
    /// <summary>
    /// Named-key → <see cref="Rite"/> factory. <c>bk_faiths.xml</c> picks rites by
    /// key (<c>&lt;rite key="astaronia_festival"/&gt;</c>); the loader resolves the
    /// key here and binds a fresh instance to the faith. Setting-overhaul mods
    /// that want a brand-new rite ship a C# companion mod that calls
    /// <see cref="Register"/> from their <c>SubModule.OnSubModuleLoad</c>; rite
    /// behaviour is too code-bound to live in XML (per the project brief, that's
    /// option (b) — a DSL — and explicitly out of scope for BK).
    /// </summary>
    public static class RiteRegistry
    {
        private static readonly Dictionary<string, Func<Rite>> _factories
            = new Dictionary<string, Func<Rite>>(StringComparer.OrdinalIgnoreCase)
            {
                ["astaronia_festival"]  = () => new AstaroniaFestival(),
                ["darusosian_homage"]   = () => new DarusosianHomage(),
                ["darusosian_execution"]= () => new DarusosianExecution(),
                ["lance_offering"]      = () => new LanceOffering(),
                ["vlandia_horse"]       = () => new VlandiaHorse(),
                ["iron_offering"]       = () => new IronOffering(),
                ["great_sword_offering"]= () => new GreatSwordOffering(),
                ["axe_offering"]        = () => new AxeOffering(),
                ["treelore_festival"]   = () => new TreeloreFestival(),
            };

        /// <summary>Companion mods register additional rite keys here. Last
        /// registration wins on key collision so a flavor mod can swap BK's
        /// default implementation of e.g. "axe_offering" without recompiling BK.</summary>
        public static void Register(string key, Func<Rite> factory)
        {
            if (string.IsNullOrEmpty(key) || factory == null) return;
            _factories[key] = factory;
        }

        /// <summary>Returns null when the key is unknown; the loader logs the
        /// miss and skips it rather than crashing boot.</summary>
        public static Rite Create(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            return _factories.TryGetValue(key, out var f) ? f() : null;
        }

        public static bool IsKnown(string key)
            => !string.IsNullOrEmpty(key) && _factories.ContainsKey(key);
    }
}
