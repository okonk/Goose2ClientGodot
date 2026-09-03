# Portable Map Editor — Part 2 of 4: Editing Domain Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add the app-neutral editing domain to `MapEditor.Core`: layer-specific pencil and eraser behavior, blocked-bit toggling, eyedropper selection, gap-free drag interpolation, one-command stroke grouping, deterministic undo/redo, a configurable retained-history accounting cap, bounded peak storage for a maximum stroke, and savepoint-based dirty state.

**Architecture:** `MapEditSession` owns one Part 1 `MapDocument`, editor-only active-layer/brush state, one optional in-progress stroke, undo/redo history, and dirty-state identity. A stroke mutates the document eagerly through the exact Part 1 `SetLayer`/`SetFlags` APIs so a later canvas can repaint during a drag, while collecting each affected cell's before/after value. Editing strokes suppress duplicate tiles with a one-bit-per-tile bitmap and append deltas into bounded segmented storage. Completing a stroke transfers that same storage into at most one command without copying every delta; canceling reverse-replays the same storage and leaves history unchanged. Integer Bresenham interpolation fills every segment between valid sampled cells. History applies a deterministic cap only to commands retained across undo and redo; it is not a hard cap on the document, active stroke, session, or total managed heap. Paths and `MapFileRevision` remain outside the session: the later app creates a clean session only after `MapFileStore.Open` succeeds and calls `MarkSaved` only after `MapFileStore.Save` returns successfully.

**Series:** Part 2 of 4.
- **Part 1:** shared core/model, codec, file storage, external-change detection, and Godot migration, as specified by `docs/plans/2026-09-01-portable-map-editor-part1-core-codec.md`.
- **Part 2 (this plan):** editing semantics, interpolation, grouped commands, undo/redo, retained-history limits, bounded stroke storage, dirty state, and core tests.
- **Part 3:** rendering and assets.
- **Part 4:** Avalonia application integration, dialogs/settings/shortcuts, and local build/publish implementation.

**Tech Stack:** C# / .NET 8 in the Part 1 `MapEditor.Core` and `MapEditor.Core.Tests` projects; xUnit 2.9.2. No new package, project, or framework dependency.

**Planning baseline verified 2026-09-01:** This checkout is still before Part 1 implementation: `src/MapEditor.Core` does not exist, and the Part 1 plan is currently untracked. On the current tree, `dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj -v minimal` passes 457 tests and `dotnet build Goose2ClientGodot.csproj -v minimal` succeeds with 324 existing nullable warnings and no errors. Execute Part 1 first. After its planned migration, use the Part 1 expectation of 455 remaining client tests plus the new core tests, but require zero failures rather than relying on a count. Part 2 must introduce no new build warnings.

**Design sources:** `docs/plans/2026-09-01-portable-map-editor-design.md` and `docs/plans/2026-09-01-portable-map-editor-part1-core-codec.md`.

---

## Prerequisite and scope boundaries

Part 1 must be complete and green before this plan starts. In particular, these exact types and methods must already exist in namespace `MapEditor.Core`:

```csharp
public readonly record struct MapTileLayer(int Sheet, int Graphic);

public readonly struct MapTile
{
    public int Flags { get; }
    public bool IsBlocked { get; }
    public bool IsRoof { get; }
    public MapTileLayer GetLayer(int layerIndex);
}

public sealed class MapDocument
{
    public const int LayerCount = 5;
    public const int BlockedFlag = 2;

    public int Width { get; }
    public int Height { get; }
    public int TileCount { get; }
    public MapTile this[int x, int y] { get; }
    public MapTile GetTile(int rowMajorIndex);

    public static MapDocument Create(int width = DefaultWidth, int height = DefaultHeight);
    public void SetFlags(int x, int y, int flags);
    public void SetLayer(int x, int y, int layerIndex, MapTileLayer layer);
}

public sealed class MapFileStore
{
    public OpenedMap Open(string path);
    public MapFileRevision Save(
        string path,
        MapDocument document,
        MapFileRevision? expectedRevision = null);
}
```

Do not rename, overload around, replace, or widen these Part 1 APIs. Editing commands must use value reads from `MapDocument` and the coordinate-checked `SetFlags`/`SetLayer` mutation methods. Do not add bulk mutation, cloning, backing-array access, events, or mutable tile/layer properties to make Part 2 easier.

In scope:

- App-neutral editing/session state in `MapEditor.Core`.
- Active layer and selected `(sheet, graphic)` brush values.
- Pencil, eraser, blocked-toggle, and eyedropper semantics.
- Inclusive all-octant integer line interpolation between pointer samples.
- Eager stroke application, duplicate-cell suppression, cancellation, and one-command completion.
- Before/after delta commands and deterministic undo/redo.
- Configurable retained-history accounting cap and eviction; bounded transient storage for a maximum 1000×1000 stroke.
- Savepoint-based dirty transitions for opened, new, edited, undone, redone, branched, saved, canceled, no-op, and evicted states.
- Unit and Part 1 storage-boundary tests.
- Full core/client/build regression gates.

Out of scope:

- `MapEditor.Rendering`, viewport/canvas math, draw operations, culling, images, manifests, sprite caches, placeholders, or any asset path.
- `MapEditor.App`, Avalonia references, controls, pointer event adapters, keyboard shortcuts, menus, prompts, view models, file dialogs, settings persistence, or active-window state.
- Paths, `MapFileRevision`, open/save orchestration, conflict dialogs, or destination replacement inside `MapEditSession`.
- Godot source changes; Part 1's migrated client continues to use `MapDocument` directly and does not use editor history.
- Rectangle/flood fill, selection, clipboard, map resize, autosave, crash recovery, file watching, merge, macro/scripting command APIs, or command serialization.
- Rendering/assets/Avalonia project scaffolding, publishing, build scripts, CI, or packaging.

`MapEditSession` is one document's app-neutral editing lifetime, not a mutable application-wide “active document.” New/open replacement resets history by constructing a new session only after creation/open succeeds. The later app owns path/revision beside that session and decides whether to prompt from `IsDirty`.

---

## APIs and facts verified in the current repository and Part 1 plan

