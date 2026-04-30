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

**Q: My skills are leveling way too fast — bug?**
*Was* a bug. A leftover Harmony postfix from an old AlternateLeveling
experiment was clamping every vanilla skill's learning rate to a 5%
floor — the value vanilla normally tapers toward zero past your skill's
learning limit. With the floor in place, every skill kept gaining 5% of
base XP forever, so combat skills, social skills, everything kept
ticking past their natural caps. Removed in v1.6.4.4. Skills past their
learning limit now decay normally, and the per-day Scholarship XP from
language/book reading was also rescaled (50/day → 10/day, 2000 on
completion → 500). Existing high skills won't be reset; only future
gains use the corrected curve.

**Q: How do I turn off BK's smithing changes and go back to vanilla
smithing?**
**MCM → Banner Kings → Balancing → BK Smithing System** — flip it off
and restart. With the toggle off, vanilla `DefaultSmithingModel` runs
unmodified: no smelting yield caps (a dagger gives full vanilla iron
yield), no extra stamina cost on one/two-handed weapons, the BK
"Armor" tab in the smithy is inert (clicking it shows a one-line note
in the log), and the per-hour smithing fee is skipped. Vanilla weapon
crafting / smelting / refinement work normally. The Crafting Waiting
Time setting is also implicitly off when BK Smithing is off.

What BK Smithing does when on (default):

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

**Q: Where is the Wisdom attribute? It's mentioned in tooltips but I
can't find it.**
*Was* a bug. BK registers a 7th attribute (Wisdom) for use with the
learning model, but a Harmony patch on `Attributes.All` was hiding it
from every UI screen — that patch existed to prevent vanilla character
creation from crashing on the extra attribute, but it was applying
post-game-load too. Now scoped to character-creation phase only:
during creation Wisdom stays hidden (so vanilla doesn't crash), and
once your campaign starts you should see Wisdom alongside the six
vanilla attributes in the character developer screen and on hero
pages. Each hero starts at Wisdom 2; bonus points come from the
Theology *Religious Teachings* perk on a parent.

## Diplomacy & demands

**Q: An interest group is demanding something — what happens if I refuse?**
Each refusal raises grievance level. Hitting max grievance triggers
escalation: defection, secession war, or claimant uprising depending on
group type.

**Q: What's "support on decision" relation?**
When you side with a clan in a kingdom decision, they gain a relation
modifier with you (+8 to +25 by support strength) for 5 years.

## Mercenaries & combat

**Q: Custom troop daily wage seems insane.**
By design, ≈ 3× vanilla. The hire price is also higher. They're meant to be
elite fillers, not core army composition.

**Q: Where do BK perks apply?**
At model evaluation time wherever vanilla reads the same skill effect. The
seafaring perks specifically also hook into War Sails' naval game models.

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

---

← [Systems reference](Systems-Reference) · [Home](Home) · [Shipping & trade →](Shipping-and-Trade)
