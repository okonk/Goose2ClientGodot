# Terrain Brush Part 2: Core Editing Implementation Plan

**Goal:** Add deterministic terrain pattern scoring and resolution, live preview strokes, cancellation, and one-command map undo/redo on the topmost selected layer.

**Architecture:** A pure `TerrainMapResolver` reads immutable Part 1 indexes and resolves final logical-center intent into ordinary `MapTileLayer` patches. A terrain-specific stroke accumulates interpolated intent and coalesces repeatedly repaired halo cells while previewing through the live `MapDocument`. `MapEditSession` owns either one manual stroke or one terrain stroke and commits both through the existing `MapLayerChangesCommand` history path.

**Tech Stack:** C# 12, .NET 8, xUnit, existing map edit/history primitives.

---

Part 2 of 4. Start only after Part 1 is implemented and green.

## APIs verified before planning

- `MapDocument.SetLayer` is the canonical one-layer mutation at `src/MapEditor.Core/MapDocument.cs:193-202`; terrain must not add map-format state.
- `MapEditSession.TopLayer` chooses the highest selected layer at `src/MapEditor.Core/Editing/MapEditSession.cs:56-69`.
- Manual strokes preview through the live document, interpolate, and commit one command at `src/MapEditor.Core/Editing/MapEditSession.cs:104-149`.
- `GridLine.Enumerate` includes both segment endpoints at `src/MapEditor.Core/Editing/GridLine.cs:8-37`.
- Cancellation reverse-replays preview changes without history at `src/MapEditor.Core/Editing/MapEditSession.cs:381-396`.
- Undo/redo replay exact command values and state IDs at `src/MapEditor.Core/Editing/MapEditSession.cs:398-451`; completion enters history only through `PushLayerCommand` at `src/MapEditor.Core/Editing/MapEditSession.cs:483-489`.
- `DocumentEditTimeline` will observe one terrain commit through the existing `MapEditSession.HistoryChanged` subscription at `src/MapEditor.App/ViewModels/DocumentEditTimeline.cs:18-24,335-355`; no App timeline changes belong in this part.

## Locked Core surface

Add:

```csharp
public enum TerrainEditMode
{
    Paint,
    Erase
}

public sealed record TerrainResolutionFailure(
    Guid? TerrainId,
    int X,
    int Y,
    string Message);

public readonly record struct TerrainEditResult(
    bool IsActive,
    bool Changed,
    TerrainResolutionFailure? Failure)
{
    public bool Succeeded => Failure is null;
}

internal interface ITerrainPatchResolver
{
    Guid? GetLogicalCenter(MapTileLayer graphic);

    bool TryResolvePatch(
        MapDocument document,
        int layer,
        IReadOnlyDictionary<int, Guid?> centerOverrides,
        IReadOnlyCollection<int> directlyChangedIndices,
        out TerrainResolvedPatch patch,
        out TerrainResolutionFailure? failure);
}

public sealed class TerrainMapResolver : ITerrainPatchResolver
{
    public TerrainMapResolver(TerrainCatalogIndex catalog);

    public Guid? GetLogicalCenter(MapTileLayer graphic);

    internal bool TryResolvePatch(
        MapDocument document,
        int layer,
        IReadOnlyDictionary<int, Guid?> centerOverrides,
        IReadOnlyCollection<int> directlyChangedIndices,
        out TerrainResolvedPatch patch,
        out TerrainResolutionFailure? failure);

    bool ITerrainPatchResolver.TryResolvePatch(
        MapDocument document,
        int layer,
        IReadOnlyDictionary<int, Guid?> centerOverrides,
        IReadOnlyCollection<int> directlyChangedIndices,
        out TerrainResolvedPatch patch,
        out TerrainResolutionFailure? failure)
        => TryResolvePatch(document, layer, centerOverrides, directlyChangedIndices, out patch, out failure);
}
```

Extend `MapEditSession` with:

```csharp
public TerrainEditResult BeginTerrainStroke(
    TerrainMapResolver resolver,
    Guid terrainId,
    TerrainEditMode mode,
    int x,
    int y);

public TerrainEditResult ContinueTerrainStroke(int x, int y);
```

