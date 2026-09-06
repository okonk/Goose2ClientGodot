# Map Editor Google Sheets Integration — Part 3: Editing Implementation Plan

**Goal:** Expose the Part 1–2 game-data synchronization through the editor, with independent per-tab spawn/warp state, direct canvas/property editing, safe clipboard and coordinated undo behavior, dirty lifecycle prompts, and resize propagation.

**Architecture:** `MapEditor.App` owns application-wide connectivity and per-document presentation state. `MapEditor.GameData` remains the owner of sheet rows and their reversible mutations; `MapEditor.Core` remains map-only. `MapEditor.Rendering` gains provider-neutral marker inputs and draw operations, but no NPC appearance composition or art loading. A document-level edit timeline chooses map, sheet, or compound resize history without merging the two persistence baselines.

**Tech Stack:** C#/.NET 10; Avalonia 11 headless UI tests; Part 1 `SheetEditSession`; Part 2 `GameDataConnectivity`, `GameDataSyncCoordinator`, and explicit sync results; xUnit 2.9.2.

---

> Implement with @executing-plans, one task and commit at a time. Parts 1 and 2 must be merged first. Run commands from `/home/agent/workspace/Goose2ClientGodot/.worktrees/map-editor-google-sheets`. Do not add comments or doc strings unless the repository exception applies.

## Scope

Included:

- Game Data menu and compact toolbar controls for Connect/Disconnect, Pull, Push, overlays, and preview-mode toggle;
- application-wide connection/remembered URL with independent confirmed map, sync session, selection, tool, visibility, dirty state, and history per tab;
- remembered-spreadsheet entry, map-row confirmation/search, and reusable in-panel NPC/map search pickers;
- spawn and warp canvas tools, right-side properties, deterministic marker hit selection, drag/move, delete, and destination-coordinate editing;
- individual spawn/warp clipboard with guarded cross-tab paste, while rectangular clipboard remains tile-only;
- most-recent-domain undo/redo and one-step compound resize undo/redo;
- separate sheet dirty prompts during Pull, Disconnect, tab close, and application close;
- resize coordinate transforms, crop confirmation, self-warp destination transforms, and inbound-warp warning.

Excluded:

- any global spawn/warp list or spawn/warp management dialog;
- overlap disambiguation UI beyond selecting the first spawn occurrence;
- NPC sprite composition, appearance assets, or NPC art rendering;
- cross-map arrows, animated previews, inbound-warp mutation, merge UI, or implicit Push on map Save.

The preview-mode command is present and stored per document, but in Part 3 it only preserves selectable spawn markers and reports that art preview is unavailable; Part 4 will replace markers with art. It must not hide markers or disable editing.

## Verified repository and prerequisite facts

- The approved Pull flow remembers only the spreadsheet URL, suggests a map by filename, requires explicit map-row confirmation, and publishes no partial Pull: `docs/plans/2026-09-06-map-editor-google-sheets-design.md:112-121`.
- Push conflicts require Overwrite/Pull Instead/Cancel, while confirmed success alone advances the sheet baseline: `docs/plans/2026-09-06-map-editor-google-sheets-design.md:123-139`.
- Direct canvas/sidebar behavior, first-occurrence overlap selection, and marker-only warps are fixed by the approved design: `docs/plans/2026-09-06-map-editor-google-sheets-design.md:141-171`.
- Tile selections exclude game data; individual game-data clipboard, same-spreadsheet cross-tab checks, and most-recent-domain undo are required: `docs/plans/2026-09-06-map-editor-google-sheets-design.md:173-187`.
- Resize must move loaded sources, confirm cropped entries, transform self-warps, and warn that inbound warps are unavailable: `docs/plans/2026-09-06-map-editor-google-sheets-design.md:189-198`.
- Part 1 exposes index-addressed spawn/warp add/remove/move operations and a pushed dirty baseline: `docs/plans/2026-09-06-map-editor-google-sheets-part1-schema-foundation.md:198-215`.
- Part 2 makes coordinator outcomes explicit for this UI to translate and latches ambiguous writes until Pull: `docs/plans/2026-09-06-map-editor-google-sheets-part2-connectivity.md:122-133`.
- The existing window binds menu enablement to the active document and hosts one right panel: `src/MapEditor.App/Views/MainWindow.axaml:9-33`, `src/MapEditor.App/Views/MainWindow.axaml:62-68`, `src/MapEditor.App/Views/MainWindow.axaml:110-163`.
- Every open document already receives a separate view model and map session, while only the clipboard is workspace-shared: `src/MapEditor.App/ViewModels/WorkspaceViewModel.cs:20-35`, `src/MapEditor.App/ViewModels/WorkspaceViewModel.cs:197-203`.
- Tab activation commits the outgoing canvas interaction and swaps a per-document `MapCanvas`: `src/MapEditor.App/Views/MainWindow.axaml.cs:325-360`, `src/MapEditor.App/Views/MainWindow.axaml.cs:380-381`.
- Canvas gestures already own pointer capture, release, capture-loss, and Escape cancellation: `src/MapEditor.App/Controls/MapCanvas.cs:119-165`, `src/MapEditor.App/Controls/MapCanvas.cs:263-303`, `src/MapEditor.App/Controls/MapCanvas.cs:326-356`.
- Keyboard Copy/Cut/Paste and Undo/Redo currently dispatch directly to tile/map methods and avoid stealing text-box input: `src/MapEditor.App/Views/MainWindow.axaml.cs:405-450`, `src/MapEditor.App/Views/MainWindow.axaml.cs:468-506`.
- Map resize creates one reversible map command and raises the forward/inverse transform on apply, undo, and redo: `src/MapEditor.Core/Editing/MapEditSession.cs:321-373`, `src/MapEditor.Core/Editing/MapEditSession.cs:393-435`; transform signs are defined at `src/MapEditor.Core/Editing/MapEditCommand.cs:87-117`.
- Existing close flow activates a dirty tab before prompting and processes tabs sequentially: `src/MapEditor.App/ViewModels/WorkspaceViewModel.cs:145-189`; window close already guards async re-entry and active gestures: `src/MapEditor.App/Views/MainWindow.axaml.cs:841-887`.
- Rendering currently paints map layers first and editing overlays last through `MapRenderOptions`/`IMapDrawSink`: `src/MapEditor.Rendering/Composition/MapRenderer.cs:59-180`, `src/MapEditor.Rendering/Composition/MapRenderOptions.cs:5-23`, `src/MapEditor.Rendering/Composition/IMapDrawSink.cs:3-12`.
- Real Avalonia pointer tests use headless windows and raw mouse input: `tests/MapEditor.App.Tests/MapCanvasTests.cs:43-61`, `tests/MapEditor.App.Tests/MapCanvasTests.cs:190-227`; close tests exercise modal gates and re-entrant lifecycle behavior: `tests/MapEditor.App.Tests/MainWindowCloseTests.cs:168-192`, `tests/MapEditor.App.Tests/MainWindowCloseTests.cs:253-283`.

