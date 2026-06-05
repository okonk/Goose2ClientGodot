# Asset Converter Compiled Animations Implementation Plan

**Goal:** Extend the standalone AssetConverter from the validated `.adf` PNG vertical slice into a Godot-native animation/data generator that parses `compiled.enc`, emits real multi-animation `SpriteFrames` resources, writes the Unity-compatible animation metadata text files, and copies map bytes.

**Architecture:** Keep the converter as a standalone .NET console tool under `tools/AssetConverter/`. Port Unity's remaining pure data parser (`CompiledEnc`) verbatim, then replace Unity's `AnimationClip`/AssetBundle output with text `.tres` generation. Generated character resources are grouped by compiled graphic identity (`Type-Id`, e.g. `Body-1`) and may reference multiple sheet PNGs via multiple `ext_resource` entries.

**Tech Stack:** .NET 10 console app/tests, xUnit, SixLabors.ImageSharp, Godot 4 `SpriteFrames` `.tres` text resources.

---

## APIs verified

Unity source:
- `/home/agent/workspace/Goose2Client/Assets/Scripts/Editor/IllutiaData.cs:10-43` — `AnimationDirection`, `AnimationOrder`, `AnimationType` enum values and ordering.
- `/home/agent/workspace/Goose2Client/Assets/Scripts/Editor/IllutiaData.cs:45-60` — `CompiledAnimation` fields: `Type`, `Id`, `AnimationIndexes[4 * 11]`, `AnimationFiles[11]`.
- `/home/agent/workspace/Goose2Client/Assets/Scripts/Editor/IllutiaData.cs:63-108` — `CompiledEnc` binary format: repeated records of `Int16 typeMinusOne`, `Int32 id`, 44 animation ids, 11 sheet file numbers; `SheetToAnimation[fileNumber] = animation`.
- `/home/agent/workspace/Goose2Client/Assets/Scripts/Editor/ToolsMenu.cs:278-360` — Unity `ImportAnimations` behavior: loops compiled animations, resolves `sheetNumber`, resolves `animationId`, falls back to `adf.Frames[direction]` when animation definition missing, writes first-frame metadata only for `WalkingNoEquip + Down`, writes height metadata for non-64px animations, then emits uncompiled animations as spell bundles.
- `/home/agent/workspace/Goose2Client/Assets/Scripts/Editor/ToolsMenu.cs:362-383` — Unity-generated idle animations are one-frame aliases from the first walking frame for `WalkingNoEquip`, `WalkingEquip`, and `Mounted`.
- `/home/agent/workspace/Goose2Client/Assets/Scripts/Editor/ToolsMenu.cs:386-391` — height metadata is the max frame height and omits default height `64`.
- `/home/agent/workspace/Goose2Client/Assets/Scripts/Editor/ToolsMenu.cs:122-131` — map copy behavior: `*.map` becomes `M{basename.Substring(1)}.bytes`, which preserves names like `Map100.bytes`.
- `/home/agent/workspace/Goose2Client/Assets/Scripts/Editor/ToolsMenu.cs:437-446` — Unity sprite rects used bottom-left `Rect(frame.X, totalHeight - frame.Y - frame.H, frame.W, frame.H)`; Godot `AtlasTexture.region` stays top-left and uses `Frame.X/Y` directly.
- `/home/agent/workspace/Goose2Client/Assets/Scripts/AnimationManager.cs:37-85` — runtime metadata reader shape: `AnimationHeights.txt` lines are `name,height`; `AnimationToFirstFrame.txt` lines are `name,fileId,graphicId,width,height`; missing height returns `64`.
- `/home/agent/workspace/Goose2Client/Assets/Scripts/Character/Character.cs:61-83` — character layer type/id combinations: `Body`, `Hair`, `Eyes`, `Chest`, `Helm`, `Legs`, `Feet`, `Hand`.
- `/home/agent/workspace/Goose2Client/Assets/Scripts/Character/CharacterAnimation.cs:33-66` — Unity per-layer graphic replacement loads bundle `${type.ToLowerInvariant()}-${id}`, maps template clip names to `${type}-${id}-...`, and uses `AnimationManager.GetHeight` for positioning.
- `/home/agent/workspace/3dMMO-Server/client/Assets/Scripts/Entity/Character.cs:152-160` — reference Godot client loads one `SpriteFrames` resource per body id and replays the current animation after swap.
- `/home/agent/workspace/3dMMO-Server/client/Assets/Scripts/Entity/Character.cs:403-430` — reference animation naming and runtime convention: `animated.Play($"{state}-{direction.ToString().ToLower()}")`, attack lock uses the same name.
- `/home/agent/workspace/3dMMO-Server/client/Assets/Sprites/Bodies/1/animations.tres:1-80` and `:413-738` — target `.tres` shape: `SpriteFrames` resource, `ext_resource` textures, `AtlasTexture` subresources, animations named like `attack-down`, `idle-left`, `walk-up`.

Current converter source:
- `tools/AssetConverter/src/AssetConverter/Adf/IllutiaData.cs:7-165` — existing `AdfType`, `Animation`, `Frame`, `AdfFile` parser and decoded `FileData`.
- `tools/AssetConverter/src/AssetConverter/BatchConverter.cs:17-51` — current batch API and result shape.
- `tools/AssetConverter/src/AssetConverter/BatchConverter.cs:54-82` — current private payload decoder supports GIF and BMP PNG output.
- `tools/AssetConverter/src/AssetConverter/SpriteFrames/SpriteFramesWriter.cs:6-45` — current vertical-slice writer emits one sheet as one animation named `all`; this will be replaced/overloaded for compiled animation resources.
- `tools/AssetConverter/src/AssetConverter/Program.cs:3-28` — current commands: `batch [outDir]`, `frames <id>`.
- `tools/AssetConverter/src/AssetConverter/Paths.cs:5-14` — centralized source paths.

Ground-truth fixture values from `/home/agent/workspace/Illutia/data/compiled.enc` and ADF parsing:
- `compiled.enc` size: `190066`, record size: `226`, record count: `841`.
- First compiled record: `Type=Body`, `Id=1`, `AnimationFiles = [115,116,117,118,119,120,121,122,123,124,125]`.
- Body-1 `WalkingNoEquip` animation ids by direction `[Left,Down,Right,Up] = [3220,3221,3222,3223]`.
- `115.adf` animation `3220` has 5 frames; first frame is `(Index=3200, X=0, Y=0, W=24, H=48)`, max height `48`.
- `115.adf` animation `3221` has first frame `(Index=3205, X=0, Y=48, W=24, H=48)`.

---

## Naming and output conventions

### Character/paper-doll resources

For each `CompiledAnimation` record, write one resource:

```text
Assets/Sprites/<TypePlural>/<Id>/animations.tres
```

Plural folder mapping:

| `AnimationType` | Folder |
|---|---|
| `Body` | `Bodies` |
| `Hair` | `Hair` |
| `Eyes` | `Eyes` |
| `Chest` | `Chest` |
| `Helm` | `Helms` |
| `Legs` | `Legs` |
| `Feet` | `Feet` |
| `Hand` | `Hands` |

