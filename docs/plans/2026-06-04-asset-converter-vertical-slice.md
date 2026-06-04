# Asset Converter Vertical Slice — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Build a standalone C# console tool that decodes the original Illutia `.adf` graphics
into PNGs and Godot `SpriteFrames` resources, proven correct against the Unity client's
known-good output for one sheet end-to-end.

**Architecture:** A separate dotnet console project under `tools/AssetConverter/` (kept out of
the Godot build via `.gdignore`). It ports three pure-C# classes from the Unity Editor
tooling — the `.adf` container parser, the custom GIF/LZW decoder, and the frame-rect model —
swapping Unity's `Texture2D`/`AssetDatabase` output for ImageSharp PNG encoding and
hand-written `.tres` resources. Correctness is anchored by *golden tests*: the Unity client
already generated these exact PNGs and sprite-rects, so we assert byte-for-byte (pixel) and
value-for-value parity against them.

**Tech Stack:** .NET 8 (`net8.0`), xUnit, SixLabors.ImageSharp 3.1.5. Godot 4.6 (target repo)
for the final visual smoke test only.

---

## APIs verified

Source being ported (read in full, paths relative to `~/code/Goose2Client/`):
- `Assets/Scripts/Editor/IllutiaData.cs:110-114` — `enum AdfType { Graphic = 1, Sound = 2 }`
- `Assets/Scripts/Editor/IllutiaData.cs:116-126` — `class Animation { int Id; List<Frame> Frames }`
- `Assets/Scripts/Editor/IllutiaData.cs:128-144` — `class Frame { int Index, X, Y, W, H }`
- `Assets/Scripts/Editor/IllutiaData.cs:146-268` — `class AdfFile` (ctor parses container; `Decode`/`DecodeByte`/`RealSize` helpers). **No UnityEngine dependency.**
- `Assets/Scripts/Editor/GifLoader.cs:1-398` — `class GifLoader` with `static byte[] Load(byte[] input, out int width, out int height)` (`:163`), nested `BitReader` (`:343`), `WriteToOutput` (`:94`), `InitializeCodeTable` (`:74`). Only Unity coupling is the unused `using UnityEngine;` (`:6`).
- `Assets/Scripts/Editor/ToolsMenu.cs:185-213` — `ConvertToPng`: the Unity path does a vertical flip (`:191-195`) before `Texture2D.LoadRawTextureData` + `EncodeToPNG`. We replace this (see orientation note in Task 3).
- `Assets/Scripts/Editor/ToolsMenu.cs:437-450` — sprite-rect generation: `rect = new Rect(frame.X, totalHeight - frame.Y - frame.H, frame.W, frame.H)`. This is the formula linking our top-down `Frame` coords to the Unity `.meta` oracle.

ImageSharp 3.x public API used:
- `Image.LoadPixelData<Rgba32>(ReadOnlySpan<byte> data, int width, int height) -> Image<Rgba32>`
- `Image.Load<Rgba32>(string path) -> Image<Rgba32>`
- `Image<Rgba32>.SaveAsPng(string path)`
- `Image<Rgba32>` indexer `this[int x, int y] -> Rgba32` (fields `.R .G .B .A`, each `byte`)

Reference for the `.tres` output shape (a working Godot project):
- `~/code/3dMMO-Server/client/Assets/Sprites/Bodies/1/animations.tres` — `SpriteFrames` with `AtlasTexture` sub-resources (`region = Rect2(x, y, w, h)`) and an `animations` array (`name`, `speed`, `loop`, `frames[].texture`).

---

## Ground-truth oracle (fixture: sheet 1000)

`~/code/Illutia/data/1000.adf` → `~/code/Goose2Client/Assets/Spritesheets/1000.png`
(96×256 RGBA, 8 frames, each 48×64). Frame coords below are **top-down** (origin top-left),
as `AdfFile` produces them. Derived from `1000.png.meta` via `frame.Y = 256 - meta.y - 64`:

| Frames[i] | Index  | X  | Y   | W  | H  |
|-----------|--------|----|-----|----|----|
| 0         | 108760 | 0  | 0   | 48 | 64 |
| 1         | 108761 | 48 | 0   | 48 | 64 |
| 2         | 108762 | 0  | 64  | 48 | 64 |
| 3         | 108763 | 48 | 64  | 48 | 64 |
| 4         | 108764 | 0  | 128 | 48 | 64 |
| 5         | 108765 | 48 | 128 | 48 | 64 |
| 6         | 108766 | 0  | 192 | 48 | 64 |
| 7         | 108767 | 48 | 192 | 48 | 64 |

