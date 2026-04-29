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

## Table of contents

1. [What is Banner Kings — Redux](#1-what-is-banner-kings--redux)
2. [Installing](#2-installing)
3. [What's in the mod (high-level)](#3-whats-in-the-mod-high-level)
4. [First 30 minutes — what should I do?](#4-first-30-minutes--what-should-i-do)
5. [Glossary — the words that come up constantly](#5-glossary--the-words-that-come-up-constantly)
6. [Lifestyles, doctrines, laws, policies](#6-lifestyles-doctrines-laws-policies)
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
- **Religions** — multiple faiths with doctrines, clergy, piety, and rites.
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

### Requirements

- **Mount & Blade II: Bannerlord v1.3.x** (build 110062 or later)
- **Harmony** — `Bannerlord.Harmony`
- **ButterLib** — `Bannerlord.ButterLib`
- **UIExtenderEx** — `Bannerlord.UIExtenderEx`
- **MCM (Mod Configuration Menu)** — `Bannerlord.MBOptionScreen`
- *Optional:* **War Sails (NavalDLC)** — TaleWorlds DLC. If installed, the
  Nord title hierarchy, seafaring lifestyles, and Nordic Thrall Law activate.
  The mod runs fine without it.

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

### Religion

Heroes belong to a faith. Faiths have doctrines (some active, some passive),
divinities, and rites. Piety accrues from skill use and observance, and is
spent on rites and conversion. Settlements have dominant and minority faiths;
clergy spawn, preach, and shift adherent counts over time. Stances between
faiths (`Tolerated`, `Untolerated`, `Hostile`) drive marriage rules,
conversion costs, and casus belli.

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
takes real time and is faster under specific doctrines (Astrology) or
seafaring perks (Drakkar Helmsman).

### Goals

Long-running goals frame the late game: found a culture-specific empire, restore
a deposed dynasty, complete a faith's foundation rite, reach a population
benchmark, or build the largest mercantile network.

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
4. **Don't ignore your faith.** Talk to a clergyman in a tavern or capital
   and let your hero be inducted. Piety is a real currency in BK and starts
   accumulating immediately.
5. **Skip estates until you have a clan tier 2.** They're expensive and take
   time to pay off. Start with a workshop in an active town; the income is
   immediate and reliable.
6. **Vassalize before founding a kingdom.** A barony under an existing king
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
- **Piety** — religious-stat counterpart to influence. Spent on rites,
  blessings, and conversions; gained from prayer, sacrifices, and observance.
- **Fervor** — a faith's strength as a campaign-wide pool, driven by adherent
  count, holy site control, and active doctrines.
- **Stance** — one religion's attitude toward another: `Tolerated`,
  `Untolerated`, `Hostile`. Affects relations, conversion costs, and
  whether war can be declared on faith grounds.
- **Doctrine** — an unlockable, sometimes mutually exclusive tenet of a faith
  (e.g., *Astrology* boosts ship speed; *Reavers* awards piety from raids).
- **Lifestyle** — paired-skill specialization gating perks (Cataphract =
  Riding+Polearm, Outlaw = Roguery+Crossbow, etc.).
- **Scholarship** — flag set when a hero has any of four research perks
  (ScholarshipMechanic / Accountant / NaturalScientist / Treasurer). Required
  to enter the Scholar lifestyle.
- **Notable** — a non-noble settlement personality (Rural Notable, Headman,
  Gang Leader, Preacher, Merchant). Drives recruitment, quests, and
  prosperity.
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

## 6. Lifestyles, doctrines, laws, policies

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

### Doctrines (a sample)

Doctrines are tenets of a faith. Some are passive (constant effects), some are
active (votable, mutually exclusive). Selected high-impact ones:

| Doctrine | Effect |
|---|---|
| Astrology | Sea travel ~25% faster |
| Tolerant | Reduces hostile-stance penalties; eases conversion |
| Esotericism | Bonus to scholar lifestyle XP, hidden rites |
| Reavers | Raid output and morale bonus; piety from raiding |
| Warlike | Combat XP and morale boosts |
| Pacifism | Morale penalty in offensive war, peace influence boost |
| Sacrifice | Human sacrifice rite; piety surge, relation hits |
| HeathenTax | Surcharge on out-of-faith notables in your settlements |
| Childbirth | Increased fertility for adherent clans |
| Pastoralism | Herd animal bonuses in villages |
| Druidism / Animism | Tribal-only nature worship doctrines |

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

### How do I claim a title?

You acquire a claim by:
- **Inheritance** — a parent or relative dies and the claim passes to you.
- **Marriage** — your spouse's claim transfers per the realm's gender law.
- **Grant** — the current holder grants the title to you for influence + gold.
- **Fabrication** — a Chancellor of high enough skill on your council can
  forge a claim over time.

You **press** a claim by:
- Declaring war on the holder using the claim as casus belli, winning, and
  taking the fief.
- Or, if you're already the de facto holder of the underlying settlements,
  the claim auto-resolves on the next succession tick.

### How do I start my own kingdom?

Two BK paths beyond vanilla:
- **Found a culture-specific empire goal.** Hold N counties of one culture,
  complete the foundation rite, pay the influence cost.
- **Convert an existing kingdom you took over.** Usurp the kingdom-tier
  title via claim, war, or election, then use the council to issue a new
  contract under your name.

### How do I become a vassal?

Approach a kingdom's ruler. They'll offer a title (typically a barony or
county) under their crown. Accepting binds your clan via the contract — you
owe taxes, levies, and council attendance; you receive military protection
and trade access.

### How do religion conversion and rites work?

- **Personal conversion** — visit a clergyman (preacher in a tavern, bishop
  in a capital), spend piety + gold, take an oath. Your faith change applies
  on the next daily tick.
- **Settlement conversion** — assigned clergy preach over time, raising
  adherent count. Requires the faith to be tolerated by the realm contract,
  or the demesne law to permit conversion.
- **Rites** — listed per faith. Each costs piety, has a cooldown, and a
  triggering condition (battle won, settlement taken, hero married, etc.).
  Effects range from troop morale to permanent traits.

### How do I get an estate?

- **Buy** — at the village screen, *Manage estates* → purchase from the
  current owner. Cost scales with land size and tenant count.
- **Grant** — your liege can grant you a vacant estate.
- **Inherit** — passes via the estate's inheritance line on owner death.
- **Seize** — if a vassal's estate becomes claimable (e.g., owner died
  heirless or committed treason), the liege can seize it.

### How do I hire a custom mercenary unit?

1. Open the BK Mercenary screen.
2. Pick a culture (sets accent and naming pool).
3. Pick a formation class (Infantry, Ranged, Cavalry, HorseArcher).
4. Build the equipment roster from your inventory or purchases.
5. Pay the hire price + ongoing daily wage (≈ 3× vanilla equivalent).
6. The unit is added to your clan's recruitable pool.

### How do I appoint council members?

Open the clan/court screen. For each position, you see candidates with skill
ratings, relation, and traits. Picking costs influence and a relation
adjustment. Demoting also costs influence and applies a relation hit.
Kingdom-level council (royal Marshal, etc.) is only available to the ruling
clan and uses kingdom influence.

### What does a Marshal actually do?

Reduces party wage costs across the realm, boosts levy size from settlements,
and slightly improves army cohesion. Marshal also gates into council-tier
perks. Verify the wage tooltip breakdown in your party UI to see the live
modifier.

### How do I make money?

Ranked roughly by yield per hour of attention:

1. **Workshops + estates** combined in the same town/village pair.
2. **Caravans** (vanilla + BK trade modifiers).
3. **Tournament prize riding** (early game).
4. **Mercenary contracts** to a wealthy kingdom (mid).
5. **Raiding** — strongest with the *Reavers* doctrine and a raid-focused
   lifestyle (Outlaw, Mercenary, Varyag, Jawwal, Kheshig, Drakkar Captain).
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

### Religion

**Q: How do I see my hero's faith?**
Character → BK Religion tab. Shows current faith, piety, last rite, doctrine
votes, and conversion progress if any.

**Q: Can my whole kingdom share one faith?**
Yes via the kingdom contract's religion clause + active conversion. Settling
mixed-faith populations under one ruler causes notable relation hits unless
the doctrine is *Tolerant* or the local stance is `Tolerated`.

**Q: What happens if I marry across faiths?**
Allowed if both faiths' marriage rules permit it. Hostile-stance pairings
are banned. Tolerated-stance pairings carry a piety penalty for both
spouses on the marriage day.

### Education

**Q: How do I pick a lifestyle?**
Character → BK Education tab → Lifestyle dropdown. Locked once chosen until
that lifestyle is fully completed (5 perk tiers) or a respec rite is
performed (rare and very expensive).

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
Travel time is distance / 75 (or distance / 60 with the *Astrology*
doctrine, faster again under the Drakkar Helmsman perk). Cross-Calradia
trips take 4–6 days.

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
  multiplicative; other modifiers (custom troop, doctrine, mercenary status)
  can dominate. Check the wage tooltip breakdown in the party UI.
- **"Estate showing zero income"** — daily ticks accumulate but income posts
  weekly. Or the estate has no tenants — check the estate panel.
- **"Religion fervor dropping every day"** — fervor decays without active
  rites and adherent growth. Run the holy-day rite or take an active doctrine.
- **"Can't change demesne law"** — locked behind a contract-change cooldown
  (≈ 1 in-game year) and minimum loyalty / authority gates.
- **"My new game crashes during loading"** — almost always a non-BK mod's
  Harmony patch failing (e.g., GovernorsHandleIssues against newer
  Bannerlord builds). Check `Crashes/mostrecentcrash.htm`; the inner
  exception will name the mod.

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
  (titles, estates, custom troops, religion data) become orphaned and the
  save will corrupt. Once you start a save with BK, keep BK installed for
  the life of that save.
- **Updating BK on an active save is generally safe within a minor version**
  (e.g., v1.5.0 → v1.5.1). Major-version updates (e.g., upstream BK →
  Redux, or a future v2.x) may require a fresh save.
- **Switching from upstream Banner Kings to Banner Kings — Redux on an
  existing save is not supported.** The two are separate modules with
  separate save data. Start fresh.

---

## 12. Reporting bugs

A useful crash/issue report includes:

1. `Crashes/mostrecentcrash.htm` (game-generated) — has the full stack trace
   and the loaded module list. Open it and look at the "Reasons" and
   "Inner Exception" sections; they often name the offending mod
   immediately.
2. The last few hundred lines of `rgl_log.txt` — Banner Kings warnings are
   tagged `[BK]`.
3. Save file name, Banner Kings — Redux version (visible on the main menu),
   and whether War Sails / NavalDLC is installed.
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

Issues with the original Banner Kings systems (titles, religions, estates,
council, etc. — anything also present in the original release) are upstream
BK problems. They'll get fixed in Redux as we encounter them, but the
underlying design is R-Vaccari's.

---

## 13. Credits & license

**Banner Kings is the work of [R-Vaccari](https://github.com/R-Vaccari) and
the original Banner Kings contributors.** Every system this wiki describes —
titles, councils, courts, religions, languages, populations, estates,
education, succession, the economy rework, the framework underneath — was
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
