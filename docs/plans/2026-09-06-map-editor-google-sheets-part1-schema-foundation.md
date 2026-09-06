# Map Editor Google Sheets Integration — Part 1: Schema Foundation Implementation Plan

**Goal:** Establish a verified server-generated worksheet schema and a provider-neutral `MapEditor.GameData` library for mapping, comparing, validating, planning, and editing map-owned sheet rows.

**Architecture:** `/home/agent/workspace/illutiagooseserver` remains the schema authority and emits a checked-in JSON artifact for the client. The client embeds that artifact, resolves fields by descriptor name rather than handwritten indexes, keeps duplicate-preserving immutable snapshots, and exposes pure domain operations with no Google, Godot, Avalonia, or rendering dependency.

**Tech Stack:** C#; server `net10.0`, ClosedXML 0.105.0, xUnit 2.9.3; client .NET 8, `System.Text.Json`, xUnit 2.9.2.

---

> Implement with @executing-plans, one task and commit at a time. There are two repositories; run and commit commands in the repository named by each task.

## Scope

Part 1 includes:

- verified workbook-header metadata for `Maps`, `NPCs`, `NPC Spawns`, and `Warptiles`;
- additive JSON output from server `SchemaGen`;
- the client `MapEditor.GameData` and test projects;
- embedded schema loading, positional header checks, and typed row mapping;
- duplicate-preserving snapshots and multiset equality;
- validation and target-map replacement planning;
- reversible spawn/warp editing with independent dirty state.

Part 1 excludes Google APIs, OAuth, spreadsheet URL parsing, Pull/Push orchestration, UI, rendering, map resize propagation, and NPC sprite composition. Those remain in Parts 2–4.

## APIs and facts verified

### Server: `/home/agent/workspace/illutiagooseserver`

- `SchemaRegistry.Tables` owns worksheet names, table names, and ordered descriptors: `CsvToSql/CsvToSql.Core/Schema/SchemaRegistry.cs:11-55`.
- Worksheet cell `i + 1` is read using descriptor `i`: `CsvToSql/CsvToSql.Core/CsvToSqlBase.cs:22-42`. Descriptor order must not be sorted or filtered before positions are assigned.
- The four converters declare the actual positional columns: `CsvToSql/CsvToSql.Core/MapsCsvToSql.cs:7-28`, `NpcCsvToSql.cs:7-81`, `NpcSpawnsCsvToSql.cs:7-16`, and `WarpTilesCsvToSql.cs:7-18`.
- `Column` currently owns name, kind, SQL type, required/default, key, reference, and enum metadata but no header: `CsvToSql/CsvToSql.Core/Schema/Column.cs:10-56`.
- `SchemaModel.Build()` is the existing registry projection: `tools/SchemaGen/SchemaModel.cs:8-46`.
- `SchemaJs.Render()` uses camel-case JSON, omits nulls, and pins LF endings: `tools/SchemaGen/SchemaJs.cs:8-30`.
- `Program.Main` currently accepts exactly one `schema.js` path and creates its directory before writing: `tools/SchemaGen/Program.cs:3-37`. Preserve that invocation while adding an optional JSON path.
- The checked-in `Goose.IntegrationTests/Fixtures/aspereta-data.xlsx` is described as a real workbook by `Goose.IntegrationTests/CsvToSqlSnapshotTests.cs:31`; it is already copied to integration-test output by `Goose.IntegrationTests/Goose.IntegrationTests.csproj:15-17`.
- ClosedXML workbook loading is already used by `CsvToSqlConverter.ConvertWorkbook(Stream)`: `CsvToSql/CsvToSql.Core/CsvToSqlConverter.cs:21-45`. Cell text is read with `GetValue<string>()`: `CsvToSql/CsvToSql.Core/CsvToSqlBase.cs:35`.
- Existing generator tests and stale-artifact convention are in `tools/Tools.Tests/SchemaModelTests.cs:7-94` and `tools/Tools.Tests/SchemaJsTests.cs:7-87`.

### Client: `/home/agent/workspace/Goose2ClientGodot/.worktrees/map-editor-google-sheets`

- Existing map-editor libraries target `net8.0` with nullable enabled: `src/MapEditor.Core/MapEditor.Core.csproj:1-7`.
- Test package versions and project-reference style are in `tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj:1-21`.
- The solution uses `src` and `tests` solution folders: `Goose2ClientGodot.sln:8-31` and its `NestedProjects` section.
- Existing history behavior clears redo on a new command and moves commands without replay side effects: `src/MapEditor.Core/Editing/MapEditHistory.cs:20-54`.
- Existing dirty state uses monotonically assigned state IDs and a saved-state ID, allowing undo back to clean: `src/MapEditor.Core/Editing/MapEditSession.cs:15-17,84-98,407-446`.

## Header source and migration decision

Do not derive displayed workbook headers from descriptor names. The real workbook uses human-facing text that differs from SQL names, and some sheets contain helper columns after or outside the imported range.

