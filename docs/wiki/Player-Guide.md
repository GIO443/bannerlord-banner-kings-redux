# Player guide

← [Home](Home)

Step-by-step "how do I…" recipes plus the per-system FAQ. Procedural —
every entry is a sequence of menu paths, clicks, and visual feedback you
should see along the way, with the most common failure modes called out.

For shipping/trade, see the dedicated [Shipping & trade](Shipping-and-Trade)
page. For the slavery and raid economy, see [Slavery & raiding](Slavery-and-Raiding).

## On this page

**How-to recipes**

- [How do I claim a title?](#how-do-i-claim-a-title)
- [How do I become a vassal?](#how-do-i-become-a-vassal)
- [How do I start my own kingdom?](#how-do-i-start-my-own-kingdom)
- [How do I get an estate?](#how-do-i-get-an-estate)
- [How do I trade in a castle?](#how-do-i-trade-in-a-castle)
- [How do I get into a kingdom's council?](#how-do-i-get-into-a-kingdoms-council)
- [How do I appoint council members (my own clan)?](#how-do-i-appoint-council-members-my-own-clan)
- [What does a Marshal actually do?](#what-does-a-marshal-actually-do)
- [How do I hire a custom mercenary unit?](#how-do-i-hire-a-custom-mercenary-unit)
- [How does a mercenary contract work?](#how-does-a-mercenary-contract-work)
- [How do I make money?](#how-do-i-make-money-by-yield-per-hour-of-attention)

**Per-system FAQ**

- [Population](#population)
- [Titles](#titles)
- [Education](#education)
- [Diplomacy & demands](#diplomacy--demands)
- [Mercenaries & combat](#mercenaries--combat)

---

## How do I claim a title?

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

## How do I become a vassal?

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

## How do I start my own kingdom?

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

## How do I get an estate?

**Buy:** At a village's BK menu → *Banner Kings → Manage estates → Buy
estate*. Browse the estates list (ownership, tenant count, food/gold
output). Select one → confirm price (scales with land size and tenants).

**Grant:** While serving a liege, ask them in conversation —
*I have a request → grant me an estate*. They'll offer vacant estates.

**Inherit:** Passes via the estate's inheritance line on the owner's
death (configured per estate's contract).

**Seize:** Available to a liege when a vassal's estate becomes claimable
— owner died heirless, treason, banditry, etc. *Court → Estates → Seize*.

## How do I trade in a castle?

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

## How do I get into a kingdom's council?

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

## How do I appoint council members (my own clan)?

1. Open the BK character panel → *Court* (or *Council* in some BK
   builds — same screen).
2. Each position lists candidates from your clan and your courtiers
   with their skill rating, relation to you, and traits.
3. Select a candidate → click *Appoint*. Costs influence and applies a
   small relation adjustment to other candidates who weren't picked.
4. To demote, select the current holder → *Dismiss*. Costs influence
   and a relation hit with the dismissed hero.

## What does a Marshal actually do?

Reduces party wage costs across the realm, boosts settlement levy
sizes, and slightly improves army cohesion. To verify the modifier is
applying:

1. Open your party screen.
2. Hover the daily wage figure in the bottom strip.
3. The breakdown tooltip lists modifiers — you should see a Marshal
   line if the realm's Marshal is active and your party qualifies.

If the Marshal line is absent, your clan tier or party type may be
ineligible for the bonus.

## How do I hire a custom mercenary unit?

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

## How does a mercenary contract work?

Serving a kingdom as a mercenary (your whole clan, not custom troops)
runs on a fixed term:

1. Talk to a kingdom's ruler about service and accept. You receive an
   up-front earnest-money payment, then a daily wage.
2. The contract runs for **one year**. To see how much is left, open
   the BK character panel → *Mercenary* tab → *Service Time* — it reads
   `"… contract due in N days"`, or `"contract term complete"` once the
   year is up.
3. When the year elapses a popup appears:
   - **Accept** — renews for another year.
   - **Reject** — ends the contract on the spot. Your clan leaves their
     service immediately with **no penalty**, because the term was
     served in full. You do *not* need to find the ruler again to
     leave.
4. **Leaving early** — before the year is up — still costs relation
   with the ruler, scaled from -15 (a day early) up to -50 (just
   signed), plus a small mercenary-reputation hit. Wait for the term to
   end if you want a clean break.

## How do I make money? *(by yield per hour of attention)*

1. **Workshops + estates** combined in the same town/village pair —
   buy a workshop in a town, then an estate in one of its bound
   villages. The village feeds the workshop input, the workshop turns
   it into output, and you collect both ends.
2. **Caravans** — vanilla flow, with BK trade-route modifiers slightly
   improving margins. See [Shipping & trade](Shipping-and-Trade) for
   how caravan routing works under BK's graph and freight pricing.
3. **Tournament prize riding** (early game) — visit any town with an
   active tournament, win, take the prize.
4. **Mercenary contracts** to a wealthy kingdom (mid game) — talk to
   their ruler about service; payment is daily.
5. **Raiding** — strongest with a raid-focused lifestyle (Outlaw,
   Mercenary, Varyag, Jawwal, Kheshig, Drakkar Captain). See
   [Slavery & raiding](Slavery-and-Raiding) for the full system.
6. **Selling prisoners as slaves** — see
   [Slavery & raiding → Selling prisoners](Slavery-and-Raiding#how-do-i-sell-prisoners-as-slaves).
7. **Custom mercenary contracts** sold to AI clans (late, complex) —
   create a custom troop, then offer it to other clans through the
   mercenary tab's contract menu.

---

# Per-system FAQ

## Economy and population

Banner Kings runs its economy on top of **Bannerlord Living Economy** — a
required dependency (see [Installing](Installing)). Living Economy owns
prosperity, production, workshops, and the settlement economy. BK adds the
feudal layer — titles, taxes, militia, retainers, estates — on top of it.

The **population** of every settlement is Living Economy's seven social
classes: Nobles, Landowners, Tenants, Serfs, Craftsmen, Merchants, and
Bonded Laborers (BK's "slaves" are Bonded Laborers). BK mirrors those
counts each day and shows the breakdown — with growth, food, and per-class
tax differentials — in the BK settlement panel. Population drives BK's
rent, tax-by-class, militia composition, retainer recruitment, and food
modelling.

**Estates** are woven onto Living Economy's estate parcels: a BK estate's
land type, size, and quality come from the bound parcel. Manage them from
the clan's estate window — see [Economy](Economy) for estate
specializations, cluster fit, stagnation, and the income model.

## Population

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

**Q: When does a conquered fief change culture?**
A settlement only flips its culture once one culture holds a sustained
**majority — over 55% of the population's assimilation**. A freshly-taken
fief with three near-even cultures (e.g. 34 / 33 / 33%) will *not* flip to
whoever leads by a hair; conversion takes the months or years of demographic
shift you'd expect. While a culture is still a minority (under 15%
assimilation) it also won't seed new notables, so a small foreign pocket
can't bootstrap its own troop tree inside your fief. Watch the settlement's
culture breakdown in the population panel — the engine-side culture follows
the majority, not the current plurality.

## Estates

**Q: How does estate income work?**
Two parallel sources, both feeding the estate's `TaxAccumulated`. The
owner gets paid 80% of the accumulated total each daily tick.

1. **Daily production income** (the bulk source). Each in-game day
   the village runs an estate-production tick. Formula:
   ```
   effectiveAcres   = Farmland + Pastureland*0.5 + Woodland*0.15
   workforceFactor  = clamp((Population + Slaves) / (effectiveAcres*0.5), 0..1)
   gross            = effectiveAcres × workforceFactor × 0.4
   net              = gross × (1 - TaxRatio)
   ```
   A 100-acre fully-staffed allodial estate yields ≈ 40 denar/day.
   Tax rate from the parent fief's policy (Low / Standard / High /
   Exemption) cuts proportionally; allodial keeps 100%.
2. **Trade-tax share.** Whenever a villager party returns to its
   home village after selling at a town, the village-tax slice is
   split between the village's lord and the local estates by
   workforce proportion. Smaller and noisier than production
   income, but adds up at high-volume villages.

**Q: My estate makes 0 income, what gives?**
First, check the visit panel — if there's an **Income Blocked**
reason at the top of the Clan → Other → estate row (or in the
Daily Income tooltip), that's why. Three cases the system surfaces
explicitly:

- *"at war with X"* — your owner faction is at war with the village's
  faction; the daily payout skips the estate. Resolves itself when
  the war ends.
- *"BK title manager not loaded"* — `BannerKingsConfig.TitleManager`
  is null on this save, which gates the estate-finance hook in
  `BKClanFinanceModel`. Indicates a save-load order issue or a
  partial-load campaign.
- *"estate not registered to its owner — try save/reload to resync"* —
  `Estate.Owner` is set but the population manager's owner→estates
  dictionary doesn't contain it. A save+reload usually rebuilds the
  dict.

While blocked, the **Pending Balance** row keeps climbing — when the
block resolves, the next clan-finance daily tick drains 80% of it
and pays you in one lump sum.

If no blocker is named and income is still zero, run
`bannerkings.dump_estate_finance` in the console — it writes
`BK_dump_estate_finance.txt` showing the active `ClanFinanceModel`
class. If it's not `BKClanFinanceModel` (because another mod replaced
the slot), a backstop daily-tick payout in
`BKEstateIncomeBehavior` catches that case. Older builds silently
piled up `TaxAccumulated` without paying — upgrade if you see this.

If still nothing checks out, give it 2-3 in-game days — a fresh-
purchased estate needs population to ramp before production hits a
meaningful daily denar count. Below ~population 20 the per-day
yield is sub-1-denar and may show as 0 on the panel even though
it's accumulating fractionally.

**Q: Can I make the estate clear new land while still earning income?**
Yes — just leave it on the **Production** task. Workers up to 100%
saturation drive production; any *excess* workforce above 100%
saturation automatically clears land (Farmland / Pastureland /
Woodland weighted by the village's land mix). Production income is
unaffected; only the surplus does the clearing. Set the task to
**Land Expansion** when you want to clear AS FAST AS POSSIBLE and
don't care about income — that diverts half the entire population
into clearing, which cuts your daily yield substantially.

**Q: Allodial Tenure shows "Tax Rate: 0%" — am I being robbed?**
No. The Tax Rate stat is the LORD'S cut, not your income. Allodial
means the parent fief's lord gets nothing; you keep 100% of the
production net. Standard tenure with the default tax policy gives
the lord 15% and you 85%; high policy 30/70; exemption 0/100 (lord
gets nothing via a different mechanism). Allodial is the most
profitable tenure for the estate owner.

**Q: What does the estate panel show?**
- **Daily Income (est.)** — steady-state payout you should see per
  day from the production tick. Last actual paid income shows next
  to it (the *secondary* number on the same line). Forces to 0 when
  blocked, with the blocker reason shown at the top of the tooltip.
- **Pending Balance** — `TaxAccumulated`, the gold sitting in the
  estate waiting for the next clan-finance tick to drain 80% of it.
  Useful when income is blocked: the balance climbs visibly so you
  know the estate is *earning*, just not *paying*.
- **Workforce Saturation** with a status label — e.g. "120%
  (surplus → clearing land)" — so it's obvious whether you're
  under-staffed, balanced, or over-saturated.
- **Acreage Growth** appears only when actively growing, with the
  source named (Production surplus vs Land Expansion task).

Same data is mirrored in the per-estate row of the clan finance
income panel; an extra "Income Blocked" entry appears at the top of
that row when the blocker is active.

**Q: The Slaves button on the estate panel does nothing.**
*Was* broken in the 1.4 port — `PartyScreenHelper.OpenScreenAsLoot`
was removed from vanilla and the original BK transfer screen depended
on it. Replaced with a multi-choice inquiry that shows your
party-prisoner count and the estate's slave count, then offers
"Transfer all party prisoners to estate" / "Take all estate slaves
into party". Hero prisoners are never transferred. Both-empty just
shows the status line.

## Titles

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

## Education

**Q: How do I pick a lifestyle?**
Character → BK Education tab → Lifestyle dropdown. Locked once chosen until
that lifestyle is fully completed (all its perk tiers — three or four,
depending on the lifestyle) or a respec is performed (rare and very
expensive).

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

**Q: My skills are leveling way too fast — bug?**
*Was* a bug, in two layers, both fixed:

1. A leftover Harmony postfix from an old AlternateLeveling experiment
   was clamping every vanilla skill's learning rate to a 5% floor — the
   value vanilla normally tapers toward zero past your skill's learning
   limit. With the floor in place, every skill kept gaining 5% of base
   XP forever, so combat skills, social skills, everything kept ticking
   past their natural caps. That postfix is gone: skills past their
   learning limit decay normally.
2. The *Alternative Leveling* MCM toggle (under Balancing) used a custom
   XP curve that gave only ~8K cumulative XP for skill level 100 vs
   ~250K for vanilla — a single battle could push a skill from 1 to 100.
   The toggle defaulted **on** in 2023, was flipped to off later, but
   MCM settings persist across versions so saves carrying the old default
   kept hitting the bug. The toggle is now **removed entirely** —
   every save uses vanilla's XP curve.

The per-day Scholarship XP from language/book reading was also rescaled
(50/day → 10/day, 2000 on completion → 500). Existing high skills won't
be reset; only future gains use the corrected curve.

**Q: I want BK's smithing overhaul. How do I turn it on?**
**MCM → Banner Kings → Balancing → BK Smithing System → on, then
restart.** It defaults to **off** — vanilla `DefaultSmithingModel`
runs unmodified out of the box. With the toggle on, BK's smelting
caps, stamina inflation, armor crafting tab, and hourly smith fee all
apply.

What BK Smithing does when on:

- **Smelting yield caps** — dagger / throwing-axe / crossbow gives at
  most 1 metal, one-handed weapons up to 2, two-handed up to 3.
  Designed to make smelting feel like recovering material rather than
  printing it.
- **Stamina cost adjustments** — two-handed weapons cost +50% stamina,
  one-handed +20%; daggers and other small weapons unchanged. Stamina
  cost is also clamped to a min of 15 and max equal to your max
  crafting stamina.
- **Armor crafting tab** — a fourth tab next to Smelting / Refinement /
  Smithing lets you craft armor, shields, ammunition from materials
  (iron ingots for plate/chain, leather/linen for soft armor). Each
  item has a difficulty, a stamina cost, a botch chance based on your
  Crafting skill, and may roll a quality modifier with the Artisan
  Craftsman perk.
- **Hourly smith fee** — when Crafting Waiting Time is also on, leaving
  the smithy triggers a wait menu that ticks campaign hours and
  charges a per-hour fee (base 50 denarii, scaled by town prosperity
  and clan tier, reduced 15% by the Artisan Smith perk).

**Q: My Wisdom attribute starts at 0 on a new game — bug?**
*Was* a bug. The seeder that writes Wisdom = 2 into every hero's
attribute dict only fired on `OnGameLoadedEvent`, which doesn't run on
fresh new games — it only fires when you load a save. So on a brand-
new sandbox, your starting hero (and every other world hero) had no
Wisdom entry until the first save/reload. The seeder now also runs
on `OnCharacterCreationIsOverEvent` (covers the fresh-start case) and
on every `HeroCreated` (covers notables, wanderers, and other
mid-campaign heroes). Existing saves with Wisdom = 0 will pick up the
value 2 on next load.

**Q: Where is the Wisdom attribute?**
On the character-developer screen alongside the six vanilla
attributes (Vigor / Control / Endurance / Cunning / Social /
Intelligence). Wisdom is BK's 7th attribute and starts at 2 on every
hero. The Wisdom tile is injected directly into the character-developer
screen via a Harmony postfix on
`CharacterDeveloperHeroItemVM.InitializeCharacter` — that touches only
the screen's per-instance attribute list, not the global
`Attributes.All`, so vanilla education stays happy. (An earlier attempt
to expose Wisdom through `Attributes.All` was reverted because it
crashed `EducationCampaignBehavior.CreateStage2` on every child's
daily tick.)

## Diplomacy & demands

**Q: An interest group is demanding something — what happens if I refuse?**
Each refusal raises grievance level. Hitting max grievance triggers
escalation: defection, secession war, or claimant uprising depending on
group type.

**Q: What's "support on decision" relation?**
When you side with a clan in a kingdom decision, they gain a relation
modifier with you (+8 to +25 by support strength) for 5 years.

## Religion

> **As of v1.9.10.21 the religion system is being moved to **Banner Kings
> Content Expansion (BKCE)**.** The MCM toggle **Enable Religion System
> (legacy)** now defaults to OFF — the seven faiths are no longer seeded,
> the religion HUD widget is hidden, and the religion campaign behaviour
> is silent. Existing saves still load with their religion state preserved
> but inert. To keep playing with the legacy in-BK religion system until
> BKCE ships, flip the toggle back ON in MCM → Performance and restart.
> The recipes below describe the legacy system as it ran when enabled.

**Q: How do I see what faith my character is in?**
Open the character page → there's a religion tab/panel showing your
current faith, its main god, the secondary cults, your current piety,
and the doctrines the faith holds. Your **piety total** is also surfaced
on the campaign map's right-side info bar (alongside gold, food, troops)
once you've been assigned to a faith — hover the row for a breakdown of
where today's piety came from. Heroes are assigned the natural
faith of their culture automatically — empire→Darusosian Path,
vlandia→Canticles of Caïon, battania→Amra Druidh, aserai→Path of
Akhmar, khuzait→Six Winds, sturgia→Old Gods of the North,
nord→Osfeydian Tradition. See [Systems reference → Faiths](Systems-Reference#faiths)
for the full list.

**Q: How do I change faith?**
Walk into a town or castle that's holding a preacher notable of the
target faith. Talk to them → choose **"I would like to be inducted."**
Costs you a chunk of clan renown if you're converting from another
faith (—100 if you're the clan leader, —50 otherwise) and resets your
piety in the new faith to zero. Lords of your old faith may take it
poorly.

**Q: How do I find a preacher?**
Preachers spawn as notables in towns and castles of cultures that
match the faith. Each faith caps at its own clergy depth: the
**Darusosian Path** and **Canticles of Caïon** run a 3-tier hierarchy
(towns get the top rank — *Pontifex* / *Primarch*; castles get the
mid rank — *Lictor* / *Canon*; villages get the entry rank — *Acolyte*
/ *Brother*). **Amra Druidh** runs 2 tiers (*Arch-Druid* in towns,
*Bard* below). The other four faiths — **Path of Akhmar**, **Six
Winds**, **Old Gods of the North**, **Osfeydian Tradition** — are
single-tier: one rank title (*Imam*, *Khan-Shaman*, *Eldgothi*,
*Hrafnskáld*) regardless of settlement type. Open the settlement's
notable list and look for the religious title in front of the name.

**Q: How do I earn piety?**
Daily piety ticks based on faith doctrines and your behaviour:

- **Warlike** faiths (most of them) — piety equal to the influence
  reward of every won battle.
- **Reavers / Osric's Vengeance** (Nord) — piety from raid and
  occupation income.
- **Renovatio Imperi** (Empire) — piety from showing mercy on siege
  victory in Imperial fiefs (also gains relations with all notables).
- **Childbirth** (Vlandia, Sturgia) — clan renown bonus on every birth
  in the faith.
- **Astrology / Esotericism** (Empire) — piety from finishing
  education projects, faster cultural innovation if Cultural Head.
- **Ancestor Worship** — piety from clan renown gain in general.

You can also perform **rites** (see below) for one-shot piety
rewards.

**Q: How do I request a blessing?**
Talk to a preacher of your faith → **"{Boon-action}"** (varies per
faith — *I would seek a blessing of the Triad*, *Father, would you
sing a canticle for me?*, *Imam, give me a sun-blessing*, etc.) →
pick one of the secondary divinities. Costs piety; grants a temporary
in-character bonus tied to that divinity (combat, fertility, trade,
etc — read the inquiry tooltip for the specific effect). You hold one
blessing at a time.

**Q: How do I perform a rite?**
Talk to a preacher of your faith → **"I would like to perform a rite."**
Each faith ships with one or more rites tied to its lore — Imperial
faiths perform the Astaronia Festival and the execution of Western /
Northern Imperial prisoners; Battanians offer iron and great-swords
into the sacred lynns; Vlandians offer lances and warhorses; Northern
faiths offer axes and hold the Pérkos festival; Aserai and Khuzait
faiths currently have no rites in this version. Each rite has a
cooldown (years) and prerequisites — the inquiry will list them up
front.

**Q: I'm playing Nord — what's the Osfeydian Tradition?**
The reaver-faith of the Nordvyg, named for the burnt shore where the
Wilunding host first beached under Osric. Hreinwald the Sea-King is
its chief divinity; Skǫll the Wolf of the Deep takes the wake; Vethari
(named ancestors) and a syncretised Pérkos sit alongside. Doctrines
are Reavers, Osric's Vengeance, Warlike, and Ancestor Worship — every
raid you complete and every fief you occupy converts directly into
piety. The faith has no organised priesthood: chieftains lead the
offering, and the rank title for any Nord preacher you find is
*Hrafnskáld* (raven-skald).

## Mercenaries & combat

**Q: Custom troop daily wage seems insane.**
By design, ≈ 3× vanilla. The hire price is also higher. They're meant to be
elite fillers, not core army composition.

**Q: Where do BK perks apply?**
At model evaluation time wherever vanilla reads the same skill effect. The
seafaring perks specifically also hook into War Sails' naval game models.

**Q: Why aren't AI armies forming all the time anymore?**
Older builds had a bug where AI lords formed Patrolling-type armies
with no target settlement. Vanilla AI dispersed them within days for
"no purpose", and BK pushed again as soon as influence rebuilt — a
recruit→march→disband loop that wasted influence and never fielded an
actual force. The current behaviour is a target-quality gate: AI only
forms an army when there's a real objective (friendly fief under
siege within 350 units → Defender, or enemy fortification within 280
units → Besieger). With no target, BK doesn't push and vanilla AI
runs unmodified. The result is fewer but more purposeful AI armies.
An MCM toggle (Performance → AI Army Formation) disables the BK push
entirely if you want pure vanilla AI behaviour.

## Roleplay

### How do I open a dialogue with a hero far away (telepathy)?

v1.9.10.8 added an opt-in "telepathy" surface — open the standard
dialogue UI with any met hero on the map, after a distance-based
delay. Off by default; turn it on in MCM → Performance → **Enable
Telepathy**. There's also a **Telepathy Delay Multiplier** slider
(default 100%) that scales how long the thought takes to arrive.

Step by step:

1. **Enter any settlement** (town, castle, or village). The entry
   point lives on the BK actions submenu, so being in a settlement
   is the framing — "you find solitude to focus your thought".
2. Open the settlement menu → **Banner Kings** submenu → **Reach
   out with a thought**.
3. A picker opens listing every hero you have met (Hero.HasMet),
   excluding the dead, imprisoned, or disabled. Next to each name
   you see the delivery time in hours — that's the distance from
   your party to the recipient at 0.5h per map unit, clamped to
   the [1h, 10d] window and scaled by the MCM multiplier.
4. Pick a hero, accept. You get the message: *"Your thought
   reaches out across N hours of distance toward {NAME}…"*
5. Travel, fight, or sleep through those hours. When the timer
   elapses (checked every in-game hour), you'll see *"A thought
   from {NAME} settles in your mind…"* and the standard dialogue
   UI opens with that hero as the partner, no matter where they
   are on the map. Run dialogue normally; close to return to
   campaign.

What can go wrong:

- **No one in the picker** — you haven't met anyone yet, or every
  acquaintance is dead/imprisoned/disabled. The picker shows a
  *"There is no one within your thought's reach"* notice.
- **"Your thought finds no response from {NAME}"** — the recipient
  died, was imprisoned, or otherwise dropped out of valid state
  between when you queued and when the delivery fired. No dialogue
  opens; the message dissolves.
- **Vanilla dialogue conditions that gate on co-location** — some
  vanilla dialogue lines check `Hero.MainHero.CurrentSettlement` or
  the partner's settlement and may skip. The conversation still
  runs; you may see fewer branches than you'd see standing next
  to the hero in person.

Pending thoughts persist across save/load — close the game, come
back, and the dialogue opens at the right hour.

---

← [Systems reference](Systems-Reference) · [Home](Home) · [Shipping & trade →](Shipping-and-Trade)
