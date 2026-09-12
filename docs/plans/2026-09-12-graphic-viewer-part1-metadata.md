# Graphic Viewer Part 1: Metadata Foundation Implementation Plan

**Goal:** Generate, parse, index, and cross-validate the animation/category metadata required by the map editor graphic viewer.

**Architecture:** Add a deterministic `animation-manifest.json` sidecar generated from Illutia and Aspereta ADF/compiled metadata without changing `manifest.json` or runtime `.tres` files. Parse the sidecar in `MapEditor.Rendering`, then combine it with `SpriteManifest` in an immutable catalog that owns category, sheet, frame, and animation indexes.

**Tech Stack:** C# 12, .NET 8/10, `System.Text.Json`, xUnit

---

## Design and API decisions

Use an array for animations because source animation IDs are not globally unique. Identity is the composite `(ownerSheet, animationId)`; the UI still displays the source animation ID.

Representative version-1 shape:

```json
{
  "version": 1,
  "sheets": {
    "115": {
      "categories": [
        { "name": "Body", "id": 1 }
      ]
    }
  },
  "animations": [
    {
      "ownerSheet": 115,
      "id": 3205,
      "fps": 8,
      "frames": [[115, 3205], [115, 3206]]
    }
  ]
}
```

`Tiles` and `Spells` entries omit `id`. `All` is an implicit viewer category and is not serialized. Category behavior matches the legacy viewer:

- Equipment category: a sheet occurs in the corresponding `compiled.enc` record.
- Illutia `Spells`: a non-equipment sheet has one or more local animations.
- Illutia `Tiles`: a non-equipment sheet has no local animations.
- Aspereta `Body`: each imported monster Body ID is assigned to every frame-bearing sheet reached by its compiled walk/attack entries.
- Aspereta `Spells`: each frame-bearing sheet reached by an animation in `AsperetaEffectsConverter.IsEffectId`.
- Aspereta `Tiles`: remaining unclaimed frame-bearing sheets.

A sheet can retain multiple equipment category/ID mappings. The converter emits all mappings rather than using `CompiledEnc.SheetToAnimation`, which overwrites duplicate sheet ownership. Aspereta categories follow frame references because most animation definitions live on `0.adf` rather than their image sheets.

## APIs verified before planning

- `FrameManifestBuilder.Build(string, int[]?)` emits Illutia frames: `tools/AssetConverter/src/AssetConverter/Manifest/FrameManifestBuilder.cs:12-17`.
- `FrameManifestBuilder.BuildCombined(string, string)` normalizes Aspereta sheets/graphics: `tools/AssetConverter/src/AssetConverter/Manifest/FrameManifestBuilder.cs:23-38`.
- `AsperetaSheets.Load(string)` assigns numeric-rank sheet IDs; constants are `SheetBase = 20000`, `GraphicBase = 700000`, and `BodyBase = 10000`: `tools/AssetConverter/src/AssetConverter/Aspereta/AsperetaSheets.cs:7-30`.
- `CompiledAnimation` exposes `Type`, `Id`, 44 `AnimationIndexes`, and 11 `AnimationFiles`: `tools/AssetConverter/src/AssetConverter/Adf/IllutiaData.cs:30-57`.
- `CompiledEnc.SheetToAnimation` overwrites by sheet, so it is not a complete category source: `tools/AssetConverter/src/AssetConverter/Adf/IllutiaData.cs:60-96`.
- Illutia ADFs retain ordered `Animation.Frames`; Aspereta ADFs retain unresolved `Animation.SourceFrameIds`: `tools/AssetConverter/src/AssetConverter/Adf/IllutiaData.cs:117-132`, `tools/AssetConverter/src/AssetConverter/Adf/AsperetaAdf.cs:29-68`.
- The `manifest` and `all` CLI branches are at `tools/AssetConverter/src/AssetConverter/Program.cs:129-138` and `:140-203`.
- Rendering uses `SpriteReference(int Sheet, int Graphic)`: `src/MapEditor.Rendering/Assets/SpriteReference.cs:3-6`.
- Cross-validation can use `SpriteManifest.ContainsSheet`, `TryGetSourceRect`, and `GetFrames`: `src/MapEditor.Rendering/Assets/SpriteManifest.cs:40-62`.
- `SpriteManifest.Parse` and `Load` establish strict JSON/file error conventions: `src/MapEditor.Rendering/Assets/SpriteManifest.cs:64-127`.

