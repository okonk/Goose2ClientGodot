# Terrain Brush Part 3: Terrain Editor Implementation Plan

**Goal:** Build the modeless full-sheet Terrain Editor with local draft history, Godot-style region painting, validation, revision conflicts, malformed-file recovery, and transactional save handoff.

**Architecture:** An App-layer `TerrainEditorSession` owns a mutable deep copy of the last published catalog and records compact local commands. A custom full-sheet Avalonia control uses one normalized polygon table for both hit testing and translucent drawing. `TerrainEditorController` coordinates validation and Part 1 atomic storage through a prepare/commit publication interface that Part 4 will implement for the live application.

**Tech Stack:** C# 12, .NET 10, Avalonia 11/Skia, existing sprite sheet loader, xUnit with Avalonia Headless.

---

Part 3 of 4. Start only after Parts 1–2 are implemented and green.

## APIs verified before planning

- `GraphicSheetControl` already draws a complete bitmap nearest-neighbor and sizes itself by zoom at `src/MapEditor.App/Controls/GraphicSheetControl.cs:26-31,56-97`.
- Existing frame hit testing divides by zoom and picks the lowest overlapping graphic ID at `src/MapEditor.App/ViewModels/GraphicViewerViewModel.cs:116-145`.
- The Graphic Viewer disposes the old sheet before replacement and detaches resources on close at `src/MapEditor.App/Views/GraphicViewerWindow.axaml.cs:164-179,324-335`.
- `IMapDrawTarget` currently has image/line/rectangle/text only at `src/MapEditor.App/Rendering/IMapDrawTarget.cs:8-18`; polygon support must update every implementation and fake.
- `SheetEditSession` demonstrates state-ID dirty tracking and private undo/redo at `src/MapEditor.GameData/Editing/SheetEditSession.cs:15-43,279-314`; Terrain Editor history must preserve the same savepoint behavior without copying its domain commands.
- `IEditorDialogs` already has Save/Discard/Cancel and map external-change surfaces at `src/MapEditor.App/Dialogs/IEditorDialogs.cs:10-22,33-59`.
- Avalonia tests show real windows and drain `Dispatcher.UIThread` through the harness at `tests/MapEditor.App.Tests/MainWindowTests.cs:31-78`; the headless app uses real Skia controls at `tests/MapEditor.App.Tests/TestApplication.cs:23-28`.

## Locked geometry

Use source-pixel coordinates in a half-open 32×32 frame. Define these nine polygons once in `TerrainRegionGeometry`, scaling only at draw time:

- Center: `(10,8) (22,8) (24,10) (24,22) (22,24) (10,24) (8,22) (8,10)`
- North: `(8,0) (24,0) (22,8) (10,8)`
- East: `(32,8) (32,24) (24,22) (24,10)`
- South: `(24,32) (8,32) (10,24) (22,24)`
- West: `(0,24) (0,8) (8,10) (8,22)`
- NorthEast: `(24,0) (32,0) (32,8) (24,10) (22,8)`
- SouthEast: `(32,24) (32,32) (24,32) (22,24) (24,22)`
- SouthWest: `(8,32) (0,32) (0,24) (8,22) (10,24)`
- NorthWest: `(0,8) (0,0) (8,0) (10,8) (8,10)`

Shared boundaries use precedence: Center, North, East, South, West, NorthEast, SouthEast, SouthWest, NorthWest. Draw every authored region at alpha `0xCC` over the source artwork. The same point-in-polygon routine and precedence must drive hit testing.

## Locked editor/session surface

Use:

```csharp
internal readonly record struct TerrainRegionKey(
    TerrainGraphicReference Graphic,
    TerrainPeer Peer);

internal sealed class TerrainEditorSession
{
    TerrainEditorSession(TerrainCatalog baseline, SpriteManifest manifest);

    bool IsDirty { get; }
    bool CanUndo { get; }
    bool CanRedo { get; }
    bool CanSave { get; }
    TerrainCatalog CurrentCatalog { get; }
    IReadOnlyList<TerrainValidationIssue> Diagnostics { get; }

    event Action<TerrainEditorChangeKind>? Changed;

    Guid AddTerrain();
    bool RenameTerrain(Guid id, string name);
    bool SetColorOverride(Guid id, TerrainColor? color);
    bool DeleteTerrain(Guid id);
    void BeginRegionStroke(Guid? value);
    void VisitRegion(TerrainRegionKey key);
    bool CompleteRegionStroke();
    void CancelRegionStroke();
    bool Undo();
    bool Redo();
    void MarkSaved(TerrainCatalog baseline);
    void Revert();
}
```

