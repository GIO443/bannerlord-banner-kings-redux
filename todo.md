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
- [ ] **Charm gain too fast as mercenary (and possibly lordship)** —
      mercenary career charm XP scaling appears inflated; player gains
      charm levels far quicker than vanilla pace. Audit
      `BKMercenaryCareerBehavior` and `BKLordPropertyBehavior` for
      charm XP awards; check whether a per-tick or per-event multiplier
      stacks with vanilla skill XP, or if BK awards charm in a path
      vanilla doesn't. May also affect lordship career.
- [ ] **Wars at game start not using vanilla diplomacy** — initial
      kingdom wars on new campaign appear to bypass vanilla war
      declaration logic. Suspect `BKDiplomacyBehavior` or an init-time
      patch is force-declaring wars without going through
      `DeclareWarAction` / vanilla `ConsiderWar` scoring. Verify with
      a fresh campaign and grep for `DeclareWar` calls in BK init
      paths. Per BK-decides-vanilla-executes: BK should set up
      diplomatic state, not bypass the vanilla pipeline.
- [ ] **Estate displayed value ≠ purchase price** — UI shows one
      estate value but the gold deducted on buy is different. Audit
      the estate-purchase UI (likely `EstateVM` / estate panel mixin)
      vs the actual purchase action; the price model used by the
      transaction is probably different from the one rendered. Single
      source of truth: both display and purchase must call the same
      pricing helper.
- [ ] **Wisdom level resets to 0 on screen leave/return** — putting
      points into the Wisdom education attribute appears to persist
      visually only until the screen is closed and reopened, then
      reverts to 0. Likely an unsaved staging value in the education
      VM that isn't committed to `EducationData` on apply, OR a
      reload path that re-derives from a stale source. Check
      `EducationVM` apply flow + `EducationData.Wisdom` save field.

## Audit follow-ups (2026-05-08 deep audit)

Items the overnight audit flagged as real issues but intentionally
left alone — each requires more context or design work than a
mechanical fix allows. Do NOT batch these blindly with future
"clean up the audit findings" passes; each one has a real reason
it wasn't fixed in the v1.6.15.0 stability sweep.

- [ ] **`EconomyPatches.KingdomBudgetPrefix` returns `false` whenever
      TitleManager exists** (BannerKings/Patches/EconomyPatches.cs:651).
      Suppresses vanilla `AddIncomeFromKingdomBudget` for *every* clan
      forever. Commented-out original logic was conditional on
      `FeudalRights.Assistance_Rights`. Likely accidental dead code from
      the FeudalRights removal in 1.3.x — but BK has its own income
      pipeline (`BKClanFinanceModel`) that may already replace this, so
      restoring vanilla could double-count clan budget income. **Action**:
      walk `BKClanFinanceModel.CalculateClanIncome` and confirm whether
      the kingdom-budget transfer is BK-side now; if yes, document the
      suppression intent; if no, restore the conditional gate.

- [ ] **`EconomyPatches.CalculateClanExpensesInternalPrefix` does 6
      reflective `GetMethod` lookups per call inside the daily clan-
      expense path** (BannerKings/Patches/EconomyPatches.cs:582-643).
      Hot path on big saves. Caching the MethodInfos is mechanical, but
      the prefix is a parallel reimplementation of vanilla's expense
      pipeline (gated only on `payBudget`). Per the BK-decides-vanilla-
      executes principle, the right fix is to drop the parallel impl and
      reduce to a postfix that adds BK-specific lines on top of vanilla.
      **Action**: audit what BK actually adds vs vanilla and decide
      between (a) cache reflection + accept the parallel impl, or
      (b) rewrite as a postfix.

