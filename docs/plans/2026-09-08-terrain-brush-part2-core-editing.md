# Terrain Brush Part 2: Core Editing Implementation Plan

**Goal:** Add deterministic, atomic terrain paint/erase strokes to `MapEditor.Core`, including topology-local neighbor repair, preview, cancellation, and ordinary map undo/redo.

**Architecture:** Build an immutable enabled-set runtime index over Part 1's catalog and validate every runtime-relevant invariant before it can be used. A dedicated terrain stroke retains cumulative ownership intent and first displaced owners for the gesture, but each update resolves only newly effective cells and the owner-specific local stencils whose masks can have changed. A first-before/latest-after accumulator applies complete row-major preview patches and emits one existing `MapLayerChangesCommand` on completion.

**Tech Stack:** C#; .NET 8 `MapEditor.Core`; xUnit; `System.Security.Cryptography`.

---

> Implement with @executing-plans, one task and commit at a time. Follow `AGENTS.md`: add no comments/doc strings unless a non-obvious invariant genuinely requires one.

## Scope, prerequisites, and cross-part boundary

Part 1 is required. Consume its schema unchanged, including `TerrainCatalog`, `TerrainSetDefinition`, `TerrainMaskDefinition`, revised `TerrainMemberDefinition`, `TerrainMemberProvenance`, `TerrainGraphicReference`, `TerrainTopology`, `TerrainReviewStatus`, `TerrainMasks`, `TerrainGeneratedId`, `TerrainCatalogValidator`, and `TerrainValidationIssue` in `MapEditor.Core.Terrain` (`docs/plans/2026-09-08-terrain-brush-part1-converter.md:58-253,298-365`).

Included:

- immutable enabled-terrain lookup and constructor-time runtime validation;
- deterministic normalized-mask variant selection;
- the Core-owned `MapEditTool.Terrain` value and isolated terrain stroke APIs;
- topmost-layer paint/erase, selected/displaced owner repair, interpolation, preview, rollback, cancellation, completion, history, dirty/savepoint state, undo/redo, and bounded stroke work/storage;
- an intentionally narrow missing/empty-mask fault-injection carve-out for the design-required Core rollback path;
- proof that terrain writes remain ordinary map layers in the unchanged `.map` format.

Excluded: JSON/manifest/frame loading, Rendering/App/UI, pointer capture, manager workflows, catalog schema changes, map-format changes, terrain-pair transitions, closest-mask fallback, and terrain flood fill. No persistence migration is needed.

Part 2 changes only Core production code and Core tests. App currently accepts tools only through `FloodFill` (`src/MapEditor.App/ViewModels/MapDocumentViewModel.cs:121-141`) and its canvas default branch routes stroke tools to the manual API (`src/MapEditor.App/Controls/MapCanvas.cs:398-438,542-557`). That is the intentional intermediate boundary: after Part 2, App still cannot activate Terrain; Part 3 expands the App validator and routes Terrain explicitly. `DocumentEditTimeline` consumes one generic map `HistoryChanged` event per pushed command (`src/MapEditor.App/ViewModels/DocumentEditTimeline.cs:335-355`), so Part 2 must use existing history rather than introduce a terrain timeline. Part 3 supplies the cross-layer timeline/canvas proof.

Compile-time boundaries to preserve:

- `MapTileLayer` is a value type (`public readonly record struct`) at `src/MapEditor.Core/MapDocument.cs:5`; copy it directly in patches/changes.
- A `MapEditSession` owns a readonly document and exposes only that instance (`src/MapEditor.Core/Editing/MapEditSession.cs:10,32`); Core has no document-replacement API.
- Core owns neither the App's active-tool property nor asset/catalog publication. Changing the selected terrain, Paint/Erase mode, active tool, document, or published catalog is a Part 3 concern. Core captures the resolver, terrain, mode, and layer at successful begin; Part 3 must cancel before replacing a catalog/document.

## APIs and repository facts verified before planning

| Fact/API | Citation and consequence |
|---|---|
| Core owns definitions, mask resolution, deterministic selection, and atomic edits; the selected top layer is edited as ordinary tiles | `docs/plans/2026-09-08-terrain-brush-design.md:38-46`. Keep all Part 2 production work in Core. |
| Paint adds selected membership; erase clears only a selected-terrain member; drag previews and commits once; cancellation restores exact values | `docs/plans/2026-09-08-terrain-brush-design.md:84-92`. Reuse the existing stroke/history lifecycle. |
| Membership is `(sheet, graphic)` and affected cells are direct cells plus selected/displaced terrain neighbors | `docs/plans/2026-09-08-terrain-brush-design.md:94-106`. Repair owner-specific local stencils, not the whole map. |
| Enabled topology/ID/completeness/frame/overlap validity is required | `docs/plans/2026-09-08-terrain-brush-design.md:108-117`. Core proves catalog semantics; Part 3 additionally proves frames and never publishes an incomplete enabled set. |
| Core coverage explicitly requires 16/47 masks, normalization, stable variants, active layer, overwrite, interpolation, no-op, incomplete-set atomicity, cancellation, and exact replay | `docs/plans/2026-09-08-terrain-brush-design.md:138-146`. |
| Part 1 fixes top-left coordinates, direction bits, all 16/47 masks, stable IDs, status-aware validation, graphic-zero rejection, and variant-order significance | `docs/plans/2026-09-08-terrain-brush-part1-converter.md:249-365`. Use those helpers; do not duplicate normalization or ID derivation. |
| `TopLayer` returns the highest selected bit; selected layer/brush remain mutable | `src/MapEditor.Core/Editing/MapEditSession.cs:42-76`. Capture the layer at begin. |
| Existing manual begin/continue applies interpolation immediately; completion transfers one buffer; cancel reverse-replays it | `src/MapEditor.Core/Editing/MapEditSession.cs:104-149,381-396`; `src/MapEditor.Core/Editing/MapEditStroke.cs:44-86`. Preserve this path rather than folding terrain state into it. |
| Existing manual-tool validation currently uses an ordinal range | `src/MapEditor.Core/Editing/MapEditSession.cs:491-497`. Replace it with an explicit manual set so `Terrain` can never enter `BeginStroke`. |
| Every active-stroke guard currently checks one `_stroke` field | `src/MapEditor.Core/Editing/MapEditSession.cs:78-101,153-154,216-217,270-271,332-333,398-400,423-425,446-448,466-468`. Every guard must account for the new terrain stroke. |
| Grid interpolation includes both endpoints and uses signed coordinates internally | `src/MapEditor.Core/Editing/GridLine.cs:8-38`; tests at `tests/MapEditor.Core.Tests/GridLineTests.cs:10-197`. Public session coordinates still require in-bounds nonnegative values. |
| `MapDocument.SetLayer` validates layer/coordinates and mutates one layer only | `src/MapEditor.Core/MapDocument.cs:169-211`. Prevalidate a complete patch before the first write. |
| Active deltas make a session dirty and disable undo/redo; state IDs advance only through `PushLayerCommand` | `src/MapEditor.Core/Editing/MapEditSession.cs:78-101,483-489`. Terrain previews must feed the same readers without advancing history. |
| Layer commands replay forward/reverse and account segmented allocated capacity | `src/MapEditor.Core/Editing/MapEditCommand.cs:26-69`; `src/MapEditor.Core/Editing/MapEditChangeBuffer.cs:6-72`. Emit one final delta per coordinate. |
| `PushUndo` clears redo, evicts to the cap, and raises one version/event | `src/MapEditor.Core/Editing/MapEditHistory.cs:35-47,101-130`. Do not add terrain-specific command/history types. |
| Existing tests pin invalid-continuation sample retention, rollback, no-op redo, maximum storage, cap behavior, events, persistence, and wire layout | `tests/MapEditor.Core.Tests/MapEditStrokeTests.cs:108-228`; `tests/MapEditor.Core.Tests/MapEditStrokeStorageTests.cs:85-177`; `tests/MapEditor.Core.Tests/MapEditHistoryTests.cs:113-128,253-271,417-466`; `tests/MapEditor.Core.Tests/MapCodecTests.cs:113-141`. |
| `.map` encoding is fixed at a 12-byte header plus 34 bytes per tile with five ordinary layer pairs | `src/MapEditor.Core/MapCodec.cs:7-10,80-120`. Terrain must add no codec field or sidecar. |

