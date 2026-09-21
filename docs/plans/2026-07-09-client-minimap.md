# Client Minimap Implementation Plan

**Goal:** Add a top-right HUD minimap to the Godot client: a 1px/tile average-color bitmap of the whole map (topmost non-empty layer among 4→3→2→0, skipping 1), shown as a 64×64-tile player-centered window at 3px/tile with nearest filtering and a player arrow.

**Architecture:** Three pieces: `MinimapColors` (pure layer-priority logic, unit-tested), `MinimapBitmapBuilder` (Godot; builds the whole-map `Image` at map load using `SpriteCache` + `SpriteManifest` rects, averaging opaque frame pixels per unique (sheet, graphic)), and `MinimapControl` (HUD `Control` that blits a clamped 64×64 source region of the bitmap and draws the player arrow). `MapManager` builds the bitmap in `_Ready` and propagates single-pixel updates from `TileUpdatePacket`.

**Tech Stack:** C# / Godot 4 (GodotSharp 4.6.2), xunit 2.9.2. No new dependencies.

**Design doc:** `docs/plans/2026-07-09-client-minimap-design.md`

---

## APIs verified

| API | Citation |
|---|---|
| `SpriteCache.Get(int sheet, int graphic)` → `AtlasTexture?` (null when sheet==0, no manifest rect, or missing PNG) | `Scripts/Map/SpriteCache.cs:25` |
| `SpriteManifest.TryGetRect(int sheet, int graphic, out FrameRect)` | `Scripts/Map/SpriteManifest.cs:16` |
| `AtlasTexture.Atlas` (`Texture2D`), `AtlasTexture.Region` (`Rect2`) | `Scripts/Map/SpriteCache.cs:35-36` |
| `MapDocument.this[int x, int y]` → `MapTile`; `Width`/`Height` | `src/MapEditor.Core/MapDocument.cs:169,87,89` |
| `MapTile.GetLayer(int)` → `MapTileLayer(Sheet, Graphic)` | `src/MapEditor.Core/MapDocument.cs:36` |
| `MapManager._Ready` (map from `GameManager.Instance.CurrentMap`, early return when null; `EnsureHud()` at end) | `Scripts/MapManager.cs:60-116` |
| `MapManager.OnTileUpdate` (bounds check, `MapTileUpdate.Apply`, per-layer `RefreshCell`) | `Scripts/MapManager.cs:377-387` |
| `MapManager._cache` (`SpriteCache`) | `Scripts/MapManager.cs:15,63` |
| `MapCoords.WorldToTile(Vector2 world)` → `(int x, int y)` | `Scripts/Map/MapCoords.cs:23` |
| `GameManager.Hud` (`GameHud`), `GameManager.CurrentMapManager` | `Scripts/GameManager.cs:68,60` |
| `GameHud._Ready` instantiates windows; plain-`new` nodes are added too (e.g. `QuestWindowManager`) | `Scripts/UI/GameHud.cs:37,80` |
| UI-scale pattern: `_geom = UiScaleLayout.Snapshot(this); applier.RegisterWindow(this); Relayout();` + `TreeExited += UnregisterWindow` | `Scripts/UI/BuffEffectsWindow.cs:37-49` |
| `UiScaleLayout.Snapshot(Control)` / `Apply(records, factor)` | `Scripts/UiScaleLayout.cs:24,33` |
| `UiScaleApplier.RegisterWindow(IScalableWindow)` / `UnregisterWindow` / `Factor` | `Scripts/UiScaleApplier.cs:68` |
| `TileUpdatePacket` fields: `X`, `Y`, `Tiles` (10 ints), `Flags` | `Scripts/Network/Packets/TileUpdatePacket.cs:8-11` |
| Test project compiles `Scripts/**/*.cs` and references `MapEditor.Core` + GodotSharp (Godot value types like `Color`/`Vector2I` are usable in pure tests) | `tests/Goose2Client.Tests/Goose2Client.Tests.csproj` |

Godot runtime APIs used (standard Godot 4, GodotSharp 4.6.2): `Texture2D.GetImage()`,
`Image.GetPixelv(Vector2I)`, `Image.Create(w, h, false, Image.Format.Rgba8)`,
`Image.SetPixelv(Vector2I, Color)`, `ImageTexture.CreateFromImage(Image)`,
`ImageTexture.Update()`, `Control.SetAnchorsPreset(LayoutPreset.TopRight)`,
`CanvasItem.TextureFilter`, `CanvasItem.DrawTextureRect`, `DrawColoredPolygon`, `QueueRedraw`.

## Invariant-to-test matrix

