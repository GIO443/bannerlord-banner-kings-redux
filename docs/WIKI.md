# BannerKings Wiki (1.3.x Fork)

A pseudo-wiki of the BannerKings mod for Mount & Blade II: Bannerlord, scoped to the
1.3.x fork with War Sails / Nord integration. Audience: players asking "what does X
do", modders asking "where does X live in the code", and the RAG bot that will read
this file.

---

## 1. Project at a glance

BannerKings ("BK") is a deep simulation overlay on top of Bannerlord's Campaign. Where
vanilla treats settlements as resource nodes and clans as hero bags, BK adds:

- **Population simulation** — every settlement has serfs, slaves, craftsmen, nobles,
  with classes that grow/shrink based on policies, food, raids, and laws.
- **Feudal titles** — a hierarchy of Empires → Kingdoms → Duchies → Counties → Baronies →
  Lordships, each with deeds, claimants, succession rules, and contracts.
- **Religions** — multiple faiths with doctrines, clergy, piety, and rites.
- **Education** — heroes have languages, books, scholarship, lifestyles (skill-line specs).
- **Estates** — clan-owned, hero-managed land within villages that produce income/food.
- **Council & courts** — clans and kingdoms have appointed officers (Marshal, Steward,
  Chancellor, Spymaster, Court Physician) with real effects.
- **Mercenary contracts, criminality, gentry, knighthood** — many smaller systems.

The fork's two purposes:

1. **Port to Bannerlord 1.3.x (build 110062+)** — original project stalled before 1.3.x.
2. **Native War Sails / Nord faction support** — Nord settlements/clans/titles/culture
   integrated directly so Nord interactions don't null-ref crash.

---

## 2. Code layout

```
BannerKings/
├── BannerKingsConfig.cs       Singleton, lazy-inits all managers
├── Main.cs                    SubModule entry, registers behaviors & models
├── Patches.cs                 Misc Harmony patches (Hero, NameGenerator, etc.)
├── Patches/                   Topic-grouped Harmony patches (Economy, Diplomacy,
│                              Fixes, NordCompat, etc.)
├── Behaviours/                CampaignBehaviorBase subclasses — the runtime "hooks"
├── Managers/                  Pure-data domain managers (titles, religion, education…)
├── Models/Vanilla/            BK overrides of vanilla GameModels (XP, finance, war…)
├── Components/                MobileParty/Settlement Components (estates, militia, etc.)
├── CampaignContent/           Traits, characters, story content
├── UI/                        Gauntlet view models & screens
├── Actions/                   Static helpers wrapping campaign actions
├── Settings/                  MCM settings & feature toggles
├── Utils/                     Helpers (text, math, save migration)
├── Dialogue/                  Conversation lines & conditions
└── _Module/ModuleData/        XML data: titles, religions, lifestyles, traits…
```

### Naming conventions
- `BK*Behavior` — campaign behaviors (live game-loop hooks).
- `BK*Model` — overrides of `XxxModel` from `TaleWorlds.CampaignSystem.GameComponents`.
- `Default*` — singleton registries of static content (e.g., `DefaultLifestyles.Instance`).
- `*Manager` — manager singletons accessed via `BannerKingsConfig.Instance.XxxManager`.

---

## 3. Bootstrap & lifecycle

`Main.cs::OnGameStart` registers BK's models and behaviors with the campaign starter.
`BannerKingsConfig.Instance` is the top-level service locator; managers are **lazy-
initialized** on first access to avoid null-refs during early load:

```csharp
public PopulationManager PopulationManager
{
    get => _populationManager ??= new PopulationManager(...);
}
```

Save data lives in two places:
- Per-behavior `SyncData` saves the behavior's own state.
- Managers are saved via `SaveDefiner` which registers BK types with the save system.

---

## 4. Major systems

### 4.1 Population (`Managers/Populations/`)
Each settlement gets a `PopulationData` with `PopulationClass` rows (Serfs, Slaves,
Craftsmen, Nobles, Tenants). Daily ticks update growth, food consumption, mood, and
class transitions. `LandData`, `EconomicData`, `MilitaryData`, `CultureData`,
`MineralData`, `VillageData` are sub-records on the same data object. Tax and
production models read these to compute settlement output.

Owned by: `Behaviours/BKSettlementBehavior.cs`, `BKPopulationsBehavior` (if present),
manager: `PopulationManager` (collection of `Settlement → PopulationData`).

### 4.2 Titles (`Managers/Titles/`)
A `FeudalTitle` is the deed-of-ownership for a settlement (or a virtual region for
higher tiers). Tiers run Empire (1) → Kingdom (2) → Duchy (3) → County (4) →
Barony (5) → Lordship (6). Each title carries:
- `deJure` — legal owner (Hero).
- `deFacto` — actual controlling hero, derived from settlement ownership.
- `Vassals` — child titles.
- `Contract` — `FeudalContract` defining government type, succession, inheritance,
  gender law, and demesne laws.
- `Claimants` — heroes with pressed claims.

Behavior: `BKTitleBehavior.cs` handles inheritance, claim aging, succession events,
and patches vanilla heir selection (`OnHeirSelectionOver(Hero selectedHeir)` in
1.3.x — note: not the older `List<InquiryElement>` signature).

XML data: `_Module/ModuleData/titles.xml` plus `nord_titles.xml` for War Sails.

