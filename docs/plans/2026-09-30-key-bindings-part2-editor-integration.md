# Key Bindings Part 2: Editor and Integration Implementation Plan

**Goal:** Add the resizable binding editor, safe raw-input capture, Options/HUD wiring, exact-modifier consumer audit, and real-engine verification on top of Part 1.

**Architecture:** A pure staged-editor model owns draft/baseline/search/reset behavior, while `KeyBindingsWindow` renders stable catalog rows and delegates persistence/runtime publication to `InputBindingService`. Capture is a pure state machine paired with a service-owned suppression lease so the active `InputMap` is empty during capture and restored exactly once. This is Part 2 of 2 and requires Part 1 to be complete.

**Tech Stack:** C# 12 / .NET 8, Godot 4.7.2 C# UI and input APIs, `.tscn` scenes, xUnit, headless Godot runtime self-test.

---

## Prerequisites and verified APIs

- Approved design: `docs/plans/2026-09-30-key-bindings-design.md`.
- Required foundation: `docs/plans/2026-09-30-key-bindings-part1-foundation.md`, including `InputActionCatalog`, immutable snapshots, conflicts, global persistence, `GodotInputMapAdapter`, and `GameManager.InputBindingService`.
- `BaseWindow` exposes resizable/default-visibility hooks (`Scripts/UI/BaseWindow.cs:35-40,59-70`), performs control lookup/chrome before subclass construction completes (`Scripts/UI/BaseWindow.cs:73-130`), snapshots UI geometry once through `ScaleRegister` (`Scripts/UI/BaseWindow.cs:281-299`), persists resized geometry (`Scripts/UI/BaseWindow.cs:375-420`), and hides/persists visibility from its default close path (`Scripts/UI/BaseWindow.cs:492-504`).
- `UiScaleLayout.Snapshot` must run after build-time geometry and captures container-managed controls/minimum sizes (`Scripts/UiScaleLayout.cs:20-37,92-139`). `UiScaleApplier.Apply` changes the global theme font and calls each registered window's `Relayout` (`Scripts/UiScaleApplier.cs:132-171`).
- The current Options root is 240×316 and its reset button ends at y=308 (`Scenes/UI/OptionsWindow.tscn:20-28,224-232`). Options wires controls before its explicit `ScaleRegister()` (`Scripts/UI/OptionsWindow.cs:32-114`).
- `GameHud` creates persistent windows before manager nodes and wires references only after every `AddChild` has run `_Ready` (`Scripts/UI/GameHud.cs:78-115`). HUD teardown queues every `UiLayer` child for deletion (`Scripts/GameManager.cs:486-497`).
- Raw capture can consume propagation with `Viewport.SetInputAsHandled` (`~/.nuget/packages/godotsharp/4.7.2/lib/net8.0/GodotSharp.xml:269103-269111`). Godot explicitly notes that handled propagation does not change global input state (`GodotSharp.xml:121171`), so erasing action events during capture remains mandatory.
- Controller neutral/release discovery uses `Input.GetConnectedJoypads()`, `Input.GetJoyAxis(int, JoyAxis)`, and `Input.IsJoyButtonPressed(int, JoyButton)` (`GodotSharp.xml:121387,121485-121521`). Runtime reflection verifies the connected-device return type as `Godot.Collections.Array<int>`.
- Physical display labels use `DisplayServer.KeyboardGetLabelFromPhysical(Key)` and `OS.GetKeycodeString(Key)` (`GodotSharp.xml:22578`, `GodotSharp.xml:159819-159825`).
- Runtime event injection uses `Input.ParseInputEvent(InputEvent)`, which invokes `_Input` (`GodotSharp.xml:121880-121887`).
- Current modifier-sensitive readers are `GameHud` (`Scripts/UI/GameHud.cs:118-181`), fullscreen (`Scripts/GameManager.cs:139-143`), targeting (`Scripts/SpellTargetManager.cs:54-80`), movement/attack (`Scripts/Character/Character.cs:643-720`), and hotbar (`Scripts/UI/HotbarWindow.cs:338-360`).

Do not add comments or doc strings except where an approved non-obvious invariant requires one.

### Task 1: Add the staged editor model and binding display formatter

**Files:**
- Create: `Scripts/InputBindings/KeyBindingEditorState.cs`
- Create: `Scripts/InputBindings/InputBindingDisplay.cs`
- Test: `tests/Goose2Client.Tests/KeyBindingEditorStateTests.cs`
- Test: `tests/Goose2Client.Tests/InputBindingDisplayTests.cs`

