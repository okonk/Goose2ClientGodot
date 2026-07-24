from remap.sheets import Sheet
from remap.core import Remapper
from remap.mappings import ItemMapEntry, EFFECT_BASE, BODY_BASE
from remap.spells import transform_spells, transform_spell_effects


def test_spells_spellbook_tile():
    hdr = ["spell id", "name", "spellbook graphic", "graphic file"]
    sheet = Sheet("Spells", hdr, [[1, "Heal", 130001.0, None]])
    r = Remapper({130001: (20010, 703001)}, {})
    transform_spells(sheet, r)
    assert sheet.rows[0][2:4] == [703001, 20010]


def test_spell_effects_animation_offset_buff_tile_and_morph():
    hdr = ["effect id", "name", "animation (0)", "animation file (0)",
           "attack animation (0)", "cast animation (1)",
           "body id", "hair id", "face id",
           "buff graphic", "buff graphic file"]
    rows = [[5, "Fire", 35001.0, None, 0.0, 1.0, 101.0, None, None, 130002.0, None]]
    r = Remapper({130002: (20011, 703002)},
                 {("Body", 101): ItemMapEntry(ill=None, dye=None)})
    sheet = Sheet("Spell Effects", hdr, rows)
    transform_spell_effects(sheet, r)
    assert sheet.rows[0][2] == EFFECT_BASE + 35001
    assert sheet.rows[0][3] is None                      # animation file untouched
    assert sheet.rows[0][4:6] == [0.0, 1.0]              # booleans untouched
    assert sheet.rows[0][6] == BODY_BASE + 101           # morph body injected
    assert sheet.rows[0][9:11] == [703002, 20011]


def test_spell_effect_zero_animation_untouched():
    hdr = ["effect id", "name", "animation (0)", "animation file (0)",
           "body id", "hair id", "face id", "buff graphic", "buff graphic file"]
    sheet = Sheet("Spell Effects", hdr, [[6, "Buff", None, None, None, None, None, None, None]])
    r = Remapper({}, {})
    transform_spell_effects(sheet, r)
    assert sheet.rows[0][2] is None
