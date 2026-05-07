# Campaign behaviours BK overrides

Vanilla `CampaignBehaviorBase` subclasses that BK patches, replaces, or
queries by interface. Source: 1.3.x (build 110062+) decompiled from
`TaleWorlds.CampaignSystem.dll` and `Modules/SandBox/SandBox.dll`.

Each entry lists:
- **Type** + namespace (so you can type-locate via `ilspycmd -t`)
- **Events** subscribed in `RegisterEvents()` — these define when the
  behaviour runs
- **Key methods** — public/internal API + any handler BK patches
- **Save data** — what the behaviour persists via `SyncData`
- **BK touch points** — where in BK source

The principle from CLAUDE.md applies: **BK decides, vanilla executes.**
Most touch points are Harmony prefixes that gate or override decision
logic; the actual mechanic stays in vanilla.

---

## Quick index

| Vanilla behaviour | BK touch | Why |
|---|---|---|
| `BanditSpawnCampaignBehavior` | queried | Read bandit clan/hideout state |
| `CaravanConversationsCampaignBehavior` | patched | `FindSuitableCompanionsToLeadCaravan` knighthood gate |
| `CaravansCampaignBehavior` | **removed** | BK ships its own shipping-graph caravan |
| `CharacterRelationCampaignBehavior` | patched | Bandit interactions |
| `ClanVariablesCampaignBehavior` | heavily patched | BK runs its own clan finance |
| `CompanionRolesCampaignBehavior` | patched | Knighthood / fief grant flow |
| `CompanionsCampaignBehavior` | patched | Spawn rules |
| `CraftingCampaignBehavior` | queried via `ICraftingCampaignBehavior` | Read crafting state |
| `DisbandPartyCampaignBehavior` | queried via `IDisbandPartyCampaignBehavior` | Disband progress |
| `DiplomaticBartersBehavior` | patched | Mercenary-leave gating |
| `FoodConsumptionBehavior` | patched | Patch consumption math |
| `GovernorCampaignBehavior` | patched | `DailyTickSettlement` notable spawn integration |
| `HeirSelectionCampaignBehavior` (SandBox) | patched | `OnHeirSelectionOver` title pass-down |
| `HeroSpawnCampaignBehavior` | patched | New-game / partial-followup spawn |
| `ItemConsumptionBehavior` | patched + queried | Consumption math + read state |
| `KingdomDecisionProposalBehavior` | patched | `ConsiderWar` gate |
| `LordConversationsCampaignBehavior` | heavily patched | Dialogue gates (preacher, oath, etc.) |
| `PlayerTownVisitCampaignBehavior` | patched | `town_market` consequence |
| `RansomOfferCampaignBehavior` | patched | Bandit ransom |
| `RebellionsCampaignBehavior` | queried | Read rebellion state |
| `RecruitmentCampaignBehavior` | patched | `RecruitVolunteersFromNotable` (skipped if RecruitEverywhere) |
| `RomanceCampaignBehavior` | patched | Romance/marriage gating |
| `TeleportationCampaignBehavior` | queried via `ITeleportationCampaignBehavior` | Hero arrival ETA |
| `TournamentBehavior` (SandBox) | patched | Bet caps |
| `VassalAndMercenaryOfferCampaignBehavior` | patched | Vassal/merc offer gating |
| `VillageGoodProductionCampaignBehavior` | patched | Daily village production |
| `VillagerCampaignBehavior` | patched | Settlement-entered + daily |
| `WorkshopsCampaignBehavior` | patched + queried | BK workshop layer overlay |

---

## TaleWorlds.CampaignSystem.CampaignBehaviors

### BanditSpawnCampaignBehavior

Ticks bandit clans, populates hideouts, spawns looter parties.

**Events**: `DailyTickEvent`, `HourlyTickClanEvent`, `MobilePartyCreated`,
`MobilePartyDestroyed`, `OnGameLoadedEvent`, `OnHomeHideoutChangedEvent`,
`OnNewGameCreatedPartialFollowUpEvent`, `SettlementEntered`.

**Key methods**:
- `void InitializeInitialHideouts()`
- `void SpawnBanditsAroundHideoutAtNewGame()` / `SpawnLootersAtNewGame()`
- `MobileParty AddBanditToHideout(Hideout, PartyTemplateObject overriden=null, bool isBanditBossParty=false)`
- `void OnSettlementEntered(MobileParty, Settlement, Hero)` — bandit-boss spawn check
- `void DailyTick()` — looter-count rebalance

