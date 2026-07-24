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


def merge_xendria_npcs(base_npcs, xen_npcs, base_spawns, xen_spawns,
                       base_maps, xen_maps, warnings):
    """Per docs/plans/2026-07-24-aspereta-item-mapping-notes.md:
    add Xendria-only NPCs + their spawns + alias Maps rows; copy quest ids onto
    shared NPCs; base wins for everything else."""
    base_ids = {intval(base_npcs.get(r, "ID")) for r in base_npcs.rows}
    xen_by_id = {intval(xen_npcs.get(r, "ID")): r for r in xen_npcs.rows}

    for row in base_npcs.rows:
        nid = intval(base_npcs.get(row, "ID"))
        xrow = xen_by_id.get(nid)
        if xrow is not None:
            q = xen_npcs.get(xrow, "quest ids")
            if q not in (None, ""):
                base_npcs.set(row, "quest ids", q)

    new_ids = set()
    for nid, xrow in sorted(xen_by_id.items()):
        if nid not in base_ids:
            base_npcs.rows.append(list(xrow))
            new_ids.add(nid)

    for xrow in xen_spawns.rows:
        if intval(xen_spawns.get(xrow, "npc id")) in new_ids:
            base_spawns.rows.append(list(xrow))

    base_map_ids = {intval(base_maps.get(r, "id")) for r in base_maps.rows}
    needed = {intval(base_spawns.get(r, "map id")) for r in base_spawns.rows}
    needed -= base_map_ids
    xen_maps_by_id = {intval(xen_maps.get(r, "id")): r for r in xen_maps.rows}
    for mid in sorted(needed):
        xrow = xen_maps_by_id.get(mid)
        if xrow is None:
            warnings.append(f"Merge: spawns reference map {mid} missing from both workbooks")
            continue
        base_maps.rows.append(list(xrow))
