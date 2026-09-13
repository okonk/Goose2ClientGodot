# Terrain Brush Part 4: Main Editor Integration Implementation Plan

**Goal:** Integrate terrain availability, selection, tools, map gestures, the singleton Terrain Editor, asset-root lifecycle, and smoke coverage into the multi-document map editor.

**Architecture:** `AssetContextController` becomes the UI-thread owner of live terrain publication without replacing sprite resources. Each document stores only a selected stable terrain ID and reconciles it against the shared catalog. The main window adds Tiles/Terrains presentation and routes terrain gestures to Part 2, while the Part 3 editor publishes through a prepare/commit lease that cancels all active terrain previews before durable save.

**Tech Stack:** C# 12, .NET 10, Avalonia 11, xUnit/Avalonia Headless, existing Core/Rendering/App terrain APIs from Parts 1–3.

---

Part 4 of 4. Start only after Parts 1–3 are implemented and green.

## APIs verified before planning

- `MapDocumentViewModel` already owns per-document active tool, brush, palette invalidation, and canvas invalidation at `src/MapEditor.App/ViewModels/MapDocumentViewModel.cs:24-57,93-105,121-174`.
- MainWindow owns one canvas/palette pair per document and swaps them at `src/MapEditor.App/Views/MainWindow.axaml.cs:65,356-420`; document removal currently unbinds but does not dispose controls at `src/MapEditor.App/Views/MainWindow.axaml.cs:166-190`.
- The current left panel and tool strip are at `src/MapEditor.App/Views/MainWindow.axaml:87-128`.
- Unmodified tool shortcuts are guarded from focused text boxes at `src/MapEditor.App/Views/MainWindow.axaml.cs:493-561`; Ctrl/Cmd+T is separately reserved for New Tab at `src/MapEditor.App/Views/MainWindow.axaml.cs:568-603`.
- Canvas pointer capture, preview movement, release, capture loss, and tool dispatch are at `src/MapEditor.App/Controls/MapCanvas.cs:143-189,212-345,401-450`; the current default path calls manual `BeginStroke` at `src/MapEditor.App/Controls/MapCanvas.cs:554-574`.
- The existing Graphic Viewer singleton focuses/reuses one modeless window at `src/MapEditor.App/Views/MainWindow.axaml.cs:966-1000`.
- Full asset replacement currently publishes one context and disposes the old one at `src/MapEditor.App/Rendering/AssetContextController.cs:68-91`; live terrain publication must not use this path.
- MainWindow's generic command runner commits canvas interactions before awaiting at `src/MapEditor.App/Views/MainWindow.axaml.cs:1142-1165`; asset/catalog replacement needs a terrain-aware cancellation path.
- `WorkspaceViewModel.CloseAllAsync` confirms and removes documents sequentially at `src/MapEditor.App/ViewModels/WorkspaceViewModel.cs:161-190`; a deferred Terrain Editor discard must not be applied before map close is known to succeed.

## Locked live publication contract

Implement Part 3's `ITerrainCatalogPublisher` in `AssetContextController`.

`PrepareSave(operation, expectedContext, preparedSave)` and `PrepareLoaded(operation, expectedContext, loadedReplacement, kind)` must run on the controller's creating thread, verify the currently held shared operation lease, and:

1. Reject a stale `expectedContext`, reentrant publication, or mismatched gate lease. `PrepareSave` also rejects an invalid prepared candidate; `PrepareLoaded` validates that `ValidReload` has a valid result and `ConfirmedMalformedReload` has an invalid revision-bearing result.
2. Reserve terrain publication so another terrain save/root commit cannot interleave.
3. Ask every document to allocate and validate a callback-free `TerrainDocumentReconciliation` plan, including reusable property-event arguments; no document fields change yet.
4. Snapshot notification delegates and preallocate a fixed-capacity `PublicationNotificationErrors` buffer large enough for every document/controller callback.
5. Snapshot and invoke all registered terrain-gesture cancellation callbacks.
6. Release and fail before file I/O if planning, allocation, or cancellation throws.

The save lease's `Commit(savedResult)` receives the matching operation-bound result; the loaded lease's parameterless `Commit()` uses its retained exact snapshot. Both then synchronously and nonthrowingly:

