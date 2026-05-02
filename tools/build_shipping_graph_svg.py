#!/usr/bin/env python3
# Generate an SVG of the BK shipping topology (v1.6.10+).
# Mirrors the new ShippingGraph.Build():
#   - Land edges: KNN K=3 between fiefs (towns + castles) within 75-unit cap.
#   - Sea edges: opportunistic shortcuts. For each port, KNN K=4 by euclidean,
#     add a Sea edge iff seaDist < shortest land path (Dijkstra on land graph).
# Port detection in-engine uses Settlement.HasPort. The XML doesn't surface
# that attribute, so for the SVG we approximate the port set as:
#   curated lane membership ∪ known gap coastal towns (town_S3 Omor,
#   town_S1 Varcheg, town_S6 Sibir, town_EN4 Argoron, town_V1 Sargot).
# This is a static-snapshot reconstruction — the in-game graph relies
# directly on HasPort and will be authoritative.
import os, re, sys, math, xml.etree.ElementTree as ET

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SETTLEMENTS = os.path.join(ROOT, "ref", "warsails", "settlements.xml")
OUT = os.path.join(ROOT, "docs", "wiki", "assets", "shipping-graph.svg")
os.makedirs(os.path.dirname(OUT), exist_ok=True)

LAND_KNN_K = 3
LAND_MAX_EDGE = 75.0
SEA_KNN_K = 4
SEA_SHORTCUT_PENALTY = 0.0

# Authoritative port set per the in-game HasPort flag (base game + War Sails).
# These are the 13 coastal towns that the engine recognizes as having a real
# harbour scene. The new ShippingGraph reads HasPort directly at runtime; this
# set is just for the SVG approximation.
PORTS_BY_ID = {
    # Aserai south coast
    "town_A2": "Husn Fulq",
    "town_A8": "Qasira",
    "town_A4": "Razih",
    "town_A6": "Sanala",
    # Empire south & east coast
    "town_EW4": "Ortysia",
    "town_EN2": "Diathma",
    "town_ES7": "Syronea",
    "town_ES2": "Vostrum",
    # Sturgia & northern coast
    "town_S1":  "Varcheg",
    "town_S7":  "Revyl",
    "town_S3":  "Omor",
    # Vlandia west coast
    "town_V7":  "Charas",
    "town_V5":  "Galend",
    # Battania
    "town_B3":  "Car Banseth",
    # Nord (War Sails) — all four towns
    "town_N1":  "Hvalvik",
    "town_N2":  "Gretysfjord",
    "town_N3":  "Thronderlag",
    "town_N4":  "Hargard",
}

# --- Parse settlements -------------------------------------------------------
# Use regex rather than ET because settlements.xml has gameplay XML quirks.
settlements = {}  # id -> {name, x, y, kind}  kind in {town, castle, village}
src = open(SETTLEMENTS, encoding="utf-8").read()
for m in re.finditer(
    r'<Settlement\s+id="([^"]+)"\s+name="\{=[^}]*\}([^"]+)"[^>]*?posX="([0-9.]+)"\s+posY="([0-9.]+)"',
    src, re.S):
    sid, name, x, y = m.group(1), m.group(2), float(m.group(3)), float(m.group(4))
    if sid.startswith("hideout_"):
        kind = "hideout"
    elif sid.startswith("village_") or sid.startswith("castle_village_"):
        kind = "village"
    elif sid.startswith("castle_"):
        kind = "castle"
    elif sid.startswith("town_"):
        kind = "town"
    else:
        kind = "other"
    settlements[sid] = {"name": name, "x": x, "y": y, "kind": kind}
print(f"parsed {len(settlements)} settlements", file=sys.stderr)

# --- Build land edges first (KNN K=3 between fiefs ≤75 units) ---------------
land_node_ids = [sid for sid, s in settlements.items() if s["kind"] in ("town","castle")]
# Adjacency map: id → list of (neighbor_id, distance) for land edges only.
# Used by the sea-shortcut Dijkstra below.
adj = {sid: [] for sid in land_node_ids}
land_edges_set = set()  # frozenset({a,b})
for a in land_node_ids:
    sa = settlements[a]
    cands = []
    for b in land_node_ids:
        if b == a: continue
        sb = settlements[b]
        d = math.hypot(sa["x"]-sb["x"], sa["y"]-sb["y"])
        if d > LAND_MAX_EDGE: continue
        cands.append((d, b))
    cands.sort()
    for d, b in cands[:LAND_KNN_K]:
        adj[a].append((b, d))
        land_edges_set.add(frozenset((a, b)))
