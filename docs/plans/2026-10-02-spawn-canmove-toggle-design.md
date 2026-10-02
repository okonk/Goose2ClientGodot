# Spawn Can-Move Toggle Design

Paths are relative to this repo (`Goose2ClientGodot`) unless written `<server>/`, which is the
`illutiagooseserver` checkout.

## Goal

Add a tri-state "Can move" checkbox to the map editor's Spawn Properties panel that writes the
`canMove` key into the spawn row's `properties` JSON cell, so a placement's movement override can
be authored from the editor instead of only in the sheet.

Folded in: appended rows currently land in the sheet as text-formatted numbers; numeric-kind
columns must be written as numeric cells.

## Context — what already exists

- `<server>/docs/plans/2026-09-12-npc-spawn-properties-design.md` defines the mechanism: the
  `NPC Spawns` sheet's fifth column `properties` holds a JSON dictionary; the server reads it at
  load and applies `this.CanMove = this.Properties.GetProperty("canMove", template.CanMove)`
  (`<server>/Goose/NPC.cs:635`). Restart is required for a change to take effect.
- Part 1 (server) and Part 2 (editor pass-through) are implemented here: `NpcSpawnRow` carries
  `string Properties = ""` (`src/MapEditor.GameData/Rows/GameDataRows.cs:18`), the mapper pair,
  clipboard, and push path all preserve the cell. The schema artifact already has the column.
- Part 2 explicitly left the UI out of scope: the `Spawn Properties` panel
  (`src/MapEditor.App/Views/MainWindow.axaml:238`) has only the NPC picker, and
  `SheetEditSession.UpdateSpawn` (`src/MapEditor.GameData/Editing/SheetEditSession.cs:80`) has no
  caller for this field. This design adds that UI. No server change is needed.

Server semantics the UI must express — three states, not two:

| Cell | Meaning |
| --- | --- |
| blank / no `canMove` key | template default (the NPC's `stationary` flag decides) |
| `{"canMove":true}` | force this spawn to wander, even on a stationary template |
| `{"canMove":false}` | force this spawn to hold position, even on a movable template |

The value must be a JSON boolean; `1`/`0` crashes server startup (accepted server-side risk,
called out in the server design doc as the mistake authors actually make).

## Decisions taken in brainstorming

- **Tri-state checkbox**, not a dropdown: ☑ = `{"canMove":true}`, ◪ (indeterminate) = no override,
  ☐ = `{"canMove":false}`.
- **Preserve unknown keys**: the column is deliberately generic for future keys, so the toggle
  parses the cell, replaces/removes only `canMove`, and re-serializes.
- **Unparseable cells disable the checkbox with an inline warning** — the editor never destroys a
  value it doesn't understand.
- **The checkbox also acts as a pending value** for newly placed spawns, mirroring the NPC picker.
- **Approach A**: mirror the existing NPC-picker pattern (code-behind sync + view-model commit),
  with the JSON semantics as a pure helper in `MapEditor.GameData`.

## Data model — `MapEditor.GameData`

New file `src/MapEditor.GameData/Rows/SpawnPropertiesJson.cs`, beside `NpcSpawnRow`. No row-model,
record, schema, or mapper change — `Properties` is already carried end to end.

```csharp
public enum SpawnMoveOverride { Default, Movable, Stationary }

public static class SpawnPropertiesJson
{
    public static bool TryRead(string properties, out SpawnMoveOverride value);
    public static bool TryWrite(string properties, SpawnMoveOverride value, out string result);
}
```

**Read.** Blank/whitespace → `true`/`Default`. A valid object without `canMove` (or `{}`) →
`true`/`Default`. `canMove: true`/`false` → `Movable`/`Stationary`. **Not editable** (`TryRead`
returns `false`, `value = Default`): malformed JSON; a non-object root; or `canMove` present but
not a JSON boolean — the last case extends the disable rule to the value that crashes the server.

**Write.** Parse → set, replace, or remove the `canMove` key → re-serialize. Other keys keep their
original order; `canMove` is appended last when absent. `Default` removes the key; if nothing
remains the result is `""`, keeping blank as the single representation of "no overrides" (the
server design doc's convention). `TryWrite("")` with any value always succeeds. Writing to a
non-editable input returns `false` and leaves `result` at `""` — the caller must not touch the row.

**Accepted side effect:** re-serializing normalizes whitespace and escaping of other keys
(`{"foo": 1}` → `{"foo":1}` — value-preserving, byte-different). Use
`JavaScriptEncoder.UnsafeRelaxedJsonEscaping` so non-ASCII in other keys stays readable. Only
happens when the value genuinely changes, so no extra dirty churn.

## Panel UI & App wiring

**Layout** (`MainWindow.axaml`, inside the existing `SpawnProperties` card below the NPC picker):

```
NPC
[ SearchPicker…            ▼ ]
Can move
[◪] CheckBox (IsThreeState)   ⚠ warning TextBlock (muted, wraps)
```

`CheckBox x:Name="SpawnCanMoveCheck" IsThreeState="True"` plus a collapsed-by-default muted
`TextBlock` for the warning: "Invalid properties value — edit in the sheet to fix." The row is
disabled, not hidden, so the panel doesn't jump between selections.

**State** (`src/MapEditor.App/ViewModels/DocumentGameDataState.cs`): pending
`SpawnMoveOverride? SelectedCanMove`, mirroring `SelectedNpcId`'s nullable convention (`:127`);
`null` is treated as `Default` when stamping new spawns.

**Write path** (mirrors `CommitSpawnNpc`, `MapDocumentViewModel.cs:1045`): code-behind
`OnSpawnCanMoveCheckChanged` (next to `OnSpawnNpcPickerChanged`, `MainWindow.axaml.cs:950`) →
`MapDocumentViewModel.CommitSpawnCanMove(SpawnMoveOverride)`:

1. Sets `state.SelectedCanMove` (pending value).
2. If a spawn is selected (`state.SelectedSpawn`) and `TryWrite` succeeds:
   `session.Edits.UpdateSpawn(index, row with { Properties = result })`. A `false` write is a
   no-op. Undo/redo, dirty tracking and the push plan come free from `SheetEditSession`.

**Read path** (`SyncSpawnProperties`, `MainWindow.axaml.cs:1600`): with a selection, `TryRead` the
row's cell and set the checkbox (☑/☐/◪), or disable it and show the warning when not editable.
With no selection, show the pending value, enabled — even if the last-viewed row was broken.

**Sync guard:** programmatic checkbox assignment fires the change handler. For editable cells the
resulting commit is the desired "selection refreshes the pending value" behavior — exactly how the
picker behaves — but for a non-editable cell it would stamp a meaningless pending value. Use a
`_syncingSpawnPanel` guard flag around the assignment in `SyncSpawnProperties`; the handler returns
early while it is set.

**Placement** (`AddSpawnAt`, `MapDocumentViewModel.cs:962`): stamp
`TryWrite("", SelectedCanMove ?? Default)` into the new row. A placed spawn auto-selects, so the
two-click path (place, toggle) also works.

**Paste keeps clipboard fidelity:** a pasted spawn carries the source row's properties (already the
behavior via `EditorClipboardPayload.SpawnProperties`, `:1199`), not the pending value.

**Undo/redo refresh:** the checkbox reuses the picker's existing sync trigger path, so refresh
behavior is identical to the NPC picker's — no new mechanism. Any picker-side quirk there is
inherited, not fixed in this cut (deferred).

## Numeric cells on append

`GoogleBatchBuilder` writes every appended cell as `ExtendedValue { StringValue = cell }`
(`src/MapEditor.GameData.Google/Sheets/GoogleBatchBuilder.cs:63`), so editor-appended rows land as
text-formatted numbers (left-aligned, warning triangles, bad sorting). Rows authored in the sheet
are already numeric — the editor's rows are the anomaly, and the server importer already consumes
numeric cells.

**Fix:** pass the schema's column kinds into `Build` (`GoogleSheetsGateway` already holds the
schema and calls `Build` at `GoogleSheetsGateway.cs:280`). For columns whose `Kind` is `Id` or
`Int`, write `NumberValue` when the cell text parses as an integer; otherwise fall back to
`StringValue`. The only appended sheets are `NPC Spawns` (`Id, Id, Int, Int, Text`) and `Warptiles`
(six `Id`/`Int`), so `properties` stays text — JSON never parses as a number.

