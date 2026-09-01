# Portable Map Editor — Part 3 of 4: Engine-Neutral Rendering and Assets Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add `MapEditor.Rendering`, an engine- and UI-neutral .NET 8 library that strictly loads the converter's sprite manifest, lazily owns and caches sheet images through a backend interface, performs pan/zoom transforms and conservative viewport culling at the five approved zoom levels, and synchronously emits platform-independent draw operations for five ordered map layers, missing-art placeholders, and optional map overlays.

**Architecture:** `SpriteManifest` is an immutable, validated index of the existing `manifest.json` contract, including valid converter outputs with no sheets or no frames. `SpriteAssetCache` combines that metadata with an injected `ISpriteSheetLoader`, loads each sheet at most once, negatively caches expected asset failures, validates source rectangles against decoded dimensions, and exclusively owns successful images until disposal. Immutable `ViewportTransform` values keep a world-pixel top-left origin and convert to/from canvas pixels; `ViewportCulling` computes in constant time a clipped cell range plus a conservative sprite-anchor range that accounts for tall/wide bottom-anchored frames. `MapRenderer` keeps no map-derived state: on each call it preflights a fixed tile-read work budget, reads only the current Part 1 `MapDocument` ranges when within budget, resolves visible references through the cache, and emits typed value operations to an injected synchronous `IMapDrawSink`. Part 4 will supply the Avalonia image loader and draw sink and will own cache replacement/invalidation; no Avalonia or Godot type enters this project.

**Series:** Part 3 of 4.
- **Part 1:** shared model/codec/storage and Godot migration, specified by `docs/plans/2026-09-01-portable-map-editor-part1-core-codec.md`.
- **Part 2:** editing session, strokes, history, memory cap, and dirty state, specified by `docs/plans/2026-09-01-portable-map-editor-part2-editing.md`.
- **Part 3 (this plan):** validated manifests, sprite/sheet cache lifetime, viewport math/culling, draw composition, placeholders/overlays, and rendering tests.
- **Part 4:** Avalonia controls/application integration, dialogs/settings, image and draw adapters, manual smoke tests, and build/publish scripts.

**Tech Stack:** C# / .NET 8; the Part 1 `MapEditor.Core` project; `System.Text.Json`; xUnit 2.9.2. No Avalonia, Godot, ImageSharp, Skia, `System.Drawing`, native library, or new NuGet dependency.

**Planning baseline verified 2026-09-01:** This checkout is still before Parts 1–2 implementation: `src/MapEditor.Core` and `src/MapEditor.Rendering` do not exist, and the Part 1–2 plans are untracked. On this tree, `dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj -v minimal` passes 457 tests and `dotnet build Goose2ClientGodot.csproj -v minimal` succeeds with zero warnings/errors. The focused Illutia converter contract test passes. The combined-manifest converter test cannot run here because its configured Aspereta directory `/home/hayden/code/gooseclient/AsperetaClient/bin/Release/net8/data` is absent; this part does not change converter code and must not make that machine-local dataset a gate. `Assets/Sprites/manifest.json` and `Assets/Sprites/sheets/` are also absent in this worktree, so all rendering tests use temporary manifests and fake image objects rather than generated assets.

**Design sources:** `docs/plans/2026-09-01-portable-map-editor-design.md`, `docs/plans/2026-09-01-portable-map-editor-part1-core-codec.md`, and `docs/plans/2026-09-01-portable-map-editor-part2-editing.md`.

---

## Prerequisites and scope boundaries

Parts 1 and 2 must be implemented and green before this plan starts. Use their exact `MapEditor.Core.MapDocument`, `MapTile`, `MapTileLayer`, and `MapEditSession` APIs; do not rename or widen them. In particular, rendering reads only:

```csharp
public sealed class MapDocument
{
    public const int LayerCount = 5;
    public const int BlockedFlag = 2;
    public int Width { get; }
    public int Height { get; }
    public MapTile this[int x, int y] { get; }
}

public readonly struct MapTile
{
    public int Flags { get; }
    public bool IsBlocked { get; }
    public MapTileLayer GetLayer(int layerIndex);
}

public readonly record struct MapTileLayer(int Sheet, int Graphic);
```

In scope:

- `MapEditor.Rendering` and `MapEditor.Rendering.Tests` project setup.
- Immutable manifest loading from an explicitly supplied asset root.
- Structural and semantic validation of `tileSize`, sheet/graphic identifiers, and frame rectangles while permitting empty `sheets` and empty per-sheet frame objects emitted by the current converter.
- Deterministic sheet/frame enumeration for Part 4's sprite selectors.
- Neutral sheet-image/loader interfaces, lazy positive and negative caching, source-bounds checks, diagnostics, ownership, and disposal.
- Neutral double-precision points/sizes/rectangles/colors and integer source rectangles.
- Exactly 25%, 50%, 100%, 200%, and 400% zoom; pan, coordinate conversion, floor-based tile hit testing, and cursor-centered zoom.
- Half-open viewport/map intersection, clipped visible-cell ranges, conservative bottom-anchor candidate culling for oversized art, and a preflight work limit that prevents pathological valid frame sizes from causing million-tile render scans.
- Synchronous draw-sink operations for sprites, conspicuous missing-reference placeholders, blocked cells, grid lines, selected cells, and hovered cells.
- Five layer visibility bits and deterministic layer 0→4 composition.
- Tests of operations and contracts without pixel screenshots or a graphics backend.
- Full core/client/solution regression gates.

Out of scope:

- Any Avalonia project/reference/type, `Control`, `DrawingContext`, `Bitmap`, pointer event, invalidation callback, dispatcher, view model, window, dialog, menu, shortcut, or application lifecycle.
- Asset-directory selection, OS settings paths, persistence, user-facing dialogs, or active cache swapping. Part 4 owns those actions using the boundaries defined here.
- Godot renderer/cache migration or modification. Part 1 shares only `MapEditor.Core`; the existing Godot-specific `Scripts/Map/SpriteManifest.cs`, `SpriteCache.cs`, `MapCoords.cs`, and map renderers remain unchanged.
- Converter changes, regeneration of PNGs/manifests, generated assets, or tests requiring proprietary source data.
- Map editing, history, dirty state, save orchestration, paths/revisions, or document replacement.
- Zoom animation, arbitrary zoom factors, rotation, isometric projection, minimaps, sprite search/favorites, texture atlasing/repacking, mipmaps, GPU upload, asynchronous/background loading, file watching, cache eviction, or hot reload.
- Y-sorting layer 2 with characters. The approved standalone editor draws map content strictly by map layer 0 through 4; characters/items are not editor content.
- Pixel screenshots. Part 4 performs the local visual smoke pass.

`MapRenderer` is a read-only projection over the current document and cache. It does not subscribe to `MapEditSession`, own a document, retain a render batch, or invalidate a canvas. Part 4 synchronously renders after its own invalidation and therefore immediately sees eager stroke mutations, undo, redo, layer visibility, overlays, pan, and zoom without a rendering cache reset.

---

## APIs and facts verified in the current repository and prior plans

