# Map Editor — Part 1: Layer Selection Model & UI Rework

**Goal:** Replace the single active-layer concept with a multi-layer selection set (editing targets the topmost selected layer), replace the right panel's radio/checkbox groups with a named multi-selectable layer list, move Grid/Blocked into a checkable View menu, and strip the toolbar to tool toggles only.

**Architecture:** `MapEditSession` gains a `SelectedLayers` byte mask + `TopLayer` replacing `ActiveLayer`. `MainWindowViewModel` exposes `SelectedLayers` and a single `LayerVisibility` mask replacing the five `LayerNVisible` properties. The window view swaps the right-panel controls for five row Borders with visibility checkboxes, adds the View menu, and removes the file/zoom toolbar buttons. No file-format, undo, or rendering changes in this part.

**Tech Stack:** C# (.NET 8 core / .NET 10 app), Avalonia 11.3.20, xunit + Avalonia.Headless.

**Part:** 1 of 2. Part 2 (new tools: Select, Multi-select, Flood fill) builds on this plan's `SelectedLayers` API.

**Repo rules:** Per `AGENTS.md`, add no comments or doc strings to new/modified code. Leave unrelated existing comments untouched.

**APIs verified:**
- `MapEditSession.ActiveLayer` — `src/MapEditor.Core/Editing/MapEditSession.cs:36`; used by `BeginStroke` at `:89`
- `MapDocument.LayerCount = 5` — `src/MapEditor.Core/MapDocument.cs:68`
- `MainWindowViewModel.ActiveLayer` — `src/MapEditor.App/ViewModels/MainWindowViewModel.cs:81`; `Layer0Visible` `:134` (five copies through `:182`); `ShowGrid` `:194`; `ShowBlocked` `:206`
- `MapCanvas.BuildRenderRequest` composes the visibility mask — `src/MapEditor.App/Controls/MapCanvas.cs:344`
- Checkable menu items: `MenuItem.ToggleType` + `MenuItemToggleType.CheckBox` and `MenuItem.IsChecked` (Avalonia 11.3.20, `Avalonia.Controls.xml`: `P:Avalonia.Controls.MenuItem.ToggleType`, `F:Avalonia.Controls.MenuItemToggleType.CheckBox`, `P:Avalonia.Controls.MenuItem.IsChecked`)
- Headless pointer/keyboard test helpers: `HeadlessWindowExtensions.MouseDown(TopLevel, Point, MouseButton, RawInputModifiers)`, `.MouseMove`, `.MouseUp`, `.KeyPress(TopLevel, Key, RawInputModifiers)` (`Avalonia.Headless.xml`, Avalonia.Headless 11.3.20)
- Test harness: `MainWindowHarness.Create()` — `tests/MapEditor.App.Tests/MainWindowTests.cs:34`; `Find<T>(name)` helper `:105`

---

### Task 1: Core — `SelectedLayers` mask + `TopLayer` in `MapEditSession`

**Files:**
- Modify: `src/MapEditor.Core/Editing/MapEditSession.cs`
- Test: `tests/MapEditor.Core.Tests/MapEditSessionTests.cs`
- Test: `tests/MapEditor.Rendering.Tests/MapRendererTests.cs:289` (one-line API update)