- [ ] **`BKShippingBehavior` shadow-state dict consolidation**
      (BannerKings/Behaviours/Shipping/BKShippingBehavior.cs).
      Audit recommends collapsing the 9 cooperating dicts (`sailing`,
      `redirectCache`, `progressTracker`, `lastRescueHour`,
      `lastRescueTown`, `rescueHistory`, `bkOptOutUntilHour`,
      `hopByHopState`, `lastPortLeft`, `walkingWaterLastLogHour`)
      into a single per-party state record. Would eliminate the
      duplicate-source-of-truth risk where `hopByHopState.intended`
      mirrors `TargetSettlement` (drift after `SetMoveGoToSettlement`,
      enter-settlement, or another mod's redirect). Multi-day rewrite
      with high regression risk; v1.6.15.0 fixed only the most acute
      thread-safety / leak issues. **Action**: design the single state
      record, plan a save migration path, then refactor.

- [ ] **`Components/BanditHeroComponent` SaveableField slots 10-13
      may collide with vanilla `BanditPartyComponent`**
      (BannerKings/Components/BanditHeroComponent.cs:19-22). The base
      class's saveable slots are typically <10 but slots 10-13 should
      be verified against the 1.3.x metadata using ILSpy/dotPeek on the
      installed game DLL. If a collision exists, bump the BK slots to
      the 100+ range matching `RetinueComponent` / `EstateComponent`
      (1001+) — but that's a save-incompat bump unless a migration is
      written. **Action**: verify with ILSpy first; only act if a real
      collision is observed.

- [ ] **Layered economy behaviors with empty `SyncData{}` blocks**
      (`LayeredEconomyAssignmentBehavior`, `ClusterFoodTracker`,
      `EstatePolicyAI`, `VillageDecreeManager`). Per-instance fields
      (decree timers, pending cluster moves, AI engagement bands) are
      silently dropped across save/reload — the daily-tick re-derives
      most of it from `EconomicData`/`EstateData`/`PopulationData`,
      but anything held only on the behavior instance is lost.
      **Action**: audit each for instance fields; either move them
      onto persisted Estate/EconomicData fields (preferred — keeps
      single source of truth) or add explicit `SyncData` calls + register
      any new types in `SaveDefiner.cs`.

- [ ] **`InvasionBehavior.invasions` not persisted** — v1.6.15.0
      reverted my `dataStore.SyncData` call after the audit critic
      flagged that `Invasion` isn't registered in `SaveDefiner.cs`.
      Currently the picked-invasion list rolls fresh each session.
      **Action**: register `Invasion` in `SaveDefiner.cs` (with a
      `[SaveableClass]` attribute and a container definition for
      `List<Invasion>`), then re-add the `SyncData` call. Verify a
      save→reload round-trip preserves the list.

- [ ] **`BKClanFinanceModel.cs` and other untouched models** — the
      audit only inspected the ~12 highest-leverage Vanilla model
      overrides. Models not in that batch (BKClanFinance,
      BKBattleSimulationModel, BKBanditModel, BKAgentDamageModel,
      BKCombatXpModel, BKLearningModel, BKRaidModel, etc.) may have
      similar `OwnerClan.Leader` / `?.Clan.Kingdom` / `data == null`
      gaps. **Action**: dispatch a focused agent on the remaining
      `Models/Vanilla/*.cs` files using the same prompt template, then
      patch root-cause NREs.

- [ ] **`EncyclopediaClanPageMixin.addedFields` reset is per-clan;
      verify other Encyclopedia mixins** — `EncyclopediaHeroPageMixin`,
      `EncyclopediaUnitPageMixin`, etc. likely have the same "stuck on
      first opened object" bug pattern (added fields once, never
      re-added when user navigates to a different object of the same
      type). v1.6.15.0 only fixed Clan. **Action**: grep for
      `addedFields` / `addedOnce` in `BannerKings/UI/Extensions/
      Encyclopedia/` and apply the same per-target reset pattern.

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
- [x] **Phase 2 — yields.** ✅ Landed on `economy-phase-2` branch.
      `EstateYieldCalculator` is the single source of yield-multiplier
      math: `GoldMultiplier(estate)` returns `Breakdown { SpecVolume,
      SpecQuality, WorkerFitMean, IndustryDemand, Final }`; `DailyFoodBalance(estate)`
      returns daily food units accounting for village-class baseline +
      spec contribution. Pure functions; no I/O; safe across threads.
      Wired into `EstateData.DailyProductionIncome` at exactly one site —
      multiplies `gross` before keepRate / payout. Other yield sites
      (taxes, recruitment) untouched in Phase 2 — Phase 3+ audit.
      MCM toggle `LayeredEconomyYields` (default OFF) gates the whole
      thing — opt-in until playtest validates regression baseline.
      Refactored: `VillageExtensions.IsFarmingVillage` /
      `IsAnimalVillage` / `IsRanchVillage` delegate to
      `village.GetVillageClass()`. `IsMiningVillage` deliberately kept
      narrow (mines only, excludes Lumberjack — different semantics
      from `VillageClass.Extractive`).
      Validation cheats:
        - `bannerkings.dump_estate_yields` → BK_estate_yields.txt with
          per-estate breakdown (vol × qty × fit = final) + food/day,
          plus a header line confirming the MCM toggle state.
      Industry-demand factor stays at 1.0 in Phase 2 (Phase 3 cluster
      aggregation will multiply it in once cluster fit computes).
      ⏳ Food calibration single helper (`BKFoodConsumptionModel.
      GetVillageDailyConsumption`) — still deferred. The food rework's
      Phase 2 will land that helper; both layered economy and the
      food sim should route through it then.
