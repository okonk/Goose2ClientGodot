# Terrain Brush Part 1: Converter and Catalog Contract Implementation Plan

**Goal:** Add the shared terrain-catalog contract and a deterministic converter pipeline that mines generated maps and 32×32 sprite frames, infers conservative four-way/eight-way terrain candidates, and atomically emits `Assets/Sprites/terrain-brushes.json` from both `terrain` and `all`.

**Architecture:** `MapEditor.Core` owns the provider-neutral schema, mask semantics, stable-ID derivation, structural parsing, canonical serialization, and catalog validation. `AssetConverter` references Core, reads a declared generated-map inventory plus the manifest and relevant sheets in bounded batches, computes exact image/evidence descriptors, trains independently from deterministic holdouts, and separates top-1 evaluation predictions from emitted variant lists. The synchronous generator builds and validates the complete catalog before a durable sibling-temp write and same-directory replacement.

**Tech Stack:** C#; .NET 8 `MapEditor.Core`; .NET 10 `AssetConverter`; `System.Text.Json`; `System.Security.Cryptography`; SixLabors.ImageSharp 3.1.12; xUnit 2.9.x.

---

> Implement with @executing-plans, one task and commit at a time. Follow `AGENTS.md`: add no comments/doc strings unless a non-obvious invariant genuinely requires one.

## Scope, sequencing, and cross-part ownership

This is Part 1 of 3. It includes only:

- provider-neutral terrain definitions, mask rules, stable IDs, typed JSON parsing, canonical serialization, and catalog validation used by all parts;
- a generated-map inventory so stale files in `Assets/Maps` cannot silently enter the corpus;
- strict layer-0 map/manifest/PNG discovery and exact corpus/holdout hashing;
- deterministic 32×32 RGBA feature extraction, same-sheet buckets, weighted candidate inference, image-only expansion, topology evaluation, scoring, diagnostics, and review status;
- complete in-memory generation followed by durable same-directory atomic replacement;
- the focused `terrain [repoRoot]` command and production `all` finalization after map inventory and combined manifest publication;
- synthetic tests and an optional local real-corpus calibration smoke that never becomes a proprietary-data dependency.

It excludes runtime resolution and map edits (Part 2), and Rendering asset validation/degradation, palettes, gestures, and the Terrain Sets manager (Part 3). Manifest frame existence and 32×32 checks at editor load time belong to Part 3; the converter independently guarantees its own generated members here. `MapEditTool.Terrain` and all Core editing changes belong to Part 2.

No map schema or database changes are involved. `terrain-map-inputs-v1.txt` is one converter-owned generated corpus inventory, not map metadata or a per-map sidecar. `terrain-brushes.json` is a new generated schema-v1 file. Both live under ignored `Assets/`; no migration is needed, and regeneration intentionally replaces manager reviews.

Tasks remain independently executable in dependency order: Task 0 establishes the shared contract; Tasks 1–4 add read-only inference stages; Task 5 composes and publishes them; Task 6 alone changes command routing and existing map-converter result reporting.

## APIs and repository facts verified before planning

| API / fact | Citation and consequence |
|---|---|
| Core owns definitions/topology/editing; converter analyzes layer 0; Rendering/App own asset loading and UI | `docs/plans/2026-09-08-terrain-brush-design.md:38-46`. Keep JSON types in Core, ImageSharp in Converter, runtime editing in Part 2, and asset/UI work in Part 3. |
| Generated data requires versions, fingerprint, settings, IDs/names/status, metrics, mappings, provenance, and diagnostics | `docs/plans/2026-09-08-terrain-brush-design.md:48-66`. Every field and wire spelling is locked below. |
| The design requires weighted hybrid inference, held-out 4/8 selection, conservative status, deterministic focused/`all` output, and atomic replacement | `docs/plans/2026-09-08-terrain-brush-design.md:68-82`. Training isolation, evaluation prediction, sorting, and publication need explicit tests. |
| Converter tests must be synthetic and cover topology, variants/lookalikes, sparse corners, image-only inference, weighting, thresholds, IDs, determinism, and failure preservation | `docs/plans/2026-09-08-terrain-brush-design.md:138-142`. Proprietary data is smoke/calibration only. |
| `MapCodec.Decode(ReadOnlySpan<byte>)` validates supported editor versions/dimensions/minimum length, ignores trailers, and returns `MapDocument` | `src/MapEditor.Core/MapCodec.cs:15-69`; versions/dimension limits are `src/MapEditor.Core/MapDocument.cs:72-81`. Use it; wrap its typed format failures rather than duplicating the wire parser. |
| `MapDocument` exposes coordinate and row-major reads; each `MapTile` exposes `GetLayer(int)` | `src/MapEditor.Core/MapDocument.cs:169-184` and `src/MapEditor.Core/MapDocument.cs:36-47`. Mining reads only layer 0 and never mutates decoded documents. |
| Synthetic maps can use `MapDocument.Create`, `SetLayer`, and `MapCodec.Encode` | `src/MapEditor.Core/MapDocument.cs:107-120,193-202`; `src/MapEditor.Core/MapCodec.cs:80-120`. Do not copy the unsupported editor-version-1 fixture in `tools/AssetConverter/tests/AssetConverter.Tests/TileSheetGeneratorTests.cs:110-125`. |
| The converter manifest wire shape is `{tileSize:32,sheets:{sheet:{graphic:[x,y,w,h]}}}` | `tools/AssetConverter/src/AssetConverter/Manifest/FrameManifestBuilder.cs:10-16,23-37`. Parse with duplicate/alias detection rather than lossy dictionary deserialization. |
| Existing Rendering parsing demonstrates duplicate-aware traversal, exact 32 tile size, checked rectangles, sorted frames, and signed canonical integer handling | `src/MapEditor.Rendering/Assets/SpriteManifest.cs:64-127,129-259,262-327`. Do not reference Rendering from Converter; mirror the wire rules in a converter-internal index. |
| Generated sheet files are `<sheet>.png` | `tools/AssetConverter/src/AssetConverter/BatchConverter.cs:14-17,40-42`; current CLI output paths are `tools/AssetConverter/src/AssetConverter/Program.cs:146-147`. |
| `Graphic == 0` is the empty sentinel regardless of sheet | `src/MapEditor.Rendering/Assets/SpriteReference.cs:3-6`; converted Aspereta maps write `(0,0)` for empty at `tools/AssetConverter/src/AssetConverter/Aspereta/AsperetaMapConverter.cs:41-57`. Exclude graphic zero from observations and image-only search even if manifest graphic 0 exists. |
| Current `all` writes Illutia maps, then Aspereta maps, then the combined manifest, and returns after summaries | `tools/AssetConverter/src/AssetConverter/Program.cs:140-204`. Production finalization must write the successful-map inventory and manifest before terrain generation. |
| Existing map converter results report counts/failures but not successful output names | `tools/AssetConverter/src/AssetConverter/Maps/MapCopyConverter.cs:3-4,12-49`; `tools/AssetConverter/src/AssetConverter/Aspereta/AsperetaMapConverter.cs:3-9,18-67`. Task 6 extends these results so `all` can declare exact map inputs without globbing stale output. |
| Existing focused commands and `all` use optional output/root arguments | `tools/AssetConverter/src/AssetConverter/Program.cs:119-148,207-220`. Preserve existing argument meanings and add `terrain [repoRoot]`. |
| Converter is net10 while Core is net8 | `tools/AssetConverter/src/AssetConverter/AssetConverter.csproj:3-12`; `src/MapEditor.Core/MapEditor.Core.csproj:1-5`. Add a normal project reference; do not copy Core types. |
| Rendering already references Core | `src/MapEditor.Rendering/MapEditor.Rendering.csproj:1-9`. Core gains no ImageSharp, file-system root, Rendering, Avalonia, or Godot dependency. |
| Existing atomic output has `Exists/CreateFile/Replace/Move/Delete`, but calls only `Stream.Flush()` and cleanup can mask a primary exception | `tools/AssetConverter/src/AssetConverter/Manifest/AppearanceManifestFileStore.cs:5-12,18-51`. The new independent terrain seam adds explicit durable flush and primary-exception preservation rather than copying those gaps. |
| Existing failure tests cover write/flush/replace/move and temp cleanup using exact file-operation fakes | `tools/AssetConverter/tests/AssetConverter.Tests/AppearanceManifestBuilderTests.cs:197-350`. Terrain tests mirror only the new interface declared below. |
| Generated assets are ignored and regenerated by `all` | `.gitignore:21-30`. Do not commit map inventory, sheets, maps, or terrain JSON. |
| AssetConverter tests target net10 and already reference the converter executable | `tools/AssetConverter/tests/AssetConverter.Tests/AssetConverter.Tests.csproj:3-24`. Internals can be exposed to this existing test project without a new project. |

Planning baseline in this worktree is .NET SDK `10.0.400`. New automated tests and every mandatory commit gate must be synthetic and independent of the configured Illutia/Aspereta paths. Illutia-only and combined proprietary-corpus runs are explicitly optional manual calibration smokes and are skipped when their inputs are unavailable.

## Locked Part 1 → Parts 2/3 catalog contract

Use namespace `MapEditor.Core.Terrain`. The public shape is:

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

public sealed record TerrainMaskDefinition
{
    public int Mask { get; }
    public IReadOnlyList<TerrainGraphicReference> Variants { get; }
    public TerrainMaskDefinition(int mask, IEnumerable<TerrainGraphicReference> variants);
}

public sealed record TerrainMemberDefinition
{
    public TerrainGraphicReference Reference { get; }
    public TerrainMemberProvenance Provenance { get; }
    public TerrainMemberDefinition(
        TerrainGraphicReference reference,
        TerrainMemberProvenance provenance);
}

public sealed record TerrainSetDefinition
{
    public string Id { get; }
    public string DisplayName { get; }
    public TerrainReviewStatus Status { get; }
    public TerrainTopology Topology { get; }
    public TerrainSetMetrics Metrics { get; }
    public IReadOnlyList<TerrainMaskDefinition> Masks { get; }
    public IReadOnlyList<TerrainMemberDefinition> Members { get; }
    public IReadOnlyList<TerrainDiagnostic> Diagnostics { get; }

    public TerrainSetDefinition(
        string id,
        string displayName,
        TerrainReviewStatus status,
        TerrainTopology topology,
        TerrainSetMetrics metrics,
        IEnumerable<TerrainMaskDefinition> masks,
        IEnumerable<TerrainMemberDefinition> members,
        IEnumerable<TerrainDiagnostic> diagnostics);
}

public sealed record TerrainCatalog
{
    public int SchemaVersion { get; }
    public string GeneratorVersion { get; }
    public string CorpusFingerprint { get; }
    public TerrainGenerationSettings Settings { get; }
    public IReadOnlyList<TerrainSetDefinition> Sets { get; }
    public IReadOnlyList<TerrainDiagnostic> Diagnostics { get; }

    public TerrainCatalog(
        int schemaVersion,
        string generatorVersion,
        string corpusFingerprint,
        TerrainGenerationSettings settings,
        IEnumerable<TerrainSetDefinition> sets,
        IEnumerable<TerrainDiagnostic> diagnostics);
}

public sealed record TerrainDiagnostic
{
    public string Code { get; }
    public string Message { get; }
    public int? Mask { get; }
    public TerrainGraphicReference? Reference { get; }

    public TerrainDiagnostic(
        string code,
        string message,
        int? mask = null,
        TerrainGraphicReference? reference = null);
}

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

public enum TerrainCatalogError
{
    MalformedJson,
    InvalidRoot,
    UnsupportedSchemaVersion,
    MissingProperty,
    DuplicateProperty,
    UnknownProperty,
    InvalidPropertyType,
    NullNotAllowed,
    InvalidEnum,
    NumberOutOfRange,
    InvalidRelationship
}

public sealed class TerrainCatalogException : FormatException
{
    public TerrainCatalogError Error { get; }
    public string SourcePath { get; }

