#!/usr/bin/env python3
"""Generate Assets/UI/vitals-sp-bar.png (93x14) and Assets/UI/vitals-sp-outline.png
(96x16, the SP panel incl. its top line). Pure stdlib (no PIL in this environment).
Purely additive: never touches Assets/UI/vitals-outline.png.
Re-run note: overwrites the two generated PNGs; run the godot import step afterwards
to refresh .import files."""
import struct, zlib

def encode(path, w, h, px):
    def chunk(typ, data):
        c = struct.pack('>I', len(data)) + typ + data
        return c + struct.pack('>I', zlib.crc32(typ + data) & 0xffffffff)
    raw = b''
    for y in range(h):
        raw += b'\x00' + bytes(px[y * w * 4:(y + 1) * w * 4])
    out = b'\x89PNG\r\n\x1a\n'
    out += chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 6, 0, 0, 0))
    out += chunk(b'IDAT', zlib.compress(raw, 9))
    out += chunk(b'IEND', b'')
    open(path, 'wb').write(out)

BLACK = (0,0,0,255); MID = (96,96,96,255); DARK = (59,59,59,255); LIGHT = (166,166,166,255)
T = (0,0,0,0)

# ---------- SP panel outline: 96x16, placed at window (47,45) ----------
OUT_W, OUT_H = 96, 16
px = bytearray(OUT_W * OUT_H * 4)
def setp(x, y, c):
    i = (y * OUT_W + x) * 4
    px[i:i+4] = bytes(c)
for x in range(OUT_W):        # ty0 (y45): full black line — extends MP bottom line x47..140 -> x47..142
    setp(x, 0, BLACK)
for y in range(1, 15):        # ty1..ty14 (y46..y59): border tx1/tx95, fill tx2..94
    fill = DARK if y == 1 else (LIGHT if y == 14 else MID)
    setp(0, y, T)
    setp(1, y, BLACK)
    for x in range(2, 95):
        setp(x, y, fill)
    setp(95, y, BLACK)
setp(0, 15, T)                # ty15 (y60): black tx1..95 (x48..142)
for x in range(1, OUT_W):
    setp(x, 15, BLACK)
encode('Assets/UI/vitals-sp-outline.png', OUT_W, OUT_H, px)

# ---------- SP bar: 93x14 plain rectangle, window x49..141 / y46..59 ----------
BAR_W, BAR_H = 93, 14
bar = bytearray(BAR_W * BAR_H * 4)
for y in range(BAR_H):
    c = (190,190,90,255) if y == 0 else ((82,82,23,255) if y == BAR_H-1 else (125,125,35,255))
    for x in range(BAR_W):
        i = (y * BAR_W + x) * 4
        bar[i:i+4] = bytes(c)
encode('Assets/UI/vitals-sp-bar.png', BAR_W, BAR_H, bar)

# ---------- verify (re-decode; index with the DECODED width, not the generation consts) ----------
def load(path):
    d = open(path, 'rb').read()
    assert d[:8] == b'\x89PNG\r\n\x1a\n'
    i = 8; idat = b''
    while i < len(d):
        ln = struct.unpack('>I', d[i:i+4])[0]
        typ = d[i+4:i+8]; chunk = d[i+8:i+8+ln]
        if typ == b'IHDR': w, h, bd, ct = struct.unpack('>IIBB', chunk[:10])
        elif typ == b'IDAT': idat += chunk
        i += 12 + ln
    assert bd == 8 and ct == 6
    raw = zlib.decompress(idat)
    stride = w * 4
    out = bytearray(); prev = bytearray(stride); pos = 0
    for y in range(h):
        f = raw[pos]; pos += 1
        line = bytearray(raw[pos:pos+stride]); pos += stride
        if f == 1:
            for x in range(4, stride): line[x] = (line[x] + line[x-4]) & 255
        elif f == 2:
            for x in range(stride): line[x] = (line[x] + prev[x]) & 255
        elif f == 3:
            for x in range(stride):
                a = line[x-4] if x >= 4 else 0
                line[x] = (line[x] + ((a + prev[x]) >> 1)) & 255
        elif f == 4:
            for x in range(stride):
                a = line[x-4] if x >= 4 else 0; b = prev[x]
                c2 = prev[x-4] if x >= 4 else 0
                p = a + b - c2
                pa, pb, pc = abs(p-a), abs(p-b), abs(p-c2)
                pr = a if (pa <= pb and pa <= pc) else (b if pb <= pc else c2)
                line[x] = (line[x] + pr) & 255
        out += line; prev = line
    return w, h, out

w, h, npx = load('Assets/UI/vitals-sp-outline.png')
assert (w, h) == (OUT_W, OUT_H), (w, h)
for x in range(w):
    assert tuple(npx[x*4:x*4+4]) == BLACK, x
for y in range(1, 15):
    fill = DARK if y == 1 else (LIGHT if y == 14 else MID)
    assert npx[y*w*4 + 3] == 0
    assert tuple(npx[(y*w+1)*4:(y*w+1)*4+4]) == BLACK
    for x in (2, 50, 94):
        assert tuple(npx[(y*w+x)*4:(y*w+x)*4+4]) == fill, (y, x)
    assert tuple(npx[(y*w+95)*4:(y*w+95)*4+4]) == BLACK
assert npx[15*w*4 + 3] == 0
for x in range(1, w):
    assert tuple(npx[(15*w+x)*4:(15*w+x)*4+4]) == BLACK
w, h, npx = load('Assets/UI/vitals-sp-bar.png')
assert (w, h) == (BAR_W, BAR_H), (w, h)
assert tuple(npx[0:4]) == (190,190,90,255)
assert tuple(npx[(7*w+40)*4:(7*w+40)*4+4]) == (125,125,35,255)
i = (13*w+92)*4
assert tuple(npx[i:i+4]) == (82,82,23,255)
print("ALL CHECKS PASSED")
