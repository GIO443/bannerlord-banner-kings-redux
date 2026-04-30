using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;

namespace BannerKings.Utils
{
    /// <summary>
    /// Detects other installed mods so BK can defer to them where features overlap.
    /// All checks are cached after first call.
    ///
    /// Strategy: try TaleWorlds.ModuleManager.ModuleInfo if available, fall back to
    /// scanning loaded assemblies. Both are cheap; both are wrapped in try/catch so
    /// a missing API in any 1.3.x build can't crash BK init.
    /// </summary>
    public static class ModCompat
    {
        // Well-known module / assembly identifiers for the top conflict mods.
        // Tracking both because Bannerlord allows them to differ.
        public const string DiplomacyId = "Diplomacy";
        public const string DiplomacyAsm = "DiplomacyFixes";

        public const string ImprovedGarrisonsId = "ImprovedGarrisons";
        public const string ImprovedGarrisonsAsm = "ImprovedGarrisons";

        public const string RecruitEverywhereId = "RecruitEverywhere";
        public const string RecruitEverywhereAsm = "RecruitEverywhere";

        public const string MarryAnyoneId = "MarryAnyone";
        public const string MarryAnyoneAsm = "MarryAnyone";

        public const string BuyLandAtVillagesId = "BuyLandAtVillages";
        public const string BuyLandAtVillagesAsm = "BuyLandAtVillages";

        public const string RealisticBattleModId = "RBMCombat";
        public const string RealisticBattleModAsm = "RealisticBattleCombatModule";

        public const string WarSailsId = "NavalDLC";
        public const string WarSailsAsm = "NavalDLC";

        // AI Influence (AI Diplomacy) — https://www.nexusmods.com/mountandblade2bannerlord/mods/9711
        // Module folder name confirmed as `AIInfluence` per Nexus install instructions.
        // Assembly name presumed to match; ProbeAssembly() falls back gracefully if it differs.
        public const string AIInfluenceId = "AIInfluence";
        public const string AIInfluenceAsm = "AIInfluence";

        // Realm of Thrones — total conversion replacing Calradia with Westeros.
        // BK doesn't ship ROT-specific data; a separate compat patch mod is
        // expected to register ROT lifestyles, titles, lanes, etc. via BK's
        // DefaultTypeInitializer<> registries. This flag is here so BK code
        // can yield Calradia-only behaviour when ROT is loaded if needed
        // (rare — most BK code uses Settlement/Culture lookups by reference,
        // not by hardcoded StringId).
        public const string RealmOfThronesId = "realmofthrones.core";
        public const string RealmOfThronesAsm = "ROT";

        private static readonly ConcurrentDictionary<string, bool> _cache = new();

        private static MethodInfo _getModulesMethod;
        private static bool _moduleApiResolved;

        /// <summary>True if any mod with the given module id OR assembly name is loaded.</summary>
        public static bool IsLoaded(string moduleId, string assemblyName = null)
        {
            string key = moduleId + "|" + (assemblyName ?? string.Empty);
            if (_cache.TryGetValue(key, out var hit)) return hit;

            bool present = ProbeModule(moduleId) || ProbeAssembly(assemblyName ?? moduleId);
            _cache[key] = present;
            return present;
        }

        // Convenience properties for the well-known mods. These are read at every
        // skip-point in BK; the cache makes them essentially free.

        public static bool DiplomacyMod
            => IsLoaded(DiplomacyId, DiplomacyAsm);

        public static bool ImprovedGarrisons
            => IsLoaded(ImprovedGarrisonsId, ImprovedGarrisonsAsm);

        public static bool RecruitEverywhere
            => IsLoaded(RecruitEverywhereId, RecruitEverywhereAsm);

        public static bool MarryAnyone
            => IsLoaded(MarryAnyoneId, MarryAnyoneAsm);

        public static bool BuyLandAtVillages
            => IsLoaded(BuyLandAtVillagesId, BuyLandAtVillagesAsm);

        public static bool RealisticBattleMod
            => IsLoaded(RealisticBattleModId, RealisticBattleModAsm);

        /// <summary>True if the War Sails (NavalDLC) module is loaded.</summary>
        public static bool WarSails
            => IsLoaded(WarSailsId, WarSailsAsm);

        /// <summary>True if AI Influence (AI Diplomacy) is loaded.</summary>
        public static bool AIInfluence
            => IsLoaded(AIInfluenceId, AIInfluenceAsm);

        /// <summary>True if the Realm of Thrones total-conversion module is loaded.</summary>
        public static bool RealmOfThrones
            => IsLoaded(RealmOfThronesId, RealmOfThronesAsm);

        // ----- internals -----

        private static bool ProbeModule(string moduleId)
        {
            if (string.IsNullOrEmpty(moduleId)) return false;
            try
            {
                ResolveModuleApi();
                if (_getModulesMethod == null) return false;
                var modules = _getModulesMethod.Invoke(null, null) as System.Collections.IEnumerable;
                if (modules == null) return false;
                foreach (var m in modules)
                {
                    var idProp = m.GetType().GetProperty("Id");
                    if (idProp == null) continue;
                    var id = idProp.GetValue(m) as string;
                    if (string.Equals(id, moduleId, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch
            {
                // Module API surface differs across 1.3.x patches — fall through.
            }
            return false;
        }

        private static bool ProbeAssembly(string assemblyName)
        {
            if (string.IsNullOrEmpty(assemblyName)) return false;
            try
            {
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (string.Equals(a.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch
            {
            }
            return false;
        }

        private static void ResolveModuleApi()
        {
            if (_moduleApiResolved) return;
            _moduleApiResolved = true;

            // Look for TaleWorlds.ModuleManager.ModuleInfo.GetModules() or
            // TaleWorlds.ModuleManager.ModuleHelper.GetModules() — both have shipped
            // in different 1.x builds.
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
                    var t = asm.GetType(typeName, throwOnError: false);
                    if (t == null) continue;
                    foreach (var method in methodNames)
                    {
                        var mi = t.GetMethod(method, BindingFlags.Public | BindingFlags.Static);
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