For this migration, copy header text exactly from row 1 of the checked-in real-workbook fixture into the four converters via `Column.Header(string)`. An integration test must compare every consumed descriptor position against that fixture. Compare only positions `1..Columns.Count`; trailing/helper workbook columns are permitted and are not emitted as descriptors. Before deploying against the team workbook, compare its four row-1 ranges with the fixture; if they differ, replace/update the fixture through the existing reviewed fixture workflow first, then update descriptor metadata until the integration test passes. This makes the checked-in real workbook the migration evidence and `SchemaRegistry` descriptors the ongoing generated contract; no guessed or normalized header strings enter the implementation.

No database migration is involved. JSON and header metadata are additive; existing SQL generation and the one-argument `SchemaGen` command must remain behaviorally unchanged.

## Locked public contracts

Keep the public surface limited to these signatures. Internal JSON DTOs, comparer helpers, and command classes are implementation details.

```csharp
public readonly record struct MapReference(int MapId, string MapName, string MapFilename);
public readonly record struct RgbaValue(int R, int G, int B, int A);
public readonly record struct NpcAppearance(
    int NpcId,
    string NpcName,
    int BodyState,
    int BodyId,
    RgbaValue BodyTint,
    int FaceId,
    int HairId,
    RgbaValue HairTint,
    string EquippedItems);
public readonly record struct NpcSpawnRow(int NpcId, int MapId, int MapX, int MapY);
public readonly record struct WarpRow(int MapId, int MapX, int MapY, int WarpId, int WarpX, int WarpY);
public readonly record struct MapDimensions(int Width, int Height);

public sealed class GameDataSchema
{
    public IReadOnlyList<SheetSchema> Sheets { get; }
    public static GameDataSchema LoadEmbedded();
    public static GameDataSchema Load(Stream stream);
    public SheetSchema GetRequiredSheet(string sheetName);
}

public sealed class SheetSchema
{
    public string Sheet { get; }
    public string Table { get; }
    public IReadOnlyList<ColumnSchema> Columns { get; }
    public ColumnSchema GetRequiredColumn(string columnName);
    public int GetColumnIndex(string columnName);
}

public sealed class ColumnSchema
{
    public string Name { get; }
    public string Header { get; }
    public string Kind { get; }
    public string Sql { get; }
    public string? Default { get; }
    public bool Required { get; }
    public bool IsPrimaryKey { get; }
    public string? RefSheet { get; }
}

public readonly record struct HeaderMismatch(int ColumnIndex, string Expected, string? Actual);

public static class SchemaHeaderValidator
{
    public static IReadOnlyList<HeaderMismatch> Validate(
        SheetSchema schema,
        IReadOnlyList<string?> actualHeaders);
}

public sealed class SheetRowMapper
{
    public SheetRowMapper(GameDataSchema schema);
    public MapReference MapMap(IReadOnlyList<string?> cells);
    public NpcAppearance MapNpc(IReadOnlyList<string?> cells);
    public NpcSpawnRow MapSpawn(IReadOnlyList<string?> cells);
    public WarpRow MapWarp(IReadOnlyList<string?> cells);
    public IReadOnlyList<string?> ToCells(NpcSpawnRow row);
    public IReadOnlyList<string?> ToCells(WarpRow row);
}

public sealed class SpawnSnapshot : IEquatable<SpawnSnapshot>
{
    public SpawnSnapshot(IEnumerable<NpcSpawnRow> rows);
    public IReadOnlyList<NpcSpawnRow> Rows { get; }
}

public sealed class WarpSnapshot : IEquatable<WarpSnapshot>
{
    public WarpSnapshot(IEnumerable<WarpRow> rows);
    public IReadOnlyList<WarpRow> Rows { get; }
}

public static class SnapshotComparer
{
    public static bool Equals(IEnumerable<NpcSpawnRow> left, IEnumerable<NpcSpawnRow> right);
    public static bool Equals(IEnumerable<WarpRow> left, IEnumerable<WarpRow> right);
}

public readonly record struct ValidationIssue(string Code, string Message);
public sealed record ValidationResult(IReadOnlyList<ValidationIssue> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

public static class ValidationCodes
{
    public const string SpawnNpcNotFound = "spawn-npc-not-found";
    public const string SpawnOutOfBounds = "spawn-out-of-bounds";
    public const string WarpOutOfBounds = "warp-out-of-bounds";
    public const string WarpDuplicateSource = "warp-duplicate-source";
    public const string WarpDestinationMapNotFound = "warp-dest-map-not-found";
    public const string WarpDestinationNumericRange = "warp-dest-numeric-range";
    public const string WarpDestinationOutOfBounds = "warp-dest-out-of-bounds";
}

public sealed class GameDataValidator
{
    public GameDataValidator(GameDataSchema schema);
    public ValidationResult ValidateSpawns(
        IEnumerable<NpcSpawnRow> rows,
        IReadOnlyDictionary<int, NpcAppearance> npcs,
        MapDimensions currentMapDimensions);
    public ValidationResult ValidateWarps(
        IEnumerable<WarpRow> rows,
        IReadOnlyDictionary<int, MapReference> maps,
        IReadOnlyDictionary<int, MapDimensions> openMapDimensions,
        MapDimensions currentMapDimensions);
}

public readonly record struct RemoteRow<T>(int WorksheetRowNumber, T Value);
public readonly record struct RowDelete(int WorksheetRowNumber);
public readonly record struct RowInsert(IReadOnlyList<string?> CellValues);
public sealed record ReplacementPlan(
    string Sheet,
    IReadOnlyList<RowDelete> Deletes,
    IReadOnlyList<RowInsert> Inserts);

public sealed class ReplacementPlanner
{
    public ReplacementPlanner(SheetRowMapper mapper);
    public ReplacementPlan PlanSpawnReplacement(
        IReadOnlyList<RemoteRow<NpcSpawnRow>> currentRemote,
        IReadOnlyList<NpcSpawnRow> desiredLocal,
        int mapId);
    public ReplacementPlan PlanWarpReplacement(
        IReadOnlyList<RemoteRow<WarpRow>> currentRemote,
        IReadOnlyList<WarpRow> desiredLocal,
        int mapId);
}

public sealed class SheetEditSession
{
    public SheetEditSession(IEnumerable<NpcSpawnRow> spawns, IEnumerable<WarpRow> warps);
    public IReadOnlyList<NpcSpawnRow> Spawns { get; }
    public IReadOnlyList<WarpRow> Warps { get; }
    public bool IsDirty { get; }
    public bool CanUndo { get; }
    public bool CanRedo { get; }
    public void AddSpawn(NpcSpawnRow row);
    public void RemoveSpawnAt(int index);
    public void MoveSpawn(int index, int mapX, int mapY);
    public void AddWarp(WarpRow row);
    public void RemoveWarpAt(int index);
    public void MoveWarpSource(int index, int mapX, int mapY);
    public bool Undo();
    public bool Redo();
    public void MarkPushed();
}
```

