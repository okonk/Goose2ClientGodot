import re
from remap.sheets import intval
from remap.mappings import MAP_BASE

XENDRIA_COPY_SHEETS = ["Quests", "Quest Reqs", "Quest Rewards",
                       "Titles", "Surnames", "Classes",
                       "Class Info", "Class Levelup Spells"]

_MAPFILE = re.compile(r"^Map(\d+)\.map$")


def rename_map_files(maps_sheet):
    for row in maps_sheet.rows:
        m = _MAPFILE.match(str(maps_sheet.get(row, "filename") or ""))
        if m:
            maps_sheet.set(row, "filename", f"Map{MAP_BASE + int(m.group(1))}.map")


def _project_row(src_sheet, src_row, dst_sheet):
    """Copy cells from src_row into a new row aligned to dst_sheet's columns."""
    out = [None] * len(dst_sheet.header)
    for name, di in dst_sheet._cols.items():
        if name not in src_sheet._cols:
            continue
        si = src_sheet._cols[name]
        if si < len(src_row):
            out[di] = src_row[si]
    return out


# Contiguous free range after stock Aspereta items (max id 643).
QUEST_ITEM_ID_BASE = 644


def merge_xendria_quest_items(base_items, xen_items, quest_rewards, quest_reqs, warnings):
    """Append Xendria Items referenced by quest rewards/reqs that are missing
    from the base Items sheet.

    Xendria quest gear uses high ids (944–950); they are renumbered into the
    free 644+ range (after Aspereta's max 643) and quest reward/req references
    are rewritten. Graphics/displays are remapped later by transform_items.

    Returns {old_xendria_id: new_id} for the rows that were appended.
    """
    needed = set()
    for row in quest_rewards.rows:
        if str(quest_rewards.get(row, "reward type") or "").lower().startswith("item"):
            v = intval(quest_rewards.get(row, "long value"))
            if v:
                needed.add(v)
    for row in quest_reqs.rows:
        if "item" in str(quest_reqs.get(row, "requirement type") or "").lower():
            v = intval(quest_reqs.get(row, "value"))
            if v:
                needed.add(v)

    base_ids = {intval(base_items.get(r, "id")) for r in base_items.rows}
    xen_by_id = {intval(xen_items.get(r, "id")): r for r in xen_items.rows}

    id_map = {}
    next_id = QUEST_ITEM_ID_BASE
    while next_id in base_ids:
        next_id += 1

    for iid in sorted(needed):
        if iid in base_ids:
            continue
        xrow = xen_by_id.get(iid)
        if xrow is None:
            warnings.append(f"Merge: quest item {iid} missing from both workbooks")
            continue
        new_row = _project_row(xen_items, xrow, base_items)
        base_items.set(new_row, "id", next_id)
        base_items.rows.append(new_row)
        id_map[iid] = next_id
        base_ids.add(next_id)
        next_id += 1
        while next_id in base_ids:
            next_id += 1

    if id_map:
        for row in quest_rewards.rows:
            if not str(quest_rewards.get(row, "reward type") or "").lower().startswith("item"):
                continue
            v = intval(quest_rewards.get(row, "long value"))
            if v in id_map:
                quest_rewards.set(row, "long value", id_map[v])
        for row in quest_reqs.rows:
            if "item" not in str(quest_reqs.get(row, "requirement type") or "").lower():
                continue
            v = intval(quest_reqs.get(row, "value"))
            if v in id_map:
                quest_reqs.set(row, "value", id_map[v])

    return id_map


def _quest_referenced_npc_ids(xen_npcs, quest_reqs):
    """NPC ids that must exist for Xendria quests: givers (quest ids set) and
    TalkToNPC requirements. Kill targets are Aspereta template ids already in base."""
    needed = set()
    for row in xen_npcs.rows:
        q = xen_npcs.get(row, "quest ids")
        if q not in (None, ""):
            nid = intval(xen_npcs.get(row, "ID"))
            if nid:
                needed.add(nid)
    if quest_reqs is not None:
        for row in quest_reqs.rows:
            rt = str(quest_reqs.get(row, "requirement type") or "").lower()
            if "talk" in rt and "npc" in rt:
                v = intval(quest_reqs.get(row, "value"))
                if v:
                    needed.add(v)
    return needed