**SyncData**: none.

**BK**: queried via `Campaign.Current.GetCampaignBehavior<BanditSpawnCampaignBehavior>()` — never patched.

---

### CaravanConversationsCampaignBehavior

All caravan-magistrate dialogue. Pure dialogue behaviour, only listens
on `OnSessionLaunchedEvent`.

**Key methods**:
- `List<CharacterObject> FindSuitableCompanionsToLeadCaravan()` — list of clan companions eligible to lead caravans.
- `bool conversation_caravan_build_clickable_condition(out TextObject explanation)` — gate for "form caravan" dialogue option.
- Cost helpers: `int GetSmallCaravanGoldCost()`, `int GetLargeCaravanGoldCost()`.

**SyncData**: none.

**BK**: `BKKnighthoodBehavior.cs:730` Harmony-patches
`FindSuitableCompanionsToLeadCaravan` — knighthood / oath flag filters
out companions that have been knighted (now lords, not eligible to run a
caravan).

---

### CaravansCampaignBehavior

The whole vanilla caravan AI: which town to visit next, what to buy/sell,
when to sell shapes (with naval support).

**Events** (full list since BK replaces it): `DailyTickEvent`,
`DailyTickHeroEvent`, `HourlyTickPartyEvent`, `KingdomDestroyedEvent`,
`MapEventEnded`, `MobilePartyCreated`, `MobilePartyDestroyed`,
`OnGameLoadFinishedEvent`, `OnLootDistributedToPartyEvent`,
`OnNewGameCreatedPartialFollowUpEndEvent`, `OnSessionLaunchedEvent`,
`OnSettlementLeftEvent`, `OnSiegeEventStartedEvent`, `SettlementEntered`.

**Key methods** (BK calls some by reflection):
- `void SpawnCaravan(Hero hero, bool initialSpawn = false)`
- `Town ThinkNextDestination(MobileParty caravanParty, out NavigationType bestNavigationType, out bool isFromPort, out bool isTargetingPort)` — **the central destination scorer; CLAUDE.md mentions invoking this reflectively from BK**.
- `Town FindNextDestinationForCaravan(MobileParty, bool distanceCut, out NavigationType, out bool isFromPort, out bool isTargetingPort)`
- `float GetTradeScoreForTown(MobileParty, Town, CampaignTime, float caravanFullness, bool distanceCut, out NavigationType, out bool isTargetingPort)` — score function
- `void HourlyTickParty(MobileParty)` — caravan AI step
- Naval helpers: `float GetDistanceLimit{VeryFar,Far,Medium,Close}AsDaysForNavigationType(bool isNavalCaravan)`
- `bool ShouldPartyUseCoastalPrices(MobileParty)`
- `void AdjustConvoyShips(MobileParty, Town)` / `void BuyShips(MobileParty, Town)` / `void DiscardShips(MobileParty)` / `float GetShipPriority(MobileParty, Ship, bool isForSelling)`

**SyncData**:
```
_tradeRumorTakenCaravans, _lootedCaravans, _interactedCaravans,
_tradeActionLogs, _caravanLastHomeTownVisitTime,
_prohibitedKingdomsForPlayerCaravans
```

**BK**: removed and replaced.
`Campaign.Current.CampaignBehaviorManager.RemoveBehavior<CaravansCampaignBehavior>()`
in `BKCaravansBehavior.cs:56` and `:61`. `BKCaravansBehavior` runs the
shipping graph + still uses `ThinkNextDestination`/`HourlyTickParty`
reflectively per the "BK decides, vanilla executes" principle.

---

### CharacterRelationCampaignBehavior

Daily/yearly relation drift, friendship/enemy generation, marriage and
prisoner relation effects.

**Events**: `BeforeHeroesMarried`, `DailyTickEvent`, `DailyTickPartyEvent`,
`HeroKilledEvent`, `HeroRelationChanged`, `MapEventEnded`,
`OnClanChangedKingdomEvent`, `OnHeroUnregisteredEvent`,
`OnNewGameCreatedEvent`, `OnPrisonerDonatedToSettlementEvent`,
`OnSettlementOwnerChangedEvent`, `RaidCompletedEvent`.

