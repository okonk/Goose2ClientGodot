# Terrain Brush Part 1: Catalog Foundation Implementation Plan

**Goal:** Add the immutable flat terrain catalog, deterministic JSON contract, semantic and sprite-manifest validation, revision-aware atomic storage, and terrain-only asset availability.

**Architecture:** `MapEditor.Core` owns provider-neutral terrain definitions, stable colors, validation, and immutable indexes. `MapEditor.Rendering` owns the versioned JSON and sprite-manifest cross-validation. `MapEditor.App` owns file-system persistence and publishes the initially loaded terrain state through the existing asset context without making terrain failures block ordinary sprites.

**Tech Stack:** C# 12, .NET 8/10, `System.Text.Json`, SHA-256, Avalonia 11, xUnit.

---

Part 1 of 4. Implement in `/home/agent/workspace/Goose2ClientGodot/.worktrees/terrain-brush-editor`.

## APIs verified before planning

- Map graphics are `MapTileLayer(int Sheet, int Graphic)` at `src/MapEditor.Core/MapDocument.cs:5`; Core must not depend on Rendering's sprite types.
- Rendering requires 32×32 tiles and exposes manifest frame lookup through `SpriteManifest.RequiredTileSize` and `TryGetSourceRect` at `src/MapEditor.Rendering/Assets/SpriteManifest.cs:12-20,43-59`.
- `SpriteAssetCache.Open` loads the manifest without decoding PNG sheets at `src/MapEditor.Rendering/Assets/SpriteAssetCache.cs:48-52`; terrain validation must not call `Resolve`, which loads sheets at `src/MapEditor.Rendering/Assets/SpriteAssetCache.cs:54-107`.
- Optional appearance and graphic-viewer sidecars already degrade independently in `AssetContext.Create` at `src/MapEditor.App/Rendering/AssetContext.cs:61-89`.
- A successful asset-root replacement updates documents, raises `CurrentChanged`, then disposes the old context at `src/MapEditor.App/Rendering/AssetContextController.cs:68-91`.
- The existing atomic map store writes and flushes a sibling temporary before checking revision and replacing at `src/MapEditor.Core/MapFileStore.cs:31-81`; terrain storage will follow this ordering with injectable file operations.

## Locked catalog contract

Use these Core concepts under `src/MapEditor.Core/Terrain/`:

```csharp
public readonly record struct TerrainGraphicReference(int Sheet, int Graphic);
public readonly record struct TerrainColor(byte R, byte G, byte B);

public enum TerrainPeer
{
    Center, North, East, South, West,
    NorthEast, SouthEast, SouthWest, NorthWest
}

public sealed record TerrainDefinition(Guid Id, string Name, TerrainColor? ColorOverride);

public readonly record struct TerrainPattern(
    Guid? Center,
    Guid? North,
    Guid? East,
    Guid? South,
    Guid? West,
    Guid? NorthEast,
    Guid? SouthEast,
    Guid? SouthWest,
    Guid? NorthWest);

public sealed record TerrainGraphicDefinition(
    TerrainGraphicReference Reference,
    TerrainPattern Pattern);
```

`TerrainCatalog` copies all incoming collections. `TerrainCatalogIndex.Create` copies/group-sorts its indexes and exposes read-only terrain lookup, graphic lookup, candidates by center, representative graphics, and display colors. IDs use `Guid`; `Guid.Empty` is invalid. Names are unique with `StringComparer.OrdinalIgnoreCase` while serialized ordering is stable by canonical lowercase GUID text.

Lock JSON version 1 to this shape:

```json
{
  "version": 1,
  "terrains": [
    { "id": "00000000-0000-0000-0000-000000000001", "name": "Grass", "color": null }
  ],
  "graphics": [
    {
      "sheet": 1,
      "graphic": 10,
      "center": "00000000-0000-0000-0000-000000000001",
      "north": null,
      "east": null,
      "south": null,
      "west": null,
      "northEast": null,
      "southEast": null,
      "southWest": null,
      "northWest": null
    }
  ]
}
```

Canonical output uses UTF-8 without BOM, two-space indentation, lowercase GUIDs, `#RRGGBB` color overrides, terrain ordering by ID, graphics by `(sheet, graphic)`, the shown property order, explicit nulls, and one final newline. Reject unknown properties at every level.

## Task 1: Add immutable Core models and stable display colors

**Files:**
- Create: `src/MapEditor.Core/Terrain/TerrainCatalog.cs`
- Create: `src/MapEditor.Core/Terrain/TerrainColor.cs`
- Create: `src/MapEditor.Core/Terrain/TerrainPeer.cs`
- Test: `tests/MapEditor.Core.Tests/Terrain/TerrainCatalogTests.cs`

