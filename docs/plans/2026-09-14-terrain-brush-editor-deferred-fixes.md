# Terrain Brush Editor — Deferred Fixes and Non-Blocking Cleanup

**Goal:** Resolve the one deferred design item (long-stroke terrain resolution performance) and fix the carried non-blocking notes from the Part 1–4 reviews, without changing observable editing behavior or the map format.

**Architecture:** Add an incremental resolution path (`TryResolveStaged`) to the terrain patch resolver so a stroke validates and resolves only newly staged cells instead of re-validating the full cumulative override set per segment; keep the stateless `TryResolvePatch` intact as the reference/external path. The remaining tasks are small, independent fixes: a fail-fast guard in the test-only `TryOpen`, hermetic thread-affinity tests, active-stroke cancellation on canvas dispose, and test/doc hygiene.

**Tech Stack:** C# / .NET 8 (Core) and .NET 10 (App), Avalonia 11, xunit.

---

## Background

`docs/plans/2026-09-13-terrain-brush-editor-design.md` "Deferred work" (line 260) records one deferred **fix** (the rest of that section is future product scope — out of scope here):

> **Long-stroke resolution performance:** Part 2's stateless `TryResolvePatch` re-validates and re-sorts the full cumulative center-override set on every segment (O(K log K), K = cumulative visited cells; O(N² log N) per stroke, plus per-segment clone allocations). A fix needs a stateful/incremental resolution fast path … or stroke-side persistent overrides with rollback — both require relaxing Part 2's locked resolver contract.

Part 2's "locked contract" was a planning-time scope constraint. The feature is now fully integrated (Parts 1–4 landed), so the contract is deliberately evolved: a new incremental method is **added**; the stateless method is unchanged.

Carried non-blocking notes from the Part 1–4 reviews that this plan fixes:

1. `TryOpen` compatibility wrapper syncs the gate via `GetAwaiter().GetResult()` (self-deadlock on reentrant call).
2. Thread-affinity tests rely on thread-pool thread identity (`PrepareSave_FromOtherThread_Throws` flaked once).
3. `MapCanvas.Dispose` does not cancel an active gesture (unreachable today; latent).
4. Vacuous tool assertion in `UnmodifiedT_WithFocusInBrushField_TypesInsteadOfSelectingTerrain`.
5. Unused `xmlns:rendering` in `MainWindow.axaml`.
6. Shared fixed temp settings path across `MapCanvasTests` harnesses.
7. `FrameX` helper in the e2e tests duplicates the manifest layout.

Explicitly **accepted as-is** (documented, not defects — recorded in the design doc in Task 5):

- `PublicationNotificationErrors` fixed-capacity `TryAdd` silently drops overflow (spec-consistent).
- No disposed-check in publication `Commit` (contract requires it at prepare time only).
- First document's `Terrain` is null until first root open (mirrors pre-existing `SheetIds` behavior).
- Terrain editor keeps its pre-switch draft catalog after a root swap (plan-specified rebind behavior).

## APIs verified

