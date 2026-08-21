# UI Scale Part 2 — Product Implementation Plan

**Goal:** The user-facing surface: Auto/Manual mode + slider in the Options window, settings persistence, auto-scale on window resize, commit-time drag cancellation, login/loading scaling, and the manual verification matrix. Builds directly on Part 1's `UiScale` / `UiScaleApplier` / `UiScaleLayout` machinery.

**Architecture:** A pure `UiScale.Resolve(mode, savedValue, windowHeight)` decides the target factor from (mode, persisted slider value, window height); `GameManager`'s startup `Apply(1f, Startup)` (pinned in Part 1, `Scripts/GameManager.cs` HUD-build site) becomes settings-driven, and the existing `window.SizeChanged` handler (`Scripts/GameManager.cs:103,337`) gains the auto-recompute leg. The Options window gains a UI Scale group (two exclusive mode checks + a 0.5-step slider that commits on `drag_ended` or on non-drag `value_changed`), persisting through the existing `CharacterSettings.Options` dictionary. Login/loading scenes self-register (Part 1 pattern) and get explicit `ApplyFontSize` bases of 16 (engine default — they attach no `GameTheme`).

**Tech Stack:** C# / Godot 4.7.1 (GodotSharp), xUnit, headless self-test (Part 1's `tools/tests/run_ui_scale.sh`), in-engine manual matrix.

---

## APIs verified (citations)

- Part 1's verified surface applies unchanged (`AddThemeFontSizeOverride`, `Theme.SetDefaultFontSize`, `HasThemeFontSizeOverride`, `OS.GetCmdlineUserArgs`, `SetMeta`/`GetMeta`, `Node.TreeExited`).
- `HSlider` (GodotSharp 4.7.1, same DLL): properties `MinValue`, `MaxValue`, `Step`, `Value`; C# events `ValueChanged` (verified) and `GuiInput`. **`Range.DragEnded` DOES NOT EXIST in this binding or engine** — verified two ways: reflection on the DLL (Range events: `ValueChanged, Changed, Resized, GuiInput, ...` — no drag signal) and a headless runtime probe (`get_signal_list()` on a live `Range`: ends `...,value_changed,changed,` — no `drag_ended`). Drag state must therefore be tracked with the exact pattern `BaseWindow` already uses for window moves (`Scripts/UI/BaseWindow.cs:120-140`): left-press via `GuiInput` sets `_dragging = true`; a `_Process` poll of `Input.IsMouseButtonPressed(MouseButton.Left)` detects the release (works even if the cursor leaves the slider). This is Task 4's commit mechanism.
- `CharacterSettings.Options` — `Dictionary<string, object>`, `GetOption<T>(key, default)`, indexer-set + `Save()` — `Scripts/CharacterSettings.cs:42-67,144`. Existing usage pattern: `Scripts/UI/OptionsWindow.cs:23-49` (checkbox read in `_Ready`, write in toggled handler, `Save()` on close/toggle — `OptionsWindow.cs:56-60`).
- `Constants.cs:136` — `public static class Options` (string keys: `TargetFiltering`, `ShowSpiritBar`, `SpiritBarShown`, `RenderMode`).
- `OptionsWindow.tscn` — checkboxes are `Content/*Check` nodes; window root is a `BaseWindow` (self-registered by Part 1, so the new group scales automatically).
- Window resize: `GameManager.cs:103` (`window.SizeChanged += OnWindowResized`), handler `Scripts/GameManager.cs:337-345` (guards: `UiLayer == null`, canvas < 2; walks `CollectBaseWindows`).
- Only drag state in the codebase: `BaseWindow._dragging` (`Scripts/UI/BaseWindow.cs:122-140`) — press sets true, release persists + clears, motion accumulates `Position += motion.Relative`. No other mouse-follow drags exist (hotbar swap is click-based, `Scripts/UI/HotbarSwap.cs`).
- Login: `Scenes/Login.tscn` — `MarginContainer` (anchor 0.5, offsets ±150/±100) → `VBox` (`theme_override_constants/separation = 10`) → `NameInput`, `PasswordInput`, `LoginButton`, `StatusLabel`; script `Scripts/LoginScene/LoginScene.cs`; **no `GameTheme` attached** (engine default font, size 16).
- Loading: `Scenes/LoadingMap.tscn` — single `StatusLabel` (anchor 0.5, offsets ±150/±10), script `Scripts/LoadingMapScene/LoadingMapScene.cs`; **no theme**.

