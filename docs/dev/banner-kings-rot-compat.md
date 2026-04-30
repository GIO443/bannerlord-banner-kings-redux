# Writing a Banner Kings ↔ Realm of Thrones compatibility patch

This document is for a **third-party patch-mod author** who wants to make
Banner Kings — Redux work against the Realm of Thrones (ROT) total
conversion. It is not a Banner Kings (BK) internals reference; it is a
focused walkthrough of every place where vanilla BK assumes Calradian
data, and the public hooks you can use to provide ROT-equivalent data.

The intended end-state: a separate module (e.g. `BannerKings.RealmOfThrones`)
that depends on both BK and ROT, registers ROT-specific content into BK's
extension points, and harmony-patches around the few BK code paths that
hardcode Calradian StringIds.

> Maintainer note for the BK side: the items called out as "BK should fix"
> below are crashes-on-init that any patch mod will trip over. We're happy
> to take small null-guard PRs that don't change Calradia behaviour. Items
> called out as "Patch mod authors a)" or "Patch mod registers" are work
> the patch is expected to do — BK isn't going to ship ROT data.

---

## 1. Mental model

BK's content is split across three layers, each of which the patch mod
has to address differently:

1. **`DefaultTypeInitializer<T>` registries** — 59 of them across the
   codebase (laws, lifestyles, schemes, dynasties, council positions,
   etc.). Each is a static singleton with `All` and a protected
   `ModAdditions` list. **The patch mod adds objects via `AddObject`
   from its `OnGameStart` / `OnGameStarted` hook.** No XML, all code.

2. **`titles.xml`-style XML in `_Module/ModuleData/`** — declarative
   feudal hierarchy (kingdom → duchy → county → barony → lordship)
   referencing settlements and lord characters by StringId. **The patch
   mod ships its own `titles.xml` in its own ModuleData folder.** BL's
   XML loader will merge it.

3. **Hardcoded StringId lookups in BK code** — `Settlement.All.First(x =>
   x.StringId == "town_V8")`, `Helpers.GetCulture("nord")`, etc.
   **These will throw `InvalidOperationException` against ROT's map.**
   The patch mod can't fix these from outside; either BK ships
   `FirstOrDefault` null-guards (preferred), or the patch mod harmony-
   patches the offending init methods.

