# Banner Kings — Redux Player Wiki

Welcome. This is the player-facing handbook for **Banner Kings — Redux**, a
maintenance fork of R-Vaccari's Banner Kings updated for Bannerlord v1.3.x with
native War Sails (NavalDLC) integration. If you're looking for code internals
and developer documentation, those live next to the code in the GitHub
repository — these wiki pages are intentionally written for players.

> Banner Kings is the work of R-Vaccari and the original Banner Kings
> contributors. This fork is a community maintenance effort while the upstream
> project is dormant. All credit for the design, content, and core systems
> belongs to the original author. See [Credits & license](Troubleshooting#credits--license).

---

## 📥 Download

The release zip lives on the GitHub Releases page:

### **➡️ [github.com/GIO443/bannerlord-banner-kings-redux/releases/latest](https://github.com/GIO443/bannerlord-banner-kings-redux/releases/latest)**

That's the authoritative download. The Nexus page is currently hidden
while attribution and licensing details are sorted with the original
author — GitHub Releases is where you grab the build in the meantime.
Extract the zip into your Bannerlord install per [Installing](Installing).

Two parallel release lines are maintained:

| Line | What's different | Latest |
|---|---|---|
| **🟢 1.6.x** *(recommended)* | Graph-driven shipping + adaptive risk weighting; raid capture system. | [Latest 1.6.x](https://github.com/GIO443/bannerlord-banner-kings-redux/releases/latest) |
| **🔵 1.5.x** *(stable maintenance)* | Original lane-overlap shipping; pre-raid-capture flow. | [v1.5.8.0](https://github.com/GIO443/bannerlord-banner-kings-redux/releases/tag/v1.5.8.0) |

---

## What is Banner Kings — Redux

Banner Kings is a deep simulation overlay on top of Bannerlord's Campaign. Where
vanilla treats settlements as resource nodes and clans as hero bags, BK adds:

- **Population simulation** — every settlement has serfs, slaves, craftsmen,
  nobles. Classes grow and shrink based on policies, food, raids, and laws.
- **Feudal titles** — a hierarchy of Empires → Kingdoms → Duchies → Counties →
  Baronies → Lordships, each with deeds, claimants, succession rules, and
  contracts.
- **Education** — heroes have languages, books, scholarship, and lifestyles
  (skill-line specializations that grant escalating perks).
- **Estates** — clan-owned, hero-managed land within villages that produces
  income and food and can be inherited or sold.
- **Council & courts** — clans and kingdoms have appointed officers (Marshal,
  Steward, Chancellor, Spymaster, Court Physician) with real effects on
  recruitment, taxes, diplomacy, and hero recovery.
- **Mercenary contracts, criminality, gentry, knighthood** — many smaller
  systems woven through the campaign loop.

This **Redux fork** brings the mod current with Bannerlord v1.3.x and adds
native support for the **War Sails (NavalDLC)** Nord faction, including:

- A full Nord title hierarchy (kingdom → 2 duchies → 4 counties → 9 baronies)
- Three Nord seafaring lifestyles — Jomsviking, Drakkar Captain, Sjofarandi —
  with real perks that affect naval combat, party speed at sea, and spotting
- The **Nordic Thrall Law**, a culture-specific demesne law that makes the
  Nord economy lean hard into raid-based slavery and slave trade
- Crash hardening so the mod runs cleanly with or without War Sails installed

---

## Pages on this wiki

- **[Installing](Installing)** — requirements, install steps, sub-mod compat warnings.
- **[Getting started](Getting-Started)** — what's in the mod (high-level), the first 30 minutes, glossary of terms that come up constantly.
- **[Systems reference](Systems-Reference)** — lifestyles, demesne laws, per-settlement policies. Tables you look up while playing.
- **[Player guide](Player-Guide)** — step-by-step "how do I…" recipes plus the per-system FAQ for population, titles, education, diplomacy, mercenaries.
- **[Shipping & trade](Shipping-and-Trade)** — caravan auto-board, AI shipping, adaptive risk weighting, freight pricing, console cheats for testing routes.
- **[Slavery & raiding](Slavery-and-Raiding)** — the Nord raid economy, slave caravans, the 1.6.2 raid capture system (toggles, captives, dispositions, foreign-merc skim), cheats and logging.
- **[Troubleshooting & compatibility](Troubleshooting)** — edge cases, mod compatibility table, save-game safety, how to file a useful bug report, credits & license.

If you're new to BK, read **Installing → Getting started → Player guide** in that order. The other pages are reference material you visit when you need a specific answer.