Each resource references sheet textures at:

```text
res://Assets/Sprites/sheets/<sheetNumber>.png
```

### Animation names

Use lowercase Godot names while preserving Goose2's equipment/body-state distinctions. Direction suffixes are lowercase `left`, `down`, `right`, `up`.

| Unity `AnimationOrder` | Godot animation state prefix |
|---|---|
| `WalkingNoEquip` | `walk-no-equip` |
| `WalkingEquip` | `walk-equip` |
| `AttackNoEquip` | `attack-no-equip` |
| `Attack1Hand` | `attack-1hand` |
| `AttackStaff` | `attack-staff` |
| `Attack2Hand` | `attack-2hand` |
| `AttackBow` | `attack-bow` |
| `SpellCast` | `cast` |
| `Knealing` | `kneel` |
| `Death` | `death` |
| `Mounted` | `mounted-walk` |

Generate one-frame idle aliases matching Unity's `GenerateIdleAnimation`:

| Source order | Idle prefix |
|---|---|
| `WalkingNoEquip` | `idle-no-equip` |
| `WalkingEquip` | `idle-equip` |
| `Mounted` | `mounted-idle` |

Also generate simple compatibility aliases for the no-equip baseline so the first Godot character prototype can use the reference convention immediately:

| Alias | Source |
|---|---|
| `walk-<direction>` | `walk-no-equip-<direction>` |
| `idle-<direction>` | `idle-no-equip-<direction>` |
| `attack-<direction>` | `attack-no-equip-<direction>` |

Do **not** collapse the specific names; future character state logic still needs the exact equipped/weapon/mounted variants.

### Metadata files

Write Unity-compatible text files under:

```text
Assets/Resources/AnimationToFirstFrame.txt
Assets/Resources/AnimationHeights.txt
```

This preserves the current Unity runtime shape verified in `AnimationManager.cs:37-85` and gives the later Godot `AnimationManager` port a known data contract.

---

## Task 0: Port `compiled.enc` parser and enums

**Files:**
- Modify: `tools/AssetConverter/src/AssetConverter/Adf/IllutiaData.cs`
- Test: `tools/AssetConverter/tests/AssetConverter.Tests/CompiledEncTests.cs`

**Step 1: Write the failing tests**

Add tests that prove the parser shape and a concrete Body-1 oracle:

```csharp
using Goose2.AssetConverter;
using Goose2.AssetConverter.Adf;
using Xunit;

public class CompiledEncTests
{
    [Fact]
    public void CompiledEnc_HasExpectedRecordCountAndFirstBodyRecord()
    {
        var compiled = new CompiledEnc(Paths.CompiledEnc);

        Assert.Equal(841, compiled.CompiledAnimations.Count);
        var body1 = compiled.CompiledAnimations[0];
        Assert.Equal(AnimationType.Body, body1.Type);
        Assert.Equal(1, body1.Id);
        Assert.Equal(new[] { 115,116,117,118,119,120,121,122,123,124,125 }, body1.AnimationFiles);
        Assert.Equal(new[] { 3220, 3221, 3222, 3223 }, new[]
        {
            body1.AnimationIndexes[(int)AnimationDirection.Left * 11 + (int)AnimationOrder.WalkingNoEquip],
            body1.AnimationIndexes[(int)AnimationDirection.Down * 11 + (int)AnimationOrder.WalkingNoEquip],
            body1.AnimationIndexes[(int)AnimationDirection.Right * 11 + (int)AnimationOrder.WalkingNoEquip],
            body1.AnimationIndexes[(int)AnimationDirection.Up * 11 + (int)AnimationOrder.WalkingNoEquip],
        });
    }

    [Fact]
    public void CompiledEnc_SheetToAnimationIndexesAllNonZeroFiles()
    {
        var compiled = new CompiledEnc(Paths.CompiledEnc);

        Assert.True(compiled.SheetToAnimation.TryGetValue(115, out var body1));
        Assert.Equal(AnimationType.Body, body1.Type);
        Assert.Equal(1, body1.Id);
    }
}
```

Also add to `Paths.cs`:

```csharp
public static string CompiledEnc => Path.Combine(IllutiaData, "compiled.enc");
```

**Step 2: Run test to verify it fails (red)**

Run:

```bash
cd /home/agent/workspace/Goose2ClientGodot/tools/AssetConverter
dotnet test --filter CompiledEncTests
```

Expected: compile failure because `CompiledEnc`, `CompiledAnimation`, and animation enums do not exist.

**Step 3: Port the parser**

Copy Unity `IllutiaData.cs:10-108` into the existing `Goose2.AssetConverter.Adf` namespace above `AdfType`:

- Keep enum numeric ordering exactly.
- Keep `CompiledAnimation.AnimationIndexes = new int[4 * 11]` and `AnimationFiles = new int[11]`.
- Keep binary read order exactly: `ReadInt16() - 1`, `ReadInt32()`, 44 `ReadInt32()` indexes, 11 `ReadInt32()` file numbers.
- Initialize `SheetToAnimation` in both constructors; the Unity parameterless constructor leaves it null, but the converter does not need that footgun.

**Step 4: Run test to verify it passes (green)**

Run:

```bash
dotnet test --filter CompiledEncTests
```

Expected: PASS.

**Step 5: Commit**

```bash
cd /home/agent/workspace/Goose2ClientGodot
git add tools/AssetConverter/
git commit -m "feat: port compiled.enc animation parser"
```

---

## Task 1: Add animation naming and output path helpers

**Files:**
- Create: `tools/AssetConverter/src/AssetConverter/SpriteFrames/AnimationNaming.cs`
- Test: `tools/AssetConverter/tests/AssetConverter.Tests/AnimationNamingTests.cs`

**Step 1: Write the failing tests**

Cover baseline names, specific names, idle aliases, folder mapping, and an adversarial enum-order case:

```csharp
using Goose2.AssetConverter.Adf;
using Goose2.AssetConverter.SpriteFrames;
using Xunit;

public class AnimationNamingTests
{
    [Theory]
    [InlineData(AnimationOrder.WalkingNoEquip, AnimationDirection.Down, "walk-no-equip-down")]
    [InlineData(AnimationOrder.Attack1Hand, AnimationDirection.Left, "attack-1hand-left")]
    [InlineData(AnimationOrder.SpellCast, AnimationDirection.Up, "cast-up")]
    [InlineData(AnimationOrder.Mounted, AnimationDirection.Right, "mounted-walk-right")]
    public void ClipName_MapsOrdersAndDirections(AnimationOrder order, AnimationDirection direction, string expected)
        => Assert.Equal(expected, AnimationNaming.ClipName(order, direction));

    [Theory]
    [InlineData(AnimationOrder.WalkingNoEquip, "idle-no-equip-left")]
    [InlineData(AnimationOrder.WalkingEquip, "idle-equip-left")]
    [InlineData(AnimationOrder.Mounted, "mounted-idle-left")]
    public void TryIdleName_ReturnsOnlyUnityGeneratedIdleOrders(AnimationOrder order, string expected)
    {
        Assert.True(AnimationNaming.TryIdleName(order, AnimationDirection.Left, out var name));
        Assert.Equal(expected, name);
    }

    [Fact]
    public void TryIdleName_DoesNotGenerateIdleForAttack()
        => Assert.False(AnimationNaming.TryIdleName(AnimationOrder.AttackNoEquip, AnimationDirection.Down, out _));

    [Theory]
    [InlineData(AnimationType.Body, "Bodies")]
    [InlineData(AnimationType.Helm, "Helms")]
    [InlineData(AnimationType.Hand, "Hands")]
    public void TypeFolder_MapsPluralFolders(AnimationType type, string expected)
        => Assert.Equal(expected, AnimationNaming.TypeFolder(type));

    [Fact]
    public void DirectionNames_FollowCompiledEncDirectionOrder()
    {
        Assert.Equal("left", AnimationNaming.DirectionName(AnimationDirection.Left));
        Assert.Equal("down", AnimationNaming.DirectionName(AnimationDirection.Down));
        Assert.Equal("right", AnimationNaming.DirectionName(AnimationDirection.Right));
        Assert.Equal("up", AnimationNaming.DirectionName(AnimationDirection.Up));
    }
}
```

**Step 2: Run test to verify it fails (red)**

Run:

```bash
dotnet test --filter AnimationNamingTests
```

Expected: compile failure because `AnimationNaming` does not exist.

**Step 3: Implement helper contracts**

Create a static helper with these methods:

```csharp
public static class AnimationNaming
{
    public static string DirectionName(AnimationDirection direction);
    public static string ClipName(AnimationOrder order, AnimationDirection direction);
    public static bool TryIdleName(AnimationOrder sourceOrder, AnimationDirection direction, out string name);
    public static IReadOnlyList<string> CompatibilityAliases(AnimationOrder order, AnimationDirection direction);
    public static string TypeFolder(AnimationType type);
    public static string ResourceRelativePath(AnimationType type, int id);
    public static string ResourcePath(AnimationType type, int id); // res://...
}
```

Contracts:
- These helpers only construct names/paths. They do **not** read files, write files, register resources, or mutate converter state.
- `CompatibilityAliases` returns `walk-<dir>` for `WalkingNoEquip`, `idle-<dir>` is handled by idle generation, and `attack-<dir>` for `AttackNoEquip`; otherwise empty.
- `ResourceRelativePath(Body, 1)` returns `Assets/Sprites/Bodies/1/animations.tres`.
- `ResourcePath(Body, 1)` returns `res://Assets/Sprites/Bodies/1/animations.tres`.

**Step 4: Run tests to verify they pass (green)**

Run:

```bash
dotnet test --filter AnimationNamingTests
```

Expected: PASS.

**Step 5: Commit**

```bash
git add tools/AssetConverter/
git commit -m "feat: add Godot animation naming helpers"
```

---

## Task 2: Replace single-sheet `SpriteFramesWriter` with multi-animation support

**Files:**
- Modify: `tools/AssetConverter/src/AssetConverter/SpriteFrames/SpriteFramesWriter.cs`
- Test: `tools/AssetConverter/tests/AssetConverter.Tests/SpriteFramesWriterTests.cs`

**Mutation impact:**
- Source of truth changed: `SpriteFramesWriter.Build` currently emits all frames from one `AdfFile` as one animation named `all` (`SpriteFramesWriter.cs:11-45`).
- Important readers: existing `SpriteFramesWriterTests.cs:8-23`; current `Program.cs:15-24` `frames <id>` command.
- Derived/cached state affected: no runtime cache; output text format changes only for new overloads. Preserve the existing `Build(AdfFile, texturePath, speed)` API for the vertical-slice test/command.
- Required propagation sequence:
  1. Keep current `Build(AdfFile, string, float)` behavior intact.
  2. Add new model types for multi-sheet animation specs.
  3. Add a new build overload for compiled resources.
  4. Update only new tests to use the new overload.
- Invariants to preserve:
  - Existing sheet-1000 `all` writer test still passes.
  - Godot `AtlasTexture.region` uses top-left `Frame.X/Y` directly.
  - Multiple animations can reference the same sheet without duplicate `ext_resource` paths.
- Observable proof required:
  - Tests assert emitted `.tres` contains expected paths, regions, animation names, and no duplicate texture resources.

**Step 1: Write the failing tests**

Append tests while keeping the existing vertical-slice test:

```csharp
[Fact]
public void BuildCompiled_EmitsMultipleAnimationsAcrossMultipleSheets()
{
    var adf115 = new AdfFile(Goose2.AssetConverter.Paths.Adf(115));
    var adf116 = new AdfFile(Goose2.AssetConverter.Paths.Adf(116));

    var animations = new[]
    {
        SpriteFramesAnimationSpec.FromFrames(
            "walk-left", 115, "res://Assets/Sprites/sheets/115.png", adf115.Animations![3220].Frames),
        SpriteFramesAnimationSpec.FromFrames(
            "walk-down", 115, "res://Assets/Sprites/sheets/115.png", adf115.Animations![3221].Frames),
        SpriteFramesAnimationSpec.FromFrames(
            "walk-equip-left", 116, "res://Assets/Sprites/sheets/116.png", adf116.Animations![3244].Frames),
    };

    string tres = SpriteFramesWriter.Build(animations);

    Assert.Contains("path=\"res://Assets/Sprites/sheets/115.png\"", tres);
    Assert.Contains("path=\"res://Assets/Sprites/sheets/116.png\"", tres);
    Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(tres, "ext_resource type=\"Texture2D\"").Count);
    Assert.Contains("region = Rect2(0, 0, 24, 48)", tres);
    Assert.Contains("region = Rect2(0, 48, 24, 48)", tres);
    Assert.Contains("\"name\": &\"walk-left\"", tres);
    Assert.Contains("\"name\": &\"walk-down\"", tres);
    Assert.Contains("\"name\": &\"walk-equip-left\"", tres);
}

[Fact]
public void BuildCompiled_ThrowsForEmptyAnimationList()
{
    Assert.Throws<ArgumentException>(() => SpriteFramesWriter.Build(Array.Empty<SpriteFramesAnimationSpec>()));
}
```

**Step 2: Run test to verify it fails (red)**

Run:

```bash
dotnet test --filter SpriteFramesWriterTests
```

Expected: compile failure because `SpriteFramesAnimationSpec` and `SpriteFramesWriter.Build(IEnumerable<...>)` do not exist.

**Step 3: Implement the model and overload**

Add small records/classes in the same namespace:

```csharp
public sealed record SpriteFrameSpec(int SheetNumber, string TexturePath, Frame Frame);
public sealed record SpriteFramesAnimationSpec(string Name, IReadOnlyList<SpriteFrameSpec> Frames, bool Loop = true, float Speed = 8f)
{
    public static SpriteFramesAnimationSpec FromFrames(string name, int sheetNumber, string texturePath, IReadOnlyList<Frame> frames);
}
```