| API / fact | Citation and consequence |
|---|---|
| The three-project architecture assigns manifests, sprite caching, viewport calculations, culling, and platform-independent operations to `MapEditor.Rendering` | `docs/plans/2026-09-01-portable-map-editor-design.md:13-23`. Rendering references Core; App references both; Rendering references neither UI engine. |
| A 1000×1000 map must not create one graphics/UI object per tile, and only visible tiles draw | `docs/plans/2026-09-01-portable-map-editor-design.md:48-54`. The renderer streams transient operations for clipped ranges and retains no per-cell object/index. |
| Converter output is per-sheet PNGs, a `(sheet, graphic)` manifest, 32×32 cells, and bottom-center anchoring | `docs/plans/2026-09-01-portable-map-editor-design.md:56-64`. These become locked constants/path and destination formulas, not inferred backend behavior. |
| Missing/malformed manifests need actionable errors; missing art must not block map open/save | `docs/plans/2026-09-01-portable-map-editor-design.md:65-67,98-102`. Manifest opening throws typed asset exceptions, while per-reference resolution emits placeholders and never mutates numeric map data. |
| Layers draw 0→4, can be hidden independently, and overlays include boundaries/hover/selection/blocked | `docs/plans/2026-09-01-portable-map-editor-design.md:67-71`. Renderer operation order and visibility-mask validation are explicit below. |
| Zoom levels and nearest filtering are exactly 25/50/100/200/400 percent | `docs/plans/2026-09-01-portable-map-editor-design.md:69-69`. Arbitrary doubles are not public zoom input. |
| Required tests cover every zoom transform, viewport range, source/destination/layer order, manifests, and missing references using operations instead of screenshots | `docs/plans/2026-09-01-portable-map-editor-design.md:106-125`. Every item maps to focused tests below. |
| Part 1 locks five value layers and row-major coordinate reads without exposing backing storage | `docs/plans/2026-09-01-portable-map-editor-part1-core-codec.md:80-128`. Renderer loops clipped `(y,x)` coordinates and calls `GetLayer`; it adds no bulk-array API to Core. |
| Part 1 preserves unusual signed numeric references and does not normalize `Graphic == 0` to sheet 0 | `docs/plans/2026-09-01-portable-map-editor-part1-core-codec.md:118-125`. Rendering treats graphic 0 as the legacy empty sentinel but never rewrites the accompanying sheet; every nonzero unresolvable graphic gets a placeholder. |
| Part 2 applies edits eagerly and deliberately exposes no events | `docs/plans/2026-09-01-portable-map-editor-part2-editing.md:135-165`. Part 4 invalidates after calls; renderer reads fresh document state on each render. |
| Part 2 keeps paths/revisions and application active-document ownership outside the session | `docs/plans/2026-09-01-portable-map-editor-part2-editing.md:167-176`. Rendering likewise owns no active session/path/revision. |
| The converter emits exactly `{ "tileSize": 32, "sheets": { sheet: { graphic: [x,y,w,h] } } }` | `tools/AssetConverter/src/AssetConverter/Manifest/FrameManifestBuilder.cs:7-16,36-37`. Parser validates this shape and tolerates unknown root properties for forward compatibility. |
| Current converter output can legitimately contain no sheets or sheets with no frames | `BuildIllutiaSheets` starts empty, skips unreadable/non-graphic inputs, and creates each frame dictionary with `adf.Frames.Count` at `tools/AssetConverter/src/AssetConverter/Manifest/FrameManifestBuilder.cs:40-60`; `Build` serializes that result without requiring entries at lines 7-16. Empty objects are therefore valid converter shape, not malformed data. |
| Illutia keys are source file/frame integers and Aspereta uses renumbered sheets plus `700000 + frame` graphics | `tools/AssetConverter/src/AssetConverter/Manifest/FrameManifestBuilder.cs:23-33,40-60`. Parse full invariant-culture positive `Int32` identifiers; never assume a small range or contiguous IDs. |
| Converter PNG names are `<sheet>.png` | `tools/AssetConverter/src/AssetConverter/BatchConverter.cs:17-18,40-42`; `tools/AssetConverter/src/AssetConverter/Aspereta/AsperetaBatchConverter.cs:15-21`. The only sheet path contract is `<assetRoot>/sheets/<sheet>.png`. |
| Converter commands place sheets under `Assets/Sprites/sheets` and manifest beside them | `tools/AssetConverter/src/AssetConverter/Program.cs:90-99,127-135,183-187`. `SpriteManifest.Load` receives that `Assets/Sprites` directory, not repository root or `sheets` itself. |
| Existing converter tests pin sample rects `(0,0,48,64)` and `(48,192,48,64)` and eight frames | `tools/AssetConverter/tests/AssetConverter.Tests/FrameManifestBuilderTests.cs:10-30`. Rendering tests use this literal contract without running conversion. |
| Converter integration treats every `graphic == 0` as empty and resolves all other map pairs through manifest sheet/graphic properties | `tools/AssetConverter/tests/AssetConverter.Tests/AsperetaIntegrationTests.cs:28-46`; map conversion emits `(0,0)` for empty output at `tools/AssetConverter/src/AssetConverter/Aspereta/AsperetaMapConverter.cs:41-57`. Renderer follows the same empty/reference split. |
| Existing manifest parsing assumes properties/array fields and performs almost no validation | `Scripts/Map/SpriteManifest.cs:22-40`; its two tests cover only known/unknown lookups at `tests/Goose2Client.Tests/SpriteManifestTests.cs:4-27`. New strict behavior belongs in the new namespace/project and does not alter Godot. |
| Existing Godot cache lazily keys sheets and atlas textures and stores a failed sheet lookup | `Scripts/Map/SpriteCache.cs:9-47`. New cache preserves lazy one-attempt semantics while making image loading/backend ownership explicit and validating dimensions/bounds. |
| The current client uses 32-pixel Y-down cells, floor hit testing, and bottom-center anchors | `Scripts/Map/MapCoords.cs:8-24`. Rendering uses neutral doubles but the same formulas and no vertical flip. |
| Existing object rendering places a frame at `anchorX - width/2, anchorY - height` | `Scripts/Map/ObjectLayer.cs:36-55`; dropped items use the same formula at `Scripts/Map/MapItem.cs:12-19`. This is the exact sprite destination formula below. |
| Existing map bands read all coordinates/layers and source references directly | `Scripts/Map/MapLayer.cs:27-43`; object layer does the same at `Scripts/Map/ObjectLayer.cs:25-40`. Standalone composition changes full-map setup to viewport-only reads and preserves reference/source semantics. |
| Current Godot creates separate sprite caches in `GameManager` and `MapManager` | `Scripts/GameManager.cs:145-147`; `Scripts/MapManager.cs:40-45`. Part 3 does not copy that ambiguous ownership: one Part 4 asset context owns one rendering cache and all images it publishes. |
| Repository policy defaults to no comments/doc strings in new/modified code | `AGENTS.md:5-26`. Add none in production/tests unless a non-obvious ownership invariant truly requires a one-line explanation. |

The focused command below was verified green during planning and is the only converter-data gate used by this part:

```bash
dotnet test tools/AssetConverter/tests/AssetConverter.Tests/AssetConverter.Tests.csproj \
  --filter FullyQualifiedName~FrameManifestBuilderTests.Build_EmitsSheetGraphicRects_ForSheet1000 -v minimal
```

---

## Locked Part 3 contracts

Use namespace `MapEditor.Rendering` throughout `src/MapEditor.Rendering`. Rendering types do not move into Core. Use plain .NET types internally and the neutral public value types below; do not expose `System.Drawing` geometry/color.

### Geometry, zoom, and viewport surface

```csharp
public readonly record struct RenderPoint(double X, double Y);
public readonly record struct RenderSize(double Width, double Height);
public readonly record struct RenderRect(double X, double Y, double Width, double Height);
public readonly record struct RenderColor(byte R, byte G, byte B, byte A);
public readonly record struct MapTileCoordinate(int X, int Y);

public enum MapZoom
{
    Percent25 = 25,
    Percent50 = 50,
    Percent100 = 100,
    Percent200 = 200,
    Percent400 = 400
}

public static class MapZoomLevels
{
    public static double GetScale(MapZoom zoom);
    public static MapZoom ZoomIn(MapZoom zoom);
    public static MapZoom ZoomOut(MapZoom zoom);
}

public readonly record struct ViewportTransform
{
    public RenderSize ViewportSize { get; }
    public RenderPoint WorldOrigin { get; }
    public MapZoom Zoom { get; }
    public double Scale { get; }
    public RenderRect VisibleWorldRect { get; }

    public ViewportTransform(
        RenderSize viewportSize,
        RenderPoint worldOrigin,
        MapZoom zoom);

    public RenderPoint WorldToScreen(RenderPoint worldPoint);
    public RenderRect WorldToScreen(RenderRect worldRect);
    public RenderPoint ScreenToWorld(RenderPoint screenPoint);
    public MapTileCoordinate ScreenToTile(RenderPoint screenPoint);
    public ViewportTransform WithWorldOrigin(RenderPoint worldOrigin);
    public ViewportTransform PanByScreenDelta(RenderPoint screenDelta);
    public ViewportTransform ZoomAt(MapZoom zoom, RenderPoint screenAnchor);
}

public readonly record struct TileRange(int MinX, int MinY, int MaxX, int MaxY)
{
    public static TileRange Empty { get; }
    public bool IsEmpty { get; }
    public int Width { get; }
    public int Height { get; }
    public bool Contains(int x, int y);
}

public readonly record struct ViewportTileRanges(
    TileRange VisibleCells,
    TileRange SpriteCandidates);

public static class ViewportCulling
{
    public const int TileSize = 32;

    public static ViewportTileRanges Compute(
        ViewportTransform viewport,
        int mapWidth,
        int mapHeight,
        int maxSpriteWidth,
        int maxSpriteHeight);
}
```

Geometry contract:

- Reject non-finite points/origins/anchors/deltas and non-finite or non-positive viewport sizes with `ArgumentOutOfRangeException`. Reject undefined zoom enum values before any calculation.
- World coordinates are Y-down. World origin is the world point shown at screen `(0,0)`. `screen = (world - origin) * scale`; inverse division uses the same exact scale.
- `ScreenToTile` performs `Math.Floor(world / 32)` independently on both axes, including negative values. It does not clamp to a document.
- `PanByScreenDelta(d)` returns origin `origin - d/scale`: dragging content right/down reveals world farther left/up. It does not clamp map edges.
- `ZoomAt(next, anchor)` preserves the exact world point under `anchor` within double tolerance: compute it with the old transform, then solve the new origin. At min/max, `ZoomOut`/`ZoomIn` return the same enum.
- Rectangles use nonnegative finite width/height and half-open `[left,right) × [top,bottom)` intersection. Internal checked helpers centralize validation/intersection; do not sprinkle epsilon subtraction into culling.
- Visible cells are cells whose 32×32 world rectangles intersect the viewport, clamped to `0..width-1`/`0..height-1`. A viewport wholly outside map cells returns `TileRange.Empty` for `VisibleCells`.
- Compute `SpriteCandidates` from the same **unclipped** world-cell range, expand left/right by `ceil(max(0,maxWidth-32)/2/32)` cells, and expand downward only by `ceil(max(0,maxHeight-32)/32)` cells, then clamp to the map. Sprites grow upward from a bottom-center anchor, so no upward candidate padding is needed. Candidates can therefore be nonempty when the viewport is just outside the map but oversized edge art overlaps it; a viewport farther away than the computed padding returns both ranges empty. Renderer still exact-intersection-tests each resolved destination before emission.
- Width/height must be within Part 1's 1…1000 dimensions. Maximum sprite dimensions must be positive. Zoom does not affect tile-range membership because culling converts the canvas to world space first.

### Manifest and sprite-reference surface

