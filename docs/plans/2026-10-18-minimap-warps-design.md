# Minimap Warps — Design

Show warp tile locations on the minimap as light blue pixels. Only positions are revealed —
never where a warp leads. Every warp on the map is shown; there is no option toggle.

## Wire protocol

`MWP<x>,<y>` — server → client, **one packet per warp tile**.

- 1-based tile coordinates like every other tile coordinate on the wire; the client subtracts 1
  (as `TileUpdatePacket` / `SetYourPositionPacket` do).
- Position only. Fields may be appended after `y` later; the client reads `x,y` and ignores
  anything after, so a future server stays compatible.
- Sent from `DoneLoadingMapEvent` right after `P.DoneSendingMap()` — the single site that runs
  after both login and map change, once the client's new `MapManager` is live.
- No clear packet: the warp set lives on the `MapManager` instance, so each map starts empty. A
  map with no warps sends nothing.
- Pre-feature clients drop the unknown prefix; server and client ship together.

## Server (`../illutiagooseserver`)

- `Map`:
  - `public readonly record struct WarpTileEntry(int X, int Y, WarpTile Tile);`
  - `public IReadOnlyList<WarpTileEntry> GetWarpTiles()` — lazily scans `x = 1..Width`,
    `y = 1..Height` for `WarpTile`, caches the list (row order).
  - `SetTile` invalidates the cache after a valid write. `Clone` gives the copy an empty cache.
  - Game thread only; no locking.
  - Cache rationale: maps are 100×100 today but up to 1000×1000 are supported (~1M entries,
    ~1–3 ms per scan on the game thread per map entry). The first entrant pays one scan.
  - Entries hold the `WarpTile` instance (not just coordinates) so a future `Hidden` flag or new
    `MWP` fields can be read from it without changing the cache. Position is stored separately
    because a single `WarpTile` instance can be placed on several tiles (see
    `MapReachabilityTests.cs:61`).
  - Invariant: `tiles` is a public field; all runtime writes must go through `SetTile` or the
    cache goes stale. Nothing writes it directly today outside `LoadData` (pre-cache).
- `P.MapWarp(x, y)` → `"MWP" + x + "," + y`.
- `DoneLoadingMapEvent`: after `DoneSendingMap`, send `P.MapWarp(e.X, e.Y)` for each entry.
  Sent to every player including GMs/GM-invisible.

## Client

- `MapWarpPacket` (prefix `MWP`): `X = GetInt32() - 1`, `Y = GetInt32() - 1`, trailing tokens
  ignored.
- `MinimapColors.Warp` — light blue, ~`Rgb(120, 200, 255)` (distinct from `GameColors.Blue`).
  Tune in-game.
- `MinimapColors.TileColor(MapTile tile, bool isWarp, provider)` →
  `isWarp ? Warp : PickColor(tile, provider) ?? Empty`. Used by every pixel write so a warp stays
  blue under a `TileUpdate`.
- `MapManager`:
  - `HashSet<Vector2I> _warps` per instance.
  - Listens to `MapWarpPacket`; `OnMapWarp` bounds-checks, adds to `_warps`, paints via
    `TileColor`, marks dirty.
  - `OnTileUpdate` paints via `TileColor(…, _warps.Contains(p), …)` and marks dirty.
  - Per-frame flush: handlers set `_minimapDirty`; `_Process` calls `_minimapTexture.Update` and
    `Minimap.Invalidate()` once. Avoids hundreds of full-image uploads (~4 MB each at
    1000×1000) during a warp burst.

## Testing

- Server (`Goose.Tests`): `P.MapWarp` format; `GetWarpTiles` returns only warp positions;
  stale-cache regression (add a warp / overwrite a warp via `SetTile` after a cached read);
  entry `Tile` is the placed instance; clone cache independence; `DoneLoadingMapEvent` sends one
  `MWP` per warp after `DoneSendingMap`, none for a warp-less map.
- Client: `MapWarpPacket` parse (1-based → 0-based; trailing fields ignored); `TileColor` (warp
  beats tile art; non-warp falls through; no art → `Empty`).
- Manual: warps visible on login; correct after warping; none on a warp-less map; minimap
  hide/opacity unaffected; (optional) GM tile edit on a warp stays blue.

## Deferred

- Pushing warp changes made mid-session (runtime `SetTile` while players are on the map).
- Secret warps — a future `Hidden` flag on `WarpTile`, filtered at the send site.
- `MapManager` wiring and the TileUpdate-on-warp path have no unit test (no Godot node harness);
  covered by the manual smoke.

## Parts

1. Client (this repo) — `docs/plans/2026-10-18-minimap-warps-part1-client.md`
2. Server (`../illutiagooseserver`) — `docs/plans/2026-10-18-minimap-warps-part2-server.md`
