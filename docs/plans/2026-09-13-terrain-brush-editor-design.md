# Terrain Brush and Terrain Editor Design

Date: 2026-09-13
Branch: `terrain-brush-editor`
Scope: `src/MapEditor.Core`, `src/MapEditor.Rendering`, `src/MapEditor.App`, and their tests.

## Goal

Add a Godot-style terrain authoring workflow and terrain brush to the portable map editor. Authors define logical terrains by painting center, side, and corner regions over 32×32 graphics on complete sprite sheets. Map authors then select a named terrain and paint it while the editor automatically chooses and repairs edge, corner, transition, and interior graphics.

The existing binary map format remains unchanged. Terrain painting writes ordinary sheet/graphic references, so the game client requires no terrain logic.

## Scope

Included:

- full corners-and-sides peering with center plus eight neighbors;
- explicit transitions between any named terrains;
- a flat global terrain catalog without terrain sets;
- manually authored, versioned terrain JSON beside the sprite manifest;
- a Tiles/Terrains tabbed asset panel;
- a terrain selector that activates the Terrain tool;
- freehand map painting and erasing with automatic neighbor repair;
- a modeless full-sheet Terrain Editor;
- Terrain Editor undo/redo, validation, atomic save, and live publication;
- deterministic closest-match resolution and coordinate-stable variants.

Excluded:

- terrain sets or other grouping;
- terrain flood fill, paths/connect, and line tools;
- per-map terrain sidecars or binary format changes;
- automatic terrain inference or AssetConverter generation;
- oversized terrain graphics;
- user-configurable overlay opacity;
- runtime-client terrain behavior.

## Architecture

The feature follows the existing project boundaries:

- `MapEditor.Core` owns terrain catalog models, validation independent of image files, logical-center lookup, candidate scoring, deterministic resolution, terrain stroke state, and undoable map changes.
- `MapEditor.Rendering` loads the catalog beside `manifest.json` and cross-validates graphic references and dimensions.
- `MapEditor.App` owns the Terrains palette, Terrain tool, map gestures, modeless Terrain Editor, editor-local draft history, catalog persistence, and publication across documents.

The map renderer remains terrain-agnostic. Resolved terrain cells are ordinary `MapTileLayer` values and use the existing rendering path.

A previous terrain-brush branch used inferred binary-membership terrain sets and a per-tile management UI. Its behavior does not match this design. Reusable low-level ideas may be adapted, but automatic generation, terrain sets, and binary masks will not be restored.

## Catalog model

A versioned `terrain-brushes.json` lives beside the selected asset directory's sprite manifest. It contains a deterministic ordered list of terrains and authored graphic patterns.

Each terrain has:

- a stable ID;
- a unique display name;
- an optional user-selected color;
- a derived color when no override is present.

The derived color uses a stable hash of the terrain ID, so renaming does not change it.

Each authored graphic identifies a sheet and graphic and contains nine peering values:

- center;
- north, east, south, and west sides;
- northeast, southeast, southwest, and northwest corners.

Each value is a terrain ID or `None`. A persisted graphic must have a named center. Its center determines the graphic's logical terrain and the candidate pool in which it participates. Side and corner values may reference any terrain in the global catalog, enabling explicit terrain-to-terrain transitions.

A graphic has at most one authored pattern globally. Multiple graphics may share one complete pattern and act as visual variants.

Map files contain no logical terrain IDs. A map cell's logical terrain is inferred from the center assignment of its current graphic. Empty and unrecognized graphics have no logical terrain. Consequently, a catalog-authored graphic is recognized even when it was originally placed with the ordinary Pencil tool and may be repaired by a later nearby terrain stroke.

## Validation

The persisted catalog is valid only when:

- terrain IDs and names are non-empty and unique;
- every terrain has at least one graphic centered on it;
- every authored graphic has a non-`None` center;
- every peering reference targets an existing terrain;
- every graphic exists in `manifest.json` and resolves to exactly 32×32;
- no graphic has more than one authored pattern;
- color overrides are valid.

Duplicate complete patterns are valid variants. Missing exact transition patterns produce coverage warnings rather than validation failures because the brush has deterministic closest-match behavior.

The Terrain Editor may temporarily contain centerless graphics or other invalid intermediate state, but Save is blocked until the entire catalog validates. Drafts are not persisted.

Deleting a terrain removes graphics centered on it and clears references to it from other patterns. The whole deletion is one Terrain Editor undo operation.

## Terrain resolution

A terrain stroke first constructs the final intended logical center for every visited map cell. It then resolves graphics from that final state rather than from partially updated graphics. This makes a drag's result independent of pointer sampling order.

