# Portable Map Editor — Part 4 of 4: Avalonia Application and Local Builds Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Deliver the standalone Avalonia 11 desktop map editor over Parts 1–3: application composition, desktop controls and dialogs, map/palette drawing adapters, complete pointer/keyboard editing, guarded document lifecycle, OS-standard asset settings, actionable errors, a repeatable local smoke path, and local self-contained archives for Linux x64, Windows x64, macOS x64, and macOS arm64.

**Architecture:** `MapEditor.App` targets `net8.0`, references Core and Rendering, and is the only project containing Avalonia types. `EditorDocumentController` owns one active `MapEditSession` plus path/revision and publishes a replacement only after create/open succeeds. `MainWindowViewModel` exposes selection, tools, layer visibility, overlays, status, and lifecycle actions; controls call it only on the UI thread. `MapCanvas` translates Avalonia drawing/input to Part 2/3 APIs without retaining render operations. One replaceable `AssetContext` owns the Part 3 cache/renderer and all Avalonia bitmaps; its explicit unavailable cache mode resolves every nonempty numeric reference as unavailable without inventing manifest data. Dialog and settings interfaces keep lifecycle behavior testable without native pickers. `build-map-editor.sh` publishes the dirty working tree into a run-specific staging directory and atomically publishes a complete requested archive set without deleting prior successful sets.

**Series:** Part 4 of 4.
- **Part 1:** Core model/codec/storage and Godot migration.
- **Part 2:** Editing session, grouped strokes, history cap, and dirty savepoints.
- **Part 3:** Neutral assets, viewport math, culling, and draw operations.
- **Part 4 (this plan):** Avalonia application, adapters, lifecycle, settings, smoke path, and local archives.

**Tech Stack:** C# / .NET 8; Avalonia Desktop, Fluent theme, Inter font, and Avalonia Headless xUnit **11.3.20**; xUnit 2.9.2 and existing Microsoft.NET.Test.Sdk 17.11.1; bash, tar, and zip. Do not add ReactiveUI, CommunityToolkit, an image library, DI container, or logging package.

**Planning baseline verified 2026-09-01:** This checkout precedes Parts 1–3: only the Godot project is presently in `Goose2ClientGodot.sln:1-18`; `Goose2ClientGodot.csproj:1-13` targets net8.0 with SDK recursive source inclusion; test/package conventions are at `tests/Goose2Client.Tests/Goose2Client.Tests.csproj:1-17`; current local release behavior is in `build.sh:1-151`. Parts 1–3 plans are currently untracked and must be executed first. A net8 compile probe restored and compiled Avalonia 11.3.20 desktop, storage picker, window/dialog, pointer capture, bitmap, clipping, nearest-neighbor, and menu hot-key APIs with zero warnings/errors. NuGet reported 11.3.20 as the latest Avalonia 11 release during planning. Do not substitute Avalonia 12.

**Design sources:** `docs/plans/2026-09-01-portable-map-editor-design.md`, `docs/plans/2026-09-01-portable-map-editor-part1-core-codec.md`, `docs/plans/2026-09-01-portable-map-editor-part2-editing.md`, and `docs/plans/2026-09-01-portable-map-editor-part3-rendering-assets.md`.

---

## Prerequisites and scope boundaries

Parts 1–3 must be complete and green. In particular, consume their locked `MapFileStore`, `MapEditSession`, `SpriteManifest`, `SpriteAssetCache`, `MapRenderer`, `ViewportTransform`, and operation interfaces without renaming or widening them, except for the narrowly scoped unavailable-cache prerequisite specified below.

In scope:

- App/test project/package/solution setup and classic desktop lifetime.
- Main menu, toolbar, palette, canvas, layers/properties, and status layout.
- Avalonia PNG image loader and synchronous Part 3 drawing sink.
- Pointer paint/erase/eyedrop/toggle, interpolation through Part 2, pan, cursor zoom, hover/selection, and keyboard shortcuts.
- New/Open/Save/Save As, external-change confirmation, dirty prompts, and close re-entry handling.
- Asset-directory selection/reload and settings in per-user OS-standard configuration storage.
- Separate map/asset/settings/filesystem errors without app termination.
- Headless integration tests, local headed smoke instructions, and local cross-RID archives.

Out of scope:

- CI, signing, notarization, installers, package managers, automatic updates, or checksums.
- Godot UI/rendering changes, converter changes, generated/proprietary assets, or bundling an asset directory.
- Autosave/recovery, file watching, merge, resize, fill/rectangle tools, selection/clipboard, search/favorites, screenshot goldens, or arbitrary zoom.
- Background rendering/loading. All App, bitmap, cache publication, and disposal work is on the Avalonia UI thread.

The editor must run and edit/save maps with no valid art directory. Part 3 requires a concrete `SpriteAssetCache` backed by a loaded manifest, including a converter-valid empty manifest, but that still represents an available asset source rather than unavailable configuration. Before Task 3, make one narrow Rendering prerequisite extension: add `SpriteResolutionStatus.AssetsUnavailable`, `SpriteAssetCache.CreateUnavailable()`, and cache-level `IsAvailable`, `MaxFrameWidth`, and `MaxFrameHeight` values; make `Manifest` nullable only when `IsAvailable` is false; and make `MapRenderer` consume the cache-level maxima rather than dereferencing `Manifest`. Real caches expose their manifest maxima; unavailable caches expose 32 for both maxima so placeholder-cell culling remains correct. In unavailable mode `Resolve` returns `Empty` only when `Graphic == 0` and returns `AssetsUnavailable` with no image for every other reference, including every signed or otherwise potentially valid `(sheet, graphic)` pair. It performs no loader call and reserves no sentinel pair or synthetic frame. Unavailable-cache disposal and post-disposal exceptions follow the existing cache contract. `AssetContext.CreateUnavailable` wraps that mode, exposes an empty palette, and is replaced by a successfully loaded context. Existing real-cache resolution, disposal, and renderer behavior remain unchanged; add the prerequisite API and regression tests in Task 3 rather than altering the Part 3 plan retroactively.

---

## Verified APIs and repository facts