**Mutation impact:**
- Source of truth changed: new immutable `TerrainCatalog`; no existing map state changes.
- Important readers: validator/index in Task 2, JSON in Task 3, and editor drafts in Part 3.
- Derived/cached state affected: display color derives only from stable terrain ID; no existing cache.
- Required propagation: constructor defensively copies → consumers receive read-only snapshots → later indexes copy again.
- Invariants: callers cannot mutate published nested collections; rename never changes derived color; all nine peers preserve orientation.
- Observable proof: mutate constructor input collections after construction and assert the catalog remains unchanged.

**Steps:**
1. Add failing tests for all nine `TerrainPattern` positions, defensive copies of terrain/graphic lists, equality, override colors, and rename-independent derived colors.
2. Pin the derived-color algorithm: SHA-256 over the lowercase `Guid.ToString("N")` UTF-8 bytes; read hue seed as big-endian `(digest[0] << 8) | digest[1]`; compute hue as `seed * 360.0 / 65536.0`, saturation `0.65`, and value `0.90`; convert HSV to RGB and round channel values with `MidpointRounding.AwayFromZero`. Golden vectors are `00000000-0000-0000-0000-000000000001 → #E650BD`, `01234567-89ab-cdef-0123-456789abcdef → #9FE650`, and `ffffffff-ffff-ffff-ffff-ffffffffffff → #C1E650`.
3. Implement the minimal immutable records and catalog. Do not expose arrays or mutable list implementations through public properties.
4. Run `dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj --filter TerrainCatalogTests -v minimal` and expect PASS.
5. Commit: `git commit -m "feat: add terrain catalog model"`.

| Invariant | Proved by |
|---|---|
| Published catalog cannot be changed through source collections | `Constructor_DefensivelyCopiesNestedCollections` |
| Every peer maps to its named direction | `Pattern_Get_ReturnsAllNinePeers` |
| Derived colors are stable and rename-independent | `DeriveColor_UsesStableIdGoldenVectors` |

## Task 2: Add semantic validation and immutable indexes

**Files:**
- Create: `src/MapEditor.Core/Terrain/TerrainValidation.cs`
- Create: `src/MapEditor.Core/Terrain/TerrainCatalogValidator.cs`
- Create: `src/MapEditor.Core/Terrain/TerrainCatalogIndex.cs`
- Test: `tests/MapEditor.Core.Tests/Terrain/TerrainCatalogValidatorTests.cs`
- Test: `tests/MapEditor.Core.Tests/Terrain/TerrainCatalogIndexTests.cs`
- Test helper: `tests/MapEditor.Core.Tests/Terrain/TerrainCatalogFixture.cs`

**Mutation impact:**
- Source of truth changed: none; validator and index consume catalog snapshots.
- Important readers: Rendering load state and Part 2 resolver.
- Derived/cached state affected: ID, graphic, center-candidate, pattern-variant, color, and representative indexes.
- Required propagation: validate complete snapshot → reject on any error → construct all indexes from the validated copy → expose no partially built index.
- Invariants: one graphic has one pattern; duplicate patterns remain unbiased variants; invalid catalogs produce ordered contextual issues and no runtime index.
- Observable proof: shuffled valid inputs create equivalent indexes; duplicate graphic input creates no index.

**Steps:**
1. Define ordered `TerrainValidationIssue` records with severity, code, message, and optional terrain ID, graphic reference, and peer. Errors block publication; warnings do not.
2. Add red tests for empty IDs, case-insensitive duplicate names, centerless graphics, unknown peer IDs with exact peer context, terrain without a centered graphic, duplicate references, `Graphic == 0`, sheets outside `short` range, and valid duplicate complete patterns.
3. Add a bounded canonical coverage matrix. For each center terrain A, warn once when its all-A interior pattern is missing. Always compare A against `None`; also compare it against each distinct terrain B other than A that appears in any A-centered peer or has A in one of its peers. For each ordered A/B pair, define exact patterns with center A: isolated has all eight peers B; straight North has N/NE/NW B and the other five A, then rotate four ways; convex NorthEast has N/NE/E B and the other five A, then rotate four ways; concave NorthEast has only NE B and the other seven A, then rotate four ways. Warn for each missing pattern. Coverage is informational and bounded, not an exhaustive `(terrain count + 1)^8` enumeration.
4. Define the exact result API:
   ```csharp
   public sealed record TerrainCatalogValidationResult(
       IReadOnlyList<TerrainValidationIssue> Issues,
       TerrainCatalogIndex? Index)
   {
       public bool IsValid { get; }
   }

   public static TerrainCatalogValidationResult TerrainCatalogValidator.Validate(
       TerrainCatalog catalog);
   ```
   Build the index only when there are no error-severity issues.
