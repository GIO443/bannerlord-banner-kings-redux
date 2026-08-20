#!/usr/bin/env python3
"""
Extract translatable strings from BannerKings structural data (BKData).

Run from the repo root:

  python tools/extract_loc.py --lang FR --lang-name "Français"
      Emit ready-to-translate Languages/FR/bk_*.xml + language_data.xml,
      every string pre-filled with its English text.

  python tools/extract_loc.py --report
      Show per-language coverage (how many strings translated vs total).

  python tools/extract_loc.py --check
      CI mode. Fails on orphan ids in shipped translations and on duplicate
      ids. Exit code 0 = clean, 1 = at least one problem.

WHY THIS EXISTS

Text in BKData is written inline in English:

    <faith id="darusosian"><name>Darusosian Path</name></faith>

At load, BKXml.LocText wraps each field in a TextObject carrying an
auto-derived localization id:

    bk_<loc_category>_<id>_<field>      ->  bk_faith_darusosian_name

A translator therefore never edits BKData or C# — they ship a Languages/
string table using those ids, and TaleWorlds prefers it over the inline
English fallback. But the ids are implicit: nothing in the XML states them,
and <loc_category> is a token that lives in C# and does NOT match the file
or root element (root <faiths> -> loc category "faith"; root <dilemmas> ->
"dilemmas"). Deriving ~800 ids by hand across 26 files is the single
biggest barrier to translating BK. This script does it mechanically.

DRIFT-PROOFING

The store-category -> loc-category map is NOT hardcoded here. It is derived
by pairing, per registry .cs file, the BKDataStore.GetRows("<store>") call
with the BKXml.LocText(..., "<loc>", ..., "<field>") calls in the same file.
If a registry is added, renamed, or gains a field, this script picks it up
with no edit. That is deliberate: an earlier hand-maintained list in the
docs drifted out of sync with the code and sent translators looking for
files that never existed.
"""

import argparse
import os
import re
import sys
import xml.etree.ElementTree as ET
from xml.sax.saxutils import quoteattr

REPO_CS_DIR = "BannerKings"
BKDATA_DIR = os.path.join("BannerKings", "_Module", "ModuleData", "BKData")
LANG_DIR = os.path.join("BannerKings", "_Module", "ModuleData", "Languages")

# BKDataStore.GetRows("<store category>") — the root element of a BKData file.
RE_GETROWS = re.compile(r'GetRows\(\s*"([a-z_0-9]+)"\s*\)')
# BKXml.LocText(row, "<loc category>", id, "<field>" ...)
RE_LOCTEXT = re.compile(
    r'LocText\(\s*[^,]+,\s*"([a-z_0-9]+)"\s*,\s*[^,]+,\s*"([a-z_0-9]+)"'
)


def derive_category_map():
    """Pair GetRows(store) with LocText(loc, field) per .cs file.

    Returns (mapping, notes) where mapping is
        store_category -> {"loc": loc_category, "fields": [field, ...]}
    """
    mapping = {}
    notes = []
    for dirpath, _dirnames, filenames in os.walk(REPO_CS_DIR):
        for fname in filenames:
            if not fname.endswith(".cs"):
                continue
            path = os.path.join(dirpath, fname)
            try:
                with open(path, "r", encoding="utf-8-sig", errors="replace") as fh:
                    src = fh.read()
            except OSError:
                continue
            if "LocText(" not in src:
                continue

            stores = sorted(set(RE_GETROWS.findall(src)))
            pairs = RE_LOCTEXT.findall(src)
            if not pairs:
                continue
            locs = sorted({loc for loc, _f in pairs})
            fields = sorted({f for _loc, f in pairs})

            if not stores:
                # e.g. BKXml.cs itself, or a helper that formats but doesn't read.
                continue
            if len(stores) > 1 or len(locs) > 1:
                notes.append(
                    f"{fname}: ambiguous mapping (stores={stores}, locs={locs}); "
                    f"skipped — split the registry or extend extract_loc.py"
                )
                continue

            store, loc = stores[0], locs[0]
            if store in mapping and mapping[store]["loc"] != loc:
                notes.append(
                    f"{fname}: store '{store}' already mapped to "
                    f"'{mapping[store]['loc']}', now also '{loc}'"
                )
                continue
            entry = mapping.setdefault(store, {"loc": loc, "fields": []})
            for f in fields:
                if f not in entry["fields"]:
                    entry["fields"].append(f)
            entry["fields"].sort()
    return mapping, notes


def field_text(row, field):
    """Mirror BKXml.LocText: prefer a child element, fall back to an attribute."""
    child = row.find(field)
    if child is not None:
        text = "".join(child.itertext())
        return text.strip() if text else ""
    attr = row.get(field)
    if attr is not None:
        return attr.strip()
    return None