`CompleteStroke()` and `CancelStroke()` remain the shared completion API. `BeginStroke(MapEditTool.Terrain, ...)` must throw rather than bypass terrain resolution. A valid terrain begin always creates an active gesture, even when its first erase sample is empty or unrecognized; that sample is a no-op, but the user may continue dragging into recognized terrain. Releasing a zero-delta gesture creates no command.

## Stable selection algorithm

Use literal ASCII domain tag `terrain-pattern-v1` for pattern selection and `terrain-variant-v1` for variant selection. Use FNV-1a 64 with offset `14695981039346656037` and prime `1099511628211`. Fold every framing and payload byte in order. Encode the ASCII domain tag as unsigned 32-bit little-endian byte length followed by bytes. Encode center terrain ID as lowercase GUID `N` UTF-8 with the same length prefix, then X and Y as signed 32-bit little-endian two's-complement bytes. Encode desired peers in N/E/S/W/NE/SE/SW/NW order: marker byte `0` for null; or marker `1`, unsigned 32-bit little-endian length `32`, then lowercase GUID `N` bytes. The variant domain additionally appends all nine selected candidate values in Center/N/E/S/W/NE/SE/SW/NW order with the same nullable encoding. Never use `HashCode`, `Guid.GetHashCode`, or `string.GetHashCode`.

Golden vectors use id1 `00000000-0000-0000-0000-000000000001`, id2 `01234567-89ab-cdef-0123-456789abcdef`, and id3 `ffffffff-ffff-ffff-ffff-ffffffffffff`: pattern domain, center id1, `(0,0)`, all-null desired peers → `0x10AD570CEC1F1968`; pattern domain, center id2, `(-1,42)`, desired peers `[id1,null,id3,id2,null,id1,null,id3]` → `0x9E98B8B54B27BAA4`; variant domain, center id1, `(7,9)`, all desired and all candidate values id1 → `0x94351B02ED09FACD`.

Distinct patterns sort by peers in `Center, North, East, South, West, NorthEast, SouthEast, SouthWest, NorthWest` order, with `null` before GUID canonical text. Variants sort by `(Sheet, Graphic)`. Unsigned hash remainder selects from tied sorted patterns and then variants.

## Task 1: Add desired-pattern construction, scoring, and stable hashing

**Files:**
- Create: `src/MapEditor.Core/Terrain/TerrainPatternScorer.cs`
- Create: `src/MapEditor.Core/Terrain/TerrainStableHash.cs`
- Test: `tests/MapEditor.Core.Tests/Terrain/TerrainPatternScorerTests.cs`
- Test: `tests/MapEditor.Core.Tests/Terrain/TerrainStableHashTests.cs`

**Mutation impact:**
- Source of truth changed: none; these are pure functions over immutable patterns.
- Important readers: resolver in Tasks 2–3.
- Derived/cached state affected: candidate score and deterministic tie index only.
- Required propagation: desired peers → grouped candidate patterns → score → stable tie index → sorted variant index.
- Invariants: duplicate variants cannot increase a pattern score; sides weigh 2 and corners 1; hashes are process/platform/order independent.
- Observable proof: fixed byte/hash vectors and shuffled candidate lists produce identical selections.

**Steps:**
1. Add failing table tests for all score cells: exact named/`None` adds weight, candidate `None` versus named adds zero, wrong named and named-versus-`None` subtract weight.
2. Test side weight 2, corner weight 1, score range `[-12, 12]`, and an exact eight-peer pattern outranking every incomplete alternative.
3. Add golden FNV vectors including negative coordinates, all-null peers, and mixed GUID peers. Run tests in at least two current cultures to prove culture independence.
4. Implement pure scoring and the exact length-prefixed hash encoding above.
5. Run focused tests and commit: `git commit -m "feat: score terrain peering patterns"`.

| Invariant | Proved by |
|---|---|
| Cardinal mismatches dominate corner matches | `Score_SideMismatchOutweighsCornerMatch` |
| Generic `None` is neutral only against named desired peers | scoring theory |
| Stable hash never uses process-randomized hashing | fixed-vector and culture tests |