| Invariant | Proved by |
|---|---|
| Layer priority 4 > 3 > 2 > 0 | `PickColor_ReturnsTopmostOccupiedLayer` |
| Layer 1 is never consulted | `PickColor_IgnoresLayer1` (adversarial: a wrong loop over 0..4 fails this) |
| Null provider result falls through to next layer (does not stop) | `PickColor_FallsThroughWhenTopLayerHasNoOpaquePixels` (adversarial: early-return-on-null fails this) |
| All-empty tile → null | `PickColor_ReturnsNullWhenAllLayersEmpty` |
| Build fills every cell of the map | `Build_ProducesOneColorPerTile` |
| Minimap pixel tracks `TileUpdatePacket` | Manual smoke (Godot runtime; pure selection logic covered above) |

---

### Task 1: `MinimapColors` (pure layer-priority logic)

**Files:**
- Create: `Scripts/Map/MinimapColors.cs`
- Test: `tests/Goose2Client.Tests/MinimapColorsTests.cs`

**Step 1: Write the failing tests**

`tests/Goose2Client.Tests/MinimapColorsTests.cs` (global namespace with `using Godot;`
`using Goose2Client.Map;` `using Xunit;`, matching `MapCoordsTests.cs`):

- `PickColor_ReturnsTopmostOccupiedLayer`: tile with distinct colors on layers 0 and 4 → returns the layer-4 color.
- `PickColor_IgnoresLayer1`: tile with a color ONLY on layer 1 → returns null.
- `PickColor_FallsThroughWhenTopLayerHasNoOpaquePixels`: provider returns null for layer 4's graphic and a color for layer 3's → returns the layer-3 color.
- `PickColor_ReturnsNullWhenAllLayersEmpty`: all layers (0,0) → null.
- `Build_ProducesOneColorPerTile`: 3×2 fake map via the tile accessor; result array length 6 with expected colors per cell.

Test helper: build `MapDocument` with `MapDocument.Create(w, h)` + `SetLayer` (public,
`src/MapEditor.Core/MapDocument.cs:155`) and a provider `Func<int,int,Color?>` over a
`Dictionary<(int,int),Color>`.

**Step 2: Run to verify red**

Run: `dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj --filter MinimapColors -v minimal`
Expected: build failure (type does not exist).

**Step 3: Implement**

`Scripts/Map/MinimapColors.cs`, `namespace Goose2Client.Map;`, `using Godot;` (Color only):

- `public static Color? PickColor(MapTile tile, Func<int, int, Color?> provider)` —
  for `layer in {4, 3, 2, 0}`: `var l = tile.GetLayer(layer); if (l.Sheet == 0 || l.Graphic == 0) continue; var c = provider(l.Sheet, l.Graphic); if (c != null) return c;` return null.
- `public static Color[] Build(MapDocument map, Func<int, int, Color?> provider)` —
  `Color[] colors = new Color[map.Width * map.Height]`; per cell `colors[y * map.Width + x] = PickColor(map[x, y], provider) ?? Colors.Black;`

No comments beyond what the codebase already has on similar pure helpers (see AGENTS.md).

**Step 4: Run to verify green**

Run: `dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj --filter MinimapColors -v minimal`
Expected: PASS (5 tests).

**Step 5: Commit**

```bash
git add Scripts/Map/MinimapColors.cs tests/Goose2Client.Tests/MinimapColorsTests.cs
git commit -m "feat(minimap): pure per-tile color selection (topmost of layers 4,3,2,0)"
```

---

### Task 2: `MinimapBitmapBuilder` (Godot, whole-map Image)

**Files:**
- Create: `Scripts/Map/MinimapBitmapBuilder.cs`

No unit tests (needs the Godot runtime for `Texture2D.GetImage()`); verified by compile +
manual smoke in Task 5.

**Implementation contract**

`namespace Goose2Client.Map;`, `public static class MinimapBitmapBuilder`:

- `public static Image Build(MapDocument map, Func<int, int, Color?> provider)` — the
  provider is supplied by `MapManager` (Task 4) so the same memoized (sheet, graphic) →
  color lookup serves both the initial build and single-pixel `TileUpdatePacket` updates.
  Provider contract (implemented in Task 4 as `MapManager.TileColor`):
  - `var atlas = cache.Get(sheet, graphic); if (atlas == null) return null;`
    (null covers sheet==0, missing manifest rect, missing PNG — `Scripts/Map/SpriteCache.cs:25`)
  - `var img = atlas.Atlas.GetImage();` iterate `atlas.Region` pixels via
    `img.GetPixelv(new Vector2I(px, py))` for `py in [Y, Y+H)`, `px in [X, X+W)`; average
    **only pixels with A > 0** (accumulate R/G/B as int, divide by opaque count).
  - Zero opaque pixels → null (empty layer, falls through in `PickColor`).
  - Memoized per (sheet, graphic).
