# Map Resize & Rectangle Blocking Implementation Plan

**Goal:** Make existing maps resizable via undoable edge offsets, and turn the Blocked tool into a rectangle gesture (drag sets, Shift clears).

**Architecture:** `MapEditCommand` becomes a small closed hierarchy so resize can join layer/flags deltas as a third command kind that replays by swapping document shape rather than writing cells. Resize is expressed in core as a single source-coordinate window, so growth, cropping and their inverses are one concept. Blocked drops its stroke path entirely in favour of a rectangle patch, which collapses `MapCanvas`'s three rubber-band fields into one nullable `RectDrag`.

**Tech Stack:** C# (.NET 8 core/rendering, .NET 10 app), Avalonia 11.3.20, xunit + Avalonia.Headless.

**Design:** `docs/plans/2026-09-05-map-resize-blocked-rect-design.md`

**Repo rules:** Per `AGENTS.md`, add no comments or doc strings to new/modified code. Leave unrelated existing comments untouched.

**Baseline at branch point (`ff831d7`):** Core 201, Rendering 168, App 261, Godot 459 — all green.

---

## APIs verified

Core:
- `MapEditSession.BeginStroke` `src/MapEditor.Core/Editing/MapEditSession.cs:97`; `CompleteStroke` `:130`; `ApplyFloodFill` `:156`; `ApplyLayerPatch` `:228`; `CancelStroke` `:287`; `Undo` `:308`; `Redo` `:327`
- `MapEditSession.ReplayCommand` `:379`; `PushLayerCommand` `:391`; `ReplayLayerChanges` `:399`; `ReplayFlagsChanges` `:413`; `ValidateTool` `:427` (`private static`, bound is `tool > MapEditTool.BlockedToggle`)
- `MapEditSession.History` `:371` (`internal`), `ActiveStroke` `:373`, `RetainedHistoryUsedBytes` `:35`
- `MapDocument._tiles` `src/MapEditor.Core/MapDocument.cs:66` is **`private readonly`** — Task 2 must drop the modifier before assigning a replacement array
- `MapEditCommand` `src/MapEditor.Core/Editing/MapEditCommand.cs:3` (`internal sealed`); private ctor `:8`; `ForLayerChanges` `:17`; `ForFlagsChanges` `:22`; `AccountedSizeBytes` `:38`; constants `BaseCommandBytes=64` `:5`, `SegmentBytes=32` `:6`, `LayerSlotBytes=32` `:7`, `FlagsSlotBytes=24` `:8`
- `MapEditHistory.PushUndo` `src/MapEditor.Core/Editing/MapEditHistory.cs:33`; `PeekUndo` `:27`; `EvictToCap` `:58` (drops oldest undo first, then last redo)
- `MapDocument` `src/MapEditor.Core/MapDocument.cs:64`; `_tiles` `:66`; `Width` `:83`, `Height` `:85` (both get-only); `TileCount` `:87`; internal ctor `:89`; `Create` `:103`; indexer `:118`; `SetFlags` `:136`; `SetLayer` `:142`; `Index` `:153`
- `MapDocument.LayerCount=5` `:68`, `BlockedFlag=2` `:69`, `MinDimension=1` `:72`, `MaxDimension=1000` `:73`
- `MapTile` `:7` (`readonly struct`); internal ctor `:16`; `Flags` `:26`; `IsBlocked` `:28`; `IsRoof` `:30`; `GetLayer` `:32`
- `MapEditStroke` ctor `src/MapEditor.Core/Editing/MapEditStroke.cs:13`; `_flagsChanges` `:11`; `BlockedToggle` arm `:78-82`; `FlagsChanges` `:47`; `HasDeltas` `:51`
- `MapEditChangeBuffer<T>.Append` `src/MapEditor.Core/Editing/MapEditChangeBuffer.cs:46`; `Count` `:21`; `AllocatedSlotCount` `:23`; `SegmentCount` `:25`; `GetSegment` `:60`; `GetSegmentLength` `:64`
- `MapTileRectangle.ClipTo` `src/MapEditor.Core/MapTileRectangle.cs:7`
- `MapEditTool` `src/MapEditor.Core/Editing/MapEditTool.cs:3` — order is `Pencil, Eraser, Eyedropper, BlockedToggle, Select, MultiSelect, FloodFill`
- `InternalsVisibleTo("MapEditor.Core.Tests")` — `src/MapEditor.Core/Properties/AssemblyInfo.cs:3` (the only friend assembly)

Rendering:
- `MapRenderOptions` `src/MapEditor.Rendering/Composition/MapRenderOptions.cs:5` (positional record; `Default` `:14`)
- `MapRenderer.Render` overlay dispatch `src/MapEditor.Rendering/Composition/MapRenderer.cs:154-172`; `DrawRectangleOutline` `:245`; `DrawRectangleFill` `:301` (**hardcodes `CellOverlayKind.PasteGhost` at `:322`**); `CellRect` `:334`; `Intersects` `:337`
- `MapRenderPalette` `:340` (`internal static`) — `BlockedFill = new(0xFF,0x00,0x00,0x60)` `:345`, `PasteGhostFill` `:350`
- `RenderColor(byte R, byte G, byte B, byte A)` `src/MapEditor.Rendering/Geometry/RenderGeometry.cs:11` — **RGBA order, alpha last**
- `CellOverlayKind` `src/MapEditor.Rendering/Composition/MapDrawOperations.cs:10` — `Blocked, Selected, Hovered, PasteGhost`
- `CellOverlayDrawOperation(Kind, Tile, DestinationRect, FillColor, StrokeColor)` `:37`
- `AvaloniaMapDrawSink.DrawCellOverlay` `src/MapEditor.App/Rendering/AvaloniaMapDrawSink.cs:38` (honours alpha via `Color.FromArgb`, `:57`)
- `RecordingMapDrawSink` `tests/MapEditor.Rendering.Tests/Fakes/RecordingMapDrawSink.cs` — public `Calls` (`IReadOnlyList<object>`), `CallCount`
- `InternalsVisibleTo("MapEditor.Rendering.Tests")` — `src/MapEditor.Rendering/Properties/AssemblyInfo.cs:3`, so the internal `MapRenderPalette` is reachable from rendering tests; `MapRendererTests.cs:990,1026` already read it this way

App:
- `MapCanvas` fields `_multiSelecting` `src/MapEditor.App/Controls/MapCanvas.cs:24`, `_rectDragMoved` `:25`, `_rectDragStart` `:26`
- Their nine touch sites: `FinishInteraction` `:63-65`; `IsGestureActive` `:110`; `OnPointerPressed` `:153`; `OnPointerMoved` `:196-211`; `OnPointerReleased` `:254,265`; `OnPointerCaptureLost` `:276,286`; `BeginToolPress` `:356-358`; `FinalizeMultiSelection` `:378-393`; `OnCanvasInvalidated` `:442-444`
- `MapCanvas.OnKeyDown` Escape `:314-322`; `BuildRenderRequest` `:449`; `TileAt`; `Invalidate` `:104`
- `MapCanvas.FinishInteraction(bool commit)` `:46` — commits or cancels an active **stroke** per the flag, but clears the rect-drag fields `:63-65` **without committing them**
- `OnPointerCaptureLost:286` **calls `FinalizeMultiSelection()`** — a mechanical rename to `CommitRectDrag()` would commit on capture loss, contradicting the cancel-on-capture-loss invariant
- `BeginToolPress(Point position)` `:340` takes **no modifiers** — Shift state must be threaded in explicitly
- `MainWindowViewModel.ActiveTool` range check `src/MapEditor.App/ViewModels/MainWindowViewModel.cs:72` (`< Pencil || > FloodFill`)
- `SelectionRectangle` `:236`; `PasteGhost` `:242`; `CancelPasteMode` `:274`; `Refresh` `:353`; `EditorRefresh` flags `:11-19`
- **`MainWindowViewModel.Undo` `:331` and `Redo` `:342` call `Refresh(EditorRefresh.Canvas)` only** — they update no dimensions and no coordinates
- The session field `_session` is reassigned inside `Refresh` at `:361`, which is the resubscribe point for any session-level event
- `IEditorDialogs` `src/MapEditor.App/Dialogs/IEditorDialogs.cs:22`; `NewMapRequest` record `:20`
- `NewMapDialog` ctor `src/MapEditor.App/Dialogs/NewMapDialog.axaml.cs:10`; `TryParseDimension` `:37`
- `FakeEditorDialogs` `tests/MapEditor.App.Tests/Fakes/FakeEditorDialogs.cs:8`
- `MainWindow` Edit menu `src/MapEditor.App/Views/MainWindow.axaml:18-22`; `BlockedTool` toggle `:82`; tool hotkeys `src/MapEditor.App/Views/MainWindow.axaml.cs:182-208` (`Key.X` → blocked at `:194`)
- `MapCanvasTests` harness constants: `MapSize = 4` `tests/MapEditor.App.Tests/MapCanvasTests.cs:34`, `Cell = 32` `:35`

