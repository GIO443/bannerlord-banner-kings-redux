using System;
using System.Reflection;
using HarmonyLib;

namespace BannerKings.Patches.Diag
{
    // RBM is compiled against 1.3 reference assemblies. Several call sites
    // inside RBMAI / RBMCombat hit MissingMethodException at runtime on 1.4
    // because the vanilla overload they were built against was dropped or
    // renamed.
    //
    // v1.9.1.9..v1.9.1.12 evolved the install strategy and finally got the
    // type FQN right ("SiegeArcherPoints", no namespace) — but the user's
    // v1.9.1.12 crash htm shows the exception STILL fires. Conclusion:
    //
    //   (a) AssemblyLoad timing alone isn't enough. RBM lazy-loads RBMAI at
    //       Mission.AfterStart; the load event may fire too close to the
    //       first OnMissionTick to install reliably, OR our handler is
    //       running but Harmony's finalizer wrap doesn't survive the JIT
    //       MissingMethodException that fires at IL-compile time inside
    //       the method body (Harmony's try/catch goes around the CALL to
    //       the original — if the JIT throws while compiling the body, the
    //       wrapper's catch may be set up in an order that doesn't catch
    //       it).
    //   (b) A Prefix returning false skips the original method body
    //       entirely. The JIT never compiles the broken IL. There's no
    //       window in which the MissingMethodException can fire.
    //
    // v1.9.1.13 switches to Prefix-skip and adds three install paths so at
    // least one always wins:
    //   1. AppDomain.AssemblyLoad listener (lazy, fires on first load).
    //   2. Immediate try at Install() time (catches already-loaded case).
    //   3. CampaignBehavior subscribed to OnMissionStartedEvent — RBMAI is
    //      DEFINITELY loaded by then. Backup of last resort.
    //
    // All three call the same idempotent TryInstallAll. Diagnostic logging
    // routes through TaleWorlds.Library.Debug.Print so it appears in
    // rgl_log_*.txt regardless of MCM toggles.
    internal static class RBMRuntimeFinalizers
    {
        private static bool _wired;
        private static bool _siegeArcherPointsInstalled;
        private static bool _siegeArcherPointsLogged;
        private static bool _doPatchingFinalizerInstalled;
        private static bool _doPatchingFinalizerLogged;