**Mutation impact:**
- Source of truth changed: `MapEditSession._activeLayer` (`src/MapEditor.Core/Editing/MapEditSession.cs:36`)
- Important readers: `BeginStroke` (`:89`, picks the stroke's layer index); `MainWindowViewModel.ActiveLayer` (Task 2); tests listed above
- Derived/cached state affected: none — undo/redo commands and the file format record an explicit layer index per change (`MapLayerChange.LayerIndex`), so history and persistence are unaffected
- Required propagation sequence: replace field + property, update `BeginStroke` to use `TopLayer`; no other core call sites
- Invariants to preserve:
  - mask is never 0 and never has bits ≥ `LayerCount`
  - `TopLayer` is always the highest set bit and is always within the mask
  - a stroke applies to `TopLayer` even when multiple layers are selected
- Observable proof required: adversarial test asserting a pencil stroke with a multi-layer selection writes only to the topmost layer and leaves lower selected layers untouched.

**Step 1: Write the failing tests**

In `tests/MapEditor.Core.Tests/MapEditSessionTests.cs`, replace the `ActiveLayer` tests (lines ~16, ~43-50, and every `session.ActiveLayer = N` assignment) with the mask API, and add:

```csharp
[Fact]
public void SelectedLayers_DefaultsToLayer0Only()
{
    var session = CreateSession();
    Assert.Equal((byte)1, session.SelectedLayers);
    Assert.Equal(0, session.TopLayer);
}

[Theory]
[InlineData(0, 0)]
[InlineData(0b00101, 2)]
[InlineData(0b11111, 4)]
public void SelectedLayers_Mask_TopLayerIsHighestSetBit(byte mask, int expectedTop)
{
    var session = CreateSession();
    session.SelectedLayers = mask;
    Assert.Equal(expectedTop, session.TopLayer);
}

[Theory]
[InlineData((byte)0)]
[InlineData((byte)0b111110)]
public void SelectedLayers_InvalidMask_ThrowsWithoutChangingSelection(byte mask)
{
    var session = CreateSession();
    session.SelectedLayers = 0b00101;
    Assert.Throws<ArgumentOutOfRangeException>(() => session.SelectedLayers = mask);
    Assert.Equal((byte)0b00101, session.SelectedLayers);
}

[Fact]
public void Pencil_WithMultiLayerSelection_EditsOnlyTopmostLayer()
{
    var session = CreateSession();
    session.SelectedLayers = 0b01001; // layers 0 and 3
    session.SelectedTileLayer = new MapTileLayer(7, 42);
    session.BeginStroke(MapEditTool.Pencil, 0, 0);
    Assert.True(session.CompleteStroke());

    Assert.Equal(new MapTileLayer(7, 42), session.Document[0, 0].GetLayer(3));
    Assert.Equal(new MapTileLayer(0, 0), session.Document[0, 0].GetLayer(0));
}
```

Use the existing session-creation helper pattern in that file (check how `CreateSession`/fixtures are built there; the existing tests at lines 16-64 show the shape). Match the existing test style — no comments.

**Step 2: Run tests to verify they fail (red)**

Run: `dotnet test tests/MapEditor.Core.Tests -v q --nologo`
Expected: compile failure — `SelectedLayers`/`TopLayer` do not exist.

**Step 3: Implement**

In `MapEditSession`: replace the `_activeLayer` field and `ActiveLayer` property with:

```csharp
private byte _selectedLayers = 1;

public byte SelectedLayers
{
    get => _selectedLayers;
    set
    {
        if (value == 0 || value >= 1 << MapDocument.LayerCount)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        _selectedLayers = value;
    }
}

public int TopLayer
{
    get
    {
        for (int layer = MapDocument.LayerCount - 1; layer >= 0; layer--)
        {
            if ((_selectedLayers & (1 << layer)) != 0)
            {
                return layer;
            }
        }

        return -1;
    }
}
```

Update `BeginStroke` (`:89`) to use `TopLayer` instead of `_activeLayer`. Remove `_activeLayer`.

Update `tests/MapEditor.Rendering.Tests/MapRendererTests.cs:289`: `session.ActiveLayer = 2;` → `session.SelectedLayers = 1 << 2;`.

**Step 4: Run tests to verify they pass (green)**

Run: `dotnet test tests/MapEditor.Core.Tests -v q --nologo && dotnet test tests/MapEditor.Rendering.Tests -v q --nologo`
Expected: all pass (Core was 168, Rendering was 163 at baseline).

Note: `tests/MapEditor.App.Tests` will not compile yet (it uses `ViewModel.ActiveLayer` and `LayerNVisible`) — that is fixed in Task 2. Do not run it in this task.

**Step 5: Commit**

```bash
git add src/MapEditor.Core/Editing/MapEditSession.cs tests/MapEditor.Core.Tests/MapEditSessionTests.cs tests/MapEditor.Rendering.Tests/MapRendererTests.cs
git commit -m "feat: multi-layer selection mask in MapEditSession"
```

**Invariant-to-test matrix:**

| Invariant | Proved by |
|-----------|-----------|
| Default selection is layer 0 only | `SelectedLayers_DefaultsToLayer0Only` |
| TopLayer is highest set bit | `SelectedLayers_Mask_TopLayerIsHighestSetBit` |
| Empty/oversized mask rejected, state unchanged (adversarial) | `SelectedLayers_InvalidMask_ThrowsWithoutChangingSelection` |
| Multi-select edits only topmost layer (adversarial) | `Pencil_WithMultiLayerSelection_EditsOnlyTopmostLayer` |

---

### Task 2: ViewModel — `SelectedLayers` + `LayerVisibility` mask

**Files:**
- Modify: `src/MapEditor.App/ViewModels/MainWindowViewModel.cs`
- Modify: `src/MapEditor.App/Controls/MapCanvas.cs:344-371` (`BuildRenderRequest` mask composition)
- Test: `tests/MapEditor.App.Tests/MainWindowViewModelTests.cs`
- Test: `tests/MapEditor.App.Tests/MapCanvasTests.cs` (lines 165, 297, 371-384)
- Test: `tests/MapEditor.App.Tests/MainWindowTests.cs` (mechanical API updates only: lines 202, 240, 337; layout tests rewritten in Task 3)

**Mutation impact:**
- Source of truth changed: `MainWindowViewModel._layer0Visible`…`_layer4Visible` (`src/MapEditor.App/ViewModels/MainWindowViewModel.cs:134-182`) → single `_layerVisibility` byte; `ActiveLayer` property (`:81`) → `SelectedLayers`/`TopLayer`
- Important readers: `MapCanvas.BuildRenderRequest` (`src/MapEditor.App/Controls/MapCanvas.cs:344`); code-behind `SyncLayerRadios`/`OnViewModelPropertyChanged` (`src/MapEditor.App/Views/MainWindow.axaml.cs:406,430-439` — updated in Task 3); tests listed
- Derived/cached state affected: none — visibility is view-only state, never persisted; `MapRenderOptions` is rebuilt per render
- Required propagation sequence:
  1. `LayerVisibility` setter: `SetField` then `Refresh(EditorRefresh.Canvas)` (same as today's `LayerNVisible` setters at `:134-141`)
  2. `SelectedLayers` setter: write to `_session.SelectedLayers`, `OnPropertyChanged()` when changed (same shape as today's `ActiveLayer` at `:81-97`)
  3. `Refresh(EditorRefresh.Document)` raises `OnPropertyChanged(nameof(SelectedLayers))` in place of `ActiveLayer` (`:316`)
  4. `MapCanvas.BuildRenderRequest`: replace the five `if (_viewModel.LayerNVisible)` blocks with `byte mask = _viewModel.LayerVisibility;`
- Invariants to preserve:
  - default visibility mask is `0b11111` (all visible), default selection is `0b00001`
  - `LayerVisibility` accepts any byte (mask is view-only; no validation needed beyond byte range — it is a byte)
  - canvas invalidation still fires on visibility change
- Observable proof required: existing `MapCanvasTests` visibility-toggle test (lines 371-384) rewritten against the mask and still passing.

**Step 1: Update tests first (red)**

`MainWindowViewModelTests.cs`:
- Lines 55, 59-63: `Assert.Equal(0, _viewModel.ActiveLayer)` → `Assert.Equal((byte)1, _viewModel.SelectedLayers)`; the five `LayerNVisible` asserts → `Assert.Equal((byte)0b11111, _viewModel.LayerVisibility)`
- Lines 100-114 (`ActiveLayer_ValidValue…`, `ActiveLayer_OutOfRange…`): rewrite for `SelectedLayers` — valid value `(byte)0b01000` writes to session and raises only `SelectedLayers`; invalid values `0` and `(byte)0b111110` throw without mutating the session
- Lines 147-150: `Layer2Visible = false` → `LayerVisibility = 0b11011`, assert raised property is `LayerVisibility`
- Lines 263-270: `Layer0Visible = false` → `LayerVisibility = 0b11110`

`MapCanvasTests.cs`:
- Lines 165, 297: `harness.ViewModel.ActiveLayer = 2;` → `harness.ViewModel.SelectedLayers = 1 << 2;`
- Lines 371-380: the five `LayerNVisible = false/true` pairs → single `harness.ViewModel.LayerVisibility = (byte)0b11110;` / `= (byte)0b11111;` (keep the `ShowGrid`/`ShowBlocked` lines unchanged)

`MainWindowTests.cs` (mechanical only, keeps the project compiling; the layout tests themselves are rewritten in Task 3):
- Line 202: `Assert.Equal(3, ViewModel.ActiveLayer);` → `Assert.Equal((byte)0b01000, ViewModel.SelectedLayers);`
- Line 240: `ViewModel.ActiveLayer = 2;` → `ViewModel.SelectedLayers = 1 << 2;`
- Line 337: `Assert.Contains(nameof(MainWindowViewModel.ActiveLayer), raised);` → `SelectedLayers`; line 339: `Assert.Equal(0, _viewModel.ActiveLayer)` → `Assert.Equal((byte)1, _viewModel.SelectedLayers)`
- Line 326: `_viewModel.ActiveLayer = 2;` → `_viewModel.SelectedLayers = 1 << 2;`

**Step 2: Run tests to verify they fail (red)**

Run: `dotnet test tests/MapEditor.App.Tests -v q --nologo`
Expected: compile failure — `SelectedLayers`/`LayerVisibility` do not exist.

**Step 3: Implement**

In `MainWindowViewModel`:
- Remove `ActiveLayer` (`:81-97`), `Layer0Visible`…`Layer4Visible` (`:117-182`) and their fields.
- Add:

```csharp
public byte SelectedLayers
{
    get => _session.SelectedLayers;
    set
    {
        if (_session.SelectedLayers != value)
        {
            _session.SelectedLayers = value;
            OnPropertyChanged();
        }
    }
}

public int TopLayer => _session.TopLayer;

private byte _layerVisibility = 0b11111;

public byte LayerVisibility
{
    get => _layerVisibility;
    set
    {
        if (SetField(ref _layerVisibility, value))
        {
            Refresh(EditorRefresh.Canvas);
        }
    }
}
```

- In `Refresh`, replace `OnPropertyChanged(nameof(ActiveLayer))` (`:316`) with `OnPropertyChanged(nameof(SelectedLayers))`.

In `MapCanvas.BuildRenderRequest` (`:344`): replace the five `if` blocks with `byte mask = _viewModel.LayerVisibility;` and pass `new MapLayerVisibility(mask)`.

**Step 4: Run tests to verify they pass (green)**

Run: `dotnet test tests/MapEditor.App.Tests -v q --nologo`
Expected: all pass (baseline 199).

**Step 5: Commit**

```bash
git add src/MapEditor.App/ViewModels/MainWindowViewModel.cs src/MapEditor.App/Controls/MapCanvas.cs tests/MapEditor.App.Tests/MainWindowViewModelTests.cs tests/MapEditor.App.Tests/MapCanvasTests.cs tests/MapEditor.App.Tests/MainWindowTests.cs
git commit -m "feat: layer selection and visibility mask in editor view model"
```

**Invariant-to-test matrix:**

| Invariant | Proved by |
|-----------|-----------|
| Selection writes through to session, raises `SelectedLayers` only | rewritten `SelectedLayers_ValidValue_WritesToSessionAndRaisesOnlySelectedLayers` |
| Invalid mask throws without mutating session (adversarial) | rewritten `SelectedLayers_OutOfRange_ThrowsWithoutMutatingSession` |
| Visibility change invalidates canvas | existing canvas-invalidation test in `MapCanvasTests` (rewritten, lines 371-384) |
| Document replacement resets selection readout | rewritten `New_ReplacesDocument_RaisesBrushAndSelectedLayers…` |

---

### Task 3: View — layer list, View menu, toolbar strip-down, hotkey X

**Files:**
- Modify: `src/MapEditor.App/Views/MainWindow.axaml`
- Modify: `src/MapEditor.App/Views/MainWindow.axaml.cs`
- Create: `src/MapEditor.App/Views/LayerSelection.cs`
- Test: `tests/MapEditor.App.Tests/MainWindowTests.cs`
- Test: `tests/MapEditor.App.Tests/LayerSelectionTests.cs` (create)

**Step 1: Write the failing tests**

`tests/MapEditor.App.Tests/LayerSelectionTests.cs` (new):

```csharp
using Xunit;
using MapEditor.App.Views;

namespace MapEditor.App.Tests;

public class LayerSelectionTests
{
    [Fact]
    public void Plain_SingleSelectsClickedLayer()
    {
        Assert.Equal((byte)0b00100, LayerSelection.Plain(2));
    }

    [Fact]
    public void Toggle_AddsAndRemovesLayer()
    {
        Assert.Equal((byte)0b01100, LayerSelection.Toggle(0b00100, 3));
        Assert.Equal((byte)0b00000, LayerSelection.Toggle(0b00100, 2));
    }

    [Fact]
    public void Toggle_OffLastSelectedLayer_SingleSelectsIt()
    {
        Assert.Equal((byte)0b00100, LayerSelection.Toggle(0b00100, 2, keepNonEmpty: true));
    }

    [Fact]
    public void Range_SelectsInclusiveSpanFromAnchor()
    {
        Assert.Equal((byte)0b01110, LayerSelection.Range(1, 3));
        Assert.Equal((byte)0b01110, LayerSelection.Range(3, 1));
    }
}
```

`tests/MapEditor.App.Tests/MainWindowTests.cs`:
- Rewrite `Layout_ContainsFileEditToolbarCommands` (`:157`): drop the `NewButton`/`OpenButton`/`SaveButton`/`UndoButton`/`RedoButton` asserts; keep the menu-item asserts; add `Assert.NotNull(Find<MenuItem>("ViewMenu"))`, `GridMenuItem`, `BlockedMenuItem`.
- Rewrite `Layout_ContainsFiveLayerRadiosFiveVisibilityChecksAndOverlays` (`:195`) as `Layout_ContainsNamedLayerListAndViewToggles`:

```csharp
[AvaloniaFact]
public void Layout_ContainsNamedLayerListAndViewToggles()
{
    string[] names = { "Ground", "Below Entities", "Entities", "Above Entities", "Roof" };
    for (int layer = 0; layer < MapDocument.LayerCount; layer++)
    {
        Assert.NotNull(Find<Border>($"Layer{layer}Row"));
        Assert.True(Find<CheckBox>($"Layer{layer}VisibleCheck").IsChecked == true);
    }

    Assert.Equal((byte)1, ViewModel.SelectedLayers);

    Point row3 = Find<Border>("Layer3Row").TranslatePoint(new Point(10, 5), Window).Value;
    Window.MouseDown(row3, MouseButton.Left, RawInputModifiers.None);
    Window.MouseUp(row3, MouseButton.Left, RawInputModifiers.None);
    Assert.Equal((byte)0b01000, ViewModel.SelectedLayers);

    Point row1 = Find<Border>("Layer1Row").TranslatePoint(new Point(10, 5), Window).Value;
    Window.MouseDown(row1, MouseButton.Left, RawInputModifiers.Control);
    Window.MouseUp(row1, MouseButton.Left, RawInputModifiers.Control);
    Assert.Equal((byte)0b00110, ViewModel.SelectedLayers);

    Point row4 = Find<Border>("Layer4Row").TranslatePoint(new Point(10, 5), Window).Value;
    Window.MouseDown(row4, MouseButton.Left, RawInputModifiers.Shift);
    Window.MouseUp(row4, MouseButton.Left, RawInputModifiers.Shift);
    Assert.Equal((byte)0b11100, ViewModel.SelectedLayers);

    Find<CheckBox>("Layer2VisibleCheck").IsChecked = false;
    Assert.Equal((byte)0b11011, ViewModel.LayerVisibility);

    Assert.True(Find<MenuItem>("GridMenuItem").IsChecked == true);
    Assert.False(Find<MenuItem>("BlockedMenuItem").IsChecked == true);
    Find<MenuItem>("BlockedMenuItem").IsChecked = true;
    Assert.True(ViewModel.ShowBlocked);
}
```

(Shift-click above starts from the anchor set by the last plain click — layer 3 — so the range 3..4 gives `0b11100`. If the headless `MouseDown` with `RawInputModifiers.Shift` does not surface modifiers through `PointerPointProperties.KeyModifiers`, verify with a quick probe test before wiring the handler; the `KeyModifiers` must come from the pressed pointer point, not `e.KeyModifiers`.)

- Replace `ToolbarZoomButtons_ExistAndZoomAroundCanvasCenter` (`:391`) with a key-based zoom test: `Window.KeyPress(Key.Add, RawInputModifiers.None)` → `ZoomPercent == 200`, `Window.KeyPress(Key.Subtract, RawInputModifiers.None)` → back to `100`.
- `CommandEnablement_FollowsSessionState` (`:281`): drop `undoButton`/`saveButton` references; assert menu-item `IsEnabled` only.
- `NewSmallerMap_WithOutOfRangeSelection_ClearsSelectionWithoutError` (`:333`): `Find<Button>("NewButton").RaiseEvent(...)` → `Find<MenuItem>("NewCommand").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent))`.
- `OpenCommand_UnexpectedExceptionAtWindowBoundary_ShowsLastErrorDialogAndKeepsDocument` (`:364`): same swap to `OpenCommand`.

**Step 2: Run tests to verify they fail (red)**

Run: `dotnet test tests/MapEditor.App.Tests -v q --nologo`
Expected: compile failure (`LayerSelection` missing) + layout test failures (radios still present, buttons still present).

**Step 3: Implement**

`src/MapEditor.App/Views/LayerSelection.cs` (new, pure logic, no Avalonia dependencies):

```csharp
using System;

namespace MapEditor.App.Views;

internal static class LayerSelection
{
    public static byte Plain(int layer) => (byte)(1 << layer);

    public static byte Toggle(byte current, int layer, bool keepNonEmpty = false)
    {
        byte next = (byte)(current ^ (1 << layer));
        return keepNonEmpty && next == 0 ? (byte)(1 << layer) : next;
    }

    public static byte Range(int anchor, int layer)
    {
        int lo = Math.Min(anchor, layer);
        int hi = Math.Max(anchor, layer);
        byte next = 0;
        for (int l = lo; l <= hi; l++)
        {
            next |= (byte)(1 << l);
        }

        return next;
    }
}
```

`MainWindow.axaml`:
- Menu: after `EditMenu`, add:

```xml
<MenuItem x:Name="ViewMenu" Header="_View">
  <MenuItem x:Name="GridMenuItem" Header="Grid" ToggleType="CheckBox" IsChecked="{Binding ShowGrid}" />
  <MenuItem x:Name="BlockedMenuItem" Header="Blocked" ToggleType="CheckBox" IsChecked="{Binding ShowBlocked}" />
</MenuItem>
```

- Toolbar: remove `NewButton`, `OpenButton`, `SaveButton`, `UndoButton`, `RedoButton`, `ZoomInButton`, `ZoomOutButton`. Keep the four tool toggles unchanged.
- Right panel: replace the "Active layer" radio group, the "Visibility" checkbox group, `ShowGridCheck`, and `ShowBlockedCheck` with:

```xml
<TextBlock Text="Layers" FontWeight="SemiBold" />
<Border x:Name="Layer0Row" Tag="0" Background="Transparent" PointerPressed="OnLayerRowPressed">
  <Grid ColumnDefinitions="*,Auto">
    <TextBlock Text="0 — Ground" VerticalAlignment="Center" />
    <CheckBox x:Name="Layer0VisibleCheck" Tag="0" VerticalAlignment="Center"
              Checked="OnLayerVisibilityChanged" Unchecked="OnLayerVisibilityChanged" />
  </Grid>
</Border>
<!-- repeat for Layer1Row "1 — Below Entities" … Layer4Row "4 — Roof" -->
```

Keep the "Selected tile" readout section unchanged.

`MainWindow.axaml.cs`:
- Remove `OnLayerSelected`, `SyncLayerRadios`, `OnZoomIn`, `OnZoomOut`, and the `ActiveLayer` case in `OnViewModelPropertyChanged` (`:406`).
- Add field `private int _layerAnchor;` and `private Border[] _layerRows;` / `private CheckBox[] _layerVisibleChecks;` populated in the constructor from the named controls.
- Add:

```csharp
private void OnLayerRowPressed(object? sender, PointerPressedEventArgs e)
{
    if (sender is not Border { Tag: string tag } || !int.TryParse(tag, out int layer))
    {
        return;
    }

    PointerPoint point = e.GetCurrentPoint(this);
    if (!point.Properties.IsLeftButtonPressed)
    {
        return;
    }

    KeyModifiers modifiers = point.Properties.KeyModifiers;
    byte next;
    if (modifiers.HasFlag(KeyModifiers.Control))
    {
        next = LayerSelection.Toggle(_viewModel.SelectedLayers, layer, keepNonEmpty: true);
    }
    else if (modifiers.HasFlag(KeyModifiers.Shift))
    {
        next = LayerSelection.Range(_layerAnchor, layer);
    }
    else
    {
        next = LayerSelection.Plain(layer);
        _layerAnchor = layer;
    }

    _viewModel.SelectedLayers = next;
}

private void OnLayerVisibilityChanged(object? sender, RoutedEventArgs e)
{
    if (sender is CheckBox { Tag: string tag } && int.TryParse(tag, out int layer))
    {
        byte mask = _viewModel.LayerVisibility;
        mask = (byte)(mask & ~(1 << layer));
        if (sender is CheckBox { IsChecked: true })
        {
            mask |= (byte)(1 << layer);
        }

        _viewModel.LayerVisibility = mask;
    }
}

private void SyncLayerRows()
{
    byte selection = _viewModel.SelectedLayers;
    byte visibility = _viewModel.LayerVisibility;
    for (int layer = 0; layer < MapDocument.LayerCount; layer++)
    {
        _layerRows[layer].Background = (selection & (1 << layer)) != 0 ? SelectedRowBrush : Brushes.Transparent;
        _layerVisibleChecks[layer].IsChecked = (visibility & (1 << layer)) != 0;
    }
}
```

with `private static readonly IBrush SelectedRowBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x99, 0xFF));`.
- `OnViewModelPropertyChanged`: `ActiveLayer` case → `SelectedLayers` case calling `SyncLayerRows()`; add a `LayerVisibility` case calling `SyncLayerRows()`.
- Constructor: replace `SyncLayerRadios()` with `SyncLayerRows()`.
- Hotkeys in `OnKeyDown`: `case Key.B:` → `case Key.X:` (blocked tool; `B` is reserved for flood fill in Part 2).
- Remove `OnZoomIn`/`OnZoomOut` handlers (the `+`/`-` keys in `OnKeyDown` and wheel zoom in `MapCanvas` remain).

**Step 4: Run tests to verify they pass (green)**

Run: `dotnet test tests/MapEditor.App.Tests -v q --nologo`
Expected: all pass. Then full sweep:
`dotnet test tests/MapEditor.Core.Tests -v q --nologo && dotnet test tests/MapEditor.Rendering.Tests -v q --nologo`

**Step 5: Commit**

```bash
git add src/MapEditor.App/Views/ tests/MapEditor.App.Tests/
git commit -m "feat: named multi-select layer list, View menu, tool-only toolbar"
```

**Invariant-to-test matrix:**

| Invariant | Proved by |
|-----------|-----------|
| Plain click single-selects; Ctrl toggles; Shift ranges from anchor | `Layout_ContainsNamedLayerListAndViewToggles` + `LayerSelectionTests` |
| Selection never empties via Ctrl toggle (adversarial) | `Toggle_OffLastSelectedLayer_SingleSelectsIt` |
| Visibility checkbox drives `LayerVisibility` mask without touching selection | `Layout_ContainsNamedLayerListAndViewToggles` (checkbox uncheck leaves `SelectedLayers` unchanged) |
| Grid/Blocked toggled from View menu, defaults on/off | `Layout_ContainsNamedLayerListAndViewToggles` |
| File/zoom toolbar buttons gone; menu items remain | rewritten `Layout_ContainsFileEditToolbarCommands` |
| Zoom still works via +/- keys after button removal | rewritten zoom test |

---

## Final verification (after all tasks)

Run: `dotnet test tests/MapEditor.Core.Tests -v q --nologo && dotnet test tests/MapEditor.Rendering.Tests -v q --nologo && dotnet test tests/MapEditor.App.Tests -v q --nologo`
Expected: all green (baseline totals 168/163/199, adjusted for rewritten tests).

Manual smoke (optional, headless environment may not allow): `./build-map-editor.sh` or run `src/MapEditor.App` — verify the layer list, View menu, and toolbar render as designed.

## Design alignment notes

- Layer names exactly: Ground / Below Entities / Entities / Above Entities / Roof, displayed as "0 — Ground" etc.
- Selected-tile readout keeps `L0 …` index format (unchanged).
- Toolbar order for Part 1: Pencil, Eraser, Eyedropper, Blocked (Part 2 appends Select, Multi-select, Flood fill).
- Hotkey change: Blocked B → X (B reserved for Part 2 flood fill).
- No new comments/doc strings anywhere (AGENTS.md).
