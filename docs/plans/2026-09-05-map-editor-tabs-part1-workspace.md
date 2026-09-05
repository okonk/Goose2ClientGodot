# Map Editor Tabs — Part 1: Workspace Model Implementation Plan

**Goal:** Hold several open maps in a `WorkspaceViewModel` with one view model, canvas and palette per map, a clipboard shared across maps, and window-side activation that swaps between them — with no tab strip yet.

**Architecture:** `MainWindowViewModel` becomes `MapDocumentViewModel`, one instance per open map, owning its own `EditorDocumentController`. A new `WorkspaceViewModel` owns the document collection, the active document and a `SharedTileClipboard`. `MainWindow` keeps a `Dictionary<MapDocumentViewModel, DocumentView>` of canvas + palette controls and gains `ActivateDocument`, which swaps the hosted controls and rebinds the chrome. Part 2 adds the tab strip, shortcuts and close/quit flows on top.

**Tech Stack:** C#, .NET 10 (`MapEditor.App`) / .NET 8 (`MapEditor.Core`), Avalonia 11, xUnit with `Avalonia.Headless.XUnit`.

**Design doc:** `docs/plans/2026-09-05-map-editor-tabs-design.md`

---

## APIs verified

| API | Declaration |
|-----|-------------|
| `MapEditSession.Document` | `src/MapEditor.Core/Editing/MapEditSession.cs:31` |
| `MapEditSession.SelectedLayers` | `src/MapEditor.Core/Editing/MapEditSession.cs:37` |
| `MapEditSession.TopLayer` | `src/MapEditor.Core/Editing/MapEditSession.cs:51` |
| `MapEditSession.SelectedTileLayer` | `src/MapEditor.Core/Editing/MapEditSession.cs:67` |
| `MapEditSession.Resized` (`event Action<MapResizeTransform>?`) | `src/MapEditor.Core/Editing/MapEditSession.cs:75` |
| `MapEditSession.IsDirty` | `src/MapEditor.Core/Editing/MapEditSession.cs:81` |
| `MapEditSession.ApplyLayerPatch(int, int, int, int, MapTileLayer[]?[])` | `src/MapEditor.Core/Editing/MapEditSession.cs:219` |
| `MapEditSession.MarkSaved()` | `src/MapEditor.Core/Editing/MapEditSession.cs:439` |
| `MapFileStore.Open(string) : OpenedMap` | `src/MapEditor.Core/MapFileStore.cs:23` |
| `MapFileStore.Save(string, MapDocument, MapFileRevision?) : MapFileRevision` | `src/MapEditor.Core/MapFileStore.cs:31` |
| `IEditorDialogs` (`ShowNewMapAsync`, `ShowDirtyAsync(string)`, `PickOpenMapAsync`, `PickSaveMapAsync(string)`, `ShowErrorAsync(ErrorPresentation)`) | `src/MapEditor.App/Dialogs/IEditorDialogs.cs:24-41` |
| `DirtyChoice` = `Save` \| `Discard` \| `Cancel` | `src/MapEditor.App/Dialogs/IEditorDialogs.cs:7-12` |
| `TileClipboard.Capture(MapDocument, byte, MapTileRectangle)` | `src/MapEditor.App/ViewModels/TileClipboard.cs:24` |
| `ViewModelBase.SetField<T>` / `OnPropertyChanged` | `src/MapEditor.App/ViewModels/ViewModelBase.cs:12-27` |
| `MapCanvas(MainWindowViewModel, AssetContextController)` | `src/MapEditor.App/Controls/MapCanvas.cs:29` |
| `MapCanvas.OnAttachedToVisualTree` / `OnDetachedFromVisualTree` hover hooks | `src/MapEditor.App/Controls/MapCanvas.cs:168-186` |
| `MapCanvas.OnCanvasInvalidated` session-swap reset | `src/MapEditor.App/Controls/MapCanvas.cs:466-481` |
| `SpritePaletteControl(MainWindowViewModel, AssetContextController)` | `src/MapEditor.App/Controls/SpritePaletteControl.cs:35` |
| `SpritePaletteControl.BindScrollBar(ScrollBar)` (unsubscribes the previous bar) | `src/MapEditor.App/Controls/SpritePaletteControl.cs:77` |
| `AssetContextController.TryOpen(string, out Exception?)` pushes sheet ids into the view model | `src/MapEditor.App/Rendering/AssetContextController.cs:33,63-66` |
| `LayerSelection.Apply(byte, int, int, LayerClickMode)` | `src/MapEditor.App/LayerSelection.cs` |
| `MainWindowHarness` test fixture | `tests/MapEditor.App.Tests/MainWindowTests.cs:29-75` |
| `FakeEditorDialogs` (`DirtyResult`, `DirtyShown`, `OpenPickResult`, `SavePickResult`, `NewMapResult`, `Errors`) | `tests/MapEditor.App.Tests/Fakes/FakeEditorDialogs.cs:11-36` |

