from remap.sheets import intval, is_blank, cell_int
from remap.mappings import SLOT_TO_TYPE, TYPE_TO_SLOT, DYE_ALPHA, illutia_body_state


def transform_items(sheet, remapper, ill_index):
    for row in sheet.rows:
        item_id = intval(sheet.get(row, "id"))
        where = f"Items id={item_id}"
        slot = str(sheet.get(row, "slot") or "")
        item_type = str(sheet.get(row, "type") or "")
        typ = SLOT_TO_TYPE.get(slot)
        display = cell_int(sheet.get(row, "equip display"))

        tile_written = False
        if typ is not None and display:
            hit = remapper.display(typ, display, where)
            if hit is not None:
                (ill_type, ill_id), dye = hit
                sheet.set(row, "equip display", ill_id)
                if ill_type != typ and ill_type in TYPE_TO_SLOT:
                    sheet.set(row, "slot", TYPE_TO_SLOT[ill_type])
                    slot = TYPE_TO_SLOT[ill_type]
                # dye only when the item has no colour of its own (a == 0/empty)
                if dye is not None and not remapper.colour_is_set(
                        sheet.get(row, "r"), sheet.get(row, "g"),
                        sheet.get(row, "b"), sheet.get(row, "a")):
                    sheet.set(row, "r", dye[0])
                    sheet.set(row, "g", dye[1])
                    sheet.set(row, "b", dye[2])
                    sheet.set(row, "a", DYE_ALPHA)
                ill_tile = ill_index.tile_for((ill_type, ill_id), prefer_slot=slot)
                if ill_tile is not None:
                    sheet.set(row, "graphic tile", ill_tile[0])
                    sheet.set(row, "graphic file", ill_tile[1])
                    tile_written = True
                else:
                    remapper.warn(f"{where}: no Illutia item carries display "
                                  f"{ill_type}:{ill_id}; falling back to tile mapping")
            else:
                if (typ, display) in remapper.items:   # inject row: no Illutia art
                    remapper.warn(f"{where}: display {typ}:{display} has no Illutia art "
                                  f"(inject) — item will render unequipped look")

        if not tile_written and not is_blank(sheet.get(row, "graphic tile")):
            tile = cell_int(sheet.get(row, "graphic tile"))
            if tile:
                out = remapper.tile(tile, where)
                if out is not None:
                    sheet.set(row, "graphic tile", out[1])
                    sheet.set(row, "graphic file", out[0])

        # Body state: only rewrite when the source cell was non-blank.
        # Blank weapons keep blank (importer/header default applies).
        if not is_blank(sheet.get(row, "body state")):
            slot = str(sheet.get(row, "slot") or "")
            pose = illutia_body_state(slot, item_type)
            if pose is not None:
                sheet.set(row, "body state", pose)