1. Replace only `AssetContext.Terrain`, preserving context/cache/renderer/tint identity.
2. Assign every precomputed document reconciliation without callbacks or allocations.
3. After all assignments, issue each document's property/canvas/palette notifications individually under exception isolation.
4. Raise dedicated `TerrainCatalogChanged`, invoking subscribers individually so one bad subscriber cannot prevent later subscribers.
5. Fill the preallocated failure buffer with any callback exceptions, assign it to `AssetContextController.LastPublicationNotificationErrors`, then release the reservation. Notification failures never roll back or throw from Commit.

Disposing an uncommitted lease releases the reservation and publishes nothing. Preparing a root does not reserve publication. Immediately before root commit, acquire the shared operation gate, precompute document state, any optional editor participant, delegate snapshots, event arguments, and failure capacity for the candidate, cancel terrain gestures, and then swap. Once swapped, root commit is allocation-free and nonthrowing: assign all document/participant state, issue guarded document/participant notifications, invoke `CurrentChanged` subscribers individually, guard old-context disposal, and fill the prepared failure result (including any disposal exception).

Capture `Environment.CurrentManagedThreadId` in the controller constructor and reject mutating publication/root-commit calls from another thread. This avoids relying on an unverified dispatcher API and pins the existing UI-thread ownership assumption.

## Task 1: Implement live terrain publication and gesture cancellation

**Files:**
- Modify: `src/MapEditor.App/Rendering/AssetContext.cs:7-102`
- Modify: `src/MapEditor.App/Rendering/AssetContextController.cs:35-113`
- Create: `src/MapEditor.App/Rendering/PreparedAssetContext.cs`
- Test: `tests/MapEditor.App.Tests/AssetContextControllerTests.cs`

**Mutation impact:**
- Source of truth changed: `AssetContext.Terrain` for terrain-only saves and `_current` for full root replacement.
- Important readers: all document view models, terrain selector/thumbnails, Terrain Editor, and Part 2 resolvers. Graphic Viewer continues reading `CurrentChanged` only.
- Derived/cached state affected: per-document terrain availability/selection, terrain controls, and canvas resolver snapshots. Sprite cache, renderer, tint cache, sheets, appearance, and graphics must retain identity on terrain-only publication.
- Required propagation: prepare reconciliation plans/cancel → durable save in Part 3 → terrain state replacement → callback-free assignment to every document → guarded document notifications → dedicated notifications. Full root: build candidate → resolve dirty editor in Task 5 → acquire gate/plan/cancel → swap/assign/notify/dispose nonthrowingly.
- Invariants: no observer sees half-reconciled documents; failure before Commit leaves old terrain published; committed terrain carries the exact durable file revision; confirmed invalid Reload publishes unavailable terrain state without affecting ordinary assets; terrain-only save never disposes sprite resources; callbacks unregister with their canvas.
- Observable proof: subscribe observers that inspect all documents and object identities during notification.

**Steps:**
1. Add red tests for creating-thread enforcement, stale context, invalid prepared save, confirmed revision-bearing invalid loaded replacement, reentrant prepare, uncommitted disposal, callback failure, and successful commit ordering.
2. Add `RegisterTerrainGestureCancellation(Action)` returning an idempotent `IDisposable`. Registration/removal and invocation are creating-thread-only; invoke a snapshot so callbacks may unregister themselves.
3. Let `AssetContext` replace only its terrain load result through an internal method; do not recreate `SpriteAssetCache`, `MapRenderer`, or `AvaloniaTintedSpriteCache`.
4. Add `PublicationNotificationErrors`, allocated during prepare with fixed capacity and exposed read-only with a final count, plus `LastPublicationNotificationErrors`. Add dedicated `TerrainCatalogChanged`; snapshot its delegates and allocate event arguments before file/root mutation, then invoke each subscriber after all state assignment and continue after subscriber exceptions. `PrepareSave` records the prepared operation ID and exact catalog/index references, so its Commit can publish the save result's new revision without allocation or fallible validation; `PrepareLoaded` retains the exact loaded result for its parameterless Commit. Fill the prepared error buffer only after state is committed; never throw from Commit.
5. Split root open into `TryPrepareOpen(path, out PreparedAssetContext, out failure)` and `CommitPreparedOpen(TerrainOperationLease operation, prepared, IPreparedAssetReconciliation? participant = null)`. Verify the operation is the gate's current lease before any mutation. The optional participant has allocation-free `Apply` and guarded `Notify` phases so the Terrain Editor can join the same root transaction without the controller depending on editor types. `TryOpen` remains as a compatibility wrapper. A prepared candidate owns/disposes its context until consumed; preparing does not acquire the operation gate.
6. Make post-swap root commit nonthrowing, including guarded document notifications and `CurrentChanged`, and always dispose the old context. Update the existing throwing-subscriber regression to assert committed candidate state and disposal.
7. Test late-added documents use current terrain, failed candidate/root cancellation preserves current state, a prepared participant applies before the first root observer and not at all on failure, and successful root replacement still raises one `CurrentChanged`.
8. Commit: `git commit -m "feat: publish terrain catalogs without reloading assets"`.