## Locked public Part 2 contract

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

Append `Terrain` after `FloodFill` in the Core-owned `MapEditTool` enum. `BeginStroke(MapEditTool.Terrain, ...)` is always rejected; only the dedicated terrain API accepts terrain edits.

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

`CompleteStroke()` and `CancelStroke()` remain the shared completion APIs. A successful no-op begin still publishes an active terrain gesture. `Changed` means that this successful call changed at least one concrete preview `MapTileLayer` from its value immediately before the call; it does not mean cumulative net change. Every failure update has `Changed == false`. A missing or empty normalized required-mask mapping returns `Succeeded == false` with exact terrain ID/mask. Begin failure publishes nothing; continuation failure restores the entire gesture, clears it, and publishes no history/state transition.

IDs use `StringComparer.Ordinal`. Hash inputs use invariant signed decimal coordinates/mask. Public begin/continue reject negative and out-of-range coordinates before any stroke mutation.

### Public update and exception table

Validation order is the row order below within each operation. A rejected call never mutates document bytes, selection/brush, active stroke, prior sample, cumulative intent, history/version/bytes, current/saved state IDs, dirty state, undo/redo, or events.

| Operation/condition | Exact outcome |
|---|---|
| `new TerrainMapResolver(null)` | `ArgumentNullException`, parameter `catalog`. |
| Resolver with no enabled sets, or only pending/disabled sets | Constructor succeeds with an empty enabled index. |
| Resolver with a rejected enabled-set defect | `ArgumentException`, parameter `catalog`; message starts `Enabled terrain catalog is invalid: <code>: <Part 1 message>`. Use the first issue in Part 1's locked ordering. |
| Resolver whose only enabled issues are `enabled-mask-missing` and/or `enabled-mask-empty` | Constructor succeeds solely for Core fault-injection/rollback; lookups return failure, never throw. |
| `BeginTerrainStroke` null resolver | `ArgumentNullException`, parameter `resolver`. |
| Null `terrainId` | `ArgumentNullException`, parameter `terrainId`. |
| Empty/whitespace `terrainId` | `ArgumentException`, parameter `terrainId`. Do not trim IDs. |
| Unknown `TerrainEditMode` | `ArgumentOutOfRangeException`, parameter `mode`. |
| Begin `x`, then `y`, outside the document | `ArgumentOutOfRangeException` for the offending parameter. |
| Unknown, pending, disabled, or ordinal-case-mismatched `terrainId` | `ArgumentException`, parameter `terrainId`. |
| Either begin API with otherwise valid arguments while either stroke kind is active | `InvalidOperationException`; the existing stroke is unchanged. |
| `BeginStroke(MapEditTool.Terrain, ...)` or any other nonmanual tool | `ArgumentOutOfRangeException`, parameter `tool`, before active-stroke publication. |
| Successful terrain begin with empty patch | `Succeeded=true`, `Changed=false`, `Failure=null`, active terrain stroke exists. |
| Successful terrain begin with preview patch | `Succeeded=true`, `Changed=true`, `Failure=null`, complete preview visible, active terrain stroke exists. |
| Begin missing/empty required mask | `Succeeded=false`, `Changed=false`, exact failure, no active stroke and no mutation/history. |
| `ContinueStroke` with no manual stroke, including while terrain is active | `InvalidOperationException` before coordinate validation. |
| `ContinueTerrainStroke` with no terrain stroke, including while manual is active | `InvalidOperationException` before coordinate validation. |
| Either continuation with invalid `x`/`y` for the correct active kind | `ArgumentOutOfRangeException`; exact preview, active state, previous sample, history, and IDs remain unchanged. |
| Successful terrain continuation | Same success tuple rules as begin; previous sample advances only after full resolution/application succeeds. |
| Terrain continuation missing/empty required mask | Failure tuple; restore all pre-begin layer values, clear active stroke, keep history/version/redo/state IDs exact. |
| Shared complete with active manual/terrain stroke | Existing manual behavior; terrain returns `true` and pushes one ordinary layer command only for net changes, otherwise `false` with no history/redo/state-ID change. |
| Shared cancel with active manual/terrain stroke | Restore exact pre-begin state and push nothing; return type remains `void`. |
| Shared complete/cancel with no stroke | Existing `InvalidOperationException`. |

Argument checks deliberately precede the active-stroke check on begin, preserving the current manual API's validation precedence. Continuation first verifies the active stroke kind, matching its need to reject the wrong entry point without touching that stroke.

## Resolver construction and validation boundary

`TerrainMapResolver` first rejects an unknown `TerrainReviewStatus` value, then examines only definitions whose status is exactly `Enabled`. Pending/disabled definitions do not own IDs or members, do not conflict with an enabled set, and may retain empty/missing masks, invalid semantic review data, duplicate IDs, or overlapping references without affecting runtime construction. Catalog-level generator/settings/fingerprint/root-diagnostic issues are not runtime lookup inputs. Part 1 and Part 3 validate those full-catalog concerns.

For the enabled-only projection, use Part 1's validator and issue ordering. Reject every set-scoped issue except `enabled-mask-missing` and `enabled-mask-empty`:

| Enabled data | Constructor behavior |
|---|---|
| Blank, duplicate, or generated-ID-mismatched ID; ordinal duplicate ID | Reject. |
| Invalid display name, topology, status, metrics, provenance, or set diagnostic | Reject. |
| Empty members, duplicate member, graphic zero (including `(0,0)` or `(sheet,0)`), sheet outside Int16 | Reject. |
| Duplicate/unreachable mask; duplicate variant; variant absent from members; graphic-zero variant through either wiring path | Reject. |
| Enabled member unused by every variant | Reject. |
| Same member in two enabled sets | Reject both within construction; no resolver is published. |
| Missing required mask or present required mask with zero variants, with no other enabled issue | Accept only as recoverable resolution failure. |
| All 16/47 required mappings nonempty and otherwise valid | Accept. |

