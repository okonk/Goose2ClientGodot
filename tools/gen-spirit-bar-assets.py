#!/usr/bin/env python3
"""Generate Assets/UI/vitals-sp-bar.png (92x14) and Assets/UI/vitals-sp-outline.png
(95x16, the SP panel incl. its top line). Pure stdlib (no PIL in this environment).
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

# ---------- SP panel outline: 95x16, placed at window (47,45) ----------
OUT_W, OUT_H = 95, 16
px = bytearray(OUT_W * OUT_H * 4)
def setp(x, y, c):
    i = (y * OUT_W + x) * 4
    px[i:i+4] = bytes(c)
for x in range(OUT_W):        # ty0 (y45): full black line — extends MP bottom line x47..140 -> x47..141
    setp(x, 0, BLACK)
for y in range(1, 15):        # ty1..ty14 (y46..y59): border tx1/tx94, inset bevel fill
    setp(0, y, T)
    setp(1, y, BLACK)
    if y == 1:                # top row: full DARK (matches HP/MP panels)
        for x in range(2, 94):
            setp(x, y, DARK)
    elif y == 14:             # bottom row: full LIGHT (matches HP/MP panels)
        for x in range(2, 94):
            setp(x, y, LIGHT)
    else:                     # reversed bevel vs the bar: shadow left, highlight right
        setp(2, y, DARK)      # 1px in from the left border
        for x in range(3, 93):
            setp(x, y, MID)
        setp(93, y, LIGHT)    # 1px left of the right border
    setp(94, y, BLACK)
setp(0, 15, T)                # ty15 (y60): black tx1..94 (x48..141)
for x in range(1, OUT_W):
    setp(x, 15, BLACK)
encode('Assets/UI/vitals-sp-outline.png', OUT_W, OUT_H, px)

# ---------- SP bar: 92x14, window x49..140 / y46..59 ----------
# Raised bevel: highlight top row + left column, shadow bottom row + right column,
# body in between (rows win at the corners).
# Same relative ratios as the original olive scheme, recomputed from base #6D31AE.
BAR_W, BAR_H = 92, 14
SP_BASE = (109, 49, 174)  # #6D31AE
_OLD_BODY, _OLD_HI, _OLD_DK = (125, 125, 35), (190, 190, 90), (82, 82, 23)
def _rescale(base, old_body, old_tone):
    return tuple(min(255, round(b * t / ob)) for b, ob, t in zip(base, old_body, old_tone))
SP_HI = _rescale(SP_BASE, _OLD_BODY, _OLD_HI)   # -> (166, 74, 255)
SP_DK = _rescale(SP_BASE, _OLD_BODY, _OLD_DK)   # -> (72, 32, 114)
bar = bytearray(BAR_W * BAR_H * 4)
for y in range(BAR_H):
    for x in range(BAR_W):
        if y == 0:
            c = SP_HI
        elif y == BAR_H - 1:
            c = SP_DK
        elif x == 0:
            c = SP_HI
        else:
            c = SP_DK if x == BAR_W - 1 else SP_BASE
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
    base = y * w
    assert npx[base*4 + 3] == 0
    assert tuple(npx[(base+1)*4:(base+1)*4+4]) == BLACK
    assert tuple(npx[(base+94)*4:(base+94)*4+4]) == BLACK
    if y in (1, 14):
        edge = DARK if y == 1 else LIGHT
        assert all(tuple(npx[(base+x)*4:(base+x)*4+4]) == edge for x in range(2, 94)), y
    else:
        assert tuple(npx[(base+2)*4:(base+2)*4+4]) == DARK, y
        assert all(tuple(npx[(base+x)*4:(base+x)*4+4]) == MID for x in range(3, 93)), y
        assert tuple(npx[(base+93)*4:(base+93)*4+4]) == LIGHT, y
assert npx[15*w*4 + 3] == 0
for x in range(1, w):
    assert tuple(npx[(15*w+x)*4:(15*w+x)*4+4]) == BLACK
w, h, npx = load('Assets/UI/vitals-sp-bar.png')
assert (w, h) == (BAR_W, BAR_H), (w, h)
# independent literals mirroring the generation values (full-coverage check)
assert all(tuple(npx[x*4:x*4+4]) == (166,74,255,255) for x in range(w)), 'row0'
for y in range(1, BAR_H - 1):
    base = y * w
    assert tuple(npx[base*4:base*4+4]) == (166,74,255,255), (y, 'left')
    for x in range(1, w - 1):
        assert tuple(npx[(base+x)*4:(base+x)*4+4]) == (109,49,174,255), (y, x)
    assert tuple(npx[(base+w-1)*4:(base+w-1)*4+4]) == (72,32,114,255), (y, 'right')
last = (BAR_H - 1) * w
assert all(tuple(npx[(last+x)*4:(last+x)*4+4]) == (72,32,114,255) for x in range(w)), 'lastrow'
print("ALL CHECKS PASSED")
