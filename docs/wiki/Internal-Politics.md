# Internal Politics

Banner Kings models realm-internal politics as a set of overlapping
pressures the player and the AI both navigate. The player has explicit
buttons and menus; the AI has daily-tick behaviors that drive
equivalent decisions on its own clocks. Both sides act on the same
shared state (relations, renown, influence, claims, ambition, threat).

This page maps the flow graphs of who can do what, when. The first
two are **player paths** — the actions surfaced through the BK UI,
dialogue, and decisions. The third is the **AI flow** — what the
non-player clans are actually doing on the campaign map while you're
busy with your own moves.

---

## Flow graph 1 — Player vassal feuds another vassal

```
START ─ I want Vassal B's title (county, duchy, etc.)
  │
  ├── Path A: paperwork ──────────────────────────────────────────┐
  │                                                                │
  │   [CLAIM]                                                      │
  │   pay 10% gold + 20% influence + 20% renown                    │
  │   prereqs: legal heir candidate, not own clan, title de-jure   │
  │   result: ongoing claim recorded on title; -5 relation w/ B    │
  │   result: NO war, NO immediate transfer                        │
  │            │                                                   │
  │     ┌──────┼──────┐                                            │
  │     ▼      ▼      ▼                                            │
  │  abandon  wait   upgrade ─────────────────────────────────────►│
  │                                                                ▼
  │   [USURP]
  │   pay full gold + influence + renown
  │   prereqs (any one):
  │     - completed claim, OR
  │     - 80%+ of title's leaf fiefs under my control, OR
  │     - marriage / inheritance basis
  │   plus: clan tier ≥2 (≥4 for low-rank titles)
  │   plus: if B is in same faction, I must NOT be B's vassal
  │           (use REVOKE instead — but only liege can revoke)
  │   result: ownership transfers immediately
  │   result: -relation w/ B (scaled by title value)
  │   result: 10% chance -relation w/ each neutral 3rd-party clan
  │   result: still NOT a war declaration
  │
  ├── Path B: lobbying ─────────────────────────────────────────────
  │
  │   I join an INTEREST GROUP whose demands target B
  │   group tension rises while demand unmet
  │     │
  │     ▼
  │   group escalates to RADICAL GROUP, spawning a DEMAND:
  │     - ClaimantDemand: "B's clan steps down from contested seat"
  │     - SecessionDemand: a bloc breaks from the realm (drags B's faction)
  │     - PretenderDemand: bloc backs a rival kingdom claimant
  │   resolution: realm-wide vote, capitulation, or civil war
  │
  └── Path C: get the liege involved ───────────────────────────────
      Only the liege (realm leader) can:
        [REVOKE]  pay 80% influence + 60% renown
        result: B loses the title; B becomes "Previous Owner" claimant
                ⇒ B can now press a CLAIM back at no basis cost
        result: -relation w/ B
```

---

## Flow graph 2 — Player clan climbs the hierarchy

