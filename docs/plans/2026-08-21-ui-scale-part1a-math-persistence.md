# UI Scale Part 1A — Math & Persistence

**Part order:** 1A → 1B → 1C → Part 2 (sequential, same worktree/branch; each part is a self-contained execution context with its own task list and commit sequence). Split rationale (review): the original 10-task Part 1 was too large for one execution context — math/persistence (1A), core scaler/registration (1B), dynamic surfaces/runtime audit (1C).

**Goal:** The two pure/persistence foundations: `UiScale` (factor normalization, auto thresholds, integer scaling) and the saved-quad placement model (`WindowPlacement.ResolveScaled` + per-window `Size`/`Factor` persistence + `LegacySize`). No Godot node changes, no runtime code paths — xUnit-only proof. Everything in 1B/1C/Part 2 builds on these two tasks.

**Architecture (shared by all parts):** Windows keep their build-time geometry as the 1× base — `.tscn` pixel offsets load at 1× regardless of the active factor, and C# build code writes 1× base constants (it does **not** scale at build time; scaling happens in `Relayout`). A static `UiScaleLayout` helper (Part 1B) snapshots each window's descendant geometry at end-of-`_Ready` as-is (anchor-relative offsets) — that snapshot is the base, no division. `ScaleRegister()` (end of each window's `_Ready`) snapshots, registers, relays out, and places — so a window spawned at runtime under a 2× factor scales AND places in the same frame (zero 1× frames). `UiScaleApplier.Apply(factor)` (plain class, `GameManager`-hosted, `TooltipManager.Instance`-style `Instance` accessor; Part 1B) then: normalizes the factor, cancels in-flight window drags (Part 2), hides live tooltips, mutates `GameTheme.default_font_size`, re-applies registered explicit font overrides, calls each registered window's geometry-only `Relayout()`, then every `BaseWindow`'s `RepositionFromSaved()`. Placement is the **saved-quad model** (this part, Task 2): each window persists (position, size, factor, canvas) at drag-end, and every placement — registration, scale commit, canvas resize — derives from that quad + the live (Size, factor, canvas) via pure `WindowPlacement.ResolveScaled`; the quad is invariant across commits, so scale commits round-trip exactly and edge margins (logical px) scale with the factor.

**Requirements (stable IDs SC-01…SC-16):** see the `Requirements` table in `2026-08-21-ui-scale-design.md` — it is the canonical requirement→component→phase→test mapping; task headers in this file tag the IDs they implement.

**Tech Stack:** C# / Godot 4.7.1 (GodotSharp, `net10.0` test target), xUnit.

**Execution:** dedicated worktree off main (via @using-git-worktrees); the two tasks run sequentially as written — each compiles standalone and lands as its own commit. Recommended: @subagent-driven-development (fresh subagent per task, spec-compliance then code-quality review after each). No headless `godot` needed in this part (pure xUnit); the headless gate lives in Part 1C Task 5. Part 2's matrix M1–M9 and M11 need a display and a game server — run manually or in a headed session (M10 is Part 1C Task 5's headless gate, no server).

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
### Task 1: `UiScale` pure math + xUnit — SC-02

**Files:**
- Create: `Scripts/UiScale.cs`
- Test: `tests/Goose2Client.Tests/UiScaleTests.cs`

**Step 1: Write the failing tests.**

