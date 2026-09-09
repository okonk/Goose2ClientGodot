# Terrain Brush Part 3: Editor Integration Implementation Plan

**Goal:** Load and validate terrain catalogs without disrupting ordinary assets, expose per-document terrain editing and management UI, wire real canvas gestures to Part 2, and atomically save/publish reviewed terrain sets.

**Architecture:** `MapEditor.Rendering` turns Part 1 JSON plus the sprite manifest into an immutable runtime terrain catalog while distinguishing catalog validity from terrain-tool usability. `MapEditor.App` owns per-document palette/tool/mode/selection state, UI-thread canvas gestures, stable-key manager drafts, and the durable-save-before-publication boundary. Asset replacement and manager save hold one UI-thread controller reservation across loaders/cancellation, durable I/O, commit, notifications, settings, and disposal; they precompute reconciliation before irreversible work, commit context and all documents without callbacks, then notify every observer safely so observer failures become warnings rather than partial publication. Existing sprite rendering remains terrain-agnostic because Part 2 writes ordinary map tile layers.

**Tech Stack:** C#; .NET 8/10; Avalonia 11 headless UI tests; `System.Text.Json`; xUnit; Part 1 `MapEditor.Core.Terrain` catalog APIs; Part 2 `TerrainMapResolver` and terrain stroke APIs.

---

> Implement with @executing-plans, one task and commit at a time. Follow `AGENTS.md`: add no comments or doc strings unless a non-obvious invariant requires one.

## Scope and prerequisites

Parts 1 and 2 are prerequisites. Consume their locked contracts unchanged:

- Part 1 owns catalog JSON, `TerrainCatalogException`/`TerrainCatalogError`, schema validation, topology/masks, stable IDs, statuses, diagnostics, and canonical serialization: `docs/plans/2026-09-08-terrain-brush-part1-converter.md:58-394`.
- Part 2 owns `MapEditTool.Terrain`, immutable runtime resolution, and `MapEditSession.BeginTerrainStroke`, `ContinueTerrainStroke`, `CompleteStroke`, and `CancelStroke`: `docs/plans/2026-09-08-terrain-brush-part2-core-editing.md:58-128`.

Included here:

- optional `terrain-brushes.json` loading and enabled-frame validation against the already loaded manifest;
- graceful terrain unavailability while sprites, maps, and manual tools continue working;
- per-document Tiles/Terrain palette mode, terrain selection, Paint/Erase mode, and Terrain tool;
- `T` shortcut and real pointer preview/commit/cancel behavior;
- manager drafts for names, topology, review status, required masks, variants, provenance, and diagnostics;
- enabling validation, atomic save, then in-process catalog publication and open-document refresh;
- cancellation of active terrain gestures before any catalog replacement;
- automated cross-layer tests and a real generated-corpus smoke.

Excluded: converter changes, inference, map format changes, terrain-aware rendering, closest-mask fallback, transitions between terrain types, manager override files, and persistence of per-document UI selection. No map or database migration is needed: terrain edits remain ordinary map layer changes, and palette/tool/mode state is session-only.

Tasks 0-4 are independent red→green boundaries and may not reference types first introduced by a later task. Task 2 intentionally combines palette/tool exposure with complete canvas routing and disposal so no committed state can activate a Terrain tool that the canvas sends to Part 2's rejecting normal-stroke API. After each task's focused green command, run the full directly affected project suite, `dotnet build Goose2ClientGodot.sln -v minimal`, and `git diff --check` before its commit step. Task 5 runs every Core/Rendering/App suite. Project SDK default globs include the new source/tests/XAML; no `.csproj` change is required.

## APIs and facts verified before planning

| API / fact | Citation and consequence |
|---|---|
| Missing/malformed terrain JSON must not block sprites/maps; successful manager save is durable before catalog publication and cancels active terrain gestures | `docs/plans/2026-09-08-terrain-brush-design.md:108-133`. Treat terrain as an optional sub-resource, unlike the required sprite manifest. |
| Terrain palette lists enabled sets; selection activates Terrain; `T` is the shortcut; Paint/Erase are explicit per-document choices | `docs/plans/2026-09-08-terrain-brush-design.md:84-92`. Keep these values on `MapDocumentViewModel`, not global settings/window fields. |
| Sprite manifest exposes sorted frames and exact rectangle lookup via `TryGetSourceRect(SpriteReference, out SpriteSourceRect)` | `src/MapEditor.Rendering/Assets/SpriteManifest.cs:14-55,225-251`. Frame validation must use this manifest metadata and require 32×32 without loading sheet images. |
| `SpriteAssetCache.Open` first loads the required manifest; frame image loading remains lazy in `Resolve` | `src/MapEditor.Rendering/Assets/SpriteAssetCache.cs:48-52,54-108`. Terrain loading must not call `Resolve` or create a second sprite cache. |
| `AssetContext.Create` opens the sprite cache first and already degrades a missing/corrupt optional appearance sidecar | `src/MapEditor.App/Rendering/AssetContext.cs:50-67`. Follow that optional-resource pattern for terrain while retaining one shared cache/manifest. |
| Context replacement currently publishes `_current`, updates every document's sheets, invalidates canvas/palette, persists settings, then disposes the old context | `src/MapEditor.App/Rendering/AssetContextController.cs:36-80`. Insert terrain cancellation before `_current = candidate`; do not dispose the old resolver/cache while a gesture still references it. |
| Documents added after context publication are seeded in the collection-changed handler | `src/MapEditor.App/Rendering/AssetContextController.cs:86-97`. Seed terrain state there as well as in the replacement loop. |
| `MapDocumentViewModel` owns active tool/brush/sheets at `src/MapEditor.App/ViewModels/MapDocumentViewModel.cs:24-183`, delegates shared completion/cancellation at `src/MapEditor.App/ViewModels/MapDocumentViewModel.cs:546-554`, and concretely updates title/command fields then invokes canvas/palette events in `Refresh` | `src/MapEditor.App/ViewModels/MapDocumentViewModel.cs:981-1007`. Add terrain state/wrappers here; planned publication must reproduce this concrete propagation without callbacks during backing-field commit. |
| `MapCanvas` captures only after a gesture becomes active, routes manual continuation, commits on release, cancels on Escape, and currently commits strokes on capture loss | `src/MapEditor.App/Controls/MapCanvas.cs:135-204,206-334,357-387`. Terrain capture loss must cancel while legacy manual capture-loss behavior remains unchanged. |
| `FinishInteraction(false)` calls the session cancellation path and then clears capture/interaction state | `src/MapEditor.App/Controls/MapCanvas.cs:49-94`. Add a terrain-only cancellation entry point rather than using this broad method during catalog publication, so panning/selection/manual strokes are not disturbed. |
| Canvas tool routing defaults every non-select/fill tool to `BeginStroke`, whose call is direct | `src/MapEditor.App/Controls/MapCanvas.cs:389-439,542-557`. Terrain activation and dedicated Part 2 routing must land in the same Task 2 commit. |
| Sprite palette uses viewport-bounded custom rendering, shared `AssetContext.Resolve`, per-document invalidation, direct hit testing, and explicit scrollbar binding | `src/MapEditor.App/Controls/SpritePaletteControl.cs:18-106,108-225`. Terrain palette should mirror its rendering/scroll ownership and both controls need final disposal/unbinding. |
| Main window owns a `DocumentView` dictionary and reacts to remove before the workspace disposes the view model | `src/MapEditor.App/Views/MainWindow.axaml.cs:38-64,142-181`; `src/MapEditor.App/ViewModels/WorkspaceViewModel.cs:257-279`. Make `DocumentView` own/dispose the canvas and both palettes during that collection callback. |
| Main window activation commits the outgoing interaction, hosts one document canvas/palette, and rebinds the shared scrollbar | `src/MapEditor.App/Views/MainWindow.axaml.cs:346-409`. Extend this exact activation path for the selected palette. |
| Unmodified tool shortcuts are ignored after the exact `TextBox` guard | `src/MapEditor.App/Views/MainWindow.axaml.cs:483-531`. Add `T` in the same switch after that guard. |
| Asset opening currently calls the two-result controller overload and returns without warning presentation | `src/MapEditor.App/Views/MainWindow.axaml.cs:1002-1046`. Task 1 must call the three-result overload and present every warning safely. |
| `IEditorDialogs.ShowInfoAsync(string,string)` is the existing warning surface | `src/MapEditor.App/Dialogs/IEditorDialogs.cs:33-59`; production and fake implementations are `src/MapEditor.App/Dialogs/AvaloniaEditorDialogs.cs:112-115` and `tests/MapEditor.App.Tests/Fakes/FakeEditorDialogs.cs:158-173`. Use it sequentially and continue after an individual presentation failure. |
| Headless canvas tests drive real Avalonia pointer capture but their direct harness currently returns undisposed canvas/window/controller objects | `tests/MapEditor.App.Tests/MapCanvasTests.cs:30-141,923-1025`. Convert this harness to owned idempotent teardown while extending it for terrain. |
| Direct sprite-palette tests likewise create a window, control, and controller without per-harness teardown | `tests/MapEditor.App.Tests/SpritePaletteControlTests.cs:32-50,337-385`. Task 2 repairs this harness and gives the new terrain palette the same ownership proof. |
| Existing asset tests prove optional appearance degradation, successful replacement/disposal, all-document propagation, and late-document seeding | `tests/MapEditor.App.Tests/AssetContextControllerTests.cs:137-235,250-375,421-479`. Add terrain and reservation cases beside these, preserving all existing outcomes. |

## Locked Part 3 APIs and behavior

Add to `MapEditor.Rendering`:

```csharp
public readonly record struct TerrainAvailability(
    bool IsCatalogValid,
    bool IsToolAvailable,
    string? Diagnostic);

public sealed class TerrainAssetCatalog
{
    public TerrainCatalog Source { get; }
    public TerrainMapResolver Resolver { get; }
    public IReadOnlyList<TerrainSetDefinition> EnabledSets { get; }

    public static TerrainAssetLoadResult Load(string assetDirectory, SpriteManifest manifest);
    public static TerrainAssetLoadResult Validate(TerrainCatalog catalog, SpriteManifest manifest);
}

public sealed record TerrainAssetLoadResult
{
    public TerrainCatalog? Source { get; }
    public TerrainAssetCatalog? Runtime { get; }
    public TerrainAvailability Availability { get; }
    public IReadOnlyList<TerrainValidationIssue> Issues { get; }

    public TerrainAssetLoadResult(
        TerrainCatalog? source,
        TerrainAssetCatalog? runtime,
        TerrainAvailability availability,
        IEnumerable<TerrainValidationIssue> issues);

    public static TerrainAssetLoadResult Unavailable(string diagnostic);
}
```

The `TerrainAssetLoadResult` constructor copies `issues`; `TerrainAssetCatalog` copies its enabled-set list. Neither retains caller-owned mutable collections. `Load(null/blank, ...)` throws `ArgumentException` for `assetDirectory`; `Load(..., null)` and null arguments to `Validate` throw `ArgumentNullException` for the named argument. The unavailable-result factory rejects a null/blank diagnostic.

`Load` normalizes the directory, reads exactly `<assetDirectory>/terrain-brushes.json`, and returns actionable unavailability for expected `FileNotFoundException`, `DirectoryNotFoundException`, `UnauthorizedAccessException`, `IOException`, and Part 1 `TerrainCatalogException` only. It does not catch `FormatException` broadly; malformed/unsupported/schema failures are recognized by the exact terrain exception and its `Error` category. Parsed but runtime-invalid data remains in `Source` for repair. `Validate` combines Core validation with enabled-only `terrain-frame-missing` and `terrain-frame-size` issues, then orders the merged list by Part 1's exact issue ordering. Any Core issue, including `enabled-mask-missing` or `enabled-mask-empty`, produces no runtime; Part 2 accepts those two only in direct Core fault-injection resolvers so its rollback contract remains testable. Unresolved pending/disabled frames remain reviewable.