- Builder body:
  - `var out = Image.Create(map.Width, map.Height, false, Image.Format.Rgba8);`
    per cell: `out.SetPixelv(new Vector2I(x, y), MinimapColors.PickColor(map[x, y], provider) ?? Colors.Black);`
  - Return `out`.

Precondition: `map` non-null, `provider` non-null.
Postcondition: `out.GetWidth() == map.Width`, `out.GetHeight() == map.Height`; one call is
one-time map-load work (a few million `GetPixelv` calls worst case is acceptable — it runs
once per map enter, not per frame).

**Verification:** `dotnet build Goose2ClientGodot.csproj -v minimal` → zero errors.

**Step: Commit**

```bash
git add Scripts/Map/MinimapBitmapBuilder.cs
git commit -m "feat(minimap): whole-map 1px/tile average-color bitmap builder"
```

---

### Task 3: `MinimapControl` (HUD control)

**Files:**
- Create: `Scripts/UI/MinimapControl.cs`
- Modify: `Scripts/UI/GameHud.cs` (accessor + instantiation)

**Implementation contract**

`namespace Goose2Client.UI;`, `public partial class MinimapControl : Control, IScalableWindow`:

- Constants: `WindowTiles = 64`, `BasePixels = 192` (64 × 3px/tile at UI scale 1).
- State: `ImageTexture _bitmap`, `int _mapWidth`, `int _mapHeight`, `List<UiScaleLayout.GeomRecord> _geom`, `int _lastPlayerTileX/Y` (sentinel -1).
- `public void SetMap(int mapWidth, int mapHeight, ImageTexture bitmap)` — store, `_lastPlayerTileX = _lastPlayerTileY = -1`, `QueueRedraw()`. Called once per map enter from `MapManager` (Task 4).
- `public void Invalidate()` — `QueueRedraw()` (called after a pixel update).
- `_Ready`:
  - `TextureFilter = TextureFilterEnum.Nearest;`
  - `SetAnchorsPreset(LayoutPreset.TopRight);` then 1x base offsets: `OffsetLeft = -BasePixels - 8; OffsetTop = 8; OffsetRight = -8; OffsetBottom = 8 + BasePixels;` (8px margin, matching the 8px margins used by corner windows).
  - `MouseFilter = MouseFilterEnum.Ignore;`
  - UI-scale registration, exactly the established pattern (`Scripts/UI/BuffEffectsWindow.cs:37-49`):
    `_geom = UiScaleLayout.Snapshot(this); UiScaleApplier.Instance.RegisterWindow(this); Relayout(); TreeExited += () => UiScaleApplier.Instance.UnregisterWindow(this);`
  - `public void Relayout() => UiScaleLayout.Apply(_geom, UiScaleApplier.Instance.Factor);`
- `_Process(double delta)`:
  - `var player = GameManager.Instance?.CurrentMapManager?.LocalPlayer;`
  - if `player == null || _bitmap == null` return;
  - `var (tx, ty) = MapCoords.WorldToTile(player.GlobalPosition);`
  - if `(tx, ty) != (_lastPlayerTileX, _lastPlayerTileY)` → store and `QueueRedraw()`.
- `_Draw()`:
  - if `_bitmap == null` return.
  - Window origin: `winX = Mathf.Clamp(playerX - WindowTiles / 2, 0, Mathf.Max(0, _mapWidth - WindowTiles));` (same for Y); use the last known player tile (if no player yet, origin 0,0).
  - `srcW = Mathf.Min(WindowTiles, _mapWidth)`, `srcH = Mathf.Min(WindowTiles, _mapHeight)`; `source = new Rect2(winX, winY, srcW, srcH)`.
  - `float px = Size.X / WindowTiles;` `dest = new Rect2(0, 0, srcW * px, srcH * px);`
    (local size scales with UI factor because the snapshot-scaled offsets resize the control rect; drawing in local units keeps it correct at every factor).
  - `DrawTextureRect(_bitmap, source, dest);`
  - Player arrow (only when a player tile is known and inside the map):
    `ax = (playerX - winX) * px + px / 2;` `ay = (playerY - winY) * px + px / 2;`
    `DrawColoredPolygon` upward triangle `[(ax, ay - 4), (ax - 4, ay + 4), (ax + 4, ay + 4)]` in white.

**`GameHud.cs` changes**

- Add accessor: `public MinimapControl Minimap { get; private set; }`
- In `_Ready`, after the window instantiations (near the `new QuestWindowManager()` block):
  `Minimap = new MinimapControl(); AddChild(Minimap);`