    public TerrainCatalogException(
        TerrainCatalogError error,
        string sourcePath,
        string message,
        Exception? inner = null);
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

The collection-bearing records have explicit constructors and get-only properties:

```text
TerrainMaskDefinition(mask, variants)
TerrainMemberDefinition(reference, provenance)
TerrainSetDefinition(id, displayName, status, topology, metrics, masks, members, diagnostics)
TerrainCatalog(schemaVersion, generatorVersion, corpusFingerprint, settings, sets, diagnostics)
```

The declarations above are the exact signatures; constructor bodies perform the copies rather than using positional collection properties.

Every constructor rejects null required arguments/elements and copies each incoming collection into a private array exposed through a read-only wrapper. Copy at every nested boundary, not only in `Parse`; mutating a caller list after construction must not alter any catalog value. Scalar records are immutable values. This is the immutability contract consumed by Part 2’s reusable resolver and Part 3’s asset catalog; `IReadOnlyList<T>` alone is not treated as proof of immutability.

### Stable IDs and masks

`TerrainGeneratedId.Create` materializes input once, rejects empty input and any duplicate, sorts numeric `(Sheet, Graphic)`, encodes exactly `four-way|sheet:graphic,...` or `eight-way|sheet:graphic,...` as UTF-8 without BOM/LF, hashes with SHA-256, and returns `terrain-4-<64 lowercase hex>` or `terrain-8-<64 lowercase hex>`. Name, review status, metrics, mask/variant order, and diagnostics do not affect the ID; topology or the effective member set does. Part 3 must immediately recompute the authored/generated ID when topology or effective membership changes while retaining a separate immutable draft key for manager identity and selection.

Four-way normalization first masks to `0x0F`. Eight-way normalization first masks to `0xFF`, then removes NE unless N and E are both set, SE unless S and E are set, SW unless S and W are set, and NW unless N and W are set. Unknown `TerrainTopology` values throw `ArgumentOutOfRangeException`. `Required` returns a non-mutable numeric-ascending list containing exactly 16 or 47 values.

Coordinate convention is fixed across converter and later resolver work: `(0,0)` is top-left; `x` increases east/right; `y` increases south/down. Neighbor offsets are N `(0,-1)`, E `(+1,0)`, S `(0,+1)`, W `(-1,0)`, NE `(+1,-1)`, SE `(+1,+1)`, SW `(-1,+1)`, NW `(-1,-1)`. Outside-map coordinates and nonfamily references are absent bits.

### JSON syntax, nulls, ranges, and canonical order

All JSON property names are exact camel case. Enum strings are exactly `four-way`/`eight-way`, `enabled`/`pending`/`disabled`, and `map-observed`/`image-only`, case-sensitive. Every listed property is required at every object level. Unknown and duplicate properties are rejected at every fixed-shape object. Manifest-style dynamic IDs are not part of this catalog schema.

Only `diagnostic.mask` and `diagnostic.reference` may be JSON `null`; both properties are still emitted. No string, object, array, array element, metric, setting, member reference, or variant reference may be null. Empty `sets` and diagnostic arrays parse; empty member/mask/variant arrays are left to status-aware validation. JSON numbers must be JSON numbers of the required integer/double kind; numeric strings and non-finite values are rejected.

Parser failures always throw `TerrainCatalogException`, preserve `SourcePath`, and use these categories. At each object, enumerate wire properties once: the first unknown property or second occurrence throws immediately; after enumeration, report the first missing property in the canonical property order; then validate values in canonical property order, depth-first and array order. A forbidden null is reported before wrong-kind validation. At the root, validate `schemaVersion` before other property values so a complete unsupported document reports `UnsupportedSchemaVersion`.

- invalid JSON syntax → `MalformedJson`;
- nonobject root → `InvalidRoot`;
- missing/duplicate/unknown property → the corresponding category;
- wrong JSON kind or noninteger integer field → `InvalidPropertyType`;
- forbidden null → `NullNotAllowed`;
- unknown enum spelling → `InvalidEnum`;
- schema other than 1 → `UnsupportedSchemaVersion`;
- scalar outside the ranges below → `NumberOutOfRange`;
- a cross-setting relationship failure → `InvalidRelationship`.

`TerrainGenerationSettings` requires `HoldoutModulo >= 2`; all four support minima are `>= 1`; `MinimumEightWayAccuracyGain` and `MinimumClassificationMargin` are finite in `(0,1]`; all other double settings are finite in `[0,1]`; `ImageOnlyCompatibility >= MapMemberCompatibility`; and `PendingConfidence <= EnabledConfidence`. Metrics require nonnegative integer supports, all normalized values in `[0,1]`, and `EightWayAccuracyGain` in `[-1,1]`. This corrects the tempting but invalid blanket `[0,1]` rule for a signed accuracy difference. `Serialize(null)` throws `ArgumentNullException`; otherwise serialization applies the same enum/scalar/relationship checks as parsing and throws `TerrainCatalogException` with `SourcePath == "<object>"` before writing invalid structural values. Semantic catalog issues remain the validator/caller's responsibility.

Canonical serialization is compact UTF-8 JSON represented by a .NET string, with exactly one trailing LF and no BOM, timestamp, or absolute path. Numbers use invariant JSON formatting; generator metrics are rounded to six decimal places, midpoint-to-even, before object construction. Property order is exact:

```text
root:       schemaVersion, generatorVersion, corpusFingerprint, settings, diagnostics, sets
settings:   holdoutModulo, minimumMapSupport, minimumRegionSupport,
            minimumObservationSupport, minimumDiagonalSupport,
            minimumEightWayAccuracyGain, mapMemberCompatibility,
            imageOnlyCompatibility, minimumClassificationMargin,
            maximumAmbiguity, enabledConfidence, pendingConfidence
set:        id, displayName, status, topology, metrics, masks, members, diagnostics
metrics:    mapSupport, regionSupport, observationSupport, diagonalSupport,
            maskEntropy, completeness, ambiguity, visualCompatibility,
            holdoutAccuracy, eightWayAccuracyGain, confidence
mask:       mask, variants
member:     reference, provenance
diagnostic: code, message, mask, reference
reference:  sheet, graphic
```

The serializer canonicalizes root/set diagnostics by the ordering below, sets by ID ordinal, masks numeric ascending, and members by numeric reference. It preserves variant order because Part 2 selection and Part 3 manager reordering make that order semantically significant. Parse preserves variant order and defensively copies everything; parse→serialize canonicalizes only nonsemantic ordering.

### Catalog validation issue contract

Parsing is structural and range-aware; status-aware/catalog-wide validation is separate. `TerrainCatalogValidator.Validate` never throws for semantic invalidity and returns fresh read-only issues in this order: `(TerrainId null first ordinal, Mask null first numeric, Reference null first then Sheet then Graphic, Code ordinal, Message ordinal)`. Exact duplicate issues are removed by all five fields.

Machine codes and emission are:

| Code | Emission |
|---|---|
| `catalog-schema-version-invalid` | programmatically built schema version is not current version 1 |
| `catalog-generator-version-required` | generator version is blank |
| `catalog-fingerprint-invalid` | fingerprint is not `sha256:` plus 64 lowercase hex digits |
| `setting-out-of-range` | a programmatically built setting violates the scalar ranges above |
| `setting-relationship-invalid` | either locked setting relationship fails |
| `terrain-id-required` | set ID is blank |
| `terrain-id-duplicate` | the same nonblank ID occurs more than once; emit for each occurrence |
| `terrain-id-mismatch` | ID differs from `TerrainGeneratedId.Create(topology, distinct members)`; suppress when member input is empty/duplicate so the primary member issue is sufficient |
| `terrain-display-name-required` | display name is blank |
| `terrain-topology-invalid` | programmatically built topology enum is unknown |
| `terrain-status-invalid` | programmatically built review-status enum is unknown |
| `metric-out-of-range` | a programmatically built metric violates its range |
| `member-required` | set has no members |
| `member-provenance-invalid` | programmatically built provenance enum is unknown |
| `member-graphic-zero` | any member has `Graphic == 0` |
| `member-sheet-out-of-range` | member sheet is outside signed Int16 range used by `MapCodec.Encode` |
| `member-duplicate` | duplicate member in one set |
| `mask-duplicate` | duplicate mask in one set |
| `mask-unreachable` | mask is not normalized/reachable for the set topology |
| `variant-duplicate` | duplicate reference within one mask list |
| `variant-not-member` | variant is absent from the member list |
| `enabled-member-unused` | enabled member appears in no variant list |
| `enabled-mask-missing` | enabled set omits a required mask |
| `enabled-mask-empty` | enabled required mask has no variant |
| `enabled-member-conflict` | a reference belongs to two or more enabled sets; emit one issue per owner with all sorted owner IDs in the message |
| `diagnostic-code-required` | root or set diagnostic code is blank |
| `diagnostic-message-required` | root or set diagnostic message is blank |

Issue messages are exact:

```text
catalog-schema-version-invalid: Catalog schema version <n> is not supported; expected 1.
catalog-generator-version-required: Catalog generator version is required.
catalog-fingerprint-invalid: Catalog corpus fingerprint must be 'sha256:' plus 64 lowercase hex digits.
setting-out-of-range: Setting '<camelName>' value <v> is outside <range>.
setting-relationship-invalid: Setting '<left>' must be <relation> setting '<right>'.
terrain-id-required: Terrain ID is required.
terrain-id-duplicate: Terrain ID '<id>' occurs more than once.
terrain-id-mismatch: Terrain '<id>' does not match generated ID '<expected>'.
terrain-display-name-required: Terrain '<id>' display name is required.
terrain-topology-invalid: Terrain '<id>' topology value <n> is invalid.
terrain-status-invalid: Terrain '<id>' review status value <n> is invalid.
metric-out-of-range: Terrain '<id>' metric '<camelName>' value <v> is outside <range>.
member-required: Terrain '<id>' must contain at least one member.
member-provenance-invalid: Terrain '<id>' member (s,g) provenance value <n> is invalid.
member-graphic-zero: Terrain '<id>' member (s,g) uses reserved graphic 0.
member-sheet-out-of-range: Terrain '<id>' member (s,g) sheet is outside Int16 range.
member-duplicate: Terrain '<id>' contains duplicate member (s,g).
mask-duplicate: Terrain '<id>' contains duplicate mask 0xNN.
mask-unreachable: Terrain '<id>' mask 0xNN is not reachable for <topology>.
variant-duplicate: Terrain '<id>' mask 0xNN contains duplicate variant (s,g).
variant-not-member: Terrain '<id>' mask 0xNN variant (s,g) is not a member.
enabled-member-unused: Enabled terrain '<id>' member (s,g) is unused.
enabled-mask-missing: Enabled terrain '<id>' is missing mask 0xNN.
enabled-mask-empty: Enabled terrain '<id>' mask 0xNN has no variants.
enabled-member-conflict: Enabled terrain '<id>' member (s,g) is shared by [<quoted sorted IDs>].
diagnostic-code-required: Diagnostic code is required.
diagnostic-message-required: Diagnostic message is required.
```

`<v>` uses invariant `G17` (`NaN`, `Infinity`, and `-Infinity` literally when validating a programmatic value), `<n>` is invariant decimal, `<range>` is the mathematical range written in the JSON section, `<topology>` is the wire spelling when valid, and `(s,g)`/`0xNN` use actual invariant integers/uppercase hex. The relationship text is `be greater than or equal to` for image/map compatibility and `be less than or equal to` for pending/enabled confidence. Conflict IDs render as comma-separated individually quoted IDs. Blank IDs use `'<blank>'` when needed by another issue. Pending/disabled sets may be incomplete, have empty required masks, overlap each other, and retain unused members without issues; duplicate/unreachable/zero/wiring errors remain invalid regardless of status. Manifest resolution is not a Core concern. Part 3 adds enabled-only manifest/exact-32 checks while retaining parsed pending/disabled review data.

### Persisted generator diagnostics contract

Scope is represented by containment: root diagnostics describe excluded corpus/family input; `set.diagnostics` describe one emitted candidate. `mask` and `reference` are explicitly null when not applicable. Generator messages never contain absolute paths. Map identity is the quoted normalized repository-relative identity. Generated numbers are invariant fixed six decimals; references and masks use the issue formatting above.

Diagnostics are exact-deduplicated by `(code, message, mask, reference)` within their scope, then ordered by `(code ordinal, mask null first/numeric, reference null first/sheet/graphic, message ordinal)`. A diagnostic is emitted once per condition at the scope/key described below:

| Scope/code | Emission and exact message template |
|---|---|
| root `missing-manifest-reference` | once per map/reference excluded: `Map '<map>' references (s,g), which is absent from manifest; placements were excluded.` |
| root `unsupported-frame-size` | once per map-used reference excluded: `Frame (s,g) is <w>x<h>; expected 32x32; placements were excluded.` |
| root `insufficient-family-members` | once per training reference left in a singleton component: `Map-observed reference (s,g) did not join a family with at least two members.` |
| root `heldout-only-member-excluded` | once per eligible reference observed only in held-out maps: `Map-observed reference (s,g) has no training-family owner and was not considered image-only.` |
| set `image-only-member-admitted` | once per admitted member/reference/mask: `Admitted image-only (s,g) at mask 0xNN: compatibility <v>, owner margin <v>.` |
| set `image-only-ambiguous` | once per near-threshold rejected frame on its unique best candidate, or on every exactly tied best candidate: `Rejected image-only (s,g): compatibility <v>, owner margin <v>.` |
| set `image-only-centroids-missing` | once when a family cannot classify any expansion because a required connected/disconnected side centroid is absent: `Image-only expansion skipped because connected/disconnected edge centroids are incomplete.` |
| set `no-training-observations` | model has no training observations: `No training observations were available.` |
| set `no-holdout-observations` | model has no eligible held-out observations: `No held-out observations were available; holdout accuracy is 0.000000.` |
| set `eight-way-evidence-insufficient` | eight-way is not selected despite at least one diagonal trial: `Eight-way not selected: diagonal support <n>, diagonal maps <n>, four-way accuracy <v>, eight-way accuracy <v>, required gain <v>.` |
| set `support-below-minimum` | any support gate fails: `Support <maps>/<regions>/<observations> is below required <maps>/<regions>/<observations>.` |
| set `incomplete-required-masks` | selected model is incomplete: `Completeness <v> leaves <n> required masks without variants.` |
| set `ambiguity-above-maximum` | ambiguity gate fails: `Ambiguity <v> exceeds maximum <v>.` |
| set `holdout-accuracy-below-enabled` | holdout enable gate fails: `Holdout accuracy <v> is below enabled threshold <v>.` |
| set `confidence-below-enabled` | pending candidate is below enabled confidence: `Confidence <v> is below enabled threshold <v>.` |
| set `confidence-below-pending` | disabled candidate is below pending confidence: `Confidence <v> is below pending threshold <v>.` |
| set `enabled-member-conflict` | overlap loser is demoted: `Demoted from enabled because (s,g) is owned by higher-ranked terrain '<id>'.` |
| set `member-frame-invalid` | a final member's frame is missing or not a 32x32 tile: `Member frame (s,g) is missing or not a 32x32 tile.` |

In these templates `(s,g)` is replaced by the actual invariant sheet/graphic integers, `<v>` is `value.ToString("F6", InvariantCulture)`, and `<n>` is invariant decimal. Root aborts (missing inventory, malformed map/manifest, unsupported tile size, missing/corrupt relevant PNG, out-of-bounds rect, no maps, no eligible placements) are typed exceptions, not persisted diagnostics. Every admitted image-only member has `image-only-member-admitted` review evidence. The builder asserts every pending/disabled set has at least one of the status-explanation codes `no-training-observations`, `no-holdout-observations`, `eight-way-evidence-insufficient`, `support-below-minimum`, `incomplete-required-masks`, `ambiguity-above-maximum`, `holdout-accuracy-below-enabled`, `confidence-below-enabled`, `confidence-below-pending`, `enabled-member-conflict`, or `member-frame-invalid`.

## Locked generated-map, fingerprint, and holdout inputs

### Generated map inventory and map identity

The converter-owned file is `<repoRoot>/Assets/Maps/terrain-map-inputs-v1.txt`. Its internal API is `TerrainMapInventory.Read(string repoRoot)` and `TerrainMapInventory.Write(string mapsDirectory, IEnumerable<string> outputFileNames)`; `TerrainCorpusLoader.Load(string repoRoot)` consumes `Read`. Its exact UTF-8/no-BOM/LF format is:

```text
terrain-map-inputs-v1
Map1.map
Map2.map
```

The header and at least one file line are required. File names are ordinal-sorted, unique, bare names ending in `.map`, and may not be rooted, contain `/` or `\`, equal `.`/`..`, or escape `Assets/Maps`. Each listed file must exist as a regular file. The map identity used for diagnostics and holdout is exactly `Assets/Maps/<fileName>` with `/`, regardless of host separator/root. Unlisted `.map` files are stale/irrelevant and must not be read, diagnosed, or fingerprinted.

Task 6 extends successful map conversion results with sorted output file names. `maps` writes an Illutia-only inventory for its output directory; `aspereta` writes an Aspereta-only inventory for its output; `all` writes the sorted distinct union of both successful result lists. The inventory is sibling-temp/replace written before terrain runs. A conversion failure is absent from the inventory and remains in existing CLI failures. This avoids destructive directory cleanup and makes stale generated maps explicit.

### Corpus fingerprint byte layout

The fingerprint is `sha256:<64 lowercase hex>` over this exact byte stream:

```text
ASCII "terrain-corpus-v1\0"
record inventory
records maps in map-identity ordinal order
record manifest
records relevant sheets in numeric sheet order
```

Each record is:

```text
1 byte kind: 0x01 inventory, 0x02 map, 0x03 manifest, 0x04 sheet
UInt32 big-endian UTF-8 path-byte count
path UTF-8 bytes, no terminator
UInt64 big-endian content-byte count
exact file bytes
```

Record paths are respectively `Assets/Maps/terrain-map-inputs-v1.txt`, the map identity, `Assets/Sprites/manifest.json`, and `Assets/Sprites/sheets/<canonical-sheet>.png`. A relevant sheet is one containing at least one manifest-resolved exact-32 frame used by an eligible layer-0 placement. Each relevant sheet is recorded once. Absolute roots, timestamps, enumeration order, diagnostics, and settings are excluded. Therefore an unlisted stale map, an unreferenced sheet PNG, root relocation, timestamp change, or settings-only change does not change the corpus fingerprint; any inventory byte, listed map byte, manifest byte, or relevant PNG byte change does. A manifest change is relevant as a whole even if the changed property names an otherwise unused frame.

Use streaming/incremental SHA-256 for inventory/manifest/PNGs and at most one listed map byte array at a time for `MapCodec.Decode`; never concatenate or retain all file bytes.

### Holdout byte layout

For map identity UTF-8 bytes `p`, hash exactly:

```text
ASCII "terrain-holdout-v1\0"
UInt32 big-endian p.Length
p
```

Read digest bytes 0–7 as unsigned UInt64 big-endian. The map is held out iff that value modulo `HoldoutModulo` is zero. The fixture builder must find deterministic training/holdout names by this public rule rather than hard-code machine-dependent names.

## Locked image descriptor, distances, and buckets

Only manifest frames with `Graphic != 0` and rectangle exactly 32×32 are feature candidates. For source pixel bytes `(R,G,B,A)`, all sums use integer arithmetic in row-major order and convert to `double` only at the final denominator. Transparent RGB never contributes because color terms are multiplied by alpha.

Define per-pixel normalized channels:

```text
pr = R*A / 65025
pg = G*A / 65025
pb = B*A / 65025
pa = A / 255
l  = (54*R + 183*G + 19*B)*A / (256*65025)
```

The five descriptor groups are:

1. **Alpha occupancy:** 64 row-major 4×4 cells. Each is `sum(A)/(16*255)`.
2. **Palette:** 65 bins. For each pixel, add `A` to opaque-color bin `(R >> 6)*16 + (G >> 6)*4 + (B >> 6)` and add `255-A` to bin 64. Divide every bin by `32*32*255`; the vector sums to 1.
3. **Perceptual cells:** for the same 64 row-major 4×4 cells, store mean `(pr,pg,pb,pa,l)` in that order, producing 320 values.
4. **Corners:** NW `[0,8)×[0,8)`, NE `[24,32)×[0,8)`, SE `[24,32)×[24,32)`, SW `[0,8)×[24,32)`, in that order. Store each region’s mean `(pr,pg,pb,pa,l)`, producing 20 values.
5. **Directional edges:** N, E, S, W in that order. Each side has depth `d=0..3`, then edge segment `s=0..7`, then `(pr,pg,pb,pa,l)`. A segment averages four pixels: N uses `y=d,x=4s..4s+3`; E uses `x=31-d,y=4s..4s+3`; S uses `y=31-d,x=4s..4s+3`; W uses `x=d,y=4s..4s+3`. This produces 160 values/side and 640 total.

No gamma transform, color-space conversion, resizing, DCT, SIMD reduction, or platform image transform is allowed. This exact simple descriptor satisfies the design’s alpha/palette/perceptual/corner/edge needs without adding an uncalibrated perceptual pipeline.

The 64-bit perceptual hash uses the 64 luminance cells. Keep each cell's pre-division Int64 luminance numerator; bit `i = 8*y+x` is 1 iff `cellNumerator*64 > sum(allCellNumerators)`, with bit 0 the least-significant bit. Equality is 0. Hash bands are numeric bits 0–15, 16–31, 32–47, 48–63 in that order. All descriptor accumulators are checked Int64 values.

Component distances are:

```text
alpha      = sum(abs(a-b)) / 64
palette    = sum(abs(a-b)) / 2
perceptual = sum(abs(a-b)) / 320
corner     = sum(abs(a-b)) / 20
edge       = sum(abs(a-b)) / 640
```

Each is clamped to `[0,1]` only after division. Component similarity is `1-distance`. Overall similarity is exactly `0.15*alpha + 0.20*palette + 0.25*perceptual + 0.15*corner + 0.25*edge`, with one final `[0,1]` clamp. No L2 normalization is applied because every coordinate and each distance denominator is already range-normalized.

Bucket metadata is exact:

- sheet ID;
- alpha decile `min(9, floor(meanAlpha*10))`, where `meanAlpha=sum(A)/(1024*255)`;
- two palette bin IDs with greatest normalized mass across all 65 bins, tie by lower bin ID;
- four hash bands above.

A query may compare only entries on the same sheet, with alpha decile difference `<=1`, at least one common dominant palette ID, and at least three equal corresponding hash bands. Results are distinct and numeric `(Sheet,Graphic)` sorted. Bucket filtering is only a recall/performance gate; compatibility thresholds still decide edges/admissions.

Hand-checkable fixtures lock the implementation:

| 32×32 fixture | Expected values |
|---|---|
| fully transparent with arbitrary RGB | all alpha/perceptual/corner/edge values 0; palette[64]=1 and all others 0; hash `0x0000000000000000`; decile 0; dominant bins 64 then 0 |
| opaque black | alpha cells 1; palette[0]=1; every five-channel cell/region/profile is `(0,0,0,1,0)`; hash 0; decile 9; dominant bins 0 then 1 |
| opaque white | alpha cells 1; palette[63]=1; every five-channel cell/region/profile is `(1,1,1,1,1)`; strict-mean hash 0; decile 9; dominant bins 63 then 0 |
| opaque vertical split, black columns 0–15/white 16–31 | palette[0]=palette[63]=0.5; perceptual columns 0–3 are black and 4–7 white; hash `0xF0F0F0F0F0F0F0F0`; all bands `0xF0F0`; corners NW/SW black and NE/SE white; N/S edge segments 0–3 black and 4–7 white; E all white; W all black |

Tests compare complete arrays for the first three fixtures, the stated selected indices for the split fixture, and prove transparent RGB noise is byte-for-byte feature-equal.

## Locked evidence, image-only, model, and scoring formulas

Generator version `terrain-v1` emits these deterministic initial defaults:

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

These are initial deterministic defaults, not claims of corpus calibration. Do not alter formulas or thresholds ad hoc while implementing a fixture.

### Candidate graph and populations

Partition maps before any graph, mask, centroid, or image admission work. The global set of references observed anywhere is used only to prevent a map-observed frame from being mislabeled `ImageOnly`; holdout adjacency, masks, region sizes, and labels never train a family.

Graph vertices are eligible map-observed references in training maps. Evaluate only distinct unordered pairs produced by the union of each vertex's locked bucket query. For a same-sheet pair A/B, an undirected edge exists when feature similarity is at least `MapMemberCompatibility` and either (a) at least one cardinal A/B adjacency occurs in each of two distinct training maps, or (b) one training map contains two cardinal A/B adjacency occurrences whose endpoint coordinate sets are disjoint. Occurrences are unordered cell pairs; repeated direction scans do not double count. Deterministic connected components, including transitive A–B–C bridges, with at least two vertices are provisional families. Singleton references receive root diagnostics.

For family F in training map m, flood-fill cardinally connected cells whose references are in F. Let `R_m` be region count and `n_r` placements in region r. Each placement in r has training weight:

```text
w(m,r,placement) = 1 / (R_m * n_r)
```

Thus each region has mass `1/R_m` and each map containing F has total mass 1. Holdout evaluation independently uses the same formula over trained-family references in each held-out map. All sums iterate map identity, region minimum row-major coordinate, then placement row-major coordinate and use `double`; nonfinite results throw before publication.

Persisted/raw supports use training data only:

- `MapSupport`: distinct training maps containing at least one family placement;
- `RegionSupport`: total cardinal family regions across those maps;
- `ObservationSupport`: total family placements across those maps;
- `DiagonalSupport`: total corner trials across training placements where both cardinal prerequisites for that corner are present; present and absent diagonal outcomes both count one trial.

Raw counts are diversity gates, not statistical weights. A giant map can increase observation/diagonal counts but has only weight 1, has one map support, and commonly one region support. It therefore cannot satisfy all enable gates by itself. Eight-way additionally requires diagonal trials in at least `MinimumMapSupport` distinct training maps, preventing one giant map from supplying its evidence gate.

For a topology t, observation mask is family membership at the eight fixed offsets followed by `TerrainMasks.Normalize(raw,t)`. Define weighted mass `W_t(reference,mask)` as the sum of training placement weights for that reference/mask.

### Emitted variants versus evaluation predictions

These are deliberately separate:

- **Emitted mapping:** each map-observed member is assigned to the mask with maximum `W_t(reference,mask)`; a tie is ambiguity evidence and is broken by smaller mask. Each admitted image-only member is assigned to its classified mask. A mask’s variant list contains all assigned members, map-observed first then image-only, each numeric-reference sorted.
- **Evaluation predictor:** for each mask, predict exactly one map-observed reference with maximum `W_t(reference,mask)`; tie by smaller numeric reference. A mask with no training mass predicts no reference.

For each held-out placement whose reference belongs to the trained map-observed family, prediction is correct only when it equals that single predicted reference for the held-out normalized mask. Membership in the emitted multi-variant list is not an accuracy hit. `HoldoutAccuracy_t` is `sum(holdout weights of correct placements) / sum(all eligible holdout weights)`; no denominator or no prediction yields 0 for those cases. Because each held-out map has total mass 1, a giant map cannot dominate accuracy.

This top-1 rule is required for topology selection: four-way cardinal merging can emit a superset of eight-way variants, but cannot claim every superset member as a correct prediction. A diagonal-distinct fixture must make four-way choose one tied/majority member while eight-way predicts the correct member per canonical mask and gains at least 0.05.

`EightWayAccuracyGain = HoldoutAccuracy_eight - HoldoutAccuracy_four`. Select eight-way iff all are true: `DiagonalSupport >= MinimumDiagonalSupport`, informative corner trials occur in at least `MinimumMapSupport` training maps, and gain `>= MinimumEightWayAccuracyGain`. Otherwise select four-way. If any diagonal trial existed, emit `eight-way-evidence-insufficient` with both accuracies/supports.

### Image-only expansion

Build family medoids/centroids from training map-observed members only. The map-observed universe across training and holdout is excluded from image-only search; a holdout-only reference gets `heldout-only-member-excluded` and is neither admitted nor used to train/evaluate another family. This provenance-only exclusion is the sole allowed holdout inventory use. Tests that alter held-out masks/adjacency while preserving its reference set must leave membership/centroids/admissions unchanged.

For each family, the visual medoid is the map-observed reference minimizing the weighted sum of `(1-overallSimilarity)` to other map-observed references, where each reference weight is its total training placement weight; tie by numeric reference. No map-observed members means no candidate.

For each N/E/S/W side, compute connected and disconnected arithmetic-mean centroids of that side’s 160-value descriptor, weighted by training placement weight and grouped by observed family cardinal bit. For each corner, compute present/absent centroids of its 5-value corner descriptor only from trials where both adjacent cardinal bits are present. A centroid with zero group weight is missing.

Consider the numeric-sorted union of bucket-query results obtained by querying from every map-observed family member, then keep only other exact-32, nonzero frames on that family’s sheet. Classification uses a snapshot of all provisional families before any admission. Side/corner centroid similarity is `1 - mean absolute distance` over the side's 160 or corner's 5 normalized values:

1. Overall owner score is similarity to each same-sheet family medoid. Families missing any connected/disconnected side centroid are ineligible owners.
2. Choose greatest score, tie by no owner (do not break by ID). Require score `>= ImageOnlyCompatibility`. A threshold-passing exact tie emits `image-only-ambiguous` on every tied family with owner margin 0 and admits nowhere.
3. If at least two eligible families exist and the best is unique, require best minus second-best `>= MinimumClassificationMargin`; a near tie emits only on that unique best family. If exactly one eligible family exists, runner-up score is defined as 0, so the strict absolute compatibility threshold remains the conservative gate.
4. For each side, compare candidate side descriptor similarity to connected versus disconnected centroid. Require absolute difference `>= MinimumClassificationMargin`; set the side bit from the closer centroid. Exact ties fail.
5. For a corner whose two predicted cardinal bits are present, require both present/absent corner centroids, require their similarity difference `>= MinimumClassificationMargin`, and set the diagonal from the closer centroid. Otherwise clear that diagonal without consulting corner centroids.
6. Normalize the raw prediction with eight-way rules and retain that canonical mask as classification evidence. Each fitted topology normalizes it again for its emitted mapping. Admit once to the unique owner; after topology selection, emit `image-only-member-admitted` with the selected emitted mask. A near-threshold (`score >= ImageOnlyCompatibility`) ownership/side/corner failure emits `image-only-ambiguous` on the best family; incomplete side centroid coverage emits one `image-only-centroids-missing` on that family. Frames with no owner below threshold produce no catalog noise.

A frame already map-observed anywhere, a manifest graphic 0, a non-32 frame, or a frame on another sheet is never admitted. Simultaneous snapshot ownership means a frame cannot be admitted by two families and one family’s additions cannot change another’s score.

Image-only members contribute to emitted completeness, visual compatibility, members, ID, and diagnostics. They contribute zero map/region/observation/diagonal support, zero training/holdout mass, zero entropy/ambiguity evidence, and no centroids/medoid selection.

### Metrics, status, conflict resolution, and ties

For selected topology with `K=16` or `47` and weighted training mask totals `q(mask)`:

- `MaskEntropy = 0` when total mass is 0; otherwise `-sum(p*ln(p))/ln(K)`, `p=q/total`, with zero terms omitted.
- `Completeness = nonempty required emitted masks / K`.
- For each map-observed reference, modal mass is `max_mask W(reference,mask)`. `Ambiguity = sum(total-reference-mass - modal-mass) / sum(total-reference-mass)`; no mass yields 1. Tied modal masks use the same maximum and smaller-mask output tie break.
- `VisualCompatibility = arithmetic mean similarity from every final member to the training medoid`; no members yields 0. Every member has equal visual weight so a giant map cannot dominate it.
- `HoldoutAccuracy` is the selected topology’s exact top-1 weighted accuracy above; no eligible holdout is 0.
- `EightWayAccuracyGain` is signed and retained even when four-way is selected.
- `SupportScore = min(min(1,MapSupport/MinimumMapSupport), min(1,RegionSupport/MinimumRegionSupport), min(1,ObservationSupport/MinimumObservationSupport))`, with floating-point division.
- `Confidence = 0.20*SupportScore + 0.15*MaskEntropy + 0.25*Completeness + 0.15*(1-Ambiguity) + 0.10*VisualCompatibility + 0.15*HoldoutAccuracy`.

Model comparisons/status use unrounded finite values. Persisted doubles are rounded six places midpoint-to-even. Numeric/reference/ID ties always use the lower numeric reference, smaller mask, or ordinal ID as specified; do not use collection iteration order.

Initial `Enabled` requires all three support minima, completeness exactly 1, ambiguity `<= MaximumAmbiguity`, holdout accuracy `>= EnabledConfidence`, confidence `>= EnabledConfidence`, all member frames source-valid, and no set-local Core validation issue. Otherwise status is `Pending` when confidence `>= PendingConfidence` or when incomplete while all three support minima pass; all other candidates are `Disabled`. Emit every failed status gate, not only the first, and assert the explanation invariant above.

Resolve enabled overlap once after all definitions exist: rank by descending unrounded confidence, then descending MapSupport, RegionSupport, ObservationSupport, then ID ordinal. Keep already claimed references with the higher-ranked set; demote every overlapping lower-ranked set to Pending and emit one `enabled-member-conflict` per conflicting reference/winner. Pending/disabled overlap is retained. Recompute no ID because status is not identity.

Generated display name is `Generated <lowest-sheet>-<first 8 hash hex chars after the ID prefix>`. Sets sort by ID. The shared validator must return no issue for the final generated catalog; any remaining issue is `TerrainGenerationError.InvalidGeneratedCatalog` and prevents publication.

## Deterministic default calibration workflow

The defaults above remain fixed for initial implementation and synthetic byte snapshots. Real-corpus calibration may change a threshold or bucket condition in either direction only through an explicit measured change:

1. Run identical corpus bytes/settings before and after and export candidate ID, status, topology, both holdout accuracies, gain, all metrics, failed gates, and image-only admission score/margin.
2. Manually label representative candidate families and image-only admissions as accept/reject/uncertain, emphasizing deceptive lookalikes, shorelines, paths, and sparse corners. Record corpus fingerprint and reviewer labels outside generated JSON in PR evidence.
3. Report before/after true enables, false enables, false disables, pending shifts, and comparison counts. A less conservative numeric threshold is allowed only when it improves labeled recall without enabling any known negative/adversarial fixture; a more conservative change is allowed to remove false enables.
4. Preserve hard conservative gates: training/holdout isolation, same-sheet and bucket boundedness, unique image owner/margins, three support minima, multi-map diagonal evidence, completeness, ambiguity, top-1 accuracy, enabled overlap resolution, and shared validation. Calibration may adjust their configured values/bucket constants, not silently delete a gate.
5. Update generator version if interpretation changes, settings defaults, locked JSON bytes, deterministic synthetic fixtures, false-enable regression fixtures, and smoke assertions in one reviewed change. Rerun separate-process byte identity.

No implementation task may tune just to force a real enabled candidate. Zero real enabled sets is acceptable; a known false enable is not.

## Converter failure type and bounded ownership

Add:

```csharp
public enum TerrainGenerationError
{
    MapInventoryNotFound,
    InvalidMapInventory,
    MapNotFound,
    MapReadFailed,
    MapDecodeFailed,
    ManifestNotFound,
    ManifestReadFailed,
    ManifestMalformed,
    UnsupportedTileSize,
    SheetNotFound,
    SheetReadFailed,
    SheetDecodeFailed,
    FrameOutOfBounds,
    NoMaps,
    NoEligiblePlacements,
    NumericOverflow,
    InvalidGeneratedCatalog
}

public sealed class TerrainGenerationException : InvalidOperationException
{
    public TerrainGenerationError Error { get; }
    public string? InputPath { get; }
    public TerrainGraphicReference? Reference { get; }
}
```

Input path is absolute for actionable CLI exceptions but is never persisted. Wrap expected I/O/`MapFormatException`/ImageSharp decode failures with the exact category and inner exception. Do not convert publication `IOException` into success or an inference diagnostic.

`TerrainCorpus` is a read-only index of sorted map descriptors/identities, strict frame metadata, globally observed references, relevant sheets, root diagnostics, and fingerprint; it retains no map bytes, `MapDocument`, pixel buffers, or `Image`. Corpus loading reads/decode-validates one listed map at a time and releases its bytes/document before the next. Later graph/region/evaluation passes deliberately reread one map at a time rather than retaining all map grids. Region/placement objects are map-local; candidates retain only aggregate counts, weighted reference/mask totals, medoid/centroid vectors, and diagnostics after each visitor returns.

`TerrainFeatureCache.Build(TerrainCorpus corpus, ITerrainSheetImageLoader? loader = null)` processes relevant sheets in numeric order. `ITerrainSheetImageLoader.Load(string path)` returns an `Image<Rgba32>` owned by `Build`; `Build` disposes that image in the same sheet iteration on success and every extraction failure after extracting every eligible exact-32 frame on it once. The returned cache owns only immutable descriptor arrays and bucket indexes and needs no disposal. One image/sheet and one feature/reference are asserted.

The builder/generator seams are exact and invocation-local:

```csharp
internal interface ITerrainCatalogBuilder
{
    TerrainCatalog Build(TerrainCorpus corpus, TerrainGenerationSettings settings);
}

internal interface ITerrainCatalogStore
{
    void Write(string repoRoot, string serializedCatalog);
}

internal static TerrainGenerationResult Generate(
    string repoRoot,
    TerrainGenerationSettings settings,
    Func<string, TerrainCorpus> loadCorpus,
    ITerrainCatalogBuilder builder,
    ITerrainCatalogStore store);
```

The public overload supplies `TerrainCorpusLoader.Load`, a production `TerrainCatalogBuilder`, and `TerrainCatalogFileStore`. Generator owns orchestration only: load → build → shared validate → serialize → store → result. Builder owns feature-cache construction and all map rereads but no persisted file. Store owns temp stream/path only, receives an already serialized string, and never validates/builds. Test fakes implement these exact surfaces; no async/open/save aliases.

## Overall mutation propagation

| Mutation | Source/readers | Required propagation and atomicity |
|---|---|---|
| Add catalog contract | New get-only Core values → converter now, Parts 2/3 later | Define/copy → parse/serialize/validate tests → converter project reference. No runtime registry or migration. |
| Publish map inventory | Successful map-converter output names → terrain loader | Collect successful names → canonicalize/validate → durable sibling-temp replace. Stale unlisted maps remain ignored. |
| Build features/candidates | Listed map, manifest, relevant PNG bytes → scorer/diagnostics | Decode one map/sheet batch → retain compact evidence/features only → dispose/release → immutable candidate values. No global publication. |
| Generate catalog | Complete validated catalog → CLI/Part 3 loader | Serialize entirely → durable sibling-temp write → replace → only then success result. Any prior/I/O failure preserves destination. |
| Integrate `all` | Existing production all tail → generated inventory/manifest/catalog | Existing sheets/maps → successful-name inventory → combined manifest → exact `TerrainCommand.Execute` → summaries. Terrain failure leaves previous terrain catalog and causes nonzero process exit. |

## Task 0: Add the shared schema, typed parser, masks, IDs, and validation

**Files:**
- Create: `src/MapEditor.Core/Terrain/TerrainCatalog.cs`
- Create: `src/MapEditor.Core/Terrain/TerrainMasks.cs`
- Create: `src/MapEditor.Core/Terrain/TerrainGeneratedId.cs`
- Create: `src/MapEditor.Core/Terrain/TerrainCatalogException.cs`
- Create: `src/MapEditor.Core/Terrain/TerrainCatalogJson.cs`
- Create: `src/MapEditor.Core/Terrain/TerrainCatalogValidator.cs`
- Create: `tests/MapEditor.Core.Tests/Terrain/TerrainMasksTests.cs`
- Create: `tests/MapEditor.Core.Tests/Terrain/TerrainCatalogJsonTests.cs`
- Create: `tests/MapEditor.Core.Tests/Terrain/TerrainCatalogValidatorTests.cs`
- Modify: `tools/AssetConverter/src/AssetConverter/AssetConverter.csproj:10-12`
- Modify: `tools/AssetConverter/AssetConverter.sln:6-13,23-55`

**Mutation impact:**
- Source of truth: new Core schema-v1 values and helpers.
- Readers: parser/writer/validator, converter, Part 2 resolver, Part 3 loader/manager.
- Derived state: no runtime cache; converter solution gains Core.
- Propagation: define values → prove deep copying/masks/IDs/parser bytes/issues → add project/solution reference → build both dependency directions.
- Invariants: provider-neutral Core; exact 16/47 masks; identity depends only on topology/distinct members; strict typed parsing; review candidates remain representable; canonical bytes.
- Observable proof: exact bytes, exception category/path, copied-list behavior, and complete issue values.

**Step 1: Write failing tests**

Add:

- `Required_FourWay_ReturnsAll16CardinalMasksInOrder`
- `Required_EightWay_ReturnsExactly47NormalizedMasksInOrder`
- `Normalize_EightWay_RemovesUnsupportedDiagonalBits` over all 256 masks
- `Create_ReorderedMembersProducesSameId`
- `Create_EmptyOrDuplicateMembersThrows`
- `Create_ChangedTopologyOrMemberChangesId`
- `Constructors_CopyEveryNestedInputCollection`
- `Serialize_RepresentativeCatalogMatchesLockedSchemaV1Bytes`
- `Parse_SerializeCanonicalizesNonSemanticOrderButPreservesVariantOrder`
- `Parse_MalformedDuplicateUnknownMissingNullWrongKindAndInvalidEnumReturnExactTerrainCatalogError`
- `Parse_InvalidSettingsRelationshipsOrMetricRangesReturnExactTerrainCatalogError`
- `Validate_ProgrammaticSchemaEnumAndEmptyMemberErrorsReturnExactIssuesWithoutThrowing`
- `Validate_IncompleteOverlappingPendingAndDisabledAllowsReviewData`
- `Validate_IncompleteOrOverlappingEnabledReturnsExactOrderedIssues`
- adversarial `Validate_VariantNotInMembersAndUnreachableDiagonalAreNotSilentlyAccepted`.

The locked-byte fixture includes one complete enabled four-way set and one incomplete pending eight-way set with image-only provenance and a diagnostic, exercising all fields, enum spellings, explicit diagnostic nulls, nested property order, sorting, and trailing LF.

**Step 2: Run red**

```bash
dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj \
  --filter 'FullyQualifiedName~Terrain' -v minimal
```

Expected: compile failure because `MapEditor.Core.Terrain` does not exist.

**Step 3: Implement the minimal contract**

Use explicit get-only constructors for collection-bearing records. Use `Utf8JsonWriter` for exact output and `JsonDocument.EnumerateObject()` plus seen-name sets for strict parsing. Parsing wraps every structural/range failure in the exact typed exception; semantic validation returns issues. Add Core to `AssetConverter.csproj` and to the converter solution under `src` using path `..\..\src\MapEditor.Core\MapEditor.Core.csproj`.

**Step 4: Run green**

```bash
dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj \
  --filter 'FullyQualifiedName~Terrain' -v minimal
dotnet build tools/AssetConverter/AssetConverter.sln -v minimal
dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj -v minimal
```

Expected: all pass; only pre-existing converter warnings are acceptable.

| Invariant | Proved by |
|---|---|
| Exact mask spaces and normalization | `Required_*`, exhaustive `Normalize_*` |
| Stable identity and invalid input rejection | three `Create_*` tests |
| Values do not retain mutable caller collections | `Constructors_CopyEveryNestedInputCollection` |
| Wire bytes/types/nulls/order are exact | serializer/parser tests |
| Review data is retained while enabled invalidity is rejected | validator status tests |
| Adversarial wiring cannot pass | `Validate_VariantNotInMembers*` |

**Step 5: Commit**

```bash
git add src/MapEditor.Core/Terrain tests/MapEditor.Core.Tests/Terrain \
  tools/AssetConverter/src/AssetConverter/AssetConverter.csproj \
  tools/AssetConverter/AssetConverter.sln
git commit -m "feat: add terrain catalog contract"
```

## Task 1: Declare, load, stream, and fingerprint the generated corpus

**Files:**
- Create: `tools/AssetConverter/src/AssetConverter/Terrain/TerrainGenerationException.cs`
- Create: `tools/AssetConverter/src/AssetConverter/Terrain/TerrainMapInventory.cs`
- Create: `tools/AssetConverter/src/AssetConverter/Terrain/TerrainCorpus.cs`
- Create: `tools/AssetConverter/src/AssetConverter/Terrain/TerrainCorpusLoader.cs`
- Create: `tools/AssetConverter/src/AssetConverter/Terrain/TerrainFrameIndex.cs`
- Create: `tools/AssetConverter/src/AssetConverter/Terrain/TerrainCorpusFingerprint.cs`
- Create: `tools/AssetConverter/src/AssetConverter/Terrain/TerrainHoldout.cs`
- Create: `tools/AssetConverter/src/AssetConverter/Properties/AssemblyInfo.cs`
- Create: `tools/AssetConverter/tests/AssetConverter.Tests/Terrain/TerrainFixtureBuilder.cs`
- Create: `tools/AssetConverter/tests/AssetConverter.Tests/Terrain/TerrainMapInventoryTests.cs`
- Create: `tools/AssetConverter/tests/AssetConverter.Tests/Terrain/TerrainCorpusLoaderTests.cs`
- Create: `tools/AssetConverter/tests/AssetConverter.Tests/Terrain/TerrainCorpusFingerprintTests.cs`
- Create: `tools/AssetConverter/tests/AssetConverter.Tests/Terrain/TerrainHoldoutTests.cs`

**Mutation impact:**
- Source of truth: generated input files remain read-only; new inventory declares the map subset.
- Readers: Tasks 2–5 consume the read-only corpus index and reread descriptors.
- Derived state: strict frame index, globally observed reference set, relevant sheets, diagnostics, and fingerprint; no retained maps/images/file bytes.
- Propagation: parse inventory/manifest → stream/decode listed maps one at a time → identify eligible refs/sheets → stream fingerprint records → return complete index.
- Failure: expected inputs throw exact `TerrainGenerationException`; missing manifest references/non-32 map-used frames are excluded diagnostics; no output mutation exists.
- Invariants: only listed maps/layer 0/nonzero graphics; stale maps irrelevant; exact map/hash identity; bounded lifetime; no eligible corpus aborts.
- Observable proof: inspect final identities/references/diagnostics/hash and recording lifetime counters.

**Step 1: Write failing tests**

`TerrainFixtureBuilder` creates a temp repo layout, canonical inventory, `MapDocument.Create`/`SetLayer`/`MapCodec.Encode` maps, strict manifest, and real ImageSharp PNG sheets. Add:

- `Inventory_WriteProducesCanonicalSiblingReplacementWithoutStaleEntries`
- `Inventory_MalformedDuplicateUnsortedTraversalOrMissingEntryThrowsExactCategory`
- `Load_ReadsOnlyInventoryListedMapsAndIgnoresStaleMap`
- `Load_ReadsOnlyLayer0AndUsesLockedMapIdentity`
- `Load_GraphicZeroWithNonzeroSheetIsIgnored`
- `Load_MapTrailerIsAcceptedByMapCodec`
- `Load_MissingManifestReferenceAndUndersizedOrOversizedFrameAreDiagnosedAndExcluded`
- `Load_MalformedOrDuplicateAliasManifestThrowsManifestMalformed`
- `Load_UnsupportedTileSizeThrowsUnsupportedTileSize`
- `Load_MalformedMissingOrUnreadableMapThrowsExactCategory`
- `Load_MissingRelevantPngThrowsSheetNotFound`
- `Load_NoMapsOrNoEligiblePlacementsThrows`
- `Load_ProcessesAtMostOneMapByteArrayAndDocumentAtATime`
- `Compute_EquivalentRootsCreationOrderAndTimestampsHaveSameFingerprint`
- `Compute_UnlistedMapAndUnreferencedPngDoNotChangeFingerprint`
- adversarial `Compute_InventoryListedMapManifestOrRelevantPngByteChangeChangesFingerprint`
- `Compute_SettingsChangeDoesNotChangeCorpusFingerprint`
- `Holdout_KnownIdentityBytesProduceLockedDigestPrefixAndAssignment`.

The layer test puts a valid frame only on layer 1 at one cell. The manifest test includes exact duplicate property names and numeric aliases (`"1"`/`"01"`) for sheets and graphics. The size test covers 16×32 and 64×32.

**Step 2: Run red**

```bash
dotnet test tools/AssetConverter/tests/AssetConverter.Tests/AssetConverter.Tests.csproj \
  --filter 'FullyQualifiedName~TerrainMapInventory|FullyQualifiedName~TerrainCorpus|FullyQualifiedName~TerrainHoldout' -v minimal
```

Expected: compile failure because corpus types do not exist.

**Step 3: Implement strict bounded loading**

Use exact generated paths from the locked section. Parse IDs only when `int.TryParse(..., NumberStyles.AllowLeadingSign, InvariantCulture)` succeeds and `value.ToString(InvariantCulture) == propertyName`; this rejects `01`, `+1`, and `-0`. Detect aliases before insertion. Require manifest root to contain only `tileSize` and `sheets` exactly once, with tile size 32. Rects require four Int32 values, nonnegative x/y, positive width/height, and checked `x+width`/`y+height`.

Read a listed map byte array, hash/decode/scan it, then release it before the next. Keep sorted descriptors, not grids. Wrap map codec errors with map identity/path. Stream inventory, manifest, and PNG hash content. Implement inventory `Write` now as create-new sibling temp → UTF-8/no-BOM write → `FileStream.Flush(true)` → close → same-directory move/overwrite, with primary-exception-preserving best-effort cleanup; Task 6 only supplies successful names. Grant internals only to `AssetConverter.Tests`.

**Step 4: Run green**

```bash
dotnet test tools/AssetConverter/tests/AssetConverter.Tests/AssetConverter.Tests.csproj \
  --filter 'FullyQualifiedName~TerrainMapInventory|FullyQualifiedName~TerrainCorpus|FullyQualifiedName~TerrainHoldout' -v minimal
dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj -v minimal
```

Expected: the exact synthetic inventory/corpus/holdout filter and the full hermetic Core suite pass; no configured corpus path is accessed.

| Invariant | Proved by |
|---|---|
| Inventory publication is canonical and stale/unlisted maps cannot affect inference | inventory write, stale-map, and irrelevant fingerprint tests |
| Layer 0 and graphic-zero semantics are exact | layer/graphic tests |
| Parser/input failures are typed and actionable | manifest/map/tile-size/PNG tests |
| Unsupported frames are review diagnostics, not members | size/reference test |
| Memory does not scale with simultaneous decoded maps | lifetime recording test |
| Fingerprint/holdout byte contracts are stable | all hash tests |

**Step 5: Commit**

```bash
git add tools/AssetConverter/src/AssetConverter/Terrain \
  tools/AssetConverter/src/AssetConverter/Properties/AssemblyInfo.cs \
  tools/AssetConverter/tests/AssetConverter.Tests/Terrain
git commit -m "feat: load terrain inference corpus"
```

## Task 2: Extract exact image features by sheet batch

**Files:**
- Create: `tools/AssetConverter/src/AssetConverter/Terrain/TerrainImageFeatures.cs`
- Create: `tools/AssetConverter/src/AssetConverter/Terrain/TerrainFeatureExtractor.cs`
- Create: `tools/AssetConverter/src/AssetConverter/Terrain/TerrainFeatureBuckets.cs`
- Create: `tools/AssetConverter/src/AssetConverter/Terrain/TerrainFeatureCache.cs`
- Create: `tools/AssetConverter/tests/AssetConverter.Tests/Terrain/TerrainFeatureExtractorTests.cs`
- Create: `tools/AssetConverter/tests/AssetConverter.Tests/Terrain/TerrainFeatureBucketsTests.cs`
- Create: `tools/AssetConverter/tests/AssetConverter.Tests/Terrain/TerrainFeatureCacheTests.cs`

**Mutation impact:**
- Source of truth: indexed RGBA pixels remain read-only.
- Readers: graph similarity, medoids/centroids, image-only ownership, visual score.
- Derived state: descriptor arrays and bucket indexes only.
- Propagation: relevant sheet numeric order → load one image → validate every eligible rect → extract every feature once → dispose image → publish complete cache after all sheets.
- Lifecycle: loader returns an owned image; cache builder disposes it on success/failure before moving to next sheet; no `Image` escapes or survives publication.
- Failure: decode/bounds failures throw `SheetDecodeFailed`/`FrameOutOfBounds` with path/reference; partial cache is not returned.
- Invariants: exact hand fixtures, transparent noise independence, bounded same-sheet queries, one decode/sheet/extraction/reference.
- Observable proof: full arrays, hashes/buckets, similarities, comparison counts, and disposal counters.

**Step 1: Write failing tests**

Add:

- `Extract_TransparentNoiseBlackWhiteAndSplitMatchLockedVectorsAndHashes`
- `Extract_TransparentRgbNoiseProducesIdenticalFeatures`
- `Extract_DirectionalBordersAndCornersUseLockedOrientation`
- `Similarity_UsesLockedComponentDistancesAndIsSymmetricBounded`
- `QueryCandidates_UsesExactSameSheetDecilePaletteAndThreeBandRulesInNumericOrder`
- adversarial `QueryCandidates_DifferentSheetOrTwoMatchingBandsIsNeverCompared`
- `Build_DecodesEachRelevantSheetOnceExtractsEachEligibleFrameOnceAndRetainsNoImages`
- `Build_SuccessOrExtractionFailureDisposesEachLoadedImageOnce`
- `Build_MissingOrCorruptPngThrowsExactCategory`
- `Build_OutOfBoundsOrOverflowingManifestRectThrowsReferenceCategory`
- one real-PNG `Build_ImageSharpLoaderMatchesDirectExtractor` integration test.

The recording fake implements exactly `ITerrainSheetImageLoader.Load(string)` and returns real `Image<Rgba32>` instances; it has no `Open`, `Read`, async, or caller-dispose method.

**Step 2: Run red**

```bash
dotnet test tools/AssetConverter/tests/AssetConverter.Tests/AssetConverter.Tests.csproj \
  --filter 'FullyQualifiedName~TerrainFeature' -v minimal
```

Expected: compile failure because feature/cache/bucket types do not exist; test discovery is limited to synthetic `TerrainFeature*` fixtures.

**Step 3: Implement only the locked descriptor/distance/buckets**

Use fixed scalar loops and checked integer accumulation where required. Materialize descriptor arrays/read-only bucket results. `TerrainFeatureCache.Build` uses a per-sheet `using`; the returned cache is read-only and not `IDisposable`. Expose internal comparison counts only for bounded-work tests.

**Step 4: Run green**

Run the focused command above. Expected: all feature tests pass.

| Invariant | Proved by |
|---|---|
| Exact descriptor and bit/band order | locked hand fixtures |
| Transparent storage artifacts do not alter inference | transparent-noise test |
| Distances/weights are exact and bounded | similarity test |
| Work is same-sheet and bucket bounded | query tests |
| Images are batch-owned and always disposed | build lifetime tests |
| Real ImageSharp loading follows the fake surface | real PNG integration |

**Step 5: Commit**

```bash
git add tools/AssetConverter/src/AssetConverter/Terrain \
  tools/AssetConverter/tests/AssetConverter.Tests/Terrain
git commit -m "feat: extract terrain image features"
```

## Task 3: Mine weighted families and classify image-only members

**Files:**
- Create: `tools/AssetConverter/src/AssetConverter/Terrain/TerrainCandidate.cs`
- Create: `tools/AssetConverter/src/AssetConverter/Terrain/TerrainMapBatchReader.cs`
- Create: `tools/AssetConverter/src/AssetConverter/Terrain/TerrainRegionMiner.cs`
- Create: `tools/AssetConverter/src/AssetConverter/Terrain/TerrainCandidateMiner.cs`
- Create: `tools/AssetConverter/src/AssetConverter/Terrain/TerrainImageMemberClassifier.cs`
- Create: `tools/AssetConverter/tests/AssetConverter.Tests/Terrain/TerrainRegionMinerTests.cs`
- Create: `tools/AssetConverter/tests/AssetConverter.Tests/Terrain/TerrainCandidateMinerTests.cs`
- Create: `tools/AssetConverter/tests/AssetConverter.Tests/Terrain/TerrainImageMemberClassifierTests.cs`

**Mutation impact:**
- Source of truth: none; candidates project corpus/features/settings.
- Readers: Task 4 fitter/scorer.
- Derived state: graph components, per-map regions/weights, map-observed masks/supports, medoids/centroids, admissions/rejections.
- Propagation: fixed partition → training graph → sorted components → one-map-at-a-time weighted evidence → training visual models → simultaneous image ownership → read-only candidates.
- Failure: unclassified frames remain absent with bounded diagnostics; nonfinite/overflow throws before any output.
- Invariants: holdout labels do not train; map/region weighting; transitive same-sheet graph; strict unique image owner; provenance/graphic zero; no image support inflation.
- Observable proof: candidate members, exact weights/supports/centroids/masks/provenance/diagnostics.

**Step 1: Write failing tests**

Add:

- `Mine_CardinallyAdjacentCompatibleVariantsFormFamily`
- `Mine_TransitiveBridgeProducesOneDeterministicComponent`
- `Mine_DeceptiveLookalikeWithoutAdjacencyDoesNotJoin`
- `Mine_DifferentSheetsNeverJoin`
- `Weighting_HugeRegionAndTinyMapEachContributeOneMapMass`
- `Weighting_DisconnectedRegionsSplitMapMassEqually`
- `Masks_UseLockedCoordinateDirectionsAndTreatOutsideAsAbsent`
- `Supports_UseTrainingRawMapsRegionsPlacementsAndCornerTrialsOnly`
- `Classify_MissingVariantIsAdmittedWithNormalizedMaskAndReviewEvidence`
- `Classify_GraphicZeroAndEveryMapObservedReferenceAreNeverImageOnly`
- `Classify_HoldoutObservedReferenceWithoutTrainingOwnerIsExcludedNotRelabeled`
- `Classify_OnlyOneFamilyUsesZeroRunnerUpAndStillRequiresStrictThreshold`
- `Classify_MissingCentroidCannotOwnAndEmitsExactDiagnostic`
- `Classify_NoOwnerExactTieNearTieOrSideCornerTieIsRejectedDeterministically`
- `Classify_AdmissionsUseSnapshotSoFamilyIterationCannotChangeOwner`
- `Mine_InputAndMapOrderPermutationProducesEquivalentCandidates`
- adversarial `Mine_ChangingOnlyHeldoutMasksAndAdjacencyDoesNotChangeTrainingOrAdmissions`
- `Mine_ReleasesEachDecodedMapBeforeReadingNextAcrossEveryPass`.

Use deterministic identity helpers to force train/holdout. The giant fixture has one huge region and several tiny maps with conflicting masks; assert weighted modes are controlled by map mass, while raw support values remain the locked populations.

**Step 2: Run red**

```bash
dotnet test tools/AssetConverter/tests/AssetConverter.Tests/AssetConverter.Tests.csproj \
  --filter 'FullyQualifiedName~TerrainCandidate|FullyQualifiedName~TerrainRegion|FullyQualifiedName~TerrainImageMember' -v minimal
```

Expected: compile failure because candidate/region/classifier types do not exist; only temporary synthetic fixtures are discovered.

**Step 3: Implement graph, evidence, centroids, and simultaneous expansion**

`TerrainMapBatchReader` decodes one descriptor and invokes a synchronous visitor; it retains nothing after return. `TerrainRegionMiner` returns map-local values only and mutates no candidate/map/cache. The candidate miner folds those values into aggregate support/mask/centroid accumulators before the next map and never appends placement samples to a cross-map collection. `TerrainCandidateMiner` owns components and evidence but not topology/status/serialization. `TerrainImageMemberClassifier` receives a complete family snapshot and returns admissions/diagnostics; the caller materializes all expanded candidates only after classification ends.

Region identity is `(map identity, row-major minimum coordinate)` within a family; flood fill visits N/E/S/W in that order but output sorts by minimum coordinate. Preserve raw support separately from weights.

**Step 4: Run green**

Run the focused command above. Expected: candidate snapshots and all exact diagnostics pass.

| Invariant | Proved by |
|---|---|
| Adjacency+visual graph and transitive closure are exact | graph/bridge/lookalike tests |
| Giant maps/regions do not dominate fitted evidence | weighting tests |
| Support populations stay map-observed/training-only | support test |
| Image-only edge cases are explicit and conservative | classifier zero/owner/tie/centroid tests |
| Provenance-only holdout exclusion does not train labels | heldout tests |
| Processing is bounded per map | lifetime test |

**Step 5: Commit**

```bash
git add tools/AssetConverter/src/AssetConverter/Terrain \
  tools/AssetConverter/tests/AssetConverter.Tests/Terrain
git commit -m "feat: mine hybrid terrain candidates"
```

## Task 4: Fit top-1 topology models, score, diagnose, and resolve conflicts

**Files:**
- Create: `tools/AssetConverter/src/AssetConverter/Terrain/TerrainModelFitter.cs`
- Create: `tools/AssetConverter/src/AssetConverter/Terrain/TerrainCandidateScorer.cs`
- Create: `tools/AssetConverter/src/AssetConverter/Terrain/TerrainCatalogBuilder.cs`
- Create: `tools/AssetConverter/tests/AssetConverter.Tests/Terrain/TerrainModelFitterTests.cs`
- Create: `tools/AssetConverter/tests/AssetConverter.Tests/Terrain/TerrainCandidateScorerTests.cs`
- Create: `tools/AssetConverter/tests/AssetConverter.Tests/Terrain/TerrainCatalogBuilderTests.cs`

**Mutation impact:**
- Source of truth: none; definitions derive from read-only candidates/settings.
- Readers: Task 5 serialization/publication and Part 3 diagnostics.
- Derived state: topology-specific `W`, emitted mappings, top-1 predictors/accuracies, selected metrics/status/ID, overlap demotions.
- Propagation: fit both from training → evaluate top-1 on holdout only → select → formulas/status diagnostics → construct copied values → globally resolve overlap → shared validate.
- Failure: no train/holdout/support yields review status/diagnostics; remaining generated validation issue throws typed failure.
- Invariants: eight-way can prove material gain; emitted variants never define accuracy; exact empty/tie/formula behavior; every nonenabled set explained; enabled unique/complete.
- Observable proof: final mappings/predictions/metrics/status/diagnostics/IDs from synthetic evidence.

**Step 1: Write failing tests**

Add:

- `Fit_FourWayCorpusReconstructsAll16AndSelectsFourWay`
- blocker regression `Fit_DiagonalDistinctCorpus_Top1EightWayBeatsFourWayAlthoughFourWayEmittedListsAreSupersets`
- `Fit_CanonicalBlobCorpusWithMultiMapDiagonalSupportSelectsEightWayAndAll47`
- `Fit_SparseOrSingleMapCornersStayFourWayWithExactEvidenceDiagnostic`
- `Fit_NoisyVariantsShareEmittedMaskButTop1TieUsesLowerReference`
- `Fit_NoTrainingOrNoHoldoutUsesZeroAccuracyAndExactDiagnostic`
- `Fit_HoldoutAssignmentAndWeightedAccuracyAreStableAcrossOrderAndHugeMaps`
- adversarial `Fit_ChangingHeldoutLabelsChangesAccuracyButNotFittedPredictorsMappingsCentroidsOrMembers`
- `Metrics_HandCalculatedEntropyAmbiguityMedoidVisualSupportConfidenceMatch`
- `Metrics_EmptyPopulationsUseLockedZeroOrOneValuesWithoutNaN`
- `Score_CompleteHighConfidenceCandidateIsEnabled`
- `Score_IncompleteCandidateIsPendingEvenWhenOtherMetricsAreHigh`
- `Score_BelowPendingThresholdIsDisabled`
- `Score_EveryPendingAndDisabledSetHasStatusExplanation`
- `Build_StableIdIgnoresEditableAndObservationOrder`
- adversarial `Build_OverlappingEnabledCandidatesDeterministicallyDemotesLoserAndValidates`
- `Build_PendingAlternativesMayOverlap`
- `Build_DiagnosticsAreDeduplicatedOrderedAndUseInvariantFixedValues`.

Generate 16/47 evidence algorithmically from `TerrainMasks.Required`. The blocker fixture must explicitly compute the old membership-list hit rate and show it cannot favor eight-way, then assert the locked top-1 four/eight accuracies and selected eight-way result.

**Step 2: Run red**

```bash
dotnet test tools/AssetConverter/tests/AssetConverter.Tests/AssetConverter.Tests.csproj \
  --filter 'FullyQualifiedName~TerrainModel|FullyQualifiedName~TerrainCandidateScorer|FullyQualifiedName~TerrainCatalogBuilder' -v minimal
```

Expected: compile failure because fitter/scorer/builder types do not exist; only synthetic terrain model tests are discovered.

**Step 3: Implement exact fitting/scoring**

Keep emitted map and evaluation predictor as different fields/types so code cannot accidentally use a membership list for accuracy. Reread held-out maps one at a time only after fitting is complete. Use locked formulas, unrounded comparisons, six-place persisted values, and all tie rules. Validate explanation codes before catalog construction, resolve overlap once, and throw `InvalidGeneratedCatalog` with all ordered issue messages if shared validation is nonempty.

**Step 4: Run green**

```bash
dotnet test tools/AssetConverter/tests/AssetConverter.Tests/AssetConverter.Tests.csproj \
  --filter 'FullyQualifiedName~Terrain' -v minimal
dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj \
  --filter 'FullyQualifiedName~Terrain' -v minimal
```

Expected: all synthetic converter/Core terrain tests pass; no legacy converter corpus class matches either exact positive filter.

| Invariant | Proved by |
|---|---|
| Eight-way can materially outperform cardinal supersets | blocker regression and full blob test |
| Holdout is deterministic, top-1, weighted, and isolated | holdout/label/huge-map tests |
| Every formula/population/empty/tie is exact | hand metric and empty tests |
| Status diagnostics explain all review states | scoring explanation tests |
| Enabled overlap is deterministic and pending overlap retained | both build overlap tests |
| Diagnostic persistence is canonical | diagnostic ordering/value test |

**Step 5: Commit**

```bash
git add tools/AssetConverter/src/AssetConverter/Terrain \
  tools/AssetConverter/tests/AssetConverter.Tests/Terrain
git commit -m "feat: score terrain topology candidates"
```

## Task 5: Compose generation and durable atomic catalog publication

**Files:**
- Create: `tools/AssetConverter/src/AssetConverter/Terrain/TerrainCatalogGenerator.cs`
- Create: `tools/AssetConverter/src/AssetConverter/Terrain/TerrainCatalogFileStore.cs`
- Create: `tools/AssetConverter/tests/AssetConverter.Tests/Terrain/TerrainCatalogGeneratorTests.cs`
- Create: `tools/AssetConverter/tests/AssetConverter.Tests/Terrain/TerrainCatalogFileStoreTests.cs`

**Mutation impact:**
- Source of truth: persisted `<repoRoot>/Assets/Sprites/terrain-brushes.json` after successful replacement.
- Readers: CLI now; Part 3 later.
- Derived state: invocation-local corpus/features/candidates only; all disposed/released before store return.
- Propagation: load → build → shared validate → serialize memory → temp create/write/durable flush/close → same-directory move/replace → return counts.
- Publication boundary: no destination access before complete bytes; destination never deleted first; no success result until replace.
- Failure: input/builder/validation/serialize touches no destination; write/flush/dispose/replace/move preserves prior destination. Cleanup is best effort and cannot replace the primary exception.
- Observable proof: exact destination/temp bytes, exception identity/data, production durable-flush call, and full parsed output.

Public entry point:

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

Exact store seams:

```csharp
internal interface ITerrainCatalogFileOperations
{
    bool Exists(string path);
    Stream CreateFile(string path);
    void FlushToDisk(Stream stream);
    void Replace(string sourcePath, string destinationPath);
    void Move(string sourcePath, string destinationPath);
    void Delete(string path);
}

internal sealed class TerrainCatalogFileStore : ITerrainCatalogStore
{
    public TerrainCatalogFileStore();
    internal TerrainCatalogFileStore(ITerrainCatalogFileOperations operations);
    public void Write(string repoRoot, string serializedCatalog);
}
```

Production `CreateFile` uses `new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)`; `FlushToDisk` requires that `FileStream` and calls `Flush(flushToDisk:true)`. Store writes UTF-8 without BOM, invokes `FlushToDisk` before disposing, and only then replaces. Temp is `terrain-brushes.json.tmp-<guid N>` in the destination directory.

On a primary failure, attempt `Delete(temp)`. If cleanup fails, rethrow the original exception instance with original stack via `ExceptionDispatchInfo`; attach the cleanup exception at `primary.Data["TerrainCatalogFileStore.CleanupException"]`. Never throw the cleanup exception or an aggregate instead. A cleanup failure may leave only the unique temp, never a partial/replaced destination.

**Step 1: Write failing tests**

Add:

- `Generate_SyntheticFourWayPipelineWritesParseableValidatedCatalog`
- `Generate_IntegratedEightWayPipelineSelectsEightWayByTop1Gain`
- `Generate_IntegratedImageOnlyPipelinePersistsProvenanceMaskAndAdmissionEvidence`
- `Generate_EquivalentRootsAndRepeatedRunsAreByteIdentical`
- `Generate_RelevantMutationChangesFingerprintAndOutputWhileIrrelevantMutationDoesNot`
- `Generate_InputFailurePreservesPriorCatalogWithoutCreatingTemp`
- adversarial `Generate_InvalidInjectedBuilderOutputIsNeverPublished`
- `Generate_InjectedBuilderAndStoreAreCalledOnceAndGeneratorOwnsOnlyOrchestration`
- `Write_UsesSiblingCreateNewAndDurableFlushBeforeReplace`
- `Write_WriteFlushDisposeOrReplaceFailurePreservesPriorBytesAndDeletesTemp`
- `Write_MoveFailureWithoutPriorCatalogLeavesNoDestinationOrTemp`
- adversarial `Write_DeleteFailureRethrowsPrimaryExceptionAndRecordsCleanupFailure`.

The three pipeline tests use real corpus loader, feature builder, miner, fitter, shared serializer/validator, and real files; do not mock an inference stage. The invalid-builder test uses the exact internal overload and store fake.

**Step 2: Run red**

```bash
dotnet test tools/AssetConverter/tests/AssetConverter.Tests/AssetConverter.Tests.csproj \
  --filter 'FullyQualifiedName~TerrainCatalogGenerator|FullyQualifiedName~TerrainCatalogFileStore' -v minimal