A valid catalog with zero enabled sets is not a load failure: preserve `Source`, return an empty runtime resolver/list, set `IsCatalogValid=true` and `IsToolAvailable=false`, and use `Terrain catalog has no enabled terrain sets. Open Edit > Terrain Sets… to enable a complete set.` A valid catalog with enabled sets sets both booleans true. Sort enabled sets by display name then ID and use the first variant of mask `0` as the representative. Manifest validation never resolves PNGs: lazy missing/corrupt/unreadable/undersized sheets remain paintable ordinary references and use existing `SpriteResolution` placeholders/diagnostics in palette and manager preview.

Add app state:

```csharp
internal enum AssetPaletteMode { Tiles, Terrain }

public AssetPaletteMode PaletteMode { get; set; }
public string? SelectedTerrainId { get; set; }
public TerrainEditMode TerrainMode { get; set; }
public bool IsTerrainAvailable { get; }
public string? TerrainDiagnostic { get; }
public string? TerrainStatus { get; }
```

`MapEditTool.Terrain` is routed only to `BeginTerrainStroke`; normal `BeginStroke` rejects it. Selection activates Terrain palette/tool. `T` does so only with an available selection, otherwise preserving the manual tool and showing the diagnostic. Replacement preserves an enabled ID, falls back to the first enabled set, or clears selection and returns Terrain to Pencil; Paint/Erase mode persists.

Part 1 defines authored/generated ID as topology plus sorted effective members. Manager rename/status/variant reorder leaves it unchanged. Manager identity is instead an immutable source-order `TerrainDraftKey`; blank, duplicate, or mismatched authored IDs therefore remain separately selectable and repairable. A topology change, adding a previously absent member, or removing the final use of an image-only member immediately recomputes only the authored ID with `TerrainGeneratedId.Create`. Stage the complete change and validate a temporary catalog; if it collides, reject the entire mutation with the exact Part 1 `terrain-id-duplicate` issue and preserve key, authored ID, topology, masks, members, selection, issues, and dirty state. Published document selection is reconciled only after a successful save/catalog publication.

## Audit-locked App contracts

The contracts in this section are normative and replace any shorter lifecycle wording in the task steps below.

### Exact palette/tool transitions

`SelectedLayers` can never be zero and terrain captures `MapEditSession.TopLayer`, the highest selected bit. A canvas regression sets `SelectedLayers=0b10101`, proves `TopLayer==4`, paints through real pointer input, and verifies only layer 4 changed.

| Action | Palette after | Selection after | Tool after | Other behavior |
|---|---|---|---|---|
| Publish catalog and current ID remains enabled | unchanged | same ID | unchanged | Paint/Erase persists |
| Publish catalog and current ID is absent/disabled, with enabled sets | unchanged | first runtime entry | Terrain remains Terrain | deterministic display-name/ID fallback |
| Manager save maps selected old ID to an enabled new ID | unchanged | mapped ID | Terrain remains Terrain | applies only to this save |
| Publish unavailable/invalid/zero-enabled catalog | unchanged | null | Terrain falls back to Pencil | diagnostic is actionable |
| Set/click exact enabled ID | Terrain | ID | Terrain | no map/history mutation |
| Set unknown/case-mismatched ID | unchanged | unchanged | unchanged | throw `ArgumentException` before mutation |
| Set selection to null | unchanged | null | Pencil if Terrain | no automatic fallback until publication |
| Click `TerrainPaletteTab` with usable selection | Terrain | unchanged | Terrain | Paint/Erase visible and enabled |
| Click `TerrainPaletteTab` without usable selection | Terrain | null | current nonterrain tool | Paint/Erase visible but disabled |
| Click `TilesPaletteTab` | Tiles | preserved | Pencil if Terrain | tile controls visible |
| Click `TerrainTool` or press unmodified `T` when usable | Terrain | unchanged | Terrain | handled |
| Click `TerrainTool` or press `T` when unusable | unchanged | unchanged | unchanged | copy diagnostic to `TerrainStatus`; handled |
| Click any nonterrain tool | unchanged | preserved | requested tool | no silent palette switch |
| Press a tool key in a focused `TextBox` | unchanged | unchanged | unchanged | native text input wins |

Unknown `AssetPaletteMode`/`TerrainEditMode` values throw `ArgumentOutOfRangeException`. `TerrainPaintMode` and `TerrainEraseMode` are visible whenever Terrain palette is selected and enabled only when the tool is usable. `SheetCombo`, `BrushSheet`, `BrushGraphic`, and `BrushValidationError` live in `TilePaletteControls` and are visible only in Tiles mode. UI terrain selection/mode/tab/tool changes first cancel only an active terrain preview; they do not cancel manual strokes, panning, selection, marker, or paste interactions.

### Gesture, registration, thread, and lifetime contract

Keep Part 2 as the only terrain gesture implementation. Press calls `BeginTerrainStroke` and captures only on success; in-bounds moves call `ContinueTerrainStroke`; movement outside while captured preserves the last valid sample; re-entry continues from that sample; inside/outside release commits once. Escape and terrain capture loss restore exact prior bytes and create no history; manual capture loss still commits. A Part 2 continuation failure has already restored/cleared Core state, so canvas releases capture and reports terrain ID/mask without calling cancel/complete again. Do not add an App-only missing-mask injection seam: that unreachable case remains proved by Part 2 tests.

Do not add a multicast replacement event. Use controller-owned registrations:

```csharp
internal IDisposable RegisterTerrainGestureCancellation(Action cancellation);
```

Registrations receive monotonic order. Preparation snapshots and invokes every live callback once in registration order, individually catches every `Exception`, and continues after failures. Registration/token disposal is idempotent; a callback added or removed during invocation affects only the next preparation. If any callback fails, return all failures in order and do no I/O/publication; successful callbacks may remain canceled because cancellation preserves map/history.

Controller construction, registration/token disposal, preparation/publication/abandonment, canvas interaction, manager mutation/save, and document notification are synchronous on the controller's creating UI thread. Wrong-thread calls fail before callback/state mutation except the explicitly nonthrowing abandonment API, whose wrong-thread/foreign/reused behavior is a no-op. Do not add locks, `Task.Run`, file watchers, dispatch waits, or continuation-based publication; there is no blocking wait and therefore no UI-thread deadlock path.

`MapCanvas`, `SpritePaletteControl`, `TerrainPaletteControl`, and the MainWindow-owned `DocumentView` implement idempotent disposal. `MapCanvas.Dispose` cancels any active interaction, disposes its cancellation registration, releases capture, and removes view-model/game-data/size/window subscriptions. Both palette controls dispose by unbinding the scrollbar and removing view-model/size subscriptions. Ordinary visual detach removes only the canvas's temporary window hover hook because a tab can reattach. On document removal, MainWindow unhosts/unbinds and disposes the view during the collection callback, before `WorkspaceViewModel.RemoveDocument` disposes its view model. Window close disposes all remaining views before disposing assets. Every direct canvas and direct palette test harness owns idempotent teardown, disposes controls/controllers, and closes its window.

### Three-phase planning and publication

Add opaque one-use planning/result types and exact controller operations:

```csharp
internal sealed record TerrainOperationWarning(string Scope, string Message, Exception Exception);

internal sealed record TerrainReplacementPreparation
{
    public bool ContextMatches { get; }
    public TerrainReplacementPlan? Plan { get; }
    public IReadOnlyList<Exception> CancellationFailures { get; }
    public bool Succeeded => ContextMatches && Plan is not null && CancellationFailures.Count == 0;
}

internal sealed class TerrainReplacementPlan { }

internal sealed record TerrainPublicationResult
{
    public IReadOnlyList<TerrainOperationWarning> Warnings { get; }
}

internal TerrainReplacementPreparation PrepareTerrainReplacement(
    AssetContext expectedContext,
    TerrainAssetLoadResult replacement,
    IReadOnlyDictionary<string, string>? idRekeys = null);

internal TerrainPublicationResult PublishTerrain(TerrainReplacementPlan plan);

internal void AbandonTerrainReplacement(TerrainReplacementPlan plan);

internal bool TryOpen(
    string path,
    out Exception? failure,
    out IReadOnlyList<TerrainOperationWarning> warnings);
```

Copy every collection-bearing input/result. Keep existing `TryOpen` overloads as delegates for compatibility. A controller has exactly one creating-thread replacement slot, initially empty, and each opaque plan records its owning controller plus one-use `Reserved`, `Published`, or `Abandoned` state. The exact busy failure is `InvalidOperationException("An asset replacement is already in progress.")`.

### Exact reservation protocol

1. Every operation first enforces creating thread and already-disposed state. New-operation entries then check the replacement slot **before** operation-specific null/blank/path/context validation: if occupied, `TryOpen` returns `false` with the exact busy exception in `failure` and empty warnings, while `PrepareTerrainReplacement` and controller `Dispose` throw it. `PublishTerrain` instead validates the supplied plan against the occupied slot as item 5 specifies. These exits occur before path normalization, candidate construction, cancellation callbacks, file operations, context/document/settings mutation, notifications, unsubscription, or resource disposal.
2. Full `TryOpen` creates a one-use operation plan and occupies the slot **before** invoking `_openContext`, so an injected loader/file fake cannot recursively open or dispose/reorder the controller. A direct terrain preparation validates basic arguments and `ReferenceEquals(Current, expectedContext)`, then immediately creates/occupies its plan before snapshotting documents, precomputing immutable backing-field changes/exact changed-property order, or invoking the first cancellation callback.
3. Cancellation snapshots run in registration order while the slot is occupied. Recursive `TryOpen`, replacement preparation, manager save, or controller disposal is rejected before its own callbacks/state mutation. Registration-token disposal remains allowed and affects only the next snapshot. On any cancellation/planning/candidate failure after reservation, the owning operation calls the exact `AbandonTerrainReplacement(plan)` path before returning/throwing; cancellation failure returns no plan.
4. A successful preparation returns its still-reserved plan. The manager performs its durable store write while that reservation remains held, so a file-operation fake cannot start another open/save/disposal. Every pre-publication manager exit after successful preparation—including create/write/flush/stream-dispose/move/replace/cleanup handling—calls `AbandonTerrainReplacement(plan)` in `finally` unless publication consumed the plan.
5. `PublishTerrain` accepts only the currently reserved plan owned by that controller on its creating thread. A plan from another controller throws `ArgumentException` with parameter `plan`; a published/abandoned plan or an owned plan that is no longer the active reservation throws `InvalidOperationException`; all reject before commit/notification. `AbandonTerrainReplacement` is deliberately nonthrowing: null, foreign, wrong-thread, published, abandoned, and noncurrent plans are no-ops; only the active owned reserved plan becomes `Abandoned` and releases the slot. Repeated abandonment is a no-op.
6. A valid publication commits context then every preplanned document without callbacks, then safely notifies all document observers. Task 4's manager overload also commits the already-prepared draft baseline without callbacks and safely notifies manager observers before returning. Full `TryOpen` additionally updates settings and disposes the old context. The reservation is released only after **all** document/manager notifications, settings work, and old-context disposal attempts finish. Nested observer calls therefore see the busy result and cannot reorder resources; no user callback runs after release but before the operation returns.

`MapDocumentViewModel` separates `PlanAssetState`/`PlanTerrainState`, no-callback `CommitAssetState`/`CommitTerrainState`, and safe notification. Add a protected helper to `ViewModelBase` that individually invokes `PropertyChanged` subscribers; safe publication similarly invokes every `CanvasInvalidated` and `PaletteInvalidated` subscriber. Ordinary setters keep current throwing multicast behavior.

