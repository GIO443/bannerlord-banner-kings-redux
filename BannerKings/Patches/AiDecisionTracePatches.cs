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

        // Build a rich, human-readable snapshot of a settlement move and hand
        // it to the freeze watchdog. If the engine's follower for THIS move
        // hangs, the watchdog dumps this verbatim into the crash HTM so the
        // report names the party, its position/flags, the target, the gate
        // position, and the GetDistance values — the data that has been
        // invisible in every freeze so far. Runs on the campaign thread (safe
        // state); every field is independently guarded so a single missing
        // value never aborts the whole snapshot. Diagnostic mode only.
        private static void CaptureMoveContext(MobileParty party, Settlement settlement, MobileParty.NavigationType navigationType)
        {
            try
            {
                var sb = new System.Text.StringBuilder(384);
                // Party — cheap, hang-free fields first.
                try { sb.Append("party: ").Append(party?.Name?.ToString() ?? "?").Append(" [").Append(party?.StringId ?? "?").Append("]\n"); } catch { }
                try { sb.Append("  kind: ").Append(party.IsCaravan ? "caravan" : party.IsLordParty ? "lord" : party.IsMilitia ? "militia" : "other").Append('\n'); } catch { }
                try { var p = party.GetPosition2D; sb.Append("  pos: ").Append(p.X.ToString("0.0")).Append(',').Append(p.Y.ToString("0.0")).Append('\n'); } catch { }
                try { sb.Append("  atSea: ").Append(party.IsCurrentlyAtSea).Append("  hasNaval: ").Append(party.HasNavalNavigationCapability).Append("  hasLand: ").Append(party.HasLandNavigationCapability).Append('\n'); } catch { }
                try { sb.Append("  currentSettlement: ").Append(party.CurrentSettlement?.Name?.ToString() ?? "(none)").Append('\n'); } catch { }
                // Target.
                try { sb.Append("target: ").Append(settlement?.Name?.ToString() ?? "?").Append(" [").Append(settlement?.StringId ?? "?").Append("]\n"); } catch { }
                try { sb.Append("  type: ").Append(settlement.IsTown ? "town" : settlement.IsCastle ? "castle" : settlement.IsVillage ? "village" : "other").Append("  hasPort: ").Append(settlement.HasPort).Append('\n'); } catch { }
                try { var g = settlement.GatePosition; sb.Append("  gate: ").Append(g.X.ToString("0.0")).Append(',').Append(g.Y.ToString("0.0")).Append('\n'); } catch { }
                try { sb.Append("  owner: ").Append(settlement.OwnerClan?.Name?.ToString() ?? "(none)").Append("  faction: ").Append(settlement.MapFaction?.Name?.ToString() ?? "(none)").Append('\n'); } catch { }
                try { sb.Append("nav requested: ").Append(navigationType).Append('\n'); } catch { }
                // Set the cheap snapshot NOW, before the potentially-hanging
                // GetDistance calls below — so the report has context even if a
                // distance probe is what wedges.
                BannerKings.Utils.FreezeWatchdog.SetMoveContext(sb.ToString());

                var dm = TaleWorlds.CampaignSystem.Campaign.Current.Models.MapDistanceModel;
                try { float d = dm.GetDistance(party, settlement, false, MobileParty.NavigationType.Default, out _); sb.Append("GetDistance(Default/land): ").Append(d >= 50000f ? d.ToString("0") + " (UNREACHABLE)" : d.ToString("0.0")).Append('\n'); } catch { sb.Append("GetDistance(Default): error\n"); }
                try { float d = dm.GetDistance(party, settlement, false, MobileParty.NavigationType.All, out _); sb.Append("GetDistance(All/land+sea): ").Append(d >= 50000f ? d.ToString("0") + " (UNREACHABLE)" : d.ToString("0.0")).Append('\n'); } catch { sb.Append("GetDistance(All): error\n"); }
                BannerKings.Utils.FreezeWatchdog.SetMoveContext(sb.ToString());
            }
            catch { /* diagnostics must never disturb the move */ }
        }

        // Shared reachability guard for settlement-targeted moves. Returns false
        // to SKIP a move whose target the party cannot travel-pathfind to (the
        // native follower would hang the campaign thread). For a NAVAL-capable
        // party on a Default (land) move with no land route but a sea route,
        // upgrades nav to All by ref so the engine sails/auto-boards instead of
        // dead-ending. Only Default moves are inspected; non-Default (the AI
        // already chose a sea/any route) and reachable targets pass untouched, so
        // a degenerate d<=0/NaN (mid-transition / inside a settlement) is never
        // wrongly dropped.
        //
        // Used by GoToSettlement AND the combat setters (Besiege / Raid /
        // Defend): every settlement-target setter shares the same native-pathfind
        // hang surface, but only GoToSettlement was guarded — so an army (or a
        // party RELEASED when that army disbands) resuming an unreachable
        // besiege/raid/defend objective re-issued a hanging move every tick. That
        // is the deterministic "disbanding this army freezes the game" report:
        // the released members resume a sea-locked combat objective through a
        // previously-unguarded setter.
        private static bool GuardSettlementMove(MobileParty party, Settlement settlement, ref MobileParty.NavigationType navigationType)
        {
            if (settlement == null || party == null) return true;
            if (navigationType != MobileParty.NavigationType.Default) return true;
            try
            {
                var dm = TaleWorlds.CampaignSystem.Campaign.Current.Models.MapDistanceModel;
                bool naval = false;
                try { naval = party.HasNavalNavigationCapability; } catch { }
                if (!naval)
                {
                    float d = dm.GetDistance(party, settlement, false, MobileParty.NavigationType.Default, out _);
                    if (d >= 50000f) return false; // land-only, no land route — skip
                }
                else
                {
                    float dLand = dm.GetDistance(party, settlement, false, MobileParty.NavigationType.Default, out _);
                    if (dLand >= 50000f)
                    {
                        float dAll = dm.GetDistance(party, settlement, false, MobileParty.NavigationType.All, out _);
                        if (dAll < 50000f) navigationType = MobileParty.NavigationType.All; // sail there
                        else return false; // unreachable by land or sea — skip
                    }
                }
            }
            catch { }
            return true;
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
                    // Capture a rich snapshot for the crash HTM in case THIS move
                    // hangs. Cheap fields first (so even if a later GetDistance
                    // itself hangs, the report still has party/target context),
                    // then the distances. Diagnostic mode only.
                    CaptureMoveContext(__instance, settlement, navigationType);
                }

                // CENTRAL reachability guard (always on — the freeze-class fix).
                // Every freeze in this saga is a movement command to a target the
                // engine can't travel-pathfind to; the follower then hangs the
                // campaign thread. Per-caller guards can't cover VANILLA deciders,
                // so gate the setter itself. Shared with the combat setters below.
                if (!GuardSettlementMove(__instance, settlement, ref navigationType))
                    return false;

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
            private static bool Prefix(MobileParty __instance, Settlement settlement, ref MobileParty.NavigationType navigationType)
            {
                BannerKings.Utils.FreezeWatchdog.Enter("MobileParty.SetMoveDefendSettlement",
                    settlement?.StringId ?? BannerKings.Utils.TickTrace.IdOf(__instance));
                // Same hang surface as GoToSettlement — guard it (a released army
                // member resuming an unreachable defend objective hangs the tick).
                return GuardSettlementMove(__instance, settlement, ref navigationType);
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
            private static bool Prefix(MobileParty __instance, Settlement settlement, ref MobileParty.NavigationType navigationType)
            {
                BannerKings.Utils.FreezeWatchdog.Enter("MobileParty.SetMoveBesiegeSettlement",
                    settlement?.StringId ?? BannerKings.Utils.TickTrace.IdOf(__instance));
                // Same hang surface as GoToSettlement — guard it (a released army
                // member resuming an unreachable besiege objective hangs the tick).
                return GuardSettlementMove(__instance, settlement, ref navigationType);
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
            private static bool Prefix(MobileParty __instance, Settlement settlement, ref MobileParty.NavigationType navigationType)
            {
                BannerKings.Utils.FreezeWatchdog.Enter("MobileParty.SetMoveRaidSettlement",
                    settlement?.StringId ?? BannerKings.Utils.TickTrace.IdOf(__instance));
                // Same hang surface as GoToSettlement — guard it (a released army
                // member or a bandit resuming an unreachable raid hangs the tick).
                return GuardSettlementMove(__instance, settlement, ref navigationType);
            }
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
