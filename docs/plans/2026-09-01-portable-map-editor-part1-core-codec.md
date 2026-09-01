# Portable Map Editor — Part 1 of 4: Shared Core and Codec Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Create the engine-independent `MapEditor.Core` foundation, including the compact shared map model, constrained new-map creation, strict little-endian decode/encode, guarded same-filesystem local file replacement, content-based external-change detection, and regression-safe migration of the Godot client from `Scripts/MapFile.cs` to the shared project.

**Architecture:** `MapEditor.Core` targets `net8.0` and has no Godot, Avalonia, rendering, or asset dependencies. A `MapDocument` owns one row-major array of value-type `MapTile` values; each tile stores five value-type `(sheet, graphic)` layers inline, avoiding six heap objects per tile in a 1000×1000 map. Mutations go through coordinate-checked `SetFlags` and `SetLayer` methods so later edit commands have a narrow state-change interface without being implemented in this part. `MapCodec` validates the complete input before allocating the tile array, preserves every signed integer field, and emits the same 12-byte header plus 34 bytes per tile. `MapFileStore` keeps paths and content revisions outside the model, writes an adjacent temporary file, flushes and closes it, rechecks an expected SHA-256 content revision, and requests one same-filesystem local replacement without a copy/delete fallback. The Godot client references the core project and routes runtime tile packets through a small engine-neutral helper before redraw callbacks.

**Series:** Part 1 of 4.
- **Part 1 (this plan):** project setup, shared model, codec, new-map defaults/limits, guarded local replacement, external-change detection, tests, and Godot migration.
- **Later parts:** editing commands/undo, rendering/assets, and Avalonia application/builds. They are out of scope here except for the model mutation and file-storage interfaces they will consume.

**Tech Stack:** C# / .NET 8 for `MapEditor.Core` and `MapEditor.Core.Tests`; xUnit 2.9.2; existing Godot client on `Godot.NET.Sdk/4.7.2` and `net8.0`; existing client tests on `net10.0` with GodotSharp 4.6.2.

**Baseline verified 2026-09-01:** `dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj -v minimal` passes 457 tests; `dotnet build Goose2ClientGodot.csproj -v minimal` succeeds with zero warnings/errors. The installed `godot` is 4.7.1 while the project SDK is 4.7.2, and an editor scan rewrites the SDK line and reports unavailable generated assets in this worktree, so `godot --editor` is not an automated gate for this part. Use the .NET build plus tests and revert/avoid any engine-generated project edit.

**Design source:** `docs/plans/2026-09-01-portable-map-editor-design.md`.

---

## Scope boundaries

In scope:

- `MapEditor.Core` and `MapEditor.Core.Tests` project setup.
- Engine-independent map document, tile, and five-layer value types.
- New-map constants and creation for dimensions 1 through 1000.
- Strict decode, model validation, and byte-for-byte-compatible encode.
- Typed format, validation, and external-change failures.
- Adjacent temporary save, explicit `Flush(true)`, same-filesystem local replacement, cleanup, and SHA-256 revision checks.
- Godot project/test references and migration of all current `MapFile` consumers.
- Automated tests and a Godot build regression gate.

Out of scope:

- Paint/erase/blocked-toggle commands, drag interpolation, undo/redo, dirty-state ownership, and discard prompts.
- Rendering math, manifests, image loading, sprite caching changes, placeholders, or canvas behavior.
- Avalonia projects, controls, dialogs, settings, app document/view-model state, publishing, or build scripts.
- Resizing an existing map.
- Asset-converter migration. Its direct binary writer remains unchanged; its current output independently confirms the same field order at `tools/AssetConverter/src/AssetConverter/Aspereta/AsperetaMapConverter.cs:27-59` and exact consumption at `tools/AssetConverter/tests/AssetConverter.Tests/AsperetaMapConverterTests.cs:25-47`.
- Merging concurrent changes, file watchers, autosave, or recovery files.

The core does **not** own a mutable “active document,” path, dirty flag, or undo stack. `MapFileStore.Open` returns a complete result; a later app layer must swap active state only after that call succeeds. `MapFileStore.Save` returns a new revision only after replacement succeeds; a later app layer must clear dirty state and update path/revision only after receiving that return value.

---

## APIs and facts verified in the current repository

| API / fact | Citation and consequence |
|---|---|
| Approved architecture requires the Godot client to reference `MapEditor.Core`, with no Godot dependency in the editor | `docs/plans/2026-09-01-portable-map-editor-design.md:13-23` |
| Required model fields and row-major five-layer shape | `docs/plans/2026-09-01-portable-map-editor-design.md:25-34` |
| Wire layout is `Int16, Int16, Int32, Int32`, then `Int32 Flags` and five `Int32 Graphic, Int16 Sheet` pairs | `docs/plans/2026-09-01-portable-map-editor-design.md:36-42`; current reader at `Scripts/MapFile.cs:61-86` |
| Header is 12 bytes and each tile is 34 bytes; fixture is exactly 3412 bytes for 10×10 | Existing assertion at `tests/Goose2Client.Tests/MapFileTests.cs:25-26`; verified fixture size; current loops at `Scripts/MapFile.cs:70-86` |
| Existing fixture has `Version = 146` and `EditorVersion = 10` | `tests/Goose2Client.Tests/MapFileTests.cs:18-21`. Therefore `Version` is data and must not be treated as the format discriminator; only editor/layout version 10 is accepted. |
| Existing indexer is `(x,y) -> y * Width + x` | `Scripts/MapFile.cs:92-95`; fixture assertion at `tests/Goose2Client.Tests/MapFileTests.cs:28-33` |
| Blocked is bit `2`; roof derives from layer 4 having a nonzero graphic | `Scripts/MapFile.cs:26-33`; current tests at `tests/Goose2Client.Tests/MapFileTests.cs:41-51` |
| Current model allocates one `MapTile` class plus five `Layer` classes per tile | `Scripts/MapFile.cs:6-24`. Replacing these with inline structs addresses the approved direct-storage constraint at `docs/plans/2026-09-01-portable-map-editor-design.md:48-54`. |
| Only four production files consume `MapFile`: `GameManager`, `MapManager`, `Map/MapLayer`, and `Map/ObjectLayer` | Types/calls at `Scripts/GameManager.cs:29-30,351-363`, `Scripts/MapManager.cs:12,42`, `Scripts/Map/MapLayer.cs:11-17`, `Scripts/Map/ObjectLayer.cs:15-23`; repository grep found no others. |
| Runtime tile packets replace flags and may replace each layer; sheet 0 clears the graphic | `Scripts/MapManager.cs:322-340`. This is the state mutation most likely to regress when class tiles become value types, so migration must route it through an engine-neutral helper tested in the client test project. |
| Rendering reads dimensions and `(sheet, graphic)` but does not need mutable layer objects | `Scripts/Map/MapLayer.cs:27-43`; `Scripts/Map/ObjectLayer.cs:25-40` |
| Movement and roof behavior read `IsBlocked` and `IsRoof` from the current tile | `Scripts/MapManager.cs:129-137,272-277` |
| Godot loads bytes through `Godot.FileAccess`, not a normal filesystem path | `Scripts/GameManager.cs:351-363`. Keep this path and pass its buffer to `MapCodec.Decode`; do not route `res://` through `MapFileStore`. |
| The Godot project is `net8.0`, recursively includes source by SDK default, and currently excludes only tools, `.godot`, and tests | `Goose2ClientGodot.csproj:1-13`. Adding top-level `src/` requires `<Compile Remove="src/**" />` in Task 1 as soon as that directory exists. Only the `ProjectReference` waits for client migration. |
| Client tests compile all `Scripts/**/*.cs` and target net10.0 | `tests/Goose2Client.Tests/Goose2Client.Tests.csproj:1-17`. They need a project reference after `Scripts/MapFile.cs` is removed. A net10 test project can reference net8 core. |
| The solution currently contains only the Godot project | `Goose2ClientGodot.sln:1-18`. Add core, core tests, and the existing client tests so one solution gate covers all projects. |
| New maps default to 100×100, allow 1…1000, and use versions 1/10 with empty layers and zero flags | `docs/plans/2026-09-01-portable-map-editor-design.md:48-54` |
| Save must be temporary-adjacent, flushed/closed, replaced in one same-filesystem local operation where supported, and guarded against source changes | `docs/plans/2026-09-01-portable-map-editor-design.md:42-46`. This is process-level partial-file protection on ordinary supporting local filesystems, not universal atomicity or crash durability. |
| Failed open/save state guarantees belong at the later active-document boundary | `docs/plans/2026-09-01-portable-map-editor-design.md:98-104` |
| Repository policy is no new comments/doc strings unless the “why” is non-obvious | `AGENTS.md:3-27`. New code in this plan should have no comments/doc strings; retain unrelated existing comments while migrating callers. |

