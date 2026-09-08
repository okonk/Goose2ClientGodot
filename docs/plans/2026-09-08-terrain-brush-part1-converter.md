# Terrain Brush Part 1: Converter and Catalog Contract Implementation Plan

**Goal:** Add the shared terrain-catalog contract and a deterministic converter pipeline that mines generated maps and 32×32 sprite frames, infers conservative four-way/eight-way terrain candidates, and atomically emits `Assets/Sprites/terrain-brushes.json` from both `terrain` and `all`.

**Architecture:** `MapEditor.Core` owns the provider-neutral schema, mask semantics, stable-ID derivation, structural parsing, canonical serialization, and catalog-level validation. `AssetConverter` references that .NET 8 library, strictly loads the generated map/manifest/PNG corpus, extracts each frame once, builds bucketed hybrid candidates, scores both topologies on deterministic held-out maps, then validates and atomically publishes a complete in-memory catalog. The converter remains synchronous and CLI-owned; no editor asset loading, rendering, painting, manager UI, or map-format change is included.

**Tech Stack:** C#; .NET 8 `MapEditor.Core`; .NET 10 `AssetConverter`; `System.Text.Json`; `System.Security.Cryptography`; SixLabors.ImageSharp 3.1.12; xUnit 2.9.x.

---

> Implement with @executing-plans, one task and commit at a time. Follow `AGENTS.md`: add no comments/doc strings unless a non-obvious invariant genuinely requires one.

## Scope and sequencing

This is Part 1 of 3. It includes only:

- provider-neutral terrain definitions, mask rules, stable IDs, JSON parsing/serialization, and validation needed by conversion and later editor parts;
- strict generated-corpus discovery and layer-0 map mining;
- deterministic 32×32 RGBA feature extraction and caching;
- bucketed map/image candidate inference, weighting, holdout evaluation, topology selection, diagnostics, and conservative review status;
- complete in-memory generation followed by same-directory atomic replacement;
- the focused `terrain [repoRoot]` command and invocation from `all` after the combined manifest is written;
- synthetic tests and an optional local real-corpus smoke that never becomes a proprietary-data test dependency.

It excludes rendering asset-context loading, editor-side frame validation/degradation, terrain resolution and map edits, palette/tool state, gestures, undo/redo, and the Terrain Sets manager. Parts 2 and 3 will consume the exact contract introduced here.

No map schema or database changes are involved. `terrain-brushes.json` is a new, generated, gitignored schema-v1 file; no migration is needed. Regeneration intentionally replaces manager reviews, as approved. Do not commit generated `Assets/` output.

## APIs and facts verified before planning

| API / fact | Citation and consequence |
|---|---|
| Core owns provider-neutral definitions and topology behavior; converter analysis is layer 0 and rendering/editor concerns remain elsewhere | `docs/plans/2026-09-08-terrain-brush-design.md:38-46`. Put the contract in `MapEditor.Core`, not in converter DTOs or Rendering. |
| The generated contract requires versions, fingerprint, settings, IDs/names/status, metrics, masks, provenance, and diagnostics | `docs/plans/2026-09-08-terrain-brush-design.md:48-66`. Lock every field below; do not emit anonymous converter-only JSON. |
| Four-way has 16 masks; canonical eight-way has 47, with diagonals retained only when both adjacent cardinals exist | `docs/plans/2026-09-08-terrain-brush-design.md:64-66`. The shared mask helper and exhaustive tests are prerequisites for inference. |
| Required inference stages and deterministic/atomic publication are explicit | `docs/plans/2026-09-08-terrain-brush-design.md:68-82`. Input ordering, feature cache, buckets, holdout assignment, tie breaks, JSON ordering, and replace behavior must all be pinned. |
| Converter tests must use synthetic maps/images and cover topology, noise, lookalikes, sparse corners, image-only members, weighting, thresholds, IDs, determinism, and atomic failure | `docs/plans/2026-09-08-terrain-brush-design.md:138-143`. Proprietary paths are smoke-only. |
| `MapCodec.Decode(ReadOnlySpan<byte>)` accepts the normal and legacy editor versions, validates dimensions/length, ignores trailers, and returns `MapDocument` | `src/MapEditor.Core/MapCodec.cs:15-69`; supported versions are at `src/MapEditor.Core/MapDocument.cs:72-81`. Use it instead of duplicating the wire parser currently in `TileSheetGenerator`. |
| `MapDocument` exposes row-major and coordinate reads, and each `MapTile` exposes `GetLayer(int)` | `src/MapEditor.Core/MapDocument.cs:169-184` and `src/MapEditor.Core/MapDocument.cs:36-47`. Mining reads only `GetLayer(0)` and never mutates decoded documents. |
| Tests can build valid synthetic map bytes with `MapDocument.Create`, `SetLayer`, and `MapCodec.Encode` | `src/MapEditor.Core/MapDocument.cs:107-120,193-202`; `src/MapEditor.Core/MapCodec.cs:80-120`. Do not hand-write an incompatible editor-version-1 map like the older tile-sheet fixture does at `tools/AssetConverter/tests/AssetConverter.Tests/TileSheetGeneratorTests.cs:110-125`. |
| The frame manifest shape is `{tileSize:32,sheets:{sheet:{graphic:[x,y,w,h]}}}` | `tools/AssetConverter/src/AssetConverter/Manifest/FrameManifestBuilder.cs:7-16,23-37`. The converter input parser must preserve numeric reference/rect semantics and reject duplicate aliases rather than deserialize into lossy dictionaries. |
| Generated PNGs are `<sheet>.png` under the sheets output directory | `tools/AssetConverter/src/AssetConverter/BatchConverter.cs:17-18,40-42`; CLI paths place them at `Assets/Sprites/sheets` in `tools/AssetConverter/src/AssetConverter/Program.cs:92-99,146-151`. |
| Rendering's existing strict manifest parser already proves duplicate-aware object traversal, rectangle checks, sorted frame publication, and `<assetDirectory>/manifest.json` loading | `src/MapEditor.Rendering/Assets/SpriteManifest.cs:64-127,129-223,225-259`. Do not reference Rendering from the converter; mirror this wire contract in an internal converter input index so dependency direction remains Core ← Rendering and Core ← Converter. |
| A graphic with `Graphic == 0` is the existing empty sentinel regardless of sheet | `src/MapEditor.Rendering/Assets/SpriteReference.cs:3-6`; converted Aspereta maps also write `(0,0)` for empty cells at `tools/AssetConverter/src/AssetConverter/Aspereta/AsperetaMapConverter.cs:41-57`. Ignore such layer-0 placements. |
| Existing `all` writes maps before the combined manifest and currently ends conversion after writing that manifest | `tools/AssetConverter/src/AssetConverter/Program.cs:173-189`. Invoke terrain generation immediately after line 189 so every required generated input exists. |
| Focused commands use an optional repo root and `all` defaults it to `../..` | `tools/AssetConverter/src/AssetConverter/Program.cs:140-148,207-216`. `terrain` must follow the same root convention. |
| The converter targets `net10.0` while Core targets `net8.0` | `tools/AssetConverter/src/AssetConverter/AssetConverter.csproj:3-7`; `src/MapEditor.Core/MapEditor.Core.csproj:1-5`. A net10 executable can reference the net8 library; add the project reference rather than copying Core types. |
| Rendering already references Core, preserving the desired future consumption direction | `src/MapEditor.Rendering/MapEditor.Rendering.csproj:1-9`. Core must gain no ImageSharp, file-system asset-root, Rendering, Avalonia, or Godot dependency. |
| Existing converter atomic output uses create/write/flush followed by same-directory move-with-overwrite and cleanup on failure | `tools/AssetConverter/src/AssetConverter/Manifest/AppearanceManifestFileStore.cs:5-12,18-51,54-64`. Keep the terrain writer independent but follow and strengthen this tested seam. |
| Existing injected atomic-write tests prove prior-byte preservation and no leaked temp file on write/replace/move failure | `tools/AssetConverter/tests/AssetConverter.Tests/AppearanceManifestBuilderTests.cs:197-285,287-350`. The terrain fake must mirror the new terrain file-operations interface exactly. |
| Generated assets, including the new catalog, are ignored and are recreated through `all` | `.gitignore:21-30`. Commit code/tests only. |

Planning baseline, verified in this worktree with .NET SDK `10.0.400`:

```bash
dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj -v minimal
# PASS: 220 tests

dotnet test tools/AssetConverter/tests/AssetConverter.Tests/AssetConverter.Tests.csproj \
  --filter 'FullyQualifiedName~TileSheetGeneratorTests|FullyQualifiedName~AppearanceManifestBuilderTests' -v minimal
# PASS: 22 tests; existing GifLoader nullable warnings remain
```

