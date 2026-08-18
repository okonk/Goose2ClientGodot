#!/usr/bin/env python3
"""Regenerates tests/Goose2Client.Tests/Fixtures/Map10x10.bytes.

Carves the 10x10 tile region at (row 100, col 100) out of the real Assets/Maps/Map1.bytes
and rewrites the header dimensions. Real game data, small enough to commit. Run from the
repo root with generated assets present; the output is deterministic.
"""
import struct

SRC = "Assets/Maps/Map1.bytes"
DST = "tests/Goose2Client.Tests/Fixtures/Map10x10.bytes"
TILE, N, R0, C0 = 34, 10, 100, 100

src = open(SRC, "rb").read()
ver, ed, w, _ = struct.unpack_from("<hhii", src, 0)
out = bytearray(struct.pack("<hhii", ver, ed, N, N))
for r in range(N):
    for c in range(N):
        off = 12 + (((R0 + r) * w) + (C0 + c)) * TILE
        out += src[off:off + TILE]
open(DST, "wb").write(out)
print(f"wrote {DST} ({len(out)} bytes)")