| Invariant | Proved by |
|---|---|
| All documents reconcile before first observer | `CommitTerrain_ObserverSeesEveryDocumentReconciled` |
| Terrain save preserves sprite resource identity | `CommitTerrain_PreservesContextCacheRendererAndTint` |
| Failed prepare/root switch leaves no partial publication | callback/stale/reentrant/candidate failure tests |

## Task 2: Add per-document terrain selection and reconciliation

**Files:**
- Modify: `src/MapEditor.App/ViewModels/ViewModelBase.cs:8-22`
- Modify: `src/MapEditor.App/ViewModels/MapDocumentViewModel.cs:15-174`
- Test: `tests/MapEditor.App.Tests/MapDocumentViewModelTests.cs`
- Test: `tests/MapEditor.App.Tests/AssetContextControllerTests.cs`

**Mutation impact:**
- Source of truth changed: each document's nullable `SelectedTerrainId` and shared immutable terrain load result reference.
- Important readers: terrain selector, Terrain tool enablement, canvas begin gesture, and tool synchronization.
- Derived/cached state affected: terrain choices, selected representative/color, terrain availability diagnostic, and active-tool fallback.
- Required propagation: controller asks document to build `TerrainDocumentReconciliation` with all choice allocations and next field values → controller builds every document plan → controller applies every plan without callbacks → only then controller asks each document to emit exact selection/tool/palette/canvas notifications.
- Invariants: publication never auto-selects a terrain; only the affected document stores selection; removed IDs fall back only when Terrain is active; map bytes/history/dirty state never change.
- Observable proof: hash map bytes and capture history/state before multiple publication scenarios.

**Steps:**
1. Add red tests for initial null selection, valid selection, selection activating Terrain, per-document independence, surviving publication, removed selection fallback, and non-Terrain tool preservation.
2. Add `SelectedTerrainId`, read-only `Terrains`, `TerrainAvailability`, and `SelectTerrain(Guid)`; reject IDs absent from the current valid index.
3. Add protected `RaisePropertyChangedSafely` in `ViewModelBase` that iterates a prepared delegate snapshot and records failures without changing normal `SetField` behavior. Add `PrepareTerrainReconciliation(TerrainCatalogLoadResult)`, callback-free `ApplyTerrainReconciliation(TerrainDocumentReconciliation)`, and `NotifyTerrainReconciliation(...)`. Preparation allocates all choices, changed-property lists, event arguments, and property/canvas/palette delegate snapshots; apply only assigns fields and cannot throw; notify runs after every document is applied, performs no allocation, catches each delegate exception into the caller-provided fixed buffer, and returns normally. None may call map session mutation APIs.
4. Extend active-tool validation for `MapEditTool.Terrain`. Directly setting Terrain without a valid selected ID throws before changing the game-data tool or any field. Toolbar and unmodified T are disabled/no-op without a selection; `SelectTerrain` is the normal activation path.
5. Add adversarial tests proving removed graphics alone do not mutate map cells and malformed terrain disables only terrain properties.
6. Commit: `git commit -m "feat: track terrain selection per map"`.

| Invariant | Proved by |
|---|---|
| Catalog publication does not choose for the user | `ApplyTerrainReconciliation_WithChoices_LeavesSelectionNull` |
| Removed selection has deterministic Pencil fallback | `ApplyTerrainReconciliation_RemovedSelectedId_FallsBackOnlyFromTerrain` |
| Reconciliation cannot dirty maps | `ApplyTerrainReconciliation_PreservesBytesHistoryAndDirtyState` |

## Task 3: Add Tiles/Terrains presentation, thumbnails, toolbar tool, and shortcut