### Task 1: Generate deterministic Illutia animation metadata

**Files:**
- Create: `tools/AssetConverter/src/AssetConverter/Manifest/AnimationManifestBuilder.cs`
- Create: `tools/AssetConverter/src/AssetConverter/Manifest/AnimationManifestModels.cs`
- Create: `tools/AssetConverter/tests/AssetConverter.Tests/AnimationManifestBuilderTests.cs`
- Create: `tools/AssetConverter/tests/AssetConverter.Tests/Fixtures/AnimationSourceFixture.cs`

**Mutation impact:**
- Source of truth changed: none; this task derives a new document from ADF files and `compiled.enc`.
- Important readers: the new tests initially; the CLI and map editor are added in later tasks.
- Derived/cached state affected: no existing state or output.
- Required propagation sequence: parse all source files into temporary models, classify all sheets, sort, then serialize once.
- Invariants to preserve: source IDs and frame order remain unchanged; duplicate equipment mappings remain visible; output does not depend on directory enumeration order.
- Observable proof required: compare parsed fields and byte-identical output from equivalent fixtures created in different file orders.

**Step 1: Write failing hermetic tests**

Build minimal valid ADF and `compiled.enc` bytes in a temporary directory. Cover:

- `version` is `1` and every animation has `fps: 8`.
- All eight equipment category names map from `AnimationType` values.
- One sheet referenced by two records retains both category/ID mappings.
- An equipment sheet with local animations is not also `Spells`.
- An unclaimed animated sheet is `Spells`.
- An unclaimed static sheet is `Tiles`.
- Ordered frame IDs are preserved.
- Two animations with the same raw ID on different sheets are both emitted.
- Reversed file creation order produces byte-identical JSON.

Expected red failure: `AnimationManifestBuilder` does not exist.

**Step 2: Implement the Illutia builder**

Add a pure builder API:

```csharp
public static string Build(string illutiaDataDir, string compiledEncPath);
```

Implementation requirements:

1. Enumerate `*.adf` paths, reject nonnumeric filenames deterministically, then process numeric paths in numeric order; skip unreadable/non-graphic payloads consistently.
2. Iterate every `CompiledAnimation` and every nonzero `AnimationFiles` entry to collect all category/ID mappings.
3. Emit local ADF animations with `(adf.FileNumber, animation.Id)` identity and ordered frame references.
4. Classify only unclaimed sheets as `Spells` or `Tiles`.
5. Sort sheet IDs, category name then optional ID, and animations by owner sheet then animation ID before serialization.
6. Build the complete model before serialization; do not mutate ADF or compiled models.

Do not use `AnimationBatchConverter.SelectEffectAnimations`: that path intentionally deduplicates animation IDs for runtime effect resources, while this viewer must retain sheet-local definitions.

**Step 3: Run the focused tests**

```bash
dotnet test tools/AssetConverter/tests/AssetConverter.Tests/AssetConverter.Tests.csproj --filter FullyQualifiedName~AnimationManifestBuilderTests
```

Expected: PASS without external asset directories.

**Step 4: Commit**

```bash
git add tools/AssetConverter/src/AssetConverter/Manifest tools/AssetConverter/tests/AssetConverter.Tests
git commit -m "feat: generate Illutia graphic animation metadata"
```

**Invariant-to-test matrix:**

| Invariant | Proved by |
|-----------|-----------|
| Generation is deterministic | Equivalent fixtures created in opposite order produce identical JSON |
| Raw animation ID collisions are retained | Same ID on two owner sheets emits two array entries |
| Category mappings are not overwritten | Shared sheet fixture exposes both equipment records |
| Legacy category rules are preserved | Equipment, standalone animated, and standalone static fixture assertions |

### Task 2: Add combined Aspereta metadata with normalized references

