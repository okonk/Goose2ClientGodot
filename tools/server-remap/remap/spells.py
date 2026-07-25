from remap.sheets import intval, is_blank, cell_int
from remap.mappings import EFFECT_BASE
from remap.npcs import remap_body, remap_face

# Aspereta typos: animation id written into buff graphic (Haste effect 253).
BUFF_GRAPHIC_FIXUPS = {
    115021: 110027,  # Haste → same icon as Haste V/X/XX/XXX
}


def _tile_cells(sheet, row, remapper, graphic_col, file_col, where):
    if is_blank(sheet.get(row, graphic_col)):
        return
    g = cell_int(sheet.get(row, graphic_col))
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

        if not is_blank(sheet.get(row, "animation")):
            anim = cell_int(sheet.get(row, "animation"))
            if anim:
                sheet.set(row, "animation", EFFECT_BASE + anim)

        if not is_blank(sheet.get(row, "buff graphic")):
            buff = cell_int(sheet.get(row, "buff graphic"))
            if buff in BUFF_GRAPHIC_FIXUPS:
                sheet.set(row, "buff graphic", BUFF_GRAPHIC_FIXUPS[buff])
            _tile_cells(sheet, row, remapper, "buff graphic", "buff graphic file", where)

        if not is_blank(sheet.get(row, "body id")):
            body = cell_int(sheet.get(row, "body id"))
            if body:
                sheet.set(row, "body id", remap_body(body, remapper, where))

        if not is_blank(sheet.get(row, "hair id")):
            hair = cell_int(sheet.get(row, "hair id"))
            if hair:
                entry = remapper.items.get(("Hair", hair))
                if entry is not None and entry.ill is not None:
                    ill_type, ill_id = entry.ill
                    if ill_type == "Eyes":
                        sheet.set(row, "face id", ill_id)
                        sheet.set(row, "hair id", None)
                    else:
                        sheet.set(row, "hair id", ill_id)
                else:
                    remapper.warn(f"{where}: morph hair {hair} unmapped; cleared")
                    sheet.set(row, "hair id", None)

        if not is_blank(sheet.get(row, "face id")):
            face = cell_int(sheet.get(row, "face id"))
            if face is not None:
                sheet.set(row, "face id", remap_face(face, remapper, where))
