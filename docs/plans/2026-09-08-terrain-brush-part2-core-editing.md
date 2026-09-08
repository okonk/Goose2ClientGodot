# Terrain Brush Part 2: Core Editing Implementation Plan

**Goal:** Add deterministic, atomic terrain paint/erase strokes to `MapEditor.Core`, including neighbor repair, preview, cancellation, and normal undo/redo.

**Architecture:** Build an immutable runtime index over Part 1’s locked `TerrainCatalog`. Resolve each cumulative stroke against intended terrain membership before applying a preview patch, then coalesce all preview mutations into one existing `MapLayerChangesCommand`. This part has no asset loading, rendering, application state, UI, or generated assets.

**Tech Stack:** C#; .NET 8 `MapEditor.Core`; xUnit; `System.Security.Cryptography`.

---

> Implement with @executing-plans, one task and commit at a time. Follow `AGENTS.md`: add no comments/doc strings unless a non-obvious invariant requires one.

## Scope and prerequisite

Part 1 is required. Consume its schema unchanged: `TerrainCatalog`, `TerrainSetDefinition`, `TerrainMaskDefinition`, `TerrainGraphicReference`, `TerrainTopology`, `TerrainReviewStatus`, and `TerrainMasks` in `MapEditor.Core.Terrain` (`docs/plans/2026-09-08-terrain-brush-part1-converter.md:68-176`).

Included: enabled-terrain lookup, deterministic variants, topmost-layer paint/erase, selected/displaced neighbor repair, interpolated previews, missing-mask atomicity, cancellation, history, dirty state, and undo/redo.

Excluded: JSON/manifest/frame loading, Rendering/App/UI, pointer handling, manager workflows, catalog schema changes, map-format changes, transitions between terrain pairs, closest-mask fallback, and terrain flood fill. No persistence migration is needed because edits remain ordinary `MapTileLayer` values.

## APIs and facts verified

| Fact/API | Citation and consequence |
|---|---|
| Core owns definitions, mask resolution, deterministic selection, and atomic terrain edits | `docs/plans/2026-09-08-terrain-brush-design.md:38-46`. Keep all production work in Core. |
| Paint adds selected membership; erase clears only selected-terrain members; drag previews and commits once; cancellation restores exact values | `docs/plans/2026-09-08-terrain-brush-design.md:84-92`. Reuse the existing stroke/history lifecycle. |
| Membership uses `(sheet, graphic)` on the topmost selected layer; affected cells include visited and selected/displaced neighbors | `docs/plans/2026-09-08-terrain-brush-design.md:94-103`. Capture `TopLayer` at begin. |
| Variant choice hashes terrain ID, coordinates, and normalized mask; missing resolution changes no map/history state | `docs/plans/2026-09-08-terrain-brush-design.md:104-106`. Stage the whole patch before mutation. |
| Required Core coverage includes masks, stable variants, active layer, overwrite, displaced repair, interpolation, no-ops, atomic failure, cancellation, undo/redo | `docs/plans/2026-09-08-terrain-brush-design.md:138-146`. |
| Part 1 locks set status/topology/members/masks and `TerrainMasks.Normalize/Required` | `docs/plans/2026-09-08-terrain-brush-part1-converter.md:104-150`. Do not duplicate normalization. |
| `TopLayer` returns the highest selected layer | `src/MapEditor.Core/Editing/MapEditSession.cs:42-70`. |
| Existing begin/continue validates lifecycle and coordinates, captures layer state, applies first sample, and interpolates continuations | `src/MapEditor.Core/Editing/MapEditSession.cs:104-135`; `src/MapEditor.Core/Editing/MapEditStroke.cs:44-71`. |
| Active deltas drive dirty state and disable undo/redo | `src/MapEditor.Core/Editing/MapEditSession.cs:78-101`. Terrain deltas must use the same contract. |
| Completion pushes one layer buffer; cancellation reverse-restores active changes | `src/MapEditor.Core/Editing/MapEditSession.cs:137-149,381-388`. |
| Layer commands replay forward/reverse, and accounting uses segmented allocated capacity | `src/MapEditor.Core/Editing/MapEditCommand.cs:26-69`; `src/MapEditor.Core/Editing/MapEditChangeBuffer.cs:6-72`. Emit one final delta per coordinate. |
| `PushUndo` clears redo, accounts/evicts, and raises one history change | `src/MapEditor.Core/Editing/MapEditHistory.cs:35-47`. Do not add terrain history. |
| Existing tests pin interpolation, cancellation, no-op redo preservation, maximum-stroke storage, and history accounting | `tests/MapEditor.Core.Tests/MapEditStrokeTests.cs:10-197`; `tests/MapEditor.Core.Tests/MapEditStrokeStorageTests.cs:84-150`; `tests/MapEditor.Core.Tests/MapEditHistoryTests.cs:54-109`. Preserve manual behavior. |