Godot client:
- `MapManager.ItemKey` `Scripts/MapManager.cs:335` — `y * _map.Height + x`, should be `Width`
- Client reads dimensions fresh at `Scripts/MapManager.cs:133,276,326` — never caches them

Existing tests that must change (verified call sites):
- `MapEditStrokeTests.cs:108` `BlockedToggle_LoopTogglesEachVisitedCellExactlyOnce`, `:132` `BlockedToggle_UndoRedoAndCancelReplayExactFlags` — **delete** (behaviour removed)
- `MapEditStrokeStorageTests.cs:106` `MaximumToggleStroke_WithLoopsNeverExceedsTileCountDeltasOrBitmapBound` — **delete**, replaced by an `ApplyBlockedPatch` storage test
- `MapEditStrokeStorageTests.cs:32` `Assert.Null(stroke.FlagsChanges)` — **delete that line** (member removed)
- `MapCanvasTests.cs:140` `BlockedToggleLoopOverItself_TogglesCrossedCellOnce` — **delete**, replaced by rect-drag tests
- `ShortcutTests.cs:127` — rename `MapEditTool.BlockedToggle` → `MapEditTool.Blocked`
- 15 `.LayerChanges`/`.FlagsChanges` reads (listed in the design) become `((MapLayerChangesCommand)cmd).Changes`

---

### Task 1: Core command hierarchy

Pure refactor. No behaviour change, no new feature. Ends green with the same test count minus nothing.

**Files:**
- Modify: `src/MapEditor.Core/Editing/MapEditCommand.cs`
- Modify: `src/MapEditor.Core/Editing/MapEditSession.cs:379-425`
- Test: `tests/MapEditor.Core.Tests/MapEditStrokeStorageTests.cs`, `MapEditStrokeTests.cs`, `MapEditSessionTests.cs`

**Mutation impact:**
- Source of truth changed: `MapEditCommand.cs:3` — the command's shape, not any map state
- Important readers: `MapEditSession.ReplayCommand:379`, `PushLayerCommand:391`; `MapEditHistory.PushUndo:33` and `EvictToCap:58` (both only via `AccountedSizeBytes`); 15 test call sites
- Derived/cached state affected: `MapEditHistory._usedBytes` — must produce **byte-identical** accounting for existing commands, or history eviction behaviour silently changes
- Required propagation sequence: none at runtime; this is a compile-time restructuring
- Invariants to preserve:
  - `AccountedSizeBytes` for a layer command stays `64 + SegmentCount*32 + AllocatedSlotCount*32`, and for flags `64 + SegmentCount*32 + AllocatedSlotCount*24`
  - the stroke's buffer reaches the command **by reference, never copied** (`MapEditStrokeStorageTests.cs:152`)
- Observable proof required: the existing suite passes unchanged in count and in the `Assert.Same` zero-copy assertion.

**Step 1: Restructure the type**

```csharp
internal abstract class MapEditCommand
{
    internal const long BaseCommandBytes = 64;
    internal const long SegmentBytes = 32;
    internal const long LayerSlotBytes = 32;
    internal const long FlagsSlotBytes = 24;

    protected MapEditCommand(int beforeStateId, int afterStateId)
    {
        BeforeStateId = beforeStateId;
        AfterStateId = afterStateId;
    }

    internal int BeforeStateId { get; }
    internal int AfterStateId { get; }
    internal abstract long AccountedSizeBytes { get; }
    internal abstract void Replay(MapDocument document, bool reverse, bool before);
}

internal abstract class MapDeltaCommand<T> : MapEditCommand
{
    protected MapDeltaCommand(MapEditChangeBuffer<T> changes, int beforeStateId, int afterStateId)
        : base(beforeStateId, afterStateId) => Changes = changes;

    internal MapEditChangeBuffer<T> Changes { get; }

    protected abstract long SlotBytes { get; }
    protected abstract void Apply(MapDocument document, in T change, bool before);

    internal sealed override long AccountedSizeBytes
        => checked(BaseCommandBytes + Changes.SegmentCount * SegmentBytes + Changes.AllocatedSlotCount * SlotBytes);

    internal sealed override void Replay(MapDocument document, bool reverse, bool before)
    {
        // the single loop that ReplayLayerChanges/ReplayFlagsChanges each duplicated
    }
}
```

`MapLayerChangesCommand` and `MapFlagsChangesCommand` supply `SlotBytes` and `Apply`. Drop the static factories; `MapEditSession.PushLayerCommand:391` and the flags push at `:148` use `new`.

`ReplayCommand:379`, `ReplayLayerChanges:399` and `ReplayFlagsChanges:413` are all deleted; the single call site becomes `command.Replay(_document, reverse, before)`.

**Step 2: Update the 15 test call sites**

Mechanically, e.g. `MapEditStrokeStorageTests.cs:98`:

```csharp
var buffer = ((MapLayerChangesCommand)session.History.PeekUndo()!).Changes;
```

`:152`'s `Assert.Same(buffer, command.LayerChanges)` becomes `Assert.Same(buffer, ((MapLayerChangesCommand)command).Changes)`. **Do not weaken this assertion** — it is the zero-copy proof.

**Step 3: Verify green**

Run: `dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj -v minimal`
Expected: `Passed: 201` — identical to baseline. A different number means behaviour changed; investigate before continuing.

**Step 4: Commit**

```bash
git commit -m "refactor: closed hierarchy for map edit commands"
```

---

### Task 2: `MapTile.IsEmpty` and `MapDocument.ResizeTo`

**Files:**
- Modify: `src/MapEditor.Core/MapDocument.cs`
- Test: `tests/MapEditor.Core.Tests/MapDocumentTests.cs`

**Mutation impact:**
- Source of truth changed: `MapDocument._tiles:66`, `Width:83`, `Height:85` become mutable via one internal method
- Important readers: **the Godot client holds a live `MapDocument`** — `Scripts/MapManager.cs:13`, reading dimensions at `:133,276,326`. It re-reads them at every call site and never caches, and cannot call `ResizeTo` because the method is `internal` and `AssemblyInfo.cs:3` names only `MapEditor.Core.Tests` as a friend. **`ResizeTo` must not be made public.**
- Derived/cached state affected: `TileCount:87` is `_tiles.Length`, so it follows automatically. `MapCodec.Encode` reads `document.Width/Height` at write time, so saved files follow. No other derived state found.
- Required propagation sequence: allocate the new array, copy the overlapping region, then assign `_tiles`, `Width`, `Height` together — never leave dimensions describing an array of a different length
- Invariants to preserve:
  - `_tiles.Length == Width * Height` at every observable moment
  - out-of-range dimensions throw **before** any field is assigned
  - tiles outside the copied region are default
- Observable proof required: a test asserting that a rejected resize leaves the document byte-identical, not merely that it threw.

**Step 1: Write the failing tests**

