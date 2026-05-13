using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
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

    // Crash3 (2026-05-13): NRE in DefaultSkillLevelingManager.OnSurgeryApplied
    // during MapEvent.SimulateBattleSessionForMapEvent. Vanilla
    // MapEventSide.ApplySimulationDamageToSelectedTroop calls
    // OnSurgeryApplied(party.MobileParty, ...) where `party` is a PartyBase
    // with MobileParty == null. Hits in siege auto-resolves (defender's
    // town PartyBase has no MobileParty) and any BK custom-component
    // battle where the component is wired as a Party but lacks a
    // MobileParty wrapper. Inside OnSurgeryApplied, party.GetEffectiveRoleHolder
    // dereferences the null first argument and throws.
    //
    // Vanilla bug — but the cost of not fixing it is a campaign-tick
    // crash mid-battle-simulation, so we early-return when the party is
    // null. Effect: a single null call doesn't grant Medicine XP this
    // iteration. Real surgeons attached to actual MobileParty parties
    // still receive XP on subsequent iterations.
    [HarmonyPatch(typeof(DefaultSkillLevelingManager), nameof(DefaultSkillLevelingManager.OnSurgeryApplied))]
    internal static class OnSurgeryAppliedNullSafePatch
    {
        private static int _skipped;

        private static bool Prefix(MobileParty party)
        {
            if (party != null) return true; // proceed
            _skipped++;
            if (_skipped <= 4)
            {
                TaleWorlds.Library.Debug.Print(
                    "[BK] Skipped DefaultSkillLevelingManager.OnSurgeryApplied with null party (#" + _skipped + "). Caller (MapEventSide.ApplySimulationDamageToSelectedTroop) passed party.MobileParty == null — likely a town-defender PartyBase in a siege auto-resolve or a BK PartyBase without MobileParty wiring. No Medicine XP credit this iteration.",
                    color: TaleWorlds.Library.Debug.DebugColor.Yellow);
            }
            return false; // skip vanilla, no NRE
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
