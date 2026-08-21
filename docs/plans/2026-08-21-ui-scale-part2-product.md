# UI Scale Part 2 — Product Implementation Plan

**Goal:** The user-facing surface: Auto/Manual mode + slider in the Options window, settings persistence, auto-scale on window resize, commit-time drag cancellation, login/loading scaling, and the manual verification matrix. Builds directly on Part 1's `UiScale` / `UiScaleApplier` / `UiScaleLayout` machinery.

**Architecture:** Pure static `UiScale.Resolve(mode, savedValue, windowHeight)` + `UiScale.NormalizeMode(rawInt)` decide the target factor from (mode, persisted slider value, window height) — **pure: they read nothing but their arguments and never touch `CurrentFactor` or `Mode`** (a corrupt persisted mode 99 normalizes to Auto BEFORE it is stored on the applier — review finding: storing the raw cast leaves auto-resize outside the Auto branch). `GameManager`'s pre-login Auto `Apply(AutoFactor(canvas.Y), Startup)` (Part 1, `GameManager._Ready` — NOT a pinned 1×) becomes settings-driven at `LoadSettings` (post-login, pre-HUD), and the existing `window.SizeChanged` handler (`Scripts/GameManager.cs:103,337`) gains the auto-recompute leg. The Options window gains a UI Scale group (two exclusive mode checks + a 0.5-step slider that commits on the `DragEnded` event or on non-drag `value_changed`), persisting through the existing `CharacterSettings.Options` dictionary. Login/loading scenes self-register (Part 1 pattern); fonts reach them through the PROJECT-WIDE theme (`project.godot:37` `theme/custom="res://Assets/UI/GameTheme.tres"` — no per-scene theme needed), so Task 5 is geometry-only registration plus a test pinning the effective font 10 → 20 → 10 round-trip.

**Tech Stack:** C# / Godot 4.7.1 (GodotSharp), xUnit, headless self-test (Part 1's `tools/tests/run_ui_scale.sh`), in-engine manual matrix.