Warnings are collected in deterministic operation order: document order → planned property order → `CanvasInvalidated` subscription order → `PaletteInvalidated` subscription order → optional manager property order/subscription order → settings → old-context disposal. Use these exact stable scopes/messages (`<i>` is zero-based, `<property>` is the planned property name, and `<error>` is `Exception.Message`):

| Scope | Exact `Message` |
|---|---|
| `document:<i>:PropertyChanged:<property>` | `Document <i> property '<property>' observer failed after asset publication: <error>` |
| `document:<i>:CanvasInvalidated` | `Document <i> canvas observer failed after asset publication: <error>` |
| `document:<i>:PaletteInvalidated` | `Document <i> palette observer failed after asset publication: <error>` |
| `terrain-manager:PropertyChanged:<property>` | `Terrain manager property '<property>' observer failed after catalog publication: <error>` |
| `settings` | `Assets were published, but saving the asset directory failed: <error>` |
| `old-context-disposal` | `Assets were published, but disposing the previous asset context failed: <error>` |

Warning construction uses precomputed scope/message prefixes and cannot throw for represented observer/settings/disposal failures.

A prepared manager plan performs no I/O, validation, resolver construction, document planning, or sprite-context disposal during publication. Observer failures never roll back: disk, context, every document, manager baseline, map bytes, and history already agree. Full `TryOpen` cancellation failure disposes the unpublished candidate through the abandonment path and preserves old context/settings/runtime/map/history; post-publication notification/settings/old-context-disposal failures are warnings and open still succeeds.

`AssetContext.Dispose` attempts cache disposal and tint-cache disposal independently in that order. Preserve the first exception and attach a second at `first.Data["AssetContext.AdditionalDisposeException"]`. A replacement reports disposal failures as warnings after publication and never claims rollback. The published `Current` context is controller-owned and consumers never dispose it directly.

MainWindow uses `_assets.TryOpen(path, out failure, out warnings)`. After successful state sync it sequentially calls `ShowInfoAsync("Load assets warning", $"{warning.Scope}: {warning.Message}")` for every warning in returned order. After a successful manager save it uses title `Terrain Sets warning` and the same exact message format. A nonfatal exception from one `ShowInfoAsync` is caught and the next warning is still attempted; presentation failure cannot alter the already published file/context/documents/manager baseline. `OutOfMemoryException` remains outside the represented warning guarantee.

### Stable-key drafts and selection rekeys

```csharp
internal readonly record struct TerrainDraftKey(int Value);
```

Assign keys by source array order and never change them. All manager selection and mutations use keys, not authored IDs. Clone/build preserves schema/generator/fingerprint/settings, metrics, provenance, diagnostics, authored IDs, masks, and variant order until an explicit edit. `Build` may return semantically invalid repair data; `CanSave` requires a dirty issue-free result. `RegenerateId(key)` repairs blank/mismatched IDs; duplicate authored IDs remain independent. Truly identical generated identities must first be made distinct through topology/effective membership or remain invalid with Part 1's exact duplicate issue.

A topology change retains mappings reachable in the new topology, creates newly required rows, and moves unreachable mappings to draft-only orphans. Orphans block save and are removed only via `RemoveOrphanMask`/`RemoveAllOrphanMasks`; they never serialize. Tests perform 8→4, explicitly remove orphans, complete rows as needed, save, reload, and compare the exact catalog. Dirty state compares canonical built content plus orphan state to the accepted baseline, so edit/revert is clean and rejected mutation is not dirty.

For each originally unique enabled ID, track old ID to current generated ID. On that manager save only, a document selected on old ID follows the new ID when enabled. If not enabled, preserve the same enabled ID if possible and otherwise use ordinary first-enabled fallback. After save, accept current IDs as the next baseline. Full asset replacement has no ID map. Test with at least three enabled sets and rekey the middle/non-first one.

### Exact manager, store, and dialog ownership

```csharp
internal sealed class TerrainCatalogFileStore
{
    public TerrainCatalogFileStore();
    internal TerrainCatalogFileStore(ITerrainCatalogFileOperations operations);
    public void Write(string assetDirectory, string serializedCatalog);
}

internal enum TerrainCatalogSaveStatus
{
    Succeeded,
    NoChanges,
    InvalidDraft,
    SourceContextChanged,
    ReplacementInProgress,
    GestureCancellationFailed,
    WriteFailed
}

internal sealed record TerrainCatalogSaveResult
{
    internal TerrainCatalogSaveResult(
        TerrainCatalogSaveStatus status,
        IEnumerable<TerrainValidationIssue> issues,
        Exception? failure,
        IEnumerable<TerrainOperationWarning> warnings);

    public TerrainCatalogSaveStatus Status { get; }
    public IReadOnlyList<TerrainValidationIssue> Issues { get; }
    public Exception? Failure { get; }
    public IReadOnlyList<TerrainOperationWarning> Warnings { get; }
    public bool ClosesDialog => Status is TerrainCatalogSaveStatus.Succeeded or TerrainCatalogSaveStatus.NoChanges;
}

internal sealed class TerrainCatalogManager
{
    internal TerrainCatalogManager(
        AssetContextController assets,
        AssetContext sourceContext,
        TerrainCatalogFileStore fileStore);

    internal TerrainSetsManagerViewModel ViewModel { get; }
    internal string AssetDirectory { get; }
    internal TerrainCatalogSaveResult Save();
}
```

The manager constructor rejects nulls, a context other than `assets.Current`, missing sprite manifest/cache, or a source whose `Terrain.Source` is null. It captures exact context reference, normalized `sourceContext.Cache.AssetDirectory`, manifest identity, source catalog, and preview resolver. Before any cancellation, `Save` builds, validates, constructs the replacement result, canonical-serializes in memory, computes ID rekeys, and asks the controller to precompute all document state. Clean returns `NoChanges` without validation/cancellation/I/O. Invalid returns exact issues without cancellation. A changed context, even at the same path, or changed normalized directory/manifest returns `SourceContextChanged`; dirty data remains for discard/cancel and repeated save stays rejected.

The save-result constructor copies nonnull collections and validates status shape: `InvalidDraft` alone has nonempty issues; `SourceContextChanged` has an `InvalidOperationException` with `The asset context changed while Terrain Sets was open. Discard this draft and reopen Terrain Sets against the current assets.`; `ReplacementInProgress` has the exact busy `InvalidOperationException`; `GestureCancellationFailed` has the ordered `AggregateException`; `WriteFailed` has the exact primary exception; only `Succeeded` may carry publication warnings; success/no-change have no failure/issues.

`TerrainCatalogManager.Save` has its own creating-thread and `_saveInProgress` entry guard. Reentry on the same manager returns `ReplacementInProgress` before draft validation, cancellation, or file I/O; another manager reaches the controller's busy rejection and maps it to the same status. The outer invocation remains authoritative.

Cancellation failures return ordered `GestureCancellationFailed`; all callbacks were attempted and no write occurred. Create/write/flush/stream-dispose/replace/move failures return `WriteFailed` with the exact primary exception, and a `finally` calls nonthrowing `AbandonTerrainReplacement` before returning; old destination/runtime/cache/document selection/map/history remain, dirty remains retryable, and a safe preview may remain canceled. Success durably replaces the file, calls only nonthrowing publication through a manager-aware overload that keeps the reservation through baseline acceptance and notifications, leaves context/cache/renderer/tint identity intact, accepts the already prepared draft baseline through no-callback assignment, safely notifies manager observers, and returns observer warnings. Baseline acceptance allocates nothing, validates nothing, and cannot throw for a prepared plan.

The six-method `ITerrainCatalogFileOperations` shown in Task 4 is exact. `Write` rejects null/blank directory and null text, normalizes the directory, and uses one unique same-directory `terrain-brushes.json.tmp-<guid N>` with create-new semantics. Write UTF-8 without BOM, `Flush(flushToDisk:true)`, dispose the stream, then production `Replace` uses `File.Move(source,destination,overwrite:true)` when destination existed at decision time or `Move` uses non-overwriting `File.Move`. On failure delete only this invocation's temp; attach cleanup failure to the primary and rethrow the original with `ExceptionDispatchInfo`. Never sweep pre-existing `*.tmp-*`; tests preseed stale temps and prove they remain untouched.

The dialog interface is asynchronous and accepts the owner service, not a bare view model:

```csharp
Task<TerrainCatalogSaveResult?> ShowTerrainSetsAsync(TerrainCatalogManager manager);
```

`EditorDialogsProxy` delegates it; `FakeEditorDialogs` records the exact manager and returns a configured/gated task. `AvaloniaEditorDialogs` opens a resizable owner-centered dialog with exact constructor `internal TerrainSetsDialog(TerrainCatalogManager manager, Func<Task<DirtyChoice>> confirmDirtyAsync)`. Save invokes only `manager.Save`; failure focuses status/validation and keeps the same dirty draft for retry. Clean Cancel/title close exits. Dirty close runs Save/Discard/Cancel: Save uses the same path, Discard changes no file/runtime, Cancel stays open. While modal work is pending, the existing MainWindow command guard refuses tab switching/window close. A successful result's warnings are shown with `ShowInfoAsync`; warning presentation cannot undo publication.

`TerrainSetsCommand` is under Edit and enabled exactly when `Current.IsAvailable`, `Current.Terrain.Source` is nonnull, and no command is running. It is disabled initially and for missing/malformed terrain, enabled for parsed invalid and valid zero-enabled catalogs, unchanged by active-document/new/open/close changes, and recomputed after context publication. It captures current context, constructs manager/store, and awaits the dialog. Existing `RunCommandAsync` commits the active interaction before opening; invalid `manager.Save` itself cancels nothing.

The dialog contains exact names `TerrainSetList`, `TerrainNameBox`, `TerrainStatusCombo`, `TerrainTopologyCombo`, `TerrainMemberList`, `TerrainDiagnosticList`, `TerrainMaskList`, `TerrainVariantList`, `AddVariantSheetBox`, `AddVariantGraphicBox`, `AddVariantButton`, `RemoveVariantButton`, `MoveVariantUpButton`, `MoveVariantDownButton`, `RemoveOrphanMaskButton`, `RemoveAllOrphansButton`, `RegenerateIdButton`, `TerrainMaskPreview`, `PreviewDiagnosticText`, `ValidationSummary`, `RegenerationWarningText`, `SaveButton`, and `CancelButton`. Lists/regions own scroll viewers. Preview uses the captured context's lazy resolver, resolves only visible ordered variants, and exposes the first non-ready `SpriteResolution` diagnostic while drawing the existing placeholder. Tests assert names, binding/state, references, viewport-bounded draw calls, and placeholder diagnostics, never screenshots or pixel rasterization.

## Mutation impact matrix

| Mutation | Source and readers | Propagation / atomicity |
|---|---|---|
| Open assets | Manifest plus optional terrain JSON → context/documents/palettes | Reserve before candidate loader/cancellation callbacks; build/plan; cancel; commit/notify; update settings; dispose old; release. Bad terrain degrades only terrain; bad manifest abandons and preserves old context. |
| Select terrain state | Per-document palette/ID/mode → window/palette/canvas | Cancel only an active terrain preview before transition; notify/redraw only; never commit map/history. |
| Pointer gesture | Part 2 active stroke → map renderer/history/title | Preview cumulatively; release creates one command; failure/Escape/capture loss/disposal restores exact state. |
| Edit manager | Isolated draft → manager views/validation | Rebuild and validate draft only; file/runtime/map remain unchanged. |
| Save manager | Valid draft → JSON/runtime/documents/baseline | Precompute; reserve/cancel; durable sibling-temp replace while reserved; nonthrowing commit/notify; release. Every pre-publication failure abandons and preserves file/runtime. |

## Invariant-to-test matrix