**Mutation impact:**
- Source of truth changed: only the editor's private draft/baseline state; Part 1's `InputBindingService.Active` remains canonical until Apply succeeds.
- Important readers: `KeyBindingsWindow` rows, conflict styling, status text, and Apply/Cancel controls.
- Derived/cached state affected: dirty state, visible catalog groups, per-binding conflicts, and status text derive from the draft/search/startup warning.
- Required propagation sequence: open from active/factory snapshots → deep-copy baseline and draft → mutate one draft list → normalize → recompute dirty/conflict/filter projections → render; successful Apply replaces baseline with the service-returned active snapshot; Cancel discards without service calls.
- Invariants to preserve: editor operations never mutate factory/active snapshots or `InputMap`; intentional empty lists survive; conflicts warn but never block Apply.
- Observable proof required: tests inspect the complete draft and projected rows after every operation, not only `IsDirty`.

**Step 1: Write failing staged-editor tests**

Cover:

- `Open` deep-copies active and factory snapshots.
- Add appends; replace preserves position; remove-last leaves a present empty action list.
- Duplicate add/replace collapses deterministically.
- Reset Action copies only that action from factory.
- Reset All copies the entire factory snapshot.
- Apply success establishes the returned active snapshot as the new baseline and clears dirty state.
- Apply failure retains draft and baseline and publishes the safe error.
- Cancel/reopen discards edits and reclones current service active state.
- Search is case-insensitive over action and category labels; groups with zero matches are omitted while catalog order remains stable.
- Context-aware conflicts from Part 1 are projected onto both affected action rows without blocking readiness to Apply.
- Startup load warning remains visible until a successful Apply, which replaces the invalid file.

**Step 2: Write failing display tests**

Define an injectable physical-key label provider so xUnit does not call `DisplayServer`. Tests must prove:

- Physical key storage is untouched while the provider's current logical label is displayed.
- Ctrl/Shift/Alt/Meta appear in a fixed platform-neutral order.
- Empty/unknown logical labels fall back to the physical key name.
- Middle/extra mouse buttons and wheel directions have stable names; Left/Right cannot be formatted because validation rejects them.
- Joypad buttons and positive/negative axis directions have distinct stable text.

**Step 3: Run tests to verify red**

```bash
dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj --filter 'FullyQualifiedName~KeyBindingEditorStateTests|FullyQualifiedName~InputBindingDisplayTests'
```

Expected: FAIL because editor/display types do not exist.

**Step 4: Implement the pure editor and production label provider**

Keep all model operations independent of Godot scene objects. The production label provider alone calls `DisplayServer.KeyboardGetLabelFromPhysical` and `OS.GetKeycodeString`; formatters consume Part 1's normalized values and never rewrite `PhysicalKey`.

Expose projected groups/rows as read-only values suitable for rendering. Do not put `Button`, `Control`, or signal references in editor state.

**Step 5: Run focused tests and build**

```bash
dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj --filter 'FullyQualifiedName~KeyBindingEditorStateTests|FullyQualifiedName~InputBindingDisplayTests'
dotnet build Goose2ClientGodot.csproj
```

Expected: PASS and successful production build.

**Invariant-to-test matrix:**

| Invariant | Proved by |
|---|---|
| Draft edits cannot leak into active/factory snapshots | post-edit deep-copy regression test |
| Empty means intentionally unbound | remove-last/reset/apply tests |
| Search/filtering never reorders catalog | multi-category search theory |
| Physical storage and logical display remain separate | injected-layout-label test |
| Conflict warnings do not block Apply | conflicting-draft readiness test |

**Step 6: Commit**

```bash
git add Scripts/InputBindings/KeyBindingEditorState.cs Scripts/InputBindings/InputBindingDisplay.cs tests/Goose2Client.Tests/KeyBindingEditorStateTests.cs tests/Goose2Client.Tests/InputBindingDisplayTests.cs
git commit -m "feat: add staged key binding editor state"
```

### Task 2: Build the resizable bindings window and Options entry point

**Files:**
- Create: `Scenes/UI/KeyBindingsWindow.tscn`
- Create: `Scripts/UI/KeyBindingsWindow.cs`
- Create: `Scripts/UI/KeyBindingsLayout.cs`
- Modify: `Scenes/UI/OptionsWindow.tscn:20-28,224-232`
- Modify: `Scripts/UI/OptionsWindow.cs:14-30,107-114`
- Test: `tests/Goose2Client.Tests/KeyBindingsSceneTests.cs`
- Test: `tests/Goose2Client.Tests/KeyBindingsWindowContractTests.cs`
- Modify: `tests/Goose2Client.Tests/DefaultWindowLayoutTests.cs:16-30` only in Task 4 when centering is wired

**Mutation impact:**
- Source of truth changed: editor draft through `KeyBindingEditorState`; scene controls are projections only.
- Important readers: the window's row renderer, search handler, buttons, status/error label, and later capture handler.
- Derived/cached state affected: stable action-row controls cache catalog identity; chip controls derive from the current draft and conflicts.
- Required propagation sequence: open → clone active/factory into editor → synchronize search/status → update stable row visibility/text/chips → user edit mutates editor → rerender affected rows/status; Apply delegates once to the service and only accepts the returned active snapshot on success.
- Invariants to preserve: opening/Reset never mutates runtime; Apply stays open; Cancel/close discard since last Apply; dynamic controls remain usable after UI scaling.
- Observable proof required: scene/contract tests pin node structure and lifecycle order; Part 2 runtime test later observes real visibility, scale registration, and dynamic controls.