`Guid? value == null` is clear mode. `Changed(Preview)` fires after each effective region visit/cancel restoration; committed metadata/history operations fire `Changed(Committed)`. Only completion advances state ID/history. An all-`None` pattern is absent and is removed from the draft; clearing its final authored region records absence as the stroke's after-state. The control captures the selected paint ID or clear mode, selected sheet, zoom, frames, and modifiers when pressed. Sheet/zoom changes and all non-stroke mutation commands are disabled while capture is active; Escape or capture loss cancels first.

## Task 1: Add the isolated terrain draft and compact local history

**Files:**
- Create: `src/MapEditor.App/Terrain/TerrainEditorSession.cs`
- Create: `src/MapEditor.App/Terrain/TerrainEditorCommand.cs`
- Create: `src/MapEditor.App/Terrain/TerrainRegionKey.cs`
- Test: `tests/MapEditor.App.Tests/TerrainEditorSessionTests.cs`

**Mutation impact:**
- Source of truth changed: the private draft terrain/graphic collections and editor state IDs.
- Important readers: Part 3 view model/control and save controller; published Part 1 catalog remains untouched.
- Derived/cached state affected: diagnostics, `CanSave`, selected IDs, dirty state, undo/redo, and graphic assignments.
- Required propagation: preview visit → mutate draft and raise `Changed(Preview)` without history; completion → rebuild immutable catalog/diagnostics → advance state ID → push one command → raise `Changed(Committed)`. No-op completion performs no history transition.
- Invariants: deep isolation from published catalog; stable IDs survive rename; one region drag is one command; deleting a terrain cleans centered graphics and peer references atomically.
- Observable proof: compare published catalog before/after every draft mutation and undo deletion back to exact bytes.

**Steps:**
1. Add red tests proving constructor deep-copy isolation, generated nonempty GUIDs, unique default names (`Terrain`, `Terrain 2`, ... case-insensitively), rename/color no-ops, and derived-color stability.
2. Implement compact command types for terrain add/update/delete and region-change batches. Commands store before/after deltas, not whole-catalog snapshots.
3. Test deletion: remove all graphics centered on the terrain, clear every remaining peer reference including temporary center references, and restore the exact prior graph with one Undo.
4. Implement state-ID dirty tracking: split saving into `PrepareMarkSaved(TerrainCatalog baseline)`, allocation-free/callback-free `ApplyMarkSaved(...)`, and guarded `NotifyMarkSaved(...)`; preparation captures event arguments, delegate snapshots, and failure capacity before file mutation. Marking saved changes the baseline state without clearing undo; undo past the baseline is dirty; returning to it is clean; accepted edits clear redo.
5. Remove a graphic draft entry when all nine peers become `None`; keep centerless entries only while at least one side/corner remains authored. Add undo tests for both cases.
6. Define active-stroke guards: metadata mutation, Delete, Undo/Redo, Revert, Save, sheet change, and zoom change reject while active; the window/control cancels before invoking them.
7. Implement `Revert` to reject while a region stroke is active, restore the last saved immutable baseline otherwise, and clear local history. The control cancels before invoking it.
8. Run focused tests and commit: `git commit -m "feat: add terrain editor draft history"`.

| Invariant | Proved by |
|---|---|
| Unsaved draft cannot mutate the published catalog | `Constructor_AndMutations_AreDeeplyIsolated` |
| Delete cleanup is one reversible operation | `DeleteTerrain_Undo_RestoresGraphicsAndAllPeers` |
| Dirty state follows state identity, not undo count | `UndoAcrossSavedState_TracksDirtyExactly` |

## Task 2: Add editor validation and metadata view model

**Files:**
- Create: `src/MapEditor.App/ViewModels/TerrainEditorViewModel.cs`
- Create: `src/MapEditor.App/ViewModels/TerrainEditorItemViewModel.cs`
- Test: `tests/MapEditor.App.Tests/TerrainEditorViewModelTests.cs`

**Mutation impact:**
- Source of truth changed: selected draft terrain, selected sheet, zoom, pending metadata fields, and session draft through explicit commit methods.
- Important readers: Terrain Editor controls and Save/Revert command enablement.
- Derived/cached state affected: terrain list items, swatches, effective color, eligible sheet/frame lists, validation summary, and coverage warnings.
- Required propagation: `Changed(Preview)` redraws assignments/diagnostics without changing history flags; `Changed(Committed)` rebuilds affected items and command states → raises exact property changes → requests sheet redraw. Metadata text commits before selection change, undo, save, or close.
- Invariants: UI selection never changes catalog identity; invalid intermediate centerless graphics remain visible; Save enables only when Core plus manifest validation succeeds.
- Observable proof: edit metadata and region state through the view model, then assert immutable `CurrentCatalog`, diagnostics, and command flags.

