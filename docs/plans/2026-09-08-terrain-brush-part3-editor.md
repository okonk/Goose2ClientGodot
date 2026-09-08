# Terrain Brush Part 3: Editor Integration Implementation Plan

**Goal:** Load and validate terrain catalogs without disrupting ordinary assets, expose per-document terrain editing and management UI, wire real canvas gestures to Part 2, and atomically save/publish reviewed terrain sets.

**Architecture:** `MapEditor.Rendering` turns Part 1 JSON plus the sprite manifest into an immutable runtime terrain asset catalog and an availability result. `MapEditor.App` owns per-document palette/tool/mode/selection state, synchronous canvas gestures, editable manager drafts, and the durable-save-before-publication boundary; catalog replacement synchronously cancels active terrain gestures before readers can observe the replacement. Existing sprite rendering remains terrain-agnostic because Part 2 writes ordinary map tile layers.

**Tech Stack:** C#; .NET 8; Avalonia 11 headless UI tests; `System.Text.Json`; xUnit; Part 1 `MapEditor.Core.Terrain` catalog APIs; Part 2 `TerrainMapResolver` and terrain stroke APIs.

---

> Implement with @executing-plans, one task and commit at a time. Follow `AGENTS.md`: add no comments or doc strings unless a non-obvious invariant requires one.

## Scope and prerequisites

Parts 1 and 2 are prerequisites. Consume their locked contracts unchanged except for adding the app-visible `Terrain` tool enum value in Task 2:

- Part 1 owns catalog JSON, schema validation, topology/masks, statuses, diagnostics, and canonical serialization: `docs/plans/2026-09-08-terrain-brush-part1-converter.md:68-176`.
- Part 2 owns immutable runtime resolution and `MapEditSession.BeginTerrainStroke`, `ContinueTerrainStroke`, `CompleteStroke`, and `CancelStroke`: `docs/plans/2026-09-08-terrain-brush-part2-core-editing.md:42-91`.

Included here:

- optional `terrain-brushes.json` loading and enabled-frame validation against the already loaded manifest;
- graceful terrain unavailability while sprites, maps, and manual tools continue working;
- per-document Tiles/Terrain palette mode, terrain selection, Paint/Erase mode, and Terrain tool;
- `T` shortcut and real pointer preview/commit/cancel behavior;
- manager drafts for names, topology, review status, required masks, variants, provenance, and diagnostics;
- enabling validation, atomic save, then in-process catalog publication and open-document refresh;
- cancellation of active terrain gestures before any catalog replacement;
- automated cross-layer tests and a real generated-corpus smoke.

Excluded: converter changes, inference, map format changes, terrain-aware rendering, closest-mask fallback, transitions between terrain types, manager override files, and persistence of per-document UI selection. No map or database migration is needed: terrain edits remain ordinary map layer changes, and palette/tool/mode state is session-only.

## APIs and facts verified before planning

| API / fact | Citation and consequence |
|---|---|
| Missing/malformed terrain JSON must not block sprites/maps; successful manager save is durable before catalog publication and cancels active terrain gestures | `docs/plans/2026-09-08-terrain-brush-design.md:108-133`. Treat terrain as an optional sub-resource, unlike the required sprite manifest. |
| Terrain palette lists enabled sets; selection activates Terrain; `T` is the shortcut; Paint/Erase are explicit per-document choices | `docs/plans/2026-09-08-terrain-brush-design.md:84-92`. Keep these values on `MapDocumentViewModel`, not global settings/window fields. |
| Sprite manifest exposes sorted frames and exact rectangle lookup via `TryGetSourceRect(SpriteReference, out SpriteSourceRect)` | `src/MapEditor.Rendering/Assets/SpriteManifest.cs:14-55,225-251`. Frame validation must use this manifest metadata and require 32×32 without loading sheet images. |
| `SpriteAssetCache.Open` first loads the required manifest; frame image loading remains lazy in `Resolve` | `src/MapEditor.Rendering/Assets/SpriteAssetCache.cs:51-67,69-112`. Terrain loading must not call `Resolve` or create a second sprite cache. |
| `AssetContext.Create` opens the sprite cache first and already degrades a missing/corrupt optional appearance sidecar | `src/MapEditor.App/Rendering/AssetContext.cs:50-67`. Follow that optional-resource pattern for terrain while retaining one shared cache/manifest. |
| Context replacement currently publishes `_current`, updates every document's sheets, invalidates canvas/palette, persists settings, then disposes the old context | `src/MapEditor.App/Rendering/AssetContextController.cs:36-80`. Insert terrain cancellation before `_current = candidate`; do not dispose the old resolver/cache while a gesture still references it. |
| Documents added after context publication are seeded in the collection-changed handler | `src/MapEditor.App/Rendering/AssetContextController.cs:82-96`. Seed terrain state there as well as in the replacement loop. |
| `MapDocumentViewModel` owns active tool, brush, sheets, selected sheet, refresh events, and session completion/cancellation | `src/MapEditor.App/ViewModels/MapDocumentViewModel.cs:31-67,106-183,376-386,658-683`. Add terrain state and wrappers here so every document retains independent selection/mode. |
| `MapCanvas` captures the pointer after a gesture starts, interpolates through session continuation, commits on release, cancels on Escape, but currently commits normal strokes on capture loss | `src/MapEditor.App/Controls/MapCanvas.cs:107-142,174-230,246-293,315-344`. Terrain capture loss must cancel while legacy manual capture-loss behavior remains unchanged. |
| `FinishInteraction(false)` calls the session cancellation path and clears capture/state | `src/MapEditor.App/Controls/MapCanvas.cs:43-81`. Add a terrain-only cancellation entry point rather than using this broad method during catalog publication, so panning/selection/manual strokes are not disturbed. |
| Sprite palette uses viewport-bounded custom rendering, `AssetContext.Resolve`, per-document invalidation, and direct hit testing | `src/MapEditor.App/Controls/SpritePaletteControl.cs:18-64,100-143,145-215`. Terrain palette should mirror its rendering/scroll ownership and use representative variants from the runtime catalog. |
| Main window creates one canvas/palette view per document, commits outgoing interactions before tab activation, and binds the active palette scrollbar | `src/MapEditor.App/Views/MainWindow.axaml.cs:38-65,257-300,322-323`. Extend `DocumentView` to own both palette controls and switch the hosted child from document palette mode. |
| Unmodified tool shortcuts are ignored while a `TextBox` has focus | `src/MapEditor.App/Views/MainWindow.axaml.cs:359-437`. Add `T` in the same switch after the text-input guard. |
| Headless canvas tests drive real Avalonia pointer capture and assert map/history outcomes | `tests/MapEditor.App.Tests/MapCanvasTests.cs:31-145,213-272,650-739`. Extend this real harness for terrain; do not replace gesture lifecycle with mocks. |
| Existing asset tests prove optional appearance degradation, successful replacement/disposal, all-document propagation, and late-document seeding | `tests/MapEditor.App.Tests/AssetContextControllerTests.cs:89-194,236-286,353-421`. Add terrain cases beside these, preserving all existing outcomes. |

