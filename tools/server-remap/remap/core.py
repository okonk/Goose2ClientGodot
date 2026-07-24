from remap.sheets import intval


class Remapper:
    def __init__(self, graphics, items):
        self.graphics = graphics      # asp_graphic -> (out_sheet, out_graphic)
        self.items = items            # (asp_type, asp_id) -> ItemMapEntry
        self.warnings = []

    def warn(self, msg):
        self.warnings.append(msg)

    def tile(self, asp_graphic, where):
        """Remap a tile/icon graphic id. Returns (out_sheet, out_graphic) or None."""
        hit = self.graphics.get(asp_graphic)
        if hit is None:
            self.warn(f"{where}: graphic {asp_graphic} not in graphics mapping")
        return hit

    def display(self, asp_type, asp_id, where):
        """Remap an equip-display/compiled id. Returns ((ill_type, ill_id), dye)
        for matches, None for inject/unknown (caller applies its inject rule)."""
        entry = self.items.get((asp_type, asp_id))
        if entry is None:
            self.warn(f"{where}: display {asp_type}:{asp_id} not in item mapping")
            return None
        if entry.ill is None:
            return None
        return entry.ill, entry.dye

    @staticmethod
    def colour_is_set(r_, g, b, a):
        # Server treats GraphicA == 0 as untinted regardless of r/g/b.
        return bool(intval(a))