---

### Task 1: pure factor resolution + settings keys + startup read

**Files:**
- Modify: `Scripts/UiScale.cs`
- Modify: `Scripts/Constants.cs:136`
- Modify: `Scripts/GameManager.cs` (Part 1's startup `Apply(1f, Startup)` line)
- Test: `tests/Goose2Client.Tests/UiScaleTests.cs` (extend)

**Step 1: Failing tests.**

Add to `UiScale` (pure, Godot-free): `public enum UiScaleMode { Auto = 0, Manual = 1 }` and
`public float Resolve(UiScaleMode mode, float savedValue, int windowHeightPx)` —
`Auto → AutoFactor(windowHeightPx)`; `Manual → Factor(savedValue)`.

- `Resolve_AutoIgnoresSavedValue`: `Resolve(Auto, 2.5f, 1080) == 2f`, `Resolve(Auto, 1f, 1440) == 3f`.
- `Resolve_ManualIgnoresWindowHeight`: `Resolve(Manual, 1.5f, 720) == 1.5f`, `Resolve(Manual, 3.4f, 720) == 3f` (corrupt value normalizes through `Factor`).
- `Resolve_ManualNaN`: `Resolve(Manual, float.NaN, 1080) == 1f`.

**Step 2 (red)** → **Step 3:** implement; add keys to `Constants.cs:136`:
```csharp
public const string UiScaleMode = "UiScaleMode";   // int: 0 = Auto (default), 1 = Manual
public const string UiScaleValue = "UiScaleValue"; // float, snapped by UiScale.Factor
```
**Step 4:** GameManager startup site (Part 1 line):
```csharp
var cs = CharacterSettings;
var mode = (UiScaleMode)cs.GetOption<int>(Options.UiScaleMode, (int)UiScaleMode.Auto);
var saved = cs.GetOption<float>(Options.UiScaleValue, 1f);
var canvas = (Vector2I)GetTree().Root.GetVisibleRect().Size;
UiScaleApplier.Instance.Apply(UiScale.Instance.Resolve(mode, saved, canvas.Y), ApplyReason.Startup);
```
(`UiScale.Instance` = the applier's `Scale` property — use `UiScaleApplier.Instance.Scale.Resolve(...)`; `Resolve` is an instance method reading nothing but arguments, so either exposure is fine — prefer calling it statically-style through the applier's `Scale` instance as shown.) Note the headless self-test (Part 1 Task 8) still passes: default settings → Auto → headless canvas is tiny (not 720 — headless windows are minimal) → `AutoFactor → 1` → identical to Part 1's pinned `Apply(1f, Startup)`.

**Step 5 (green) + commit:** `feat: settings-driven UI scale factor at startup`.

| Invariant | Proved by |
|-----------|-----------|
| Auto ignores stale slider values (and vice versa) | `Resolve_AutoIgnoresSavedValue` / `Resolve_ManualIgnoresWindowHeight` |
| Corrupt persisted value can't escape [1,3] | `Resolve_ManualNaN`, `Resolve_Manual...3.4f` |
| Headless/default path unchanged (Part 1 tests stay green) | Part 1 Task 8 command re-run |

---

### Task 2: commit-time drag cancellation (`BaseWindow.CancelDrag`)

**Files:**
- Modify: `Scripts/UI/BaseWindow.cs:110-140`
- Modify: `Scripts/UiScaleApplier.cs` (apply pass step 2)

**Mutation impact:**
- Source of truth: `BaseWindow._dragging` + the live `Position` during a move-drag (`Scripts/UI/BaseWindow.cs:122-140`).
- Readers: the drag's own release handler (persists `Position` to `CharacterSettings` via `SetWindowSetting`); `RepositionForCurrentCanvas` (reads `Size`, not `Position` mid-drag).
- Derived state: the persisted `WindowSettings.Position` — **must not record the mid-drag position**; cancel must prevent the release-persist from firing for a cancelled drag.
- Propagation: (1) on press, store `_preDragPosition = Position`; (2) `CancelDrag()`: if `_dragging` → `_dragging = false; _dragCancelled = true; Position = _preDragPosition;` (3) apply pass: before hiding tooltips, call `CancelDrag()` on every registered `BaseWindow` (cast; non-BaseWindow `IScalableWindow`s skip); (4) **persistence suppression is mandatory, not incidental**: the title-bar release handler (`Scripts/UI/BaseWindow.cs:118-142`) calls `SetWindowSetting` **unconditionally** on left-button release — it never checks `_dragging` — so without a guard the user's eventual mouse release (after the scale commit has already restored `Position`) would still fire a persist. Guard BOTH release branches (the `GuiInput` release and the `MouseMotion` escape) with `if (!_dragCancelled)` and clear the flag when consumed. (In the pure case the value equals the re-resolved position so the observable outcome would coincidentally match — but the flag makes the invariant airtight and covers the `Visible`/canvas fields persisted in the same `SetWindowSetting` call.) The subsequent `Relayout()` + `RepositionForCurrentCanvas()` re-solves from the untouched saved position.
- Invariants: a scale commit mid-move leaves the saved settings unchanged and the window at its pre-drag position; a *completed* drag (released before the commit) persists as today.
- Observable proof: Task 5's in-engine check M6.

`public void CancelDrag()` postcondition: `_dragging == false` on return; `Position` equals the value before the drag's first press. Idempotent when not dragging.

**Gate:** xUnit green (no new pure surface); in-engine M6.
**Commit:** `feat: cancel in-flight window move-drag on scale commit`.

---

### Task 3: auto-scale on window resize

**Files:**
- Modify: `Scripts/GameManager.cs:337` (`OnWindowResized`)

**Change:** inside the existing guard block, before/after the reposition walk (order: recompute FIRST so `Relayout` uses the new factor, then the existing `RepositionForCurrentCanvas` walk — which `Relayout` itself already triggers for `BaseWindow`s; to avoid double reposition, structure as: if auto-factor changed → `Apply(newFactor, AutoResize)` (which repositions via `Relayout`); else → existing walk only):
```csharp
var applier = UiScaleApplier.Instance;
if (applier != null && applier.Mode == UiScaleMode.Auto)
{
    var f = applier.Scale.AutoFactor(canvas.Y);
    if (f != applier.Factor)
    {
        applier.Apply(f, ApplyReason.AutoResize);
        return;   // Apply already re-laid-out + repositioned every window
    }
}
// existing CollectBaseWindows walk (unchanged)
```
`applier.Mode` = the mode stored at startup (Part 1 Task 1) and updated by the Options window (Task 4) — add `public UiScaleMode Mode { get; set; }` to the applier (set in both places; it is UI-state, not scale-math).

**Why safe:** `OnWindowResized` fires on the root window's `size_changed` — user drag-resize emits many; the `f != applier.Factor` compare makes each a no-op except at the 720/1080/1440 boundaries (design Section 4, no debounce). The `Apply` early-return-on-unchanged-factor (Part 1 Task 3) is the second guard.

**Gate:** xUnit green; in-engine M4 (resize across 1080 → HUD rescales once, no flicker storm; back across → rescales back).
**Commit:** `feat: auto UI scale follows window height`.

---

### Task 4: Options window UI Scale group

**Files:**
- Modify: `Scenes/UI/OptionsWindow.tscn` — **(a) resize the root**: `offset_bottom = 112.0` → `240.0` (root offsets `0, 0, 240, 112` at `OptionsWindow.tscn:22-25`; `Background` and `Content` are full-rect anchored and follow; existing checkbox rows occupy y 28–108, so the new group gets y ≈ 120–232; the window's saved/default position re-resolves at the new size through the existing `RepositionForCurrentCanvas` — no extra work). **(b) add under `Content`**: a `Label` ("UI Scale"), two `CheckBox`es (`ScaleAutoCheck` checked by default, `ScaleManualCheck`), an `HSlider` (`ScaleSlider`: `min_value = 1.0`, `max_value = 3.0`, `step = 0.5`, `value = 1.0`), a `Label` (`ScaleValueLabel`, text `"1×"`). Layout with the existing checkbox rows' pixel style (the generic scaler handles sizing; tscn offsets are the base).
- Modify: `Scripts/UI/OptionsWindow.cs`

**Behavior spec (pinned — the drag-release contract from the design):**
- Mode is exclusive: checking one unchecks the other. `ScaleAutoCheck` → mode Auto; `ScaleManualCheck` → mode Manual (slider row becomes visible; in Auto the slider + value label are `Visible = false`).
- On open (`_Ready`, after the existing checkbox reads): read `Options.UiScaleMode` (default Auto) + `Options.UiScaleValue` (default 1); set check states, slider value, effective-factor display (`"Auto (2×)"` style text on `ScaleValueLabel` — the user always sees the in-force factor).
- **Initial-open guard (adversarial):** programmatic `ScaleSlider.Value = x` in `_Ready` fires `ValueChanged`, and with `_dragging == false` that would `Commit` on EVERY window open — in first-open Manual mode the mode-switch rule would double-commit on top. Set `_initializing = true` before the programmatic sets and clear it on the next `ProcessFrame` (one `await`); `ValueChanged` and the mode handlers early-return while it is set.
- **Commit rule** (no `drag_ended` in this engine — see APIs verified): track a local `_dragging` on the slider with the `BaseWindow` move-drag pattern (`Scripts/UI/BaseWindow.cs:120-140`):
  - `ScaleSlider.GuiInput`: left mouse button **press** → `_dragging = true`.
  - `_Process` (options window): `if (_dragging)` → live-update `ScaleValueLabel`; `if (!Input.IsMouseButtonPressed(MouseButton.Left))` → `_dragging = false; Commit((float)ScaleSlider.Value);` (release detected globally, even off-slider — same as window-move release).
  - `ValueChanged(v)`: update `ScaleValueLabel` ALWAYS (live feedback). If `!_dragging` (keyboard arrow / programmatic set) → `Commit(v)` — mirrors how `BaseWindow` treats non-drag state, and matches the design's "commit on `value_changed` iff not dragging" rule.
- `Commit(v)`: `snapped = UiScaleApplier.Instance.Scale.Factor(v)`; if `snapped != UiScaleApplier.Instance.Factor` → `Options[UiScaleValue] = snapped`, `Save()`, `UiScaleApplier.Instance.Apply(snapped, ApplyReason.UserCommit)`; always set `applier.Mode` + persist `Options[UiScaleMode]` on mode changes and `Save()` on window close/toggle (existing pattern, `OptionsWindow.cs:56-60`).
- Mode switch to Manual commits the current slider value immediately (`ApplyReason.UserCommit`); switch to Auto commits `AutoFactor(currentCanvasY)`.
- The Options window itself live-resizes on its own commit (accepted per design Section 5); the release-only commit means it never resizes under the dragging cursor.

**Gate:** xUnit green; in-engine M1–M3, M7.
**Commit:** `feat: UI scale options (auto/manual slider, drag-release commit)`.

---

### Task 5: login + loading registration

**Files:**
- Modify: `Scripts/LoginScene/LoginScene.cs` (`_Ready` end: Part 1 self-registration pattern — snapshot + `RegisterWindow`; `Relayout()` = `UiScaleLayout.Apply(_geom, factor)`; no reposition — the scene is full-rect anchored)
- Modify: `Scripts/LoadingMapScene/LoadingMapScene.cs` (same)
- Same files: explicit fonts — every text control gets `applier.ApplyFontSize(c, 16f)`: Login's `NameInput`, `PasswordInput`, `LoginButton`, `StatusLabel`; Loading's `StatusLabel`. Base 16 = engine default (`GetDefaultFontSize` without a theme — verify at implementation with a one-line headless probe if in doubt; the 1× no-op check below pins it).

**Why explicit (not theme):** neither scene attaches `GameTheme`, so `SetDefaultFontSize` never reaches them; attaching the theme would be a 16→10px regression for 1× users (design Gap 1). The `VBox` `separation = 10` constant scales via the snapshot's constants path (Part 1 Task 4).

**1× no-op proof (adversarial):** extend Part 1's self-test (same `+selftest=ui_scale` run — no separate mode). At startup the login scene IS the current scene (freed only on successful login), so the sequence gains a login phase around the existing HUD phases: (0) at startup factor 1, assert login baselines: `MarginContainer` size `300×200` (tscn ±150/±100), `NameInput.GetThemeFontSize("font_size") == 16` (pins the engine-default base), VBox separation `== 10`; (1) `Apply(2f)`: login `MarginContainer == 600×400`, `NameInput` font `== 32`, separation `== 20`, plus all Part 1 HUD assertions; (2) `Apply(1f)`: login values round-trip to `300×200` / 16 / 10 (a wrong base would not round-trip — the adversarial leg).

**Gate:** self-test exits 0 including the login legs.
**Commit:** `feat: scale login and loading screens`.

---

### Task 6: manual verification matrix (in-engine, headed)

**Files:** none (checklist task). Run the dev server on at least 720p and 1080p; record results in the PR description. `run.sh` lives in the main workspace (gitignored — absent from this worktree); from the worktree either copy it over or inline its two commands (`godot-mono --headless --path . --build-solutions --quit`, then `godot-mono --path . --gpu-index 1 --display-driver wayland`).

| # | Check | Pass condition |
|---|-------|----------------|
| M1 | 1080p first launch (default Auto) | Login + HUD render at 2× (fonts ~20px, windows 2× tscn sizes); no 1× flash at map entry |
| M2 | Options → UI Scale → drag slider 2× → 1.5×, release | HUD (incl. options window) rescales ONCE, on release; label follows thumb live during drag; arrow-key nudge applies immediately |
| M3 | Restart | Mode/value persist; factor applied before HUD build (no flash) |
| M4 | Auto mode: drag-resize window across 1080px height boundary | Exactly one rescale crossing each way; no per-pixel churn (watch for layout thrash while dragging) |
| M5 | 720p Manual 3× | HUD scales to 3×; oversized windows clamp on-screen (accepted-limitation behavior, design §7); 1.5× icons acceptably soft (the Q3 verdict) |
| M6 | Start moving a window (title-bar drag), then commit a scale change before releasing | Window snaps back to pre-drag position; saved settings unchanged (verify by restarting and comparing position) |
| M7 | Non-16:9 window (e.g. 1600×900) | World gutters per Stage 1; HUD placement correct at the auto factor (round(900/720)=1); tooltips clamp to viewport at 3× |
| M8 | Login screen at 1080p Auto | Login box 2× (600×400), text 32px; loading overlay scales during a map transition |
| M9 | Chat: scroll mid-log, commit scale change | Chat content + scroll offset preserved; reposition keeps edge-stuck offset (M-edge check) |
| M10 | `bash tools/tests/run_ui_scale.sh` (+ login leg) | Exit 0, no `ERR_` output — headless regression gate stays green |

**Done criteria for the part (and feature):** M1–M10 all pass; `dotnet test tests/Goose2Client.Tests` green; the design doc's §7 accepted limitations hold (no new unbounded cases found); PR description carries the matrix results.

---

## Invariant-to-test matrix (part-wide)

| Invariant | Proved by |
|-----------|-----------|
| Mode/value resolution pure + total (NaN/corrupt safe) | Task 1 `Resolve_*` (xUnit) |
| No persisted-state corruption from cancelled drags | Task 2 propagation sequence + M6 (in-engine; headless can't synthesize the mouse interleave — stated limitation) |
| Auto recompute is boundary-only (no resize churn) | Task 3 compare + M4 |
| Drag-release commit (no resize under cursor) | Task 4 pinned commit rule + M2 |
| Slider can't produce off-step values | `HSlider.Step = 0.5` + `Commit` snaps through `UiScale.Factor` again (belt and braces) + M2 |
| Login/loading scale WITHOUT a 1× visual change | Task 5 round-trip self-test leg (adversarial: wrong base 16 fails the round-trip) |
| Whole feature regression gate | M10 (Part 1 self-test + new login leg) |

**Explicitly NOT in this part (design §7, accepted):** 3×-overflow fit guarantee, OS-DPI auto-mode accuracy, sub-0.5 steps, world-viewport text scaling (out of scope by design).
