from remap.sheets import Sheet
from remap.core import Remapper
from remap.mappings import ItemMapEntry, BODY_BASE
from remap.npcs import transform_npcs, remap_equip_string, remap_npc_body_state

HDR = ["ID", "name", "body id (1)", "body state (3)", "hair id (0)", "face id (0)",
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
        [1, "Pig",    165.0, 0, None, None, None],
        [2, "Ghost",  101.0, 4, None, None, None],
        [3, "Guy",    1.0,   0, 70.0, None, None],
        [4, "Gal",    1.0,   4, 3.0,  None, None],
        [5, "Mage",   1.0,   3, None, None, None],
        [6, "Odd",    1.0,   2, None, None, None],
        [7, "Vendor", None, None, 20.0, 71.0, None],  # blank body must stay blank
    ]
    r = make_remapper()
    r.items[("Body", 1)] = ItemMapEntry(ill=("Body", 1), dye=None)
    r.items[("Hair", 20)] = ItemMapEntry(ill=("Hair", 31), dye=None)
    r.items[("Hair", 71)] = ItemMapEntry(ill=("Eyes", 1), dye=None)
    sheet = Sheet("NPCs", HDR, rows)
    transform_npcs(sheet, r)
    assert sheet.rows[0][2] == 120                       # matched body
    assert sheet.rows[1][2] == BODY_BASE + 101           # injected body
    assert sheet.get(sheet.rows[0], "body state") == 3   # explicit 0 → unarmed
    assert sheet.get(sheet.rows[1], "body state") == 3   # monster inject → unarmed
    assert sheet.get(sheet.rows[2], "body state") == 3   # explicit 0 → unarmed
    assert sheet.get(sheet.rows[3], "body state") == 4   # ASP sword 4 → Illutia 1hand
    assert sheet.get(sheet.rows[4], "body state") == 5   # ASP staff 3 → Illutia staff
    assert sheet.get(sheet.rows[5], "body state") == 4   # ASP unused 2 → 1hand
    assert sheet.get(sheet.rows[2], "hair id") is None and sheet.get(sheet.rows[2], "face id") == 1
    assert sheet.get(sheet.rows[3], "hair id") is None   # unmapped hair cleared to blank
    assert sheet.get(sheet.rows[6], "body id") is None   # blank body stays blank
    assert sheet.get(sheet.rows[6], "body state") is None
    assert any("hair" in w.lower() for w in r.warnings)


def test_remap_npc_body_state_helper():
    # ASP 1 = normal → Illutia unarmed (3); ASP 3 = staff → 5; ASP 4 = sword → 4
    assert remap_npc_body_state(1, 0) == 3
    assert remap_npc_body_state(1, 1) == 3
    assert remap_npc_body_state(1, 2) == 4
    assert remap_npc_body_state(1, 3) == 5
    assert remap_npc_body_state(1, 4) == 4
    assert remap_npc_body_state(1, 5) == 5   # already Illutia-range
    assert remap_npc_body_state(10101, 4) == 3   # monster
    assert remap_npc_body_state(150, 4) == 3
    assert remap_npc_body_state(None, 3) == 5   # no body id: still map ASP staff


def test_npc_face_id_remaps_aspereta_faces():
    from remap.npcs import remap_face
    r = make_remapper()
    r.items[("Hair", 70)] = ItemMapEntry(ill=("Eyes", 1), dye=None)
    r.items[("Hair", 71)] = ItemMapEntry(ill=("Eyes", 1), dye=None)
    r.items[("Hair", 72)] = ItemMapEntry(ill=("Eyes", 3), dye=None)
    r.items[("Hair", 73)] = ItemMapEntry(ill=("Eyes", 2), dye=None)
    assert remap_face(71, r, "x") == 1
    assert remap_face(72, r, "x") == 3
    assert remap_face(1, r, "x") == 1    # already Illutia
    assert remap_face(99, r, "x") is None  # unknown cleared to blank
    assert any("face 99" in w for w in r.warnings)

    hdr = ["ID", "name", "body id (1)", "body state (3)", "hair id (0)", "face id (0)",
           "equipped items"]
    rows = [
        [1, "Guy", 1.0, 3, None, 71.0, None],
        [2, "Gal", 1.0, 3, None, 72.0, None],
    ]
    r2 = make_remapper()
    r2.items[("Body", 1)] = ItemMapEntry(ill=("Body", 1), dye=None)
    r2.items[("Hair", 71)] = ItemMapEntry(ill=("Eyes", 1), dye=None)
    r2.items[("Hair", 72)] = ItemMapEntry(ill=("Eyes", 3), dye=None)
    sheet = Sheet("NPCs", hdr, rows)
    transform_npcs(sheet, r2)
    assert sheet.get(sheet.rows[0], "face id") == 1
    assert sheet.get(sheet.rows[1], "face id") == 3

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
