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

        // Walk the call stack to the first real caller of the SetMove setter,
        // skipping this patch class, Harmony's dynamic wrapper (null declaring
        // type), the System/Harmony/MonoMod plumbing, and the original setter
        // method itself. Returns "Type.Method" (BK or vanilla) so a freeze line
        // names who issued the move. Only called when the watchdog is enabled.
        private static string FindMoveCaller()
        {
            try
            {
                var st = new System.Diagnostics.StackTrace(2, false); // skip this + the Prefix
                int n = st.FrameCount;
                for (int i = 0; i < n && i < 12; i++)
                {
                    var m = st.GetFrame(i)?.GetMethod();
                    var t = m?.DeclaringType;
                    if (t == null) continue; // Harmony dynamic method
                    string ns = t.Namespace ?? "";
                    if (ns.StartsWith("System") || ns.StartsWith("HarmonyLib") || ns.StartsWith("MonoMod")) continue;
                    if (t.Name != null && t.Name.Contains("AiDecisionTrace")) continue;
                    // SetPartyAiAction is vanilla's move-apply funnel — skip it
                    // so we name the REAL decider above it (a BK behaviour or a
                    // vanilla AI behaviour), not the plumbing.
                    if (t.Name == "SetPartyAiAction") continue;
                    if (m.Name == "SetMoveGoToSettlement") continue; // the original being wrapped
                    return (t.Name ?? "?") + "." + (m.Name ?? "?");
                }
            }
            catch { }
            return "?";
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
            private static bool Prefix(MobileParty __instance, Settlement settlement, ref MobileParty.NavigationType navigationType)
            {
                // Set the watchdog marker FIRST (when on) so that if the
                // reachability GetDistance below itself hangs (broken SOURCE
                // face), the freeze is still named SetMoveGoToSettlement rather
                // than unnamed. Balanced by the Postfix's unconditional Exit().
                // FindMoveCaller skips the vanilla SetPartyAiAction funnel to
                // name the REAL decider (BK behaviour or vanilla AI behaviour):
                //   last MobileParty.SetMoveGoToSettlement←CallBannersGoal.ApplyGoal:town_B2
                if (BannerKings.Utils.FreezeWatchdog.Enabled)
                {
                    BannerKings.Utils.FreezeWatchdog.Enter(
                        "MobileParty.SetMoveGoToSettlement←" + FindMoveCaller(),
                        settlement?.StringId ?? BannerKings.Utils.TickTrace.IdOf(__instance));
                }

                // CENTRAL land-reachability guard (always on — the freeze-class
                // fix). Every freeze in this saga is a movement command to a
                // target the engine can't travel-pathfind to; the follower then
                // hangs the campaign thread. Per-caller guards (FindArmyObjective
                // .16.7, RouteCaravanHopByHop .16.11, militia escort .16.12)
                // can't cover VANILLA deciders — and the caller capture proved
                // some come straight from vanilla (SetPartyAiAction.ApplyInternal
                // :castle_A6, GC frozen 3min+). So gate the setter itself.
                //
                // Only Default (land) nav moves are inspected. LAND-ONLY parties
                // whose target has no land route are SKIPPED (no sea escape).
                // NAVAL-capable parties whose target has no land route but is
                // sea-reachable are UPGRADED to All so the engine sails/auto-
                // boards instead of hanging a land-only pathfind (the vanilla
                // PartyHourlyAiTick:town_N2 freeze). Naval moves that ARE land-
                // reachable, and all non-Default moves, are left untouched, so
                // NavalDLC's normal sea routing is unaffected. Act ONLY on a
                // clearly-huge distance (unreachable sentinel) — NOT on a
                // degenerate d<=0/NaN (party mid-transition / inside a
                // settlement), so a valid move is never wrongly dropped.
                if (settlement != null && __instance != null
                    && navigationType == MobileParty.NavigationType.Default)
                {
                    bool naval = false;
                    try { naval = __instance.HasNavalNavigationCapability; } catch { }
                    if (!naval)
                    {
                        // LAND-ONLY party: a Default move to a land-unreachable
                        // target hangs the follower and there is no sea escape.
                        // Skip; the AI re-decides next tick.
                        try
                        {
                            float d = TaleWorlds.CampaignSystem.Campaign.Current.Models.MapDistanceModel
                                .GetDistance(__instance, settlement, false, MobileParty.NavigationType.Default, out _);
                            if (d >= 50000f) return false; // clearly-unreachable land target — don't commit
                        }
                        catch { }
                    }
                    else
                    {
                        // NAVAL-CAPABLE party on a Default (land-only) move. If the
                        // target has NO land route the land follower HANGS — the
                        // vanilla-issued AiPartyThinkBehavior.PartyHourlyAiTick
                        // :town_N2 freeze (a Nord coastal/island town a sailing
                        // lord was sent to with land nav). The party CAN reach it
                        // by sea, so upgrade the nav type to All by ref: the engine
                        // then routes via a port + auto-board instead of dead-
                        // ending a land path. Only substituted when there is no
                        // land route at all — exactly the case where sea routing
                        // is correct, so the cosmetic "land unit on water" glitch
                        // (which needs a land route to shortcut over) doesn't
                        // arise. A Default move that IS land-reachable is left
                        // untouched. If even All can't reach it, skip.
                        try
                        {
                            var dm = TaleWorlds.CampaignSystem.Campaign.Current.Models.MapDistanceModel;
                            float dLand = dm.GetDistance(__instance, settlement, false, MobileParty.NavigationType.Default, out _);
                            if (dLand >= 50000f)
                            {
                                float dAll = dm.GetDistance(__instance, settlement, false, MobileParty.NavigationType.All, out _);
                                if (dAll < 50000f) navigationType = MobileParty.NavigationType.All; // sail there
                                else return false; // unreachable by land or sea — don't commit
                            }
                        }
                        catch { }
                    }
                }

                return true;
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

        [HarmonyPatch(typeof(MobileParty), nameof(MobileParty.SetMoveEscortParty))]
        internal static class SetMoveEscortPartyPostfix
        {
            // Militias / escorts move via this setter, NOT SetMoveGoToSettlement
            // — the one SetMove* variant the brackets missed. A capture showed a
            // malformed militia (militias_of_militias_of_town_V6_aaa1_aaa1)
            // ticking just before a frozen native pathfind; if a broken escort
            // party hangs here, name it + its escort target.
            private static void Prefix(MobileParty __instance, MobileParty mobileParty)
            {
                BannerKings.Utils.FreezeWatchdog.Enter("MobileParty.SetMoveEscortParty",
                    BannerKings.Utils.TickTrace.IdOf(mobileParty ?? __instance));
            }
            private static void Postfix(MobileParty __instance)
            {
                BannerKings.Utils.FreezeWatchdog.Exit();
            }
        }

        // ---- Movers the earlier brackets missed -----------------------------
        // A freeze capture showed `current (idle); last BKArmy.AiHourlyTick:
        // lord_4_26_party_1` with frozen GC — i.e. the hang is NOT in any of
        // the 9 already-bracketed setters (those would show as `current`). The
        // remaining engine movers that pathfind are RaidSettlement (lords raid
        // constantly — prime suspect), GoAroundParty, GoToInteractablePoint and
        // ToNearestLand. Bracket them so the next capture names the exact one
        // + its target. __args is used (not named params) so no signature
        // mismatch can break the patch.
        private static string FirstArgId(object[] args, MobileParty fallback)
        {
            try
            {
                if (args != null && args.Length > 0)
                {
                    if (args[0] is Settlement s) return s?.StringId;
                    if (args[0] is MobileParty p) return BannerKings.Utils.TickTrace.IdOf(p);
                }
            }
            catch { }
            return BannerKings.Utils.TickTrace.IdOf(fallback);
        }

        [HarmonyPatch(typeof(MobileParty), nameof(MobileParty.SetMoveRaidSettlement))]
        internal static class SetMoveRaidSettlementBracket
        {
            private static void Prefix(MobileParty __instance, object[] __args)
            { BannerKings.Utils.FreezeWatchdog.Enter("MobileParty.SetMoveRaidSettlement", FirstArgId(__args, __instance)); }
            private static void Postfix() { BannerKings.Utils.FreezeWatchdog.Exit(); }
        }

        [HarmonyPatch(typeof(MobileParty), nameof(MobileParty.SetMoveGoAroundParty))]
        internal static class SetMoveGoAroundPartyBracket
        {
            private static void Prefix(MobileParty __instance, object[] __args)
            { BannerKings.Utils.FreezeWatchdog.Enter("MobileParty.SetMoveGoAroundParty", FirstArgId(__args, __instance)); }
            private static void Postfix() { BannerKings.Utils.FreezeWatchdog.Exit(); }
        }

        [HarmonyPatch(typeof(MobileParty), nameof(MobileParty.SetMoveGoToInteractablePoint))]
        internal static class SetMoveGoToInteractablePointBracket
        {
            private static void Prefix(MobileParty __instance)
            { BannerKings.Utils.FreezeWatchdog.Enter("MobileParty.SetMoveGoToInteractablePoint", BannerKings.Utils.TickTrace.IdOf(__instance)); }
            private static void Postfix() { BannerKings.Utils.FreezeWatchdog.Exit(); }
        }

        [HarmonyPatch(typeof(MobileParty), nameof(MobileParty.SetMoveToNearestLand))]
        internal static class SetMoveToNearestLandBracket
        {
            private static void Prefix(MobileParty __instance)
            { BannerKings.Utils.FreezeWatchdog.Enter("MobileParty.SetMoveToNearestLand", BannerKings.Utils.TickTrace.IdOf(__instance)); }
            private static void Postfix() { BannerKings.Utils.FreezeWatchdog.Exit(); }
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
