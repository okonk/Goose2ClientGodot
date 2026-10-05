#!/usr/bin/env python3
"""Move layer-2 graphics off walkable tiles whose below-tile is blocked up to
layer 3 (which renders at z=30, above characters, unlike the y-sorted layer 2).
Hit predicate matches tools/find-layer2-above-blocked.py. A hit whose layer 3
already holds a graphic is reported as a conflict and left untouched. Edits the
tile bytes in place, preserving headers, trailing data, and all other tiles.

Wire layout (src/MapEditor.Core/MapCodec.cs): 12-byte header, then 34 bytes per
tile row-major: Int32 flags, then five layers of Int32 graphic + Int16 sheet.
Blocked = flags & 2. Layer n graphic at tile offset 4 + n*6."""

import argparse
import struct
import sys

HEADER_SIZE = 12
BYTES_PER_TILE = 34
BLOCKED_FLAG = 2
LAYER2_OFF = 4 + 2 * 6
LAYER3_OFF = 4 + 3 * 6


def process(path, dry_run, reverse=False):
    with open(path, "rb") as f:
        data = bytearray(f.read())
    if len(data) < HEADER_SIZE:
        raise ValueError(f"{path}: truncated header")
    _, editor_version, width, height = struct.unpack_from("<hhii", data, 0)
    if editor_version not in (3, 10):
        raise ValueError(f"{path}: unsupported editor version {editor_version}")
    needed = HEADER_SIZE + BYTES_PER_TILE * width * height
    if len(data) < needed:
        raise ValueError(f"{path}: expected {needed} bytes, got {len(data)}")

    src, dst = (LAYER3_OFF, LAYER2_OFF) if reverse else (LAYER2_OFF, LAYER3_OFF)
    moved = conflicts = 0
    for y in range(height - 1):
        row = HEADER_SIZE + y * width * BYTES_PER_TILE
        next_row = row + width * BYTES_PER_TILE
        for x in range(width):
            here = row + x * BYTES_PER_TILE
            if struct.unpack_from("<i", data, here)[0] & BLOCKED_FLAG:
                continue
            if struct.unpack_from("<i", data, here + src)[0] == 0:
                continue
            below = struct.unpack_from("<i", data, next_row + x * BYTES_PER_TILE)[0]
            if not below & BLOCKED_FLAG:
                continue
            if struct.unpack_from("<i", data, here + dst)[0] != 0:
                conflicts += 1
                print(f"  CONFLICT ({x}, {y}): destination layer occupied, left untouched")
                continue
            data[here + dst:here + dst + 6] = data[here + src:here + src + 6]
            data[here + src:here + src + 6] = bytes(6)
            moved += 1

    if moved and not dry_run:
        with open(path, "wb") as f:
            f.write(data)
    return width, height, moved, conflicts


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("maps", nargs="+", help="map files, e.g. Assets/Maps/Map10013.map")
    ap.add_argument("--dry-run", action="store_true", help="report what would move; write nothing")
    ap.add_argument("--reverse", action="store_true",
                    help="move layer 3 back down to layer 2 (undoes the forward move)")
    args = ap.parse_args()

    total = 0
    for path in args.maps:
        try:
            width, height, moved, conflicts = process(path, args.dry_run, args.reverse)
        except (OSError, ValueError) as e:
            print(f"ERROR: {e}", file=sys.stderr)
            continue
        verb = "would move" if args.dry_run else "moved"
        print(f"{path}  {width}x{height}  {verb}: {moved}  conflicts: {conflicts}")
        total += moved
    print(f"\ntotal tiles affected: {total}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