- [x] **Phase 3 — cluster aggregation.** ✅ Landed on `economy-phase-3`
      branch. `EconomicCluster.Compute(town)` aggregates a town with
      its bound villages: collects classes, computes IndustryFit
      ([0..1+] weighted average of `IndustryDemand(industry, cls)`
      across bound villages), sums daily food balance across all
      estates in the cluster. `EconomicCluster.IndustryDemandFactor(estate)`
      returns the per-estate cluster-fit multiplier (banded:
      1.20/1.10/1.00/0.85 by demand weight). Wired into
      `EstateYieldCalculator.GoldMultiplier` — Phase 2's placeholder
      `IndustryDemand=1.0` is now the cluster-fit value, applied
      under the same MCM toggle.
      Validation cheat:
        - `bannerkings.dump_clusters` → BK_clusters.txt with industry,
          IndustryFit, FoodBalance, bound village count, class
          distribution per town. Summary line counts healthy
          (fit≥0.75), mismatch (fit≤0.25), food-deficit clusters.
      No save schema changes (cluster computed on-demand, not
      persisted). Phase 5 caching with invalidation hooks if profiling
      shows the recompute is hot. Town panel UI deferred to Phase 7.
- [x] **Phase 4 — food deficit gating.** ✅ Landed on `economy-phase-4`
      branch. `ClusterFoodTracker` daily-ticks every town: increments
      `EconomicData.ClusterFoodDeficitDays` (new SaveableProperty 6)
      when `town.FoodChange < 0 && FoodStocks < 25%`, decrements on
      surplus days. Hysteresis via separate enter/exit thresholds
      (14d / 7d). Counter capped at 3× enter threshold so recovery is
      bounded.
      Stagnation factor (`ClusterFoodTracker.StagnationFactor`) wired
      into `EstateYieldCalculator.Breakdown.Stagnation` — multiplied
      into `Final` after IndustryDemand. 0.7× for non-food classes
      (Extractive/FibreFarm/StudFarm) when cluster is stagnant; 1.0×
      always for food-positive classes (Cropland/Pastoral/Coastal —
      they're the way out of the crisis, not the cause).
      Detection signal is **vanilla `town.FoodChange`** — not the
      Phase 3 cluster.FoodBalance, which is estate-side only. Vanilla
      already counts town consumption + village inflow + workshop
      consumption; we just observe its sign.
      Validation cheats:
        - `bannerkings.dump_food_status` → BK_food_status.txt with
          per-town foodChange, stocks, deficit days, state. Summary
          counts stagnant + recovering.
        - `bannerkings.test_force_deficit <town_id> [days]` — sets
          counter directly to test the gate without waiting for a
          real famine.
- [x] **Phase 5 — inter-cluster trade.** ✅ Landed on `economy-phase-5`
      branch. `CargoKind` enum (Slaves / Food / Raw / Finished /
      Unset) is the new discriminator on `PopulationPartyComponent`
      (SaveableProperty 10). `EffectiveKind` accessor falls back to
      the legacy `SlaveCaravan` bool for pre-Phase-5 saves —
      backward-compat without a save migration step.
      `CreateSlaveCaravan` now stamps `Kind=Slaves` on forward
      saves; new `CreateFoodCaravan(origin, target, amount)` uses
      same overland primitive but stamps `Kind=Food` and stocks
      grain in the ItemRoster.
      Existing slave-caravan rescue paths in `BKShippingBehavior`
      (lines ~1029, ~1538) updated to gate on
      `EffectiveKind == CargoKind.Slaves` instead of the bool —
      raw-goods caravans are explicitly excluded from slave-caravan
      cleanup logic.
      Auto-dispatch in `ClusterFoodTracker.TryDispatchFoodCaravan`:
      surplus town (FoodChange > 0, FoodStocks > 50%) sends ≥25-unit
      food caravan to its nearest stagnant town in same kingdom,
      gated on the `LayeredEconomyYields` MCM toggle. 3-day per-town
      cooldown in static dict (rebuild on load — in-flight caravans
      satisfy intent until they arrive).
      Validation cheats:
        - `bannerkings.dump_trade_caravans` → BK_trade_caravans.txt
          listing every `PopulationPartyComponent` party with its
          `CargoKind`, origin, target, AT_SEA flag, plus histogram
          summary.
        - `bannerkings.test_dispatch_food_caravan <from> <to> [amount]`
          — manual dispatch bypassing surplus/cooldown checks.
      `BK_food_caravans.txt` (auto-written) logs every organic
      dispatch.
      Naval clusters via shipping graph: deferred — current
      implementation is land-only, sea route via shipping graph is
      a follow-up if playtest shows naval food trade matters.
- [x] **Phase 6 — AI policy.** ✅ Landed on `economy-phase-6` branch.
      `EstatePolicyAI` 6-priority trigger ladder, 30-day per-clan
      cadence, 60-day per-estate cooldown via new SaveableProperty(16)
      `Estate.LastSpecChange` (CampaignTime).
      Triggers in order: levy crisis (war + party <70%) → Levy;
      bankruptcy (gold < tier×3000) → Yield; cluster food crisis
      (cluster stagnant + food-class estate) → Sustained; quality
      opportunity (Loomhouse/Stable/CaravanHub bound, supplying
      class) → Quality; wartime baseline → Yield; peacetime → hold.
      Per-clan eval skips player clan (they decide their own spec).
      Notable replacement re-spec: `OnHeroOccupationChangedEvent`
      handler re-derives spec via `DefaultEstateSpecs.ForNotable`
      when a notable's role changes. Closes Phase 1 review obligation.
      Validation cheats:
        - `bannerkings.test_eval_clan <clan_id>` — force-run the
          ladder ignoring cadence
        - `BK_ai_estate_decisions.txt` (auto-written) logs every
          flip with old/new spec + reason
      ⏳ Deferred: AI village-owner tax adjustment, AI town-industry
      annual review, religion-aware spec — follow-up tuning.
- [x] **Phase 7 — player levers (cheat-driven).** ✅ Landed on
      `economy-phase-7` branch. Cheats provide every player decision
      surface:
        - `bannerkings.set_estate_spec <settlement> <owner> <spec>`
          — change spec on a player-or-notable estate; stamps
          `LastSpecChange` so the AI cooldown applies symmetrically
        - `bannerkings.set_town_industry <town> <industry>` —
          hard-flip a town's industry tag (no gradual workshop
          conversion in Phase 7; that's a polish-pass follow-up)
      ⏳ DEFERRED: proper UIExtenderEx-based UI on the existing
      Estate / Town / Village panels. Hours of UIExtender mixin
      work + XML overlay; lands on `economy-phase-7-ui` follow-up.
      The cheat surface is the testbed until then; players who
      enable cheats can already drive every decision.
      ⏳ Tax-rate slider — uses existing BK estate `TaxRatio` (not
      a new lever); will surface in the same UI pass.
- [x] **Phase 8 — village-class transition + Growth decree.** ✅
      Landed on `economy-phase-8` branch. `DecreeKind` enum (None /
      Growth / ClassTransition); `VillageDecreeManager` daily-tick
      tracker; SaveableProperty 9/10/11 on LandData (ActiveDecree,
      DecreeStartTime, DecreeTargetClass).
      Decree mechanics:
        - Duration: 2 in-game years
        - Output multiplier during: 0.5× (`Breakdown.Decree`,
          multiplied into `Final` after Stagnation)
        - Growth-only daily side-effect: `+0.5 hearth/day` on top of
          vanilla growth (compounds to ~+365 hearth across 2 years)
        - ClassTransition completion swaps `LandData.VillageClass`
          to `DecreeTargetClass`
        - Mutually exclusive: only one decree per village at a time
      Validation cheats:
        - `bannerkings.start_growth_decree <village_id>`
        - `bannerkings.start_class_transition <village_id> <new_class>`
        - `bannerkings.cancel_decree <village_id>`
      Auto-logs every start/cancel/complete to BK_village_decrees.txt.
      `DecreeKind` registered in SaveDefiner (1114).
      Gated on the `LayeredEconomyYields` MCM toggle.

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

- ⏳ **InferIndustry fallback for Unset towns.** Towns whose workshops
  are 100% modded or 100% "artisans" infer `TownIndustry.Unset`. Phase 1
  persists Unset and Phase 2 yield math will have to either tolerate
  Unset (treat as vanilla pass-through) or pick a per-culture fallback.
  Decide which when wiring `EstateYieldCalculator`.
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

- ⏳ **Notable death/replacement re-spec hook.** When a notable dies and
  a new one of a different `Occupation` spawns into their estate, the
  persisted `Spec` stays the dead notable's value. Phase 1 has no
  `OnHeroDestroyed` / occupation-change listener; Phase 6 AI policy
  module should add one and re-derive via `DefaultEstateSpecs.ForNotable`
  on the replacement.
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