## Locked Part 3 APIs and behavior

Add to `MapEditor.Rendering`:

```csharp
public readonly record struct TerrainAvailability(bool IsAvailable, string? Diagnostic);

public sealed class TerrainAssetCatalog
{
    public TerrainCatalog Source { get; }
    public TerrainMapResolver Resolver { get; }
    public IReadOnlyList<TerrainSetDefinition> EnabledSets { get; }

    public static TerrainAssetLoadResult Load(string assetDirectory, SpriteManifest manifest);
    public static TerrainAssetLoadResult Validate(TerrainCatalog catalog, SpriteManifest manifest);
}

public sealed record TerrainAssetLoadResult(
    TerrainCatalog? Source,
    TerrainAssetCatalog? Runtime,
    TerrainAvailability Availability,
    IReadOnlyList<TerrainValidationIssue> Issues);
```

`Load` reads `<assetDirectory>/terrain-brushes.json` and returns actionable unavailability for expected file/JSON failures. Parsed but runtime-invalid data remains in `Source` for repair. `Validate` combines Core validation with enabled-only manifest existence and exact 32×32 checks; unresolved pending/disabled data remains reviewable. Success builds one resolver, sorts enabled sets by display name then ID, and uses the first required-mask variant as representative.

Add app state:

```csharp
internal enum AssetPaletteMode { Tiles, Terrain }

public AssetPaletteMode PaletteMode { get; set; }
public string? SelectedTerrainId { get; set; }
public TerrainEditMode TerrainMode { get; set; } // default Paint
public bool IsTerrainAvailable { get; }
public string? TerrainDiagnostic { get; }
```

`MapEditTool.Terrain` is routed only to `BeginTerrainStroke`; normal `BeginStroke` rejects it. Selection activates Terrain palette/tool. `T` does so only with an available selection, otherwise preserving the manual tool and showing the diagnostic. Replacement preserves an enabled ID, falls back to the first enabled set, or clears selection and returns Terrain to Pencil; Paint/Erase mode persists.

## Mutation impact matrix

| Mutation | Source and readers | Propagation / atomicity |
|---|---|---|
| Open assets | Manifest plus optional terrain JSON → context/documents/palettes | Build and validate candidate; cancel old terrain gestures; publish; seed documents; dispose old. Bad terrain degrades only terrain; bad manifest preserves old context. |
| Select terrain state | Per-document palette/ID/mode → window/palette/canvas | Notify and redraw only; never touch map/history. |
| Pointer gesture | Part 2 active stroke → map renderer/history/title | Preview cumulatively; release creates one command; failure/Escape/capture loss restores exact state. |
| Edit manager | Isolated draft → manager views/validation | Rebuild and validate draft only; file/runtime/map remain unchanged. |
| Save manager | Valid draft → JSON/runtime/documents | Validate/build; serialize; cancel gesture while reversible; durable sibling-temp replace; publish resolver through a nonthrowing path; reconcile documents. Any failed save preserves file/runtime. |

## Invariant-to-test matrix

| Invariant | Primary proof |
|---|---|
| Terrain failure never blocks sprite/map/manual editing | loader, context, and malformed-terrain end-to-end tests |
| Enabled references exist and are exactly 32×32 | Rendering missing/non-32/member adversarial tests |
| Selection and Paint/Erase mode are per document | multi-tab view-model/window tests |
| One drag is one Part 2 undo command | headless paint/erase canvas tests |
| Escape, capture loss, and replacement restore previews | encoded-byte/history-version adversarial tests |
| Draft edits cannot leak before save | draft/service identity tests |
| Reversible cancellation precedes durable save, which precedes nonthrowing publication | injected cancellation/write/flush/replace and publication-order tests |
| Publication cannot leave a stale active resolver | replacement-during-preview test |

### Task 0: Load, validate, and degrade terrain assets in Rendering

**Files:**
- Create: `src/MapEditor.Rendering/Terrain/TerrainAvailability.cs`
- Create: `src/MapEditor.Rendering/Terrain/TerrainAssetCatalog.cs`
- Create: `tests/MapEditor.Rendering.Tests/TerrainAssetCatalogTests.cs`

**Mutation impact:**
- Source of truth changed: none; `terrain-brushes.json` and the already parsed `SpriteManifest` are read-only inputs.
- Important readers: `AssetContext` in Task 1, terrain palettes/gestures, and manager draft creation.
- Derived/cached state affected: immutable enabled-set ordering, representative references, and one `TerrainMapResolver`. No global cache or image ownership is added.
- Required propagation sequence: read optional file → Part 1 parse → shared catalog validation → enabled-only manifest/32×32 checks → construct resolver/runtime index → return one complete result.
- Invariants to preserve: expected terrain failures become unavailable results; parsed invalid review data remains available as `Source`; pending/disabled unresolved references do not poison valid enabled runtime sets; sprite sheets are never loaded.
- Observable proof required: assert final result/catalog/issue values and a normal `SpriteAssetCache.Resolve` path separately, not only exception handling.

