using BannerKings.Managers.Institutions.Religions;
using BannerKings.Managers.Institutions.Religions.Doctrines;
using BannerKings.Managers.Shipping;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace BannerKings.Behaviours.Shipping
{
    public class BKShippingBehavior : BannerKingsBehavior
    {
        private static readonly MethodInfo Caravans_ThinkNextDestination = AccessTools.Method(typeof(BKCaravansBehavior), "ThinkNextDestination");

        // BK's legacy SetTravel timer-teleport survives v1.6.10 as the
        // shipping fallback for users *without* War Sails (NavalDLC). With
        // War Sails the engine provides naval pathfinding + at-sea state,
        // so AI caravans + the player use vanilla SetSailAtPosition for
        // visible sailing. Without War Sails there is no naval AI and
        // HasPort returns false on every settlement, so we keep BK's
        // self-contained timer-teleport: player picks a destination from
        // a hard-coded canonical-ports list, time advances via the
        // bk_shipping_wait menu, party teleports on arrival.
        //
        // The `sailing` dict + Travel struct + AddParty/RemoveParty/
        // TryGetTravelInfo/SetTravel/FinishTravel/TickSailing+Impl/
        // UpdateShippingMenu / CalculatePrice / CalculateArrival members
        // below all live exclusively on the !WarSails path.
        private Dictionary<MobileParty, Travel> sailing = new Dictionary<MobileParty, Travel>(20);

        // Canonical port set used when War Sails isn't installed (HasPort
        // is set by NavalDLC, so without it nothing reads as a port). This
        // list mirrors the user-confirmed authoritative port set: every
        // base-game coastal town with a real harbour. Used by the
        // !WarSails player ship-menu to enumerate destinations.
        private static readonly HashSet<string> CanonicalPortStringIds = new HashSet<string>
        {
            // Aserai south coast
            "town_A2", "town_A8", "town_A4", "town_A6",
            // Empire south & east coast
            "town_EW4", "town_EN2", "town_ES7", "town_ES2",
            // Sturgia & northern coast
            "town_S1", "town_S7", "town_S3",
            // Vlandia west coast
            "town_V7", "town_V5",
            // Battania
            "town_B3",
            // Nord (War Sails only — included so the same list works as
            // documentation, but unreachable when !WarSails since these
            // settlements are added by the DLC).
            "town_N1", "town_N2", "town_N3", "town_N4",
        };

        public static bool IsBKShippingPort(Settlement s)
        {
            if (s == null) return false;
            try
            {
                if (BannerKings.Utils.ModCompat.WarSails) return s.HasPort;
            }
            catch { }
            return s.StringId != null && CanonicalPortStringIds.Contains(s.StringId);
        }

        private void AddParty(MobileParty party, Settlement destination, CampaignTime time)
        {
            sailing[party] = new Travel(party, time, destination);
        }

        public void RemoveParty(MobileParty party)
        {
            if (sailing.ContainsKey(party)) sailing.Remove(party);
        }

        // Diagnostic surface for the caravan watchdog. Returns the sailing
        // entry's destination + arrival time, or (null, default) if BK isn't
        // tracking this party. Only meaningful on the !WarSails path; the
        // dict is empty otherwise.
        public bool TryGetTravelInfo(MobileParty party, out Settlement destination, out CampaignTime arrival)
        {
            if (party != null && sailing.TryGetValue(party, out var t))
            {
                destination = t.Destination;
                arrival = t.Arrival;
                return true;
            }
            destination = null;
            arrival = default;
            return false;
        }

        // Per-handler perf accumulator. Each registered hourly listener
        // records its own wall-time per call here; once an in-game hour
        // ends we sum totals and emit a single line if any handler
        // exceeded 100ms. Keeps the surface tiny (one file write per
        // game-hour) instead of one per party. Toggle via MCM →
        // Diagnostics → Log Hourly Tick Perf so the file isn't created
        // for users who don't need it.
        private static readonly System.Collections.Generic.Dictionary<string, long> _perfTotalsTicks
            = new System.Collections.Generic.Dictionary<string, long>();
        private static long _perfWindowStartHour = -1;

        public static void PerfRecordPublic(string handlerName, System.Diagnostics.Stopwatch sw) => PerfRecord(handlerName, sw);

        // Tracer for diagnosing indefinite freezes that the per-hour
        // accumulator can't catch (because the hour never finishes).
        // Writes ENTER and EXIT lines immediately to disk. When a freeze
        // hits, the last unmatched ENTER in the tracer file names the
        // hung handler. Off by default (same MCM toggle as the perf
        // accumulator). File I/O on every handler call is too expensive
        // for steady-state, but acceptable as a diagnostic.
        public static System.Diagnostics.Stopwatch TraceEnter(string handlerName)
        {
            if (!Settings.BannerKingsSettings.Instance.LogHourlyTickPerf) return null;
            try
            {
                long curHour;
                try { curHour = (long)TaleWorlds.CampaignSystem.CampaignTime.Now.ToHours; }
                catch { curHour = 0L; }
                BannerKings.BannerKingsCheats.AppendDiagnosticLine("tick_trace.txt",
                    $"ENTER {handlerName} at game-hour {curHour}");
            }
            catch { /* defensive */ }
            return System.Diagnostics.Stopwatch.StartNew();
        }

        public static void TraceExit(string handlerName, System.Diagnostics.Stopwatch sw)
        {
            if (sw == null) return;
            sw.Stop();
            try
            {
                double ms = sw.Elapsed.TotalMilliseconds;
                // Always write EXIT so we can verify each handler returned.
                // The diagnostic is short-lived; the file is overwritten by
                // the next run anyway, and the appendallText is gated on
                // the MCM toggle.
                BannerKings.BannerKingsCheats.AppendDiagnosticLine("tick_trace.txt",
                    $"EXIT  {handlerName} ({ms:0} ms)");
            }
            catch { /* defensive */ }
        }

        private static void PerfRecord(string handlerName, System.Diagnostics.Stopwatch sw)
        {
            if (!Settings.BannerKingsSettings.Instance.LogHourlyTickPerf) return;
            try
            {
                long ticks = sw.ElapsedTicks;
                long curHour;
                try { curHour = (long)TaleWorlds.CampaignSystem.CampaignTime.Now.ToHours; }
                catch { return; }
                if (_perfWindowStartHour < 0) _perfWindowStartHour = curHour;
                if (curHour != _perfWindowStartHour)
                {
                    // Always emit a per-hour line so we can see whether the
                    // freeze is during an hour where BK is doing nothing
                    // (= someone else's problem) or during one where a
                    // specific BK handler dominates.
                    var sb = new System.Text.StringBuilder();
                    long totalTicks = 0;
                    foreach (var kv in _perfTotalsTicks)
                    {
                        double ms = (double)kv.Value * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                        sb.AppendLine($"  {kv.Key}: {ms:0.0} ms");
                        totalTicks += kv.Value;
                    }
                    double totalMs = (double)totalTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                    BannerKings.BannerKingsCheats.AppendDiagnosticLine("hourly_perf.txt",
                        $"hour {_perfWindowStartHour} (BK total {totalMs:0.0} ms):\n{(sb.Length == 0 ? "  (no BK handler activity)\n" : sb.ToString())}");
                    _perfTotalsTicks.Clear();
                    _perfWindowStartHour = curHour;
                }
                if (_perfTotalsTicks.TryGetValue(handlerName, out var existing))
                    _perfTotalsTicks[handlerName] = existing + ticks;
                else
                    _perfTotalsTicks[handlerName] = ticks;
            }
            catch { /* defensive */ }
        }

        // Per-party redirect cache: (last evaluated target, last evaluated
        // hour). RedirectAIToShippingPort is called per party per hourly
        // tick and does heavy work — full graph walk, Dijkstra path find,
        // sometimes a MapDistanceModel.GetDistance pathfind. When the
        // party's target is the same as the previous evaluation and not
        // many hours have passed, the result will be the same; skipping
        // the recompute drops the per-party hourly cost to a dict lookup.
        // Without this, populated campaigns produced minute-long freezes
        // on hour rollovers when many parties qualified for the redirect.
        // Non-serialized — recomputes from scratch on load.
        private readonly Dictionary<MobileParty, (Settlement target, long hour)> redirectCache
            = new Dictionary<MobileParty, (Settlement, long)>();
        private const int RedirectCacheLifetimeHours = 6;

        // Movement-progress tracker. Records the last position at which a
        // party was redirect-eligible plus a stuck-tick counter. Catches
        // the "Kras the Baker → Gretysfjord" pattern where vanilla
        // MapDistanceModel.GetDistance reports a finite path (so loop-
        // prevention's reachability check would let the caravan bail
        // silently) but the caravan in practice doesn't move. When
        // stuck-ticks crosses BypassLoopPreventionThreshold we force the
        // full redirect logic to run regardless of cache or loop-
        // prevention so the seaFallback / alternative-target branches
        // get a chance to assign a different reachable destination.
        private readonly Dictionary<MobileParty, (Vec2 lastPos, int stuckTicks)> progressTracker
            = new Dictionary<MobileParty, (Vec2, int)>();
        private const float ProgressEpsilonPerTick = 1.5f;
        private const int BypassLoopPreventionThreshold = 3;

        // Cooldown between rescue attempts for the same party. Vanilla
        // CaravanAi / LordAi re-pick TargetSettlement on a slower tick than
        // the hourly redirect, so back-to-back rescues fire before the AI
        // has a chance to set a sensible new destination after the previous
        // settlement visit. Without this, lord parties whose target
        // happens to equal the rescue town (e.g. Voleric's Party heading
        // to Charas, rescued INTO Charas, target=Charas, exit, redirect
        // re-rescues to Charas) ping-pong forever between in-and-out of
        // the rescue town. 24 game-hours is enough for vanilla AI to tick
        // at least once and update the target.
        private const int RescueCooldownHours = 24;
        private readonly Dictionary<MobileParty, long> lastRescueHour
            = new Dictionary<MobileParty, long>();

        // Last town the party was rescued into. Used by the rescue branches
        // to detect oscillation: if the next rescue would drop the party
        // into the *same* town again, the rescue is a no-op for forward
        // progress (caravan auto-leaves and returns to the same broken
        // tile near the gate). On a repeat hit we instead advance to the
        // next graph node along the planned path so the caravan teleports
        // past whatever navmesh obstacle is trapping them.
        private readonly Dictionary<MobileParty, Settlement> lastRescueTown
            = new Dictionary<MobileParty, Settlement>();

        // Rescue oscillation tracking. When the same party gets rescued ≥
        // OptOutRescueThreshold times within OscillationWindowHours, we
        // conclude that BK's shipping pipeline isn't going to make this
        // caravan progress (typically a navmesh defect at a specific port's
        // exit tile — see Amprela at 744.4,522.7 in the BK_redirect.txt
        // log of 2026-05-01). Mark the party as BK-opt-out and let vanilla
        // CaravanAi handle them via its own hourly thinking. Re-evaluation
        // happens after the cooldown.
        private const int OscillationWindowHours = 24;
        private const int OptOutRescueThreshold = 3;
        private const int OptOutCooldownHours = 48;
        private readonly Dictionary<MobileParty, List<long>> rescueHistory
            = new Dictionary<MobileParty, List<long>>();
        private readonly Dictionary<MobileParty, long> bkOptOutUntilHour
            = new Dictionary<MobileParty, long>();

        // Hop-by-hop traversal state. Caravans focus on a single intended
        // destination but walk the shipping graph one hop at a time so each
        // pathfind segment is short and reliable. Intermediate hops are
        // walked via SetMoveGoToPoint (no settlement entry, no trade); only
        // the intended destination is entered. The dict tracks (intended,
        // currentWaypoint) per caravan so the tick handler can detect
        // arrival-at-waypoint and advance to the next hop.
        // Non-serialized: caravans get a fresh routing decision on load.
        private readonly Dictionary<MobileParty, (Settlement intended, Settlement waypoint)> hopByHopState
            = new Dictionary<MobileParty, (Settlement, Settlement)>();

        // Tracks the most-recent port a party LEFT and the hour it left.
        // Used by the sea-edge boarding redirect to detect the port-bounce
        // pattern where vanilla CaravanAi's auto-target makes BK want to
        // redirect a caravan back to the same port it just left for "the
        // next sea hop" — re-entering the same port immediately. NavalDLC's
        // NavigationTransition handles boarding on port departure; BK's
        // redirect at this moment is redundant. Window-bounded so longer
        // trips that legitimately need a second redirect still get one.
        // Non-serialized: rebuilt from runtime events.
        private readonly Dictionary<MobileParty, (Settlement port, long hour)> lastPortLeft
            = new Dictionary<MobileParty, (Settlement, long)>();
        // Hours after leaving a port within which BK suppresses redirects
        // pointing back at that same port. Keep small — the boarding
        // transition fires on port departure, so the caravan is committed
        // to the next leg within an hour or two.
        private const long BoardingBounceWindowHours = 3;
        // Final-approach distance: when caravan position is within this
        // many units of the intended destination's gate, switch from
        // SetMoveGoToPoint waypoints to SetMoveGoToSettlement so vanilla
        // auto-enter fires. 8f matches roughly the engine's auto-enter
        // proximity, with a small buffer.
        private const float HopFinalApproachDistance = 8f;
        // Waypoint-arrival threshold: when caravan is within this many
        // units of the current waypoint's gate, advance to next hop.
        // Smaller than HopFinalApproachDistance so we don't accidentally
        // trip auto-enter on an intermediate.
        private const float HopArrivalDistance = 4f;

        public override void RegisterEvents()
        {
            CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
            CampaignEvents.AfterSettlementEntered.AddNonSerializedListener(this, AfterSettlementEntered);
            CampaignEvents.OnSettlementLeftEvent.AddNonSerializedListener(this, OnSettlementLeft);
            CampaignEvents.HourlyTickPartyEvent.AddNonSerializedListener(this, TickParty);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this,
                BannerKings.Utils.TickTrace.Wrap("BKShipping.DailyNavalActivityProbe", DailyNavalActivityProbe));
            // In-session convoy beaching sweep. LoadCleanup catches the
            // signatures on save load, but mid-session a beach can persist
            // for the entire play session because there's no other detector
            // running. Daily cadence is conservative — too low to risk the
            // oscillation problems that disabled the old hourly
            // UnifiedRescueSweep, high enough that a beached convoy gets
            // unstuck within a game-day instead of waiting for save reload.
            //
            // Scoped to caravans only (Convoy invariant per the design memo:
            // naval-capable lords legitimately spend most of the campaign on
            // land between voyages). Reuses the same Mismatch G + C1 detection
            // as LoadCleanup so the in-session and on-load rescues stay in
            // sync.
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this,
                BannerKings.Utils.TickTrace.Wrap("BKShipping.DailyConvoyBeachingCheck", DailyConvoyBeachingCheck));
            // 2026-05-02: no automatic mid-game rescue or flag-correction.
            //
            // Root cause of the AtSea-on-land state: the engine's per-tick
            // transition pipeline (CheckTransitionParallel → Initialize-
            // NavigationTransitionParallel → FinishNavigationTransition-
            // Internal) is designed to handle AI parties. The bug is in
            // cancellation: CancelNavigationTransitionParallel only
            // clears NavigationTransitionStartTime + EndPosition; it
            // does NOT flip IsCurrentlyAtSea back. So if BK changes a
            // party's move target while a transition is in progress, the
            // engine's tick cancels the transition (because embark/
            // disembark data no longer validates against the new target),
            // and AtSea is left in whatever state it had — usually wrong.
            //
            // The fix lives at every BK SetMove call site: gate on
            // !party.IsTransitionInProgress. See RouteCaravanHopByHop,
            // BKLordShippingBehavior, RedirectAIToShippingPort,
            // DisembarkAIOnPortArrival, the hostile-target retarget, and
            // the per-tick lord pre-emptive port redirect.
            //
            // LoadCleanup remains the only on-load corrector; manual
            // bannerkings.rescue_caravans_now cheat for emergencies.
            CampaignEvents.MobilePartyDestroyed.AddNonSerializedListener(this, OnMobilePartyDestroyed);
            CampaignEvents.OnGameLoadFinishedEvent.AddNonSerializedListener(this,
                () =>
                {
                    // Per-save log cleaner. Wipe the BK_*.txt session logs
                    // BEFORE LoadCleanup writes any rescue lines, so each
                    // play session starts with a fresh log slate. Without
                    // this the redirect / rescue / lifecycle logs accumulate
                    // across loads and grep-by-pattern mixes data from
                    // multiple sessions, hiding the current session's
                    // timeline. bk-firstchance.log is preserved so cross-
                    // session exception patterns stay visible.
                    try { BannerKings.BannerKingsCheats.ClearSessionDiagnostics(); } catch { }

                    // Start the always-on freeze watchdog (separate thread,
                    // names whatever handler the campaign thread is stuck in
                    // to BK_freeze.txt). Reset clears any stale marker from a
                    // prior session loaded in the same process.
                    try { BannerKings.Utils.FreezeWatchdog.Reset(); BannerKings.Utils.FreezeWatchdog.EnsureStarted(); } catch { }

                    // Force a graph rebuild on every save-load. The static
                    // ShippingGraph._instance cache otherwise persists across
                    // save reloads in the same Bannerlord session, which
                    // means a save loaded against a freshly-deployed DLL can
                    // still see the old graph topology if the assembly was
                    // loaded earlier. Cheap (~one-shot Build, ~30ms).
                    try { BannerKings.Managers.Shipping.ShippingGraph.Invalidate(); } catch { }

                    // LoadCleanup is the only rescue-style pass that runs
                    // on load. It covers every signature the now-disabled
                    // hourly UnifiedRescueSweep used to handle (AtSea-on-
                    // land, land-mode-on-water for naval-capable, IsActive
                    // orphans, AiDisabled stuck, slave caravan reaper).
                    // The save lands in a vanilla-neutral state — every
                    // weird mid-flight party is in a town with sane flags.
                    LoadCleanup();
                });
            CampaignEvents.OnNewGameCreatedEvent.AddNonSerializedListener(this,
                (CampaignGameStarter starter) =>
                {
                    // Per-save log cleaner — same rationale as on load.
                    try { BannerKings.BannerKingsCheats.ClearSessionDiagnostics(); } catch { }

                    // Start the freeze watchdog for new campaigns too.
                    try { BannerKings.Utils.FreezeWatchdog.Reset(); BannerKings.Utils.FreezeWatchdog.EnsureStarted(); } catch { }

                    // Same rebuild trigger for new-game transitions. Without
                    // this the static graph carries stale Settlement object
                    // references from the previous campaign, and every
                    // RouteCaravanHopByHop call fails the
                    // graph.Adjacency.ContainsKey(target) check ("target not
                    // in graph adjacency" log spam, observed 2026-05-02
                    // after a player started a fresh campaign in the same
                    // Bannerlord session).
                    try { BannerKings.Managers.Shipping.ShippingGraph.Invalidate(); } catch { }
                });

            // BK SetTravel infrastructure — registered only when War Sails
            // is NOT installed. With War Sails the engine provides naval
            // pathfinding + at-sea state and BK's timer-teleport would be
            // redundant; the player uses vanilla NavalDLC's port menu and
            // AI caravans use SetSailAtPosition. Without War Sails neither
            // exists, so BK keeps its self-contained shipping system.
            if (!BannerKings.Utils.ModCompat.WarSails)
            {
                CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, TickSailing);
                CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this,
                    (CampaignGameStarter starter) =>
                    {
                        starter.AddWaitGameMenu("bk_shipping_wait",
                            "{=grOE0m3c}You are now travelling to {DESTINATION} by ship. Estimated arrival is on {ARRIVAL}.{newline}{SIEGE_INFO}",
                            (MenuCallbackArgs args) => UpdateShippingMenu(),
                            (MenuCallbackArgs args) => true,
                            null,
                            (MenuCallbackArgs args, CampaignTime time) =>
                            {
                                if (time.GetHourOfDay % 1f == 0) UpdateShippingMenu();
                            },
                            GameMenu.MenuAndOptionType.WaitMenuHideProgressAndHoursOption,
                            GameMenu.MenuOverlayType.None);
                    });
                CampaignEvents.TickEvent.AddNonSerializedListener(this,
                    (float dt) =>
                    {
                        if (sailing.ContainsKey(MobileParty.MainParty))
                        {
                            MapState mapState = Game.Current.GameStateManager.ActiveState as MapState;
                            if (!PlayerCaptivity.IsCaptive && (dt > 0f || (mapState != null && !mapState.AtMenu)))
                            {
                                if (TaleWorlds.CampaignSystem.Campaign.Current.CurrentMenuContext == null
                                    || TaleWorlds.CampaignSystem.Campaign.Current.CurrentMenuContext.StringId != "bk_shipping_wait")
                                {
                                    GameMenu.ActivateGameMenu("bk_shipping_wait");
                                }
                            }
                        }
                    });
            }
        }

        public override void SyncData(IDataStore dataStore)
        {
            // Persist BK's sailing dict for the !WarSails save format.
            // dataStore tolerates the field on War Sails saves silently
            // (sailing stays empty there because nothing populates it).
            dataStore.SyncData("bannerkings-travels", ref sailing);
            if (sailing == null) sailing = new Dictionary<MobileParty, Travel>(20);
        }

        // "Is this settlement a working ship-boarding location?" Used to gate
        // boarding flows (auto-board, ship menu, etc). v1.6.10 swapped from
        // lane membership to the engine HasPort flag — DefaultShippingLanes
        // no longer drives the routing graph, so a settlement on a curated
        // lane is no longer a stronger signal than a settlement with a real
        // harbour scene. The flag is set by base game + War Sails on every
        // coastal town we care about.
        public bool HasLanes(Settlement settlement) => settlement != null && settlement.HasPort;
        // Can this party initiate a sea voyage from its current settlement?
        // True iff the destination fief is operable (not sieged, village
        // not raided) and the source is a BK shipping port (HasPort with
        // War Sails, canonical-list otherwise). With BK SetTravel as the
        // !WarSails fallback, gold is also gated.
        public bool CanTravel(Settlement settlement, MobileParty party)
        {
            bool fief = settlement.Town != null ? !settlement.IsUnderSiege : settlement.Village.VillageState == Village.VillageStates.Normal;
            bool from = party.CurrentSettlement != null && IsBKShippingPort(party.CurrentSettlement);
            if (BannerKings.Utils.ModCompat.WarSails)
            {
                return fief && from;
            }
            // !WarSails path uses SetTravel which charges gold; double-book guard via sailing dict.
            bool gold = false;
            if (party.CurrentSettlement != null)
            {
                int price = CalculatePrice(settlement, party);
                gold = price <= party.PartyTradeGold || price <= party.LeaderHero?.Gold;
            }
            return fief && from && gold && !sailing.ContainsKey(party);
        }

        // ----------------------------------------------------------------
        // BK SetTravel timer-teleport (legacy, !WarSails path)
        // ----------------------------------------------------------------

        public int CalculatePrice(Settlement settlement, MobileParty party)
        {
            float distance = party.CurrentSettlement.GatePosition.Distance(settlement.GatePosition);
            if (Settings.BannerKingsSettings.Instance.AdaptiveShippingRisk)
            {
                try
                {
                    var graph = ShippingGraph.Instance;
                    var perspective = party.MapFaction;
                    float adaptive = graph.GetAdaptiveDistance(party.CurrentSettlement, settlement, perspective);
                    if (adaptive > 0f) distance = adaptive;
                }
                catch { /* fall through to raw distance */ }
            }
            return MBRandom.RoundRandomized(distance);
        }

        public CampaignTime CalculateArrival(Settlement settlement, MobileParty party)
        {
            float distance = party.CurrentSettlement.GatePosition.Distance(settlement.GatePosition);
            float days = distance / 75f;
            Hero owner = party.LeaderHero != null ? party.LeaderHero : party.Owner;
            if (owner != null)
            {
                Religion religion = BannerKingsConfig.Instance.ReligionsManager.GetHeroReligion(owner);
                if (religion != null && religion.HasDoctrine(DefaultDoctrines.Instance.Astrology))
                {
                    days = distance / 60f;
                }
            }
            return CampaignTime.DaysFromNow(days);
        }

        public void SetTravel(MobileParty party, Settlement destination)
        {
            int price = CalculatePrice(destination, party);
            if (party.LeaderHero?.Gold >= price) party.LeaderHero.ChangeHeroGold(-price);
            else party.PartyTradeGold -= price;

            Settlement current = party.CurrentSettlement;
            CampaignTime arrival = CalculateArrival(destination, party);
            LeaveSettlementAction.ApplyForParty(party);

            party.IsActive = false;
            party.Ai.DisableAi();

            AddParty(party, destination, arrival);
            if (party.MemberRoster.Contains(Hero.MainHero.CharacterObject))
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    new TextObject("{=enrNvyGg}{PARTY} is at {PLACE} and travelling to {FIEF} on water until {DATE}.")
                    .SetTextVariable("PARTY", party.Name)
                    .SetTextVariable("FIEF", destination.Name)
                    .SetTextVariable("PLACE", current.Name)
                    .SetTextVariable("DATE", arrival.ToString())
                    .ToString(),
                    Color.FromUint(Utils.TextHelper.COLOR_LIGHT_BLUE)));
            }

            if (party == MobileParty.MainParty)
            {
                // Hard cap the menu-pop loop. ExitToLast() is supposed to
                // pop the active menu, but if a custom menu (another mod's,
                // or a leaked context) refuses to exit, CurrentMenuContext
                // stays non-null and we'd hang the player on the shipping
                // call. Real menu stacks are at most a handful deep.
                int popIter = 0;
                while (TaleWorlds.CampaignSystem.Campaign.Current.CurrentMenuContext != null && popIter++ < 16)
                    GameMenu.ExitToLast();
                GameMenu.SwitchToMenu("bk_shipping_wait");

                if (MBCommon.IsPaused)
                {
                    GameStateManager.Current.UnregisterActiveStateDisableRequest(this);
                    MBCommon.UnPauseGameEngine();
                }
            }
            party.Party.UpdateVisibilityAndInspected(party.Position);
            party.IsVisible = false;
        }

        private void FinishTravel(Travel travel)
        {
            bool teleportOutside = false;
            if (travel.Destination.Town != null && travel.Destination.IsUnderSiege) teleportOutside = true;
            else if (travel.Destination.IsVillage && travel.Destination.Village.VillageState != Village.VillageStates.Normal) teleportOutside = true;

            MobileParty party = travel.Party;
            if (party == MobileParty.MainParty)
            {
                // Same hang shield as in SetTravel above.
                int popIter = 0;
                while (TaleWorlds.CampaignSystem.Campaign.Current.CurrentMenuContext != null && popIter++ < 16)
                    GameMenu.ExitToLast();
            }

            party.IsActive = true;
            party.Ai.EnableAi();
            party.IsVisible = true;

            if (teleportOutside) travel.Party.Position = travel.Destination.GatePosition;
            else EnterSettlementAction.ApplyForParty(party, travel.Destination);

            party.Party.UpdateVisibilityAndInspected(party.Position);
            RemoveParty(travel.Party);
        }

        private void TickSailing()
        {
            if (sailing.Count == 0) return;
            List<Travel> finished = null;
            foreach (var pair in sailing)
            {
                if (pair.Key == null) continue;
                if (pair.Key == MobileParty.MainParty) continue;
                Travel travel = pair.Value;
                if (travel.Arrival.IsPast || travel.Arrival.IsNow)
                {
                    finished ??= new List<Travel>();
                    finished.Add(travel);
                }
            }
            if (finished == null) return;
            foreach (var travel in finished)
            {
                try { FinishTravel(travel); }
                catch (Exception ex)
                {
                    try
                    {
                        TaleWorlds.Library.Debug.Print(
                            $"[BK] FinishTravel threw on {travel?.Party?.Name}: {ex.GetType().Name}: {ex.Message}",
                            color: TaleWorlds.Library.Debug.DebugColor.Yellow);
                        if (travel?.Party != null)
                        {
                            try { travel.Party.IsActive = true; } catch { }
                            try { travel.Party.Ai?.EnableAi(); } catch { }
                            try { travel.Party.IsVisible = true; } catch { }
                            sailing.Remove(travel.Party);
                        }
                    }
                    catch { }
                }
            }
        }

        private void UpdateShippingMenu()
        {
            if (sailing.ContainsKey(MobileParty.MainParty))
            {
                Travel travel = sailing[MobileParty.MainParty];
                MBTextManager.SetTextVariable("DESTINATION", travel.Destination.Name);
                MBTextManager.SetTextVariable("ARRIVAL", travel.Arrival.ToString());
                if (travel.Destination.IsUnderSiege)
                {
                    MBTextManager.SetTextVariable("SIEGE_INFO",
                        new TextObject("{=ua5R0cSg}Your destination is under siege. The crew will leave you nearby."));
                }
                else
                {
                    MBTextManager.SetTextVariable("SIEGE_INFO",
                        new TextObject("{=tUyv4ppp}Your destination is in not under siege, the crew will leave you inside."));
                }
                if (travel.Arrival.IsPast || travel.Arrival.IsNow) FinishTravel(travel);
            }
        }

        private void OnWeeklyTick()
        {
            // Backstop merchant-notable spawn at every harbour town. Vanilla
            // already populates notables of a settlement's native culture; this
            // weekly pass guarantees at least one merchant-occupation notable
            // of the port's *own* culture exists, in case vanilla turnover
            // leaves a port without one and the trade tooltips read empty.
            //
            // v1.6.10 dropped the cross-cultural injection (Junme lane spawning
            // Nord merchants at Vlandian ports etc) along with the lane
            // system. The graph no longer carries a "this port belongs to
            // this trade culture" mapping; the port's settlement.Culture is
            // the only signal.
            foreach (var port in Settlement.All)
            {
                if (port == null || !port.IsTown) continue;
                bool hp = false; try { hp = port.HasPort; } catch { }
                if (!hp) continue;
                var culture = port.Culture;
                if (culture == null) continue;
                if (port.Notables.Any(x => x.Culture != null && x.Culture.StringId == culture.StringId
                    && x.CharacterObject?.Occupation == Occupation.Merchant)) continue;
                var merchant = culture.NotableTemplates.FirstOrDefault(x => x.Occupation == Occupation.Merchant);
                if (merchant == null) continue;
                EnterSettlementAction.ApplyForCharacterOnly(HeroCreator
                    .CreateSpecialHero(merchant, port, null, null, 30), port);
            }
        }

        private void AfterSettlementEntered(MobileParty party, Settlement settlement, Hero hero)
        {
            if (party == null) return;
            if (party.IsCaravan && settlement != null)
            {
                BannerKings.Utils.Logs.CaravanLifecycle(() =>
                    $"ENTER: {party.Name} → {settlement.Name} (target was {party.TargetSettlement?.Name?.ToString() ?? "(null)"}, AtSea={party.IsCurrentlyAtSea})");
            }

            // First handle disembark — if an AI party arrives at a port while
            // IsCurrentlyAtSea==true, flip it back to land mode before any of
            // the auto-board logic runs (otherwise we'd see them as already
            // at sea and refuse to ship them again the next time they need it).
            DisembarkAIOnPortArrival(party, settlement);

            // Caravan auto-board (existing behaviour, unchanged)
            if (party.IsCaravan)
            {
                AfterSettlementEntered_Caravan(party, settlement);
                return;
            }

            // Lord auto-board now lives in BKLordShippingBehavior (split out
            // 2026-05-02). Caravans are the only kind handled here.
        }

        // Vanilla's player ship-disembark goes through the port menu's
        // "Go to the town center" option; AI parties don't touch menus,
        // so they stay at sea unless we flip the flag. The IsCurrentlyAtSea
        // setter cascades to AttachedParties (armies disembark together) so
        // we only need to touch the leader.
        //
        // Applies to:
        //   * AI lord parties — clear at-sea on arrival.
        //   * Non-player caravans — clear at-sea on arrival, since v1.6.10
        //     RouteCaravanHopByHop now uses vanilla SetSailAtPosition for
        //     sea hops instead of SetTravel's timer-teleport, so caravans
        //     also need explicit disembark.
        // Skips NavalDLC convoys with their own AI active (they manage
        // their own at-sea state) is implicit — those parties wouldn't
        // be entering through this code path at all unless our routing
        // sent them.
        private void DisembarkAIOnPortArrival(MobileParty party, Settlement settlement)
        {
            if (party == null || party == MobileParty.MainParty) return;
            // ROOT-FIX GATE: don't touch parties mid-transition. The engine
            // will finish its disembark on its own; BK touching the flag
            // mid-flight produces the cancellation desync.
            if (party.IsTransitionInProgress) return;
            // Hands-off for siege / map-event / player-in-army parties.
            if (ShouldSkipForArmyOrSiege(party)) return;
            if (settlement == null || !settlement.HasPort) return;
            if (!party.IsCurrentlyAtSea) return;
            bool isAIParty = (party.IsLordParty && party.LeaderHero != null
                              && party.LeaderHero.Clan != Clan.PlayerClan)
                          || party.IsCaravan;
            if (!isAIParty) return;
            if (party.Army != null && party.Army.LeaderParty != party) return;

            // Stay at sea ONLY when the next graph hop is itself a sea edge.
            // Without this gate, a fully-connected unified graph triggers
            // stay-at-sea for any port→port pair, and the engine then
            // pathfinds an overland route while AtSea=true persists — the
            // visible "boat sailing on Mountain" bug. The graph is
            // authoritative on whether a sea hop should be the next move.
            var lordTarget = party.TargetSettlement;
            if (lordTarget != null && lordTarget != settlement && lordTarget.HasPort)
            {
                bool nextHopIsSea = false;
                try
                {
                    var graph = BannerKings.Managers.Shipping.ShippingGraph.Instance;
                    if (graph != null && graph.Adjacency.ContainsKey(settlement) && graph.Adjacency.ContainsKey(lordTarget))
                    {
                        var path = graph.GetShortestPath(settlement, lordTarget);
                        if (path != null && path.Count >= 2 && graph.Adjacency.TryGetValue(path[0], out var edges))
                        {
                            foreach (var e in edges)
                            {
                                if (e.To == path[1])
                                {
                                    nextHopIsSea = e.Kind == BannerKings.Managers.Shipping.ShippingGraph.EdgeKind.Sea;
                                    break;
                                }
                            }
                        }
                    }
                }
                catch { nextHopIsSea = false; }

                if (nextHopIsSea)
                {
                    // Stay-at-sea path. Genuine multi-leg sea voyage. Use
                    // NavigationType.Naval — the party is AtSea=true here
                    // and Naval restricts pathfinding to water-only faces.
                    // isTargetingThePort MUST be true so MoveTargetPoint =
                    // lordTarget.PortPosition (water tile). With false the
                    // target would be GatePosition (land), which Naval-mode
                    // pathfinding cannot reach — the boat would freeze in
                    // place at the current port.
                    try { party.SetMoveGoToSettlement(lordTarget, MobileParty.NavigationType.Naval, true); }
                    catch { /* defensive */ }
                    return;
                }
                // Else: fall through to disembark — next leg is land.
            }

            // Disembark — but only if the party has land capability.
            // Vanilla's EnterSettlementAction.cs:78 sets
            //   IsCurrentlyAtSea = !HasLandNavigationCapability
            // on settlement entry. A naval-only party (no land cap, e.g.
            // a NavalDLC convoy) stays AtSea=true inside the port and
            // sails out the other side — they cannot exist in land mode.
            // Forcing disembark on such a party leaves them visibly on
            // a water tile with no boat sprite the moment they exit
            // (the "Convoy of Gavalon walking the open sea" report).
            //
            // For land-capable parties: clear AtSea AND reposition to
            // GatePosition. The CurrentSettlement setter wrote
            // Position = PortPosition when they entered at-sea; the
            // setter does NOT re-run on exit, so without a Position
            // update the caravan would emerge on the water-side tile
            // (the original "caravan walking water" bug). Vanilla's
            // FinishNavigationTransitionInternal handles this path by
            // writing Position = GatePosition on disembark completion;
            // we mirror that here because we're bypassing the engine's
            // transition machinery.
            try
            {
                bool hasLand = false;
                try { hasLand = party.HasLandNavigationCapability; } catch { }
                if (!hasLand)
                {
                    // Naval-only — keep AtSea=true. The party will sail
                    // out of the port to its next graph-hop target.
                    return;
                }
                party.IsCurrentlyAtSea = false;
                if (party.CurrentSettlement != null)
                {
                    party.Position = party.CurrentSettlement.GatePosition;
                }
            }
            catch
            {
                // Defensive: never crash on disembark.
            }
        }

        private void AfterSettlementEntered_Caravan(MobileParty party, Settlement settlement)
        {
            // ROOT-FIX GATE: skip mid-transition. See TickParty comment.
            if (party != null && party.IsTransitionInProgress) return;

            // BK opt-out: this caravan kept oscillating through the rescue
            // pipeline. Step out and let vanilla CaravanAi tick without BK
            // re-engaging on every port entry.
            if (IsBkOptedOut(party)) return;

            // CRITICAL: skip parties already at sea via NavalDLC's own naval
            // AI. They're vanilla NavalDLC convoys (IsCaravan=true, but
            // IsCurrentlyAtSea=true). Calling SetTravel on them sets
            // IsActive=false and disables AI, leaving them mid-ocean with
            // no way to reach their destination — observed as the
            // "Convoy of X the Y" stuck-caravan pattern with IsActive=False
            // / AiDisabled=True / AtSea=True.
            if (party.IsCurrentlyAtSea) return;

            // Auto-board gate: only act on caravans entering a settlement
            // that's actually a port. v1.6.10 uses HasPort directly instead
            // of lane membership — settlements like Omor / Varcheg / Sibir /
            // Argoron / Sargot aren't on any curated lane but ARE coastal
            // towns with HasPort==true, and were being skipped under the
            // lane gate (the "Varag the Baker walking on the channel"
            // class of bug).
            if (settlement == null || !settlement.HasPort) return;

            Town town = null;
            try
            {
                BKCaravansBehavior behavior = TaleWorlds.CampaignSystem.Campaign.Current.GetCampaignBehavior<BKCaravansBehavior>();
                town = (Town)Caravans_ThinkNextDestination.Invoke(behavior, new object[] { party });
            }
            catch (Exception)
            {
                // Reflection failure — fall back to vanilla AI by leaving early.
            }

            if (town == null) return;
            if (town.Settlement == settlement || party.CurrentSettlement == null) return;

            // Hop-by-hop traversal. The caravan focuses on town.Settlement
            // (the intended destination chosen by ThinkNextDestination).
            // RouteCaravanHopByHop picks the next graph hop and uses vanilla
            // primitives (SetMoveGoToPoint for non-port land hops, SetSailAt-
            // Position + SetMoveGoToSettlement for sea hops, SetMoveGoTo-
            // Settlement for ports) so vanilla pathfinder + naval AI handle
            // the actual movement. Re-runs on every port arrival.
            if (RouteCaravanHopByHop(party, town.Settlement)) return;

            // No graph route from this settlement to the intended destination.
            // Don't fall back to SetMoveGoToSettlement(intendedDest) — if the
            // intended destination is inland and we're in a coastal port,
            // NavalDLC's auto-board hook may pick this caravan up with that
            // inland target, set IsCurrentlyAtSea=true, and the engine's
            // naval pathfinder produces a straight-line "naval" path that
            // visibly clips land (the "boats going over land" report). The
            // safer move is to leave TargetSettlement unchanged and let
            // vanilla CaravanAi pick a fresh, reachable target on its own
            // hourly tick. The progress watchdog + nearest-town rescue
            // catch any caravan that ends up sitting around in the port.
            LogRedirect(party, $"AfterSettlementEntered: no graph route from {settlement.Name} to {town.Settlement.Name}, leaving target unchanged", town.Settlement);
        }

        // Per-day mid-session orphan-rescue. Catches caravans that ended up
        // IsActive=false / Ai disabled but aren't in the sailing dict, which
        // is the "stuck on coast forever" pattern that the load-time rescue
        // can't address while a save is live. Reactivates them so vanilla
        // AI can drive them again.
        //
        // Hands-off rule: never touch parties currently at sea. NavalDLC
        // owns at-sea state for its convoys; reactivating one mid-ocean
        // could interact badly with its naval AI. The BK-shipping-limbo
        // signature we want to fix is *land-side* parties stuck at gates
        // or ports, which is what the load-time rescue's original intent
        // was — just running daily so it works for live sessions too.
        private void OnMobilePartyDestroyed(MobileParty party, PartyBase destroyer)
        {
            if (party == null) return;
            if (sailing.ContainsKey(party)) sailing.Remove(party);
            if (redirectCache.ContainsKey(party)) redirectCache.Remove(party);
            if (progressTracker.ContainsKey(party)) progressTracker.Remove(party);
            if (hopByHopState.ContainsKey(party)) hopByHopState.Remove(party);
            if (lastRescueHour.ContainsKey(party)) lastRescueHour.Remove(party);
            if (lastRescueTown.ContainsKey(party)) lastRescueTown.Remove(party);
            if (rescueHistory.ContainsKey(party)) rescueHistory.Remove(party);
            if (bkOptOutUntilHour.ContainsKey(party)) bkOptOutUntilHour.Remove(party);
            if (lastPortLeft.ContainsKey(party)) lastPortLeft.Remove(party);
            // walkingWaterLastLogHour was added later than this cleanup
            // path; without removal, destroyed MobileParty references
            // accumulate in long-running campaigns (per audit).
            if (walkingWaterLastLogHour.ContainsKey(party)) walkingWaterLastLogHour.Remove(party);
        }

        // Records the most-recent settlement a party left, with the
        // in-game hour. Consumed by both the sea-edge boarding redirect
        // and the land-edge hop-by-hop redirect to suppress backward-bounce
        // (caravan exits settlement S heading for next hop H, BK's tick
        // computes "closest graph node = S" and redirects them back to S,
        // vanilla auto-target re-asserts H, repeat). Tracks all settlement
        // exits — inland fiefs (Omor) bounce the same way port towns
        // (Hargard, Makeb) do, just on land edges instead of sea edges.
        // Non-serialized: rebuilt at runtime; field name retained for
        // history (was originally port-only).
        private void OnSettlementLeft(MobileParty party, Settlement settlement)
        {
            if (party == null || settlement == null) return;
            try
            {
                long hour = (long)CampaignTime.Now.ToHours;
                lastPortLeft[party] = (settlement, hour);
            }
            catch { /* defensive: never throw from a settlement-left hook */ }
        }

        // External entry point so other behaviours (BKCaravansBehavior's
        // ReleaseCaravanFromHold) can invalidate the redirect cache after
        // a target change. Without this, a Hold-released caravan whose
        // pre-Hold target is still cached for up to 6 hours would skip
        // RedirectAIToShippingPort's full re-evaluation and miss the
        // coastal-stuck → boarding-port redirect.
        public void InvalidateRedirectCache(MobileParty party)
        {
            if (party == null) return;
            if (redirectCache.ContainsKey(party)) redirectCache.Remove(party);
            if (progressTracker.ContainsKey(party)) progressTracker.Remove(party);
            if (hopByHopState.ContainsKey(party)) hopByHopState.Remove(party);
        }

        // Single combined rescue pass. Runs on save load and once per day.
        // Walks MobileParty.All exactly once and applies every relevant
        // signature fix to each party.
        //
        // Signatures handled:
        //
        //   A. BK shipping limbo
        //      - IsCaravan && !IsActive && not in sailing dict
        //      - Cause: SetTravel called, party orphaned out of the dict
        //        before FinishTravel.
        //      - Fix: IsActive=true, EnableAi, IsVisible=true.
        //
        //   B. AI-disabled caravan not BK-tracked
        //      - IsCaravan && Ai.IsDisabled && not in sailing dict
        //      - Cause: pre-v1.6.4.0 BK hijacked NavalDLC convoys into its
        //        ship-travel system; v1.6.4.0 stopped doing it but legacy
        //        parties stay AiDisabled because BK no longer touches them.
        //      - Fix: EnableAi (NavalDLC's own AI takes over, or vanilla
        //        caravan AI if on land).
        //
        //   C. Boat on land
        //      - IsCurrentlyAtSea && face terrain is unambiguously inland
        //        (Plain/Forest/Steppe/Desert/Snow/Mountain/Dune/Swamp)
        //        AND Position.IsOnLand. Both signals required: face under
        //        a port's PortPosition is RuralArea/Beach, so face-only
        //        flagged convoys legitimately at-sea at their port.
        //      - Cause: pre-v1.6.4.9 port-redirect set NavigationType.All
        //        on at-sea convoys, which gave the land pathfinder a target.
        //      - Fix: IsCurrentlyAtSea=false.
        //
        //   D. Land mode over open water
        //      - !IsCurrentlyAtSea && naval-capable && over Water/CoastalSea/
        //        OpenSea/Lake terrain && not in BK's sailing dict
        //      - Cause: an earlier version of (C) was too aggressive and
        //        cleared the at-sea flag at coastal ports; those convoys
        //        ended up land-mode but physically over deep water.
        //      - Fix: IsCurrentlyAtSea=true.
        //
        //   E. Legacy slave caravan with no live move target
        //      - PopulationPartyComponent && SlaveCaravan && no move target
        //      - Cause: pre-v1.6.7.0 decision_slaves_export AI-town flow
        //        spawned these; the new raid system doesn't refresh them.
        //      - Fix: DestroyPartyAction.
        //
        // Each fix is per-step try/catch so a single throwing step can't
        // leave a party half-rescued.
        // Public on-demand handle so cheat can fire the sweep manually
        // without waiting for the next daily tick.
        public void RunUnifiedRescueSweepNow() => UnifiedRescueSweep();

        // Hands-off gate. Three cases where BK shipping must NOT touch a
        // party's movement state:
        //
        //   1. The party is actively in a siege (SiegeEvent != null). It's
        //      not traveling — it's at the gate, in the camp, or sallying.
        //      Issuing SetMoveGoToSettlement / SetSailAtPosition here breaks
        //      the siege state machine and reproduces the v1.8.10.0
        //      Hakarshus oscillation.
        //   2. The party is in a map event (battle, raid in progress). Same
        //      reason — vanilla owns the encounter resolution.
        //   3. MainParty is in the same army as this party. The player
        //      drives intent through UI move orders; BK has no business
        //      pre-empting those, and the player-included army case is the
        //      one that regressed in v1.8.10.0 even with the v1.8.9.9
        //      behavior gate (the gate covered AI-only sieges, not the
        //      shared-army case).
        //
        // Applied at the top of every redirect / auto-board / disembark
        // entry point in BKShippingBehavior and BKLordShippingBehavior.
        public static bool ShouldSkipForArmyOrSiege(MobileParty p)
        {
            if (p == null) return true;
            try
            {
                if (p.SiegeEvent != null) return true;
                if (p.MapEvent != null) return true;
                if (p.Army != null && p.Army.Parties != null)
                {
                    var main = MobileParty.MainParty;
                    if (main != null && p.Army.Parties.Contains(main)) return true;
                }
            }
            catch { }
            return false;
        }

        // Save-load cleanup pass. Runs once on OnGameLoadFinished, AFTER
        // the graph is rebuilt and BEFORE the first hourly tick. Goal: put
        // every party that's in a BK-shaped weird state into a vanilla-
        // neutral situation (inside a real settlement, with valid AI flags)
        // so the next vanilla AI tick can pick them up cleanly. We do this
        // by ENTERING a settlement — vanilla's settlement-entry pipeline
        // resets state machines (AtSea transition, IsActive, Ai flags) to
        // their canonical post-entry values. The party's exit on the next
        // tick lands them at a guaranteed on-mesh GatePosition.
        //
        // Conditions we treat as "weird" and reset by entering a settlement:
        //   * IsCurrentlyAtSea=true on a non-water terrain face (carries
        //     persisted desync from old BK builds).
        //   * IsCurrentlyAtSea=false on an open-water face with a naval-
        //     capable party (the engine should have transitioned them but
        //     didn't, in a save taken mid-transit).
        //   * IsActive=false on a caravan/lord party that has a live
        //     LeaderHero (legacy SetTravel orphan).
        //   * Ai.IsDisabled with no time bound and a live target (legacy
        //     BK builds suspended AI permanently for "holder" components
        //     that we now bound to 72h).
        //
        // Anything not matching these patterns is left alone — we don't
        // want to interfere with parties whose state vanilla would
        // happily continue ticking.
        private void LoadCleanup()
        {
            // MCM gate. When off, BK never teleports parties as a rescue —
            // vanilla AI is left to handle stuck/stranded states. Default
            // off because the rescue paths repeatedly produced enemy-fief
            // teleports and siege oscillation while the underlying
            // detection logic is being audited.
            if (!BannerKings.Settings.BannerKingsSettings.Instance.EnableShippingRescues) return;

            try
            {
                var wrapper = TaleWorlds.CampaignSystem.Campaign.Current?.MapSceneWrapper;
                int reset = 0;
                int destroyed = 0;
                System.Collections.Generic.List<MobileParty> toDestroy = null;
                foreach (var party in MobileParty.All)
                {
                    if (party == null || party == MobileParty.MainParty) continue;

                    // Legacy slave caravan with stale or missing target. Same
                    // condition the previous hourly E-rescue used. Cleanup
                    // happens on load only now; mid-game an orphan slave
                    // caravan is left visible so the spawn / target-update
                    // logic bug surfaces.
                    // Phase 5: gate the slave-caravan rescue on the Kind
                    // discriminator AS WELL AS the legacy bool. Food /
                    // Raw / Finished caravans share the same component
                    // class; without the Kind check we'd silently destroy
                    // them on load if their TargetSettlement got fiddled
                    // by an unrelated mod. Belt and suspenders — both
                    // checks must agree before we destroy.
                    if (party.PartyComponent is BannerKings.Components.PopulationPartyComponent ppc
                        && ppc.EffectiveKind == BannerKings.CampaignContent.Economy.Layered.CargoKind.Slaves
                        && (party.TargetSettlement == null || party.TargetSettlement != ppc.TargetSettlement))
                    {
                        toDestroy ??= new System.Collections.Generic.List<MobileParty>();
                        toDestroy.Add(party);
                        continue;
                    }

                    if (!(party.IsCaravan || party.IsLordParty)) continue;
                    if (party.CurrentSettlement != null) continue;     // already inside a settlement — nothing to clean up

                    bool needReset = false;
                    string reason = "";

                    // Mismatch C: AtSea-flag on a non-water terrain face AND
                    // a land position. Both signals are required because the
                    // face under a port's PortPosition is typically
                    // RuralArea/Beach (water-adjacent land face) even though
                    // the party is rendering on water — face-terrain alone
                    // false-positive-rescues naval-only NavalDLC convoys
                    // legitimately at sea at their port. Mirrors F2 below:
                    // a party is "on water" if EITHER the face says water
                    // OR Position.IsOnLand says water; only a non-water
                    // face combined with a land position is a true desync.
                    if (wrapper != null && party.IsCurrentlyAtSea)
                    {
                        try
                        {
                            var t = wrapper.GetFaceTerrainType(party.CurrentNavigationFace);
                            bool onWaterByTerrain = IsOpenWater(t);
                            bool onWaterByPosition = false;
                            try { onWaterByPosition = !party.Position.IsOnLand; } catch { }
                            if (!onWaterByTerrain && !onWaterByPosition)
                            {
                                needReset = true; reason = $"AtSea over {t}";
                            }
                        }
                        catch { }
                    }

                    // Mismatch D/F: land-mode on water for a naval-capable party.
                    // Two sub-cases:
                    //
                    //   F1 (naval-ONLY): NavalDLC convoy / pirate / fishing
                    //     party with HasLandNavigationCapability=false (set
                    //     by CaravanPartyComponent at spawn for templates
                    //     with ShipHulls > 0, by PiratesCampaignBehavior,
                    //     etc). These parties cannot survive in land mode;
                    //     vanilla EnterSettlementAction explicitly keeps
                    //     them AtSea=true even inside ports (line 78). If
                    //     a save persisted them with AtSea=false (because
                    //     a previous BK build cleared the flag during
                    //     disembark), the fix is just to restore AtSea=true
                    //     in place. They're already on water — no haven
                    //     entry needed, that's vanilla state.
                    //
                    //   F2 (naval+land): regular caravan with both flags.
                    //     Vanilla pathfinder routed them onto water and
                    //     the boarding transition didn't fire. Drop them
                    //     into the nearest haven so they re-enter the
                    //     normal trade pipeline.
                    if (!needReset && wrapper != null && !party.IsCurrentlyAtSea)
                    {
                        try
                        {
                            var t = wrapper.GetFaceTerrainType(party.CurrentNavigationFace);
                            bool navalCap = false;
                            try { navalCap = party.HasNavalNavigationCapability; } catch { }
                            bool hasLand = false;
                            try { hasLand = party.HasLandNavigationCapability; } catch { }

                            // Two ways to detect "party is on water":
                            //   (a) face terrain is OpenSea/CoastalSea/Lake/Water (IsOpenWater)
                            //   (b) Position.IsOnLand == false
                            // (a) misses parties at PortPosition because the
                            // navmesh face under PortPosition is typically
                            // RuralArea/Beach/Bridge (water-adjacent land
                            // face), even though the party is rendering on
                            // water. (b) is set when the position was last
                            // computed via the engine's nav helpers and is
                            // authoritative regardless of face classification.
                            // Combine: either signal counts.
                            bool onWaterByTerrain = IsOpenWater(t);
                            bool onWaterByPosition = false;
                            try { onWaterByPosition = !party.Position.IsOnLand; } catch { }
                            bool onWater = onWaterByTerrain || onWaterByPosition;

                            if (onWater && navalCap)
                            {
                                if (!hasLand)
                                {
                                    // F1: just restore AtSea=true. No haven.
                                    try { party.IsCurrentlyAtSea = true; } catch { }
                                    // Was: `continue` here, which skipped the
                                    // IsActive=true / Ai.EnableAi resets below
                                    // for legacy-AI-disabled F1 parties — the
                                    // rescue restored AtSea but left the
                                    // convoy un-tickable, so it stayed sitting
                                    // at the port forever despite the rescue.
                                    // Fall through so AI/IsActive get fixed
                                    // too; needReset stays false so we don't
                                    // also enter a settlement.
                                    reset++;
                                    string sig = onWaterByTerrain ? t.ToString() : "PortPosition/water-tile";
                                    LogRescue(party, $"load-cleanup: restored AtSea=true (naval-only on {sig}, no haven needed)");
                                }
                                else
                                {
                                    // F2: fall through to haven rescue below.
                                    string sig2 = onWaterByTerrain ? t.ToString() : "PortPosition/water-tile";
                                    needReset = true; reason = $"land-mode on {sig2} (naval+land)";
                                }
                            }
                        }
                        catch { }
                    }

                    // Legacy IsActive=false orphan (BK SetTravel timer-teleport).
                    if (!needReset && !party.IsActive)
                    {
                        try { party.IsActive = true; } catch { }
                        try { party.IsVisible = true; } catch { }
                    }

                    // Legacy DisableAi() with no time bound.
                    if (party.Ai != null && party.Ai.IsDisabled)
                    {
                        try { party.Ai.EnableAi(); } catch { }
                    }

                    // v1.6.10.0 — additional rescue signature for naval
                    // caravans (Convoys) that the terrain check above misses.
                    // Mismatch C only fires when face-terrain says non-water
                    // AND Position.IsOnLand says land. Some convoys end up
                    // with AtSea=true on a face the engine still classifies
                    // as water-adjacent (Beach, Bridge, RuralArea) even though
                    // the X,Y is plainly inland — a stuck-at-coastal-ish-tile
                    // state. The clearer signal for these is "naval party with
                    // a non-port TargetSettlement": Convoys can only legitimately
                    // target port towns, so a non-port target is by itself
                    // evidence of a bad state. Caravan dump showed 8 such
                    // convoys piled at ~(510,510), 6 at ~(510,600), all heading
                    // to inland Diathma / Vostrum / Corenia Castle / etc.
                    if (!needReset && party.IsCaravan)
                    {
                        try
                        {
                            bool nav = party.HasNavalNavigationCapability;
                            var tgt = party.TargetSettlement;
                            // A valid naval target is exactly: a TOWN with a PORT.
                            // Castles, villages, hideouts, and inland towns all fail.
                            // Earlier gate only checked `tgt.IsTown && !tgt.HasPort`,
                            // which let CASTLE targets slip past (IsTown=false → gate
                            // skipped; castle had no port either way). Observed
                            // "Convoy of Encurion → Corenia Castle" persisting after
                            // v1.6.9.33's first rescue pass. Now: anything that isn't
                            // a port-town triggers rescue.
                            if (nav && tgt != null && !(tgt.IsTown && tgt.HasPort))
                            {
                                needReset = true;
                                reason = $"naval caravan targeting non-port {tgt.StringId}";
                            }
                        }
                        catch { }
                    }

                    if (!needReset) continue;

                    // Drop into the nearest non-hostile town and explicitly
                    // reset the at-sea flag. EnterSettlementAction puts the
                    // party inside (exit places them at on-mesh GatePosition);
                    // vanilla does NOT auto-clear IsCurrentlyAtSea on entry,
                    // so we do that explicitly. Result: party in town, land
                    // mode, AI enabled — vanilla-neutral.
                    //
                    // For naval-capable parties, route to nearest PORT instead
                    // of nearest town: an inland haven park-spot strands them
                    // where they can never re-enter the sea graph. Falls back
                    // to FindNearestRescueTown if no port is reachable.
                    bool navalCapable = false;
                    try { navalCapable = party.HasNavalNavigationCapability; } catch { }
                    Settlement haven = navalCapable
                        ? FindNearestPort(party, party.GetPosition2D)
                        : FindNearestRescueTown(party, party.GetPosition2D);
                    if (haven == null) continue;
                    try
                    {
                        // Match vanilla EnterSettlementAction.cs:78:
                        //   IsCurrentlyAtSea = !HasLandNavigationCapability
                        // For land-capable parties: flip AtSea=false BEFORE
                        // EnterSettlement so the CurrentSettlement setter
                        // writes Position = GatePosition (land-side).
                        // For naval-only parties: keep AtSea=true; they
                        // can't exist in land mode and need to be at the
                        // port's water-side. Skipping the explicit flip
                        // lets EnterSettlementAction's own logic handle it.
                        bool hasLand = false;
                        try { hasLand = party.HasLandNavigationCapability; } catch { }
                        if (hasLand)
                        {
                            try { party.IsCurrentlyAtSea = false; } catch { }
                        }
                        TaleWorlds.CampaignSystem.Actions.EnterSettlementAction.ApplyForParty(party, haven);
                        // Force a fresh destination pick on next hourly tick.
                        // Without this, the rescued convoy exits the port
                        // still aimed at its original (bad) TargetSettlement
                        // because Path-A (at-settlement) hourly tick is
                        // gated on a 33% RNG re-think probability.
                        try
                        {
                            if (party.Ai != null) party.Ai.RethinkAtNextHourlyTick = true;
                        }
                        catch { }
                        reset++;
                        LogRescue(party, $"load-cleanup: ENTER {haven.Name} ({reason})");
                    }
                    catch { /* skip parties the engine refuses */ }
                }

                if (toDestroy != null)
                {
                    foreach (var p in toDestroy)
                    {
                        try { TaleWorlds.CampaignSystem.Actions.DestroyPartyAction.Apply(null, p); destroyed++; }
                        catch { }
                    }
                }

                if (reset > 0 || destroyed > 0)
                {
                    try
                    {
                        TaleWorlds.Library.Debug.Print(
                            $"[BK] Load cleanup: {reset} parties reset into nearest town, {destroyed} legacy slave caravans destroyed.",
                            color: TaleWorlds.Library.Debug.DebugColor.Green);
                    }
                    catch { }
                }
            }
            catch
            {
                // Defensive: never crash on load.
            }
        }

        private void UnifiedRescueSweep()
        {
            // MCM gate (see LoadCleanup for rationale).
            if (!BannerKings.Settings.BannerKingsSettings.Instance.EnableShippingRescues) return;

            try
            {
                var wrapper = TaleWorlds.CampaignSystem.Campaign.Current?.MapSceneWrapper;
                List<MobileParty> staleSlaveCaravans = null;

                // Build a port-position snapshot ONCE per sweep. The previous
                // code looped Settlement.All inside the per-party loop to test
                // "am I at any port's PortPosition?" — with ~5000 parties × 150
                // settlements that's 750k Vec2.Distance calls per sweep on the
                // campaign thread. Snapshot once, walk a small list per party.
                List<Vec2> portPositions = null;
                try
                {
                    portPositions = new List<Vec2>(64);
                    foreach (var s in Settlement.All)
                    {
                        if (s == null) continue;
                        bool hp = false; try { hp = s.HasPort; } catch { }
                        if (!hp) continue;
                        portPositions.Add(s.PortPosition.ToVec2());
                    }
                }
                catch { portPositions = null; }

                foreach (var party in MobileParty.All)
                {
                    if (party == null) continue;
                    if (party == MobileParty.MainParty) continue;

                    // Caravan-only signatures (A & B). v1.6.10 dropped the
                    // BK sailing dict; sig A (!IsActive) catches caravans
                    // suspended by the legacy SetTravel timer-teleport on
                    // older saves and reactivates them. Once they're live,
                    // vanilla CaravanAi + RouteCaravanHopByHop take over.
                    if (party.IsCaravan)
                    {
                        bool fixedA = !party.IsActive;
                        bool fixedB = party.Ai != null && party.Ai.IsDisabled;
                        if (fixedA || fixedB)
                        {
                            LogRescue(party, $"reactivating (IsActive={party.IsActive}, AiDisabled={party.Ai?.IsDisabled}, AtSea={party.IsCurrentlyAtSea})");
                            try { party.IsActive = true; } catch { }
                            try { party.Ai?.EnableAi(); } catch { }
                            try { party.IsVisible = true; } catch { }
                            try { party.Party.UpdateVisibilityAndInspected(party.Position); } catch { }
                            // Stale TargetSettlement — the original target
                            // may have been conquered/destroyed while AI
                            // was off. EnableAi alone doesn't refresh it
                            // (vanilla CaravansCampaignBehavior would,
                            // but BK removes that). Ask BK's hourly tick
                            // to re-pick on the next pass.
                            try
                            {
                                var t = party.TargetSettlement;
                                bool targetInvalid = t == null
                                    || (t.IsTown == false && t.IsVillage == false)
                                    || t.IsUnderSiege
                                    || (t.MapFaction != null && t.MapFaction.IsAtWarWith(party.MapFaction));
                                if (targetInvalid && party.Ai != null)
                                {
                                    party.Ai.RethinkAtNextHourlyTick = true;
                                }
                            }
                            catch { }
                        }
                    }

                    // Terrain-based signatures (C, D, F). Skip if at a
                    // settlement — the brief boarding / port-arrival
                    // window legitimately straddles modes.
                    //
                    // Pre-filters in priority order to avoid expensive
                    // checks on the 5000+ irrelevant parties (bandits,
                    // peasants, looters, militia):
                    //   1. Skip if party type can't ever match (bandit
                    //      components etc. aren't naval candidates).
                    //   2. For C, IsCurrentlyAtSea is the only gate.
                    //   3. For D (lord) and F (caravan), the terrain
                    //      probe is the gate — water + not at sea.
                    bool isShippableType = party.IsCaravan || party.IsLordParty;
                    if (wrapper != null
                        && party.CurrentSettlement == null
                        && isShippableType)
                    {
                        TerrainType terrain;
                        bool gotTerrain;
                        try
                        {
                            terrain = wrapper.GetFaceTerrainType(party.CurrentNavigationFace);
                            gotTerrain = true;
                        }
                        catch { terrain = default; gotTerrain = false; }

                        if (gotTerrain)
                        {
                            bool overWater = IsOpenWater(terrain);

                            // C — at-sea but not over water: clear flag.
                            // Inverted from the old "clearly land" allow-
                            // list (which missed coastal/transitional
                            // terrain like Beach, RuralArea, Bridge,
                            // Fording — exactly where the pre-v1.6.4.9
                            // hijack stranded NavalDLC convoys).
                            //
                            // Skip the clear when the party is at a port's
                            // PortPosition — SetSailAtPosition just placed
                            // them there for naval transit, and the
                            // terrain face under PortPosition is often
                            // RuralArea/Beach/Bridge (water-adjacent land
                            // face), not OpenSea. Clearing the flag here
                            // produces the "Convoy of Urkhun AtSea=true at
                            // RuralArea" loop: rescue clears the flag, the
                            // caravan can't pathfind from this water-edge
                            // tile in land mode, STUCK rescue fires, the
                            // next AfterSettlementEntered_Caravan re-runs
                            // SetSailAtPosition, repeat.
                            bool atPortPos = false;
                            if (party.IsCurrentlyAtSea && !overWater && portPositions != null)
                            {
                                try
                                {
                                    var partyPos = party.GetPosition2D;
                                    for (int i = 0; i < portPositions.Count; i++)
                                    {
                                        if (partyPos.Distance(portPositions[i]) <= 5f)
                                        {
                                            atPortPos = true;
                                            break;
                                        }
                                    }
                                }
                                catch { atPortPos = false; }
                            }
                            if (party.IsCurrentlyAtSea && !overWater && !atPortPos)
                            {
                                // C-rescue: AtSea-flag set on non-water terrain.
                                // Honor the per-party rescue cooldown and use
                                // ChooseRescueTown (advances past last rescue
                                // town on repeat) so we don't rescue the same
                                // party into the same town every tick. The
                                // unbounded-loop pattern we saw was: ENTER
                                // Jaculan → exit at GatePosition → engine's
                                // NavalDLC NavigationTransition re-flags
                                // AtSea=true on the port-exit boundary tile
                                // → C-rescue fires next tick → loop.
                                long nowHourC = (long)CampaignTime.Now.ToHours;
                                bool inCooldown = lastRescueHour.TryGetValue(party, out long prevHourC)
                                    && nowHourC - prevHourC < RescueCooldownHours;
                                if (!inCooldown)
                                {
                                    Settlement haven = ChooseRescueTown(party, party.GetPosition2D, null);
                                    LogRescue(party, $"at-sea over non-water {terrain} — ENTER {haven?.Name?.ToString() ?? "(none)"}");
                                    if (haven != null)
                                    {
                                        // Flip AtSea BEFORE EnterSettlement
                                        // (see disembark-on-port comment in
                                        // DisembarkAIOnPortArrival): otherwise
                                        // Position lands at PortPosition and
                                        // the party walks water on next exit.
                                        try { party.IsCurrentlyAtSea = false; } catch { }
                                        try { TaleWorlds.CampaignSystem.Actions.EnterSettlementAction.ApplyForParty(party, haven); } catch { }
                                        lastRescueHour[party] = nowHourC;
                                        lastRescueTown[party] = haven;
                                    }
                                    else
                                    {
                                        // No haven — clear flag in place. Less
                                        // ideal but unblocks the per-tick
                                        // logic; the next sweep tries again.
                                        try { party.IsCurrentlyAtSea = false; } catch { }
                                    }
                                    try { redirectCache.Remove(party); } catch { }
                                    try { hopByHopState.Remove(party); } catch { }
                                }
                                else
                                {
                                    // In cooldown — just clear the flag without
                                    // teleporting. Engine has time to resolve
                                    // the state in between rescues. If party
                                    // is still in this state when cooldown
                                    // expires, rescue advances to a different
                                    // town via ChooseRescueTown's repeat-detect.
                                    try { party.IsCurrentlyAtSea = false; } catch { }
                                }
                            }
                            else if (overWater && !party.IsCurrentlyAtSea)
                            {
                                if (party.IsCaravan)
                                {
                                    // F — Caravan walking on water in land mode.
                                    // NavigationType.Default permits water faces
                                    // for naval-capable caravans (boat troop in
                                    // roster from spawning at a port town), so
                                    // vanilla CaravanAi happily picks a water-
                                    // cutting shortcut and the caravan walks
                                    // visibly across open sea with IsCurrentlyAtSea
                                    // still false (the "Varag the Baker walking
                                    // across the channel" report). The enum
                                    // has no "Land" value to forbid water in
                                    // SetMove calls, so the fix is reactive:
                                    // when caught on water, redirect to the
                                    // nearest sea-reachable port. On port
                                    // arrival, AfterSettlementEntered_Caravan
                                    // boards via SetTravel and BK shipping
                                    // takes over the sea hop properly.
                                    //
                                    // Do NOT flip IsCurrentlyAtSea on a caravan
                                    // here — that would let NavalDLC's naval AI
                                    // take over and direct-pathfind to the
                                    // original target, bypassing the shipping
                                    // graph (same bug, different render). The
                                    // caravan needs to enter a port settlement
                                    // for SetTravel to fire.
                                    // Prefer the caravan's actual target if it's
                                    // a usable rescue destination. Same logic
                                    // as the unstuck rescue: dropping into the
                                    // target sidesteps the "walk to a port,
                                    // can't pathfind from this water tile,
                                    // re-trigger every tick" loop.
                                    Settlement rescueDest = null;
                                    var t = party.TargetSettlement;
                                    if (t != null)
                                    {
                                        var cand = (t.IsVillage && t.Village?.Bound != null) ? t.Village.Bound : t;
                                        // Was: (cand.IsTown || cand.IsCastle).
                                        // EnterSettlementAction for caravans
                                        // into castles is not vanilla-supported
                                        // and produces "caravan inside castle"
                                        // desync — skip castle candidates and
                                        // let FindNearestSeaReachablePort take
                                        // over.
                                        bool usable = cand.IsTown
                                            && !cand.IsUnderSiege
                                            && (cand.MapFaction == null
                                                || !cand.MapFaction.IsAtWarWith(party.MapFaction));
                                        if (usable) rescueDest = cand;
                                    }
                                    if (rescueDest == null)
                                    {
                                        rescueDest = FindNearestSeaReachablePort(party, null);
                                    }
                                    if (rescueDest != null)
                                    {
                                        LogRescue(party, $"caravan walking water — ENTER {rescueDest.Name} (terrain={terrain})");
                                        // SetMoveGoToSettlement is a no-op when the
                                        // party is on a water tile vanilla
                                        // pathfind can't escape from. EnterSettlement-
                                        // Action sidesteps pathfind entirely and
                                        // vanilla's exit places the party on a
                                        // walkable mesh tile.
                                        try { TaleWorlds.CampaignSystem.Actions.EnterSettlementAction.ApplyForParty(party, rescueDest); } catch { }
                                        // Clear caches so the next hourly tick
                                        // can re-evaluate from scratch instead
                                        // of skipping under stale entries from
                                        // the original target.
                                        try { redirectCache.Remove(party); } catch { }
                                        try { hopByHopState.Remove(party); } catch { }
                                    }
                                }
                                else
                                {
                                    // D — Lord party land-mode over open water.
                                    // Teleport into the nearest port and clear
                                    // any stale AtSea state. BKLordShipping-
                                    // Behavior's per-tick logic handles re-
                                    // boarding via vanilla SetSailAtPosition on
                                    // the next port-entry pass — that's the
                                    // supported path for AI lord/army naval
                                    // transit. Direct flag mutation is avoided
                                    // because the engine's NavigationTransition
                                    // owns the visual/AtSea state machine when
                                    // boarding properly.
                                    Settlement port = FindNearestRescueTown(party, party.GetPosition2D);
                                    LogRescue(party, $"lord land-mode over water {terrain} — ENTER {port?.Name?.ToString() ?? "(none)"}");
                                    if (port != null)
                                    {
                                        try { TaleWorlds.CampaignSystem.Actions.EnterSettlementAction.ApplyForParty(party, port); } catch { }
                                    }
                                    // Belt-and-braces: ensure flag is sane
                                    // post-teleport. Lord enters port in land
                                    // mode; auto-board (if needed) re-fires
                                    // via BKLordShippingBehavior on next tick.
                                    try { party.IsCurrentlyAtSea = false; } catch { }
                                }
                            }
                        }
                    }

                    // E — legacy slave caravan with no live move target.
                    // Phase 5: Kind discriminator gates the cleanup so
                    // raw-goods caravans (Food/Raw/Finished) don't get
                    // swept up by the same path. EffectiveKind handles
                    // pre-Phase-5 saves where Kind defaults to Unset.
                    if (party.PartyComponent is BannerKings.Components.PopulationPartyComponent ppc
                        && ppc.EffectiveKind == BannerKings.CampaignContent.Economy.Layered.CargoKind.Slaves
                        && (party.TargetSettlement == null || party.TargetSettlement != ppc.TargetSettlement))
                    {
                        staleSlaveCaravans ??= new List<MobileParty>();
                        staleSlaveCaravans.Add(party);
                    }
                }

                if (staleSlaveCaravans != null)
                {
                    foreach (var party in staleSlaveCaravans)
                    {
                        LogRescue(party, "destroying legacy slave caravan (no live move target)");
                        try { DestroyPartyAction.Apply(null, party); } catch { }
                    }
                }
            }
            catch { /* never throw out of a daily tick or load handler */ }
        }

        private static void LogRescue(MobileParty party, string message)
        {
            string name = "?";
            // BanditPartyComponent.get_Name throws NRE in 1.3.x for some
            // bandit states (observed in bk-firstchance.log). Null-
            // conditional doesn't catch property-getter throws — needs
            // try/catch. Without this each rescue iteration that touches
            // a bandit party generates a managed exception, which is
            // expensive in a tight loop.
            if (party != null)
            {
                try { name = party.Name?.ToString() ?? "?"; }
                catch { name = "?"; }
            }
            try
            {
                TaleWorlds.Library.Debug.Print(
                    $"[BK] Rescue sweep: {name} — {message}",
                    color: TaleWorlds.Library.Debug.DebugColor.Yellow);
            }
            catch { /* defensive */ }
            // Persist to BK_rescue.txt via the central per-category log
            // helper. Gate is now Diagnostics → LogRescueSweep (split from
            // the legacy LogShippingRedirect catch-all so each surface
            // can be toggled independently).
            BannerKings.Utils.Logs.Rescue(() =>
            {
                string pos = "?";
                try
                {
                    if (party != null && party.CurrentSettlement != null) pos = party.CurrentSettlement.Name?.ToString() ?? "?";
                    else if (party != null) { var p = party.GetPosition2D; pos = $"({p.X:0.0},{p.Y:0.0})"; }
                }
                catch { pos = "?"; }
                string atSea = "?";
                try { atSea = party?.IsCurrentlyAtSea.ToString() ?? "?"; } catch { }
                return $"{name} @ {pos} (AtSea={atSea}): {message}";
            });
        }

        // Route a caravan toward an intended final destination using hop-by-hop
        // graph traversal. Architecture:
        //   * BK picks which graph nodes to traverse and in what order
        //     (Dijkstra over ShippingGraph; re-decided at every port visit
        //     so vanilla CaravanAi's trade decisions can override the path
        //     mid-route).
        //   * Vanilla handles the actual movement between consecutive nodes:
        //       - Land hop: SetMoveGoToPoint or SetMoveGoToSettlement,
        //         vanilla road pathfinder.
        //       - Sea hop: SetSailAtPosition + SetMoveGoToSettlement, vanilla
        //         naval pathfinder picks the route around peninsulas etc.
        //   * Ports are ALWAYS entered (intermediate or final) via auto-
        //     enter. Each port visit fires AfterSettlementEntered_Caravan
        //     which re-runs ThinkNextDestination and the routing helper —
        //     this is the integration point with vanilla trade.
        //   * Non-port intermediate fiefs are passed through at the gate
        //     via SetMoveGoToPoint (no entry, no trade fees, no settlement-
        //     entered event). AdvanceHopByHopWaypoints picks up arrival
        //     within HopArrivalDistance and re-runs us for the next hop.
        //   * The final intendedDest is always entered (SetMoveGoToSettlement
        //     in the final-approach branch or the single-hop branch).
        //
        // Sea ↔ land transitions on the hop boundary are handled by toggling
        // IsCurrentlyAtSea at port gates: the gate position sits on land
        // terrain, so clearing the flag in place lets vanilla pathfind treat
        // the caravan as walking for the next land hop. Boarding for a sea
        // hop requires being inside the start port (SetSailAtPosition needs
        // CurrentSettlement set), so a walk into the start port precedes
        // any new sea segment.
        //
        // Returns true if a routing decision was issued, false if the helper
        // could not handle the case (caller should fall back to direct
        // SetMoveGoToSettlement).
        public bool RouteCaravanHopByHop(MobileParty caravan, Settlement intendedDest)
        {
            if (caravan == null || intendedDest == null) return false;
            // BK opt-out: this caravan was rescue-oscillating; let vanilla
            // CaravanAi handle them for the cooldown.
            if (IsBkOptedOut(caravan)) return false;
            try
            {
                var pos = caravan.GetPosition2D;

                // Final approach: caravan is within auto-enter proximity of the
                // intended destination. Hand off to SetMoveGoToSettlement so
                // vanilla auto-enter fires and OnSettlementEntered triggers
                // the trade pipeline.
                if (pos.Distance(intendedDest.GatePosition.ToVec2()) <= HopFinalApproachDistance)
                {
                    caravan.SetMoveGoToSettlement(intendedDest, MobileParty.NavigationType.Default, false);
                    hopByHopState.Remove(caravan);
                    return true;
                }

                var graph = ShippingGraph.Instance;

                // Determine the start node for the graph path. If the caravan
                // is currently in a settlement that's a graph node, use it.
                // Otherwise, use the closest graph node to the caravan's
                // current position.
                Settlement startNode = caravan.CurrentSettlement;
                if (startNode == null || !graph.Adjacency.ContainsKey(startNode))
                {
                    Settlement closest = null;
                    float bestD = float.MaxValue;
                    foreach (var node in graph.Adjacency.Keys)
                    {
                        if (node == null || node == intendedDest) continue;
                        float d = pos.Distance(node.GatePosition.ToVec2());
                        if (d < bestD) { bestD = d; closest = node; }
                    }
                    startNode = closest;
                }

                if (startNode == null
                    || !graph.Adjacency.ContainsKey(intendedDest)
                    || !graph.AreConnected(startNode, intendedDest))
                {
                    // Target not on the graph or not connected. Fall back to
                    // a direct settlement target — caller's responsibility
                    // to handle vanilla pathfind unreliability via the
                    // existing redirect / progress watchdog.
                    return false;
                }

                List<Settlement> path = Settings.BannerKingsSettings.Instance.AdaptiveShippingRisk
                    ? (graph.GetAdaptivePath(startNode, intendedDest, caravan.MapFaction)
                       ?? graph.GetShortestPath(startNode, intendedDest))
                    : graph.GetShortestPath(startNode, intendedDest);
                if (path == null || path.Count < 2) return false;

                Settlement nextHop = path[1];

                // Single hop remaining (path[1] == intendedDest): caravan is
                // adjacent to the destination in graph terms. Route directly.
                if (nextHop == intendedDest)
                {
                    caravan.SetMoveGoToSettlement(intendedDest, MobileParty.NavigationType.Default, false);
                    hopByHopState.Remove(caravan);
                    return true;
                }

                // Hop-by-hop traversal model.
                //
                // Reflection of vanilla CaravansCampaignBehavior + NavalDLC
                // shows that NO vanilla caravan code calls SetSailAtPosition;
                // SetSailAtPosition is reserved for the player's port menu
                // and AnchorPoint interactions. Vanilla AI caravans just
                // call SetMoveGoToSettlement(target, NavigationType) and let
                // the engine handle naval transitions automatically via
                // _desiredAiNavigationType + the EndPositionForNavigation-
                // Transition / NavigationTransitionStartTime / NavigationTran-
                // sitionDuration triple. The engine's pathfinder respects
                // NavalPartyNavigationModel.IsTerrainTypeValidForNavigationType
                // (which marks Mountain / Plain / RuralArea / etc. as INVALID
                // for Naval), so a properly-typed move call routes only over
                // water faces and the visual transitions match the terrain.
                //
                // BK previously called SetSailAtPosition manually, which
                // forced AtSea=true at PortPosition (a face the engine
                // classifies as water-adjacent RuralArea, not OpenSea).
                // Because the engine's auto-transit pipeline didn't put
                // the caravan there, it didn't recognise the state and the
                // pathfinder produced overland routes while AtSea=true
                // persisted — the visible "boat on Mountain" bug. Removed.
                //
                // Hop-by-hop:
                //   * Ports (intermediate or final) → SetMoveGoToSettlement
                //     with NavigationType.Default. Caravans are in LAND
                //     mode here (IsCurrentlyAtSea=false), so Default lets
                //     the engine route via land faces. NavigationType.All
                //     is the LEAST restrictive — it would let the engine
                //     pick water-shortcut paths for naval-capable caravans,
                //     producing the "caravan walking on water in land
                //     mode" bug (Convoy of X visible on CoastalSea, no
                //     boat sprite). Default keeps them on the road.
                //     If the route legitimately needs to cross water,
                //     vanilla NavalDLC's NavigationTransition mechanism
                //     auto-boards the caravan when it reaches a port.
                //   * Non-port intermediate fiefs are passed through at the
                //     gate via SetMoveGoToPoint — no entry, no trade fees.
                //     AdvanceHopByHopWaypoints detects gate-arrival and
                //     re-runs us for the next hop.
                if (nextHop.HasPort || nextHop.IsTown || nextHop.IsCastle)
                {
                    try { caravan.SetMoveGoToSettlement(nextHop, MobileParty.NavigationType.Default, false); } catch { }
                }
                else
                {
                    var hopPoint = new TaleWorlds.CampaignSystem.CampaignVec2(nextHop.GatePosition.ToVec2(), true);
                    caravan.SetMoveGoToPoint(hopPoint, MobileParty.NavigationType.Default);
                }
                hopByHopState[caravan] = (intendedDest, nextHop);
                return true;
            }
            catch
            {
                // Defensive: never throw out of a movement helper.
                return false;
            }
        }

        // Per-tick advancement of caravans in hop-by-hop mode. Called from
        // TickParty before the redirect runs. Detects "arrived at current
        // waypoint" via straight-line proximity and re-routes to the next
        // hop (or final approach if intended is now within range).
        private void AdvanceHopByHopWaypoints(MobileParty caravan)
        {
            if (caravan == null || !caravan.IsCaravan) return;
            if (!hopByHopState.TryGetValue(caravan, out var state)) return;
            try
            {
                // Sanity: intended must still be a valid settlement.
                if (state.intended == null
                    || (state.intended.Town == null && !state.intended.IsVillage))
                {
                    hopByHopState.Remove(caravan);
                    return;
                }

                var pos = caravan.GetPosition2D;

                // Caravan reached current waypoint area? Recompute from here
                // toward intended. (Also recompute if waypoint is null — first
                // tick after a partial-rescue path.)
                bool nearWaypoint = state.waypoint == null
                    || pos.Distance(state.waypoint.GatePosition.ToVec2()) <= HopArrivalDistance;
                bool nearIntended = pos.Distance(state.intended.GatePosition.ToVec2()) <= HopFinalApproachDistance;

                if (nearIntended || nearWaypoint)
                {
                    RouteCaravanHopByHop(caravan, state.intended);
                }
            }
            catch { /* defensive */ }
        }

        private static bool IsClearlyLand(TerrainType t)
        {
            return t == TerrainType.Plain
                || t == TerrainType.Forest
                || t == TerrainType.Steppe
                || t == TerrainType.Desert
                || t == TerrainType.Snow
                || t == TerrainType.Mountain
                || t == TerrainType.Dune
                || t == TerrainType.Swamp;
        }

        private static bool IsOpenWater(TerrainType t)
        {
            return t == TerrainType.Water
                || t == TerrainType.CoastalSea
                || t == TerrainType.OpenSea
                || t == TerrainType.Lake;
        }

        // Closest sea-reachable graph port to the party's current position,
        // skipping hostile / besieged candidates and an optional excluded
        // node. Public so the unstrand_party cheat can reuse the same
        // selection logic as the F-rescue branch.
        public Settlement FindNearestSeaReachablePort(MobileParty party, Settlement excluded)
        {
            if (party == null) return null;
            var graph = ShippingGraph.Instance;
            var partyPos = party.GetPosition2D;
            Settlement bestPort = null;
            float bestDist = float.MaxValue;
            foreach (var node in graph.Adjacency.Keys)
            {
                if (node == null || node == excluded) continue;
                if (node.IsUnderSiege) continue;
                if (node.MapFaction != null && node.MapFaction.IsAtWarWith(party.MapFaction)) continue;
                bool hasSea = false;
                foreach (var edge in graph.Adjacency[node])
                {
                    if (edge.Kind == ShippingGraph.EdgeKind.Sea) { hasSea = true; break; }
                }
                if (!hasSea) continue;
                float d = partyPos.Distance(node.GatePosition.ToVec2());
                if (d < bestDist) { bestDist = d; bestPort = node; }
            }
            return bestPort;
        }

        // Nearest town for the stuck-on-coast / off-mesh-tile rescue.
        // Vanilla's settlement-exit logic places exiting parties on a
        // guaranteed on-mesh tile, so dropping a stuck party into ANY
        // non-hostile town breaks the off-mesh wedge — the party pays a
        // one-time settlement-entry tax and walks out cleanly. We don't
        // require HasPort here because the goal is just "get the party
        // back on the navmesh"; if the chosen town happens to be a port
        // and the party's intended destination requires shipping,
        // AfterSettlementEntered_Caravan auto-boards on entry.
        // True when BK's shipping pipeline should leave this party to
        // vanilla CaravanAi (rescue oscillation detected; cooldown active).
        // Checked at the top of every BK shipping hook so no edge case can
        // re-engage the pipeline mid-cooldown.
        private bool IsBkOptedOut(MobileParty party)
        {
            if (party == null) return false;
            if (!bkOptOutUntilHour.TryGetValue(party, out long until)) return false;
            long nowHour = (long)TaleWorlds.CampaignSystem.CampaignTime.Now.ToHours;
            if (nowHour < until) return true;
            // Cooldown expired — clear the entry and the rescue history so
            // we start counting fresh.
            bkOptOutUntilHour.Remove(party);
            if (rescueHistory.ContainsKey(party)) rescueHistory.Remove(party);
            return false;
        }

        // Record a rescue against this party. If the rescue count in the
        // recent window crosses the threshold, set the opt-out flag.
        // Returns true when opt-out was just activated (caller can log it).
        private bool RecordRescueAndCheckOscillation(MobileParty party)
        {
            if (party == null) return false;
            long nowHour = (long)TaleWorlds.CampaignSystem.CampaignTime.Now.ToHours;
            if (!rescueHistory.TryGetValue(party, out var history))
            {
                history = new List<long>();
                rescueHistory[party] = history;
            }
            history.Add(nowHour);
            // Trim entries outside the window so the count only reflects
            // the recent past.
            history.RemoveAll(h => nowHour - h > OscillationWindowHours);
            if (history.Count >= OptOutRescueThreshold && !bkOptOutUntilHour.ContainsKey(party))
            {
                bkOptOutUntilHour[party] = nowHour + OptOutCooldownHours;
                return true;
            }
            return false;
        }

        // Pick a rescue destination. Strategy: drop the caravan directly
        // into its actual target settlement. Vanilla's EnterSettlementAction
        // works regardless of pathfind state (the navmesh defect that
        // trapped them is irrelevant once we're using settlement entry,
        // not movement), and AfterSettlementEntered_Caravan re-runs
        // ThinkNextDestination so the whole BK pipeline resets cleanly
        // for the caravan's next leg.
        //
        // Falls back to the nearest non-hostile town only when the
        // target itself is invalid (null, sieged, hostile, or a village
        // with no bound fief).
        //
        // Note: the plannedPath argument is unused by this strategy; kept
        // in the signature for callers that already pass it in.
        private Settlement ChooseRescueTown(MobileParty party, Vec2 fromPos, List<Settlement> plannedPath)
        {
            if (party == null) return null;

            // Prefer the actual target. If it's a village, route to the
            // village's bound fief — entering a village can't reset the
            // trade pipeline since vanilla doesn't track village stays
            // for caravans.
            Settlement target = party.TargetSettlement;
            if (target != null)
            {
                Settlement candidate = target;
                if (candidate.IsVillage && candidate.Village?.Bound != null)
                {
                    candidate = candidate.Village.Bound;
                }
                // For CARAVAN parties, exclude castles — EnterSettlementAction
                // for caravans into castles isn't vanilla-supported and
                // produces "caravan inside castle" desync. Lord parties can
                // legitimately rescue into castles.
                bool isCaravan = party.IsCaravan;
                bool usable = (candidate.IsTown || (!isCaravan && candidate.IsCastle))
                    && !candidate.IsUnderSiege
                    && (candidate.MapFaction == null
                        || !candidate.MapFaction.IsAtWarWith(party.MapFaction));
                if (usable) return candidate;
            }

            // Target is null / sieged / hostile / unusable — fall back to
            // the absolute-nearest non-hostile town from the current
            // position so the caravan at least gets onto a clean mesh tile.
            return FindNearestRescueTown(party, fromPos);
        }

        public Settlement FindNearestRescueTown(MobileParty party, Vec2 fromPos)
        {
            if (party == null) return null;
            Settlement best = null;
            float bestDist = float.MaxValue;
            try
            {
                foreach (var s in Settlement.All)
                {
                    if (s == null) continue;
                    if (!s.IsTown) continue;
                    if (s.IsUnderSiege) continue;
                    if (s.MapFaction != null && s.MapFaction.IsAtWarWith(party.MapFaction)) continue;
                    float d = fromPos.Distance(s.GatePosition.ToVec2());
                    if (d < bestDist) { bestDist = d; best = s; }
                }
            }
            catch { /* Settlement.All not ready — return whatever we found */ }
            return best;
        }

        // Naval-aware variant: filter to PORT towns only. Used by LoadCleanup
        // when rescuing AtSea-on-land convoys — sending a naval party to
        // an inland town parks it where it can never re-enter the sea graph,
        // perpetuating the stranded-boat problem. A port haven puts the
        // party at the water side of a real harbour, so the next hourly
        // tick re-picks via the (now port-gated) ThinkNextDestination and
        // the convoy resumes normal sea routing. Falls back to FindNearestRescueTown
        // if no eligible port exists (no-WarSails / hostile-only ports).
        public Settlement FindNearestPort(MobileParty party, Vec2 fromPos)
        {
            if (party == null) return null;
            Settlement best = null;
            float bestDist = float.MaxValue;
            try
            {
                foreach (var s in Settlement.All)
                {
                    if (s == null) continue;
                    if (!s.IsTown) continue;
                    if (s.IsUnderSiege) continue;
                    if (!s.HasPort) continue;
                    if (s.MapFaction != null && s.MapFaction.IsAtWarWith(party.MapFaction)) continue;
                    float d = fromPos.Distance(s.GatePosition.ToVec2());
                    if (d < bestDist) { bestDist = d; best = s; }
                }
            }
            catch { }
            return best ?? FindNearestRescueTown(party, fromPos);
        }

        // Top-of-pipeline opt-out gate. Caravans flagged as oscillating
        // get fully ignored by BK shipping for the cooldown so vanilla
        // CaravanAi can pick a fresh target without BK fighting it.
        private bool ShipPipelineSkipForOptOut(MobileParty party)
        {
            if (!IsBkOptedOut(party)) return false;
            // Cleanup any state BK had on this party so vanilla starts fresh.
            try { redirectCache.Remove(party); } catch { }
            try { hopByHopState.Remove(party); } catch { }
            return true;
        }

        private void TickParty(MobileParty party)
        {
            // ROOT-FIX GATE: don't touch parties the engine is mid-transitioning
            // (boarding ship, disembarking, etc.). Issuing a SetMove at this
            // point causes CommonTransitioningPartyTick to cancel the
            // transition; CancelNavigationTransitionParallel only clears
            // the transition fields — it does NOT flip IsCurrentlyAtSea
            // back. Result: party left AtSea-on-land or land-mode-on-water.
            // Letting the engine finish the transition cleanly avoids the
            // entire desync class.
            if (party == null || party.IsTransitionInProgress) return;

            // STUTTER FIX (BK_hourly_perf.txt 2026-05-15 showed this method
            // at 204 ms / 320 ms total per game-hour): vanilla fires
            // HourlyTickPartyEvent for every party, but the only cohorts
            // any shipping sub-method actually services are caravans and
            // lord parties — RedirectAIToShippingPort early-returns at
            // `if (!isAILord && !isCaravan) return;`, ProbeCaravanWalking
            // Water is caravan-only, AdvanceHopByHopWaypoints reads
            // hopByHopState which is only written for caravans. For the
            // hundreds of villager / militia / bandit / garrison / retinue /
            // BK-component parties active in the world, every tick was
            // paying stopwatch + sub-call overhead just to no-op. Hoist
            // the cohort filter up front so we don't even start the perf
            // timer on parties we won't touch.
            if (!party.IsCaravan && !party.IsLordParty) return;

            // Always-on watch (toggle-independent) feeds the slow-handler
            // self-detector; PerfRecord below is still internally gated on
            // LogHourlyTickPerf, so steady-state cost is just the timer.
            var sw = System.Diagnostics.Stopwatch.StartNew();
            BannerKings.Utils.FreezeWatchdog.Enter("BKShipping.TickParty", BannerKings.Utils.TickTrace.IdOf(party));

            // Re-target caravans whose destination flipped hostile or got
            // sieged mid-trip. Without this they keep hop-routing into a
            // settlement they can't actually use, the redirect's "no valid
            // entry node (all hostile)" branch fires, and they stall in
            // enemy territory until the rescue eventually intervenes.
            // Pick the nearest non-hostile town as the new target; the
            // hop-by-hop tick below picks up the new destination cleanly.
            if (party != null && party.IsCaravan && party != MobileParty.MainParty)
            {
                var t = party.TargetSettlement;
                bool targetUnusable = t != null
                    && (t.IsUnderSiege
                        || (t.MapFaction != null
                            && t.MapFaction != party.MapFaction
                            && t.MapFaction.IsAtWarWith(party.MapFaction)));
                if (targetUnusable)
                {
                    Settlement replacement = FindNearestRescueTown(party, party.GetPosition2D);
                    if (replacement != null && replacement != t)
                    {
                        LogRedirect(party, $"target {t.Name} now hostile/sieged — re-targeting to nearest friendly town {replacement.Name}", t);
                        try { party.SetMoveGoToSettlement(replacement, MobileParty.NavigationType.Default, false); } catch { }
                        try { hopByHopState.Remove(party); } catch { }
                        try { redirectCache.Remove(party); } catch { }
                    }
                }
            }

            // Diagnostic probe (NOT a rescue): surface caravans currently
            // walking water in land mode so we can find the root cause.
            // Logs context on every detected occurrence; does not modify
            // party state. Hiding the symptom with a teleport-rescue is
            // a bug-mask, not a fix.
            ProbeCaravanWalkingWater(party);

            // Hop-by-hop advancement runs first. If the caravan is in
            // waypoint mode and has reached its current waypoint, advance
            // to the next hop (or final approach to intended destination).
            // Runs unconditionally so a caravan that vanilla AI redirected
            // away from its waypoint gets re-routed cleanly.
            AdvanceHopByHopWaypoints(party);

            // Proactive port redirect — when an AI lord party or caravan has a
            // target settlement on a shipping lane, and the nearest connecting
            // port is much closer than the target itself, redirect to the port.
            // The party will reach the port, then AfterSettlementEntered's
            // shipping hook auto-boards them. Without this, vanilla AI often
            // pathfinds straight toward a cross-water target and the party gets
            // stuck on the coast indefinitely (Nord caravan → Osican was the
            // motivating report).
            //
            // Skip the redirect for caravans already in hop-by-hop mode —
            // their movement is owned by the waypoint logic above. Letting
            // RedirectAIToShippingPort run would re-evaluate based on
            // TargetSettlement (which SetMoveGoToPoint may or may not
            // preserve) and could fight the waypoint progression.
            if (!hopByHopState.ContainsKey(party))
            {
                if (!ShipPipelineSkipForOptOut(party))
                {
                    RedirectAIToShippingPort(party);
                }
            }
            sw.Stop();
            PerfRecord("BKShipping.TickParty", sw);
            BannerKings.Utils.TickTrace.WatchSlow("BKShipping.TickParty", BannerKings.Utils.TickTrace.IdOf(party), sw);
            BannerKings.Utils.FreezeWatchdog.Exit();
        }

        // Daily naval-activity snapshot. Three signals to diagnose why
        // observed lord sea-travel rate is low — without behaviour
        // change. One line per game day to BK_naval_activity.txt.
        //
        // Signal 1 (silent auto-board indicator): how many lord parties
        //   are currently AtSea? Vanilla's CheckTransitionParallel embarks
        //   parties without going through any BK hook, so BK's
        //   "lord auto-board: SetSailAtPosition" log only counts the
        //   manual rescue path. If atSea/total is non-trivial, vanilla
        //   IS sailing lords and we just don't see it in BK redirect
        //   logs. If it's always 0, vanilla isn't.
        //
        // Signal 2 (amplifier-penalty indicator): how many lord parties
        //   have damaged ships and how badly are they under-crewed?
        //   AiHelper.CalculateShipDistanceAmplifier multiplies naval
        //   distance by 1.5×–3.5× for under-crewed parties and adds a
        //   penalty proportional to safe-sail-duration vs journey time.
        //   If most lord ships are damaged or under-crewed, vanilla's
        //   sea-vs-land decision keeps picking land even when sea is
        //   shorter.
        //
        // Signal 3 (sea-travel-candidate indicator): how many naval-
        //   capable lords currently have a TargetSettlement that's
        //   land-unreachable from their current position? These are
        //   the parties that MUST sail to reach their goal — the upper
        //   bound on visible sea-travel demand.
        // Daily in-session convoy beaching detector. Mirrors LoadCleanup's
        // Mismatch G + C1 signatures, scoped to caravans, scoped to whether
        // we believe the party is on a land tile. When detected, route to
        // nearest port via EnterSettlementAction and re-pin the target with
        // Naval navigation so the convoy resumes water-only routing.
        //
        // Why daily not hourly: an hourly sweep used to oscillate against
        // BK's own redirect logic — rescue a party into a port, BK redirect
        // routes them out, vanilla pathfinder hits a dead-end tile, rescue
        // fires again. Daily is far enough below redirect-cache TTL (6h)
        // that the rescue + redirect pair settles before re-evaluation.
        //
        // Scoped to caravans (Convoy invariant): naval-capable lords
        // legitimately walk on land between voyages — only auto-rescue
        // caravans, where the naming convention is the canonical mode.
        private void DailyConvoyBeachingCheck()
        {
            // MCM gate (see LoadCleanup for rationale).
            if (!BannerKings.Settings.BannerKingsSettings.Instance.EnableShippingRescues) return;

            int rescued = 0;
            try
            {
                var wrapper = TaleWorlds.CampaignSystem.Campaign.Current?.MapSceneWrapper;
                if (wrapper == null) return;

                foreach (var party in MobileParty.AllCaravanParties)
                {
                    if (party == null || !party.IsActive) continue;
                    if (party.CurrentSettlement != null) continue;
                    if (party.MapEvent != null) continue;
                    if (party.IsTransitionInProgress) continue;

                    bool naval = false;
                    try { naval = party.HasNavalNavigationCapability; } catch { }
                    if (!naval) continue;

                    // Cooldown so a freshly-rescued convoy doesn't re-trigger
                    // mid-walk-out from the haven port. RescueCooldownHours
                    // is the same window used by other rescue paths.
                    long nowHour;
                    try { nowHour = (long)CampaignTime.Now.ToHours; } catch { continue; }
                    if (lastRescueHour.TryGetValue(party, out long lastHour)
                        && nowHour - lastHour < RescueCooldownHours) continue;

                    bool needRescue = false;
                    string reason = null;

                    // Mismatch C1: AtSea=true on interior land terrain
                    // (Plain / Forest / Steppe / etc). Unambiguous boat-on-
                    // land — Position.IsOnLand can lie post-cancelled-
                    // transition so we don't require it for interior faces.
                    try
                    {
                        var t = wrapper.GetFaceTerrainType(party.CurrentNavigationFace);
                        if (party.IsCurrentlyAtSea && IsClearlyLand(t))
                        {
                            needRescue = true;
                            reason = $"AtSea over interior {t}";
                        }
                        else if (party.IsCurrentlyAtSea && !IsOpenWater(t))
                        {
                            // Mismatch C2: water-adjacent face (Beach /
                            // RuralArea / Bridge). Need both signals to
                            // avoid false-positive at PortPosition where
                            // the party is legitimately on water.
                            bool onWaterByPosition = false;
                            try { onWaterByPosition = !party.Position.IsOnLand; } catch { }
                            if (!onWaterByPosition)
                            {
                                needRescue = true;
                                reason = $"AtSea over {t} (water-adjacent + Position-on-land)";
                            }
                        }
                        else if (!needRescue)
                        {
                            // Mismatch G: naval-capable caravan on a land
                            // face regardless of AtSea. Convoy invariant —
                            // a caravan with naval cap should never be
                            // walking on land tiles.
                            if (IsClearlyLand(t))
                            {
                                needRescue = true;
                                reason = $"Convoy on interior {t}";
                            }
                            else if (!IsOpenWater(t))
                            {
                                bool onWaterByPosition = false;
                                try { onWaterByPosition = !party.Position.IsOnLand; } catch { }
                                if (!onWaterByPosition)
                                {
                                    needRescue = true;
                                    reason = $"Convoy on {t} (water-adjacent + Position-on-land)";
                                }
                            }
                        }
                    }
                    catch { /* defensive */ }

                    if (!needRescue) continue;

                    Settlement haven = FindNearestPort(party, party.GetPosition2D);
                    // Require a real port. FindNearestPort falls back to
                    // FindNearestRescueTown which returns ANY town — a
                    // landlocked inland town would be the same desync we're
                    // trying to fix (Naval-routed convoy parked on land
                    // tile). If no port is reachable this tick, skip the
                    // rescue and try again tomorrow; LoadCleanup catches
                    // it on next save load.
                    if (haven == null || !haven.HasPort) continue;

                    try
                    {
                        // Pre-clear stale BK state — see LoadCleanup for the
                        // duplicate-source-of-truth rationale (clear before
                        // EnterSettlementAction so AfterSettlementEntered's
                        // re-route runs on a clean slate).
                        try { InvalidateRedirectCache(party); } catch { }
                        try { if (lastPortLeft.ContainsKey(party)) lastPortLeft.Remove(party); } catch { }
                        try { if (lastRescueTown.ContainsKey(party)) lastRescueTown.Remove(party); } catch { }

                        bool hasLand = false;
                        try { hasLand = party.HasLandNavigationCapability; } catch { }
                        if (hasLand)
                        {
                            try { party.IsCurrentlyAtSea = false; } catch { }
                        }
                        // EnterSettlementAction synchronously fires
                        // AfterSettlementEntered_Caravan which already calls
                        // RouteCaravanHopByHop with naval-aware routing for
                        // a Convoy. A post-Enter SetMoveGoToSettlement(haven,
                        // Naval) here would OVERWRITE that hop's target,
                        // pinning the convoy at the haven instead of letting
                        // it resume hop-by-hop traversal. Trust the existing
                        // re-route inside the action. Note: rescueHistory
                        // and bkOptOutUntilHour are intentionally untouched
                        // — this rescue is not the oscillation pattern those
                        // tracks; oscillation guard is via lastRescueHour
                        // cooldown.
                        TaleWorlds.CampaignSystem.Actions.EnterSettlementAction.ApplyForParty(party, haven);

                        try { if (party.Ai != null) party.Ai.RethinkAtNextHourlyTick = true; } catch { }

                        lastRescueHour[party] = nowHour;
                        lastRescueTown[party] = haven;
                        rescued++;
                        LogRescue(party, $"daily-sweep: ENTER {haven.Name} ({reason})");
                    }
                    catch { /* skip parties the engine refuses */ }
                }
            }
            catch { /* never crash a daily tick */ }

            if (rescued > 0)
            {
                try
                {
                    TaleWorlds.Library.Debug.Print(
                        $"[BK] Daily convoy sweep: {rescued} beached caravan(s) rescued",
                        color: TaleWorlds.Library.Debug.DebugColor.Yellow);
                }
                catch { }
            }
        }

        private void DailyNavalActivityProbe()
        {
            try
            {
                int total = 0, navalCap = 0, atSea = 0;
                int damagedShipCount = 0, totalShipsAcrossLords = 0;
                float hpRatioSum = 0f; int hpRatioCount = 0;
                int underCrewedParties = 0;
                int candidates = 0, candidatesAtSea = 0;
                TaleWorlds.CampaignSystem.ComponentInterfaces.MapDistanceModel distModel = null;
                try { distModel = Campaign.Current.Models.MapDistanceModel; } catch { }

                foreach (var party in MobileParty.AllLordParties)
                {
                    if (party == null) continue;
                    total++;

                    bool naval = false;
                    try { naval = party.HasNavalNavigationCapability; } catch { }
                    if (!naval) continue;
                    navalCap++;

                    bool isSea = false;
                    try { isSea = party.IsCurrentlyAtSea; } catch { }
                    if (isSea) atSea++;

                    // Ship-health.
                    int shipsCount = 0, partyDamaged = 0;
                    int totalCrewCap = 0;
                    try
                    {
                        if (party.Ships != null)
                        {
                            foreach (var ship in party.Ships)
                            {
                                if (ship == null) continue;
                                shipsCount++;
                                float maxHp = 0f, hp = 0f;
                                try { maxHp = ship.MaxHitPoints; hp = ship.HitPoints; } catch { }
                                if (maxHp > 0f)
                                {
                                    float ratio = hp / maxHp;
                                    hpRatioSum += ratio; hpRatioCount++;
                                    if (ratio < 0.99f) { partyDamaged++; damagedShipCount++; }
                                }
                                try { totalCrewCap += ship.TotalCrewCapacity; } catch { }
                            }
                        }
                    }
                    catch { }
                    totalShipsAcrossLords += shipsCount;

                    int troopCount = 0;
                    try { troopCount = party.MemberRoster?.TotalManCount ?? 0; } catch { }
                    if (totalCrewCap > 0 && troopCount > totalCrewCap)
                    {
                        underCrewedParties++;
                    }

                    // Sea-travel candidate: target is land-unreachable.
                    Settlement target = null;
                    try { target = party.TargetSettlement; } catch { }
                    if (target != null && distModel != null)
                    {
                        float d = float.PositiveInfinity;
                        try { d = distModel.GetDistance(party, target, false, MobileParty.NavigationType.Default, out _); }
                        catch { }
                        bool landReachable = !float.IsNaN(d) && !float.IsInfinity(d) && d >= 0f && d < 1e6f;
                        if (!landReachable)
                        {
                            candidates++;
                            if (isSea) candidatesAtSea++;
                        }
                    }
                }

                float pctSea = total == 0 ? 0f : 100f * atSea / total;
                float pctDamagedShips = totalShipsAcrossLords == 0 ? 0f : 100f * damagedShipCount / totalShipsAcrossLords;
                float avgHpRatio = hpRatioCount == 0 ? 1f : hpRatioSum / hpRatioCount;
                float pctUnderCrewed = navalCap == 0 ? 0f : 100f * underCrewedParties / navalCap;
                float pctCandidatesSailing = candidates == 0 ? 0f : 100f * candidatesAtSea / candidates;

                BannerKingsCheats.AppendDiagnosticLine("naval_activity.txt",
                    $"lords total={total} navalCap={navalCap} atSea={atSea} ({pctSea:0.0}%) | "
                    + $"ships total={totalShipsAcrossLords} damaged={damagedShipCount} ({pctDamagedShips:0.0}%) avgHP={avgHpRatio:0.00} | "
                    + $"underCrewedParties={underCrewedParties} ({pctUnderCrewed:0.0}% of naval) | "
                    + $"sea-travel-candidates={candidates} sailing={candidatesAtSea} ({pctCandidatesSailing:0.0}%)");
            }
            catch { /* never throw out of a daily-tick probe */ }
        }

        // Diagnostic probe (NOT a rescue). When a caravan is in land mode
        // (IsCurrentlyAtSea=false) on a water terrain face, log context
        // so the root cause is observable. Does NOT modify state — hiding
        // this symptom with a teleport-rescue masks the underlying bug
        // (engine pathfinder routing naval-capable caravans across water
        // without firing the boarding transition that flips AtSea). The
        // log line includes everything needed to trace the bug back to
        // the writer that left the party in this state.
        //
        // Throttled per-party to once per RescueCooldownHours hours so
        // a stuck caravan doesn't spam the log every tick.
        private readonly Dictionary<MobileParty, long> walkingWaterLastLogHour
            = new Dictionary<MobileParty, long>();

        private void ProbeCaravanWalkingWater(MobileParty party)
        {
            if (party == null || party == MobileParty.MainParty) return;
            if (!party.IsCaravan) return;
            if (party.IsCurrentlyAtSea) return;
            if (party.CurrentSettlement != null) return;
            if (party.IsTransitionInProgress) return;
            if (party.MapEvent != null) return;
            try
            {
                var wrapper = TaleWorlds.CampaignSystem.Campaign.Current?.MapSceneWrapper;
                if (wrapper == null) return;
                TerrainType terrain = wrapper.GetFaceTerrainType(party.CurrentNavigationFace);
                if (!IsOpenWater(terrain)) return;

                long nowHour = (long)CampaignTime.Now.ToHours;
                if (walkingWaterLastLogHour.TryGetValue(party, out long prevHour)
                    && nowHour - prevHour < RescueCooldownHours) return;
                walkingWaterLastLogHour[party] = nowHour;

                // Build a context dump with everything that could explain
                // why the caravan is on water in land mode.
                var pos = party.GetPosition2D;
                string target = "(null)"; try { target = party.TargetSettlement?.Name?.ToString() ?? "(null)"; } catch { }
                int ships = 0; try { ships = party.Ships?.Count ?? 0; } catch { }
                bool naval = false; try { naval = party.HasNavalNavigationCapability; } catch { }
                MobileParty.NavigationType navCap = MobileParty.NavigationType.Default;
                try { navCap = party.NavigationCapability; } catch { }
                bool aiDisabled = false; try { aiDisabled = party.Ai?.IsDisabled ?? false; } catch { }
                bool hopState = hopByHopState.ContainsKey(party);
                string lastLeft = "(none)";
                try
                {
                    if (lastPortLeft.TryGetValue(party, out var llp) && llp.port != null)
                        lastLeft = $"{llp.port.Name} {nowHour - llp.hour}h ago";
                }
                catch { }

                LogRescue(party,
                    $"WALKING-WATER PROBE @ ({pos.X:0.0},{pos.Y:0.0}) terrain={terrain} "
                    + $"target={target} ships={ships} naval={naval} navCap={navCap} "
                    + $"aiDisabled={aiDisabled} hopByHopState={hopState} lastLeft={lastLeft}");
            }
            catch { /* never throw out of an hourly tick */ }
        }

        private void RedirectAIToShippingPort(MobileParty party)
        {
            try
            {
                if (party == null || party == MobileParty.MainParty) return;
                // ROOT-FIX GATE: see TickParty comment.
                if (party.IsTransitionInProgress) return;
                // Hands-off when player is in this army, or party is in a
                // siege / map event. See ShouldSkipForArmyOrSiege.
                if (ShouldSkipForArmyOrSiege(party)) return;

                // Two redirect cohorts: AI lord parties (not the player's own
                // clan — they pilot themselves) and caravans (any clan, since
                // the player doesn't directly steer caravans either way).
                // Caravans don't require a LeaderHero check because the
                // owning merchant might be in a town/captured/dead while
                // the caravan continues to operate. AI lords DO require a
                // LeaderHero so we can do the player-clan check.
                bool isCaravan = party.IsCaravan;
                bool isAILord = party.IsLordParty
                    && party.LeaderHero != null
                    && party.LeaderHero.Clan != null
                    && party.LeaderHero.Clan != Clan.PlayerClan;
                if (!isAILord && !isCaravan) return;

                // STUTTER FIX (knight-clan lord-party scaling): the port
                // redirect is only useful for parties that can actually
                // embark on a ship at the port. Non-naval parties would
                // walk to the port, fail to board, and walk back —
                // wasted cycles. AI knight clans (which the user
                // identified as a stutter contributor) typically have
                // many lord parties with no naval capability; gating
                // them off here matches the same check BKLordShipping
                // Behavior.TickParty applies and short-circuits the
                // expensive graph+pathfind work below before it starts.
                bool navalCapEarly = false;
                try { navalCapEarly = party.HasNavalNavigationCapability; } catch { navalCapEarly = false; }
                if (!navalCapEarly) return;

                // For armies, redirect only the leader; sub-parties follow via
                // Army linkage. Skipping armies entirely would strand cross-water
                // sieges at the coast.
                if (party.Army != null && party.Army.LeaderParty != party) return;

                // Hands-off: parties already at sea are owned by NavalDLC
                // (its convoys / its naval AI) or by BK's own SetSail
                // branch for AI lords. Issuing SetMoveGoToSettlement with
                // NavigationType.All on an at-sea party gives the land
                // pathfinder a target, so the party walks on land terrain
                // while still showing the boat sprite — "boating around on
                // land", which was the reported symptom in the field.
                if (party.IsCurrentlyAtSea) return;
                if (party.CurrentSettlement != null) return;        // wait for Step A on settlement entry

                var target = party.TargetSettlement;
                if (target == null) return;

                // Movement-progress tracker. Update FIRST, before any
                // early-returns or cache checks. If the party isn't
                // making meaningful forward progress per tick, every
                // subsequent hourly tick fully re-evaluates the redirect
                // (bypassing cache + loop-prevention) so the seaFallback /
                // alternative-port branches get a chance to run. We do
                // NOT teleport — caravans get a different target, not a
                // different position. If the alternative target also
                // can't be reached, the underlying issue (graph data
                // claiming a road that the navmesh doesn't honour, or
                // similar) needs to be fixed at source, not papered
                // over by relocating the party.
                var partyPosNow = party.GetPosition2D;
                int progressStuckTicks = 0;
                if (progressTracker.TryGetValue(party, out var prog))
                {
                    if (prog.lastPos.Distance(partyPosNow) >= ProgressEpsilonPerTick)
                    {
                        progressTracker[party] = (partyPosNow, 0);
                    }
                    else
                    {
                        progressStuckTicks = prog.stuckTicks + 1;
                        progressTracker[party] = (prog.lastPos, progressStuckTicks);
                    }
                }
                else
                {
                    progressTracker[party] = (partyPosNow, 0);
                }

                bool progressStuck = progressStuckTicks >= BypassLoopPreventionThreshold;

                // Per-party throttle: if we evaluated this same target for
                // this party recently, skip the heavy graph + pathfind work.
                // Vanilla CaravanAi changes a caravan's target relatively
                // rarely (after settlement entry / trade decision), so the
                // 6-hour cache lifetime is a sweet spot — tight enough to
                // pick up a fresh decision after a couple of game hours,
                // loose enough to avoid the per-hour storm of Dijkstra
                // recomputes that was driving the nightfall freezes.
                long nowHour;
                try { nowHour = (long)TaleWorlds.CampaignSystem.CampaignTime.Now.ToHours; }
                catch { nowHour = 0L; }
                // Bypass the cache for parties detected as not-progressing
                // by the progress watchdog. If position-tracking shows this
                // party hasn't moved across BypassLoopPreventionThreshold
                // ticks, retry the full redirect every hour rather than
                // skipping for up to 6 hours on a stale cached entry.
                if (!progressStuck
                    && redirectCache.TryGetValue(party, out var cached)
                    && cached.target == target
                    && nowHour - cached.hour < RedirectCacheLifetimeHours)
                {
                    return;
                }
                redirectCache[party] = (target, nowHour);

                // Rescue cooldown: skip if we just rescued this party. Vanilla
                // AI needs time to update TargetSettlement post-settlement-
                // visit; rescuing again before that just re-traps them in
                // the same in-and-out cycle around the rescue town.
                if (lastRescueHour.TryGetValue(party, out long lastHour)
                    && nowHour - lastHour < RescueCooldownHours)
                {
                    return;
                }

                // Loop-prevention: if the current target is itself a port
                // (graph node with at least one sea edge), the caravan
                // MIGHT already be heading to a boarding port from a
                // prior redirect of ours. Re-evaluating from a port-target
                // makes us pick a DIFFERENT port (the original target gets
                // excluded from the candidate scan as graphTarget), and
                // the next tick we flip back, ping-ponging the caravan
                // forever between two ports without it ever actually
                // walking.
                //
                // BUT: this assumption breaks for caravans whose ORIGINAL
                // target is a port across water (e.g. Sturgia caravan
                // targeting Gretysfjord, vanilla CaravanAi assigned that
                // target directly). Vanilla land-pathfind from this
                // island's coast to that port returns Infinity, so the
                // caravan parks on the shore. Verify reachability before
                // bailing — only skip if vanilla can actually walk there
                // from current position.
                {
                    var graphCheck = ShippingGraph.Instance;
                    if (graphCheck.Adjacency.ContainsKey(target))
                    {
                        bool targetHasSeaEdge = false;
                        foreach (var edge in graphCheck.Adjacency[target])
                        {
                            if (edge.Kind == ShippingGraph.EdgeKind.Sea) { targetHasSeaEdge = true; break; }
                        }
                        if (targetHasSeaEdge)
                        {
                            // Probe whether a LAND-ONLY graph route exists from
                            // the party's nearest graph node to this port-target.
                            // Previous probe used MapDistanceModel.GetDistance
                            // with NavigationType.Default — for naval-capable
                            // caravans (boat troop in roster) Default permits
                            // water faces, so the probe returned "reachable"
                            // for routes that vanilla in fact walked *across
                            // water* in land mode (the "Varag the Baker walking
                            // across the channel" report). The
                            // MobileParty.NavigationType enum has only
                            // None/Default/Naval/All — there is no Land value
                            // — so the engine cannot answer "land-only
                            // reachable" directly. The shipping graph is the
                            // authoritative classifier: an all-Land path means
                            // a genuine land walk; any Sea edge means the
                            // caravan should board.
                            bool landOnlyPathExists = false;
                            try
                            {
                                Settlement nearestNode = null;
                                float bestD = float.MaxValue;
                                foreach (var node in graphCheck.Adjacency.Keys)
                                {
                                    if (node == null || node == target) continue;
                                    float d = partyPosNow.Distance(node.GatePosition.ToVec2());
                                    if (d < bestD) { bestD = d; nearestNode = node; }
                                }
                                if (nearestNode != null)
                                {
                                    var probePath = graphCheck.GetShortestPath(nearestNode, target);
                                    if (probePath != null && probePath.Count >= 2)
                                    {
                                        landOnlyPathExists = true;
                                        for (int i = 0; i < probePath.Count - 1; i++)
                                        {
                                            var edges = graphCheck.Adjacency[probePath[i]];
                                            var edge = edges.FirstOrDefault(e => e.To == probePath[i + 1]);
                                            if (edge.Kind == ShippingGraph.EdgeKind.Sea)
                                            {
                                                landOnlyPathExists = false;
                                                break;
                                            }
                                        }
                                    }
                                }
                            }
                            catch { landOnlyPathExists = false; }

                            if (landOnlyPathExists && !progressStuck)
                            {
                                // Optimal route to this port is land-only —
                                // let the caravan arrive, then
                                // AfterSettlementEntered_Caravan picks up
                                // the next hop. Skip silently to avoid log
                                // flood on the every-hour tick.
                                //
                                // EXCEPT when progressStuck: graph reports
                                // a land path but the party isn't moving
                                // more than ProgressEpsilonPerTick across
                                // BypassLoopPreventionThreshold ticks. Fall
                                // through to entry-node logic so the
                                // STUCK-at-coast / teleport branches can
                                // act on it.
                                return;
                            }
                            // Fall through: target is a port and either
                            // (a) optimal route includes a sea edge (caravan
                            // must board, not water-walk) or (b) no graph
                            // path at all (treat as coastal-stuck). Either
                            // way, run the entry-node + boarding-port logic
                            // below.
                        }
                    }
                }

                // Graph-driven redirect. The unified shipping graph already
                // weights every edge (sea + land) by distance × risk, so it
                // naturally chooses the shortest viable route between any
                // two settlements. Decision logic for a party out in the
                // world:
                //
                //   1. Find the nearest graph node to the party's current
                //      position. That's the entry point into the graph.
                //   2. Compute the shortest (or risk-adaptive) path from
                //      that entry node to the target.
                //   3. If the first edge of that path is a SEA edge, the
                //      optimal route boards a ship at the entry node —
                //      redirect the party to walk to the entry node so
                //      AfterSettlementEntered_Caravan can call SetTravel
                //      on arrival. The boarding port == the entry node.
                //   4. If the first edge is a LAND edge, the optimal
                //      route walks first — let vanilla AI handle it; no
                //      redirect.
                //
                // This replaces the older geometric heuristic
                // (closest-port-that's-30%-closer-than-target) which
                // missed the "land path exists, but sea is better" case
                // and pinned caravans on the shore forever.
                var graph = ShippingGraph.Instance;
                // Villages aren't graph nodes (graph only has towns + castles).
                // For a village target, route to the village's bound fief
                // instead — once the party arrives there, vanilla AI walks
                // them the rest of the way to the village by road.
                Settlement graphTarget = target;
                if (!graph.Adjacency.ContainsKey(graphTarget))
                {
                    if (graphTarget.IsVillage && graphTarget.Village?.Bound != null
                        && graph.Adjacency.ContainsKey(graphTarget.Village.Bound))
                    {
                        graphTarget = graphTarget.Village.Bound;
                    }
                    else
                    {
                        LogRedirect(party, $"target not in graph adjacency (and no bound fief in graph)", target);
                        return;
                    }
                }

                // Early exit: if graphTarget is itself the closest graph
                // node to the party, vanilla can auto-enter the gate when
                // the party walks the last few units. Forcing a detour to
                // whatever graph node is *second*-closest produces the
                // "Convoy of Marena @ (563,572) → Omor: STUCK at coast"
                // pattern: the party is at Omor's gate, BK picks Varcheg as
                // entry, vanilla pathfind to Varcheg from this tile is
                // Infinity, party fires the rescue. Let vanilla finish.
                //
                // EXCEPTION: if the watchdog says the party hasn't actually
                // moved despite "auto-enter" being declared the right
                // move, vanilla isn't going to finish — the party is
                // sitting at a navmesh edge that won't connect. Fall
                // through to the rescue/teleport branch instead. Without
                // this exception, Convoy of Varag/Ynggorm/Marena/Kras sit
                // at Omor's gate forever logging "vanilla auto-enter" on
                // every tick.
                var partyPos2D = party.GetPosition2D;
                float distToTarget = partyPos2D.Distance(graphTarget.GatePosition.ToVec2());
                bool targetIsClosest = true;
                foreach (var node in graph.Adjacency.Keys)
                {
                    if (node == null || node == graphTarget) continue;
                    if (node.IsUnderSiege) continue;
                    if (node.MapFaction != null && node.MapFaction.IsAtWarWith(party.MapFaction)) continue;
                    float d = partyPos2D.Distance(node.GatePosition.ToVec2());
                    if (d < distToTarget) { targetIsClosest = false; break; }
                }
                if (targetIsClosest && !progressStuck)
                {
                    LogRedirect(party, $"target {graphTarget.Name} is closest graph node — vanilla auto-enter", target);
                    return;
                }
                if (targetIsClosest && progressStuck)
                {
                    // PROBE: capture full state for stuck stay-around
                    // parties. Don't intervene — vanilla has its own
                    // mechanism (StartTransitionNextFrameToExitFromPort)
                    // for naval patrols and we shouldn't preempt it.
                    try
                    {
                        var defaultBeh = party.DefaultBehavior;
                        var shortBeh = party.ShortTermBehavior;
                        // Vanilla-owned movement / sit-around behaviors. BK
                        // must not preempt these — for sieges/raids/escorts
                        // the party intentionally hangs at a position and
                        // "not moving" IS the desired state, not stranding.
                        // BesiegeSettlement / RaidSettlement were originally
                        // missing here; without them, a Sturgian army that
                        // sat at the gates of an enemy castle (e.g. sieging
                        // Hakarshus) was treated as stuck and force-ENTER'd
                        // INTO the enemy castle (line below), the engine
                        // ejected them, the siege restarted, and the loop
                        // repeated forever. EngageParty / EscortParty added
                        // for armies tracking another mobile party.
                        bool stayAround = defaultBeh == TaleWorlds.CampaignSystem.Party.AiBehavior.PatrolAroundPoint
                                       || defaultBeh == TaleWorlds.CampaignSystem.Party.AiBehavior.DefendSettlement
                                       || defaultBeh == TaleWorlds.CampaignSystem.Party.AiBehavior.Hold
                                       || defaultBeh == TaleWorlds.CampaignSystem.Party.AiBehavior.BesiegeSettlement
                                       || defaultBeh == TaleWorlds.CampaignSystem.Party.AiBehavior.RaidSettlement
                                       || defaultBeh == TaleWorlds.CampaignSystem.Party.AiBehavior.EngageParty
                                       || defaultBeh == TaleWorlds.CampaignSystem.Party.AiBehavior.EscortParty
                                       || shortBeh == TaleWorlds.CampaignSystem.Party.AiBehavior.PatrolAroundPoint
                                       || shortBeh == TaleWorlds.CampaignSystem.Party.AiBehavior.DefendSettlement
                                       || shortBeh == TaleWorlds.CampaignSystem.Party.AiBehavior.Hold
                                       || shortBeh == TaleWorlds.CampaignSystem.Party.AiBehavior.BesiegeSettlement
                                       || shortBeh == TaleWorlds.CampaignSystem.Party.AiBehavior.RaidSettlement
                                       || shortBeh == TaleWorlds.CampaignSystem.Party.AiBehavior.EngageParty
                                       || shortBeh == TaleWorlds.CampaignSystem.Party.AiBehavior.EscortParty;
                        if (stayAround)
                        {
                            string sts = "(null)";
                            try { sts = party.ShortTermTargetSettlement?.Name?.ToString() ?? "(null)"; } catch { }
                            var mtp = party.MoveTargetPoint;
                            LogRedirect(party,
                                $"STUCK-PROBE land-edge: DefaultBeh={defaultBeh} ShortBeh={shortBeh} ShortTermTargetSettlement={sts} IsTargetingPort={party.IsTargetingPort} DesiredNav={party.DesiredAiNavigationType} StartTransition={party.StartTransitionNextFrameToExitFromPort} IsTransitionInProgress={party.IsTransitionInProgress} AtSea={party.IsCurrentlyAtSea} HasLand={party.HasLandNavigationCapability} HasNaval={party.HasNavalNavigationCapability} MoveTargetIsOnLand={mtp.IsOnLand}",
                                target);
                            progressTracker[party] = (party.GetPosition2D, 0);
                            return;
                        }
                    }
                    catch { }

                    // Faction filter on the force-ENTER below. Without it,
                    // any party whose target is enemy-controlled (siege,
                    // raid, hostile-fief march) would get teleported INSIDE
                    // the enemy settlement. The behavior gate above already
                    // covers siege/raid, but a hostile-target party with
                    // some other behavior (e.g. GoToSettlement against a
                    // freshly-flipped fief) could still slip through. Never
                    // force-ENTER a hostile settlement under any
                    // circumstance — let vanilla AI re-target.
                    if (graphTarget.MapFaction != null && party.MapFaction != null
                        && graphTarget.MapFaction.IsAtWarWith(party.MapFaction))
                    {
                        LogRedirect(party, $"target {graphTarget.Name} is hostile — refusing force-ENTER (would teleport into enemy fief)", target);
                        progressTracker[party] = (party.GetPosition2D, 0);
                        return;
                    }

                    // MCM gate. The force-ENTER is a rescue-style teleport;
                    // the master toggle disables it along with the other
                    // rescue passes when set off.
                    if (!BannerKings.Settings.BannerKingsSettings.Instance.EnableShippingRescues)
                    {
                        LogRedirect(party, $"force-ENTER skipped at {graphTarget.Name} — rescues disabled in MCM", target);
                        progressTracker[party] = (party.GetPosition2D, 0);
                        return;
                    }

                    // Force-enter the target for movement-required behaviors
                    // (GoToSettlement, etc.). The party is at the target's
                    // gate but vanilla can't finish the walk-and-enter.
                    LogRedirect(party, $"target {graphTarget.Name} is closest graph node BUT progressStuck — force ENTER {graphTarget.Name}", target);
                    try { TaleWorlds.CampaignSystem.Actions.EnterSettlementAction.ApplyForParty(party, graphTarget); } catch { }
                    progressTracker[party] = (party.GetPosition2D, 0);
                    if (redirectCache.ContainsKey(party)) redirectCache.Remove(party);
                    if (hopByHopState.ContainsKey(party)) hopByHopState.Remove(party);
                    lastRescueHour[party] = nowHour;
                    lastRescueTown[party] = graphTarget;
                    return;
                }
                // Entry-node selection. Two-pass:
                //   Pass 1: collect candidates by Euclidean distance (fast,
                //           no pathfinder calls). Take the K nearest.
                //   Pass 2: probe land-reachability via MapDistanceModel
                //           for each candidate in order. First one that
                //           returns finite is the entry node.
                //
                // Why two-pass: previously BK picked entryNode by Euclidean
                // alone. The graph's edges are valid in graph space, but
                // the *party's specific tile* may not be able to reach the
                // closest graph node by land (party is on a peninsula, the
                // closest port is across a bay, etc.). Setting that node
                // as the move target produced the "STUCK at coast — vanilla
                // pathfind to X returned Infinity" rescue cycles. Probing
                // a small handful of candidates costs <K MapDistanceModel
                // calls per redirect (~ms-scale, gated by the redirect cache).
                Settlement entryNode = null;
                float entryDist = float.MaxValue;
                Settlement euclidNearest = null;
                float euclidNearestDist = float.MaxValue;
                var candidates = new List<(Settlement node, float dist)>(8);
                foreach (var node in graph.Adjacency.Keys)
                {
                    if (node == null) continue;
                    if (node == graphTarget) continue;
                    if (node.IsUnderSiege) continue;
                    if (node.MapFaction != null && node.MapFaction.IsAtWarWith(party.MapFaction)) continue;
                    float d = partyPos2D.Distance(node.GatePosition.ToVec2());
                    if (d < euclidNearestDist) { euclidNearestDist = d; euclidNearest = node; }
                    candidates.Add((node, d));
                }
                if (candidates.Count == 0)
                {
                    LogRedirect(party, "no valid entry node (all hostile/sieged?)", target);
                    // Transient failure — every graph node is hostile or under
                    // siege. Drop the cache entry so the next hourly tick
                    // re-evaluates once a war/siege clears, rather than staying
                    // locked out for RedirectCacheLifetimeHours.
                    if (redirectCache.ContainsKey(party)) redirectCache.Remove(party);
                    return;
                }
                candidates.Sort((a, b) => a.dist.CompareTo(b.dist));
                // Probe up to 6 nearest. If none reachable, fall back to
                // Euclidean nearest — the stuck rescue can teleport from
                // there, which is the same as the previous behavior.
                const int EntryReachabilityProbeK = 6;
                try
                {
                    var distModel = TaleWorlds.CampaignSystem.Campaign.Current.Models.MapDistanceModel;
                    int n = System.Math.Min(EntryReachabilityProbeK, candidates.Count);
                    for (int i = 0; i < n; i++)
                    {
                        var cand = candidates[i].node;
                        float d = float.PositiveInfinity;
                        try { d = distModel.GetDistance(party, cand, false, MobileParty.NavigationType.Default, out _); }
                        catch { d = float.PositiveInfinity; }
                        if (!float.IsNaN(d) && !float.IsInfinity(d) && d >= 0f && d < 1e6f)
                        {
                            entryNode = cand;
                            entryDist = candidates[i].dist;
                            break;
                        }
                    }
                }
                catch { /* model unavailable; fall through to Euclidean */ }
                if (entryNode == null)
                {
                    entryNode = euclidNearest;
                    entryDist = euclidNearestDist;
                }

                List<Settlement> path = Settings.BannerKingsSettings.Instance.AdaptiveShippingRisk
                    ? (graph.GetAdaptivePath(entryNode, graphTarget, party.MapFaction)
                       ?? graph.GetShortestPath(entryNode, graphTarget))
                    : graph.GetShortestPath(entryNode, graphTarget);
                if (path == null || path.Count < 2)
                {
                    LogRedirect(party, $"no graph path from {entryNode.Name} to {graphTarget.Name}", target);
                    return;
                }

                // First edge of the optimal route. Sea → redirect to
                // entryNode (boarding port). Land → usually let vanilla
                // walk, BUT if vanilla can't even pathfind from the party's
                // current position to the entry node (party stuck on a
                // coast or behind impassable terrain), force-redirect to
                // the nearest sea-reachable port instead. That covers
                // "Caravan of Khachin parked at (571, 605) targeting
                // Khimli Castle" — graph says land path exists between
                // settlements, but vanilla can't get the party off the
                // coast to start that walk.
                if (!graph.Adjacency.ContainsKey(path[0]))
                {
                    LogRedirect(party, $"path[0]={path[0]?.Name} not in adjacency", target);
                    return;
                }
                var firstEdge = graph.Adjacency[path[0]].FirstOrDefault(e => e.To == path[1]);
                if (firstEdge.Kind != ShippingGraph.EdgeKind.Sea)
                {
                    // Probe BOTH the entry node AND the actual target. Vanilla
                    // CaravanAi sets the move target to the ORIGINAL destination,
                    // not the graph entry node — so even if entryNode is
                    // reachable by land from current position, the caravan
                    // can still sit forever if the original target isn't
                    // reachable. That's Khachin at (571.3, 605.6) targeting
                    // Khimli Castle: Mazhadan Castle is reachable, Khimli
                    // Castle is not, vanilla AI keeps trying Khimli Castle
                    // and failing. Treat as stuck if either probe returns
                    // unreachable.
                    bool stuck = false;
                    try
                    {
                        var distModel = TaleWorlds.CampaignSystem.Campaign.Current.Models.MapDistanceModel;
                        float landToEntry = distModel.GetDistance(party, entryNode, false, MobileParty.NavigationType.Default, out _);
                        if (float.IsNaN(landToEntry) || float.IsInfinity(landToEntry) || landToEntry < 0f || landToEntry > 1e6f)
                            stuck = true;
                        if (!stuck && target != entryNode)
                        {
                            float landToTarget = distModel.GetDistance(party, target, false, MobileParty.NavigationType.Default, out _);
                            if (float.IsNaN(landToTarget) || float.IsInfinity(landToTarget) || landToTarget < 0f || landToTarget > 1e6f)
                                stuck = true;
                        }
                    }
                    catch { /* if pathfind throws, treat as not-stuck */ }

                    // Promote the progress watchdog's verdict to a stuck=true
                    // here as well. Vanilla MapDistanceModel reports a finite
                    // path for "Kras the Baker @ (528.8, 632.9) → Gretysfjord"
                    // (graph entry Varcheg→Omor is land, both ends reachable
                    // by pathfind), so the standard probe never trips. But
                    // the caravan in practice does not move — five
                    // consecutive ticks at the identical position. Without
                    // this promotion, the LAND-edge branch silently lets
                    // vanilla walk on a caravan that has already proven it
                    // cannot walk.
                    if (progressStuck) stuck = true;

                    if (!stuck)
                    {
                        // Hop-by-hop traversal — caravans only.
                        //
                        // BK forces caravans through each graph node so the
                        // shipping system gets predictable boarding /
                        // disembark points and the trade-decision pipeline
                        // re-runs at every port. This makes sense for
                        // caravans because vanilla CaravanAi is happy with
                        // BK telling it the next hop.
                        //
                        // For LORD parties this is the wrong move. Lords
                        // have their own AI driving war goals, army linkage,
                        // diplomacy targets, etc. If BK overrides the
                        // lord's target to "next graph node toward Y" every
                        // hour, vanilla LordAi flips it back to its own
                        // chosen target on its next tick, BK overrides
                        // again, and the lord oscillates between two
                        // graph nodes ~20u apart forever (the "Sein's
                        // Party / Col's Party doing loops near Marunath"
                        // pattern). Vanilla land pathfinder already walks
                        // the lord across the graph just fine; BK should
                        // only intervene when the lord is actually stuck
                        // (the stuck branch below).
                        if (!party.IsCaravan)
                        {
                            // Lord-party land walk. Vanilla LordAi handles
                            // pure-land routes well. EXCEPT: when the
                            // planned graph path contains a SEA edge
                            // somewhere, vanilla often walks the lord to a
                            // coastal tile near the port (not into it),
                            // gets stuck, and BK rescues. Cheaper to
                            // redirect them to the closest port now and
                            // let AfterSettlementEntered's lord-auto-board
                            // pipeline take over (boards via SetSailAt-
                            // Position, sails to target).
                            bool pathHasSea = false;
                            try
                            {
                                for (int i = 0; i + 1 < path.Count; i++)
                                {
                                    if (graph.Adjacency.TryGetValue(path[i], out var es))
                                    {
                                        foreach (var e in es)
                                        {
                                            if (e.To == path[i + 1] && e.Kind == ShippingGraph.EdgeKind.Sea) { pathHasSea = true; break; }
                                        }
                                    }
                                    if (pathHasSea) break;
                                }
                            }
                            catch { pathHasSea = false; }

                            bool entryIsPort = false;
                            try { entryIsPort = entryNode.HasPort; } catch { }
                            bool targetIsPort = false;
                            try { targetIsPort = target.HasPort; } catch { }
                            bool navalCap = false;
                            try { navalCap = party.HasNavalNavigationCapability; } catch { }

                            if (pathHasSea && entryIsPort && targetIsPort && navalCap
                                && party.TargetSettlement != entryNode)
                            {
                                LogRedirect(party, $"lord-party sea route — REDIRECT to nearest port {entryNode.Name} (target {target.Name} requires sea)", target);
                                try { party.SetMoveGoToSettlement(entryNode, MobileParty.NavigationType.Default, false); } catch { }
                                try { party.Ai?.DisableForHours(2); } catch { }
                                return;
                            }

                            LogRedirect(party, $"lord-party land walk — letting vanilla AI handle (entry {entryNode.Name})", target);
                            return;
                        }
                        // CARAVAN LAND HOP-BY-HOP BACKWARD-BOUNCE GATE.
                        // Same bug class as the sea-edge boarding bounce:
                        // when path[1] == target the caravan is heading
                        // directly toward the next (and final) graph node.
                        // Forcing them to entryNode = path[0] = the node
                        // they JUST LEFT walks them backward, vanilla auto-
                        // enters them, AfterSettlementEntered sets target
                        // forward again, BK redirects backward, repeat.
                        // Jamair (Omor → Mazhadan Castle) port-bounces this
                        // way ~20×/session even though the trip is a single
                        // land hop. Skip when the caravan is already heading
                        // to the next graph hop — vanilla LandAi walks them
                        // there directly. Lords are handled by the branch
                        // above (returns before reaching this point).
                        if (path.Count >= 2 && path[1] == target)
                        {
                            LogRedirect(party, $"hop-by-hop SKIPPED: target {target.Name} is the next graph hop from {entryNode.Name} — vanilla walks directly", target);
                            return;
                        }
                        // Recently-left-settlement gate: same backward-bounce
                        // even when path[1] != target, if entryNode is the
                        // settlement the caravan exited a few hours ago.
                        if (lastPortLeft.TryGetValue(party, out var llpLand)
                            && llpLand.port == entryNode)
                        {
                            long nowHL = (long)CampaignTime.Now.ToHours;
                            if (nowHL - llpLand.hour < BoardingBounceWindowHours)
                            {
                                LogRedirect(party, $"hop-by-hop SKIPPED: caravan left {entryNode.Name} {nowHL - llpLand.hour}h ago — vanilla walks", target);
                                return;
                            }
                        }
                        if (party.TargetSettlement != entryNode
                            && (entryNode.Town != null || entryNode.IsVillage))
                        {
                            LogRedirect(party, $"hop-by-hop: REDIRECT to {entryNode.Name} (next graph node toward {target.Name})", target);
                            party.SetMoveGoToSettlement(entryNode, MobileParty.NavigationType.Default, false);
                            return;
                        }
                        LogRedirect(party, $"already heading to graph entry {entryNode.Name} — first edge {path[0]?.Name}→{path[1]?.Name} land, vanilla walk", target);
                        return;
                    }
                    // Stuck-on-coast rescue. Drop the party directly into the
                    // nearest non-hostile town via EnterSettlementAction. Why
                    // any town and not specifically a sea port:
                    //   * The off-mesh tile that traps coastal-stuck parties
                    //     is the navmesh problem; getting to a working tile
                    //     is what matters, not which kind of town. Vanilla's
                    //     own settlement-exit logic places exiting parties
                    //     on a guaranteed on-mesh tile.
                    //   * If the chosen town is a port AND the party's
                    //     intended destination requires shipping,
                    //     AfterSettlementEntered_Caravan auto-boards them
                    //     via SetTravel on entry. If it isn't a port,
                    //     vanilla CaravanAi's normal flow takes over.
                    //   * SetMoveGoToSettlement-based "stuck → redirect to
                    //     seaFallback" no-ops on a navmesh dead-end (vanilla
                    //     pathfind from the dead-end tile returns Infinity
                    //     to every settlement), and ping-pongs the redirect
                    //     between graphTarget and seaFallback forever. The
                    //     direct EnterSettlementAction sidesteps pathfind
                    //     entirely.
                    //   * Cost: a one-time settlement-entry tax. Cheaper
                    //     than the multi-hop sea voyage we used to issue.
                    if (progressStuck)
                    {
                        LogRedirect(party, $"PROGRESS STUCK at {partyPos2D.X:0.0},{partyPos2D.Y:0.0} for {progressStuckTicks} ticks — escaping via nearest town", target);
                    }

                    // PROBE for stay-around behaviors (mirror of LAND-edge).
                    // No intervention — capture state to find the root cause.
                    try
                    {
                        var defaultBehSea = party.DefaultBehavior;
                        var shortBehSea = party.ShortTermBehavior;
                        // Same vanilla-owned behavior set as the land-edge
                        // stayAround above. BesiegeSettlement / RaidSettlement /
                        // EngageParty / EscortParty added so the sea-edge
                        // rescue doesn't hijack a sieging or raiding party
                        // either.
                        bool stayAroundSea = defaultBehSea == TaleWorlds.CampaignSystem.Party.AiBehavior.PatrolAroundPoint
                                          || defaultBehSea == TaleWorlds.CampaignSystem.Party.AiBehavior.DefendSettlement
                                          || defaultBehSea == TaleWorlds.CampaignSystem.Party.AiBehavior.Hold
                                          || defaultBehSea == TaleWorlds.CampaignSystem.Party.AiBehavior.BesiegeSettlement
                                          || defaultBehSea == TaleWorlds.CampaignSystem.Party.AiBehavior.RaidSettlement
                                          || defaultBehSea == TaleWorlds.CampaignSystem.Party.AiBehavior.EngageParty
                                          || defaultBehSea == TaleWorlds.CampaignSystem.Party.AiBehavior.EscortParty
                                          || shortBehSea == TaleWorlds.CampaignSystem.Party.AiBehavior.PatrolAroundPoint
                                          || shortBehSea == TaleWorlds.CampaignSystem.Party.AiBehavior.DefendSettlement
                                          || shortBehSea == TaleWorlds.CampaignSystem.Party.AiBehavior.Hold
                                          || shortBehSea == TaleWorlds.CampaignSystem.Party.AiBehavior.BesiegeSettlement
                                          || shortBehSea == TaleWorlds.CampaignSystem.Party.AiBehavior.RaidSettlement
                                          || shortBehSea == TaleWorlds.CampaignSystem.Party.AiBehavior.EngageParty
                                          || shortBehSea == TaleWorlds.CampaignSystem.Party.AiBehavior.EscortParty;
                        if (stayAroundSea)
                        {
                            string sts = "(null)";
                            try { sts = party.ShortTermTargetSettlement?.Name?.ToString() ?? "(null)"; } catch { }
                            var mtp = party.MoveTargetPoint;
                            LogRedirect(party,
                                $"STUCK-PROBE sea-edge: DefaultBeh={defaultBehSea} ShortBeh={shortBehSea} ShortTermTargetSettlement={sts} IsTargetingPort={party.IsTargetingPort} DesiredNav={party.DesiredAiNavigationType} StartTransition={party.StartTransitionNextFrameToExitFromPort} IsTransitionInProgress={party.IsTransitionInProgress} AtSea={party.IsCurrentlyAtSea} HasLand={party.HasLandNavigationCapability} HasNaval={party.HasNavalNavigationCapability} MoveTargetIsOnLand={mtp.IsOnLand} entryNode={entryNode.Name}",
                                target);
                            progressTracker[party] = (party.GetPosition2D, 0);
                            return;
                        }
                    }
                    catch { }

                    Settlement rescueTown = ChooseRescueTown(party, partyPos2D, path);
                    if (rescueTown == null)
                    {
                        LogRedirect(party, $"stuck at coast (vanilla can't pathfind to {entryNode.Name}) but no rescue town available", target);
                        return;
                    }

                    // MCM gate (master rescue toggle).
                    if (!BannerKings.Settings.BannerKingsSettings.Instance.EnableShippingRescues)
                    {
                        LogRedirect(party, $"coast-stuck rescue to {rescueTown.Name} skipped — rescues disabled in MCM", target);
                        return;
                    }

                    LogRedirect(party, $"STUCK at coast — ENTER {rescueTown.Name} (vanilla pathfind to {entryNode.Name} returned Infinity)", target);
                    try { TaleWorlds.CampaignSystem.Actions.EnterSettlementAction.ApplyForParty(party, rescueTown); } catch { }
                    progressTracker[party] = (party.GetPosition2D, 0);
                    if (redirectCache.ContainsKey(party)) redirectCache.Remove(party);
                    lastRescueHour[party] = nowHour;
                    lastRescueTown[party] = rescueTown;
                    if (RecordRescueAndCheckOscillation(party))
                    {
                        LogRedirect(party, $"BK opt-out: rescued {OptOutRescueThreshold}+ times in {OscillationWindowHours}h — yielding to vanilla CaravanAi for {OptOutCooldownHours}h", target);
                    }
                    return;
                }

                // Already heading there → no-op.
                if (party.TargetSettlement == entryNode)
                {
                    LogRedirect(party, $"already heading to {entryNode.Name} (boarding port)", target);
                    return;
                }

                // Verify the caravan can ACTUALLY pathfind to the entryNode
                // before issuing the redirect. The party may be parked on a
                // coastal tile from which vanilla pathfinder returns Infinity
                // for every settlement (impassable terrain edge case). In
                // that case SetMoveGoToSettlement(entryNode) is a no-op,
                // the caravan sits there, and the 6h redirect cache locks
                // out re-evaluation. Symptom: "Sturgia caravan moves a
                // bit then re-sticks on the shore".
                bool entryReachable = false;
                try
                {
                    var distModel = TaleWorlds.CampaignSystem.Campaign.Current.Models.MapDistanceModel;
                    float dToEntry = distModel.GetDistance(party, entryNode, false, MobileParty.NavigationType.Default, out _);
                    entryReachable = !float.IsNaN(dToEntry)
                        && !float.IsInfinity(dToEntry)
                        && dToEntry >= 0f
                        && dToEntry <= 1e6f;
                }
                catch { entryReachable = false; }

                if (!entryReachable)
                {
                    // Same nearest-town rescue as the LAND-edge branch above.
                    // The party is on a navmesh tile from which it can't
                    // pathfind to the boarding entry node; SetMoveGoToSettlement
                    // is a no-op. Drop them directly into the nearest non-
                    // hostile town and let vanilla's exit place them on-mesh.
                    Settlement rescueTown = ChooseRescueTown(party, partyPos2D, path);
                    if (rescueTown == null)
                    {
                        LogRedirect(party, $"sea-edge redirect: entry {entryNode.Name} unreachable AND no rescue town available", target);
                        return;
                    }
                    // MCM gate (master rescue toggle).
                    if (!BannerKings.Settings.BannerKingsSettings.Instance.EnableShippingRescues)
                    {
                        LogRedirect(party, $"sea-edge unreachable rescue to {rescueTown.Name} skipped — rescues disabled in MCM", target);
                        return;
                    }
                    LogRedirect(party, $"sea-edge redirect: entry {entryNode.Name} unreachable — ENTER {rescueTown.Name}", target);
                    try { TaleWorlds.CampaignSystem.Actions.EnterSettlementAction.ApplyForParty(party, rescueTown); } catch { }
                    progressTracker[party] = (party.GetPosition2D, 0);
                    if (redirectCache.ContainsKey(party)) redirectCache.Remove(party);
                    lastRescueHour[party] = nowHour;
                    lastRescueTown[party] = rescueTown;
                    if (RecordRescueAndCheckOscillation(party))
                    {
                        LogRedirect(party, $"BK opt-out: rescued {OptOutRescueThreshold}+ times in {OscillationWindowHours}h — yielding to vanilla CaravanAi for {OptOutCooldownHours}h", target);
                    }
                    return;
                }

                // Lord parties without naval capability cannot board at the
                // boarding port. Redirecting them produces the loop the
                // user observed: lord enters port → can't board (auto-board
                // fails on HasNavalNavigationCapability check) → exits
                // toward original target → vanilla pathfinder fails on
                // water crossing → BK redirects back. Skip the redirect
                // entirely; the rescue branch below or vanilla's own
                // failure will resolve the situation.
                if (!party.IsCaravan)
                {
                    bool navalCap = false;
                    try { navalCap = party.HasNavalNavigationCapability; } catch { }
                    if (!navalCap)
                    {
                        LogRedirect(party, $"sea-edge redirect SKIPPED: lord has no naval capability (target {target.Name}, would-be entry {entryNode.Name})", target);
                        return;
                    }

                    // OSCILLATION GATE for lords: skip the boarding redirect
                    // when vanilla land-pathfinder can reach the target
                    // directly. The graph correctly says "port-to-port edge
                    // is sea" but adjacent-strait port pairs (Rhotae ↔
                    // Zeonica, Balgard ↔ Varnovapol) are also land-reachable.
                    // Pre-empting a naval-capable lord toward a different
                    // port produces the ~2u oscillation observed pre-fix:
                    // BK redirects, vanilla LordAi re-targets, BK redirects
                    // back, lord ping-pongs across the strait forever and
                    // never arrives. Caravans don't have this problem
                    // (vanilla CaravanAi follows BK's hop-by-hop without
                    // re-targeting on a military goal), so this gate is
                    // lord-only. Pairs with the equivalent gate in
                    // BKLordShippingBehavior.TickParty.
                    try
                    {
                        var distModelGate = TaleWorlds.CampaignSystem.Campaign.Current.Models.MapDistanceModel;
                        float dToTarget = distModelGate.GetDistance(party, target, false, MobileParty.NavigationType.Default, out _);
                        bool landReachable = !float.IsNaN(dToTarget)
                            && !float.IsInfinity(dToTarget)
                            && dToTarget >= 0f
                            && dToTarget < 1e6f;
                        if (landReachable)
                        {
                            LogRedirect(party, $"sea-edge redirect SKIPPED: target {target.Name} land-reachable from current tile (vanilla LordAi handles)", target);
                            return;
                        }
                    }
                    catch { /* probe failed — fall through and try the redirect */ }
                }

                // CARAVAN PORT-BOUNCE GATES.
                // Two distinct anti-bounce checks for caravans only. Lord
                // parties have separate gates above (no-naval-cap SKIP +
                // land-reachable SKIP); these don't apply to lords.
                //
                // Approach 2 — single-hop sea trip. When path[1] == target
                // the trip is one sea hop: entryNode → target. Vanilla
                // NavalDLC's NavigationTransition handles boarding when the
                // caravan reaches the coast; BK forcing the caravan back to
                // entryNode is redundant. The bounce is most visible when
                // entryNode is the port the caravan just left — they
                // re-enter, AfterSettlementEntered re-issues the same hop,
                // they leave again, this fires again, repeat. Garmi the
                // Baker (Hargard ↔ Thronderlag), Akadan the Butcher (Makeb
                // ↔ Chaikand) — both port-bounce with this exact signature.
                //
                // Approach 1 — recently-left port. For multi-hop trips the
                // path[1]!=target check doesn't fire, but the same bounce
                // happens whenever entryNode == "port the caravan just
                // left". Window-bounded so a caravan that genuinely needs
                // a re-redirect after a few hours of off-course drift still
                // gets one.
                if (party.IsCaravan && path.Count >= 2 && path[1] == target)
                {
                    LogRedirect(party, $"sea-edge redirect SKIPPED: target {target.Name} is the next graph hop from {entryNode.Name} — letting NavalDLC auto-board", target);
                    return;
                }
                if (party.IsCaravan
                    && lastPortLeft.TryGetValue(party, out var llp)
                    && llp.port == entryNode)
                {
                    long nowH = (long)CampaignTime.Now.ToHours;
                    if (nowH - llp.hour < BoardingBounceWindowHours)
                    {
                        LogRedirect(party, $"sea-edge redirect SKIPPED: caravan left {entryNode.Name} {nowH - llp.hour}h ago — letting NavalDLC auto-board", target);
                        return;
                    }
                }

                LogRedirect(party, $"REDIRECT to {entryNode.Name} (boarding for sea hop {path[0]?.Name}→{path[1]?.Name})", target);
                party.SetMoveGoToSettlement(entryNode, MobileParty.NavigationType.Default, false);
                // Disable vanilla AI for a couple of hours so vanilla CaravanAi
                // doesn't immediately re-target the original destination —
                // applies only to AI lord parties. For caravans, NavalDLC's
                // own AI conflicts with our 2h disable and the convoy ends
                // up permanently AI-disabled (Dalidos, etc.).
                if (!party.IsCaravan) try { party.Ai?.DisableForHours(2); } catch { }
            }
            catch (Exception ex)
            {
                try { LogRedirect(party, $"redirect threw {ex.GetType().Name}: {ex.Message}", null); } catch { }
                // Defensive: never throw out of an hourly tick. AI parties getting
                // weird targets from other mods should not crash BK.
            }
        }

        // Append-only redirect-decision log. Off by default — every call
        // does a synchronous File.AppendAllText (open / write / flush /
        // close) on the main thread, and the redirect runs per-party
        // per-hourly-tick. With ~5000 parties that becomes thousands of
        // disk writes per game day, which produced 25MB+ log files and
        // visible second-scale freezes in the wild. Toggle MCM →
        // Diagnostics → Log Shipping Redirect Decisions to enable when
        // debugging a specific stuck-caravan report.
        internal static void LogRedirect(MobileParty party, string note, Settlement target)
        {
            if (!Settings.BannerKingsSettings.Instance.LogShippingRedirect) return;
            try
            {
                if (party == null) return;
                string pos;
                try
                {
                    if (party.CurrentSettlement != null) pos = party.CurrentSettlement.Name?.ToString() ?? "?";
                    else { var p = party.GetPosition2D; pos = $"({p.X:0.0},{p.Y:0.0})"; }
                }
                catch { pos = "?"; }
                string targetName = target?.Name?.ToString() ?? "?";
                string line = $"{TaleWorlds.CampaignSystem.CampaignTime.Now}  {party.Name} @ {pos} → {targetName}: {note}";
                BannerKings.BannerKingsCheats.AppendDiagnosticLine("redirect.txt", line);
            }
            catch { /* never throw out of a logger */ }
        }
    }
}