```csharp
public readonly record struct SpriteReference(int Sheet, int Graphic)
{
    public bool IsEmpty { get; }
}

public readonly record struct SpriteSourceRect(int X, int Y, int Width, int Height);
public readonly record struct SpriteFrame(
    SpriteReference Reference,
    SpriteSourceRect SourceRect);

public enum SpriteManifestError
{
    ManifestNotFound,
    ReadFailed,
    MalformedJson,
    InvalidRoot,
    MissingTileSize,
    UnsupportedTileSize,
    MissingSheets,
    InvalidSheetId,
    DuplicateSheetId,
    InvalidSheetFrames,
    InvalidGraphicId,
    DuplicateGraphicId,
    InvalidFrameRect
}

public sealed class SpriteManifestException : IOException
{
    public SpriteManifestError Error { get; }
    public string Path { get; }
}

public sealed class SpriteManifest
{
    public const int RequiredTileSize = 32;

    public int TileSize { get; }
    public int MaxFrameWidth { get; }
    public int MaxFrameHeight { get; }
    public IReadOnlyList<int> SheetIds { get; }
    public IReadOnlyList<SpriteFrame> Frames { get; }

    public bool ContainsSheet(int sheet);
    public bool TryGetSourceRect(
        SpriteReference reference,
        out SpriteSourceRect sourceRect);
    public IReadOnlyList<SpriteFrame> GetFrames(int sheet);

    public static SpriteManifest Parse(
        string json,
        string sourcePath = "<memory>");
    public static SpriteManifest Load(string assetDirectory);
}
```

Manifest contract:

- `Load` normalizes `assetDirectory` with `Path.GetFullPath` and reads `<assetDirectory>/manifest.json`. Null/blank paths fail as argument errors. A missing file, another read failure, and malformed/invalid data remain distinct typed errors carrying the full manifest path and a message naming the failing property/sheet/graphic.
- Root must be an object with exactly one integer `tileSize == 32` property and exactly one object-valued `sheets` property. The `sheets` object may be empty because the current converter can emit that shape. Duplicate required root properties are `InvalidRoot`. Other unknown root properties are tolerated for forward compatibility.
- Sheet and graphic property names parse with invariant culture, no sign/whitespace/exponent, into positive `Int32` values. Zero is reserved by map rendering; negative and overflow identifiers are invalid manifest data.
- Detect duplicate JSON properties and numeric aliases such as `"1"`/`"01"` as duplicate IDs. Do not silently use first/last value.
- Every sheet value is an object and may contain zero frames because the current converter can emit an empty frame dictionary. Every present frame is exactly four integer JSON numbers `[x,y,width,height]`, with nonnegative x/y, positive width/height, and checked `x+width`/`y+height` within `Int32`. Overlap between frames is allowed because the converter contract does not prohibit it. Do not invent a frame dimension or area cap: this repository has no complete combined generated manifest that proves a compatibility-safe limit.
- Parse the complete document into locals, then publish an immutable manifest. Sort sheet IDs ascending and global/per-sheet frames by numeric sheet then graphic for deterministic selectors/tests. Returned collections must be read-only wrappers, not mutable dictionary/list/array instances.
- `MaxFrameWidth/Height` are each `max(RequiredTileSize, maximum validated frame dimension)`, yielding 32 for a manifest with no frames and conservative culling inputs for sub-tile frames. `SpriteReference.IsEmpty` is exactly `Graphic == 0`, regardless of sheet, matching converter/client semantics. A nonzero graphic with sheet 0 is not empty and will resolve as an unknown sheet placeholder.
- Structural parse does not open PNG files. Source rectangles are checked against actual decoded dimensions lazily by `SpriteAssetCache`, because loading every sheet during manifest validation would defeat the cache contract.

### Image adapter, cache, and resolution surface

```csharp
public interface ISpriteSheetImage : IDisposable
{
    int PixelWidth { get; }
    int PixelHeight { get; }
}

public enum SpriteSheetLoadStatus
{
    Success,
    NotFound,
    InvalidData,
    Unreadable
}

public readonly record struct SpriteSheetLoadResult(
    SpriteSheetLoadStatus Status,
    ISpriteSheetImage? Image,
    string? Diagnostic)
{
    public static SpriteSheetLoadResult Success(ISpriteSheetImage image);
    public static SpriteSheetLoadResult Failure(
        SpriteSheetLoadStatus status,
        string diagnostic);
}

public interface ISpriteSheetLoader
{
    SpriteSheetLoadResult Load(string path);
}

public enum SpriteResolutionStatus
{
    Ready,
    Empty,
    UnknownSheet,
    UnknownGraphic,
    MissingSheetFile,
    SheetLoadFailed,
    FrameOutsideSheet
}

public readonly record struct SpriteResolution(
    SpriteResolutionStatus Status,
    SpriteReference Reference,
    ISpriteSheetImage? Image,
    SpriteSourceRect SourceRect,
    string? Diagnostic);

public sealed class SpriteAssetCache : IDisposable
{
    public string AssetDirectory { get; }
    public SpriteManifest Manifest { get; }

    public SpriteAssetCache(
        string assetDirectory,
        SpriteManifest manifest,
        ISpriteSheetLoader loader);

    public static SpriteAssetCache Open(
        string assetDirectory,
        ISpriteSheetLoader loader);

    public SpriteResolution Resolve(SpriteReference reference);
    public void Dispose();
}
```

Cache and loader contract:

- `Open` validates/parses the manifest before constructing a cache and before invoking the image loader. The constructor supports tests and dependency composition but still normalizes the asset root. Neither path owns or disposes the immutable manifest or loader.
- Resolve graphic 0 as `Empty` before consulting sheet metadata. For nonzero graphics, distinguish absent manifest sheet (`UnknownSheet`) from absent graphic in a known sheet (`UnknownGraphic`) without invoking the loader.
- Sheet paths are exactly `<full asset root>/sheets/<sheet>.png`; positive validated manifest IDs prevent path traversal. Call the loader lazily on the first reference to a known sheet.
- `ISpriteSheetLoader.Load` must return `NotFound`, `InvalidData`, or `Unreadable` for expected filesystem/decode failures rather than throw them. A success has one newly transferred, non-null image and no failure diagnostic; a failure has a nonempty diagnostic and null image. Malformed adapter results throw `InvalidOperationException`, disposing any improperly supplied image.
- A successful image must report positive dimensions. Otherwise dispose it immediately and cache `SheetLoadFailed`. A frame whose checked right/bottom exceeds the decoded image resolves as `FrameOutsideSheet`; other frames on that image may still resolve.
- Cache one sheet result per manifest sheet and one final resolution per nonempty reference. One successful image backs all frames from that sheet. Cache failures and out-of-bounds resolutions too: no filesystem/decode retry occurs during that cache lifetime.
- `Resolve` never rewrites or validates the map model. Expected missing/invalid assets return a non-ready status with a path/reference diagnostic. Unexpected adapter/programmer exceptions propagate.
- The cache is single-threaded. It has no locks, async work, eviction, file timestamps, watchers, or reload. To retry changed files or switch roots, Part 4 creates a new cache.

### Cache ownership and publication boundary

- `SpriteAssetCache` exclusively owns every successful `ISpriteSheetImage` transferred by its loader and disposes each loaded sheet image exactly once. It never disposes the loader. `MapRenderer` borrows the cache and never disposes it.
- A `SpriteResolution` and `SpriteDrawOperation` publish borrowed image references, valid only while their originating cache remains undisposed. Consumers must not dispose or retain them beyond that asset-context lifetime.
- `MapRenderer.Render` invokes the sink synchronously and retains no operation/image after the call. Part 4 must not swap/dispose a cache during a render callback. Its required UI-thread sequence is: construct/validate a complete new cache locally; publish a new cache+renderer pair; invalidate the canvas; dispose the old cache only when no old render callback is active.
- If new manifest opening fails, Part 4 does not publish it and the old context remains usable. Lazy sheet failures after publication become placeholders; selecting a corrected directory or explicit reload constructs a fresh cache to retry.
- Cached metadata/images are asset-root state only. They are never keyed by document, tile, layer visibility, viewport, selection, or dirty/history state, so map mutation needs no cache invalidation.
- Disposal is idempotent. Any `Resolve` or `Render` attempt using a disposed cache throws `ObjectDisposedException`; already published borrowed images must not be used afterward.

### Draw operation and renderer surface consumed by Part 4

