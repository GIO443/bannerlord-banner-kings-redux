# Getting started

← [Home](Home)

## On this page

- [What's in the mod (high-level)](#whats-in-the-mod-high-level)
- [First 30 minutes — what should I do?](#first-30-minutes--what-should-i-do)
- [Glossary — the words that come up constantly](#glossary--the-words-that-come-up-constantly)

For step-by-step recipes ("how do I claim a title?", "how do I become a vassal?"), see [Player guide](Player-Guide). For tables of lifestyles, demesne laws, and policies, see [Systems reference](Systems-Reference).

---

## What's in the mod (high-level)

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
takes real time and is faster under seafaring perks (Drakkar Helmsman). AI
lord parties also use ships now. Routes are graph-aware and adapt to wars,
sieges, and bandit pressure — see [Shipping & trade](Shipping-and-Trade) for
the full system, including freight pricing and console diagnostics.

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
The 1.6.2 raid capture system layers on top of this — see
[Slavery & raiding](Slavery-and-Raiding) for the full picture.

---

## First 30 minutes — what should I do?

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

For the procedural detail on each step (which menu, which buttons, what to
look for), see [Player guide](Player-Guide).

---

## Glossary — the words that come up constantly

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

← [Installing](Installing) · [Home](Home) · [Systems reference →](Systems-Reference)
