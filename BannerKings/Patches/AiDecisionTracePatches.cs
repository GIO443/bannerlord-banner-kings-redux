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

        // Party-target sibling of GuardSettlementMove, for SetMoveEscortParty /
        // SetMoveEngageParty. Those setters trigger a synchronous escort/engage
        // pathfind (via DefaultBehavior); if the TARGET PARTY is unreachable the
        // native pathfind HANGS the campaign thread — captured precisely as
        //   STUCK MobileParty.SetMoveEscortParty:lord_4_1_party_1  (GC frozen),
        // the army-gather case where a banner party is told to escort a leader it
        // can't reach (across water). Skip the move when the target is unreachable
        // by land (land-only party) or by land AND sea (naval); upgrade Default->
        // All when only a sea route exists. GetDistance is the hang-safe oracle.
        // Cheap escort/engage snapshot for the crash HTM (no GetDistance, so it
        // survives even if a distance probe is what wedges). Diagnostic-mode only.
        private static void CaptureEscortContext(MobileParty party, MobileParty target, MobileParty.NavigationType nav)
        {
            try
            {
                var sb = new System.Text.StringBuilder(256);
                try { sb.Append("escorter: ").Append(party?.Name?.ToString() ?? "?").Append(" [").Append(party?.StringId ?? "?").Append("]\n"); } catch { }
                try { var p = party.GetPosition2D; sb.Append("  pos: ").Append(p.X.ToString("0.0")).Append(',').Append(p.Y.ToString("0.0")).Append("  atSea: ").Append(party.IsCurrentlyAtSea).Append("  hasNaval: ").Append(party.HasNavalNavigationCapability).Append('\n'); } catch { }
                try { sb.Append("target: ").Append(target?.Name?.ToString() ?? "?").Append(" [").Append(target?.StringId ?? "?").Append("]\n"); } catch { }
                try { var t = target.GetPosition2D; sb.Append("  pos: ").Append(t.X.ToString("0.0")).Append(',').Append(t.Y.ToString("0.0")).Append("  atSea: ").Append(target.IsCurrentlyAtSea).Append("  inSettlement: ").Append(target.CurrentSettlement?.Name?.ToString() ?? "(none)").Append('\n'); } catch { }
                try { sb.Append("nav requested: ").Append(nav).Append('\n'); } catch { }
                BannerKings.Utils.FreezeWatchdog.SetMoveContext(sb.ToString());
            }
            catch { }
        }

        // Reconcile a party's IsCurrentlyAtSea flag to the terrain actually under
        // it. A desynced flag (on land but flagged at-sea, or vice versa — the
        // root behind the disband freeze) makes any pathfind that derives its nav
        // capability from the flag search the WRONG navmesh layer and hang. Cheap
        // terrain lookup, no navmesh search; only writes a genuinely-wrong flag.
        private static void ReconcileAtSea(MobileParty p)
        {
            if (p == null) return;
            try
            {
                if (p.CurrentSettlement != null) return; // in a settlement — flag irrelevant to field pathing
                var terrain = TaleWorlds.CampaignSystem.Campaign.Current.MapSceneWrapper.GetTerrainTypeAtPosition(p.Position);
                bool onWater = !TaleWorlds.CampaignSystem.Campaign.Current.Models.PartyNavigationModel
                    .IsTerrainTypeValidForNavigationType(terrain, MobileParty.NavigationType.Default);
                if (p.IsCurrentlyAtSea != onWater) p.IsCurrentlyAtSea = onWater;
            }
            catch { }
        }

        private static bool GuardPartyTargetMove(MobileParty party, MobileParty target, ref MobileParty.NavigationType navigationType)
        {
            if (party == null || target == null) return true;
            try
            {
                // Fix the AtSea/terrain desync on the escorter FIRST — the escort
                // pathfind derives its search layer from this flag, so a wrong
                // flag makes it spin even toward a "reachable" target. Same root
                // as the disband freeze, applied to the moving party here.
                ReconcileAtSea(party);

                var dm = TaleWorlds.CampaignSystem.Campaign.Current.Models.MapDistanceModel;

                // NAV-AGNOSTIC — unlike the settlement guard. The engine re-commits
                // an escort/engage each tick using the PARTY'S OWN capability (All/
                // Naval for a naval party), NOT Default, so a Default-only check
                // would no-op on exactly the naval-escort case that hangs. First,
                // unreachable by ANY means (land OR sea) → skip regardless of the
                // requested nav: the target genuinely can't be reached, so the
                // escort/engage pathfind would spin.
                float dAll = dm.GetDistance(party, target, MobileParty.NavigationType.All, out _);
                if (dAll >= 50000f) return false;

                // Reachable by sea but a Default (land) move was requested with no
                // land route: upgrade a naval party to All (sail to it), or skip a
                // land-only party that has no land route.
                if (navigationType == MobileParty.NavigationType.Default)
                {
                    float dLand = dm.GetDistance(party, target, MobileParty.NavigationType.Default, out _);
                    if (dLand >= 50000f)
                    {
                        bool naval = false;
                        try { naval = party.HasNavalNavigationCapability; } catch { }
                        if (naval) navigationType = MobileParty.NavigationType.All; // sail to escort
                        else return false; // land-only, no land route — skip
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
            // Vanilla signature is SetMoveEngageParty(MobileParty party, NavigationType
            // navigationType) — the target param is `party` (Harmony binds by name).
            private static bool Prefix(MobileParty __instance, MobileParty party, ref MobileParty.NavigationType navigationType)
            {
                BannerKings.Utils.FreezeWatchdog.Enter("MobileParty.SetMoveEngageParty",
                    BannerKings.Utils.TickTrace.IdOf(party ?? __instance));
                if (BannerKings.Utils.FreezeWatchdog.Enabled) CaptureEscortContext(__instance, party, navigationType);
                // Same hang surface as escort — engaging an unreachable target party.
                return GuardPartyTargetMove(__instance, party, ref navigationType);
            }
            private static void Postfix(MobileParty __instance, MobileParty party)
            {
                BannerKings.Utils.FreezeWatchdog.Exit();
                if (!ShouldTrace(__instance)) return;
                string targetName = party?.Name?.ToString() ?? "(null)";
                bool targetAtSea = false; try { targetAtSea = party?.IsCurrentlyAtSea ?? false; } catch { }
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
            private static bool Prefix(MobileParty __instance, MobileParty mobileParty, ref MobileParty.NavigationType navigationType)
            {
                BannerKings.Utils.FreezeWatchdog.Enter("MobileParty.SetMoveEscortParty",
                    BannerKings.Utils.TickTrace.IdOf(mobileParty ?? __instance));
                if (BannerKings.Utils.FreezeWatchdog.Enabled) CaptureEscortContext(__instance, mobileParty, navigationType);
                // Guard the escort pathfind: escorting a target the party can't
                // reach (across water — the army-gather hang) wedges the thread.
                return GuardPartyTargetMove(__instance, mobileParty, ref navigationType);
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

        // Guard + instrument the navmesh "find a reachable point" search — the
        // native call that wedges the campaign thread when a position's terrain
        // disagrees with the requested navigation type (the AtSea-on-land desync).
        // Army disband scatter goes through here (Army.DisperseInternal), as does
        // other party repositioning. It is NOT a SetMove*, so a hang here shows as
        // "current (idle)" in BK_freeze.txt and the auto-crash never sees it (the
        // exact pattern in the latest freeze log: heartbeats alive, GC flatlined,
        // no named handler).
        //
        // FIX: if the search CENTRE is itself invalid for the requested nav type,
        // the native search has no valid frontier and can spin forever — return
        // the centre as a best-effort point instead (the party stays put, corrected
        // on its next move; far better than a frozen game). INSTRUMENT: bracket the
        // call so when it DOES run and hangs, the watchdog names it and the
        // auto-crash fires with a report naming this call instead of freezing
        // unnamed. (Postfix runs Exit even when the Prefix returns false.)
        [HarmonyPatch(typeof(global::Helpers.NavigationHelper),
            nameof(global::Helpers.NavigationHelper.FindReachablePointAroundPosition),
            new System.Type[] { typeof(CampaignVec2), typeof(MobileParty.NavigationType), typeof(float), typeof(float), typeof(bool) })]
        internal static class FindReachablePointHangGuard
        {
            private static bool Prefix(CampaignVec2 center, MobileParty.NavigationType navigationCapability, ref CampaignVec2 __result, out bool __state)
            {
                // Mark this call ONLY when nothing else is in-flight (untracked
                // context — exactly the case the watchdog can't otherwise see). If
                // an outer BK handler called us, leave its marker alone so we don't
                // blind its coverage. __state tells the Postfix whether to Exit.
                __state = false;
                if (BannerKings.Utils.FreezeWatchdog.Enabled && BannerKings.Utils.FreezeWatchdog.IsIdle)
                {
                    BannerKings.Utils.FreezeWatchdog.Enter("NavigationHelper.FindReachablePointAroundPosition", "navmesh");
                    __state = true;
                }
                try
                {
                    var terrain = Campaign.Current.MapSceneWrapper.GetTerrainTypeAtPosition(center);
                    if (!Campaign.Current.Models.PartyNavigationModel.IsTerrainTypeValidForNavigationType(terrain, navigationCapability))
                    {
                        __result = center; // no valid frontier at the centre — don't let the native search spin
                        return false;
                    }
                }
                catch { }
                return true;
            }
            private static void Postfix(bool __state) { if (__state) BannerKings.Utils.FreezeWatchdog.Exit(); }
        }
    }
}