The Illutia data/maps and Unity comparison PNGs exist locally, but the configured Aspereta data/maps under `/home/hayden/...` do not. Therefore neither full existing converter tests that require Aspereta nor `all` are a planning/CI gate on this machine. All new automated tests must remain synthetic.

## Locked schema-v1 contract

Use namespace `MapEditor.Core.Terrain`. The small public surface is:

```csharp
public enum TerrainTopology { FourWay, EightWay }
public enum TerrainReviewStatus { Enabled, Pending, Disabled }
public enum TerrainMemberProvenance { MapObserved, ImageOnly }

public readonly record struct TerrainGraphicReference(int Sheet, int Graphic);

public sealed record TerrainGenerationSettings(
    int HoldoutModulo,
    int MinimumMapSupport,
    int MinimumRegionSupport,
    int MinimumObservationSupport,
    int MinimumDiagonalSupport,
    double MinimumEightWayAccuracyGain,
    double MapMemberCompatibility,
    double ImageOnlyCompatibility,
    double MinimumClassificationMargin,
    double MaximumAmbiguity,
    double EnabledConfidence,
    double PendingConfidence);

public sealed record TerrainSetMetrics(
    int MapSupport,
    int RegionSupport,
    int ObservationSupport,
    int DiagonalSupport,
    double MaskEntropy,
    double Completeness,
    double Ambiguity,
    double VisualCompatibility,
    double HoldoutAccuracy,
    double EightWayAccuracyGain,
    double Confidence);

public sealed record TerrainMaskDefinition(
    int Mask,
    IReadOnlyList<TerrainGraphicReference> Variants);

public sealed record TerrainMemberDefinition(
    TerrainGraphicReference Reference,
    TerrainMemberProvenance Provenance);

public sealed record TerrainDiagnostic(
    string Code,
    string Message,
    int? Mask = null,
    TerrainGraphicReference? Reference = null);

public sealed record TerrainSetDefinition(
    string Id,
    string DisplayName,
    TerrainReviewStatus Status,
    TerrainTopology Topology,
    TerrainSetMetrics Metrics,
    IReadOnlyList<TerrainMaskDefinition> Masks,
    IReadOnlyList<TerrainMemberDefinition> Members,
    IReadOnlyList<TerrainDiagnostic> Diagnostics);

public sealed record TerrainCatalog(
    int SchemaVersion,
    string GeneratorVersion,
    string CorpusFingerprint,
    TerrainGenerationSettings Settings,
    IReadOnlyList<TerrainSetDefinition> Sets,
    IReadOnlyList<TerrainDiagnostic> Diagnostics);

public static class TerrainMasks
{
    public const int North = 1;
    public const int East = 2;
    public const int South = 4;
    public const int West = 8;
    public const int NorthEast = 16;
    public const int SouthEast = 32;
    public const int SouthWest = 64;
    public const int NorthWest = 128;

    public static int Normalize(int mask, TerrainTopology topology);
    public static bool IsReachable(int mask, TerrainTopology topology);
    public static IReadOnlyList<int> Required(TerrainTopology topology);
}

public static class TerrainGeneratedId
{
    public static string Create(
        TerrainTopology topology,
        IEnumerable<TerrainGraphicReference> members);
}

public static class TerrainCatalogJson
{
    public const int CurrentSchemaVersion = 1;
    public static TerrainCatalog Parse(string json, string sourcePath = "<memory>");
    public static string Serialize(TerrainCatalog catalog);
}

public static class TerrainCatalogValidator
{
    public static IReadOnlyList<TerrainValidationIssue> Validate(TerrainCatalog catalog);
}

public readonly record struct TerrainValidationIssue(
    string Code,
    string Message,
    string? TerrainId = null,
    int? Mask = null,
    TerrainGraphicReference? Reference = null);
```

JSON uses the exact camel-case field names represented above. Enum wire values are `four-way`/`eight-way`, `enabled`/`pending`/`disabled`, and `map-observed`/`image-only`. The root property order is `schemaVersion`, `generatorVersion`, `corpusFingerprint`, `settings`, `diagnostics`, `sets`; set property order follows the constructor above. Serialization is compact UTF-8 JSON represented as a .NET string, ends with exactly one LF, writes invariant-culture JSON numbers, and never emits a timestamp or absolute path.

Defaults emitted by generator version `terrain-v1` are:

```text
holdoutModulo=5
minimumMapSupport=5
minimumRegionSupport=8
minimumObservationSupport=64
minimumDiagonalSupport=32
minimumEightWayAccuracyGain=0.05
mapMemberCompatibility=0.70
imageOnlyCompatibility=0.94
minimumClassificationMargin=0.05
maximumAmbiguity=0.10
enabledConfidence=0.90
pendingConfidence=0.45
```

`TerrainGeneratedId.Create` sorts distinct members by numeric `(Sheet, Graphic)`, encodes `four-way|sheet:graphic,...` or `eight-way|...` as UTF-8, hashes it with SHA-256, and returns `terrain-4-<64 lowercase hex>` or `terrain-8-<64 lowercase hex>`. Empty input and duplicate input are invalid. Name/status/metrics/mask ordering/diagnostics do not affect the ID; topology or membership does.

Four-way normalization removes all diagonal bits. Eight-way normalization first masks to the eight declared bits, then removes NE unless N+E are set, SE unless S+E are set, SW unless S+W are set, and NW unless N+W are set. `Required` is numeric ascending and therefore contains exactly 16 or 47 values.

Parsing and validation are separate. Parsing rejects malformed JSON, wrong schema version, missing/duplicate/wrong-kind required properties, unknown enum strings, non-finite/out-of-range `[0,1]` settings or metrics, and mutable/null collection members. It permits incomplete/overlapping pending and disabled sets. Catalog validation then reports stable machine-readable issues for blank/duplicate/mismatched IDs, `Graphic == 0`, a sheet outside the map format’s signed Int16 range, duplicate members/masks/variants, unreachable masks, variants absent from members, members absent from all variant lists, enabled missing masks/empty variants, and membership shared by enabled sets. Sheet zero and negative sheet/graphic IDs remain structurally valid when `Graphic != 0`; the future asset validator decides whether the manifest resolves them, matching current map/sprite semantics. Manifest existence and exact 32×32 frame checks remain a Part 2 asset-context responsibility; the converter independently guarantees them from its source index.

## Locked inference rules

These rules make the approved “hybrid” and “deterministic conservative” requirements implementable rather than leaving tuning decisions hidden in code:

1. Sort map files, manifest IDs, sheet IDs, frame IDs, references, candidates, diagnostics, and ties with ordinal strings then numeric IDs as applicable. Never depend on `Directory.EnumerateFiles`, `Dictionary`, or `HashSet` iteration order.
2. Hash only normalized relative names and exact bytes, never full paths. The corpus fingerprint is `sha256:<lowercase hex>` over length-prefixed records for sorted `Assets/Maps/*.map`, exact `Assets/Sprites/manifest.json`, and each map-referenced eligible `Assets/Sprites/sheets/<sheet>.png`. Include record kind, `/`-normalized relative path, byte length, and bytes. Settings are recorded separately and are not part of this corpus fingerprint.
3. A placement is eligible only when layer-0 `Graphic != 0`, its manifest reference exists, and its rect is exactly 32×32. Missing references and oversized frames become sorted root diagnostics and are excluded. A malformed/unreadable map, malformed manifest, missing/decode-failed required PNG, out-of-bounds source rect, no maps, or no eligible placements aborts generation before publication.
4. Premultiply RGB by alpha before feature work. Extract once per reference: 8×8 alpha occupancy, a 65-bin quantized RGB histogram (4 bins/channel plus transparent), 8×8 premultiplied RGBA/luminance perceptual cells, four 8×8 corner descriptors, and N/E/S/W four-pixel-deep directional edge profiles. All values are normalized to `[0,1]` using integer accumulation before division.
5. Similarity is `0.15 alpha + 0.20 palette + 0.25 perceptual + 0.15 corner + 0.25 edge-style`, where each component is `1 - normalized mean absolute distance`, clamped to `[0,1]`. Bucket by sheet, alpha-coverage decile, the two strongest palette bins, and four 16-bit perceptual-hash bands. Compare only same-sheet entries with adjacent alpha deciles, at least one matching dominant palette bin, and at least three matching hash bands; this bounds comparisons instead of global all-pairs work.
6. Seed a graph from map-observed references. Add a same-sheet edge only when references occur cardinally adjacent in at least two distinct maps, or in at least two disjoint adjacency occurrences that share no cell, and similarity is at least `MapMemberCompatibility`. Deterministic connected components with at least two members become provisional families. Omit singletons with a sorted root `insufficient-family-members` diagnostic; Task 4 assigns review states only to materialized families.
7. For each family/map, cardinal flood-fill family cells into regions. Give each map total weight 1, each family region in that map equal share, and each placement within its region equal share. The weighted modal neighbor mask for a reference is the greatest weight, with a tie marked ambiguous and broken by smaller normalized mask only for deterministic output. This prevents a giant floor/map from dominating many small maps.
8. Hold out maps for which the first 64 bits of SHA-256 of the ordinal map identity are divisible by `HoldoutModulo`. Fit modal members/masks on remaining maps and reconstruct held-out placements. A candidate with no training or no held-out observations receives holdout accuracy 0 and cannot auto-enable.
9. Train connected/disconnected N/E/S/W edge centroids and present/absent corner centroids from map-observed members. Search only other 32×32 frames on the family’s sheet through the same buckets. Admit an image-only frame only when overall similarity is at least `ImageOnlyCompatibility`, its best family beats the next family by `MinimumClassificationMargin`, and every classified side/corner beats its opposite centroid by that margin. Normalize the inferred mask; otherwise emit `image-only-ambiguous` on the owning candidate and do not admit it.
10. Fit four-way and eight-way models independently. Choose eight-way only when diagonal support reaches `MinimumDiagonalSupport` and held-out accuracy exceeds four-way by at least `MinimumEightWayAccuracyGain`; otherwise choose four-way and emit `eight-way-evidence-insufficient` when diagonal observations existed.
11. Compute normalized entropy over required masks and completeness as covered-required/required. Ambiguity is nonmodal weighted observation mass/total mass. Visual compatibility is the mean member-to-family-medoid similarity. Support score is the minimum of map/minimum-map, region/minimum-region, and observations/minimum-observations, each capped at 1. Confidence is `0.20 support + 0.15 entropy + 0.25 completeness + 0.15 (1-ambiguity) + 0.10 visualCompatibility + 0.15 holdoutAccuracy`.
12. A candidate is initially enabled only if all support minima pass, completeness is 1, ambiguity is at most the maximum, holdout accuracy and confidence are each at least `EnabledConfidence`, every source frame is valid, and catalog validation has no set-local issue. Otherwise it is pending at confidence ≥ `PendingConfidence` or when incomplete despite passing support; lower-confidence candidates are disabled. Resolve enabled overlaps by descending confidence, then descending map/region/observation support, then ID ordinal; keep the winner enabled and demote losers to pending with `enabled-member-conflict` diagnostics. Pending/disabled overlaps remain.
13. Within each mask, list map-observed variants before image-only variants, then numeric `(Sheet, Graphic)`. Sort members numerically, diagnostics by `(code, mask, sheet, graphic, message)`, sets by ID, and root diagnostics by the same key. Generated display name is `Generated <lowest-sheet>-<first 8 ID hash chars>`.

## Overall mutation propagation

| Mutation | Source of truth | Readers/dependencies | Required propagation and atomicity |
|---|---|---|---|
| Add catalog contract | New `MapEditor.Core.Terrain` values | Converter now; Rendering/App in Parts 2–3 | Core compile → canonical parser/writer/validator tests → converter project reference. No runtime registry or persisted migration. |
| Build feature cache/candidates | Manifest/map/PNG bytes | Scoring and output diagnostics only | Load strict corpus → cache one feature/reference and one decoded image/sheet → dispose after candidate materialization. No shared/background publication. |
| Generate catalog file | Fully built validated `TerrainCatalog` | Future asset loader and manager; current CLI output | Serialize in memory → write and durable-flush unique same-directory temp → atomic move/replace → report success. Any earlier or I/O failure leaves prior destination bytes and no temp. |
| Integrate `all` | Existing conversion workflow | Generated `Assets/Sprites/terrain-brushes.json` | Sheets/maps/combined manifest → invoke the exact focused terrain implementation → only then print terrain success. A terrain failure does not roll back other generated assets but must preserve the previous terrain catalog and make `all` fail. |

## Task 0: Add the shared schema, masks, stable IDs, and validation

**Files:**
- Create: `src/MapEditor.Core/Terrain/TerrainCatalog.cs`
- Create: `src/MapEditor.Core/Terrain/TerrainMasks.cs`
- Create: `src/MapEditor.Core/Terrain/TerrainGeneratedId.cs`
- Create: `src/MapEditor.Core/Terrain/TerrainCatalogJson.cs`
- Create: `src/MapEditor.Core/Terrain/TerrainCatalogValidator.cs`
- Create: `tests/MapEditor.Core.Tests/Terrain/TerrainMasksTests.cs`
- Create: `tests/MapEditor.Core.Tests/Terrain/TerrainCatalogJsonTests.cs`
- Create: `tests/MapEditor.Core.Tests/Terrain/TerrainCatalogValidatorTests.cs`
- Modify: `tools/AssetConverter/src/AssetConverter/AssetConverter.csproj:10-12`
- Modify: `tools/AssetConverter/AssetConverter.sln:6-13,23-54`

**Mutation impact:**
- Source of truth changed: the new immutable schema-v1 types in `src/MapEditor.Core/Terrain/TerrainCatalog.cs`; `MapEditor.Core` remains the sole terrain-contract owner.
- Important readers: `TerrainCatalogJson`, `TerrainCatalogValidator`, the new converter project reference, and future Rendering/App parts. Existing map codec/editing readers are unchanged.
- Derived/cached state affected: no runtime cache. Converter build dependency gains Core; root editor dependency direction is unchanged because Rendering already references Core (`src/MapEditor.Rendering/MapEditor.Rendering.csproj:6-9`).
- Required propagation: define values → prove normalization/round-trip/validation → add Core reference to converter csproj and converter solution → build both solutions.
- Invariants: no provider/file-system/UI type in Core; 16/47 masks exactly; IDs depend only on topology+distinct sorted membership; pending/disabled incompleteness and overlap parse; enabled incompleteness/overlap validate as errors; serializer output is canonical.
- Observable proof required: compare exact serialized bytes and validation issue values, not merely method calls.

**Step 1: Write failing Core tests**

Add exhaustive tests named:

- `Required_FourWay_ReturnsAll16CardinalMasksInOrder`
- `Required_EightWay_ReturnsExactly47NormalizedMasksInOrder`
- `Normalize_EightWay_RemovesUnsupportedDiagonalBits` (iterate all 256 masks)
- `Create_ReorderedMembersAndChangedEditableFields_KeepGeneratedId`
- `Create_ChangedTopologyOrMember_ChangesGeneratedId`
- `Serialize_RepresentativeCatalog_MatchesLockedSchemaV1Bytes`
- `Parse_Serialize_RoundTripsWithoutMutableCollectionLeaks`
- `Parse_DuplicateRequiredPropertyOrUnknownEnum_ThrowsWithSourcePath`
- `Validate_IncompletePendingAndOverlappingDisabledSets_AllowsReviewData`
- `Validate_IncompleteOrOverlappingEnabledSets_ReturnsExactIssues`
- adversarial `Validate_VariantNotInMembersAndUnreachableDiagonal_AreNotSilentlyAccepted`.

The locked-byte fixture must include one enabled complete four-way set and one incomplete pending eight-way set with an image-only member and diagnostic, so every wire field and enum spelling is pinned.

**Step 2: Run tests to verify red**

Run:

```bash
dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj \
  --filter 'FullyQualifiedName~Terrain' -v minimal
```

Expected: FAIL to compile because `MapEditor.Core.Terrain` does not exist.

**Step 3: Implement the minimal shared contract**

Implement the locked APIs and JSON rules above. Construct defensive `Array.AsReadOnly`/read-only copies during parse; never expose parser DTO collections. Use `Utf8JsonWriter` for canonical property/order control and `JsonDocument.EnumerateObject()` with explicit seen-property sets so duplicate JSON properties cannot collapse. Keep semantic review-state validation out of parse.

Add the Core project reference to `AssetConverter.csproj`, and add Core under the converter solution’s `src` folder with normal configuration/nesting entries. Do not add ImageSharp or converter references to Core.

**Step 4: Run green and dependency checks**

Run:

```bash
dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj \
  --filter 'FullyQualifiedName~Terrain' -v minimal
dotnet build tools/AssetConverter/AssetConverter.sln -v minimal
dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj -v minimal
```

Expected: all terrain tests pass, converter solution builds with only pre-existing GifLoader nullable warnings, and all Core tests pass.

**Invariant-to-test matrix:**

| Invariant | Proved by |
|---|---|
| Cardinal/blob spaces are exactly 16/47 and diagonals normalize canonically | `Required_*`, `Normalize_EightWay_*` |
| Editable metadata cannot perturb generated identity | `Create_ReorderedMembersAndChangedEditableFields_KeepGeneratedId` |
| Schema bytes and enum spellings are stable | `Serialize_RepresentativeCatalog_MatchesLockedSchemaV1Bytes` |
| Review candidates can remain incomplete/overlapping, enabled sets cannot | `Validate_IncompletePending*`, `Validate_IncompleteOrOverlappingEnabledSets*` |
| Invalid mask/member wiring is rejected adversarially | `Validate_VariantNotInMembersAndUnreachableDiagonal_AreNotSilentlyAccepted` |
| Core remains provider-neutral | `dotnet build tools/AssetConverter/AssetConverter.sln` plus project-reference inspection; compile-time dependency structure is the proof |