```csharp
public enum SpriteSampling
{
    NearestNeighbor
}

public enum CellOverlayKind
{
    Blocked,
    Selected,
    Hovered
}

public readonly record struct SpriteDrawOperation(
    int Layer,
    MapTileCoordinate Tile,
    SpriteReference Reference,
    ISpriteSheetImage Image,
    SpriteSourceRect SourceRect,
    RenderRect DestinationRect,
    SpriteSampling Sampling);

public readonly record struct PlaceholderDrawOperation(
    int Layer,
    MapTileCoordinate Tile,
    SpriteReference Reference,
    SpriteResolutionStatus Reason,
    RenderRect DestinationRect,
    RenderColor FillColor,
    RenderColor StrokeColor,
    string Diagnostic);

public readonly record struct CellOverlayDrawOperation(
    CellOverlayKind Kind,
    MapTileCoordinate Tile,
    RenderRect DestinationRect,
    RenderColor FillColor,
    RenderColor StrokeColor);

public readonly record struct GridLineDrawOperation(
    RenderPoint Start,
    RenderPoint End,
    RenderColor Color);

public interface IMapDrawSink
{
    void DrawSprite(in SpriteDrawOperation operation);
    void DrawPlaceholder(in PlaceholderDrawOperation operation);
    void DrawCellOverlay(in CellOverlayDrawOperation operation);
    void DrawGridLine(in GridLineDrawOperation operation);
}

public readonly record struct MapLayerVisibility
{
    public static MapLayerVisibility All { get; }
    public static MapLayerVisibility None { get; }
    public byte Mask { get; }

    public MapLayerVisibility(byte mask);
    public bool IsVisible(int layerIndex);
    public MapLayerVisibility WithVisibility(int layerIndex, bool visible);
}

public sealed record MapRenderOptions(
    MapLayerVisibility VisibleLayers,
    bool ShowGrid,
    bool ShowBlocked,
    MapTileCoordinate? HoveredTile,
    MapTileCoordinate? SelectedTile)
{
    public static MapRenderOptions Default { get; }
}

public readonly record struct MapRenderRequest(
    MapDocument Document,
    ViewportTransform Viewport,
    MapRenderOptions Options);

public sealed class MapRenderWorkLimitException : Exception
{
    public long RequestedTileReads { get; }
    public int MaximumTileReads { get; }
}

public sealed class MapRenderer
{
    public const int MaximumTileReadsPerRender = 800_000;

    public MapRenderer(SpriteAssetCache assets);
    public void Render(MapRenderRequest request, IMapDrawSink sink);
}
```

Render/composition contract:

- Validate null document/options/sink, disposed cache, layer mask outside low five bits, and option coordinates only when used. Hover/selection outside the map are accepted as “no visible operation,” not argument failures; pointer movement can temporarily be outside.
- Compute ranges from current document dimensions and manifest maximum frame dimensions on every render. `ViewportCulling.Compute` remains O(1) even when valid frame dimensions expand `SpriteCandidates` across the whole map. Retain no tile/sprite/operation list between calls.
- Before any document indexer/layer read, cache resolution, or sink call, compute in 64-bit arithmetic the exact upper bound on tile reads for this request: `SpriteCandidates.Width * SpriteCandidates.Height * visibleLayerCount`, plus `VisibleCells.Width * VisibleCells.Height` when blocked overlays are enabled. If it exceeds `MaximumTileReadsPerRender`, throw `MapRenderWorkLimitException` with both counts and an actionable message to zoom in, hide layers/blocked overlay, or inspect anomalously large frame metadata. This is a render-work guard, not a manifest frame dimension/area cap; valid giant frames remain loadable and render when the requested ranges fit the budget. Grid/target geometry requires no tile reads.
- For each visible layer in numeric order 0→4, traverse `SpriteCandidates` row-major `(y,x)`. Read the current `MapTileLayer` by value. Graphic 0 emits nothing. Resolve every other reference. The successful preflight mathematically bounds all candidate/reference work by `MaximumTileReadsPerRender`; do not perform a second wider scan or silently omit over-budget candidates.
- For `Ready`, destination world rect is exactly `left = x*32 + 16 - frameWidth/2`, `top = (y+1)*32 - frameHeight`, `width = frameWidth`, `height = frameHeight`. Exact-intersection-test it against `VisibleWorldRect`; if visible, transform its rect to screen coordinates and emit `SpriteSampling.NearestNeighbor` with the manifest source rect unchanged.
- For any nonempty non-ready resolution, use the tile's 32×32 world cell as the placeholder destination. Emit only if that cell intersects the viewport. The placeholder is one magenta-fill/yellow-stroke operation carrying layer, tile, exact numeric reference, reason, and diagnostic. Part 4's sink draws the fill plus a diagonal X; it may additionally display `sheet:graphic`, but text layout is App scope.
- Empty references never create placeholders, including `(nonzero sheet, graphic 0)`. `(sheet 0, nonzero graphic)`, negative values, unknown IDs, missing PNGs, decode failures, and out-of-sheet frames do create placeholders while the map remains untouched/saveable.
- Sprite/placeholder operations are emitted before all overlays. Within each layer, later row-major cells are emitted later. There is no inter-layer Y sort: every operation from layer N precedes every operation from layer N+1.
- If enabled, blocked overlays follow all five layers, one translucent-red cell operation for each `tile.IsBlocked` in `VisibleCells`, row-major. Unknown flag bits do not affect it.
- If enabled, grid follows blocked overlays. Emit one clipped line for each visible map-cell boundary: `width+1` vertical and `height+1` horizontal lines over the visible map intersection, not four lines per cell. Do not emit duplicate shared edges or grid outside map bounds.
- Selected overlay follows grid; hovered overlay is last and therefore remains visible when both target the same cell. Each emits only when its cell intersects the viewport and map.
- Palette constants are internal to `MapRenderer`: placeholder fill `#FF00FFCC`, placeholder stroke `#FFFF00FF`, blocked fill `#FF000060`, grid `#FFFFFF30`, selected stroke `#00FFFFFF`, hovered stroke `#FFFF00FF`; unused fill/stroke channels are transparent. Tests pin these byte values so all backends receive the same semantics.
- The sink must consume calls synchronously. A sink exception propagates immediately; renderer has no partially published batch or state to roll back. A work-limit failure also propagates before reads or calls rather than publishing an incomplete rendering. Rendering never changes document, edit session, options, viewport, manifest, cache entries beyond lazy asset resolution, or dirty/history state.

Part 4 implements only these renderer-facing adapters:

1. `ISpriteSheetImage` wrapping the backend-decoded sheet object and reporting pixel dimensions.
2. `ISpriteSheetLoader` converting expected Avalonia/filesystem decode outcomes into `SpriteSheetLoadResult`.
3. `IMapDrawSink` executing operations against the current canvas drawing context with clip-to-control bounds and nearest-neighbor image interpolation.

Those interfaces deliberately contain no controls, dialogs, settings, dispatcher, application service, or build concern.

---

## Mutation and cache impact analysis

| State/change | Writer / owner | Rendering/cache behavior | Required non-impact proof |
|---|---|---|---|
| Manifest publication | `SpriteManifest.Parse/Load` | Build complete local dictionaries/sorted read-only views, publish only on success | Malformed input returns no partial manifest/cache; old Part 4 context can remain active |
| One sheet load | `SpriteAssetCache.Resolve` | First known reference loads once; cache owns successful image; all frames share it | Other sheets remain unloaded; map/options unchanged; repeated refs return same image without loader call |
| Expected sheet failure | Loader result consumed by cache | Negative result cached and rendered as placeholders | Repeated renders do not retry/thrash; other sheets still resolve; no exception or map rewrite |
| Out-of-bounds source frame | Resolution validation | Cache only that reference as `FrameOutsideSheet` | Valid frames on same image still render; image remains owned once |
| Asset-root replacement | Part 4 only | Construct new context before publish; old remains until no render uses it | Failed new manifest cannot invalidate old context; old images disposed once after publication boundary |
| Sprite/image disposal | Cache only | Dispose each loaded successful sheet once; make cache unusable | Loader/manifest/document/session are not disposed; duplicate refs do not double-dispose |
| Map layer mutation | Part 2 session via Part 1 `SetLayer` | Next render reads new value; asset resolution cache is reference-only | No stale per-tile cache, no renderer write, history/dirty unaffected |
| Flags mutation/undo/redo | Part 2 session | Next blocked pass reads `IsBlocked` from current tile | Sprite cache and five map layers unaffected; unknown bits ignored, not rewritten |
| Pan | Part 4 replaces `ViewportTransform` | Changes range and destination coordinates only | Document/assets/options/history unchanged; no load for culled unknown refs |
| Zoom at cursor | Immutable transform method | Preserves anchor world point, then reculls | No arbitrary scale; map coordinates and sprite source rects unchanged |
| Layer visibility | Part 4 replaces options mask | Hidden layers are not traversed and trigger no asset loads | Other layer ordering/content unchanged; map data remains editable/saveable |
| Missing reference | Map data plus manifest/cache lookup | Visible cell gets diagnostic placeholder | Exact numeric pair survives; no asset error changes save/open/dirty state |
| Overlay toggles/targets | Part 4 replaces options | Add/remove only post-sprite operations | No document command, asset load, dirty change, or hidden-layer effect |
| Large/empty map or giant valid frame | Core owns compact tiles; renderer owns no map index | O(1) culling plus 64-bit preflight rejects requests above 800,000 tile reads before touching the document/cache/sink | Distant nonempty reference loader counts prove bounded reads; no UI/image/operation object retained per million map tiles |
| Sink failure | Part 4 backend | Exception propagates from current synchronous call | Renderer/cache/document retain valid state; caller may invalidate and retry |

No document mutation invalidates the asset cache. No cache mutation changes document/history. The only stateful rendering mutation is lazy sheet/reference resolution, which is deterministic for one cache lifetime and scoped to the selected asset root.

---

## Task 1: Scaffold Rendering and lock viewport transforms/culling

**Files:**
- Create: `src/MapEditor.Rendering/MapEditor.Rendering.csproj`
- Create: `src/MapEditor.Rendering/Geometry/RenderGeometry.cs`
- Create: `src/MapEditor.Rendering/Viewport/MapZoom.cs`
- Create: `src/MapEditor.Rendering/Viewport/ViewportTransform.cs`
- Create: `src/MapEditor.Rendering/Viewport/ViewportCulling.cs`
- Create: `tests/MapEditor.Rendering.Tests/MapEditor.Rendering.Tests.csproj`
- Create: `tests/MapEditor.Rendering.Tests/ViewportTransformTests.cs`
- Create: `tests/MapEditor.Rendering.Tests/ViewportCullingTests.cs`
- Modify: `Goose2ClientGodot.sln`

