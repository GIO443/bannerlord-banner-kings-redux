# BannerKings — 1.3.x Fork with War Sails Integration

## What this project is
A fork of the BannerKings mod for Mount & Blade II: Bannerlord, with two goals:

1. **Port to Bannerlord v1.3.x** (build 110062+) — the original project was abandoned
   before 1.3.x support was added. Fix all compile errors from TaleWorlds API changes.

2. **Integrate War Sails (NavalDLC) Nord faction support directly** — BK has no data
   for Nord settlements, clans, titles, or culture, causing null-ref crashes whenever
   the player interacts with Nord content. Add this support natively into BK rather
   than as a separate sub-mod.

The end product is one mod, not two.

## Project layout
```
bannerlord-banner-kings/
├── CLAUDE.md
├── BannerKings/              ← BK source — all edits go here
│   ├── BannerKings.csproj
│   ├── Behaviours/
│   ├── Models/Vanilla/
│   ├── Patches/
│   ├── UI/
│   ├── Managers/
│   └── _Module/ModuleData/  ← titles.xml, nord titles, etc.
└── ref/                     ← local DLL references + warsails XMLs (gitignored)
```

## Build
```bash
BANNERLORD_GAME_DIR="C:/Program Files (x86)/Steam/steamapps/common/Mount & Blade II Bannerlord" \
  dotnet build BannerKings/BannerKings.csproj -c Release
```

The csproj resolves all game DLL references via `$(BANNERLORD_GAME_DIR)`. Output
goes directly to the game's BannerKings Modules folder.

## Status

Both phases are complete. The build is clean (0 errors).

- **Phase 1** — All 1.3.x compile errors fixed.
- **Phase 2** — Nord/War Sails integrated: titles in `_Module/ModuleData/titles.xml`,
  succession in `DefaultSuccessions.cs`, Nordic language in `DefaultLanguages.cs`,
  null-guard patches in `Patches/NordCompatPatches.cs`. Works with and without War Sails.

## Phase 1 error groups (historical reference)

### Group 1 — Removed UI types
`ImageIdentifierVM` and `CharacterVM` removed/moved in 1.3.x. Also `CharacterCreation`
and `InventoryManager` in UIManager.cs. Find replacements in the 1.3.x game DLLs.

Files:
- `UI/VanillaTabs/TownManagement/MaterialItemVM.cs`
- `UI/VanillaTabs/Kingdoms/Groups/GroupItemVM.cs`
- `UI/VanillaTabs/Character/Religion/ReligionVM.cs`
- `UI/Titles/DemesneHierarchyVM.cs`
- `UI/Estates/EstateVM.cs`
- `UI/Crafting/ExtraMaterialItemVM.cs`
- `UI/Crafting/ArmorItemVM.cs`
- `UI/Cultures/CultureTabVM.cs`
- `UI/Court/CourtVM.cs`
- `UI/CampaignStart/ReligionStartOptionVM.cs`
- `UI/UIManager.cs`

### Group 2 — Model method signature changes
Base class method signatures changed in 1.3.x. Update overrides to match.

Files and broken methods:
- `Models/Vanilla/BKVillageProductionModel.cs` — `CalculateDailyProductionAmount` return type
- `Models/Vanilla/BKRaidModel.cs` — `CalculateHitDamage` return type
- `Models/Vanilla/BKPartyWageModel.cs` — `GetTroopRecruitmentCost` return type, `GetTotalWage` missing
- `Models/Vanilla/BKPartyLimitModel.cs` — `GetTierPartySizeEffect` missing
- `Models/Vanilla/BKPartyImpairmentModel.cs` — `GetDisorganizedStateDuration` return type
- `Models/Vanilla/BKPartyHealingModel.cs` — `GetDailyHealingHpForHeroes`, `GetDailyHealingForRegulars` missing
- `Models/Vanilla/BKMilitiaModel.cs` — `CalculateEliteMilitiaSpawnChance` missing
- `Models/Vanilla/BKLearningModel.cs` — `CalculateLearningRate`, `GetSkillsDerivedFromTraits` missing
- `Models/Vanilla/BKInventoryCapacityModel.cs` — `CalculateInventoryCapacity` missing
- `Models/Vanilla/BKGarrisonModel.cs` — `CalculateGarrisonChange` missing
- `Models/Vanilla/BKDiplomacyModel.cs` — `GetScoreOfDeclaringPeace/War` missing
- `Models/Vanilla/BKCombatXpModel.cs` — `GetXpFromHit` missing
- `Models/Vanilla/BKBattleSimulationModel.cs` — `SimulateHit` missing
- `Models/Vanilla/BKTargetScoreModel.cs` — `GetTargetScoreForFaction`, `CalculatePatrollingScoreForSettlement` missing
- `Models/Vanilla/BKAgentDamageModel.cs` — verify
- `Models/Vanilla/BKArmyManagementModel.cs` — verify
- `Models/Vanilla/BKBanditModel.cs` — verify

