# Naval / sailing API

Campaign-layer sailing surface from build 1.3.x. Decompiled from
`TaleWorlds.CampaignSystem.dll` and `Modules/NavalDLC/NavalDLC.dll`.

In-mission ship/agent code (oars, sails, ballistas, ramming) is **not**
covered here — BK never touches mission code. Only the campaign-map
sailing surface, since that's what BK's shipping/caravan graph sits on.

---

## MobileParty sail state (`TaleWorlds.CampaignSystem.Party.MobileParty`)

The whole sail/sea question lives on `MobileParty`. Key members:

### NavigationType enum (nested)
```csharp
public enum NavigationType
{
    None    = 0,
    Default = 1,  // land
    Naval   = 2,  // sea
    All     = 3,  // both (flag combination)
}
```

### Sail state (read)
```csharp
public bool IsCurrentlyAtSea { get; set; }      // backing _isCurrentlyAtSea
public bool IsTargetingPort  { get; }           // backing _isTargetingPort
public bool StartTransitionNextFrameToExitFromPort;  // raw bool flag
public bool HasLandNavigationCapability  { get; }
public bool HasNavalNavigationCapability { get; }    // = PartyNavigationModel.HasNavalNavigationCapability(this)
public NavigationType NavigationCapability       // = (HasLand ? Default : 0) | (HasNaval ? Naval : 0)
public NavigationType DesiredAiNavigationType { get; set; }
public MBReadOnlyList<Ship> Ships => Party.Ships;
```

`IsCurrentlyAtSea` is the canonical "this party is on the water" flag.
Setting it propagates to attached parties (army members) — see
MobileParty.cs:481-510. Land/sea transitions also re-snap `Position` to
`_currentSettlement.PortPosition` vs `GatePosition`.

### Sail state (write — issue intent)
```csharp
public void SetSailAtPosition(CampaignVec2 position);    // force-go-to-sea at point
public void ChangeIsCurrentlyAtSeaCheat();               // dev cheat
public void SetMoveModeHold();                           // stop moving
public void SetMoveGoToSettlement(Settlement, NavigationType, bool isTargetingThePort);
public void SetMoveGoToPoint(CampaignVec2, NavigationType);
public void SetMoveGoToInteractablePoint(IInteractablePoint, NavigationType);
public void SetMoveEscortParty(MobileParty, NavigationType, bool isTargetingPort);
public void SetTargetSettlement(Settlement, bool isTargetingPort);
```

`SetMoveGoToSettlement` is **the** routing call — it resolves the target
to `settlement.PortPosition` if `isTargetingThePort`, else `GatePosition`.
Per CLAUDE.md "BK decides, vanilla executes": BK should always issue
movement intent through these calls and never write `party.Position`
directly.

### Position helpers
```csharp
public Settlement TargetSettlement      { get; }   // _targetSettlement
public Settlement ShortTermTargetSettlement => Ai.AiBehaviorPartyBase?.Settlement;
public CampaignVec2 MoveTargetPoint;
```

`Settlement.PortPosition` vs `Settlement.GatePosition` is the key
distinction — naval-capable parties dock at the port, land parties at
the gate.

### Ship-related
```csharp
public MBReadOnlyList<Ship> Ships;                 // owned by this party
// Inventory capacity changes when at sea:
public int InventoryCapacity =>
    (int)Campaign.Current.Models.InventoryCapacityModel
        .CalculateInventoryCapacity(this, IsCurrentlyAtSea).ResultNumber;
```

### Internal `void DestroyShipsOnDeath()`-ish path
When a party is destroyed at sea, ships are explicitly destroyed in a
loop (MobileParty.cs:3283):
```csharp
for (int i = Ships.Count - 1; i >= 0; i--)
    DestroyShipAction.Apply(Ships[i]);
```

---

## Ship (`TaleWorlds.CampaignSystem.Naval.Ship`)

Persistent campaign-side ship object. Owned by a `PartyBase`.