**Step 1: Confirm Parts 1–2 are complete and green.**

```bash
test -f src/MapEditor.Core/MapEditor.Core.csproj
test -f src/MapEditor.Core/Editing/MapEditSession.cs
dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj -v minimal
```

Expected: all Part 1–2 core tests pass. Stop and execute/fix those plans if prerequisites are absent or red.

**Step 2: Add project scaffolding.**

`MapEditor.Rendering.csproj` targets `net8.0`, enables nullable, and has one project reference to `../MapEditor.Core/MapEditor.Core.csproj`. The test project targets `net8.0`, is not packable, uses the same test package versions as Part 1 tests, and references Core and Rendering. Add both projects to the solution:

```bash
dotnet sln Goose2ClientGodot.sln add src/MapEditor.Rendering/MapEditor.Rendering.csproj
dotnet sln Goose2ClientGodot.sln add tests/MapEditor.Rendering.Tests/MapEditor.Rendering.Tests.csproj
```

Part 1 must already have excluded top-level `src/**` from the Godot SDK's recursive compile glob. Do not add Avalonia packages or an App placeholder.

**Step 3: Write failing zoom/transform tests.**

Add:

- `GetScale_ReturnsExactlyPoint25Point5OneTwoFour`.
- `ZoomInAndOut_UseOnlyOrderedLevelsAndClampAtEnds`.
- `WorldAndScreen_RoundTripAtEveryZoom`: table-drive noninteger positive/negative origins and points.
- `ScreenToTile_FloorsNegativeWorldCoordinates`.
- `PanByScreenDelta_UsesInverseScaleAndExpectedDirection` at all zooms.
- `ZoomAt_PreservesWorldPointUnderCursorAtEveryAdjacentPair` with center and off-center anchors.
- `WorldRectToScreen_ScalesOriginAndBothDimensions`.
- `ConstructorAndMethods_RejectNonFiniteOrNonPositiveGeometryAndUndefinedZoom`.
- `TransformMethods_DoNotMutateOriginalValue`.

Use precision assertions appropriate for double operations; do not round expected values to integers.

**Step 4: Write failing culling tests.**

Add:

- `Compute_Exact32PixelViewportContainsOneCellAtEveryZoom`.
- `Compute_PartialEdgesIncludesIntersectingCellsUsingHalfOpenBounds`.
- `Compute_ExactBoundaryDoesNotIncludeNextCell`.
- `Compute_ClampsNegativeOriginAndFarMapEdges`.
- `Compute_ViewportOutsideMapHasNoCellsAndOnlyOverlappingEdgeCandidates`: test near/far positions on all four sides.
- `Compute_OneByOneAnd1000By1000MapsStayWithinBounds`.
- `Compute_WideFrameAddsSymmetricCandidateColumns` for widths 32, 33, 96.
- `Compute_TallFrameAddsOnlyCandidateRowsBelow` for heights 32, 33, 96.
- `Compute_ZoomChangesWorldExtentButNotRules` across all levels.
- `Compute_Int32MaxSpriteExtentsClampsWithConstantTimeRangeMath`: pin the full-map range without enumerating it.
- `Compute_RejectsInvalidDimensionsOrSpriteExtents`.
- `TileRange_EmptyHasZeroSizeAndContainsNothing`.

Pin explicit ranges for representative origins instead of deriving expected values with production helpers.

**Step 5 (red):**

```bash
dotnet test tests/MapEditor.Rendering.Tests/MapEditor.Rendering.Tests.csproj \
  --filter "FullyQualifiedName~ViewportTransformTests|FullyQualifiedName~ViewportCullingTests" -v minimal
```

Expected: compile failures because the Rendering geometry/viewport types do not exist.

**Step 6: Implement neutral geometry, fixed zoom, and culling.**

Use double arithmetic, `Math.Floor`, `Math.Ceiling`, and checked integer range/padding calculations. Keep half-open rectangle helpers internal. Do not use float, backend geometry, epsilon hacks, map scanning, or allocate coordinate collections. Add no comments/doc strings.

**Step 7 (green):**

```bash
dotnet test tests/MapEditor.Rendering.Tests/MapEditor.Rendering.Tests.csproj \
  --filter "FullyQualifiedName~ViewportTransformTests|FullyQualifiedName~ViewportCullingTests" -v minimal
dotnet build src/MapEditor.Rendering/MapEditor.Rendering.csproj -v minimal
```

Expected: focused tests pass and build has zero warnings/errors.

**Step 8: Audit and commit.**

```bash
git grep -nE 'Godot|Avalonia|System.Drawing|Skia|ImageSharp' -- src/MapEditor.Rendering tests/MapEditor.Rendering.Tests || true
git diff --check
git add src/MapEditor.Rendering tests/MapEditor.Rendering.Tests Goose2ClientGodot.sln
git commit -m "feat(map-editor): add viewport rendering math"
```

**Task 1 invariant matrix:**

| Invariant | Test |
|---|---|
| Zoom is restricted to the approved five scales | scale/step/undefined-enum tests |
| Pan and forward/inverse transforms agree at every zoom | round-trip and pan tables |
| Cursor zoom preserves the pointed world coordinate | adjacent-pair anchor test |
| Negative hit testing floors rather than truncates | negative `ScreenToTile` test |
| Cell ranges use clipped half-open intersection | exact/partial/outside tests |
| Tall/wide art gets conservative anchor candidates | candidate-padding tests |
| Giant valid extents are range math, not a map scan | `Int32.MaxValue` extent test and source audit |
| Culling allocates no map-sized structure | API/source audit and 1000×1000 range test |

---

## Task 2: Add immutable, actionable manifest loading and validation

**Files:**
- Create: `src/MapEditor.Rendering/Assets/SpriteReference.cs`
- Create: `src/MapEditor.Rendering/Assets/SpriteManifestException.cs`
- Create: `src/MapEditor.Rendering/Assets/SpriteManifest.cs`
- Create: `tests/MapEditor.Rendering.Tests/SpriteManifestTests.cs`

Do not modify or share the Godot `Goose2Client.Map.SpriteManifest`; this implementation has a stricter editor-specific contract in a separate assembly/namespace.

**Step 1: Write failing valid-contract tests.**

Add:

- `Parse_ConverterContractIndexesKnownRectsAndTileSize`: use the literal current converter-test sample, not converter implementation output.
- `Parse_SortsSheetsAndFramesNumericallyForSelectors` with deliberately shuffled JSON keys.
- `Parse_ComputesMaximumFrameDimensionsWithTileSizeFloor`.
- `Parse_EmptySheetsIsValidAndUsesTileSizeMaxima`.
- `Parse_EmptyFrameObjectIsValidKnownSheetWithNoFrames`.
- `Parse_AllowsLargeCheckedFrameDimensionsWithoutAnInventedDimensionOrAreaCap`.
- `Parse_AllowsUnknownRootProperties`.
- `TryGetSourceRect_DistinguishesKnownAndUnknownReferences`.
- `GetFrames_UnknownSheetReturnsEmptyReadOnlyList`.
- `PublishedCollectionsCannotBeMutatedOrCastToMutableBackingCollections`.
- `Load_ReadsManifestFromNormalizedAssetRoot`: use a unique temp directory and mixed relative segments.
- `SpriteReference_GraphicZeroIsEmptyRegardlessOfSheet` and nonzero graphic/sheet-zero is not empty.

**Step 2: Write failing validation/error matrix tests.**

Table-drive literal JSON and assert typed `Error`, `Path`, and that the message includes the relevant property/ID:

- missing file and a directory created at the expected `manifest.json` file path (`ManifestNotFound`, `ReadFailed`);
- `SpriteManifestException` is assignable to `IOException`, preserves the inner exception where present, and exposes the typed error/path;
- invalid JSON/trailing JSON (`MalformedJson`);
- null/array root and duplicate required root properties (`InvalidRoot`);
- missing, string, fractional, or 31/64 `tileSize` (`MissingTileSize`/`UnsupportedTileSize`);
- missing/null/array `sheets` (`MissingSheets`), while `{}` is covered by the valid-contract tests;
- sheet IDs `0`, `-1`, whitespace, exponent, overflow, and duplicate/alias (`InvalidSheetId`/`DuplicateSheetId`);
- null/array sheet frame value (`InvalidSheetFrames`), while an empty object is covered by the valid-contract tests;
- graphic IDs with the same invalid/duplicate cases (`InvalidGraphicId`/`DuplicateGraphicId`);
- frame null/object/string, lengths 0/3/5, fractional/string members, negative x/y, zero/negative width/height, and right/bottom overflow (`InvalidFrameRect`);
- `ParseFailure_DoesNotPublishPartialManifest` expressed by return-or-typed-throw API.

`SpriteManifest.Load` filesystem tests must clean their temp directory in `finally` and not depend on repository assets.

**Step 3 (red):**

```bash
dotnet test tests/MapEditor.Rendering.Tests/MapEditor.Rendering.Tests.csproj \
  --filter FullyQualifiedName~SpriteManifestTests -v minimal
```

Expected: compile failures for manifest/reference types.

**Step 4: Implement the locked parser.**

