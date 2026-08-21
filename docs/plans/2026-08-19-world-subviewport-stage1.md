# World Sub-Viewport (Stage 1) Implementation Plan

**Goal:** Render the game world in a capped sub-viewport (≤1280×720, displayed at a strictly integer uniform scale relative to the window) while the GUI renders at native resolution — eliminating the current whole-window 720p stretch (pixelated text) and capping map fill-rate on large displays.

**Architecture:** The root window stops using Godot's project stretch (`window/stretch/mode="disabled"`); everything on the root viewport (login, HUD windows, tooltips) renders at native pixels. `Map.tscn`'s **root node becomes a `SubViewport`** (`handle_input_locally=false`) containing the world `Node2D` (which carries `MapManager`). A persistent `WorldViewport` node under the `GameManager` autoload holds a `TextureRect` that displays the current map's `ViewportTexture`. The sub-viewport size and the TextureRect's display rect come from a pure, xUnit-tested layout: uniform integer scale, sub-viewport capped at 1280×720 per axis, **display rect = sub·scale exactly** (integer pixels), centered in the window with ≤(scale−1)px total gutter per axis. Map entry is `SceneTree.CurrentScene = mapScene` **plus an explicit `QueueFree` of the previous scene** — assigning `CurrentScene` does not add or remove nodes (Godot `SceneTree` docs; verified on 4.7.1). Mouse world-clicks are converted window→world at the root using the display origin/scale and the sub-viewport canvas transform's **inverse**, dispatched to `MapManager`. In-world text (names, chat bubbles, battle text) stays inside the sub-viewport for this stage — soft at 2×, by design; Stage 2 fixes that.

**Tech Stack:** Godot 4.7.1 (app, `Godot.NET.Sdk/4.7.1`), xUnit. The test project (`tests/Goose2Client.Tests`) references **GodotSharp 4.6.2** and compiles all of `Scripts/**` into the test assembly (`Goose2Client.Tests.csproj:9,17`) — new `Scripts/` code must compile against both. **Verified test command** (runs on a fresh clone; do not use `--no-restore`/`--no-build` without a prior successful `dotnet build`): `dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj --filter <Name> -v minimal` (measured: 27 existing tests, ~0.8s built+run). Engine behavior is verified manually / with a headless script (the test project has no Godot runtime).