| API / fact | Citation and consequence |
|---|---|
| Edit commands and undo/redo belong in engine-independent `MapEditor.Core`; app controls belong elsewhere | `docs/plans/2026-09-01-portable-map-editor-design.md:13-23`. Keep every Part 2 production file free of Godot, Avalonia, rendering, and asset types. |
| The model is row-major, has exactly five layers, and retains the complete integer flags | `docs/plans/2026-09-01-portable-map-editor-design.md:25-34`. Commands address one `(x,y,layer)` or one `(x,y)` flags field and do not copy whole documents. |
| Unknown flag bits must survive | `docs/plans/2026-09-01-portable-map-editor-design.md:42-46`; Part 1 model constraints at `docs/plans/2026-09-01-portable-map-editor-part1-core-codec.md:118-125`. Blocked toggle must use XOR with only `MapDocument.BlockedFlag`. |
| New/open resets history, and a new document has no path | `docs/plans/2026-09-01-portable-map-editor-design.md:48-54`. Construct a new session rather than adding document replacement/path state. |
| Initial tools are pencil, eraser, eyedropper, and blocked toggle | `docs/plans/2026-09-01-portable-map-editor-design.md:73-90`. These are the only tools added. |
| Paint touches only active layer; erase clears only that layer; blocked changes only the known bit | `docs/plans/2026-09-01-portable-map-editor-design.md:90-90`. Tests must seed flags, all five layers, and neighbors before every narrow-write assertion. |
| Drags interpolate, group into one command, retain before/after values, and use a configurable memory cap | `docs/plans/2026-09-01-portable-map-editor-design.md:92-92`. Interpret that setting as a deterministic retained undo+redo history budget, not a total editor-memory ceiling. The session applies every interpolated tile once per captured tool/layer and history retains typed delta storage, not snapshots. |
| Dirty state drives discard prompts and must coexist with save/undo/redo | `docs/plans/2026-09-01-portable-map-editor-design.md:94-94`. Core exposes state only; prompts and shortcuts wait for Part 4. |
| Required automated coverage explicitly includes edit isolation, unknown-bit preservation, interpolation/grouping, history limits, and dirty transitions | `docs/plans/2026-09-01-portable-map-editor-design.md:106-123`. Each appears in the task and consolidated matrices below. |
| Failed save must preserve dirty state | `docs/plans/2026-09-01-portable-map-editor-design.md:98-104`. `MapFileStore.Save` cannot clear session state; only a later explicit `MarkSaved` can. |
| Part 1 intentionally leaves editing to this part | `docs/plans/2026-09-01-portable-map-editor-part1-core-codec.md:21-43`. This plan introduces history/session state without moving path or active-app ownership into the model. |
| Part 1 locks value reads and narrow coordinate-checked writes | `docs/plans/2026-09-01-portable-map-editor-part1-core-codec.md:80-128`. Commands must not mutate an indexer-returned struct copy. |
| Part 1 retains every signed `Graphic`/`Flags`, keeps `Sheet` as `int`, and validates sheet wire range only during encode | `docs/plans/2026-09-01-portable-map-editor-part1-core-codec.md:118-125,159-167`. Pencil and eyedropper preserve exact `MapTileLayer` values; they do not clamp, clear, or asset-validate. |
| Part 1 `MapFileStore.Save` returns only after replacement succeeds | `docs/plans/2026-09-01-portable-map-editor-part1-core-codec.md:171-206`. The app sequence is `Save` return, update path/revision, then `MarkSaved`; exceptions skip all three updates. |
| Part 1 adds `InternalsVisibleTo("MapEditor.Core.Tests")` | `docs/plans/2026-09-01-portable-map-editor-part1-core-codec.md:194-204`. Test internal interpolation/accounting helpers directly without making them public. |
| The current legacy blocked bit is `2` | `Scripts/MapFile.cs:26-33`; current derivation test at `tests/Goose2Client.Tests/MapFileTests.cs:41-51`; Part 1 locks `MapDocument.BlockedFlag = 2`. Never use a Boolean flags replacement. |
| Current runtime updates demonstrate flags and layers are independent mutation fields | `Scripts/MapManager.cs:322-340`; Part 1 migration replaces those writes with `SetFlags`/`SetLayer`. Editor commands use the same narrow model interface but do not change runtime packet behavior. |
| Part 1 creates core/core-test projects and adds them to the solution | `docs/plans/2026-09-01-portable-map-editor-part1-core-codec.md:225-283`. SDK default compile globs include the new `Editing/` and test files; no project or solution edit is needed here. |
| Repository policy defaults to no comments/doc strings | `AGENTS.md:5-26`. Add none to Part 2 code/tests; the contracts live in this plan and self-explanatory names. |

The current repository has no pre-existing undo, redo, dirty, Bresenham, or map-editor command implementation (`git grep` found none). There is therefore no legacy editor behavior to preserve beyond the model/blocked semantics cited above.

---

## Locked Part 2 contracts

Use namespace `MapEditor.Core` for every new type, matching Part 1. Put implementation files under `src/MapEditor.Core/Editing/`; the folder does not introduce a nested namespace.

### Public surface

```csharp
public enum MapEditTool
{
    Pencil,
    Eraser,
    Eyedropper,
    BlockedToggle
}

public sealed class MapEditSession
{
    public const long DefaultRetainedHistoryCapBytes = 64 * 1024 * 1024;

    public MapDocument Document { get; }
    public int ActiveLayer { get; set; }
    public MapTileLayer SelectedTileLayer { get; set; }
    public bool HasActiveStroke { get; }
    public bool CanUndo { get; }
    public bool CanRedo { get; }
    public bool IsDirty { get; }
    public long RetainedHistoryCapBytes { get; }
    public long RetainedHistoryUsedBytes { get; }

    public MapEditSession(
        MapDocument document,
        bool initiallyDirty = false,
        long retainedHistoryCapBytes = DefaultRetainedHistoryCapBytes);

    public void BeginStroke(MapEditTool tool, int x, int y);
    public void ContinueStroke(int x, int y);
    public bool CompleteStroke();
    public void CancelStroke();
    public bool Undo();
    public bool Redo();
    public void MarkSaved();
    public void SetRetainedHistoryCap(long bytes);
}
```

Do not expose command classes, change storage, history stacks, mutable state IDs, storage diagnostics, or interpolation collections publicly. Do not add events in this part. A later app can invalidate its canvas after `BeginStroke`/`ContinueStroke` and after a successful `Undo`/`Redo`; rendering invalidation policy is not a core concern. The retained-history properties report the deterministic accounting model below, not process working set or exact CLR heap bytes.

### Session ownership and editor-state contract

- The constructor rejects a null document and a negative cap before creating usable state.
- `ActiveLayer` starts at 0 and accepts only `0..MapDocument.LayerCount-1`; invalid assignments throw `ArgumentOutOfRangeException` and retain the prior value.
- `SelectedTileLayer` starts at `(0,0)` and preserves any `int` sheet/graphic pair exactly. Part 1's codec remains the authority that rejects out-of-wire-range sheets on save.
- Active layer and selected brush are editor selection state. Changing either never creates a command or affects dirty state.
- A session never replaces its `Document`. All editor writes for that document must pass through the session. Direct external calls to `Document.SetFlags`/`SetLayer` are unsupported because they bypass history and dirty tracking; the Godot runtime remains free to use the document directly because it does not create an editor session.
- The API is single-threaded and has at most one active stroke. It contains no locks or dispatching.
- `BeginStroke` validates tool and coordinate before registering or mutating a stroke. Starting a second stroke throws `InvalidOperationException` without affecting the first.
- `ContinueStroke`, `CompleteStroke`, and `CancelStroke` require an active stroke. `Undo`, `Redo`, `MarkSaved`, and `SetRetainedHistoryCap` also throw `InvalidOperationException` while a stroke is active. The later app must complete or cancel pointer interaction before saving or changing history settings.
- `CanUndo` and `CanRedo` are false while a stroke is active, even if retained history exists.
- `CompleteStroke` returns true only when document content changed. Eyedropper and no-op document strokes return false. `CancelStroke` restores pending document values and, for eyedropper, the pre-stroke selected brush.

### Tool semantics

A stroke captures `ActiveLayer` and `SelectedTileLayer` at `BeginStroke`; changing selection properties during a stroke affects only subsequent strokes.

- **Pencil:** For each first-visited cell, replace only the captured active layer with the captured selected `MapTileLayer`. Preserve flags, the other four layers, and every other cell.
- **Eraser:** For each first-visited cell, replace only the captured active layer with `new MapTileLayer(0, 0)`. Preserve flags and all other layers/cells. Do not interpret `Sheet == 0` separately.
- **BlockedToggle:** For each first-visited cell, set `newFlags = oldFlags ^ MapDocument.BlockedFlag`. Preserve every bit other than bit 2 and preserve every layer/cell.
- **Eyedropper:** Read the captured active layer at the latest sampled coordinate into `SelectedTileLayer`. Do not interpolate, mutate the document, create/clear history, or affect dirty state. Continuing an eyedropper drag samples only each supplied endpoint; cancellation restores the brush value from before `BeginStroke`.
- Every map-editing tool visits a cell at most once in a continuous stroke. This is observable for blocked toggle: repeated points, overlapping interpolated segments, or a loop cannot toggle a cell back during the same gesture.
- If the requested after value already equals the before value, do not retain a change. A completely no-op stroke does not clear redo.

