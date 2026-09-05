# Map Editor Tabs — Part 2: Tab Strip Implementation Plan

**Goal:** Expose the workspace built in Part 1 as a tab strip with per-tab close, keyboard shortcuts, drag-reorder, and a quit flow that prompts for each dirty map.

**Architecture:** An `ItemsControl` bound to `WorkspaceViewModel.Documents` docks full width under the menu; each item is a tab header carrying the map name, a dirty dot, a close button, and drag/middle-click handling. Tab commands route through `WorkspaceViewModel.CloseAsync` / `ActiveDocument` / `Move`, so the UI adds presentation and input only. A single `_commandRunning` flag on `MainWindow` serialises modal commands against tab input.

**Tech Stack:** C#, .NET 10, Avalonia 11 (`ItemsControl` + `DataTemplate`, `ScrollViewer`), xUnit with `Avalonia.Headless.XUnit`.

**Prerequisite:** Part 1 (`docs/plans/2026-09-05-map-editor-tabs-part1-workspace.md`) is merged. This plan assumes `WorkspaceViewModel`, `MapDocumentViewModel`, `SharedTileClipboard`, per-document canvas/palette bundles and `MainWindow.ActivateDocument` all exist.

**Design doc:** `docs/plans/2026-09-05-map-editor-tabs-design.md`

---

## APIs verified

| API | Declaration |
|-----|-------------|
| `Root` `DockPanel`, `Menu` docked top, `StatusBar` docked bottom, `Body` grid | `src/MapEditor.App/Views/MainWindow.axaml:8-40` |
| `CanvasHost` / `PaletteBorder` / `PaletteBar` hosts | `src/MapEditor.App/Views/MainWindow.axaml:63,66,89` |
| Toolbar `Border.card` + `ToggleButton.tool` pattern to imitate | `src/MapEditor.App/Views/MainWindow.axaml:70-83` |
| Style classes available: `chrome`, `card`, `row`, `row.last`, `sunken`, `panel`, `muted`, `mono`, `section`, `tool`, `toolSeparator`, `ScrollBar.overlay` | `src/MapEditor.App/Styles/EditorTheme.axaml:85-248` |
| Icon geometries live as `StreamGeometry` resources | `src/MapEditor.App/Styles/Icons.axaml:9-17` |
| `MainWindow.OnKeyDown` shortcut switch, `PrimaryModifier`, `IsPrimaryModifier` | `src/MapEditor.App/Views/MainWindow.axaml.cs:105-231` |
| Tunnelling handler precedent (`AddHandler(PointerPressedEvent, …, RoutingStrategies.Tunnel)`) | `src/MapEditor.App/Views/MainWindow.axaml.cs:69` |
| `MainWindow.OnClosing` guards and cancel-then-re-`Close()` | `src/MapEditor.App/Views/MainWindow.axaml.cs:474-510` |
| `RunCommandAsync` (currently fire-and-forget) | `src/MapEditor.App/Views/MainWindow.axaml.cs:512-527` |
| `OnWindowPointerPressed` paste-mode cancel (must not fire for tab clicks) | `src/MapEditor.App/Views/MainWindow.axaml.cs:299-305` |
| `BuildShortcut(Key, KeyModifiers, bool)` | `src/MapEditor.App/Views/MainWindow.axaml.cs:103` |
| Headless input helpers `KeyPressQwerty`, `MouseDown`, `MouseUp` | used in `tests/MapEditor.App.Tests/ShortcutTests.cs:23-34` |
| `MainWindowHarness` (gains `Workspace` in Part 1) | `tests/MapEditor.App.Tests/MainWindowTests.cs:29-75` |

---

### Task 1: Tab strip markup and styles

**Files:**
- Modify: `src/MapEditor.App/Views/MainWindow.axaml` (new `TabStrip` between `Menu` and `StatusBar` in `Root`), `src/MapEditor.App/Styles/EditorTheme.axaml`, `src/MapEditor.App/Styles/Icons.axaml` (an `IconClose` glyph, matching the existing 24×24 geometries)
- Modify: `src/MapEditor.App/ViewModels/MapDocumentViewModel.cs`
- Test: `tests/MapEditor.App.Tests/TabStripTests.cs` (new)

**Mutation impact:**
- Source of truth: tab labels derive from `EditorDocument.Path` and dirty state from `MapEditSession.IsDirty` (`src/MapEditor.Core/Editing/MapEditSession.cs:81`) — both already owned per document.
- New readers: the header template binds `TabTitle`, `IsDirty` and `TabToolTip`.
- Derived state: `MapDocumentViewModel` already computes `Title` (window title, `"Goose2 Map Editor — {name}{*}"`). Add `TabTitle` (bare file name, or `"Untitled"`) and `TabToolTip` (full path, or `"Untitled"`) rather than parsing the window title — they are separate presentations of the same source.
- Propagation: `Refresh(EditorRefresh.Title)` already re-reads the path and raises `Title`; it must also raise `TabTitle`, `TabToolTip` and `IsDirty`. `Refresh(EditorRefresh.Commands)` recomputes `CanSave` from `Session.IsDirty` and must raise `IsDirty` too — this is the notification that makes the dirty dot appear on the first edit, and it is the easy thing to miss. `MapDocumentViewModel.IsDirty` is a new pass-through property (`Session.IsDirty`) so the template has something to bind.
- Invariant: the dot is visible exactly when that document's session is dirty, for every tab at once — not only the active one.
- Observable proof: assert the rendered header's dot visibility per tab, not that `Refresh` was called.