Snapshot `Rows` deliberately uses `IReadOnlyList<T>`, not `IReadOnlySet<T>`: duplicate rows are valid data and multiplicity is part of conflict equality. Constructors must defensively copy input. Snapshot equality is order-independent multiset equality; equal snapshots must return equal hash codes.

`RemoteRow.WorksheetRowNumber` and `RowDelete.WorksheetRowNumber` are one-based Google worksheet row numbers, including the header as row 1. The future gateway is responsible for conversion to the Google API's zero-based grid indexes.

## Task 1: Add verified header metadata and JSON generation

**Repository:** `/home/agent/workspace/illutiagooseserver`

**Files:**
- Modify: `CsvToSql/CsvToSql.Core/Schema/Column.cs`
- Modify: `CsvToSql/CsvToSql.Core/MapsCsvToSql.cs`
- Modify: `CsvToSql/CsvToSql.Core/NpcCsvToSql.cs`
- Modify: `CsvToSql/CsvToSql.Core/NpcSpawnsCsvToSql.cs`
- Modify: `CsvToSql/CsvToSql.Core/WarpTilesCsvToSql.cs`
- Modify: `tools/SchemaGen/SchemaModel.cs`
- Create: `tools/SchemaGen/SchemaJson.cs`
- Modify: `tools/SchemaGen/Program.cs`
- Modify: `tools/README.md:45-49`
- Modify: `tools/Tools.Tests/SchemaModelTests.cs`
- Create: `tools/Tools.Tests/SchemaJsonTests.cs`
- Modify: `tools/Tools.Tests/SchemaJsTests.cs`
- Create: `Goose.IntegrationTests/Schema/WorkbookHeaderContractTests.cs`

**Mutation impact:**
- Source of truth changed: ordered `Column` instances in the four converters; `SchemaRegistry` publishes those instances through `TableSchema.Columns` (`SchemaRegistry.cs:11-31`).
- Important readers: SQL DDL and insert generation (`CsvToSqlConverter.cs:31-39`), `SchemaModel.Build()` (`SchemaModel.cs:28-45`), Apps Script's generated `tools/DataEditor/schema.js`, and the new JSON artifact.
- Derived/cached state affected: regenerated `tools/DataEditor/schema.js`; no runtime cache or database state.
- Required propagation: copy exact fixture row-1 text into descriptors → project `Header` in `SchemaModel` → render both outputs → regenerate `schema.js` → in Task 2 generate and commit the client JSON copy.
- Failure behavior: one-argument invocation still writes only JS; two arguments write JS then JSON, return nonzero on either write failure, and identify the failed path. Do not leave a success message claiming both files were written after a partial failure.
- Invariants: descriptor order and SQL output do not change; every column of each consumed sheet has a nonblank verified header; extra fixture columns are allowed; JSON contains only the four consumed sheets and retains complete ordered columns for each.

**Step 1: Write failing tests**