land_edges = [(min(p), max(p)) for p in land_edges_set]

# --- Identify ports (HasPort approximation) ----------------------------------
sea_node_ids = {sid for sid in PORTS_BY_ID.keys() if sid in settlements}
# Ensure ports have an adjacency entry even if not a fief (village ports).
for sid in sea_node_ids:
    if sid not in adj:
        adj[sid] = []

# --- Per-port land-only Dijkstra ---------------------------------------------
import heapq
def dijkstra_land(source):
    dist = {sid: float("inf") for sid in adj}
    dist[source] = 0.0
    pq = [(0.0, source)]
    while pq:
        d, u = heapq.heappop(pq)
        if d > dist[u]: continue
        for v, w in adj[u]:
            nd = d + w
            if nd < dist[v]:
                dist[v] = nd
                heapq.heappush(pq, (nd, v))
    return dist

land_dist = {p: dijkstra_land(p) for p in sea_node_ids}

# --- Build sea edges (opportunistic shortcuts) -------------------------------
sea_edges = []  # (a_id, b_id, label="Sea") — label kept for legend compat
sea_edges_added = set()  # frozenset to dedupe a↔b
ports_sorted = sorted(sea_node_ids)
for a in ports_sorted:
    sa = settlements[a]
    cands = []
    for b in sea_node_ids:
        if b == a: continue
        sb = settlements[b]
        d = math.hypot(sa["x"]-sb["x"], sa["y"]-sb["y"])
        cands.append((d, b))
    cands.sort()
    added = 0
    for sea_d, b in cands:
        if added >= SEA_KNN_K: break
        if sea_d <= 0: continue
        ld = land_dist[a].get(b, float("inf"))
        if sea_d + SEA_SHORTCUT_PENALTY >= ld: continue
        key = frozenset((a, b))
        if key not in sea_edges_added:
            sea_edges.append((a, b, "Sea"))
            sea_edges_added.add(key)
        added += 1

# --- Render SVG --------------------------------------------------------------
# Calradia Y goes up = north; SVG Y goes up = south, so flip.
all_x = [s["x"] for s in settlements.values()]
all_y = [s["y"] for s in settlements.values()]
minx, maxx = min(all_x), max(all_x)
miny, maxy = min(all_y), max(all_y)
PAD = 60
W = (maxx - minx) + 2*PAD
H = (maxy - miny) + 2*PAD

def to_svg(p):
    x = (p[0] - minx) + PAD
    y = (maxy - p[1]) + PAD  # flip
    return x, y

LANE_COLORS = {
    "Sea": "#2b6cb0",  # all sea edges in the new model are auto-derived shortcuts
}

