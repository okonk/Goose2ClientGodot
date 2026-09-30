# Key Bindings Part 1: Foundation Implementation Plan

**Goal:** Build the global binding catalog, immutable binding model, context-aware conflict engine, strict persistence, and transactional runtime `InputMap` service.

**Architecture:** Pure binding and persistence types own validation, comparison, defaults, and conflict semantics. A narrow Godot adapter is the only code that reads or mutates `InputMap`; `InputBindingService` composes that adapter with a global versioned file store and publishes immutable factory and active snapshots. This is Part 1 of 2 and implements the foundation described in `docs/plans/2026-09-30-key-bindings-design.md`.

**Tech Stack:** C# 12 / .NET 8, Godot 4.7.2 C# APIs, `System.Text.Json`, xUnit.

---

## Prerequisites and verified APIs

- Approved design: `docs/plans/2026-09-30-key-bindings-design.md` at commit `d47bf78`.
- Production uses `Godot.NET.Sdk/4.7.2`; tests compile all `Scripts/**/*.cs` against GodotSharp 4.6.2 (`Goose2ClientGodot.csproj:1-6`, `tests/Goose2Client.Tests/Goose2Client.Tests.csproj:1-17`). Keep pure logic free of native engine calls and compile both projects after every Godot-facing task.
- The 46 remappable actions are defined among `project.godot:52-339`; `Move` at `project.godot:40-50` is unused, and UI/pointer/tracked-device actions occupy `project.godot:297-356`.
- Existing keyboard defaults use `physical_keycode`, including movement (`project.godot:52-74`), hotbar (`project.godot:108-166`), and modifier chords (`project.godot:199-267`).
- GodotSharp exposes `InputMap.HasAction`, `ActionGetDeadzone`, `ActionAddEvent`, `ActionEraseEvents`, and `ActionGetEvents` (`~/.nuget/packages/godotsharp/4.7.2/lib/net8.0/GodotSharp.xml:125808-125867`).
- Mapping events should set only one of `Keycode`, `PhysicalKeycode`, or `Unicode`; `PhysicalKeycode` is intended for game input (`GodotSharp.xml:124100-124136`). Keyboard/mouse modifier properties are `AltPressed`, `ShiftPressed`, `CtrlPressed`, and `MetaPressed` (`GodotSharp.xml:125635-125663`).
- Joypad bindings use `InputEventJoypadButton.ButtonIndex`, `InputEventJoypadMotion.Axis`, and `AxisValue` (`GodotSharp.xml:123918-124027`). A runtime probe against installed Godot 4.7.1 verified that an action event with `Device = -1` matches otherwise-identical joypad events from devices 0, 1, and 3; use `-1` as the all-controller mapping value.
- `ProjectSettings.GlobalizePath` belongs only in engine-running composition code. Existing settings isolate it from plain xUnit for the same reason (`Scripts/CharacterSettings.cs:57-62`).
- `GameManager` is the autoload (`project.godot:15-18`) and its earliest initialization boundary is `_EnterTree` (`Scripts/GameManager.cs:85-97`).

Do not add comments or doc strings except where an approved non-obvious invariant requires one.

### Task 1: Add the immutable binding model and complete action catalog

**Files:**
- Create: `Scripts/InputBindings/InputBinding.cs`
- Create: `Scripts/InputBindings/InputBindingSet.cs`
- Create: `Scripts/InputBindings/InputActionCatalog.cs`
- Test: `tests/Goose2Client.Tests/InputBindingModelTests.cs`
- Test: `tests/Goose2Client.Tests/InputActionCatalogTests.cs`

**Step 1: Write failing model and catalog tests**

Add tests proving:

- The catalog contains exactly the 46 approved actions, in the design's category and display order.
- Names, labels, and category labels are nonempty; action names are unique.
- Every catalog name appears as an action in `project.godot`.
- `Move`, `Navigate`, `Submit`, `Cancel`, pointer actions, and tracked-device actions are absent.
- Keyboard, mouse, joypad-button, and signed-axis records compare structurally.
- Binding lists retain insertion order while duplicate entries collapse for editor-originated data.
- Validation rejects `Key.None`, modifier-only physical keys, modifier-mask bits embedded in the physical key value, `MouseButton.None/Left/Right`, invalid/max joy buttons and axes, and axis directions other than `-1` or `+1`.
- `InputBindingSet` deep-copies its action lists; mutating source arrays after construction cannot mutate the snapshot.

