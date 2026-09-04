# Map Editor — Part 2: Select, Multi-select (copy/paste), Flood Fill

**Goal:** Add three tools — Select (single-tile inspect), Multi-select (rectangle copy/paste across all selected layers with a ghost preview), and Flood fill (bounded 4-directional fill) — wired through the core session, rendering overlays, and the window/canvas.

**Architecture:** `MapEditTool` gains `Select`, `MultiSelect`, `FloodFill`. The core session gains two click-only, stroke-free entry points: `ApplyFloodFill(x, y)` (BFS, one undoable command) and `ApplyLayerPatch(...)` (multi-layer rectangle write, one undoable command, no-op-safe). Copy/paste state (clipboard, paste mode, selection rectangle) lives in the App layer; the canvas drives the interactions; the renderer draws the selection-rectangle outline and paste ghost via new optional `MapRenderOptions` fields.

**Tech Stack:** C# (.NET 8 core / .NET 10 app), Avalonia 11.3.20, xunit + Avalonia.Headless.

**Part:** 2 of 2. Builds on Part 1 (`docs/plans/2026-09-03-map-editor-selection-ui.md`): `MapEditSession.SelectedLayers`/`TopLayer`, `MainWindowViewModel.SelectedLayers`, layer rows, tool-only toolbar.

**Repo rules:** Per `AGENTS.md`, add no comments or doc strings to new/modified code. Leave unrelated existing comments untouched.

**APIs verified:**
- `MapEditSession.BeginStroke` `src/MapEditor.Core/Editing/MapEditSession.cs:80` (signature `BeginStroke(MapEditTool tool, int x, int y)` — no layer parameter; the stroke is constructed with `_activeLayer` at `:89`); `CompleteStroke` `:113` (state-id bookkeeping `:124-129`); `ValidateTool` `:266` (`private static`)
- `MapEditCommand.ForLayerChanges` — `src/MapEditor.Core/Editing/MapEditCommand.cs:17`
- `MapEditChangeBuffer.Append` — `src/MapEditor.Core/Editing/MapEditChangeBuffer.cs:46`; `Count` `:21`
- `MapLayerChange` record — `src/MapEditor.Core/Editing/MapEditChange.cs:3`
- `StrokeVisitBitmap.TryMark` — `src/MapEditor.Core/Editing/StrokeVisitBitmap.cs:19`
- `MapDocument.Width/Height/TileCount/SetLayer` — `src/MapEditor.Core/MapDocument.cs:83,85,87,142`; `MapTile.GetLayer` `:32`
- `MapEditSessionTests` has no `CreateSession` helper at the branch point — tests build `new MapEditSession(MapDocument.Create(w, h))` inline (e.g. `:13, :45`); Part 1 adds `CreateSession(int width = 4, int height = 4)`, which Part 2 reuses
- `MapTileCoordinate` — `src/MapEditor.Rendering/Geometry/RenderGeometry.cs:13`; `RenderColor(byte R, byte G, byte B, byte A)` — `:11` (**RGBA order — alpha is the last argument**)
- `MapRenderOptions` — `src/MapEditor.Rendering/Composition/MapRenderOptions.cs:5` (positional record; `Default` at `:11`)
- `CellOverlayKind` — `src/MapEditor.Rendering/Composition/MapDrawOperations.cs:10`; `GridLineDrawOperation(Start, End, Color)` `:43`
- `IMapDrawSink` — `src/MapEditor.Rendering/Composition/IMapDrawSink.cs:3`
- `MapRenderer` overlay section — `src/MapEditor.Rendering/Composition/MapRenderer.cs:154-162`; `DrawTarget` `:212`; `MapRenderPalette` `:240` (internal — see Task 2 prerequisite)
- `RecordingMapDrawSink` — `tests/MapEditor.Rendering.Tests/Fakes/RecordingMapDrawSink.cs`: public `Calls` (`IReadOnlyList<object>`) and `CallCount`
- `InternalsVisibleTo` pattern: `src/MapEditor.Core/Properties/AssemblyInfo.cs:3`, `src/MapEditor.App/Properties/AssemblyInfo.cs:3` — `MapEditor.Rendering` has none yet
- `AvaloniaMapDrawSink.DrawCellOverlay` honors alpha fill (`Color.FromArgb`) — `src/MapEditor.App/Rendering/AvaloniaMapDrawSink.cs:37`
- `MapCanvas`: `IsGestureActive` `src/MapEditor.App/Controls/MapCanvas.cs:100`; `OnPointerPressed` `:102` (pointer capture at `:131`); `OnPointerCaptureLost` `:229`; `OnKeyDown` `:268`; `FinishInteraction` `:42`; `BuildRenderRequest` `:344`; `OnCanvasInvalidated` `:328`; `UpdateHover` `:189`
- `MainWindow`: `OnKeyDown` `src/MapEditor.App/Views/MainWindow.axaml.cs:~90` (`case Key.N` at `:100`); `case Key.B` `:158`; `OnToolChecked` `:217`; `SyncToolButtons` `:424`
- `MainWindowViewModel.ActiveTool` range check — `src/MapEditor.App/ViewModels/MainWindowViewModel.cs:72`
- `MapCanvasTests` harness: `MapSize = 4` `tests/MapEditor.App.Tests/MapCanvasTests.cs:30`, `Cell = 32` `:31` (**the small map is 4×4, not 5×5**)
- `ShortcutTests.cs:126-127` asserts `PhysicalKey.B` → `BlockedToggle`; codebase convention is `KeyPressQwerty(PhysicalKey, RawInputModifiers)`
- `MainWindowTests.Layout_ContainsFourToolTogglesWithPencilActive` — `tests/MapEditor.App.Tests/MainWindowTests.cs:176` (the Pencil-checked assert lives here)

---

### Task 1: Core — new tools, `MapTileRectangle`, `ApplyFloodFill`, `ApplyLayerPatch`

**Files:**
- Modify: `src/MapEditor.Core/Editing/MapEditTool.cs`
- Create: `src/MapEditor.Core/MapTileRectangle.cs`
- Modify: `src/MapEditor.Core/Editing/MapEditSession.cs`
- Test: `tests/MapEditor.Core.Tests/MapEditSessionTests.cs`

**Mutation impact:**
- Source of truth changed: none existing — additive API on `MapEditSession` and a new public type; `MapEditTool` enum extended (existing members keep their values, so nothing persisted or switch-based breaks; the ViewModel range check at `MainWindowViewModel.cs:72` must widen to the new max — `ValidateTool` at `MapEditSession.cs:266` must NOT widen, see below)
- Important readers: `MapEditStroke.ApplySegment` switch on `MapEditTool` (`src/MapEditor.Core/Editing/MapEditStroke.cs:71-79`) — the new tools never reach it (canvas routes them to the new entry points); C# switches on enums are not required to be exhaustive, so no change needed
- Derived/cached state affected: undo history (new commands via the existing `MapEditHistory`), dirty tracking (`IsDirty` via `_currentStateId`) — both work unchanged because the new entry points use the same state-id bookkeeping as `CompleteStroke`
- Required propagation sequence: each new entry point appends to a `MapEditChangeBuffer<MapLayerChange>`, mutates the document via `MapDocument.SetLayer`, then pushes one `MapEditCommand.ForLayerChanges` with fresh before/after state ids
- Invariants to preserve:
  - no active stroke may exist when a new entry point is called (throw `InvalidOperationException`, same as `Undo`/`Redo` at `MapEditSession.cs:157,176`)
  - no-op operations (flood fill target == start value; patch with zero differing cells) return `false` and push no history entry
  - flood fill is 4-directional and stops at any cell whose layer value differs from the start cell's value
  - flood fill and patch apply to `TopLayer` / the explicitly passed layers only
- Observable proof required: adversarial diagonal-leak test (4-directional fill leaks past a diagonally placed blocker) and a no-op test asserting `CanUndo` stays false.

**Step 1: Write the failing tests**

In `tests/MapEditor.Core.Tests/MapEditSessionTests.cs` (reuse the `CreateSession(int width = 4, int height = 4)` helper added in Part 1 — do not add a second one; the signatures are identical and a duplicate would not compile):