`FirstFrameIndex = 108760`, `FrameCount = 8`, `Type = Graphic`.

---

## Conventions for the implementer

- **Ports are verbatim copies + cited edits.** Where a task says "port `X`", copy the source
  file's class body exactly and apply only the listed edits. Do not "improve" the LZW/GIF
  logic — it is load-bearing and matched against golden output.
- **Fixture paths are absolute** and live outside this repo (the original data + the Unity
  project). They are centralized in `Paths.cs` (Task 0) so they're easy to change.
- One failing test → minimal code → green → commit. See @superpowers:executing-plans.

---

### Task 0: Scaffold the converter project

**Files:**
- Create: `tools/.gdignore` (empty file — makes Godot's editor ignore this folder)
- Create: `tools/AssetConverter/AssetConverter.sln`
- Create: `tools/AssetConverter/src/AssetConverter/AssetConverter.csproj`
- Create: `tools/AssetConverter/src/AssetConverter/Paths.cs`
- Create: `tools/AssetConverter/src/AssetConverter/Program.cs`
- Create: `tools/AssetConverter/tests/AssetConverter.Tests/AssetConverter.Tests.csproj`

**Step 1: Create the projects and solution**

Run:
```bash
cd ~/code/Goose2ClientGodot
mkdir -p tools/AssetConverter
touch tools/.gdignore
cd tools/AssetConverter
dotnet new console -n AssetConverter -o src/AssetConverter -f net8.0
dotnet new xunit -n AssetConverter.Tests -o tests/AssetConverter.Tests -f net8.0
dotnet new sln -n AssetConverter
dotnet sln add src/AssetConverter/AssetConverter.csproj
dotnet sln add tests/AssetConverter.Tests/AssetConverter.Tests.csproj
dotnet add src/AssetConverter/AssetConverter.csproj package SixLabors.ImageSharp --version 3.1.5
dotnet add tests/AssetConverter.Tests/AssetConverter.Tests.csproj reference src/AssetConverter/AssetConverter.csproj
dotnet add tests/AssetConverter.Tests/AssetConverter.Tests.csproj package SixLabors.ImageSharp --version 3.1.5
```

**Step 2: Add `Paths.cs`** (centralized fixture locations)

```csharp
namespace Goose2.AssetConverter;

/// <summary>Absolute locations of the original game data and the Unity client's
/// known-good generated output (used as test oracles). Change here if the repos move.</summary>
public static class Paths
{
    public const string IllutiaData = "/home/hayden/code/Illutia/data";
    public const string IllutiaMaps = "/home/hayden/code/Illutia/maps";
    public const string UnitySpritesheets =
        "/home/hayden/code/Goose2Client/Assets/Spritesheets";

    public static string Adf(int fileNumber) => Path.Combine(IllutiaData, $"{fileNumber}.adf");
    public static string UnityPng(int fileNumber) =>
        Path.Combine(UnitySpritesheets, $"{fileNumber}.png");
}
```

**Step 3: Replace `Program.cs` with a stub that builds**

```csharp
using Goose2.AssetConverter;

Console.WriteLine($"AssetConverter. Data dir: {Paths.IllutiaData}");
```

**Step 4: Verify it builds**

Run: `cd ~/code/Goose2ClientGodot/tools/AssetConverter && dotnet build`
Expected: `Build succeeded`, 0 errors.

**Step 5: Commit**

```bash
cd ~/code/Goose2ClientGodot
git add tools/
git commit -m "chore: scaffold AssetConverter console tool + tests"
```

---

### Task 1: Port the `.adf` parser

**Files:**
- Create: `tools/AssetConverter/src/AssetConverter/Adf/IllutiaData.cs`
- Test: `tools/AssetConverter/tests/AssetConverter.Tests/AdfFileTests.cs`

**Step 1: Write the failing test** (asserts against the oracle table)

```csharp
using Goose2.AssetConverter;
using Goose2.AssetConverter.Adf;
using Xunit;

public class AdfFileTests
{
    [Fact]
    public void Sheet1000_HasExpectedFramesMatchingUnityMeta()
    {
        var adf = new AdfFile(Paths.Adf(1000));

        Assert.Equal(1000, adf.FileNumber);
        Assert.Equal(AdfType.Graphic, adf.Type);
        Assert.Equal(8, adf.FrameCount);
        Assert.Equal(108760, adf.FirstFrameIndex);
        Assert.Equal(8, adf.Frames.Count);

        var f0 = adf.Frames[0];
        Assert.Equal((108760, 0, 0, 48, 64), (f0.Index, f0.X, f0.Y, f0.W, f0.H));

        var f2 = adf.Frames[2];
        Assert.Equal((108762, 0, 64, 48, 64), (f2.Index, f2.X, f2.Y, f2.W, f2.H));

        var f7 = adf.Frames[7];
        Assert.Equal((108767, 48, 192, 48, 64), (f7.Index, f7.X, f7.Y, f7.W, f7.H));
    }

    [Fact]
    public void DecodedFileData_LooksLikeAGif()
    {
        var adf = new AdfFile(Paths.Adf(1000));
        // After the per-byte de-offset + 790-byte de-interleave, the payload is a GIF.
        Assert.True(adf.FileData.Length > 6);
        Assert.Equal((byte)'G', adf.FileData[0]);
        Assert.Equal((byte)'I', adf.FileData[1]);
        Assert.Equal((byte)'F', adf.FileData[2]);
    }
}
```

**Step 2: Run test to verify it fails**

Run: `cd ~/code/Goose2ClientGodot/tools/AssetConverter && dotnet test --filter AdfFileTests`
Expected: FAIL — `AdfFile` / `AdfType` / `Frame` do not exist (compile error).

**Step 3: Port the parser**

Copy `~/code/Goose2Client/Assets/Scripts/Editor/IllutiaData.cs` lines **110-268** (the
`AdfType` enum, `Animation`, `Frame`, and `AdfFile` classes — **omit** `CompiledEnc` at
`:63-108` and the three `enum`s at `:10-43`; those belong to the next plan) into the new file.
Apply exactly these edits:

- Change the namespace from `Goose2Client.Assets.Scripts.Editor` to `Goose2.AssetConverter.Adf`.
- Keep `using System;`, `using System.Collections.Generic;`, `using System.IO;`. Drop
  `System.Collections`, `System.Linq`, `System.Text.RegularExpressions` (unused here).
- Make no logic changes. The de-offset (`DecodeByte`), the `RealSize` 790-byte de-interleave
  (`:247-250`, `:218-224`), and the frame loop (`:186-193`) must stay byte-identical.

The file must expose public `AdfFile(string file)`, and public properties `FileNumber`, `Type`,
`FrameCount`, `FirstFrameIndex`, `Frames` (`List<Frame>`), `Animations` (`Dictionary<int, Animation>?`),
`FileData` (`byte[]`). `Frame` exposes public `Index, X, Y, W, H`.

**Step 4: Run test to verify it passes**

Run: `dotnet test --filter AdfFileTests`
Expected: PASS (2 tests).

> If `Sheet1000_HasExpectedFrames...` fails on the X/Y values, the bug is in the port (a
> changed read order or a dropped `Decode` call), **not** the oracle — the table is derived
> directly from Unity's own `.meta`. Re-diff against the cited source lines.

**Step 5: Commit**

```bash
git add tools/AssetConverter/
git commit -m "feat: port .adf container parser with golden frame test"
```

---

### Task 2: Port the GIF/LZW decoder

**Files:**
- Create: `tools/AssetConverter/src/AssetConverter/Gif/GifLoader.cs`
- Test: `tools/AssetConverter/tests/AssetConverter.Tests/GifLoaderTests.cs`

**Step 1: Write the failing test** (dimensions only — pixels are verified in Task 3)

```csharp
using Goose2.AssetConverter;
using Goose2.AssetConverter.Adf;
using Goose2.AssetConverter.Gif;
using Xunit;

public class GifLoaderTests
{
    [Fact]
    public void Sheet1000_DecodesTo96x256Rgba()
    {
        var adf = new AdfFile(Paths.Adf(1000));

        var rgba = GifLoader.Load(adf.FileData, out int width, out int height);

        Assert.Equal(96, width);
        Assert.Equal(256, height);
        Assert.Equal(96 * 256 * 4, rgba.Length);
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test --filter GifLoaderTests`
Expected: FAIL — `GifLoader` does not exist.

**Step 3: Port the decoder**

Copy `~/code/Goose2Client/Assets/Scripts/Editor/GifLoader.cs` lines **1-398** verbatim into the
new file. Apply exactly these edits:

- Remove `using UnityEngine;` (`:6`) and `using System.Runtime.InteropServices;` (`:5`) — both unused.
- Change `namespace Goose2Client` to `namespace Goose2.AssetConverter.Gif`.
- Make the class `public` (`public class GifLoader`) so the test/tool can call it.
- No other changes. The LZW table handling, `BitReader`, interlace logic, and the illutia
  transparency hack (`:102`, `a = 0` when `r<=1 && g==0 && b==0`) must stay byte-identical.

**Step 4: Run test to verify it passes**

Run: `dotnet test --filter GifLoaderTests`
Expected: PASS.

**Step 5: Commit**

```bash
git add tools/AssetConverter/
git commit -m "feat: port adf GIF/LZW decoder"
```

---

### Task 3: PNG output + golden image test (the key validation)

This is the task that proves the whole decode chain: a pixel-for-pixel match against the PNG
Unity already produced from the same `.adf`.

**Files:**
- Create: `tools/AssetConverter/src/AssetConverter/Png/PngWriter.cs`
- Test: `tools/AssetConverter/tests/AssetConverter.Tests/GoldenImageTests.cs`

**Orientation note (no flip):** Unity's `ConvertToPng` flips rows (`ToolsMenu.cs:191-195`) into a
bottom-up `Texture2D`, and `EncodeToPNG` then writes top-down — the two flips cancel, so the
PNG on disk is in the **same orientation as `GifLoader.Load`'s output**. Therefore `PngWriter`
writes the buffer directly, with **no flip**. The golden test is the arbiter: if it reports a
vertical mirror (top rows equal bottom rows), reverse this decision by flipping rows in
`PngWriter` — that is the only orientation degree of freedom.

**Step 1: Write the failing test**

```csharp
using Goose2.AssetConverter;
using Goose2.AssetConverter.Adf;
using Goose2.AssetConverter.Gif;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

public class GoldenImageTests
{
    [Fact]
    public void Sheet1000_PixelMatchesUnityPng()
    {
        var adf = new AdfFile(Paths.Adf(1000));
        var rgba = GifLoader.Load(adf.FileData, out int w, out int h);

        using var expected = Image.Load<Rgba32>(Paths.UnityPng(1000));
        Assert.Equal(w, expected.Width);
        Assert.Equal(h, expected.Height);

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                Rgba32 e = expected[x, y];
                int i = (y * w + x) * 4;
                byte ar = rgba[i], ag = rgba[i + 1], ab = rgba[i + 2], aa = rgba[i + 3];

                // Both fully transparent → equal regardless of RGB (Unity's
                // alphaIsTransparency may bleed color under transparent pixels).
                if (e.A == 0 && aa == 0) continue;

                if (e.A != aa || e.R != ar || e.G != ag || e.B != ab)
                    Assert.Fail(
                        $"Pixel ({x},{y}) expected RGBA({e.R},{e.G},{e.B},{e.A}) " +
                        $"got RGBA({ar},{ag},{ab},{aa})");
            }
        }
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test --filter GoldenImageTests`
Expected: FAIL — `PngWriter` referenced by the tool doesn't exist yet *(this test doesn't call
PngWriter directly, so it may instead pass already if Tasks 1-2 are correct; if it passes, that
is the success signal — proceed to Step 3 to add PngWriter for the batch task, then commit).*