### Interpolation helper contract

Add these internal helpers:

```csharp
internal readonly record struct MapCoordinate(int X, int Y);

internal static class GridLine
{
    internal static IEnumerable<MapCoordinate> Enumerate(
        MapCoordinate start,
        MapCoordinate end);
}
```

Use the symmetric integer error-term Bresenham form (`dx`, negative `dy`, signed steps, `error = dx + dy`, then doubled-error x/y decisions). Its contract is:

- Input coordinates have already been validated against the same document and are therefore in `0..999`; the helper performs no map lookup or clipping.
- Include start and end exactly once. A zero-length segment yields one coordinate.
- Produce `max(abs(dx), abs(dy)) + 1` coordinates.
- Each consecutive coordinate moves by at most one on each axis and never repeats.
- Progress is monotonic toward the endpoint on each non-equal axis and works in all eight octants, including horizontal, vertical, 45-degree, shallow, and steep lines.
- The implementation is deterministic, but tests do not require rasterizing `A→B` to be the exact reversed sequence of `B→A` at tie points.

`ContinueStroke` validates its endpoint first, then enumerates inclusively from the previous valid sample. The per-stroke visited bitmap removes the repeated segment start and all path overlap. An invalid endpoint throws before interpolation, leaves the document/stroke unchanged, and does not advance the previous sample; the caller may continue with another valid endpoint or cancel.

### Command, grouping, and storage-ownership contract

Use internal `MapLayerChange` and `MapFlagsChange` value records. A layer change stores `(x, y, layerIndex, beforeLayer, afterLayer)`; a flags change stores `(x, y, beforeFlags, afterFlags)`. An internal append-only `MapEditChangeBuffer<T>` owns typed segments and a logical count. It starts with four slots, doubles segment capacity through 4096 slots, and uses 4096-slot segments thereafter. `MapEditCommand` stores exactly one immutable buffer of one change kind plus immutable before/after state IDs; it does not flatten segments.

- Apply edits eagerly as valid stroke cells arrive and append a before/after delta only for a first-visited effective change.
- `CompleteStroke` creates at most one lightweight command wrapper, regardless of pointer sample count or interpolated cell count. Pencil, eraser, and toggle cannot mix within one stroke.
- Completion transfers ownership of the stroke's existing typed buffer into the command/history. It must not call `ToArray`, copy deltas into a second collection, retain both a stroke list and command copy, or compact the final segment.
- Until history accepts the completed command, the stroke remains the owner and remains cancellable. On acceptance, history owns the command/buffer even if cap enforcement immediately evicts it; the stroke clears its buffer and bitmap references and becomes terminal. An ordinary failure before acceptance leaves the stroke and its deltas intact for cancellation. Fatal allocation failures are not recovered.
- Undo applies all `before` values in reverse segment/order through Part 1 setters. Redo applies all `after` values in forward segment/order. Coordinates/layer indices were validated at capture, so normal undo/redo has no partial-failure path.
- Cancel reverse-replays the stroke-owned buffer and releases it without creating a command or a second delta buffer.
- A no-op completion creates no command, does not advance state identity, and does not clear redo.
- Completing an effective edit after undo clears the complete redo branch before retaining the new command.
- History eviction and redo-branch clearing release their command/buffer references. Do not store `MapTile`, whole-map snapshots, delegates/closures per cell, UI objects, sprite data, or asset references.

### Maximum-stroke peak-storage contract

Part 1 limits each dimension to 1000, so an editing stroke can target at most `TileCount <= 1_000_000` tiles. Use these bounds rather than a `HashSet<int>` or a second completion-time delta array:

- Pencil, eraser, and blocked-toggle strokes allocate a `ulong[]` visited bitmap of exactly `ceil(TileCount / 64)` words and key it by checked row-major tile index. A 1000×1000 map therefore uses 15,625 words, or 125,000 payload bytes, instead of up to one million hash entries.
- One tile bit is sufficient because a stroke captures exactly one editing tool and, for pencil/eraser, one active layer. Tools and layers cannot mix within a stroke. Mark the tile visited before checking whether its requested value is a no-op, so later overlap cannot reinterpret that tile. Eyedropper allocates neither visited bitmap nor delta buffer because it samples only supplied endpoints and changes no document field.
- At most one effective delta can be appended per tile. With segment capacities 4, 8, …, 4096 and then 4096 repeatedly, a one-million-delta buffer has at most 1,003,516 allocated slots in at most 254 segments. Directory growth may copy bounded segment references, never delta values.
- Using the deterministic slot estimates below, the maximum stroke-owned payload/accounting bound is 32,245,704 bytes for layer changes or 24,217,576 bytes for flag changes, including the 125,000-byte bitmap, one command base, and 254 segment charges. These are design-accounting bounds, not claims about exact CLR object headers or process working set.
- Bresenham enumeration is streaming and uses constant per-segment state. The document and pre-existing retained history coexist with the active stroke. At completion, only a lightweight command/history wrapper is added around the transferred buffer; there is no second 24–32 MB delta copy. Thus total editor memory can temporarily exceed `RetainedHistoryCapBytes` by the bounded active-stroke storage and runtime overhead.
- Cancellation preserves eager semantics by reverse-iterating the same segments, restoring every before value, and then releasing the bitmap/buffer references. It does not need a snapshot or replacement buffer.

Internal tests may inspect buffer identity, logical count, allocated slot count, segment count, and bitmap word count through `InternalsVisibleTo`; none of these diagnostics become public API.

### Retained-history accounting contract

`RetainedHistoryCapBytes` is a hard cap only on the deterministic accounting value of commands currently retained across both undo and redo stacks. It is not a hard cap on total editor memory, managed heap, working set, the current document, session/editor state, interpolation, or the transient active-stroke bitmap and delta buffer.

Use checked deterministic accounting rather than claiming exact managed-heap measurement. Because commands retain segmented capacity, charge reserved slots and segments, not only logical changes:

```text
command base:                    64 bytes
per retained change segment:     32 bytes
per reserved MapLayerChange slot: 32 bytes
per reserved MapFlagsChange slot: 24 bytes
```

A command's accounted size is `64 + (segmentCount * 32) + (allocatedSlotCount * slotSize)`. Expose the undo+redo total as `RetainedHistoryUsedBytes`. Keep these constants internal on `MapEditCommand` so tests can verify boundary behavior through Part 1's `InternalsVisibleTo` without expanding public API. This estimate is deterministic and conservative about reserved delta payload, but it still is not exact CLR heap measurement.

- Constructor and `SetRetainedHistoryCap` accept zero and all nonnegative `long` values; negatives throw without changing cap/history.
- Retained usage is always `<= RetainedHistoryCapBytes` after a completed edit or cap change.
- Completing an edit clears redo, transfers the new command to undo, then evicts the oldest undo commands until the retained accounting value is under cap. If the new command alone is oversized, this process evicts every prior command and the new command; the document remains edited and dirty but cannot undo that edit.
- Undo/redo move one command between stacks without changing retained usage or buffer ownership.
- Reducing the cap first evicts oldest undo commands. If more space is needed after undo is empty, evict redo commands farthest from the current state. This preserves a contiguous nearest-to-current redo prefix.
- Increasing the cap never resurrects evicted commands.
- A cap of zero retains no commands. No-op and eyedropper strokes consume no retained-history bytes.

### Dirty/savepoint contract

Dirty state uses unique history-state identities, not `CanUndo`, stack counts, command memory, document hashing, or a monotonically latched Boolean.

