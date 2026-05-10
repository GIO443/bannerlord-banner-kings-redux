# Banner Kings — Redux Player Wiki

Welcome. This is the player-facing handbook for **Banner Kings — Redux**, a maintenance fork of R-Vaccari's Banner Kings updated for **Bannerlord v1.3.x** with native **War Sails (NavalDLC)** Nord faction integration and the seven culture faiths seeded for the religion system. If you're looking for code internals and developer documentation, those live next to the code in the GitHub repository — these wiki pages are intentionally written for players.

> Banner Kings is the work of R-Vaccari and the original Banner Kings contributors. This fork is a community maintenance effort while the upstream project is dormant. All credit for the design, content, and core systems belongs to the original author. See [Credits & license](Troubleshooting#credits--license).

---

## 📥 Download

The release zip lives on the GitHub Releases page:

### **➡️ [github.com/GIO443/bannerlord-banner-kings-redux/releases/latest](https://github.com/GIO443/bannerlord-banner-kings-redux/releases/latest)**

That's the authoritative download. The Nexus page is currently hidden while attribution and licensing details are sorted with the original author. Extract the zip into your Bannerlord install per [Installing](Installing).

Releases are tagged on the `1.8.x` line; the latest tag is the recommended build. Older `1.5.x` / `1.6.x` tags remain available on the page if you specifically need to roll back, but no fixes are backported to them.

---

## What is Banner Kings — Redux

Banner Kings is a deep simulation overlay on top of Bannerlord's Campaign. Where vanilla treats settlements as resource nodes and clans as hero bags, BK adds:

- **Population simulation** — every settlement has serfs, slaves, craftsmen, nobles. Classes grow and shrink based on policies, food, raids, and laws.
- **Feudal titles** — a hierarchy of Empires → Kingdoms → Duchies → Counties → Baronies → Lordships, each with deeds, claimants, succession rules, and contracts.
- **Religion** *(new in v1.8.9.0)* — seven natural faiths (one per culture), with divinities, doctrines, clergy, blessings, rites, induction, holy wars, and a piety counter on the campaign HUD. Master kill switch in MCM if you'd rather skip the system.
- **Education** — heroes have languages, books, scholarship, and lifestyles (skill-line specializations that grant escalating perks).
- **Estates** — clan-owned, hero-managed land within villages that produces income and food and can be inherited or sold.
- **Council & courts** — clans and kingdoms have appointed officers (Marshal, Steward, Chancellor, Spymaster, Court Physician) with real effects on recruitment, taxes, diplomacy, and hero recovery.
- **Mercenary contracts, criminality, gentry, knighthood** — many smaller systems woven through the campaign loop.

This **Redux fork** brings the mod current with Bannerlord v1.3.x and adds a layer of native and compat work on top:

- **War Sails (NavalDLC) Nord faction** — full title hierarchy (kingdom → 2 duchies → 4 counties → 9 baronies), three seafaring lifestyles (**Jomsviking**, **Drakkar Captain**, **Sjofarandi**) with real perks that affect naval combat, party speed at sea, and spotting, plus the culture-specific **Nordic Thrall Law** that biases the Nord economy toward raid-based slave trade. Crash hardening so the mod runs cleanly with War Sails *or* with War Sails uninstalled.
- **Religion seeded** — upstream had the religion machinery scaffolded but unpopulated; v1.8.9.0 added the seven natural faiths (Empire's Darusosian Path, Vlandia's Canticles of Caïon, Battania's Amra Druidh, Aserai's Path of Akhmar, Khuzait's Six Winds, Sturgia's Old Gods of the North, and the War Sails Nord *Osfeydian Tradition*). Heroes are auto-assigned, preachers spawn, doctrines fire, blessings work end-to-end. See [Player guide → Religion](Player-Guide#religion).
- **Economy Overhaul Framework (EOF) compatibility** — when EOF is loaded, BK yields prosperity, loyalty, food, and workshops to EOF and pauses the BK estate loop. All other BK feudal mechanics keep running.
- **Naval shipping & adaptive risk weighting** — caravans and lord parties cross water via a graph-driven shipping system; war, siege, and banditry adjust routes and freight prices in real time.
- **Raid capture** — village raids produce actual captives (not just gold and disabled production), with culture-specific dispositions. Toggleable in MCM.
- **MCM kill switches** for the religion system and the shipping rescue paths, in case either causes issues with your specific mod stack.

---

## Pages on this wiki

- **[Installing](Installing)** — requirements, install steps, sub-mod compat warnings.
- **[Getting started](Getting-Started)** — what's in the mod (high-level), the first 30 minutes, glossary of terms that come up constantly.
- **[Systems reference](Systems-Reference)** — lifestyles, demesne laws, per-settlement policies, faiths. Tables you look up while playing.
- **[Player guide](Player-Guide)** — step-by-step "how do I…" recipes plus the per-system FAQ for population, titles, religion, education, diplomacy, mercenaries.
- **[Economy](Economy)** — village classes, town industries, estate specializations (incl. Growth investment mode), cluster fit, stagnation, food caravans, AI estate-policy ladder. EOF caveat banner at the top.
- **[Shipping & trade](Shipping-and-Trade)** — caravan auto-board, AI shipping, adaptive risk weighting, freight pricing, console cheats for testing routes.
- **[Slavery & raiding](Slavery-and-Raiding)** — the Nord raid economy, slave caravans, the raid capture system (toggles, captives, dispositions, foreign-merc skim), cheats and logging.
- **[Troubleshooting & compatibility](Troubleshooting)** — edge cases, mod compatibility table, save-game safety, MCM kill switches, how to file a useful bug report, credits & license.

If you're new to BK, read **Installing → Getting started → Player guide** in that order. The other pages are reference material you visit when you need a specific answer.

---

## Hitting issues?

The 1.8.x line is the actively-maintained release line. Most reported issues fall into one of three buckets:

- **A crash on load or campaign tick.** Install [Better Exception Window](Installing) before you play a save you care about. With BEW installed, a crash produces an HTML report with the full stack, inner exception, loaded mods, and harmony patches — that's what we need to debug. Open an [issue](https://github.com/GIO443/bannerlord-banner-kings-redux/issues) and attach the HTML.
- **Religion-related weirdness.** The religion system was seeded in v1.8.9.0; subsequent hotfixes addressed save/load contracts, bucket-desyncs, and a few enemy-castle teleport edge cases. If you're hitting issues that stop you playing, the **MCM → Performance → Enable Religion System** kill switch lets you keep the rest of BK while skipping the religion surface entirely. See [Troubleshooting](Troubleshooting#how-do-i-disable-the-religion-system-entirely) for what flips when you toggle it.
- **Caravans / boats walking on land, lords stuck on coastal tiles.** BK has rescue passes that try to unstick stranded parties, but they've occasionally produced their own teleport-style bugs (sieging armies pulled out of siege state, parties teleported into enemy fiefs). The **MCM → Economy → Enable Shipping Rescues** toggle defaults **OFF** in v1.8.9.10+ — flip it on if you specifically want the unsticking behaviour back.
