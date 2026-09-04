# Map Resize & Rectangle Blocking — Design

Date: 2026-09-05
Branch: `feat/map-resize-blocked-rect`
Scope: `src/MapEditor.Core`, `src/MapEditor.Rendering`, `src/MapEditor.App` and their test projects; one one-line fix in `Scripts/MapManager.cs`.

## Summary

1. Existing maps become resizable, via edge offsets (north/south/east/west), as a single undoable operation.
2. The Blocked tool becomes rectangle-based: drag or click sets blocked, Shift clears. No toggle, no freehand.
3. `MapEditCommand` becomes a small closed hierarchy so resize can join layer and flags deltas as a third command kind.
4. Fix a latent client bug that this work makes reachable: `MapManager.ItemKey` indexes by `Height` instead of `Width`.

Both features were listed as deferred scope in `2026-09-01-portable-map-editor-design.md:143`.

## Considered and rejected

**Multi-tile stamp brush.** Explored first and dropped. Large objects are already a single frame on a single tile — `MapRenderer.cs:86-90` draws them horizontally centred and bottom-anchored on their anchor tile, overflowing into neighbours visually, with `ViewportCulling` padding `SpriteCandidates` to catch the overhang. So a stamp only buys composites (a building of many 32×32 pieces), which existing multi-select copy/paste already covers. A palette-sourced stamp is worse still: the sheet is a packed atlas of variable-size frames, so "adjacent in the sheet" is a packing artifact, not a spatial relationship.

**Rectangle / line / ellipse draw tools.** Cut as unnecessary; the actual pain was blocking many tiles at once, which is addressed directly.

**Full document snapshots for resize undo.** A 1000×1000 map is ~34 MB per snapshot against a 64 MB history cap, so two resizes would evict the entire undo stack.

**A general `IMapEditCommand` apply/revert refactor of the whole history layer.** Larger than the two features warrant; the closed hierarchy below gets the same benefit for this change.

## 1. Core command model

`MapEditCommand` today is a two-arm union discriminated by nullable fields, with two `!` assertions and an `else`-means-flags fallthrough in both `ReplayCommand` and `AccountedSizeBytes`. That is fine for two arms of per-tile deltas and breaks down at three, because resize replays by swapping the document's shape rather than writing cells.

```
MapEditCommand (abstract)          BeforeStateId, AfterStateId
                                   abstract AccountedSizeBytes
                                   abstract Replay(MapDocument, reverse, before)
├── MapDeltaCommand<T> (abstract)  owns the one reverse-iteration loop
│   ├── MapLayerChangesCommand     Apply → document.SetLayer
│   └── MapFlagsChangesCommand     Apply → document.SetFlags
└── MapResizeCommand               Replay → document.ResizeTo(...)
```

`MapDeltaCommand<T>` collapses `ReplayLayerChanges` and `ReplayFlagsChanges`, currently two near-identical 12-line methods differing only in element type and one setter call. Subclasses supply `SlotBytes` and `Apply(MapDocument, in T, bool before)`. `MapEditSession.ReplayCommand` becomes `command.Replay(_document, reverse, before)`.

The static factories (`ForLayerChanges`, `ForFlagsChanges`) are dropped in favour of plain constructors.

Tests do reach these types through `MapEditSession.History.PeekUndo()`: 15 call sites across `MapEditStrokeStorageTests.cs:98,122,151-158`, `MapEditStrokeTests.cs:100,119,194` and `MapEditSessionTests.cs:552` read `.LayerChanges`/`.FlagsChanges`. They become `((MapLayerChangesCommand)cmd).Changes`, a mechanical edit.

One of them is load-bearing: `MapEditStrokeStorageTests.cs:152` asserts `Assert.Same(buffer, command.LayerChanges)` — the stroke's delta buffer must reach the command **without being copied**. The hierarchy must preserve that zero-copy handoff.

## 2. Resize

### Core

`MapDocument` gains `internal void ResizeTo(MapTileRectangle window)`. `Width`/`Height` become `{ get; private set; }`.

**This type is shared with the running Godot client** (`Goose2ClientGodot.csproj:17`); `MapManager` holds a live `MapDocument`. The change is safe because `ResizeTo` is `internal` and `AssemblyInfo.cs` exposes internals only to `MapEditor.Core.Tests`, and because the client re-reads `Width`/`Height` at each call site (`MapManager.cs:133,276,326`) rather than caching them. **`ResizeTo` must not be made public.**

