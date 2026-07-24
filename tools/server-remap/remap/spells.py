from remap.sheets import intval
from remap.mappings import EFFECT_BASE
from remap.npcs import remap_body


def _tile_cells(sheet, row, remapper, graphic_col, file_col, where):
    g = intval(sheet.get(row, graphic_col))
    if g:
        out = remapper.tile(g, where)
        if out is not None:
            sheet.set(row, graphic_col, out[1])
            sheet.set(row, file_col, out[0])


def transform_spells(sheet, remapper):
    for row in sheet.rows:
        where = f"Spells id={intval(sheet.get(row, 'spell id'))}"
        _tile_cells(sheet, row, remapper, "spellbook graphic", "graphic file", where)


def transform_spell_effects(sheet, remapper):
    for row in sheet.rows:
        where = f"Spell Effects id={intval(sheet.get(row, 'effect id'))}"
        anim = intval(sheet.get(row, "animation"))
        if anim:
            sheet.set(row, "animation", EFFECT_BASE + anim)
        _tile_cells(sheet, row, remapper, "buff graphic", "buff graphic file", where)
        body = intval(sheet.get(row, "body id"))
        if body:
            sheet.set(row, "body id", remap_body(body, remapper, where))
        hair = intval(sheet.get(row, "hair id"))
        if hair:
            entry = remapper.items.get(("Hair", hair))
            if entry is not None and entry.ill is not None:
                ill_type, ill_id = entry.ill
                if ill_type == "Eyes":
                    sheet.set(row, "face id", ill_id)
                    sheet.set(row, "hair id", 0)
                else:
                    sheet.set(row, "hair id", ill_id)
            else:
                remapper.warn(f"{where}: morph hair {hair} unmapped; cleared")
                sheet.set(row, "hair id", 0)