**Files:**
- Modify: `src/MapEditor.App/Views/MainWindow.axaml:87-128`
- Modify: `src/MapEditor.App/Views/MainWindow.axaml.cs:65-146,356-420,493-561,693-709,1186-1240`
- Create: `src/MapEditor.App/Controls/TerrainThumbnailControl.cs`
- Modify: `src/MapEditor.App/Styles/Icons.axaml`
- Test: `tests/MapEditor.App.Tests/MainWindowTests.cs`
- Test: `tests/MapEditor.App.Tests/MainWindowDefaultLayoutTests.cs`
- Test: `tests/MapEditor.App.Tests/ShortcutTests.cs`
- Test: `tests/MapEditor.App.Tests/ToolbarIconTests.cs`
- Test: `tests/MapEditor.App.Tests/TerrainThumbnailControlTests.cs`

**Mutation impact:**
- Source of truth changed: active left-panel tab and document terrain selection/tool through UI commands.
- Important readers: active document bindings, tool buttons, canvas, Add/Edit command enablement.
- Derived/cached state affected: selector entries, thumbnail draw operations, tab contents, and checked toolbar state.
- Required propagation: ComboBox selection → `SelectTerrain` → `ActiveTool = Terrain` → property events → tool button/selector synchronization. Catalog event → document choices first → controls redraw.
- Invariants: Tiles workflow/layout remains intact; terrain entries show color/name/representative; unmodified T never steals text input; Ctrl/Cmd+T still creates a map tab.
- Observable proof: headless window tests switch documents/tabs and assert controls, selected IDs, and tools.

**Steps:**
1. Add a left `TabControl`: keep Load Assets and directory status above it; move sheet combo, numeric brush fields, palette host, and scrollbar unchanged into `Tiles`; add `Terrains` with one ComboBox plus Add/Edit buttons.
2. Use a ComboBox item template with a solid color swatch, terrain name, and `TerrainThumbnailControl`. The thumbnail draws the representative through current `AssetContext.Resolve` and owns no bitmap; placeholders follow `SpritePaletteControl` behavior at `src/MapEditor.App/Controls/SpritePaletteControl.cs:108-187`.
3. Add tests that valid empty enables Add but disables selector/Edit, malformed data exposes the diagnostic/recovery path while Tiles remains usable, and selected terrain restores on document switches.
4. Add `IconTerrain`, `TerrainTool`, checked-state synchronization beside existing controls at `MainWindow.axaml.cs:1220-1232`, and unmodified `T` handling after the text-box guard. Preserve Ctrl/Cmd+T tunnel behavior.
5. Dispose/unsubscribe thumbnail controls when recycled/closed; terrain-only publication redraws thumbnails without reloading the Graphic Viewer.
6. Run focused App tests and commit: `git commit -m "feat: add terrain palette and tool"`.

| Invariant | Proved by |
|---|---|
| Existing Tiles palette remains operational | `TerrainTabs_TilesPreservesSheetBrushAndPalette` |
| Selecting an entry activates only that document | multi-document selector test |
| T and Ctrl/Cmd+T retain distinct behavior | shortcut theory including focused TextBox |

## Task 4: Route real map terrain gestures

**Files:**
- Modify: `src/MapEditor.App/Controls/MapCanvas.cs:35-100,141-189,260-345,401-450,554-574`
- Modify: `src/MapEditor.App/Controls/SpritePaletteControl.cs:18-52,77-106,201-220`
- Modify: `src/MapEditor.App/ViewModels/MapDocumentViewModel.cs`
- Modify: `src/MapEditor.App/Views/MainWindow.axaml.cs`
- Test: `tests/MapEditor.App.Tests/MapCanvasTests.cs`

**Mutation impact:**
- Source of truth changed: Part 2 terrain stroke previews the active layer in the live map, then commits through existing map history.
- Important readers: map renderer, title/commands/dirty state, App timeline, publication cancellation registry, and error dialogs.
- Derived/cached state affected: canvas gesture kind/capture, selected tile readout, undo/redo availability, and document invalidations.
- Required propagation: Terrain press → capture selected terrain/index and Shift erase mode → Part 2 begin → register active gesture state → moves call terrain continuation → invalidate live preview → release completes one history command; Escape/capture loss/publication cancels and restores.
- Invariants: terrain capture loss cancels while ordinary Pencil capture loss keeps current commit behavior; a zero-delta erase remains captured so dragging can reach terrain but releases with no command if unchanged; topmost layer comes from Core; App never resolves terrain itself.
- Observable proof: real pointer tests assert map values before release, after cancel/release, and after undo/redo.