## Locked behavioral contracts

### Ownership and selection

- `GameDataConnectivity` and authorization are application-wide. Every `MapDocumentViewModel` owns at most one `GameDataSyncSession`; successful Pull replaces only that tab's prior session.
- A tab never persists a map-row association. Reopening or repulling asks again, with an ordinal filename match as suggestion only. Untitled, unmatched, or multiply matched filenames have no automatic selection.
- Selected entries are occurrence identities (`Spawn, index` or `Warp, index`), not row-value identities. Recompute/clear indexes after every edit, Pull, undo/redo, resize, and tab close so duplicates never redirect an action.
- Clicking a spawn tile selects the lowest current spawn index. Clicking a warp source selects its sole occurrence. In spawn/warp tools, selection wins over creation.

### Tool and property behavior

- Add an App-owned `EditorTool` covering existing map tools plus `Spawn` and `Warp`; do not add sheet concepts to `MapEditor.Core.MapEditTool`. Translate to `MapEditTool` only when starting a map edit.
- Spawn creation requires a selected NPC. Warp creation requires a selected destination map and valid destination coordinates. Invalid or incomplete properties perform no mutation.
- A press on an existing entry starts a pending drag; crossing into another tile previews the source marker, and release adds exactly one move command. Click/release on the original tile only selects. Escape or capture loss cancels without history.
- Right-property coordinate commits are one move/update command. Delete and keyboard Delete remove the selected occurrence and select nothing.
- `Use Selected Tile` is enabled only when exactly one open tab has the same spreadsheet ID and confirmed destination map ID and has a selected in-bounds tile; otherwise it is disabled rather than guessing.

### Clipboard and timeline

- Replace `SharedTileClipboard` with a shared discriminated editor clipboard containing exactly one of tile rectangle, spawn NPC ID plus source spreadsheet ID, or warp destination plus source spreadsheet ID. Tile selection copy/cut/paste behavior remains unchanged.
- Copy/Cut targets a selected game-data occurrence when Spawn/Warp is active; otherwise it targets only the rectangular tile selection. Game-data Paste applies immediately at the destination tab's selected tile; it is rejected without mutation when no selected tile, no pulled session, or spreadsheet IDs differ.
- Each document maintains a monotonic edit timeline containing `Map`, `Sheet`, or `ResizeCompound`. Every effective new edit clears redo in both underlying histories and the timeline. Push only changes `SheetEditSession`'s baseline and creates no timeline entry.
- Undo/redo consumes the timeline's latest entry. A compound resize invokes exactly one map-history and one sheet-history transition under notification suppression. Map-only and sheet-only entries never change the other persistence baseline.

### Resize

For a map window `(X, Y, Width, Height)`, use the core-emitted forward transform `(-X, -Y, Width, Height)`; never independently infer another sign convention.

- Shift every loaded spawn and warp source by the transform offset; remove those outside the new half-open bounds.
- For every warp whose destination map ID equals the current confirmed map ID, shift destination coordinates by the same offset. Do not delete a warp merely because its destination becomes out of bounds; Push validation reports it.
- Before mutation, present exact cropped spawn/warp counts and the inbound-warp warning whenever the document has pulled data. Cancel leaves map, sheet rows, selections, dirty baselines, and both histories unchanged.
- Confirm applies one map resize command and one bulk sheet transform command and records one compound timeline entry. Undo restores cropped occurrences, order, source/destination coordinates, selection validity, and old dimensions; redo reapplies them.

## Mutation impact matrix