## Locked Part 2 API

Add in `MapEditor.Core.Terrain`:

```csharp
public enum TerrainEditMode { Paint, Erase }

public sealed record TerrainEditFailure(string TerrainId, int Mask);

public readonly record struct TerrainStrokeUpdate(
    bool Succeeded,
    bool Changed,
    TerrainEditFailure? Failure);

public sealed class TerrainMapResolver
{
    public TerrainMapResolver(TerrainCatalog catalog);
}
```

Add to `MapEditSession`:

```csharp
public TerrainStrokeUpdate BeginTerrainStroke(
    TerrainMapResolver resolver,
    string terrainId,
    TerrainEditMode mode,
    int x,
    int y);

public TerrainStrokeUpdate ContinueTerrainStroke(int x, int y);
```

`CompleteStroke()` and `CancelStroke()` remain shared. Successful begin creates an active stroke even when the first sample is a no-op. `Changed` describes concrete preview changes made by that call. A missing/empty mask returns `Succeeded == false`, identifies the terrain/mask, restores all earlier preview changes in that gesture, clears the stroke, and changes no history/state identity. Invalid arguments or lifecycle usage throw before mutation.

`TerrainMapResolver` indexes only enabled sets. Unknown/pending/disabled selection is rejected before stroke publication. Duplicate enabled membership is rejected by its constructor. It is immutable, reusable, and owns no document/history/disposal lifecycle.

Lock variant selection: hash UTF-8 `terrainId + "\n" + x + "\n" + y + "\n" + normalizedMask` with SHA-256; read the first eight digest bytes as unsigned big-endian; choose `value % variants.Count`. Variant order is significant. Do not use `GetHashCode`.

All mutation remains synchronous on the existing caller context; no worker, registry, event stream, or thread marshal is added.

## Task 0: Immutable lookup and deterministic variant selection

**Files:**
- Create: `src/MapEditor.Core/Terrain/TerrainMapResolver.cs`
- Create: `src/MapEditor.Core/Terrain/TerrainEditResult.cs`
- Create: `tests/MapEditor.Core.Tests/Terrain/TerrainMapResolverTests.cs`

**Mutation impact:**
- Source of truth: unchanged immutable `TerrainCatalog`.
- Readers: terrain resolution in Tasks 1–2.
- Derived state: enabled ID, member-owner, and mask-variant indexes; no global cache.
- Propagation: inspect all enabled sets → reject duplicate enabled IDs/membership → defensively copy indexes → publish only the complete resolver.
- Invariants: review-only sets never own runtime membership; variant order is preserved; metadata/catalog order cannot affect selection.
- Proof: assert returned references/failures, not private collections.

**Step 1: Write failing tests**

Add:

- `Constructor_IndexesOnlyEnabledSets`
- `Constructor_DuplicateEnabledMembership_Throws`
- `GetVariant_UsesTerrainMasksNormalizationForFourAndEightWay`
- `GetVariant_IsStableAcrossResolverInstances`
- `GetVariant_DisplayNameAndCatalogOrderDoNotChangeSelection`
- adversarial `GetVariant_DoesNotUseProcessRandomizedHashing`
- `GetVariant_MissingMaskOrEmptyVariants_ReturnsExactFailure`.

Build fixtures from the locked Part 1 records. Iterate `TerrainMasks.Required(topology)` rather than copying masks.

**Step 2: Verify red**

```bash
dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj \
  --filter 'FullyQualifiedName~TerrainMapResolverTests' -v minimal
```

Expected: compile failure because the runtime resolver/result types do not exist.

**Step 3: Implement minimal lookup**

Keep lookup/resolution methods internal. Convert `TerrainGraphicReference` to/from `MapTileLayer` only at the editing boundary, preserving `(Sheet, Graphic)`. Normalize via Part 1’s `TerrainMasks.Normalize`. Implement the exact SHA-256 contract above with explicit big-endian reading.

Do not run full catalog validation per stroke. Constructor enforces runtime indexing preconditions only; frame existence belongs to later asset loading.

**Step 4: Verify green**

Run the same command. Expected: PASS.