## Task 2: Resolve one logical terrain cell deterministically

**Files:**
- Create: `src/MapEditor.Core/Terrain/TerrainMapResolver.cs`
- Create: `src/MapEditor.Core/Terrain/TerrainResolvedPatch.cs`
- Test: `tests/MapEditor.Core.Tests/Terrain/TerrainMapResolverTests.cs`

**Mutation impact:**
- Source of truth changed: none; resolver produces a patch without applying it.
- Important readers: terrain stroke in Task 4.
- Derived/cached state affected: desired pattern and chosen graphic for a coordinate.
- Required propagation: infer logical centers via Part 1 graphic index → overlay cumulative intended centers → build cardinal/diagonal desired peers → score distinct patterns → hash-select tied pattern/variant.
- Invariants: center candidates are filtered before scoring; outside/empty/unrecognized is `None`; current JSON/list order cannot change output.
- Observable proof: equivalent shuffled indexes resolve byte-identical patches.

**Steps:**
1. Add tests for `GetLogicalCenter` with default, `(sheet, 0)`, recognized, and unrecognized graphics.
2. Add orientation tests using unique terrain IDs at N/E/S/W/NE/SE/SW/NW. At boundaries, assert clipped neighbors become `None`.
3. Test exact transition, generic-`None` fallback, wrong-named penalty, tied distinct patterns, duplicate-pattern variants, coordinates, and shuffled catalog order.
4. Implement a pure single-cell chooser. Grouping must come from Part 1's index; never score each variant separately.
5. Return contextual failure for an unknown requested center or empty candidate pool without mutating the document.
6. Run focused tests and commit: `git commit -m "feat: resolve terrain graphics deterministically"`.

| Invariant | Proved by |
|---|---|
| Corners read diagonal logical centers | `Resolve_UsesAllEightExpectedCoordinates` |
| Candidate center always equals requested logical center | `Resolve_NeverChoosesHigherScoringWrongCenter` |
| Duplicate variants do not bias tied pattern selection | `Resolve_DuplicateVariants_PreservesPatternChoice` |

## Task 3: Resolve atomic multi-cell paint and erase patches

**Files:**
- Modify: `src/MapEditor.Core/Terrain/TerrainMapResolver.cs`
- Modify: `src/MapEditor.Core/Terrain/TerrainResolvedPatch.cs`
- Test: `tests/MapEditor.Core.Tests/Terrain/TerrainStrokeResolverTests.cs`

**Mutation impact:**
- Source of truth changed: none; cumulative logical-center overrides are the input source of truth for a pending gesture.
- Important readers: Task 4 stroke preview.
- Derived/cached state affected: union of directly changed cells and clipped eight-neighbor halo.
- Required propagation: validate all override indices/IDs → compute effective final centers → collect affected union → resolve every recognized/overridden center into a temporary patch → return only after every target succeeds.
- Invariants: final intent, not partially selected graphics, drives all cells; direct manual targets may be overwritten; manual neighbors remain absent from the patch; failure returns no partial patch.
- Observable proof: two adjacent terrain changes resolve both sides from the same final center map.

**Steps:**
1. Add red tests for a single paint, adjacent A/B paints, both sides of A↔B boundaries, corners, multiple directly changed cells, and map-edge clipping.
2. Prove direct paint replaces an unrecognized graphic while an unrecognized halo neighbor is untouched.
3. Represent erase as a direct override to `null`; it emits default `MapTileLayer` only for recognized directly visited cells. Empty/unrecognized erase inputs produce no changed indices and no halo work.
4. Add an adversarial prevalidation test with a valid first override and an unknown terrain ID later in row-major order; assert `TryResolvePatch` rejects the complete request before exposing a patch and leaves the document unchanged.
5. Implement sorted row-major patch output so later accumulation and tests are deterministic.
6. Run focused tests and commit: `git commit -m "feat: resolve terrain edit patches"`.

| Invariant | Proved by |
|---|---|
| Multi-cell output uses one final logical-center snapshot | `ResolvePatch_AdjacentTransitions_RepairsBothSides` |
| Manual neighbors cannot be rewritten | `ResolvePatch_UnrecognizedHalo_IsExcluded` |
| Failure exposes no partial patch | `ResolvePatch_LateFailure_ReturnsNoChanges` |