### Group 3 — Removed API members
- `Hero.CanHaveQuestsOrIssues` — `Patches.cs:352`
- `InventoryManager` class — `Patches/EconomyPatches.cs:546`
- `EmpireFoundedScene.GetSceneNotificationCharacters()` return type — `UI/Cutscenes/EmpireFoundedScene.cs`

### Group 4 — Component and behaviour errors
- `Components/BanditHeroComponent.cs`
- `Components/EstateComponent.cs`
- `Components/FreeCompanyComponent.cs`
- `Components/GarrisonPartyComponent.cs`
- `Components/MilitiaComponent.cs`
- `Components/PopulationPartyComponent.cs`
- `Components/RetinueComponent.cs`
- `Behaviours/BKClanBehavior.cs`
- `Behaviours/BKSettlementActions.cs`
- `CampaignContent/Traits/TraitEffect.cs`
- `Managers/Education/EducationData.cs`
- `Managers/Kingdoms/Contract/BKDemesneLawDecision.cs`
- `Managers/Kingdoms/Council/BKCouncilPositionDecision.cs`
- `Managers/Kingdoms/Peerage/PeerageKingdomDecision.cs`
- `Managers/Titles/Governments/BKContractChangeDecision.cs`

## Fix strategy
1. **Missing overrides**: remove the method if the base class no longer has it, or
   find the new name. Never leave a dead override.
2. **Return type mismatches**: update return type and all return statements.
3. **Removed types**: grep the 1.3.x game DLLs or use ILSpy/dotPeek to find replacements.
4. **Removed members**: remove the call or find the 1.3.x equivalent.

## Finding 1.3.x API replacements
```bash
# Search installed DLL text for a type or method name
grep -rl "ImageIdentifier" \
  "C:/Program Files (x86)/Steam/steamapps/common/Mount & Blade II Bannerlord/bin/Win64_Shipping_Client/"
```

## Rules
- Only edit files inside `BannerKings/`
- Never edit `ref/warsails/` — reference only
- Do not add new gameplay features — compatibility and Nord integration only
- Compile after each group of fixes before moving to the next
- When a vanilla override no longer exists in the base class, remove it entirely

## Mod compatibility layer

`BannerKings/Utils/ModCompat.cs` is the central detection helper for other
installed mods. It tries `TaleWorlds.ModuleManager.ModuleInfo.GetModules()`
via reflection, falls back to `AppDomain.CurrentDomain.GetAssemblies()`,
and caches results.

When BK overlaps with another mod's domain, the rule is **acquiesce** — BK
yields the user-facing surface and keeps its internal state for downstream
features (titles, claims, dowries, etc.).

Current per-mod skip points:

| Mod | What BK skips |
|---|---|
| Diplomacy | `BKDiplomacyModel` registration; `KingdomDiplomacyVM` `CalculateWarSupport` / `GetIsProposingWarEnabledWithReason` / `OnDeclareWar` patches; `KingdomDecisionProposalBehavior::ConsiderWar` prefix |
| ImprovedGarrisons | `BKGarrisonModel` registration; `ClanVariablesCampaignBehavior::UpdateClanSettlementAutoRecruitment` prefix |
| RecruitEverywhere | `RecruitmentCampaignBehavior::RecruitVolunteersFromNotable` prefix; `HeroHelper::GetVolunteerTroopsOfHeroForRecruitment` prefix |
| MarryAnyone | `BKMarriageModel` registration |
| BuyLandAtVillages | (documented overlap only; estates and BLAV land coexist) |
| RealisticBattleMod | (no skip; load order via SubModule.xml) |

`SubModule.xml` declares `LoadAfterThis optional="true"` for all six so BK
runs detection before the cooperator's patches register.

Recipe to add a new shim:
1. Add module id + assembly name constants in `ModCompat.cs`.
2. Add a convenience property.
3. `if (ModCompat.MyMod) return true;` at the prefix entry of the competing
   patch, or wrap the `AddModel`/`AddBehavior` call in `Main.cs`.
4. Add `<DependedModuleMetadata id="MyMod" order="LoadAfterThis" optional="true" />`.
5. Document in `docs/WIKI.md` §21.

## Documentation

`docs/WIKI.md` is the project reference. Sections 1–12 are code architecture,
13–20 player-facing, 21 mod compatibility. Update it when major systems
change.