```
LESSER PEERAGE  (default for clans without titles/fiefs)
  rights: vote in kingdom decisions, council eligibility
  │
  ├─ acquire any non-Lordship title OR any fief  ─────► auto-promoted to FULL
  │
  ├─ RequestPeerageDecision goal
  │   cost: influence (PeerageKingdomDecision model)
  │   gate: must be in a kingdom; clan leader (not realm leader)
  │   resolution: kingdom peer vote; ≥40% support succeeds
  │
  └─ Mercenary FullPeerage privilege  (career-points reward path)
      cost: 1000 career points
      gate: clan must NOT already have Full Peerage
      result: full peerage granted on mercenary contract resolution
  │
  ▼
FULL PEERAGE
  rights added: start elections, grant knighthood, fief eligibility,
                council seat eligibility, +vote weight
  │
  ├─► acquire a COUNCIL SEAT ─────────────────────────┐
  │     action: CouncilAction REQUEST / SWAP / RELINQUISH
  │     cost: influence (varies by position + government)
  │     gate: government's council_control mode:
  │       APPOINTED  → ruler picks; vassal asks, ruler grants/denies
  │       ELECTED    → ≥3 candidates, kingdom-wide vote
  │       MIGHT      → strongest clan seizes; no formal request
  │
  ├─► acquire a DUKEDOM ──────────────────────────────┐
  │     paths: GRANT from liege; INHERIT (succession);
  │            FOUND via dukedom mint; USURP / Conquer
  │     ⇒ now ranked as a duke under the kingdom
  │
  ├─► FOUND A KINGDOM  (FoundKingdomGoal)
  │     cost: ~500k gold + ~annual realm income,
  │           ~1000 influence + annual baseline, +100 renown
  │     gate: clan IS the realm leader; no existing sovereign title
  │     pick: a held dukedom to base the new kingdom on
  │     result: new Kingdom-tier title minted; contract inherits
  │             government / succession / inheritance / gender law
  │             from chosen dukedom
  │
  └─► FOUND AN EMPIRE  (FoundEmpireGoal)
      cost: extreme gold + influence + renown
      gate: clan holds a Kingdom-tier de jure title
      result: Empire-tier title; vassal/demesne limits raised;
              Crown Authority ceiling raised
```

---

## Flow graph 3 — AI clans do the same on their own clocks

The AI doesn't push UI buttons; it adjusts the same shared state your
buttons adjust — relations, renown, claims, transition pressure,
revocations. Two daily-tick behaviors drive the bulk of it. Both are
gated behind the *Politics Rework* MCM toggle and skip the player's
clan (you drive that one yourself).

### 3a — AI vassal climbs (`BKVassalPoliticsBehavior`)

```
DAILY TICK  ─ for every non-ruler vassal clan in every kingdom
  │
  ├─ gates:    clan exists, has a leader, not eliminated
  │            not the ruling clan, not a mercenary contract
  │            kingdom has a ruler
  │            (player clan skipped — you drive yours)
  │
  ▼
COMPUTE DISPOSITION
  │   pull Ambition from BKPoliticalDisposition (clan trait + state)
  │
  ├─ gate: Ambition ≥ 0.15  → otherwise the clan is content; bail
  │
  ├─ cadence roll: probability = 3% + 10% × Ambition
  │   (Ambition 1.0 climber fires ~13%/day; Ambition 0.2 ~5%/day;
  │    averages to "a deliberate move every couple of weeks")
  │
  ▼
SENSE THE RULER'S WEAKNESS  (0 = secure, 1 = ripe for the knife)
  │   = (1 − Legitimacy) × 0.4
  │   + (negative relative strength of ruling clan vs realm avg) × 0.4
  │   + (ruler IsPrisoner ? +0.3 : 0)
  │   + (ruler IsChild    ? +0.3 : 0)
  │
  ▼
TREACHEROUS MODE?
  │   yes if  Weakness > 0.50  AND  Ambition > 0.25
  │   no otherwise (= "loyal climb")
  │
  ▼
FIND A RIVAL  (the realm peer this clan most contends with)
  │   score per peer = tier-closeness × 0.5
  │                  + negative-relation factor × 0.8
  │                  + peer Ambition × 0.4
  │   pick highest if score > 0.60; otherwise no named rival
  │
  ▼
PULL A LEVER  (chosen by realm's PoliticalLayer)
  │   magnitude scales with Ambition (mag = 2 + 3 × Ambition, min 1)
  │
  ├─ TRIBAL (Chiefs)
  │     loyal:       +renown; rally 3 peers (+1 rel each)
  │     treacherous: −relation w/ ruler; +renown
  │
  ├─ IMPERIAL (Governors — Emperor appoints)
  │     loyal:       +relation w/ ruler (imperial patronage)
  │     treacherous: −relation w/ ruler; +renown;
  │                  +1 transition pressure on the realm
  │
  ├─ REPUBLIC (Parliament)
  │     loyal:       rally 3 peers
  │     treacherous: −relation w/ rival (motion of no confidence);
  │                  rally 3 peers
  │
  ├─ DICTATORSHIP (senate cowed by strongman)
  │     loyal:       +relation w/ ruler (courting the dictator)
  │     treacherous: −relation w/ ruler; +renown;
  │                  +1 transition pressure
  │
  └─ FEUDAL (Vassals — the default hierarchy)
        loyal:       +relation w/ ruler (petitioning the crown)
        treacherous: −relation w/ rival; small −relation w/ ruler;
                     +renown (pressing claims against a rival)
  │
  ▼
NOTIFY THE PLAYER  (intel only, never a decision popup)
  │   if treacherous AND  (rival == PlayerClan OR ruler == PlayerClan)
  │   message: "<CLAN> is manoeuvring against you within <KINGDOM>."
  │
  END
```