1. In `WorkbookHeaderContractTests`, open the copied fixture with `XLWorkbook`, locate each of the four sheets, assert every descriptor has nonblank `Header`, and compare `descriptor.Header` to `worksheet.Cell(1, index + 1).GetValue<string>()` with ordinal equality. Do not encode expected header literals in the test.
2. Extend `SchemaModelTests` to prove header projection and unchanged sheet/column order.
3. Add `SchemaJsonTests` proving parseable camel-case JSON, LF endings, null omission, exactly the four sheet names, and full column order matching `SchemaRegistry`.
4. Add a `SchemaJsTests` assertion that an existing header is additive and that the stale `schema.js` check still passes after regeneration.
5. Test `Program.Main` with temporary paths for one argument, two arguments, invalid argument count, and an unwritable second path. Assert exit codes and file existence, not console text alone.

**Step 2: Run red tests**

```bash
cd /home/agent/workspace/illutiagooseserver
dotnet test tools/Tools.Tests/Tools.Tests.csproj --filter 'FullyQualifiedName~Schema'
dotnet test Goose.IntegrationTests/Goose.IntegrationTests.csproj --filter 'FullyQualifiedName~WorkbookHeaderContract'
```

Expected: FAIL because `Column.Header`, `SchemaJson`, and the second output are absent.

**Step 3: Implement the minimum**

- Add `public string Header { get; private set; }` and fluent `public Column HeaderText(string header)` to `Column`; reject null/empty/whitespace. Use `HeaderText` rather than `Header` because C# cannot have a property and method with the same name.
- Inspect row 1 of `Goose.IntegrationTests/Fixtures/aspereta-data.xlsx` and apply `HeaderText(...)` to every descriptor in the four converters. Copy exact cell text, including spaces, capitalization, punctuation, and displayed defaults. Do not infer from descriptor names.
- Add nullable `Header` to `SchemaColumn` and project it without changing list order.
- `SchemaJson.Render(SchemaRoot)` must select the four sheets in registry order and serialize the same model shape as camel-case indented JSON with LF endings and null omission. Do not use JSON-with-comments.
- Make `Program.Main` accept one or two paths: one preserves current JS-only behavior; two writes both. Keep directory creation for each output. Update the README command to document both forms.
- Regenerate JS:

```bash
dotnet run --project tools/SchemaGen -- tools/DataEditor/schema.js
```

**Step 4: Run green and regression tests**

```bash
dotnet test tools/Tools.Tests/Tools.Tests.csproj --filter 'FullyQualifiedName~Schema'
dotnet test Goose.IntegrationTests/Goose.IntegrationTests.csproj --filter 'FullyQualifiedName~WorkbookHeaderContract'
dotnet test Goose.sln
```

Expected: PASS; existing generated-schema and conversion tests remain green.

**Invariant-test matrix:**

| Invariant | Proved by |
|---|---|
| Header text comes from the real fixture, not normalized descriptor names | `WorkbookHeaderContractTests` exact positional comparison |
| Descriptor and SQL positions remain unchanged | existing `Sheets_preserve_registry_order` plus full server test suite |
| Extra helper columns do not become schema columns | header test compares only `Columns.Count`; JSON order/count test |
| Existing JS-only workflow remains valid | `Program_Main_WithOnePath_WritesOnlyJs` and stale JS test |
| Partial JSON write failure is reported | `Program_Main_WhenJsonWriteFails_ReturnsFailure` |

**Step 5: Commit**

```bash
git add CsvToSql/CsvToSql.Core/Schema/Column.cs \
  CsvToSql/CsvToSql.Core/MapsCsvToSql.cs \
  CsvToSql/CsvToSql.Core/NpcCsvToSql.cs \
  CsvToSql/CsvToSql.Core/NpcSpawnsCsvToSql.cs \
  CsvToSql/CsvToSql.Core/WarpTilesCsvToSql.cs \
  tools/SchemaGen tools/Tools.Tests tools/DataEditor/schema.js tools/README.md \
  Goose.IntegrationTests/Schema/WorkbookHeaderContractTests.cs
git commit -m "feat: generate map editor game data schema"
```

## Task 2: Scaffold GameData and embed the generated schema

**Repository:** `/home/agent/workspace/Goose2ClientGodot/.worktrees/map-editor-google-sheets`

**Files:**
- Create: `src/MapEditor.GameData/MapEditor.GameData.csproj`
- Create: `src/MapEditor.GameData/Schema/GameDataSchema.cs`
- Create: `src/MapEditor.GameData/Schema/SchemaHeaderValidator.cs`
- Create/generated: `src/MapEditor.GameData/Schema/game-data-schema.json`
- Create: `tests/MapEditor.GameData.Tests/MapEditor.GameData.Tests.csproj`
- Create: `tests/MapEditor.GameData.Tests/Schema/GameDataSchemaTests.cs`
- Create: `tests/MapEditor.GameData.Tests/Schema/SchemaHeaderValidatorTests.cs`
- Modify: `Goose2ClientGodot.sln`

