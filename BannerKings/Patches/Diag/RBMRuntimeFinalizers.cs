using System;
using System.Reflection;
using HarmonyLib;

namespace BannerKings.Patches.Diag
{
    // RBM is compiled against 1.3 reference assemblies. Several call sites
    // inside RBMAI / RBMCombat hit MissingMethodException at runtime on 1.4
    // because the vanilla overload they were built against was dropped or
    // renamed. The cleanest broadly-compatible fix without forking RBM is
    // to wrap the offending methods in a Harmony Finalizer that swallows
    // the exception and lets the mission tick continue.
    //
    // The catch: RBMAI.dll is not in the AppDomain at BK's OnSubModuleLoad
    // / OnGameStart time. RBM lazy-loads it from RBM's own SubModule entry
    // points. A [HarmonyPatch]-decorated class with Prepare()/TargetMethod()
    // runs during BK's PatchAll-loop at OnGameStart — by then RBMAI types
    // are NOT yet discoverable, Prepare() returns false, and the finalizer
    // is silently skipped.
    //
    // Install path: subscribe to AppDomain.AssemblyLoad at BK OnSubModuleLoad.
    // The moment RBMAI / RBMCombat surfaces in the AppDomain (which the CLR
    // fires synchronously when the assembly is loaded), we run the install.
    // Idempotency flags ensure a single install per process even when the
    // user exits to main menu and starts a new campaign (which re-runs
    // OnGameStart and re-fires assembly loads in some scenarios).
    //
    // Each finalizer logs the first occurrence per process via
    // Logs.MajorEvent then suppresses further logs (the mission tick fires
    // every frame; without the once-flag major_events.txt would spew).
    internal static class RBMRuntimeFinalizers
    {
        private static bool _wired;
        private static bool _siegeArcherPointsInstalled;
        private static bool _siegeArcherPointsLogged;

        // Idempotent global wire-up; safe to call from Main.OnSubModuleLoad.
        // Subscribes once per AppDomain.
        public static void Install()
        {
            if (_wired) return;
            _wired = true;

            AppDomain.CurrentDomain.AssemblyLoad += (_, args) =>
            {
                try
                {
                    var name = args.LoadedAssembly?.GetName()?.Name;
                    if (string.IsNullOrEmpty(name)) return;
                    if (name == "RBMAI" || name == "RBM" || name == "RBMCombat")
                    {
                        TryInstallAll();
                    }
                }
                catch
                {
                    // AssemblyLoad handlers throwing into the loader chain
                    // are a famously bad time — never propagate.
                }
            };

            // Also try once now — RBMAI may have already loaded before BK
            // wired up (race against module init order).
            try { TryInstallAll(); }
            catch { /* never propagate from OnSubModuleLoad */ }
        }

        // Run every installer. Each one is idempotent and no-ops if the
        // target type / method isn't reachable yet, so this is safe to
        // call repeatedly.
        private static void TryInstallAll()
        {
            TryInstallSiegeArcherPointsFinalizer();
        }

        // RBMAI.SiegeArcherPoints.OnMissionTick(float) — crash-htm
        // 2026-05-22 22:53 and 2026-05-23 08:51 hit this. Calls
        // GameEntity.Instantiate with a 1.3 overload that 1.4 dropped.
        private static void TryInstallSiegeArcherPointsFinalizer()
        {
            if (_siegeArcherPointsInstalled) return;

            Type t = AccessTools.TypeByName("RBMAI.SiegeArcherPoints");
            if (t == null) return;
            MethodInfo m = AccessTools.Method(t, "OnMissionTick", new[] { typeof(float) });
            if (m == null) return;

            try
            {
                var harmony = new Harmony("BannerKings.RBM.RuntimeFinalizers");
                var finalizerMethod = AccessTools.Method(
                    typeof(RBMRuntimeFinalizers), nameof(SiegeArcherPointsFinalizer));
                harmony.Patch(m, finalizer: new HarmonyMethod(finalizerMethod));
                _siegeArcherPointsInstalled = true;
                BannerKings.Utils.Logs.MajorEvent(() =>
                    "[BK] RBMRuntimeFinalizers: installed finalizer on RBMAI.SiegeArcherPoints.OnMissionTick(float)");
            }
            catch (Exception ex)
            {
                BannerKings.Utils.Logs.MajorEvent(() =>
                    $"[BK] RBMRuntimeFinalizers: failed to install SiegeArcherPoints finalizer: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // Harmony finalizer body. Public so Harmony can find it via
        // AccessTools.Method.
        public static Exception SiegeArcherPointsFinalizer(Exception __exception)
        {
            if (__exception == null) return null;
            if (!_siegeArcherPointsLogged)
            {
                _siegeArcherPointsLogged = true;
                BannerKings.Utils.Logs.MajorEvent(() =>
                    $"[BK] Swallowed RBM SiegeArcherPoints.OnMissionTick: {__exception.GetType().Name}: {__exception.Message}");
            }
            return null; // swallow — mission tick continues
        }
    }
}
