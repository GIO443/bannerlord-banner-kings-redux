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

        // v1.9.9.3: ImprovedGarrisons compat constants retired.
        // The IG yield-gates in Main.cs (BKGarrisonModel registration) and
        // EconomyPatches (UpdateClanSettlementAutoRecruitment prefix) were
        // producing incompatible behaviour rather than coexistence. BK now
        // runs its garrison + auto-recruit logic unconditionally.
        // ImprovedGarrisons property below stays as a stub returning false
        // so any external/legacy callers still compile but get no-op
        // behaviour. Will be removed entirely in a follow-up.

        public const string RecruitEverywhereId = "RecruitEverywhere";
        public const string RecruitEverywhereAsm = "RecruitEverywhere";

        public const string MarryAnyoneId = "MarryAnyone";
        public const string MarryAnyoneAsm = "MarryAnyone";

        public const string BuyLandAtVillagesId = "BuyLandAtVillages";
        public const string BuyLandAtVillagesAsm = "BuyLandAtVillages";

        // Realistic Battle Mod (RBM) — combat overhaul.
        // Workshop ID 2859251492. Modern RBM (v4.2.x) uses module Id "RBM" and
        // ships RBM.dll, RBMCombat.dll, RBMAI.dll, RBMConfig.dll. The legacy
        // identifier "RBMCombat" / "RealisticBattleCombatModule" was the
        // pre-v4 naming; kept here as a secondary check so old installs are
        // still recognised.
        public const string RealisticBattleModId = "RBM";
        public const string RealisticBattleModAsm = "RBM";
        public const string RealisticBattleModLegacyId = "RBMCombat";
        public const string RealisticBattleModLegacyAsm = "RealisticBattleCombatModule";

        // RBM War Sails Submod — naval-combat data layered on top of RBM
        // for the NavalDLC. XML-only (no DLL); detection is module-id only.
        public const string RealisticBattleWarSailsId = "RBM_WS";

        // RTS Camera — battle-scope RTS-style control. Mission scope only;
        // doesn't intersect BK campaign systems but BK declares LoadAfterThis
        // so any Mission-scope BK hook (BKTournamentBehavior, NavalPerk
        // patches) runs after RTSCamera's Mission patches install.
        public const string RTSCameraId = "RTSCamera";
        public const string RTSCameraAsm = "RTSCamera";

        // RTS Camera Command System — the order-issuing UI submod. Same
        // Mission-scope-only profile as RTSCamera proper.
        public const string RTSCameraCommandSystemId = "RTSCamera.CommandSystem";
        public const string RTSCameraCommandSystemAsm = "RTSCamera.CommandSystem";

        public const string WarSailsId = "NavalDLC";
        public const string WarSailsAsm = "NavalDLC";

        // AI Influence (AI Diplomacy) — https://www.nexusmods.com/mountandblade2bannerlord/mods/9711
        // Module folder name confirmed as `AIInfluence` per Nexus install instructions.
        // Assembly name presumed to match; ProbeAssembly() falls back gracefully if it differs.
        public const string AIInfluenceId = "AIInfluence";
        public const string AIInfluenceAsm = "AIInfluence";

        // Economy Overhaul Framework — https://www.nexusmods.com/mountandblade2bannerlord/mods/9558
        // Module folder `Bannerlord.Economy_Overhaul`, module Id `Bannerlord.EconomyOverhaul`,
        // assembly `Bannerlord.Economy_Overhaul`. Decorator-pattern overhaul that wraps
        // existing economy models (BK survives stacked underneath where EOF delegates).
        public const string EconomyOverhaulId = "Bannerlord.EconomyOverhaul";
        public const string EconomyOverhaulAsm = "Bannerlord.Economy_Overhaul";

        // Bannerlord Living Economy (BetterEconomy) — https://www.nexusmods.com/mountandblade2bannerlord/mods/10796
        // Hard dependency as of the BetterEconomy integration (it replaces EOF).
        // Module Id and assembly name are both `BetterEconomy`. BK force-enables
        // its FeudalEconomyCampaignBehavior and reads the social-class / estate
        // layer through BannerKings.Utils.BetterEconomyBridge.
        public const string BetterEconomyId = "BetterEconomy";
        public const string BetterEconomyAsm = "BetterEconomy";

        // Realm of Thrones — total conversion replacing Calradia with Westeros.
        // BK doesn't ship ROT-specific data; a separate compat patch mod is
        // expected to register ROT lifestyles, titles, lanes, etc. via BK's
        // DefaultTypeInitializer<> registries. This flag is here so BK code
        // can yield Calradia-only behaviour when ROT is loaded if needed
        // (rare — most BK code uses Settlement/Culture lookups by reference,
        // not by hardcoded StringId).
        public const string RealmOfThronesId = "realmofthrones.core";
        public const string RealmOfThronesAsm = "ROT";

        // Shokuho — Sengoku-era total conversion (Workshop ID 3496296180).
        // Ships its own `ShokuhoCampaign` GameType, cultures, kingdoms, clans,
        // settlements, and religions (shinto / rinzai / shingon / tendai /
        // soto / shin). Shokuho.dll contains zero references to BannerKings
        // or BetterEconomy — it is BK-unaware. BK's behaviors are registered
        // for the `Campaign` / `CampaignStoryMode` GameTypes, so they do not
        // auto-load into a Shokuho campaign; this flag is here so any BK
        // bootstrap code that runs *before* GameType resolution (XML
        // registration, SubModule init, model replacement) can yield when
        // Shokuho is present. No BK content (titles, religions, lifestyles)
        // currently fits Shokuho's setting — a content port is out of scope
        // and would belong in a separate sub-mod.
        public const string ShokuhoId = "Shokuho";
        public const string ShokuhoAsm = "Shokuho";

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

        // v1.9.9.3: Retired. Always returns false so any remaining
        // callers behave as if IG is not loaded (BK's own garrison
        // logic runs). Property kept as a stub for compile-compat with
        // out-of-tree callers; will be removed once nothing references
        // it.
        public static bool ImprovedGarrisons => false;

        public static bool RecruitEverywhere
            => IsLoaded(RecruitEverywhereId, RecruitEverywhereAsm);

        public static bool MarryAnyone
            => IsLoaded(MarryAnyoneId, MarryAnyoneAsm);

        public static bool BuyLandAtVillages
            => IsLoaded(BuyLandAtVillagesId, BuyLandAtVillagesAsm);

        public static bool RealisticBattleMod
            => IsLoaded(RealisticBattleModId, RealisticBattleModAsm)
            || IsLoaded(RealisticBattleModLegacyId, RealisticBattleModLegacyAsm);

        /// <summary>True if the RBM War Sails naval-combat submod is loaded.</summary>
        public static bool RealisticBattleWarSails
            => IsLoaded(RealisticBattleWarSailsId);

        /// <summary>True if RTS Camera is loaded (battle scope only).</summary>
        public static bool RTSCamera
            => IsLoaded(RTSCameraId, RTSCameraAsm);

        /// <summary>True if RTS Camera Command System is loaded.</summary>
        public static bool RTSCameraCommandSystem
            => IsLoaded(RTSCameraCommandSystemId, RTSCameraCommandSystemAsm);

        /// <summary>True if the War Sails (NavalDLC) module is loaded.</summary>
        public static bool WarSails
            => IsLoaded(WarSailsId, WarSailsAsm);

        /// <summary>True if AI Influence (AI Diplomacy) is loaded.</summary>
        public static bool AIInfluence
            => IsLoaded(AIInfluenceId, AIInfluenceAsm);

        /// <summary>True if Economy Overhaul Framework is loaded.</summary>
        public static bool EconomyOverhaul
            => IsLoaded(EconomyOverhaulId, EconomyOverhaulAsm);

        /// <summary>True if Bannerlord Living Economy (BetterEconomy) is loaded.</summary>
        public static bool BetterEconomy
            => IsLoaded(BetterEconomyId, BetterEconomyAsm);

        /// <summary>True if the Realm of Thrones total-conversion module is loaded.</summary>
        public static bool RealmOfThrones
            => IsLoaded(RealmOfThronesId, RealmOfThronesAsm);

        /// <summary>True if the Shokuho Sengoku-era total-conversion module is loaded.</summary>
        public static bool Shokuho
            => IsLoaded(ShokuhoId, ShokuhoAsm);

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