**Step 3: Add `PngWriter`** (used by the tool in Task 4)

```csharp
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Goose2.AssetConverter.Png;

public static class PngWriter
{
    /// <summary>Writes a top-down RGBA8 buffer to a PNG. No vertical flip — see Task 3
    /// orientation note: matches Unity's on-disk PNG orientation.</summary>
    public static void Write(byte[] rgba, int width, int height, string path)
    {
        using var image = Image.LoadPixelData<Rgba32>(rgba, width, height);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        image.SaveAsPng(path);
    }
}
```

**Step 4: Run the full suite to verify everything passes**

Run: `dotnet test`
Expected: PASS (all tests across Tasks 1-3).

**Step 5: Commit**

```bash
git add tools/AssetConverter/
git commit -m "feat: PNG writer + golden pixel test vs Unity output"
```

---

### Task 4: Batch-convert all `.adf` graphics → PNG

Scales the proven decode to the full data set, logging any sheet that fails to decode (no
silent drops — the Unity converter wrapped this in try/catch and some `.adf`s may be sound,
not graphics).

**Files:**
- Create: `tools/AssetConverter/src/AssetConverter/BatchConverter.cs`
- Modify: `tools/AssetConverter/src/AssetConverter/Program.cs`
- Test: `tools/AssetConverter/tests/AssetConverter.Tests/BatchConverterTests.cs`