```

Expected: compile failure because generator/store types do not exist; only synthetic terrain publication tests are discovered.

**Step 3: Implement one synchronous path**

Normalize repo root for access only. Builder owns all bounded processing. Generator validates even injected output and creates a defensively copied result diagnostic list. Store performs no validation. Let typed generation and I/O failures propagate so CLI exits nonzero.

**Step 4: Run green and repository-isolated converter gate**

```bash
dotnet test tools/AssetConverter/tests/AssetConverter.Tests/AssetConverter.Tests.csproj \
  --filter 'FullyQualifiedName~Terrain|FullyQualifiedName~MapConversionResultTests' -v minimal
```

Expected: every selected synthetic terrain test passes; the `MapConversionResultTests` branch is intentionally empty until Task 6 introduces that dedicated class. This exact positive filter excludes all legacy Illutia/Aspereta corpus snapshots; do not substitute a broad negative filter or run an unconditional external-data suite and call its failure acceptable.

| Invariant | Proved by |
|---|---|
| Full four/eight/image-only pipelines honor contracts | three integrated tests |
| Equivalent inputs emit exact bytes | determinism test |
| Only relevant inputs alter fingerprint/output | mutation test |
| Builder invalidity cannot cross publication boundary | injected-builder test |
| File bytes are durably flushed before replace | ordered operations test |
| Primary failure and old destination survive every I/O/cleanup edge | failure tests |

**Step 5: Commit**

```bash
git add tools/AssetConverter/src/AssetConverter/Terrain \
  tools/AssetConverter/tests/AssetConverter.Tests/Terrain
