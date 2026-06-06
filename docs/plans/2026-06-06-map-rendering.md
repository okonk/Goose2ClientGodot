# Map Rendering — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Land Migration Plan **Step 5** (`MIGRATION_PLAN.md:305-307`, §4): parse the binary map
files and draw the live world — all 5 tile layers, dropped map items, runtime tile updates, the roof
toggle, and a camera centred on the player's spawn tile — so that logging into the real server shows
the actual map on screen instead of the `Map.tscn` placeholder label.

**Architecture:** Map tile art is **arbitrary-rect, bottom-center-anchored** sprites packed into per-
sheet PNGs (a tree/roof sprite is taller than its 32 px tile), which does **not** fit Godot's grid-
locked `TileSetAtlasSource`. So we **do not use `TileMapLayer`/`TileSet`** (this consciously revises
§4's wording — see "Decisions locked"). Instead each of the 5 layers is a `Node2D` that draws its
tiles in `_Draw` by blitting `AtlasTexture` regions straight off the original sheet PNGs, via a
runtime sprite cache backed by a converter-emitted **frame-rect manifest** (`sheet → {graphic → rect}`).
This is the Godot replacement for Unity's `ResourceManager.LoadSprite("{sheet}-{graphic}")` /
`Helpers.GetSprite` and matches the native art shape with zero repacking. The `MapFile` binary parser
ports verbatim from Unity (pure C#). A `MapManager` (port of the Unity one, **character handling
deferred to Step 6**) owns the `MapFile`, builds the layers, runs the `Camera2D`, and handles the
`TileUpdate` / `MapObject` / `EraseObject` packets.

**Tech Stack:** Godot 4.6 (.NET / C#); `Node2D._Draw` + `CanvasItem.DrawTextureRect`; `AtlasTexture`;
`Camera2D`; `System.Text.Json` (manifest). Converter extension in the existing
`tools/AssetConverter` (.NET console, xUnit, SixLabors.ImageSharp). Target repo:
`~/code/Goose2ClientGodot`. Unity source (read-only reference): `~/code/Goose2Client`. Original data:
`~/code/Illutia/{data,maps}`.

**Decisions locked for this plan** (confirmed with the user 2026-06-06):

- **No `TileMapLayer`/`TileSet`.** Render every layer via `AtlasTexture` regions drawn off the
  original sheet PNGs. This **revises `MIGRATION_PLAN.md` §4** (which said `TileMapLayer` +
  `TileSet` + `SetCell`). Rationale: the source frames are arbitrary-rect and bottom-center-anchored;
  Godot atlas sources are grid-locked, so honouring §4 literally would force the converter to repack
  every tile into a uniform grid and author per-tile `texture_origin` for tall sprites. Drawing the
  native rects is simpler, lossless, and y-sorts naturally with the Step 6 entities. **Task 11 edits
  §4 to record this.**
- **Converter prerequisites are in-scope (Task 0–2).** The converter has never been run on this
  machine and emits no tile assets. This plan fixes `Paths.cs`, runs it to generate the sheet PNGs +
  map `.bytes`, and **extends it to emit the frame-rect manifest** the renderer needs. The plan is
  self-contained: it ends with real pixels from real server data.
- **No Y-flip.** Unity's pervasive `map.Height - y` exists only to convert the server's Y-down tile
  rows into Unity's Y-up world. Godot 2D is **Y-down like the server**, so tile `(x, y)` maps to world
  `(x, y)` with **no vertical flip**. All tile↔world math lives in one helper (`MapCoords`); the
  flip is simply absent there. (`MIGRATION_PLAN.md:325` "lock 1 tile = 32 px and a single tile↔world
  helper" — done here.)
- **Characters are NOT rendered this step.** `MapManager`'s character listeners (`MakeCharacter`,
  `MoveCharacter`, `SetYourCharacter`, vitals, attack, spell, emote, chat, …) are **Step 6**. Step 5
  ports only the map/tile/item/camera subset. The camera centres on the spawn tile via
  `SetYourPositionPacket` (no `Character` node needed).
- **One coordinate scale: 1 tile = 32 px**, `MapCoords.TileSize = 32`. Matches Unity's
  `pixelsPerUnit = 32` (`ToolsMenu.cs:415`).

---

## APIs verified

Unity source being ported (paths relative to `~/code/Goose2Client/Assets/Scripts/`, read 2026-06-06):

- `MapFile.cs:1-96` — the entire file. `class Layer { int Sheet; int Graphic }` (`:9-13`);
  `class MapTile { int Flags; Layer[5] Layers; bool IsBlocked => (Flags & 2) > 0; bool IsRoof =>
  Layers[4].Graphic != 0 }` (`:15-34`); `class MapFile` with `short Version, short EditorVersion,
  int Width, int Height, MapTile[] Tiles`, ctors `(string path)` / `(byte[] bytes)`, `Load(Stream)`
  reading header `Int16 Version, Int16 EditorVersion, Int32 Width, Int32 Height` then per tile
  `Int32 Flags` + 5×(`Int32 Graphic`, `Int16 Sheet`), row-major `Tiles[i*Width + j]`, and indexer
  `this[x,y] => Tiles[y*Width + x]` (`:36-95`). **No `UnityEngine` dependency.** Per-tile size on
  disk = 4 + 5×(4+2) = **34 bytes**; header = **12 bytes**.
- `Helpers.cs:10-13` — `GetSprite(int id, int file) => ResourceManager.LoadSprite($"{file}-{id}")`.
  The sprite key is `"{sheet}-{graphic}"`. **Step 5 replaces this** with `SpriteCache.Get(sheet, graphic)`.
- `MapManager.cs:30-58` — `Start()` `Listen<T>` registrations. **In scope this step:**
  `TileUpdatePacket`, `MapObjectPacket`, `EraseObjectPacket`, `SendCurrentMapPacket`,
  `SetYourPositionPacket`. **Deferred to Step 6:** `MakeCharacter`, `UpdateCharacter`,
  `SetYourCharacter`, `MoveCharacter`, `ChangeHeading`, `EraseCharacter`, `VitalsPercentage`,
  `Attack`, `WeaponSpeed`, `SpellCharacter`, `SpellTile`, `BattleText`, `Cast`, `Emote`, `Chat`,
  `Ping`.
- `MapManager.cs` tile-update handler (`OnTileUpdatePacket`, reported `:380-410`) — for each of 5
  layers reads `Tiles[layer*2]` = graphic, `Tiles[layer*2+1]` = sheet; `sheet == 0` ⇒ clear the cell,
  else set sprite `"{sheet}-{graphic}"`; also `map[x,y].Flags = packet.Flags`. Unity pos
  `(packet.X, map.Height - packet.Y - 1)` → **Godot `(packet.X, packet.Y)`** (no flip).
- `MapManager.cs` map-object handlers (`OnMapObject` reported `:300-312`, `OnEraseMapObject`
  `:314-320`) — instantiate item prefab, `SpriteRenderer.sprite = Helpers.GetSprite(GraphicId,
  GraphicFile)`, `material.SetColor("_Tint", RGBA(GraphicR,G,B,A))`, localPosition
  `(TileX + 0.5f, map.Height - TileY - 0.5f)` → **Godot bottom-center of tile, no flip**; key
  `mapObjects[TileY*map.Height + TileX]`. Erase removes by the same key.
- `MapManager.cs` roof + camera (reported) — `roofLayer = Find("Roofs")`; on spawn/move,
  `roofLayer.SetActive(map[x,y].IsRoof ? false : true)` → **Godot `roofLayer.Visible = !IsRoof`**.
  `SetCameraFollow` set Cinemachine `Follow` → **Godot `Camera2D` positioned at the player tile**.
- `MapManager.cs` `IsValidMove(x,y)` (reported) — bounds + `map[x,y].IsBlocked` (character occupancy
  check is Step 6). Port the map-only part; movement is Step 6 but the helper is cheap to land now.

Converter (existing, `~/code/Goose2ClientGodot/tools/AssetConverter/`, read 2026-06-06):

- `src/AssetConverter/Adf/IllutiaData.cs` — `AdfFile(string)`, `.FileNumber`, `.Type` (`AdfType.Graphic`),
  `.Frames` (`List<Frame>`), `Frame { int Index, X, Y, W, H }`. Already ported & golden-tested.
- `src/AssetConverter/BatchConverter.cs:18-49` — `Convert(dataDir, outDir, int[]? onlyFileNumbers)`
  → `BatchResult(int Succeeded, int Failed, IReadOnlyList<string> Failures)`; writes
  `{fileNumber}.png` per graphic sheet.
- `src/AssetConverter/Maps/MapCopyConverter.cs:12-49` — `Convert(sourceMapsDir, outMapsDir)`
  → copies `Map100.map` → `Map100.bytes` (rule `M + basename[1:] + .bytes`).
- `src/AssetConverter/SpriteFrames/SpriteFramesWriter.cs:24-31` — shows the exact `.tres` `region =
  Rect2(f.X, f.Y, f.W, f.H)` shape; confirms **Godot `AtlasTexture` region == top-down `Frame` rect,
  no conversion**. The manifest (Task 2) emits the same `(X,Y,W,H)` per frame as JSON.
- `src/AssetConverter/Program.cs` — dispatch on `args[0]`: `batch`, `frames`, `animations`, `maps`,
  `all`. **Task 0 fixes `Paths.cs`; Task 2 adds a `manifest` command + folds it into `all`.**
- `src/AssetConverter/Paths.cs:7-9` — **BUG: points at `/home/agent/workspace/...`** (a sandbox path).
  Must become `/home/hayden/code/...` before anything runs here. Fixed in Task 0.

Target repo baseline — current Godot state (`~/code/Goose2ClientGodot`, read 2026-06-06):

- `Scripts/GameManager.cs:75-97` — `ChangeMap(string mapFile, string mapName)` is `async void`:
  `SetPaused(true)` → `ChangeSceneToPacked(LoadingMap.tscn)` → `await ToSignal(..ProcessFrame)` →
  `loading.SetMapName(mapName)` → **`// Step 5 hook` (`:86`)** → `ChangeSceneToPacked(Map.tscn)` →
  `await ToSignal(..ProcessFrame)` → `DoneLoadingMap()` → `finally { SetPaused(false); }`.
  **The map load (parse `MapFile`, stash on `CurrentMap`) slots in at `:86`, before the Map.tscn swap.**
  No `CurrentMap` property exists yet — Task 7 adds it.
- `Scripts/MapScene/MapScene.cs:9-14` — placeholder `Node2D`, `_Ready` prints "Entered map".
  **Task 7 replaces this** as the world root with `MapManager`.
- `Scenes/Map.tscn` — `Node2D "Map"` (script `MapScene.cs`) + a `Label "StatusLabel"`. Task 7 rebuilds it.
- `Scripts/Network/Packets/` (read in full):
  - `TileUpdatePacket.cs:8-33` — `int X, Y` (already `-1`), `int[10] Tiles`
    (`Tiles[l*2]`=graphic, `Tiles[l*2+1]`=sheet), `int Flags`. Prefix `TUP`.
  - `MapObjectPacket.cs:8-64` — `GraphicId, GraphicFile, TileX, TileY (−1), Title, Name, …,
    GraphicR/G/B/A`. Prefix `DOB`. (`*` sentinel ⇒ RGBA all 0.)
  - `EraseObjectPacket.cs:8-21` — `int TileX, TileY` (−1). Prefix `EOB`.
  - `SendCurrentMapPacket.cs:8-26` — `string MapFileName, int MapVersion, string MapName`. Prefix `SCM`.
  - `SetYourPositionPacket.cs:8-21` — `int MapX, MapY` (already `-1`). Prefix `SUP`.
- `Goose2ClientGodot.csproj:9-13` — `<Compile Remove="tools/**" />`, `tests/**`, `.godot/**`.
  New runtime scripts under `Scripts/` are auto-included.
- `tests/Goose2Client.Tests/Goose2Client.Tests.csproj:13-17` — links source files individually via
  `<Compile Include="../../Scripts/..."/>` (NOT a project ref). Pure-C# Step 5 files
  (`MapFile.cs`, `MapCoords.cs`, `SpriteManifest.cs`) are added here the same way so they're unit-
  testable without Godot. Target `net10.0`, xUnit.
- `tools/AssetConverter/tests/AssetConverter.Tests/` — xUnit, the converter's own test project.

Godot 4.6 C# APIs used (verified against Godot 4.6 docs):

- `Node2D._Draw()` override; inside it `DrawTextureRectRegion(Texture2D, Rect2 rect, Rect2 srcRect)`
  and/or `DrawTexture(Texture2D, Vector2 position)`. Trigger redraw with `QueueRedraw()`.
- `AtlasTexture` : `Texture2D` — properties `Atlas` (`Texture2D`), `Region` (`Rect2`). Construct in C#
  `new AtlasTexture { Atlas = sheet, Region = new Rect2(x, y, w, h) }`.
- `GD.Load<Texture2D>("res://…png")` / `ResourceLoader.Exists(path)`.
- `Camera2D` : `Node2D` — set `Position`/`GlobalPosition`; `Enabled`/`MakeCurrent()`.
- `Json` / `System.Text.Json` `JsonSerializer.Deserialize<T>` (project already uses `System.Text.Json`
  in `CharacterSettings.cs`).

---

## Conventions for the implementer

- **Source of truth is the Unity project.** Port `MapFile.cs` and the map-relevant `MapManager`
  handlers by reading them, not this plan's summaries. Keep field/method names identical; only the
  engine API and the Y-flip-removal change.
- **Namespaces / layout:** everything under `Goose2Client.*`. New files:
  `Scripts/MapFile.cs` (namespace `Goose2Client`), `Scripts/Map/MapCoords.cs`,
  `Scripts/Map/SpriteManifest.cs`, `Scripts/Map/SpriteCache.cs`, `Scripts/Map/MapLayer.cs`,
  `Scripts/Map/MapItem.cs`, `Scripts/MapManager.cs` (namespace `Goose2Client.Map` for the helpers,
  `Goose2Client` for `MapFile`/`MapManager` to match Unity).
- **No Y-flip, ever.** If you catch yourself writing `Height - y`, stop — that's the Unity-Y-up
  artifact this port removes. The only coordinate transform lives in `MapCoords`.
- **Bottom-center anchoring.** Tiles and items anchor at the **bottom-center** of their cell (tall
  sprites grow upward). `MapCoords` provides the anchor point; drawing offsets the texture so its
  bottom-center lands there. Validate visually in Task 10 and adjust **only** `MapCoords`.
- **Scene-lifecycle gotcha (load-bearing, same rule as Step 4):** every `Listen<T>` / `+=` in a scene
  script MUST be matched by a `Remove<T>` / `-=` in that scene's `_ExitTree`. `MapManager` lives on
  `Map.tscn`, which is freed on the next `ChangeMap` — dangling listeners fire into a freed node and
  crash. **Do not skip cleanup.**
- **Threading rule (unchanged):** packets marshal to the main thread via `GameManager.HandlePacket`;
  all `MapManager` handlers run on the main thread, so node mutation is safe with no locking.
- **TDD for the pure layers, smoke for the Godot layers.** `MapFile`, `MapCoords`, `SpriteManifest`,
  and the converter manifest are pure C# → xUnit (one failing test → minimal code → green → commit,
  see @superpowers:test-driven-development). `SpriteCache` / `MapLayer` / `MapManager` / `MapItem`
  touch `Texture2D`/`Node2D` → validated by the **live run** in Task 10, not fake-based unit tests.
- **Asset volume is not committed.** Generated `Assets/Sprites/sheets/*.png`, `Assets/Maps/*.bytes`,
  and `Assets/Sprites/manifest.json` are build artifacts — add them to `.gitignore` (Task 0), commit
  only code.

---

### Task 0: Prerequisites — fix converter paths, run it, gitignore artifacts

**Files:**
- Modify: `tools/AssetConverter/src/AssetConverter/Paths.cs:7-9`
- Create/Modify: `.gitignore`
- Modify: `tests/Goose2Client.Tests/Goose2Client.Tests.csproj` (link `MapFile.cs` for Task 1)

**Step 1:** Fix the sandbox paths. In `Paths.cs`, change the three constants to this machine's layout:
```csharp
    public const string IllutiaData = "/home/hayden/code/Illutia/data";
    public const string IllutiaMaps = "/home/hayden/code/Illutia/maps";
    public const string UnitySpritesheets =
        "/home/hayden/code/Goose2Client/Assets/Spritesheets";
```

**Step 2:** Confirm the source data is present:
```bash
ls /home/hayden/code/Illutia/maps/*.map | wc -l     # expect ~114
ls /home/hayden/code/Illutia/data/*.adf | wc -l      # expect ~4956
```
Expected: non-zero counts. If zero, STOP — the data moved; update `Paths.cs` to the real location.

**Step 3:** Verify the converter's own tests still pass after the path fix:
```bash
cd /home/hayden/code/Goose2ClientGodot/tools/AssetConverter && dotnet test
```
Expected: green (AdfFile, GifLoader, GoldenImage, BatchConverter, MapCopyConverter, …).

**Step 4:** Run the full asset generation into the Godot project:
```bash
cd /home/hayden/code/Goose2ClientGodot/tools/AssetConverter
dotnet run --project src/AssetConverter -- all /home/hayden/code/Goose2ClientGodot
ls /home/hayden/code/Goose2ClientGodot/Assets/Sprites/sheets/*.png | wc -l   # thousands
ls /home/hayden/code/Goose2ClientGodot/Assets/Maps/*.bytes | wc -l           # ~114
```
Expected: thousands of PNGs and ~114 `.bytes`. The printed failure list should contain only
sound/non-graphic `.adf`s. (The manifest is added in Task 2; ignore its absence for now.)

**Step 5:** Add a `.gitignore` rule so the generated volume is never committed:
```gitignore
# Generated by tools/AssetConverter — regenerate with `AssetConverter all`
/Assets/Sprites/sheets/
/Assets/Maps/
/Assets/Sprites/manifest.json
```

**Step 6:** Wire `MapFile.cs` (created in Task 1) into the unit-test project so Task 1 can compile.
Add under the existing `<ItemGroup>` of `tests/Goose2Client.Tests/Goose2Client.Tests.csproj`:
```xml
    <Compile Include="../../Scripts/MapFile.cs" />
```

**Step 7:** Commit (code only — artifacts are gitignored):
```bash
cd /home/hayden/code/Goose2ClientGodot
git add tools/AssetConverter/src/AssetConverter/Paths.cs .gitignore tests/
git commit -m "chore: fix AssetConverter paths, gitignore generated assets, link MapFile for tests"
```

---

### Task 1: Port `MapFile` (binary parser) + golden test against a real map

**Files:**
- Create: `Scripts/MapFile.cs`
- Test: `tests/Goose2Client.Tests/MapFileTests.cs`

**Step 1: Write the failing test.** Parse a real generated map and assert the header + a known
property. (Header of `Map1.bytes` is `Version=146, EditorVersion=10, Width=286, Height=194` — verified
from the on-disk bytes `92 00 0A 00 1E 01 00 00 C2 00 00 00`.)
```csharp
using System.IO;
using Goose2Client;
using Xunit;

public class MapFileTests
{
    private const string MapsDir = "/home/hayden/code/Goose2ClientGodot/Assets/Maps";

    [Fact]
    public void Map1_ParsesHeaderAndGrid()
    {
        var bytes = File.ReadAllBytes(Path.Combine(MapsDir, "Map1.bytes"));
        var map = new MapFile(bytes);

        Assert.Equal(146, map.Version);
        Assert.Equal(10, map.EditorVersion);
        Assert.Equal(286, map.Width);
        Assert.Equal(194, map.Height);
        Assert.Equal(286 * 194, map.Tiles.Length);

        // File length must equal header(12) + 34 bytes/tile.
        Assert.Equal(12 + 34 * map.Width * map.Height, bytes.Length);

        // Indexer is (x, y) → Tiles[y*Width + x]; first tile is reachable and well-formed.
        var t = map[0, 0];
        Assert.Equal(5, t.Layers.Length);
        Assert.All(t.Layers, l => Assert.NotNull(l));
    }

    [Fact]
    public void MapTile_FlagsAndRoofDerive()
    {
        var blocked = new MapTile { Flags = 2 };
        Assert.True(blocked.IsBlocked);

        var open = new MapTile { Flags = 0 };
        Assert.False(open.IsBlocked);
        Assert.False(open.IsRoof);

        open.Layers[4].Graphic = 99;
        Assert.True(open.IsRoof);
    }
}
```

**Step 2: Run to verify it fails.**
Run: `cd /home/hayden/code/Goose2ClientGodot/tests/Goose2Client.Tests && dotnet test --filter MapFileTests`
Expected: FAIL — `MapFile` / `MapTile` do not exist (compile error).

**Step 3: Port the parser.** Copy `~/code/Goose2Client/Assets/Scripts/MapFile.cs:1-96` into
`Scripts/MapFile.cs` **verbatim**, applying only:
- Keep `namespace Goose2Client`.
- Keep `using System; using System.IO;`. Drop `System.Collections.Generic`, `System.Linq`,
  `System.Text` (unused).
- No logic changes. The read order (`ReadInt16` Version/EditorVersion, `ReadInt32` Width/Height,
  per tile `ReadInt32` Flags + 5×(`ReadInt32` Graphic, `ReadInt16` Sheet)) and the row-major
  `Tiles[i*Width + j]` indexing are load-bearing — they must stay byte-identical.
- `MapTile()` ctor must `new` each of the 5 `Layers[k]` (the test sets `Layers[4].Graphic`); the
  Unity source allocates `Layers` but fills entries in the read loop. **Add `Layers[k] = new Layer()`
  in the ctor** so an in-memory `MapTile` (no file) has non-null layers. (For the file path the read
  loop overwrites them — harmless.)

**Step 4: Run to verify it passes.**
Run: `dotnet test --filter MapFileTests`
Expected: PASS (2 tests).
> If `Map1_ParsesHeaderAndGrid` fails on the byte-length assert, the per-tile read drifted — re-diff
> the read order against `MapFile.cs:74-83`. The header values are derived from the real file, not
> guessed.

**Step 5: Commit.**
```bash
git add Scripts/MapFile.cs tests/Goose2Client.Tests/MapFileTests.cs
git commit -m "feat: port MapFile binary parser with golden test vs real map"
```

---

### Task 2: Converter — emit the frame-rect manifest (`manifest.json`)

The renderer needs, at runtime, the pixel rect of any `(sheet, graphic)` to build an `AtlasTexture`.
The `.adf` `Frame` table holds exactly that. Emit one JSON manifest mapping every graphic sheet's
frame indices to rects.

**Files:**
- Create: `tools/AssetConverter/src/AssetConverter/Manifest/FrameManifestBuilder.cs`
- Modify: `tools/AssetConverter/src/AssetConverter/Program.cs`
- Test: `tools/AssetConverter/tests/AssetConverter.Tests/FrameManifestBuilderTests.cs`

**Step 1: Write the failing test.** Build the manifest for the golden sheet 1000 (8 frames, each
48×64, indices 108760–108767 — from `2026-06-04-asset-converter-vertical-slice.md:50-61`).
```csharp
using System.Text.Json;
using Goose2.AssetConverter;
using Goose2.AssetConverter.Manifest;
using Xunit;

public class FrameManifestBuilderTests
{
    [Fact]
    public void Build_EmitsSheetGraphicRects_ForSheet1000()
    {
        string json = FrameManifestBuilder.Build(Paths.IllutiaData, onlyFileNumbers: new[] { 1000 });

        using var doc = JsonDocument.Parse(json);
        var sheets = doc.RootElement.GetProperty("sheets");
        var sheet1000 = sheets.GetProperty("1000");

        // graphic 108760 → [0,0,48,64]; graphic 108767 → [48,192,48,64]
        var first = sheet1000.GetProperty("108760");
        Assert.Equal(0,  first[0].GetInt32());
        Assert.Equal(0,  first[1].GetInt32());
        Assert.Equal(48, first[2].GetInt32());
        Assert.Equal(64, first[3].GetInt32());

        var last = sheet1000.GetProperty("108767");
        Assert.Equal(48,  last[0].GetInt32());
        Assert.Equal(192, last[1].GetInt32());

        Assert.Equal(8, sheet1000.EnumerateObject().Count());
    }
}
```

**Step 2: Run to verify it fails.**
Run: `cd /home/hayden/code/Goose2ClientGodot/tools/AssetConverter && dotnet test --filter FrameManifestBuilderTests`
Expected: FAIL — `FrameManifestBuilder` does not exist.

**Step 3: Implement the builder.** Iterate the graphic `.adf`s (reuse the `AdfFile` parser + the
`AdfType.Graphic` gate from `BatchConverter.cs:34-40`), and serialize `sheet → {graphicIndex → [X,Y,W,H]}`.
```csharp
using System.Text.Json;
using Goose2.AssetConverter.Adf;

namespace Goose2.AssetConverter.Manifest;

/// <summary>Emits a JSON manifest mapping every graphic sheet's frame index to its pixel rect,
/// so the Godot runtime can build an AtlasTexture for any (sheet, graphic) without re-parsing .adf.
/// Shape: { "tileSize": 32, "sheets": { "<sheet>": { "<graphic>": [x,y,w,h], ... }, ... } }.</summary>
public static class FrameManifestBuilder
{
    public static string Build(string dataDir, int[]? onlyFileNumbers = null)
    {
        var only = onlyFileNumbers is null ? null : new HashSet<int>(onlyFileNumbers);
        var sheets = new SortedDictionary<string, Dictionary<string, int[]>>();

        foreach (var file in Directory.EnumerateFiles(dataDir, "*.adf"))
        {
            int fileNumber = int.Parse(Path.GetFileNameWithoutExtension(file));
            if (only is not null && !only.Contains(fileNumber)) continue;

            AdfFile adf;
            try { adf = new AdfFile(file); }
            catch { continue; }                       // non-graphic / undecodable → skip (BatchConverter logs those)
            if (adf.Type != AdfType.Graphic) continue;

            var frames = new Dictionary<string, int[]>(adf.Frames.Count);
            foreach (var f in adf.Frames)
                frames[f.Index.ToString()] = new[] { f.X, f.Y, f.W, f.H };

            sheets[fileNumber.ToString()] = frames;
        }

        var root = new { tileSize = 32, sheets };
        return JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = false });
    }
}
```

**Step 4: Run to verify it passes.**
Run: `dotnet test --filter FrameManifestBuilderTests`
Expected: PASS.

**Step 5: Add a `manifest` command + fold into `all`.** In `Program.cs`, add before the final usage
line:
```csharp
if (args.Length >= 1 && args[0] == "manifest")
{
    string outPath = args.Length >= 2
        ? args[1]
        : Path.GetFullPath(Path.Combine("..", "..", "Assets", "Sprites", "manifest.json"));
    Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
    File.WriteAllText(outPath, Goose2.AssetConverter.Manifest.FrameManifestBuilder.Build(Paths.IllutiaData));
    Console.WriteLine($"Wrote {outPath}");
    return;
}
```
And inside the existing `all` branch, after the maps copy, also write the manifest to
`<repoRoot>/Assets/Sprites/manifest.json` and print a line for it.

**Step 6: Generate it for real.**
```bash
dotnet run --project src/AssetConverter -- manifest
ls -la /home/hayden/code/Goose2ClientGodot/Assets/Sprites/manifest.json
```
Expected: a multi-MB JSON exists.

**Step 7: Commit** (code only; `manifest.json` is gitignored):
```bash
cd /home/hayden/code/Goose2ClientGodot
git add tools/AssetConverter/
git commit -m "feat: AssetConverter emits frame-rect manifest for tile rendering"
```

---

### Task 3: `MapCoords` — the single tile↔world helper (no Y-flip)

**Files:**
- Create: `Scripts/Map/MapCoords.cs`
- Test: `tests/Goose2Client.Tests/MapCoordsTests.cs`
- Modify: `tests/Goose2Client.Tests/Goose2Client.Tests.csproj` (link `MapCoords.cs`)

> `MapCoords` uses `Godot.Vector2`. `GodotSharp` is already referenced by the test project
> (`Goose2Client.Tests.csproj:8`), so `Vector2` math is testable headlessly without an engine.

**Step 1: Link the file for tests.** Add to the test csproj `<ItemGroup>`:
```xml
    <Compile Include="../../Scripts/Map/MapCoords.cs" />
```

**Step 2: Write the failing test.** Lock 32 px tiles, Y-down, bottom-center anchoring.
```csharp
using Godot;
using Goose2Client.Map;
using Xunit;

public class MapCoordsTests
{
    [Fact]
    public void TileSize_Is32() => Assert.Equal(32, MapCoords.TileSize);

    [Fact]
    public void TileCenter_NoYFlip()
    {
        // Tile (0,0) is top-left of the world (Godot Y-down) — center at (16,16), NOT flipped.
        Assert.Equal(new Vector2(16, 16), MapCoords.TileCenter(0, 0));
        Assert.Equal(new Vector2(48, 80), MapCoords.TileCenter(1, 2));
    }

    [Fact]
    public void TileBottomCenter_IsCellBottomEdgeMidpoint()
    {
        // Bottom-center anchor of tile (0,0): x=16, y=32 (bottom edge of the 32px cell).
        Assert.Equal(new Vector2(16, 32), MapCoords.TileBottomCenter(0, 0));
        Assert.Equal(new Vector2(48, 96), MapCoords.TileBottomCenter(1, 2));
    }

    [Fact]
    public void WorldToTile_RoundTrips()
    {
        Assert.Equal((3, 5), MapCoords.WorldToTile(new Vector2(3 * 32 + 5, 5 * 32 + 5)));
    }
}
```

**Step 3: Implement.**
```csharp
using Godot;

namespace Goose2Client.Map;

/// <summary>The ONE place tile↔world conversion happens. Godot 2D is Y-down (like the server's
/// tile rows), so there is NO vertical flip here — tile (x,y) maps to world (x,y). Unity's pervasive
/// `map.Height - y` was only to reach Unity's Y-up world and is intentionally absent.</summary>
public static class MapCoords
{
    public const int TileSize = 32;   // 1 tile = 32 px (Unity pixelsPerUnit = 32)

    /// <summary>Center of tile (x,y) in world pixels.</summary>
    public static Vector2 TileCenter(int x, int y)
        => new(x * TileSize + TileSize / 2f, y * TileSize + TileSize / 2f);

    /// <summary>Bottom-center of tile (x,y): horizontal center, bottom edge of the cell.
    /// Tiles/items anchor here so tall sprites grow upward.</summary>
    public static Vector2 TileBottomCenter(int x, int y)
        => new(x * TileSize + TileSize / 2f, (y + 1) * TileSize);

    /// <summary>World pixel → tile coords (floor).</summary>
    public static (int x, int y) WorldToTile(Vector2 world)
        => ((int)(world.X / TileSize), (int)(world.Y / TileSize));
}
```

**Step 4: Run to verify it passes.**
Run: `dotnet test --filter MapCoordsTests`
Expected: PASS (4 tests).

**Step 5: Commit.**
```bash
git add Scripts/Map/MapCoords.cs tests/Goose2Client.Tests/MapCoordsTests.cs tests/Goose2Client.Tests/Goose2Client.Tests.csproj
git commit -m "feat: add MapCoords tile<->world helper (32px, Y-down, no flip)"
```

---

### Task 4: `SpriteManifest` — load the frame-rect manifest

**Files:**
- Create: `Scripts/Map/SpriteManifest.cs`
- Test: `tests/Goose2Client.Tests/SpriteManifestTests.cs`
- Modify: `tests/Goose2Client.Tests/Goose2Client.Tests.csproj` (link `SpriteManifest.cs`)

**Step 1: Link for tests.** Add to the test csproj:
```xml
    <Compile Include="../../Scripts/Map/SpriteManifest.cs" />
```

**Step 2: Write the failing test** against a small inline fixture (so the test is hermetic — it does
not depend on the multi-MB generated manifest).
```csharp
using Goose2Client.Map;
using Xunit;

public class SpriteManifestTests
{
    private const string Json =
        "{\"tileSize\":32,\"sheets\":{\"1000\":{\"108760\":[0,0,48,64],\"108767\":[48,192,48,64]}}}";

    [Fact]
    public void TryGetRect_ReturnsRectForKnownSheetGraphic()
    {
        var m = SpriteManifest.Parse(Json);

        Assert.True(m.TryGetRect(1000, 108760, out var r));
        Assert.Equal((0, 0, 48, 64), (r.X, r.Y, r.W, r.H));

        Assert.True(m.TryGetRect(1000, 108767, out var r2));
        Assert.Equal((48, 192, 48, 64), (r2.X, r2.Y, r2.W, r2.H));
    }

    [Fact]
    public void TryGetRect_FalseForUnknown()
    {
        var m = SpriteManifest.Parse(Json);
        Assert.False(m.TryGetRect(9999, 1, out _));
        Assert.False(m.TryGetRect(1000, 1, out _));
    }
}
```

**Step 3: Implement** (pure `System.Text.Json`, no Godot — keeps it unit-testable; the runtime
`SpriteCache` turns these rects into `AtlasTexture`s in Task 5).
```csharp
using System.Collections.Generic;
using System.Text.Json;

namespace Goose2Client.Map;

public readonly record struct FrameRect(int X, int Y, int W, int H);

/// <summary>In-memory view of Assets/Sprites/manifest.json: (sheet, graphic) → pixel rect.
/// Parser only — turning a rect into a Godot AtlasTexture is SpriteCache's job.</summary>
public sealed class SpriteManifest
{
    private readonly Dictionary<int, Dictionary<int, FrameRect>> _sheets;

    private SpriteManifest(Dictionary<int, Dictionary<int, FrameRect>> sheets) => _sheets = sheets;

    public bool TryGetRect(int sheet, int graphic, out FrameRect rect)
    {
        rect = default;
        return _sheets.TryGetValue(sheet, out var g) && g.TryGetValue(graphic, out rect);
    }

    public static SpriteManifest Parse(string json)
    {
        var sheets = new Dictionary<int, Dictionary<int, FrameRect>>();
        using var doc = JsonDocument.Parse(json);
        foreach (var sheet in doc.RootElement.GetProperty("sheets").EnumerateObject())
        {
            var frames = new Dictionary<int, FrameRect>();
            foreach (var frame in sheet.Value.EnumerateObject())
            {
                var a = frame.Value;
                frames[int.Parse(frame.Name)] =
                    new FrameRect(a[0].GetInt32(), a[1].GetInt32(), a[2].GetInt32(), a[3].GetInt32());
            }
            sheets[int.Parse(sheet.Name)] = frames;
        }
        return new SpriteManifest(sheets);
    }

    public static SpriteManifest Load(string path) => Parse(System.IO.File.ReadAllText(path));
}
```

**Step 4: Run to verify it passes.**
Run: `dotnet test --filter SpriteManifestTests`
Expected: PASS (2 tests).

**Step 5: Commit.**
```bash
git add Scripts/Map/SpriteManifest.cs tests/Goose2Client.Tests/SpriteManifestTests.cs tests/Goose2Client.Tests/Goose2Client.Tests.csproj
git commit -m "feat: add SpriteManifest loader for frame-rect manifest"
```

---

### Task 5: `SpriteCache` — `(sheet, graphic)` → `AtlasTexture` (the `ResourceManager` replacement)

This is the Godot replacement for Unity's `ResourceManager.LoadSprite("{sheet}-{graphic}")` /
`Helpers.GetSprite` (`Helpers.cs:10-13`). Godot-coupled (`Texture2D`/`AtlasTexture`) → validated by
the live run in Task 10, not a fake-based unit test.

**Files:**
- Create: `Scripts/Map/SpriteCache.cs`

**Step 1: Implement.** Lazy-load each sheet PNG once; build + cache one `AtlasTexture` per
`(sheet, graphic)`. Missing sheet/graphic → `null` (caller skips — matches Unity treating sheet 0 /
empty as "no tile").
```csharp
using System.Collections.Generic;
using Godot;

namespace Goose2Client.Map;

/// <summary>Runtime sprite lookup: (sheet, graphic) → AtlasTexture region of the sheet PNG.
/// Replaces Unity's ResourceManager.LoadSprite("{sheet}-{graphic}") / Helpers.GetSprite.
/// Sheets load lazily from res://Assets/Sprites/sheets/{sheet}.png; rects come from the manifest.</summary>
public sealed class SpriteCache
{
    private const string SheetsDir = "res://Assets/Sprites/sheets";
    private const string ManifestPath = "res://Assets/Sprites/manifest.json";

    private readonly SpriteManifest _manifest;
    private readonly Dictionary<int, Texture2D> _sheets = new();
    private readonly Dictionary<(int, int), AtlasTexture> _tiles = new();

    public SpriteCache() : this(SpriteManifest.Load(ProjectSettings.GlobalizePath(ManifestPath))) { }
    public SpriteCache(SpriteManifest manifest) => _manifest = manifest;

    /// <summary>The AtlasTexture for (sheet, graphic), or null when sheet==0, the manifest has no
    /// such rect, or the PNG is missing.</summary>
    public AtlasTexture Get(int sheet, int graphic)
    {
        if (sheet == 0) return null;
        var key = (sheet, graphic);
        if (_tiles.TryGetValue(key, out var cached)) return cached;

        if (!_manifest.TryGetRect(sheet, graphic, out var r)) return null;
        var tex = LoadSheet(sheet);
        if (tex == null) return null;

        var atlas = new AtlasTexture { Atlas = tex, Region = new Rect2(r.X, r.Y, r.W, r.H) };
        _tiles[key] = atlas;
        return atlas;
    }

    private Texture2D LoadSheet(int sheet)
    {
        if (_sheets.TryGetValue(sheet, out var t)) return t;
        var path = $"{SheetsDir}/{sheet}.png";
        var tex = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
        if (tex == null) GD.PushWarning($"SpriteCache: missing sheet PNG {path}");
        _sheets[sheet] = tex;
        return tex;
    }
}
```
> **Import note (load-bearing):** the sheet PNGs must import with **nearest/point filtering** (no
> mipmaps) for pixel-art crispness. In Task 10, set the editor Import dock default for
> `Assets/Sprites/sheets/` to Filter = off, or add a `.godot`-level import preset. If tiles look
> blurry, this is why.

**Step 2: Build clean.**
Run: `cd /home/hayden/code/Goose2ClientGodot && dotnet build`
Expected: `Build succeeded`, 0 errors. (No unit test — exercised live in Task 10.)

**Step 3: Commit.**
```bash
git add Scripts/Map/SpriteCache.cs
git commit -m "feat: add SpriteCache (sheet,graphic)->AtlasTexture, ResourceManager replacement"
```

---

### Task 6: `MapLayer` — draw one tile layer via `_Draw`

**Files:**
- Create: `Scripts/Map/MapLayer.cs`

**Step 1: Implement.** One `Node2D` per layer index (0–4). In `_Draw`, walk the grid and blit each
non-empty cell's `AtlasTexture` at the cell's bottom-center. Redraw on demand via `QueueRedraw()`
(after a tile update).
```csharp
using Godot;

namespace Goose2Client.Map;

/// <summary>Renders a single map layer (0..4) by drawing each non-empty cell's AtlasTexture at the
/// cell's bottom-center. No TileMapLayer: the source art is arbitrary-rect + bottom-center anchored,
/// which draws directly here. z_index orders the 5 layers; layer 4 (roofs) toggles Visible.</summary>
public partial class MapLayer : Node2D
{
    private MapFile _map;
    private int _layer;
    private SpriteCache _cache;

    public void Setup(MapFile map, int layer, SpriteCache cache)
    {
        _map = map;
        _layer = layer;
        _cache = cache;
        ZIndex = layer;          // 0 ground … 4 roof, painter order
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_map == null) return;

        for (int y = 0; y < _map.Height; y++)
        {
            for (int x = 0; x < _map.Width; x++)
            {
                var l = _map[x, y].Layers[_layer];
                if (l.Graphic == 0) continue;             // empty cell

                var tex = _cache.Get(l.Sheet, l.Graphic);
                if (tex == null) continue;

                DrawCell(x, y, tex);
            }
        }
    }

    private void DrawCell(int x, int y, Texture2D tex)
    {
        var size = tex.GetSize();
        var anchor = MapCoords.TileBottomCenter(x, y);    // bottom-center of the cell
        var topLeft = new Vector2(anchor.X - size.X / 2f, anchor.Y - size.Y);
        DrawTexture(tex, topLeft);
    }
}
```
> **Perf note (acceptable for Step 5, optimization deferred):** `_Draw` redraws the whole layer. It
> fires once on `Setup` and again only when a `TileUpdate` touches this layer (Task 9), so the dense
> ground layer's ~50k draw calls happen on map enter, not per frame. If map-enter latency is
> noticeable in Task 10, the easy win is **viewport culling** (skip cells outside
> `GetViewportRect()` transformed to world) — note it, don't build it now (YAGNI).

**Step 2: Build clean.**
Run: `dotnet build`
Expected: `Build succeeded`.

**Step 3: Commit.**
```bash
git add Scripts/Map/MapLayer.cs
git commit -m "feat: add MapLayer _Draw renderer for one tile layer"
```

---

### Task 7: `MapManager` + `GameManager.CurrentMap` + load in `ChangeMap`

Replace the `Map.tscn` placeholder with the world root. `MapManager` builds the 5 `MapLayer`s, owns
the `Camera2D`, and (this task) wires the packet listeners' registration/cleanup. `GameManager` gains
`CurrentMap` and parses the map at the `// Step 5 hook`.

**Files:**
- Modify: `Scripts/GameManager.cs` (add `CurrentMap`; load map in `ChangeMap`)
- Create: `Scripts/MapManager.cs`  (delete/replace `Scripts/MapScene/MapScene.cs`)
- Modify: `Scenes/Map.tscn` (root → `MapManager`, add `Camera2D` + `Layers`/`Objects` containers)

**Step 1: `GameManager.CurrentMap` + map load.** In `Scripts/GameManager.cs`:
- Add `using Goose2Client;` is implicit (same namespace). Add property:
  ```csharp
  /// <summary>The parsed map for the scene currently being entered. Set in ChangeMap, read by MapManager._Ready.</summary>
  public MapFile CurrentMap { get; set; }
  ```
- Replace the `// Step 5 hook` line (`GameManager.cs:86`) with the actual load, BEFORE the Map.tscn swap:
  ```csharp
  CurrentMap = LoadMap(mapFile);
  ```
- Add the helper (resolves `res://Assets/Maps/{MapFileName}.bytes`; the live run in Task 10 confirms
  the exact `MapFileName` string and we adjust this one line if needed):
  ```csharp
  private MapFile LoadMap(string mapFile)
  {
      var path = $"res://Assets/Maps/{mapFile}.bytes";
      using var f = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
      if (f == null)
      {
          GD.PushError($"LoadMap: cannot open {path} (err {Godot.FileAccess.GetOpenError()})");
          return null;
      }
      return new MapFile(f.GetBuffer((long)f.GetLength()));
  }
  ```
  > `Godot.FileAccess` reads from `res://` in exported builds (a raw `System.IO.File` would not).
  > `MapFile(byte[])` already exists (`MapFile.cs:53`).

**Step 2: `MapManager` skeleton.** Create `Scripts/MapManager.cs` as the `Map.tscn` root. Build the
layers from `GameManager.Instance.CurrentMap`; register the in-scope listeners; clean them up.
```csharp
using Godot;
using Goose2Client.Map;
using Goose2Client.Network.Packets;

namespace Goose2Client;

/// <summary>World root for the active map (port of Unity MapManager, map/tile/item subset;
/// character handling is Step 6). Builds the 5 MapLayer nodes, runs the Camera2D, and handles
/// TileUpdate / MapObject / EraseObject / SetYourPosition.</summary>
public partial class MapManager : Node2D
{
    private MapFile _map;
    private SpriteCache _cache;
    private readonly MapLayer[] _layers = new MapLayer[5];
    private Node2D _objects;     // dropped-item container
    private Camera2D _camera;

    public override void _Ready()
    {
        _map = GameManager.Instance.CurrentMap;
        _cache = new SpriteCache();
        _objects = GetNode<Node2D>("Objects");
        _camera = GetNode<Camera2D>("Camera2D");

        if (_map == null) { GD.PushError("MapManager: CurrentMap is null"); return; }

        var layersRoot = GetNode<Node2D>("Layers");
        for (int i = 0; i < 5; i++)
        {
            var layer = new MapLayer { Name = $"Layer{i}" };
            layersRoot.AddChild(layer);
            layer.Setup(_map, i, _cache);
            _layers[i] = layer;
        }

        var pm = GameManager.Instance.PacketManager;
        pm.Listen<TileUpdatePacket>(OnTileUpdate);
        pm.Listen<MapObjectPacket>(OnMapObject);
        pm.Listen<EraseObjectPacket>(OnEraseObject);
        pm.Listen<SetYourPositionPacket>(OnSetYourPosition);
    }

    public override void _ExitTree()
    {
        var pm = GameManager.Instance.PacketManager;
        pm.Remove<TileUpdatePacket>(OnTileUpdate);
        pm.Remove<MapObjectPacket>(OnMapObject);
        pm.Remove<EraseObjectPacket>(OnEraseObject);
        pm.Remove<SetYourPositionPacket>(OnSetYourPosition);
    }

    /// <summary>Bounds + blocked check (Unity IsValidMove, map-only part; occupancy is Step 6).</summary>
    public bool IsValidMove(int x, int y)
        => x >= 0 && y >= 0 && x < _map.Width && y < _map.Height && !_map[x, y].IsBlocked;

    private void OnSetYourPosition(object packetObj)
    {
        var p = (SetYourPositionPacket)packetObj;
        _camera.GlobalPosition = MapCoords.TileCenter(p.MapX, p.MapY);
        UpdateRoofVisibility(p.MapX, p.MapY);
    }

    /// <summary>Roof layer hides when the player stands under it (Unity roofLayer.SetActive(!IsRoof)).</summary>
    private void UpdateRoofVisibility(int x, int y)
    {
        if (x < 0 || y < 0 || x >= _map.Width || y >= _map.Height) return;
        _layers[4].Visible = !_map[x, y].IsRoof;
    }

    private void OnTileUpdate(object packetObj) { /* Task 9 */ }
    private void OnMapObject(object packetObj) { /* Task 8 */ }
    private void OnEraseObject(object packetObj) { /* Task 8 */ }
}
```

**Step 3: Rebuild `Scenes/Map.tscn`.** Root `Node2D "Map"` with script `MapManager.cs`,
`y_sort_enabled = true` (so Step 6 entities sort against object layers). Children:
- `Camera2D "Camera2D"`
- `Node2D "Layers"`  (the 5 `MapLayer`s are added at runtime)
- `Node2D "Objects"` (dropped items; `y_sort_enabled = true`)
Drop the placeholder `StatusLabel`. (.tscn text — model it on `2026-06-06-gamemanager-and-scene-flow.md`
Task 0's scene scaffolding; or build it in the editor and save.)

**Step 4: Delete the placeholder.** Remove `Scripts/MapScene/MapScene.cs` (its `MapScene` class is
superseded by `MapManager`). Confirm no other file references `MapScene`:
```bash
grep -rn "MapScene" Scripts Scenes | grep -v "MapScene/"   # expect: no matches (the folder is gone)
```

**Step 5: Build clean + editor smoke.**
Run: `dotnet build` → `Build succeeded`. Open the editor once; confirm `Map.tscn` opens with
`Camera2D` + `Layers` + `Objects` and no script errors.

**Step 6: Commit.**
```bash
git add Scripts/GameManager.cs Scripts/MapManager.cs Scenes/Map.tscn
git rm Scripts/MapScene/MapScene.cs
git commit -m "feat: MapManager world root, GameManager.CurrentMap, load map in ChangeMap"
```

---

### Task 8: Map items (drops) — `MapObject` / `EraseObject`

**Files:**
- Create: `Scripts/Map/MapItem.cs`
- Modify: `Scripts/MapManager.cs` (`OnMapObject`, `OnEraseObject`, container bookkeeping)

**Step 1: `MapItem` node.** A `Sprite2D` anchored bottom-center with optional tint
(`Modulate` — the migration-plan §5 replacement for Unity's `_Tint` material;
`MIGRATION_PLAN.md:153-154`).
```csharp
using Godot;

namespace Goose2Client.Map;

/// <summary>A dropped item on the ground. Sprite anchored bottom-center; tint via Modulate
/// (replaces Unity's material _Tint). Tooltip/interaction is Step 7/8.</summary>
public partial class MapItem : Sprite2D
{
    public void Setup(AtlasTexture tex, int tileX, int tileY, Color tint)
    {
        Texture = tex;
        Centered = false;
        var size = tex.GetSize();
        var anchor = MapCoords.TileBottomCenter(tileX, tileY);
        Position = new Vector2(anchor.X - size.X / 2f, anchor.Y - size.Y);
        if (tint.A > 0) Modulate = tint;     // RGBA all-0 sentinel ⇒ no tint (MapObjectPacket '*' case)
    }
}
```

**Step 2: Wire the handlers** in `MapManager`. Key by tile like Unity
(`mapObjects[TileY*Height + TileX]`, `MapManager.cs` reported `:310`/`:318`):
```csharp
private readonly System.Collections.Generic.Dictionary<int, MapItem> _mapObjects = new();

private int ItemKey(int x, int y) => y * _map.Height + x;

private void OnMapObject(object packetObj)
{
    var p = (MapObjectPacket)packetObj;
    var tex = _cache.Get(p.GraphicId, p.GraphicFile);   // note: Unity GetSprite(id=GraphicId, file=GraphicFile)
    if (tex == null) return;

    if (_mapObjects.TryGetValue(ItemKey(p.TileX, p.TileY), out var existing))
        existing.QueueFree();

    var item = new MapItem { Name = $"{p.Name} ({p.GraphicId})" };
    _objects.AddChild(item);
    item.Setup(tex, p.TileX, p.TileY,
        new Color(p.GraphicR / 255f, p.GraphicG / 255f, p.GraphicB / 255f, p.GraphicA / 255f));
    _mapObjects[ItemKey(p.TileX, p.TileY)] = item;
}

private void OnEraseObject(object packetObj)
{
    var p = (EraseObjectPacket)packetObj;
    if (_mapObjects.Remove(ItemKey(p.TileX, p.TileY), out var item))
        item.QueueFree();
}
```
> **`Get(sheet, graphic)` arg order:** `SpriteCache.Get(sheet, graphic)`. For items the sheet is
> `GraphicFile` and the graphic is `GraphicId` — i.e. `_cache.Get(p.GraphicFile, p.GraphicId)`.
> **Verify against `Helpers.GetSprite(int id, int file)` (`Helpers.cs:10`)**: it builds key
> `"{file}-{id}"`, so file=sheet, id=graphic. Use `_cache.Get(p.GraphicFile, p.GraphicId)`. (Fix the
> snippet above to this order — written out here so the implementer doesn't transpose them.)

**Step 3: Build clean.**
Run: `dotnet build` → `Build succeeded`.

**Step 4: Commit.**
```bash
git add Scripts/Map/MapItem.cs Scripts/MapManager.cs
git commit -m "feat: render dropped map items with tint; handle EraseObject"
```

---

### Task 9: Runtime tile updates — `TileUpdatePacket`

**Files:**
- Modify: `Scripts/MapManager.cs` (`OnTileUpdate`)
- Modify: `Scripts/Map/MapLayer.cs` (expose a targeted redraw)

**Step 1: Apply the update to the in-memory map + redraw affected layers.** Port
`MapManager.OnTileUpdatePacket`: write `Flags`, and for each of 5 layers set graphic+sheet (sheet 0 ⇒
clear). No Y-flip — cell is `(packet.X, packet.Y)` directly.
```csharp
private void OnTileUpdate(object packetObj)
{
    var p = (TileUpdatePacket)packetObj;
    if (p.X < 0 || p.Y < 0 || p.X >= _map.Width || p.Y >= _map.Height) return;

    var tile = _map[p.X, p.Y];
    tile.Flags = p.Flags;

    for (int layer = 0; layer < 5; layer++)
    {
        int graphic = p.Tiles[layer * 2];
        int sheet   = p.Tiles[layer * 2 + 1];
        var l = tile.Layers[layer];
        if (l.Graphic == graphic && l.Sheet == sheet) continue;   // unchanged

        l.Graphic = sheet == 0 ? 0 : graphic;                      // sheet 0 ⇒ empty cell
        l.Sheet   = sheet;
        _layers[layer].QueueRedraw();                              // repaint that layer
    }
}
```
> `MapLayer` already redraws the whole layer in `_Draw`, so `QueueRedraw()` is enough — no new
> `MapLayer` API is strictly required. (If Task 6's perf note bites and you add per-cell culling
> later, a targeted `RedrawCell(x,y)` can replace the full repaint then.) **If you keep the full
> repaint, Step 2 below (MapLayer change) is a no-op — skip it.**

**Step 2 (optional):** none required if using the full-layer `QueueRedraw`. Keep `MapLayer` as-is.

**Step 3: Build clean.**
Run: `dotnet build` → `Build succeeded`.

**Step 4: Commit.**
```bash
git add Scripts/MapManager.cs
git commit -m "feat: apply runtime TileUpdate packets and repaint affected layers"
```

---

### Task 10: Live validation — log in and see the world

**Files:** none (validation), then doc updates in Task 11.

**Step 1: Import settings.** Open `~/code/Goose2ClientGodot` in Godot 4.6; let it import
`Assets/Sprites/sheets/*.png`. Set the import **Filter = Off** (nearest) for these textures (Import
dock → select the folder → preset → Reimport) so tiles are crisp, not blurry.

**Step 2: Run against the real server** (`game.illutia.net:2006`, from
`2026-06-06-gamemanager-and-scene-flow.md` Task 5). Log in with real credentials. Confirm:
- The **map renders** in `Map.tscn` (ground + overlay layers visible), not the old placeholder label.
- The **camera is centred on the spawn tile** (from `SetYourPositionPacket`) — you're looking at the
  player's location, not the map corner.
- **Dropped items** appear at the right tiles (drop something or walk where items exist); they vanish
  on `EraseObject`.
- A **tile update** (e.g. a door/lever, or any server-driven tile change) repaints in place.
- **Roof toggle:** standing under a roof hides layer 4 (if the spawn isn't under a roof, walk/teleport
  somewhere that is, or temporarily log `_map[x,y].IsRoof`).

**Step 3: Confirm the `MapFileName` → path mapping.** Watch the output for any
`LoadMap: cannot open …` error. If the server's `MapFileName` differs from the `.bytes` basename
(e.g. it sends `100` not `Map100`), adjust the single line in `GameManager.LoadMap` (Task 7) to match,
rebuild, and re-verify. Log the actual `mapFile` string once to be sure.

**Step 4: Sanity-check alignment.** If tiles/items look half a cell off or vertically mirrored, the
fix is in **`MapCoords` only** (the bottom-center formula or, if mirrored, a missed/extra flip —
there should be none). Adjust `MapCoords`, re-run its unit test, rebuild, re-verify. Do not patch
offsets in `MapLayer`/`MapItem`.

**Step 5: Full test suite green.**
```bash
cd /home/hayden/code/Goose2ClientGodot/tests/Goose2Client.Tests && dotnet test
cd /home/hayden/code/Goose2ClientGodot/tools/AssetConverter && dotnet test
```
Expected: all green (MapFile, MapCoords, SpriteManifest, FrameManifestBuilder, + prior suites).

**Step 6: Commit** any alignment/path fixes made during validation:
```bash
git add -A
git commit -m "fix: live-validate map rendering against server (Step 5 landed)"
```

---

### Task 11: Update the migration docs

**Files:**
- Modify: `MIGRATION_PLAN.md` (§4 architecture revision; tick Step 5 in the porting order)

**Step 1: Revise §4** (`MIGRATION_PLAN.md:80-99`). Replace the `TileMapLayer`/`TileSet`/`SetCell`
prescription with the approach actually shipped: **5 `MapLayer` `Node2D`s drawing `AtlasTexture`
regions off the original sheet PNGs via `SpriteCache` + a frame-rect manifest — no `TileMapLayer`,
because the art is arbitrary-rect + bottom-center-anchored and Godot atlas sources are grid-locked.**
Keep the roof-toggle, depth (`YSortEnabled`/`z_index`), camera, and the (now-resolved) coordinate
notes; update the coordinate note to record **no Y-flip (Godot is Y-down like the server)** and
`MapCoords` as the single helper.

**Step 2: Tick Step 5** in "Recommended porting order" (`MIGRATION_PLAN.md:305-307`) with a
✅ **Landed (2026-06-06)** note: map parse + 5-layer `AtlasTexture` rendering, dropped items with
tint, runtime tile updates, roof toggle, spawn-centred `Camera2D`; converter extended to emit the
frame-rect manifest. Note that **character rendering and movement remain Step 6**.

**Step 3:** Also resolve the dependency-table row if appropriate (`MIGRATION_PLAN.md:261` "Tilemap
modules → `TileMapLayer`") — change to the `AtlasTexture`/`_Draw` approach.

**Step 4: Commit.**
```bash
git add MIGRATION_PLAN.md
git commit -m "docs: record Step 5 map rendering (AtlasTexture over TileMapLayer) as landed"
```

---

## Definition of done

- Logging into the live server shows the **real map** in `Map.tscn` — 5 layers drawn via
  `AtlasTexture` regions, camera centred on the spawn tile — replacing the placeholder label.
- **Dropped items** render (with tint) and erase correctly; **runtime tile updates** repaint in place;
  the **roof layer** hides when the player stands under it.
- `MapFile`, `MapCoords`, `SpriteManifest` unit tests are green; the converter's `FrameManifestBuilder`
  test is green; both test suites pass.
- The converter runs end-to-end on this machine (`Paths.cs` fixed) and emits PNGs + `.bytes` +
  `manifest.json`; generated assets are gitignored, not committed.
- **No Y-flip** anywhere; **all** tile↔world math is in `MapCoords`.
- `MIGRATION_PLAN.md` §4 + porting order updated to reflect the shipped `AtlasTexture` approach and
  Step 5 ticked. `dotnet build` clean; editor opens with no errors.

## Explicitly out of scope (next plans)

- **Characters + animation, movement, `PlayerController`/`PlayerInputManager`** — `MapManager`'s
  character/vitals/attack/spell/emote/chat listeners, `MakeCharacter`/`MoveCharacter`/
  `SetYourCharacter`, the layered paper-doll `AnimatedSprite2D` system, and **move-driven roof
  toggling**. **Step 6** (template: `~/code/3dMMO-Server/client/.../Character.cs`).
- **Spell/battle-text/chat-bubble/health-bar overlays**, emote/spell tile animations
  (`SpellTilePacket`, `BattleTextPacket`). **Step 8.**
- **Map-item tooltips / click interaction** (Unity `MapItem` `IPointerEnter`, `MapClickHandler`),
  pickup. **Step 7/8.**
- **Lighting** (`PointLight2D`/`CanvasModulate`). **Step 8.**
- **Viewport culling / draw batching** for the dense ground layer — only if Task 10 shows map-enter
  latency is a real problem (YAGNI until measured).
- The deferred network follow-ups (remote-close "Disconnected" event; `CharacterSettings` null-guard)
  — due with their consuming layers (`MIGRATION_PLAN.md:328-345`).

---

## Execution Handoff

**Plan complete and saved to `docs/plans/2026-06-06-map-rendering.md`. Two execution options:**

**1. Subagent-Driven (this session)** — I dispatch a fresh subagent per task, review between tasks,
fast iteration. (REQUIRED SUB-SKILL: superpowers:subagent-driven-development.)

**2. Parallel Session (separate)** — open a new session with superpowers:executing-plans, batch
execution with checkpoints.

**Which approach?**
