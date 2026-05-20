#!/usr/bin/env python3
"""
Validate BannerKings structural-data XML (ModuleData/BKData/*.xml).

Run from the repo root:  python tools/validate_bkdata.py

Catches the failure mode the XML data layer otherwise hides — a typo'd id
or ref drops a row silently at load instead of failing the build. Checks:

  1. Every file is well-formed XML.
  2. No duplicate <... id="..."> within a category.
  3. Every required attribute is present.
  4. Every discriminator key (type / behavior / flavor) is one BK ships.
  5. Every BKData-internal cross-reference resolves to a real id.

NOT checked (cannot be, from XML alone — these resolve at runtime against
the game and the C# registries): refs into CultureObject / TraitObject /
SkillObject / PolicyObject / PerkObject, demesne laws, council tasks,
demands, and rite keys. See docs/dev-reference/structural-schema.md.

Exit code 0 = clean, 1 = at least one problem (fails CI).
"""

import os
import sys
import xml.etree.ElementTree as ET

BKDATA_DIR = os.path.join("BannerKings", "_Module", "ModuleData", "BKData")

# Discriminator keys BK ships. Keep in sync with the C# registries and the
# behaviour/registry section of docs/dev-reference/structural-schema.md.
ENUMS = {
    "faith_group.type": {"Temporal", "Disorganized", "LandedPreacher"},
    "title_name.type": {"Empire", "Kingdom", "Duchy", "County",
                        "Barony", "Lordship", "Prince", "Knight"},
    "faith.flavor": {"Monotheistic", "Polytheistic", "Henotheistic", "Dualistic"},
    "innovation.type": {"Civic", "Agriculture", "Military", "Technology", "Building"},
    "succession.behavior": {"AseraiElective", "Dictatorship", "Imperial",
                            "Hereditary", "Republic", "TheocraticElective",
                            "BattanianElective", "FeudalElective",
                            "TribalElective", "WilundingElective"},
    "casus_belli.behavior": {"Rebellion", "SuppressThreat", "CulturalLiberation",
                             "FiefClaim", "HolyWar", "DivineReclamation",
                             "Invasion", "GreatRaid", "ImperialSuperiority",
                             "ImperialReconquest"},
    "mercenary_privilege.behavior": {"IncreasedPay", "WorkshopGrant", "EstateGrant",
                                     "CustomTroop3", "CustomTroop5", "BaronyGrant",
                                     "FullPeerage"},
    "council_position.behavior": {"LegionCommander", "Marshal", "Steward",
                                  "Chancellor", "Spymaster", "Spiritual", "Spouse",
                                  "CourtPhysician", "CourtSmith", "CourtMusician",
                                  "Antiquarian", "Castellan", "Constable"},
}

# Per category: the row element name, required attributes, and which
# attributes / nested elements are BKData-internal cross-references.
#   "attr_refs":  {attribute-on-the-row: target-category}
#   "child_refs": [(child-path, attribute-on-the-child, target-category)]
SCHEMA = {
    "doctrines": {
        "row": "doctrine", "required": ["id"],
        "child_refs": [("incompatible/doctrine", "ref", "doctrines")],
    },
    "divinities": {"row": "divinity", "required": ["id"]},
    "faith_groups": {"row": "faith_group", "required": ["id", "type"]},
    "marriage_doctrines": {"row": "marriage_doctrine", "required": ["id"]},
    "war_doctrines": {
        "row": "war_doctrine", "required": ["id"],
        "child_refs": [("justifications/justification", "casus_belli", "casus_belli")],
    },
    "faiths": {
        "row": "faith",
        "required": ["id", "flavor", "main_god", "group",
                     "marriage_doctrine", "war_doctrine"],
        "attr_refs": {"main_god": "divinities", "group": "faith_groups",
                      "marriage_doctrine": "marriage_doctrines",
                      "war_doctrine": "war_doctrines"},
        "child_refs": [("pantheon/divinity", "ref", "divinities"),
                       ("doctrines/doctrine", "ref", "doctrines")],
    },
    "religions": {
        "row": "religion", "required": ["id", "faith"],
        "attr_refs": {"faith": "faiths"},
    },
    "eras": {
        "row": "era", "required": ["id"],
        "attr_refs": {"previous": "eras"},
    },
    "innovations": {
        "row": "innovation", "required": ["id", "type", "era"],
        "attr_refs": {"era": "eras", "requirement": "innovations"},
    },
    "lifestyles": {
        "row": "lifestyle", "required": ["id", "first_skill", "second_skill"],
    },
    "inheritances": {"row": "inheritance", "required": ["id"]},
    "gender_laws": {"row": "gender_law", "required": ["id"]},
    "title_names": {"row": "title_name", "required": ["id", "type"]},
    "successions": {"row": "succession", "required": ["id", "behavior"]},
    "governments": {
        "row": "government", "required": ["id"],
        "child_refs": [("successions/succession", "ref", "successions")],
    },
    "interest_groups": {
        "row": "interest_group", "required": ["id", "main_trait"],
        "child_refs": [("casus_belli/cb", "ref", "casus_belli")],
    },
    "mercenary_privileges": {"row": "mercenary_privilege", "required": ["id", "behavior"]},
    "casus_belli": {"row": "casus_belli", "required": ["id", "behavior"]},
    "council_positions": {
        "row": "council_position", "required": ["id", "behavior", "primary_skill"],
    },
}