**Stages (this is Stage 1):**
1. **This plan** — sub-viewport, integer scaling, input path, window placement, 1×/2× render-mode option. World text unchanged (rendered in-world).
2. *(To be planned later)* `WorldTextBridge`: project in-world text (names, chat bubbles, battle text) onto a native-resolution `CanvasLayer` for crisp text everywhere (Unity SDF parity).
3. *(To be planned later, possibly skipped)* UI scale knob so HUD windows grow proportionally on large windows (Godot equivalent of Unity's Canvas Scaler). Positions are already handled in Stage 1 (edge-stick + clamp, `WindowPlacement`).

**Execution:** dedicated worktree off main (via @using-git-worktrees); tasks run sequentially as written — each compiles standalone and lands as its own commit. Recommended: @subagent-driven-development (fresh subagent per task, spec-compliance then code-quality review after each). Task 6 requires a display and a game server — run manually (or in a headed session).

**Window placement rule (decided):** window *positions* are stored in absolute coordinates relative to the canvas they were saved on (`DefaultWindowLayout.cs:11-18`, `BaseWindow.cs:47-50`, `CharacterSettings.cs:172-187`); with native rendering, at 1080p the Hotbar would float ~365px above the bottom edge. Rule: **edge-stick + clamp, no scaling, canvas tracked per save.** On restore, each axis is evaluated in the *saved* canvas space: if the window is strictly closer to one edge than the opposite, it re-anchors to that edge with the same offset at the *current* window size (Hotbar: 5px above bottom at 720p → 5px above bottom at 1080p; a position saved at 1080p round-trips exactly). A window equidistant (parked mid-screen) keeps its saved coordinates. Final clamp keeps the window's **title bar** (24px, `Scenes/UI/BaseWindow.tscn:24`) inside the window — all current BaseWindow scenes fit within the 640×360 min window (largest: BuffEffectsWindow at 638px wide), so full containment currently holds; title-bar-only for y is a deliberate future-proofing policy. `WindowSettings` gains a `CanvasSize` field; legacy entries (absent/zero) mean 1280×720. Pure, xUnit-tested helper `WindowPlacement`.

---

## APIs verified

| Item | Citation |
|---|---|
| Project stretch (whole app renders at fixed 1280×720, scaled) | `project.godot:25-27` (`viewport_width=1280`, `stretch/mode="viewport"`) |
| `UiLayer` CanvasLayer created in autoload `_Ready` | `Scripts/GameManager.cs:64-67` |
| `ChangeMap` flow (pause → loading frame → map → DoneLoadingMap → drain) | `Scripts/GameManager.cs:110-136`; scene swaps at :125,:133 |
| `EnsureHud` instantiates `GameHud.tscn` under `UiLayer` | `Scripts/GameManager.cs:192-197` |
| Map scene root is the `MapManager` node ("Map") | `Scenes/Map.tscn` (`[node name="Map" type="Node2D"] script = MapManager.cs`) |
| MapManager `_Ready` / relative `GetNode` paths / register+clear `CurrentMapManager` | `Scripts/MapManager.cs:40,44-46,92,99-100` |
| World mouse clicks: `_UnhandledInput` + `GetGlobalMousePosition()` | `Scripts/MapManager.cs:228-249` (pos read at :234) |
| Camera follow in `_Process` | `Scripts/MapManager.cs:219-224` |
| Tile size = 32 px | `Scripts/Map/MapCoords.cs:10` |
| **Typing-in-chat gate uses `GetViewport().GuiGetFocusOwner()` from world nodes** (breaks in a sub-viewport) | `Scripts/Character/Character.cs:506,563` |
| LoadingMap is a full-rect transparent Control (paints nothing; coverage only via empty/black world area) | `Scenes/LoadingMap.tscn` (root Control, `anchors_preset = 15`) |
| Login UI is anchor-centered (adapts to native res) | `Scenes/Login.tscn:7-21` |
| Spell reticle is a Node2D parented to the target character (in-world) | `Scripts/SpellTargetManager.cs:130-143` |
| World text = Node2D labels in-world (Stage 1: stays soft) | `Scripts/Character/Character.cs:97-100`, `Scripts/Overlays/ChatBubble.cs`, `Scripts/Overlays/BattleText.cs` |
| In-world Controls (bubble `Panel`, HP/MP `ColorRect`) default `MouseFilter.Stop` — currently swallow root clicks | `Scripts/Character/Character.cs:44-62`, `Scripts/Overlays/ChatBubble.cs` |
| Window positions: restore `BaseWindow.cs:47-50`; **single save path** `SetWindowSetting` overloads `Scripts/CharacterSettings.cs:172,180` (all 4 visible-variant call sites in `BaseWindow.cs:94,103,124,131`); `WindowSettings { Vector2 Position; bool Visible; }` `Scripts/CharacterSettings.cs:29-33`; JSON `IncludeFields=true` (missing field → default) `Scripts/CharacterSettings.cs:40-42` |
| Window defaults are absolute 720p coordinates; only ChatWindow/Toolbar anchored; title bar height 24px | `Scripts/UI/DefaultWindowLayout.cs:11-18`, `Scenes/UI/ChatWindow.tscn:12-14`, `Scenes/UI/Toolbar.tscn:13`, `Scenes/UI/BaseWindow.tscn:24` |
| Options persisted in dynamic JSON dict; typed accessor | `Scripts/CharacterSettings.cs:46,189`; keys in `Scripts/Constants.cs:136+`; write pattern `OptionsWindow.cs:31-33` |
| `GameHud` root full-rect `MouseFilter.Ignore`; `WorldDropTarget` full-rect `Pass` — world clicks reach unhandled input | `Scenes/UI/GameHud.tscn:11`, `Scripts/UI/GameHud.cs:38-44` |
| **`SceneTree.CurrentScene` assignment does not add or remove nodes** (old scene must be freed explicitly) | Godot `SceneTree` docs (4.4+); verified on 4.7.1: previous scene still valid, in tree, parented after reassignment |
| Empirical (headless 4.6.2): cross-viewport `add_child` of a parented node is refused (`node.cpp:1705`); `Viewport.GetTexture()` returns a live `ViewportTexture` | repro in `SceneTree._init` (review notes) |
| GodotSharp 4.7.1 method metadata: **no `GetViewportSize`**; `Viewport.GetVisibleRect()`, `CanvasItem.GetViewportRect()`, `Viewport.GuiGetFocusOwner()` exist | `strings GodotSharp.dll` (nuget `godotsharp/4.7.1`), this review |
| Test project: GodotSharp 4.6.2, compiles `Scripts/**`, 27 tests pass via `dotnet test` | `tests/Goose2Client.Tests/Goose2Client.Tests.csproj:9,17`; measured run |

Canonical root size (use everywhere; `GetViewportSize()` does not exist in GodotSharp):

```csharp
static Vector2I RootSize(Node n) => (Vector2I)n.GetTree().Root.GetVisibleRect().Size;
```

Engine APIs: `Viewport.Size`, `Viewport.GetCanvasTransform()` (world→viewport; use `.AffineInverse()` for viewport→world), `Viewport.GetTexture()`, `SubViewport` (`handle_input_locally`), `Window.SizeChanged`, `Control.MouseFilterEnum.Ignore`, `TextureRect` free placement (Position/Size), `SceneTree.CurrentScene`.

---

## Design invariants (what "correct" means)

- **I1 — Strictly integer uniform display scale:** inside the display rect, on-screen scale is exactly `scale` in both axes (`DisplaySize == SubViewportSize × scale`, integer pixels). No sub-pixel stretching of the world, ever.
- **I2 — Fill-rate cap:** in 2× mode `SubViewportSize.X ≤ 1280` and `.Y ≤ 720`; 1× mode is opt-in and uncapped by definition.
- **I3 — Bounded gutters:** per axis `gutter = window − DisplaySize`, `0 ≤ gutter < scale` (≤2px total through 4K; the Task 1 test range reaches scale 5, where gutters can be 4px), split centered; rendered as root background (black) — the price of strict I1 at non-exact window sizes.
- **I4 — GUI at native:** HUD windows, tooltips, login render in the root viewport at window pixels; text crisp at any resolution.
- **I5 — World text unchanged (Stage 1):** names/bubbles/battle text/reticle stay in-world; soft at 2×, nearest-upscaled with the map.
- **I6 — Input:** world clicks land on the exact tile under the cursor (same tile → same server call as today); input conversion uses the same display origin/scale as the TextureRect. **Gutter clicks are rejected, never dispatched** (with the camera centered in a large map, a gutter offset can map to a *valid* tile at the camera-view edge — passing them through would click the wrong world position). Clicking on an in-world Control (bubble panel, HP bar) becomes a world click post-change (sub-viewport GUI inert) — accepted as a fix, covered by Task 6.6.
- **I7 — Scene lifecycle:** every map transition leaves exactly one live map scene, one live `MapManager`, and no orphaned `PacketManager` listeners; the Login scene is freed on first entry.

**Scale formula (2× mode):** `scale = max(2, ceil(ww/1280), ceil(wh/720))`; `sub = floor(window / scale)`; `display = sub × scale`; `origin = (window − display) / 2` (truncated).
Checkpoints: 1920×1080→2× 960×540 · 2560×1440→2× 1280×720 · 3840×2160→3× 1280×720 · 1280×720 window→2× 640×360 · 3440×1440→3× 1146×480 (display 3438×1440, origin (1,0)).
**1× mode:** `scale = 1`, `sub = display = window`, `origin = (0,0)` (1080p → 32 px/tile, 60×33.75 tiles visible).

---

## Task 1: Pure layout calculation + xUnit tests

**Files:**
- Create: `Scripts/WorldViewportScale.cs`
- Test: `tests/Goose2Client.Tests/WorldViewportScaleTests.cs`

**Step 1: Write the failing tests**

```csharp
public readonly record struct WorldViewportLayout(int Scale, Vector2I SubViewportSize, Vector2I DisplayOrigin, Vector2I DisplaySize);
public enum WorldRenderMode { Integer2x, Native1x }
public static class WorldViewportScale
{
    public static readonly Vector2I Cap = new(1280, 720);
    /// Precondition: windowSize ≥ (2,2) per axis, else ArgumentOutOfRangeException.
    /// Invariants: DisplaySize == SubViewportSize * Scale (exact);
    ///   0 ≤ window − DisplaySize < Scale per axis; SubViewportSize ≤ Cap (Integer2x only).
    public static WorldViewportLayout Compute(WorldRenderMode mode, Vector2I windowSize);

    /// True if windowPos (root-window integer space) is inside the display rect
    /// (origin inclusive, origin+DisplaySize exclusive). Gutter clicks must use this to reject.
    public static bool IsInsideDisplay(WorldViewportLayout layout, Vector2I windowPos);
}
```

Test cases (assert the full 4-tuple):

| Window | Mode | Expected (Scale, Sub, Origin, Display) |
|---|---|---|
| 1920×1080 | Integer2x | (2, 960×540, (0,0), 1920×1080) |
| 2560×1440 | Integer2x | (2, 1280×720, (0,0), 2560×1440) |
| 3840×2160 | Integer2x | (3, 1280×720, (0,0), 3840×2160) |
| 1280×720 | Integer2x | (2, 640×360, (0,0), 1280×720) |
| 1600×900 | Integer2x | (2, 800×450, (0,0), 1600×900) |
| 3440×1440 | Integer2x | (3, 1146×480, (1,0), 3438×1440) |
| 1921×1081 | Integer2x | (2, 960×540, (0,0), 1920×1080) |
| 3050×305 | Integer2x | (3, 1016×101, (1,1), 3048×303) |
| 1920×1080 | Native1x | (1, 1920×1080, (0,0), 1920×1080) |
| (1,720), (1280,1), (0,720) | either | throws ArgumentOutOfRangeException |

`IsInsideDisplay` tests (gutter rejection, four edges):
- Layout (3, 1146×480, (1,0), 3438×1440): `(0, 720)` → false (left gutter), `(1, 720)` → true, `(3439, 720)` → false (right gutter), `(3438, 720)` → true.
- Layout (2, 960×540, (0,0), 1920×1080): `(1920, 540)` → false (right), `(960, 1080)` → false (bottom), `(0, 0)` → true, `(1919, 1079)` → true.
- Layout for window 800×2163 → (4, 200×540, (0,1), 800×2160): `(400, 0)` → false (top gutter), `(400, 1)` → true.

Property tests (loop 320×200 … 5120×3200, step 7):
- **I1+I3 (adversarial — the floor-then-stretch design fails this):** `DisplaySize.X == SubViewportSize.X * Scale` exactly (uniform integer display scale), and `0 ≤ window − DisplaySize < Scale` per axis, `SubViewportSize ≥ (1,1)`. The 3050×305 row specifically fails the old "stretch to full window" design (display scales 3.00197×/3.01980× — non-integer, non-uniform).
- **I2 (adversarial):** Integer2x ⇒ `Scale ≥ 2` AND `SubViewportSize.X ≤ 1280` AND `.Y ≤ 720` for every size — fails on a divide-by-2-only implementation (4K → 1920×1080).

**Step 2: Red.** `dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj --filter WorldViewportScale -v minimal` → FAIL (type does not exist).

**Step 3: Implement** `Compute` per the formula.

**Step 4: Green.** Same command → PASS.

**Step 5: Commit.** `feat: add integer world-subviewport layout calculation`

---

## Task 2: `WorldViewport` node + disable project stretch + window placement

**Mutation impact (project stretch change):**
- Source of truth changed: `project.godot:25-27`.
- Important readers: every root-viewport Control — coordinate space changes from fixed 1280×720 to native window pixels. Login (anchor-centered, `Scenes/Login.tscn:7-21`) adapts. Tooltip mouse tracking (`ItemTooltipControl.cs:81`, `MapItemTooltipControl.cs:71`, `TextTooltipControl.cs:38`, `SpellTooltipControl.cs:65`) and hover (`BaseWindow.cs:112`) are root-side and track the mouse in whatever space the root uses — no code change.
- **Window positions:** stored absolute relative to the canvas they were saved on (`BaseWindow.cs:47-50`, `CharacterSettings.cs:172-187`); only ChatWindow/Toolbar are anchored. **Strategy (decided): edge-stick + clamp, canvas tracked per save, no file migration.** `WindowSettings` gains `Vector2I CanvasSize` (`CharacterSettings.cs:29-33`); legacy JSON lacks the field → deserializes `(0,0)` → treated as 1280×720 (`CharacterSettings.cs:40-42` `IncludeFields=true`; the `(0,0) → LegacyCanvas` mapping lives in `BaseWindow` and is covered by the JSON tests below). Saves record the canvas they were made on, so a 1080p-saved Hotbar (y=1039, 5px bottom offset) round-trips exactly at 1080p and re-anchors correctly at 720p (y=679).
- Required propagation: none beyond the single restore path (`BaseWindow.cs:47-50`) and single save path (`SetWindowSetting`, `CharacterSettings.cs:180`); `WindowPlacement.Resolve` is pure.
- Invariants to preserve:
  - Login centered at 720p/1080p/1440p (anchors).
  - **720p identity:** with a legacy (1280×720) save or a save made at 1280×720, every window opens at exactly today's pixel position (rule is identity when saved canvas == current canvas — xUnit-tested).
  - **Cross-canvas round-trip:** edge offset to the re-anchored edge is preserved when moving between canvas sizes (the 1039↔679 Hotbar case, xUnit-tested).
  - Containment: result keeps the window's **title bar** (24px) inside the canvas per axis (`x ∈ [0, canvas.X − w.X]`, `y ∈ [0, max(0, canvas.Y − 24)]`) — full containment currently holds (no BaseWindow scene exceeds 640×360; largest is BuffEffectsWindow at 638px wide); title-bar-only y is a deliberate future-proofing policy.
- Observable proof: Task 2 Step 4 tests + Task 6.5.

**Files:**
- Modify: `project.godot:25-27` + `[display]` (min size)
- Create: `Scripts/WorldViewport.cs`, `Scripts/UI/WindowPlacement.cs`
- Create: `tests/Goose2Client.Tests/WindowPlacementTests.cs`
- Modify: `Scripts/CharacterSettings.cs:29-33` (field), `:180` (overload param)
- Modify: `Scripts/Constants.cs:136+` (`Options.RenderMode` key — `true` = Native1x)
- Modify: `Scripts/GameManager.cs:64-67` (`_Ready`)
- Modify: `Scripts/UI/BaseWindow.cs:47-50` (restore) + `:94,103,124,131` (save call sites)
- Modify: `tests/Goose2Client.Tests/CharacterSettingsJsonTests.cs` (CanvasSize round-trip + legacy default)

**Step 1:** `project.godot`: `window/stretch/mode="disabled"`; add `window/size/min_width=640`, `window/size/min_height=360`.

**Step 2:** `Scripts/WorldViewport.cs` — a `Node` that owns:
- `TextureRect WorldTexture` — **free placement** (no full-rect anchors), `MouseFilter = MouseFilterEnum.Ignore`, `Texture` set by `Attach`. On every layout apply: `Position = layout.DisplayOrigin; Size = layout.DisplaySize;` — the TextureRect's default stretch fills its own (integer-sized) rect, so the on-screen scale is exactly `layout.Scale` (I1). Gutters show the root background (black), ≤ scale−1 px total per axis (I3).
- `public SubViewport Current { get; private set; }` — the attached map scene.
- `public WorldViewportLayout Layout { get; private set; }`
- `public void Attach(SubViewport mapScene)` — `Current = mapScene; AddChild(mapScene); WorldTexture.Texture = mapScene.GetTexture(); RefreshFromSettings();` — single mode-application point; the map scene never applies its own mode (no re-entrancy through `MapManager._Ready`). Call order in the entry sequence guarantees the texture is assigned *before* the previous scene is freed.
- `public WorldRenderMode Mode { get; private set; } = WorldRenderMode.Integer2x;`
- `public void ApplyMode(WorldRenderMode mode)` — stores mode; if `Current != null`: `Layout = WorldViewportScale.Compute(mode, (Vector2I)GetTree().Root.GetVisibleRect().Size); Current.Size = Layout.SubViewportSize;` + set `WorldTexture` Position/Size.
- `public void RefreshFromSettings()` — `ApplyMode(GameManager.Instance.CharacterSettings?.GetOption<bool>(Options.RenderMode, false) == true ? Native1x : Integer2x)` (null-safe: pre-login → Integer2x, the node default). Used only by `Attach`.
- `public Vector2 WindowToWorld(Vector2 windowPos)` — `var vp = (windowPos - Layout.DisplayOrigin) / (float)Layout.Scale; return Current.GetCanvasTransform().AffineInverse() * vp;` (`.AffineInverse()` required: `GetCanvasTransform()` maps world→viewport; using it forward displaces clicks by ~2× the camera offset).
- Resize: `GetWindow().SizeChanged` (connected in `_Ready`) → `ApplyMode(Mode)`. Guard: ignore if root size is empty.

Helper contracts:
- `WindowToWorld` — preconditions: `Current` attached with active camera; `Layout` current. Postcondition: result in world (map) pixels, the space `MapCoords.WorldToTile` consumes. Pure.
- `ApplyMode` — sole mutator of `Current.Size` / `Layout` / `WorldTexture` rect. No-op (mode stored) when no map attached.

**Step 3:** `GameManager._Ready` — create and add `WorldViewport` **before** `UiLayer` (tree order: Login/LoadingMap root Controls must draw above the world texture):

```csharp
WorldViewport = new WorldViewport();
AddChild(WorldViewport);        // must precede UiLayer add
UiLayer = new CanvasLayer();    // existing code, unchanged
```

Expose `public WorldViewport WorldViewport { get; private set; }`.

**Step 4:** Window placement (pure helper, TDD red→green first).

`Scripts/UI/WindowPlacement.cs`:

```csharp
/// Placement of a window saved in `savedCanvas` design coordinates, at `currentCanvas`.
/// Pure. Title bar height for y-containment is 24 (Scenes/UI/BaseWindow.tscn:24).
public static class WindowPlacement
{
    public static readonly Vector2I LegacyCanvas = new(1280, 720);
    public const int TitleBarHeight = 24;

    /// Edge-stick + clamp (see plan header). Identity when savedCanvas == currentCanvas.
    /// Postcondition: x ∈ [0, canvas.X − w.X]; y ∈ [0, max(0, canvas.Y − 24)].
    public static Vector2 Resolve(Vector2 savedPos, Vector2 windowSize, Vector2I savedCanvas, Vector2I currentCanvas);
}
```

Rule, per axis (x shown; y symmetric with the 24px title-bar clamp): offsets in **savedCanvas** space — `left = savedPos.X`, `right = savedCanvas.X − (savedPos.X + windowSize.X)`; if `left < right` → `x = left`; else if `right < left` → `x = currentCanvas.X − windowSize.X − right`; else (equidistant/mid-screen) → `x = savedPos.X`. Then `x = clamp(x, 0, currentCanvas.X − windowSize.X)`.

Tests — `tests/Goose2Client.Tests/WindowPlacementTests.cs`:
- **Identity:** for savedCanvas == currentCanvas == (1280,720), `Resolve(p, s, C, C) == clamp(p)` for a set of positions — the 720p regression proof.
- **Cross-canvas round-trip (adversarial — the no-CanvasSize design fails this):** Hotbar `(520, 679, s=(36,36))`, saved at (1280,720), restore at (1920,1080) → `(520, 1039)`; then that **saved at (1920,1080)** again restored at (1920,1080) → `(520, 1039)` exactly (not 1044); restored at (1280,720) → `(520, 679)`.
- **Right-edge stick:** `(900, 360, s=(340,420))` saved at (1280,720) → at (1920,1080) `x = 1920−340−40 = 1540`.
- **Mid-screen stays put:** equidistant window keeps its saved coordinate at (1920,1080) (no edge-jump).
- **Clamp/title-bar:** saved `(1100, 600)` for a *synthetic* window (300×500) taller than the canvas, at canvas (640,360) → `x ≥ 0`, `y ≤ 360−24 = 336`. (No current window is this large — the case exists to pin the clamp path for future windows.)

Also extend `tests/Goose2Client.Tests/CharacterSettingsJsonTests.cs`:
- **Round-trip:** a `WindowSettings` with `CanvasSize = (1920, 1080)` survives serialize → `FromJson` → deserialize with position and visible intact.
- **Legacy (adversarial — the no-CanvasSize design fails this):** JSON built *without* the `CanvasSize` field (hand-written string, mirroring pre-change user files) deserializes to `CanvasSize == (0,0)` — which `BaseWindow` maps to `WindowPlacement.LegacyCanvas` (1280×720); combined with the `Resolve` identity test this proves old settings files place windows exactly as before.

Then wire up:
- `CharacterSettings.cs:29-33`: `WindowSettings` += `public Vector2I CanvasSize;` (legacy → `(0,0)`).
- `CharacterSettings.cs:180`: `SetWindowSetting(string, Vector2?, bool)` += required `Vector2I canvas` param; store `settings.CanvasSize = canvas;` (the `:172` no-visible overload is untouched).
- `BaseWindow.cs:47-50` restore: `var canvas = ws != null && ws.CanvasSize != default ? ws.CanvasSize : WindowPlacement.LegacyCanvas; Position = WindowPlacement.Resolve(storedOrDefaultPos, Size, canvas, (Vector2I)GetTree().Root.GetVisibleRect().Size);` (defaults from `DefaultWindowLayout` are 1280×720 design coords → same `LegacyCanvas`).
- `BaseWindow.cs:94,103,124,131`: pass `(Vector2I)GetTree().Root.GetVisibleRect().Size` to `SetWindowSetting`.

**Step 5:** Manual smoke (`./run.sh`): login centered, text crisp at 1280×720 **and** 1920×1080 (I4 before/after proof); at 720p every HUD window opens at exactly today's position (legacy identity). World area black (no map yet) — expected.

**Step 6: Commit.** `feat: render world in capped sub-viewport, root GUI at native resolution`

---

## Task 3: Map scene root becomes the sub-viewport + explicit scene lifecycle

**Why this shape:** cross-viewport `add_child` of a parented node is refused by Godot (verified headless: `node.cpp:1705`), and reparenting workarounds corrupt `current_scene` handling. Making the map scene *itself* a `SubViewport` removes all reparenting. And because **`CurrentScene = x` does not free the previous scene** (docs + verified on 4.7.1), the transition frees it explicitly — otherwise Login (first entry) and old maps (later entries) stay alive: ghost render, duplicate packet handling, leaked `PacketManager` listeners (`MapManager.cs:95-113`).

**Files:**
- Modify: `Scenes/Map.tscn` (re-root)
- Modify: `Scripts/GameManager.cs:110-136` (`ChangeMap`)
- Create: `tools/tests/scene_lifecycle.gd` (headless check)

(`MapManager` and `WorldViewport` are **not** modified by this task — mode application lives in `Attach` → `RefreshFromSettings`, Task 2.)

**Step 1: `Scenes/Map.tscn` re-root** (edit in the Godot editor to keep resources/uids intact):

```
Map (SubViewport)                  ← was Node2D "Map"
├── handle_input_locally = false, transparent_bg = false
└── World (Node2D, script = MapManager.cs, y_sort_enabled = true)   ← was the old root
    ├── Camera2D
    ├── Layers
    ├── Objects
    └── Characters
```

`MapManager` is `Node2D` (`Scripts/MapManager.cs:11`) so it moves to the inner node; relative `GetNode` paths (`:44-46`) and Y-sort unchanged.

**Step 2 (no code — ownership note):** mode application at map entry happens inside `WorldViewport.Attach` (`RefreshFromSettings`, Task 2) — `MapManager._Ready` stays unchanged. This keeps a single mode-application point instead of the re-entrant double-apply (Ready-during-AddChild + Attach).

**Step 3: `ChangeMap` rework** (`Scripts/GameManager.cs:110-136`):

```csharp
SetPaused(true);
var previousScene = GetTree().CurrentScene;          // Login (first entry) or old map
var loading = GD.Load<PackedScene>("res://Scenes/LoadingMap.tscn").Instantiate<LoadingMapScene>();
GetTree().Root.AddChild(loading);                    // NOT a current scene — freed manually
try
{
    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    loading.SetMapName(mapName);

    CurrentMap = LoadMap(mapFile);
    var mapScene = GD.Load<PackedScene>("res://Scenes/Map.tscn").Instantiate<SubViewport>();
    WorldViewport.Attach(mapScene);                  // tree + texture + size, BEFORE previous is freed
    GetTree().CurrentScene = mapScene;               // does NOT free previousScene (verified 4.7.1)
    if (previousScene != null && previousScene != mapScene
        && GodotObject.IsInstanceValid(previousScene))
        previousScene.QueueFree();                   // I7: explicit lifecycle ownership
    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    NetworkClient.DoneLoadingMap();
}
finally
{
    if (GodotObject.IsInstanceValid(loading)) loading.QueueFree();   // no leaked full-window Control
    SetPaused(false);   // always drain queued gameplay packets, even on failure
}
```

Sequencing invariants:
- `Attach` (texture swap) before `previousScene.QueueFree()` → no dangling `ViewportTexture`, no black flash.
- `Attach` → `RefreshFromSettings` sizes the viewport, and `MapManager._Ready` (fires inside `Attach`'s `AddChild`) registers `CurrentMapManager` (`MapManager.cs:92`) — both complete before `DoneLoadingMap`, so drained packets reach a ready map.
- Failure behavior: any throw → `finally` frees `loading` and drains; `previousScene` is only freed after the map is attached, so a failed entry keeps the old map live and usable (better than today's half-swap).

**Step 4: Headless lifecycle check** — `tools/tests/scene_lifecycle.gd` (plain `SceneTree` script, run: `godot --headless --path . -s tools/tests/scene_lifecycle.gd`): simulates the exact `ChangeMap` ordering with stand-in scenes (previous scene A as current; B instantiated, added under a `SubViewport` container, `CurrentScene = B`, `A.QueueFree()`; advance one frame) and asserts: A freed, B alive and inside the container, no second scene child at root. This guards the I7 assumption (which the docs-only reading got wrong once already) without a display.

**Step 5:** Manual smoke (`./run.sh`, log in, enter a map): map visible, camera follows; at 1080p tiles exactly 64px (I1); in-world names/bubbles/reticle render (soft — I5). **Adversarial:** trigger a map change (door/warp/re-login): old map fully gone — no ghost tiles, no duplicated characters (the leaked-listener symptom), no black flash; first entry frees Login (no lingering login input focus after map load).

**Step 6: Commit.** `feat: render map scene in its own sub-viewport root with explicit lifecycle`

---

## Task 4: World mouse-click input path

**Why:** with `handle_input_locally=false`, sub-viewport nodes never receive window input; `MapManager._UnhandledInput` would go dead. Clicks are handled at the root and converted explicitly.

**Files:**
- Modify: `Scripts/MapManager.cs:228-249`
- Modify: `Scripts/WorldViewport.cs`
- Modify: `Scripts/Character/Character.cs:506,563`

**Mutation impact:**
- Source of truth: mouse→world mapping. Old: `GetGlobalMousePosition()` inside the map's own viewport (`MapManager.cs:234`). New: explicit inverse-canvas conversion at the root using the display origin/scale, dispatched to `MapManager`.
- Readers: `NetworkClient.LeftClick/RightClick` — server-bound tile coordinates; wrong tiles are immediately visible to all clients.
- Invariants: exact tile under cursor (I6); **gutter clicks never dispatch** (a gutter offset can land on a valid tile of a large map — see I6); HUD windows swallow their own clicks (GUI precedes `_UnhandledInput` — verified: `GameHud` Ignore + `WorldDropTarget` Pass); targeting suppresses world clicks; **typing in chat still suppresses movement/attack** (B5: the gate at `Character.cs:506,563` must query the root viewport — world nodes' `GetViewport()` is now the sub-viewport, whose GUI focus owner is always null; unfixed, "wasd" typed in chat moves the character).
- Observable proof: Task 4 Step 4 debug output + `WorldViewportScaleTests.IsInsideDisplay` (pure, four edges) + Task 6.6.

**Step 1:** `MapManager` — replace the `_UnhandledInput` override with:

```csharp
/// World-space click. `worldPos` is in map pixels (see WorldViewport.WindowToWorld).
public void HandleWorldClick(MouseButton button, Vector2 worldPos)
{
    if (GameManager.Instance.IsTargeting) return;
    // ... existing body verbatim: `mb` → `button`, `mouseWorld` → `worldPos`,
    //     GetGlobalMousePosition() (:234) removed.
}
```

**Step 2:** `Character.cs:506` and `:563` — `GetViewport().GuiGetFocusOwner()` → `GetTree().Root.GuiGetFocusOwner()` (chat `LineEdit` focus lives on the root; note the C# name is `GuiGetFocusOwner`, not `GetGuiFocusOwner`). Comment why root is explicit.

**Step 3:** `WorldViewport` — add:

```csharp
public override void _UnhandledInput(InputEvent e)
{
    if (e is not InputEventMouseButton mb || !mb.Pressed) return;
    if (mb.ButtonIndex != MouseButton.Left && mb.ButtonIndex != MouseButton.Right) return;
    if (Current == null) return;
    if (!WorldViewportScale.IsInsideDisplay(Layout, (Vector2I)mb.Position)) return;   // gutters are not the world
    var mm = GameManager.Instance.CurrentMapManager;
    if (mm == null || !GodotObject.IsInstanceValid(mm)) return;
    mm.HandleWorldClick(mb.ButtonIndex, WindowToWorld(mb.Position));
}
```

`mb.Position` is root-window coordinates (root viewport 1:1 with window; monitor origin doesn't enter event positions). The display-rect gate is mandatory, not cosmetic: with the camera centered inside a large map, a click 1px into a gutter converts to a *valid* tile at the camera-view edge and would otherwise be sent to the server.

**Step 4 (red→green, manual):** temporary `GD.Print("worldclick", mb.ButtonIndex, worldPos, (tx, ty))` in `HandleWorldClick`. At 1920×1080: stand on a known tile, click the tile directly left — printed tile must be exact (an inverted/forward transform or missing display-origin fails by ~2× the camera offset — the adversarial check). Then: click over the chat window → no world click; click on a chat-bubble panel → DOES produce a world click (I6, accepted); at a 1921×1081 window, click with the cursor in the 1px right/bottom gutter (x=1920 or y=1080) → **no** world click (gutter rejection); type "wasd" in chat → character does not move. Remove the print before committing.

**Step 5: Commit.** `feat: convert window mouse to world coordinates for map clicks`

---

## Task 5: 1×/2× render-mode option (UI only)

`Options.RenderMode` and `WorldViewport.RefreshFromSettings` already exist from Task 2.

**Files:**
- Modify: `Scenes/UI/OptionsWindow.tscn` + `Scripts/UI/OptionsWindow.cs`

**Step 1:** Scene changes (exact): root `OptionsWindow` `offset_bottom` 108 → **112**; new child of `Content`:

```
[node name="NativeRenderCheck" type="CheckBox" parent="Content"]
layout_mode = 2
anchors_preset = 0
offset_left = 8.0
offset_top = 84.0
offset_right = 232.0
offset_bottom = 108.0
mouse_filter = 1
text = "Native 1× rendering"
```

(mirrors the existing checks at y 28–52 and 56–80 with the same 24px row pitch; +4px window height keeps the bottom margin). Script: read via `GetOption<bool>(Options.RenderMode, false)` in `_Ready` into `_nativeRender := GetNode<CheckBox>("Content/NativeRenderCheck")`; on `Toggled`: `CharacterSettings.Options[Options.RenderMode] = pressed; CharacterSettings.Save(); GameManager.Instance.WorldViewport.ApplyMode(pressed ? Native1x : Integer2x);` (mirrors `OptionsWindow.cs:22-33`).

**Step 2 (manual red→green):** 1080p, 2×: toggle 1× → tiles immediately 64px→32px, view widens to 60×33.75 tiles; toggle back. Restart → persists (JSON gains the key). Enter a second map → mode still applied via `RefreshFromSettings`. Adversarial: toggle mid-chat-bubble/battle-text — world rescales, nothing crashes or mis-projects.

**Step 3: Commit.** `feat: add 1x/2x world render mode option`

---

## Task 6: Full manual verification (no commit unless fixes needed)

Run `./run.sh`. Checklist:

1. **Login/loading (I4):** centered, crisp text at 1280×720, 1600×900, 1920×1080. Loading screen shows over the world area.
2. **World scale (I1/I2/I3):** at 1080p **and** 1440p: 2×, tile = exactly 64 screen px (30 tiles across at 1080p, 40 at 1440p — screenshot measurement). At 4K (3840×2160): 3×, 96 px/tile, 40 across. At a 3440×1440 ultrawide: 3× (ceil(3440/1280)=3), 96px tiles, 1px gutters left **and** right. Odd-sized window (1921×1081): 1px gutter right/bottom, no distortion. Pan the camera: pixel *scale* is stable (no size flicker); smooth sub-pixel scroll from the camera lerp (`MapManager.cs:219-224`) is expected, not shimmer. Confirm the `ViewportTexture` upscale is nearest (`default_texture_filter=0` applies to canvas textures — bilinear would soften the 2×).
3. **Default window (acceptance, explicit):** at 1280×720 the world renders at 640×360 in 2× mode — a visible fidelity drop vs today at the default size; 1× restores it. Sign off as the I2 trade.
4. **Resize (I1/I3):** drag between 1600×900 ↔ 1920×1080 ↔ 1366×768 in-game: immediate rescale, gutters stay ≤ scale−1 px (≤2px at these sizes) and centered, no crash, no persistent distortion, HUD intact.
5. **HUD (I4 + edge-stick):** at **720p** every window opens at exactly today's positions (identity proof); at 1080p the Hotbar sits 5px above the bottom edge, Inventory at its right-edge offset; a window parked mid-screen at 720p keeps its saved coordinates at 1080p (no edge-jump); drag a window at 1080p, restart at 1080p → position identical (canvas-tracked round-trip), restart at 720p → same edge offset; title bar reachable at 640×360 min window. Tooltips track the mouse 1:1 at both resolutions with correct native-size clamping.
6. **Input (I6):** exact-tile click (Task 4.4); gutter click at an odd-size window sends nothing (pure proof in `WorldViewportScaleTests.IsInsideDisplay`); character click selects the clicked character; right-click sends; HUD clicks never leak to the world; chat-bubble click now reaches the world (accepted); typing in chat does not move/attack; spell targeting suppresses world clicks, reticle world-locked.
7. **Scene lifecycle (I7):** second map entry — old map fully gone (no ghost tiles, no duplicated characters), no black flash; Login freed after first entry; run `godot --headless --path . -s tools/tests/scene_lifecycle.gd` → PASS.
8. **World text (I5):** names, chat bubbles, battle text visible and tracking (soft at 2× — accepted; Stage 2).
9. **1× mode (Task 5):** 1080p → 32 px/tile, 60 tiles across; persists; 2× restores 64 px/tile.

| Invariant | Proved by |
|---|---|
| I1 strictly integer display scale (adversarial: floor-then-stretch fails) | Task 1 exact-4-tuple + `DisplaySize == Sub × Scale` property test (3050×305 case); Task 6.2 px/tile + gutter measurement |
| I2 fill-rate cap (adversarial) | Task 1 `sub ≤ 1280×720` loop test; Task 6.2 (4K shows 40 tiles at 96px, not 60 at 64px) |
| I3 bounded gutters | Task 1 `0 ≤ window − display < scale`; Task 6.4 odd-size resize |
| I4 native GUI + 720p regression | Task 6.1, 6.5 |
| I5 world text unchanged | Task 6.8 |
| I6 input parity (adversarial) | Task 4.4 + Task 6.6 — exact-tile check fails on inverted transform or missing display origin; chat-typing check fails if B5 unfixed; `IsInsideDisplay` four-edge tests fail if gutters are passed through |
| I7 scene lifecycle (adversarial) | `tools/tests/scene_lifecycle.gd` headless check + Task 6.7 ghost/duplicate-character check |
| Placement: identity, round-trip, mid-screen, clamp (adversarial: no-CanvasSize design fails round-trip) | `WindowPlacementTests`; Task 6.5 restarts |

**Deferred (explicit):**
- Non-16:9 aspect padding with extra map tiles beyond bounds — not needed for I1 (math is aspect-agnostic); revisit with Stage 2 if ultrawide gutters/edges matter.
- World-text crispness (Stage 2); UI font/size scaling (Stage 3 — positions already handled here).

---

## Stage 2 (stub — plan later)

`WorldTextBridge`: move in-world text (`Character._nameLabel`, `ChatBubble`, `BattleText`; HP bars/reticle optional) to a native-resolution `CanvasLayer` between the world texture and `UiLayer`. Per frame, per element: `screenPos = WorldToWindow(worldPos)` — the **forward** transform, the exact inverse of Task 4's `WindowToWorld`: `vp = Current.GetCanvasTransform() * worldPos; screenPos = vp * Scale + DisplayOrigin` (add the method next to `WindowToWorld`). Font sizes in native px; cull outside the display rect; text always above world (fine for top-down; matches Unity's visual result).

## Stage 3 (designed — see 2026-08-21-ui-scale-design.md)

UI scale knob (poor-man's Canvas Scaler): single factor (auto `round(window_h/720)` clamped 1–3, or user slider 1–3 in 0.5 steps) multiplying theme font sizes + window size constants, applied live through a central applier + pure `UiScale` math. Positions are handled by Stage 1 (edge-stick + clamp); the applier re-solves placements via `WindowPlacement` after each layout pass.