## Task 4: Add coalesced preview accumulation and terrain strokes

**Files:**
- Create: `src/MapEditor.Core/Editing/MapLayerChangeAccumulator.cs`
- Create: `src/MapEditor.Core/Editing/TerrainMapEditStroke.cs`
- Create: `src/MapEditor.Core/Terrain/TerrainEditResult.cs`
- Test: `tests/MapEditor.Core.Tests/Terrain/MapLayerChangeAccumulatorTests.cs`
- Test: `tests/MapEditor.Core.Tests/Terrain/TerrainMapEditStrokeTests.cs`

**Mutation impact:**
- Source of truth changed: live `MapDocument` layer values are preview-mutated; cumulative center overrides own pending logical intent.
- Important readers: canvas rendering reads the live document; session history reads only the final change buffer.
- Derived/cached state affected: first-before/latest-after values, net change count, visited bitmap, previous sample, affected halo.
- Required propagation: stage interpolated visits → reject/no-op filtering → resolve complete patch → apply to document → coalesce first/latest values → publish intent/visited/previous sample. Cancellation restores first values; completion emits only first-to-final deltas.
- Invariants: preview creates no history; repeated halo changes preserve the original before value; failed staging advances no stroke state; output is row-major.
- Observable proof: repeatedly change one halo cell and verify cancel restores its pre-begin value and completion stores one delta.

**Steps:**
1. Implement red accumulator tests for first/latest coalescing, return-to-original removal, row-major output, restore, and retained-byte accounting compatible with `MapEditChangeBuffer<MapLayerChange>`.
2. Add stroke tests for first sample, sparse `GridLine` interpolation, overlapping segments, repeated visits, final-state intent, captured resolver/terrain/mode/layer, and immediate document preview.
3. Capture Paint/Erase mode at begin. Paint assigns the selected terrain for every new visit. Erase accepts only a currently effective recognized center and erases any recognized terrain.
4. Construct `TerrainMapEditStroke` against internal `ITerrainPatchResolver`; production receives `TerrainMapResolver`, while tests inject a resolver that fails after an earlier successful segment. Resolve and validate a segment before publishing visits. On failure, restore the complete stroke, release buffers, and return inactive failure.
5. Ensure invalid continuation coordinates do not alter the previous sample, intent, preview, or visit bitmap.
6. Run focused tests and commit: `git commit -m "feat: preview atomic terrain strokes"`.

| Invariant | Proved by |
|---|---|
| First pre-stroke value survives repeated repairs | `Accumulator_RepeatedCoordinate_RestoresOriginal` |
| One sparse drag visits every interpolated cell | `Continue_SparseSamples_HasNoGaps` |
| Failed continuation cancels all preview mutation | `Continue_LateResolutionFailure_RestoresWholeStroke` |

## Task 5: Integrate terrain strokes with MapEditSession and normal history

**Files:**
- Modify: `src/MapEditor.Core/Editing/MapEditTool.cs:3-12`
- Modify: `src/MapEditor.Core/Editing/MapEditSession.cs:42-158,224-330,381-499`
- Test: `tests/MapEditor.Core.Tests/Terrain/TerrainMapEditSessionTests.cs`
- Modify: `tests/MapEditor.Core.Tests/MapEditSessionTests.cs`

**Mutation impact:**
- Source of truth changed: `MapEditSession` gains terrain active-stroke state and commits final terrain deltas to its existing map history.
- Important readers: `HasActiveStroke`, `CanUndo`, `CanRedo`, `IsDirty`, patches/fill/resize/savepoint/history cap, App timeline, and Part 4 canvas.
- Derived/cached state affected: current/saved state IDs, undo/redo, retained bytes, `HistoryVersion`, and one `HistoryChanged` event on commit only.
- Required propagation: begin captures top layer/catalog snapshot → preview mutates document only → complete builds one `MapLayerChangesCommand` through `PushLayerCommand` → existing timeline observes one event; cancel restores and emits none.
- Invariants: manual and terrain strokes are mutually exclusive; every mutation/history API rejects while either is active; no-op/cancel/failure preserves redo and state IDs.
- Observable proof: subscribe to history, perform a multi-cell terrain drag, assert one event/undo entry and exact undo/redo graphics.