Test commands throughout: `dotnet test tests/MapEditor.App.Tests` and `dotnet test tests/MapEditor.Core.Tests`. `dotnet test` rejects multiple project paths in one invocation (MSB1008), so run one project per command.

---

### Task 1: Rename `MainWindowViewModel` to `MapDocumentViewModel`

Pure mechanical rename, committed alone so the behavioural diffs that follow stay readable.

**Files:**
- Rename: `src/MapEditor.App/ViewModels/MainWindowViewModel.cs` → `MapDocumentViewModel.cs` (and the sidecar `.uid` if present)
- Modify: `src/MapEditor.App/Controls/MapCanvas.cs`, `src/MapEditor.App/Controls/SpritePaletteControl.cs`, `src/MapEditor.App/Views/MainWindow.axaml.cs`, `src/MapEditor.App/Rendering/AssetContextController.cs`, `src/MapEditor.App/App.axaml.cs`
- Rename: `tests/MapEditor.App.Tests/MainWindowViewModelTests.cs` → `MapDocumentViewModelTests.cs` (class `MapDocumentViewModelTests`)
- Modify: `tests/MapEditor.App.Tests/{MapCanvasTests,SpritePaletteControlTests,MainWindowTests,MainWindowThemeTests,AvaloniaMapDrawSinkTests,AssetContextControllerTests}.cs`

`ComposedEditor.ViewModel` (`App.axaml.cs`) keeps its name for now; its type changes. 56 references across 13 files — `git grep -l MainWindowViewModel` finds them all.

**Steps:**
1. Rename the class and file; update every reference (`git mv` the file so history follows).
2. `dotnet test tests/MapEditor.App.Tests` — expect the same 302 passing.
3. Commit: `refactor: rename MainWindowViewModel to MapDocumentViewModel`.

No test changes beyond the type name: this task must not alter behaviour.

---

### Task 2: `SharedTileClipboard` and cross-map paste

This is the copy/paste-across-maps feature. `TileClipboard` already holds raw `MapTileLayer[]` with no reference to its source document, and `ApplyPasteAt` already clips to the destination and pairs layers top-down against the destination's `SelectedLayers` — so moving the clipboard out of the document view model is the entire change.

**Files:**
- Create: `src/MapEditor.App/ViewModels/SharedTileClipboard.cs`
- Modify: `src/MapEditor.App/ViewModels/MapDocumentViewModel.cs:38,229,247,263,304,322,434`
- Test: `tests/MapEditor.App.Tests/CrossMapPasteTests.cs` (new), `tests/MapEditor.App.Tests/MapDocumentViewModelTests.cs`