| Invariant | Primary proof |
|---|---|
| Terrain failure never blocks sprite/map/manual editing | loader, context, and malformed-terrain end-to-end tests |
| Enabled references exist and are exactly 32×32 | Rendering missing/non-32/member adversarial tests |
| Selection and Paint/Erase mode are per document | multi-tab view-model/window tests |
| One drag is one Part 2 undo command | headless paint/erase canvas tests |
| Escape, capture loss, and replacement restore previews | encoded-byte/history-version adversarial tests |
| Draft edits cannot leak before save | draft/service identity tests |
| Stable-key drafts repair malformed authored IDs and identity edits are atomic | blank/duplicate/mismatch, regenerate/collision, orphan, and save/reload tests |
| Reversible cancellation precedes durable save, which precedes nonthrowing publication | injected cancellation/write/flush/replace and publication-order tests |
| Nested loader/cancellation/file-fake/observer operations cannot reorder publication | exact reservation/reentry/foreign/reused-plan tests |
| Every failed reserved operation abandons and permits the next operation | cancellation/write failure plus retry tests |
| Publication cannot leave a stale active resolver | replacement-during-preview test |
| No commit exposes Terrain before safe routing exists | combined Task 2 activation-to-map blocker test |
| Canvas and both direct palettes stop callbacks after disposal | idempotent direct-harness disposal tests |

### Task 0: Load, validate, and degrade terrain assets in Rendering

**Files:**
- Create: `src/MapEditor.Rendering/Terrain/TerrainAvailability.cs`
- Create: `src/MapEditor.Rendering/Terrain/TerrainAssetCatalog.cs`
- Create: `tests/MapEditor.Rendering.Tests/TerrainAssetCatalogTests.cs`
- Regression: `tests/MapEditor.Rendering.Tests/SpriteManifestTests.cs`
- Regression: `tests/MapEditor.Rendering.Tests/SpriteAssetCacheTests.cs`

**Mutation impact:**
- Source of truth changed: none; `terrain-brushes.json` and the already parsed `SpriteManifest` are read-only inputs.
- Important readers: `AssetContext` in Task 1, terrain palettes/gestures, and manager draft creation.
- Derived/cached state affected: immutable enabled-set ordering, representative references, and one `TerrainMapResolver`. No global cache or image ownership is added.
- Required propagation sequence: read optional file → Part 1 parse → shared catalog validation → reject every Core issue, including missing/empty enabled masks → enabled-only manifest/32×32 checks → construct resolver/runtime index → return one complete result.
- Invariants to preserve: expected terrain failures become unavailable results; parsed invalid review data remains available as `Source`; pending/disabled unresolved references do not poison valid enabled runtime sets; sprite sheets are never loaded.
- Observable proof required: assert final result/catalog/issue values and a normal `SpriteAssetCache.Resolve` path separately, not only exception handling.

**Step 1: Write failing tests**

Add:

- `Load_NullBlankDirectoryAndNullManifestUseExactArguments`
- `Validate_NullCatalogOrManifestUsesExactArguments`
- `Unavailable_BlankDiagnosticIsRejected`
- `Load_MissingUnreadableMalformedOrUnsupported_ReturnsActionablePathAndExactError`
- `Validate_CompleteEnabledSetWithExactFrames_BuildsResolverAndSortedPaletteSets`
- `Validate_RepresentativeIsFirstMaskZeroVariant`
- `Validate_ValidCatalogWithNoEnabledSets_IsValidButToolUnavailable`
- `Validate_EnabledMissingFrameOrNon32Frame_ReturnsExactIssuesAndNoRuntime`
- `Validate_PendingAndDisabledUnresolvedFrames_RemainReviewable`
- `Validate_DuplicateEnabledMembershipReturnsNoRuntime`
- adversarial `Validate_VariantFrameExistsButMemberFrameDoesNot_IsRejected`
- `Load_DoesNotInvokeSpriteSheetLoader` by writing a real `manifest.json`, opening `SpriteAssetCache.Open(root, countingLoader)`, passing `cache.Manifest!`, and asserting zero loader calls before and after terrain load.

Generate complete masks through `TerrainMasks.Required`; do not hand-author 16/47 copies. The loader API has no sheet-loader parameter; do not add one merely for this test.

**Step 2: Verify red**

```bash
dotnet test tests/MapEditor.Rendering.Tests/MapEditor.Rendering.Tests.csproj \
  --filter 'FullyQualifiedName~TerrainAssetCatalogTests' -v minimal
```

Expected: compile failure because terrain asset types do not exist.

**Step 3: Implement the immutable loader**

Catch only the exact file exceptions and `TerrainCatalogException` listed in the locked behavior; include `TerrainCatalogError` and path in the unavailability diagnostic. Allow unrelated `FormatException`, `OutOfMemoryException`, and programming errors to escape. Convert `TerrainGraphicReference` to `SpriteReference` only at this boundary. For the distinct union of each enabled set's members and variants, emit `terrain-frame-missing` with message `Enabled terrain '<id>' reference (s,g) is absent from sprite manifest.` or `terrain-frame-size` with message `Enabled terrain '<id>' reference (s,g) is <w>x<h>; expected 32x32.`; set `TerrainId`/`Reference`, leave `Mask` null, exact-deduplicate, and sort merged issues by `(TerrainId null first ordinal, Mask null first numeric, Reference null first then Sheet/Graphic, Code ordinal, Message ordinal)`. Integers use invariant format. Defensively copy exposed lists. `Validate` must not mutate review status to make a catalog pass.

**Step 4: Verify green and Rendering regressions**

```bash
dotnet test tests/MapEditor.Rendering.Tests/MapEditor.Rendering.Tests.csproj \
  --filter 'FullyQualifiedName~TerrainAssetCatalogTests|FullyQualifiedName~SpriteManifestTests|FullyQualifiedName~SpriteAssetCacheTests' -v minimal
```

Expected: PASS; terrain failures remain isolated from required manifest/cache behavior and no test loads a sheet during catalog validation.

| Invariant | Proved by |
|---|---|
| Optional terrain failure cannot poison sprite assets | load/degradation and cache regression tests |
| Every enabled member/variant resolves to an exact 32×32 manifest frame | missing/non-32/member adversarial tests |
| Pending/disabled repair data remains available | unresolved review-data test |
| Runtime/issue collections are immutable and deterministically ordered | constructor/order tests |
| Validation never triggers lazy PNG loading | counting-loader integration test |

**Step 5: Commit**

```bash
git add src/MapEditor.Rendering/Terrain tests/MapEditor.Rendering.Tests/TerrainAssetCatalogTests.cs
git commit -m "feat: load terrain asset catalogs"
```

### Task 1: Add document terrain state and publish it through the asset context

**Files:**
- Create: `src/MapEditor.App/Rendering/TerrainReplacement.cs`
- Modify: `src/MapEditor.App/ViewModels/ViewModelBase.cs:8-27`
- Modify: `src/MapEditor.App/ViewModels/MapDocumentViewModel.cs:24-183,546-554,981-1007`
- Modify: `src/MapEditor.App/Rendering/AssetContext.cs:7-81`
- Modify: `src/MapEditor.App/Rendering/AssetContextController.cs:10-104`
- Modify: `src/MapEditor.App/Views/MainWindow.axaml.cs:1002-1046`
- Modify: `tests/MapEditor.App.Tests/Fixtures/AssetFixture.cs:8-34`
- Modify: `tests/MapEditor.App.Tests/Fakes/FakeEditorDialogs.cs:40-64,158-173`
- Modify: `tests/MapEditor.App.Tests/MapDocumentViewModelTests.cs`
- Modify: `tests/MapEditor.App.Tests/AssetContextControllerTests.cs`
- Modify: `tests/MapEditor.App.Tests/MainWindowTests.cs`
- Modify: `tests/MapEditor.App.Tests/AppStartupTests.cs`

**Mutation impact:**
- Source of truth changed: `_current` in `AssetContextController` remains the published asset context; each context gains an immutable terrain load result. Each `MapDocumentViewModel` owns its selected enabled terrain ID and Paint/Erase mode.
- Important readers: every document, both palette controls, `MapCanvas`, manager command, and status chrome.
- Derived/cached state affected: each document's available enabled IDs/selection and palette invalidation; existing sprite cache/tint cache ownership is unchanged. Palette mode, interactive selection activation, Terrain-tool validation/fallback, and every UI route are added atomically in Task 2. This task only seeds/reconciles catalog-backed selection/mode state and never sets `ActiveTool` to Terrain.
- Required propagation sequence: reserve before invoking `_openContext` or cancellation callbacks → fully construct the candidate → precompute changes for the workspace's initial document and every other open document → invoke each cancellation registration safely → assign `_current` → commit all document fields without callbacks → notify every observer safely → persist settings → dispose old cache/tint independently → release the reservation.
- Publication boundary: candidate/planning/cancellation failure follows exact abandonment and leaves old context/settings/runtime/map/history unchanged, although successful preview cancellations may remain canceled. After `_current` assignment there is no rollback; notification/settings/disposal exceptions are ordered warnings and every later document/observer is still processed while nested open/replacement/controller-disposal calls are rejected as busy.
- Invariants to preserve: bad terrain still publishes good sprites; bad required manifest publishes nothing; valid zero-enabled terrain remains manager-editable but tool-unusable; replacing terrain never creates another sprite cache; existing initial and late documents receive current terrain state; no nested operation can reorder the active/old context or release someone else's reservation.
- Observable proof required: assert context/directory/settings identity, both disposal attempts, sprite resolution, all document backing state before first callback, subscriber order/warnings, map/history preservation, registration ordering, exact busy outcomes, and foreign/reused plan state.

**Step 1: Write failing tests**

Extend `AssetFixture` with `WriteTerrainCatalog(TerrainCatalog)` using `TerrainCatalogJson.Serialize`. Add document tests for deterministic fallback/ID-map planning, no-callback commit, and notification order; defer interactive selection/tool transition tests to the combined Task 2 boundary. Then add:

- `Constructor_SeedsExistingInitialDocumentWithNoAssetsTerrainDiagnostic`
- `TryOpen_MissingOrMalformedTerrain_PublishesSpritesAndActionableTerrainState`
- `TryOpen_InvalidEnabledFrames_RetainsSourceForManagerButNoRuntime`
- `TryOpen_ValidEmptyTerrain_PublishesSourceButNoUsableTool`
- `TryOpen_ValidTerrain_SeedsEveryExistingAndLateDocument`
- `PrepareTerrainReplacement_InvokesThreeRegistrationsInOrderAndContinuesAfterMiddleFailure`
- `PrepareTerrainReplacement_DisposedRegistrationIsNotInvoked`
- `PrepareTerrainReplacement_WrongThreadFailsBeforeCallbacksOrState`
- adversarial `PrepareTerrainReplacement_CancellationCallbackNestedTryOpenAndDisposeAreRejectedBeforeNestedMutation`
- `PrepareTerrainReplacement_CancellationFailureAbandonsReservationAndNextPreparationSucceeds`
- `AbandonTerrainReplacement_IsNonthrowingForNullForeignWrongThreadAndReusedPlansWithoutReleasingAnotherReservation`
- `PublishTerrain_ForeignPlanThrowsArgumentExceptionAndReusedPlanThrowsInvalidOperationBeforeMutation`
- adversarial `TryOpen_OpenContextFakeReentryIsRejectedAndOuterContextDirectoryWins`
- adversarial `TryOpen_CancellationFailurePreservesContextDirectorySettingsMapAndHistory`
- adversarial `TryOpen_FirstDocumentFirstObserverNestedTryOpenAndDisposeAreRejectedWhileAllStateAndLaterObserversCommit`
- `TryOpen_PostPublicationObserverSettingsOrOldDisposeFailureReturnsSuccessWithOrderedWarningsAndReleasesReservation`
- `MainWindow_AssetOpenWarningsUseExactTitleMessageAndReturnedOrder`
- adversarial `MainWindow_AssetOpenWarningDialogFailureStillAttemptsLaterWarningsAndLeavesPublishedStateUnchanged`
- `AssetContext_DisposeAttemptsTintAfterCacheFailureAndPreservesFirstException`
- adversarial `TryOpen_BadBaseManifest_DoesNotCancelOrChangePublishedTerrain`.