Build overload contract:
- Input must contain at least one animation and each animation must contain at least one frame; throw `ArgumentException` otherwise.
- Deduplicate texture `ext_resource`s by exact `TexturePath`, assigning deterministic ids like `Tex_0`, `Tex_1` in first-seen order.
- Emit one `AtlasTexture` subresource per animation frame occurrence. Do not attempt frame deduplication yet; deterministic text is more important than minimizing file size.
- Preserve animation order as passed in.
- Preserve existing `Build(AdfFile, ...)` output for the demo command.

**Step 4: Run tests to verify they pass (green)**

Run:

```bash
dotnet test --filter SpriteFramesWriterTests
```

Expected: PASS.

**Step 5: Commit**

```bash
git add tools/AssetConverter/
git commit -m "feat: emit multi-animation SpriteFrames resources"
```

---

## Task 3: Build compiled character animation resources and metadata in memory

**Files:**
- Create: `tools/AssetConverter/src/AssetConverter/SpriteFrames/CompiledAnimationBuilder.cs`
- Create: `tools/AssetConverter/src/AssetConverter/SpriteFrames/AnimationMetadata.cs`
- Test: `tools/AssetConverter/tests/AssetConverter.Tests/CompiledAnimationBuilderTests.cs`

**Mutation impact:**
- Source of truth changed: generated animation resource specs and metadata are newly derived from `CompiledEnc` + `AdfFile`.
- Important readers: future writer task; future Godot character animation loader; Unity-compatible metadata reader shape verified at `AnimationManager.cs:37-85`.
- Derived/cached state affected: metadata dictionaries `AnimationToFirstFrame` and `AnimationHeights` are derived state from animation frame definitions.
- Required propagation sequence:
  1. For each `CompiledAnimation`, resolve each nonzero `AnimationFiles[order]` to an `AdfFile`.
  2. For each direction/order, resolve `AnimationIndexes[direction * 11 + order]`.
  3. Resolve frames from `adf.Animations[animationId]`, or fall back to `adf.Frames[direction]`, matching `ToolsMenu.cs:304-308`.
  4. Add the specific animation spec.
  5. Add compatibility aliases for baseline no-equip names.
  6. For walking no-equip/down only, write first-frame metadata keyed by `Type-Id`, matching `ToolsMenu.cs:320-323`.
  7. For walking/equip/mounted orders, add one-frame idle specs and simple idle alias, matching `ToolsMenu.cs:326-327` and `:362-383`.
  8. For every emitted animation, if max frame height is not 64, write height metadata keyed by the emitted animation name, matching `ToolsMenu.cs:386-391`.
- Invariants to preserve:
  - Missing sheet or zero animation id skips that animation and records a warning, not a crash.
  - Fallback-to-direction-frame works when `AdfFile.Animations` is null or missing the id.
  - Metadata uses Unity-compatible names where required: `AnimationToFirstFrame` key is `Type-Id`; height keys are emitted animation names.
  - Body-1 first-frame metadata is `Body-1,115,3205,24,48`.
- Observable proof required:
  - Tests assert final spec and metadata values, not helper-call counts.

**Step 1: Write the failing tests**

Use the real Body-1 fixture:

```csharp
using Goose2.AssetConverter;
using Goose2.AssetConverter.Adf;
using Goose2.AssetConverter.SpriteFrames;
using Xunit;

public class CompiledAnimationBuilderTests
{
    [Fact]
    public void BuildCharacterResource_Body1_EmitsWalkIdleAliasesAndMetadata()
    {
        var compiled = new CompiledEnc(Paths.CompiledEnc);
        var body1 = compiled.CompiledAnimations[0];
        var adfs = new[] { 115, 116, 117, 118, 119, 120, 121, 122, 123, 124, 125 }
            .ToDictionary(id => id, id => new AdfFile(Paths.Adf(id)));

        var result = CompiledAnimationBuilder.BuildCharacterResource(body1, adfs);

        Assert.Equal(AnimationType.Body, result.Type);
        Assert.Equal(1, result.Id);
        Assert.Equal("Assets/Sprites/Bodies/1/animations.tres", result.RelativeOutputPath);
        Assert.Contains(result.Animations, a => a.Name == "walk-no-equip-left" && a.Frames.Count == 5);
        Assert.Contains(result.Animations, a => a.Name == "walk-left" && a.Frames.Count == 5);
        Assert.Contains(result.Animations, a => a.Name == "idle-no-equip-down" && a.Frames.Count == 1);
        Assert.Contains(result.Animations, a => a.Name == "idle-down" && a.Frames.Count == 1);

        var first = Assert.Single(result.AnimationToFirstFrame);
        Assert.Equal("Body-1", first.Key);
        Assert.Equal(new AnimationFrameInfo(115, 3205, 24, 48), first.Value);

        Assert.Equal(48, result.AnimationHeights["walk-no-equip-left"]);
        Assert.Equal(48, result.AnimationHeights["idle-no-equip-left"]);
        Assert.DoesNotContain(result.AnimationHeights, kvp => kvp.Value == 64);
    }

    [Fact]
    public void BuildCharacterResource_MissingSheetRecordsWarningAndContinues()
    {
        var compiled = new CompiledEnc(Paths.CompiledEnc);
        var body1 = compiled.CompiledAnimations[0];
        var adfs = new Dictionary<int, AdfFile> { [115] = new(Paths.Adf(115)) };

        var result = CompiledAnimationBuilder.BuildCharacterResource(body1, adfs);

        Assert.NotEmpty(result.Animations);
        Assert.NotEmpty(result.Warnings);
        Assert.Contains(result.Warnings, w => w.Contains("116"));
    }
}
```

**Step 2: Run test to verify it fails (red)**

Run:

```bash
dotnet test --filter CompiledAnimationBuilderTests
```

Expected: compile failure because builder/result/metadata types do not exist.

**Step 3: Implement minimal models**

Create metadata records:

```csharp
public sealed record AnimationFrameInfo(int FileId, int GraphicId, int Width, int Height);

public sealed record CompiledSpriteFramesResource(
    AnimationType Type,
    int Id,
    string RelativeOutputPath,
    IReadOnlyList<SpriteFramesAnimationSpec> Animations,
    IReadOnlyDictionary<string, AnimationFrameInfo> AnimationToFirstFrame,
    IReadOnlyDictionary<string, int> AnimationHeights,
    IReadOnlyList<string> Warnings);
```

Builder contract:

```csharp
public static class CompiledAnimationBuilder
{
    public static CompiledSpriteFramesResource BuildCharacterResource(
        CompiledAnimation compiledAnimation,
        IReadOnlyDictionary<int, AdfFile> adfs);
}
```

