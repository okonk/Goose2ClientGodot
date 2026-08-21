# UI Scale (World Subviewport — Stage 3) Design

**Date:** 2026-08-21
**Branch:** `feature/ui-scale`
**Supersedes:** the "Stage 3 (stub — plan later, possibly skip)" section of
`docs/plans/2026-08-19-world-subviewport-stage1.md`.

**Background:** Stage 1 moved the world into a `SubViewport` and made the root
viewport render at native pixels; Stage 2 moved in-world text to the native-resolution
`WorldTextBridge`. Everything left on the root viewport (HUD windows, tooltips, login,
loading) still uses the 720p-era pixel constants — text and windows are tiny on
1080p+/4K displays. Stage 3 adds a UI scale factor: a single knob that multiplies
theme font sizes and window size constants, applied live.

## 1. Scope

**In scope** — everything rendered on the root (native-pixel) viewport:

- All HUD window scenes (vitals, inventory, chat, spellbook, character, party, quest,
  vendor, bank, hotbar, options, debug, buff effects, NPC/combine windows)
- Tooltips (item/spell/map-item/text), spawned by `TooltipManager`
- Login scene and loading overlay
- Theme typography: `GameTheme.default_font_size` (base 10px) and all registered
  explicit font-size overrides

**Out of scope:**

- The world subviewport and everything in it, including `WorldTextBridge`
  (names, chat bubbles, battle text) — world-anchored; scales with the world.
- Window positions — Stage 1's edge-stick/clamp placement already adapts to canvas
  size; Stage 3 only feeds it the new sizes.
- New art assets or font files — existing LiberationSans is reused; existing
  icons/sprites are stretched.
- Reticle/cursor (none exists on the root viewport).

**Behavioral contract:** the visible factor is either the user's slider value or
`AutoFactor(window_h)`, clamped to 1–3. Every scaled pixel value is
`max(1, round(base × factor))`. No scaled value is computed outside the `UiScale`
math class.

## 2. `UiScale` pure math

New file `Scripts/UiScale.cs`. Pure, no Godot types, fully xUnit-covered (same shape
as `WorldViewportScale` / `WindowPlacement`). Small non-static class owned by the
applier (no hidden global state).

```
const Min = 1f, Max = 3f, Step = 0.5f

Factor(float raw)            // snap to 0.5 steps, clamp to [1, 3]
AutoFactor(int windowHpx)    // thresholds: h < 1080 → 1, h < 1440 → 2, else 3 (clamped)
ScaleSize(float basePx)      // max(1, round(basePx * factor))   (round = half-away-from-zero, pinned by test)
ScaleSizeI(Vector2I v)       // per-axis ScaleSize
```

- `Factor` is the single normalization entry point; auto and slider values both pass
  through it (corrupt saved values included).
- `AutoFactor` boundaries: 720–1079 → 1, 1080–1439 → 2, 1440+ → 3 (2880 → 3, clamped).
- Font sizes use the same `ScaleSize` — no separate font rounding rule.
- No division anywhere: placement math takes actual sizes, never the factor.

## 3. Registration and re-layout contract

### `IScalableWindow`

```csharp
public interface IScalableWindow { void Relayout(); }
```

- Each window's build code is refactored so all size/position/anchor-pixel/font-override
  assignments live in `Relayout()`. `Build()`/`_Ready` creates nodes once, then calls
  `Relayout()`.
- `Relayout()` reads the factor through the applier — a pure function of (base
  constants, current factor). Windows never store the factor except a cached
  "last factor I laid out at" used to skip re-creating expensive child content
  (slot grids, spell pages) when only placement re-solved.
- Node children are never recreated by a scale change: state (chat contents, selected
  pages, scroll offsets) survives; only geometry changes.

### Registration

- `UiScaleApplier` is a plain class (not a Node) with a `TooltipManager.Instance`-
  style static accessor, created in `GameManager._Ready` — persistent across map
  entries, same home as `WorldViewport`.
