#!/usr/bin/env python3
"""Cut the four toolbar glyphs out of the painted source sheet and export them for
Assets/UI. The sheet ships each glyph inside a painted frame; the frame is dropped so
the themed ToolbarButton style can draw hover/pressed states instead.

Exports at 2x the 40px button so a 2x UI scale is pixel-exact.
Run `godot --headless --path . --import --quit` afterwards to refresh the .import files.

Usage: python3 tools/gen-toolbar-icons.py [source.png]
"""
import sys
from PIL import Image, ImageFilter
import numpy as np
from scipy import ndimage

SOURCE = sys.argv[1] if len(sys.argv) > 1 else 'Assets/UI/source/toolbar-icons.png'
TILES = [((48, 490), 'destroy'), ((533, 981), 'combinebagbutton'),
         ((1021, 1467), 'optionsbutton'), ((1509, 1955), 'exitbutton')]
TOP, BOTTOM = 114, 551
EXPORT = 80
INSET = 34          # painted frame thickness
CORNER = 48         # the frame's corner bevel accents
OUTLINE = (12, 14, 26, 255)
SPECK = 0.004       # components below this fraction of the glyph are source compression noise


def key(tile):
    """Alpha-key the flat backdrop and drop the painted frame."""
    a = np.asarray(tile).astype(float)
    bg = np.median(a.reshape(-1, 3), axis=0)
    alpha = np.clip((np.sqrt(((a - bg) ** 2).sum(2)) - 18) / 22, 0, 1)
    h, w = alpha.shape

    alpha[:INSET, :] = alpha[h - INSET:, :] = 0
    alpha[:, :INSET] = alpha[:, w - INSET:] = 0

    # Corner squares only: a full CORNER inset would clip the glyphs that run to the
    # top edge (the bag's neck, the door's beam).
    rows = np.zeros((h, w), bool); rows[:CORNER, :] = rows[h - CORNER:, :] = True
    cols = np.zeros((h, w), bool); cols[:, :CORNER] = cols[:, w - CORNER:] = True
    alpha[rows & cols] = 0

    labels, count = ndimage.label(alpha > 0.1)
    if count:
        sizes = ndimage.sum(alpha > 0.1, labels, range(1, count + 1))
        floor = max(150, SPECK * sizes.max())
        for i, size in enumerate(sizes, start=1):
            if size < floor:
                alpha[labels == i] = 0

    img = Image.fromarray(np.dstack([a, alpha * 255]).astype(np.uint8), 'RGBA')
    img = img.crop(img.split()[3].point(lambda v: 255 if v > 24 else 0).getbbox())
    side = max(img.size) + max(img.size) // 16
    square = Image.new('RGBA', (side, side), (0, 0, 0, 0))
    square.alpha_composite(img, ((side - img.width) // 2, (side - img.height) // 2))
    return square


def outline(img, width=2):
    solid = np.asarray(img.split()[3]).astype(int) > 110
    grown = solid.copy()
    for _ in range(width):
        step = grown.copy()
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)):
            step |= np.roll(np.roll(grown, dy, 0), dx, 1)
        grown = step
    px = np.zeros((*img.size[::-1], 4), dtype=np.uint8)
    px[grown & ~solid] = OUTLINE
    ringed = Image.fromarray(px, 'RGBA')
    ringed.alpha_composite(img)
    return ringed


def main():
    sheet = Image.open(SOURCE).convert('RGB')
    for (x0, x1), name in TILES:
        img = key(sheet.crop((x0, TOP, x1, BOTTOM))).resize((EXPORT, EXPORT), Image.LANCZOS)
        img = img.filter(ImageFilter.UnsharpMask(radius=1.6, percent=120, threshold=2))
        outline(img).save(f'Assets/UI/{name}.png')
        print('wrote', name)


main()
