import os
from remap.mappings import (load_item_mapping, load_graphics_mapping,
                            SLOT_TO_TYPE, TYPE_TO_SLOT,
                            GRAPHIC_BASE, SHEET_BASE, BODY_BASE, MAP_BASE,
                            EFFECT_BASE, DYE_ALPHA, parse_dye)

FIX = os.path.join(os.path.dirname(__file__), "fixtures")

def test_constants_match_asset_pipeline():
    assert (GRAPHIC_BASE, SHEET_BASE, BODY_BASE, MAP_BASE) == (700000, 20000, 10000, 10000)
    assert EFFECT_BASE == 700000
    assert DYE_ALPHA == 180

def test_item_mapping_loads_matches_only():
    m = load_item_mapping(os.path.join(FIX, "item-mapping.tsv"))
    assert m[("Body", 1)].ill == ("Body", 1) and m[("Body", 1)].dye is None
    assert m[("Chest", 27)].ill == ("Helm", 60)          # cross-slot
    assert m[("Feet", 1)].dye == (181, 131, 90)          # #b5835a
    assert ("Hair", 6) not in m                          # match-unreviewed = skip
    assert m[("Body", 100)].ill is None                  # inject row kept, ill=None

def test_graphics_mapping_keyed_by_graphic_id():
    g = load_graphics_mapping(os.path.join(FIX, "graphics-mapping.tsv"))
    assert g[1200] == (2275, 331900)     # matched -> (out_sheet, out_graphic)
    assert g[1201] == (20000, 701201)    # inject
    assert g[120100] == (20486, 820100)

def test_slot_type_tables():
    assert SLOT_TO_TYPE["Helmet"] == "Helm"
    assert SLOT_TO_TYPE["OneHanded"] == SLOT_TO_TYPE["TwoHanded"] == SLOT_TO_TYPE["Shield"] == "Hand"
    assert SLOT_TO_TYPE["Pants"] == "Legs" and SLOT_TO_TYPE["Shoes"] == "Feet"
    assert TYPE_TO_SLOT["Helm"] == "Helmet"   # inverse for non-ambiguous types only

def test_parse_dye():
    assert parse_dye("#B5835A") == (181, 131, 90)
    assert parse_dye("") is None and parse_dye(None) is None