**Step 1: Write the failing test** (drive a tiny batch into a temp dir)

```csharp
using Goose2.AssetConverter;
using Xunit;

public class BatchConverterTests
{
    [Fact]
    public void Convert_WritesPngForGraphicSheet_AndReportsResults()
    {
        var outDir = Path.Combine(Path.GetTempPath(), "ac_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = BatchConverter.Convert(Paths.IllutiaData, outDir, onlyFileNumbers: new[] { 1000 });

            Assert.Equal(1, result.Succeeded);
            Assert.Equal(0, result.Failed);
            Assert.True(File.Exists(Path.Combine(outDir, "1000.png")));
        }
        finally
        {
            if (Directory.Exists(outDir)) Directory.Delete(outDir, recursive: true);
        }
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test --filter BatchConverterTests`
Expected: FAIL — `BatchConverter` does not exist.

**Step 3: Implement `BatchConverter`**

```csharp
using Goose2.AssetConverter.Adf;
using Goose2.AssetConverter.Gif;
using Goose2.AssetConverter.Png;

namespace Goose2.AssetConverter;

public record BatchResult(int Succeeded, int Failed, IReadOnlyList<string> Failures);

public static class BatchConverter
{
    /// <summary>Decodes every graphic .adf in <paramref name="dataDir"/> to
    /// <paramref name="outDir"/>/&lt;fileNumber&gt;.png. Non-graphic or undecodable files are
    /// counted as failures and listed, never silently skipped.</summary>
    public static BatchResult Convert(string dataDir, string outDir, int[]? onlyFileNumbers = null)
    {
        Directory.CreateDirectory(outDir);
        var failures = new List<string>();
        int ok = 0, fail = 0;

        var only = onlyFileNumbers is null ? null : new HashSet<int>(onlyFileNumbers);

        foreach (var file in Directory.EnumerateFiles(dataDir, "*.adf"))
        {
            int fileNumber = int.Parse(Path.GetFileNameWithoutExtension(file));
            if (only is not null && !only.Contains(fileNumber)) continue;

            try
            {
                var adf = new AdfFile(file);
                if (adf.Type != AdfType.Graphic)
                {
                    fail++; failures.Add($"{fileNumber}: not a graphic ({adf.Type})");
                    continue;
                }

                var rgba = GifLoader.Load(adf.FileData, out int w, out int h);
                PngWriter.Write(rgba, w, h, Path.Combine(outDir, $"{fileNumber}.png"));
                ok++;
            }
            catch (Exception e)
            {
                fail++; failures.Add($"{fileNumber}: {e.GetType().Name} {e.Message}");
            }
        }

        return new BatchResult(ok, fail, failures);
    }
}
```

