from remap.sheets import intval, is_blank, cell_int
from remap.mappings import (
    BODY_BASE, DYE_ALPHA, EQUIP_WIRE_TYPES,
    ILLUTIA_BODY_UNARMED, ILLUTIA_BODY_1HAND, ILLUTIA_BODY_STAFF,
)


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


def remap_npc_body_state(body_id, body_state):
    """Map Aspereta NPC body_state → Illutia equip pose.

    Aspereta protocol/data (aspdata.db, aspereta-info/protocol.txt):
      1 = normal / unarmed (default; 142 NPCs)
      3 = staff (mages/priests with staves)
      4 = sword / 1hand (warriors with swords/daggers/axes)
    Illutia client (AnimationNames.cs): 3=unarmed, 4=1hand, 5=staff, 6=2hand, 7=bow.

    Only called when body_state was non-blank. Monsters (body_id >= 100,
    including injected 10000+) always unarmed. ASP 2 is unused in real data
    (compiled had 4 columns); treat as generic 1hand if present. Values 5–7
    already look Illutia-range and are kept for re-run safety.
    """
    if body_id is not None and body_id >= 100:
        return ILLUTIA_BODY_UNARMED
    if not body_state or body_state <= 0:
        return ILLUTIA_BODY_UNARMED
    if body_state == 1:
        return ILLUTIA_BODY_UNARMED
    if body_state == 2:
        return ILLUTIA_BODY_1HAND
    if body_state == 3:
        return ILLUTIA_BODY_STAFF
    if body_state == 4:
        return ILLUTIA_BODY_1HAND
    if body_state in (5, 6, 7):
        return body_state
    return ILLUTIA_BODY_UNARMED


def remap_face(face_id, remapper, where):
    """Aspereta face ids 70–73 are eye sprites stored as Hair in the item mapping
    (Hair→Eyes matches). Illutia face/eye ids are small (1–18).

    Returns None to clear an unmapped non-blank face (leave cell empty).
    """
    if not face_id:
        return None
    entry = remapper.items.get(("Hair", face_id))
    if entry is not None and entry.ill is not None and entry.ill[0] == "Eyes":
        return entry.ill[1]
    entry = remapper.items.get(("Eyes", face_id))
    if entry is not None:
        if entry.ill is None:
            remapper.warn(f"{where}: face {face_id} inject/no Illutia art; cleared")
            return None
        return entry.ill[1]
    # Already an Illutia-range face/eye id.
    if 1 <= face_id <= 30:
        return face_id
    remapper.warn(f"{where}: face {face_id} unmapped; cleared")
    return None


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

        # body id: only rewrite when the source cell was non-blank
        if not is_blank(sheet.get(row, "body id")):
            body = cell_int(sheet.get(row, "body id"))
            if body is not None:
                sheet.set(row, "body id", remap_body(body, remapper, where))

        # body state: only rewrite when non-blank (blank → server/header default)
        if not is_blank(sheet.get(row, "body state")):
            bs = cell_int(sheet.get(row, "body state"))
            body_now = cell_int(sheet.get(row, "body id"))
            sheet.set(row, "body state",
                      remap_npc_body_state(body_now, bs if bs is not None else 0))

        if not is_blank(sheet.get(row, "hair id")):
            hair = cell_int(sheet.get(row, "hair id"))
            if hair:
                entry = remapper.items.get(("Hair", hair))
                if entry is not None and entry.ill is not None:
                    ill_type, ill_id = entry.ill
                    if ill_type == "Eyes":
                        sheet.set(row, "face id", ill_id)
                        sheet.set(row, "hair id", None)  # blank, not 0
                    else:
                        sheet.set(row, "hair id", ill_id)
                else:
                    remapper.warn(f"{where}: hair {hair} unmapped; cleared")
                    sheet.set(row, "hair id", None)

        # Face column is independent of hair (Aspereta faces 70–73 live here).
        if not is_blank(sheet.get(row, "face id")):
            face = cell_int(sheet.get(row, "face id"))
            if face is not None:
                sheet.set(row, "face id", remap_face(face, remapper, where))

        eq = sheet.get(row, "equipped items")
        if not is_blank(eq):
            sheet.set(row, "equipped items", remap_equip_string(eq, remapper, where))
