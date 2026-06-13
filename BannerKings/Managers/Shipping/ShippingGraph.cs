using System.Collections.Generic;
using System.Linq;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace BannerKings.Managers.Shipping
{
    /// <summary>
    /// Explicit graph view of the BK shipping/road network.
    ///
    /// Single unified graph with two edge classes:
    ///   * Land — KNN K=3 ≤75u between fiefs (towns + castles), road-style
    ///     short-hop connectivity.
    ///   * Sea — opportunistic shortcuts between ports (settlements with
    ///     HasPort==true). A sea edge between two ports exists iff the
    ///     direct sea hop is shorter than the best land path between them
    ///     — so ports on the same coast with a road don't get a sea edge,
    ///     ports on different landmasses always get one (land = ∞), and
    ///     ports across a peninsula or bay get one when the sea route
    ///     actually saves distance. Sea edges are KNN-bounded (K=4) so a
    ///     port doesn't get a long-haul direct edge to every distant port
    ///     on the same sea body.
    ///
    /// Edge classification stays load-bearing for the boarding mechanic:
    /// crossing a Sea edge means the caravan must enter a port and call
    /// SetTravel. The graph just decides *whether* a sea hop is worth it.
    ///
    /// Built lazily on first access. Rebuild via <see cref="Invalidate"/>.
    /// </summary>
    public class ShippingGraph
    {
        private static ShippingGraph _instance;

        public static ShippingGraph Instance => _instance ??= Build();

        public static void Invalidate() => _instance = null;

        // KILL SWITCH (v1.9.22.1). The shipping graph is DISABLED.
        //
        // A reproducible late-campaign stall (BK_freeze.txt: GC frozen, last
        // marker "ShippingGraph.Build", heap climbing then flatlined) traced to
        // the shipping subsystem — either Build()'s per-port-pair naval
        // GetDistance probes (the same native pathfind that hangs elsewhere in
        // this saga) or a consumer's Dijkstra/adaptive-path traversal over the
        // built graph right after Build() returns. Per user directive, nuke it
        // and default to vanilla pathing: Build() returns an EMPTY graph, so
        // every consumer's Adjacency / AreConnected / GetShortestPath lookup
        // misses and falls through to its vanilla/land branch — BK stops
        // overriding caravan and lord sea routing, and the engine's own
        // pathfinder decides every move. No sea edges = no boarding redirects =
        // no graph traversal = no build-time naval probes.
        //
        // This is the experiment: if the stall clears with the graph off, the
        // shipping graph was the cause. Flip back to false to re-enable.
        public static bool Disabled = true;

        public enum EdgeKind
        {
            Sea,
            Land
        }

        public struct Edge
        {
            public Settlement To;
            public float Distance;
            public EdgeKind Kind;
        }

        /// <summary>settlement → outgoing edges (sea or land)</summary>
        public Dictionary<Settlement, List<Edge>> Adjacency { get; private set; } = new();

        /// <summary>All settlement nodes in the graph.</summary>
        public IEnumerable<Settlement> Ports => Adjacency.Keys;

        public int PortCount => Adjacency.Count;

        public int EdgeCount => Adjacency.Values.Sum(list => list.Count) / 2;

        public int SeaEdgeCount => Adjacency.Values.Sum(list => list.Count(e => e.Kind == EdgeKind.Sea)) / 2;

        public int LandEdgeCount => Adjacency.Values.Sum(list => list.Count(e => e.Kind == EdgeKind.Land)) / 2;

        // ------------------------------------------------------------------
        // Adaptive risk weights
        //
        // Edges store raw map distance, but real shipping cost varies with
        // local conditions: a sieged port closes business; a port whose owner
        // is at war with the cargo's faction won't dock; bandits crawling
        // the coast push insurance fees up. The methods below compute a
        // multiplier on top of the raw distance, recomputed at query time
        // (the campaign world changes faster than we want to bake into
        // edges). Use GetAdaptivePath / GetAdaptiveDistance for routing
        // decisions; the plain GetShortestPath is still correct for static
        // topology diagnostics.
        // ------------------------------------------------------------------

        // Hideout proximity is a cheap-but-nontrivial calculation; cache it
        // per port and refresh only when the campaign clock crosses a day.
        // Hideouts move at most once per cleanup cycle so 24h staleness is
        // an acceptable approximation.
        private readonly Dictionary<Settlement, int> _nearbyHideoutCache = new Dictionary<Settlement, int>();
        private CampaignTime _hideoutCacheStamp = CampaignTime.Zero;
        private const float HideoutProximityRadius = 60f;

        /// <summary>
        /// Multiplier on top of the raw map-distance edge weight, given
        /// current campaign state. Range is roughly [1.0, 4.0]; 1.0 means
        /// peaceful coastal route, ~3+ means siege + war + bandit hotspot.
        /// Returns float.PositiveInfinity if either endpoint is *currently*
        /// inaccessible to <paramref name="perspective"/> (port owner at war).
        /// </summary>
        public float GetEdgeRiskMultiplier(Settlement a, Settlement b, IFaction perspective = null)
        {
            if (a == null || b == null) return 1f;

            // Hard block: hostile port owner won't let the perspective faction
            // dock or load cargo. Edge becomes unusable for the routing pass.
            if (perspective != null)
            {
                if (a.MapFaction != null && a.MapFaction != perspective && a.MapFaction.IsAtWarWith(perspective))
                    return float.PositiveInfinity;
                if (b.MapFaction != null && b.MapFaction != perspective && b.MapFaction.IsAtWarWith(perspective))
                    return float.PositiveInfinity;
            }

            float mult = 1f;

            // Sieges: port can still nominally accept ships but the harbour
            // is contested — heavy danger pay, AI strongly avoids.
            if (a.Town != null && a.IsUnderSiege) mult += 0.6f;
            if (b.Town != null && b.IsUnderSiege) mult += 0.6f;

            // Bandit pressure near either port. Each nearby active hideout
            // adds +5%, capped at +50% combined so a hotspot doesn't fully
            // dominate the routing.
            int hideouts = GetNearbyHideoutCount(a) + GetNearbyHideoutCount(b);
            mult += System.Math.Min(hideouts * 0.05f, 0.5f);

            // Soft penalty for *neutral but tense* — the perspective is in
            // a kingdom and the port owner is in a different faction (not
            // at war, but no privileged access either). Small bump so the
            // graph prefers same-faction routes when available.
            if (perspective != null)
            {
                if (a.MapFaction != null && a.MapFaction != perspective && !a.MapFaction.IsAtWarWith(perspective)) mult += 0.05f;
                if (b.MapFaction != null && b.MapFaction != perspective && !b.MapFaction.IsAtWarWith(perspective)) mult += 0.05f;
            }

            return mult;
        }

        /// <summary>
        /// Distance × risk multiplier. The weight Dijkstra optimises over.
        /// </summary>
        public float GetEdgeWeight(Edge edge, Settlement from, IFaction perspective = null)
        {
            float mult = GetEdgeRiskMultiplier(from, edge.To, perspective);
            if (float.IsPositiveInfinity(mult)) return float.PositiveInfinity;
            return edge.Distance * mult;
        }

        private int GetNearbyHideoutCount(Settlement port)
        {
            if (port == null) return 0;
            // Refresh cache once per day. CampaignTime.Now throws when the
            // campaign isn't running yet (main menu, character creation),
            // so fall through to fresh compute in that case.
            CampaignTime now = CampaignTime.Zero;
            try { now = CampaignTime.Now; } catch { /* before campaign fully ready */ }
            if ((now - _hideoutCacheStamp).ToDays > 1f)
            {
                _nearbyHideoutCache.Clear();
                _hideoutCacheStamp = now;
            }
            if (_nearbyHideoutCache.TryGetValue(port, out int cached)) return cached;

            int count = 0;
            try
            {
                foreach (var s in Settlement.All)
                {
                    if (s == null || !s.IsHideout) continue;
                    if (s.GatePosition.Distance(port.GatePosition) <= HideoutProximityRadius) count++;
                }
            }
            catch { count = 0; }
            _nearbyHideoutCache[port] = count;
            return count;
        }

        /// <summary>Drop the per-port hideout cache; call when hideouts shift.</summary>
        public void InvalidateRiskCache()
        {
            _nearbyHideoutCache.Clear();
            _hideoutCacheStamp = CampaignTime.Zero;
        }

        // ------------------------------------------------------------------
        // Build
        // ------------------------------------------------------------------

        // Number of nearest neighbors connected per settlement for the land
        // graph. K=5 keeps node degree (after symmetrization) modest while
        // providing enough cross-region connectivity to avoid graph
        // fragmentation (the "no graph path from Hubyar to Sahel Castle"
        // class of failures: K=3 was leaving Aserai inland-coastal pairs
        // disconnected through the desert/coast belt).
        private const int LandKnnK = 5;

        // Maximum euclidean distance for a land edge. Pairs farther apart
        // are dropped — keeps islands from being bridged to the mainland by
        // straight-line proximity. Bumped from 75u to 90u along with the K
        // increase to bridge a few specific Aserai/Empire-East gaps where
        // settlement spacing is wider than the average.
        private const float LandMaxEdgeDistance = 90f;

        // KNN bound for sea edges. K=6 (up from K=4) so ports across
        // distinct sea bodies (Aserai south coast vs Battanian/Vlandian
        // west coast vs Nord channel) keep multi-hop chains intact through
        // bridge ports. With ~18 ports on the Calradia + Nord map this
        // gives generous cross-region connectivity without exploding edge
        // count.
        private const int SeaKnnK = 6;

        // Hysteresis on the sea-shortcut comparison. A sea edge is added
        // only if direct sea hop + this margin < shortest land path. 0
        // means "any savings count"; raising it above 0 biases routing
        // toward walking when sea-vs-land is close. Tunable post-hoc; 0
        // is the right starting point because the boarding mechanic
        // already adds its own implicit cost (caravan must enter the port).
        private const float SeaShortcutPenalty = 0f;

        private static ShippingGraph Build()
        {
            // Freeze-watchdog marker: Build() is an O(V²) all-pairs sweep that
            // is rebuilt lazily after Invalidate() (called on every settlement
            // ownership flip). On a large late-game map it is the heaviest
            // single computation in the shipping subsystem and a prime suspect
            // for the rare multi-minute "late-campaign" stall. If the campaign
            // thread is stuck here, the watchdog names "ShippingGraph.Build".
            BannerKings.Utils.FreezeWatchdog.Enter("ShippingGraph.Build", null);
            try {
            var g = new ShippingGraph();

            // NUKED — return the empty graph immediately. Every consumer's
            // Adjacency/AreConnected/GetShortestPath lookup then misses and
            // falls through to vanilla pathing. See the Disabled flag comment.
            if (Disabled) return g;

            // ---- Land edges: KNN K=3 ≤75u over fiefs (towns + castles) ----
            // Land edges are the road network. Villages aren't valid land
            // nodes: they have no Town and break BK's caravan-fee bookkeeping
            // on arrival. Caravans walking through villages use vanilla
            // pathfinding between graph hops — the graph models fief-level
            // routing intent, not road-level traversal.
            //
            // KNN with a 75u cap: islands self-isolate beyond the cap, so
            // disconnected landmasses correctly have no land path between
            // them (which the sea-shortcut test below relies on).
            var landNodes = new List<Settlement>();
            try
            {
                foreach (var s in Settlement.All)
                {
                    if (s == null) continue;
                    if (s.IsHideout) continue;
                    if (!(s.IsTown || s.IsCastle)) continue;
                    landNodes.Add(s);
                }
            }
            catch { /* very early in load — Settlement.All not ready */ }

            // KNN land edges, no build-time validation. An earlier
            // version probed every candidate pair via
            // MapDistanceModel.GetDistance(NavigationType.Default) and
            // dropped Infinity-result edges, intending to rule out
            // straight-line edges across bays / peninsulas. That probe
            // turned out to drop legitimate cross-region land edges too
            // (e.g. Hubyar→Razih, Revyl→Varnovapol), shattering the graph
            // into many disconnected components and producing the "no
            // graph path from X to Y" log spam at runtime.
            //
            // Without validation the graph occasionally has a cross-bay
            // edge a caravan can't actually walk, but the v1.6.10 nearest-
            // town rescue catches that case at runtime: progressStuck
            // triggers within 3 hourly ticks, the caravan is dropped into
            // its nearest non-hostile town via EnterSettlementAction, and
            // vanilla's exit places them on a known-walkable tile.
            foreach (var a in landNodes)
            {
                try
                {
                    if (!g.Adjacency.ContainsKey(a)) g.Adjacency[a] = new List<Edge>();

                    var nearest = new List<(Settlement Node, float Dist)>(landNodes.Count);
                    foreach (var b in landNodes)
                    {
                        if (b == a) continue;
                        try
                        {
                            float d = a.GatePosition.Distance(b.GatePosition);
                            if (d > LandMaxEdgeDistance) continue;
                            nearest.Add((b, d));
                        }
                        catch { /* skip this candidate */ }
                    }
                    nearest.Sort((x, y) => x.Dist.CompareTo(y.Dist));

                    int added = 0;
                    for (int i = 0; i < nearest.Count && added < LandKnnK; i++)
                    {
                        var (b, euclid) = nearest[i];
                        if (euclid <= 0f) continue;
                        g.Adjacency[a].Add(new Edge
                        {
                            To = b,
                            Distance = euclid,
                            Kind = EdgeKind.Land
                        });
                        added++;
                    }
                }
                catch { /* skip this source — partial graph still useful */ }
            }

            // Symmetrize land edges. KNN is inherently directed: a→b is added
            // when b sits in a's K-nearest list, but b→a only exists if a sits
            // in b's K-nearest list. At cluster boundaries (small dense
            // cluster next to a sparse one), edges into the dense cluster
            // disappear because the dense-side nodes prefer their own
            // neighbors. The result is hundreds of one-way connections that
            // silently break Dijkstra. Mirror every edge so the graph treats
            // land roads as undirected, which matches the gameplay reality.
            SymmetrizeEdges(g);

            // Anti-orphan pass for land nodes. A small handful of inland
            // castles (notably Ykerslund Castle and Fimbulgard Castle) sit
            // far enough from any other fief that the LandMaxEdgeDistance
            // cap drops every candidate edge — they end up as zero-edge
            // single-node components. Sea bridging can't help (they're not
            // ports). Give each orphaned land node one bidirectional edge
            // to its absolute-nearest fief regardless of distance, so
            // caravans always have a routable handle on it. Capped distance
            // not enforced here because the alternative is a dead castle
            // that no caravan can ever target.
            foreach (var a in landNodes)
            {
                if (g.Adjacency.TryGetValue(a, out var existing) && existing.Count > 0) continue;
                Settlement nearest = null;
                float bestD = float.PositiveInfinity;
                foreach (var b in landNodes)
                {
                    if (b == a) continue;
                    try
                    {
                        float d = a.GatePosition.Distance(b.GatePosition);
                        if (d < bestD) { bestD = d; nearest = b; }
                    }
                    catch { }
                }
                if (nearest == null) continue;
                if (!g.Adjacency.ContainsKey(a)) g.Adjacency[a] = new List<Edge>();
                if (!g.Adjacency.ContainsKey(nearest)) g.Adjacency[nearest] = new List<Edge>();
                g.Adjacency[a].Add(new Edge { To = nearest, Distance = bestD, Kind = EdgeKind.Land });
                g.Adjacency[nearest].Add(new Edge { To = a, Distance = bestD, Kind = EdgeKind.Land });
            }

            // ---- Identify ports: HasPort==true ----
            // Vanilla + War Sails set HasPort on coastal towns and the
            // handful of village ports they ship that XML for. We trust
            // that flag completely — it's the in-engine "this settlement
            // has a working harbour scene" signal, which is exactly what
            // we want for the boarding mechanic. No manual lane curation,
            // no XML grepping, no terrain probes.
            var ports = new List<Settlement>();
            try
            {
                foreach (var s in Settlement.All)
                {
                    if (s == null) continue;
                    if (s.IsHideout) continue;
                    bool hasPort = false;
                    try { hasPort = s.HasPort; } catch { hasPort = false; }
                    if (!hasPort) continue;
                    ports.Add(s);
                    // A village port wouldn't be in the land graph; give it
                    // an empty adjacency entry so sea edges can be appended
                    // below (and so the graph reports it as a node).
                    if (!g.Adjacency.ContainsKey(s)) g.Adjacency[s] = new List<Edge>();
                }
            }
            catch { /* defensive */ }

            // ---- Per-port land-only Dijkstra ----
            // The adjacency dict at this point holds only land edges, so
            // running Dijkstra on it gives us the shortest land path from
            // each port to every other port. We materialize a per-port
            // distance map so the sea-edge comparison below is O(1) per
            // candidate. Cost: O(P) Dijkstras × O(N log N) ≈ trivial.
            var landDist = new Dictionary<Settlement, Dictionary<Settlement, float>>(ports.Count);
            foreach (var p in ports)
            {
                try { landDist[p] = DijkstraLandOnly(g, p); }
                catch { landDist[p] = new Dictionary<Settlement, float>(); }
            }

            // ---- Sea edges: opportunistic shortcuts ----
            // For each port, walk the K nearest other ports by euclidean
            // distance (a coarse KNN gate to keep build cost bounded), then
            // ask vanilla's MapDistanceModel for the *naval* route distance
            // between them. Two important wins from using the vanilla naval
            // pathfinder rather than raw euclidean for the edge weight:
            //
            //   1. Edges whose straight line clips a peninsula get a real
            //      route distance that reflects the ship going around. The
            //      caravan's boat sprite follows the engine's naval AI on
            //      the same path, so no "boat sailing over land" visual
            //      (the "Convoy of Temyr crossed a jutted land section"
            //      report).
            //   2. Pairs with no naval connection at all (different sea
            //      bodies, blocked archipelago) return Infinity from the
            //      vanilla probe and we skip the edge entirely.
            //
            // Compare the naval-route distance to the land-only Dijkstra
            // result to decide whether the sea hop is actually a shortcut.
            // Same-coast ports with a short road typically lose the
            // comparison (no edge); cross-bay or cross-landmass pairs win.
            //
            // Cost: ~K=4 × P=25 = ~100 GetDistance calls one-shot at first
            // graph build. Negligible compared to the LandKnnK validation
            // pass already running for ~400 land edges.
            var distModelSea = TaleWorlds.CampaignSystem.Campaign.Current?.Models?.MapDistanceModel;

            foreach (var a in ports)
            {
                var nearest = new List<(Settlement Node, float Dist)>(ports.Count);
                foreach (var b in ports)
                {
                    if (b == a) continue;
                    try
                    {
                        float d = a.GatePosition.Distance(b.GatePosition);
                        nearest.Add((b, d));
                    }
                    catch { }
                }
                nearest.Sort((x, y) => x.Dist.CompareTo(y.Dist));

                int added = 0;
                for (int i = 0; i < nearest.Count && added < SeaKnnK; i++)
                {
                    var (b, euclidD) = nearest[i];
                    if (euclidD <= 0f) continue;

                    // Vanilla "best of land+naval" probe. NavigationType.All
                    // returns the engine's best route, which uses naval
                    // segments where they shorten the trip. Earlier code
                    // used NavigationType.Naval directly, but in this
                    // campaign state the Naval-only probe returns Infinity
                    // for every settlement-to-settlement query (graph built
                    // with 0 sea edges). NavigationType.All matches what
                    // every other BK MapDistanceModel call site uses and
                    // produces finite distances reliably.
                    float seaD = float.PositiveInfinity;
                    if (distModelSea != null)
                    {
                        try { seaD = distModelSea.GetDistance(a, b, false, false, MobileParty.NavigationType.All); }
                        catch { seaD = float.PositiveInfinity; }
                    }
                    if (float.IsNaN(seaD) || float.IsInfinity(seaD) || seaD < 0f || seaD > 1e6f)
                    {
                        continue;
                    }

                    float landD = float.PositiveInfinity;
                    if (landDist.TryGetValue(a, out var distMap)
                        && distMap.TryGetValue(b, out var ld))
                    {
                        landD = ld;
                    }
                    if (seaD + SeaShortcutPenalty >= landD) continue;

                    g.Adjacency[a].Add(new Edge
                    {
                        To = b,
                        Distance = seaD,
                        Kind = EdgeKind.Sea
                    });
                    added++;
                }
            }

            // Symmetrize sea edges for the same reason as land edges. K=6
            // KNN doesn't guarantee mutual membership — Razih may pick
            // Hubyar in its 6 nearest while Hubyar's 6 nearest are all on
            // the Aserai south shelf and exclude Razih. One-way sea edges
            // produce path failures that look like component separation.
            SymmetrizeEdges(g);

            // Bridge disconnected components via the shortest naval edge.
            // Even after KNN + symmetrization, the graph may have multiple
            // components — a Sturgia-south port cluster and an Aserai-south
            // port cluster, each internally connected, with no KNN-eligible
            // edge between them. The vanilla naval pathfinder routinely
            // returns finite distances between them, so the connection is
            // physically possible; the KNN gate just doesn't include the
            // pair. For each pair of components, find the shortest finite
            // naval edge between any two ports (one in each), and add it.
            // Repeats until the graph is single-component or the bridge
            // budget is exhausted.
            if (distModelSea != null)
            {
                const int MaxBridgePasses = 12;
                for (int pass = 0; pass < MaxBridgePasses; pass++)
                {
                    var components = g.GetConnectedComponents();
                    if (components.Count <= 1) break;
                    components.Sort((a, b) => b.Count.CompareTo(a.Count));
                    var anchor = components[0];

                    Settlement bestA = null, bestB = null;
                    float bestD = float.PositiveInfinity;
                    foreach (var a in anchor)
                    {
                        bool aIsPort = false; try { aIsPort = a.HasPort; } catch { }
                        if (!aIsPort) continue;
                        for (int ci = 1; ci < components.Count; ci++)
                        {
                            foreach (var b in components[ci])
                            {
                                bool bIsPort = false; try { bIsPort = b.HasPort; } catch { }
                                if (!bIsPort) continue;
                                float d = float.PositiveInfinity;
                                // NavigationType.All — see comment on the
                                // KNN sea probe above. Cross-component port
                                // pairs have land distance == Infinity by
                                // construction, so any finite All distance
                                // proves a naval segment exists.
                                try { d = distModelSea.GetDistance(a, b, false, false, MobileParty.NavigationType.All); }
                                catch { d = float.PositiveInfinity; }
                                if (float.IsNaN(d) || float.IsInfinity(d) || d < 0f || d > 1e6f) continue;
                                if (d < bestD) { bestD = d; bestA = a; bestB = b; }
                            }
                        }
                    }

                    if (bestA == null || bestB == null) break;
                    if (!g.Adjacency.ContainsKey(bestA)) g.Adjacency[bestA] = new List<Edge>();
                    if (!g.Adjacency.ContainsKey(bestB)) g.Adjacency[bestB] = new List<Edge>();
                    g.Adjacency[bestA].Add(new Edge { To = bestB, Distance = bestD, Kind = EdgeKind.Sea });
                    g.Adjacency[bestB].Add(new Edge { To = bestA, Distance = bestD, Kind = EdgeKind.Sea });
                }
            }

            return g;
            } finally { BannerKings.Utils.FreezeWatchdog.Exit(); }
        }

        // Adds the reverse of every directed edge in the adjacency map. Idempotent
        // — skips when the reverse already exists with matching kind.
        private static void SymmetrizeEdges(ShippingGraph g)
        {
            // Snapshot first; we mutate values during iteration.
            var snapshot = new List<(Settlement From, Edge Edge)>();
            foreach (var kv in g.Adjacency)
            {
                foreach (var e in kv.Value) snapshot.Add((kv.Key, e));
            }
            foreach (var (from, edge) in snapshot)
            {
                if (edge.To == null) continue;
                if (!g.Adjacency.TryGetValue(edge.To, out var revList))
                {
                    revList = new List<Edge>();
                    g.Adjacency[edge.To] = revList;
                }
                bool already = false;
                foreach (var r in revList)
                {
                    if (r.To == from && r.Kind == edge.Kind) { already = true; break; }
                }
                if (already) continue;
                revList.Add(new Edge
                {
                    To = from,
                    Distance = edge.Distance,
                    Kind = edge.Kind
                });
            }
        }


        // Dijkstra on the land-only subgraph induced by EdgeKind.Land. Used
        // during Build() to evaluate "could the caravan walk this instead"
        // before adding a sea shortcut. Operates against a partially-built
        // graph (land edges only — sea edges haven't been added yet at the
        // call site), so filtering by EdgeKind is defensive rather than
        // strictly required. Returns a distance map; unreachable entries
        // are float.PositiveInfinity.
        private static Dictionary<Settlement, float> DijkstraLandOnly(ShippingGraph g, Settlement source)
        {
            var dist = new Dictionary<Settlement, float>(g.Adjacency.Count);
            foreach (var k in g.Adjacency.Keys) dist[k] = float.PositiveInfinity;
            dist[source] = 0f;
            var visited = new HashSet<Settlement>();
            while (true)
            {
                Settlement u = null;
                float best = float.PositiveInfinity;
                foreach (var kv in dist)
                {
                    if (visited.Contains(kv.Key)) continue;
                    if (kv.Value < best) { best = kv.Value; u = kv.Key; }
                }
                if (u == null || float.IsPositiveInfinity(best)) break;
                visited.Add(u);
                if (g.Adjacency.TryGetValue(u, out var edges))
                {
                    foreach (var e in edges)
                    {
                        if (e.Kind != EdgeKind.Land) continue;
                        if (visited.Contains(e.To)) continue;
                        float alt = dist[u] + e.Distance;
                        if (alt < dist[e.To])
                        {
                            dist[e.To] = alt;
                        }
                    }
                }
            }
            return dist;
        }

        // ------------------------------------------------------------------
        // Queries
        // ------------------------------------------------------------------

        /// <summary>True if the two ports are in the same connected component.</summary>
        public bool AreConnected(Settlement from, Settlement to)
        {
            if (from == null || to == null) return false;
            if (from == to) return true;
            if (!Adjacency.ContainsKey(from) || !Adjacency.ContainsKey(to)) return false;
            return Bfs(from).Contains(to);
        }

        /// <summary>Shortest path edge-distance between two ports, or -1 if unreachable.</summary>
        public float GetShortestDistance(Settlement from, Settlement to)
        {
            var path = GetShortestPath(from, to);
            if (path == null || path.Count < 2) return from == to ? 0f : -1f;
            float total = 0f;
            for (int i = 0; i + 1 < path.Count; i++)
            {
                var step = Adjacency[path[i]].FirstOrDefault(e => e.To == path[i + 1]);
                total += step.Distance;
            }
            return total;
        }

        /// <summary>
        /// Shortest path between two ports as the ordered list of waypoint
        /// settlements (inclusive of both endpoints), or null if unreachable.
        /// </summary>
        public List<Settlement> GetShortestPath(Settlement from, Settlement to)
        {
            if (from == null || to == null) return null;
            if (!Adjacency.ContainsKey(from) || !Adjacency.ContainsKey(to)) return null;
            if (from == to) return new List<Settlement> { from };

            // Dijkstra
            var dist = new Dictionary<Settlement, float>();
            var prev = new Dictionary<Settlement, Settlement>();
            var visited = new HashSet<Settlement>();
            foreach (var p in Adjacency.Keys) dist[p] = float.PositiveInfinity;
            dist[from] = 0f;

            while (true)
            {
                Settlement u = null;
                float best = float.PositiveInfinity;
                foreach (var kv in dist)
                {
                    if (visited.Contains(kv.Key)) continue;
                    if (kv.Value < best) { best = kv.Value; u = kv.Key; }
                }
                if (u == null || float.IsPositiveInfinity(best)) break;
                if (u == to) break;
                visited.Add(u);
                foreach (var e in Adjacency[u])
                {
                    if (visited.Contains(e.To)) continue;
                    float alt = dist[u] + e.Distance;
                    if (alt < dist[e.To])
                    {
                        dist[e.To] = alt;
                        prev[e.To] = u;
                    }
                }
            }

            if (!prev.ContainsKey(to)) return null;
            var path = new List<Settlement>();
            var cur = to;
            while (cur != null)
            {
                path.Add(cur);
                if (cur == from) break;
                if (!prev.ContainsKey(cur)) return null;
                cur = prev[cur];
            }
            path.Reverse();
            return path;
        }

        /// <summary>
        /// Risk-aware shortest path. Dijkstra over GetEdgeWeight, where the
        /// weight is raw distance × <see cref="GetEdgeRiskMultiplier"/>.
        /// Hostile port edges are pruned (infinite weight). Returns null if
        /// no usable path exists from <paramref name="perspective"/>'s point
        /// of view (e.g. every connecting port is at war with them).
        /// </summary>
        public List<Settlement> GetAdaptivePath(Settlement from, Settlement to, IFaction perspective)
        {
            if (from == null || to == null) return null;
            if (!Adjacency.ContainsKey(from) || !Adjacency.ContainsKey(to)) return null;
            if (from == to) return new List<Settlement> { from };

            var dist = new Dictionary<Settlement, float>();
            var prev = new Dictionary<Settlement, Settlement>();
            var visited = new HashSet<Settlement>();
            foreach (var p in Adjacency.Keys) dist[p] = float.PositiveInfinity;
            dist[from] = 0f;

            while (true)
            {
                Settlement u = null;
                float best = float.PositiveInfinity;
                foreach (var kv in dist)
                {
                    if (visited.Contains(kv.Key)) continue;
                    if (kv.Value < best) { best = kv.Value; u = kv.Key; }
                }
                if (u == null || float.IsPositiveInfinity(best)) break;
                if (u == to) break;
                visited.Add(u);
                foreach (var e in Adjacency[u])
                {
                    if (visited.Contains(e.To)) continue;
                    float w = GetEdgeWeight(e, u, perspective);
                    if (float.IsPositiveInfinity(w)) continue;
                    float alt = dist[u] + w;
                    if (alt < dist[e.To])
                    {
                        dist[e.To] = alt;
                        prev[e.To] = u;
                    }
                }
            }

            if (!prev.ContainsKey(to)) return null;
            var path = new List<Settlement>();
            var cur = to;
            while (cur != null)
            {
                path.Add(cur);
                if (cur == from) break;
                if (!prev.ContainsKey(cur)) return null;
                cur = prev[cur];
            }
            path.Reverse();
            return path;
        }

        /// <summary>
        /// Sum of risk-weighted edge weights along the adaptive shortest
        /// path, or -1 if unreachable. This is the value used to derive
        /// caravan freight prices, not raw distance.
        /// </summary>
        public float GetAdaptiveDistance(Settlement from, Settlement to, IFaction perspective)
        {
            var path = GetAdaptivePath(from, to, perspective);
            if (path == null || path.Count < 2) return from == to ? 0f : -1f;
            float total = 0f;
            for (int i = 0; i + 1 < path.Count; i++)
            {
                var step = Adjacency[path[i]].FirstOrDefault(e => e.To == path[i + 1]);
                float w = GetEdgeWeight(step, path[i], perspective);
                if (float.IsPositiveInfinity(w)) return -1f;
                total += w;
            }
            return total;
        }

        /// <summary>Connected components of the graph as port sets.</summary>
        public List<HashSet<Settlement>> GetConnectedComponents()
        {
            var components = new List<HashSet<Settlement>>();
            var seen = new HashSet<Settlement>();
            foreach (var p in Adjacency.Keys)
            {
                if (seen.Contains(p)) continue;
                var comp = Bfs(p);
                components.Add(comp);
                foreach (var s in comp) seen.Add(s);
            }
            return components;
        }

        private HashSet<Settlement> Bfs(Settlement start)
        {
            var seen = new HashSet<Settlement> { start };
            var queue = new Queue<Settlement>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var u = queue.Dequeue();
                if (!Adjacency.ContainsKey(u)) continue;
                foreach (var e in Adjacency[u])
                {
                    if (seen.Add(e.To)) queue.Enqueue(e.To);
                }
            }
            return seen;
        }

        // ------------------------------------------------------------------
        // Diagnostics
        // ------------------------------------------------------------------

        /// <summary>Multi-line human-readable report on the current topology.</summary>
        public string BuildReport()
        {
            var sb = new StringBuilder();
            int portCount = 0;
            foreach (var s in Adjacency.Keys)
            {
                bool hp = false; try { hp = s.HasPort; } catch { }
                if (hp) portCount++;
            }
            sb.AppendLine($"Trade graph: {PortCount} nodes ({portCount} sea ports, {PortCount - portCount} inland fiefs), {EdgeCount} unique edges ({SeaEdgeCount} sea shortcuts, {LandEdgeCount} land KNN).");
            sb.AppendLine();

            var components = GetConnectedComponents();
            sb.AppendLine($"Connected components: {components.Count}");
            for (int i = 0; i < components.Count; i++)
            {
                var c = components[i];
                sb.AppendLine($"  [{i + 1}] {c.Count} ports: {string.Join(", ", c.Select(s => s.Name?.ToString() ?? s.StringId).OrderBy(n => n))}");
            }
            sb.AppendLine();

            // High-degree sea hubs — ports with the most direct sea-edge
            // connections. Replaces the old "bridge port (multi-lane
            // membership)" section now that lanes are gone.
            sb.AppendLine("Sea hubs (top-degree ports):");
            var hubs = new List<(Settlement port, int seaDeg)>();
            foreach (var kv in Adjacency)
            {
                int seaDeg = 0;
                foreach (var e in kv.Value) if (e.Kind == EdgeKind.Sea) seaDeg++;
                if (seaDeg > 0) hubs.Add((kv.Key, seaDeg));
            }
            hubs.Sort((a, b) => b.seaDeg.CompareTo(a.seaDeg));
            for (int i = 0; i < System.Math.Min(8, hubs.Count); i++)
            {
                sb.AppendLine($"  {hubs[i].port.Name?.ToString() ?? hubs[i].port.StringId}: {hubs[i].seaDeg} sea edges");
            }
            sb.AppendLine();

            // Diameter and average shortest path within the largest component.
            // Sampled, not exhaustive: a 150-node graph would need ~11k Dijkstra
            // calls if we walked every pair, which freezes the campaign for
            // tens of seconds. Sample a fixed number of pair queries instead;
            // gives a useful diameter estimate at <0.1s cost.
            var largest = components.OrderByDescending(c => c.Count).FirstOrDefault();
            if (largest != null && largest.Count > 1)
            {
                const int MaxSampledPairs = 200;
                var ports = largest.ToList();
                int totalPairs = ports.Count * (ports.Count - 1) / 2;
                bool sampling = totalPairs > MaxSampledPairs;

                float maxDist = 0f;
                Settlement maxFrom = null, maxTo = null;
                float totalDist = 0f;
                int sampled = 0;
                var rng = new System.Random(12345); // deterministic
                int target = sampling ? MaxSampledPairs : totalPairs;
                int attempts = 0;
                while (sampled < target && attempts < target * 4)
                {
                    attempts++;
                    int i = rng.Next(ports.Count);
                    int j = rng.Next(ports.Count);
                    if (i == j) continue;
                    float d = GetShortestDistance(ports[i], ports[j]);
                    if (d < 0) continue;
                    totalDist += d;
                    sampled++;
                    if (d > maxDist)
                    {
                        maxDist = d;
                        maxFrom = ports[i];
                        maxTo = ports[j];
                    }
                }
                if (sampled > 0)
                {
                    sb.AppendLine($"Largest component ({largest.Count} ports{(sampling ? $", {sampled} pairs sampled" : "")}):");
                    sb.AppendLine($"  Average shortest path: {totalDist / sampled:n1} map units");
                    sb.AppendLine($"  Diameter (observed): {maxDist:n1} map units ({maxFrom?.Name} ↔ {maxTo?.Name})");
                }
            }
            sb.AppendLine();

            // Adaptive risk surface — list edges whose current multiplier
            // is materially above 1.0. Useful when investigating "why is
            // that caravan taking the long way around?"
            sb.AppendLine("Adaptive risk hotspots (multiplier > 1.10, perspective = neutral):");
            var seen = new HashSet<(Settlement, Settlement)>();
            int hotEdges = 0;
            foreach (var kv in Adjacency)
            {
                foreach (var e in kv.Value)
                {
                    var key = kv.Key.GetHashCode() < e.To.GetHashCode() ? (kv.Key, e.To) : (e.To, kv.Key);
                    if (!seen.Add(key)) continue;
                    float mult = GetEdgeRiskMultiplier(kv.Key, e.To);
                    if (float.IsPositiveInfinity(mult) || mult <= 1.10f) continue;
                    sb.AppendLine($"  {kv.Key.Name} ↔ {e.To.Name}: ×{mult:n2}");
                    hotEdges++;
                    if (hotEdges >= 12) { sb.AppendLine("  …"); break; }
                }
                if (hotEdges >= 12) break;
            }
            if (hotEdges == 0) sb.AppendLine("  (none — current map state is calm)");

            return sb.ToString();
        }
    }
}