**Mutation impact:**
- Source of truth changed: the clipboard slot moves from `MapDocumentViewModel._clipboard` (`MapDocumentViewModel.cs:38`) to a `SharedTileClipboard` instance owned by the workspace and injected into every document view model.
- Important readers: `Clipboard` property (`:229`), `PasteGhost` (`:247`), `BeginPasteMode` (`:304`), `ApplyPasteAt` (`:322`).
- Derived/cached state affected: `PasteGhost` is computed per call from `Clipboard` + `HoverX/HoverY`; no cached copy exists. The only stale-state writer is `Refresh`'s document branch (`:434`), which nulls the clipboard on document swap — deleted here, since it would otherwise wipe a clipboard belonging to every other tab.
- Required propagation sequence for `CopySelection`: `TileClipboard.Capture(...)` → assign `_clipboard.Current` → `SharedTileClipboard` raises `Changed` → each subscribed document view model raises `OnPropertyChanged(nameof(Clipboard))`. Paste enablement in Part 2's UI reads that notification; nothing in Part 1 depends on it, but the event is needed for the tab strip later and is cheap to add now.
- Invariants to preserve: capture reads the *source* document's dimensions and `SelectedLayers`; paste clips to the *destination* document and pairs layers against the *destination's* `SelectedLayers`; a paste that falls entirely outside the destination applies no patch and leaves the destination clean.
- Observable proof: tests assert destination tile values and destination `IsDirty`, not that `ApplyLayerPatch` was called.

**Step 1: Write the failing tests**

`SharedTileClipboard` is small enough to give in full:

```csharp
internal sealed class SharedTileClipboard
{
    private TileClipboard? _current;

    public TileClipboard? Current
    {
        get => _current;
        set
        {
            _current = value;
            Changed?.Invoke();
        }
    }

    public event Action? Changed;
}
```

`CrossMapPasteTests` builds two `MapDocumentViewModel`s over separate `MapEditSession`s sharing one `SharedTileClipboard`. Required cases, the last three adversarial:

- `Paste_IntoOtherDocument_CopiesTiles` — copy a 2×2 region of set tiles from A, paste at (0,0) in B, assert B's four tiles equal the source values and `B.Session.IsDirty` is true.
- `Paste_IntoOtherDocument_LeavesSourceUnchanged` — assert A's tiles and `A.Session.IsDirty` are untouched by the paste.
- `Paste_IntoSmallerDocument_ClipsToDestination` — B is 3×3, paste a 4×4 clip at (2,2); assert only B[2,2] is written and no exception escapes.
- `Paste_WhollyOutsideDestination_LeavesDocumentClean` — paste at (10,10) into a 3×3 B; assert `B.Session.IsDirty` is false. This fails on the likely wrong implementation that patches before clipping.
- `Paste_WithDifferentSelectedLayers_PairsTopDown` — capture from A with layers {0,1} selected, paste into B with layers {3,4} selected; assert A's layer 1 lands in B's layer 4 and A's layer 0 in B's layer 3 (top-down pairing), and that B's layers 0-2 are untouched.
- `Copy_InOneDocument_IsVisibleToTheOther` — after `A.CopySelection()`, assert `B.Clipboard` is the same instance.

Also add to `MapDocumentViewModelTests`: `Refresh_Commands_DoesNotClearClipboard` — copy, then `Refresh(EditorRefresh.Commands | EditorRefresh.Title)`, assert `Clipboard` survives.

**Step 2: Run to verify red** — `dotnet test tests/MapEditor.App.Tests --filter CrossMapPasteTests`. Expect compile failure: no `SharedTileClipboard`, and the view model constructor takes no clipboard.

**Step 3: Implement** — add `SharedTileClipboard`; give `MapDocumentViewModel` a `SharedTileClipboard` constructor parameter; replace the `_clipboard` field with `_clipboard.Current`; subscribe to `Changed` and raise `OnPropertyChanged(nameof(Clipboard))`; delete the `_clipboard = null;` line in `Refresh`.

**Step 4: Green** — the filtered run passes, then `dotnet test tests/MapEditor.App.Tests` for the whole project.

