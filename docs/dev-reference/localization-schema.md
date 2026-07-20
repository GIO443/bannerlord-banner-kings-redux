# Banner Kings localization schema

How player-visible text in Banner Kings reaches the screen, and how to
translate it without touching C# or the structural data.

If you are **translating BK**, read [Quick start](#quick-start-translating-bk)
and [Adding a new language](#adding-a-new-language); the rest is reference.

## On this page

- [Quick start: translating BK](#quick-start-translating-bk)
- [What is translatable (and what isn't)](#what-is-translatable-and-what-isnt)
- [How TaleWorlds localization works (in 60 seconds)](#how-taleworlds-localization-works-in-60-seconds)
- [BKData strings and auto-derived ids](#bkdata-strings-and-auto-derived-ids)
- [File layout](#file-layout)
- [ID convention](#id-convention)
- [Categories](#categories)
- [Variables (the `{TOKEN}` parts)](#variables-the-token-parts)
- [Adding a new language](#adding-a-new-language)
- [Validation](#validation)
- [Known gaps](#known-gaps)

---

## Quick start: translating BK

```bash
# from the repo root
python tools/extract_loc.py                                  # see what exists
python tools/extract_loc.py --lang FR --lang-name "Français" # emit templates
python tools/extract_loc.py --report                         # coverage
```

That writes `BannerKings/_Module/ModuleData/Languages/FR/bk_*.xml`, every
string pre-filled with its English source text and its correct id. You
translate the `text="…"` attributes and ship the folder. Nothing else.

You never need to read C#, and you never edit `ModuleData/BKData/`.

## What is translatable (and what isn't)

BK's text lives in three places, and one chunk of it is currently
unreachable. Counts are from the current tree — regenerate with the
commands above rather than trusting these numbers after a big release.

| Surface | Roughly | How you translate it |
|---|---|---|
| **BKData structural text** — faiths, divinities, doctrines, lifestyles, innovations, successions, … | ~728 strings | `tools/extract_loc.py --lang XX`, then translate the generated files |
| **`std_module_strings_xml.xml`** — auto-keyed UI/C# strings | ~3,170 entries | Copy into `Languages/XX/` and translate `text="…"` |
| **`common_strings.xml`** — traits, generic UI words | ~147 entries | Same |
| **C# strings using the `{=!}` sentinel** | ~674 | **Not translatable.** See [Known gaps](#known-gaps) |

## How TaleWorlds localization works (in 60 seconds)

In C# every player-visible string is wrapped in a `TextObject`:

```csharp
new TextObject("{=bk_faith_darusosian_name}Darusosian Path")
```

The part inside `{= … }` is the **localization id**. The text after it is
the **English fallback**.

At load, TaleWorlds scans every `<LanguageFile>` registered in a
`language_data.xml` for the selected language. If it finds
`<string id="bk_faith_darusosian_name" text="…" />`, the XML text wins.
Otherwise the inline fallback is used. **Untranslated ids silently fall
back to English, so a partial translation is safe to ship.**

There is one sentinel id — `{=!}` — meaning "no key, do not look up, the
inline text is final." Strings written that way cannot be overridden from
XML by anyone.

## BKData strings and auto-derived ids

Text in `ModuleData/BKData/*.xml` is written inline in English:

```xml
<faith id="darusosian">
  <name>Darusosian Path</name>
  <description>The Darusosian Path is the imperial faith…</description>
</faith>
```

At load, [`BKXml.LocText`](../../BannerKings/Utils/BKData/BKXml.cs) wraps
each field in a `TextObject` carrying an id it derives on the spot:

```
bk_<loc_category>_<id>_<field>     ->  bk_faith_darusosian_name
```

So the ids are real, but **implicit** — nothing in the XML states them.
Worse, `<loc_category>` is a token that lives in C# and does *not* reliably
match the file or root element:

| BKData root element | loc category |
|---|---|
| `<faiths>` | `faith` (singular) |
| `<dilemmas>` | `dilemmas` (plural) |
| `<interest_groups>` | `interest_group` |
| `<casus_belli>` | `casus_belli` |

This is precisely why you should use `tools/extract_loc.py` rather than
deriving ids by hand: the script pairs each registry's
`BKDataStore.GetRows("…")` call with its `BKXml.LocText(…)` calls to
recover the mapping straight from the source, so it cannot drift.

A flavour mod has two ways to change one of these strings: edit the text
inline in its own BKData row, or ship a `Languages/` entry with the same
id. Translations should always use the second — it stacks cleanly and
doesn't require re-shipping structural data.

## File layout

```
BannerKings/_Module/ModuleData/Languages/
├── language_data.xml              registers the files below (English)
├── common_strings.xml             traits, generic UI words
├── std_module_strings_xml.xml     auto-keyed strings; do not hand-edit
│
└── DE/                            one folder per language
    ├── language_data.xml          same shape, language="Deutsch"
    ├── common_strings.xml
    └── std_module_strings_xml.xml
```

`tools/extract_loc.py --lang XX` adds a `Languages/XX/` folder containing
one `bk_<root_element>.xml` per BKData category (`bk_faiths.xml`,
`bk_divinities.xml`, …) plus a `language_data.xml` listing them. The
grouping is a convenience — TaleWorlds only cares about what
`language_data.xml` registers, so you may merge or split files freely.

Each generated file has the standard TaleWorlds shape:

```xml
<?xml version="1.0" encoding="utf-8"?>
<base xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
      xmlns:xsd="http://www.w3.org/2001/XMLSchema"
      type="string">
  <tags>
    <tag language="Français" />
  </tags>
  <strings>
    <string id="bk_faith_darusosian_name" text="Voie darusosienne" />
  </strings>
</base>
```

> The generated `language_data.xml` omits `subtitle_extension` and
> `supported_iso`. Copy those from `Languages/DE/language_data.xml` and
> adjust for your language.

## ID convention

```
bk_<category>_<entity>_<field>
```

| Part | Rule | Example |
|---|---|---|
| `bk_` | Mandatory namespace. | `bk_` |
| `category` | The loc category from C# (see [Categories](#categories)). | `faith` |
| `entity` | The row's `id` attribute in BKData. | `darusosian` |
| `field` | The XML child element (or attribute) name. | `description` |

Rules:

1. **Ids are a public API.** Once shipped, renaming one breaks every
   translation and flavour mod. If the entity is renamed in C#, expect
   the id to change with it — and CI will flag the orphaned strings.
2. **Lowercase ASCII in the id.** Diacritics in the `text="…"` value are
   fine; `Caïon` is a valid value.
3. **The BKData `id` attribute is the source of truth for `<entity>`.**

## Categories

There are currently **18** categories carrying translatable text. Rather
than duplicate the list here (the previous version of this page drifted
badly), get it live:

```bash
python tools/extract_loc.py
```

That prints every category, its loc token, its fields, and its string
count. As of writing: `casus_belli`, `dilemmas`, `divinity`, `doctrine`,
`era`, `faith`, `faith_group`, `gender_law`, `government`, `inheritance`,
`innovation`, `interest_group`, `lifestyle`, `marriage_doctrine`,
`mercenary_privilege`, `succession`, `title_name`, `war_doctrine`.

`bk_religions.xml` and `bk_council_positions.xml` are **structural only** —
they bind ids to cultures, skills and privileges and carry no display
text, so there is nothing to translate in them.

## Variables (the `{TOKEN}` parts)

Strings may contain runtime variables — `{DIVINITY}`, `{ATTACKER}`,
`{TIER}` — substituted in C# via `SetTextVariable`.

- **Don't rename them.** `{ATTACKER}` stays `{ATTACKER}`. A dropped
  variable means that data never appears in-game.
- **Reorder freely.** That is the whole point; word order is yours.
- **Repeat one if your grammar needs it.**
- **Don't invent new ones.** A variable not present in the English
  fallback isn't being set in C# and will render as literal `{FOO}`.

## Adding a new language

1. `python tools/extract_loc.py --lang XX --lang-name "YourLanguage"`.
   `--lang-name` must match a language name registered by vanilla or
   another module (`Deutsch`, `Français`, …).
2. Fill in `subtitle_extension` / `supported_iso` in the generated
   `Languages/XX/language_data.xml` (copy the shape from `DE/`).
3. Copy `common_strings.xml` and `std_module_strings_xml.xml` into
   `Languages/XX/`, change their `<tag language="…" />`, add them to
   `language_data.xml`, and translate them too.
4. Translate the `text="…"` values. **Leave `id` attributes alone.**
5. Ship. Untranslated ids fall back to English.

Re-run the extractor after a BK update with `--force` to a scratch
directory to see new strings, or use `--report` to watch coverage drop
when new content lands.

## Validation

`.github/workflows/validate-bkdata.yml` runs on every push and PR that
touches BKData, `Languages/`, BK C#, or either tool:

- `tools/validate_bkdata.py` — structural integrity of BKData.
- `tools/extract_loc.py --check` — **fails the build** on orphan ids (a
  shipped translation referencing a string BK no longer produces, i.e. a
  renamed or deleted entity) and on duplicate ids with conflicting text.
- `tools/extract_loc.py --report` — prints per-language coverage.

## Known gaps

Honest list of what this schema does *not* currently cover.

- **~674 `{=!}` strings in BK C# are untranslatable by anyone.** The
  sentinel means "never look this up," so no `Languages/` entry can
  override them. Converting them to real ids is a C# change, tracked
  separately. If you hit an English string in-game that no XML seems to
  control, this is almost certainly why.
- **`std_module_strings_xml.xml` ids are opaque.** They are auto-generated
  (`a3G31iZ0`) with no indication of where the string appears, which makes
  translating them without in-game context slow. Emitting source-file
  context alongside them is a possible future improvement.
- **Only ~22 `bk_*` ids appear directly in C#.** Almost all BK `bk_*` ids
  come from BKData via `LocText`, which is why the extractor covers the
  bulk of BK-specific content.
- **The German translation is a stub** — the folder exists but contains
  effectively one translated string. It is a structural template, not a
  reference translation.