```csharp
[Fact]
public void FloodFill_EmptyRegion_FillsWholeMapAsOneUndoableCommand()
{
    var session = CreateSession(5, 5);
    session.SelectedTileLayer = new MapTileLayer(3, 3);
    Assert.True(session.ApplyFloodFill(2, 2));
    for (int y = 0; y < 5; y++)
        for (int x = 0; x < 5; x++)
            Assert.Equal(new MapTileLayer(3, 3), session.Document[x, y].GetLayer(0));
    Assert.True(session.CanUndo);
    Assert.True(session.Undo());
    for (int y = 0; y < 5; y++)
        for (int x = 0; x < 5; x++)
            Assert.Equal(new MapTileLayer(0, 0), session.Document[x, y].GetLayer(0));
    Assert.True(session.CanRedo);
    Assert.True(session.Redo());
    for (int y = 0; y < 5; y++)
        for (int x = 0; x < 5; x++)
            Assert.Equal(new MapTileLayer(3, 3), session.Document[x, y].GetLayer(0));
}

[Fact]
public void FloodFill_SquareBoundary_FillsOnlyInterior()
{
    var session = CreateSession(5, 5);
    for (int i = 1; i <= 3; i++)
    {
        session.Document.SetLayer(i, 1, 0, new MapTileLayer(1, 1));
        session.Document.SetLayer(i, 3, 0, new MapTileLayer(1, 1));
        session.Document.SetLayer(1, i, 0, new MapTileLayer(1, 1));
        session.Document.SetLayer(3, i, 0, new MapTileLayer(1, 1));
    }

    session.SelectedTileLayer = new MapTileLayer(9, 9);
    Assert.True(session.ApplyFloodFill(2, 2));

    Assert.Equal(new MapTileLayer(9, 9), session.Document[2, 2].GetLayer(0));
    Assert.Equal(new MapTileLayer(1, 1), session.Document[1, 1].GetLayer(0));
    Assert.Equal(new MapTileLayer(0, 0), session.Document[0, 0].GetLayer(0));
}

[Fact]
public void FloodFill_DiagonalOnlyConnection_DoesNotFill()
{
    var session = CreateSession(4, 4);
    // both in-map orthogonal neighbours of (0,0) are blockers; (1,1) and (2,2)
    // match the start value and are reachable only diagonally
    session.Document.SetLayer(1, 0, 0, new MapTileLayer(1, 1));
    session.Document.SetLayer(0, 1, 0, new MapTileLayer(1, 1));

    session.SelectedTileLayer = new MapTileLayer(9, 9);
    Assert.True(session.ApplyFloodFill(0, 0));

    Assert.Equal(new MapTileLayer(9, 9), session.Document[0, 0].GetLayer(0));
    // an 8-directional fill would reach (1,1) and then (2,2) through the diagonal
    Assert.Equal(new MapTileLayer(0, 0), session.Document[1, 1].GetLayer(0));
    Assert.Equal(new MapTileLayer(0, 0), session.Document[2, 2].GetLayer(0));
}

[Fact]
public void FloodFill_DiagonalBlocker_DoesNotSealRegion()
{
    var session = CreateSession(4, 4);
    session.Document.SetLayer(1, 1, 0, new MapTileLayer(1, 1));

    session.SelectedTileLayer = new MapTileLayer(3, 3);
    Assert.True(session.ApplyFloodFill(0, 0));

    // the blocker touches the start cell only diagonally, so the fill flows around it
    int filled = 0;
    for (int y = 0; y < 4; y++)
    {
        for (int x = 0; x < 4; x++)
        {
            if (session.Document[x, y].GetLayer(0) == new MapTileLayer(3, 3))
            {
                filled++;
            }
        }
    }

    Assert.Equal(15, filled);
}

[Fact]
public void FloodFill_NonEmptyStartRegion_RefillsWithBrush()
{
    var session = CreateSession(4, 4);
    for (int x = 0; x < 4; x++)
    {
        for (int y = 0; y < 4; y++)
        {
            session.Document.SetLayer(x, y, 0, new MapTileLayer(3, 3));
        }
    }

    session.SelectedTileLayer = new MapTileLayer(9, 9);
    Assert.True(session.ApplyFloodFill(0, 0));

    Assert.Equal(new MapTileLayer(9, 9), session.Document[0, 0].GetLayer(0));
    Assert.Equal(new MapTileLayer(9, 9), session.Document[3, 3].GetLayer(0));
}

[Fact]
public void FloodFill_TargetEqualsStartValue_ReturnsFalseWithoutHistory()
{
    var session = CreateSession(5, 5);
    session.SelectedTileLayer = new MapTileLayer(0, 0);
    Assert.False(session.ApplyFloodFill(0, 0));
    Assert.False(session.CanUndo);
}

[Fact]
public void FloodFill_WithMultiLayerSelection_FillsOnlyTopmostLayer()
{
    var session = CreateSession(5, 5);
    session.SelectedLayers = 0b01001;
    session.SelectedTileLayer = new MapTileLayer(3, 3);
    Assert.True(session.ApplyFloodFill(0, 0));
    Assert.Equal(new MapTileLayer(3, 3), session.Document[0, 0].GetLayer(3));
    Assert.Equal(new MapTileLayer(0, 0), session.Document[0, 0].GetLayer(0));
}

[Fact]
public void LayerPatch_WritesCorrespondingLayersAsOneUndoableCommand()
{
    var session = CreateSession(5, 5);
    MapTileLayer[]?[] patch = new MapTileLayer[MapDocument.LayerCount][];
    patch[1] = new MapTileLayer[] { new(5, 5), new(6, 6) };
    patch[3] = new MapTileLayer[] { new(7, 7), new(8, 8) };

    Assert.True(session.ApplyLayerPatch(1, 1, 2, 1, patch));
    Assert.Equal(new MapTileLayer(5, 5), session.Document[1, 1].GetLayer(1));
    Assert.Equal(new MapTileLayer(6, 6), session.Document[2, 1].GetLayer(1));
    Assert.Equal(new MapTileLayer(7, 7), session.Document[1, 1].GetLayer(3));
    Assert.Equal(new MapTileLayer(0, 0), session.Document[1, 1].GetLayer(0));
    Assert.Equal(new MapTileLayer(0, 0), session.Document[1, 1].GetLayer(2));

    Assert.True(session.Undo());
    Assert.Equal(new MapTileLayer(0, 0), session.Document[1, 1].GetLayer(1));
    Assert.Equal(new MapTileLayer(0, 0), session.Document[1, 1].GetLayer(3));
    Assert.True(session.Redo());
    Assert.Equal(new MapTileLayer(5, 5), session.Document[1, 1].GetLayer(1));
}

[Fact]
public void LayerPatch_NoDifferingCells_ReturnsFalseWithoutHistory()
{
    var session = CreateSession(5, 5);
    MapTileLayer[]?[] patch = new MapTileLayer[MapDocument.LayerCount][];
    patch[0] = new MapTileLayer[] { new(0, 0) };
    Assert.False(session.ApplyLayerPatch(0, 0, 1, 1, patch));
    Assert.False(session.CanUndo);
}

[Theory]
[InlineData(4, 0, 2, 2)]
[InlineData(0, 4, 2, 2)]
[InlineData(0, 0, 0, 2)]
public void LayerPatch_OutOfBounds_ThrowsWithoutMutation(int originX, int originY, int width, int height)
{
    var session = CreateSession(5, 5);
    MapTileLayer[]?[] patch = new MapTileLayer[MapDocument.LayerCount][];
    patch[0] = new MapTileLayer[width * height];
    Assert.Throws<ArgumentOutOfRangeException>(() => session.ApplyLayerPatch(originX, originY, width, height, patch));
    Assert.False(session.IsDirty);
}

[Fact]
public void LayerPatch_WrongInnerArrayLength_ThrowsWithoutMutation()
{
    var session = CreateSession(5, 5);
    session.Document.SetLayer(0, 0, 0, new MapTileLayer(1, 1));
    MapTileLayer[]?[] patch = new MapTileLayer[MapDocument.LayerCount][];
    patch[0] = new MapTileLayer[3];
    Assert.Throws<ArgumentException>(() => session.ApplyLayerPatch(0, 0, 2, 2, patch));
    Assert.Equal(new MapTileLayer(1, 1), session.Document[0, 0].GetLayer(0));
    Assert.False(session.CanUndo);
}

[Theory]
[InlineData(MapEditTool.Select)]
[InlineData(MapEditTool.MultiSelect)]
[InlineData(MapEditTool.FloodFill)]
public void BeginStroke_NonStrokingTool_Throws(MapEditTool tool)
{
    var session = CreateSession();
    Assert.Throws<ArgumentOutOfRangeException>(() => session.BeginStroke(tool, 0, 0));
}

[Fact]
public void FloodFill_AndLayerPatch_WithActiveStroke_Throw()
{
    var session = CreateSession(5, 5);
    session.BeginStroke(MapEditTool.Pencil, 0, 0);
    Assert.Throws<InvalidOperationException>(() => session.ApplyFloodFill(1, 1));
    MapTileLayer[]?[] patch = new MapTileLayer[MapDocument.LayerCount][];
    patch[0] = new MapTileLayer[] { new(1, 1) };
    Assert.Throws<InvalidOperationException>(() => session.ApplyLayerPatch(0, 0, 1, 1, patch));
}
```

`CreateSession(w, h)` is the helper added above (the file currently builds sessions inline).

**Step 2: Run tests to verify they fail (red)**

Run: `dotnet test tests/MapEditor.Core.Tests -v q --nologo`
Expected: compile failure — `ApplyFloodFill`/`ApplyLayerPatch`/`MapTileRectangle` do not exist.

**Step 3: Implement**

`src/MapEditor.Core/Editing/MapEditTool.cs`:

```csharp
namespace MapEditor.Core;

public enum MapEditTool
{
    Pencil,
    Eraser,
    Eyedropper,
    BlockedToggle,
    Select,
    MultiSelect,
    FloodFill
}
```

`src/MapEditor.Core/MapTileRectangle.cs` (new):

```csharp
using System;

namespace MapEditor.Core;

public readonly record struct MapTileRectangle(int X, int Y, int Width, int Height)
{
    public MapTileRectangle? ClipTo(int boundsWidth, int boundsHeight)
    {
        long left = Math.Max(0, X);
        long right = Math.Min((long)boundsWidth, X + (long)Width);
        long top = Math.Max(0, Y);
        long bottom = Math.Min((long)boundsHeight, Y + (long)Height);
        int width = (int)(right - left);
        int height = (int)(bottom - top);
        return width > 0 && height > 0 ? new MapTileRectangle((int)left, (int)top, width, height) : null;
    }
}
```

`ClipTo` is the single shared clip-to-bounds operation — the renderer overlays, the VM paste application, and the canvas drag clamp all use it (or its inverse, clamping a point), so edge/overflow behavior is defined and tested in one place. Add core tests:

```csharp
[Theory]
[InlineData(0, 0, 3, 3, 5, 5, 0, 0, 3, 3)]
[InlineData(3, 3, 4, 4, 5, 5, 3, 3, 2, 2)]
[InlineData(-2, -2, 3, 3, 5, 5, 0, 0, 1, 1)]
public void ClipTo_ClipsToBounds(int x, int y, int w, int h, int bw, int bh, int ex, int ey, int ew, int eh)
{
    var clipped = new MapTileRectangle(x, y, w, h).ClipTo(bw, bh);
    Assert.Equal(new MapTileRectangle(ex, ey, ew, eh), clipped);
}

[Theory]
[InlineData(5, 0, 2, 2, 5, 5)]
[InlineData(0, 5, 2, 2, 5, 5)]
[InlineData(-7, -7, 2, 2, 5, 5)]
[InlineData(0, 0, 0, 3, 5, 5)]
public void ClipTo_NoOverlapOrEmpty_ReturnsNull(int x, int y, int w, int h, int bw, int bh)
{
    Assert.Null(new MapTileRectangle(x, y, w, h).ClipTo(bw, bh));
}
```

`MapEditSession`:
- Do NOT widen `ValidateTool` (`:266`, `private static`) — it guards `BeginStroke`, which constructs a `MapEditStroke` for whatever passes; letting `Select`/`MultiSelect`/`FloodFill` through would leave an active no-op stroke (the stroke's `ApplySegment` switch silently ignores them). Keep it at `tool > MapEditTool.BlockedToggle → throw`. Only the ViewModel's `ActiveTool` range check (`MainWindowViewModel.cs:72`) widens to `MapEditTool.FloodFill`. Add a test: `BeginStroke(MapEditTool.Select, 0, 0)` throws `ArgumentOutOfRangeException`.
- Add a private helper extracted from `CompleteStroke`'s bookkeeping (`:124-131`):

```csharp
private void PushLayerCommand(MapEditChangeBuffer<MapLayerChange> changes)
{
    int beforeStateId = _currentStateId;
    int afterStateId = _nextStateId++;
    _currentStateId = afterStateId;
    _history.PushUndo(MapEditCommand.ForLayerChanges(changes, beforeStateId, afterStateId));
}
```

and have `CompleteStroke` call it for the layer-changes branch (keep the flags branch as-is).
- Add:

```csharp
public bool ApplyFloodFill(int x, int y)
{
    ValidateCoordinate(x, y);
    if (_stroke != null)
    {
        throw new InvalidOperationException();
    }

    int layer = TopLayer;
    MapTileLayer target = _selectedTileLayer;
    MapTileLayer startValue = _document[x, y].GetLayer(layer);
    if (startValue == target)
    {
        return false;
    }

    int width = _document.Width;
    int height = _document.Height;
    var visited = new StrokeVisitBitmap(_document.TileCount);
    var changes = new MapEditChangeBuffer<MapLayerChange>();
    var frontier = new Queue<int>();
    int[] neighborOffsets = { -1, 1, -width, width };
    int start = y * width + x;
    visited.TryMark(start);
    frontier.Enqueue(start);
```

(`MapEditSession.cs` currently imports only `System` and the Core project has no implicit usings — add `using System.Collections.Generic;` for `Queue<int>`.)
    while (frontier.Count > 0)
    {
        int index = frontier.Dequeue();
        int cx = index % width;
        int cy = index / width;
        changes.Append(new MapLayerChange(cx, cy, layer, startValue, target));
        _document.SetLayer(cx, cy, layer, target);

        foreach (int offset in neighborOffsets)
        {
            int ni = index + offset;
            if (ni < 0 || ni >= _document.TileCount)
            {
                continue;
            }

            int nx = ni % width;
            if (offset == -1 && cx == 0) continue;
            if (offset == 1 && cx == width - 1) continue;
            if (!visited.TryMark(ni))
            {
                continue;
            }

            if (_document[nx, ni / width].GetLayer(layer) != startValue)
            {
                continue;
            }

            frontier.Enqueue(ni);
        }
    }

    PushLayerCommand(changes);
    return true;
}
```

(Keep the row-wrap guards: `-1`/`+1` offsets must not cross rows — the `cx == 0` / `cx == width - 1` checks above handle that; `-width`/`+width` are always in-bounds when `ni` is.)

```csharp
public bool ApplyLayerPatch(int originX, int originY, int width, int height, MapTileLayer[]?[] layerTiles)
{
    if (_stroke != null)
    {
        throw new InvalidOperationException();
    }

    if (layerTiles is null || layerTiles.Length != MapDocument.LayerCount)
    {
        throw new ArgumentException(nameof(layerTiles));
    }

    if (width <= 0 || height <= 0 || originX < 0 || originY < 0 ||
        width > _document.Width - originX || height > _document.Height - originY)
    {
        throw new ArgumentOutOfRangeException();
    }

    for (int layer = 0; layer < MapDocument.LayerCount; layer++)
    {
        if (layerTiles[layer] is { } tiles && tiles.Length != width * height)
        {
            throw new ArgumentException(nameof(layerTiles));
        }
    }

    var changes = new MapEditChangeBuffer<MapLayerChange>();
    for (int layer = 0; layer < MapDocument.LayerCount; layer++)
    {
        if (layerTiles[layer] is not { } tiles)
        {
            continue;
        }

        for (int row = 0; row < height; row++)
        {
            for (int col = 0; col < width; col++)
            {
                int x = originX + col;
                int y = originY + row;
                MapTileLayer target = tiles[row * width + col];
                MapTileLayer current = _document[x, y].GetLayer(layer);
                if (current != target)
                {
                    changes.Append(new MapLayerChange(x, y, layer, current, target));
                    _document.SetLayer(x, y, layer, target);
                }
            }
        }
    }

    if (changes.Count == 0)
    {
        return false;
    }

    PushLayerCommand(changes);
    return true;
}
```

**Step 4: Run tests to verify they pass (green)**

Run: `dotnet test tests/MapEditor.Core.Tests -v q --nologo`
Expected: all pass.

**Step 5: Commit**

```bash
git add src/MapEditor.Core tests/MapEditor.Core.Tests
git commit -m "feat: flood fill and layer patch entry points in MapEditSession"
```

**Invariant-to-test matrix:**

| Invariant | Proved by |
|-----------|-----------|
| Empty-region fill covers the map, one undo restores (happy path) | `FloodFill_EmptyRegion_FillsWholeMapAsOneUndoableCommand` |
| Fill bounded by a square of other tiles (adversarial) | `FloodFill_SquareBoundary_FillsOnlyInterior` |
| Diagonal-only connection does not fill; diagonal blocker does not seal (adversarial) | `FloodFill_DiagonalOnlyConnection_DoesNotFill`, `FloodFill_DiagonalBlocker_DoesNotSealRegion` |
| No-op fill pushes no history (adversarial) | `FloodFill_TargetEqualsStartValue_ReturnsFalseWithoutHistory` |
| Fill targets topmost of a multi-selection | `FloodFill_WithMultiLayerSelection_FillsOnlyTopmostLayer` |
| Patch writes corresponding layers only, one undo/redo round-trips | `LayerPatch_WritesCorrespondingLayersAsOneUndoableCommand` |
| No-op patch pushes no history (adversarial) | `LayerPatch_NoDifferingCells_ReturnsFalseWithoutHistory` |
| Out-of-bounds patch throws, document untouched (adversarial) | `LayerPatch_OutOfBounds_ThrowsWithoutMutation` |
| Wrong inner-array length throws before any mutation (adversarial) | `LayerPatch_WrongInnerArrayLength_ThrowsWithoutMutation` |
| Non-stroking tools rejected by `BeginStroke` (adversarial) | `BeginStroke_NonStrokingTool_Throws` |
| Rectangle clip-to-bounds: partial clip, negative origin, no-overlap null (adversarial) | `ClipTo_ClipsToBounds`, `ClipTo_NoOverlapOrEmpty_ReturnsNull` |
| Non-empty start region refills with the brush | `FloodFill_NonEmptyStartRegion_RefillsWithBrush` |
| Stroke exclusivity | `FloodFill_AndLayerPatch_WithActiveStroke_Throw` |

---

### Task 2: Rendering — selection rectangle + paste ghost overlays

**Files:**
- Create: `src/MapEditor.Rendering/Properties/AssemblyInfo.cs`
- Modify: `src/MapEditor.Rendering/Composition/MapRenderOptions.cs`
- Modify: `src/MapEditor.Rendering/Composition/MapDrawOperations.cs:10-15`
- Modify: `src/MapEditor.Rendering/Composition/MapRenderer.cs`
- Test: `tests/MapEditor.Rendering.Tests/MapRendererTests.cs`

**Mutation impact:**
- Source of truth changed: `MapRenderOptions` record shape (`src/MapEditor.Rendering/Composition/MapRenderOptions.cs:5`) — two optional positional parameters appended; existing call sites (`MapCanvas.BuildRenderRequest`, `MapRenderOptions.Default`, all renderer tests) keep compiling
- Important readers: `MapRenderer.Render` (`:17`); `MapCanvas.BuildRenderRequest` (`src/MapEditor.App/Controls/MapCanvas.cs:344`, updated in Task 4)
- Derived/cached state affected: none — options are rebuilt per render; `MaximumTileReadsPerRender` accounting (`MapRenderer.cs:40-57`) intentionally excludes overlay work (grid/blocked overlays already draw outside the read budget; the ghost fill adds per-cell overlay draws bounded by the viewport)
- Invariants to preserve:
  - rectangles extending past the document are clipped to `[0, Width) × [0, Height)` before drawing
  - empty/invalid rectangles draw nothing
  - overlays draw after tiles/blocked/grid, with the selection rectangle under the hover/selected outlines is not required — draw both new overlays after `HoveredTile` (topmost) so the paste ghost is never obscured
- Observable proof required: adversarial clip test — a paste ghost straddling the document edge emits fill overlays only for in-bounds cells.

**Step 1: Write the failing tests**

In `tests/MapEditor.Rendering.Tests/MapRendererTests.cs` (follow the file's existing `Request(document, viewport)` / `RecordingMapDrawSink` pattern):

```csharp
[Fact]
public void SelectionRectangle_DrawsFourOutlineLinesWithSelectionStroke()
{
    string manifest = ManifestJson((1, 1, 0, 0, 32, 32));
    MapRenderer renderer = new(CreateCache(new FakeSpriteSheetLoader(), manifest));
    var document = MapDocument.Create(5, 5);
    var options = new MapRenderOptions(MapLayerVisibility.All, false, false, null, null,
        SelectionRectangle: new MapTileRectangle(1, 1, 3, 2));
    var sink = new RecordingMapDrawSink();
    renderer.Render(Request(document, Viewport(5 * 32, 5 * 32), options), sink);

    var lines = sink.Calls.OfType<GridLineDrawOperation>()
        .Where(op => op.Color == MapRenderer.MapRenderPalette.SelectionStroke)
        .ToList();
    Assert.Equal(4, lines.Count);
}