**Step 5: Commit**

```bash
git add src/MapEditor.Core/Terrain tests/MapEditor.Core.Tests/Terrain \
  tools/AssetConverter/src/AssetConverter/AssetConverter.csproj \
  tools/AssetConverter/AssetConverter.sln
git commit -m "feat: add terrain catalog contract"
```

## Task 1: Load and fingerprint the generated map/frame corpus

**Files:**
- Create: `tools/AssetConverter/src/AssetConverter/Terrain/TerrainCorpus.cs`
- Create: `tools/AssetConverter/src/AssetConverter/Terrain/TerrainCorpusLoader.cs`
- Create: `tools/AssetConverter/src/AssetConverter/Terrain/TerrainFrameIndex.cs`
- Create: `tools/AssetConverter/src/AssetConverter/Terrain/TerrainCorpusFingerprint.cs`
- Create: `tools/AssetConverter/src/AssetConverter/Properties/AssemblyInfo.cs`
- Create: `tools/AssetConverter/tests/AssetConverter.Tests/Terrain/TerrainFixtureBuilder.cs`
- Create: `tools/AssetConverter/tests/AssetConverter.Tests/Terrain/TerrainCorpusLoaderTests.cs`
- Create: `tools/AssetConverter/tests/AssetConverter.Tests/Terrain/TerrainCorpusFingerprintTests.cs`

**Mutation impact:**
- Source of truth changed: none; generated map, manifest, and PNG bytes remain read-only. `TerrainCorpus` is an immutable snapshot built from those files.
- Important readers: Tasks 2–5 consume eligible placements, frame rectangles, diagnostics, and fingerprint. `MapCodec` remains the map-format authority (`src/MapEditor.Core/MapCodec.cs:15-69`).
- Derived/cached state affected: frame lookup, map grid, observed directed adjacency counts, and fingerprint are derived per invocation and are not published globally.
- Required propagation: normalize root → read exact manifest bytes → parse duplicate-aware index → read/decode every sorted map → retain layer-0 grids/boundaries/raw adjacency → identify referenced sheets/PNGs → hash all sorted records → return one immutable corpus.
- Failure behavior: any required malformed/unreadable input throws a path-bearing `TerrainGenerationException`; excluded non-32/missing references produce diagnostics. No output file exists in this task.
- Invariants: only layer 0 seeds; map trailers work through `MapCodec`; empty graphic is ignored; paths do not affect fingerprint; file creation/enumeration order does not affect observations or fingerprint; no eligible corpus cannot overwrite an existing catalog later.
- Observable proof required: inspect final placement grids/diagnostics/fingerprint from real encoded `MapDocument` objects.

**Step 1: Write failing loader/fingerprint tests**

`TerrainFixtureBuilder` must create a temporary repository layout, use `MapDocument.Create`/`SetLayer`/`MapCodec.Encode`, write a minimal manifest, and generate real ImageSharp PNG sheets. It must not read `Paths.Illutia*`.

Add tests:

- `Load_ReadsOnlyLayer0AndCollectsBoundariesCardinalAndDiagonalAdjacency`
- `Load_GraphicZeroWithNonzeroSheet_IsIgnored`
- `Load_MapTrailer_IsAcceptedByMapCodec`
- `Load_MissingManifestReferenceAndOversizedFrame_AreDiagnosedAndExcluded`
- `Load_MalformedMap_ThrowsBeforeCatalogMutation`
- `Load_NoMapsOrNoEligiblePlacements_Throws`
- `Compute_EquivalentRootsAndDifferentCreationOrder_HaveSameFingerprint`
- adversarial `Compute_OneMapOrRelevantPngByteChanged_ChangesFingerprint`.

The test fixture should place a valid tile only on layer 1 in one cell and assert it never enters observations, catching accidental all-layer mining.

**Step 2: Run red**

```bash
dotnet test tools/AssetConverter/tests/AssetConverter.Tests/AssetConverter.Tests.csproj \
  --filter 'FullyQualifiedName~TerrainCorpus' -v minimal
```

Expected: FAIL to compile because terrain corpus types do not exist.

**Step 3: Implement strict immutable corpus loading**

Use exact paths:

```text
<repoRoot>/Assets/Maps/*.map
<repoRoot>/Assets/Sprites/manifest.json
<repoRoot>/Assets/Sprites/sheets/<sheet>.png
```

The loader contract is `internal static TerrainCorpus Load(string repoRoot)`. `TerrainCorpus` owns sorted immutable map samples, eligible frame metadata grouped by sheet, root diagnostics, and the fingerprint. Grant internals only to `AssetConverter.Tests` through the new assembly attribute.

Parse manifest IDs as invariant canonical integers; reject duplicate textual properties and numeric aliases (`"1"`/`"01"`). Validate rect arithmetic with checked bounds. Do not use `MapEditor.Rendering.SpriteManifest`, because that would invert converter/editor ownership. Read each map exactly once, retain its bytes for fingerprinting, call `MapCodec.Decode`, and materialize only layer 0 references plus coordinates and outside-map boundary facts.

Do not decode PNG pixels yet, but require each relevant sheet file to exist and include its exact bytes in the fingerprint. Pixel dimensions/source bounds are validated by `Get_OutOfBoundsManifestRect_ThrowsReferenceDiagnostic` in Task 2; `Load_MalformedMap_ThrowsBeforeCatalogMutation` covers only malformed map bytes here.

**Step 4: Run green and regressions**

```bash
dotnet test tools/AssetConverter/tests/AssetConverter.Tests/AssetConverter.Tests.csproj \
  --filter 'FullyQualifiedName~TerrainCorpus' -v minimal
dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj -v minimal
```

Expected: focused and Core tests pass; no test accesses proprietary paths.

**Invariant-to-test matrix:**

| Invariant | Proved by |
|---|---|
| Inference seeds only layer 0 and ignores empty graphic values | `Load_ReadsOnlyLayer0*`, `Load_GraphicZeroWithNonzeroSheet_IsIgnored` |
| Existing trailer-bearing maps decode through the authoritative codec | `Load_MapTrailer_IsAcceptedByMapCodec` |
| Unsupported/missing frames never masquerade as candidate members | `Load_MissingManifestReferenceAndOversizedFrame_AreDiagnosedAndExcluded` |
| Invalid/empty input aborts rather than publishing an empty replacement | `Load_MalformedMap_ThrowsBeforeCatalogMutation`, `Load_NoMapsOrNoEligiblePlacements_Throws` |
| Fingerprint is root/order independent and content sensitive | both `Compute_*` tests, including the adversarial byte-change case |

**Step 5: Commit**

```bash
git add tools/AssetConverter/src/AssetConverter/Terrain \
  tools/AssetConverter/src/AssetConverter/Properties/AssemblyInfo.cs \
  tools/AssetConverter/tests/AssetConverter.Tests/Terrain
git commit -m "feat: load terrain inference corpus"
```

## Task 2: Extract and cache deterministic image features

**Files:**
- Create: `tools/AssetConverter/src/AssetConverter/Terrain/TerrainImageFeatures.cs`
- Create: `tools/AssetConverter/src/AssetConverter/Terrain/TerrainFeatureExtractor.cs`
- Create: `tools/AssetConverter/src/AssetConverter/Terrain/TerrainFeatureBuckets.cs`
- Create: `tools/AssetConverter/src/AssetConverter/Terrain/TerrainFeatureCache.cs`
- Create: `tools/AssetConverter/tests/AssetConverter.Tests/Terrain/TerrainFeatureExtractorTests.cs`
- Create: `tools/AssetConverter/tests/AssetConverter.Tests/Terrain/TerrainFeatureBucketsTests.cs`
- Create: `tools/AssetConverter/tests/AssetConverter.Tests/Terrain/TerrainFeatureCacheTests.cs`

**Mutation impact:**
- Source of truth changed: none; RGBA pixels from the indexed PNG/rect are canonical inputs.
- Important readers: family seeding, directional mask classification, image-only admission, visual score, and diagnostics.
- Derived/cached state affected: `TerrainFeatureCache` mutates private dictionaries for decoded sheets and extracted reference features.
- Required propagation: first reference request → decode sheet once with `Image.Load<Rgba32>` → validate image bounds → copy/process exact rect → cache immutable feature; later requests return the same feature → generator disposal disposes every decoded sheet once.
- Publication/lifecycle boundary: the cache is invocation-local, synchronous, and never globally registered. Construct → use while building candidates → materialize candidate values → dispose before file publication. Readers cannot observe partially extracted features.
- Failure behavior: decode/bounds failures throw path/reference-bearing `TerrainGenerationException`; cache disposal occurs through `using` even when extraction/inference fails.
- Invariants: transparent RGB cannot affect distance; one extraction/reference and one decode/sheet; bucket queries are deterministic and cannot degrade into global cross-sheet comparisons; input-order permutations preserve similarity/buckets.
- Observable proof required: compare concrete vectors/similarities/candidate reference lists and loader/disposal counts.

