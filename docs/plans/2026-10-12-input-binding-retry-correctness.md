# Input Binding Retry Correctness Implementation Plan

**Goal:** Make input-binding persistence safe on Windows and make the asynchronous Apply lifecycle prevent concurrent writes and conflicting editor mutations.

**Architecture:** Close and durably flush the temporary file before entering the retryable publish phase. Keep file retries context-free, atomically coordinate apply and suppression operations, enforce the Godot synchronization context at the production composition root, and treat the UI apply interval as a locked editor transaction whose controls and mutation handlers cannot change or discard the draft. Strengthen unit/contract/self-tests around each invariant.

**Tech Stack:** C# 12, .NET, Godot 4 C#, xUnit

---

## APIs verified

- `InputBindingFileStore.SaveAsync` creates, flushes, and publishes the temp file at `Scripts/InputBindings/InputBindingFileStore.cs:40-68`; `PublishWithRetryAsync` performs backup and `File.Move` at `Scripts/InputBindings/InputBindingFileStore.cs:71-90`.
- `InputBindingService.ApplyAsync` owns the in-flight gate and resumes into runtime `InputMap` replacement at `Scripts/InputBindings/InputBindingService.cs:162-251`.
- `KeyBindingEditorState.ApplyAsync` replaces the draft/baseline after success at `Scripts/InputBindings/KeyBindingEditorState.cs:111-123`.
- `KeyBindingsWindow.ApplyDraftAsync` owns UI apply state at `Scripts/UI/KeyBindingsWindow.cs:261-281`; row mutations are wired at `Scripts/UI/KeyBindingsWindow.cs:434-449` and `Scripts/UI/KeyBindingsWindow.cs:616-703`.
- Godot exposes the caller thread through `OS.GetThreadCallerId`; the current self-test incorrectly re-reads `OS.GetMainThreadId` at `Scripts/KeyBindingsSelfTest.cs:173-184`.

### Task 1: Close the temporary writer before publishing

**Files:**
- Modify: `Scripts/InputBindings/InputBindingFileStore.cs:40-68`
- Test: `tests/Goose2Client.Tests/InputBindingFileStoreTests.cs:74-110`

**Mutation impact:**
- Source of truth changed: the published `input-bindings.json` owned by `InputBindingFileStore`.
- Important readers: `InputBindingFileStore.Read` at `Scripts/InputBindings/InputBindingFileStore.cs:34-38` and service initialization at `Scripts/InputBindings/InputBindingService.cs:100-159`.
- Derived/cached state affected: runtime `InputMap` and `InputBindingService._active`, but only after `SaveAsync` succeeds at `Scripts/InputBindings/InputBindingService.cs:198-244`.
- Required propagation sequence: create temp → write → durable flush → dispose writer → copy backup if needed → retry atomic publish → replace runtime map → update active snapshot → delete backup.
- Invariants to preserve: no open application handle blocks `File.Move`; publish failures preserve old bytes; adapter failures can roll back; temp/backup cleanup semantics remain unchanged.
- Observable proof required: a publish hook can take an exclusive handle to the temp file, normal save tests still round-trip bytes, and existing failure/rollback tests remain green.

**Steps:**
1. Add an adversarial test that opens the temp path exclusively inside `FailBeforePublish`; before the fix it must fail on platforms enforcing share modes, and after the fix it succeeds.
2. Replace the method-scoped `using var` with a nested `using` block ending before `PublishWithRetryAsync`.
3. Run `dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj --no-restore --filter InputBindingFileStoreTests`.

### Task 2: Make service operation and thread-affinity checks real

**Files:**
- Modify: `Scripts/InputBindings/InputBindingService.cs:54-330`
- Modify: `Scripts/GameManager.cs:111-127`
- Modify: `Scripts/KeyBindingsSelfTest.cs:173-184`
- Test: `tests/Goose2Client.Tests/InputBindingServiceTests.cs:288-368`
- Test: `tests/Goose2Client.Tests/InputBindingSuppressionTests.cs:44-97`

