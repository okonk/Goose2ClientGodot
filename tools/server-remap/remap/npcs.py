from remap.sheets import intval
from remap.mappings import BODY_BASE, DYE_ALPHA, EQUIP_WIRE_TYPES


def remap_body(body_id, remapper, where):
    if not body_id:
        return body_id
    entry = remapper.items.get(("Body", body_id))
    if entry is None:
        remapper.warn(f"{where}: body {body_id} not in mapping; kept")
        return body_id
    if entry.ill is None:
        return BODY_BASE + body_id
    return entry.ill[1]


def remap_equip_string(s, remapper, where):
    """Remap Inventory.EquippedDisplay() wire format (Inventory.cs:691-721):
    6 entries, each 'id,*' or 'id,r,g,b,a', ordered Chest,Head,Legs,Feet,Shield,Weapon."""
    if not s:
        return s
    parts = [p.strip() for p in str(s).split(",")]
    out = []
    i = 0
    for typ in EQUIP_WIRE_TYPES:
        if i >= len(parts):
            break
        disp = intval(parts[i])
        i += 1
        colour = None
        if i < len(parts) and parts[i] == "*":
            i += 1
        else:
            colour = parts[i:i + 4]
            i += 4
        if disp:
            hit = remapper.display(typ, disp, where)
            if hit is not None:
                (ill_type, ill_id), dye = hit
                disp = ill_id
                if colour is None and dye is not None:
                    colour = [str(dye[0]), str(dye[1]), str(dye[2]), str(DYE_ALPHA)]
        out.append(str(disp))
        out.extend(colour if colour is not None else ["*"])
    return ",".join(out)


def transform_npcs(sheet, remapper):
    for row in sheet.rows:
        npc_id = intval(sheet.get(row, "ID"))
        where = f"NPCs ID={npc_id}"
        sheet.set(row, "body id", remap_body(intval(sheet.get(row, "body id")), remapper, where))

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
                remapper.warn(f"{where}: hair {hair} unmapped; cleared to 0")
                sheet.set(row, "hair id", 0)

        eq = sheet.get(row, "equipped items")
        if eq:
            sheet.set(row, "equipped items", remap_equip_string(eq, remapper, where))
