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
  - Anchored roots in the wild: `Scenes/UI/ChatWindow.tscn:11-14` (bottom-left, `offset_top = -213`), `Scenes/UI/Toolbar.tscn` root bottom-right (all four anchors `1.0`). Offsets scale correctly under either; `Position` would not.
- Test project (`tests/Goose2Client.Tests`) pins **GodotSharp 4.6.2** from NuGet (not the 4.7.1 in the engine dir) — the xUnit surface (`UiScale`, `WindowPlacement` param) must stay 4.6.2-compatible; `Vector2I`/`MathF` are fine (existing `WindowPlacementTests` already use GodotSharp).
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

`UiScale` is a small non-static class (no Godot usings), with **explicitly separated state and pure functions** (review finding: a `Factor` property + `Factor(float)` method is a C# CS0102 compile error — verified against the compiler — and it was ambiguous whether `Factor(raw)` normalized or mutated):
- `public float CurrentFactor { get; set; }` — plain state; **`UiScaleApplier.Apply` is the only writer** (tests set it via object initializer).
- `public static float NormalizeFactor(float raw)` — pure: clamp + snap, NaN → `MinFactor`. Never touches `CurrentFactor`.
- `public static int AutoFactor(int windowHeightPx)` — pure.
- `public int ScaleSize(float basePx)` / `public Vector2I ScaleSizeI(Vector2I v)` — read `CurrentFactor`.
- **Pinned constants** `public const float MinFactor = 1f, MaxFactor = 3f, Step = 0.5f` (the slider range; `NormalizeFactor` clamps/snaps to these).
`Vector2I` is fine in the test project (GodotSharp is already referenced there, see `WindowPlacementTests`).

Tests (all red against "does not exist"):
- `NormalizeFactor_SnapsToHalfStepsAndClamps`: `0.4f → 1`, `0.9f → 1`, `1.25f → 1.5f`, `1.7f → 1.5f`, `2.3f → 2.5f`, `3.4f → 3`, `4.2f → 3`, `-1f → 1`.
- `NormalizeFactor_RejectsNaN`: `NormalizeFactor(float.NaN)` returns `1` (normalize must be total — corrupt settings pass through here).
- `CurrentFactor_IsPlainState`: `new UiScale { CurrentFactor = 2.5f }` → `ScaleSize(10f) == 25` (25.0), and `NormalizeFactor`/`AutoFactor` do not read or write it.
- `AutoFactor_Boundaries` (explicit thresholds — NOT `round(h/720)`, which would make 1440 → 2): `719 → 1`, `720 → 1`, `1079 → 1`, `1080 → 2`, `1439 → 2`, `1440 → 3`, `2880 → 3` (clamp case).
- `ScaleSize_RoundsHalfAwayFromZero`: with `CurrentFactor = 1.5f`, `ScaleSize(10f) == 15` and — the pin — a `.5` product rounds away: `CurrentFactor = 2.5f`, `ScaleSize(3f) == 8` (7.5 → 8, not 7).
- `ScaleSize_MinOneGuard`: factor `1f`, `ScaleSize(0f) == 1`; smallest real base `ScaleSize(1f) == 1` at factor `1f`.
- `ScaleSizeI_PerAxis`: factor `2f`, `new Vector2I(32, 55) → new Vector2I(64, 110)`.

**Step 2 (red):** `dotnet test tests/Goose2Client.Tests` → compile fail (no `UiScale`).

**Step 3:** Implement `Scripts/UiScale.cs`. Use explicit half-away-from-zero rounding (Godot's `Mathf.Round` is not allowed — this file is Godot-free): `int MathF.Round(x, MidpointRounding.AwayFromZero)`. `Factor`: `if (float.IsNaN(raw)) raw = Min; snapped = MathF.Round(raw / Step, MidpointRounding.AwayFromZero) * Step; return clamp to [Min, Max]`. `AutoFactor(h)`: `h < 1080 ? 1 : h < 1440 ? 2 : 3` (clamped by construction).

**Step 4 (green):** all pass. **Step 5:** commit `feat: add UiScale pure scale math`.

| Invariant | Proved by |
|-----------|-----------|
| Corrupt/NaN values normalize into range | `Factor_SnapsToHalfStepsAndClamps`, `Factor_RejectsNaN` |
| 1.5-step slider value 3.4 can't leak through | `Factor_SnapsToHalfStepsAndClamps` |
| Rounding is deterministic, not engine-dependent | `ScaleSize_RoundsHalfAwayFromZero` |
| Build-time geometry is the 1× base; a runtime-spawned window under 2× scales correctly (the adversarial leg the headless factor-1 bias can't fake) | Task 8 step 2b (in-engine) |

---

### Task 2: `WindowPlacement.Resolve` — scale-commit placement (saved-size offset derivation + scaled title-bar allowance)

**The model (review finding — the previous draft's edge-offset derivation was broken at scale):** a saved position is a **display coordinate at the factor under which it was saved** (the old live factor at commit time, 1× for defaults/first-run). The trailing-edge offset must therefore be derived in that **saved display space** — with the window's size at the saved factor — and re-anchored to the edge with the **new** size. The current code derives `right = savedEdge − (saved + size)` with the NEW size (`Scripts/UI/WindowPlacement.cs:44`), so a hotbar saved at `(·, 679)` with 1× height 36 (5px bottom margin, `Scripts/UI/DefaultWindowLayout.cs:14`) re-resolves at 2× as `right = 720 − (679 + 72) = −31` → re-stick returns Y=679 unchanged → the 31px-taller window overruns the canvas bottom and only the title-bar clamp saves it. **Chosen model: transform at commit — edge-stuck windows keep their edge margin (clamped when the scaled window + margin doesn't fit); middle-band windows keep their top-left coordinate.**

**Files:**
- Modify: `Scripts/UI/WindowPlacement.cs`
- Test: `tests/Goose2Client.Tests/WindowPlacementTests.cs` (extend)

**New signature** (old 4-arg form stays as a delegating overload so all existing tests/callers compile unchanged):
```csharp
public static Vector2 Resolve(Vector2 savedPos, Vector2 windowSize, Vector2I savedCanvas, Vector2I currentCanvas)
    => Resolve(savedPos, windowSize, windowSize, savedCanvas, currentCanvas);

public static Vector2 Resolve(Vector2 savedPos, Vector2 savedSize, Vector2 windowSize,
    Vector2I savedCanvas, Vector2I currentCanvas, int titleBarAllowance = TitleBarHeight)
```
`ResolveAxis(saved, savedSize, size, savedEdge, currentEdge)`: middle-band test uses **`savedSize`** (`right = savedEdge − (saved + savedSize)`); leading-edge keep uses `saved`; trailing-edge re-stick uses the NEW size: `currentEdge − size − right`. Clamp: `x ∈ [0, max(0, cur.X − windowSize.X)]`, `y ∈ [0, max(0, cur.Y − titleBarAllowance)]` (allowance scaled at the call site — `BaseWindow` passes `applier.ScaleSize(24)`, Task 5).

**Step 1: Failing tests** (pure — the whole margin model is pinned here, since the tiny headless canvas can't express margins):
- `Resolve_SavedSizeDefaultsToWindowSize`: old 4-arg form == new 5-arg with `savedSize == windowSize` on a sample of positions (identity for the default path).
- `Resolve_EdgeStuckBottomMarginSurvivesScaleUp` (the REAL hotbar default — `Scripts/UI/DefaultWindowLayout.cs:14`: `(520, 679)`, 351×36, 5px bottom margin on 720p): saved `(520, 679)`, `savedSize (351, 36)`, `windowSize (702, 72)`, saved canvas `1280×720`, current `1280×720` → y `== 720 − 72 − 5 == 643` (the 5px margin survives; x: left 520 / right 409, both ≥ 25% of 1280 → middle-band → stays 520).
- `Resolve_EdgeStuckMarginClampsWhenScaledWindowExceedsCanvas`: saved `(520, 679)`, `savedSize (351, 36)`, `windowSize (702, 300)`, current canvas `1280×200` → y `== 0` (margin lost to clamp, title bar reachable), x middle-band `== 520` clamped to `[0, 578]` → 520.
- `Resolve_MiddleParkedWindowKeepsCoordinateWhenSizeDoubles`: saved `(640, 360)`, `savedSize (100, 50)`, `windowSize (200, 100)`, current `1280×720` → stays `(640, 360)`.
- `Resolve_WindowLargerThanCanvas_ClampsToOrigin`: saved `(0, 0)`, `savedSize (2000, 1500)`, `windowSize (2000, 1500)`, current `1280×720` → `(0, 0)`.
- `Resolve_TitleBarAllowance_Scaled`: saved `(100, 700)`, `savedSize (100, 700)`, `windowSize (100, 700)`, current `1280×720` → y `== 696` (default allowance); with `titleBarAllowance: 48` → y `== 672`.

**Step 2 (red):** the 5-arg calls don't compile (red).
**Step 3:** implement the overload + `ResolveAxis` split + allowance parameter (default keeps today's clamp).
**Step 4 (green):** full `WindowPlacementTests` (existing + new).
**Step 5:** commit `feat: scale-commit placement model (saved-size offset derivation) in WindowPlacement`.

**Mutation impact:** `BaseWindow.RepositionForCurrentCanvas` (Task 5) switches to the new overload with the captured old size; `GameManager`'s resize reposition walk keeps the 4-arg form (its saved positions were saved at the current factor — `savedSize == windowSize` is exactly right there). No persisted-state or cache impact.

---

### Task 3: `UiScaleApplier` (plain class, single apply pass) + font registry

**Files:**
- Create: `Scripts/UiScaleApplier.cs`
- Modify: `Scripts/GameManager.cs` (create/`Instance` + initial settings-independent `Apply` in `_Ready` — `CharacterSettings` does NOT exist yet there: it is created only in `LoadSettings`, called from `LoginScene.cs:103` on successful login; the settings-driven re-`Apply` is Part 2's `LoadSettings` hook. Windows first build later, at `EnsureHud()` from `MapManager` (post-login, settings exist).)

**Shape** (no `Node` base — plain class; Godot types only for theme/control args):

```csharp
public class UiScaleApplier
{
    public static UiScaleApplier Instance { get; internal set; }   // set by GameManager._Ready (same assembly — NOT `private set`, which GameManager can't call)
    public UiScale Scale { get; }                 // owns the active factor (CurrentFactor)
    public Theme Theme { get; }                   // GD.Load<Theme>("res://Assets/UI/GameTheme.tres") — C# has NO Preload<T>; GD.Load is the API (the old draft's `Preload(…)` would not compile)

    // Registry — see publication boundary below.
    private readonly List<WindowRegistration> _windows = new();
    private readonly List<(Control C, StringName Prop, float Base)> _fonts = new();

    public WindowRegistration RegisterWindow(IScalableWindow w);   // bookkeeping ONLY (duplicate-safe); returns the registration (window + its font entries). The initial Relayout is owned by the window's ScaleRegister (Task 5) — NOT here (single owner).
    public void UnregisterWindow(IScalableWindow w);               // removes the window AND every font entry whose control is w or a descendant of w (fonts are recorded flat, owned per-window by ancestry — runtime NPC windows free their labels with them)
    public void ApplyFontSize(Control c, float basePx, StringName prop = "font_size");
    public void Apply(float factor, ApplyReason reason);           // the single mutation entry point

    // Read-only seams (Task 8 self-test needs them; public — the applier surface is already public, and this repo has no InternalsVisibleTo infra):
    public float Factor => Scale.CurrentFactor;
    public int ScaleSize(float basePx) => Scale.ScaleSize(basePx);   // code-side pixel helper
    public IReadOnlyList<WindowRegistration> RegisteredWindows { get; }
    public bool TryGetFontBase(Control c, StringName prop, out float basePx);
}
public enum ApplyReason { Startup, UserCommit, AutoResize }
```

`WindowRegistration` (nested public): `IScalableWindow Window`, `Control ControlRef` (`w as Control` — for the descendant font prune and the old-size capture below), `List<(Control, StringName, float)> Fonts`. `ApplyFontSize` appends to the flat list AND to the most recent registration's `Fonts` (fonts belong to the window that's registering them; a control with no registered window ancestor still lands in the flat list only — pruned by validity in `Apply`). `IScalableWindow` (`Scripts/IScalableWindow.cs`): `void Relayout();`

**`Apply(factor, reason)` exact sequence (ordering is load-bearing):**
1. `var f = UiScale.NormalizeFactor(factor)` (pure; NaN-safe per Task 1); `if (f == Scale.CurrentFactor && _appliedOnce) return;` — the first `Apply` always runs (startup). Set `Scale.CurrentFactor = f` — **the applier is the only writer** (Task 1 semantics).
2. **(Part 2 Task 2 lands here — before hiding tooltips):** cancel any in-progress window move-drag via `BaseWindow.CancelDrag()` on every registered `BaseWindow` (cast; non-`BaseWindow` roots skip).
3. HIDE live tooltips: `if (TooltipManager.Instance != null) TooltipManager.Instance.HideAll();` — add `HideAll()` to `TooltipManager` (sets `Visible = false` on the four tooltip controls; mirrors existing `Hide*Tooltip`, `Scripts/UI/TooltipManager.cs:37-60`). (R2)
4. Theme: `Theme.SetDefaultFontSize(ScaleSize(10))` (base 10 = `GameTheme.tres` `default_font_size`; `Theme` is the `GD.Load`ed cached field from the API block).
5. Fonts + **validity prune**: for every `(c, prop, base)` in the flat list — `if (!GodotObject.IsInstanceValid(c)) { remove entry; continue; }` (defensive: covers controls freed outside a registered window, e.g. an NPC window whose `tree_exited` raced); then `if (c.IsInsideTree()) c.AddThemeFontSizeOverride(prop, ScaleSize(base))`. Window entries get the same `IsInstanceValid` prune in the geometry pass (normal teardown is `tree_exited` → `UnregisterWindow`, which also prunes descendant fonts — the prune is the backstop, not the primary path).
6. **Capture old display sizes**: `foreach (var r in _windows) oldSizes[r.ControlRef] = r.ControlRef.Size;` — BEFORE any relayout (the saved positions were saved at this size's factor — Task 2's model). This is the capture that makes edge margins survive the commit.
7. Geometry: `foreach (var r in _windows) r.Window.Relayout();` — geometry ONLY (Task 5's `Relayout` no longer repositions; the applier owns repositioning because only it knows the old size).
8. Placement: `foreach (var r in _windows) if (r.Window is BaseWindow bw) bw.RepositionForCurrentCanvas(oldSizes[r.ControlRef], titleBarAllowance: ScaleSize(24));` — Task 2's new `Resolve` overload: saved-space size = the captured old size, new size = the just-relaid size. Non-`BaseWindow` roots (Task 6) have no reposition (anchor-stuck).

**Publication boundary (registry):**
- Creation order (single Relayout owner — review finding): window builds nodes (at 1× base constants) → **`ScaleRegister()`** (Task 5) does snapshot → `RegisterWindow(this)` (bookkeeping) → `Relayout()` once: build-time geometry is the 1× base, so a window spawned at runtime under a 2× factor lands at 2× in the same frame (zero 1× frames); at 1× the call is a no-op re-apply. `RegisterWindow` itself never calls `Relayout` (the old draft gave both owners — one of them had to go).
- Teardown: `UnregisterWindow` on `tree_exited` (connected at registration) removes the window AND its descendant font entries (review finding: fonts are separate controls — there is no "fonts clean themselves up" path; the ancestry prune in `UnregisterWindow` IS that path). `GameHud` is never freed/rebuilt (guarded `EnsureHud`, no free path) — there is no rebuild-clear; the `IsInstanceValid` prune (step 4/6) is the backstop for anything that leaks past `tree_exited`.
- Readers (the apply pass) can only ever see a fully-built, already-laid-out window: registration happens at END of `_Ready` (R1 snapshot also taken there), and `Apply` never runs during a `_Ready` (startup Apply precedes HUD build; user commits happen on input events between frames).
- Failure behavior: `RegisterWindow` with a null/duplicate is a no-op; no partial state.

**`ApplyFontSize` contract:** sets the override immediately (build-time correctness) AND records the entry. It is the ONLY place window code may set a font-size theme override (raw `AddThemeFontSizeOverride` calls in window code are prohibited — Task 7 enforces the migration; the self-test in Task 8 is the tripwire).

**GameManager wiring:** in `_Ready` — **before `CharacterSettings` exists** (it is created only on successful login, `LoginScene.cs:103` → `LoadSettings`), so the startup `Apply` must be settings-independent. Use the settings-free best guess (Auto): the login screen (Part 2 registers it) is already at the right factor for Auto users, and at the tiny headless root size (~64–100 px, NOT the project's 1280×720 — Part 1 Task 8 step 0) `AutoFactor(small) == 1`; Part 1's self-test additionally forces `Apply(1f, …)` explicitly (Task 8), so determinism does not depend on the headless size at all:
```csharp
UiScaleApplier.Instance = new UiScaleApplier();
var canvas = (Vector2I)GetTree().Root.GetVisibleRect().Size;
UiScaleApplier.Instance.Apply(UiScaleApplier.Instance.Scale.AutoFactor(canvas.Y), ApplyReason.Startup);
```
Part 1 reads no settings (the options keys are Part 2); Part 2 adds the settings-driven re-`Apply` in `LoadSettings` (runs post-login, pre-HUD — still before any map entry, so no unscaled flash) and sets `applier.Mode`.

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

    public sealed record GeomRecord(
        Control C,                                  // the control (never null at record time)
        float OLeft, float OTop, float ORight, float OBottom,  // raw recorded floats; IGNORED when ContainerManaged
        bool ContainerManaged,                      // parent is a Container — offsets belong to the container, never written by Apply
        Vector2 MinSize, bool HasMinSize,           // CustomMinimumSize; HasMinSize false when Zero
        (StringName Name, int Value)[] Constants);  // empty when none

    public static List<GeomRecord> Snapshot(Control root);
    public static void Apply(List<GeomRecord> records, float factor);
}
```
(`GeomRecord` is a concrete `sealed record` — the previous draft described it without a declaration; the implementer must not improvise the shape.) **Offsets are stored as the recorded raw floats.** **Offsets, not Position/Size** — offsets are anchor-relative by construction, so the same record works for top-left-anchored controls (where offsets ≡ position/size) and for anchored roots (`ChatWindow` bottom-left, `Toolbar` right-edge) where scaling `Position` would detach the edge-stick.

**`Snapshot(root)` contract:**
- Preconditions: called exactly once, at END of the window's `_Ready`, after ALL build-time C# geometry. Build code writes **1× base constants** — it must NOT scale at build time (no `applier.ScaleSize` in build paths); scaling happens exclusively in `Apply`. The snapshot records the geometry as-is; that IS the base.
- Walks `root` + all descendants (depth-first). Skips any node (and its subtree) with meta `ui_scale_skip == true` (R: per-frame/dynamic controls opt out). For each `Control`: record `OffsetLeft/OffsetTop/OffsetRight/OffsetBottom`, `CustomMinimumSize` (skip if `Vector2.Zero`), and effective theme constants via `GetThemeConstant(name)` — snapshot ONLY names returned by a fixed list: `"separation"`, `"h_separation"`, `"v_separation"` (the only ones used in `Scenes/UI/*.tscn` — verify with `grep -rn theme_override_constants Scenes/UI` at implementation time; extend the list only with cited occurrences). Skip constants whose value is `0`.
- Postconditions: `Apply(records, 1f)` reproduces the end-of-`_Ready` geometry **bit-identically — which requires writing the recorded RAW FLOATS when `factor == 1`**, because real geometry contains fractional values (`BaseMultipleWindow.LineRowHeight = 11.18f` → label positions like `22 + i × 11.18f` are non-integer; `int(round(33.18)) == 33` would not reproduce the base). At any `factor != 1`, writes are `int(round(base×f))`. Records always store the original base, so 2×→1×→2× round-trips. Anchors are never recorded or touched.
- **Container-managed children (review finding — the old "harmless no-op overwrite" was wrong, two ways):** for any control whose `GetParent() is Container` (HBox/VBox/Grid children — login VBox, toolbar, item grids), record `ContainerManaged = true` and DO NOT use its offsets in `Apply` (see below). Rationale: (a) the recorded offsets can be captured BEFORE the container's queued layout pass, so writing them back makes the immediate 1× bit-identity assertion FLAKY (Relayout writes pre-layout offsets, the container re-sorts them a moment later); (b) their real scaling rides on `CustomMinimumSize` + `separation` constants + font-driven computed minimums — the container re-derives correct child offsets from those on its own pass. Implementer must verify per window that manually-positioned children (hotbar slots, `BaseMultipleWindow` line labels) have NON-container parents — if any manual-offset parent turns out to be a `Container`, that window's manual layout is already broken today (the container would overwrite it) and the window is out of the snapshot's remit.
- **1× bit-identity is asserted after one `ProcessFrame`** (queued container layouts flush), not in the same tick — Task 8's comparison follows this.

**`Apply(records, factor)`:** for each record, if `GodotObject.IsInstanceValid(c) && c.IsInsideTree()`: **offsets — SKIP entirely when `ContainerManaged`** (the container owns them); otherwise if `factor == 1f` → write back the recorded raw floats verbatim (`c.OffsetLeft = c.oLeft`, …) — the bit-identical 1× path (see postconditions, `BaseMultipleWindow.cs`'s `11.18f` pitch); else → `c.OffsetLeft = int(round(c.oLeft × f))` (same for the other three, per-value `MathF.Round(x, AwayFromZero)`); min-size via `c.CustomMinimumSize` (all records, container-managed or not); constants via `c.AddThemeConstantOverride(name, scaled)` (raw-write at 1× too). **This file contains NO Godot-free math of its own** — it takes the factor as `float` and does explicit `MathF.Round(x, AwayFromZero)` on plain floats (mirroring Task 1's policy; `UiScale`'s instance isn't reachable from a static helper without a parameter, and passing the factor is the seam).

**Flags:** anchor-attached controls (e.g. `CloseButton` at `offset_left = -18` in `BaseWindow.tscn`) scale their offsets correctly with anchors untouched; `ChatWindow`'s bottom-left root (`offset_top = -213`) stays edge-stuck because its negative offsets simply double.

**Proof:** no direct unit test (needs a live tree) — proven by Task 8: (a) 1× no-op run (snapshot→apply at factor 1 must leave every rect bit-identical, **compared after one `ProcessFrame`** so queued container layouts have flushed — both the captured baseline and the post-Relayout read), (b) factor-2 audit (sampled values equal `round(base×2)`: `VitalsWindow` root size 183×55→366×110, an `ItemSlot` min-size 32→64, `ChatWindow` root offsets `−213/−5`→`−426/−10`), (c) the runtime-spawn leg (Task 8 step 2b) — the adversarial proof that build-time geometry is the base at a live non-1 factor.
**Commit:** `feat: add UiScaleLayout geometry snapshot`.

---

### Task 5: `BaseWindow` registration, `Relayout`, factor-aware reposition

**Files:**
- Modify: `Scripts/UI/BaseWindow.cs`
- Modify: `Scripts/UI/BaseMultipleWindow.cs:46-77` (font + line positions)
- Modify: `Scripts/IScalableWindow.cs` (if not created in Task 3 — created there)

**WHY THE SNAPSHOT MUST RUN AT END OF THE SUBCLASS `_Ready` (plan-review finding — the original "end of `BaseWindow._Ready`" timing was broken):** every window subclass calls `base._Ready()` FIRST and builds its runtime content AFTER it — the `ItemSlot` grids (`InventoryWindow.cs:25-40`: slots instantiated in `_Ready` after `base._Ready()`, none are in the `.tscn`), the 20 NPC line labels (`BaseMultipleWindow.cs:43,63-71`), hotbar pages, equipment grids. A snapshot at the end of `BaseWindow._Ready` would miss all of it → at live 2× the window frame scales but its content stays 1×. **Fix — `ScaleRegister()` pattern (no reordering of `base._Ready()`, which would flip `Visible`-restore ordering for toggle windows):**
- `BaseWindow` gains `protected void ScaleRegister()`:
  ```csharp
  if (_scaleRegistered) return;                       // idempotent (deferred fallback)
  _scaleRegistered = true;
  _geom = UiScaleLayout.Snapshot(this);
  var applier = UiScaleApplier.Instance;
  applier.RegisterWindow(this);
  Relayout();                                          // same-frame scale; no-op re-apply at 1×
  TreeExited += () => applier.UnregisterWindow(this);
  ```
- `BaseWindow._Ready()`: UNCHANGED in order/behavior (keeps `Visible`-restore semantics bit-identical). At its very end (after `MoveChild`, `BaseWindow.cs:76`) it schedules the fallback `CallDeferred(nameof(ScaleRegister));` (concrete Godot C# form — `CallDeferred` takes a method-name string; `Callable.From(ScaleRegister).CallDeferred()` is the equivalent; do NOT write `CallDeferred(ScaleRegister)`) — covers `BaseWindow` subclasses that do NOT override `_Ready` (InfoWindow, QuestWindow: registration lands one frame later; both are server-driven dialogs, no perceptible flash).
- **Each of the 9 window subclasses that override `_Ready` gets ONE line at the end of it: `ScaleRegister();`** — `BankWindow`, `BaseMultipleWindow`, `CharacterWindow`, `CombineBagContainerWindow`, `HotbarWindow`, `InventoryWindow`, `OptionsWindow`, `SpellbookWindow`, `VendorWindow`. (Verified audit: no subclass build code reads the `Content`/`TitleLabel`/`Background` properties — all use their own `GetNode` calls — so no reordering was needed; `BaseMultipleWindowManager` does not call `base._Ready()` and is not an on-screen window — leave it.)
- **Invariant:** no code after the `ScaleRegister()` call may set geometry on `this` or descendants directly — the snapshot is the base and the next `Apply` clobbers any direct write. (Build code BEFORE the call uses 1× base constants; that geometry becomes part of the base.)
- `BaseWindow` fields: `private List<GeomRecord> _geom = null!;   // NOT readonly — assigned by ScaleRegister(); C# forbids readonly assignment outside constructors (review finding: the old draft's `readonly` would not compile)`; `private bool _scaleRegistered;`
- `public void Relayout()` — geometry ONLY (the applier's placement step owns repositioning, because only it knows the pre-commit size — Task 3 step 7):
  ```csharp
  UiScaleLayout.Apply(_geom, UiScaleApplier.Instance.Factor);
  ```
- `RepositionForCurrentCanvas()` (line 106) becomes `RepositionForCurrentCanvas(Vector2 savedSpaceSize, int titleBarAllowance = WindowPlacement.TitleBarHeight)` and calls Task 2's new overload: `WindowPlacement.Resolve(saved, savedSpaceSize, Size, savedCanvas, currentCanvas, titleBarAllowance)`. Callers: the applier's step 7 passes the captured old size + `applier.ScaleSize(24)` (scaled title bar); `GameManager`'s canvas-resize walk keeps passing `Size` as `savedSpaceSize` with the current factor's allowance (its saved positions were saved at the current factor — identity there is correct). At factor 1 with `savedSpaceSize == Size` this is bit-identical to today.

**`BaseMultipleWindow` (fonts only — no relayout helper needed):**
- All 20 line labels are created in `_Ready` (fixed `LineCount = 20`, lines 63-71) — BEFORE the end-of-`_Ready` `ScaleRegister()` — so the generic snapshot captures their 1× positions (`LinesOrigin` (6,22) + `i × LineRowHeight` (11.18) stay as base constants, exactly as written today) and `Relayout` scales them from the records. There is NO `RelayoutLines()` helper in this design.
- Fonts: line 60 `b.AddThemeFontSizeOverride("font_size", ButtonFontSize)` → `applier.ApplyFontSize(b, ButtonFontSize)`; line 68 `label.AddThemeFontSizeOverride("font_size", LineFontSize)` → `applier.ApplyFontSize(label, LineFontSize)`.

**Other `BaseWindow` subclasses:** covered by the 9-file one-liner list above. If any subclass sets geometry in `_Ready` AFTER the `ScaleRegister()` line (or packet-driven geometry after registration), the next `Apply` clobbers it — the 2× audit (Task 8) catches this; treat such failures as "register after build", not as scaler bugs.

**Gate:** full xUnit suite green; compile clean; Task 8's 2× audit proves the runtime-built slot grids actually scaled (the leg that fails if the end-of-`_Ready` timing regresses).
**Commit:** `feat: BaseWindow self-registration and factor-aware relayout`.

---

### Task 6: non-`BaseWindow` roots (Vitals, Chat, Party, Debug, BuffEffects, Toolbar, TooltipManager)

**Files:**
- Modify: `Scripts/UI/VitalsWindow.cs:33` (`_Ready`)
- Create: `Scripts/UI/Toolbar.cs` + attach to `Scenes/UI/Toolbar.tscn` root — the `HBoxContainer` root is **scriptless** (scripts live only on `DestroyButton`/`ToolbarItem` children); a new root script implements the registration pattern. The root is bottom-right anchored (all four anchors `1.0`), so offset scaling is what keeps it stuck.
- Modify: `Scripts/UI/ChatWindow.cs`, `Scripts/UI/PartyWindow.cs`, `Scripts/UI/DebugWindow.cs`, `Scripts/UI/BuffEffectsWindow.cs` (`_Ready` each — **all five are plain `Control` roots, verified**, and Chat/Party/Debug/BuffEffects would otherwise never scale)
- Modify: `Scripts/UI/TooltipManager.cs:17` (root + `HideAll()` added in Task 3)
- Modify: `Scenes/UI/Tooltips.tscn` (set `meta/ui_scale_skip = true` on the four tooltip control nodes `ItemTooltip`/`SpellTooltip`/`TextTooltip`/`MapItemTooltip` — their geometry is set dynamically per-show in C#, e.g. `ItemTooltipControl.SetItem`; the skip meta keeps the SNAPSHOT away from them — it does NOT exempt them from scaling, see the tooltip subsection below)
- Modify: `Scripts/UI/ItemTooltipControl.cs` (`_Process` per-frame layout: 40px text column, 9px right pad, 46/48 header constants, +4 bottom — all 1×), `Scripts/UI/SpellTooltipControl.cs` (+8×4 pad), `Scripts/UI/TextTooltipControl.cs` (+8×4 pad), `Scripts/UI/MapItemTooltipControl.cs` (6/4/2/4 margins, 400px label widths)
- Modify: `Scripts/UI/VitalsCharacterDisplay.cs:47` (`SetLayer`'s 53px/20px portrait math)
- Create: `Scripts/TooltipMetrics.cs` (pure) + extend `tests/Goose2Client.Tests`

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
- `VitalsWindow` (root at tscn `(8,8)`, 183×55): registers; its root position scales via geometry (intended). **Dynamic portrait path (review finding):** `VitalsCharacterDisplay.Refresh()` → `SetLayer` writes each portrait `TextureRect`'s `Size`/`Position` from fixed 1× constants (`PortraitSize = 53f`, `HeadDropPixels = 20f`, `PortraitZoom = 1.25f`, `VitalsCharacterDisplay.cs:47-80`) AFTER the window snapshot — at 2× the frame grows but the next character update repaints the portrait at 1×. Fix: (a) extract the pure math to `VitalsPortraitMetrics.Layout(Vector2 texSize, float factor)` → `(Vector2 rectSize, Vector2 rectPosition)` (draw size `texSize × 1.25 × f`, centered on `53 × f`, drop `20 × f`); (b) `SetLayer` reads `UiScaleApplier.Instance.Factor` and uses it; (c) `VitalsWindow.Relayout()` re-runs the portrait layout for currently-loaded layers (it already holds the layer graphic IDs — re-run `SetLayer` for each). xUnit pins `VitalsPortraitMetrics` at 1×/2×; the headless self-test cannot assert a live portrait (no character graphics without a server) — the manual matrix (Part 2) covers it: portrait fills the scaled circle after a character load at 2×.
- `ChatWindow` (bottom-left-anchored root, offsets `8, −213, 508, −5`): registers; offset scaling doubles the margins and keeps it edge-stuck — NO `RepositionForCurrentCanvas`-style reposition (it has none; its placement IS the anchor).
- `PartyWindow`, `DebugWindow`, `BuffEffectsWindow`: register (top-left-anchored tscn offsets scale).
- `Toolbar`: registers (offsets scale; its Options button wiring is untouched).
- `TooltipManager`: registers ONLY its root + static children; the four dynamic tooltip nodes carry the skip meta so the SNAPSHOT ignores them (their geometry is per-frame C#).
- **Factor-aware tooltip layout (review finding — "hidden on apply" was a scope escape, not a solution):** the four tooltip controls compute their layout every frame from 1× constants (item: 40px text column right of the 32px icon, 9px right pad, header block to y≈46, stats from y=48, +4 bottom; spell/text: label min + (8,4); map-item: 6/4/2/4 margins + 400px label widths; item icons sit at tscn offsets 4–36). Fonts scale via the project theme but this layout would not — a 2× tooltip = 2× fonts in a 1× box. Fix: (a) create PURE `TooltipMetrics` (Godot-free, xUnit-tested): `ItemMetrics(float factor)` → `(TextColumn, RightPad, HeaderTop, StatsTop, ExtraBottom, IconSize, IconOffset)`, `TextPad(float factor)` → `(w, h)`, `MapItemMetrics(float factor)` → `(LeftMargin, TopMargin, RowGap, BottomMargin, LabelWidth)` — each value `UiScale`-scaled from the cited 1× base, half-away-from-zero rounding, all read through the applier's `Scale` instance; (b) each control's `_Process` reads the metrics EVERY frame from `UiScaleApplier.Instance` (factor can change between shows; the per-frame read IS the live mechanism — no snapshot involved); (c) the item tooltip's icon `TextureRect` gets offset/size set per-show from `ItemMetrics.IconOffset/IconSize` (replacing the tscn 4–36 offsets); (d) viewport clamping (`PositionTooltip`) is untouched — it uses live `Size`, which is now scaled, so clamps stay correct; (e) xUnit: full constant table at 1× (must equal today's literals), 1.5×, 2×, and 1×→2×→1× round-trip per control; (f) self-test leg: show the SPELL tooltip (simplest — one label) over a visible parent via its public show API, two `ProcessFrame`s, assert `Size == labelMin + (ScaleSize(8), ScaleSize(4))` at 2× and the y-clamp `y + size.Y <= canvas.Y`; if the spell tooltip cannot be shown headless (no suitable parent), the leg degrades to: pure-table xUnit + the Part 2 manual matrix M7 (item tooltip at 3×) — state which one landed in the commit message. Live tooltips still HIDE on commit (Task 3 step 2, R2) so no per-frame reflow is needed mid-commit; on next hover they rebuild at the live factor.
- `WorldDropTarget` (`Scripts/UI/WorldDropTarget.cs`, full-rect, `MouseFilter.Pass`): **do not register** — full-rect anchors, nothing to scale.

**Gate:** xUnit green (incl. new `TooltipMetrics`/`VitalsPortraitMetrics` tables); 1× headless run (Task 8 command) shows no layout drift (visual/rect check via the audit's 1× no-op leg).
**Commit:** `feat: register vitals, chat, party, debug, buff-effects, toolbar, tooltip roots with the scale applier (factor-aware tooltip + portrait layout)`.

---

### Task 7: font-override migration (tscn + raw C# → `ApplyFontSize`)

**Files:**
- Modify: `Scenes/UI/BankWindow.tscn:43`, `Scenes/UI/ChatWindow.tscn:47,55`, `Scenes/UI/DebugWindow.tscn:23,32`, `Scenes/UI/VendorWindow.tscn:43` — **remove** the `theme_override_font_sizes` lines (values migrate to C# as cited base constants).
- Modify: `Scripts/UI/BankWindow.cs`, `Scripts/UI/ChatWindow.cs`, `Scripts/UI/DebugWindow.cs`, `Scripts/UI/VendorWindow.cs` `_Ready`: `applier.ApplyFontSize(<the control>, <base>)` — Chat's RichTextLabel uses `prop: "normal_font_size"`.

**Rules:**
- The base constant in C# must equal the removed tscn value (9/12/12/12/10). At factor 1 the rendered size is unchanged — verified by Task 8's 1× audit.
- Bridge files (`ChatBubble.cs:95`, `BattleTextLine.cs:33`, `BridgedNameLabel.cs:17`) stay on raw overrides — world-space, out of scope.
- After this task, `grep -rn "AddThemeFontSizeOverride\|theme_override_font_sizes" Scenes Scripts | grep -v Overlays` returns only `UiScaleApplier.cs` (the helper). (The three bridge files live in `Scripts/Overlays/` — a different path from the window code — so exclude that directory rather than filename-filtering.) State this grep in the commit message body as the invariant.

**Gate:** grep invariant above; xUnit green.
**Commit:** `refactor: route all root-viewport font overrides through UiScaleApplier.ApplyFontSize`.

---

### Task 8: headless self-test (audit + 1× no-op + 2× smoke)

**Files:**
- Modify: `Scripts/GameManager.cs` (`_Ready`): read `OS.GetCmdlineUserArgs()`; if it contains `"+selftest=ui_scale"`, run the sequence below on the NEXT frame (one `ProcessFrame` await) and `GetTree().Quit(failed ? 1 : 0)`. **The sequence's first steps (review finding F1 — both required, in this order):** (1) `GameManager.Instance.LoadSettings("ui-scale-selftest")` — the HUD does not exist at startup (`EnsureHud`'s only call site is the server-driven map transition, `Scripts/MapManager.cs:93`, which headless never reaches), so the test builds it; BUT `CharacterSettings` is only created by `LoadSettings` (normally via login, `LoginScene.cs:103`), and **every** `BaseWindow._Ready` dereferences it unguarded (`Scripts/UI/BaseWindow.cs:51` — `GameManager.Instance.CharacterSettings.GetWindowSettings(...)`), with further unguarded derefs in `OptionsWindow.cs:24/28/32`, `HotbarWindow.cs:75`, `InventoryWindow.cs:51`, `CharacterWindow.cs:91`, `SpellbookWindow.cs:80` — calling `EnsureHud` first NREs on the first window. `LoadSettings` here creates in-memory defaults; nothing is written to disk (no `Save()` fires — no window close/toggle/drag happens headless). (2) **Force the 1× baseline (review finding — determinism):** `UiScaleApplier.Instance.Apply(1f, ApplyReason.Startup);` — the settings file `user://ui-scale-selftest-settings.json` (`CharacterSettings.cs:69`) MAY exist from an earlier run or be hand-written; once Part 2's `LoadSettings` hook lands it would `Apply` a persisted Manual 2×/3× and invalidate every 1× assertion below. The explicit `Apply(1f, …)` after `LoadSettings` (registry still empty — login is unregistered in Part 1) makes the baseline independent of both the headless canvas size AND any persisted selftest profile. (3) `GameManager.Instance.EnsureHud()` (plus another `ProcessFrame` await).
- Create: `tools/tests/run_ui_scale.sh` — wrapper: `godot --headless -- +selftest=ui_scale; exit $?` (docs the invocation; args after `--` are what `OS.GetCmdlineUserArgs()` returns; the existing probes use `--script`, this one needs the project + C#).

**Sequence (all inside C#, `Print`-labeled steps):**
0. **Pin the headless canvas (review finding F3):** `Print` the root visible-rect size first. Headless does NOT apply the project's 1280×720 window size (probes report ~64–100 px). Every assertion below must therefore use the ACTUAL size read at runtime — never assume 1280×720. (The factor-1 baseline still holds: `AutoFactor(anything < 720) == 1`.)
1. **1× no-op:** for each registered `BaseWindow` (enumerate via `UiScaleApplier.Instance.RegisteredWindows`): `await ProcessFrame` first (flush queued container layouts), snapshot its descendants' `(OffsetLeft, OffsetTop, OffsetRight, OffsetBottom)` into a dict, call `w.Relayout()`, **`await ProcessFrame` again**, re-read; assert bit-identical (factor forced to 1 above). The two `ProcessFrame`s matter (review finding): without them a container-managed child's offsets are compared mid-layout-pass and the assertion is flaky. Catches snapshot bugs and any `_Ready` code that sets geometry after the `ScaleRegister()` line.
2. **2× apply:** `UiScaleApplier.Instance.Apply(2f, ApplyReason.UserCommit)`. Then assert:
   - `GameTheme` (the applier's cached instance) `GetDefaultFontSize() == 20` (`Theme` has no `GetThemeFontSize` — `SetDefaultFontSize`/`GetDefaultFontSize` are the API, reflection-verified).
   - **Font audit (adversarial):** walk every `Control` under `UiLayer`; for any with `HasThemeFontSizeOverride("font_size")` or `HasThemeFontSizeOverride("normal_font_size")` (excluding nothing — bridge text lives in the world viewport, not `UiLayer`): the control MUST be in the applier's font registry, and its effective `GetThemeFontSize(prop)` must equal `base × 2`. A raw `AddThemeFontSizeOverride` added outside the registry (e.g. a future PR skipping `ApplyFontSize`) fails here. **Scope note:** the audit walks `UiLayer` only — the login scene (not under `UiLayer`) is outside it; Task 7's source grep plus Part 2 Task 5's login self-test leg cover that surface.
   - Sampled geometry: `Vitals` root `Size == (366, 110)` (tscn 183×55) and `Position == (16, 16)` (tscn 8,8); one `ItemSlot` under Inventory has `CustomMinimumSize == (64, 64)`; `ChatWindow` root offsets `OffsetTop == −426` and `OffsetBottom == −10` (tscn −213/−5 doubled — edge-stick preserved by offset scaling, NO reposition involved; ChatWindow is not a `BaseWindow`); a `BaseWindow`-derived dialog still satisfies `WindowPlacement`'s ACTUAL postcondition (mirror `WindowPlacement.cs` exactly, review finding — the old `canvas.Y - w.Size.Y` bound was wrong: production clamps y with the TITLE-BAR allowance, not the full window height): `0 <= X <= Max(0, canvas.X - w.Size.X)` and `0 <= Y <= Max(0, canvas.Y - applier.ScaleSize(24))` (at 2× that's 48, not 24, and not the window height). The margin-preservation model itself is xUnit-pinned (Task 2) — the tiny headless canvas cannot express margins.
   - All four tooltips hidden (R2).
   - **Tooltip live-size leg (Task 6):** the spell-tooltip show leg — `Size == labelMin + (ScaleSize(8), ScaleSize(4))` at 2× + y-clamp — or its stated fallback (pure-table + M10), whichever landed.
   - **Font registry lookup** uses `applier.TryGetFontBase(c, prop, out base)` + `applier.Theme` (the seams from Task 3) — no reflection, no friend access.
2b. **Runtime-spawn leg (adversarial, THE regression the review caught):** with the HUD now at 2×, instantiate a fresh window (`GD.Load<PackedScene>("res://Scenes/UI/BankWindow.tscn").Instantiate()` — pick any window whose `_Ready` needs no server state; verify at implementation time), `UiLayer.AddChild`, await a frame; assert a sampled rect equals `round(base×2)` (e.g. its `Content` child's offsets doubled). This is the leg that fails if anyone re-introduces divide-by-factor snapshotting or build-time scaling — headless factor-1 bias cannot fake it. `QueueFree()` afterwards.
3. **Back to 1×:** `Apply(1f, UserCommit)`; re-assert the step-1 geometry dict equality (idempotence in both directions — catches records that baked in a scaled base).

**Red/green:** run before Task 5/6 land → expected FAIL (no registered windows / no `IScalableWindow`); after Task 7 → PASS. If it NREs in a window `_Ready` on `CharacterSettings`, the `LoadSettings` step is missing (finding F1); if it fails with "no windows registered", the `EnsureHud()` step is missing — fix the test, not the product code. Final state of this task is the gate for the whole part: `bash tools/tests/run_ui_scale.sh` exits 0 with labeled `OK` lines, no `ERR_`/script-error output.

**Commit:** `test: headless ui-scale self-test (1x no-op, 2x audit, idempotence)`.

---

## Invariant-to-test matrix (part-wide)

| Invariant | Proved by |
|-----------|-----------|
| Factor normalizes all sources (slider, auto, corrupt save) | Task 1 `NormalizeFactor_*` |
| Auto boundaries 720/1080/1440 + 4K clamp | Task 1 `AutoFactor_Boundaries` |
| Rounding policy is pinned, engine-independent | Task 1 `ScaleSize_RoundsHalfAwayFromZero` |
| Build-time geometry is the 1× base — a runtime-spawned window under a live 2× scales correctly (the regression no headless factor-1 test can fake) | Task 8 step 2b (adversarial) + Task 8 step 3 (idempotence) |
| Bottom-anchored root stays edge-stuck after scaling | Task 8 step 2 (ChatWindow offsets `−213/−5` → `−426/−10`) |
| 1× is bit-identical to today (no visual regression at default) | Task 8 steps 1 & 3 |
| Placement survives scaled sizes (middle/edge/oversize) | Task 2 `Resolve_*` tests |
| Edge-stuck windows keep their edge margin across a scale commit (saved-size offset derivation) | Task 2 `Resolve_EdgeStuckBottomMarginSurvivesScaleUp` / `..._ClampsWhenScaledWindowExceedsCanvas` |
| Container-managed children: snapshot skips their offsets; final geometry is container-derived and stable | Task 4 `ContainerManaged` rule + Task 8 step 1 (PostFrame compare) |
| Scaled y-containment uses scaled title bar | Task 2 `Resolve_TitleBarAllowance_Scaled` |
| Fonts scale through the registry; raw overrides trip the wire | Task 8 step 2 (audit) + Task 7 grep invariant |
| Geometry scales per window incl. min-sizes | Task 8 step 2 (sampled rects) |
| Tooltips hidden on commit (R2) | Task 8 step 2 |
| Registry stays clean: windows + descendant fonts deregister on `tree_exited`; orphans pruned by `IsInstanceValid` | apply steps 5/7 prune + `UnregisterWindow` ancestry prune (covered by Task 8 clean run across `EnsureHud`) |

**Explicitly deferred to Part 2:** options UI (slider/mode), `Options.UiScaleMode`/`UiScaleValue` persistence + startup read (Part 1's pre-login Auto `Apply(AutoFactor(canvas.Y), Startup)` becomes settings-driven), auto-mode window-resize path (`GameManager.cs:103` `size_changed` handler), login/loading registration (their scenes attach no per-scene theme, but `project.godot:37` sets `theme/custom` project-wide — their text already resolves at `font_size == 10` through the applier's theme, so Part 2 Task 5 is GEOMETRY-ONLY registration; the 10→20→10 round-trip pins it), drag-cancel on commit, manual verification matrix, and the design's accepted-limitations list.