### 4.3 Religion (`Managers/Institutions/Religions/`)
A `Religion` has a `Faith` (theology, divinities, taboos, holy days), `Doctrines`
(unlockable tenets like *Astrology* — speeds ship travel — or *Iconoclasm*), clergy
hierarchy, and stances against other faiths (`Tolerated`, `Untolerated`, `Hostile`).

Heroes have a piety stat. Taking a settlement from a different faith, or sacking a
holy site, modifies notable relations (see `BKRelationsBehavior`).

Behavior: `BKReligionsBehavior.cs`. Conversion, blessings, and rite resolution live
here.

### 4.4 Education & Lifestyles (`Managers/Education/`)
`EducationData` per hero tracks:
- **Languages** known/learning (`DefaultLanguages` — includes `Nordic` for War Sails).
- **Books** owned/read; books grant skill XP and a small permanent perk.
- **Scholarship** flag (from perks like ScholarshipMechanic / Accountant /
  NaturalScientist / Treasurer — all four enable "scholar mode").
- **Lifestyle** chosen (see below) and progress toward each tier.

A **Lifestyle** is a paired skill specialization (e.g., `Cataphract` ties Riding +
Polearm) that grants escalating perks at progress thresholds. `BKLifestyleBehavior`
ticks progress as the hero exercises both linked skills.

`BKEducationBehavior.cs` also runs the **book seller** spawn loop — populates
`bookSellers` (now null-guarded) so taverns always have a way to acquire books.

### 4.5 Council & Court (`Managers/Court/`)
Each clan has a `CouncilData` with positions: Marshal, Steward, Chancellor,
Spymaster, Court Physician (and lower tiers). Filling them costs influence/gold and
applies stat effects to clan parties or the realm. Kingdom-level councils exist for
the ruling clan with elevated authority (e.g., royal Marshal).

Behavior: `Behaviours/BKManagerBehavior.cs` and council-decision classes in
`Managers/Kingdoms/Council/`.

### 4.6 Estates (`Components/EstateComponent.cs`, `Managers/Populations/Estates/`)
Estates are sub-properties inside a village owned by clans. They hold tenants
(serfs/slaves), produce food and gold, and can be inherited, sold, or seized.
`EstateComponent` is the `MobilePartyComponent` for the auto-managed estate parties
that move between fields and the manor.

UI: `UI/Estates/`.

### 4.7 Diplomacy & Groups (`Behaviours/Diplomacy/`)
Two layers:
- **Kingdom diplomacy** — `KingdomDiplomacy.cs` extends vanilla war/peace with
  alliances, vassalages, and CB-style war justifications.
- **Interest groups & demands** — `Groups/` hosts factional sub-clusters within a
  kingdom (radicals, moderates, claimants). They issue **demands** that, if
  unaddressed, escalate to grievances or rebellion. Demand types:
  `ClaimantDemand`, `CouncilPositionDemand`, `PolicyChangeDemand`, `SecessionDemand`,
  `TitleDemand`.

### 4.8 Goals (`Managers/Goals/`)
A planning layer for AI clans/kingdoms. Each `Goal` has `IsAvailable`, a cost, and
side effects. `KingdomGoal` and `EmpireGoal` are higher-tier strategic goals.
`GoalManager` schedules evaluation per clan tick.

### 4.9 Mercenary contracts (`Behaviours/Mercenary/`)
`CustomTroop` lets the player design a custom mercenary unit — culture, equipment
roster, skills, formation class. Heavy reflection into vanilla `BasicCharacterObject`
(now field-cached for perf). Contracts have hire price, daily wage (3× vanilla — by
design), and a duration; auto-renewal pulls from clan gold.

### 4.10 Shipping (`Managers/Shipping/`, `Behaviours/Shipping/`)
`DefaultShippingLanes` defines per-culture sea routes between ports. `BKShippingBehavior`
hosts the player **wait menu** (`bk_shipping_wait`) for sea travel: pay gold → time
elapses → arrive at destination port (or just outside if besieged). AI caravans
auto-board when their destination is on a known lane. Astrology doctrine speeds
travel by ~25%. Disembark logic respects siege state.

