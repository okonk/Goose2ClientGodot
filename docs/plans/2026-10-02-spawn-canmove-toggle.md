# Spawn Can-Move Toggle Implementation Plan

**Goal:** Add a tri-state "Can move" checkbox to the map editor's Spawn Properties panel that reads and writes the `canMove` key in the spawn row's `properties` JSON cell, and make editor-appended numeric cells land in the sheet as numbers instead of text.

**Architecture:** The `properties` column already round-trips end to end (`NpcSpawnRow.Properties`, mapper, clipboard, push); this plan adds the missing UI using the existing NPC-picker pattern — a pure JSON helper in `MapEditor.GameData`, a pending value + commit method on the App view-model layer, and code-behind sync in `MainWindow`. The numeric fix is confined to `GoogleBatchBuilder`, which gains schema-kind awareness.

**Tech Stack:** C# / .NET 8–10, Avalonia 11.3 (`CheckBox.IsThreeState`, `ToggleButton.IsCheckedProperty`), System.Text.Json (already referenced by `MapEditor.GameData`), xUnit, Google Sheets batchUpdate API.

**Design doc:** `docs/plans/2026-10-02-spawn-canmove-toggle-design.md` (this worktree).

Per repo `AGENTS.md`: no new comments in code unless a "why" is non-obvious; the design doc carries the rationale.

---

## APIs verified

| API | Citation |
| --- | --- |
| `NpcSpawnRow(int, int, int, int, string Properties = "")` | `src/MapEditor.GameData/Rows/GameDataRows.cs:18` |
| `SheetEditSession.UpdateSpawn(index, row)` — no-op on equality, pushes undoable `SheetUpdateCommand`, raises `HistoryChanged` | `src/MapEditor.GameData/Editing/SheetEditSession.cs:80`, `:349` |
| `SheetEditSession.AddSpawn(row)` | used at `src/MapEditor.App/ViewModels/MapDocumentViewModel.cs:976` |
| `AddSpawnAt` (requires `state.SelectedNpcId`) | `MapDocumentViewModel.cs:962-978` |
| `CommitSpawnNpc` — the pattern `CommitSpawnCanMove` mirrors | `MapDocumentViewModel.cs:1045-1070` |
| Paste site already carries `payload.SpawnProperties` — unchanged | `MapDocumentViewModel.cs:1199` |
| `DocumentGameDataState.SelectedNpcId` — nullable pending pattern | `src/MapEditor.App/ViewModels/DocumentGameDataState.cs:127-131` |
| Undo → `HistoryChanged` → `OnGameDataStateChanged` → `SyncRightPanel` → `SyncSpawnProperties` | `DocumentEditTimeline.cs:36`, `MainWindow.axaml.cs:1516-1533`, `:1586`, `:1600-1611` |
| Picker handler pattern (`e.Property == …SelectedItemProperty`) | `MainWindow.axaml.cs:950-964`, wiring at `:96` |
| `SpawnProperties` panel markup (NPC picker inside card) | `src/MapEditor.App/Views/MainWindow.axaml:238-246` |
| `ToggleButton.IsCheckedProperty` / `using Avalonia.Controls.Primitives` | already used at `MainWindow.axaml.cs:751`, `:10` |
| `GoogleBatchBuilder.Build` + `StringValue` write | `src/MapEditor.GameData.Google/Sheets/GoogleBatchBuilder.cs:11-18`, `:63` |
| Gateway holds `_schema`, calls `Build` | `src/MapEditor.GameData.Google/Sheets/GoogleSheetsGateway.cs:79`, `:280` |
| Reads use `UNFORMATTEDVALUE` (numeric cells return `"7"`) | `GoogleSheetsGateway.cs:58` |
| `SheetSchema.Columns` / `ColumnSchema.Kind` (string, e.g. `"Id"`, `"Int"`, `"Text"`) | `src/MapEditor.GameData/Schema/GameDataSchema.cs:178`, `:212` |
| `GameDataSchema.GetRequiredSheet(name)` | `GameDataSchema.cs:87` |
| `ReplacementPlan(string Sheet, Deletes, Inserts)`; `RowInsert(IReadOnlyList<string?> CellValues)` | `src/MapEditor.GameData/Replacement/ReplacementPlan.cs:9-13` |
| Schema kinds of the two appended sheets: `NPC Spawns` = Id, Id, Int, Int, Text; `Warptiles` = Id, Int, Int, Id, Int, Int | `src/MapEditor.GameData/Schema/game-data-schema.json` |
| Test fixture: `MainWindowHarness.Create()`, `AttachSession`, `Control<T>`, `Dispatcher.UIThread.RunJobs()`, `vm.Undo()` | `tests/MapEditor.App.Tests/GameDataPropertiesTests.cs:34-58`, `:251-266` |
| Tests reach internals (`AddSpawnAt`, `CommitSpawnNpc`) | `src/MapEditor.App/Properties/AssemblyInfo.cs:3` (`InternalsVisibleTo`); `GameDataClipboardTests.cs:301`, `NpcPreviewCanvasTests.cs:120` |
| `GameDataSyncSession(spreadsheetId, mapId, RemoteGameData)` fixture shape | `GameDataPropertiesTests.cs:268-277` |
| Server `canMove` read (no server change needed) | `<server>/Goose/NPC.cs:635` |

