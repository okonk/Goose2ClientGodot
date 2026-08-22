# UI Scale Part 2 — Product Implementation Plan

**Goal:** The user-facing surface: Auto/Manual mode + slider in the Options window, settings persistence, auto-scale on window resize, commit-time drag cancellation, login/loading scaling, and the manual verification matrix. Builds directly on Part 1's `UiScale` / `UiScaleApplier` / `UiScaleLayout` machinery.

**Architecture:** Pure static `UiScale.Resolve(mode, savedValue, windowHeight)` + `UiScale.NormalizeMode(rawInt)` decide the target factor from (mode, persisted slider value, window height) — **pure: they read nothing but their arguments and never touch `CurrentFactor` or `Mode`** (a corrupt persisted mode 99 normalizes to Auto BEFORE it is stored on the applier — review finding: storing the raw cast leaves auto-resize outside the Auto branch). `GameManager`'s pre-login Auto `Apply(AutoFactor(canvas.Y), Startup)` (Part 1, `GameManager._Ready` — NOT a pinned 1×) becomes settings-driven at `LoadSettings` (post-login, pre-HUD), and the existing `window.SizeChanged` handler (`Scripts/GameManager.cs:103,337`) gains the auto-recompute leg. The Options window gains a UI Scale group (two mode `CheckBox`es in ONE `ButtonGroup` with `AllowUnpress = false` — widget-enforced exactly-one-selected — + a 0.5-step slider that commits on the `DragEnded` event or on non-drag `value_changed`), persisting through the existing `CharacterSettings.Options` dictionary. Login/loading scenes self-register (Part 1 pattern); fonts reach them through the PROJECT-WIDE theme (`project.godot:37` `theme/custom="res://Assets/UI/GameTheme.tres"` — no per-scene theme needed), so Task 5 is geometry-only registration plus a test pinning the effective font 10 → 20 → 10 round-trip.

**Tech Stack:** C# / Godot 4.7.1 (GodotSharp), xUnit, headless self-test (Part 1's `tools/tests/run_ui_scale.sh`), in-engine manual matrix.

**Part order:** 1A → 1B → 1C → Part 2 (sequential, same worktree/branch; each part is a self-contained execution context). **Prereq: Part 1C is merged** (do not start before Part 1C Task 5 is green). **Requirements (stable IDs SC-01…SC-16):** see the `Requirements` table in `2026-08-21-ui-scale-design.md` — the canonical requirement→component→phase→test mapping; task headers below tag the IDs they implement.

**Execution:** same worktree/branch as Parts 1A–1C (sequential — Part 2 builds directly on their machinery). Tasks run sequentially as written — each compiles standalone and lands as its own commit. Recommended: @subagent-driven-development (fresh subagent per task, spec-compliance then code-quality review after each). Task 6's matrix M1–M9 and M11 need a display and a game server — run manually or in a headed session; M10 is Part 1C Task 5's headless gate.

---

## APIs verified (citations)