| API | Location |
|---|---|
| `ITerrainPatchResolver` (`GetLogicalCenter`, `TryResolvePatch`) | `src/MapEditor.Core/Terrain/TerrainMapResolver.cs:7-18` |
| `TerrainMapResolver.TryResolvePatch` (internal impl; validation loop `:70-83`, shared-tail region `:85-171`) | `src/MapEditor.Core/Terrain/TerrainMapResolver.cs:54-172` |
| `TerrainCatalogIndex.GetCandidates` (dict lookup, O(1)) | `src/MapEditor.Core/Terrain/TerrainCatalogIndex.cs:62-63` |
| `TerrainResolvedPatch` (`Layer`, `Changes`, `Count`) | `src/MapEditor.Core/Terrain/TerrainResolvedPatch.cs:5-13` |
| `TerrainMapEditStroke.ApplySegment` (clone + resolve + commit) | `src/MapEditor.Core/Editing/TerrainMapEditStroke.cs:127-175` |
| `TerrainMapEditStroke` fields (`_centerOverrides`, `_visited`, `_accumulator`) | `src/MapEditor.Core/Editing/TerrainMapEditStroke.cs:10-16` |
| `MapEditSession.BeginTerrainStroke` | `src/MapEditor.Core/Editing/MapEditSession.cs:146-149` |
| `AssetContextController.TryOpen` (public wrapper + internal overload) | `src/MapEditor.App/Rendering/AssetContextController.cs:271-288` |
| `TerrainOperationGate.IsBusy`, `AcquireAsync` | `src/MapEditor.App/Terrain/TerrainOperationGate.cs:34-40, 55-82` |
| `InternalsVisibleTo("MapEditor.App.Tests")` | `src/MapEditor.App/Properties/AssemblyInfo.cs:3` |
| `MapCanvas.FinishInteraction(commit)` (Escape cancel path; also resets pan/rect/marker drags, releases pointer capture, cancels paste mode, refreshes VM) | `src/MapEditor.App/Controls/MapCanvas.cs:76-127` |
| `MapCanvas.Dispose` | `src/MapEditor.App/Controls/MapCanvas.cs:50-70` |
| Shared gate wiring (editor + asset controller share one gate; editor holds leases across awaits) | `src/MapEditor.App/App.axaml.cs:63`, `src/MapEditor.App/Terrain/TerrainEditorController.cs:73,175` |
| `AppSettingsStore` creates the settings parent dir only on save | `src/MapEditor.App/Settings/AppSettingsStore.cs:77-85` |
| Harness dispose idiom to mirror (conditional close, `RunJobs`, dispose assets, recursive dir delete) | `tests/MapEditor.App.Tests/MainWindowTests.cs:70-82` |
| `MapDocumentViewModel.SelectTerrain` (explicit select also sets `ActiveTool = Terrain`) | `src/MapEditor.App/ViewModels/MapDocumentViewModel.cs:221-233` |
| T-shortcut TextBox guard (`if (e.Source is TextBox) return;`) | `src/MapEditor.App/Views/MainWindow.axaml.cs:541-544` |
| Flaky test + 2 siblings | `tests/MapEditor.App.Tests/AssetContextControllerTests.cs:909-940` |
| `PrepareSave` test helper (pure, file-read) | `tests/MapEditor.App.Tests/AssetContextControllerTests.cs:903-904` |
| `FailingAfterFirstResolver` fakes (implement the interface) | `tests/MapEditor.Core.Tests/Terrain/TerrainMapEditStrokeTests.cs:102-137`, `tests/MapEditor.Core.Tests/Terrain/TerrainMapEditSessionTests.cs:18-52` |
| `TerrainCatalogFixture` (Grass/Water, `Solid`, `Graphic`, `Valid`) | `tests/MapEditor.Core.Tests/Terrain/TerrainCatalogFixture.cs:8-67` |
| Vacuous assertion | `tests/MapEditor.App.Tests/ShortcutTests.cs:636-652` |
| Unused xmlns | `src/MapEditor.App/Views/MainWindow.axaml:4` |
| Shared temp path + `Harness` record (58 `CreateSmallMapAsync()` call sites) | `tests/MapEditor.App.Tests/MapCanvasTests.cs:50, 1404` |
| `FrameX` helper + `TerrainManifestJson` (manifest sheet "1" contains every `FrameX` arm plus graphic 23) | `tests/MapEditor.App.Tests/TerrainEditorEndToEndTests.cs:94-110`, `tests/MapEditor.App.Tests/Fixtures/AssetFixture.cs:60-68` |
| `TerrainCatalogFixture.Solid(center)` sets all eight peers to the center (not center-only) | `tests/MapEditor.Core.Tests/Terrain/TerrainCatalogFixture.cs:50-62` |
| Single-candidate scoring always selects that candidate | `src/MapEditor.Core/Terrain/TerrainPatternScorer.cs:94-128` |

## Invariants (whole plan)

- Terrain editing behavior is byte-identical: same patches, same failures, same history for every existing scenario (existing Core/App tests pass unmodified except the two fakes gaining the new interface method).
- One gesture = one history command; cancel/restore semantics unchanged.
- Map format and `tools/AssetConverter` untouched.
- No new comments/doc strings (AGENTS.md); exception: none expected.

---

### Task 1: Incremental terrain resolution fast path

**Files:**
- Modify: `src/MapEditor.Core/Terrain/TerrainMapResolver.cs:7-18` (interface), `:54-172` (impl)
- Modify: `src/MapEditor.Core/Editing/TerrainMapEditStroke.cs:127-175` (`ApplySegment`)
- Modify: `tests/MapEditor.Core.Tests/Terrain/TerrainMapEditStrokeTests.cs:102-137` and `tests/MapEditor.Core.Tests/Terrain/TerrainMapEditSessionTests.cs:18-52` (fakes)
- Test: `tests/MapEditor.Core.Tests/Terrain/TerrainMapResolverTests.cs` (differential + staged-validation tests)
- Test: `tests/MapEditor.Core.Tests/Terrain/TerrainMapEditStrokeTests.cs` (long-stroke regression guard)

**Mutation impact:**
- Source of truth changed: `MapDocument` tile layers, mutated by the stroke via `SetLayer` + `MapLayerChangeAccumulator` (unchanged); the stroke's internal `_centerOverrides` dictionary (now mutated in place instead of cloned per segment).
- Important readers: canvas renderer (reads tiles), undo/redo (accumulator changes), map save (codec — untouched).
- Derived/cached state affected: none beyond the per-stroke `_centerOverrides` (cleared on cancel/failure exactly as today).
- Required propagation: none new — the resolution result feeds the same `patch.Changes` application loop as today.
- Invariants to preserve:
  - Incremental result is identical to the stateless result for the same cumulative state (proven differentially).
  - Failure leaves document bytes unchanged (`_accumulator.Restore()` + clear), one failure ends the stroke.
  - Unknown terrain ID still fails on the first segment with the same failure shape.