- A clean session starts at state 0 with saved state 0. An initially dirty session starts at state 0 with no saved-state identity.
- Each effective completed command receives a unique after-state identity and makes it current. Undo selects that command's before identity; redo selects its after identity.
- `MarkSaved` records the current identity as the savepoint and clears dirty. It does not clear, reorder, or resize history.
- `IsDirty` is true when an effective document stroke is pending, or when no saved identity exists, or when current identity differs from saved identity.
- Undoing back to the savepoint clears dirty; redoing away from it makes dirty true. If save occurred after an edit, undo is dirty and redo back to that saved state is clean.
- Branching after undo uses a fresh identity. A discarded redo savepoint can never be reached accidentally by an equal stack index.
- Evicting the command that once reached a savepoint does not itself change current identity or dirty state. Content at the current saved identity remains clean even with no undo; unreachable saved identities remain dirty.
- Pending effective strokes are dirty immediately because the document is eagerly changed. Cancel restores the pre-stroke dirty value; completing advances identity once.
- Eyedropper, selection changes, no-op strokes, failed validation, and failed history operations never affect dirty.

App boundary examples, for Part 4 consumption only:

```csharp
var newSession = new MapEditSession(MapDocument.Create(width, height), initiallyDirty: true);

var opened = fileStore.Open(path);
var openedSession = new MapEditSession(opened.Document, initiallyDirty: false);

var nextRevision = fileStore.Save(path, session.Document, expectedRevision);
session.MarkSaved();
```

The later app updates its path/revision only after the successful save return. `MapEditSession` does not accept or return either value. A thrown `MapValidationException`, `MapExternalChangeException`, or `IOException` leaves `IsDirty` unchanged because `MarkSaved` was not called.

---

## Mutation impact analysis

| State change | Writers/readers | Mutation path | Required non-impact proof |
|---|---|---|---|
| Active layer | Later app selection controls; stroke reads at begin | Validated `ActiveLayer` setter | No map/history/dirty change; invalid value preserves prior layer |
| Selected brush | Later sprite picker; eyedropper writes; pencil captures | `SelectedTileLayer` value assignment | Exact signed pair retained; no map/history/dirty change |
| One painted/erased layer | Stroke writes; renderer later reads document | Read `GetLayer`, then `Document.SetLayer` only if different | Flags, other four layers, neighbors, dimensions, and header unchanged |
| One blocked state | Stroke writes; overlay/path behavior later reads `IsBlocked` | `Document.SetFlags(x,y,old ^ BlockedFlag)` | Every unknown flag bit and all layers/neighbors unchanged; one visit per stroke |
| Eyedropper sample | Stroke writes editor brush only | Read captured layer from document | Document bytes, history, redo, and dirty unchanged; cancel restores prior brush |
| Interpolated stroke | Pointer adapter later supplies sparse valid samples | Inclusive `GridLine`, then visited-index suppression | No gaps, no duplicate changes, no out-of-bounds mutation, one completion command |
| Pending stroke | Begin/continue eagerly mutate document | One-bit-per-tile bitmap plus one typed segmented delta buffer | `IsDirty` reflects pending content; at most one delta per tile; cancel reverse-replays exact prior values without a copy |
| Completed command | Session completion | Transfer the stroke buffer, assign fresh state ID, clear redo, retain one command subject to cap | One undo step and no completion-time delta duplication regardless of cell count; no-op leaves all history intact |
| Undo/redo | App shortcut/menu later calls Boolean methods | Apply recorded values through Part 1 setters and move the same command/buffer | Deterministic exact values; retained usage and ownership unchanged; unrelated map state unchanged |
| Retained-history cap | Constructor/settings later configure | Deterministic command/segment/slot accounting and nearest-history eviction | Retained usage never exceeds cap; no claim about total editor memory; document/current identity/dirty do not change from eviction |
| Savepoint/dirty | Constructor, effective edit, undo/redo, cancel, `MarkSaved` | Compare current and saved unique state IDs plus pending change | Undo/redo around saves and branches cannot produce false clean state |
| New/open document | Part 4 app only | Construct a new session after create/open succeeds | Old session remains intact on failed open; new session gets empty history |
| Save result | Part 1 store plus Part 4 app | `Save` returns, app updates metadata, then `MarkSaved` | Save exception leaves path/revision/session dirty/history unchanged |
| Godot runtime map | Existing Part 1 migrated client | Continues direct `MapDocument.Set*` use without `MapEditSession` | No editor API injected into packet/render paths; client build/tests remain green |

The highest-risk mutations are blocked toggles on revisited cells, eager changes canceled before command creation, redo branching, and dirty state after history eviction. They receive explicit tests below rather than relying on implementation inspection.

---

## Task 1: Add deterministic all-octant grid-line interpolation

**Files:**
- Create: `src/MapEditor.Core/Editing/MapCoordinate.cs`
- Create: `src/MapEditor.Core/Editing/GridLine.cs`
- Create: `tests/MapEditor.Core.Tests/GridLineTests.cs`

**Step 1: Confirm the Part 1 prerequisite.**

```bash
test -f src/MapEditor.Core/MapEditor.Core.csproj
test -f src/MapEditor.Core/MapDocument.cs
test -f tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj
dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj -v minimal
```

Expected: all Part 1 core tests pass. Stop and execute/fix Part 1 if any file or gate is missing.

**Step 2: Write failing interpolation tests.**

Add:

- `Enumerate_ZeroLengthContainsOnePoint`.
- `Enumerate_HorizontalAndVerticalIncludeBothEndpoints`: both signs on each axis.
- `Enumerate_DiagonalIncludesEveryCell`: all four 45-degree directions.
- `Enumerate_ShallowLineMatchesLockedErrorTermSequence` and `Enumerate_SteepLineMatchesLockedErrorTermSequence`: assert exact sequences for representative positive/negative directions so a floating-point rounding substitute cannot drift.
- `Enumerate_AllOctantsAreMonotonicConnectedAndExpectedLength`: table-driven endpoints around one origin; assert count `max(dx,dy)+1`, unique points, first/last, axis monotonicity, and consecutive Chebyshev distance 1.
- `Enumerate_NearMaximumMapCoordinatesStaysInBounds`: representative segments within a 1000×1000 map.

Use explicit expected coordinate literals for the locked shallow/steep cases. Do not derive expected output with a second interpolation helper.

**Step 3 (red):**

```bash
dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj --filter FullyQualifiedName~GridLineTests -v minimal
```

Expected: compile failure because `MapCoordinate`/`GridLine` do not exist.

**Step 4: Implement the helper.**

Implement the exact internal iterator contract above with integer arithmetic only. Do not use floating point, `System.Drawing`, clipping, map access, or a preallocated map-sized buffer. Add no comments/doc strings.

**Step 5 (green):**

```bash
dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj --filter FullyQualifiedName~GridLineTests -v minimal
```

Expected: all interpolation tests pass.

**Step 6: Run regression gates and commit.**

```bash
dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj -v minimal
dotnet build src/MapEditor.Core/MapEditor.Core.csproj -v minimal
git diff --check
git add src/MapEditor.Core/Editing/MapCoordinate.cs \
  src/MapEditor.Core/Editing/GridLine.cs \
  tests/MapEditor.Core.Tests/GridLineTests.cs
git commit -m "feat(map-editor): add grid stroke interpolation"
```

**Task 1 invariant matrix:**

| Invariant | Test |
|---|---|
| Both endpoints occur exactly once | zero, axis, diagonal, and all-octant tests |
| Sparse samples cannot leave a disconnected path | `Enumerate_AllOctantsAreMonotonicConnectedAndExpectedLength` |
| All octants and directions work with integer behavior | axis/diagonal/shallow/steep table tests |
| A segment uses bounded per-segment output | expected-length test plus iterator source review |
| Valid map endpoints remain in map bounds | `Enumerate_NearMaximumMapCoordinatesStaysInBounds` |