**Step 1: Write failing feature tests**

Use generated solid, checker, transparent-noise, directional-edge, and deceptive-lookalike 32×32 frames. Add:

- `Extract_TransparentRgbNoise_ProducesIdenticalFeatures`
- `Extract_DirectionalBordersAndCorners_ProduceExpectedNormalizedComponents`
- `Similarity_IsSymmetricBoundedAndRanksStyledVariantAboveLookalike`
- `QueryCandidates_UsesSameSheetAndLockedBucketNeighborhoodInNumericOrder`
- adversarial `QueryCandidates_DifferentSheetOrNoSharedHashBand_IsNeverCompared`
- `Get_RepeatedReferences_DecodesSheetOnceAndExtractsFrameOnce`
- `Dispose_AfterExtractionOrFailure_DisposesEveryDecodedSheetOnce`
- `Get_OutOfBoundsManifestRect_ThrowsReferenceDiagnostic`.

Use a recording fake image loader implementing the exact new internal production interface `ITerrainSheetImageLoader.Load(string path)` and return type; do not invent `Open`, `Read`, or async methods. Also include one real ImageSharp PNG integration test so the fake cannot validate a phantom loading path.

**Step 2: Run red**

```bash
dotnet test tools/AssetConverter/tests/AssetConverter.Tests/AssetConverter.Tests.csproj \
  --filter 'FullyQualifiedName~TerrainFeature' -v minimal
```

Expected: FAIL to compile because feature types do not exist.

**Step 3: Implement the locked descriptors, distance, buckets, and cache**

Keep pixel/image types internal to converter. Use integer channel sums and fixed loop order; convert to doubles only after each bin/cell sum is complete. Use no SIMD- or platform-dependent image transforms. Feature buckets must materialize `SortedSet<TerrainGraphicReference>` results and expose comparison-count data internally for the adversarial bounded-comparison test, not as public catalog data.

The real loader surface is:

```csharp
internal interface ITerrainSheetImageLoader
{
    Image<Rgba32> Load(string path);
}
```

`TerrainFeatureCache` owns returned images and is `IDisposable`; callers do not dispose an individual image. It intentionally performs no parallel work, cancellation, global caching, or cross-run reuse.

**Step 4: Run green**

```bash
dotnet test tools/AssetConverter/tests/AssetConverter.Tests/AssetConverter.Tests.csproj \
  --filter 'FullyQualifiedName~TerrainFeature' -v minimal
```

Expected: all focused tests pass, including real PNG loading and disposal.

**Invariant-to-test matrix:**

| Invariant | Proved by |
|---|---|
| Transparent storage artifacts do not alter inference | `Extract_TransparentRgbNoise_ProducesIdenticalFeatures` |
| Feature/distance math is deterministic and bounded | `Extract_Directional*`, `Similarity_*` |
| Candidate work remains same-sheet and bucketed | `QueryCandidates_*`, especially the adversarial no-shared-band case |
| Expensive state is cached exactly once and always released | `Get_RepeatedReferences_*`, `Dispose_AfterExtractionOrFailure_*` |
| Bad atlas metadata fails with actionable identity | `Get_OutOfBoundsManifestRect_ThrowsReferenceDiagnostic` |

**Step 5: Commit**

```bash
git add tools/AssetConverter/src/AssetConverter/Terrain \
  tools/AssetConverter/tests/AssetConverter.Tests/Terrain
git commit -m "feat: extract terrain image features"
```

## Task 3: Mine weighted hybrid candidate families

**Files:**
- Create: `tools/AssetConverter/src/AssetConverter/Terrain/TerrainCandidate.cs`
- Create: `tools/AssetConverter/src/AssetConverter/Terrain/TerrainRegionMiner.cs`
- Create: `tools/AssetConverter/src/AssetConverter/Terrain/TerrainCandidateMiner.cs`
- Create: `tools/AssetConverter/src/AssetConverter/Terrain/TerrainImageMemberClassifier.cs`
- Create: `tools/AssetConverter/tests/AssetConverter.Tests/Terrain/TerrainRegionMinerTests.cs`
- Create: `tools/AssetConverter/tests/AssetConverter.Tests/Terrain/TerrainCandidateMinerTests.cs`
- Create: `tools/AssetConverter/tests/AssetConverter.Tests/Terrain/TerrainImageMemberClassifierTests.cs`

**Mutation impact:**
- Source of truth changed: none; candidates are deterministic projections of immutable corpus observations/features/settings.
- Important readers: Task 5 model fitting and diagnostics. No catalog file or editor state reads provisional candidates.
- Derived/cached state affected: adjacency graph components, family-region IDs, weighted mask histograms, centroids, image-only assignments, and rejection diagnostics.
- Required propagation: deterministically partition maps by identity → use training-map references/features only for the bounded same-sheet adjacency graph → sorted components → training-map cardinal regions → weighted placement/mask evidence → directional centroids → uniquely classified same-sheet image-only members → immutable candidates. Holdout maps remain unread by mining after corpus loading and are passed only to Task 4 evaluation.
- Failure behavior: an unclassifiable image remains absent and adds a diagnostic; it does not abort other candidates. Numeric overflow/non-finite weight is an implementation error and aborts generation before publication.
- Invariants: held-out maps cannot influence graph membership, masks, centroids, image-only admission, or topology; dominant map/region size cannot dominate total map weight; disconnected but visually identical lookalikes do not seed a family; image-only admission uses stricter threshold and unique margin; all admitted image-only members carry provenance and a normalized predicted mask.
- Observable proof required: assert final candidate membership, weighted histograms, provenance, inferred masks, and diagnostics—not just graph helper calls.

**Step 1: Write failing mining tests**

Construct maps and PNGs through `TerrainFixtureBuilder` to add:

- `Mine_CardinallyAdjacentCompatibleVariants_FormDeterministicFamily`
- `Mine_DeceptiveLookalikeWithoutAdjacency_DoesNotJoinFamily`
- `Mine_DifferentSheetsNeverJoinEvenWhenPixelsMatch`
- `Weighting_HugeRegionAndTinyMap_EachContributeOneMapWeight`
- `Weighting_DisconnectedRegionsSplitTheirMapWeightEqually`
- `Classify_MissingVariantOnSameSheet_IsAdmittedAsImageOnlyWithNormalizedMask`
- `Classify_MapObservedReference_RemainsMapObservedWhenAlsoFoundByImageSearch`
- adversarial `Classify_NearTieOrBelowStrictThreshold_IsRejectedWithImageOnlyAmbiguousDiagnostic`
- `Mine_InputAndMapOrderPermutation_ProducesEquivalentCandidates`;
- adversarial `Mine_ChangingOnlyHeldOutMaps_DoesNotChangeCandidatesCentroidsOrAdmissions`.

Assign train/holdout identity before constructing any adjacency graph or centroid. The weighting regression fixture must create one very large repeated floor and several small training maps whose modal mask differs; assert the small-map evidence is not overwhelmed by raw tile count.

**Step 2: Run red**

```bash
dotnet test tools/AssetConverter/tests/AssetConverter.Tests/AssetConverter.Tests.csproj \
  --filter 'FullyQualifiedName~TerrainCandidate|FullyQualifiedName~TerrainRegion|FullyQualifiedName~TerrainImageMember' -v minimal
```

Expected: FAIL to compile because candidate-mining types do not exist.

**Step 3: Implement graph, regions, weights, and image-only classification**

Use the locked inference rules exactly. Region identity is `(map identity, candidate component, row-major minimum coordinate)` and flood fill uses cardinal neighbors only. Compute raw support counts separately from normalized weights so diagnostics can explain both. A reference observed in maps always wins provenance over later image search.

Helper contracts:

- `TerrainRegionMiner` reads a candidate membership set and a map; it returns regions/weighted placement observations only. It does not alter the map, feature cache, candidate membership, or diagnostics.
- `TerrainCandidateMiner` owns graph/component construction and returns immutable candidates; it does not select topology/status or serialize.
- `TerrainImageMemberClassifier` reads trained centroids and eligible same-sheet features; it returns admissions and diagnostics, but does not mutate another candidate. The caller creates the expanded candidate after all classifications are known, preventing iteration-order effects.

**Step 4: Run green**