Construct private ID, member-owner, topology, and mask/variant indexes completely, copy arrays preserving variant order, then assign fields. No partially built resolver escapes. Frame existence/32×32 is not knowable in Core and remains Part 3's enabled-only validation.

The carve-out does not weaken the approved production validity rule. Part 1 still reports missing/empty enabled masks, and Part 3 must reject every Core/frame issue before constructing/publishing `TerrainAssetCatalog`. Only direct Core tests intentionally construct such a resolver to exercise the approved no-patch and whole-gesture rollback contract. Because every other runtime-derived defect is rejected and every lookup is Try/result-based, no represented catalog defect can throw after a terrain stroke starts.

## Locked internal APIs and ownership

These are internal implementation contracts, not additional public surface. Collection-bearing types copy their input; returned lists are read-only.

```csharp
internal sealed class TerrainRuntimeSet
{
    internal string Id { get; }
    internal TerrainTopology Topology { get; }
}

internal readonly record struct TerrainVariantLookup(
    bool Succeeded,
    TerrainGraphicReference Variant,
    int NormalizedMask,
    TerrainEditFailure? Failure);
```

`TerrainMapResolver` owns only catalog-domain indexing and exposes internally:

```csharp
internal bool TryGetEnabledTerrain(string terrainId, out TerrainRuntimeSet terrain);
internal bool TryGetOwner(TerrainGraphicReference reference, out TerrainRuntimeSet terrain);
internal TerrainVariantLookup ResolveVariant(
    TerrainRuntimeSet terrain,
    int rawMask,
    int x,
    int y);
```

`TryGetEnabledTerrain` and `TryGetOwner` never throw for ordinary input. `ResolveVariant` calls Part 1 `TerrainMasks.Normalize`, returns the normalized mask in both paths, returns a `TerrainEditFailure` for only missing/empty mappings, and otherwise returns one copied variant. It accepts only resolver-created, constructor-validated `TerrainRuntimeSet` handles. It performs no document/history mutation.

`TerrainStrokeResolver` is the sole owner of `TerrainGraphicReference` ↔ `MapTileLayer` adaptation: `new TerrainGraphicReference(layer.Sheet, layer.Graphic)` for lookup and `new MapTileLayer(reference.Sheet, reference.Graphic)` for a target. Any layer with `Graphic == 0`, including noncanonical `(sheet,0)`, has no terrain owner. Erase always emits canonical `(0,0)`.

Use the following row-major (`CellIndex = y * width + x`) intent, displaced-owner, request, patch, and result shapes:

```csharp
internal readonly record struct TerrainCellIntent(
    int CellIndex,
    TerrainRuntimeSet? Owner);

internal readonly record struct TerrainDisplacedOwner(
    int CellIndex,
    TerrainRuntimeSet Owner);

internal sealed class TerrainCumulativeIntent
{
    internal int Count { get; }
    internal bool TryGetOwner(int cellIndex, out TerrainRuntimeSet? owner);
    internal void Publish(IReadOnlyList<TerrainCellIntent> additions);
}

internal sealed class TerrainDisplacedOwners
{
    internal int Count { get; }
    internal bool TryGetFirst(int cellIndex, out TerrainRuntimeSet owner);
    internal void Publish(IReadOnlyList<TerrainDisplacedOwner> additions);
}

internal readonly record struct TerrainPatchCell(
    int CellIndex,
    MapTileLayer Target);

internal sealed class TerrainMapPatch
{
    internal IReadOnlyList<TerrainPatchCell> Cells { get; }
    internal TerrainMapPatch(IEnumerable<TerrainPatchCell> cells);
}

internal readonly record struct TerrainStrokeResolveRequest(
    MapDocument Document,
    int LayerIndex,
    TerrainMapResolver Resolver,
    TerrainRuntimeSet SelectedTerrain,
    TerrainEditMode Mode,
    TerrainCumulativeIntent CumulativeIntent,
    TerrainDisplacedOwners DisplacedOwners,
    IReadOnlyList<int> NewlyVisitedIndices);

internal sealed class TerrainStrokeResolution
{
    internal bool Succeeded { get; }
    internal TerrainMapPatch? Patch { get; }
    internal IReadOnlyList<TerrainCellIntent> IntentAdditions { get; }
    internal IReadOnlyList<TerrainDisplacedOwner> DisplacedOwnerAdditions { get; }
    internal int InspectedCellCount { get; }
    internal TerrainEditFailure? Failure { get; }

    internal static TerrainStrokeResolution Success(
        TerrainMapPatch patch,
        IEnumerable<TerrainCellIntent> intentAdditions,
        IEnumerable<TerrainDisplacedOwner> displacedOwnerAdditions,
        int inspectedCellCount);

    internal static TerrainStrokeResolution Failed(
        TerrainEditFailure failure,
        int inspectedCellCount);
}

internal static class TerrainStrokeResolver
{
    internal static TerrainStrokeResolution Resolve(
        in TerrainStrokeResolveRequest request);
}
```

`NewlyVisitedIndices` is unique and numeric ascending. A success contains a nonnull immutable patch, row-major unique patch cells, row-major additions, and no failure. Patch cells include only concrete targets different from the currently previewed layer. A failure contains no patch and no additions, plus the first failure in `(CellIndex, terrain ID ordinal)` order. `Resolve` never mutates the document or cumulative state. `InspectedCellCount` counts distinct owner/cell work items and supports the bounded-work regression; it is not public telemetry.

`TerrainCumulativeIntent` records every effective direct ownership decision for the whole gesture: paint records selected ownership even when the cell already belongs to it; effective erase records no owner. `TerrainDisplacedOwners` records the first different enabled owner replaced by paint and retains it until complete/cancel/failure. Neither is rebuilt or discarded after each successful continuation. Only interpolated-point staging, owner work sets, masks, and the returned patch are per-call.

The accumulator contract is:

```csharp
internal sealed class MapLayerChangeAccumulator
{
    internal int Count { get; }
    internal bool HasNetChanges { get; }
    internal bool Apply(
        MapDocument document,
        int layerIndex,
        TerrainMapPatch patch);
    internal void Restore(MapDocument document, int layerIndex);
    internal MapEditChangeBuffer<MapLayerChange> BuildChanges(int layerIndex);
}
```

`Apply` preflights the complete row-major unique/in-bounds patch and allocates all first-value entries before the first `SetLayer`. It records each coordinate's first pre-gesture value once and latest target thereafter, applies the whole patch, and returns whether concrete bytes changed in that call. `Restore` writes every first value back without history. `HasNetChanges` compares first with latest values. `BuildChanges` returns a new row-major buffer containing exactly one delta per net coordinate and does not mutate the document/accumulator. Empty/net-zero completion does not call `PushLayerCommand`.