---

## Task 2: Add tool semantics, eager grouped strokes, undo/redo, and savepoint dirty state

**Files:**
- Create: `src/MapEditor.Core/Editing/MapEditTool.cs`
- Create: `src/MapEditor.Core/Editing/MapEditChange.cs`
- Create: `src/MapEditor.Core/Editing/MapEditChangeBuffer.cs`
- Create: `src/MapEditor.Core/Editing/StrokeVisitBitmap.cs`
- Create: `src/MapEditor.Core/Editing/MapEditCommand.cs`
- Create: `src/MapEditor.Core/Editing/MapEditHistory.cs`
- Create: `src/MapEditor.Core/Editing/MapEditStroke.cs`
- Create: `src/MapEditor.Core/Editing/MapEditSession.cs`
- Create: `tests/MapEditor.Core.Tests/MapEditSessionTests.cs`
- Create: `tests/MapEditor.Core.Tests/MapEditStrokeTests.cs`
- Create: `tests/MapEditor.Core.Tests/MapEditStrokeStorageTests.cs`

At this task's commit, history may be unbounded internally. Keep history encapsulated so Task 3 can add the final cap constructor/properties without changing tool/stroke behavior. The final public surface is the locked surface above; Task 3 supplies its cap members.

**Step 1: Write failing single-cell tool/session tests.**

Add to `MapEditSessionTests`:

- `Constructor_UsesDocumentLayerZeroEmptyBrushAndCleanBaseline`.
- `Constructor_InitiallyDirtyHasNoSavedBaseline`.
- `Constructor_RejectsNullDocument`.
- `ActiveLayer_RejectsMinusOneAndLayerCountWithoutChangingSelection`.
- `SelectionChanges_DoNotChangeDocumentHistoryOrDirtyState`.
- `Pencil_ChangesOnlyCapturedLayerAtTarget`: seed flags, all five layers, and a neighbor; set layer 3 and an unusual signed brush pair.
- `Pencil_NoOpDoesNotCreateHistoryOrDirty`.
- `Eraser_ClearsOnlyCapturedLayerAtTarget`.
- `Eraser_EmptyLayerIsNoOp`.
- `BlockedToggle_XorsOnlyBlockedBitAndPreservesUnknownBits`: include negative flags/high bits and verify `before ^ 2` exactly.
- `Eyedropper_CopiesExactActiveLayerWithoutDocumentOrHistoryChange`.
- `Eyedropper_CancelRestoresPreviousBrush`.
- `InvalidToolOrCoordinate_ThrowsBeforeMutationOrActiveStroke`.
- `Stroke_CapturesLayerAndBrushAtBegin`: mutate public selection between begin and continue; all cells use captured values.

For each mutating test, compare every unaffected field, not just the intended field.

**Step 2: Write failing stroke/grouping tests.**

Add to `MapEditStrokeTests`:

- `ContinueStroke_InterpolatesSparseHorizontalSamplesWithoutGaps`.
- `ContinueStroke_InterpolatesShallowAndSteepSegments`.
- `ContinueStroke_MultipleSegmentsAreOneUndoStep`.
- `ContinueStroke_RepeatedPointsAndOverlappingSegmentsChangeEachCellOnce`.
- `BlockedToggle_LoopTogglesEachVisitedCellExactlyOnce`.
- `InvalidContinuation_DoesNotMutateOrAdvancePreviousSample`: follow the failure with a valid point and verify interpolation starts from the prior valid sample.
- `CancelStroke_RestoresEveryChangedValueAndKeepsUndoRedo`.
- `PendingEffectiveStroke_IsDirtyAndCancelRestoresPriorDirtyState`.
- `NoOpStroke_DoesNotClearRedo`.
- `BeginWhileActiveAndHistoryOperationsWhileActiveThrowWithoutMutation`.
- `StrokeLifecycleMethodsWithoutActiveStrokeThrow`.

Add to `MapEditStrokeStorageTests`:

- `EditingStroke_VisitedBitmapUsesExactlyCeilingTileCountOver64Words`.
- `Eyedropper_AllocatesNoVisitedBitmapOrDeltaBuffer`.
- `NoOpTile_IsMarkedVisitedButAddsNoDelta`.
- `SegmentGrowth_UsesFourThrough4096ThenFixed4096Capacities`.
- `MaximumLayerStroke_HasAtMostTileCountDeltas1003516SlotsAnd254Segments`: use a serpentine 1000×1000 path that visits every tile.
- `MaximumToggleStroke_WithLoopsNeverExceedsTileCountDeltasOrBitmapBound`: include repeated rows, boundary loops, and self-crossings.
- `CompleteMaximumStroke_TransfersSameDeltaBufferToCommandWithoutCopy`: assert buffer identity, segment identities/count/capacity, and logical count are unchanged across acceptance.
- `CancelMaximumStroke_ReplaysSameBufferAndRetainsNoCommand`: compare all seeded values/history and assert no second delta buffer was created.

Use internal identity/count/capacity diagnostics, not `GC.GetTotalMemory`, process working set, timing, or CLR object-size assumptions. The full-map tests lock measurable shape bounds: 15,625 bitmap words, no more than 1,000,000 logical deltas, no more than 1,003,516 reserved slots, and no more than 254 segments. Keep fixtures sequential if test-runner parallelism would distort allocation instrumentation.

**Step 3: Write failing command/history/dirty tests.**

Add to `MapEditSessionTests`:

- `CompleteStroke_ReturnsTrueForDocumentChangeAndFalseForNoOpOrEyedropper`.
- `Undo_RestoresAllBeforeValuesAndRedoRestoresAllAfterValues`.
- `UndoAndRedo_ReturnFalseWhenUnavailable`.
- `UndoThenEffectiveEdit_ClearsRedo`.
- `UndoThenNoOpOrEyedropper_PreservesRedo`.
- `CompletedDrag_IsExactlyOneUndoStep`.
- `InitiallyClean_EditUndoRedoTransitionsAroundBaseline`.
- `InitiallyDirty_EditUndoRemainsDirtyUntilMarkSaved`.
- `MarkSaved_ClearsDirtyWithoutClearingHistory`.
- `SaveAfterEdit_UndoIsDirtyAndRedoToSavepointIsClean`.
- `BranchAfterUndo_CannotMatchDiscardedSavepointByStackPosition`.
- `CanceledAndNoOpStrokes_DoNotAdvanceDirtyStateIdentity`.

**Step 4 (red):**

```bash
dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj \
  --filter "FullyQualifiedName~MapEditSessionTests|FullyQualifiedName~MapEditStrokeTests|FullyQualifiedName~MapEditStrokeStorageTests" -v minimal
```

Expected: compile failure because `MapEditTool` and `MapEditSession` do not exist.

**Step 5: Implement tool/session validation and eager strokes.**

`MapEditSession` owns the document, selection, history, current/saved state IDs, and nullable active `MapEditStroke`. Validate a begin fully before assigning the active stroke. Have the stroke capture tool/layer/brush/initial brush and apply the first sample immediately.

For editing tools, allocate a `StrokeVisitBitmap` with one bit per document tile, keyed by checked row-major `y * Document.Width + x`, and one typed `MapEditChangeBuffer<T>`. On each valid continuation, enumerate from the prior sample through the new sample and skip indexes whose bit was already set. Because tool and layer are captured for the entire stroke, tile identity is the complete duplicate-suppression key. For eyedropper, sample only the supplied coordinate and allocate neither structure.