**Step 1: Write failing tests**

Add:

- `Load_MissingFile_ReturnsUnavailableWithActionablePath`
- `Load_MalformedOrUnsupportedJson_ReturnsUnavailableWithoutThrowing`
- `Validate_CompleteEnabledSetWithExactFrames_BuildsResolverAndSortedPaletteSets`
- `Validate_EnabledMissingFrameOrNon32Frame_ReturnsExactIssuesAndNoRuntime`
- `Validate_PendingAndDisabledUnresolvedFrames_RemainReviewable`
- `Validate_DuplicateEnabledMembershipReturnsNoRuntime`
- adversarial `Validate_VariantFrameExistsButMemberFrameDoesNot_IsRejected`
- `Load_DoesNotInvokeSpriteSheetLoader` using a real `SpriteManifest` and a loader fake that throws if called.

Generate complete masks through `TerrainMasks.Required`; do not hand-author 16/47 copies.

**Step 2: Verify red**

```bash
dotnet test tests/MapEditor.Rendering.Tests/MapEditor.Rendering.Tests.csproj \
  --filter 'FullyQualifiedName~TerrainAssetCatalogTests' -v minimal
```

Expected: compile failure because terrain asset types do not exist.

**Step 3: Implement the immutable loader**

Catch only expected file/JSON/catalog exceptions and produce diagnostics; allow `OutOfMemoryException` and programming errors to escape. Convert `TerrainGraphicReference` to `SpriteReference` only at this boundary. Sort issues by `(TerrainId, Mask, Sheet, Graphic, Code, Message)` and defensively copy all exposed lists. `Validate` must not mutate review status to make a catalog pass.

**Step 4: Verify green and Rendering regressions**

```bash
dotnet test tests/MapEditor.Rendering.Tests/MapEditor.Rendering.Tests.csproj \
  --filter 'FullyQualifiedName~TerrainAssetCatalogTests|FullyQualifiedName~SpriteManifestTests|FullyQualifiedName~SpriteAssetCacheTests' -v minimal
```

**Step 5: Commit**

```bash
git add src/MapEditor.Rendering/Terrain tests/MapEditor.Rendering.Tests/TerrainAssetCatalogTests.cs
git commit -m "feat: load terrain asset catalogs"
```

### Task 1: Add document terrain state and publish it through the asset context

**Files:**
- Modify: `src/MapEditor.App/ViewModels/MapDocumentViewModel.cs:31-183,658-683`
- Modify: `src/MapEditor.App/Rendering/AssetContext.cs:7-82`
- Modify: `src/MapEditor.App/Rendering/AssetContextController.cs:10-104`
- Modify: `tests/MapEditor.App.Tests/Fixtures/AssetFixture.cs:8-34`
- Modify: `tests/MapEditor.App.Tests/MapDocumentViewModelTests.cs`
- Modify: `tests/MapEditor.App.Tests/AssetContextControllerTests.cs`

**Mutation impact:**
- Source of truth changed: `_current` in `AssetContextController` remains the published asset context; each context gains an immutable terrain load result. Each `MapDocumentViewModel` owns its selected enabled terrain ID and Paint/Erase mode.
- Important readers: every document, both palette controls, `MapCanvas`, manager command, and status chrome.
- Derived/cached state affected: each document's available enabled IDs/selection and palette invalidation; existing sprite cache/tint cache ownership is unchanged. Palette mode and Terrain-tool fallback are added in Task 2, so this task exposes catalog/selection/mode state without referencing those future UI values.
- Required propagation sequence:
  1. Fully construct candidate sprite cache, terrain load result, renderer, and optional appearance.
  2. Invoke a synchronous `TerrainCatalogReplacing` event while old context is still current.
  3. Publish `_current = candidate`.
  4. Call `document.SetTerrainCatalog(candidate.Terrain)` then existing sheet seeding and refresh for every open document.
  5. Persist settings and dispose the replaced context.
- Publication boundary: event handlers may cancel old gestures but cannot observe candidate via `Current`; after `_current` assignment all readers see a fully validated candidate. Failure before assignment leaves the old context and gestures intact.
- Invariants to preserve: bad terrain still publishes good sprites; bad required manifest publishes nothing; replacing terrain never creates another sprite cache; late documents receive current terrain state.
- Observable proof required: assert context identity/disposal, sprite resolution, document terrain state, invalidation counts, and callback ordering.

**Step 1: Write failing tests**

Extend `AssetFixture` with `WriteTerrainCatalog(TerrainCatalog)` using `TerrainCatalogJson.Serialize`. Add document tests `SetTerrainCatalog_PreservesEnabledSelectionByIdOrChoosesFirst`, `SetTerrainCatalog_UnavailableClearsSelection`, and `TerrainModeAndSelection_AreIndependentAcrossDocuments`, then add:

- `TryOpen_MissingTerrain_PublishesSpritesAndUnavailableTerrain`
- `TryOpen_MalformedTerrain_PublishesSpritesAndActionableDiagnostic`
- `TryOpen_InvalidEnabledFrames_RetainsSourceForManagerButNoRuntime`
- `TryOpen_ValidTerrain_SeedsEveryDocumentAndLateDocuments`
- `TryOpen_Replacement_RaisesTerrainReplacingBeforeCurrentChangesAndOldDisposal`
- adversarial `TryOpen_BadBaseManifest_DoesNotRaiseReplacingOrChangePublishedTerrain`.

**Step 2: Verify red**

```bash
dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj \
  --filter 'FullyQualifiedName~AssetContextControllerTests' -v minimal
```

Expected: new tests fail to compile because context/controller terrain state is absent.

**Step 3: Implement construct/cancel/publish propagation**