git commit -m "feat: atomically generate terrain catalog"
```

## Task 6: Publish map inventory, add `terrain`, and integrate production `all`

**Files:**
- Create: `tools/AssetConverter/src/AssetConverter/Terrain/TerrainCommand.cs`
- Create: `tools/AssetConverter/src/AssetConverter/Terrain/TerrainAllFinalizer.cs`
- Modify: `tools/AssetConverter/src/AssetConverter/Maps/MapCopyConverter.cs:3-4,12-49`
- Modify: `tools/AssetConverter/src/AssetConverter/Aspereta/AsperetaMapConverter.cs:3-9,18-67`
- Modify: `tools/AssetConverter/src/AssetConverter/Program.cs:119-220`
- Create: `tools/AssetConverter/tests/AssetConverter.Tests/MapConversionResultTests.cs`
- Create: `tools/AssetConverter/tests/AssetConverter.Tests/Terrain/TerrainCommandTests.cs`
- Create: `tools/AssetConverter/tests/AssetConverter.Tests/Terrain/TerrainAllFinalizerTests.cs`

**Mutation impact:**
- Source of truth: command routing plus generated map inventory/manifest/catalog.
- Readers: terrain loader, shell users, `all`, future editor.
- Derived state: successful output-name lists only.
- Propagation: map converters collect successful basenames → focused map command or all writes canonical inventory → `all` finalizer writes combined manifest → exact terrain command → summaries. Focused terrain loads existing inventory.
- Production publication boundary: `TerrainAllFinalizer` is the actual helper called by top-level `all`; its test observes the same manifest/inventory/terrain order production uses, not a parallel test-only ordering helper.
- Failure: inventory/manifest/terrain exception makes process nonzero; no terrain success line; prior terrain remains. Existing other generated classes are not rolled back.
- Invariants: no stale map glob; focused/`all` share exact generator/settings/writer; terrain after complete manifest; usage accurate; output byte-identical across processes.
- Observable proof: built CLI processes, exact stdout/exit/file bytes, and production finalizer callback observations.

`MapCopyResult` gains `IReadOnlyList<string> OutputFileNames`; `MapConvertResult` gains the same final property. Each converter appends only after its output stream/copy closes successfully and returns distinct ordinal-sorted bare names. Existing count/failure semantics remain.

`TerrainCommand` is:

```csharp
public static class TerrainCommand
{
    public static TerrainGenerationResult Execute(string repoRoot, TextWriter output);
}
```

Success output after publication is exactly:

```text
Terrain: <enabled> enabled, <pending> pending, <disabled> disabled -> <absolute output path>
Terrain fingerprint: <sha256 fingerprint>
```

Then each sorted root diagnostic is `  WARN <code>: <message>`. No success text precedes generation.

`TerrainAllFinalizer` has this exact internal surface:

```csharp
internal static TerrainGenerationResult Execute(
    string repoRoot,
    IEnumerable<string> successfulMapFileNames,
    Func<string> buildCombinedManifest,
    TextWriter output,
    Func<string, TextWriter, TerrainGenerationResult> runTerrain);