Append deltas into non-copying segments with capacities 4, 8, …, 4096 and fixed 4096 thereafter. Set the visited bit before no-op comparison. Set the prior sample only after endpoint validation. Apply each effective value through `SetLayer` or `SetFlags`, then append its before/after delta. A normal change cannot fail after coordinate/layer validation. Do not use `HashSet<int>`, `List<T>.ToArray`, a million-element index collection, or up-front million-delta allocation.

**Step 6: Implement grouping, undo/redo, cancel, and dirty identities.**

- Prepare one immutable command wrapper over the stroke-owned buffer, then atomically transfer ownership when history accepts completion; do not enumerate or copy deltas during handoff.
- Complete an effective editing stroke as that one command and assign one fresh state identity. Clear the stroke's bitmap/buffer references after transfer.
- Clear redo only when that effective command completes.
- Cancel by reverse-iterating the still stroke-owned segments and restoring the initial eyedropper brush where relevant; allocate no command or replacement buffer.
- Undo/redo apply the command before moving it between stacks/current identity; if an unexpected model exception occurs, leave stack/current identity unchanged and rethrow. Do not catch fatal runtime failures.
- Use a monotonically increasing unique state ID for every effective branch. Never derive clean state from list indexes/counts.
- While a document-changing stroke is pending, `IsDirty` must be true even though no state ID has been assigned.
- Keep command/change/history types internal. Add no app callbacks, command names, localization, or comments/doc strings.

**Step 7 (green):**

```bash
dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj \
  --filter "FullyQualifiedName~MapEditSessionTests|FullyQualifiedName~MapEditStrokeTests|FullyQualifiedName~MapEditStrokeStorageTests|FullyQualifiedName~GridLineTests" -v minimal
dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj -v minimal
```

Expected: all editing and prior core tests pass.

**Step 8: Run mutation and dependency audits.**

```bash
git grep -nE 'SetLayer|SetFlags' -- src/MapEditor.Core/Editing tests/MapEditor.Core.Tests
git grep -nE 'Godot|Avalonia|MapEditor.Rendering|manifest|Sprite|Texture|Bitmap' -- src/MapEditor.Core/Editing || true
git grep -nE 'MapTile\[\]|MapDocument\[\]|Clone|BinaryFormatter|HashSet<int>|ToArray\(' -- src/MapEditor.Core/Editing || true
git diff --check
```

Expected: production mutation occurs only in stroke command application/cancellation through Part 1 setters; no forbidden dependency, snapshot strategy, hash-entry visited set, or completion-time delta flattening appears.

**Step 9: Run regression gates and commit.**

```bash
dotnet test Goose2ClientGodot.sln -v minimal
dotnet build Goose2ClientGodot.sln -v minimal
git add src/MapEditor.Core/Editing tests/MapEditor.Core.Tests/MapEditSessionTests.cs \
  tests/MapEditor.Core.Tests/MapEditStrokeTests.cs \
  tests/MapEditor.Core.Tests/MapEditStrokeStorageTests.cs
git commit -m "feat(map-editor): add grouped editing history"
```

Require zero test failures, zero build errors, and no warnings introduced by Part 2 files. Existing unrelated Godot nullable warnings are not part of this scope.

**Task 2 invariant matrix:**

| Invariant | Test |
|---|---|
| Pencil and eraser affect only captured active layer | narrow tool tests and selection-capture test |
| Toggle changes exactly bit 2 and each cell once | unknown-bit and loop/overlap tests |
| Eyedropper changes only exact brush selection | eyedropper complete/cancel tests |
| Sparse pointer samples produce a connected edit | horizontal/shallow/steep stroke tests |
| One continuous drag is one command/undo step | multiple-segment and completed-drag tests |
| Maximum stroke duplicate suppression is one bit per tile | full-map serpentine/loop bitmap-bound tests |
| Completion transfers one segmented delta buffer without copying | buffer/segment identity and capacity tests |
| Cancel restores exact pre-stroke values from the same buffer | cancel tests with pre-existing undo/redo and maximum shape |
| No-op does not dirty or clear redo | pencil/eraser no-op and redo preservation tests |
| Undo/redo replay exact before/after data | seeded multi-cell/multi-field round-trip tests |
| Dirty tracks savepoint identity across branch changes | baseline/save/undo/redo/branch tests |
| Invalid calls fail before unrelated state mutation | invalid tool/coordinate/lifecycle/active-operation tests |

---

## Task 3: Enforce the configurable retained-history cap and verify Part 1 save boundaries

**Files:**
- Modify: `src/MapEditor.Core/Editing/MapEditCommand.cs`
- Modify: `src/MapEditor.Core/Editing/MapEditHistory.cs`
- Modify: `src/MapEditor.Core/Editing/MapEditSession.cs`
- Create: `tests/MapEditor.Core.Tests/MapEditHistoryTests.cs`
- Create: `tests/MapEditor.Core.Tests/MapEditPersistenceTests.cs`

Do not modify `MapFileStore`; these tests prove the sequencing contract between already planned Part 1 storage and the new session.

**Step 1: Write failing retained-history accounting and cap tests.**

Add to `MapEditHistoryTests`:

- `Constructor_Uses64MiBDefaultCap`.
- `Constructor_RejectsNegativeCap`.
- `OneLayerCommand_AccountsBaseSegmentAndFourReservedSlots`.
- `OneFlagsCommand_AccountsBaseSegmentAndFourReservedSlots`.
- `MultiCellCommand_AccountsBaseSegmentsAndReservedCapacity`.
- `SegmentBoundaryAccounting_ChangesOnlyWhenAReservedSegmentIsAdded`.
- `Cap_ExactlyCommandSizeRetainsCommand`.
- `Cap_OneByteBelowCommandSizeDropsOversizedCommandButKeepsEditDirty`.
- `Cap_ZeroRetainsNoUndoOrRedo`.
- `CompletingNewCommand_ClearsRedoBeforeCapEviction`.
- `OldestUndoCommandsAreEvictedAndNearestUndoRemains`.
- `UndoAndRedo_MoveCommandWithoutChangingUsage`.
- `ReduceCap_EvictsOldestUndoThenFarthestRedo`.
- `IncreaseCap_DoesNotRestoreEvictedCommands`.
- `SetNegativeCap_ThrowsWithoutChangingCapUsageStacksDocumentOrDirty`.
- `NoOpEyedropperAndCanceledStroke_ConsumeNoRetainedHistoryBytes`.
- `ActiveStrokeBitmapAndDeltas_AreExcludedFromRetainedHistoryUsage`.
- `UsageNeverExceedsCapAcrossMixedLayerAndFlagsCommands`: table/property-style loop over small deterministic caps and edits.

Avoid tests that depend on CLR object sizes. Assert the locked deterministic command/segment/reserved-slot constants and public retained usage only. Verify explicitly that `RetainedHistoryCapBytes` does not include the document or an active stroke and is never described or tested as a process/heap ceiling.

**Step 2: Write failing dirty-state/eviction edge tests.**

- `EvictingHistoryWhileCurrentlySaved_RemainsClean`.
- `EvictingCommandThatCouldReachSavedState_RemainsDirty`.
- `OversizedEditAfterSavedState_IsDirtyAndNotUndoable`.
- `ReducingCapDoesNotChangeCurrentDocumentOrStateIdentity`.
- `SetCapWhileStrokeActive_ThrowsWithoutEviction`.

These distinguish state identity from history reachability and `CanUndo`.

**Step 3: Write Part 1 persistence-boundary tests.**

Add to `MapEditPersistenceTests` using a unique temporary directory:

- `OpenedDocumentSession_StartsCleanWithEmptyHistory`: `MapFileStore.Open`, then construct with `initiallyDirty:false`.
- `NewDocumentSession_StartsDirtyWithEmptyHistory`: construct `MapDocument.Create` with `initiallyDirty:true`.
- `SuccessfulSave_DoesNotClearDirtyUntilMarkSaved`: perform edit, call store save, assert still dirty/history intact, then call `MarkSaved` and assert clean/history intact.
- `ValidationFailure_LeavesDirtyAndHistoryUnchanged`: paint an out-of-`Int16` sheet, assert `MapValidationException` from Part 1 save, and compare dirty/can-undo/usage/document.
- `ExternalChangeFailure_LeavesDirtyAndHistoryUnchanged`: open, edit, externally change same-length file, guarded save throws `MapExternalChangeException`; do not call `MarkSaved`.
- `NewSessionAfterSuccessfulOpenHasNoPriorSessionHistory`: retain an edited old session, successfully open another map, construct a separate session, and prove old/new document/history/dirty states are independent.
- `FailedOpenCannotReplaceOrResetExistingSessionByCoreAPI`: make `Open` throw and prove the caller still owns the untouched prior session; there is no `ReplaceDocument` API.

Clean temporary directories in `finally`. Do not add path/revision properties to `MapEditSession` to make these tests convenient.

**Step 4 (red):**

```bash
dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj \
  --filter "FullyQualifiedName~MapEditHistoryTests|FullyQualifiedName~MapEditPersistenceTests" -v minimal
```

Expected: compile failures for the final cap constructor/properties/method, followed by behavioral failures until accounting and eviction are implemented.

**Step 5: Implement deterministic accounting and eviction.**

Add the locked internal constants and checked command-size calculation from command base, retained segment count, and reserved slot count. Track one total retained usage value in history; accepting/releasing a command adjusts it exactly once, while undo/redo movement does not. Do not add active-stroke bitmap/buffer usage or document size to this value.

Extend the session constructor with the optional retained-history cap parameter and expose the locked cap/usage members. On effective completion, clear redo, transfer the command/buffer to undo, then enforce the cap from oldest undo. On cap reduction, remove oldest undo first and then farthest redo until usage fits. Use data structures whose ends correspond clearly to oldest/newest undo and nearest/farthest redo; do not use repeated whole-list reversal or LINQ allocation in edit paths.

An oversized command remains applied and gets a fresh current state ID before it is evicted, preserving dirty correctness. Eviction releases command/buffer references but never changes document values or current/saved IDs. Completion must use the Task 2 ownership transfer unchanged; cap enforcement must not flatten, clone, or re-materialize deltas.

**Step 6 (green focused and full core):**

```bash
dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj \
  --filter "FullyQualifiedName~MapEditHistoryTests|FullyQualifiedName~MapEditPersistenceTests" -v minimal
dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj -v minimal
```

Expected: all cap, persistence, editing, codec, storage, and model tests pass.

**Step 7: Audit final public shape and forbidden ownership.**

```bash
git grep -nE 'public .*MapEdit(Command|History|Change|Stroke)|public .*StateId' -- src/MapEditor.Core || true
git grep -nE '(Path|FileName|Revision|OpenedMap|MapFileStore)' -- src/MapEditor.Core/Editing || true
git grep -nE 'Godot|Avalonia|Drawing|Rendering|Assets|manifest|png' -- src/MapEditor.Core/Editing || true
git diff --check
```

Expected: no internal history/change/state type is public; no path/revision/storage or platform/rendering/asset concern leaks into editing.

**Step 8: Run the complete gate and commit.**

```bash
dotnet test Goose2ClientGodot.sln -v minimal
dotnet build Goose2ClientGodot.sln -v minimal
git status --short
git diff --stat
git diff --check
git add src/MapEditor.Core/Editing/MapEditCommand.cs \
  src/MapEditor.Core/Editing/MapEditHistory.cs \
  src/MapEditor.Core/Editing/MapEditSession.cs \
  tests/MapEditor.Core.Tests/MapEditHistoryTests.cs \
  tests/MapEditor.Core.Tests/MapEditPersistenceTests.cs
git commit -m "feat(map-editor): cap retained history and track savepoints"
```

**Task 3 invariant matrix:**

| Invariant | Test |
|---|---|
| Accounting is deterministic per command, segment, reserved capacity, and delta kind | one/multi layer/flags and segment-boundary accounting tests |
| Total retained undo+redo usage never exceeds cap without claiming a total-memory ceiling | boundary, zero, oversized, mixed loop, and active-stroke exclusion tests |
| Eviction preserves nearest useful contiguous history | oldest-undo/farthest-redo tests |
| Undo/redo movement does not double count | movement usage test |
| Eviction cannot imply clean/dirty from reachability | saved/unreachable/oversized dirty tests |
| Save alone cannot clear dirty | successful-save-before-`MarkSaved` test |
| Any save/open failure leaves existing session state intact | validation/external-change/failed-open tests |
| New/open resets history by session replacement, not mutation | independent-session tests and public-shape audit |

---

## Failure behavior checklist

| Operation/failure | Required behavior |
|---|---|
| Session construction with null document | `ArgumentNullException`; no session |
| Negative constructor/setter cap | `ArgumentOutOfRangeException`; existing session cap/history/document/dirty unchanged |
| Invalid active layer | `ArgumentOutOfRangeException`; prior active layer and all other state unchanged |
| Invalid enum or begin coordinate | `ArgumentOutOfRangeException`; no active stroke or mutation |
| Begin during active stroke | `InvalidOperationException`; existing stroke remains active and unchanged |
| Continue with invalid coordinate | `ArgumentOutOfRangeException` before interpolation/mutation; prior sample remains anchor |
| Continue/complete/cancel without stroke | `InvalidOperationException`; state unchanged |
| Undo/redo/mark-saved/`SetRetainedHistoryCap` during stroke | `InvalidOperationException`; pending stroke and retained history unchanged |
| Undo/redo with no available command | Return false; no exception or state change |
| No-op/eyedropper completion | Return false; preserve redo, state ID, dirty, and usage |
| Cancel effective edit | Reverse-replay the stroke-owned segments, restore exact before values, and preserve redo/history/current/savepoint and prior dirty state; create no command/copy |
| Completion before history acceptance fails ordinarily | Stroke retains ownership, remains active, and can be canceled; no redo/history/state-ID change |
| Oversized completed command | Keep edit and fresh state identity; transfer then evict the command/buffer to the retained-history cap; dirty remains correct |
| Part 1 encode/save validation error | Exception propagates; session remains dirty with history intact |
| Part 1 external-change/filesystem save error | Exception propagates; caller does not call `MarkSaved`; path/revision/session unchanged |
| Part 1 open error | No new session is constructed/swapped; old app-held session unchanged |

Do not catch and convert argument/state/storage failures to silent success. Do not attempt recovery from `OutOfMemoryException` or other fatal runtime failures. The ordinary command replay path is built solely from previously validated coordinates/layers and Part 1 setters, so it should not partially fail; if an unexpected setter exception occurs, rethrow without moving history/current identity.

---

## Consolidated invariant-to-test matrix