**Files:**
- Modify: `tools/AssetConverter/src/AssetConverter/Manifest/AnimationManifestBuilder.cs`
- Modify: `tools/AssetConverter/src/AssetConverter/Manifest/AnimationManifestModels.cs`
- Modify: `tools/AssetConverter/tests/AssetConverter.Tests/AnimationManifestBuilderTests.cs`
- Modify: `tools/AssetConverter/tests/AssetConverter.Tests/Fixtures/AnimationSourceFixture.cs`

**Mutation impact:**
- Source of truth changed: none; combined metadata is newly derived from Aspereta ADF and compiled data.
- Important readers: the future `all` CLI flow and rendering catalog.
- Derived/cached state affected: no existing `.tres`, frame manifest, or in-memory ADF object may be changed.
- Required propagation sequence: load and rank sheets, build a raw-frame ownership index, resolve each animation frame, normalize every reference, merge with Illutia entries, sort, serialize.
- Invariants to preserve: normalization exactly matches the existing combined frame manifest; cross-sheet frame order survives; ambiguous frame ownership fails before output is published.
- Observable proof required: parse generated JSON and resolve every emitted normalized reference against a frame manifest built from the same fixture.

**Step 1: Add failing combined-data tests**

Add synthetic Aspereta fixtures proving:

- Valid graphic sheets receive `20000 + numeric rank`; malformed, sound, and empty sheets do not consume a rank.
- Every graphic ID is `700000 + sourceFrameId`.
- Monster body IDs are `10000 + sourceBodyId`, and every sheet reached by that monster's compiled walk/attack frame references receives the Body mapping.
- Effect definitions selected by `AsperetaEffectsConverter.IsEffectId` assign `Spells` to every frame-bearing sheet they reference, rather than only to definition sheet `0.adf`.
- Standalone effects use normalized owner sheet and frame references.
- One animation referencing frames on two source sheets preserves order and emits two normalized sheet IDs.
- Duplicate source frame IDs on different sheets fail with a deterministic diagnostic rather than choosing one silently.
- Every emitted frame exists in `FrameManifestBuilder.BuildCombined(...)` output.

Expected red failure: no combined overload exists.

**Step 2: Implement combined generation**

Add:

```csharp
public static string BuildCombined(
    string illutiaDataDir,
    string illutiaCompiledEncPath,
    string asperetaDataDir,
    string asperetaCompiledEncPath);
```

Use `AsperetaSheets.Load` as the canonical rank mapping. Build a separate source graphic-ID ownership index and resolve each `SourceFrameIds` entry independently so one animation may cross sheets. Do not reuse the existing monster/effect resolver restriction that requires one texture sheet. Do not mutate `AdfFile.FileNumber`; existing runtime resource generation mutates it in places and must remain independent.

Build raw animation-ID and frame-ID indexes first. For each imported monster entry (`Type == Body && Id > 100`), resolve every nonzero walk/attack slot as either an animation definition or the single-frame fallback used by `AsperetaMonsterConverter`; assign the normalized Body ID to every resolved frame-bearing sheet. For effects, use `AsperetaEffectsConverter.IsEffectId` and assign `Spells` to every resolved frame-bearing sheet. Classify remaining unclaimed frame-bearing sheets as `Tiles`. Normalize effect animation IDs with `GraphicBase` exactly as the existing effect pipeline does; keep owner sheet in the composite identity regardless.

**Step 3: Run focused converter tests**

```bash
dotnet test tools/AssetConverter/tests/AssetConverter.Tests/AssetConverter.Tests.csproj --filter FullyQualifiedName~AnimationManifestBuilderTests
```

Expected: PASS hermetically.

**Step 4: Commit**

```bash
git add tools/AssetConverter/src/AssetConverter/Manifest tools/AssetConverter/tests/AssetConverter.Tests
git commit -m "feat: include Aspereta animation metadata"
```

**Invariant-to-test matrix:**

