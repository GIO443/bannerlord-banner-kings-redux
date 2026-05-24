# Installing

← [Home](Home)

## Where to download

[github.com/GIO443/bannerlord-banner-kings-redux/releases/latest](https://github.com/GIO443/bannerlord-banner-kings-redux/releases/latest)
— click the most recent release, expand *Assets*, and download the zip.
GitHub Releases is currently the authoritative source while the Nexus
page is hidden.

## Requirements

- **Mount & Blade II: Bannerlord v1.4** (built against v1.4.5).
- **Bannerlord Living Economy** — **required.** As of v1.9.0.0 this is a
  hard dependency; BK will not load without it. See the note below.
  [Download on Nexus](https://www.nexusmods.com/mountandblade2bannerlord/mods/10796).
- **Harmony** — `Bannerlord.Harmony`
- **ButterLib** — `Bannerlord.ButterLib`
- **UIExtenderEx** — `Bannerlord.UIExtenderEx`
- **MCM (Mod Configuration Menu)** — `Bannerlord.MBOptionScreen`
- *Optional:* **War Sails (NavalDLC)** — TaleWorlds DLC. If installed, the
  Nord title hierarchy, seafaring lifestyles, and Nordic Thrall Law activate.
  The mod runs fine without it.
- *Strongly recommended:* **Better Exception Window**
  ([Nexus link](https://www.nexusmods.com/mountandblade2bannerlord/mods/404)).
  Replaces vanilla Bannerlord's terse crash dialog with a detailed HTML
  crash report (full stack, inner exception, loaded modules, harmony
  patches). Without it, any crash you hit gives us nothing to debug
  from. Install it before you start a save you care about.

### Why Bannerlord Living Economy is required

From v1.9.0.0 onward, Banner Kings runs its **population and estate**
systems on top of **Bannerlord Living Economy** rather than its own
standalone economy. Settlements use Living Economy's seven social classes,
and BK estates anchor onto its estate parcels — one coherent economy
instead of two simulations fighting each other.

It is a **separate mod download** — BK does not bundle it. Install and
enable it like any other dependency. If you launch with Banner Kings
enabled but Living Economy missing, BK will fail to load.

## Steps

1. Install the required mods above — **including Bannerlord Living Economy**.
2. **Remove any existing `Modules/BannerKings/` folder**, if present from a
   previous install of the original BK. Redux is a separate module and saves
   are not interchangeable. Pick one.
3. Drop the contents of the release zip into your Bannerlord install. You
   should end up with
   `…/Mount & Blade II Bannerlord/Modules/BannerKings.Redux/`.
4. Enable **Banner Kings — Redux** in the launcher and place it after the
   four required library mods **and after Bannerlord Living Economy**.
5. **Start a fresh save** if you are coming from the original BK — those
   saves will not load on Redux. Upgrading *within* Redux across the
   v1.9.0.0 economy integration is handled by a one-time migration that
   carries your existing BK population into Living Economy on first load;
   back the save up first, as always.

## Sub-mod compatibility

**Sub-mods built against the original Banner Kings are not supported.** This
specifically includes Cultures Expanded and any mod that derives from the
original BK release. They will likely crash or behave incorrectly on Redux.

For other mods, BK detects common companions at startup and yields its
overlapping features automatically — no configuration needed. The
authoritative source on what compat shims are wired in is
[`BannerKings/Utils/ModCompat.cs`](https://github.com/GIO443/bannerlord-banner-kings-redux/blob/main/BannerKings/Utils/ModCompat.cs).
If a specific mod isn't behaving as expected with BK loaded, file an
issue and we'll add or adjust a shim — but assume it works until you
have a concrete report.

### Total conversions (Shokuho, Realm of Thrones, etc.)

Total-conversion mods replace Calradia with a different setting and ship
their own GameType (Shokuho uses `ShokuhoCampaign`, ROT uses its own
campaign mode). BK's campaign behaviors register against the vanilla
`Campaign` / `CampaignStoryMode` GameTypes and **do not auto-load into a
total-conversion campaign**, but individual BK content systems that
read from the cross-module `ModuleData/BKData/*.xml` registry (faiths,
religions, divinities, faith groups today; titles, lifestyles,
governments later) load **regardless of GameType** — so BK can ship
total-conversion-specific content data alongside its Calradian data,
and the loader's culture-binding silently drops the wrong-setting rows.

**Currently shipped Shokuho content:**

- **All six Shokuho religions** Shokuho ships in `ModuleData/religions.xml`
  (Shintō, Rinzai, Sōtō, Shingon, Tendai, Jōdo Shinshū) are seeded into
  BK's faith system with their own pantheons (Amaterasu, Hachiman, Inari,
  Susanoo, Tsukuyomi, Shakyamuni, Amida, Dainichi, Bodhidharma, Kannon,
  Fudō Myō-ō), faith groups (shrine/sangha hierarchies), doctrines, and
  clergy rank titles (Kannushi, Rōshi, Zenji, Daiajari, Zasu, Monshu).
  Cultures are bound to historically appropriate sects — Sōtō to the
  Hokuriku snows of Eiheiji, Shingon to Kinai and Nankai (Mt. Kōya), and
  so on. See [Systems-Reference → Faiths](Systems-Reference#faiths) for
  the full table.
- **Three Sengoku government types** — Shogunate, Daimyō Realm, and
  Ikkō League — selectable through the standard BK government-
  transition mechanic, with three matching succession laws (Shogunal
  Hereditary, Daimyō Elective, Ikkō Confederation) that reuse BK's
  Hereditary / Feudal Elective / Theocratic Elective C# algorithms
  with Japanese names, descriptions, and culture-ideal bindings. See
  [Systems-Reference → Shokuho governments](Systems-Reference#shokuho-governments-and-successions)
  for the table. **Not yet auto-assigned**: Shokuho kingdoms still
  start under whichever vanilla government the BK C# fallback picks;
  the player adopts the appropriate Japanese form via the in-game
  political transition. Auto-binding would need a C# extension to
  `GetKingdomIdealGovernment`.

**Not yet shipped for Shokuho:**

- **Titles** — the `TitleGenerator` loader currently reads only BK's
  single `titles.xml` file and binds settlements/factions by exact
  StringId, so Shokuho titles can't be added as a separate XML drop
  without first extending the loader. The C# change is small but
  touches save-retrofit logic and needs its own careful pass.
- **Lifestyles** — XML loader is in place, but each lifestyle entry
  must bind to existing `LifestyleCataphractEquites`-style perk
  classes shipped in BK's C# code. Authoring Sengoku-fitting
  lifestyles (samurai, monk, merchant, ji-samurai, ashigaru) needs
  matching C# perk classes, not just XML.
- **Demesne laws, council positions, estates, custom troops** — these
  systems are hard-coded C# in BK and have no XML loader at all,
  so a port for any total conversion requires new C# code for each.

BK and the conversion can be **safely enabled at the same time**: starting
the conversion's own campaign now gives you Shokuho **plus** BK's
religion system on Shokuho cultures; starting a vanilla Campaign gives
you Calradia + BK without any Shokuho content (the Shokuho rows
silently drop because Shokuho cultures aren't registered). If you see
a crash specifically while loading either campaign with both mods
enabled, file an issue — that points to a BK init path that needs
gating via `ModCompat.Shokuho` / `ModCompat.RealmOfThrones`.

## Recommended load order

The launcher will sort this automatically if you've enabled all of them:

```
Harmony → ButterLib → UIExtenderEx → MCM
Native → SandBoxCore → SandBox → StoryMode → CustomBattle
NavalDLC (if installed)
Bannerlord Living Economy
Banner Kings — Redux
Diplomacy / Improved Garrisons / Recruit Everywhere / MarryAnyone /
Buy Land at Villages / RBMCombat / etc.
Bannerlord Tweaks / cosmetic mods / etc.
```

---

← [Home](Home) · [Getting started →](Getting-Started)
