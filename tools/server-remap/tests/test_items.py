from remap.sheets import Sheet
from remap.core import Remapper
from remap.mappings import ItemMapEntry
from remap.items import transform_items

HDR = ["id", "name", "slot (Misc)", "graphic tile", "graphic file",
       "equip display", "r", "g", "b", "a"]

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
        [10, "Boots",  "Shoes", 120100.0, None, 1.0, None, None, None, None],
        [11, "Boots2", "Shoes", 120100.0, None, 1.0, None, None, None, 160.0],
    ]
    items_map = {("Feet", 1): ItemMapEntry(ill=("Feet", 2), dye=(181, 131, 90))}
    ill_index = FakeIndex({("Feet", 2): (555, 9)})
    sheet, r = run(rows, items_map, {120100: (20486, 820100)}, ill_index)
    # row 10: colour unset -> dye applied; tile from illutia index; display -> ill_id
    assert sheet.rows[0][3:10] == [555, 9, 2, 181, 131, 90, 180]
    # row 11: alpha set -> dye NOT applied
    assert sheet.rows[1][6:10] == [None, None, None, 160.0]

def test_cross_slot_match_rewrites_slot():
    rows = [[27, "Wings", "Chest", 120200.0, None, 27.0, None, None, None, None]]
    items_map = {("Chest", 27): ItemMapEntry(ill=("Helm", 60), dye=None)}
    ill_index = FakeIndex({("Helm", 60): (777, 12)})
    sheet, r = run(rows, items_map, {120200: (20001, 700200)}, ill_index)
    assert sheet.get(sheet.rows[0], "slot") == "Helmet"
    assert sheet.get(sheet.rows[0], "equip display") == 60
    assert (sheet.rows[0][3], sheet.rows[0][4]) == (777, 12)

def test_inject_display_keeps_display_warns_and_remaps_tile_via_graphics():
    rows = [[50, "Odd Hat", "Helmet", 120300.0, None, 99.0, None, None, None, None]]
    items_map = {("Helm", 99): ItemMapEntry(ill=None, dye=None)}
    sheet, r = run(rows, items_map, {120300: (20002, 700300)}, FakeIndex({}))
    assert sheet.get(sheet.rows[0], "equip display") == 99.0     # unchanged
    # graphics returns (out_sheet, out_graphic) -> tile=graphic, file=sheet
    assert (sheet.rows[0][3], sheet.rows[0][4]) == (700300, 20002)
    assert any("no Illutia art" in w for w in r.warnings)

def test_non_display_item_tile_only():
    rows = [[1, "Gold", None, 120100.0, None, None, None, None, None, None]]
    sheet, r = run(rows, {}, {120100: (20486, 820100)}, FakeIndex({}))
    assert (sheet.rows[0][3], sheet.rows[0][4]) == (820100, 20486)
    assert r.warnings == []