### Identity
```csharp
public readonly ShipHull ShipHull;            // template
public Figurehead Figurehead { get; }
public bool IsInvulnerable { get; set; }
public bool IsTradeable    { get; set; } = true;
public bool IsUsedByQuest  { get; set; }
public int  RandomValue    { get; }           // visual variation seed
public string CustomSailPatternId { get; set; } = "";
public TextObject Name { get; }
public uint VersionNo { get; }                // bumps on visual change
public PartyBase Owner { get; }
public MBReadOnlyList<ShipUpgradePiece> UnlockedUpgradePieces { get; }
public bool CanEquipFigurehead => ShipHull.CanEquipFigurehead;
```

### Health
```csharp
public float HitPoints, MaxHitPoints;
public float SailHitPoints, MaxSailHitPoints;
public float MaxFireHitPoints;                 // burning capacity
```

### Capacity / stats (delegate to model)
All routed through `Campaign.Current.Models.CampaignShipParametersModel`:
```csharp
public int   TotalCrewCapacity, MainDeckCrewCapacity, SkeletalCrewCapacity;
public float InventoryCapacity, FlagshipScore, SeaWorthiness;
public float CrewCapacityBonusFactor, ShipWeightFactor, ForwardDragFactor;
public float MaxOarPowerFactor, MaxOarForceFactor, SailForceFactor;
public float SailRotationSpeedFactor, FurlUnfurlSpeedFactor;
public float RudderSurfaceAreaFactor, MaxRudderForceFactor;
public float CrewMeleeDamageFactor, CrewShieldHitPointsFactor;
public int   AdditionalAmmo, AdditionalArcherQuivers, AdditionalThrowingWeaponStack;
public float CampaignSpeedBonusFactor;
public float GetCampaignSpeed();
public MBList<SiegeEngineType> GetSiegeEngines();
```

### Mutators
```csharp
public Ship(ShipHull shipHull);
public void ChangeFigurehead(Figurehead);
public ShipUpgradePiece GetPieceAtSlot(string slotTag);
public void EquipUpgradePiece(string slotTag, ShipUpgradePiece newUpgradePiece);
public bool HasSlot(string slotTag);
public void SetName(TextObject);
```

Always go through the action helpers for the campaign-state-relevant
ones:

- `ChangeShipOwnerAction.ApplyByMobilePartyCreation(PartyBase, Ship)`
- `DestroyShipAction.Apply(Ship)`
- `RepairShipAction.Apply(Ship, Settlement)` (implied by event below)

---

## Ship-related campaign events (`CampaignEvents`)

```csharp
IMbEvent<Ship, Settlement>                                  OnShipCreatedEvent;
IMbEvent<PartyBase, Ship, DestroyShipAction.ShipDestroyDetail> OnShipDestroyedEvent;
IMbEvent<Ship, PartyBase, ChangeShipOwnerAction.ShipOwnerChangeDetail> OnShipOwnerChangedEvent;
IMbEvent<Ship, Settlement>                                  OnShipRepairedEvent;
```

Subscribe with `CampaignEvents.OnShipOwnerChangedEvent.AddNonSerializedListener(this, handler)`
in `RegisterEvents()`.

---

## MapDistanceModel (`TaleWorlds.CampaignSystem.ComponentInterfaces`)

Abstract base. The naval-aware default is in NavalDLC, see below.
**This is what BK should call to ask "can A reach B (and at what cost)?".**

