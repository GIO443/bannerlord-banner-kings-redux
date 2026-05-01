# Shipping & trade

← [Home](Home)

How caravans, AI lord parties, captive caravans, and the player travel
across Calradia under BK Redux. The trade graph was overhauled across
the 1.5–1.6 lines into an explicit topology with adaptive risk
weighting; v1.6.3 unified it into a single graph covering both **sea**
and **land** edges so non-port settlements participate too. This page
is the HOW for living with that system.

## On this page

- [How shipping works](#how-shipping-works)
- [AI lord parties at sea](#ai-lord-parties-at-sea)
- [Caravan auto-board and ticker](#caravan-auto-board-and-ticker)
- [Adaptive shipping costs](#adaptive-shipping-costs-v161)
- [Console diagnostics](#console-diagnostics)
- [Test scenario commands](#test-scenario-commands-v1612)
- [Quest-mandated overloaded fleets](#quest-mandated-overloaded-fleets)
- [Shipping FAQ](#shipping-faq)

---

## How shipping works

Caravans and parties auto-board ships at known shipping lanes. Sea travel
takes real time and is faster under seafaring perks (Drakkar Helmsman).

**The trade graph is one unified topology with two edge kinds:**

- **Sea edges** — built from BK's `DefaultShippingLanes` (intra-lane
  clique, one edge per port-pair sharing a lane). Cross-continent routes
  chain through bridge ports that appear on more than one lane.
- **Land edges** — built from k-nearest-neighbor adjacency between every
  town, castle, and village, with vanilla pathfind validation pruning
  false land bridges (so a Nord island doesn't get connected to the
  mainland by raw straight-line distance). Edge weight is the actual
  vanilla map-pathfind distance, so the graph weights match how a
  caravan really walks.

Both edge kinds use the same risk multiplier (war / siege / banditry /
neutral). Diagonal sea+land paths emerge naturally — a captive caravan
from inland Battania to a Vlandian fief might walk south, board a ship,
and land on the Vlandian coast all in one graph path.

Caravans book the *next hop* of the shortest path on each settlement
arrival, re-evaluating trade scores at every intermediate stop. This
preserves the multi-stop economy: a caravan shipping Hvalvik → Ostican
will stop and trade at Sturgia central along the way.

## AI lord parties at sea

When an AI lord party (or an entire AI army) arrives at a port whose
shipping lane connects to its target, the party boards via the same
vanilla "Set sail" call the player uses, sails across, and disembarks at
the destination port. Armies travel intact — the vanilla
`IsCurrentlyAtSea` cascade keeps sub-parties attached to the leader.
Before this, AI lords would buy ships and never use them; you'd see them
stuck at the coast.

The auto-disembark only fires for **AI lord parties** Banner Kings put
at sea — vanilla NavalDLC convoys, caravans, and bandit ships use their
own naval AI and are left alone. Earlier builds disembarked everything
on port arrival, which left convoys in land mode while still
geometrically on water and stranded them on the coast.

**No more disembark+immediately-reembark cycle (v1.6.4.0).** When an
AI lord at sea entered an intermediate port whose lane reached its
target, the previous logic disembarked them, then the lord branch
immediately re-embarked them — a wasteful round-trip on every
intermediate port that could produce visible flicker. Now stays at
sea and just refreshes the move target.

**Graph-driven port redirect (v1.6.5.3+).** The hourly redirect that
pushes parties toward a boarding port no longer uses geometric
heuristics ("closest port that's 30% closer than target"). It
consults the unified shipping graph: nearest entry node → adaptive
or shortest path to target → if the FIRST edge is a sea hop, walk
the party to the entry node (the boarding port); if first edge is
land, hand off to vanilla AI. Village-targeted parties route via the
village's bound town/castle (graph nodes are towns + castles only).
Caravans whose owner-merchant isn't loaded as `LeaveHero` now also
go through the redirect (previously bailed silently). Coastal
parties whose vanilla pathfind to BOTH the entry node AND the
original target returns Infinity get a "stuck at coast" fallback —
forced to the nearest sea-reachable port. Parties already targeting
a port from a prior redirect skip re-evaluation to prevent
ping-pong between two coastal ports. Parties stuck at the same
coordinates over 4+ ticks are hard-teleported to the chosen port
(escape hatch for impassable-terrain spawns). Decisions log to
`Configs/ModLogs/BK_redirect.txt` if you need to debug a stuck
caravan.

## Caravan auto-board and ticker

Caravan and player sea travel uses Banner Kings' own ticker (a
behaviour-level hourly check rather than a per-party tick). This means
caravans that suspend themselves mid-voyage still get re-checked every
hour and arrive on schedule — earlier builds lost the per-party tick once
the caravan deactivated, and the caravan would sit on the coast forever.

**NavalDLC convoys are now hands-off (v1.6.4.0).** Earlier builds
treated NavalDLC convoys (`IsCaravan=true`, already at sea via
NavalDLC's own AI) as candidates for BK's ship-travel system, which
deactivated them mid-ocean and left them frozen. BK now skips any
party that's already at sea — NavalDLC keeps managing its own
convoys. The signature in `dump_caravans` to look for if you suspect a
stuck convoy: `IsActive=False / AtSea=True / AiDisabled=True / BKTracked=false`.

**Mid-session orphan rescue (v1.6.4.0).** A daily tick scans for
caravans with the BK shipping-limbo signature (inactive, AI disabled,
not in BK's sailing dict) and reactivates them. Previously the rescue
only ran on save load, so a caravan that went stuck during a session
stayed stuck until next save/load. Save-load itself no longer
cancels in-progress voyages either — the load-time rescue now skips
caravans legitimately tracked in the sailing dict.

## Adaptive shipping costs (v1.6.1)

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

## Test-scenario commands (v1.6.1.2)

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
Adaptive shipping pricing (v1.6.1+). Freight cost is graph distance ×
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
*Was* a bug. The 1.3.x port broke the caravan loot dialog (it was
deleting the cargo instead of giving you a loot screen). Redux v1.5.2+
restores it: surrendering or captured caravans now open a real loot
screen for cargo, and a separate prisoner screen for their troops, the
same way they did pre-1.3.x.

---

← [Player guide](Player-Guide) · [Home](Home) · [Slavery & raiding →](Slavery-and-Raiding)