**Step 1: Write failing scene and window contracts**

Assert the new scene contains:

- Root `KeyBindingsWindow`, title/close chrome, and `WindowName = "KeyBindings"`.
- Search `LineEdit`, scrollable category/action host, status label, Reset All, Apply, Cancel.
- A capture overlay/panel with prompt and explicit Cancel Capture button, initially hidden.
- Anchored/container layout that can resize rather than fixed child coordinates throughout.

Assert Options grows to 344px, keeps Reset UI Layout at its current position, and adds **Key Bindings…** at y=312–336. Pin that `DefaultWindowLayout.LegacySize("Options")` remains 240×112; old placement interpretation must not change when Options grows again.

Add source contracts for `_Ready` ordering: controls and all stable catalog rows are built before `ScaleRegister()`.

**Step 2: Run contracts to verify red**

```bash
dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj --filter 'FullyQualifiedName~KeyBindingsSceneTests|FullyQualifiedName~KeyBindingsWindowContractTests|FullyQualifiedName~DefaultWindowLayoutTests'
```

Expected: FAIL because the scene/window/button do not exist.

**Step 3: Author the window scene and layout constants**

Use a 760×520 design size and 520×320 minimum in `KeyBindingsLayout`. Override `DefaultVisible => false`, `Resizable => true`, and `MinResizeSize` in the window.

Author static chrome, search/footer/status, scroll host, and capture prompt in the scene. Build one stable action-row shell per catalog action in `_Ready` before `ScaleRegister`; retain row references instead of destroying/recreating whole rows. Rebuild only each row's binding-chip children.

Rows use Containers and theme-derived sizing. Any explicit dynamic minimum sizes/separation must use `UiScaleApplier.ScaleSize` from a base value and be reapplied in `Relayout`; dynamic controls created after the one-time `UiScaleLayout` snapshot cannot rely on being present in `_geom` (`Scripts/UiScaleLayout.cs:28-37`).

Each binding uses a replacement button plus a separate remove affordance. Rows show **Unbound**, conflict theme/tooltip, **Add Binding**, and **Reset Action**. Search hides nonmatches and empty category headers without changing catalog order.

**Step 4: Implement open/edit/apply/cancel behavior**

Add a public `Open()` with three paths: when already visible, only call `Activate()` so staged edits are not discarded; when hidden and no suppression is pending, clone service snapshots, refresh the startup warning, show, and activate; when hidden while a prior capture's release gate is still service-owned, record one pending-open request and defer showing/cloning until the restoration-complete event (`Scripts/UI/BaseWindow.cs:196-211`). Never force-cancel a held-input gate merely to reopen. After `base._Ready()`, explicitly set `Visible = false` as Options does (`Scripts/UI/OptionsWindow.cs:32-37`), because `BaseWindow._Ready` can restore saved `Visible = true` from a prior resize. Apply calls the service once; success updates editor baseline and leaves the window open, failure keeps draft visible. Cancel and `OnClosePressed` discard and hide without writing.

`OptionsWindow` exposes one callback/reference assigned later by `GameHud`; its button must not instantiate scenes or reach through `GameManager.Hud` during `_Ready`.

Capture buttons may initially call a placeholder method that opens the prompt; Task 3 replaces it with working capture. Do not implement raw event handling in this task.

**Step 5: Run focused tests and build**

```bash
dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj --filter 'FullyQualifiedName~KeyBindingsSceneTests|FullyQualifiedName~KeyBindingsWindowContractTests|FullyQualifiedName~DefaultWindowLayoutTests'
dotnet build Goose2ClientGodot.csproj
```

Expected: PASS and scene scripts compile.

**Invariant-to-test matrix:**

| Invariant | Proved by |
|---|---|
| Options owns only an entry button, not a second window | source contract counts scene instantiation |
| Window is resizable, explicitly hidden after saved visibility loads, and has complete controls | scene/source tests |
| Dynamic rows exist before scale snapshot | `_Ready` ordering contract |
| Apply/Cancel mutate only through editor/service boundaries | source contract plus editor tests |
| Options legacy placement remains compatible | existing/new legacy-size regression test |

**Step 6: Commit**

```bash
git add Scenes/UI/KeyBindingsWindow.tscn Scripts/UI/KeyBindingsWindow.cs Scripts/UI/KeyBindingsLayout.cs Scenes/UI/OptionsWindow.tscn Scripts/UI/OptionsWindow.cs tests/Goose2Client.Tests/KeyBindingsSceneTests.cs tests/Goose2Client.Tests/KeyBindingsWindowContractTests.cs
git commit -m "feat: add key bindings editor window"
```

### Task 3: Implement capture and service-owned InputMap suppression

