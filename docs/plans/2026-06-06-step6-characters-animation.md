# Step 6: Characters + Animation Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Pure-logic tasks follow superpowers:test-driven-development. Before claiming any task done, use superpowers:verification-before-completion.

**Goal:** Render and animate world entities (players + monsters) on the live map — a layered paper-doll character that spawns, moves tile-to-tile, faces, attacks, and follows the camera for the local player — driven by the server packet stream.

**Architecture:** Each character is a `Character : Node2D` holding N stacked `AnimatedSprite2D` slots (one per paper-doll layer). Each slot loads a per-graphic `SpriteFrames` `.tres` (already emitted by the converter) and all slots play `{state}-{direction}` in lockstep (the Godot replacement for Unity's `Animator` + `AnimatorOverrideController`). `MapManager` owns the `LoginId → Character` registry and routes character packets to it, replacing the Step-5 camera-bootstrap stubs. The local player (identified by SUC) reads input and moves predictively. All tile↔world math goes through the existing `MapCoords` (no Y-flip). Pure logic (direction strings, anchor math, slot layout, state selection, animation-height lookup) lives in Godot-free classes unit-tested in `tests/Goose2Client.Tests`; node assembly is verified by live smoke against `scyther.local:2006`.

**Tech Stack:** Godot 4.6 (C#/.NET), `AnimatedSprite2D` + `SpriteFrames`, xUnit (`tests/Goose2Client.Tests`), live server `scyther.local:2006` (`GOOSE_HOST`/`GOOSE_PORT`).

---

## APIs verified (path:line)

Existing project APIs this plan calls — all confirmed present:

- `GameManager.Instance` — `Scripts/GameManager.cs:11`; `GameManager.PacketManager` — `:14`; `GameManager.CurrentMap` (`MapFile`) — `:28`.
- `PacketManager.Listen<T>(Action<object>)` / `Remove<T>(...)` — `Scripts/Network/PacketManager.cs:12`,`:24`.
- `MapManager` (Node2D, scene root) — `Scripts/MapManager.cs:10`; existing listeners + `_charSpawns`/`_myLoginId` camera stubs to replace — `:40-48`, `:75-95`.
- `MapCoords.TileSize` (=32) — `Scripts/Map/MapCoords.cs:10`; `TileCenter(x,y)` — `:13`; `TileBottomCenter(x,y)` — `:18`; `WorldToTile` — `:22`.
- `MapItem.Setup(AtlasTexture, x, y, Color)` (bottom-center + `Modulate` tint pattern to mirror) — `Scripts/Map/MapItem.cs:9-17`.
- `Direction` enum: `Up=0, Right=1, Down=2, Left=3` — `Scripts/Direction.cs:3-9`.
- `MakeCharacterPacket` (MKC) fields: `LoginId, CharacterType, Name, MapX, MapY, Facing, HPPercent, BodyId, BodyR/G/B/A, BodyState, HairId, DisplayedEquipment (int[7][5]), HairR/G/B/A, FaceId, MoveSpeed` — `Scripts/Network/Packets/MakeCharacterPacket.cs:8-33`. `MapX/MapY` already `-1` (0-indexed) and `Facing` already `-1` — `:47-49`. Equipment indices: `[0]=Chest, [1]=Head(helm), [2]=Legs, [3]=Feet, [4]=Shield, [5]=Weapon, [6]=Mount`, each `[graphicId, r, g, b, a]` — `:97-100`,`:109-133`. `BodyState` forced to `3` when no shield+no weapon — `:76-77`.
- `MoveCharacterPacket` (MOC): `LoginId, MapX, MapY` — `Scripts/Network/Packets/MoveCharacterPacket.cs:8-12` (Prefix `:14`).
- `ChangeHeadingPacket` (CHH): `LoginId, Direction` — `Scripts/Network/Packets/ChangeHeadingPacket.cs:8-10`.
- `SetYourCharacterPacket` (SUC): `LoginId` — `Scripts/Network/Packets/SetYourCharacterPacket.cs:8`.
- `SetYourPositionPacket` (SUP): `MapX, MapY` — `Scripts/Network/Packets/SetYourPositionPacket.cs:8-10`.
- `EraseCharacterPacket` (ERC): `LoginId` — `Scripts/Network/Packets/EraseCharacterPacket.cs:8`.
- `UpdateCharacterPacket` (CHP): same shape as MKC — `Scripts/Network/Packets/UpdateCharacterPacket.cs:8-23`.
- `AttackPacket` (ATT): `LoginId` — `Scripts/Network/Packets/AttackPacket.cs:8`.
- `NetworkClient.Move(Direction)` / `Face(Direction)` / `Attack()` — `Scripts/Network/NetworkClient.cs:155`,`:160`,`:165`.
- Converter-emitted assets on disk: `Assets/Sprites/{Bodies,Hair,Eyes,Chest,Helms,Legs,Feet}/{id}/animations.tres` with clip names `idle-down`, `walk-left`, `attack-up`, `cast-right` (generic aliases) confirmed in `Assets/Sprites/Bodies/1/animations.tres`.
- Animation metadata: `Assets/Resources/AnimationHeights.txt` (`{Type}-{Id}-{clip},{height}`) and `AnimationToFirstFrame.txt` present.
- Test project: `tests/Goose2Client.Tests/Goose2Client.Tests.csproj` (xUnit; existing `MapFileTests`, `MapCoordsTests`, `PauseQueueTests`, `SpriteManifestTests`).

**Unity slot/sort rules** (verified, reproduce exactly): slot→type/id wiring (Body/Hair/Eyes/Chest/Helm/Legs/Feet, Shield+Weapon→`"Hand"`, Mount→`"Body"`) — `~/code/Goose2Client/Assets/Scripts/Character/Character.cs:61-83`; `mounted`/`equipped` flags (mount slot forced to BodyState 3, others `Mounted=true`, `Equipped = BodyState==3 ? 0 : 1`) — `:88-105`; `GetSortOrder(slot, direction)` (base `(int)slot+2`; Shield Right/Up→0; Weapon Right/Down→base, Up→1, Left→0) — `:294-317`; `SetFacing` re-applies Shield/Weapon sort — `:319-331`. `AnimationSlot` order: `Mount=0, Body=1, Face=2, Feet=3, Legs=4, Chest=5, Hair=6, Head=7, Shield=8, Weapon=9`.

**Reference template** (mirror its conventions, do not import): `~/code/3dMMO-Server/client/Assets/Scripts/Entity/Character.cs` — runtime `SpriteFrames` swap (`SetBody` ~`:152`), `{state}-{direction}` `Play` (~`:403`), attack-lock timer `duration = frameCount/fps` (~`:422-447`), `animated.Offset = (0, footOffsetY)` (~`:169`).

**Unity source of truth** (behavior to reproduce): `~/code/Goose2Client/Assets/Scripts/Character/{Character,CharacterAnimation,PlayerController}.cs`, `AnimationManager.cs`.

---

## Scope (MVP vs deferred)

**In scope (this plan):** all 10 paper-doll layers — **Mount, Body, Eyes(=Face), Feet, Legs, Chest, Hair, Helm, Shield, Weapon**. Slot→asset mapping (verified `Character.cs:61-83`): Mount→`Bodies/{equip[6][0]}`, Body→`Bodies/{BodyId}`, Eyes→`Eyes/{FaceId}`, Feet→`Feet/{equip[3][0]}`, Legs→`Legs/{equip[2][0]}`, Chest→`Chest/{equip[0][0]}`, Hair→`Hair/{HairId}`, Helm→`Helms/{equip[1][0]}`, Shield→`Hands/{equip[4][0]}`, Weapon→`Hands/{equip[5][0]}`. (Weapons & shields share the `Hands` folder; mounts are just bodies in `Bodies` — all assets already on disk.) Plus: spawn (MKC), despawn (ERC), move (MOC) + predictive local move, facing (CHH) with per-direction Shield/Weapon z-reordering, appearance update (CHP), attack trigger (ATT) with attack-lock, idle/walk/attack/cast animation via generic clip aliases (mounted slots use `mounted-walk`/`mounted-idle`), per-slot tint + anchor, local-player camera follow + input, name label, HP bar.

**Deferred (documented follow-ups, see final task):**
- **equip vs no-equip & weapon-specific attack clips** (`attack-1hand`/`-staff`/`-bow`/`-2hand`, `walk-equip`) — MVP uses the generic `idle/walk/attack/cast` aliases. Wire the detailed states later off `BodyState`/weapon type (the `Equipped`/`BodyState` flags at `Character.cs:88-105`).
- **Overlays:** chat bubble, floating battle text, spell animation (SPP), emote animation (EMOT) — Step 8 polish.
- **Occupancy check** in `IsValidMove` (don't walk into an occupied tile) — needs the registry as source of truth.
- **Exact tint shader** — MVP uses `SelfModulate` (decision 2026-06-06); revisit only on visible mismatch.

---

## Phase 0 — Prerequisites

### Task 0a: Add directional movement input actions

The ported `Move` action lumps W/A/S/D + arrows into ONE action (`project.godot` `[input] Move`), so direction can't be read from it. Add four discrete actions.

**Files:**
- Modify: `project.godot` (the `[input]` section)

**Step 1: Add the actions**

Add these four actions to the `[input]` section of `project.godot` (keep the existing `Move` action — it stays unused by gameplay but other code/UI may reference it). Use physical keycodes: W=`87`, A=`65`, S=`83`, D=`68`, Up=`4194320`, Left=`4194319`, Down=`4194322`, Right=`4194321`.

```ini
MoveUp={
"deadzone": 0.5,
"events": [Object(InputEventKey,"physical_keycode":87,"script":null)
, Object(InputEventKey,"physical_keycode":4194320,"script":null)
]
}
MoveDown={
"deadzone": 0.5,
"events": [Object(InputEventKey,"physical_keycode":83,"script":null)
, Object(InputEventKey,"physical_keycode":4194322,"script":null)
]
}
MoveLeft={
"deadzone": 0.5,
"events": [Object(InputEventKey,"physical_keycode":65,"script":null)
, Object(InputEventKey,"physical_keycode":4194319,"script":null)
]
}
MoveRight={
"deadzone": 0.5,
"events": [Object(InputEventKey,"physical_keycode":68,"script":null)
, Object(InputEventKey,"physical_keycode":4194321,"script":null)
]
}
```

> Note: Godot rewrites `InputEventKey` objects with their full property set on first save from the editor; the abbreviated form above is accepted on load. If the editor is open, prefer adding these via **Project Settings → Input Map** to avoid a churn diff.

**Step 2: Verify the project still loads**

Run: `godot --headless --path . --quit-after 2 2>&1 | grep -i "error\|MoveUp" || echo "loaded clean"`
Expected: no parse errors; project opens.

**Step 3: Commit**

```bash
git add project.godot
git commit -m "feat(input): add MoveUp/Down/Left/Right actions for 4-dir movement"
```

---

### Task 0b: Add a `Characters` container to the Map scene

Characters need a y-sorted container in the same depth band as dropped items (`Objects` is `z_index=35`), so they render above ground layers (z 0–30) and below the roof (z 40), and y-sort against each other. **All of a character's slot sprites stay at this single z (35);** intra-character paper-doll layering is done by *child order* (not per-slot z_index), which keeps the whole character inside the 30–40 band — never poking through the roof — while still allowing the per-direction Shield/Weapon reordering. Name label / HP bar deliberately use a higher z_index so they float on top (names over roofs is intended).

**Files:**
- Modify: `Scenes/Map.tscn:13-15`

**Step 1: Add the node**

After the `Objects` node in `Scenes/Map.tscn`, add:

```ini
[node name="Characters" type="Node2D" parent="."]
z_index = 35
y_sort_enabled = true
```

**Step 2: Verify scene loads**

Run: `godot --headless --path . --quit-after 2 2>&1 | grep -i error || echo "loaded clean"`
Expected: no errors.

**Step 3: Commit**

```bash
git add Scenes/Map.tscn
git commit -m "feat(map): add y-sorted Characters container to Map scene"
```

---

## Phase 1 — Pure logic (TDD, Godot-free)

All classes in this phase MUST NOT `using Godot` so the xUnit project can reference them headlessly (same constraint that lets `MapFile`/`PausablePacketQueue` be tested). Put them under `Scripts/Character/` in namespace `Goose2Client.Character`.

### Task 1: Direction → animation-direction string

The converter's clip names use lowercase `up/right/down/left`. Map the `Direction` enum to that string.

**Files:**
- Create: `Scripts/Character/AnimationNames.cs`
- Test: `tests/Goose2Client.Tests/AnimationNamesTests.cs`

**Step 1: Write the failing test**

```csharp
using Goose2Client;
using Goose2Client.Character;
using Xunit;

public class AnimationNamesTests
{
    [Theory]
    [InlineData(Direction.Up, "up")]
    [InlineData(Direction.Right, "right")]
    [InlineData(Direction.Down, "down")]
    [InlineData(Direction.Left, "left")]
    public void DirectionString_maps_each_direction(Direction d, string expected)
        => Assert.Equal(expected, AnimationNames.DirectionString(d));

    [Fact]
    public void Clip_combines_state_and_direction()
        => Assert.Equal("walk-down", AnimationNames.Clip("walk", Direction.Down));
}
```

**Step 2: Run to verify it fails**

Run: `dotnet test tests/Goose2Client.Tests --filter AnimationNamesTests`
Expected: FAIL (type `AnimationNames` does not exist).

**Step 3: Implement**

```csharp
namespace Goose2Client.Character
{
    public static class AnimationNames
    {
        public static string DirectionString(Direction d) => d switch
        {
            Direction.Up => "up",
            Direction.Right => "right",
            Direction.Down => "down",
            Direction.Left => "left",
            _ => "down",
        };

        public static string Clip(string state, Direction d) => $"{state}-{DirectionString(d)}";
    }
}
```

**Step 4: Run to verify it passes**

Run: `dotnet test tests/Goose2Client.Tests --filter AnimationNamesTests`
Expected: PASS.

**Step 5: Commit**

```bash
git add Scripts/Character/AnimationNames.cs tests/Goose2Client.Tests/AnimationNamesTests.cs
git commit -m "feat(character): direction->clip name mapping (TDD)"
```

---

### Task 2: AnimationManager port (animation heights)

Port Unity's `AnimationManager.GetHeight` (returns 64 when missing). Source file: `Assets/Resources/AnimationHeights.txt`, lines `{Type}-{Id}-{clip},{height}`. Keep Godot-free — read via `System.IO` from an injected path so it's testable; the Godot wrapper will pass the `res://`-resolved path.

**Files:**
- Create: `Scripts/Character/AnimationHeights.cs`
- Test: `tests/Goose2Client.Tests/AnimationHeightsTests.cs`

**Step 1: Write the failing test**

```csharp
using System.IO;
using Goose2Client.Character;
using Xunit;

public class AnimationHeightsTests
{
    private static AnimationHeights FromLines(params string[] lines)
    {
        var path = Path.GetTempFileName();
        File.WriteAllLines(path, lines);
        return AnimationHeights.Load(path);
    }

    [Fact]
    public void GetHeight_returns_parsed_value()
    {
        var h = FromLines("Body-1-walk-down,48", "Helm-12-idle-up,72");
        Assert.Equal(48, h.GetHeight("Body-1-walk-down"));
        Assert.Equal(72, h.GetHeight("Helm-12-idle-up"));
    }

    [Fact]
    public void GetHeight_defaults_to_64_when_missing()
        => Assert.Equal(64, FromLines("Body-1-walk-down,48").GetHeight("Nope-9-idle-down"));
}
```

**Step 2: Run to verify it fails**

Run: `dotnet test tests/Goose2Client.Tests --filter AnimationHeightsTests`
Expected: FAIL (no `AnimationHeights`).

**Step 3: Implement**

```csharp
using System.Collections.Generic;
using System.IO;

namespace Goose2Client.Character
{
    /// <summary>Port of Unity AnimationManager height lookup. Maps "{Type}-{Id}-{clip}" -> max
    /// frame height in px; defaults to 64 when absent (Unity GetHeight default).</summary>
    public class AnimationHeights
    {
        private readonly Dictionary<string, int> _heights;

        private AnimationHeights(Dictionary<string, int> heights) => _heights = heights;

        public static AnimationHeights Load(string path)
        {
            var dict = new Dictionary<string, int>();
            foreach (var line in File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                int comma = line.LastIndexOf(',');
                if (comma <= 0) continue;
                if (int.TryParse(line[(comma + 1)..], out int h))
                    dict[line[..comma]] = h;
            }
            return new AnimationHeights(dict);
        }

        public int GetHeight(string name) => _heights.TryGetValue(name, out int h) ? h : 64;
    }
}
```

**Step 4: Run to verify it passes**

Run: `dotnet test tests/Goose2Client.Tests --filter AnimationHeightsTests`
Expected: PASS.

**Step 5: Commit**

```bash
git add Scripts/Character/AnimationHeights.cs tests/Goose2Client.Tests/AnimationHeightsTests.cs
git commit -m "feat(character): port AnimationManager height lookup (TDD)"
```

---

### Task 3: Slot anchor offset from height

Port Unity `CharacterAnimation.SetPosition`: `yOffset = -max((height-48)/2, 0) - 16`. Unity then divided by 32 to reach world units; Godot works in pixels, so the pixel offset IS `yOffset`. This pushes taller sprites up so feet stay on the tile.

**Files:**
- Create: `Scripts/Character/CharacterAnchor.cs`
- Test: `tests/Goose2Client.Tests/CharacterAnchorTests.cs`

**Step 1: Write the failing test**

```csharp
using Goose2Client.Character;
using Xunit;

public class CharacterAnchorTests
{
    [Theory]
    [InlineData(48, -16)]   // baseline body height: just the -16 base
    [InlineData(64, -24)]   // -((64-48)/2) - 16 = -8 - 16
    [InlineData(96, -40)]   // -((96-48)/2) - 16 = -24 - 16
    [InlineData(32, -16)]   // shorter than 48 clamps the first term to 0
    public void OffsetY_matches_unity_formula(int height, int expected)
        => Assert.Equal(expected, CharacterAnchor.OffsetY(height));
}
```

**Step 2: Run to verify it fails**

Run: `dotnet test tests/Goose2Client.Tests --filter CharacterAnchorTests`
Expected: FAIL.

**Step 3: Implement**

```csharp
using System;

namespace Goose2Client.Character
{
    public static class CharacterAnchor
    {
        /// <summary>Vertical pixel offset for a slot sprite of the given frame height, so the
        /// feet line up at the character's tile-bottom origin (Unity CharacterAnimation.SetPosition).</summary>
        public static int OffsetY(int height) => -Math.Max((height - 48) / 2, 0) - 16;
    }
}
```

**Step 4: Run to verify it passes**

Run: `dotnet test tests/Goose2Client.Tests --filter CharacterAnchorTests`
Expected: PASS.

**Step 5: Commit**

```bash
git add Scripts/Character/CharacterAnchor.cs tests/Goose2Client.Tests/CharacterAnchorTests.cs
git commit -m "feat(character): port slot anchor offset formula (TDD)"
```

---

### Task 4: Character layout (slots, type folders, per-direction sort order, underwear)

Defines the full 10-slot paper-doll: which slots exist, which `Assets/Sprites/<folder>/<id>/animations.tres` each maps to, the **per-direction draw order** (`GetSortOrder`), and the underwear defaults. Pure data + the sort/underwear rules. The `CharacterSlot` enum matches Unity's `AnimationSlot` order so `(int)slot + 2` is the base sort, exactly as the source.

**Files:**
- Create: `Scripts/Character/CharacterLayout.cs`
- Test: `tests/Goose2Client.Tests/CharacterLayoutTests.cs`

**Step 1: Write the failing test**

```csharp
using Goose2Client;
using Goose2Client.Character;
using Xunit;

public class CharacterLayoutTests
{
    [Theory] // base sort = (int)slot + 2 for non-shield/weapon slots, direction-independent
    [InlineData(CharacterSlot.Mount, 2)]
    [InlineData(CharacterSlot.Body, 3)]
    [InlineData(CharacterSlot.Eyes, 4)]
    [InlineData(CharacterSlot.Feet, 5)]
    [InlineData(CharacterSlot.Legs, 6)]
    [InlineData(CharacterSlot.Chest, 7)]
    [InlineData(CharacterSlot.Hair, 8)]
    [InlineData(CharacterSlot.Helm, 9)]
    public void SortOrder_base_is_slot_plus_2(CharacterSlot slot, int expected)
        => Assert.Equal(expected, CharacterLayout.SortOrder(slot, Direction.Down));

    [Theory] // Shield: Right/Up -> 0 (behind), Down/Left -> base 10 (in front)
    [InlineData(Direction.Right, 0)]
    [InlineData(Direction.Up, 0)]
    [InlineData(Direction.Down, 10)]
    [InlineData(Direction.Left, 10)]
    public void SortOrder_shield_is_direction_dependent(Direction d, int expected)
        => Assert.Equal(expected, CharacterLayout.SortOrder(CharacterSlot.Shield, d));

    [Theory] // Weapon: Right/Down -> base 11, Up -> 1, Left -> 0
    [InlineData(Direction.Right, 11)]
    [InlineData(Direction.Down, 11)]
    [InlineData(Direction.Up, 1)]
    [InlineData(Direction.Left, 0)]
    public void SortOrder_weapon_is_direction_dependent(Direction d, int expected)
        => Assert.Equal(expected, CharacterLayout.SortOrder(CharacterSlot.Weapon, d));

    [Theory]
    [InlineData(CharacterSlot.Body, "Bodies")]
    [InlineData(CharacterSlot.Mount, "Bodies")]   // mounts are just bodies
    [InlineData(CharacterSlot.Hair, "Hair")]
    [InlineData(CharacterSlot.Eyes, "Eyes")]
    [InlineData(CharacterSlot.Chest, "Chest")]
    [InlineData(CharacterSlot.Helm, "Helms")]
    [InlineData(CharacterSlot.Legs, "Legs")]
    [InlineData(CharacterSlot.Feet, "Feet")]
    [InlineData(CharacterSlot.Shield, "Hands")]   // shields & weapons share the Hands folder
    [InlineData(CharacterSlot.Weapon, "Hands")]
    public void TypeFolder_matches_converter_output(CharacterSlot slot, string folder)
        => Assert.Equal(folder, CharacterLayout.TypeFolder(slot));

    [Fact]
    public void Underwear_gives_male_default_legs_when_empty()
    {
        Assert.Equal(3, CharacterLayout.UnderwearLegs(bodyId: 1, equippedLegsId: 0));
        Assert.Equal(0, CharacterLayout.UnderwearLegs(bodyId: 1, equippedLegsId: 42)); // keep equipped
    }

    [Fact]
    public void Underwear_gives_female_default_chest_when_empty()
        => Assert.Equal(8, CharacterLayout.UnderwearChest(bodyId: 11, equippedChestId: 0));

    [Fact]
    public void Underwear_falls_back_to_legs_4_for_other_bodies()
        => Assert.Equal(4, CharacterLayout.UnderwearLegs(bodyId: 99, equippedLegsId: 0));
}
```

**Step 2: Run to verify it fails**

Run: `dotnet test tests/Goose2Client.Tests --filter CharacterLayoutTests`
Expected: FAIL.

**Step 3: Implement**

```csharp
namespace Goose2Client.Character
{
    // Order MATCHES Unity AnimationSlot so (int)slot + 2 is the base draw order.
    public enum CharacterSlot { Mount = 0, Body, Eyes, Feet, Legs, Chest, Hair, Helm, Shield, Weapon }

    public static class CharacterLayout
    {
        // All 10 slots, back-to-front by base order, for iteration when (re)building a character.
        public static readonly CharacterSlot[] All =
        {
            CharacterSlot.Mount, CharacterSlot.Body, CharacterSlot.Eyes, CharacterSlot.Feet,
            CharacterSlot.Legs, CharacterSlot.Chest, CharacterSlot.Hair, CharacterSlot.Helm,
            CharacterSlot.Shield, CharacterSlot.Weapon,
        };

        /// <summary>Per-direction draw order (Unity Character.GetSortOrder). Base = (int)slot + 2;
        /// Shield/Weapon flip in front of / behind the body depending on facing. Higher = nearer.</summary>
        public static int SortOrder(CharacterSlot slot, Direction direction)
        {
            int order = (int)slot + 2;
            if (slot < CharacterSlot.Shield) return order;

            if (slot == CharacterSlot.Shield)
                return direction is Direction.Right or Direction.Up ? 0 : order;

            // Weapon
            return direction switch
            {
                Direction.Right => order,
                Direction.Down => order,
                Direction.Up => 1,
                Direction.Left => 0,
                _ => order,
            };
        }

        public static string TypeFolder(CharacterSlot slot) => slot switch
        {
            CharacterSlot.Body   => "Bodies",
            CharacterSlot.Mount  => "Bodies",   // mounts are just other bodies
            CharacterSlot.Hair   => "Hair",
            CharacterSlot.Eyes   => "Eyes",
            CharacterSlot.Chest  => "Chest",
            CharacterSlot.Helm   => "Helms",
            CharacterSlot.Legs   => "Legs",
            CharacterSlot.Feet   => "Feet",
            CharacterSlot.Shield => "Hands",    // shields & weapons render from Hands
            CharacterSlot.Weapon => "Hands",
            _ => "Bodies",
        };

        // Unity Character.SetUnderwear: male body 1 -> legs 3; female body 11 -> chest 8;
        // otherwise default legs 4. Returns 0 to mean "keep whatever is equipped".
        public static int UnderwearLegs(int bodyId, int equippedLegsId)
        {
            if (equippedLegsId != 0) return 0;
            if (bodyId == 1) return 3;
            return 4;
        }

        public static int UnderwearChest(int bodyId, int equippedChestId)
        {
            if (equippedChestId != 0) return 0;
            if (bodyId == 11) return 8;
            return 0;
        }
    }
}
```

**Step 4: Run to verify it passes**

Run: `dotnet test tests/Goose2Client.Tests --filter CharacterLayoutTests`
Expected: PASS.

**Step 5: Commit**

```bash
git add Scripts/Character/CharacterLayout.cs tests/Goose2Client.Tests/CharacterLayoutTests.cs
git commit -m "feat(character): 10-slot paper-doll layout + per-direction sort + underwear (TDD)"
```

---

### Task 5: Animation state + movement speed helpers

Two pure helpers: the state selector (idle/walk/attack/cast precedence) and the movement pixels-per-second from the server `MoveSpeed`.

**Files:**
- Create: `Scripts/Character/CharacterMotion.cs`
- Test: `tests/Goose2Client.Tests/CharacterMotionTests.cs`

**Step 1: Write the failing test**

```csharp
using Goose2Client.Character;
using Xunit;

public class CharacterMotionTests
{
    [Fact]
    public void State_attack_overrides_moving()
        => Assert.Equal("attack", CharacterMotion.State(isMoving: true, attackLocked: true, isMounted: false));

    [Fact]
    public void State_walk_when_moving_and_not_locked()
        => Assert.Equal("walk", CharacterMotion.State(isMoving: true, attackLocked: false, isMounted: false));

    [Fact]
    public void State_idle_when_still()
        => Assert.Equal("idle", CharacterMotion.State(isMoving: false, attackLocked: false, isMounted: false));

    [Fact] // a mounted rider uses the mounted-* clips for walk/idle (the mount slot itself passes isMounted:false)
    public void State_mounted_walk_and_idle()
    {
        Assert.Equal("mounted-walk", CharacterMotion.State(isMoving: true, attackLocked: false, isMounted: true));
        Assert.Equal("mounted-idle", CharacterMotion.State(isMoving: false, attackLocked: false, isMounted: true));
    }

    [Fact] // attack still wins even when mounted
    public void State_attack_overrides_mounted()
        => Assert.Equal("attack", CharacterMotion.State(isMoving: true, attackLocked: true, isMounted: true));

    [Fact]
    public void PixelsPerSecond_scales_inversely_with_moveSpeed()
    {
        // Unity: speed = 1000 / MoveSpeed (world units = tiles); px = units * 32.
        Assert.Equal(32f * (1000f / 250f), CharacterMotion.PixelsPerSecond(250), 3);
    }

    [Fact]
    public void PixelsPerSecond_guards_zero_movespeed()
        => Assert.True(CharacterMotion.PixelsPerSecond(0) > 0);
}
```

**Step 2: Run to verify it fails**

Run: `dotnet test tests/Goose2Client.Tests --filter CharacterMotionTests`
Expected: FAIL.

**Step 3: Implement**

```csharp
namespace Goose2Client.Character
{
    public static class CharacterMotion
    {
        public static string State(bool isMoving, bool attackLocked, bool isMounted)
        {
            if (attackLocked) return "attack";
            if (isMounted) return isMoving ? "mounted-walk" : "mounted-idle";
            return isMoving ? "walk" : "idle";
        }

        /// <summary>Tile-to-tile travel speed in px/s. Unity used MoveTowards at 1000/MoveSpeed
        /// world units/s (1 unit = 1 tile = 32 px).</summary>
        public static float PixelsPerSecond(int moveSpeed)
        {
            int safe = moveSpeed <= 0 ? 250 : moveSpeed;   // guard against div-by-zero / bad data
            return 32f * (1000f / safe);
        }
    }
}
```

**Step 4: Run to verify it passes**

Run: `dotnet test tests/Goose2Client.Tests --filter CharacterMotionTests`
Expected: PASS.

**Step 5: Commit**

```bash
git add Scripts/Character/CharacterMotion.cs tests/Goose2Client.Tests/CharacterMotionTests.cs
git commit -m "feat(character): animation-state + movement-speed helpers (TDD)"
```

---

## Phase 2 — Character node + animation (Godot integration)

These tasks build the visual node. They can't be headless-unit-tested meaningfully, so each ends with a build check; cumulative visual behavior is smoke-verified in Phase 3 against the live server.

### Task 6: `Character` node — appearance assembly

A `Character : Node2D` that builds one `AnimatedSprite2D` per slot, loads each slot's `SpriteFrames` by graphic id, applies tint + anchor + z-order, and exposes the data the registry needs. No movement/animation playback yet beyond a static idle pose.

**Files:**
- Create: `Scripts/Character/Character.cs`
- Reference: `~/code/3dMMO-Server/client/Assets/Scripts/Entity/Character.cs:152-175` (SetBody pattern)

**Step 1: Implement the node**

```csharp
using System.Collections.Generic;
using Godot;
using Goose2Client.Network.Packets;

namespace Goose2Client.Character
{
    public partial class Character : Node2D
    {
        public int LoginId { get; private set; }
        public string CharacterName { get; private set; }
        public int X { get; private set; }
        public int Y { get; private set; }
        public Direction Facing { get; private set; } = Direction.Down;
        public int MoveSpeed { get; private set; } = 250;
        public bool IsMounted { get; private set; }
        public bool IsLocalPlayer { get; set; }

        // Per-slot live sprite + the graphic id it was built from (needed for the height lookup).
        private sealed class Slot { public AnimatedSprite2D Sprite; public int GraphicId; }
        private readonly Dictionary<CharacterSlot, Slot> _slots = new();
        private static AnimationHeights _heights;

        // The converter's height-prefix uses its AnimationType name, which differs from the
        // asset folder for Mount/Shield/Weapon (those reuse Body/Hand art). Map slot -> prefix.
        private static string HeightPrefix(CharacterSlot slot) => slot switch
        {
            CharacterSlot.Mount or CharacterSlot.Body => "Body",
            CharacterSlot.Eyes => "Eyes",
            CharacterSlot.Feet => "Feet",
            CharacterSlot.Legs => "Legs",
            CharacterSlot.Chest => "Chest",
            CharacterSlot.Hair => "Hair",
            CharacterSlot.Helm => "Helm",
            CharacterSlot.Shield or CharacterSlot.Weapon => "Hand",
            _ => "Body",
        };

        public override void _Ready()
        {
            _heights ??= AnimationHeights.Load(
                ProjectSettings.GlobalizePath("res://Assets/Resources/AnimationHeights.txt"));
        }

        /// <summary>(Re)build every slot from an MKC/CHP-shaped appearance.</summary>
        public void SetAppearance(MakeCharacterPacket p)
        {
            LoginId = p.LoginId;
            CharacterName = p.Name;
            MoveSpeed = p.MoveSpeed <= 0 ? 250 : p.MoveSpeed;
            X = p.MapX; Y = p.MapY; Facing = p.Facing;

            var eq = p.DisplayedEquipment;
            int chestId = Equip(eq, 0, out var ec);
            int helmId  = Equip(eq, 1, out var eh);
            int legsId  = Equip(eq, 2, out var el);
            int feetId  = Equip(eq, 3, out var ef);
            int shieldId = Equip(eq, 4, out var es);
            int weaponId = Equip(eq, 5, out var ew);
            int mountId  = Equip(eq, 6, out var em);
            IsMounted = mountId != 0;

            // Underwear defaults when slots are empty (Unity SetUnderwear).
            int uwLegs = CharacterLayout.UnderwearLegs(p.BodyId, legsId);
            if (uwLegs != 0) { legsId = uwLegs; el = Colors.White; }
            int uwChest = CharacterLayout.UnderwearChest(p.BodyId, chestId);
            if (uwChest != 0) { chestId = uwChest; ec = Colors.White; }

            ApplySlot(CharacterSlot.Body, p.BodyId, RgbaColor(p.BodyR, p.BodyG, p.BodyB, p.BodyA));
            ApplySlot(CharacterSlot.Hair, p.HairId, RgbaColor(p.HairR, p.HairG, p.HairB, p.HairA));
            ApplySlot(CharacterSlot.Eyes, p.FaceId, Colors.White);
            ApplySlot(CharacterSlot.Chest, chestId, ec);
            ApplySlot(CharacterSlot.Helm, helmId, eh);
            ApplySlot(CharacterSlot.Legs, legsId, el);
            ApplySlot(CharacterSlot.Feet, feetId, ef);
            ApplySlot(CharacterSlot.Shield, shieldId, es);
            ApplySlot(CharacterSlot.Weapon, weaponId, ew);
            ApplySlot(CharacterSlot.Mount, mountId, em);

            TeleportTo(p.MapX, p.MapY);   // sets Position + _targetPosition (Task 7), no walk anim
            ApplyDrawOrder();
            PlayState();
        }

        private static Color RgbaColor(int r, int g, int b, int a)
            => a > 0 ? new Color(r / 255f, g / 255f, b / 255f, a / 255f) : Colors.White;

        private static int Equip(int[][] eq, int i, out Color color)
        {
            color = Colors.White;
            if (eq == null || i >= eq.Length || eq[i] == null || eq[i].Length < 5) return 0;
            if (eq[i][4] > 0) color = new Color(eq[i][1] / 255f, eq[i][2] / 255f, eq[i][3] / 255f, eq[i][4] / 255f);
            return eq[i][0];
        }

        private void ApplySlot(CharacterSlot slot, int graphicId, Color tint)
        {
            if (graphicId <= 0) { RemoveSlot(slot); return; }
            var path = $"res://Assets/Sprites/{CharacterLayout.TypeFolder(slot)}/{graphicId}/animations.tres";
            if (!ResourceLoader.Exists(path)) { RemoveSlot(slot); return; }

            if (!_slots.TryGetValue(slot, out var s))
            {
                s = new Slot { Sprite = new AnimatedSprite2D
                {
                    Name = slot.ToString(),
                    TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
                    // No per-slot z_index: the whole character stays in one z-band (the Characters
                    // container's z=35); intra-character order is child order via ApplyDrawOrder.
                } };
                AddChild(s.Sprite);
                _slots[slot] = s;
            }
            s.GraphicId = graphicId;
            s.Sprite.SpriteFrames = GD.Load<SpriteFrames>(path);
            s.Sprite.SelfModulate = tint;
        }

        private void RemoveSlot(CharacterSlot slot)
        {
            if (_slots.Remove(slot, out var s)) s.Sprite.QueueFree();
        }

        /// <summary>Order the slot sprites back-to-front by SortOrder(slot, Facing) via child order
        /// (Unity used per-direction sortingOrder; we use sibling order to stay inside the z-band).</summary>
        private void ApplyDrawOrder()
        {
            var ordered = new List<KeyValuePair<CharacterSlot, Slot>>(_slots);
            ordered.Sort((a, b) =>
                CharacterLayout.SortOrder(a.Key, Facing).CompareTo(CharacterLayout.SortOrder(b.Key, Facing)));
            for (int i = 0; i < ordered.Count; i++)
                MoveChild(ordered[i].Value.Sprite, i);   // lower SortOrder drawn first (behind)
        }

        public void SetFacing(Direction d) { Facing = d; ApplyDrawOrder(); PlayState(); }

        protected void PlayState()
        {
            if (AttackLocked) return;   // don't clobber a mid-attack animation (Task 8)
            PlayCurrent();
        }

        // Default stubs replaced in Task 7 (IsMoving) and Task 8 (AttackLocked); keep compilable.
        protected bool IsMoving => false;
        protected bool AttackLocked => false;

        /// <summary>Fan the current state out to every slot. The Mount slot itself always plays its
        /// own non-mounted pose (Unity forces the mount to BodyState 3); rider slots use mounted-*.</summary>
        protected void PlayCurrent()
        {
            foreach (var (slot, s) in _slots)
            {
                bool slotMounted = IsMounted && slot != CharacterSlot.Mount;
                string state = CharacterMotion.State(IsMoving, AttackLocked, slotMounted);
                string clip = AnimationNames.Clip(state, Facing);
                var frames = s.Sprite.SpriteFrames;
                if (frames == null || !frames.HasAnimation(clip))
                    clip = AnimationNames.Clip(IsMoving ? "walk" : "idle", Facing);   // fallback to generic
                if (frames == null || !frames.HasAnimation(clip)) continue;

                int h = _heights.GetHeight($"{HeightPrefix(slot)}-{s.GraphicId}-{clip}");
                s.Sprite.Offset = new Vector2(0, CharacterAnchor.OffsetY(h));
                s.Sprite.Play(clip);
            }
        }
    }
}
```

> **Notes for the implementer:**
> - `TeleportTo` and the `IsMoving`/`AttackLocked` members are introduced in Tasks 7–8; this snippet references them so the later tasks only *replace stubs*, not rewrite call sites. If you implement Task 6 standalone first, add a temporary `private void TeleportTo(int x,int y){ X=x;Y=y;Position=MapCoords.TileBottomCenter(x,y); }` and remove it when Task 7 lands.
> - **Height key:** verified format is `{Prefix}-{GraphicId}-{clip}` (e.g. `Body-1-walk-down`). Confirm the exact prefixes with `grep -oE '^[A-Za-z]+-' Assets/Resources/AnimationHeights.txt | sort -u`. A missing key returns 64 (slot still renders) — confirm feet alignment in Task 13 smoke and tune `HeightPrefix` if any layer floats/sinks.
> - **Mount anchoring:** a mount is taller than a body; its rider must sit *on* it. The Unity offset formula (`CharacterAnchor`) is height-driven and should place both correctly, but this is the most likely visual-tuning point — verify in smoke.

**Step 2: Build**

Run: `dotnet build Goose2ClientGodot.csproj`
Expected: build succeeds (0 errors).

**Step 3: Commit**

```bash
git add Scripts/Character/Character.cs
git commit -m "feat(character): Character node with per-slot SpriteFrames assembly"
```

---

### Task 7: Movement + animation playback

Add tile-to-tile movement (`MoveTowards` toward the target world position) and wire `IsMoving` so walk/idle play correctly. Mirrors Unity `Character.Move`/`Update`.

**Files:**
- Modify: `Scripts/Character/Character.cs`

**Step 1: Replace the `IsMoving` stub and add movement**

Replace `protected bool IsMoving => false;` (from Task 6) with the real field, and add `MoveTo`/`TeleportTo`/`_Process`:

```csharp
        private Vector2 _targetPosition;
        private bool _moving;
        protected bool IsMoving => _moving;   // replaces the Task 6 stub

        /// <summary>Server (or local prediction) says this character stepped to (x,y).</summary>
        public void MoveTo(int x, int y)
        {
            if (x != X) Facing = x > X ? Direction.Right : Direction.Left;
            else if (y != Y) Facing = y > Y ? Direction.Down : Direction.Up;
            X = x; Y = y;
            _targetPosition = MapCoords.TileBottomCenter(x, y);
            _moving = true;
            ApplyDrawOrder();   // facing may have changed -> reorder shield/weapon
            PlayState();
        }

        /// <summary>Instant placement (spawn / SUP teleport) — no walk animation.</summary>
        public void TeleportTo(int x, int y)
        {
            X = x; Y = y;
            Position = MapCoords.TileBottomCenter(x, y);
            _targetPosition = Position;
            _moving = false;
        }

        public override void _Process(double delta)
        {
            if (_moving)
            {
                float speed = CharacterMotion.PixelsPerSecond(MoveSpeed);
                Position = Position.MoveToward(_targetPosition, speed * (float)delta);
                if (Position.IsEqualApprox(_targetPosition))
                {
                    _moving = false;
                    PlayState();   // back to idle/mounted-idle
                }
            }
            TickAttackLock(delta);   // defined in Task 8
        }
```

> `SetAppearance` (Task 6) already calls `TeleportTo` then `PlayState`, so `_targetPosition` is initialized on spawn. `TeleportTo` no longer calls `PlayState` itself (the caller does) to avoid double-playing during build.

**Step 2: Add a no-op `TickAttackLock` placeholder** (so it compiles before Task 8):

```csharp
        protected virtual void TickAttackLock(double delta) { }
```

**Step 3: Build**

Run: `dotnet build Goose2ClientGodot.csproj`
Expected: succeeds.

**Step 4: Commit**

```bash
git add Scripts/Character/Character.cs
git commit -m "feat(character): tile-to-tile movement + walk/idle playback"
```

---

### Task 8: Attack-lock

Port the reference attack-lock: on attack, play `attack-<dir>`, lock for `frameCount/fps` seconds, then resume idle/walk. Reference: `Character.cs:422-447`,`:635-645`.

**Files:**
- Modify: `Scripts/Character/Character.cs`

**Step 1: Add the lock state + replace the `AttackLocked` stub and `TickAttackLock`**

Replace `protected bool AttackLocked => false;` (from Task 6) and the `protected virtual void TickAttackLock` placeholder (from Task 7) with:

```csharp
        private bool _attackLocked;
        private double _attackTimer;
        protected bool AttackLocked => _attackLocked;   // replaces the Task 6 stub

        public void TriggerAttack()
        {
            _attackLocked = true;
            _attackTimer = AttackDuration(AnimationNames.Clip("attack", Facing));
            PlayCurrent();   // CharacterMotion.State returns "attack" while locked -> all slots swing
        }

        private double AttackDuration(string clip)
        {
            // Read timing from the Body slot's SpriteFrames; fallback 0.5s (reference Character.cs:436).
            if (_slots.TryGetValue(CharacterSlot.Body, out var body) &&
                body.Sprite.SpriteFrames is { } frames && frames.HasAnimation(clip))
            {
                int n = frames.GetFrameCount(clip);
                float fps = (float)frames.GetAnimationSpeed(clip);
                if (fps > 0) return n / fps;
            }
            return 0.5;
        }

        protected override void TickAttackLock(double delta)
        {
            if (!_attackLocked) return;
            _attackTimer -= delta;
            if (_attackTimer <= 0)
            {
                _attackLocked = false;
                PlayCurrent();   // resume walk/idle/mounted-*
            }
        }
```

> `TickAttackLock` becomes `protected override` (the Task 7 placeholder was `protected virtual`). `PlayState()` already early-returns while `AttackLocked` (added in Task 6), so movement state changes can't clobber the swing mid-animation.

**Step 2: Build**

Run: `dotnet build Goose2ClientGodot.csproj`
Expected: succeeds.

**Step 3: Commit**

```bash
git add Scripts/Character/Character.cs
git commit -m "feat(character): attack-lock with frameCount/fps duration"
```

---

## Phase 3 — World wiring + local player

### Task 9: Character registry in `MapManager`

Replace the Step-5 camera-bootstrap stubs (`_charSpawns`, the MKC/SUC handlers) with a real `LoginId → Character` registry, and add handlers for MOC/CHH/CHP/ERC/ATT. Keep SUP/roof camera behavior.

**Files:**
- Modify: `Scripts/MapManager.cs` (replace `:17-19` fields, `:40-48` listeners, `:50-60` removals, `:66-95` handlers)

**Step 1: Add packet listeners**

In `_Ready`, after the existing `pm.Listen<MakeCharacterPacket>` / `pm.Listen<SetYourCharacterPacket>` lines, add:

```csharp
        pm.Listen<MoveCharacterPacket>(OnMoveCharacter);
        pm.Listen<ChangeHeadingPacket>(OnChangeHeading);
        pm.Listen<UpdateCharacterPacket>(OnUpdateCharacter);
        pm.Listen<EraseCharacterPacket>(OnEraseCharacter);
        pm.Listen<AttackPacket>(OnAttack);
```

Mirror each with a `pm.Remove<...>` in `_ExitTree`.

**Step 2: Add the registry + handlers**

Replace `_charSpawns` with:

```csharp
    private readonly System.Collections.Generic.Dictionary<int, Character.Character> _characters = new();
    private Node2D _characterRoot;
    private static readonly PackedScene CharacterScene = null; // built by code; see CreateCharacter
```

In `_Ready`, after getting `_objects`:

```csharp
        _characterRoot = GetNode<Node2D>("Characters");
```

Replace `OnMakeCharacter` / `OnSetYourCharacter`:

```csharp
    private void OnMakeCharacter(object packetObj)
    {
        var p = (MakeCharacterPacket)packetObj;
        if (_characters.TryGetValue(p.LoginId, out var existing))
            existing.QueueFree();

        var c = new Character.Character { Name = $"Char_{p.LoginId}" };
        _characterRoot.AddChild(c);
        c.SetAppearance(p);
        _characters[p.LoginId] = c;

        if (p.LoginId == _myLoginId) AttachLocalPlayer(c);
    }

    private void OnSetYourCharacter(object packetObj)
    {
        var p = (SetYourCharacterPacket)packetObj;
        _myLoginId = p.LoginId;
        if (_characters.TryGetValue(p.LoginId, out var c)) AttachLocalPlayer(c);
    }

    private void OnMoveCharacter(object packetObj)
    {
        var p = (MoveCharacterPacket)packetObj;
        if (_characters.TryGetValue(p.LoginId, out var c)) c.MoveTo(p.MapX, p.MapY);
    }

    private void OnChangeHeading(object packetObj)
    {
        var p = (ChangeHeadingPacket)packetObj;
        if (_characters.TryGetValue(p.LoginId, out var c)) c.SetFacing(p.Direction);
    }

    private void OnUpdateCharacter(object packetObj)
    {
        var p = (UpdateCharacterPacket)packetObj;
        if (_characters.TryGetValue(p.LoginId, out var c)) c.SetAppearance(ToMake(p));
    }

    private void OnEraseCharacter(object packetObj)
    {
        var p = (EraseCharacterPacket)packetObj;
        if (_characters.Remove(p.LoginId, out var c)) c.QueueFree();
    }

    private void OnAttack(object packetObj)
    {
        var p = (AttackPacket)packetObj;
        if (_characters.TryGetValue(p.LoginId, out var c)) c.TriggerAttack();
    }
```

> `ToMake(UpdateCharacterPacket)` adapts CHP→the MKC shape `SetAppearance` consumes. Since CHP has the same fields (`Scripts/Network/Packets/UpdateCharacterPacket.cs:8-23`), either add a `SetAppearance(UpdateCharacterPacket)` overload on `Character` or a small mapper. Prefer an overload to avoid an allocation; update Task 6's `SetAppearance` signature to take a shared interface or duplicate the body. Keep `MapX/MapY/Facing` from the existing character when CHP omits them (CHP is an appearance update, not a move) — read current `c.X/c.Y/c.Facing` and pass through.

**Step 3: Local-player camera follow**

Replace `OnSetYourPosition` + add `AttachLocalPlayer`:

```csharp
    private Character.Character _localPlayer;

    private void AttachLocalPlayer(Character.Character c)
    {
        _localPlayer = c;
        c.IsLocalPlayer = true;
        CenterCameraOn(c.X, c.Y);
    }

    private void OnSetYourPosition(object packetObj)
    {
        var p = (SetYourPositionPacket)packetObj;
        _localPlayer?.TeleportTo(p.MapX, p.MapY);
        CenterCameraOn(p.MapX, p.MapY);
    }

    public override void _Process(double delta)
    {
        if (_localPlayer != null && Godot.GodotObject.IsInstanceValid(_localPlayer))
        {
            CenterCameraOn(_localPlayer.X, _localPlayer.Y);
            _camera.GlobalPosition = _localPlayer.Position;   // smooth-follow the lerped position
        }
    }
```

> `CenterCameraOn` already calls `UpdateRoofVisibility`. Overriding with `_camera.GlobalPosition = _localPlayer.Position` after gives pixel-smooth follow during the move lerp while keeping roof toggling keyed on the tile. Keep both.

**Step 4: Build**

Run: `dotnet build Goose2ClientGodot.csproj`
Expected: succeeds.

**Step 5: Commit**

```bash
git add Scripts/MapManager.cs Scripts/Character/Character.cs
git commit -m "feat(map): character registry routing MKC/MOC/CHH/CHP/ERC/ATT to Character nodes"
```

---

### Task 10: Local-player input

Drive the local player from input: face/move on directional keys (predictive local move + send to server), attack on the Attack action. Mirrors Unity `PlayerController`. Implement inside `Character` gated by `IsLocalPlayer` (migration-plan recommendation: flag, not a separate component).

**Files:**
- Modify: `Scripts/Character/Character.cs`

**Step 1: Add input handling in `_Process`**

```csharp
        private const double MoveRepeatDelay = 0.12;   // Unity used ~0.1s debounce
        private double _moveCooldown;

        private void ProcessLocalInput(double delta)
        {
            if (!IsLocalPlayer) return;
            _moveCooldown -= delta;

            if (Input.IsActionJustPressed("Attack")) { TriggerAttack(); GameManager.Instance.NetworkClient.Attack(); }

            if (_moving || _moveCooldown > 0) return;

            Direction? dir = null;
            if (Input.IsActionPressed("MoveUp")) dir = Direction.Up;
            else if (Input.IsActionPressed("MoveDown")) dir = Direction.Down;
            else if (Input.IsActionPressed("MoveLeft")) dir = Direction.Left;
            else if (Input.IsActionPressed("MoveRight")) dir = Direction.Right;
            if (dir == null) return;

            var (dx, dy) = Delta(dir.Value);
            int nx = X + dx, ny = Y + dy;
            var map = GetParent().GetParent<MapManager>();   // Characters -> Map(MapManager)
            if (map != null && map.IsValidMove(nx, ny))
            {
                MoveTo(nx, ny);
                GameManager.Instance.NetworkClient.Move(dir.Value);
            }
            else if (Facing != dir.Value)
            {
                SetFacing(dir.Value);
                GameManager.Instance.NetworkClient.Face(dir.Value);
            }
            _moveCooldown = MoveRepeatDelay;
        }

        private static (int dx, int dy) Delta(Direction d) => d switch
        {
            Direction.Up => (0, -1),
            Direction.Down => (0, 1),
            Direction.Left => (-1, 0),
            Direction.Right => (1, 0),
            _ => (0, 0),
        };
```

Call `ProcessLocalInput(delta);` at the top of `_Process`.

> `MapManager.IsValidMove` already exists (`Scripts/MapManager.cs:63`). Occupancy (don't-walk-into-another-character) is NOT enforced yet — Unity checked it; add to `IsValidMove` later when the registry is the source of truth. Note this gap in the follow-ups.

**Step 2: Build**

Run: `dotnet build Goose2ClientGodot.csproj`
Expected: succeeds.

**Step 3: Commit**

```bash
git add Scripts/Character/Character.cs
git commit -m "feat(character): local-player input (predictive move/face/attack)"
```

---

### Task 11: Name label

Small always-visible name above the character (Unity `CreateName`). Reference overlay pattern: a `Label` child with outline.

**Files:**
- Modify: `Scripts/Character/Character.cs`

**Step 1: Add the label in `SetAppearance`**

```csharp
        private Label _nameLabel;

        private void EnsureNameLabel()
        {
            if (_nameLabel != null) return;
            _nameLabel = new Label
            {
                Text = CharacterName,
                HorizontalAlignment = HorizontalAlignment.Center,
                ZIndex = 20,
                Position = new Vector2(-50, -64),
                Size = new Vector2(100, 16),
            };
            _nameLabel.AddThemeFontSizeOverride("font_size", 12);
            _nameLabel.AddThemeConstantOverride("outline_size", 4);
            _nameLabel.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
            AddChild(_nameLabel);
        }
```

Call `EnsureNameLabel()` at the end of `SetAppearance`, and set `_nameLabel.Text = CharacterName;` on update.

**Step 2: Build + commit**

Run: `dotnet build Goose2ClientGodot.csproj` → succeeds.

```bash
git add Scripts/Character/Character.cs
git commit -m "feat(character): name label above character"
```

---

### Task 12: HP/MP bar

Render an HP (and MP) bar above the character, updated from the vitals packet. Find the vitals packet first.

**Files:**
- Modify: `Scripts/Character/Character.cs`, `Scripts/MapManager.cs`

**Step 1: Identify the vitals packet**

Run: `grep -rl "Percent\|Vital\|HPMP\|VPU" Scripts/Network/Packets/`
Then read the matching packet to confirm its prefix + `LoginId/HPPercent/MPPercent` fields. Wire `pm.Listen<...>` in `MapManager._Ready` and route to `c.SetVitals(hp, mp)`. (Unity: `VitalsPercentagePacket`/`UpdateHPMP`.) If no such packet is implemented yet, scope this task to HP-from-MKC only (`p.HPPercent`) and note MP as a follow-up.

**Step 2: Implement a minimal bar**

```csharp
        private ColorRect _hpBar;
        private void EnsureBars() { /* a 32x3 ColorRect at y=-58, green, ZIndex=20 */ }
        public void SetVitals(float hpPercent, float mpPercent)
        {
            EnsureBars();
            _hpBar.Size = new Vector2(32 * Mathf.Clamp(hpPercent, 0, 1), 3);
            _hpBar.Color = hpPercent > 0.66f ? Colors.Green : hpPercent > 0.33f ? Colors.Orange : Colors.Red;
        }
```

Call `SetVitals(p.HPPercent, 1f)` at the end of `SetAppearance`.

**Step 3: Build + commit**

Run: `dotnet build Goose2ClientGodot.csproj` → succeeds.

```bash
git add Scripts/Character/Character.cs Scripts/MapManager.cs
git commit -m "feat(character): HP bar (+vitals packet wiring if present)"
```

---

## Phase 4 — Integration smoke + close-out

### Task 13: Live smoke test against the server

This is the real acceptance gate — Godot node assembly + animation can't be unit-tested. Use @superpowers:verification-before-completion: capture evidence, don't assert success blind.

**Files:** none (manual run).

**Step 1: Run the client against the live server**

```bash
GOOSE_HOST=scyther.local GOOSE_PORT=2006 godot --path . 2>&1 | tee /tmp/step6-smoke.log
```

Log in with saved credentials (`user://login.cfg`).

**Step 2: Verify, in order (record each):**
1. **Spawn:** the local player character appears on the correct spawn tile; camera centered on it. (Compare tile to the Step-5 baseline.)
2. **Layers:** body + hair + eyes + worn equipment (chest/helm/legs/feet) render stacked and aligned; feet sit on the tile (anchor offset correct). Tune `CharacterAnchor`/`HeightPrefix` wiring if feet float or sink.
3. **Movement:** WASD/arrows move the player tile-to-tile with a walk animation; idle on stop; facing changes when blocked.
4. **Remote characters:** other players/monsters spawn (MKC), move (MOC), turn (CHH), and despawn (ERC) — stand near a spawn or `/refresh`.
5. **Attack:** Attack key plays the attack animation and locks briefly; walk doesn't clobber it mid-swing.
6. **Weapon/shield z-order:** a character holding a weapon shows it **in front** when facing right/down and **behind** when facing left/up (shield mirrors this); turning re-sorts them correctly without flicker.
7. **Mount:** a mounted character renders the mount body under the rider, the rider sits on it (mount-anchor correct), and rider slots use the `mounted-*` walk/idle poses while the mount plays its own walk/idle.
8. **Equipment update:** equip/unequip an item (if a vendor/inventory path is reachable) → CHP rebuilds the slots. If unreachable pre-Step-7, note as untested.
9. **Z-order vs map:** characters draw above ground tiles, below roofs; walking under a roof still toggles it; the tallest slot (weapon) never pokes through a still-visible adjacent roof.

**Step 3: Triage**

For each failing item, debug with @superpowers:systematic-debugging. Common culprits: missing `animations.tres` for a graphic id (check `ResourceLoader.Exists`), wrong height key (feet alignment), direction string mismatch, z_index band.

**Step 4: Commit any fixes** with focused messages, then proceed.

---

### Task 14: Update the migration plan

Record Step 6 as landed and capture the deferred follow-ups so they're not lost.

**Files:**
- Modify: `MIGRATION_PLAN.md` (the porting-order list `:327-329`, and the open-questions / follow-ups sections)

**Step 1:** Mark porting-order item 6 ✅ landed with a dated summary (mirror the Step-5 entry style): 10-slot node-per-slot paper-doll (incl. mount=body, shield+weapon=hands) with per-direction weapon/shield z-order, registry in `MapManager`, predictive local input, attack-lock, camera follow, live-validated against `scyther.local:2006`.

**Step 2:** Add a "Step 6 deferred" subsection under the follow-ups listing: equip/no-equip + weapon-specific attack clips (`attack-1hand`/`-staff`/`-bow`/`-2hand`), occupancy check in `IsValidMove`, and the Step-8 overlays (chat bubble, battle text, spell/emote).

**Step 3: Commit**

```bash
git add MIGRATION_PLAN.md docs/plans/2026-06-06-step6-characters-animation.md
git commit -m "docs: record Step 6 (characters + animation) landed + deferred follow-ups"
```

---

## Done criteria

- All Phase 1 unit tests pass (`dotnet test tests/Goose2Client.Tests`).
- `dotnet build Goose2ClientGodot.csproj` clean.
- Live smoke (Task 13) confirms: local player spawns/moves/faces/attacks with aligned 10-layer paper-doll animation (incl. equipment, weapon/shield with correct per-direction depth, and mounts), camera follows, remote characters spawn/move/despawn, z-order vs map correct (no roof poke-through).
- `MIGRATION_PLAN.md` updated; deferred items recorded.