**Step 2: Verify red**

```bash
dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj \
  --filter 'FullyQualifiedName~AssetContextControllerTests|FullyQualifiedName~MapDocumentViewModelTests|FullyQualifiedName~MainWindowTests|FullyQualifiedName~AppStartupTests' -v minimal
```

Expected: new tests fail to compile because context/controller terrain state, reservation APIs, three-result warning handling, and safe publication types are absent.

**Step 3: Implement construct/cancel/publish propagation**

Add the Part 3 document state plus immutable planned changes, no-callback commit methods, and safe per-subscriber notification from the audit-locked contract. Add `TerrainAssetLoadResult Terrain` to `AssetContext`; unavailable context uses an explicit sprite-assets-unavailable diagnostic. Add registration, one-use plan, reservation, preparation, publication, nonthrowing abandonment, warning, and three-argument `TryOpen` surfaces exactly as locked; do not add a multicast replacement event.

Seed the workspace's already-created documents in the controller constructor, and use the collection handler for later additions. Full open acquires its operation plan before `_openContext`, transfers the same reservation through plan → cancellation → commit-all → notify-all → settings → old-context disposal, and releases it last. Every failure after acquisition invokes `AbandonTerrainReplacement` exactly once (repeated calls remain harmless). Make `AssetContext.Dispose` attempt both cache/tint resources and make post-publication failures warnings.

Update `MainWindow.TryOpenAssetsAsync` to the three-result overload and add a sequential warning presenter with the exact asset-open title/message contract. Extend `FakeEditorDialogs` with an invocation-indexed queue of `ShowInfoAsync` failures so tests prove continuation. Do not add file watching, background reload, locks, waits, or async controller publication.

**Step 4: Verify green**

```bash
dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj \
  --filter 'FullyQualifiedName~AssetContextControllerTests|FullyQualifiedName~MapDocumentViewModelTests|FullyQualifiedName~MainWindowTests|FullyQualifiedName~AppStartupTests' -v minimal
```

Expected: PASS, including nested-open/disposal rejection, exact release/abandon behavior, ordered warning presentation, and all existing asset/window regressions.

| Invariant | Proved by |
|---|---|
| One reservation spans loader/cancellation/commit/notification/settings/disposal | loader-, cancellation-, observer-, settings-, and disposal-reentry tests |
| Every pre-publication failure releases through abandonment | candidate/cancellation failure tests plus next-operation success |
| Foreign/reused plans cannot publish or release another operation | plan ownership/state tests |
| Observer/presentation failures cannot roll back or suppress later work | all-document observer and MainWindow warning tests |
| Context, directory, documents, map, history, and settings have specified all-old/all-new outcomes | cancellation/bad-manifest/publication warning snapshots |

**Step 5: Commit**

```bash
git add src/MapEditor.App/Rendering/TerrainReplacement.cs \
  src/MapEditor.App/ViewModels/ViewModelBase.cs \
  src/MapEditor.App/ViewModels/MapDocumentViewModel.cs \
  src/MapEditor.App/Rendering/AssetContext.cs \
  src/MapEditor.App/Rendering/AssetContextController.cs \
  src/MapEditor.App/Views/MainWindow.axaml.cs \
  tests/MapEditor.App.Tests/Fixtures/AssetFixture.cs \
  tests/MapEditor.App.Tests/Fakes/FakeEditorDialogs.cs \
  tests/MapEditor.App.Tests/MapDocumentViewModelTests.cs \
  tests/MapEditor.App.Tests/AssetContextControllerTests.cs \
  tests/MapEditor.App.Tests/MainWindowTests.cs \
  tests/MapEditor.App.Tests/AppStartupTests.cs
git commit -m "feat: publish terrain asset availability"
```

### Task 2: Expose the Terrain UI with complete canvas routing and disposal

**Files:**
- Create: `src/MapEditor.App/ViewModels/AssetPaletteMode.cs`
- Create: `src/MapEditor.App/Controls/TerrainPaletteControl.cs`
- Modify: `src/MapEditor.App/ViewModels/MapDocumentViewModel.cs:24-183,546-554,981-1007`
- Modify: `src/MapEditor.App/Controls/MapCanvas.cs:18-94,135-204,206-439,542-580`
- Modify: `src/MapEditor.App/Controls/SpritePaletteControl.cs:18-106,201-225`
- Modify: `src/MapEditor.App/Views/MainWindow.axaml`
- Modify: `src/MapEditor.App/Views/MainWindow.axaml.cs:38-181,346-409,483-531,1048-1094,1139-1186`
- Modify: `src/MapEditor.App/Styles/Icons.axaml`
- Modify: `tests/MapEditor.App.Tests/MapDocumentViewModelTests.cs`
- Modify: `tests/MapEditor.App.Tests/MapCanvasTests.cs`
- Modify: `tests/MapEditor.App.Tests/SpritePaletteControlTests.cs`
- Create: `tests/MapEditor.App.Tests/TerrainPaletteControlTests.cs`
- Modify: `tests/MapEditor.App.Tests/MainWindowTests.cs`
- Modify: `tests/MapEditor.App.Tests/MainWindowDefaultLayoutTests.cs`
- Modify: `tests/MapEditor.App.Tests/MainWindowCloseTests.cs`
- Modify: `tests/MapEditor.App.Tests/ShortcutTests.cs`
- Modify: `tests/MapEditor.App.Tests/ToolbarIconTests.cs`
- Modify: `tests/MapEditor.App.Tests/NpcPreviewCanvasTests.cs`
- Modify: `tests/MapEditor.App.Tests/GameDataCanvasTests.cs`
- Modify: `tests/MapEditor.App.Tests/AvaloniaMapDrawSinkTests.cs`

**Mutation impact:**
- Source of truth changed: each `MapDocumentViewModel` gains palette mode and Terrain tool state; Part 2's active terrain stroke remains the sole owner of map preview intent/deltas; canvas owns only pointer/capture kind; each `DocumentView` owns one canvas and both palette controls.
- Important readers: main-window palette host/tool toggles/mode controls/status, both palettes, canvas routing, map renderer, history/timeline/title/commands, controller cancellation registrations, and document/window teardown.
- Derived/cached state affected: selected enabled definition, representative preview, per-palette extent/offset, tool checked state, canvas gesture kind, cancellation token, and view subscriptions. No persisted settings or second stroke buffer is added.
- Required propagation sequence:
  1. Task 1 catalog assignment reconciles selection/availability.
  2. A user selection/tab/tool/shortcut transition cancels only an active terrain preview, then applies the exact transition table and updates hosted controls.
  3. Terrain press resolves current runtime/selection and calls `BeginTerrainStroke`; capture occurs only on success. Move calls `ContinueTerrainStroke`; release completes once; Escape/capture loss/replacement/disposal cancels.
  4. Final document/window teardown unhosts and unbinds, disposes canvas/terrain palette/sprite palette idempotently, then allows view-model and asset disposal.
- Publication/lifecycle boundary: this is one commit. Do not commit or offer any palette, toolbar, or shortcut path to `MapEditTool.Terrain` until dedicated canvas routing, capture-loss policy, cancellation registration, and disposal are all green.
- Invariants to preserve: state does not leak between tabs; only enabled exact IDs are selectable; UI changes do not edit map/history; one gesture is one Part 2 command; manual capture loss still commits; no disposed control receives callbacks; tile/manual/game-data interactions remain unchanged.
- Observable proof required: use real headless pointer input and direct control harnesses to compare encoded map bytes/history/capture plus post-disposal invalidation/scroll/callback counts.

**Step 1: Write all failing UI, routing, and lifetime tests**

Add state/palette/shortcut tests:

- `PublishTerrain_UnavailableFallsBackFromTerrainTool`
- `TerrainPalette_RendersEnabledSetsWithRepresentativeTopologyAndWarning`
- `TerrainPalette_ClickSelectsTerrainAndActivatesToolWithoutEditingMap`
- `TerrainPalette_MissingCorruptOrUndersizedLazySheetUsesPlaceholderAndDiagnosticWithoutDisablingTool`
- `TerrainPalette_DrawsOnlyViewportEntriesInRuntimeOrder`
- `TilesTerrainTabs_SwitchHostedPalettePerDocument`
- `TerrainPaintEraseButtons_AreVisibleAndEnabledExactlyPerTransitionTable`
- `TilesAndTerrainTabs_FollowEveryLockedToolSelectionFallbackTransition`
- `NonTerrainToolbarTools_DoNotSilentlySwitchPaletteMode`
- `Toolbar_OrderIncludesTerrainBetweenEraserAndFloodFill`
- `ToolbarIcon_TerrainToolUsesUniqueIconTerrain`
- `UnmodifiedT_SelectsTerrainPaletteAndToolWhenAvailable`
- adversarial `UnmodifiedT_WhenUnavailablePreservesManualToolAndShowsDiagnostic`
- `UnmodifiedT_ReachesFocusedTextBoxWithoutChangingTool`.

Add real canvas integration tests:

- blocker regression `TerrainToolbarPaletteAndShortcutActivation_ImmediatelyRoutesMapPressThroughDedicatedTerrainApi`
- `TerrainPaint_PressPreviewsNeighborRepairAndReleaseCreatesOneUndoEntry`
- `TerrainErase_SparseDragUsesPartTwoInterpolationAndOneCommand`
- `TerrainEscape_RestoresExactBytesAndHistoryVersion`
- `TerrainCaptureLoss_CancelsWhileManualCaptureLossStillCommits`
- `TerrainGesture_CapturesLayerSelectionModeAndTerrainAtPress`
- `TerrainPaint_SelectedLayers10101ChangesOnlyTopLayerFour`
- `TerrainOutsideThenReentryContinuesFromLastValidCellAndOutsideReleaseCommits`
- `TerrainSelectionModeTabOrToolChangeCancelsBeforeStateChange`
- `CatalogReplacement_CancelsEveryLiveCanvasBeforeNewResolverIsVisible`
- `MapCanvas_DetachReattachRemainsRegisteredAndFunctional`
- `CloseDocumentAndWindow_DisposeViewsAndLaterReplacementNeverCallsThem`.

Do not add an App missing-mask seam. Begin a real preview, trigger controller replacement, assert callbacks see old `Current` and restored bytes/history, then assert all documents are committed before the first publication observer.

Repair and test direct harness ownership in this same red phase:

- `MapCanvas_DisposeIsIdempotentCancelsTerrainAndIgnoresLaterViewModelOrControllerCallbacks`
- `SpritePaletteControl_DisposeIsIdempotentUnbindsScrollbarAndIgnoresLaterCallbacks`
- `TerrainPaletteControl_DisposeIsIdempotentUnbindsScrollbarAndIgnoresLaterCallbacks`.

Make the harness records in `MapCanvasTests`, `SpritePaletteControlTests`, and new `TerrainPaletteControlTests` implement idempotent `IDisposable`; every test uses `using`, and teardown unhosts/disposes controls, closes the window, runs the UI queue, and disposes its controller. Main-window harnesses close the window so its `DocumentView` disposal path runs. The no-callback assertions mutate the view model and publish a later context after disposal and verify invalidation/resolution/scroll counts stay fixed.

**Step 2: Verify the combined boundary is red**