| Mutation | Source of truth | Readers / derived state | Required propagation | Failure atomicity |
|---|---|---|---|---|
| Connect/Disconnect | application `GameDataConnectivity` | menu/toolbar connection labels and enabled state | auth result → connection notification → all document commands | Cancel/failure keeps prior connection; Disconnect cannot proceed past a dirty tab that was not pushed/discarded |
| Pull | selected spreadsheet/map through coordinator | tab sync session, references, pickers, markers, sheet history | URL → catalog → explicit map choice → complete Pull → atomic tab publication | Any cancel/failure keeps prior session, selection, timeline, and dirty state |
| Spawn/warp edit | tab `SheetEditSession` | selected occurrence, properties, marker inputs, dirty indicator, timeline | validate → mutate exact index → record Sheet timeline → refresh panel/canvas/commands | Invalid fields/index/cross-tab paste mutate nothing |
| Push | coordinator and tab sync session | snapshots, sheet baseline, `RequiresPull`, command state | validate/open dimensions → conflict choice → result translation → refresh | Only confirmed success clears dirty; ambiguous result remains dirty and disables Push until Pull |
| Clipboard replace | workspace shared editor clipboard | Copy/Cut/Paste command state in all tabs | capture immutable payload + spreadsheet provenance → notify all tabs | Rejected paste leaves clipboard and destination untouched |
| Undo/redo | document edit timeline plus two domain histories | rows/map, dirty flags, markers, title, command enablement | pop timeline → replay designated domain(s) → repair selection → refresh | Compound replay is prevalidated; if alignment invariant fails, stop before either replay and surface an error |
| Resize | core map history + sheet bulk command | dimensions, tiles, sources, self destinations, selection, both dirty states | preview transform/counts → confirmation → both commands → compound timeline | Cancel/no-op is zero mutation; confirmed operation is one reversible unit |
| Close | map dirty baseline and sheet dirty baseline | tab/window lifetime and active tab | finish gesture → sheet Push/Discard/Cancel → map Save/Discard/Cancel → remove/dispose | Any Cancel or failed Push/Save keeps tab and both data sets alive |

## Invariant matrix

| Invariant | Required proof |
|---|---|
| Tabs never share sync sessions, selection, visibility, or history | two-tab view-model and real tab-switch tests |
| Map Save never pushes or clears sheet dirty state | controller/window test with recording gateway |
| Pull publishes only after URL and map confirmation | cancelled URL/map picker and late gateway failure tests |
| Existing entry selection precedes creation; overlap picks first occurrence | real canvas pointer tests with duplicate spawns |
| One drag produces one sheet command; Escape/capture loss produces none | real pointer/reparent tests and undo assertions |
| Tile clipboard never captures rows; game-data paste enforces spreadsheet identity | mixed selection and cross-tab matrix tests |
| Ctrl+Z/Y follows latest effective domain | interleaved map/sheet UI shortcut test |
| Compound resize is one undo/redo and preserves duplicate order | resize domain tests plus real window command test |
| Push changes only sheet baseline | timeline/baseline test after Push then map undo |
| Every destructive sheet lifecycle path offers Push/Discard/Cancel | Pull, Disconnect, tab close, and window close tests |
| Markers are after map art and remain present in preview mode | rendering operation-order tests |
| No NPC art/list/dialog enters Part 3 | dependency/file review and named-control assertions |

## Task 1: Add bulk sheet edits and document-level coordinated history

**Files:**
- Modify: `src/MapEditor.Core/Editing/MapEditHistory.cs`
- Modify: `src/MapEditor.Core/Editing/MapEditSession.cs`
- Modify: `tests/MapEditor.Core.Tests/MapEditHistoryTests.cs`
- Modify: `tests/MapEditor.Core.Tests/MapEditSessionTests.cs`
- Modify: `src/MapEditor.GameData/Editing/SheetEditSession.cs`
- Modify: `src/MapEditor.GameData/Editing/SheetEditCommand.cs`
- Create: `src/MapEditor.App/ViewModels/DocumentEditTimeline.cs`
- Modify: `src/MapEditor.App/ViewModels/MapDocumentViewModel.cs`
- Modify: `src/MapEditor.App/Controls/MapCanvas.cs`
- Modify: `tests/MapEditor.GameData.Tests/Editing/SheetEditSessionTests.cs`
- Create: `tests/MapEditor.App.Tests/DocumentEditTimelineTests.cs`
- Modify: `tests/MapEditor.App.Tests/MapCanvasTests.cs`

**Mutation impact:** Add atomic `UpdateSpawn`, `UpdateWarp`, and bulk replace/transform commands that retain exact before/after arrays, occurrence order, and state IDs. Extend both edit sessions with the same provider-neutral coordination surface: a monotonically increasing `HistoryVersion`, a `HistoryChanged` event raised after every effective new command/undo/redo/clear, and `DiscardRedo()` that removes only redo entries and raises the event only when state changed. `MapEditHistory` remains the owner of map command stacks; `MapEditSession` forwards this narrow surface without exposing command payloads. These APIs change history metadata/stacks only and must not alter document contents or existing dirty semantics by themselves. Route effective map and sheet edits through the document timeline, including canvas stroke completion, flood fill, blocked patch, tile paste/delete, property edits, and resize.