**Files:**
- Create: `Scripts/InputBindings/KeyBindingCaptureState.cs`
- Create: `Scripts/InputBindings/IInputReleaseState.cs`
- Create: `Scripts/InputBindings/GodotInputReleaseState.cs`
- Modify: `Scripts/InputBindings/InputBindingService.cs`
- Modify: `Scripts/UI/KeyBindingsWindow.cs`
- Modify: `Scripts/GameManager.cs:99-125`
- Test: `tests/Goose2Client.Tests/KeyBindingCaptureStateTests.cs`
- Test: `tests/Goose2Client.Tests/InputBindingSuppressionTests.cs`

**Mutation impact:**
- Source of truth changed: during capture only, the service owns a suppression lease while `Active` remains the canonical binding snapshot; runtime catalog events are temporarily empty.
- Important readers: every gameplay action reader listed in the prerequisites and the capture window's raw `_Input` path.
- Derived/cached state affected: Godot's pressed/just-pressed action state derives from the temporarily empty map; capture prompt/phase derives from the pure state machine; a service-owned release gate can outlive the window.
- Required propagation sequence: validate no active lease → adapter replace active snapshot with a complete empty snapshot → publish lease → arm after initiating pointer is released/next frame → consume raw event → store candidate in editor → transfer a key/button/axis/next-frame release gate to the service → `GameManager._Process` polls global input state → adapter replace empty snapshot with current active snapshot → clear lease and publish restoration completion. Cancel/hide/teardown request the same safe restoration and may destroy the window while the GameManager-owned service continues polling.
- Invariants to preserve: at most one lease; capture never changes `Active` or the file; restore happens exactly once; accepted input cannot fire immediately after restoration; wheel impulses restore on the following process frame; restore failures remain service-owned and retry; `InputMap` never stays permanently empty after window teardown.
- Observable proof required: tests assert final fake-map contents and restore count after success, repeated cancel, hide, first-restore failure followed by retry, and window teardown while input remains held.

**Step 1: Write failing pure capture-state tests**

Drive methods with primitive/domain values, not native `InputEvent` instances. Cover:

- Initiating pointer does not become a binding; capture arms only after release/next-frame signal.
- Pressed non-echo physical keys capture exact modifiers.
- Key echo is ignored.
- Bare Ctrl/Shift/Alt/Meta updates waiting status and remains armed.
- Escape is accepted.
- Left/Right Click reports reserved and remains armed; middle/extra/wheel capture.
- Joypad button capture discards device identity.
- Axis values below 0.75 are ignored; crossing captures sign.
- An axis already above 0.20 at capture start must return to `abs(value) <= 0.20` before a later 0.75 crossing can capture.
- An unseen `(device, axis)` pair, including a controller connected after capture begins, starts blocked and must first be observed at or below 0.20 before a later 0.75 crossing can capture.
- Accepted keyboard and non-wheel mouse buttons wait for release; accepted joypad buttons retain their transient source device only in the release gate; accepted axes wait for `abs(value) <= 0.20`.
- Wheel directions are impulse events with no matching release, so they request restoration on the next process frame after the press is consumed.
- Cancel before a candidate restores immediately once the initiating click is no longer held.

Use 0.75 as capture threshold and 0.20 as neutral/restoration threshold.

**Step 2: Write failing suppression-lease tests**

Against Part 1's in-memory adapter/service, prove:

- Beginning suppression replaces all catalog lists with empty lists but leaves `Active` unchanged.
- A second lease is rejected.
- Idempotent restoration republishes the exact active snapshot once.
- Apply is rejected while a lease is active; a pending Apply continuation runs only after restoration completes.
- Failure while erasing rolls runtime back and never publishes a lease.
- Failure while restoring leaves the lease service-owned; the next `ProcessSuppression` retries even if the window was freed.
- Teardown with a held key/button/axis transfers the release gate to the service rather than restoring early.
- Wheel restoration waits exactly one service process tick and cannot strand suppression.

**Step 3: Run tests to verify red**

```bash
dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj --filter 'FullyQualifiedName~KeyBindingCaptureStateTests|FullyQualifiedName~InputBindingSuppressionTests'
```

Expected: FAIL because capture/suppression APIs do not exist.

**Step 4: Implement the suppression publication boundary**

Add a service method returning a single-purpose suppression lease; do not expose arbitrary map mutation. Creation order is validate/prepare → adapter `Replace(active, empty)` → publish lease. A lease can request restoration with an immutable release gate: immediate, next process frame, physical key, mouse button, source joypad button, or source joypad axis. `InputBindingService.ProcessSuppression(IInputReleaseState)` checks that gate, calls adapter `Replace(empty, Active)`, then marks restored and clears the service reference. Readers may observe either complete active bindings or complete empty bindings, never a partial map because Part 1's adapter rolls back.