- Part 1's verified surface applies unchanged (`AddThemeFontSizeOverride`, `Theme.SetDefaultFontSize`, `HasThemeFontSizeOverride`, `OS.GetCmdlineUserArgs`, `SetMeta`/`GetMeta`, `Node.TreeExited`).
- `HSlider` (GodotSharp 4.7.1, same DLL): properties `MinValue`, `MaxValue`, `Step`, `Value`; C# events `ValueChanged`, `GuiInput`, **`DragStarted`, `DragEnded`** — reflection-verified on `Godot.HSlider` (the drag events are generated on `HSlider`/`VSlider`, NOT on `Range` — an earlier reflection check of `Range` missed them and wrongly concluded the signal didn't exist; a runtime probe confirms `HSlider.get_signal_list()` includes `drag_started,drag_ended`). Task 4's commit mechanism is the `DragEnded` event; `_Process`/`Input.IsMouseButtonPressed` polling (the `BaseWindow` pattern, `Scripts/UI/BaseWindow.cs:120-140`) is the fallback ONLY if M2 shows release-outside-control doesn't fire `DragEnded`. **Handler signature (reflection-verified):** the `DragEnded` delegate is `Slider+DragEndedEventHandler` = `void (bool valueChanged)` — the C# handler MUST take the bool (informational; Task 4 commits unconditionally). **Mode controls (reflection-verified by enumerating both classes):** `CheckBox.ButtonGroup : ButtonGroup`, and `AllowUnpress : bool` lives on **`ButtonGroup`** — NOT on `BaseButton` in the 4.7.1 binding; `AllowUnpress = false` is what makes the group widget-enforce exactly-one-selected.
- `CharacterSettings.Options` — `Dictionary<string, object>`, `GetOption<T>(key, default)`, indexer-set + `Save()` — `Scripts/CharacterSettings.cs:42-67,144`. Existing usage pattern: `Scripts/UI/OptionsWindow.cs:23-49` (checkbox read in `_Ready`, write in toggled handler, `Save()` on close/toggle — `OptionsWindow.cs:56-60`).
- `Constants.cs:136` — `public static class Options` (string keys: `TargetFiltering`, `ShowSpiritBar`, `SpiritBarShown`, `RenderMode`).
- `OptionsWindow.tscn` — checkboxes are `Content/*Check` nodes; window root is a `BaseWindow` (self-registered by Part 1, so the new group scales automatically).
- Window resize: `GameManager.cs:103` (`window.SizeChanged += OnWindowResized`), handler `Scripts/GameManager.cs:337-345` (guards: `UiLayer == null`, canvas < 2; walks `CollectBaseWindows`).
- Only drag state in the codebase: `BaseWindow._dragging` (`Scripts/UI/BaseWindow.cs:122-140`) — press sets true, release persists + clears, motion accumulates `Position += motion.Relative`. No other mouse-follow drags exist (hotbar swap is click-based, `Scripts/UI/HotbarSwap.cs`).
- Login: `Scenes/Login.tscn` — `MarginContainer` (anchor 0.5, offsets ±150/±100) → `VBox` (`theme_override_constants/separation = 10`) → `NameInput`, `PasswordInput`, `LoginButton`, `StatusLabel`; script `Scripts/LoginScene/LoginScene.cs`. Theme: the scene attaches no theme of its own, but `project.godot:37` sets `theme/custom="res://Assets/UI/GameTheme.tres"` **project-wide**, so its text controls resolve `font_size == 10` (headless-probed: themeless in-tree `Label`/`LineEdit` in this project → 10; the engine default of 16 never applies). Mutating the theme's `default_font_size` therefore reaches these controls — no explicit font entries needed (Task 5).
- Loading: `Scenes/LoadingMap.tscn` — single `StatusLabel` (anchor 0.5, offsets ±150/±10), script `Scripts/LoadingMapScene/LoadingMapScene.cs`; **no theme**.

---

### Task 1: pure factor resolution + settings keys + startup read — SC-02, SC-07

**Files:**
- Modify: `Scripts/UiScale.cs`
- Modify: `Scripts/Constants.cs:136`
- Modify: `Scripts/UiScaleApplier.cs` — add `public UiScaleMode Mode { get; set; }` (default `Auto`; used by this task's `LoadSettings` hook and Task 3's recompute — added HERE so Part 2 compiles task-by-task).
- Modify: `Scripts/GameManager.cs` — the settings read goes in **`LoadSettings`** (`Scripts/GameManager.cs:275`), NOT `GameManager._Ready`: `CharacterSettings` is null in `_Ready` (it is created only in `LoadSettings`, called from `LoginScene.cs:103` on successful login — a settings read in `_Ready` NREs on every launch). Part 1's settings-independent `Apply(AutoFactor(canvas.Y), Startup)` in `_Ready` stays as-is (it scales the login screen for Auto users pre-login).
- Test: `tests/Goose2Client.Tests/UiScaleTests.cs` (extend)

**Step 1: Failing tests.**

Add to `UiScale` (pure static — no scene-tree / global-state APIs; Godot value types permitted): `public enum UiScaleMode { Auto = 0, Manual = 1 }`,
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
Both `Resolve` and `NormalizeMode` are STATIC — call through the `UiScale` type, never `applier.Scale.…` (CS0176). The headless self-test (Part 1C Task 5) does NOT go through login; it runs its own deterministic startup sequence: `LoadSettings("ui-scale-selftest")` (a real `CharacterSettings` — settings load works headless; an NRE on `CharacterSettings` in a window `_Ready` means this step is missing) → force `Apply(1f, Startup)` for the 1× baseline → `EnsureHud()`. The headless root is NOT the project's 1280×720 (headless probes report ~64–100px; Part 1C Task 5 step 0), so the pre-login `AutoFactor(small) == 1` anyway — the forced 1× is a deterministic pin, not a workaround. The "launch with settings pinning Manual 2×" leg has NO automated form (headless cannot log in) — covered by manual check M3; state that there.

