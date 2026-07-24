from remap.sheets import Sheet
from remap.illutia_index import build_display_tile_index


def test_index_prefers_matching_slot_and_falls_back():
    header = ["id", "name", "slot", "graphic tile", "graphic file", "equip display"]
    rows = [
        [1, "Sword",      "OneHanded", 100.0, 5.0, 42.0],
        [2, "Sword2h",    "TwoHanded", 200.0, 6.0, 42.0],   # same Hand display, other slot
        [3, "Cap",        "Helmet",    300.0, 7.0, 60.0],
        [4, "NoDisplay",  "Misc",      400.0, 8.0, None],
    ]
    idx = build_display_tile_index(Sheet("Items", header, rows))
    assert idx.tile_for(("Hand", 42), prefer_slot="TwoHanded") == (200, 6)
    assert idx.tile_for(("Hand", 42), prefer_slot="OneHanded") == (100, 5)
    assert idx.tile_for(("Helm", 60), prefer_slot="Helmet") == (300, 7)
    assert idx.tile_for(("Helm", 60), prefer_slot=None) == (300, 7)      # any slot ok
    assert idx.tile_for(("Helm", 999), prefer_slot="Helmet") is None
