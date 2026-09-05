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

**Lifetime contract.** The clipboard outlives every document, so a `Changed` subscription is a strong reference from an app-lifetime object to a per-tab one. `MapDocumentViewModel` becomes `IDisposable`:

Idempotent, and safe on a document that was never activated:

```csharp
public void Dispose()
{
    _clipboard.Changed -= OnClipboardChanged;
    _controller.StateChanged -= OnControllerStateChanged;
    _session.Resized -= OnSessionResized;
}
```

Those are its three inbound subscriptions (`MapDocumentViewModel` constructor, and the `Resized` re-subscription that the deleted document branch of `Refresh` used to perform). Nothing else may subscribe to a document view model from an app-lifetime object without adding to this method. `WorkspaceViewModel.CloseAsync` (Task 5) calls it after removal; `AssetContextController` (Task 6) unsubscribes from the collection in its own `Dispose`.

Add `Dispose_StopsClipboardNotifications` to `CrossMapPasteTests`: dispose one document view model, copy from the other, and assert the disposed one raised no `PropertyChanged` for `Clipboard`. This is the observable proof that the closed-tab leak is closed — a plain reference test would pass even with the handler still attached.

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
internal EditorDocumentController(
    IEditorDialogs dialogs,
    MapFileStore store,
    EditorDocument initial,
    Func<EditorDocument, string, bool> isPathOwnedElsewhere);

internal EditorDocumentController(IEditorDialogs dialogs, MapFileStore store, EditorDocument initial)
    : this(dialogs, store, initial, static (_, _) => false);

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

**Path ownership.** `OpenAsync`'s duplicate check (Task 5) is not enough on its own: Save-As picks an arbitrary destination, so an Untitled document can save straight over a path another tab already owns, leaving two controllers with competing revisions for one file and breaking the "same file never opens twice" invariant from the other direction. Hence the fourth constructor argument above, and the three-argument overload that supplies `static (_, _) => false` for the existing controller tests, which construct controllers directly and know nothing about a workspace.

The delegate takes the asking `EditorDocument` as well as the candidate path, which is what keeps the workspace out of a construction cycle. The workspace must build a controller *before* the view model that owns it, so a delegate closing over "this document's view model" would capture a variable that is still null at construction time. Passing the asker at call time avoids that entirely:

```csharp
// in WorkspaceViewModel, when creating a document
var controller = new EditorDocumentController(_dialogs, _store, initial, IsPathOwnedElsewhere);

private bool IsPathOwnedElsewhere(EditorDocument asker, string fullPath)
    => _documents.Any(d => !ReferenceEquals(d.Document, asker) &&
                           d.Document.Path is { } owned &&
                           string.Equals(owned, fullPath, PathComparison));
```

`MapDocumentViewModel` exposes `internal EditorDocument Document => _controller.Document` for this (Part 2 also wants it for `TabTitle`/`TabToolTip`). Identity is by `EditorDocument` reference rather than by view model, and the controller always passes its current `_current`, so the comparison stays correct across the `SaveCoreAsync` replacement that swaps path and revision. `PathComparison` is the platform-aware comparison already at `EditorDocumentController.cs:11-13` — move it somewhere both types can use.

The controller consults the delegate at both Save-As destinations — the picker result in `SaveAsAsync` (`:135`) and the `ExternalChangeChoice.SaveAs` branch of `SaveCoreAsync` (`:273-275`) — and on a hit shows `ShowErrorAsync(new ErrorPresentation("Save map", $"{destination}: already open in another tab."))` and returns without writing. The document stays dirty with its original path and revision untouched.

Saving over a document's *own* path is not "owned elsewhere" and stays legal: the asker is excluded by reference, so the existing same-path revision guard (`SaveAsAsync:137-143`, covered by `SaveAs_SamePathAfterExternalRewrite_StillGuardsExpectedRevision:434`) still governs that case.

Ownership is derived, not stored: it is computed from `_documents` at each Save-As, so a tab closed in between frees its path with no bookkeeping to go stale.

