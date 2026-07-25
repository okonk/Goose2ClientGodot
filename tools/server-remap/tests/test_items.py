from remap.sheets import Sheet
from remap.core import Remapper
from remap.mappings import ItemMapEntry, illutia_body_state
from remap.items import transform_items

HDR = ["id", "name", "slot (Misc)", "type (None)", "graphic tile", "graphic file",
       "equip display", "r", "g", "b", "a", "body state (1)"]

def run(rows, items_map, graphics, ill_index):
    sheet = Sheet("Items", HDR, rows)
    r = Remapper(graphics, items_map)
    transform_items(sheet, r, ill_index)
    return sheet, r

class FakeIndex:
    def __init__(self, table): self.table = table
    def tile_for(self, key, prefer_slot): return self.table.get(key)

def test_matched_display_copies_illutia_tile_and_dye_respects_existing_colour():
    rows = [
        [10, "Boots",  "Shoes", None, 120100.0, None, 1.0, None, None, None, None, 0],
        [11, "Boots2", "Shoes", None, 120100.0, None, 1.0, None, None, None, 160.0, 0],
    ]
    items_map = {("Feet", 1): ItemMapEntry(ill=("Feet", 2), dye=(181, 131, 90))}
    ill_index = FakeIndex({("Feet", 2): (555, 9)})
    sheet, r = run(rows, items_map, {120100: (20486, 820100)}, ill_index)
    # row 10: colour unset -> dye applied; tile from illutia index; display -> ill_id
    assert sheet.rows[0][4:11] == [555, 9, 2, 181, 131, 90, 180]
    # row 11: alpha set -> dye NOT applied
    assert sheet.rows[1][7:11] == [None, None, None, 160.0]
    # non-weapon: body state untouched
    assert sheet.get(sheet.rows[0], "body state") == 0

def test_cross_slot_match_rewrites_slot():
    rows = [[27, "Wings", "Chest", "Plate", 120200.0, None, 27.0, None, None, None, None, 0]]
    items_map = {("Chest", 27): ItemMapEntry(ill=("Helm", 60), dye=None)}
    ill_index = FakeIndex({("Helm", 60): (777, 12)})
    sheet, r = run(rows, items_map, {120200: (20001, 700200)}, ill_index)
    assert sheet.get(sheet.rows[0], "slot") == "Helmet"
    assert sheet.get(sheet.rows[0], "equip display") == 60
    assert (sheet.rows[0][4], sheet.rows[0][5]) == (777, 12)

def test_inject_display_keeps_display_warns_and_remaps_tile_via_graphics():
    rows = [[50, "Odd Hat", "Helmet", None, 120300.0, None, 99.0, None, None, None, None, 0]]
    items_map = {("Helm", 99): ItemMapEntry(ill=None, dye=None)}
    sheet, r = run(rows, items_map, {120300: (20002, 700300)}, FakeIndex({}))
    assert sheet.get(sheet.rows[0], "equip display") == 99.0     # unchanged
    # graphics returns (out_sheet, out_graphic) -> tile=graphic, file=sheet
    assert (sheet.rows[0][4], sheet.rows[0][5]) == (700300, 20002)
    assert any("no Illutia art" in w for w in r.warnings)

def test_non_display_item_tile_only():
    rows = [[1, "Gold", None, None, 120100.0, None, None, None, None, None, None, 0]]
    sheet, r = run(rows, {}, {120100: (20486, 820100)}, FakeIndex({}))
    assert (sheet.rows[0][4], sheet.rows[0][5]) == (820100, 20486)
    assert r.warnings == []

def test_weapon_body_state_remapped_to_illutia_poses():
    # Aspereta 2h staves were body_state=3 (anim column) — must become staff=5, not unarmed.
    rows = [
        [1, "Stick", "OneHanded", "OneHandedSword", 120015.0, None, 4.0, None, None, None, None, 4],
        [2, "Stave", "TwoHanded", "TwoHandedBlunt", 120021.0, None, 5.0, None, None, None, None, 3],
        [3, "Claymore", "TwoHanded", "TwoHandedSword", 120015.0, None, 4.0, None, None, None, None, 3],
        [4, "Spear", "TwoHanded", "TwoHandedPierce", 120013.0, None, 1.0, None, None, None, None, 3],
        [5, "Shield", "Shield", None, 120001.0, None, 1.0, None, None, None, None, 4],
        [6, "Empty1h", "OneHanded", "OneHandedBlunt", 120039.0, None, 8.0, None, None, None, None, 0],
        [7, "Blank1h", "OneHanded", "OneHandedSword", 120015.0, None, 4.0, None, None, None, None, None],
    ]
    sheet, r = run(rows, {}, {
        120015: (20001, 700015), 120021: (20001, 700021),
        120013: (20001, 700013), 120001: (20001, 700001),
        120039: (20001, 700039),
    }, FakeIndex({}))
    assert sheet.get(sheet.rows[0], "body state") == 4   # 1hand
    assert sheet.get(sheet.rows[1], "body state") == 5   # staff
    assert sheet.get(sheet.rows[2], "body state") == 6   # 2hand
    assert sheet.get(sheet.rows[3], "body state") == 6   # 2hand spear
    assert sheet.get(sheet.rows[4], "body state") == 4   # shield
    assert sheet.get(sheet.rows[5], "body state") == 4   # explicit 0 still remapped
    assert sheet.get(sheet.rows[6], "body state") is None  # blank stays blank


def test_illutia_body_state_helper():
    assert illutia_body_state("OneHanded", "OneHandedSword") == 4
    assert illutia_body_state("TwoHanded", "TwoHandedBlunt") == 5
    assert illutia_body_state("TwoHanded", "TwoHandedSword") == 6
    assert illutia_body_state("TwoHanded", "Bow") == 7
    assert illutia_body_state("Shield", "") == 4
    assert illutia_body_state("Chest", "Plate") is None
    assert illutia_body_state("Shoes", None) is None
