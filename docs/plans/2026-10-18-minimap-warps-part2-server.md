# Minimap Warps — Part 2: Server Implementation Plan

**Goal:** Send one `MWP<x>,<y>` packet per warp tile to a player each time they finish loading a map.

**Architecture:** `Map.GetWarpTiles()` lazily scans `tiles` for `WarpTile` and caches `(X, Y, WarpTile)` entries; `SetTile` invalidates the cache. `DoneLoadingMapEvent` — the single post-load site for login and map change — sends `P.MapWarp(x, y)` for each entry right after `DSM`.

**Tech Stack:** .NET 10 (Goose server), xUnit (`Goose.Tests`).

Design: `docs/plans/2026-10-18-minimap-warps-design.md` (client repo). This is **Part 2 of 2**; Part 1 (client) is `2026-10-18-minimap-warps-part1-client.md`.

**This part edits the sibling repo `../illutiagooseserver`, not the client worktree.** Work in a worktree there (`.worktrees/` is gitignored, `.gitignore:14`):

```bash
git -C ../illutiagooseserver worktree add .worktrees/feat-minimap-warps -b feat-minimap-warps
```

Baseline: `dotnet test Goose.Tests` in that worktree → 2121 passed (measured on `master` 721d103).

Follow the server's own conventions; AGENTS.md comment rules from the client apply in spirit (no new doc strings beyond the justified lines called out below).

---

## APIs verified

- `WarpTile` `Goose/WarpTile.cs:5-10` — properties are the **destination** only (`WarpMap`, `WarpX`, `WarpY`); it does not know its own position. One instance may be placed on many tiles (`Goose.Tests/MapReachabilityTests.cs:61`, `BuildRing(m, 17, 17, new WarpTile())`), so position must be stored beside it.
- `Map.tiles` `Goose/Map.cs:65` (`public ITile?[]`), indexed `y * Width + x` with `x ∈ 1..Width`, `y ∈ 1..Height` (`GetTile` `:682-688`, `SetTile` `:690-696`, array sized `(Width+1)*(Height+1)` `:527`).
- Field-block rule `Map.cs:62-63`: "when adding a field to Map, mirror it in CloneAs". The cache is the deliberate exception — `CloneAs` `:116-146` must **not** copy it; the clone's object initializer leaves it null, so the clone builds its own. Put the cache field outside that block with a one-line reason (justified: looks wrong otherwise).
- Warps written at load: `Map.LoadData` `:647-650` writes `this.tiles[...] = warp` directly — before any player can enter, so before any cache read. Script hooks `OnLoad`/`OnFinishedLoad` run around it. Runtime writes go through `SetTile` (only callers outside `Map.cs` are tests and `Goose.IntegrationTests/DimensionsScriptTests.cs:315`).
- Game-thread-only precedent comment: `Map.cs:457`.
- `P.DoneSendingMap` `Goose/Packets.cs:86`; `P.SendMapFlags` `:452` (place `MapWarp` after it, with a wire-format comment like the one on `SendMapFlags`).
- `DoneLoadingMapEvent.Ready` `Goose/Events/DoneLoadingMapEvent.cs:23`; `map` resolved `:27`; `world.Send(this.Player, P.DoneSendingMap());` `:43`.
- `GameWorld.Send` `Goose/GameWorld.cs:799-806` appends `"\x1"` to each packet; `Player.SendBuffer` `Goose/Player.cs:41` (`List<byte>`).
- DLM test harness precedent: `Goose.Tests/InvisibilityMapLoadTests.cs` — world/map setup `:21-38`, `NewLoadingPlayer` `:62-89`, `Buffer(p)` `:98`, `DriveMapLoad` `:100-105`.
- Map construction in unit tests: `MapReachabilityTests.NewMap` `:10-16`; clone tests `Goose.Tests/MapCloneTests.cs:7-20`.

---

### Task 1: `Map.GetWarpTiles` with cache

**Files:**
- Modify: `Goose/Map.cs` (new record struct + field + method near `GetTile`/`SetTile` `:679-696`; invalidate in `SetTile`)
- Test: `Goose.Tests/MapWarpTilesTests.cs`

```csharp
public readonly record struct WarpTileEntry(int X, int Y, WarpTile Tile);
```

(namespace `Goose`, in `Map.cs` or its own `Goose/WarpTileEntry.cs` — prefer its own file, matching one-type-per-file in `Goose/`.)

**Mutation impact:**
- Source of truth: `Map.tiles` `:65`. Derived: new `private List<WarpTileEntry>? warpTiles;`.
- Readers: `GetWarpTiles()` (Task 3 send site).
- Propagation: `SetTile` writes the tile, **then** `warpTiles = null` (only on the valid-coordinate path; the early return leaves the cache untouched). Next `GetWarpTiles()` rescans.
- Invariants:
  - `GetWarpTiles()` equals a fresh scan after any sequence of `SetTile` calls.
  - Clones never share the cache list with the source.
  - Runtime writes to `tiles` must go through `SetTile` (direct writes are only `LoadData`, pre-cache). One-line comment on the field stating this + "game thread only".
- Observable proof: the stale-cache tests below assert the returned list, not that invalidation ran.

`GetWarpTiles()` returns `IReadOnlyList<WarpTileEntry>`; scan order `y` outer `1..Height`, `x` inner `1..Width`.

**Step 1: failing tests** (map built like `MapReachabilityTests.NewMap`)

