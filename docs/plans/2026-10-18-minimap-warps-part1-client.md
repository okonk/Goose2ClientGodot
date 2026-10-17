# Minimap Warps — Part 1: Client Implementation Plan

**Goal:** Parse the `MWP` warp packet and paint warp tiles light blue on the minimap.

**Architecture:** `MapWarpPacket` parses one warp position. `MapManager` keeps a per-map `HashSet<Vector2I>` of warps, and paints every minimap pixel through one pure rule (`MinimapColors.PixelColor`), so a warp stays blue under a `TileUpdate`. Minimap texture uploads are batched to once per frame.

**Tech Stack:** Godot 4 / C# (.NET 10), xUnit (`tests/Goose2Client.Tests`).

Design: `docs/plans/2026-10-18-minimap-warps-design.md`. This is **Part 1 of 2**; Part 2 (server) is `2026-10-18-minimap-warps-part2-server.md`.

Baseline: `dotnet test tests/Goose2Client.Tests` → 1685 passed.

Follow `AGENTS.md`: no new comments/doc strings except the justified wire-format line noted below.

---

## APIs verified

- `PacketHandler` subclasses declare `Prefix` and `Parse(PacketParser)`; precedent `Scripts/Network/Packets/MapFlagsPacket.cs:1-22`.
- `PacketParser.GetInt32()` `Scripts/Network/PacketParser.cs:63` reads the next comma-delimited token; unread trailing tokens are simply never consumed, so `"MWP3,7,9"` parses fine reading two ints.
- Prefix dispatch `PacketManager.Handle` `Scripts/Network/PacketManager.cs:46-67` matches the shortest registered prefix of length 1..8. Existing `M*` prefixes: `MOC`, `MKW`, `MKC`, `MFL` — no `M`/`MW` prefix, so `MWP` cannot be shadowed.
- `PacketManager.Listen<T>` `:12` registers the handler on first listen; `Remove<T>` `:24`.
- 1-based wire coordinates, client subtracts 1: `TileUpdatePacket.cs:19-20`, `SetYourPositionPacket.cs:18-19`.
- Parse-test precedent: `tests/Goose2Client.Tests/NoticePacketTests.cs:12-16`. Test project compiles all of `Scripts/**/*.cs` (`tests/Goose2Client.Tests/*.csproj:14`), so `dotnet test` also proves `MapManager` compiles.
- `MinimapColors.PickColor(MapTile, Func<int,int,Color?>)` and `Empty` `Scripts/Map/MinimapColors.cs:12-26`. Test helpers `Tile(...)`/`Provider` in `tests/Goose2Client.Tests/MinimapColorsTests.cs:9-20`.
- `MapManager` (`Scripts/MapManager.cs`): `_minimapImage`/`_minimapTexture` `:24-25`; listeners registered `:95-117`, removed in `_ExitTree` `:136-157`; minimap built `:123-125` (the same `ImageTexture` instance is handed to `MinimapControl.SetMap`, so `ImageTexture.Update` on it is what the minimap draws); `_Process` `:278`; `OnTileUpdate` `:441-453`; the existing private provider is named `TileColor(int, int)` `:455` — hence the new helper is `PixelColor`, not `TileColor`.
- `MinimapControl.Invalidate()` `Scripts/UI/MinimapControl.cs:43`.

---

### Task 1: `MapWarpPacket`

**Files:**
- Create: `Scripts/Network/Packets/MapWarpPacket.cs`
- Test: `tests/Goose2Client.Tests/MapWarpPacketTests.cs`

Shape mirrors `MapFlagsPacket`: `int X`, `int Y`, `Prefix = "MWP"`, `Parse` reads `X = p.GetInt32() - 1`, `Y = p.GetInt32() - 1`. One justified comment above the class (wire format, per AGENTS.md):

```csharp
// Wire format: MWP<x>,<y> — 1-based; position only, later fields may be appended (server: P.MapWarp).
```

**Step 1: failing tests**

```csharp
[Fact]
public void Mwp_ConvertsOneBasedToZeroBased()
{
    var p = (MapWarpPacket)new MapWarpPacket().Parse(new PacketParser("MWP3,7", "MWP"));
    Assert.Equal(2, p.X);
    Assert.Equal(6, p.Y);
}

[Fact]
public void Mwp_IgnoresTrailingFields()
{
    var p = (MapWarpPacket)new MapWarpPacket().Parse(new PacketParser("MWP3,7,9,extra", "MWP"));
    Assert.Equal(2, p.X);
    Assert.Equal(6, p.Y);
}
```

The first is the adversarial one: it fails if the `- 1` is forgotten.

**Step 2 (red):** `dotnet test tests/Goose2Client.Tests --filter MapWarpPacketTests` → compile error, `MapWarpPacket` not found.

**Step 3:** implement.

**Step 4 (green):** same command → 2 passed.

**Step 5:** commit `feat(minimap-warps): MWP packet`.

---

### Task 2: `MinimapColors.Warp` + `PixelColor`

**Files:**
- Modify: `Scripts/Map/MinimapColors.cs`
- Test: `tests/Goose2Client.Tests/MinimapColorsTests.cs`

```csharp
public static readonly Color Warp = new(120f / 255f, 200f / 255f, 1f);

public static Color PixelColor(MapTile tile, bool isWarp, Func<int, int, Color?> provider)
    => isWarp ? Warp : PickColor(tile, provider) ?? Empty;
```