**Steps:**
1. Add red real-pointer tests for one-cell paint, sparse interpolation, live eight-neighbor repair, Shift erase-any-recognized, direct manual overwrite, untouched manual neighbors, and noncontiguous selected-layer masks.
2. Add explicit Terrain dispatch before the current default manual branch. Construct/use `TerrainMapResolver` from the exact immutable catalog snapshot and call Part 2 `BeginTerrainStroke`/`ContinueTerrainStroke`.
3. Track `_terrainStroke` separately from `_stroking`. Capture Shift at press through `TerrainEditMode`; later modifier changes do not affect the gesture. A valid zero-delta erase remains active/captured; only begin validation failure avoids capture.
4. On continuation failure, clear canvas gesture flags and stored pointer before calling `Capture(null)`, then refresh once and emit one error. This prevents reentrant capture-loss or release from calling `CompleteStroke` after Core already cancelled itself.
5. On release, complete as normal. On Escape, terrain capture loss, root replacement, or terrain publication, cancel. Preserve current commit-on-capture-loss behavior for ordinary strokes.
6. Register each canvas's idempotent terrain cancellation callback with `AssetContextController`; make both `MapCanvas` and `SpritePaletteControl` idempotently disposable, and dispose registrations/view-model/window/controller handlers on document removal and main-window close.
7. Surface `TerrainResolutionFailure` through a terrain error event and existing `ErrorPresentation`; one failure yields one actionable error and no history.
8. Run `MapCanvasTests`, App suite, and commit: `git commit -m "feat: paint terrains on map canvas"`.

| Invariant | Proved by |
|---|---|
| Release commits exactly one normal map edit | `TerrainDrag_Release_OneUndoRestoresWholeGesture` |
| Every cancellation path restores exact bytes | Escape/capture-loss/publication/root-switch tests |
| Manual and terrain capture loss remain distinct | paired Pencil/Terrain regression test |

## Task 5: Integrate the singleton Terrain Editor and asset/application lifecycle

**Files:**
- Modify: `src/MapEditor.App/App.axaml.cs:35-62`
- Modify: `src/MapEditor.App/Views/MainWindow.axaml.cs:38-146,956-1000,1046-1165`
- Modify: `src/MapEditor.App/Dialogs/IEditorDialogs.cs`
- Modify: `tests/MapEditor.App.Tests/Fakes/FakeEditorDialogs.cs`
- Test: `tests/MapEditor.App.Tests/MainWindowTests.cs`
- Test: `tests/MapEditor.App.Tests/MainWindowCloseTests.cs`
- Test: `tests/MapEditor.App.Tests/AppStartupTests.cs`

**Mutation impact:**
- Source of truth changed: singleton editor/window ownership, pending terrain-close decision, active asset context, and draft binding.
- Important readers: Add/Edit buttons, main close guard, root switch command, publisher, and all terrain-aware controls.
- Derived/cached state affected: editor selected draft terrain, sheet image/context, Add/Edit enablement, and close prompt state.
- Required propagation: Add/Edit → focus existing or construct/bind one editor → create/select requested draft terrain. Root switch → prepare candidate first → resolve dirty editor → commit candidate → apply deferred discard/rebind editor. App close → resolve editor decision → close maps → only then apply deferred discard/close editor/assets.
- Invariants: one modeless editor; unsaved draft never affects map tools; failed/cancelled root change preserves root and draft; deferred Discard cannot lose work if a later map-close prompt cancels; assets outlive both modeless windows.
- Observable proof: headless lifecycle tests inspect visible windows, draft/catalog state, contexts, and disposal after each branch.