Publication order is mutate the owning session successfully, advance/raise that session's history version, then record the domain/version in the document timeline. During coordinated replay, suppress timeline recording before invoking the owning session and restore it afterward. If a compound map/data operation cannot complete both domains, replay the already-applied side immediately and publish no timeline entry.

**Step 1: Write failing tests**

- In GameData tests, prove full-field updates and bulk transforms are one command, duplicate order/cropped rows round-trip exactly, no-op transforms create no history, failed validation is atomic, and `MarkPushed` remains independent.
- In Core tests, prove `HistoryVersion` and `HistoryChanged` advance exactly once for each effective command/undo/redo/clear, remain unchanged for no-ops, and `DiscardRedo()` removes redo without mutating map tiles or changing the save baseline.
- In timeline tests, interleave map paint → sheet add → map paste; undo/redo in exact reverse/forward order. Undo twice then make a new sheet edit and assert redo is unavailable in both domains. Mark map saved and sheet pushed at different points and assert each dirty flag independently.
- Add a compound entry test that resizes tiles and transforms/crops rows, then one Undo/Redo restores/reapplies both. Corrupt/misalign a test seam and assert neither domain replays.
- Extend real `MapCanvasTests` to prove completed and cancelled map gestures create timeline entries rather than bypassing coordination.

**Step 2: Run red**

```bash
dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj --filter 'FullyQualifiedName~MapEditHistory|FullyQualifiedName~MapEditSession'
dotnet test tests/MapEditor.GameData.Tests/MapEditor.GameData.Tests.csproj --filter 'FullyQualifiedName~SheetEditSession'
dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj --filter 'FullyQualifiedName~DocumentEditTimeline|FullyQualifiedName~MapCanvas'
```

Expected: FAIL because the Core coordination surface, bulk commands, cross-domain timeline, and routed gesture notifications are absent.

**Step 3: Implement minimum**

Use immutable before/after row arrays for bulk commands instead of emitting N public edits. Implement the provider-neutral coordination surface in `MapEditHistory`/`MapEditSession` and mirror it in `SheetEditSession`; neither side references the other domain. The timeline stores only domain and expected before/after history versions; underlying histories continue owning payloads. Suppress timeline recording during replay. Before publishing every effective new edit, call `DiscardRedo()` on the other domain, then record the completed owner mutation/version. Do not put sheet types in `MapEditor.Core`, and do not make Push or Pull timeline entries.

**Step 4: Run green**

```bash
dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj --filter 'FullyQualifiedName~MapEditHistory|FullyQualifiedName~MapEditSession'
dotnet test tests/MapEditor.GameData.Tests/MapEditor.GameData.Tests.csproj --filter 'FullyQualifiedName~SheetEditSession'
dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj --filter 'FullyQualifiedName~DocumentEditTimeline|FullyQualifiedName~MapCanvas'
```

**Step 5: Commit**

```bash
git add src/MapEditor.Core/Editing/MapEditHistory.cs src/MapEditor.Core/Editing/MapEditSession.cs \
  tests/MapEditor.Core.Tests/MapEditHistoryTests.cs tests/MapEditor.Core.Tests/MapEditSessionTests.cs \
  src/MapEditor.GameData/Editing tests/MapEditor.GameData.Tests/Editing \
  src/MapEditor.App/ViewModels/DocumentEditTimeline.cs \
  src/MapEditor.App/ViewModels/MapDocumentViewModel.cs src/MapEditor.App/Controls/MapCanvas.cs \
  tests/MapEditor.App.Tests/DocumentEditTimelineTests.cs tests/MapEditor.App.Tests/MapCanvasTests.cs
git commit -m "feat: coordinate map and sheet edit history"
```

## Task 2: Attach synchronization state and implement Pull/Push lifecycle

**Files:**
- Create: `src/MapEditor.App/ViewModels/DocumentGameDataState.cs`
- Create: `src/MapEditor.App/Connectivity/GameDataCommandController.cs`
- Modify: `src/MapEditor.App/ViewModels/WorkspaceViewModel.cs`
- Modify: `src/MapEditor.App/ViewModels/MapDocumentViewModel.cs`
- Modify: `src/MapEditor.App/App.axaml.cs`
- Modify: `src/MapEditor.App/Dialogs/IEditorDialogs.cs`
- Modify: `src/MapEditor.App/Dialogs/AvaloniaEditorDialogs.cs`
- Modify: `src/MapEditor.App/Dialogs/EditorDialogsProxy.cs`
- Modify: `tests/MapEditor.App.Tests/Fakes/FakeEditorDialogs.cs`
- Create: `tests/MapEditor.App.Tests/GameDataCommandControllerTests.cs`
- Modify: `tests/MapEditor.App.Tests/WorkspaceViewModelTests.cs`
- Modify: `tests/MapEditor.App.Tests/AppStartupTests.cs`

**Mutation impact:** Inject the one composed Part 2 connectivity owner into the workspace, but construct a new `DocumentGameDataState` for every new/opened tab. The command controller is the sole UI translator for coordinator results and dialog choices. It computes open-map dimensions from tabs with matching spreadsheet/map IDs immediately before Push.

**Step 1: Write failing tests**

Cover:

- successful Connect and failed/cancelled Connect; no browser/API work at startup;
- canonical URL entry saved only after valid parse; remembered URL prefilled;
- filename suggestion by ordinal basename, no suggestion for untitled/unmatched/duplicate filename, and explicit map confirmation every Pull;
- cancellation at URL/catalog/map confirmation and late Pull failure preserving the previous session byte-for-byte;
- dirty repull choices Push/Discard/Cancel, including failed/conflicted Push preventing discard unless separately chosen;
- Push success, validation presentation, Overwrite, Pull Instead, Cancel, authentication reauthorization, and ambiguous lockout;
- open-destination dimensions include only same-spreadsheet confirmed tabs;
- two tabs have distinct sync state while sharing connectivity; closing/disposal unsubscribes callbacks.

Use a scripted coordinator/connection seam, not Google types. Assert call order and final observable state, not only returned choices.

**Step 2: Run red**

```bash
dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj --filter 'FullyQualifiedName~GameDataCommandController|FullyQualifiedName~WorkspaceViewModel|FullyQualifiedName~AppStartup'
```

Expected: FAIL because documents have no game-data state and sync results have no presentation layer.

**Step 3: Implement minimum**

Add narrowly typed dialog methods/results for spreadsheet URL, map confirmation, local-sheet dirty choice, Push conflict, and messages/errors. Keep map/NPC search matching ordinal-ignore-case over decimal ID, name, and filename; stable sort by ID and preserve the exact selected record. Pull publishes a new state only after the coordinator succeeds. Disconnect iterates dirty documents in workspace order, activates each, and requires Push or Discard before deleting the token; Cancel/failure leaves connection and remaining sessions intact. Do not add spawn/warp list UI.

**Step 4: Run green**

```bash
dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj --filter 'FullyQualifiedName~GameDataCommandController|FullyQualifiedName~WorkspaceViewModel|FullyQualifiedName~AppStartup'
```

**Step 5: Commit**

```bash
git add src/MapEditor.App/Connectivity/GameDataCommandController.cs \
  src/MapEditor.App/ViewModels src/MapEditor.App/App.axaml.cs src/MapEditor.App/Dialogs \
  tests/MapEditor.App.Tests/Fakes/FakeEditorDialogs.cs \
  tests/MapEditor.App.Tests/GameDataCommandControllerTests.cs \
  tests/MapEditor.App.Tests/WorkspaceViewModelTests.cs tests/MapEditor.App.Tests/AppStartupTests.cs
git commit -m "feat: attach game data sync to map tabs"
```

## Task 3: Add Game Data menu, toolbar, confirmation UI, and right-side pickers

**Files:**
- Modify: `src/MapEditor.App/Views/MainWindow.axaml`
- Modify: `src/MapEditor.App/Views/MainWindow.axaml.cs`
- Modify: `src/MapEditor.App/Styles/Icons.axaml`
- Create: `src/MapEditor.App/Controls/SearchPickerControl.axaml`
- Create: `src/MapEditor.App/Controls/SearchPickerControl.axaml.cs`
- Create: `src/MapEditor.App/Dialogs/SpreadsheetDialog.axaml`
- Create: `src/MapEditor.App/Dialogs/SpreadsheetDialog.axaml.cs`
- Create: `src/MapEditor.App/Dialogs/MapReferenceDialog.axaml`
- Create: `src/MapEditor.App/Dialogs/MapReferenceDialog.axaml.cs`
- Create: `tests/MapEditor.App.Tests/SearchPickerControlTests.cs`
- Create: `tests/MapEditor.App.Tests/GameDataDialogTests.cs`
- Create: `tests/MapEditor.App.Tests/MainWindowGameDataTests.cs`
- Modify: `tests/MapEditor.App.Tests/MainWindowTests.cs`
- Modify: `tests/MapEditor.App.Tests/ToolbarIconTests.cs`

**Mutation impact:** The active tab remains the window `DataContext`; connection state comes from the shared command controller. Add Game Data controls without moving existing File/Edit/View behavior. Swap the right panel between the existing layer/tile content and spawn/warp properties based on active tool/selection. Picker filtering is view-only and never mutates pulled catalogs.

**Step 1: Write failing real UI tests**

- Instantiate actual controls under a shown headless window. Type search text, verify ID/name/filename filtering, keyboard Up/Down/Enter selection, Escape cancellation, focus, stable ordering, empty results, and selection preservation.
- Show actual spreadsheet/map dialogs owner-modally; click confirm/cancel/window-close and assert typed results. Map dialog must visibly distinguish suggested row without auto-confirming it.
- Through real menu items/buttons, verify Connect↔Disconnect label, Pull/Push enablement, overlay and preview toggles, Spawn/Warp tool exclusivity, per-tab restoration after tab switching, and `_commandRunning` preventing duplicate async commands.
- Assert ordinary tools retain the original right panel. Spawn properties show NPC/source/Delete; warp properties show destination/source/destination coordinates/Use Selected Tile/Delete. Assert no global spawn/warp list control exists.

**Step 2: Run red**

```bash
dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj --filter 'FullyQualifiedName~SearchPickerControl|FullyQualifiedName~GameDataDialog|FullyQualifiedName~MainWindowGameData|FullyQualifiedName~ToolbarIcon'
```

Expected: FAIL because controls and Game Data chrome are absent.

**Step 3: Implement minimum**

