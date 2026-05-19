# Banner Kings localization schema

This is the contract between Banner Kings' C# code and its translatable
text. **All player-visible text in BK must reach the screen via this
schema**, so that a flavour mod (a 1500s-Europe reskin, a translation,
a setting-swap) only needs to edit XML — never C#.

If you are writing a flavour mod, **this page is the only one you need
to read.** Everything else is reference material for BK contributors.

## On this page

- [How TaleWorlds localization works (in 60 seconds)](#how-taleworlds-localization-works-in-60-seconds)
- [File layout](#file-layout)
- [ID convention](#id-convention)
- [Categories](#categories)
- [Per-category field reference](#per-category-field-reference)
- [Variables (the `{TOKEN}` parts)](#variables-the-token-parts)
- [Pluralisation, gendered forms, lists](#pluralisation-gendered-forms-lists)
- [Adding a new translatable string (C# side)](#adding-a-new-translatable-string-c-side)
- [Adding a new language](#adding-a-new-language)
- [Validation](#validation)

---

## How TaleWorlds localization works (in 60 seconds)

In C# every player-visible string is wrapped in a `TextObject`:

```csharp
new TextObject("{=bk_faith_darusosian_name}Darusosian Path")
```

The part inside `{= … }` is the **localization ID**. The text after it
is the **default English fallback**.

At load time, TaleWorlds scans every `<LanguageFile>` registered in a
`language_data.xml` for the currently selected language. If it finds a
`<string id="bk_faith_darusosian_name" text="…" />`, the XML text wins.
Otherwise the inline fallback is used.

There is one sentinel ID — `{=!}` — that means "no key, do not look up,
the inline text is final." **No new BK code is allowed to use `{=!}`.**
Every `TextObject` BK writes must have a real ID matching this schema,
because the modder cannot override `{=!}` from XML.

## File layout

```
BannerKings/_Module/ModuleData/Languages/
├── language_data.xml                  registers every file below
├── common_strings.xml                 trait names, generic UI words (existing)
├── std_module_strings_xml.xml         auto-keyed strings (existing, do not hand-edit)
│
├── bk_faiths.xml                      faith names, descriptions, rite text
├── bk_divinities.xml                  gods and saints
├── bk_religions.xml                   religion groups, doctrines, clergy ranks
├── bk_titles.xml                      title rank labels (Emperor / Duke / Count …)
├── bk_goals.xml                       kingdom goals + failure reasons
├── bk_diplomacy.xml                   casus belli, demands, declarations
├── bk_traits.xml                      BK trait + skill-effect descriptions
├── bk_ui.xml                          tooltips and explanation text in BKModels & UI
│
└── DE/
    ├── language_data.xml              same files, language="German"
    ├── bk_faiths.xml
    └── … (mirror)
```

Each `bk_*.xml` file has the standard TaleWorlds shape:

```xml
<?xml version="1.0" encoding="utf-8"?>
<base xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
      xmlns:xsd="http://www.w3.org/2001/XMLSchema"
      type="string">
  <tags>
    <tag language="English" />
  </tags>
  <strings>
    <string id="bk_faith_darusosian_name"
            text="Darusosian Path" />
    <string id="bk_faith_darusosian_description"
            text="The Darusosian Path is the imperial faith…" />
    <!-- … -->
  </strings>
</base>
```

A flavour mod overrides any of these by shipping its **own** XML file
with the same `<string id="…">` and a later module load order. BK's
defaults stay intact for players who don't install the flavour mod.

## ID convention

Every BK ID is built from three or four lowercase, snake-case parts
joined by underscores:

```
bk_<category>_<entity>_<field>[_<index>]
```

| Part       | Rule                                                                      | Example                |
|------------|---------------------------------------------------------------------------|------------------------|
| `bk_`      | Mandatory namespace. Distinguishes BK strings from vanilla / other mods.  | `bk_`                  |
| `category` | One of the [Categories](#categories) below. Singular noun.                | `faith`                |
| `entity`   | Stable identifier of the thing. **Matches the C# `Id` field** if any.     | `darusosian`           |
| `field`    | The property on that entity. Snake-case of the C# property name.          | `description`          |
| `index`    | 1-based ordinal **only** for ordered collections (rank titles, choices).  | `_1`, `_2`             |

Rules:

1. **Stable for the lifetime of the entity.** Once shipped, an ID is a
   public API — renaming it breaks every translation and every flavour
   mod. If the entity is renamed in C#, the ID stays.
2. **Lowercase, ASCII only.** No spaces, no punctuation, no diacritics
   in the ID itself. (Diacritics in the text are fine: `Caïon` is a
   valid `text="…"` value.)
3. **The C# `Id` field is the source of truth for `<entity>`.** If
   `FaithPreset.Id = "darusosian"`, the strings are
   `bk_faith_darusosian_*`. No exceptions.
4. **Anonymous strings** (UI tooltips with no owning entity) use a
   short slug of 2–4 words derived from the literal, plus a
   disambiguating short hash:
   `bk_ui_<file_slug>_<phrase_slug>_<hash4>`, e.g.
   `bk_ui_stability_model_loyalty_modifier_a3f1`.
   File-slug is the BKModel / VM class name in snake case. The 4-char
   hash prevents collisions and is generated once at extraction time.

## Categories

| Category       | Owns                                                                                            | File              |
|----------------|-------------------------------------------------------------------------------------------------|-------------------|
| `faith`        | Each `Faith` defined in `DefaultFaiths`                                                          | `bk_faiths.xml`   |
| `rite`         | Each `Rite` (`AstaroniaFestival`, `LanceOffering`, …)                                            | `bk_faiths.xml`   |
| `divinity`     | Each `Divinity` defined in `DefaultDivinities`                                                   | `bk_divinities.xml`|
| `faith_group`  | Each `FaithGroup` (`ImperialOrders`, `VlandicCanonical`, …)                                      | `bk_religions.xml`|
| `religion`     | Each `Religion` in `DefaultReligions`                                                            | `bk_religions.xml`|
| `doctrine`     | War, marriage, and general doctrines (`DefaultDoctrines`, `DefaultMarriageDoctrines`, …)         | `bk_religions.xml`|
| `clergy_rank`  | The 3 rank titles per faith (`Acolyte`/`Lictor`/`Pontifex`, …)                                    | `bk_religions.xml`|
| `title_rank`   | Emperor / King / Duke / Count / Baron and their feminine + abstract-noun forms                    | `bk_titles.xml`   |
| `goal`         | `KingdomGoal`, `EmpireGoal`, … — name + per-failure-reason text                                  | `bk_goals.xml`    |
| `casus_belli`  | Each entry in `DefaultCasusBelli`                                                                | `bk_diplomacy.xml`|
| `demand`       | `ClaimantDemand`, `SecessionDemand`, `DemesneLawChangeDemand`, …                                 | `bk_diplomacy.xml`|
| `radical_group`| `DefaultRadicalGroups`                                                                            | `bk_diplomacy.xml`|
| `trait`        | BK-specific traits (`BKTraits`)                                                                  | `bk_traits.xml`   |
| `skill_effect` | `BKSkillEffects` + `DefaultTraitEffects`                                                          | `bk_traits.xml`   |
| `tooltip`      | `BKModel` explanation lines, anonymous UI strings                                                | `bk_ui.xml`       |

If you need a category that isn't here, **add a row to this table in
the same PR that introduces it.** Don't invent a category in passing.

## Per-category field reference

Fields are the snake-case form of the C# property name. The reference
below lists every field the corresponding entity exposes today.
Anything not listed is either non-text or already has a vanilla
localization path.

### `faith` (entity = `FaithPreset.Id`)

| Field                          | Where it shows                                                                |
|--------------------------------|-------------------------------------------------------------------------------|
| `name`                         | Faith name in encyclopedia, religion tab, dialog                              |
| `description`                  | Long flavour text in religion tab                                             |
| `cults_desc`                   | Plural noun for the cults inside the faith (e.g. "imperial cults")            |
| `zealots_name`                 | Name of the zealots / militant order                                          |
| `blessing_action`              | First-person line the player says to a clergyman to request a blessing       |
| `blessing_action_name`         | Noun phrase for the blessings (e.g. "imperial blessings")                     |
| `blessing_question`            | Clergyman's question back to the player                                       |
| `blessing_confirm_question`    | Confirmation prompt before committing                                         |
| `rank_1`, `rank_2`, `rank_3`   | The three ordered clergy rank titles for this faith                           |

Example block (Darusosian):

```xml
<string id="bk_faith_darusosian_name"        text="Darusosian Path" />
<string id="bk_faith_darusosian_description" text="The Darusosian Path is the imperial faith of the Calradian Empire…" />
<string id="bk_faith_darusosian_cults_desc"  text="imperial cults" />
<string id="bk_faith_darusosian_zealots_name"            text="Sons of Darusos" />
<string id="bk_faith_darusosian_blessing_action"         text="I would seek a blessing of the Triad." />
<string id="bk_faith_darusosian_blessing_action_name"    text="imperial blessings" />
<string id="bk_faith_darusosian_blessing_question"       text="Which of the Triad shall hear your prayer?" />
<string id="bk_faith_darusosian_blessing_confirm_question" text="Will you commit your devotion to {DIVINITY}?" />
<string id="bk_faith_darusosian_rank_1"      text="Acolyte" />
<string id="bk_faith_darusosian_rank_2"      text="Lictor" />
<string id="bk_faith_darusosian_rank_3"      text="Pontifex" />
```

### `rite` (entity = rite class name, snake-cased)

| Field          | Where it shows                                  |
|----------------|-------------------------------------------------|
| `name`         | Menu entry, dialog                              |
| `description`  | Tooltip / encyclopedia                          |
| `success_log`  | Log line on successful performance              |
| `failure_log`  | Log line on failed performance                  |

Example: `bk_rite_astaronia_festival_name`.

### `divinity` (entity = divinity class field name, snake-cased)

| Field         | Where it shows                                  |
|---------------|-------------------------------------------------|
| `name`        | Divinity name in dialog, religion tab           |
| `description` | Lore paragraph                                  |
| `effect`      | One-line mechanical effect summary              |
| `epithet`     | Short epithet (e.g. "Sky-Father")               |
| `lore`        | Extended flavour paragraph                      |
| `prayer`      | What the divinity is prayed to for              |

### `faith_group` (entity = `FaithGroup.Id`)

| Field         | Where it shows                          |
|---------------|-----------------------------------------|
| `name`        | Group name in religion tab              |
| `description` | Group description                       |

### `religion` (entity = `Religion.Id`)

| Field         | Where it shows                          |
|---------------|-----------------------------------------|
| `name`        | Religion name                           |
| `description` | Religion description                    |

### `doctrine` (entity = `Doctrine.Id`)

| Field         | Where it shows                          |
|---------------|-----------------------------------------|
| `name`        | Doctrine name                           |
| `description` | Doctrine description                    |
| `effects`     | One-line summary of mechanical effects  |

### `clergy_rank` (entity = faith ID, then `_<n>`)

Already covered under `faith.rank_<n>`. Listed here for completeness.

### `title_rank` (entity = rank name in lowercase, e.g. `kingdom`, `dukedom`)

| Field         | Where it shows                                                  |
|---------------|-----------------------------------------------------------------|
| `holder_m`    | Masculine title-holder noun (Emperor / King / Duke / Count)     |
| `holder_f`    | Feminine title-holder noun (Empress / Queen / Duchess / Countess)|
| `realm`       | Abstract-noun form of the realm (Empire / Kingdom / Dukedom)    |

(The full display name is composed at runtime as `{realm} of {NAME}`,
so the realm form is the one most translations will need to rework.)

### `goal` (entity = `Goal.Id`)

| Field                       | Where it shows                                       |
|-----------------------------|------------------------------------------------------|
| `name`                      | Goal title in goals UI                               |
| `description`               | Goal description in goals UI                         |
| `fail_<reason_slug>`        | One row per distinct failure reason. `<reason_slug>` is a short verb-phrase: `wrong_culture`, `wrong_faith`, `missing_settlements`, `realm_already_attached`, … |

### `casus_belli` (entity = `CasusBelli.Id`)

| Field              | Where it shows                                             |
|--------------------|------------------------------------------------------------|
| `name`             | Casus belli name in the war declaration UI                 |
| `description`      | Long description and objective                            |
| `objective`        | Short objective string                                     |
| `declaration_text` | The "{ATTACKER} marches to war…" line                      |

### `demand` (entity = `Demand.Id`)

| Field              | Where it shows                                             |
|--------------------|------------------------------------------------------------|
| `name`             | Demand name                                                |
| `description`      | Demand description                                         |
| `accept_text`      | Text shown when AI accepts                                 |
| `reject_text`      | Text shown when AI rejects                                 |
| `propose_text`     | Text shown when proposing                                  |

### `radical_group` (entity = `RadicalGroup.Id`)

| Field         | Where it shows                          |
|---------------|-----------------------------------------|
| `name`        | Group name in kingdom UI                |
| `description` | Group description / flavour             |

### `trait` and `skill_effect`

| Field         | Where it shows                          |
|---------------|-----------------------------------------|
| `name`        | Encyclopedia, character UI              |
| `description` | Encyclopedia tooltip                    |
| `effect`      | Mechanical effect summary (when shown)  |

### `tooltip` (entity = file slug; anonymous)

Used for explanation lines emitted by `BKModel.*` and view-model code.
ID form: `bk_tooltip_<file_slug>_<phrase_slug>_<hash4>`.

The generator picks `<phrase_slug>` from the first 4 words of the
literal, stripping variables and punctuation. The hash is 4 hex chars
of a stable SHA-1 over the literal so that incidental wording changes
keep the same ID until the meaning changes meaningfully.

Modders edit these freely — just remember they may be read mid-sentence
inside a generated explanation, so keep tense and punctuation
consistent with neighbouring tooltip strings.

## Variables (the `{TOKEN}` parts)

Strings can contain runtime variables: `{ATTACKER}`, `{DIVINITY}`,
`{CULTURES}`, `{TIER}`. These are substituted at runtime by C# via
`textObject.SetTextVariable("ATTACKER", value)`.

Rules for modders:

- **Don't rename variables.** `{ATTACKER}` in BK code stays
  `{ATTACKER}` in your translation. If you drop one, that piece of
  data won't appear in-game.
- **You can reorder them freely** — that's the whole point.
- **You can add the same variable twice** if your language needs it
  ("The {ATTACKER} marches; the {ATTACKER}'s banners…").
- **You may use the variable in a different grammatical position** —
  TaleWorlds' interpolation doesn't care about word order.
- **You cannot invent new variables.** If a variable isn't already in
  the BK fallback string, it isn't being set in C# either and will
  render as literal `{FOO}` text.

A complete list of variables in use per string can be derived from the
fallback text — but if a string needs a variable BK currently doesn't
provide, file an issue rather than guessing.

## Pluralisation, gendered forms, lists

TaleWorlds supports inline conditional forms with `{?VAR}…{?}…{\?}`
syntax (see vanilla `std_module_strings_xml.xml` for examples). For
the localization-only pass we keep the existing grammar of every BK
string — if BK currently writes one form, modders write one form. If
your language needs pluralisation BK doesn't expose (e.g. you want
"1 soldier" vs "2 soldiers" and BK only ships a singular), open an
issue: the field needs to become two strings in C#, which is a code
change, not a schema change.

## Adding a new translatable string (C# side)

When you add a new `TextObject` in BK code:

1. **Pick the category and entity** it belongs to. If neither fits,
   add a row to the [Categories](#categories) table.
2. **Construct the ID** per [ID convention](#id-convention). For
   anonymous tooltip strings, run `tools/loc-id` (see below) which
   produces the slug + hash for you.
3. **Write the `TextObject` with the real ID, never `{=!}`**:
   ```csharp
   new TextObject("{=bk_faith_<faithid>_<field>}<English fallback>")
   ```
4. **Add the string to the matching `bk_*.xml`** in the same PR. CI
   will fail if a `{=bk_…}` ID exists in code but is missing from XML
   (or vice-versa).

## Adding a new language

1. Create `BannerKings/_Module/ModuleData/Languages/<XX>/` where `<XX>`
   is your language directory (e.g. `FR`, `ES`, `IT`).
2. Copy every `bk_*.xml` from the parent folder into it.
3. Change every `<tag language="English" />` to your language name —
   it must match a language registered by vanilla or another module.
4. Create a `language_data.xml` in the same folder listing every
   `bk_*.xml` you ship.
5. Translate each `text="…"` attribute. **Leave the `id` attributes
   alone.** Leave `{VARIABLES}` alone (but reorder freely).
6. Strings you don't translate fall back to the English version, so a
   partial translation is fine to ship.

## Validation

Two CI checks (added with the localization pass) keep the schema
honest:

- **No `{=!}` in BK source.** A grep over `BannerKings/**.cs` for
  `"{=!}` returns zero matches.
- **ID parity between code and XML.** Every `{=bk_…}` ID referenced
  in code has a matching `<string id="…">` in some `bk_*.xml`, and
  every `<string id="bk_…">` in XML is referenced by at least one
  `TextObject` in code. Orphans fail CI.

A `tools/loc-id <category> <entity> <field>` helper script will
generate well-formed IDs (and the slug+hash form for tooltips), so
contributors don't have to remember the convention by hand.