5. Implement deterministic issue ordering by severity/code/terrain/reference/peer. Group candidates by complete pattern before variant lists; sort patterns by canonical nullable peer values and variants by `(Sheet, Graphic)`. Representative selection prefers most same-center peers, then `(Sheet, Graphic)`.
6. Add adversarial tests proving variants do not weight pattern groups, input order does not affect lookup/representative results, and `(sheet, 0)` is rejected even if a manifest could contain it.
7. Run focused Core tests, then the whole Core suite.
8. Commit: `git commit -m "feat: validate and index terrain catalogs"`.

| Invariant | Proved by |
|---|---|
| Every persisted graphic has one known center | `Validate_CenterlessOrUnknownCenter_ReturnsContextualError` |
| Duplicate variants do not bias candidate patterns | `Index_GroupsDuplicatePatternsBeforeVariants` |
| Validation/index output is input-order independent | `CreateValidated_ShuffledCatalog_HasEquivalentIndexes` |

## Task 3: Implement strict deterministic JSON

**Files:**
- Create: `src/MapEditor.Rendering/Terrain/TerrainCatalogJson.cs`
- Create: `src/MapEditor.Rendering/Terrain/TerrainCatalogFormatException.cs`
- Test: `tests/MapEditor.Rendering.Tests/Terrain/TerrainCatalogJsonTests.cs`

**Mutation impact:**
- Source of truth changed: terrain catalog bytes become a persisted contract.
- Important readers: asset loader in Task 4 and file store in Task 5.
- Derived/cached state affected: none; parsing returns a new immutable catalog.
- Required propagation: bytes → strict syntax/schema parse → Core catalog → semantic validation before publication; serialization sorts copies without mutating source order.
- Invariants: malformed/unsupported data is never accepted as empty; canonical bytes are independent of input ordering; null means `None`.
- Observable proof: parse shuffled equivalent JSON and assert byte-identical canonical serialization.

**Steps:**
1. Write red golden tests for the exact version-1 document above, UTF-8/no-BOM output, final newline, lowercase GUIDs, fixed peer ordering, and `#RRGGBB` parsing.
2. Test malformed JSON, non-object root, missing/duplicate/wrong-type properties, unknown properties, unsupported version, invalid GUID/color, integer overflow, and source-path text in typed failures.
3. Implement `Parse(string json, string sourcePath = "<memory>")`, `Load(string path)`, and `Serialize(TerrainCatalog)` using explicit `JsonDocument` reads and `Utf8JsonWriter`; do not rely on reflection property order.
4. Round-trip centerless patterns at the syntax layer so Part 3 can diagnose draft models, but ensure Task 2 validation blocks publication/save.
5. Run `dotnet test tests/MapEditor.Rendering.Tests/MapEditor.Rendering.Tests.csproj --filter TerrainCatalogJsonTests -v minimal`.
6. Commit: `git commit -m "feat: add terrain catalog json contract"`.

| Invariant | Proved by |
|---|---|
| Unsupported or malformed JSON cannot become an empty catalog | `Parse_UnsupportedVersion_ThrowsTypedFailure`, `Parse_Malformed_ThrowsTypedFailure` |
| Equivalent catalogs serialize identically | `Serialize_ShuffledInput_ProducesGoldenBytes` |
| `None` has one wire representation | `RoundTrip_NullPeers_RemainNull` |

## Task 4: Cross-validate terrain graphics against the sprite manifest

**Files:**
- Create: `src/MapEditor.Rendering/Terrain/TerrainAssetCatalog.cs`
- Create: `src/MapEditor.Rendering/Terrain/TerrainCatalogLoadResult.cs`
- Create: `src/MapEditor.Rendering/Terrain/TerrainFileRevision.cs`
- Test: `tests/MapEditor.Rendering.Tests/Terrain/TerrainAssetCatalogTests.cs`

