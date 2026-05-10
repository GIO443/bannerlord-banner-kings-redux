# Banner Kings — Redux

> **Module Id:** `BannerKings.Redux` &nbsp;·&nbsp; **Folder:** `Modules/BannerKings.Redux/` &nbsp;·&nbsp; **Version:** see `_Module/SubModule.xml`

A maintenance fork of [R-Vaccari's Banner Kings](https://github.com/R-Vaccari/bannerlord-banner-kings), updated for **Bannerlord v1.3.x** with native **War Sails (NavalDLC)** Nord faction support and ongoing crash-hardening work.

[![CodeFactor](https://www.codefactor.io/repository/github/gio443/bannerlord-banner-kings-redux/badge/main)](https://www.codefactor.io/repository/github/gio443/bannerlord-banner-kings-redux/overview/main)

## 📥 Download

Get the latest release from the **[Releases page](https://github.com/GIO443/bannerlord-banner-kings-redux/releases/latest)**.

Releases tag on the `1.8.x` line; the latest tag is the recommended build. Older `1.5.x` / `1.6.x` tags remain on the page if you specifically need to roll back, but no fixes are backported to them.

The Nexus page is currently hidden while attribution and licensing details are sorted with the original author. GitHub Releases is the authoritative download in the meantime.

> **This is an unofficial fork.** All credit for the original Banner Kings design, content, and core systems belongs to R-Vaccari and the original contributors. The fork is maintained while upstream is dormant and will be taken down immediately upon request by the original author.

## Quick install

1. Install the four required dependencies: **Harmony**, **ButterLib**, **UIExtenderEx**, **MCM**. Strongly recommended: **[Better Exception Window](https://www.nexusmods.com/mountandblade2bannerlord/mods/404)** so any crash you hit produces a useful HTML report.
2. **Remove any existing `Modules/BannerKings/` folder.** Redux is a separate module under `Modules/BannerKings.Redux/` and saves do not transfer between the two — pick one.
3. Drop the release zip into your Bannerlord install. You should end up with `…/Mount & Blade II Bannerlord/Modules/BannerKings.Redux/_Module/SubModule.xml`.
4. Enable **Banner Kings — Redux** in the launcher (after the four dependencies). Start a fresh save.

Full install instructions, sub-mod compat warnings, and a player-facing wiki are at the [GitHub wiki](https://github.com/GIO443/bannerlord-banner-kings-redux/wiki) — start with **[Installing](https://github.com/GIO443/bannerlord-banner-kings-redux/wiki/Installing)** then **[Getting started](https://github.com/GIO443/bannerlord-banner-kings-redux/wiki/Getting-Started)**.

## What Redux adds on top of the original

1. **Bannerlord v1.3.x compatibility.** Upstream BK was last updated before the 1.3.x API changes. Every compile error from the TaleWorlds API churn has been fixed; the Redux DLL builds and runs against current `bin/Win64_Shipping_Client/`.

2. **Native War Sails (NavalDLC) Nord faction support.** Upstream had no data for the Nord faction added by War Sails — touching any Nord settlement crashed BK with NREs. Redux adds:
   - A full Nord title hierarchy (kingdom → 2 duchies → 4 counties → 9 baronies) in `titles.xml`.
   - `WilundingElective` succession law for the Nord kingdom.
   - Nordic language with partial Sturgian intelligibility.
   - Three Nord-only seafaring lifestyles — **Jomsviking**, **Drakkar Captain**, **Sjofarandi** — with real perks affecting naval combat, party speed at sea, and spotting.
   - The **Nordic Thrall Law**, a culture-specific demesne law that biases the Nord economy toward raid-based slave trade.
   - Null-guard patches throughout BK so the mod runs cleanly with War Sails *or* with War Sails uninstalled.

3. **Religion system seeded.** Upstream's religion machinery was scaffolded but unpopulated — the seven culture faiths, divinities, doctrines, rites, and clergy templates exist as code but no faith was ever instantiated. Redux v1.8.9.0 seeds the seven natural faiths (Empire's Darusosian Path, Vlandia's Canticles of Caïon, Battania's Amra Druidh, Aserai's Path of Akhmar, Khuzait's Six Winds, Sturgia's Old Gods of the North, and a War Sails Nord *Osfeydian Tradition*) so heroes are assigned faiths automatically, preachers spawn at settlements, and dialogue / blessings / rites / induction work end to end. Master kill switch in `MCM → Performance → Enable Religion System` if you'd rather skip the system entirely.

4. **Economy Overhaul Framework (EOF) compatibility.** When EOF is loaded, BK yields its village/town economy systems (prosperity, loyalty, food, workshops) to EOF and pauses the BK estate gameplay loop. EOF's decorator pattern wraps BK's `ClanFinanceModel`, `PriceFactor`, `Construction`, `Tax`, `Economy`, and `VillageProduction` models cleanly. All other BK feudal mechanics (titles, claims, knighthood, retainer, tax-by-class, religion, education, lifestyles, caravans, shipping) keep running unchanged.

5. **Naval shipping & raid capture.** Graph-driven cross-continent caravan and lord shipping with adaptive risk weighting (war, siege, banditry adjust routes and freight prices in real time). Raid capture system that turns village raids into actual captives instead of nothing — toggleable in MCM.

6. **Crash-hardening sweeps.** A long list of null-guards, race-condition fixes, save-deserialization backstops, and harmless-fallback patches across the BK surface. The 1.8.x line specifically focused on save/load contract bugs around the new religion system; ship state is now stable.

**Sub-mods built against the original Banner Kings are not supported.** Cultures Expanded and any mod that derives from upstream BK will likely crash or behave incorrectly on Redux.

## Build

```bash
BANNERLORD_GAME_DIR="C:/Program Files (x86)/Steam/steamapps/common/Mount & Blade II Bannerlord" \
  dotnet build BannerKings/BannerKings.csproj -c Release
```

The csproj resolves all game DLL references via `$(BANNERLORD_GAME_DIR)`. Build output is copied directly to the game's `Modules/BannerKings.Redux/` folder.

## Bug reporting

Open an [issue on this repository](https://github.com/GIO443/bannerlord-banner-kings-redux/issues) for bugs in this fork.

A useful bug report includes the **Better Exception Window** crash HTML (full stack, inner exception, loaded modules, harmony patches), a description of what you were doing when it crashed, and your full mod load order. Without the HTML, most reports come down to "the game crashed" — installable from [Nexus](https://www.nexusmods.com/mountandblade2bannerlord/mods/404) before you start a save you care about.

For issues with the **original** Banner Kings mod, use the [upstream repository](https://github.com/R-Vaccari/bannerlord-banner-kings).

## License & credits

Original Banner Kings: R-Vaccari and contributors — all design, content, and core systems credit.

Redux fork: a community maintenance effort under the same license as upstream. The fork exists to keep the mod current with Bannerlord patches while upstream is dormant; it will be retired immediately if upstream resumes development or upon request by the original author.