def merge_xendria_npcs(base_npcs, xen_npcs, base_spawns, xen_spawns,
                       base_maps, xen_maps, warnings, quest_reqs=None):
    """Keep Aspereta NPCs; pull in only quest-referenced Xendria NPCs.

    - Copy `quest ids` from Xendria onto shared NPCs (base wins for everything else).
    - Append Xendria-only NPCs that give quests or appear in TalkToNPC reqs.
    - Renumber those new NPCs consecutively after max(base id) (Aspereta ends at 182
      → 183…).
    - Append their spawns (npc id rewritten) and any missing map rows those spawns need.
    - Rewrite TalkToNPC values in quest_reqs to the new ids.

    Returns {old_xendria_id: new_id} for appended NPCs.
    """
    base_ids = {intval(base_npcs.get(r, "ID")) for r in base_npcs.rows}
    xen_by_id = {intval(xen_npcs.get(r, "ID")): r for r in xen_npcs.rows}

    for row in base_npcs.rows:
        nid = intval(base_npcs.get(row, "ID"))
        xrow = xen_by_id.get(nid)
        if xrow is not None:
            q = xen_npcs.get(xrow, "quest ids")
            if q not in (None, ""):
                base_npcs.set(row, "quest ids", q)

    needed = _quest_referenced_npc_ids(xen_npcs, quest_reqs)
    to_add = sorted(nid for nid in needed if nid not in base_ids)

    id_map = {}
    next_id = (max(base_ids) + 1) if base_ids else 1
    while next_id in base_ids:
        next_id += 1

    for old_id in to_add:
        xrow = xen_by_id.get(old_id)
        if xrow is None:
            warnings.append(f"Merge: quest NPC {old_id} missing from Xendria NPCs")
            continue
        new_row = _project_row(xen_npcs, xrow, base_npcs)
        base_npcs.set(new_row, "ID", next_id)
        base_npcs.rows.append(new_row)
        id_map[old_id] = next_id
        base_ids.add(next_id)
        next_id += 1
        while next_id in base_ids:
            next_id += 1

    for xrow in xen_spawns.rows:
        old = intval(xen_spawns.get(xrow, "npc id"))
        if old not in id_map:
            continue
        new_spawn = _project_row(xen_spawns, xrow, base_spawns)
        base_spawns.set(new_spawn, "npc id", id_map[old])
        base_spawns.rows.append(new_spawn)

    base_map_ids = {intval(base_maps.get(r, "id")) for r in base_maps.rows}
    # Only maps needed by the spawns we just added (re-scan whole sheet is fine:
    # base maps already covered; only missing ids get pulled from Xendria).
    needed_maps = {intval(base_spawns.get(r, "map id")) for r in base_spawns.rows}
    needed_maps -= base_map_ids
    xen_maps_by_id = {intval(xen_maps.get(r, "id")): r for r in xen_maps.rows}
    for mid in sorted(needed_maps):
        xrow = xen_maps_by_id.get(mid)
        if xrow is None:
            warnings.append(f"Merge: spawns reference map {mid} missing from both workbooks")
            continue
        base_maps.rows.append(_project_row(xen_maps, xrow, base_maps))

    if id_map and quest_reqs is not None:
        for row in quest_reqs.rows:
            rt = str(quest_reqs.get(row, "requirement type") or "").lower()
            if "talk" in rt and "npc" in rt:
                v = intval(quest_reqs.get(row, "value"))
                if v in id_map:
                    quest_reqs.set(row, "value", id_map[v])

    return id_map