For each cell being resolved:

1. Read the requested center terrain.
2. Read logical center terrains at all eight neighboring map cells.
3. Treat outside-map, empty, and unrecognized neighbors as `None`.
4. Consider only graphics whose authored center equals the requested center.
5. Score each candidate's sides and corners against the desired neighboring centers.

Scoring uses the region's weight: sides have weight 2 and corners have weight 1.

- An exact named-terrain or exact `None` match adds the weight.
- Candidate `None` against a named neighbor is a neutral generic fallback.
- A wrong named terrain, or a named terrain against desired `None`, subtracts the weight.

Exact patterns therefore outrank fallbacks. Sides dominate corners when an incomplete catalog forces a compromise.

Tied complete patterns are selected with a stable, platform-independent hash of the center terrain ID, map coordinates, and desired pattern. When several graphics share the selected pattern, another stable coordinate hash selects the variant. Results do not depend on JSON order, process-randomized hashes, map save/load cycles, or stroke order.

Corner values compare with the diagonally adjacent cell's logical center. Side values compare with cardinally adjacent cells.

## Map editing workflow

The Terrain tool targets the topmost selected map layer, matching existing Pencil, Eraser, Eyedropper, and Flood Fill behavior.

- Left-drag assigns the selected terrain to each visited cell.
- Shift+left-drag erases visited cells belonging to any recognized terrain.
- Painting directly overwrites empty, recognized, or unrecognized graphics.
- Erasing empty or unrecognized cells is a no-op.
- Unrecognized neighboring graphics are never modified.
- Existing line interpolation fills cells skipped by fast pointer movement.

The resolver recalculates all visited cells and their eight-neighbor halo. Recognized neighbors from any terrain may be re-tiled so both sides of an explicit transition update. Erased cells become empty because persisted terrain graphics require a named center.

The canvas previews the accumulated resolved state during the drag. Pointer release commits the complete original-to-final delta as one normal map undo command. Escape, pointer-capture loss, asset-directory replacement, or terrain-catalog replacement cancels the gesture and restores every affected graphic.

A valid terrain always has at least one center candidate. Any unexpected resolution failure cancels the full stroke and reports an error; no partial edits or history entries remain.

## Main editor UI

The left asset panel becomes a tabbed panel:

- `Tiles` preserves the existing sheet selector, numeric brush fields, and graphic palette.
- `Terrains` shows one terrain selector plus Add and Edit actions.

Each selector entry shows the terrain's color, name, and representative 32×32 graphic. The representative is the variant with the most same-terrain peers, preferring an all-same interior pattern and then stable graphic ordering.

Selecting a terrain stores its stable ID per map document and automatically activates a Terrain toolbar tool with shortcut `T`. If a catalog publication removes a document's selection, the selection clears and its active tool falls back to Pencil. Terrain controls are unavailable when no valid terrain catalog is published, while Tiles remains usable.

Add opens the Terrain Editor and creates a terrain in its local draft. Edit opens or focuses the same editor and selects the current terrain.

## Terrain Editor

The Terrain Editor is a single modeless window per application. The map continues to use the last published catalog while the window contains unsaved changes.

The window contains:

- a terrain list and selected paint terrain;
- Add, Rename, Delete, color override, and reset-to-derived-color actions;
- a sprite-sheet selector;
- zoom and scrolling controls;
- a nearest-neighbor full-sheet view;
- validation and coverage diagnostics;
- Save and Revert actions.

The full source sheet PNG is shown rather than a separate selected-tile preview. Every manifest graphic that resolves to exactly 32×32 receives Godot-style hit regions: a center region and eight surrounding side/corner regions. Authored regions are drawn with their terrain colors at fixed 80% opacity so the artwork remains visible.

Left-drag paints the selected terrain into each crossed region. Shift+left-drag clears regions to `None`. Centerless patterns are allowed as temporary edits and remain visible with validation diagnostics. Overlapping manifest rectangles resolve to the lowest graphic ID, matching the existing graphic viewer convention.

The Terrain Editor has a private undo/redo history covering region strokes, additions, deletions, renames, and color changes. Ctrl+Z, Ctrl+Y, and Ctrl+Shift+Z target this history while the window has focus. Closing with unsaved changes prompts Save, Discard, or Cancel.

The exact Godot-style polygons are defined once in normalized 32×32 local coordinates and used by both overlay drawing and hit testing so zoom does not change interaction boundaries.

## Persistence and publication

Save performs complete model and manifest validation, serializes deterministic JSON, writes a temporary file beside `terrain-brushes.json`, and atomically replaces the destination. Failed writes preserve the old file and published catalog while retaining the editor's draft.