**Step 4: Run test to verify it passes**

Run: `dotnet test --filter BatchConverterTests`
Expected: PASS.

**Step 5: Wire up `Program.cs`** so the full run is invokable

```csharp
using Goose2.AssetConverter;

if (args.Length >= 1 && args[0] == "batch")
{
    string outDir = args.Length >= 2
        ? args[1]
        : "/home/hayden/code/Goose2ClientGodot/Assets/Sprites/sheets";

    var result = BatchConverter.Convert(Paths.IllutiaData, outDir);
    Console.WriteLine($"Converted {result.Succeeded} sheets, {result.Failed} failures -> {outDir}");
    foreach (var f in result.Failures) Console.WriteLine($"  SKIP {f}");
    return;
}

Console.WriteLine("Usage: AssetConverter batch [outDir]");
```

**Step 6: Run the full batch and sanity-check the output**

Run:
```bash
cd ~/code/Goose2ClientGodot/tools/AssetConverter
dotnet run --project src/AssetConverter -- batch
ls ~/code/Goose2ClientGodot/Assets/Sprites/sheets/*.png | wc -l
```
Expected: thousands of PNGs written; the printed failure list contains only sound/non-graphic
`.adf`s. Spot-check a few PNGs open and look correct (e.g. `1000.png`).

