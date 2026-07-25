from remap.sheets import Sheet
from remap.merge import (rename_map_files, merge_xendria_npcs,
                         merge_xendria_quest_items, XENDRIA_COPY_SHEETS)


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


def test_merge_xendria_npcs_quest_referenced_only_and_renumbers():
    npc_hdr = ["ID", "name", "quest ids"]
    # Aspereta ends at 182; junk Xendria-only 200 has no quests and must not merge.
    base_npcs = Sheet("NPCs", npc_hdr, [
        [98, "Elder", None], [99, "Sage", None], [182, "LastAsp", None],
    ])
    xen_npcs = Sheet("NPCs", npc_hdr, [
        [98, "Elder", 2.0], [99, "Sage", 3.0],
        [200, "JunkMob", None],
        [311, "Bruno", 1.0], [316, "Jill", 8.0],
    ])
    spawn_hdr = ["npc id", "map id", "map x", "map y"]
    base_spawns = Sheet("NPC Spawns", spawn_hdr, [[98, 1, 5, 5]])
    xen_spawns = Sheet("NPC Spawns", spawn_hdr, [
        [98, 1, 5, 5],
        [200, 1, 1, 1],           # not quest-related — drop
        [311, 36, 60, 72],
        [316, 16, 66, 54],
    ])
    maps_hdr = ["id", "name", "filename"]
    base_maps = Sheet("Maps", maps_hdr, [
        [1, "Town", "Map1.map"], [16, "Cave", "Map16.map"], [36, "Train", "Map36.map"],
    ])
    xen_maps = Sheet("Maps", maps_hdr, list(base_maps.rows) + [[47, "Alias", "Map3.map"]])
    reqs = Sheet("Quest Reqs",
                 ["id", "quest id", "requirement type", "value"],
                 [[1, 7, "TalkToNPC", 316], [2, 5, "Kill", 1]])
    warnings = []
    id_map = merge_xendria_npcs(base_npcs, xen_npcs, base_spawns, xen_spawns,
                                base_maps, xen_maps, warnings, quest_reqs=reqs)
    assert id_map == {311: 183, 316: 184}                 # consecutive after 182
    assert [r[0] for r in base_npcs.rows] == [98, 99, 182, 183, 184]
    assert base_npcs.rows[0][2] == 2.0                    # quest ids on shared
    assert base_npcs.rows[3][1] == "Bruno" and base_npcs.rows[3][0] == 183
    assert [r[0] for r in base_spawns.rows] == [98, 183, 184]  # renumbered; junk dropped
    assert [r[0] for r in base_maps.rows] == [1, 16, 36]  # no alias maps needed
    assert reqs.rows[0][3] == 184                         # TalkToNPC rewritten
    assert reqs.rows[1][3] == 1                           # Kill target untouched
    assert warnings == []


def test_merge_xendria_npcs_skips_shared_quest_givers():
    npc_hdr = ["ID", "name", "quest ids"]
    base_npcs = Sheet("NPCs", npc_hdr, [[98, "Magus", None]])
    xen_npcs = Sheet("NPCs", npc_hdr, [[98, "Magus", 2.0]])
    spawns = Sheet("NPC Spawns", ["npc id", "map id"], [[98, 1]])
    maps = Sheet("Maps", ["id", "name", "filename"], [[1, "T", "Map1.map"]])
    id_map = merge_xendria_npcs(base_npcs, xen_npcs, spawns, spawns, maps, maps, [])
    assert id_map == {}
    assert base_npcs.rows[0][2] == 2.0
    assert len(base_npcs.rows) == 1


def test_merge_xendria_quest_items_adds_missing_reward_and_req_items():
    item_hdr = ["id", "name", "slot (Misc)", "graphic tile", "equip display"]
    # base max id 643 so renumber starts at QUEST_ITEM_ID_BASE (644)
    base_items = Sheet("Items", item_hdr, [[643, "Last stock", None, 100, None]])
    # xen has extra padding columns; merge projects onto base header
    xen_hdr = item_hdr + ["extra"]
    xen_items = Sheet("Items", xen_hdr, [
        [643, "Last stock", None, 100, None, "x"],
        [944, "Dusty Shoes", "Shoes", 120005, 1, "x"],
        [949, "Wool", None, 120601, None, "x"],
        [999, "Unused", None, 1, None, "x"],  # not referenced — must not merge
    ])
    rewards = Sheet("Quest Rewards",
                    ["id", "quest id", "reward type", "long value"],
                    [[1, 1, "Item", 944], [2, 1, "Experience", 1000]])
    reqs = Sheet("Quest Reqs",
                 ["id", "quest id", "requirement type", "value"],
                 [[1, 6, "Item", 949], [2, 6, "Gold", 50]])
    warnings = []
    id_map = merge_xendria_quest_items(base_items, xen_items, rewards, reqs, warnings)
    assert id_map == {944: 644, 949: 645}
    ids = [r[0] for r in base_items.rows]
    assert ids == [643, 644, 645]
    dusty = base_items.rows[1]
    assert dusty[1:5] == ["Dusty Shoes", "Shoes", 120005, 1]
    assert len(dusty) == len(item_hdr)  # projected to base width
    assert rewards.rows[0][3] == 644    # reward long value rewritten
    assert rewards.rows[1][3] == 1000   # non-item untouched
    assert reqs.rows[0][3] == 645       # req value rewritten
    assert warnings == []


def test_merge_xendria_quest_items_keeps_existing_and_warns_if_absent():
    item_hdr = ["id", "name"]
    base_items = Sheet("Items", item_hdr, [[944, "Already here"]])
    xen_items = Sheet("Items", item_hdr, [[944, "Xendria copy"]])
    rewards = Sheet("Quest Rewards",
                    ["id", "quest id", "reward type", "long value"],
                    [[1, 1, "Item", 944], [2, 1, "Item", 950]])
    reqs = Sheet("Quest Reqs", ["id", "quest id", "requirement type", "value"], [])
    warnings = []
    id_map = merge_xendria_quest_items(base_items, xen_items, rewards, reqs, warnings)
    assert id_map == {}
    assert [r[0] for r in base_items.rows] == [944]  # not duplicated
    assert base_items.rows[0][1] == "Already here"   # base wins
    assert rewards.rows[0][3] == 944                 # existing id left alone
    assert any("quest item 950" in w for w in warnings)


def test_merge_xendria_quest_items_skips_occupied_644():
    item_hdr = ["id", "name"]
    base_items = Sheet("Items", item_hdr, [[644, "Occupied"]])
    xen_items = Sheet("Items", item_hdr, [[944, "Dusty Shoes"]])
    rewards = Sheet("Quest Rewards",
                    ["id", "quest id", "reward type", "long value"],
                    [[1, 1, "Item", 944]])
    reqs = Sheet("Quest Reqs", ["id", "quest id", "requirement type", "value"], [])
    id_map = merge_xendria_quest_items(base_items, xen_items, rewards, reqs, [])
    assert id_map == {944: 645}
    assert base_items.rows[-1] == [645, "Dusty Shoes"]
    assert rewards.rows[0][3] == 645