**What this looks like in-game:** an ambitious AI vassal slowly
accumulates renown and shifts relations in a direction matching the
realm's government type, becomes hostile to a specific rival as that
rivalry sharpens, and ratchets up transition pressure on Imperial /
Dictatorship realms when the ruler stumbles. You don't see a per-
action popup — you see relations and renown drift, and eventually a
realm-wide consequence (election, succession crisis, group escalation)
fires from the accumulated pressure.

### 3b — AI ruler revokes ambitious vassals' titles (`BKRulerPoliticsBehavior`)

```
DAILY TICK  ─ for every ruling clan (one per kingdom)
  │
  ├─ gates: ruler is not the player, kingdom has a ruling clan
  │
  ├─ RunWeekly probability gate (deliberate, not spammy)
  │
  ▼
COMPUTE PERSONAL THRESHOLD  (the "how cruel is this ruler?" number)
  │   threshold = 60
  │             − 20 × Calculating
  │             + 25 × Mercy
  │             + 15 × Honor
  │   • maximally cruel + calculating (mercy −1, calculating +1)
  │       → threshold ≈ 25 threat units
  │   • maximally merciful + honourable (mercy +1, calc −1, honor +1)
  │       → threshold ≈ 110 threat units
  │
  ▼
FOR EACH VASSAL CLAN  (skip self, skip player, skip minor factions,
                       skip if on 1-year per-pair cooldown)
  │
  ▼
SCORE THREAT  (capped per-source so no one signal dominates)
  │
  ├─ (a) negative personal relation  →  up to +50
  │       (positive relation contributes nothing — we don't
  │        reward friends, we just don't suspect them)
  │
  ├─ (b) claims on ruler's own titles → +20 per claim
  │
  ├─ (c) radical-group involvement:
  │       leadership   → +40
  │       membership   → +15
  │       (per radical group; multiple groups stack)
  │
  └─ (d) over-mighty bannerman:
        share = vassal strength / (kingdom strength − ruler strength)
        if share > 0.30  →  +20
        (denominator excludes the ruler so a strong / weak ruler
         doesn't artificially raise / suppress every vassal's ratio
         — this is "share of vassal-power among bannermen", the
         Tokugawa fear of one overgrown peer, not "share of realm")
  │
  ▼
DECIDE
  │   if threat < threshold      → leave the vassal alone
  │   if threat ≥ threshold      → look for a title to strip
  │
  ▼
FIND CHEAPEST REVOCABLE TITLE  (lowest tier first)
  │   • a barony before a county before a dukedom
  │   • revoking a small title = measured signal of disapproval
  │   • revoking a big title = casus-belli-grade strip; reach for
  │     this only when nothing smaller is revocable
  │
  ▼
AFFORDABILITY GATE
  │   ruler clan must have:
  │     Influence ≥ action.Influence cost
  │     Renown    ≥ action.Renown × 1.5
  │   else: bail (the political will isn't there)
  │
  ▼
REVOKE   (via BKTitleManager.RevokeTitle — same pipe as the player's
          REVOKE button; all government / hierarchy gates apply)
  │
  ├─ vassal loses the title; becomes "Previous Owner" claimant
  │   (can press a CLAIM back at no basis cost — the cycle escalates)
  │
  └─ 1-year per-pair cooldown installed; ruler revokes at most one
     title per fire (political restraint — no ratchet-mode purge)
```