.NET 8 API verification was performed with a standalone `net8.0` compile probe against the installed SDK. The probe compiled `BinaryPrimitives.ReadInt16LittleEndian`, `BinaryPrimitives.ReadInt32LittleEndian`, `SHA256.HashData(ReadOnlySpan<byte>)`, the `FileStream(..., FileOptions.WriteThrough)` constructor, `FileStream.Flush(bool)`, and `File.Move(string, string, bool)`. `InvalidDataException` is sealed on the target framework, so custom exceptions below derive directly from `IOException` or `Exception`. These are the exact framework APIs used below; do not substitute guessed `File.Replace` overloads, directory-fsync claims, or platform-specific P/Invoke.

---

## Locked core contracts

Use namespace `MapEditor.Core` throughout the new project.

### Model surface

```csharp
public readonly record struct MapTileLayer(int Sheet, int Graphic);

public readonly struct MapTile
{
    public int Flags { get; }
    public bool IsBlocked { get; }
    public bool IsRoof { get; }
    public MapTileLayer GetLayer(int layerIndex);
}

public sealed class MapDocument
{
    public const int LayerCount = 5;
    public const int BlockedFlag = 2;
    public const int DefaultWidth = 100;
    public const int DefaultHeight = 100;
    public const int MinDimension = 1;
    public const int MaxDimension = 1000;
    public const short NewMapVersion = 1;
    public const short SupportedEditorVersion = 10;

    public short Version { get; }
    public short EditorVersion { get; }
    public int Width { get; }
    public int Height { get; }
    public int TileCount { get; }
    public MapTile this[int x, int y] { get; }
    public MapTile GetTile(int rowMajorIndex);

    public static MapDocument Create(int width = DefaultWidth, int height = DefaultHeight);
    public void SetFlags(int x, int y, int flags);
    public void SetLayer(int x, int y, int layerIndex, MapTileLayer layer);
}
```

Implementation constraints:

- `MapDocument` owns exactly one `MapTile[]`; `MapTile` owns five inline `MapTileLayer` fields selected by a switch. Do not allocate `Layer[]` inside every tile and do not expose the backing tile array.
- `MapTile`, `MapTileLayer`, and returned tile/layer values are value types. `SetFlags` and `SetLayer` replace the corresponding tile value in the backing array.
- Width, height, version, and editor version are immutable after creation/decode. Existing-map resize remains deferred.
- `Sheet` remains an `int` in memory for compatibility with server packet values. Encoding validates it is in the signed `Int16` range; do not silently truncate, clamp, normalize negative values, or force `(Graphic == 0) => (Sheet == 0)`.
- `Graphic` and `Flags` preserve all signed `Int32` values. Unknown flag bits are not interpreted or cleared.
- Coordinate, row-major index, and layer-index failures throw `ArgumentOutOfRangeException` before mutation.
- An internal decoded-document factory/constructor may accept the already validated tile array. It must verify the array length matches checked `width * height`; it is not public API.

This is the only forward interface intentionally exposed to later parts: read a tile/layer by value and mutate one flag field or one layer. No command, event, selection, observer, or undo API is introduced now.

### Codec surface

```csharp
public enum MapFormatError
{
    TruncatedHeader,
    UnsupportedEditorVersion,
    InvalidDimensions,
    OversizedDimensions,
    LengthMismatch
}

public sealed class MapFormatException : IOException
{
    public MapFormatError Error { get; }
}

public enum MapValidationError
{
    SheetOutOfRange
}

public sealed class MapValidationException : Exception
{
    public MapValidationError Error { get; }
}

public static class MapCodec
{
    public const int HeaderSize = 12;
    public const int BytesPerTile = 34;

    public static MapDocument Decode(ReadOnlySpan<byte> bytes);
    public static byte[] Encode(MapDocument document);
}
```

Validation/order contract:

1. Require all 12 header bytes.
2. Decode explicitly little-endian.
3. Require `EditorVersion == 10`; accept every `Int16 Version`, including fixture version 146.
4. Reject width/height below 1 as `InvalidDimensions`; reject either above 1000 as `OversizedDimensions`.
5. Compute tile count and total length with checked 64-bit arithmetic, then require exact input length before allocating tiles. A short body and trailing data are both `LengthMismatch`, with expected/actual values included in the exception message.
6. Decode all fields row-major without semantic normalization.
7. `Encode` validates the document and every sheet before allocating/writing output, writes explicit little-endian fields, and produces exactly `12 + 34 * TileCount` bytes. An invalid sheet throws `MapValidationException` with `Error == MapValidationError.SheetOutOfRange`.