New controller tests: `SaveAs_PathOwnedByAnotherDocument_ReportsErrorAndKeepsDirtyState` (assert the destination file is not created, the document is still dirty, and `Path`/`Revision` are unchanged); `Save_ExternalConflictSaveAs_PathOwnedByAnotherDocument_ReportsErrorAndKeepsOriginal` — adversarial, because the external-change branch is the one an implementation forgets; `SaveAs_UnownedPath_Writes` as the negative control. A workspace-level `SaveAs_ToAnotherDocumentsPath_LeavesBothDocumentsIntact` goes in Task 5, using the real delegate rather than a stub.

**Steps:** delete the moved members; add the constructor parameter; rename `RequestCloseAsync`/`RequestCloseCoreAsync` to `ConfirmCloseAsync` without the guards. Update `EditorDocumentControllerTests`: every Save, Save-As, external-change and dirty-prompt case stays. The New/Open cases move to `WorkspaceViewModelTests` (Task 5) — **move, not drop**. Each of these must have a workspace-level equivalent, and Task 5's list is not complete without them:

| Controller test being moved | Workspace equivalent |
|---|---|
| `New_ValidDimensions_PublishesNewDocumentOnce:52` | `New_AddsTabAndActivatesIt` |
| `New_InvalidDimensions_KeepsCurrentDocument:79` (theory) | `New_InvalidDimensions_AddsNothing`, same theory rows |
| `New_Canceled_KeepsCurrentDocumentWithoutDirtyPrompt:96` | `New_CancelledDialog_AddsNothing` |
| `New_DialogFailure_PresentsLastResortErrorAndKeepsDocument:608` | `New_DialogFailure_ReportsErrorAndAddsNothing` |
| `Open_ValidFile_PublishesCleanDocumentWithPathAndRevision:164` | `Open_AddsTabAndActivatesIt` (assert `Path` and `Revision`) |
| `Open_MissingFile_ReportsErrorAndKeepsCurrentDocument:191` | `Open_MissingFile_ReportsErrorAndAddsNothing` |
| `Open_MalformedFile_ReportsTypedFormatReasonAndKeepsCurrentDocument:207` | `Open_MalformedFile_ReportsTypedFormatReason` — asserts the exact `DescribeFormatError` text, which is why that method moves rather than being rewritten |
| `Open_CanceledPicker_KeepsCurrentDocument:226` | `Open_CanceledPicker_AddsNothing` |
| `Open_DialogFailure_PresentsLastResortErrorAndKeepsDocument:646` | `Open_DialogFailure_ReportsErrorAndAddsNothing` |
| `New_DirtyAndCanceled_*`, `New_DirtyAndSave_*`, `New_DirtyAndSaveCanceled_*`, `Open_DirtyAndCanceled_*` (`:109,125,148,240`) | Deleted deliberately — New/Open no longer consult dirty state. Replaced by `New_DoesNotPromptWhenCurrentIsDirty`, which asserts the prompt is *absent*. |

Also add a `MapValidationException` open case (`Open_InvalidDimensionsFile_ReportsValidationMessage`); the controller suite covered that path only through save.

Two guard tests lose their subject when the controller's guards go: `RequestClose_SecondCallAfterApproval_DoesNotPromptAgain:566` and `RequestClose_DuplicateWhilePending_YieldsExactlyOnePrompt:579`. That behaviour now lives in `MainWindow._closeApproved` / `_closeGuardRunning`. Do not delete the coverage — check `MainWindowCloseTests` for equivalents and, if either is missing, add it there in Part 2, Task 6 before removing the controller test. `MainWindow`'s `OnClosing` still calls `_viewModel.RequestCloseAsync()`; keep that compiling by having `MapDocumentViewModel.RequestCloseAsync()` delegate to `ConfirmCloseAsync` for now — Task 7 rewires it to the workspace. Run `dotnet test tests/MapEditor.App.Tests`. Commit: `refactor: scope EditorDocumentController to a single document`.

---

### Task 5: `WorkspaceViewModel`

**Files:**
- Create: `src/MapEditor.App/ViewModels/WorkspaceViewModel.cs`
- Test: `tests/MapEditor.App.Tests/WorkspaceViewModelTests.cs` (new)

**Publication boundary.** `Documents` is the shared registry other code observes (Part 2's tab strip binds to it; Task 6's asset controller subscribes to it).

