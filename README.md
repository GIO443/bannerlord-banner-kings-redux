# Banner Kings — 1.3.x Fork with War Sails Integration

> **This is an unofficial fork** of [Banner Kings by R-Vaccari](https://github.com/R-Vaccari/bannerlord-banner-kings).
> The original author has been inactive for a while. This fork is maintained for personal use
> and will be taken down immediately upon request by the original author.
> All credit for the Banner Kings mod goes to R-Vaccari and the original contributors.

[![CodeFactor](https://www.codefactor.io/repository/github/r-vaccari/bannerlord-banner-kings/badge)](https://www.codefactor.io/repository/github/r-vaccari/bannerlord-banner-kings)

## What this fork adds

This fork has two goals on top of the original mod:

1. **Bannerlord v1.3.x compatibility** — The original project was last updated before the 1.3.x
   API changes. All compile errors from the TaleWorlds API changes have been fixed.

2. **War Sails (NavalDLC) Nord faction support** — The original BK has no data for the Nord
   settlements, clans, titles, or culture added by the War Sails DLC, causing null-reference
   crashes whenever the player interacts with Nord content. This fork adds native support:
   - Nord title hierarchy (kingdom, 2 duchies, 4 counties, 9 baronies) in `titles.xml`
   - WilundingElective succession for the Nord kingdom
   - Nordic language with partial Sturgian intelligibility
   - Null-guard patches to prevent crashes when BK code encounters unrecognised settlements

   **The mod works without War Sails installed** — Nord-specific code is skipped gracefully
   when the Nord culture and settlements are absent.

---

## Original description

Banner Kings is a suite of features developed for Mount & Blade: Bannerlord. The modification
focuses on adding depth to gameplay by expanding and adding layers of complexity to non-combat
related features. Inspiration is mostly drawn from games such as Crusader Kings.

Banner Kings is both a mod and a modding framework — it can be sub-modded by other mods,
making use of its base systems such as Languages, BK Troop Spawn system, Titles, Books,
and Religions. Note that sub-mods target the original release and are not supported by this fork.

## Compatibility

**No sub-mods are supported.** This fork is not tested or compatible with any Banner Kings
sub-mods, including the official
[Banner Kings: Cultures Expanded](https://github.com/R-Vaccari/BannerKings.CulturesExpanded).
Those sub-mods target the original BK release and will likely break or crash on this fork.

## Installation

See the [original wiki](https://github.com/R-Vaccari/bannerlord-banner-kings/wiki/Installation).

## Build

```bash
BANNERLORD_GAME_DIR="C:/Program Files (x86)/Steam/steamapps/common/Mount & Blade II Bannerlord" \
  dotnet build BannerKings/BannerKings.csproj -c Release
```

## Bug Reporting

For issues specific to this fork (1.3.x compatibility, Nord/War Sails integration), open an
issue on this repository.

For issues with the original Banner Kings mod, use the
[original repository](https://github.com/R-Vaccari/bannerlord-banner-kings) or the
[Discord](https://discord.gg/z7DS5R46wC).