**Steps:**
1. Add red tests for terrain ordering by name then ID, selected paint terrain, sheet eligibility from exact 32×32 manifest frames, zoom clamping, and derived/override swatches.
2. Define metadata commit policy: Enter or focus loss commits; selection change, Undo/Redo, Save, Revert, and close first commit pending valid text. Invalid name/color text remains focused, shows a field error, and blocks the requested operation.
3. Implement Add/Rename/Delete, `#RRGGBB` override parsing, reset-to-derived color, and session command properties.
4. Surface blocking errors separately from nonblocking coverage warnings defined in Part 1. Centerless temporary patterns must identify sheet/graphic and `Center`.
5. Add adversarial tests that a rename collision and malformed color do not mutate the session or clear redo.
6. Run focused tests and commit: `git commit -m "feat: add terrain editor state"`.

| Invariant | Proved by |
|---|---|
| Invalid pending metadata cannot partially commit | `CommitPending_InvalidNameOrColor_PreservesSession` |
| Save reflects both Core and manifest validation | `CanSave_CenterlessOrMissingFrame_IsFalse` |
| Color reset returns to ID-derived color | `ResetColor_UsesStableDerivedSwatch` |

## Task 3: Add shared polygon drawing and hit geometry

**Files:**
- Create: `src/MapEditor.App/Terrain/TerrainRegionGeometry.cs`
- Modify: `src/MapEditor.App/Rendering/IMapDrawTarget.cs:8-18`
- Modify: `src/MapEditor.App/Rendering/DrawingContextMapDrawTarget.cs:9-54`
- Modify: `tests/MapEditor.App.Tests/Fakes/RecordingMapDrawTarget.cs:10-53`
- Modify all other `IMapDrawTarget` test doubles found by `grep -R "IMapDrawTarget" tests src`
- Test: `tests/MapEditor.App.Tests/TerrainRegionGeometryTests.cs`
- Test: `tests/MapEditor.App.Tests/AvaloniaMapDrawSinkTests.cs`

**Mutation impact:**
- Source of truth changed: one immutable normalized polygon table.
- Important readers: Terrain sheet drawing and hit testing; existing map drawing implementations gain a method but behavior is unchanged.
- Derived/cached state affected: scaled polygon points only; no persistent state.
- Required propagation: normalized source polygon → translate by frame origin → multiply by zoom for drawing; inverse zoom/translation → same source polygon for hit testing.
- Invariants: polygons and precedence match the locked table; alpha belongs to brushes, not geometry; existing draw targets still compile and record correctly.
- Observable proof: pin every vertex and boundary hit, then record a polygon through the real drawing adapter without exceptions.

**Steps:**
1. Add failing geometry tests for all exact vertex arrays, interior points, outside points, and every shared boundary precedence.
2. Add `DrawPolygon(Brush fill, Pen? stroke, IReadOnlyList<Point> points)` to `IMapDrawTarget`; implement with `StreamGeometry`/`DrawGeometry` in `DrawingContextMapDrawTarget` and point-copy recording in fakes.
3. Implement translation/scaling and inverse hit helpers from the single table. Do not duplicate polygon coordinates in the control.
4. Run all App tests to catch every interface implementation.
5. Commit: `git commit -m "feat: add terrain region geometry"`.

| Invariant | Proved by |
|---|---|
| Drawing and hit testing share one polygon definition | geometry tests call both transformations from `TerrainRegionGeometry` |
| Boundary ownership is deterministic | `HitTest_SharedBoundaries_UsesDocumentedPrecedence` |
| Existing rendering remains source-compatible | full App suite compiles and passes |

## Task 4: Render complete sheets and authored overlays

**Files:**
- Create: `src/MapEditor.App/Controls/TerrainSheetControl.cs`
- Create: `src/MapEditor.App/Rendering/TerrainSheetImageController.cs`
- Test: `tests/MapEditor.App.Tests/TerrainSheetControlTests.cs`
- Test: `tests/MapEditor.App.Tests/TerrainSheetImageControllerTests.cs`

