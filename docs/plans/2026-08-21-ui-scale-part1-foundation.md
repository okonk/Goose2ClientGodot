# UI Scale Part 1 — Foundation Implementation Plan

**Goal:** The scale machinery: pure `UiScale` math, a central `UiScaleApplier` with the single apply pass, generic geometry snapshot/scale, window registration, and headless + xUnit proof — usable at a pinned 1× today and drivable at any factor for tests, before the options UI exists (Part 2).

**Architecture:** Windows keep their build-time geometry as the 1× base — `.tscn` pixel offsets load at 1× regardless of the active factor, and C# build code writes 1× base constants (it does **not** scale at build time; scaling happens in `Relayout`). A static `UiScaleLayout` helper snapshots each window's descendant geometry at end-of-`_Ready` as-is (anchor-relative offsets) — that snapshot is the base, no division. `ScaleRegister()` (end of each window's `_Ready`) snapshots, registers, relays out, and places — so a window spawned at runtime under a 2× factor scales AND places in the same frame (zero 1× frames). `UiScaleApplier.Apply(factor)` (plain class, `GameManager`-hosted, `TooltipManager.Instance`-style `Instance` accessor) then: normalizes the factor, cancels in-flight window drags (Part 2), hides live tooltips, mutates `GameTheme.default_font_size`, re-applies registered explicit font overrides, calls each registered window's geometry-only `Relayout()`, then every `BaseWindow`'s `RepositionFromSaved()`. Placement is the **saved-quad model** (Task 2): each window persists (position, size, factor, canvas) at drag-end, and every placement — registration, scale commit, canvas resize — derives from that quad + the live (Size, factor, canvas) via pure `WindowPlacement.ResolveScaled`; the quad is invariant across commits, so scale commits round-trip exactly and edge margins (logical px) scale with the factor.

**Tech Stack:** C# / Godot 4.7.1 (GodotSharp, `net10.0` test target), xUnit, headless `godot` self-test.

**Execution:** dedicated worktree off main (via @using-git-worktrees); tasks run sequentially as written — each compiles standalone and lands as its own commit. Recommended: @subagent-driven-development (fresh subagent per task, spec-compliance then code-quality review after each). Task 10 needs the headless `godot` binary (`/usr/local/bin/godot`, verified) — no display, no server. Part 2's matrix M1–M9 need a display and a game server — run manually or in a headed session (M10 is Part 1 Task 10's headless gate, no server).

---

## APIs verified (citations)

- `BaseWindow.RepositionForCurrentCanvas()` — `Scripts/UI/BaseWindow.cs:88` — public, idempotent, "safe to call any time after `_Ready`"; first-run-dialog centers, else `WindowPlacement.Resolve(storedOrDefaultPos, Size, savedCanvas, currentCanvas)`. Reads `Size` at call time → **must run after geometry is applied** (that ordering is why the apply pass is fonts → geometry → reposition).
- `GameManager.OnWindowResized` — `Scripts/GameManager.cs:337-345` — existing live-resize precedent: walks `UiLayer`, calls `RepositionForCurrentCanvas()` on every `BaseWindow` via `CollectBaseWindows` (`Scripts/GameManager.cs:347-356`).
- `EnsureHud()` (`Scripts/GameManager.cs:323`) has exactly **one call site**: `MapManager._Ready` (`Scripts/MapManager.cs:93`), a server-driven map transition — at startup (login screen) **no HUD exists**. The applier is created in `GameManager._Ready`, which precedes any window's `_Ready` (windows first build when `EnsureHud` runs). A headless self-test that needs the HUD must call `EnsureHud()` itself (Task 10).
- `CharacterSettings.Options` — `Dictionary<string, object>` with `GetOption<T>(key, default)` / indexer; `IncludeFields` JSON — `Scripts/CharacterSettings.cs:42-67`. Key constants live in `Constants.cs:136` (`public static class Options`). Part 2 will add the two new keys; Part 1 adds **no** persisted state.
- `WindowPlacement.Resolve(savedPos, windowSize, savedCanvas, currentCanvas)` — `Scripts/UI/WindowPlacement.cs:30`; `TitleBarHeight = 24` (`Scripts/UI/WindowPlacement.cs:17`) is the y-containment allowance.
- GodotSharp 4.7.1, verified by **reflection on the actual project DLL** (`/opt/Godot_v4.7.1-stable_mono_linux_x86_64/GodotSharp/Api/Release/GodotSharp.dll`), not docs:
  - `Theme.SetDefaultFontSize(int)` / `GetDefaultFontSize()` — the theme-mutation API.
  - `Control.AddThemeFontSizeOverride(StringName, int)` and `Control.AddThemeConstantOverride(StringName, int)` — set-or-replace; **there is no `SetThemeFontSize`/`SetThemeConstant`** in this binding (the existing codebase already uses `AddThemeFontSizeOverride`).
  - `Control.GetThemeFontSize(StringName, StringName)` / `GetThemeConstant(...)` — effective values (the constants snapshot reads `GetThemeConstant`; the Task 10 audit reads `GetThemeFontSize`).
  - `Control.OffsetLeft/OffsetTop/OffsetRight/OffsetBottom` and `AnchorLeft/Top/Right/Bottom` — all present (reflection). The snapshot records the **offsets** (anchor-relative by construction) and never touches anchors or `Position`/`Size` directly.
  - `Control.HasThemeFontSizeOverride(StringName)` / `HasThemeConstantOverride(StringName)` — override-presence queries; the font audit (Task 10) uses these to find ANY font-override control under `UiLayer` and demand registry membership.
  - `Control.SetMeta(StringName, Variant)` / `GetMeta(...)` — the skip-meta mechanism.
  - `Node.TreeExited` event — deregistration hook.
  - `OS.GetCmdlineUserArgs()` (`string[]`) — project args after `--`; **`GD.GetCommandLineArgs` does not exist**. Task 10's flag comes from here.
  - Anchored roots in the wild: `Scenes/UI/ChatWindow.tscn:11-14` (bottom-left, `offset_top = -213`), `Scenes/UI/Toolbar.tscn` root bottom-right (all four anchors `1.0`). Offsets scale correctly under either; `Position` would not.
- Test project (`tests/Goose2Client.Tests`) pins **GodotSharp 4.6.2** from NuGet (not the 4.7.1 in the engine dir) — the xUnit surface (`UiScale`, `WindowPlacement` param) must stay 4.6.2-compatible; `Vector2I`/`MathF` are fine (existing `WindowPlacementTests` already use GodotSharp).
- `theme_override_font_sizes` occurrences to migrate: `Scenes/UI/BankWindow.tscn:43` (9), `Scenes/UI/ChatWindow.tscn:47` (`normal_font_size` 12, RichTextLabel), `Scenes/UI/ChatWindow.tscn:55` (12), `Scenes/UI/DebugWindow.tscn:23,32` (12), `Scenes/UI/VendorWindow.tscn:43` (10). Raw C# overrides: `Scripts/UI/BaseMultipleWindow.cs:60` (12) and `:68` (10). (Bridge files `ChatBubble`/`BattleTextLine`/`BridgedNameLabel` are world-space — **do not touch**.)
- Window geometry is `.tscn` pixel-offset layout (e.g. `Scenes/UI/VitalsWindow.tscn` all `layout_mode = 0` offsets; `Scenes/UI/BaseWindow.tscn` anchor-based children). Slots: `Scenes/UI/ItemSlot.tscn` `custom_minimum_size = Vector2(32, 32)`.
- Headless runner: `/usr/local/bin/godot --headless` (4.7.1 mono). Existing probes `tools/tests/*.gd` intentionally do NOT execute C#; Part 1's C#-executing proof is a project-argument self-test (Task 10), run as `godot --headless -- +selftest=ui_scale`.

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