[Fact]
public void PasteGhost_StraddlingEdge_ClipsFillToDocumentBounds()
{
    string manifest = ManifestJson((1, 1, 0, 0, 32, 32));
    MapRenderer renderer = new(CreateCache(new FakeSpriteSheetLoader(), manifest));
    var document = MapDocument.Create(5, 5);
    var options = new MapRenderOptions(MapLayerVisibility.All, false, false, null, null,
        PasteGhost: new MapTileRectangle(3, 3, 4, 4));
    var sink = new RecordingMapDrawSink();
    renderer.Render(Request(document, Viewport(5 * 32, 5 * 32), options), sink);

    var fills = sink.Calls.OfType<CellOverlayDrawOperation>()
        .Where(op => op.Kind == CellOverlayKind.PasteGhost)
        .ToList();
    Assert.Equal(4, fills.Count);
}

[Fact]
public void PasteGhost_FullyInBounds_DrawsFillAndOutline()
{
    string manifest = ManifestJson((1, 1, 0, 0, 32, 32));
    MapRenderer renderer = new(CreateCache(new FakeSpriteSheetLoader(), manifest));
    var document = MapDocument.Create(5, 5);
    var options = new MapRenderOptions(MapLayerVisibility.All, false, false, null, null,
        PasteGhost: new MapTileRectangle(1, 1, 2, 2));
    var sink = new RecordingMapDrawSink();
    renderer.Render(Request(document, Viewport(5 * 32, 5 * 32), options), sink);

    Assert.Equal(4, sink.Calls.OfType<CellOverlayDrawOperation>()
        .Count(op => op.Kind == CellOverlayKind.PasteGhost));
    Assert.Equal(4, sink.Calls.OfType<GridLineDrawOperation>()
        .Count(op => op.Color == MapRenderer.MapRenderPalette.PasteGhostStroke));
}

[Fact]
public void RectangleOverlays_FullyOutOfBounds_DrawNothing()
{
    string manifest = ManifestJson((1, 1, 0, 0, 32, 32));
    MapRenderer renderer = new(CreateCache(new FakeSpriteSheetLoader(), manifest));
    var document = MapDocument.Create(5, 5);
    var options = new MapRenderOptions(MapLayerVisibility.All, false, false, null, null,
        SelectionRectangle: new MapTileRectangle(5, 5, 2, 2),
        PasteGhost: new MapTileRectangle(5, 5, 2, 2));
    var sink = new RecordingMapDrawSink();
    renderer.Render(Request(document, Viewport(5 * 32, 5 * 32), options), sink);

    Assert.Empty(sink.Calls.OfType<GridLineDrawOperation>()
        .Where(op => op.Color == MapRenderer.MapRenderPalette.SelectionStroke));
    Assert.Empty(sink.Calls.OfType<CellOverlayDrawOperation>()
        .Where(op => op.Kind == CellOverlayKind.PasteGhost));
}

[Fact]
public void RectangleOverlays_PartiallyOutOfBounds_ClipsToDocument()
{
    string manifest = ManifestJson((1, 1, 0, 0, 32, 32));
    MapRenderer renderer = new(CreateCache(new FakeSpriteSheetLoader(), manifest));
    var document = MapDocument.Create(5, 5);
    var options = new MapRenderOptions(MapLayerVisibility.All, false, false, null, null,
        SelectionRectangle: new MapTileRectangle(-3, -3, 4, 4),
        PasteGhost: new MapTileRectangle(-3, -3, 4, 4));
    var sink = new RecordingMapDrawSink();
    renderer.Render(Request(document, Viewport(5 * 32, 5 * 32), options), sink);

    // both rectangles clip to the single cell (0,0)
    Assert.Equal(4, sink.Calls.OfType<GridLineDrawOperation>()
        .Count(op => op.Color == MapRenderer.MapRenderPalette.SelectionStroke));
    Assert.Equal(1, sink.Calls.OfType<CellOverlayDrawOperation>()
        .Count(op => op.Kind == CellOverlayKind.PasteGhost));
}
```

Check `RecordingMapDrawSink`'s exposed collection name — it is the public `Calls` (`IReadOnlyList<object>`, `tests/MapEditor.Rendering.Tests/Fakes/RecordingMapDrawSink.cs:13`).

**Step 2: Run tests to verify they fail (red)**

Run: `dotnet test tests/MapEditor.Rendering.Tests -v q --nologo`
Expected: compile failure — `SelectionRectangle`/`PasteGhost` parameters, `CellOverlayKind.PasteGhost`, and `MapRenderPalette` (internal, not visible to the test assembly) missing.

**Step 3: Implement**

`src/MapEditor.Rendering/Properties/AssemblyInfo.cs` (new, mirroring the Core/App pattern):

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("MapEditor.Rendering.Tests")]
```

`MapRenderOptions.cs`:

```csharp
public sealed record MapRenderOptions(
    MapLayerVisibility VisibleLayers,
    bool ShowGrid,
    bool ShowBlocked,
    MapTileCoordinate? HoveredTile,
    MapTileCoordinate? SelectedTile,
    MapTileRectangle? SelectionRectangle = null,
    MapTileRectangle? PasteGhost = null)
```

(`using MapEditor.Core;` already present.)

`MapDrawOperations.cs`: add `PasteGhost` to `CellOverlayKind`.

`MapRenderer.cs`:
- Add palette entries in `MapRenderPalette` (`:240`) — note `RenderColor` is `(R, G, B, A)`, alpha last (`RenderGeometry.cs:11`), and the two outline colors must differ so sink-based tests can distinguish them:

```csharp
public static readonly RenderColor SelectionStroke = new(0xFF, 0xFF, 0xFF, 0xFF);
public static readonly RenderColor PasteGhostFill = new(0xFF, 0xFF, 0xFF, 0x60);
public static readonly RenderColor PasteGhostStroke = new(0xFF, 0xBF, 0xBF, 0xBF);
```

- After the `HoveredTile` block (`:159-162`), add:

```csharp
if (options.SelectionRectangle is { } selection)
{
    DrawRectangleOutline(document, viewport, selection, MapRenderPalette.SelectionStroke, sink);
}

if (options.PasteGhost is { } ghost)
{
    DrawRectangleFill(document, viewport, ghost, MapRenderPalette.PasteGhostFill, sink);
    DrawRectangleOutline(document, viewport, ghost, MapRenderPalette.PasteGhostStroke, sink);
}
```

- Add two private static helpers (model on `DrawGrid`/`DrawTarget`, `:145-162,212`): clip the rectangle with `MapTileRectangle.ClipTo(document.Width, document.Height)`; skip if null. `DrawRectangleOutline` emits four `GridLineDrawOperation`s along the clipped rectangle's edges (world coords via `viewport.WorldToScreen`), skipping edges outside `viewport.VisibleWorldRect` like `DrawGrid` does. `DrawRectangleFill` emits one `CellOverlayDrawOperation(PasteGhost, ...)` per cell in the clipped rectangle ∩ visible cells, fill = the given color, stroke = `MapRenderPalette.Transparent`.

**Step 4: Run tests to verify they pass (green)**

Run: `dotnet test tests/MapEditor.Rendering.Tests -v q --nologo`
Expected: all pass.

**Step 5: Commit**

```bash
git add src/MapEditor.Rendering tests/MapEditor.Rendering.Tests
git commit -m "feat: selection rectangle and paste ghost render overlays"
```

**Invariant-to-test matrix:**

| Invariant | Proved by |
|-----------|-----------|
| Selection rectangle draws exactly 4 outline lines in the selection stroke color | `SelectionRectangle_DrawsFourOutlineLinesWithSelectionStroke` |
| Ghost clipped to document bounds (adversarial) | `PasteGhost_StraddlingEdge_ClipsFillToDocumentBounds` |
| In-bounds ghost draws per-cell fills + outline in distinct colors | `PasteGhost_FullyInBounds_DrawsFillAndOutline` |
| Fully out-of-bounds rectangle draws nothing (adversarial) | `RectangleOverlays_FullyOutOfBounds_DrawNothing` |
| Partially out-of-bounds rectangle clips to the document (adversarial) | `RectangleOverlays_PartiallyOutOfBounds_ClipsToDocument` |
| Negative paste origin clips both source and destination (adversarial) | `ApplyPasteAt_NegativeOrigin_ClipsSourceAndDestination` |
| Existing renders unaffected when new options are null | all pre-existing `MapRendererTests` still green |

---

### Task 3: App — clipboard, paste mode, selection rectangle state

**Files:**
- Create: `src/MapEditor.App/ViewModels/TileClipboard.cs`
- Modify: `src/MapEditor.App/ViewModels/MainWindowViewModel.cs`
- Test: `tests/MapEditor.App.Tests/MainWindowViewModelTests.cs`