`GodotInputReleaseState` wraps `Input.IsPhysicalKeyPressed`, `Input.IsMouseButtonPressed`, `Input.IsJoyButtonPressed`, and `Input.GetJoyAxis`; the pure interface keeps service tests native-free. Call `ProcessSuppression` at the start of every `GameManager._Process`, before any early return, so restoration continues while networking is paused and after the HUD/window is freed. On restore failure, retain the lease and retry next frame while logging safely; never let the destroyed window own the only retry path.

All lease operations run on the game thread. No lock/background scheduling is needed; reject cross-lifecycle misuse explicitly.

**Step 5: Translate Godot events in KeyBindingsWindow**

Override `_Input` and call `base._Input` when capture is inactive. During capture:

- Translate `InputEventKey`, `InputEventMouseButton`, `InputEventJoypadButton`, and `InputEventJoypadMotion` to pure state transitions.
- Ignore key echoes and use `PhysicalKeycode` plus modifier booleans.
- At capture start, enumerate `Input.GetConnectedJoypads()` and every supported `JoyAxis`; mark sampled-neutral pairs eligible and sampled-held pairs blocked-until-neutral. Treat every later unseen `(device, axis)` pair as blocked until an event at or below 0.20 establishes neutral.
- Use `GetViewport().SetInputAsHandled()` before GUI propagation for capture events.
- Exempt Left Click events inside the visible Cancel Capture button's global rect so its `Pressed` signal can run; Left Click is reserved and therefore cannot accidentally become the candidate.
- Disable the normal footer visually while capture is active. If Apply is invoked programmatically during capture, set a pending-Apply continuation, request safe cancellation/restoration, and execute Apply automatically from the service restoration-complete event; do not require a second user action.
- On accepted candidate, update the draft once, render a release/neutral prompt, and transfer the matching release gate to the service. Wheel candidates use the next-frame gate.
- `OnClosePressed`, visibility-driven external hide, and `_ExitTree` request idempotent safe restoration. Override `_Notification` only with `base._Notification(what)` so `BaseWindow` keeps activation/chrome behavior (`Scripts/UI/BaseWindow.cs:240-249`). `_ExitTree` unsubscribes UI callbacks but does not force early restoration; the GameManager-owned service continues polling after the window is gone.

Do not assume `SetInputAsHandled` suppresses action polling; the Godot API explicitly says it does not (`GodotSharp.xml:121171`).

**Step 6: Run focused tests and build**

```bash
dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj --filter 'FullyQualifiedName~KeyBindingCaptureStateTests|FullyQualifiedName~InputBindingSuppressionTests|FullyQualifiedName~KeyBindingsWindowContractTests'
dotnet build Goose2ClientGodot.csproj
```

Expected: PASS and successful production build.

**Invariant-to-test matrix:**

| Invariant | Proved by |
|---|---|
| Capture cannot trigger mapped gameplay actions | fake map is completely empty while lease exists |
| Held accepted input cannot fire on restore | release/neutral-gated transition tests |
| Escape remains bindable while Left/Right remain reserved | adversarial capture-input tests |
| Teardown cannot strand an empty InputMap or restore a held input early | held-input teardown plus service retry tests |
| Capture never persists or publishes draft | active/file assertions throughout suppression tests |

**Step 7: Commit**

```bash
git add Scripts/InputBindings/KeyBindingCaptureState.cs Scripts/InputBindings/IInputReleaseState.cs Scripts/InputBindings/GodotInputReleaseState.cs Scripts/InputBindings/InputBindingService.cs Scripts/UI/KeyBindingsWindow.cs Scripts/GameManager.cs tests/Goose2Client.Tests/KeyBindingCaptureStateTests.cs tests/Goose2Client.Tests/InputBindingSuppressionTests.cs
git commit -m "feat: capture bindings without triggering gameplay"
```

### Task 4: Wire one persistent window through GameHud and Options

**Files:**
- Modify: `Scripts/UI/GameHud.cs:12-31,78-115`
- Modify: `Scripts/UI/OptionsWindow.cs`
- Modify: `Scripts/UI/DefaultWindowLayout.cs:26-44`
- Test: `tests/Goose2Client.Tests/KeyBindingsIntegrationContractTests.cs`
- Modify: `tests/Goose2Client.Tests/DefaultWindowLayoutTests.cs:16-30`

**Mutation impact:**
- Source of truth changed: `GameHud.KeyBindings` becomes the sole live window instance; Options holds only its open callback.
- Important readers: Options button, reset-layout enumeration through `GameManager.HudWindows`, HUD teardown, and runtime self-test.
- Derived/cached state affected: `BaseWindow` placement/visibility entry named `KeyBindings`; no binding data enters `CharacterSettings`.
- Required propagation sequence: GameHud creates Options and KeyBindings before managers → both `_Ready` methods complete via `AddChild` → assign Options callback to `KeyBindings.Open` → user press opens/raises existing window. Teardown queues the HUD subtree → KeyBindings `_ExitTree` requests/transfers safe restoration and unsubscribes → `GameManager._Process` completes release-gated restoration later.
- Invariants to preserve: exactly one instance; first placement centered; hidden→visible opening reclones active state; opening an already-visible editor only raises it and preserves staged edits; reset layout finds it automatically; teardown transfers any pending suppression restoration to the service.
- Observable proof required: integration contracts assert creation count/order/reference wiring; runtime test later observes actual scene behavior.