Use `JsonDocument` and explicit `ValueKind`/number checks. Enumerate properties, parse names with invariant `Int32.TryParse` and strict lexical checks, and use `Dictionary.Add` on numeric IDs so raw duplicates and aliases are rejected. Accept zero sheet properties and zero frame properties, but apply every identifier/rectangle rule to each property that is present. Validate complete locals before constructing sorted read-only views. Derive each maximum with a 32-pixel floor. Wrap JSON and read exceptions in the `IOException`-derived `SpriteManifestException` with inner exception; do not catch fatal runtime failures, normalize invalid values, or add unsupported dimension/area limits.

**Step 5 (green):**

```bash
dotnet test tests/MapEditor.Rendering.Tests/MapEditor.Rendering.Tests.csproj \
  --filter FullyQualifiedName~SpriteManifestTests -v minimal
dotnet test tests/MapEditor.Rendering.Tests/MapEditor.Rendering.Tests.csproj -v minimal
```

Expected: all manifest and viewport tests pass.

**Step 6: Verify the unchanged converter contract and commit.**

```bash
dotnet test tools/AssetConverter/tests/AssetConverter.Tests/AssetConverter.Tests.csproj \
  --filter FullyQualifiedName~FrameManifestBuilderTests.Build_EmitsSheetGraphicRects_ForSheet1000 -v minimal
git diff --check
git add src/MapEditor.Rendering/Assets tests/MapEditor.Rendering.Tests/SpriteManifestTests.cs
git commit -m "feat(map-editor): validate sprite manifests"
```

Do not run or “fix” the combined test by introducing machine-specific paths/assets.

**Task 2 invariant matrix:**

| Invariant | Test |
|---|---|
| Existing converter shape, empty outputs, and sample rects load exactly | converter-contract/empty literal tests plus focused converter test |
| Tile size is exactly 32 | missing/type/value matrix |
| IDs are positive full `Int32`, unique numerically | invalid/alias/overflow tables |
| Rect fields and checked extents are valid integers without an unevidenced size/area cap | frame matrix and giant-valid-frame test |
| Enumeration, including empty collections, is stable and immutable | sorting/read-only/empty tests |
| Errors identify path and failing manifest element | error assertions |
| Failed parse publishes no partial metadata | return-or-throw API and tests |
| Parsing does not touch PNG files | loader-free API/source audit |

---

## Task 3: Add lazy sheet/reference caching with explicit ownership

**Files:**
- Create: `src/MapEditor.Rendering/Assets/ISpriteSheetImage.cs`
- Create: `src/MapEditor.Rendering/Assets/ISpriteSheetLoader.cs`
- Create: `src/MapEditor.Rendering/Assets/SpriteResolution.cs`
- Create: `src/MapEditor.Rendering/Assets/SpriteAssetCache.cs`
- Create: `tests/MapEditor.Rendering.Tests/SpriteAssetCacheTests.cs`
- Create: `tests/MapEditor.Rendering.Tests/Fakes/FakeSpriteSheetImage.cs`
- Create: `tests/MapEditor.Rendering.Tests/Fakes/FakeSpriteSheetLoader.cs`

**Step 1: Write failing resolution/laziness tests.**

Use fake disposable images and a recording loader. Add:

- `Open_ParsesManifestBeforeAnySheetLoad`.
- `Resolve_EmptyGraphicDoesNotInspectManifestOrLoad`.
- `Resolve_UnknownSheetAndGraphicDoNotLoad`.
- `Resolve_UnknownGraphicInKnownEmptySheetDoesNotLoad`.
- `Resolve_FirstKnownFrameLoadsExactNormalizedSheetPath`.
- `Resolve_TwoFramesOnOneSheetLoadOnceAndShareImage`.
- `Resolve_FramesOnDifferentSheetsLoadIndependently`.
- `Resolve_ReturnsManifestSourceRectUnchanged`.
- `Resolve_MissingSheetFileIsNegativelyCached`.
- `Resolve_InvalidAndUnreadableSheetAreNegativelyCachedAsLoadFailure`.
- `Resolve_FrameOutsideSheetIsCachedButOtherFrameCanResolve`.
- `Resolve_ExactRightAndBottomBoundsAreAccepted`.
- `Resolve_InvalidImageDimensionsDisposesImageAndCachesFailure`.
- `Resolve_MalformedLoaderResultThrowsAndDisposesImproperImage`.
- `Resolve_DiagnosticsContainPathAndReference`.

Assert loader call counts after each operation, not only final status.

**Step 2: Write failing lifetime/publication tests.**

Add:

- `Dispose_DisposesEachSuccessfulSheetExactlyOnce` with many frame resolutions.
- `Dispose_DoesNotDisposeLoaderOrFailedResults`.
- `Dispose_IsIdempotent`.
- `Resolve_AfterDisposeThrowsObjectDisposedExceptionWithoutLoaderCall`.
- `FailedOpenDoesNotCreateCacheOrInvokeLoader`.
- `SeparateCachesRetryPriorFailureAndOwnSeparateImages`.
- `DisposingOldCacheDoesNotAffectImageOwnedByNewCache`.

Fakes must expose only counters/results needed by tests; do not reference Avalonia or read real PNGs.

**Step 3 (red):**

```bash
dotnet test tests/MapEditor.Rendering.Tests/MapEditor.Rendering.Tests.csproj \
  --filter FullyQualifiedName~SpriteAssetCacheTests -v minimal
```

Expected: compile failures for image loader/cache/resolution types.

**Step 4: Implement result validation, lazy entries, and disposal.**

Use dictionaries keyed by sheet and `SpriteReference`. Store one sheet entry for success or failure. Cache final nonempty resolutions. Validate source extents with checked/64-bit arithmetic before publication. Keep one disposed flag and reject use afterward. Dispose only cache-owned successful images, preserving the first disposal failure while still attempting remaining image disposals; make the cache terminal even if disposal throws. Add no LRU, timestamps, async, locks, static cache, or backend code.

**Step 5 (green):**

```bash
dotnet test tests/MapEditor.Rendering.Tests/MapEditor.Rendering.Tests.csproj \
  --filter FullyQualifiedName~SpriteAssetCacheTests -v minimal
dotnet test tests/MapEditor.Rendering.Tests/MapEditor.Rendering.Tests.csproj -v minimal
```

Expected: all cache, manifest, and viewport tests pass.

**Step 6: Ownership/dependency audit and commit.**

```bash
git grep -nE 'static .*Dictionary|FileSystemWatcher|Task|async|lock|Avalonia|Godot|Bitmap|Texture|ImageSharp|System.Drawing' -- src/MapEditor.Rendering || true
git grep -nE '\.Dispose\(' -- src/MapEditor.Rendering/Assets tests/MapEditor.Rendering.Tests/SpriteAssetCacheTests.cs
git diff --check
git add src/MapEditor.Rendering/Assets tests/MapEditor.Rendering.Tests/SpriteAssetCacheTests.cs \
  tests/MapEditor.Rendering.Tests/Fakes
git commit -m "feat(map-editor): cache sprite sheet assets"
```

Expected: no static/global cache, watcher/background/UI/backend dependency; disposal appears only at the locked ownership/error boundaries.

**Task 3 invariant matrix:**

| Invariant | Test |
|---|---|
| Unknown/empty references do not load sheets | empty/unknown tests |
| Each known sheet has one lazy load attempt per cache | same/different/negative cache tests |
| Source rect is checked against decoded dimensions | exact-bound/outside/invalid-dimension tests |
| One image is shared by all frames on its sheet | identity and call-count test |
| Cache owns and disposes successful images exactly once | disposal tests |
| Loader and manifest are borrowed, never disposed | lifetime tests/source audit |
| A fresh cache is the only retry/reload mechanism | separate-cache tests |
| Failed manifest publication cannot damage a current context | failed-open/no-loader plus Part 4 boundary review |

---

## Task 4: Compose five layers, placeholders, and overlays into neutral draw operations

**Files:**
- Create: `src/MapEditor.Rendering/Composition/MapLayerVisibility.cs`
- Create: `src/MapEditor.Rendering/Composition/MapRenderOptions.cs`
- Create: `src/MapEditor.Rendering/Composition/MapDrawOperations.cs`
- Create: `src/MapEditor.Rendering/Composition/IMapDrawSink.cs`
- Create: `src/MapEditor.Rendering/Composition/MapRenderer.cs`
- Create: `tests/MapEditor.Rendering.Tests/MapRendererTests.cs`
- Create: `tests/MapEditor.Rendering.Tests/Fakes/RecordingMapDrawSink.cs`

**Step 1: Write failing sprite/layer/anchor tests.**

Construct small Part 1 documents, literal manifests, and fake images. Add:

- `Render_EmitsLayersZeroThroughFourRegardlessOfReferenceNumericOrder`.
- `Render_WithinLayerUsesRowMajorTileOrder`.
- `Render_HiddenLayerIsNotReadForAssetResolutionOrEmitted`.
- `Render_UsesExactManifestSourceRect`.
- `Render_BottomCenterAnchors32x32And48x64Frames` using explicit world/screen destinations.
- `Render_DestinationScalesAtEveryZoomWithoutChangingSourceRect`.
- `Render_AlwaysRequestsNearestNeighborSampling`.
- `Render_CullsCellOutsideViewport`.
- `Render_TallSpriteAnchoredOneRowBelowVisibleCellsWhenItOverlaps`.
- `Render_ConservativeCandidateThatDoesNotActuallyIntersectEmitsNothing`.
- `Render_GiantValidFrameOnSmallMapRendersWithoutManifestDimensionCap` using a fake image, not a giant allocation.
- `Render_SeesDocumentSetLayerImmediatelyWithoutRendererRecreation`.
- `LayerVisibility_ValidatesFiveBitsAndLayerIndexesWithoutMutation`.