Add named Game Data menu items and a separated compact toolbar group. Bind enabled/check state to explicit view-model/controller properties; do not infer it in code-behind from labels. `SearchPickerControl` accepts immutable item projections and returns the original typed item. Preview toggle must leave spawn markers enabled and show a nonfatal unavailable status in this part. Reuse existing command serialization and close guards rather than starting parallel modal flows.

**Step 4: Run green**

```bash
dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj --filter 'FullyQualifiedName~SearchPickerControl|FullyQualifiedName~GameDataDialog|FullyQualifiedName~MainWindowGameData|FullyQualifiedName~MainWindow|FullyQualifiedName~ToolbarIcon'
```

**Step 5: Commit**

```bash
git add src/MapEditor.App/Views/MainWindow.axaml src/MapEditor.App/Views/MainWindow.axaml.cs \
  src/MapEditor.App/Styles/Icons.axaml src/MapEditor.App/Controls/SearchPickerControl.* \
  src/MapEditor.App/Dialogs/SpreadsheetDialog.* src/MapEditor.App/Dialogs/MapReferenceDialog.* \
  tests/MapEditor.App.Tests/SearchPickerControlTests.cs tests/MapEditor.App.Tests/GameDataDialogTests.cs \
  tests/MapEditor.App.Tests/MainWindowGameDataTests.cs tests/MapEditor.App.Tests/MainWindowTests.cs \
  tests/MapEditor.App.Tests/ToolbarIconTests.cs
git commit -m "feat: add game data editor controls"
```

## Task 4: Render markers and implement canvas/property editing

**Files:**
- Modify: `src/MapEditor.Rendering/Composition/MapRenderOptions.cs`
- Modify: `src/MapEditor.Rendering/Composition/MapDrawOperations.cs`
- Modify: `src/MapEditor.Rendering/Composition/IMapDrawSink.cs`
- Modify: `src/MapEditor.Rendering/Composition/MapRenderer.cs`
- Modify: `src/MapEditor.App/Rendering/AvaloniaMapDrawSink.cs`
- Modify: `src/MapEditor.App/Controls/MapCanvas.cs`
- Modify: `src/MapEditor.App/ViewModels/MapDocumentViewModel.cs`
- Modify: `src/MapEditor.App/Views/MainWindow.axaml.cs`
- Create: `tests/MapEditor.Rendering.Tests/GameDataMarkerRenderingTests.cs`
- Create: `tests/MapEditor.App.Tests/GameDataCanvasTests.cs`
- Create: `tests/MapEditor.App.Tests/GameDataPropertiesTests.cs`

**Mutation impact:** Derive immutable visible marker inputs from the active tab's current row lists on each render request. Rendering never owns or mutates rows. Pointer press records an occurrence and origin; move changes only preview state; release commits through `SheetEditSession`. Property commits use the same view-model mutation methods as canvas actions.

**Step 1: Write failing rendering tests**

Prove spawn and warp markers are culled to visible/in-bounds cells, have distinct draw kinds, draw after all sprites and blocked/grid overlays but before final selection affordance, preserve one marker input per occurrence, identify the selected occurrence, and remain emitted when preview mode is on. Assert no sprite/appearance operation is introduced.

**Step 2: Write failing real interaction tests**

Using shown Avalonia windows and raw input:

- click empty tile to add, click occupied tile to select without adding, and duplicate-spawn click selects index zero;
- drag spawn/warp across tiles and outside/re-enter; release gives one undo entry, while Escape/capture loss gives none;
- warp tool cannot create a duplicate source and surfaces validation without mutation;
- edit NPC, destination map, destination coordinates, and source coordinates through actual property controls; invalid text retains focus/error and does not mutate;
- Delete button and keyboard Delete remove the exact selected occurrence;
- `Use Selected Tile` works with exactly one matching associated tab and disables for none/ambiguous/different-spreadsheet tabs;
- overlay off hides markers without disabling hit/edit state; preview mode still shows selectable markers.

**Step 3: Run red**

```bash
dotnet test tests/MapEditor.Rendering.Tests/MapEditor.Rendering.Tests.csproj --filter 'FullyQualifiedName~GameDataMarkerRendering'
dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj --filter 'FullyQualifiedName~GameDataCanvas|FullyQualifiedName~GameDataProperties'
```

Expected: FAIL because marker operations and spawn/warp interactions are absent.

**Step 4: Implement minimum**

Use marker draw operations with occurrence kind/index, tile, selected flag, and destination metadata needed for diagnostics only. Do not reference Google or appearance assets. Extend `FinishInteraction` so tab switches/commands commit an effective game-data drag once, while Escape and capture loss cancel it. Hit testing uses current row order, not draw-operation geometry. Property edits and canvas edits converge on the same validation/mutation methods.

**Step 5: Run green**

```bash
dotnet test tests/MapEditor.Rendering.Tests/MapEditor.Rendering.Tests.csproj --filter 'FullyQualifiedName~GameDataMarkerRendering'
dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj --filter 'FullyQualifiedName~GameDataCanvas|FullyQualifiedName~GameDataProperties|FullyQualifiedName~MapCanvas'
```

**Step 6: Commit**