**Mutation impact:**
- Source of truth changed: current disposable sheet image and control subscriptions.
- Important readers: control measure/render and Terrain Editor diagnostic text.
- Derived/cached state affected: desired size, visible overlays, image-load diagnostic.
- Required propagation: selected sheet changes → clear control image → dispose prior image → load new `sheets/<id>.png` → assign image or diagnostic → invalidate measure/render.
- Invariants: nearest-neighbor artwork draws before overlays; only exact 32×32 manifest frames receive regions; centerless side/corner assignments remain visible; old images and subscriptions are disposed once.
- Observable proof: use `RecordingMapDrawTarget` to assert one image followed by exact `0xCC` polygons and test disposal on sheet change/close.

**Steps:**
1. Add red draw-operation tests for source/destination rectangles at min/max zoom, frame translation, effective colors, fixed alpha `0xCC`, and authored-only region drawing.
2. Filter eligible frames by `SourceRect.Width == 32 && Height == 32`; do not assume a regular atlas grid.
3. Reuse `AvaloniaSpriteSheetLoader`; implement path-specific inline diagnostics for missing/corrupt PNGs while keeping sheet selection available.
4. Leave plain wheel to the parent `ScrollViewer`; Ctrl+wheel changes zoom, matching `GraphicSheetControl` at `src/MapEditor.App/Controls/GraphicSheetControl.cs:116-125`.
5. Make the control/controller idempotently disposable and detach every event.
6. Run focused tests and commit: `git commit -m "feat: render terrain sheet overlays"`.

| Invariant | Proved by |
|---|---|
| Artwork remains visible below 80% overlays | `Render_DrawsImageBeforeAlphaCcPolygons` |
| Oversized graphics cannot be authored | `Render_Non32Frame_HasNoOverlayOrHitTarget` |
| Sheet replacement cannot leak images | `SelectSheet_DisposesPreviousImageExactlyOnce` |

## Task 5: Implement region-paint pointer lifecycle

**Files:**
- Modify: `src/MapEditor.App/Controls/TerrainSheetControl.cs`
- Create: `src/MapEditor.App/Controls/TerrainSheetLine.cs`
- Test: `tests/MapEditor.App.Tests/TerrainSheetControlTests.cs`

**Mutation impact:**
- Source of truth changed: active pointer capture and session region-stroke preview.
- Important readers: overlay rendering, local undo/redo, dirty state, and close/save guards.
- Derived/cached state affected: captured sheet/paint ID/clear mode, previous source point, visited region keys.
- Required propagation: press hit → capture gesture constants → begin/visit → pointer capture → inverse-zoom segment sampling → visit crossed regions → release complete; Escape/capture loss cancels then releases.
- Invariants: one drag is one command; Shift is sampled at press; sparse movement crosses every source pixel; invalid image/frame starts no capture; cancellation restores preview exactly.
- Observable proof: real headless pointer tests assert session data and history, not only handled flags.

**Steps:**
1. Add red real-pointer tests for left paint, Shift+left clear, release commit, Escape cancel, capture-loss cancel, and repeated region no-op.
2. Implement source-space DDA sampling with `ceil(max(abs(dx), abs(dy)))` steps and both endpoints; feed each sample through lowest-graphic-ID frame hit and shared polygon hit.
3. Capture selected terrain ID or clear mode, sheet ID, zoom, frame list, and previous source point at press. Disable sheet/zoom selectors and ignore Ctrl+wheel while active; selection and metadata commands cancel or wait for the gesture before mutation.
4. Leaving the control yields no hit but preserves the previous valid source point; re-entry interpolates from it.
5. Clear gesture flags before calling `Capture(null)` so reentrant capture-loss cannot cancel twice. On dispose, cancel active preview before detaching/capture release.
6. Run focused and full App tests; commit: `git commit -m "feat: paint terrain regions on sprite sheets"`.

| Invariant | Proved by |
|---|---|
| Sparse pointer events cannot leave region gaps | `Drag_SparseMovement_VisitsEveryCrossedRegion` |
| Shift/terrain/sheet/zoom cannot drift mid-stroke | `Drag_ActiveGesture_DisablesStateChangesAndUsesCapturedValues` |
| Capture loss never commits a draft command | `CaptureLoss_RestoresDraftAndHistory` |

## Task 6: Coordinate validation, conflict handling, and transactional save handoff

