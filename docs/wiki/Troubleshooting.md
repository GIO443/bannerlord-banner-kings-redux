# Troubleshooting & compatibility

← [Home](Home)

## On this page

- [Edge cases & frequent confusions](#edge-cases--frequent-confusions)
- [Mod compatibility](#mod-compatibility)
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
- **"Crash hovering parties in the Army Management screen"** — fixed
  in v1.6.9.33. Caused by BK's mercenary eligibility tweak leaving the
  hover tooltip's reason text null. Update Banner Kings — Redux.
- **"My army disbands far too soon"** — fixed in v1.6.9.34. BK's
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
  recipe under [Player guide → Estates](Player-Guide#estates). If
  you're running an older 1.6.x build, upgrade — recent builds added
  a backstop payout that fixes the silent
  ImprovedGarrisons-replaces-finance-model case.
- **"Can't change demesne law"** — locked behind a contract-change cooldown
  (≈ 1 in-game year) and minimum loyalty / authority gates.
- **"Skills level too fast in Banner Kings"** — older builds shipped
  with the *Alternative Leveling* MCM toggle on by default, and its XP
  curve only added ~20 XP per level past level 1, so any small XP gain
  rocketed you through 10+ levels. As of v1.6.9.26 the toggle is
  **removed entirely** — every save uses vanilla's XP curve regardless
  of what value the MCM file remembers from a previous version. No
  action needed; load your save and skills will progress at vanilla
  rates.
- **"Language learning finishes instantly"** — symptom of the same
  alternate-leveling explosion (Scholarship XP racing up boosted the
  language-rate skill effect off the rails) plus an unsafe rate path.
  Per-tick fluency gain is now hard-capped at 5%, so even with the worst
  rate inputs a language can't finish in fewer than ~20 in-game days.
- **"How do I use the Religion / Theology system?"** You don't.
  Religions, faiths, piety, doctrines, preachers, and the Theology
  skill effects are **not functional** in Banner Kings — Redux. The
  upstream system was deeply broken when the 1.3.x port landed and
  has been left dormant pending a future rewrite; the encyclopedia
  pages, council Philosopher tasks, and any tooltips referencing
  faiths or piety should be treated as inert. Your character can
  level Theology as a skill, but its bonuses won't fire reliably and
  there is no working in-game way to convert a settlement, install a
  preacher, or perform a rite. If you see a religion popup, ignore
  it. If a quest references piety, the quest is stuck — abandon it.
- **"A caravan is walking visibly across open water"** — fixed in two
  layers on the 1.6.x line: a daily rescue sweep steers stranded
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
  some BK-tracked heroes can briefly end up with). Fixed in v1.6.8.1: a
  defensive prefix on that vanilla method returns a zero teleport delay
  instead of crashing. If you still see this on an older build, update to
  the latest Banner Kings — Redux release.
- **"My new game crashes during loading"** — almost always a non-BK mod's
  Harmony patch failing (e.g., GovernorsHandleIssues against newer
  Bannerlord builds). Install **Better Exception Window** if you haven't
  already (see [Reporting bugs](#reporting-bugs) below) and read the
  inner exception in the crash report — it usually names the offending
  mod by its patch method, and you can disable that mod and continue.
- **"Save load crashes with a BannerKings GetPopData NRE"** — fixed in
  v1.6.9.25. Cause: vanilla `Clan.AfterLoad` recomputes party strength
  early in the load sequence, which reaches `Town.FoodChange` →
  BK's food model → `PopulationManager.GetPopData` before BK's
  population caches are wired up, so the lookup NRE'd. The fix is a
  null-guard: BK's food calc cleanly falls back to vanilla until
  `PostInitialize` runs, then takes over. Update to v1.6.9.25 or newer.
- **"Crash entering any town with a `MountCreationKey.GetRandomMountKey`
  NRE in `MissionHelper.SpawnCows`"** — fixed in v1.6.9.25. Cause:
  BK's price-adjust pass on game create/load was calling
  `ItemObject.InitializeTradeGood` on the vanilla cow item, which is
  the wrong helper for items with a `HorseComponent` and stripped the
  cow's mount data. Vanilla town-center scenes then NRE'd trying to
  spawn cows. The fix is to leave the vanilla cow item alone; cow
  pricing falls back to vanilla. Update to v1.6.9.25 or newer.

---

## Mod compatibility

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
| **AI Influence (AI Diplomacy)** | BK yields its `InfluenceModel` to AI Influence on the vanilla GameModel slot, so the LLM-driven diplomacy / influence calculations can run cleanly. BK's internal influence queries (caps, costs for council appointments, claims, demands, knighthoods) still resolve through BK's own model so titles and claim logic continue to work. No configuration needed; detection is automatic. |

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

For the recommended load order, see [Installing → Recommended load order](Installing#recommended-load-order).

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
([Bannerlord.BetterExceptionWindow](https://www.nexusmods.com/mountandblade2bannerlord/mods/2032)).
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
  against the live 1.3.x DLLs.
- Compile warnings about obsolete types — 1.3.x deprecations not yet
  fully removed. They don't affect runtime.
- "GovernorsHandleIssues crashed" — that's a different mod failing to
  patch a method. Disable it.

Issues for this fork specifically (1.3.x compatibility, War Sails / Nord
integration, seafaring lifestyles, Nordic Thrall Law) belong on the GitHub
repo:

**https://github.com/GIO443/bannerlord-banner-kings-redux/issues**

Issues with the original Banner Kings systems (titles, estates, council,
etc. — anything also present in the original release) are upstream
BK problems. They'll get fixed in Redux as we encounter them, but the
underlying design is R-Vaccari's.

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