def main():
    if not os.path.isdir(BKDATA_DIR):
        print(f"ERROR: {BKDATA_DIR} not found — run from the repo root.")
        return 1

    errors = []
    # category -> { id: source-file }
    ids = {cat: {} for cat in SCHEMA}
    # (category, file, root-element) for the cross-ref pass
    parsed = []

    # Pass 1 — parse, collect ids, flag duplicates / unknown categories.
    for fname in sorted(os.listdir(BKDATA_DIR)):
        if not fname.endswith(".xml"):
            continue
        path = os.path.join(BKDATA_DIR, fname)
        try:
            root = ET.parse(path).getroot()
        except ET.ParseError as ex:
            errors.append(f"{fname}: malformed XML — {ex}")
            continue

        category = root.tag
        if category not in SCHEMA:
            errors.append(f"{fname}: unknown root element <{category}> "
                          f"(not a known BKData category)")
            continue

        row_name = SCHEMA[category]["row"]
        for row in root:
            if row.tag is ET.Comment:
                continue
            if row.tag != row_name:
                errors.append(f"{fname}: unexpected element <{row.tag}> "
                              f"(expected <{row_name}>)")
                continue
            rid = row.get("id")
            if rid:
                if rid in ids[category]:
                    errors.append(f"{fname}: duplicate id '{rid}' in "
                                  f"category '{category}' "
                                  f"(also in {ids[category][rid]})")
                else:
                    ids[category][rid] = fname
        parsed.append((category, fname, root))

    # Pass 2 — required attributes, enum keys, cross-references.
    for category, fname, root in parsed:
        spec = SCHEMA[category]
        for row in root:
            if row.tag is ET.Comment or row.tag != spec["row"]:
                continue
            rid = row.get("id") or "<no id>"

            for attr in spec.get("required", []):
                if not (row.get(attr) or "").strip():
                    errors.append(f"{fname}: {spec['row']} '{rid}' "
                                  f"missing required attribute '{attr}'")

            for attr in ("type", "behavior", "flavor"):
                key = f"{spec['row']}.{attr}"
                val = row.get(attr)
                if val and key in ENUMS and val not in ENUMS[key]:
                    errors.append(f"{fname}: {spec['row']} '{rid}' has "
                                  f"unknown {attr} '{val}' — expected one of "
                                  f"{sorted(ENUMS[key])}")

            for attr, target in spec.get("attr_refs", {}).items():
                val = row.get(attr)
                if val and val not in ids[target]:
                    errors.append(f"{fname}: {spec['row']} '{rid}' "
                                  f"{attr}='{val}' does not resolve to any "
                                  f"'{target}' id")

            for child_path, child_attr, target in spec.get("child_refs", []):
                for child in row.findall(child_path):
                    val = child.get(child_attr)
                    if val and val not in ids[target]:
                        errors.append(f"{fname}: {spec['row']} '{rid}' "
                                      f"<{child.tag} {child_attr}='{val}'> "
                                      f"does not resolve to any '{target}' id")

    if errors:
        print(f"BKData validation FAILED — {len(errors)} problem(s):\n")
        for e in errors:
            print(f"  - {e}")
        return 1

    total = sum(len(v) for v in ids.values())
    print(f"BKData validation passed — {len(parsed)} files, {total} rows, "
          f"all cross-references resolve.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