| Invariant | Proved by |
|---|---|
| Only enabled sets participate | `Constructor_IndexesOnlyEnabledSets` |
| Membership is unambiguous | `Constructor_DuplicateEnabledMembership_Throws` |
| Shared normalization is used | `GetVariant_UsesTerrainMasksNormalizationForFourAndEightWay` |
| Selection is deterministic and metadata-independent | stability/order/hash tests |
| Missing mappings are actionable | `GetVariant_MissingMaskOrEmptyVariants_ReturnsExactFailure` |

**Step 5: Commit**

```bash
git add src/MapEditor.Core/Terrain/TerrainMapResolver.cs \
  src/MapEditor.Core/Terrain/TerrainEditResult.cs \
  tests/MapEditor.Core.Tests/Terrain/TerrainMapResolverTests.cs
git commit -m "feat: add terrain map resolver"
```

## Task 1: Stage paint/erase and neighbor repair atomically

**Files:**
- Create: `src/MapEditor.Core/Terrain/TerrainStrokeResolver.cs`
- Create: `tests/MapEditor.Core.Tests/Terrain/TerrainStrokeResolverTests.cs`

**Mutation impact:**
- Source of truth: current captured layer plus cumulative visited membership intent.
- Readers: Task 2 applies the resulting patch.
- Derived state: intended membership overlay, displaced-owner records, affected set, masks, and staged targets; discarded after each call.
- Propagation:
  1. Overlay all visited paint/erase intentions without mutating the document.
  2. Include visited cells, neighboring selected members, and neighbors of terrain displaced by paint.
  3. Determine each affected cell’s conceptual terrain owner.
  4. Compute topology-specific bits, normalize, and select variants.
  5. Return a complete row-major patch, or the first deterministic failure and no patch.
- Invariants: erase of a non-selected member is a no-op; terrain types see each other only as nonmembers; boundaries are nonmembers; failure cannot partially mutate.
- Proof: apply successful patches in tests and compare final grids; compare encoded document bytes on failure.

**Step 1: Write failing tests**

Add:

- `ResolvePaint_ChoosesFinalMasksForCellAndSelectedNeighbors`
- `ResolveErase_WritesEmptyAndRepairsSelectedNeighbors`
- `ResolveErase_NonSelectedTerrainIsNoOp`
- `ResolvePaint_UsesOnlyCapturedLayerAndPreservesFlags`
- `ResolvePaint_OverOtherTerrainRepairsDisplacedNeighbors`
- `Resolve_DifferentTerrainsAreOnlyMemberOrNonmember`
- `Resolve_EightWayUsesCanonicalDiagonalNormalization`
- `Resolve_VisitedOrderDoesNotChangePatch`
- adversarial `Resolve_MissingSelectedOrDisplacedMaskReturnsNoPatch`
- `Resolve_MapEdgesTreatOutsideAsNonmember`.

The displaced test paints terrain A over the center of a terrain-B run and verifies both surviving B neighbors. The missing-mask test must encounter valid affected cells before the invalid one.

**Step 2: Verify red**

```bash
dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj \
  --filter 'FullyQualifiedName~TerrainStrokeResolverTests' -v minimal
```

Expected: compile failure because `TerrainStrokeResolver` does not exist.

**Step 3: Implement staged resolution**

Use row-major indices for deterministic ordering. Four-way inspects N/E/S/W; eight-way inspects all eight and calls `TerrainMasks.Normalize`. Painted visited cells conceptually belong to the selected terrain before receiving a concrete variant. Erased cells become nonmembers only when they belong to the selected terrain. Retain the original enabled owner of painted-over cells so displaced neighbors remain repairable across cumulative previews.

Helper contract: the resolver reads document/layer, immutable lookup, selected ID/mode, cumulative visited intent, and displaced owners. It returns a complete immutable patch or one failure. It never calls `SetLayer`, changes history, updates visited intent, or retains per-call collections.

**Step 4: Verify green**

Run the focused command. Expected: PASS.

| Invariant | Proved by |
|---|---|
| Paint/erase resolve final selected membership | paint/erase tests |
| Erase preserves unrelated graphics | adversarial non-selected erase test |
| Overwrite repairs displaced terrain without pair transitions | displaced/different-terrain tests |
| Only captured layer is represented | active-layer/flags test |
| Resolution is order-independent and atomic | visited-order and missing-mask tests |

**Step 5: Commit**

```bash
git add src/MapEditor.Core/Terrain/TerrainStrokeResolver.cs \
  tests/MapEditor.Core.Tests/Terrain/TerrainStrokeResolverTests.cs
git commit -m "feat: resolve atomic terrain edits"
```

## Task 2: Integrate cumulative terrain previews into session strokes

