# UI Scale Part 1B — Core Scaler & Registration

**Part order:** 1A → 1B → 1C → Part 2 (sequential, same worktree/branch; each part is a self-contained execution context with its own task list and commit sequence). **Prereq: Part 1A is merged** (this part calls `UiScale` and `WindowPlacement.ResolveScaled`).

**Goal:** The scale machinery's runtime core: `UiScaleApplier` (the single apply pass + font registry + window registry), the `UiScaleLayout` generic snapshot, and registration for every root-viewport window (`BaseWindow` + non-`BaseWindow` roots) — usable at a pinned 1× today and drivable at any factor for tests, before fonts/tooltips/dynamic surfaces (Part 1C) and the options UI (Part 2) exist.

**Architecture (shared by all parts):** Windows keep their build-time geometry as the 1× base — `.tscn` pixel offsets load at 1× regardless of the active factor, and C# build code writes 1× base constants (it does **not** scale at build time; scaling happens in `Relayout`). A static `UiScaleLayout` helper snapshots each window's descendant geometry at end-of-`_Ready` as-is (anchor-relative offsets) — that snapshot is the base, no division. `ScaleRegister()` (end of each window's `_Ready`) snapshots, registers, relays out, and places — so a window spawned at runtime under a 2× factor scales AND places in the same frame (zero 1× frames). `UiScaleApplier.Apply(factor)` (plain class, `GameManager`-hosted, `TooltipManager.Instance`-style `Instance` accessor) then: normalizes the factor, cancels in-flight window drags (Part 2), hides live tooltips, mutates `GameTheme.default_font_size`, re-applies registered explicit font overrides, calls each registered window's geometry-only `Relayout()`, then every `BaseWindow`'s `RepositionFromSaved()`. Placement is the **saved-quad model** (Part 1A Task 2): each window persists (position, size, factor, canvas) at drag-end, and every placement — registration, scale commit, canvas resize — derives from that quad + the live (Size, factor, canvas) via pure `WindowPlacement.ResolveScaled`; the quad is invariant across commits, so scale commits round-trip exactly and edge margins (logical px) scale with the factor.

**Requirements (stable IDs SC-01…SC-16):** see the `Requirements` table in `2026-08-21-ui-scale-design.md` — it is the canonical requirement→component→phase→test mapping; task headers in this file tag the IDs they implement.

**Tech Stack:** C# / Godot 4.7.1 (GodotSharp, `net10.0` test target), xUnit.

**Execution:** dedicated worktree off main (via @using-git-worktrees) — SAME worktree/branch as Part 1A (sequential dependency); the four tasks run sequentially as written — each compiles standalone and lands as its own commit. Recommended: @subagent-driven-development (fresh subagent per task, spec-compliance then code-quality review after each). No headless `godot` needed in this part (the headless gate lives in Part 1C Task 5); in-engine legs referenced below are pinned there. Part 2's matrix M1–M9 and M11 need a display and a game server — run manually or in a headed session (M10 is Part 1C Task 5's headless gate, no server).

---

## APIs verified (citations)