Add the Part 3 app-state properties `SelectedTerrainId`, `TerrainMode`, `IsTerrainAvailable`, and `TerrainDiagnostic` to `MapDocumentViewModel`, plus `SetTerrainCatalog(TerrainAssetLoadResult)`. Reconciliation preserves an enabled ID, otherwise selects the first enabled set by runtime order, or clears selection. The method raises the exact property notifications and `Refresh(EditorRefresh.Canvas | EditorRefresh.Palette)` after replacing its immutable catalog reference; it never mutates map/history.

Add `TerrainAssetLoadResult Terrain` to `AssetContext`; unavailable context uses an explicit sprite-assets-unavailable terrain diagnostic. Add `event Action? TerrainCatalogReplacing` to the controller. Event invocation is synchronous on the existing Avalonia/UI caller context; `TryOpen` is synchronous and existing callers invoke it on that context. If an event handler throws, dispose the candidate and preserve `_current`; do not persist settings or dispose the old context.

`OnDocumentsChanged` calls `SetTerrainCatalog` before the new document can begin a gesture. Do not add file watching or background reload.

**Step 4: Verify green**

```bash
dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj \
  --filter 'FullyQualifiedName~AssetContextControllerTests' -v minimal
```

**Step 5: Commit**

```bash
git add src/MapEditor.App/ViewModels/MapDocumentViewModel.cs \
  src/MapEditor.App/Rendering/AssetContext.cs \
  src/MapEditor.App/Rendering/AssetContextController.cs \
  tests/MapEditor.App.Tests/Fixtures/AssetFixture.cs \
  tests/MapEditor.App.Tests/MapDocumentViewModelTests.cs \
  tests/MapEditor.App.Tests/AssetContextControllerTests.cs
git commit -m "feat: publish terrain asset availability"
```

### Task 2: Add Tiles/Terrain palettes, Terrain tool state, and shortcut

**Files:**
- Modify: `src/MapEditor.Core/Editing/MapEditTool.cs:1-12`
- Modify: `src/MapEditor.Core/Editing/MapEditSession.cs:491-498`
- Create: `src/MapEditor.App/ViewModels/AssetPaletteMode.cs`
- Create: `src/MapEditor.App/Controls/TerrainPaletteControl.cs`
- Modify: `src/MapEditor.App/Views/MainWindow.axaml`
- Modify: `src/MapEditor.App/Views/MainWindow.axaml.cs:38-65,257-323,359-437,1090-1175`
- Modify: `src/MapEditor.App/Styles/Icons.axaml`
- Create: `tests/MapEditor.App.Tests/TerrainPaletteControlTests.cs`
- Modify: `tests/MapEditor.App.Tests/MapDocumentViewModelTests.cs`
- Modify: `tests/MapEditor.App.Tests/MainWindowDefaultLayoutTests.cs`
- Modify: `tests/MapEditor.App.Tests/ShortcutTests.cs`

**Mutation impact:**
- Source of truth changed: this task adds palette mode and the Terrain tool choice to each `MapDocumentViewModel`; selected terrain ID, `TerrainEditMode`, and runtime availability were established in Task 1.
- Important readers: main-window palette host/tool toggles/mode controls/status, `TerrainPaletteControl`, and Task 3 canvas gesture routing.
- Derived/cached state affected: selected enabled definition, representative preview, palette extent/offset, and tool checked state. No persisted settings are added.
- Required propagation sequence: Task 1 catalog assignment reconciles selection/availability; user terrain hit → set selection → set Terrain palette/tool → notify window/canvas; document activation → host that document's selected palette and controls.
- Invariants to preserve: state does not leak between tabs; selection never names pending/disabled/missing sets; changing palette/mode/selection never edits the map/history; manual tile palette behavior remains unchanged.
- Observable proof required: compare two real view models and encoded documents/history while selecting/switching UI.

**Step 1: Write failing tests**

Add:

- `SetTerrainCatalog_UnavailableFallsBackFromTerrainTool`
- `TerrainPalette_RendersEnabledSetsWithRepresentativeTopologyAndWarning`
- `TerrainPalette_ClickSelectsTerrainAndActivatesToolWithoutEditingMap`
- `TilesTerrainTabs_SwitchHostedPalettePerDocument`
- `TerrainPaintEraseButtons_UpdateOnlyActiveDocument`
- `UnmodifiedT_SelectsTerrainPaletteAndToolWhenAvailable`
- adversarial `UnmodifiedT_WhenUnavailablePreservesManualToolAndShowsDiagnostic`
- `UnmodifiedT_ReachesFocusedTextBoxWithoutChangingTool`.

The warning indicator is present when an enabled set carries diagnostics, even though runtime validation passed. Pending and disabled sets are absent from this palette.

**Step 2: Verify red**

```bash
dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj \
  --filter 'FullyQualifiedName~TerrainPalette|FullyQualifiedName~TerrainModeAndSelection|FullyQualifiedName~UnmodifiedT|FullyQualifiedName~TilesTerrainTabs' -v minimal
```

Expected: compile/test failure because terrain document/palette/window state does not exist.

**Step 3: Implement document state and UI**

Append `Terrain` to `MapEditTool`; change Core's normal-stroke validator to an explicit allowed set so `BeginStroke(MapEditTool.Terrain, ...)` throws. Add Tiles/Terrain toggle tabs above palette-specific controls, Paint/Erase radio toggles visible in Terrain mode, a Terrain toolbar toggle, and a terrain diagnostic text block. Keep raw sheet/graphic controls visible only in Tiles mode.

`TerrainPaletteControl` mirrors `SpritePaletteControl` scrolling and viewport-bound drawing. It resolves only each enabled set's representative reference through the current shared `AssetContext`; it never constructs a resolver or loads a catalog. Define an exact internal item/hit-test helper returning the runtime set by index so tests assert identity, not text coordinates alone.

Extend `DocumentView` to contain `MapCanvas`, `SpritePaletteControl`, and `TerrainPaletteControl`; bind the one shared scrollbar only to the active palette and unbind the outgoing control. Property handlers must explicitly switch `PaletteBorder.Child`, tab checked state, mode controls, diagnostic text, and tool buttons.

**Step 4: Verify green and regressions**

