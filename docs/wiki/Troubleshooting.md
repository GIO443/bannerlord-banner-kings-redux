# Troubleshooting

← [Home](Home)

## On this page

- [Edge cases & frequent confusions](#edge-cases--frequent-confusions)
- [Save-game safety](#save-game-safety)
- [Reporting bugs](#reporting-bugs)
- [Credits & license](#credits--license)

---

## Edge cases & frequent confusions

- **"My title disappeared"** — usually inherited by an heir on a death you
  didn't notice, or absorbed into a higher-tier title via succession. Check
  the title event log in the encyclopedia → titles tab.
- **"BK menu is empty"** — the feature was disabled in the MCM settings.
  Re-enable and reload the save.
- **"A huge bandit army (500–600+) is sitting outside a town doing
  nothing"** — fixed in v1.9.16.18–.19. BK spawns special bandit-hero
  hordes; an inflated size formula let them balloon to ~600 troops, and
  a 50/50 behaviour roll could leave a horde "robbing" a town — which
  in practice meant patrolling outside it inertly, scaring off parties
  without ever attacking. Now hordes are capped far smaller and **always
  pursue an active village raid**: they move toward a reachable village,
  raid it, then pick the next. If you still see a frozen giant horde on
  an old save, it clears within a week of in-game time as the new logic
  re-evaluates its target. No player action needed.
- **"Crash hovering parties in the Army Management screen"** — fixed.
  Caused by BK's mercenary eligibility tweak leaving the hover tooltip's
  reason text null. Update to a current Banner Kings — Redux build.
- **"Crash on opening Inventory / toggling equip-mode"** — fixed in
  v1.9.10.5. Vanilla's `TownMarketData.GetCategoryData(null)` throws
  `ArgumentNullException` from `Dictionary.FindEntry` when an item in
  your inventory has no `ItemCategory` set — usually a custom item
  from another installed content mod whose XML omitted the category
  attribute. Finalizer on `InventoryLogic.GetItemPrice` returns 0 for
  the affected slot so the rest of the inventory renders normally.
- **"Crash on daily tick: NRE in Religion.GenerateClergyman"** — fixed
  in v1.9.10.5. When a Faith doesn't have a rank title configured for
  a settlement's ideal rank, or when no eligible hero culture exists
  for the preset, the clergy generator NRE'd on the daily settlement
  tick and killed the campaign. Defensive null guards on the title,
  generated hero, and culture lookups. The generator simply skips that
  settlement this tick and retries next day.
- **"Crash on AI tick: IndexOutOfRangeException in GetCharacterAtIndex /
  morale calculation"** — fixed in v1.9.10.4. v1.9.9.6 already added a
  finalizer on the inner `TroopRoster.GetCharacterAtIndex` method, but
  it only swallowed the IOOR throw — vanilla's morale skill-bonus loop
  then dereferenced the null character it received and re-threw NRE
  from the same spot, killing the AI tick the same way. The fix wraps
  the actual vanilla method (`DefaultPartyMoraleModel.GetMoraleEffectsFromSkill`)
  in a defense-in-depth finalizer that catches both IOOR and NRE from
  a corrupted roster slot table. Affected parties get vanilla baseline
  morale until the roster self-heals on save/load. No player action
  needed — load the affected save and the AI tick proceeds.
- **"My kingdom's Legitimacy is always 0"** — fixed in v1.9.10.3. The
  clan-tier penalty in the legitimacy model was on the wrong scale —
  whole-number penalties (-5/-10/-20) sat next to fractional bonuses
  (+0.075/+0.10/+0.30) for titles, culture, and faith. Any tier-4 or
  lower ruler's Legitimacy target went deeply negative and clamped to
  0 regardless of how well they were playing the realm. Penalty
  rescaled to match the fractional scale (-0.05/-0.10/-0.20). Open
  Kingdom → Demesne → Legitimacy breakdown to verify the contribution
  now sits alongside the title / culture / faith lines instead of
  dominating them.
- **"Knighted hero's party shows up in both my clan and the new knight
  clan"** — fixed in v1.9.10.10. `ClanActions.CreateNewClan` (the path
  knighthood uses to spin a new noble clan from a hero) moved the hero's
  `Clan` reference but never re-parented the party they were leading.
  Vanilla source of truth for which clan owns a party is
  `MobileParty.ActualClan`, not `Hero.Clan` — setting that triggers
  `WarPartyComponent.OnClanChange(old, new)` which actually moves the
  entry between the two clans' `WarPartyComponents` collections. Without
  it the party was visible in both the original clan and the new knight
  clan on the management screen, and downstream consumers (income,
  recruitment, council role assignment) saw conflicting ownership.
  Fixed in `ClanActions.CreateNewClan` and `ClanActions.JoinClan`; the
  knight now correctly belongs to (and brings their party to) only the
  new clan. Existing save? Load it — re-knighting any affected hero is
  not required, but BK doesn't auto-heal historical knight clans;
  manual fix is to disband and re-form the affected knight clan via
  the cheat menu, or accept the cosmetic duplication for legacy
  clans.
- **"Daily ~10-20s freeze + 'failed peace settlements' spam every day"**
  — fixed in v1.9.10.7. v1.9.10.6 lowered the threshold at which BK
  force-proposes peace so stalemate wars would actually wind down. But
  the existing "already queued" guard only skipped wars whose previous
  proposal was still in the kingdom decision queue — once a vote
  completed (succeed or fail), the same proposal re-queued the next
  day, so every war in the world ran a full vanilla KingdomElection
  vote daily. The freeze was the cumulative election cost; the spam
  was the per-war "force-propose peace" log line. Two fixes: a
  14-in-game-day cooldown per (kingdom, target) pair after BK queues
  a peace decision, and a stronger vote-push curve so the proposals
  that do get queued actually carry.
- **"Voted to change a demesne law, won the vote, but it shows the old
  law / the popup says 'X to X'"** — fixed in v1.9.11.8. The law change
  itself was applying correctly all along (the aspect's hover tooltip
  showed the new law); the bug was display-only. The kingdom-decision
  text built its "{NEW} replacing {OLD}" line by comparing the realm's
  current contract against the proposal *live* — but the outcome popup
  and the decision panel render *after* the change has already been
  applied, so the comparison found no difference and printed the new
  law on both sides ("Agnatic to Agnatic") or a stale aspect name. The
  from→to names are now captured when the proposal is made, so the
  popup and panel read correctly regardless of when they render.
  (Earlier fixes in this chain: v1.9.11.3 made the aspects actually
  apply, v1.9.11.4 fixed the post-reload re-bind, v1.9.11.6 fixed the
  vote scoring for leanless laws.)
- **"Random hard freeze (must force-quit), often around a peace deal,
  ~a day after loading"** — fixed in v1.9.11.7. BK's per-kingdom truce
  and trade-pact records were a plain dictionary/list read by the
  diplomacy screen and the influence-cap tooltip (UI thread) while the
  campaign thread wrote them on peace deals, war declarations and the
  daily cleanup. A read landing during a write's internal resize could
  corrupt the collection and spin a CPU core forever — a hard hang with
  no crash report. The v1.9.11.1 war fix made the AI sign far more
  truces/peaces, so a long-latent race started firing regularly (and
  reverting that version made it rare again, which is why it looked
  like a peace-deal bug). All access is now serialised. No save changes;
  existing saves are unaffected.
- **"Wars never end / forever-war stalemates"** — extended in v1.9.10.6.
  v1.9.10.2 fixed *decisively losing* kingdoms (war fatigue past 0.6
  with a clearly negative war score) — that vote now passes. But many
  forever-wars are stalemates: two evenly-matched kingdoms grinding
  away with high mutual fatigue but neither side's war score is
  decisively negative. Neither side qualified as "losing" so nobody
  proposed peace and nobody pushed the vote. The proposer now also
  queues peace when **either** side is sufficiently exhausted
  (fatigue >= 0.5 with at least neutral score). The vote-push formula
  adds an `exhaustion` term that ramps from 0 at fatigue=0.5 to 0.5
  at fatigue=1.0, so both sides of a stalemate get a mild push toward
  peace while decisive losers still get the strong push. Wars that
  used to sit at 90% mutual fatigue forever should now wind down.
- **"No one is declaring war / kingdoms sit at peace for decades"** —
  the real fix landed in **v1.9.11.1**. If you saw the diplomacy screen
  report high *War Support* (even 100%) yet no AI war ever started, this
  was the cause. BK replaces vanilla's war/peace scoring with its own
  scale (`GetScoreOfDeclaringWar` returns small numbers centred on zero:
  negative = don't, positive = do), but it never replaced the matching
  *decision threshold*. Vanilla's default threshold is roughly the target
  kingdom's total settlement value ÷ 6 — tens of thousands of points,
  calibrated to vanilla's own scoring. The game discards any war proposal
  that scores below the threshold **before it is ever put to a vote**, so
  BK's small scores never cleared the bar and every AI war was thrown out
  pre-vote. (The *War Support* % is a separate calculation that ignores
  this threshold — hence the "100% support but no war" disconnect.)
  v1.9.11.1 overrides the threshold to `0` so a net-positive war appetite
  is enough to put the war to a vote, and reworks the no-justification
  scoring so a kingdom with a clear strategic edge (much stronger, or a
  weak isolated neighbour) will declare an opportunistic war even without
  a formal casus belli, while evenly-matched neighbours stay at peace
  unless they have one. Earlier partial fixes (v1.9.10.33 / .42) had
  shortened AI-bought truces to 1 year and softened the per-existing-war
  penalty — those still apply — but the threshold mismatch was the
  dominant blocker. **Note:** "no truces forming" was a *symptom* of this,
  not a separate bug — truces are bought to wind down an existing war, so
  with no wars there were no truces. Once wars resume, truces reappear.
  After updating you should see fresh wars begin within in-game weeks.
- **"Captured castle has no ownership vote and can't be granted"** —
  fixed in v1.9.10.41. BK's `SettlementClaimantDecision` patch filtered
  candidate clans by `Peerage.CanHaveFief`, then required *more than
  two* eligible clans for the vote to even appear. In small kingdoms,
  or after the prior owner was excluded, that gate routinely failed →
  no decision queued → settlement stayed `IsOwnerUnassigned` forever,
  and the Kingdom screen's "Grant Fief" path was equally stuck because
  it dispatches through the same decision. Now: if the peerage filter
  empties the candidate list, BK falls back to every otherwise-eligible
  clan (still excluding mercenaries, eliminated clans, and clans with
  dead leaders), and the IsAllowed gate requires only `>= 1` candidate.
  After capture, the decision should appear in Kingdom → Decisions
  within the normal 1-3 in-game day window.
- **"War Support says 0% but nobody votes for peace"** — fixed in
  v1.9.10.2. The kingdom screen's *War Support* % runs BK's full
  decision model (war fatigue, war score, casus belli expiry), so it
  drops to zero on hopeless wars. The peace vote, however, used pure
  vanilla heuristics (kingdom strength, fief threat) and never saw
  those BK signals, so every clan voted "stay at war" and you got
  forever wars. The peace vote now reads the same BK fatigue + war-
  score and pushes losing-side clans toward peace proportional to how
  badly they're losing. The winning side keeps voting against peace —
  only kingdoms BK actually flags as losing get the nudge. Open the
  Kingdom → Diplomacy screen, propose peace, and the vote should now
  carry when *War Support* is at or near 0%.
- **"My army disbands far too soon"** — fixed. BK's
  cohesion postfix was clamping the daily change at a forced loss,
  blocking every vanilla recovery condition (camped at home, food,
  leader perks). The MCM "Army Cohesion Boost" slider now actually
  matches its tooltip: at 50% it halves daily cohesion loss; at 0% it
  matches vanilla.
- **"Council Marshal didn't reduce wages"** — the reduction is
  multiplicative; other modifiers (custom troop, mercenary status) can
  dominate. Check the wage tooltip breakdown in the party UI.
- **"Estate showing zero income"** — check the visit panel for an
  **Income Blocked** reason (war with the village's faction, BK title
  manager not loaded, owner→estate registry desync). See the full
  recipe under [Player guide → Estates](Player-Guide#estates). On older
  builds, upgrade — current builds added a backstop payout that fixes
  the silent finance-model-replaced-by-another-mod case.
- **"Can't change demesne law"** — locked behind a contract-change cooldown
  (≈ 1 in-game year) and minimum loyalty / authority gates.
- **"Skills level too fast in Banner Kings"** — older builds shipped
  with the *Alternative Leveling* MCM toggle on by default, and its XP
  curve only added ~20 XP per level past level 1, so any small XP gain
  rocketed you through 10+ levels. The toggle is now **removed
  entirely** — every save uses vanilla's XP curve regardless of what
  value the MCM file remembers from a previous version. No
  action needed; load your save and skills will progress at vanilla
  rates.
- **"Language learning finishes instantly"** — symptom of the same
  alternate-leveling explosion (Scholarship XP racing up boosted the
  language-rate skill effect off the rails) plus an unsafe rate path.
  Per-tick fluency gain is now hard-capped at 5%, so even with the worst
  rate inputs a language can't finish in fewer than ~20 in-game days.
- **"Language learning rate is 0 / fluency never grows / book reading makes
  no progress" (properly fixed v1.9.30.0)** — this was a stubborn one that
  several earlier patches only half-fixed. Your fluency was stored in a
  per-hero table *keyed by the language object itself*. When a save reloaded,
  those keys came back as fresh copies that no longer matched the game's master
  language list. Earlier builds tried to "re-link" the table on load, but the
  *currently-learning* language could still slip back to a stale copy, so the
  daily tick quietly added progress to an invisible duplicate entry while the
  screen kept showing 0 — looking exactly like "nothing is happening" for weeks.
  As of v1.9.30.0 the backend was rebuilt to store fluency and book progress
  **by the language's text id (a plain string)** instead of by object, so there
  is no object identity to go stale — what the daily tick writes and what the
  screen reads are always the same entry. **Existing saves heal automatically
  on load**: your already-learned languages and books are migrated into the new
  store, and a language you were mid-learning resumes growing. (To confirm, run
  `campaign.bannerkings.education_debug` in the console — it writes
  `BK_education_debug.txt`; the `manual UpdateHeroData` line should now show a
  non-zero delta on the language you're learning.)
- **"A workshop / rite / building / quest says I don't have a good even though I
  clearly do" (fixed v1.9.31.0)** — BK used to stamp a random *quality modifier*
  (crude / fine, etc.) onto goods produced by workshops and villages, so your
  stock could be all "Fine Grain" with no plain "Grain". Anything that asks for a
  good by its base type — workshop input consumption, religious rite offerings,
  building material costs, and **vanilla quests** — counted only the plain
  version and saw zero, so it blocked even though your inventory was full.
  Trade goods are meant to be fungible commodities, so as of v1.9.31.0 BK **no
  longer puts quality modifiers on produced goods** (the workshop keeps the
  quality bonus as extra gold revenue instead). **Existing saves heal on load**:
  a one-time pass strips the stray modifiers off all trade goods in every market,
  stash and party, so requirements recognise them immediately. (Side effect:
  village trade goods no longer fetch a small "quality" price premium when sold —
  a minor, intentional economy correction. Weapons, armour and horses keep their
  modifiers — those are legitimate.)
- **"Late-game days take minutes to pass / the game crawls but doesn't hard-freeze
  (improved v1.9.23.3)."** A vassal-list lookup used all over BK (banner-calling,
  levies, army formation) is cached once per day per clan — but the cache was being
  thrown away *entirely* every time any title changed hands anywhere in the world.
  In a busy late-game realm with frequent usurps/claims, that meant the expensive
  lookup recomputed for every clan over and over, dragging out the daily tick. The
  cache now only refreshes the two clans actually involved in a title transfer, so
  it stays warm. If your saves still crawl, turn on **MCM → Diagnostics → Enable
  Freeze Detection** and send `BK_slow.txt` — it names whichever handler is eating
  the time.
- **"How do I use the Religion / Theology system?"** As of v1.8.9.0
  the seven culture faiths (Darusosian Path, Canticles of Caïon, Amra
  Druidh, Path of Akhmar, Six Winds, Old Gods of the North, Osfeydian
  Tradition) are seeded and functional — heroes get a
  faith automatically, preacher notables generate at settlements,
  and dialogue with a preacher of your faith gives blessings, rites
  and induction options. See [Player-Guide → Religion](Player-Guide#religion)
  for the procedural how-to and [Systems-Reference → Faiths](Systems-Reference#faiths)
  for the per-culture table. Theology XP ticks from piety gain
  (battles, rites, doctrines). If you see anything that looks
  broken — empty preacher dialogue, induction with no effect, a
  doctrine that doesn't fire — file an issue with a Better Exception
  Window report.
- **"How do I disable the religion system entirely?"** As of v1.8.10.0
  there is a master kill switch in **MCM → Banner Kings → Performance →
  Enable Religion System**. When OFF, BK skips seeding the seven
  default religions, doesn't register the religion campaign behavior,
  and doesn't surface piety in the map bar — the entire system goes
  dormant. **Requires a restart** because seeding happens at
  game-data-load time. Save data already containing religion entries
  (loaded from a religion-enabled save) is silently dropped on the
  first PostInitialize tick — heroes lose their faith assignments,
  no further piety accrues. Saves made on the religion-disabled
  branch can later be re-loaded with religion ON and the seven
  faiths re-seed automatically; existing heroes get their ideal
  faith assigned via the normal daily-tick fallback. Default ON so
  existing saves keep their religion state on upgrade.
- **"A caravan is walking visibly across open water"** — fixed in two
  layers: a daily rescue sweep steers stranded
  parties to the nearest sea-reachable port, and the routing graph
  now reads the engine's `HasPort` flag directly so previously-missed
  coastal towns (Omor, Varcheg, Sibir, Argoron, Sargot) no longer
  leave a gap caravans can fall through. To unstick a specific party
  immediately without waiting for the daily sweep, enable cheats and
  run `bannerkings.unstrand_party <name substring>` — it redirects
  the first matching caravan or lord party to its nearest port.
- **"Crash every time I open castle/town management to set a governor"** —
  was an NRE inside vanilla `DefaultDelayedTeleportationModel.GetTeleportationDelayAsHours`
  when the candidate hero hovered in the picker had no clan reference (which
  some BK-tracked heroes can briefly end up with). Fixed: a defensive
  prefix on that vanilla method returns a zero teleport delay instead
  of crashing. Update to a current Banner Kings — Redux build if you
  still see this.
- **"My new game crashes during loading"** — almost always a non-BK mod's
  Harmony patch failing (e.g., GovernorsHandleIssues against newer
  Bannerlord builds). Install **Better Exception Window** if you haven't
  already (see [Reporting bugs](#reporting-bugs) below) and read the
  inner exception in the crash report — it usually names the offending
  mod by its patch method, and you can disable that mod and continue.
- **"Save load crashes with a BannerKings GetPopData NRE"** — fixed.
  Cause: vanilla `Clan.AfterLoad` recomputes party strength early in
  the load sequence, which reaches `Town.FoodChange` → BK's food model
  → `PopulationManager.GetPopData` before BK's population caches are
  wired up, so the lookup NRE'd. The fix is a null-guard: BK's food
  calc cleanly falls back to vanilla until `PostInitialize` runs, then
  takes over.
- **"Crash entering any town with a `MountCreationKey.GetRandomMountKey`
  NRE in `MissionHelper.SpawnCows`"** — fixed. Cause: BK's price-adjust
  pass on game create/load was calling `ItemObject.InitializeTradeGood`
  on the vanilla cow item, which is the wrong helper for items with a
  `HorseComponent` and stripped the cow's mount data. Vanilla town-
  center scenes then NRE'd trying to spawn cows. The fix is to leave
  the vanilla cow item alone; cow pricing falls back to vanilla.

---

## Save-game safety

- **Saves are version-tagged.** Loading a save from an older Banner Kings
  Redux build runs a migration where defined; otherwise old fields keep
  their values and new fields lazy-init to safe defaults.
- **Removing BK from an active save is not safe.** References to BK objects
  (titles, estates, custom troops) become orphaned and the save will corrupt.
  Once you start a save with BK, keep BK installed for the life of that save.
- **Updating BK on an active save is generally safe within a minor version.**
  Major-version updates (e.g. upstream BK → Redux) may require a fresh save.
- **Switching from upstream Banner Kings to Banner Kings — Redux on an
  existing save is not supported.** The two are separate modules with
  separate save data. Start fresh.

---

## Reporting bugs

### Install Better Exception Window first

Before you submit a crash report — and ideally before you even play a
save you care about — install **Better Exception Window** from Nexus
([Bannerlord.BetterExceptionWindow](https://www.nexusmods.com/mountandblade2bannerlord/mods/404)).
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
  against the live 1.4 DLLs.
- Compile warnings about obsolete types — 1.4 deprecations not yet
  fully removed. They don't affect runtime.
- "GovernorsHandleIssues crashed" — that's a different mod failing to
  patch a method. Disable it.

Issues for this fork specifically (1.4 compatibility, War Sails / Nord
integration, seafaring lifestyles, Nordic Thrall Law) belong on the GitHub
repo:

**https://github.com/GIO443/bannerlord-banner-kings-redux/issues**

Issues with the original Banner Kings systems (titles, estates, council,
etc. — anything also present in the original release) are upstream
BK problems. They'll get fixed in Redux as we encounter them, but the
underlying design is R-Vaccari's.

### Diagnostic logging

BK can append a focused event log to help diagnose a misbehaving system.
Open **MCM → Banner Kings → Diagnostics** and turn on the toggle for the
system you're investigating — e.g. **Log Politics Rework**. Each toggle
writes a plain-text file under
`%LOCALAPPDATA%\BannerKings\ModLogs\` (e.g. `BK_politics.txt`). These
logs are quiet — they fire on actual events, not every tick — so it's
fine to leave the relevant one on for a play session and attach the file
to a bug report. Turn it back off when you're done.

`BK_politics.txt` records Crown Authority changes, government-transition
pressure and realm government changes / usurpations, faction-tension
escalations, Imperial donative shortfalls, and Republic mandate changes.

#### Reporting a freeze (the easy way)

If you hit a freeze of several seconds — or the long "stuck on day → night"
freeze some players have seen, especially deep into a campaign (1000+ days) —
BK can name the culprit for you. **First, turn the detector on:** open
**MCM → Banner Kings → Diagnostics → Enable Freeze Detection** (no restart
needed). It's off by default because it runs a small background watcher;
turn it on only while you're hunting a freeze.

The moment you enable it, `BK_freeze.txt` is created in the ModLogs folder
above with a single `freeze watchdog ARMED …` line — that's your
confirmation it's active and where the file lives. (If you don't see the
file after enabling the toggle, the folder is
`%LOCALAPPDATA%\BannerKings\ModLogs` — paste that into the File Explorer
address bar.) Then play until the freeze happens and check these files:

> **Important — grab the log BEFORE you reload.** Loading a save clears the
> current `BK_freeze.txt` so each play session starts clean. If you froze,
> force-quit, and reloaded your save to look around, the freeze you just had
> has been archived to **`BK_freeze.prev.txt`** (one generation back) — send
> *that* file. The simplest capture is: right after a freeze, force-quit, and
> copy `BK_freeze.txt` out of the ModLogs folder **before** launching the game
> again. (`.htm` auto-crash reports are never cleared, so those always stay.)

**`BK_freeze.txt` — send this one first (the WHOLE file).** A background
watchdog records four kinds of line:

- `alive — heap …MB, workingSet …MB …` every ~15 seconds — a heartbeat that
  proves the watcher is running and tracks memory. A **gap** between two
  heartbeats is itself the freeze (the game was frozen for that long).
- `STUCK <system>:<entity> running Ns …` — a specific BK system is stuck
  right now, named while the freeze is still happening.
- `RUNTIME STALL — every managed thread was frozen for Ns … gen2 GCs +N …`
  — the *entire game* (not one system) locked up, typically a long garbage
  collection. The `gen2 GCs +N` and memory figures tell us if runaway
  memory is the cause.
- `ARMED …` — written once when you enable detection.

Example of a system-level stall:

```
[14:02:16] STUCK ShippingGraph.Build running 5s — campaign thread not progressing. heap 612MB …
[14:02:26] STUCK ShippingGraph.Build running 15s — campaign thread not progressing. heap 640MB …
```

Don't worry about reading it — just send the whole file. The lines around
the freeze tell us exactly which BK system (or whether memory/GC) locked up
and for how long.

**Auto-crash on a confirmed freeze (v1.9.17.0+).** A true freeze used to mean
the game was stuck forever and you had to force-quit — losing the session and
most of the diagnostic detail. Now, when the watchdog confirms a *genuine*
hang (the game wedged inside one operation for 20+ seconds with the garbage
collector frozen, i.e. it will never recover), BK writes a one-page report
named `BK_freeze_crash_<date>_<time>.htm` to the same ModLogs folder as
`BK_freeze.txt` (`%LOCALAPPDATA%\BannerKings\ModLogs`) and then crashes the
game on purpose, so you can restart instead of force-quitting.
**Open that .htm in any browser and send it** — it names the exact party and
destination whose movement hung (plus position, whether it can sail, and the
land/sea path distances), which is precisely what's needed to fix the cause.
This only happens while **Enable Freeze Detection** is on; it is controlled by
**MCM → Banner Kings → Diagnostics → Hard-Crash on Confirmed Freeze** (on by
default). Turn that sub-option **off** if you'd rather keep the freeze logging
without the automatic crash. Normal slow-but-recovering hitches never trigger
it — only a wedged game with a frozen GC does.

**Late-game politics-tick optimization (v1.9.20.0).** On very mature saves
(many kingdoms, clans, and settlements) the daily diplomacy/politics tick could
grow heavy enough to stutter or freeze. Several internal causes were fixed:
faction-group influence is now computed once per kingdom per day instead of
rebuilt for every lord and notable; war-justification proximity uses a cheap
distance estimate instead of a full pathfind (which could also wedge on a bad
tile); and the pending-claim queue is now capped so it can't grow without
bound. No settings or actions change — large late-game realms just tick faster.

**More diplomacy freeze fixes (v1.9.21.0).** Two further causes were closed: a
rare hard freeze when the faction-group screen was open as members joined (a
thread-safety race on the join-time table — now serialized), and the war-AI's
realm-distance and front-proximity checks, which used a map-pathfind that could
wedge on a bad tile, now use a cheap straight-line estimate. **War Sails note:**
because the new estimate is straight-line, the AI now treats realms separated by
a narrow strait as *closer* than the old sea-route pathfind did — a faction may
be a touch keener to declare war across water. If you notice the naval war AI
behaving oddly with War Sails, mention it in a report; it's an intentional
trade to remove the freeze and can be refined.

**Freeze when disbanding an army (fixed v1.9.21.1).** Disbanding an army
(especially an AI-led one gathered with influence) could reliably freeze the
game. On disband the member parties are released and each resumes its own
objective — and a party resuming an *unreachable* besiege/raid/defend target
(e.g. across water, or an island it can't actually path to) re-issued a
movement command every tick that wedged the engine's pathfinder. The
reachability guard that already protected ordinary "go to settlement" moves now
also covers besiege, raid, and defend, so an unreachable combat objective is
skipped (the party re-decides) or, for a fleet, routed by sea — instead of
hanging.

**Doomed "1-party armies" no longer form (v1.9.21.2).** The disband freeze
above was usually triggered by an army that had only its leader and no other
parties — created when a lord was allowed to call an army even though no party
could actually reach them to join (everyone eligible was across water or too
far). That army immediately disperses for "not enough parties," and the disband
was where the freeze landed. A lord is now refused army formation unless at
least one party can really reach them, so the doomed one-party army isn't
created in the first place. (Side effect: an army whose *only* possible members
are reachable solely by sea won't form for now — that needs the sea-aware join
path, a later change — but a mixed army with any land-reachable member still
forms normally.)

**The disband freeze itself is fixed (v1.9.21.5).** The freeze-auto-crash report
pinned it down: when an army disbands, the engine scatters the freed parties
around the leader using a navmesh search, and that search never finishes if a
party's land/sea (sailing) state disagrees with the terrain it's actually on — a
state desync. BK now corrects that sailing flag to match the real terrain the
instant before an army disbands, so the search always completes and the disband
can't hang. This covers **every** disband (player or AI, any cause), so the
"disbanding an army freezes the game" report should be resolved. (A brief
v1.9.21.3 attempt to auto-clean one-party armies on load was reverted in
v1.9.21.4 — it tripped this very hang during loading — and is unnecessary now
that the disband itself is safe.)

**General navmesh-search guard (v1.9.21.6).** The same "find a reachable point"
navmesh search is used elsewhere too (any time the game repositions a party),
and it hangs the same way whenever a party's land/sea state is out of sync with
the ground under it. BK now intercepts that search: if the spot it's searching
from is invalid for the party's current land/sea mode (the exact condition that
makes it spin forever), BK returns that spot immediately instead of letting it
hang. If a freeze still slips through here, the watchdog now names this search
specifically, so the auto-crash report can pinpoint it.

**Army-gather / escort freeze (fixed v1.9.21.7).** The watchdog named this one
exactly — `SetMoveEscortParty`. When an army gathers, the called parties are
sent to **escort** the leader; if one of them can't actually reach the leader
(the leader is across water, or the party's land/sea state is off), the engine's
escort pathfind hangs the game. BK now reachability-checks the escort (and the
"engage another party" move) the same way it does settlement moves: an
unreachable escort is skipped (the party re-decides) or, for a fleet, routed by
sea — instead of hanging. This was the remaining named cause behind the
late-campaign army freezes.

**Doomed armies that "wait for members" forever (fixed v1.9.22.0).** Some saves
carry a stuck AI army that never grew past its own leader (a "1-party army") and
sits permanently *waiting to gather*. Every in-game hour that army tries to walk
its leader toward the rally point — and if the leader's land/sea state is off, or
the rally settlement has no route from where the leader actually is, that hourly
move hangs the game. (The tell in `BK_freeze.txt` is a stall that reads
`current (idle); last FindReachablePoint` — the freeze is in the move issued
right after the rally-point search.) Players had been clearing it by killing the
army's leader; that works only because it deletes the army so the hourly tick
stops. BK now fixes the cause: before each hourly gather, it corrects the
leader's land/sea flag to match the real terrain, and if the rally settlement is
genuinely unreachable it **skips that move** so the army falls idle and vanilla's
own "inactive army" rule disbands it cleanly within a day or so — no need to kill
anyone. A healthy gathering army is untouched. If a navmesh search ever does hang
after this, the watchdog now names *both* of the engine's reachable-point
searches (the last one was previously unnamed), so the report can pinpoint it.

**"The army marker is there but nothing's under it" — off-the-map armies
(fixed v1.9.22.2).** A worse form of the stuck army: its leader's map position
lands on a spot with **no walkable ground at all** (off the navigation mesh —
effectively "under the map"). The name-plate still shows where the army logically
is, but the army model renders under/off the terrain, so it looks like it isn't
there. This is the same family as the freeze: with no valid ground under it,
*any* move the army tries hangs the game. The base game only repairs this on a
battle→map transition, so an army that drifts off-mesh mid-campaign just sits
there, invisible and freeze-prone. BK now repairs it every hour — if the leader
is off the mesh, it's snapped back to the nearest valid ground (and its attached
parties with it), so the army reappears where its marker says and can move or
disband normally. A correctly-placed army is never moved.

**Army-gather freeze was BK's own reachability check (fixed v1.9.22.4).** The
single most stubborn freeze in this saga — the game locking up hard (often while
armies were forming, the watchdog naming `SetMoveEscortParty`) — turned out to be
**self-inflicted**. To *avoid* a hang, BK was pre-checking "can this party reach
its target?" by asking the engine for the combined land+sea route distance. On
the War Sails map that combined-route query is itself the thing that wedges the
game on certain coastlines — so the safety check was causing the very freeze it
was meant to prevent, and it ran for every escorting party and every gathering
army every hour (which also explains the heavy stutter beforehand). BK no longer
makes that query anywhere on the movement path; it only does the cheap, safe
repairs (fixing a party's land/sea flag and snapping off-mesh parties back onto
the map) and lets the base game's own pathfinder decide routes. This should be
the end of the gather/escort freezes **and** the pre-freeze slowdown.

A follow-up sweep (v1.9.22.5) hunted down the same pattern everywhere else:
village trade parties were being sent with the wrong (land+sea) movement mode on
every trade run — now corrected to the plain land route they actually use — and
the dormant caravan-shipping reachability check had the same risky query removed.
The remaining land+sea distance lookups in the mod are all town-to-town
calculations (used for war scoring and trade range), which don't touch the
freeze-prone path.

**Caught at birth, too (v1.9.23.2).** The repair now also runs the instant an
army is *formed*: if the new army's leader (or a starting member) is off the
mesh, it's snapped onto valid ground immediately — so a degenerate army can't
even be born off-map and start its gather ticks from an invalid position. Between
this, the hourly repair, and the disband heal, an off-mesh army is caught at
formation, every hour it exists, and when it dissolves.

**Disbanding now heals, too (v1.9.22.3).** If you (or the game) disband one of
these off-the-map armies, the released parties no longer spill out stranded:
the disband first snaps the leader and **every** member back onto valid ground,
so the freed parties come out on the map, visible and movable, instead of
inheriting the broken position. So disbanding a degenerate army is now a clean
repair — no leader-killing, no left-over stuck parties. Healthy armies disband
exactly as before.

**`BK_slow.txt` — the backup** (also needs the toggle on). Logs any single
BK handler that took over 3 seconds, *after* it finishes:

```
[14:02:57] SLOW BKShipping.TickParty:caravan_party_1138 took 41200 ms
```

**Attach `BK_freeze.txt` (and `BK_slow.txt` if present) to your bug report,
then turn the toggle back off.** If neither file appears during a freeze,
BK's own systems stayed responsive and the cause is elsewhere — turn on
**Log Hourly Tick Perf** and send `BK_tick_trace.txt` as the last-resort
fallback.

### Testing the politics rework

To check the politics rework without waiting for a campaign to develop,
enable cheats (`cheat_mode 1` in `engine_config.txt`, or via the
launcher) and use the console (Alt+~):

- `bannerkings.politics_dump` — writes a full snapshot of your realm's
  politics (government, Crown Authority, transition pressure, faction
  tensions, every clan's vote weight) to `BK_politics_dump.txt` in the
  ModLogs folder. Pass a kingdom name, or `all`, to dump others.
- `bannerkings.politics_set_ca <kingdom> | <0-4>` — set Crown Authority
  (clamped to the government's legal band).
- `bannerkings.politics_set_tension <kingdom> | <0-100>` — set every
  interest group's tension; at 100 the next weekly tick forces a demand.
- `bannerkings.politics_add_transition <kingdom> | <delta>` — push
  government-transition pressure; reach 100 to force a government change.

---

## Credits & license

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

- **Bannerlord 1.4 compatibility port** — fixing all TaleWorlds API
  breakage from the 1.4 updates so the mod builds and runs again.
- **Native War Sails (NavalDLC) integration** — Nord titles, succession,
  language, three Nord seafaring lifestyles, naval-side perk effects,
  and the Nordic Thrall Law.
- **Crash hardening** — null-guards, mixin attachment fixes for NavalDLC's
  subclassed view models, the ItemRoster underflow clamp, the book-seller
  iteration fix, and other targeted stability work.
- **UI polish** — consolidated kingdom-screen tab, lifestyle picker bonus
  tooltips, Tax/Conquest aspect button rewire, several latent UI bugs fixed.
- **Shipping graph + adaptive risk weighting** — explicit graph topology
  with war/siege/banditry-aware routing.
- **Raid capture system** — village raids drop captives directly into
  the player's prisoner roster with cultural carry-over.

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

---

← [Slavery & raiding](Slavery-and-Raiding) · [Home](Home)