**Files:**
- Create: `src/MapEditor.App/Terrain/ITerrainCatalogPublisher.cs`
- Create: `src/MapEditor.App/Terrain/TerrainEditorController.cs`
- Create: `src/MapEditor.App/Terrain/TerrainOperationGate.cs`
- Modify: `src/MapEditor.App/Dialogs/IEditorDialogs.cs:10-59`
- Modify: `src/MapEditor.App/Dialogs/AvaloniaEditorDialogs.cs`
- Modify: `src/MapEditor.App/Dialogs/EditorDialogsProxy.cs`
- Modify: `tests/MapEditor.App.Tests/Fakes/FakeEditorDialogs.cs`
- Test: `tests/MapEditor.App.Tests/TerrainEditorControllerTests.cs`

**Mutation impact:**
- Source of truth changed: terrain file, draft savepoint, loaded revision, and later the live catalog through a publication lease.
- Important readers: Terrain Editor window, Part 4 asset controller/documents/canvases.
- Derived/cached state affected: dirty/CanSave, published catalog/index, selections, and diagnostics.
- Required propagation: commit pending fields → `PrepareSave` validates/builds immutable candidate and canonical bytes against the expected loaded revision → prepare publication lease and local mark-saved transition → atomic `Save(prepared)` returns the same operation/catalog/index plus new revision → publication Commit → callback-free revision/mark-saved assignment → guarded preallocated local notifications; failure disposes the lease and leaves draft/published catalog unchanged.
- Invariants: no disk write before successful prepare; no old in-memory catalog after a successful disk replacement; overwrite checks a freshly observed revision immediately before the atomic move; malformed recovery is explicit and retains the invalid file's revision; only one save/conflict/root/close terrain operation runs at a time.
- Observable proof: fake publisher/store tests assert exact call ordering and all final states under failures.

Define:

```csharp
internal interface ITerrainCatalogPublisher
{
    ITerrainCatalogSavePublication PrepareSave(
        TerrainOperationLease operation,
        AssetContext expectedContext,
        TerrainCatalogPreparedSave preparedSave);

    ITerrainCatalogLoadedPublication PrepareLoaded(
        TerrainOperationLease operation,
        AssetContext expectedContext,
        TerrainCatalogLoadResult loadedReplacement,
        TerrainLoadedPublicationKind kind);
}

internal interface ITerrainCatalogSavePublication : IDisposable
{
    void Commit(TerrainCatalogSaveResult durableReplacement);
}

internal interface ITerrainCatalogLoadedPublication : IDisposable
{
    void Commit();
}
```

`PrepareSave` verifies that `operation` is the currently held shared gate lease and stores the prepared save's operation ID, then either fails before write I/O or returns a reservation after cancelling active terrain gestures. After Save, its Commit receives `TerrainCatalogSaveResult` with the same operation ID and exact prepared catalog/index references plus the new durable revision; this type-level handoff requires no post-write allocation or validation. `TerrainLoadedPublicationKind` has `ValidReload` and `ConfirmedMalformedReload`; publisher validation requires the kind to match the load result. `PrepareLoaded` retains that revision-bearing result and its parameterless Commit publishes the exact snapshot. Confirmed malformed publication disables terrain features and rebinds the editor to recovery state. Part 4 supplies the real implementation. Disposing either lease without Commit releases the publication reservation and publishes nothing.

**Steps:**
1. Add `TerrainExternalChangeChoice { Reload, Overwrite, Cancel }`, `ConfirmReplaceTerrainCatalogAsync(string path)`, and exact dialog/proxy/fake implementations; do not reuse map `SaveAs`, which has no terrain meaning.
2. Add `TerrainOperationGate` shared by Terrain Editor save/close and Part 4 root/application lifecycle. `AcquireAsync` returns a `TerrainOperationLease`; public Save acquires its own lease, while root/close coordinators acquire once and call internal editor methods with that same lease. A different or missing lease is rejected, preventing unrelated nesting without deadlocking intentional Save-inside-root/close flows. Expose busy/completion state so disposal waits for the active lease and publication reservation.
3. Initialize the controller from the exact `AssetContext.Terrain` load result, including revision even when malformed. Add red ordering tests for prepare failure, store failure, commit, revision/baseline marking, and no-op Save when clean. Prepare the draft mark-saved transition and controller delegate snapshots/error capacity before writing; after durable Save, run publication Commit, callback-free local assignments, then guarded prepared notifications.
4. On conflict, Reload reads current bytes. A valid result prepares publication plus a complete draft/controller replacement transition, publishes that exact revision, applies draft/baseline/revision fields, and issues guarded notifications. For malformed/invalid bytes, require `ConfirmReplaceMalformedExternalTerrainAsync`; Cancel preserves the draft, while confirmation publishes the exact invalid revision-bearing load result, disables map terrain features, and rebinds the editor to an empty recovery draft whose Save expectation is that invalid revision. Overwrite re-reads, creates a new prepared save against the newly observed revision, and retries. Document the unavoidable portable TOCTOU window between final hash check and move; do not claim a second concurrent write is always detected.
5. Add explicit malformed recovery: `ReplaceWithEmptyCatalogAsync` uses the dedicated confirmation, creates an empty local draft while retaining the invalid file revision as Save's expectation, and does not write/publish until Save.
6. Ensure observer/dialog exceptions before disk replacement preserve file and publication; after durable replacement only publication Commit, callback-free local assignments, and guarded prepared notifications may run. The operation ID, retained object references, and preallocated transition data make this tail allocation-free and nonthrowing; notification failures are recorded for diagnostics.
7. Add close-during-conflict, root-switch-during-save, and dispose-with-live-lease tests against the shared gate.
8. Run focused tests and commit: `git commit -m "feat: coordinate terrain catalog saves"`.