```csharp
public abstract class MapDistanceModel : MBGameModel<MapDistanceModel>
{
    public abstract int RegionSwitchCostFromLandToSea { get; }
    public abstract int RegionSwitchCostFromSeaToLand { get; }
    public abstract float MaximumSpawnDistanceForCompanionsAfterDisband { get; }

    public abstract float GetMaximumDistanceBetweenTwoConnectedSettlements(MobileParty.NavigationType);
    public abstract float GetLandRatioOfPathBetweenSettlements(Settlement from, Settlement to, bool isFromPort, bool isTargetingPort);

    // distance overloads:
    public abstract float GetDistance(MobileParty from, Settlement to,        bool isTargetingPort, MobileParty.NavigationType, out float estimatedLandRatio);
    public abstract float GetDistance(MobileParty from, MobileParty to,                              MobileParty.NavigationType, out float landRatio);
    public abstract bool  GetDistance(MobileParty from, MobileParty to,                              MobileParty.NavigationType, float maxDistance, out float distance, out float landRatio);
    public abstract float GetDistance(Settlement from, Settlement to,         bool isFromPort, bool isTargetingPort, MobileParty.NavigationType);
    public abstract float GetDistance(Settlement from, Settlement to,         bool isFromPort, bool isTargetingPort, MobileParty.NavigationType, out float landRatio);
    public abstract float GetDistance(MobileParty from, in CampaignVec2 to,   MobileParty.NavigationType, out float landRatio);
    public abstract float GetDistance(Settlement from, in CampaignVec2 to,    bool isFromPort, MobileParty.NavigationType);

    public abstract float GetPortToGateDistanceForSettlement(Settlement);
    public abstract bool  PathExistBetweenPoints(in CampaignVec2 from, in CampaignVec2 to, MobileParty.NavigationType);
    public abstract void  RegisterDistanceCache(MobileParty.NavigationType, INavigationCache);

    public abstract (Settlement, bool) GetClosestEntranceToFace(PathFaceRecord, MobileParty.NavigationType);
    public abstract MBReadOnlyList<Settlement> GetNeighborsOfFortification(Town, MobileParty.NavigationType);
    public abstract float GetTransitionCostAdjustment(Settlement s1, bool isFromPort, Settlement s2, bool isTargetingPort, bool fromIsCurrentlyAtSea, bool toIsCurrentlyAtSea);
}
```

`landRatio` is in `[0, 1]` — fraction of the path that is on land. Lets
callers reason about how much of a route is sea (e.g. shipping graph
prefers high-sea-ratio legs for naval-capable carriers).

`isFromPort`/`isTargetingPort` flag whether each endpoint is the port
location (sea side of the settlement) vs the gate (land side).

`Campaign.Current.Models.MapDistanceModel.GetDistance(...)` is the
public entry point.

### NavalDLC override

`NavalDLC.GameComponents.NavalDLCMapDistanceModel` replaces `DefaultMapDistanceModel`
and adds the actual sea-route pathfinder. Same interface.

---

## Sailing helpers

### `Helpers.ShipHelper`
```csharp
static Banner GetShipBanner(IShipOrigin shipOrigin, IAgent captain = null);
static (uint sailColor1, uint sailColor2) GetSailColors(IShipOrigin shipOrigin, IAgent captain = null);
static Banner GetShipBanner(PartyBase party = null);
static (uint sailColor1, uint sailColor2) GetSailColors(PartyBase party = null);
```
Resolves which banner / colour to apply to the visual sail. Falls
through army leader → owner clan → faction.

### `Helpers.TeleportationHelper`
```csharp
static float GetHoursLeftForTeleportingHeroToReachItsDestination(Hero teleportingHero);
```
Wraps `ITeleportationCampaignBehavior.GetHeroArrivalTimeToDestination`.

### `Helpers.MobilePartyHelper`
14 KB, large. Land-and-sea utility methods.

### `NavalDLC.NavalDLCExtensions` (extension methods)
```csharp
public static CampaignVec2 DropOffLocation(this Village);
public static bool IsFishingParty(this MobileParty);
public static MBReadOnlyList<FishingPartyComponent> FishingParties(this Village);
public static bool IsPirate(this CharacterObject);                  // Mariner + Occupation 15
public static Building GetShipyard(this Town);                       // returns Building of NavalBuildingTypes.SettlementShipyard
public static List<ShipUpgradePiece> GetAvailableShipUpgradePieces(this Town);
public static bool IsNavalStorylineQuestParty(this PartyBase, out NavalStorylinePartyData);
public static bool IsNavalStorylineQuestParty(this PartyBase);
public static bool IsNavalStorylineQuestParty(this MobileParty, out NavalStorylinePartyData);
public static bool IsNavalStorylineQuestParty(this MobileParty);
```

### `NavalDLC.NavalDLCHelpers`
Mostly mission-code helpers (`IsShipOrdersAvailable`, `IsAgentCaptainOfFormationShip`),
but two campaign-side ones BK can use:
```csharp
public static ExplainedNumber GetAveragePartySizeLimitFromTemplate(PartyTemplateObject);
public static ExplainedNumber GetMaxPartySizeLimitFromTemplate    (PartyTemplateObject);
public static List<Ship> GetSetPieceBattleShips(PartyTemplateObject template, PartyBase party);
public static void SetCustomSailPatternOfPartyShips(MobileParty, string sailId);
public static void AddUpgradePiecesToPartyShips(MobileParty, Dictionary<string,string> bySlot, Figurehead figurehead = null);
public static void AddSisterToClan();    // story-mode-specific
```