Implementation notes:
- Texture path for sheet `115` is exactly `res://Assets/Sprites/sheets/115.png`.
- Loop `animationNumber` from `0` to `10`, cast to `AnimationOrder`.
- Loop directions from `0` to `3`, cast to `AnimationDirection`.
- Index formula is exactly `direction * 11 + animationNumber` (`IllutiaData.cs:88`, `ToolsMenu.cs:299`).
- For missing sheet or zero animation id, skip and add a warning string with enough detail: `Body-1 WalkingEquip Left: missing sheet 116`.
- For fallback frames, use exactly `new[] { adf.Frames[(int)direction] }`.
- Metadata dictionaries are per resource. The batch task will merge them.

**Step 4: Run tests to verify they pass (green)**

Run:

```bash
dotnet test --filter CompiledAnimationBuilderTests
```

Expected: PASS.

**Step 5: Commit**

```bash
git add tools/AssetConverter/
git commit -m "feat: build compiled character SpriteFrames specs"
```

---

## Task 4: Add metadata text writers

**Files:**
- Create: `tools/AssetConverter/src/AssetConverter/SpriteFrames/AnimationMetadataWriter.cs`
- Test: `tools/AssetConverter/tests/AssetConverter.Tests/AnimationMetadataWriterTests.cs`

**Mutation impact:**
- Source of truth changed: generated metadata text files are new persisted output.
- Important readers: Unity `AnimationManager` parsing contract at `AnimationManager.cs:37-85`; future Godot manager port.
- Derived/cached state affected: no in-process cache; this writes sorted text from dictionaries.
- Required propagation sequence:
  1. Merge per-resource first-frame dictionaries; duplicate keys with same value are allowed, duplicate keys with different values throw.
  2. Merge per-resource height dictionaries; duplicate keys with same value are allowed, duplicate keys with different values throw.
  3. Write `AnimationToFirstFrame.txt` and `AnimationHeights.txt` lines.
- Invariants to preserve:
  - Output is deterministic: sort by key ordinal.
  - First-frame line shape is exactly `name,fileId,graphicId,width,height`.
  - Height line shape is exactly `name,height`.
  - Conflicting duplicate keys fail before writing partial files.
- Observable proof required:
  - Tests assert final file text and conflict behavior.

**Step 1: Write the failing tests**

```csharp
using Goose2.AssetConverter.SpriteFrames;
using Xunit;

public class AnimationMetadataWriterTests
{
    [Fact]
    public void BuildText_WritesDeterministicUnityCompatibleLines()
    {
        var frames = new Dictionary<string, AnimationFrameInfo>
        {
            ["Hair-2"] = new(200, 3000, 32, 48),
            ["Body-1"] = new(115, 3205, 24, 48),
        };
        var heights = new Dictionary<string, int>
        {
            ["walk-left"] = 48,
            ["idle-left"] = 48,
        };

        Assert.Equal("Body-1,115,3205,24,48\nHair-2,200,3000,32,48\n",
            AnimationMetadataWriter.BuildFirstFrameText(frames));
        Assert.Equal("idle-left,48\nwalk-left,48\n",
            AnimationMetadataWriter.BuildHeightsText(heights));
    }

    [Fact]
    public void MergeFirstFrames_ThrowsOnConflictingDuplicateKey()
    {
        var a = new Dictionary<string, AnimationFrameInfo> { ["Body-1"] = new(115, 3205, 24, 48) };
        var b = new Dictionary<string, AnimationFrameInfo> { ["Body-1"] = new(999, 3205, 24, 48) };

        Assert.Throws<InvalidOperationException>(() => AnimationMetadataWriter.MergeFirstFrames(new[] { a, b }));
    }
}
```

**Step 2: Run test to verify it fails (red)**

Run:

```bash
dotnet test --filter AnimationMetadataWriterTests
```

Expected: compile failure because `AnimationMetadataWriter` does not exist.

**Step 3: Implement writer**

Required API:

```csharp
public static class AnimationMetadataWriter
{
    public static Dictionary<string, AnimationFrameInfo> MergeFirstFrames(IEnumerable<IReadOnlyDictionary<string, AnimationFrameInfo>> dictionaries);
    public static Dictionary<string, int> MergeHeights(IEnumerable<IReadOnlyDictionary<string, int>> dictionaries);
    public static string BuildFirstFrameText(IReadOnlyDictionary<string, AnimationFrameInfo> frames);
    public static string BuildHeightsText(IReadOnlyDictionary<string, int> heights);
    public static void Write(string resourcesDir, IReadOnlyDictionary<string, AnimationFrameInfo> frames, IReadOnlyDictionary<string, int> heights);
}
```

`Write` creates `resourcesDir` and writes:
- `Path.Combine(resourcesDir, "AnimationToFirstFrame.txt")`
- `Path.Combine(resourcesDir, "AnimationHeights.txt")`

Failure behavior:
- `Merge*` throws before `Write` is called when conflicts exist.
- `Write` overwrites complete files with `File.WriteAllText`; no append.

**Step 4: Run tests to verify they pass (green)**

Run:

```bash
dotnet test --filter AnimationMetadataWriterTests
```

Expected: PASS.

**Step 5: Commit**

```bash
git add tools/AssetConverter/
git commit -m "feat: write animation metadata text files"
```

---

## Task 5: Add animation batch generator and CLI command

**Files:**
- Create: `tools/AssetConverter/src/AssetConverter/SpriteFrames/AnimationBatchConverter.cs`
- Modify: `tools/AssetConverter/src/AssetConverter/Program.cs`
- Test: `tools/AssetConverter/tests/AssetConverter.Tests/AnimationBatchConverterTests.cs`

**Mutation impact:**
- Source of truth changed: generated `.tres` and metadata files are persisted under a caller-provided output root.
- Important readers: Godot resource loader paths, future character loader, current CLI.
- Derived/cached state affected: output filesystem only.
- Required propagation sequence:
  1. Load all ADFs from `dataDir` into a dictionary by `FileNumber`, matching current `ToolsMenu.LoadAdfs` behavior (`ToolsMenu.cs:143-154`).
  2. Parse `compiled.enc`.
  3. For each compiled animation, build a `CompiledSpriteFramesResource`.
  4. Serialize `resource.Animations` with `SpriteFramesWriter.Build` and write to `<outRoot>/<RelativeOutputPath>`.
  5. Merge and write metadata under `<outRoot>/Assets/Resources`.
  6. Return counts and warnings.
- Publication boundary:
  - Resources become observable when individual `.tres` files are written.
  - Metadata becomes observable after all resources are built and conflict-checked.
  - If metadata merge conflicts, fail before writing metadata; already-written `.tres` files may remain. This is acceptable for a rerunnable converter, but the result must report failure and the next successful run overwrites outputs.
- Invariants to preserve:
  - Body-1 `animations.tres` exists and includes `walk-no-equip-left`, `walk-left`, `idle-down`.
  - Metadata files exist and contain `Body-1,115,3205,24,48`.
  - Warnings are returned for missing sheets, but normal full data should not crash.
- Observable proof required:
  - Integration-style test writes to a temp output root and asserts files/text, using real `compiled.enc` and real ADFs.

**Step 1: Write the failing tests**

