# Aspereta → Illutia Asset Pipeline Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Extend `tools/AssetConverter` so Aspereta's graphics and maps are converted into the Goose2/Illutia asset space — reusing pixel-identical Illutia frames where they exist, injecting the rest under collision-free IDs, converting Aspereta maps to the Goose2 binary map format, and building Godot animation resources for Aspereta's 66 monsters.

**Architecture:** Three new offline components inside the existing AssetConverter tool: (1) an Aspereta `.adf` decoder + a deterministic frame-matching step that emits a committed mapping table (`asp graphic → reuse Illutia graphic | inject as 700000+id`); (2) an Aspereta map converter that rewrites the 100×100/4-layer Aspereta map format into the Goose2 5-layer format using that table; (3) a monster-animation synthesizer that reshapes Aspereta's 4×8 `compiled.enc` entries into Illutia-layout `CompiledAnimation` objects and feeds them through the existing `CompiledAnimationBuilder`/`SpriteFramesWriter` pipeline. All output lands in the gitignored `Assets/` tree exactly like the Illutia pipeline; only the mapping table (a decision record) is committed.

**Tech Stack:** .NET 10 console tool (`tools/AssetConverter`), SixLabors.ImageSharp, xUnit. No Godot-side code changes.

**Server-data remapping (graphic/body/map-number rewrites in GooseServer2 data) is explicitly out of scope — separate plan.**

---

## Background facts (verified against real data — do not re-derive)

These were established by decoding both complete datasets and pixel-hashing every frame:

- Aspereta: 487 graphic `.adf` sheets, 16,139 frames. Illutia: 4,956 sheets, 65,046 frames.
- **3,079 Aspereta frames are byte-identical (RGB + transparency) to an Illutia frame; 13,060 are not** and must be injected. Expect exactly these counts from the matching step; a differing count means a decoder bug.
- Aspereta `.adf` payloads are **BMP**; Illutia's are **GIF**. Same byte-obfuscation and 790-byte interleave, different header layout (see Task 1).
- Transparency in the original Aspereta client is a **constant black color key** — `AsperetaCS-1.8/adfDecode.bas` sets `ddck.low = 0 : ddck.high = 0` on every surface. This is behaviorally identical to the Godot pipeline's GIF rule `r <= 1 && g == 0 && b == 0` (`Gif/GifLoader.cs:100`): no Aspereta frame that matches an Illutia frame contains an `(1,0,0)` pixel (verified exhaustively). Do NOT use a top-left-pixel key (that is a known bug in the gooseclient reimplementation).
- The two games' graphic-ID and sheet-number spaces are unrelated (Illutia `1.adf` is a font; Aspereta `1.adf` is bodies). Illutia graphic IDs occupy up to 621,709; Illutia sheet numbers occupy 0–4,962; Illutia body IDs occupy 1–308.
- Aspereta `compiled.enc` has 241 entries: 72 Body (6 players ≤100, **66 monsters >100**), the rest player equipment (discarded — Illutia's own equipment art is used instead).
- Aspereta maps: 44 files at `maps/Map<N>.map`, fixed 100×100, 4 layers, with a small trailing blob after the tile grid that the client ignores (`Map1.map` is 170,264 bytes; the tile grid is 4 + 100×100×17 = 170,004).

### ID schemes (fixed, deterministic — used by this plan and the future server-data plan)

| Space | Rule | Why safe |
|---|---|---|
| Injected graphic IDs | `700000 + aspGraphicId` | Illutia max graphic ID is 621,709; graphic is `Int32` in maps/manifest |
| Injected sheet numbers | `20000 + rank` where rank = index of the Aspereta file number in the ascending sorted list of all Aspereta **graphic** adf file numbers (0-based) | Sheet is `Int16` in the map format (`Scripts/MapFile.cs:81`); Illutia uses ≤4,962; 20000+486 = 20486 < 32767. Raw Aspereta file numbers can't be used: they collide with Illutia (1.adf) AND overflow Int16 (50008.adf) |
| Monster body IDs | `10000 + aspBodyId` (→ 10101–10166) | Illutia body IDs ≤ 308 |
| Map numbers | `Map<N>.map` → `Map<10000+N>.bytes` | Aspereta and Illutia both have a Map1 etc. |

### APIs verified (path:line)

- `AdfFile` (Illutia): properties all `{ get; set; }` — `tools/AssetConverter/src/AssetConverter/Adf/IllutiaData.cs:147-162`; only ctor is `AdfFile(string file)` at `:164` (Task 0 adds a parameterless one). `Frame` ctor `(index, x, y, w, h)` at `:133-144` of the same file (fields `Index, X, Y, W, H`). `Animation` has `Frames` list (`:60` region), `AdfType.Graphic` enum at `:111`.
- `CompiledAnimation`: ctor `(AnimationType type, int id)`, public arrays `AnimationIndexes` (`int[4*11]`, indexed `direction * 11 + order`) and `AnimationFiles` (`int[11]`, indexed by order) — `Adf/IllutiaData.cs:42-58`. Direction index order is **left=0, down=1, right=2, up=3** (`SpriteFrames/AnimationNaming.cs:13-23`).
- `AnimationOrder` (Illutia, 11 values): `WalkingNoEquip=0 … AttackNoEquip=2 … Mounted=10` — `Adf/IllutiaData.cs:15-28`.
- `CompiledAnimationBuilder.BuildCharacterResource(CompiledAnimation, IReadOnlyDictionary<int, AdfFile>)` — `SpriteFrames/CompiledAnimationBuilder.cs:14-16`. It reads `AnimationFiles[order]` as the adf-dictionary key AND bakes it into the texture path `res://Assets/Sprites/sheets/{fileNumber}.png` (`:30, :45`), so Aspereta AdfFiles handed to it must already carry their **renumbered** sheet number. Zero file / zero animation index slots produce warnings, not errors (`:32-43, :52-56`).
- `SpriteFramesWriter.Build(IReadOnlyList<SpriteFramesAnimationSpec>)` → `.tres` text — used at `SpriteFrames/AnimationBatchConverter.cs:116`.
- `AnimationBatchConverter.Convert(dataDir, compiledEncPath, outRoot, only, includeEffects, onlyEffectsFromSheets)` — `SpriteFrames/AnimationBatchConverter.cs:35-41`; its step 6 (`:179-199`) writes merged metadata via `AnimationMetadataWriter.MergeFirstFrames/MergeHeights/Write` (`SpriteFrames/AnimationMetadataWriter.cs:18-48`, merge throws on conflicting values, tolerates equal duplicates). `CompiledSpriteFramesResource` carries `Animations`, `AnimationToFirstFrame`, `AnimationHeights`, `RelativeOutputPath`, `Warnings` (`CompiledAnimationBuilder.cs:130-137`).
- `BatchConverter.Convert(dataDir, outDir, onlyFileNumbers?)` → `BatchResult(Succeeded, Failed, Failures)` — `BatchConverter.cs:17`. Its private `DecodeGraphicPayload` dispatches GIF→`GifLoader.Load(payload, out w, out h)` + `PngWriter.Write(rgba, w, h, path)` and BMP→ImageSharp **without alpha keying** (`BatchConverter.cs:58-82`). The BMP branch must NOT be reused for Aspereta output (no keying); Task 0 adds a shared keyed decoder.
- `GifLoader.Load(byte[] payload, out int width, out int height)` → top-down RGBA8 `byte[]`, alpha rule `r <= 1 && g == 0 && b == 0 → 0` — `Gif/GifLoader.cs:100`.
- `PngWriter.Write(byte[] rgba, int width, int height, string path)` — `Png/PngWriter.cs:11`.
- `FrameManifestBuilder.Build(string dataDir, int[]? onlyFileNumbers)` → JSON `{ "tileSize": 32, "sheets": { "<sheet>": { "<graphic>": [x,y,w,h] } } }` — `Manifest/FrameManifestBuilder.cs:11-35`. Task 4 refactors it for multi-source composition.
- `MapCopyConverter.Convert(sourceMapsDir, outMapsDir)` naming rule `Map100.map → Map100.bytes` — `Maps/MapCopyConverter.cs:12-49`. (Illutia maps are already Goose2-format; Aspereta maps are NOT and need Task 5's real converter.)
- Goose2 map binary format (the conversion TARGET), from the Godot client's parser `Scripts/MapFile.cs:59-90`: `Int16 Version, Int16 EditorVersion, Int32 Width, Int32 Height`, then per tile (row-major, y outer): `Int32 Flags`, then 5 × (`Int32 Graphic`, `Int16 Sheet`). `IsBlocked = (Flags & 2) > 0` (`:26-29`), roof = layer index 4 (`:31-34`).
- Aspereta map format (the SOURCE), from `~/code/gooseclient/AsperetaClient/AsperetaMapLoader.cs:14-35`: `Int16, Int16` header (observed values 65, 3), then 100×100 tiles (y outer): `Byte blocked` (1 = blocked), `4 × Int32 graphicId`. Trailing bytes after the grid are ignored.
- Aspereta `compiled.enc` (SOURCE), from `~/code/gooseclient/AsperetaClient/CompiledEnc.cs:80-106`: repeated records `Int16 type` (1-based; 1=Body), `Int32 id`, `32 × Int32 animationIndexes`. **No file-number table** (unlike Illutia). Runtime indexing from `~/code/gooseclient/AsperetaClient/Character.cs:185-187`: `index = (attacking ? 16 : 0) + (bodyState - 1) + (int)facing * 4`, with `Direction` enum **Up=0, Right=1, Down=2, Left=3** (`Character.cs:8-14`). Monsters (BodyId ≥ 100) always use bodyState = 1 (`Character.cs:186`), i.e. column 0.
- Aspereta `.adf` decode algorithm (SOURCE), from `~/code/gooseclient/AsperetaClient/AdfFile.cs:53-146` — full port in Task 1.
- Test conventions: xUnit `[Fact]` classes running against the real datasets via `Paths` (`tests/AssetConverter.Tests/AdfFileTests.cs:10-29` asserts exact frame rects of Illutia sheet 1000). Run with `dotnet test` from `tools/AssetConverter`.

### Source data locations (this machine)

- Illutia data: `/home/hayden/code/Illutia/data` (4,956 `.adf` + `compiled.enc`), maps `/home/hayden/code/Illutia/maps`.
- Aspereta data: `/home/hayden/code/gooseclient/AsperetaClient/bin/Release/net8/data` (974 `.adf`, 487 graphic + `compiled.enc`), maps `/home/hayden/code/gooseclient/AsperetaClient/bin/Release/net8/maps` (44 `Map<N>.map`).
- NOTE: `Paths.cs` currently hardcodes `/home/agent/workspace/...` (a cloud-workspace path). Task 0 adds env-var overrides so both environments work without edits.

---

### Task 0: Prerequisites — Paths, parameterless AdfFile ctor, shared keyed payload decoder

**Files:**
- Modify: `tools/AssetConverter/src/AssetConverter/Paths.cs`
- Modify: `tools/AssetConverter/src/AssetConverter/Adf/IllutiaData.cs:164` (add ctor)
- Create: `tools/AssetConverter/src/AssetConverter/Png/PayloadDecoder.cs`
- Test: `tools/AssetConverter/tests/AssetConverter.Tests/PayloadDecoderTests.cs`

**Step 1: Rewrite Paths with env overrides + Aspereta locations**

Replace the body of `Paths.cs` with:

```csharp
namespace Goose2.AssetConverter;

/// <summary>Absolute locations of the original game data. Defaults suit the cloud
/// workspace; override per-machine with environment variables.</summary>
public static class Paths
{
    private static string Env(string name, string fallback) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : fallback;

    public static string IllutiaData => Env("ILLUTIA_DATA", "/home/agent/workspace/Illutia/data");
    public static string IllutiaMaps => Env("ILLUTIA_MAPS", "/home/agent/workspace/Illutia/maps");
    public static string UnitySpritesheets =>
        Env("UNITY_SPRITESHEETS", "/home/agent/workspace/Goose2Client/Assets/Spritesheets");

    public static string AsperetaData => Env("ASPERETA_DATA",
        "/home/hayden/code/gooseclient/AsperetaClient/bin/Release/net8/data");
    public static string AsperetaMaps => Env("ASPERETA_MAPS",
        "/home/hayden/code/gooseclient/AsperetaClient/bin/Release/net8/maps");

    public static string CompiledEnc => Path.Combine(IllutiaData, "compiled.enc");
    public static string AsperetaCompiledEnc => Path.Combine(AsperetaData, "compiled.enc");

    public static string Adf(int fileNumber) => Path.Combine(IllutiaData, $"{fileNumber}.adf");
    public static string UnityPng(int fileNumber) =>
        Path.Combine(UnitySpritesheets, $"{fileNumber}.png");
}
```

(Consumers use `Paths.IllutiaData` etc. as before — properties instead of consts is source-compatible; `const string` → property is fine because nothing uses them in attributes or switch patterns. Verify with the full build in Step 5.)

On this machine, add to the shell profile or a `.env` you source before running the tool:

```bash
export ILLUTIA_DATA=/home/hayden/code/Illutia/data
export ILLUTIA_MAPS=/home/hayden/code/Illutia/maps
```

(Aspereta defaults already point at this machine's copy.)

**Step 2: Add parameterless AdfFile ctor**

In `Adf/IllutiaData.cs`, directly above the existing `public AdfFile(string file)` (`:164`):

```csharp
    /// <summary>Creates an empty AdfFile for programmatic population (Aspereta import).</summary>
    public AdfFile()
    {
        this.Frames = new List<Frame>();
        this.FileData = Array.Empty<byte>();
        this.ExtraBytes = Array.Empty<byte>();
    }
```

**Step 3: Write the failing test for the keyed decoder**

`tests/AssetConverter.Tests/PayloadDecoderTests.cs`:

```csharp
using Goose2.AssetConverter.Png;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace AssetConverter.Tests;

public class PayloadDecoderTests
{
    private static byte[] BmpBytes(params Rgba32[] pixels)
    {
        using var img = new Image<Rgba32>(pixels.Length, 1);
        for (int i = 0; i < pixels.Length; i++) img[i, 0] = pixels[i];
        using var ms = new MemoryStream();
        img.SaveAsBmp(ms);
        return ms.ToArray();
    }

    [Fact]
    public void Bmp_BlackAndNearBlackBecomeTransparent()
    {
        // black, near-black (1,0,0), and an opaque colour
        var payload = BmpBytes(new Rgba32(0, 0, 0), new Rgba32(1, 0, 0), new Rgba32(200, 50, 50));

        var rgba = PayloadDecoder.ToRgba(payload, out int w, out int h);

        Assert.Equal((3, 1), (w, h));
        Assert.Equal(0, rgba[3]);        // black -> alpha 0
        Assert.Equal(0, rgba[7]);        // (1,0,0) -> alpha 0 (matches GifLoader.cs:100 rule)
        Assert.Equal(255, rgba[11]);     // colour stays opaque
        Assert.Equal(200, rgba[8]);      // rgb preserved
    }

    [Fact]
    public void Gif_DelegatesToGifLoader()
    {
        // Real Illutia GIF payload: sheet 1000 (see AdfFileTests.DecodedFileData_LooksLikeAGif)
        var adf = new Goose2.AssetConverter.Adf.AdfFile(Goose2.AssetConverter.Paths.Adf(1000));
        var expected = Goose2.AssetConverter.Gif.GifLoader.Load(adf.FileData, out int ew, out int eh);

        var actual = PayloadDecoder.ToRgba(adf.FileData, out int aw, out int ah);

        Assert.Equal((ew, eh), (aw, ah));
        Assert.Equal(expected, actual);
    }
}
```

**Step 4: Run tests to verify they fail**

Run: `cd /home/hayden/code/Goose2ClientGodot/tools/AssetConverter && ILLUTIA_DATA=/home/hayden/code/Illutia/data dotnet test --filter PayloadDecoderTests`
Expected: FAIL — `PayloadDecoder` does not exist.

**Step 5: Implement PayloadDecoder**

`src/AssetConverter/Png/PayloadDecoder.cs`:

```csharp
using Goose2.AssetConverter.Gif;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Goose2.AssetConverter.Png;

/// <summary>Decodes a graphic ADF payload (GIF or BMP) to a top-down RGBA8 buffer with the
/// original clients' transparency rule applied: near-black (r&lt;=1, g==0, b==0) is transparent.
/// For GIF this is GifLoader's existing behavior (GifLoader.cs:100); for BMP (Aspereta) the same
/// rule is applied here — the original Aspereta client color-keys on constant black.</summary>
public static class PayloadDecoder
{
    public static byte[] ToRgba(byte[] payload, out int width, out int height)
    {
        if (payload.Length >= 3 && payload[0] == 'G' && payload[1] == 'I' && payload[2] == 'F')
            return GifLoader.Load(payload, out width, out height);

        if (payload.Length >= 2 && payload[0] == 'B' && payload[1] == 'M')
        {
            using var image = Image.Load<Rgba32>(payload);
            width = image.Width;
            height = image.Height;
            var rgba = new byte[width * height * 4];
            image.CopyPixelDataTo(rgba);
            for (int i = 0; i < rgba.Length; i += 4)
            {
                if (rgba[i] <= 1 && rgba[i + 1] == 0 && rgba[i + 2] == 0)
                    rgba[i + 3] = 0;
            }
            return rgba;
        }

        throw new NotSupportedException(
            $"Unsupported payload format (first bytes: 0x{payload.ElementAtOrDefault(0):X2} 0x{payload.ElementAtOrDefault(1):X2})");
    }
}
```

**Step 6: Run tests and full build**

Run: `ILLUTIA_DATA=/home/hayden/code/Illutia/data dotnet test` (from `tools/AssetConverter`)
Expected: PASS, including the whole pre-existing suite (proves the Paths refactor broke nothing).

**Step 7: Commit**

```bash
git add tools/AssetConverter/src/AssetConverter/Paths.cs \
        tools/AssetConverter/src/AssetConverter/Adf/IllutiaData.cs \
        tools/AssetConverter/src/AssetConverter/Png/PayloadDecoder.cs \
        tools/AssetConverter/tests/AssetConverter.Tests/PayloadDecoderTests.cs
git commit -m "feat(assetconverter): env-overridable paths, keyed payload decoder, empty AdfFile ctor"
```

---

### Task 1: Aspereta `.adf` decoder

Ports `~/code/gooseclient/AsperetaClient/AdfFile.cs:53-146` into the tool, emitting Illutia-shaped `AdfFile` objects so every downstream component (manifest builder, animation builder) works unchanged.

**Files:**
- Create: `tools/AssetConverter/src/AssetConverter/Adf/AsperetaAdf.cs`
- Test: `tools/AssetConverter/tests/AssetConverter.Tests/AsperetaAdfTests.cs`

**Format (differences from Illutia, both verified against the respective client sources):**

| | Illutia (`IllutiaData.cs:164-230`) | Aspereta (`gooseclient AdfFile.cs:53-125`) |
|---|---|---|
| After type byte | version byte, then extra block | extra block immediately (no version byte) |
| Offset byte | stored in header after extra block | **derived**: `offset = bytes[^2]`, then adjusted by a header byte: `offset2 = DecodeByte(nextByte, offset); offset = (byte)(offset + offset2)` |
| Frame table | firstIndex + count + separate animation table | count, then per record: `Int32 frameId, Byte n`; `n==1` → frame rect (4×Int32); `n>1` → animation (`n`×Int32 frame ids + Byte interval) |
| Trailer | headerSize Int32 then payload | unknown Int32 (36 ⇒ sound) then payload |
| Payload | GIF | BMP (same de-offset + 790-interleave) |

**Step 1: Write the failing test**

Golden values below were computed with a verified reference decoder against the real data (`asp 1.adf`: 12 frames 1200–1211 of 24×48; frame 1200 at (0,0); payload BMP 41,527 bytes).

`tests/AssetConverter.Tests/AsperetaAdfTests.cs`:

```csharp
using Goose2.AssetConverter;
using Goose2.AssetConverter.Adf;
using Xunit;

namespace AssetConverter.Tests;

public class AsperetaAdfTests
{
    private static string Adf(int n) => Path.Combine(Paths.AsperetaData, $"{n}.adf");

    [Fact]
    public void Sheet1_Bodies_HasExpectedFrames()
    {
        var adf = AsperetaAdf.Load(Adf(1));

        Assert.Equal(1, adf.FileNumber);
        Assert.Equal(AdfType.Graphic, adf.Type);
        Assert.Equal(12, adf.Frames.Count);
        Assert.Equal(1200, adf.FirstFrameIndex);

        var f0 = adf.Frames[0];
        Assert.Equal((1200, 0, 0, 24, 48), (f0.Index, f0.X, f0.Y, f0.W, f0.H));
    }

    [Fact]
    public void Sheet1_PayloadIsBmp()
    {
        var adf = AsperetaAdf.Load(Adf(1));
        Assert.Equal(41527, adf.FileData.Length);
        Assert.Equal((byte)'B', adf.FileData[0]);
        Assert.Equal((byte)'M', adf.FileData[1]);
    }

    [Fact]
    public void AllGraphicSheets_DecodeWithoutError()
    {
        int graphics = 0;
        foreach (var file in Directory.EnumerateFiles(Paths.AsperetaData, "*.adf"))
        {
            var adf = AsperetaAdf.Load(file);          // must not throw on any file
            if (adf.Type == AdfType.Graphic && adf.Frames.Count > 0) graphics++;
        }
        Assert.Equal(487, graphics);                    // known dataset size
    }
}
```

**Step 2: Run to verify failure**

Run: `dotnet test --filter AsperetaAdfTests`
Expected: FAIL — `AsperetaAdf` not defined.

**Step 3: Implement**

`src/AssetConverter/Adf/AsperetaAdf.cs`:

```csharp
namespace Goose2.AssetConverter.Adf;

/// <summary>Decodes Aspereta-layout .adf files into Illutia-shaped <see cref="AdfFile"/>
/// objects. Port of gooseclient AdfFile.cs:53-146 (verified against AsperetaCS-1.8).</summary>
public static class AsperetaAdf
{
    public static AdfFile Load(string file)
    {
        byte[] bytes = File.ReadAllBytes(file);
        using var reader = new BinaryReader(new MemoryStream(bytes));

        var result = new AdfFile
        {
            FileNumber = int.Parse(Path.GetFileNameWithoutExtension(file)),
            Type = (AdfType)reader.ReadByte(),
            Animations = new Dictionary<int, Animation>(),
        };

        // Second-to-last byte of the file is the base de-obfuscation offset.
        byte offset = bytes[^2];

        int extraLength = reader.ReadInt32();
        result.ExtraBytes = reader.ReadBytes(extraLength);

        // Header byte adjusts the offset (gooseclient AdfFile.cs:71-73).
        byte offset2 = DecodeByte(reader.ReadByte(), offset);
        offset = (byte)(offset + offset2);

        int records = reader.ReadInt32() - offset;
        for (int i = 0; i < records; i++)
        {
            int id = reader.ReadInt32() - offset;
            byte n = DecodeByte(reader.ReadByte(), offset);

            if (n == 1)
            {
                int x = reader.ReadInt32() - offset;
                int y = reader.ReadInt32() - offset;
                int w = reader.ReadInt32() - offset;
                int h = reader.ReadInt32() - offset;
                result.Frames.Add(new Frame(id, x, y, w, h));
            }
            else
            {
                var animation = new Animation(id);
                var frameIds = new int[n];
                for (int f = 0; f < n; f++) frameIds[f] = reader.ReadInt32() - offset;
                DecodeByte(reader.ReadByte(), offset); // interval — unused, must still consume
                // Frame objects are resolved after the frame table is complete.
                _pendingAnimations.Add((result, animation, frameIds));
                result.Animations[id] = animation;
            }
        }

        // Resolve animation frame ids -> Frame objects now the table is complete.
        foreach (var (owner, animation, frameIds) in _pendingAnimations)
        {
            if (!ReferenceEquals(owner, result)) continue;
            foreach (int fid in frameIds)
            {
                var frame = result.Frames.FirstOrDefault(fr => fr.Index == fid);
                if (frame is not null) animation.Frames.Add(frame);
            }
        }
        _pendingAnimations.RemoveAll(p => ReferenceEquals(p.Item1, result));

        result.FirstFrameIndex = result.Frames.Count == 0 ? 0 : result.Frames[0].Index;
        result.FrameCount = result.Frames.Count;

        int unknown = reader.ReadInt32() - offset;
        if (unknown == 36) result.Type = AdfType.Sound;

        // Payload: de-offset every byte and remove the 790-byte interleave
        // (identical scheme to Illutia — IllutiaData.cs:218-225).
        int length = (int)(reader.BaseStream.Length - reader.BaseStream.Position);
        byte[] buffer = reader.ReadBytes(length);
        byte[] data = new byte[buffer.Length - (buffer.Length / 790)];
        for (int k = 0; k < buffer.Length; k++)
        {
            int idx = k - (k / 790);
            if (idx >= data.Length) continue;
            data[idx] = DecodeByte(buffer[k], offset);
        }
        result.FileData = data;

        return result;
    }

    private static readonly List<(AdfFile, Animation, int[])> _pendingAnimations = new();

    private static byte DecodeByte(byte data, byte offset) => (byte)(data - offset);
}
```

NOTE for implementer: check `Animation`'s actual ctor/`Frames` surface in `Adf/IllutiaData.cs` (~`:60-108`, class `Animation`) and adapt the two `Animation` lines if its ctor differs — the shape used above (`new Animation(id)`, mutable `Frames` list of `Frame`) is how `CompiledAnimationBuilder.cs:60-66` and `AnimationBatchConverter.cs:141-168` consume it. If `_pendingAnimations` feels awkward, an equivalent local two-pass (collect `(animation, frameIds)` in a local list, resolve after the loop) is cleaner — the static list is only sketched to keep this snippet linear; **prefer the local list**.

**Step 4: Run tests**

Run: `dotnet test --filter AsperetaAdfTests`
Expected: PASS (3 tests). If `AllGraphicSheets_DecodeWithoutError` reports ≠ 487, the decoder diverges from the reference — debug before proceeding, do not adjust the expected count.

**Step 5: Commit**

```bash
git add tools/AssetConverter/src/AssetConverter/Adf/AsperetaAdf.cs \
        tools/AssetConverter/tests/AssetConverter.Tests/AsperetaAdfTests.cs
git commit -m "feat(assetconverter): Aspereta .adf decoder producing Illutia-shaped AdfFile"
```

---

### Task 2: Frame-matching mapping generator + committed mapping table

Produces the single source of truth consumed by Tasks 3–5 and the future server-data plan.

**Files:**
- Create: `tools/AssetConverter/src/AssetConverter/Aspereta/AsperetaMapping.cs`
- Create: `tools/AssetConverter/src/AssetConverter/Aspereta/AsperetaSheets.cs`
- Test: `tools/AssetConverter/tests/AssetConverter.Tests/AsperetaMappingTests.cs`
- Committed output: `tools/AssetConverter/data/aspereta-mapping.tsv`

**Mapping algorithm (mirror of the verified reference implementation):**
1. For every Illutia graphic sheet: decode payload with `PayloadDecoder.ToRgba`, crop each frame rect, key = SHA256 of `w,h` + RGBA bytes → dictionary key→(illGraphic, illSheet). First writer wins (duplicates are pixel-identical by construction, so the choice is arbitrary but deterministic given sorted file enumeration).
2. For every Aspereta graphic sheet (sorted by file number): same hash per frame. Hash present in the Illutia index → `matched`; absent → `inject` with `outGraphic = 700000 + aspGraphic`, `outSheet = 20000 + rank(aspFileNumber)`.
3. Skip degenerate frames (w≤0, h≤0, or rect outside the payload image) — same rule on both sides.

TSV columns: `asp_sheet  asp_graphic  status  out_sheet  out_graphic` (for `matched`, out_* are the Illutia donor's sheet/graphic).

**Step 1: Write the failing test**

`tests/AssetConverter.Tests/AsperetaMappingTests.cs`:

```csharp
using Goose2.AssetConverter;
using Goose2.AssetConverter.Aspereta;
using Xunit;

namespace AssetConverter.Tests;

public class AsperetaMappingTests
{
    [Fact]
    public void SheetNumbering_IsRankBased()
    {
        var sheets = AsperetaSheets.Load(Paths.AsperetaData);
        // 487 graphic sheets; lowest aspereta file number gets 20000, next 20001, ...
        Assert.Equal(487, sheets.Count);
        var ordered = sheets.Keys.OrderBy(n => n).ToList();
        Assert.Equal(20000, sheets[ordered[0]].NewSheetNumber);
        Assert.Equal(20486, sheets[ordered[^1]].NewSheetNumber);
    }

    [Fact]
    public void FullDatasets_ProduceKnownMatchCounts()
    {
        var rows = AsperetaMapping.Build(Paths.IllutiaData, Paths.AsperetaData);

        Assert.Equal(16139, rows.Count);
        Assert.Equal(3079, rows.Count(r => r.Status == MappingStatus.Matched));
        Assert.Equal(13060, rows.Count(r => r.Status == MappingStatus.Inject));

        // Injected ids follow the fixed schemes
        var inject = rows.First(r => r.Status == MappingStatus.Inject);
        Assert.Equal(700000 + inject.AspGraphic, inject.OutGraphic);
        Assert.InRange(inject.OutSheet, 20000, 20486);
    }
}
```

**Step 2: Run to verify failure**

Run: `dotnet test --filter AsperetaMappingTests`
Expected: FAIL — types not defined. (The passing run of `FullDatasets_...` takes a few minutes — it decodes ~5,400 sheets; that's acceptable for this golden test. Mark with `[Trait("Category","Slow")]` if the suite needs a fast path later — YAGNI now.)

**Step 3: Implement**

`src/AssetConverter/Aspereta/AsperetaSheets.cs`:

```csharp
using Goose2.AssetConverter.Adf;

namespace Goose2.AssetConverter.Aspereta;

public sealed record AsperetaSheet(AdfFile Adf, int NewSheetNumber);

/// <summary>Loads all Aspereta graphic sheets and assigns their deterministic
/// renumbered sheet ids: 20000 + rank of the file number in ascending order.</summary>
public static class AsperetaSheets
{
    public const int SheetBase = 20000;
    public const int GraphicBase = 700000;
    public const int BodyBase = 10000;
    public const int MapNumberBase = 10000;

    public static IReadOnlyDictionary<int, AsperetaSheet> Load(string asperetaDataDir)
    {
        var graphics = new SortedDictionary<int, AdfFile>();
        foreach (var file in Directory.EnumerateFiles(asperetaDataDir, "*.adf"))
        {
            AdfFile adf;
            try { adf = AsperetaAdf.Load(file); }
            catch { continue; }
            if (adf.Type != AdfType.Graphic || adf.Frames.Count == 0) continue;
            graphics[adf.FileNumber] = adf;
        }

        var result = new Dictionary<int, AsperetaSheet>(graphics.Count);
        int rank = 0;
        foreach (var (fileNumber, adf) in graphics)   // SortedDictionary => ascending
            result[fileNumber] = new AsperetaSheet(adf, SheetBase + rank++);
        return result;
    }
}
```

`src/AssetConverter/Aspereta/AsperetaMapping.cs`:

```csharp
using System.Security.Cryptography;
using Goose2.AssetConverter.Adf;
using Goose2.AssetConverter.Png;

namespace Goose2.AssetConverter.Aspereta;

public enum MappingStatus { Matched, Inject }

public sealed record MappingRow(
    int AspSheet, int AspGraphic, MappingStatus Status, int OutSheet, int OutGraphic);

/// <summary>Pixel-hash matches every Aspereta frame against the Illutia frame set and
/// assigns output ids per the fixed schemes (see plan header).</summary>
public static class AsperetaMapping
{
    public static List<MappingRow> Build(string illutiaDataDir, string asperetaDataDir)
    {
        // 1. Index every Illutia frame by pixel hash.
        var illutiaIndex = new Dictionary<string, (int Graphic, int Sheet)>();
        foreach (var file in Directory.EnumerateFiles(illutiaDataDir, "*.adf").OrderBy(f => f))
        {
            AdfFile adf;
            try { adf = new AdfFile(file); }
            catch { continue; }
            if (adf.Type != AdfType.Graphic || adf.Frames.Count == 0) continue;

            foreach (var (frame, hash) in FrameHashes(adf))
                illutiaIndex.TryAdd(hash, (frame.Index, adf.FileNumber));
        }

        // 2. Match every Aspereta frame.
        var rows = new List<MappingRow>();
        foreach (var (fileNumber, sheet) in AsperetaSheets.Load(asperetaDataDir).OrderBy(kv => kv.Key))
        {
            foreach (var (frame, hash) in FrameHashes(sheet.Adf))
            {
                rows.Add(illutiaIndex.TryGetValue(hash, out var donor)
                    ? new MappingRow(fileNumber, frame.Index, MappingStatus.Matched, donor.Sheet, donor.Graphic)
                    : new MappingRow(fileNumber, frame.Index, MappingStatus.Inject,
                        sheet.NewSheetNumber, AsperetaSheets.GraphicBase + frame.Index));
            }
        }
        return rows;
    }

    private static IEnumerable<(Frame Frame, string Hash)> FrameHashes(AdfFile adf)
    {
        byte[] rgba;
        int width, height;
        try { rgba = PayloadDecoder.ToRgba(adf.FileData, out width, out height); }
        catch { yield break; }

        foreach (var f in adf.Frames)
        {
            if (f.W <= 0 || f.H <= 0 || f.X < 0 || f.Y < 0 ||
                f.X + f.W > width || f.Y + f.H > height) continue;

            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            Span<byte> dims = stackalloc byte[8];
            BitConverter.TryWriteBytes(dims[..4], f.W);
            BitConverter.TryWriteBytes(dims[4..], f.H);
            sha.AppendData(dims);
            for (int row = 0; row < f.H; row++)
                sha.AppendData(rgba, ((f.Y + row) * width + f.X) * 4, f.W * 4);
            yield return (f, Convert.ToHexString(sha.GetHashAndReset()));
        }
    }

    public static string ToTsv(IEnumerable<MappingRow> rows)
    {
        var sb = new System.Text.StringBuilder("asp_sheet\tasp_graphic\tstatus\tout_sheet\tout_graphic\n");
        foreach (var r in rows)
            sb.Append($"{r.AspSheet}\t{r.AspGraphic}\t{r.Status.ToString().ToLowerInvariant()}\t{r.OutSheet}\t{r.OutGraphic}\n");
        return sb.ToString();
    }

    public static List<MappingRow> FromTsv(string path) =>
        File.ReadLines(path).Skip(1).Where(l => l.Length > 0).Select(l =>
        {
            var f = l.Split('\t');
            return new MappingRow(int.Parse(f[0]), int.Parse(f[1]),
                f[2] == "matched" ? MappingStatus.Matched : MappingStatus.Inject,
                int.Parse(f[3]), int.Parse(f[4]));
        }).ToList();
}
```

**Step 4: Run tests**

Run: `ILLUTIA_DATA=/home/hayden/code/Illutia/data dotnet test --filter "AsperetaMappingTests"`
Expected: PASS. The 3,079/13,060 counts are ground truth from the verified reference run — a mismatch means a decoding or hashing bug (likely suspects: transparency rule not applied on one side, or frame-rect clipping rules differing between `FrameHashes` and the reference).

**Step 5: Generate and commit the table** (Program wiring comes in Task 7; use a one-off run)

Add temporarily to `Program.cs` or run via the Task 7 subcommand once implemented; either way finish this task by producing the file:

```bash
cd tools/AssetConverter/src/AssetConverter
ILLUTIA_DATA=/home/hayden/code/Illutia/data dotnet run -- aspereta-mapping ../../data/aspereta-mapping.tsv
wc -l ../../data/aspereta-mapping.tsv    # expect 16140 (header + 16139 rows)
```

**Step 6: Commit**

```bash
git add tools/AssetConverter/src/AssetConverter/Aspereta/ \
        tools/AssetConverter/tests/AssetConverter.Tests/AsperetaMappingTests.cs \
        tools/AssetConverter/data/aspereta-mapping.tsv
git commit -m "feat(assetconverter): aspereta->illutia frame mapping generator + committed table"
```

---

### Task 3: Aspereta sheet PNG conversion (`aspereta-batch`)

Converts **all 487** Aspereta graphic sheets to `Assets/Sprites/sheets/<newSheetNumber>.png` with the black-key transparency applied. (All sheets, not just inject-bearing ones: 14 sheets are fully matched but monster resources in Task 6 reference their own sheet unconditionally, and 473/487 need conversion anyway — uniformity beats the marginal saving.)

**Files:**
- Create: `tools/AssetConverter/src/AssetConverter/Aspereta/AsperetaBatchConverter.cs`
- Test: `tools/AssetConverter/tests/AssetConverter.Tests/AsperetaBatchConverterTests.cs`

**Step 1: Write the failing test**

```csharp
using Goose2.AssetConverter;
using Goose2.AssetConverter.Aspereta;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace AssetConverter.Tests;

public class AsperetaBatchConverterTests
{
    [Fact]
    public void ConvertsAllSheetsWithRenumberedNamesAndTransparency()
    {
        string outDir = Path.Combine(Path.GetTempPath(), $"asp-batch-{Guid.NewGuid():N}");
        try
        {
            var result = AsperetaBatchConverter.Convert(Paths.AsperetaData, outDir);

            Assert.Equal(487, result.Succeeded);
            Assert.Empty(result.Failures);
            Assert.Equal(487, Directory.EnumerateFiles(outDir, "*.png").Count());
            Assert.True(File.Exists(Path.Combine(outDir, "20000.png")));

            // Sheet PNGs must carry alpha: 1.adf (bodies) has a black background.
            var sheets = AsperetaSheets.Load(Paths.AsperetaData);
            using var img = Image.Load<Rgba32>(
                Path.Combine(outDir, $"{sheets[1].NewSheetNumber}.png"));
            Assert.Equal(0, img[0, img.Height - 1].A);   // corner background pixel transparent
        }
        finally { Directory.Delete(outDir, recursive: true); }
    }
}
```

**Step 2: Run to verify failure** — `dotnet test --filter AsperetaBatchConverterTests` → FAIL.

**Step 3: Implement**

`src/AssetConverter/Aspereta/AsperetaBatchConverter.cs`:

```csharp
using Goose2.AssetConverter.Png;

namespace Goose2.AssetConverter.Aspereta;

/// <summary>Converts every Aspereta graphic sheet to a renumbered, alpha-keyed PNG.
/// Output name = the sheet's assigned NewSheetNumber (see AsperetaSheets).</summary>
public static class AsperetaBatchConverter
{
    public static BatchResult Convert(string asperetaDataDir, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var failures = new List<string>();
        int ok = 0;

        foreach (var (fileNumber, sheet) in AsperetaSheets.Load(asperetaDataDir))
        {
            try
            {
                var rgba = PayloadDecoder.ToRgba(sheet.Adf.FileData, out int w, out int h);
                PngWriter.Write(rgba, w, h, Path.Combine(outDir, $"{sheet.NewSheetNumber}.png"));
                ok++;
            }
            catch (Exception e)
            {
                failures.Add($"{fileNumber}: {e.GetType().Name} {e.Message}");
            }
        }
        return new BatchResult(ok, failures.Count, failures);
    }
}
```

(`BatchResult` record reused from `BatchConverter.cs:9`.)

**Step 4: Run tests** — expect PASS.

**Step 5: Commit**

```bash
git add tools/AssetConverter/src/AssetConverter/Aspereta/AsperetaBatchConverter.cs \
        tools/AssetConverter/tests/AssetConverter.Tests/AsperetaBatchConverterTests.cs
git commit -m "feat(assetconverter): aspereta sheet PNG batch conversion"
```

---### Task 4: Combined frame manifest (Illutia + injected Aspereta)

The Godot runtime resolves `(sheet, graphic) → rect` through `Assets/Sprites/manifest.json`. Aspereta sheets/graphics must appear in it under their renumbered ids.

**Files:**
- Modify: `tools/AssetConverter/src/AssetConverter/Manifest/FrameManifestBuilder.cs`
- Test: extend `tools/AssetConverter/tests/AssetConverter.Tests/FrameManifestBuilderTests.cs`

**Step 1: Write the failing test** (append to the existing test class — check its existing shape first, `FrameManifestBuilderTests.cs` is 31 lines)

```csharp
    [Fact]
    public void CombinedManifest_ContainsIllutiaAndRenumberedAsperetaSheets()
    {
        string json = FrameManifestBuilder.BuildCombined(Paths.IllutiaData, Paths.AsperetaData);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var sheets = doc.RootElement.GetProperty("sheets");

        Assert.True(sheets.TryGetProperty("1000", out _));      // illutia sheet still present
        Assert.True(sheets.TryGetProperty("20000", out var asp)); // renumbered aspereta sheet
        // every graphic key in an aspereta sheet is in the 700000+ range
        foreach (var g in asp.EnumerateObject())
            Assert.True(int.Parse(g.Name) >= 700000);
    }
```

**Step 2: Run to verify failure** — FAIL, `BuildCombined` missing.

**Step 3: Implement** — refactor `Build` to expose its per-directory core, then compose:

In `FrameManifestBuilder.cs`, extract the loop body of `Build` (`:16-31`) into:

```csharp
    private static SortedDictionary<string, Dictionary<string, int[]>> BuildIllutiaSheets(
        string dataDir, int[]? onlyFileNumbers)
    { /* existing Build() loop verbatim, returning `sheets` */ }
```

keep `Build(dataDir, only)` delegating to it + serializing (behavior unchanged), and add:

```csharp
    /// <summary>Illutia sheets plus every Aspereta sheet under its renumbered id, with
    /// every Aspereta graphic keyed as 700000 + original id. Aspereta frames are keyed
    /// under the injected id even when a matched Illutia twin exists — matched graphics
    /// are simply never referenced by converted data, so the duplicates are inert.</summary>
    public static string BuildCombined(string illutiaDataDir, string asperetaDataDir)
    {
        var sheets = BuildIllutiaSheets(illutiaDataDir, null);

        foreach (var (_, sheet) in Aspereta.AsperetaSheets.Load(asperetaDataDir))
        {
            var frames = new Dictionary<string, int[]>(sheet.Adf.Frames.Count);
            foreach (var f in sheet.Adf.Frames)
                frames[(Aspereta.AsperetaSheets.GraphicBase + f.Index).ToString()] =
                    new[] { f.X, f.Y, f.W, f.H };
            sheets[sheet.NewSheetNumber.ToString()] = frames;
        }

        var root = new { tileSize = 32, sheets };
        return System.Text.Json.JsonSerializer.Serialize(root);
    }
```

**Step 4: Run the full manifest test file** — `dotnet test --filter FrameManifestBuilderTests` → PASS (old tests prove `Build` unchanged).

**Step 5: Commit**

```bash
git add tools/AssetConverter/src/AssetConverter/Manifest/FrameManifestBuilder.cs \
        tools/AssetConverter/tests/AssetConverter.Tests/FrameManifestBuilderTests.cs
git commit -m "feat(assetconverter): combined illutia+aspereta frame manifest"
```

---

### Task 5: Aspereta map converter

Rewrites Aspereta `Map<N>.map` (100×100, 4 layers, graphic-only) into Goose2 `Map<10000+N>.bytes` (5 layers, graphic+sheet) using the mapping table.

**Layer mapping:** Aspereta renders characters between layer 2 and layer 3 (`gooseclient Map.cs:183-196`: objects/characters drawn inside the `l == 2` pass, layer 3 painted after) — layer 3 is the above-character/roof layer. Goose2 renders the roof as layer index 4 (`Scripts/MapFile.cs:31-34`). Therefore: asp layers 0,1,2 → goose2 layers 0,1,2; goose2 layer 3 left empty; asp layer 3 → goose2 layer 4.

**Flags:** asp `blocked byte == 1` → goose2 `Flags = 2` (`IsBlocked` tests bit 1: `Scripts/MapFile.cs:26-29`), else 0.

**Header:** goose2 `Version`/`EditorVersion` are parsed but never branched on in the Godot client — pass through Aspereta's two Int16s (observed 65, 3), then `Width=100, Height=100` as Int32s.

**Graphic remap:** each nonzero asp layer value is an asp graphic id → look up `(OutSheet, OutGraphic)` in the mapping table. Zero stays `(0, 0)`. An id absent from the table (graphic referenced by a map but present in no `.adf`) → write `(0,0)` and record a warning — never throw, maps may reference dead art.

**Files:**
- Create: `tools/AssetConverter/src/AssetConverter/Aspereta/AsperetaMapConverter.cs`
- Test: `tools/AssetConverter/tests/AssetConverter.Tests/AsperetaMapConverterTests.cs`

**Step 1: Write the failing test**

```csharp
using Goose2.AssetConverter;
using Goose2.AssetConverter.Aspereta;
using Xunit;

namespace AssetConverter.Tests;

public class AsperetaMapConverterTests
{
    private static readonly string MappingTsv = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "../../../../../data/aspereta-mapping.tsv"));

    [Fact]
    public void ConvertsAllMaps_RoundTripParsesInGoose2Format()
    {
        string outDir = Path.Combine(Path.GetTempPath(), $"asp-maps-{Guid.NewGuid():N}");
        try
        {
            var mapping = AsperetaMapping.FromTsv(MappingTsv);
            var result = AsperetaMapConverter.Convert(Paths.AsperetaMaps, outDir, mapping);

            Assert.Equal(44, result.Converted);
            Assert.Empty(result.Failures);
            Assert.True(File.Exists(Path.Combine(outDir, "Map10001.bytes")));

            // Round-trip: parse with the exact logic of Scripts/MapFile.cs:59-90.
            var bytes = File.ReadAllBytes(Path.Combine(outDir, "Map10001.bytes"));
            using var r = new BinaryReader(new MemoryStream(bytes));
            short version = r.ReadInt16(); short editorVersion = r.ReadInt16();
            int width = r.ReadInt32(); int height = r.ReadInt32();
            Assert.Equal((100, 100), (width, height));

            int nonEmptyLayers = 0;
            var lookup = mapping.ToDictionary(m => m.AspGraphic, m => m);
            for (int i = 0; i < width * height; i++)
            {
                int flags = r.ReadInt32();
                Assert.True(flags is 0 or 2);
                for (int k = 0; k < 5; k++)
                {
                    int graphic = r.ReadInt32();
                    short sheet = r.ReadInt16();
                    if (graphic == 0) { Assert.Equal(0, sheet); continue; }
                    nonEmptyLayers++;
                    Assert.True(k != 3, "layer 3 must be empty");
                    // every written (graphic, sheet) pair comes from the mapping table
                    Assert.True(lookup.TryGetValue(
                        graphic >= 700000 ? graphic - 700000 : lookup.Values.First(v => v.OutGraphic == graphic).AspGraphic,
                        out _) || graphic < 700000);
                    Assert.True(sheet is (>= 0 and <= 4962) or (>= 20000 and <= 20486));
                }
            }
            Assert.Equal(bytes.Length, r.BaseStream.Position);   // no trailing garbage
            Assert.True(nonEmptyLayers > 0);
        }
        finally { Directory.Delete(outDir, recursive: true); }
    }
}
```

(The convoluted reverse-lookup assert is only sanity; the essential assertions are the sheet ranges, empty layer 3, exact stream length, and 100×100 round-trip.)

**Step 2: Run to verify failure** — FAIL.

**Step 3: Implement**

`src/AssetConverter/Aspereta/AsperetaMapConverter.cs`:

```csharp
namespace Goose2.AssetConverter.Aspereta;

public sealed record MapConvertResult(int Converted, IReadOnlyList<string> Failures,
    IReadOnlyList<string> Warnings);

/// <summary>Converts Aspereta Map&lt;N&gt;.map files into Goose2-format
/// Map&lt;10000+N&gt;.bytes, remapping every graphic id through the mapping table.
/// Layer mapping: asp 0,1,2 -> out 0,1,2; out 3 empty; asp 3 (roof) -> out 4.</summary>
public static class AsperetaMapConverter
{
    public static MapConvertResult Convert(
        string asperetaMapsDir, string outDir, IReadOnlyList<MappingRow> mapping)
    {
        Directory.CreateDirectory(outDir);
        var failures = new List<string>();
        var warnings = new List<string>();
        var byGraphic = mapping.ToDictionary(m => m.AspGraphic);
        int converted = 0;

        foreach (var file in Directory.EnumerateFiles(asperetaMapsDir, "Map*.map"))
        {
            try
            {
                string basename = Path.GetFileNameWithoutExtension(file);   // "Map12"
                int number = int.Parse(basename["Map".Length..]);
                string outPath = Path.Combine(outDir,
                    $"Map{AsperetaSheets.MapNumberBase + number}.bytes");

                using var reader = new BinaryReader(File.OpenRead(file));
                using var writer = new BinaryWriter(File.Create(outPath));

                writer.Write(reader.ReadInt16());   // version passthrough
                writer.Write(reader.ReadInt16());   // editor version passthrough
                writer.Write(100); writer.Write(100);

                for (int i = 0; i < 100 * 100; i++)
                {
                    byte blocked = reader.ReadByte();
                    writer.Write(blocked == 1 ? 2 : 0);

                    Span<int> asp = stackalloc int[4];
                    for (int k = 0; k < 4; k++) asp[k] = reader.ReadInt32();

                    for (int outLayer = 0; outLayer < 5; outLayer++)
                    {
                        int src = outLayer switch { 0 => 0, 1 => 1, 2 => 2, 3 => -1, 4 => 3, _ => -1 };
                        int graphic = src < 0 ? 0 : asp[src];

                        if (graphic == 0) { writer.Write(0); writer.Write((short)0); continue; }

                        if (byGraphic.TryGetValue(graphic, out var row))
                        {
                            writer.Write(row.OutGraphic);
                            writer.Write((short)row.OutSheet);
                        }
                        else
                        {
                            warnings.Add($"{basename}: graphic {graphic} not in mapping table, dropped");
                            writer.Write(0); writer.Write((short)0);
                        }
                    }
                }
                // Trailing bytes after the tile grid are intentionally ignored
                // (AsperetaMapLoader.cs reads only the grid).
                converted++;
            }
            catch (Exception e)
            {
                failures.Add($"{Path.GetFileName(file)}: {e.GetType().Name} {e.Message}");
            }
        }
        return new MapConvertResult(converted, failures, warnings.Distinct().ToList());
    }
}
```

**Step 4: Run tests** — expect PASS. Print/inspect `result.Warnings` once manually: a handful of dead-graphic warnings is plausible; hundreds means the mapping table or decoder is broken.

**Step 5: Commit**

```bash
git add tools/AssetConverter/src/AssetConverter/Aspereta/AsperetaMapConverter.cs \
        tools/AssetConverter/tests/AssetConverter.Tests/AsperetaMapConverterTests.cs
git commit -m "feat(assetconverter): aspereta map converter to goose2 format"
```

---

### Task 6: Monster animation synthesis (4×8 → 4×11)

Builds Godot `SpriteFrames` resources for the 66 Aspereta monsters by constructing Illutia-layout `CompiledAnimation` objects and running them through the existing builder. Also refactors metadata writing so Illutia + Aspereta resources merge into ONE `Assets/Resources` metadata set instead of the second run clobbering the first.

**Index math (all verified above):**
- Aspereta flat index: `(attack ? 16 : 0) + facing * 4 + (bodyState - 1)`; monsters use bodyState 1 → column 0. Facing: Up=0, Right=1, Down=2, Left=3.
- Illutia: `AnimationIndexes[dir * 11 + (int)order]`, dir: left=0, down=1, right=2, up=3; `AnimationFiles[(int)order]` = sheet number.
- Direction translation (illutia dir ← aspereta facing): left←Left(3), down←Down(2), right←Right(1), up←Up(0).
- Orders filled: `WalkingNoEquip` (0) from asp walk block, `AttackNoEquip` (2) from asp attack block. All other orders/files stay 0 → builder emits warnings and skips them (`CompiledAnimationBuilder.cs:32-43`), which is the desired "blank slot" behavior (59% of real Illutia entries have blank slots).
- Sheet resolution: Aspereta animation ids are globally unique across sheets (gooseclient `AdfManager.cs:38-40` builds one flat dictionary). Build `animId → aspFileNumber` from the loaded sheets; a monster's `AnimationFiles[order]` = the renumbered sheet of its first nonzero animation id for that order. If the four directions of one order resolve to different sheets, fail that monster loudly (the builder supports one sheet per order) — not expected in the real data.

**Files:**
- Create: `tools/AssetConverter/src/AssetConverter/Aspereta/AsperetaCompiledEnc.cs`
- Create: `tools/AssetConverter/src/AssetConverter/Aspereta/AsperetaMonsterConverter.cs`
- Modify: `tools/AssetConverter/src/AssetConverter/SpriteFrames/AnimationBatchConverter.cs` (extract metadata write)
- Test: `tools/AssetConverter/tests/AssetConverter.Tests/AsperetaMonsterConverterTests.cs`

**Step 1: Extract metadata writing from AnimationBatchConverter (pure refactor, run existing tests after)**

In `AnimationBatchConverter.cs`, replace step 6 (`:179-199`) with a call to a new public method, moving the existing code verbatim:

```csharp
    /// <summary>Merges and writes AnimationToFirstFrame/AnimationHeights metadata for a set of
    /// resources (possibly from multiple games) plus effect heights. Extracted so the Aspereta
    /// pipeline can contribute to the same files. Merge throws on conflicting duplicate keys
    /// (AnimationMetadataWriter.cs:27-31) — id-space offsets make cross-game keys disjoint.</summary>
    public static void WriteMergedMetadata(
        string outRoot,
        IEnumerable<CompiledSpriteFramesResource> resources,
        IReadOnlyDictionary<string, int> effectHeights)
    {
        var mergedFirstFrames = AnimationMetadataWriter.MergeFirstFrames(
            resources.Select(r => r.AnimationToFirstFrame));
        var characterHeights = AnimationMetadataWriter.MergeHeights(
            resources.Select(r => r.AnimationHeights));
        var mergedHeights = AnimationMetadataWriter.MergeHeights(
            new[] { characterHeights, effectHeights });

        string resourcesDir = Path.Combine(outRoot, "Assets", "Resources");
        AnimationMetadataWriter.Write(resourcesDir, mergedFirstFrames, mergedHeights);
    }
```

and give `Convert` an optional `IReadOnlyList<CompiledSpriteFramesResource>? extraResources = null` parameter appended to `allResources` before the metadata call. Run `dotnet test` — all pre-existing AnimationBatchConverter tests must still pass.

**Step 2: Write the failing tests**

```csharp
using Goose2.AssetConverter;
using Goose2.AssetConverter.Adf;
using Goose2.AssetConverter.Aspereta;
using Xunit;

namespace AssetConverter.Tests;

public class AsperetaMonsterConverterTests
{
    [Fact]
    public void CompiledEnc_Has66MonsterEntries()
    {
        var entries = AsperetaCompiledEnc.Load(Paths.AsperetaCompiledEnc);
        Assert.Equal(241, entries.Count);
        Assert.Equal(66, entries.Count(e => e.Type == AnimationType.Body && e.Id > 100));
    }

    [Fact]
    public void MonsterResources_HaveWalkAndAttackOnly_UnderOffsetIds()
    {
        var sheets = AsperetaSheets.Load(Paths.AsperetaData);
        var monsters = AsperetaMonsterConverter.BuildResources(
            AsperetaCompiledEnc.Load(Paths.AsperetaCompiledEnc), sheets, out var errors);

        Assert.Empty(errors);
        Assert.Equal(66, monsters.Count);

        var m = monsters.First();
        Assert.InRange(m.Id, 10101, 10166);
        Assert.Equal(AnimationType.Body, m.Type);
        Assert.StartsWith("Assets/Sprites/Bodies/1", m.RelativeOutputPath);
        // walk + attack clips exist for all 4 directions (plus idle/aliases from the builder)
        var names = m.Animations.Select(a => a.Name).ToHashSet();
        foreach (var dir in new[] { "left", "down", "right", "up" })
        {
            Assert.Contains($"walk-no-equip-{dir}", names);
            Assert.Contains($"attack-no-equip-{dir}", names);
            Assert.DoesNotContain($"cast-{dir}", names);   // unfilled orders stay blank
        }
    }
}
```

(Adjust `m.Animations.Select(a => a.Name)` to the actual member name on `SpriteFramesAnimationSpec` — verify in `SpriteFrames/` when implementing; it is the first ctor arg called `clipName` at `CompiledAnimationBuilder.cs:70-71`.)

**Step 3: Run to verify failure** — FAIL.

**Step 4: Implement**

`src/AssetConverter/Aspereta/AsperetaCompiledEnc.cs`:

```csharp
using Goose2.AssetConverter.Adf;

namespace Goose2.AssetConverter.Aspereta;

/// <summary>An Aspereta compiled.enc entry: 4 directions x 8 columns
/// (walk/attack blocks x 4 body-state columns). Layout per gooseclient
/// CompiledEnc.cs:80-106 / Character.cs:185-187.</summary>
public sealed record AsperetaCompiledAnimation(AnimationType Type, int Id, int[] Indexes)
{
    /// <summary>facing: 0=Up,1=Right,2=Down,3=Left (gooseclient Character.cs:8-14).</summary>
    public int Walk(int facing) => Indexes[facing * 4];
    public int Attack(int facing) => Indexes[16 + facing * 4];
}

public static class AsperetaCompiledEnc
{
    public static List<AsperetaCompiledAnimation> Load(string path)
    {
        var result = new List<AsperetaCompiledAnimation>();
        using var reader = new BinaryReader(File.OpenRead(path));
        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            // Aspereta type order: Body,Hair,Hand,Chest,Helm,Legs,Feet (1-based on disk).
            // Only Body entries are consumed; map others to the nearest illutia enum
            // member purely so the record is representable.
            int rawType = reader.ReadInt16() - 1;
            int id = reader.ReadInt32();
            var indexes = new int[32];
            for (int i = 0; i < 32; i++) indexes[i] = reader.ReadInt32();
            AnimationType type = rawType == 0 ? AnimationType.Body : (AnimationType)rawType;
            result.Add(new AsperetaCompiledAnimation(type, id, indexes));
        }
        return result;
    }
}
```

`src/AssetConverter/Aspereta/AsperetaMonsterConverter.cs`:

```csharp
using Goose2.AssetConverter.Adf;
using Goose2.AssetConverter.SpriteFrames;

namespace Goose2.AssetConverter.Aspereta;

/// <summary>Reshapes Aspereta monster entries (Body id &gt; 100) into Illutia-layout
/// CompiledAnimations and builds their SpriteFrames resources via the standard builder.</summary>
public static class AsperetaMonsterConverter
{
    // illutia dir index (left,down,right,up) -> aspereta facing (Up=0,Right=1,Down=2,Left=3)
    private static readonly int[] AspFacingForIllutiaDir = { 3, 2, 1, 0 };

    public static List<CompiledSpriteFramesResource> BuildResources(
        IReadOnlyList<AsperetaCompiledAnimation> entries,
        IReadOnlyDictionary<int, AsperetaSheet> sheets,
        out List<string> errors)
    {
        errors = new List<string>();

        // Global animId -> aspereta file number (gooseclient AdfManager.cs:38-40 semantics),
        // plus AdfFiles renumbered + keyed by NEW sheet number for the builder.
        var animToFile = new Dictionary<int, int>();
        var renumbered = new Dictionary<int, AdfFile>();
        foreach (var (fileNumber, sheet) in sheets)
        {
            sheet.Adf.FileNumber = sheet.NewSheetNumber;
            renumbered[sheet.NewSheetNumber] = sheet.Adf;
            if (sheet.Adf.Animations is null) continue;
            foreach (var animId in sheet.Adf.Animations.Keys)
                animToFile[animId] = sheet.NewSheetNumber;
        }

        var resources = new List<CompiledSpriteFramesResource>();
        foreach (var entry in entries.Where(e => e.Type == AnimationType.Body && e.Id > 100))
        {
            var ca = new CompiledAnimation(AnimationType.Body, AsperetaSheets.BodyBase + entry.Id);

            bool ok = FillOrder(ca, AnimationOrder.WalkingNoEquip, entry.Walk, animToFile, errors, entry.Id)
                    & FillOrder(ca, AnimationOrder.AttackNoEquip, entry.Attack, animToFile, errors, entry.Id);
            if (!ok) continue;

            resources.Add(CompiledAnimationBuilder.BuildCharacterResource(ca, renumbered));
        }
        return resources;
    }

    private static bool FillOrder(
        CompiledAnimation ca, AnimationOrder order, Func<int, int> aspIndex,
        IReadOnlyDictionary<int, int> animToFile, List<string> errors, int aspId)
    {
        int sheetForOrder = 0;
        for (int dir = 0; dir < 4; dir++)
        {
            int animId = aspIndex(AspFacingForIllutiaDir[dir]);
            ca.AnimationIndexes[dir * 11 + (int)order] = animId;
            if (animId == 0) continue;

            if (!animToFile.TryGetValue(animId, out int sheet))
            {
                errors.Add($"monster {aspId} {order}: animation {animId} not found in any sheet");
                return false;
            }
            if (sheetForOrder != 0 && sheetForOrder != sheet)
            {
                errors.Add($"monster {aspId} {order}: directions span sheets {sheetForOrder} and {sheet}");
                return false;
            }
            sheetForOrder = sheet;
        }
        ca.AnimationFiles[(int)order] = sheetForOrder;
        return true;
    }
}
```

CAUTION: `BuildResources` mutates `sheet.Adf.FileNumber`. `AsperetaSheets.Load` is called freshly by each consumer, so no shared state — but do NOT cache `AsperetaSheets.Load` results across Task 3/4/6 calls within one `all` run without accounting for this (Task 7 loads it once per sub-step; keep it that way or renumber up front in `AsperetaSheets.Load` itself — implementer's choice, with a preference for renumbering inside `Load` so `AdfFile.FileNumber` is ALWAYS the new sheet number after load; if you do that, simplify Task 3's PNG naming to use `Adf.FileNumber` and note that `AsperetaMapping` reports `AspSheet` from the pre-renumber key, which `Load`'s dictionary key still preserves).

**Step 5: Run tests** — `dotnet test --filter AsperetaMonsterConverterTests` → PASS, `errors` empty. If `directions span sheets` errors appear, list them — that would falsify the one-sheet-per-order assumption and those monsters need per-direction sheet support (deal with it then, not preemptively).

**Step 6: Commit**

```bash
git add tools/AssetConverter/src/AssetConverter/Aspereta/AsperetaCompiledEnc.cs \
        tools/AssetConverter/src/AssetConverter/Aspereta/AsperetaMonsterConverter.cs \
        tools/AssetConverter/src/AssetConverter/SpriteFrames/AnimationBatchConverter.cs \
        tools/AssetConverter/tests/AssetConverter.Tests/AsperetaMonsterConverterTests.cs
git commit -m "feat(assetconverter): aspereta monster animation synthesis (4x8 -> 4x11)"
```

---

### Task 7: Program wiring — `aspereta-mapping`, `aspereta`, and extended `all`

**Files:**
- Modify: `tools/AssetConverter/src/AssetConverter/Program.cs`

**Step 1: Add subcommands** (before the final usage line, `Program.cs:94`):

```csharp
if (args.Length >= 1 && args[0] == "aspereta-mapping")
{
    string outPath = args.Length >= 2
        ? args[1]
        : Path.GetFullPath(Path.Combine("..", "..", "data", "aspereta-mapping.tsv"));
    Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
    var rows = Goose2.AssetConverter.Aspereta.AsperetaMapping.Build(Paths.IllutiaData, Paths.AsperetaData);
    File.WriteAllText(outPath, Goose2.AssetConverter.Aspereta.AsperetaMapping.ToTsv(rows));
    int matched = rows.Count(r => r.Status == Goose2.AssetConverter.Aspereta.MappingStatus.Matched);
    Console.WriteLine($"Wrote {rows.Count} rows ({matched} matched, {rows.Count - matched} inject) -> {outPath}");
    return;
}

if (args.Length >= 1 && args[0] == "aspereta")
{
    string repoRoot = args.Length >= 2 ? args[1] : Path.GetFullPath(Path.Combine("..", ".."));
    string mappingPath = Path.GetFullPath(Path.Combine("..", "..", "data", "aspereta-mapping.tsv"));
    var mapping = Goose2.AssetConverter.Aspereta.AsperetaMapping.FromTsv(mappingPath);

    var sheets = Goose2.AssetConverter.Aspereta.AsperetaBatchConverter.Convert(
        Paths.AsperetaData, Path.Combine(repoRoot, "Assets", "Sprites", "sheets"));
    var maps = Goose2.AssetConverter.Aspereta.AsperetaMapConverter.Convert(
        Paths.AsperetaMaps, Path.Combine(repoRoot, "Assets", "Maps"), mapping);

    Console.WriteLine($"Aspereta sheets: {sheets.Succeeded} ok, {sheets.Failed} failed");
    Console.WriteLine($"Aspereta maps: {maps.Converted} converted, {maps.Failures.Count} failed, {maps.Warnings.Count} warnings");
    foreach (var w in maps.Warnings) Console.WriteLine($"  WARN {w}");
    foreach (var f in sheets.Failures.Concat(maps.Failures)) Console.WriteLine($"  FAIL {f}");
    return;
}
```

**Step 2: Extend `all`** (`Program.cs:69-92`) — after the existing illutia steps, add the aspereta steps and route monster resources through the shared metadata write. Replace the `animations` call and manifest write with:

```csharp
    var aspSheetsInfo = Goose2.AssetConverter.Aspereta.AsperetaSheets.Load(Paths.AsperetaData);
    var monsterResources = Goose2.AssetConverter.Aspereta.AsperetaMonsterConverter.BuildResources(
        Goose2.AssetConverter.Aspereta.AsperetaCompiledEnc.Load(Paths.AsperetaCompiledEnc),
        aspSheetsInfo, out var monsterErrors);

    var animations = AnimationBatchConverter.Convert(
        Paths.IllutiaData, Paths.CompiledEnc, repoRoot, includeEffects: true,
        extraResources: monsterResources);

    // monster .tres files are not written by Convert's illutia loop — write them here
    foreach (var resource in monsterResources.Where(r => r.Animations.Count > 0))
    {
        string fullPath = Path.Combine(repoRoot, resource.RelativeOutputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, SpriteFramesWriter.Build(resource.Animations));
    }

    var aspBatch = Goose2.AssetConverter.Aspereta.AsperetaBatchConverter.Convert(
        Paths.AsperetaData, sheetsDir);
    var mappingRows = Goose2.AssetConverter.Aspereta.AsperetaMapping.FromTsv(
        Path.GetFullPath(Path.Combine("..", "..", "data", "aspereta-mapping.tsv")));
    var aspMaps = Goose2.AssetConverter.Aspereta.AsperetaMapConverter.Convert(
        Paths.AsperetaMaps, mapsDir, mappingRows);

    File.WriteAllText(manifestPath,
        FrameManifestBuilder.BuildCombined(Paths.IllutiaData, Paths.AsperetaData));
```

(Design note: `extraResources` merges monster metadata into the single `Assets/Resources` write inside `Convert` — Task 6 Step 1's refactor. The `.tres` writing loop mirrors `AnimationBatchConverter.cs:103-125`. Cross-game metadata keys can't conflict: they're prefixed `Body-10101-...` vs Illutia's `Body-1...308-...`.)

**Step 3: Full pipeline smoke run**

```bash
cd tools/AssetConverter/src/AssetConverter
ILLUTIA_DATA=/home/hayden/code/Illutia/data ILLUTIA_MAPS=/home/hayden/code/Illutia/maps \
  dotnet run -- all /home/hayden/code/Goose2ClientGodot
```

Expected output (counts): Illutia sheets ~4,9xx ok; Aspereta sheets 487 ok; Aspereta maps 44 converted; monster errors 0; manifest written. Then confirm on disk:

```bash
ls /home/hayden/code/Goose2ClientGodot/Assets/Sprites/sheets/20*.png | wc -l   # 487
ls /home/hayden/code/Goose2ClientGodot/Assets/Maps/Map1*.bytes | head          # Map10001.bytes etc.
ls /home/hayden/code/Goose2ClientGodot/Assets/Sprites/Bodies/ | grep 101       # 10101 ... 10166
```

**Step 4: Commit**

```bash
git add tools/AssetConverter/src/AssetConverter/Program.cs
git commit -m "feat(assetconverter): aspereta subcommands and combined 'all' pipeline"
```

---

### Task 8: Integration verification — every converted-map reference resolves

The invariant that makes converted maps renderable: **every nonzero (graphic, sheet) pair in every converted map resolves through the combined manifest, and the sheet PNG exists.** This is the cross-component test that unit tests can't give us (mapping ↔ manifest ↔ batch ↔ map converter all agree).

**Files:**
- Test: `tools/AssetConverter/tests/AssetConverter.Tests/AsperetaIntegrationTests.cs`

**Step 1: Write the test**

```csharp
using System.Text.Json;
using Goose2.AssetConverter;
using Goose2.AssetConverter.Aspereta;
using Goose2.AssetConverter.Manifest;
using Xunit;

namespace AssetConverter.Tests;

public class AsperetaIntegrationTests
{
    [Fact]
    public void EveryConvertedMapReference_ResolvesInCombinedManifestAndSheets()
    {
        string tmp = Path.Combine(Path.GetTempPath(), $"asp-integ-{Guid.NewGuid():N}");
        try
        {
            string mappingPath = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "../../../../../data/aspereta-mapping.tsv"));
            var mapping = AsperetaMapping.FromTsv(mappingPath);

            var maps = AsperetaMapConverter.Convert(Paths.AsperetaMaps, tmp, mapping);
            Assert.Equal(44, maps.Converted);

            using var manifest = JsonDocument.Parse(
                FrameManifestBuilder.BuildCombined(Paths.IllutiaData, Paths.AsperetaData));
            var sheets = manifest.RootElement.GetProperty("sheets");

            var missing = new List<string>();
            foreach (var mapFile in Directory.EnumerateFiles(tmp, "*.bytes"))
            {
                using var r = new BinaryReader(File.OpenRead(mapFile));
                r.ReadInt16(); r.ReadInt16(); int w = r.ReadInt32(); int h = r.ReadInt32();
                for (int i = 0; i < w * h; i++)
                {
                    r.ReadInt32(); // flags
                    for (int k = 0; k < 5; k++)
                    {
                        int graphic = r.ReadInt32(); short sheet = r.ReadInt16();
                        if (graphic == 0) continue;
                        if (!sheets.TryGetProperty(sheet.ToString(), out var sheetObj) ||
                            !sheetObj.TryGetProperty(graphic.ToString(), out _))
                            missing.Add($"{Path.GetFileName(mapFile)}: ({sheet},{graphic})");
                    }
                }
            }
            Assert.Empty(missing.Distinct().Take(20).ToList());
        }
        finally { Directory.Delete(tmp, recursive: true); }
    }
}
```

**Step 2: Run** — `dotnet test --filter AsperetaIntegrationTests`. Expected: PASS. A nonempty `missing` list pinpoints exactly which component disagrees (a `(20xxx, 7xxxxx)` miss → manifest/batch renumbering skew; a `(≤4962, ...)` miss → a matched row whose Illutia donor sheet was excluded from the manifest — both are real bugs, fix before proceeding).

**Step 3: Manual smoke in Godot (desktop, requires display + server irrelevant — pure rendering)**

Open the project in Godot, and in the editor run a scene that loads a converted map, e.g. temporarily point the Map scene's debug/test path at `Map10001.bytes` (see `Scripts/Map/MapManager.cs:1-40` for how maps are loaded), and eyeball: Aspereta tiles render, transparency correct, roofs draw above the player position. This is the same class of manual step the migration plan's E1 task uses. Record findings in the plan's outcome notes; do not skip silently.

**Step 4: Commit**

```bash
git add tools/AssetConverter/tests/AssetConverter.Tests/AsperetaIntegrationTests.cs
git commit -m "test(assetconverter): aspereta map->manifest->sheet integration invariant"
```

---

## Out of scope (next plan)

- Server-data remapping: rewrite graphic ids (`700000+`/matched donors from the same TSV), body ids (`10000+`), and map numbers (`10000+`) in GooseServer2 data; server-side handling of Aspereta spawns pointing at monster bodies 10101–10166.
- Item/spell **icon id** references in server data (pure data remap through the same table).
- Any Godot client changes (none are needed for rendering; the client is data-driven).
- Aspereta sound `.adf` conversion (487 of the 974 files are sounds/non-graphics — untouched).