### 4.11 Relations (`Behaviours/Relations/`)
`BKRelationsBehavior` extends the vanilla relation int with `RelationsModifier`s —
named, dated, optionally expiring contributions ("Support on decision X (+15)").
Hooks: kingdom decisions, quest completion, daily decay, battle defeats, settlement
captures (with religion/culture matching for the conqueror's reception by notables).

### 4.12 Criminality (`Behaviours/Criminality/`)
`DefaultCriminalSentences` defines outcomes for caught criminals (fines, imprisonment,
execution). `BKBanditBehavior` spawns and manages bandit clans with culture/biome
preferences.

### 4.13 Gentry, Knighthood, Notables
Three behaviors layer sub-noble heroes on top of vanilla:
- `BKGentryBehavior` — minor landed families below clan tier 1, can be sponsored.
- `BKKnighthoodBehavior` — granting knighthood (creates a vassal tier-1 clan with a
  fief grant and oath).
- `BKNotableBehavior` — extends notable progression, retiring, and inheritance.

### 4.14 Lordships, Republics, Coronations
- `BKLordPropertyBehavior` — tracks personal vs. clan vs. realm property.
- `BKRepublicBehavior` — alternate constitution (republics elect rulers).
- `BKCoronationBehavior` — crowning event, bestows authority bonus, affects legitimacy.

---

## 5. Patches (Harmony)

After the 1.3.x audit only essential patches remain. They live in `Patches/`:

- **EconomyPatches** — caravan fixes, garrison auto-recruit cap, loot distribution
  (`LootDefeatedPartyItems` in 1.3.x).
- **DiplomacyPatches** — war proposal cost wiring
  (`GetIsProposingWarEnabledWithReason`).
- **FixesPatches** — 7 BK-essential vanilla fixes (companions, map screen, name
  generator, inventory logic, item registration, food consumption). All other
  legacy fixes were deleted as obsolete in 1.3.x.
- **NordCompatPatches** — null guards around Nord settlements when War Sails is
  installed without explicit BK Nord data.
- **`Patches.cs`** — top-level misc patches (Hero render, etc.).

Reflection-heavy hot paths cache `FieldInfo` / `MethodInfo` / `PropertyInfo`
statically (see `BKSettlementBehavior`, `BKSkillBehavior`, `UIManager`,
`CustomTroop`, `BKReligionsBehavior`).

---

## 6. Models (`Models/Vanilla/`)

BK overrides ~30 vanilla `GameModel`s. Notable ones:

| Model | What it changes |
|---|---|
| `BKClanFinanceModel` | Folds estate income, mercenary wages, council costs into clan budget |
| `BKPartyWageModel` | Custom merc wage = 3× vanilla; council Marshal reduces wage |
| `BKBattleRewardModel` | Slave capture, religious morale, estate-tied loot share |
| `BKEconomyModel` | Population-driven prosperity & demand instead of vanilla curve |
| `BKMarriageModel` | Title-aware compatibility, dowries, claim transfer |
| `BKLearningModel` | Education & lifestyle XP multipliers |
| `BKDiplomacyModel` | War/peace scoring includes title pressure & faith stance |
| `BKMilitiaModel` / `BKGarrisonModel` | Population class drives militia comp |
| `BKVillageProductionModel` | Estate output, climate, religion bonuses |

When an override's base method was removed in 1.3.x the override was deleted, never
left as a no-op.

---

## 7. UI (`UI/`)

Gauntlet-driven. Top-level entry: `BannerKingsScreen.cs` (full-screen mod menu).
Tabs in `UI/VanillaTabs/`:
- **TownManagement** — population, garrison, buildings, estates per settlement.
- **Kingdoms** — diplomacy, councils, peerage, demesne laws, succession.
- **Character** — religion, education, lifestyle, courtiers.
- **Court** — clan officers, oaths.
- **Cultures** — culture deep-dive, traits, languages.
- **Crafting** — extra materials and armor crafting extensions.

Notifications & cutscenes live in `UI/Notifications/` and `UI/Cutscenes/`
(empire-founding scene, coronations).

---

## 8. War Sails / Nord integration

Native, not a separate sub-mod. Touch points:
- `_Module/ModuleData/titles.xml` and `nord_titles.xml` — Nordic title hierarchy.
- `Managers/Titles/Succession/DefaultSuccessions.cs` — Nord succession rules
  (typically agnatic seniority).
- `Managers/Education/Languages/DefaultLanguages.cs` — `Nordic` language entry.
- `Patches/NordCompatPatches.cs` — null guards: if Nord settlement has no BK data
  yet, return safe defaults instead of crashing.
- Works with **and without** the War Sails DLC installed.

---

## 9. Settings & cheats

- `Settings/` — MCM-driven feature toggles (turn off religions, estates, etc.).
- `BannerKingsCheats.cs` — debug commands prefixed `bk_*` (grant title, set piety,
  spawn estate, simulate raid, force election, etc.). Disabled in non-dev builds.

---

## 10. Build & dev

```bash
BANNERLORD_GAME_DIR="C:/Program Files (x86)/Steam/steamapps/common/Mount & Blade II Bannerlord" \
  dotnet build BannerKings/BannerKings.csproj -c Release
```

The csproj resolves all game refs from `$(BANNERLORD_GAME_DIR)`. Output goes
straight into `Modules/BannerKings/bin/Win64_Shipping_Client/`.

Build is currently clean (0 errors). The remaining ~149 BHA0001 warnings are
analyzer false positives — verified against the actual 1.3.x DLLs with Mono.Cecil.

---

## 11. Common gotchas

- **Don't call manager properties before `OnGameStart`** — they lazy-init only on
  access; some early hooks may fire pre-init. Use the lazy-init guard, not a
  null check.
- **`OnHeirSelectionOver` signature changed in 1.3.x** — takes `Hero selectedHeir`,
  not `List<InquiryElement>`. Old patches will silently no-op.
- **`InitializeLordPartyProperties` is gone in 1.3.x** — the patch was removed,
  not retargeted.
- **Reflection hot paths are cached** — when adding new reflection, follow the
  `private static readonly FieldInfo X = AccessTools.Field(...)` pattern to avoid
  per-tick cost. `Hero.Name` and per-hero loops are the worst offenders.
- **Perk values** — vanilla `EffectIncrementType.AddFactor` takes a *fraction*
  (`0.05f` = +5%). Whole-number values are a 100× balance bug. All BK perks were
  audited and corrected.
- **Lifestyle scholarship gate** — requires *any* of ScholarshipMechanic /
  Accountant / NaturalScientist / Treasurer. Earlier code had four duplicate
  ScholarshipMechanic checks; fixed.

---

## 12. Where things live — quick lookup

| Question | File |
|---|---|
| How is a hero's title decided when they die? | `BKTitleBehavior.cs::OnHeirSelectionOver` + `FeudalTitle.Succession` |
| Where do religion stances apply? | `BKRelationsBehavior.cs::OnSettlementOwnerChanged` |
| Where is sea travel time calculated? | `BKShippingBehavior.cs::CalculateArrival` |
| How do interest groups raise demands? | `Behaviours/Diplomacy/Groups/Demands/*.cs` |
| Where is council position cost? | `Models/Vanilla/BK*` + `Managers/Court/CourtManager.cs` |
| How do estates produce income? | `Components/EstateComponent.cs` + `BKVillageProductionModel.cs` |
| Where is the book seller spawn loop? | `BKEducationBehavior.cs` (null-guarded) |
| Custom merc unit creation flow | `Behaviours/Mercenary/CustomTroop.cs` |

---

## 13. Glossary

Terms that come up constantly and trip up new players.

- **De jure** — the *legal* owner of a title (held by the bearer's clan tree even
  if their kingdom doesn't physically control the fief). Inherited via
  `FeudalContract.Inheritance`.
- **De facto** — the *actual* current controller, derived from settlement
  ownership. A hero can hold a title de jure while another holds it de facto;
  this is a casus belli.
- **Demesne** — the personal fiefs and estates a clan directly administers
  (vs. fiefs held by sub-vassals in the same title tree).
- **Vassal** — a clan that has sworn to a liege. In BK, vassalage is per-title
  not per-kingdom: you can hold a county under one duke and a barony under
  another.
- **Liege** — the higher-tier title-holder you are sworn to.
- **Contract** — the bundle of laws on a title (`FeudalContract`): government
  type, succession rule, gender law, inheritance, plus 0–N demesne laws.
- **Demesne law** — toggleable rule on a title (e.g., *Slave Trade Allowed*,
  *Imperial Coronation Required*). Defined in `DefaultDemesneLaws.cs`.
- **Government** — Imperial / Feudal / Tribal / Republic / Theocratic. Affects
  vassal limits, taxation cap, and which decisions are available.
- **Succession** — Hereditary Monarchy / Elective Monarchy / Republican Election
  / Theocratic. Determines how the title passes on holder death.
- **Gender law** — Agnatic (male only), Cognatic (eldest regardless of gender),
  Agnatic-Cognatic (male-preferred), Enatic (female only).
- **Piety** — religious-stat counterpart to influence. Spent on rites,
  blessings, conversions; gained from prayer, sacrifices, holy-day observance.
- **Fervor** — a faith's strength as a campaign-wide pool (driven by adherent
  count, holy site control, doctrines).
- **Stance** — one religion's attitude toward another: `Tolerated`,
  `Untolerated`, `Hostile`. Affects relations, conversion costs, war justification.
- **Doctrine** — an unlockable, sometimes mutually exclusive tenet of a faith
  (e.g., *Astrology* boosts ship speed; *Reavers* enables raid bonuses).
- **Lifestyle** — paired-skill specialization gating perks (Cataphract =
  Riding+Polearm, Outlaw = Roguery+Crossbow, etc.). Tracked in `EducationData`.
- **Scholarship** — flag set when a hero has any of four research perks
  (ScholarshipMechanic / Accountant / NaturalScientist / Treasurer). Required
  to enter the Scholar lifestyle.
- **Notable** — a non-noble settlement personality (Rural Notable, Headman,
  Gang Leader, Preacher, Merchant). Drives recruitment, quests, prosperity.
- **Gentry** — minor landed family, below clan tier 1. Often a notable's
  promoted relatives. Can be sponsored into a vassal clan.
- **Knight (BK sense)** — a hero granted knighthood by a clan, becoming a
  tier-1 vassal clan with a fief grant and oath. Distinct from vanilla "knight"
  troop tier.
- **Estate** — a sub-property inside a village owned by a clan, with tenants,
  food/gold output, and an inheritance line. Clans can hold multiple per village.
- **Council** — clan-level officer board: Marshal, Steward, Chancellor,
  Spymaster, Court Physician. Each costs influence/gold and grants stat effects.
- **Peerage** — kingdom-level political tier (Full Peer / Partial Peer /
  No Peer). Determines voting rights on kingdom decisions and policy changes.
- **Interest group** — sub-faction within a kingdom (radicals, moderates,
  zealots, traders). Issues demands; can defect.
- **Demand** — formal pressure from an interest group: `ClaimantDemand`,
  `CouncilPositionDemand`, `PolicyChangeDemand`, `SecessionDemand`, `TitleDemand`.
- **Claim** — a hero's pressed right to a title, aging into a justified war
  basis. Resolved at `BKTitleBehavior` succession ticks.
- **Custom troop** — player-designed mercenary unit (culture, equipment,
  skills, formation). 3× vanilla wage by design.

---

## 14. Major content registries

These `Default*` classes are the canonical content lists. If a player asks
"what lifestyles exist" or "what doctrines exist", point them here.

### Lifestyles (`Managers/Education/Lifestyles/DefaultLifestyles.cs`)

Active ones (have full perk trees and skill-progression hooks):

| Lifestyle | Skills | Theme |
|---|---|---|
| Fian | Bow + Athletics | Battanian woodland skirmisher |
| Cataphract | Riding + Polearm | Heavy lancer cavalry |
| August | Charm + Leadership | Imperial statesman |
| SiegeEngineer | Engineering + Crossbow | Siege specialist |
| CivilAdministrator | Steward + Trade | Realm bureaucrat |
| Caravaneer | Trade + Scouting | Long-distance trader |
| Artisan | Crafting + Smithing | Master crafter |
| Outlaw | Roguery + Crossbow | Bandit chief |
| Mercenary | Two-Handed + Tactics | Sellsword captain |
| Kheshig | Bow + Riding | Khuzait elite horse-archer |
| Varyag | One-Handed + Two-Handed | Sturgian raider |
| Gladiator | Athletics + One-Handed | Arena fighter |
| Ritter | Polearm + Athletics | Vlandian heavy knight |
| Jawwal | Throwing + Riding | Aserai light cavalry |
| Commander | Leadership + Tactics | Battlefield commander |

Orphaned (declared but no perk wiring): `Courtier`, `Scholar`, `Diplomat`.

### Doctrines (`Managers/Institutions/Religions/Doctrines/DefaultDoctrines.cs`)

Selected high-impact ones:

| Doctrine | Effect |
|---|---|
| Astrology | Sea travel ~25% faster (read by `BKShippingBehavior`) |
| Tolerant | Reduces hostile-stance penalties; eases conversion |
| Esotericism | Bonus to scholar lifestyle XP, hidden rites |
| Reavers | Raid output and morale bonus |
| Warlike | Combat XP and morale boosts |
| Pacifism | Morale penalty in offensive war, peace influence boost |
| Sacrifice | Human sacrifice rite; piety surge, relation hits |
| HeathenTax | Surcharge on out-of-faith notables in your settlements |
| Childbirth | Increased fertility for adherent clans |
| Pastoralism | Herd animal bonuses in villages |
| Druidism / Animism | Tribal-only nature worship doctrines |

### Faiths and religions

Faiths and religions are loaded from XML (`_Module/ModuleData/`) and registered
through `DefaultFaiths.ModAdditions` rather than hard-coded. Faith subtypes:
`MonotheisticFaith`, `PolytheisticFaith`, `HenotheisticFaith`, `DualisticFaith`.
Each defines: divinities, taboos, holy days, allowed/forbidden marriages,
funeral rite, baseline stance map.

### Demesne laws (`Managers/Titles/Laws/DefaultDemesneLaws.cs`)

Toggleable on a title's `FeudalContract`. Examples: tax exemption tiers,
slave trade allowed, serfdom intensity, militia draft policy, religious
tolerance, war booty rules.

### Government / succession / inheritance (`Managers/Titles/Governments/`)

- `DefaultGovernments.cs` — Imperial, Feudal, Tribal, Republic, Theocratic.
- `DefaultSuccessions.cs` — HereditaryMonarchy, ElectiveMonarchy, Republic, Theocratic.
- `DefaultInheritances.cs` — Primogeniture, Ultimogeniture, Seniority, ElectiveMonarchy.
- `DefaultGenderLaws.cs` — Agnatic, Cognatic, AgnaticCognatic, Enatic.

### Policies (`Managers/Policies/`)

Per-settlement player-set toggles:

- `BKTaxPolicy` — Standard / High / Low / Exempted.
- `BKMilitiaPolicy` — Balanced / Melee / Ranged.
- `BKDraftPolicy` — Standard / High Draft / No Draft.
- `BKGarrisonPolicy` — Standard / Reinforce / Disband.
- `BKWorkforcePolicy` — Construction / Production / Martial.
- `BKCriminalPolicy` — Lenient / Standard / Strict.

---

## 15. Player-facing how-to

### How do I claim a title?

1. Acquire a claim — by inheritance (parent dies and passes it), marriage
   (spouse's claim transfers per gender law), grant (current holder grants
   it to you), or fabrication (a council Chancellor of high enough skill can
   forge a claim over time).
2. Press the claim — declare war using the claim as casus belli, win, take
   the fief. Or, if you're already the de facto holder, the claim auto-resolves
   on the next succession tick.
3. Be granted — your liege can grant a vacant title of theirs to you for
   influence + gold.

### How do I start my own kingdom?

Two BK paths beyond vanilla:

- **Found a culture-specific empire goal** (`Managers/Goals/EmpireGoal.cs`) —
  hold N counties of one culture, complete the foundation rite, pay the
  influence cost.
- **Convert an existing kingdom you took over** — usurp the kingdom-tier
  title via claim/war/election, then use the council to issue a new contract.

### How do I become a vassal?

Approach a kingdom's ruler. They'll offer a title (typically a barony or
county) under their crown. Accepting binds your clan via `FeudalContract`'s
duties — you owe taxes, levies, and council attendance; you receive military
protection and trade access.

### How do religion conversion and rites work?

- **Personal conversion** — visit a clergyman (preacher in tavern, bishop in
  capital), spend piety + gold, take an oath. Faith change applies on next
  daily tick.
- **Settlement conversion** — assigned clergy preach over time, raising
  adherent count; requires the faith to be tolerated by the realm contract
  or the demesne law to permit conversion.
- **Rites** — listed per faith (`Faiths/Rites/`). Each costs piety, has
  cooldown, and a triggering condition (battle won, settlement taken, hero
  married, etc.). Effects range from troop morale to permanent traits.

### How do I get an estate?

- **Buy** — at the village screen, "Manage estates" → purchase the estate
  from the current owner. Cost scales with land size and tenant count.
- **Grant** — your liege can grant you a vacant estate.
- **Inherit** — passes via the estate's inheritance line on owner death.
- **Seize** — if a vassal's estate becomes claimable (e.g., owner died
  heirless or committed treason), the liege can seize it.