```bash
dotnet test tools/AssetConverter/tests/AssetConverter.Tests/AssetConverter.Tests.csproj \
  --filter 'FullyQualifiedName~TerrainCandidate|FullyQualifiedName~TerrainRegion|FullyQualifiedName~TerrainImageMember' -v minimal
```

Expected: focused tests pass and candidate snapshots are order independent.

**Invariant-to-test matrix:**

| Invariant | Proved by |
|---|---|
| Adjacency plus similarity, not appearance alone, seeds map families | `Mine_CardinallyAdjacent*`, adversarial `Mine_DeceptiveLookalike*` |
| Family membership never crosses sheets | `Mine_DifferentSheetsNeverJoinEvenWhenPixelsMatch` |
| Every map has equal total influence and each region shares it | both `Weighting_*` tests |
| Image-only additions are strict, uniquely classified, normalized, and marked | `Classify_MissingVariant*`, `Classify_MapObserved*`, adversarial `Classify_NearTie*` |
| Input order cannot change candidates | `Mine_InputAndMapOrderPermutation_ProducesEquivalentCandidates` |
| Held-out evidence cannot leak into training | adversarial `Mine_ChangingOnlyHeldOutMaps_DoesNotChangeCandidatesCentroidsOrAdmissions` |

**Step 5: Commit**

```bash
git add tools/AssetConverter/src/AssetConverter/Terrain \
  tools/AssetConverter/tests/AssetConverter.Tests/Terrain
git commit -m "feat: mine hybrid terrain candidates"
```

## Task 4: Fit topologies, score candidates, and resolve enabled conflicts

**Files:**
- Create: `tools/AssetConverter/src/AssetConverter/Terrain/TerrainModelFitter.cs`
- Create: `tools/AssetConverter/src/AssetConverter/Terrain/TerrainCandidateScorer.cs`
- Create: `tools/AssetConverter/src/AssetConverter/Terrain/TerrainCatalogBuilder.cs`
- Create: `tools/AssetConverter/tests/AssetConverter.Tests/Terrain/TerrainModelFitterTests.cs`
- Create: `tools/AssetConverter/tests/AssetConverter.Tests/Terrain/TerrainCandidateScorerTests.cs`
- Create: `tools/AssetConverter/tests/AssetConverter.Tests/Terrain/TerrainCatalogBuilderTests.cs`

**Mutation impact:**
- Source of truth changed: none; final definitions/statuses are derived from candidates and locked settings.
- Important readers: canonical JSON output, future manager diagnostics, and later enabled-set resolution.
- Derived/cached state affected: train/holdout partitions, modal mask mappings, 4/8 model metrics, selected topology, stable IDs, initial statuses, and conflict-demoted statuses.
- Required propagation: receive the partition fixed by Task 3 → fit both models from training observations only → reconstruct holdout without updating any model value → select topology → compute exact metrics/confidence → construct masks/members/diagnostics/ID → rank initial enabled sets globally → demote overlaps → run shared catalog validation.
- Failure behavior: no-holdout/no-training/insufficient support yields diagnostics and cannot enable; one bad candidate does not erase reviewable alternatives. Any enabled validation issue is an internal generation failure, not silently emitted invalid JSON.
- Invariants: 8-way requires support and material improvement; only complete high-confidence candidates enable; enabled membership is unique; pending/disabled alternatives may overlap; stable ID ignores names/status/metrics; all tie breaks are deterministic.
- Observable proof required: assert final topology, status, mappings, metrics, IDs, and conflict diagnostics from synthetic evidence.

**Step 1: Write failing model/scoring tests**

Add the design-required scenarios:

- `Fit_FourWayCorpus_ReconstructsAll16MasksAndSelectsFourWay`
- `Fit_CanonicalBlobCorpus_WithSupportedAccuracyGain_SelectsEightWayAndAll47Masks`
- `Fit_SparseCorners_StayFourWayAndReportInsufficientDiagonalEvidence`
- `Fit_NoisyVisualVariants_ShareMaskInDeterministicVariantOrder`
- `Fit_HoldoutAssignment_IsStableAcrossInputOrder`
- adversarial `Fit_ChangingHoldoutLabelsChangesAccuracyButNotFittedMasksCentroidsOrMembership`
- `Score_CompleteHighConfidenceCandidate_IsEnabled`
- `Score_IncompleteCandidate_IsPendingEvenWhenOtherMetricsAreHigh`
- `Score_BelowPendingThreshold_IsDisabled`
- `Build_StableIdIgnoresDisplayNameStatusAndObservationOrder`
- adversarial `Build_OverlappingEnabledCandidates_DeterministicallyDemotesLoserAndValidates`
- `Build_PendingAlternativesMayOverlapWithoutDemotion`.

Generate the complete mask corpora algorithmically from `TerrainMasks.Required`; do not check in 63 hand-authored image/map files. Ensure the 8-way fixture makes four-way collapse diagonal-distinct references so held-out reconstruction has a measured gain ≥ 0.05.

**Step 2: Run red**

```bash
dotnet test tools/AssetConverter/tests/AssetConverter.Tests/AssetConverter.Tests.csproj \
  --filter 'FullyQualifiedName~TerrainModel|FullyQualifiedName~TerrainCandidateScorer|FullyQualifiedName~TerrainCatalogBuilder' -v minimal
```

Expected: FAIL to compile because fitter/scorer/builder types do not exist.

**Step 3: Implement deterministic fitting and scoring**

Keep train and holdout evidence separate. Variants are learned from training data; held-out references only count correct when the trained mapping for the held-out normalized mask contains that observed member. Do not leak held-out modal masks into the model before measuring accuracy.

Emit exact diagnostic codes from the locked rules. Metrics are serialized rounded to six decimal places using midpoint-to-even rounding so equivalent arithmetic paths do not change bytes; comparisons use unrounded values. Build all definitions first, resolve global overlap once, then invoke `TerrainCatalogValidator.Validate`. The builder must throw with all enabled validation issues if any remain.

**Step 4: Run green and all terrain tests**

```bash
dotnet test tools/AssetConverter/tests/AssetConverter.Tests/AssetConverter.Tests.csproj \
  --filter 'FullyQualifiedName~Terrain' -v minimal
dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj \
  --filter 'FullyQualifiedName~Terrain' -v minimal
```

Expected: all converter/Core terrain tests pass.

**Invariant-to-test matrix:**

| Invariant | Proved by |
|---|---|
| Four-way and canonical blob models cover their exact reachable masks | both `Fit_*Corpus*` tests |
| 8-way is selected only for supported material held-out gain | `Fit_CanonicalBlobCorpus*`, `Fit_SparseCorners*` |
| Holdout evidence is deterministic and not leaked into training | `Fit_HoldoutAssignment_IsStableAcrossInputOrder` plus expected held-out accuracy assertions in both corpus tests |
| Completeness/support/confidence govern enabled/pending/disabled exactly | three `Score_*` tests |
| Enabled references are globally unique under adversarial overlap | `Build_OverlappingEnabledCandidates_DeterministicallyDemotesLoserAndValidates` |
| Review alternatives remain available | `Build_PendingAlternativesMayOverlapWithoutDemotion` |

**Step 5: Commit**

```bash
git add tools/AssetConverter/src/AssetConverter/Terrain \
  tools/AssetConverter/tests/AssetConverter.Tests/Terrain
git commit -m "feat: score terrain topology candidates"
```

## Task 5: Orchestrate deterministic generation and atomic catalog publication

**Files:**
- Create: `tools/AssetConverter/src/AssetConverter/Terrain/TerrainCatalogGenerator.cs`
- Create: `tools/AssetConverter/src/AssetConverter/Terrain/TerrainCatalogFileStore.cs`
- Create: `tools/AssetConverter/tests/AssetConverter.Tests/Terrain/TerrainCatalogGeneratorTests.cs`
- Create: `tools/AssetConverter/tests/AssetConverter.Tests/Terrain/TerrainCatalogFileStoreTests.cs`

**Mutation impact:**
- Source of truth changed: persisted `<repoRoot>/Assets/Sprites/terrain-brushes.json`; its canonical value is the serialized, validated `TerrainCatalog` built from current corpus bytes.
- Important readers: focused/all CLI output now, and future Rendering/App asset loading. Existing maps, manifest, PNGs, and map codec are read-only.
- Derived/cached state affected: no cross-run cache. Features/candidates are invocation-local and disposed/materialized before write.
- Required propagation sequence:
  1. Load and fingerprint all source inputs.
  2. Build/cache features under `using`.
  3. Mine candidates and materialize definitions/diagnostics.
  4. Build and shared-validate the complete catalog.
  5. Canonically serialize all bytes in memory.
  6. Create a unique temp in the destination directory, write all bytes, and flush file contents.
  7. Move temp over destination (or move into an absent destination).
  8. Only after replacement succeeds, return counts/path/fingerprint to the caller.
