# UI Scale Part 1 — Foundation Implementation Plan

**Goal:** The scale machinery: pure `UiScale` math, a central `UiScaleApplier` with the single apply pass, generic geometry snapshot/scale, window registration, and headless + xUnit proof — usable at a pinned 1× today and drivable at any factor for tests, before the options UI exists (Part 2).

**Architecture:** Windows keep their build-time geometry as the 1× base — `.tscn` pixel offsets load at 1× regardless of the active factor, and C# build code writes 1× base constants (it does **not** scale at build time; scaling happens in `Relayout`). A static `UiScaleLayout` helper snapshots each window's descendant geometry at end-of-`_Ready` as-is (anchor-relative offsets) — that snapshot is the base, no division. Registration calls `Relayout()` once immediately, so a window spawned at runtime under a 2× factor scales in the same frame (zero 1× frames). `UiScaleApplier.Apply(factor)` (plain class, `GameManager`-hosted, `TooltipManager.Instance`-style `Instance` accessor) then: normalizes the factor, mutates `GameTheme.default_font_size`, re-applies registered explicit font overrides, hides live tooltips, calls each registered window's `Relayout()` (generic geometry re-apply + per-window overrides), and lets `BaseWindow.Relayout()` finish by calling the existing idempotent `RepositionForCurrentCanvas()` (with a factor-aware title-bar allowance). Placement math is not duplicated — Stage 1's `WindowPlacement.Resolve` and `RepositionForCurrentCanvas` are reused unchanged in spirit.

**Tech Stack:** C# / Godot 4.7.1 (GodotSharp, `net10.0` test target), xUnit, headless `godot` self-test.

---

## APIs verified (citations)

- `BaseWindow.RepositionForCurrentCanvas()` — `Scripts/UI/BaseWindow.cs:88` — public, idempotent, "safe to call any time after `_Ready`"; first-run-dialog centers, else `WindowPlacement.Resolve(storedOrDefaultPos, Size, savedCanvas, currentCanvas)`. Reads `Size` at call time → **must run after geometry is applied** (that ordering is why the apply pass is fonts → geometry → reposition).
- `GameManager.OnWindowResized` — `Scripts/GameManager.cs:337-345` — existing live-resize precedent: walks `UiLayer`, calls `RepositionForCurrentCanvas()` on every `BaseWindow` via `CollectBaseWindows` (`Scripts/GameManager.cs:347-356`).
- `EnsureHud()` (`Scripts/GameManager.cs:323`) has exactly **one call site**: `MapManager._Ready` (`Scripts/MapManager.cs:93`), a server-driven map transition — at startup (login screen) **no HUD exists**. The applier is created in `GameManager._Ready`, which precedes any window's `_Ready` (windows first build when `EnsureHud` runs). A headless self-test that needs the HUD must call `EnsureHud()` itself (Task 8).
- `CharacterSettings.Options` — `Dictionary<string, object>` with `GetOption<T>(key, default)` / indexer; `IncludeFields` JSON — `Scripts/CharacterSettings.cs:42-67`. Key constants live in `Constants.cs:136` (`public static class Options`). Part 2 will add the two new keys; Part 1 adds **no** persisted state.
- `WindowPlacement.Resolve(savedPos, windowSize, savedCanvas, currentCanvas)` — `Scripts/UI/WindowPlacement.cs:30`; `TitleBarHeight = 24` (`Scripts/UI/WindowPlacement.cs:17`) is the y-containment allowance.
- GodotSharp 4.7.1, verified by **reflection on the actual project DLL** (`/opt/Godot_v4.7.1-stable_mono_linux_x86_64/GodotSharp/Api/Release/GodotSharp.dll`), not docs:
  - `Theme.SetDefaultFontSize(int)` / `GetDefaultFontSize()` — the theme-mutation API.
  - `Control.AddThemeFontSizeOverride(StringName, int)` and `Control.AddThemeConstantOverride(StringName, int)` — set-or-replace; **there is no `SetThemeFontSize`/`SetThemeConstant`** in this binding (the existing codebase already uses `AddThemeFontSizeOverride`).
  - `Control.GetThemeFontSize(StringName, StringName)` / `GetThemeConstant(...)` — effective values (the constants snapshot reads `GetThemeConstant`; the Task 8 audit reads `GetThemeFontSize`).
  - `Control.OffsetLeft/OffsetTop/OffsetRight/OffsetBottom` and `AnchorLeft/Top/Right/Bottom` — all present (reflection). The snapshot records the **offsets** (anchor-relative by construction) and never touches anchors or `Position`/`Size` directly.
  - `Control.HasThemeFontSizeOverride(StringName)` / `HasThemeConstantOverride(StringName)` — override-presence queries; the font audit (Task 8) uses these to find ANY font-override control under `UiLayer` and demand registry membership.
  - `Control.SetMeta(StringName, Variant)` / `GetMeta(...)` — the skip-meta mechanism.
  - `Node.TreeExited` event — deregistration hook.
  - `OS.GetCmdlineUserArgs()` (`string[]`) — project args after `--`; **`GD.GetCommandLineArgs` does not exist**. Task 8's flag comes from here.
  - Anchored roots in the wild: `Scenes/UI/ChatWindow.tscn:11-14` (bottom-left, `offset_top = -213`), `Scenes/UI/Toolbar.tscn` root `anchor_left = 1.0` (right-edge). Offsets scale correctly under either; `Position` would not.