---

## NavalDLC top-level

### NavalDLCManager (`NavalDLC.NavalDLCManager : GameHandler`)
```csharp
public static NavalDLCManager Instance;
public GameModels GameModels { get; private set; }
public NavalCulturalFeats NavalCulturalFeats { get; }
public NavalBuildingTypes NavalBuildingTypes { get; }
public NavalVillageTypes  NavalVillageTypes  { get; }
public NavalSkills        NavalSkills        { get; }
public NavalSkillEffects  NavalSkillEffects  { get; }
public NavalPerks         NavalPerks         { get; }
public NavalPolicies      NavalPolicies      { get; }
public NavalStorylineData NavalStorylineData { get; }
public NavalDLCEvents     NavalDLCEvents     { get; }
public NavalItemCategories NavalItemCategories { get; }
public INavalMapSceneWrapper NavalMapSceneWrapper { get; set; }
public Dictionary<Village, List<FishingPartyComponent>> FishingParties { get; }
public StormManager StormManager { get; internal set; }
public void OnGameStart(Game, IGameStarter);  // installs models, events, StormManager
public void OnGameEnd(Game);
```

`Campaign.Current.AddCustomManager<StormManager>()` is called in
`OnGameStart` for new games (and `GetCustomManager<StormManager>` on
load).

