# Client Minimap — Design

**Status:** approved (brainstormed 2026-07-09)
**Scope:** Godot client only. The map-editor minimap is a possible future part and is out of scope here.

## Problem

Drawing a minimap by rendering the map's sprites at a reduced scale looks bad: 32px
detail-heavy sprites alias into mud below ~50% zoom. The minimap must be a separate,
low-detail representation, not a scaled-down view of the same art.

## Approach (approved)

A **per-tile average-color bitmap** (1px per tile) for the whole map, computed once at
map load, displayed as a **64×64-tile window centered on the player** at **3px/tile**
(192×192px), anchored **top-right** of the HUD, scaled with the existing `UiScale`.

Locked decisions:

- Base color per tile = **topmost non-empty layer among 4 → 3 → 2 → 0** (layer 1 skipped).
  Trees (layer 2) and roofs (layer 4) are included so landmarks read.
- A frame with **no opaque pixels** counts as an empty layer → fall through to the next
  layer down.
- Window is clamped to map bounds at edges (player arrow goes off-center; no padding).
- Whole-map fit is rejected: at 1000-tile maps a 192px box is <0.2px/tile — the same
  aliasing problem in miniature.

## Components

### 1. `MinimapColors` (pure C#, unit-testable)

No Godot types. Core selection logic:

- Input: a tile's five `(sheet, graphic)` layer references plus a color-provider delegate
  `Func<int sheet, int graphic, Color?>` (Color here is a pure struct, not Godot's).
- `PickColor(...)`: walks layers 4 → 3 → 2 → 0 (skipping 1); returns the first layer whose
  provider returns a non-null color; null if none.
- `BuildRow`/`Build` over a tile-accessor delegate (`Func<int x, int y, TileLayers>` or
  equivalent) so tests fake maps without Godot or real sheets.
- (sheet, graphic) == (0, 0) / empty reference is treated as an empty layer.

### 2. `MinimapBitmapBuilder` (Godot, map-load time)

Implements the provider over `SpriteCache`:

- For each **unique** `(sheet, graphic)` referenced by the map (cached in a dictionary,
  not per-tile): fetch the frame rect from the manifest, read the sheet `Image` pixels
  once, average the **opaque** pixels of the frame rect. No opaque pixels → null (empty).
- Emits one `Image` of `mapWidth × mapHeight` (1px/tile) wrapped in an `ImageTexture`.
- Runs once in `MapManager._Ready` after layers are set up.
- Cost: one tile pass + one pixel read per unique graphic. Cheap at 1000×1000.

### 3. `MinimapControl` (Godot, HUD)

- `Control`, 192×192 at UI scale 1, anchored top-right in `GameHud`, scaled with
  `UiScale` like the other HUD elements.
- `_Draw`: compute the player's tile → clamp a 64×64 source window to map bounds →
  `DrawTextureRect(bitmap, sourceRect, destRect)` with **Nearest** filtering → draw a
  small player triangle at the player's in-window position.
- Redraws only when the player's tile or the bitmap changes (not every frame).

### 4. Live updates

The existing `TileUpdatePacket` handler in `MapManager` recomputes the one affected
pixel through the same provider and updates the texture, so in-world tile changes
appear on the minimap.

## Testing

- xunit tests for `MinimapColors`: layer priority (4 > 3 > 2 > 0), layer 1 skipped,
  empty-layer fall-through, all-empty → null, empty (0,0) references.
- Godot side (builder + control) verified visually in-game; no pixel-screenshot tests.

## Deferred (flagged, not designed)

- Darkening blocked cells.
- Party/NPC dots on the minimap.
- Whole-map inset.
- Click-to-move / click-to-center from the minimap.
- Map-editor (Avalonia) minimap panel.