**Safety:** the editor's reads use `UNFORMATTEDVALUE` (`GoogleSheetsGateway.cs:58`), so numeric
cells come back as plain `"7"` and the pull→push round trip is unchanged. No read-path or
row-model change.

## Validation

Automated (all in this repo):

- New `tests/MapEditor.GameData.Tests/Rows/SpawnPropertiesJsonTests.cs`:
  - Read: blank/whitespace → `Default`; `{}` and object-without-`canMove` → `Default`;
    `true`/`false` → `Movable`/`Stationary`; malformed, array root, and `{"canMove":1}` → not
    editable.
  - Write: `Default` on `{"canMove":true,"foo":1}` → `{"foo":1}`; setting on a cell with other
    keys appends `canMove` last and keeps order; removal-to-empty → `""`; write refuses
    non-editable input; `TryWrite("")` → `TryRead` round-trips all three states.
- Extended `tests/MapEditor.App.Tests/GameDataPropertiesTests.cs` (drives the panel headless):
  - Selecting spawns with `""` / `{"canMove":true}` / `{"canMove":false}` shows ◪/☑/☐.
  - Selecting a malformed cell disables the checkbox and shows the warning.
  - Toggling with a selection updates `session.Edits.Spawns[i].Properties`; one undo restores it.
  - Toggling with no selection stamps the next `AddSpawnAt` row; paste still carries the source
    row's properties.
- `tests/MapEditor.GameData.Google.Tests/Sheets/GoogleBatchBuilderTests.cs`: numeric-kind cells
  assert `NumberValue`; `properties` and unparseable-numeric fallback assert `StringValue`;
  gateway request-payload assertions updated.
- Tests that enumerate the `SpawnProperties` panel's children (`ShortcutTests.cs:470` and
  neighbors) may need one-line updates.
- Gate: `dotnet test Goose2ClientGodot.sln`. Baseline at this design: 3,678 passed, 0 failed
  (2026-10-02).

Manual smoke (needs credentials + server restart; record the outcome, don't claim):

1. Select a stationary mob's spawn, set ☑, push → sheet `NPC Spawns` E reads `{"canMove":true}`
   **and** A–D are numeric cells (right-aligned, no warning triangle).
2. Restart the server → the mob wanders and returns within 10 tiles of its spawn point.
3. Set ◪, push → E is blank; ☐ → `{"canMove":false}`.
4. Place a new spawn with ☑ pending → its E is `{"canMove":true}` and its ids are numeric.

## Accepted risks and deferred work

- **Re-serialization normalizes formatting** of other keys when the value changes. Accepted.
- **No JSON validation on pull or push** beyond what `TryRead` needs for the checkbox; a broken
  cell on an unselected row still round-trips untouched, as today.
- **Undo/refresh inherits any picker-side sync quirk** (deferred, gap 2 at brainstorm).
- **`{"canMove":1}` on an unselected row is invisible** until selected — same as today; no
  column-wide validation pass was added.
- **Bool/Double/Enum kinds still write as strings.** Only `Id`/`Int` were changed because only the
  two appended sheets matter; if the editor ever appends to other sheets, extend the kind list.