```csharp
[Fact]
public void ResizeTo_GrowingNorthWest_MovesContentAndZeroFillsTheRest()
{
    var doc = MapDocument.Create(3, 3);
    doc.SetLayer(0, 0, 0, new MapTileLayer(5, 9));
    doc.ResizeTo(new MapTileRectangle(-2, -1, 5, 4));

    Assert.Equal(5, doc.Width);
    Assert.Equal(4, doc.Height);
    Assert.Equal(new MapTileLayer(5, 9), doc[2, 1].GetLayer(0));
    Assert.Equal(new MapTileLayer(0, 0), doc[0, 0].GetLayer(0));
    Assert.Equal(20, doc.TileCount);
}

[Fact]
public void ResizeTo_OversizedResult_ThrowsAndLeavesDocumentByteIdentical()
{
    var doc = MapDocument.Create(3, 3);
    doc.SetLayer(1, 1, 0, new MapTileLayer(4, 4));
    doc.SetFlags(2, 2, MapDocument.BlockedFlag);
    byte[] before = MapCodec.Encode(doc);

    Assert.Throws<ArgumentOutOfRangeException>(
        () => doc.ResizeTo(new MapTileRectangle(0, 0, 1001, 3)));

    Assert.Equal(before, MapCodec.Encode(doc));
}

[Fact]
public void IsEmpty_IsFalseForABlockedTileWithNoGraphics()
{
    var doc = MapDocument.Create(2, 2);
    doc.SetFlags(0, 0, MapDocument.BlockedFlag);

    Assert.False(doc[0, 0].IsEmpty);
    Assert.True(doc[1, 1].IsEmpty);
}
```

The third is the adversarial one: a graphics-only `IsEmpty` passes the first two and silently loses blocked flags on crop-undo in Task 3.

**Step 2: Run (red)**

Run: `dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj --filter "ResizeTo|IsEmpty" -v minimal`
Expected: FAIL — `MapDocument` has no `ResizeTo`, `MapTile` has no `IsEmpty`.

**Step 3: Implement**

`MapTile.IsEmpty` beside `IsBlocked:28`:

```csharp
public bool IsEmpty => _flags == 0
    && _layer0 == default && _layer1 == default && _layer2 == default
    && _layer3 == default && _layer4 == default;
```

`MapDocument.Width`/`Height` become `{ get; private set; }`, and **`_tiles:66` loses its `readonly` modifier** — without that the replacement array cannot be assigned and the task will not compile.

`ResizeTo(MapTileRectangle window)` validates `window.Width`/`Height` against `MinDimension`/`MaxDimension` **and** rejects an origin that cannot be safely inverted (see *Bounds and overflow* in the notes) before allocating anything, then builds the new array and assigns `_tiles`, `Width` and `Height` together.

Encoding the whole document is the proof that a rejected resize changed nothing — the previous single-tile assertion would pass an implementation that mutated dimensions before validating.

**Step 4: Green, then full Core suite**

Expected: 201 + 3 = 204.

`MapCodec` is already in scope for `MapDocumentTests` via the `MapEditor.Core` namespace; no new using is needed.

**Step 5: Commit**

```bash
git commit -m "feat: MapDocument.ResizeTo and MapTile.IsEmpty"
```

---

### Task 3: `MapResizeCommand`, `ApplyResize`, and the resize transform

**Files:**
- Modify: `src/MapEditor.Core/Editing/MapEditCommand.cs`
- Modify: `src/MapEditor.Core/Editing/MapEditChange.cs`
- Modify: `src/MapEditor.Core/Editing/MapEditSession.cs`
- Test: `tests/MapEditor.Core.Tests/MapEditSessionTests.cs`

**Mutation impact:**
- Source of truth changed: document dimensions and tile array, plus `_currentStateId`
- Important readers: `MapEditHistory` byte accounting; `IsDirty:79` via state ids; every later per-tile command in the undo stack
- Derived/cached state affected: `_history._usedBytes` — a large lossy crop can exceed the 64 MB cap and evict earlier commands. This is accepted (design, §2) and must not be worked around.
- Required propagation sequence, forward: validate dimensions → collect `!IsEmpty` discards → `ResizeTo(window)` → push `MapResizeCommand` with fresh before/after state ids, matching `PushLayerCommand:391`
- Required propagation sequence, reverse: `ResizeTo(inverse)` → write snapshots back via `SetFlags` + `SetLayer`
- Invariants to preserve:
  - undo across a resize is correct because history is strictly LIFO — per-tile commands recorded at old dimensions replay only after the resize has restored those dimensions. `EvictToCap:58` drops oldest-undo-first and last-redo-first, preserving that ordering.
  - identity window is a no-op returning `false` with no history entry
  - a grow stores zero snapshots
  - dimension validation happens before any mutation
  - **every direction reports its transform.** `ApplyResize`, and `Undo`/`Redo` when the replayed command is a `MapResizeCommand`, raise `Resized` with the offset that maps old coordinates to new. Dimensions alone are insufficient: growing north and growing south both increase `Height`, but only one shifts existing content.
  - a failed or no-op resize raises nothing
- Observable proof required: interleaved paint/resize/paint then three undos asserting tile values and dimensions at each step, not just that undo returned true.

**Step 1: Write the failing tests**

```csharp
[Fact]
public void ApplyResize_CropThenUndo_RestoresDiscardedTilesExactly()
{
    var session = CreateSession(4, 4);
    session.Document.SetLayer(3, 3, 0, new MapTileLayer(7, 8));
    session.Document.SetFlags(3, 0, MapDocument.BlockedFlag);

    Assert.True(session.ApplyResize(new MapTileRectangle(0, 0, 2, 2)));
    Assert.Equal(2, session.Document.Width);

    Assert.True(session.Undo());
    Assert.Equal(4, session.Document.Width);
    Assert.Equal(new MapTileLayer(7, 8), session.Document[3, 3].GetLayer(0));
    Assert.True(session.Document[3, 0].IsBlocked);
}

[Fact]
public void ApplyResize_GrowThenUndo_AllocatesNoSnapshotBuffer()
{
    var session = CreateSession(4, 4);
    long before = session.RetainedHistoryUsedBytes;

    Assert.True(session.ApplyResize(new MapTileRectangle(0, 0, 8, 8)));

    Assert.Equal(MapEditCommand.BaseCommandBytes, session.RetainedHistoryUsedBytes - before);
    Assert.True(session.Undo());
    Assert.Equal(4, session.Document.Width);
}

[Fact]
public void ApplyResize_IdentityWindow_IsNoOp()
{
    var session = CreateSession(4, 4);
    Assert.False(session.ApplyResize(new MapTileRectangle(0, 0, 4, 4)));
    Assert.False(session.CanUndo);
}

[Fact]
public void PaintResizePaint_UndoesBackThroughTheResizeInOrder()
{
    var session = CreateSession(4, 4);
    session.SelectedTileLayer = new MapTileLayer(1, 1);
    session.BeginStroke(MapEditTool.Pencil, 0, 0);
    session.CompleteStroke();

    Assert.True(session.ApplyResize(new MapTileRectangle(0, 0, 6, 6)));

    session.SelectedTileLayer = new MapTileLayer(2, 2);
    session.BeginStroke(MapEditTool.Pencil, 5, 5);
    session.CompleteStroke();

    Assert.True(session.Undo());
    Assert.Equal(new MapTileLayer(0, 0), session.Document[5, 5].GetLayer(0));
    Assert.Equal(6, session.Document.Width);

    Assert.True(session.Undo());
    Assert.Equal(4, session.Document.Width);
    Assert.Equal(new MapTileLayer(1, 1), session.Document[0, 0].GetLayer(0));

    Assert.True(session.Undo());
    Assert.Equal(new MapTileLayer(0, 0), session.Document[0, 0].GetLayer(0));
    Assert.False(session.CanUndo);
}

[Fact]
public void ApplyResize_WithActiveStroke_Throws()
{
    var session = CreateSession(4, 4);
    session.BeginStroke(MapEditTool.Pencil, 0, 0);
    Assert.Throws<InvalidOperationException>(
        () => session.ApplyResize(new MapTileRectangle(0, 0, 2, 2)));
}
```

`PaintResizePaint_...` is the adversarial one. A `MapResizeCommand` that restores dimensions but not tiles, or that replays in the wrong order relative to per-tile commands, passes every other test here and fails this one.

**Step 2: Run (red)** — Expected: FAIL, `ApplyResize` not defined.

**Step 3: Implement**

The transform type, public so the App layer can consume it:

```csharp
public readonly record struct MapResizeTransform(int OffsetX, int OffsetY, int Width, int Height);
```