The file store records the revision loaded by the Terrain Editor. If the destination changes externally before Save, the user must reload or explicitly confirm overwrite.

Successful publication:

1. Cancels active terrain map gestures.
2. Replaces the shared terrain catalog.
3. Preserves per-document selections whose IDs still exist.
4. Clears removed selections and switches those documents back to Pencil.
5. Refreshes all terrain selectors and canvases.
6. Leaves map contents, history, and dirty state unchanged.

Changing the selected asset directory while the Terrain Editor is dirty first prompts Save, Discard, or Cancel. The switch proceeds only after the draft is resolved.

Missing terrain JSON represents an empty catalog, allowing Add to create the first terrain. Malformed, unsupported, or invalid terrain JSON disables only terrain behavior and reports the path and exact issue. The Terrain Editor offers an explicit confirmed `Replace with Empty Catalog` recovery action; it never silently overwrites invalid data.

Removing a graphic from the catalog does not change existing maps. That graphic becomes unrecognized and remains renderable and saveable until directly overwritten.

## Error handling

Terrain catalog errors do not prevent loading the sprite manifest, opening maps, painting ordinary graphics, or saving maps. Missing or unreadable selected sheet PNGs appear as inline Terrain Editor errors while other sheet navigation remains available.

Errors identify the operation and affected path. Validation errors identify terrain names, graphic references, and peering regions where applicable. Coverage warnings remain non-blocking.

## Testing

### Core

Automated tests cover:

- peering orientation and desired-neighbor construction for all eight regions;
- exact, neutral fallback, mismatch, side-weight, and corner-weight scoring;
- stable pattern ties and coordinate variants;
- logical-center inference from empty, recognized, and unrecognized graphics;
- single- and multi-cell paint and erase;
- final-state resolution and eight-neighbor repair;
- explicit transitions on both sides of a boundary;
- map edges and diagonal corners;
- directly overwritten manual graphics and untouched manual neighbors;
- interpolated drag paths;
- no-op behavior, cancellation, exact undo/redo, and one command per drag;
- all-or-nothing failure recovery.

### Rendering

Tests cover:

- deterministic JSON round trips;
- schema and version failures;
- duplicate IDs, names, and graphic assignments;
- missing terrain references;
- manifest resolution and 32×32 enforcement;
- missing or malformed terrain files degrading independently from base assets;
- catalog replacement without disturbing the normal sprite cache.

### App

Avalonia headless tests cover:

- Tiles/Terrains tab behavior;
- automatic Terrain-tool activation and shortcut;
- per-document terrain selection and removal fallback;
- full-sheet region drawing and hit testing under pan and zoom;
- overlapping-frame selection;
- left-drag and Shift+left-drag authoring;
- fixed 80% overlay output;
- Terrain Editor undo/redo and singleton lifecycle;
- Save validation, Revert, close prompts, and malformed-file recovery;
- external-change and asset-directory prompts;
- atomic save failure;
- publication across open documents;
- active map-stroke cancellation on publication.

Tests validate models, geometry, selected references, and draw operations rather than platform-specific pixels.

### Manual smoke validation

A smoke pass will:

1. Create Grass and Water terrains.
2. Author interior, edge, corner, and cross-terrain transition graphics on complete sheets.
3. Save and reopen the terrain catalog.
4. Paint adjacent Grass and Water map regions.
5. Verify side and diagonal repair during a live drag.
6. Erase with Shift+drag.
7. Undo and redo a complete gesture.
8. Save and reopen the map to confirm unchanged map-format compatibility.

## Deferred work

- Terrain sets and selector grouping.
- Terrain flood fill, line, connect, and path tools.
- Per-map logical terrain metadata.
- Exact-match-only modes and configurable scoring.
- Automatic terrain detection or generation.
- Terrain Editor overlay opacity controls and authored-only filtering.
- Oversized or multi-cell terrain graphics.
- User-authored variant weights or per-stroke randomization.
- Pixel screenshot tests.
- Long-stroke resolution performance: Part 2's stateless `TryResolvePatch` re-validates and re-sorts the full cumulative center-override set on every segment (O(K log K), K = cumulative visited cells; O(N² log N) per stroke, plus per-segment clone allocations). Realistic strokes stay sub-millisecond per event, but a single gesture visiting tens of thousands of cells on a large map degrades progressively (CPU + GC pressure). A fix needs a stateful/incremental resolution fast path (validated cumulative state + only newly staged entries) or stroke-side persistent overrides with rollback — both require relaxing Part 2's locked resolver contract. Surfaced by the Part 2 final review.