| Invariant | Proved by |
|-----------|-----------|
| Sheet/graphic/body normalization matches existing assets | Explicit base/rank assertions |
| Cross-sheet sequences retain order | Alternating-sheet synthetic animation assertion |
| Metadata references real normalized frames | Generated sidecar references resolve in generated frame manifest |
| Ambiguous source ownership is never guessed | Duplicate-frame adversarial fixture throws deterministically |

### Task 3: Publish paired manifests from converter commands

**Files:**
- Create: `tools/AssetConverter/src/AssetConverter/Manifest/ManifestFileStore.cs`
- Create: `tools/AssetConverter/tests/AssetConverter.Tests/ManifestFileStoreTests.cs`
- Modify: `tools/AssetConverter/src/AssetConverter/Program.cs:129-203`

**Mutation impact:**
- Source of truth changed: converter command output now includes `animation-manifest.json` beside `manifest.json`.
- Important readers: map editor asset directories; existing Godot and map-editor frame-manifest readers continue reading only `manifest.json`.
- Derived/cached state affected: generated files under ignored `Assets/`; no checked-in runtime resource changes.
- Required propagation sequence: build both JSON strings completely in memory, create the output directory, then write both deterministic files; rendering-side strict cross-validation detects any interrupted or mismatched pair.
- Invariants to preserve: existing `manifest [outPath]` and `all [repoRoot]` output locations remain valid; a build failure writes neither newly generated string; ordinary frame-manifest consumers are unchanged.
- Observable proof required: temp-directory tests assert both sibling files and repeatable bytes.

**Step 1: Write failing file-store tests**

Cover:

- Illutia-only write places the sidecar in the same directory as an arbitrary `manifest` output path.
- Combined write places both files under `<repoRoot>/Assets/Sprites`.
- Builders run before either destination is opened; a thrown sidecar build leaves pre-existing destination files unchanged.
- Repeated writes are byte-identical.

Use injected string factories in the file-store test surface so failure ordering is hermetic and does not require real datasets. This task does not add a transaction: a process or filesystem failure between writes is detected later by strict rendering-side cross-validation.

Expected red failure: `ManifestFileStore` does not exist.

**Step 2: Implement the file store**

Define `AnimationFileName = "animation-manifest.json"`. The helper owns output paths and file writes but does not parse source datasets. Its precondition is two already-built strings, or factories that are both evaluated before publication. Avoid a transaction/staging system; strict load-time cross-validation is the consistency backstop approved in the design.

**Step 3: Wire both CLI paths**

- `manifest`: call the Illutia builder with `Paths.IllutiaData` and `Paths.CompiledEnc`; write beside the requested frame-manifest path.
- `all`: after all source conversions needed for combined metadata, build the combined frame and animation strings and publish them together under `Assets/Sprites`.
- Print both output paths.
- Do not call the `tiles` command or alter `tile-sheets.json`; animation categories follow the legacy rules and are independent of the palette's curated tile-sheet list.

**Step 4: Run tests**

```bash
dotnet test tools/AssetConverter/tests/AssetConverter.Tests/AssetConverter.Tests.csproj --filter "FullyQualifiedName~AnimationManifestBuilderTests|FullyQualifiedName~ManifestFileStoreTests"
```

Expected: PASS without external datasets.

If local integration data is available, additionally run:

```bash
ASPERETA_DATA=/home/agent/workspace/gooseclient/AsperetaClient/bin/Release/net8/data \
ASPERETA_MAPS=/home/agent/workspace/gooseclient/AsperetaClient/bin/Release/net8/maps \
dotnet test tools/AssetConverter/AssetConverter.sln --no-restore
```

Record the known baseline separately: four dataset-pinned tests currently fail with the installed data. Do not change those expectations in this task.

**Step 5: Commit**

```bash
git add tools/AssetConverter/src/AssetConverter/Manifest tools/AssetConverter/src/AssetConverter/Program.cs tools/AssetConverter/tests/AssetConverter.Tests
git commit -m "feat: publish graphic animation manifest"
```

**Invariant-to-test matrix:**

| Invariant | Proved by |
|-----------|-----------|
| Both CLI modes publish sibling manifests | Illutia-only and combined path tests |
| Builder failure does not publish a new partial pair | Throwing-factory adversarial test |
| Existing frame-manifest format remains unchanged | Existing `FrameManifestBuilderTests` plus exact output fixture |