```csharp
using Goose2.AssetConverter;
using Goose2.AssetConverter.SpriteFrames;
using Xunit;

public class AnimationBatchConverterTests
{
    [Fact]
    public void Convert_OnlyBody1_WritesResourceAndMetadata()
    {
        var outRoot = Path.Combine(Path.GetTempPath(), "ac_anim_" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = AnimationBatchConverter.Convert(
                Paths.IllutiaData,
                Paths.CompiledEnc,
                outRoot,
                only: ca => ca.Type == Goose2.AssetConverter.Adf.AnimationType.Body && ca.Id == 1);

            Assert.Equal(1, result.ResourcesWritten);
            var tresPath = Path.Combine(outRoot, "Assets/Sprites/Bodies/1/animations.tres");
            Assert.True(File.Exists(tresPath));
            var tres = File.ReadAllText(tresPath);
            Assert.Contains("\"name\": &\"walk-no-equip-left\"", tres);
            Assert.Contains("\"name\": &\"walk-left\"", tres);
            Assert.Contains("\"name\": &\"idle-down\"", tres);
            Assert.Contains("path=\"res://Assets/Sprites/sheets/115.png\"", tres);

            var firstFrame = File.ReadAllText(Path.Combine(outRoot, "Assets/Resources/AnimationToFirstFrame.txt"));
            Assert.Contains("Body-1,115,3205,24,48", firstFrame);
            var heights = File.ReadAllText(Path.Combine(outRoot, "Assets/Resources/AnimationHeights.txt"));
            Assert.Contains("walk-no-equip-left,48", heights);
        }
        finally
        {
            if (Directory.Exists(outRoot)) Directory.Delete(outRoot, recursive: true);
        }
    }
}
```

**Step 2: Run test to verify it fails (red)**

Run:

```bash
dotnet test --filter AnimationBatchConverterTests
```

Expected: compile failure because `AnimationBatchConverter` does not exist.

**Step 3: Implement batch converter**

Required API:

```csharp
public sealed record AnimationBatchResult(int ResourcesWritten, int Failed, IReadOnlyList<string> Warnings, IReadOnlyList<string> Failures);

public static class AnimationBatchConverter
{
    public static AnimationBatchResult Convert(
        string dataDir,
        string compiledEncPath,
        string outRoot,
        Func<CompiledAnimation, bool>? only = null);
}
```

Implementation notes:
- Load all `.adf` files once. If an individual ADF parse fails, record a failure and continue loading the rest.
- Only pass successfully parsed ADFs to `CompiledAnimationBuilder`.
- For each resource, write `.tres` with `SpriteFramesWriter.Build(resource.Animations)`.
- Merge metadata across resources only after all selected resources are built.
- If a selected resource has zero animations, count it as failed and do not write an empty `.tres`.
- Return builder warnings in `Warnings`; return exceptions in `Failures`.

**Step 4: Wire CLI**

Modify `Program.cs`:

```csharp
if (args.Length >= 1 && args[0] == "animations")
{
    string outRoot = args.Length >= 2
        ? args[1]
        : Path.GetFullPath(Path.Combine("..", ".."));

    var result = Goose2.AssetConverter.SpriteFrames.AnimationBatchConverter.Convert(
        Paths.IllutiaData, Paths.CompiledEnc, outRoot);

    Console.WriteLine($"Wrote {result.ResourcesWritten} animation resources, {result.Failed} failures -> {outRoot}");
    foreach (var w in result.Warnings) Console.WriteLine($"  WARN {w}");
    foreach (var f in result.Failures) Console.WriteLine($"  FAIL {f}");
    return;
}
```

Update usage to include `animations [outRoot]`.

**Step 5: Run tests to verify they pass (green)**

Run:

```bash
dotnet test --filter AnimationBatchConverterTests
```

Expected: PASS.

**Step 6: Run full converter smoke**

Run:

```bash
cd /home/agent/workspace/Goose2ClientGodot/tools/AssetConverter
dotnet run --project src/AssetConverter -- animations /home/agent/workspace/Goose2ClientGodot
find /home/agent/workspace/Goose2ClientGodot/Assets/Sprites -name animations.tres | wc -l
grep -n "Body-1,115,3205,24,48" /home/agent/workspace/Goose2ClientGodot/Assets/Resources/AnimationToFirstFrame.txt
```

Expected:
- Hundreds of `animations.tres` files.
- The grep finds the Body-1 metadata line.
- Warnings are acceptable for genuinely missing/zero entries; failures should be investigated before commit.

**Step 7: Commit**

Do not commit generated `Assets/Sprites/**/*.png` or generated `animations.tres` yet unless the repository asset policy is explicitly decided. Commit converter source/tests only.

```bash
git add tools/AssetConverter/
git commit -m "feat: batch generate compiled Godot animations"
```

---

## Task 6: Convert uncompiled effect/spell animations

**Files:**
- Modify: `tools/AssetConverter/src/AssetConverter/SpriteFrames/AnimationBatchConverter.cs`
- Test: `tools/AssetConverter/tests/AssetConverter.Tests/EffectAnimationConverterTests.cs`

**Mutation impact:**
- Source of truth changed: additional generated `.tres` resources for ADF animations not referenced by `compiled.enc`.
- Important readers: future spell/emote/effect scene loaders; Unity behavior at `ToolsMenu.cs:334-355` labels these as `Spell-<animation.Id>`.
- Derived/cached state affected: height metadata receives effect animation heights using animation id string keys, matching `ToolsMenu.cs:345-355`.
- Required propagation sequence:
  1. Build a `HashSet<int>` of compiled animation ids while processing compiled character resources, matching `ToolsMenu.cs:283` and `:302`.
  2. Iterate all parsed ADFs with non-null `Animations`.
  3. For each animation id not in the compiled set, emit a single-animation `SpriteFrames` resource.
  4. Write resources under `Assets/Sprites/Effects/<animationId>/animations.tres`.
  5. Add non-64 max heights under key `<animationId>`.
- Invariants to preserve:
  - Compiled character animation ids are not duplicated as effect resources.
  - Effect resources use their source sheet texture path and top-left frame regions.
  - Effects do not write `AnimationToFirstFrame` entries; Unity did not add them in `ToolsMenu.cs:334-355`.
- Observable proof required:
  - Test chooses a known uncompiled ADF animation and asserts an effect resource is written while a known compiled id like `3220` is not.

**Step 1: Identify a stable uncompiled fixture**

Use a test helper that computes it from real data rather than hard-coding a fragile id:

```csharp
private static (int sheet, int animationId) FindFirstUncompiledAnimation()
{
    var compiled = new CompiledEnc(Paths.CompiledEnc);
    var compiledIds = compiled.CompiledAnimations
        .SelectMany(c => c.AnimationIndexes)
        .Where(id => id != 0)
        .ToHashSet();

    foreach (var file in Directory.EnumerateFiles(Paths.IllutiaData, "*.adf").OrderBy(p => p))
    {
        var adf = new AdfFile(file);
        if (adf.Type != AdfType.Graphic || adf.Animations is null) continue;
        foreach (var id in adf.Animations.Keys.OrderBy(id => id))
            if (!compiledIds.Contains(id)) return (adf.FileNumber, id);
    }

    throw new InvalidOperationException("No uncompiled animation fixture found");
}
```