**Mutation impact:**
- Source of truth changed: no existing state; the client artifact is a generated copy whose authority remains server `SchemaRegistry`.
- Important readers: the new schema loader and all later mappers, validators, and planners.
- Derived state: the embedded resource in `MapEditor.GameData.dll`.
- Required propagation: run the committed server generator → write directly to the client path → embed through an explicit `EmbeddedResource` item → load through `LoadEmbedded()`.
- Failure behavior: malformed JSON, duplicate sheet names, duplicate column names, or missing/blank headers throw `InvalidDataException` with sheet/column context; absent requested names throw `KeyNotFoundException`.
- Invariants: there is one checked-in artifact; runtime has no server path dependency; header checks are ordinal and positional; missing/truncated headers mismatch; trailing worksheet headers are ignored.

**Step 1: Scaffold and write failing tests**

Use project files matching the verified client target and package versions. Give the resource the fixed logical name `MapEditor.GameData.game-data-schema.json`; `LoadEmbedded()` requests that exact name rather than scanning by suffix.

Tests cover embedded loading of all four sheets in generated order, malformed and duplicate stream metadata, missing sheet/column lookups, exact headers, swapped/missing/case-changed/whitespace-changed headers, and accepted trailing headers.

Generate the artifact only after Task 1 is committed:

```bash
cd /home/agent/workspace/illutiagooseserver
dotnet run --project tools/SchemaGen -- \
  tools/DataEditor/schema.js \
  /home/agent/workspace/Goose2ClientGodot/.worktrees/map-editor-google-sheets/src/MapEditor.GameData/Schema/game-data-schema.json

cd /home/agent/workspace/Goose2ClientGodot/.worktrees/map-editor-google-sheets
dotnet sln Goose2ClientGodot.sln add src/MapEditor.GameData/MapEditor.GameData.csproj --solution-folder src
dotnet sln Goose2ClientGodot.sln add tests/MapEditor.GameData.Tests/MapEditor.GameData.Tests.csproj --solution-folder tests
```

**Step 2: Run red**

```bash
dotnet test tests/MapEditor.GameData.Tests/MapEditor.GameData.Tests.csproj --filter 'FullyQualifiedName~Schema'
```

Expected: FAIL because loader and validator types are absent.

**Step 3: Implement the minimum**

- Deserialize camel-case JSON with explicit serializer options; validate into copied read-only collections before publishing `GameDataSchema`.
- Keep JSON DTOs private/internal. Public schema collections must not expose mutable `List<T>` instances.
- `SchemaHeaderValidator.Validate` compares every schema column by position with `StringComparison.Ordinal`; each absent actual cell produces a mismatch with `Actual == null`; trailing cells are ignored.
- Do not add a filesystem fallback to `LoadEmbedded()`.

**Step 4: Run green**

```bash
dotnet test tests/MapEditor.GameData.Tests/MapEditor.GameData.Tests.csproj --filter 'FullyQualifiedName~Schema'
dotnet build Goose2ClientGodot.sln
```

Expected: PASS.

**Invariant-test matrix:**

| Invariant | Proved by |
|---|---|
| Client consumes the generated four-sheet artifact | `LoadEmbedded_ContainsRequiredSheetsInGeneratedOrder` |
| Reordered/truncated headers fail before mapping | swapped and missing header tests |
| Helper columns after the consumed range remain allowed | `Validate_WithTrailingHeaders_ReturnsEmpty` |
| Invalid schema cannot be partially published | malformed/duplicate stream-load tests |

**Step 5: Commit**

```bash
git add Goose2ClientGodot.sln src/MapEditor.GameData tests/MapEditor.GameData.Tests
git commit -m "feat: add embedded game data schema"
```

## Task 3: Add typed records and positional row mapping

**Repository:** `/home/agent/workspace/Goose2ClientGodot/.worktrees/map-editor-google-sheets`

**Files:**
- Create: `src/MapEditor.GameData/Rows/GameDataRows.cs`
- Create: `src/MapEditor.GameData/Rows/SheetRowMapper.cs`
- Create: `tests/MapEditor.GameData.Tests/Rows/SheetRowMapperTests.cs`

**Mutation impact:**
- Source of truth changed: none; mapping creates immutable values from caller-owned cell lists.
- Important readers: snapshots, validation, replacement planning, and later Pull/Push orchestration.
- Derived state: none.
- Required propagation: resolve descriptor name to index → read that cell → apply required/default conversion → construct a typed record. Serialization reverses this through the same schema indexes.
- Failure behavior: missing required cells and malformed integers throw `FormatException` naming sheet, descriptor, and value; short optional rows use descriptor defaults; extra cells are ignored.
- Invariants: no hardcoded positional indexes; invariant-culture integer conversion; required blanks never silently become zero; `ToCells` emits `Columns.Count` cells and round-trips spawn/warp values.

**Step 1: Write failing tests**

Use `GameDataSchema.LoadEmbedded()` for at least one test per row type. Build rows by allocating `sheet.Columns.Count` cells and assigning through `GetColumnIndex`, so tests do not repeat indexes while production mapping still exercises them.

Cover all four records, reordered descriptor metadata loaded through `GameDataSchema.Load(Stream)`, required blank, malformed integer, optional NPC defaults, short rows, extra cells, and spawn/warp `Map(ToCells(value))` round trips. The adversarial reordered-schema test proves the mapper follows descriptors rather than current workbook indexes.