```bash
dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj \
  --filter 'FullyQualifiedName~TerrainPalette|FullyQualifiedName~MapDocumentViewModelTests|FullyQualifiedName~ShortcutTests|FullyQualifiedName~MainWindowDefaultLayoutTests|FullyQualifiedName~SpritePaletteControlTests' -v minimal
dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj \
  --filter 'FullyQualifiedName~MapEditStrokeTests' -v minimal
```

**Step 5: Commit**

```bash
git add src/MapEditor.Core/Editing/MapEditTool.cs \
  src/MapEditor.Core/Editing/MapEditSession.cs \
  src/MapEditor.App/ViewModels/AssetPaletteMode.cs \
  src/MapEditor.App/ViewModels/MapDocumentViewModel.cs \
  src/MapEditor.App/Controls/TerrainPaletteControl.cs \
  src/MapEditor.App/Views/MainWindow.axaml \
  src/MapEditor.App/Views/MainWindow.axaml.cs \
  src/MapEditor.App/Styles/Icons.axaml \
  tests/MapEditor.App.Tests/TerrainPaletteControlTests.cs \
  tests/MapEditor.App.Tests/MapDocumentViewModelTests.cs \
  tests/MapEditor.App.Tests/MainWindowDefaultLayoutTests.cs \
  tests/MapEditor.App.Tests/ShortcutTests.cs
git commit -m "feat: add terrain palette and tool state"
```

### Task 3: Wire real canvas terrain gestures to Part 2

**Files:**
- Modify: `src/MapEditor.App/ViewModels/MapDocumentViewModel.cs:376-386`
- Modify: `src/MapEditor.App/Controls/MapCanvas.cs:43-81,107-142,174-293,315-456`
- Modify: `tests/MapEditor.App.Tests/MapCanvasTests.cs`

**Mutation impact:**
- Source of truth changed: Part 2's active terrain stroke owns preview intent and map deltas; canvas owns only pointer/capture flags.
- Important readers: map renderer, dirty/title/commands, history/timeline, selected coordinate, terrain status, and asset replacement cancellation.
- Derived/cached state affected: canvas `_stroking` gains terrain/manual distinction; no second stroke buffer is introduced.
- Required propagation sequence: terrain press validates current runtime/selection → `BeginTerrainStroke` → preview map values → canvas/title/command refresh; move → `ContinueTerrainStroke`; release → shared `CompleteStroke`; Escape/capture loss/replacement → `CancelStroke`; failure → Part 2 has already restored/cleared, canvas releases capture and reports terrain/mask.
- Lifecycle boundary: capture is acquired only after successful begin. `CancelTerrainInteraction` acts only when the active canvas gesture is terrain, cancels the session first, then clears flags/releases capture. Subscribe each canvas to `AssetContextController.TerrainCatalogReplacing` on construction and unsubscribe on visual/lifetime teardown.
- Invariants to preserve: resolver/terrain/mode/layer are captured at begin by Part 2; one gesture is one command; capture loss cancels terrain but still commits existing manual strokes; failed begin/continue creates no dangling capture/history.
- Observable proof required: real headless pointer input must assert preview bytes, final bytes, history, capture-loss, and replacement behavior.

**Step 1: Write failing headless tests**

Extend the harness to open a synthetic valid terrain catalog and select Terrain. Add:

- `TerrainPaint_PressPreviewsNeighborRepairAndReleaseCreatesOneUndoEntry`
- `TerrainErase_SparseDragUsesPartTwoInterpolationAndOneCommand`
- `TerrainEscape_RestoresExactBytesAndHistoryVersion`
- `TerrainCaptureLoss_CancelsWhileManualCaptureLossStillCommits`
- `TerrainContinuationFailure_ReleasesGestureAndShowsTerrainAndMask`
- `TerrainGesture_CapturesLayerSelectionModeAndTerrainAtPress`
- adversarial `CatalogReplacing_DuringTerrainPreviewCancelsBeforeNewResolverIsVisible`.

For the ordering test, begin a preview, trigger a real controller context replacement, and in the replacement observer assert old `Current`, restored encoded bytes, no active session stroke, and unchanged history before the new context is visible.

**Step 2: Verify red**

```bash
dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj \
  --filter 'FullyQualifiedName~TerrainPaint|FullyQualifiedName~TerrainErase|FullyQualifiedName~TerrainEscape|FullyQualifiedName~TerrainCapture|FullyQualifiedName~CatalogReplacing' -v minimal
```

Expected: FAIL because canvas still routes Terrain through normal `BeginStroke` and commits on capture loss.

**Step 3: Implement terrain-specific routing**

Add view-model wrappers that obtain the current immutable resolver supplied by `SetTerrainCatalog` and call the exact Part 2 APIs. Keep result handling centralized: on `Succeeded == false`, set `TerrainStatus` to include terrain ID and missing mask, clear canvas gesture state, and do not call complete/cancel again. On successful updates, clear stale failure status and run the existing canvas/commands/title refresh.

Use a small internal gesture enum (`None`, `ManualStroke`, `TerrainStroke`) rather than booleans that cannot distinguish capture-loss policy. Do not re-read mode/terrain during continuation. Keep pan, rectangle, marker, paste, and manual paths unchanged.

**Step 4: Verify green and full canvas regressions**

```bash
dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj \
  --filter 'FullyQualifiedName~MapCanvasTests' -v minimal
```

**Step 5: Commit**

```bash
git add src/MapEditor.App/ViewModels/MapDocumentViewModel.cs \
  src/MapEditor.App/Controls/MapCanvas.cs \
  tests/MapEditor.App.Tests/MapCanvasTests.cs
git commit -m "feat: connect terrain canvas gestures"
```

### Task 4: Build manager drafts and validation without publishing

