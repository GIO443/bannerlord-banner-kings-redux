using System.Collections.Generic;
using System.Linq;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace BannerKings.Managers.Shipping
{
    /// <summary>
    /// Explicit graph view of the BK shipping network.
    ///
    /// Nodes are ports (Settlements that appear on at least one ShippingLane).
    /// Edges are gate-to-gate Vec2 distance between every port-pair that shares
    /// at least one lane (full intra-lane clique — matches the current AI's
    /// "any port in the same lane is reachable" semantics).
    ///
    /// Provides connectivity, shortest-path, and connected-component analysis.
    /// The shipping AI does NOT consult this graph yet; this is a diagnostic
    /// and design tool, with the option to migrate the AI later once the
    /// topology proves out.
    ///
    /// Built lazily on first access. Rebuild via <see cref="Invalidate"/> if
    /// lane data changes mid-campaign (rare — usually fixed at game start).
    /// </summary>
    public class ShippingGraph
    {
        private static ShippingGraph _instance;

        public static ShippingGraph Instance => _instance ??= Build();

        public static void Invalidate() => _instance = null;

        public enum EdgeKind
        {
            Sea,
            Land
        }

        public struct Edge
        {
            public Settlement To;
            public float Distance;
            public ShippingLane Lane;     // null for land edges
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
        // graph. K=3 keeps node degree (after symmetrization) modest while
        // still providing enough redundancy for Dijkstra to detour around
        // war/siege/banditry. Combined with the max-distance cap below,
        // islands self-isolate into their own components.
        private const int LandKnnK = 3;

        // Maximum euclidean distance for a land edge. Pairs farther apart
        // are dropped — keeps islands from being bridged to the mainland by
        // straight-line proximity, and bounds graph density. Calradia is
        // ~700u across; settlements average ~50u between neighbors, so 75u
        // is just above the typical KNN distance.
        private const float LandMaxEdgeDistance = 75f;

        // Greedy nearest-neighbor traversal: start at the first non-null
        // port, then repeatedly append the closest remaining port. This
        // produces a roughly geographically-sensible chain even when the
        // lane's declared port order isn't sorted by position.
        private static List<Settlement> OrderLanePortsAsChain(IList<Settlement> ports)
        {
            var remaining = new List<Settlement>();
            foreach (var p in ports) if (p != null) remaining.Add(p);
            var chain = new List<Settlement>();
            if (remaining.Count == 0) return chain;
            chain.Add(remaining[0]);
            remaining.RemoveAt(0);
            while (remaining.Count > 0)
            {
                var last = chain[chain.Count - 1];
                int bestIdx = 0;
                float bestDist = float.MaxValue;
                for (int i = 0; i < remaining.Count; i++)
                {
                    float d = last.GatePosition.Distance(remaining[i].GatePosition);
                    if (d < bestDist) { bestDist = d; bestIdx = i; }
                }
                chain.Add(remaining[bestIdx]);
                remaining.RemoveAt(bestIdx);
            }
            return chain;
        }

        private static ShippingGraph Build()
        {
            var g = new ShippingGraph();

            // Sea edges — chain (not clique) via greedy nearest-neighbor
            // traversal. Each lane becomes a sequence of N-1 edges between
            // adjacent ports rather than N*(N-1)/2 edges between every pair.
            // Multi-port routes still resolve via Dijkstra; the chain just
            // makes them explicit hops (which matches how ships actually
            // sail port-to-port).
            foreach (var lane in DefaultShippingLanes.Instance.All)
            {
                if (lane?.Ports == null || lane.Ports.Count < 2) continue;
                var ordered = OrderLanePortsAsChain(lane.Ports);
                for (int i = 0; i < ordered.Count; i++)
                {
                    var a = ordered[i];
                    if (a == null) continue;
                    if (!g.Adjacency.ContainsKey(a)) g.Adjacency[a] = new List<Edge>();
                }
                for (int i = 0; i + 1 < ordered.Count; i++)
                {
                    var a = ordered[i];
                    var b = ordered[i + 1];
                    if (a == null || b == null) continue;
                    float d = a.GatePosition.Distance(b.GatePosition);
                    g.Adjacency[a].Add(new Edge { To = b, Distance = d, Lane = lane, Kind = EdgeKind.Sea });
                    g.Adjacency[b].Add(new Edge { To = a, Distance = d, Lane = lane, Kind = EdgeKind.Sea });
                }
            }

            // Land edges — KNN by euclidean distance between fiefs only
            // (towns + castles, no villages). Villages aren't valid graph
            // nodes: they have no Town and break BK's caravan-fee bookkeeping
            // on arrival. Captive caravans walking through villages use
            // vanilla pathfinding between graph hops — the graph models
            // fief-level routing intent, not road-level traversal.
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

            // Land edges use raw euclidean distance with a max-distance cap.
            // Earlier versions ran a vanilla MapDistanceModel pathfind per
            // candidate to validate land-reachability, but that scaled to
            // ~900 real pathfind calls on a Calradia-sized map and froze the
            // game for several seconds on first build. The cap-based approach
            // produces near-identical topology (islands self-isolate beyond
            // the cap distance) at zero pathfind cost.
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
                            Lane = null,
                            Kind = EdgeKind.Land
                        });
                        added++;
                    }
                }
                catch { /* skip this source — partial graph still useful */ }
            }

            return g;
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
            sb.AppendLine($"Trade graph: {PortCount} settlement nodes, {EdgeCount} unique edges ({SeaEdgeCount} sea, {LandEdgeCount} land) across {DefaultShippingLanes.Instance.All.Count()} sea lanes.");
            sb.AppendLine();

            var components = GetConnectedComponents();
            sb.AppendLine($"Connected components: {components.Count}");
            for (int i = 0; i < components.Count; i++)
            {
                var c = components[i];
                sb.AppendLine($"  [{i + 1}] {c.Count} ports: {string.Join(", ", c.Select(s => s.Name?.ToString() ?? s.StringId).OrderBy(n => n))}");
            }
            sb.AppendLine();

            // Bridge ports — ports on more than one lane (i.e. the connectors).
            sb.AppendLine("Bridge ports (multi-lane membership):");
            foreach (var p in Adjacency.Keys)
            {
                var lanes = DefaultShippingLanes.Instance.GetSettlementLanes(p).ToList();
                if (lanes.Count >= 2)
                {
                    sb.AppendLine($"  {p.Name?.ToString() ?? p.StringId}: {string.Join(" + ", lanes.Select(l => l.Name?.ToString() ?? l.StringId))}");
                }
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
