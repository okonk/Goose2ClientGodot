#!/usr/bin/env python3
"""Report Map100xx tiles that are unblocked and have a layer-2 graphic while the
tile directly below (y+1) is blocked. See src/MapEditor.Core/MapCodec.cs for the
wire layout: 12-byte header, then 34 bytes per tile (Int32 flags, then five
layers of Int32 graphic + Int16 sheet), row-major. Blocked = flags & 2."""

import argparse
import glob
import os
import struct
import sys

HEADER_SIZE = 12
BYTES_PER_TILE = 34
LAYER2_GRAPHIC_OFFSET = 4 + 2 * 6
BLOCKED_FLAG = 2


def find_hits(path):
    with open(path, "rb") as f:
        data = f.read()
    if len(data) < HEADER_SIZE:
        raise ValueError(f"{path}: truncated header")
    _, editor_version, width, height = struct.unpack_from("<hhii", data, 0)
    if editor_version not in (3, 10):
        raise ValueError(f"{path}: unsupported editor version {editor_version}")
    needed = HEADER_SIZE + BYTES_PER_TILE * width * height
    if len(data) < needed:
        raise ValueError(f"{path}: expected {needed} bytes, got {len(data)}")

    hits = []
    for y in range(height - 1):
        row = HEADER_SIZE + y * width * BYTES_PER_TILE
        next_row = row + width * BYTES_PER_TILE
        for x in range(width):
            here = row + x * BYTES_PER_TILE
            flags = struct.unpack_from("<i", data, here)[0]
            if flags & BLOCKED_FLAG:
                continue
            if struct.unpack_from("<i", data, here + LAYER2_GRAPHIC_OFFSET)[0] == 0:
                continue
            below_flags = struct.unpack_from("<i", data, next_row + x * BYTES_PER_TILE)[0]
            if below_flags & BLOCKED_FLAG:
                hits.append((x, y))
    return width, height, hits


def find_runs(hits, min_len):
    # Hits are never vertically adjacent (a hit's below-tile is blocked, so it
    # cannot itself be a hit), so runs are horizontal only.
    by_row = {}
    for x, y in hits:
        by_row.setdefault(y, []).append(x)
    runs = []
    for y in sorted(by_row):
        xs = sorted(by_row[y])
        start = xs[0]
        prev = xs[0]
        for x in xs[1:] + [None]:
            if x is not None and x == prev + 1:
                prev = x
                continue
            if prev - start + 1 >= min_len:
                runs.append((y, start, prev))
            if x is not None:
                start = prev = x
    return runs


def main():
    repo_root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--maps-dir", default=os.path.join(repo_root, "Assets", "Maps"))
    ap.add_argument("--pattern", default="Map100[0-9][0-9].map")
    ap.add_argument("--min-run", type=int, default=1, metavar="N",
                    help="only report hits in horizontal runs of at least N tiles")
    ap.add_argument("-v", "--verbose", action="store_true", help="list every matching tile")
    args = ap.parse_args()

    paths = sorted(glob.glob(os.path.join(args.maps_dir, args.pattern)))
    if not paths:
        print(f"no files match {os.path.join(args.maps_dir, args.pattern)}", file=sys.stderr)
        return 2

    maps_with_hits = 0
    total_hits = 0
    for path in paths:
        try:
            width, height, hits = find_hits(path)
        except ValueError as e:
            print(f"ERROR: {e}", file=sys.stderr)
            continue
        if args.min_run > 1:
            runs = find_runs(hits, args.min_run)
            hits = [(x, y) for y, x0, x1 in runs for x in range(x0, x1 + 1)]
        else:
            runs = None
        total_hits += len(hits)
        if hits:
            maps_with_hits += 1
        if runs is not None:
            print(f"{os.path.basename(path)}  {width}x{height}  runs>={args.min_run}: {len(runs)}  tiles: {len(hits)}")
            for y, x0, x1 in runs:
                print(f"    y={y}  x={x0}..{x1}  (len {x1 - x0 + 1})")
        else:
            print(f"{os.path.basename(path)}  {width}x{height}  hits: {len(hits)}")
            if hits and args.verbose:
                for x, y in hits:
                    print(f"    ({x}, {y})")

    print(f"\n{maps_with_hits}/{len(paths)} maps have at least one such tile; {total_hits} tiles total")
    return 0


if __name__ == "__main__":
    sys.exit(main())