```bash
dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj \
  --filter 'FullyQualifiedName~TerrainPalette|FullyQualifiedName~MapDocumentViewModelTests|FullyQualifiedName~MapCanvasTests|FullyQualifiedName~SpritePaletteControlTests|FullyQualifiedName~MainWindowTests|FullyQualifiedName~MainWindowDefaultLayoutTests|FullyQualifiedName~MainWindowCloseTests|FullyQualifiedName~ShortcutTests|FullyQualifiedName~ToolbarIconTests|FullyQualifiedName~NpcPreviewCanvasTests|FullyQualifiedName~GameDataCanvasTests|FullyQualifiedName~AvaloniaMapDrawSinkTests' -v minimal
```

Expected: compile/test failure because palette mode/control, window regions, Terrain routing, cancellation registration consumption, and disposable control/view contracts do not exist. The blocker regression would otherwise reach normal `BeginStroke(MapEditTool.Terrain, ...)` and throw.

**Step 3: Implement the complete usable slice without an intermediate commit**

Consume Part 2's existing `MapEditTool.Terrain`; do not modify Core. Implement every transition in the locked table. Add Tiles/Terrain tabs above palette-specific controls, Paint/Erase toggles visible in Terrain mode, a Terrain toolbar toggle, and diagnostic status. Keep raw tile controls visible only in Tiles mode.

`TerrainPaletteControl` mirrors the sprite palette's bounded rendering, hit testing, and scrolling. Resolve only each enabled set's representative through the shared current `AssetContext`; never construct another resolver/catalog. Both palettes implement idempotent disposal that first unbinds their scrollbar and then removes all subscriptions.

Add view-model terrain wrappers that obtain the currently committed immutable resolver for begin and call exact Part 2 continuation/completion/cancellation APIs. Centralize result handling: a defensive failed update records terrain ID/mask, clears/releases canvas state, and does not complete/cancel again. Use a gesture enum (`None`, `ManualStroke`, `TerrainStroke`) so release/capture loss cannot confuse policies. Do not re-read terrain/mode/layer during continuation.

Register each canvas exactly once with `RegisterTerrainGestureCancellation`; retain its token across visual detach and dispose it only from final idempotent `MapCanvas.Dispose`. `CancelTerrainInteraction` acts only on a terrain gesture and cancels Core before releasing capture/flags. Disposal cancels any interaction, unregisters, removes every view-model/game-data/size/window subscription, and is safe twice.

Replace the record-like `DocumentView` with an idempotent owner of `MapCanvas`, `SpritePaletteControl`, and `TerrainPaletteControl`. Activation unbinds the outgoing active palette and binds/hosts the incoming document's selected palette. Document removal unhosts both possible palette children and the canvas, disposes the view during the collection callback, then removes it. Window close disposes every remaining view before `_assets.Dispose()` and remains idempotent with harness cleanup.

**Step 4: Verify green and all directly affected regressions**

```bash
dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj \
  --filter 'FullyQualifiedName~TerrainPalette|FullyQualifiedName~MapDocumentViewModelTests|FullyQualifiedName~MapCanvasTests|FullyQualifiedName~SpritePaletteControlTests|FullyQualifiedName~MainWindowTests|FullyQualifiedName~MainWindowDefaultLayoutTests|FullyQualifiedName~MainWindowCloseTests|FullyQualifiedName~ShortcutTests|FullyQualifiedName~ToolbarIconTests|FullyQualifiedName~NpcPreviewCanvasTests|FullyQualifiedName~GameDataCanvasTests|FullyQualifiedName~AvaloniaMapDrawSinkTests' -v minimal
```

Expected: PASS. Clicking/shortcut-selecting Terrain and immediately pressing the map is safe; all pointer/history and direct-disposal regressions pass.

| Invariant | Proved by |
|---|---|
| No exposed Terrain path reaches normal `BeginStroke` | blocker toolbar/palette/shortcut-to-map test |
| Terrain pointer lifecycle is exactly preview/one commit/rollback | paint, erase, Escape, capture-loss, and outside/reentry tests |
| Captured resolver/ID/mode/top layer cannot drift | gesture capture and `0b10101` tests |
| UI transitions are per-document and map/history-neutral | transition-table multi-tab tests |
| Canvas and both direct palette controls release all ownership idempotently | three disposal tests and owned harness teardown |
| Replacement cannot leave an active old resolver | real replacement-during-preview test |
| Manual, game-data, and rendering paths remain unchanged | listed regression classes |

**Step 5: Commit the complete slice**

```bash
git add src/MapEditor.App/ViewModels/AssetPaletteMode.cs \
  src/MapEditor.App/ViewModels/MapDocumentViewModel.cs \
  src/MapEditor.App/Controls/MapCanvas.cs \
  src/MapEditor.App/Controls/SpritePaletteControl.cs \
  src/MapEditor.App/Controls/TerrainPaletteControl.cs \
  src/MapEditor.App/Views/MainWindow.axaml \
  src/MapEditor.App/Views/MainWindow.axaml.cs \
  src/MapEditor.App/Styles/Icons.axaml \
  tests/MapEditor.App.Tests/MapDocumentViewModelTests.cs \
  tests/MapEditor.App.Tests/MapCanvasTests.cs \
  tests/MapEditor.App.Tests/SpritePaletteControlTests.cs \
  tests/MapEditor.App.Tests/TerrainPaletteControlTests.cs \
  tests/MapEditor.App.Tests/MainWindowTests.cs \
  tests/MapEditor.App.Tests/MainWindowDefaultLayoutTests.cs \
  tests/MapEditor.App.Tests/MainWindowCloseTests.cs \
  tests/MapEditor.App.Tests/ShortcutTests.cs \
  tests/MapEditor.App.Tests/ToolbarIconTests.cs \
  tests/MapEditor.App.Tests/NpcPreviewCanvasTests.cs \
  tests/MapEditor.App.Tests/GameDataCanvasTests.cs \
  tests/MapEditor.App.Tests/AvaloniaMapDrawSinkTests.cs
git commit -m "feat: add usable terrain palette and canvas tool"
```

### Task 3: Build manager drafts and validation without publishing

**Files:**
- Create: `src/MapEditor.App/Terrain/TerrainDraftKey.cs`
- Create: `src/MapEditor.App/Terrain/TerrainCatalogDraft.cs`
- Create: `src/MapEditor.App/Terrain/TerrainSetDraft.cs`
- Create: `src/MapEditor.App/Terrain/TerrainMaskDraft.cs`
- Create: `src/MapEditor.App/ViewModels/TerrainSetsManagerViewModel.cs`
- Create: `tests/MapEditor.App.Tests/TerrainCatalogDraftTests.cs`
- Create: `tests/MapEditor.App.Tests/TerrainSetsManagerViewModelTests.cs`

**Mutation impact:**
- Source of truth changed: only manager-owned draft objects cloned from `TerrainAssetLoadResult.Source`; published `AssetContext` and disk remain unchanged.
- Important readers: manager mask grid, provenance/diagnostic panels, enable/status controls, validation summary, and Task 4 save service.
- Derived/cached state affected: required mask rows, representative previews, issue lists, `CanEnable`, and `CanSave`; recompute after each draft mutation from a newly built immutable catalog plus current manifest.
- Required propagation sequence: clone every source-array entry under an immutable key → stage one edit → derive effective members/generated authored ID → reject collision or atomically replace that key's value → rebuild required/orphan rows → build copied catalog → run shared Core/frame validation → notify draft readers only.
- Invariants to preserve: blank/duplicate/mismatched authored IDs remain representable and selectable; source settings, fingerprint, generator version, metrics, provenance, diagnostics, and variant order round-trip unless explicitly edited; orphans are explicit and block save; rejected edits preserve full draft/selection/issues/dirty state; pending/disabled may retain incomplete/conflicting/unresolved review data.
- Observable proof required: compare complete built catalog values and prove current runtime/file bytes are unchanged after edits and rejected enable.

**Step 1: Write failing tests**

Add:

- `CloneBuild_RoundTripsGeneratedMetadataProvenanceAndDiagnostics`
- `Clone_BlankDuplicateAndMismatchedIdsRemainDistinctBySourceOrderKey`
- `RegenerateId_RepairsBlankMismatchAndDifferingDuplicateIds`
- `RegenerateId_TrueIdentityCollisionReturnsExactPartOneIssueWithoutMutation`
- `RenameStatusAndVariantReorder_DoNotChangeId`
- `ChangeTopology_RebuildsRowsRecomputesAuthoredIdAndRetainsSelectionKey`
- `AddNewMemberAndRemoveFinalImageOnlyUse_RecomputeIdWhileUnusedMapObservedMemberDoesNot`
- `AddRemoveReorderVariant_PreservesExactUserOrderAndSynchronizesMemberProvenance`
- adversarial `IdentityChangingEdit_CollidingWithExistingSetRejectsWithoutAnyDraftOrSelectionMutation`
- `MaskRows_ExposeVisualNeighborhoodAndResolvedFrameState`
- `TrySetEnabled_CompleteUniqueResolvedSetSucceeds`
- `TrySetEnabled_MissingMaskFrameOrConflictReturnsExactIssuesAndKeepsStatus`
- `SetPendingOrDisabled_AllowsIncompleteConflictingReviewData`
- `RemoveOneOrAllOrphansThenSerializeParseValidateRoundTripsExactly`
- `EditThenRevertIsCleanAndRejectedEditNeverDirties`
- `PublishedIdRekeysTrackOnlyUniqueOriginallyEnabledIds`
- adversarial `DraftMutations_DoNotChangePublishedResolverFileOrMapHistory`.

A topology change retains variants for masks reachable under the new topology, creates empty rows for newly required masks, and retains now-unreachable mappings in a draft-only orphan list shown as validation errors until removed; it must never silently discard user edits. Its ID nevertheless changes immediately because topology is identity-defining.

**Step 2: Verify red**

```bash
dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj \
  --filter 'FullyQualifiedName~TerrainCatalogDraft|FullyQualifiedName~TerrainSetsManagerViewModel' -v minimal
```

Expected: compile failure because draft/manager types do not exist.

**Step 3: Implement isolated draft editing**

Expose explicit operations rather than mutable lists:

```csharp
internal sealed class TerrainCatalogDraft
{
    internal TerrainCatalogDraft(TerrainCatalog source, SpriteManifest manifest);
}

internal sealed class TerrainSetsManagerViewModel
{
    internal TerrainSetsManagerViewModel(TerrainCatalogDraft draft);
    public TerrainDraftKey? SelectedSetKey { get; set; }
}

internal sealed record TerrainDraftMutationResult
{
    public bool Succeeded { get; }
    public TerrainDraftKey Key { get; }
    public IReadOnlyList<TerrainValidationIssue> Issues { get; }
}

void Rename(TerrainDraftKey key, string displayName);
TerrainDraftMutationResult RegenerateId(TerrainDraftKey key);
TerrainDraftMutationResult ChangeTopology(TerrainDraftKey key, TerrainTopology topology);
TerrainDraftMutationResult AddVariant(TerrainDraftKey key, int mask, TerrainGraphicReference reference);
TerrainDraftMutationResult RemoveVariant(TerrainDraftKey key, int mask, int index);
void MoveVariant(TerrainDraftKey key, int mask, int fromIndex, int toIndex);
TerrainDraftMutationResult RemoveOrphanMask(TerrainDraftKey key, int mask);
TerrainDraftMutationResult RemoveAllOrphanMasks(TerrainDraftKey key);
bool TrySetStatus(TerrainDraftKey key, TerrainReviewStatus status, out IReadOnlyList<TerrainValidationIssue> issues);
TerrainCatalog Build();
IReadOnlyDictionary<string, string> BuildPublishedIdRekeys();
```