**Step 2: Run red**

```bash
dotnet test tests/MapEditor.GameData.Tests/MapEditor.GameData.Tests.csproj --filter 'FullyQualifiedName~SheetRowMapper'
```

Expected: FAIL because records and mapper are absent.

**Step 3: Implement the minimum**

- Resolve and cache indexes from each `SheetSchema` in the mapper constructor; construction fails when a consumed descriptor is absent.
- For blank optional integer/text fields, apply the generated descriptor default. Support the SQL literal forms present in consumed NPC fields: invariant integer literals and single-quoted text with doubled single-quote escaping. Reject unsupported syntax instead of guessing.
- Keep `equipped_items` as the raw logical string; equipment parsing belongs to preview work.
- `ToCells` places values at descriptor-resolved indexes, leaves unrelated/defaulted positions null, and formats invariantly.

**Step 4: Run green**

```bash
dotnet test tests/MapEditor.GameData.Tests/MapEditor.GameData.Tests.csproj --filter 'FullyQualifiedName~SheetRowMapper'
```

Expected: PASS.

**Invariant-test matrix:**

| Invariant | Proved by |
|---|---|
| Mapping follows generated names, not handwritten indexes | reordered-schema adversarial test |
| Blank required data cannot become a valid zero ID | required-blank test |
| Generated defaults govern optional NPC cells | optional-default tests |
| Outbound rows preserve values and generated positions | spawn/warp round-trip tests |

**Step 5: Commit**

```bash
git add src/MapEditor.GameData/Rows tests/MapEditor.GameData.Tests/Rows
git commit -m "feat: map generated sheet rows to game data records"
```

## Task 4: Add duplicate-preserving snapshots and multiset equality

**Repository:** `/home/agent/workspace/Goose2ClientGodot/.worktrees/map-editor-google-sheets`

**Files:**
- Create: `src/MapEditor.GameData/Snapshots/GameDataSnapshots.cs`
- Create: `src/MapEditor.GameData/Snapshots/SnapshotComparer.cs`
- Create: `tests/MapEditor.GameData.Tests/Snapshots/GameDataSnapshotTests.cs`

**Mutation impact:**
- Source of truth changed: snapshot constructors capture caller-provided rows into private arrays.
- Important readers: later conflict detection and replacement planning tests.
- Derived state: snapshot hash code may be computed on demand; no mutable cache is needed.
- Required propagation: enumerate input once → defensively copy while preserving duplicates → expose a read-only list → compare frequency maps.
- Failure behavior: null constructor input throws `ArgumentNullException`; no input collection is mutated.
- Invariants: order is ignored only for equality; duplicate count is preserved; equal snapshots have equal hashes; exposed collections cannot mutate captured data.

**Step 1: Write failing tests**

For both row types cover reordered equality, duplicate-vs-single inequality, differing duplicate counts, empty equality, hash equality for reordered multisets, caller-list mutation after construction, and inability to alter `Rows` through a mutable cast.

**Step 2: Run red**

```bash
dotnet test tests/MapEditor.GameData.Tests/MapEditor.GameData.Tests.csproj --filter 'FullyQualifiedName~Snapshot'
```

Expected: FAIL because snapshot types are absent.

**Step 3: Implement the minimum**

Use a frequency dictionary keyed by record value. Snapshot equality delegates to it. Implement an order-independent hash that includes every occurrence and total count; do not sort rows and do not convert to `HashSet<T>`. `Rows` is an `IReadOnlyList<T>` backed by a defensive copy.

**Step 4: Run green**

```bash
dotnet test tests/MapEditor.GameData.Tests/MapEditor.GameData.Tests.csproj --filter 'FullyQualifiedName~Snapshot'
```

Expected: PASS.

**Invariant-test matrix:**

| Invariant | Proved by |
|---|---|
| Order alone does not create a conflict | reordered equality tests |
| Duplicate multiplicity does create a conflict | duplicate-vs-single and count tests |
| Snapshot contents cannot drift with caller mutation | defensive-copy test |
| Equality/hash contract holds | reordered hash tests |

**Step 5: Commit**

```bash
git add src/MapEditor.GameData/Snapshots tests/MapEditor.GameData.Tests/Snapshots
git commit -m "feat: add duplicate preserving game data snapshots"
```

## Task 5: Add schema-driven spawn and warp validation

**Repository:** `/home/agent/workspace/Goose2ClientGodot/.worktrees/map-editor-google-sheets`

**Files:**
- Create: `src/MapEditor.GameData/Validation/GameDataValidator.cs`
- Create: `src/MapEditor.GameData/Validation/ValidationResult.cs`
- Create: `tests/MapEditor.GameData.Tests/Validation/GameDataValidatorTests.cs`