**Step 5 (green) + commit:** `feat: settings-driven UI scale factor at settings load`.

| Invariant | Proved by |
|-----------|-----------|
| Auto ignores stale slider values (and vice versa) | `Resolve_AutoIgnoresSavedValue` / `Resolve_ManualIgnoresWindowHeight` |
| Corrupt persisted value can't escape [1,3] | `Resolve_ManualNaN`, `Resolve_Manual...3.4f` |
| Headless/default path unchanged (Part 1 tests stay green) | Part 1C Task 5 command re-run |
| No NRE pre-login; login screen still scales (Auto) before settings exist | `LoadSettings` hook placement (settings-null-proof `_Ready` unchanged) + M8 (login at 1080p Auto) |

---

### Task 2: commit-time drag cancellation (`BaseWindow.CancelDrag`) — SC-08

**Files:**
- Modify: `Scripts/UI/BaseWindow.cs:110-140`
- Modify: `Scripts/UiScaleApplier.cs` (apply pass step 2)

**Mutation impact:**
- Source of truth: `BaseWindow._dragging` + the live `Position` during a move-drag (`Scripts/UI/BaseWindow.cs:122-140`).
- Readers: the drag's own release handler (persists the full saved quad to `CharacterSettings` via `SetWindowSetting`); `RepositionFromSaved` (reads the persisted quad + `Size`, never the live `Position` mid-drag).
- Derived state: the persisted `WindowSettings.Position` — **must not record the mid-drag position**; cancel must prevent the release-persist from firing for a cancelled drag.
- Propagation: (1) on press, store `_preDragPosition = Position`; (2) `CancelDrag()`: if `_dragging` → `_dragging = false; _dragCancelled = true; Position = _preDragPosition;` (3) apply pass: before hiding tooltips, call `CancelDrag()` on every registered `BaseWindow` (cast; non-BaseWindow `IScalableWindow`s skip). **The restore is an INTERMEDIATE step (review finding — the draft's "window ends at its pre-drag position" was wrong):** `Apply`'s placement step then runs `RepositionFromSaved()` on every window, so the FINAL position is `ResolveScaled(unchanged quad, newFactor)` — which may differ from the pre-drag pixel (a hotbar dragged up, then a 1×→2× commit, settles at (520, 638), not its pre-drag (520, 679)); it equals the pre-drag pixel only when the factor didn't change. (4) **persistence suppression is mandatory, not incidental**: the title-bar release handler (`Scripts/UI/BaseWindow.cs:118-142`) calls `SetWindowSetting` **unconditionally** on left-button release — it never checks `_dragging` — so without a guard the user's eventual mouse release (after the scale commit has already restored `Position`) would still fire a persist. Guard BOTH release branches (the `GuiInput` release and the `MouseMotion` escape) with `if (!_dragCancelled)` and clear the flag when consumed. **The flag MUST also be cleared on the next left-press** (the press site, `BaseWindow.cs:118` — `if (_dragCancelled) _dragCancelled = false;` before `_dragging = true`): a cancelled release that never reaches a guarded branch (e.g. the window was freed mid-drag, so no release handler ran) would otherwise leak the flag and silently suppress the user's NEXT legitimate drag save (review finding). (In the pure case the value equals the re-resolved position so the observable outcome would coincidentally match — but the flag makes the invariant airtight and covers the `Visible`/canvas fields persisted in the same `SetWindowSetting` call.) The subsequent `Relayout()` + `RepositionFromSaved()` re-solves from the untouched saved quad.
- Invariants: a scale commit mid-move leaves the saved quad unchanged (the in-flight drag position is NEVER persisted) and the window ends at `ResolveScaled(quad, newFactor)`; a *completed* drag (released before the commit) persists as today.
- Observable proof: Task 5's in-engine check M6.

`public void CancelDrag()` postcondition (on return, BEFORE the apply pass's placement step re-derives from the quad — see the invariants bullet): `_dragging == false`; `Position` equals the value before the drag's first press. Idempotent when not dragging.

**Gate:** xUnit green (no new pure surface); in-engine M6.
**Commit:** `feat: cancel in-flight window move-drag on scale commit`.

---

### Task 3: auto-scale on window resize — SC-13

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
The `return` matters: `Apply`'s placement step already re-laid-out and repositioned every window — running the existing `CollectBaseWindows` walk afterwards would double-reposition (harmless but wasteful, and it obscures the single-owner model). In the else branch the existing walk (now `RepositionFromSaved` per Part 1B Task 3) runs unchanged.
`applier.Mode` = the mode stored at settings load (Task 1, in `LoadSettings`) and updated by the Options window (Task 4) — the `Mode` property is added in Task 1 (set in BOTH places; default `Auto` covers the pre-login window). It is UI-state, not scale-math.

**Why safe:** `OnWindowResized` fires on the root window's `size_changed` — user drag-resize emits many; the `f != applier.Factor` compare makes each a no-op except at the 720/1080/1440 boundaries (design Section 4, no debounce). The `Apply` early-return-on-unchanged-factor (Part 1B Task 1) is the second guard. **The canvas+factor composition is already correct in Part 1's model** (review finding — the old claim that "stored positions are necessarily at the current factor" was false after a commit): every placement (this walk, an auto-crossing `Apply`, a user commit) derives from the per-window saved QUAD (pos/size/factor/canvas, Part 1A Task 2) + the live (Size, factor, canvas), so a threshold crossing that changes BOTH canvas and factor re-anchors in one `ResolveScaled` call — no old-canvas tracking, no live-rect capture. The walk (manual resize, no factor change) is the same call with size/factor unchanged.

**Gate:** xUnit green; in-engine M4 (resize across 1080 → HUD rescales once, no flicker storm; back across → rescales back).
**Commit:** `feat: auto UI scale follows window height`.

---

### Task 4: Options window UI Scale group — SC-03, SC-14

**Files:**
- Modify: `Scenes/UI/OptionsWindow.tscn` — **(a) resize the root**: `offset_bottom = 112.0` → `240.0` (root offsets `0, 0, 240, 112` at `OptionsWindow.tscn:22-25`; `Background` and `Content` are full-rect anchored and follow; existing checkbox rows occupy y 28–108, so the new group gets y ≈ 120–232; the window's saved/default position re-resolves at the new size through `RepositionFromSaved` (at registration and every commit — the quad model, Part 1A Task 2) — no extra work. **Legacy files (review finding):** saved positions predate this resize — the `Size == default` fallback for this window MUST use `DefaultWindowLayout.LegacySize` → 240×112 (Part 1A Task 2), NOT the new tscn size, or legacy y-margins are misread by 128px). **(b) add under `Content`**: a `Label` ("UI Scale"), two `CheckBox`es (`ScaleAutoCheck` pressed by default, `ScaleManualCheck`) — NO `ButtonGroup` resource in the tscn; `_Ready` creates the single group (mode bullets below) — an `HSlider` (`ScaleSlider`: `min_value = 1.0`, `max_value = 3.0`, `step = 0.5`, `value = 1.0`), a `Label` (`ScaleValueLabel`, text `"1×"`). Layout with the existing checkbox rows' pixel style (the generic scaler handles sizing; tscn offsets are the base).
- Modify: `Scripts/UI/OptionsWindow.cs`

**Behavior spec (pinned — the drag-release contract from the design):**
- Mode exclusivity is WIDGET-ENFORCED, not flag-enforced (review finding — two independent checkboxes + a reentrancy flag leave "user unchecks the currently selected mode" undefined): ONE `ButtonGroup` with `AllowUnpress = false` (verified group-level property in the 4.7.1 binding) assigned to both boxes → the user can never unpress the active mode; exactly one is always pressed. `ScaleAutoCheck` → mode Auto; `ScaleManualCheck` → mode Manual (slider row becomes visible; in Auto ONLY THE SLIDER is hidden — `ScaleValueLabel` stays visible showing the effective factor, e.g. `"Auto (2×)"`, so Auto users still see what is in force; review finding: hiding the value label contradicted the "user always sees the in-force factor" rule two bullets down). **`_Ready` order (load-bearing):** create the group → assign to both boxes → sync `ButtonPressed` from the normalized mode → ONLY THEN connect the `Toggled` handlers (the programmatic press fires `Toggled` on the partner box; connecting first would commit a spurious mode switch). **Handler rule:** act ONLY when `Toggled.IsPressed == true` (the newly-pressed box — the auto-unpress on the other fires `IsPressed == false` and is ignored). The `_modeSwitching` reentrancy guard is therefore NOT needed — the only programmatic mutation is the pre-connection initial sync.
- On open — the Options window is built ONCE at HUD time (Part 1) and toggled visible, so the open-state refresh belongs in the `_Ready` body guarded by the existing `_initializing`-style first-run flag (or a one-shot `bool _optionsInitialized`), NOT re-read on every show: read `Options.UiScaleMode` through **`UiScale.NormalizeMode`** (the SAME normalization as `LoadSettings` — a persisted 99 must not render as neither-check state) + `Options.UiScaleValue` (default 1); set `ButtonPressed` (the group's boxes), slider value, effective-factor display — the `ButtonPressed` sync happens BEFORE the `Toggled` handlers are connected (mode-exclusivity bullet above) (`"Auto (2×)"` style text on `ScaleValueLabel` — the user always sees the in-force factor). (No `_Process` override is needed for the commit mechanism — `DragStarted`/`DragEnded` events replace the poll; only if the M2 fallback is adopted does a `_Process` override come in, and it MUST then call `base._Process(delta)` first — `BaseWindow._Process` drives the hover fade.)
- **Initial-open guard (adversarial):** programmatic `ScaleSlider.Value = x` in `_Ready` fires `ValueChanged`, and with `_dragging == false` that would `Commit` on EVERY window open — in first-open Manual mode the mode-switch rule would double-commit on top. Set `_initializing = true` at the top of the refresh and clear it **synchronously at the end of the same `_Ready` block** — NOT on a next-frame `await` (review finding: a next-frame clear races the deferred `ScaleRegister` and the ready-flush ordering; there is no user input interleaved INSIDE `_Ready`, so a synchronous set/clear is race-free by construction); `ValueChanged` and the mode handlers early-return while it is set.
- **Commit rule** (events verified — see APIs verified):
  - `ScaleSlider.DragStarted` → `_dragging = true` (suppresses per-tick commits while the thumb is held).
  - `ScaleSlider.DragEnded` → handler `void OnScaleDragEnded(bool valueChanged)` (the binding's delegate takes the bool — Task 1) → `_dragging = false; CommitManualValue((float)ScaleSlider.Value);` — commit UNCONDITIONALLY; `valueChanged` is informational (a no-change commit just persists the slider value, no factor change).
  - `ValueChanged(v)`: update `ScaleValueLabel` ALWAYS (live feedback). If `!_dragging` (keyboard arrow / programmatic set) → `CommitManualValue(v)`.
  - **Fallback (decide at M2):** if manual check M2 shows a release with the cursor OFF the slider fails to fire `DragEnded` (the engine emits it on control mouse-release; verify, don't assume), add the `BaseWindow`-style `_Process` poll of `Input.IsMouseButtonPressed(MouseButton.Left)` as the release detector (left-press via `GuiInput` sets `_dragging`, matching `Scripts/UI/BaseWindow.cs:120-140`).
- **Two commit methods — split is load-bearing (review finding: a single `Commit(v)` that always persists `UiScaleValue` would, on switching to Auto, overwrite the user's dormant Manual slider choice with the current automatic factor — contradicting the `Resolve_AutoIgnoresSavedValue` model):**
  - `CommitManualValue(v)`: `snapped = UiScale.NormalizeFactor(v)` (pure); **persist `Options[UiScaleValue] = snapped` + `Save()` on every manual commit** (persist the normalized slider value even when it equals the current live factor — a commit that changes nothing to the factor can still change what the slider should show next open); then `if (snapped != applier.Factor) applier.Apply(snapped, ApplyReason.UserCommit);` The ONLY method that writes `UiScaleValue`.
  - `CommitAuto()`: `applier.Mode = Auto; Options[UiScaleMode] = Auto; Save(); applier.Apply(UiScale.AutoFactor(currentCanvasY), ApplyReason.UserCommit);` — **never touches `UiScaleValue`** (the dormant Manual choice survives an Auto excursion and returns intact when the user switches back).
  - **Mode switch to Manual (review finding — the mode itself must be set AND persisted, not just the value):** in order: `applier.Mode = Manual;` → `Options[UiScaleMode] = 1;` → `Save();` → THEN `CommitManualValue(current slider value)`. Without the explicit mode persist, switching Auto→Manual leaves `applier.Mode == Auto` and the next window resize auto-recomputes over the user's manual choice. `CommitManualValue` stays value-only — a slider event must not re-persist the mode on every tick; the checkbox handler owns mode persistence. Mode switch to Auto → `CommitAuto()` (already sets + persists the mode, above). Task 3's auto-resize path likewise never writes `UiScaleValue` — it `Apply`s the automatic factor directly.
  - The slider row is hidden in Auto mode, so no manual commit can fire while Auto is active; the `_initializing` guard covers the programmatic open-time sets.
- The Options window itself live-resizes on its own commit (accepted per design Section 5); the release-only commit means it never resizes under the dragging cursor.
- **Label refresh on auto-resize (review — the displayed factor can go stale):** an auto-mode window resize `Apply`s through `GameManager.OnWindowResized` without touching any Options handler, so `ScaleValueLabel` ("Auto (n×)") would show the pre-resize factor. `public override void Relayout()` (possible only because `BaseWindow.Relayout` is virtual — Part 1B Task 3): `base.Relayout()` (the generic frame pass) then refresh `ScaleValueLabel` from `applier.Mode`/`applier.Factor` in the same format as the open-time refresh. The apply path calls `Relayout` on every registered window on every commit, so the label refresh rides on it — the one text side-effect the geometry-only pass carries (M4 asserts it).

**Gate:** xUnit green; in-engine M1–M3, M7.
**Commit:** `feat: UI scale options (auto/manual slider, drag-release commit)`.

---

### Task 5: login + loading registration — SC-01, SC-16

**Files:**
- Modify: `Scripts/LoginScene/LoginScene.cs` (`_Ready` end: Part 1 self-registration pattern — snapshot + `RegisterWindow`; `Relayout()` = `UiScaleLayout.Apply(_geom, factor)`; no reposition — the scene is full-rect anchored)
- Modify: `Scripts/LoadingMapScene/LoadingMapScene.cs` (same)
- Modify: `Scripts/GameManager.cs` — the self-test sequence (Part 1C Task 5) gains the login leg (0/1/2) and the loading leg (3); the settings-driven `Apply` hook also lands in `LoadSettings` here (Part 2 Task 1's `LoadSettings` edit and this task's self-test extension both touch `GameManager` — same file, different methods)
- **No explicit font entries (review finding F2):** `project.godot:37` sets `theme/custom` project-wide, so Login/Loading text already resolves `font_size == 10` through the same `GameTheme` instance the applier mutates — `SetDefaultFontSize` reaches these controls with zero per-scene work. Do NOT add `ApplyFontSize(c, 16f)` overrides: the effective base is 10 (probed), not 16, and a 16-based override would change 1× login text from 10px to 16px — a visible regression this task's own gate exists to prevent.

The `VBox` `separation = 10` constant and the `MarginContainer` ±150/±100 offsets scale via the snapshot (Part 1B Task 2).

**1× no-op proof (adversarial):** extend Part 1's self-test (same `+selftest=ui_scale` run — no separate mode). At startup the login scene IS the current scene (freed only on successful login), so the sequence gains a login phase around the existing HUD phases: (0) at startup factor 1, assert login baselines: `MarginContainer` size `300×200` (tscn ±150/±100), `NameInput.GetThemeFontSize("font_size") == 10` (pins the PROJECT-THEME base — if this ever reads 16, the project theme stopped applying and this task's premise is broken), VBox separation `== 10`; (1) `Apply(2f)`: login `MarginContainer == 600×400`, `NameInput` font `== 20`, separation `== 20`, plus all Part 1 HUD assertions; (2) `Apply(1f)`: login values round-trip to `300×200` / 10 / 10 (an explicit-override approach with a wrong base would not round-trip — the adversarial leg). **(3) Loading leg (SC-16 — automated, not manual-only):** instantiate `res://Scenes/LoadingMap.tscn` directly and `AddChild` to the root (its `_Ready` self-registers — no map transition or server involved), `ProcessFrame`, assert its 1× baselines (snapshot-relative rects read live at runtime, never hard-coded canvas); `Apply(2f)` → sampled rects doubled; `Apply(1f)` → round-trip; `QueueFree` + await + registration gone. This is the regression gate; M8's real-transition check stays a sanity observation.

**Gate:** self-test exits 0 including the login legs.
**Commit:** `feat: scale login and loading screens`.

---

### Task 6: manual verification matrix (in-engine, headed) — SC-15, SC-16

**Files:** none (checklist task). Run the dev server on at least 720p and 1080p; record results in the PR description. `run.sh` lives in the main workspace (gitignored — absent from this worktree); from the worktree either copy it over or inline its two commands (`godot-mono --headless --path . --build-solutions --quit`, then `godot-mono --path . --gpu-index 1 --display-driver wayland`).

| # | Check | Pass condition |
|---|-------|----------------|
| M1 | 1080p first launch (default Auto) | EVERY scoped window renders at 2× — check each: Vitals (portrait fills the scaled circle after a character load — the headless-untestable Part 1C Task 2 leg), Inventory, Character, Spellbook, Hotbar, Toolbar, Chat, Options, Vendor, Bank, CombineBag, Debug (tscn-offset windows scale their children), Party (all 8 roster tiles scaled — SC-10), BuffEffects, a runtime-spawned Info or Quest window with content (native-scaled lines — SC-11), one item tooltip (hover a hotbar item), login text at 20px (SC-16). No 1× flash at map entry. ("HUD renders at 2×" alone is not reproducible — every row above must be observed) |
| M2 | Options → UI Scale → drag slider 2× → 1.5×, release | HUD (incl. options window) rescales ONCE, on release; label follows thumb live during drag; arrow-key nudge applies immediately. Also: drag a HUD window at 2×, then close and reopen it (and again at 1×) — position/size persist exactly (the visibility-only toggle persistence, Part 1B Task 3: a close must not write a mixed-coordinate quad) |
| M3 | Restart (incl. with settings pinning Manual 2× — the only automated-gap leg, headless can't log in) | Mode/value persist; factor applied before HUD build (no flash) |
| M4 | Auto mode: drag-resize window across 1080px height boundary | Exactly one rescale crossing each way; no per-pixel churn (watch for layout thrash while dragging); with the Options window open, `ScaleValueLabel` tracks the crossing ("Auto (1×)" → "Auto (2×)") — the auto-resize label refresh (Task 4 `Relayout` override) |
| M5 | 720p Manual 3× | HUD scales to 3×; oversized windows keep their title bar reachable — bottom may be clipped when the scaled window + saved margin doesn't fit (the Task 2 clamp model, design §7); 1.5× icons acceptably soft (the Q3 verdict) |
| M6 | Start moving a window (title-bar drag), then commit a scale change before releasing | Window ends at the quad-derived position at the NEW factor (== pre-drag pixel only when the factor is unchanged); the in-flight drag position is never saved (verify by restarting: position matches the quad derivation, not the drag midpoint) |
| M7 | Non-16:9 window (e.g. 1600×900) | World gutters per Stage 1; HUD placement correct at the auto factor (900 → 1, threshold); at 3× an item tooltip's box/padding/icon scale with its font (the Part 1C Task 1 leg) and clamp to the viewport |
| M8 | Login screen at 1080p Auto | Login box 2× (600×400), text 20px (project theme base 10 × 2); loading overlay scales during a REAL map transition (sanity — the scaled overlay is what the player actually sees) — the regression gate for loading is AUTOMATED: Task 5's self-test leg instantiates `LoadingMap.tscn` directly (no server transition needed) and pins its 1×→2×→1× round-trip headless (SC-16) |
| M9 | Chat: scroll mid-log, commit scale change | Chat content + scroll offset preserved; reposition keeps its edge margin (R5 model — Task 2's clamp if the scaled window + margin overruns) |
| M10 | `bash tools/tests/run_ui_scale.sh` (+ login leg) | Exit 0, no `ERR_` output — headless regression gate stays green |
| M11 | Mode switching (SC-14) | (1) Press Manual → Auto unpresses, Manual presses, slider row appears — exactly ONE selected at every step (widget-enforced by the `ButtonGroup` `AllowUnpress = false` — the user cannot unpress the active mode); (2) set 2.5×, release; (3) press Auto → Manual unpresses, slider hidden, label shows `Auto (n×)`; (4) press Manual again → slider restored AT 2.5× (the dormant manual value survived the Auto leg — Auto never writes `UiScaleValue`); (5) press Auto → file ends with mode Auto and `UiScaleValue` 2.5. **Automated coverage of this row: NONE — widget input cannot be synthesized headless; the value-preservation invariant itself is xUnit-pinned (Task 1's commit-split + Task 2's round-trip), M11 is the widget-behavior gate** |

**Done criteria for the part (and feature):** M1–M11 all pass; `dotnet test tests/Goose2Client.Tests` green; the design doc's §7 accepted limitations hold (no new unbounded cases found); PR description carries the matrix results.

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