`MapEditSession.ApplyResize(MapTileRectangle window)` mirrors `ApplyFloodFill`'s shape:

- the window is expressed in **current-document coordinates** and may have negative `X`/`Y` or extend past the edges; that region becomes the new document. Growth north/west falls out as a negative origin, cropping as a smaller window. Core never learns the words "north" or "south".
- throws `InvalidOperationException` if a stroke is active
- validates resulting dimensions against `MinDimension`/`MaxDimension` (1..1000) **before** mutating anything
- returns `false` with no history entry for the identity window
- snapshots discarded tiles **only where `!tile.IsEmpty`**, because `ResizeTo` allocates a fresh zeroed array and default tiles restore themselves. Cropping an empty border costs zero bytes.

`MapResizeCommand` payload: the source window, old and new dimensions, and the discarded tiles as a **nullable, lazily allocated** `MapEditChangeBuffer<MapTileSnapshot>?` where `MapTileSnapshot(int X, int Y, MapTile Tile)` — reusing the segmented buffer so byte accounting stays uniform (`ResizeSlotBytes` alongside `LayerSlotBytes`/`FlagsSlotBytes`).

The buffer must be lazy because `MapEditChangeBuffer`'s constructor eagerly allocates a segment: an *empty* buffer already accounts for 320 bytes, so a grow would be charged for storing nothing. It is allocated on the first appended snapshot, and `AccountedSizeBytes` reports only the base command bytes while it is null — so a grow, and a crop of an empty border, are genuinely free.

Forward replay is `ResizeTo(window)`. Reverse is `ResizeTo(inverse)` followed by writing the snapshots back, where a forward window of `(X, Y, W, H)` in old coordinates inverts to `(-X, -Y, oldW, oldH)` in new coordinates. Growth stores zero snapshots, so undoing a grow is a pure crop.

Undo across a resize is correct because history is strictly LIFO: per-tile commands recorded at old dimensions are only replayed after the resize has already restored those dimensions. `EvictToCap` drops oldest-undo-first and last-redo-first, which preserves that ordering.

`MapTile.IsEmpty` joins the existing `IsBlocked`/`IsRoof` and is defined as `flags == 0 && all five layers are (0,0)`. **Including flags is a correctness requirement, not a display detail:** a graphics-only predicate would store no snapshot for a region of blocked-but-artless tiles and undo would silently lose the blocked flags. The dialog's discard count and `ApplyResize`'s snapshot decision share this one predicate so they cannot drift.

### Dialog

`ResizeMapDialog.axaml/.cs`, modeled on `NewMapDialog` and surfaced through the existing `IEditorDialogs` / `AvaloniaEditorDialogs` / `EditorDialogsProxy` seam so it is driveable from tests.

Four signed offset fields (north, south, east, west) plus editable absolute width and height. Absolute entry always resolves against the **top-left-anchored** edges — typing a width adjusts `east`, typing a height adjusts `south` — so absolute entry never shifts existing coordinates. The four offset fields remain the only way to grow north or west.

Live readouts: `100 × 100 → 120 × 110`, and when lossy `⚠ Discards 143 non-empty tiles`. Resize is disabled when the result falls outside 1..1000. The count scans only the discarded bands, not the whole map, so it stays proportional to what is being cut.

Offsets convert to the core window in one line:

```csharp
new MapTileRectangle(-west, -north, width + west + east, height + north + south)
```

Lives at the bottom of the Edit menu, behind a separator. No keyboard shortcut.

### Coordinate shifting

Growing north or west shifts every existing tile's `(x, y)`, which invalidates any server-side warp or spawn coordinate stored against that map. The dialog's readout makes the shift visible; the editor does not attempt to rewrite external data.

The coordinate work does **not** live in `MainWindowViewModel.ResizeMap`. `ResizeMap` only calls `Session.ApplyResize`; the session then raises `Resized` with the transform, and a single handler on the view model does the shifting. That way apply, undo and redo all travel the identical path — `Undo`/`Redo` resize the same live document, so a handler-free `ResizeMap` would leave dimensions and coordinates stale after an undo.

The handler:

- refreshes the cached `_mapWidth`/`_mapHeight` explicitly (the `EditorRefresh.Document` branch only fires when the *session* is replaced, and would additionally wipe the selection it is meant to shift)
- shifts the selected point **as a pair**, clearing both coordinates when either leaves the new bounds — never clamping, and never leaving one of the two null
- **clips** `SelectionRectangle` to the new bounds, keeping its surviving part and clearing it only when nothing survives; every tile left in it was in the user's original selection
- **clears hover** rather than shifting it: hover is re-derived from the next pointer move, and a resize is not a pointer event
- cancels paste mode; the clipboard itself stays valid, being size-independent

Selection and hover are UI state and are never stored in history, so undoing a crop restores the tiles but leaves a cleared selection cleared. Dirty state follows from the pushed command's state id and needs no special handling. The handler is subscribed for the life of the session and re-subscribed when the document is replaced, so no handler leaks onto a discarded session.

`_viewport` is deliberately left untouched. `PanByScreenDelta` has no map-bounds clamping and `ClampToMap` returns `TileRange.Empty` when the view is entirely off-map, so a view left over empty space after a large crop is already a reachable, recoverable state today.

**Accepted limitation:** cropping a fully-painted 1000×1000 map to 100×100 discards ~990k non-empty tiles at ~52 bytes each ≈ 51 MB against the 64 MB cap, evicting most prior history. The accounting handles this gracefully and the resize itself stays undoable.

## 3. Rectangle blocking

The Blocked tool becomes rectangle-only:

| Gesture | Effect |
|---|---|
| Click or drag | Set blocked across the rectangle |
| Shift + click or drag | Clear blocked across the rectangle |

A click is simply a 1×1 drag, so there is no special case anywhere. There is no toggle and no freehand painting.

`MapEditTool.BlockedToggle` is renamed to `MapEditTool.Blocked` — nothing toggles any more. This touches the enum, `ValidateTool`'s bound, the XAML `Tag`, and `ShortcutTests`.

### Core

`MapEditSession.ApplyBlockedPatch(MapTileRectangle rect, bool blocked)` mirrors `ApplyFloodFill`: stroke check, bounds validation throwing `ArgumentOutOfRangeException` as `ApplyLayerPatch` does, one `MapEditChangeBuffer<MapFlagsChange>` appending only tiles whose blocked bit actually changes, `false` and no history entry when none do.

Flags are computed as `blocked ? old | BlockedFlag : old & ~BlockedFlag`, never assignment, so the unknown flag bits the smoke checklist requires preserved stay preserved.

**Nothing begins a stroke with the blocked tool any more.** `MapEditStroke`'s `_flagsChanges` buffer, its `BlockedToggle` arm, and the flags branches in `CompleteStroke` and `CancelStroke` become dead and are removed; strokes become layer-only. `ValidateTool`'s bound tightens from `> BlockedToggle` to `> Eyedropper`, which needs no enum reordering since `Blocked` already sits just past `Eyedropper`. `MapFlagsChangesCommand` stays — `ApplyBlockedPatch` is now its only producer.

### App gesture

```csharp
internal enum RectDragPurpose { Select, Block, Unblock }
internal readonly record struct RectDrag(
    RectDragPurpose Purpose, MapTileCoordinate Origin, MapTileRectangle Current);
```

One `private RectDrag? _rectDrag` in `MapCanvas` replaces `_multiSelecting`, `_rectDragStart` and `_rectDragMoved`, which are currently touched at nine sites across `FinishInteraction`, `IsGestureActive`, `OnPointerPressed`, `OnPointerMoved`, `OnPointerReleased`, `OnPointerCaptureLost`, `OnCanvasInvalidated`, `BeginToolPress` and `FinalizeMultiSelection`.

`Current` is initialized to a 1×1 rect at press and updated on move, so `Moved` disappears. Today `SelectionRectangle` is only assigned inside `OnPointerMoved`, forcing `FinalizeMultiSelection` to synthesize a 1×1 rect for a no-move click via a flag that any sub-pixel jitter can set.

`BeginToolPress` gains a `Blocked` case starting a drag with `Purpose = Shift ? Unblock : Block`; `MultiSelect` starts one with `Purpose = Select`. `IsGestureActive` becomes `_stroking || _panning || _rectDrag is not null`.