**Files:**
- Create: `src/MapEditor.Core/Editing/MapLayerChangeAccumulator.cs`
- Modify: `src/MapEditor.Core/Editing/MapEditStroke.cs:5-87`
- Modify: `src/MapEditor.Core/Editing/MapEditSession.cs:78-150,381-388`
- Test: `tests/MapEditor.Core.Tests/MapEditSessionTests.cs`
- Test: `tests/MapEditor.Core.Tests/MapEditStrokeTests.cs`

**Mutation impact:**
- Source of truth: document layer values; the stroke’s first observed values are canonical for rollback/undo, while cumulative intent is canonical for preview.
- Readers: `Document`, `HasActiveStroke`, `IsDirty`, undo/redo availability, completion/cancellation, and later canvas rendering.
- Derived state: visited bitmap, displaced owners, first-before/latest-after accumulator, and preview values.
- Propagation:
  1. Validate resolver/terrain/mode/coordinate/lifecycle before publishing `_stroke`.
  2. Capture `TopLayer`; interpolate using existing `GridLine.Enumerate` semantics.
  3. Add newly visited coordinates and displaced owners to cumulative intent.
  4. Resolve the whole intent before applying that call’s patch.
  5. Record first values and apply the complete staged preview.
  6. Complete by filtering net no-ops into one `MapEditChangeBuffer<MapLayerChange>` and calling existing `PushLayerCommand`; cancel/failure restores originals and pushes nothing.
- Invariants: readers never see half a resolution patch; failed continuation rolls back earlier previews; no-op preserves redo/state ID; manual strokes remain unchanged.
- Proof: assert document bytes, preview values, active state, history version/count, dirty state, and failure values.

**Step 1: Write failing tests**

Add:

- `TerrainStroke_CapturesTopLayerResolverTerrainAndModeAtBegin`
- `TerrainStroke_InterpolatesSparseSamplesAndPreviewsRepairs`
- `TerrainStroke_OverlappingSegmentsVisitAuthoredCellsOnce`
- `TerrainStroke_CompleteCreatesOneLayerCommand`
- `TerrainStroke_CancelRestoresOriginalValuesAndKeepsHistory`
- `TerrainStroke_NoOpErasePreservesRedoAndStateIdentity`
- adversarial `TerrainStroke_FailedContinuationRollsBackEarlierPreviewAndClearsStroke`
- `TerrainStroke_InvalidBeginThrowsBeforeMutation`
- `TerrainAndManualStrokeLifecycleRejectOverlap`.

Inspect preview values before completion. In the failed-continuation test, first produce a valid preview, then reach a missing mask and assert pre-begin map bytes, unchanged history/dirty baseline, no active stroke, and exact failure.

**Step 2: Verify red**

```bash
dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj \
  --filter 'FullyQualifiedName~TerrainStroke' -v minimal
```

Expected: compile failure because session terrain APIs/accumulator do not exist.

**Step 3: Implement lifecycle and coalescing**

Keep the manual stroke path intact to preserve its storage tests. Use a distinct terrain constructor/factory rather than ambiguous boolean flags. Terrain state owns resolver context, ID, mode, bitmap, cumulative intent/displaced owners, and accumulator.

Accumulator contract:

- Record each coordinate’s first pre-stroke value and latest target.
- Apply values only after the resolver returns a complete patch.
- `Restore` writes all first values back and changes no history.
- `BuildChanges` emits row-major, one-per-coordinate deltas where first != latest; it does not mutate document/state.

On missing resolution, restore, release, clear `_stroke`, and return failure. On success, return whether concrete preview bytes changed during that call. Advance `PreviousSample` only after success. Continue to use `CompleteStroke` and `CancelStroke` for final lifecycle.

**Step 4: Run focused regressions**

```bash
dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj \
  --filter 'FullyQualifiedName~TerrainStroke|FullyQualifiedName~MapEditStrokeTests|FullyQualifiedName~MapEditSessionTests' -v minimal
```

Expected: all terrain and existing stroke/session tests pass.

| Invariant | Proved by |
|---|---|
| Interpolated calls preview repaired variants | interpolation test |
| One gesture creates one ordinary command | completion test |
| Cancel/failure restore exact pre-begin state | cancellation and adversarial failure tests |
| No-op publishes nothing | no-op erase test |
| Existing stroke behavior survives | existing focused regression suite |

**Step 5: Commit**