**Files:**
- Create: `src/MapEditor.App/Terrain/TerrainCatalogDraft.cs`
- Create: `src/MapEditor.App/Terrain/TerrainSetDraft.cs`
- Create: `src/MapEditor.App/Terrain/TerrainMaskDraft.cs`
- Create: `src/MapEditor.App/ViewModels/TerrainSetsManagerViewModel.cs`
- Create: `tests/MapEditor.App.Tests/TerrainCatalogDraftTests.cs`
- Create: `tests/MapEditor.App.Tests/TerrainSetsManagerViewModelTests.cs`

**Mutation impact:**
- Source of truth changed: only manager-owned draft objects cloned from `TerrainAssetLoadResult.Source`; published `AssetContext` and disk remain unchanged.
- Important readers: manager mask grid, provenance/diagnostic panels, enable/status controls, validation summary, and Task 5 save service.
- Derived/cached state affected: required mask rows, representative previews, issue lists, `CanEnable`, and `CanSave`; recompute after each draft mutation from a newly built immutable catalog plus current manifest.
- Required propagation sequence: clone source → edit one field/list/status → rebuild affected required-mask rows deterministically → build immutable candidate catalog → run Core and frame validation → publish draft issues/property notifications only.
- Invariants to preserve: source settings, fingerprint, generator version, metrics, provenance, and diagnostics round-trip unless explicitly edited; topology changes expose exactly 16/47 required masks; variant order is significant; rejected enable leaves status unchanged; pending/disabled may retain missing masks/conflicts/unresolved frames.
- Observable proof required: compare complete built catalog values and prove current runtime/file bytes are unchanged after edits and rejected enable.

**Step 1: Write failing tests**

Add:

- `CloneBuild_RoundTripsGeneratedMetadataProvenanceAndDiagnostics`
- `Rename_DoesNotChangeIdOrVariantOrder`
- `ChangeTopology_RebuildsExactly16Or47RequiredMaskRows`
- `AddRemoveReorderVariant_PreservesExactUserOrderAndSynchronizesMemberProvenance`
- `MaskRows_ExposeVisualNeighborhoodAndResolvedFrameState`
- `TrySetEnabled_CompleteUniqueResolvedSetSucceeds`
- `TrySetEnabled_MissingMaskFrameOrConflictReturnsExactIssuesAndKeepsStatus`
- `SetPendingOrDisabled_AllowsIncompleteConflictingReviewData`
- adversarial `DraftMutations_DoNotChangePublishedResolverFileOrMapHistory`.

A topology change retains variants for masks reachable under the new topology, creates empty rows for newly required masks, and retains now-unreachable mappings in a draft-only orphan list shown as validation errors until removed; it must never silently discard user edits.

**Step 2: Verify red**

```bash
dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj \
  --filter 'FullyQualifiedName~TerrainCatalogDraft|FullyQualifiedName~TerrainSetsManagerViewModel' -v minimal
```

Expected: compile failure because draft/manager types do not exist.

**Step 3: Implement isolated draft editing**

Expose explicit operations rather than mutable lists:

```csharp
void Rename(string terrainId, string displayName);
void ChangeTopology(string terrainId, TerrainTopology topology);
void AddVariant(string terrainId, int mask, TerrainGraphicReference reference);
void RemoveVariant(string terrainId, int mask, int index);
void MoveVariant(string terrainId, int mask, int fromIndex, int toIndex);
bool TrySetStatus(string terrainId, TerrainReviewStatus status, out IReadOnlyList<TerrainValidationIssue> issues);
TerrainCatalog Build();
```

Each operation validates IDs/indices before mutation. Adding a reference absent from `Members` also adds it with `ImageOnly` provenance; a manager-added graphic has no map-observed evidence. Removing the final use of an image-only reference removes that member entry, while an unused map-observed member remains visible with a diagnostic so generated provenance is not silently discarded. Reordering never changes membership. `TrySetStatus(...Enabled...)` validates a temporary catalog with the requested status through `TerrainAssetCatalog.Validate`; it commits status only when runtime-valid. `Build` returns fresh read-only Part 1 records and has no file/context side effects. Keep all work synchronous on the Avalonia UI thread; catalog sizes are bounded editor metadata and no background worker is needed.

**Step 4: Verify green**

```bash
dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj \
  --filter 'FullyQualifiedName~TerrainCatalogDraft|FullyQualifiedName~TerrainSetsManagerViewModel' -v minimal
```

**Step 5: Commit**

```bash
git add src/MapEditor.App/Terrain/TerrainCatalogDraft.cs \
  src/MapEditor.App/Terrain/TerrainSetDraft.cs \
  src/MapEditor.App/Terrain/TerrainMaskDraft.cs \
  src/MapEditor.App/ViewModels/TerrainSetsManagerViewModel.cs \
  tests/MapEditor.App.Tests/TerrainCatalogDraftTests.cs \
  tests/MapEditor.App.Tests/TerrainSetsManagerViewModelTests.cs
git commit -m "feat: add terrain set manager drafts"
```

### Task 5: Atomically save, publish, and expose the Terrain Sets manager

**Files:**
- Create: `src/MapEditor.App/Terrain/ITerrainCatalogFileOperations.cs`
- Create: `src/MapEditor.App/Terrain/TerrainCatalogFileStore.cs`
- Create: `src/MapEditor.App/Terrain/TerrainCatalogManager.cs`
- Create: `src/MapEditor.App/Dialogs/TerrainSetsDialog.axaml`
- Create: `src/MapEditor.App/Dialogs/TerrainSetsDialog.axaml.cs`
- Modify: `src/MapEditor.App/Dialogs/IEditorDialogs.cs:29-58`
- Modify: `src/MapEditor.App/Dialogs/AvaloniaEditorDialogs.cs:19-115`
- Modify: `src/MapEditor.App/Dialogs/EditorDialogsProxy.cs`
- Modify: `tests/MapEditor.App.Tests/Fakes/FakeEditorDialogs.cs:61-224`
- Modify: `src/MapEditor.App/Rendering/AssetContextController.cs:36-80`
- Modify: `src/MapEditor.App/Views/MainWindow.axaml`
- Modify: `src/MapEditor.App/Views/MainWindow.axaml.cs`
- Create: `tests/MapEditor.App.Tests/TerrainCatalogFileStoreTests.cs`
- Create: `tests/MapEditor.App.Tests/TerrainCatalogManagerTests.cs`
- Create: `tests/MapEditor.App.Tests/TerrainSetsDialogTests.cs`