```

It canonical-writes map inventory, fully writes/closes manifest, then calls the runner. Production `Program` passes `FrameManifestBuilder.BuildCombined(...)` and `TerrainCommand.Execute`; tests inject only the runner/builder while invoking this production finalizer. Top-level `all` calls this helper after both map conversions at current `tools/AssetConverter/src/AssetConverter/Program.cs:173-183` and does not separately write the manifest.

**Step 1: Write failing result/inventory/CLI tests**

Add to the dedicated `MapConversionResultTests` class, using only temporary synthetic map inputs:

- `MapCopyConvert_ReturnsOnlySuccessfulSortedOutputFileNames`
- `AsperetaConvert_ReturnsOnlySuccessfulSortedOutputFileNames`

The Aspereta case creates one valid minimal 100×100 source map, one malformed source map, and an invocation-local mapping (empty tile graphics are sufficient); it must not read `Paths.AsperetaMaps`, `Paths.AsperetaData`, `data/aspereta-mapping.tsv`, or any repository corpus. Both tests assert ordinal output-name order, absence of failed names, unchanged count/failure behavior, and that a name is added only after its output stream/copy completed successfully.

Add the remaining synthetic command/finalizer tests:

- `Execute_SyntheticRootPrintsLockedSummaryAfterWritingCatalog`
- `TerrainProcess_SyntheticRootExitsZeroAndWritesExpectedPath`
- `TerrainProcess_InvalidRootExitsNonzeroAndPreservesPriorCatalog`
- `TerrainProcess_EquivalentRootsProduceByteIdenticalCatalogs`
- `Execute_RepeatedSameInputsIsByteIdentical`
- `AllFinalizer_ProductionPathWritesSuccessfulInventoryThenManifestThenCallsExactTerrainRunner`
- adversarial `AllFinalizer_PreseededStaleMapIsAbsentFromInventoryAndCannotAffectTerrainBytes`
- `AllFinalizer_TerrainFailurePrintsNoSuccessAndPreservesPriorCatalog`.

Process tests run `dotnet` with `typeof(TerrainCommand).Assembly.Location`, command, and fixture root, capturing stdout/stderr/exit. The ordering runner asserts inventory and manifest streams are closed/readable and exact before returning. Also assert `Program.cs` delegates manifest+terrain finalization only to `TerrainAllFinalizer` by exercising the built `terrain` process and the production finalizer; do not create an unrelated ordering helper.

**Step 2: Run red**

```bash
dotnet test tools/AssetConverter/tests/AssetConverter.Tests/AssetConverter.Tests.csproj \
  --filter 'FullyQualifiedName~Terrain|FullyQualifiedName~MapConversionResultTests' -v minimal