**What this looks like in-game:** rulers with high mercy / honor
threaten almost nothing and tolerate ambitious bannermen for years; a
cruel + calculating ruler clamps down on any vassal pressing claims
or leading radical groups within weeks. After a revocation, the
stripped vassal becomes a Previous-Owner claimant on that title —
the cycle is designed to escalate: revoke → claim → press →
counter-revoke, etc. Average realm sees a deliberate revoke maybe
every couple of weeks at most.

---

## How the two AI loops feed the player's flow graphs

- A vassal AI in **treacherous Feudal** mode pressing claims against
  a rival is the AI doing **Player Flow 1 Path A** to itself: it
  ratchets relations + renown so that a future window opens where it
  can press a claim. You feel this as relations drifting and an AI
  rival you weren't expecting suddenly having a leg up.
- A vassal AI's **Imperial / Dictatorship treacherous** mode raising
  transition pressure is the upstream of **Player Flow 2's** found-
  kingdom / regime-change content: enough accumulated pressure and
  the realm transitions, putting your clan's standing under a new
  contract.
- The ruler AI's **revoke** acts as a brake on AI vassal climbing.
  If you're an AI ruler with cruel/calculating personality, you'll
  see the AI strip titles from the very same climbing-vassals
  flow-graph-3a is driving. If you're an AI vassal climbing
  successfully, expect the cruel-ruler revoke (flow-graph-3b) to
  trim your gains.
- Both AI loops respect the same **MCM toggle** — disabling *Politics
  Rework* in MCM stops them entirely, leaving the realm to vanilla +
  BK title mechanics without the active climbing / revoking pressure.

---

## Tuning influence gain (MCM)

Every political action above is paid for in **influence**, so if gains
feel too slow you can scale them in **MCM → Banner Kings → Balancing**:

- **Player Influence Gain** — boosts *your* clan's daily influence gain.
  Default **100%** (no boost).
- **AI Influence Gain** — boosts *every other* clan's daily influence
  gain, so the AI can actually afford votes, annexations, and title
  actions. Default **200%** (AI clans gain influence roughly twice as
  fast as base BK).

Both sliders only apply while a clan's **net daily change is positive** —
they boost gain and never deepen a loss — and they take effect with no
restart. To check a slider is working, open a clan's influence tooltip:
a boosted clan shows a `Player Influence Gain (MCM)` or `AI Influence
Gain (MCM)` line in the breakdown. Set a slider to 100% to turn its
boost off.

---

## How captured fiefs are distributed