- `theme_override_font_sizes` occurrences to migrate: `Scenes/UI/BankWindow.tscn:43` (9), `Scenes/UI/ChatWindow.tscn:47` (`normal_font_size` 12, RichTextLabel), `Scenes/UI/ChatWindow.tscn:55` (12), `Scenes/UI/DebugWindow.tscn:23,32` (12), `Scenes/UI/VendorWindow.tscn:43` (10). Raw C# overrides: `Scripts/UI/BaseMultipleWindow.cs:60` (12) and `:68` (10). (Bridge files `ChatBubble`/`BattleTextLine`/`BridgedNameLabel` are world-space — **do not touch**.)
- Window geometry is `.tscn` pixel-offset layout (e.g. `Scenes/UI/VitalsWindow.tscn` all `layout_mode = 0` offsets; `Scenes/UI/BaseWindow.tscn` anchor-based children). Slots: `Scenes/UI/ItemSlot.tscn` `custom_minimum_size = Vector2(32, 32)`.
- Headless runner: `/usr/local/bin/godot --headless` (4.7.1 mono). Existing probes `tools/tests/*.gd` intentionally do NOT execute C#; Part 1's C#-executing proof is a project-argument self-test (Task 8), run as `godot --headless -- +selftest=ui_scale`.

## Design refinements vs `2026-08-21-ui-scale-design.md` (ratified in design chat)

- R1 (revised after plan review): per-window hand-written layout constants → **generic snapshot** (`UiScaleLayout`). **Build-time geometry is the base as-is — no division.** The original divide-by-current-factor idea was wrong: `.tscn` geometry loads at 1× regardless of the active factor, so at a 1080p startup (Apply 2× before HUD build) a snapshot would divide 1× geometry by 2 and `Relayout(2)` would un-scale it — the HUD would render at 1× on exactly the displays this feature exists for, and no headless test could catch it (headless factor is always 1). Registration calls `Relayout()` once (same-frame scaling, no flash). The snapshot records **anchor-relative offsets, not Position/Size**, because anchored roots (`ChatWindow` bottom-left, `Toolbar` right-edge) must not have their `Position` scaled or they detach from their edge.
- R2: live tooltips are **hidden** on apply (design said repositioned) — re-shown on next hover; avoids mouse-follow geometry mid-commit.
- R3: windows **self-register** at end of `_Ready` (GameHud does not enumerate) — `VitalsWindow` is a plain `Control`, and runtime-spawned NPC windows (via `BaseWindow._Ready`) register automatically.

---

### Task 1: `UiScale` pure math + xUnit

**Files:**
- Create: `Scripts/UiScale.cs`
- Test: `tests/Goose2Client.Tests/UiScaleTests.cs`

**Step 1: Write the failing tests.**

`UiScale` is a small non-static class (no Godot usings): `float Factor`, `float Factor(float raw)`, `int AutoFactor(int windowHeightPx)`, `int ScaleSize(float basePx)`, `Vector2I ScaleSizeI(Vector2I v)` — `Vector2I` is fine in the test project (GodotSharp is already referenced there, see `WindowPlacementTests`).

Tests (all red against "does not exist"):
- `Factor_SnapsToHalfStepsAndClamps`: `0.4f → 1`, `0.9f → 1`, `1.25f → 1.5f`, `1.7f → 1.5f`, `2.3f → 2.5f`, `3.4f → 3`, `4.2f → 3`, `-1f → 1`.
- `Factor_RejectsNaN`: `Factor(float.NaN)` returns `1` (normalize must be total — corrupt settings pass through here).
- `AutoFactor_Boundaries`: `719 → 1`, `720 → 1`, `1079 → 2`, `1080 → 2`, `1439 → 2`, `1440 → 3`, `2880 → 3` (clamp case).
- `ScaleSize_RoundsHalfAwayFromZero`: with factor `1.5f`, `ScaleSize(10f) == 15` and — the pin — a `.5` product rounds away: set factor `2.5f`, `ScaleSize(3f) == 8` (7.5 → 8, not 7).
- `ScaleSize_MinOneGuard`: factor `1f`, `ScaleSize(0f) == 1`; smallest real base `ScaleSize(1f) == 1` at factor `1f`.
- `ScaleSizeI_PerAxis`: factor `2f`, `new Vector2I(32, 55) → new Vector2I(64, 110)`.