**Mutation impact:**
- Source of truth changed: none; combines catalog and manifest snapshots.
- Important readers: `AssetContext` in Task 6, Part 2 resolver, and Part 3 editor.
- Derived/cached state affected: publishable `TerrainCatalogIndex` and validity/paintability state.
- Required propagation: parse → Core validation → inspect every graphic with `SpriteManifest.TryGetSourceRect` → publish catalog/index only when all errors are absent.
- Invariants: PNG decoding is never required; missing JSON is valid empty authoring state; invalid terrain never invalidates the sprite manifest.
- Observable proof: validate directly from a `SpriteManifest` and assert no sheet image or `SpriteAssetCache.Resolve` API is required.

**Steps:**
1. Define `TerrainFileRevision(bool Exists, string ContentHash)` in Rendering using SHA-256; missing is `(false, "")`, while an empty file is `(true, SHA256(empty))`.
2. Define `TerrainCatalogLoadResult` with `SourcePath`, `Revision`, `Catalog`, `Index`, ordered issues, `IsValid`, `CanAuthor`, `CanPaint`, and `Diagnostic`. No asset root is unavailable (`CanAuthor == false`); missing JSON under a valid root is valid empty (`CanAuthor == true`, `CanPaint == false`); malformed/invalid retains its raw file revision but has no catalog/index.
3. Write red tests for valid, empty, file-path-is-a-directory, unreadable, malformed, missing frame, non-32×32 frame, semantic errors plus manifest errors, and deterministic merged issue order.
4. Implement `TerrainAssetCatalog.Validate(catalog, manifest)` using only `TryGetSourceRect`; a frame is valid only when both dimensions equal `SpriteManifest.RequiredTileSize`.
5. Implement `Load(assetDirectory, manifest)`: read bytes and revision once, parse those exact bytes, and carry the same revision into the result. An absent file returns valid empty; expected read/parse/validation failures become invalid terrain with path-specific diagnostics; out-of-memory remains unhandled.
6. Add an adversarial test that a valid manifest containing graphic zero still cannot publish terrain graphic zero.
7. Run Rendering terrain tests and the full Rendering suite.
8. Commit: `git commit -m "feat: load validated terrain assets"`.

| Invariant | Proved by |
|---|---|
| Only manifest-declared 32×32 graphics publish | `Validate_MissingOrOversizedGraphic_ReturnsNoIndex` |
| Validation does not load PNG files | `Validate_UsesManifestWithoutSpriteResolution` |
| Missing and malformed files remain distinct | `Load_Missing_ReturnsValidEmpty`, `Load_Malformed_ReturnsInvalid` |

## Task 5: Add revision-aware atomic terrain storage

**Files:**
- Create: `src/MapEditor.App/Terrain/ITerrainCatalogFileOperations.cs`
- Create: `src/MapEditor.App/Terrain/TerrainExternalChangeException.cs`
- Create: `src/MapEditor.App/Terrain/TerrainCatalogFileStore.cs`
- Create: `src/MapEditor.App/Terrain/TerrainCatalogSaveResult.cs`
- Test: `tests/MapEditor.App.Tests/TerrainCatalogFileStoreTests.cs`

**Mutation impact:**
- Source of truth changed: `terrain-brushes.json` and its loaded revision.
- Important readers: initial asset loading, Part 3 draft baseline, and Part 4 live publisher.
- Derived/cached state affected: returned validated catalog/index and content-hash revision; no live context is mutated by the store.
- Required propagation: `PrepareSave` validates against current manifest and serializes canonical bytes without writing → caller reserves publication → `Save(prepared)` writes/flushes/closes a unique sibling temp → compares destination revision → performs one atomic overwrite move → returns the same prepared catalog/index with its new revision; caller publishes later.
- Invariants: failed validation performs no I/O; failed write/conflict leaves destination intact; missing-file revision differs from empty-file revision; only this operation's temp is cleaned.
- Observable proof: inject failures at every I/O boundary and compare exact destination bytes.

**Steps:**
1. Reuse Rendering's `TerrainFileRevision`; never reopen a published catalog without carrying its original revision, including invalid/malformed files.
2. Define file operations matching actual calls: file/directory status and read, `CreateNew` sibling stream, durable flush, atomic overwrite move, and delete. Production uses `FileStream(... FileOptions.WriteThrough)` and `Flush(true)`, mirroring `MapFileStore` at `src/MapEditor.Core/MapFileStore.cs:41-60`.
3. Write red tests for open valid/missing/invalid, deterministic bytes, successful replacement, newly appeared destination conflict, changed destination conflict, injected create/write/flush/dispose/move/delete failures, and primary-exception preservation during cleanup.
4. Implement `Open(assetDirectory, manifest)` returning the same revision-bearing `TerrainCatalogLoadResult` contract as initial loading. Implement `PrepareSave(assetDirectory, catalog, manifest, expectedRevision)` returning `TerrainCatalogPreparedSave` with a unique operation ID, canonical bytes, valid catalog/index, source path, and expected revision; it performs no file mutation. Implement `Save(TerrainCatalogPreparedSave)` so `TerrainCatalogSaveResult` retains that operation ID and the exact prepared catalog/index references while adding the new durable revision. The store does not publish.
5. Ensure overwrite confirmation in Part 3 creates a new prepared save using the newly observed revision; never add a `force` flag that disables the pre-move check. Document and test the same unavoidable TOCTOU limitation as `MapFileStore`: a separate writer can still race between the final revision read and atomic move, because the supported file APIs do not provide portable compare-and-replace.
6. Run the focused App tests and full App suite.
7. Commit: `git commit -m "feat: atomically store terrain catalogs"`.

