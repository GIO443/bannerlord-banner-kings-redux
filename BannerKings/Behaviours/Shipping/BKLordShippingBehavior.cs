using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace BannerKings.Behaviours.Shipping
{
    /// <summary>
    /// Lord-party shipping pipeline. Split out of BKShippingBehavior so the
    /// caravan and lord pipelines aren't sharing one entangled state machine.
    ///
    /// Responsibilities:
    ///   * Lord auto-board on port entry (fires when an AI lord enters a port
    ///     with a port-target reachable through the unified shipping graph).
    ///   * Lord stay-at-sea on intermediate port arrivals when the next graph
    ///     hop is itself a sea edge — saves the disembark-then-re-embark
    ///     round trip.
    ///   * Pre-emptive port redirect: when a lord's planned path includes a
    ///     sea edge but the FIRST edge is land, redirect the lord to the
    ///     closest port up-front so AfterSettlementEntered can auto-board
    ///     them. Without this, vanilla LordAi walks the lord toward the
    ///     coast tile next to the port and stalls.
    ///
    /// Caravan paths stay in BKShippingBehavior. The split is by *party kind*,
    /// not by *event* — both behaviors subscribe to the same campaign events
    /// but each early-returns for the other kind. AddBehavior order in Main
    /// determines tick order; both being additive listeners means neither
    /// blocks the other.
    /// </summary>
    public class BKLordShippingBehavior : BannerKings.Behaviours.BannerKingsBehavior
    {
        public override void RegisterEvents()
        {
            CampaignEvents.AfterSettlementEntered.AddNonSerializedListener(this, AfterSettlementEntered);
            CampaignEvents.HourlyTickPartyEvent.AddNonSerializedListener(this, TickParty);
        }

        public override void SyncData(IDataStore dataStore) { }

        // ------------------------------------------------------------------
        // Settlement entry: lord auto-board
        // ------------------------------------------------------------------

        private void AfterSettlementEntered(MobileParty party, Settlement settlement, Hero hero)
        {
            if (party == null || settlement == null) return;
            if (party == MobileParty.MainParty) return;
            if (party.IsCaravan) return;                // caravan path lives in BKShippingBehavior
            if (!party.IsLordParty) return;
            if (party.LeaderHero == null) return;
            if (party.LeaderHero.Clan == null || party.LeaderHero.Clan == Clan.PlayerClan) return;
            if (party.Army != null && party.Army.LeaderParty != party) return;
            // Hands-off for siege / map-event / player-in-army parties.
            // A sieging lord transiently entering a friendly port for
            // resupply (vanilla siege food refit) used to trigger
            // SetSailAtPosition + Naval move, boarding them away from
            // the siege camp.
            if (BKShippingBehavior.ShouldSkipForArmyOrSiege(party)) return;

            // From here on we'll log gate failures so we can see WHY a
            // naval-capable lord arriving at a port didn't auto-board.
            // Filter is "this is the kind of party we'd consider boarding"
            // — a clan AI lord arriving at a settlement. Anything below
            // is a meaningful diagnostic, not noise.
            if (!settlement.HasPort)
            {
                // Lord entered a non-port settlement. Auto-board never
                // applies; not interesting to log.
                return;
            }
            if (party.IsTransitionInProgress)
            {
                BKShippingBehavior.LogRedirect(party,
                    $"lord auto-board SKIPPED: IsTransitionInProgress at {settlement.Name}",
                    party.TargetSettlement);
                return;
            }
            if (party.IsCurrentlyAtSea)
            {
                BKShippingBehavior.LogRedirect(party,
                    $"lord auto-board SKIPPED: arrived at {settlement.Name} already AtSea (port-to-port stay-at-sea handled elsewhere)",
                    party.TargetSettlement);
                return;
            }
            if (!party.HasNavalNavigationCapability)
            {
                BKShippingBehavior.LogRedirect(party,
                    $"lord auto-board SKIPPED: no naval capability at {settlement.Name} (party has no ship)",
                    party.TargetSettlement);
                return;
            }

            var lordTarget = party.TargetSettlement;
            if (lordTarget == null)
            {
                BKShippingBehavior.LogRedirect(party,
                    $"lord auto-board SKIPPED: TargetSettlement is null at {settlement.Name}",
                    null);
                return;
            }
            if (lordTarget == settlement)
            {
                // Naval-patrol seamless integration with vanilla.
                //
                // Vanilla's embark mechanism for naval-patrolling lords
                // (data-confirmed via STUCK-PROBE 2026-05-03):
                //   1. Lord arrives at patrol-target port.
                //   2. CheckExitingSettlementParallel (MobileParty.cs:3838)
                //      keeps the lord in port if
                //        ShortTermTargetSettlement == CurrentSettlement.
                //   3. AI think tick (TickPartialHourlyAiEvent) eventually
                //      runs while still in port → SetPartyAiAction's
                //      PatrolAroundSettlement case sees isFromPort=true →
                //      sets StartTransitionNextFrameToExitFromPort=true.
                //   4. Next exit tick fires
                //      InitializeNavigationTransitionParallel(PortPosition,
                //      PortPosition) — boarding transition kicks off.
                //   5. FinishNavigationTransitionInternal flips AtSea=true.
                //
                // The chain hinges on step 2: ShortTermTargetSettlement
                // (= Ai.AiBehaviorPartyBase?.Settlement) must equal the
                // settlement the lord is in. For PatrolPartyComponent
                // naval-only parties the chain works because they're
                // AtSea=true on every entry (vanilla EnterSettlementAction
                // line 78: AtSea=!HasLand=true) and never touch this gate.
                //
                // For naval+land lord parties (Olek class) entering their
                // patrol-target port, AtSea=false on entry (HasLand=true),
                // and vanilla's MobilePartyAi.GetBehaviors PatrolAroundPoint
                // case (line 555) sets ShortTermBehavior=GoToPoint with a
                // null interactable. AiBehaviorPartyBase stays null →
                // ShortTermTargetSettlement is null → step 2's gate
                // doesn't fire → the lord exits before AI think can re-set
                // his behavior with isFromPort=true.
                //
                // Fix: set Ai.AiBehaviorInteractable = settlement.Party so
                // ShortTermTargetSettlement = settlement. CheckExitingSet-
                // tlementParallel's gate fires correctly, the lord stays
                // in port, and vanilla's normal AI chain takes over from
                // step 3. Also set RethinkAtNextHourlyTick so the AI think
                // fires as soon as possible (default cadence is every
                // 6 game-hours per Ai.HourCounter, too slow).
                //
                // This is NOT a bypass — every step from 3 onward is
                // vanilla's own machinery. We're just setting the state
                // that vanilla's PatrolAroundPoint case forgot to set
                // (the null interactable at MobilePartyAi.cs:555).
                // Trigger on the inconsistent state vanilla itself flags
                // at PartyBase.cs:634 — "IsTargetingPort != IsCurrentlyAtSea
                // && !IsTransitionInProgress". A naval+land lord arriving
                // at their port-target while in land mode is exactly that
                // case: they're heading to a port's water-side but they're
                // on land. Vanilla's resolution is to embark them via the
                // AI-think → StartTransitionNextFrameToExitFromPort →
                // CheckExitingSettlementParallel chain. We unblock that
                // chain by setting AiBehaviorInteractable so the lord
                // stays in port until AI think fires.
                //
                // Doesn't matter what DefaultBehavior is at this exact
                // moment — Olek arrives with DefaultBehavior=GoToSettlement
                // (he was walking to Omor), but his LONG-TERM intent is
                // PatrolAroundPoint (set by AiPatrollingBehavior, will be
                // re-applied on the next AI think). The intent shows up
                // in IsTargetingPort=true, which vanilla wouldn't have
                // set unless naval transit was the goal.
                //
                // PROBE first so we see exactly what state vanilla left
                // for us at entry — useful for tracking down any
                // remaining edge cases.
                try
                {
                    string itp = "?"; try { itp = party.IsTargetingPort.ToString(); } catch { }
                    string defaultBeh = "?"; try { defaultBeh = party.DefaultBehavior.ToString(); } catch { }
                    string shortBeh = "?"; try { shortBeh = party.ShortTermBehavior.ToString(); } catch { }
                    string sts = "(null)"; try { sts = party.ShortTermTargetSettlement?.Name?.ToString() ?? "(null)"; } catch { }
                    BKShippingBehavior.LogRedirect(party,
                        $"NAVAL-PATROL ENTRY PROBE: arrived at {settlement.Name} | DefaultBeh={defaultBeh} ShortBeh={shortBeh} ShortTermTargetSettlement={sts} IsTargetingPort={itp} StartTransition={party.StartTransitionNextFrameToExitFromPort} AtSea={party.IsCurrentlyAtSea} HasLand={party.HasLandNavigationCapability} HasNaval={party.HasNavalNavigationCapability}",
                        lordTarget);
                }
                catch { }
                // Force vanilla AI think to fire next tick (instead of
                // waiting for the default 6-game-hour cadence) so the
                // lord doesn't exit the port via path #2 (coast embark
                // at navmesh edge) before vanilla can set up naval
                // intent + StartTransitionNextFrameToExitFromPort=true
                // for port-side embark at PortPosition.
                //
                // RethinkAtNextHourlyTick is vanilla's own flag, used
                // by Army, EncounterManager, RaftStateChangeAction,
                // MobilePartyCreated. AiPartyThinkBehavior.PartyHourly-
                // AiTick (line 57) reads it and drops num to 1 (every
                // hour) instead of 6.
                //
                // Gated on naval-capable + on-land + entered a port —
                // narrow enough not to spam AI think for non-naval
                // visits, broad enough to catch every potential naval
                // embark candidate. The HasPort gate matters: naval-cap
                // lords whose patrol target is an inland castle (e.g.
                // Yathan @ Car Banseth in the user's own-save log) were
                // getting Rethink primed even though vanilla's
                // PatrolAroundSettlement won't initiate a port-side
                // embark from a non-port. Unnecessary AI-think pressure
                // on the post-mutation window where every freeze landed.
                try
                {
                    bool hasPort = false; try { hasPort = settlement.HasPort; } catch { }
                    if (party.HasNavalNavigationCapability
                        && !party.IsCurrentlyAtSea
                        && hasPort
                        && party.Ai != null
                        && !party.Ai.RethinkAtNextHourlyTick)
                    {
                        party.Ai.RethinkAtNextHourlyTick = true;
                        BKShippingBehavior.LogRedirect(party,
                            $"naval-port arrival: RethinkAtNextHourlyTick=true at {settlement.Name} (vanilla AI think will fire next tick → port-side embark via StartTransition)",
                            lordTarget);
                    }
                }
                catch { }
                BKShippingBehavior.LogRedirect(party,
                    $"lord auto-board SKIPPED: target IS current settlement {settlement.Name} (arrived at intended dest)",
                    lordTarget);
                return;
            }
            // If target is non-port (e.g. inland castle), we can still
            // sail to a port that's near the target on land and walk the
            // last leg from there. This is how cycling lords like
            // "Aelfeyja → Tirby Castle (inland across water)" used to
            // dead-end: BK rescued them to Hvalvik (port), refused to
            // auto-board because target.HasPort=false, lord exited and
            // walked toward Tirby, stuck at coast, rescued back to
            // Hvalvik, repeat. The fix is to find a port that bridges
            // the water — current port is sea-reachable, target is
            // land-reachable from that bridge port — and sail there.
            //
            // Skip when target IS land-reachable from here directly:
            // vanilla LordAi will walk them and we don't want to
            // unnecessarily ship.
            Settlement sailDest = lordTarget;
            if (!lordTarget.HasPort)
            {
                bool targetLandReachable = false;
                try
                {
                    var distModel = Campaign.Current.Models.MapDistanceModel;
                    float d = distModel.GetDistance(party, lordTarget, false, MobileParty.NavigationType.Default, out _);
                    targetLandReachable = !float.IsNaN(d) && !float.IsInfinity(d) && d >= 0f && d < 1e6f;
                }
                catch { }
                if (targetLandReachable)
                {
                    BKShippingBehavior.LogRedirect(party,
                        $"lord auto-board SKIPPED: target {lordTarget.Name} land-reachable from {settlement.Name} (vanilla walks)",
                        lordTarget);
                    return;
                }

                // Pick a bridge port: HasPort=true, connected by graph
                // to the current port, AND target is land-reachable from
                // it. Prefer the port closest to the target by Euclidean.
                var graph = Managers.Shipping.ShippingGraph.Instance;
                Settlement bridge = null;
                float bestDistToTarget = float.MaxValue;
                try
                {
                    var distModel = Campaign.Current.Models.MapDistanceModel;
                    var targetPos2D = lordTarget.GatePosition.ToVec2();
                    foreach (var node in graph.Adjacency.Keys)
                    {
                        if (node == null || node == settlement) continue;
                        bool hp = false; try { hp = node.HasPort; } catch { }
                        if (!hp) continue;
                        if (node.IsUnderSiege) continue;
                        if (node.MapFaction != null && node.MapFaction.IsAtWarWith(party.MapFaction)) continue;
                        if (!graph.AreConnected(settlement, node)) continue;

                        // Euclidean pre-filter — cheap before MapDistance probe.
                        float euclid = node.GatePosition.ToVec2().Distance(targetPos2D);
                        if (euclid >= bestDistToTarget) continue;

                        // Confirm target is land-reachable from this bridge.
                        // Settlement-to-settlement overload: (from, to, isFromPort, isTargetingPort, navCap).
                        float d = float.PositiveInfinity;
                        try { d = distModel.GetDistance(node, lordTarget, false, false, MobileParty.NavigationType.Default); }
                        catch { }
                        bool reachable = !float.IsNaN(d) && !float.IsInfinity(d) && d >= 0f && d < 1e6f;
                        if (!reachable) continue;

                        bridge = node;
                        bestDistToTarget = euclid;
                    }
                }
                catch { }

                if (bridge == null)
                {
                    BKShippingBehavior.LogRedirect(party,
                        $"lord auto-board SKIPPED: target {lordTarget.Name} has no port AND no bridge port found (graph or land-reach)",
                        lordTarget);
                    return;
                }
                sailDest = bridge;
            }
            else if (!Managers.Shipping.ShippingGraph.Instance.AreConnected(settlement, lordTarget))
            {
                BKShippingBehavior.LogRedirect(party,
                    $"lord auto-board SKIPPED: graph says {settlement.Name} ↛ {lordTarget.Name} not connected",
                    lordTarget);
                return;
            }

            try
            {
                // SetSailAtPosition is the supported "lord/army auto-board"
                // primitive (vanilla NavalTransitionCampaignBehavior.SetSail
                // calls it for the player). It flips IsCurrentlyAtSea and
                // places the party at the port's water-side PortPosition.
                // The follow-up SetMove uses NavigationType.Naval so the
                // engine's pathfinder constrains to water faces — Default
                // or All would let it route the boat over land tiles.
                // isTargetingThePort=true is REQUIRED so MoveTargetPoint =
                // sailDest.PortPosition (water). With false the target is
                // GatePosition (land), unreachable in Naval mode — boat
                // freezes at the source port.
                party.SetSailAtPosition(settlement.PortPosition);
                party.SetMoveGoToSettlement(sailDest, MobileParty.NavigationType.Naval, true);
                if (sailDest == lordTarget)
                {
                    BKShippingBehavior.LogRedirect(party,
                        $"lord auto-board: SetSailAtPosition at {settlement.Name} → sailing to {lordTarget.Name}",
                        lordTarget);
                }
                else
                {
                    BKShippingBehavior.LogRedirect(party,
                        $"lord auto-board: SetSailAtPosition at {settlement.Name} → sailing to bridge port {sailDest.Name} (target {lordTarget.Name} reachable on land from there)",
                        lordTarget);
                }
            }
            catch
            {
                // Never crash AI movement on a sail-transition edge case.
            }
        }

        // ------------------------------------------------------------------
        // Hourly tick: pre-emptive port redirect
        // ------------------------------------------------------------------

        private void TickParty(MobileParty party)
        {
            if (party == null || party == MobileParty.MainParty) return;
            if (party.IsCaravan) return;
            if (!party.IsLordParty) return;
            // ROOT-FIX GATE: skip mid-transition (see AfterSettlementEntered).
            if (party.IsTransitionInProgress) return;
            // Hands-off for parties in active siege / map event, or when
            // MainParty shares this army. The Hakarshus oscillation in
            // v1.8.10.0 was this redirect firing on an AI siege leader
            // whose land-reachability probe transiently flipped after
            // the player joined the army. See ShouldSkipForArmyOrSiege.
            if (BKShippingBehavior.ShouldSkipForArmyOrSiege(party)) return;
            // ROOT-FIX: lord without naval capability cannot board at a port
            // (AfterSettlementEntered's auto-board path also returns early
            // on this condition). Redirecting them to a boarding port
            // produces the loop: lord enters port → can't board → exits
            // toward original target → vanilla pathfinder fails on the
            // water crossing → BK redirects to port → repeat. Don't
            // pre-empt them; let vanilla LordAi handle pure-land routes
            // and accept they can't reach water-only targets.
            bool navalCap = false;
            try { navalCap = party.HasNavalNavigationCapability; } catch { navalCap = false; }
            if (!navalCap) return;
            if (party.LeaderHero == null || party.LeaderHero.Clan == null
                || party.LeaderHero.Clan == Clan.PlayerClan) return;
            if (party.Army != null && party.Army.LeaderParty != party) return;
            if (party.IsCurrentlyAtSea) return;
            if (party.CurrentSettlement != null) return;

            var target = party.TargetSettlement;
            if (target == null || target == party.CurrentSettlement) return;
            if (!target.HasPort) return;            // pre-emptive port redirect only useful for port targets

            // If the lord is already heading to a port, don't re-target.
            if (party.TargetSettlement != null && party.TargetSettlement.HasPort
                && Managers.Shipping.ShippingGraph.Instance.Adjacency.ContainsKey(party.TargetSettlement)
                && party.TargetSettlement == target) {
                // Heading directly to target port — no redirect needed.
                // The auto-board branch fires when they arrive.
                // Skip if first graph edge from current location to target
                // is land-only (no sea hop needed for this trip).
            }

            try
            {
                var graph = Managers.Shipping.ShippingGraph.Instance;
                if (graph == null) return;
                if (!graph.Adjacency.ContainsKey(target)) return;

                // OSCILLATION GATE: skip pre-empt when vanilla land-pathfinder
                // can reach the target directly. The graph says "sea edge
                // needed" because port-to-port edges are sea by construction,
                // but adjacent-strait port pairs (Rhotae ↔ Zeonica, Lageta ↔
                // nearby coast, Balgard ↔ Varnovapol) are also land-reachable
                // around the strait. If we redirect a naval-capable lord
                // toward a different port instead of letting them walk to the
                // target directly, vanilla LordAi re-targets back on the next
                // tick, BK pre-empts again, lord oscillates ~2u between two
                // ports forever (the "Sein's Party near Marunath" pattern,
                // re-introduced here). Probing the vanilla pathfinder is
                // authoritative on land-reachability and matches the same
                // gate the caravan rescue branches use.
                try
                {
                    var distModelGate = Campaign.Current.Models.MapDistanceModel;
                    float dToTarget = distModelGate.GetDistance(party, target, false, MobileParty.NavigationType.Default, out _);
                    bool landReachable = !float.IsNaN(dToTarget)
                        && !float.IsInfinity(dToTarget)
                        && dToTarget >= 0f
                        && dToTarget < 1e6f;
                    if (landReachable) return;
                }
                catch { /* if probe fails, fall through and try the redirect */ }

                // Pick a graph entry node reachable by land from the party's
                // current tile. K=6 closest by Euclidean, then probe vanilla's
                // MapDistanceModel.GetDistance until one returns finite.
                var partyPos2D = party.GetPosition2D;
                var candidates = new System.Collections.Generic.List<(Settlement node, float dist)>(8);
                foreach (var node in graph.Adjacency.Keys)
                {
                    if (node == null || node == target) continue;
                    if (node.IsUnderSiege) continue;
                    if (node.MapFaction != null && node.MapFaction.IsAtWarWith(party.MapFaction)) continue;
                    if (!node.HasPort) continue;        // lord pre-emptive needs a boarding port
                    float d = partyPos2D.Distance(node.GatePosition.ToVec2());
                    candidates.Add((node, d));
                }
                if (candidates.Count == 0) return;
                candidates.Sort((a, b) => a.dist.CompareTo(b.dist));

                Settlement entryPort = null;
                try
                {
                    var distModel = Campaign.Current.Models.MapDistanceModel;
                    int n = System.Math.Min(6, candidates.Count);
                    for (int i = 0; i < n; i++)
                    {
                        float d = float.PositiveInfinity;
                        try { d = distModel.GetDistance(party, candidates[i].node, false, MobileParty.NavigationType.Default, out _); }
                        catch { d = float.PositiveInfinity; }
                        if (!float.IsNaN(d) && !float.IsInfinity(d) && d >= 0f && d < 1e6f)
                        {
                            entryPort = candidates[i].node;
                            break;
                        }
                    }
                }
                catch { /* model unavailable */ }
                if (entryPort == null) return;
                if (entryPort == party.TargetSettlement) return; // already heading to a port

                // Confirm the planned path actually contains a sea edge —
                // pure-land routes don't benefit from boarding.
                bool pathHasSea = false;
                try
                {
                    var path = graph.GetShortestPath(entryPort, target);
                    if (path != null && path.Count >= 2)
                    {
                        for (int i = 0; i + 1 < path.Count; i++)
                        {
                            if (graph.Adjacency.TryGetValue(path[i], out var es))
                            {
                                foreach (var e in es)
                                {
                                    if (e.To == path[i + 1]
                                        && e.Kind == Managers.Shipping.ShippingGraph.EdgeKind.Sea)
                                    { pathHasSea = true; break; }
                                }
                            }
                            if (pathHasSea) break;
                        }
                    }
                }
                catch { pathHasSea = false; }
                if (!pathHasSea) return;

                // Redirect lord to the boarding port. Disable LordAi for 2h
                // so vanilla doesn't immediately re-target before the lord
                // arrives at the port.
                party.SetMoveGoToSettlement(entryPort, MobileParty.NavigationType.Default, false);
                try { party.Ai?.DisableForHours(2); } catch { }
                BKShippingBehavior.LogRedirect(party,
                    $"lord pre-emptive port redirect: REDIRECT to {entryPort.Name} (target {target.Name} requires sea hop)",
                    target);
            }
            catch
            {
                // Defensive: never throw out of an hourly tick.
            }
        }
    }
}
