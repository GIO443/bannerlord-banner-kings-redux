# BK TODO

## Open

- [ ] **Player caravan making no money** — `RealisticCaravanIncome` MCM
      toggle on by default may be too restrictive. Caravan only adds profit
      on entering owner-or-current settlement; if the caravan visits player
      towns rarely, profit stays 0. Verify via `BKTradeProfitBehavior`.
- [ ] **Wars lasting long time** — peace decision threshold or war exhaustion
      not triggering. Likely in `BKDiplomacyModel` or
      `KingdomDecisionProposalBehavior::ConsiderPeace` (we patch ConsiderWar
      but not ConsiderPeace).

---

# Village / Estate / Town economic rework

Layered decision system on top of vanilla settlement primitives. BK
decides (class / spec / industry tags + worker-fit math); vanilla
executes (existing village production, workshop, trade, recruitment).
Plugs directly into the population-driven food economy below (village
class determines food balance, town industry determines import demand).

## Hierarchy

```
Town       → Industry (Granary / Foundry / Loomhouse / Stable / Caravan Hub)
  ↓ biases bound-village class transitions; consumes raw → produces finished
Village    → Class (Cropland / Fibre Farm / Pastoral / Stud Farm / Extractive / Coastal Fishery)
  ↓ raw-good producer; food balance flows up; aggregates 3 estates
Estate     → Specialization (Yield / Quality / Sustained / Levy)
  ↓ unit of ownership; 3 slots per village (2 notable, 1 buyable)
Workers    → existing PopType (Slaves / Serfs / Craftsmen / Nobles)
             with Spec-fit + Class-fit multipliers on yield
```

**Strict rules:** villages are raw producers only (no industry).
Workshops live in towns. Estates pick spec, not job mix (no
micromanagement). Single source of truth per layer; aggregation
flows up only.

## Village classes (6 — derived from vanilla VillageType)

| Class | Folds in | Outputs | Food | Worker-fit (Slv/Srf/Crf/Nob) |
|---|---|---|---|---|
| Cropland | WheatFarm, OliveTrees, VineYard, DateFarm | grain, oil, wine, dates, papyrus, spice | +++ | +15/+10/-10/0 |
| Fibre Farm | FlaxPlant, SilkPlant | flax, silk | 0 | +10/+10/0/0 |
| Pastoral | CattleRange, HogFarm, SheepFarm | meat, wool, hides, eggs | ++ | +5/+15/0/+5 |
| Stud Farm | 6 HorseRanch variants | horses (mounts) | 0 | -10/+5/+5/+15 |
| Extractive | IronMine, SilverMine, SaltMine, ClayMine, Lumberjack | iron, silver, gold, salt, clay, limestone, marble, hardwood, mead | -- | +20/+5/0/-10 |
| Coastal Fishery | Fisherman | fish, garum, whale meat, purple dye | + | 0/+15/0/0 |

Class is set from village's vanilla `VillageType` at session start.
Stable; only changes via village-owner decree (very rare, very costly,
multi-year — Cropland → Pastoral via Enclosure-style policy).

## Town industries (5)

| Industry | Workshops favored | Consumes | Produces |
|---|---|---|---|
| Granary | Brewery, Olive Press, Wine Press, Mill | grain, olives, grapes, dates | ale, oil, fine wine, flour |
| Foundry | Smithy, Charcoal, Tool Shop | iron, hardwood, salt | tools, weapons, armor |
| Loomhouse | Linen/Wool/Silk/Velvet Weavery | flax, silk, wool, dye | linen, wool cloth, velvet |
| Stable | Tannery, Saddlery, Smithy (horseshoes) | horses, hides, iron | warhorses, saddles, leather goods, leather armor |
| Caravan Hub | Jeweler, Perfumery, Glasswork (+ trade-volume bonus) | silver, gold, salt, clay, spice, dye, oil, mead | jewelry, perfume, fine glassware |

Industry derived from current workshop mix at session start; player
can change on owned towns at high cost (workshops convert gradually,
~6 months in-game). AI town owners re-evaluate yearly with high
stickiness.

**Foundry implicitly covers shipyard** (coastal Foundry already wants
iron + lumber). **Caravan Hub competes with Foundry for silver/salt** —
healthy regional tension, no design fix needed.

