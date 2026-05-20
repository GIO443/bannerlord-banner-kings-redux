# Banner Kings structural schema

BK's "flavor" content — religions, faiths, divinities, doctrines,
lifestyles, innovations, eras, titles, governments, succession and
inheritance laws, casus belli, council positions, interest groups — is
defined in **XML data files**, not hardcoded in C#. This page is the
contract for those files.

It is the structural counterpart to
[localization-schema.md](localization-schema.md): the localization
schema covers *how text reaches the screen*; this one covers *what
entities exist and how they reference each other*.

If you are writing a **setting-overhaul mod** — a 1500s-Europe reskin, a
Lord of the Rings conversion, a Game of Thrones total conversion — this
page is everything you need to drop in your own pantheon, titles,
education paths, and politics. **No C# required** to re-skin, re-tune,
add, or remove content.

You only need a small C# companion mod for genuinely new *behaviour* —
a rite that runs new code, a succession algorithm, a casus belli win
condition. The XML carries data and picks behaviour from a fixed menu of
named keys; it never carries code. See
[Behaviour and registries](#behaviour-and-registries).

## On this page

- [How the loader works (in 60 seconds)](#how-the-loader-works-in-60-seconds)
- [File layout](#file-layout)
- [ID convention](#id-convention)
- [Override semantics](#override-semantics)
- [Variable-size lists](#variable-size-lists)
- [Strings: inline vs. Languages/](#strings-inline-vs-languages)
- [Per-category reference](#per-category-reference)
- [Behaviour and registries](#behaviour-and-registries)
- [Coverage](#coverage)
- [Validation](#validation)

---

## How the loader works (in 60 seconds)

At BK init time (`BannerKingsConfig.Initialize()`), the loader:

1. Enumerates every loaded module via `TaleWorlds.ModuleManager.ModuleInfo`
   (reflection — survives 1.x patch-level API moves).
2. For each module, looks for `Modules/<ModuleId>/ModuleData/BKData/*.xml`.
3. Parses every file. The **root element** of each file names the
   category (`<faiths>`, `<doctrines>`, `<governments>`, …) — see the
   [File layout](#file-layout) for the full set. Direct children are
   entity rows keyed by their `id` attribute.
4. Merges rows by `(category, id)` — **last writer wins by module load
   order.** That means a mod loaded after BK overrides BK's row.
5. Hands the merged row set to the appropriate `Default*` initialiser.

The pre-XML hardcoded constructors are gone. `DefaultFaiths.Instance`,
`DefaultDoctrines.Instance`, etc. now populate themselves from the
merged row set, and their named properties (e.g.
`DefaultDoctrines.Instance.Druidism`) resolve by id-match.

The whole thing is one piece of infrastructure — `BKDataStore` (the
per-module scan + merge), `BKXml` (the typed attribute / list readers),
and one `Default*` consumer per category. Adding a future category is
the same template: an XML file, a root element, a refactored `Default*`.

## File layout

```
<YourMod>/ModuleData/BKData/
├── bk_doctrines.xml          doctrines (BK ships ~19)
├── bk_divinities.xml         gods / saints / spirits (BK ships ~21)
├── bk_faith_groups.xml       faith groups / priesthoods (BK ships 7)
├── bk_marriage_doctrines.xml marriage systems (BK ships 7)
├── bk_war_doctrines.xml      holy-war doctrines (BK ships 3)
├── bk_faiths.xml             faiths binding everything above (BK ships 7)
├── bk_religions.xml          religion = faith + favored cultures (BK ships 7)
├── bk_eras.xml               tech-progression ages (BK ships 3)
├── bk_innovations.xml        researchable innovations (BK ships 23)
├── bk_lifestyles.xml         education paths (BK ships 15 + 3 War Sails)
├── bk_inheritances.xml       inheritance laws (BK ships 3)
├── bk_gender_laws.xml        gender laws (BK ships 3)
├── bk_title_names.xml        per-rank title nouns (BK ships 8)
├── bk_successions.xml        succession laws (BK ships 10)
├── bk_governments.xml        constitutional forms (BK ships 4)
├── bk_interest_groups.xml    kingdom-politics factions (BK ships 5)
├── bk_mercenary_privileges.xml  mercenary career rewards (BK ships 7)
├── bk_casus_belli.xml        war justifications (BK ships 10)
└── bk_council_positions.xml  privy-council positions (BK ships 17)
```

A flavor-mod XML may:

- **Override** a BK row by reusing its id (e.g. `<faith id="darusosian">`).
- **Add** a new row with a fresh id (`<faith id="church_of_yvarra">`).
- **Remove** a BK row by overriding it with an empty / invalid body —
  the loader silently drops rows that can't construct (e.g. a religion
  whose culture refs don't resolve).

You can split a category across as many files as you like; the loader
re-merges everything ending in `.xml` under `BKData/`. One file per
category is the convention but not a requirement.

## ID convention

Same convention as the localization schema. Every row has an `id`
attribute that is:

- Lowercase, ASCII, snake-case (digits allowed). Some legacy BK ids use
  CamelCase or mixed case (e.g. `AncestorWorship`, `OsricsVengeance`,
  `sixWinds`); those are preserved verbatim. New ids should be
  snake-case.
- **Stable for the lifetime of the entity.** Once shipped, an id is a
  public API — renaming it breaks every translation, every flavor mod
  override, and every save that persists the id (faiths and religions
  are saved by id).
- The same id used for the localization ids (`bk_faith_<id>_name`, …).
  Don't pick `id="darusosian"` and then write strings under
  `bk_faith_imperial_*` — the loader composes the loc id from the
  structural id automatically.

The C# property mapping is **CamelCase by convention**, but the loader
does case-insensitive id matching. Property `DefaultDoctrines.Druidism`
matches `id="druidism"`; property `DefaultDoctrines.OsricsVengeance`
matches `id="osrics_vengeance"`; etc.

Both `BKDataStore` (when merging) and `DefaultTypeInitializer.GetById`
(when resolving) compare ids with `OrdinalIgnoreCase`. A flavor mod that
ships `id="Legalism"` overrides BK's `id="legalism"` row cleanly, and
the C# property `DefaultDoctrines.Instance.Legalism` still resolves.

## Override semantics

Module load order determines who wins per id. BK declares
`<LoadAfterThis optional="true" />` for the conflict-cooperator mods
(Diplomacy, ImprovedGarrisons, etc.) — those load *after* BK, so their
rows override BK's defaults if id-equal.

For a setting-overhaul that wants to *replace* the BK pantheon entirely,
the recommended pattern is:

1. Ship `<YourMod>/ModuleData/BKData/bk_faiths.xml` with every BK faith
   id replaced (same ids, your text and refs).
2. Optionally ship rows with brand-new ids for setting-specific faiths.
3. Ensure your module loads after BK — by default it will, as long as
   BK is in the user's enabled-modules list.

To *erase* a BK row, override it with the minimum legal structure for
that category but leave the cross-refs empty or invalid; the loader
drops it during the consume pass.

## Variable-size lists

Every list field is expressed as N child elements of a wrapper element.
Empty lists are legal — write `<pantheon/>` or omit the wrapper. The
loader treats both as zero-length:

```xml
<faith id="example" ...>
  <natural_cultures>            (variable size, may be empty)
    <culture id="empire"/>
    <culture id="aserai"/>
  </natural_cultures>
  <pantheon>                    (minor divinities; main_god is an attribute)
    <divinity ref="astaronia"/>
    <divinity ref="darusos"/>
  </pantheon>
  <doctrines>
    <doctrine ref="legalism"/>
    <doctrine ref="Tolerant"/>
    <doctrine ref="Astrology"/>
  </doctrines>
  <ranks>                       (N entries = N clergy ranks)
    <rank>Acolyte</rank>
    <rank>Lictor</rank>
    <rank>Pontifex</rank>
  </ranks>
  <rites>
    <rite key="astaronia_festival"/>
  </rites>
</faith>
```

There is no fixed cap on list length. A faith with eight clergy ranks
is legal — the loader generates `bk_faith_<id>_rank_1` through
`_rank_8` automatically. A pantheon with fifteen divinities is legal.
A doctrine with thirty incompatible doctrines is legal.

## Strings: inline vs. `Languages/`

Every text field in a structural row (`<name>`, `<description>`, …) is
written **inline** in the XML. At load the value is wrapped in a
`TextObject` carrying an **auto-derived localization id** of the form
`bk_<category>_<id>_<field>` — e.g. `<name>` on `<faith id="darusosian">`
becomes `bk_faith_darusosian_name`.

That gives a flavor mod two ways to change a string:

- **Inline** — edit the text directly in your structural XML row.
  Simplest; this is what BK itself does.
- **`Languages/` override** — ship a `Languages/bk_*.xml` with the same
  `bk_<category>_<id>_<field>` ids and translated text. This is the
  standard TaleWorlds localization mechanism, it stacks cleanly with
  translations, and it doesn't require re-shipping the structural row.
  See [localization-schema.md](localization-schema.md).

Either way the runtime resolution is identical — TaleWorlds looks up the
loc id, and the `Languages/` text wins over the inline fallback when
present.

## Per-category reference

### `<doctrines>` → file `bk_doctrines.xml`

```xml
<doctrine id="..." permanent="true|false">
  <name>...</name>
  <description>...</description>
  <effects>...</effects>
  <incompatible>
    <doctrine ref="other_doctrine_id"/>
  </incompatible>
</doctrine>
```

| Field | Meaning |
|-------|---------|
| `id` | Stable id. Lowercase preferred; existing BK rows are mixed-case (preserved). |
| `permanent` | Once adopted, the doctrine cannot be removed. Default false. |
| `name` / `description` / `effects` | Player-facing strings. Generated loc ids: `bk_doctrine_<id>_name`, `…_description`, `…_effects`. |
| `incompatible` | Zero or more `<doctrine ref="…"/>`. A faith cannot hold this doctrine alongside any listed one. Unknown refs are silently skipped. |

Forward refs are fine — the loader builds in two passes, resolving
incompatibility lists after every doctrine has been constructed.

### `<divinities>` → file `bk_divinities.xml`

```xml
<divinity id="..." blessing_cost="N">
  <name>...</name>
  <description>...</description>
  <effects>...</effects>
  <epithet>...</epithet>
  <lore>...</lore>
  <prayer>...</prayer>
</divinity>
```

| Field | Meaning |
|-------|---------|
| `id` | Stable id. |
| `blessing_cost` | Base piety cost of a blessing from this divinity. Modulated by the consuming faith's flavor multiplier at runtime. Default 300. |
| `name` / `description` / `effects` | Standard. |
| `epithet` | Short title ("Sky-Father"). |
| `lore` | Extended flavour paragraph used in dialogue. |
| `prayer` | Short prayer line used in dialogue. |

### `<faith_groups>` → file `bk_faith_groups.xml`

A faith group is the supra-faith organisation (priesthood / hierarchy) a
Faith belongs to. Several faiths can share one group.

```xml
<faith_group id="..." type="Temporal|Disorganized|LandedPreacher">
  <name>...</name>
  <title>...</title>
  <description>...</description>
</faith_group>
```

| Field | Meaning |
|-------|---------|
| `id` | Stable id. |
| `type` | The behaviour class. `Temporal` = organised hierarchy with a leader. `Disorganized` = no central seat; reformable at runtime. `LandedPreacher` = preacher-led, seated. Resolved via `FaithGroupRegistry`. |
| `name` | Group name. |
| `title` | Rank title of the group's leader (e.g. "Pontifex"). |
| `description` | Flavour text. |

`type="Appointed"` is **not** XML-expressible — `AppointedGroup`'s
constructor needs a live `CouncilMember`. A mod wanting one ships a C#
companion and registers it via `DefaultFaithGroups.AddObject(...)`, or
registers a new type key with `FaithGroupRegistry.Register(...)`.

### `<marriage_doctrines>` → file `bk_marriage_doctrines.xml`

A faith binds exactly one marriage doctrine. It governs consort count,
the consanguinity (blood-relation) restriction, and cross-faith marriage.

```xml
<marriage_doctrine id="..."
                   consorts="N"
                   consanguinity="N"
                   accepts_untolerated="true|false"
                   is_concubinage="true|false">
  <name>...</name>
  <description>...</description>
</marriage_doctrine>
```

| Field | Meaning |
|-------|---------|
| `id` | Stable id. |
| `consorts` | Additional spouses/concubines beyond the primary spouse. `0` = strictly monogamous. |
| `consanguinity` | Forbidden blood-relation degree. `0` = any relation allowed. |
| `accepts_untolerated` | Whether a spouse from an untolerated (but not hostile) faith is allowed. |
| `is_concubinage` | When true the extra spouses are concubines (forceable, no diplomatic binding); when false they are secondary spouses. |

`MarriageDoctrine` is a `Doctrine` subclass; its effects field is always
empty (the consort/consanguinity/untolerated explanations are generated
at runtime from the numeric attributes).

### `<war_doctrines>` → file `bk_war_doctrines.xml`

A faith binds exactly one war doctrine. It lists the holy-war casus belli
the faith endorses and the piety each costs to invoke.

```xml
<war_doctrine id="..." permanent="true|false">
  <name>...</name>
  <description>...</description>
  <justifications>
    <justification casus_belli="<casus_belli_id>" piety="N"/>
  </justifications>
</war_doctrine>
```

| Field | Meaning |
|-------|---------|
| `id` | Stable id. |
| `permanent` | Once adopted, cannot be removed. Default false. |
| `justifications` | Variable-size list. Each `<justification>` references a casus belli id and the piety required to invoke it. An empty list = the faith forbids holy war. |

`casus_belli` refs resolve against `bk_casus_belli.xml` ids — `HolyWar`
and `DivineReclamation` are the two holy-war justifications. Unknown
refs are skipped with a diagnostic.

### `<faiths>` → file `bk_faiths.xml`

```xml
<faith id="..."
       flavor="Monotheistic|Polytheistic|Henotheistic|Dualistic"
       main_god="<divinity_id>"
       group="<faith_group_id>"
       marriage_doctrine="<marriage_doctrine_id>"
       war_doctrine="<war_doctrine_id>"
       banner_code="..."           (optional)
       faith_seat="<settlement_id>" (optional)>
  <name>...</name>
  <description>...</description>
  <cults_desc>...</cults_desc>
  <zealots_name>...</zealots_name>
  <blessing_action>...</blessing_action>
  <blessing_action_name>...</blessing_action_name>
  <blessing_question>...</blessing_question>
  <blessing_confirm_question>...</blessing_confirm_question>
  <natural_cultures>
    <culture id="empire"/>
    ...
  </natural_cultures>
  <pantheon>
    <divinity ref="<id>"/>
    ...
  </pantheon>
  <doctrines>
    <doctrine ref="<id>"/>
    ...
  </doctrines>
  <ranks>
    <rank>Acolyte</rank>
    <rank>Lictor</rank>
    <rank>Pontifex</rank>
  </ranks>
  <rites>
    <rite key="astaronia_festival"/>
    ...
  </rites>
</faith>
```

| Attribute | Meaning |
|-----------|---------|
| `flavor` | One of the four `FaithFlavor` enum values. Drives blessing-cost, faith-strength, virtue, and society-join cost multipliers. |
| `main_god` | The supreme divinity. Required. Must reference an id from `<divinities>`. |
| `group` | A `<faith_group>` id from `bk_faith_groups.xml`. |
| `marriage_doctrine` / `war_doctrine` | Ids from `bk_marriage_doctrines.xml` / `bk_war_doctrines.xml`. |
| `banner_code` | Optional Bannerlord banner code (the one editor exports). When absent, a random banner is generated. |
| `faith_seat` | Optional settlement StringId of the religion's seat. Resolved lazily at runtime; bad ids silently null. |

`<natural_cultures>` — cultures whose members consider this faith
native. Empty list is legal (no one is born into this faith). Cultures
that don't exist at runtime (e.g. `nord` without War Sails loaded)
are silently filtered.

`<ranks>` — variable-size list of clergy rank labels. N entries = N
clergy ranks. Generated loc ids: `bk_faith_<id>_rank_<n>`.

`<rites>` — variable-size list of rite keys. The key must be registered
in `BannerKings.Utils.BKData.RiteRegistry`. BK ships keys for every
canonical BK rite (`astaronia_festival`, `darusosian_homage`, etc.).
Unknown keys are silently skipped. To add a new rite type, write a
small C# companion mod that calls `RiteRegistry.Register(key, () => new MyRite())`
from its `SubModule.OnSubModuleLoad`.

### `<religions>` → file `bk_religions.xml`

```xml
<religion id="..." faith="<faith_id>">
  <cultures>
    <culture id="empire"/>
  </cultures>
</religion>
```

| Field | Meaning |
|-------|---------|
| `id` | Stable id; matches the faith id by convention but doesn't have to. |
| `faith` | Required ref to a `<faith>` id. Religions with unknown faith refs are dropped. |
| `cultures` | Variable-size list of CultureObject StringIds. **At least one must resolve** at init time or the religion is silently dropped (matches pre-XML behaviour — a religion with empty `FavoredCultures` IOOBs on `MainCulture` access). |

### `<eras>` → file `bk_eras.xml`

An Era is a tech-progression age. Every Innovation belongs to one; eras
form a chain.

```xml
<era id="..." previous="<era_id>">
  <name>...</name>
  <description>...</description>
</era>
```

| Field | Meaning |
|-------|---------|
| `id` | Stable id. |
| `previous` | Optional ref to the preceding era. Omit for the first era. The loader builds eras in two passes, so `previous` may point at an era declared later in the file. |
| `name` / `description` | Standard. |

### `<innovations>` → file `bk_innovations.xml`

An Innovation is a researchable cultural / technological advance.

```xml
<innovation id="..."
            type="Civic|Agriculture|Military|Technology|Building"
            required_progress="N"
            era="<era_id>"
            requirement="<innovation_id>">
  <name>...</name>
  <description>...</description>
  <effects>...</effects>
  <starting_for>
    <culture id="empire"/>
    ...
  </starting_for>
</innovation>
```

| Field | Meaning |
|-------|---------|
| `id` | Stable id. |
| `type` | One of the five `Innovation.InnovationType` values. Drives the research skill (Building→Engineering, Military→Tactics, Agriculture→Steward, Technology→Scholarship, Civic→Lordship). |
| `required_progress` | Research points needed to finish. Default 1000. |
| `era` | Required ref to an `<era>` id. |
| `requirement` | Optional ref to a prerequisite innovation. Two-pass load, so it may point forward in the file. |
| `name` / `description` / `effects` | Standard. |
| `starting_for` | Variable-size list of culture StringIds that begin the campaign with this innovation already unlocked. May be empty. Builds the per-culture `StartingInnovations` map. |

### `<lifestyles>` → file `bk_lifestyles.xml`

A lifestyle is an education path: two governing skills, an ordered perk
list unlocked by investing focus, a passive effect line, and an optional
native culture.

```xml
<lifestyle id="..."
           first_skill="<skill_id>"
           second_skill="<skill_id>"
           first_effect="N"
           second_effect="N"
           culture="<culture_id>">
  <name>...</name>
  <description>...</description>
  <effects>...{EFFECT1}...{EFFECT2}...</effects>
  <perks>
    <perk ref="<perk_string_id>"/>
    ...
  </perks>
</lifestyle>
```

| Field | Meaning |
|-------|---------|
| `id` | Stable id. |
| `first_skill` / `second_skill` | StringIds of the two governing skills. Resolve against every registered `SkillObject` — vanilla (`Bow`, `Leadership`, …) and BK (`Lordship`, `Scholarship`, `Theology`). |
| `first_effect` / `second_effect` | Numeric values substituted into the `{EFFECT1}` / `{EFFECT2}` tokens of the effects line at runtime. |
| `culture` | Optional. **Absent** → universal lifestyle. **Present and resolvable** → native to that culture. **Present but unresolvable** → the lifestyle is dropped. The Nord seafaring lifestyles use this last rule to stay absent unless War Sails registers the `nord` culture. |
| `name` / `description` / `effects` | Standard. `effects` holds the `{EFFECT1}` / `{EFFECT2}` tokens. |
| `perks` | **Ordered** list of perk StringIds. Order is significant — the nth perk is the one unlocked by the nth invested focus point. Unknown refs are skipped with a diagnostic. |

Perk StringIds are not the `BKPerks` C# property names — they carry a
`Lifestyle` prefix and occasionally diverge (`CataphractKlibanophoros`
→ `LifestyleCataphractKlibanophori`). A setting overhaul shipping its
own perks registers them as `PerkObject`s in the object manager and
references those StringIds here.

### `<inheritances>` → file `bk_inheritances.xml`

An inheritance law weights who succeeds to a clan.

```xml
<inheritance id="..."
             children_score="N" sibling_score="N"
             spouse_score="N" relative_score="N"
             authoritarian="N" oligarchic="N" egalitarian="N">
  <name>...</name>
  <description>...</description>
</inheritance>
```

Pure data — the four `*_score` attributes weight candidate categories; the
three leaning attributes feed contract politics. No refs.

### `<gender_laws>` → file `bk_gender_laws.xml`

```xml
<gender_law id="..."
            authoritarian="N" oligarchic="N" egalitarian="N"
            male_preference="N" female_preference="N"
            male_suppressed="true|false" female_suppressed="true|false">
  <name>...</name>
  <description>...</description>
</gender_law>
```

Pure data. `*_suppressed` blocks that gender from positions such as
knighthood; `*_preference` biases succession weighting.

### `<title_names>` → file `bk_title_names.xml`

```xml
<title_name id="..."
            type="Empire|Kingdom|Duchy|County|Barony|Lordship|Prince|Knight"
            culture="<culture_id>">
  <name>...</name>      male holder noun
  <female>...</female>  female holder noun
  <realm>...</realm>    realm noun (rank) or plural noun (Prince/Knight)
</title_name>
```

| Field | Meaning |
|-------|---------|
| `type` | Picks the title rank. |
| `culture` | Optional. Omit for the generic fallback; set it for a culture-specific override. BK ships only generic rows. |

### `<successions>` → file `bk_successions.xml`

A succession law is **behaviour-bound** — its data is XML, its
candidate-enumeration and scoring logic is C#.

```xml
<succession id="..."
            behavior="<registry_key>"
            elected="true|false"
            authoritarian="N" oligarchic="N" egalitarian="N">
  <name>...</name>
  <description>...</description>
  <candidates_text>...</candidates_text>
  <score_text>...</score_text>
  <ideal_for>
    <culture id="vlandia"/>
  </ideal_for>
</succession>
```

| Field | Meaning |
|-------|---------|
| `behavior` | Key into `SuccessionRegistry` — selects the C# candidate/scoring algorithm. BK ships: `AseraiElective`, `Dictatorship`, `Imperial`, `Hereditary`, `Republic`, `TheocraticElective`, `BattanianElective`, `FeudalElective`, `TribalElective`, `WilundingElective`. |
| `elected` | Whether the realm holds an election. |
| `candidates_text` / `score_text` | UI explanation of eligibility and scoring. |
| `ideal_for` | Variable-size list of cultures for which this is the default succession (builds `KingdomIdealSuccessions`). |

Data in XML, algorithm behind a named key — see
[Behaviour and registries](#behaviour-and-registries).

### `<governments>` → file `bk_governments.xml`

A government is a realm's constitutional form.

```xml
<government id="..."
            mercantilism="N"
            authoritarian="N" oligarchic="N" egalitarian="N">
  <name>...</name>
  <description>...</description>
  <effects>...</effects>
  <prohibited_policies>
    <policy ref="<policy_string_id>"/>
  </prohibited_policies>
  <successions>
    <succession ref="<succession_id>"/>
  </successions>
</government>
```

| Field | Meaning |
|-------|---------|
| `mercantilism` / leanings | Numeric modifiers. |
| `prohibited_policies` | Policies this government forbids. Refs resolve against every registered `PolicyObject` — vanilla ids are `policy_*` (e.g. `policy_war_tax`), BK's is `policy_limited_army_privilege`. |
| `successions` | The succession laws this government permits. Refs are `bk_successions.xml` ids. |

### `<interest_groups>` → file `bk_interest_groups.xml`

A kingdom-politics faction. **Pure data, no behaviour** — a plain XML
conversion (no registry). Each group is a stance map.

```xml
<interest_group id="..."
                main_trait="<trait_id>"
                demands_council="true|false"
                allows_commoners="true|false"
                allows_nobles="true|false"
                favored_position="<council_member_id>"
                legitimacy_factor="N">
  <name>...</name>
  <description>...</description>
  <occupations>        <occupation id="Lord"/> ...        </occupations>
  <supported_policies> <policy ref="<policy_id>"/> ...    </supported_policies>
  <shunned_policies>   <policy ref="<policy_id>"/> ...    </shunned_policies>
  <supported_laws>     <law ref="<demesne_law_id>"/> ...  </supported_laws>
  <shunned_laws>       <law ref="<demesne_law_id>"/> ...  </shunned_laws>
  <casus_belli>        <cb ref="<casus_belli_id>"/> ...   </casus_belli>
  <demands>            <demand ref="<demand_id>"/> ...    </demands>
</interest_group>
```

Every list is variable-size. Refs resolve against `TraitObject`
(vanilla+BK), `PolicyObject` (vanilla+BK), `DefaultDemesneLaws`,
`DefaultCasusBelli`, `DefaultDemands`, `DefaultCouncilPositions`.
`favored_position` is optional.

### `<mercenary_privileges>` → file `bk_mercenary_privileges.xml`

A reward a mercenary company spends career points on. **Behaviour-bound** —
data in XML, availability/grant logic behind a key.

```xml
<mercenary_privilege id="..."
                     behavior="<registry_key>"
                     points="N"
                     max_level="N">
  <name>...</name>
  <description>...</description>
  <unavailable_hint>... {POINTS} ... {LEVEL} ...</unavailable_hint>
</mercenary_privilege>
```

`{POINTS}` / `{LEVEL}` in the hint are auto-filled from the `points` /
`max_level` attributes. `behavior` keys into `MercenaryPrivilegeRegistry`;
BK ships `IncreasedPay`, `WorkshopGrant`, `EstateGrant`, `CustomTroop3`,
`CustomTroop5`, `BaronyGrant`, `FullPeerage`.

### `<casus_belli>` → file `bk_casus_belli.xml`

A war justification. **Behaviour-bound** — data in XML, the fulfilment /
adequacy / option logic behind a key.

```xml
<casus_belli id="..."
             behavior="<registry_key>"
             conquest="N" raid="N" capture="N"
             declare_war_score="N"
             requires_fief="true|false"
             requires_claimant="true|false">
  <name>...</name>
  <description>...</description>
  <objective>...</objective>
  <war_declared_text>...{ATTACKER}...{DEFENDER}...{FIEF}...</war_declared_text>
  <trait_weights>
    <trait ref="<trait_id>" weight="N"/>
  </trait_weights>
</casus_belli>
```

`trait_weights` is the AI-leaning map — variable size, refs resolve
against every `TraitObject`. `behavior` keys into `CasusBelliRegistry`
(`Rebellion`, `HolyWar`, `CulturalLiberation`, …). Note casus belli `id`s
are mixed-case (`imperial_superiority` vs `Rebellion`) — match the real
StringId, it is save-persisted.

### `<council_positions>` → file `bk_council_positions.xml`

A privy-council position. **Behaviour-bound, refs-only** — structural refs
in XML; the adequacy / candidate predicates *and* the per-culture title
map stay in C# behind a key.

```xml
<council_position id="..."
                  behavior="<registry_key>"
                  primary_skill="<skill_id>"
                  secondary_skill="<skill_id>"
                  ai_priority="true|false">
  <tasks>         <task ref="<council_task_id>"/> ...     </tasks>
  <privileges>    <privilege id="ARMY_PRIVILEGE"/> ...    </privileges>
  <trait_weights> <trait ref="<trait_id>" weight="N"/> ... </trait_weights>
</council_position>
```

`secondary_skill` is optional. `task` refs resolve against
`DefaultCouncilTasks`, `privilege` ids are the `CouncilPrivileges` enum.
A council position has **no name in XML** — its title is produced at
runtime by the behaviour's per-culture resolver. `behavior` keys into
`CouncilPositionRegistry` (the five Legion Commander rows share the
`LegionCommander` key).

## Behaviour and registries

The XML carries **data**, never code. There is deliberately no inline
C#, no DSL, no scripting language — that keeps flavor mods safe, simple,
and forward-compatible.

Most categories are **pure data**: every field is a number, a string, or
a reference to another row. They convert to XML one-to-one.

Some categories also carry **behaviour** — a succession's candidate
algorithm, a casus belli's win condition, a council position's
eligibility check. That behaviour cannot live in XML, so it lives in C#
behind a **named-key registry**. The XML row carries a `behavior="..."`
(or `type="..."`, or `key="..."`) attribute that selects one entry from a
fixed menu; the registry maps that key to the actual code.

This means a flavor mod can freely re-skin, re-tune, and re-mix
behaviour-bound content — change names, numbers, references, and *which*
algorithm a row uses — entirely from XML. Inventing a genuinely new
algorithm is the one thing that needs a C# companion mod: it calls the
registry's `Register(key, …)` from its `SubModule`, and from then on XML
rows can reference that new key like any built-in one.

The registries BK ships:

| Registry | Used by | Key attribute | Selects |
|----------|---------|---------------|---------|
| `RiteRegistry` | `bk_faiths.xml` `<rite>` | `key` | a `Rite` instance (festival, offering, …) |
| `FaithGroupRegistry` | `bk_faith_groups.xml` | `type` | the faith-group behaviour class (`Temporal` / `Disorganized` / `LandedPreacher`) |
| `SuccessionRegistry` | `bk_successions.xml` | `behavior` | the candidate-enumeration + heir-scoring algorithm |
| `MercenaryPrivilegeRegistry` | `bk_mercenary_privileges.xml` | `behavior` | the availability check + grant effect |
| `CasusBelliRegistry` | `bk_casus_belli.xml` | `behavior` | the fulfilment / adequacy / show-as-option logic |
| `CouncilPositionRegistry` | `bk_council_positions.xml` | `behavior` | the adequacy / candidate-validity checks + per-culture title map |

All six live in `BannerKings.Utils.BKData`. Each exposes a static
`Register(key, …)` so a C# companion mod can add keys, and a `Get(key)`
the loader uses to resolve them. An unknown key is skipped with a
diagnostic rather than crashing the load.

Pure-data registries (`DefaultDoctrines`, `DefaultFaithGroups`, etc.)
also keep the `DefaultTypeInitializer.AddObject(...)` hook — a C# mod can
inject a fully-constructed object at runtime without XML at all.

## Coverage

Every BK flavor category with a real data layer is XML-driven.

**Pure data** — plain XML, no registry:

- ✅ Faiths, Divinities, Religions, Doctrines
- ✅ FaithGroups, MarriageDoctrines, WarDoctrines
- ✅ Eras, Innovations
- ✅ Lifestyles
- ✅ Inheritances, GenderLaws, TitleNames, Governments
- ✅ InterestGroup

**Behaviour-bound** — data in XML, logic behind a named-key registry:

- ✅ Successions (`SuccessionRegistry`)
- ✅ MercenaryPrivileges (`MercenaryPrivilegeRegistry`)
- ✅ CasusBelli (`CasusBelliRegistry`)
- ✅ CouncilPositions (`CouncilPositionRegistry`, refs-only)

**Deliberately left in code** — pure subclass registries with no data
layer; an XML would be an empty husk and `DefaultTypeInitializer.AddObject`
is already their extension point:

- Demands, Goals, CriminalSentences, Schemes

A category is worth XML only when a modder can meaningfully edit it
without compiling — numbers to retune, refs to rewire, text to reskin,
*plus* a behaviour key to pick. Pure-behaviour registries fail that test.

The loader infrastructure (`BKDataStore`, `BKXml`, the per-module scan)
is category-agnostic — a future category follows the same template.

## Validation

**Runtime.** The loader logs parse failures and missing-id warnings to
`BKDataStore.Instance.Diagnostics`, and silently drops rows that can't
construct (missing required refs, unresolvable cultures, …). The drop is
intentional — flavor mods rely on it to selectively remove BK content —
but it also means a typo'd id or ref vanishes silently rather than
failing loudly.

**Build-time (CI).** `tools/validate_bkdata.py` closes that gap for
BK's own shipped data. It runs in the `Validate BKData XML` GitHub
workflow on every push and pull request that touches `BKData/`, and
fails the build on:

- malformed XML;
- a duplicate `id` within a category;
- a missing required attribute;
- an unknown discriminator key (`type` / `behavior` / `flavor` not one
  BK ships);
- a BKData-internal cross-reference that doesn't resolve — `<doctrine
  ref>`, `main_god`, `group`, `marriage_doctrine`, `war_doctrine`,
  `<divinity ref>`, `faith`, `era`, `requirement`, `previous`,
  `<succession ref>`, and the war-doctrine `casus_belli` justifications.

**What CI cannot check.** References that resolve at runtime against the
game or the C# registries — `culture`, `main_trait` / `<trait ref>`,
`*_skill`, `<policy ref>`, `<perk ref>`, `<law ref>` (demesne laws),
`<task ref>` (council tasks), `<demand ref>`, and `<rite key>` — are out
of scope; the validator has no game data to resolve them against. A
typo there still drops silently at load, so test in-game after editing
those.

To run the validator locally: `python tools/validate_bkdata.py` from the
repo root.