**Mutation impact:**
- Source of truth changed: `MainWindowViewModel` gains transient state — `_clipboard` (`TileClipboard?`), `PasteMode` (bool), `SelectionRectangle` (`MapTileRectangle?`) — all view-only, never persisted
- Important readers: `MapCanvas` (Task 4) reads `PasteMode`, `SelectionRectangle`, and the ghost rectangle; `MainWindow` (Task 4) calls `CopySelection`/`BeginPasteMode`
- Derived/cached state affected: `PasteMode` must be cancelled by (a) `SelectedLayers` changes and (b) document replacement, otherwise a stale ghost/paste would apply to the wrong layer set or a replaced document
- Required propagation sequence:
  1. `CopySelection()`: capture only when `SelectionRectangle` is non-null; capture reads `_session.Document` for the layers in `_session.SelectedLayers`
  2. `BeginPasteMode()`: only when a clipboard exists; sets `PasteMode = true`, raises `PropertyChanged` for `PasteMode`
  3. `CancelPasteMode()`: no-op unless `PasteMode`; sets false, raises
  4. `ApplyPasteAt(x, y)`: compute the intersection of the ghost rectangle `(x, y, w, h)` with the document bounds; if empty → `CancelPasteMode()` only; else build clipped per-layer sub-arrays (null for un-captured layers) and call `_session.ApplyLayerPatch(originX, originY, clipW, clipH, subArrays)`; then `CancelPasteMode()` and `Refresh(EditorRefresh.Canvas | EditorRefresh.Commands | EditorRefresh.Title)`
  5. `SelectedLayers` setter and `Refresh(EditorRefresh.Document)` both call `CancelPasteMode()` and clear `SelectionRectangle`/`_clipboard` (document replacement) or just cancel paste mode (selection change)
- Invariants to preserve:
  - paste writes only the layers captured at copy time, each to its corresponding layer
  - a fully out-of-bounds paste click changes nothing and pushes no history
  - paste mode is one-shot: any apply or cancel ends it
- Observable proof required: adversarial edge test — pasting a 3×3 clipboard with origin at (98, 98) on a 100×100 map writes only the 2×2 in-bounds corner and leaves the rest untouched.

**Step 1: Write the failing tests**