```bash
git add src/MapEditor.Core/Editing/MapLayerChangeAccumulator.cs \
  src/MapEditor.Core/Editing/MapEditStroke.cs \
  src/MapEditor.Core/Editing/MapEditSession.cs \
  tests/MapEditor.Core.Tests/MapEditSessionTests.cs \
  tests/MapEditor.Core.Tests/MapEditStrokeTests.cs
git commit -m "feat: add terrain edit strokes"
```

## Task 3: Prove history, dirty state, storage, and exact replay

**Files:**
- Modify: `tests/MapEditor.Core.Tests/MapEditHistoryTests.cs`
- Modify: `tests/MapEditor.Core.Tests/MapEditStrokeStorageTests.cs`
- Modify only if tests expose a generic defect: `src/MapEditor.Core/Editing/MapEditCommand.cs:26-69`
- Modify only if tests expose a generic defect: `src/MapEditor.Core/Editing/MapEditHistory.cs:35-130`
- Modify only if tests expose a generic defect: `src/MapEditor.Core/Editing/MapEditChangeBuffer.cs:6-72`

**Mutation impact:**
- Source of truth: no new state; this verifies propagation through existing layer-command history.
- Readers: stacks, `HistoryVersion`, `HistoryChanged`, retained bytes, undo/redo, dirty/savepoint IDs.
- Derived state: existing history stacks/accounting only.
- Propagation: completed coalesced buffer → existing `MapLayerChangesCommand` → `PushUndo` → reverse undo/forward redo → state ID and dirty readers.
- Invariants: repeated previews yield one delta per coordinate; cap eviction may remove undoability without reverting the map; failed/cancelled/no-op strokes consume no history.
- Proof: assert final grids and history state through complete/undo/redo/cap transitions.

**Step 1: Add tests**

- `TerrainCommand_UndoRestoresSelectedAndDisplacedOriginalValues`
- `TerrainCommand_RedoRestoresExactStableFinalVariants`
- `TerrainCommand_SavepointDirtyTransitionsMatchLayerCommands`
- `TerrainCommand_HistoryVersionAdvancesOnceOnCompleteUndoRedo`
- adversarial `TerrainCommand_RepeatedNeighborPreviewsStoreOneDeltaPerCoordinate`
- `TerrainCommand_AccountingUsesCoalescedLayerBufferCapacity`
- `TerrainCommand_OverCapRemainsAppliedDirtyAndNotUndoable`
- `FailedCanceledAndNoOpTerrainStrokesConsumeNoBytesOrVersion`.

Grow a line one cell at a time so an endpoint changes variants repeatedly; inspect the final command and assert unique `(x,y,layer)` and exact pre-begin/final `Before`/`After`.

**Step 2: Run focused tests**

```bash
dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj \
  --filter 'FullyQualifiedName~TerrainCommand|FullyQualifiedName~FailedCanceledAndNoOpTerrain' -v minimal
```

Expected: PASS. If red, fix coalescing/session integration first; do not create terrain-specific command/history types.

**Step 3: Run the full Core suite**

```bash
dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj -v minimal
```

Expected: all Core tests pass, including existing maximum-stroke, cap, cancellation, resize, and persistence tests.

| Invariant | Proved by |
|---|---|
| Undo/redo exactly round-trip all repaired values | undo/redo tests |
| Savepoint/events match ordinary commands | dirty/version tests |
| Preview churn does not inflate command deltas | adversarial repeated-preview test |
| Accounting/cap behavior remains generic | accounting/over-cap tests |
| Failure/cancel/no-op publish nothing | no-bytes/version test |
| Manual editing remains unchanged | full Core suite |

**Step 4: Commit**

```bash
git add tests/MapEditor.Core.Tests/MapEditHistoryTests.cs \
  tests/MapEditor.Core.Tests/MapEditStrokeStorageTests.cs
git commit -m "test: verify terrain edit history"
```

Add production files only if they actually changed.

## Final red-team check

- Part 1 schema is consumed unchanged.
- Only captured `TopLayer` changes; flags and other layers remain exact.
- Every preview is resolved cumulatively before mutation, including displaced neighbors.
- Hashing follows the exact SHA-256 contract and excludes display name/order.
- Missing mappings restore the whole gesture and publish no history/state transition.
- Completion emits one coalesced ordinary layer command; cancellation emits none.
- Resolver construction has an explicit construct/validate/publish boundary and no shared mutable registry.
- All mutation is synchronous; no threading marshal is needed.
- No persistence, UI, assets, rendering, manager, or generated output enters this part.

Final verification:

```bash
dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj -v minimal
git status --short
```

Expected: all Core tests pass; only intended source/tests are changed and no `Assets/` output exists.