**Design-doc refinement (gap 1 resolution):** the doc proposed a `_syncingSpawnPanel` guard flag so programmatic checkbox assignment doesn't stamp a pending value from a non-editable row. Task 2's `CommitSpawnCanMove` achieves the same behavior statelessly — a non-editable selected row makes it a complete no-op — which also makes the editable-row sync case refresh the pending value from the row (the desired picker-equivalent). No flag is introduced; observable behavior matches the doc.

---

## Task 1: `SpawnMoveOverride` + `SpawnPropertiesJson`

**Files:**
- Create: `src/MapEditor.GameData/Rows/SpawnPropertiesJson.cs`
- Test: `tests/MapEditor.GameData.Tests/Rows/SpawnPropertiesJsonTests.cs`

**Contract:**

```csharp
namespace MapEditor.GameData.Rows;

public enum SpawnMoveOverride { Default, Movable, Stationary }

public static class SpawnPropertiesJson
{
    public static bool TryRead(string properties, out SpawnMoveOverride value);
    public static bool TryWrite(string properties, SpawnMoveOverride value, out string result);
}
```

Precondition: `properties` is non-null (`NpcSpawnRow.Properties` is non-null by construction; blank means "no overrides").

- `TryRead`: blank/whitespace → `true`/`Default`. Parse with `JsonDocument`; malformed JSON or a root whose `ValueKind != Object` → `false`/`Default`. `canMove` (ordinal key match) missing → `Default`; `True` → `Movable`; `False` → `Stationary`; any other `ValueKind` → `false`/`Default` (this is the `{"canMove":1}` case that crashes server startup — the editor refuses to touch it).
- `TryWrite`: non-editable input (same rule) → `false`, `result = ""`, caller must not touch the row. Otherwise rebuild the object in original document order: other keys copied verbatim as `JsonElement`s; `canMove` replaced in place, appended last when absent, omitted entirely for `Default`. If the object ends up empty the result is `""` — never `{}`. Empty input: `Default` → `""`, `Movable` → `{"canMove":true}`, `Stationary` → `{"canMove":false}` (so `TryWrite("")` always succeeds). Serialize with `Utf8JsonWriter` and `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`, no indented output.

**Step 1: Write the failing tests** — `SpawnPropertiesJsonTests.cs`:

Read:
- `TryRead("")`, `"   "`, `"{}"`, `"{\"facing\":1}"` → `true`/`Default`.
- `{"canMove":true}` → `Movable`; `{"canMove":false}` → `Stationary`.
- `{"canMove":` (malformed), `"[1,2]"` (array root), `{"canMove":1}`, `{"canMove":"yes"}`, `{"canMove":null}` → `false`.

Write:
- `TryWrite("{\"canMove\":true,\"foo\":1}", Default)` → `{"foo":1}`.
- `TryWrite("{\"foo\": 1}", Movable)` → `{"foo":1,"canMove":true}` (other key preserved, `canMove` appended last, whitespace normalized).
- `TryWrite("{\"canMove\":true}", Default)` → `""`; `TryWrite("{\"canMove\":true}", Default)` never yields `"{}"`.
- `TryWrite("", Default/Movable/Stationary)` → `""` / `{"canMove":true}` / `{"canMove":false}`.
- `TryWrite("{\"canMove\":", Movable, out _)` → `false` (adversarial: an implementation that overwrites unparseable cells fails here).
- Round trip: for each `v` in the three states and each base in `""`, `{"foo":1}`: `TryWrite(base, v, out s)` then `TryRead(s)` returns `v`.

**Step 2: Red** — `dotnet test tests/MapEditor.GameData.Tests --filter FullyQualifiedName~SpawnPropertiesJsonTests` → compile failure (type missing).

**Step 3: Implement** — single file, `System.Text.Json` (already used in this project, `Schema/GameDataSchema.cs:5`). No new package reference.

**Step 4: Green** — same command, all pass.

**Step 5: Commit**

```bash
git add src/MapEditor.GameData/Rows/SpawnPropertiesJson.cs tests/MapEditor.GameData.Tests/Rows/SpawnPropertiesJsonTests.cs
git commit -m "feat: add spawn properties canMove read/write helper"
```

| Invariant | Proved by |
| --- | --- |
| Unknown keys survive every write | `TryWrite` key-preservation tests |
| Blank is the only "no overrides" form | removal-to-empty → `""`, never `{}` |
| The editor refuses values the server would choke on | malformed/array/wrong-type read + adversarial write test |
| Round-trip stability | read-after-write loop |

---

## Task 2: Pending state, `CommitSpawnCanMove`, placement stamp

**Files:**
- Modify: `src/MapEditor.App/ViewModels/DocumentGameDataState.cs` (field block `:24-34`, property beside `SelectedNpcId` `:127`)
- Modify: `src/MapEditor.App/ViewModels/MapDocumentViewModel.cs:962-978` (`AddSpawnAt`), new `CommitSpawnCanMove` beside `CommitSpawnNpc` (`:1045`)
- Test: `tests/MapEditor.App.Tests/GameDataPropertiesTests.cs`

**Mutation impact:**
- Source of truth changed: `NpcSpawnRow.Properties` inside `SheetEditSession._spawns` (`SheetEditSession.cs:80`); new pending value `DocumentGameDataState.SelectedCanMove` (per-document, like `SelectedNpcId`).
- Important readers: `ReplacementPlanner` matches remote rows by `NpcSpawnRow` value equality (`src/MapEditor.GameData/Replacement/ReplacementPlanner.cs:59-68`) — a toggled row becomes delete+append on push; clipboard copy sites read `row.Properties` (already carry it); rendering reads position/id only.
- Derived/cached state affected: `SpawnSnapshot`/`SnapshotComparer` hash rows by value — no code change, the field already participates.
- Required propagation sequence:
  1. `UpdateSpawn` mutates `_spawns[index]` and pushes an undoable command (`SheetEditSession.cs:80-93`).
  2. `Push` raises `HistoryChanged` (`:349`) → `DocumentEditTimeline` (`:36`) → `DocumentGameDataState.Changed` → `MainWindow.OnGameDataStateChanged` (`MainWindow.axaml.cs:1516`) → `SyncRightPanel` → `SyncSpawnProperties` re-reads the row. (Task 3 adds the checkbox read; until then the row mutation is the observable half.)
  3. Undo replays the command and raises `HistoryChanged` the same way — no new mechanism.