Parse only action declarations in the `[input]` section for the project contract test; do not satisfy it with string occurrences elsewhere in the file.

**Step 2: Run tests to verify red**

Run:

```bash
dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj --filter 'FullyQualifiedName~InputBindingModelTests|FullyQualifiedName~InputActionCatalogTests'
```

Expected: FAIL because the binding and catalog types do not exist.

**Step 3: Implement the minimal immutable model**

Use a small closed record hierarchy or equivalent discriminated value with these exact semantic fields:

- Keyboard: `Godot.Key PhysicalKey`, Ctrl, Shift, Alt, Meta.
- Mouse: `Godot.MouseButton Button`, Ctrl, Shift, Alt, Meta.
- Gamepad button: `Godot.JoyButton Button`.
- Gamepad axis: `Godot.JoyAxis Axis`, integer direction constrained to `-1/+1`.

Keep validation/normalization in one pure component shared later by JSON parsing, editor operations, conflict detection, and the adapter. Separate two entry points:

- Editor normalization: validate and collapse duplicates while preserving first-seen order.
- Persisted-input validation: validate but reject duplicates so malformed files fail closed.

`InputBindingSet` must contain every catalog action when it represents factory, active, or draft state. Provide defensive lookup/copy APIs; do not expose writable lists.

Define catalog metadata for the design's categories and behavior roles. Each action may have more than one `(context, role)` entry:

- Normal movement directions have distinct normal roles.
- In targeting, Move Up/Left and Target Up share `TargetPrevious`.
- In targeting, Move Down/Right and Target Down share `TargetNext`.
- Every hotbar action has its distinct normal slot role and shares `TargetConfirm` with Confirm Target during targeting.
- Target Home and Cancel Targeting have distinct targeting roles.
- Normal-only commands have normal roles.
- Toggle Fullscreen has a distinct role in both normal and targeting contexts.

Do not infer behavior roles from display category names.

**Step 4: Run focused tests and compile both targets**

Run:

```bash
dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj --filter 'FullyQualifiedName~InputBindingModelTests|FullyQualifiedName~InputActionCatalogTests'
dotnet build Goose2ClientGodot.csproj
```

Expected: PASS and successful production build.

**Invariant-to-test matrix:**

| Invariant | Proved by |
|---|---|
| Every remappable action has stable metadata | exact 46-action catalog test |
| UI/system actions cannot enter a binding snapshot | catalog exclusion and unknown-action tests |
| Snapshots cannot alias caller-owned lists | source-list mutation regression test |
| Reserved pointer buttons and bare modifiers never normalize | adversarial invalid-binding theory |
| Binding order is stable and duplicates are deterministic | first-seen normalization test |

**Step 5: Commit**

```bash
git add Scripts/InputBindings tests/Goose2Client.Tests/InputBindingModelTests.cs tests/Goose2Client.Tests/InputActionCatalogTests.cs
git commit -m "feat: define remappable input action catalog"
```

### Task 2: Implement context-aware conflict detection

**Files:**
- Create: `Scripts/InputBindings/InputConflictDetector.cs`
- Test: `tests/Goose2Client.Tests/InputConflictDetectorTests.cs`

**Mutation impact:**
- Source of truth changed: no existing runtime state; conflict truth comes from the role entries in `Scripts/InputBindings/InputActionCatalog.cs`.
- Important readers: Part 2's draft editor and conflict styling will consume deterministic conflict results.
- Derived/cached state affected: no cache in Part 1; conflicts are recomputed from a supplied snapshot.
- Required propagation sequence: normalize complete snapshot → group identical bindings → compare catalog roles in intersecting contexts → return deterministic action/binding conflict records.
- Invariants to preserve: warnings never mutate or reject a snapshot; equivalent roles in a shared context do not warn; distinct roles in any shared context do warn.
- Observable proof required: tests assert exact conflicting action pairs and binding values, not merely a conflict count.

**Step 1: Write failing conflict tests**

Cover at least:

- Move Up + Target Up on the same binding: no warning.
- Move Left + Target Up: no warning because both are previous-target aliases.
- Move Up + Target Down: warning in targeting.
- Start Chat + Confirm Target: no warning because their contexts do not overlap.
- Hotbar Slot 1 + Confirm Target: no warning in targeting, while two hotbar slots on one binding still warn in normal gameplay.
- Toggle Fullscreen + any same-binding normal or targeting command: warning.
- Same physical key with different modifier sets: no warning.
- Opposite directions of the same gamepad axis: no warning.
- Results are stable in catalog order regardless of dictionary insertion order.

**Step 2: Run tests to verify red**

```bash
dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj --filter FullyQualifiedName~InputConflictDetectorTests
```

Expected: FAIL because conflict detection does not exist.

**Step 3: Implement the detector**

A collision exists only when bindings are structurally equal. For each colliding action pair, compare all role entries whose context overlaps. Emit one deterministic conflict record when any overlapping context has different behavior roles. Equal roles are compatible aliases.

Keep the detector pure. It must not inspect `InputMap`, categories, factory defaults, or current targeting state.

**Step 4: Run focused tests**

```bash
dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj --filter FullyQualifiedName~InputConflictDetectorTests
```

Expected: PASS.

**Invariant-to-test matrix:**

| Invariant | Proved by |
|---|---|
| Cross-context reuse is not noisy | Start Chat/Confirm Target regression test |
| Same-behavior targeting aliases are compatible | movement/target and hotbar/confirm tests |
| Actual same-context behavior collisions warn | Move Up/Target Down and duplicate-hotbar tests |
| Global commands overlap both action spaces | two-context fullscreen theory |

**Step 5: Commit**

```bash
git add Scripts/InputBindings/InputConflictDetector.cs tests/Goose2Client.Tests/InputConflictDetectorTests.cs
git commit -m "feat: detect contextual input binding conflicts"
```

### Task 3: Add strict versioned JSON and transactional file persistence

**Files:**
- Create: `Scripts/InputBindings/InputBindingJson.cs`
- Create: `Scripts/InputBindings/InputBindingFileStore.cs`
- Test: `tests/Goose2Client.Tests/InputBindingJsonTests.cs`
- Test: `tests/Goose2Client.Tests/InputBindingFileStoreTests.cs`

**Mutation impact:**
- Source of truth changed: global override document `user://input-bindings.json`; the store receives a globalized filesystem path and does not know character settings.
- Important readers: `InputBindingService` startup and Apply; no existing `CharacterSettings` reader may consume this file (`Scripts/CharacterSettings.cs:43-55`).
- Derived/cached state affected: resolved active bindings derive from overrides plus factory defaults; no other cache exists.
- Required propagation sequence: strict parse → validate action/binding records → resolve missing actions against factory snapshot; on save serialize normalized overrides → write and flush same-directory temp file → atomically replace/move destination → retain rollback information until the service confirms runtime publication.
- Invariants to preserve: missing action means factory default; present empty list means unbound; malformed data applies nothing; failed save preserves prior bytes; runtime publication failure can restore the prior file.
- Observable proof required: tests read final bytes from a real temporary directory after success, forced write failure, and rollback.

**Step 1: Write failing codec tests**

Define this exact version-1 schema; enum-backed values are signed JSON integers validated against supported Godot enum members:

```json
{
  "version": 1,
  "actions": {
    "MoveUp": [
      { "kind": "key", "physicalKey": 87, "ctrl": false, "shift": false, "alt": false, "meta": false },
      { "kind": "mouse", "button": 3, "ctrl": false, "shift": false, "alt": false, "meta": false },
      { "kind": "joyButton", "button": 0 },
      { "kind": "joyAxis", "axis": 1, "direction": -1 }
    ]
  }
}
```

Root property order is `version`, `actions`. Binding property order is exactly the order shown for each discriminator. `physicalKey`, `button`, and `axis` are signed 64-bit integers on the wire because Godot's four enums use `Int64`; `direction` is exactly integer `-1` or `1`. Tests must cover round-trip of all four binding kinds, all modifier flags, axis signs, and an empty action list.

Adversarial theories must reject:

- Missing/duplicate/unknown root properties.
- Unsupported version.
- Duplicate action properties.
- Unknown action names or binding kinds.
- Missing/duplicate/unknown binding properties.
- Wrong JSON types and out-of-range enum values.
- Bare modifiers, reserved mouse buttons, and duplicate bindings.
- Trailing JSON or malformed JSON.

Test override resolution and diffing separately:

- Missing action inherits factory.
- Present empty action remains empty.
- Factory-equal lists are omitted from output.
- List order is significant and preserved.