| Fact/API | Exact citation and consequence |
|---|---|
| Three-project architecture and custom canvas | Design `:13-23`; App references Core/Rendering, never Godot. |
| Main regions and initial tools | Design `:73-90`; XAML must expose all regions and exactly Pencil/Eraser/Eyedropper/Blocked Toggle. |
| Dirty prompts and standard shortcuts | Design `:92-94`; controller prompts before replacement/close and window uses platform primary modifiers. |
| Asset setting and missing-art behavior | Design `:56-71,98-104`; settings failure/art failure cannot block map I/O. |
| Local build requirements | Design `:127-139`; publish current dirty tree, self-contained four RIDs, no Godot/assets, preserve successful output. |
| Core save/open publication boundary | Part 1 `:171-206`; assign path/revision and call `MarkSaved` only after `Save` returns. |
| Session stroke/history restrictions | Part 2 `:135-176`; UI must complete/cancel a stroke before save/undo/replacement and invalidate after eager mutation. |
| Rendering image/cache publication boundary | Part 3 section “Cache ownership and publication boundary”; swap complete context on UI thread, invalidate, then dispose old after synchronous rendering. |
| Renderer App adapter surface | Part 3 section “Draw operation and renderer surface consumed by Part 4”; App implements only image, loader, and draw sink adapters. |
| Native storage picker contracts | Avalonia 11.3.20 source [`IStorageProvider.cs:9-49`](https://github.com/AvaloniaUI/Avalonia/blob/984c3b833f29806eb159fb7f12a6132e936b75df/src/Avalonia.Base/Platform/Storage/IStorageProvider.cs#L9-L49): open/folder cancellation is an empty list; save cancellation is null. Use `IStorageProvider`, not removed legacy dialogs. |
| Modal and close APIs | Avalonia source [`Window.cs:500-529,656-681`](https://github.com/AvaloniaUI/Avalonia/blob/984c3b833f29806eb159fb7f12a6132e936b75df/src/Avalonia.Controls/Window.cs#L500-L529): `Closing`, `Close`; typed `ShowDialog<TResult>(owner)` is the modal result boundary. |
| Pointer coordinates/capture | Avalonia source [`PointerEventArgs.cs:51-120`](https://github.com/AvaloniaUI/Avalonia/blob/984c3b833f29806eb159fb7f12a6132e936b75df/src/Avalonia.Base/Input/PointerEventArgs.cs#L51-L120) and [`Pointer.cs:49-109`](https://github.com/AvaloniaUI/Avalonia/blob/984c3b833f29806eb159fb7f12a6132e936b75df/src/Avalonia.Base/Input/Pointer.cs#L49-L109): use `GetPosition/GetCurrentPoint` and `e.Pointer.Capture(control/null)`. |
| Drawing and clipping | Avalonia source [`DrawingContext.cs:71-140,317-343`](https://github.com/AvaloniaUI/Avalonia/blob/984c3b833f29806eb159fb7f12a6132e936b75df/src/Avalonia.Base/Media/DrawingContext.cs#L71-L140) and [`DrawingContext.cs:317-343`](https://github.com/AvaloniaUI/Avalonia/blob/984c3b833f29806eb159fb7f12a6132e936b75df/src/Avalonia.Base/Media/DrawingContext.cs#L317-L343): sink uses `DrawLine`, `DrawRectangle`, and disposable `PushClip`. |
| Image source/destination and ownership | Avalonia source [`Bitmap.cs:53-92,134-142`](https://github.com/AvaloniaUI/Avalonia/blob/984c3b833f29806eb159fb7f12a6132e936b75df/src/Avalonia.Base/Media/Imaging/Bitmap.cs#L53-L92): `Bitmap(Stream)`, `PixelSize`, and `Dispose`; [`DrawingContext` API](https://github.com/AvaloniaUI/Avalonia/blob/984c3b833f29806eb159fb7f12a6132e936b75df/src/Avalonia.Base/Media/DrawingContext.cs#L43-L63) accepts source and destination rectangles. |
| Nearest-neighbor control | Avalonia source [`RenderOptions.cs:13-31`](https://github.com/AvaloniaUI/Avalonia/blob/984c3b833f29806eb159fb7f12a6132e936b75df/src/Avalonia.Base/Media/RenderOptions.cs#L13-L31): call `RenderOptions.SetBitmapInterpolationMode(canvas, BitmapInterpolationMode.None)`. |
| Invalid Skia image data | Avalonia source [`ImmutableBitmap.cs:23-41`](https://github.com/AvaloniaUI/Avalonia/blob/984c3b833f29806eb159fb7f12a6132e936b75df/src/Skia/Avalonia.Skia/ImmutableBitmap.cs#L23-L41): decode failure is `ArgumentException`; loader maps that to `InvalidData`. |
| Headless input APIs | Avalonia source [`HeadlessWindowExtensions.cs:44-122`](https://github.com/AvaloniaUI/Avalonia/blob/984c3b833f29806eb159fb7f12a6132e936b75df/src/Headless/Avalonia.Headless/HeadlessWindowExtensions.cs#L44-L122): tests may issue key, mouse, and wheel events without screenshot assertions. |
| Desktop startup pattern | Avalonia source [`SingleProjectSandbox/Program.cs:6-17`](https://github.com/AvaloniaUI/Avalonia/blob/984c3b833f29806eb159fb7f12a6132e936b75df/samples/SingleProjectSandbox/Platforms/Desktop/Program.cs#L6-L17) and [`App.axaml.cs:13-29`](https://github.com/AvaloniaUI/Avalonia/blob/984c3b833f29806eb159fb7f12a6132e936b75df/samples/SingleProjectSandbox/App.axaml.cs#L13-L29): `[STAThread]`, `UsePlatformDetect`, `StartWithClassicDesktopLifetime`, and `IClassicDesktopStyleApplicationLifetime.MainWindow`. |
| Existing build script hazards | `build.sh:88-151` wipes `build/` and deletes staging on exit; the editor script must be separate and must preserve old artifacts/failing staging. |
| Source glob hazard | `Goose2ClientGodot.csproj:7-12`; Part 1’s required `<Compile Remove="src/**" />` must still prevent App/Rendering sources entering the Godot assembly. |
| Comment policy | `AGENTS.md:3-27`; add no comments/doc strings unless the “why” exception applies. |

---

## Locked App contracts and behavior

### Projects and composition

Create `src/MapEditor.App/MapEditor.App.csproj` as `WinExe`, net8.0, nullable and implicit usings enabled, with `Avalonia.Desktop`, `Avalonia.Themes.Fluent`, and `Avalonia.Fonts.Inter` pinned to 11.3.20 and project references to Core/Rendering. Set `PublishTrimmed=false` and `UseAppHost=true`; do not set `SelfContained` or a runtime identifier in the RID-less project. Only `build-map-editor.sh` requests self-contained output, by passing `--self-contained true` to each `dotnet publish` invocation.

`tests/MapEditor.App.Tests` targets net8.0 and references App/Core/Rendering. Pin `Avalonia.Headless.XUnit` 11.3.20 and repository xUnit/Test SDK versions. Disable parallelization and use a test `Application`/`AppBuilder.UseHeadless`; only tests requiring controls use `[AvaloniaFact]`.

`Program` performs no service lookup. Task 1 implements only the classic desktop lifetime, Fluent resources, and settings types; `App` must compile and initialize without mentioning `MainWindow`, `EditorDocumentController`, `MainWindowViewModel`, dialogs, or `AssetContext`, which do not exist yet. Task 4 completes `App.OnFrameworkInitializationCompleted`: compose the settings store, file store, unavailable asset context, controller, view model, dialogs, and `MainWindow`, then set `desktop.MainWindow`. `MainWindow.Opened` loads settings and, if no usable asset root is published, offers the folder picker once. Cancellation leaves the unavailable context active. The full startup-composition integration test belongs to Task 4.

### Document/controller surface

Use internal App types (visible to App tests):

```csharp
internal sealed class EditorDocument
{
    internal MapEditSession Session { get; }
    internal string? Path { get; }
    internal MapFileRevision? Revision { get; }
}

internal interface IEditorDialogs
{
    Task<NewMapRequest?> ShowNewMapAsync();
    Task<DirtyChoice> ShowDirtyAsync(string displayName);
    Task<ExternalChangeChoice> ShowExternalChangeAsync(string path);
    Task<string?> PickOpenMapAsync();
    Task<string?> PickSaveMapAsync(string suggestedName);
    Task<string?> PickAssetDirectoryAsync();
    Task ShowErrorAsync(ErrorPresentation error);
}
```

`EditorDocumentController` starts with `MapDocument.Create()` in a session with `initiallyDirty:true`, no path/revision, and empty history. It exposes async New/Open/Save/Save As/close guard plus synchronous Undo/Redo. It emits one `StateChanged` after any effective publication/history/dirty change. No command mutates path/revision before success.

Dirty choice is Save/Discard/Cancel. New first gets validated dimensions, then resolves dirty state, constructs the complete new session locally, and publishes once. Open gets a path, resolves dirty state, calls `MapFileStore.Open` locally, then publishes a clean session/path/revision once. Cancellation or any failure keeps the prior object identity.

Save uses current path/revision; no path delegates to Save As. Save As uses a picker with `.bytes` default extension and overwrite prompt. A different destination passes no expected revision; selecting the current normalized path preserves the expected revision guard. After `MapFileStore.Save` returns: publish returned path/revision, then call `Session.MarkSaved`, then notify. On `MapExternalChangeException`, offer Overwrite/Save As/Cancel; Overwrite deliberately retries with `expectedRevision:null` only after explicit confirmation. Any canceled/failed retry leaves metadata and dirty state unchanged.

Close handling is asynchronous: the first `Closing` event always sets `e.Cancel=true`; a `_closeGuardRunning` flag prevents duplicate prompts. If guard succeeds, set `_closeApproved=true` and call `Close()` again; the second event does not cancel. A dialog failure is presented and close remains canceled.

### View model and lifecycle propagation

`MainWindowViewModel` implements `INotifyPropertyChanged` directly. It owns no duplicate map cells/history/path/revision. It exposes active tool/layer/brush, sheet/frame selector data, five visibility values, grid/blocked options, hover/selection, zoom, dimensions/coordinates, title, and command availability. Every setter validates before mutation and raises only affected properties.

All map writes flow through `MapEditSession`. After Begin/Continue, completion/cancel, undo/redo, palette/brush selection, viewport change, options change, document replacement, or asset-context replacement, the adapter calls a single `Refresh(EditorRefresh flags)` path. That path updates status/commands/title as needed and invokes canvas/palette invalidation; it never reconstructs the renderer for a map edit.

### Asset settings and publication

Create `SettingsPathResolver` with testable inputs. Actual locations are:

| OS | Settings path |
|---|---|
| Windows | `%APPDATA%/Goose2MapEditor/settings.json` |
| macOS | `~/Library/Application Support/Goose2MapEditor/settings.json` (from `SpecialFolder.ApplicationData`) |
| Linux | `$XDG_CONFIG_HOME/goose2-map-editor/settings.json` when absolute/nonempty, else `~/.config/goose2-map-editor/settings.json` |

Settings JSON contains only `assetDirectory` in v1. Missing file means unset. Malformed JSON/type/path yields a typed `AppSettingsException`, is shown nonfatally, and does not overwrite the file. `AppSettingsStore` accepts a narrow internal file-operations seam in tests and uses real `System.IO` operations in production so failures can be injected at temp write and replacement boundaries. Save uses a process-safe local temp-and-replace sequence, scoped to failures this process observes before replacement: create the directory, write the complete JSON to a uniquely named same-directory temp, close it, then call `File.Move(temp, path, overwrite:true)`. An exception before the replacement call leaves an existing settings file untouched; cleanup targets only that invocation's temp path and never the destination. This avoids publishing a partially serialized file from this process during ordinary operation, but it does not claim fsync/power-loss durability, universal atomicity on every filesystem, preservation after every failure once the platform replacement has begun, or coordination with another editor process.

`AssetContext.TryOpen` creates `SpriteAssetCache.Open(fullPath, new AvaloniaSpriteSheetLoader())` and `MapRenderer` locally. Publish only after manifest validation. On success update view model sheet IDs/frames, invalidate canvas/palette, attempt to persist settings, and dispose the old context even if the nonfatal settings write reports an error; the newly loaded context remains active. Because render callbacks and swaps are UI-thread synchronous, no callback can retain an old borrowed bitmap. Manifest/open failure preserves the old context and setting. Lazy PNG failure remains a Part 3 placeholder.

`AvaloniaSpriteSheetLoader` opens a `FileStream` and constructs `Bitmap(stream)`: missing path -> `NotFound`; `ArgumentException` -> `InvalidData`; unauthorized or other `IOException` -> `Unreadable`; success transfers one `AvaloniaSpriteSheetImage`. Do not catch programmer/fatal failures.

### Canvas and palette interaction

`AvaloniaMapDrawSink` converts Part 3 geometry/colors to Avalonia values. It accepts only `AvaloniaSpriteSheetImage`, uses `DrawImage(bitmap, source, destination)`, draws placeholder fill/stroke plus both diagonals, and executes overlay/grid operations exactly as received. `MapCanvas.Render` pushes `new Rect(Bounds.Size)`, creates one sink, and calls the current renderer synchronously. Set bitmap interpolation to `None` in the constructor.

`MapCanvas` is focusable and owns only viewport/input gesture state:

- Left press in-map starts the selected Part 2 tool, captures the pointer, updates selected tile, applies the first eager sample, refreshes, and marks handled.
- Move updates hover. During an edit, valid in-map endpoints call `ContinueStroke`; out-of-map points do nothing and do not become anchors, so re-entry interpolates from the last valid sample.
- Left release completes the stroke even if outside, releases capture, and refreshes once. Escape/canceled document operation cancels and restores it. Unexpected capture loss completes an effective stroke so visible work is not silently discarded.
- Middle drag, or Space+left drag, pans with `PanByScreenDelta`; it never starts a stroke. Wheel Y>0/Y<0 performs one `ZoomIn`/`ZoomOut` step at the pointer using `ZoomAt`. Min/max remain unchanged.
- Before Save/New/Open/Undo/Redo/close, `MainWindow` calls `FinishInteraction(commit:true)`; this satisfies Part 2’s active-stroke precondition. Escape uses `commit:false`.
- Window shortcuts use Meta on macOS and Control elsewhere: N, O, S, Shift+S, Z; redo is Meta+Shift+Z on macOS and Control+Y plus Control+Shift+Z elsewhere. Unmodified P/E/I/B select tools; +/- zoom around canvas center; Space is reserved for pan. Set matching `MenuItem.HotKey` values in code so displayed gestures equal handling.

`SpritePaletteControl` is a directly drawn, explicitly scrollable viewport rather than a tall control merely clipped by a `ScrollViewer`. It owns a clamped vertical `Offset`, computes `ExtentHeight` from frame count, column count, and fixed thumbnail cell size, derives `ViewportHeight` from `Bounds.Height`, handles wheel scrolling, and synchronizes an adjacent vertical `ScrollBar` (`Value`, `Maximum`, and `ViewportSize`) used by the left pane. Width/frame/sheet changes recompute columns and extent and clamp the offset. Render and hit testing use the same offset; rendering enumerates only rows intersecting `[Offset, Offset + ViewportHeight)` plus at most one partially visible row on each edge. It draws directly from the selected sheet’s sorted `SpriteFrame` list, resolves only those thumbnails, uses source rectangles/nearest-neighbor, and emits no child control per frame. Left click selects the exact `(sheet,graphic)` brush. Missing images show the same conspicuous colors. Changing sheets does not mutate map/history/dirty state. Large-palette tests use tens of thousands of frames, scroll to beginning/middle/end, and prove resolver calls are bounded by `columns * (ceiling(viewportHeight / cellHeight) + 2)` per render and the underlying selected-sheet loader is called at most once, independent of total frame count.

### Main layout

`MainWindow.axaml` uses a DockPanel: File/Edit menus; toolbar for New/Open/Save, Undo/Redo, four tool toggles, zoom; status bar at bottom. The body is a three-column Grid:

1. Left (260 px): asset-directory/reload button, sheet ComboBox, viewport-height `SpritePaletteControl` with its synchronized vertical `ScrollBar`, editable signed-int brush Sheet/Graphic fields.
2. Center: bordered `MapCanvas` filling available space.
3. Right (260 px): active-layer radio buttons 0–4, independent visibility checkboxes 0–4, grid/blocked checkboxes, and selected-tile readout (coordinates, flags, blocked, all five numeric references).

Status shows hover coordinates or `—`, fixed zoom percent, and `width × height`. Window title is `Goose2 Map Editor — <name>` with `*` iff dirty. Numeric brush fields reject invalid `Int32` text without changing the prior brush and show an inline validation state; codec sheet-range errors remain save-time errors as required by Part 1.

### Error presentation

| Failure | User presentation and state |
|---|---|
| `MapFormatException` | “Open map” dialog names path and typed format reason; active document unchanged. |
| `MapValidationException` | “Save map” dialog names path and invalid numeric value; dirty/path/revision unchanged. |
| `MapExternalChangeException` | Dedicated Overwrite/Save As/Cancel dialog; never silently overwrite. |
| `SpriteManifestException` | “Load assets” dialog names manifest/property/path; old asset context remains. |
| Assets unavailable or expected sheet failure | Canvas/palette placeholder; unavailable mode has no palette frames, no modal per reference, and map remains saveable. |
| Settings read/write failure | “Settings” dialog with settings path; current document/assets remain usable. |
| Unauthorized/IO failure | Operation-specific dialog names path; no app termination/state publication. |
| Unexpected UI command exception | Last-resort error dialog at the async event boundary; do not catch OOM/stack overflow/process-corruption failures. |

---

## Mutation and UI lifecycle impact

| Mutation/publication | Writer | Required propagation | Must not change |
|---|---|---|---|
| Pending/effective stroke | Canvas -> session | Invalidate canvas; refresh dirty/title/undo after completion | Asset cache, path/revision, visibility |
| Undo/redo | Controller/session | Finish pointer; repaint; refresh title/commands/properties | Viewport/assets/path/revision |
| New/open | Controller | Publish one new `EditorDocument`; reset hover/selection/viewport; repaint | Old state on cancellation/failure |
| Save success | Store then controller | Revision/path, `MarkSaved`, title/commands | Document/history contents |
| Save failure/conflict cancel | Store/dialog | Error only | Path/revision/dirty/history/destination |
| Pan/zoom/hover | Canvas/view model | Repaint/status | Document/history/dirty/assets |
| Tool/layer/brush | View model/palette | Toggle/readout refresh | Document/history/dirty until stroke |
| Visibility/overlays | View model | Replace render options; repaint | Document/assets/history/dirty |
| Asset context | Asset controller | Publish complete context, refresh selector/repaint, dispose old | Document/history/path/revision |
| Settings path | Settings store | Persist only validated selected root | Current context if write fails |
| Window close | Closing adapter | Cancel first close; prompt/save; re-close once | Process/window on cancel/failure |
| Build publication | Build script | Rename complete staged set | Prior release sets on any failure |

---

## Task 1: Scaffold independently compiling Avalonia startup and OS-standard settings

**Files:**
- Create: `src/MapEditor.App/MapEditor.App.csproj`, `Program.cs`, `App.axaml`, `App.axaml.cs`, `Properties/AssemblyInfo.cs`
- Create: `src/MapEditor.App/Settings/AppSettings.cs`, `SettingsPathResolver.cs`, `AppSettingsStore.cs`, `AppSettingsException.cs`, `ISettingsFileOperations.cs`
- Create: `tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj`, `AssemblyInfo.cs`, `TestApplication.cs`, `SettingsTests.cs`
- Modify: `Goose2ClientGodot.sln`

1. Confirm Parts 1–3 files/tests and the Part 1 `src/**` Godot exclusion. Add App/App.Tests to the solution.
2. Write red tests for every OS path row, XDG relative fallback, missing/malformed settings, round trip, unique temp cleanup, injected temp-write and pre-replacement failures preserving old settings, and injected replacement errors being typed/caller-presentable. Do not simulate or claim power-loss durability or concurrent-process serialization.
3. Red: `dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj --filter "FullyQualifiedName~SettingsTests" -v minimal`.
4. Add pinned packages/projects and implement only `Program`, basic `App` resource/lifetime initialization, and settings contracts. The scaffold must not reference or stub future `MainWindow`, controller, view-model, dialog, or asset-context types. Do not add a service locator/static mutable singleton.
5. Green, then `dotnet build src/MapEditor.App/MapEditor.App.csproj -v minimal` and `dotnet build Goose2ClientGodot.sln -v minimal`.
6. Commit: `feat(map-editor): scaffold Avalonia app and settings`.

**Invariant matrix:** standard paths -> path table tests; malformed settings never rewritten -> read-failure tests; failures before replacement leave destination bytes unchanged and clean only the owned temp -> save-failure tests; no crash-durability/cross-process guarantee -> contract/source audit; App is only Avalonia dependency -> project/grep audit; Task 1 has no future composition dependencies -> source audit and standalone App build.

---

## Task 2: Implement document lifecycle, dialogs, errors, and view-model state

**Files:**
- Create: `src/MapEditor.App/Documents/EditorDocument.cs`, `EditorDocumentController.cs`
- Create: `src/MapEditor.App/ViewModels/ViewModelBase.cs`, `MainWindowViewModel.cs`
- Create: `src/MapEditor.App/Dialogs/IEditorDialogs.cs`, `AvaloniaEditorDialogs.cs`, `NewMapDialog.axaml(.cs)`, `ChoiceDialog.axaml(.cs)`, `ErrorDialog.axaml(.cs)`, `ErrorPresentation.cs`
- Create: `tests/MapEditor.App.Tests/EditorDocumentControllerTests.cs`, `MainWindowViewModelTests.cs`, `Fakes/FakeEditorDialogs.cs`

1. Write red controller tests for startup, valid/invalid New, dirty Save/Discard/Cancel, successful/failed Open identity, Save-vs-Save-As, canceled picker, save success ordering, validation/IO failure, external-change three choices, undo/redo, and title `*` transitions.
2. Red focused command.
3. Implement controller against Part 1/2 only. Keep dialog results explicit enums; no Boolean that conflates Discard and Cancel. Catch/classify only at operation boundary.
4. Add modal Avalonia dialogs using `ShowDialog<TResult>(owner)` and storage pickers with map filters/default extension/overwrite prompt. Extract desktop local path then dispose storage items.
5. Green focused/full App tests.
6. Commit: `feat(map-editor): add guarded document lifecycle`.

**Invariant matrix:** open/new publish only after success -> identity tests; save metadata/savepoint only after store return -> failure/success ordering tests; dirty cancellation blocks replacement/close -> choice matrix; external overwrite is explicit -> conflict tests; VM property refresh is narrow -> property-change tests.

---

## Task 3: Add Avalonia assets, drawing sink, and canvas/palette interactions

**Files:**
- Modify prerequisite: `src/MapEditor.Rendering/Assets/SpriteResolution.cs`, `SpriteAssetCache.cs`, `src/MapEditor.Rendering/Rendering/MapRenderer.cs`
- Modify prerequisite tests: `tests/MapEditor.Rendering.Tests/SpriteAssetCacheTests.cs`, `MapRendererTests.cs`
- Create: `src/MapEditor.App/Rendering/AvaloniaSpriteSheetImage.cs`, `AvaloniaSpriteSheetLoader.cs`, `AvaloniaMapDrawSink.cs`, `AssetContext.cs`, `AssetContextController.cs`
- Create: `src/MapEditor.App/Controls/MapCanvas.cs`, `SpritePaletteControl.cs`
- Create: `tests/MapEditor.App.Tests/AvaloniaSpriteSheetLoaderTests.cs`, `AssetContextControllerTests.cs`, `AvaloniaMapDrawSinkTests.cs`, `MapCanvasTests.cs`, `SpritePaletteControlTests.cs`
- Add temporary PNG/manifest helpers under `tests/MapEditor.App.Tests/Fixtures/` only if generated at test runtime; do not commit converter output.

1. Red Rendering prerequisite tests: unavailable cache has no manifest/sheets, reports `IsAvailable == false`, returns `Empty` for every graphic-zero reference, returns `AssetsUnavailable` for representative positive/zero/negative-sheet and positive/negative-graphic nonempty references, invokes no loader, emits placeholders through `MapRenderer`, and reserves no pair that can resolve `Ready`. Existing real-cache and disposal tests remain green.
2. Red loader/ownership tests: exact path/status, invalid PNG, dimensions, one disposal, failed context preserves old, success swaps/disposes after publication, settings failure keeps the newly loaded context while disposing the replaced one, and unavailable context exposes an empty palette and placeholders for all nonempty references.
3. Red sink tests with a recording/fake DrawingContext seam: exact source/destination conversion, colors, both placeholder diagonals, clipping, nearest setting, and foreign image rejection. Do not test screenshots.
4. Red headless canvas tests using Avalonia’s `MouseDown/Move/Up/MouseWheel/KeyPressQwerty`: one-cell stroke, sparse drag, release outside, out/re-entry anchor, blocked loop once, eyedrop, Escape cancel, capture-loss complete, middle/Space pan, cursor zoom/clamps, hover/selection, and session replacement reset.
5. Red palette tests: sorted frames, exact click after scrolling, sheet switch no dirty, offset/extent/viewport clamping after resize and sheet changes, scrollbar and wheel synchronization, and no child-per-frame growth. Render a sheet with tens of thousands of frames at beginning/middle/end; assert per-render resolver calls satisfy the viewport-row bound and backend sheet-loader calls remain at most one rather than growing with frame count.
6. Implement the narrow Rendering prerequisite, adapters, and controls exactly to locked behavior; all invalidation is UI-thread synchronous. The palette owns its viewport/offset and is not made “virtual” merely by clipping a full-height control in a `ScrollViewer`.
7. Green focused/full App and Rendering tests; audit that the prerequisite adds no Avalonia/App type to Rendering and no App type leaks into Core/Rendering.
8. Commit: `feat(map-editor): add unavailable assets and Avalonia rendering input adapters`.

**Invariant matrix:** unavailable means every nonempty pair without synthetic manifest data -> Rendering/cache tests; borrowed bitmaps die only with context -> ownership tests; render calls are synchronous/no retained operation -> sink/control audit; pointer drag remains one Part 2 command -> canvas undo test; out-of-map never mutates -> re-entry test; pan/zoom never dirty -> state test; palette selection never edits -> palette test; palette work/loader calls are viewport-bounded for large sheets -> offset/scroll/bounded-call tests.

---

## Task 4: Compose main window, controls, shortcuts, and close lifecycle

**Files:**
- Create: `src/MapEditor.App/Views/MainWindow.axaml`, `MainWindow.axaml.cs`
- Create: `tests/MapEditor.App.Tests/MainWindowTests.cs`, `MainWindowCloseTests.cs`, `ShortcutTests.cs`, `AppStartupTests.cs`
- Modify: `src/MapEditor.App/App.axaml.cs`

1. Write headless red tests asserting named menu/layout regions, five active/visibility controls, four tool controls, overlays, status fields, selection readout, palette scrollbar/viewport wiring, command enablement, platform hot keys, and title state.
2. Write red close tests for clean close, Save/Discard/Cancel, save cancellation/failure, duplicate Closing while a prompt is pending, and approved second-close without recursion.
3. Write red shortcut tests for primary-modifier File commands, both redo conventions where applicable, P/E/I/B, +/- zoom, and command-before-stroke completion.
4. Write the deferred full startup integration test: classic desktop initialization creates exactly one `MainWindow`, one initially dirty new session, and one unavailable context; all composed controller/view-model/dialog/context references are the same instances used by the window; Opened loads settings and offers the asset picker at most once when no usable context is published.
5. Implement final `App` composition and XAML/code-behind. Code-behind is an Avalonia event adapter only; lifecycle decisions remain controller/view model methods. This is the first task that may reference every composed type from `App.axaml.cs`.
6. Green App tests; run full solution tests/build and XAML compile in Release.
7. Commit: `feat(map-editor): compose desktop startup and editor window`.

**Invariant matrix:** all approved regions/tools are reachable -> visual-tree tests; full composition is deferred until every dependency exists -> startup integration test; close cannot bypass dirty guard -> close matrix; displayed hotkey equals routed action -> shortcut tests; active stroke is terminal before lifecycle/history action -> shortcut stroke tests; selected properties reflect fresh document after edit/undo -> lifecycle tests.

---

## Task 5: Add dirty-tree local publisher with preserved staging

**Files:**
- Create: `build-map-editor.sh` (mode 755)
- Create: `tests/build-map-editor-script-tests.sh` (mode 755)
- Create: `src/MapEditor.App/Packaging/macos/Info.plist.template`
- Modify: `.gitignore` only if a narrower staging rule is needed (existing `/build/` already covers output)

Script contract:

```text
./build-map-editor.sh [--skip-tests] [linux-x64|windows-x64|osx-x64|osx-arm64 ...]
```

No RID means all four; an explicit list may select one or more platforms, duplicates run once, and unknown options fail. An all-platform invocation means either no RID arguments or an explicit request containing all four RIDs; it must execute all four real `dotnet publish` commands and cannot silently skip an unavailable runtime pack/platform. A selected single-platform invocation may publish only that RID. The script requires dotnet/git/tar/zip, but never Godot or generated assets. It intentionally accepts dirty/untracked state and marks build id `<UTC>-<short-sha>-dirty` when `git status --porcelain` is nonempty. It invokes `dotnet publish` directly on the current App project, so uncommitted source/XAML is included.

Unless skipped, run the three editor suites explicitly (Core, Rendering, App), not the Godot tests. For every requested RID invoke `dotnet publish src/MapEditor.App/MapEditor.App.csproj -c Release -r <rid> --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false` into `build/map-editor/.staging/<build-id>.<random>/publish/<rid>` and retain per-RID logs. `--self-contained true` appears only here, never in the RID-less project. Linux archives a folder with tar.gz; Windows uses zip; each macOS publish is placed in `Goose2MapEditor.app/Contents/MacOS`, with executable name matching `CFBundleExecutable` and generated `Info.plist`, then zipped. No signing step.

Create every requested nonempty archive and inspect it before publication: run `tar -tzf` or `unzip -t`, list entries, and assert the RID-specific app host, `.deps.json`, `.runtimeconfig.json`, and expected Linux/Windows/macOS bundle layout are present; assert Linux/macOS app hosts retain executable mode and no Godot binary or asset directory was added. For an all-platform invocation, all four publish logs and all four inspected archives are mandatory before the staged release can be renamed. Remove raw publish intermediates only after all requested RIDs succeed. Put the complete requested archive set plus `BUILD-METADATA.txt` recording the exact RIDs in a staged release directory, then perform one same-filesystem directory rename to `build/map-editor/<build-id>/`. Never remove/overwrite any prior release directory. On any test/publish/archive/inspection/rename failure, exit nonzero, print the preserved staging/log path, and leave previous releases untouched. On success remove only that run’s empty staging shell and print artifact sizes.

1. Write shell tests with a temporary git repo and fake `dotnet/tar/zip`: dirty accepted; no-argument and explicit-all invocations require exactly all four RIDs; one-RID selection publishes only one; filtering/dedup; exact `--self-contained true`; tests vs skip; a missing/failing fourth publish preserves old release and stage/logs; archive or required-entry inspection failure publishes nothing; successful one-rename requested set; mac app structure; and no Godot/assets lookup.
2. Red: `bash tests/build-map-editor-script-tests.sh`.
3. Implement script/template; `bash -n` both scripts and make executable.
4. Green shell tests.
5. Mandatory real all-platform gate: run `./build-map-editor.sh --skip-tests` with real `dotnet`, producing all four RIDs in that one invocation. Inspect all four retained publish logs and archive listings/tests, including app hosts, runtime files, bundle layout, and executable modes. Failure to acquire any runtime pack fails and preserves staging; it is not a reason to reduce this gate to Linux. Extract and launch the Linux x64 artifact on a Linux development host as a separate host smoke. Windows x64 and macOS x64/arm64 launches remain separate target-host smoke checks and are not implied by successful cross-publish/archive inspection. A developer intentionally requesting one RID outside this all-platform gate may publish and inspect only that RID.
6. Commit: `build(map-editor): publish inspected self-contained desktop archives`.

**Invariant matrix:** dirty state included -> fake log/build ID test; all-platform mode executes four exact real publish RIDs -> invocation tests plus mandatory real gate/log audit; selected one-platform mode executes only its RID -> selection test; self-contained is publish-only -> csproj/script audit; every requested archive is inspected before publication -> injected entry/mode failures; no Godot/assets -> PATH/sentinel and archive-entry tests; any failure preserves old output and stage -> injected failures; only the complete requested set is visible -> rename test; mac architectures are separate unsigned app archives -> structure/RID tests; target-host launching is separate -> smoke document/run log.

---

## Task 6: Document and run the local smoke path

**Files:**
- Create: `docs/map-editor-smoke.md`

Document these exact headed checks:

1. `dotnet run --project src/MapEditor.App/MapEditor.App.csproj`; first-launch folder picker appears. Cancel leaves editor usable with placeholders; selecting converter `Assets/Sprites` loads sheets/palette and persists to the OS path.
2. New defaults 100×100; reject 0/1001; verify title dirty marker.
3. Select sheet/frame, paint a fast sparse drag, erase, eyedrop, toggle a loop; verify one undo per drag, redo, unknown flag preservation through save/reopen.
4. Pan with middle and Space+left; wheel and +/- through 25/50/100/200/400; verify cursor anchor and nearest-neighbor edges.
5. Toggle each layer/grid/blocked; verify hover/selected/status and tall bottom-center sprites.
6. Open malformed map and bad asset root; prior document/context remains. Make an external same-length change and verify Overwrite/Save As/Cancel. Inject unwritable destinations/settings and verify state remains usable.
7. Exercise New/Open/close dirty prompts with Save/Discard/Cancel and native keyboard shortcuts on Linux/Windows/macOS.
8. Run the mandatory no-RID all-platform publisher and record inspection of all four archives. Run/extract the local Linux archive; on each target host launch the corresponding Windows x64/macOS x64/macOS arm64 archive. Keep archive inspection and target-host launch results as separate entries. Record that the macOS artifacts are unsigned/unnotarized and may require OS override.

Run final automated gates, including the real all-platform publish gate, then perform the headed checklist on at least the development host. Record date/OS/assets used in the smoke document’s run log without committing generated maps/assets or machine-specific paths.

Commit: `docs(map-editor): add local desktop smoke path`.

---

## Failure behavior checklist

| Operation/failure | Required result |
|---|---|
| First-launch asset picker canceled | Unavailable context remains; map edit/open/save works. |
| Settings malformed/unreadable | Nonfatal settings error; no path accepted or file rewritten. |
| Settings save fails before replacement | Nonfatal settings error; existing destination bytes remain and only this attempt's temp is cleaned. No broader crash/filesystem guarantee is claimed. |
| New dimensions invalid/canceled | Existing document identity/state unchanged. |
| Dirty prompt canceled or Save As canceled | Requested new/open/close aborted. |
| Open decode/I/O failure | Existing document/session/path/revision/viewport retained. |
| Save validation/I/O/replacement failure | Dirty/history/path/revision retained; Part 1 preserves destination. |
| External map change | No overwrite without explicit Overwrite choice. |
| Asset manifest reload failure | Old context/settings remain; new partial cache is disposed. |
| Lazy PNG failure | Negative-cached placeholder, no modal storm/map mutation. |
| Pointer exits canvas | No invalid coordinate sent; prior valid interpolation anchor retained. |
| Pointer capture lost | Effective stroke completed once; pointer state cleared. |
| Renderer/sink throws | Error reaches UI boundary; context/document remain valid and can redraw. |
| Close prompt/error already pending | Additional Closing events canceled; only one dialog. |
| Build test/publish/archive inspection failure | Nonzero exit; staging/logs retained; prior releases unchanged; no partial requested final set. An all-platform run never degrades to fewer than four RIDs. |

---

## Consolidated invariant-to-test matrix

| ID | Invariant | Proof |
|---|---|---|
| A1 | Avalonia is confined to App/App.Tests | project reference and namespace grep |
| A2 | App composes Part 1–3 APIs without duplicate map/render state; the only Rendering change is the unavailable-cache prerequisite | public-shape/source audit and prerequisite regression tests |
| A3 | New/open replacement is transactional | controller identity tests |
| A4 | Save metadata and clean state advance only after replacement | save ordering/failure tests |
| A5 | Dirty prompts guard New/Open/close | choice matrix and close tests |
| A6 | External changes never overwrite silently | conflict-choice tests |
| A7 | Every edit remains one narrow Part 2 stroke/command | canvas/history tests |
| A8 | Pointer leave/re-entry/capture loss are deterministic | headless input tests |
| A9 | Pan/zoom/hit testing use Part 3 transforms at five levels | canvas tests |
| A10 | Draw adapter preserves operations and nearest sampling | sink tests |
| A11 | No control/image exists per map tile or palette frame; palette resolution/loading is viewport-bounded | bounded visual-tree, offset/extent, resolver-call, and loader-call tests |
| A12 | Layer/tool/palette/options do not dirty until map mutation | VM/palette tests |
| A13 | Unavailable assets resolve every nonempty pair without reserving manifest data and never block numeric map persistence | unavailable-cache exhaustive-class and save tests |
| A14 | Asset publication/disposal respects borrowed image lifetime | context ownership tests |
| A15 | Settings use OS-standard user locations and local temp replacement preserves old bytes for failures before replacement without claiming crash durability | settings path/save-failure matrix and contract audit |
| A16 | Errors name operation/path and retain active state | error/controller tests |
| A17 | Platform shortcuts and close re-entry are correct | shortcut/close tests |
| A18 | Build consumes dirty current files, keeps self-contained configuration out of the RID-less project, and includes no Godot/assets | shell tests and csproj/script/archive audit |
| A19 | All-platform invocation really publishes and inspects all four self-contained RIDs; selected-platform invocation may publish one | fake invocation tests, mandatory real publish logs, and archive inspections |
| A20 | Failed requested build leaves old releases and diagnostic stage | injected shell failures, including fourth-RID and inspection failures |

---

## Red-team review

1. Replace the active session/path before `Open` returns; transactional identity tests must fail it.
2. Call `MarkSaved` in `finally` or before `Save` return; validation/conflict tests must catch false clean state.
3. Select the current path in Save As after an external rewrite; expected revision must still guard it.
4. Fire Closing repeatedly while Save As is open; exactly one prompt/picker exists and no recursive close occurs.
5. Trigger keyboard Save during a drag; the stroke must complete once before storage, not throw or split.
6. Drag outside then re-enter far away; interpolation begins at last valid cell and never mutates out of bounds.
7. Cross a blocked-toggle path over itself; Part 2 visited suppression still toggles once.
8. Switch active layer/brush mid-stroke; captured Part 2 values govern that stroke; next stroke sees new values.
9. Pan/zoom/toggle visibility/select palette and compare encoded bytes/history/dirty before/after.
10. Render one visible cell of a 1000×1000 map and scroll a tens-of-thousands-frame palette to beginning/middle/end; reject child/per-tile retained objects, full-map/full-palette scans, resolver calls above the visible-row bound, or sheet-loader calls growing with frame count.
11. Resolve empty and nonempty references across positive, zero, and negative numeric pairs with no assets; reject any synthetic manifest/frame, reserved pair, `Ready` result, or loader call. Then fail a new manifest after valid assets are displayed; old bitmaps/context remain usable and undisposed until failed candidate cleanup.
12. Swap a valid context during repeated renders; all action is UI-thread synchronous, old cache is disposed after publication/invalidation, and no operation is retained.
13. Use missing, corrupt, unreadable, and out-of-bounds PNGs; statuses/placeholders differ without modal storms.
14. Corrupt settings and inject a settings save failure before destination replacement; assets/document remain usable, old settings bytes survive, and only the owned temp is removed. Reject any claim that this proves power-loss durability, all-filesystem atomicity, or cross-process locking.
15. Build Task 1 alone and grep `App.axaml.cs`; reject references or stubs for MainWindow, controller, view model, dialogs, or AssetContext. In Task 4, require the deferred startup test to prove the final shared composition.
16. Inspect File/Edit/tool hot keys on macOS versus Windows/Linux; reject Control-only Mac behavior or mismatched labels.
17. Search Core/Rendering for Avalonia and App for Godot; both must be empty. Audit the Rendering diff so it contains only the unavailable-cache prerequisite and tests.
18. Run publisher from a dirty tree with an untracked XAML change and verify publish reads current project, not `git archive`/HEAD.
19. Search the RID-less csproj for `SelfContained`; reject it. Verify every requested publish command, and only the publish script, passes `--self-contained true`.
20. Invoke the real publisher with no RIDs and inspect logs/archive entries for all four exact RIDs; reject optional runtime-pack wording, fake-only proof, skipped fourth RID, or substituting a Linux-only run. Separately verify a deliberate one-RID invocation publishes only that RID.
21. Inject failure on the fourth RID and during final archive validation; old release sets stay byte-identical and failed stage/logs remain.
22. Search publisher for `rm -rf build`, `git clean`, Godot, asset sentinels, signing, installer, CI, or output overwrite; reject all.
23. Confirm mac archives are separate x64/arm64 `.app` bundles with no universal/signing claim; keep target-host launches separate from cross-publish/archive inspection.
24. Reject scope creep: no resize/fill/selection clipboard/search/autosave/watcher/background loader/CI/installer/signing.
25. Confirm no `bin/obj`, generated assets, temporary settings/maps, build archives, package probes, or machine paths are staged.

Final commands:

```bash
dotnet test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj -v minimal
dotnet test tests/MapEditor.Rendering.Tests/MapEditor.Rendering.Tests.csproj -v minimal
dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj -v minimal
dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj -v minimal
dotnet test Goose2ClientGodot.sln -v minimal
dotnet build Goose2ClientGodot.sln -c Release -v minimal
bash -n build-map-editor.sh
bash tests/build-map-editor-script-tests.sh
./build-map-editor.sh --skip-tests
git grep -nE 'Avalonia|MapEditor.App' -- src/MapEditor.Core src/MapEditor.Rendering || true
git grep -n 'Godot' -- src/MapEditor.App tests/MapEditor.App.Tests || true
git diff --check
git status --short
```

Expected implementation commit sequence:

1. `feat(map-editor): scaffold Avalonia app and settings`
2. `feat(map-editor): add guarded document lifecycle`
3. `feat(map-editor): add unavailable assets and Avalonia rendering input adapters`
4. `feat(map-editor): compose desktop startup and editor window`
5. `build(map-editor): publish inspected self-contained desktop archives`
6. `docs(map-editor): add local desktop smoke path`

Each commit must be independently green at its stated gate. Do not squash unrelated work, do not commit this planning-only request, and do not implement CI/signing/installers.