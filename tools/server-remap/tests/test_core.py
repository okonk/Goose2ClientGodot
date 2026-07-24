from remap.core import Remapper
from remap.mappings import ItemMapEntry


def make_remapper():
    graphics = {120100: (20486, 820100), 331900: None}  # value None never occurs; keys are asp ids
    graphics = {120100: (20486, 820100)}
    items = {
        ("Chest", 3): ItemMapEntry(ill=("Chest", 13), dye=None),
        ("Chest", 27): ItemMapEntry(ill=("Helm", 60), dye=None),
        ("Feet", 1): ItemMapEntry(ill=("Feet", 2), dye=(181, 131, 90)),
        ("Body", 100): ItemMapEntry(ill=None, dye=None),
        ("Hair", 70): ItemMapEntry(ill=("Eyes", 1), dye=None),
    }
    return Remapper(graphics, items)


def test_tile_known_and_unknown():
    r = make_remapper()
    assert r.tile(120100, where="Items id=1") == (20486, 820100)
    assert r.tile(999999, where="Items id=2") is None
    assert any("999999" in w for w in r.warnings)


def test_display_match_inject_and_missing():
    r = make_remapper()
    assert r.display("Chest", 3, where="x") == (("Chest", 13), None)
    assert r.display("Chest", 27, where="x") == (("Helm", 60), None)
    assert r.display("Feet", 1, where="x") == (("Feet", 2), (181, 131, 90))
    assert r.display("Body", 100, where="x") is None          # inject -> caller keeps asp id + BODY_BASE etc.
    assert r.display("Chest", 999, where="x") is None
    assert any("Chest:999" in w for w in r.warnings)


def test_colour_set_semantics():
    # server sends '*' iff a == 0 (Inventory.cs:702)
    r = make_remapper()
    assert r.colour_is_set(r_=None, g=None, b=None, a=None) is False
    assert r.colour_is_set(r_=None, g=None, b=None, a=0) is False
    assert r.colour_is_set(r_=None, g=None, b=None, a=180) is True   # black tint counts
    assert r.colour_is_set(r_=20, g=65, b=30, a=160) is True
