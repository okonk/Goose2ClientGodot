"""Header canonicalization and mutable sheet wrapper.

Workbook headers embed default annotations ("slot (Misc)") that differ between
the Aspereta/Illutia/Xendria exports; canon() strips them so lookups are stable.
"""
import re

_ANNOT = re.compile(r"\s*\(.*\)\s*$")


def canon(header):
    if header is None:
        return ""
    return _ANNOT.sub("", str(header)).strip()


def intval(v):
    """Cell -> int. Empty/None -> 0. Non-numeric text -> None."""
    if v is None or v == "":
        return 0
    try:
        return int(float(v))
    except (TypeError, ValueError):
        return None


class Sheet:
    def __init__(self, name, header, rows):
        self.name = name
        self.header = list(header)
        self.rows = [list(r) for r in rows]
        self._cols = {}
        for i, h in enumerate(self.header):
            c = canon(h)
            if c and c not in self._cols:
                self._cols[c] = i

    def col(self, name):
        return self._cols[name]

    def get(self, row, name):
        i = self.col(name)
        return row[i] if i < len(row) else None

    def set(self, row, name, value):
        i = self.col(name)
        while len(row) <= i:
            row.append(None)
        row[i] = value
