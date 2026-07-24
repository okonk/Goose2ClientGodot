from remap.sheets import intval
from remap.mappings import SLOT_TO_TYPE


class DisplayTileIndex:
    def __init__(self):
        self._by_key = {}     # (type, display) -> list of (slot, tile, file)

    def add(self, typ, display, slot, tile, file):
        self._by_key.setdefault((typ, display), []).append((slot, tile, file))

    def tile_for(self, key, prefer_slot):
        cands = self._by_key.get(key)
        if not cands:
            return None
        for slot, tile, file in cands:
            if prefer_slot is not None and slot == prefer_slot:
                return (tile, file)
        slot, tile, file = cands[0]
        return (tile, file)


def build_display_tile_index(items_sheet):
    idx = DisplayTileIndex()
    for row in items_sheet.rows:
        slot = str(items_sheet.get(row, "slot") or "")
        typ = SLOT_TO_TYPE.get(slot)
        display = intval(items_sheet.get(row, "equip display"))
        if typ is None or not display:
            continue
        tile = intval(items_sheet.get(row, "graphic tile"))
        file = intval(items_sheet.get(row, "graphic file"))
        if tile:
            idx.add(typ, display, slot, tile, file)
    return idx
