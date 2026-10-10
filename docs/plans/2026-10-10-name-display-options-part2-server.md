# Name Display Options — Part 2: Server Implementation Plan

**Goal:** Add a `hide_name` field to NPC data and emit it on the MKC spawn packet (immediately after the existing `Invisible` field) so the client can force a character's overhead name off.

**Architecture:** `hide_name` is a boolean NPC-template column (like `stationary`/`see_invisible`), copied through `NPCTemplate` → `NPC` (with a per-spawn `Properties` override), and serialized into the three MKC builders. Players and pets emit `0`; NPCs emit their value. CHP is unchanged.

**Tech Stack:** .NET (Goose server), xUnit (`Goose.Tests`, `Goose.IntegrationTests`).

Design: `docs/plans/2026-10-10-name-display-options-design.md`. Companion to Part 1 (client). **This part edits the sibling repo `../illutiagooseserver`, not the client worktree** — create a worktree there (e.g. `git -C ../illutiagooseserver worktree add .worktrees/feat-name-display -b feat-name-display`) before implementing.

---

## APIs verified

- Descriptor list `CsvToSql/CsvToSql.Core/NpcCsvToSql.cs:7-93`; boolean precedent `Col.Bool("see_invisible", ...)` at `:27`; last descriptor `stuck_message` at `:92` (append after it — cells are positional, see the file's own warning at `:68-72`).
- DDL generated from descriptors: `CsvToSql/CsvToSql.Core/Schema/TableDdl.cs:19`; snapshot gate `Goose.IntegrationTests/CsvToSqlSnapshotTests.cs` (regenerate with `GOOSE_UPDATE_SNAPSHOT=1`).
- `NPCTemplate.CanMove` `Goose/NPCTemplate.cs:165`; clone-ctor copy `this.CanMove = other.CanMove;` `Goose/NPCTemplate.cs:252`.
- `NPCHandler.LoadNPCTemplates` `Goose/NPCHandler.cs:69`; `stationary` read `Goose/NPCHandler.cs:98`.
- `NPC.CanMove` `Goose/NPC.cs:284`; per-instance override `this.CanMove = this.Properties.GetProperty("canMove", template.CanMove);` `Goose/NPC.cs:635`.
- `PropertiesDictionary.GetProperty<T>(string, T)` `Goose/PropertiesDictionary.cs:43`.
- MKC `Invisible` emission lines in `Goose/Packets.cs`: `MakeCharacter` `:144`, `MakeNPCCharacter` `:231`, `MakePetCharacter` `:261`. CHP lines `:178`/`:202`/`:286` are NOT touched.
- Server packet test model `Goose.Tests/InvisibilityPacketTests.cs` — `NewPlayer()` `:23`, `NewPet()` `:77`, `NewNPC()` `:109`; `MakeCharacter_PinsInvisFieldBeforeFaceID` asserts `Contains(",255,0,70,")` at `:65` (this assertion breaks with the new field — see Task 4).
- Spawn-properties load test model `Goose.Tests/NPCSpawnPropertiesLoadTests.cs` (canMove override `:47-53`, template default `:64`).

---

## Task 1: `hide_name` descriptor + snapshot

**Files:**
- Modify: `CsvToSql/CsvToSql.Core/NpcCsvToSql.cs:92` (append descriptor)
- Modify: `Goose.IntegrationTests/generated.snapshot` (regenerate)

**Step 1: Add the descriptor** (append after `stuck_message`, `:92`, so positional reads of earlier columns are unaffected):

```csharp
            Col.Bool("hide_name", def: false).HeaderText("hide name (0)"),
```

**Step 2: Regenerate + review the snapshot**

```
GOOSE_UPDATE_SNAPSHOT=1 dotnet test Goose.IntegrationTests --filter FullyQualifiedName~CsvToSqlSnapshot
```

The diff should add exactly one `hide_name` column (INTEGER, default 0) to `npc_templates` with all fixture rows 0 (the fixed workbook has no such column). Read the diff; if it is only that, commit. Re-run without the env var to confirm it is now green.

**Step 3: Commit** — `git commit -am "feat(npc): add hide_name column to npc_templates schema"`.

---

## Task 2: `NPCTemplate.HideName` + load

**Files:**
- Modify: `Goose/NPCTemplate.cs:165` (property), `:252` (clone)
- Modify: `Goose/NPCHandler.cs:98` (load)

**Step 1: Property + clone**

`NPCTemplate.cs`, next to `CanMove` (`:165`):

```csharp
        public bool HideName { get; set; }
```

In the clone ctor (`NPCTemplate.cs:252`, beside `this.CanMove = other.CanMove;`):

```csharp
            this.HideName = other.HideName;
```

**Step 2: Load from the reader** (`NPCHandler.cs:98`, beside the `stationary` read):

```csharp
                        npc.HideName = reader.GetString("hide_name") == "1";
```

**Step 3: Build** — `dotnet build Goose/Goose.csproj`.

**Step 4: Commit** — `git commit -am "feat(npc): load hide_name into NPCTemplate"`.

---

## Task 3: `NPC.HideName` per-instance + `Properties` override

**Files:**
- Modify: `Goose/NPC.cs:284` (property), `:635` (ctor override)
- Test: `Goose.Tests/NPCSpawnPropertiesLoadTests.cs` (extend)

**Mutation impact:**
- Source of truth: `NPCTemplate.HideName` (from `npc_templates.hide_name`), overridable per spawn via the `Properties` JSON key `hideName` (mirrors `canMove`).
- Readers: `Packets.MakeNPCCharacter` (Task 4).
- Derived/cached state: none.
- Propagation: template → `NPC` at construction (`NPC.cs:635`), then read when the MKC is built for viewers.
- Invariants: a spawn with no `hideName` property inherits the template value; a malformed properties JSON falls back to the template value (same guarantee `canMove` already has).
- Observable proof: a spawn row `{"hideName":true}` yields `npc.HideName == true` even when the template is false, and vice-versa.

**Step 1: Write the failing test** — extend the shared helper and add two cases.

`NPCSpawnPropertiesLoadTests.cs:11` — add an optional template flag (existing one-arg calls keep compiling):

```csharp
    private static TestWorldFixture WorldWithSpawnRow(string? propertiesCell, bool templateHideName = false)
    {
        ...
        fixture.World.NPCHandler.AddTemplate(new NPCTemplate
        {
            NPCTemplateID = 1, Name = "Test NPC", Level = 50, ClassID = 0,
            BaseStats = new AttributeSet(), CanMove = false, HideName = templateHideName,
        });
        ...
    }
```

Then add tests (call `LoadNPCs` and read via `OnlyNpc`, mirroring `:49-52`):

```csharp
    [Fact]
    public void SpawnHideNameProperty_OverridesTemplate()
    {
        using var fixture = WorldWithSpawnRow("{\"hideName\":true}", templateHideName: false);
        fixture.World.NPCHandler.LoadNPCs(fixture.World);
        Assert.True(OnlyNpc(fixture).HideName);
    }

    [Fact]
    public void SpawnWithoutHideNameProperty_InheritsTemplate()
    {
        using var fixture = WorldWithSpawnRow("", templateHideName: true);
        fixture.World.NPCHandler.LoadNPCs(fixture.World);
        Assert.True(OnlyNpc(fixture).HideName);
    }
```

**Step 2: Run to verify it fails (red)** — `dotnet test Goose.Tests --filter NPCSpawnPropertiesLoadTests` → `HideName` undefined on `NPCTemplate`/`NPC`.

**Step 3: Implement**

`NPC.cs`, next to `CanMove` (`:284`):

```csharp
        public bool HideName { get; set; }
```

In the ctor (`NPC.cs:635`, beside the `canMove` override):

```csharp
            this.HideName = this.Properties.GetProperty("hideName", template.HideName);
```

**Step 4: Run to verify it passes (green)** — same filter.

**Step 5: Commit** — `git commit -am "feat(npc): NPC.HideName from template with per-spawn override"`.

---

## Task 4: MKC emission + packet tests

**Files:**
- Modify: `Goose/Packets.cs:144` (`MakeCharacter`), `:231` (`MakeNPCCharacter`), `:261` (`MakePetCharacter`)
- Modify: `Goose.Tests/InvisibilityPacketTests.cs:65` (fix broken assertion)
- Test: `Goose.Tests/HideNamePacketTests.cs` (new)

**Step 1: Write the failing test** — `Goose.Tests/HideNamePacketTests.cs`, reusing the `NewPlayer/NewPet/NewNPC` helper shape from `InvisibilityPacketTests.cs`:

```csharp
using Goose;
using Xunit;

namespace Goose.Tests
{
    public class HideNamePacketTests
    {
        // FaceID is 70 in the helpers; Invisible=0. HideName sits between them: ,<invis>,<hide>,70,
        [Fact]
        public void MakeNPCCharacter_HideName1_PinsFieldAfterInvisible()
        {
            var npc = new NPC { LoginID = 5, Name = "NPC", MaxStats = new AttributeSet { HP = 100 },
                                HairA = 255, FaceID = 70, HideName = true };
            npc.CurrentHP = 100;
            Assert.Contains(",0,1,70,", P.MakeNPCCharacter(npc));   // invis=0, hide=1, face=70
        }

        [Fact]
        public void MakeNPCCharacter_HideName0_PinsFieldAfterInvisible()
        {
            var npc = new NPC { LoginID = 5, Name = "NPC", MaxStats = new AttributeSet { HP = 100 },
                                HairA = 255, FaceID = 70, HideName = false };
            npc.CurrentHP = 100;
            Assert.Contains(",0,0,70,", P.MakeNPCCharacter(npc));
        }

        [Fact]
        public void MakeCharacter_PlayersAlwaysEmitHideName0()
        {
            var p = new Player(0);   // plus the same inventory/class/stats setup as InvisibilityPacketTests.NewPlayer()
            Assert.Contains(",0,0,70,", P.MakeCharacter(p));
        }
    }
}
```

Copy the full `NewPlayer()` body (inventory/class/stats) from `InvisibilityPacketTests.cs:23-40` rather than the abbreviated line above.

**Step 2: Run to verify it fails (red)** — `dotnet test Goose.Tests --filter HideNamePacketTests` → `HideName` undefined on NPC / assertion fails (field not yet emitted).

**Step 3: Implement** — insert the field immediately after the `Invisible` line in the three MKC builders.

`MakeCharacter` (`Packets.cs:144`) and `MakePetCharacter` (`:261`) — players/pets are never hidden:

```csharp
                          (player.IsInvisible ? "1" : "0") + "," + // Invisible
                          "0" + "," + // HideName (players/pets never hidden)
```

`MakeNPCCharacter` (`:231`):

```csharp
                        (npc.IsInvisible ? "1" : "0") + "," + // Invisible
                        (npc.HideName ? "1" : "0") + "," + // HideName
```

CHP builders (`:178`, `:202`, `:286`) are left unchanged — the flag is fixed at spawn.

**Step 4: Fix the invisibility assertion** — `MakeCharacter_PinsInvisFieldBeforeFaceID` (`InvisibilityPacketTests.cs:65`) asserts `Contains(",255,0,70,")` (hairA,Invisible,FaceID). With `HideName=0` now between Invisible and FaceID the player packet is `,255,0,0,70,`; update that assertion to:

```csharp
            Assert.Contains(",255,0,0,70,", packet);
```

The `InvisField = ",0,70,"` constant still matches (`,0,0,70,` contains `,0,70,`), so the other invisibility assertions need no change.

**Step 5: Run to verify green** — `dotnet test Goose.Tests --filter "HideNamePacketTests|InvisibilityPacketTests"`, then the full server suite `dotnet test`.

**Step 6: Commit** — `git commit -am "feat(npc): emit hide_name on MKC after Invisible"`.

---

## Invariant-to-test matrix

| Invariant | Proved by |
|-----------|-----------|
| `hide_name` column exists, defaults 0 | `CsvToSqlSnapshotTests` (regenerated snapshot diff) |
| Template value reaches `NPC` | `SpawnWithoutHideNameProperty_InheritsTemplate` |
| Per-spawn `hideName` property overrides template | `SpawnHideNameProperty_OverridesTemplate` |
| NPC MKC carries HideName right after Invisible | `MakeNPCCharacter_HideName1/0_PinsFieldAfterInvisible` |
| Player/pet MKC emit `0` in that slot | `MakeCharacter_PlayersAlwaysEmitHideName0` |
| CHP unchanged (no new field) | `InvisibilityPacketTests` CHP cases unchanged and green |

## Notes

- No DB migration: NPC data is regenerated from CSV each run, so the descriptor + snapshot are sufficient. The source Google Sheet "NPCs" tab needs a `hide name (0)` column appended for designers to set it; blank ⇒ default `false`.
- Coordinate deploy with Part 1: the client reads the field positionally right after `Invisible`.