- Failure behavior: steps 1–5 touch no destination; write/flush/move failures delete temp and preserve prior bytes. With no prior file, failure leaves no destination. Never delete destination first.
- Invariants: repeat generation from equivalent input is byte-identical; publication is all-or-nothing; no absolute paths/timestamps leak; generated enabled sets pass shared validation and all source refs resolve to exact 32×32 frames.
- Observable proof required: compare destination bytes before/after injected failures and compare complete bytes from independent equivalent roots.

The public converter entry point is intentionally small:

```csharp
public sealed record TerrainGenerationResult(
    string OutputPath,
    string CorpusFingerprint,
    int Enabled,
    int Pending,
    int Disabled,
    IReadOnlyList<TerrainDiagnostic> Diagnostics);

public static class TerrainCatalogGenerator
{
    public const string RelativeOutputPath = "Assets/Sprites/terrain-brushes.json";
    public static TerrainGenerationResult Generate(string repoRoot);
}
```

`TerrainCatalogFileStore` has an internal overload accepting `ITerrainCatalogFileOperations`. The fake implements exactly the production interface methods `Exists`, `CreateFile`, `Replace`, `Move`, and `Delete`, matching the established repository seam at `tools/AssetConverter/src/AssetConverter/Manifest/AppearanceManifestFileStore.cs:5-12`. Production `CreateFile` returns a `FileStream`; after writing, call its durable flush before close. Tests may return a failing `Stream`, and the store must still clean up.

**Step 1: Write failing orchestration/atomicity tests**

Add:

- `Generate_SyntheticFourWayCorpus_WritesParseableValidatedCatalog`
- `Generate_EquivalentRootsAndRepeatedRuns_AreByteIdentical`
- `Generate_MapOrPngChange_ChangesFingerprintAndOutput`
- `Generate_InputFailure_PreservesPriorCatalogWithoutCreatingTemp`
- `Write_FlushOrReplaceFailure_PreservesPriorBytesAndDeletesTemp`
- `Write_MoveFailureWithoutPriorCatalog_LeavesNoDestinationOrTemp`
- adversarial `Generate_InvalidEnabledBuilderOutput_IsNeverPublished` using an internal `ITerrainCatalogBuilder.Build(TerrainCorpus, TerrainGenerationSettings)` seam whose production adapter delegates to `TerrainCatalogBuilder`.

The first test must parse the actual destination through `TerrainCatalogJson.Parse`, run `TerrainCatalogValidator.Validate`, and assert the final enabled set/masks/provenance—not only `TerrainGenerationResult` counts.

**Step 2: Run red**

```bash
dotnet test tools/AssetConverter/tests/AssetConverter.Tests/AssetConverter.Tests.csproj \
  --filter 'FullyQualifiedName~TerrainCatalogGenerator|FullyQualifiedName~TerrainCatalogFileStore' -v minimal
```

Expected: FAIL to compile because orchestrator/store types do not exist.

**Step 3: Implement one synchronous build-and-publish path**

`Generate` normalizes but never serializes the absolute repo root. It owns all temporary/cache lifetimes. Do not catch generation exceptions merely to return a success-shaped result; let the CLI fail nonzero after the store has preserved prior state.

The file store must generate a sibling name such as `terrain-brushes.json.tmp-<guid>`, never a system-temp path. Follow construct/build/validate → write temp → publish; there is no shared registry and no reader can observe the temp as the catalog path.

**Step 4: Run green and full converter suite**

```bash
dotnet test tools/AssetConverter/tests/AssetConverter.Tests/AssetConverter.Tests.csproj \
  --filter 'FullyQualifiedName~TerrainCatalogGenerator|FullyQualifiedName~TerrainCatalogFileStore' -v minimal
dotnet test tools/AssetConverter/tests/AssetConverter.Tests/AssetConverter.Tests.csproj -v minimal
```

Expected: focused tests pass. Full suite passes where configured external datasets exist; on this machine, run the non-Aspereta gate below because the known hard-coded Aspereta paths are absent:

```bash
dotnet test tools/AssetConverter/tests/AssetConverter.Tests/AssetConverter.Tests.csproj \
  --filter 'FullyQualifiedName!~Aspereta&FullyQualifiedName!~CombinedManifest_ContainsIllutiaAndRenumberedAsperetaSheets' -v minimal
```

Expected locally: all selected tests pass; only pre-existing nullable warnings are acceptable.

**Invariant-to-test matrix:**

| Invariant | Proved by |
|---|---|
| End-to-end output is consumable, valid, and source-resolved | `Generate_SyntheticFourWayCorpus_WritesParseableValidatedCatalog` |
| Equivalent corpus always emits identical bytes | `Generate_EquivalentRootsAndRepeatedRuns_AreByteIdentical` |
| Fingerprint/output reflects relevant source mutations | `Generate_MapOrPngChange_ChangesFingerprintAndOutput` |
| Build/input failure never reaches persisted state | `Generate_InputFailure_*`, adversarial invalid-builder test |
| Write/flush/replace/move failure preserves the prior publication boundary | both `Write_*Failure*` tests |

**Step 5: Commit**

```bash
git add tools/AssetConverter/src/AssetConverter/Terrain \
  tools/AssetConverter/tests/AssetConverter.Tests/Terrain
git commit -m "feat: atomically generate terrain catalog"
```

## Task 6: Add `terrain`, integrate `all`, and run final/red-team gates

**Files:**
- Create: `tools/AssetConverter/src/AssetConverter/Terrain/TerrainCommand.cs`
- Modify: `tools/AssetConverter/src/AssetConverter/Program.cs:1-6,140-220`
- Create: `tools/AssetConverter/tests/AssetConverter.Tests/Terrain/TerrainCommandTests.cs`

**Mutation impact:**
- Source of truth changed: converter command routing and `all` workflow; both publish the same generated catalog path.
- Important readers: shell users, `.gitignore` regeneration guidance (`.gitignore:26-29`), release asset generation, and the future editor.
- Derived/cached state affected: none across commands. Both routes instantiate the same generator defaults per invocation.
- Required propagation sequence: for focused command, resolve repo root → `TerrainCommand.Execute` → `TerrainCatalogGenerator.Generate` → print one success summary plus sorted warnings. For `all`, complete sheets/maps/combined manifest (`Program.cs:146-189`) → call that exact same `TerrainCommand.Execute` method → continue existing summaries and include terrain summary.
- Failure behavior: missing/invalid inputs or publication failure propagate out of top-level Main for nonzero exit. `all` may already have generated other asset classes, but the previous terrain file remains unchanged and no false terrain success line prints.
- Invariants: `terrain` and `all` share no duplicated inference/settings/writer code; same generated inputs produce byte-identical catalog bytes; terrain executes only after the combined manifest in `all`; usage includes `terrain [repoRoot]`.
- Observable proof required: invoke the built CLI process against a synthetic root and inspect exit code/stdout/file bytes, then compare with direct shared-command regeneration.

`TerrainCommand` surface:

```csharp
public static class TerrainCommand
{
    public static TerrainGenerationResult Execute(string repoRoot, TextWriter output);
}
```

Success output is exactly:

```text
Terrain: <enabled> enabled, <pending> pending, <disabled> disabled -> <absolute output path>
Terrain fingerprint: <sha256 fingerprint>
```

Each root diagnostic follows as `  WARN <code>: <message>`, already sorted by generation. It writes no success line before atomic publication.

**Step 1: Write failing CLI tests**

Add:

- `Execute_SyntheticRoot_PrintsLockedSummaryAfterWritingCatalog`
- `TerrainProcess_SyntheticRoot_ExitsZeroAndWritesExpectedPath`
- `TerrainProcess_InvalidRoot_ExitsNonzeroAndPreservesPriorCatalog`
- `Execute_RepeatedAfterSameGeneratedInputs_IsByteIdentical`
- adversarial `AllRouting_CallsTerrainOnlyAfterManifestAndUsesSameCommand`.

For the process test, run `dotnet` with `typeof(TerrainCommand).Assembly.Location`, `terrain`, and the fixture root; capture stdout/stderr/exit code. The `all` ordering test should exercise an extracted internal ordering helper or command delegate seam, not launch proprietary conversion. Its fake must mirror `TerrainCommand.Execute(string, TextWriter)` semantics and assert the manifest exists before invocation. Keep actual `all` production code as one call to the real method.

**Step 2: Run red**

```bash
dotnet test tools/AssetConverter/tests/AssetConverter.Tests/AssetConverter.Tests.csproj \
  --filter 'FullyQualifiedName~TerrainCommand' -v minimal
```

Expected: FAIL because command routing/output does not exist.

**Step 3: Implement focused and `all` routing**