When a kingdom takes a settlement, ownership is decided by an **ownership
vote** (the vanilla claimant decision, with BK's scoring layered on top).
Each candidate clan gets a merit score, and the realm's **Conquest law**
shifts it:

- **Conquest by Might** — the clan that captured the fief gets a large
  bonus, so conquerors tend to keep what they take.
- **Conquest by Claim** — the de jure title-holder / legal claimant is
  favoured.
- **Distributed Conquest** — clans that already own a lot are penalised,
  spreading land toward fief-poor clans.

**How the demesne limit is computed (reworked).** Your demesne limit — how
many fiefs a clan can hold before stability penalties bite — is now a flat,
legible formula instead of a tier-driven one:

- **Base 2** for every clan.
- **+ highest title held:** county **+1**, duchy **+2**, kingdom **+3**,
  empire **+4** (baronies and lordships add nothing — the base covers them).
- **+ stewardship:** **+1 per 100** Stewardship skill on the clan leader, up
  to **+3**.
- Plus the existing modifiers (the *August de Jure* perk, the Lordship-skill
  bonus, the *Jawwal* lifestyle penalty), capped at 10.

The old model started at 0.5 and leaned heavily on clan tier, so a landless or
low-tier AI lord bottomed out at a demesne limit of **one** fief. The flat base
+ title floor means even a small house can hold a couple of fiefs, while a
titled, well-administered lord scales sensibly with rank and stewardship. Open
the **Demesne** tab and hover the limit to see the live breakdown.

**De jure holdings count for less.** How much a fief weighs *against* that limit
depends on whether you also hold its title:

- A fief you hold **de jure** (you own its title too) counts at **75%** weight —
  legitimising your conquests literally lightens your demesne load.
- A fief whose title is held **de jure by a member of your house** (e.g. a
  knighted companion) counts **nothing** toward your clan — this is why parcelling
  titles out to household knights lets a clan hold a lot of land without tripping
  the limit.
- A fief you hold **de facto only** (the land without its title) counts **full**
  weight — raw conquest is the heaviest way to hold ground.

AI lords now factor this in when judging whether taking a fief would put them
over the limit: because an acquired fief usually comes with its title, they
project it at the **discounted** rate rather than full weight, so they no longer
refuse land they could comfortably administer.

**Over-fief lords are now deprioritized, and vassals vote along their own
lines.** Previously an AI king could win every ownership vote for himself
and end up sitting on far more towns and castles than his demesne limit
allows while landless vassals got nothing. Two things drove that: the
vanilla vote amplifies a candidate's vote *for itself*, so the king kept
re-electing himself; and ordinary vassals cast a flat merit vote that
defaulted to the crown. As of v1.9.11.9:

- **Any candidate already over its demesne limit gets reduced support**,
  scaling with how far over it is (roughly half support at 2× the limit,
  a third at 3×). This is a soft deprioritization, not a veto — the king
  still appears as a candidate and can still win a fief when he's
  genuinely the best home for it, but a bloated ruler no longer
  out-polls needier peers.
- **Vassals lean toward lords they're aligned with** — by personal
  relation and, more strongly, by shared interest-group membership — so
  blocs push land toward their own members along ideological lines
  instead of rubber-stamping the ruler.

Combined with the **Conquest by Might** conqueror bonus, captured fiefs
now tend to flow to the conqueror or an under-limit vassal rather than
piling onto an already-bloated king. To see why a fief went where it
did, open the ownership decision and read each candidate's score
breakdown; the conquest law shows up as a named line (e.g. `Last
conquered by … (Conquest by Might)`).

**AI lords over their demesne limit shed land — to knights and young
clans, slowly (reworked v1.9.19.0).** The vote above governs *new*
conquests; this governs land a clan already holds. When an AI clan
leader is over its demesne limit it gives fiefs away, **lowest fief
first** (a backwater village before a castle, a castle before a town;
the council-seat town is never given, and the clan is never stripped to
landlessness). Two changes fix the old "hot-potato," where fiefs bounced
endlessly between full clans because there was nowhere for them to land:

- **It mints young clans as the sink.** When the fief is a **village
  (Lordship)** the clan held in its own name, the clan **knights one of
  its own** — a capable companion or other eligible non-family member is
  granted the Lordship, becomes a knight, and through the normal
  knighthood path soon founds their own minor clan around that village.
  This relieves the demesne immediately (a fief held de jure by a
  non-leader member counts zero toward the clan) and steadily grows the
  realm's pool of small houses. This is **no longer blocked by the
  clan's vassal limit** — a knight is the *cure* for being over-limit
  (the new house leaves the liege's vassal rolls once it founds), so the
  cap no longer stops the shedding. One knight clan per village still
  caps the spread.
- **Towns, castles, and surplus villages flow to young clans with real
  room.** A shed fief only goes to a clan that can hold it **without
  going over its own limit** — preferring the youngest/smallest houses
  (especially knight-founded ones) with the giver's relationship as a
  tiebreak (you hand land to a house you favour). Because lords shed one
  after another and each reads the realm's *current* holdings, the one
  young house that looked empty doesn't get buried by everyone at once.
  If nobody has room this week, the lord simply **keeps the fief and
  tries again later** rather than forcing it onto an already-overloaded
  lord — which is what caused the bouncing.

Redistribution is deliberately **gradual** (a couple of fiefs per clan
per week), so a deeply over-limit lord takes several weeks to comply and
the land spreads across many young houses instead of flooding one. (Only
AI clans do this automatically; the player gets a map notice when over
limit and chooses what to grant.)

---

## Realm Dilemmas (experimental — for testers)

A **dilemma** is an ongoing, two-sided contest in a kingdom — the realm's lords
take sides over a deliberation window, and it resolves on the balance of
weight behind each side. It's gated behind **MCM → Banner Kings → Politics →
Enable Politics Rework** (the same master toggle as the rest of the politics
systems); turn that off and no dilemmas occur.

**Where to see them.** Open **Kingdom → BannerKings → Groups**. Active dilemmas
appear in their own **Dilemmas (N)** section in the left list alongside Interest
and Radical Groups. Select one to see the **For / Against** push-pull bar, who
raised it and against whom, and the time remaining. You can also list/inspect
them from the console with `campaign.bannerkings.dilemmas`.

**Taking part.** On a selected dilemma, **Support** and **Oppose** spend
influence to add your clan's weight to that side. AI clans pick a side by
relation, faction, ambition and government, and spend their own spare resources
on it over the window — so the bar moves as the realm deliberates. By default the
**deliberating lords are the realm's full peers** — clans without full peerage
watch but don't vote — while the two clans directly involved (the initiator and
the clan they're pressing) always take part regardless. A balance mod can lift
that restriction, or require the initiator to hold a minimum influence to raise
the dilemma at all, by retuning `peers_only` / `min_initiator_influence` in
`bk_dilemmas.xml`.

**Radical factions (pretender / secession) — when they form and dissolve.**
A radical group occupies one slot per type per realm. An AI only spins one up
when the realm actually conditions it: **predicted support must be at least
40%** (the same headline bar shown on the group — driven by legitimacy, war
fatigue, crown authority, etc.). A faction that loses all momentum (its
**radicalism falls to zero**) is **dissolved outright** — members and leader
cleared — and stays gone unless conditions again predict ≥40% support. That
frees the slot, so if no faction is active you can start your own from the
Groups tab. (Previously a spent faction could linger and block you.)

If you lead a **claimant faction**, the claimant you pick is binding for the
life of the group and **persists across save/reload** — load a save and your
faction still backs the same claimant, with **Make Ultimatum** available once
radicalism reaches the demand's threshold. (Fixed in v1.9.16.20: a reload used
to silently reset the chosen claimant, leaving the ultimatum greyed out even
with requirements met.)

**Recovering a stuck claimant faction (older saves).** If you have a claimant
faction from before the fix whose **Make Ultimatum** stays greyed out even at
high radicalism, its claimant was lost by the old bug. As the faction leader,
open the faction in the **Groups** tab: the demand button now reads **Choose
Claimant** instead of Make Ultimatum. Click it, pick your claimant from the
succession list, and the button reverts to **Make Ultimatum** — the choice
persists from then on. (You can tell a faction is in this state because its
**Demand** row reads a generic "Claimant" rather than "Install &lt;name&gt;".)

**How a contest is weighed.** Each clan contributes `clout × (1 − m) +
military × m`, where `m` (the military coefficient) rises for martial
governments — a Tribal realm is settled by swords, a Republic by standing.
A minimum deliberation window keeps a lopsided opening from auto-completing
before others can weigh in, and undecided clans can be swayed onto a clearly
leading side.

**How it resolves** (For-side share of committed weight):

- **≥ 75%** — carries overwhelmingly, resolved immediately.
- **65–75%** — carries when the timer ends.
- **50–65%** — contested: the **ruler decides** (for things the crown controls),
  or it goes to a power-struggle outcome for radical demands.
- **25–50%** — fails; the status quo holds.
- **< 25%** — backfires: the instigator loses standing, with a cooldown before
  they can try again.

**Title claims.** The first full dilemma is the **title claim**, and it is now
the *only* road to a title you don't legally hold. Conquering a settlement gives
you the land (de facto control) but **not** its title — holding the fiefs is no
longer a usurpation shortcut. To take the title you must:

1. **Fabricate a claim** (the **Claim** button) — it matures over **about a
   year**, costing gold/influence/renown and some relation with the holder.
2. Once the claim has matured, **press it** — for an **in-realm** holder the
   title screen shows **Press Claim**, which opens the realm **dilemma**. Win
   the contest and the title changes hands; a contested result is left to the
   ruler to uphold or deny.

When **you** press a claim, the dilemma is promoted **immediately** (next tick) —
it jumps the queue ahead of AI-driven dilemmas and ignores the realm's dilemma
cooldowns and the active-slot cap, so your deliberate action always surfaces in
**Kingdom → BannerKings → Groups → Dilemmas** rather than sitting forever behind
the realm's other politics. (Previously a player claim could be starved for months
by AI claims refreshing the shared cooldown — it would queue and never appear.)
If the **Politics Rework** toggle is off, there's no engine to run the dilemma, so
in-realm usurps simply apply instantly instead of queuing a dead-end claim.

