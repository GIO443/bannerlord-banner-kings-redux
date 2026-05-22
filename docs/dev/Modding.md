# Modding Banner Kings

Banner Kings is a **modding framework** as much as it is a gameplay
mod. This page documents the design philosophy you should follow when:

- contributing patches to BK itself,
- writing a sub-mod that extends BK (a new lifestyle, a new title type,
  a new shipping consumer),
- or building a separate mod that lives alongside BK and needs to play
  nicely with its systems.

If you're a player, you can ignore this page — see
[Getting started](../wiki/Getting-Started.md) instead.

## On this page

- [Reskinning content: the XML data layer](#reskinning-content-the-xml-data-layer)
- [Core philosophy: BK decides, vanilla executes](#core-philosophy-bk-decides-vanilla-executes)
- [What "decision" means in practice](#what-decision-means-in-practice)
- [What "executes" means in practice](#what-executes-means-in-practice)
- [The checklist](#the-checklist)
- [Worked example: shipping](#worked-example-shipping)
- [Worked example: rescue sweep](#worked-example-rescue-sweep)
- [Anti-patterns](#anti-patterns)
- [Compatibility shims](#compatibility-shims)
- [Recipe — adding a new BK system](#recipe--adding-a-new-bk-system)
- [Recipe — extending an existing BK system from a sub-mod](#recipe--extending-an-existing-bk-system-from-a-sub-mod)
- [Log loudly — but behind a toggle](#log-loudly--but-behind-a-toggle)
- [Conventions](#conventions)

---

## Reskinning content: the XML data layer

**If you only want to change BK's *content* — different religions,
different titles, different lifestyles, a whole different setting — you
do not need to write or compile any C#.** Stop here and read the
[structural schema reference](../dev-reference/structural-schema.md)
(`docs/dev-reference/structural-schema.md` in the repo). The rest of
this page is about C#; this section is about XML.

BK's flavor content is defined in XML data files, not hardcoded. That
includes religions, faiths, divinities, doctrines, faith groups,
marriage and war doctrines, lifestyles, innovations, eras, title names,
governments, succession and inheritance laws, gender laws, casus belli,
interest groups, mercenary privileges, and council positions.

### How it works

At startup BK scans **every loaded module** for
`ModuleData/BKData/*.xml`, and merges what it finds by `(category, id)`
— **last writer wins by module load order.** A mod that loads after BK
overrides BK's rows; a mod can also add brand-new rows or remove BK's.

So a setting-overhaul mod ships its own `ModuleData/BKData/` folder:

```
MyOverhaul/
└── ModuleData/
    └── BKData/
        ├── bk_faiths.xml          ← your pantheon, BK faith ids reused
        ├── bk_divinities.xml      ← your gods
        ├── bk_governments.xml     ← your constitutions
        └── bk_title_names.xml     ← your title nouns
```

Reuse a BK `id` to **replace** that entity; use a fresh `id` to **add**
one. You only need to ship the files for the categories you actually
change — anything you don't override keeps BK's defaults.

### What needs C#, what doesn't

| You want to… | Need C#? |
|---|---|
| Rename / re-describe an entity | No — edit the XML, or ship a `Languages/` override |
| Re-tune numbers (costs, weights, scores) | No — edit the XML attribute |
| Add / remove a religion, title, lifestyle, … | No — add or drop an XML row |
| Re-mix which behaviour an entity uses | No — change its `behavior` / `type` / `key` attribute to another built-in |
| Invent a genuinely new *algorithm* (a new rite, succession, casus belli win condition) | Yes — a small companion mod registers a new behaviour key; XML can then reference it |

The XML carries data and picks behaviour from a fixed menu of named
keys. It never carries code. That boundary is what keeps content mods
safe and forward-compatible — see
[Behaviour and registries](../dev-reference/structural-schema.md#behaviour-and-registries)
in the schema reference.

### The one rule

An `id` is a public contract. Once shipped it is referenced by saves,
by translations, and by other mods — never rename one. Everything else
about a row is fair game to change.

The full per-category field reference, the override and variable-size
list rules, and the registry list are all in the
[structural schema reference](../dev-reference/structural-schema.md). Its
companion, [localization-schema.md](../dev-reference/localization-schema.md),
covers translating or overriding the player-facing text.

---

## Core philosophy: BK decides, vanilla executes

The single most important rule when writing for BK:

> **BK is a decision-making and state-recovery layer on top of vanilla.
> It does not own movement, pathfinding, simulation, or game-state
> mechanics — Bannerlord does. Whenever a feature would have BK roll
> its own version of something the engine already does, wire vanilla
> primitives together instead of reimplementing.**

This sounds obvious. It isn't. Most of BK's historical bug surface
came from BK reimplementing engine machinery, getting it 90% right,
and then drifting from vanilla in subtle ways — the rest of vanilla
keeps changing around the BK reimplementation, mods that subclass
vanilla expect vanilla behaviour, players see weird state, and BK
eventually has to maintain a parallel reality forever.

The shipping rewrite is the textbook case. The previous implementation
had:

- A custom `SetTravel` timer-teleport.
- A shadow `sailing` dictionary mirroring `IsCurrentlyAtSea`.
- A custom price calculation.
- A custom arrival-time formula.
- A `bk_shipping_wait` menu that fast-forwarded campaign time.
- A `FinishTravel` that teleported parties on arrival.

It worked. But it produced "boats sailing over land", caravans that
went invisible mid-voyage, save state that diverged from vanilla, and
required an entire rescue subsystem to clean up parties whose state
had drifted. Replacing the whole thing with two vanilla primitives:

```csharp
caravan.SetSailAtPosition(startPort.PortPosition);
caravan.SetMoveGoToSettlement(nextHop, NavigationType.Default, false);
```

…eliminated all of those bugs simultaneously. Vanilla naval transit
already exists. BK contributes which port to sail to and when.

Apply this principle to everything you build on top of BK.

## What "decision" means in practice

BK adds value as the layer that decides:

- **Order** — what graph node a caravan visits next, what title
  succession rule applies, which estate on a clan gets income this
  tick, which settlement should host this week's tournament.
- **Eligibility / policy** — can this clan adopt this religion, can
  this hero declare a casus belli, does this contract change qualify
  for a free vote.
- **Recovery** — detecting that a party is in a stuck state and
  un-sticking it; detecting that a clan finance model is bypassing the
  estate-income hook and adding a backstop.
- **Aggregation / scoring** — combining many vanilla signals into a
  composite (e.g. the redirect's risk-weighted Dijkstra is BK; the
  raw distances are vanilla).

All of these are *informational* or *control-flow* decisions. They
don't move parties, they don't compute paths, they don't sum up gold.
They figure out *what should happen* and ask vanilla to do it.

## What "executes" means in practice

Vanilla owns the verbs:

| Verb | Vanilla primitive |
|---|---|
| Walk to settlement (auto-enter) | `MobileParty.SetMoveGoToSettlement(target, NavigationType.Default)` |
| Walk to a point (no enter) | `MobileParty.SetMoveGoToPoint(CampaignVec2(pos, isOnLand=true), NavigationType.Default)` |
| Sail to a port | `MobileParty.SetSailAtPosition(port.PortPosition)` then `SetMoveGoToSettlement(target, Default)` |
| Drop a party into a settlement | `EnterSettlementAction.ApplyForParty(party, settlement)` |
| Eject a party from a settlement | `LeaveSettlementAction.ApplyForParty(party)` |
| Move gold | `Hero.ChangeHeroGold(±n)`, `Clan.AddRenown(±n)`, `party.PartyTradeGold ±= n` |
| Reachability probe | `Campaign.Current.Models.MapDistanceModel.GetDistance(...)` |
| Terrain query | `Campaign.Current.MapSceneWrapper.GetTerrainTypeAtPosition(pos)` |
| Settlement entry / exit hooks | `CampaignEvents.OnSettlementEntered`, `CampaignEvents.OnSettlementLeft` |
| Hourly / daily / weekly | `CampaignEvents.HourlyTickEvent`, `DailyTickEvent`, `WeeklyTickEvent` |
| Trade decisions | `CaravansCampaignBehavior.ThinkNextDestination` (reflectively, since it's private) |

If you're about to write `party.Position = ...`, `// custom path
follower`, or `if (timer >= arrivalHours) { teleport to dest; }` — stop
and look for the vanilla primitive instead.

## The checklist

Before merging any new BK code that touches gameplay state, run this
mentally:

1. **Could the engine answer this?** If you're computing reachability,
   distance, terrain class, faction relations — vanilla almost
   certainly already exposes a method for it. Find the method.
2. **Could the engine do this?** If you're moving a party, transferring
   gold, applying a relation change, entering a settlement — there's a
   vanilla `Action` or setter for it.
3. **Am I about to write a parallel implementation?** If the answer is
   "yes, but mine is faster / has a feature vanilla doesn't" — almost
   always the right move is to *layer* on vanilla rather than replace.
4. **Am I mirroring engine state?** Shadow dicts that track what
   `IsCurrentlyAtSea` / `IsActive` / `CurrentSettlement` already say
   are a smell. Read the engine; don't duplicate.
5. **What happens on save load?** If your feature requires non-trivial
   `SyncData` to keep in lock-step with vanilla, that's a sign the
   feature is mirroring rather than layering. Layered features don't
   need to persist their own state — vanilla persists the truth.
6. **What happens when another mod modifies the same vanilla state?**
   If your code only works when nothing else is in the loop, you've
   built an island. Vanilla state is a coordination point; honour it.

## Worked example: shipping

The current shipping architecture, end-to-end, as a reference:

```
caravan triggers AfterSettlementEntered (vanilla event)
  └── BK reads CaravansCampaignBehavior.ThinkNextDestination (vanilla)
       to learn the *intended* destination
  └── BK runs Dijkstra over ShippingGraph (BK)
       to pick the *next graph hop* toward that destination
  └── if next hop is a port reachable by sea:
        caravan.SetSailAtPosition(...)        ← vanilla
        caravan.SetMoveGoToSettlement(...)    ← vanilla
        engine's naval pathfinder routes around peninsulas (vanilla)
        boat sails visibly across map (vanilla)
        on port arrival → AfterSettlementEntered fires again
        repeat from step 1 with the new port
  └── if next hop is a non-port intermediate fief:
        caravan.SetMoveGoToPoint(gate, isOnLand=true)  ← vanilla
        engine's land pathfinder picks the road (vanilla)
        on gate-arrival proximity → BK's AdvanceHopByHopWaypoints
        re-runs the routing helper for the next hop
  └── if final destination:
        caravan.SetMoveGoToSettlement(dest)   ← vanilla
        vanilla auto-enter at gate proximity (vanilla)
        BK trade pipeline runs at destination (BK)
```

BK in this flow:
- Owns the shipping graph, including auto-derived sea edges (KNN over
  ports, vanilla-naval-pathfind validated).
- Owns the *order* of node traversal.
- Owns the rescue sweep that catches parties in stuck states.

Vanilla in this flow:
- Owns every `Position` change.
- Owns the at-sea state (`IsCurrentlyAtSea`).
- Owns the settlement-entry mechanics.
- Owns the trade decision (`ThinkNextDestination`).
- Owns disembark on port arrival (`DisembarkAIOnPortArrival` is BK
  glue but the actual flag flip and exit placement are engine-driven).

## Worked example: rescue sweep

`UnifiedRescueSweep` (in `BKShippingBehavior.cs`) is BK's main
state-recovery surface. It walks `MobileParty.All` once per day and
applies fixes. Every fix is a vanilla primitive:

| Signature | Detection | Recovery |
|---|---|---|
| A: BK shipping limbo | `IsCaravan && !IsActive` | `party.IsActive = true; party.Ai.EnableAi(); party.IsVisible = true; party.Party.UpdateVisibilityAndInspected(...)` — all vanilla setters |
| B: AI-disabled caravan | `IsCaravan && Ai.IsDisabled` | Same recovery as A |
| C: Boat on land | `IsCurrentlyAtSea && terrain not water` | `party.IsCurrentlyAtSea = false` — vanilla setter |
| D: Lord land-mode over water | `!IsCurrentlyAtSea && naval-capable && terrain water` | `party.IsCurrentlyAtSea = true` — vanilla setter |
| F: Caravan land-mode over water | `IsCaravan && !IsCurrentlyAtSea && terrain water` | `EnterSettlementAction.ApplyForParty(party, nearestPort)` — vanilla action |
| Stuck-on-coast | `progressStuckTicks ≥ threshold` | `EnterSettlementAction.ApplyForParty(party, nearestRescueTown)` — vanilla action |

The rescue sweep is pure decision: it looks at engine state, decides
"this party is broken in pattern X", and undoes the broken state via
the engine's own setters and actions. It never custom-paths, never
sets `Position` directly, never simulates movement.

This is what good BK code looks like: lots of conditional logic
(decision), tiny one-line vanilla calls (execution).

## Anti-patterns

These produce bugs you will then have to spend weeks chasing. Don't
do them.

### Custom path geometry

```csharp
// BAD
for (int i = 1; i < samples; i++)
{
    var p = lerp(from, to, i / (float)samples);
    if (terrain[p].IsLand) return false;  // edge crosses land?
}
```

You're sampling the navmesh by hand. The engine has a pathfinder.
Ask the pathfinder.

```csharp
// GOOD
float d = MapDistanceModel.GetDistance(a, b, false, false, NavigationType.Naval);
if (float.IsInfinity(d)) return false;  // engine says no naval route
```

### Custom travel timers

```csharp
// BAD
party.IsActive = false;  // hide
sailing[party] = (destination, CampaignTime.Now + days);
// ... later ...
if (CampaignTime.Now >= sailing[party].arrival) {
    EnterSettlementAction.ApplyForParty(party, dest);
    sailing.Remove(party);
}
```

You've reimplemented sailing as a teleport with a timer. Caravans go
invisible, save state diverges, vanilla state isn't updated, mods that
expect to see at-sea parties on the map don't see them.

```csharp
// GOOD
party.SetSailAtPosition(port.PortPosition);
party.SetMoveGoToSettlement(dest, NavigationType.Default, false);
// engine sails the party visibly; vanilla auto-enters at arrival.
```

### Shadow state mirrors

```csharp
// BAD
private Dictionary<MobileParty, bool> bkAtSea = new();
private Dictionary<MobileParty, Settlement> bkCurrentSettlement = new();
// SyncData persists both, of course.
```

`MobileParty.IsCurrentlyAtSea` and `MobileParty.CurrentSettlement`
already exist. Reading them costs a property access. Storing your own
copies means you have to keep them in sync with vanilla, which you
will fail to do consistently.

```csharp
// GOOD
if (party.IsCurrentlyAtSea && party.CurrentSettlement == null) { ... }
```

### Direct position writes for routing

```csharp
// BAD (in a routing context)
party.Position = port.GatePosition;
party.SetMoveGoToSettlement(target);
```

You've snapped the party to a position the navmesh may not consider
walkable. Subsequent pathfinds return Infinity and the party is stuck.

```csharp
// GOOD
EnterSettlementAction.ApplyForParty(party, port);
// vanilla handles the placement; on next exit, the party is on a
// known-walkable tile.
```

### Bypassing vanilla AI for AI parties

```csharp
// BAD
party.Ai.DisableAi();
// ... custom movement loop ...
```

You've turned off the engine's brain for this party and now have to
provide a complete replacement. That replacement won't handle siege
state, war state, faction transitions, army linkage, or anything else
vanilla AI does. Use redirect / target hints instead:

```csharp
// GOOD
party.SetMoveGoToSettlement(suggestedTarget, NavigationType.Default, false);
// vanilla AI takes the suggestion and re-evaluates if circumstances change.
```

## Compatibility shims

The principle has one explicit exception: **shims that exist because
vanilla *can't* do the thing in that environment.** The canonical
example is BK's `SetTravel` timer-teleport, which BK keeps for users
running without War Sails (NavalDLC). Without War Sails the engine
has no naval pathfinder and no `IsCurrentlyAtSea` semantics, so a BK
fallback is the only way to provide ship travel at all.

When you write a shim:

1. **Gate it on the environment that requires it.** Use
   `BannerKings.Utils.ModCompat.WarSails` (or add a similar flag for
   your case). The conditional must be auditable from one place.
2. **Document the gate in code comments.** Future maintainers should
   see "this is a fallback for X" without having to dig.
3. **Mirror the vanilla API, don't extend it.** A shim that vanilla
   could replace one-for-one is healthy. A shim that's also adding
   features is a parallel implementation in disguise.
4. **Plan for removal.** Shims are temporary by nature. If War Sails
   becomes a hard dependency or vanilla 2.x adds the feature
   natively, the shim should be deletable in one commit.

## Recipe — adding a new BK system

A typical BK feature touches an existing vanilla system in a
specific way. Steps that match the philosophy:

1. **Read the vanilla code first.** Open
   `bin/Win64_Shipping_Client/TaleWorlds.CampaignSystem.dll` in ILSpy
   and find the vanilla behaviour your feature interacts with. What
   events does it listen to? What state does it own? What actions does
   it use?
2. **Decide what the feature is *deciding*.** State the decision in
   one sentence. "BK decides which estate on a clan gets the next
   weekly income" is good. "BK calculates and assigns weekly income"
   is too broad — that's vanilla's job for the gold transfer.
3. **Find the right vanilla extension point.** Usually a
   `CampaignBehavior` subclass that listens to one or two
   `CampaignEvents`. Don't write a `Tick(float dt)` loop.
4. **Make the smallest possible state additions.** A
   `Dictionary<Hero, BKWhatever>` is a smell — usually whatever's in
   the dict could be a property of an existing vanilla object you
   already access. If you really need state, persist it via
   `SyncData` and clean it up in `OnMobilePartyDestroyed` /
   `OnHeroKilled` / etc.
5. **Add a rescue path.** If your feature can leave state in a broken
   shape, add a daily-tick check that detects the broken shape and
   reverts via vanilla setters. Mirror the existing
   `UnifiedRescueSweep` pattern.
6. **Add a cheat for testing.** `bannerkings.<your_feature>_dump` and
   `bannerkings.<your_feature>_force` style cheats let you reproduce
   the broken state at will. The shipping system has
   `bannerkings.dump_caravans` and `bannerkings.unstrand_party` as
   reference examples.

## Recipe — extending an existing BK system from a sub-mod

If you're writing a sub-mod that adds to BK rather than modifying it
in place:

1. **Read BK's source for the system you're extending.** All BK code
   is in `BannerKings/`. Find the behaviour class, the model class,
   and the data class for the system. Behaviours subscribe to events;
   models compute values; data classes hold per-entity state.
2. **Prefer subclassing models over patching behaviours.** BK's models
   (e.g. `BKEconomyModel`, `BKCouncilModel`) are designed to be
   subclassed and registered. Patching a behaviour with Harmony works
   but couples your sub-mod to the patch site.
3. **Read BK state through public APIs.** `BannerKingsConfig.Instance`
   exposes the manager singletons. Read titles via
   `TitleManager.GetTitle`, religions via
   `ReligionsManager.GetHeroReligion`, etc. Don't reach into private
   fields.
4. **Honour the vanilla-state-of-record convention.** If BK stores
   something redundant with vanilla (a known anti-pattern, not a
   pattern to follow), don't depend on the BK copy — read the vanilla
   value. BK will eventually fix the redundancy and your sub-mod
   should keep working.
5. **Use `ModCompat`.** If your sub-mod conflicts with another mod
   BK detects, follow the same acquiesce pattern BK uses (yield the
   conflict to the other mod's primary surface).

## Log loudly — but behind a toggle

When you build something non-trivial, **log everything that mattered
to the decision**. The hop chosen, the score it beat, the score it
lost to, why a candidate was filtered out, the final action issued.
Most of the hard bugs in BK history have been "the decision was
wrong" bugs — and the only way to debug those after the fact is to
have a log line that names the inputs the decision saw at the moment
it was made.

The strict rule: every one of those log paths is **gated behind a
per-feature MCM toggle that defaults to `false`**. With the toggle
off the logging path doesn't run at all — no string formatting, no
file I/O, no allocations on the hot path. With the toggle on, the
overhead is real and sometimes severe: the shipping-redirect log
runs synchronous `File.AppendAllText` per-party per-hourly-tick,
which on a ~5000-party campaign produced 25MB+ log files and
visible second-scale freezes in the wild. That's the whole point of
the gate — those traces are the only way to debug a "why did this
party do X?" report after the fact, but they cannot be the default.

Existing examples in `BannerKings/Settings/BannerKingsSettings.cs`:

- `LogRaidCaptureBehavior` — every capture decision (projection,
  cohort split, prisoners added) prints to info panel + `Debug.Print`
  with a `[BKRaid]` prefix.
- `LogShippingRedirect` — every hourly redirect decision (entry node,
  path picked, fallback reason) writes to
  `Configs/ModLogs/BK_redirect.txt`.
- `LogHourlyTickPerf` — per-behaviour wall-clock for the hourly tick,
  for finding the hot caller when daily ticks start to lag.

The pattern in code is "gate at the top of a tiny static helper, log
freely inside, swallow exceptions at the boundary":

```csharp
private static void LogRedirect(MobileParty party, string note, Settlement target)
{
    if (!BannerKingsSettings.Instance.LogShippingRedirect) return;
    try
    {
        string pos = party.CurrentSettlement?.Name?.ToString()
                     ?? $"({party.GetPosition2D.X:0.0},{party.GetPosition2D.Y:0.0})";
        string line = $"{CampaignTime.Now}  {party.Name} @ {pos} → " +
                      $"{target?.Name?.ToString() ?? "?"}: {note}";
        BannerKingsCheats.AppendDiagnosticLine("redirect.txt", line);
    }
    catch { /* never throw out of a logger */ }
}
```

The `if` is the gate. Inside the gate, log generously — names, ids,
scores, every branch you took, every branch you didn't. Future you
(or another contributor) will grep these lines a year from now and
the more context each line has, the faster the bug closes. The
trailing `catch` is non-negotiable: a logger that crashes the
campaign tick is worse than no logger at all.

A rough rubric: **if a player ever has to ask "why did my caravan do
X?", you should be able to tell them to flip a toggle, reproduce
once, and post the log.** That's only possible if the toggle exists,
defaults off, and prints enough context to answer the question
without re-running anything.

Don't:

- Log unconditionally. Even cheap formatting adds up across 5000+
  parties × 24 hourly ticks × 30 days. The default-off rule isn't
  optional.
- Default a toggle to `true` "just for this release". Players don't
  read changelogs and a noisy info panel will be the only thing they
  remember about the build.
- Reuse one toggle for two unrelated systems. Per-feature gates mean
  you can ask a reporter to enable exactly the trace you need without
  drowning the log in unrelated chatter.

## Conventions

- **Versioning** — `vMAJOR.MINOR.PATCH.BUILD`. Small fixes bump the
  build segment, substantive batches bump the patch segment, and
  save-incompatible or API-breaking changes bump the minor segment.
- **Wiki updates** — every player-visible change requires a wiki
  edit in the same commit or a follow-up. Pure-internal refactors are
  exempt.
- **Comments are for the WHY** — name your symbols well, don't
  paragraph the WHAT. Comments earn their place by explaining a
  hidden constraint, a workaround, or a non-obvious design choice.
- **Defensive try/catch around vanilla calls** — vanilla throws on
  edge cases (siege state, dead heroes, mid-tick faction transitions).
  Wrap calls that might throw and log/skip rather than crash the
  campaign tick.
- **Hourly / daily ticks are hot paths** — perf matters. Avoid
  allocating per-party per-tick. Cache aggressively. The
  `LogHourlyTickPerf` MCM toggle exists for measuring.

If you read one thing on this page, read [Core philosophy: BK decides,
vanilla executes](#core-philosophy-bk-decides-vanilla-executes). Every
other section is detail.