### Task 4: Parse the sidecar into immutable rendering models

**Files:**
- Create: `src/MapEditor.Rendering/Graphics/GraphicCategory.cs`
- Create: `src/MapEditor.Rendering/Graphics/GraphicAnimationManifest.cs`
- Create: `src/MapEditor.Rendering/Graphics/GraphicAnimationManifestException.cs`
- Create: `tests/MapEditor.Rendering.Tests/GraphicAnimationManifestTests.cs`

**Mutation impact:**
- Source of truth changed: none; this task adds an immutable in-memory representation of the sidecar.
- Important readers: the catalog in Task 5 and viewer in Part 2.
- Derived/cached state affected: no existing caches.
- Required propagation sequence: parse into private temporary dictionaries/lists, fully validate, sort, publish read-only collections.
- Invariants to preserve: malformed input never publishes a partial manifest; composite animation identity retains duplicate raw IDs on different sheets; normal map editing still does not require this sidecar.
- Observable proof required: parser tests assert values and typed failures, including adversarial duplicate/property cases.

**Step 1: Write failing parser tests**

Cover valid parsing plus:

- Missing, duplicate, non-integer, and unsupported `version`.
- Malformed JSON and wrong root/property kinds.
- Unknown category names and category IDs with invalid presence rules.
- Invalid owner sheet, animation ID, FPS, or frame tuples, including graphic ID `0`, which the existing sprite cache reserves as empty and therefore cannot preview.
- Duplicate normalized sheet keys such as `"1"` and `"01"`.
- Duplicate `(ownerSheet, animationId)` entries while allowing the same raw ID on different owners.
- Duplicate category/ID mappings.
- Empty animation frame lists.
- Deterministic numeric ordering and non-mutable published collections.
- `Load(assetDirectory)` reports the exact `animation-manifest.json` path for missing/unreadable files.

Expected red failure: the parser types do not exist.

**Step 2: Implement minimal models and parser**

Recommended public records:

```csharp
public enum GraphicCategory { Body, Hair, Eyes, Chest, Helm, Legs, Feet, Hand, Tiles, Spells }
public readonly record struct GraphicCategoryMapping(GraphicCategory Category, int? Id);
public readonly record struct GraphicAnimationKey(int OwnerSheet, int AnimationId);
public sealed record GraphicAnimation(GraphicAnimationKey Key, int FramesPerSecond, IReadOnlyList<SpriteReference> Frames);
```

Expose `Parse(string json, string sourcePath = "<memory>")` and `Load(string assetDirectory)`. Follow `SpriteManifest` conventions for duplicate-property detection, strict integer IDs, typed causes, and all-or-nothing publication. Reject animation frame graphic ID `0` because `SpriteAssetCache.Resolve` reserves it as empty and cannot preview it, even though the broader frame manifest parser accepts zero for compatibility.

**Step 3: Run focused tests**

```bash
dotnet test tests/MapEditor.Rendering.Tests/MapEditor.Rendering.Tests.csproj --filter FullyQualifiedName~GraphicAnimationManifestTests
```

Expected: PASS.

**Step 4: Commit**

```bash
git add src/MapEditor.Rendering/Graphics tests/MapEditor.Rendering.Tests/GraphicAnimationManifestTests.cs
git commit -m "feat: parse graphic animation metadata"
```

**Invariant-to-test matrix:**

| Invariant | Proved by |
|-----------|-----------|
| Invalid documents publish nothing | Typed exception tests for late invalid entries |
| Composite keys resolve ID collisions | Same-ID/different-owner valid test and exact-key duplicate invalid test |
| Collections are deterministic and immutable | Numeric-order and mutation-attempt tests |

### Task 5: Build indexed, strictly cross-validated graphic catalog

**Files:**
- Create: `src/MapEditor.Rendering/Graphics/GraphicAssetCatalog.cs`
- Create: `tests/MapEditor.Rendering.Tests/GraphicAssetCatalogTests.cs`
- Modify: `src/MapEditor.Rendering/Graphics/GraphicAnimationManifestException.cs`