**Step 2: Run codec tests to verify red**

```bash
dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj --filter FullyQualifiedName~InputBindingJsonTests
```

Expected: FAIL because the codec does not exist.

**Step 3: Implement the strict codec**

Use `Utf8JsonReader`/`JsonDocument` property enumeration or an equivalently strict parser; default `JsonSerializer` DTO binding is insufficient because it silently accepts duplicate and unknown properties. The codec returns parsed override entries or a typed parse failure; the service turns that failure into one safe startup warning and factory defaults. Never partially return valid actions from an invalid document.

Serialization must be deterministic: catalog action order, binding-list order, and the exact fixed property order above. Do not persist actions equal to factory defaults.

**Step 4: Write failing real-filesystem store tests**

Use unique temporary directories and an injectable atomic writer/failure seam. The file store owns bytes and publication only; JSON parsing remains in `InputBindingJson` and orchestration remains in the service. Assert:

- Missing-file read is distinct from an empty/corrupt file.
- Existing-file read returns exact bytes without interpreting them.
- First save creates the destination and leaves no temp file.
- Replacing an existing file publishes complete new bytes and leaves no temp/backup file after confirmation.
- A failure before rename preserves old bytes.
- A service-requested rollback restores old bytes, or deletes the destination when none existed.
- Cleanup failure after successful publication leaves destination bytes intact and is reported separately from publication failure.

**Step 5: Implement staged atomic writes**

The store accepts an absolute path. Stage a uniquely named temp file in the destination directory, write UTF-8 without BOM, flush it to disk, then publish by same-filesystem rename/replace. The published transaction retains enough prior state for `Rollback()` until `Complete()` is called. Both methods are idempotent and clean temp/backup files.

State the persistence strategy in code/tests: this is a new versioned global file, so no migration is needed. The service interprets a missing file as defaults. `Complete()` is cleanup-only after file/runtime/active publication: inability to remove a backup is a warning and orphan cleanup concern, not an Apply rollback, because the destination already contains the committed bytes.

**Step 6: Run persistence tests**

```bash
dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj --filter 'FullyQualifiedName~InputBindingJsonTests|FullyQualifiedName~InputBindingFileStoreTests'
```

Expected: PASS.

**Invariant-to-test matrix:**

| Invariant | Proved by |
|---|---|
| Corrupt files never partially apply | mixed valid/invalid action adversarial test |
| Empty and missing have different meanings | unbound versus inherited-default tests |
| Failed publication preserves previous bytes | forced writer/rename failure tests |
| Runtime rollback normally restores persisted state | existing/missing destination rollback tests |
| Rollback failure is surfaced without lying about state | injected rollback-failure result test in Task 5 |
| Output is reviewable and stable | deterministic byte-for-byte serialization test |

**Step 7: Commit**

```bash
git add Scripts/InputBindings/InputBindingJson.cs Scripts/InputBindings/InputBindingFileStore.cs tests/Goose2Client.Tests/InputBindingJsonTests.cs tests/Goose2Client.Tests/InputBindingFileStoreTests.cs
git commit -m "feat: persist global input binding overrides"
```

### Task 4: Add the transactional Godot InputMap adapter

**Files:**
- Create: `Scripts/InputBindings/IInputMapAdapter.cs`
- Create: `Scripts/InputBindings/InputMapEventDescriptor.cs`
- Create: `Scripts/InputBindings/IInputMapSurface.cs`
- Create: `Scripts/InputBindings/GodotInputMapSurface.cs`
- Create: `Scripts/InputBindings/GodotInputMapAdapter.cs`
- Test: `tests/Goose2Client.Tests/InputMapAdapterTests.cs`
- Test: `tests/Goose2Client.Tests/InputMapContractTests.cs`