**Step 7: Commit**

```bash
cd ~/code/Goose2ClientGodot
git add tools/AssetConverter/
# NOTE: do NOT commit the generated Assets/Sprites/sheets/*.png in this task; that volume
# decision is handled when wiring Godot import. Add a .gitignore entry if needed.
git commit -m "feat: batch .adf -> png converter with failure reporting"
```

---

### Task 5: Emit a `SpriteFrames` `.tres` and view it in Godot

Proves the Godot-native target format end-to-end: slice one sheet's frames into `AtlasTexture`s
and an `AnimatedSprite2D`-playable `SpriteFrames`, then confirm it animates in the editor. This
deliberately ignores the per-character animation tables (directions/states/equipment) — that is
the next plan. Here all of sheet 1000's frames become a single looping animation named `all`.

**Files:**
- Create: `tools/AssetConverter/src/AssetConverter/SpriteFrames/SpriteFramesWriter.cs`
- Modify: `tools/AssetConverter/src/AssetConverter/Program.cs`
- Test: `tools/AssetConverter/tests/AssetConverter.Tests/SpriteFramesWriterTests.cs`

**Step 1: Write the failing test** (assert the emitted `.tres` text shape)

```csharp
using Goose2.AssetConverter.Adf;
using Goose2.AssetConverter.SpriteFrames;
using Xunit;

public class SpriteFramesWriterTests
{
    [Fact]
    public void Build_EmitsAtlasRegionsAndAnimationFromTopDownFrames()
    {
        var adf = new AdfFile(Goose2.AssetConverter.Paths.Adf(1000));

        string tres = SpriteFramesWriter.Build(adf, texturePath: "res://Assets/Sprites/sheets/1000.png");

        Assert.Contains("[gd_resource type=\"SpriteFrames\" format=3", tres);
        Assert.Contains("path=\"res://Assets/Sprites/sheets/1000.png\"", tres);
        // First frame is top-left 48x64 (Frames[0] = index 108760 @ 0,0).
        Assert.Contains("region = Rect2(0, 0, 48, 64)", tres);
        // Last frame is at (48, 192).
        Assert.Contains("region = Rect2(48, 192, 48, 64)", tres);
        Assert.Contains("\"name\": &\"all\"", tres);
        Assert.Contains("\"loop\": true", tres);
        // 8 frames → 8 AtlasTexture sub-resources.
        Assert.Equal(8, System.Text.RegularExpressions.Regex.Matches(tres, "type=\"AtlasTexture\"").Count);
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test --filter SpriteFramesWriterTests`
Expected: FAIL — `SpriteFramesWriter` does not exist.

**Step 3: Implement `SpriteFramesWriter`**

Godot's `AtlasTexture.region` is top-left origin, Y-down — i.e. exactly our `Frame` coords, so
regions are written directly with no conversion.