Creation order for open: pick path → `MapFileStore.Open` → construct `EditorDocument` + `EditorDocumentController` + `MapDocumentViewModel` → **add to `Documents`** (publication point) → activate it. A failed open throws before the add, so no partial entry is ever visible. Readers observe an added document before it is active, which is safe: `MainWindow` creates its view bundle on the collection-changed event and only hosts it when activation follows.

Teardown order for close is **activate the successor first, then remove**, not the reverse:

1. `ConfirmCloseAsync`; on false, return false with nothing changed.
2. If the document being closed is the active one, activate its successor. When it is the only document, first add a fresh clean Untitled and activate that.
3. Remove the closed document from `Documents`.
4. `Dispose()` the removed view model (Task 2's contract).

The ordering matters: `Documents.Contains(ActiveDocument)` must hold at *every* collection notification, because `MainWindow`'s collection-changed handler and the tab strip both read `ActiveDocument` while reacting. Removing first opens a window in which the active document is not in the collection — a dangling active tab that the strip would try to mark selected. Assert this invariant directly (see the tests below).

**Surface.** The collection and the active document are exposed read-only; every mutation goes through a method that can enforce membership. A public setter on `ActiveDocument` would let a caller activate a view model that is not in the workspace, and a bare `ObservableCollection` would let one empty it.

```csharp
internal sealed class WorkspaceViewModel : ViewModelBase
{
    internal WorkspaceViewModel(IEditorDialogs dialogs, MapFileStore store);

    private readonly ObservableCollection<MapDocumentViewModel> _documents = new();
    internal ReadOnlyObservableCollection<MapDocumentViewModel> Documents { get; }

    internal MapDocumentViewModel ActiveDocument { get; private set; }   // raises PropertyChanged
    internal SharedTileClipboard Clipboard { get; }

    internal void Activate(MapDocumentViewModel document);   // throws if not in _documents
    internal Task NewAsync();
    internal Task OpenAsync();
    internal Task<bool> CloseAsync(MapDocumentViewModel document);
    internal Task<bool> CloseAllAsync();
    internal void Move(int fromIndex, int toIndex);
}
```

`ReadOnlyObservableCollection<T>` forwards `CollectionChanged`, so the window and the tab strip observe it exactly as they would the mutable one. Part 2 binds a selecting control to `ActiveDocument`; with a private setter that binding is one-way out of the workspace, and the control's selection change calls `Activate` instead — the membership check stays on the write path.

The constructor seeds one clean Untitled document (`MapDocument.Create()`, `initiallyDirty: false`) and makes it active, matching today's `EditorDocumentController` constructor.

Behaviour:
- `NewAsync` — `ShowNewMapAsync`; on null or out-of-range dimensions (`MapDocument.MinDimension`/`MaxDimension`, as in the current `NewAsync:37-42`) return without adding; otherwise add a tab and activate it. No dirty prompt: nothing is being replaced.
- `OpenAsync` — `PickOpenMapAsync`; if the full path matches an existing document's `Path` (using the platform comparison at `EditorDocumentController.cs:11-13`), activate that document and return without re-reading the file. Otherwise `MapFileStore.Open`; on `MapFormatException` / `MapValidationException` / `IOException` / `UnauthorizedAccessException` / `PathTooLongException` show the same messages the controller used and add no document.
- `CloseAsync(document)` — if the document is not in `_documents`, return false immediately without prompting; a stale tab header or a double-fired close button must not raise a second dialog for a document that is already gone. If it is dirty, `Activate` it *before* prompting, so the user sees the map being asked about; on `Cancel` return false and leave it active (the user was just shown it, and yanking the view back would hide what they declined to discard). On approval, follow the teardown order above.

  Activation only moves when the *active* document is the one closing. Closing an inactive clean tab — a middle-click on a background tab — leaves the user exactly where they are. Successor choice, when the active document is closing: the document at the same index after removal, or the new last document when the closed one was last.
- `CloseAllAsync` — walk `Documents` in order; activate each dirty document before prompting (so the window shows the map being asked about); return false on the first refusal, leaving that document active. Used by Part 2's quit path.
- `Move` — reorder within `_documents` without touching `ActiveDocument`.

- `Activate(document)` — throws `ArgumentException` when the document is not in `_documents`; otherwise sets the field and raises `PropertyChanged`. Re-activating the current document is a no-op that still raises nothing.

**Tests** (`FakeEditorDialogs` for every prompt; real `MapFileStore` writing into a temp directory, as `EditorDocumentControllerTests` already does):

- `New_AddsTabAndActivatesIt`; `New_CancelledDialog_AddsNothing`; `New_DoesNotPromptWhenCurrentIsDirty` (assert `Dialogs.DirtyShown == 0`) — adversarial against carrying the old replace-semantics prompt over.
- `Open_AddsTabAndActivatesIt` — the workspace starts with one Untitled document, so opening a file leaves `Documents.Count == 2` with the opened document active, its `Path` set and `Revision` non-null.
- `Open_AlreadyOpenPath_ActivatesExistingTabWithoutDuplicating` — open a file (count 2), activate the Untitled tab, open the *same* path again: `Documents.Count` is still 2 and the previously opened instance is active again. The pristine Untitled tab is *not* consumed or replaced — Part 1 has no reuse rule. Adversarial.
- `Open_InvalidFile_AddsNoTabAndReportsError` (assert `Documents.Count` unchanged and `Dialogs.Errors` has one entry) — proves the publication boundary.
- `Close_CleanDocument_ActivatesRightNeighbour`; `Close_LastInStrip_ActivatesNewLast`.
- `Close_OnlyDocument_LeavesFreshUntitled` — assert `Documents.Count == 1`, not dirty, `Path` null, and *not* the same instance as the closed one.
- `Close_DirtyDocument_CancelKeepsIt`; `Close_DirtyDocument_DiscardDropsIt`; `Close_DirtySaveThatFails_KeepsDocument` (set `SavePickResult = null` so Save-As cancels; assert the document is still in `Documents`) — adversarial against a close path that trusts the choice instead of re-checking `IsDirty`.
- `CloseAll_CancelOnSecond_StopsAndLeavesItActive`.
- `Move_DoesNotChangeActiveDocument`.
- `Close_InactiveCleanTab_KeepsCurrentActiveDocument` — adversarial against a close path that always reselects a neighbour.
- `Close_InactiveDirtyTab_ActivatesItBeforePrompting` — assert `ActiveDocument` is the closing document at the moment `ShowDirtyAsync` runs (use a `FakeEditorDialogs` callback or `DirtyGate` to observe mid-prompt), and that on `Cancel` it stays active.
- `Close_DocumentNotInWorkspace_ReturnsFalseWithoutPrompting` — assert `Dialogs.DirtyShown == 0` for an already-removed dirty document.
- `Close_ActiveDocument_KeepsActiveInsideDocumentsAtEveryNotification` — subscribe to `Documents.CollectionChanged` and assert `Documents.Contains(ActiveDocument)` inside the handler, for both the middle-of-strip case and the last-document case. This is the adversarial proof for the remove-then-activate ordering bug.
- `Activate_DocumentNotInWorkspace_Throws`.
- `SaveAs_ToAnotherDocumentsPath_LeavesBothDocumentsIntact` — open a file in one tab, then Save-As the Untitled tab onto that same path; assert the error is reported, the Untitled tab is still dirty with a null `Path`, and the opened document's `Revision` is unchanged. Exercises the real ownership delegate from Task 4, not a stub.

Red first (the class does not exist), then implement, then `dotnet test tests/MapEditor.App.Tests`. Commit: `feat: add WorkspaceViewModel for multiple open maps`.

| Invariant | Proved by |
|-----------|-----------|
| A failed open publishes no document | `Open_InvalidFile_AddsNoTabAndReportsError` |
| The same file never opens twice | `Open_AlreadyOpenPath_ActivatesExistingTabWithoutDuplicating` |
| The workspace is never empty | `Close_OnlyDocument_LeavesFreshUntitled` |
| A close whose save failed does not drop the document | `Close_DirtySaveThatFails_KeepsDocument` |
| New/Open never prompt about the current document | `New_DoesNotPromptWhenCurrentIsDirty` |
| Reordering does not change activation | `Move_DoesNotChangeActiveDocument` |
| Closing a background tab does not move the user | `Close_InactiveCleanTab_KeepsCurrentActiveDocument` |
| The active document is always in `Documents` | `Close_ActiveDocument_KeepsActiveInsideDocumentsAtEveryNotification` |
| A closed document is never prompted about twice | `Close_DocumentNotInWorkspace_ReturnsFalseWithoutPrompting` |
| No two tabs can own one path | `SaveAs_ToAnotherDocumentsPath_LeavesBothDocumentsIntact` |
| A closed document stops receiving notifications | `Dispose_StopsClipboardNotifications` (Task 2), `Dispose_StopsAssetNotifications` (Task 6) |

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

**Disposal.** `AssetContextController.Dispose` (`:76-77`) currently only disposes `_current`. It must also unsubscribe from `Documents.CollectionChanged`; the workspace outlives nothing here, but leaving the handler attached keeps a disposed controller reachable from the live workspace and lets a late add call into a disposed `AssetContext`. Add the unsubscribe before `_current.Dispose()`.

The controller must not subscribe to individual documents — it only reads the collection and pushes into each document on demand, so a removed document needs no unsubscription here. Keep it that way: per-document subscriptions from this app-lifetime object would have to join `MapDocumentViewModel.Dispose`.

**Tests:** `TryOpen_WithSeveralDocuments_UpdatesEveryOne` (assert each document's `SheetIds` and `SelectedSheet`); `DocumentAddedAfterOpen_IsSeededWithCurrentSheetIds` — adversarial, fails on the loop-only implementation that forgets late arrivals; `Dispose_StopsAssetNotifications` — dispose the controller, add a document to the workspace, assert no seeding occurred and nothing threw. Existing single-document tests get a one-document workspace and keep passing.

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

Views are created on `Documents.CollectionChanged` add (or lazily on first activation) and removed on remove. On removal, unhost the controls first if they are the hosted ones (`CanvasHost.Child = null`, `PaletteBorder.Child = null`), call `UnbindScrollBar` on the removed palette, then drop the dictionary entry. Because `WorkspaceViewModel.CloseAsync` activates the successor before removing (Task 5), the removed bundle is normally already unhosted by the time the notification arrives; do the check anyway rather than relying on that ordering from a second file.

The window creates the view bundle but the workspace owns the view model's lifetime: `MainWindow` must not call `MapDocumentViewModel.Dispose` — `CloseAsync` already does, and a double dispose that also runs on a document the workspace still holds would silently kill its notifications. The dictionary entry is the only thing the window owns.

`ActivateDocument(MapDocumentViewModel document)`, in this order:

1. If it is already active, return. `_document` starts null so the constructor's initial activation is never skipped by this check — the first document must go through the same path as every later one, or the startup window hosts no canvas at all. Write step 2's `FinishInteraction` and step 4's detach as null-safe on the outgoing document rather than special-casing startup.
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
- `Startup_HostsTheInitialCanvasAndPalette` — assert `CanvasHost.Child` and `PaletteBorder.Child` are non-null on a freshly created window. Adversarial against the early-return-on-first-activation bug.
- `CloseDocument_UnhostsAndDropsItsViews` — assert the dictionary no longer holds the closed document and the hosted canvas is the successor's.

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
| The startup document is hosted like any other | `Startup_HostsTheInitialCanvasAndPalette` |
| Closed documents leave no view behind | `CloseDocument_UnhostsAndDropsItsViews` |

---

### Task 8: Full suite and smoke

**Steps:**
1. `dotnet test tests/MapEditor.Core.Tests` (expect 216), `dotnet test tests/MapEditor.Rendering.Tests` (expect 171), `dotnet test tests/MapEditor.App.Tests` (expect 302 plus the new cases, none removed except the New/Open controller tests that moved to `WorkspaceViewModelTests`).
2. `./build-map-editor.sh --skip-tests linux-x64` to confirm the app still publishes.
3. Manual smoke against `docs/map-editor-smoke.md`, plus: open two maps, confirm the second replaces the first on screen and the first is still in `Documents`; copy a region in one, activate the other via the workspace, paste, confirm the tiles land. With no tab strip yet, activation is exercised by New/Open — this is the intentional intermediate state Part 2 makes visible.
4. Commit any fixes separately.

**Known intermediate state at the end of Part 1:** opening a second map shows it and leaves the first open but unreachable from the UI. This is deliberate; Part 2 adds the tab strip that exposes it. Do not add a stopgap UI here.