        // Idempotent global wire-up; safe to call from Main.OnSubModuleLoad.
        // Subscribes once per AppDomain.
        public static void Install()
        {
            if (_wired) return;
            _wired = true;

            TaleWorlds.Library.Debug.Print(
                "[BK] RBMRuntimeFinalizers.Install: subscribing AssemblyLoad");

            AppDomain.CurrentDomain.AssemblyLoad += (_, args) =>
            {
                try
                {
                    var name = args.LoadedAssembly?.GetName()?.Name;
                    if (string.IsNullOrEmpty(name)) return;
                    if (name == "RBMAI" || name == "RBM" || name == "RBMCombat")
                    {
                        TaleWorlds.Library.Debug.Print(
                            $"[BK] RBMRuntimeFinalizers: AssemblyLoad saw '{name}', retrying install");
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
            catch (Exception ex)
            {
                TaleWorlds.Library.Debug.Print(
                    $"[BK] RBMRuntimeFinalizers.Install: immediate try threw {ex.GetType().Name}: {ex.Message}");
            }
        }

        // Public so the CampaignBehavior backup path can call it.
        public static void TryInstallAll()
        {
            TryInstallSiegeArcherPointsSkip();
            TryInstallDoPatchingFinalizer();
        }

        // Crash htm 2026-05-23 22:39: RBM 4.3 on game v1.4.5 throws during
        // mission start. Stack:
        //   Mission.AfterStart()
        //   → RBM.RBMAIPatchLogic.EarlyStart()
        //   → RBMAI.RBMAiPatcher.DoPatching()
        //   → Harmony.PatchAll(RBMAI.dll)
        //   → PatchClassProcessor.Patch() throws HarmonyException
        //     "Parameter 'forceDismounted' not found in method Mission::
        //     SpawnTroop(...)" — RBMAI's prefix declares a parameter that
        //     was removed from vanilla SpawnTroop in 1.4.
        //
        // BK has an existing [HarmonyFinalizer] on PatchClassProcessor.Patch
        // (UIManager.RBMHarmonyPatchFinalizer, v1.9.1.8) that filters by
        // container-type assembly name. That finalizer either doesn't fire
        // on this build of Harmony or doesn't match the failing patch's
        // owner — there is no "[BK] Swallowed RBM..." line in the user's
        // log around the crash, only the HarmonyException propagating.
        //
        // This patch installs an additional Finalizer one level higher in
        // the stack — directly on RBMAI.RBMAiPatcher.DoPatching. When
        // DoPatching throws, we swallow. Any RBM patch classes that were
        // already installed by the time of the throw stay installed; the
        // specific patch class that's broken (and any after it in the
        // iteration) silently fail. RBM loses partial functionality,
        // Mission.AfterStart succeeds, battle entry survives.
        //
        // Trade-off relative to the existing finalizer: this is bluntly
        // catches ANYTHING DoPatching throws, not just the specific
        // SpawnTroop case. If RBM has unrelated patches that throw for
        // unrelated reasons, those are swallowed too. Acceptable — the
        // alternative is the game crash dialog, and the user can read the
        // major_events.txt log to see what was swallowed.
        private static void TryInstallDoPatchingFinalizer()
        {
            if (_doPatchingFinalizerInstalled) return;

            // Type might be in namespace "RBMAI" or global. Try the FQN
            // first, then fall back to name-only resolution.
            Type t = AccessTools.TypeByName("RBMAI.RBMAiPatcher")
                  ?? AccessTools.TypeByName("RBMAiPatcher");
            if (t == null) return; // RBMAI not loaded yet — AssemblyLoad retry will catch it.

            MethodInfo m = AccessTools.Method(t, "DoPatching");
            if (m == null)
            {
                TaleWorlds.Library.Debug.Print(
                    $"[BK] RBMRuntimeFinalizers: found type {t.FullName} but no DoPatching method — not installing PatchAll-swallow finalizer");
                return;
            }

            try
            {
                var harmony = new Harmony("BannerKings.RBM.RuntimeFinalizers");
                var finalizerMethod = AccessTools.Method(
                    typeof(RBMRuntimeFinalizers), nameof(DoPatchingFinalizer));
                harmony.Patch(m, finalizer: new HarmonyMethod(finalizerMethod));
                _doPatchingFinalizerInstalled = true;
                TaleWorlds.Library.Debug.Print(
                    $"[BK] RBMRuntimeFinalizers: installed swallow-finalizer on {t.FullName}.DoPatching");
                BannerKings.Utils.Logs.MajorEvent(() =>
                    $"[BK] RBMRuntimeFinalizers: installed swallow-finalizer on {t.FullName}.DoPatching");
            }
            catch (Exception ex)
            {
                TaleWorlds.Library.Debug.Print(
                    $"[BK] RBMRuntimeFinalizers: failed to install DoPatching swallow-finalizer: {ex.GetType().Name}: {ex.Message}");
                BannerKings.Utils.Logs.MajorEvent(() =>
                    $"[BK] RBMRuntimeFinalizers: failed to install DoPatching swallow-finalizer: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // Harmony Finalizer body. Returning null suppresses the exception.
        // Logs once on first swallow so the user can see what dropped.
        public static Exception DoPatchingFinalizer(Exception __exception)
        {
            if (__exception == null) return null;
            try
            {
                if (!_doPatchingFinalizerLogged)
                {
                    _doPatchingFinalizerLogged = true;
                    TaleWorlds.Library.Debug.Print(
                        $"[BK] RBMRuntimeFinalizers: swallowed RBMAiPatcher.DoPatching exception ({__exception.GetType().Name}: {__exception.Message}). Some RBM patches may have failed to install — battle entry preserved.");
                    BannerKings.Utils.Logs.MajorEvent(() =>
                        $"[BK] RBMRuntimeFinalizers: swallowed RBMAiPatcher.DoPatching exception ({__exception.GetType().Name}: {__exception.Message})");
                }
            }
            catch { /* never throw out of a finalizer */ }
            return null; // suppress
        }

        // Install a Prefix returning false on SiegeArcherPoints.OnMissionTick.
        // This SKIPS the original method body entirely, which means the JIT
        // never has to compile the broken IL inside it. The mission tick
        // simply has SiegeArcherPoints as a no-op MissionBehavior — RBM's
        // siege-archer-formation logic doesn't run, but battle entry +
        // tick + exit all proceed normally.
        private static void TryInstallSiegeArcherPointsSkip()
        {
            if (_siegeArcherPointsInstalled) return;

            Type t = AccessTools.TypeByName("SiegeArcherPoints");
            if (t == null)
            {
                // Don't log every retry — too spammy. Only log on first
                // successful install. AssemblyLoad will fire again when
                // RBMAI eventually loads, retrying through this path.
                return;
            }
            MethodInfo m = AccessTools.Method(t, "OnMissionTick", new[] { typeof(float) });
            if (m == null)
            {
                TaleWorlds.Library.Debug.Print(
                    $"[BK] RBMRuntimeFinalizers: found type {t.FullName} but no OnMissionTick(float) — not installing skip");
                return;
            }

            try
            {
                var harmony = new Harmony("BannerKings.RBM.RuntimeFinalizers");
                var prefixMethod = AccessTools.Method(
                    typeof(RBMRuntimeFinalizers), nameof(SiegeArcherPointsSkipPrefix));
                harmony.Patch(m, prefix: new HarmonyMethod(prefixMethod));
                _siegeArcherPointsInstalled = true;
                TaleWorlds.Library.Debug.Print(
                    $"[BK] RBMRuntimeFinalizers: installed skip-prefix on {t.FullName}.OnMissionTick(float)");
                BannerKings.Utils.Logs.MajorEvent(() =>
                    $"[BK] RBMRuntimeFinalizers: installed skip-prefix on {t.FullName}.OnMissionTick(float)");
            }
            catch (Exception ex)
            {
                TaleWorlds.Library.Debug.Print(
                    $"[BK] RBMRuntimeFinalizers: failed to install SiegeArcherPoints skip-prefix: {ex.GetType().Name}: {ex.Message}");
                BannerKings.Utils.Logs.MajorEvent(() =>
                    $"[BK] RBMRuntimeFinalizers: failed to install SiegeArcherPoints skip-prefix: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // Harmony Prefix body. Returning false skips the original method.
        // Public so Harmony can find it via AccessTools.Method.
        public static bool SiegeArcherPointsSkipPrefix()
        {
            if (!_siegeArcherPointsLogged)
            {
                _siegeArcherPointsLogged = true;
                TaleWorlds.Library.Debug.Print(
                    "[BK] RBMRuntimeFinalizers: skipping SiegeArcherPoints.OnMissionTick (1.4 compat — RBM's GameEntity.Instantiate(Scene,String,MatrixFrame,Boolean,String) overload doesn't exist)");
                BannerKings.Utils.Logs.MajorEvent(() =>
                    "[BK] RBMRuntimeFinalizers: skipping SiegeArcherPoints.OnMissionTick (1.4 compat)");
            }
            return false; // skip original method body
        }
    }

    // Backup install path: a CampaignBehavior subscribed to
    // OnMissionStartedEvent. By the time a mission starts, RBM has loaded
    // RBMAI and added all its MissionBehaviors. Calling TryInstallAll
    // here as a backstop guarantees the install runs at least once per
    // mission start, even if the AssemblyLoad listener missed.
    internal class RBMRuntimeFinalizersBackupBehavior : TaleWorlds.CampaignSystem.CampaignBehaviorBase
    {
        public override void RegisterEvents()
        {
            TaleWorlds.CampaignSystem.CampaignEvents.OnMissionStartedEvent.AddNonSerializedListener(this, OnMissionStarted);
        }

        public override void SyncData(TaleWorlds.CampaignSystem.IDataStore dataStore) { }

        private void OnMissionStarted(TaleWorlds.Core.IMission mission)
        {
            try { RBMRuntimeFinalizers.TryInstallAll(); }
            catch { /* never break mission start */ }
        }
    }
}