**Step 2: Write failing tests**

```csharp
[Fact]
public void Convert_WritesUncompiledEffectAnimationsAndSkipsCompiledIds()
{
    var (sheet, effectId) = FindFirstUncompiledAnimation();
    var outRoot = Path.Combine(Path.GetTempPath(), "ac_effect_" + Guid.NewGuid().ToString("N"));
    try
    {
        var result = AnimationBatchConverter.Convert(
            Paths.IllutiaData,
            Paths.CompiledEnc,
            outRoot,
            includeEffects: true,
            onlyEffectsFromSheets: new[] { sheet });

        Assert.True(result.EffectsWritten >= 1);
        var effectPath = Path.Combine(outRoot, $"Assets/Sprites/Effects/{effectId}/animations.tres");
        Assert.True(File.Exists(effectPath));
        Assert.Contains($"\"name\": &\"{effectId}\"", File.ReadAllText(effectPath));

        Assert.False(File.Exists(Path.Combine(outRoot, "Assets/Sprites/Effects/3220/animations.tres")));
    }
    finally
    {
        if (Directory.Exists(outRoot)) Directory.Delete(outRoot, recursive: true);
    }
}
```

**Step 3: Run test to verify it fails (red)**

Run:

```bash
dotnet test --filter EffectAnimationConverterTests
```

Expected: compile failure because `includeEffects`, `onlyEffectsFromSheets`, and `EffectsWritten` do not exist.

**Step 4: Implement effect generation**

Evolve the API deliberately:

```csharp
public sealed record AnimationBatchResult(
    int ResourcesWritten,
    int EffectsWritten,
    int Failed,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Failures);

public static AnimationBatchResult Convert(
    string dataDir,
    string compiledEncPath,
    string outRoot,
    Func<CompiledAnimation, bool>? only = null,
    bool includeEffects = false,
    int[]? onlyEffectsFromSheets = null);
```

Postconditions:
- Existing Task 5 tests still pass after updating constructor/record expectations.
- Effects are opt-in in tests but the CLI should run with `includeEffects: true` so full asset generation includes them.

**Step 5: Run tests to verify they pass (green)**

Run:

```bash
dotnet test --filter "AnimationBatchConverterTests|EffectAnimationConverterTests"
```

Expected: PASS.

**Step 6: Commit**

```bash
git add tools/AssetConverter/
git commit -m "feat: generate uncompiled effect animations"
```

---

## Task 7: Add map copy converter and CLI command

**Files:**
- Create: `tools/AssetConverter/src/AssetConverter/Maps/MapCopyConverter.cs`
- Modify: `tools/AssetConverter/src/AssetConverter/Program.cs`
- Test: `tools/AssetConverter/tests/AssetConverter.Tests/MapCopyConverterTests.cs`

**Mutation impact:**
- Source of truth changed: copied map byte files under `Assets/Maps`.
- Important readers: future Godot `MapFile`/map rendering port; Unity copy behavior at `ToolsMenu.cs:122-131`.
- Derived/cached state affected: no cache; copied bytes only.
- Required propagation sequence:
  1. Enumerate source `*.map` files.
  2. For each file, compute output basename as `M{Path.GetFileNameWithoutExtension(file).Substring(1)}.bytes`.
  3. Copy bytes with overwrite.
- Invariants to preserve:
  - `Map100.map` becomes `Map100.bytes`.
  - Bytes are copied exactly.
  - Existing output is overwritten.
- Observable proof required:
  - Temp-dir test asserts output file name and exact byte equality.

**Step 1: Write failing tests**

```csharp
using Goose2.AssetConverter.Maps;
using Xunit;

public class MapCopyConverterTests
{
    [Fact]
    public void Convert_CopiesMapFilesUsingUnityNameRule()
    {
        var source = Path.Combine(Path.GetTempPath(), "maps_src_" + Guid.NewGuid().ToString("N"));
        var output = Path.Combine(Path.GetTempPath(), "maps_out_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(source);
            File.WriteAllBytes(Path.Combine(source, "Map100.map"), new byte[] { 1, 2, 3 });

            var result = MapCopyConverter.Convert(source, output);

            Assert.Equal(1, result.Copied);
            var outPath = Path.Combine(output, "Map100.bytes");
            Assert.True(File.Exists(outPath));
            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(outPath));
        }
        finally
        {
            if (Directory.Exists(source)) Directory.Delete(source, recursive: true);
            if (Directory.Exists(output)) Directory.Delete(output, recursive: true);
        }
    }
}
```

**Step 2: Run test to verify it fails (red)**

Run:

```bash
dotnet test --filter MapCopyConverterTests
```

Expected: compile failure because `MapCopyConverter` does not exist.

**Step 3: Implement converter**

Required API:

```csharp
public sealed record MapCopyResult(int Copied, IReadOnlyList<string> Failures);

public static class MapCopyConverter
{
    public static MapCopyResult Convert(string sourceMapsDir, string outMapsDir);
}
```

Failure behavior:
- Invalid short basenames are recorded as failures and skipped.
- Individual copy exceptions are recorded as failures and conversion continues.

**Step 4: Wire CLI**

Add command:

```csharp
if (args.Length >= 1 && args[0] == "maps")
{
    string outDir = args.Length >= 2
        ? args[1]
        : Path.GetFullPath(Path.Combine("..", "..", "Assets", "Maps"));
    var result = Goose2.AssetConverter.Maps.MapCopyConverter.Convert(Paths.IllutiaMaps, outDir);
    Console.WriteLine($"Copied {result.Copied} maps -> {outDir}");
    foreach (var f in result.Failures) Console.WriteLine($"  FAIL {f}");
    return;
}
```

Update usage string.

**Step 5: Run tests to verify they pass (green)**

Run:

```bash
dotnet test --filter MapCopyConverterTests
```

Expected: PASS.

**Step 6: Commit**

```bash
git add tools/AssetConverter/
git commit -m "feat: copy Illutia map bytes into Godot layout"
```

---

## Task 8: Add one-shot `all` command and final smoke checks

**Files:**
- Modify: `tools/AssetConverter/src/AssetConverter/Program.cs`
- Test: `tools/AssetConverter/tests/AssetConverter.Tests/ProgramCommandSmokeTests.cs` if command parsing has been extracted; otherwise no unit test, use CLI smoke.

**Mutation impact:**
- Source of truth changed: CLI workflow only; generated files are still derived from original Illutia data.
- Important readers: developer workflow and future migration phases.
- Derived/cached state affected: output filesystem from `batch`, `animations`, and `maps` commands.
- Required propagation sequence:
  1. Run sheet PNG batch to `Assets/Sprites/sheets`.
  2. Run compiled/effect animation generation to repo root.
  3. Run map copy to `Assets/Maps`.
  4. Print a summary with counts/failures from all three steps.