Capture typed sink calls in one ordered list with a test-only discriminant. Do not duplicate renderer formulas in expected helpers; use explicit numeric expected rectangles.

**Step 2: Write failing placeholder tests.**

Add:

- `Render_GraphicZeroEmitsNothingEvenWithNonzeroSheet`.
- `Render_NonzeroGraphicWithZeroOrNegativeSheetEmitsUnknownSheetPlaceholder`.
- `Render_UnknownGraphicMissingFileLoadFailureAndOutsideFrameEmitReasonedPlaceholders`.
- `Render_PlaceholderPreservesExactLayerTileAndSignedReference`.
- `Render_PlaceholderUsesCellRectRatherThanManifestFrameRect`.
- `Render_OffscreenMissingReferenceDoesNotLoadOrEmitPlaceholder`.
- `Render_PlaceholderPaletteIsConspicuousAndLocked`.
- `Render_DoesNotChangeDocumentBytesOrEditSessionDirtyHistory`: encode before/after with Part 1 codec and inspect Part 2 session state.

**Step 3: Write failing overlay/order tests.**

Add:

- `Render_BlockedOverlayUsesOnlyBlockedBitAndVisibleCells` with unknown/high signed bits.
- `Render_HiddenLayersDoNotHideBlockedOverlay`.
- `Render_GridEmitsUniqueVisibleBoundaryLinesOnly` for one cell and a partial 2×2 viewport.
- `Render_GridLinesAreClippedToVisibleMapIntersection`.
- `Render_SelectedThenHoveredAreLastAndCanShareCell`.
- `Render_OutOfMapOrOffscreenTargetsEmitNothing`.
- `Render_DefaultOptionsShowAllLayersAndNoOverlays`.
- `Render_OperationOrderIsFiveLayersThenBlockedGridSelectedHovered`.
- `Render_1000x1000SparseMapReadsOnlyCandidateReferences`: place one visible/candidate reference and several distant nonempty references on distinct known sheets; assert exact fake-loader paths/counts prove distant references were not resolved, in addition to emitted operations.
- `Render_GiantFrameCandidateRangeAboveBudgetThrowsBeforeReadsResolutionOrSinkCalls`: use a valid giant frame plus distant nonempty references on a 1000×1000 map; assert requested/maximum counts, zero loader calls, and zero sink calls rather than timing the test or merely counting output from an empty map.
- `Render_WorkBudgetCountsVisibleLayersAndBlockedReadsButNotHiddenLayersOrGrid` with exact just-under/over cases.
- `Render_NullDependenciesThrowBeforeSinkCalls`.
- `Render_DisposedAssetsThrowsBeforeSinkCalls`.
- `Render_SinkFailurePropagatesAndNextRenderCanRetry`.

The bounded-work tests must use nonempty distant tile references and instrument fake-loader/reference-resolution calls (or a narrower test seam that counts document/layer reads). Operation count on an empty map is not proof of bounded candidate work. Pin the 64-bit preflight arithmetic and assert an over-budget request fails before any document/cache/sink side effect. Do not use elapsed-time or process-heap assertions.

**Step 4 (red):**

```bash
dotnet test tests/MapEditor.Rendering.Tests/MapEditor.Rendering.Tests.csproj \
  --filter FullyQualifiedName~MapRendererTests -v minimal
```

Expected: compile failures for draw/options/renderer interfaces.

**Step 5: Implement visibility/options/operations and streaming composition.**

Use outer layer loop 0…4 and inner row-major candidate loops. Before those loops, compute the locked tile-read upper bound with checked/64-bit products and throw `MapRenderWorkLimitException` when it exceeds 800,000; perform this after argument/range validation but before any document indexer/layer read, `Resolve`, or sink call. Resolve only nonempty references in visible layers. Exact-test ready sprite destinations and placeholder cells before sink calls. Then loop only `VisibleCells` for blocked overlays. Generate unique grid boundaries from the visible range/map intersection. Emit selected and hovered last. Keep palette values in one internal static value holder. Do not add a manifest dimension/area cap, silently truncate candidates, materialize all operations, capture document snapshots, build a per-tile index, cache tile resolution, or catch sink/work-limit failures.

**Step 6 (green focused/full Rendering):**

```bash
dotnet test tests/MapEditor.Rendering.Tests/MapEditor.Rendering.Tests.csproj \
  --filter FullyQualifiedName~MapRendererTests -v minimal
dotnet test tests/MapEditor.Rendering.Tests/MapEditor.Rendering.Tests.csproj -v minimal
```

Expected: every rendering operation test and all prior Part 3 tests pass.

**Step 7: Run integration and dependency audits.**

```bash
git grep -nE 'Avalonia|Godot|System.Drawing|Skia|ImageSharp|Control|DrawingContext|Bitmap|Texture2D' -- src/MapEditor.Rendering || true
git grep -nE 'SetLayer|SetFlags|MapEditSession|MapFileStore|MapFileRevision' -- src/MapEditor.Rendering || true
git grep -nE 'MapTile\[\]|MapDocument\[\]|new .*[Ww]idth.*[Hh]eight|TileCount' -- src/MapEditor.Rendering || true
dotnet list src/MapEditor.Rendering/MapEditor.Rendering.csproj reference
dotnet list tests/MapEditor.Rendering.Tests/MapEditor.Rendering.Tests.csproj reference
git diff --check
```

Expected: Rendering references only Core, has no backend/storage/edit mutation, and contains no map-sized retained allocation strategy. Test project references Rendering/Core.

**Step 8: Run full regression gates and commit.**

```bash
dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj -v minimal
dotnet test tests/MapEditor.Rendering.Tests/MapEditor.Rendering.Tests.csproj -v minimal
dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj -v minimal
dotnet test Goose2ClientGodot.sln -v minimal
dotnet build Goose2ClientGodot.sln -v minimal
git status --short
git diff --stat
git diff --check
git add src/MapEditor.Rendering/Composition tests/MapEditor.Rendering.Tests/MapRendererTests.cs \
  tests/MapEditor.Rendering.Tests/Fakes/RecordingMapDrawSink.cs
git commit -m "feat(map-editor): compose neutral map draw operations"
```

Require zero failures/errors and no warning introduced by Part 3. Do not rely on historical test counts.

**Task 4 invariant matrix:**

| Invariant | Test |
|---|---|
| Five layers emit strictly 0→4; cells are row-major within a layer | ordering tests |
| Hidden layers cause no operation or asset resolution | visibility test with loader counts |
| Source rectangles are unchanged converter metadata | exact source tests |
| Destination uses exact 32-pixel bottom-center anchor at every zoom | explicit frame/zoom tests |
| Only intersecting candidate art emits | ordinary/tall/conservative culling tests |
| Valid giant frames are accepted while candidate/reference reads have a hard preflight bound | small-map giant render, sparse distant-reference instrumentation, and over-budget no-side-effect tests |
| Every nonempty unresolved visible pair gets a reasoned placeholder | status/reference matrix |
| Empty graphics remain empty and unmodified | graphic-zero test |
| Blocked/grid/selection/hover are optional, clipped, and ordered | overlay/order tests |
| Rendering is read-only and fresh after edit/undo | bytes/session and immediate-set tests |
| Million-tile maps retain no renderer object per tile and cannot be scanned because of giant frame metadata | sparse distant-reference call counts, over-budget preflight test, and source audit |
| Backend errors do not corrupt renderer/cache/document state | sink retry test |

---

## Failure handling checklist

| Operation/failure | Required behavior |
|---|---|
| Invalid asset-root argument | `ArgumentException`/`ArgumentOutOfRangeException` before filesystem or loader access |
| Missing manifest | `SpriteManifestException(ManifestNotFound)` with full manifest path; no cache published |
| Manifest read failure | `SpriteManifestException(ReadFailed)` with inner exception/path; no cache published |
| Invalid JSON/shape/value | `IOException`-derived typed manifest error naming property/reference; no partial manifest/cache |
| Empty `sheets` or empty per-sheet frame object | Valid immutable manifest; maxima remain 32; no PNG load |
| Missing manifest sheet/graphic for map reference | Cached non-ready resolution; visible placeholder; no loader call/map mutation |
| Missing PNG | Loader `NotFound` → cached `MissingSheetFile`; placeholders for that sheet |
| Unreadable/undecodable PNG | Cached `SheetLoadFailed`; image null; placeholders; app remains usable |
| Loader success with null/invalid dimensions or malformed result | `InvalidOperationException` for adapter bug; dispose improperly transferred image; no ready entry |
| Frame beyond decoded sheet | Cache `FrameOutsideSheet` for only that reference; other valid frames work |
| Resolve/render after cache disposal | `ObjectDisposedException`; no loader/sink call |
| One image disposal throws | Cache stays terminal and attempts remaining owned disposals; surface first failure |
| Invalid viewport/zoom/map dimensions/layer mask | Argument exception before sink operation or document/cache mutation |
| Valid culling range exceeds 800,000 requested tile reads | `MapRenderWorkLimitException` with requested/maximum counts before document/cache/sink access; caller can zoom in or reduce visible work |
| Hover/selection outside map/viewport | Valid no-op for that overlay; other rendering proceeds |
| Sink throws | Propagate; retain no batch/progress state; later render may retry |
| Failed Part 4 asset reload | New cache never published; current cache/renderer remain active |
| Map art unavailable | Map Core/edit/save remains fully functional because references are numeric |

