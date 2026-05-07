# Slavery & raiding

← [Home](Home)

The slavery and raid economy in BK Redux: how to sell prisoners, how
slave caravans work, the raid capture system, and the Nord-flavoured
build that the Nordic Thrall Law unlocks.

## On this page

- [The Nord raid economy (high-level)](#the-nord-raid-economy-high-level)
- [How do I sell prisoners as slaves?](#how-do-i-sell-prisoners-as-slaves)
- [Raid capture system](#raid-capture-system)
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
   prisoner roster automatically when you defeat enemies). **Raiding a
   village now also drops captives directly into your prisoner roster**
   — see [Raid capture system](#raid-capture-system) below.
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

## Raid capture system

When the **Raid Capture System** is enabled in MCM (default on), every
village you raid drops a cohort of captive villagers into **your own
prisoner roster** on top of the normal raid damage. There is no
caravan, no delivery target, no absorb step — the captives are yours
to dispose of however vanilla allows: sell at any town, ransom,
recruit, or release.

The vanilla raid still hits hearths and prosperity exactly as before —
captives are conceptually drawn from the already-displaced cohort, so
the source village is *not* damaged extra.

### How it works

1. Walk up to a hostile village. The `village_hostile_action` menu
   shows two new lines above the raid options:
   - `Captives: Take` / `Captives: Leave` — flip with a click. Sticky
     per clan. Default Take if your realm has slavery, Leave otherwise.
   - `Estimated captives: ~N` — read-only preview computed from village
     serf population and your party size.
2. Choose "Raid the village" as normal.
3. When the raid completes, BK reads your toggle and (if Take) adds
   captive prisoners directly to your party's `PrisonRoster`. You'll
   see "X captives taken from VILLAGE as prisoners." in the info
   panel.
4. Sell, ransom, or recruit them as you would any other prisoner.

### Capture count

Two limiters, whichever is lower:

- **Village pool**: serfs × 10% × *Raid Capture Fraction* (MCM, default
  40%) — so a 1,000-serf village offers up to 40 captives. If BK's
  population data hasn't been generated for that village yet (common
  on a fresh hostile village you've never visited), BK falls back to
  `village.Hearth × 4` as the serf estimate so the preview never
  shows 0 just because the data wasn't ready.
- **Party carry**: `(troops − 5) × 0.5` with a floor of 5 — a 30-troop
  war band carries 12, a 100-troop army carries 47, 200 troops hits
  the cap.

Hard cap 150 per raid. Multi-party raids (armies, multiple clans on
the same village) **pool their carry capacity** — the game sums troops
across every party on the attacker side, so a coordinated army of three
50-troop parties has a 72 carry cap, not 22.

### Cohort culture

Captives keep their **original culture** — Battanian raids on a mixed
Vlandian village produce a culture-weighted cohort of Vlandian, Empire,
etc., **excluding your raid leader's culture**. This is intentional:
no internal slave-taking among your own ethnos. The breakdown follows
the village's `CultureData.Assimilation` weights with the raider's
culture filtered out.

### Foreign mercenaries: skim payout

The skim system models the awkward reality that a captain serving a
foreign crown has private interests of their own. **Trigger:** the
raid leader has a Kingdom AND the leader's culture differs from that
Kingdom's culture. Both required.

When a foreign-led party raids, **20% of captives** (default — MCM-tunable
as *Foreign Merc Skim*) are converted to **instant gold** paid to the
raid leader, priced at the local slave market rate of the nearest
friendly fief. The remaining 80% go to the prisoner roster as usual.
There is no longer a side caravan — the diversion is just a gold lump
sum.

A clan in a kingdom whose leader shares the kingdom's culture is
treated as native and the skim doesn't apply. An independent clan
(no kingdom) is also not flagged as foreign — they're explicitly
serving no one, so there's no employer to skim *from*.

A foreign captain raiding villages of *their own* culture produces no
captives at all (the cohort is empty after exclusion), and the system
silently aborts the capture.

### Toggling the system off

Open MCM → Banner Kings → Slavery → *Raid Capture System*. With it off,
village raids no longer produce captives — only vanilla raid damage
applies, and the existing slavery-economy loops (criminal-policy
Enslavement on prisoner sale, `decision_slaves_export` slave caravans
between AI towns) continue to run.

### Migrating from older builds

Earlier BK Redux builds spawned a separate **captive caravan** that
walked from the raided village to a destination fief and absorbed
captives into the destination's population on arrival. That whole
flow is gone. On save load, any leftover captive caravan from an
older build is silently destroyed — they were ghosts either way
(frequent stuck-on-coast / hop-routing failure cases). No manual
cleanup required.

The companion toggles **Disposition (Slaves/Serfs)** and **Destination
(Nearest Friendly / Nearest Owned / Most Profitable)** were only
meaningful inside the caravan flow and are also gone. With captives
landing directly in your prisoner roster, *you* decide what to do with
them — sell at the highest-paying market, hand-walk to a frontier
estate, ransom individuals, etc.

### Gotchas

- Raid leader's *culture* (not their kingdom's) decides the cohort
  exclusion. Mercenary captains carry their own culture into this rule.
- Bandits never produce captives — only player and AI lord raids do.
- Prisoners go to the **raid leader's party**, not split across the
  army. If you led the raid as part of an army, redistribute later
  through the party screen if needed.

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

- `bannerkings.test_raid_policy Take` (or `Leave`) — set the player
  clan's raid capture toggle directly without walking up to a village
  menu.
- `bannerkings.test_raid_capture village_V1_1` — run the capture flow
  on the named village as if MainParty just finished a successful raid
  there. Skips the actual raid combat / village damage; you only see
  the prisoner handoff.
- `bannerkings.test_dump_raid_state` — list the player policy and the
  current settings (enable / fraction / skim / log).
- `bannerkings.dump_caravans` — snapshot of every caravan-style party
  (BK + vanilla trade) with position, target, IsActive, AtSea,
  prisoner count. Output → `BK_dump_caravans.txt`.

**Behavior logging** (no cheats required): toggle **MCM → Banner Kings
→ Slavery → Log Raid Capture Behavior**. With it on, every capture
decision (projection, split, cohort distribution, prisoners added)
prints to both the in-game info panel and `Debug.Print`. Lines are
prefixed `[BKRaid]` for grepping.

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

**Q: Where does the captive caravan go after a raid?**
There is no captive caravan anymore. Captives are added directly to
your party's prisoner roster — sell or ransom them through any town's
prisoner UI.

**Q: Why didn't I get any captives after raiding?**
A few reasons. (1) You may have set Captives → Leave on the village menu;
flip it back. (2) The village had ~0 serfs (raid frequency or famine);
the model floors at 0 captives. (3) The MCM toggle is off. (4) You raided
as a bandit clan — bandits never produce captives. (5) Your party was
the only one on the attacker side and had ≤5 troops — the carry floor
is 5, but a literal 5-troop band gets 5 max even on a high-pop village.
Run `bannerkings.test_dump_raid_state` to confirm your policy, and
toggle on *Log Raid Capture Behavior* in MCM to see the decision trace.

---

← [Shipping & trade](Shipping-and-Trade) · [Home](Home) · [Troubleshooting →](Troubleshooting)
