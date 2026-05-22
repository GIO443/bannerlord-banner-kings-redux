# Systems reference

← [Home](Home)

Reference tables for the structured systems you look up while playing —
the kingdom screen, lifestyles, demesne laws, per-settlement policies. For
procedural how-to, see [Player guide](Player-Guide).

## The BannerKings kingdom screen

Open any kingdom screen and click the **BannerKings** tab. It carries five
sub-tabs:

- **Realm** — the realm's political state. The government form and its
  political layer, then four live metrics each drawn as a proportion bar:
  **Crown Authority** (the realm's centralisation, 0 Decentralised → 4
  Absolute), **Ruler Legitimacy**, **War Fatigue**, and **Government
  Transition** pressure. Hover any of them for a breakdown of *why* the
  number sits where it does — Legitimacy in particular lists every factor
  pushing it toward its target. Below the metrics: the succession law and
  the current heir, with the other candidates ranked beside them.
- **Laws** — the realm's editable legal code: the demesne-law grid plus the
  Inheritance, Gender Law, Tax, and Conquest contract aspects.
- **Court** — the kingdom council (Marshal, Steward, Chancellor, Spymaster,
  Court Physician), the current holders, and their competence.
- **Groups** — interest groups and radical groups (claimant, secession, and
  the rest). Join one, lead it, or push its demands.
- **Career** — your mercenary career. Shown only while your clan is serving
  under a mercenary contract.

> Changing a government, succession law, demesne law, or contract aspect is
> a **proposal** — the realm's peers vote on it. The button tooltip shows
> the current support percentage and the influence cost before you commit.

## Lifestyles

Pick one on the BK Education tab. Each has two linked skills; you must reach
at least 15 in both to adopt it. Both skills must be exercised — only the
*lower* skill contributes to progress per tick.

**Core lifestyles** (always available):

| Lifestyle | Skills | Theme |
|---|---|---|
| Fian | Bow + Two-Handed | Battanian woodland skirmisher |
| Cataphract | Polearm + Riding | Heavy lancer cavalry |
| August | Leadership + Lordship | Imperial statesman |
| SiegeEngineer | Engineering + Tactics | Siege specialist |
| CivilAdministrator | Engineering + Steward | Realm bureaucrat |
| Caravaneer | Trade + Scouting | Long-distance trader |
| Artisan | Smithing + Trade | Master crafter |
| Outlaw | Roguery + Scouting | Bandit chief |
| Mercenary | Leadership + Roguery | Sellsword captain |
| Kheshig | Riding + Bow | Khuzait elite horse-archer |
| Varyag | Athletics + One-Handed | Sturgian raider |
| Gladiator | Athletics + Riding | Arena fighter |
| Ritter | Lordship + Riding | Vlandian heavy knight |
| Jawwal | Throwing + Riding | Aserai light cavalry |
| Commander | Leadership + Tactics | Battlefield commander |

**Nord seafaring lifestyles** *(War Sails only, Nord culture only)*:

| Lifestyle | Skills | Theme | Naval-specific bonuses |
|---|---|---|---|
| Jomsviking | Two-Handed + Athletics | Nord shieldwall warrior | Boarding Fury → +6% melee damage on naval missions |
| Drakkar Captain | Leadership + Tactics | Sea-going war-band leader | Helmsman → +4% party speed at sea; Raid Master → +12% raid hit damage on naval raids |
| Sjofarandi | Bow + Scouting | Coastal pathfinder/scout | Pathfinder + Sea-Eyes → +12%/+8% spotting range at sea |

The lifestyle picker shows each lifestyle's bonuses, perks, and lore in the
hover tooltip. Bonuses appear at the top so you can see them even on
lifestyles you don't yet qualify for.

## Demesne laws

Set on the **Laws** sub-tab of the BannerKings kingdom screen (above). Each
is an aspect of a title's contract, and changing one is a peer-voted
proposal. The slavery laws are illustrative:

| Slavery law | Effect |
|---|---|
| Standard | Baseline; no modifiers |
| Vlandic Law | Slave demand −30%; Vlandian prisoners cannot be enslaved |
| Aseran Law | Slave demand +50%; slaves count as military manpower |
| Nordic Thrall Law *(Nord-only)* | Slave demand +80%; slaves count as military manpower; Nord ports run slave caravans regardless of policy; no automatic manumission |
| Manumission | Slave demand reduced to zero — abolitionist law |

Other law families: drafting (Hidage / Vassalage / Free Contracts), tenancy
(Full / Mixed / None), council (Appointed / Elected), army type (Private /
Horde / Legion).

## Per-settlement policies

You set these directly from the BK settlement panel. They take effect daily.

| Policy | Options |
|---|---|
| Tax | Standard / High / Low / Exempted |
| Militia | Balanced / Melee / Ranged |
| Draft | Standard / High Draft / No Draft |
| Garrison | Standard / Reinforce / Disband |
| Workforce | Construction / Production / Martial |
| Criminal | Lenient / Standard / Strict |

## BK trade goods — where to find them

BK adds a layer of trade goods on top of vanilla. They appear naturally
in the same village and town markets you already use, attached to vanilla
village types as bonus productions.