The 1000 load limit deliberately matches the creation limit. It bounds allocation and gives “oversized input” a deterministic meaning. Do not accept a 1×1001 legacy file while rejecting creation of the same shape.

### File-storage surface

```csharp
public readonly record struct MapFileRevision(string ContentHash);
public sealed record OpenedMap(MapDocument Document, MapFileRevision Revision);

public sealed class MapExternalChangeException : IOException
{
    public string Path { get; }
    public MapFileRevision ExpectedRevision { get; }
    public MapFileRevision? ActualRevision { get; }
}

public sealed class MapFileStore
{
    public OpenedMap Open(string path);
    public MapFileRevision Save(
        string path,
        MapDocument document,
        MapFileRevision? expectedRevision = null);
}
```

Storage contract:

- Normalize to `Path.GetFullPath`; path is never stored in `MapDocument`.
- A revision is uppercase SHA-256 of the exact encoded/opened bytes, not mtime. Same-content replacement is not a conflict; changed content is a conflict even if timestamp and length are restored.
- `Open` reads bytes, decodes into a local result, hashes those same bytes, and returns only after both succeed.
- `Save` encodes/validates before touching the destination directory, creates a unique hidden-ish temp name beside the destination, writes with `FileOptions.WriteThrough`, calls `Flush(flushToDisk: true)`, and closes the stream before any replacement attempt.
- If `expectedRevision` is supplied, hash the current destination after the temp closes. Missing destination or differing bytes throws `MapExternalChangeException` and leaves the current destination untouched.
- Replace with `File.Move(temp, destination, overwrite: true)`. The adjacent temp keeps this on one filesystem and requests one process-safe local rename/replace operation on ordinary filesystems that support those semantics. Do not add copy/delete or delete-then-move fallbacks; unsupported replacement must fail rather than deliberately create a partial-file window.
- Always attempt temp cleanup in `finally`. Never remove or rewrite the destination as cleanup.
- Return the hash of the bytes just saved only after replacement succeeds.
- An internal constructor takes a replacement delegate for deterministic fault tests. Add `src/MapEditor.Core/Properties/AssemblyInfo.cs` with `InternalsVisibleTo("MapEditor.Core.Tests")`; keep the public API filesystem-implementation-free.

This contract does not promise universal atomicity or crash durability. `WriteThrough` and `Flush(true)` request file-data persistence before replacement, but .NET has no portable directory-fsync step here; a power loss can still lose replacement metadata. Remote, virtual, network, or unusual filesystems may provide weaker or unsupported rename/overwrite behavior. Tests prove adjacent placement, a closed complete temp, one replacement call, no fallback, and process-observable ordering; they do not simulate power loss or claim directory durability.

The expected-revision check is advisory concurrency detection, not locking: another process can still race after the final hash and before replacement, and path components can change between normalization/check and use. These residual TOCTOU and filesystem limitations match the approved “warn, do not merge” design and are called out rather than hidden.

---

## Mutation impact analysis

| State change | Existing writers/readers | New mutation path | Required non-impact proof |
|---|---|---|---|
| New document allocation | New feature | `MapDocument.Create` fills one value array with default-zero tiles | Every tile has five readable empty layers, flags zero, 1/10 versions; invalid dimensions allocate nothing |
| Tile flags | Runtime packet currently assigns `tile.Flags` at `Scripts/MapManager.cs:327-328`; movement reads blocked at `:132-134` | `MapDocument.SetFlags(x,y,p.Flags)` | Complete integer replacement persists; layers and neighboring tiles remain unchanged; blocked bit behavior remains identical |
| One tile layer | Runtime packet currently mutates a referenced `Layer` at `Scripts/MapManager.cs:330-338`; renderers read it at `Scripts/Map/MapLayer.cs:35-43` and `Scripts/Map/ObjectLayer.cs:31-40` | Compare `GetLayer`, then `SetLayer` once when changed | Only selected layer changes; flags/other layers/neighbors survive; sheet 0 still produces `(0,0)` in the runtime handler; only changed cells refresh |
| Decode | Current constructor mutates `this` as reads proceed at `Scripts/MapFile.cs:59-89` | Validate complete span, build local value array, construct once | Truncation/trailer/incompatibility returns no partial document; row-major coordinates and all signed fields match fixture |
| Encode | New | Read immutable header/dimensions and all tile values | Input document is unchanged; unknown flags and unusual signed sheet/graphic values round-trip |
| Opened file revision | New | `MapFileStore.Open` returns `OpenedMap` | No global active state changes on format or I/O failure |
| Destination file | New | Adjacent temp write/flush/close, revision check, one local replace call | Failures before the replace call preserve destination; injected pre-move failure cleans temp; no copy/delete fallback or crash-durability claim |
| Godot active map | `ChangeMap` currently assigns `CurrentMap = LoadMap(...)` at `Scripts/GameManager.cs:218` | Decode into `nextMap`; assign `CurrentMap` only after success | Invalid map leaves old `CurrentMap` and old attached world intact; valid map follows the existing scene transition |

The value-type migration has one specific trap: `var tile = document[x,y]` is a copy. Production code must never mutate that copy. The model deliberately has no mutable tile/layer setters, and `MapManager.OnTileUpdate` must call document mutation methods. Tests pin persistence through a fresh indexer read.

---

## Task 1: Scaffold projects and implement the compact shared model/new-map rules

**Files:**
- Create: `src/MapEditor.Core/MapEditor.Core.csproj`
- Create: `src/MapEditor.Core/MapDocument.cs`
- Create: `tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj`
- Create: `tests/MapEditor.Core.Tests/MapDocumentTests.cs`
- Modify: `Goose2ClientGodot.csproj`
- Modify: `Goose2ClientGodot.sln`

**Step 1: Add project scaffolding.**

`src/MapEditor.Core/MapEditor.Core.csproj` targets `net8.0`, enables nullable, and has no package references. `tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj` targets `net8.0`, sets `IsPackable=false`, references the core project, and uses the same test package versions as `tests/Goose2Client.Tests/Goose2Client.Tests.csproj:8-11`.

Immediately add `<Compile Remove="src/**" />` beside the existing recursive excludes in `Goose2ClientGodot.csproj`. The SDK otherwise compiles the new core files into the Godot assembly during every Task 1–3 gate. Do not add the Godot-to-core `ProjectReference` yet; that alone is deferred to Task 4.

Add these two projects and the existing client test project to the solution:

```bash
dotnet sln Goose2ClientGodot.sln add src/MapEditor.Core/MapEditor.Core.csproj
dotnet sln Goose2ClientGodot.sln add tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj
dotnet sln Goose2ClientGodot.sln add tests/Goose2Client.Tests/Goose2Client.Tests.csproj
```

Do not add `MapEditor.Rendering` or `MapEditor.App` placeholders.

**Step 2: Write failing model tests.**

Add these tests:

- `Create_Uses100x100DefaultsAndVersions1And10`.
- `Create_InitializesEveryTileWithZeroFlagsAndFiveEmptyLayers`: inspect first, middle, and last tiles plus `TileCount`; loop layer indices 0…4.
- `Create_AcceptsMinimumAndMaximumDimensions`: create 1×1 and 1000×1000; assert checked tile counts and last coordinate access.
- `Create_RejectsEachDimensionOutside1Through1000`: width/height values 0, -1, 1001, and `int.MaxValue` each throw before allocation.
- `Tiles_AreCompactInlineValueStorage`: assert both types are value types; assert `RuntimeHelpers.IsReferenceOrContainsReferences<MapTile>()` and `RuntimeHelpers.IsReferenceOrContainsReferences<MapTileLayer>()` are false; inspect instance fields to require exactly five `MapTileLayer` fields plus the flags integer and no reference/array fields in `MapTile`; inspect `MapDocument` instance fields to require exactly one array field and that it is `MapTile[]`. This field-shape proof covers the 1,000,000-tile case without a brittle GC allocation benchmark.
- `Coordinates_AreRowMajor`: set distinct flags at `(0,0)`, `(1,0)`, `(0,1)`, `(1,1)` and assert `GetTile(0..3)` order.
- `SetFlags_ChangesOnlyFlagsAtTarget`: seed all five layers, replace flags with a negative value containing unknown bits, and verify layers/neighbors unchanged.
- `SetLayer_ChangesOnlySelectedLayerAtTarget`: seed flags and all layers, replace layer 3, and verify flags/other layers/neighbors unchanged.
- `SetFlagsAndSetLayer_PersistAcrossFreshValueReads`: catches an implementation that mutates only a struct copy.
- `MapTile_DerivesBlockedAndRoof`: blocked bit 2, unknown bits without 2, and layer-4 graphic zero/nonzero.
- `CoordinatesRowMajorIndexesAndLayers_RejectOutOfRangeWithoutMutation`: x/y, linear index, and layer `-1/5` cases.

**Step 3 (red):**

```bash
dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj --filter FullyQualifiedName~MapDocumentTests -v minimal
```

Expected: compile failure because `MapDocument`, `MapTile`, and `MapTileLayer` do not exist.

**Step 4: Implement the locked model surface.**

Use one checked allocation in `Create`; zero-initialized structs already produce all required new-map defaults. Implement five inline layer fields and switch-based get/replace helpers. Keep the decoded constructor internal for Task 2. Add no comments/doc strings.

**Step 5 (green):**

```bash
dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj --filter FullyQualifiedName~MapDocumentTests -v minimal
```

Expected: all model tests pass.

**Step 6: Run the regression gate and commit.**

```bash
dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj -v minimal
dotnet build Goose2ClientGodot.sln -v minimal
git diff --check
git add src/MapEditor.Core tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj tests/MapEditor.Core.Tests/MapDocumentTests.cs Goose2ClientGodot.csproj Goose2ClientGodot.sln
git commit -m "feat(map-editor): add shared map model"
```

The first command remains at 457 tests; the solution build includes the new green core tests and unchanged client.

**Task 1 invariant matrix:**

| Invariant | Test |
|---|---|
| New map defaults are 100×100 and versions 1/10 | `Create_Uses100x100DefaultsAndVersions1And10` |
| Every new tile has flags 0 and five `(0,0)` layers | `Create_InitializesEveryTileWithZeroFlagsAndFiveEmptyLayers` |
| Dimensions are exactly 1…1000 | `Create_AcceptsMinimumAndMaximumDimensions`, `Create_RejectsEachDimensionOutside1Through1000` |
| 1000×1000 storage is one bounded reference-free value array, not per-tile layer objects | `Tiles_AreCompactInlineValueStorage` runtime reference check and field inspection |
| Tile order is `y * Width + x` | `Coordinates_AreRowMajor` |
| Flag/layer writes are narrow and persist despite value semantics | `SetFlags_*`, `SetLayer_*`, `SetFlagsAndSetLayer_PersistAcrossFreshValueReads` |
| Blocked/roof compatibility remains | `MapTile_DerivesBlockedAndRoof` |

---

## Task 2: Add strict little-endian decode, validation, and encode

**Files:**
- Create: `src/MapEditor.Core/MapCodec.cs`
- Create: `src/MapEditor.Core/MapFormatException.cs`
- Create: `src/MapEditor.Core/MapValidationException.cs`
- Create: `tests/MapEditor.Core.Tests/MapCodecTests.cs`
- Copy fixture: `tests/Goose2Client.Tests/Fixtures/Map10x10.bytes` → `tests/MapEditor.Core.Tests/Fixtures/Map10x10.bytes`
- Modify: `tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj`

Keep the old fixture in place until Task 4 removes the old client test.

**Step 1: Write failing happy-path and preservation tests.**

Configure `Fixtures/**` to copy to test output. Add:

- `Decode_ExistingFixturePreservesHeaderGridAndBlockedCount`: migrate the current facts at `tests/Goose2Client.Tests/MapFileTests.cs:13-38`, using `GetLayer` and a row-major loop.
- `Decode_UsesLittleEndianAndWireFieldOrder`: use a literal one-tile byte sequence with asymmetric hex values for version, editor version 10, dimensions, flags, and all five graphic/sheet pairs. Do not construct this test input with `BinaryWriter`, which would merely repeat the implementation assumption.
- `Decode_RowMajorCoordinates`: a literal/synthetic 2×2 payload with distinct flags at each serialized tile.
- `Encode_ExistingFixtureIsByteForByteIdentical`: decode the real fixture, encode, and compare the complete byte arrays. This proves every fixture field and exact length, not only selected assertions.
- `EncodeDecode_PreservesSignedExtremesAndUnknownFlags`: 2×1 document containing `int.MinValue/int.MaxValue` flags and graphics, sheet `short.MinValue/short.MaxValue`, empty graphic with nonzero sheet, and unknown flag bits.
- `Encode_LengthIsHeaderPlus34BytesPerTile`.

**Step 2: Write failing rejection tests.**