**Step 1: Write failing integration contracts**

Mirror the repository's existing persistent-window contract style (`tests/Goose2Client.Tests/LogViewerIntegrationContractTests.cs:33-51`). Assert:

- `GameHud` exposes one typed `KeyBindingsWindow` property.
- It calls `Add<KeyBindingsWindow>` exactly once before Quest/Info/OptionList managers.
- Options-to-window callback wiring occurs after both Add calls.
- Options never loads/instantiates `KeyBindingsWindow` itself.
- `DefaultWindowLayout.IsDialog("KeyBindings")` is true.
- Reset UI Layout needs no special-case list because `HudWindows()` recursively discovers every `BaseWindow` (`Scripts/GameManager.cs:539-558`).
- KeyBindings window explicitly hides after `base._Ready`, so even a saved `Visible = true` written by resize cannot auto-open it on the next login.
- Opening while already visible preserves staged edits and only raises the window.
- Opening while a hidden window's prior release gate is pending defers until service restoration; repeated presses coalesce and do not force early restore.

**Step 2: Run tests to verify red**

```bash
dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj --filter 'FullyQualifiedName~KeyBindingsIntegrationContractTests|FullyQualifiedName~DefaultWindowLayoutTests'
```

Expected: FAIL because HUD ownership/wiring and dialog placement are absent.

**Step 3: Implement ownership and post-ready wiring**

Instantiate KeyBindings next to Options and before manager creation. After all fixed windows exist, set the Options callback/reference to `KeyBindings.Open`. Opening an already visible window only calls `Activate()` and preserves its draft; opening after Cancel/close reclones the current active state before showing.

Add `KeyBindings` to `DefaultWindowLayout.IsDialog`. Do not add a hardcoded default position or change Options legacy size.

**Step 4: Run integration tests and build**

```bash
dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj --filter 'FullyQualifiedName~KeyBindingsIntegrationContractTests|FullyQualifiedName~DefaultWindowLayoutTests'
dotnet build Goose2ClientGodot.csproj
```

Expected: PASS.

**Invariant-to-test matrix:**

| Invariant | Proved by |
|---|---|
| Exactly one persistent editor exists | typed-property/Add count contract |
| No reference is used before `_Ready` | source-order contract |
| Reopen preserves a visible draft and safely waits for pending suppression | open-state integration contract |
| First placement centers and later placement persists | IsDialog test plus runtime placement test |
| Binding data remains global while geometry remains per-character | absence/presence contracts around CharacterSettings/BaseWindow |

**Step 5: Commit**

```bash
git add Scripts/UI/GameHud.cs Scripts/UI/OptionsWindow.cs Scripts/UI/DefaultWindowLayout.cs tests/Goose2Client.Tests/KeyBindingsIntegrationContractTests.cs tests/Goose2Client.Tests/DefaultWindowLayoutTests.cs
git commit -m "feat: open key bindings from options"
```

### Task 5: Complete the exact-modifier input-consumer audit

**Files:**
- Modify: `Scripts/UI/GameHud.cs:118-181`
- Modify: `Scripts/GameManager.cs:139-143`
- Modify: `Scripts/SpellTargetManager.cs:54-80`
- Modify: `Scripts/Character/Character.cs:643-720`
- Verify unchanged: `Scripts/UI/HotbarWindow.cs:338-360`
- Verify unchanged: `Scripts/UI/ChatWindow.cs:338-358`
- Verify unchanged: `Scripts/Helpers.Godot.cs:5-12`
- Test: `tests/Goose2Client.Tests/KeyBindingInputContractTests.cs`

**Mutation impact:**
- Source of truth changed: action-event modifier matching behavior in existing input consumers; no persisted state changes.
- Important readers: window/chat/emote commands, fullscreen, spell targeting, attack, and movement.
- Derived/cached state affected: held movement direction/mask and attack cadence derive from exact action state; target cycling derives from exact raw-event action matches.
- Required propagation sequence: raw/held input → exact action match → existing guard/context logic → unchanged command method. Remove only the obsolete Alt blanket guard; retain targeting and `LineEdit` guards.
- Invariants to preserve: Shift+digit does not fire hotbar; arbitrary Alt chords can reach their assigned command; plain actions do not fire from a differently modified chord; typing cannot trigger catalog actions; fixed UI/direct-state behavior remains fixed.
- Observable proof required: source contracts cover every catalog call site, and existing movement/hotbar/target tests remain green.

**Step 1: Write failing input-audit contracts**

Assert:

- `GameHud` has no blanket `AltPressed` return and every catalog `InputEvent.IsActionPressed` passes `exactMatch: true`.
- `GameManager` fullscreen checks root GUI focus for `LineEdit` and uses `exactMatch: true`.
- Target Up/Down, movement aliases, Confirm, Cancel, and Home use exact matching while retaining `allowEcho: true` only for cycling.
- Every Attack/Move held poll in both `ProcessLocalInput` and `TryChainLocalStep` passes `exactMatch: true`.
- Existing hotbar/target-hotkey exact checks remain present.
- Chat Up/Down/Escape, stack-split Ctrl/Shift, mouse drag/resize state, and coordinate-bearing world clicks remain direct and absent from `InputActionCatalog`.

Make the contract enumerate catalog action string literals in production input readers so a future non-exact call fails visibly.

**Step 2: Run tests to verify red**

```bash
dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj --filter FullyQualifiedName~KeyBindingInputContractTests
```

Expected: FAIL on the Alt guard and non-exact call sites.

**Step 3: Update consumer calls without changing command order**

Remove `GameHud`'s Alt early return. Add `exactMatch: true` at each catalog match while preserving the existing `else if` priority. Add the same root-viewport `LineEdit` focus guard used by local movement (`Scripts/Character/Character.cs:647-650`) before fullscreen handling.

Update targeting and held movement/attack calls. Do not convert fixed chat history, stack-split modifiers, pointer coordinates, or drag state into actions.

**Step 4: Run focused and regression tests**

```bash
dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj --filter 'FullyQualifiedName~KeyBindingInputContractTests|FullyQualifiedName~MovementInput|FullyQualifiedName~HoldCast|FullyQualifiedName~Hotbar'
dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj
dotnet build Goose2ClientGodot.csproj
```

Expected: PASS.

**Invariant-to-test matrix:**

| Invariant | Proved by |
|---|---|
| Arbitrary Alt chords are not globally discarded | no-Alt-guard contract |
| Modifier-distinct bindings remain distinct at runtime | exact-call enumeration contract |
| Text entry suppresses fullscreen and existing gameplay commands | focus-guard contracts plus existing guards |
| Fixed local UI behavior remains outside remapping | direct-input/catalog exclusion contract |

**Step 5: Commit**

```bash
git add Scripts/UI/GameHud.cs Scripts/GameManager.cs Scripts/SpellTargetManager.cs Scripts/Character/Character.cs tests/Goose2Client.Tests/KeyBindingInputContractTests.cs
git commit -m "fix: honor exact modifiers for remappable actions"
```

### Task 6: Add real-engine self-test and final acceptance coverage

**Files:**
- Create: `Scripts/KeyBindingsSelfTest.cs`
- Create: `tools/tests/run_key_bindings.sh`
- Modify: `Scripts/GameManager.cs:85-97,218-223`
- Test: all key-binding tests from Parts 1 and 2

**Mutation impact:**
- Source of truth changed: only the self-test process's temporary binding file and runtime `InputMap`; normal launches continue using `user://input-bindings.json`.
- Important readers: production service/window and all runtime action consumers during the self-test.
- Derived/cached state affected: service active snapshot, live InputMap, draft/window projections, UI scale registration, and saved window geometry inside the disposable self-test character settings.
- Required propagation sequence: detect self-test argument before Part 1 service construction → choose a process-unique `user://input-bindings-selftest-<pid>.json` path → initialize normally against it → set the root canvas to 1280×720 → load a process-unique character settings profile → create HUD/run assertions → request capture restoration and process it to completion → remove temporary binding and character files in `finally` → emit PASS/ERR marker → quit with status.
- Invariants to preserve: a headless run never reads/writes the user's production binding file; failure cleanup restores input and deletes temporary files; normal argument-free startup path is unchanged.
- Observable proof required: shell wrapper checks exit status and PASS marker; self-test asserts final runtime event lists and scene behavior, not only method calls.

**Step 1: Add a failing command-line gate contract**

Extend integration contracts to require `+selftest=key_bindings`, a process-unique temporary path selected before service initialization, and cleanup in `finally`. The normal literal `user://input-bindings.json` must remain the argument-free path.

**Step 2: Implement the self-test path and runner**

Follow the existing async gate shape (`Scripts/LogViewerSelfTest.cs:22-38`) and robust PASS-marker shell wrapper (`tools/tests/run_log_viewer.sh`). The runner must build first, create the same minimal generated asset placeholders as existing scripts, capture output, require `[key_bindings_selftest] PASS`, and propagate nonzero Godot status.

Use `Input.ParseInputEvent` for real input dispatch. Before `EnsureHud`, set `tree.Root.Size = new Vector2I(1280, 720)`, call `LoadSettings` with a process-unique self-test character, and await a process frame; Options reads `CharacterSettings` synchronously in `_Ready` (`Scripts/UI/OptionsWindow.cs:39-50`). Record the generated character settings path and delete it with the temporary binding file in `finally`.