- Invariants to preserve: toggling an unparseable cell changes neither the row nor the pending value; a no-op toggle (`before == row`) does not dirty the session; paste keeps clipboard fidelity (untouched, `MapDocumentViewModel.cs:1199`).
- Observable proof required: tests assert `session.Edits.Spawns[i].Properties` values and undo restore, not that a method was called.

**Step 1: Write the failing tests** (harness-driven, calling internals directly — `InternalsVisibleTo` is confirmed):

- `CommitSpawnCanMove_WithSelectedSpawn_WritesRow_AndUndoRestores`: session with one spawn `new NpcSpawnRow(1, 10, 3, 4)`; select it (`GameData.SelectedSpawn = 0`); `vm.CommitSpawnCanMove(Movable)` → row `Properties == "{\"canMove\":true}"`, `Timeline.CanUndo`, `vm.Undo()` → `""`.
- `CommitSpawnCanMove_OnUnparseableCell_TouchesNeitherRowNorPending` (adversarial): row `Properties = "{\"canMove\":"`; commit any value → row unchanged and `SelectedCanMove` unchanged.
- `CommitSpawnCanMove_WithNoSelection_SetsPendingOnly`: `SelectedSpawn = null` → row list unchanged, `SelectedCanMove` set.
- `AddSpawnAt_StampsPendingOverride_AndNewDocumentDefaultsToBlank`: pending `Stationary` + `SelectedNpcId = 1` → `vm.AddSpawnAt(5, 6)` → added row `Properties == "{\"canMove\":false}"`; with pending unset (`null`) → `""`.

**Step 2: Red** — compile failure (`SelectedCanMove`, `CommitSpawnCanMove` missing).

**Step 3: Implement.**

`DocumentGameDataState`: `private SpawnMoveOverride? _selectedCanMove;` and

```csharp
public SpawnMoveOverride? SelectedCanMove
{
    get => _selectedCanMove;
    set => SetField(ref _selectedCanMove, value);
}
```

`MapDocumentViewModel`:

```csharp
internal void CommitSpawnCanMove(SpawnMoveOverride value)
{
    if (_gameData is not { } state)
    {
        return;
    }

    if (state.SelectedSpawn is { } index && state.Session is { } session && index < session.Edits.Spawns.Count)
    {
        NpcSpawnRow row = session.Edits.Spawns[index];
        if (!SpawnPropertiesJson.TryWrite(row.Properties, value, out string result))
        {
            return;
        }

        state.SelectedCanMove = value;
        session.Edits.UpdateSpawn(index, row with { Properties = result });
        return;
    }

    state.SelectedCanMove = value;
}
```

`AddSpawnAt` (`:976`): stamp the pending value —

```csharp
SpawnPropertiesJson.TryWrite(string.Empty, state.SelectedCanMove ?? SpawnMoveOverride.Default, out string properties);
session.Edits.AddSpawn(new NpcSpawnRow(npcId, session.MapId, x, y, properties));
```

(`TryWrite("")` always succeeds per the Task 1 contract; no failure branch needed.)

**Step 4: Green** — `dotnet test tests/MapEditor.App.Tests --filter FullyQualifiedName~GameDataPropertiesTests`.

**Step 5: Commit**

```bash
git add src/MapEditor.App/ViewModels/DocumentGameDataState.cs src/MapEditor.App/ViewModels/MapDocumentViewModel.cs tests/MapEditor.App.Tests/GameDataPropertiesTests.cs
git commit -m "feat: commit spawn can-move overrides through the edit session"
```

| Invariant | Proved by |
| --- | --- |
| Toggle is undoable in one step | `..._WritesRow_AndUndoRestores` |
| Broken cells are untouchable, pending included | `..._TouchesNeitherRowNorPending` |
| New spawns take the pending value; fresh documents stamp blank | `AddSpawnAt_StampsPendingOverride_AndNewDocumentDefaultsToBlank` |
| Paste fidelity unchanged | existing `GameDataClipboardTests` stay green |