def collect_strings(mapping):
    """Walk BKData and build the full id -> english map, grouped by store category.

    Returns (groups, dupes, unmapped) where groups is
        store_category -> list of (loc_id, english, source_file)
    """
    groups = {}
    seen = {}
    dupes = []
    unmapped = []

    if not os.path.isdir(BKDATA_DIR):
        return groups, dupes, unmapped

    for fname in sorted(os.listdir(BKDATA_DIR)):
        if not fname.endswith(".xml"):
            continue
        path = os.path.join(BKDATA_DIR, fname)
        try:
            root = ET.parse(path).getroot()
        except ET.ParseError as exc:
            dupes.append(f"{fname}: not well-formed XML ({exc})")
            continue

        store = root.tag
        spec = mapping.get(store)
        if spec is None:
            # Structural-only data (e.g. <religions>, <council_positions>) that
            # binds ids together and carries no display text. Nothing to do.
            unmapped.append((fname, store))
            continue

        bucket = groups.setdefault(store, [])
        for row in root:
            rid = row.get("id")
            if not rid:
                continue
            for field in spec["fields"]:
                text = field_text(row, field)
                if text is None or text == "":
                    continue
                loc_id = f"bk_{spec['loc']}_{rid}_{field}"
                if loc_id in seen and seen[loc_id][1] != text:
                    # BKDataStore merges by (category, id), last file wins.
                    dupes.append(
                        f"duplicate id '{loc_id}' in {fname} "
                        f"(also {seen[loc_id][0]}) with differing text"
                    )
                seen[loc_id] = (fname, text)
                bucket.append((loc_id, text, fname))
    return groups, dupes, unmapped


def all_ids(groups):
    return {loc_id for bucket in groups.values() for loc_id, _t, _f in bucket}


def write_language(lang, lang_name, groups, force):
    outdir = os.path.join(LANG_DIR, lang)
    os.makedirs(outdir, exist_ok=True)
    written = []

    for store in sorted(groups):
        bucket = groups[store]
        if not bucket:
            continue
        target = os.path.join(outdir, f"bk_{store}.xml")
        if os.path.exists(target) and not force:
            print(f"  skip  {target} (exists; pass --force to overwrite)")
            continue
        lines = [
            '<?xml version="1.0" encoding="utf-8"?>',
            "<!--",
            f"  BannerKings translatable strings — {store}.",
            "  Generated by tools/extract_loc.py. Text is the English source.",
            "",
            "  Translate the text=\"...\" values ONLY. Never change an id.",
            "  Leave {VARIABLES} intact — you may reorder them freely, but a",
            "  dropped variable means that data never appears in-game, and you",
            "  cannot invent new ones.",
            "  Untranslated or omitted strings fall back to English, so a",
            "  partial translation is safe to ship.",
            "-->",
            '<base xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"',
            '      xmlns:xsd="http://www.w3.org/2001/XMLSchema"',
            '      type="string">',
            "  <tags>",
            f'    <tag language={quoteattr(lang_name)} />',
            "  </tags>",
            "  <strings>",
        ]
        last_src = None
        for loc_id, text, src in bucket:
            if src != last_src:
                lines.append(f"    <!-- {src} -->")
                last_src = src
            lines.append(
                f"    <string id={quoteattr(loc_id)} text={quoteattr(text)} />"
            )
        lines += ["  </strings>", "</base>", ""]

        with open(target, "w", encoding="utf-8") as fh:
            fh.write("\n".join(lines))
        written.append(f"bk_{store}.xml")
        print(f"  write {target}  ({len(bucket)} strings)")

    if written:
        ld_path = os.path.join(outdir, "language_data.xml")
        if os.path.exists(ld_path) and not force:
            print(f"  skip  {ld_path} (exists; pass --force to overwrite)")
        else:
            ld = [
                '<?xml version="1.0" encoding="utf-8"?>',
                "",
                f'<LanguageData id={quoteattr(lang_name)} name={quoteattr(lang_name)}>',
            ]
            ld += [f'\t<LanguageFile xml_path="{lang}/{w}" />' for w in written]
            ld += ["</LanguageData>", ""]
            with open(ld_path, "w", encoding="utf-8") as fh:
                fh.write("\n".join(ld))
            print(f"  write {ld_path}")
            print(
                "\n  NOTE: language_data.xml needs the subtitle_extension and\n"
                "  supported_iso attributes for your language — copy them from\n"
                "  Languages/DE/language_data.xml and adjust."
            )
    return written


def shipped_language_dirs():
    if not os.path.isdir(LANG_DIR):
        return []
    out = []
    for name in sorted(os.listdir(LANG_DIR)):
        path = os.path.join(LANG_DIR, name)
        if os.path.isdir(path):
            out.append((name, path))
    return out


def read_shipped_ids(path):
    """Return {id: text} for every bk_* string id in a language directory."""
    found = {}
    for fname in sorted(os.listdir(path)):
        if not fname.endswith(".xml") or fname == "language_data.xml":
            continue
        # These are the two general TaleWorlds string catalogues, not BKData
        # structural translations.  They legitimately contain a number of
        # bk_* ids that are defined directly in C# and must not be reported as
        # orphaned BKData rows.
        if fname in {"common_strings.xml", "std_module_strings_xml.xml"}:
            continue
        try:
            root = ET.parse(os.path.join(path, fname)).getroot()
        except ET.ParseError:
            continue
        for s in root.iter("string"):
            sid = s.get("id")
            if sid and sid.startswith("bk_"):
                found[sid] = (s.get("text") or "", fname)
    return found