**Mutation impact:**
- Source of truth changed: none; validation reads rows, generated SQL metadata, NPC/map dictionaries, and dimensions.
- Important readers: later Push orchestration.
- Derived state: none.
- Required propagation: validate constructor schema → derive destination coordinate limits from `warp_x`/`warp_y` SQL types → evaluate every row → return all deterministic errors without modifying inputs.
- Failure behavior: nonpositive current/open dimensions throw `ArgumentOutOfRangeException`; unsupported coordinate SQL types make validator construction fail with `InvalidDataException`; closed destination maps receive existence and numeric-range checks only.
- Invariants: bounds are half-open (`0 <= x < Width`, `0 <= y < Height`); duplicate warp sources are keyed by `(MapId, MapX, MapY)`; only known open destinations produce destination-bound errors; numeric limits come from generated `Sql`, not copied constants at call sites.

**Step 1: Write failing tests**

Cover valid rows, missing NPC, negative/equal-to-width/equal-to-height source coordinates, duplicate warp source with different destinations, missing destination map, negative and above-`SMALLINT` destination coordinates, open destination out of bounds, closed destination accepted within SQL range, and invalid dimensions. Add an adversarial schema stream whose coordinate SQL differs and prove the validator follows or rejects metadata rather than silently using a hardcoded range.

**Step 2: Run red**

```bash
dotnet test tests/MapEditor.GameData.Tests/MapEditor.GameData.Tests.csproj --filter 'FullyQualifiedName~GameDataValidator'
```

Expected: FAIL because validator types are absent.

**Step 3: Implement the minimum**

- Support integral limits for generated integral SQL types actually present (`SMALLINT`, `INT`, `INTEGER`, `BIGINT`) in one internal resolver. Since row coordinates are `int`, reject a generated range not representable by the row contract rather than truncating it.
- Return stable issue order by input row; emit duplicate-source errors on the second and later occurrences.
- Do not emit a closed-map bounds warning here; later UI/orchestration owns that warning.

**Step 4: Run green**

```bash
dotnet test tests/MapEditor.GameData.Tests/MapEditor.GameData.Tests.csproj --filter 'FullyQualifiedName~GameDataValidator'
```

Expected: PASS.

**Invariant-test matrix:**

| Invariant | Proved by |
|---|---|
| Source coordinates use current map bounds | negative and upper-edge tests |
| Duplicate source tiles are rejected despite different destinations | duplicate-source adversarial test |
| Closed destinations are not checked against invented dimensions | closed-destination test |
| Destination numeric range follows generated schema | altered-SQL adversarial test |
| Validation leaves inputs unchanged | source-collection assertions after validation |

**Step 5: Commit**

```bash
git add src/MapEditor.GameData/Validation tests/MapEditor.GameData.Tests/Validation
git commit -m "feat: validate map sheet data"
```

## Task 6: Plan minimal target-map replacements

**Repository:** `/home/agent/workspace/Goose2ClientGodot/.worktrees/map-editor-google-sheets`

**Files:**
- Create: `src/MapEditor.GameData/Replacement/ReplacementPlanner.cs`
- Create: `src/MapEditor.GameData/Replacement/ReplacementPlan.cs`
- Create: `tests/MapEditor.GameData.Tests/Replacement/ReplacementPlannerTests.cs`

**Mutation impact:**
- Source of truth changed: none; plans are immutable descriptions of future gateway mutations.
- Important readers: the Part 2 Google batch-request builder.
- Derived state: delete and insert lists only.
- Required propagation: partition remote rows by target map → multiset-match desired rows → preserve matched occurrences → emit unmatched target rows as descending one-based deletes → serialize unmatched desired occurrences through `SheetRowMapper` in desired order.
- Failure behavior: reject `WorksheetRowNumber <= 1` because row 1 is the header; do not mutate inputs.
- Invariants: other maps never appear in deletes; duplicates match one-for-one; plans do not renumber rows; no-op replacement yields empty operations.

**Step 1: Write failing tests**

For both row types cover reordered no-op equality, one changed row, duplicate count increase/decrease, interleaved other-map rows, descending deletes, desired insert order, one-based row-number rejection, and descriptor-position serialization. The key adversarial case has two identical remote rows and one desired row and proves exactly one occurrence is deleted.

**Step 2: Run red**

```bash
dotnet test tests/MapEditor.GameData.Tests/MapEditor.GameData.Tests.csproj --filter 'FullyQualifiedName~ReplacementPlanner'
```

Expected: FAIL because planner types are absent.

**Step 3: Implement the minimum**

Use occurrence counts/queues rather than set subtraction. Plans do not execute writes, update snapshots, renumber rows after deletion, or append other-map rows. `Sheet` is exactly `NPC Spawns` or `Warptiles`; inserts contain only unmatched local rows serialized by the mapper.

**Step 4: Run green**

```bash
dotnet test tests/MapEditor.GameData.Tests/MapEditor.GameData.Tests.csproj --filter 'FullyQualifiedName~ReplacementPlanner'
```

Expected: PASS.

**Invariant-test matrix:**

| Invariant | Proved by |
|---|---|
| Duplicate rows are diffed by occurrence | identical-duplicate adversarial tests |
| Other maps are never rewritten | interleaved-other-map tests |
| Deletes are safe for index-shifting APIs | descending-delete tests |
| Generated positions control inserted cells | descriptor-position serialization test |