- Windows **self-register at end of their own `_Ready`** (R3) via `ScaleRegister()`
  (snapshot → `RegisterWindow` → `Relayout()` → `RepositionFromSaved()` — the window
  is placed at its FINAL scaled size, never the 1× tscn size; `ScaleRegister` is the
  SINGLE registration-time layout owner, `RegisterWindow` is bookkeeping only);
  `tree_exited` deregisters.
  `UnregisterWindow` removes the window AND its descendant font entries (fonts are
  recorded flat, owned per-window by ancestry — runtime NPC windows free their labels
  with them); the apply pass additionally prunes entries that fail
  `GodotObject.IsInstanceValid` as a backstop. `GameHud` is never freed/rebuilt
  (guarded `EnsureHud`), so there is no rebuild-clear.
- Windows spawned mid-session (NPC windows on click) must build through the same
  Register → `Relayout()` path — **no window may build without registering.**
- Tooltips do **not** register with the SNAPSHOT (R2): their geometry is per-frame
  C# (skip meta). They are NOT exempt from scaling — each tooltip control computes
  its layout every frame from factor-scaled constants (pure `TooltipMetrics`: item
  40/9/46/48/+4, spell/text 8×4 pad, map-item 6/4/2/4 margins + 400px widths, item
  icon 32px@4), so a re-shown tooltip at 2× has 2× fonts AND 2× box/padding/icon.
  Live tooltips are hidden on apply (no per-frame reflow mid-commit) and re-shown on
  next hover at the live factor.
- **Dynamic post-snapshot geometry** (same class of bug as tooltips): `VitalsCharacterDisplay.SetLayer`
  repaints the portrait from 1× constants (53px circle, 20px drop) on every character
  update, after the window snapshot — it routes through pure
  `VitalsPortraitMetrics.Layout(texSize, factor)`, and `VitalsWindow.Relayout()` re-runs
  the portrait pass.
- Login scene and loading overlay register in `_Ready`, deregister in `_ExitTree`.