**Key methods**:
- `void DailyTick()` — global drift
- `void DailyTickParty(MobileParty)` — clan-mate relation drift
- `void DetermineRelation(Hero, Hero, float randomValue)` — friend/enemy roll on new-game
- `void OnRaidCompleted(BattleSideEnum winnerSide, RaidEventComponent)` — raider reputation hit
- `void UpdateFriendshipAndEnemies(CampaignGameStarter)` — new-game seed

**SyncData**: none.

**BK**: `BKBanditBehavior.cs:326` patches the class — bandits captured
by player do not generate the same relation-change paths as lords.

---

### ClanVariablesCampaignBehavior

Daily clan tick: finance evaluation, settlement payment limits,
auto-recruitment, governor reassign. **The most heavily patched vanilla
behaviour in BK.**

**Events**: `DailyTickClanEvent`, `DailyTickHeroEvent`,
`OnClanChangedKingdomEvent`, `OnGameLoadedEvent`,
`OnGameLoadFinishedEvent`, `OnHeroChangedClanEvent`,
`OnNewGameCreatedEvent`, `OnNewGameCreatedPartialFollowUpEndEvent`,
`OnSessionLaunchedEvent`, `OnSettlementOwnerChangedEvent`, `WeeklyTickEvent`.

**Key methods**:
- `void DailyTickClan(Clan clan)` — entry point. Calls:
  - `MakeClanFinancialEvaluation(clan)` — bookkeeping
  - `UpdateClanSettlementsPaymentLimit(clan)` — auto wage cap
  - `UpdateClanSettlementAutoRecruitment(clan)` — auto-recruit toggle
- `void DailyTickHero(Hero)` — hero gold pruning
- `void UpdateGovernorsOfClan(Clan)` — governor swap
- `void DetermineBasicTroopsForMinorFactions()`

**SyncData**: none.

**BK**: heavily patched in `EconomyPatches.cs`:
- Line `42` patches `DailyTickClan` (BK runs custom finance eval)
- Line `55` patches `UpdateClanAfterDays`
- Line `68` patches `UpdateClanSettlementsPaymentLimit`
- Line `240` patches the whole class (likely transpiler/multi-target)
- `ImprovedGarrisons` shim: `UpdateClanSettlementAutoRecruitment` is
  prefix-skipped when IG is present (`ModCompat.ImprovedGarrisons`
  check, see CLAUDE.md mod-compat layer).

---

### CompanionRolesCampaignBehavior

Companion → roles (engineer, surgeon, scout, quartermaster), fire flow,
"turn companion into vassal lord" flow, rescue-companion flow.

**Events**: `CompanionRemoved`, `HeroRelationChanged`, `OnSessionLaunchedEvent`.

**Key methods** (most are dialogue conditions/consequences):
- `void OnCompanionRemoved(Hero, RemoveCompanionAction.RemoveCompanionDetail)`
- `static void turn_companion_to_lord_consequence()` — the central "make
  companion a vassal" path
- `static void SpawnNewHeroesForNewCompanionClan(Hero, Clan, Settlement)`
- `static int GetRandomBannerIdForNewClan()` — note BK's knighthood flow
  competes here
- Role conditions: `companion_becomes_{engineer,surgeon,quartermaster,scout}_on_condition/consequence`

**SyncData**: `_alreadyUsedIconIdsForNewClans` (banner ids already taken).

**BK**: `BKKnighthoodBehavior.cs:710` patches the class — BK injects its
knighthood flow which displaces parts of this. The patches at
`BKKnighthoodBehavior.cs:803` (against `LordConversationsCampaignBehavior`)
extend the oath-giving conversation; the `:710` patch on companion-roles
likely intercepts `turn_companion_to_lord_*` so titled companions get
the BK title pass-through.

---

### CompanionsCampaignBehavior

Wanderer/companion population — keeps a target ratio of companions
alive across towns, picks templates by skill bias.

**Events**: `DailyTickEvent`, `HeroCreated`, `HeroKilledEvent`,
`HeroOccupationChangedEvent`, `OnGameLoadFinishedEvent`,
`OnNewGameCreatedEvent`, `WeeklyTickEvent`.