A tile at old `(x, y)` is at new `(x + OffsetX, y + OffsetY)`. For a forward window `(X, Y, W, H)` the offset is `(-X, -Y)`; for its inverse it is `(X, Y)`. `MapResizeCommand` exposes `TransformFor(bool reverse)` so both directions come from one place.

`MapEditSession` gains `public event Action<MapResizeTransform>? Resized`, raised at the end of `ApplyResize`, and in `Undo:308`/`Redo:327` via `if (command is MapResizeCommand resize) Resized?.Invoke(resize.TransformFor(reverse))` **after** the replay and after `_currentStateId` is assigned, so a handler observing the session sees settled state.

`MapTileSnapshot` in `MapEditChange.cs` beside `MapLayerChange:3`:

```csharp
internal readonly record struct MapTileSnapshot(int X, int Y, MapTile Tile);
```

`MapResizeCommand : MapEditCommand` holds the window, old and new dimensions, and a **nullable, lazily allocated** `MapEditChangeBuffer<MapTileSnapshot>?`. `ResizeSlotBytes = 56` joins the existing constants.

**The buffer must be lazy.** `MapEditChangeBuffer`'s constructor eagerly allocates one segment of four slots (`MapEditChangeBuffer.cs:15-18`), so an *empty* buffer already accounts for `64 + 32 + 4×56 = 320` bytes. A grow — which discards nothing — would then charge 320 bytes for storing nothing, and no "grow is free" assertion could hold. Allocate on the first appended snapshot only; `AccountedSizeBytes` returns `BaseCommandBytes` while the buffer is null. This mirrors `MapEditStroke`, which likewise allocates only the buffer its tool needs (`MapEditStroke.cs:24-33`).

`Replay` resizes forward by the window or reverse by `(-X, -Y, oldW, oldH)`, writing snapshots back on the reverse pass only, and skips the write loop entirely when the buffer is null.

`MapEditSession.ApplyResize(MapTileRectangle window)` follows `ApplyFloodFill:156`'s shape for the stroke guard and the state-id bookkeeping.

Add the remaining coverage the design promised:

```csharp
[Fact]
public void ApplyResize_CropUndoRedo_RestoresThenRecropsDimensionsAndContent()
{
    // crop 4x4 -> 2x2 with art at (3,3); undo restores 4x4 + the tile;
    // redo returns to 2x2 and the tile is gone again
}

[Fact]
public void ApplyResize_EmptyBorderCrop_AllocatesNoSnapshotBuffer()
{
    // 4x4 with art only in the 2x2 top-left, crop to that 2x2: tiles are
    // discarded but all are empty, so no snapshot is ever appended and the
    // buffer is never allocated — growth is exactly BaseCommandBytes
}

[Fact]
public void ApplyResize_Rejected_LeavesDocumentHistoryStateIdAndDirtyUnchanged()
{
    // paint once, MarkSaved, then attempt an oversized resize:
    // throws; encoded bytes, CanUndo/CanRedo and IsDirty all unchanged
}

[Fact]
public void Undo_OfAResize_RaisesResizedWithTheInverseOffset()
{
    // grow north/west by (2,1) -> Resized offset (2,1);
    // undo -> Resized offset (-2,-1) and the new dimensions
}
```

```csharp
[Fact]
public void ApplyResize_IdentityOrRejected_RaisesNoResizedEvent()
{
    var session = CreateSession(4, 4);
    int raised = 0;
    session.Resized += _ => raised++;

    Assert.False(session.ApplyResize(new MapTileRectangle(0, 0, 4, 4)));
    Assert.Throws<ArgumentOutOfRangeException>(
        () => session.ApplyResize(new MapTileRectangle(0, 0, 1001, 4)));

    Assert.Equal(0, raised);
}
```

`Undo_OfAResize_RaisesResizedWithTheInverseOffset` is the proof for finding 1: without it, undo silently leaves the view model on the old coordinate system. `ApplyResize_IdentityOrRejected_RaisesNoResizedEvent` guards the other direction — a handler that fires on a no-op would clear the user's selection for nothing.

**Step 4: Green** — Core 204 + 10 = 214.

**Step 5: Commit**

```bash
git commit -m "feat: undoable map resize with a reported transform"
```

---

### Task 4: `ApplyBlockedPatch`, stroke-flags removal, tool rename

Three coupled changes; splitting them leaves the tree non-compiling in between.

**Files:**
- Modify: `src/MapEditor.Core/Editing/MapEditTool.cs`, `MapEditStroke.cs`, `MapEditSession.cs`
- Test: `tests/MapEditor.Core.Tests/MapEditSessionTests.cs`, `MapEditStrokeTests.cs`, `MapEditStrokeStorageTests.cs`

**Mutation impact:**
- Source of truth changed: `MapTile._flags` via `MapDocument.SetFlags:136`
- Important readers: `MapTile.IsBlocked:28`; the client's movement gate `Scripts/MapManager.cs:135`; `MapCodec.Encode` writes the full `Flags` int; the `ShowBlocked` overlay at `MapRenderer.cs:140-144`
- Derived/cached state affected: no derived state found — flags are read directly from the tile
- Required propagation sequence: compute `blocked ? old | BlockedFlag : old & ~BlockedFlag` → append `MapFlagsChange` only where it differs → `SetFlags` → push one `MapFlagsChangesCommand`
- Invariants to preserve:
  - **unknown flag bits survive** — never assign, always mask. The smoke checklist depends on this.
  - no-op returns `false` with no history entry
  - after this task nothing can begin a stroke with the blocked tool
- Observable proof required: a test starting from `0xFFFFFFFD` asserting every other bit survives a block/unblock round trip.

**Step 1: Write the failing tests**

```csharp
[Fact]
public void ApplyBlockedPatch_PreservesUnknownFlagBits()
{
    var session = CreateSession(4, 4);
    int noisy = unchecked((int)0xFFFFFFFD);
    session.Document.SetFlags(1, 1, noisy);

    Assert.True(session.ApplyBlockedPatch(new MapTileRectangle(0, 0, 3, 3), blocked: true));
    Assert.Equal(noisy | MapDocument.BlockedFlag, session.Document[1, 1].Flags);

    Assert.True(session.ApplyBlockedPatch(new MapTileRectangle(0, 0, 3, 3), blocked: false));
    Assert.Equal(noisy & ~MapDocument.BlockedFlag, session.Document[1, 1].Flags);
}

[Fact]
public void ApplyBlockedPatch_AlreadyInTargetState_IsNoOp()
{
    var session = CreateSession(4, 4);
    Assert.False(session.ApplyBlockedPatch(new MapTileRectangle(0, 0, 4, 4), blocked: false));
    Assert.False(session.CanUndo);
}

[Fact]
public void ApplyBlockedPatch_RectOutsideTheMap_Throws()
{
    var session = CreateSession(4, 4);
    Assert.Throws<ArgumentOutOfRangeException>(
        () => session.ApplyBlockedPatch(new MapTileRectangle(2, 2, 5, 5), blocked: true));
    Assert.False(session.CanUndo);
}

[Fact]
public void BeginStroke_WithBlockedTool_Throws()
{
    var session = CreateSession(4, 4);
    Assert.Throws<ArgumentOutOfRangeException>(
        () => session.BeginStroke(MapEditTool.Blocked, 0, 0));
}
```

The first is adversarial: an implementation assigning `BlockedFlag` outright passes a naive round-trip test and fails this one.

**Step 2: Run (red).**

**Step 3: Implement, then delete the dead paths**

Add `ApplyBlockedPatch` following `ApplyLayerPatch:228`'s validation style. Rename `MapEditTool.BlockedToggle` → `Blocked`. Tighten `ValidateTool:427` to `tool > MapEditTool.Eyedropper` — no enum reordering needed, `Blocked` already sits just past `Eyedropper`.

Then remove from `MapEditStroke`: `_flagsChanges:11`, its allocation in the ctor `:13`, the `BlockedToggle` arm `:78-82`, the `FlagsChanges` property `:47`, and the flags branches in `CompleteStroke:142-150` and `CancelStroke:287`. `MapFlagsChangesCommand` stays — `ApplyBlockedPatch` is now its only producer.