A separate `TerrainMapEditStroke` owns resolver/set/mode/layer, a `StrokeVisitBitmap`, `TerrainCumulativeIntent`, `TerrainDisplacedOwners`, `MapLayerChangeAccumulator`, previous sample, and cumulative inspected-work count. Its session-facing surface is:

```csharp
internal int LayerIndex { get; }
internal MapCoordinate PreviousSample { get; }
internal StrokeVisitBitmap Visited { get; }
internal int IntentCount { get; }
internal int DisplacedOwnerCount { get; }
internal int AccumulatedCellCount { get; }
internal long TotalInspectedCellCount { get; }
internal bool HasNetChanges { get; }
internal TerrainStrokeUpdate ApplyFirstSample(int x, int y, MapDocument document);
internal TerrainStrokeUpdate ApplySegment(int x, int y, MapDocument document);
internal void Restore(MapDocument document);
internal MapEditChangeBuffer<MapLayerChange> BuildChanges();
internal void Release();
```

`ApplySegment` uses its own `PreviousSample`; it publishes visited/intent/displaced state and advances that sample only after a successful patch apply. A failure leaves the current gesture state available for `Restore`, after which the session clears/releases it. `BuildChanges` delegates to the accumulator with the captured layer.

Keep the existing `MapEditStroke` as the manual-only type and keep its internal test surface intact. `MapEditSession` owns separate nullable manual and terrain fields, exposes `internal TerrainMapEditStroke? ActiveTerrainStroke` for Core tests, and enforces that at most one is nonnull; this avoids a boolean mode flag and prevents either continuation API from reaching the wrong state machine.

### Successful begin publication sequence

Run synchronously with no callbacks:

1. Validate resolver, terrain ID, mode, coordinates, enabled lookup, and no active stroke in the public order above.
2. Capture `TopLayer`; construct an unpublished `TerrainMapEditStroke` with the resolved runtime handle.
3. Enumerate the one-point first segment, stage its unvisited index, and call `TerrainStrokeResolver.Resolve` against cumulative intent plus staged additions.
4. On failure, release the unpublished stroke and return failure; the map/history/session remain exact.
5. On success, accumulator-preflight then apply the complete patch; publish successful visited/intent/displaced additions and advance the sample.
6. Assign the fully initialized stroke to the session's terrain field last.
7. Return success. This assignment also occurs for an empty patch, so no-op begin is active while `Changed == false`.

No event is raised during preview. Ordinary fatal runtime failures such as process termination or `OutOfMemoryException` are outside transactional guarantees; represented catalog/input failures are handled before or through nonthrowing result paths.

## Local resolution, repair, and bounded-work algorithm

For each call, interpolate from the captured previous sample with existing `GridLine.Enumerate`, remove indices already in the gesture bitmap and duplicate indices in this segment, then sort the new indices. Resolve all new points together so their final intended ownership, not interpolation order, drives masks.

Direct behavior:

- Paint first captures the coordinate's pre-addition conceptual owner, then stages selected ownership. If that captured owner is a different enabled terrain, stage it as the first displaced owner.
- Erase stages empty only when the current conceptual owner is selected. Erasing another terrain, raw unowned graphic, `(0,0)`, or `(sheet,0)` is an exact no-op and creates no owner work set.
- A painted cell already owned by the selected terrain is still an effective direct paint cell, so it and selected neighbors are canonicalized even if ownership itself did not change.

Build owner-scoped work sets, not one untyped bounding box:

- selected paint: direct cell plus the selected terrain's stencil;
- selected erase: direct cell plus the erased selected terrain's stencil;
- paint over owner B: additionally direct cell plus B's stencil;
- four-way stencil: N/E/S/W; eight-way stencil: all eight neighbors;
- clip each coordinate independently to document bounds.

For each `(owner, cell)` work item, resolve the cell only when its final conceptual owner is that owner. Therefore a nearby unrelated terrain is not recanonicalized merely because it lies in another owner's geometric stencil. Resolve a direct effective erase to `(0,0)`. If owner work sets overlap, unambiguous enabled membership means each final member has at most one matching owner.

Conceptual ownership lookup order is staged additions → cumulative intent → current layer owner. Current preview variant changes preserve ownership, and painted-over coordinates are overridden by intent. For each affected final member, inspect only its owner's topology offsets, treat out-of-bounds/graphic-zero/unowned/other-terrain cells as absent, normalize through `TerrainMasks.Normalize`, and resolve a variant.

This is sufficient without full-stroke re-resolution: changing ownership at cell `c` can change only `c` and members whose topology stencil contains `c`. Rewriting a variant changes no ownership, so it cannot propagate mask changes beyond that union. Cells outside every newly effective owner's stencil have exactly the same conceptual occupancy at every offset as after the prior successful preview; their prior concrete value remains valid. Pre-existing noncanonical raw values outside the design's affected region intentionally remain untouched.

With at most selected and one displaced owner per new paint cell, a call inspects at most 18 owner/cell work items per newly effective cell (at most 9 for erase or paint without displacement), before deduplication. Total resolution is `O(V)` owner/cell work plus `O(1)` owner/mask lookups per item over `V` effective direct cells, not `O(V²)`. Persistent storage is one tile bitmap plus `O(V)` intent/displaced entries and `O(P)` accumulator entries for distinct previewed cells; affected sets/patches are call-local.

### Exact repair fixtures

Tests must pin these geometries, not only broad final counts:

- Eight-way displaced diagonal: for B at `(1,1)`, B also occupies N `(1,0)`, E `(2,1)`, and NE `(2,0)`; painting A at `(2,0)` changes B `(1,1)` from normalized `N|E|NE` (`0x13`) to `N|E` (`0x03`) while retaining N/E.
- Erase selected A beside an intentionally noncanonical B leaves B's exact raw value unchanged.
- Erasing raw unowned, canonical empty, and noncanonical graphic-zero cells beside intentionally noncanonical A is a no-op and does not canonicalize A.
- Painting over a raw unowned nonzero graphic overwrites it with selected terrain and repairs selected neighbors, with no displaced-terrain work.
- Mirror A-over-B and B-over-A fixtures prove each terrain sees the other only as nonmember; no pair-specific transition lookup exists.
- A 1×1 map paints with mask zero, erases to `(0,0)`, and clips every neighbor safely.
- Corner/edge fixtures prove out-of-bounds neighbors are absent and clipped without throwing.

## Deterministic hash contract and fixed vectors

Normalize first. Hash UTF-8 bytes for:

```text
<terrainId>\n<x invariant signed decimal>\n<y invariant signed decimal>\n<normalizedMask invariant decimal>
```

There is no trailing LF. Read digest bytes 0-7 as unsigned UInt64 big-endian and select `value % variants.Count`. Never use `GetHashCode`, platform endianness, current culture, display name, catalog order, or member/mask order. Variant-list order is intentionally significant.

Externally calculable vectors use members `(3,41),(3,42)` and therefore ID `terrain-4-06a9f0d53153180e867606aef016cbdc5bb62ccb75aebfb7960bcc42d0c0db01`:

| Coordinates/raw mask | Normalized hash text suffix | SHA-256 digest | UInt64 prefix | Index for 2 variants |
|---|---|---|---:|---:|
| `(0,0)`, `0` | `0\n0\n0` | `be7ed729f90ad5bf63eabe775a8f53124a955c578b9cc201849f002beb332b17` | `13726645289543718335` | `1` |
| `(17,29)`, four-way raw `0x15` | `17\n29\n5` | `7fe370c9840ec4f35511d2b85aa27bc3cb3814e880de29ab1fc74eab19be33dc` | `9215333273336464627` | `1` |
| `(-7,11)`, four-way raw `0xFF` | `-7\n11\n15` | `6cee359acc546225fe4edf5648cb10e1a3ab251c749f2da6381a2b54114a51fd` | `7849270139522671141` | `1` |

An eight-way vector uses the same members, ID `terrain-8-accdd79bb38de5a2e8c4a23e8551083c730208103f3af79b151469ce248cb36a`, coordinates `(17,29)`, and raw/normalized mask `0x13`/`19`: digest `85630f849fc9c9075ba222ecc63474c053df35983c4060af52980ad78c0b8488`, UInt64 `9611543092029671687`, index `1` for two variants.

The negative-coordinate vector proves signed invariant formatting at the internal resolver layer; `MapEditSession` still rejects it publicly. Run these under a non-default culture such as `ar-SA`, restore culture in `finally`, and also iterate every required 4/8 mask plus all 256 raw masks for both topology normalization paths.

## Invariant-to-test matrix

| Invariant | Primary proof |
|---|---|
| Resolver publishes only fully indexed enabled runtime data | constructor defect matrix and pending/disabled isolation tests |
| Missing/empty is the only runtime-validation carve-out | constructor theory plus Part 3 full-validation statement/test |
| All required/raw masks and hash bytes are exact | exhaustive resolver tests and fixed vectors under `ar-SA` |
| Repair is owner/topology-local and terrain-pair-neutral | exact stencil fixtures, including displaced diagonal and mirrored overwrite |
| Cumulative intent/displaced owners survive the entire gesture | multi-continuation overwrite test and internal state assertions |
| Resolution work is linear/local, not cumulative-quadratic | long serpentine inspected-work/storage bound |
| Entry points cannot cross stroke kinds | complete manual/terrain lifecycle matrix |
| Invalid/failing updates preserve or roll back exact state as specified | invalid-coordinate and missing-mask session tests |
| One gesture is one coalesced ordinary map command | history/buffer uniqueness, exact replay, and timeline-compatible event count |
| Dirty/savepoint/cap/redo semantics remain generic | terrain history transition tests and existing full Core suite |
| `.map` format is unchanged | terrain edit encode/decode length/layer test and no codec production change |

## Task 0: Build the shared fixture and immutable runtime resolver

**Files:**
- Create: `src/MapEditor.Core/Terrain/TerrainMapResolver.cs`
- Create: `src/MapEditor.Core/Terrain/TerrainEditResult.cs`
- Create: `tests/MapEditor.Core.Tests/Terrain/TerrainCatalogFixture.cs`
- Create: `tests/MapEditor.Core.Tests/Terrain/TerrainMapResolverTests.cs`

**Mutation impact:**
- Source of truth changed: none; Part 1 catalog values remain immutable inputs.
- Important readers: `TerrainStrokeResolver` in Task 1 and session strokes in Task 3.
- Derived/cached state affected: enabled ID, member-owner, topology, and ordered mask-variant indexes, all invocation-local to one resolver.
- Required propagation sequence: select exact enabled status → validate enabled-only projection → tolerate only missing/empty issues → build/copy all indexes → assign complete fields.
- Invariants to preserve: pending/disabled data is inert; enabled membership is unambiguous; no runtime-derived throw after construction; variant order is preserved.
- Observable proof required: assert public exceptions and internal lookup/result values, not private dictionary implementation.

**Step 1: Write failing tests**

`TerrainCatalogFixture` must create Part 1-valid settings, `sha256:` fingerprint, metrics, diagnostics, generated IDs through `TerrainGeneratedId.Create`, `TerrainMemberDefinition` values with provenance, and complete mappings from `TerrainMasks.Required`. It exposes focused factories for one/multiple ordered variants and deliberate defect overrides; tests must not hand-copy 16/47 masks.

Add:

- `Constructor_NullCatalogThrowsExactArgumentNullException`
- `Constructor_IndexesOnlyEnabledSetsWithOrdinalIds`
- `Constructor_PendingDisabledAndTheirOverlapsOrDefectsDoNotParticipate`
- theory `Constructor_EachEnabledRuntimeDefectThrowsFirstExactIssue` covering invalid topology/stable ID/status/display/metric/diagnostic, empty/zero/out-of-range/duplicate/provenance members, duplicate/unreachable masks, duplicate/not-member/graphic-zero variants, unused member, duplicate enabled ID, and cross-enabled membership;
- `Constructor_MissingOrEmptyRequiredMaskIsTheOnlyAcceptedEnabledIssue`
- `Constructor_NoEnabledSetsBuildsEmptyResolver`
- `Constructor_CatalogMetadataDoesNotAffectRuntimeIndex`
- `OwnerLookup_GraphicZeroNeverHasOwner`
- `ResolveVariant_ResolvesAll16And47RequiredMasks`
- `ResolveVariant_All256RawMasksUsePartOneNormalization`
- `ResolveVariant_FixedSha256VectorsMatchUnderNonDefaultCulture`
- `ResolveVariant_IsStableAcrossInstancesAndMultipleCoordinates`
- `ResolveVariant_VariantOrderIsSignificant`
- `ResolveVariant_DisplayNameCatalogOrderAndReviewOnlyDataAreIrrelevant`
- `ResolveVariant_MissingOrEmptyMappingReturnsExactFailureWithoutThrowing`.

**Step 2: Run red**

```bash
dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj \
  --filter 'FullyQualifiedName~TerrainMapResolverTests' -v minimal
```

Expected: compile failure because Part 2 resolver/result types do not exist.

**Step 3: Implement the minimal resolver**

Implement exactly the public/internal contracts, constructor validation matrix, immutable publication boundary, ordinal comparers, Part 1 normalization, invariant UTF-8 formatting, SHA-256, and explicit big-endian read. Do not validate manifest frames or mutate review status. Do not add file, Rendering, App, or disposal ownership.

**Step 4: Run green**

Run the same command. Expected: PASS.

| Invariant | Proved by |
|---|---|
| Enabled runtime defects cannot escape construction | exact defect theory |
| Review-only data cannot own/conflict | pending/disabled isolation |
| Only missing/empty can become result failures | carve-out and lookup tests |
| Masks/hash/culture/order are exact | exhaustive and fixed-vector tests |
| Fixture matches revised Part 1 records | all fixture-created catalogs pass Part 1 validation unless a test names its defect |

**Step 5: Commit**

