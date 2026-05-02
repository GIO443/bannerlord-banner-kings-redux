# Shipping & trade

← [Home](Home)

How caravans, AI lord parties, captive caravans, and the player travel
across Calradia under BK Redux. The trade graph is an explicit topology
with adaptive risk weighting and a single unified graph covering both
**sea** and **land** edges, so non-port settlements participate too.
This page is the HOW for living with that system.

## On this page

- [How shipping works](#how-shipping-works)
- [Graph topology map](#graph-topology-map)
- [AI lord parties at sea](#ai-lord-parties-at-sea)
- [Caravan auto-board and ticker](#caravan-auto-board-and-ticker)
- [Adaptive shipping costs](#adaptive-shipping-costs-v161)
- [Console diagnostics](#console-diagnostics)
- [Test scenario commands](#test-scenario-commands-v1612)
- [Quest-mandated overloaded fleets](#quest-mandated-overloaded-fleets)
- [Shipping FAQ](#shipping-faq)

---

## How shipping works

Caravans and parties auto-board ships at any settlement with a working
harbour scene. Sea travel takes real time and is faster under seafaring
perks (Drakkar Helmsman).

**The trade graph is one unified topology with two edge kinds:**

- **Land edges** — k-nearest-neighbor between every town and castle
  (k = 3, ≤75u euclidean cap). Villages aren't graph nodes for land
  routing; they're walked through via vanilla pathfind. Edge weight is
  raw gate-to-gate distance.
- **Sea edges** — *opportunistic shortcuts* between ports. A port is
  any settlement the engine flags as `HasPort` (base game + War Sails
  set this on every coastal town with an actual harbour scene). For
  each port, BK looks at its 4 nearest port neighbours and **only
  adds a sea edge if the direct sea hop is shorter than the best land
  path between them.** Same-coast ports with a road lose the
  comparison and stay land-only — a caravan walks. Ports across a bay
  or on different landmasses win the comparison and get a sea edge —
  a caravan boards.

The "should this caravan board a ship" decision is no longer a separate
classification step. It's just whichever route is shorter on the unified
graph; if shortest crosses a sea edge, the caravan ships.

This replaced an earlier curated lane list, which missed five coastal
towns (Omor, Varcheg, Sibir, Argoron, Sargot) that the auto-port
detection now picks up correctly.

Both edge kinds use the same risk multiplier (war / siege / banditry /
neutral). Diagonal sea+land paths emerge naturally — a captive caravan
from inland Battania to a Vlandian fief might walk south, board a ship,
and land on the Vlandian coast all in one graph path.

Caravans book the *next hop* of the shortest path on each settlement
arrival, re-evaluating trade scores at every intermediate stop. This
preserves the multi-stop economy: a caravan shipping Hvalvik → Ostican
will stop and trade at Sturgia central along the way.

## Graph topology map

A snapshot of the current shipping graph across Calradia. Towns and
castles are land nodes; coastal towns flagged as ports are blue. Dashed
grey lines are land hops; solid blue lines are the auto-derived sea
shortcuts. Sea hops only show up where sailing between two ports is
genuinely shorter than the road path, so same-coast neighbours like
Sanala ↔ Husn Fulq stay land-only and a caravan walks them.

![Banner Kings shipping graph topology](assets/shipping-graph.svg)

Counts at the top of the diagram give the current totals (fiefs as
land nodes, ports, land edges, sea edges). The diagram is a static
reconstruction; the live in-game graph reads the engine's port flag
directly and adapts to wars, sieges, and bandit pressure as the world
state changes.

## AI lord parties at sea

When an AI lord party (or an entire AI army) arrives at a port whose
shipping lane connects to its target, the party boards via the same
vanilla "Set sail" call the player uses, sails across, and disembarks
at the destination port. Armies travel intact — the vanilla
`IsCurrentlyAtSea` cascade keeps sub-parties attached to the leader.
Without this, AI lords buy ships and never use them; you see them
stuck at the coast.

Auto-disembark only fires for **AI lord parties** Banner Kings put at
sea. Vanilla NavalDLC convoys, caravans, and bandit ships use their
own naval AI and are left alone, and an AI lord crossing an
intermediate port stays at sea rather than briefly disembarking and
re-embarking.

**Graph-driven port redirect.** The hourly redirect that pushes
parties toward a boarding port consults the unified shipping graph:
nearest entry node → shortest (or risk-weighted) path to target → if
the first edge is a sea hop, walk the party to the boarding port; if
the first edge is land, hand off to vanilla AI. Village-targeted
parties route via the village's bound town or castle, since graph
nodes are towns and castles only. Coastal parties whose pathfind to
both the entry node *and* the original target returns Infinity get a
"stuck at coast" fallback — forced to the nearest sea-reachable port —
and parties pinned at the same coordinates for several ticks are
hard-teleported there as an escape hatch for impassable-terrain
spawns. Once a party is targeting a port from a prior redirect, the
re-evaluation is skipped to prevent ping-pong between two coastal
options. Decisions log to `Configs/ModLogs/BK_redirect.txt` for
diagnosing a stuck caravan.

## Caravan auto-board and ticker

Caravan and player sea travel uses Banner Kings' own ticker — a
behaviour-level hourly check rather than a per-party tick. Caravans
that suspend themselves mid-voyage still get re-checked every hour
and arrive on schedule, where earlier builds lost the per-party tick
the moment a caravan deactivated and left it sitting on the coast.

**NavalDLC convoys are hands-off.** A NavalDLC convoy (`IsCaravan=true`,
already at sea via NavalDLC's own AI) is never picked up by BK's
ship-travel flow. If you suspect a stuck convoy from a save migrated
off an older build, look for `IsActive=False / AtSea=True /
AiDisabled=True / BKTracked=false` in `dump_caravans`.

**Unified rescue sweep.** A single daily-tick sweep — also run on
save load — walks `MobileParty.All` once and applies every known
broken-state fix in one pass. The five signatures it catches:

- **BK shipping limbo** — `IsActive=false`, not in the sailing dict,
  no path back to `FinishTravel`. Reactivated and AI re-enabled.
- **AI-disabled NavalDLC convoy not BK-tracked** — legacy state from
  pre-rescue builds that briefly hijacked at-sea convoys into BK's
  ship-travel flow. AI re-enabled so NavalDLC takes over.
- **At-sea over non-water terrain (boat on land)** — at-sea flag
  cleared. The test now triggers on coastal and transitional terrain
  (Beach, RuralArea, Bridge, Fording) where most strandings actually
  happen, rather than only "clearly land".
- **Land mode over open water** — a naval-capable party walking
  through the sea is re-flagged as at sea so NavalDLC pathfinding
  works.
- **Legacy slave caravan with no live move target** — destroyed
  (the AI-town slave-export flow that used to spawn these is gone).

The sweep is gated to `IsCaravan || IsLordParty` parties before any
expensive checks; without that gate it caused 10-second daily-tick
freezes on campaigns with 5000+ parties.

**Siege-end caravan release.** When a siege starts, BK puts every
caravan inside the besieged settlement on hold so they don't try to
walk out through siege lines. `OnSiegeEventEnded` now flags
`Ai.RethinkAtNextHourlyTick = true` on every released caravan so
vanilla CaravanAi picks a fresh destination — older builds never
released the hold and caravans pinned at siege start stayed pinned
forever after the siege resolved.

## Adaptive shipping costs

Routes are graph-aware *and* react to the current world state. Each
shipping edge is weighted by raw map distance × a risk multiplier that
combines:

- **Hostile port owners** — if either endpoint of an edge is owned by a
  faction at war with the cargo's owner, the edge is unusable for that
  caravan. Routing automatically detours around it. This is why a
  Vlandian caravan suddenly takes a longer route through neutral ports
  when Vlandia declares war on Sturgia.
- **Sieged ports** — +60% per sieged endpoint. Caravans avoid sieged
  ports when alternative paths exist; freight prices through sieged
  zones go up sharply.
- **Bandit pressure** — +5% per active hideout within ~60 map units of
  either endpoint, capped at +50% combined. Coasts crawling with bandit
  hideouts cost more to ship through.
- **Soft neutral penalty** — +5% per "foreign but peaceful" endpoint, so
  same-faction routes are preferred when otherwise equal.

Caravans pick their next hop using this weighted shortest path, falling
back to the static shortest path only if every adaptive route is fully
blocked (every connecting port at war). Player freight prices use the
same weighted distance — sailing into a war zone costs more.

Toggle this off via **MCM → Banner Kings → Economy → Adaptive Shipping
Risk** (default: on, no restart required). With the toggle off,
caravans still use the shipping graph for cross-continent routing but
ignore war / siege / banditry — freight prices fall back to raw
straight-line distance, matching v1.5.x flavour. Useful if a long
campaign-wide war makes shipping feel too disrupted.

## Console diagnostics

Diagnose the live state in-game with these console commands:

- `bannerkings.shipping_topology` — connected components, bridge ports,
  diameter, **and current risk hotspots** (edges with multiplier > 1.10).
- `bannerkings.shipping_path <fromId> <toId>` — static shortest path
  ignoring risk.
- `bannerkings.shipping_risk_path <fromId> <toId>` — side-by-side
  comparison of the static route vs the adaptive route a player-faction
  caravan would actually take given the current world state. Use this
  when a caravan is taking a surprising path.

## Test-scenario commands

A small suite of cheats for forcing world state instead of waiting for
it. Cheats must be enabled in the launcher; otherwise these are inert.
Composable — run them in sequence to set up a specific situation:

- `bannerkings.test_setup` — leveraged player start: +500 000 gold,
  +1 000 renown, full peerage applied to your clan. Idempotent.
- `bannerkings.test_war Vlandia | Sturgia` — declare war between two
  kingdoms. Argument is `<factionA> | <factionB>` (StringId or display
  name, pipe-separated).
- `bannerkings.test_peace Vlandia | Sturgia` — make peace between two
  kingdoms.
- `bannerkings.test_clear_wars` — peace out every active war on the
  map. Useful for resetting between scenario runs.
- `bannerkings.test_spawn_caravan SomeMerchant | town_V8` — spawn a
  fresh caravan owned by the named hero, starting at the given town.
- `bannerkings.test_relocate_caravan CaravanName | town_S4` — teleport
  an existing caravan to another town. Caravan name is what appears in
  the encyclopedia.
- `bannerkings.test_dump_state` — read-only summary: player gold /
  renown / fiefs, ongoing wars, ongoing sieges, every caravan's
  current/target settlement, and the current shipping risk hotspots.

A typical shipping iteration loop: `test_setup` → `test_war Vlandia | Sturgia`
→ `shipping_risk_path town_V8 town_S2` (confirm the war redirects the route)
→ `test_clear_wars` (reset).

## Quest-mandated overloaded fleets

The War Sails Northern Crossing quest hands you ~190 troops on a fleet
with ~50 crew capacity, which under vanilla NavalDLC's −74% over-crew
speed penalty would floor you at speed 1. Banner Kings — Redux clamps
that penalty at −50% so the quest is traversable in finite time. Still
painful to overload your fleet, but not stranded.

The "Overmanned" line in your speed tooltip should now show roughly
−50% rather than worse.

---

## Shipping FAQ

**Q: My caravan went on a ship — bug?**
No — caravans whose destination is on a known shipping lane auto-board.
Unboard via the caravan menu in that port.

**Q: Why is my ship taking forever?**
Travel time is distance / 75, faster under the Drakkar Helmsman perk.
Cross-Calradia trips take 4–6 days.

**Q: Why did the freight cost just jump?**
Adaptive shipping pricing. Freight cost is graph distance ×
risk multiplier. Sieged endpoint = +60%. Bandits crawling the coast
near either port = up to +50%. Foreign-owned port = +5%. A war zone
on your route can land you at almost double the previous fare. Run
`bannerkings.shipping_risk_path <fromId> <toId>` in the console to see
exactly which hops the price came from.

**Q: Why did my caravan take the long way around?**
Same system. If the direct path crosses a port owned by a faction at
war with you, that edge is closed and the caravan reroutes. Bandit-
heavy coasts are also avoided when a comparable alternative exists.
The route stabilises once the war ends or the bandit hideouts clear.

**Q: Why am I crawling at speed 1 with the War Sails quest fleet?**
You shouldn't be on Redux. See [Quest-mandated overloaded fleets](#quest-mandated-overloaded-fleets) above.

**Q: I defeated an enemy caravan but got nothing — bug?**
*Was* a bug. The 1.3.x port broke the caravan loot dialog — it was
deleting the cargo instead of giving you a loot screen. Surrendering
or captured caravans now open a real loot screen for cargo and a
separate prisoner screen for their troops, the way they did before
the 1.3.x port landed.

---

← [Player guide](Player-Guide) · [Home](Home) · [Slavery & raiding →](Slavery-and-Raiding)