Delete `MapEditStrokeTests.cs:108` and `:132`, `MapEditStrokeStorageTests.cs:106` and its line `:32`. Replace the deleted storage test with a full-map `ApplyBlockedPatch(new MapTileRectangle(0, 0, 1000, 1000), true)` asserting `Count == 1_000_000` and the same `AllocatedSlotCount`/`SegmentCount` bounds it previously asserted, so the buffer-growth coverage is not lost.

**Step 4: Green** — Core 214 − 3 deleted + 4 new + 1 replacement = 216.

**Step 5: Commit**

```bash
git commit -m "feat: rectangle blocked patch, drop blocked strokes"
```

---

### Task 5: Rendering — block preview fill

**Files:**
- Modify: `src/MapEditor.Rendering/Composition/MapRenderOptions.cs`, `MapDrawOperations.cs`, `MapRenderer.cs`
- Test: `tests/MapEditor.Rendering.Tests/MapRendererTests.cs`

**Step 1: Write the failing test**

```csharp
[Fact]
public void Render_BlockPreview_FillsEachCellClippedToTheMap()
{
    var sink = new RecordingMapDrawSink();
    var options = MapRenderOptions.Default with
    {
        BlockPreview = new BlockPreview(new MapTileRectangle(2, 2, 4, 4), Blocked: true)
    };

    Renderer.Render(new MapRenderRequest(MapDocument.Create(4, 4), Viewport, options), sink);

    var overlays = sink.Calls.OfType<CellOverlayDrawOperation>()
        .Where(o => o.Kind == CellOverlayKind.BlockPreview).ToList();
    Assert.Equal(4, overlays.Count);
    Assert.All(overlays, o => Assert.Equal(MapRenderer.MapRenderPalette.BlockPreviewFill, o.FillColor));
}
```

Plus the colour pairing, which the design promises in both directions:

```csharp
[Theory]
[InlineData(true)]
[InlineData(false)]
public void Render_BlockPreview_UsesRedToBlockAndGreenToClear(bool blocked)
{
    // assert FillColor == blocked ? BlockPreviewFill : UnblockPreviewFill
}
```

Clipping is the adversarial part: the 4×4 rect at (2,2) on a 4×4 map must yield 4 cells, not 16. `DrawRectangleFill:301` already calls `ClipTo:307`, so this passes once wired — but it guards against a hand-rolled loop being added instead.

**Step 2: Run (red).**

**Step 3: Implement**

```csharp
public readonly record struct BlockPreview(MapTileRectangle Rectangle, bool Blocked);
```

as a new trailing optional field on `MapRenderOptions:5`. Add `CellOverlayKind.BlockPreview` to `MapDrawOperations.cs:10`. Add `BlockPreviewFill = new(0xFF, 0x00, 0x00, 0x60)` and `UnblockPreviewFill = new(0x00, 0xFF, 0x00, 0x60)` to `MapRenderPalette:340` — RGBA, alpha last.

`DrawRectangleFill:301` gains a `CellOverlayKind kind` parameter replacing the hardcoded `PasteGhost` at `:322`; the existing paste-ghost caller at `:169` passes `CellOverlayKind.PasteGhost`. Dispatch the new option after the paste-ghost block at `:172`.