### How do I hire a custom mercenary unit?

`Behaviours/Mercenary/CustomTroop.cs` flow:

1. Open the BK Mercenary screen.
2. Pick a culture (sets accent and naming pool).
3. Pick a formation class (Infantry, Ranged, Cavalry, HorseArcher).
4. Build the equipment roster from your inventory or purchases.
5. Pay hire price + ongoing daily wage = 3× vanilla equivalent.
6. The unit is added to your clan's recruitable pool.

### How do I appoint council members?

Open the clan/court screen. For each position, you see candidates (skill
ratings + relation + traits). Picking costs influence and a relation
adjustment. Demoting also costs influence and applies a relation hit.
Kingdom-level council (royal Marshal etc.) is only available to the
ruling clan and uses kingdom influence.

### What does a Marshal actually do?

Reduces party wage costs across the realm, boosts levy size from settlements,
and slightly improves army cohesion. The exact numbers come from
`Models/Vanilla/BKPartyWageModel.cs` and the army cohesion model. Marshal
position gates into council-tier perks.

### How do I make money?

Ranked roughly by yield per hour of attention:

1. **Workshops + estates** combined in the same town/village pair.
2. **Caravans** (vanilla + BK trade modifiers).
3. **Tournament prize riding** (early game).
4. **Mercenary contracts** to a wealthy kingdom (mid).
5. **Raiding** under the *Reavers* doctrine (high relation cost).
6. **Custom mercenary contracts** sold to AI clans (late, complex).