`FinalizeMultiSelection` is **split, not renamed**, into `CommitRectDrag()` and `CancelRectDrag()`, because its current callers do not all mean the same thing. `OnPointerReleased` calls it to commit, but `OnPointerCaptureLost` calls it too and must *cancel*; conversely `FinishInteraction(commit: true)` today clears the rect-drag fields without committing them, and must now commit. A mechanical rename inverts both of those. So: release and `FinishInteraction(commit: true)` commit; capture loss, Escape and `FinishInteraction(commit: false)` cancel. The preview disappears either way. Commit switches on purpose — a `Select` rect persists in the view model, a `Block`/`Unblock` rect applies a patch and vanishes.

Gesture *behaviour* stays in `MapCanvas` rather than moving into `RectDrag`: commit needs the view model, `Invalidate()` and the session, so extracting it would relocate coupling rather than remove it. The win is the field collapse.

### Rendering

Select keeps its outline. Block and Unblock render as filled cells — red for block, green for clear — which needs one new `MapRenderOptions` field and one `CellOverlayKind`. Both reuse existing machinery: `MapRenderer.cs:308-314` already iterates a rectangle's cells for the paste ghost, and `AvaloniaMapDrawSink.cs:37` already honours alpha fills. `BuildRenderRequest` routes `_rectDrag?.Current` to the outline or fill field by purpose, so a Select rect persists in the view model after commit while a Block rect renders live and vanishes on release.

Starting a Block drag forces `ShowBlocked` on, and leaves it on afterwards — silently reverting a visible menu toggle would be surprising.

## 4. Client bug fix

`Scripts/MapManager.cs:335`:

```csharp
private int ItemKey(int x, int y) => y * _map.Height + x;   // should be _map.Width
```

Every other index in the codebase is `y * Width + x` (`MapDocument.Index`, `MapEditStroke.ApplySegment`, `ApplyFloodFill`). This is harmless only because maps have been square. Resize with differing north/south and east/west offsets makes non-square maps a one-dialog operation, so this design is what turns the bug from theoretical into reachable. Fixed here as a one-liner with a regression test.

## 5. Testing

**Core.** Resize: identity window returns `false` with no history entry; grow-then-undo restores dimensions storing zero snapshots; crop-then-undo restores discarded tiles exactly and redo re-crops; an all-empty cropped border stores nothing, asserted through `RetainedHistoryUsedBytes`; out-of-range dimensions throw *before* mutating, asserted by checking the document is untouched; an active stroke throws. The adversarial case is interleaving — paint, resize, paint, undo three times — which exercises per-tile commands recorded at old dimensions replaying against a document that must be back at old dimensions first.

`ApplyBlockedPatch`: starting from `flags = unchecked((int)0xFFFFFFFD)`, block and unblock, asserting every bit but `BlockedFlag` survives; no-op returns `false` with `CanUndo` unchanged; out-of-bounds rect throws.

**Rendering.** The fill overlay emits one `CellOverlayDrawOperation` per cell through `RecordingMapDrawSink`, clips at map edges, and carries red for block versus green for unblock.

**App.** `MapCanvasTests`: press→move→release produces exactly one undoable command; the Shift variant clears; Escape and capture-loss cancel with nothing applied; the preview renders during the drag and vanishes after; `ShowBlocked` is forced on. Resize side effects: selection shifted by the window offset and cleared as a pair when outside, the selection rectangle clipped, hover cleared, paste mode cancelled, cached dimensions refreshed — asserted after undo and redo as well as after the initial apply. `MainWindowTests` gains the Edit-menu item; `ShortcutTests` and the toolbar `Tag` follow the rename. The dialog is tested through the `IEditorDialogs` seam for offset→window conversion, the discard count, and the disabled-when-invalid state.

**Godot.** A regression test for `ItemKey` on a non-square map.

**Smoke doc.** `docs/map-editor-smoke.md` hardcodes exact expected pass counts, which are already stale (it says App 183; the suite is at 261 as of `ff831d7`). Baseline for this branch is Core 201, Rendering 168, App 261, Godot 459. Both the counts and the headed checklist need updating as an explicit task, not as an end-of-work discovery.

## Deferred (consciously out of scope)

- Multi-tile stamp brushes and a saved brush library.
- Rectangle, line and ellipse draw tools for tile layers.
- A spatial sheet view of the palette (useful for browsing; belongs with palette search/favourites, not here).
- Rewriting server-side warp or spawn coordinates when a resize shifts the map.
- Clamping the viewport to map bounds.