- `Decode_RejectsEveryTruncatedHeaderLength`: byte lengths 0 through 11 yield `TruncatedHeader`.
- `Decode_RejectsZeroAndNegativeDimensions`: patch each width/height independently and assert `InvalidDimensions`.
- `Decode_RejectsDimensionsAbove1000BeforeAllocation`: 1001 and `int.MaxValue` in either dimension yield `OversizedDimensions`.
- `Decode_MaximumDimensionsComputesCheckedExpectedLength`: header advertises 1000×1000 with no body; assert `LengthMismatch` reports expected 34,000,012 rather than overflowing or allocating.
- `Decode_RejectsEverySingleByteTruncationBoundary`: valid one-tile bytes truncated at header+flags, within each layer pair, and final byte all yield `LengthMismatch`.
- `Decode_RejectsTrailingByte`: exact map plus one byte yields `LengthMismatch`.
- `Decode_RejectsUnsupportedEditorLayoutWithoutRewriting`: editor versions 9 and 11 yield `UnsupportedEditorVersion`; version 146 with editor version 10 remains accepted.
- `Encode_RejectsSheetOutsideInt16WithoutWriting`: set sheet to 32768 and -32769 and assert `MapValidationException` with `Error == MapValidationError.SheetOutOfRange`.
- `DecodeFailure_DoesNotProducePartialDocument`: express this by the API returning only or throwing; no `TryDecode` with an out document is added.

**Step 3 (red):**

```bash
dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj --filter FullyQualifiedName~MapCodecTests -v minimal
```

Expected: compile failure because `MapCodec` and its exception types do not exist.

**Step 4: Implement validation and codec.**

Use `System.Buffers.Binary.BinaryPrimitives` against spans and advance an integer offset. Parse the header into locals; run all header, compatibility, dimension, checked-count, and exact-length checks before creating the decoded array. Iterate tile index 0…`tileCount-1`, then layer 0…4. Build each tile as a value and pass the completed array to the internal `MapDocument` constructor.

For encode, first walk every tile/layer and validate each sheet. Only then allocate the exact output array and write fields using `WriteInt16LittleEndian`/`WriteInt32LittleEndian`. Do not use host-endian `BitConverter`, reinterpret casts, normalization, or the old `BinaryReader`/`BinaryWriter` implementation.

`MapFormatException : IOException` and `MapValidationException : Exception` are direct bases because `InvalidDataException` is sealed. Their messages include the operation and relevant value, but tests assert the typed `MapFormatError`/`MapValidationError` values rather than full localized text.

**Step 5 (green):**

```bash
dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj --filter FullyQualifiedName~MapCodecTests -v minimal
dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj -v minimal
dotnet build Goose2ClientGodot.sln -v minimal
```

Expected: all codec/model tests pass, and the solution build remains green because the Godot project has excluded `src/**` since Task 1.

**Step 6: Commit.**

```bash
git diff --check
git add src/MapEditor.Core tests/MapEditor.Core.Tests
git commit -m "feat(map-editor): add validated legacy map codec"
```

**Task 2 invariant matrix:**

| Invariant | Test |
|---|---|
| Existing real map data decodes with row-major coordinates | `Decode_ExistingFixturePreservesHeaderGridAndBlockedCount`, `Decode_RowMajorCoordinates` |
| Wire is explicitly little-endian and field order is exact | `Decode_UsesLittleEndianAndWireFieldOrder` |
| Decode→encode preserves every byte of a compatible fixture | `Encode_ExistingFixtureIsByteForByteIdentical` |
| Unknown flags and all signed values are retained | `EncodeDecode_PreservesSignedExtremesAndUnknownFlags` |
| Header/body/trailer must be exact before allocation | `Decode_RejectsEveryTruncatedHeaderLength`, `Decode_RejectsEverySingleByteTruncationBoundary`, `Decode_RejectsTrailingByte` |
| Invalid/oversized dimensions cannot overflow or allocate | dimension tests and `Decode_MaximumDimensionsComputesCheckedExpectedLength` |
| Version is preserved data; editor version 10 is the compatible layout | `Decode_RejectsUnsupportedEditorLayoutWithoutRewriting` plus fixture version 146 |
| Invalid in-memory sheets are rejected with a typed validation error, never truncated | `Encode_RejectsSheetOutsideInt16WithoutWriting` |

---

## Task 3: Add guarded same-filesystem local storage and content-based external-change detection

**Files:**
- Create: `src/MapEditor.Core/MapFileRevision.cs`
- Create: `src/MapEditor.Core/OpenedMap.cs`
- Create: `src/MapEditor.Core/MapExternalChangeException.cs`
- Create: `src/MapEditor.Core/MapFileStore.cs`
- Create: `src/MapEditor.Core/Properties/AssemblyInfo.cs`
- Create: `tests/MapEditor.Core.Tests/MapFileStoreTests.cs`

**Step 1: Write failing open/save tests using a unique temporary directory per test.**

Add:

- `Open_ReturnsDecodedDocumentAndRevisionOfExactBytes`.
- `Open_InvalidFileThrowsWithoutReturningState`.
- `Save_NewPathWritesDecodableExactBytesAndReturnsMatchingRevision`.
- `Save_ExistingPathUsesSingleLocalReplacementAndLeavesNoTemp`.
- `Save_ReplacementReceivesAdjacentTempAndNormalizedDestination`: injected delegate asserts both paths have the same directory, receives one call, and then moves the temp.
- `Save_ReturnedRevisionAllowsNextGuardedSave`.
- `Save_ValidationFailureLeavesDestinationUntouchedAndCreatesNoTemp`.
- `Save_InjectedPreMoveFailureLeavesDestinationUntouchedAndCleansTemp`: inject an internal replacement delegate that opens/asserts the completed temp payload and then throws `IOException` before moving it; verify old bytes remain.
- `Save_ReplaceRunsAfterTempStreamIsClosed`: injected delegate reopens the temp with exclusive access before moving it.

Do not add a directory-fsync test or a crash/power-loss durability assertion; those guarantees are not portable in this implementation.

Use a helper that enumerates the temp directory before and after and rejects names matching the store’s temp pattern. Cleanup the whole test directory in `finally`.

**Step 2: Write failing conflict tests.**

- `Save_ExpectedRevisionDetectsChangedContentEvenWhenLengthAndTimestampMatch`: open, externally rewrite same-length bytes, restore last-write timestamp, guarded save throws, external bytes remain.
- `Save_ExpectedRevisionTreatsDeletedDestinationAsExternalChange`.
- `Save_ExpectedRevisionAllowsSameContentReplacement`: externally replace the file with byte-identical content; guarded save succeeds because revisions are content-based.
- `Save_WithoutExpectedRevisionImplementsSaveAsOverwrite`: overwrite an existing chosen destination when caller passes null. The later UI is responsible for Save As confirmation.
- `ExternalChangeFailure_CleansTempAndDoesNotReturnNewRevision`.