**Steps:**
1. Append `Terrain` to `MapEditTool`, but change manual `ValidateTool` at `MapEditSession.cs:491-497` to explicit Pencil/Eraser/Eyedropper acceptance so `BeginStroke(Terrain, ...)` fails.
2. Add red tests for `BeginTerrainStroke` validation, topmost noncontiguous layer selection, an active zero-delta erase beginning on an unrecognized cell and continuing into terrain, active-stroke mutual exclusion, and unknown terrain failure. The public overload accepts `TerrainMapResolver`; an internal overload accepts `ITerrainPatchResolver` for failure-atomicity tests.
3. Replace every `_stroke`-only guard and dirty/command property with common manual-or-terrain checks, including flood fill, layer/blocked patches, resize, undo, redo, `MarkSaved`, `DiscardRedo`, `ClearHistory`, and `SetRetainedHistoryCap`. Add direct Core tests that `DiscardRedo` and `ClearHistory` reject without changing history while either stroke kind is active.
4. Dispatch shared `CompleteStroke` and `CancelStroke` to the active kind. Terrain completion uses the existing `PushLayerCommand`; do not create a terrain-specific history command.
5. Add adversarial tests for redo preservation on no-op/cancel/failure, one event per committed drag, dirty state during preview and after cancel, savepoint transitions, retained cap eviction, and exact variant redo.
6. Run all Core tests and commit: `git commit -m "feat: integrate terrain strokes with map history"`.

| Invariant | Proved by |
|---|---|
| Only one stroke kind can be active | `BeginTerrainStroke_DuringManualStroke_Throws` and inverse |
| One gesture is one normal map command | `CompleteTerrainStroke_ManyCells_RaisesOneHistoryEvent` |
| Cancel/no-op does not damage redo or savepoint | `CancelTerrainStroke_PreservesRedoAndDirtyState` |

## Task 6: Prove map-format and App timeline compatibility

**Files:**
- Test: `tests/MapEditor.Core.Tests/Terrain/TerrainMapFormatTests.cs`
- Test: `tests/MapEditor.App.Tests/DocumentEditTimelineTests.cs`

**Mutation impact:**
- Source of truth changed: tests exercise normal map/history state; no new production mutation.
- Important readers: `MapCodec`, `MapEditSession`, and `DocumentEditTimeline`.
- Derived/cached state affected: encoded bytes and cross-domain undo/redo timeline.
- Required propagation: terrain commit → existing map history event → existing App timeline entry → map codec serializes ordinary sheet/graphic pairs.
- Invariants: no map schema/version change; one terrain gesture occupies one timeline position; sheet edits still interleave correctly.
- Observable proof: encode/decode a terrain-painted map and compare every map field without any terrain metadata.

**Steps:**
1. Add a Core round-trip test painting terrain on a fixture, encoding through the unchanged codec, decoding, and asserting exact layers/flags/header.
2. Add App timeline tests interleaving one terrain gesture with sheet edits, then undo/redo through the existing timeline.
3. Assert a canceled terrain preview creates no timeline entry.
4. Run Core and App suites and require no map codec change across the complete feature range.
5. Commit: `git commit -m "test: verify terrain map compatibility"`.

| Invariant | Proved by |
|---|---|
| Terrain adds no map metadata | `TerrainPaint_EncodeDecode_PreservesOrdinaryMapFormat` |
| App sees one gesture as one edit | `Timeline_TerrainGesture_UsesOneMapEntry` |
| Cancellation remains outside history | `Timeline_CancelledTerrainStroke_AddsNoEntry` |

## Part 2 completion

Run:

```bash
dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj -v minimal
dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj --filter DocumentEditTimelineTests -v minimal
dotnet build src/MapEditor.App/MapEditor.App.csproj -c Release
git diff --check
git diff --exit-code 5711959...HEAD -- src/MapEditor.Core/MapCodec.cs
```

Expected: all commands pass and map codec remains untouched.
