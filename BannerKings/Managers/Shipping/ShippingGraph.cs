using System.Collections.Generic;
using System.Linq;
using System.Text;
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

        public struct Edge
        {
            public Settlement To;
            public float Distance;
            public ShippingLane Lane;
        }

        /// <summary>port → outgoing edges</summary>
        public Dictionary<Settlement, List<Edge>> Adjacency { get; private set; } = new();

        public IEnumerable<Settlement> Ports => Adjacency.Keys;

        public int PortCount => Adjacency.Count;

        public int EdgeCount => Adjacency.Values.Sum(list => list.Count) / 2;

        // ------------------------------------------------------------------
        // Build
        // ------------------------------------------------------------------

        private static ShippingGraph Build()
        {
            var g = new ShippingGraph();
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
                            Lane = lane
                        });
                    }
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
            sb.AppendLine($"Shipping graph: {PortCount} ports, {EdgeCount} unique edges across {DefaultShippingLanes.Instance.All.Count()} lanes.");
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

            return sb.ToString();
        }
    }
}