```

Expected: compile failure because the result properties and terrain command/finalizer types do not exist. The exact positive filter discovers only new hermetic synthetic tests and excludes every legacy Illutia/Aspereta corpus test.

**Step 3: Implement map reporting and routing**

Collect names without changing existing naming/conversion behavior. Call the already tested Task 1 `TerrainMapInventory.Write`; do not add a second inventory format or direct `File.WriteAllText` path.

Add focused `terrain` before `all`, with `all`’s optional repo-root default. Replace current direct combined-manifest write at `tools/AssetConverter/src/AssetConverter/Program.cs:185-189` with `TerrainAllFinalizer.Execute` after maps/sheets/effects are complete. Print existing summaries plus terrain’s own two-line summary once. Update usage to include `terrain [repoRoot]`. Do not weaken missing Aspereta behavior.

**Step 4: Run automated final gates**

```bash
dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj -v minimal
dotnet test tools/AssetConverter/tests/AssetConverter.Tests/AssetConverter.Tests.csproj \
  --filter 'FullyQualifiedName~Terrain|FullyQualifiedName~MapConversionResultTests' -v minimal
dotnet build tools/AssetConverter/AssetConverter.sln -v minimal
dotnet build Goose2ClientGodot.sln -v minimal
git status --short
```

Expected: the full hermetic Core suite, exactly the new synthetic converter tests, and both builds pass; only intended source/tests/plans are changed and no generated `Assets/` file is staged. The positive converter filter must list `Terrain*` and `MapConversionResultTests` only.

**Step 5: Optionally run an asserted real-Illutia smoke outside the repository**

This is conditional manual calibration evidence, never an automated or pre-commit gate. Run it only when the configured Illutia source data/maps exist; otherwise skip it and still proceed to Step 6:

```bash
SMOKE_ROOT="$(mktemp -d)"
mkdir -p "$SMOKE_ROOT/Assets/Sprites" "$SMOKE_ROOT/Assets/Maps"
dotnet run --project tools/AssetConverter/src/AssetConverter -- batch "$SMOKE_ROOT/Assets/Sprites/sheets"
dotnet run --project tools/AssetConverter/src/AssetConverter -- maps "$SMOKE_ROOT/Assets/Maps"
dotnet run --project tools/AssetConverter/src/AssetConverter -- manifest "$SMOKE_ROOT/Assets/Sprites/manifest.json"
dotnet run --project tools/AssetConverter/src/AssetConverter -- terrain "$SMOKE_ROOT"
python3 - "$SMOKE_ROOT/Assets/Sprites/terrain-brushes.json" <<'PY'
import json, re, sys
with open(sys.argv[1], encoding="utf-8") as f:
    c = json.load(f)