| Invariant | Proved by |
|---|---|
| Publication is reserved before file mutation | `Save_PrepareFailure_PerformsNoFileIo` |
| Durable success cannot leave old catalog published | `Save_Success_CommitsLeaseBeforeMarkingDraftSaved` |
| Overwrite confirmation performs a fresh best-effort revision check | `Overwrite_RereadsRevisionBeforeRetry` |

## Task 7: Build and test the modeless Terrain Editor window

**Files:**
- Create: `src/MapEditor.App/Views/TerrainEditorWindow.axaml`
- Create: `src/MapEditor.App/Views/TerrainEditorWindow.axaml.cs`
- Modify: `src/MapEditor.App/Styles/EditorTheme.axaml` only for reusable terrain-window styles that are absent
- Test: `tests/MapEditor.App.Tests/TerrainEditorWindowTests.cs`

**Mutation impact:**
- Source of truth changed: UI selection/pending fields delegate to the view model/session; the window owns one sheet image/control lifetime.
- Important readers: user controls and Part 4 singleton coordinator.
- Derived/cached state affected: command enabled states, validation text, selected sheet image, and close guard.
- Required propagation: UI event → view-model mutation → property changes → controls/drawing; close → commit pending metadata → Save/Discard/Cancel → detach/dispose only after approved close.
- Invariants: modeless owner remains enabled; local shortcuts never reach map history; dirty close cannot be bypassed; resources detach once.
- Observable proof: show a real headless window with a fake publisher/store, drive controls and keys, close it, then mutate former dependencies and assert no callback.

**Steps:**
1. Build the window with terrain list/actions, name and `#RRGGBB` fields, reset color, paint-terrain selection, sheet and zoom selectors, `ScrollViewer`/`TerrainSheetControl`, diagnostics, Save, Revert, and an invalid-source recovery panel with explicit Replace with Empty Catalog.
2. Add Ctrl+Z, Ctrl+Y, and Ctrl+Shift+Z routing before general controls, except when native text editing should consume the gesture; map sessions are not reachable from this window.
3. Add red headless tests for add/rename/delete/color, sheet selection, zoom/scroll, validation display, Save/Revert, dirty close choices, clean close, and missing/corrupt image diagnostics.
4. Test modeless `Show(owner)` leaves the owner enabled. Singleton ownership is deferred to Part 4.
5. Implement an idempotent close guard coordinated with `TerrainOperationGate`: an active async save/conflict operation makes close wait and retry rather than disposing dependencies. Approved close order is cancel active region gesture → detach handlers → clear host → dispose control/image → close.
6. Run all App tests and Release build; commit: `git commit -m "feat: add terrain editor window"`.

| Invariant | Proved by |
|---|---|
| Terrain shortcuts cannot mutate map history | `Window_UndoRedo_ChangesOnlyDraftSession` |
| Dirty close always resolves Save/Discard/Cancel | close-choice theory including title-bar close |
| Closed window receives no later events | `Close_DetachesAndDisposesAllResources` |

## Part 3 completion

Run:

```bash
dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj -v minimal
dotnet test tests/MapEditor.Rendering.Tests/MapEditor.Rendering.Tests.csproj -v minimal
dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj -v minimal
dotnet build src/MapEditor.App/MapEditor.App.csproj -c Release
git diff --check
```

Expected: all commands pass. The Terrain Editor is directly constructible and fully tested, but Add/Edit main-window entry points and the real `ITerrainCatalogPublisher` remain for Part 4.
