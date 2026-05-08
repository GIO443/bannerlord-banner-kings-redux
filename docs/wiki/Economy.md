# Economy

← [Home](Home)

The Banner Kings layered economy adds three classification layers on top of vanilla settlements: **villages** are tagged with a class, **towns** are tagged with an industry, and **estates** pick a specialization. The three layers interact through a **cluster** — a town plus all the villages that trade with it. Cluster alignment determines how much yield you get out of an estate.

This page is the player handbook for the system. For "what does cheat X do" see [Systems Reference](Systems-Reference). For caravan / shipping mechanics see [Shipping & trade](Shipping-and-Trade).

---

## Quick mental model

You own an estate in a village. The village has a **class** (Cropland / FibreFarm / Pastoral / StudFarm / Extractive / CoastalFishery). The village trades with a town that has an **industry** (Granary / Foundry / Loomhouse / CaravanHub). When the village's class supplies what the town's industry wants, your estate's income gets a bonus. When it doesn't, your income takes a small penalty.

You also pick the estate's **specialization** — Yield, Quality, Sustained, Levy, or Growth. Each spec is a different shape of the same base income; pick the one that matches your goals.

Open the estate panel from **Settlement → Estates** (or **Clan → Finance → Income** to see them inline). Both panels surface the cluster context and let you change spec.

---

## Village classes

Set automatically from the village's vanilla type at session start. Stable across a campaign — you can't change a village's class mid-game except through a multi-year decree (rare).

| Class | Vanilla types | Outputs | Food | Best workers |
|---|---|---|---|---|
| **Cropland** | wheat, olive, vineyard, date | grain, oil, wine, dates | +++ | Slaves / Serfs |
| **FibreFarm** | flax, silk | flax, silk | 0 | Slaves / Serfs |
| **Pastoral** | cattle, swine, sheep | meat, wool, hides | ++ | Serfs |
| **StudFarm** | horse ranches | horses (mounts) | 0 | Nobles |
| **Extractive** | iron, silver, salt, clay, lumber | ore, metal, lumber, mead | -- | Slaves |
| **CoastalFishery** | fisherman, whaler, walrus_hunter | fish, garum, dye, whale meat | + | Serfs |

**Food balance** matters because food-positive classes (Cropland / Pastoral / CoastalFishery) feed the cluster, while food-negative ones (Extractive especially) need food imported from neighbors. If a cluster's food balance turns negative for ≥14 in-game days, **stagnation** kicks in (more on this below).

---

## Town industries

Inferred at session start from the town's workshop mix. Granary towns have brewery / olive_press / wine_press / mill workshops; Foundry towns have smithy / weaponsmithy / armorsmithy / fletcher / mines; Loomhouse have weaveries; CaravanHub have pottery / perfumery / jewelry / silversmithy / glassworks.

| Industry | Wants from villages |
|---|---|
| Granary | Cropland (1.20×) |
| Foundry | Extractive (1.20×), Pastoral (0.20× hides) |
| Loomhouse | FibreFarm (0.60×), Pastoral (0.30× wool), CoastalFishery (0.10× dye) |
| CaravanHub | Extractive (0.40×), Cropland (0.30×), CoastalFishery (0.30×) |

The **Stable** industry is deprecated — StudFarm villages produce horses through the vanilla pipeline without needing a dedicated town processing tag. The enum value still exists in saves but no town will ever be tagged Stable going forward.

The number on the right is the village class's **demand weight** for the industry. Higher weight = better cluster fit.

To change a town's industry on a save:

```
bannerkings.set_town_industry <town_id> <Granary|Foundry|Loomhouse|CaravanHub>
bannerkings.reclassify_economy
```

The first hard-flips one town. The second wipes all town industries to Unset and re-runs InferIndustry — useful after the workshop mix changed (you bought a new workshop type).

---

## Industry demand bands

The cluster's industry demand multiplier on each estate's gold-yield axis:

| Demand weight | Band | Effect on yield |
|---|---|---|
| ≥ 1.00 | Perfect supply | **×1.20** |
| ≥ 0.50 | Partial supply | **×1.10** |
| ≥ 0.20 | Minor supply | **×1.00** |
| < 0.20 | Off-mission | **×0.85** |

So an off-mission estate (Cropland in a Foundry-bound cluster) takes a 15% income penalty. A perfect-supply estate (Cropland in a Granary cluster) gets a 20% boost. Cluster alignment matters but won't crater your income — even off-mission, the estate still pays workshop-comparable amounts.

---

## Estate specializations

Pick on the estate panel via the spec dropdown (Settlement → Estates) or the Change Spec button (Clan → Finance → Income). Five options:

| Spec | Volume | Quality | Food | Recruits | Use when |
|---|---|---|---|---|---|
| **Yield** | 1.60× | 0.85× | -0.20 | 0 | Bulk gold; you have a healthy cluster fit and want max income. Slave-heavy. |
| **Quality** | 0.80× | 1.60× | 0 | 0 | Premium grade — wins in luxury clusters (Loomhouse / CaravanHub). Craftsman-heavy. |
| **Sustained** | 1.10× | 1.00× | +0.20 | 0.25× | Balanced default; food-positive on food classes; small recruit yield. Serf-heavy. |
| **Levy** | 0.85× | 1.00× | 0 | 1.50× | Recruit factory — trades income for an expanded levy pool. Serf + Noble. |
| **Growth** | 0.50× | 1.00× | 0 | 0 | **Investment mode.** Halves output now in exchange for daily population gain (+0.2/day stochastic) and acreage expansion (+3 acres/day, split by village land mix). |

### How Growth works (and when to use it)

Growth halves your daily income but the estate's **physical capacity grows** every day:
- `Population` ticks up ~0.2/day on average (1 in 5 days)
- Acreage grows by 3/day total, distributed across Farmland / Pastureland / Woodland by the village's land composition
- Growth caps at the village's `LandData × 0.2` for each acreage component and at `Estate.PopulationCapacity` for population

When the estate is at cap (population and acreage both ≥85% of their ceilings), the estate panel shows a red **"Growth: at cap. Halved output is no longer buying capacity. Switch spec."** warning. AI will never pick Growth on a saturated estate, never on food-positive classes, and only when at peace with gold ≥ tier×5000.

**Use Growth when:** you bought a small, undeveloped estate and want to scale it up into a serious income source over 1-2 in-game years. Your effective acres double, your population doubles, then you flip back to Yield or Sustained and reap the fully-developed estate.

**Don't use Growth when:** you need income now (war chest), or the estate is already at-cap, or you own a food-class estate in a famine-prone cluster (food classes are too strategic to halve).

---

## How estate income flows

Each in-game day:
1. The settlement tick fires for each village; Banner Kings calculates your estate's net daily production based on effective acres × workforce saturation × keep rate × the layered multiplier.
2. The result is paid **directly** to the estate owner's gold via vanilla `GiveGoldAction`.
3. Throughout the day, villager trade events also pay you a per-trip share directly.

There is no "pending balance" or accumulated buffer. Each day's daily income is computed and credited that day, deterministic. The Estate panel's "Last Income" line shows what was actually paid that day. The Daily Income (est.) line is the steady-state prediction — they should match closely once your population stabilizes.

**War custody**: when your faction is at war with the village's faction, the estate enters custody and pays nothing that day. There is no back-pay accrual when the war ends — under-custody days are simply lost income. Hover the Daily Income line in the panel to see the exact blocker reason ("at war with X", or any other condition that drove income to zero).

---

## Workforce saturation

Effective workforce vs the labor required to work all your acres:

- **< 50%** — severely under-staffed. Income heavily reduced (acres aren't worked).
- **50–90%** — under-staffed. Income proportionally reduced.
- **90–110%** — balanced. Full production.
- **> 110%** — surplus. Excess workforce automatically clears new land, growing acreage over time.

The **Population vs Cap** line in the Workforce section tells you whether you have headroom for natural growth or whether you're already at the village ceiling. Buying slaves or recruiting tenants raises population; the cap is set by the village's land and your estate's PopulationCapacity ExplainedNumber (hover for breakdown).

---

## Cluster food and stagnation

Each cluster (a town + its bound villages) has an aggregated food balance. When a cluster runs a food deficit AND its food stocks drop below 25% of cap, the **stagnation counter** starts ticking. After 14 consecutive deficit days, the cluster is flagged stagnant; it stays stagnant until food stocks recover for 7+ days (hysteresis).

While stagnant:
- **Food-positive classes** (Cropland, Pastoral, CoastalFishery) keep producing at full yield — they're the way out of the deficit.
- **Food-negative or food-neutral classes** (Extractive, FibreFarm, StudFarm) take a **0.7×** yield penalty.

This is shown on the estate panel as **Stagnation: ACTIVE** in the Cluster section.

Phase 5 also dispatches **food caravans** automatically — when a same-kingdom surplus town has stocks > 50% cap, it sends a grain caravan to the nearest stagnant town. The caravan walks overland, enters the target, and absorbs into FoodStocks via vanilla settlement-entry. No player action needed; it happens in the background.

---

## AI behavior

Other clans (NPC lords) re-evaluate their estate specs every 30 in-game days. The trigger ladder is:

1. **Personal levy crisis** → flip 1 estate to Levy (at war + party < 70% capacity).
2. **Bankruptcy risk** → flip 1 to Yield (gold < tier × 3000).
3. **Cluster food crisis** → flip food-class estates to Sustained (cluster stagnant + food-positive class available).
4. **Quality opportunity** → flip to Quality (Loomhouse or CaravanHub bound town + supplying class).
5. **Investment opportunity** → flip to Growth (peace + gold ≥ tier × 5000 + non-food class + cluster fit ≥ 0.5 + per-clan cap of 1 + 15% headroom on either acreage or pop).
6. **Wartime baseline** → flip to Yield.
7. **Peacetime baseline** → no-op (sticky).

Per-estate cooldown: 60 in-game days before the same estate can be flipped again.

---

## Cheats for testing

| Command | What it does |
|---|---|
| `bannerkings.dump_economy_state` | Every village class, town industry, estate spec to `BK_economy_state.txt`. |
| `bannerkings.dump_estate_yields` | Per-estate yield multiplier breakdown (vol × qty × workerfit × IndustryDemand × Stagnation × Decree). |
| `bannerkings.dump_clusters` | Per-town cluster summary with IndustryFit and food balance. |
| `bannerkings.dump_food_status` | Per-town stagnation counter and current state. |
| `bannerkings.dump_player_estates` | Player clan's estates with blocker reason and last-paid amount. |
| `bannerkings.set_estate_spec <village> <owner> <spec>` | Force-flip a specific estate's spec. |
| `bannerkings.set_town_industry <town> <industry>` | Force-set a town's industry. |
| `bannerkings.reclassify_economy` | Wipe all town industries to Unset and re-run InferIndustry. |
| `bannerkings.start_growth_decree <village>` | Start a 2-year village Growth decree (+0.5 hearth/day, 0.5× output). |
| `bannerkings.start_class_transition <village> <new_class>` | Start a 2-year village class transition. |
| `bannerkings.cancel_decree <village>` | Cancel any active village decree. |
| `bannerkings.test_force_deficit <town> <days>` | Force a town's stagnation counter — instant. |
| `bannerkings.test_dispatch_food_caravan <from> <to> <amount>` | Manual food caravan dispatch. |

All cheats require dev console enabled (vanilla launch option `-developer`).