**Mutation impact:**
- Source of truth changed: runtime events for the 46 catalog actions in Godot's global `InputMap`; factory deadzones remain owned by `project.godot`.
- Important readers: `GameManager._Input` (`Scripts/GameManager.cs:139-143`), `GameHud._UnhandledInput` (`Scripts/UI/GameHud.cs:118-181`), targeting (`Scripts/SpellTargetManager.cs:54-80,223-234`), held movement/attack (`Scripts/Character/Character.cs:643-720`), and hotbar polling (`Scripts/UI/HotbarWindow.cs:338-360`).
- Derived/cached state affected: Godot's action pressed/just-pressed state derives from the event lists; no client-side cache exists yet.
- Required propagation sequence: validate all catalog actions → capture ordered factory descriptors → convert the complete next snapshot to descriptors before mutation → erase/add each catalog action → on any exception restore every catalog action from previous descriptors → report whether restoration succeeded.
- Invariants to preserve: noncatalog actions and action deadzones never change; event order is preserved; ordinary replacement failure restores the complete previous map; restoration failure is explicitly classified for recovery rather than hidden; all calls run on the Godot game thread.
- Observable proof required: real-adapter/fake-surface tests assert final complete maps after success and recoverable mid-replacement failure, plus explicit recovery status after injected restoration failure; contract tests pin the thin production surface's API usage.

**Step 1: Write failing tests for the production adapter over an injectable surface**

Define `IInputMapAdapter` around domain snapshots rather than leaking static `InputMap` calls into the service. Its contract must support:

- `CaptureFactory(catalog)` returning a complete immutable snapshot.
- `Replace(previous, next)` that either publishes `next`, restores `previous`, or throws a typed recovery-required failure when restoration itself fails.

Put the static Godot calls behind the thinner `IInputMapSurface`. Its boundary exchanges pure `InputMapEventDescriptor` values containing event kind, physical/logical/unicode key fields, modifiers, mouse/joy indices, axis value, and device; it must not expose or require constructing native `InputEvent` resources in xUnit. `GodotInputMapSurface` alone converts descriptors to/from native events and delegates to static `InputMap`. Tests instantiate the real `GodotInputMapAdapter` over an in-memory, fault-injecting descriptor surface. This proves production adapter validation, descriptor conversion, action ordering, complete replacement, noncatalog preservation, and rollback without initializing native Godot.

Add source/contract tests for the thin production surface's native-event conversion:

- Keyboard mappings set `PhysicalKeycode`, modifiers, and leave logical key/unicode unset.
- Mouse mappings set permitted button and modifiers.
- Gamepad mappings set `Device = -1`; axis values are exactly `-1f/+1f`.
- Replacement calls `ActionEraseEvents` and `ActionAddEvent` but never removes/recreates actions or sets deadzones.

**Step 2: Run tests to verify red**

```bash
dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj --filter 'FullyQualifiedName~InputMapAdapterTests|FullyQualifiedName~InputMapContractTests'
```

Expected: FAIL because the adapter does not exist.

**Step 3: Implement factory capture**

For each catalog action, call `InputMap.HasAction` before `ActionGetEvents`. Convert only:

- `InputEventKey` with nonzero `PhysicalKeycode` and zero `Keycode`/`Unicode`.
- `InputEventMouseButton` excluding Left/Right.
- `InputEventJoypadButton`.
- `InputEventJoypadMotion` whose `AxisValue` is exactly `-1f` or `+1f`.

Unsupported factory event classes, noncanonical axis values, logical/unicode keyboard defaults, absent actions, invalid bindings, or duplicates are configuration errors. Include action/event identity in the thrown message. Read deadzones only to verify they remain available; do not copy them into the binding file or change them during replacement.

**Step 4: Implement all-or-rollback replacement**

Construct and validate all next and rollback descriptors before the first `ActionEraseEvents`. Replace only catalog actions, preserving per-action order. If any erase/add throws, restore every catalog action from the already-prepared previous descriptors. Throw a typed adapter failure that distinguishes `RuntimeRestored = true` from restoration failure and retains both underlying errors; the service must never mistake a partial runtime map for an ordinary rolled-back failure.

This adapter is game-thread-only. Do not add locks or background I/O around `InputMap`; `GameManager._EnterTree` and window signal handlers already run on the scene thread.

**Step 5: Run focused tests and production compile**

```bash
dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj --filter 'FullyQualifiedName~InputMapAdapterTests|FullyQualifiedName~InputMapContractTests'
dotnet build Goose2ClientGodot.csproj
```

Expected: PASS and successful build under Godot 4.7.2.

**Invariant-to-test matrix:**

| Invariant | Proved by |
|---|---|
| Factory defaults are complete and physical | missing/logical/unsupported factory-event tests |
| Replacement restores the old map or reports runtime recovery failure explicitly | injected mutation and restoration failure tests |
| Deadzone and noncatalog actions survive | before/after fake-map assertions and source contract |
| Controller mappings are device-agnostic | `Device = -1` conversion contract |