| ID | Invariant | Automated proof / audit |
|---|---|---|
| E1 | Editing remains engine/rendering/UI/asset neutral | dependency grep and core project references |
| E2 | Session owns one fixed Part 1 document and no path/revision | public-shape/storage grep and persistence tests |
| E3 | Active layer is exactly 0…4 and selection state is non-document state | constructor/selection validation tests |
| E4 | Pencil changes only one captured layer | seeded narrow mutation tests |
| E5 | Eraser writes only `(0,0)` to one captured layer | seeded erase/no-op tests |
| E6 | Blocked toggle is exact XOR bit 2 with unknown-bit retention | negative/high-bit and loop tests |
| E7 | Eyedropper preserves exact numeric references without map mutation | sample/drag/cancel tests |
| E8 | Interpolation is inclusive, connected, integer, deterministic, and all-octant | `GridLineTests` |
| E9 | Repeated/overlapping stroke cells apply at most once | repeat/overlap/blocked-loop tests |
| E10 | Editing strokes use one bit per tile and at most one delta per tile | maximum serpentine/loop bitmap and shape-bound tests |
| E11 | Continuous drag creates at most one command by ownership transfer, without a delta copy | multi-segment one-undo and storage-identity tests |
| E12 | Cancellation reverse-replays the same storage and restores all prior history state | cancel with existing undo/redo and maximum-shape tests |
| E13 | No-op edits do not dirty, consume retained-history budget, or clear redo | no-op tool/history tests |
| E14 | Undo/redo deterministically replay exact before/after values | multi-cell seeded replay tests |
| E15 | Effective branch edit clears redo and gets unique identity | branch/savepoint tests |
| E16 | Retained undo+redo history obeys the configured accounting cap | exact-boundary/zero/oversized/mixed tests |
| E17 | The cap excludes document and active-stroke storage and makes no total-memory claim | active-stroke exclusion test, API names, and wording audit |
| E18 | Maximum 1000×1000 stroke storage obeys bitmap/segment/count bounds | full-map adversarial shape tests |
| E19 | Eviction preserves nearest reachable operations | oldest-undo/farthest-redo tests |
| E20 | Dirty state tracks current versus saved identity, not stack shape | baseline/save/undo/redo/branch/eviction matrix |
| E21 | Pending edits are dirty and cancel restores prior dirty | pending/cancel tests |
| E22 | Save clears dirty only through explicit post-success `MarkSaved` | storage-boundary tests |
| E23 | Failed open/save cannot reset the existing session | validation/conflict/open failure tests |
| E24 | Direct value-type copy mutation is never used | production setter grep and fresh document-read assertions |
| E25 | Godot behavior is unaffected | full solution tests and Godot build; no Godot files modified |

---

## Red-team review

Perform after Task 3 and before declaring Part 2 complete. Fix findings in the commit that introduced the behavior and rerun that task's focused and full gates. Do not add Part 3/4 code as a workaround.

1. **Wrong-layer attack:** Seed every layer and flags, paint/erase layer 3, and compare all fields/cells. Reject whole-tile replacement or a hard-coded layer.
2. **Unknown-bit attack:** Toggle flags `int.MinValue | 0x101 | 2` twice in separate strokes. Each stroke must change only bit 2; undo/redo must restore exact signed integers.
3. **Loop double-toggle attack:** Draw a path that crosses its start and another segment. Every cell must toggle once for the whole continuous stroke, not once per segment/sample.
4. **Gap attack:** Supply only distant shallow/steep endpoints. Inspect every expected Bresenham coordinate and consecutive Chebyshev distance; reject endpoint-only painting or floating-point holes.
5. **Out-of-bounds partial attack:** Continue an already effective stroke to `-1`, `width`, `height`, then to a valid endpoint. Confirm the invalid call adds no cells and does not become the next interpolation anchor.
6. **Value-copy attack:** Search editing production code for assignments to an indexer-returned `MapTile`/`MapTileLayer` local. Every persistent write must call Part 1 `SetLayer`/`SetFlags` and survive a fresh read.
7. **Cancel attack:** Begin from a state with both undo and redo, mutate many cells, then cancel. Compare encoded document bytes, stack availability, usage, current/savepoint dirty, and brush before/after.
8. **No-op redo attack:** Undo, then paint the already-present value, erase an empty layer, or eyedrop. Redo must remain available because no command completed.
9. **Selection-race attack:** Change active layer and selected brush midway through a stroke. Existing stroke must use captured values; only the next stroke sees new selection.
10. **Stack-index dirty attack:** Save at state A, undo, branch to state B with the same apparent history depth. B must remain dirty because state identities differ.
11. **Savepoint eviction attack:** Save, navigate away, reduce cap until the command leading to the savepoint is evicted. Dirty must not become clean merely because history was removed.
12. **Oversized command attack:** Paint enough cells that one command exceeds cap. Confirm the edit remains, usage is within cap, `CanUndo` is false if nothing else fits, and dirty is true.
13. **Redo accounting attack:** Repeatedly undo/redo under a tight cap. Usage must remain constant while commands move and never count the same command twice.
14. **Cap-reduction topology attack:** Create three commands, undo twice, then shrink to one command. Keep the nearest redo command, not a disconnected far redo command.
15. **Million-entry attack:** Sweep and self-cross a 1000×1000 map. Require exactly 15,625 visited words, at most one million logical deltas, at most 1,003,516 reserved slots, and at most 254 segments; reject `HashSet<int>`, per-tile objects, or any equivalent million-entry index structure.
16. **Completion-copy attack:** Complete a maximum layer stroke while old history is near its cap. Assert the command owns the same buffer/segments and logical count, with no `ToArray`, flattening, compaction, or second 24–32 MB delta collection before eviction.
17. **Cancellation-storage attack:** Cancel a maximum adversarial stroke. Require reverse replay from the original segments, exact restoration, no command, and no replacement delta buffer; eager dirty/canvas-visible semantics must remain intact until cancellation.
18. **Memory-claim attack:** Inspect API names, tests, and docs. `RetainedHistoryCapBytes` limits deterministic retained undo+redo accounting only; document, active-stroke transient storage, runtime object overhead, and total managed/process memory are explicitly outside the claim.
19. **Segment-accounting attack:** Create commands immediately below/above each segment boundary. Charge command base, every retained segment, and every reserved slot once; undo/redo changes no usage and eviction releases the full charge.
20. **Snapshot attack:** Search for full document/tile arrays, codec byte snapshots, serialization, or one closure per cell in history. Commands retain only one typed segmented delta buffer.
21. **Failed-save attack:** Trigger sheet validation and external-change failures. Any helper that calls `MarkSaved` in `finally`, before save return, or inside `MapFileStore` is a defect.
22. **Open replacement attack:** Trigger malformed/missing open while an edited session exists. Core APIs must make replacement impossible without the app explicitly assigning a newly constructed session.
23. **Dependency attack:** Search editing files/project references for Godot, Avalonia, rendering, images, manifests, paths, and revisions. None belong here.
24. **Scope-creep attack:** Reject rectangle/flood fill, resize, selection/clipboard, file dialogs, prompts, shortcuts, canvas invalidation, settings persistence, asset validation, build/publish, or Godot integration changes.
25. **Public-API attack:** Reject public command/history/change buffers, storage diagnostics, mutable state IDs, document replacement, or backing storage. Keep only the locked public surface.
26. **Repository-hygiene attack:** Confirm no `bin/obj`, temporary maps, `.godot` edits, generated assets, project SDK rewrite, rendering/app projects, or unrelated warning fixes are staged.

Final commands:

```bash
dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj -v minimal
dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj -v minimal
dotnet test Goose2ClientGodot.sln -v minimal
dotnet build Goose2ClientGodot.sln -v minimal
git grep -nE 'Godot|Avalonia|MapEditor.Rendering|manifest|png' -- src/MapEditor.Core/Editing || true
git grep -nE '(Path|FileName|Revision|OpenedMap|MapFileStore)' -- src/MapEditor.Core/Editing || true
git grep -nE 'HashSet<int>|ToArray\(' -- src/MapEditor.Core/Editing || true
git diff --check
git status --short
```

Expected implementation commit sequence:

1. `feat(map-editor): add grid stroke interpolation`
2. `feat(map-editor): add grouped editing history`
3. `feat(map-editor): cap retained history and track savepoints`

Each implementation commit must be independently buildable/testable at its stated gate. Do not squash unrelated work into these commits. Do not commit this planning-only request, and do not begin rendering/assets/Avalonia/build implementation.