**Key methods**:
- `void DailyTick()` / `void WeeklyTick()` — `TryKillCompanion()` then `TrySpawnNewCompanion()`
- `void CreateCompanionAndAddToSettlement(Settlement)` — actual spawn
- `CompanionTemplateType GetCompanionTemplateTypeToSpawn()` — picks weighted skill bucket from `_companionsOfTemplates`

**SyncData**: none.

**BK**: `FixesPatches.cs:25` — class-level patch (likely transpiler or
SyncData fix to handle stale companion lists; BK adds gentry/notable
heroes that confuse the vanilla template buckets).

---

### CraftingCampaignBehavior : ICraftingCampaignBehavior

Crafting state, smithing orders, hero crafting stamina, material spend.

**Events**: `DailyTickEvent`, `DailyTickSettlementEvent`, `HeroKilledEvent`,
`HourlyTickEvent`, `OnGameLoadedEvent`,
`OnNewGameCreatedPartialFollowUpEndEvent`, `OnNewItemCraftedEvent`,
`OnSessionLaunchedEvent`.

**Key methods (interface surface)**:
- `int GetCraftingDifficulty(WeaponDesign)`
- `bool IsOpened(CraftingPiece, CraftingTemplate)`
- `int GetHeroCraftingStamina(Hero)` / `void SetHeroCraftingStamina(Hero, int)` / `int GetMaxHeroCraftingStamina(Hero)`
- `void SetCraftedWeaponName(ItemObject, TextObject)`
- `void DoRefinement(Hero, Crafting.RefiningFormula)`
- `void DoSmelting(Hero, EquipmentElement)`
- `ItemObject CreateCraftedWeaponInFreeBuildMode(Hero, WeaponDesign, ItemModifier=null)`
- `ItemObject CreateCraftedWeaponInCraftingOrderMode(Hero, CraftingOrder, WeaponDesign)`
- `Hero GetActiveCraftingHero()` / `void SetActiveCraftingHero(Hero)`
- `void CreateTownOrder(Hero orderOwner, int orderSlot)`
- `CraftingOrder CreateCustomOrderForHero(Hero, float orderDifficulty=-1, WeaponDesign=null, CraftingTemplate=null)`
- `void CompleteOrder(Town, CraftingOrder, ItemObject, Hero)` / `void CancelCustomOrder(Town, CraftingOrder)`
- `ItemModifier GetCurrentItemModifier()` / `void SetCurrentItemModifier(ItemModifier)`

**SyncData**: `_activeCraftingHero`, `_craftedItemDictionary`,
`_heroCraftingRecordsNew`, `_craftingOrders`, `_cratingItemsHistory`,
`_openedPartsDictionary`, `_openNewPartXpDictionary`, `_townOrderCount`,
`_craftedItemCount`. Has `< e1.8.0` and `< v1.3.2` save migrations.

**BK**: queried via `GetCampaignBehavior<ICraftingCampaignBehavior>()` —
read-only access to the interface. Never patched.

---

### DisbandPartyCampaignBehavior : IDisbandPartyCampaignBehavior

Tracks parties currently disbanding (heading to home settlement to be
disbanded). Provides ETAs.

**Events**: `DailyTickPartyEvent`, `HeroPrisonerTaken`, `HourlyTickEvent`,
`HourlyTickPartyEvent`, `MobilePartyDestroyed`, `OnGameLoadFinishedEvent`,
`OnHeroTeleportationRequestedEvent`, `OnPartyDisbandCanceledEvent`,
`OnPartyDisbandedEvent`, `OnPartyDisbandStartedEvent`,
`OnSessionLaunchedEvent`, `OnSettlementLeftEvent`.

**Interface surface**: `IDisbandPartyCampaignBehavior` — query "is this
party disbanding?" and ETA.

**BK**: queried only — never patched.

---

### DiplomaticBartersBehavior

Daily clan-level barter "should I leave my mercenary contract / propose
peace / propose alliance" rolls.

**Events**: `DailyTickClanEvent`.

**Key methods** (decompiled file is small — most logic is private):
- `void DailyTickClan(Clan)` — entry; rolls for considered barters

**BK**: `DiplomacyPatches.cs:116` patches `ConsiderClanLeaveAsMercenary`
— skipped if `ModCompat.Diplomacy` is present (Diplomacy mod owns the
mercenary contract surface, see CLAUDE.md).

---

### FoodConsumptionBehavior

Per-party food consumption tick.