- `BaseWindow.RepositionForCurrentCanvas()` — `Scripts/UI/BaseWindow.cs:88` — public, idempotent, "safe to call any time after `_Ready`"; first-run-dialog centers, else `WindowPlacement.Resolve(storedOrDefaultPos, Size, savedCanvas, currentCanvas)`. Reads `Size` at call time → **must run after geometry is applied** (that ordering is why the apply pass is fonts → geometry → reposition).
- `GameManager.OnWindowResized` — `Scripts/GameManager.cs:337-345` — existing live-resize precedent: walks `UiLayer`, calls `RepositionForCurrentCanvas()` on every `BaseWindow` via `CollectBaseWindows` (`Scripts/GameManager.cs:347-356`).
- `EnsureHud()` (`Scripts/GameManager.cs:323`) has exactly **one call site**: `MapManager._Ready` (`Scripts/MapManager.cs:93`), a server-driven map transition — at startup (login screen) **no HUD exists**. The applier is created in `GameManager._Ready`, which precedes any window's `_Ready` (windows first build when `EnsureHud` runs). A headless self-test that needs the HUD must call `EnsureHud()` itself (Part 1C Task 5).
- `CharacterSettings.Options` — `Dictionary<string, object>` with `GetOption<T>(key, default)` / indexer; `IncludeFields` JSON — `Scripts/CharacterSettings.cs:42-67`. Key constants live in `Constants.cs:136` (`public static class Options`). Part 2 adds the two new OPTION keys (`UiScaleMode`/`UiScaleValue`); the parts of this feature add per-window QUAD fields `Size`/`Factor` (Part 1A Part 1A Task 2 — written at drag-end), NOT option keys.
- `WindowPlacement.Resolve(savedPos, windowSize, savedCanvas, currentCanvas)` — `Scripts/UI/WindowPlacement.cs:30`; `TitleBarHeight = 24` (`Scripts/UI/WindowPlacement.cs:17`) is the y-containment allowance.
- GodotSharp 4.7.1, verified by **reflection on the actual project DLL** (`/opt/Godot_v4.7.1-stable_mono_linux_x86_64/GodotSharp/Api/Release/GodotSharp.dll`), not docs:
  - `Theme.SetDefaultFontSize(int)` / `GetDefaultFontSize()` — the theme-mutation API.
  - `Control.AddThemeFontSizeOverride(StringName, int)` and `Control.AddThemeConstantOverride(StringName, int)` — set-or-replace; **there is no `SetThemeFontSize`/`SetThemeConstant`** in this binding (the existing codebase already uses `AddThemeFontSizeOverride`).
  - `Control.GetThemeFontSize(StringName, StringName)` / `GetThemeConstant(...)` — effective values (the constants snapshot reads `GetThemeConstant`; the Part 1C Task 5 audit reads `GetThemeFontSize`).
  - `Control.OffsetLeft/OffsetTop/OffsetRight/OffsetBottom` and `AnchorLeft/Top/Right/Bottom` — all present (reflection). The snapshot records the **offsets** (anchor-relative by construction) and never touches anchors or `Position`/`Size` directly.
  - `Control.HasThemeFontSizeOverride(StringName)` / `HasThemeConstantOverride(StringName)` — override-presence queries; the font audit (Part 1C Task 5) uses these to find ANY font-override control under `UiLayer` and demand registry membership.
  - `Control.SetMeta(StringName, Variant)` / `GetMeta(...)` — the skip-meta mechanism.
  - `Node.TreeExited` event — deregistration hook.
  - `OS.GetCmdlineUserArgs()` (`string[]`) — project args after `--`; **`GD.GetCommandLineArgs` does not exist**. Part 1C Task 5's flag comes from here.
  - Anchored roots in the wild: `Scenes/UI/ChatWindow.tscn:11-14` (bottom-left, `offset_top = -213`), `Scenes/UI/Toolbar.tscn` root bottom-right (all four anchors `1.0`). Offsets scale correctly under either; `Position` would not.
- Test project (`tests/Goose2Client.Tests`) pins **GodotSharp 4.6.2** from NuGet (not the 4.7.1 in the engine dir) — the xUnit surface (`UiScale`, `WindowPlacement` param) must stay 4.6.2-compatible; `Vector2I`/`MathF` are fine (existing `WindowPlacementTests` already use GodotSharp).
- `theme_override_font_sizes` occurrences to migrate: `Scenes/UI/BankWindow.tscn:43` (9), `Scenes/UI/ChatWindow.tscn:47` (`normal_font_size` 12, RichTextLabel), `Scenes/UI/ChatWindow.tscn:55` (12), `Scenes/UI/DebugWindow.tscn:23,32` (12), `Scenes/UI/VendorWindow.tscn:43` (10). Raw C# overrides: `Scripts/UI/BaseMultipleWindow.cs:60` (12) and `:68` (10). (Bridge files `ChatBubble`/`BattleTextLine`/`BridgedNameLabel` are world-space — **do not touch**.)
- Window geometry is `.tscn` pixel-offset layout (e.g. `Scenes/UI/VitalsWindow.tscn` all `layout_mode = 0` offsets; `Scenes/UI/BaseWindow.tscn` anchor-based children). Slots: `Scenes/UI/ItemSlot.tscn` `custom_minimum_size = Vector2(32, 32)`.
- Headless runner: `godot-mono`/`godot` — whichever C#-capable build the machine has (the binary name varies across environments; `tools/tests/run_ui_scale.sh`, Part 1C Task 5, resolves it and fails clearly if neither exists), run as `--headless`. Existing probes `tools/tests/*.gd` intentionally do NOT execute C#; Part 1's C#-executing proof is a project-argument self-test (Part 1C Task 5), run as `<godot> --headless -- +selftest=ui_scale`.

## Design refinements vs `2026-08-21-ui-scale-design.md` (ratified in design chat)

- R1 (revised after plan review): per-window hand-written layout constants → **generic snapshot** (`UiScaleLayout`). **Build-time geometry is the base as-is — no division.** The original divide-by-current-factor idea was wrong: `.tscn` geometry loads at 1× regardless of the active factor, so at a 1080p startup (Apply 2× before HUD build) a snapshot would divide 1× geometry by 2 and `Relayout(2)` would un-scale it — the HUD would render at 1× on exactly the displays this feature exists for, and no headless test could catch it (headless factor is always 1). Registration calls `Relayout()` once (same-frame scaling, no flash). The snapshot records **anchor-relative offsets, not Position/Size**, because anchored roots (`ChatWindow` bottom-left, `Toolbar` right-edge) must not have their `Position` scaled or they detach from their edge.
- R2: live tooltips are **hidden** on apply (design said repositioned) — re-shown on next hover; avoids mouse-follow geometry mid-commit.
- R3: windows **self-register** at end of `_Ready` (GameHud does not enumerate) — `VitalsWindow` is a plain `Control`, and runtime-spawned NPC windows (via `BaseWindow._Ready`) register automatically.

