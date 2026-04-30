# Slavery & raiding

← [Home](Home)

The slavery and raid economy in BK Redux: how to sell prisoners, how
slave caravans work, the v1.6.2 raid capture system, and the Nord-flavoured
build that the Nordic Thrall Law unlocks.

## On this page

- [The Nord raid economy (high-level)](#the-nord-raid-economy-high-level)
- [How do I sell prisoners as slaves?](#how-do-i-sell-prisoners-as-slaves)
- [Raid capture system (v1.6.2)](#raid-capture-system-v162)
- [Is slave trading and raiding profitable?](#is-slave-trading-and-raiding-profitable)
- [Console cheats and logging](#console-cheats-and-logging)
- [Slavery & raiding FAQ](#slavery--raiding-faq)

---

## The Nord raid economy (high-level)

The Nords lean hard into the raid-economy loop. The Nordic Thrall Law applies
automatically to Nord realms and amplifies slave demand by 80%, making Nord
ports the most profitable place in Calradia to sell prisoners as slaves. Nord
towns run slave caravans regardless of policy. Combined with the Drakkar
Captain lifestyle, raiding and selling becomes a viable Nord economic build.

For the demesne law table (Standard / Vlandic / Aseran / Nordic Thrall /
Manumission), see [Systems reference → Demesne laws](Systems-Reference#demesne-laws).

---

## How do I sell prisoners as slaves?

1. Capture prisoners in battle (vanilla — they go into your party's
   prisoner roster automatically when you defeat enemies).
2. Travel to a town with high slave demand. Best markets in order:
   - **Nord ports** under the Nordic Thrall Law (+80% demand).
   - **Aserai cities** under the Aseran Law (+50% demand).
   - Generic towns (no bonus, only base 150 gold per prisoner).
3. Make sure the *Enslavement* criminal policy is active — open the
   kingdom decisions screen and enact *Enslavement* if it isn't.
   Without this, the prisoner-ransom UI uses vanilla ransom prices
   instead of BK's slave price.
4. Open the town menu → *Sell prisoners*. The slave-price calculation
   kicks in automatically; you'll see ~150–270 gold per prisoner
   depending on the market.

---

## Raid capture system (v1.6.2)

When the **Raid Capture System** is enabled in MCM (default on), every
village you raid produces a *captive caravan* on top of vanilla raid
damage. The vanilla raid still hits hearths and prosperity exactly as
before — the captives are conceptually drawn from the already-displaced
cohort, so the source village is *not* damaged extra. The caravan ships
captives to your nearest friendly fief, and on arrival they enter the
local population either as Slaves or as Serfs depending on your toggle.

### 1. Set your defaults

> **All three toggles are sticky per clan and survive save/load.** Set
> them once on your first raid; you don't need to cycle them again
> unless you want to change strategy.

When you walk up to a hostile village (the `village_hostile_action`
menu, the same one with "Raid the village" and "Loot the village"),
three new lines appear above the raid options:

- `Captives: Take` / `Captives: Leave` — click to flip. Sticky per clan.
  Default Take if your clan's realm has slavery, Leave otherwise.
- `Disposition: Slaves` / `Disposition: Serfs` — only shown if Captives
  is set to Take. Sticky per clan. Default Slaves under slavery realms,
  Serfs otherwise.
- `Destination: …` — only shown if Captives is set to Take. Cycles
  through the three destination strategies. Sticky per clan. Default
  Nearest Friendly.
- `Estimated captives: ~N` — read-only preview computed from village
  serf population. Helps you decide whether the raid is worth setting up
  for capture.

**Destination modes (v1.6.2.2).**

| Mode | How the caravan picks its target |
|---|---|
| **Nearest Friendly** *(default)* | Closest non-sieged town/castle in your faction or your clan's faction. Cheapest in time, lowest payout ceiling. |
| **Nearest Owned** | Closest fief your *clan* owns. Funnels captives into your demesne — useful for populating a frontier estate. Falls back to Nearest Friendly when your clan owns no fiefs. |
| **Most Profitable** | Scores every reachable friendly fief by `(payout-per-head × surviving-captives) − (graph-weighted-distance × travel-cost)` and picks the max within a 600-unit search radius. Uses the [adaptive shipping graph](Shipping-and-Trade#adaptive-shipping-costs-v161) so war zones and bandit coasts are auto-discounted. Can produce long, interceptable caravans through hostile waters — that's the trade. |

**AI clans always use Nearest Owned**, regardless of any saved policy. The destination toggle on the village menu only affects the player clan; AI raiders funnel captives back to their own demesne, not random allied fiefs.

**Fallback chain (v1.6.3.0).** If the chosen mode finds no destination — e.g. an exiled lord or fresh mercenary band with no kingdom and no fiefs — the system tries the alternates and finally falls back to *Most Profitable* as a last resort. Captives never just dissolve.

**No payout when delivering to your own clan's fief (v1.6.3.0).** The slaves still go into the receiving population (so you keep the long-term economic value via tax revenue and population growth), but you don't get an instant gold lump sum — you'd be paying yourself for the slaves you captured. Pick **Most Profitable** or any non-clan-owned destination if you want the gold.

**Hop-by-hop graph routing (v1.6.3.0).** Captive caravans now route through the unified [trade graph](Shipping-and-Trade) — they walk hop by hop along risk-weighted edges, detouring around hostile coasts and sieged regions instead of vanilla pathfinding straight across. A captive caravan from inland Battania to a Vlandian fief might walk south, board a ship, and land on the Vlandian coast all in one graph path. This is what makes the destination toggle actually meaningful for non-port raids.

**Village anchoring (v1.6.3.1).** The graph contains only towns and castles, not villages. When a raid completes at a village (which is always the case), the captive caravan first walks to the *nearest safe* graph fief — closest by distance, weighted by risk so a hostile-bordered fief loses to a slightly farther peaceful one and sieged fiefs are skipped. Once it reaches that anchor, normal graph hops take over. This preserves risk-aware routing for the full journey instead of dropping to vanilla pathfind for the whole trip.

**Captive count (v1.6.3.1).** Two limiters, whichever is lower:

- **Village pool**: serfs × 10% × *Raid Capture Fraction* (MCM, default 40%) — so a 1,000-serf village offers up to 40 captives.
- **Party carry**: `(troops − 5) × 0.5` with a floor of 5 — a 30-troop war band carries 12, a 100-troop army carries 47, 200 troops hits the cap.

Hard cap 150 per raid. Multi-party raids (armies, multiple clans on the same village) **pool their carry capacity** — the game sums troops across every party on the attacker side, so a coordinated army of three 50-troop parties has a 72 carry cap, not 22.

**Most Profitable scoring (v1.6.3.1).** Multiplicative decay rather than flat penalty:

```
score = (captives × payout/head) × distanceDecay × safetyDecay
distanceDecay = 1 / (1 + dist / 100)
safetyDecay = 1 / riskMultiplier
```

A close peaceful fief beats a distant high-payout one unless the payout differential is genuinely large. War zones, sieges, and bandit-heavy approaches reduce the score proportional to risk.

### 2. Run the raid

Choose "Raid the village" as normal. When the raid completes, BK applies
your toggles:

- A captive caravan spawns at the raided village and walks to the
  *nearest friendly* town or castle that isn't at war with your party.
- Captives keep their **original culture** — Battanian raids on a mixed
  Vlandian village produce a culture-weighted cohort of Vlandian, Empire,
  etc., **excluding your raid leader's culture**. This is intentional: no
  internal slave-taking among your own ethnos.
- A small culture-typed escort accompanies the caravan (10–40 troops, tier
  ≤ 2). Strong enough to fend off a small bandit pack; weak enough that
  any war party will roll over it. Decide whether to escort it home
  yourself.

### 3. Arrival

When the caravan reaches its destination, captives are absorbed into the
receiving fief's population (Slaves or Serfs per your toggle), each
cohort credited under its *own* culture in `CultureData`. You receive a
lump-sum payout to your hero — full slave price for Slaves, ~55% of
slave price for Serfs.

### 4. Disposition legality

- **Independent clan** (no kingdom): both Slaves and Serfs always legal.
- **Realm with `SlaveryNord` / `SlaveryAserai` / criminal Enslavement**:
  both legal, default Slaves.
- **Realm without slavery**: Serfs legal; Slaves shows
  *"Slaves (UNLAWFUL)"* — you can still pick it for the higher payout, but
  expect a criminal rating tick, relation hit with your kingdom's ruler,
  and influence loss per caravan. Profit beats penalty for one-off
  captures; sustained illegal slaving will cost more than it earns.

### 5. Foreign mercenaries and mercenary raiding

The skim system models the awkward reality that a captain serving a
foreign crown has private interests of his own. **The trigger is
strictly: the raid leader's clan has a Kingdom, AND the leader's
culture differs from that Kingdom's culture.** Both conditions are
required — independent mercenary captains (no kingdom) are *not*
flagged as foreign, even though they're explicitly serving no one,
because the skim is about secretly diverting an employer's haul.
Without an employer, there's nothing to skim *from* — you just keep
everything via the regular destination policy.

**Who counts as foreign:**

- **A mercenary clan on an active kingdom contract**, where the clan's
  leader has a different culture than the employing kingdom — almost
  the textbook case.
- **A vassal whose culture differs from their liege's kingdom** — e.g.
  a Battanian count under a Vlandian king after a successful claim
  war. They get the skim every time they raid for Vlandia, indefinitely.
- **The player serving a foreign kingdom**, with your character's
  culture set to anything other than that kingdom's culture.

A clan in a kingdom whose leader shares the kingdom's culture is
treated as native and the skim doesn't apply. An independent clan
(no kingdom) is also not flagged as foreign — see "Independent
mercenaries" below for what they actually do.

**The split for a foreign captain.** When a foreign-led party raids:

- **20% of captives** (default — MCM-tunable as *Foreign Merc Skim*)
  are diverted to the captain's private profit pool.
- The remaining **80%** travel to the destination chosen by the raid
  capture policy (Nearest Friendly / Nearest Owned / Most Profitable
  for the player; always Nearest Owned for AI clans).
- **Skim destination**: a *second*, smaller captive caravan spawns at
  the raided village and walks to the captain's **clan home
  settlement** — not their employer's territory. Disposition is
  always **Slaves**, regardless of the policy's disposition setting:
  the captain personally captured these heads and is selling them
  through his own networks, not on his employer's books. Both
  caravans (main + skim) appear on the map and are interceptable.
- **Edge case** — if the foreign captain's clan has a kingdom but no
  home settlement (an orphan clan, rare), the skim collapses to
  instant gold paid directly to the captain at the destination's
  slave price.

**Cohort culture exclusion uses the leader's culture, not the
employer's.** A Battanian mercenary serving Vlandia who raids a
Battanian village does not take Battanian captives — even though
Vlandia might have wanted them. The exclusion is about identity, not
employer obligation. A foreign captain raiding villages of *their
own* culture produces no captives at all (the cohort is empty after
exclusion), and the system silently aborts the capture.

**Independent mercenaries** (clan exists, no kingdom — typical of a
fresh mercenary band or a clan between contracts) are *not* flagged
as foreign. All captives go to the main caravan via the regular
destination policy. With no fief or kingdom, the destination
fallback chain (added in v1.6.3.0) routes them via *Most Profitable*
as a last resort — a long caravan to whatever market pays best,
through whatever route the trade graph picks.

**Mercenary raiding scenarios at a glance:**

- **Player as an independent mercenary captain.** No skim. Your raids
  produce one caravan with all captives, routed by your policy
  toggle on the village menu (or fallback to Most Profitable if you
  own no fief).
- **Player vassal/mercenary in a foreign kingdom.** Skim 20% to a
  side caravan to your clan home (Slaves). Main caravan follows your
  policy.
- **AI mercenary clans on contract.** Same flow. AI clans always use
  **Nearest Owned** for the main caravan; the skim side caravan goes
  to their clan home as Slaves. AI mercenary clans with no fief and
  no clan home hit the fallback chain and route the main caravan via
  Most Profitable.
- **Vassal clans of mismatched culture.** Same skim rules. A
  long-standing Battanian vassal of Vlandia keeps diverting 20% of
  their raid captives to their own clan home over decades — meaningful
  demographic pressure on the vassal's own demesne, and meaningful
  loss to the Vlandian crown's recruitable pool.
- **Bandits and BK bandit-hero clans never produce captives.** This
  shortcut applies before the foreign-merc check, so a hired bandit
  clan acting as a war party still won't trigger captures.

### 6. Intercepting enemy caravans

Hostile captive caravans appear on the map and can be attacked like any
party. Defeating one releases the captives (no transfer to your fief) —
useful for harassing slaver realms.

### 7. Demographic warfare

Because captives keep their original culture and feed the destination's
`CultureData`, sustained raiding visibly reshapes both sides over decades:

- **Donor settlements** lose pop biased toward their own culture (your
  culture is excluded), so a raided foreign town slowly purifies toward
  the *raider's* cultural minority over many raids.
- **Receiver settlements** gain a foreign-culture cohort with low
  acceptance (0.20). The next-tick weight recompute shifts assimilation
  in their favor; over many caravans, visible foreign pockets form in
  your towns, with all the loyalty/recruit-pool consequences that come
  with cultural mismatch.

### 8. Toggling the system off

Open MCM → Banner Kings → Slavery → *Raid Capture System*. With it off,
only the existing slavery system runs (criminal-policy Enslavement on
prisoner sale, `decision_slaves_export` slave caravans). Existing saves
remain compatible either way.

### Gotchas

- Raid leader's *culture* (not their kingdom's) decides the cohort
  exclusion. Mercenary captains carry their own culture into this rule.
- Bandits never produce captives — only player and AI lord raids do.
- If no friendly town/castle exists (besieged, all-hostile, etc.), the
  caravan routes to your clan's home settlement as a fallback.
- Caravans are not invincible. Plan to escort them home if you raided
  deep in enemy territory.

---

## Is slave trading and raiding profitable?

Yes, but with caveats:

- **Selling captured prisoners as slaves**: ~150 gold base per prisoner,
  scaled by local demand. A run of 20 prisoners delivered to a high-demand
  Nord or Aserai port nets ~3,000 gold instantly.
- **Holding slaves in your own fief**: ~0.115 gold per slave per day in
  tax-line income (with the *Slaves Domestic Duties* law). Break-even vs
  selling takes ~3.6 in-game years.
- **Conclusion**: sell directly. Holding in your own fief is only better if
  you specifically want population growth and intend to keep that fief for
  many in-game years.
- **Cultural amplifiers matter.** Aserai (`SlaveryAserai`, +50% demand) and
  Nord (`SlaveryNord`, +80% demand) are the best markets. Vlandian
  (`SlaveryVlandia`, −30%, no Vlandian enslavement) and Manumission realms
  are poor markets.

---

## Console cheats and logging

Cheats must be enabled in the launcher.

**Test cheats** (v1.6.2.1):

- `bannerkings.test_raid_policy Take | Slaves [| MostProfitable]` — set
  the player clan's raid capture policy directly (cycling through the
  village menu also works in-game; this is faster from the console).
  Optional 3rd arg sets the destination mode: `NearestFriendly`,
  `NearestOwned`, or `MostProfitable`.
- `bannerkings.test_raid_capture village_V1_1` — run the capture flow
  on the named village as if MainParty just finished a successful raid
  there. Skips the actual raid combat / village damage; you only see
  the captive caravan side.
- `bannerkings.test_dump_raid_state` — list the player policy, the
  current settings (enable / fraction / skim / log), and every active
  captive caravan with cargo breakdown by culture, target, captor.
- `bannerkings.dump_caravans` *(v1.6.3.2)* — instant snapshot of every
  caravan-style party (captive + trade) with position, current
  settlement, move target, final destination, IsActive, AtSea,
  AiDisabled, prisoner count, captor. Output → BK_dump_caravans.txt.
  Use this when a caravan looks stuck; the field values usually
  identify the cause (IsActive=false → BK shipping limbo; moveTo=null
  → AI lost its goal; AtSea=true on land settlement → disembark
  failure; etc.).

A passive **daily caravan watchdog** also appends every captive
caravan's state and any 24h-idle trade caravan to BK_caravan_watchdog.txt
when MCM *Log Raid Capture Behavior* is on, so retroactive diagnosis is
possible even if the cheat wasn't run at the time.

**Behavior logging** (no cheats required): toggle **MCM → Banner Kings
→ Slavery → Log Raid Capture Behavior**. With it on, every capture
decision (project, split, disposition, cohort, spawn, payout) prints
to both the in-game info panel and `Debug.Print`. Lines are prefixed
`[BKRaid]` for grepping. Use this when investigating an unexpected
outcome from a real campaign raid.

---

## Slavery & raiding FAQ

**Q: Where do I sell prisoners as slaves?**
Any town accepts prisoners via the standard prisoner-ransom UI. With the
*Enslavement* criminal policy active, the gold paid switches from vanilla
ransom to BK's slave-price calculation. Nord ports under the Nordic Thrall
Law pay the most (≈ +80% demand), Aserai second (+50%).

**Q: How do slave caravans work?**
AI-only feature. Towns with enough surplus slaves and the *Slave Export*
decision enacted dispatch caravans that move 0.5% of the slave population
per day. Nord towns run these caravans automatically regardless of the
decision (Nordic Thrall Law overrides the gate).

**Q: How do I free slaves in my realm?**
Enact the *Manumission* demesne law. It drives slave demand to zero, and
the population balance code converts excess slaves to serfs over time.

**Q: How does the new raid capture system work?**
See [Raid capture system (v1.6.2)](#raid-capture-system-v162) above.

**Q: Why didn't I get a captive caravan after raiding?**
A few reasons. (1) You may have set Captives → Leave on the village menu;
flip it back. (2) The village had ~0 serfs (raid frequency or famine);
the model floors at 0 captives. (3) The MCM toggle is off. (4) You raided
as a bandit clan — bandits never produce captives. Run
`bannerkings.test_dump_raid_state` to confirm your policy and the system
state, and toggle on the *Log Raid Capture Behavior* MCM setting to see
the decision trace.

---

← [Shipping & trade](Shipping-and-Trade) · [Home](Home) · [Troubleshooting →](Troubleshooting)
