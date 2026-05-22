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
- **"Crash on entering a Nord settlement"** — only on pre-fix or
  non-Redux builds. Update to the latest Banner Kings — Redux release;
  the Nord null-guards are bundled.
- **"Crash hovering parties in the Army Management screen"** — fixed.
  Caused by BK's mercenary eligibility tweak leaving the hover tooltip's
  reason text null. Update to a current Banner Kings — Redux build.
- **"My army disbands far too soon"** — fixed. BK's
  cohesion postfix was clamping the daily change at a forced loss,
  blocking every vanilla recovery condition (camped at home, food,
  leader perks). The MCM "Army Cohesion Boost" slider now actually
  matches its tooltip: at 50% it halves daily cohesion loss; at 0% it
  matches vanilla.
- **"My lifestyle locked at Scholar"** — Scholar requires the scholarship
  gate (any of ScholarshipMechanic, Accountant, NaturalScientist, Treasurer).
  Without it, progress doesn't tick.
- **"Council Marshal didn't reduce wages"** — the reduction is
  multiplicative; other modifiers (custom troop, mercenary status) can
  dominate. Check the wage tooltip breakdown in the party UI.
- **"Estate showing zero income"** — check the visit panel for an
  **Income Blocked** reason (war with the village's faction, BK title
  manager not loaded, owner→estate registry desync). See the full
  recipe under [Player guide → Estates](Player-Guide#estates). On older
  builds, upgrade — current builds added a backstop payout that fixes
  the silent ImprovedGarrisons-replaces-finance-model case.
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
- **"How do I use the Religion / Theology system?"** As of v1.8.9.0
  the seven culture faiths (Darusosian, Canticles, Amra, Asera, Six
  Winds, Treelore, Osfeyd) are seeded and functional — heroes get a
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