---

## Task 3: Panel checkbox, warning, and sync

**Files:**
- Modify: `src/MapEditor.App/Views/MainWindow.axaml:240-245` (inside the `SpawnProperties` card, after the picker)
- Modify: `src/MapEditor.App/Views/MainWindow.axaml.cs` — wiring beside `:96`, handler beside `OnSpawnNpcPickerChanged` (`:950`), `SyncSpawnProperties` (`:1600-1611`)
- Test: `tests/MapEditor.App.Tests/GameDataPropertiesTests.cs`

**Markup** (matches the card's existing label + control rhythm):

```xml
<TextBlock Text="Can move" Classes="muted" FontSize="11" />
<StackPanel Orientation="Horizontal" Spacing="8">
  <CheckBox x:Name="SpawnCanMoveCheck" IsThreeState="True" VerticalAlignment="Center" />
  <TextBlock x:Name="SpawnCanMoveWarning" Classes="muted" IsVisible="False" MaxWidth="180"
             TextWrapping="Wrap" VerticalAlignment="Center"
             Text="Invalid properties value — edit in the sheet to fix." />
</StackPanel>
```

**Code-behind:**

- Constructor: `SpawnCanMoveCheck.PropertyChanged += OnSpawnCanMoveCheckChanged;` (same pattern as `:96`).
- Handler (mirrors `OnSpawnNpcPickerChanged`; `ToggleButton` is already imported, `:10`):

```csharp
private void OnSpawnCanMoveCheckChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
{
    if (e.Property != ToggleButton.IsCheckedProperty)
    {
        return;
    }

    Document.CommitSpawnCanMove(SpawnCanMoveCheck.IsChecked switch
    {
        true => SpawnMoveOverride.Movable,
        false => SpawnMoveOverride.Stationary,
        _ => SpawnMoveOverride.Default,
    });
}
```

- `SyncSpawnProperties` gains the checkbox half; the picker half is unchanged:
  - selection branch: `bool editable = SpawnPropertiesJson.TryRead(spawn.Properties, out SpawnMoveOverride move);` then `SpawnCanMoveCheck.IsEnabled = editable; SpawnCanMoveWarning.IsVisible = !editable; SpawnCanMoveCheck.IsChecked = editable ? ToIsChecked(move) : null;`
  - no-selection branch (currently the method does nothing): `IsEnabled = true`, warning collapsed, `IsChecked = ToIsChecked(state.SelectedCanMove ?? SpawnMoveOverride.Default)`.
  - `private static bool? ToIsChecked(SpawnMoveOverride value)` → `true`/`false`/`null`.

  Programmatic `IsChecked` assignment re-enters the handler; that is safe by design (see the refinement note above): editable rows refresh the pending value from the row (desired, picker-equivalent), non-editable rows make `CommitSpawnCanMove` a no-op. Do not add a guard flag.

**Step 1: Write the failing tests** (extend `GameDataPropertiesTests`; fixture helper takes a properties string — reuse `Session()` shape at `:268-277` with `new NpcSpawnRow(1, 10, 3, 4, props)`):

- `SpawnCanMoveCheckbox_ReflectsSelectedRowState`: select spawns carrying `""` / `{"canMove":true}` / `{"canMove":false}` in turn → `IsChecked` is `null` / `true` / `false`, `IsEnabled == true`, warning hidden.
- `SpawnCanMoveCheckbox_OnMalformedProperties_DisablesAndWarns`: row `Properties = "{\"canMove\":"` → `IsEnabled == false`, `SpawnCanMoveWarning.IsVisible == true`.
- `SpawnCanMoveCheckbox_Toggle_WritesRowAndUndoRestores`: with a selection, set `IsChecked = true` → row `{"canMove":true}`; `vm.Undo()` → `""` and the checkbox re-syncs to `null` (proves the `HistoryChanged` → `SyncRightPanel` chain reaches the new control).
- `SpawnCanMoveCheckbox_ToggleOnDisabledRow_LeavesRowUntouched` (adversarial): disabled checkbox on a malformed row; programmatic `IsChecked = true` → row `Properties` still the malformed text, warning still visible.
- `SpawnCanMoveCheckbox_NoSelection_EditsPendingForNextPlacement`: `SelectedSpawn = null`, `SelectedNpcId = 1`, set `IsChecked = false` → `vm.AddSpawnAt(5, 6)` → new row `{"canMove":false}`.

**Step 2: Red** — `dotnet test tests/MapEditor.App.Tests --filter FullyQualifiedName~GameDataPropertiesTests` → `FindControl` returns null (control missing).

**Step 3: Implement** markup + wiring + sync as above.

**Step 4: Green** — same filter, then the whole App test project (`dotnet test tests/MapEditor.App.Tests`) to confirm panel-enumerating tests (`MainWindowGameDataTests.cs:331-473`, `ShortcutTests.cs:470`, `GameDataPropertiesTests.cs:265`) stay green; the control is additive, so any failure there is a real regression, not a fixture pin to bump. If one asserts exact children, update it to include the new controls.

**Step 5: Commit**

```bash
git add src/MapEditor.App/Views/MainWindow.axaml src/MapEditor.App/Views/MainWindow.axaml.cs tests/MapEditor.App.Tests/GameDataPropertiesTests.cs
git commit -m "feat: add tri-state can-move toggle to the spawn properties panel"
```

| Invariant | Proved by |
| --- | --- |
| Three states display correctly | `..._ReflectsSelectedRowState` |
| Unparseable cells are inert and labeled | `..._DisablesAndWarns` + adversarial toggle test |
| Undo refreshes the checkbox | `..._WritesRowAndUndoRestores` |
| Pending value drives placement with no selection | `..._EditsPendingForNextPlacement` |

---

## Task 4: Numeric cells on append

**Files:**
- Modify: `src/MapEditor.GameData.Google/Sheets/GoogleBatchBuilder.cs:11-18`, `:47-71`
- Modify: `src/MapEditor.GameData.Google/Sheets/GoogleSheetsGateway.cs:280`
- Test: `tests/MapEditor.GameData.Google.Tests/Sheets/GoogleBatchBuilderTests.cs` (all `Build(` call sites + cell assertions at `:101-118`, `:138`)

**Mutation impact:**
- Source of truth changed: none — wire representation of existing values only.
- Important readers: the editor's own pull reads `UNFORMATTEDVALUE` (`GoogleSheetsGateway.cs:58`), so numeric cells return `"7"` and `SheetRowMapper.ReadInt` parses unchanged; the server importer already consumes the numeric cells that sheet-authored rows produce today.
- Derived/cached state affected: none.
- Required propagation sequence: `ReplaceOwnedRowsAsync` passes `_schema` (`:79`) into `Build`; `AppendPlan` resolves `schema.GetRequiredSheet(plan.Sheet).Columns` and picks `ExtendedValue` per cell.
- Invariants to preserve: trailing-empty-cell omission (`GoogleBatchBuilder.cs:55-61`) and delete-before-append ordering are untouched; `properties` (Text kind) always writes `StringValue`; an `Id`/`Int` cell that fails to parse falls back to `StringValue` rather than dropping the value.
- Observable proof required: request payload asserts `NumberValue`/`StringValue` on the final cells.

**Step 1: Write the failing tests** in `GoogleBatchBuilderTests`:
- Existing numeric assertions at `:101`, `:103-104`, `:108`, `:113`, `:117-118` become `NumberValue` (e.g. `Assert.Equal(10, interior[0].UserEnteredValue.NumberValue);`).
- `Build_IdColumns_WriteNumericCells` for `NPC Spawns` cells `{"10","1","5","6"}` → four `NumberValue`s (adversarial: fails on the current string-only implementation).
- `Build_TextColumn_NumericLookingValue_StaysString`: `{"10","1","5","6","7"}` → fifth cell `StringValue == "7"` (fails on a naive `long.TryParse`-over-all-columns heuristic).
- `Build_IntColumn_Unparseable_FallsBackToString`: `{"10","x","5","6"}` → second cell `StringValue == "x"`.
- `Build_AppendRow_KeepsNonBlankPropertiesAsTheFifthCell` (`:120-139`) stays asserting `StringValue` for all five — properties JSON untouched.

**Step 2: Red** — compile failure (new `Build` parameter).

**Step 3: Implement.**

```csharp
public static BatchUpdateSpreadsheetRequest Build(
    GameDataSchema schema,
    IReadOnlyDictionary<string, int> sheetIds,
    ReplacementPlan spawnPlan,
    ReplacementPlan warpPlan)
```

`AppendPlan` takes the schema (or resolves columns once per plan): for cell index `i` in an insert, when `i < columns.Count && IsNumericKind(columns[i].Kind)` and `long.TryParse(cell, NumberStyles.Integer, CultureInfo.InvariantCulture, out long number)`, emit `new ExtendedValue { NumberValue = number }`; otherwise the existing `StringValue`. `IsNumericKind`: `Kind` is `"Id"` or `"Int"` (ordinal) — the only kinds on the two appended sheets; a one-line comment naming that constraint is justified per AGENTS.md. Gateway call site becomes `GoogleBatchBuilder.Build(_schema, sheetIds, spawnPlan, warpPlan)`.

Update every `Build(` call site in the test file to pass `GameDataSchema.LoadEmbedded()`; the test plans' 4-cell spawn rows are fine against the 5-column schema (kind lookup is positional, guarded by `i < columns.Count`).

**Step 4: Green** — `dotnet test tests/MapEditor.GameData.Google.Tests`.

**Step 5: Commit**

```bash
git add src/MapEditor.GameData.Google/Sheets/GoogleBatchBuilder.cs src/MapEditor.GameData.Google/Sheets/GoogleSheetsGateway.cs tests/MapEditor.GameData.Google.Tests/Sheets/GoogleBatchBuilderTests.cs
git commit -m "fix: write appended id and int cells as numeric sheet cells"
```

| Invariant | Proved by |
| --- | --- |
| Numeric-kind cells become numbers | `Build_IdColumns_WriteNumericCells` (adversarial) |
| Text columns are never coerced | `Build_TextColumn_NumericLookingValue_StaysString` |
| Unparseable numerics are preserved as text, not dropped | `Build_IntColumn_Unparseable_FallsBackToString` |
| Properties round-trip untouched | existing fifth-cell test stays green |

---

## Task 5: Full gate and hand-off smoke

**Step 1:** `dotnet test Goose2ClientGodot.sln` — expect all projects green (baseline at design time: 3,678 passed, 0 failed).

**Step 2: Manual smoke** (needs Google credentials and a server restart; record the outcome in the PR, don't claim):
1. Select a stationary mob's spawn, set ☑, push → sheet `NPC Spawns` E is `{"canMove":true}` and A–D are right-aligned numeric cells with no warning triangle.
2. Restart the server → the mob wanders and walks home beyond 10 tiles (`<server>/Goose/NPC.cs:411-430` behavior).
3. ☐ → `{"canMove":false}`; ◪ → E blank.
4. Place a new spawn with ☑ pending → E is `{"canMove":true}`, ids numeric.
5. Select a row whose E holds `{"canMove":1}` → checkbox disabled with the warning; toggling other rows leaves it untouched.

**Step 3:** No doc updates needed in either repo — the server design doc and `SKILL.md` key vocabulary already describe `canMove`; the editor now authors exactly the documented values.
