"""Loads the two committed mapping tables and holds the fixed id bases.

Bases MUST equal AsperetaSheets constants in the asset pipeline
(docs/plans/2026-07-20-aspereta-asset-pipeline.md Task 2).
"""
import csv
from dataclasses import dataclass

GRAPHIC_BASE = 700000   # injected frame/graphic ids
SHEET_BASE = 20000      # injected sheet numbers
BODY_BASE = 10000       # injected monster body ids
MAP_BASE = 10000        # renamed map files
EFFECT_BASE = 700000    # injected effect animation ids (separate namespace from graphics)
DYE_ALPHA = 180         # alpha written when applying a mapping dye (Illutia convention)

# Wire order Chest, Head, Legs, Feet, Shield, Weapon: Inventory.cs:694-695.
# Shield/Weapon draw from Hand art: Character.cs:104.
SLOT_TO_TYPE = {
    "Chest": "Chest", "Helmet": "Helm", "Pants": "Legs", "Shoes": "Feet",
    "OneHanded": "Hand", "TwoHanded": "Hand", "Shield": "Hand",
}
TYPE_TO_SLOT = {"Chest": "Chest", "Helm": "Helmet", "Legs": "Pants", "Feet": "Shoes"}

# equipped-items wire positions -> animation type
EQUIP_WIRE_TYPES = ["Chest", "Helm", "Legs", "Feet", "Hand", "Hand"]


def parse_dye(s):
    if not s:
        return None
    s = s.lstrip("#")
    return tuple(int(s[i:i + 2], 16) for i in (0, 2, 4))


@dataclass(frozen=True)
class ItemMapEntry:
    ill: tuple | None       # (ill_type, ill_id) or None for inject
    dye: tuple | None       # (r, g, b) or None


def load_item_mapping(path):
    """(asp_type, asp_id) -> ItemMapEntry. 'match-unreviewed' rows are dropped
    (reviewed decision: those are known-bad shape matches; see
    docs/plans/2026-07-24-aspereta-item-mapping-notes.md)."""
    out = {}
    with open(path, newline="") as f:
        for row in csv.DictReader(f, delimiter="\t"):
            key = (row["asp_type"], int(row["asp_id"]))
            if row["decision"] == "match":
                out[key] = ItemMapEntry(
                    ill=(row["ill_type"], int(row["ill_id"])),
                    dye=parse_dye(row.get("dye", "")))
            elif row["decision"] == "inject":
                out[key] = ItemMapEntry(ill=None, dye=None)
            # match-unreviewed: skip entirely
    return out


def load_graphics_mapping(path):
    """asp_graphic -> (out_sheet, out_graphic). Aspereta frame ids are globally
    unique (AdfManager.cs:33-36), so the sheet column is not needed for lookup."""
    out = {}
    with open(path, newline="") as f:
        for row in csv.DictReader(f, delimiter="\t"):
            out[int(row["asp_graphic"])] = (int(row["out_sheet"]), int(row["out_graphic"]))
    return out