```bash
git add src/MapEditor.Core/Terrain/TerrainMapResolver.cs \
  src/MapEditor.Core/Terrain/TerrainEditResult.cs \
  tests/MapEditor.Core.Tests/Terrain/TerrainCatalogFixture.cs \
  tests/MapEditor.Core.Tests/Terrain/TerrainMapResolverTests.cs
git commit -m "feat: add validated terrain map resolver"
```

## Task 1: Resolve owner-scoped local terrain patches without mutation

**Files:**
- Create: `src/MapEditor.Core/Terrain/TerrainStrokeResolver.cs`
- Create: `tests/MapEditor.Core.Tests/Terrain/TerrainStrokeResolverTests.cs`

**Mutation impact:**
- Source of truth changed: none; current preview layer plus cumulative intent is read-only during resolution.
- Important readers: Task 3's terrain stroke applies a successful patch and publishes additions.
- Derived/cached state affected: only per-call interpolated additions, owner work sets, masks, and immutable result; cumulative intent/displaced owners remain gesture-owned inputs.
- Required propagation sequence: derive staged direct intents/first displaced owners → build owner-specific clipped stencils → compute final conceptual ownership → normalize/resolve in deterministic order → return complete patch/additions or failure with no patch.
- Invariants to preserve: no document/state mutation; no unrelated recanonicalization; pair-neutral membership; no full-stroke scan; first displaced ownership is retained, not recomputed from later preview graphics.
- Observable proof required: apply successful patch cells in the test only after resolution; compare exact grids and prove failure leaves encoded bytes unchanged.

**Step 1: Write failing tests**

Add:

- `ResolvePaint_SelectedFourWayCellAndNeighborsUseFinalIntent`
- `ResolvePaint_EightWayCanonicalizesSupportedDiagonals`
- `ResolvePaint_DisplacedEightWayDiagonalUsesOwnerStencilUnion`
- `ResolveErase_SelectedWritesCanonicalEmptyAndRepairsOnlySelected`
- `ResolveErase_AdjacentOtherTerrainRemainsExactRawValue`
- theory `ResolveErase_UnownedCanonicalEmptyOrGraphicZeroDoesNotRecanonicalizeAdjacentSelected`
- `ResolveErase_CellOwnedByOtherTerrainIsExactNoOp`
- `ResolvePaint_RawUnownedGraphicIsOverwrittenWithoutDisplacedOwner`
- `ResolvePaint_AOverBAndBOverAAreSymmetricMemberVsNonmember`
- `ResolvePaint_ExistingSelectedMemberStillCanonicalizesSelectedLocalRegion`
- `Resolve_OneByOneMapUsesMaskZeroAndClipsAllNeighbors`
- `Resolve_MapEdgesClipOwnerStencilsAndTreatOutsideAbsent`
- `Resolve_MultipleNewCellsUseFinalCombinedIntentIndependentOfInputOrder`
- `Resolve_CumulativeIntentAndFirstDisplacedOwnerPersistAcrossCalls`
- adversarial `Resolve_MissingSelectedOrDisplacedMaskReturnsNoPatchOrAdditions`
- `Resolve_UnchangedOwnershipOutsideLocalStencilsIsNotInspectedOrRewritten`.

The missing-mask test must encounter at least one valid candidate before the deterministically later invalid candidate. Assert unchanged `MapCodec.Encode` bytes to prove this local resolver is mutation-free; explicitly label it as local contract proof, not the session rollback proof.

**Step 2: Run red**

```bash
dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj \
  --filter 'FullyQualifiedName~TerrainStrokeResolverTests' -v minimal
```

Expected: compile failure because terrain stroke resolution types do not exist.

**Step 3: Implement staged local resolution**

Implement the locked request/result/intent/displaced/patch types and local algorithm. Copy result collections. Use row-major indices, owner-specific sets, exact topology offsets, and one final conceptual overlay. Never call `SetLayer`, publish cumulative additions, retain call-local sets, use a terrain-pair key, or scan prior intent to resolve unchanged cells.

**Step 4: Run green**

Run the same command. Expected: PASS.

| Invariant | Proved by |
|---|---|
| Selected and displaced masks use their own topologies | four/eight/displaced fixtures |
| No-op erases do not canonicalize neighbors | three-value erase theory |
| Terrain boundaries have no pair transition behavior | mirrored A/B fixture |
| Boundaries and 1×1 are safe mask-zero cases | edge/1×1 tests |
| Resolver failure returns no patch and mutates nothing | adversarial missing-mask test |
| Only local occupancy changes are inspected | outside-stencil test and inspected count |

**Step 5: Commit**

```bash
git add src/MapEditor.Core/Terrain/TerrainStrokeResolver.cs \
  tests/MapEditor.Core.Tests/Terrain/TerrainStrokeResolverTests.cs
git commit -m "feat: resolve local terrain patches"
```

## Task 2: Add a first-before/latest-after preview accumulator

**Files:**
- Create: `src/MapEditor.Core/Editing/MapLayerChangeAccumulator.cs`
- Create: `tests/MapEditor.Core.Tests/Terrain/MapLayerChangeAccumulatorTests.cs`

**Mutation impact:**
- Source of truth changed: one captured document layer during successful `Apply`; first observed values become rollback/undo truth and latest targets become preview/redo truth.
- Important readers: Task 3 dirty state, cancellation/failure restore, completion, command replay, and history accounting.
- Derived/cached state affected: one entry per distinct previewed coordinate; no command/history state before completion.
- Required propagation sequence: validate/materialize full patch → capture all unseen first values → apply every target → expose net state → restore or build one row-major change buffer.
- Invariants to preserve: no partial apply for represented invalid patch input; repeated preview churn never loses the first value; net-zero coordinates do not enter history; restore/build do not publish history.
- Observable proof required: compare layer grids and exact built `Before`/`After`, including apply-away/apply-back.

**Step 1: Write failing tests**

Add:

- `Apply_CompletePatchRecordsFirstAndLatestValuesInRowMajorOrder`
- `Apply_RepeatedCoordinateRetainsFirstBeforeAndLatestAfter`
- `Apply_ReturnsConcretePerCallChangeWhileHasNetChangesTracksGestureNet`
- `Apply_InvalidDuplicateOrOutOfBoundsPatchThrowsBeforeAnyWrite`
- `Restore_ReinstatesEveryFirstValueWithoutTouchingFlagsOrOtherLayers`
- `BuildChanges_EmitsOneNetDeltaPerCoordinateAndDoesNotMutateState`
- adversarial `BuildChanges_ApplyAwayThenBackOmitsNetZeroAndPreservesRedoEligibility`.

**Step 2: Run red**

```bash
dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj \
  --filter 'FullyQualifiedName~MapLayerChangeAccumulatorTests' -v minimal
```

Expected: compile failure because the accumulator does not exist.

**Step 3: Implement the accumulator contract**

Keep storage bounded by distinct touched coordinates. Materialize/preflight before writes, preserve value-type copies, restore originals directly, and build the existing segmented `MapEditChangeBuffer<MapLayerChange>`. Do not push history or know terrain IDs/masks.