The smallest viable patch mod is layer 1 + layer 2 + a handful of harmony
prefixes for layer 3. Approximate scope: a competent BL modder can
ship a Tier-1-functional ("doesn't crash, BK menus appear, titles
inheritable") patch in 2-4 weeks; a Tier-2 ("BK gameplay is balanced for
ROT") patch is 2-3 months and is ongoing maintenance.

---

## 2. Required: ROT module manifest

Your `SubModule.xml` must depend on both BK Redux and ROT, and load
*after* both. BK's culture/settlement detection runs at `OnGameStart`,
so as long as ROT's data is loaded first, BK gets the ROT cultures and
settlements visible via `MBObjectManager`.

```xml
<DependedModuleMetadatas>
    <DependedModuleMetadata id="BannerKings.Redux" order="LoadBeforeThis" />
    <DependedModuleMetadata id="realmofthrones.core" order="LoadBeforeThis" />
</DependedModuleMetadatas>
```

Use whichever ROT module id is canonical (the crash log we saw used
`realmofthrones.core`).

---

## 3. Layer 1 — `DefaultTypeInitializer<T>` registrations

The pattern, used 59 times across the codebase:

```csharp
public abstract class DefaultTypeInitializer<TSelf, TObj> where TObj : MBObjectBase
{
    public static TSelf Instance { get; }
    public abstract IEnumerable<TObj> All { get; }       // BK's defaults + ModAdditions
    protected List<TObj> ModAdditions { get; }
    public void AddObject(TObj toAdd);                   // patch mod calls this
    public abstract void Initialize();                   // BK calls this
}
```

The full list of registries the patch mod will likely want to extend or
inspect is in `BannerKings/**/Default*.cs`. The high-impact ones:

| Registry | What to add for ROT | When to call AddObject |
|---|---|---|
| `DefaultLifestyles` | ROT-flavored paired-skill lifestyles for each major house — equivalents to Cataphract/Fian/Jawwal/etc. with the right culture filter. | `OnGameStart` (your behaviour's hook) |
| `DefaultLanguages` | One language per ROT culture, with intelligibility relations (e.g. Old Tongue partially intelligible with Common Tongue). Mirror what BK does for Battanian/Sturgian/Nordic. | `OnGameStart` |
| `DefaultDemesneLaws` | At minimum: a slavery family for ROT cultures. BK's slavery laws are culture-keyed (`SlaveryNord`, `SlaveryAserai`, etc.). ROT cultures need their own entries or BK's default-no-slavery branch will apply. | `OnGameStart` |
| `DefaultSuccessions` | ROT cultures need a default succession assigned per kingdom. If you don't, BK falls back to `HereditaryMonarchy` which is fine but generic. | `OnGameStart` |
| `DefaultGovernments` | If ROT has government types BK doesn't model (e.g. a magical theocracy?), add them here. Otherwise BK's existing 5 (Imperial/Feudal/Tribal/Republic/Theocratic) are usually sufficient. | `OnGameStart` |
| `DefaultStartOptions` | Custom campaign-start options for ROT-flavoured player starts. Optional. | `OnGameStart` |
| `DefaultRadicalGroups` / `DefaultInterestGroup` | ROT-flavoured kingdom factions. Default groups work but feel out of place. | `OnGameStart` |
| `DefaultTitleNames` | Per-culture title-tier name strings (King → Aerlinn, Duke → Eorlsmaer, etc. for ROT cultures). Without this BK falls back to generic English titles. | `OnGameStart` |
| `DefaultPopulationNames` | Per-culture serf/craftsman/noble flavour names for the BK settlement panel. | `OnGameStart` |
| `DefaultDynasties` / `DefaultLegacies` | ROT noble houses as BK dynasties; gives the dynasty/legacy UI proper data. | `OnGameStart` |
| `DefaultFiefHeritage` | Per-fief heritage entries for the cultural-standing system. | `OnGameStart` |
| `DefaultMarketGroups` | ROT-region-specific market-group memberships if you want differentiated trade economies. | `OnGameStart` |

**Pattern for adding an object:**

```csharp
public class ROTBKContent : CampaignBehaviorBase
{
    public override void RegisterEvents()
    {
        CampaignEvents.OnGameLoadedEvent.AddNonSerializedListener(this, OnGameLoaded);
        CampaignEvents.OnNewGameCreatedEvent.AddNonSerializedListener(this, OnNewGame);
    }
    public override void SyncData(IDataStore _) { }

    void OnNewGame(CampaignGameStarter starter) => Register();
    void OnGameLoaded(CampaignGameStarter starter) => Register();

    static bool _registered;
    void Register()
    {
        if (_registered) return; _registered = true;

        var westeros = MBObjectManager.Instance.GetObject<CultureObject>("westeros_north");
        if (westeros != null)
        {
            var stark = new Lifestyle("StarkLord");
            stark.Initialize(/* skill pair, perks, culture filter, lore */);
            DefaultLifestyles.Instance.AddObject(stark);
        }
        // ... more
    }
}
```

Register as a behavior in your `Main.cs` via
`campaignStarter.AddBehavior(new ROTBKContent())`.

---

## 4. Layer 2 — `titles.xml`

BK's feudal hierarchy is declared in `BannerKings/_Module/ModuleData/titles.xml`.
Format (real example, abbreviated):

```xml
<base type="string">
    <titles autoGenerate="true">
        <kingdom faction="vlandia"
                 government="Tribal"
                 succession="WilundingElective"
                 inheritance="Seniority"
                 genderLaw="Agnatic"
                 deJure="lord_4_1">
            <duchy name="{=kS1GtIuD}Sargotha" deJure="lord_4_1">
                <county settlement="town_V1" deJure="lord_4_1">
                    <barony settlement="castle_V7" deJure="lord_4_1" />
                </county>
            </duchy>
        </kingdom>
        <!-- more kingdoms -->
    </titles>
</base>
```

**Patch mod ships its own `titles.xml`**: place at
`<your-module>/ModuleData/titles.xml`. BL's XML loader merges it with
BK's at runtime via the `<XmlNode id="..." path="titles"/>` registration
in BK's `SubModule.xml`. The merge is a simple concatenation — your
`<kingdom>` entries appear alongside BK's. **Don't replicate BK's
Calradia entries in your file** (that creates duplicates). Just declare
your ROT kingdoms.

What every attribute means:

| Attribute | Acceptable values | Notes |
|---|---|---|
| `faction` | `Kingdom.StringId` | Must match a real ROT kingdom at game start |
| `government` | `Imperial` / `Feudal` / `Tribal` / `Republic` / `Theocratic` | Or any custom government you registered via `DefaultGovernments` |
| `succession` | StringId from `DefaultSuccessions` | E.g. `HereditaryMonarchy`, `WilundingElective`, etc. — see `DefaultSuccessions.cs` for the full set |
| `inheritance` | `Primogeniture` / `Seniority` | Affects who inherits when a holder dies |
| `genderLaw` | `Agnatic` / `Cognatic` / `AgnaticCognatic` / `Enatic` | |
| `deJure` | `Hero.StringId` | The starting de jure holder. Must be a real hero in ROT's data |
| `<county settlement="…">` / `<barony settlement="…">` | `Settlement.StringId` | Must exist in ROT's settlements |

**Critical**: every StringId you reference (settlements, lords, factions)
must exist in ROT's data when titles.xml loads. A typo or stale ID
becomes a `NullReferenceException` deep inside BK's title loader. There
is currently no skip-on-missing fallback — BK assumes Calradia's
hardcoded settlements all exist.

**Authoring scale**: 7-8 ROT kingdoms × ~3-4 duchies × ~3 counties × ~2
baronies = 200+ XML entries. Mostly mechanical but needs ROT lore
fluency.

---

## 5. Layer 3 — hardcoded Calradian StringIds in BK code

These are the crashes a patch mod **cannot fix from outside** without
either BK shipping null-guards or the patch mod harmony-patching the
offending methods. Each is small.

### 5.1 `DefaultShippingLanes.Initialize` *(patch mod must Harmony-replace OR wait for BK fix)*

Location: `BannerKings/Managers/Shipping/DefaultShippingLanes.cs:50`. The
init body is:

```csharp
var laconisPorts = new List<Settlement>()
{
    Settlement.All.First(x => x.StringId == "town_S4"),     // throws on ROT
    Settlement.All.First(x => x.StringId == "town_EN2"),    // throws on ROT
    // ...
};
Laconis.Initialize(/* TextObject */, /* desc */, laconisPorts);
```

`First` throws `InvalidOperationException` immediately on any missing
ID, and in ROT none of these IDs exist.

**Best fix (BK side, ideal):** change every `Settlement.All.First(...)`
to `Settlement.All.FirstOrDefault(...)`, build the port list with a
null-skip, and skip the lane entirely if it has fewer than 2 ports.
~30 lines.

**Patch-mod workaround until BK ships that:** Harmony-prefix
`DefaultShippingLanes.Initialize` with `return false;` to skip BK's
init, then after that runs, register your own ROT shipping lanes via
`DefaultShippingLanes.Instance.AddObject(...)` for each ROT lane. There
is no public Lane constructor, but `ShippingLane.Initialize(name, desc,
ports, isRiver, culture)` is public — instantiate via reflection or
expose via a public `BK.MakeLane(...)` factory if BK is willing to add
one.

The graph topology (`ShippingGraph`) builds lazily off of
`DefaultShippingLanes.All` so once your lanes are registered, the rest
of BK's shipping system works against ROT's geography.

### 5.2 `Helpers.GetCulture(string id)` *(callers must null-check; usually fine)*

Location: `BannerKings/Utils/Helpers.cs:413`.

```csharp
public static CultureObject GetCulture(string id)
{
    return MBObjectManager.Instance.GetObjectTypeList<CultureObject>()
        .FirstOrDefault(x => x.StringId == id);
}
```

This already returns `null` on missing — no crash here. The risk is that
**callers don't null-check the result** before dereferencing it. Common
sites:

- `DefaultShippingLanes.Initialize` passes `Helpers.GetCulture("nord")`
  to `Junme.Initialize(...)` — fine because the null is just stored.
- `BKBanditBehavior.cs` and several others use `GetCulture("empire")`
  in conditionals; usually they short-circuit cleanly on null.

Likely fine for ROT compat. If you see culture-related NREs at runtime,
grep `GetCulture("` in BK source for the offending caller.

### 5.3 Title-XML lord StringIds *(patch mod ships its own XML)*

`titles.xml` references heroes by StringId (`deJure="lord_4_1"`). If a
ROT kingdom is declared in your titles.xml but `lord_4_1` doesn't exist
in ROT, BK's title loader hits NRE.

**Patch mod's job**: only reference heroes/settlements that exist in
ROT's data. Don't copy BK's Calradia entries into your XML.

### 5.4 `BKShippingBehavior.OnWeeklyTick` notable-spawn loop

Location: `BannerKings/Behaviours/Shipping/BKShippingBehavior.cs`.

For each shipping lane, BK spawns culture-typed merchant notables in
the lane's ports. The loop calls `Helpers.GetCulture(lane.Culture)` and
then iterates the culture's `NotableTemplates`. If lane culture is
unset (which is allowed — only Junme and Norden set a culture today),
the loop short-circuits cleanly. ROT lanes you register can leave
`culture` null and skip this. Or set culture and provide notable
templates — BK uses them as-is.

### 5.5 Bandit faction references

`Clan.BanditFactions` is referenced in `BKBanditBehavior` and the
`spawn_bandit_hero` cheat. ROT replaces bandit factions with custom ones;
as long as ROT registers them under `Clan.BanditFactions`, no change
needed.

### 5.6 Religion stack *(stripped — ignore)*

BK Redux has stripped religion content from the player-facing surface;
the framework is still in code but not wired to gameplay. Ignore for ROT.

---

## 6. Detection: how should BK detect ROT?

Add a property to `BannerKings/Utils/ModCompat.cs`:

```csharp
public const string ROTId = "realmofthrones.core";   // or canonical ROT id
public const string ROTAsm = "ROT";

public static bool RealmOfThrones => IsLoaded(ROTId, ROTAsm);
```

The patch mod doesn't need this — but if BK wants to gate any of its
own features on ROT presence (e.g. skip Nord-flavored cultural-standing
entries that reference Calradian-only cultures), it'd use this property
the same way `WarSails`, `Diplomacy`, etc. are used.

In `BannerKings/_Module/SubModule.xml`, add:

```xml
<DependedModuleMetadata id="realmofthrones.core" order="LoadAfterThis" optional="true" />
```

So load order is correct when both are installed.

---

## 7. Save-game compatibility

- BK adds class definitions in `SaveDefiner.cs` with explicit IDs (1000+
  range). Your patch mod can register additional `[SaveableClass]`
  types in its own SaveDefiner using IDs in a different range
  (≥10000) so there's no collision.
- BK's persisted state references `Hero` and `Settlement` by reference,
  not StringId — so a patch mod that adds heroes/settlements via ROT
  doesn't break BK's persisted state structurally. But a save started
  on Calradia will not load against ROT's geography (the references
  resolve to nothing). Always start fresh saves when switching base
  modules.

---

## 8. Suggested patch-mod skeleton

```
RealmOfThrones.BannerKings/
├── _Module/
│   ├── SubModule.xml                   ← depends on both BK + ROT
│   └── ModuleData/
│       ├── titles.xml                  ← ROT kingdoms, deJure heroes, fiefs
│       ├── partyTemplates.xml          ← optional: ROT-flavored caravans
│       └── Languages/                  ← optional: localized strings
└── src/
    ├── ROTBKContent.cs                 ← CampaignBehaviorBase that registers
    │                                     into all DefaultTypeInitializer<>
    │                                     instances on game start
    ├── ShippingLanesPatch.cs           ← Harmony patch on
    │                                     DefaultShippingLanes.Initialize
    │                                     (skip BK init, register ROT lanes)
    └── Main.cs                         ← MBSubModuleBase
```

Estimated line count: 1500-3000 lines of C# + 200-500 lines of XML for a
playable Tier-2 patch.

---

## 9. Testing checklist

Once your patch mod is buildable:

1. **Game starts without crashing.** That's the ROT init NRE we were
   seeing in our earlier crash report. If BK's `DefaultShippingLanes.Initialize`
   still fires unmodified, you'll see `InvalidOperationException: Sequence
   contains no matching element`.
2. **Open the BK kingdom screen.** Should show your ROT kingdoms with
   the titles, dynasties, succession laws you declared in `titles.xml`.
3. **Walk into a ROT settlement.** BK menu options should appear. If
   you see "BK menu is empty," your culture-keyed registrations
   (`DefaultLifestyles`, `DefaultDemesneLaws`) didn't fire — check
   `OnGameStart` ordering and that `_registered` flag works.
4. **Try the lifestyle picker.** Should show ROT-culture lifestyles for
   ROT-cultured heroes.
5. **Run `bannerkings.shipping_topology`** in the console (cheats on).
   You should see your ROT lanes in the report. If lane count is 0, your
   `AddObject` didn't run or the timing was wrong.
6. **Test a raid via `bannerkings.test_raid_capture <ROT village id>`** —
   captive caravan should spawn and walk to a ROT fief.
7. **Wait an in-game day.** `BK_caravan_watchdog.txt` should appear in
   the user's `Configs/ModLogs/` and contain caravan-state lines.
8. **Force a war via `bannerkings.test_war <kingdomA> | <kingdomB>`** —
   shipping graph adaptive routing should kick in.
9. **Run `bannerkings.shipping_risk_path <fromId> <toId>`** with two
   ROT settlements — should produce a path through your registered lanes.

If steps 1-3 work, you have a Tier-1 patch (doesn't crash, BK exists).
Steps 4-9 confirm Tier-2 functionality.

---

## 10. Coordination with BK upstream

The maintainer will accept small null-guard PRs for the items in §5 if
they don't change Calradia behaviour. Specifically these are merge-
ready candidates:

- `DefaultShippingLanes.Initialize`: replace `First` with `FirstOrDefault`
  + skip-lane-if-empty.
- Any other `Settlement.All.First(x => x.StringId == "...")` site —
  same treatment.
- A `BannerKings.RealmOfThrones` constant in `ModCompat.cs`.

Submit PRs to `main` against
[github.com/GIO443/bannerlord-banner-kings-redux](https://github.com/GIO443/bannerlord-banner-kings-redux).

Anything that materially changes Calradia gameplay (lane topology
changes, new shipping mechanics, etc.) should stay in the patch mod —
BK ships native support for War Sails Nord because that's a TaleWorlds-
official DLC; ROT is a third-party total conversion and is the patch
mod author's responsibility.

---

## Appendix A: full list of `DefaultTypeInitializer<>` registries

For completeness; grep
`DefaultTypeInitializer<` in `BannerKings/**.cs` to verify against
the current code.

Behaviours: `DefaultCrimes`, `DefaultCriminalSentences`,
`DefaultInterestGroup`, `DefaultRadicalGroups`, `DefaultDemands`,
`DefaultCasusBelli`, `DefaultBannerKingsEvents`, `DefaultInvasions`,
`DefaultCustomTroopPresets`, `DefaultMercenaryPrivileges`,
`DefaultSchemes`, `DefaultShippingLanes`.

Campaign content: `BKVillageTypes`, `DefaultCulturalStandings`,
`DefaultMarketGroups`, `BKSkillEffects`, `BKTraits`,
`DefaultTraitEffects`.

Court & council: `DefaultCourtExpenses`, `DefaultCouncilPositions`,
`DefaultCouncilTasks`.

Cultures: `DefaultFiefHeritage`, `DefaultPopulationNames`,
`DefaultTitleNames`.

Dynasties: `DefaultDynasties`, `DefaultLegacies`, `DefaultLegacyTypes`.

Buildings: `BKBuildings`, `DefaultVillageBuildings`.

Campaign start: `DefaultStartOptions`.

(There are ~30 more under `Managers/`, `Education/`, `Religions/`,
`Estates/` — the grep is the source of truth.)

## Appendix B: cheat-driven testing

Cheats must be enabled in the launcher (`engine_config.txt`:
`cheat_mode = 1`). Useful for patch development:

| Cheat | Purpose |
|---|---|
| `bannerkings.ping` | Sanity-check that BK cheats are dispatching at all |
| `bannerkings.shipping_topology` | Dumps the trade graph (sea + land edges, components, bridge ports) to `BK_shipping_topology.txt` |
| `bannerkings.test_setup` | Player setup: 500k gold, 1k renown, full peerage |
| `bannerkings.test_war <kingdomA> \| <kingdomB>` | Force-declare war between two kingdoms |
| `bannerkings.test_raid_capture <villageId>` | Run the BK raid capture flow on a village without combat |
| `bannerkings.test_dump_raid_state` | Snapshot of player raid policy + active captive caravans |
| `bannerkings.dump_caravans` | Snapshot of every caravan-style party with stuck-detection fields |

All long output gets mirrored to `BK_<cheat>.txt` under
`Documents/Mount and Blade II Bannerlord/Configs/ModLogs/` so you can
read it while the game is running.