Cross-realm targets (an enemy lord's de jure title) can't be a realm-internal
dilemma, so a matured claim there is pressed as a direct usurpation instead.
**AI lords follow the exact same path** — they fabricate claims on the titles
they want and press them as dilemmas once mature; they no longer seize titles
instantly by conquest. (With the Politics Rework toggle off, in-realm usurps
fall back to the old instant behaviour, since there is no dilemma system to run.)

**Contested succession.** When a **duchy** passes to its heir on the holder's
death but a rival of **another house** held a near-equal claim by blood, that
rival challenges the inheritance before the realm — automatically, no fabricated
claim needed (their standing *is* their place in the succession line). It appears
as a dilemma like any other (Kingdom → Groups → Dilemmas): the lords take sides by
kinship and loyalty, a decisive showing hands the duchy to the rival, a close
result is left to the ruler to settle, and a weak showing leaves the heir in
place. You'll see this most when **you inherit a duchy and a cousin's house
disputes it** — you defend your inheritance by rallying support. (Only duchies
trigger it — kingdoms run through the king-election instead, and counties/baronies
are too granular; and a dispute is never auto-filed *in your name* — pressing your
own succession claim stays your choice.)

**Demesne law disputes.** Interest groups don't only nag the ruler one law at a
time any more — when a group's frustration boils over and it favours a demesne
law the realm hasn't enacted, it takes the matter to the whole realm as a
dilemma. Watch a group's **tension** climb in **Kingdom → Groups** (it builds
while the group has an unmet grievance and the realm's mood runs against it);
when it peaks, the group raises a **Demesne Law Dispute** in the **Dilemmas**
section. Clans line up by their *own* group's stance — a group that favours the
same law pushes For, one that opposes it pushes Against, and unaligned lords lean
on their relationship with the group's leader. If the For side carries, the law
is enacted on the realm's sovereign title; a divided realm (the contested middle
band) lands on the **ruler's desk** to enact or reject; a weak showing fails and
a rout costs the group's leader some standing with the crown.