- Observable proof: differential test (per-segment patch equality vs `TryResolvePatch`), long-stroke guard, all existing stroke/session/resolver tests unmodified and green.

**Step 1: Add the interface method (red)**

Add to `ITerrainPatchResolver` (`TerrainMapResolver.cs:8-18`):

```csharp
bool TryResolveStaged(
    MapDocument document,
    int layer,
    IReadOnlyDictionary<int, Guid?> centerOverrides,
    IReadOnlyCollection<(int Index, Guid? Center)> staged,
    out TerrainResolvedPatch patch,
    out TerrainResolutionFailure? failure);
```

Contract (state in the plan, enforce with tests — no doc comments per AGENTS.md):
- **Preconditions (caller guarantees):** every `centerOverrides` key is within `[0, document.TileCount)` and every non-null center is a known terrain in the resolver's catalog (validated when staged); every `staged` entry is already applied to `centerOverrides`.
- **Validates only `staged`, in the same deterministic order as the stateless method:** distinct-sorted by index (exactly the `direct` computation at `TerrainMapResolver.cs:87`) before validation and resolution. This is mandatory for failure-parity: `TryResolvePatch` validates cumulative keys in ascending index order (`:70-83`), so a multi-cell segment with an unknown terrain must report the lowest-index staged cell under both methods — never traversal order.
- **Resolution is identical to `TryResolvePatch`** with `directlyChangedIndices` = the staged indices: affected = union of 8-neighbor halos of distinct-sorted staged indices (neighbor included only when its effective center — override first, else stored tile's logical center — is non-null); each affected cell scored by `TerrainPatternScorer.Select` against the desired pattern built from effective centers.

Implementation: extract the shared tail of `TryResolvePatch` (from the `direct` computation through the `changes` build, `TerrainMapResolver.cs:87-163`) into a private helper `ResolveAffected(MapDocument document, int layer, IReadOnlyDictionary<int, Guid?> centerOverrides, List<int> direct, out TerrainResolvedPatch patch, out TerrainResolutionFailure? failure)`. `TryResolvePatch` keeps its full validation loop and calls the helper. `TryResolveStaged` validates the staged entries (range + `TryGetTerrain`) and calls the helper with the staged indices.

Update both `FailingAfterFirstResolver` fakes: move the fail-after-N counting from `TryResolvePatch` into `TryResolveStaged` (the path the stroke now drives); `TryResolvePatch` becomes a pure passthrough to `_inner`. This is mandatory — the stroke will no longer call `TryResolvePatch`, and the existing failure-injection tests ("one failure ends the stroke", `Calls` assertions) must keep exercising the real stroke path.

Run: `dotnet build src/MapEditor.Core/MapEditor.Core.csproj`
Expected: FAIL (interface not implemented / fakes incomplete) — red.

**Step 2: Write the differential and staged-validation tests (red)**

In `TerrainMapResolverTests.cs`:

1. `TryResolveStaged_MatchesStatelessAcrossSegments` — build a document + catalog (reuse the file's existing fixtures/helpers), then run a multi-segment scenario: segment 1 paints 3 cells, segment 2 paints 2 more adjacent cells, segment 3 erases 1 (stages `(index, null)`). Maintain the cumulative `overrides` dictionary across segments, and **apply each successful patch to the document before the next segment** (exactly as `TerrainMapEditStroke.ApplySegment` does) so the erase segment operates on painted state and the test models a real stroke. For each segment, call both `TryResolveStaged(document, layer, overrides, staged)` and `TryResolvePatch(document, layer, overrides, stagedIndices)` and assert the `patch.Changes` dictionaries are equal (and both succeed). Note: both methods share the extracted resolution helper, so this test proves entry-point equivalence (validation scoping + ordering + argument plumbing), not the helper's correctness — the file's existing independent expected-value tests continue to cover the helper. Keep both.
2. `TryResolveStaged_UnknownTerrainInStaged_FailsLikeStateless` — staged entry with an unknown terrain ID → returns false, failure message `Unknown terrain {id}.` at the staged cell; a staged entry with an out-of-range index → `ArgumentOutOfRangeException`.
3. `TryResolveStaged_MultiCellSegment_ReportsLowestIndexCell` — reverse-direction multi-cell segment (e.g. staged indices `[5, 3, 4]` in traversal order) with an unknown terrain → failure coordinates are the lowest-index staged cell (3), matching what `TryResolvePatch` reports for the same cumulative state. This is the ordering regression guard.
4. `TryResolveStaged_DoesNotRevalidateCumulativeEntries` — contract guard for the optimization: seed `centerOverrides` with an invalid entry (unknown terrain ID) at a cell far outside the affected halo, stage a valid entry elsewhere → `TryResolveStaged` succeeds while `TryResolvePatch` on the same state fails. This documents that the invalid cumulative entry violates the caller precondition and proves the incremental path actually skips cumulative re-validation (the performance point).

Run: `dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj --filter "FullyQualifiedName~TerrainMapResolverTests" -v minimal`
Expected: new tests FAIL (method not implemented) — red.

**Step 3: Implement `TryResolveStaged` + switch the stroke (green)**

Implement per Step 1's design. Then in `TerrainMapEditStroke.ApplySegment` (`TerrainMapEditStroke.cs:127-175`), replace the clone + stateless call:

```csharp
foreach (var (index, center) in staged)
{
    _centerOverrides[index] = center;
}

if (!_resolver.TryResolveStaged(_document, _layerIndex, _centerOverrides, staged, out var patch, out var failure))
{
    _accumulator.Restore();
    _centerOverrides.Clear();
    _visited.Clear();
    _active = false;
    return new TerrainEditResult(false, false, failure);
}
```

and delete the trailing `_centerOverrides = overrides;` (the dictionary is already current). Failure handling is unchanged: in-place mutation + clear-on-failure yields the same final state as clone + discard.

Per-segment cost is now local to the segment: staged validation O(S log S), affected-set build/ordering over the halo, and per-cell candidate scoring — instead of the previous cumulative O(K log K) validation plus the O(K) clone allocation (K = cumulative visited cells).

Run: `dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj -v minimal`
Expected: PASS (all Core tests, including the new ones and all pre-existing stroke/session/resolver tests).

**Step 4: Long-stroke regression guard**

In `TerrainMapEditStrokeTests.cs`: `LongStroke_PaintsFullMapInOneGesture` — 100×100 document (10,000 cells), single-terrain catalog built from `TerrainCatalogFixture` with one graphic using `TerrainCatalogFixture.Solid(center)` (sets the center and all eight peers to the terrain — with exactly one candidate, `TerrainPatternScorer.Select` always selects it regardless of score, so every painted cell resolves to that graphic), validated via `TerrainCatalogValidator.Validate(...).Index!` as in `TerrainMapEditSessionTests.cs:15-16`. Serpentine stroke: `Begin(0, 0)`, then `Continue` one cell at a time row by row (row 0 left→right, row 1 right→left, …) covering every cell. Assert: stroke stays active through the whole gesture, `Complete()` returns a single change list covering all 10,000 cells, and every cell in the document equals the single variant.

This test passes before and after the fix — it is a **regression guard, not a red/green test**: under the old O(N² log N) behavior it would take minutes-to-hours (unrunnable in CI), under the fix it completes well under a second. Do not add timing assertions (flaky).

Run: `dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj --filter "FullyQualifiedName~TerrainMapEditStrokeTests" -v minimal`
Expected: PASS.

**Step 5: Commit**

```bash
git add src/MapEditor.Core/Terrain/TerrainMapResolver.cs src/MapEditor.Core/Editing/TerrainMapEditStroke.cs tests/MapEditor.Core.Tests/Terrain/
git commit -m "perf: resolve terrain strokes incrementally"
```

| Invariant | Proved by |
|---|---|
| Incremental == stateless results | `TryResolveStaged_MatchesStatelessAcrossSegments` (with document progression) |
| Failure coordinates match stateless ordering | `TryResolveStaged_MultiCellSegment_ReportsLowestIndexCell` + `TryResolveStaged_UnknownTerrainInStaged_FailsLikeStateless` |
| Incremental path skips cumulative re-validation (the optimization) | `TryResolveStaged_DoesNotRevalidateCumulativeEntries` |
| Failure leaves bytes unchanged, one failure ends stroke | existing `TerrainMapEditStrokeTests`/`TerrainMapEditSessionTests` failure tests (unmodified) |
| Long strokes stay tractable | `LongStroke_PaintsFullMapInOneGesture` |
| One gesture = one history command | existing session/stroke tests (unmodified) |

---

### Task 2: `TryOpen` fails fast when the gate is busy

**Files:**
- Modify: `src/MapEditor.App/Terrain/TerrainOperationGate.cs` (new `TryAcquire`)
- Modify: `src/MapEditor.App/Rendering/AssetContextController.cs:271-288`
- Test: `tests/MapEditor.App.Tests/AssetContextControllerTests.cs`

**Mutation impact:**
- Source of truth changed: none (no state mutation on the new failure path; `TryPrepareOpen` already ran and its prepared context is disposed by the existing `using (prepared)`).
- Important readers: 126 test call sites of `TryOpen` (gate always idle there → no behavior change); zero production callers (UI uses `TryPrepareOpen`/`CommitPreparedOpen`).
- Derived/cached state affected: none.
- Invariants to preserve: idle-gate `TryOpen` behaves exactly as before (all 126 call sites stay green); a busy gate fails fast instead of blocking, with **no check-then-wait race**.
- Observable proof: new reentrancy test (bounded by a join timeout so the red phase cannot hang).

**Why an atomic acquire (not an `IsBusy` check):** the gate is shared with the Terrain Editor (`App.axaml.cs:63` wires `assets.Gate` into `TerrainEditorController`, which holds leases across awaits at `TerrainEditorController.cs:73,175`), and `AcquireAsync` has no creating-thread check (`TerrainOperationGate.cs:60-87`). A check-then-wait sequence (`IsBusy` then `AcquireAsync().GetResult()`) has a TOCTOU window in which another acquisition can land between the check and the wait, leaving `GetResult()` blocked. `SemaphoreSlim.Wait(0)` makes the acquire itself atomic and non-blocking, so `TryOpen` contains no blocking call at all.

**Step 1: Add the atomic non-blocking acquire to the gate (red)**

In `TerrainOperationGate` (`src/MapEditor.App/Terrain/TerrainOperationGate.cs`), mirroring `AcquireAsync`'s bookkeeping:

```csharp
public bool TryAcquire(out TerrainOperationLease? lease)
{
    if (!_semaphore.Wait(0))
    {
        lease = null;
        return false;
    }

    lock (_sync)
    {
        if (_disposed)
        {
            _semaphore.Release();
            lease = null;
            return false;
        }

        lease = new TerrainOperationLease(this);
        _current = lease;
        if (++_active == 1)
        {
            _completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        return true;
    }
}
```

Postconditions: on `true`, `lease` is the gate's active lease (`VerifyCurrent(lease)` passes) and the gate is busy; on `false`, nothing was acquired and the gate state is unchanged. `lease.Dispose()` releases exactly as an `AcquireAsync` lease does.

**Step 2: Write the failing test (red)**

No assertions inside the thread delegate (an unhandled exception there is not reported through xunit); capture state and assert on the test thread. `System.Threading.Thread` is not `IDisposable` and has no `Exception` property — no `using`, no `thread.Exception`.

```csharp
[Fact]
public void TryOpen_WhileGateBusy_FailsFastInsteadOfDeadlocking()
{
    string assetDirectory = WriteAssetDirectory("assets-busy");
    AssetFixture.WriteTerrainSidecar(assetDirectory, AssetFixture.TerrainCatalogJson);

    AssetContextController controller = null!;
    TerrainOperationLease? heldLease = null;
    bool opened = true;
    Exception? openFailure = null;
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try
        {
            controller = CreateController();
            heldLease = controller.Gate.AcquireAsync().GetAwaiter().GetResult();
            opened = controller.TryOpen(assetDirectory, out openFailure);
        }
        catch (Exception ex)
        {
            failure = ex;
        }
    }) { IsBackground = true };
    thread.Start();
    bool finished = thread.Join(TimeSpan.FromSeconds(10));
    heldLease?.Dispose();
    thread.Join();
    Assert.True(finished);
    Assert.Null(failure);
    Assert.False(opened);
    Assert.IsType<InvalidOperationException>(openFailure);
    controller.Dispose();
}
```

Everything runs on one dedicated thread (the creating thread), so the thread-pinning checks pass and the only question is the gate. Pre-fix, `TryOpen` blocks forever in `AcquireAsync().GetAwaiter().GetResult()` (the semaphore is held by `heldLease`) → the bounded join times out → `Assert.True(finished)` fails — red, cannot hang. Cleanup is ordered **before** the assertions because xunit aborts the test method at the first failed assert: `heldLease?.Dispose()` unblocks a still-stuck thread in the red case (it then proceeds into `CommitPreparedOpen`, which commits on the creating thread of the per-test controller — harmless — and the second `Join` lets the thread finish), so no blocked thread is left behind; post-fix it is a plain release. (Pre-fix this test also fails to compile until `TryAcquire` exists — that is the intended red for the gate change.)

Run: `dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj --filter "FullyQualifiedName~TryOpen_WhileGateBusy" -v minimal`
Expected: red (compile error before `TryAcquire` exists; after adding it but before rewiring `TryOpen`, the bounded join times out).

**Step 3: Rewire `TryOpen` (green)**

In `AssetContextController.cs:273-288`, replace the lease acquisition:

```csharp
using (prepared)
{
    if (!_gate.TryAcquire(out TerrainOperationLease? operation))
    {
        failure = new InvalidOperationException("The terrain operation gate is busy.");
        return false;
    }

    using (operation)
    {
        CommitPreparedOpen(operation, prepared!);
    }
}

return true;
```

`TryOpen` is now fully non-blocking. Also change `public bool TryOpen(string path)` (`:271`) to `internal` — it is a test-only convenience (126 call sites, all in tests; `InternalsVisibleTo("MapEditor.App.Tests")` at `src/MapEditor.App/Properties/AssemblyInfo.cs:3` keeps them compiling).

Run: `dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj -v minimal`
Expected: PASS (new test + all pre-existing `TryOpen` call sites).

**Step 4: Commit**

```bash
git add src/MapEditor.App/Terrain/TerrainOperationGate.cs src/MapEditor.App/Rendering/AssetContextController.cs tests/MapEditor.App.Tests/AssetContextControllerTests.cs
git commit -m "fix: fail fast when TryOpen finds the terrain gate busy"
```

| Invariant | Proved by |
|---|---|
| Busy gate fails fast, no self-deadlock, no TOCTOU | `TryOpen_WhileGateBusy_FailsFastInsteadOfDeadlocking` (atomic `Wait(0)` acquire) |
| `TryAcquire` postconditions (active lease on success, no state change on failure) | the reentrancy test + pre-existing gate/lease tests staying green |
| Idle-gate behavior unchanged | all 126 pre-existing `TryOpen` call sites green |

---

### Task 3: Hermetic thread-affinity tests

**Files:**
- Modify: `tests/MapEditor.App.Tests/AssetContextControllerTests.cs:909-940`

**Mutation impact:** test-only. No production state.

The three `*_FromOtherThread_Throws` tests currently create the controller on the xunit test thread (a thread-pool thread) and run the violating call via `Task.Run` (another thread-pool thread), relying on pool thread identity. `PrepareSave_FromOtherThread_Throws` flaked once. Rewrite all three with **dedicated threads**: controller created on dedicated thread A (joined, setup failure captured and asserted on the test thread), violating call on dedicated thread B with the exception captured into a local, then `Assert.IsType<InvalidOperationException>(failure)` on the main thread (exact type, stronger than the current `ThrowsAny`). Thread IDs are guaranteed distinct; no pool identity, no blocking `GetResult` on the test thread, no exception-type ambiguity.

Thread rules (verified): `System.Threading.Thread` is **not** `IDisposable` (no `using`) and has **no** `Exception` property — capture `Exception?` inside every delegate and assert on the test thread; no assertions inside delegates.

Shape (apply to all three; `TryPrepareOpen` and `RegisterTerrainGestureCancellation` need no `TryOpen`/`PrepareSave` setup — only controller creation on thread A):

```csharp
[Fact]
public void PrepareSave_FromOtherThread_Throws()
{
    string assetDirectory = WriteAssetDirectory("assets-thread");
    AssetFixture.WriteTerrainSidecar(assetDirectory, AssetFixture.TerrainCatalogJson);

    AssetContextController controller = null!;
    TerrainCatalogPreparedSave prepared = null!;
    Exception? setupFailure = null;
    var creating = new Thread(() =>
    {
        try
        {
            controller = CreateController();
            if (!controller.TryOpen(assetDirectory))
            {
                throw new InvalidOperationException("TryOpen failed.");
            }

            prepared = PrepareSave(assetDirectory, controller.Current, ParseCatalog(), controller.Current.Terrain.Revision);
        }
        catch (Exception ex)
        {
            setupFailure = ex;
        }
    }) { IsBackground = true };
    creating.Start();
    creating.Join();
    Assert.Null(setupFailure);

    Exception? failure = null;
    var other = new Thread(() =>
    {
        try
        {
            using TerrainOperationLease operation = controller.Gate.AcquireAsync().GetAwaiter().GetResult();
            controller.PrepareSave(operation, controller.Current, prepared);
        }
        catch (Exception ex)
        {
            failure = ex;
        }
    }) { IsBackground = true };
    other.Start();
    other.Join();
    Assert.IsType<InvalidOperationException>(failure);
}
```

The helpers used inside the dedicated threads (`CreateController`, `TryOpen`, the `PrepareSave` helper, `ParseCatalog`) have no xunit synchronization-context dependency — object creation, synchronous file/catalog work — so they are safe on a raw `Thread`. The controller is created on thread A and the violating call runs on thread B, so `VerifyCreatingThread` (`AssetContextController.cs:377-381`) throws deterministically.

Run: `dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj --filter "FullyQualifiedName~FromOtherThread" -v minimal`
Expected: PASS (behavior under test is unchanged; only the harness is hermetic). Run the filter 3× to confirm stability.

**Commit:**

```bash
git add tests/MapEditor.App.Tests/AssetContextControllerTests.cs
git commit -m "test: make thread-affinity tests hermetic"
```

---

### Task 4: Cancel the active stroke when `MapCanvas` is disposed

**Files:**
- Modify: `src/MapEditor.App/Controls/MapCanvas.cs:50-70` (`Dispose`)
- Test: `tests/MapEditor.App.Tests/MapCanvasTests.cs`

**Mutation impact:**
- Source of truth changed: `MapDocument` tile layers — only on the new dispose-mid-stroke path, where `FinishInteraction(commit: false)` → `MapDocumentViewModel.CancelStroke()` restores the pre-stroke bytes (the same cancel the Escape key uses, `MapCanvas.cs:76-127`).
- Important readers: renderer, undo/redo (cancel records no history entry), map save.
- Derived/cached state affected: none.
- Invariants to preserve: dispose with no active interaction is behaviorally unchanged (today's path); dispose mid-interaction **cancels all active interaction state** — the stroke (bytes restored, no history entry), plus panning, rectangle/marker drags, pointer capture, and paste mode (`FinishInteraction` resets all of these, `MapCanvas.cs:94-127`) — never commits. The broader reset is the established Escape-key behavior and is appropriate for dispose.
- Observable proof: adversarial test below (red before the fix: stroke still active after dispose).

**Step 1: Write the failing test (red)**

In `MapCanvasTests.cs` (using the existing `CreateSmallMapAsync` harness): `Dispose_DuringActiveStroke_CancelsTheStroke` — set a brush, `MouseDown` at a cell center (assert `session.HasActiveStroke`), `harness.Canvas.Dispose()`, then assert: `session.HasActiveStroke` is false, the painted cell's layer is still `default` (bytes restored), and the session has no new undo entry (`CanUndo` false, or the history count unchanged — use whatever the file's existing assertions use for "no command recorded").

Run: `dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj --filter "FullyQualifiedName~Dispose_DuringActiveStroke" -v minimal`
Expected: FAIL (`HasActiveStroke` still true after dispose).

**Step 2: Implement (green)**

In `MapCanvas.Dispose` (`MapCanvas.cs:50-70`), after the `_disposed` guard and before `_terrainCancellation?.Dispose()`:

```csharp
FinishInteraction(commit: false);
```

`FinishInteraction` takes the established cancel path for any active interaction (stroke, pan, rect/marker drags, capture, paste mode) and refreshes the view-model; with nothing active it only resets already-false flags and refreshes. The view-model outlives the canvas (app close order: windows → controls → assets, `MainWindow.axaml.cs:150-158`), so the call is safe during dispose.

Run: `dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj -v minimal`
Expected: PASS.

**Step 3: Commit**

```bash
git add src/MapEditor.App/Controls/MapCanvas.cs tests/MapEditor.App.Tests/MapCanvasTests.cs
git commit -m "fix: cancel active stroke when the map canvas is disposed"
```

| Invariant | Proved by |
|---|---|
| Dispose mid-stroke cancels, never commits | `Dispose_DuringActiveStroke_CancelsTheStroke` |
| Dispose with no stroke unchanged | all pre-existing MapCanvas tests |

---

### Task 5: Test hygiene and design-doc bookkeeping

**Files:**
- Modify: `tests/MapEditor.App.Tests/ShortcutTests.cs:636-652`
- Modify: `src/MapEditor.App/Views/MainWindow.axaml:4`
- Modify: `tests/MapEditor.App.Tests/MapCanvasTests.cs:50, 1395-1484` (+ all `CreateSmallMapAsync()` call sites, 58)
- Modify: `tests/MapEditor.App.Tests/TerrainEditorEndToEndTests.cs:94-110`
- Modify: `docs/plans/2026-09-13-terrain-brush-editor-design.md:260-271`

**Mutation impact:** test/doc-only, except the xmlns removal (XAML compile-time).

**Step 1: Strengthen the vacuous T-shortcut assertion**

`UnmodifiedT_WithFocusInBrushField_TypesInsteadOfSelectingTerrain` (`ShortcutTests.cs:636-652`): `SelectTerrain` already sets `ActiveTool = Terrain` (`MapDocumentViewModel.cs:229-232`), so the current `Assert.Equal(MapEditTool.Terrain, ...)` passes regardless of the TextBox guard. After `SelectTerrain`, add `harness.ViewModel.ActiveTool = MapEditTool.Eraser;` and change the assertion to `Assert.Equal(MapEditTool.Eraser, harness.ViewModel.ActiveTool);` — now the test proves the guard: T in a focused TextBox must not switch the tool, while `Assert.Contains("t", graphic.Text)` proves the key still reaches the field.

**Step 2: Remove the unused xmlns**

Delete `xmlns:rendering="using:MapEditor.App.Rendering"` (`MainWindow.axaml:4`) — zero `rendering:` usages in the file.

**Step 3: Unique temp directory per MapCanvas harness**

`CreateSmallMapAsync` (`MapCanvasTests.cs:1395-1484`) shares the fixed path `Path.Combine(Path.GetTempPath(), "map-editor-canvas-tests", "settings.json")` across all 58 harnesses, so `AppSettingsStore` state can leak between tests. Replace with a unique directory per harness, **created explicitly** — `AppSettingsStore` creates the parent only when it saves (`AppSettingsStore.cs:77-85`), so many harnesses never cause the directory to exist and a blind `Directory.Delete` would throw `DirectoryNotFoundException`:

```csharp
string tempDirectory = Path.Combine(Path.GetTempPath(), $"map-editor-canvas-{Guid.NewGuid():N}");
Directory.CreateDirectory(tempDirectory);
string settingsPath = Path.Combine(tempDirectory, "settings.json");
```

Extend the `Harness` record (`:50`) with the temp directory and make it `IDisposable`, mirroring the `MainWindowHarness` dispose idiom (`MainWindowTests.cs:70-82`):

```csharp
public void Dispose()
{
    Canvas.Dispose();
    Palette.Dispose();
    if (Window.IsVisible)
    {
        Window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    Assets.Dispose();
    Directory.Delete(TempDirectory, recursive: true);
}
```

Update all 58 call sites from `Harness harness = CreateSmallMapAsync();` to `using Harness harness = CreateSmallMapAsync();` (verify with a grep that no bare `Harness harness = CreateSmallMapAsync()` remains).

**Step 4: Derive `FrameX` from the manifest**

`FrameX` (`TerrainEditorEndToEndTests.cs:94-110`) hand-duplicates the frame layout of `AssetFixture.TerrainManifestJson` (`AssetFixture.cs:60-68`). Replace the switch with a lookup parsed **once into a dictionary** (static lazy; do not retain the `JsonDocument` — parse and discard it): read `sheets → "1" → <graphic> → [x, …]` with `System.Text.Json` and store `int graphic → int x` (the manifest's sheet "1" contains every current switch arm plus graphic 23, which the dictionary simply carries). `FrameX(graphic)` returns the x of the sheet-1 frame; if a graphic is missing, let the `KeyNotFoundException` surface (a fixture/manifest drift should fail loudly, not silently paint the wrong cell).

**Step 5: Design-doc bookkeeping**

In `docs/plans/2026-09-13-terrain-brush-editor-design.md` "Deferred work" (`:260-271`):
- Mark the long-stroke performance bullet resolved (2026-09-14): incremental `TryResolveStaged` fast path in `TerrainMapResolver` + in-place stroke overrides; stateless `TryResolvePatch` retained as the reference path; guard test `LongStroke_PaintsFullMapInOneGesture`.
- Add a short "Accepted behaviors (not defects)" subsection listing the four accepted items from the plan header, one line each.

**Step 6: Verify + commit**

Run: `dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj -v minimal`
Expected: PASS.

```bash
git add tests/ src/MapEditor.App/Views/MainWindow.axaml docs/plans/2026-09-13-terrain-brush-editor-design.md
git commit -m "test: tidy terrain test hygiene and record deferred-fix outcomes"
```

| Invariant | Proved by |
|---|---|
| T in a focused TextBox never switches the tool | strengthened `UnmodifiedT_WithFocusInBrushField_...` (would fail if the guard regressed) |
| No cross-test settings leakage in MapCanvasTests | unique per-harness temp dir + cleanup |
| Frame positions track the manifest | `FrameX` parsed from `TerrainManifestJson` |

---

## Final verification (after all tasks)

```bash
dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj -v minimal
dotnet test tests/MapEditor.Rendering.Tests/MapEditor.Rendering.Tests.csproj -v minimal
dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj -v minimal
dotnet test Goose2ClientGodot.sln -v minimal
dotnet build src/MapEditor.App/MapEditor.App.csproj -c Release
bash tests/build-map-editor-script-tests.sh
git diff --check
git status --short
```

Isolation (map format + converter untouched):

```bash
test -z "$(git diff --name-only 5711959...HEAD -- tools/AssetConverter)"
git diff --exit-code 5711959...HEAD -- src/MapEditor.Core/MapCodec.cs
```

Expected: all tests/builds pass; AssetConverter and map codec unchanged; clean tree.

## Out of scope

- All other "Deferred work" bullets (terrain sets, flood fill/line tools, per-map metadata, scoring modes, detection/generation, overlay opacity controls, multi-cell graphics, variant weights, pixel screenshot tests) — future product work.
- The four accepted behaviors listed in the plan header.
- Adding `IDisposable` to `MapCanvas`/`SpritePaletteControl` (established dispose-method pattern; the real risk — active gesture on dispose — is fixed in Task 4).