**Step 3:** Implement `Scripts/UiScale.cs`. Use explicit half-away-from-zero rounding (Godot's `Mathf.Round` is not allowed — this file is Godot-free): `int MathF.Round(x, MidpointRounding.AwayFromZero)`. `NormalizeFactor`: `if (float.IsNaN(raw)) raw = MinFactor; snapped = MathF.Round(raw / Step, MidpointRounding.AwayFromZero) * Step; return clamp to [MinFactor, MaxFactor]`. `AutoFactor(h)`: `h < 1080 ? 1 : h < 1440 ? 2 : 3` (clamped by construction).

**Step 4 (green):** all pass. **Step 5:** commit `feat: add UiScale pure scale math`.

| Invariant | Proved by |
|-----------|-----------|
| Corrupt/NaN values normalize into range | `NormalizeFactor_SnapsToHalfStepsAndClamps`, `NormalizeFactor_RejectsNaN` |
| 1.5-step slider value 3.4 can't leak through | `NormalizeFactor_SnapsToHalfStepsAndClamps` |
| Rounding is deterministic, not engine-dependent | `ScaleSize_RoundsHalfAwayFromZero` |
| Build-time geometry is the 1× base; a runtime-spawned window under 2× scales correctly (the adversarial leg the headless factor-1 bias can't fake) | Task 10 step 2b (in-engine) |

---

### Task 2: `WindowPlacement` — saved-quad placement model (`ResolveScaled`) + margin-scaling policy

**The model (review finding — capturing only the pre-commit size cannot round-trip):** a scale commit must re-derive every window's position from a **stable source**, because the persisted position goes STALE after any commit (nothing writes it back during apply). With only a captured old size, the 1×→2×→1× trace for the hotbar (`(520, 679)`, 351×36, 5px bottom margin, `DefaultWindowLayout.cs:14`) fails on the way back: the persisted Y is still 679 but the captured height is now 72 → `right = 720 − (679+72) = −31` → no round-trip. **Chosen model: persist a per-window QUAD — (position, size, factor, canvas) — and derive ALL placement from the quad + the current (Size, factor, canvas).** The quad changes only when the user ends a window drag. Because the quad is invariant across scale commits, commits round-trip exactly by construction, and canvas changes (resize, auto-threshold crossing) compose with factor changes in one call. **Margin policy (resolves the anchored-vs-persisted inconsistency): edge margins are LOGICAL UI PIXELS — they scale with the factor** (× `factor/savedFactor`, half-away-from-zero). Anchored roots (chat) already do this by construction (tscn offsets double at 2×); persisted windows now match. Middle-band windows keep their saved coordinate (unscaled) — the coordinate is the semantic there.

**Files:**
- Modify: `Scripts/UI/WindowPlacement.cs`
- Modify: `Scripts/CharacterSettings.cs` — the window-settings record (returned by `GetWindowSettings`, `CharacterSettings.cs:144`) gains ADDITIVE fields `Size` (Vector2, display px at save time) and `Factor` (float); missing keys → null → caller fallback (Task 5). JSON keys `size`/`factor` per window section.
- Test: `tests/Goose2Client.Tests/WindowPlacementTests.cs` (extend)

**New API** (old 4-arg form delegates — the delegation is mathematically IDENTICAL for `savedFactor == factor == 1`, `savedSize == windowSize`, so all 340 baseline tests stay green untouched):
```csharp
public static Vector2 Resolve(Vector2 savedPos, Vector2 windowSize, Vector2I savedCanvas, Vector2I currentCanvas)
    => ResolveScaled(savedPos, windowSize, 1f, savedCanvas, windowSize, 1f, currentCanvas, TitleBarHeight);

public static Vector2 ResolveScaled(Vector2 savedPos, Vector2 savedSize, float savedFactor, Vector2I savedCanvas,
    Vector2 windowSize, float factor, Vector2I currentCanvas, int titleBarAllowance = TitleBarHeight)
```
`ResolveAxisScaled(saved, savedSize, size, marginScale, savedEdge, currentEdge)` with `marginScale = factor / savedFactor` — same branch structure as today's `ResolveAxis` (`Scripts/UI/WindowPlacement.cs:46-62`), with the two deltas: (a) the trailing-edge offset is derived with **`savedSize`**: `right = savedEdge − (saved + savedSize)` (the band test already uses the saved size — unchanged); (b) **re-sticks scale the margin**: leading → `left × marginScale`; trailing → `currentEdge − size − MathF.Round(right × marginScale, MidpointRounding.AwayFromZero)`; band-keep and equidistant keep return `saved` unscaled. Containment clamp unchanged: `x ∈ [0, max(0, cur.X − windowSize.X)]`, `y ∈ [0, max(0, cur.Y − titleBarAllowance)]` (allowance scaled at the call site — `BaseWindow` passes `applier.ScaleSize(24)`, Task 5). Guard `savedFactor <= 0` → treat as 1 (corrupt settings).

**Step 1: Failing tests** (pure — the tiny headless canvas can't express margins, so the whole model is pinned here):
- `Resolve_DelegatesToResolveScaled`: the 4-arg form equals `ResolveScaled` with `savedSize == windowSize, factors 1` across a position/size/canvas sample (identity for the pre-feature path).
- `ResolveScaled_HotbarMarginScalesWithFactor` (real default `(520, 679)`, 351×36, f1, C720): at `windowSize (702, 72), factor 2, C720` → `(520, 720 − 72 − 10 == 638)` (margin 5×2; x middle-band in saved space — 520/409 ≥ 320 — keeps 520).
- `ResolveScaled_ScaleCommitRoundTrips` (the F1 trace, pinned): same quad, forward `(702,72)@2` → `(520, 638)`; backward `(351,36)@1` → **exactly `(520, 679)`** (the quad never changes across commits — this is why the model round-trips; a capture-only model cannot satisfy this). Also 1×→1.5×→1×: forward y `720 − 54 − 8 == 658` (5×1.5=7.5→8), backward `(520, 679)`.
- `ResolveScaled_DragAtScale_PersistsScaledQuad`: quad `((300, 600), (702, 72), 2, C720)` (user dragged near the right edge at 2×), commit to 1× `(351,36)@1` → x: right offset in saved space `1280 − (300+702) == 278`, not in-band (300 < 320) → `1280 − 351 − 139 == 790`; y: right offset `720 − (600+72) == −52 < 0` → re-stick → `720 − 36 − Round(−26) == 710` → clamped to `696` → result `(790, 696)`.
- `ResolveScaled_MarginClampsWhenScaledWindowExceedsCanvas`: quad `((520,679),(351,36),1,C720)`, `windowSize (702, 300), factor 2`, canvas `1280×200` → y `== 0` (scaled margin lost to clamp, title bar reachable), x `== 520`.
- `ResolveScaled_TitleBarAllowance_Scaled`: quad `((100,700),(100,100),1,C720)`, `windowSize (100,100)@1` → y `696`; with `titleBarAllowance: 48` → y `672`.
- `ResolveScaled_CorruptSavedFactorFallsBackTo1`: `savedFactor 0`/`-1` behaves like 1 on a sample.

**Step 2 (red):** `ResolveScaled` doesn't compile (red).
**Step 3:** implement `ResolveScaled` + `ResolveAxisScaled`; re-implement the 4-arg as the delegation; keep `Center`, `LegacyCanvas`, `TitleBarHeight` untouched.
**Step 4 (green):** full `WindowPlacementTests` (existing 14 tests MUST stay green unmodified + new).
**Step 5:** commit `feat: saved-quad placement model (ResolveScaled) with factor-scaled edge margins`.

**Mutation impact (spans Tasks 3/5, pinned here for traceability):** every production placement site (registration reposition, scale commit, canvas-resize walk) converges on ONE method — `BaseWindow.RepositionFromSaved()` (Task 5) — which reads its quad and calls `ResolveScaled` with its live `Size`, the live factor, and the live canvas. No live-rect capture, no previous-canvas tracking, no per-path size bookkeeping.

---

### Task 3: `UiScaleApplier` (plain class, single apply pass) + font registry

**Files:**
- Create: `Scripts/UiScaleApplier.cs`
- Modify: `Scripts/GameManager.cs` (create/`Instance` + initial settings-independent `Apply` in `_Ready` — `CharacterSettings` does NOT exist yet there: it is created only in `LoadSettings`, called from `LoginScene.cs:103` on successful login; the settings-driven re-`Apply` is Part 2's `LoadSettings` hook. Windows first build later, at `EnsureHud()` from `MapManager` (post-login, settings exist).)

**Shape** (no `Node` base — plain class; Godot types only for theme/control args):

```csharp
public class UiScaleApplier
{
    public static UiScaleApplier Instance { get; internal set; }
    public UiScale Scale { get; }
    public Theme Theme { get; }

    private readonly List<WindowRegistration> _windows = new();
    private readonly List<(Control C, StringName Prop, float Base)> _fonts = new();

    public WindowRegistration RegisterWindow(IScalableWindow w);
    public void UnregisterWindow(IScalableWindow w);
    public void ApplyFontSize(Control c, float basePx, StringName prop = "font_size");
    public void Apply(float factor, ApplyReason reason);

    public float Factor => Scale.CurrentFactor;
    public int ScaleSize(float basePx) => Scale.ScaleSize(basePx);
    public IReadOnlyList<WindowRegistration> RegisteredWindows { get; }
    public bool TryGetFontBase(Control c, StringName prop, out float basePx);
}
public enum ApplyReason { Startup, UserCommit, AutoResize }
```
`Instance` has an `internal set` (set by `GameManager._Ready`, same assembly — NOT `private set`, which GameManager can't call). `Theme` is `GD.Load<Theme>("res://Assets/UI/GameTheme.tres")` — C# has NO `Preload<T>`; `GD.Load` is the API. `_fonts` is FLAT with ownership by ancestry at unregister time (NOT by registration — review finding: `ApplyFontSize` runs during the window's `_Ready`, BEFORE its own registration, so "the most recent registration" would be the PREVIOUS window). ``RegisterWindow` is bookkeeping ONLY (duplicate-safe) and rejects non-`Control` `IScalableWindow`s outright — `ControlRef` is non-null by contract (a stored null would be used as a dictionary key), and the initial Relayout is owned by the window's `ScaleRegister` (Task 5), not here. The read-only seams (`RegisteredWindows`, `TryGetFontBase`, `Theme`) are public for the Task 10 self-test — the applier surface is already public, and this repo has no InternalsVisibleTo infra.

`WindowRegistration` (nested public): `IScalableWindow Window`, `Control ControlRef` — **non-null by construction**: `RegisterWindow` does `if (w is not Control c) return w;` (reject) before storing. NO per-registration font list — fonts live only in the flat `_fonts`; `UnregisterWindow` walks `_fonts` and removes entries where `c == root || root.IsAncestorOf(c)`. A control whose window never registered still lands in `_fonts` only — pruned by validity in `Apply`. `IScalableWindow` (`Scripts/IScalableWindow.cs`): `void Relayout();`

**`Apply(factor, reason)` exact sequence (ordering is load-bearing):**
1. `var f = UiScale.NormalizeFactor(factor)` (pure; NaN-safe per Task 1); `if (f == Scale.CurrentFactor && _appliedOnce) return;` — the first `Apply` always runs (startup). Set `Scale.CurrentFactor = f` — **the applier is the only writer** (Task 1 semantics).
2. **(Part 2 Task 2 lands here — before hiding tooltips):** cancel any in-progress window move-drag via `BaseWindow.CancelDrag()` on every registered `BaseWindow` (cast; non-`BaseWindow` roots skip).
3. HIDE live tooltips: `if (TooltipManager.Instance != null) TooltipManager.Instance.HideAll();` — add `HideAll()` to `TooltipManager` (sets `Visible = false` on the four tooltip controls; mirrors existing `Hide*Tooltip`, `Scripts/UI/TooltipManager.cs:37-60`). (R2)
4. Theme: `Theme.SetDefaultFontSize(ScaleSize(10))` (base 10 = `GameTheme.tres` `default_font_size`; `Theme` is the `GD.Load`ed cached field from the API block).
5. **Prune first, then apply (review finding: removal during `foreach` throws)** — `var invalid = _fonts.Where(e => !GodotObject.IsInstanceValid(e.C)).Select(e => e.C).Distinct().ToList();` then `_fonts.RemoveAll(...)` for them, and `_windows.RemoveAll(r => !GodotObject.IsInstanceValid(r.ControlRef))`; THEN apply: for every `(c, prop, base)` left, `if (c.IsInsideTree()) c.AddThemeFontSizeOverride(prop, ScaleSize(base))`. (Normal teardown is `tree_exited` → `UnregisterWindow` + ancestry font prune; the validity prune is the backstop for anything that leaks past it.)
6. Geometry: `foreach (var r in _windows) r.Window.Relayout();` — geometry ONLY (Task 5's `Relayout` never repositions; placement is a separate, later step).
7. Placement: `foreach (var r in _windows) if (r.Window is BaseWindow bw) bw.RepositionFromSaved();` — each window derives its position from its OWN persisted quad (Task 2: saved pos/size/factor/canvas + live Size/factor/canvas). No size capture needed anywhere — the quad is the source of scale space, so 1×→2×→1× round-trips exactly (Task 2's `ResolveScaled_ScaleCommitRoundTrips`). Non-`BaseWindow` roots (Task 6) have no reposition (anchor-stuck).

**Publication boundary (registry):**
- Creation order (single Relayout owner — review finding): window builds nodes (at 1× base constants) → **`ScaleRegister()`** (Task 5) does snapshot → `RegisterWindow(this)` (bookkeeping) → `Relayout()` once: build-time geometry is the 1× base, so a window spawned at runtime under a 2× factor lands at 2× in the same frame (zero 1× frames); at 1× the call is a no-op re-apply. `RegisterWindow` itself never calls `Relayout` (the old draft gave both owners — one of them had to go).
- Teardown: `UnregisterWindow` on `tree_exited` (connected at registration) removes the window AND its descendant font entries (review finding: fonts are separate controls — there is no "fonts clean themselves up" path; the ancestry prune in `UnregisterWindow` IS that path). `GameHud` is never freed/rebuilt (guarded `EnsureHud`, no free path) — there is no rebuild-clear; the `IsInstanceValid` prune (step 5) is the backstop for anything that leaks past `tree_exited`.
- Readers (the apply pass) can only ever see a fully-built, already-laid-out window: registration happens at END of `_Ready` (R1 snapshot also taken there), and `Apply` never runs during a `_Ready` (startup Apply precedes HUD build; user commits happen on input events between frames).
- Failure behavior: `RegisterWindow` with a null/duplicate is a no-op; no partial state.

**`ApplyFontSize` contract:** sets the override immediately (build-time correctness) AND records the entry. It is the ONLY place window code may set a font-size theme override (raw `AddThemeFontSizeOverride` calls in window code are prohibited — Task 9 enforces the migration; the self-test in Task 10 is the tripwire).

**GameManager wiring:** in `_Ready` — **before `CharacterSettings` exists** (it is created only on successful login, `LoginScene.cs:103` → `LoadSettings`), so the startup `Apply` must be settings-independent. Use the settings-free best guess (Auto): the login screen (Part 2 registers it) is already at the right factor for Auto users, and at the tiny headless root size (~64–100 px, NOT the project's 1280×720 — Part 1 Task 10 step 0) `AutoFactor(small) == 1`; Part 1's self-test additionally forces `Apply(1f, …)` explicitly (Task 10), so determinism does not depend on the headless size at all:
```csharp
UiScaleApplier.Instance = new UiScaleApplier();
var canvas = (Vector2I)GetTree().Root.GetVisibleRect().Size;
UiScaleApplier.Instance.Apply(UiScale.AutoFactor(canvas.Y), ApplyReason.Startup);
```
(`UiScale.AutoFactor` is STATIC — call it through the type, never `applier.Scale.AutoFactor(...)`: C# rejects static access through an instance with CS0176.)
Part 1 reads no settings (the options keys are Part 2); Part 2 adds the settings-driven re-`Apply` in `LoadSettings` (runs post-login, pre-HUD — still before any map entry, so no unscaled flash) and sets `applier.Mode`.

**Step ordering / tests:** this task has no pure-xUnit surface (Godot types). Proof comes from Task 10's self-test (audit + smoke) and Task 5/9's 1× no-op run. Compile + existing test suite green is the gate here.
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
        Control C,
        float OLeft, float OTop, float ORight, float OBottom,
        bool ContainerManaged,
        Vector2 MinSize, bool HasMinSize,
        (StringName Name, int Value)[] Constants);

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
- **1× bit-identity is asserted after one `ProcessFrame`** (queued container layouts flush), not in the same tick — Task 10's comparison follows this.

**`Apply(records, factor)`:** for each record, if `GodotObject.IsInstanceValid(c) && c.IsInsideTree()`: **offsets — SKIP entirely when `ContainerManaged`** (the container owns them); otherwise if `factor == 1f` → write back the recorded raw floats verbatim (`c.OffsetLeft = c.oLeft`, …) — the bit-identical 1× path (see postconditions, `BaseMultipleWindow.cs`'s `11.18f` pitch); else → `c.OffsetLeft = int(round(c.oLeft × f))` (same for the other three, per-value `MathF.Round(x, AwayFromZero)`); min-size via `c.CustomMinimumSize` (all records, container-managed or not); constants via `c.AddThemeConstantOverride(name, scaled)` (raw-write at 1× too). **This file contains NO Godot-free math of its own** — it takes the factor as `float` and does explicit `MathF.Round(x, AwayFromZero)` on plain floats (mirroring Task 1's policy; `UiScale`'s instance isn't reachable from a static helper without a parameter, and passing the factor is the seam).

**Flags:** anchor-attached controls (e.g. `CloseButton` at `offset_left = -18` in `BaseWindow.tscn`) scale their offsets correctly with anchors untouched; `ChatWindow`'s bottom-left root (`offset_top = -213`) stays edge-stuck because its negative offsets simply double.

**Proof:** no direct unit test (needs a live tree) — proven by Task 10: (a) 1× no-op run (snapshot→apply at factor 1 must leave every rect bit-identical, **compared after one `ProcessFrame`** so queued container layouts have flushed — both the captured baseline and the post-Relayout read), (b) factor-2 audit (sampled values equal `round(base×2)`: `VitalsWindow` root size 183×55→366×110, an `ItemSlot` min-size 32→64, `ChatWindow` root offsets `−213/−5`→`−426/−10`), (c) the runtime-spawn leg (Task 10 step 2b) — the adversarial proof that build-time geometry is the base at a live non-1 factor.
**Commit:** `feat: add UiScaleLayout geometry snapshot`.

---

### Task 5: `BaseWindow` registration, `Relayout`, factor-aware reposition

**Files:**
- Modify: `Scripts/UI/BaseWindow.cs`
- Modify: `Scripts/UI/BaseMultipleWindow.cs:46-77` (font + line positions)
- Modify: `Scripts/IScalableWindow.cs` (if not created in Task 3 — created there)

**WHY THE SNAPSHOT MUST RUN AT END OF THE SUBCLASS `_Ready` (plan-review finding — the original "end of `BaseWindow._Ready`" timing was broken):** every window subclass calls `base._Ready()` FIRST and builds its runtime content AFTER it — the `ItemSlot` grids (`InventoryWindow.cs:25-40`: slots instantiated in `_Ready` after `base._Ready()`, none are in the `.tscn`), the 20 NPC line labels (`BaseMultipleWindow.cs:43,63-71`), hotbar pages, equipment grids. A snapshot at the end of `BaseWindow._Ready` would miss all of it → at live 2× the window frame scales but its content stays 1×. **Fix — `ScaleRegister()` pattern (no reordering of `base._Ready()`, which would flip `Visible`-restore ordering for toggle windows):**
- `BaseWindow` gains `protected void ScaleRegister()` — the SINGLE placement+scale owner at registration (review finding: the old draft placed in `_Ready` with the 1× tscn size and then scaled geometry WITHOUT repositioning, so right/bottom-anchored windows grew past their placement at a 2× startup):
  ```csharp
  protected void ScaleRegister()
  {
      if (_scaleRegistered) return;
      _scaleRegistered = true;
      _geom = UiScaleLayout.Snapshot(this);
      var applier = UiScaleApplier.Instance;
      applier.RegisterWindow(this);
      Relayout();
      RepositionFromSaved();
      TreeExited += () => applier.UnregisterWindow(this);
  }
  ```
  Order: snapshot (tscn/1× geometry is the base) → register → `Relayout()` (geometry at the live factor — at a 2× startup the frame is NOW its final size) → `RepositionFromSaved()` (placement against the FINAL size, Task 2's quad model) → teardown hook. Both mutations happen inside `_Ready`, before the first render — no 1× frame, no wrong-placement frame.
- `BaseWindow._Ready()`: the placement call is REMOVED (the `RepositionForCurrentCanvas();` line ~54 — its logic moves into `RepositionFromSaved`, called from `ScaleRegister` AFTER relayout). Everything else stays in order/behavior — the `Visible`-restore (`ws.Visible`) and `MoveChild` are untouched, and `private Vector2 _tscnSize = Size;` is captured at the TOP of `_Ready` (Size is still the tscn size — relayout has not run yet); `_tscnSize` is the saved-size fallback for legacy settings (Task 2). At its very end (after `MoveChild`, `BaseWindow.cs:76`) it schedules the fallback `CallDeferred(nameof(ScaleRegister));` (concrete Godot C# form — `CallDeferred` takes a method-name string; do NOT write `CallDeferred(ScaleRegister)`) — covers `BaseWindow` subclasses that do NOT override `_Ready` (InfoWindow, QuestWindow: registration lands one frame later; both are server-driven dialogs, no perceptible flash).
- **Each of the 9 window subclasses that override `_Ready` gets ONE line at the end of it: `ScaleRegister();`** — `BankWindow`, `BaseMultipleWindow`, `CharacterWindow`, `CombineBagContainerWindow`, `HotbarWindow`, `InventoryWindow`, `OptionsWindow`, `SpellbookWindow`, `VendorWindow`. (Verified audit: no subclass build code reads the `Content`/`TitleLabel`/`Background` properties — all use their own `GetNode` calls — so no reordering was needed; `BaseMultipleWindowManager` does not call `base._Ready()` and is not an on-screen window — leave it.)
- **Invariant:** no code after the `ScaleRegister()` call may set geometry on `this` or descendants directly — the snapshot is the base and the next `Apply` clobbers any direct write. (Build code BEFORE the call uses 1× base constants; that geometry becomes part of the base.)
- `BaseWindow` fields: `private List<GeomRecord> _geom = null!;   // NOT readonly — assigned by ScaleRegister(); C# forbids readonly assignment outside constructors`; `private bool _scaleRegistered;`; `private readonly Vector2 _tscnSize;` (assigned at top of `_Ready`).
- `public void Relayout()` — geometry ONLY (placement is a separate step; the applier calls it then `RepositionFromSaved`, Task 3 step 6/7):
  ```csharp
  UiScaleLayout.Apply(_geom, UiScaleApplier.Instance.Factor);
  ```
- `RepositionForCurrentCanvas()` (lines 90-109) is REPLACED by `RepositionFromSaved()` (parameterless — the quad needs no arguments; this also dissolves the old draft's signature-vs-`_Ready` conflict):
  ```csharp
  public void RepositionFromSaved()
  {
      if (!IsInsideTree()) return;
      var canvas = (Vector2I)GetTree().Root.GetVisibleRect().Size;
      var ws = GameManager.Instance?.CharacterSettings?.GetWindowSettings(WindowName);
      if (ws == null && DefaultWindowLayout.IsDialog(WindowName))
      {
          Position = WindowPlacement.Center(canvas, Size);
          return;
      }
      var pos = ws != null ? ws.Position : DefaultWindowLayout.For(WindowName);
      var savedCanvas = ws != null && ws.CanvasSize != default ? ws.CanvasSize : WindowPlacement.LegacyCanvas;
      var savedSize = ws != null && ws.Size != default ? ws.Size : _tscnSize;
      var savedFactor = ws != null && ws.Factor > 0f ? ws.Factor : 1f;
      var applier = UiScaleApplier.Instance;
      Position = WindowPlacement.ResolveScaled(pos, savedSize, savedFactor, savedCanvas, Size,
          applier != null ? applier.Factor : 1f, canvas,
          applier != null ? applier.ScaleSize(24f) : WindowPlacement.TitleBarHeight);
  }
  ```
  Legacy settings (no `Size`/`Factor` keys — everything shipped to date): `savedSize = _tscnSize`, `savedFactor = 1` — exactly the true saved pair (pre-feature saves were 1× tscn-size positions), so old files place identically to today. Callers: `ScaleRegister` (registration, post-relayout), the applier's placement step (every commit), and `GameManager`'s canvas-resize walk (`CollectBaseWindows` → `RepositionFromSaved` — the walk keeps its shape, one method renamed). Because the quad is invariant across commits, the walk, commits, and registration all compose: no previous-canvas tracking, no size capture (review findings on stale captures).
- **Drag-end save persists the full quad** — both `SetWindowSetting` call sites in `OnTitleBarGuiInput` (the `GuiInput` release and the `MouseMotion` escape) pass the new args: `SetWindowSetting(WindowName, Position, Visible, canvas, Size, UiScaleApplier.Instance != null ? UiScaleApplier.Instance.Factor : 1f)`; `CharacterSettings.SetWindowSetting` (the 4-arg overload, `CharacterSettings.cs:179`) gains optional `Vector2? size = null, float? factor = null` writing `settings.Size`/`settings.Factor` (fields added in Task 2). The Part 2 drag-cancel flag suppresses BOTH call sites exactly as planned — a cancelled drag persists no quad.

**`BaseMultipleWindow` (fonts only — no relayout helper needed):**
- All 20 line labels are created in `_Ready` (fixed `LineCount = 20`, lines 63-71) — BEFORE the end-of-`_Ready` `ScaleRegister()` — so the generic snapshot captures their 1× positions (`LinesOrigin` (6,22) + `i × LineRowHeight` (11.18) stay as base constants, exactly as written today) and `Relayout` scales them from the records. There is NO `RelayoutLines()` helper in this design.
- Fonts: line 60 `b.AddThemeFontSizeOverride("font_size", ButtonFontSize)` → `applier.ApplyFontSize(b, ButtonFontSize)`; line 68 `label.AddThemeFontSizeOverride("font_size", LineFontSize)` → `applier.ApplyFontSize(label, LineFontSize)`.

**Other `BaseWindow` subclasses:** covered by the 9-file one-liner list above. If any subclass sets geometry in `_Ready` AFTER the `ScaleRegister()` line (or packet-driven geometry after registration), the next `Apply` clobbers it — the 2× audit (Task 10) catches this; treat such failures as "register after build", not as scaler bugs.

**Gate:** full xUnit suite green; compile clean; Task 10's 2× audit proves the runtime-built slot grids actually scaled (the leg that fails if the end-of-`_Ready` timing regresses).
**Commit:** `feat: BaseWindow self-registration and factor-aware relayout`.

---

### Task 6: non-`BaseWindow` roots (Vitals, Chat, Party, Debug, BuffEffects, Toolbar, TooltipManager)

**Files:**
- Modify: `Scripts/UI/VitalsWindow.cs:33` (`_Ready`)
- Create: `Scripts/UI/Toolbar.cs` + attach to `Scenes/UI/Toolbar.tscn` root — the `HBoxContainer` root is **scriptless** (scripts live only on `DestroyButton`/`ToolbarItem` children); a new root script implements the registration pattern. The root is bottom-right anchored (all four anchors `1.0`), so offset scaling is what keeps it stuck.
- Modify: `Scripts/UI/ChatWindow.cs`, `Scripts/UI/PartyWindow.cs`, `Scripts/UI/DebugWindow.cs`, `Scripts/UI/BuffEffectsWindow.cs` (`_Ready` each — **all five are plain `Control` roots, verified**, and Chat/Party/Debug/BuffEffects would otherwise never scale)
- Modify: `Scripts/UI/TooltipManager.cs:17` (root + `HideAll()` added in Task 3)
- Modify: `Scenes/UI/Tooltips.tscn` (set `meta/ui_scale_skip = true` on the four tooltip control nodes `ItemTooltip`/`SpellTooltip`/`TextTooltip`/`MapItemTooltip` — their geometry is set dynamically per-show in C#; the skip meta keeps the SNAPSHOT away from them. Their factor-aware layout is Task 7 — this task only makes the snapshot ignore them)

**Pattern for each root** (plain-`Control` windows): same as Task 5 minus reposition:
```csharp
var applier = UiScaleApplier.Instance;
_geom = UiScaleLayout.Snapshot(this);
applier.RegisterWindow(this);
Relayout();
TreeExited += () => applier.UnregisterWindow(this);
```
(`Relayout()` is `UiScaleLayout.Apply(_geom, applier.Factor);` — same as Task 5 minus the `RepositionFromSaved` call, which plain-`Control` roots never do.)
- `VitalsWindow` (root at tscn `(8,8)`, 183×55): registers; its root position scales via geometry (intended). Its dynamic portrait path is Task 10 (this task only registers the root).
- `ChatWindow` (bottom-left-anchored root, offsets `8, −213, 508, −5`): registers; offset scaling doubles the margins and keeps it edge-stuck — NO `RepositionForCurrentCanvas`-style reposition (it has none; its placement IS the anchor).
- `PartyWindow`, `DebugWindow`, `BuffEffectsWindow`: register (top-left-anchored tscn offsets scale).
- `Toolbar`: registers (offsets scale; its Options button wiring is untouched).
- `TooltipManager`: registers ONLY its root + static children; the four dynamic tooltip nodes carry the skip meta so the SNAPSHOT ignores them (their geometry is per-frame C# — scaled by Task 7's per-frame metrics, not by the snapshot).
- `WorldDropTarget` (`Scripts/UI/WorldDropTarget.cs`, full-rect, `MouseFilter.Pass`): **do not register** — full-rect anchors, nothing to scale.

**Gate:** xUnit green; 1× headless run (Task 10 command) shows no layout drift (visual/rect check via the audit's 1× no-op leg).
**Commit:** `feat: register vitals, chat, party, debug, buff-effects, toolbar, tooltip roots with the scale applier`.

---

### Task 7: factor-aware tooltip layout (`TooltipMetrics`)

**The problem (review finding — "hidden on apply" was a scope escape, not a solution):** the four tooltip controls compute their layout every frame from 1× constants (item: 40px text column right of the 32px icon, 9px right pad, header block to y≈46, stats from y=48, +4 bottom; spell/text: label min + (8,4); map-item: 6/4/2/4 margins + 400px label widths; item icons sit at tscn offsets 4–36). Fonts scale via the project theme but this layout would not — a 2× tooltip = 2× fonts in a 1× box.

**Files:**
- Create: `Scripts/TooltipMetrics.cs` (pure, Godot-free)
- Create: `tests/Goose2Client.Tests/TooltipMetricsTests.cs`
- Modify: `Scripts/UI/ItemTooltipControl.cs` (`_Process` per-frame layout — the 1× constants above), `Scripts/UI/SpellTooltipControl.cs`, `Scripts/UI/TextTooltipControl.cs`, `Scripts/UI/MapItemTooltipControl.cs`

**Spec:**
- `TooltipMetrics` (pure): `ItemMetrics(float factor)` → `(TextColumn, RightPad, HeaderTop, StatsTop, ExtraBottom, IconSize, IconOffset)`, `TextPad(float factor)` → `(w, h)`, `MapItemMetrics(float factor)` → `(LeftMargin, TopMargin, RowGap, BottomMargin, LabelWidth)` — each value `UiScale.ScaleSize`-scaled from the cited 1× base (the 1× row of the table MUST equal today's literals), half-away-from-zero rounding. A shared static instance (or `ScaleSize` via `UiScaleApplier.Instance.Scale`) is the only scaling entry — controls never hand-multiply.
- Each control's `_Process` reads the metrics EVERY frame from `UiScaleApplier.Instance` (the factor can change between shows; the per-frame read IS the live mechanism — no snapshot involved). The item tooltip's icon `TextureRect` gets offset/size set per-show from `ItemMetrics.IconOffset/IconSize` (replacing the tscn 4–36 offsets). Viewport clamping (`PositionTooltip`) is untouched — it uses live `Size`, which is now scaled, so clamps stay correct.
- Live tooltips still HIDE on commit (Task 3 step 3, R2) so no per-frame reflow is needed mid-commit; on next hover they rebuild at the live factor.

**Step 1 (xUnit, red):** `TooltipMetricsTests` — full constant table at 1× (every field == today's literal), 1.5×, 2×, and 1×→2×→1× round-trip per control. **Step 2:** implement the metrics + rewire the four `_Process` bodies. **Step 3 (green):** suite + headless leg in Task 10: show the SPELL tooltip (simplest — one label) over a visible parent via its public show API, two `ProcessFrame`s, assert `Size == labelMin + (ScaleSize(8), ScaleSize(4))` at 2× and the y-clamp `y + size.Y <= canvas.Y`; if the spell tooltip cannot be shown headless (no suitable parent), the leg degrades to: pure-table xUnit + the Part 2 manual matrix M7 (item tooltip at 3×) — state which one landed in the commit message.
**Commit:** `feat: factor-aware per-frame tooltip layout (TooltipMetrics)`.

---

### Task 10: vitals portrait scaling (`VitalsPortraitMetrics`)

**The problem (review finding):** `VitalsCharacterDisplay.Refresh()` → `SetLayer` writes each portrait `TextureRect`'s `Size`/`Position` from fixed 1× constants (`PortraitSize = 53f`, `HeadDropPixels = 20f`, `PortraitZoom = 1.25f`, `VitalsCharacterDisplay.cs:47-80`) AFTER the window snapshot — at 2× the frame grows but the next character update repaints the portrait at 1×. (Correction to the earlier draft: the parent `VitalsWindow` does NOT hold the layer graphic IDs — `Refresh()` queries `CurrentMapManager.LocalPlayer`'s appearance on every call, `VitalsCharacterDisplay.cs:22-36`; the re-layout path must re-query the same way, not replay cached IDs.)

**Files:**
- Create: `Scripts/VitalsPortraitMetrics.cs` (pure, Godot-free)
- Create: `tests/Goose2Client.Tests/VitalsPortraitMetricsTests.cs`
- Modify: `Scripts/UI/VitalsCharacterDisplay.cs` (`SetLayer` + new `Relayout`)
- Modify: `Scripts/UI/VitalsWindow.cs` (`Relayout` re-runs the portrait pass)

**Spec:**
- (a) Extract the pure math: `VitalsPortraitMetrics.Layout(Vector2 texSize, float factor)` → `(Vector2 rectSize, Vector2 rectPosition)` (draw size `texSize × 1.25 × f`, centered on `53 × f`, drop `20 × f`) — xUnit-pinned at 1×/1.5×/2× + round-trip; the 1× row equals today's literals.
- (b) `SetLayer` reads `UiScaleApplier.Instance.Factor` and uses the metrics.
- (c) `VitalsCharacterDisplay.Relayout()` re-queries the local player's appearance EXACTLY like `Refresh()` does (same `CurrentMapManager?.LocalPlayer` guard; if absent, no-op) and re-runs the per-layer layout for the currently displayed layers — no cached IDs anywhere. `VitalsWindow.Relayout()` calls it after the generic geometry apply.

**Step 1 (xUnit, red):** `VitalsPortraitMetricsTests`. **Step 2:** implement. **Step 3 (green):** suite. The headless self-test (Task 10) cannot assert a live portrait (no character graphics without a server) — the Part 2 manual matrix M1 covers it: portrait fills the scaled circle after a character load at 2×.
**Commit:** `feat: scale the vitals portrait with the UI factor (VitalsPortraitMetrics)`.

---

### Task 9: font-override migration (tscn + raw C# → `ApplyFontSize`)

**Files:**
- Modify: `Scenes/UI/BankWindow.tscn:43`, `Scenes/UI/ChatWindow.tscn:47,55`, `Scenes/UI/DebugWindow.tscn:23,32`, `Scenes/UI/VendorWindow.tscn:43` — **remove** the `theme_override_font_sizes` lines (values migrate to C# as cited base constants).
- Modify: `Scripts/UI/BankWindow.cs`, `Scripts/UI/ChatWindow.cs`, `Scripts/UI/DebugWindow.cs`, `Scripts/UI/VendorWindow.cs` `_Ready`: `applier.ApplyFontSize(<the control>, <base>)` — Chat's RichTextLabel uses `prop: "normal_font_size"`.

**Rules:**
- The base constant in C# must equal the removed tscn value (9/12/12/12/10). At factor 1 the rendered size is unchanged — verified by Task 10's 1× audit.
- Bridge files (`ChatBubble.cs:95`, `BattleTextLine.cs:33`, `BridgedNameLabel.cs:17`) stay on raw overrides — world-space, out of scope.
- After this task, `grep -rn "AddThemeFontSizeOverride\|theme_override_font_sizes" Scenes Scripts | grep -v Overlays` returns only `UiScaleApplier.cs` (the helper). (The three bridge files live in `Scripts/Overlays/` — a different path from the window code — so exclude that directory rather than filename-filtering.) State this grep in the commit message body as the invariant.

**Gate:** grep invariant above; xUnit green.
**Commit:** `refactor: route all root-viewport font overrides through UiScaleApplier.ApplyFontSize`.

---

### Task 10: headless self-test (audit + 1× no-op + 2× smoke)

**Files:**
- Modify: `Scripts/GameManager.cs` (`_Ready`): read `OS.GetCmdlineUserArgs()`; if it contains `"+selftest=ui_scale"`, run the sequence below on the NEXT frame (one `ProcessFrame` await) and `GetTree().Quit(failed ? 1 : 0)`. **The sequence's first steps (review finding F1 — both required, in this order):** (1) `GameManager.Instance.LoadSettings("ui-scale-selftest")` — the HUD does not exist at startup (`EnsureHud`'s only call site is the server-driven map transition, `Scripts/MapManager.cs:93`, which headless never reaches), so the test builds it; BUT `CharacterSettings` is only created by `LoadSettings` (normally via login, `LoginScene.cs:103`), and **every** `BaseWindow._Ready` dereferences it unguarded (`Scripts/UI/BaseWindow.cs:51` — `GameManager.Instance.CharacterSettings.GetWindowSettings(...)`), with further unguarded derefs in `OptionsWindow.cs:24/28/32`, `HotbarWindow.cs:75`, `InventoryWindow.cs:51`, `CharacterWindow.cs:91`, `SpellbookWindow.cs:80` — calling `EnsureHud` first NREs on the first window. `LoadSettings` here creates in-memory defaults; nothing is written to disk (no `Save()` fires — no window close/toggle/drag happens headless). (2) **Force the 1× baseline (review finding — determinism):** `UiScaleApplier.Instance.Apply(1f, ApplyReason.Startup);` — the settings file `user://ui-scale-selftest-settings.json` (`CharacterSettings.cs:69`) MAY exist from an earlier run or be hand-written; once Part 2's `LoadSettings` hook lands it would `Apply` a persisted Manual 2×/3× and invalidate every 1× assertion below. The explicit `Apply(1f, …)` after `LoadSettings` (registry still empty — login is unregistered in Part 1) makes the baseline independent of both the headless canvas size AND any persisted selftest profile. (3) `GameManager.Instance.EnsureHud()` (plus another `ProcessFrame` await).
- Create: `tools/tests/run_ui_scale.sh` — wrapper: `godot --headless -- +selftest=ui_scale; exit $?` (docs the invocation; args after `--` are what `OS.GetCmdlineUserArgs()` returns; the existing probes use `--script`, this one needs the project + C#).

**Sequence (all inside C#, `Print`-labeled steps):**
0. **Pin the headless canvas (review finding F3):** `Print` the root visible-rect size first. Headless does NOT apply the project's 1280×720 window size (probes report ~64–100 px). Every assertion below must therefore use the ACTUAL size read at runtime — never assume 1280×720. (The factor-1 baseline still holds: `AutoFactor(anything < 720) == 1`.)
1. **1× no-op:** for each registered `BaseWindow` (enumerate via `UiScaleApplier.Instance.RegisteredWindows`): `await ProcessFrame` first (flush queued container layouts), snapshot its descendants' `(OffsetLeft, OffsetTop, OffsetRight, OffsetBottom)` into a dict **AND record each window's 1× `Position`** (the step-3 round-trip pin, below), call `w.Relayout()`, **`await ProcessFrame` again**, re-read; assert bit-identical (factor forced to 1 above). The two `ProcessFrame`s matter (review finding): without them a container-managed child's offsets are compared mid-layout-pass and the assertion is flaky. Catches snapshot bugs and any `_Ready` code that sets geometry after the `ScaleRegister()` line.
2. **2× apply:** `UiScaleApplier.Instance.Apply(2f, ApplyReason.UserCommit)`. Then assert:
   - `GameTheme` (the applier's cached instance) `GetDefaultFontSize() == 20` (`Theme` has no `GetThemeFontSize` — `SetDefaultFontSize`/`GetDefaultFontSize` are the API, reflection-verified).
   - **Font audit (adversarial):** walk every `Control` under `UiLayer`; for any with `HasThemeFontSizeOverride("font_size")` or `HasThemeFontSizeOverride("normal_font_size")` (excluding nothing — bridge text lives in the world viewport, not `UiLayer`): the control MUST be in the applier's font registry, and its effective `GetThemeFontSize(prop)` must equal `base × 2`. A raw `AddThemeFontSizeOverride` added outside the registry (e.g. a future PR skipping `ApplyFontSize`) fails here. **Scope note:** the audit walks `UiLayer` only — the login scene (not under `UiLayer`) is outside it; Task 9's source grep plus Part 2 Task 5's login self-test leg cover that surface.
   - Sampled geometry: `Vitals` root `Size == (366, 110)` (tscn 183×55) and `Position == (16, 16)` (tscn 8,8); one `ItemSlot` under Inventory has `CustomMinimumSize == (64, 64)`; `ChatWindow` root offsets `OffsetTop == −426` and `OffsetBottom == −10` (tscn −213/−5 doubled — edge-stick preserved by offset scaling, NO reposition involved; ChatWindow is not a `BaseWindow`); a `BaseWindow`-derived dialog still satisfies `WindowPlacement`'s ACTUAL postcondition (mirror `WindowPlacement.cs` exactly, review finding — the old `canvas.Y - w.Size.Y` bound was wrong: production clamps y with the TITLE-BAR allowance, not the full window height): `0 <= X <= Max(0, canvas.X - w.Size.X)` and `0 <= Y <= Max(0, canvas.Y - applier.ScaleSize(24))` (at 2× that's 48, not 24, and not the window height). The margin-preservation model itself is xUnit-pinned (Task 2) — the tiny headless canvas cannot express margins.
   - All four tooltips hidden (R2).
   - **Tooltip live-size leg (Task 7):** the spell-tooltip show leg — `Size == labelMin + (ScaleSize(8), ScaleSize(4))` at 2× + y-clamp — or its stated fallback (pure-table + M7), whichever landed.
   - **Font registry lookup** uses `applier.TryGetFontBase(c, prop, out base)` + `applier.Theme` (the seams from Task 3) — no reflection, no friend access.
2b. **Runtime-spawn leg (adversarial, THE regression the review caught):** with the HUD now at 2×, instantiate a fresh window (`GD.Load<PackedScene>("res://Scenes/UI/BankWindow.tscn").Instantiate()` — pick any window whose `_Ready` needs no server state; verify at implementation time), `UiLayer.AddChild`, await a frame; assert a sampled rect equals `round(base×2)` (e.g. its `Content` child's offsets doubled). This is the leg that fails if anyone re-introduces divide-by-factor snapshotting or build-time scaling — headless factor-1 bias cannot fake it. `QueueFree()` afterwards.
3. **Back to 1×:** `Apply(1f, UserCommit)`; re-assert the step-1 geometry dict equality (idempotence in both directions — catches records that baked in a scaled base); **AND** assert every registered `BaseWindow`'s `Position` is EXACTLY its step-1 recorded 1× position (review finding F1, pinned end-to-end: the saved-quad model derives both the 2× and the restored 1× position from the SAME invariant quad + canvas + factor, so the round-trip is exact by construction — a capture-only or stale-position model fails this assertion).

**Red/green:** run before Task 5/6 land → expected FAIL (no registered windows / no `IScalableWindow`); after Task 9 → PASS. If it NREs in a window `_Ready` on `CharacterSettings`, the `LoadSettings` step is missing (finding F1); if it fails with "no windows registered", the `EnsureHud()` step is missing — fix the test, not the product code. Final state of this task is the gate for the whole part: `bash tools/tests/run_ui_scale.sh` exits 0 with labeled `OK` lines, no `ERR_`/script-error output.

**Commit:** `test: headless ui-scale self-test (1x no-op, 2x audit, idempotence)`.

---

## Invariant-to-test matrix (part-wide)

| Invariant | Proved by |
|-----------|-----------|
| Factor normalizes all sources (slider, auto, corrupt save) | Task 1 `NormalizeFactor_*` |
| Auto boundaries 720/1080/1440 + 4K clamp | Task 1 `AutoFactor_Boundaries` |
| Rounding policy is pinned, engine-independent | Task 1 `ScaleSize_RoundsHalfAwayFromZero` |
| Build-time geometry is the 1× base — a runtime-spawned window under a live 2× scales correctly (the regression no headless factor-1 test can fake) | Task 10 step 2b (adversarial) + Task 10 step 3 (idempotence) |
| Bottom-anchored root stays edge-stuck after scaling | Task 10 step 2 (ChatWindow offsets `−213/−5` → `−426/−10`) |
| 1× is bit-identical to today (no visual regression at default) | Task 10 steps 1 & 3 |
| Placement survives scaled sizes (middle/edge/oversize) | Task 2 `Resolve_*` tests |
| Edge margins are logical px: they scale with the factor across a commit, and 1×→2×→1× round-trips EXACTLY (saved-quad model) | Task 2 `ResolveScaled_HotbarMarginScalesWithFactor` / `ResolveScaled_ScaleCommitRoundTrips` / `..._MarginClampsWhenScaledWindowExceedsCanvas` |
| Container-managed children: snapshot skips their offsets; final geometry is container-derived and stable | Task 4 `ContainerManaged` rule + Task 10 step 1 (PostFrame compare) |
| Scaled y-containment uses scaled title bar | Task 2 `Resolve_TitleBarAllowance_Scaled` |
| Fonts scale through the registry; raw overrides trip the wire | Task 10 step 2 (audit) + Task 9 grep invariant |
| Geometry scales per window incl. min-sizes | Task 10 step 2 (sampled rects) |
| Tooltips hidden on commit (R2) | Task 10 step 2 |
| Registry stays clean: windows + descendant fonts deregister on `tree_exited`; orphans pruned by `IsInstanceValid` | apply steps 5/7 prune + `UnregisterWindow` ancestry prune (covered by Task 10 clean run across `EnsureHud`) |

**Explicitly deferred to Part 2:** options UI (slider/mode), `Options.UiScaleMode`/`UiScaleValue` persistence + startup read (Part 1's pre-login Auto `Apply(AutoFactor(canvas.Y), Startup)` becomes settings-driven), auto-mode window-resize path (`GameManager.cs:103` `size_changed` handler), login/loading registration (their scenes attach no per-scene theme, but `project.godot:37` sets `theme/custom` project-wide — their text already resolves at `font_size == 10` through the applier's theme, so Part 2 Task 5 is GEOMETRY-ONLY registration; the 10→20→10 round-trip pins it), drag-cancel on commit, manual verification matrix, and the design's accepted-limitations list.