**Events**: `DailyTickPartyEvent`, `OnNewGameCreatedPartialFollowUpEndEvent`,
`PartyAttachedAnotherParty`, `TickEvent`.

**Key methods**: pure event handlers — `DailyTickPartyEvent` does the
food deduction; `TickEvent` smooths it. All private.

**BK**: `FixesPatches.cs:113` patches the class. Likely fixes a vanilla
NRE around custom party components (BK adds militia/garrison/estate
components that the vanilla food calc didn't anticipate).

---

### GovernorCampaignBehavior

Governor-related ticks: governor effects on settlements, governor
swap-out on death.

**Events**: `DailyTickSettlementEvent`, `HeroKilledEvent`,
`OnGameLoadFinishedEvent`, `OnHeroChangedClanEvent`,
`OnSessionLaunchedEvent`.

**Key methods**:
- `void DailyTickSettlement(Settlement)` — governor effects
- (governor-change handlers private)

**BK**: `NotablePatches.cs:200` patches `DailyTickSettlement`.
Likely a postfix that re-runs notable spawn / cleans up notable lists
after the settlement tick.

---

### HeroSpawnCampaignBehavior

Spawns the initial hero set + handles aging-in/coming-of-age placement.

**Events**: `CompanionRemoved`, `DailyTickClanEvent`, `DailyTickHeroEvent`,
`HeroComesOfAgeEvent`, `OnGovernorChangedEvent`, `OnNewGameCreatedEvent`,
`OnNewGameCreatedPartialFollowUpEndEvent`,
`OnNewGameCreatedPartialFollowUpEvent`.

**BK**: `EconomyPatches.cs:219` — class-level patch. (Likely a postfix
that wires BK estate / shipping ownership for newly placed heroes.)

---

### ItemConsumptionBehavior

Daily town consumption — drives town prosperity / food shortage.

**Events**: `DailyTickTownEvent`, `OnNewGameCreatedPartialFollowUpEndEvent`,
`OnNewGameCreatedPartialFollowUpEvent`.

**BK**:
- `EconomyPatches.cs:793` patches the class.
- `Campaign.Current.GetCampaignBehavior<ItemConsumptionBehavior>()`
  — also queried for current consumption maps.

---

### KingdomDecisionProposalBehavior

Daily / hourly check that fires AI kingdoms making war/peace/policy/fief
decisions.

**Events**: `DailyTickClanEvent`, `DailyTickEvent`, `HourlyTickEvent`,
`KingdomDecisionAdded`, `KingdomDestroyedEvent`, `MakePeace`,
`OnClanChangedKingdomEvent`, `WarDeclared`.

**BK**: `DiplomacyPatches.cs:104` prefix-patches `ConsiderWar` — skipped
if `ModCompat.Diplomacy` is present (Diplomacy mod owns war scoring).
BK keeps its `BKDeclareWarDecision` for when Diplomacy is not present.

---

### LordConversationsCampaignBehavior

The dialogue tree for every lord conversation. **Largest decompiled file
in the touched set (255 KB).**

**Events**: `OnBarterAcceptedEvent`, `OnBarterCanceledEvent`,
`OnSessionLaunchedEvent`.

**Key methods (BK-patched ones)**:
- `bool conversation_puritan_preacher_introduction_on_condition()`
- `bool conversation_minor_faction_preacher_introduction_on_condition()`
- `bool conversation_mystic_preacher_introduction_on_condition()`
- `bool conversation_messianic_preacher_introduction_on_condition()`
- `bool conversation_lord_give_oath_go_on_condition()`

**BK**:
- `ReligionPatches.cs:10/30/50/70` — replace each preacher introduction
  condition with religion-aware logic so a preacher only appears for
  the right faith.
- `BKKnighthoodBehavior.cs:803` — gates the oath-giving conversation
  through BK's knighthood/lord-promotion flow.
- `BKBanditBehavior.cs:312` — hooks bandit conversations.
- `DialoguePatches.cs:33` — class-level patch (further dialogue
  injections).

---

### PlayerTownVisitCampaignBehavior

Town menu actions (market, tavern, smithy, etc.) when the player enters
a town.

**Events**: `OnSessionLaunchedEvent`, `OnSettlementLeftEvent`, `SettlementEntered`.

**Key methods (patched)**:
- `void game_menu_town_town_market_on_consequence(MenuCallbackArgs)` — opens market screen.

**BK**: `BKTradeProfitBehavior.cs:65` patches `game_menu_town_town_market_on_consequence`
— records pre/post-market gold for trade-profit-based skill XP.

---

### RansomOfferCampaignBehavior

Daily ransom-offer rolls; player notification when an enemy lord
captured by the player offers ransom.

**Events**: `DailyTickHeroEvent`, `HeroKilledEvent`, `HeroPrisonerReleased`,
`HeroPrisonerTaken`, `HourlyTickEvent`, `OnRansomOfferedToPlayerEvent`,
`PrisonersChangeInSettlement`.

**BK**: `BKBanditBehavior.cs:296` patches the class — bandit-tier
captives don't go through the lord ransom flow.

---

### RebellionsCampaignBehavior

Tracks active rebellions, daily progress, settlement state on rebellion
trigger.

**Events**: `DailyTickClanEvent`, `DailyTickSettlementEvent`,
`OnClanDestroyedEvent`, `OnGameLoadFinishedEvent`,
`OnNewGameCreatedPartialFollowUpEndEvent`, `OnSiegeEventStartedEvent`.

**BK**: queried via `GetCampaignBehavior<RebellionsCampaignBehavior>()` —
read-only.

---

### RecruitmentCampaignBehavior

Per-settlement volunteer pool, recruitment rolls, post-recruit XP.

**Events**: `BeforeSettlementEnteredEvent`, `DailyTickSettlementEvent`,
`DailyTickTownEvent`, `HourlyTickPartyEvent`,
`OnNewGameCreatedPartialFollowUpEndEvent`, `OnSessionLaunchedEvent`,
`OnTroopRecruitedEvent`, `OnUnitRecruitedEvent`.

**Key patched method**:
- `RecruitVolunteersFromNotable(...)` — picks a volunteer to recruit at notable.

**BK**: `Patches.cs:29` patches the class. Skipped if `ModCompat.RecruitEverywhere`
is present (RE owns the surface).

---

### RomanceCampaignBehavior

Romance state machine: courting, marriage proposal, lord-to-lord
romance daily rolls.

**Events**: `DailyTickClanEvent`, `DailyTickEvent`, `OnSessionLaunchedEvent`.

**BK**: `BKMarriageBehavior.cs:664` patches the class — BK marriage
overlay (dowries, title-claim through marriage) injects ahead of vanilla
romance progression.

Also `BKMarriageBehavior.cs:643` patches `MarriageBarterable` (the
barter type, not the behaviour) for the dowry mechanic.

---

### TeleportationCampaignBehavior : ITeleportationCampaignBehavior

Hero-teleport (vanilla "send to capital" mechanic) — moves heroes home
when the campaign needs them somewhere.

**Events**: `DailyTickPartyEvent`, `HeroComesOfAgeEvent`, `HeroKilledEvent`,
`HeroPrisonerTaken`, `HourlyTickEvent`, `MobilePartyDestroyed`,
`OnClanLeaderChangedEvent`, `OnGovernorChangedEvent`,
`OnHeroTeleportationRequestedEvent`, `OnPartyDisbandedEvent`,
`OnPartyDisbandStartedEvent`, `OnSettlementOwnerChangedEvent`.

**Interface surface**: `ITeleportationCampaignBehavior` — used by
`TeleportationHelper.GetHoursLeftForTeleportingHeroToReachItsDestination(Hero)`.

**BK**: queried only.

---

### VassalAndMercenaryOfferCampaignBehavior : IVassalAndMercenaryOfferCampaignBehavior

Daily/event rolls for kingdoms making vassal or mercenary offers to
clan leaders.

**Events**: `DailyTickEvent`, `HeroPrisonerTaken`, `HeroRelationChanged`,
`KingdomDestroyedEvent`, `OnClanChangedKingdomEvent`,
`OnPlayerCharacterChangedEvent`, `OnSessionLaunchedEvent`,
`OnVassalOrMercenaryServiceOfferedToPlayerEvent`, `WarDeclared`.

**BK**: `DialoguePatches.cs:17` patches the class — BK's diplomacy
overlay gates which offers go to the player and which go straight into
BK's group/peerage system.

---

### VillageGoodProductionCampaignBehavior

Daily production calculation for villages.

**Events**: `DailyTickSettlementEvent`, `OnNewGameCreatedPartialFollowUpEvent`.

**BK**: `EconomyPatches.cs:1235` patches the class — BK overlay for
estate-driven production splits.

---

### VillagerCampaignBehavior

Villager parties — the carts that run goods from village → town and
back, drive village ←→ town economy.

**Events**: `DailyTickEvent`, `HourlyTickPartyEvent`,
`HourlyTickSettlementEvent`, `MobilePartyDestroyed`,
`OnLootDistributedToPartyEvent`, `OnSessionLaunchedEvent`,
`OnSiegeEventStartedEvent`, `SettlementEntered`.

**Key patched method**:
- `void OnSettlementEntered(MobileParty, Settlement, Hero)` — villager arrives at town/village.

**BK**:
- `EconomyPatches.cs:1157` — class-level patch.
- `EconomyPatches.cs:1192` — specifically `OnSettlementEntered` (BK
  routes village→town goods through BK's estate / shipping accounting
  before vanilla applies the trade).

---

### WorkshopsCampaignBehavior : IWorkshopWarehouseCampaignBehavior

Workshops: ownership, daily production, profit, sale to AI lords.

**Events**: `DailyTickTownEvent`, `HeroKilledEvent`,
`OnAfterSessionLaunchedEvent`, `OnClanChangedKingdomEvent`,
`OnGameLoadedEvent`, `OnNewGameCreatedPartialFollowUpEvent`,
`OnSettlementOwnerChangedEvent`, `WarDeclared`,
`WorkshopOwnerChangedEvent`, `WorkshopTypeChangedEvent`.

**BK**:
- `EconomyPatches.cs:948` patches the class.
- Also queried via `GetCampaignBehavior<WorkshopsCampaignBehavior>()` —
  BK uses both directions, so don't fully replace this. BK overlays the
  workshop layer (BK has its own `BKWorkshopBehavior`) but lets the
  vanilla behaviour run underneath.

---

## SandBox.CampaignBehaviors

### HeirSelectionCampaignBehavior (SandBox)

Triggered when the player main hero dies and the game has to switch
control to an heir.

**Events**: `OnBeforeMainCharacterDiedEvent`, `OnBeforePlayerCharacterChangedEvent`,
`OnHeirSelectionOverEvent`, `OnPlayerCharacterChangedEvent`.

**Key methods**: handlers around `OnHeirSelectionOver`, which fires once
the player has picked the heir.

**BK**: `BKTitleBehavior.cs:509` patches `OnHeirSelectionOver` — BK
re-runs title pass-down (titles that were on the dead hero must
transfer to the heir per BK succession laws, which can differ from the
heir the player picked for the player-character role).

---

## SandBox.Tournaments.MissionLogics

### TournamentBehavior (SandBox)

In-mission tournament-bracket logic. Not a `CampaignBehaviorBase` —
inherits from `MissionLogic`. Lives during a tournament mission only.

**Key patched methods**:
- `int GetExpectedDenarsForBet()` — the displayed expected reward
- `int GetMaximumBet()` — bet cap

**BK**: `BKTournamentBehavior.cs:108/122` — BK extends bet caps based
on player tournament skill perks / lifestyle.

---

## Removal / replacement summary

Only one vanilla behaviour is **removed outright** — `CaravansCampaignBehavior`
(BK ships its own caravan AI built on the shipping graph). Everything
else is patched in place. This matches the "BK decides, vanilla executes"
principle: BK doesn't reimplement vanilla mechanics, it gates and
extends them via Harmony.

## Mod-compat skips

These BK Harmony patches `return true`-skip when a competing mod is
loaded (see `BannerKings/Utils/ModCompat.cs`):

| Patch target | Skipped if |
|---|---|
| `KingdomDecisionProposalBehavior::ConsiderWar` | `ModCompat.Diplomacy` |
| `DiplomaticBartersBehavior::ConsiderClanLeaveAsMercenary` | `ModCompat.Diplomacy` |
| `ClanVariablesCampaignBehavior::UpdateClanSettlementAutoRecruitment` | `ModCompat.ImprovedGarrisons` |
| `RecruitmentCampaignBehavior::RecruitVolunteersFromNotable` | `ModCompat.RecruitEverywhere` |
| `HeroHelper::GetVolunteerTroopsOfHeroForRecruitment` | `ModCompat.RecruitEverywhere` |
