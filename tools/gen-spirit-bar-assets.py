#!/usr/bin/env python3
"""Generate Assets/UI/vitals-sp-bar.png (92x14) and Assets/UI/vitals-sp-outline.png
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

# ---------- SP bar: 92x14, window x49..140 / y46..59 ----------
# 3-tone vertical striping (highlight row 0 / body rows 1..12 / shadow row 13),
# same relative ratios as the original olive scheme, recomputed from base #C2BB0D.
BAR_W, BAR_H = 92, 14
SP_BASE = (194, 187, 13)  # #C2BB0D
_OLD_BODY, _OLD_HI, _OLD_DK = (125, 125, 35), (190, 190, 90), (82, 82, 23)
def _rescale(base, old_body, old_tone):
    return tuple(min(255, round(b * t / ob)) for b, ob, t in zip(base, old_body, old_tone))
SP_HI = _rescale(SP_BASE, _OLD_BODY, _OLD_HI)   # -> (255, 255, 33)
SP_DK = _rescale(SP_BASE, _OLD_BODY, _OLD_DK)   # -> (127, 123, 9)
bar = bytearray(BAR_W * BAR_H * 4)
for y in range(BAR_H):
    c = SP_HI if y == 0 else (SP_DK if y == BAR_H - 1 else SP_BASE)
    for x in range(BAR_W):
        i = (y * BAR_W + x) * 4
        bar[i:i+4] = bytes(c + (255,))
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
# independent literals mirroring the generation values (full-coverage check)
assert all(tuple(npx[x*4:x*4+4]) == (255,255,33,255) for x in range(w)), 'row0'
for y in range(1, BAR_H - 1):
    assert all(tuple(npx[(y*w+x)*4:(y*w+x)*4+4]) == (194,187,13,255) for x in range(w)), y
last = (BAR_H - 1) * w
assert all(tuple(npx[(last+x)*4:(last+x)*4+4]) == (127,123,9,255) for x in range(w)), 'lastrow'
print("ALL CHECKS PASSED")