Do not convert programmer errors, undefined enums, malformed adapter results, or sink exceptions into missing-art placeholders. Do not let expected per-sheet filesystem/decode errors escape the Part 4 loader adapter; it reports them through `SpriteSheetLoadResult`.

---

## Consolidated invariant-to-test matrix

| ID | Invariant | Automated proof / audit |
|---|---|---|
| R1 | Rendering project depends only on Core and .NET | project-reference/dependency grep |
| R2 | Manifest path/shape matches converter output | literal contract and focused converter test |
| R3 | Manifest validation is strict, `IOException`-catchable, actionable, and all-or-nothing while accepting converter-valid empty objects | validation/error/empty matrices |
| R4 | Published manifest metadata is deterministic and immutable, including empty views and 32-pixel empty maxima | sorting/read-only/empty tests |
| R5 | Graphic 0 is empty; all other signed unresolved references survive as placeholders | empty/signed placeholder tests |
| R6 | Sheets load lazily once and resolutions are cached | fake-loader call-count tests |
| R7 | Decoded image dimensions guard source rectangles | bounds tests |
| R8 | Cache owns images, renderer borrows them, and disposal is exact | lifetime/publication tests/audit |
| R9 | Transform math is reversible at 25/50/100/200/400 percent | transform tables |
| R10 | Cursor zoom preserves world point and pan direction is exact | zoom/pan tests |
| R11 | Tile hit testing floors Y-down world coordinates | negative-coordinate test |
| R12 | Culling is half-open, map-clipped, zoom-correct, tall-art-safe, and O(1) for giant valid extents | culling matrix/giant-extent test |
| R13 | Renderer retains no per-tile state and performs at most 800,000 preflight-accounted tile reads | sparse distant-reference instrumentation, work-limit no-side-effect tests, and source audit |
| R14 | Layer order is 0→4 and hidden layers do no work | operation order/loader count tests |
| R15 | Source rect and bottom-center destination match client/converter | source/anchor tests |
| R16 | Sprite sampling is always nearest-neighbor | operation tests |
| R17 | Missing art has conspicuous, diagnostic placeholders without data loss | placeholder matrix/palette/codec comparison |
| R18 | Overlays are optional, clipped, unique, and after map art | overlay tests |
| R19 | Eager edits/undo/redo require no renderer cache invalidation | fresh-read tests and no document-keyed cache audit |
| R20 | Asset and rendering failures cannot alter map/edit/save state | bytes/session/lifetime/failure tests |
| R21 | Part 4 integration surface is limited to image loader/image/draw sink | public API and dependency audit |
| R22 | Existing Godot/client/converter behavior is untouched | no-file-change audit plus full solution/focused converter gates |

---

## Red-team review

Perform after Task 4 and before declaring Part 3 complete. Fix a finding in the commit that introduced it, rerun that task's focused tests and all final gates, and do not add Part 4 implementation as a workaround.

1. **Manifest shape attack:** Remove/retag required properties, add trailing JSON, fractional values, wrong-valued objects, and overflows. Confirm typed all-or-nothing rejection with full path and useful element context. Separately confirm empty `sheets` and empty per-sheet frame objects are accepted, immutable, and use 32-pixel maxima; do not misclassify converter-valid emptiness as malformed.
2. **Identifier alias attack:** Include `"1"` and `"01"`, duplicate raw names, signs, whitespace, exponent text, zero, negative, and `Int32` overflow. Confirm no dictionary overwrite or culture-dependent parsing.
3. **Rectangle overflow/giant-frame attack:** Use `x = int.MaxValue, width = 1`, negative origins, zero sizes, and a decoded image one pixel too small. Confirm checked rejection/status, not wrapped acceptance. Also use a checked giant width/height with a fake image and confirm it remains valid—there is no unevidenced manifest dimension/area cap.
4. **Generated-contract drift attack:** Compare parser fixture against `FrameManifestBuilder` and its focused test. Reject invented filename patterns, zero-based frame remapping, atlas repacking, or a tile size other than 32.
5. **Eager-load attack:** Open a manifest with hundreds of sheets and resolve one frame. Exactly one loader call is allowed; parsing must not inspect any PNG.
6. **Negative-cache attack:** Repeatedly render missing/decode-failed sheets. Loader count stays one per sheet per cache; a newly constructed cache retries exactly once.
7. **Ownership attack:** Resolve many frames on one sheet, dispose twice, and count image disposal. Reject image disposal by operations/sinks/renderer, loader disposal, static sharing, or double-disposal.
8. **Publication race attack:** Model an old render operation while a replacement cache is built. Confirm the documented Part 4 sequence never disposes old images before all old synchronous draw use ends; Rendering itself exposes no global swap.
9. **Disposed-borrow attack:** Retain an operation after cache disposal and try to use it. This is explicitly invalid; ensure APIs/docs do not imply operations own images or extend lifetime.
10. **Arbitrary-zoom attack:** Cast an undefined enum and attempt transform/culling. It must fail before arithmetic; no public constructor accepts an arbitrary scale double.
11. **Cursor drift attack:** Zoom successively in/out around an off-center fractional cursor at all levels. The represented world point remains fixed within double tolerance; reject integer rounding in neutral math.
12. **Negative-coordinate attack:** Pan just above/left of world zero and hit test `-0.001`. It maps to tile -1; culling then clips it rather than truncating to tile 0.
13. **Boundary attack:** Put viewport right/bottom exactly on a tile edge and one representable amount beyond. Half-open rules must avoid an extra tile at the exact edge and include it after crossing.
14. **Tall-sprite popping attack:** Place a 64-pixel sprite with its anchor one row below visible cells. It must be a candidate and render if its top intersects; an equally distant 32-pixel sprite must not emit.
15. **Culling/work-amplification attack:** Put nonempty references on distinct known sheets near and far across a 1000×1000 map and view one cell with ordinary frame sizes. Instrument tile/reference resolution through exact fake-loader paths/counts (or a narrower read-count seam) and confirm distant references and hidden layers do no work. Then make one valid giant frame expand candidates across the map: O(1) culling may return that range, but render preflight must throw with exact requested/maximum counts before any tile/reference/cache/sink access. Confirm the same giant frame renders on a small map whose request is under budget; do not add an asset-size cap or silently truncate.
16. **Anchor attack:** For a 48×64 frame at tile `(1,2)`, independently calculate anchor `(48,96)` and world destination `(24,32,48,64)`. Reject center anchoring, top-left anchoring, Y flip, source offsets in destination, or use of tile center as feet.
17. **Layer-order attack:** Use numerically shuffled references on all five layers and neighboring rows. Calls must group layers 0→4 and then row-major; reference/sheet ID must never drive order.
18. **Empty/data-loss attack:** Render `(123,0)`, `(0,456)`, negatives, and unknown positives, then encode the map. Only the first is visually empty; all document bytes remain identical.
19. **Placeholder invisibility attack:** Inspect operation palette/reason/geometry. Missing art must be a magenta/yellow cell marker with exact reference metadata, not silently omitted or replaced in the model.
20. **Overlay duplication attack:** Render partial multi-cell ranges. Grid has one shared boundary line, blocked has one operation per visible blocked cell, and selected/hover remain last without changing layer visibility.
21. **Stale-edit attack:** Render, paint/erase/toggle, undo, and redo using Part 2, rendering after each. Calls must reflect current Core values without renderer/session recreation or a per-tile cache flush.
22. **Sink-failure attack:** Throw on the Nth operation and rerender. Renderer must not resume midway, retain a batch, mark assets failed, alter options, or mutate document/history.
23. **Backend-dependency attack:** Search public signatures, project references, and source for Avalonia, Godot, Skia, ImageSharp, `System.Drawing`, controls, bitmaps, textures, drawing contexts, dispatchers, and native handles. None are allowed.
24. **Scope-creep attack:** Reject settings, dialogs, selected-directory state, controls, pointer adapters, file watchers, async loading, LRU limits, hot reload, sprite search, build/publish scripts, converter changes, Godot migration, or App scaffolding.
25. **Repository-hygiene attack:** Confirm no `Assets/Sprites`, generated PNG/manifest, proprietary data, `.godot` edits, `bin/obj`, temp directories, SDK rewrites, or unrelated files are staged.

Final commands:

```bash
dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj -v minimal
dotnet test tests/MapEditor.Rendering.Tests/MapEditor.Rendering.Tests.csproj -v minimal
dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj -v minimal
dotnet test Goose2ClientGodot.sln -v minimal
dotnet build Goose2ClientGodot.sln -v minimal
dotnet test tools/AssetConverter/tests/AssetConverter.Tests/AssetConverter.Tests.csproj \
  --filter FullyQualifiedName~FrameManifestBuilderTests.Build_EmitsSheetGraphicRects_ForSheet1000 -v minimal
git grep -nE 'Avalonia|Godot|System.Drawing|Skia|ImageSharp' -- src/MapEditor.Rendering || true
git grep -nE 'SetLayer|SetFlags|MapEditSession|MapFileStore|MapFileRevision' -- src/MapEditor.Rendering || true
git diff --check
git status --short
```

Expected implementation commit sequence:

1. `feat(map-editor): add viewport rendering math`
2. `feat(map-editor): validate sprite manifests`
3. `feat(map-editor): cache sprite sheet assets`
4. `feat(map-editor): compose neutral map draw operations`

Each implementation commit must be independently buildable/testable at its stated gate. Do not squash unrelated work into these commits. Do not commit this planning-only request, and do not begin Avalonia application/settings/dialog/build implementation.