out = []
out.append(f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {W:.0f} {H:.0f}" font-family="system-ui,-apple-system,sans-serif" font-size="9">')
out.append(f'<rect width="100%" height="100%" fill="#f7fafc"/>')
# Title
out.append(f'<text x="{PAD}" y="{PAD-20}" font-size="18" font-weight="bold" fill="#1a202c">Banner Kings Shipping Graph</text>')
out.append(f'<text x="{PAD}" y="{PAD-6}" font-size="11" fill="#4a5568">{len(land_node_ids)} fiefs as land nodes • {len(sea_node_ids)} ports (HasPort) • {len(land_edges)} land KNN edges • {len(sea_edges)} sea shortcut edges</text>')

# Land edges first (so sea draws on top)
for a, b in land_edges:
    pa = to_svg((settlements[a]["x"], settlements[a]["y"]))
    pb = to_svg((settlements[b]["x"], settlements[b]["y"]))
    out.append(f'<line x1="{pa[0]:.1f}" y1="{pa[1]:.1f}" x2="{pb[0]:.1f}" y2="{pb[1]:.1f}" stroke="#a0aec0" stroke-width="0.6" stroke-dasharray="2,2" opacity="0.7"/>')

# Sea edges
for a, b, lane in sea_edges:
    pa = to_svg((settlements[a]["x"], settlements[a]["y"]))
    pb = to_svg((settlements[b]["x"], settlements[b]["y"]))
    out.append(f'<line x1="{pa[0]:.1f}" y1="{pa[1]:.1f}" x2="{pb[0]:.1f}" y2="{pb[1]:.1f}" stroke="{LANE_COLORS.get(lane,"#000")}" stroke-width="2.5" opacity="0.85"/>')

# Nodes
def draw_node(sid, s, fill, stroke, r, label_pos="below"):
    x, y = to_svg((s["x"], s["y"]))
    out.append(f'<circle cx="{x:.1f}" cy="{y:.1f}" r="{r}" fill="{fill}" stroke="{stroke}" stroke-width="1"/>')
    ly = y + r + 9 if label_pos == "below" else y - r - 3
    out.append(f'<text x="{x:.1f}" y="{ly:.1f}" text-anchor="middle" fill="#1a202c">{s["name"]}</text>')

# Background: faint markers for non-graph fiefs (towns/castles not on any edge)
graph_fiefs = set(land_node_ids) | sea_node_ids
for sid, s in settlements.items():
    if sid not in graph_fiefs: continue
    if s["kind"] == "village":
        draw_node(sid, s, "#fff", "#2b6cb0", 3.5)  # village ports = small blue ring
    elif s["kind"] == "castle":
        draw_node(sid, s, "#cbd5e0", "#4a5568", 4)
    elif s["kind"] == "town":
        if sid in sea_node_ids:
            draw_node(sid, s, "#bee3f8", "#2b6cb0", 5.5)  # town port = blue
        else:
            draw_node(sid, s, "#feebc8", "#dd6b20", 5)    # town inland = orange

# Legend
lx, ly = W - 220, PAD + 10
out.append(f'<rect x="{lx-10}" y="{ly-15}" width="220" height="160" fill="#fff" stroke="#cbd5e0" stroke-width="1"/>')
out.append(f'<text x="{lx}" y="{ly}" font-size="11" font-weight="bold" fill="#1a202c">Legend</text>')
ly += 18
out.append(f'<line x1="{lx}" y1="{ly}" x2="{lx+30}" y2="{ly}" stroke="{LANE_COLORS["Sea"]}" stroke-width="2.5"/>')
out.append(f'<text x="{lx+38}" y="{ly+3}" fill="#1a202c">Sea shortcut (KNN K=4)</text>')
ly += 14
out.append(f'<line x1="{lx}" y1="{ly}" x2="{lx+30}" y2="{ly}" stroke="#a0aec0" stroke-width="0.6" stroke-dasharray="2,2"/>')
out.append(f'<text x="{lx+38}" y="{ly+3}" fill="#1a202c">Land KNN edge (≤75u, K=3)</text>')
ly += 16
out.append(f'<circle cx="{lx+15}" cy="{ly}" r="5.5" fill="#bee3f8" stroke="#2b6cb0"/>')
out.append(f'<text x="{lx+38}" y="{ly+3}" fill="#1a202c">Town port (sea-edge)</text>')
ly += 14
out.append(f'<circle cx="{lx+15}" cy="{ly}" r="3.5" fill="#fff" stroke="#2b6cb0"/>')
out.append(f'<text x="{lx+38}" y="{ly+3}" fill="#1a202c">Village port (sea-edge)</text>')
ly += 14
out.append(f'<circle cx="{lx+15}" cy="{ly}" r="5" fill="#feebc8" stroke="#dd6b20"/>')
out.append(f'<text x="{lx+38}" y="{ly+3}" fill="#1a202c">Town (no sea edge)</text>')
ly += 14
out.append(f'<circle cx="{lx+15}" cy="{ly}" r="4" fill="#cbd5e0" stroke="#4a5568"/>')
out.append(f'<text x="{lx+38}" y="{ly+3}" fill="#1a202c">Castle</text>')

out.append('</svg>')

with open(OUT, "w", encoding="utf-8") as f:
    f.write("\n".join(out))

print(f"\nSEA-EDGE NODES: {len(sea_node_ids)}", file=sys.stderr)
print(f"LAND-EDGE NODES (towns+castles): {len(land_node_ids)}", file=sys.stderr)
print(f"Wrote {OUT}", file=sys.stderr)
