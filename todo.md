# BK TODO

## Politics rework — remaining

Phases 1–5a, plus the demesne-law integration, are done and committed (all
behind the default-off `EnablePoliticsRework` MCM toggle). Left to do:

- [ ] **Phase 5b — dilemma events.** Rare, weighty either/or political
      events for the player's realm — each pits factions or values against
      each other, every option carrying a real cost. Best as a self-contained
      `BKPoliticalDilemmaBehavior` (new behavior, own state if any) that
      fires an inquiry-driven event occasionally; modest set of events to
      start (~3–5), each applying concrete effects (faction tension, Crown
      Authority, loyalty, relations). Gate on the politics toggle; pace it
      with the MCM Political Pressure scaler.
- [ ] **Phase 6 — Fourberie integration.** Detect the Fourberie mod via
      `BannerKings.Utils.ModCompat` (reflection, per the existing recipe).
      One-directional and robust: BK *reacts* to Fourberie plot outcomes via
      vanilla `CampaignEvents` — a murdered ruler/heir/faction-leader fires a
      succession crisis / swings the relevant tension or transition pressure;
      a destabilised clan shifts its faction weight. Do NOT build a parallel
      scheme system — Fourberie owns the cloak-and-dagger, BK owns the
      political stakes. BK→Fourberie feed (surfacing BK grudges as contract
      fodder) is best-effort only.

- [ ] **Phase 7 — AI political intelligence.** AI interacts with the
      politics systems through one derived scoring layer, minimal hardcoding.
  - [x] **7a — `BKPoliticalDisposition`.** Per-clan disposition, three signed
        axes — Centralism (+centralist/-autonomist), Ambition (+ambitious/
        -content), Militarism (+militarist/-developer) — derived from
        personality (traits), culture (realm insider/outsider, measured not
        per-culture), and holdings economics (foreign-pop share via
        `CultureData`, prosperity, relative strength). Lazy daily cache,
        never saved. All weights centralised in `BKPoliticsTuning`.
  - [x] **7b — Coalesced kingdom politics.** Rewire the 6 AI touch-points
        (CA propose/vote, demesne-law vote, transition levers,
        `GetAscendantForce`, Republic mandate) to read the three axes;
        derive group constitutional pull from group data, not the StringId
        switch. Vassal intent funnels through bloc tension → one coalesced
        proposal; realm proposal cooldown (~3-6 wk, one `CampaignTime` on
        `KingdomDiplomacy`) so the player isn't spammed. Per-kingdom
        evaluation staggered by day-of-week; nothing added to the daily tick.
  - [x] **7c — `BKVassalPoliticsBehavior` + government-typed levers.**
        Vassal-to-vassal politics: derived rivalries, sensing weakness,
        government-typed climbing (loyal vs treacherous, keyed on
        `PoliticalLayer`). Routes through vanilla relation/renown/transition-
        pressure primitives — council appointment and the usurpation cycle
        consume those, so the climb emerges; no claim/council/succession
        reimplementation. AI-vs-AI, surfaces to the player only as a
        notification when a treacherous move targets them.
  - [ ] **7c-follow — player-facing levers.** The Realm Politics screen's
        options become government-dependent so the *player* can pull the
        same loyal/treacherous climbing levers their AI rivals use.
        Deferred from 7c (separable inquiry-UI work).
  - [ ] **7d (proposed) — Crown-Authority vassal limit.** Lower Crown
        Authority raises a realm's direct-vassal capacity, higher CA lowers
        it (`limit = governmentBase + (ceiling - CA) * N`); over the limit
        feeds faction tension. Forces the wide-vs-centralised dilemma.
        Awaiting go-ahead; grep first for any existing BK vassal cap.
- [ ] **Before the politics rework ships (version bump):** run a full cold
      critic pass over the whole rework, and an in-game playtest with the
      toggle on. It is a large body of code now (~14 commits), all behind the
      toggle — verified to compile, not yet verified in play.
- [ ] **Optional — demesne-law balance sweep.** The laws are wired and now
      integrated with Crown Authority; a pass to confirm each law's effect
      magnitude is sensibly tuned, and that the "Standard" slavery and
      "Council Appointed" laws are intentional no-modifier baselines.