## Estate specializations (4 — same set across all village classes)

| Spec | Output volume | Quality grade | Food balance | Recruits | Worker bias |
|---|---|---|---|---|---|
| Yield | +++ (1.60×) | low (0.85×) — bulk | tanks own (-0.20) | none | slave-heavy |
| Quality | + (0.80×) | +++ premium (1.60×) | neutral | none | craftsman-heavy |
| Sustained | ++ (1.10×) | normal (1.00×) | + (0.20) | small (0.25×) | serf-heavy |
| Levy | + (0.85×) | normal (1.00×) | neutral | +++ (1.50×) | serf + noble |

Same enum across every class — what changes is the flavor of the
output (Yield Cropland = bulk grain; Yield Extractive = pig iron;
Quality Cropland = vintner's reserve; Quality Stud = warhorse).
Players learn the four labels once.

Gold-axis (volume × quality): Yield 1.36, Quality 1.28, Sustained 1.10,
Levy 0.85. Yield wins on bulk; Quality wins on per-unit margin
(important in Loomhouse / Stable / Caravan Hub clusters); cluster fit
picks the winner in any given context.

## Cluster synergy matrix (town industry × village class supply)

| Industry wants | Class to supply | Estate spec for synergy |
|---|---|---|
| Granary | Cropland | Yield (mass ale) + Quality (fine wine) |
| Foundry | Extractive (iron, wood, salt) | Yield |
| Loomhouse | Fibre Farm + Pastoral (wool) + Coastal (dye) | Quality |
| Stable | Stud Farm + Pastoral (hides) + Extractive (iron) | Quality |
| Caravan Hub | Extractive (silver/gold/salt) + Cropland (spice/oil) + Coastal (dye) | Quality |
| (any, in food crisis) | any food-positive | Sustained |
| (any, in wartime) | any | Levy |

Inter-cluster trade reuses the existing slave-caravan primitive
(extended to ship raw goods). Clusters with mismatched supply pull
imports from neighbors.

**Cluster definition:** a village's cluster = its current
`Village.TradeBound.Town` plus all other villages with the same
`TradeBound`. Recompute on `OnSettlementOwnerChanged` / on rebellion
events that re-bind villages. Unbound villages have no cluster (rare —
mostly transient post-rebellion state) and are treated as a degenerate
1-village cluster with no town demand until rebound.

## Landlord-of-landlords ownership

```
Village fief — held by a Lord (kingdom hierarchy)
  ├── Estate slot 1 — owned by Notable A (default, BK existing)
  ├── Estate slot 2 — owned by Notable B (default, BK existing)
  └── Estate slot 3 — purchasable (existing BK)
```

Village owner taxes all 3 estates (flat % within demesne-law cap).
Village owner can buy the available estate slot → collects estate
production income + tax (tax loops back to themselves but is
accounted on the tax line for clean books).

Income flow per estate:
```
Raw output → village→town caravan → gold (volume × quality × industry-demand)
  − estate upkeep
  − food purchase (Yield/Quality net food-negative)
  − village tax → village owner
  = net to estate owner
```

Village owner's tax rate is a real lever — too high and notables
drift to Sustained/exit; too low and village treasury can't fund
Growth decree or upgrades. Sweet spot exists.

Notable estate spec defaults from notable role:
- Gang leader → Yield
- Headman → Sustained
- Merchant / Artisan → Quality
- Rural notable / preacher → Sustained

Player relation with notables shifts their spec slowly toward what
village owner is signaling.

## AI lord economic decisions (intent → spec response)

Re-evaluate per-clan every 30 in-game days OR on big-event hooks
(war, settlement gain/loss, army-decision pending). At most one spec
change per lord per evaluation. 60-day cooldown on changed estate.
Hysteresis on every trigger.

Priority order (first matching wins):

1. **Personal levy crisis** — at war AND own party < 70% size limit
   OR kingdom army-decision pending → flip 1 estate to **Levy**
2. **Bankruptcy risk** — runway < 30 days, OR `Clan.Gold < Clan.Tier × 3000`
   → flip 1 estate to **Yield**
3. **Cluster food crisis** — bound town cluster food < 0 for ≥ 2
   evals AND lord owns **food-class** estate (Cropland / Pastoral /
   Coastal Fishery) in cluster → flip to **Sustained**.
   ⚠ Trigger only fires when at least one food-class village exists
   in the cluster. Pure non-food clusters (e.g. all-Extractive) can't
   self-fix via re-spec; they depend on Phase 5 inter-cluster food
   imports.
4. **Quality opportunity** — bound town is Loomhouse/Stable/Caravan Hub
   AND estate class supplies it → flip to **Quality**
5. **Wartime baseline** — at war, none above → bias **Yield**
6. **Peacetime baseline** — none above → hold (sticky)

Vanilla signals consumed (no new computation):
`Kingdom.IsAtWarWith`, `MemberRoster.TotalManCount`/`PartySizeLimit`,
`Clan.WarPartyComponents.Count`, `Kingdom.UnresolvedDecisions`,
`Clan.Gold`, `DefaultClanFinanceModel.CalculateClanIncome`,
`Settlement.IsUnderRaid`/`IsUnderSiege`, plus BK-computed cluster
food balance + town industry alignment.

AI town-industry change: yearly, only if mismatch persists 2+ years
AND prosperity dropping. Sustained 3+ year wartime biases Foundry/
Stable on owned towns where village classes support.

AI village-owner tax: yearly. <60-day runway → +5% tax (within
demesne-law cap). >180-day runway → −5%. No other signal.

## Player levers (six total surfaces, mostly low frequency)

| Scope | Decision | Cadence |
|---|---|---|
| Estate (owned) | Specialization (1 of 4) | rare, cost + cooldown |
| Estate (owned) | Buy/sell (existing BK) | rare |
| Village (owner) | Tax rate (within demesne-law) | adjust freely |
| Village (owner) | Growth decree (hearth+, output−, multi-year) | rare, costly |
| Village (owner) | Class transition (Cropland → Pastoral, etc.) | very rare, very costly |
| Village (owner) | Cultivate notable to nudge their spec | gradual via relation |
| Town (owner) | Industry pick (1 of 5) | rare, cost + cooldown |
| Town (owner) | Cluster export opt-in | toggle |

## Phase plan

- [x] **Phase 0 — data model.** ✅ Landed. `BannerKings/CampaignContent/
      Economy/Layered/`: `VillageClass` + `TownIndustry` + `EstateSpec`
      enums; `DefaultVillageClasses.GetClass(VillageType)`;
      `DefaultTownIndustries.GetIndustry(WorkshopType)` +
      `InferIndustry(Town)`; `DefaultEstateSpecs.ForOwner(Hero)` /
      `ForNotable(Hero)`; `EstateYieldTables` with `SpecOutput`,
      `WorkerFit`, `IndustryDemand`, `FoodBalancePer100` tables.
      Build clean (0 errors). Nothing wired yet. Spec balance
      rebalanced post-review (Yield 1.60×0.85=1.36, Quality
      0.80×1.60=1.28).
- [x] **Phase 1 — assignment.** ✅ Landed on `economy-phase-1` branch.
      `LayeredEconomyAssignmentBehavior` walks all settlements +
      estates on `OnGameLoaded` / `OnSessionLaunched` and writes
      `VillageClass` / `TownIndustry` / `EstateSpec` from
      `Default*` lookups when the persisted field is `Unset`.
      Idempotent. SaveableProperty fields added on `LandData` (idx 8),
      `EconomicData` (idx 5), `Estate` (idx 15); enums registered in
      SaveDefiner (1110/1111/1112). `LayeredEconomyExtensions` is
      the single read access point — `village.GetVillageClass()`,
      `town.GetTownIndustry()`, `estate.GetSpec()`,
      `village.GetClusterTown()`. Yield-side math NOT touched
      (Phase 2 wires it). UI badges deferred to Phase 7 (where all
      UI lands together).
      Validation cheats:
        - `bannerkings.dump_economy_state` → BK_economy_state.txt
          (every village class, town industry, estate spec + summary)
        - `bannerkings.snapshot_clan_income <tag>` → BK_clan_income_<tag>.txt
          (regression baseline; run before/after each phase)
        - `bannerkings.classify_village <id>` (one-shot diagnostic)
      Behavior also writes daily-summary lines to BK_economy_assignment.txt
      on every assignment pass (load / session / owner-change events).
      ⏳ NOT YET DONE: refactor `VillageExtensions.IsMineVillage` /
      `IsFarmingVillage` / `IsAnimalVillage` / `IsHorseRanch` to
      delegate to `village.GetVillageClass()`. Deferred to Phase 2
      when those callers' yield math also gets routed; the behavioral
      change should land with the math, not before it.
- [ ] **Phase 2 — yields.** Spec + class + worker-fit math feeds BK
      income/pop calcs through one `EstateYieldCalculator`. Compare
      total yields against pre-rework baseline; tune to match within
      ±10% (no regression). Share food calibration with food rework:
      add `BKFoodConsumptionModel.GetVillageDailyConsumption(pop, classMix)`
      and have `EstateYieldTables.FoodBalancePer100` and the food sim's
      villageNet both route through it. No two parallel "per-100"
      constants.
- [ ] **Phase 3 — cluster aggregation.** Town panel shows cluster
      overview: bound village classes, food balance, industry-fit %.
      Apply cluster bonuses/penalties based on industry × class
      alignment.
- [ ] **Phase 4 — food deficit gating.** Hooks the food rework
      (Phase 2 of food sim). Negative cluster food balance → Extractive /
      Fibre / Stud estates take stagnation penalty. Cluster food
      surplus → exportable.
- [ ] **Phase 5 — inter-cluster trade.** Extend slave-caravan
      primitive to ship raw goods between food-surplus and
      food-deficit clusters. Naval clusters via shipping graph.
      ⚠ Add a `Travel.Kind` (or equivalent discriminator) to the
      caravan struct so the existing slave-caravan rescue logic
      (`BKShippingBehavior` LoadCleanup slave-orphan branch, etc.)
      doesn't accidentally clean up raw-goods caravans. Audit every
      `slavecaravan_` StringId check site before adding new cargo
      kinds.
- [ ] **Phase 6 — AI policy.** `EstatePolicyAI` lord decision module
      with the 6-priority trigger ladder + 30-day cadence + hysteresis.
      AI village-owner tax adjustment. AI town-industry annual review.
      Optional: religion-aware spec selection (Druidism averse to Yield
      slave-heavy; Aserai faith averse to Pastoral Hog) — defer if
      complexity grows.
- [ ] **Phase 7 — player levers.** UI for estate spec pick (with
      cluster-aware suggestion), village tax rate slider (within
      demesne-law bounds), Growth decree menu, Town Industry pick.
- [ ] **Phase 8 — village-class transition + Growth decree.** Long-form
      multi-year policies. Cropland ↔ Pastoral / Cropland → Cropland
      Growth-mode / etc. Reuses demesne-law contract change cadence.

Each phase is reviewable on its own merits and save-compatible
(new fields default to `Unset` → AI picks on next eval).

## Review feedback integrated (post Phase 0)

Tracked here so future-phase work has the design corrections in one
place. Items resolved in Phase 0 are marked ✅; items deferred to
later phases are tagged with their target phase.

### Phase 0 resolutions ✅

- ✅ **#1 Spec gold-yield asymmetry.** Old: Yield 1.50×0.70=1.05,
  Quality 0.85×1.50=1.275 — Quality dominated despite Yield being
  labeled +++. **Fixed:** Yield 1.60×0.85=1.36, Quality 0.80×1.60=1.28.
  Yield now wins on bulk gold, Quality wins on per-unit margin.
- ✅ **#2 Sustained on non-food classes — semantics documented.**
  Sustained.Food = +0.20 doesn't close Extractive's -0.80 deficit by
  design (mine isn't a farm). Comment added in `EstateYieldTables`
  explaining: on food-positive class, Sustained lifts surplus; on
  food-negative class, Sustained mitigates but never closes the gap.
- ✅ **#11 InferIndustry tie-break clarified.** Comment added: first
  iteration always falls into strict-greater branch because Unset
  entries are filtered before the vote; canonical-order tie-break
  ((int)Key < (int)winner) only matters from second iteration onward.
- ✅ **#12 GetClass StringId fallback rationale documented.** Comment
  added: covers both modded village types AND the static-field-init-
  timing window (per the v1.6.9.x DefaultVillageTypes init memory
  note). Lumberjack/fisherman string-checks aren't dead code.

### Phase 1 obligations

- ⏳ **#8 Income regression test.** Dump per-clan daily income
  pre-Phase-1 vs post-Phase-1. Phase 1 promises "income unchanged";
  if it actually drifts, fix before Phase 2 buries it under further
  changes.
- ⏳ **Single access point discipline.** Once Phase 1 writes
  `VillageClass` onto `Village` / `LandData`, no other call site
  may read `VillageType` for "what kind of village is this" purposes.
  `VillageExtensions.IsMineVillage` etc. should delegate to
  `village.GetVillageClass() == VillageClass.Extractive`. Track and
  refactor in Phase 1.

### Phase 2 obligations

- ⏳ **#6 Single food-calibration helper.** Add
  `BKFoodConsumptionModel.GetVillageDailyConsumption(pop, classMix)`
  and route both `EstateYieldTables.FoodBalancePer100` and the food
  sim's `villageNet[Basic]` through it. Two parallel "per-100"
  constants will drift — refuse to ship Phase 2 with the duplication.

### Phase 3 obligations

- ⏳ **#4 Cluster definition explicit.** Cluster = village's current
  `Village.TradeBound.Town` + all other villages with same
  `TradeBound`. Recompute on `OnSettlementOwnerChanged` and on
  rebellion-driven re-bindings. Unbound village = degenerate 1-village
  cluster with no town demand until rebound. Documented in design
  above; enforce in code at Phase 3.

### Phase 5 obligations

- ⏳ **#7 Caravan kind discriminator.** Before extending the
  slave-caravan primitive to raw-goods cargo, add a `Travel.Kind`
  (or equivalent) to the caravan component. Audit
  `BKShippingBehavior` LoadCleanup, all `slavecaravan_` StringId
  checks, the rescue / orphan paths. Don't ship Phase 5 until raw-
  goods caravans are excluded from slave-caravan-specific cleanup.

### Phase 6 obligations

- ⏳ **#3 Trigger #3 fires only with food-class villages in cluster.**
  Documented in priority list above. AI policy must check cluster
  has at least one Cropland/Pastoral/Coastal village before
  triggering Sustained re-spec; otherwise the lord's only recourse
  is import (Phase 5).
- ⏳ **#10 Religion-aware spec.** Optional — Druidism ↔ Yield
  (slave-heavy), Aserai faith ↔ Pastoral with Hog. Implement only
  if it doesn't add a tangle of edge cases; otherwise defer.

### Future tuning (post-Phase-7 playtest)

- 🔍 **#5 Cultural multipliers.** Optional small per-culture multiplier
  on a class-fit (Khuzait +5% StudFarm, Aserai +5% Cropland date/spice,
  Vlandian +5% Quality on manors). Lore depth without a new lever.
  Land it once base economy is stable and we can tell signal from
  noise.
- 🔍 **#9 Pottery in CaravanHub.** Pottery uses clay (Extractive
  supplies) → fits CaravanHub. Mechanically pottery is a basic utility
  good more than a luxury. Justification: pottery is the cheapest
  CaravanHub workshop, anchors Hub presence in low-prosperity towns.
  Verify in playtest; if pottery routinely keeps a town flagged Hub
  when its real character is Foundry/Granary, downweight pottery
  in `InferIndustry`.

## Open questions deferred to phase resolution

- **Workshop conversion cost** when town industry changes — gradual
  with prosperity drag (AI default), or hybrid gold-accelerated
  (player option)? Decide at Phase 7.
- **Caravan Hub × Foundry silver competition** — observe in playtest
  before tuning. Healthy tension first; rebalance if a region
  consistently starves one of the two.
- **Lobbying town owner for industry choice** when you own bound
  villages but not the town — defer past phase 7. Strict ownership
  scopes for now.
- **Stud Farm noble worker fit** at +15% — re-tune to +5% with
  noble-as-quality-multiplier if noble supply becomes a bottleneck
  in playtest.

---

# Population-driven food economy

## Phase 1 ✅ shipped

Live in v1.6.9.27+:
- `BKFoodConsumptionModel` — per-class daily food consumption rates
  (Slaves 0.05, Serfs 0.07, Tenants 0.09, Craftsmen 0.10, Nobles 0.12).
  Mid-food fraction by class. Luxury gated on prosperity ≥ 3000.
- `BKMarketExportSink` — caps food stockpile at 14 days × per-pop demand,
  exports excess at 0.4× market price to town owner gold + small
  prosperity bump. MCM toggle `MarketSurplusExport` (default true).
- BK `MakeConsumption` rewired to use pop-driven food demand units
  instead of vanilla's prosperity × priceIndex budget. MCM toggle
  `PopulationDrivenFood` (default true).
- Coverage → satisfaction table (Food + Luxury channels). Numbers
  dialed back so single famines are recoverable, rebellions only
  fire when famine stacks with bad governance / war / culture
  conflict.
- Famine state machine: enter at <0.2 basic coverage for 7 days,
  exit at >0.5. In-game log message on entry / exit (player
  faction only). Daily destabilizing hit while active: -0.2
  loyalty, -0.15 security, -0.002 stability. **No pop death** —
  pop only dies from raids/sieges/plague/war.
- `DeleteOverProduction` MCM hint corrected to reflect what vanilla
  actually does (player-crafted weapon + 5%/day modifier-item
  cleanup).

## Open phases

- [ ] **Phase 2 — village ↔ town food flow virtual accounting** —
      compute villageNet[cat] = production - village's own consumption
      per day. For deficit villages (Mining, Forestry, Smithy), generate
      a virtual import order against the bound town's stockpile. Failed
      imports → village hunger satisfaction penalty. Track flows in
      `EconomicData` for visibility.
      ⚠ Share calibration with the village/estate rework: production
      side comes from `EstateYieldTables.FoodBalancePer100`,
      consumption side from `BKFoodConsumptionModel`. Both call one
      shared helper (see Phase 2 obligations in the rework section).
- [ ] **Phase 2b — caravan flow into the new economic sim** —
      caravans should arbitrage real demand signals, not just vanilla
      price index. Pick up surplus where the export sink would
      otherwise dump, deliver to deficit settlements. Profit reflects
      sim-faithful margins: cheap-buy from surplus town, sell-high in
      deficit town. Specifically:
        - caravan target picker reads town's `FamineActive` /
          per-tier coverage when ranking destinations (deficit
          settlements get score boost)
        - export sink and caravan share the same surplus pool: an
          item the caravan picked up isn't double-counted as exportable
        - village → caravan trade if the village has surplus and no
          bound-town import demand
        - tie into existing `BKCaravansBehavior` `ThinkNextDestination`
          rather than building a parallel scoring function
- [ ] **Revisit market export sink** — once Phase 1 is live for several
      sessions:
        - tune `bufferDays` (likely 7–21, possibly per-category)
        - tune `exportPrice` factor (0.3 / 0.4 / 0.5 — risk: AI clan
          income inflated, war chests too deep)
        - replace flat buffer with **storage-building-derived** buffer
          (Granary +X days, Marketplace boosts export volume)
        - decide gold split: town owner vs estate owner of supplier
          village vs realm crown (currently: town owner only)
        - extend the sink to non-food categories (cloth, leather, salt,
          hardwood) once food math is stable
- [ ] **Phase 3 — religious / lifestyle food preferences**
        - Aserai Faith: Hog forbidden
        - Druidism: vegetarian sects
        - Vlandian: wine demand bonus
        - Gladiator lifestyle: +20% Meat for nobles
        - Faith violation when forbidden food is consumed: -0.005 sat[Food]
- [ ] **Phase 4 — non-food market caps** — extend the export sink
      design to cloth / leather / hardwood / salt / clay / iron with
      their own per-category buffer days and wholesale rate
- [ ] **Phase 4b — famine → BK Demands hookup** — when famine sustained
      30+ days, fire "Hungry Peasants" demand via existing BK Demands
      pipeline. Lord can: import food (gold), lower taxes (loyalty
      short-term, gold loss), or ignore (continued attrition).
- [ ] **Phase 5 — storage-building integration** — Granary +N buffer
      days for food categories. Marketplace boosts export volume /
      gold per export. Replace flat 14-day buffer.
- [ ] **Phase 6 — UI surfacing** — population panel shows per-tier
      coverage, daily import/export flow, famine status. Settlement
      tooltip flags famine state.
