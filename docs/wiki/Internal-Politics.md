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

**AI lords over their demesne limit shed land back to their vassals.**
The vote above governs *new* conquests; this governs land a clan already
holds. When an AI clan leader is over its demesne limit, it grants fiefs
away each week until it's back under — and it does so **lowest fief
first**: a backwater village goes before a castle, a castle before a
town, and the clan's council-seat town is never given away. The clan is
also never stripped to landlessness — it always keeps at least one fief
even if its limit drops very low. Recipients are chosen for relation and
title fit, and a lord already over **his own** landed limit is skipped
unless no vassal has room — so land flows to clans that can actually
absorb it instead of just relocating the overflow. Because the cheapest
fiefs go first, a deeply over-limit lord may take several weeks to fully
comply. (Only AI clans do this automatically; the player gets a map
notice when over limit and chooses what to grant.)

When the fief being shed is a **village (Lordship)** and there's no
existing vassal with room, the clan **knights one of its own** instead
of forcing the land onto an overloaded lord: a capable companion (or
other eligible non-family clan member) is granted the Lordship, becomes
a knight, and — through the normal knighthood path — soon founds their
own minor vassal clan around that village. This both relieves the
demesne immediately (a fief held de jure by a non-leader clan member
counts as zero toward the clan's demesne) and grows the realm's pool of
small vassal houses, so a large kingdom keeps spawning fresh knightly
clans instead of stalling. It's bounded by the clan's vassal limit and
skips villages that already have a knight clan, so realms don't flood
with knights. Castles and towns can't be knighted, so those still go to
an existing vassal (an over-limit one only as a last resort).

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
on it over the window — so the bar moves as the realm deliberates.

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

**Title claims.** The first full dilemma is the **title claim**. You must
*already hold a valid claim* on a title held by a fellow realm member; then the
title screen shows a **Press Claim** button (replacing the instant Usurp for
in-realm targets). Win the contest and the title changes hands; a contested
result is left to the ruler to uphold or deny. Ambitious AI vassals do the same
— they fabricate claims on rivals' titles and press them once the claims mature,
so claim disputes arise on their own. (Cross-realm claims still use the old
instant Usurp.)

**Pacing.** At most a couple of dilemmas run in a realm at once (tunable via
**MCM → Banner Kings → Balancing → Max Active Dilemmas**); the rest queue and
promote as slots free. Because AI claims must be fabricated and then mature
(~1 year) before they can be pressed, AI-driven claim dilemmas ramp up over time
rather than appearing immediately.

> This system is new and under active development — feedback from testers is
> exactly what it's for. If anything misbehaves, the Politics Rework toggle
> disables it cleanly.

---

## See also

- [Player Guide](Player-Guide) — the action buttons in the BK UI
  that drive these flows from the player side.
- [Systems Reference](Systems-Reference) — the underlying systems
  (titles, contracts, demesne laws, council positions, interest /
  radical groups, government types) that the flows operate over.
- [Troubleshooting](Troubleshooting) — what to do when a flow stalls
  (silent claims, denied revokes, missing peerage votes).