**Step 4: Run green**

Run the same command. Expected: PASS.

| Invariant | Proved by |
|---|---|
| First/latest values survive preview churn | repeated/apply-back tests |
| Invalid patch cannot partially apply | adversarial preflight test |
| Rollback is exact and layer-local | restore test |
| Completion buffer is unique, row-major, and net-only | build test |

**Step 5: Commit**

```bash
git add src/MapEditor.Core/Editing/MapLayerChangeAccumulator.cs \
  tests/MapEditor.Core.Tests/Terrain/MapLayerChangeAccumulatorTests.cs
git commit -m "feat: accumulate terrain preview changes"
```

## Task 3: Integrate isolated terrain strokes, history, replay, and bounded storage

**Files:**
- Create: `src/MapEditor.Core/Editing/TerrainMapEditStroke.cs`
- Modify: `src/MapEditor.Core/Editing/MapEditTool.cs:1-11`
- Modify: `src/MapEditor.Core/Editing/MapEditSession.cs:10-150,153-154,216-217,270-271,332-333,381-511`
- Create: `tests/MapEditor.Core.Tests/Terrain/TerrainMapEditSessionTests.cs`
- Create: `tests/MapEditor.Core.Tests/Terrain/TerrainMapEditHistoryTests.cs`
- Create: `tests/MapEditor.Core.Tests/Terrain/TerrainMapEditStorageTests.cs`
- Create: `tests/MapEditor.Core.Tests/Terrain/TerrainMapFormatTests.cs`
- Regression: `tests/MapEditor.Core.Tests/MapEditSessionTests.cs`
- Regression: `tests/MapEditor.Core.Tests/MapEditStrokeTests.cs`
- Regression: `tests/MapEditor.Core.Tests/MapEditStrokeStorageTests.cs`
- Regression: `tests/MapEditor.Core.Tests/MapEditHistoryTests.cs`
- Regression: `tests/MapEditor.Core.Tests/MapEditPersistenceTests.cs`
- Regression: `tests/MapEditor.Core.Tests/MapCodecTests.cs`

**Mutation impact:**
- Source of truth changed: the captured document layer receives previews; terrain stroke cumulative intent/displaced owners govern conceptual ownership; accumulator first/latest values govern rollback and command construction. `MapEditTool` gains a Core enum member.
- Important readers: `Document`, `HasActiveStroke`, `CanUndo`, `CanRedo`, `IsDirty`, every operation guarded against active strokes, shared complete/cancel, history stacks/version/events/cap, state IDs/savepoint, `MapCodec`, later canvas rendering, App tool validation/routing, and `DocumentEditTimeline`.
- Derived/cached state affected: separate active terrain stroke, visited bitmap, intent/displaced dictionaries, accumulator, previous sample, and cumulative inspected count. Existing manual stroke internals/history remain unchanged.
- Required propagation sequence:
  1. Validate and resolve all begin inputs before constructing unpublished stroke state.
  2. Resolve/apply the first update and publish the active stroke last, including no-op begin.
  3. On each continuation, validate kind/coordinates, stage only new interpolated cells, resolve/apply atomically, then publish cumulative additions and advance sample.
  4. On resolution failure, restore accumulator originals, release/clear terrain state, and return failure without history.
  5. On complete, build one net buffer, clear/release active state, then call existing `PushLayerCommand` once; on cancel, restore and clear without push.
  6. Make every existing active-stroke guard use either manual or terrain state.
- Invariants to preserve: no simultaneous strokes; manual behavior/storage is byte-for-byte unchanged; no invalid call advances a sample; captured resolver/ID/mode/layer cannot drift; history publication happens only once at effective complete.
- Observable proof required: assert encoded bytes, exact previews/final grids, active kind/sample/state, intent/displaced counts, history stacks/version/bytes/events, state IDs/savepoint/dirty, command entries, and encode/decode output.

**Step 1: Write all failing integration tests before production changes**

In `TerrainMapEditSessionTests` add:

- `BeginTerrainStroke_CapturesResolverTerrainModeAndTopLayerForWholeGesture`
- `TerrainPaint_InterpolatesSparseSamplesAndPreviewsLocalRepairs`
- `TerrainErase_InterpolatesAndOnlyErasesSelectedMembers`
- `TerrainStroke_OverlappingSegmentsVisitDirectCellsOnce`
- `TerrainStroke_ChangingSelectedLayerAfterBeginStillUsesCapturedLayer`
- `TerrainStroke_ChangesOnlyCapturedLayerAndPreservesFlagsAndOtherLayers`
- `TerrainStroke_ChangingCallerSelectionIdOrPaintEraseModeAfterBeginCannotAffectContinuation`
- `TerrainStroke_NoOpBeginPublishesActiveStrokeWithFalseChanged`
- `TerrainStroke_OneByOneAndMapEdgeBeginUseMaskZeroAndClipNeighbors`
- `TerrainStroke_BeginOutOfBoundsUnknownNonEnabledNullOrBlankIdNullResolverOrInvalidModeThrowsWithoutMutation`
- `BeginStroke_TerrainRequiresDedicatedApi`
- theory `ManualAndTerrainBeginApisRejectWhileEitherStrokeKindIsActive`
- `ContinueStroke_WhileTerrainActiveThrowsWithoutPreviewOrSampleChange`
- `ContinueTerrainStroke_WhileManualActiveThrowsWithoutManualMutationOrSampleChange`
- adversarial `ContinueTerrainStroke_OutOfBoundsPreservesExactPreviewActiveStatePriorSampleHistoryAndIds`
- `ContinueTerrainStroke_MissingMaskRestoresPreBeginBytesClearsStrokeAndReturnsExactFailure`
- `BeginTerrainStroke_MissingMaskReturnsFailureWithoutPublication`
- `CompleteAndCancelDispatchBothKindsAndWithoutActiveStillThrow`
- `TerrainCancel_RestoresExactBytesAndPriorDirtyRedoState`.

The invalid-continuation regression must snapshot `MapCodec.Encode`, `HasActiveStroke`, internal previous sample, intent/displaced/accumulator counts, `HistoryVersion`, retained bytes, undo/redo counts, current/saved IDs, and `IsDirty`, then assert every value is exact after both negative and high coordinates. The missing-continuation test is the actual stroke rollback/atomicity proof; Task 1's no-patch test proves only local resolver behavior.

In `TerrainMapEditHistoryTests` add before implementation:

- `TerrainComplete_CreatesExactlyOneOrdinaryLayerCommand`
- `TerrainUndo_RestoresSelectedDisplacedAndRawOriginalValues`
- `TerrainRedo_ReplaysExactPreviouslyChosenFinalVariants`
- `TerrainCommand_SavepointDirtyTransitionsMatchOrdinaryLayerCommands`
- `TerrainCommand_HistoryVersionAndEventAdvanceOnceOnCompleteUndoRedo`
- `TerrainCommand_RepeatedNeighborPreviewsStoreOneRowMajorDeltaPerCoordinate`
- `TerrainCommand_AccountingUsesFinalCoalescedBufferCapacity`
- `TerrainCommand_OverCapRemainsAppliedDirtyAndNotUndoable`
- `FailedCanceledAndNetNoOpTerrainStrokesPreserveRedoBytesVersionAndStateIds`.