`Warp` is deliberately distinct from `GameColors.Blue` (0,146,255); tune in-game if needed.

**Step 1: failing tests** (reuse the file's `Tile`/`Provider` helpers)

- `PixelColor_WarpBeatsTileArt` — tile has layer art with a known color; `isWarp: true` → `MinimapColors.Warp`. **Adversarial:** fails if the tile art is allowed to win (the bug that would turn warps back to terrain on a `TileUpdate`).
- `PixelColor_NonWarpUsesTileArt` — same tile, `isWarp: false` → the art color.
- `PixelColor_NonWarpWithoutArtIsEmpty` — empty tile, `isWarp: false` → `MinimapColors.Empty`.

**Step 2 (red):** `dotnet test tests/Goose2Client.Tests --filter MinimapColorsTests` → compile error, `PixelColor` not found.

**Steps 3–4:** implement; same command green (existing `MinimapColorsTests` still pass).

**Step 5:** commit `feat(minimap-warps): warp minimap color`.

---

### Task 3: `MapManager` wiring + per-frame minimap flush

**Files:**
- Modify: `Scripts/MapManager.cs` (fields near `:24-26`, listeners `:95-117` / `:136-157`, `_Process` `:278`, `OnTileUpdate` `:441-453`, new `OnMapWarp` next to it)

**Mutation impact:**
- Source of truth changed: new `private readonly HashSet<Vector2I> _warps = new();` on the `MapManager` instance. A new map scene builds a new `MapManager`, so the set starts empty per map — this is the "clear on map change" mechanism; do not make it static or put it on `GameManager`.
- Important readers: `OnMapWarp` (writes), `OnTileUpdate` (reads `_warps.Contains`).
- Derived state affected: `_minimapImage` pixels → `_minimapTexture` (GPU copy) → `MinimapControl` draw.
- Required propagation sequence (both handlers):
  1. `_minimapImage.SetPixelv(pos, MinimapColors.PixelColor(_map[x, y], _warps.Contains(pos), TileColor));`
  2. `_minimapDirty = true;`
  3. In `_Process`, **before** the existing local-player block (it must run even with no local player): `if (_minimapDirty) { _minimapDirty = false; _minimapTexture.Update(_minimapImage); GameManager.Instance.Hud?.Minimap?.Invalidate(); }`
  - `ImageTexture.Update` re-uploads the whole image (≈4 MB at 1000×1000); per-packet uploads during a burst of hundreds of `MWP`s is the thing being avoided. Remove the immediate `Update`/`Invalidate` calls from `OnTileUpdate`.
- Invariants to preserve:
  - Every minimap pixel write goes through `PixelColor` (no remaining direct `PickColor(...) ?? Empty` in `MapManager`).
  - Out-of-bounds `MWP` is ignored (same guard as `OnTileUpdate:444`: `x < 0 || y < 0 || x >= _map.Width || y >= _map.Height`) and does not enter `_warps`.
  - Listener added in `_Ready` is removed in `_ExitTree`.
- Observable proof: `PixelColor` rule is unit-tested (Task 2); wiring has no Godot node harness (consistent with the existing minimap wiring) and is proved by the manual smoke below.

**Steps:**
1. Add fields `_warps` and `private bool _minimapDirty;`.
2. `pm.Listen<MapWarpPacket>(OnMapWarp);` after `OnTileUpdate`'s listen; matching `pm.Remove<MapWarpPacket>(OnMapWarp);` in `_ExitTree`.
3. `OnMapWarp`: cast, bounds-guard, `_warps.Add`, paint + dirty per the sequence above.
4. `OnTileUpdate`: replace the three minimap lines with paint-through-`PixelColor` + dirty.
5. `_Process`: dirty flush at the top.
6. `dotnet test tests/Goose2Client.Tests` → all green (1685 + 5 new), proving it compiles.
7. Commit `feat(minimap-warps): paint warps on the minimap`.

**Manual smoke (after Part 2 is deployed to the local server):** run the client (`./run.sh`) against the local server:
- Log in on a map with known warps → light blue pixels at those tiles.
- Walk through a warp → only the new map's warps show; none left over.
- A map with no warps → no blue pixels.
- Minimap hidden / opacity changed → unchanged behaviour.
- Optional: a GM tile edit on a warp tile keeps it blue.

---

## Invariant-to-test matrix

| Invariant | Proved by |
|-----------|-----------|
| Wire coordinates are converted 1→0 based | `Mwp_ConvertsOneBasedToZeroBased` (adversarial) |
| Future appended fields don't break parsing | `Mwp_IgnoresTrailingFields` |
| Warp color wins over tile art | `PixelColor_WarpBeatsTileArt` (adversarial) |
| Non-warp pixels unchanged from today | `PixelColor_NonWarpUsesTileArt`, `PixelColor_NonWarpWithoutArtIsEmpty`, existing `MinimapColorsTests` |
| Warps reset per map | Structural (`_warps` is per `MapManager` instance) + manual smoke |
| TileUpdate on a warp keeps it blue | `PixelColor` test + both handlers use it; manual smoke (optional GM edit) |
| `MWP` not shadowed by another prefix | Verified: no `M`/`MW` prefix registered |