def cmd_report(groups):
    known = all_ids(groups)
    total = len(known)
    print(f"BKData translatable strings: {total}\n")
    dirs = shipped_language_dirs()
    if not dirs:
        print("  (no language subdirectories found)")
        return 0
    for name, path in dirs:
        shipped = read_shipped_ids(path)
        covered = len(set(shipped) & known)
        orphans = len(set(shipped) - known)
        pct = (100.0 * covered / total) if total else 0.0
        line = f"  {name:<6} {covered}/{total} ({pct:.1f}%)"
        if orphans:
            line += f"  [{orphans} orphan id(s)]"
        print(line)
    return 0


def suspicious_unmapped(mapping, unmapped):
    """Unmapped files that nonetheless look like they carry display text.

    A category with no LocText wiring is normally structural-only and fine
    (<religions> binds ids to cultures). But if such a file DOES contain a
    field we know to be display text elsewhere, its strings are unreachable
    to translators and nothing else would report it.
    """
    text_fields = {f for spec in mapping.values() for f in spec["fields"]}
    hits = []
    for fname, _store in unmapped:
        try:
            root = ET.parse(os.path.join(BKDATA_DIR, fname)).getroot()
        except ET.ParseError:
            continue
        found = set()
        for row in root:
            for child in row:
                if child.tag in text_fields and "".join(child.itertext()).strip():
                    found.add(child.tag)
        if found:
            hits.append((fname, sorted(found)))
    return hits


def cmd_check(groups, dupes, notes, mapping=None, unmapped=None):
    problems = list(dupes)
    known = all_ids(groups)

    for fname, fields in suspicious_unmapped(mapping or {}, unmapped or []):
        problems.append(
            f"{fname}: has display text ({', '.join(fields)}) but no LocText "
            f"wiring in C# — these strings cannot be translated. Add a "
            f"BKXml.LocText call in the registry, or move the text out."
        )

    for name, path in shipped_language_dirs():
        shipped = read_shipped_ids(path)
        for sid, (_text, fname) in sorted(shipped.items()):
            if sid not in known:
                problems.append(
                    f"{name}/{fname}: orphan id '{sid}' — no BKData row "
                    f"produces it (entity renamed or removed?)"
                )

    for n in notes:
        print(f"  note: {n}")

    if problems:
        print(f"\nLocalization check FAILED — {len(problems)} problem(s):\n")
        for p in problems:
            print(f"  - {p}")
        return 1

    print(
        f"Localization check passed — {len(known)} extractable strings, "
        f"no orphan or duplicate ids."
    )
    return 0


def main():
    ap = argparse.ArgumentParser(
        description="Extract/verify BannerKings BKData translatable strings."
    )
    ap.add_argument("--lang", help="language directory to emit, e.g. FR")
    ap.add_argument(
        "--lang-name",
        help='<tag language="..."> value, e.g. "Français". Defaults to --lang.',
    )
    ap.add_argument("--force", action="store_true", help="overwrite existing files")
    ap.add_argument("--report", action="store_true", help="per-language coverage")
    ap.add_argument("--check", action="store_true", help="CI mode; exit 1 on problems")
    args = ap.parse_args()

    mapping, notes = derive_category_map()
    if not mapping:
        print(
            "No category mapping derived — run from the repo root "
            "(expected BannerKings/ and BKData/ to exist).",
            file=sys.stderr,
        )
        return 1
    groups, dupes, unmapped = collect_strings(mapping)

    if args.check:
        return cmd_check(groups, dupes, notes, mapping, unmapped)
    if args.report:
        return cmd_report(groups)
    if args.lang:
        print(
            f"Deriving ids from {len(mapping)} registries, "
            f"{len(all_ids(groups))} strings.\n"
        )
        write_language(args.lang, args.lang_name or args.lang, groups, args.force)
        return 0

    # Default: summarise what would be extracted.
    print(f"Category map ({len(mapping)} registries derived from C#):\n")
    for store in sorted(mapping):
        spec = mapping[store]
        count = len(groups.get(store, []))
        print(
            f"  <{store}>".ljust(26)
            + f"loc='{spec['loc']}'".ljust(26)
            + f"{count} strings"
        )
        print(f"  {'':<24}fields: {', '.join(spec['fields'])}")
    if unmapped:
        print("\nStructural-only files (no display text, nothing to translate):")
        for fname, store in unmapped:
            print(f"  {fname}  <{store}>")
    for n in notes:
        print(f"\n  note: {n}")
    print(f"\nTotal extractable strings: {len(all_ids(groups))}")
    print("\nRun with --lang XX --lang-name \"Name\" to emit a translation template.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