**Step 3 (red):**

```bash
dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj --filter FullyQualifiedName~MapFileStoreTests -v minimal
```

Expected: compile failure because the storage/revision types do not exist.

**Step 4: Implement `MapFileStore`.**

Encode first. Derive the full destination and adjacent unique temp. Open the temp with `FileMode.CreateNew`, `FileAccess.Write`, `FileShare.None`, a normal buffer, and `FileOptions.WriteThrough`; write all bytes, call `Flush(true)`, then leave the `using` before revision comparison/replacement. This explicit flush is retained as a best available file-data persistence request, not presented as directory-metadata or crash durability.

Hash with `Convert.ToHexString(SHA256.HashData(bytes))`. For an expected revision, read/hash the current destination after temp close. If it is absent or differs, throw `MapExternalChangeException` carrying expected and nullable actual revisions. Invoke the injected/default replace delegate once. In `finally`, delete only the temp if it still exists. If cleanup also fails while another exception is active, preserve the primary failure rather than masking it; otherwise surface cleanup failure.

Do not catch/translate `MapFormatException`, `MapValidationException`, unauthorized access, missing directory, disk-full, or replacement errors into success values. Typed exceptions let the later app distinguish map format, external change, and filesystem errors.

**Step 5 (green):**

```bash
dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj --filter FullyQualifiedName~MapFileStoreTests -v minimal
dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj -v minimal
dotnet build Goose2ClientGodot.sln -v minimal
```

Expected: all storage/core tests pass, no temp files remain, and the solution build stays green without compiling core source into the Godot assembly.

**Step 6: Commit.**

```bash
git diff --check
git add src/MapEditor.Core tests/MapEditor.Core.Tests/MapFileStoreTests.cs
git commit -m "feat(map-editor): add guarded local map storage"
```

**Task 3 invariant matrix:**

| Invariant | Test |
|---|---|
| Open revision hashes exactly the bytes decoded | `Open_ReturnsDecodedDocumentAndRevisionOfExactBytes` |
| Save output is codec-valid and revision matches disk | `Save_NewPathWritesDecodableExactBytesAndReturnsMatchingRevision` |
| Replacement sees a complete, closed adjacent temp file exactly once | `Save_ReplaceRunsAfterTempStreamIsClosed`, `Save_ReplacementReceivesAdjacentTempAndNormalizedDestination`, injected check in `Save_InjectedPreMoveFailure...` |
| Failures before replacement preserve the prior destination | validation, external-change, and injected pre-move failure tests |
| Temp files never leak on success/conflict/tested pre-move failure | temp-directory assertions in all save tests |
| Content changes cannot hide behind equal size/coarse timestamps | `Save_ExpectedRevisionDetectsChangedContentEvenWhenLengthAndTimestampMatch` |
| Delete and same-content replacement have intentional conflict semantics | deleted/same-content tests |
| Revision advances only after a successful save | `Save_ReturnedRevisionAllowsNextGuardedSave`, `ExternalChangeFailure_CleansTempAndDoesNotReturnNewRevision` |

---

## Task 4: Migrate the Godot client to the shared model/codec without regressions

**Files:**
- Create: `Scripts/MapTileUpdate.cs`
- Create: `tests/Goose2Client.Tests/MapTileUpdateTests.cs`
- Modify: `Goose2ClientGodot.csproj`
- Modify: `tests/Goose2Client.Tests/Goose2Client.Tests.csproj`
- Modify: `Scripts/GameManager.cs`
- Modify: `Scripts/MapManager.cs`
- Modify: `Scripts/Map/MapLayer.cs`
- Modify: `Scripts/Map/ObjectLayer.cs`
- Delete: `Scripts/MapFile.cs`
- Delete: `tests/Goose2Client.Tests/MapFileTests.cs`
- Delete: `tests/Goose2Client.Tests/Fixtures/Map10x10.bytes`

The deleted codec/model test coverage already moved and expanded in Task 2. Do not leave duplicate `Goose2Client.MapFile`, `Layer`, or `MapTile` compatibility wrappers; the approved design requires the client to use the shared core directly. `Scripts/MapTileUpdate.cs` is production client code but contains no Godot or packet types, and the client test project already source-compiles `Scripts/**/*.cs`, so its internal helper is directly testable without widening public API.

**Step 1: Wire only the deferred project references and write failing packet-mutation tests.**

`Goose2ClientGodot.csproj` already has `<Compile Remove="src/**" />` from Task 1. Add only `<ProjectReference Include="src/MapEditor.Core/MapEditor.Core.csproj" />`. In `tests/Goose2Client.Tests/Goose2Client.Tests.csproj`, add a project reference to `../../src/MapEditor.Core/MapEditor.Core.csproj`.

Keep `Scripts/MapFile.cs` temporarily so the existing client still compiles. Add `tests/Goose2Client.Tests/MapTileUpdateTests.cs` with:

- `Apply_ReplacesFlagsAndExactlyFiveLayers`: seed flags, all five layers, and a neighbor; apply ten packet integers in graphic/sheet order; assert exact replacement flags/layers, unchanged neighbor, and redraw requests for exactly layers 0…4.
- `Apply_SheetZeroClearsGraphic`: pass a nonzero packet graphic with sheet zero and assert stored `(sheet, graphic)` is `(0,0)`.
- `Apply_UnchangedNormalizedLayersRequestNoRedraw`: seed all five normalized replacements, including `(0,0)` for a packet pair with nonzero graphic/sheet zero; assert flags still replace but no redraw callback occurs.
- `Apply_MutatesBeforeEachRedrawCallback`: in each callback, reread the document and assert the new flags and that layer's normalized replacement are already visible.

Run:

```bash
dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj --filter FullyQualifiedName~MapTileUpdateTests -v minimal
```

Expected red: compile failure because the internal production helper does not exist.

**Step 2: Add the engine-neutral production helper and make its tests green.**

Create internal `MapTileUpdate` in `Scripts/MapTileUpdate.cs` with an `Apply` method taking `MapDocument`, coordinates, flags, a `ReadOnlySpan<int>` of exactly ten graphic/sheet values, and an `Action<int>` redraw callback. It must:

1. Validate the span length as exactly `MapDocument.LayerCount * 2` before mutation.
2. Set flags once before any callback.
3. Iterate exactly `MapDocument.LayerCount` layers and normalize each packet pair to `new MapTileLayer(sheet, sheet == 0 ? 0 : graphic)` before equality comparison.
4. Skip both mutation and callback when the normalized replacement equals the stored layer.
5. Call `SetLayer` before invoking the callback with that layer index.