**Mutation impact:**
- Source of truth changed: `<current asset directory>/terrain-brushes.json`, followed by `AssetContext.Current.Terrain` as the in-process publication.
- Important readers: all terrain palettes/tools/gestures, manager reopening, current manifest/cache, and open documents. Map contents/history are not manager readers and must remain unchanged.
- Derived/cached state affected: current runtime resolver/enabled ordering and every document's reconciled selection; sprite cache, renderer, tint cache, sheets, and appearance catalog must retain identity.
- Required propagation sequence:
  1. Build immutable catalog from draft.
  2. Run shared/core plus enabled frame validation against `Current.Cache.Manifest` and build the complete runtime result.
  3. Canonically serialize all bytes in memory.
  4. Invoke synchronous terrain-gesture cancellation while the old runtime and file remain current; any cancellation failure aborts before I/O.
  5. Create a unique sibling temp, write, flush durably, close, and atomically move/replace destination.
  6. Publish the already validated `TerrainAssetLoadResult` into the existing `AssetContext` terrain slot through a nonthrowing controller path.
  7. Reconcile every document and contain observer-notification errors without rolling runtime back; disk and runtime must remain the same catalog once replacement succeeds.
  8. Return success and close/mark-clean the manager draft.
- Publication boundary: steps 1-4 cannot change the file or published catalog; step 4 denies stale gesture continuation before an irreversible write. Step 6 is the only runtime publication point and performs no I/O or validation. No throwable callback is allowed between durable replacement and publication.
- Invariants to preserve: invalid drafts and cancellation failures do not write; write/flush/replace failure preserves old bytes and runtime, though an in-progress terrain preview has been safely cancelled; after destination replacement, runtime publication cannot fail or remain on the old catalog; successful save does not replace/dispose sprite assets; manager warns that converter regeneration overwrites edits.
- Observable proof required: injected failures compare exact destination bytes, context/runtime identity, sprite-cache identity, map bytes, history versions, and active gesture state.

**Step 1: Write failing store/manager tests**

Define production/fake interface methods exactly:

```csharp
bool Exists(string path);
Stream CreateFile(string path);
void Replace(string sourcePath, string destinationPath);
void Move(string sourcePath, string destinationPath);
void Delete(string path);
```

Add:

- `Save_InvalidDraftChangesNeitherFileNorRuntime`
- `Write_FlushReplaceOrMoveFailurePreservesPriorBytesAndDeletesTemp`
- `Save_SuccessCancelsGestureBeforeWritingCanonicalBytesAndThenPublishes`
- `Save_SuccessPublishesIntoExistingContextWithoutReplacingSpriteCache`
- `Save_ReconcilesEveryDocumentSelectionAndInvalidatesPalettes`
- `Save_CancelsActiveTerrainGestureButDoesNotAlterMapHistory`
- adversarial `Save_CancellationCallbackFailureOccursBeforeWriteAndPreservesFileAndRuntime`;
- adversarial `Save_ObserverFailureAfterReplaceStillLeavesDiskAndPublishedRuntimeEqual`.

The fake implements only the declared file-operation methods. A failing stream is used for write/flush; no phantom async API is introduced.

**Step 2: Verify red**

```bash
dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj \
  --filter 'FullyQualifiedName~TerrainCatalogFileStore|FullyQualifiedName~TerrainCatalogManager' -v minimal
```

Expected: compile failure because store/manager publication APIs do not exist.

**Step 3: Implement atomic store and terrain-only publication**

`TerrainCatalogFileStore` uses a sibling `terrain-brushes.json.tmp-<guid>` and never deletes the destination first. Flush the `FileStream` durably before close; cleanup is best effort and must not mask the original failure.

Add `AssetContextController.PrepareTerrainReplacement()` to synchronously invoke terrain-gesture cancellation while failures are still reversible, and `PublishTerrain(TerrainAssetLoadResult validated)` restricted to a result already validated against `Current.Cache.Manifest`. `PublishTerrain` must not invoke callbacks, perform I/O, or validate: it updates only terrain state, then reconciles documents while containing observer exceptions so the published catalog cannot diverge from the durably replaced file. Because `AssetContext` currently has immutable construction, introduce a private terrain field/property setter owned by the controller rather than reconstructing the context; do not transfer/dispose cache ownership.

**Step 4: Write failing manager UI tests**

Add a `ShowTerrainSetsAsync(TerrainSetsManagerViewModel manager)` method to the real interface/proxy/fake. Add headless tests:

- `Dialog_ShowsAllStatusesDiagnosticsProvenanceAndRegenerationWarning`
- `Dialog_SelectingTopologyShowsEveryRequiredVisualMask`
- `Dialog_AddRemoveAndMoveVariantUpdatesPreviewAndValidation`
- `Dialog_InvalidEnableShowsExactFailuresAndStaysOpen`
- `Dialog_SaveFailureShowsErrorAndPreservesDraftForRetry`
- `MainWindow_TerrainSetsCommandUsesCurrentSourceAndDisablesWhenUnparseable`.

**Step 5: Implement the focused dialog and command**

Add `Terrain Sets…` under Edit. The dialog uses a set list and selected-set editor: name, status, topology, diagnostics/provenance, required mask neighborhood, ordered variant list with add/remove/up/down, validation summary, Save/Cancel, and the explicit regeneration warning. Reuse existing sprite resolution for previews. Do not add inference, drag/drop, bulk auto-fix, or settings persistence.

`AvaloniaEditorDialogs.ShowTerrainSetsAsync` opens `TerrainSetsDialog`; the fake records the exact manager instance. The dialog calls the manager service only on Save. Invalid enable/save keeps the dialog open and focuses the validation summary.

**Step 6: Verify green and commit**

