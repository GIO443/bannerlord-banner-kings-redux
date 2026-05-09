# Installing

← [Home](Home)

## Where to download

[github.com/GIO443/bannerlord-banner-kings-redux/releases/latest](https://github.com/GIO443/bannerlord-banner-kings-redux/releases/latest)
— click the most recent release, expand *Assets*, and download the zip.
GitHub Releases is currently the authoritative source while the Nexus
page is hidden.

## Requirements

- **Mount & Blade II: Bannerlord v1.3.x** (build 110062 or later)
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

## Steps

1. Install the four required mods above.
2. **Remove any existing `Modules/BannerKings/` folder**, if present from a
   previous install of the original BK. Redux is a separate module and saves
   are not interchangeable. Pick one.
3. Drop the contents of the release zip into your Bannerlord install. You
   should end up with
   `…/Mount & Blade II Bannerlord/Modules/BannerKings.Redux/`.
4. Enable **Banner Kings — Redux** in the launcher and place it after the
   four required dependencies.
5. **Start a fresh save.** Saves from the original BK will not load on Redux.

## Sub-mod compatibility

**Sub-mods built against the original Banner Kings are not supported.** This
specifically includes Cultures Expanded and any mod that derives from the
original BK release. They will likely crash or behave incorrectly on Redux.

For mods that *do* coexist with BK Redux (Diplomacy, Improved Garrisons,
Recruit Everywhere, MarryAnyone, BLAV, RBM, AI Influence) and the
recommended load order, see [Troubleshooting & compatibility](Troubleshooting#mod-compatibility).

## Recommended load order

The launcher will sort this automatically if you've enabled all of them:

```
Harmony → ButterLib → UIExtenderEx → MCM
Native → SandBoxCore → SandBox → StoryMode → CustomBattle
NavalDLC (if installed)
Banner Kings — Redux
Diplomacy / Improved Garrisons / Recruit Everywhere / MarryAnyone /
Buy Land at Villages / RBMCombat / etc.
Bannerlord Tweaks / cosmetic mods / etc.
```

---

← [Home](Home) · [Getting started →](Getting-Started)