**Step 5: Commit** — `feat: share the tile clipboard across map documents`.

| Invariant | Proved by |
|-----------|-----------|
| Paste clips to the destination's bounds | `Paste_IntoSmallerDocument_ClipsToDestination` |
| A fully out-of-bounds paste is a no-op | `Paste_WhollyOutsideDestination_LeavesDocumentClean` |
| Layers pair top-down against the destination's selection | `Paste_WithDifferentSelectedLayers_PairsTopDown` |
| Copy in one document is visible in another | `Copy_InOneDocument_IsVisibleToTheOther` |
| Refreshing one document does not wipe a shared clipboard | `Refresh_Commands_DoesNotClearClipboard` |

---

### Task 3: Move the layer anchor into the document view model

**Files:**
- Modify: `src/MapEditor.App/ViewModels/MapDocumentViewModel.cs`, `src/MapEditor.App/Views/MainWindow.axaml.cs:41,102,328,561-563`
- Test: `tests/MapEditor.App.Tests/MapDocumentViewModelTests.cs`

**Mutation impact:**
- Source of truth changed: `MainWindow._layerAnchor` (`MainWindow.axaml.cs:41`) moves to `MapDocumentViewModel.LayerAnchor`.
- Important readers: `OnLayerRowPressed` (`:328`, passes it to `LayerSelection.Apply`), the `SelectedLayers` case of `OnViewModelPropertyChanged` (`:561-563`, resets it to `TopLayer` when the anchor leaves the selection), and `MainWindow.LayerAnchor` (`:102`, read by tests).
- Derived state: none — `LayerSelection.Apply` is pure.
- Propagation: `OnLayerRowPressed` assigns `(_viewModel.SelectedLayers, _viewModel.LayerAnchor)`; the reset in `OnViewModelPropertyChanged` writes `_viewModel.LayerAnchor`. `MainWindow.LayerAnchor` becomes `=> _document.LayerAnchor` so the existing `LayerSelectionTests`/`MainWindowTests` assertions keep working.
- Invariant: each document's shift-click range extends from that document's own anchor, never another document's.

**Steps:** add `internal int LayerAnchor { get; set; }` to the view model (a plain property — no notification, nothing binds to it); repoint the three window sites; add a view-model test asserting two view models keep independent anchors. Run `dotnet test tests/MapEditor.App.Tests`. Commit: `refactor: move the layer anchor into the document view model`.

---

### Task 4: Reduce `EditorDocumentController` to one document

**Files:**
- Modify: `src/MapEditor.App/Documents/EditorDocumentController.cs`
- Test: `tests/MapEditor.App.Tests/EditorDocumentControllerTests.cs`

The controller stops being able to replace its document. Removed: `NewAsync` (`:32`), `OpenAsync` (`:63`), `ResolveDirtyAsync` (`:242`), and the `_closeGuardRunning` / `_closeApproved` fields (`:18-19`) with the `RequestCloseAsync` wrapper (`:188-200`). `DescribeFormatError` (`:310`) moves to `WorkspaceViewModel` in Task 5 — move it rather than duplicating it, so open-error text stays identical.

New shape:

```csharp
internal EditorDocumentController(IEditorDialogs dialogs, MapFileStore store, EditorDocument initial);
internal EditorDocument Document { get; }
internal event Action? StateChanged;
internal Task SaveAsync();
internal Task SaveAsAsync();
internal bool Undo();
internal bool Redo();
internal Task<bool> ConfirmCloseAsync();   // was RequestCloseCoreAsync's body, minus the guards
```

**Contract for `ConfirmCloseAsync`:** returns true when the document may be discarded. Preconditions: caller (the workspace, or the window during quit) owns re-entrancy — this method has no internal guard and must not be called concurrently with itself. Postconditions: on `DirtyChoice.Save` it awaits `SaveAsync` and returns false if the document is still dirty afterwards (cancelled picker, failed write); on `Discard` returns true without saving; on `Cancel` returns false. It does not remove anything from the workspace and fires no events — closing is the caller's job.