In `TerrainMapEditStorageTests` add:

- `LongEightWaySerpentine_UsesLinearLocalResolutionWorkAndBoundedPersistentStorage`;
- `TerrainStroke_DisplacedOwnerAndIntentEntriesPersistUntilGestureEnds`;
- `TerrainComplete_TransfersOnlyBuiltCoalescedBufferAndReleasesPreviewState`;
- `TerrainCancel_ReleasesAllTerrainStateAndRetainsNoCommand`.

Use at least a 200×200 one-cell-at-a-time serpentine over empty cells. Assert selected-only `TotalInspectedCellCount <= 9 * visitedCount`, intent count equals visited paint cells, displaced count is zero, accumulator distinct count is at most map tile count, bitmap words are exactly `ceil(tileCount/64)`, and no patch/work-set collection is retained. This bound fails a whole-cumulative-intent scan even if final pixels are correct.

In `TerrainMapFormatTests` add `TerrainEdit_EncodeDecodeUsesUnchangedHeader34ByteTilesAndOrdinaryLayerReferences`: complete a repaired terrain edit, assert length `MapCodec.HeaderSize + MapCodec.BytesPerTile * TileCount`, decode, and compare all flags/five layers. No terrain ID, topology, mode, or sidecar is serialized.

**Step 2: Run red**

```bash
dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj \
  --filter 'FullyQualifiedName~TerrainMapEditSessionTests|FullyQualifiedName~TerrainMapEditHistoryTests|FullyQualifiedName~TerrainMapEditStorageTests|FullyQualifiedName~TerrainMapFormatTests' \
  -v minimal
```

Expected: compile failure because session terrain APIs, `TerrainMapEditStroke`, and `MapEditTool.Terrain` do not exist. This red phase includes history/storage/map-format expectations before the production integration that satisfies them; there is no later green-only test task.

**Step 3: Implement the isolated lifecycle**

Append `Terrain` to `MapEditTool`. Keep existing `MapEditStroke` manual-only. Add a distinct terrain field/type and central `HasAnyActiveStroke` guard in `MapEditSession`; update every existing guard/read path listed in mutation impact, not only begin/continue. Preserve manual API validation with explicit `Pencil`, `Eraser`, and `Eyedropper` cases.

Implement successful-begin publication order, continuation result/rollback, captured values, cumulative state lifetime, accumulator use, and shared completion/cancellation exactly as locked above. Clear active state before calling `PushLayerCommand`, matching current completion event observability. Do not change `MapEditHistory`, `MapEditCommand`, `MapEditChangeBuffer`, `MapCodec`, or App production code unless an existing generic defect is independently demonstrated; terrain must fit those contracts.

**Step 4: Run focused green and manual regressions**

```bash
dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj \
  --filter 'FullyQualifiedName~TerrainMapEdit|FullyQualifiedName~MapEditSessionTests|FullyQualifiedName~MapEditStrokeTests|FullyQualifiedName~MapEditStrokeStorageTests|FullyQualifiedName~MapEditHistoryTests|FullyQualifiedName~MapEditPersistenceTests|FullyQualifiedName~MapCodecTests' \
  -v minimal
```

Expected: PASS, including all new and existing tests.

| Invariant | Proved by |
|---|---|
| Entry points and begin publication cannot overlap/cross | lifecycle matrix and no-op begin |
| Invalid continuation preserves every active/history identity | adversarial snapshot test |
| Missing continuation rolls back the whole gesture | exact encoded-byte failure test |
| Resolver/ID/mode/layer stay captured | capture/change tests |
| Completion is one exact ordinary command | command/replay/coalescing tests |
| Dirty/savepoint/version/cap/redo remain generic | terrain history tests |
| Work/storage are linear and released | long serpent and release tests |
| Manual behavior remains unchanged | focused existing regression classes |
| Persisted map format is unchanged | terrain map-format and existing codec/persistence tests |

**Step 5: Commit**

```bash
git add src/MapEditor.Core/Editing/TerrainMapEditStroke.cs \
  src/MapEditor.Core/Editing/MapEditTool.cs \
  src/MapEditor.Core/Editing/MapEditSession.cs \
  tests/MapEditor.Core.Tests/Terrain/TerrainMapEditSessionTests.cs \
  tests/MapEditor.Core.Tests/Terrain/TerrainMapEditHistoryTests.cs \
  tests/MapEditor.Core.Tests/Terrain/TerrainMapEditStorageTests.cs \
  tests/MapEditor.Core.Tests/Terrain/TerrainMapFormatTests.cs
git commit -m "feat: add atomic terrain edit strokes"
```

## Final verification and red-team review

Run:

```bash
dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj -v minimal
dotnet build Goose2ClientGodot.sln -v minimal
git diff --check
git status --short
```

Expected: Core tests and full solution build pass. `git status` is **not** expected to be clean: this planning worktree already had uncommitted contract repairs in all three terrain plan files before implementation. Compare status/diff against that baseline; do not discard, stage accidentally, or describe Part 1/Part 3 plan changes as Part 2 production output. No generated `Assets/` file may appear.

Red-team checklist:

- Re-run every resolver constructor theory and verify pending/disabled overlap cannot enter owner lookup.
- Confirm Part 3 full validation rejects missing/empty before runtime publication; only direct Core fault fixtures reach the carve-out.
- Confirm every resolver path after begin is result-based and no catalog dictionary index or invalid topology call can throw.
- Check all 16/47 required masks, all 256 raw masks, fixed digests, non-default culture, ordinal IDs, and variant-order significance.
- Inspect exact selected/displaced owner-scoped work sets; no untyped radius may recanonicalize unrelated terrain.
- Verify graphic zero, raw unowned graphics, map boundaries, 1×1, A↔B overwrite, and erase no-op fixtures.
- Verify cumulative intent and first displaced owners survive until complete/cancel/failure while all affected/mask/patch sets are call-local.
- Inspect the serpentine counter bound to exclude cumulative full-stroke re-resolution.
- Search every `_stroke` guard in `MapEditSession`; manual and terrain active states must both block undo/redo/save/cap/patch/fill/resize and both begin APIs.
- Confirm invalid calls do not advance previous samples; missing continuation returns `Changed=false`, restores, clears, and emits no history event.
- Confirm one row-major first/latest delta per coordinate, exact undo/redo, cap eviction, dirty/savepoint transitions, and redo preservation.
- Confirm `MapCodec` and `MapDocument` production files remain unchanged and terrain persists only ordinary `(sheet, graphic)` values.
- Confirm Core has no App tool/catalog replacement state, file access, Rendering/Avalonia dependency, worker, event stream, or disposal lifecycle.

Plan complete and saved to `docs/plans/2026-09-08-terrain-brush-part2-core-editing.md`. Ready to implement.