**Steps:**
1. Red: `TabStripTests` — `TabStrip_ShowsOneHeaderPerDocument`; `TabStrip_HeaderShowsFileName` after opening a saved map; `TabStrip_DirtyDot_AppearsOnEditAndClearsOnSave`; `TabStrip_InactiveTabDirtyDot_Updates` (edit tab A, activate B, assert A's header still shows the dot) — adversarial against wiring notifications only through the active document; `TabStrip_ActiveHeader_IsHighlighted`.
2. Implement: an `ItemsControl` (inside a horizontal `ScrollViewer`, `HorizontalScrollBarVisibility="Auto"`) with `ItemsSource="{Binding Documents}"`, an `ItemsPanel` of `StackPanel Orientation="Horizontal"`, and an `ItemTemplate` of `Border Classes="tab"` containing the label, an `Ellipse` dirty dot bound to `IsDirty`, and a close `Button`. Tabs get `MinWidth`/`MaxWidth` so many tabs shrink and then scroll. Add `Border.tab`, `Border.tab.active` styles beside the existing `Border.row` rules.
3. The strip's `DataContext` is the workspace; each item's is a `MapDocumentViewModel`. `MainWindow`'s own `DataContext` stays the active document (Part 1), so bind the strip's `ItemsSource` through an explicit named-control reference rather than inheriting `DataContext` — set `TabStrip.ItemsSource = Workspace.Documents` in the constructor instead of a XAML binding, and mark the active tab from `ActivateDocument`.
4. Green, then `dotnet test tests/MapEditor.App.Tests`.
5. Commit: `feat: show a tab strip for open maps`.

---

### Task 2: Activate and close tabs by pointer

**Files:**
- Modify: `src/MapEditor.App/Views/MainWindow.axaml.cs`
- Test: `tests/MapEditor.App.Tests/TabStripTests.cs`

Left-click on a header sets `Workspace.ActiveDocument`; the close button and middle-click both call `Workspace.CloseAsync(document)` through `RunCommandAsync` (Task 5 guards it).

`OnWindowPointerPressed` (`:299-305`) cancels paste mode for any press whose source is not the `MapCanvas` — a tab click therefore cancels an armed paste. That is correct and worth a test: arming paste in tab A and clicking tab B must leave A's paste mode off, since the ghost would otherwise be stranded on a hidden map.

**Tests:** `Tab_LeftClick_ActivatesDocument`; `Tab_CloseButton_ClosesDocument`; `Tab_MiddleClick_ClosesDocument`; `Tab_CloseDirty_PromptsAndCancelKeepsTab` (via `FakeEditorDialogs.DirtyResult`); `Tab_CloseLastTab_LeavesFreshUntitled`; `Tab_Click_CancelsArmedPasteMode` — adversarial.

Commit: `feat: activate and close tabs with the pointer`.

---

### Task 3: Tab keyboard shortcuts

**Files:**
- Modify: `src/MapEditor.App/Views/MainWindow.axaml.cs:105-231`
- Test: `tests/MapEditor.App.Tests/ShortcutTests.cs`

Ctrl/Cmd+T new tab (`Workspace.NewAsync`), Ctrl+W close active, Ctrl+Tab / Ctrl+Shift+Tab cycle with wrapping, Ctrl+1..8 by index (no-op past the end), Ctrl+9 to the last tab.

**Threading/lifecycle note:** the Ctrl+Tab case must not reach `Window.OnKeyDown` after the focus manager has consumed Tab for directional navigation. Register the tab shortcuts in the constructor as
`AddHandler(InputElement.KeyDownEvent, OnTabShortcutKeyDown, RoutingStrategies.Tunnel)`, mirroring the existing tunnelling pointer handler at `:69`, and set `e.Handled` there. Do not add these cases to the bubbling `OnKeyDown` switch and hope. All of this runs on the UI thread; `NewAsync`/`CloseAsync` are awaited through `RunCommandAsync` as the menu handlers already are.

The plain-key branch of `OnKeyDown` returns early when `e.Source is TextBox` (`:180-183`); the tunnelling handler only fires on the primary modifier, so brush-field typing is unaffected. Add a test for that anyway.

**Tests:** `ControlT_AddsTab`; `ControlW_ClosesActiveTab`; `ControlTab_CyclesForwardWithWrap`; `ControlShiftTab_CyclesBackwardWithWrap`; `Control3_ActivatesThirdTab`; `Control9_ActivatesLastTab`; `Control5_WithThreeTabs_DoesNothing`; `ControlTab_WithFocusInBrushField_StillSwitchesTabs` — adversarial against the focus-manager conflict, and the one most likely to go red first.

Commit: `feat: add tab keyboard shortcuts`.

---

### Task 4: Drag to reorder

**Files:**
- Modify: `src/MapEditor.App/Views/MainWindow.axaml.cs` (or a small `TabStripDrag` helper beside `src/MapEditor.App/Controls/RectDrag.cs` if the window file grows unwieldy)
- Test: `tests/MapEditor.App.Tests/TabStripTests.cs`

Press on a header records the document and its index; once the pointer passes a neighbour's midpoint, call `Workspace.Move(from, to)`; release ends the drag; Escape during a drag restores the original order. Reordering never changes `ActiveDocument` (Part 1's `Move` guarantees this; assert it here at the UI level too). A drag that never crosses a midpoint is an ordinary click, so activation still happens on release.