Taking the initial `EditorDocument` as a constructor argument is what lets the workspace build a controller per opened file; `SaveCoreAsync` (`:263`) keeps replacing `_current` in place, which is still correct — that swaps path and revision, never the session.

**Steps:** delete the moved members; add the constructor parameter; rename `RequestCloseAsync`/`RequestCloseCoreAsync` to `ConfirmCloseAsync` without the guards. Update `EditorDocumentControllerTests` — delete the New/Open cases (they move to `WorkspaceViewModelTests` in Task 5) and keep every Save, Save-As, external-change and dirty-prompt case. `MainWindow`'s `OnClosing` still calls `_viewModel.RequestCloseAsync()`; keep that compiling by having `MapDocumentViewModel.RequestCloseAsync()` delegate to `ConfirmCloseAsync` for now — Task 7 rewires it to the workspace. Run `dotnet test tests/MapEditor.App.Tests`. Commit: `refactor: scope EditorDocumentController to a single document`.

---

### Task 5: `WorkspaceViewModel`

**Files:**
- Create: `src/MapEditor.App/ViewModels/WorkspaceViewModel.cs`
- Test: `tests/MapEditor.App.Tests/WorkspaceViewModelTests.cs` (new)

**Publication boundary.** `Documents` is the shared registry other code observes (Part 2's tab strip binds to it; Task 6's asset controller subscribes to it). Creation order for open: pick path → `MapFileStore.Open` → construct `EditorDocument` + `EditorDocumentController` + `MapDocumentViewModel` → **add to `Documents`** (publication point) → set `ActiveDocument`. A failed open throws before the add, so no partial entry is ever visible. Teardown order for close: confirm via `ConfirmCloseAsync` → remove from `Documents` → set `ActiveDocument` to the neighbour. Readers observe an added document before it is active, which is safe: `MainWindow` creates its canvas on the collection-changed event and only hosts it when activation follows.

**Surface:**

```csharp
internal sealed class WorkspaceViewModel : ViewModelBase
{
    internal WorkspaceViewModel(IEditorDialogs dialogs, MapFileStore store);
    internal ObservableCollection<MapDocumentViewModel> Documents { get; }
    internal MapDocumentViewModel ActiveDocument { get; set; }   // raises PropertyChanged
    internal SharedTileClipboard Clipboard { get; }
    internal Task NewAsync();
    internal Task OpenAsync();
    internal Task<bool> CloseAsync(MapDocumentViewModel document);
    internal Task<bool> CloseAllAsync();
    internal void Move(int fromIndex, int toIndex);
}
```

The constructor seeds one clean Untitled document (`MapDocument.Create()`, `initiallyDirty: false`) and makes it active, matching today's `EditorDocumentController` constructor.

Behaviour:
- `NewAsync` — `ShowNewMapAsync`; on null or out-of-range dimensions (`MapDocument.MinDimension`/`MaxDimension`, as in the current `NewAsync:37-42`) return without adding; otherwise add a tab and activate it. No dirty prompt: nothing is being replaced.
- `OpenAsync` — `PickOpenMapAsync`; if the full path matches an existing document's `Path` (using the platform comparison at `EditorDocumentController.cs:11-13`), activate that document and return without re-reading the file. Otherwise `MapFileStore.Open`; on `MapFormatException` / `MapValidationException` / `IOException` / `UnauthorizedAccessException` / `PathTooLongException` show the same messages the controller used and add no document.
- `CloseAsync` — `ConfirmCloseAsync` first; on false return false unchanged. On true, remove the document. If it was the last one, add a fresh clean Untitled and activate it. Otherwise activate the neighbour at the same index, or the new last document when the closed one was last. Returns true.
- `CloseAllAsync` — walk `Documents` in order; activate each dirty document before prompting (so the window shows the map being asked about); return false on the first refusal, leaving that document active. Used by Part 2's quit path.
- `Move` — reorder within `Documents` without touching `ActiveDocument`.