```bash
git add src/MapEditor.Rendering/Composition src/MapEditor.App/Rendering/AvaloniaMapDrawSink.cs \
  src/MapEditor.App/Controls/MapCanvas.cs src/MapEditor.App/ViewModels/MapDocumentViewModel.cs \
  src/MapEditor.App/Views/MainWindow.axaml.cs \
  tests/MapEditor.Rendering.Tests/GameDataMarkerRenderingTests.cs \
  tests/MapEditor.App.Tests/GameDataCanvasTests.cs tests/MapEditor.App.Tests/GameDataPropertiesTests.cs
git commit -m "feat: edit spawn and warp markers on the canvas"
```

## Task 5: Add individual clipboard and coordinated keyboard commands

**Files:**
- Create: `src/MapEditor.App/ViewModels/EditorClipboard.cs`
- Modify: `src/MapEditor.App/ViewModels/SharedTileClipboard.cs`
- Modify: `src/MapEditor.App/ViewModels/MapDocumentViewModel.cs`
- Modify: `src/MapEditor.App/ViewModels/WorkspaceViewModel.cs`
- Modify: `src/MapEditor.App/Views/MainWindow.axaml.cs`
- Modify: `tests/MapEditor.App.Tests/CrossMapPasteTests.cs`
- Create: `tests/MapEditor.App.Tests/GameDataClipboardTests.cs`
- Modify: `tests/MapEditor.App.Tests/ShortcutTests.cs`

**Mutation impact:** Generalize the one workspace clipboard while retaining immutable tile payloads. Clipboard provenance contains only spreadsheet ID, never a sync session or tab reference. Keyboard commands dispatch by active domain and focused control; text boxes continue receiving standard editing shortcuts.

**Step 1: Write failing tests**

Create a matrix for Copy/Cut/Paste of spawn and warp in the same tab, same-spreadsheet other tab, different-spreadsheet tab, and unpulled tab. Assert payload contains only NPC ID or warp destination fields, source remains unchanged after Copy, Cut is one reversible sheet edit, Paste uses the destination selected tile/current map ID, and rejected Paste is zero mutation/history. Add mixed rectangular/game-entry selection tests proving tiles never carry game rows and active Spawn/Warp selection takes precedence only in that tool.

In real shortcut tests, perform map edit → spawn edit → Ctrl+Z → Ctrl+Z → Ctrl+Y twice and assert visible tiles, markers, right-panel selection, dirty indicators, and menu enablement after each key. Verify Copy/Cut/Paste and Delete are not intercepted from focused picker/property text boxes.

**Step 2: Run red**

```bash
dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj --filter 'FullyQualifiedName~CrossMapPaste|FullyQualifiedName~GameDataClipboard|FullyQualifiedName~Shortcut'
```

Expected: FAIL because the clipboard is tile-only and shortcuts select only map history.

**Step 3: Implement minimum**

Keep one discriminated payload and one Changed event. Immediate game-data Paste does not enter tile paste mode. Copy never edits; Cut captures before deleting. Refresh all command properties after clipboard replacement and timeline replay. Rejection uses a nonmodal validation presentation so clipboard contents remain available for a valid destination.

**Step 4: Run green**

```bash
dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj --filter 'FullyQualifiedName~CrossMapPaste|FullyQualifiedName~GameDataClipboard|FullyQualifiedName~Shortcut'
```

**Step 5: Commit**

```bash
git add src/MapEditor.App/ViewModels/EditorClipboard.cs \
  src/MapEditor.App/ViewModels/SharedTileClipboard.cs src/MapEditor.App/ViewModels/MapDocumentViewModel.cs \
  src/MapEditor.App/ViewModels/WorkspaceViewModel.cs src/MapEditor.App/Views/MainWindow.axaml.cs \
  tests/MapEditor.App.Tests/CrossMapPasteTests.cs tests/MapEditor.App.Tests/GameDataClipboardTests.cs \
  tests/MapEditor.App.Tests/ShortcutTests.cs
git commit -m "feat: add spawn and warp clipboard editing"
```

## Task 6: Make close, disconnect, and resize destructive flows safe

**Files:**
- Modify: `src/MapEditor.App/Dialogs/ResizeMapDialog.axaml`
- Modify: `src/MapEditor.App/Dialogs/ResizeMapDialog.axaml.cs`
- Modify: `src/MapEditor.App/Dialogs/IEditorDialogs.cs`
- Modify: `src/MapEditor.App/Dialogs/AvaloniaEditorDialogs.cs`
- Modify: `src/MapEditor.App/Documents/EditorDocumentController.cs`
- Modify: `src/MapEditor.App/ViewModels/WorkspaceViewModel.cs`
- Modify: `src/MapEditor.App/ViewModels/MapDocumentViewModel.cs`
- Modify: `src/MapEditor.App/Views/MainWindow.axaml.cs`
- Modify: `tests/MapEditor.App.Tests/Fakes/FakeEditorDialogs.cs`
- Modify: `tests/MapEditor.App.Tests/ResizeMapDialogTests.cs`
- Create: `tests/MapEditor.App.Tests/GameDataResizeTests.cs`
- Modify: `tests/MapEditor.App.Tests/EditorDocumentControllerTests.cs`
- Modify: `tests/MapEditor.App.Tests/MainWindowCloseTests.cs`