The helper has no Godot, rendering, or protocol dependency. It owns state mutation and changed-layer decisions; `MapManager` will retain bounds checking and renderer selection.

```bash
dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj --filter FullyQualifiedName~MapTileUpdateTests -v minimal
dotnet build Goose2ClientGodot.csproj -v minimal
```

Expected: all four focused client tests pass while the old map model remains temporarily intact, and the Godot project resolves core only through its project reference.

**Step 3: Delete the old model and deliberately make consumer migration red.**

Delete `Scripts/MapFile.cs`, then run:

```bash
dotnet build Goose2ClientGodot.csproj -v minimal
```

Expected red: unresolved `MapFile`/legacy map model usages in the four verified consumers. If core source compiles as part of the Godot assembly or duplicate-type errors appear, the Task 1 `src/**` exclusion was lost and must be restored before migration.

**Step 4: Migrate read-only consumers and route tile updates through the helper.**

Add `using MapEditor.Core;` where needed.

- `Scripts/GameManager.cs`: `CurrentMap` and `LoadMap` become `MapDocument`; decode `f.GetBuffer(...)` with `MapCodec.Decode`.
- At the current assignment site `Scripts/GameManager.cs:218`, use `var nextMap = LoadMap(mapFile); if (nextMap == null) return; CurrentMap = nextMap;`. Catch `MapFormatException` inside `LoadMap`, include the `res://` path and typed `Error` in `GD.PushError`, and return null. Do not replace the old active map on malformed input.
- `Scripts/MapManager.cs`: `_map` becomes `MapDocument`; blocked/roof/dimension reads remain conceptually identical.
- `Scripts/Map/MapLayer.cs` and `Scripts/Map/ObjectLayer.cs`: setup fields/arguments become `MapDocument`; replace `.Layers[_layer]` with `.GetLayer(_layer)`.
- At `Scripts/MapManager.cs:322-340`, keep the existing packet bounds guard, then call `MapTileUpdate.Apply(_map, p.X, p.Y, p.Flags, p.Tiles, callback)`. The callback keeps the existing layer-2 object-renderer choice and flat-renderer path. Do not duplicate flag/layer mutation in `MapManager`.

Do not change coordinate conversion, culling, z order, sprite lookup, anchoring, listeners, scene flow, renderer refresh bodies, or asset paths. The helper tests, rather than source review alone, pin five-layer mutation, normalized sheet-zero clearing, unchanged suppression, and mutation-before-redraw ordering.

**Step 5 (green compile and focused tests):**

```bash
dotnet build Goose2ClientGodot.csproj -v minimal
dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj --filter "FullyQualifiedName~MapDocumentTests|FullyQualifiedName~MapCodecTests" -v minimal
dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj --filter FullyQualifiedName~MapTileUpdateTests -v minimal
dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj -v minimal
```

Expected: game build succeeds with zero warnings/errors; core compatibility tests and all four mutation tests pass. Baseline 457 minus the two deleted `MapFileTests` tests plus four new helper tests gives 459 client tests unless unrelated tests changed earlier in the execution branch. Do not use count alone—require zero failures.

**Step 6: Static integration audit.**

```bash
git grep -nE '\b(MapFile|class Layer|\.Layers\[)' -- '*.cs' ':!docs/**'
git grep -n 'MapEditor.Core' -- '*.csproj' '*.cs'
git grep -nE 'Godot|TileUpdatePacket' -- Scripts/MapTileUpdate.cs || true
git diff --check
```

Expected:

- First command has no legacy model/class/mutable-layer hits. `Goose2Client.Map.MapLayer` is a renderer class and is allowed; the word-boundary search may show it only where named explicitly, not as `MapFile` or `.Layers[...]`.
- Godot and client-test projects reference core; migrated consumers and the helper import it; `Goose2ClientGodot.csproj` still excludes `src/**`.
- The helper is engine/protocol neutral, and `src/MapEditor.Core` contains no `using Godot`, `GodotObject`, `Node`, `Avalonia`, rendering, or asset types.

Also inspect `dotnet list Goose2ClientGodot.csproj reference` and `dotnet list tests/Goose2Client.Tests/Goose2Client.Tests.csproj reference`; both must list `MapEditor.Core.csproj`.

**Step 7: Full gate and optional headed smoke.**

```bash
dotnet test Goose2ClientGodot.sln -v minimal
dotnet build Goose2ClientGodot.sln -v minimal
git status --short
git diff --stat
git diff --check
```

Do not run `godot --editor` in this worktree as a gate: the verified installed 4.7.1 editor rewrites the tracked 4.7.2 SDK line and this checkout lacks ignored generated assets. If a developer environment has generated `Assets/Maps`, sprites, and a reachable server, run the existing headed client without opening the editor and verify: login map renders, blocked movement still blocks, roof visibility toggles, one live tile update refreshes only its affected layers, and a second map transition succeeds. This is supplementary; no assets or launcher are added/committed.

**Step 8: Commit.**

```bash
git add Goose2ClientGodot.csproj tests/Goose2Client.Tests/Goose2Client.Tests.csproj \
  tests/Goose2Client.Tests/MapTileUpdateTests.cs Scripts/MapTileUpdate.cs \
  Scripts/GameManager.cs Scripts/MapManager.cs Scripts/Map/MapLayer.cs Scripts/Map/ObjectLayer.cs
git add -u Scripts/MapFile.cs tests/Goose2Client.Tests/MapFileTests.cs tests/Goose2Client.Tests/Fixtures/Map10x10.bytes
git commit -m "refactor(map): use shared map editor core"
```

**Task 4 invariant matrix:**

| Invariant | Proof |
|---|---|
| Godot consumes the shared assembly, not duplicated source | project-reference audit and no legacy grep hits |
| Existing fixture remains compatible | core fixture decode and byte-identical round trip |
| Runtime blocked/roof reads are unchanged | `MapDocumentTests.MapTile_DerivesBlockedAndRoof` plus successful client build at `MapManager` read sites |
| Runtime packet replaces flags and exactly five layers with value tiles | `MapTileUpdateTests.Apply_ReplacesFlagsAndExactlyFiveLayers` plus core persistence tests |
| Runtime clear semantics remain sheet 0 → graphic 0 | `MapTileUpdateTests.Apply_SheetZeroClearsGraphic` |
| Unchanged normalized layers suppress redraw | `MapTileUpdateTests.Apply_UnchangedNormalizedLayersRequestNoRedraw` |
| Render refresh observes the new flags/layer | `MapTileUpdateTests.Apply_MutatesBeforeEachRedrawCallback`; no renderer logic changes |
| Invalid map cannot replace `CurrentMap` | local `nextMap` assignment order and typed catch review |
| No Godot/Avalonia dependency leaks into core | project file and namespace grep audit |
| Existing client behavior compiles/tests without regressions | full solution tests and Godot project build |

