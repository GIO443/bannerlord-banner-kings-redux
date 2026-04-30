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

When you walk up to a hostile village (the `village_hostile_action`
menu, the same one with "Raid the village" and "Loot the village"),
three new lines appear above the raid options:

- `Captives: Take` / `Captives: Leave` — click to flip. Sticky per clan.
  Default Take if your clan's realm has slavery, Leave otherwise.
- `Disposition: Slaves` / `Disposition: Serfs` — only shown if Captives
  is set to Take. Sticky per clan. Default Slaves under slavery realms,
  Serfs otherwise.
- `Estimated captives: ~N` — read-only preview computed from village
  serf population. Helps you decide whether the raid is worth setting up
  for capture.

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

### 5. Foreign mercenaries

If your raid leader's culture differs from your employing kingdom's
culture (e.g. a Sturgian captain serving Vlandia), 20% of captives are
skimmed for the captain's private benefit:

- Independent merc (no employer kingdom): instant gold payout, no
  secondary caravan.
- Kingdom-affiliated foreign captain: a *second*, smaller caravan spawns
  to the captain's clan home. Both caravans are interceptable.

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

- `bannerkings.test_raid_policy Take | Slaves` — set the player clan's
  raid capture policy directly (cycling through the village menu also
  works in-game; this is faster from the console).
- `bannerkings.test_raid_capture village_V1_1` — run the capture flow
  on the named village as if MainParty just finished a successful raid
  there. Skips the actual raid combat / village damage; you only see
  the captive caravan side.
- `bannerkings.test_dump_raid_state` — list the player policy, the
  current settings (enable / fraction / skim / log), and every active
  captive caravan with cargo breakdown by culture, target, captor.

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
