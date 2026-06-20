using HarmonyLib;
using System.Diagnostics;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;

namespace BannerKings.Patches
{
    // Diagnostic-only: wrap every DisbandArmyAction.Apply* entry point
    // with a Prefix that records the army leader, dispersion reason, and
    // a managed stack trace to BK_army_disband.txt. When the user
    // reports "my army disbanded after a small bit", this trace shows
    // which engine path actually fired the disband — vanilla.HourlyTick,
    // an encounter, an AI think, a BK behaviour, etc. — instead of
    // forcing us to grep speculatively.
    //
    // Always on (no MCM gate): the dispersion event is rare and the
    // trace cost is one stack walk per call. Output is buffered through
    // the existing AppendDiagnosticLine pipeline, so writes don't block
    // the campaign thread.
    internal static class ArmyDisbandTracePatches
    {
        private static void WriteTrace(string applyName, Army army)
        {
            try
            {
                if (army == null) return;
                string leader = "<null>";
                try { leader = army.LeaderParty?.Name?.ToString() ?? "<no-name>"; }
                catch { }
                bool isPlayer = false;
                try { isPlayer = army.LeaderParty == MobileParty.MainParty; }
                catch { }
                int parties = -1;
                try { parties = army.Parties != null ? army.Parties.Count : -1; }
                catch { }
                float cohesion = -1f;
                try { cohesion = army.Cohesion; }
                catch { }

                string day = "?";
                try { day = CampaignTime.Now.ToDays.ToString("F2"); }
                catch { }

                string trace = "<no-trace>";
                try { trace = new StackTrace(2, false).ToString(); }
                catch { }

                string line = $"day={day} apply={applyName} player={isPlayer} leader={leader} parties={parties} cohesion={cohesion:F1}\n{trace}";
                BannerKings.BannerKingsCheats.AppendDiagnosticLine("army_disband.txt", line);
            }
            catch { }
        }

        [HarmonyPatch(typeof(DisbandArmyAction), nameof(DisbandArmyAction.ApplyByReleasedByPlayerAfterBattle))]
        internal static class TraceReleasedByPlayer
        {
            private static void Prefix(Army army) => WriteTrace("ReleasedByPlayerAfterBattle", army);
        }

        [HarmonyPatch(typeof(DisbandArmyAction), nameof(DisbandArmyAction.ApplyByArmyLeaderIsDead))]
        internal static class TraceLeaderDead
        {
            private static void Prefix(Army army) => WriteTrace("ArmyLeaderIsDead", army);
        }

        [HarmonyPatch(typeof(DisbandArmyAction), nameof(DisbandArmyAction.ApplyByNotEnoughParty))]
        internal static class TraceNotEnoughParty
        {
            private static void Prefix(Army army) => WriteTrace("NotEnoughParty", army);
        }

        [HarmonyPatch(typeof(DisbandArmyAction), nameof(DisbandArmyAction.ApplyByObjectiveFinished))]
        internal static class TraceObjectiveFinished
        {
            private static void Prefix(Army army) => WriteTrace("ObjectiveFinished", army);
        }

        [HarmonyPatch(typeof(DisbandArmyAction), nameof(DisbandArmyAction.ApplyByPlayerTakenPrisoner))]
        internal static class TracePlayerTakenPrisoner
        {
            private static void Prefix(Army army) => WriteTrace("PlayerTakenPrisoner", army);
        }

        [HarmonyPatch(typeof(DisbandArmyAction), nameof(DisbandArmyAction.ApplyByFoodProblem))]
        internal static class TraceFoodProblem
        {
            private static void Prefix(Army army) => WriteTrace("FoodProblem", army);
        }

        [HarmonyPatch(typeof(DisbandArmyAction), nameof(DisbandArmyAction.ApplyByInactivity))]
        internal static class TraceInactivity
        {
            private static void Prefix(Army army) => WriteTrace("Inactivity", army);
        }

        [HarmonyPatch(typeof(DisbandArmyAction), nameof(DisbandArmyAction.ApplyByCohesionDepleted))]
        internal static class TraceCohesionDepleted
        {
            private static void Prefix(Army army) => WriteTrace("CohesionDepleted", army);
        }

        [HarmonyPatch(typeof(DisbandArmyAction), nameof(DisbandArmyAction.ApplyByNoActiveWar))]
        internal static class TraceNoActiveWar
        {
            private static void Prefix(Army army) => WriteTrace("NoActiveWar", army);
        }

        [HarmonyPatch(typeof(DisbandArmyAction), nameof(DisbandArmyAction.ApplyByUnknownReason))]
        internal static class TraceUnknownReason
        {
            private static void Prefix(Army army) => WriteTrace("UnknownReason", army);
        }

        [HarmonyPatch(typeof(DisbandArmyAction), nameof(DisbandArmyAction.ApplyByLeaderPartyRemoved))]
        internal static class TraceLeaderPartyRemoved
        {
            private static void Prefix(Army army) => WriteTrace("LeaderPartyRemoved", army);
        }

        [HarmonyPatch(typeof(DisbandArmyAction), nameof(DisbandArmyAction.ApplyByNoShip))]
        internal static class TraceNoShip
        {
            private static void Prefix(Army army) => WriteTrace("NoShip", army);
        }
    }

    // Player-army protection: an army the PLAYER leads (LeaderParty ==
    // MainParty) is theirs to disband — the automatic / AI-driven disband paths
    // must not dissolve it out from under them. Each prefix returns false to
    // SKIP the vanilla disband when the army is player-led; the trace prefixes
    // above still fire, so a blocked attempt is recorded in BK_army_disband.txt.
    //
    // We block only the POLICY reasons. The STRUCTURAL reasons (leader dead,
    // player taken prisoner, leader party removed, no ship at sea) and the
    // player's OWN release-after-battle are deliberately NOT blocked: skipping
    // those would leave a corrupt / leaderless army — a freeze hazard, and the
    // exact dangling-state class this codebase has been bitten by. Cohesion and
    // food disbands are also left intact — those are player-managed mechanics, so
    // a starved or low-cohesion army still falls apart as the player expects.
    internal static class PlayerArmyDisbandProtection
    {
        private static bool IsPlayerLedArmy(Army army)
        {
            try { return army != null && army.LeaderParty == MobileParty.MainParty; }
            catch { return false; }
        }

        [HarmonyPatch(typeof(DisbandArmyAction), nameof(DisbandArmyAction.ApplyByNotEnoughParty))]
        internal static class BlockNotEnoughParty
        {
            private static bool Prefix(Army army) => !IsPlayerLedArmy(army);
        }

        [HarmonyPatch(typeof(DisbandArmyAction), nameof(DisbandArmyAction.ApplyByObjectiveFinished))]
        internal static class BlockObjectiveFinished
        {
            private static bool Prefix(Army army) => !IsPlayerLedArmy(army);
        }

        [HarmonyPatch(typeof(DisbandArmyAction), nameof(DisbandArmyAction.ApplyByInactivity))]
        internal static class BlockInactivity
        {
            private static bool Prefix(Army army) => !IsPlayerLedArmy(army);
        }

        [HarmonyPatch(typeof(DisbandArmyAction), nameof(DisbandArmyAction.ApplyByNoActiveWar))]
        internal static class BlockNoActiveWar
        {
            private static bool Prefix(Army army) => !IsPlayerLedArmy(army);
        }

        [HarmonyPatch(typeof(DisbandArmyAction), nameof(DisbandArmyAction.ApplyByUnknownReason))]
        internal static class BlockUnknownReason
        {
            private static bool Prefix(Army army) => !IsPlayerLedArmy(army);
        }
    }
}
