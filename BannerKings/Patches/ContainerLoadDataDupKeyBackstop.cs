using System;
using System.Reflection;
using HarmonyLib;

namespace BannerKings.Patches
{
    // Backstop for v1.8.9.0 / v1.8.9.1 / v1.8.9.2 saves that ended up with
    // duplicate keys in a serialized Dictionary container (most commonly
    // Dictionary<Religion, Dictionary<Hero, FaithfulData>> with two
    // Religion entries per StringId — see project_equals_without_gethashcode
    // memory).
    //
    // First attempt used [HarmonyPatch] with a TargetMethod() returning
    // the internal type via AccessTools.TypeByName. That registered as a
    // no-op (no log line, no _Patch suffix in the eventual crash stack
    // — see crashes/6.htm on v1.8.9.3). Switched to a manual
    // harmony.Patch() call wired from Main.OnSubModuleLoad with explicit
    // logging so we can see install success / failure.
    public static class ContainerLoadDataDupKeyBackstop
    {
        private static int _swallowed;

        public static void Install(Harmony harmony)
        {
            try
            {
                var t = AccessTools.TypeByName("TaleWorlds.SaveSystem.Load.ContainerLoadData");
                if (t == null)
                {
                    TaleWorlds.Library.Debug.Print(
                        "[BK] ContainerLoadDataDupKeyBackstop: type not found at install time — skipping",
                        color: TaleWorlds.Library.Debug.DebugColor.Yellow);
                    return;
                }

                var target = AccessTools.Method(t, "FillObject");
                if (target == null)
                {
                    TaleWorlds.Library.Debug.Print(
                        "[BK] ContainerLoadDataDupKeyBackstop: FillObject method not found — skipping",
                        color: TaleWorlds.Library.Debug.DebugColor.Yellow);
                    return;
                }

                var finalizer = AccessTools.Method(typeof(ContainerLoadDataDupKeyBackstop),
                    nameof(FillObjectFinalizer));

                harmony.Patch(target, finalizer: new HarmonyMethod(finalizer));
                TaleWorlds.Library.Debug.Print(
                    "[BK] ContainerLoadDataDupKeyBackstop: installed Finalizer on ContainerLoadData.FillObject",
                    color: TaleWorlds.Library.Debug.DebugColor.Green);
            }
            catch (Exception ex)
            {
                TaleWorlds.Library.Debug.Print(
                    $"[BK] ContainerLoadDataDupKeyBackstop install threw: {ex.GetType().Name}: {ex.Message}",
                    color: TaleWorlds.Library.Debug.DebugColor.Red);
            }
        }

        public static Exception FillObjectFinalizer(Exception __exception)
        {
            if (__exception is ArgumentException ae &&
                ae.Message != null &&
                ae.Message.IndexOf("same key", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _swallowed++;
                if (_swallowed <= 8) // avoid log spam for pathologically dup-heavy saves
                {
                    TaleWorlds.Library.Debug.Print(
                        $"[BK] Swallowed duplicate-key in ContainerLoadData.FillObject (#{_swallowed}). Likely a stale Religion dup from a v1.8.9.0/1/2 save predating the GetHashCode fix. The bad entry was dropped; load continues.",
                        color: TaleWorlds.Library.Debug.DebugColor.Yellow);
                }
                return null;
            }
            return __exception;
        }
    }
}