**Step 6: Commit**

```bash
git add Scripts/InputBindings/IInputMapAdapter.cs Scripts/InputBindings/InputMapEventDescriptor.cs Scripts/InputBindings/IInputMapSurface.cs Scripts/InputBindings/GodotInputMapSurface.cs Scripts/InputBindings/GodotInputMapAdapter.cs tests/Goose2Client.Tests/InputMapAdapterTests.cs tests/Goose2Client.Tests/InputMapContractTests.cs
git commit -m "feat: adapt binding snapshots to Godot InputMap"
```

### Task 5: Build InputBindingService and initialize it from GameManager

**Files:**
- Create: `Scripts/InputBindings/InputBindingService.cs`
- Modify: `Scripts/GameManager.cs:85-97`
- Test: `tests/Goose2Client.Tests/InputBindingServiceTests.cs`
- Test: `tests/Goose2Client.Tests/InputBindingCompositionContractTests.cs`

**Mutation impact:**
- Source of truth changed: `InputBindingService.Active` becomes the client-owned canonical resolved snapshot; `FactoryDefaults` is the immutable startup baseline; the override file is the durable delta.
- Important readers: Part 2's editor/capture service and every existing `InputMap` consumer listed in Task 4.
- Derived/cached state affected: runtime `InputMap` events derive from `Active`; persisted overrides derive from `Active` versus `FactoryDefaults`; startup warning derives from the load result.
- Required propagation sequence at startup: construct dependencies → verify/capture factory map → read and strictly parse overrides → resolve against factory or select factory on missing/invalid input → transactionally replace runtime map → publish `Active` and warning. Apply: normalize full draft → stage/publish file → transactionally replace runtime map → publish `Active` → complete file transaction.
- Invariants to preserve: no use before initialization; factory never changes; successful Apply leaves active/file/runtime equal; missing startup file leaves no file while factory is active; invalid startup bytes remain untouched while factory is active and a warning is exposed; ordinary save/runtime failure leaves prior state; unrecoverable runtime/file recovery failure is reported explicitly rather than hidden; only one production service exists.
- Observable proof required: tests assert adapter contents, service snapshot, and real temp-file bytes together after each success/failure and recovery-failure path.

**Step 1: Write failing service tests**

Use the real codec/store with a temporary path and the in-memory adapter. Cover:

- Missing file publishes factory defaults.
- Partial overrides resolve missing actions from factory.
- Intentional empty action remains unbound.
- Invalid file publishes factory, exposes one safe startup warning, and leaves invalid bytes untouched.
- Apply writes persistence before invoking adapter publication.
- Persistence failure never calls adapter replacement.
- Adapter failure after file publication rolls the file back and leaves active/runtime unchanged when both adapter restoration and file rollback succeed.
- Adapter restoration failure returns a distinct recovery-required result even when file rollback succeeds, because runtime may be partial.
- If adapter publication fails and file rollback is also injected to fail, Active remains old while file/runtime recovery status is reported explicitly; the failure is never reported as an ordinary unchanged-state error.
- Combined adapter-restoration and file-rollback failure reports both recovery dimensions without overwriting the original errors.
- Cleanup failure after successful Apply leaves file/runtime/active committed and returns success with a warning rather than attempting to roll back published state.
- Successful Apply updates file, runtime, and active snapshot together.
- Repeated reads/copies cannot mutate factory or active state.
- Initialize cannot run twice and Apply before Initialize fails clearly.

Use an operation log in fakes to prove ordering; do not settle for verifying calls independently.

**Step 2: Run service tests to verify red**

```bash
dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj --filter FullyQualifiedName~InputBindingServiceTests
```

Expected: FAIL because the service does not exist.

**Step 3: Implement the service transaction boundaries**

Expose only immutable snapshots and safe warning text. Keep persistence exceptions out of the UI-facing contract by returning a typed Apply result with success/error while retaining exception details for `GD.PushWarning` at composition boundaries.

Publication order for Apply is load-bearing:

1. Validate and deep-copy the entire draft.
2. Serialize and publish the file transaction.
3. Call adapter `Replace(oldActive, candidate)`.
4. Assign the immutable candidate to `Active`.
5. Complete the file transaction.