**Mutation impact:** Extend close confirmation into two explicit baselines without coupling map Save to Push. Resize preview reads sheet rows to calculate destructive effects but performs no mutation. Confirmed resize submits a precomputed transform to both histories as one timeline entry. The existing async window close guard remains the single owner of re-entry.

**Step 1: Write failing resize tests**

- Unit-test transform planning for grow west/north, crop east/south, wholly displaced windows, boundary coordinates, duplicate rows, and self/non-self warps. Assert exact cropped counts and retained order.
- Show the real resize dialog with pulled state; verify it displays non-empty tile count plus spawn/warp crop counts and inbound warning. Cancel/window-close changes nothing.
- Through the real Resize menu command, confirm resize, then one Ctrl+Z/Ctrl+Y restores/reapplies tiles, dimensions, spawn/warp sources, self destinations, selection, both dirty states, and exact duplicate ordering.
- Assert no prompt and no sheet edit for an unpulled tab; identity resize is a no-op; non-self destination coordinates never shift.

**Step 2: Write failing lifecycle tests**

For tab close, Close All, app window close, and Disconnect, cover clean/dirty combinations and Push/Discard/Cancel. Required order is sheet prompt first, then existing map prompt; successful Push followed by cancelled map Save keeps the tab open with sheet clean and map dirty. Failed/conflicted/ambiguous Push keeps the tab open and dirty. Discarding sheet changes must not mark the map saved; discarding map changes must not mark sheet pushed. A pending sheet prompt must preserve the existing duplicate-close and command-running guards.

Add a recording gateway assertion that map Save/Save As never performs Pull/Push and sheet Push never calls `MapFileStore` or changes map `SavedStateId`.

**Step 3: Run red**

```bash
dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj --filter 'FullyQualifiedName~GameDataResize|FullyQualifiedName~ResizeMapDialog|FullyQualifiedName~EditorDocumentController|FullyQualifiedName~MainWindowClose'
```

Expected: FAIL because resize ignores rows and close checks only map dirty state.

**Step 4: Implement minimum**

Pass a read-only resize-impact model into the dialog; keep transform planning in the view model/domain helper, not XAML code-behind. Add a sheet close guard that can Push through `GameDataCommandController`, discard by allowing document destruction only, or cancel. Do not mutate/mark clean on Discard because the tab is about to be destroyed; for dirty repull, use coordinator-approved replacement instead. Run sheet confirmation before `EditorDocumentController.ConfirmCloseAsync`, preserving sequential tab activation and the window's existing re-entry guard.

**Step 5: Run green and full suite**

```bash
dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj --filter 'FullyQualifiedName~GameDataResize|FullyQualifiedName~ResizeMapDialog|FullyQualifiedName~EditorDocumentController|FullyQualifiedName~MainWindowClose'
dotnet test tests/MapEditor.GameData.Tests/MapEditor.GameData.Tests.csproj
dotnet test tests/MapEditor.Rendering.Tests/MapEditor.Rendering.Tests.csproj
dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj
dotnet test Goose2ClientGodot.sln
dotnet build Goose2ClientGodot.sln
```

**Step 6: Commit**

```bash
git add src/MapEditor.App/Dialogs src/MapEditor.App/Documents/EditorDocumentController.cs \
  src/MapEditor.App/ViewModels/WorkspaceViewModel.cs src/MapEditor.App/ViewModels/MapDocumentViewModel.cs \
  src/MapEditor.App/Views/MainWindow.axaml.cs tests/MapEditor.App.Tests/Fakes/FakeEditorDialogs.cs \
  tests/MapEditor.App.Tests/ResizeMapDialogTests.cs tests/MapEditor.App.Tests/GameDataResizeTests.cs \
  tests/MapEditor.App.Tests/EditorDocumentControllerTests.cs tests/MapEditor.App.Tests/MainWindowCloseTests.cs
git commit -m "feat: safeguard game data resize and close flows"
```

## Final red-team verification

```bash
dotnet test tests/MapEditor.GameData.Tests/MapEditor.GameData.Tests.csproj
dotnet test tests/MapEditor.GameData.Google.Tests/MapEditor.GameData.Google.Tests.csproj
dotnet test tests/MapEditor.Rendering.Tests/MapEditor.Rendering.Tests.csproj
dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj
dotnet test Goose2ClientGodot.sln
dotnet build Goose2ClientGodot.sln
git status --short
```

Confirm manually from the diff:

- `MapEditor.Core` contains no game-data, Google, marker, or sheet-history types;
- no Google type appears in App view models, dialogs, rendering, or public provider-neutral contracts;
- no map Save/Save As path invokes Push and no Push path invokes map persistence;
- every tab owns its own sync/edit/timeline/selection/visibility state and disposes subscriptions on close;
- every effective edit is represented once in the coordinated timeline, and new edits invalidate redo in both domains;
- duplicate spawn occurrences retain order and first-index selection behavior through drag, delete, clipboard, resize, and undo/redo;
- cross-tab game-data paste compares exact spreadsheet IDs and never relies on remembered URL or connection identity;
- resize uses the emitted `MapResizeTransform`, confirms source cropping before mutation, shifts only self-warp destinations, and never claims to update inbound warps;
- markers remain selectable in both marker and preview mode, with no NPC art/appearance-manifest work;
- no global spawn/warp list or management dialog was added;
- no credential, token, spreadsheet ID, or machine-specific path is committed.