- `GetWarpTiles_ReturnsOnlyWarpPositions` — warps at (2,3) and (10,1), a `BlockedTile` at (4,4) → exactly `[(10,1), (2,3)]` in row order.
- `GetWarpTiles_IncludesFarEdgeTiles` — warp at (`Width`, `Height`) is returned (off-by-one guard on the scan bounds).
- `GetWarpTiles_EntryHoldsPlacedInstance` — `Assert.Same(placed, entry.Tile)`.
- **Adversarial:** `GetWarpTiles_SeesWarpAddedAfterCachedRead` — read (empty), `SetTile(5,5, new WarpTile())`, read → contains (5,5). Fails without invalidation.
- **Adversarial:** `GetWarpTiles_DropsWarpOverwrittenAfterCachedRead` — warp at (5,5), read, `SetTile(5,5, new BlockedTile())`, read → empty.
- `GetWarpTiles_SharedInstanceReportsEachPosition` — one `WarpTile` instance at (1,1) and (3,1) → two entries with correct coordinates.
- `CloneAs_WarpCacheIsIndependent` — read the source (populate cache), clone, `SetTile` a new warp on the clone → clone's list has it, source's does not. Fails if `CloneAs` copies the cache reference.

**Step 2 (red):** `dotnet test Goose.Tests --filter MapWarpTilesTests` → compile error (`GetWarpTiles` missing).

**Steps 3–4:** implement; green. Run `--filter MapCloneTests` too (still green).

**Step 5:** commit `feat(minimap-warps): cached warp tile list on Map`.

---

### Task 2: `P.MapWarp`

**Files:**
- Modify: `Goose/Packets.cs` (after `SendMapFlags` `:452-457`)
- Test: `Goose.Tests/MapWarpPacketTests.cs`

```csharp
// Wire format: MWP<x>,<y> — 1-based; position only, later fields may be appended (client: MapWarpPacket)
public static Func<int, int, string> MapWarp = (x, y) => { return "MWP" + x + "," + y; };
```

**Test:** `P.MapWarp(3, 7) == "MWP3,7"`. Red (compile) → green. Commit `feat(minimap-warps): MWP packet builder` (may be grouped with Task 3 if preferred).

---

### Task 3: Send warps from `DoneLoadingMapEvent`

**Files:**
- Modify: `Goose/Events/DoneLoadingMapEvent.cs:43` — directly after the `DoneSendingMap` send:
  ```csharp
  foreach (var warp in map.GetWarpTiles())
      world.Send(this.Player, P.MapWarp(warp.X, warp.Y));
  ```
  Unconditional (GMs and GM-invisible players included — positions only). Update the class header comment's "Server responds" list with one `MWP` line, since that comment enumerates the response packets and would otherwise be wrong.
- Test: `Goose.Tests/MapWarpLoadTests.cs`, copying the `InvisibilityMapLoadTests` harness (`:21-105`) — world, 20×20 map in `world.MapHandler.Maps`, `NewLoadingPlayer`, `DriveMapLoad`, `Buffer`. Split the buffer on `'\x1'` into packets for exact matching (substring `Contains` would let `MWP2,2` match `MWP2,20`).

**Step 1: failing tests**

- `MapLoad_SendsOneMwpPerWarp` — warps at (2,3) and (15,20) → packets contain exactly `MWP2,3` and `MWP15,20`, once each, and no other `MWP*`.
- **Adversarial:** `MapLoad_SendsWarpsAfterDoneSendingMap` — index of `DSM` < index of every `MWP` packet. Fails if the send is placed before `DSM` (or before `SCM` on a map change, where the client's new `MapManager` would not exist yet).
- `MapLoad_MapWithoutWarps_SendsNoMwp`.

**Step 2 (red):** `dotnet test Goose.Tests --filter MapWarpLoadTests` → first two fail (no `MWP` sent).

**Steps 3–4:** implement; green. Then full `dotnet test Goose.Tests` → 2121 + new, all green.

**Step 5:** commit `feat(minimap-warps): send warp positions on map load`.

---

### Integration / manual smoke

Run the local server from this worktree with the Part 1 client and walk the smoke list in Part 1, Task 3. The ordering risk (warps must reach a live `MapManager`) is covered by `MapLoad_SendsWarpsAfterDoneSendingMap` on the server and the client's paused packet queue during `ChangeMap` (`Scripts/GameManager.cs:297-316`, `NetworkClient.LoadingMap = true`); the smoke "walk through a warp" step confirms it end to end.

---

## Invariant-to-test matrix

| Invariant | Proved by |
|-----------|-----------|
| Only `WarpTile` positions are reported, full 1..W × 1..H range | `GetWarpTiles_ReturnsOnlyWarpPositions`, `GetWarpTiles_IncludesFarEdgeTiles` |
| Cache never goes stale via `SetTile` | `…SeesWarpAddedAfterCachedRead`, `…DropsWarpOverwrittenAfterCachedRead` (adversarial) |
| Shared `WarpTile` instances report every position | `GetWarpTiles_SharedInstanceReportsEachPosition` |
| Clone builds its own cache | `CloneAs_WarpCacheIsIndependent` (adversarial) |
| Entry carries the placed instance (future `Hidden` filter) | `GetWarpTiles_EntryHoldsPlacedInstance` |
| Wire format `MWP<x>,<y>`, 1-based | `MapWarpPacketTests` + client `Mwp_ConvertsOneBasedToZeroBased` |
| Warps sent after `DSM`, one per warp, none if no warps | `MapWarpLoadTests` |
| Destination never leaves the server | Structural: `P.MapWarp` takes only `x, y` |
| Runtime `tiles` writes go through `SetTile` | Not testable; documented on the field. Deferred: mid-session warp pushes |

## Deferred (from design)

- Pushing warp changes made while players are on the map.
- Secret warps (`Hidden` on `WarpTile`, filtered at the Task 3 send site).