---

## Consolidated invariant-to-test matrix

| ID | Invariant | Automated proof / audit |
|---|---|---|
| C1 | Model is engine-independent and compact for 1,000,000 tiles | `Tiles_AreCompactInlineValueStorage` reference-content assertion and field inspection; core project dependency grep |
| C2 | New map dimensions/defaults are exact | `Create_*` tests |
| C3 | Tile order is row-major and all five layers exist | `Coordinates_AreRowMajor`, new-map layer checks, codec coordinate test |
| C4 | Complete flags, graphics, and sheets are preserved | signed-extremes round trip and fixture byte equality |
| C5 | Blocked/roof derivation remains client-compatible | `MapTile_DerivesBlockedAndRoof` |
| C6 | Mutations affect only requested state | `SetFlags_*`, `SetLayer_*`, out-of-range non-mutation test |
| C7 | Decode is explicitly little-endian and exact-length | literal endian test; truncation/trailer tests |
| C8 | Invalid, oversized, and incompatible inputs allocate no document | header/dimension/version/length error tests and decode ordering review |
| C9 | Version 146 remains valid while incompatible editor layouts are rejected | fixture and unsupported-editor tests |
| C10 | Encode validates before output and never truncates sheet values | invalid-sheet tests |
| C11 | Save writes/flushes/closes an adjacent temp before one local replacement call | adjacent-path/closed-stream injected tests plus source review of `Flush(true)` and `using` boundary; no crash-durability claim |
| C12 | Failures before replacement preserve prior destination and leave no temp | validation/conflict/injected pre-move failure tests |
| C13 | External content changes are detected independently of mtime/length | same-length/restored-timestamp conflict test |
| C14 | Successful save returns the next expected revision only after replace | guarded second-save test and API ordering review |
| C15 | Failed open/save cannot mutate later app active/dirty state implicitly | no global state in core; result/exception-only API audit |
| C16 | Godot runtime references and mutates the shared model correctly before redraw | project-reference/static audits and four `MapTileUpdateTests` covering flags, five layers, clearing, suppression, and callback ordering |

---

## Red-team review

Perform this review after Task 4 and before declaring Part 1 complete. Record any discovered correction in the task’s commit, rerun its red/green gate, and do not add future-part code as a workaround.

1. **Version confusion:** Try to reject fixture `Version = 146`. The test must fail that implementation; only `EditorVersion = 10` gates layout compatibility.
2. **Integer/allocation attack:** Feed dimensions `int.MaxValue`, 1001×1, 1×1001, and a 1000×1000 header with no body. Confirm rejection/expected-length calculation occurs before tile allocation and uses checked arithmetic.
3. **Length ambiguity:** Append bytes that look like a sixth layer or a future trailer. Confirm exact-length rejection rather than silently rewriting an unknown layout.
4. **Normalization/data-loss attack:** Round-trip negative flags, unknown high flag bits, negative sheets, zero graphic with nonzero sheet, and extreme graphics. Confirm no clearing, sign conversion, or canonicalization.
5. **Value-copy/redraw-order attack:** Move `SetLayer` after the callback or mutate a local tile/layer copy. `Apply_MutatesBeforeEachRedrawCallback` and core persistence tests must fail that implementation.
6. **Per-tile allocation attack:** Assert `RuntimeHelpers.IsReferenceOrContainsReferences<MapTile>()` is false and inspect all instance fields. Reject any `Layer[]`, `List<>`, reference-containing tile, class tile, or five heap layer objects per tile.
7. **Partial-open attack:** Truncate at every field boundary. Confirm there is no public partially populated object, mutable static current document, or out parameter.
8. **Validation-after-write attack:** Use an out-of-range sheet while a destination exists. Confirm no temp/destination change because encode validation precedes filesystem work.
9. **External-change bypass:** Change bytes while preserving length and mtime; delete the file; replace with identical bytes. Confirm changed/deleted conflict and identical content succeeds by the documented policy.
10. **Replacement failure:** Inject a failure after temp close but before the delegate moves it. Confirm destination bytes remain exactly prior content and temp cleanup does not mask the primary exception; do not generalize this into a crash guarantee.
11. **Cross-filesystem/corrupt fallback:** Search for `File.Copy`, destination `File.Delete`, or temp-directory APIs in `MapFileStore`. None are allowed; temp must be adjacent and replacement is one same-filesystem local call where supported.
12. **Durability/TOCTOU honesty:** Confirm docs/API do not claim locking, merge safety, universal atomicity, directory fsync, or crash durability. Revision and path checks cannot eliminate a final race, and remote/unusual filesystems can have weaker semantics.
13. **Project double-compile:** Confirm `<Compile Remove="src/**" />` has been present since Task 1, before any project reference. Temporarily removing it demonstrates the SDK recursive-glob hazard; final builds must load core only through the Task 4 `ProjectReference`.
14. **Godot regression:** Inspect every old `MapFile` grep hit against the verified four-consumer list. Confirm no coordinate, rendering, scene lifecycle, or packet listener changes slipped into the migration, and all tile packet mutation goes through the tested neutral helper.
15. **Scope creep:** Reject commands, undo stacks, dirty state, renderer abstractions, manifests, Avalonia references, app projects, file dialogs, watchers, or build/publish scripts in this part.
16. **Repository hygiene:** Confirm no `.godot` changes, generated assets, temporary maps, `bin/obj`, editor SDK rewrite, or copied fixture remains under the old client fixture path.
17. **Exception hierarchy:** Compile against net8.0 and confirm `MapFormatException : IOException`, `MapValidationException : Exception`, and `MapExternalChangeException : IOException`; typed error/path/revision properties remain asserted rather than replacing them with message matching.

Final commands:

```bash
dotnet test Goose2ClientGodot.sln -v minimal
dotnet build Goose2ClientGodot.sln -v minimal
git grep -nE 'Godot|Avalonia' -- src/MapEditor.Core || true
git grep -nE '\bMapFile\b|\.Layers\[' -- '*.cs' ':!docs/**' || true
git diff --check
git status --short
```

Expected final commit sequence:

1. `feat(map-editor): add shared map model`
2. `feat(map-editor): add validated legacy map codec`
3. `feat(map-editor): add guarded local map storage`
4. `refactor(map): use shared map editor core`

Each commit must be independently buildable/testable at its stated gate. Do not squash unrelated work into these commits, do not commit this planning-only request, and do not begin Parts 2–4.