assert c["schemaVersion"] == 1
assert re.fullmatch(r"sha256:[0-9a-f]{64}", c["corpusFingerprint"])
sets = c["sets"]
status_reasons = {
    "no-training-observations", "no-holdout-observations",
    "eight-way-evidence-insufficient", "support-below-minimum",
    "incomplete-required-masks", "ambiguity-above-maximum",
    "holdout-accuracy-below-enabled", "confidence-below-enabled",
    "confidence-below-pending", "enabled-member-conflict",
}
owners = {}
for s in sets:
    required = 16 if s["topology"] == "four-way" else 47
    if s["status"] == "enabled":
        assert len(s["masks"]) == required
        assert all(m["variants"] for m in s["masks"])
        for member in s["members"]:
            ref = (member["reference"]["sheet"], member["reference"]["graphic"])
            assert ref not in owners, (ref, owners.get(ref), s["id"])
            owners[ref] = s["id"]
    else:
        assert status_reasons.intersection(d["code"] for d in s["diagnostics"]), s["id"]
    admitted = {d["reference"]["sheet"] << 32 | (d["reference"]["graphic"] & 0xffffffff)
                for d in s["diagnostics"] if d["code"] == "image-only-member-admitted"}
    for member in s["members"]:
        if member["provenance"] == "image-only":
            key = member["reference"]["sheet"] << 32 | (member["reference"]["graphic"] & 0xffffffff)
            assert key in admitted, (s["id"], member)
