from remap.sheets import Sheet
from remap.merge import rename_map_files, merge_xendria_npcs, XENDRIA_COPY_SHEETS


def test_map_filename_rename():
    hdr = ["id", "name", "filename"]
    sheet = Sheet("Maps", hdr, [[1, "Town", "Map1.map"], [44, "Pit", "Map44.map"]])
    rename_map_files(sheet)
    assert sheet.rows[0][2] == "Map10001.map"
    assert sheet.rows[1][2] == "Map10044.map"


def test_copy_sheet_list_matches_notes_doc():
    assert XENDRIA_COPY_SHEETS == ["Quests", "Quest Reqs", "Quest Rewards",
                                   "Titles", "Surnames", "Classes",
                                   "Class Info", "Class Levelup Spells"]


def test_merge_xendria_npcs_adds_new_spawns_and_quest_ids():
    npc_hdr = ["ID", "name", "quest ids"]
    base_npcs = Sheet("NPCs", npc_hdr, [[98, "Elder", None], [99, "Sage", None]])
    xen_npcs = Sheet("NPCs", npc_hdr,
                     [[98, "Elder", 2.0], [99, "Sage", 3.0], [183, "QuestGuy", 4.0]])
    spawn_hdr = ["npc id", "map id", "map x", "map y"]
    base_spawns = Sheet("NPC Spawns", spawn_hdr, [[98, 1, 5, 5]])
    xen_spawns = Sheet("NPC Spawns", spawn_hdr,
                       [[98, 1, 5, 5], [183, 47, 3, 3], [183, 1, 9, 9]])
    maps_hdr = ["id", "name", "filename"]
    base_maps = Sheet("Maps", maps_hdr, [[1, "Town", "Map1.map"]])
    xen_maps = Sheet("Maps", maps_hdr,
                     [[1, "Town", "Map1.map"], [47, "Maze2", "Map3.map"]])
    warnings = []
    merge_xendria_npcs(base_npcs, xen_npcs, base_spawns, xen_spawns,
                       base_maps, xen_maps, warnings)
    assert [r[0] for r in base_npcs.rows] == [98, 99, 183]       # new npc appended
    assert base_npcs.rows[0][2] == 2.0                           # quest ids copied
    assert len(base_spawns.rows) == 3                            # 2 new spawn rows
    assert [r[0] for r in base_maps.rows] == [1, 47]             # alias map row added
