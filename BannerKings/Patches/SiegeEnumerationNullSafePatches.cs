using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.Core;

namespace BannerKings.Patches
{
    // Defensive Finalizer around DefaultEncounterModel.GetLeaderOfSiegeEvent.
    //
    // The vanilla method calls GetLeaderOfEventInternal, which iterates
    // `BesiegerCamp.InvolvedParties` (a yield-return IEnumerable backed by
    // a List). If anything mutates that list mid-walk — another mod's
    // siege-state hook, a callback fired by a BK tick handler, or a
    // race between campaign thread and UI thread — the underlying
    // List<>.Enumerator throws InvalidOperationException ("collection
    // was modified") via MoveNextRare.
    //
    // Reported on v1.8.10.0 against the siege strategies game-menu tick:
    //   game_menu_siege_strategies_on_tick
    //   → SiegeEventCampaignBehavior.currentSiegeDescription
    //   → DefaultEncounterModel.GetLeaderOfSiegeEvent_Patch1
    //   → GetLeaderOfEventInternal
    //   → BesiegerCamp.<>d__25.MoveNext
    //   → InvalidOperationException
    //
    // Vanilla is stable in stock games — the crash is an interaction
    // with patched / sub-modded siege state. Swallow the exception and
    // return null; the menu skips its leader description for one frame
    // and re-renders cleanly on the next tick.
    [HarmonyPatch(typeof(DefaultEncounterModel), nameof(DefaultEncounterModel.GetLeaderOfSiegeEvent))]
    internal static class GetLeaderOfSiegeEventNullSafePatch
    {
        private static int _swallowed;

        public static Exception Finalizer(Exception __exception, ref Hero __result)
        {
            if (__exception is InvalidOperationException)
            {
                _swallowed++;
                if (_swallowed <= 4)
                {
                    TaleWorlds.Library.Debug.Print(
                        "[BK] Swallowed InvalidOperationException in DefaultEncounterModel.GetLeaderOfSiegeEvent (#" + _swallowed + "). Likely a collection-modified-during-enumeration race on BesiegerCamp.InvolvedParties; menu skips leader description for this tick.",
                        color: TaleWorlds.Library.Debug.DebugColor.Yellow);
                }
                __result = null;
                return null;
            }
            return __exception;
        }
    }

    // Mirror for GetLeaderOfMapEvent: same internal helper, same iterator
    // pattern, same potential race on MapEvent.Parties enumeration.
    [HarmonyPatch(typeof(DefaultEncounterModel), nameof(DefaultEncounterModel.GetLeaderOfMapEvent))]
    internal static class GetLeaderOfMapEventNullSafePatch
    {
        private static int _swallowed;

        public static Exception Finalizer(Exception __exception, ref Hero __result)
        {
            if (__exception is InvalidOperationException)
            {
                _swallowed++;
                if (_swallowed <= 4)
                {
                    TaleWorlds.Library.Debug.Print(
                        "[BK] Swallowed InvalidOperationException in DefaultEncounterModel.GetLeaderOfMapEvent (#" + _swallowed + "). Likely a collection-modified-during-enumeration race on MapEvent.Parties; caller gets null for this tick.",
                        color: TaleWorlds.Library.Debug.DebugColor.Yellow);
                }
                __result = null;
                return null;
            }
            return __exception;
        }
    }
}