```bash
dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj \
  --filter 'FullyQualifiedName~TerrainCatalogFileStore|FullyQualifiedName~TerrainCatalogManager|FullyQualifiedName~TerrainSetsDialog' -v minimal
```

```bash
git add src/MapEditor.App/Terrain \
  src/MapEditor.App/Dialogs/TerrainSetsDialog.axaml \
  src/MapEditor.App/Dialogs/TerrainSetsDialog.axaml.cs \
  src/MapEditor.App/Dialogs/IEditorDialogs.cs \
  src/MapEditor.App/Dialogs/AvaloniaEditorDialogs.cs \
  src/MapEditor.App/Dialogs/EditorDialogsProxy.cs \
  src/MapEditor.App/Rendering/AssetContext.cs \
  src/MapEditor.App/Rendering/AssetContextController.cs \
  src/MapEditor.App/Views/MainWindow.axaml \
  src/MapEditor.App/Views/MainWindow.axaml.cs \
  tests/MapEditor.App.Tests/Fakes/FakeEditorDialogs.cs \
  tests/MapEditor.App.Tests/TerrainCatalogFileStoreTests.cs \
  tests/MapEditor.App.Tests/TerrainCatalogManagerTests.cs \
  tests/MapEditor.App.Tests/TerrainSetsDialogTests.cs
git commit -m "feat: manage and publish terrain sets"
```

### Task 6: End-to-end editor proof, full gates, and real-corpus smoke

**Files:**
- Create: `tests/MapEditor.App.Tests/TerrainEditorEndToEndTests.cs`
- Modify only when a test exposes a defect: files from Tasks 0-5
- Modify: `docs/plans/2026-09-08-terrain-brush-part3-editor.md` only to record any verified environment limitation; do not record generated counts as contractual snapshots

**Mutation impact:**
- Source of truth changed: none intentionally; this task proves the full path from generated-format JSON through UI, map history, manager durability, and republished runtime.
- Important readers: Rendering loader, asset context, documents, both palettes, canvas, manager, map codec/history, and reopened editor context.
- Derived/cached state affected: all runtime state from prior tasks is exercised; no new cache is planned.
- Required propagation sequence under test: fixture catalog file → load/validate → select Terrain → pointer edit/undo/redo → manager draft/build → gesture cancellation → durable save → nonthrowing publication → document reconciliation → reload from disk.
- Invariants to preserve: maps contain no terrain metadata; ordinary Tiles/Pencil/Eraser still work after terrain failure and manager save; no generated `Assets/` output is committed.
- Observable proof required: one real headless window test asserts final file bytes, runtime IDs, map layers, history transitions, and reopened context behavior.

**Step 1: Write the failing end-to-end tests**

Add:

- `LoadSelectPaintUndoManageSaveReload_EndToEnd`
- `MalformedTerrain_ManualTileEditingAndMapSaveStillWork_EndToEnd`
- adversarial `SaveReplacementDuringPreview_CancelsThenNextGestureUsesOnlyNewCatalog`.

The first test must use a real temp asset directory, real manifest/PNG, canonical Part 1 JSON, real `MainWindow`, real pointer input, real file store, and `MapCodec`; mock only user dialog choices. After manager save, parse disk bytes with `TerrainCatalogJson.Parse`, reopen a fresh context, and paint with the changed variants.

**Step 2: Verify red, then fix only integration defects**

```bash
dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj \
  --filter 'FullyQualifiedName~TerrainEditorEndToEndTests' -v minimal
```

Expected before final integration fixes: fail on the first omitted propagation/lifecycle edge. Make the smallest production correction, rerun until PASS, and do not broaden scope.

**Step 3: Run all automated gates**

```bash
dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj -v minimal
dotnet test tests/MapEditor.Rendering.Tests/MapEditor.Rendering.Tests.csproj -v minimal
dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj -v minimal
dotnet build Goose2ClientGodot.sln -v minimal
git status --short
```

Expected: all tests/build pass; status contains only intended source/tests/this plan and no generated `Assets/` files.

**Step 4: Run real-corpus smoke without committing output**

On a machine where Part 1's configured source corpora are available:

```bash
dotnet run --project tools/AssetConverter/src/AssetConverter -- all "$PWD"
dotnet run --project src/MapEditor.App/MapEditor.App.csproj
```

Manual checklist: load `Assets/Sprites`; inspect all manager statuses/diagnostics; paint and erase grass, water, path, and shoreline on layer 0 and a higher layer; verify drag previews, one-step undo/redo, Escape/capture-loss rollback, save-during-preview cancellation, restart persistence, and subsequent Pencil/Eraser map save/reopen. If the full corpus is unavailable, use Part 1's temporary synthetic/Illutia-only root. Never weaken validation merely to produce enabled sets.

**Step 5: Red-team review**

- Confirm UI-thread-only mutation/publication; no worker or file watcher.
- Confirm both open and manager save cancel terrain before resolver replacement, and old caches are disposed only afterward.
- Confirm validation/serialization and durable temp flush precede atomic replacement; destination is never deleted first.
- Exercise malformed/unsupported JSON, bad frames/membership, invalid enable, I/O failures, callback failure, Escape, capture loss, and mid-preview replacement.
- Search for per-document terrain state outside `MapDocumentViewModel`, cached stale resolvers, draft leakage, and manager changes to map/history/cache identity.
- Confirm fakes mirror production interfaces, masks come from `TerrainMasks.Required`, and end-to-end tests use real files/session/pointer capture.
- Reject inference, terrain rendering, transitions, fallback masks, map metadata, override files, and generated asset commits.

**Step 6: Commit**

```bash
git add tests/MapEditor.App.Tests/TerrainEditorEndToEndTests.cs \
  docs/plans/2026-09-08-terrain-brush-part3-editor.md
git commit -m "test: verify terrain editor workflow"
```

Add production files to this commit only if the end-to-end test required a small integration correction not already committed.

Plan complete and saved to `docs/plans/2026-09-08-terrain-brush-part3-editor.md`. Ready to implement.