**Execution:** same worktree/branch as Part 1 (sequential — Part 2 builds directly on Part 1's machinery; do not start it before Part 1 Task 10 is green). Tasks run sequentially as written — each compiles standalone and lands as its own commit. Recommended: @subagent-driven-development (fresh subagent per task, spec-compliance then code-quality review after each). Task 6's matrix M1–M9 need a display and a game server — run manually or in a headed session; M10 is Part 1 Task 10's headless gate.

---

## APIs verified (citations)

- Part 1's verified surface applies unchanged (`AddThemeFontSizeOverride`, `Theme.SetDefaultFontSize`, `HasThemeFontSizeOverride`, `OS.GetCmdlineUserArgs`, `SetMeta`/`GetMeta`, `Node.TreeExited`).
- `HSlider` (GodotSharp 4.7.1, same DLL): properties `MinValue`, `MaxValue`, `Step`, `Value`; C# events `ValueChanged`, `GuiInput`, **`DragStarted`, `DragEnded`** — reflection-verified on `Godot.HSlider` (the drag events are generated on `HSlider`/`VSlider`, NOT on `Range` — an earlier reflection check of `Range` missed them and wrongly concluded the signal didn't exist; a runtime probe confirms `HSlider.get_signal_list()` includes `drag_started,drag_ended`). Task 4's commit mechanism is the `DragEnded` event; `_Process`/`Input.IsMouseButtonPressed` polling (the `BaseWindow` pattern, `Scripts/UI/BaseWindow.cs:120-140`) is the fallback ONLY if M2 shows release-outside-control doesn't fire `DragEnded`.
- `CharacterSettings.Options` — `Dictionary<string, object>`, `GetOption<T>(key, default)`, indexer-set + `Save()` — `Scripts/CharacterSettings.cs:42-67,144`. Existing usage pattern: `Scripts/UI/OptionsWindow.cs:23-49` (checkbox read in `_Ready`, write in toggled handler, `Save()` on close/toggle — `OptionsWindow.cs:56-60`).
- `Constants.cs:136` — `public static class Options` (string keys: `TargetFiltering`, `ShowSpiritBar`, `SpiritBarShown`, `RenderMode`).
- `OptionsWindow.tscn` — checkboxes are `Content/*Check` nodes; window root is a `BaseWindow` (self-registered by Part 1, so the new group scales automatically).
- Window resize: `GameManager.cs:103` (`window.SizeChanged += OnWindowResized`), handler `Scripts/GameManager.cs:337-345` (guards: `UiLayer == null`, canvas < 2; walks `CollectBaseWindows`).
- Only drag state in the codebase: `BaseWindow._dragging` (`Scripts/UI/BaseWindow.cs:122-140`) — press sets true, release persists + clears, motion accumulates `Position += motion.Relative`. No other mouse-follow drags exist (hotbar swap is click-based, `Scripts/UI/HotbarSwap.cs`).
- Login: `Scenes/Login.tscn` — `MarginContainer` (anchor 0.5, offsets ±150/±100) → `VBox` (`theme_override_constants/separation = 10`) → `NameInput`, `PasswordInput`, `LoginButton`, `StatusLabel`; script `Scripts/LoginScene/LoginScene.cs`. Theme: the scene attaches no theme of its own, but `project.godot:37` sets `theme/custom="res://Assets/UI/GameTheme.tres"` **project-wide**, so its text controls resolve `font_size == 10` (headless-probed: themeless in-tree `Label`/`LineEdit` in this project → 10; the engine default of 16 never applies). Mutating the theme's `default_font_size` therefore reaches these controls — no explicit font entries needed (Task 5).
- Loading: `Scenes/LoadingMap.tscn` — single `StatusLabel` (anchor 0.5, offsets ±150/±10), script `Scripts/LoadingMapScene/LoadingMapScene.cs`; **no theme**.

---

### Task 1: pure factor resolution + settings keys + startup read

**Files:**
- Modify: `Scripts/UiScale.cs`
- Modify: `Scripts/Constants.cs:136`
- Modify: `Scripts/UiScaleApplier.cs` — add `public UiScaleMode Mode { get; set; }` (default `Auto`; used by this task's `LoadSettings` hook and Task 3's recompute — added HERE so Part 2 compiles task-by-task).
- Modify: `Scripts/GameManager.cs` — the settings read goes in **`LoadSettings`** (`Scripts/GameManager.cs:275`), NOT `GameManager._Ready`: `CharacterSettings` is null in `_Ready` (it is created only in `LoadSettings`, called from `LoginScene.cs:103` on successful login — a settings read in `_Ready` NREs on every launch). Part 1's settings-independent `Apply(AutoFactor(canvas.Y), Startup)` in `_Ready` stays as-is (it scales the login screen for Auto users pre-login).
- Test: `tests/Goose2Client.Tests/UiScaleTests.cs` (extend)

**Step 1: Failing tests.**

Add to `UiScale` (pure static, Godot-free): `public enum UiScaleMode { Auto = 0, Manual = 1 }`,
`public static UiScaleMode NormalizeMode(int raw)` — `0 → Auto, 1 → Manual, anything else → Auto` (pure; the ONLY place a persisted mode int is interpreted — both `LoadSettings` and the Options window go through it), and
`public static float Resolve(UiScaleMode mode, float savedValue, int windowHeightPx)` —
`Auto → AutoFactor(windowHeightPx)`; `Manual → NormalizeFactor(savedValue)`; **unknown mode enum value → Auto** (defensive second layer — callers already normalize, but `Resolve` must be safe if called raw). Pure: never reads or writes `CurrentFactor`.

- `NormalizeMode_Known`: `NormalizeMode(0) == Auto`, `NormalizeMode(1) == Manual`.
- `NormalizeMode_UnknownFallsBackToAuto`: `NormalizeMode(99) == Auto`, `NormalizeMode(-1) == Auto`, `NormalizeMode(int.MaxValue) == Auto` (review finding: a persisted 99 must become Auto BEFORE `applier.Mode` is set — otherwise Task 3's auto-resize branch never fires for that session).
- `Resolve_AutoIgnoresSavedValue`: `Resolve(Auto, 2.5f, 1080) == 2f`, `Resolve(Auto, 1f, 1440) == 3f`.
- `Resolve_ManualIgnoresWindowHeight`: `Resolve(Manual, 1.5f, 720) == 1.5f`, `Resolve(Manual, 3.4f, 720) == 3f` (corrupt value normalizes through `NormalizeFactor`).
- `Resolve_ManualNaN`: `Resolve(Manual, float.NaN, 1080) == 1f`.
- `Resolve_UnknownModeFallsBackToAuto`: `Resolve((UiScaleMode)99, 2.5f, 1080) == AutoFactor(1080) == 2f` (the guard lives in `Resolve` as a second layer; the PRIMARY guard is `NormalizeMode` at every read site).

**Step 2 (red)** → **Step 3:** implement; add keys to `Constants.cs:136`:
```csharp
public const string UiScaleMode = "UiScaleMode";
public const string UiScaleValue = "UiScaleValue";
```
`UiScaleMode` persists as int (0 = Auto default, 1 = Manual — interpreted only through `NormalizeMode`); `UiScaleValue` is a float snapped by `UiScale.NormalizeFactor` at every commit.
**Step 4:** in `LoadSettings`, right after `CharacterSettings = new CharacterSettings(characterName);` (runs post-login, before the first map transition/HUD build — so the re-`Apply` precedes any HUD window: no unscaled flash; it also re-scales the still-visible login screen if settings pin a Manual factor that differs from the pre-login Auto guess):
```csharp
var applier = UiScaleApplier.Instance;
var mode = UiScale.NormalizeMode(CharacterSettings.GetOption<int>(Options.UiScaleMode, (int)UiScaleMode.Auto));
var saved = CharacterSettings.GetOption<float>(Options.UiScaleValue, 1f);
var canvas = (Vector2I)GetTree().Root.GetVisibleRect().Size;
applier.Mode = mode;
applier.Apply(UiScale.Resolve(mode, saved, canvas.Y), ApplyReason.Startup);
```
`applier.Mode` is set to the NORMALIZED mode and must be set HERE too (not just in the Options window) — Task 3's auto-recompute branches on it.
Both `Resolve` and `NormalizeMode` are STATIC — call through the `UiScale` type, never `applier.Scale.…` (CS0176). The headless self-test (Part 1 Task 10) never calls `LoadSettings` (no login in headless) — `CharacterSettings` stays null there, and Part 1's settings-independent `Apply` at the headless root size (NOT the project's 1280×720 — headless probes report ~64–100px; see Part 1 Task 10 step 0) gives `AutoFactor(small) == 1`, so the self-test's factor-1 baseline is untouched. The "launch with settings pinning Manual 2×" leg has NO automated form in either plan's self-test (headless cannot log in) — it is covered by manual check M3; state that there.

**Step 5 (green) + commit:** `feat: settings-driven UI scale factor at settings load`.

| Invariant | Proved by |
|-----------|-----------|
| Auto ignores stale slider values (and vice versa) | `Resolve_AutoIgnoresSavedValue` / `Resolve_ManualIgnoresWindowHeight` |
| Corrupt persisted value can't escape [1,3] | `Resolve_ManualNaN`, `Resolve_Manual...3.4f` |
| Headless/default path unchanged (Part 1 tests stay green) | Part 1 Task 10 command re-run |
| No NRE pre-login; login screen still scales (Auto) before settings exist | `LoadSettings` hook placement (settings-null-proof `_Ready` unchanged) + M8 (login at 1080p Auto) |

---

### Task 2: commit-time drag cancellation (`BaseWindow.CancelDrag`)

**Files:**
- Modify: `Scripts/UI/BaseWindow.cs:110-140`
- Modify: `Scripts/UiScaleApplier.cs` (apply pass step 2)

**Mutation impact:**
- Source of truth: `BaseWindow._dragging` + the live `Position` during a move-drag (`Scripts/UI/BaseWindow.cs:122-140`).
- Readers: the drag's own release handler (persists the full saved quad to `CharacterSettings` via `SetWindowSetting`); `RepositionFromSaved` (reads the persisted quad + `Size`, never the live `Position` mid-drag).
- Derived state: the persisted `WindowSettings.Position` — **must not record the mid-drag position**; cancel must prevent the release-persist from firing for a cancelled drag.
- Propagation: (1) on press, store `_preDragPosition = Position`; (2) `CancelDrag()`: if `_dragging` → `_dragging = false; _dragCancelled = true; Position = _preDragPosition;` (3) apply pass: before hiding tooltips, call `CancelDrag()` on every registered `BaseWindow` (cast; non-BaseWindow `IScalableWindow`s skip); (4) **persistence suppression is mandatory, not incidental**: the title-bar release handler (`Scripts/UI/BaseWindow.cs:118-142`) calls `SetWindowSetting` **unconditionally** on left-button release — it never checks `_dragging` — so without a guard the user's eventual mouse release (after the scale commit has already restored `Position`) would still fire a persist. Guard BOTH release branches (the `GuiInput` release and the `MouseMotion` escape) with `if (!_dragCancelled)` and clear the flag when consumed. **The flag MUST also be cleared on the next left-press** (the press site, `BaseWindow.cs:118` — `if (_dragCancelled) _dragCancelled = false;` before `_dragging = true`): a cancelled release that never reaches a guarded branch (e.g. the window was freed mid-drag, so no release handler ran) would otherwise leak the flag and silently suppress the user's NEXT legitimate drag save (review finding). (In the pure case the value equals the re-resolved position so the observable outcome would coincidentally match — but the flag makes the invariant airtight and covers the `Visible`/canvas fields persisted in the same `SetWindowSetting` call.) The subsequent `Relayout()` + `RepositionFromSaved()` re-solves from the untouched saved quad.
- Invariants: a scale commit mid-move leaves the saved settings unchanged and the window at its pre-drag position; a *completed* drag (released before the commit) persists as today.
- Observable proof: Task 5's in-engine check M6.

`public void CancelDrag()` postcondition: `_dragging == false` on return; `Position` equals the value before the drag's first press. Idempotent when not dragging.

**Gate:** xUnit green (no new pure surface); in-engine M6.
**Commit:** `feat: cancel in-flight window move-drag on scale commit`.

---

### Task 3: auto-scale on window resize

**Files:**
- Modify: `Scripts/GameManager.cs:337` (`OnWindowResized`)

**Change:** inside the existing guard block (order: recompute FIRST; to avoid double reposition — the auto-factor path's `Apply` already repositions every window in its placement step — structure as: auto-factor changed → `Apply(newFactor, AutoResize)` and return; else → existing walk only):
```csharp
var applier = UiScaleApplier.Instance;
if (applier != null && applier.Mode == UiScaleMode.Auto)
{
    var f = UiScale.AutoFactor(canvas.Y);
    if (f != applier.Factor)
    {
        applier.Apply(f, ApplyReason.AutoResize);
        return;
    }
}
```
The `return` matters: `Apply`'s placement step already re-laid-out and repositioned every window — running the existing `CollectBaseWindows` walk afterwards would double-reposition (harmless but wasteful, and it obscures the single-owner model). In the else branch the existing walk (now `RepositionFromSaved` per Part 1 Task 5) runs unchanged.
`applier.Mode` = the mode stored at settings load (Task 1, in `LoadSettings`) and updated by the Options window (Task 4) — the `Mode` property is added in Task 1 (set in BOTH places; default `Auto` covers the pre-login window). It is UI-state, not scale-math.

**Why safe:** `OnWindowResized` fires on the root window's `size_changed` — user drag-resize emits many; the `f != applier.Factor` compare makes each a no-op except at the 720/1080/1440 boundaries (design Section 4, no debounce). The `Apply` early-return-on-unchanged-factor (Part 1 Task 3) is the second guard. **The canvas+factor composition is already correct in Part 1's model** (review finding — the old claim that "stored positions are necessarily at the current factor" was false after a commit): every placement (this walk, an auto-crossing `Apply`, a user commit) derives from the per-window saved QUAD (pos/size/factor/canvas, Part 1 Task 2) + the live (Size, factor, canvas), so a threshold crossing that changes BOTH canvas and factor re-anchors in one `ResolveScaled` call — no old-canvas tracking, no live-rect capture. The walk (manual resize, no factor change) is the same call with size/factor unchanged.

**Gate:** xUnit green; in-engine M4 (resize across 1080 → HUD rescales once, no flicker storm; back across → rescales back).
**Commit:** `feat: auto UI scale follows window height`.

---

### Task 4: Options window UI Scale group

**Files:**
- Modify: `Scenes/UI/OptionsWindow.tscn` — **(a) resize the root**: `offset_bottom = 112.0` → `240.0` (root offsets `0, 0, 240, 112` at `OptionsWindow.tscn:22-25`; `Background` and `Content` are full-rect anchored and follow; existing checkbox rows occupy y 28–108, so the new group gets y ≈ 120–232; the window's saved/default position re-resolves at the new size through `RepositionFromSaved` (at registration and every commit — the quad model, Part 1 Task 2) — no extra work). **(b) add under `Content`**: a `Label` ("UI Scale"), two `CheckBox`es (`ScaleAutoCheck` checked by default, `ScaleManualCheck`), an `HSlider` (`ScaleSlider`: `min_value = 1.0`, `max_value = 3.0`, `step = 0.5`, `value = 1.0`), a `Label` (`ScaleValueLabel`, text `"1×"`). Layout with the existing checkbox rows' pixel style (the generic scaler handles sizing; tscn offsets are the base).
- Modify: `Scripts/UI/OptionsWindow.cs`

**Behavior spec (pinned — the drag-release contract from the design):**
- Mode is exclusive: checking one unchecks the other. `ScaleAutoCheck` → mode Auto; `ScaleManualCheck` → mode Manual (slider row becomes visible; in Auto ONLY THE SLIDER is hidden — `ScaleValueLabel` stays visible showing the effective factor, e.g. `"Auto (2×)"`, so Auto users still see what is in force; review finding: hiding the value label contradicted the "user always sees the in-force factor" rule two bullets down). **Reentrancy:** the programmatic uncheck of the other box re-fires its `Toggled` handler and would flip the mode straight back — guard with a `bool _modeSwitching` set for the duration of the paired check/uncheck (same suppression idea as the `_initializing` guard below, but for mode switches at runtime).
- On open — the Options window is built ONCE at HUD time (Part 1) and toggled visible, so the open-state refresh belongs in the `_Ready` body guarded by the existing `_initializing`-style first-run flag (or a one-shot `bool _optionsInitialized`), NOT re-read on every show: read `Options.UiScaleMode` through **`UiScale.NormalizeMode`** (the SAME normalization as `LoadSettings` — a persisted 99 must not render as neither-check state) + `Options.UiScaleValue` (default 1); set check states, slider value, effective-factor display (`"Auto (2×)"` style text on `ScaleValueLabel` — the user always sees the in-force factor). (No `_Process` override is needed for the commit mechanism — `DragStarted`/`DragEnded` events replace the poll; only if the M2 fallback is adopted does a `_Process` override come in, and it MUST then call `base._Process(delta)` first — `BaseWindow._Process` drives the hover fade.)
- **Initial-open guard (adversarial):** programmatic `ScaleSlider.Value = x` in `_Ready` fires `ValueChanged`, and with `_dragging == false` that would `Commit` on EVERY window open — in first-open Manual mode the mode-switch rule would double-commit on top. Set `_initializing = true` at the top of the refresh and clear it **synchronously at the end of the same `_Ready` block** — NOT on a next-frame `await` (review finding: a next-frame clear races the deferred `ScaleRegister` and the ready-flush ordering; there is no user input interleaved INSIDE `_Ready`, so a synchronous set/clear is race-free by construction); `ValueChanged` and the mode handlers early-return while it is set.
- **Commit rule** (events verified — see APIs verified):
  - `ScaleSlider.DragStarted` → `_dragging = true` (suppresses per-tick commits while the thumb is held).
  - `ScaleSlider.DragEnded` → `_dragging = false; CommitManualValue((float)ScaleSlider.Value);`
  - `ValueChanged(v)`: update `ScaleValueLabel` ALWAYS (live feedback). If `!_dragging` (keyboard arrow / programmatic set) → `CommitManualValue(v)`.
  - **Fallback (decide at M2):** if manual check M2 shows a release with the cursor OFF the slider fails to fire `DragEnded` (the engine emits it on control mouse-release; verify, don't assume), add the `BaseWindow`-style `_Process` poll of `Input.IsMouseButtonPressed(MouseButton.Left)` as the release detector (left-press via `GuiInput` sets `_dragging`, matching `Scripts/UI/BaseWindow.cs:120-140`).
- **Two commit methods — split is load-bearing (review finding: a single `Commit(v)` that always persists `UiScaleValue` would, on switching to Auto, overwrite the user's dormant Manual slider choice with the current automatic factor — contradicting the `Resolve_AutoIgnoresSavedValue` model):**
  - `CommitManualValue(v)`: `snapped = UiScale.NormalizeFactor(v)` (pure); **persist `Options[UiScaleValue] = snapped` + `Save()` on every manual commit** (persist the normalized slider value even when it equals the current live factor — a commit that changes nothing to the factor can still change what the slider should show next open); then `if (snapped != applier.Factor) applier.Apply(snapped, ApplyReason.UserCommit);` The ONLY method that writes `UiScaleValue`.
  - `CommitAuto()`: `applier.Mode = Auto; Options[UiScaleMode] = Auto; Save(); applier.Apply(UiScale.AutoFactor(currentCanvasY), ApplyReason.UserCommit);` — **never touches `UiScaleValue`** (the dormant Manual choice survives an Auto excursion and returns intact when the user switches back).
  - Mode switch to Manual → `CommitManualValue(current slider value)`; mode switch to Auto → `CommitAuto()`. Both also `Save()` `Options[UiScaleMode]` on window close/toggle (existing pattern, `OptionsWindow.cs:56-60`). Task 3's auto-resize path likewise never writes `UiScaleValue` — it `Apply`s the automatic factor directly.
  - The slider row is hidden in Auto mode, so no manual commit can fire while Auto is active; the `_initializing` guard covers the programmatic open-time sets.
- The Options window itself live-resizes on its own commit (accepted per design Section 5); the release-only commit means it never resizes under the dragging cursor.

**Gate:** xUnit green; in-engine M1–M3, M7.
**Commit:** `feat: UI scale options (auto/manual slider, drag-release commit)`.

---

### Task 5: login + loading registration

**Files:**
- Modify: `Scripts/LoginScene/LoginScene.cs` (`_Ready` end: Part 1 self-registration pattern — snapshot + `RegisterWindow`; `Relayout()` = `UiScaleLayout.Apply(_geom, factor)`; no reposition — the scene is full-rect anchored)
- Modify: `Scripts/LoadingMapScene/LoadingMapScene.cs` (same)
- **No explicit font entries (review finding F2):** `project.godot:37` sets `theme/custom` project-wide, so Login/Loading text already resolves `font_size == 10` through the same `GameTheme` instance the applier mutates — `SetDefaultFontSize` reaches these controls with zero per-scene work. Do NOT add `ApplyFontSize(c, 16f)` overrides: the effective base is 10 (probed), not 16, and a 16-based override would change 1× login text from 10px to 16px — a visible regression this task's own gate exists to prevent.

The `VBox` `separation = 10` constant and the `MarginContainer` ±150/±100 offsets scale via the snapshot (Part 1 Task 4).

**1× no-op proof (adversarial):** extend Part 1's self-test (same `+selftest=ui_scale` run — no separate mode). At startup the login scene IS the current scene (freed only on successful login), so the sequence gains a login phase around the existing HUD phases: (0) at startup factor 1, assert login baselines: `MarginContainer` size `300×200` (tscn ±150/±100), `NameInput.GetThemeFontSize("font_size") == 10` (pins the PROJECT-THEME base — if this ever reads 16, the project theme stopped applying and this task's premise is broken), VBox separation `== 10`; (1) `Apply(2f)`: login `MarginContainer == 600×400`, `NameInput` font `== 20`, separation `== 20`, plus all Part 1 HUD assertions; (2) `Apply(1f)`: login values round-trip to `300×200` / 10 / 10 (an explicit-override approach with a wrong base would not round-trip — the adversarial leg).

**Gate:** self-test exits 0 including the login legs.
**Commit:** `feat: scale login and loading screens`.

---

### Task 6: manual verification matrix (in-engine, headed)

**Files:** none (checklist task). Run the dev server on at least 720p and 1080p; record results in the PR description. `run.sh` lives in the main workspace (gitignored — absent from this worktree); from the worktree either copy it over or inline its two commands (`godot-mono --headless --path . --build-solutions --quit`, then `godot-mono --path . --gpu-index 1 --display-driver wayland`).

| # | Check | Pass condition |
|---|-------|----------------|
| M1 | 1080p first launch (default Auto) | Login + HUD render at 2× (fonts ~20px, windows 2× tscn sizes); vitals portrait fills the scaled circle after a character load (the headless-untestable Part 1 Task 8 leg); no 1× flash at map entry |
| M2 | Options → UI Scale → drag slider 2× → 1.5×, release | HUD (incl. options window) rescales ONCE, on release; label follows thumb live during drag; arrow-key nudge applies immediately |
| M3 | Restart (incl. with settings pinning Manual 2× — the only automated-gap leg, headless can't log in) | Mode/value persist; factor applied before HUD build (no flash) |
| M4 | Auto mode: drag-resize window across 1080px height boundary | Exactly one rescale crossing each way; no per-pixel churn (watch for layout thrash while dragging) |
| M5 | 720p Manual 3× | HUD scales to 3×; oversized windows keep their title bar reachable — bottom may be clipped when the scaled window + saved margin doesn't fit (the Task 2 clamp model, design §7); 1.5× icons acceptably soft (the Q3 verdict) |
| M6 | Start moving a window (title-bar drag), then commit a scale change before releasing | Window snaps back to pre-drag position; saved settings unchanged (verify by restarting and comparing position) |
| M7 | Non-16:9 window (e.g. 1600×900) | World gutters per Stage 1; HUD placement correct at the auto factor (900 → 1, threshold); at 3× an item tooltip's box/padding/icon scale with its font (the Part 1 Task 7 leg) and clamp to the viewport |
| M8 | Login screen at 1080p Auto | Login box 2× (600×400), text 20px (project theme base 10 × 2); loading overlay scales during a map transition |
| M9 | Chat: scroll mid-log, commit scale change | Chat content + scroll offset preserved; reposition keeps its edge margin (R5 model — Task 2's clamp if the scaled window + margin overruns) |
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
| Slider can't produce off-step values | `HSlider.Step = 0.5` + `CommitManualValue` snaps through `UiScale.NormalizeFactor` again (belt and braces) + M2 |
| Login/loading scale WITHOUT a 1× visual change | Task 5 round-trip self-test leg (adversarial: any wrong font base — e.g. the old 16 assumption — fails the 10→20→10 round-trip) |
| Whole feature regression gate | M10 (Part 1 self-test + new login leg) |

**Explicitly NOT in this part (design §7, accepted):** 3×-overflow fit guarantee, OS-DPI auto-mode accuracy, sub-0.5 steps, world-viewport text scaling (out of scope by design), item/spell DnD cancel on commit (no Godot API; window move-drag IS cancelled).