**Mutation impact:**
- Source of truth changed: the service's atomic idle/applying/suppressing operation state.
- Important readers: every `ApplyAsync`, `BeginSuppression`, and `ProcessSuppression` caller, principally `KeyBindingEditorState` and `KeyBindingsWindow`.
- Derived/cached state affected: persisted bindings, runtime `InputMap`, `_active`, and suppression lease ownership.
- Required propagation sequence: enter on the configured Godot context → atomically reserve apply or suppression → perform the operation → release the state only after apply completion or suppression restoration; rejected callers touch no file/runtime state.
- Invariants to preserve: apply and suppression cannot overlap; exactly one apply owns persistence/runtime replacement; rejection remains immediate; Godot continuation returns to the engine main thread before runtime/UI mutations.
- Observable proof required: concurrent/reentrant apply and suppression-during-apply are rejected without interleaving, off-context calls are rejected before saving, and the runtime self-test compares `OS.GetThreadCallerId()` with the captured main-thread ID.

**Steps:**
1. Replace the apply-only `bool` with one `Interlocked.CompareExchange` state machine shared by apply and suppression.
2. Inject and enforce the production Godot synchronization context before runtime operations.
3. Retain context-capturing awaits, and correct the self-test assertion to use the current caller thread ID.
4. Run focused service and suppression tests.

### Task 3: Lock the editor transaction while Apply is pending

**Files:**
- Modify: `Scripts/UI/KeyBindingsWindow.cs:223-390,434-449,549-703`
- Test: `tests/Goose2Client.Tests/KeyBindingsWindowContractTests.cs:117-147`

**Mutation impact:**
- Source of truth changed: `_applyInFlight` controls whether the editor draft may be mutated or discarded.
- Important readers: close/cancel, reset-all, row reset, add/rebind/remove, and footer rendering in `KeyBindingsWindow`.
- Derived/cached state affected: editor groups/status and dynamic row controls rebuilt by `Rerender`; no persisted schema changes.
- Required propagation sequence: set `_applyInFlight` → rerender controls disabled → await editor apply → clear `_applyInFlight` in `finally` → rerender if the node remains valid.
- Invariants to preserve: Cancel cannot claim to discard an operation already publishing; draft changes cannot be silently overwritten by an earlier apply; teardown can still restore suppression; unexpected completion cannot leave controls permanently disabled.
- Observable proof required: contract tests verify guards on every mutating/close handler and disabled state on footer/dynamic controls.

**Steps:**
1. Add contract assertions for close/reset/remove guards and for disabled Cancel, Reset All, reset-action, chip/remove/add controls while applying.
2. Rerender when apply starts and from `finally` when it ends.
3. Guard handlers as defense in depth and bind dynamic control `Disabled` state to `_applyInFlight`.
4. Run contract and editor-state tests.

## Invariant-to-test matrix

| Invariant | Proved by |
|---|---|
| Publish starts only after the temp writer is closed | New exclusive-open `InputBindingFileStoreTests` regression |
| Retry failures preserve existing bytes | Existing persistent-sharing and non-transient failure tests |
| Apply and suppression never overlap | In-flight apply and suppression rejection tests plus the shared atomic state |
| Runtime operations reject callers outside the configured Godot context | New off-context `InputBindingServiceTests` regression |
| Godot retry continuation is checked against the actual caller thread | Corrected `KeyBindingsSelfTest` assertion |
| UI draft cannot mutate or cancel while Apply is pending | Expanded `KeyBindingsWindowContractTests` |
| Successful apply still updates file, runtime map, and active snapshot together | Existing `InputBindingServiceTests` success cases |

## Final verification

1. `dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj --no-restore`
2. `dotnet build Goose2ClientGodot.sln --no-restore`
3. `bash tools/tests/run_key_bindings.sh` with writable temporary `HOME`/`XDG_DATA_HOME` if the sandbox blocks Godot user paths.
4. `git diff --check` and review the final diff for unrelated changes.

No persistence schema migration is needed; the JSON format is unchanged. Group the implementation and regression tests into one coherent fix commit if a commit is requested.