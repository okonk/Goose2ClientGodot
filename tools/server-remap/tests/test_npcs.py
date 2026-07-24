from remap.sheets import Sheet
from remap.core import Remapper
from remap.mappings import ItemMapEntry, BODY_BASE
from remap.npcs import transform_npcs, remap_equip_string

HDR = ["ID", "name", "body id (1)", "hair id (0)", "face id (0)",
       "equipped items (0,*,0,*,0,*,0,*,0,*,0,*)"]

def make_remapper():
    items = {
        ("Body", 165): ItemMapEntry(ill=("Body", 120), dye=None),
        ("Body", 101): ItemMapEntry(ill=None, dye=None),
        ("Hair", 70): ItemMapEntry(ill=("Eyes", 1), dye=None),
        ("Hair", 3): ItemMapEntry(ill=None, dye=None),
        ("Chest", 11): ItemMapEntry(ill=("Chest", 15), dye=(192, 28, 40)),
        ("Helm", 4): ItemMapEntry(ill=("Helm", 12), dye=None),
    }
    return Remapper({}, items)

def test_body_match_inject_and_hair_to_eyes():
    rows = [
        [1, "Pig",    165.0, None, None, None],
        [2, "Ghost",  101.0, None, None, None],
        [3, "Guy",    1.0,   70.0, None, None],
        [4, "Gal",    1.0,   3.0,  None, None],
    ]
    r = make_remapper()
    r.items[("Body", 1)] = ItemMapEntry(ill=("Body", 1), dye=None)
    sheet = Sheet("NPCs", HDR, rows)
    transform_npcs(sheet, r)
    assert sheet.rows[0][2] == 120                       # matched body
    assert sheet.rows[1][2] == BODY_BASE + 101           # injected body
    assert sheet.rows[2][3] == 0 and sheet.rows[2][4] == 1   # hair -> face
    assert sheet.rows[3][3] == 0                         # unmapped hair cleared
    assert any("hair" in w.lower() for w in r.warnings)

def test_equip_string_remap_with_dye_and_whitespace():
    r = make_remapper()
    s = "11,*,4,148,231,148,160,0,*,0,*,0,*, 4,*"
    out = remap_equip_string(s, r, where="NPCs ID=27")
    # pos1 Chest 11 -> 15 with dye (was '*'); pos2 Helm 4 -> 12 keeps its colour;
    # pos6 Weapon Hand 4 unmapped -> warn, kept
    assert out == "15,192,28,40,180,12,148,231,148,160,0,*,0,*,0,*,4,*"
    assert any("Hand:4" in w for w in r.warnings)

def test_equip_string_empty_passthrough():
    r = make_remapper()
    assert remap_equip_string(None, r, where="x") is None
    assert remap_equip_string("", r, where="x") == ""
