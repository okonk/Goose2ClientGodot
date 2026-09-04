# Map Editor — Part 1: Layer Selection Model & UI Rework

**Goal:** Replace the single active-layer concept with a multi-layer selection set (editing targets the topmost selected layer), replace the right panel's radio/checkbox groups with a named multi-selectable layer list, move Grid/Blocked into a checkable View menu, and strip the toolbar to tool toggles only.

**Architecture:** `MapEditSession` gains a `SelectedLayers` byte mask + `TopLayer` replacing `ActiveLayer`. `MainWindowViewModel` exposes `SelectedLayers` and a single `LayerVisibility` mask replacing the five `LayerNVisible` properties. The window view swaps the right-panel controls for five row Borders with visibility checkboxes, adds the View menu, and removes the file/zoom toolbar buttons. No file-format, undo, or rendering changes in this part.

**Tech Stack:** C# (.NET 8 core / .NET 10 app), Avalonia 11.3.20, xunit + Avalonia.Headless.

**Part:** 1 of 2. Part 2 (new tools: Select, Multi-select, Flood fill) builds on this plan's `SelectedLayers` API.

**Repo rules:** Per `AGENTS.md`, add no comments or doc strings to new/modified code. Leave unrelated existing comments untouched.

**APIs verified** (all line numbers against the worktree at this plan's base commit):
- `MapEditSession.ActiveLayer` — `src/MapEditor.Core/Editing/MapEditSession.cs:36`; `BeginStroke` `:80` (stroke constructed with `_activeLayer` at `:89`; eyedropper branch reads `_activeLayer` at `:92`); `ValidateTool` `:266` (`private static`)
- `MapDocument.LayerCount = 5` — `src/MapEditor.Core/MapDocument.cs:68`
- `MainWindowViewModel.ActiveLayer` — `src/MapEditor.App/ViewModels/MainWindowViewModel.cs:81-97`; `Layer0Visible` `:134` (five copies through `:182`); `ShowGrid` `:194`; `ShowBlocked` `:206`; `Refresh` document-replaced branch `:301-318` (hover/selection resets `:312-315`, `OnPropertyChanged(nameof(ActiveLayer))` `:316`)
- `MapCanvas.BuildRenderRequest` composes the visibility mask — `src/MapEditor.App/Controls/MapCanvas.cs:344`
- `MapLayerVisibility(byte mask)` throws `ArgumentOutOfRangeException` for `mask > 0b11111` — `src/MapEditor.Rendering/Composition/MapLayerVisibility.cs:18-21`
- Checkable menu items: `MenuItem.ToggleType` + `MenuItemToggleType.CheckBox`, `MenuItem.IsChecked`, `MenuItem.ClickEvent` (Avalonia 11.3.20). **Probe-verified:** `IsChecked` binds OneWay by default — `IsChecked="{Binding …, Mode=TwoWay}"` is required for click write-back; `RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent))` toggles `IsChecked` under a TwoWay binding in headless
- Pointer modifiers: `PointerEventArgs.KeyModifiers` exists (`Avalonia.Base.xml` `P:Avalonia.Input.PointerEventArgs.KeyModifiers`) — use `e.KeyModifiers` in the row handler. **Probe-verified:** headless `MouseDown(…, RawInputModifiers.Control)` and `KeyPressQwerty(PhysicalKey.ControlLeft)` + `MouseDown` both yield `KeyModifiers.None` — Ctrl/Shift cannot be simulated headless, so modifier behavior is covered by `LayerSelectionTests` only
- Headless test helpers: `HeadlessWindowExtensions.MouseDown/MouseMove/MouseUp/KeyPressQwerty/KeyReleaseQwerty` (`Avalonia.Headless.xml`). Codebase convention is `KeyPressQwerty(PhysicalKey, RawInputModifiers)` (see `tests/MapEditor.App.Tests/ShortcutTests.cs:34-127`); the `KeyPress(Key, …)` overload is `[Obsolete]`
- `ShortcutTests.cs:126-127` asserts `PhysicalKey.B` → `BlockedToggle` (breaks when B is freed for Part 2)
- Test harness: `MainWindowHarness.Create()` — `tests/MapEditor.App.Tests/MainWindowTests.cs:53`; `Find<T>(name)` helper `:105`; `Layout_ContainsFourToolTogglesWithPencilActive` `:176` (Part 1 keeps exactly four tool toggles, so this test stays green unchanged; Part 2 extends it)
- `MapEditSessionTests` has no `CreateSession` helper — tests build `new MapEditSession(MapDocument.Create(w, h))` inline (e.g. `:13, :45`)

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
- Observable proof required: adversarial test asserting a pencil stroke with a multi-layer selection writes only to the topmost layer and leaves lower selected layers untouched; and that the eyedropper samples the topmost layer of a multi-selection (design-doc test list).

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
[InlineData(0b00001, 0)]
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
[InlineData((byte)32)]
[InlineData((byte)0b111111)]
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

[Fact]
public void Eyedropper_WithMultiLayerSelection_SamplesTopmostLayer()
{
    var session = CreateSession();
    session.Document.SetLayer(0, 0, 0, new MapTileLayer(1, 1));
    session.Document.SetLayer(0, 0, 3, new MapTileLayer(2, 2));
    session.SelectedLayers = 0b01001; // layers 0 and 3

    session.BeginStroke(MapEditTool.Eyedropper, 0, 0);
    session.CompleteStroke();

    Assert.Equal(new MapTileLayer(2, 2), session.SelectedTileLayer);
}
```

Add a `CreateSession(int width = 4, int height = 4) => new MapEditSession(MapDocument.Create(width, height));` helper to the test file (no such helper exists — existing tests build sessions inline, e.g. `:13, :45`) and use it in the new tests. Match the existing test style — no comments.

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

Update `BeginStroke` (`:80`; stroke construction at `:89`) to use `TopLayer` instead of `_activeLayer`, and the eyedropper branch (`:92`) likewise. Remove `_activeLayer`. Leave `ValidateTool` (`:266`, static) at its current bound (`BlockedToggle`) — it guards `BeginStroke`, which must keep rejecting the non-stroking tools that Part 2 adds to the enum.

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
- Test: `tests/MapEditor.App.Tests/MainWindowViewModelTests.cs` (includes `New_ReplacesDocument_RaisesBrushAndActiveLayerWithSessionResetValues` at `:324-340`)
- Test: `tests/MapEditor.App.Tests/MapCanvasTests.cs` (lines 165, 297, 371-380)
- Test: `tests/MapEditor.App.Tests/MainWindowTests.cs` — `ActiveLayer` references exist at `:202` (`Assert.Equal(3, ViewModel.ActiveLayer)`) and `:240` (`ViewModel.ActiveLayer = 2`); rewrite them to the mask API in Task 3 along with the layout tests

**Mutation impact:**
- Source of truth changed: `MainWindowViewModel._layer0Visible`…`_layer4Visible` (`src/MapEditor.App/ViewModels/MainWindowViewModel.cs:134-182`) → single `_layerVisibility` byte; `ActiveLayer` property (`:81`) → `SelectedLayers`/`TopLayer`
- Important readers: `MapCanvas.BuildRenderRequest` (`src/MapEditor.App/Controls/MapCanvas.cs:344`); code-behind `SyncLayerRadios`/`OnViewModelPropertyChanged` (`src/MapEditor.App/Views/MainWindow.axaml.cs:406,430-439` — updated in Task 3); tests listed
- Derived/cached state affected: none — visibility is view-only state, never persisted; `MapRenderOptions` is rebuilt per render
- Required propagation sequence:
  1. `LayerVisibility` setter: `SetField` then `Refresh(EditorRefresh.Canvas)` (same as today's `LayerNVisible` setters at `:134-141`); **validate `value > 0b11111` and throw `ArgumentOutOfRangeException`** — `MapLayerVisibility` (Rendering) throws for the same range and `BuildRenderRequest` feeds the mask straight into it
  2. `SelectedLayers` setter: write to `_session.SelectedLayers`, `OnPropertyChanged()` when changed (same shape as today's `ActiveLayer` at `:81-97`)
  3. `Refresh(EditorRefresh.Document)` raises `OnPropertyChanged(nameof(SelectedLayers))` in place of `ActiveLayer` (`:316`)
  4. `MapCanvas.BuildRenderRequest`: replace the five `if (_viewModel.LayerNVisible)` blocks with `byte mask = _viewModel.LayerVisibility;`
- Invariants to preserve:
  - default visibility mask is `0b11111` (all visible), default selection is `0b00001`
  - `LayerVisibility` rejects `> 0b11111` (matches `MapLayerVisibility`), so the canvas can never receive an out-of-range mask
  - canvas invalidation still fires on visibility change
- Observable proof required: existing `MapCanvasTests` visibility-toggle test (lines 371-380) rewritten against the mask and still passing.

**Step 1: Update tests first (red)**

`MainWindowViewModelTests.cs`:
- Lines 55, 59-63 (`InitialState_ReflectsNewCleanDocument`): `Assert.Equal(0, _viewModel.ActiveLayer)` → `Assert.Equal((byte)1, _viewModel.SelectedLayers)`; the five `LayerNVisible` asserts → `Assert.Equal((byte)0b11111, _viewModel.LayerVisibility)`
- Lines 100-114 (`ActiveLayer_ValidValue…`, `ActiveLayer_OutOfRange…`): rewrite for `SelectedLayers` — valid value `(byte)0b01000` writes to session and raises only `SelectedLayers`; invalid values `0` and `(byte)32` throw without mutating the session
- Lines 147-150 (`LayerVisibility_RaisesOnlyChangedProperty`): `Layer2Visible = false` → `LayerVisibility = 0b11011`, assert raised property is `LayerVisibility`
- Lines 263-270: `Layer0Visible = false` → `LayerVisibility = 0b11110`
- Add: `LayerVisibility_OutOfRange_Throws` — `0b100000` throws `ArgumentOutOfRangeException` without invalidating the canvas
- Lines 324-340 (`New_ReplacesDocument_RaisesBrushAndActiveLayerWithSessionResetValues`): rename to `…SelectedLayers…`; `_viewModel.ActiveLayer = 2;` → `_viewModel.SelectedLayers = 1 << 2;`; `Assert.Contains(nameof(MainWindowViewModel.ActiveLayer), raised);` → `SelectedLayers`; `Assert.Equal(0, _viewModel.ActiveLayer);` → `Assert.Equal((byte)1, _viewModel.SelectedLayers);`

`MapCanvasTests.cs`:
- Lines 165, 297: `harness.ViewModel.ActiveLayer = 2;` → `harness.ViewModel.SelectedLayers = 1 << 2;`
- Lines 371-380: the five `LayerNVisible = false/true` pairs → single `harness.ViewModel.LayerVisibility = (byte)0b11110;` / `= (byte)0b11111;` (keep the `ShowGrid`/`ShowBlocked` lines unchanged)

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
        if (value > 0b11111)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        if (SetField(ref _layerVisibility, value))
        {
            Refresh(EditorRefresh.Canvas);
        }
    }
}
```

- In `Refresh`, replace `OnPropertyChanged(nameof(ActiveLayer))` (`:316`) with `OnPropertyChanged(nameof(SelectedLayers))`.

In `MapCanvas.BuildRenderRequest` (`:344`): replace the five `if` blocks with `byte mask = _viewModel.LayerVisibility;` and pass `new MapLayerVisibility(mask)`.

**Step 4: Verify (partial green)**

`MapEditor.App` intentionally does not compile yet: the code-behind (`MainWindow.axaml.cs:233,406,434-438`) and XAML (`MainWindow.axaml:72-76`) still reference the removed `ActiveLayer`/`LayerNVisible` properties until Task 3 migrates them. Tasks 2 and 3 are one compilation unit. Verify the projects that do compile:

Run: `dotnet test tests/MapEditor.Core.Tests -v q --nologo && dotnet test tests/MapEditor.Rendering.Tests -v q --nologo`
Expected: green. **Do not commit** — Task 3 commits both tasks together.

**Step 5: (no commit — see Task 3 Step 5)**

**Invariant-to-test matrix:**

| Invariant | Proved by |
|-----------|-----------|
| Selection writes through to session, raises `SelectedLayers` only | rewritten `SelectedLayers_ValidValue_WritesToSessionAndRaisesOnlySelectedLayers` |
| Invalid mask throws without mutating session (adversarial) | rewritten `SelectedLayers_OutOfRange_ThrowsWithoutMutatingSession` |
| Visibility change invalidates canvas | existing canvas-invalidation test in `MapCanvasTests` (rewritten, lines 371-380) |
| Out-of-range visibility mask rejected (adversarial) | `LayerVisibility_OutOfRange_Throws` |
| Document replacement resets selection readout | rewritten `New_ReplacesDocument_RaisesBrushAndSelectedLayers…` |

---

### Task 3: View — layer list, View menu, toolbar strip-down, hotkey X

**Files:**
- Modify: `src/MapEditor.App/Views/MainWindow.axaml`
- Modify: `src/MapEditor.App/Views/MainWindow.axaml.cs`
- Create: `src/MapEditor.App/LayerSelection.cs`
- Test: `tests/MapEditor.App.Tests/MainWindowTests.cs`
- Test: `tests/MapEditor.App.Tests/ShortcutTests.cs` (B → X, per Step 1)
- Test: `tests/MapEditor.App.Tests/LayerSelectionTests.cs` (create)

**Step 1: Write the failing tests**

`tests/MapEditor.App.Tests/LayerSelectionTests.cs` (new):

```csharp
using MapEditor.App;
using Xunit;

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

    [Fact]
    public void Apply_Plain_SingleSelectsAndReanchors()
    {
        Assert.Equal(((byte)0b00010, 1), LayerSelection.Apply(0b01000, 2, 1, LayerClickMode.Plain));
    }

    [Fact]
    public void Apply_Toggle_UpdatesMaskAndReanchors()
    {
        Assert.Equal(((byte)0b00110, 1), LayerSelection.Apply(0b00010, 0, 1, LayerClickMode.Toggle));
    }

    [Fact]
    public void Apply_Toggle_OffLastSelectedLayer_KeepsNonEmpty()
    {
        Assert.Equal(((byte)0b00001, 0), LayerSelection.Apply(0b00001, 0, 0, LayerClickMode.Toggle));
    }

    [Fact]
    public void Apply_Range_KeepsAnchorForExtension()
    {
        var (first, anchor) = LayerSelection.Apply(0b00001, 0, 3, LayerClickMode.Range);
        Assert.Equal((byte)0b01111, first);
        Assert.Equal(0, anchor);
        var (second, _) = LayerSelection.Apply(first, anchor, 4, LayerClickMode.Range);
        Assert.Equal((byte)0b11111, second);
    }
}
```

`tests/MapEditor.App.Tests/MainWindowTests.cs`:
- Rewrite `Layout_ContainsFileEditToolbarCommands` (`:157`): drop the `NewButton`/`OpenButton`/`SaveButton`/`UndoButton`/`RedoButton` asserts and assert all seven removed controls are absent (`Assert.Null(Find<Button>("NewButton"))`, same for `OpenButton`, `SaveButton`, `UndoButton`, `RedoButton`, `ZoomInButton`, `ZoomOutButton`); keep the menu-item asserts; add `Assert.NotNull(Find<MenuItem>("ViewMenu"))`, `GridMenuItem`, `BlockedMenuItem`.
- Rewrite `Layout_ContainsFiveLayerRadiosFiveVisibilityChecksAndOverlays` (`:195`) as `Layout_ContainsNamedLayerListAndViewToggles` (add `using Avalonia.Media;` to the test file for `Brushes`):

```csharp
[AvaloniaFact]
public void Layout_ContainsNamedLayerListAndViewToggles()
{
    string[] names = { "Ground", "Below Entities", "Entities", "Above Entities", "Roof" };
    for (int layer = 0; layer < MapDocument.LayerCount; layer++)
    {
        Assert.NotNull(Find<Border>($"Layer{layer}Row"));
        Assert.True(Find<CheckBox>($"Layer{layer}VisibleCheck").IsChecked == true);
        Assert.Equal($"{layer} — {names[layer]}", Find<TextBlock>($"Layer{layer}Label").Text);
    }

    Assert.Equal((byte)1, ViewModel.SelectedLayers);
    Assert.NotEqual(Brushes.Transparent, Find<Border>("Layer0Row").Background);
    Assert.Equal(Brushes.Transparent, Find<Border>("Layer3Row").Background);

    Point row3 = Find<Border>("Layer3Row").TranslatePoint(new Point(10, 5), Window).Value;
    Window.MouseDown(row3, MouseButton.Left, RawInputModifiers.None);
    Window.MouseUp(row3, MouseButton.Left, RawInputModifiers.None);
    Assert.Equal((byte)0b01000, ViewModel.SelectedLayers);
    Assert.NotEqual(Brushes.Transparent, Find<Border>("Layer3Row").Background);
    Assert.Equal(Brushes.Transparent, Find<Border>("Layer0Row").Background);

    Point row1 = Find<Border>("Layer1Row").TranslatePoint(new Point(10, 5), Window).Value;
    Window.MouseDown(row1, MouseButton.Left, RawInputModifiers.None);
    Window.MouseUp(row1, MouseButton.Left, RawInputModifiers.None);
    Assert.Equal((byte)0b00010, ViewModel.SelectedLayers);

    Find<CheckBox>("Layer2VisibleCheck").IsChecked = false;
    Assert.Equal((byte)0b11011, ViewModel.LayerVisibility);

    Point check2 = Find<CheckBox>("Layer2VisibleCheck").TranslatePoint(new Point(5, 5), Window).Value;
    Window.MouseDown(check2, MouseButton.Left, RawInputModifiers.None);
    Window.MouseUp(check2, MouseButton.Left, RawInputModifiers.None);
    Assert.True(Find<CheckBox>("Layer2VisibleCheck").IsChecked == true);
    Assert.Equal((byte)0b11111, ViewModel.LayerVisibility);
    Assert.Equal((byte)0b00010, ViewModel.SelectedLayers);

    Find<MenuItem>("GridMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    Assert.False(Find<MenuItem>("GridMenuItem").IsChecked == true);
    Assert.False(ViewModel.ShowGrid);
    Find<MenuItem>("BlockedMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    Assert.True(Find<MenuItem>("BlockedMenuItem").IsChecked == true);
    Assert.True(ViewModel.ShowBlocked);
}
```

**Headless modifier limitation (probe-verified):** `RawInputModifiers.Control/Shift` on `MouseDown` and a held `KeyPressQwerty(PhysicalKey.ControlLeft)` both yield `e.KeyModifiers == None` — Ctrl/Shift-click cannot be simulated headless. The window test therefore covers plain-click single-select only; the Ctrl-toggle and Shift-range semantics are proven by `LayerSelectionTests` (pure logic) and the production handler is a thin dispatch onto it. Do not attempt to make the modifier path headless-testable.

- Replace `ToolbarZoomButtons_ExistAndZoomAroundCanvasCenter` (`:391`) with a key-based zoom test: `Window.KeyPressQwerty(PhysicalKey.Add, RawInputModifiers.None)` → `ZoomPercent == 200`, `Window.KeyPressQwerty(PhysicalKey.Subtract, RawInputModifiers.None)` → back to `100`.
- `ShortcutTests.cs:126-127`: `KeyPressQwerty(PhysicalKey.B, …)` → `BlockedToggle` becomes `KeyPressQwerty(PhysicalKey.X, …)` → `BlockedToggle`; add an assert that `PhysicalKey.B` leaves `ActiveTool` unchanged (B is unbound in Part 1; Part 2 binds it to Flood fill).
- `CommandEnablement_FollowsSessionState` (`:281`): drop `undoButton`/`saveButton` references; assert menu-item `IsEnabled` only.
- `NewSmallerMap_WithOutOfRangeSelection_ClearsSelectionWithoutError` (`:333`): `Find<Button>("NewButton").RaiseEvent(...)` → `Find<MenuItem>("NewCommand").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent))`.
- `OpenCommand_UnexpectedExceptionAtWindowBoundary_ShowsLastErrorDialogAndKeepsDocument` (`:364`): same swap to `OpenCommand`.
- Selection readout test (`:240`): `ViewModel.ActiveLayer = 2;` → `ViewModel.SelectedLayers = 1 << 2;` (update the readout assertions accordingly).
- `MainWindowTests` declares `Dispose()` (`:97`) without implementing `IDisposable` (`:75`), so xUnit never calls it — add `: IDisposable` to the class declaration.

**Step 2: Run tests to verify they fail (red)**

Run: `dotnet test tests/MapEditor.App.Tests -v q --nologo`
Expected: compile failure (`LayerSelection` missing) + layout test failures (radios still present, buttons still present).

**Step 3: Implement**

`src/MapEditor.App/LayerSelection.cs` (new, pure logic, no Avalonia dependencies; namespace `MapEditor.App` — the `MainWindow` code-behind is in `MapEditor.App` with no `using MapEditor.App.Views;`, and the test file already imports `MapEditor.App`):

```csharp
using System;
using MapEditor.Core;

namespace MapEditor.App;

public enum LayerClickMode
{
    Plain,
    Toggle,
    Range,
}

internal static class LayerSelection
{
    public static (byte Next, int Anchor) Apply(byte current, int anchor, int layer, LayerClickMode mode)
    {
        byte next = mode switch
        {
            LayerClickMode.Plain => Plain(layer),
            LayerClickMode.Toggle => Toggle(current, layer, keepNonEmpty: true),
            _ => Range(anchor, layer),
        };

        int nextAnchor = mode == LayerClickMode.Range ? anchor : layer;
        return (next, nextAnchor);
    }

    public static byte Plain(int layer)
    {
        Validate(layer);
        return (byte)(1 << layer);
    }

    public static byte Toggle(byte current, int layer, bool keepNonEmpty = false)
    {
        Validate(layer);
        byte next = (byte)(current ^ (1 << layer));
        return keepNonEmpty && next == 0 ? (byte)(1 << layer) : next;
    }

    public static byte Range(int anchor, int layer)
    {
        Validate(anchor);
        Validate(layer);
        int lo = Math.Min(anchor, layer);
        int hi = Math.Max(anchor, layer);
        byte next = 0;
        for (int l = lo; l <= hi; l++)
        {
            next |= (byte)(1 << l);
        }

        return next;
    }

    private static void Validate(int layer)
    {
        if (layer < 0 || layer >= MapDocument.LayerCount)
        {
            throw new ArgumentOutOfRangeException(nameof(layer));
        }
    }
}
```

`MainWindow.axaml`:
- Menu: after `EditMenu`, add:

```xml
<MenuItem x:Name="ViewMenu" Header="_View">
  <MenuItem x:Name="GridMenuItem" Header="Grid" ToggleType="CheckBox" IsChecked="{Binding ShowGrid, Mode=TwoWay}" />
  <MenuItem x:Name="BlockedMenuItem" Header="Blocked" ToggleType="CheckBox" IsChecked="{Binding ShowBlocked, Mode=TwoWay}" />
</MenuItem>
```

(`Mode=TwoWay` is required — probe-verified that the default OneWay binding never writes menu clicks back to the view model.)

- Toolbar: remove `NewButton`, `OpenButton`, `SaveButton`, `UndoButton`, `RedoButton`, `ZoomInButton`, `ZoomOutButton`. Keep the four tool toggles, adding `Unchecked="OnToolUnchecked"` to each (clicking the active tool's toggle can otherwise uncheck it and leave every toggle unchecked while `ActiveTool` is unchanged).
- Right panel: replace the "Active layer" radio group, the "Visibility" checkbox group, `ShowGridCheck`, and `ShowBlockedCheck` with:

```xml
<TextBlock Text="Layers" FontWeight="SemiBold" />
<Border x:Name="Layer0Row" Tag="0" Background="Transparent" PointerPressed="OnLayerRowPressed">
  <Grid ColumnDefinitions="*,Auto">
    <TextBlock x:Name="Layer0Label" Text="0 — Ground" VerticalAlignment="Center" />
    <CheckBox x:Name="Layer0VisibleCheck" Tag="0" VerticalAlignment="Center"
              Checked="OnLayerVisibilityChanged" Unchecked="OnLayerVisibilityChanged" />
  </Grid>
</Border>
<!-- repeat for Layer1Row/Layer1Label/Layer1VisibleCheck "1 — Below Entities" … Layer4Row/Layer4Label/Layer4VisibleCheck "4 — Roof" -->
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

    if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
    {
        return;
    }

    KeyModifiers modifiers = e.KeyModifiers;
    LayerClickMode mode = modifiers.HasFlag(KeyModifiers.Control)
        ? LayerClickMode.Toggle
        : modifiers.HasFlag(KeyModifiers.Shift)
            ? LayerClickMode.Range
            : LayerClickMode.Plain;

    (_viewModel.SelectedLayers, _layerAnchor) = LayerSelection.Apply(_viewModel.SelectedLayers, _layerAnchor, layer, mode);
    e.Handled = true;
}
```

Notes:
- `e.KeyModifiers` comes from `PointerEventArgs` (Avalonia 11.3.20 — `PointerPointProperties` has no `KeyModifiers` member).
- No checkbox guard is needed: probe-verified that a pointer press on a `CheckBox` is handled by `ToggleButton` (`e.Handled = true`), so it never bubbles to the row `Border` — the visibility-checkbox pointer-click test in `Layout_ContainsNamedLayerListAndViewToggles` proves the selection is untouched.
- Anchor semantics (probe-verified pure logic): Plain and Ctrl-toggle re-anchor to the clicked layer; Shift-range keeps the anchor so successive Shift-clicks extend from the original anchor. Ctrl+Shift resolves to Toggle (Ctrl checked first).

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
- Add (paired with the `Unchecked` handlers above):

```csharp
private void OnToolUnchecked(object? sender, RoutedEventArgs e)
{
    if (sender is ToggleButton { Tag: string tag } &&
        Enum.TryParse<MapEditTool>(tag, out MapEditTool tool) &&
        tool == _viewModel.ActiveTool)
    {
        ((ToggleButton)sender).IsChecked = true;
    }
}
```
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
git add src/MapEditor.App tests/MapEditor.App.Tests
git commit -m "feat: named multi-select layer list, View menu, tool-only toolbar"
```

(One commit covers Task 2 + Task 3 — they are a single compilation unit.)

**Invariant-to-test matrix:**

| Invariant | Proved by |
|-----------|-----------|
| Plain click single-selects (window level) | `Layout_ContainsNamedLayerListAndViewToggles` |
| Selected row is highlighted; deselected rows are not | `Layout_ContainsNamedLayerListAndViewToggles` (row `Background` assertions) |
| Ctrl toggles; Shift ranges from anchor (logic level; headless cannot simulate modifiers) | `LayerSelectionTests` |
| Selection never empties via Ctrl toggle (adversarial) | `Toggle_OffLastSelectedLayer_SingleSelectsIt` |
| Visibility checkbox drives `LayerVisibility` mask without touching selection | `Layout_ContainsNamedLayerListAndViewToggles` (checkbox uncheck leaves `SelectedLayers` unchanged) |
| Grid/Blocked toggled from View menu, defaults on/off | `Layout_ContainsNamedLayerListAndViewToggles` |
| File/zoom toolbar buttons gone (asserted absent); menu items remain | rewritten `Layout_ContainsFileEditToolbarCommands` |
| Zoom still works via +/- keys after button removal | rewritten zoom test |

---

## Final verification (after all tasks)

Run: `dotnet test tests/MapEditor.Core.Tests -v q --nologo && dotnet test tests/MapEditor.Rendering.Tests -v q --nologo && dotnet test tests/MapEditor.App.Tests -v q --nologo`
Expected: all green (baseline totals 168/163/199, adjusted for rewritten tests).

Manual smoke (optional, headless environment may not allow): `./build-map-editor.sh` or run `src/MapEditor.App` — verify the layer list, View menu, and toolbar render as designed.

## Design alignment notes

- Layer names exactly: Ground / Below Entities / Entities / Above Entities / Roof, displayed as "0 — Ground" etc. (names live in the XAML rows and the test's `names` array; the design doc's "shared constant array" wording is updated to match).
- Selected-tile readout keeps `L0 …` index format (unchanged).
- Toolbar order for Part 1: Pencil, Eraser, Eyedropper, Blocked (Part 2 appends Select, Multi-select, Flood fill).
- Hotkey change: Blocked B → X (B reserved for Part 2 flood fill); `ShortcutTests.cs:126-127` updated accordingly.
- Ctrl/Shift-click is not headless-testable (probe-verified); coverage strategy: plain-click window test + `LayerSelection` unit tests.
- No comments/doc strings beyond the non-obvious-invariant comments shown in the proposed code (AGENTS.md default: none).