**Tests** (`FakeEditorDialogs` for every prompt; real `MapFileStore` writing into a temp directory, as `EditorDocumentControllerTests` already does):

- `New_AddsTabAndActivatesIt`; `New_CancelledDialog_AddsNothing`; `New_DoesNotPromptWhenCurrentIsDirty` (assert `Dialogs.DirtyShown == 0`) — adversarial against carrying the old replace-semantics prompt over.
- `Open_AddsTabAndActivatesIt`; `Open_AlreadyOpenPath_ActivatesExistingTabWithoutDuplicating` (assert `Documents.Count == 1` and the same instance is active) — adversarial.
- `Open_InvalidFile_AddsNoTabAndReportsError` (assert `Documents.Count` unchanged and `Dialogs.Errors` has one entry) — proves the publication boundary.
- `Close_CleanDocument_ActivatesRightNeighbour`; `Close_LastInStrip_ActivatesNewLast`.
- `Close_OnlyDocument_LeavesFreshUntitled` — assert `Documents.Count == 1`, not dirty, `Path` null, and *not* the same instance as the closed one.
- `Close_DirtyDocument_CancelKeepsIt`; `Close_DirtyDocument_DiscardDropsIt`; `Close_DirtySaveThatFails_KeepsDocument` (set `SavePickResult = null` so Save-As cancels; assert the document is still in `Documents`) — adversarial against a close path that trusts the choice instead of re-checking `IsDirty`.
- `CloseAll_CancelOnSecond_StopsAndLeavesItActive`.
- `Move_DoesNotChangeActiveDocument`.

Red first (the class does not exist), then implement, then `dotnet test tests/MapEditor.App.Tests`. Commit: `feat: add WorkspaceViewModel for multiple open maps`.

| Invariant | Proved by |
|-----------|-----------|
| A failed open publishes no document | `Open_InvalidFile_AddsNoTabAndReportsError` |
| The same file never opens twice | `Open_AlreadyOpenPath_ActivatesExistingTabWithoutDuplicating` |
| The workspace is never empty | `Close_OnlyDocument_LeavesFreshUntitled` |
| A close whose save failed does not drop the document | `Close_DirtySaveThatFails_KeepsDocument` |
| New/Open never prompt about the current document | `New_DoesNotPromptWhenCurrentIsDirty` |
| Reordering does not change activation | `Move_DoesNotChangeActiveDocument` |

---

### Task 6: Make `AssetContextController` workspace-aware

**Files:**
- Modify: `src/MapEditor.App/Rendering/AssetContextController.cs:11,16,21,63-66`
- Test: `tests/MapEditor.App.Tests/AssetContextControllerTests.cs`

**Mutation impact:**
- Source of truth changed: `_current` (the `AssetContext`) is unchanged, but its *fan-out* changes from one view model to all of them.
- Important readers: `SetSheetIds` / `SelectedSheet` / `Refresh(Canvas | Palette)` at `:63-66`; `SpritePaletteControl.Frames` reads `_assets.Current.GetFrames(_viewModel.SelectedSheet)` (`SpritePaletteControl.cs:251`); `MapCanvas.RenderMap` reads `_assets.Current.Renderer` (`MapCanvas.cs:93`).
- Derived state: each document view model caches `SheetIds` and `SelectedSheet`. A document created *after* a successful `TryOpen` would otherwise hold an empty `SheetIds` and sheet 0 — a stale-palette bug the current single-document code cannot have.
- Required propagation sequence in `TryOpen`, after `_current = candidate`: for each document in `Documents`, `SetSheetIds(candidate.SheetIds)`, then `SelectedSheet = candidate.SheetIds.Count > 0 ? candidate.SheetIds[0] : 0`, then `Refresh(EditorRefresh.Canvas | EditorRefresh.Palette)`. Then persist the directory, then `replaced.Dispose()` — disposal stays last, after every reader has been repointed at the new context.
- Ordering constraint: the controller takes the `WorkspaceViewModel` and subscribes to `Documents.CollectionChanged`, seeding each added document with the current sheet ids. This resolves the construction cycle — the workspace is built first with no knowledge of assets, then the asset controller attaches to it.
- Invariant: every document, whenever created, sees the same sheet ids as `Current`.