`UiScale` is a small non-static class with **explicitly separated state and pure functions** (review finding: a `Factor` property + `Factor(float)` method is a C# CS0102 compile error — verified against the compiler — and it was ambiguous whether `Factor(raw)` normalized or mutated). **"Pure" = no scene-tree / global-state APIs; Godot VALUE types are permitted (review: `Vector2I` in the signature with "no Godot usings" and no global `using Godot` in the project cannot compile)** — the file carries `using Godot;` for `Vector2I`, exactly like the existing `WorldViewportScale` (which is the pattern this class copies):
- `public float CurrentFactor { get; set; }` — plain state; **`UiScaleApplier.Apply` is the only writer** (tests set it via object initializer).
- `public static float NormalizeFactor(float raw)` — pure: clamp + snap, NaN → `MinFactor`. Never touches `CurrentFactor`.
- `public static int AutoFactor(int windowHeightPx)` — pure.
- **`public static int ScaleSize(float basePx, float factor)` — the SINGLE scaling primitive (review: the draft's "shared static instance" is global state and `UiScaleApplier.Instance.Scale` ignores an explicit factor — neither fits the pure `Metrics(factor)` contract)**: pure, `Math.Max(1, (int)MathF.Round(basePx × factor, MidpointRounding.AwayFromZero))`. ALL Part 1C metrics classes call this static two-arg form with their explicit `factor` argument — no rounding logic duplicated in metrics files.
- `public int ScaleSize(float basePx) => ScaleSize(basePx, CurrentFactor);` / `public Vector2I ScaleSizeI(Vector2I v)` — thin instance forms reading `CurrentFactor` (applier-internal only; metrics never use them).
- **Pinned constants** `public const float MinFactor = 1f, MaxFactor = 3f, Step = 0.5f` (the slider range; `NormalizeFactor` clamps/snaps to these).
Both projects reference GodotSharp (the test project already does, see `WindowPlacementTests`) — `Vector2`/`Vector2I` in signatures are the norm for the pure math classes (`WorldViewportScale`, `WindowPlacement`).

Tests (all red against "does not exist"):
- `NormalizeFactor_SnapsToHalfStepsAndClamps`: `0.4f → 1`, `0.9f → 1`, `1.25f → 1.5f`, `1.7f → 1.5f`, `2.3f → 2.5f`, `3.4f → 3`, `4.2f → 3`, `-1f → 1`.
- `NormalizeFactor_RejectsNaN`: `NormalizeFactor(float.NaN)` returns `1` (normalize must be total — corrupt settings pass through here).
- `CurrentFactor_IsPlainState`: `new UiScale { CurrentFactor = 2.5f }` → `ScaleSize(10f) == 25` (25.0), and `NormalizeFactor`/`AutoFactor` do not read or write it.
- `AutoFactor_Boundaries` (explicit thresholds — NOT `round(h/720)`, which would make 1440 → 2): `719 → 1`, `720 → 1`, `1079 → 1`, `1080 → 2`, `1439 → 2`, `1440 → 3`, `2880 → 3` (clamp case).
- `ScaleSize_RoundsHalfAwayFromZero`: with `CurrentFactor = 1.5f`, `ScaleSize(10f) == 15` and — the pin — a `.5` product rounds away: `CurrentFactor = 2.5f`, `ScaleSize(3f) == 8` (7.5 → 8, not 7).
- `ScaleSize_StaticTwoArg`: the static primitive is factor-explicit and matches the instance form: `ScaleSize(10f, 1.5f) == 15`, `ScaleSize(3f, 2.5f) == 8` (same half-away pin, no state involved), `ScaleSize(1f, 1f) == 1`, `ScaleSize(0f, 3f) == 1` (min-1 via the static path).
- `ScaleSize_MinOneGuard`: factor `1f`, `ScaleSize(0f) == 1`; smallest real base `ScaleSize(1f) == 1` at factor `1f`.
- `ScaleSizeI_PerAxis`: factor `2f`, `new Vector2I(32, 55) → new Vector2I(64, 110)`.

**Step 2 (red):** `dotnet test tests/Goose2Client.Tests` → compile fail (no `UiScale`).

**Step 3:** Implement `Scripts/UiScale.cs`. Use explicit half-away-from-zero rounding (Godot's `Mathf.Round` is not allowed — this file is Godot-free): `MathF.Round(x, MidpointRounding.AwayFromZero)` returns a `float` — the `int` result needs an explicit cast (review): `Math.Max(1, (int)MathF.Round(x, MidpointRounding.AwayFromZero))`. `NormalizeFactor`: `if (float.IsNaN(raw)) raw = MinFactor; snapped = MathF.Round(raw / Step, MidpointRounding.AwayFromZero) * Step; return clamp to [MinFactor, MaxFactor]`. `AutoFactor(h)`: `h < 1080 ? 1 : h < 1440 ? 2 : 3` (clamped by construction).

**Step 4 (green):** all pass. **Step 5:** commit `feat: add UiScale pure scale math`.

| Invariant | Proved by |
|-----------|-----------|
| Corrupt/NaN values normalize into range | `NormalizeFactor_SnapsToHalfStepsAndClamps`, `NormalizeFactor_RejectsNaN` |
| 1.5-step slider value 3.4 can't leak through | `NormalizeFactor_SnapsToHalfStepsAndClamps` |
| Rounding is deterministic, not engine-dependent | `ScaleSize_RoundsHalfAwayFromZero` + `ScaleSize_StaticTwoArg` |
| Build-time geometry is the 1× base; a runtime-spawned window under 2× scales correctly (the adversarial leg the headless factor-1 bias can't fake) | Part 1C Task 5 step 2b (in-engine) |

---

### Task 2: `WindowPlacement` — saved-quad placement model (`ResolveScaled`) + margin-scaling policy — SC-06, SC-07

**The model (review finding — capturing only the pre-commit size cannot round-trip):** a scale commit must re-derive every window's position from a **stable source**, because the persisted position goes STALE after any commit (nothing writes it back during apply). With only a captured old size, the 1×→2×→1× trace for the hotbar (`(520, 679)`, 351×36, 5px bottom margin, `DefaultWindowLayout.cs:14`) fails on the way back: the persisted Y is still 679 but the captured height is now 72 → `right = 720 − (679+72) = −31` → no round-trip. **Chosen model: persist a per-window QUAD — (position, size, factor, canvas) — and derive ALL placement from the quad + the current (Size, factor, canvas).** The quad changes only when the user ends a window drag. Because the quad is invariant across scale commits, commits round-trip exactly by construction, and canvas changes (resize, auto-threshold crossing) compose with factor changes in one call. **Margin policy (resolves the anchored-vs-persisted inconsistency): edge margins are LOGICAL UI PIXELS — they scale with the factor** (× `factor/savedFactor`, no rounding — see the spec below). Anchored roots (chat) already do this by construction (tscn offsets double at 2×); persisted windows now match. Middle-band windows keep their saved coordinate (unscaled) — the coordinate is the semantic there.

**Files:**
- Modify: `Scripts/UI/WindowPlacement.cs`
- Modify: `Scripts/CharacterSettings.cs` — (a) the window-settings record (returned by `GetWindowSettings`, `CharacterSettings.cs:144`) gains ADDITIVE **non-nullable** fields `Size` (Vector2 — display px at save time), `Factor` (float), and `Placed` (bool): value types default to **ZERO / false** when the key is absent (not null), and the serializer emits the declared PascalCase member names — JSON keys `"Size"`/`"Factor"`/`"Placed"` per window section (verify against the existing JSON fixtures). `Placed` is the placement-valid marker (review finding — `Position == default` cannot mean "no saved position": a window legitimately dragged to (0,0) would be replaced by its default layout on next launch): it distinguishes (a) a visibility-only record with no placement (`Placed` false, `Position` zero → default layout), (b) a legacy record (`Placed` false, `Position` non-zero → the position IS honored, with the legacy size/factor fallbacks), and (c) a valid quad whose position happens to be (0,0) (`Placed` true → trust the quad wholesale). Part 1B Task 3 falls back when `Size == default` / `Factor <= 0`. (b) NEW method `SetWindowVisible(string windowName, bool visible)` — a visibility-ONLY update: writes `Visible` and leaves `Position`/`Size`/`Factor`/`CanvasSize` untouched (the toggle/close path must not corrupt the quad — review finding: the existing toggle saves live Position + CanvasSize without Size/Factor, producing mixed-coordinate quads after scaling and breaking first-time close at 2×). The full quad is persisted ONLY by the drag-end `SetWindowSetting` call (all four fields atomically, plus `Placed = true`).
- Modify: `Scripts/UI/DefaultWindowLayout.cs` — new pure `public static Vector2? LegacySize(string windowName)`: the pre-feature tscn size for windows whose SAVED positions predate the `Size` key while their tscn has since changed — today exactly `Options → (240, 112)` (Part 2 Task 4 grows that tscn to 240×240; a legacy saved position was captured with the window 112 tall, and falling back to the LIVE `_tscnSize` would misinterpret its y-margin by 128px, breaking the legacy-place-identically guarantee). All other windows → null (caller uses live `_tscnSize`).
- Test: `tests/Goose2Client.Tests/DefaultWindowLayoutTests.cs` (extend — it exists): `LegacySize_Options_Is240x112`; `LegacySize_UnlistedWindow_IsNull`.
- Test: `tests/Goose2Client.Tests/WindowPlacementTests.cs` (extend)
- Test: `tests/Goose2Client.Tests/CharacterSettingsJsonTests.cs` (extend — the schema change lands here): `WindowSettings_SizeFactorPlaced_RoundTrip` (all three fields persist and reload); `WindowSettings_LegacyJsonWithoutSizeFactor` (a pre-feature JSON string deserializes with `Size == default, Factor == 0, Placed == false`, nothing else disturbed); `WindowSettings_SavedOriginRoundTrips` (review: `Placed = true` with `Position == (0, 0)` serializes and reloads intact — the saved origin is a valid position, not an absence); `SetWindowVisible_PreservesFullQuad` (save full quad → `SetWindowVisible(false)` → reload → all four quad fields + `Placed` byte-identical, `Visible` flipped, and a FIRST-TIME visibility write on an unplaced record leaves `Placed == false`); `SetWindowSetting_DragSave_UpdatesAllFiveAtomically` (one call updates Position/Size/Factor/CanvasSize + `Placed = true` together, `Visible` preserved).

**New API** (old 4-arg form delegates — the delegation is mathematically IDENTICAL for `savedFactor == factor == 1`, `savedSize == windowSize`, so all 340 baseline tests stay green untouched):
```csharp
public static Vector2 Resolve(Vector2 savedPos, Vector2 windowSize, Vector2I savedCanvas, Vector2I currentCanvas)
    => ResolveScaled(savedPos, windowSize, 1f, savedCanvas, windowSize, 1f, currentCanvas, TitleBarHeight);

public static Vector2 ResolveScaled(Vector2 savedPos, Vector2 savedSize, float savedFactor, Vector2I savedCanvas,
    Vector2 windowSize, float factor, Vector2I currentCanvas, int titleBarAllowance = TitleBarHeight)
```
`ResolveAxisScaled(saved, savedSize, size, marginScale, savedEdge, currentEdge)` with `marginScale = factor / (savedFactor > 0 ? savedFactor : 1f)` (guard — corrupt settings). Same branch structure as today's `ResolveAxis` (`WindowPlacement.cs:46-62`), with **every saved-space quantity derived from `savedSize`** (the band test AND the trailing offset): `left = saved`, `right = savedEdge − (saved + savedSize)`. Branches in order: (1) **band** — `left >= 0.25·savedEdge && right >= 0.25·savedEdge` (the existing `MiddleBandEdgeFraction = 0.25f` — NOT a pixel threshold) → keep `saved` UNSCALED; (2) `left < right` (leading) → `left × marginScale`; (3) `right < left` (trailing) → `currentEdge − size − (right × marginScale)`; (4) equidistant → keep `saved`. **NO rounding on any branch** (review finding — the existing code rounds nothing per-axis): at `marginScale == 1` every branch reduces to today's `ResolveAxis` EXACTLY (`x × 1.0f == x`), so the 4-arg delegation preserves the documented "identity when savedCanvas == currentCanvas" postcondition even for fractional drag-saved positions; rounding here would break that and drift legacy fractional saves by up to 0.5px on every canvas resize. Float positions are consistent with the drag-save format (positions are already float there). Containment clamp: `x ∈ [0, max(0, cur.X − windowSize.X)]`, `y ∈ [0, max(0, cur.Y − titleBarAllowance)]` (allowance scaled at the call site — `BaseWindow` passes `applier.ScaleSize(24)`, Part 1B Task 3).

**Step 1: Failing tests** (pure — the tiny headless canvas can't express margins, so the whole model is pinned here):
C720 = the 1280×720 canvas; band threshold = 25% of the saved edge (320 on x, 180 on y at C720). Every expected value below is derived from the branch structure above (saved-space left/right, 25% band, leading/trailing scaled re-stick, equidistant-keep, NO rounding, then containment clamp) — recompute by hand before implementing; a test whose arithmetic doesn't follow the spec rejects the spec itself (review finding).
- `Resolve_DelegatesToResolveScaled`: the 4-arg form equals `ResolveScaled` with `savedSize == windowSize, factors 1` across a position/size/canvas sample INCLUDING FRACTIONAL positions (the no-rounding rule makes the delegation exact for every float input, not just integers).
- `ResolveScaled_HotbarCommitAt2x` (real hotbar quad `(520, 679)`, 351×36, f1, C720; at `(702, 72), factor 2`, C720): x — left 520 ≥ 320, right `1280 − (520+351) == 409` ≥ 320 → **band → kept 520** (clamp bound `1280 − 702 == 578` — fits); y — top 679 ≥ 180, bottom `720 − (679+36) == 5` < 180 → trailing → `720 − 72 − (5×2) == 638` → **`(520, 638)`** (the 5px logical bottom margin doubles to 10).
- `ResolveScaled_ScaleCommitRoundTrips` (the F1 trace, pinned): the SAME invariant quad: `@1×` `(351,36)` → x band-kept 520, y `720 − 36 − 5 == 679` → **exactly `(520, 679)`**; `@1.5×` `(527, 54)` → x band-kept 520, y `720 − 54 − (5×1.5 == 7.5) == 658.5` → **`(520, 658.5)`**; `@2×` → `(520, 638)`. The quad never changes across commits, so deriving at the saved factor returns the saved position EXACTLY — a capture-only or stale-position model cannot satisfy this.
- `ResolveScaled_DragAtScale_CommitAndRoundTrips`: quad `((800, 600), (400, 72), 2, C720)` (a 400-wide window dragged at 2×): commit to 1× `(200, 36)`, ms 0.5 — x: left 800 ≥ 320, right `1280 − 1200 == 80` < 320 → not band; 80 < 800 → trailing → `1280 − 200 − (80×0.5) == 1040`; y: top 600 ≥ 180, bottom `720 − 672 == 48` < 180 → trailing → `720 − 36 − (48×0.5) == 660` → **`(1040, 660)`**; back at 2× `(400, 72)`, ms 1 → x `1280 − 400 − 80 == 800`, y `720 − 72 − 48 == 600` → **exactly `(800, 600)`**.
- `ResolveScaled_LeadingMarginScales`: quad `((100, 679), (351, 36), 1, C720)` at `(702, 72) @ 2`, C720: x — left 100 < 320 → not band; left < right (829) → leading → `100×2 == 200` (clamp bound 578 — fits); y — bottom 5 → trailing → 638 → **`(200, 638)`** (a leading margin scales exactly like a trailing one).
- `ResolveScaled_ClampWhenScaledWindowExceedsCanvas`: the same quad at `(702, 72) @ 2` on a `1280×60` canvas: y — trailing → `60 − 72 − 10 == −22` → clamped to `[0, max(0, 60−24) == 36]` → **0** (window taller than the whole canvas → top-aligned, title bar reachable); x — leading 200 → **`(200, 0)`**.
- `ResolveScaled_TitleBarAllowance_Scaled`: quad `((100,700),(100,100),1,C720)`, `(100,100)@1`: x — left 100 < 320 → leading → 100; y — top 700 ≥ 180, bottom `720 − 800 == −80` → trailing → `720 − 100 − (−80×1) == 700` → clamped → y **`696`** (allowance 24) / **`672`** (allowance 48).
- `ResolveScaled_CorruptSavedFactorFallsBackTo1`: `savedFactor 0`/`−1` behaves like 1 on a sample.

**Step 2 (red):** `ResolveScaled` doesn't compile (red).
**Step 3:** implement `ResolveScaled` + `ResolveAxisScaled`; re-implement the 4-arg as the delegation; keep `Center`, `LegacyCanvas`, `TitleBarHeight` untouched.
**Step 4 (green):** full `WindowPlacementTests` (existing 14 tests MUST stay green unmodified + new).
**Step 5:** commit `feat: saved-quad placement model (ResolveScaled) with factor-scaled edge margins`.

**Mutation impact (spans Part 1B Tasks 1/3, pinned here for traceability):** every production placement site (registration reposition, scale commit, canvas-resize walk) converges on ONE method — `BaseWindow.RepositionFromSaved()` (Part 1B Task 3) — which reads its quad and calls `ResolveScaled` with its live `Size`, the live factor, and the live canvas. No live-rect capture, no previous-canvas tracking, no per-path size bookkeeping.

---


---

## Invariant-to-test matrix (Part 1A)

| Invariant | Proved by |
|-----------|-----------|
| Factor normalizes all sources (slider, auto, corrupt save) — SC-02 | Task 1 `NormalizeFactor_*` |
| Auto boundaries 720/1080/1440 + 4K clamp — SC-02 | Task 1 `AutoFactor_Boundaries` |
| Rounding policy is pinned, engine-independent — SC-02 | Task 1 `ScaleSize_RoundsHalfAwayFromZero` |
| Build-time geometry is the 1× base — a runtime-spawned window under a live 2× scales correctly (the regression no headless factor-1 test can fake) — SC-05 | Task 1 table + Part 1C Task 5 steps 2b & 3 (in-engine) |
| Placement survives scaled sizes (middle/edge/oversize) — SC-06 | Task 2 `Resolve_*` tests |
| Edge margins are logical px: they scale with the factor across a commit, and 1×→2×→1× round-trips EXACTLY (saved-quad model) — SC-06 | Task 2 `ResolveScaled_HotbarCommitAt2x` / `..._ScaleCommitRoundTrips` / `..._DragAtScale_CommitAndRoundTrips` / `..._LeadingMarginScales` / `..._ClampWhenScaledWindowExceedsCanvas` |
| Scaled y-containment uses scaled title bar — SC-06 | Task 2 `ResolveScaled_TitleBarAllowance_Scaled` |
| Legacy files place identically to today (no Size/Factor keys → tscn size, factor 1; Options via `LegacySize`) — SC-06 | Task 2 `CharacterSettingsJsonTests.WindowSettings_LegacyJsonWithoutSizeFactor` + `DefaultWindowLayoutTests.LegacySize_*` + the delegation test's fractional-position leg |