**Steps:**
1. Add one Terrain Editor field/factory beside the Graphic Viewer singleton. Add focuses/creates a terrain in the draft; Edit focuses/selects the active document's terrain. Repeated commands never create a second instance.
2. Inject controller/store/publisher creation through `App.ComposeMainWindow` so tests use real composition with fake I/O/dialog boundaries.
3. Add an interaction policy to MainWindow command execution instead of always calling `Canvas.FinishInteraction(commit: true)`. Ordinary commands retain Commit; asset selection preserves interaction while choosing/preparing a candidate; immediately before successful root commit, terrain strokes cancel and manual strokes commit. A canceled/invalid candidate leaves the active gesture and current root unchanged.
4. Replace MainWindow's direct root open with `TryPrepareOpen`; use the first candidate only to validate the selected path before prompting. Then acquire one `TerrainOperationLease`, ask the dirty Terrain Editor for Save/Discard/Cancel, and pass the same lease into any requested Save. Cancel/failing Save disposes the candidate and preserves current root/draft.
5. After every wait/prompt/save completes, dispose the first candidate and call `TryPrepareOpen` again under the same lease so final root state cannot predate the old root's terrain save. If this final preparation fails, preserve the current root and any deferred Discard (a completed Save remains legitimately saved). For Discard, defer mutation until root commit. Prepare `TerrainEditorRebind` from the refreshed candidate with allocation-free Apply/guarded Notify phases, then call `CommitPreparedOpen(operation, refreshed, rebind)`; the controller applies it with document fields before any notification. No await or user callback occurs between final preparation and this final synchronous commit.
6. If a terrain save/conflict operation already owns the gate, root switch and main close wait for its completion and retry rather than disposing dependencies. For app close, request a terrain decision first. Save may complete immediately; hold Discard as a token. Run `Workspace.CloseAllAsync`; if it cancels, retain the draft. Only on success apply deferred discard, close both modeless windows, dispose every `DocumentView` control, then dispose assets.
7. Add tests for Add/Edit singleton behavior, invalid-catalog recovery, dirty root-switch branches, failed initial/refreshed candidates, same-root Save then switch using the saved revision, root-switch-during-save, close-during-conflict, main-close/map-cancel preservation, and no callbacks after close.
8. Commit: `git commit -m "feat: integrate terrain editor lifecycle"`.

| Invariant | Proved by |
|---|---|
| Dirty draft cannot be discarded for a failed root | `LoadAssets_InvalidCandidate_PreservesTerrainDraft` |
| Map close cancellation preserves deferred terrain discard | `Close_TerrainDiscardThenMapCancel_KeepsDraft` |
| Windows release resources before assets | disposal-order integration test |

## Task 6: Add end-to-end regression coverage and smoke instructions

**Files:**
- Create: `tests/MapEditor.App.Tests/TerrainEditorEndToEndTests.cs`
- Modify: `tests/MapEditor.App.Tests/Fixtures/AssetFixture.cs:9-58`
- Modify: `docs/map-editor-smoke.md`

**Mutation impact:**
- Source of truth changed: test fixtures and smoke documentation only.
- Important readers: full App composition and release validation.
- Derived/cached state affected: none in production.
- Required propagation under test: author draft → durable save → live publish → select terrain → map preview/commit → undo/redo → map save/reopen.
- Invariants: the complete user workflow works through real domain objects; malformed terrain never blocks ordinary map editing; map format remains unchanged.
- Observable proof: compare terrain JSON bytes, map bytes/decoded values, history counts, UI state, and resource lifetimes.

**Steps:**
1. Extend `AssetFixture` with deterministic terrain JSON, explicit Grass/Water interior/edge/corner variants, malformed files, overlapping frames, and corrupt sheets.
2. Add a real end-to-end headless test: open Terrain Editor → add Grass/Water → paint center/sides/corners on full sheets → local undo/redo → save → assert selector publication → paint adjacent map regions → Shift erase → one-step map undo/redo → save/reopen map.
3. Add adversarial workflows for active stroke during publication, best-effort external conflict detection, malformed recovery, dirty root switch, and removed graphic remaining unchanged in a map.
4. Update `docs/map-editor-smoke.md` with the approved manual sequence, fixed 80% overlay expectation, transitions on both sides, diagonal repair, Shift erase, one-gesture undo, map reopen, malformed recovery, and dirty root prompts.
5. Run all verification commands below, fix regressions, and commit: `git commit -m "test: verify terrain editor workflow"`.

| Invariant | Proved by |
|---|---|
| Durable catalog reaches all map readers | end-to-end save/publish/paint test |
| Terrain map files remain ordinary maps | end-to-end save/reopen plus Part 2 codec test |
| Optional terrain failure cannot disable Tiles | malformed recovery workflow |

## Final verification

Run:

```bash
dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj -v minimal
dotnet test tests/MapEditor.Rendering.Tests/MapEditor.Rendering.Tests.csproj -v minimal
dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj -v minimal
dotnet test Goose2ClientGodot.sln -v minimal
dotnet build src/MapEditor.App/MapEditor.App.csproj -c Release
bash tests/build-map-editor-script-tests.sh
git diff --check
git status --short
```

Also run isolation checks:

```bash
test -z "$(git diff --name-only 5711959...HEAD -- tools/AssetConverter)"
git diff --exit-code 5711959...HEAD -- src/MapEditor.Core/MapCodec.cs
```

Expected: all tests/builds pass; AssetConverter and map codec are unchanged; generated assets remain uncommitted.