**Tests:** `TryOpen_WithSeveralDocuments_UpdatesEveryOne` (assert each document's `SheetIds` and `SelectedSheet`); `DocumentAddedAfterOpen_IsSeededWithCurrentSheetIds` — adversarial, fails on the loop-only implementation that forgets late arrivals. Existing single-document tests get a one-document workspace and keep passing.

Commit: `refactor: fan asset changes out to every open document`.

---

### Task 7: Compose the workspace and activate documents in `MainWindow`

**Files:**
- Modify: `src/MapEditor.App/Views/MainWindow.axaml.cs`, `src/MapEditor.App/App.axaml.cs:31-52`
- Modify: `tests/MapEditor.App.Tests/MainWindowTests.cs:29-75` (harness), and the other window test files for the new constructor
- Test: `tests/MapEditor.App.Tests/MapCanvasTests.cs`, `tests/MapEditor.App.Tests/MainWindowTests.cs`

`MainWindow`'s constructor takes `WorkspaceViewModel` instead of `MapDocumentViewModel`. It keeps a per-document view bundle:

```csharp
private sealed record DocumentView(MapCanvas Canvas, SpritePaletteControl Palette);
private readonly Dictionary<MapDocumentViewModel, DocumentView> _views = new();
```

Views are created on `Documents.CollectionChanged` add (or lazily on first activation) and removed on remove; the removed bundle's controls are dropped after being unhosted.

`ActivateDocument(MapDocumentViewModel document)`, in this order:

1. If it is already active, return.
2. `ActiveCanvas.FinishInteraction(commit: true)` on the outgoing canvas — an in-flight stroke or rect-drag must never survive a switch.
3. `_document.CancelPasteMode()` on the outgoing document.
4. Detach `PropertyChanged` (`OnViewModelPropertyChanged`) and `CanvasInvalidated` (`SyncReadouts`) from the outgoing view model; attach both to the incoming one.
5. Set `_document`, `DataContext`, `CanvasHost.Child` and `PaletteBorder.Child` from the bundle; call `incoming.Palette.BindScrollBar(PaletteBar)`.
6. Re-run `SyncToolButtons`, `SyncLayerRows`, `SyncBrushFields`, `SyncReadouts`, `SyncAssetDirectory`, and set `Title = _document.Title`.

Hover needs no work: `MapCanvas` attaches and detaches its window `PointerMoved` handler in `OnAttachedToVisualTree`/`OnDetachedFromVisualTree` (`MapCanvas.cs:168-186`), so replacing `CanvasHost.Child` stops the outgoing canvas tracking the cursor. Verify this with a test rather than assuming it.

The shared `PaletteBar` must not drive an inactive palette. `BindScrollBar` unsubscribes only its *own* previous bar (`SpritePaletteControl.cs:88-92`), so add `internal void UnbindScrollBar()` — unsubscribe `ValueChanged`, null `_bar` — and call it on the outgoing palette in step 5. Without it every palette ever activated stays subscribed to the one bar.

The window subscribes to `Workspace.PropertyChanged` for `ActiveDocument` and calls `ActivateDocument`, so workspace-driven activation (New, Open, Close) reaches the UI through one path. `OnNew`/`OnOpen` call `Workspace.NewAsync()`/`OpenAsync()`; `OnClosing` calls `Workspace.CloseAllAsync()` behind the existing `_closeGuardRunning`/`_closeApproved` guards (`MainWindow.axaml.cs:474-510`), which stay exactly as they are. `MapDocumentViewModel.RequestCloseAsync` — the temporary delegate added in Task 4 — is deleted here.

`App.ComposeMainWindow` order: settings → dialogs proxy → `WorkspaceViewModel` → `AssetContextController(workspace, store)` → `MainWindow(surface, store, workspace, assets)` → proxy target. `ComposedEditor` swaps its `Controller` and `ViewModel` members for `Workspace`; update `AppStartupTests` accordingly.

**Tests** (headless, through `MainWindowHarness`, which grows a `Workspace` property):

- `Activate_SwitchingDocuments_KeepsEachViewportIndependent` — zoom tab A to 200% via `ZoomStep`, switch to B, assert B is at 100%, switch back, assert A is still 200%. Adversarial against a shared-canvas implementation.
- `Activate_MidStroke_CommitsTheStroke` — begin a stroke on A's canvas, activate B, assert `A.Session.HasActiveStroke` is false, `A.CanUndo` is true, and `A.Session.IsDirty` is true.
- `Activate_DeactivatedCanvas_StopsTrackingHover` — move the pointer after switching, assert the outgoing document's `HoverX` is unchanged.
- `Activate_RebindsChrome` — set different tools/brushes/layer selections per document, switch, and assert the toolbar toggles, layer rows, brush text boxes and window title match the newly active document.
- `PaletteBar_AfterSwitch_ScrollsOnlyTheActivePalette` — proves `UnbindScrollBar`.

Also delete the now-dead `EditorRefresh.Document` branch of `MapDocumentViewModel.Refresh` (`:419-441`), the `OnControllerStateChanged` flag that passes it (`:467`), and the session-swap reset in `MapCanvas.OnCanvasInvalidated` (`MapCanvas.cs:466-481`) — no view model ever changes session now. Removing the enum member is a compile-time proof that nothing still swaps documents in place. Keep `EditorRefresh.Title/Commands/Canvas/Palette`.

Commit: `feat: host one canvas and palette per open map`.

| Invariant | Proved by |
|-----------|-----------|
| Zoom and scroll are per document | `Activate_SwitchingDocuments_KeepsEachViewportIndependent` |
| No gesture survives a switch | `Activate_MidStroke_CommitsTheStroke` |
| Only the visible canvas tracks hover | `Activate_DeactivatedCanvas_StopsTrackingHover` |
| Chrome always reflects the active document | `Activate_RebindsChrome` |
| Only the active palette follows the scroll bar | `PaletteBar_AfterSwitch_ScrollsOnlyTheActivePalette` |
| No view model ever swaps its session | `EditorRefresh.Document` no longer exists (compile-time) |

---

### Task 8: Full suite and smoke

**Steps:**
1. `dotnet test tests/MapEditor.Core.Tests` (expect 216), `dotnet test tests/MapEditor.Rendering.Tests` (expect 171), `dotnet test tests/MapEditor.App.Tests` (expect 302 plus the new cases, none removed except the New/Open controller tests that moved to `WorkspaceViewModelTests`).
2. `./build-map-editor.sh --skip-tests linux-x64` to confirm the app still publishes.
3. Manual smoke against `docs/map-editor-smoke.md`, plus: open two maps, confirm the second replaces the first on screen and the first is still in `Documents`; copy a region in one, activate the other via the workspace, paste, confirm the tiles land. With no tab strip yet, activation is exercised by New/Open — this is the intentional intermediate state Part 2 makes visible.
4. Commit any fixes separately.

**Known intermediate state at the end of Part 1:** opening a second map shows it and leaves the first open but unreachable from the UI. This is deliberate; Part 2 adds the tab strip that exposes it. Do not add a stopgap UI here.
