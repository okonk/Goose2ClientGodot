#!/usr/bin/env python3
"""Convert Aspereta MapN.map files into Illutia/Goose2 binary maps as Map{10000+N}.map.

Server Illutia mode loads DataPath/Maps/<map_filename> with IllutiaMapLoader
(Int16 ver, Int16 editor, Int32 w/h, then per-tile Int32 flags + 5×(Int32 graphic, Int16 sheet)).
The remapped workbook uses filenames Map10001.map … Map10044.map.

Mirrors tools/AssetConverter AsperetaMapConverter:
  - blocked byte 1 → flags 2
  - layers 0,1,2 → goose layers 0,1,2; goose layer 3 empty; asp layer 3 → goose layer 4
  - each nonzero graphic remapped via aspereta-mapping.tsv (missing → 0 + warning)

Usage (from tools/server-remap):
  python3 remap_maps.py
  python3 remap_maps.py --src ... --dst ... --mapping ...
"""
from __future__ import annotations

import argparse
import csv
import os
import re
import struct
import sys

REPO = os.path.normpath(os.path.join(os.path.dirname(__file__), "..", ".."))
MAP_BASE = 10000
_MAP_NAME = re.compile(r"^Map(\d+)\.map$", re.I)

DEFAULTS = {
    "src": "/home/hayden/code/illutiagooseserver/Goose/Data/Aspereta/Maps",
    "dst": "/home/hayden/code/illutiagooseserver/Goose/Data/Illutia/Maps",
    "mapping": os.path.join(REPO, "tools/AssetConverter/data/aspereta-mapping.tsv"),
}


def load_graphics_mapping(path: str) -> dict[int, tuple[int, int]]:
    """asp_graphic -> (out_sheet, out_graphic)."""
    out: dict[int, tuple[int, int]] = {}
    with open(path, newline="") as f:
        for row in csv.DictReader(f, delimiter="\t"):
            out[int(row["asp_graphic"])] = (int(row["out_sheet"]), int(row["out_graphic"]))
    return out


def convert_map(src_path: str, dst_path: str, graphics: dict[int, tuple[int, int]]) -> list[str]:
    """Convert one Aspereta map to Illutia format. Returns warning strings."""
    warnings: list[str] = []
    basename = os.path.basename(src_path)

    with open(src_path, "rb") as f:
        data = f.read()

    if len(data) < 4 + 100 * 100 * 17:
        raise ValueError(f"{basename}: too short ({len(data)} bytes)")

    ver, editor = struct.unpack_from("<hh", data, 0)
    off = 4
    out = bytearray()
    out += struct.pack("<hhii", ver, editor, 100, 100)

    for _ in range(100 * 100):
        blocked = data[off]
        off += 1
        layers = struct.unpack_from("<4i", data, off)
        off += 16

        flags = 2 if blocked == 1 else 0
        out += struct.pack("<i", flags)

        # asp 0,1,2 → goose 0,1,2; goose 3 empty; asp 3 → goose 4
        src_for_out = (0, 1, 2, -1, 3)
        for src in src_for_out:
            if src < 0:
                out += struct.pack("<ih", 0, 0)
                continue
            graphic = layers[src]
            if graphic == 0:
                out += struct.pack("<ih", 0, 0)
                continue
            hit = graphics.get(graphic)
            if hit is None:
                warnings.append(f"{basename}: graphic {graphic} not in mapping table, dropped")
                out += struct.pack("<ih", 0, 0)
            else:
                out_sheet, out_graphic = hit
                out += struct.pack("<ih", out_graphic, out_sheet)

    os.makedirs(os.path.dirname(dst_path) or ".", exist_ok=True)
    with open(dst_path, "wb") as f:
        f.write(out)
    return warnings


def run(src: str, dst: str, mapping: str) -> int:
    if not os.path.isdir(src):
        print(f"error: source maps dir not found: {src}", file=sys.stderr)
        return 1
    if not os.path.isfile(mapping):
        print(f"error: mapping tsv not found: {mapping}", file=sys.stderr)
        return 1

    graphics = load_graphics_mapping(mapping)
    files = sorted(
        f for f in os.listdir(src)
        if _MAP_NAME.match(f)
    )
    if not files:
        print(f"error: no Map*.map files in {src}", file=sys.stderr)
        return 1

    os.makedirs(dst, exist_ok=True)
    converted = 0
    all_warnings: list[str] = []
    failures: list[str] = []

    for name in files:
        m = _MAP_NAME.match(name)
        assert m
        number = int(m.group(1))
        out_name = f"Map{MAP_BASE + number}.map"
        src_path = os.path.join(src, name)
        dst_path = os.path.join(dst, out_name)
        try:
            warns = convert_map(src_path, dst_path, graphics)
            converted += 1
            all_warnings.extend(warns)
            print(f"  {name} -> {out_name}")
        except Exception as e:
            failures.append(f"{name}: {type(e).__name__} {e}")

    # de-dupe warnings but keep count
    uniq = sorted(set(all_warnings))
    print(f"\nconverted {converted}/{len(files)} maps -> {dst}")
    if failures:
        print(f"{len(failures)} failures:")
        for f in failures:
            print(f"  FAIL {f}")
    if uniq:
        print(f"{len(all_warnings)} warnings ({len(uniq)} unique):")
        for w in uniq:
            print(f"  WARN {w}")
    return 1 if failures else 0


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--src", default=os.environ.get("ASP_MAPS", DEFAULTS["src"]),
                   help="Aspereta Maps directory (MapN.map)")
    p.add_argument("--dst", default=os.environ.get("ILL_MAPS", DEFAULTS["dst"]),
                   help="Illutia Maps directory (output Map1000N.map)")
    p.add_argument("--mapping", default=os.environ.get("GFX_MAP", DEFAULTS["mapping"]),
                   help="aspereta-mapping.tsv path")
    args = p.parse_args()
    sys.exit(run(args.src, args.dst, args.mapping))


if __name__ == "__main__":
    main()
