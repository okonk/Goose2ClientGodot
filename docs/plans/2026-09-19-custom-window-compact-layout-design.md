# Compact Custom Window Layout Design

## Goal

Improve the custom-item window's hierarchy and spacing without increasing its original 440×260 logical footprint. Keep the familiar picker-left, controls-center, preview-right arrangement and make the character more prominent at 3×.

## Window Composition

Keep the root window at 440×260. Preserve the existing background texture at this size so its built-in horizontal separator continues to divide the editing area from the Create footer near the bottom.

Use three compact columns above the separator:

1. A 112-pixel-wide visual color picker on the left.
2. Precisely aligned RGBA controls and source slots in the center.
3. A 144-pixel-wide character preview on the right.

Use a consistent 12-pixel outer margin and small, regular internal gaps. Do not add grouping panels or large section headings.

## Color Picker

Reduce the saturation/lightness picker from 128×128 to 112×112. Keep the hue and lightness bars at the same 112-pixel width and align all three controls to identical left and right edges. Use compact, consistent vertical gaps between them.

Preserve the current cursor behavior, generated textures, smooth gradient filtering, and color synchronization.

## RGBA Controls and Source Slots

Place the four RGBA rows in the center column. Each row uses a fixed-width channel letter, an equal-width slider, and a fixed-width right-aligned value. Tighten the rows to approximately 22-pixel vertical spacing with about four pixels between tracks.

Place the two 32×32 source slots beneath the slider rows with an eight-pixel gap. Center them as a pair within the control column. Put Graphic and Stats directly beneath their corresponding slots without a separate Source Items heading.

Preserve slider ranges, initial values, slot drag-and-drop behavior, and slot compatibility rules.

## Character Preview

Reserve exactly 144 logical pixels of width, approximately x=286–430, so a standard 48-pixel frame fits horizontally at 3×. Vertically center the preview region on the right side using the existing ground-line calculation.

Change valid previews from a fixed 2× scale to a fixed 3× scale. Keep nearest-neighbor filtering and clipping enabled. Standard frames must fit horizontally. Tall equipment may be clipped vertically; this is an accepted tradeoff for the compact window.

## Name and Create

Place a small inline Name label and the text field on one row above the background separator. The field consumes the remaining width while retaining its existing maximum length and filtering behavior.

Keep Create centered below the separator. Preserve its existing server-controlled visibility, disabled conditions, submission-pending behavior, theme styling, and text.

## Implementation Boundaries

Modify only the fixed geometry in `CustomWindow.tscn`, add the Name label, and update the fixed preview scale and its unit-test expectations. Keep all existing interactive controls as direct `Content/*` children so `CustomWindow.cs` node paths remain unchanged.

Do not modify the background asset, shared theme, network protocol, validation, drag-and-drop logic, color logic, name filtering, or submission logic. Do not introduce responsive layout or adaptive preview scaling.

## Verification

Verify:

- The window remains exactly 440×260.
- The background separator falls between the Name row and Create footer.
- The 112×112 picker and both bars remain aligned and synchronized.
- RGBA tracks, labels, and values do not overlap.
- Both slots remain draggable and their captions remain readable.
- Standard 48-pixel-wide frames fit horizontally at 3×.
- Tall-frame vertical clipping is limited to the preview viewport and does not overlap other controls.
- Name filtering and all Create states remain unchanged.
- The complete layout remains legible at common global UI scales.

The redesign succeeds when it looks like a refined version of the original window rather than a larger editor dialog.
