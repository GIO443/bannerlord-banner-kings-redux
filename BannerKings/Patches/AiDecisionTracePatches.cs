using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace BannerKings.Patches
{
    // Postfix every MobileParty.SetMove* entry point so we can see the
    // chronological tape of "who decided this lord/party should go where,
    // and with what navigation type". Vanilla and NavalDLC AI both
    // ultimately call into these setters when they pick a behavior;
    // logging the call site of every assignment to a naval-capable lord
    // tells us why a naval+land lord ended up walking to a coast tile
    // instead of being routed through a port.
    //
    // Only naval-capable parties (HasNavalNavigationCapability) are
    // logged — non-naval parties have no embark path so their movement
    // decisions aren't relevant to the stuck-at-coast question.
    //
    // Output goes to BK_ai_decisions.txt via the buffered
    // AppendDiagnosticLine. Gated on the same LogHourlyTickPerf MCM
    // toggle as the rest of the trace facility.
    //
    // Why postfix instead of prefix: we want to see the *decided* state
    // (after vanilla's logic ran), not intercept it. Prefix would risk
    // observing a stale value if the setter mutates internal fields
    // before returning.
    internal static class AiDecisionTracePatches
    {
        private static bool ShouldTrace(MobileParty party)
        {
            try
            {
                if (party == null) return false;
                if (!Settings.BannerKingsSettings.Instance.LogHourlyTickPerf) return false;
                return party.HasNavalNavigationCapability;
            }
            catch { return false; }
        }

        private static string PartyState(MobileParty party)
        {
            try
            {
                string cur = party.CurrentSettlement?.Name?.ToString() ?? "(none)";
                bool atSea = false; try { atSea = party.IsCurrentlyAtSea; } catch { }
                bool hasLand = false; try { hasLand = party.HasLandNavigationCapability; } catch { }
                bool hasNaval = false; try { hasNaval = party.HasNavalNavigationCapability; } catch { }
                int ships = 0; try { ships = party.Ships?.Count ?? 0; } catch { }
                return $"@{cur} AtSea={atSea} HasLand={hasLand} HasNaval={hasNaval} Ships={ships}";
            }
            catch { return "@? state-read-failed"; }
        }

        private static void Log(string line)
        {
            try { BannerKings.BannerKingsCheats.AppendDiagnosticLine("ai_decisions.txt", line); }
            catch { }
        }

        // ---- Settlement-targeted setters ------------------------------------

        [HarmonyPatch(typeof(MobileParty), nameof(MobileParty.SetMoveGoToSettlement))]
        internal static class SetMoveGoToSettlementPostfix
        {
            // Freeze-watchdog bracket (independent of the gated trace below).
            // A late-campaign freeze sits in a NATIVE pathfind that runs when a
            // party commits its AI-chosen target right after the hourly think
            // (BK_freeze.txt: campaign thread frozen, GC stopped, last marker
            // BKArmy.AiHourlyTick — i.e. downstream of every instrumented BK
            // handler, in vanilla movement). If the path setup hangs inside the
            // setter, the watchdog names the TARGET settlement here.
            private static void Prefix(MobileParty __instance, Settlement settlement)
            {
                BannerKings.Utils.FreezeWatchdog.Enter("MobileParty.SetMoveGoToSettlement",
                    settlement?.StringId ?? BannerKings.Utils.TickTrace.IdOf(__instance));
            }
            private static void Postfix(MobileParty __instance, Settlement settlement, MobileParty.NavigationType navigationType, bool isTargetingThePort)
            {
                BannerKings.Utils.FreezeWatchdog.Exit();
                if (!ShouldTrace(__instance)) return;
                string targetName = settlement?.Name?.ToString() ?? "(null)";
                bool targetHasPort = false; try { targetHasPort = settlement?.HasPort ?? false; } catch { }
                Log($"{__instance.Name?.ToString() ?? "?"} {PartyState(__instance)} → SetMoveGoToSettlement(target={targetName} HasPort={targetHasPort}, nav={navigationType}, targetingPort={isTargetingThePort})");
            }
        }

        [HarmonyPatch(typeof(MobileParty), nameof(MobileParty.SetMovePatrolAroundSettlement))]
        internal static class SetMovePatrolAroundSettlementPostfix
        {
            private static void Prefix(MobileParty __instance, Settlement settlement)
            {
                BannerKings.Utils.FreezeWatchdog.Enter("MobileParty.SetMovePatrolAroundSettlement",
                    settlement?.StringId ?? BannerKings.Utils.TickTrace.IdOf(__instance));
            }
            private static void Postfix(MobileParty __instance, Settlement settlement, MobileParty.NavigationType navigationType, bool isTargetingThePort)
            {
                BannerKings.Utils.FreezeWatchdog.Exit();
                if (!ShouldTrace(__instance)) return;
                string targetName = settlement?.Name?.ToString() ?? "(null)";
                bool targetHasPort = false; try { targetHasPort = settlement?.HasPort ?? false; } catch { }
                Log($"{__instance.Name?.ToString() ?? "?"} {PartyState(__instance)} → SetMovePatrolAroundSettlement(target={targetName} HasPort={targetHasPort}, nav={navigationType}, targetingPort={isTargetingThePort})");
            }
        }

        [HarmonyPatch(typeof(MobileParty), nameof(MobileParty.SetMoveDefendSettlement))]
        internal static class SetMoveDefendSettlementPostfix
        {
            private static void Prefix(MobileParty __instance, Settlement settlement)
            {
                BannerKings.Utils.FreezeWatchdog.Enter("MobileParty.SetMoveDefendSettlement",
                    settlement?.StringId ?? BannerKings.Utils.TickTrace.IdOf(__instance));
            }
            private static void Postfix(MobileParty __instance, Settlement settlement)
            {
                BannerKings.Utils.FreezeWatchdog.Exit();
                if (!ShouldTrace(__instance)) return;
                string targetName = settlement?.Name?.ToString() ?? "(null)";
                bool targetHasPort = false; try { targetHasPort = settlement?.HasPort ?? false; } catch { }
                Log($"{__instance.Name?.ToString() ?? "?"} {PartyState(__instance)} → SetMoveDefendSettlement(target={targetName} HasPort={targetHasPort})");
            }
        }

        [HarmonyPatch(typeof(MobileParty), nameof(MobileParty.SetMoveBesiegeSettlement))]
        internal static class SetMoveBesiegeSettlementPostfix
        {
            private static void Prefix(MobileParty __instance, Settlement settlement)
            {
                BannerKings.Utils.FreezeWatchdog.Enter("MobileParty.SetMoveBesiegeSettlement",
                    settlement?.StringId ?? BannerKings.Utils.TickTrace.IdOf(__instance));
            }
            private static void Postfix(MobileParty __instance, Settlement settlement)
            {
                BannerKings.Utils.FreezeWatchdog.Exit();
                if (!ShouldTrace(__instance)) return;
                string targetName = settlement?.Name?.ToString() ?? "(null)";
                bool targetHasPort = false; try { targetHasPort = settlement?.HasPort ?? false; } catch { }
                Log($"{__instance.Name?.ToString() ?? "?"} {PartyState(__instance)} → SetMoveBesiegeSettlement(target={targetName} HasPort={targetHasPort})");
            }
        }

        [HarmonyPatch(typeof(MobileParty), nameof(MobileParty.SetMoveEngageParty))]
        internal static class SetMoveEngagePartyPostfix
        {
            private static void Prefix(MobileParty __instance, MobileParty mobileParty)
            {
                BannerKings.Utils.FreezeWatchdog.Enter("MobileParty.SetMoveEngageParty",
                    BannerKings.Utils.TickTrace.IdOf(mobileParty ?? __instance));
            }
            private static void Postfix(MobileParty __instance, MobileParty mobileParty)
            {
                BannerKings.Utils.FreezeWatchdog.Exit();
                if (!ShouldTrace(__instance)) return;
                string targetName = mobileParty?.Name?.ToString() ?? "(null)";
                bool targetAtSea = false; try { targetAtSea = mobileParty?.IsCurrentlyAtSea ?? false; } catch { }
                Log($"{__instance.Name?.ToString() ?? "?"} {PartyState(__instance)} → SetMoveEngageParty(target={targetName} targetAtSea={targetAtSea})");
            }
        }

        // ---- Point-targeted setters -----------------------------------------

        [HarmonyPatch(typeof(MobileParty), nameof(MobileParty.SetMoveGoToPoint))]
        internal static class SetMoveGoToPointPostfix
        {
            private static void Prefix(MobileParty __instance)
            {
                BannerKings.Utils.FreezeWatchdog.Enter("MobileParty.SetMoveGoToPoint", BannerKings.Utils.TickTrace.IdOf(__instance));
            }
            private static void Postfix(MobileParty __instance, Vec2 position)
            {
                BannerKings.Utils.FreezeWatchdog.Exit();
                if (!ShouldTrace(__instance)) return;
                Log($"{__instance.Name?.ToString() ?? "?"} {PartyState(__instance)} → SetMoveGoToPoint(pos=({position.X:0.0},{position.Y:0.0}))");
            }
        }

        [HarmonyPatch(typeof(MobileParty), nameof(MobileParty.SetMovePatrolAroundPoint))]
        internal static class SetMovePatrolAroundPointPostfix
        {
            private static void Prefix(MobileParty __instance)
            {
                BannerKings.Utils.FreezeWatchdog.Enter("MobileParty.SetMovePatrolAroundPoint", BannerKings.Utils.TickTrace.IdOf(__instance));
            }
            private static void Postfix(MobileParty __instance, Vec2 position)
            {
                BannerKings.Utils.FreezeWatchdog.Exit();
                if (!ShouldTrace(__instance)) return;
                Log($"{__instance.Name?.ToString() ?? "?"} {PartyState(__instance)} → SetMovePatrolAroundPoint(pos=({position.X:0.0},{position.Y:0.0}))");
            }
        }

        // ---- Sea-only setters (only fire when vanilla / NavalDLC initiates a sea move) ----

        [HarmonyPatch(typeof(MobileParty), nameof(MobileParty.SetSailAtPosition))]
        internal static class SetSailAtPositionPostfix
        {
            private static void Prefix(MobileParty __instance)
            {
                BannerKings.Utils.FreezeWatchdog.Enter("MobileParty.SetSailAtPosition", BannerKings.Utils.TickTrace.IdOf(__instance));
            }
            private static void Postfix(MobileParty __instance, Vec2 position)
            {
                BannerKings.Utils.FreezeWatchdog.Exit();
                if (!ShouldTrace(__instance)) return;
                Log($"{__instance.Name?.ToString() ?? "?"} {PartyState(__instance)} → SetSailAtPosition(pos=({position.X:0.0},{position.Y:0.0}))");
            }
        }
    }
}