for status in ("enabled", "pending", "disabled"):
    selected = [s for s in sets if s["status"] == status]
    print(status, len(selected))
    for s in selected[:10]:
        print(s["id"], s["topology"], s["metrics"]["confidence"],
              s["metrics"]["holdoutAccuracy"], [d["code"] for d in s["diagnostics"]])
PY
rm -rf "$SMOKE_ROOT"
```

The assertions do not require any real candidate to be enabled. If candidates exist, all nonenabled sets are explained and image-only members have evidence. Record counts/fingerprint and manually label representative results for the calibration workflow. Do not alter thresholds merely to make this smoke produce enabled sets.

On a machine with both configured corpora:

```bash
dotnet run --project tools/AssetConverter/src/AssetConverter -- all "$PWD"
cp Assets/Sprites/terrain-brushes.json /tmp/terrain-from-all.json
dotnet run --project tools/AssetConverter/src/AssetConverter -- terrain "$PWD"
cmp -s /tmp/terrain-from-all.json Assets/Sprites/terrain-brushes.json
```

Expected: `cmp` exits 0. Remove verification assets if created solely for testing; never commit them.

| Invariant | Proved by |
|---|---|
| Successful map inventory excludes stale/failed files | converter result and finalizer stale tests |
| Focused CLI has exact success/failure behavior | command/process tests |
| Separate processes emit identical bytes | equivalent-root process test |
| Production all finalization observes inventory → manifest → terrain | production finalizer test |
| Real data satisfies structural/conservative review invariants without forced enables | asserted smoke |

**Step 6: Commit**

```bash
git add tools/AssetConverter/src/AssetConverter/Maps/MapCopyConverter.cs \
  tools/AssetConverter/src/AssetConverter/Aspereta/AsperetaMapConverter.cs \
  tools/AssetConverter/src/AssetConverter/Program.cs \
  tools/AssetConverter/src/AssetConverter/Terrain/TerrainCommand.cs \
  tools/AssetConverter/src/AssetConverter/Terrain/TerrainAllFinalizer.cs \
  tools/AssetConverter/tests/AssetConverter.Tests/MapConversionResultTests.cs \
  tools/AssetConverter/tests/AssetConverter.Tests/Terrain \
  docs/plans/2026-09-08-terrain-brush-part1-converter.md
git commit -m "feat: integrate terrain converter command"
```

## Final red-team review before declaring Part 1 complete

- **Evaluation:** inspect types/call sites to prove held-out accuracy consumes only one top-1 prediction/mask, never emitted variants. Re-run the cardinal-superset blocker fixture.
- **Coordinates/formulas:** compare direction offsets, corner order, support populations, map/region weights, entropy/ambiguity/medoid/visual/confidence equations, all empty behavior, and every tie against this plan.
- **Parsing/schema:** reject duplicate/unknown/null/wrong-kind/invalid enum/range/relationship input with exact `TerrainCatalogException.Error`; verify all nested output order and defensive copies. Part 3 catches this exact type.
- **Diagnostics:** every image-only member has evidence; every pending/disabled set has a status reason; all codes/messages/nulls/value formatting/order/dedup match the contract.
- **Memory/lifecycle:** no collection retains map bytes/documents or sheet images. At most one decoded map and one sheet image is live in its respective pass; every exception disposes/releases before publication.
- **Publication:** validate/serialize before temp creation; call `FileStream.Flush(true)` before close/replace; never delete destination first; preserve the primary exception if cleanup fails.
- **Fingerprint/stale input:** inventory is canonical; unlisted maps and unreferenced PNGs are never read; exact relevant records and holdout bytes match tests.
- **Inference isolation:** holdout masks/adjacency/labels never train graph, mappings, medoids, centroids, or admissions. Global holdout reference awareness only prevents false `ImageOnly` provenance and contributes no support/evaluation label.
- **Conservatism:** deceptive lookalikes, missing centroids, one-family classification, no owner/ties, sparse/single-map diagonals, incomplete masks, graphic zero, and overlaps never false-enable.
- **Orchestration:** `Program` uses the tested production finalizer after successful map names and combined manifest; focused/`all` both call exact `TerrainCommand.Execute` and default settings.
- **Ownership:** Core remains provider-neutral; Part 2 owns Core editing/tool work; Part 3 owns Rendering validation and manager ID recomputation/publication. `TerrainGraphicReference` adapts to Rendering’s `SpriteReference` only in Part 3.
- **Environment:** every mandatory converter gate uses the exact positive `Terrain|MapConversionResultTests` filter and no proprietary paths. Illutia-only and full `all` comparisons are optional manual calibration where the required corpora exist.
- **Scope:** no editor loader, runtime resolver, map edits, UI, manager, map format change, nearest-mask fallback, transition terrain, or generated asset enters Part 1.

## Design alignment and explicit clarifications

This plan preserves every approved design decision: hybrid map/image inference, layer-0 seeding, exact 32×32 frames, map/region weighting, stricter image-only admission with diagnostics, automatic conservative 4/8 selection, complete nonoverlapping enabled sets, stable topology+membership IDs, focused/`all` identity, and atomic failure preservation.

Three implementation clarifications resolve real ambiguities without changing product behavior:

1. Held-out reconstruction uses a deterministic weighted top-1 member prediction per mask, while emitted masks retain all visual variants. Using emitted membership as correctness would make four-way supersets unable to lose and contradict the approved requirement that measured diagonal gain can select eight-way.
2. A generated map inventory declares successful map outputs, so stale ignored files cannot enter the corpus. It is converter metadata, not map-format terrain metadata or a per-map sidecar.
3. Manager topology/member edits immediately recompute the design-defined authored ID; Part 3 keeps immutable draft keys separate from authored IDs and atomically updates the authored ID plus any published-ID reconciliation map. Rename/status/variant reorder remain ID-stable.

Do not weaken combined behavior or silently fall back when either proprietary corpus is absent. The hermetic synthetic integration/process tests are the mandatory implementation gates. Illutia-only smoke and combined `all`/`terrain` `cmp` are optional manual calibration/acceptance evidence on an environment where the corresponding corpora exist.

Plan complete and saved to `docs/plans/2026-09-08-terrain-brush-part1-converter.md`. Ready to implement.