| Good | Find it in villages of type | Daily output | What it's used for |
|---|---|---|---|
| Limestone | Clay-mine villages | 8 | Construction (fortifications, marketplaces, theaters, waterworks) |
| Marble | Silver-mine villages | 0.8 | High-tier prestige builds (theater lvl 3, waterworks lvl 3) |
| Gold ore | Silver-mine villages | 0.2 | High-tier court extravagance; resale luxury |
| Mead | Lumberjack villages | 2 | Party alcohol supply (alongside wine and beer) |
| Honey | Lumberjack villages | 0.5 + skeps bonus | Party animal-products supply |
| Garum | Fisherman villages | 2 | Party animal-products supply (fermented fish) |
| Whale meat | Fisherman villages | 1.5 | Party animal-products supply (counts as meat) |
| Purple dye | Fisherman villages | 0.05 | Pure luxury good — extremely rare, high resale |
| Spice | Date-farm villages | 0.5 | Luxury good — caravan AI prices it heavily |
| Papyrus | Wheat-farm villages | 0.5 | Luxury trade good *(no in-game consumer yet)* |
| Eggs | Cattle-range villages | 1.5 | Party animal-products supply (also drops from your own flocks) |

Notes for finding stock:

- **Limestone is the workhorse.** Every clay-mine village in the world
  produces it. If you're constructing a fortification and the materials
  panel shows missing limestone, the closest source is the nearest
  clay-mine village to the fief.
- **Marble is rare on purpose.** Only silver-mine villages produce it,
  at less than 1 per day each. Stock up before queuing a theater or
  waterworks lvl 3 — you may need to caravan-import.
- **Mead, garum, and honey go fast.** Parties consume them, so village
  stocks turn over quickly. Buy when you see a load.
- **Workshop "mines"** in towns (vanilla mines workshop) is a separate
  source: a town's local mineral composition (random per settlement)
  produces limestone/marble/iron/etc. independently of which villages
  feed it. So even towns with no nearby quarry village can produce
  small amounts of limestone or marble through their own mine
  workshop, depending on the local rock.

A future scriptorium / book workshop will give papyrus + ink real
demand; for now they exist as luxury cargo.

### Cavalry-culture warhorse boost

Vanilla applies a heavy tier penalty to mount production, which leaves
heavy-cavalry empires chronically short of warhorses. BK gives Khuzait
and Vlandia village owners a **+35 % factor on warhorse output only**
(Tier-2 mounts; doesn't touch regular horses, sheep, cattle, or pack
animals).

- **Khuzait** — stacks with the existing Khuzait *Animal Production*
  cultural feat, so a Khuzait-owned herding village produces noticeably
  more warhorses than any other culture. They are the horse culture by
  design.
- **Vlandia** — gets the +35 % cleanly, giving Vlandian fiefs a real
  reason to hold pasture-rich villages for the lance squadrons.
- **Other cultures** — no change. If you're playing an empire that
  fields heavy cavalry but isn't Khuzait or Vlandia, you'll still need
  to import warhorses or seize a cavalry-culture fief.

What you should see in-game: open a Khuzait or Vlandian herding
village's production tooltip — the warhorse line will show a
**+35 % "Culture"** modifier on top of the base pasture math.

## Faiths

Each culture has one natural faith. Heroes are assigned the natural
faith of their culture on session-launch (or the next daily tick after
load on existing saves). Switching faith is done in dialogue with a
preacher notable — see [Player guide → Religion](Player-Guide#religion).

| Culture | Faith | Type | Faith group | Main divinity | Notable doctrines |
|---|---|---|---|---|---|
| Empire | **Darusosian Path** | Henotheistic | Imperial Orders (pontifex) | Iovis (Sky-Father) | Legalism, Renovatio Imperi, Tolerant, Astrology, Esotericism |
| Vlandia | **Canticles of Caïon** | Henotheistic | Canonical See (primarch) | Caïon (Crowner) | Legalism, Honoured Childbirth, Warlike, Literalism |
| Battania | **Amra Druidh** | Polytheistic | Druidic Circles (arch-druid) | Pérkos (Thunder-Wielder) | Druidism, Animism, Shamanism, Ancestor Worship |
| Aserai | **Path of Akhmar** | Monotheistic | Ulama of the Sun (imam) | Akhmar (the Most High) | Tolerant, Legalism, Literalism, Heathen Taxation |
| Khuzait | **Six Winds** | Polytheistic | Sky-Shamans (khan-shaman) | Tengri (Eternal Sky) | Ancestor Worship, Shamanism, Pastoralism, Warlike |
| Sturgia | **Old Gods of the North** | Polytheistic | Eldercouncil (eldgothi) | Pérkos (Thunder-Wielder) | Ancestor Worship, Childbirth, Warlike, Tolerant |
| Nord (War Sails) | **Osfeydian Tradition** | Polytheistic | Hraef-Sworn (hrafnskáld) | Hreinwald (Sea-King) | Reavers, Osric's Vengeance, Warlike, Ancestor Worship |

Each faith carries a **pantheon of secondary cults** you can request a
blessing from once you've earned enough piety — Astaronia, Darusos,
Marcosus, Belisaria, Reginus, Máthair, Iarnan, Eilean, Etugen, Sülde,
Asra, Frydan, Mátr, Vethari, Skǫll. The pantheons overlap deliberately
where lore allows: Pérkos is recognised by both the Battanian and
Sturgian faiths, and the Nord pantheon syncretises Pérkos and Vethari
from their Sturgian neighbours.

Doctrines determine what a faith *does* — they grant piety from
specific actions (battle wins, occupations, raids, childbirth,
education projects) and unlock or restrict behaviours (mercy-on-siege
relations, council eligibility, holy-war availability). Each faith
ships with four to five baseline doctrines, listed above.

---

← [Getting started](Getting-Started) · [Home](Home) · [Player guide →](Player-Guide)