---

## 16. Per-system FAQ — exact answers

### Population

**Q: Why is my settlement losing population?**
Check `PopulationData` in the settlement UI — common causes: food shortage
(check granary + village output), high tax policy (drives serfs to flee),
recent raid (halves growth for ~30 days), failed siege defense, slave
overrun (slave class cap exceeded triggers riots).

**Q: How do classes transition?**
Daily ticks in `BKSettlementBehavior` evaluate per-settlement: serfs can
become craftsmen if there's craftsman housing demand, slaves can be freed
into serfs by demesne law, craftsmen can be promoted to nobles by the
gentry pipeline.

**Q: What does "settlement issue" mean?**
A `PopulationData` issue is a flagged condition (food shortage, slave
overrun, mood collapse). Resolve it via the relevant policy lever or by
addressing the underlying cause.

### Titles

**Q: My heir is the wrong person — why?**
Inheritance order is decided by `FeudalContract.Inheritance` and
`GenderLaw`. Primogeniture + Cognatic = eldest child. Primogeniture +
Agnatic = eldest male; if no males, bypasses to brother before daughter.
Seniority = oldest living clan member by birth date.

**Q: Can I change a title's contract?**
Yes — `BKContractChangeDecision` (`Managers/Kingdoms/Contract/`). Costs
influence, takes time, can be vetoed by vassals via the demand system.