**Step 2 (red):** `dotnet test tests/Goose2Client.Tests` → compile fail (no `UiScale`).

**Step 3:** Implement `Scripts/UiScale.cs`. Use explicit half-away-from-zero rounding (Godot's `Mathf.Round` is not allowed — this file is Godot-free): `int MathF.Round(x, MidpointRounding.AwayFromZero)`. `Factor`: `if (float.IsNaN(raw)) raw = Min; snapped = MathF.Round(raw / Step, MidpointRounding.AwayFromZero) * Step; return clamp to [Min, Max]`.

**Step 4 (green):** all pass. **Step 5:** commit `feat: add UiScale pure scale math`.

| Invariant | Proved by |
|-----------|-----------|
| Corrupt/NaN values normalize into range | `Factor_SnapsToHalfStepsAndClamps`, `Factor_RejectsNaN` |
| 1.5-step slider value 3.4 can't leak through | `Factor_SnapsToHalfStepsAndClamps` |
| Rounding is deterministic, not engine-dependent | `ScaleSize_RoundsHalfAwayFromZero` |
| Build-time geometry is the 1× base; a runtime-spawned window under 2× scales correctly (the adversarial leg the headless factor-1 bias can't fake) | Task 8 step 2b (in-engine) |

---

### Task 2: `WindowPlacement.Resolve` — factor-aware title-bar allowance + scaled-size cases

**Files:**
- Modify: `Scripts/UI/WindowPlacement.cs:30`
- Test: `tests/Goose2Client.Tests/WindowPlacementTests.cs` (extend)

**Mutation impact:**
- Source of truth changed: `WindowPlacement.Resolve` signature (`Scripts/UI/WindowPlacement.cs:30`).
- Important readers: `BaseWindow.RepositionForCurrentCanvas()` (`Scripts/UI/BaseWindow.cs:106`); every existing xUnit test in `WindowPlacementTests`.
- Derived/cached state: none.
- Propagation: existing callers keep working via a new **optional** parameter `int titleBarAllowance = TitleBarHeight` — current call sites and tests are untouched; `BaseWindow` switches in Task 5.
- Invariants: default-parameter behavior is bit-identical to today for all existing tests.
- Observable proof: existing tests pass unchanged (default path) + new tests below (explicit path).

**Step 1: Failing tests** (new cases — the "sizes doubled, placement must stay sane" matrix):
- `Resolve_MiddleParkedWindowKeepsCoordinateWhenSizeDoubles`: saved `(640, 360)` on `LegacyCanvas`, window size `(100, 50) → (200, 100)`, current canvas `1280×720` → result stays `(640, 360)` (middle band, both halves).
- `Resolve_EdgeStuckWindowKeepsEdgeOffsetAtLargerSize`: saved `(0, 640)` left-edge on `LegacyCanvas`, size `(160, 60) → (320, 120)`, current `1280×720` → x stays `0`, y keeps its left-edge-irrelevant value per existing rule (assert exactly what the axis rule yields; x must be 0).
- `Resolve_WindowLargerThanCanvas_ClampsToOrigin`: saved `(0, 0)` on `LegacyCanvas`, size `(2000, 1500)`, current `1280×720` → `(0, 0)` (both axes edge-stuck to origin; x clamps to `max(0, 1280−2000)=0`).
- `Resolve_TitleBarAllowance_Scaled`: saved `(100, 700)` on `LegacyCanvas`, window height `700`, current `1280×720`. Trace: y-axis `right = 720 − (700+700) = −680 < left`, so `y = 720 − 700 − (−680) = 700`; default allowance clamps to `720−24` → assert y `== 696`; with `titleBarAllowance: 48` assert y `== 672`. (Saved y must be ≥ 696 or the clamp never engages — e.g. saved `(100, 600)` yields 600, not 696; do not use it.)

**Step 2 (red):** new tests fail (parameter doesn't exist → compile error for the 4-arg+1 calls; size cases may already pass — the compile failure is the red).
**Step 3:** add the optional parameter; use it in the y clamp (`Mathf.Max(0, currentCanvas.Y - titleBarAllowance)`).
**Step 4 (green):** full `WindowPlacementTests` + new cases pass.
**Step 5:** commit `feat: title-bar allowance parameter for scaled windows in WindowPlacement`.

---

### Task 3: `UiScaleApplier` (plain class, single apply pass) + font registry

**Files:**
- Create: `Scripts/UiScaleApplier.cs`
- Modify: `Scripts/GameManager.cs` (create/`Instance` in `_Ready`, after `CharacterSettings` exists; initial `Apply` there too — no HUD-ordering constraint: windows first build later, at `EnsureHud()` from `MapManager`)
- Modify: `Scripts/UI/GameHud.cs:39` (`_Ready` start: `UiScaleApplier.Instance.ClearRegistry()`)

**Shape** (no `Node` base — plain class; Godot types only for theme/control args):

```csharp
public class UiScaleApplier
{
    public static UiScaleApplier Instance { get; private set; }
    public UiScale Scale { get; }                 // owns the active factor

    // Registry — see publication boundary below.
    private readonly List<IScalableWindow> _windows = new();
    private readonly List<(Control C, StringName Prop, float Base)> _fonts = new();

    public void RegisterWindow(IScalableWindow w);          // duplicate-safe (contains-check)
    public void UnregisterWindow(IScalableWindow w);
    public void ApplyFontSize(Control c, float basePx, StringName prop = "font_size");
    public void Apply(float factor, ApplyReason reason);    // the single mutation entry point
    public void ClearRegistry();                             // windows + fonts
    public float Factor => Scale.Factor;
    public int ScaleSize(float basePx) => Scale.ScaleSize(basePx);   // code-side pixel helper
}
public enum ApplyReason { Startup, UserCommit, AutoResize }
```

`IScalableWindow` (`Scripts/IScalableWindow.cs`): `void Relayout();`

**`Apply(factor, reason)` exact sequence (ordering is load-bearing):**
1. `Scale.Factor(factor)` (normalizes; NaN-safe per Task 1). If unchanged from previous factor → still run steps 3–5 once at startup, but on non-`Startup` reasons **early-return when factor is unchanged** (window-resize spam costs one compare).
2. HIDE live tooltips: `if (TooltipManager.Instance != null) TooltipManager.Instance.HideAll();` — add `HideAll()` to `TooltipManager` (sets `Visible = false` on the four tooltip controls; mirrors existing `Hide*Tooltip`, `Scripts/UI/TooltipManager.cs:37-60`). (R2)
3. Theme: `Preload("res://Assets/UI/GameTheme.tres")` cached field, `theme.SetDefaultFontSize(ScaleSize(10))` (base 10 = `GameTheme.tres` `default_font_size`).
4. Fonts: for every `(c, prop, base)` — `if (c.IsInsideTree()) c.AddThemeFontSizeOverride(prop, ScaleSize(base))`. (Skip-free: a control leaving the tree is simply unqueried next pass; registry entries are cleared at HUD rebuild.)
5. Geometry: `foreach (var w in _windows) w.Relayout();` — `BaseWindow.Relayout()` (Task 5) ends with `RepositionForCurrentCanvas()`, so repositioning happens per window right after its own geometry — the `Size` it reads is already scaled (`BaseWindow.cs:106`).

**Publication boundary (registry):**
- Creation order: window builds nodes (at 1× base constants) → snapshots geometry (Task 5) → `RegisterWindow(this)` → **calls `Relayout()` once immediately**: build-time geometry is the 1× base, so a window spawned at runtime under a 2× factor lands at 2× in the same frame (zero 1× frames); at 1× the call is a no-op re-apply.
- Teardown: `UnregisterWindow` on `tree_exited` (connected at registration); `ClearRegistry()` at `GameHud._Ready` start (`GameHud.cs:39`) for map-entry rebuilds. A leaked entry is harmless (step 4/5 null-skip).
- Readers (the apply pass) can only ever see a fully-built, already-laid-out window: registration happens at END of `_Ready` (R1 snapshot also taken there), and `Apply` never runs during a `_Ready` (startup Apply precedes HUD build; user commits happen on input events between frames).
- Failure behavior: `RegisterWindow` with a null/duplicate is a no-op; no partial state.

**`ApplyFontSize` contract:** sets the override immediately (build-time correctness) AND records the entry. It is the ONLY place window code may set a font-size theme override (raw `AddThemeFontSizeOverride` calls in window code are prohibited — Task 7 enforces the migration; the self-test in Task 8 is the tripwire).

**GameManager wiring:** in `_Ready`, after `CharacterSettings` exists (the HUD builds later, from `MapManager`, so there is no HUD-ordering constraint):
```csharp
UiScaleApplier.Instance = new UiScaleApplier();
UiScaleApplier.Instance.Apply(1f, ApplyReason.Startup);
```
Part 1 pins 1× (no settings read — the options keys are Part 2). A `// Part 2: read Options.UiScaleMode/UiScaleValue here` is NOT added (comment policy: no forward-looking comments); Part 2 edits this line.

**Step ordering / tests:** this task has no pure-xUnit surface (Godot types). Proof comes from Task 8's self-test (audit + smoke) and Task 5/7's 1× no-op run. Compile + existing test suite green is the gate here.
**Commit:** `feat: add UiScaleApplier apply pass and font registry`.

---

### Task 4: `UiScaleLayout` generic snapshot (R1 mechanism, revised)

**Files:**
- Create: `Scripts/UiScaleLayout.cs`

**API:**
```csharp
public static class UiScaleLayout
{
    public const string SkipMeta = "ui_scale_skip";

    public static List<GeomRecord> Snapshot(Control root);
    public static void Apply(List<GeomRecord> records, float factor);
}
```
`GeomRecord` (nested or private): `(Control c, float oLeft, float oTop, float oRight, float oBottom, Vector2 minSize /* zero if none */, (StringName,int)[] constants /* empty if none */)`. **Offsets, not Position/Size** — offsets are anchor-relative by construction, so the same record works for top-left-anchored controls (where offsets ≡ position/size) and for anchored roots (`ChatWindow` bottom-left, `Toolbar` right-edge) where scaling `Position` would detach the edge-stick.

**`Snapshot(root)` contract:**
- Preconditions: called exactly once, at END of the window's `_Ready`, after ALL build-time C# geometry. Build code writes **1× base constants** — it must NOT scale at build time (no `applier.ScaleSize` in build paths); scaling happens exclusively in `Apply`. The snapshot records the geometry as-is; that IS the base.
- Walks `root` + all descendants (depth-first). Skips any node (and its subtree) with meta `ui_scale_skip == true` (R: per-frame/dynamic controls opt out). For each `Control`: record `OffsetLeft/OffsetTop/OffsetRight/OffsetBottom`, `CustomMinimumSize` (skip if `Vector2.Zero`), and effective theme constants via `GetThemeConstant(name)` — snapshot ONLY names returned by a fixed list: `"separation"`, `"h_separation"`, `"v_separation"` (the only ones used in `Scenes/UI/*.tscn` — verify with `grep -rn theme_override_constants Scenes/UI` at implementation time; extend the list only with cited occurrences). Skip constants whose value is `0`.
- Postconditions: `Apply(records, 1f)` reproduces the end-of-`_Ready` geometry bit-identically; `Apply` at any factor is a pure re-scaling of the recorded base. Anchors are never recorded or touched.
- Container-managed children (HBox/VBox/Grid children — login VBox, toolbar, item grids): container layout overwrites their offsets on each layout pass; their actual scaling rides on `CustomMinimumSize` + `separation` constants + font-driven computed minimums. Recording their offsets is a harmless no-op overwrite — do NOT chase "why didn't my offset stick" for such children.

**`Apply(records, factor)`:** for each record, if `c.IsInsideTree()`: `c.OffsetLeft = round(c.oLeft × f)` (same for the other three, per-value `int(round(base×f))`); min-size via `c.CustomMinimumSize`; constants via `c.AddThemeConstantOverride(name, scaled)`. **This file contains NO Godot-free math of its own** — it takes the factor as `float` and does explicit `MathF.Round(x, AwayFromZero)` on plain floats (mirroring Task 1's policy; `UiScale`'s instance isn't reachable from a static helper without a parameter, and passing the factor is the seam).

**Flags:** anchor-attached controls (e.g. `CloseButton` at `offset_left = -18` in `BaseWindow.tscn`) scale their offsets correctly with anchors untouched; `ChatWindow`'s bottom-left root (`offset_top = -213`) stays edge-stuck because its negative offsets simply double.

**Proof:** no direct unit test (needs a live tree) — proven by Task 8: (a) 1× no-op run (snapshot→apply at factor 1 must leave every rect bit-identical), (b) factor-2 audit (sampled values equal `round(base×2)`: `VitalsWindow` root size 183×55→366×110, an `ItemSlot` min-size 32→64, `ChatWindow` root offsets `−213/−5`→`−426/−10`), (c) the runtime-spawn leg (Task 8 step 2b) — the adversarial proof that build-time geometry is the base at a live non-1 factor.
**Commit:** `feat: add UiScaleLayout geometry snapshot`.

---

### Task 5: `BaseWindow` registration, `Relayout`, factor-aware reposition

**Files:**
- Modify: `Scripts/UI/BaseWindow.cs`
- Modify: `Scripts/UI/BaseMultipleWindow.cs:46-77` (font + line positions)
- Modify: `Scripts/IScalableWindow.cs` (if not created in Task 3 — created there)

**Changes to `BaseWindow` (implements `IScalableWindow`):**
- Field: `private readonly List<GeomRecord> _geom = null!;`
- `_Ready()`: at the VERY END (after drag-handle wiring, `MoveChild` etc. — after `BaseWindow.cs:76`'s `MoveChild` call):
  ```csharp
  var applier = UiScaleApplier.Instance;
  _geom = UiScaleLayout.Snapshot(this);
  applier.RegisterWindow(this);
  Relayout();
  ```
  Connect `TreeExited += () => applier.UnregisterWindow(this);` in the same place.
  **Invariant:** no code after these lines may set geometry on `this` or descendants directly — the snapshot is the base and the next `Apply` clobbers any direct write. (Build code BEFORE the snapshot uses 1× base constants; that geometry becomes part of the base.)
- `public void Relayout()`:
  ```csharp
  UiScaleLayout.Apply(_geom, UiScaleApplier.Instance.Factor);
  RepositionForCurrentCanvas();   // reads the just-scaled Size (BaseWindow.cs:106)
  ```
  and `RepositionForCurrentCanvas()`'s `Resolve` call (line 106) passes `titleBarAllowance: UiScaleApplier.Instance != null ? UiScaleApplier.Instance.ScaleSize(24) : WindowPlacement.TitleBarHeight` (Task 2 parameter). At factor 1 this is bit-identical to today.

**`BaseMultipleWindow` (fonts only — no relayout helper needed):**
- All 20 line labels are created in `_Ready` (fixed `LineCount = 20`, lines 63-71) — BEFORE the end-of-`_Ready` snapshot — so the generic snapshot captures their 1× positions (`LinesOrigin` (6,22) + `i × LineRowHeight` (11.18) stay as base constants, exactly as written today) and `Relayout` scales them from the records. There is NO `RelayoutLines()` helper in this design.
- Fonts: line 60 `b.AddThemeFontSizeOverride("font_size", ButtonFontSize)` → `applier.ApplyFontSize(b, ButtonFontSize)`; line 68 `label.AddThemeFontSizeOverride("font_size", LineFontSize)` → `applier.ApplyFontSize(label, LineFontSize)`.

**Other `BaseWindow` subclasses:** no changes expected — their geometry is `.tscn`/generic. If any subclass sets geometry in `_Ready` AFTER the snapshot point, the next `Apply` clobbers it — the 2× audit (Task 8) catches this; treat such failures as "move this window's build code before the snapshot", not as scaler bugs.

**Gate:** full xUnit suite green; compile clean.
**Commit:** `feat: BaseWindow self-registration and factor-aware relayout`.

---

### Task 6: non-`BaseWindow` roots (Vitals, Chat, Party, Debug, BuffEffects, Toolbar, TooltipManager)

**Files:**
- Modify: `Scripts/UI/VitalsWindow.cs:33` (`_Ready`)
- Create: `Scripts/UI/Toolbar.cs` + attach to `Scenes/UI/Toolbar.tscn` root — the `HBoxContainer` root is **scriptless** (scripts live only on `DestroyButton`/`ToolbarItem` children); a new root script implements the registration pattern. The root is right-edge anchored (`anchor_left = 1.0`), so offset scaling is what keeps it stuck.
- Modify: `Scripts/UI/ChatWindow.cs`, `Scripts/UI/PartyWindow.cs`, `Scripts/UI/DebugWindow.cs`, `Scripts/UI/BuffEffectsWindow.cs` (`_Ready` each — **all five are plain `Control` roots, verified**, and Chat/Party/Debug/BuffEffects would otherwise never scale)
- Modify: `Scripts/UI/TooltipManager.cs:17` (root + `HideAll()` added in Task 3)
- Modify: `Scenes/UI/Tooltips.tscn` (set `meta/ui_scale_skip = true` on the four tooltip control nodes `ItemTooltip`/`SpellTooltip`/`TextTooltip`/`MapItemTooltip` — their geometry is set dynamically per-show in C#, e.g. `ItemTooltipControl.SetItem`)

**Pattern for each root** (plain-`Control` windows): same as Task 5 minus reposition:
```csharp
// end of _Ready:
var applier = UiScaleApplier.Instance;
_geom = UiScaleLayout.Snapshot(this);
applier.RegisterWindow(this);
Relayout();
TreeExited += () => applier.UnregisterWindow(this);
// Relayout(): UiScaleLayout.Apply(_geom, applier.Factor);
```
- `VitalsWindow` (root at tscn `(8,8)`, 183×55): registers; its root position scales via geometry (intended).
- `ChatWindow` (bottom-left-anchored root, offsets `8, −213, 508, −5`): registers; offset scaling doubles the margins and keeps it edge-stuck — NO `RepositionForCurrentCanvas`-style reposition (it has none; its placement IS the anchor).
- `PartyWindow`, `DebugWindow`, `BuffEffectsWindow`: register (top-left-anchored tscn offsets scale).
- `Toolbar`: registers (offsets scale; its Options button wiring is untouched).
- `TooltipManager`: registers ONLY its root + static children; the four dynamic tooltip nodes carry the skip meta so per-show C# geometry is untouched; live tooltips hide on commit (Task 3 step 2, R2) and reappear correctly on next hover because `SetItem`/`ShowTextTooltip` recompute position from the live factor.
- `WorldDropTarget` (`Scripts/UI/WorldDropTarget.cs`, full-rect `Ignore` filter): **do not register** — full-rect anchors, nothing to scale.

**Gate:** xUnit green; 1× headless run (Task 8 command) shows no layout drift (visual/rect check via the audit's 1× no-op leg).
**Commit:** `feat: register vitals, toolbar, and tooltip roots with the scale applier`.

---

### Task 7: font-override migration (tscn + raw C# → `ApplyFontSize`)

**Files:**
- Modify: `Scenes/UI/BankWindow.tscn:43`, `Scenes/UI/ChatWindow.tscn:47,55`, `Scenes/UI/DebugWindow.tscn:23,32`, `Scenes/UI/VendorWindow.tscn:43` — **remove** the `theme_override_font_sizes` lines (values migrate to C# as cited base constants).
- Modify: `Scripts/UI/BankWindow.cs`, `Scripts/UI/ChatWindow.cs`, `Scripts/UI/DebugWindow.cs`, `Scripts/UI/VendorWindow.cs` `_Ready`: `applier.ApplyFontSize(<the control>, <base>)` — Chat's RichTextLabel uses `prop: "normal_font_size"`.

**Rules:**
- The base constant in C# must equal the removed tscn value (9/12/12/12/10). At factor 1 the rendered size is unchanged — verified by Task 8's 1× audit.
- Bridge files (`ChatBubble.cs:95`, `BattleTextLine.cs:33`, `BridgedNameLabel.cs:17`) stay on raw overrides — world-space, out of scope.
- After this task, `grep -rn "AddThemeFontSizeOverride\|theme_override_font_sizes" Scenes/UI Scripts/UI | grep -v Bridge` returns only `UiScaleApplier.cs` (the helper) and the three bridge files. State this grep in the commit message body as the invariant.

**Gate:** grep invariant above; xUnit green.
**Commit:** `refactor: route all root-viewport font overrides through UiScaleApplier.ApplyFontSize`.

---

### Task 8: headless self-test (audit + 1× no-op + 2× smoke)

**Files:**
- Modify: `Scripts/GameManager.cs` (`_Ready`, after settings load): read `OS.GetCmdlineUserArgs()`; if it contains `"+selftest=ui_scale"`, run the sequence below on the NEXT frame (one `ProcessFrame` await) and `GetTree().Quit(failed ? 1 : 0)`. **The sequence's first step is `GameManager.Instance.EnsureHud()`** (plus another `ProcessFrame` await) — the HUD does not exist at startup: `EnsureHud`'s only call site is the server-driven map transition (`Scripts/MapManager.cs:93`), which headless never reaches. Without this the test fails for the wrong reason (no registered windows at all).
- Create: `tools/tests/run_ui_scale.sh` — wrapper: `godot --headless -- +selftest=ui_scale; exit $?` (docs the invocation; args after `--` are what `OS.GetCmdlineUserArgs()` returns; the existing probes use `--script`, this one needs the project + C#).

**Sequence (all inside C#, `Print`-labeled steps):**
1. **1× no-op:** for each registered `BaseWindow`: snapshot its descendants' `(OffsetLeft, OffsetTop, OffsetRight, OffsetBottom)` into a dict, call `w.Relayout()`, re-read; assert bit-identical (factor is 1 from startup Apply). Catches snapshot bugs and any `_Ready` code that sets geometry after the snapshot point.
2. **2× apply:** `UiScaleApplier.Instance.Apply(2f, ApplyReason.UserCommit)`. Then assert:
   - `GameTheme` (the applier's cached instance) `GetThemeFontSize("font_size") == 20`.
   - **Font audit (adversarial):** walk every `Control` under `UiLayer`; for any with `HasThemeFontSizeOverride("font_size")` or `HasThemeFontSizeOverride("normal_font_size")` (excluding nothing — bridge text lives in the world viewport, not `UiLayer`): the control MUST be in the applier's font registry, and its effective `GetThemeFontSize(prop)` must equal `base × 2`. A raw `AddThemeFontSizeOverride` added outside the registry (e.g. a future PR skipping `ApplyFontSize`) fails here.
   - Sampled geometry: `Vitals` root `Size == (366, 110)` (tscn 183×55) and `Position == (16, 16)` (tscn 8,8); one `ItemSlot` under Inventory has `CustomMinimumSize == (64, 64)`; `ChatWindow` root offsets `OffsetTop == −426` and `OffsetBottom == −10` (tscn −213/−5 doubled — edge-stick preserved by offset scaling, NO reposition involved; ChatWindow is not a `BaseWindow`); a `BaseWindow`-derived dialog still satisfies `WindowPlacement` containment: `0 <= X <= canvas.X - w.Size.X` etc. (the re-solve path).
   - All four tooltips hidden (R2).
2b. **Runtime-spawn leg (adversarial, THE regression the review caught):** with the HUD now at 2×, instantiate a fresh window (`GD.Load<ScenePackaged>("res://Scenes/UI/BankWindow.tscn").Instantiate()` — pick any window whose `_Ready` needs no server state; verify at implementation time), `UiLayer.AddChild`, await a frame; assert a sampled rect equals `round(base×2)` (e.g. its `Content` child's offsets doubled). This is the leg that fails if anyone re-introduces divide-by-factor snapshotting or build-time scaling — headless factor-1 bias cannot fake it. `QueueFree()` afterwards.
3. **Back to 1×:** `Apply(1f, UserCommit)`; re-assert the step-1 geometry dict equality (idempotence in both directions — catches records that baked in a scaled base).

**Red/green:** run before Task 5/6 land → expected FAIL (no registered windows / no `IScalableWindow`); after Task 7 → PASS. If it fails with "no windows registered", the `EnsureHud()` step is missing — fix the test, not the product code. Final state of this task is the gate for the whole part: `bash tools/tests/run_ui_scale.sh` exits 0 with labeled `OK` lines, no `ERR_`/script-error output.

**Commit:** `test: headless ui-scale self-test (1x no-op, 2x audit, idempotence)`.

---

## Invariant-to-test matrix (part-wide)

| Invariant | Proved by |
|-----------|-----------|
| Factor normalizes all sources (slider, auto, corrupt save) | Task 1 `Factor_*` |
| Auto boundaries 720/1080/1440 + 4K clamp | Task 1 `AutoFactor_Boundaries` |
| Rounding policy is pinned, engine-independent | Task 1 `ScaleSize_RoundsHalfAwayFromZero` |
| Build-time geometry is the 1× base — a runtime-spawned window under a live 2× scales correctly (the regression no headless factor-1 test can fake) | Task 8 step 2b (adversarial) + Task 8 step 3 (idempotence) |
| Bottom-anchored root stays edge-stuck after scaling | Task 8 step 2 (ChatWindow offsets `−213/−5` → `−426/−10`) |
| 1× is bit-identical to today (no visual regression at default) | Task 8 steps 1 & 3 |
| Placement survives scaled sizes (middle/edge/oversize) | Task 2 `Resolve_*` tests |
| Scaled y-containment uses scaled title bar | Task 2 `Resolve_TitleBarAllowance_Scaled` |
| Fonts scale through the registry; raw overrides trip the wire | Task 8 step 2 (audit) + Task 7 grep invariant |
| Geometry scales per window incl. min-sizes | Task 8 step 2 (sampled rects) |
| Tooltips hidden on commit (R2) | Task 8 step 2 |
| Registry leaks are harmless / cleared at HUD rebuild | Task 3 `ClearRegistry` wiring (`GameHud.cs:39`) + null-skip in steps 4–5 (covered by Task 8 clean run across HUD build) |

**Explicitly deferred to Part 2:** options UI (slider/mode), `Options.UiScaleMode`/`UiScaleValue` persistence + startup read (Task 3's pinned `Apply(1f, Startup)` becomes the settings-driven value), auto-mode window-resize path (`GameManager.cs:103` `size_changed` handler), login/loading registration (their scenes attach no `GameTheme` — they need explicit `ApplyFontSize` with base 16, the engine default), drag-cancel on commit, manual verification matrix, and the design's accepted-limitations list.