- Invariants to preserve:
  - Existing individual commands still work.
  - `all` is rerunnable and overwrites generated text/resources.
  - Generated asset files are not accidentally committed unless explicitly requested.
- Observable proof required:
  - Full CLI smoke produces PNGs, `Body-1` `animations.tres`, metadata files, and map bytes.

**Step 1: Add command**

Add to `Program.cs` before the final usage:

```csharp
if (args.Length >= 1 && args[0] == "all")
{
    string repoRoot = args.Length >= 2
        ? args[1]
        : Path.GetFullPath(Path.Combine("..", ".."));

    var sheetsDir = Path.Combine(repoRoot, "Assets", "Sprites", "sheets");
    var mapsDir = Path.Combine(repoRoot, "Assets", "Maps");

    var sheets = BatchConverter.Convert(Paths.IllutiaData, sheetsDir);
    var animations = Goose2.AssetConverter.SpriteFrames.AnimationBatchConverter.Convert(
        Paths.IllutiaData, Paths.CompiledEnc, repoRoot, includeEffects: true);
    var maps = Goose2.AssetConverter.Maps.MapCopyConverter.Convert(Paths.IllutiaMaps, mapsDir);

    Console.WriteLine($"Sheets: {sheets.Succeeded} ok, {sheets.Failed} failed");
    Console.WriteLine($"Animations: {animations.ResourcesWritten} character, {animations.EffectsWritten} effects, {animations.Failed} failed");
    Console.WriteLine($"Maps: {maps.Copied} copied, {maps.Failures.Count} failed");
    return;
}
```

Update usage to:

```text
Usage: AssetConverter batch [outDir] | frames <id> | animations [repoRoot] | maps [outDir] | all [repoRoot]
```

**Step 2: Run full tests**

Run:

```bash
cd /home/agent/workspace/Goose2ClientGodot/tools/AssetConverter
dotnet test
```

Expected: PASS.

**Step 3: Run full generation smoke**

Run:

```bash
dotnet run --project src/AssetConverter -- all /home/agent/workspace/Goose2ClientGodot
test -f /home/agent/workspace/Goose2ClientGodot/Assets/Sprites/sheets/115.png
test -f /home/agent/workspace/Goose2ClientGodot/Assets/Sprites/Bodies/1/animations.tres
test -f /home/agent/workspace/Goose2ClientGodot/Assets/Resources/AnimationToFirstFrame.txt
test -f /home/agent/workspace/Goose2ClientGodot/Assets/Resources/AnimationHeights.txt
test -f /home/agent/workspace/Goose2ClientGodot/Assets/Maps/Map100.bytes
grep -n '"name": &"walk-left"' /home/agent/workspace/Goose2ClientGodot/Assets/Sprites/Bodies/1/animations.tres
grep -n 'Body-1,115,3205,24,48' /home/agent/workspace/Goose2ClientGodot/Assets/Resources/AnimationToFirstFrame.txt
```

Expected: every `test -f` succeeds and both `grep`s print at least one line.

**Step 4: Optional Godot editor smoke**

1. Open `/home/agent/workspace/Goose2ClientGodot` in Godot 4.6.
2. Let Godot import generated PNGs/resources.
3. Create a throwaway `Node2D` with an `AnimatedSprite2D` child.
4. Assign `Assets/Sprites/Bodies/1/animations.tres`.
5. Play `walk-down`, `idle-down`, `attack-down`, and `walk-equip-down` if present.

Expected: sprites are upright, sliced cleanly, and animate without torn pixels.

**Step 5: Commit source changes only**

```bash
cd /home/agent/workspace/Goose2ClientGodot
git status --short
# Stage converter source/tests. Do not stage generated Assets unless asset policy changes.
git add tools/AssetConverter/ docs/plans/2026-06-04-asset-converter-compiled-animations.md
git commit -m "feat: add full asset conversion command"
```

---

## Invariant-to-test matrix

| Invariant | Proved by |
|---|---|
| `compiled.enc` record parsing preserves Unity enum/order/index layout | `CompiledEnc_HasExpectedRecordCountAndFirstBodyRecord` |
| Sheet-to-animation lookup is populated | `CompiledEnc_SheetToAnimationIndexesAllNonZeroFiles` |
| Godot animation names are deterministic and preserve Goose2 variants | `AnimationNamingTests` |
| Existing `frames <id>` vertical-slice writer remains compatible | Existing `Build_EmitsAtlasRegionsAndAnimationFromTopDownFrames` |
| Multi-sheet `SpriteFrames` emits deduped texture resources and top-left regions | `BuildCompiled_EmitsMultipleAnimationsAcrossMultipleSheets` |
| Body-1 generated resource contains walk/idle aliases and metadata | `BuildCharacterResource_Body1_EmitsWalkIdleAliasesAndMetadata` |
| Missing sheets warn and continue | `BuildCharacterResource_MissingSheetRecordsWarningAndContinues` |
| Metadata text matches Unity-compatible parsing shape | `BuildText_WritesDeterministicUnityCompatibleLines` |
| Metadata duplicate conflicts fail before producing ambiguous output | `MergeFirstFrames_ThrowsOnConflictingDuplicateKey` |
| Batch writes real Body-1 `.tres` and metadata files | `Convert_OnlyBody1_WritesResourceAndMetadata` |
| Uncompiled effects are emitted and compiled ids are not duplicated | `Convert_WritesUncompiledEffectAnimationsAndSkipsCompiledIds` |
| Map copy preserves Unity naming and bytes | `Convert_CopiesMapFilesUsingUnityNameRule` |
| Full workflow is rerunnable from CLI | Task 8 full generation smoke |

---

## Definition of done

- `dotnet test` is green.
- `AssetConverter all /home/agent/workspace/Goose2ClientGodot` writes:
  - sheet PNGs under `Assets/Sprites/sheets`,
  - character `animations.tres` resources under `Assets/Sprites/<TypePlural>/<Id>/`,
  - effect `animations.tres` resources under `Assets/Sprites/Effects/<animationId>/`,
  - `Assets/Resources/AnimationToFirstFrame.txt`,
  - `Assets/Resources/AnimationHeights.txt`,
  - copied maps under `Assets/Maps/*.bytes`.
- `Assets/Sprites/Bodies/1/animations.tres` contains at least `walk-left`, `walk-no-equip-left`, `idle-down`, and specific Goose2 variant names.
- `AnimationToFirstFrame.txt` contains `Body-1,115,3205,24,48`.
- Godot editor can load and play Body-1 `walk-down` from the generated `.tres`.
- Generated asset files are left uncommitted unless the project explicitly decides to check them in.

## Explicitly out of scope

- Implementing the Godot `Character` layered `AnimatedSprite2D` runtime.
- Porting `AnimationManager` into Godot runtime code.
- Deciding generated asset version-control policy.
- Automating Godot import settings/filtering.
- Map rendering or `MapFile` parsing beyond copying source bytes.
