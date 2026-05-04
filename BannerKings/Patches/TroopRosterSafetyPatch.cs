using System;
using System.Diagnostics;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;

namespace BannerKings.Patches
{
    /// <summary>
    /// Defensive Harmony shims around TroopRoster mutation paths that
    /// crash when a roster's UniqueTroopDescriptor slot table is corrupt.
    ///
    /// Root cause: BK previously had several iterate-and-mutate loops over
    /// PrisonRoster / MemberRoster (HandleBandits, AddPatrolBehavior,
    /// KillBandits, mercenary-conversion, etc.). TroopRoster.AddToCounts
    /// re-keys the internal UniqueTroopDescriptor slot indices when a
    /// count is decremented; iterating the live roster while decrementing
    /// it leaves stale UniqueTroopDescriptors in surviving slots. Vanilla
    /// MapEvent.LootDefeatedPartyPrisoners later walks the roster looking
    /// up troops by descriptor → AddToCountsAtIndex(stale) →
    /// IndexOutOfRangeException → game crash.
    ///
    /// The iterate-and-mutate sites in BK are now patched (snapshot via
    /// .ToList() before mutating). However saves taken before that fix
    /// carry persisted corruption; loading them and resolving the next
    /// siege still crashes via the same path.
    ///
    /// This patch swallows IndexOutOfRangeException at the lookup point.
    /// It's a finalizer (runs after the original); when the exception
    /// fires it discards it and returns normally, leaving the roster
    /// state slightly inconsistent (one fewer troop counted than vanilla
    /// expected) but not corrupt — the rebuild on next save/load cycles
    /// the slot table back to clean.
    /// </summary>
    [HarmonyPatch(typeof(TroopRoster))]
    internal class TroopRosterSafetyPatch
    {
        // RemoveTroop(CharacterObject, int, UniqueTroopDescriptor, int) is
        // the path used by MapEvent.LootDefeatedPartyPrisoners. The
        // descriptor lookup is what crashes on a corrupt roster. A
        // finalizer that returns the exception (swallowing it) is the
        // minimal-blast-radius fix — vanilla's loop continues, the
        // subsequent troop is processed, and the save self-heals.
        [HarmonyFinalizer]
        [HarmonyPatch("RemoveTroop", typeof(CharacterObject), typeof(int), typeof(UniqueTroopDescriptor), typeof(int))]
        private static Exception RemoveTroopFinalizer(Exception __exception)
        {
            if (__exception is IndexOutOfRangeException)
            {
                // Swallow. Vanilla's outer loop carries on.
                return null;
            }
            // Anything else — let it propagate. We don't want to mask
            // unrelated bugs.
            return __exception;
        }

        // AddToCountsAtIndex is the inner method that actually throws.
        // Some non-LootDefeatedPartyPrisoners callers reach this directly
        // (e.g. AI-tactical loot calls). Swallow the same exception class
        // there too.
        [HarmonyFinalizer]
        [HarmonyPatch("AddToCountsAtIndex", typeof(int), typeof(int), typeof(int), typeof(int), typeof(bool))]
        private static Exception AddToCountsAtIndexFinalizer(Exception __exception, ref int __result)
        {
            if (__exception is IndexOutOfRangeException)
            {
                __result = 0;
                return null;
            }
            return __exception;
        }

        // Caller-trace logger for AddToCounts. Off by default; enabled via
        // MCM → Diagnostics → Log Roster Mutations (heavy). Useful to
        // identify any iterate-and-mutate site that BK code reaches that
        // hasn't been audited / snapshot-fixed.
        [HarmonyPostfix]
        [HarmonyPatch("AddToCounts", typeof(CharacterObject), typeof(int), typeof(bool), typeof(int), typeof(int), typeof(bool), typeof(int))]
        private static void AddToCountsTracePostfix(CharacterObject character, int countChange)
        {
            if (!BannerKings.Utils.Logs.RosterMutationsTraceEnabled) return;
            try
            {
                // Capture caller frames 2..6 (skip Harmony shim + the method
                // itself). Use simple frame names; full IL trace is too
                // heavy for a hot-path opt-in.
                var st = new StackTrace(2, false);
                int n = System.Math.Min(5, st.FrameCount);
                var frames = new string[n];
                for (int i = 0; i < n; i++)
                {
                    var m = st.GetFrame(i)?.GetMethod();
                    if (m == null) { frames[i] = "?"; continue; }
                    frames[i] = (m.DeclaringType?.Name ?? "?") + "." + m.Name;
                }
                BannerKings.Utils.Logs.RosterMutationsTrace(() =>
                    $"AddToCounts({character?.Name}, {countChange}) ← {string.Join(" ← ", frames)}");
            }
            catch { /* never throw out of a tracer */ }
        }
    }
}
