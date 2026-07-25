from remap.sheets import canon, Sheet

def test_canon_strips_default_annotation():
    assert canon("slot (Misc)") == "slot"
    assert canon("type (Monster)") == "type"
    assert canon("graphic tile") == "graphic tile"
    assert canon("equipped items (0,*,0,*,0,*,0,*,0,*,0,*)") == "equipped items"
    assert canon(None) == ""

def test_sheet_column_lookup_and_cell_access():
    header = ["id", "name", "slot (Misc)", "equip display"]
    rows = [[1.0, "Stick", "OneHanded", 6.0], [2.0, "Gold", None, None]]
    s = Sheet("Items", header, rows)
    assert s.col("slot") == 2
    assert s.get(rows[0], "equip display") == 6.0
    assert s.get(rows[1], "equip display") is None
    s.set(rows[1], "name", "Coins")
    assert rows[1][1] == "Coins"

def test_intval_normalizes_floats_and_blanks():
    from remap.sheets import intval, cell_int, is_blank
    assert intval(6.0) == 6
    assert intval("6") == 6
    assert intval(None) == 0
    assert intval("") == 0
    assert intval("*") is None   # non-numeric -> None (caller decides)
    assert is_blank(None) and is_blank("")
    assert not is_blank(0) and not is_blank(1)
    assert cell_int(None) is None
    assert cell_int("") is None
    assert cell_int(6.0) == 6
    assert cell_int(0) == 0