`TerrainDraftMutationResult` copies issues. Each operation validates key/indices/enums before mutation. Identity-changing methods stage complete rows/members and authored ID, validate a temporary catalog for exact shared collision issues, then atomically replace only the value under the immutable key. `TerrainSetsManagerViewModel.SelectedSetKey` never changes as a side effect. Adding an absent reference adds `ImageOnly`; removing its final use removes that member, while an unused map-observed member remains with provenance/issue. Reordering never changes membership. Topology orphans remain draft-only until explicit removal. `TrySetStatus(...Enabled...)` validates a temporary catalog and commits only when runtime-valid. `Build` constructs copied Part 1 records even while repairable semantic issues remain; `CanSave` alone requires issue-free dirty state. Keep all work synchronous on the UI thread.

**Step 4: Verify green**

```bash
dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj \
  --filter 'FullyQualifiedName~TerrainCatalogDraft|FullyQualifiedName~TerrainSetsManagerViewModel' -v minimal
```

Expected: PASS; draft mutations remain isolated and every accepted/rejected identity transition has the exact final catalog/dirty state.

| Invariant | Proved by |
|---|---|
| Source metadata and malformed identities remain independently repairable | clone/build and stable-key tests |
| Identity-changing collisions are all-or-nothing | adversarial collision snapshot test |
| Topology changes never silently discard orphan mappings | topology/orphan round-trip tests |
| Enablement requires complete conflict-free resolved data | exact enable issue tests |
| Draft edits cannot leak to disk/runtime/map/history | adversarial isolation test |

**Step 5: Commit**

```bash
git add src/MapEditor.App/Terrain/TerrainDraftKey.cs \
  src/MapEditor.App/Terrain/TerrainCatalogDraft.cs \
  src/MapEditor.App/Terrain/TerrainSetDraft.cs \
  src/MapEditor.App/Terrain/TerrainMaskDraft.cs \
  src/MapEditor.App/ViewModels/TerrainSetsManagerViewModel.cs \
  tests/MapEditor.App.Tests/TerrainCatalogDraftTests.cs \
  tests/MapEditor.App.Tests/TerrainSetsManagerViewModelTests.cs
git commit -m "feat: add terrain set manager drafts"
```

### Task 4: Atomically save, publish, and expose the Terrain Sets manager

**Files:**
- Create: `src/MapEditor.App/Terrain/ITerrainCatalogFileOperations.cs`
- Create: `src/MapEditor.App/Terrain/TerrainCatalogFileStore.cs`
- Create: `src/MapEditor.App/Terrain/TerrainCatalogSaveResult.cs`
- Create: `src/MapEditor.App/Terrain/TerrainCatalogManager.cs`
- Create: `src/MapEditor.App/Controls/TerrainDraftPreviewControl.cs`
- Create: `src/MapEditor.App/Dialogs/TerrainSetsDialog.axaml`
- Create: `src/MapEditor.App/Dialogs/TerrainSetsDialog.axaml.cs`
- Modify: `src/MapEditor.App/Dialogs/IEditorDialogs.cs:29-58`
- Modify: `src/MapEditor.App/Dialogs/AvaloniaEditorDialogs.cs:19-115`
- Modify: `src/MapEditor.App/Dialogs/EditorDialogsProxy.cs`
- Modify: `tests/MapEditor.App.Tests/Fakes/FakeEditorDialogs.cs:61-224`
- Modify: `src/MapEditor.App/Rendering/TerrainReplacement.cs`
- Modify: `src/MapEditor.App/Rendering/AssetContextController.cs:10-104`
- Modify: `src/MapEditor.App/ViewModels/TerrainSetsManagerViewModel.cs`
- Modify: `src/MapEditor.App/Views/MainWindow.axaml`
- Modify: `src/MapEditor.App/Views/MainWindow.axaml.cs`
- Create: `tests/MapEditor.App.Tests/TerrainCatalogFileStoreTests.cs`
- Create: `tests/MapEditor.App.Tests/TerrainCatalogManagerTests.cs`
- Create: `tests/MapEditor.App.Tests/TerrainSetsDialogTests.cs`
- Modify: `tests/MapEditor.App.Tests/MainWindowTests.cs`
- Modify: `tests/MapEditor.App.Tests/MainWindowCloseTests.cs`
- Modify: `tests/MapEditor.App.Tests/AppStartupTests.cs`

**Mutation impact:**
- Source of truth changed: `<captured source-context asset directory>/terrain-brushes.json`, followed by that exact still-current `AssetContext.Terrain` as the in-process publication.
- Important readers: all terrain palettes/tools/gestures, manager reopening, current manifest/cache, and open documents. Map contents/history are not manager readers and must remain unchanged.
- Derived/cached state affected: current runtime resolver/enabled ordering and every document's reconciled selection; sprite cache, renderer, tint cache, sheets, and appearance catalog must retain identity.
- Required propagation sequence:
  1. Enter the manager save guard; capture and recheck the exact source `AssetContext`, normalized directory, and manifest identity.
  2. Build/validate the immutable catalog, ID map, runtime result, canonical bytes, document plans, and opaque manager-baseline publication plan in memory.
  3. Ask the controller to reserve and invoke every cancellation registration in order; aggregate failures before I/O.
  4. While still reserved, create one unique sibling temp, write, flush durably, dispose, and atomically move/replace destination.
  5. Consume the same reserved plan to commit context terrain, every document, and the prepared draft baseline without callbacks.
  6. Still reserved, notify every document/manager observer safely and collect warnings; release only after the last observer.
  7. In `finally`, call nonthrowing abandonment (a no-op after publication), leave the save guard, and return deterministic status; the dialog decides close/retry/discard.
- Publication boundary: steps 1-3 cannot change the file or published catalog; the reservation before cancellation denies stale continuation and nested open/save/disposal before irreversible step 4. Every pre-publication exit after reservation abandons. After destination replacement, commit is preplanned/nonthrowing and no callback occurs until disk/context/all documents/manager baseline agree. Notification warnings never roll back.
- Invariants to preserve: invalid drafts/cancellation failures do not write; create/write/flush/dispose/replace/move failure preserves old bytes/runtime and releases reservation, though a preview may remain safely cancelled; after destination replacement runtime cannot remain old; nested file fakes/observers cannot reorder contexts or writes; success does not replace/dispose sprite assets; manager warns that converter regeneration overwrites edits.
- Observable proof required: injected failures/reentry compare exact destination bytes, current-context/directory/runtime identity, sprite-cache identity, every document selection, map bytes, history versions, manager baseline/dirty state, active gesture state, reservation release, and warning presentation.

**Step 1: Write failing store/manager tests**

Define production/fake interface methods exactly:

```csharp
bool Exists(string path);
Stream CreateFile(string path);
void FlushToDisk(Stream stream);
void Replace(string sourcePath, string destinationPath);
void Move(string sourcePath, string destinationPath);
void Delete(string path);
```

Add:

- `Store_NullBlankArgumentsUseExactParameterNames`
- `Write_UsesUniqueSiblingCreateNewUtf8NoBomAndFlushTrueBeforeDisposeAndReplacement`
- theory `Write_CreateWriteFlushDisposeReplaceOrMoveFailurePreservesPriorBytesAndDeletesOwnTemp`
- `Write_PreexistingStaleTempsRemainUntouchedOnSuccessAndFailure`
- adversarial `Write_CleanupFailureRethrowsPrimaryInstanceAndRecordsCleanupException`
- `ManagerConstructor_CapturesExactCurrentContextDirectoryManifestAndSource`
- `Save_CleanReturnsNoChangesWithoutValidationCancellationOrIo`
- `Save_InvalidDraftChangesNeitherFileNorRuntimeAndDoesNotCancel`
- `Save_SamePathContextReplacementReturnsSourceContextChangedAndStaysDirty`
- `Save_ThreeCallbacksWithMiddleFailureAttemptsAllBeforeWriteAndPreservesExternalState`
- adversarial `Save_CancellationCallbackNestedTryOpenSaveAndControllerDisposeAreRejectedBeforeNestedMutation`
- `Save_SuccessCancelsBeforeFirstFileOperationThenPublishesCanonicalBytes`
- adversarial `Save_FileOperationFakeNestedTryOpenSaveAndDisposeAreRejectedAndOuterWriteWins`
- `Save_SuccessPublishesIntoExistingContextWithoutReplacingCacheRendererOrTint`
- theory `Save_CreateWriteFlushDisposeReplaceMoveFailurePreservesFileRuntimeSelectionMapHistoryDirtyDraftAndReleasesReservation`
- `Save_FailedWriteThenRetryOrAssetOpenSucceeds`
- `Save_RekeyedMiddleSelectionFollowsMappedIdAcrossAtLeastThreeEnabledSets`
- `Save_RekeyTargetDisabledUsesOrdinaryFallback`
- `Save_ValidZeroEnabledCatalogPublishesAndFallsBackTool`
- adversarial `Save_FirstDocumentFirstObserverNestedTryOpenAndSaveAreRejectedWhileAllLaterObserversCommit`
- `Save_ObserverFailuresReturnSucceededWithOrderedWarningsAndDiskRuntimeEquality`
- `Save_PublishedIdRekeysResetOnlyAfterSuccessfulPublication`
- `Save_SuccessMarksCleanAndSecondSaveReturnsNoChanges`.

The fake implements only the declared file-operation methods and records their order. Production `FlushToDisk` requires its `FileStream` and calls `Flush(flushToDisk:true)` before close/replacement; fakes inject write/flush/dispose failures without phantom async APIs.

**Step 2: Verify red**

```bash
dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj \
  --filter 'FullyQualifiedName~TerrainCatalogFileStore|FullyQualifiedName~TerrainCatalogManager' -v minimal
```

Expected: compile failure because store/manager publication APIs do not exist.

**Step 3: Implement atomic store and terrain-only publication**

Implement the exact audited file-store and manager constructors/results. The store creates a unique sibling temp and never deletes the destination first or sweeps stale temps. It writes UTF-8 without BOM, calls injected `FlushToDisk`, disposes the stream, and only then moves/replaces. On failure, best-effort delete only its own temp and rethrow the original instance/stack with cleanup attached.

The manager builds/validates/serializes/plans before cancellation and returns the exact status table. Add this opaque Task 4 surface:

```csharp
internal sealed class TerrainManagerPublicationPlan { }

internal TerrainManagerPublicationPlan PlanAcceptSavedCatalog();

internal TerrainReplacementPreparation PrepareTerrainReplacement(
    AssetContext expectedContext,
    TerrainAssetLoadResult replacement,
    IReadOnlyDictionary<string, string>? idRekeys,
    TerrainManagerPublicationPlan managerPublication);

internal TerrainPublicationResult PublishTerrain(
    TerrainReplacementPlan plan,
    TerrainManagerPublicationPlan managerPublication);
```

`PlanAcceptSavedCatalog` is on `TerrainSetsManagerViewModel`; call it during preflight and pass it into the manager-specific preparation overload so the controller binds that exact manager object into the reserved replacement plan. The manager plan contains copied baseline values and planned property names, performs only no-callback baseline commit plus safe per-subscriber manager notification, is bound to that manager instance, and is one-use. It cannot perform I/O/validation/resolver construction or invoke arbitrary callbacks. The controller rejects a null, mismatched, or reused manager publication plan before commit just as it rejects an invalid replacement plan.

Set the manager save guard before any operation that can reenter. Map its own/controller busy result to `ReplacementInProgress`. After successful preparation, wrap store/publication in `try/finally` and always call `AbandonTerrainReplacement(plan)` in `finally`; publication makes that call a harmless reuse no-op. Convert expected store exceptions into `WriteFailed` but do not catch fatal failures such as `OutOfMemoryException`. The controller accepts only the active owned plan, commits context/documents/manager baseline, safely notifies them all, and releases reservation last. It performs no I/O, validation, resolver construction, or sprite-context reconstruction/disposal after durable replacement.