**Verification:** `dotnet build Goose2ClientGodot.csproj -v minimal` → zero errors.

**Step: Commit**

```bash
git add Scripts/UI/MinimapControl.cs Scripts/UI/GameHud.cs
git commit -m "feat(minimap): top-right HUD minimap control (64x64 tile window, player arrow)"
```

---

### Task 4: `MapManager` wiring (build at load, propagate tile updates)

**Files:**
- Modify: `Scripts/MapManager.cs` (`_Ready` ~line 116, `OnTileUpdate` lines 377-387)

**Mutation impact:**

- Source of truth changed: none — `MapDocument` tiles remain canonical; the minimap bitmap
  is derived state owned by `MapManager` (new fields `Image _minimapImage`,
  `ImageTexture _minimapTexture`, plus the memoized provider from Task 2 exposed as an
  instance method `Color? TileColor(int sheet, int graphic)` so `OnTileUpdate` can reuse it).
- Important readers of the mutated tile: layer `RefreshCell` calls (existing,
  `Scripts/MapManager.cs:382-386`) and the new minimap pixel.
- Derived/cached state affected: the minimap `Image`/`ImageTexture` (new); nothing else
  reads the bitmap.
- Required propagation sequence (inside `OnTileUpdate`, after `MapTileUpdate.Apply`):
  1. `var c = MinimapColors.PickColor(_map[p.X, p.Y], TileColor);`
  2. `_minimapImage.SetPixelv(new Vector2I(p.X, p.Y), c ?? Colors.Black);`
  3. `_minimapTexture.Update();` (re-uploads the image to the GPU)
  4. `GameManager.Instance.Hud?.Minimap?.Invalidate();`
- Invariants to preserve:
  - Minimap pixel always matches the current tile's selected color.
  - `OnTileUpdate`'s existing bounds check (line 380) still guards all of the above.
  - No minimap work when `_map == null` (the `_Ready` early return at line 70 must skip
    bitmap construction and leave the minimap showing nothing).
- Observable proof: manual smoke in Task 5 (a `TileUpdatePacket`-changed tile recolors its
  minimap pixel). Pure selection logic is unit-tested in Task 1.

**Implementation**

- New fields: `private Image _minimapImage; private ImageTexture _minimapTexture;` and a
  `Dictionary<(int,int), Color?>` memo backing an instance method
  `Color? TileColor(int sheet, int graphic)` — the provider contract from Task 2
  (`SpriteCache.Get` → `AtlasTexture.Atlas.GetImage()` → average opaque pixels of
  `AtlasTexture.Region`, null when no opaque pixels).
- In `_Ready`, after `GameManager.Instance.EnsureHud();` (line 115):
  ```csharp
  _minimapImage = MinimapBitmapBuilder.Build(_map, TileColor);   // TileColor is the Task 2 provider
  _minimapTexture = ImageTexture.CreateFromImage(_minimapImage);
  GameManager.Instance.Hud.Minimap.SetMap(_map.Width, _map.Height, _minimapTexture);
  ```
  (Hud is non-null here: `EnsureHud` just ran. If `EnsureHud` ever yields a null Hud,
  `?.`-guard the `SetMap` call.)
- In `OnTileUpdate`, after the `MapTileUpdate.Apply(...)` call: the 4-step propagation
  sequence above, using `?.` for the Hud access.

**Verification:** `dotnet build Goose2ClientGodot.csproj -v minimal` → zero errors;
`dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj -v minimal` → all pass.

**Step: Commit**

```bash
git add Scripts/MapManager.cs
git commit -m "feat(minimap): build bitmap at map load, propagate TileUpdatePacket pixels"
```

---

### Task 5: Manual smoke

Run the client (`run.sh`), enter a map, and verify:

1. Minimap appears top-right, 192×192 at UI scale 1, shows the local 64×64 area around the
   player; trees/roofs visible as landmarks; empty/void tiles black.
2. Walking pans the window; at map edges the window clamps and the arrow goes off-center.
3. Player arrow tracks the player and points up.
4. Change UI scale (options) → minimap size and margins scale, texture stays crisp
   (nearest), no blur.
5. Warp to another map → minimap rebuilds for the new map.
6. If a `TileUpdatePacket` is easy to trigger in-test (GM/admin), confirm the pixel updates.
   Otherwise defer this check — flag it in the commit message.

No commit unless a fix is needed; fix commits reference `fix(minimap): ...`.

**Final regression gate:**

```bash
dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj -v minimal   # all pass
dotnet build Goose2ClientGodot.csproj -v minimal                            # zero errors
```
