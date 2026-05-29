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

## Sub-mods

**Sub-mods built against the original Banner Kings are not supported.** This
specifically includes Cultures Expanded and any mod that derives from the
original BK release. They will likely crash or behave incorrectly on Redux.

## Other mods

No guarantees in either direction. Mods that touch the same systems BK
touches (economy, garrisons, recruitment, diplomacy, workshops, etc.)
may or may not coexist cleanly with Banner Kings on any given combination
of versions. We can't keep a current compatibility matrix and don't try —
the moving target of other mods' update schedules makes any list stale
within weeks.

Try the combo you want. If something is clearly broken, file an issue
with both versions named. If you can't tell which mod is at fault, BK's
[Troubleshooting](Troubleshooting) page has a triage approach (look at
crash logs, isolate by disabling halves of the load order).

## Recommended load order

The launcher will sort this automatically if you've enabled all of them:

```
Harmony → ButterLib → UIExtenderEx → MCM
Native → SandBoxCore → SandBox → StoryMode → CustomBattle
NavalDLC (if installed)
Bannerlord Living Economy
Banner Kings — Redux
Other gameplay mods
Bannerlord Tweaks / cosmetic mods / etc.
```

---

← [Home](Home) · [Getting started →](Getting-Started)
