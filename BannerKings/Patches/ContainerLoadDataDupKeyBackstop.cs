using System;
using System.Reflection;
using HarmonyLib;

namespace BannerKings.Patches
{
    // Backstop for v1.8.9.0 / v1.8.9.1 saves that ended up with duplicate
    // keys in a serialized Dictionary container. Root cause was the
    // Religion / Faith / FaithGroup / Doctrine / BannerKingsObject
    // Equals/GetHashCode contract violation: Equals was overridden to
    // compare by StringId but GetHashCode was left as default identity,
    // so Dictionary<,>.ContainsKey missed logically-equal entries and
    // ReligionsManager.InitializeReligions ended up adding a second
    // Religion instance per StringId on every game-load. Saving wrote
    // both. Reloading collapsed them to one MBObjectBase instance, then
    // ContainerLoadData.FillObject called Dictionary.Add twice with
    // that single instance → ArgumentException "An item with the same
    // key has already been added".
    //
    // The forward fix is to override GetHashCode so the dict actually
    // detects duplicates at insertion time and InitializeReligions
    // becomes a no-op when the religion is already present. New saves
    // will not contain duplicates.
    //
    // This finalizer rescues OLD saves that already shipped with the
    // duplicate baked in. Without it those saves are dead — the
    // exception fires during the parallel container-fill pass before
    // any BK code can intervene. The cost: a bad container fill drops
    // the duplicate entry (which is what we want) and bails out of
    // that container's loop — partial fill, but the data the engine
    // serialised first is what survives, and BK's PostInitialize
    // chain repairs the rest.
    // Type is internal in TaleWorlds.SaveSystem so we resolve it
    // reflectively via TargetMethod() rather than typeof(...).
    [HarmonyPatch]
    internal static class ContainerLoadDataDupKeyBackstop
    {
        private static int _swallowed;

        public static MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("TaleWorlds.SaveSystem.Load.ContainerLoadData");
            return t != null ? AccessTools.Method(t, "FillObject") : null;
        }

        public static Exception Finalizer(Exception __exception)
        {
            if (__exception is ArgumentException ae &&
                ae.Message != null &&
                ae.Message.IndexOf("same key", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _swallowed++;
                if (_swallowed <= 8) // avoid log spam for pathologically dup-heavy saves
                {
                    TaleWorlds.Library.Debug.Print(
                        $"[BK] Swallowed duplicate-key in ContainerLoadData.FillObject (#{_swallowed}). Likely a stale Religion dup from a v1.8.9.0/1 save predating the GetHashCode fix. The bad entry was dropped; load continues.",
                        color: TaleWorlds.Library.Debug.DebugColor.Yellow);
                }
                return null;
            }
            return __exception;
        }
    }
}
