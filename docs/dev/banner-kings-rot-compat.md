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

## ⚠️ Target the 1.5.x branch, not main

**Build and test against
[`release/1.5.x`](https://github.com/GIO443/bannerlord-banner-kings-redux/tree/release/1.5.x)
(latest tagged release: [v1.5.8.0](https://github.com/GIO443/bannerlord-banner-kings-redux/releases/tag/v1.5.8.0)).**

The 1.6.x line (head: `main`) is currently iterating fast — graph-driven
shipping, adaptive risk weighting, raid capture, captive caravans,
hop-by-hop routing — and you'd be chasing a moving target. The 1.5.x
maintenance branch has stable, predictable shipping-lane semantics and
no raid capture system, so a compat patch built against it stays valid.
Bug fixes that don't depend on graph or raid-capture code are
cherry-picked from main to 1.5.x, so you'll get safety updates without
gameplay-shape changes.

If the 1.6.x systems eventually stabilise and the patch author wants to
forward-port, that's a follow-up — but ROT compat against 1.5.x first
is the right scope.

> Maintainer note for the BK side: the items called out as "BK should fix"
> below are crashes-on-init that any patch mod will trip over. We're happy
> to take small null-guard PRs against the **`release/1.5.x` branch**
> that don't change Calradia behaviour. Items called out as "Patch mod
> registers" are work the patch is expected to do — BK isn't going to
> ship ROT data.

---

## ⚖️ Legal & licensing — read this first

Before writing a single line of code, decide what your patch mod **is**
in IP terms. The path that's clearly defensible:

> **Your patch mod is a standalone module** that you author from scratch,
> ship under your own license, and host on your own Nexus / GitHub. It
> calls Banner Kings's and Realm of Thrones's **public C# APIs** and
> references their data **by StringId**. It does **not** include any
> file from either upstream — no DLLs, no XML, no assets, no ported
> source.

That's the architecture this whole document assumes. If you stay inside
those lines, you own your patch outright and neither upstream has a
claim on it. If you stray over those lines, things get fuzzy fast.

### Do

- **Write all code from scratch.** Reference BK types
  (`DefaultLifestyles`, `ShippingLane`, `Hero`, `Settlement`, etc.) by
  their public surface. That's normal cross-mod consumption, same as
  any mod that depends on Harmony, ButterLib, or MCM.
- **Author your own XML data.** Your `titles.xml` should declare *your
  own* ROT-flavored kingdoms with *your own* names, descriptions, and
  ID assignments. The schema is BK's, but the contents are yours.
- **Distribute your build artifact only** — your DLL, your XMLs, your
  assets. The end user installs BK and ROT separately; your module
  *depends on* them but does not bundle them.
- **Publish under an explicit license** that you control. MIT, BSD,
  Apache, or "All Rights Reserved with personal-use exception" all
  work. Pick one and put it in `LICENSE.md` at your repo root. Make
  it clear *your* code is *your* IP.
- **Credit both upstreams in your README.** Something like *"Requires
  Banner Kings — Redux ([link]) and Realm of Thrones ([link]). All
  credit for the underlying systems and Westeros/Calradia content
  belongs to those projects."* Linking is normal; embedding is not.
- **Add a takedown clause** matching the spirit of BK Redux's:
  > "If the maintainers of Banner Kings or Realm of Thrones request
  > this patch be taken down, for any reason and at any time, it
  > will be removed immediately, without question, without delay,
  > and without argument."
  >
  > Cheap to write, demonstrates good faith, gives you cover if the
  > relationship sours.

### Don't

- **Don't fork either project.** No `git clone bannerlord-banner-kings &&
  rename` and ship-as-yours. That's how the BK Redux maintainer ended up
  on shaky ground (working in good faith, but without explicit
  permission). Your patch should be its own repo from line 1.
- **Don't redistribute BK or ROT files** in your release zip. No copying
  their `BannerKings.dll`, no shipping their textures, no embedding
  their XML. The launcher resolves dependencies from the user's
  installed modules.
- **Don't copy BK's `titles.xml` verbatim** and edit Calradian content
  out. Author your own XML from scratch. The schema is the schema; the
  data is yours. (This matters because BK's title hierarchy is
  R-Vaccari's creative work; *replicating it with names changed* is a
  derivative work in a way that *writing parallel ROT data* isn't.)
- **Don't copy BK source files** into your codebase, even with
  modifications. If you need a method that's not exposed publicly
  (e.g. a private `BK.MakeLane` factory), open an issue or PR on BK
  to make it public — don't reimplement by copying.
- **Don't reuse ROT's textures, scripts, or XML.** If you need
  ROT-specific names or assets, reference them by ID — at runtime ROT
  resolves them from its own files, not yours.
- **Don't post your patch on the BK or ROT Nexus pages.** It's *your*
  mod. Host it on your own page, link to the upstreams as
  prerequisites.

### Grey areas to call out

- **Identifying ROT factions, settlements, characters by StringId**:
  fine. StringIds are functional identifiers, not creative content.
  Your code says `MBObjectManager.Instance.GetObject<CultureObject>("westeros_north")`
  — that's a runtime reference, not an embedded asset.
- **Naming your kingdoms after Westeros houses in your XML**: arguably
  uses ROT's lore. The cleanest approach is to use generic descriptive
  names (`{=...}House of the North`) rather than franchise names, OR
  get explicit permission from the ROT maintainers before publishing.
  When in doubt, ask the ROT discord/repo before shipping.
- **Reproducing BK's UI layouts**: your patch shouldn't need to. BK's
  UI reads from BK's data, which reads from your registered content.
  No UI cloning required.

### Suggested LICENSE.md skeleton

```
Copyright (c) <year> <your name>.

This patch module — including all C# source files, XML data files,
assets, and documentation in this repository — is licensed under
<MIT / BSD-3 / Apache-2.0 / your choice>. See the SPDX header in each
file.

This module is a compatibility patch. It depends on but does NOT
include or redistribute:

- Banner Kings (R-Vaccari and contributors), maintained as
  Banner Kings — Redux at <link>. All credit for Banner Kings
  systems and Calradia content belongs to that project.
- Realm of Thrones (<author>), at <link>. All credit for the
  Westeros conversion and lore belongs to that project.

If the maintainers of either upstream project request this patch be
taken down, it will be removed immediately, without argument.
```

A clean LICENSE plus a README that links to (not embeds) both upstreams
puts your patch in clear IP territory. You own your code; they own
theirs; users assemble all three at install time.

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
    <DependedModuleMetadata id="BannerKings.Redux" order="LoadBeforeThis" version="v1.5.8.0" />
    <DependedModuleMetadata id="realmofthrones.core" order="LoadBeforeThis" />
</DependedModuleMetadatas>
```

The pinned BK version (`v1.5.8.0` or whatever the latest 1.5.x release
tag is) tells the BUTR launcher to refuse to enable the patch against
incompatible BK versions. Update the pin when 1.5.x cherry-picks ship
and you've re-tested.

Use whichever ROT module id is canonical (the crash log we observed
used `realmofthrones.core`).

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

Once your patch mod is buildable on a 1.5.x base:

1. **Game starts without crashing.** That's the ROT init NRE we were
   seeing. If BK's `DefaultShippingLanes.Initialize` still fires
   unmodified, you'll see `InvalidOperationException: Sequence contains
   no matching element`.
2. **Open the BK kingdom screen.** Should show your ROT kingdoms with
   the titles, dynasties, succession laws you declared in `titles.xml`.
3. **Walk into a ROT settlement.** BK menu options should appear. If
   you see "BK menu is empty," your culture-keyed registrations
   (`DefaultLifestyles`, `DefaultDemesneLaws`) didn't fire — check
   `OnGameStart` ordering and that the `_registered` flag works.
4. **Try the lifestyle picker.** Should show ROT-culture lifestyles for
   ROT-cultured heroes.
5. **Run `bannerkings.shipping_topology`** in the console (cheats on).
   You should see your ROT lanes in the report. If lane count is 0, your
   `AddObject` didn't run or the timing was wrong. (1.5.x's topology
   report covers connected components, bridge ports, and diameter; it
   doesn't include the adaptive risk surface — that's a 1.6.x feature.)
6. **Run `bannerkings.shipping_path <fromId> <toId>`** between two of
   your ROT ports — should produce a path through your registered lanes.
7. **Confirm a vassalage / kingdom interaction.** Talk to a ROT ruler,
   get the BK "join your service" dialog option, accept a county.
8. **Trigger an in-game succession.** Use `bannerkings.give_title` to
   transfer a title, then kill the holder via vanilla cheat
   (`campaign.give_xp 1000`-style or kill via combat). The BK title
   should re-inherit per the contract you declared in `titles.xml`.

If steps 1-3 work, you have a Tier-1 patch (doesn't crash, BK exists).
Steps 4-8 confirm Tier-2 functionality.

The 1.6.x raid capture system, captive caravans, adaptive shipping
risk, and the `dump_caravans` watchdog **don't exist on 1.5.x** so
none of those tests apply to a 1.5.x-targeted patch. If the patch
is later forward-ported to 1.6.x, see `main` for the additional
checks (raid capture flow, captive caravan hop routing, adaptive
risk pathfinding under war state).

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

## Appendix B: cheats available on 1.5.x

Cheats must be enabled in the launcher (`engine_config.txt`:
`cheat_mode = 1`). The full set on `release/1.5.x`:

| Cheat | Purpose |
|---|---|
| `bannerkings.give_title <Title> \| <Hero>` | Transfer a title to a hero, useful for testing succession |
| `bannerkings.start_rebellion <settlement>` | Start a rebellion event at the named settlement |
| `bannerkings.add_piety <amount>` | Adds piety to MainHero (religion stack) |
| `bannerkings.add_career_points` | Adds mercenary career points |
| `bannerkings.finish_claims` | Resolves all open title claims |
| `bannerkings.shipping_topology` | Connected components, bridge ports, average shortest path, diameter — useful for verifying your registered lanes |
| `bannerkings.shipping_path <fromId> <toId>` | Shortest path between two ports through registered lanes |
| `bannerkings.give_player_full_peerage` | Sets player clan's peerage to Full Peer |
| `bannerkings.spawn_bandit_hero` | Spawns a BK bandit-hero clan (useful for testing bandit faction integration) |
| `bannerkings.advance_era <culture_id>` | Advances the innovation era for a culture |

The output of long-running cheats appears in the in-game console echo
and (on later 1.5.x patch revisions) is mirrored to
`BK_<cheat>.txt` under `Documents/Mount and Blade II Bannerlord/Configs/ModLogs/`.
If your local console echo doesn't show multi-line output cleanly,
look for the file there.

Cheats added in 1.6.x and **not present on 1.5.x**:

`bannerkings.ping`, `bannerkings.test_setup`, `bannerkings.test_war`,
`bannerkings.test_peace`, `bannerkings.test_clear_wars`,
`bannerkings.test_spawn_caravan`, `bannerkings.test_relocate_caravan`,
`bannerkings.test_dump_state`, `bannerkings.test_raid_policy`,
`bannerkings.test_raid_capture`, `bannerkings.test_dump_raid_state`,
`bannerkings.shipping_risk_path`, `bannerkings.dump_caravans`.

If you want any of these for diagnostic convenience, the simplest path
is to copy the relevant cheat methods from `main`'s
`BannerKings/BannerKingsCheats.cs` into your patch mod's own static
class — they're standalone and don't depend on 1.6.x-only systems
unless their name says they do (the `test_raid_*` and `dump_caravans`
ones do).