| Invariant | Proved by |
|---|---|
| Durable replacement precedes publishable save result | `Save_FlushesAndMovesBeforeReturningResult` |
| Conflict/failure preserves destination bytes | `Save_ChangedRevision_LeavesDestinationUntouched`, injected-failure theory |
| Missing and empty revisions differ | `ReadRevision_MissingAndEmpty_AreDistinct` |

## Task 6: Expose initial terrain state through AssetContext

**Files:**
- Modify: `src/MapEditor.App/Rendering/AssetContext.cs:7-102`
- Modify: `src/MapEditor.App/Rendering/AssetContextController.cs:39-106`
- Modify: `tests/MapEditor.App.Tests/Fixtures/AssetFixture.cs:9-58`
- Test: `tests/MapEditor.App.Tests/AssetContextControllerTests.cs`

**Mutation impact:**
- Source of truth changed: `AssetContext` gains the initially loaded immutable `TerrainCatalogLoadResult`, including the exact source path and loaded revision.
- Important readers: Parts 3–4; existing map renderer, palettes, appearance, and Graphic Viewer continue reading the same cache/context properties.
- Derived/cached state affected: terrain availability only. Sprite cache, renderer, sheet IDs, tint cache, appearance, and graphics are unchanged.
- Required propagation: open base cache/manifest → load optional terrain result → construct complete candidate context → swap once through existing controller ordering → notify once → dispose only replaced context.
- Invariants: malformed terrain still opens the asset directory; base-manifest failure still rejects the whole candidate; terrain load never causes PNG decoding; later terrain-only publication must not use `CurrentChanged` or dispose the context.
- Observable proof: malformed terrain yields usable `Current.Resolve(...)`, and replacing roots still disposes only the old context.

**Steps:**
1. Extend `AssetFixture` with terrain JSON helpers and add red controller tests for missing, valid, malformed, unsupported, semantic-invalid, and manifest-invalid catalogs.
2. Add a terrain load-result property to `AssetContext.CreateUnavailable/Create`; catch terrain-specific failures inside terrain loading rather than in `AssetContextController.TryOpen`, whose `IOException` path currently rejects the whole root at `src/MapEditor.App/Rendering/AssetContextController.cs:49-65`.
3. Prove successful root replacement still raises exactly one `CurrentChanged`; do not add live terrain publication yet.
4. Add a regression test that malformed terrain leaves sheet IDs, ordinary sprite resolution, appearance availability, Graphic Viewer availability, and map state unaffected.
5. Run all three map-editor suites and `dotnet build src/MapEditor.App/MapEditor.App.csproj -c Release`.
6. Commit: `git commit -m "feat: expose terrain asset availability"`.

| Invariant | Proved by |
|---|---|
| Terrain failure cannot block normal assets | `TryOpen_MalformedTerrain_KeepsSpriteContextUsable` |
| Root publication remains all-or-nothing | existing replacement tests plus `TryOpen_InvalidBaseManifest_PreservesCurrent` |
| Terrain-only state has no cache ownership | identity/disposal assertions in replacement tests |

## Part 1 completion

Run:

```bash
dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj -v minimal
dotnet test tests/MapEditor.Rendering.Tests/MapEditor.Rendering.Tests.csproj -v minimal
dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj -v minimal
dotnet build src/MapEditor.App/MapEditor.App.csproj -c Release
git diff --check
git status --short
```

Expected: all tests and build pass; only intentional plan/implementation changes remain. Verify the full feature range has not changed AssetConverter or the map codec:

```bash
test -z "$(git diff --name-only 5711959...HEAD -- tools/AssetConverter)"
git diff --exit-code 5711959...HEAD -- src/MapEditor.Core/MapCodec.cs
```