**Step 4: Write failing manager UI tests**

Add `Task<TerrainCatalogSaveResult?> ShowTerrainSetsAsync(TerrainCatalogManager manager)` to the async real interface/proxy/fake. Add headless tests:

- `Dialog_ShowsAllStatusesDiagnosticsProvenanceAndRegenerationWarning`
- `Dialog_SelectingTopologyShowsEveryRequiredVisualMask`
- `Dialog_AddRemoveAndMoveVariantUpdatesPreviewAndValidation`
- `Dialog_InvalidEnableShowsExactFailuresAndStaysOpen`
- `Dialog_SaveFailureShowsErrorAndPreservesDraftForRetry`
- `Dialog_DirtyTitleCloseSaveDiscardCancelUsesSameSavePath`
- `Dialog_AllExactNamedControlsAreBoundAndScrollable`
- `Dialog_LazyMissingOrCorruptPreviewUsesPlaceholderAndDiagnostic`
- `MainWindow_TerrainSetsCommandUsesExactCapturedManagerSource`
- `MainWindow_TerrainSetsCommandEnablementCoversInitialMissingMalformedParsedInvalidAndZeroEnabled`
- `MainWindow_PendingManagerDialogDisablesCommandAndRefusesTabOrWindowClose`
- `MainWindow_TerrainManagerWarningsUseExactTitleMessageAndPublicationOrder`
- adversarial `MainWindow_TerrainManagerFirstShowInfoFailureStillAttemptsLaterWarningsAndPublishedStateStaysUnchanged`.

**Step 4a: Verify the UI surface is red**

```bash
dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj \
  --filter 'FullyQualifiedName~TerrainSetsDialogTests|FullyQualifiedName~MainWindowTests|FullyQualifiedName~MainWindowCloseTests|FullyQualifiedName~AppStartupTests' -v minimal
```

Expected: compile/XAML failures name the absent preview/dialog, interface member, named controls, and command.

**Step 5: Implement the focused dialog and command**

Add `Terrain Sets…` under Edit with exact audited command enablement. Implement all locked named controls, stable-key selection, explicit ID regeneration/orphan removal, bounded scrolling, async dirty close, and lazy preview behavior. Reuse existing sprite resolution; do not add inference, drag/drop, bulk auto-fix, or settings persistence.

`AvaloniaEditorDialogs.ShowTerrainSetsAsync` opens `TerrainSetsDialog`; proxy/fake mirror the Task-based signature and the fake records the exact manager. The dialog calls only `manager.Save()` on Save. Failure focuses validation/status and preserves the same draft for retry; success/no-change closes; dirty title close uses Save/Discard/Cancel. After a succeeded dialog result, MainWindow presents each result warning sequentially as `ShowInfoAsync("Terrain Sets warning", $"{warning.Scope}: {warning.Message}")`; it catches each nonfatal presentation failure and continues without changing the already published state.

**Step 6: Verify green and commit**

```bash
dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj \
  --filter 'FullyQualifiedName~TerrainCatalogFileStore|FullyQualifiedName~TerrainCatalogManager|FullyQualifiedName~TerrainSetsDialog|FullyQualifiedName~AssetContextControllerTests|FullyQualifiedName~MainWindowTests|FullyQualifiedName~MainWindowCloseTests|FullyQualifiedName~AppStartupTests' -v minimal
```

Expected: PASS, including cancellation/file-fake/observer reentry, every store failure, reservation release, exact warning presentation, and dialog retry/close behavior.

| Invariant | Proved by |
|---|---|
| Exact source context/directory/manifest remains authoritative | constructor and same-path replacement tests |
| Reservation spans cancellation, durable I/O, all commits, and all notifications | callback/file-fake/observer reentry tests |
| Every failed write preserves old disk/runtime/documents and releases reservation | failure theory plus retry/open test |
| Durable replacement cannot leave old runtime or dirty baseline published | success ordering and disk/runtime equality tests |
| Rekeys/fallback affect selections only after publication | middle-selection, disabled-target, zero-enabled tests |
| Manager and asset warning dialogs are ordered, failure-tolerant presentation only | MainWindow warning tests |

```bash
git add src/MapEditor.App/Terrain \
  src/MapEditor.App/Controls/TerrainDraftPreviewControl.cs \
  src/MapEditor.App/Dialogs/TerrainSetsDialog.axaml \
  src/MapEditor.App/Dialogs/TerrainSetsDialog.axaml.cs \
  src/MapEditor.App/Dialogs/IEditorDialogs.cs \
  src/MapEditor.App/Dialogs/AvaloniaEditorDialogs.cs \
  src/MapEditor.App/Dialogs/EditorDialogsProxy.cs \
  src/MapEditor.App/Rendering/TerrainReplacement.cs \
  src/MapEditor.App/Rendering/AssetContextController.cs \
  src/MapEditor.App/ViewModels/TerrainSetsManagerViewModel.cs \
  src/MapEditor.App/Views/MainWindow.axaml \
  src/MapEditor.App/Views/MainWindow.axaml.cs \
  tests/MapEditor.App.Tests/Fakes/FakeEditorDialogs.cs \
  tests/MapEditor.App.Tests/TerrainCatalogFileStoreTests.cs \
  tests/MapEditor.App.Tests/TerrainCatalogManagerTests.cs \
  tests/MapEditor.App.Tests/TerrainSetsDialogTests.cs \
  tests/MapEditor.App.Tests/MainWindowTests.cs \
  tests/MapEditor.App.Tests/MainWindowCloseTests.cs \
  tests/MapEditor.App.Tests/AppStartupTests.cs
git commit -m "feat: manage and publish terrain sets"
```

### Task 5: End-to-end editor proof, full gates, and real-corpus smoke

**Files:**
- Create: `tests/MapEditor.App.Tests/TerrainEditorEndToEndTests.cs`
- Modify only when a test exposes a defect: files from Tasks 0-4
- Modify: `docs/plans/2026-09-08-terrain-brush-part3-editor.md` only to record any verified environment limitation; do not record generated counts as contractual snapshots

**Mutation impact:**
- Source of truth changed: none intentionally; this task proves the full path from generated-format JSON through UI, map history, manager durability, and republished runtime.
- Important readers: Rendering loader, asset context, documents, both palettes, canvas, manager, map codec/history, and reopened editor context.
- Derived/cached state affected: all runtime state from prior tasks is exercised; no new cache is planned.
- Required propagation sequence under test: fixture catalog file → load/validate → select Terrain → pointer edit/undo/redo → manager draft/build → gesture cancellation → durable save → nonthrowing publication → document reconciliation → reload from disk.
- Invariants to preserve: maps contain no terrain metadata; ordinary Tiles/Pencil/Eraser still work after terrain failure and manager save; no generated `Assets/` output is committed.
- Observable proof required: one real headless window test asserts final file bytes, runtime IDs, map layers, history transitions, and reopened context behavior.

**Step 1: Write the failing end-to-end tests**

Add:

- `LoadSelectPaintUndoManageSaveReload_EndToEnd`
- `MalformedTerrain_ManualTileEditingAndMapSaveStillWork_EndToEnd`
- adversarial `SaveReplacementDuringPreview_CancelsThenNextGestureUsesOnlyNewCatalog`.

The first test must use a real temp asset directory, real manifest/PNG, canonical Part 1 JSON, real `MainWindow`, real pointer input, real file store, and `MapCodec`; mock only user dialog choices. After manager save, parse disk bytes with `TerrainCatalogJson.Parse`, reopen a fresh context, and paint with the changed variants.

**Step 2: Verify red, then fix only integration defects**

```bash
dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj \
  --filter 'FullyQualifiedName~TerrainEditorEndToEndTests' -v minimal
```

Expected: these tests exercise already completed Task 0-4 surfaces and may pass immediately. Do not manufacture a failure. If a test exposes a verified integration defect, make the smallest production correction, rerun focused tests, then run every full gate; otherwise proceed directly to the gates.

**Step 3: Run all automated gates**

```bash
dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj -v minimal
dotnet test tests/MapEditor.Rendering.Tests/MapEditor.Rendering.Tests.csproj -v minimal
dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj -v minimal
dotnet build Goose2ClientGodot.sln -v minimal
git status --short
```

Expected: all tests/build pass; status contains only intended source/tests/this plan and no generated `Assets/` files.

| Invariant | Proved by |
|---|---|
| Generated-format JSON reaches real pointer editing and ordinary undo/redo | load/select/paint end-to-end test |
| Manager disk/runtime publication survives reload with the same IDs/variants | manage/save/reload end-to-end test |
| Malformed terrain leaves manual tile editing and map persistence usable | degradation end-to-end test |
| Replacement during preview rolls back old gesture and next gesture uses only new runtime | adversarial replacement test |
| All project-level regressions remain green | three full suites and solution build |

**Step 4: Run real-corpus smoke without committing output**

On a machine where Part 1's configured source corpora are available:

```bash
dotnet run --project tools/AssetConverter/src/AssetConverter -- all "$PWD"
dotnet run --project src/MapEditor.App/MapEditor.App.csproj
```

Manual checklist: load `Assets/Sprites`; inspect all manager statuses/diagnostics; paint and erase grass, water, path, and shoreline on layer 0 and a higher layer; verify drag previews, one-step undo/redo, Escape/capture-loss rollback, save-during-preview cancellation, restart persistence, and subsequent Pencil/Eraser map save/reopen. If the full corpus is unavailable, use Part 1's temporary synthetic/Illutia-only root. Never weaken validation merely to produce enabled sets.

**Step 5: Red-team review**

- Confirm UI-thread-only mutation/publication; no lock, dispatch wait, worker, or file watcher.
- Confirm open reserves before its loader; direct replacement reserves before cancellation; manager keeps that reservation through file I/O, all commits, and all notifications.
- Exercise loader-, cancellation-, file-operation-, and observer-triggered nested open/save/controller disposal; verify exact busy outcomes, outer-operation identity/directory authority, and no callback/state mutation by the nested operation.
- Confirm every failure after reservation uses nonthrowing abandonment, failed writes permit retry, and foreign/reused plans neither publish nor release another operation.
- Confirm both open and manager save cancel terrain before resolver replacement, and old caches are disposed only afterward while still reserved.
- Confirm validation/serialization and `FileStream.Flush(true)` precede atomic replacement; destination is never deleted first and cleanup failure cannot mask the primary exception.
- Exercise malformed/unsupported JSON, bad frames/membership, invalid enable, every I/O failure, callback failure, Escape, capture loss, and mid-preview replacement.
- Verify asset-open and manager warning titles/messages/order, continuation after one `ShowInfoAsync` failure, and unchanged already-published state.
- Search for per-document terrain state outside `MapDocumentViewModel`, cached stale resolvers, draft leakage, and manager changes to map/history/cache identity.
- Confirm the combined Task 2 commit cannot activate Terrain without dedicated canvas routing, and direct canvas/sprite-palette/terrain-palette harnesses close, unbind, dispose twice safely, and receive no later callback.
- Confirm fakes mirror production interfaces, masks come from `TerrainMasks.Required`, and end-to-end tests use real files/session/pointer capture.
- Reject inference, terrain rendering, transitions, fallback masks, map metadata, override files, and generated asset commits.

**Step 6: Commit only verified implementation/test corrections**

If Task 5 adds the end-to-end test or fixes a verified defect, stage only those exact files and commit them. Do not stage this plan merely because it exists; include it only if the real-corpus step added a verified environment note. If no correction or note was needed, this verification task has no commit.

```bash
git add <exact verified test or correction files>
git commit -m "test: verify terrain editor workflow"
```

Plan complete and saved to `docs/plans/2026-09-08-terrain-brush-part3-editor.md`. Ready to implement.
