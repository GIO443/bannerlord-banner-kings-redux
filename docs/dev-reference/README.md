# Dev reference (internal)

API cache for Bannerlord build 1.3.x (build 110062+) decompiled from the
local install. **Not** the player wiki — this is for development.

## Files

- [campaign-behaviours.md](campaign-behaviours.md) — every vanilla
  `CampaignBehaviorBase` BK patches, replaces, or queries: class, events
  it subscribes to, key methods, BK touch points.
- [naval-api.md](naval-api.md) — sailing / ship / port / storm
  campaign-layer API. `MobileParty` sail state, `MapDistanceModel`,
  ship events, `NavalDLC` behaviours, port state.

## Source

Decompiled with `ilspycmd 10.0.0` against:

- `bin/Win64_Shipping_Client/TaleWorlds.CampaignSystem.dll`
- `Modules/SandBox/bin/.../SandBox.dll`
- `Modules/NavalDLC/bin/.../NavalDLC.dll`

Working copies of the decompiled `.cs` files live under `/tmp/bk-decomp/`
(behaviors, naval, sailing) — regenerate by re-running ilspycmd against
the install path.
