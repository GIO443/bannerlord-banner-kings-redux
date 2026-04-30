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
        // graph. K=6 empirically gives a fully-connected mainland on Calradia
        // without enough false edges to matter. Edges that look like "land"
        // by Vec2 distance but are actually across water (island ↔ mainland)
        // get pruned via the map-distance ratio check below.
        private const int LandKnnK = 6;

        // If the ratio of vanilla map-pathfind distance / euclidean distance
        // exceeds this, the two settlements are not actually land-reachable
        // (e.g. island vs mainland) and the candidate land edge is dropped.
        // 1.5 is permissive enough to allow legitimate winding road routes.
        private const float LandPathfindRatioCap = 1.5f;

        private static ShippingGraph Build()
        {
            var g = new ShippingGraph();

            // Sea edges — intra-lane clique, weighted by gate-to-gate distance.
            foreach (var lane in DefaultShippingLanes.Instance.All)
            {
                if (lane?.Ports == null) continue;
                var ports = lane.Ports;
                for (int i = 0; i < ports.Count; i++)
                {
                    var a = ports[i];
                    if (a == null) continue;
                    if (!g.Adjacency.ContainsKey(a)) g.Adjacency[a] = new List<Edge>();
                    for (int j = 0; j < ports.Count; j++)
                    {
                        if (i == j) continue;
                        var b = ports[j];
                        if (b == null) continue;
                        g.Adjacency[a].Add(new Edge
                        {
                            To = b,
                            Distance = a.GatePosition.Distance(b.GatePosition),
                            Lane = lane,
                            Kind = EdgeKind.Sea
                        });
                    }
                }
            }

            // Land edges — KNN by euclidean distance, pruned by vanilla
            // map-distance ratio so we don't accidentally land-bridge an
            // island to the mainland. Covers towns, castles, villages.
            var landNodes = new List<Settlement>();
            try
            {
                foreach (var s in Settlement.All)
                {
                    if (s == null) continue;
                    if (s.IsHideout) continue;
                    if (!(s.IsTown || s.IsCastle || s.IsVillage)) continue;
                    landNodes.Add(s);
                }
            }
            catch { /* very early in load — Settlement.All not ready */ }

            var mapDistanceModel = TaleWorlds.CampaignSystem.Campaign.Current?.Models?.MapDistanceModel;
            foreach (var a in landNodes)
            {
                if (!g.Adjacency.ContainsKey(a)) g.Adjacency[a] = new List<Edge>();

                // Compute distances to all other land nodes, take K nearest.
                var nearest = new List<(Settlement Node, float Dist)>(landNodes.Count);
                foreach (var b in landNodes)
                {
                    if (b == a) continue;
                    float d = a.GatePosition.Distance(b.GatePosition);
                    nearest.Add((b, d));
                }
                nearest.Sort((x, y) => x.Dist.CompareTo(y.Dist));

                int added = 0;
                for (int i = 0; i < nearest.Count && added < LandKnnK; i++)
                {
                    var (b, euclid) = nearest[i];
                    if (euclid <= 0f) continue;

                    // Prune false land edges using vanilla pathfind distance.
                    // Skip the check if the model is unavailable or throws —
                    // graceful degradation: better an extra edge than none.
                    if (mapDistanceModel != null)
                    {
                        try
                        {
                            float pathDist = mapDistanceModel.GetDistance(a, b, false, false, TaleWorlds.CampaignSystem.Party.MobileParty.NavigationType.All);
                            if (pathDist > 0f && pathDist / euclid > LandPathfindRatioCap) continue;
                            // Use the actual pathfind distance so the graph
                            // weights match how the caravan will really walk.
                            if (pathDist > 0f) euclid = pathDist;
                        }
                        catch { /* fall through to euclidean */ }
                    }

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
            var largest = components.OrderByDescending(c => c.Count).FirstOrDefault();
            if (largest != null && largest.Count > 1)
            {
                float maxDist = 0f;
                Settlement maxFrom = null, maxTo = null;
                float totalDist = 0f;
                int pairs = 0;
                var ports = largest.ToList();
                for (int i = 0; i < ports.Count; i++)
                {
                    for (int j = i + 1; j < ports.Count; j++)
                    {
                        float d = GetShortestDistance(ports[i], ports[j]);
                        if (d < 0) continue;
                        totalDist += d;
                        pairs++;
                        if (d > maxDist)
                        {
                            maxDist = d;
                            maxFrom = ports[i];
                            maxTo = ports[j];
                        }
                    }
                }
                if (pairs > 0)
                {
                    sb.AppendLine($"Largest component ({largest.Count} ports):");
                    sb.AppendLine($"  Average shortest path: {totalDist / pairs:n1} map units");
                    sb.AppendLine($"  Diameter: {maxDist:n1} map units ({maxFrom?.Name} ↔ {maxTo?.Name})");
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