```csharp
using System.Text;
using Goose2.AssetConverter.Adf;

namespace Goose2.AssetConverter.SpriteFrames;

public static class SpriteFramesWriter
{
    /// <summary>Builds a Godot 4 SpriteFrames .tres putting all of the adf's frames into a
    /// single looping animation "all". Region coords are top-down (Godot AtlasTexture origin),
    /// matching Frame.X/Y directly.</summary>
    public static string Build(AdfFile adf, string texturePath, float speed = 8f)
    {
        var sb = new StringBuilder();
        int n = adf.Frames.Count;

        sb.AppendLine("[gd_resource type=\"SpriteFrames\" format=3]");
        sb.AppendLine();
        sb.AppendLine($"[ext_resource type=\"Texture2D\" path=\"{texturePath}\" id=\"1\"]");
        sb.AppendLine();

        for (int i = 0; i < n; i++)
        {
            var f = adf.Frames[i];
            sb.AppendLine($"[sub_resource type=\"AtlasTexture\" id=\"Atlas_{i}\"]");
            sb.AppendLine("atlas = ExtResource(\"1\")");
            sb.AppendLine($"region = Rect2({f.X}, {f.Y}, {f.W}, {f.H})");
            sb.AppendLine();
        }

        sb.AppendLine("[resource]");
        sb.AppendLine("animations = [{");
        sb.Append("\"frames\": [");
        for (int i = 0; i < n; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append($"{{\"duration\": 1.0, \"texture\": SubResource(\"Atlas_{i}\")}}");
        }
        sb.AppendLine("],");
        sb.AppendLine("\"loop\": true,");
        sb.AppendLine("\"name\": &\"all\",");
        sb.AppendLine($"\"speed\": {speed:0.0}");
        sb.AppendLine("}]");

        return sb.ToString();
    }
}
```

**Step 4: Run test to verify it passes**

Run: `dotnet test --filter SpriteFramesWriterTests`
Expected: PASS.

**Step 5: Add a `frames <id>` command and generate the demo resource**

Add to `Program.cs` (before the final usage line):

```csharp
if (args.Length >= 2 && args[0] == "frames")
{
    int id = int.Parse(args[1]);
    var adf = new Goose2.AssetConverter.Adf.AdfFile(Paths.Adf(id));
    string tres = Goose2.AssetConverter.SpriteFrames.SpriteFramesWriter.Build(
        adf, $"res://Assets/Sprites/sheets/{id}.png");
    string outPath = $"/home/hayden/code/Goose2ClientGodot/Assets/Sprites/sheets/{id}.frames.tres";
    Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
    File.WriteAllText(outPath, tres);
    Console.WriteLine($"Wrote {outPath}");
    return;
}
```

Run:
```bash
cd ~/code/Goose2ClientGodot/tools/AssetConverter
dotnet run --project src/AssetConverter -- batch        # if not already run
dotnet run --project src/AssetConverter -- frames 1000
```
Expected: `Assets/Sprites/sheets/1000.png` and `1000.frames.tres` exist.

**Step 6: Manual Godot smoke test**

1. Open `~/code/Goose2ClientGodot` in Godot 4.6; let it import `Assets/Sprites/sheets/1000.png`
   (it must import as a Texture2D — point filter looks best: set Filter off in the import dock).
2. Create a throwaway scene with a `Node2D` → `AnimatedSprite2D` child.
3. In the AnimatedSprite2D inspector, set **Sprite Frames** to `1000.frames.tres`, set
   Animation to `all`, enable **Playing**.
4. Confirm: 8 frames cycle, each a clean 48×64 sprite (no garbage/torn pixels, correct
   orientation — the character is upright, not upside-down).

Expected: a recognizable sprite animates. This validates `.adf` → PNG → `AtlasTexture` →
`SpriteFrames` → `AnimatedSprite2D` end-to-end, which is the foundation the §5 layered
animation system consumes.

**Step 7: Commit**

```bash
cd ~/code/Goose2ClientGodot
git add tools/AssetConverter/
git commit -m "feat: emit Godot SpriteFrames .tres from adf frames"
```

---

## Definition of done

- `dotnet test` is green (AdfFile, GifLoader, GoldenImage, BatchConverter, SpriteFramesWriter).
- The golden test proves the decode is pixel-identical to Unity's output for sheet 1000.
- A full `batch` run converts the bulk of `~/code/Illutia/data` with only sound/non-graphic
  `.adf`s reported as failures.
- A `SpriteFrames` `.tres` plays in Godot's editor as a clean, upright sprite animation.

## Explicitly out of scope (next plan)

- `compiled.enc` parsing and the per-character animation tables (`AnimationIndexes` /
  `AnimationFiles`) that map body/equipment graphics to the 11 actions × 4 directions.
- `AnimationToFirstFrame.txt` / `AnimationHeights.txt` (foot-offset / height metadata sidecar).
- Real `{state}-{direction}` animation naming, and the layered multi-`AnimatedSprite2D`
  paper-doll assembly (§5 of `MIGRATION_PLAN.md`).
- Map (`.map`) conversion.
- Godot import settings automation and committing the generated asset volume.