**Q: What's the difference between Empire and Kingdom?**
Empire (tier 1) is a multi-kingdom super-realm (Western/Northern/Southern
Empire in vanilla). Kingdom (tier 2) is the realm tier most factions sit at.
Empire-tier titles unlock at `EmpireGoal` completion.

### Religion

**Q: How do I see my hero's faith?**
Character → BK Religion tab. Shows current faith, piety, last rite, doctrine
votes, and conversion progress if any.

**Q: Can my whole kingdom share one faith?**
Yes via the kingdom contract's religion clause + active conversion. Settling
mixed-faith populations under one ruler causes notable relation hits unless
the doctrine is `Tolerant` or the local stance is `Tolerated`.

**Q: What happens if I marry across faiths?**
Allowed if both faiths' marriage rules permit it. Hostile-stance pairings
are banned. Tolerated-stance pairings carry a piety penalty for both
spouses on marriage day.

### Education

**Q: How do I pick a lifestyle?**
Character → BK Education tab → Lifestyle dropdown. Locked once chosen until
that lifestyle is fully completed (5 perk tiers) or a respec rite is
performed (rare, very expensive).

**Q: Why is my lifestyle progress so slow?**
Both linked skills must be exercised — only the *lower* of the two
contributes per tick. Pure cavalry play barely advances a Cataphract
because Polearm doesn't tick when you don't melee.

**Q: Where do I get books?**
Tavern book seller (spawned by `BKEducationBehavior`) or as quest reward.
A book seller exists in every cultural capital tavern as long as
`bookSellers.Count >= DesiredSellerCount()`.

### Diplomacy & demands

**Q: An interest group is demanding something — what happens if I refuse?**
Each refusal raises grievance level. Hitting max grievance triggers
escalation: defection, secession war, or claimant uprising depending on
group type.