**Tests:** `Drag_PastNeighbourMidpoint_ReordersTabs`; `Drag_DoesNotChangeActiveDocument`; `Drag_EscapeCancels_RestoresOriginalOrder`; `Drag_ShortPress_StillActivatesTab` — adversarial against a drag handler that swallows plain clicks.

Commit: `feat: reorder tabs by dragging`.

---

### Task 5: Serialise modal commands with `_commandRunning`

**Files:**
- Modify: `src/MapEditor.App/Views/MainWindow.axaml.cs:512-527`
- Test: `tests/MapEditor.App.Tests/MainWindowTests.cs`

**Mutation impact:**
- Source of truth changed: a new `bool _commandRunning` on `MainWindow` guards entry to `RunCommandAsync`.
- Important readers: every menu handler (`OnNew`, `OnOpen`, `OnSave`, `OnSaveAs`, `OnResize`, `OnLoadAssets`), the tab shortcuts, tab activation and tab close.
- Derived state: none, but the flag interacts with `_closeGuardRunning` (`:474-510`), which stays independent — quitting must still work while nothing else is running, and `OnClosing` sets its own guard.
- Propagation: set the flag before awaiting, clear it in a `finally` so a thrown dialog cannot wedge the window (`ShowFatalErrorAsync` already swallows secondary failures at `:529-545`). While set, tab activation, tab close and file commands return without acting.
- Invariant: a Save-As picker opened for one document can never write after the user has activated another — the picker's continuation still holds its own `MapDocumentViewModel`, and no activation could have happened while it was open.
- Observable proof: a test using `FakeEditorDialogs.DirtyGate` (`Fakes/FakeEditorDialogs.cs:28`) to hold a prompt open, then attempting a tab switch, then releasing the gate.

**Tests:** `TabSwitch_WhileDialogOpen_IsIgnored` — adversarial, fails today; `Command_ThatThrows_ClearsTheRunningFlag` (use `FakeEditorDialogs.PickOpenException`, then assert a following command still runs).

Commit: `fix: serialise modal editor commands against tab input`.

---

### Task 6: Quit with several dirty maps

**Files:**
- Modify: `src/MapEditor.App/Views/MainWindow.axaml.cs:474-510`
- Test: `tests/MapEditor.App.Tests/MainWindowCloseTests.cs`

`OnClosing` keeps its shape — cancel the event, run the guard, re-`Close()` on approval — and swaps `_viewModel.RequestCloseAsync()` for `Workspace.CloseAllAsync()`, which walks `Documents` in order, activating and prompting each dirty document (Part 1, Task 5). Before the walk, call `FinishInteraction(commit: true)` on the active canvas, as `:476` already does.

`CloseAllAsync` activating each document means the window's `ActiveDocument` subscription runs `ActivateDocument` mid-quit; that is the intended behaviour (the user sees the map being asked about) and is safe because activation is synchronous and the `_commandRunning` guard is not held by `OnClosing`. Verify with the cancel test that the cancelled-on document is the one left active and visible.

**Tests:** `Close_TwoDirtyTabs_PromptsTwice`; `Close_CancelOnSecondTab_AbortsQuitAndLeavesItActive` (assert the window is still visible, `Documents.Count` unchanged, and `Workspace.ActiveDocument` is the second document) — adversarial; `Close_AllClean_ClosesWithoutPrompt` (the existing case, still passing).

Commit: `feat: prompt for each dirty map when quitting`.

---

### Task 7: Full suite, smoke, and docs

**Steps:**
1. `dotnet test tests/MapEditor.Core.Tests`, `dotnet test tests/MapEditor.Rendering.Tests`, `dotnet test tests/MapEditor.App.Tests` — one project per command (`dotnet test` rejects multiple paths with MSB1008).
2. `./build-map-editor.sh --skip-tests linux-x64`.
3. Update `docs/map-editor-smoke.md` with a tabs pass: open three maps; edit one and confirm only its dot appears; copy in one and paste in another; reorder by dragging; Ctrl+Tab through them; close a dirty tab and cancel, then discard; close the last tab and confirm a fresh Untitled appears; quit with two dirty maps and cancel on the second.
4. Run that smoke pass by hand.
5. Commit: `docs: add a tabs pass to the map editor smoke test`.