**Step 4: Green** — Rendering 168 + 1 + 2 (the theory's two cases) = 171.

**Step 5: Commit**

```bash
git commit -m "feat: block preview fill overlay"
```

---

### Task 6: `MapCanvas` rect-drag rework

**Files:**
- Create: `src/MapEditor.App/Controls/RectDrag.cs`
- Modify: `src/MapEditor.App/Controls/MapCanvas.cs`
- Modify: `src/MapEditor.App/Views/MainWindow.axaml` (`Tag="BlockedToggle"` → `"Blocked"`, `:82`), `MainWindow.axaml.cs:194`
- Test: `tests/MapEditor.App.Tests/MapCanvasTests.cs`, `ShortcutTests.cs:127`

**Mutation impact:**
- Source of truth changed: `MapCanvas`'s gesture state — three fields become one
- Important readers: all nine sites listed in APIs verified
- Derived/cached state affected: `_viewModel.SelectionRectangle:236` is written by the Select purpose only; the Block/Unblock rect lives in `_rectDrag` and never reaches the view model
- Required propagation sequence on commit: `ApplyBlockedPatch` → `_rectDrag = null` → `Invalidate()` → `_viewModel.Refresh(Canvas | Commands | Title)`, matching `OnPointerReleased:270`. Skipping `Commands` leaves Undo greyed out after a successful block.
- Invariants to preserve — **the full lifecycle, stated explicitly because a mechanical rename gets two of these backwards:**

  | Event | Rect drag | Today's code |
  |---|---|---|
  | `OnPointerReleased:254` | **commit** | calls the finalizer `:265` — same direction |
  | `OnPointerCaptureLost:276` | **cancel** | calls the finalizer `:286` — **inverts if renamed** |
  | `FinishInteraction(commit: true)` | **commit** | clears the fields `:63-65` without committing — **inverts** |
  | `FinishInteraction(commit: false)` | **cancel** | clears the fields — same direction |
  | `OnKeyDown` Escape `:317` | **cancel** | routes through `FinishInteraction(false)` |
  | `OnCanvasInvalidated:442` session swap | **cancel** | clears the fields — same direction |

  So `FinalizeMultiSelection:378` splits into `CommitRectDrag()` and `CancelRectDrag()`; it is **not** a rename. `FinishInteraction` gains a `CommitRectDrag()`/`CancelRectDrag()` call driven by its existing `commit` flag, matching how it already treats strokes.
  - a Block drag produces exactly one undo entry
  - `ShowBlocked` is forced on when a Block drag starts and left on afterwards
  - the block preview disappears on both commit and cancel
  - **Shift is sampled at pointer press**, not at release, so releasing the key mid-drag cannot flip a block into an unblock
- Observable proof required: assert `document[x,y].IsBlocked` and `session.CanUndo`, not that a method was called.

**Step 1: Write the failing tests**

```csharp
[AvaloniaFact]
public async Task BlockedDrag_BlocksTheRectangleAsOneUndoEntry()
{
    Harness harness = await CreateSmallMapAsync();
    harness.ViewModel.ActiveTool = MapEditTool.Blocked;
    MapDocument document = harness.ViewModel.Session.Document;

    harness.Window.MouseDown(new Point(Cell / 2, Cell / 2), MouseButton.Left, RawInputModifiers.None);
    harness.Window.MouseMove(new Point(Cell * 2 + Cell / 2, Cell * 2 + Cell / 2), RawInputModifiers.None);
    harness.Window.MouseUp(new Point(Cell * 2 + Cell / 2, Cell * 2 + Cell / 2), MouseButton.Left, RawInputModifiers.None);

    for (int y = 0; y <= 2; y++)
        for (int x = 0; x <= 2; x++)
            Assert.True(document[x, y].IsBlocked);

    Assert.False(document[3, 3].IsBlocked);
    Assert.True(harness.ViewModel.ShowBlocked);
    Assert.True(harness.ViewModel.Undo());
    Assert.False(document[0, 0].IsBlocked);
    Assert.False(harness.ViewModel.CanUndo);
}

[AvaloniaFact]
public async Task BlockedShiftDrag_ClearsInsteadOfSetting() { /* pre-block, then Shift-drag, assert cleared */ }

[AvaloniaFact]
public async Task BlockedDragEscape_AppliesNothing()
{
    // MouseDown, MouseMove, Key.Escape, MouseUp
    // Assert no tile blocked and CanUndo is false
}

[AvaloniaFact]
public async Task BlockedDragCaptureLost_AppliesNothing()
{
    // MouseDown, MouseMove, then raise capture loss
    // Assert no tile blocked, CanUndo false, and no preview in the render request
}

[AvaloniaFact]
public async Task FinishInteractionCommit_AppliesTheBlockRectangle()
{
    // MouseDown, MouseMove, FinishInteraction(commit: true)
    // Assert the rectangle is blocked exactly once
}

[AvaloniaFact]
public async Task BlockedDragShiftReleasedMidDrag_StillClears()
{
    // Shift held at MouseDown, released before MouseUp -> still an unblock
}

[AvaloniaFact]
public async Task BlockPreview_VanishesAfterCommitAndAfterCancel() { }
```

`BlockedDragCaptureLost_AppliesNothing` and `FinishInteractionCommit_AppliesTheBlockRectangle` are the adversarial pair: a mechanical rename of `FinalizeMultiSelection` passes the happy-path drag test and fails both, in opposite directions. `BlockedDragShiftReleasedMidDrag_StillClears` fails any implementation that reads modifiers at release.

**Step 2: Run (red).**

**Step 3: Implement**

```csharp
internal enum RectDragPurpose { Select, Block, Unblock }

internal readonly record struct RectDrag(
    RectDragPurpose Purpose, MapTileCoordinate Origin, MapTileRectangle Current);
```

`RectDrag` is **state only** — it constructs and carries data, registers nothing, fires no events, and mutates no session state. Commit behaviour stays in `MapCanvas` because it needs `_viewModel`, `Invalidate()` and the session.

Replace the three fields with `private RectDrag? _rectDrag`. `Current` is initialized to a 1×1 rect at press, so `Moved` is not needed and jitter cannot change semantics.

`BeginToolPress:340` changes signature to `BeginToolPress(Point position, KeyModifiers modifiers)` — it currently takes only a position and cannot see Shift. The sole caller is `OnPointerPressed:145`, which has `e.KeyModifiers` in hand. The `Blocked` case reads `modifiers.HasFlag(KeyModifiers.Shift)` **once, at press**, and stores the resulting purpose in `RectDrag`.

`IsGestureActive:110` becomes `_stroking || _panning || _rectDrag is not null`. `FinalizeMultiSelection:378` splits into `CommitRectDrag()` and `CancelRectDrag()` wired per the lifecycle table above.

`BuildRenderRequest:449` routes `_rectDrag` by purpose: `Select` → the existing `SelectionRectangle` field, `Block`/`Unblock` → the new `BlockPreview` field.

**Step 4: Green** — App 261 − 1 deleted + 7 new = 267.

**Step 5: Commit**

```bash
git commit -m "feat: rectangle blocking gesture"
```

---

### Task 7: Resize dialog and menu wiring

**Files:**
- Create: `src/MapEditor.App/Dialogs/ResizeMapDialog.axaml` + `.axaml.cs`
- Modify: `src/MapEditor.App/Dialogs/IEditorDialogs.cs`, `AvaloniaEditorDialogs.cs`, `EditorDialogsProxy.cs`
- Modify: `src/MapEditor.App/ViewModels/MainWindowViewModel.cs`
- Modify: `src/MapEditor.App/Views/MainWindow.axaml:18-22`, `MainWindow.axaml.cs` (new `OnResize` handler beside `OnNew:243`)
- Modify: `tests/MapEditor.App.Tests/Fakes/FakeEditorDialogs.cs`
- Test: `tests/MapEditor.App.Tests/MainWindowViewModelTests.cs`, `MainWindowTests.cs`

**Mutation impact:**
- Source of truth changed: the document, via `Session.ApplyResize`
- Important readers: `SelectedX/Y:190,196`, `SelectionRectangle:236`, `HoverX/Y`, `PasteMode:230`, `MapWidth:216`/`MapHeight:218`, and the status bar bound to them
- Derived/cached state affected: `_mapWidth`/`_mapHeight` are **cached fields** on the view model (`MainWindowViewModel.cs:42-43`, set in the ctor `:53-54`, exposed as `MapWidth:216`/`MapHeight:218` and bound to the status bar).

  **`Refresh(EditorRefresh.Document)` will NOT update them.** That branch (`MainWindowViewModel.cs:356-375`) is guarded by `!ReferenceEquals(session, _session)` at `:359` — it only fires when the session is *replaced*, as by New/Open. A resize keeps the same session, so the branch is skipped and the status bar silently keeps the old size.

  Do not pass `EditorRefresh.Document` from the resize path for a second reason: that branch also nulls `HoverX/Y`, `SelectedX/Y`, `_clipboard`, paste mode and `SelectionRectangle` (`:365-371`). Those are document-replacement semantics and would wipe the very selection this task shifts.
- Required propagation sequence, in order:
  1. `Session.ApplyResize(window)`; if it returns `false`, stop — no state changes
  2. the session raises `Resized`; **the view model's handler does the coordinate work, not `ResizeMap`**, so apply, undo and redo all run the identical path (see below)
  3. `CancelPasteMode()` (`:274`)
  4. assign `_mapWidth`/`_mapHeight` explicitly via `SetField(ref _mapWidth, Session.Document.Width, nameof(MapWidth))` and the same for height — **not** via `Refresh(EditorRefresh.Document)`, see above
  5. `Refresh(EditorRefresh.Canvas | EditorRefresh.Commands | EditorRefresh.Title)` — no `Document` flag
- **Undo and redo resize the same live document.** `Undo:331` and `Redo:342` call `Refresh(EditorRefresh.Canvas)` only, so without this task's event handler they leave `MapWidth`/`MapHeight` stale and leave selection, selection rectangle and hover expressed in the *old* coordinate system. A grow-north followed by undo shows it immediately.

  The fix is a single handler, subscribed once:

  ```csharp
  private void OnSessionResized(MapResizeTransform t)
  {
      SetField(ref _mapWidth, t.Width, nameof(MapWidth));
      SetField(ref _mapHeight, t.Height, nameof(MapHeight));
      (SelectedX, SelectedY) = ShiftPoint(SelectedX, SelectedY, t);  // both, or neither
      HoverX = null; HoverY = null;
      SelectionRectangle = ShiftRect(SelectionRectangle, t);
      CancelPasteMode();
  }
  ```

  Hover is cleared rather than shifted: it is re-derived from the next pointer move, and a resize is not a pointer event.

  **`ShiftPoint` and `ShiftRect` contracts.** `ShiftPoint(int? x, int? y, in MapResizeTransform t)` returns `(int? X, int? Y)`. It translates the selected point **as a pair**: if either translated coordinate leaves `[0, Width)`/`[0, Height)`, *both* come back `null`. There is no clamping, per the invariant below.

  **The pair is why this is not two independent calls.** Shifting the axes separately lets a crop on one axis alone produce `SelectedX = null, SelectedY = 1` — a half-selected point that every reader of `SelectedX`/`SelectedY` would have to defend against. A selected tile either survives the transform intact or is gone.

  `ShiftRect` translates by `(OffsetX, OffsetY)` and then **clips** to the new bounds via `MapTileRectangle.ClipTo:7`, keeping the surviving part and returning `null` when nothing survives. Clipping is not clamping: every tile left in the rectangle was in the user's original selection, so the result is always a subset of what they chose, never a rectangle slid onto tiles they never picked. A selection half-cropped keeps its surviving half rather than vanishing.

  **Subscription lifecycle:** subscribe in the constructor next to `_controller.StateChanged` (`:56`), and in `Refresh` at `:361` — where `_session` is reassigned on document replacement — **unsubscribe from the old session before assigning and subscribe to the new one after**. Missing this leaks a handler onto a discarded session and, worse, keeps a stale session driving the view model's coordinates.
- Invariants to preserve:
  - `_viewport` is deliberately **not** touched (design, §2)
  - a cancelled dialog mutates nothing
  - clearing, never clamping, out-of-bounds selection
  - the selected point moves **as a pair**: `SelectedX` and `SelectedY` are never left one-null
  - a partially cropped selection is **clipped to its surviving part**, never clamped or slid
  - **a selection cropped away is not restored by undo.** Selection and hover are UI state and are never stored in history; each transform re-derives them by shift-or-clear. Undoing a crop restores the *tiles* but leaves a cleared selection cleared. This is deliberate — restoring it would mean persisting UI state into `MapResizeCommand`.
- Observable proof required: assert `MapWidth`/`MapHeight` and the selection **after undo and after redo**, not only after the initial apply.

**Step 1: Write the failing tests**

```csharp
[Fact]
public void ResizeMap_ShiftsSelectionAndRefreshesCachedDimensions()
{
    // 4x4, select (0,0), grow 2 north/west -> selection follows to (2,2), MapWidth == 6
}

[Fact]
public void ResizeMap_SelectionCroppedAway_IsClearedNotClamped()
{
    // 4x4, select (3,1), crop the WIDTH only, to (0,0,2,4)
    // -> BOTH SelectedX and SelectedY null; not (1,1), and not (null,1)
}

[Fact]
public void ResizeMap_CancelsPasteMode()
{
    // enter paste mode, resize, assert PasteMode false
}

[Fact]
public void ResizeMap_PartiallyCroppedSelection_KeepsTheSurvivingPart()
{
    // 4x4, SelectionRectangle (1,1,3,3), crop to (0,0,3,3)
    // -> rectangle becomes (1,1,2,2), not null and not slid
}

[Fact]
public void UndoOfAResize_RestoresDimensionsAndUnshiftsTheSelection()
{
    // 4x4, select (0,0), grow 2 north/west -> selection (2,2), MapWidth 6
    // Undo() -> MapWidth back to 4 AND selection back to (0,0)
}

[Fact]
public void RedoOfAResize_ReappliesDimensionsAndShift()
{
    // same setup; Undo then Redo -> MapWidth 6 and selection (2,2) again
}

[Fact]
public void DocumentReplacement_UnsubscribesTheOldSessionResizeHandler()
{
    // resize on session A, replace the document, resize session A directly:
    // the view model must not react to the discarded session
}
```

`ResizeMap_SelectionCroppedAway_IsClearedNotClamped` is adversarial twice over. It crops only the width, so the surviving `Y` catches an implementation that shifts the two axes independently and leaves `SelectedX = null, SelectedY = 1`; a `(3,3)`-into-`(0,0,2,2)` case would not, because it invalidates both axes at once. And `Math.Clamp` passes the shift test while silently pointing the selection at a tile the user never chose. `UndoOfAResize_RestoresDimensionsAndUnshiftsTheSelection` is the proof for finding 1 — it fails against any implementation that does the coordinate work inside `ResizeMap` instead of in the `Resized` handler.

**Step 2: Run (red).**

**Step 3: Implement**

**Dialog contract.** `(int width, int height)` cannot support the discard warning — counting non-empty discarded tiles needs the tiles. And returning four offsets while also converting to a window inside the dialog was self-contradictory. One signature does both jobs:

```csharp
Task<MapTileRectangle?> ShowResizeMapAsync(MapDocument document);
```

The dialog reads `document` (dimensions and tile emptiness) and returns the **already-converted window**, or `null` on cancel. It treats the document as read-only and must not mutate it. `ResizeMapRequest` is not needed. `FakeEditorDialogs` mirrors the signature exactly with a `ResizeMapResult` field and `ResizeMapShown` counter, matching the `NewMapResult`/`NewMapShown` pattern at `Fakes/FakeEditorDialogs.cs:9,27`.

The dialog holds four signed offset fields plus editable width/height. Absolute entry resolves against the top-left-anchored edges: typing a width adjusts `east`, a height adjusts `south`. Live readouts show `100 × 100 → 120 × 110` and, when lossy, `⚠ Discards N non-empty tiles`. Resize is disabled when the result leaves 1..1000, reusing `TryParseDimension:37`'s bounds.

Window conversion, once, in the dialog:

```csharp
new MapTileRectangle(-west, -north, width + west + east, height + north + south)
```

**Counting discards without double-counting corners.** Four naive bands overlap at the corners and inflate the count. Let `keep = window.ClipTo(oldWidth, oldHeight)`; if it is `null`, every tile is discarded. Otherwise decompose the discarded region into four **disjoint** bands — full-width top and bottom, then left and right restricted to the kept row span:

```
top    rows [0, keep.Y)                        cols [0, W)
bottom rows [keep.Y + keep.Height, H)          cols [0, W)
left   rows [keep.Y, keep.Y + keep.Height)     cols [0, keep.X)
right  rows [keep.Y, keep.Y + keep.Height)     cols [keep.X + keep.Width, W)
```

This stays proportional to what is cut rather than to map area, and each tile is visited at most once.

**Dialog tests are headless, not fake-seam.** The `IEditorDialogs` fake proves orchestration only — that the view model calls the dialog and applies the returned window. The dialog's own behaviour (offset↔absolute synchronisation, the live discard count, the disabled state at the bounds) needs `[AvaloniaFact]` tests constructing `ResizeMapDialog` directly, in the style of the existing headless window tests. Six of them: offsets→window, absolute width adjusts east, absolute height adjusts south, discard count with a corner overlap, zero count for an empty border, and Resize disabled at 0 and at 1001.

**Orchestration.** `MainWindow`, not the view model, owns `IEditorDialogs` (`MainWindow.axaml.cs:32`) — the view model reaches dialogs only indirectly through `_controller`. `ResizeMap(window)` takes an already-converted window, so the click handler shows the dialog, in the shape `OnLoadAssets:357` already uses:

```csharp
private void OnResize(object? sender, RoutedEventArgs e)
    => _ = RunCommandAsync(async () =>
    {
        MapTileRectangle? window = await _dialogs.ShowResizeMapAsync(_viewModel.Session.Document);
        if (window is { } value)
        {
            _viewModel.ResizeMap(value);
        }
    });
```

`RunCommandAsync:499` is doing real work here, not decoration: it calls `_canvas.FinishInteraction(commit: true)` before the command, so a block drag in progress commits before the dialog opens — matching the lifecycle table in Task 6 — and it routes any dialog exception through `ShowFatalErrorAsync`.

Menu item goes at the bottom of the Edit menu (`MainWindow.axaml:18-22`) behind a `<Separator />`. No hotkey, so `ApplyHotKeys:225` is untouched.

One `MainWindowTests` case covers the wiring end to end, which neither the view-model tests nor the headless dialog tests reach:

```csharp
[AvaloniaFact]
public void ResizeMenuItem_ShowsTheDialogAndAppliesTheReturnedWindow()
{
    Dialogs.ResizeMapResult = new MapTileRectangle(0, 0, 6, 6);
    Find<MenuItem>("ResizeCommand").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

    Assert.Equal(1, Dialogs.ResizeMapShown);
    Assert.Equal(6, ViewModel.MapWidth);
}
```

**Step 4: Green** — App 267 + 7 view-model tests + 6 headless dialog tests + 1 menu-wiring test = 281.

**Step 5: Commit**

```bash
git commit -m "feat: resize map dialog"
```

---

### Task 8: Client `ItemKey` fix and doc refresh

**Files:**
- Modify: `Scripts/MapManager.cs:335`
- Test: `tests/Goose2Client.Tests/` — a new `MapItemKeyTests.cs`
- Modify: `docs/map-editor-smoke.md`

**Mutation impact:**
- Source of truth changed: none — this corrects an index computation
- Important readers: the ground-item dictionary keyed by `ItemKey`, `Scripts/MapManager.cs:335` and its callers
- Derived/cached state affected: item lookups collide today on non-square maps; no persisted data is keyed this way
- Invariants to preserve: distinct tiles produce distinct keys for every map shape
- Observable proof required: a test over a non-square map asserting no collision, which fails on the current `Height` form.

**Step 1: Write the failing test**

**The map must be wider than it is tall.** With `width < height` the buggy stride exceeds the row length and produces gaps, not collisions — a 3×5 map yields 15 distinct keys under both formulas and would pass against the bug. A 5×3 map yields 11 distinct keys out of 15 under `y * Height + x`:

```
(3,0) -> 0*3 + 3 = 3
(0,1) -> 1*3 + 0 = 3     collision
```

**Test seam.** `ItemKey` is `private` (`Scripts/MapManager.cs:335`) and `MapManager` is a `Node2D` (`:11`) needing a scene tree, so calling the instance method under test would need a scene fixture. But internals are reachable: `tests/Goose2Client.Tests/Goose2Client.Tests.csproj:13` does `<Compile Include="../../Scripts/**/*.cs" />`, compiling the client sources **into the test assembly**, so an `internal static` member needs no `InternalsVisibleTo` at all.

The seam must contain the width-versus-height decision, or it proves nothing. A helper taking `width` already resolved (`TileIndex(x, y, width)`) is green no matter which dimension `MapManager` hands it — and that choice *is* the bug. So take the document:

```csharp
internal static int ItemKey(MapDocument map, int x, int y) => y * map.Width + x;
```

`MapManager`'s private instance method becomes `private int ItemKey(int x, int y) => ItemKey(_map, x, y);`.

No doc comment: under `AGENTS.md`'s no-new-comments default, the signature and body already say it, and the earlier draft's comment was restating the code.

```csharp
[Fact]
public void ItemKey_IsUniquePerTileWhenWidthExceedsHeight()
{
    MapDocument map = MapDocument.Create(5, 3);
    var keys = new List<int>();
    for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
            keys.Add(MapManager.ItemKey(map, x, y));

    Assert.Equal(map.TileCount, keys.Distinct().Count());
}
```

**Step 2: Run (red)**

The red phase is a **compile failure** — `MapManager.ItemKey(MapDocument, int, int)` does not exist yet — not an assertion failure. To see the assertion go red instead, add the overload first with the buggy `y * map.Height + x` body and run: expect `Assert.Equal(15, 11)`, the eleven distinct keys a 5×3 map produces under the defect. Then correct the body.

**Step 3: Fix**

```csharp
internal static int ItemKey(MapDocument map, int x, int y) => y * map.Width + x;

private int ItemKey(int x, int y) => ItemKey(_map, x, y);
```

The static overload is what the regression test drives, so the width-versus-height choice is test-proven; the one-line instance delegation is compile- and review-proven.

**Step 4: Refresh the smoke doc**

`docs/map-editor-smoke.md` hardcodes expected pass counts that are already stale (it says App 183; the branch point is 261). Update to the final counts from this branch, and add headed-checklist items for: rectangle block and Shift-unblock with the preview colours, Escape cancelling a block drag, and a resize round trip with undo.

**Step 5: Full gate**

```bash
dotnet test Goose2ClientGodot.sln -v minimal
dotnet build Goose2ClientGodot.sln -c Release -v minimal
```

**Step 6: Commit**

```bash
git commit -m "fix: index ground items by map width, not height"
```

---

## Invariant-to-test matrix

| Invariant | Proved by |
|---|---|
| Command byte accounting unchanged by the refactor | Existing `MapEditHistoryTests` pass at 201 unchanged (Task 1) |
| Stroke buffer reaches the command without copying | `MapEditStrokeStorageTests.cs:152` `Assert.Same` preserved (Task 1) |
| Rejected resize leaves the document byte-identical | `ResizeTo_OversizedResult_ThrowsAndLeavesDocumentByteIdentical` |
| `IsEmpty` counts flags, so crop-undo keeps blocked tiles | `IsEmpty_IsFalseForABlockedTileWithNoGraphics` + `ApplyResize_CropThenUndo_RestoresDiscardedTilesExactly` |
| Growing stores no snapshots | `ApplyResize_GrowThenUndo_AllocatesNoSnapshotBuffer` |
| Undo is correct across a resize boundary | `PaintResizePaint_UndoesBackThroughTheResizeInOrder` (adversarial) |
| Unknown flag bits survive blocking | `ApplyBlockedPatch_PreservesUnknownFlagBits` (adversarial) |
| Blocked tool can no longer begin a stroke | `BeginStroke_WithBlockedTool_Throws` |
| Block preview clips to map bounds | `Render_BlockPreview_FillsEachCellClippedToTheMap` |
| Escape cancels a block drag with nothing applied | `BlockedDragEscape_AppliesNothing` (adversarial) |
| One undo entry per block rectangle | `BlockedDrag_BlocksTheRectangleAsOneUndoEntry` |
| Cropped-away selection is cleared, not clamped, and never half-cleared | `ResizeMap_SelectionCroppedAway_IsClearedNotClamped` (adversarial) |
| Cached view-model dimensions follow a resize | `ResizeMap_ShiftsSelectionAndRefreshesCachedDimensions` |
| Undo and redo of a resize sync the same UI state | `UndoOfAResize_RestoresDimensionsAndUnshiftsTheSelection`, `RedoOfAResize_ReappliesDimensionsAndShift` (adversarial) |
| Every resize direction reports its transform | `Undo_OfAResize_RaisesResizedWithTheInverseOffset` |
| No handler leaks onto a replaced session | `DocumentReplacement_UnsubscribesTheOldSessionResizeHandler` |
| Crop survives undo *and* redo | `ApplyResize_CropUndoRedo_RestoresThenRecropsDimensionsAndContent` |
| Empty border crop stores nothing | `ApplyResize_EmptyBorderCrop_AllocatesNoSnapshotBuffer` |
| Rejected resize leaves document, history, state id and dirty flag intact | `ApplyResize_Rejected_LeavesDocumentHistoryStateIdAndDirtyUnchanged`, `ResizeTo_OversizedResult_ThrowsAndLeavesDocumentByteIdentical` |
| Out-of-bounds blocked patch throws and records nothing | `ApplyBlockedPatch_RectOutsideTheMap_Throws` |
| Both preview colours are correct | `Render_BlockPreview_UsesRedToBlockAndGreenToClear` |
| Capture loss cancels; `FinishInteraction(true)` commits | `BlockedDragCaptureLost_AppliesNothing`, `FinishInteractionCommit_AppliesTheBlockRectangle` (adversarial pair) |
| Shift is sampled at press | `BlockedDragShiftReleasedMidDrag_StillClears` (adversarial) |
| Preview vanishes on commit and cancel | `BlockPreview_VanishesAfterCommitAndAfterCancel` |
| Discard count visits corner cells once | headless dialog test: discard count with a corner overlap |
| Ground-item key is unique when width exceeds height | `ItemKey_IsUniquePerTileWhenWidthExceedsHeight` (regression) |
| `ResizeTo` stays unreachable from the Godot client | Compile-time: `internal` + `AssemblyInfo.cs:3` names only `MapEditor.Core.Tests` |
| Large lossy crops may evict history | Not tested — accepted limitation per design §2 |

## Notes for the implementer

- **Threading:** every path here runs on the Avalonia UI thread. `MapEditSession` has no synchronization and needs none; do not introduce background work.
- **Persistence:** no schema or format change. `MapCodec.Encode` reads dimensions from the document at write time, so resized maps serialize correctly with no migration. Saved files from before this change load unchanged.
- **Failure behaviour:** `ApplyResize` and `ApplyBlockedPatch` validate before mutating, so a rejected call leaves the document, history and dirty state untouched. A cancelled dialog does nothing at all.
- **Do not** make `MapDocument.ResizeTo` public — the Godot client references this assembly (`Goose2ClientGodot.csproj:17`) and holds a live document.
- **Do not** weaken `MapEditStrokeStorageTests.cs:152` to make the Task 1 refactor easier; it is the zero-copy proof.

**Expected suite totals on completion:** Core 216, Rendering 171, App 281, Godot 459 + 1 = 460. Task 8's smoke-doc refresh must record these, replacing the stale figures in `docs/map-editor-smoke.md`.

**Bounds and overflow.** `-west`, `width + west + east` and the inverse `-window.X` are all `int` arithmetic on user-supplied values, and a valid-looking result can come from cancelling extreme inputs (`west = int.MaxValue`, `east = int.MinValue + 1`). Resulting dimensions alone are therefore not sufficient validation. Two rules:

- The dialog rejects any offset outside `[-MapDocument.MaxDimension, MapDocument.MaxDimension]` at parse time, before arithmetic. That bounds every intermediate to ±2000 and makes overflow unreachable from the UI.
- `MapDocument.ResizeTo` and `MapEditSession.ApplyResize` independently reject an origin outside that same range, so a programmatic caller cannot pass `int.MinValue` — whose negation is not representable and whose inverse window could not be formed. Compute in `long` inside the validation, as `MapTileRectangle.ClipTo:7` already does.

**Why `ResizeSlotBytes = 56` when a snapshot is 52 bytes.** `MapTileSnapshot` holds `MapTile` (a 4-byte `int` plus five 8-byte `MapTileLayer` values = 44) plus two coordinate `int`s = 52. The constant is deliberately rounded up to 56 for object-layout headroom, matching the existing constants' conservative style. It is an accounting figure, not a measurement — do not "correct" it to 52 without also re-checking `EvictToCap` behaviour.
