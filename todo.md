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

- [ ] **Before the politics rework ships (version bump):** run a full cold
      critic pass over the whole rework, and an in-game playtest with the
      toggle on. It is a large body of code now (~14 commits), all behind the
      toggle — verified to compile, not yet verified in play.
- [ ] **Optional — demesne-law balance sweep.** The laws are wired and now
      integrated with Crown Authority; a pass to confirm each law's effect
      magnitude is sensibly tuned, and that the "Standard" slavery and
      "Council Appointed" laws are intentional no-modifier baselines.
