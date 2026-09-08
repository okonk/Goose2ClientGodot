# Map Editor Terrain Brush Design

Date: 2026-09-08
Branch: `terrain-brush`
Scope: `tools/AssetConverter`, `src/MapEditor.Core`, `src/MapEditor.Rendering`, `src/MapEditor.App`, and their tests.

## Summary

Add an active-layer terrain brush that automatically selects edge, corner, and center graphics from neighboring terrain membership. The asset converter will infer conservative terrain sets from the existing map corpus and sprite imagery. The editor will load the generated definitions, expose enabled brushes in a terrain palette, and provide a manager for reviewing and correcting generated sets.

The map format remains unchanged. Terrain membership is derived from ordinary `(sheet, graphic)` values, and every terrain edit is stored as ordinary map layer changes.

## Scope

Included:

- hybrid map-adjacency and image-based terrain inference in the asset converter;
- automatic selection between 4-way and canonical 8-way blob topology;
- high-confidence enabled terrain sets and disabled review candidates;
- deterministic coordinate-based visual variants;
- a versioned `Assets/Sprites/terrain-brushes.json` asset;
- Tiles and Terrain palette modes;
- active-layer terrain paint and erase strokes;
- atomic neighbor repair and normal map undo/redo;
- a Terrain Sets manager for renaming, correcting, enabling, disabling, and saving definitions;
- graceful terrain-feature degradation when definitions are absent or invalid.

Excluded:

- explicit transitions between different terrain types;
- map-format terrain metadata or per-map sidecars;
- multi-layer terrain composites;
- runtime or editor-side terrain generation;
- preserving manager reviews across a later converter regeneration;
- closest-mask fallback for incomplete sets;
- inference from oversized sprites or authored layers other than layer 0.

## Architecture and ownership

The converter runs terrain analysis after converted maps, sprite sheets, and `manifest.json` exist. It reads layer-0 map placements, extracts image features from resolved 32×32 graphics, combines those features with observed cardinal and diagonal adjacency, evaluates 4-way and 8-way models against held-out map samples, and emits terrain definitions and diagnostics.

Layer-0 map graphics seed candidate families. The converter also searches resolved 32×32 graphics from the same tile sheets for high-confidence variants absent from the map corpus. Image-only members receive stronger admission thresholds and explicit diagnostics.

`MapEditor.Core` owns provider-neutral terrain definitions, topology validation, mask resolution, deterministic variant selection, and atomic terrain map edits. It has no asset-directory or Avalonia concerns. `MapEditor.Rendering` loads and validates the terrain JSON as part of an asset context. `MapEditor.App` owns per-document terrain selection and mode, terrain pointer gestures, the terrain palette, and the Terrain Sets manager.

Rendering remains terrain-agnostic because resolved terrain cells are ordinary map tiles. Terrain editing operates on the topmost selected layer even though inference is initially trained from layer 0.

## Generated data contract

`Assets/Sprites/terrain-brushes.json` contains:

- schema and generator versions;
- a deterministic corpus fingerprint;
- generation settings and confidence thresholds needed to interpret diagnostics;
- terrain sets with stable generated IDs and editable display names;
- enabled, pending, or disabled review status;
- selected topology and confidence/support metrics;
- mask-to-variant mappings;
- member provenance distinguishing map-observed and image-only graphics;
- candidate diagnostics sufficient for the manager to explain acceptance or deferral.

A stable generated ID is derived from topology and sorted member references. Renaming or changing review status does not change it. A changed inferred family receives a new ID on regeneration.

Every variant is a `(sheet, graphic)` reference. A graphic may belong to at most one enabled terrain set. Disabled and pending candidates may overlap so alternatives remain reviewable.

Four-way sets use all 16 cardinal masks. Eight-way sets use the canonical 47 blob masks: a diagonal bit is retained only when both adjacent cardinal bits are present. An enabled set must provide at least one variant for every reachable mask.

## Inference pipeline

The converter uses a deterministic conservative hybrid pipeline:

1. Decode every converted map and collect layer-0 placements, boundaries, cardinal/diagonal adjacency, map identity, and connected-region context.
2. Resolve map-used 32×32 frames from the manifest and crop their RGBA pixels from generated sheet PNGs.
3. Extract alpha, palette, perceptual, corner, and directional edge features once per graphic.
4. Seed candidate families using sheet locality and visual feature buckets rather than global pairwise comparison.
5. Infer family membership and each member's modal neighbor mask from map observations, weighting maps and connected regions so dominant floors do not overwhelm the corpus.
6. Search same-tile-sheet 32×32 frames for missing family variants. Admit image-only members only at a stricter compatibility threshold.
7. Fit both 4-way and 8-way models. Select 8-way only when diagonal evidence has adequate support and materially improves deterministic held-out reconstruction.
8. Score support, mask entropy, completeness, ambiguity, visual compatibility, and held-out reconstruction.
9. Enable only complete high-confidence sets. Emit uncertain or incomplete alternatives as pending candidates for review.

The focused `terrain` converter command and the `all` pipeline call the same implementation and produce byte-identical output for the same inputs. Feature extraction is cached, candidate comparisons are bucketed, ordering and holdout selection are deterministic, and output is built fully before a same-directory atomic replacement. Failure preserves the prior file.

## Editor workflow

The left asset panel gains Tiles and Terrain tabs. Tiles preserves the current sheet/graphic palette. Terrain lists enabled terrain sets with a representative preview, display name, topology, and warning indicator. Selecting a terrain activates the Terrain tool, with `T` as its shortcut.

Terrain mode is per document and has explicit Paint and Erase choices. Paint adds the selected terrain at visited cells. Erase removes the selected terrain by writing the empty `(0, 0)` tile. Erasing a cell that is not a member of the selected terrain is a no-op.

A pointer drag records visited cells using the existing continuous line interpolation behavior. The editor previews recalculated variants during the gesture. Pointer release commits the complete stroke as one undoable map command; Escape or pointer-capture loss restores the exact prior values and creates no history entry.

Pencil and ordinary Eraser remain manual escape hatches. They continue writing raw graphics and do not repair surrounding terrain automatically.

## Terrain resolution and editing

Membership is derived from the current topmost selected layer. A cell belongs to a terrain when its `(sheet, graphic)` reference appears in that enabled set.

For each effective stroke, the resolver computes the final intended membership before committing values. Its affected region includes:

- every painted or erased cell;
- neighboring members of the selected terrain;
- neighboring members of any terrain displaced by painting.

Each affected member's normalized neighbor mask selects its variant list. The chosen list index is a stable hash of terrain ID, map coordinates, and mask. Display-name edits and operation order therefore do not change visual variants.

Resolution is all-or-nothing. If any affected cell has no valid variant, no map value or history state changes and the editor reports the terrain and missing mask. Painting another terrain treats each side only as member or non-member; there are no terrain-pair-specific transition rules.

## Validation and failures

An enabled terrain set is valid only when:

- its topology and IDs are valid;
- every reachable mask has at least one variant;
- every referenced frame exists in the sprite manifest and is 32×32;
- no graphic belongs to another enabled set.

The manager may retain incomplete, conflicting, or unresolved sets only as pending or disabled. Enabling one displays exact validation failures and is rejected until corrected.

Missing or malformed terrain JSON does not block sprite assets or maps. The terrain palette and tool become unavailable with an actionable diagnostic while ordinary editing continues. A manager save validates and builds the replacement, cancels active terrain gestures while failure is still reversible, atomically replaces the file, then publishes the new catalog through a nonthrowing path and refreshes open documents. A failed save preserves both the prior file and current published catalog; an I/O failure after preflight may leave a safely cancelled preview but does not alter map contents or history.

Manager saves modify the generated file directly. A future converter run overwrites those reviews. The manager warns about this limitation; a generated-default plus override merge format is deferred.

## Terrain Sets manager

The manager shows generated and reviewed sets, including pending candidates and their diagnostics. It supports:

- editing display names;
- choosing 4-way or 8-way topology;
- viewing every required mask and its visual neighborhood;
- adding, removing, and reordering graphic variants per mask;
- enabling, disabling, or leaving a candidate pending;
- identifying map-observed versus image-only members;
- locating missing masks, unresolved frames, and cross-set conflicts;
- atomically saving valid definitions to the selected asset directory.

The manager does not run inference or merge regenerated output.

## Testing and validation

### Converter

Use synthetic map/image fixtures to prove 4-way and 8-way reconstruction, topology selection, noisy variants, deceptive lookalikes, sparse corners, image-only missing variants, weighting, confidence thresholds, stable IDs, deterministic output, and atomic-write failure preservation. Tests must not depend on external proprietary source data.

### Core

Cover all 16 cardinal and 47 normalized blob masks, diagonal normalization, coordinate-stable variant selection, active-layer behavior, paint, erase, overwrite, displaced-terrain repair, interpolated strokes, no-ops, incomplete-set atomicity, cancellation, and exact undo/redo.

### Rendering and asset loading

Cover schema validation, frame resolution, duplicate enabled membership, unavailable/corrupt JSON degradation, and asset-context replacement without disturbing the normal sprite cache.

### App

Cover Tiles/Terrain palette switching, terrain selection and shortcut, per-document terrain mode, manager editing and validation, atomic save behavior, active-gesture cancellation on catalog replacement, and real pointer paint/erase strokes.

### Real-corpus smoke validation

Run conversion against the generated asset corpus, review candidate counts and held-out accuracy, inspect pending reasons, and manually paint representative grass, water, path, and shoreline sets. Tune conservative thresholds based on this validation before treating the generated defaults as final.

## Mutation impact matrix

| Mutation | Source of truth | Required propagation | Failure atomicity |
|---|---|---|---|
| Generate terrain catalog | maps, manifest, sheet PNGs | features → candidates → topology/masks → diagnostics → JSON | Build and validate completely, then atomic replace; preserve prior file on failure |
| Save manager changes | manager draft | validate/build → cancel active terrain previews → atomic file write → nonthrowing catalog publication → reconcile documents | Invalid or failed save changes neither file nor published catalog; an I/O failure may leave a preview cancelled with map/history restored |
| Select terrain/mode | document view model | palette/tool checks → gesture configuration → canvas/status refresh | Presentation only; no map/history mutation |
| Paint/erase stroke | intended membership set | affected-region masks → deterministic variants → one map command → canvas/title/commands | Any missing mask/reference or cancelled gesture restores exact prior state and adds no history |
| Replace asset context | asset controller | sprite cache and optional terrain catalog → all palettes/canvases | Bad base manifest preserves old context; bad terrain file publishes usable sprite context with terrain unavailable |

## Invariants

| Invariant | Required proof |
|---|---|
| Map files remain compatible and contain no terrain metadata | codec byte-format tests and no schema change |
| Generator output is deterministic | repeated synthetic generation is byte-identical |
| Auto-enabled sets are complete and non-overlapping | catalog validation and generator acceptance tests |
| Eight-way diagonals obey canonical blob normalization | exhaustive mask tests |
| Painting affects only the topmost selected layer | multi-layer core and canvas tests |
| One gesture creates one normal undo command | stroke and undo/redo tests |
| Failed or cancelled resolution never partially edits a map | missing-mask, invalid-reference, Escape, and capture-loss tests |
| Coordinate variants are independent of operation order and display names | resolver permutation tests |
| Terrain failures never block ordinary sprite/map editing | asset-context and real window tests |
| Manager save cannot publish data not durably written or leave durable data unpublished | injected cancellation/write/replace/observer failure tests |

## Deferred gaps

- Converter regeneration overwrites editor reviews.
- Confidence thresholds require tuning against the real corpus.
- Incomplete terrain sets have no nearest-mask fallback.
- No explicit terrain-to-terrain transition sets.
- No multi-layer terrain composites.
- No inference from oversized graphics or non-ground authored layers.