### NavalDLCEvents (`NavalDLC.NavalDLCEvents : CampaignEventReceiver`)
```csharp
static IMbEvent<PartyBase, NavalStorylinePartyData> IsNavalQuestPartyEvent;
static IMbEvent<bool>                               OnNavalStorylineActivityChangedEvent;
static IMbEvent                                     OnSisterRansomedEvent;
static IMbEvent                                     OnSisterRansomRequestedEvent;
static IMbEvent                                     OnGangradirSavedEvent;
static IMbEvent<NavalStorylineData.StorylineCancelDetail> OnNavalStorylineCanceledEvent;
static IMbEvent                                     OnNavalStorylineTutorialSkippedEvent;
static IMbEvent<Storm>                              OnStormCreatedEvent;
```
Used to ask "is this party a quest party?" (early bail-out for BK
behaviours so they don't double-process quest parties), and to react to
storm creation.

### NavalPolicies (`NavalDLC.NavalPolicies`)
Policy objects added to `KingdomDecision.PolicyDecision` pool:
```csharp
static PolicyObject FraternalFleetDoctrine;
static PolicyObject KingsTitheOnKeels;
static PolicyObject RoyalRansomClaim;
static PolicyObject RoyalNavyPrerogative;
static PolicyObject MaritimeWealEdict;
static PolicyObject KingsPardonForPirates;
static PolicyObject RaidersSpoils;
static PolicyObject CoastalGuardEdict;
static PolicyObject BolsterTheFyrd;
static PolicyObject NavalConjoiningStatute;
static PolicyObject ArsenalDepositoryAct;
```

### Storm (`NavalDLC.Map.Storm`)
```csharp
public enum StormTypes { /* ... */ }
public readonly StormTypes StormType;
public bool   IsActive               { get; set; }
public Vec2   CurrentPosition        { get; set; }
public float  Intensity              { get; set; }
public bool   IsInDevelopingState    => _developingStateFinishCampaignTime.IsFuture;
public bool   IsInFinalizingState    => _finalizingStateStartCampaignTime.IsPast;
public bool   IsReadyToBeFinalized   { get; }
public bool   IsVisuallyDirty        { get; }
public float  EffectRadius => MapStormModel.GetEffectRadiusOfStorm(this);
public float  EyeRadius    => MapStormModel.GetEyeRadiusOfStorm(this);

public Storm(Vec2 initialPosition, StormTypes);
public void ForceDeactivate();
public void SetVisualDirty();
public void OnVisualUpdated();
public bool HasWetWeatherEffectAtPosition(Vec2);
public void HourlyTick();
public void Tick(float dt);
public void OnAfterLoad();
public void ChangeMoveDirection();
```

### StormManager (`NavalDLC.Map.StormManager`)
```csharp
public bool DebugVisualsEnabled, DebugVisualsStopped;
public MBReadOnlyList<Storm> SpawnedStorms;
public void CreateStormAtPosition(Vec2);
public void CreateStormAtPosition(Vec2, Storm.StormTypes);
public void OnAfterLoad();
```

---

## NavalDLC campaign behaviours

These run alongside vanilla. BK does **not** patch any of them currently,
but if BK ever needs to gate ship purchases / ship trades / port
characters, this is the table.

### `NavalDLC.CampaignBehaviors.ShipTradeCampaignBehavior`
Daily clan-tick: clans buy/sell/transfer ships between their parties.

**Events**: `OnNewGameCreatedPartialFollowUpEvent`, `DailyTickClanEvent`,
`OnShipOwnerChangedEvent`, `OnShipRepairedEvent`, `SettlementEntered`,
`OnGameLoadFinishedEvent`, `TickEvent`.

Notable methods:
- `void ConsiderPurchasingShip(Clan)`, `float GetClanShipPurchaseChance(Clan)`
- `void TryPurchasingShipFromTown(MobileParty, Town)`, `Town GetTownToBuyShipFrom(Clan)`, `bool CanClanBuyShipFromTown(Clan, Town)`
- `void ConsiderSwappingClanLeaderShips(Clan)` / `ConsiderSwappingShipsBetweenClanParties(Clan)`
- `void ConsiderSellingShips(Clan)`, `bool TryGetShipToSell(MobileParty, out Ship)`, `Town GetTownToSellShip(Clan)`
- `int GetTotalNumberOfWarShipsInClan(Clan)`
- `bool CanPartyTradeShip(MobileParty)`

Constants: `ShipSellingChance = 0.1f`, `ShipTransferringChance = 0.75f`,
`ClanGoldRatioToBuyShip = 0.2f`. `public static bool DebugNavalLordParties`.

### `NavalDLC.CampaignBehaviors.ShipProductionCampaignBehavior`
Towns produce ships at port over time.

**Events**: `OnNewGameCreatedPartialFollowUpEvent`, `DailyTickTownEvent`,
`OnShipCreatedEvent`, `OnShipDestroyedEvent`, `OnShipOwnerChangedEvent`, `TickEvent`.

### `NavalDLC.CampaignBehaviors.ShipRepairCampaignBehavior`
Repairs damaged ships in port.

**Events**: `OnAfterSessionLaunchedEvent`, `OnSettlementOwnerChangedEvent`,
`AfterSettlementEntered`, `DailyTickPartyEvent`,
`OnClanChangedKingdomEvent`, `OnShipDestroyedEvent`.

### `NavalDLC.CampaignBehaviors.ShipUpgradeCampaignBehavior`
AI clans buy ship upgrades when in port.

**Events**: `SettlementEntered`, `DailyTickPartyEvent`, `OnNewGameCreatedPartialFollowUpEvent`.

### `NavalDLC.CampaignBehaviors.SeaDamageCampaignBehavior`
Storm/sea-attrition damage to parties at sea.

**Events**: `HourlyTickPartyEvent`, `TickEvent`.

### `NavalDLC.CampaignBehaviors.NavalShipDistributionCampaignBehavior`
Distributes ships when a party disbands or is destroyed (recovers gold,
hands ships to nearby clan parties).

**Events**: `OnPartyDisbandedEvent`, `MobilePartyDestroyed`.

Methods:
```csharp
void DistributePartyShipsAndRecoverGold(MobileParty);
void DistributeShips(MobileParty);
MobileParty GetClanPartyToGetShipOfDisbandingParty(Ship, Clan);
void RecoverGoldFromRemainingShipsAfterDistribution(MobileParty);
```

### `NavalDLC.CampaignBehaviors.PortCharactersCampaignBehavior`
Spawns port-themed townsfolk when player visits a port location.

**Events**: `OnAfterSessionLaunchedEvent`, `LocationCharactersAreReadyToSpawnEvent`.

Spawn percentages (constants):
`PortTownsmanCarryingStuffSpawnPercentage = 0.6f`,
`PortTownsmanSpawnPercentageMale = 0.2f`, `…Female = 0.1f`,
`ShipyardWorkerSpawnPercentage = 1f`, `MarketWorkerSpawnPercentage = 0.75f`,
`CarpenterSpawnPercentage = 0.35f`.

### `NavalDLC.CampaignBehaviors.ShipNameCampaignBehavior`
Generates ship names from culture-specific lists. Largest of these
(58 KB).

### `TaleWorlds.CampaignSystem.CampaignBehaviors.NavalPatrolPartiesCampaignBehavior`
Spawns/manages naval patrol parties.

**Events**: `DailyTickSettlementEvent`, `OnSettlementOwnerChangedEvent`,
`OnNewGameCreatedPartialFollowUpEvent`, `SettlementEntered`,
`AiHourlyTickEvent`, `OnSettlementLeftEvent`, `MobilePartyDestroyed`.

Note: this class lives in `TaleWorlds.CampaignSystem.dll` but is a
naval-aware behaviour added by NavalDLC at game start.

---

## PortState (`TaleWorlds.CampaignSystem.GameState.PortState`)

Game-state for the port screen (where the player manages ships at a town):
```csharp
public readonly PortScreenModes PortScreenMode;
public readonly PartyBase LeftOwner, RightOwner;
public readonly MBReadOnlyList<Ship> LeftShips, RightShips;
public readonly Action OnEndAction;
public override bool IsMenuState => true;
```
Constructors cover: party-vs-party port (e.g. ship trade between
caravans), settlement-vs-party, raw ships-vs-ships.

Push with `Game.Current.GameStateManager.PushState(new PortState(...))`.

---

## NavalDLC GameModels (overrides BK should know about)

`NavalDLC.GameComponents.GameModels` registers these replacements via
`game.AddGameModelsManager<GameModels>(gameStarter.Models)`:

| Model class | Interface |
|---|---|
| `NavalDLCCampaignShipDamageModel` | `CampaignShipDamageModel` |
| `NavalDLCCampaignShipParametersModel` | `CampaignShipParametersModel` |
| `NavalDLCMapDistanceModel` | `MapDistanceModel` (replaces default) |
| `NavalDLCMapVisibilityModel` | overlays vanilla `MapVisibilityModel` |
| `NavalDLCMapWeatherModel` | overlays vanilla `MapWeatherModel` |
| `NavalDLCShipCostModel` | `ShipCostModel` |
| `NavalDLCClanShipOwnershipModel` | `ClanShipOwnershipModel` (NavalDLC interface) |
| `NavalDLCShipDistributionModel` | `ShipDistributionModel` (NavalDLC interface) |
| `NavalDLCShipLimitModel` | ship-cap model |
| `NavalDLCShipPhysicsParametersModel` | `ShipPhysicsParametersModel` |
| `NavalDLCShipStatModel` | derived ship stats |

Two of these are NavalDLC-only abstract bases (no vanilla equivalent):
`ClanShipOwnershipModel`, `ShipDistributionModel`, `MapStormModel`.

If BK wanted to influence ship stats / distribution, it would override
these models — but per "BK decides, vanilla executes", BK should prefer
gating *which* port a caravan visits over rewriting ship stats.

---

## BK touch points

BK does not patch any naval campaign-system class directly. The only
naval-adjacent touch points are:

- `BannerKings/Behaviours/Shipping/BKShippingBehavior.cs` — BK's shipping
  graph; reads `MapDistanceModel` and `MobileParty.IsCurrentlyAtSea`,
  issues `SetMoveGoToSettlement(..., NavigationType.Naval, isTargetingThePort: true)`.
- `BannerKings/Patches/NavalPerkPatches.cs` — patches naval-perk surfaces
  (mission-side bonuses, not campaign behaviours).
- `BannerKings/Utils/ModCompat.cs` — detects `NavalDLC` (always present
  in this fork's target build).

The "BK decides, vanilla executes" rule from CLAUDE.md applies in
particular to naval routing: BK chooses the next port via the shipping
graph, then issues a single `SetMoveGoToSettlement` call. Never write
to `MobileParty.Position` directly when at sea — the sailing visuals
and physics rely on the engine pathfinder owning the trajectory.