For **you**, this shows up two ways. As **ruler**, a group in your realm can force
a law you'd otherwise never have raised — back it by spending influence on
**Support**, kill it with **Oppose**, or wait and make the call yourself if it
ends up contested. As a **group leader** (and not the ruler), leading a group
that wins the contest is how you drag the realm's laws toward your faction's
agenda without holding the throne. (Only AI-led groups raise these automatically —
your own group won't push a law dilemma behind your back; and law grievances now
flow *only* through this contest while the rework is on, replacing the old
one-shot "the X group demands law Y" prompt.)

**Freshly-conquered fiefs are off-limits until the realm decides.** When your
kingdom takes a settlement, it sits under *temporary* ownership (usually the
ruler's) until the kingdom votes who keeps it. During that window the title can
**no longer** be usurped or claimed by anyone — the buttons are disabled with
"The realm has not yet decided who will hold this fief," and AI rulers can't grab
it either. This fixes the exploit where the ruler instantly seized a captured
town's (and its villages') titles before the vote, leaving the clan that *won*
the settlement owning the land but not the title — and forced to claim it back
from their own ruler. Once the vote resolves, the rightful owner can usurp the
title from the former (enemy) holder normally, with no relations cost inside your
realm.

**Pacing.** At most a couple of dilemmas run in a realm at once (tunable via
**MCM → Banner Kings → Balancing → Max Active Dilemmas**); the rest queue and
promote as slots free. Because AI claims must be fabricated and then mature
(~1 year) before they can be pressed, AI-driven claim dilemmas ramp up over time
rather than appearing immediately.