Add `using Goose2.AssetConverter.Terrain;`. Place the focused branch before `all`, with the same optional root resolution as `all`. In `all`, invoke terrain immediately after `FrameManifestBuilder.BuildCombined` has been completely written at current `Program.cs:185-189`; do not call it before maps or manifest and do not reimplement settings. Update the final usage string at current line 220.

Do not change unrelated converter command behavior or rewrite the whole top-level program.

**Step 4: Run automated final gates**

```bash
dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj -v minimal
dotnet test tools/AssetConverter/tests/AssetConverter.Tests/AssetConverter.Tests.csproj \
  --filter 'FullyQualifiedName!~Aspereta&FullyQualifiedName!~CombinedManifest_ContainsIllutiaAndRenumberedAsperetaSheets' -v minimal
dotnet build tools/AssetConverter/AssetConverter.sln -v minimal
dotnet build Goose2ClientGodot.sln -v minimal
git status --short
```

Expected: tests/builds pass, with only existing GifLoader nullable warnings; status lists source/test/plan changes and no generated `Assets/` files.

**Step 5: Run the local real-Illutia smoke outside the repository output**

This is manual evidence, not an automated test or committed artifact:

```bash
SMOKE_ROOT="$(mktemp -d)"
mkdir -p "$SMOKE_ROOT/Assets/Sprites" "$SMOKE_ROOT/Assets/Maps"
dotnet run --project tools/AssetConverter/src/AssetConverter -- batch "$SMOKE_ROOT/Assets/Sprites/sheets"
dotnet run --project tools/AssetConverter/src/AssetConverter -- maps "$SMOKE_ROOT/Assets/Maps"
dotnet run --project tools/AssetConverter/src/AssetConverter -- manifest "$SMOKE_ROOT/Assets/Sprites/manifest.json"
dotnet run --project tools/AssetConverter/src/AssetConverter -- terrain "$SMOKE_ROOT"
python3 - "$SMOKE_ROOT/Assets/Sprites/terrain-brushes.json" <<'PY'
import json, sys
with open(sys.argv[1], encoding="utf-8") as f:
    c = json.load(f)
sets = c["sets"]
print("fingerprint", c["corpusFingerprint"])
for status in ("enabled", "pending", "disabled"):
    selected = [s for s in sets if s["status"] == status]
    print(status, len(selected))
    for s in selected[:10]:
        m = s["metrics"]
        print(" ", s["id"], s["topology"], m["confidence"], m["holdoutAccuracy"], len(s["masks"]), [d["code"] for d in s["diagnostics"]])
PY
rm -rf "$SMOKE_ROOT"
```

Expected: the terrain command succeeds, parsing succeeds, at least one review candidate is reported, every enabled four-way/eight-way set has 16/47 nonempty masks respectively, and no enabled references overlap. Record candidate counts and representative diagnostic codes in the PR/commit notes. Threshold changes are allowed only in this task if the smoke reveals false auto-enables; keep them more conservative, update the locked defaults/snapshots/tests together, and rerun byte-determinism and synthetic topology gates. Absence of an auto-enabled real set is acceptable; a false enabled set is not.

The full `all` smoke is deferred on this workstation because its configured Aspereta paths do not exist. On a machine with both source corpora, run:

```bash
dotnet run --project tools/AssetConverter/src/AssetConverter -- all "$PWD"
cp Assets/Sprites/terrain-brushes.json /tmp/terrain-from-all.json
dotnet run --project tools/AssetConverter/src/AssetConverter -- terrain "$PWD"
cmp -s /tmp/terrain-from-all.json Assets/Sprites/terrain-brushes.json
```

Expected: `cmp` exits 0. Remove generated assets if they were created only for verification; they remain gitignored and must not be committed.

**Invariant-to-test matrix:**

| Invariant | Proved by |
|---|---|
| Focused command writes only after successful generation and reports exact result | `Execute_SyntheticRoot_*`, `TerrainProcess_SyntheticRoot_*` |
| CLI failure is nonzero and preserves the old file | `TerrainProcess_InvalidRoot_ExitsNonzeroAndPreservesPriorCatalog` |
| Focused and `all` share one implementation after manifest publication | adversarial `AllRouting_CallsTerrainOnlyAfterManifestAndUsesSameCommand` plus full-corpus `cmp` where data exists |
| Repeated command bytes are stable | `Execute_RepeatedAfterSameGeneratedInputs_IsByteIdentical` |
| Real corpus produces conservative, inspectable candidates | manual smoke counts/diagnostics and enabled completeness/overlap checks |

**Step 6: Commit**

```bash
git add tools/AssetConverter/src/AssetConverter/Program.cs \
  tools/AssetConverter/src/AssetConverter/Terrain/TerrainCommand.cs \
  tools/AssetConverter/tests/AssetConverter.Tests/Terrain/TerrainCommandTests.cs \
  docs/plans/2026-09-08-terrain-brush-part1-converter.md
git commit -m "feat: integrate terrain converter command"
```

## Red-team review before declaring Part 1 complete

Run this review after implementation and before the final commit:

- **Threading/lifecycle:** all work is synchronous on the converter process thread. Confirm no `Task.Run`, parallel LINQ, static mutable feature cache, or background publication was introduced. `TerrainFeatureCache` must dispose images on success and every exception path before atomic write.
- **Persistence/schema:** schema v1 is new generated JSON, not a database/map migration. Confirm `MapCodec.Encode` and map files are never called for mutation by production terrain code. Generated output remains ignored and uncommitted.
- **Publication boundary:** confirm source loading, feature extraction, inference, validation, and serialization complete before temp creation. Confirm destination is never deleted first and success output occurs only after move/replace.
- **Failure paths:** corrupt map/manifest/PNG, missing sheet, source-rect overflow/out-of-bounds, no eligible corpus, invalid enabled set, failed write/flush/replace/move, and failed cleanup all need deterministic behavior. Cleanup failure must not mask the original exception; best-effort cleanup may leave only a uniquely named temp, never a replaced/partially written destination.
- **Input completeness:** confirm fingerprint includes all maps, manifest bytes, and every relevant PNG exactly once, and excludes absolute root and file timestamps. Confirm a newly added relevant frame/sheet changes manifest/PNG records and therefore the fingerprint.
- **Determinism:** search for unsorted enumeration before every graph component, tie, hash, diagnostic list, variants list, set list, and JSON list. Repeat synthetic generation in separate roots/processes and compare bytes.
- **Inference leakage:** confirm held-out maps do not train masks/centroids, and image-only references do not count as map/region/observation support. Confirm giant maps/regions receive no extra total weight.
- **Conservatism:** adversarial deceptive lookalikes, near-tie image-only frames, sparse diagonal evidence, incomplete masks, and overlaps must never remain enabled.
- **Contract alignment:** compare every schema field and enum spelling against the locked representative JSON. Verify IDs change only for topology/member changes and every enabled set has all 16/47 masks with nonempty variants.
- **Dependency direction:** inspect project references: Core has no provider/ImageSharp/Rendering reference; converter references Core; Rendering continues to reference Core. No converter type leaks into the shared contract.
- **Test-helper reality:** fixture maps must be encoded with supported editor version 10 via `MapDocument.Create`, sheet PNGs must be real files at the generated path, and process tests must invoke the actual built converter DLL. Fakes implement only declared interfaces.
- **Environment isolation:** run new tests with proprietary directories temporarily unavailable or environment variables unset. No new automated test may depend on `/home/agent/workspace/Illutia` or `/home/hayden/...`.
- **Scope:** reject editor asset loading, runtime terrain resolution, painting, manager save, UI, or generated default data in this part. Those belong to Parts 2–3.

## Design alignment and known conflict

The plan covers every converter/catalog promise in the approved design: all schema fields, stable IDs, review states, provenance/diagnostics, 16/47 topology semantics, layer-0-only mining, same-sheet image-only search, weighted map/region evidence, deterministic holdout, conservative enablement, non-overlapping enabled sets, focused/`all` byte identity, and atomic failure preservation.

One repository/design integration conflict is explicit: `all` is the only approved combined-corpus producer, but this worktree cannot run it because `Paths.AsperetaData`/`Paths.AsperetaMaps` default to absent `/home/hayden/...` locations while `Program.cs:153-189` unconditionally loads that corpus. The implementation must not weaken `all` or silently fall back to Illutia-only data. Synthetic tests prove routing and determinism; the Illutia-only focused smoke tunes conservatism locally; the final `all`/`terrain` byte comparison must be run where both configured corpora exist.

A second contract tension is resolved deliberately: `MapEditor.Rendering` already owns `SpriteReference` and strict manifest loading, but the design assigns terrain definitions to Core. Core therefore introduces `TerrainGraphicReference` instead of referencing or moving Rendering’s type. Part 2 will adapt the two same-shaped values at the asset boundary without reversing project dependencies.

Plan complete and saved to `docs/plans/2026-09-08-terrain-brush-part1-converter.md`. Ready to implement.