**Q: What's "support on decision" relation?**
When you side with a clan in a kingdom decision, they gain a relation
modifier with you (+8 to +25 by support strength) for 5 years
(`BKRelationsBehavior::OnKingdomDecisionConcluded`).

### Shipping & travel

**Q: My caravan went on a ship — bug?**
No — `BKShippingBehavior::AfterSettlementEntered` auto-boards caravans whose
destination is on a known shipping lane. Unboard via the caravan menu in
that port.

**Q: Why is my ship taking forever?**
Travel time = distance / 75 (or /60 with the *Astrology* doctrine). Distances
on the campaign map are large; cross-Calradia trips take 4–6 days.

### Mercenaries & combat

**Q: Custom troop daily wage seems insane.**
By design, 3× vanilla. The hire price is also higher. They're meant to be
elite fillers, not core army composition.

**Q: Where do BK perks apply?**
Most BK perks use vanilla `EffectIncrementType.AddFactor` — applied at
model evaluation time wherever vanilla reads the same skill effect. After
the 1.3.x balance pass, no whole-number perk values remain.

---

## 17. Edge cases & frequent confusions

- **"My title disappeared"** — usually inherited by an heir on death you
  didn't notice, or absorbed into a higher-tier title via succession. Check
  `BKTitleBehavior` event log in the encyclopedia → titles tab.
- **"BK menu is empty"** — feature was disabled in the MCM `Settings/`
  options. Re-enable and reload save.
- **"Crash on entering Nord settlement"** — only on pre-fix builds. The fork
  ships `Patches/NordCompatPatches.cs` to null-guard. Update the mod.
- **"My lifestyle locked at scholar"** — the Scholar lifestyle requires the
  scholarship gate (any of ScholarshipMechanic/Accountant/NaturalScientist
  /Treasurer). Without it, progress doesn't tick.
- **"Council Marshal didn't reduce wages"** — reduction is multiplicative on
  `BKPartyWageModel`. Other modifiers (custom troop, doctrine) can dominate.
  Check the wage tooltip breakdown in the party UI.
- **"Estate showing zero income"** — daily ticks accumulate; income posts
  weekly. Or the estate has no tenants — check `EstateComponent` data.
- **"Religion fervor dropping every day"** — fervor decays without active
  rites and adherent growth. Run the holy-day rite or take an *active*
  doctrine.
- **"Can't change demesne law"** — locked behind contract change cooldown
  (~1 in-game year) and minimum loyalty / authority gates.

---

## 18. Cross-mod compatibility

- **War Sails / NavalDLC** — natively supported. Nord titles, succession,
  language, null guards all built in.
- **Diplomacy mod** — overlapping kingdom-decision systems. BK's
  `KingdomDiplomacy` adds layers; usually compatible, but `Diplomacy`'s
  custom alliance UI shows alongside BK's.
- **Custom Spawns / Calradia at War** — compatible; BK doesn't touch
  spawn templates.
- **Realistic Battle Mod** — combat-side compat fine. RBM's damage model
  doesn't conflict with BK's economy/title overlay.
- **Banner Color Persistence / cosmetic mods** — no interaction.
- **Stand-alone mods that override `XxxModel`** — last-loaded-wins. Load BK
  *after* combat overhaul mods if you want BK's economy/wage models to apply.

---

## 19. Save game safety

- Saves are version-tagged. Loading a save from an older BK build runs
  `SaveDefiner` migration where defined; otherwise old fields keep their
  values and new fields lazy-init.
- Removing BK from an active save is **not** safe — references to BK objects
  (titles, estates, custom troops) become orphaned.
- Updating BK on an active save is generally safe within a minor version.
  Major versions (e.g., 1.2 → 1.3 fork) may require restart.

---

## 20. Reporting issues

For a useful crash/issue report include:

1. `Crashes/mostrecentcrash.htm` (game-generated) — full stack and module list.
2. `rgl_log.txt` (last few hundred lines) — BK warns are tagged `[BK]`.
3. Save file name, BK version, and War Sails on/off.
4. Steps to reproduce, ideally from a fresh save.