> This system is new and under active development — feedback from testers is
> exactly what it's for. If anything misbehaves, the Politics Rework toggle
> disables it cleanly.

---

## Personal unions — inheriting a second crown

Bannerlord lets a clan rule only **one** kingdom. So if your house comes to hold
the **crown of a second kingdom** — most often by **inheriting it** when another
realm's king dies and you held a claim to that throne — the two realms can't both
keep you as ruler. BK resolves this by **merging them into one united kingdom**
rather than leaving a realm without a ruler (which previously produced a broken,
crash-prone half-state).

What happens when the union triggers:

- **You choose which crown leads.** A prompt asks under which kingdom's name the
  union is ruled — keep your founded realm, or take up the inherited one. (AI
  rulers automatically keep their **larger** realm, by fief count.)
- **The other realm joins it whole.** Every clan of the absorbed kingdom — with
  all their fiefs — becomes part of the surviving kingdom, and its **duchies are
  kept intact**, carried over as duchies of the united crown (so you can still
  grant them to vassals later). The absorbed crown itself is dissolved.
- **Wars follow the surviving crown.** The united realm keeps the survivor's
  diplomatic stances; the absorbed realm's separate wars end with its crown.

In short: instead of two thrones you can't both sit on, you end with one larger
kingdom under a single crown. The merge only fires when a clan genuinely ends up
entitled to two kingdoms; normal vassalage and dukedoms are untouched.

---

## When kingdoms make peace

An AI realm's decision to end a war is now a **continuous weighing of two numbers
you can see on the diplomacy screen — War Fatigue and War Support** — rather than
a fixed trigger:

- **War Fatigue** (rises with casualties and the sheer length of the war): the
  higher it climbs, the more the realm wants out.
- **War Support** (how much the realm's lords still back this war): as it erodes,
  the pull toward peace grows.
- **War score and objective**: clearly losing pushes toward peace; a fulfilled
  casus belli (you took what you came for) is a strong settle-now signal.

These combine into a single "should we end this?" pressure. A **fresh, well-backed
war** sits firmly on the *keep fighting* side — kingdoms won't sue for peace at the
first skirmish (and BK overrides vanilla's eager war-exhaustion offers). As fatigue
mounts and support drains, the pressure climbs smoothly until the realm sues for
peace. So a long, bloody, unpopular war ends; a short, popular, winning one
doesn't — and both numbers genuinely move the outcome instead of being ignored.

This only governs **AI-vs-AI** wars — **your** kingdom's war and peace are yours to
decide; BK doesn't bias your own vassals' votes. BK steps aside entirely when the
Diplomacy mod is installed.

---

## See also

- [Player Guide](Player-Guide) — the action buttons in the BK UI
  that drive these flows from the player side.
- [Systems Reference](Systems-Reference) — the underlying systems
  (titles, contracts, demesne laws, council positions, interest /
  radical groups, government types) that the flows operate over.
- [Troubleshooting](Troubleshooting) — what to do when a flow stalls
  (silent claims, denied revokes, missing peerage votes).