On step 3 failure, inspect the adapter's typed `RuntimeRestored` result and call file rollback. Never assign `Active` in that failure path. If runtime restoration or file rollback fails, return a distinct recovery-required result identifying which resources need recovery and log all underlying failures; do not claim unchanged state. After step 4, `Complete()` only removes transaction artifacts. A cleanup failure keeps the successful publication and surfaces a warning because rolling back after `Active` publication would create a worse split state.

**Step 4: Compose in GameManager**

Add a single read-only `InputBindingService` property. In `_EnterTree`, after setting `Instance` and before packet/network setup, globalize `user://input-bindings.json`, construct the production store and Godot adapter, initialize the service, and log any recoverable load warning. Missing catalog actions or invalid factory events are developer configuration errors and should fail startup rather than running with an incomplete map.

Do not add bindings to `CharacterSettings`, call per-character `LoadSettings`, or defer initialization to HUD creation.

Add contract tests proving:

- Exactly one service is owned by `GameManager`.
- Initialization occurs in `_EnterTree` before input can be handled.
- Only `GodotInputMapSurface` contains static mutating `InputMap` calls, and `GodotInputMapAdapter` is its sole production caller.
- `CharacterSettings` has no binding-file field/reference.

**Step 5: Run focused and full tests**

```bash
dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj --filter 'FullyQualifiedName~InputBindingServiceTests|FullyQualifiedName~InputBindingCompositionContractTests'
dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj
dotnet build Goose2ClientGodot.csproj
```

Expected: all tests pass and production compiles.

**Invariant-to-test matrix:**

| Invariant | Proved by |
|---|---|
| Successful Apply publishes active, file, and runtime together | success transaction test |
| Ordinary failure restores prior state; file/runtime recovery failures are explicit | store failure, adapter restoration failure, and combined-failure tests |
| Corrupt startup data never replaces defaults or rewrites its bytes | invalid-file integration test |
| Factory snapshot never drifts after Apply | post-Apply factory immutability regression test |
| Bindings are global and ready before gameplay | GameManager/CharacterSettings composition contracts |

**Step 6: Commit**

```bash
git add Scripts/InputBindings/InputBindingService.cs Scripts/GameManager.cs tests/Goose2Client.Tests/InputBindingServiceTests.cs tests/Goose2Client.Tests/InputBindingCompositionContractTests.cs
git commit -m "feat: initialize global input binding service"
```

### Task 6: Part 1 final verification and design-alignment review

**Files:**
- Modify only files above if review finds a defect.

**Step 1: Run the focused foundation suite**

```bash
dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj --filter 'FullyQualifiedName~InputBinding|FullyQualifiedName~InputActionCatalog|FullyQualifiedName~InputConflict|FullyQualifiedName~InputMap'
```

Expected: PASS.

**Step 2: Run all client tests and production build**

```bash
dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj
dotnet build Goose2ClientGodot.csproj
git diff --check
```

Expected: 0 failures, successful build, and no whitespace errors.

**Step 3: Red-team the foundation against the approved design**

Verify explicitly:

- Exactly 46 actions are cataloged; no action shown later can be a no-op.
- Physical keys and exact modifiers survive factory capture, JSON, and event reconstruction.
- Left/Right Click and bare modifiers cannot enter via any boundary.
- Gamepad device IDs never enter equality or persistence; reconstructed mappings use `-1`.
- Missing versus empty action semantics survive load/save.
- Invalid files do not rewrite themselves.
- Ordinary Apply failures leave prior file bytes, active snapshot, and runtime map unchanged; injected rollback failure returns the explicit recovery-required state instead of asserting an impossible guarantee.
- No background thread calls Godot APIs.
- No implementation behavior promised only in Part 2 has been accidentally added to gameplay consumers.

**Step 4: Commit review fixes if needed**

```bash
git add Scripts/InputBindings Scripts/GameManager.cs tests/Goose2Client.Tests
git commit -m "test: harden input binding foundation"
```

If no fixes are needed, do not create an empty commit.

Part 1 is complete when its tests/build pass and the runtime service is available for Part 2. Native `InputMap` behavior cannot run in the xUnit host; the required real-engine factory/apply/signed-axis/generic-device integration coverage is explicitly implemented by Part 2 Task 6 before the feature is considered complete. Implement Part 1 with `@subagent-driven-development` one task and commit at a time.