Then test:

- Real factory capture contains all 46 actions and physical-key defaults.
- Applying representative keyboard chord, middle/wheel mouse, `Device = -1` gamepad button, and positive/negative axis events produces the exact `InputMap.ActionGetEvents` values.
- A synthetic joypad event from a concrete nonzero device matches the generic mapping.
- Starting capture empties every catalog event list while `Active` remains unchanged.
- Synthetic captured input cannot match a gameplay action during suppression.
- Release/axis-neutral dispatch restores the exact active map once, and wheel capture restores on the following process frame. Restore-failure retry remains covered by the fault-injecting unit adapter; the real-engine self-test does not add a production failure hook.
- Exactly one hidden `KeyBindingsWindow` exists after `EnsureHud`.
- Options callback opens and activates it; Cancel closes without applying.
- First placement is centered.
- Design and minimum sizes are honored.
- Dynamic rows/chips survive 1×→2×→1× and the window remains within the root canvas after resize.

Do not assert exact OS keyboard-layout labels or physical-device driver behavior headlessly.

**Step 3: Run focused tests, build, and self-test**

```bash
dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj --filter FullyQualifiedName~KeyBinding
dotnet build Goose2ClientGodot.csproj
chmod +x tools/tests/run_key_bindings.sh
tools/tests/run_key_bindings.sh
```

Expected: all tests pass, build succeeds, and output contains `[key_bindings_selftest] PASS`.

**Step 4: Run full verification**

```bash
dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj
git diff --check
git status --short
```

Expected: 0 failures and only intended feature files changed.

**Step 5: Manual acceptance**

Run an in-game smoke test with an actual controller and, where available, a non-QWERTY layout:

- Open from Options; search each category; resize and change UI scale.
- Add, replace, and remove multiple bindings; leave one action unbound.
- Capture modifier chords, middle/extra mouse buttons, wheel, controller buttons, sticks, and triggers.
- Confirm Left/Right Click and bare modifiers remain waiting with clear status.
- Confirm Escape binds and Cancel Capture is clickable.
- Confirm no capture triggers movement, attack, hotbar, windows, chat, targeting, or fullscreen, including the held-input release boundary.
- Confirm compatible aliases do not warn and true same-context/global collisions do.
- Confirm Apply, failed Apply, Cancel, reopen, Reset Action, and Reset All.
- Relaunch and switch characters; bindings remain global while window geometry remains per-character.
- On a non-QWERTY layout, labels follow current logical legends while physical key positions continue to trigger actions.

**Invariant-to-test matrix:**

| Invariant | Proved by |
|---|---|
| Production user data is untouched by self-test | temp-path gate contract and `finally` deletion assertion |
| Domain-to-real-InputMap conversion works in engine | representative event and device-matching runtime assertions |
| Suppression blocks real action matching and restores | synthetic press/release/axis runtime sequence |
| HUD/Options/scale lifecycle works in a real tree | one-window/open/center/resize/scale assertions |
| Hardware/layout behavior is accepted on real systems | manual controller and non-QWERTY checklist |

**Step 6: Commit**

```bash
git add Scripts/KeyBindingsSelfTest.cs Scripts/GameManager.cs tools/tests/run_key_bindings.sh tests/Goose2Client.Tests
git commit -m "test: verify key bindings in Godot runtime"
```

### Task 7: Final design-alignment and release-readiness review

**Files:**
- Modify only files from Parts 1 and 2 if review finds a defect.

**Step 1: Walk every approved design promise**

Confirm:

- All and only 46 gameplay actions appear with friendly grouped labels.
- Multiple bindings, unbound actions, individual reset, Reset All, staged Apply, Cancel, and reopen behavior match the design.
- Physical keyboard storage and logical display are separate.
- Left/Right Click and standalone modifiers cannot be applied.
- Any controller can match saved gamepad bindings.
- Context-role warnings match actual exact-modifier input behavior.
- Invalid files fail closed; ordinary save/runtime failures leave prior state unchanged, and runtime/file recovery failure reports the explicit recovery-required state.
- Capture erases actions before arming and restores exactly once after release/neutral; wheel uses next-frame restoration, and teardown leaves release-gated retry ownership with the service.
- Options creates no duplicate window; first placement and UI scaling follow existing framework rules.
- No direct input read outside the approved fixed list remains for a catalog command.

**Step 2: Run release-level checks**

```bash
dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj
dotnet build Goose2ClientGodot.csproj
tools/tests/run_key_bindings.sh
git diff --check
```

Expected: all green.

**Step 3: Commit review fixes if needed**

```bash
git add Scripts Scenes/UI tests/Goose2Client.Tests tools/tests
git commit -m "fix: complete key binding editor integration"
```

If no fixes are needed, do not create an empty commit.

Part 2 and the feature are complete when automated verification passes and the hardware/layout manual smoke is recorded. Implement it with `@subagent-driven-development` one task and commit at a time.