**Implementation refinements (ratified in the part-1 plan — `2026-08-21-ui-scale-part1-foundation.md`):**
- R1: instead of per-window hand-written constants in `Relayout()`, a generic
  `UiScaleLayout` snapshot at end-of-`_Ready` IS the 1× base (build code writes 1×
  constants — `.tscn` offsets load at 1× regardless of factor; **no** divide-by-factor
  recovery — an early draft of the plan had that and it was wrong: it would have
  un-scaled the HUD on real 1080p startups while every headless test stayed green).
  The snapshot records anchor-relative **offsets** (anchored roots like ChatWindow's
  bottom-left or Toolbar's right-edge would detach if `Position` were scaled), EXCEPT
  for children whose parent is a `Container` (`ContainerManaged`: the container owns
  their offsets — writing them back is async-flaky; their scaling rides on
  min-sizes + separation constants, and the container re-derives the offsets itself).
  `ScaleRegister()` calls `Relayout()` once, so runtime-spawned windows scale in the
  same frame.
- R2: live tooltips hidden on apply; factor-aware per-show layout (body above).
- R3: windows self-register at end of `_Ready` (GameHud does not enumerate).
- R4: `UiScale` separates state and pure functions — `CurrentFactor` (plain state,
  applier is the only writer) + `static NormalizeFactor(raw)` / `static AutoFactor(h)`;
  `ScaleSize*` read `CurrentFactor`. (A `Factor` property + `Factor(float)` method is
  a C# CS0102 error — verified — and the old naming was semantically ambiguous.)
- R5: **saved-quad placement model** (a captured old size alone cannot round-trip —
  the persisted position goes stale after any commit): each window persists a QUAD
  — (position, size, factor, canvas) — at drag-end, and EVERY placement (registration,
  scale commit, canvas resize, auto-threshold crossing) derives from
  `WindowPlacement.ResolveScaled(quad + live Size/factor/canvas)`. The quad is
  invariant across commits — it changes ONLY at drag-end (visibility toggles/close
  persist `Visible` only; a toggle path that also saved live position + canvas would
  yield mixed-coordinate quads after a scale commit) — so 1×→2×→1× round-trips
  exactly by construction; canvas
  and factor changes compose in one call (no old-canvas tracking, no live-rect
  capture). **Margin policy: edge margins are LOGICAL UI PIXELS — they scale with the
  factor** (× `factor/savedFactor`), matching anchored roots (chat's tscn margins
  double at 2×); middle-band windows keep their saved coordinate (unscaled). Legacy
  settings (no size/factor keys) fall back to (tscn size, 1) — the true pre-feature
  pair, so old files place identically.
- `UiScaleApplier` is a plain class with a `TooltipManager.Instance`-style static
  accessor, created in `GameManager._Ready` (not a Node).

### Fonts — two tiers

1. **Default-size text** (the majority): the applier sets
   `GameTheme.default_font_size = ScaleSize(10)` on every apply pass; the shared Theme
   resource propagates live to every themed control. Nothing to register.
2. **Explicit overrides**: all raw `AddThemeFontSizeOverride` calls in window code
   (e.g. `BaseMultipleWindow` button/line sizes) convert to
   `UiScaleApplier.ApplyFontSize(Control c, float basePx)`, which sets the override to
   `ScaleSize(basePx)` *and* records `(c, basePx)`. `ApplyFontSize` is the only way
   window code sets a font size.
   - Bridge text (`BridgedNameLabel`, `ChatBubble`, `BattleTextLine`) does **not**
     use it — world-space, out of scope.
   - `Login.tscn` and `LoadingMap.tscn` attach no theme of their own, but
     `project.godot:37` sets `theme/custom` **project-wide**, so their text already
     resolves through `GameTheme` at effective `font_size == 10` (headless-probed).
     Tier 1 therefore reaches them with **no per-scene work** — no `ApplyFontSize`
     entries, no theme attaching (an earlier draft assumed the 16px engine default
     here; the probe disproved it — the part-2 plan's Task 5 pins 10→20→10).
     Their geometry (the `MarginContainer` offsets, VBox `separation`) scales via
     the standard snapshot registration.

### Apply pass — the single mutation point

`UiScaleApplier.Apply(factor, reason)`, in order:

1. `f = UiScale.NormalizeFactor(factor)` (pure); early-return if `f == CurrentFactor`
   (not on the first apply); set `CurrentFactor = f` — the applier is the only writer.
2. Cancel any in-progress **window move-drag** (the only mouse-follow drag with state —
   `BaseWindow._dragging`; cancel = restore pre-drag position, persist nothing, flag
   cleared on the next press). The restore is an INTERMEDIATE step: step 7 then
   re-derives every window from its UNCHANGED quad at the new factor, so the final
   position is `ResolveScaled(quad, newFactor)` — equal to the pre-drag pixel only when
   the factor didn't change; the in-flight drag position is never persisted. Godot's
   built-in item/spell DnD has no cancel API and
   cannot realistically co-occur with a scale commit (both need the left button) —
   accepted limitation, see §7.
3. Hide live tooltips (R2).
4. Set `GameTheme.default_font_size`; re-apply all registered explicit overrides
   (pruning invalid entries via `IsInstanceValid`).
5. Call `Relayout()` on all registered windows (HUD, login/loading; geometry only).
6. Placement: every registered `BaseWindow` calls `RepositionFromSaved()` — it reads
   its OWN persisted quad and resolves via the pure `ResolveScaled(quad + live
   Size/factor/canvas, titleBarAllowance: ScaleSize(24))` (R5). All windows re-solve
   — no opt-out. Registration uses the same call AFTER its `Relayout()` (a window is
   placed at its FINAL scaled size, never at the 1× tscn size), and the
   canvas-resize walk is the same call — one placement method, three callers.

Order matters: fonts before `Relayout` (minimum-size queries see correct values),
placement last (needs final sizes).

## 4. Live change paths

Both triggers funnel through `Apply(factor, reason)`.

**1. Slider commit (options window).**

- While dragging: only the slider's value label updates; nothing else happens.
- On mouse release: if value ≠ committed factor → save → `Apply`.
- Keyboard/programmatic change: `value_changed` with no pointer drag in progress →
  commit immediately. Rule: commit on `value_changed` iff not dragging, else on
  release. (Mechanism note: `HSlider`/`VSlider` expose C# `DragStarted`/`DragEnded`
  events in 4.7.1 — reflection + runtime `get_signal_list()` verified; they are
  generated on the slider types, not on `Range`, which is where an earlier check
  wrongly looked. The commit uses `DragEnded`, with the `BaseWindow`
  `GuiInput`+`Input.IsMouseButtonPressed` poll as fallback only if M2 shows
  release-outside-control doesn't fire it. See the part-2 plan, APIs verified.)

**2. Auto mode + window resize.**

- On Auto, the window `size_changed` signal drives: `AutoFactor(newHeight)`; if it
  differs from the committed factor → `Apply(newFactor, AutoResize)`.
- Auto factors only change at the 720/1080/1440 height boundaries; the compare-and-
  skip makes a drag-resize cost one int compare per frame. No debounce.

**Commit-time safety:**

- A window move-drag in progress at commit time is cancelled (Section 3 step 2); the
  move never "finished", so `savedPos` is unchanged and the re-solve is a no-op.
- `ScrollContainer` children are not recreated by `Relayout` — chat does not jump to
  the top.

**Startup order:** settings load → initial `Apply` → scene/HUD build. The factor is
set before any window registers, so the first build is already scaled; no unscaled
flash.

## 5. Options window UI

- New **UI Scale** group in the Options window: mode `Auto` (default) / `Manual`. The
  mode pair is two `CheckBox`es in ONE `ButtonGroup` with `AllowUnpress = false`
  (reflection-verified group-level property in the 4.7.1 binding) — widget-enforced
  exactly-one-selected, so "user unchecks the currently selected mode" is impossible;
  the handler acts only on the newly-pressed box (`IsPressed == true`), and the initial
  `ButtonPressed` sync in `_Ready` happens BEFORE the `Toggled` handlers connect.
- Slider visible only in Manual mode: 1.0–3.0, 0.5 steps, value shown as `1.5×`.
  In Auto mode the group shows the effective factor (`Auto (2×)`). The `DragEnded`
  handler's signature is `void (bool valueChanged)` (the binding's delegate — verified);
  the commit is unconditional.
- Pending-while-dragging per Section 4.
- **Persistence:** `UiScaleMode` (enum) + `UiScaleValue` (float) added to the
  existing options settings save path that `OptionsWindow` already uses (plan phase
  confirms the exact struct; no new file). Corrupt values pass through
  `UiScale.NormalizeFactor` at load (`4.2 → 3`, `-1 → 1`).
- **First-run default:** Auto (720p → 1×, no change; 1080p → 2×, the point of the
  feature).
- The Options window scales like every other window, including live resize on its own
  commit (accepted).
- Window positions keep using the existing saved-canvas placement file unchanged.

## 6. Testing

**xUnit (`tests/Goose2Client.Tests`), pure:**

1. `UiScale`: `Factor` snap/clamp table; `AutoFactor` boundaries incl. 2880→3 clamp;
   `ScaleSize` half-away-from-zero rounding pin and min-1 guard.
2. `WindowPlacement` new cases for changed sizes: middle-parked window keeps its
   coordinate when its size doubles; edge-stuck window keeps its edge offset at 2×/3×;
   a window larger than the canvas at 3× clamps to (0,0).
3. Normalization of corrupt saved values (incl. NaN-safe).

**Headless/runtime (`tools/tests/` pattern, like `scene_lifecycle.gd`):**

4. Font-registry audit: build HUD at factor 2, walk all `Control`s, assert any control
   with a `font_size` override is in the applier's registry. Fails if a future PR adds
   a raw `AddThemeFontSizeOverride`.
5. No-unscaled-flash: start with settings pinning Manual 2×; before the first HUD
   frame, theme `default_font_size == 20` and sampled window sizes equal
   `round(base × 2)`.
6. Live-change smoke: commit 1→2 on a built HUD; window sizes updated, placements
   re-solved (edge-stuck edge offset unchanged), no script errors, chat scroll offset
   preserved. The interrupted-drag sub-case is tested by directly invoking the cancel
   path on a fake in-flight drag (synthetic mouse interleaving left to manual).

**Manual / in-engine:**

- 720p / 1080p / 1440p + non-16:9 windows: login, HUD, every window type, tooltips at
  3×, slider drag-release, keyboard nudge, auto boundary crossing by window resize,
  window move-drag interrupted by a scale commit, save/reload persistence.
- Icon crispness at 1.5× specifically (user-accepted risk; this is where the 0.5-step
  choice gets its verdict).

## 7. Accepted limitations / deferred

- **3× on a small window:** HUD can exceed the canvas (e.g. 3× Manual at 720p);
  `WindowPlacement` clamps and overflow is unreachable. Accepted — 3× is an explicit
  user choice and Auto never produces it below 1440p. Fit-guarantee deferred.
- **OS-level DPI scaling vs Auto:** Godot's reported window height may be in scaled
  pixels on some Windows DPI setups, so Auto may land one step off. Accepted;
  one-line fix later (e.g. `DisplayServer` content scale) if it actually bites.
- **Icon softness at 1.5×:** inherent to stretching sprite textures at a fractional
  factor. Accepted by design decision (Q3: start with 1–3 in 0.5 steps, revisit).
- **Tooltip clamping at large scale:** tooltips clamp to the viewport, consistent
  with existing tooltip behavior.
- **Item/spell DnD not cancelable on commit:** Godot's built-in drag-and-drop has no
  cancel API; a scale commit cannot realistically co-occur with an in-flight DnD (both
  hold the left button). Window move-drag IS cancelled (it is the only stateful
  mouse-follow drag).
- **Dev build stamp stays 1×:** `BuildStampOverlay` (root-viewport label) is not
  registered — it is a dev-only stamp, intentionally unscaled. Mechanism: a FIXED local
  `AddThemeFontSizeOverride("font_size", 10)` on its label — the applier mutates the
  shared project theme's DEFAULT font size, so any control without a local override
  scales with it. Self-test asserts 10 at 2× (Part 1 Task 9/10).
- **PartyMember tiles are the one exception to the container-managed skip:** their
  87×33 exists only as tscn offsets (no `CustomMinimumSize`), so a scalable
  min-size + scaled internal offsets via pure `PartyMemberMetrics` (Part 1 Task 9);
  self-test asserts (174, 66) at 2×.

## 8. Rejected alternatives

- **Per-window multiplication** (each of ~16 windows multiplies its own constants):
  rounding/clamping policy copy-pasted across 20+ call sites; 16 separate live-update
  hooks. Rots.
- **Scaling the HUD root `Control` node:** free uniform scale, but fonts
  rasterize-then-scale (blur at 1.5×), `WindowPlacement` canvas-coordinate math
  breaks, tooltip anchoring needs inverse transforms, and it fights the native-
  pixels philosophy Stages 1–2 established.
- **Explicit `ApplyFontSize` bases for login/loading (e.g. base 16):** their text
  already resolves through the project-wide theme at 10px, so explicit overrides
  would change 1× appearance — the part-2 Task 5 round-trip test is the guard.
- **Integer-only factors (1–4):** crisper icons but no in-between sizes; the chosen
  1–3 in 0.5 steps is the compromise, with the clamp keeping Auto integer.