In `tests/MapEditor.App.Tests/MainWindowViewModelTests.cs` (the file's existing pattern: `_viewModel`, `_controller`, `RaisedProperties` helper). Add a shared setup helper first — every paste test uses it:

```csharp
private void SeedClipboard(int size = 3, MapTileLayer tile = new(7, 7))
{
    for (int x = 0; x < size; x++)
    {
        for (int y = 0; y < size; y++)
        {
            _viewModel.Session.Document.SetLayer(x, y, 0, tile);
        }
    }

    _viewModel.SelectionRectangle = new MapTileRectangle(0, 0, size, size);
    _viewModel.CopySelection();
}
```

```csharp
[Fact]
public void CopySelection_CapturesOnlySelectedLayers()
{
    // the stroke captures the brush at BeginStroke time, so set the brush first
    _viewModel.SelectedLayers = 0b01001; // layers 0 and 3
    _viewModel.Brush = new MapTileLayer(5, 5);
    _viewModel.Session.BeginStroke(MapEditTool.Pencil, 0, 0);
    _viewModel.Session.ContinueStroke(1, 0);
    _viewModel.Session.CompleteStroke();
    _viewModel.SelectionRectangle = new MapTileRectangle(0, 0, 2, 1);
    _viewModel.CopySelection();

    var clip = _viewModel.Clipboard!;
    Assert.Equal(2, clip.Width);
    Assert.Equal(1, clip.Height);
    Assert.NotNull(clip.Layers[0]);
    Assert.Null(clip.Layers[1]);
    Assert.Equal(new MapTileLayer(5, 5), clip.Layers[3]![0]);
}

[Fact]
public void ApplyPasteAt_WritesEachCapturedLayerToItsCorrespondingLayer()
{
    _viewModel.SelectedLayers = 0b01001;
    for (int x = 0; x < 3; x++)
    {
        _viewModel.Session.Document.SetLayer(x, 0, 0, new MapTileLayer(5, 5));
        _viewModel.Session.Document.SetLayer(x, 0, 3, new MapTileLayer(6, 6));
    }

    _viewModel.SelectionRectangle = new MapTileRectangle(0, 0, 3, 1);
    _viewModel.CopySelection();
    _viewModel.BeginPasteMode();
    _viewModel.ApplyPasteAt(5, 2);

    for (int x = 5; x < 8; x++)
    {
        Assert.Equal(new MapTileLayer(5, 5), _viewModel.Session.Document[x, 2].GetLayer(0));
        Assert.Equal(new MapTileLayer(6, 6), _viewModel.Session.Document[x, 2].GetLayer(3));
        Assert.Equal(new MapTileLayer(0, 0), _viewModel.Session.Document[x, 2].GetLayer(1));
        Assert.Equal(new MapTileLayer(0, 0), _viewModel.Session.Document[x, 2].GetLayer(2));
        Assert.Equal(new MapTileLayer(0, 0), _viewModel.Session.Document[x, 2].GetLayer(4));
    }

    Assert.False(_viewModel.PasteMode);
    Assert.True(_viewModel.CanUndo);
}

[Fact]
public void ApplyPasteAt_EdgeOrigin_ClipsToDocumentBounds()
{
    SeedClipboard();
    _viewModel.BeginPasteMode();
    Assert.True(_viewModel.PasteMode);
    _viewModel.ApplyPasteAt(98, 98);

    Assert.Equal(new MapTileLayer(7, 7), _viewModel.Session.Document[98, 98].GetLayer(0));
    Assert.Equal(new MapTileLayer(7, 7), _viewModel.Session.Document[99, 99].GetLayer(0));
    Assert.Equal(new MapTileLayer(0, 0), _viewModel.Session.Document[97, 97].GetLayer(0));
    Assert.False(_viewModel.PasteMode);
    Assert.True(_viewModel.CanUndo);
    Assert.True(_viewModel.Undo());
}

[Fact]
public void ApplyPasteAt_NegativeOrigin_ClipsSourceAndDestination()
{
    // distinct values per source cell so the assertion proves which source cell landed
    for (int y = 0; y < 3; y++)
    {
        for (int x = 0; x < 3; x++)
        {
            _viewModel.Session.Document.SetLayer(x, y, 0, new MapTileLayer(x + 1, y + 1));
        }
    }

    _viewModel.SelectedLayers = 0b00001;
    _viewModel.SelectionRectangle = new MapTileRectangle(0, 0, 3, 3);
    _viewModel.CopySelection();
    _viewModel.BeginPasteMode();
    _viewModel.ApplyPasteAt(-2, -2);

    Assert.Equal(new MapTileLayer(3, 3), _viewModel.Session.Document[0, 0].GetLayer(0));
    Assert.False(_viewModel.PasteMode);
    Assert.True(_viewModel.CanUndo);
}

[Fact]
public void ApplyPasteAt_FullyOutOfBounds_ChangesNothing()
{
    // 100x100 default document, 3x3 clipboard; origin at x == Width puts the
    // whole ghost past the right edge (clip width computes to 0)
    SeedClipboard();
    _viewModel.BeginPasteMode();
    _viewModel.ApplyPasteAt(100, 0);
    Assert.False(_viewModel.PasteMode);
    Assert.False(_viewModel.CanUndo);
}

[Fact]
public void PasteMode_CancelsWhenLayerSelectionChanges()
{
    SeedClipboard();
    _viewModel.BeginPasteMode();
    _viewModel.SelectedLayers = 1 << 2;
    Assert.False(_viewModel.PasteMode);
}

[Fact]
public async Task NewDocument_ClearsClipboardPasteAndSelectionRectangle()
{
    SeedClipboard();
    _viewModel.BeginPasteMode();
    _dialogs.NewMapResult = new NewMapRequest(10, 10);
    _dialogs.DirtyResult = DirtyChoice.Discard;
    await _viewModel.NewAsync();
    Assert.Null(_viewModel.Clipboard);
    Assert.Null(_viewModel.SelectionRectangle);
    Assert.False(_viewModel.PasteMode);
}
```

**Step 2: Run tests to verify they fail (red)**

Run: `dotnet test tests/MapEditor.App.Tests -v q --nologo`
Expected: compile failure — `TileClipboard`/`Clipboard`/`PasteMode`/`SelectionRectangle`/`CopySelection`/`BeginPasteMode`/`ApplyPasteAt` missing.

**Step 3: Implement**

`src/MapEditor.App/ViewModels/TileClipboard.cs` (new):

```csharp
using MapEditor.Core;

namespace MapEditor.App.ViewModels;

internal sealed class TileClipboard
{
    private TileClipboard(int width, int height, MapTileLayer[]?[] layers)
    {
        Width = width;
        Height = height;
        Layers = layers;
    }

    public int Width { get; }

    public int Height { get; }

    public MapTileLayer[]?[] Layers { get; }

    public static TileClipboard Capture(MapDocument document, byte selectedLayers, MapTileRectangle rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0 || rect.X < 0 || rect.Y < 0 ||
            rect.X + rect.Width > document.Width || rect.Y + rect.Height > document.Height)
        {
            throw new ArgumentOutOfRangeException(nameof(rect));
        }

        var layers = new MapTileLayer[MapDocument.LayerCount][];
        for (int layer = 0; layer < MapDocument.LayerCount; layer++)
        {
            if ((selectedLayers & (1 << layer)) == 0)
            {
                continue;
            }

            var data = new MapTileLayer[rect.Width * rect.Height];
            for (int row = 0; row < rect.Height; row++)
            {
                for (int col = 0; col < rect.Width; col++)
                {
                    data[row * rect.Width + col] = document[rect.X + col, rect.Y + row].GetLayer(layer);
                }
            }

            layers[layer] = data;
        }

        return new TileClipboard(rect.Width, rect.Height, layers);
    }
}
```

`MainWindowViewModel`:
- Widen the `ActiveTool` range check (`:72`) to `MapEditTool.FloodFill`.
- Add:

```csharp
private TileClipboard? _clipboard;
private bool _pasteMode;
private MapTileRectangle? _selectionRectangle;

public TileClipboard? Clipboard => _clipboard;

public bool PasteMode
{
    get => _pasteMode;
    private set => SetField(ref _pasteMode, value);
}

public MapTileRectangle? SelectionRectangle
{
    get => _selectionRectangle;
    set => SetField(ref _selectionRectangle, value);
}

public MapTileRectangle? PasteGhost
{
    get
    {
        if (!_pasteMode || _clipboard is not { } clip || HoverX is not { } x || HoverY is not { } y)
        {
            return null;
        }

        return new MapTileRectangle(x, y, clip.Width, clip.Height);
    }
}

public void CopySelection()
{
    if (SelectionRectangle is not { } rect)
    {
        return;
    }

    _clipboard = TileClipboard.Capture(_session.Document, _session.SelectedLayers, rect);
}

public void BeginPasteMode()
{
    if (_clipboard is not null)
    {
        PasteMode = true;
    }
}

public void CancelPasteMode()
{
    PasteMode = false;
}

public void ApplyPasteAt(int x, int y)
{
    TileClipboard clip = _clipboard;
    if (clip is null)
    {
        return;
    }

    MapTileRectangle? dest = new MapTileRectangle(x, y, clip.Width, clip.Height)
        .ClipTo(_session.Document.Width, _session.Document.Height);
    if (dest is { } d)
    {
        int sourceX = d.X - x;
        int sourceY = d.Y - y;
        var sub = new MapTileLayer[MapDocument.LayerCount][];
        for (int layer = 0; layer < MapDocument.LayerCount; layer++)
        {
            if (clip.Layers[layer] is not { } data)
            {
                continue;
            }

            var slice = new MapTileLayer[d.Width * d.Height];
            for (int row = 0; row < d.Height; row++)
            {
                Array.Copy(data, (sourceY + row) * clip.Width + sourceX, slice, row * d.Width, d.Width);
            }

            sub[layer] = slice;
        }

        _session.ApplyLayerPatch(d.X, d.Y, d.Width, d.Height, sub);
    }

    CancelPasteMode();
    Refresh(EditorRefresh.Canvas | EditorRefresh.Commands | EditorRefresh.Title);
}
```

- `SelectedLayers` setter: after writing, call `CancelPasteMode()`.
- `BeginPasteMode`/`CancelPasteMode`: call `Refresh(EditorRefresh.Canvas)` — the canvas only redraws on `CanvasInvalidated` or a local `Invalidate()` (hover move), so without this the ghost would not appear when Ctrl+V is pressed while the cursor is already over a cell, nor disappear on cancel via a layer change.
- `Refresh` document-replaced branch (`:301-318`): `_clipboard = null; CancelPasteMode(); SelectionRectangle = null;` (alongside the existing `HoverX`/`SelectedX` resets at `:312-315`).
- Do NOT add `PasteGhost` re-raise to the `HoverX`/`HoverY` setters — nothing binds to `PasteGhost`; the canvas re-renders via `Invalidate()` in `UpdateHover` (`MapCanvas.cs:189`).

**Step 4: Run tests to verify they pass (green)**

Run: `dotnet test tests/MapEditor.App.Tests -v q --nologo`
Expected: all pass.

**Step 5: Commit**

```bash
git add src/MapEditor.App/ViewModels tests/MapEditor.App.Tests/MainWindowViewModelTests.cs
git commit -m "feat: tile clipboard and paste mode in editor view model"
```

**Invariant-to-test matrix:**

| Invariant | Proved by |
|-----------|-----------|
| Copy captures only selected layers, null for the rest | `CopySelection_CapturesOnlySelectedLayers` |
| Paste writes each captured layer to its corresponding layer only (adversarial) | `ApplyPasteAt_WritesEachCapturedLayerToItsCorrespondingLayer` |
| Edge paste clips to bounds, one undo restores (adversarial) | `ApplyPasteAt_EdgeOrigin_ClipsToDocumentBounds` |
| Fully out-of-bounds paste is a no-op with no history (adversarial) | `ApplyPasteAt_FullyOutOfBounds_ChangesNothing` |
| Layer-selection change cancels paste mode | `PasteMode_CancelsWhenLayerSelectionChanges` |
| Document replacement clears clipboard/paste/selection | `NewDocument_ClearsClipboardPasteAndSelectionRectangle` |

---

### Task 4: App — canvas tool behavior, toolbar, hotkeys

**Files:**
- Modify: `src/MapEditor.App/Controls/MapCanvas.cs`
- Modify: `src/MapEditor.App/Views/MainWindow.axaml`
- Modify: `src/MapEditor.App/Views/MainWindow.axaml.cs`
- Test: `tests/MapEditor.App.Tests/MapCanvasTests.cs`
- Test: `tests/MapEditor.App.Tests/MainWindowTests.cs`

**Mutation impact:**
- Source of truth changed: left-click pointer flow in `MapCanvas` (`OnPointerPressed` `:102`, local `BeginStroke` `:293`) — currently every left press starts a session stroke; now it routes by tool and by paste mode
- Important readers: `MapCanvas.OnPointerMoved/OnPointerReleased/OnPointerCaptureLost` (`:229` for capture-lost) finalize strokes; `FinishInteraction` (`:42`) is called by every menu command and close
- Derived/cached state affected: paste mode must be cancelled by `FinishInteraction` (menu commands, close), Escape, window-level presses outside the canvas, and layer-selection changes, or a stale paste would fire on the next click after e.g. Ctrl+Z
- Required propagation sequence:
  1. `OnPointerPressed` (left, not space-pan): if `_viewModel.PasteMode` → `TileAt` hit: `ApplyPasteAt(tile.X, tile.Y)`; miss: `CancelPasteMode()`; consume the press — set `e.Handled = true` and do NOT start a stroke or capture
  2. else route by `_viewModel.ActiveTool`: `Select` → set `SelectedX/SelectedY`, then `_viewModel.Refresh(EditorRefresh.Canvas)` (the selected-tile overlay only redraws on `CanvasInvalidated`/`Invalidate` — without this it stays stale until the next hover move); no stroke; `MultiSelect` → begin local rectangle drag (`_rectDragStart = tile`, `_multiSelecting = true`, `_rectDragMoved = false`) and capture the pointer (store it in a new `IPointer? _capturedPointer` field — the existing capture site `:131` stores nothing, and `FinishInteraction` has no event to release through); `FloodFill` → `_viewModel.Session.ApplyFloodFill(tile.X, tile.Y)`, set `SelectedX/SelectedY`, refresh; default → existing `BeginStroke`
  3. `IsGestureActive` (`:100`) and the capture condition (`:131`): include `_multiSelecting` — otherwise the drag freezes when the pointer leaves the canvas and release-outside never finalizes
  4. `OnPointerMoved` while `_multiSelecting`: normalize `_rectDragStart`→current tile into inclusive bounds; the raw `ScreenToTile` coordinate is clamped independently to `[0, Width-1]` / `[0, Height-1]` before normalizing against the drag origin (a null `TileAt` mid-drag clamps to the map edge), set `_viewModel.SelectionRectangle = new MapTileRectangle(minX, minY, w, h)` (w/h = span+1), `_rectDragMoved = true`, `Invalidate()`
  5. `OnPointerReleased` while `_multiSelecting`: if no move occurred, set a 1×1 rectangle at the pressed tile; clear `_multiSelecting`; `Invalidate()`
  6. `OnKeyDown` Escape (`:268`): existing `FinishInteraction(commit: false)` plus `_viewModel.CancelPasteMode()`
  7. `FinishInteraction`: add `_viewModel.CancelPasteMode()`, and clear `_multiSelecting`/`_rectDragStart` + release the captured pointer via `_capturedPointer?.Capture(null); _capturedPointer = null;` (Avalonia 11.3.20 has no `ReleaseCapture()` — the codebase pattern is `Pointer.Capture(null)`, see `MapCanvas.cs:224`) — a menu command or Escape mid-drag must not leave the canvas permanently gesture-active (`FinishInteraction` currently clears only `_stroking`/`_panning`)
  8. `MainWindow.OnKeyDown`: add `case Key.Escape:` → `Canvas.FinishInteraction(commit: false); _viewModel.CancelPasteMode();` — window-level so Esc cancels paste regardless of focus (the canvas's own Escape handler sets `e.Handled` and wins when the canvas is focused; both paths reach the same state). The paste-Escape test must therefore not require canvas focus
  9. `BuildRenderRequest` (`:344`): pass `SelectionRectangle: _viewModel.SelectionRectangle, PasteGhost: _viewModel.PasteGhost`
- Invariants to preserve:
  - Select and FloodFill never leave `session.HasActiveStroke` true
  - paste mode ends after exactly one click (apply or cancel)
  - existing pencil/eraser/eyedropper/blocked stroke behavior is byte-for-byte unchanged
- Observable proof required: adversarial test — entering paste mode, raising the real Undo menu item (`UndoCommand`), then clicking does not paste (no document change, no history entry).

**Step 1: Write the failing tests**

`tests/MapEditor.App.Tests/MapCanvasTests.cs` (follow the file's existing `CreateSmallMapAsync`/`Cell`/`MouseDown` patterns; add `using Avalonia.Interactivity;` for `RoutedEventArgs`):

```csharp
[AvaloniaFact]
public async Task SelectTool_Click_SetsSelectionWithoutStrokeOrHistory()
{
    Harness harness = await CreateSmallMapAsync();
    harness.ViewModel.ActiveTool = MapEditTool.Select;
    Point p = new(Cell / 2, Cell / 2);
    harness.Window.MouseDown(p, MouseButton.Left, RawInputModifiers.None);
    harness.Window.MouseUp(p, MouseButton.Left, RawInputModifiers.None);

    Assert.Equal(0, harness.ViewModel.SelectedX);
    Assert.Equal(0, harness.ViewModel.SelectedY);
    Assert.False(harness.ViewModel.Session.HasActiveStroke);
    Assert.False(harness.ViewModel.Session.CanUndo);
}

[AvaloniaFact]
public async Task FloodFillTool_Click_FillsRegionAsOneUndoableCommand()
{
    Harness harness = await CreateSmallMapAsync();
    MapDocument document = harness.ViewModel.Session.Document;
    // the harness map is 4x4 (MapSize = 4); ring = all perimeter cells with (1,1),
    // interior = the 4 cells (1,1),(1,2),(2,1),(2,2)
    for (int x = 0; x < MapSize; x++)
        for (int y = 0; y < MapSize; y++)
            if (x == 0 || x == MapSize - 1 || y == 0 || y == MapSize - 1)
                document.SetLayer(x, y, 0, new MapTileLayer(1, 1));

    harness.ViewModel.Brush = new MapTileLayer(9, 9);
    harness.ViewModel.ActiveTool = MapEditTool.FloodFill;
    Point p = new(Cell + Cell / 2, Cell + Cell / 2);
    harness.Window.MouseDown(p, MouseButton.Left, RawInputModifiers.None);
    harness.Window.MouseUp(p, MouseButton.Left, RawInputModifiers.None);

    Assert.Equal(new MapTileLayer(9, 9), document[1, 1].GetLayer(0));
    Assert.Equal(new MapTileLayer(9, 9), document[2, 2].GetLayer(0));
    Assert.Equal(new MapTileLayer(1, 1), document[0, 0].GetLayer(0));
    Assert.True(harness.ViewModel.Session.CanUndo);
    harness.ViewModel.Undo();
    Assert.Equal(new MapTileLayer(0, 0), document[1, 1].GetLayer(0));
}

[AvaloniaFact]
public async Task MultiSelectTool_Drag_SetsSelectionRectangle()
{
    Harness harness = await CreateSmallMapAsync();
    harness.ViewModel.ActiveTool = MapEditTool.MultiSelect;
    Point start = new(Cell / 2, Cell / 2);
    Point end = new(3 * Cell + Cell / 2, 2 * Cell + Cell / 2);
    harness.Window.MouseDown(start, MouseButton.Left, RawInputModifiers.None);
    harness.Window.MouseMove(end, RawInputModifiers.None);
    harness.Window.MouseUp(end, MouseButton.Left, RawInputModifiers.None);

    Assert.Equal(new MapTileRectangle(0, 0, 4, 3), harness.ViewModel.SelectionRectangle);
    Assert.False(harness.ViewModel.Session.HasActiveStroke);
    Assert.False(harness.ViewModel.Session.CanUndo);
}

[AvaloniaFact]
public async Task MultiSelectTool_DragOutsideMap_ClampsToMapBounds()
{
    Harness harness = await CreateSmallMapAsync();
    harness.ViewModel.ActiveTool = MapEditTool.MultiSelect;
    Point start = new(Cell / 2, Cell / 2);
    Point outside = new(3 * Cell + Cell / 2 + 400, 3 * Cell + Cell / 2 + 400);
    harness.Window.MouseDown(start, MouseButton.Left, RawInputModifiers.None);
    harness.Window.MouseMove(outside, RawInputModifiers.None);
    harness.Window.MouseUp(outside, MouseButton.Left, RawInputModifiers.None);

    // the 4x4 map fully covered — the out-of-bounds drag endpoint clamps to the map edge
    Assert.Equal(new MapTileRectangle(0, 0, 4, 4), harness.ViewModel.SelectionRectangle);
}

[AvaloniaFact]
public async Task PasteMode_IsOneShot_SecondClickRunsActiveTool()
{
    Harness harness = await CreateSmallMapAsync();
    MapDocument document = harness.ViewModel.Session.Document;
    document.SetLayer(0, 0, 0, new MapTileLayer(7, 7));
    document.SetLayer(1, 0, 0, new MapTileLayer(7, 7));
    document.SetLayer(0, 1, 0, new MapTileLayer(7, 7));
    document.SetLayer(1, 1, 0, new MapTileLayer(7, 7));

    harness.ViewModel.SelectionRectangle = new MapTileRectangle(0, 0, 2, 2);
    harness.ViewModel.CopySelection();
    harness.ViewModel.BeginPasteMode();
    Assert.True(harness.ViewModel.PasteMode);

    Point target = new(2 * Cell + Cell / 2, 2 * Cell + Cell / 2);
    harness.Window.MouseDown(target, MouseButton.Left, RawInputModifiers.None);
    harness.Window.MouseUp(target, MouseButton.Left, RawInputModifiers.None);

    Assert.False(harness.ViewModel.PasteMode);
    Assert.Equal(new MapTileLayer(7, 7), document[2, 2].GetLayer(0));
    Assert.True(harness.ViewModel.Session.CanUndo);

    // paste mode is one-shot: the second click runs the active tool (Pencil). With the
    // brush set to the pasted value the pencil stroke has zero deltas, so no second
    // history entry appears — a second paste would have pushed one
    harness.ViewModel.Brush = new MapTileLayer(7, 7);
    harness.Window.MouseDown(target, MouseButton.Left, RawInputModifiers.None);
    harness.Window.MouseUp(target, MouseButton.Left, RawInputModifiers.None);
    Assert.True(harness.ViewModel.Undo());
    Assert.False(harness.ViewModel.CanUndo);
}

[AvaloniaFact]
public async Task PasteMode_EscapeCancelsWithoutPasting()
{
    Harness harness = await CreateSmallMapAsync();
    MapDocument document = harness.ViewModel.Session.Document;
    document.SetLayer(0, 0, 0, new MapTileLayer(7, 7));
    harness.ViewModel.SelectionRectangle = new MapTileRectangle(0, 0, 1, 1);
    harness.ViewModel.CopySelection();
    harness.ViewModel.BeginPasteMode();

    harness.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
    Assert.False(harness.ViewModel.PasteMode);
    Assert.False(harness.ViewModel.Session.CanUndo);
}

[AvaloniaFact]
public async Task PasteMode_ClickOutsideMapCancelsWithoutPasting()
{
    Harness harness = await CreateSmallMapAsync();
    MapDocument document = harness.ViewModel.Session.Document;
    document.SetLayer(0, 0, 0, new MapTileLayer(7, 7));
    harness.ViewModel.SelectionRectangle = new MapTileRectangle(0, 0, 1, 1);
    harness.ViewModel.CopySelection();
    harness.ViewModel.BeginPasteMode();

    Point outside = new(4 * Cell + Cell / 2, 4 * Cell + Cell / 2); // past the 4x4 map
    harness.Window.MouseDown(outside, MouseButton.Left, RawInputModifiers.None);
    harness.Window.MouseUp(outside, MouseButton.Left, RawInputModifiers.None);
    Assert.False(harness.ViewModel.PasteMode);
    Assert.False(harness.ViewModel.Session.CanUndo);
}

[AvaloniaFact]
public async Task PasteMode_UndoMenuCancelsBeforeNextClick()
{
    Harness harness = await CreateSmallMapAsync();
    MapDocument document = harness.ViewModel.Session.Document;

    // paint a cell before entering paste mode so the Undo menu item is enabled
    harness.ViewModel.Brush = new MapTileLayer(9, 9);
    Point paint = new(Cell / 2, Cell / 2);
    harness.Window.MouseDown(paint, MouseButton.Left, RawInputModifiers.None);
    harness.Window.MouseUp(paint, MouseButton.Left, RawInputModifiers.None);
    Assert.True(harness.ViewModel.CanUndo);

    document.SetLayer(1, 0, 0, new MapTileLayer(7, 7));
    harness.ViewModel.SelectionRectangle = new MapTileRectangle(1, 0, 1, 1);
    harness.ViewModel.CopySelection();
    harness.ViewModel.BeginPasteMode();

    harness.Window.Find<MenuItem>("UndoCommand").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    Assert.False(harness.ViewModel.PasteMode);
    Assert.Equal(new MapTileLayer(0, 0), document[0, 0].GetLayer(0));

    // the next click runs the active tool, not a paste
    harness.ViewModel.Brush = new MapTileLayer(4, 4);
    harness.ViewModel.ActiveTool = MapEditTool.Pencil;
    Point p = new(Cell + Cell / 2, Cell + Cell / 2);
    harness.Window.MouseDown(p, MouseButton.Left, RawInputModifiers.None);
    harness.Window.MouseUp(p, MouseButton.Left, RawInputModifiers.None);

    Assert.Equal(new MapTileLayer(4, 4), document[1, 1].GetLayer(0));
}

[AvaloniaFact]
public async Task PasteMode_ClickOnNonCanvasControl_CancelsWithoutPasting()
{
    Harness harness = await CreateSmallMapAsync();
    MapDocument document = harness.ViewModel.Session.Document;
    document.SetLayer(0, 0, 0, new MapTileLayer(7, 7));
    harness.ViewModel.SelectionRectangle = new MapTileRectangle(0, 0, 1, 1);
    harness.ViewModel.CopySelection();
    harness.ViewModel.BeginPasteMode();

    // the status bar is a plain control with no other side effects
    Point status = harness.Window.Find<Border>("StatusBar").TranslatePoint(new Point(5, 5), harness.Window).Value;
    harness.Window.MouseDown(status, MouseButton.Left, RawInputModifiers.None);
    harness.Window.MouseUp(status, MouseButton.Left, RawInputModifiers.None);

    Assert.False(harness.ViewModel.PasteMode);
    Assert.False(harness.ViewModel.Session.CanUndo);
}
```

`tests/MapEditor.App.Tests/MainWindowTests.cs`:
- `Layout_ContainsFourToolTogglesWithPencilActive` (`:176`): rename to `Layout_ContainsSevenToolTogglesWithPencilActive`; assert the three new toggles (`SelectTool`, `MultiSelectTool`, `FloodFillTool`) exist, Pencil is still checked by default, and the toolbar's toggle children appear in the design order Pencil, Eraser, Eyedropper, Blocked, Select, Multi-select, Flood fill (assert the sequence of `x:Name`s of the `ToggleButton` descendants of `Find<Border>("Toolbar")`).
- Hotkey tests (use `KeyPressQwerty` — the `KeyPress(Key, …)` overload is `[Obsolete]`): `PhysicalKey.V` → `ActiveTool == Select`; `PhysicalKey.M` → `MultiSelect`; `PhysicalKey.B` → `FloodFill`; `PhysicalKey.X` → `BlockedToggle` (regression for the Part 1 change). Also add the V/M/B cases to `ShortcutTests.cs` next to the existing B/X cases (`:126-127`).
- Ctrl+C/Ctrl+V (probe-verified: `KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Control)` delivers `KeyModifiers.Control` headless — unlike pointer modifiers, which cannot be simulated): in `MainWindowTests` using the `MainWindowHarness`:

```csharp
[AvaloniaFact]
public void CtrlC_PopulatesClipboardAndCtrlV_EntersPasteMode()
{
    using MainWindowHarness harness = MainWindowHarness.Create();
    harness.ViewModel.Session.Document.SetLayer(0, 0, 0, new MapTileLayer(7, 7));
    harness.ViewModel.SelectionRectangle = new MapTileRectangle(0, 0, 1, 1);

    harness.Window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Control);
    Assert.NotNull(harness.ViewModel.Clipboard);

    harness.Window.KeyPressQwerty(PhysicalKey.V, RawInputModifiers.Control);
    Assert.True(harness.ViewModel.PasteMode);
}

[AvaloniaFact]
public void CtrlC_WithoutSelectionRectangle_DoesNothing()
{
    using MainWindowHarness harness = MainWindowHarness.Create();
    harness.Window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Control);
    Assert.Null(harness.ViewModel.Clipboard);
}

[AvaloniaFact]
public void CtrlC_WithBrushFieldFocused_KeepsNativeBehavior()
{
    using MainWindowHarness harness = MainWindowHarness.Create();
    harness.ViewModel.SelectionRectangle = new MapTileRectangle(0, 0, 1, 1);
    harness.Window.Find<TextBox>("BrushGraphic").Focus();

    harness.Window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Control);
    Assert.Null(harness.ViewModel.Clipboard);
}
```

The last test proves the `e.Source is not TextBox` guard: with a brush field focused, Ctrl+C must not populate the editor clipboard.

**Step 2: Run tests to verify they fail (red)**

Run: `dotnet test tests/MapEditor.App.Tests -v q --nologo`
Expected: new tests fail (tools not wired, toggles missing).

**Step 3: Implement**

`MapCanvas.cs`:
- New fields: `private MapTileCoordinate? _rectDragStart; private bool _multiSelecting; private bool _rectDragMoved; private IPointer? _capturedPointer;`
- `OnPointerPressed`: insert the paste-mode and tool routing described in the mutation impact before the existing `BeginStroke` call; only the default branch calls `BeginStroke`.
- `OnPointerMoved`: add the `_multiSelecting` branch (before the `_stroking` branch).
- `OnPointerReleased` / `OnPointerCaptureLost`: finalize `_multiSelecting` (1×1 if `!_rectDragMoved`).
- `OnKeyDown` Escape and `FinishInteraction`: add `_viewModel.CancelPasteMode()`.
- `BuildRenderRequest`: pass the two new options.
- `OnCanvasInvalidated` (document replacement): also clear `_rectDragStart`/`_multiSelecting` (the existing session-replacement branch already resets stroke/pan state).

`MainWindow.axaml` toolbar: after `BlockedTool` add:

```xml
<ToggleButton x:Name="SelectTool" Content="Select" Tag="Select" Checked="OnToolChecked" Unchecked="OnToolUnchecked" />
<ToggleButton x:Name="MultiSelectTool" Content="Multi-select" Tag="MultiSelect" Checked="OnToolChecked" Unchecked="OnToolUnchecked" />
<ToggleButton x:Name="FloodFillTool" Content="Flood fill" Tag="FloodFill" Checked="OnToolChecked" Unchecked="OnToolUnchecked" />
```

(`OnToolChecked` already parses the tag into `MapEditTool` — `MainWindow.axaml.cs:217`.)

`MainWindow.axaml.cs`:
- `SyncToolButtons` (`:424`): add the three new toggles.
- Window-level paste cancellation (any press not originating from the canvas cancels paste mode — clicks on the toolbar, right panel, or menus never reach `MapCanvas`): subscribe in the constructor and add:

```csharp
private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
{
    if (_viewModel.PasteMode && e.Source is not MapCanvas)
    {
        _viewModel.CancelPasteMode();
    }
}
```

(`PointerPressed` bubbles from the pressed control, so the window sees every press; `e.Source` is the canvas for canvas clicks, which the canvas handles itself — inside the map applies, outside cancels.)
- `OnKeyDown` unmodified-letter switch: add `Key.V` → `Select`, `Key.M` → `MultiSelect`, `Key.B` → `FloodFill` (the `Key.X` → `BlockedToggle` case exists from Part 1; `case Key.B` currently at `:158` was moved to X by Part 1).
- `OnKeyDown` primary-modifier switch: add, with a TextBox guard so brush fields keep native copy/paste:

```csharp
case Key.C when modifiers == PrimaryModifier && e.Source is not TextBox:
    _viewModel.CopySelection();
    e.Handled = true;
    break;
case Key.V when modifiers == PrimaryModifier && e.Source is not TextBox:
    _viewModel.BeginPasteMode();
    e.Handled = true;
    break;
```

**Step 4: Verify design-doc sync**

The design doc was already updated during planning to describe the paste ghost as a flat per-cell translucent highlight + outline (not a sprite preview — the draw sink has no per-sprite alpha). If the implementation diverges from that description, re-sync `docs/plans/2026-09-03-map-editor-ui-tools-design.md` in the Step 5 commit.

**Step 5: Run tests to verify they pass (green)**

Run: `dotnet test tests/MapEditor.App.Tests -v q --nologo`
Expected: all pass. Full sweep:

```bash
dotnet test tests/MapEditor.Core.Tests -v q --nologo && dotnet test tests/MapEditor.Rendering.Tests -v q --nologo && dotnet test tests/MapEditor.App.Tests -v q --nologo
```

**Step 6: Commit**

```bash
git add src/MapEditor.App tests/MapEditor.App.Tests docs/plans/2026-09-03-map-editor-ui-tools-design.md
git commit -m "feat: select, multi-select copy/paste, and flood fill tools in the map editor"
```

**Invariant-to-test matrix:**

| Invariant | Proved by |
|-----------|-----------|
| Select sets selection with no stroke/history (adversarial) | `SelectTool_Click_SetsSelectionWithoutStrokeOrHistory` |
| Flood fill click = bounded fill, one undo | `FloodFillTool_Click_FillsRegionAsOneUndoableCommand` |
| Multi-select drag yields the exact rectangle, no document change | `MultiSelectTool_Drag_SetsSelectionRectangle` |
| Drag outside the map clamps the rectangle to map bounds (adversarial) | `MultiSelectTool_DragOutsideMap_ClampsToMapBounds` |
| Paste applies exactly once; second click runs the active tool (adversarial) | `PasteMode_IsOneShot_SecondClickRunsActiveTool` |
| Escape cancels paste mode without pasting | `PasteMode_EscapeCancelsWithoutPasting` |
| Click outside the map cancels paste mode without pasting | `PasteMode_ClickOutsideMapCancelsWithoutPasting` |
| Menu/close path cancels paste mode before the next click (adversarial) | `PasteMode_UndoMenuCancelsBeforeNextClick` (raises the real Undo menu item) |
| Click on a non-canvas control cancels paste mode without pasting | `PasteMode_ClickOnNonCanvasControl_CancelsWithoutPasting` |
| Toolbar shows all seven tools in the design order; hotkeys V/M/B/X route correctly | extended `MainWindowTests` toolbar + hotkey tests |
| Ctrl+C populates clipboard, Ctrl+V enters paste mode; no-selection Ctrl+C is a no-op | `CtrlC_PopulatesClipboardAndCtrlV_EntersPasteMode`, `CtrlC_WithoutSelectionRectangle_DoesNothing` |
| Ctrl+C guarded against TextBox focus | `CtrlC_WithBrushFieldFocused_KeepsNativeBehavior` |

---

## Final verification (after all tasks)

Run: `dotnet test tests/MapEditor.Core.Tests -v q --nologo && dotnet test tests/MapEditor.Rendering.Tests -v q --nologo && dotnet test tests/MapEditor.App.Tests -v q --nologo`
Expected: all green.

Manual smoke (optional): run `src/MapEditor.App` — draw a square ring on the Entities layer, flood-fill its interior with the bucket, multi-select a region across two layers, copy, Ctrl+V, verify the ghost follows the cursor and Esc cancels.

## Design alignment notes

- Tool hotkeys: V=Select, M=Multi-select, B=Flood fill, X=Blocked (Part 1).
- Toolbar order: Pencil, Eraser, Eyedropper, Blocked, Select, Multi-select, Flood fill.
- Flood fill: 4-directional, matches the clicked cell's tile, topmost selected layer, one undo entry, no-op produces nothing.
- Paste: top-left anchored ghost, clipped to map bounds, one undo entry, Esc/outside-click/selection-change cancels.
- Ghost rendering is a region highlight (per-cell translucent fill + outline), not a sprite preview — the draw sink has no per-sprite alpha; a sprite-accurate preview is a follow-up if wanted.
- No comments/doc strings beyond the non-obvious-invariant comments shown in the proposed code (AGENTS.md default: none).