**Step 5: Commit**

```bash
git add src/MapEditor.GameData/Replacement tests/MapEditor.GameData.Tests/Replacement
git commit -m "feat: plan map scoped sheet replacements"
```

## Task 7: Add reversible sheet-data editing and dirty baseline

**Repository:** `/home/agent/workspace/Goose2ClientGodot/.worktrees/map-editor-google-sheets`

**Files:**
- Create: `src/MapEditor.GameData/Editing/SheetEditSession.cs`
- Create: `src/MapEditor.GameData/Editing/SheetEditCommand.cs`
- Create: `tests/MapEditor.GameData.Tests/Editing/SheetEditSessionTests.cs`

**Mutation impact:**
- Source of truth changed: private mutable spawn/warp lists and edit-history state owned by each `SheetEditSession`.
- Important readers: public read-only list views, `IsDirty`, `CanUndo`, `CanRedo`, and later per-document UI state.
- Derived state: current state ID, pushed baseline ID, undo stack, and redo stack.
- Required propagation: validate index → capture exact before/after value and index → mutate list → assign new state ID → push command and clear redo. Undo replays the inverse then restores the before-state ID; redo replays forward then restores the after-state ID. `MarkPushed` sets the baseline to current state without clearing history.
- Failure behavior: invalid indexes throw before list/history/state mutation; moving to identical coordinates is a no-op with no history entry; empty undo/redo returns false and changes nothing.
- Invariants: duplicates are addressed by index; undo restores exact order/value; a new edit clears redo; undo back to pushed state clears dirty; undo away from a newly marked pushed state becomes dirty; sessions share no state.

**Step 1: Write failing tests**

Cover all six edit methods and undo/redo, duplicate removal by index, exact order restoration, move preserving non-coordinate fields, no-op move, redo clearing, baseline transitions before and after `MarkPushed`, invalid-index atomicity, empty undo/redo, and two-session isolation. Assert final rows and dirty state, not just stack flags.

**Step 2: Run red**

```bash
dotnet test tests/MapEditor.GameData.Tests/MapEditor.GameData.Tests.csproj --filter 'FullyQualifiedName~SheetEditSession'
```

Expected: FAIL because session and commands are absent.

**Step 3: Implement the minimum**

- Defensively copy constructor inputs and expose read-only wrappers.
- Keep command classes internal. Use separate add/remove/move command types or equivalent strongly typed commands; do not use lifecycle boolean flags.
- Commands mutate only their owning row list and preserve the captured index. They do not validate domain rules, update snapshots, call gateways, or publish events.
- Follow the verified map-history state-ID behavior, but do not copy its byte-cap machinery into this YAGNI foundation.

**Step 4: Run green and the full client suite**

```bash
dotnet test tests/MapEditor.GameData.Tests/MapEditor.GameData.Tests.csproj
dotnet test Goose2ClientGodot.sln
dotnet build Goose2ClientGodot.sln
```

Expected: PASS.

**Invariant-test matrix:**

| Invariant | Proved by |
|---|---|
| Duplicate entries are edited by exact occurrence | duplicate-index remove/undo test |
| Undo/redo restores observable rows and order | each command round-trip test |
| Dirty state tracks pushed baseline through history | baseline-transition tests |
| Failed edits are atomic | invalid-index state/list/history test |
| Per-document histories cannot leak | two-session isolation test |

**Step 5: Commit**

```bash
git add src/MapEditor.GameData/Editing tests/MapEditor.GameData.Tests/Editing
git commit -m "feat: add sheet data edit history"
```

## Final red-team verification

```bash
cd /home/agent/workspace/illutiagooseserver
dotnet test Goose.sln
git status --short

cd /home/agent/workspace/Goose2ClientGodot/.worktrees/map-editor-google-sheets
dotnet test Goose2ClientGodot.sln
dotnet build Goose2ClientGodot.sln
git status --short
```

Verify artifact reproducibility:

```bash
cd /home/agent/workspace/illutiagooseserver
dotnet run --project tools/SchemaGen -- \
  tools/DataEditor/schema.js \
  /home/agent/workspace/Goose2ClientGodot/.worktrees/map-editor-google-sheets/src/MapEditor.GameData/Schema/game-data-schema.json
git diff --exit-code -- tools/DataEditor/schema.js

cd /home/agent/workspace/Goose2ClientGodot/.worktrees/map-editor-google-sheets
git diff --exit-code -- src/MapEditor.GameData/Schema/game-data-schema.json
```

Confirm:

- snapshots expose `IReadOnlyList<T>`, never a set;
- no client production code contains handwritten worksheet indexes, workbook headers, or SQL numeric ranges;
- the server's one-path generator command still works;
- `MapEditor.GameData` references no Google, Godot, Avalonia, or rendering package;
- the client has no runtime filesystem dependency on the server repository;
- mutable collections are privately owned and exposed only through read-only views;
- no generated artifact or source change remains uncommitted in either repository.
