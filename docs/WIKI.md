# Banner Kings — Redux Player Wiki

Welcome. This is the player-facing handbook for **Banner Kings — Redux**, a
maintenance fork of R-Vaccari's Banner Kings updated for Bannerlord v1.3.x with
native War Sails (NavalDLC) integration. If you're looking for code internals
and developer documentation, those live next to the code in the GitHub
repository — this file is intentionally written for players.

> Banner Kings is the work of R-Vaccari and the original Banner Kings
> contributors. This fork is a community maintenance effort while the upstream
> project is dormant. All credit for the design, content, and core systems
> belongs to the original author. See "Credits" at the bottom of this page.

---

## 📥 Download

The release zip lives on the GitHub Releases page:

### **➡️ [github.com/GIO443/bannerlord-banner-kings-redux/releases/latest](https://github.com/GIO443/bannerlord-banner-kings-redux/releases/latest)**

That's the authoritative download. The Nexus page is currently hidden
while attribution and licensing details are sorted with the original
author — GitHub Releases is where you grab the build in the meantime.
Extract the zip into your Bannerlord install per the *Installing*
section below.

---

## Table of contents

1. [What is Banner Kings — Redux](#1-what-is-banner-kings--redux)
2. [Installing](#2-installing)
3. [What's in the mod (high-level)](#3-whats-in-the-mod-high-level)
4. [First 30 minutes — what should I do?](#4-first-30-minutes--what-should-i-do)
5. [Glossary — the words that come up constantly](#5-glossary--the-words-that-come-up-constantly)
6. [Lifestyles, laws, policies](#6-lifestyles-laws-policies)
7. [Player how-to](#7-player-how-to)
8. [Per-system FAQ](#8-per-system-faq)
9. [Edge cases & frequent confusions](#9-edge-cases--frequent-confusions)
10. [Mod compatibility](#10-mod-compatibility)
11. [Save-game safety](#11-save-game-safety)
12. [Reporting bugs](#12-reporting-bugs)
13. [Credits & license](#13-credits--license)

---

## 1. What is Banner Kings — Redux

Banner Kings is a deep simulation overlay on top of Bannerlord's Campaign. Where
vanilla treats settlements as resource nodes and clans as hero bags, BK adds:

- **Population simulation** — every settlement has serfs, slaves, craftsmen,
  nobles. Classes grow and shrink based on policies, food, raids, and laws.
- **Feudal titles** — a hierarchy of Empires → Kingdoms → Duchies → Counties →
  Baronies → Lordships, each with deeds, claimants, succession rules, and
  contracts.
- **Education** — heroes have languages, books, scholarship, and lifestyles
  (skill-line specializations that grant escalating perks).
- **Estates** — clan-owned, hero-managed land within villages that produces
  income and food and can be inherited or sold.
- **Council & courts** — clans and kingdoms have appointed officers (Marshal,
  Steward, Chancellor, Spymaster, Court Physician) with real effects on
  recruitment, taxes, diplomacy, and hero recovery.
- **Mercenary contracts, criminality, gentry, knighthood** — many smaller
  systems woven through the campaign loop.

This **Redux fork** brings the mod current with Bannerlord v1.3.x and adds
native support for the **War Sails (NavalDLC)** Nord faction, including:

- A full Nord title hierarchy (kingdom → 2 duchies → 4 counties → 9 baronies)
- Three Nord seafaring lifestyles — Jomsviking, Drakkar Captain, Sjofarandi —
  with real perks that affect naval combat, party speed at sea, and spotting
- The **Nordic Thrall Law**, a culture-specific demesne law that makes the
  Nord economy lean hard into raid-based slavery and slave trade
- Crash hardening so the mod runs cleanly with or without War Sails installed

---

## 2. Installing

### Where to download

[github.com/GIO443/bannerlord-banner-kings-redux/releases/latest](https://github.com/GIO443/bannerlord-banner-kings-redux/releases/latest)
— click the most recent release, expand *Assets*, and download the zip.
GitHub Releases is currently the authoritative source while the Nexus
page is hidden.

### Requirements

- **Mount & Blade II: Bannerlord v1.3.x** (build 110062 or later)
- **Harmony** — `Bannerlord.Harmony`
- **ButterLib** — `Bannerlord.ButterLib`
- **UIExtenderEx** — `Bannerlord.UIExtenderEx`
- **MCM (Mod Configuration Menu)** — `Bannerlord.MBOptionScreen`
- *Optional:* **War Sails (NavalDLC)** — TaleWorlds DLC. If installed, the
  Nord title hierarchy, seafaring lifestyles, and Nordic Thrall Law activate.
  The mod runs fine without it.
- *Strongly recommended:* **Better Exception Window**
  ([Nexus link](https://www.nexusmods.com/mountandblade2bannerlord/mods/2032)).
  Replaces vanilla Bannerlord's terse crash dialog with a detailed HTML
  crash report (full stack, inner exception, loaded modules, harmony
  patches). Without it, any crash you hit gives us nothing to debug
  from. Install it before you start a save you care about.

### Steps

1. Install the four required mods above.
2. **Remove any existing `Modules/BannerKings/` folder**, if present from a
   previous install of the original BK. Redux is a separate module and saves
   are not interchangeable. Pick one.
3. Drop the contents of the release zip into your Bannerlord install. You
   should end up with
   `…/Mount & Blade II Bannerlord/Modules/BannerKings.Redux/`.
4. Enable **Banner Kings — Redux** in the launcher and place it after the
   four required dependencies.
5. **Start a fresh save.** Saves from the original BK will not load on Redux.

### Sub-mod compatibility

**Sub-mods built against the original Banner Kings are not supported.** This
specifically includes Cultures Expanded and any mod that derives from the
original BK release. They will likely crash or behave incorrectly on Redux.

---

## 3. What's in the mod (high-level)

### Population & economy

Every settlement carries a population data block: nobles, craftsmen, serfs,
slaves, tenants. Classes interact with each other (serfs can become craftsmen
where there's housing demand; slaves can be freed into serfs by law; craftsmen
can become gentry over time) and respond to policies you set per fief.

### Titles, vassalage, and contracts

Titles are inheritable by clan tree, can be claimed and pressed in war, and
each carries a feudal contract — government type, succession rule, gender law,
and a stack of demesne laws (slavery, draft, tenancy, council appointment, army
type). Vassals are bound per-title rather than per-kingdom: you can hold a
county under one duke and a barony under another.

### Education

Each hero has a language pool, a book they're currently reading, and a chosen
lifestyle. Lifestyles are paired-skill specializations (Bow + Athletics for
Fian, Riding + Polearm for Cataphract, etc.) that grant escalating perks at
progress thresholds. Books grant skill XP and minor passive effects.

### Estates

A village contains slots that clans can buy as estates. Each estate has
tenants, food/gold output, and an inheritance line. You can hold multiple
estates across multiple villages. Estates pass on owner death via the estate
contract's inheritance rule — independently of fief succession.

### Council and court

Each clan can appoint officers — Marshal (military), Steward (economy),
Chancellor (diplomacy), Spymaster (intrigue), Court Physician (health). Each
position costs influence/gold to fill and has tier-by-tier effects. Kingdoms
also have a kingdom-level council available to the ruling clan.

### Diplomacy, demands, and interest groups

Within each kingdom, sub-factions (interest groups — radicals, moderates,
zealots, traders, etc.) issue demands: claimant pressure, council positions,
policy changes, secession, title transfers. Refusing a demand raises grievance.
Hitting max grievance triggers escalation: defection, secession war, or an
uprising depending on the group.

### Shipping & travel

Caravans and parties auto-board ships at known shipping lanes. Sea travel
takes real time and is faster under seafaring perks (Drakkar Helmsman).

**AI lords now use ships.** When an AI lord party (or an entire AI army)
arrives at a port whose shipping lane connects to its target, the party
boards via the same vanilla "Set sail" call the player uses, sails across,
and disembarks at the destination port. Armies travel intact — the
vanilla `IsCurrentlyAtSea` cascade keeps sub-parties attached to the
leader. Before this, AI lords would buy ships and never use them; you'd
see them stuck at the coast.

The auto-disembark only fires for **AI lord parties** Banner Kings put at
sea — vanilla NavalDLC convoys, caravans, and bandit ships use their own
naval AI and are left alone. Earlier builds disembarked everything on
port arrival, which left convoys in land mode while still geometrically
on water and stranded them on the coast.

Caravan and player sea travel uses Banner Kings' own ticker (a
behaviour-level hourly check rather than a per-party tick). This means
caravans that suspend themselves mid-voyage still get re-checked every
hour and arrive on schedule — earlier builds lost the per-party tick once
the caravan deactivated, and the caravan would sit on the coast forever.

**Adaptive shipping costs (v1.6.1).** Routes are graph-aware *and* react
to the current world state. Each shipping edge is weighted by raw map
distance × a risk multiplier that combines:

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

Diagnose the live state in-game with these console commands:

- `bannerkings.shipping_topology` — connected components, bridge ports,
  diameter, **and current risk hotspots** (edges with multiplier > 1.10).
- `bannerkings.shipping_path <fromId> <toId>` — static shortest path
  ignoring risk.
- `bannerkings.shipping_risk_path <fromId> <toId>` — side-by-side
  comparison of the static route vs the adaptive route a player-faction
  caravan would actually take given the current world state. Use this
  when a caravan is taking a surprising path.

**Test-scenario commands (v1.6.1.2).** A small suite of cheats for
forcing world state instead of waiting for it. Cheats must be enabled
in the launcher; otherwise these are inert. Composable — run them in
sequence to set up a specific situation:

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

**Quest-mandated overloaded fleets.** The War Sails Northern Crossing
quest hands you ~190 troops on a fleet with ~50 crew capacity, which
under vanilla NavalDLC's −74% over-crew speed penalty would floor you at
speed 1. Banner Kings — Redux clamps that penalty at −50% so the quest
is traversable in finite time. Still painful to overload your fleet, but
not stranded.

### Goals

Long-running goals frame the late game: found a culture-specific empire, restore
a deposed dynasty, reach a population benchmark, or build the largest
mercantile network.

### Raiding, slavery, and Nord economy *(War Sails)*

The Nords lean hard into the raid-economy loop. The Nordic Thrall Law applies
automatically to Nord realms and amplifies slave demand by 80%, making Nord
ports the most profitable place in Calradia to sell prisoners as slaves. Nord
towns run slave caravans regardless of policy. Combined with the Drakkar
Captain lifestyle, raiding and selling becomes a viable Nord economic build.

---

## 4. First 30 minutes — what should I do?

If you've never played BK before, the systems can feel overwhelming. A
condensed onboarding:

1. **Pick a starting culture you want to commit to.** Some lifestyles, laws,
   and opportunities are culture-locked. Vlandian → Ritter / feudal lord
   ergonomics. Battanian → Fian / woodland skirmisher. Aserai → Jawwal /
   slave economy. Nord → seafaring + raid-economy build *(War Sails only)*.
2. **Pick a lifestyle on the BK Education tab.** It locks once chosen, so
   read each one's bonuses (the picker tooltip now shows them at the top).
   Skill values matter: you need at least 15 in each of the lifestyle's two
   skills to start.
3. **Visit a tavern in any cultural capital and find the book seller.** Buy
   one book in a language you understand. Books grant slow skill XP that
   accumulates while you carry them.
4. **Skip estates until you have a clan tier 2.** They're expensive and take
   time to pay off. Start with a workshop in an active town; the income is
   immediate and reliable.
5. **Vassalize before founding a kingdom.** A barony under an existing king
   is a stable platform to grow the clan and learn the contract system. Going
   independent too early is a brutal multifront war.

---

## 5. Glossary — the words that come up constantly

These are the terms most likely to trip up new players.

- **De jure** — the *legal* owner of a title (held by the bearer's clan tree
  even if their kingdom doesn't physically control the fief).
- **De facto** — the *actual* current controller, derived from settlement
  ownership. A hero can hold a title de jure while another holds it de facto;
  this is a casus belli.
- **Demesne** — the personal fiefs and estates a clan directly administers
  (vs. fiefs held by sub-vassals in the same title tree).
- **Vassal** — a clan that has sworn to a liege. In BK, vassalage is
  per-title, not per-kingdom: you can hold a county under one duke and a
  barony under another.
- **Liege** — the higher-tier title-holder you are sworn to.
- **Contract** — the bundle of laws on a title: government type, succession
  rule, gender law, inheritance, plus 0–N demesne laws.
- **Demesne law** — toggleable rule on a title (e.g., *Slave Trade Allowed*,
  *Imperial Coronation Required*). Each is voted on by vassals.
- **Government** — Imperial / Feudal / Tribal / Republic / Theocratic.
  Affects vassal limits, taxation cap, and which decisions are available.
- **Succession** — Hereditary Monarchy / Elective Monarchy / Republican
  Election / Theocratic. Determines how the title passes on holder death.
- **Gender law** — Agnatic (male only), Cognatic (eldest regardless of
  gender), Agnatic-Cognatic (male-preferred), Enatic (female only).
- **Lifestyle** — paired-skill specialization gating perks (Cataphract =
  Riding+Polearm, Outlaw = Roguery+Crossbow, etc.).
- **Scholarship** — flag set when a hero has any of four research perks
  (ScholarshipMechanic / Accountant / NaturalScientist / Treasurer). Required
  to enter the Scholar lifestyle.
- **Notable** — a non-noble settlement personality (Rural Notable, Headman,
  Gang Leader, Merchant). Drives recruitment, quests, and prosperity.
- **Gentry** — minor landed family, below clan tier 1. Often a notable's
  promoted relatives. Can be sponsored into a vassal clan.
- **Knight (BK sense)** — a hero granted knighthood by a clan, becoming a
  tier-1 vassal clan with a fief grant and an oath. Distinct from the
  vanilla "knight" troop tier.
- **Estate** — a sub-property inside a village owned by a clan, with
  tenants, food/gold output, and an inheritance line.
- **Council** — clan-level officer board: Marshal, Steward, Chancellor,
  Spymaster, Court Physician.
- **Peerage** — kingdom-level political tier (Full Peer / Partial Peer / No
  Peer). Determines voting rights on kingdom decisions.
- **Interest group** — sub-faction within a kingdom (radicals, moderates,
  zealots, traders). Issues demands; can defect.
- **Demand** — formal pressure from an interest group: claimant, council
  position, policy change, secession, title transfer.
- **Claim** — a hero's pressed right to a title, justifying war.
- **Custom troop** — player-designed mercenary unit (culture, equipment,
  skills, formation). Costs roughly 3× vanilla wage by design.

---

## 6. Lifestyles, laws, policies

### Lifestyles

Pick one on the BK Education tab. Each has two linked skills; you must reach
at least 15 in both to adopt it. Both skills must be exercised — only the
*lower* skill contributes to progress per tick.

**Core lifestyles** (always available):

| Lifestyle | Skills | Theme |
|---|---|---|
| Fian | Bow + Athletics | Battanian woodland skirmisher |
| Cataphract | Riding + Polearm | Heavy lancer cavalry |
| August | Charm + Leadership | Imperial statesman |
| SiegeEngineer | Engineering + Crossbow | Siege specialist |
| CivilAdministrator | Steward + Trade | Realm bureaucrat |
| Caravaneer | Trade + Scouting | Long-distance trader |
| Artisan | Crafting + Smithing | Master crafter |
| Outlaw | Roguery + Crossbow | Bandit chief |
| Mercenary | Two-Handed + Tactics | Sellsword captain |
| Kheshig | Bow + Riding | Khuzait elite horse-archer |
| Varyag | One-Handed + Two-Handed | Sturgian raider |
| Gladiator | Athletics + One-Handed | Arena fighter |
| Ritter | Polearm + Athletics | Vlandian heavy knight |
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

### Demesne laws

Toggleable on a title's contract. The slavery laws are illustrative:

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

### Per-settlement policies

You set these directly from the BK settlement panel. They take effect daily.

| Policy | Options |
|---|---|
| Tax | Standard / High / Low / Exempted |
| Militia | Balanced / Melee / Ranged |
| Draft | Standard / High Draft / No Draft |
| Garrison | Standard / Reinforce / Disband |
| Workforce | Construction / Production / Martial |
| Criminal | Lenient / Standard / Strict |

---

## 7. Player how-to

> The how-to section is **procedural** — every entry is a sequence of
> menu paths, clicks, and visual feedback you should see along the way,
> with the most common failure modes called out. Skim the headings to
> find the goal you want, then follow the steps top-to-bottom.

### How do I claim a title?

**1. Get the claim.** A claim is a *right* — you don't have it by default
on most fiefs. Four ways to acquire one:

- **Inheritance.** A relative who held the title dies, and the contract's
  inheritance rule passes the claim to you. Watch the encyclopedia notes
  on family death events; if you've inherited a claim, it'll appear in
  your hero's "Claims" tab on the BK character panel.
- **Marriage.** When you marry, your spouse's claims transfer to your
  family per the realm's gender law (Cognatic = both spouses, Agnatic =
  male only, etc.). Check the spouse's encyclopedia entry for their
  claims before proposing.
- **Grant.** A title's current holder can grant the claim to you in
  exchange for influence and gold. Talk to the holder, choose
  *I have a request — give me a claim on…* if available.
- **Fabrication.** Appoint a Chancellor with at least Roguery 100 to
  your council, then on the council screen choose
  *Council tasks → Fabricate claim → [target title]*. Takes ~60 in-game
  days and uses influence weekly. Failure is possible if Roguery is low.

**2. Press the claim.** Open the kingdom's diplomacy screen → *Declare
war* → select *Casus belli: Press claim of [title]*. War starts. Win
the war and end it via the diplomacy demand system, and the claim
resolves automatically on the next succession tick. Alternative: if
you already de-facto control the title's underlying settlements, the
claim resolves at the next BK title-tick (no war needed).

**Failure modes to watch for:**
- "No claim available" on the war declaration — the claim wasn't fully
  registered. Check the BK character panel's Claims tab.
- War goes inconclusive — the title doesn't transfer until you actually
  win the war's named demand.

### How do I become a vassal?

1. Pick a kingdom whose culture and contract you can live with.
2. Travel to that kingdom's ruler (the encyclopedia lists their current
   location).
3. Talk to them → choose *I'd like to join your service*.
4. They'll offer one or more titles under their crown — typically a
   barony or county. Each offer shows the contract terms (taxes owed,
   levies expected, gender law, succession rule).
5. Accept the offer that fits. You're now bound to their contract.

**Common gotcha:** if you're already at war with a kingdom you want to
serve, peace out first. Hostile relations block the offer dialog.

### How do I start my own kingdom?

Two BK paths:

**Founding via empire goal:**
1. Hold the required count of culture-matched fortifications (varies by
   culture; check the *Goals* tab in the character panel).
2. Open the *Goals* tab → select *Found [Culture] Empire* → click *Begin*.
3. Travel to the foundation site (the goal's UI shows it).
4. Pay the influence + gold cost shown.
5. The foundation ceremony plays. Your clan becomes the founding house of a
   new kingdom-tier title.

**Usurpation:**
1. Take the kingdom-tier title from an existing realm via claim + war
   (see "How do I claim a title?").
2. Once you hold the kingdom title, open the *Court* tab → *Reissue
   contract* — costs influence and triggers a vassal vote. Your vassals
   may ratify or rebel.

### How do I get an estate?

**Buy:** At a village's BK menu → *Banner Kings → Manage estates → Buy
estate*. Browse the estates list (ownership, tenant count, food/gold
output). Select one → confirm price (scales with land size and tenants).

**Grant:** While serving a liege, ask them in conversation —
*I have a request → grant me an estate*. They'll offer vacant estates.

**Inherit:** Passes via the estate's inheritance line on the owner's
death (configured per estate's contract).

**Seize:** Available to a liege when a vassal's estate becomes claimable
— owner died heirless, treason, banditry, etc. *Court → Estates → Seize*.

### How do I trade in a castle?

Banner Kings — Redux re-enables castle trade (vanilla castles have no
workshops or markets, but BK's economy populates a castle's item roster
through the population/economy system).

1. Enter the castle.
2. Open the BK menu → *Banner Kings*.
3. Select *Trade* — this opens an inventory exchange against the castle's
   stocks.

You won't see workshop slots in a castle, but caravans can visit and the
castle stocks slowly accumulate trade goods. Useful for offloading
random loot far from the nearest town.

### How do I sell prisoners as slaves?

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

### How do I get into a kingdom's council?

1. Be a vassal of that kingdom (see *How do I become a vassal?*).
2. Open the BK kingdom screen → *Council* tab.
3. The kingdom council shows positions — Marshal, Steward, Chancellor,
   Spymaster, Court Physician — with current holders and their tenure.
4. If a position is vacant, talk to the kingdom ruler → *I'd like to
   serve as [position]*. Skill check applies; if you don't have enough
   skill or the right traits, the option is greyed.
5. If a position is held by another clan, propose a *Council position
   demand* via the interest-group system. Costs influence and triggers
   a vote.

### How do I appoint council members (my own clan)?

1. Open the BK character panel → *Court* (or *Council* in some BK
   builds — same screen).
2. Each position lists candidates from your clan and your courtiers
   with their skill rating, relation to you, and traits.
3. Select a candidate → click *Appoint*. Costs influence and applies a
   small relation adjustment to other candidates who weren't picked.
4. To demote, select the current holder → *Dismiss*. Costs influence
   and a relation hit with the dismissed hero.

### What does a Marshal actually do?

Reduces party wage costs across the realm, boosts settlement levy
sizes, and slightly improves army cohesion. To verify the modifier is
applying:

1. Open your party screen.
2. Hover the daily wage figure in the bottom strip.
3. The breakdown tooltip lists modifiers — you should see a Marshal
   line if the realm's Marshal is active and your party qualifies.

If the Marshal line is absent, your clan tier or party type may be
ineligible for the bonus.

### How do I hire a custom mercenary unit?

1. Open the BK character panel → *Mercenary* tab → *Custom troop*.
2. Pick a culture (sets the accent, naming pool, and available equipment).
3. Pick a formation class — Infantry, Ranged, Cavalry, HorseArcher.
4. Open the equipment editor: drag items from your inventory into the
   troop's slots, or click *Buy* on any slot to pull from market stocks.
5. Click *Confirm* — pays the hire price (one-time), and the unit is
   added to your clan's recruitable pool.
6. Going forward, the unit appears in towns where your clan has
   notable connections, with daily wage roughly 3× a vanilla
   equivalent. They're meant as elite fillers, not core composition.

### How do I make money? *(by yield per hour of attention)*

1. **Workshops + estates** combined in the same town/village pair —
   buy a workshop in a town, then an estate in one of its bound
   villages. The village feeds the workshop input, the workshop turns
   it into output, and you collect both ends.
2. **Caravans** — vanilla flow, with BK trade-route modifiers slightly
   improving margins.
3. **Tournament prize riding** (early game) — visit any town with an
   active tournament, win, take the prize.
4. **Mercenary contracts** to a wealthy kingdom (mid game) — talk to
   their ruler about service; payment is daily.
5. **Raiding** — strongest with a raid-focused lifestyle (Outlaw,
   Mercenary, Varyag, Jawwal, Kheshig, Drakkar Captain).
6. **Selling prisoners as slaves** — see the dedicated section above.
   Best in Nord and Aserai markets with the right laws.
7. **Custom mercenary contracts** sold to AI clans (late, complex) —
   create a custom troop, then offer it to other clans through the
   mercenary tab's contract menu.

### How do I make money?

Ranked roughly by yield per hour of attention:

1. **Workshops + estates** combined in the same town/village pair.
2. **Caravans** (vanilla + BK trade modifiers).
3. **Tournament prize riding** (early game).
4. **Mercenary contracts** to a wealthy kingdom (mid).
5. **Raiding** — strongest with a raid-focused lifestyle (Outlaw,
   Mercenary, Varyag, Jawwal, Kheshig, Drakkar Captain).
6. **Selling prisoners as slaves** to high-demand markets (Aserai, and
   especially Nord ports under the Nordic Thrall Law). 150 gold base per
   prisoner, with up to ±50% on demand and law multipliers.
7. **Custom mercenary contracts** sold to AI clans (late, complex).

### Is slave trading and raiding profitable?

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

### How does the village raid capture system work?

When the **Raid Capture System** is enabled in MCM (default on), every
village you raid produces a *captive caravan* on top of vanilla raid
damage. The vanilla raid still hits hearths and prosperity exactly as
before — the captives are conceptually drawn from the already-displaced
cohort, so the source village is *not* damaged extra. The caravan ships
captives to your nearest friendly fief, and on arrival they enter the
local population either as Slaves or as Serfs depending on your toggle.

**1. Set your defaults.** When you walk up to a hostile village (the
`village_hostile_action` menu, the same one with "Raid the village" and
"Loot the village"), three new lines appear above the raid options:

- `Captives: Take` / `Captives: Leave` — click to flip. Sticky per clan.
  Default Take if your clan's realm has slavery, Leave otherwise.
- `Disposition: Slaves` / `Disposition: Serfs` — only shown if Captives
  is set to Take. Sticky per clan. Default Slaves under slavery realms,
  Serfs otherwise.
- `Estimated captives: ~N` — read-only preview computed from village
  serf population. Helps you decide whether the raid is worth setting up
  for capture.

**2. Run the raid.** Choose "Raid the village" as normal. When the raid
completes, BK applies your toggles:

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

**3. Arrival.** When the caravan reaches its destination, captives are
absorbed into the receiving fief's population (Slaves or Serfs per your
toggle), each cohort credited under its *own* culture in `CultureData`.
You receive a lump-sum payout to your hero — full slave price for Slaves,
~55% of slave price for Serfs.

**4. Disposition legality.**

- **Independent clan** (no kingdom): both Slaves and Serfs always legal.
- **Realm with `SlaveryNord` / `SlaveryAserai` / criminal Enslavement**:
  both legal, default Slaves.
- **Realm without slavery**: Serfs legal; Slaves shows
  *"Slaves (UNLAWFUL)"* — you can still pick it for the higher payout, but
  expect a criminal rating tick, relation hit with your kingdom's ruler,
  and influence loss per caravan. Profit beats penalty for one-off
  captures; sustained illegal slaving will cost more than it earns.

**5. Foreign mercenaries.** If your raid leader's culture differs from
your employing kingdom's culture (e.g. a Sturgian captain serving Vlandia),
20% of captives are skimmed for the captain's private benefit:

- Independent merc (no employer kingdom): instant gold payout, no
  secondary caravan.
- Kingdom-affiliated foreign captain: a *second*, smaller caravan spawns
  to the captain's clan home. Both caravans are interceptable.

**6. Intercepting enemy caravans.** Hostile captive caravans appear on the
map and can be attacked like any party. Defeating one releases the
captives (no transfer to your fief) — useful for harassing slaver realms.

**7. Demographic warfare.** Because captives keep their original culture
and feed the destination's `CultureData`, sustained raiding visibly
reshapes both sides over decades:

- **Donor settlements** lose pop biased toward their own culture (your
  culture is excluded), so a raided foreign town slowly purifies toward
  the *raider's* cultural minority over many raids.
- **Receiver settlements** gain a foreign-culture cohort with low
  acceptance (0.20). The next-tick weight recompute shifts assimilation
  in their favor; over many caravans, visible foreign pockets form in
  your towns, with all the loyalty/recruit-pool consequences that come
  with cultural mismatch.

**8. Toggling the system off.** Open MCM → Banner Kings → Slavery →
*Raid Capture System*. With it off, only the existing slavery system
runs (criminal-policy Enslavement on prisoner sale, `decision_slaves_export`
slave caravans). Existing saves remain compatible either way.

**Gotchas:**

- Raid leader's *culture* (not their kingdom's) decides the cohort
  exclusion. Mercenary captains carry their own culture into this rule.
- Bandits never produce captives — only player and AI lord raids do.
- If no friendly town/castle exists (besieged, all-hostile, etc.), the
  caravan routes to your clan's home settlement as a fallback.
- Caravans are not invincible. Plan to escort them home if you raided
  deep in enemy territory.

---

## 8. Per-system FAQ

### Population

**Q: Why is my settlement losing population?**
Common causes: food shortage (check granary + village output), high tax
policy (drives serfs to flee), recent raid (halves growth for ~30 days),
failed siege defense, slave overrun (slave class cap exceeded triggers
riots).

**Q: How do classes transition?**
Daily ticks evaluate per settlement: serfs can become craftsmen if there's
craftsman housing demand, slaves can be freed into serfs by demesne law,
craftsmen can be promoted to nobles via the gentry pipeline.

**Q: What does "settlement issue" mean?**
A flagged condition (food shortage, slave overrun, mood collapse, etc.).
Resolve it via the relevant policy lever or by addressing the underlying
cause.

### Titles

**Q: My heir is the wrong person — why?**
Inheritance order is decided by the contract's `Inheritance` rule and gender
law. *Primogeniture + Cognatic* = eldest child. *Primogeniture + Agnatic* =
eldest male; if no males, bypasses to brother before daughter. *Seniority*
= oldest living clan member by birth date.

**Q: Can I change a title's contract?**
Yes, via the kingdom decision system. Costs influence, takes time, and can
be vetoed by vassals via the demand system.

**Q: What's the difference between Empire and Kingdom?**
Empire (tier 1) is a multi-kingdom super-realm (Western/Northern/Southern
Empire in vanilla). Kingdom (tier 2) is the realm tier most factions sit at.
Empire-tier titles unlock through the Empire foundation goal.

### Education

**Q: How do I pick a lifestyle?**
Character → BK Education tab → Lifestyle dropdown. Locked once chosen until
that lifestyle is fully completed (5 perk tiers) or a respec is performed
(rare and very expensive).

**Q: Why is my lifestyle progress so slow?**
Both linked skills must be exercised — only the *lower* of the two
contributes per tick. Pure cavalry play barely advances a Cataphract
because Polearm doesn't tick when you don't melee.

**Q: Where do I get books?**
Tavern book sellers (one in every cultural capital tavern) or as quest
rewards.

**Q: I see "Jomsviking" / "Drakkar Captain" / "Sjofarandi" in the
lifestyle list — what are those?**
Three Nord-restricted seafaring lifestyles that only appear when both the
**War Sails (NavalDLC)** module is loaded *and* the Nord culture exists.
Jomsviking is a shieldwall infantry build, Drakkar Captain is a longship
commander, Sjofarandi is a coastal scout/archer. Naval-specific bonuses
trigger on War Sails sea scenes; the rest apply on land too.

**Q: Why don't I see the bonuses listed when I hover a lifestyle I can't
take yet?**
You should — the picker tooltip shows bonuses first, then perks, then lore,
then any unmet-requirement reasons last. If they appear cut off, that's
vanilla truncating long disabled-entry tooltips; the bonus block is always
the first thing in the string so it survives the truncation.

### Diplomacy & demands

**Q: An interest group is demanding something — what happens if I refuse?**
Each refusal raises grievance level. Hitting max grievance triggers
escalation: defection, secession war, or claimant uprising depending on
group type.

**Q: What's "support on decision" relation?**
When you side with a clan in a kingdom decision, they gain a relation
modifier with you (+8 to +25 by support strength) for 5 years.

### Shipping & travel

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
You shouldn't be on Redux. Vanilla NavalDLC penalises overloaded fleets
harshly — the Northern Crossing quest hands you ~190 troops on a fleet
with ~50 crew capacity, which would normally hit −74% speed. Redux
clamps that penalty at −50% so the quest is traversable in finite time.
The "Overmanned" line in your speed tooltip should now show roughly
−50% rather than worse.

**Q: I defeated an enemy caravan but got nothing — bug?**
*Was* a bug. The 1.3.x port broke the caravan loot dialog (it was
deleting the cargo instead of giving you a loot screen). Redux v1.5.2+
restores it: surrendering or captured caravans now open a real loot
screen for cargo, and a separate prisoner screen for their troops, the
same way they did pre-1.3.x.

**Q: Why aren't AI armies forming all the time anymore?**
Earlier Redux versions (and original BK pre-1.3.x) had a bug where AI
lords formed Patrolling-type armies with no target settlement. Vanilla
AI dispersed them within days for "no purpose", and BK pushed again as
soon as influence rebuilt — a recruit→march→disband loop that wasted
influence and never fielded an actual force. Redux v1.5.2+ replaces
that with a target-quality gate: AI only forms an army when there's a
real objective (friendly fief under siege within 350 units → Defender,
or enemy fortification within 280 units → Besieger). With no target,
BK doesn't push and vanilla AI runs unmodified. The result is fewer
but more purposeful AI armies. There's also an MCM toggle
(Performance → AI Army Formation) to disable the BK push entirely if
you want pure vanilla AI behaviour.

### Mercenaries & combat

**Q: Custom troop daily wage seems insane.**
By design, ≈ 3× vanilla. The hire price is also higher. They're meant to be
elite fillers, not core army composition.

**Q: Where do BK perks apply?**
At model evaluation time wherever vanilla reads the same skill effect. The
seafaring perks specifically also hook into War Sails' naval game models.

### Slavery & raiding *(Nord-flavoured paths)*

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

---

## 9. Edge cases & frequent confusions

- **"My title disappeared"** — usually inherited by an heir on a death you
  didn't notice, or absorbed into a higher-tier title via succession. Check
  the title event log in the encyclopedia → titles tab.
- **"BK menu is empty"** — the feature was disabled in the MCM settings.
  Re-enable and reload the save.
- **"Crash on entering a Nord settlement"** — only on pre-fix or
  non-Redux builds. Update to the latest Banner Kings — Redux release;
  the Nord null-guards are bundled.
- **"My lifestyle locked at Scholar"** — Scholar requires the scholarship
  gate (any of ScholarshipMechanic, Accountant, NaturalScientist, Treasurer).
  Without it, progress doesn't tick.
- **"Council Marshal didn't reduce wages"** — the reduction is
  multiplicative; other modifiers (custom troop, mercenary status) can
  dominate. Check the wage tooltip breakdown in the party UI.
- **"Estate showing zero income"** — daily ticks accumulate but income posts
  weekly. Or the estate has no tenants — check the estate panel.
- **"Can't change demesne law"** — locked behind a contract-change cooldown
  (≈ 1 in-game year) and minimum loyalty / authority gates.
- **"Skills level too fast in Banner Kings"** — older builds shipped
  with the *Alternative Leveling* MCM toggle on by default, and its XP
  curve only added ~20 XP per level past level 1, so any small XP gain
  rocketed you through 10+ levels. The toggle now defaults **off** (vanilla
  XP curve), so a fresh start with 1 focus point in Leadership behaves
  the same as vanilla. Existing saves: open MCM → BannerKings →
  Balancing → uncheck *Alternative Leveling*.
- **"Language learning finishes instantly"** — symptom of the same
  alternate-leveling explosion (Scholarship XP racing up boosted the
  language-rate skill effect off the rails) plus an unsafe rate path.
  Per-tick fluency gain is now hard-capped at 5%, so even with the worst
  rate inputs a language can't finish in fewer than ~20 in-game days.
- **"My new game crashes during loading"** — almost always a non-BK mod's
  Harmony patch failing (e.g., GovernorsHandleIssues against newer
  Bannerlord builds). Install **Better Exception Window** if you haven't
  already (see *Reporting bugs* below) and read the inner exception in
  the crash report — it usually names the offending mod by its patch
  method, and you can disable that mod and continue.

---

## 10. Mod compatibility

Banner Kings detects other mods at startup and yields its overlapping
features to them where appropriate. The detection is automatic; no
configuration needed.

| Mod | Behaviour |
|---|---|
| **War Sails (NavalDLC)** | Natively supported. Nord titles, succession, language, null guards, naval perk hooks, seafaring lifestyles, Nordic Thrall Law all built in. |
| **Diplomacy** | BK yields its diplomacy model (war support, war proposal, alliance handling) so Diplomacy's UI runs cleanly. BK still tracks pacts and casus belli internally for title/claim logic. |
| **Improved Garrisons** | BK skips its garrison auto-recruitment override so IG can manage garrison composition. BK's patrol-party feature (separate from garrison composition) still draws troops from the garrison; toggle it off in MCM if it conflicts. |
| **Recruit Everywhere** | BK skips its volunteer-recruitment overrides so RE owns the volunteer pool. |
| **MarryAnyone** | BK skips its marriage model so MA's relaxed rules apply. |
| **Buy Land at Villages** | Both can coexist; the player can hold both BK estates and BLAV land in the same village, which can be confusing. Pick one or the other in practice. |
| **Realistic Battle Mod (RBM)** | Full compat. BK's campaign-side combat XP / battle reward / battle simulation logic stays; RBM owns mission-time damage. |
| **AI Influence (AI Diplomacy)** | BK yields its `InfluenceModel` to AI Influence on the vanilla GameModel slot, so the LLM-driven diplomacy / influence calculations can run cleanly. BK's internal influence queries (caps, costs for council appointments, claims, demands, knighthoods) still resolve through BK's own model so titles and claim logic continue to work. No configuration needed; detection is automatic. |

**Compatible without configuration** — these touch different layers and have
no overlap with BK:

- RTS Camera / Family Tree / Settlement Icons / Better Time / Realistic Weather
- Open Source Armory / Saddles / Banner Color Persistence
- Custom Spawns / Calradia at War (BK's bandit behaviour doesn't override
  spawn templates)
- Serve as Soldier (different code path)
- BetterExceptionWindow / Adjustable Troop Selection

**Compatible but watch for stacking**:

- **Distinguished Service** — both touch combat XP. May stack; disable BK's
  combat XP model in MCM if you don't want the bonus stacked.
- **Bannerlord Tweaks** — patches widely. Usually fine if loaded after BK.
  If a tweak silently reverts a BK behaviour, it loaded later — adjust
  launcher order.
- **Heroes Must Die** — both listen to hero death. If title succession looks
  wrong with HMD, set HMD to load *after* BK.
- **Calradia Expanded / CE Kingdoms** — adds new factions that don't have BK
  title data. Currently only Nord null-guards exist; new factions may crash
  or load with empty BK data.
- **Detailed Character Creation** — overlaps with the BK campaign-start hooks.
  Test the prologue thoroughly when both are installed.

**Not compatible**:

- **Sub-mods built against the original Banner Kings** (Cultures Expanded,
  etc.). They target the upstream BK release and are not compatible with
  Redux.

**Recommended load order** (the launcher will sort this automatically if
you've enabled all of them):

```
Harmony → ButterLib → UIExtenderEx → MCM
Native → SandBoxCore → SandBox → StoryMode → CustomBattle
NavalDLC (if installed)
Banner Kings — Redux
Diplomacy / Improved Garrisons / Recruit Everywhere / MarryAnyone /
Buy Land at Villages / RBMCombat / etc.
Bannerlord Tweaks / cosmetic mods / etc.
```

---

## 11. Save-game safety

- **Saves are version-tagged.** Loading a save from an older Banner Kings
  Redux build runs a migration where defined; otherwise old fields keep
  their values and new fields lazy-init to safe defaults.
- **Removing BK from an active save is not safe.** References to BK objects
  (titles, estates, custom troops) become orphaned and the save will corrupt.
  Once you start a save with BK, keep BK installed for the life of that save.
- **Updating BK on an active save is generally safe within a minor version**
  (e.g., v1.5.0 → v1.5.1). Major-version updates (e.g., upstream BK →
  Redux, or a future v2.x) may require a fresh save.
- **Switching from upstream Banner Kings to Banner Kings — Redux on an
  existing save is not supported.** The two are separate modules with
  separate save data. Start fresh.

---

## 12. Reporting bugs

### Install Better Exception Window first

Before you submit a crash report — and ideally before you even play a
save you care about — install **Better Exception Window** from Nexus
([Bannerlord.BetterExceptionWindow](https://www.nexusmods.com/mountandblade2bannerlord/mods/2032)).
It replaces vanilla Bannerlord's terse crash dialog with a detailed HTML
crash report that lists the full stack trace, the inner exception, all
loaded modules with versions and load order, and the harmony patches
attached to the crashing method. With Better Exception Window installed,
a crash report goes from "the game crashed" to "the game crashed in
*[mod X's]* patch on *[method Y]*" — which is what we need to do
anything with the report.

Without it, a crash drops you back to the desktop with a message box
that says nothing useful, and there's nothing for us to debug from.

### What to send

A useful crash/issue report includes:

1. **The Better Exception Window crash report HTML.** This is the file
   produced by the Better Exception Window mod when the game crashes;
   the filename is whatever Better Exception Window writes (often a
   timestamped `.html` in the BEW output directory, or whatever name
   you save it under). Open it once before sending and look at the
   "Reasons" and "Inner Exception" sections — they often name the
   offending mod immediately, and you can sanity-check that the report
   isn't blank before you upload it.
2. The last few hundred lines of `rgl_log.txt` (Bannerlord's general
   game log; it lives in the game's Logs directory). Banner Kings
   warnings are tagged `[BK]`.
3. Save file name, Banner Kings — Redux version (visible on the main
   menu), and whether War Sails / NavalDLC is installed.
4. Steps to reproduce, ideally from a fresh save.

**Don't bother reporting** these — they're known and harmless:

- "BUTR Harmony analyzer warnings" in the build log — false positives
  against the live 1.3.x DLLs.
- Compile warnings about obsolete types — 1.3.x deprecations not yet
  fully removed. They don't affect runtime.
- "GovernorsHandleIssues crashed" — that's a different mod failing to
  patch a method. Disable it.

Issues for this fork specifically (1.3.x compatibility, War Sails / Nord
integration, seafaring lifestyles, Nordic Thrall Law) belong on the GitHub
repo:

**https://github.com/GIO443/bannerlord-banner-kings-redux/issues**

Issues with the original Banner Kings systems (titles, estates, council,
etc. — anything also present in the original release) are upstream
BK problems. They'll get fixed in Redux as we encounter them, but the
underlying design is R-Vaccari's.

---

## 13. Credits & license

**Banner Kings is the work of [R-Vaccari](https://github.com/R-Vaccari) and
the original Banner Kings contributors.** Every system this wiki describes —
titles, councils, courts, languages, populations, estates, education,
succession, the economy rework, the framework underneath — was
designed and built by them over years of effort. The craft and the vision
are entirely theirs.

The original author has been **inactive for an extended period**; the
upstream repository has had no commits in roughly a year, and regulars on
the original mod's official Discord report no contact with R-Vaccari over
the same span. With Banner Kings no longer compiling against current
Bannerlord builds and players asking for a working version, **Banner Kings —
Redux** was put together as a community maintenance fork.

This fork's contributions on top of the original BK:

- **Bannerlord 1.3.x compatibility port** — fixing all TaleWorlds API
  breakage from the 1.3.x updates so the mod builds and runs again.
- **Native War Sails (NavalDLC) integration** — Nord titles, succession,
  language, three Nord seafaring lifestyles, naval-side perk effects,
  and the Nordic Thrall Law.
- **Crash hardening** — null-guards, mixin attachment fixes for NavalDLC's
  subclassed view models, the ItemRoster underflow clamp, the book-seller
  iteration fix, and other targeted stability work.
- **UI polish** — consolidated kingdom-screen tab, lifestyle picker bonus
  tooltips, Tax/Conquest aspect button rewire, several latent UI bugs fixed.

These are real engineering contributions but they're a thin layer on top of a
thick original — by line count, less than 2% of the codebase is Redux work.
**The mod is overwhelmingly R-Vaccari's; we are keeping it running.**

### License posture

The upstream Banner Kings repository declares no license. By default this
means R-Vaccari retains all rights over the original code. We have not
received explicit permission from R-Vaccari to fork or redistribute Banner
Kings, and we do not claim it is BSD- or MIT-licensed.

Banner Kings — Redux is published in good faith as a maintenance fork
during the upstream project's dormancy, with full credit to the original
author and an explicit standing offer:

> If R-Vaccari (or any of the original contributors) requests this fork to
> be taken down, for any reason and at any time, it will be removed
> immediately, without question, without delay, and without argument.

Thanks also to:
- The **BUTR team** — Harmony, ButterLib, UIExtenderEx, the Harmony
  Analyzer. None of this stack works without them.
- The **MCM / MBOptionScreen** maintainers.
- **TaleWorlds** for Bannerlord and the War Sails DLC.
- The **Bannerlord modding community** on Discord and Nexus, whose
  collective troubleshooting underpins half the workarounds in the codebase.

Banner Kings belongs to R-Vaccari. This fork is borrowed time, gratefully.