**Mutation impact:**
- Source of truth changed: none; the catalog derives immutable indexes from two manifests.
- Important readers: Part 2 viewer category, details, selection, and playback state.
- Derived/cached state affected: category-to-sheet, sheet-to-mapping, frame-to-animation, and key-to-animation indexes.
- Required propagation sequence: validate every categorized sheet and frame reference, build temporary indexes, sort each result, then publish the catalog only after all checks pass.
- Invariants to preserve: every returned reference exists in `SpriteManifest`; all mappings and matching animations remain visible; a single bad late entry rejects the whole catalog.
- Observable proof required: tests query final indexes and prove an invalid final entry leaves no catalog instance.

**Step 1: Write failing catalog tests**

Cover:

- `All` returns all `SpriteManifest.SheetIds`, not only categorized sheets.
- Every named category returns sorted eligible sheet IDs.
- A sheet returns all category/equipment mappings.
- A frame returns every containing animation in `(ownerSheet, animationId)` order.
- Animation lookup by composite key returns ordered frames and FPS.
- One frame may belong to multiple animations.
- Missing categorized sheets, missing animation owner sheets, and missing frame references fail.
- A bad final frame after valid entries rejects construction.
- `Load(assetDirectory)` composes `SpriteManifest.Load` and `GraphicAnimationManifest.Load` and preserves typed sidecar diagnostics.

Expected red failure: `GraphicAssetCatalog` does not exist.

**Step 2: Implement and validate before publication**

Recommended API:

```csharp
public static GraphicAssetCatalog Create(SpriteManifest sprites, GraphicAnimationManifest animations);
public static GraphicAssetCatalog Load(string assetDirectory);
public IReadOnlyList<int> GetSheets(GraphicCategory? category);
public IReadOnlyList<GraphicCategoryMapping> GetMappings(int sheet);
public IReadOnlyList<GraphicAnimation> GetAnimations(SpriteReference frame);
public bool TryGetAnimation(GraphicAnimationKey key, out GraphicAnimation animation);
```

Use `null` for the implicit `All` query rather than adding `All` to the serialized enum. Validate every category sheet, every animation owner sheet, and every frame reference with `SpriteManifest.ContainsSheet` and `TryGetSourceRect`. Build all indexes in locals so constructor failure cannot expose partial state.

**Step 3: Run focused and regression tests**

```bash
dotnet test tests/MapEditor.Rendering.Tests/MapEditor.Rendering.Tests.csproj --filter "FullyQualifiedName~GraphicAnimationManifestTests|FullyQualifiedName~GraphicAssetCatalogTests"
dotnet test tests/MapEditor.Rendering.Tests/MapEditor.Rendering.Tests.csproj
```

Expected: PASS.

**Step 4: Run Part 1 gate**

```bash
dotnet test tools/AssetConverter/tests/AssetConverter.Tests/AssetConverter.Tests.csproj --filter "FullyQualifiedName~AnimationManifestBuilderTests|FullyQualifiedName~ManifestFileStoreTests"
dotnet test Goose2ClientGodot.sln
```

Expected: all hermetic new tests and the main solution pass. External converter integration failures remain documented separately.

**Step 5: Commit**

```bash
git add src/MapEditor.Rendering/Graphics tests/MapEditor.Rendering.Tests
git commit -m "feat: index graphic viewer metadata"
```

**Invariant-to-test matrix:**

| Invariant | Proved by |
|-----------|-----------|
| Every catalog frame resolves in `manifest.json` | Missing-reference and valid-load tests |
| `All` is not accidentally tile-filtered | Mixed-category sprite manifest test |
| Reverse indexes retain all matches | Shared-frame multi-animation test |
| Failed validation publishes no partial catalog | Late-invalid-reference adversarial test |

## Part 1 completion criteria

- `manifest` and `all` generate sibling frame and animation manifests.
- The new converter tests are hermetic and pass without machine-local datasets.
- `MapEditor.Rendering` can load and strictly validate the pair.
- No normal map-editor or Godot code requires the sidecar yet.
- Main solution tests pass.