Common report-killers (don't bother reporting these — known and excluded):

- "BUTR Harmony analyzer warnings" — 149 false positives, verified.
- "Compile warnings about obsolete types" — 1.3.x deprecations not yet
  removed, harmless.

---

## 21. Mod compatibility (the in-depth answer)

§18 above is the short version. This section is what the RAG bot should
quote when players ask "does BK work with X?".

### 21.1 How BK detects other mods

`BannerKings/Utils/ModCompat.cs` exposes one method and a small set of
properties:

```csharp
ModCompat.IsLoaded(string moduleId, string assemblyName = null)
ModCompat.DiplomacyMod
ModCompat.ImprovedGarrisons
ModCompat.RecruitEverywhere
ModCompat.MarryAnyone
ModCompat.BuyLandAtVillages
ModCompat.RealisticBattleMod
```

Detection tries `TaleWorlds.ModuleManager.ModuleInfo.GetModules()` (via
reflection so missing API surfaces don't crash BK), then falls back to
scanning `AppDomain.CurrentDomain.GetAssemblies()`. Results are cached.
Cost is a single dictionary hit per check after the first call.

### 21.2 What BK skips when each mod is present

| Mod | What BK yields |
|---|---|
| **Diplomacy** | (1) Skips registering `BKDiplomacyModel` in `Main.cs::OnGameStart` so Diplomacy's diplomacy model wins. (2) Patches on `KingdomDiplomacyVM` (`CalculateWarSupport`, `GetIsProposingWarEnabledWithReason`, `OnDeclareWar`) become no-ops, returning `true` from the prefix to let vanilla / Diplomacy's flow run. (3) `ConsiderWar` prefix on `KingdomDecisionProposalBehavior` lets vanilla through. BK still tracks its own pacts/casus belli internally for title/claim logic, just doesn't compete for the kingdom-decision UI. |
| **ImprovedGarrisons** | Skips the `UpdateClanSettlementAutoRecruitment` prefix on `ClanVariablesCampaignBehavior`, so IG can manage `Town.GarrisonAutoRecruitmentIsEnabled` and recruit composition without BK overwriting it. BK's `BKGarrisonModel` is also not registered. The patrol-party feature (`HandleGarrison` / `GarrisonPartyComponent`) still draws troops from the garrison; toggle it off in MCM if you don't want patrols depleting an IG-managed roster. |
| **RecruitEverywhere** | Skips `RecruitVolunteersFromNotable` prefix and `GetVolunteerTroopsOfHeroForRecruitment` prefix so RE owns volunteer pool semantics. |
| **MarryAnyone** | Skips registering `BKMarriageModel` so MA's relaxed restrictions apply. BK's title/dowry calculations still run via the config-level model when a marriage actually happens. |
| **BuyLandAtVillages** | No code skip (BK's estate system is independent). Documented overlap only — both can coexist, but the player can hold both BK estates and BLAV land in the same village, which is confusing. Pick one or the other in practice. |
| **RealisticBattleMod (RBM)** | No code skip. RBM patches `AgentDamageModel` etc.; load order in `SubModule.xml` is `LoadAfterThis` so RBM wins on combat. BK keeps its campaign-side combat XP / battle reward / battle simulation logic. |

### 21.3 Mods expected to be compatible without code changes

These touch different layers and have no overlap with BK's domain:

- **RTSCommand** — mission-time camera / agent commands. BK is campaign-time.
- **Family Tree** — read-only over `Hero` data.
- **Settlement Icons / Better Time / Realistic Weather** — UI / time / weather only.
- **Open Source Armory / Saddles / Banner Color Persistence** — items / cosmetic.
- **Custom Spawns / Calradia at War** — spawn templates; BK's `BKBanditBehavior` doesn't override templates.
- **Serve as Soldier (SAS)** — soldier-mode flow on a different code path.
- **BetterExceptionWindow / Adjustable Troop Selection** — error UI / troop picker UI.

### 21.4 Mods that need manual care

- **Distinguished Service** — both touch `MapEvent` / troop XP. No detected
  crash, but XP rewards may stack. If you don't want stacked bonuses,
  disable BK's `BKCombatXpModel` via MCM (added knob if present in your
  build).
- **Bannerlord Tweaks** — patches widely; usually fine if loaded after BK.
  If a tweak silently reverts a BK behavior, it loaded later — check
  launcher order.
- **Heroes Must Die** — both listen to hero death. BK's `BKTitleBehavior`
  inheritance may run before HMD's logic; if title succession looks
  wrong with HMD, set HMD to load after BK.
- **Calradia Expanded / CE Kingdoms** — adds new factions that need BK
  title data, same problem as Nords. Currently only Nord null-guards
  exist (`Patches/NordCompatPatches.cs`). New-faction shims would have
  to be authored per faction.
- **Detailed Character Creation** — overlaps with BK's `BKCampaignStartBehavior`
  patches. Test the prologue thoroughly when both are installed.

### 21.5 Confirmed incompatible

Nothing currently. If a mod-vs-BK interaction crashes consistently, file
an issue with the crash HTM and we'll add either a detection skip or an
incompatibility entry to `SubModule.xml`.

### 21.6 Recommended load order

`SubModule.xml` declares `LoadAfterThis` for the well-known cooperators.
With those hints the BUTR launcher places BK before:

```
BannerKings
  ↓ (BK loads first, runs its detection)
Diplomacy / ImprovedGarrisons / RecruitEverywhere / MarryAnyone /
BuyLandAtVillages / RBMCombat
  ↓ (these load next; BK has already chosen what to skip)
Bannerlord Tweaks / cosmetic mods / etc.
```

### 21.7 Adding a new compat shim

1. Add the module id and assembly name as constants in `ModCompat.cs`.
2. Add a convenience property (`public static bool MyMod => IsLoaded(...)`).
3. Add `if (ModCompat.MyMod) return true;` at the prefix entry of any
   patch that would compete, or wrap the relevant `AddModel` /
   `AddBehavior` call in `if (!ModCompat.MyMod) ...` in `Main.cs`.
4. Add a `<DependedModuleMetadata id="MyMod" order="LoadAfterThis"
   optional="true" />` line in `SubModule.xml`.
5. Add a row to §21.2 of this wiki.

---

*Generated 2026-04-27 from a code survey of the 1.3.x fork. Sections 1–12
cover code architecture; 13–21 cover gameplay, player questions, and mod
compatibility, and are the primary RAG retrieval surface for player-facing
/ask queries. Update when major systems change.*
