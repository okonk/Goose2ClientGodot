import os
import struct
import sys

import pytest

# remap_maps.py lives one level up (not under remap/)
sys.path.insert(0, os.path.dirname(os.path.dirname(__file__)))
from remap_maps import convert_map, MAP_BASE  # noqa: E402


def _write_aspereta_map(path, tiles):
    """tiles: list of (blocked:int, layers:tuple[4 ints]) length 100*100, row-major."""
    assert len(tiles) == 100 * 100
    buf = bytearray()
    buf += struct.pack("<hh", 65, 3)
    for blocked, layers in tiles:
        buf.append(blocked & 0xFF)
        buf += struct.pack("<4i", *layers)
    # trailing junk (ignored)
    buf += b"\x00\x01\x02"
    with open(path, "wb") as f:
        f.write(buf)


def test_convert_map_layers_flags_and_remap(tmp_path):
    src = tmp_path / "Map7.map"
    dst = tmp_path / f"Map{MAP_BASE + 7}.map"
    tiles = [(0, (0, 0, 0, 0))] * (100 * 100)
    # tile 0: blocked + all layers
    tiles[0] = (1, (100, 200, 300, 400))
    # tile 1: unknown graphic dropped
    tiles[1] = (0, (999999, 0, 0, 0))
    _write_aspereta_map(src, tiles)

    graphics = {
        100: (11, 1000),
        200: (12, 2000),
        300: (13, 3000),
        400: (14, 4000),
    }
    warns = convert_map(str(src), str(dst), graphics)
    assert any("999999" in w for w in warns)

    data = dst.read_bytes()
    ver, editor, w, h = struct.unpack_from("<hhii", data, 0)
    assert (ver, editor, w, h) == (65, 3, 100, 100)
    off = 12

    # tile 0
    flags = struct.unpack_from("<i", data, off)[0]
    off += 4
    assert flags == 2
    layers = [struct.unpack_from("<ih", data, off + i * 6) for i in range(5)]
    off += 30
    assert layers[0] == (1000, 11)
    assert layers[1] == (2000, 12)
    assert layers[2] == (3000, 13)
    assert layers[3] == (0, 0)       # empty mid layer
    assert layers[4] == (4000, 14)   # asp layer 3 → goose roof

    # tile 1: unknown dropped
    flags = struct.unpack_from("<i", data, off)[0]
    off += 4
    assert flags == 0
    g0, s0 = struct.unpack_from("<ih", data, off)
    assert (g0, s0) == (0, 0)