---
### Task 1: `UiScaleApplier` (plain class, single apply pass) + font registry — SC-03, SC-04, SC-05, SC-08

**Files:**
- Create: `Scripts/UiScaleApplier.cs`
- Create: `Scripts/IScalableWindow.cs` — `void Relayout();` (this task is where the interface is created; Task 3's file list is not a second creation site)
- Modify: `Scripts/UI/TooltipManager.cs` — add `HideAll()` (Apply step 3 calls it; mirrors the existing `Hide*Tooltip` methods, `TooltipManager.cs:37-60`)
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
    public void ApplyFontSize(Control c, float basePx);
    public void ApplyFontSize(Control c, float basePx, StringName prop);   // 2-arg form delegates with new StringName("font_size")
    public void Apply(float factor, ApplyReason reason);

    public float Factor => Scale.CurrentFactor;
    public int ScaleSize(float basePx) => Scale.ScaleSize(basePx);
    public IReadOnlyList<WindowRegistration> RegisteredWindows { get; }
    public bool TryGetFontBase(Control c, StringName prop, out float basePx);
}
public enum ApplyReason { Startup, UserCommit, AutoResize }
```
The 2-arg `ApplyFontSize` overload is load-bearing (review finding): `StringName prop = "font_size"` as a DEFAULT ARGUMENT is CS1750 — C# default values must be constant expressions and `string → StringName` is an implicit conversion, so the single-method-with-default form does not compile (verified against the referenced GodotSharp assembly). All call sites in Part 1B Tasks 3-4 and Part 1C Tasks 3-4 use the 2-arg form; the 3-arg form is for this task's xUnit `outline_size` leg.
`Instance` has an `internal set` (set by `GameManager._Ready`, same assembly — NOT `private set`, which GameManager can't call). `Theme` is `GD.Load<Theme>("res://Assets/UI/GameTheme.tres")` — C# has NO `Preload<T>`; `GD.Load` is the API. `_fonts` is FLAT with ownership by ancestry at unregister time (NOT by registration — review finding: `ApplyFontSize` runs during the window's `_Ready`, BEFORE its own registration, so "the most recent registration" would be the PREVIOUS window). `RegisterWindow` is bookkeeping ONLY per the single contract stated below (null → `ArgumentNullException`, non-`Control` → `ArgumentException`, duplicate → existing registration) — a stored null `ControlRef` would break the dictionary, so a non-`Control` caller is a bug that must surface at the call site, not be silently skipped. The initial Relayout is owned by the window's `ScaleRegister` (Task 3), not here. The read-only seams (`RegisteredWindows`, `TryGetFontBase`, `Theme`) are public for the Part 1C Task 5 self-test — the applier surface is already public, and this repo has no InternalsVisibleTo infra.

`WindowRegistration` (nested public): `IScalableWindow Window`, `Control ControlRef` — **non-null by construction**: the SINGLE contract (review finding — the draft carried three mutually incompatible ones: `return w` from a `WindowRegistration`-returning method, a null no-op, and a throw) is: `null` → `ArgumentNullException`; `w is not Control` → `ArgumentException`; already registered → return the EXISTING registration (idempotent — the deferred fallback and a direct call can both fire); otherwise store and return the new registration. NO per-registration font list — fonts live only in the flat `_fonts`; `UnregisterWindow` walks `_fonts` and removes entries where `c == root || root.IsAncestorOf(c)`. A control whose window never registered still lands in `_fonts` only — pruned by validity in `Apply`. `IScalableWindow` (`Scripts/IScalableWindow.cs`): `void Relayout();`

**`Apply(factor, reason)` exact sequence (ordering is load-bearing):**
1. `var f = UiScale.NormalizeFactor(factor)` (pure; NaN-safe per Part 1A Task 1); `if (f == Scale.CurrentFactor && _appliedOnce) return;` — the first `Apply` always runs (startup). Set `Scale.CurrentFactor = f` — **the applier is the only writer** (Part 1A Task 1 semantics).
2. **(Part 2 Task 2 lands here — before hiding tooltips):** cancel any in-progress window move-drag via `BaseWindow.CancelDrag()` on every registered `BaseWindow` (cast; non-`BaseWindow` roots skip).
3. HIDE live tooltips: `if (TooltipManager.Instance != null) TooltipManager.Instance.HideAll();` — add `HideAll()` to `TooltipManager` (sets `Visible = false` on the four tooltip controls; mirrors existing `Hide*Tooltip`, `Scripts/UI/TooltipManager.cs:37-60`). (R2)
4. Theme: `Theme.SetDefaultFontSize(ScaleSize(10))` (base 10 = `GameTheme.tres` `default_font_size`; `Theme` is the `GD.Load`ed cached field from the API block).
5. **Prune first, then apply (review finding: removal during `foreach` throws)** — `var invalid = _fonts.Where(e => !GodotObject.IsInstanceValid(e.C)).Select(e => e.C).Distinct().ToList();` then `_fonts.RemoveAll(...)` for them, and `_windows.RemoveAll(r => !GodotObject.IsInstanceValid(r.ControlRef))`; THEN apply: for every `(c, prop, base)` left, `if (c.IsInsideTree()) c.AddThemeFontSizeOverride(prop, ScaleSize(base))`. (Normal teardown is `tree_exited` → `UnregisterWindow` + ancestry font prune; the validity prune is the backstop for anything that leaks past it.)
6. Geometry: `foreach (var r in _windows) r.Window.Relayout();` — geometry ONLY (Task 3's `Relayout` never repositions; placement is a separate, later step).
7. Placement: `foreach (var r in _windows) if (r.Window is BaseWindow bw) bw.RepositionFromSaved();` — each window derives its position from its OWN persisted quad (Part 1A Task 2: saved pos/size/factor/canvas + live Size/factor/canvas). No size capture needed anywhere — the quad is the source of scale space, so 1×→2×→1× round-trips exactly (Part 1A Task 2's `ResolveScaled_ScaleCommitRoundTrips`). Non-`BaseWindow` roots (Task 4) have no reposition (anchor-stuck).

**Publication boundary (registry):**
- Creation order (single Relayout owner — review finding): window builds nodes (at 1× base constants) → **`ScaleRegister()`** (Task 3) does snapshot → `RegisterWindow(this)` (bookkeeping) → `Relayout()` once: build-time geometry is the 1× base, so a window spawned at runtime under a 2× factor lands at 2× in the same frame (zero 1× frames); at 1× the call is a no-op re-apply. `RegisterWindow` itself never calls `Relayout` (the old draft gave both owners — one of them had to go).
- Teardown: `UnregisterWindow` on `tree_exited` (connected at registration) removes the window AND its descendant font entries (review finding: fonts are separate controls — there is no "fonts clean themselves up" path; the ancestry prune in `UnregisterWindow` IS that path). `GameHud` is never freed/rebuilt (guarded `EnsureHud`, no free path) — there is no rebuild-clear; the `IsInstanceValid` prune (step 5) is the backstop for anything that leaks past `tree_exited`.
- Readers (the apply pass) can only ever see a fully-built, already-laid-out window: registration happens at END of `_Ready` (R1 snapshot also taken there), and `Apply` never runs during a `_Ready` (startup Apply precedes HUD build; user commits happen on input events between frames).
- Failure behavior: `RegisterWindow(null)` throws `ArgumentNullException` per the single contract above (NOT a no-op — the draft's three contradictory behaviors were resolved there); a duplicate is the designed no-op (returns the existing registration, idempotent); a throw leaves no partial state.

**`ApplyFontSize` contract:** sets the override immediately (build-time correctness) AND records the entry. It is the ONLY place window code may set a font-size theme override (raw `AddThemeFontSizeOverride` calls in window code are prohibited — Part 1C Task 3 enforces the migration; the self-test in Part 1C Task 5 is the tripwire).

**GameManager wiring:** in `_Ready` — **before `CharacterSettings` exists** (it is created only on successful login, `LoginScene.cs:103` → `LoadSettings`), so the startup `Apply` must be settings-independent. Use the settings-free best guess (Auto): the login screen (Part 2 registers it) is already at the right factor for Auto users, and at the tiny headless root size (~64–100 px, NOT the project's 1280×720 — Part 1C Task 5 step 0) `AutoFactor(small) == 1`; Part 1's self-test additionally forces `Apply(1f, …)` explicitly (Part 1C Task 5), so determinism does not depend on the headless size at all:
```csharp
UiScaleApplier.Instance = new UiScaleApplier();
var canvas = (Vector2I)GetTree().Root.GetVisibleRect().Size;
UiScaleApplier.Instance.Apply(UiScale.AutoFactor(canvas.Y), ApplyReason.Startup);
```
(`UiScale.AutoFactor` is STATIC — call it through the type, never `applier.Scale.AutoFactor(...)`: C# rejects static access through an instance with CS0176.)
Part 1 reads no settings (the options keys are Part 2); Part 2 adds the settings-driven re-`Apply` in `LoadSettings` (runs post-login, pre-HUD — still before any map entry, so no unscaled flash) and sets `applier.Mode`.

**Step ordering / tests:** this task has no pure-xUnit surface (Godot types). Proof comes from Part 1C Task 5's self-test (audit + smoke + 1× no-op run). Compile + existing test suite green is the gate here.
**Commit:** `feat: add UiScaleApplier apply pass and font registry`.

---

### Task 2: `UiScaleLayout` generic snapshot (R1 mechanism, revised) — SC-05

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
- Walks `root` + all descendants (depth-first). Skips any node (and its subtree) with meta `ui_scale_skip == true` (R: per-frame/dynamic controls opt out). For each `Control`: record `OffsetLeft/OffsetTop/OffsetRight/OffsetBottom`, `CustomMinimumSize` (skip if `Vector2.Zero`), and theme constants — **AUTHORED OVERRIDES ONLY (decision, review: `GetThemeConstant` returns the EFFECTIVE value, falling back through the theme's defaults, so materializing effective constants would add+scale values the tscn never authored — a visible 1×→2× regression on unauthored margins/separations)**: record `GetThemeConstant(name)` ONLY where `HasThemeConstantOverride(name)` is true, and ONLY names in a fixed list: `"separation"`, `"h_separation"`, `"v_separation"` (the only ones used in `Scenes/UI/*.tscn` — verify with `grep -rn theme_override_constants Scenes/UI` at implementation time; extend the list only with cited occurrences). Skip constants whose value is `0`. The authored-only rule is pinned by Part 1C Task 5: the 2× phase asserts a control with NO authored constants at snapshot time still has none added at 2×.
- Postconditions: `Apply(records, 1f)` reproduces the end-of-`_Ready` geometry **bit-identically — which requires writing the recorded RAW FLOATS when `factor == 1`**, because real geometry contains fractional values (`BaseMultipleWindow.LineRowHeight = 11.18f` → label positions like `22 + i × 11.18f` are non-integer; `int(round(33.18)) == 33` would not reproduce the base). At any `factor != 1`, writes are `int(round(base×f))`. Records always store the original base, so 2×→1×→2× round-trips. Anchors are never recorded or touched.
- **Container-managed children (review finding — the old "harmless no-op overwrite" was wrong, two ways):** for any control whose `GetParent() is Container` (HBox/VBox/Grid children — login VBox, toolbar, item grids), record `ContainerManaged = true` and DO NOT use its offsets in `Apply` (see below). Rationale: (a) the recorded offsets can be captured BEFORE the container's queued layout pass, so writing them back makes the immediate 1× bit-identity assertion FLAKY (Relayout writes pre-layout offsets, the container re-sorts them a moment later); (b) their real scaling rides on `CustomMinimumSize` + `separation` constants + font-driven computed minimums — the container re-derives correct child offsets from those on its own pass. Implementer must verify per window that manually-positioned children (hotbar slots, `BaseMultipleWindow` line labels) have NON-container parents — if any manual-offset parent turns out to be a `Container`, that window's manual layout is already broken today (the container would overwrite it) and the window is out of the snapshot's remit.
- **1× bit-identity is asserted after one `ProcessFrame`** (queued container layouts flush), not in the same tick — Part 1C Task 5's comparison follows this.

**`Apply(records, factor)`:** for each record, if `GodotObject.IsInstanceValid(c) && c.IsInsideTree()`: **offsets — SKIP entirely when `ContainerManaged`** (the container owns them); otherwise if `factor == 1f` → write back the recorded raw floats verbatim (`c.OffsetLeft = c.oLeft`, …) — the bit-identical 1× path (see postconditions, `BaseMultipleWindow.cs`'s `11.18f` pitch); else → `c.OffsetLeft = int(round(c.oLeft × f))` (same for the other three, per-value `MathF.Round(x, AwayFromZero)`); min-size via `c.CustomMinimumSize` (all records, container-managed or not); constants via `c.AddThemeConstantOverride(name, scaled)` (raw-write at 1× too). **This file contains no reusable math of its own** — it takes the factor as `float` and does explicit `MathF.Round(x, AwayFromZero)` on plain floats with NO min-1 floor (offsets are coordinates — 0 must stay 0, matching `UiScale.ScaleCoordinate`'s semantics, Part 1A Task 1; `UiScale`'s instance isn't reachable from a static helper without a parameter, and passing the factor is the seam).

**Flags:** anchor-attached controls (e.g. `CloseButton` at `offset_left = -18` in `BaseWindow.tscn`) scale their offsets correctly with anchors untouched; `ChatWindow`'s bottom-left root (`offset_top = -213`) stays edge-stuck because its negative offsets simply double.

**Proof:** no direct unit test (needs a live tree) — proven by Part 1C Task 5: (a) 1× no-op run (snapshot→apply at factor 1 must leave every rect bit-identical, **compared after one `ProcessFrame`** so queued container layouts have flushed — both the captured baseline and the post-Relayout read), (b) factor-2 audit (sampled values equal `round(base×2)`: `VitalsWindow` root size 183×55→366×110, an `ItemSlot` min-size 32→64, `ChatWindow` root offsets `−213/−5`→`−426/−10`), (c) the runtime-spawn leg (Part 1C Task 5 step 2b) — the adversarial proof that build-time geometry is the base at a live non-1 factor.
**Commit:** `feat: add UiScaleLayout geometry snapshot`.

---

### Task 3: `BaseWindow` registration, `Relayout`, factor-aware reposition — SC-06, SC-07, SC-08

**Files:**
- Modify: `Scripts/UI/BaseWindow.cs`
- Modify: `Scripts/UI/BaseMultipleWindow.cs:46-77` (fonts + line-label skip-meta + registration ONLY — line GEOMETRY is Part 1C Task 4's `MultiWindowMetrics` + `Relayout` override)
- Modify: `Scripts/IScalableWindow.cs` (if not created in Task 1 — created there)

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
  Order: snapshot (tscn/1× geometry is the base) → register → `Relayout()` (geometry at the live factor — at a 2× startup the frame is NOW its final size) → `RepositionFromSaved()` (placement against the FINAL size, Part 1A Task 2's quad model) → teardown hook. Both mutations happen inside `_Ready`, before the first render — no 1× frame, no wrong-placement frame.
- `BaseWindow._Ready()`: the placement call is REMOVED (the `RepositionForCurrentCanvas();` line ~54 — its logic moves into `RepositionFromSaved`, called from `ScaleRegister` AFTER relayout). Everything else stays in order/behavior — the `Visible`-restore (`ws.Visible`) and `MoveChild` are untouched, and `private Vector2 _tscnSize = Size;` is captured at the TOP of `_Ready` (Size is still the tscn size — relayout has not run yet); `_tscnSize` is the saved-size fallback for legacy settings (Part 1A Task 2). At its very end (after `MoveChild`, `BaseWindow.cs:76`) it schedules the fallback `Callable.From(() => ScaleRegister()).CallDeferred();` (the repo's established deferred-call pattern, `Scripts/Network/NetworkClient.cs:106`) — covers `BaseWindow` subclasses that do NOT override `_Ready` (InfoWindow, QuestWindow: registration lands one frame later; both are server-driven dialogs, no perceptible flash).
- **Each of the 9 window subclasses that override `_Ready` gets ONE line at the end of it: `ScaleRegister();`** — `BankWindow`, `BaseMultipleWindow`, `CharacterWindow`, `CombineBagContainerWindow`, `HotbarWindow`, `InventoryWindow`, `OptionsWindow`, `SpellbookWindow`, `VendorWindow`. (Verified audit: no subclass build code reads the `Content`/`TitleLabel`/`Background` properties — all use their own `GetNode` calls — so no reordering was needed; `BaseMultipleWindowManager` does not call `base._Ready()` and is not an on-screen window — leave it.)
- **Invariant:** no code after the `ScaleRegister()` call may set geometry on `this` or descendants directly — the snapshot is the base and the next `Apply` clobbers any direct write. (Build code BEFORE the call uses 1× base constants; that geometry becomes part of the base.)
- `BaseWindow` fields: `private List<GeomRecord> _geom = null!;` (NOT readonly — assigned by `ScaleRegister()`; C# forbids readonly assignment outside constructors); `private bool _scaleRegistered;`; `private Vector2 _tscnSize;` — a NORMAL field, assigned at the TOP of `_Ready` (`_tscnSize = Size;`): a field initializer cannot capture the tscn size (initializers run before the tscn is applied), and `readonly` cannot be assigned in `_Ready` — both forms in the earlier draft are compile errors as written (review finding).
- `public virtual void Relayout()` — geometry ONLY (placement is a separate step; the applier calls it then `RepositionFromSaved`, Task 1 step 6/7). **Virtual is load-bearing (review):** derived windows override it — `BaseMultipleWindow` (Part 1C Task 4; verified `BaseMultipleWindow : BaseWindow`, so a non-virtual method would either not scale the frame or never dispatch the override) and `OptionsWindow` (Part 2 Task 4 — label refresh on auto-resize). Every override calls `base.Relayout()` FIRST (the generic pass owns the frame).
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
      var placed = ws != null && ws.Placed;                       // (b) valid quad — Position may legitimately be (0,0)
      var legacy = !placed && ws != null && ws.Position != default; // (a) pre-feature position, honored with legacy size/factor
      if (!placed && !legacy && DefaultWindowLayout.IsDialog(WindowName))
      {
          Position = WindowPlacement.Center(canvas, Size);
          return;
      }
      var pos = placed || legacy ? ws.Position : DefaultWindowLayout.For(WindowName); // (c) unplaced non-dialog → default layout
      var savedCanvas = ws != null && ws.CanvasSize != default ? ws.CanvasSize : WindowPlacement.LegacyCanvas;
      var savedSize = placed && ws.Size == default ? (DefaultWindowLayout.LegacySize(WindowName) ?? _tscnSize)   // defensive: Placed is written with Size
          : (!placed ? (DefaultWindowLayout.LegacySize(WindowName) ?? _tscnSize) : ws.Size);
      var savedFactor = placed && ws.Factor > 0f ? ws.Factor : 1f;   // defensive: Placed is written with Factor
      var applier = UiScaleApplier.Instance;
      Position = WindowPlacement.ResolveScaled(pos, savedSize, savedFactor, savedCanvas, Size,
          applier != null ? applier.Factor : 1f, canvas,
          applier != null ? applier.ScaleSize(24f) : WindowPlacement.TitleBarHeight);
  }
  ```
  **The `Placed` marker (review — a saved (0,0) is a position, not an absence):** three record states resolve as: (c) `Placed` false + `Position` zero (visibility-only or never-moved) → default layout — and for DIALOGS (`Quest`/`Vendor`/`Info`/`Bank`/`CombineBag`, `DefaultWindowLayout.IsDialog`) the default layout IS centering, so the center check keys on PLACEMENT VALIDITY (`!placed && !legacy`), NOT `ws == null` (review: after a visibility-only close the record exists with `Placed == false` and zero position — the old `ws == null` condition silently demoted dialogs to `DefaultWindowLayout.For(...)` on the next launch after the first close); (a) `Placed` false + `Position` non-zero (LEGACY — no `Size`/`Factor`/`Placed` keys, everything shipped to date) → the position is HONORED with `savedSize = DefaultWindowLayout.LegacySize(WindowName) ?? _tscnSize`, `savedFactor = 1` — exactly the true saved pair (the `LegacySize` leg is non-null only for `Options`, whose tscn Part 2 grows 112→240 — every other window's legacy size IS its tscn size), so old files place identically to today (a legacy file that predates this feature cannot round-trip a (0,0) — same as today's code; accepted); (b) `Placed` true (any drag-end save) → trust the quad WHOLESALE, including `Position == (0,0)` (defensive fallbacks in the snippet cover only corrupt files). End-to-end pin: Part 1C Task 5 step 2d (saved-origin round-trip) + 2e (visibility-only dialog record reopens centered — the automated test for the centering-validity fix) + Part 1A Task 2's `WindowSettings_SavedOriginRoundTrips` (JSON level). Callers: `ScaleRegister` (registration, post-relayout), the applier's placement step (every commit), and `GameManager`'s canvas-resize walk (`CollectBaseWindows` → `RepositionFromSaved` — the walk keeps its shape, one method renamed). Because the quad is invariant across commits, the walk, commits, and registration all compose: no previous-canvas tracking, no size capture (review findings on stale captures).
- **Drag-end save persists the full quad** — both `SetWindowSetting` call sites in `OnTitleBarGuiInput` (the `GuiInput` release and the `MouseMotion` escape) pass the new args: `SetWindowSetting(WindowName, Position, Visible, canvas, Size, UiScaleApplier.Instance != null ? UiScaleApplier.Instance.Factor : 1f)`; `CharacterSettings.SetWindowSetting` (the 4-arg overload, `CharacterSettings.cs:179`) gains optional `Vector2? size = null, float? factor = null` writing `settings.Size`/`settings.Factor` AND setting `settings.Placed = true` (fields added in Part 1A Task 2 — the marker is written only here, never by the visibility-only path). The Part 2 drag-cancel flag suppresses BOTH call sites exactly as planned — a cancelled drag persists no quad.
- **`SetWindowVisible` (lines 141-145) is CHANGED to persist VISIBLE ONLY** — it calls the new `CharacterSettings.SetWindowVisible(WindowName, v)` (writes `Visible`, leaves `Position`/`Size`/`Factor`/`CanvasSize` untouched) instead of the full `SetWindowSetting`; the `Toggle()` path (lines 153-156) gets the same treatment (review finding: the old toggle saved live Position+CanvasSize WITHOUT size/factor — after a scale commit that is a mixed-coordinate quad (a 2× position + missing size/factor falling back to 1), and a first-time close at 2× would persist a 2× position the legacy fallback can't place). Quad freshness does not require the toggle path: canvas migrations compose in `ResolveScaled` from ANY saved canvas, and drag-end keeps the canvas fresh. Regression coverage: `SetWindowVisible_PreservesFullQuad` (Part 1A Task 2) + the Part 2 M2 leg (drag at 2× → close → reopen at 1× → position unchanged).

**`BaseMultipleWindow` (this task: fonts + registration only — line geometry is Part 1C Task 4):**
- The 20 runtime-created line labels (`LineCount = 20`, created lines 63-71 — BEFORE the end-of-`_Ready` `ScaleRegister()`) carry the snapshot SKIP-META (Part 1C Task 4): their geometry is owned by `MultiWindowMetrics` + that class's `Relayout` override, so the generic snapshot must NOT capture them (a snapshot record would double-scale the already-scaled offsets on the next commit).
- Fonts: line 60 `b.AddThemeFontSizeOverride("font_size", ButtonFontSize)` → `applier.ApplyFontSize(b, ButtonFontSize)`; line 68 `label.AddThemeFontSizeOverride("font_size", LineFontSize)` → `applier.ApplyFontSize(label, LineFontSize)` (Part 1C Task 4 moves the label CREATION to `MultiWindowMetrics.LinePosition` at the live factor — native scaling for late-spawned windows).

**Other `BaseWindow` subclasses:** covered by the 9-file one-liner list above. If any subclass sets geometry in `_Ready` AFTER the `ScaleRegister()` line (or packet-driven geometry after registration), the next `Apply` clobbers it — the 2× audit (Part 1C Task 5) catches this; treat such failures as "register after build", not as scaler bugs.

**Gate:** full xUnit suite green; compile clean; Part 1C Task 5's 2× audit proves the runtime-built slot grids actually scaled (the leg that fails if the end-of-`_Ready` timing regresses).
**Commit:** `feat: BaseWindow self-registration and factor-aware relayout`.

---

### Task 4: non-`BaseWindow` roots (Vitals, Chat, Party, Debug, BuffEffects, Toolbar, TooltipManager) — SC-05

**Files:**
- Modify: `Scripts/UI/VitalsWindow.cs:33` (`_Ready`)
- Create: `Scripts/UI/Toolbar.cs` + attach to `Scenes/UI/Toolbar.tscn` root — the `HBoxContainer` root is **scriptless** (scripts live only on `DestroyButton`/`ToolbarItem` children); a new root script implements the registration pattern. The root is bottom-right anchored (all four anchors `1.0`), so offset scaling is what keeps it stuck.
- Modify: `Scripts/UI/ChatWindow.cs`, `Scripts/UI/PartyWindow.cs`, `Scripts/UI/DebugWindow.cs`, `Scripts/UI/BuffEffectsWindow.cs` (`_Ready` each — **all five are plain `Control` roots, verified**, and Chat/Party/Debug/BuffEffects would otherwise never scale)
- Modify: `Scripts/UI/TooltipManager.cs:17` (root + `HideAll()` added in Task 1)
- Modify: `Scenes/UI/Tooltips.tscn` (set `meta/ui_scale_skip = true` on the four tooltip control nodes `ItemTooltip`/`SpellTooltip`/`TextTooltip`/`MapItemTooltip` — their geometry is set dynamically per-show in C#; the skip meta keeps the SNAPSHOT away from them. Their factor-aware layout is Part 1C Task 1 — this task only makes the snapshot ignore them)

**Pattern for each root** (plain-`Control` windows): same as Task 3 minus reposition:
```csharp
var applier = UiScaleApplier.Instance;
_geom = UiScaleLayout.Snapshot(this);
applier.RegisterWindow(this);
Relayout();
TreeExited += () => applier.UnregisterWindow(this);
```
(`Relayout()` is `UiScaleLayout.Apply(_geom, applier.Factor);` — same as Task 3 minus the `RepositionFromSaved` call, which plain-`Control` roots never do.)
- `VitalsWindow` (root at tscn `(8,8)`, 183×55): registers; its root position scales via geometry (intended). Its dynamic portrait path is Part 1C Task 5 (this task only registers the root).
- `ChatWindow` (bottom-left-anchored root, offsets `8, −213, 508, −5`): registers; offset scaling doubles the margins and keeps it edge-stuck — NO `RepositionForCurrentCanvas`-style reposition (it has none; its placement IS the anchor).
- `PartyWindow`, `DebugWindow`, `BuffEffectsWindow`: register (top-left-anchored tscn offsets scale). `PartyWindow`'s `MemberList` tiles are container-managed (VBox) → the snapshot skips their offsets, and each `PartyMember` tile's 87×33 exists ONLY as tscn offsets (no `CustomMinimumSize`, `PartyMember.tscn:9`) — the tile's SIZE is Part 1C Task 3's `PartyMemberMetrics` item (without it the tiles stay 87×33 while their theme-scaled font overflows); the tile's INTERNAL offsets are captured by this generic snapshot (their parents are ordinary Controls) and scaled by `Apply` — single ownership, round 12.
- `Toolbar`: registers (offsets scale; its Options button wiring is untouched).
- `TooltipManager`: registers ONLY its root + static children; the four dynamic tooltip nodes carry the skip meta so the SNAPSHOT ignores them (their geometry is per-frame C# — scaled by Part 1C Task 1's per-frame metrics, not by the snapshot).
- `WorldDropTarget` (`Scripts/UI/WorldDropTarget.cs`, full-rect, `MouseFilter.Pass`): **do not register** — full-rect anchors, nothing to scale.

**Gate:** xUnit green; 1× headless run (Part 1C Task 5 command) shows no layout drift (visual/rect check via the audit's 1× no-op leg).
**Commit:** `feat: register vitals, chat, party, debug, buff-effects, toolbar, tooltip roots with the scale applier`.

---


---

## Invariant-to-test matrix (Part 1B)

| Invariant | Proved by |
|-----------|-----------|
| Container-managed children: snapshot skips their offsets; final geometry is container-derived and stable — SC-05 | Task 2 `ContainerManaged` rule + Part 1C Task 5 step 1 (PostFrame compare) |
| Registry stays clean: windows + descendant fonts deregister on `tree_exited`; orphans pruned by `IsInstanceValid` — SC-05 | Task 1 apply-step prune + `UnregisterWindow` ancestry prune (covered by Part 1C Task 5's clean run across `EnsureHud`) |
| A cancelled drag persists no quad; visibility toggle persists only `Visible` — SC-07, SC-08 | Task 3 (both `SetWindowSetting` sites + `SetWindowVisible` rewire) + Part 1A Task 2 `SetWindowVisible_PreservesFullQuad` + the Part 2 M2/M6 legs |
