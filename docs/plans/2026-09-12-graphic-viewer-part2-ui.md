# Graphic Viewer Part 2: Viewer UI Implementation Plan

**Goal:** Add a modeless map-editor window for categorized sheet inspection and animation playback using the Part 1 metadata catalog.

**Architecture:** Load the Part 1 `GraphicAssetCatalog` as optional viewer metadata inside `AssetContext`, while preserving normal editing when the sidecar is absent. Keep navigation, selection, zoom, and playback state in a testable view model; use two focused Avalonia custom controls for full-sheet drawing and animation preview; let `MainWindow` own a singleton modeless viewer.

**Tech Stack:** C# 12, .NET 10, Avalonia 11.3.20, Avalonia Headless, xUnit

---

## Part 1 contract consumed by this plan

Part 2 uses these exact APIs established by Part 1:

```csharp
GraphicAnimationManifest.Load(string assetDirectory)
GraphicAssetCatalog.Create(SpriteManifest sprites, GraphicAnimationManifest animations)
GraphicAssetCatalog.Load(string assetDirectory)
GraphicAssetCatalog.GetSheets(GraphicCategory? category)
GraphicAssetCatalog.GetMappings(int sheet)
GraphicAssetCatalog.GetAnimations(SpriteReference frame)
GraphicAssetCatalog.TryGetAnimation(GraphicAnimationKey key, out GraphicAnimation animation)
```

`null` means the implicit `All` category. `GraphicAnimation` exposes its composite key, 8 FPS rate, and ordered `SpriteReference` frames. `GraphicAnimationManifestException` carries missing, malformed, unsupported-version, and cross-validation failures.

## APIs verified before planning

- The current context is `AssetContextController.Current`: `src/MapEditor.App/Rendering/AssetContextController.cs:33`.
- Context replacement and old-context disposal occur in `TryOpen`: `src/MapEditor.App/Rendering/AssetContextController.cs:66-83`.
- `AssetContext.Create` opens the sprite cache and treats appearance metadata as optional: `src/MapEditor.App/Rendering/AssetContext.cs:51-68`.
- `AssetContext.SheetIds` is tile-filtered and must not drive viewer `All`: `src/MapEditor.App/Rendering/AssetContext.cs:54`.
- `SpriteManifest.GetFrames(int)` supplies graphic-ID-sorted frame rectangles: `src/MapEditor.Rendering/Assets/SpriteManifest.cs:61-62`.
- `SpriteAssetCache.AssetDirectory`, `Manifest`, and `Resolve` provide the current path, all sheets, and lazy animation-frame resolution: `src/MapEditor.Rendering/Assets/SpriteAssetCache.cs:14-19,47-107`.
- `AvaloniaSpriteSheetLoader.Load(string)` returns status/diagnostic results for expected PNG failures: `src/MapEditor.App/Rendering/AvaloniaSpriteSheetLoader.cs:8-34`.
- `AvaloniaSpriteSheetImage` exposes its `Bitmap` and owns idempotent disposal: `src/MapEditor.App/Rendering/AvaloniaSpriteSheetImage.cs:7-46`.
- Existing custom drawing/input patterns are in `SpritePaletteControl.RenderPalette`, `Render`, and `OnPointerPressed`: `src/MapEditor.App/Controls/SpritePaletteControl.cs:108-187`.
- Nearest-neighbor rendering uses `RenderOptions.SetBitmapInterpolationMode(..., BitmapInterpolationMode.None)`: `src/MapEditor.App/Controls/SpritePaletteControl.cs:48`.
- The existing shared `MapZoom` stops at 400%, so it cannot represent the viewer's 800% maximum: `src/MapEditor.Rendering/Viewport/MapZoom.cs:5-23`.
- `MainWindow` already owns `_assets`, creates child views, and disposes assets from its `Closed` handler: `src/MapEditor.App/Views/MainWindow.axaml.cs:38-72,115-120`.
- The menu structure has no Tools menu yet: `src/MapEditor.App/Views/MainWindow.axaml:11-46`.
- Errors are shown through `IEditorDialogs.ShowErrorAsync(ErrorPresentation)`: `src/MapEditor.App/Dialogs/IEditorDialogs.cs:26-43`; `ErrorPresentation` is declared at `src/MapEditor.App/Dialogs/ErrorPresentation.cs:3`.
- Avalonia headless setup is in `tests/MapEditor.App.Tests/TestApplication.cs:6-28`; real-window harness setup is in `tests/MapEditor.App.Tests/MainWindowTests.cs:31-64`.

### Task 1: Publish optional viewer metadata and asset changes

**Files:**
- Create: `src/MapEditor.App/Rendering/GraphicViewerAvailability.cs`
- Modify: `src/MapEditor.App/Rendering/AssetContext.cs`
- Modify: `src/MapEditor.App/Rendering/AssetContextController.cs`
- Modify: `tests/MapEditor.App.Tests/AssetContextControllerTests.cs`
- Modify: `tests/MapEditor.App.Tests/Fixtures/AssetFixture.cs`

**Mutation impact:**
- Source of truth changed: `AssetContextController._current` remains the active asset source; `AssetContext` gains derived viewer catalog/availability state.
- Important readers: existing map canvases and palettes read `Current`; the new viewer subscribes to `CurrentChanged` and reads catalog availability.
- Derived/cached state affected: document sheet lists and render caches continue to update in `TryOpen`; viewer state will be invalidated through the new event.
- Required propagation sequence: construct and fully validate candidate context, assign `_current`, update every document's tile sheet IDs and invalidate canvas/palette, raise one synchronous `CurrentChanged` event whose readers observe the new context, then dispose the replaced context in a `finally` block.
- Invariants to preserve: sidecar failure does not fail normal asset opening; ordinary manifest failure leaves the previous context and emits no event; event subscribers never observe a partially initialized candidate.
- Observable proof required: tests assert final `Current`, document sheet state, availability, event count, and old-context disposal.

**Step 1: Add failing asset-context tests**

Extend the existing fixture to write small valid `manifest.json`, PNGs, and the Part 1 animation sidecar. Add tests proving:

- Valid sidecar produces a non-null `GraphicAssetCatalog` and available status.
- Missing, malformed, unsupported, and cross-invalid sidecars leave `TryOpen` successful and sprite resolution usable, but viewer availability contains the exact typed diagnostic.
- Unavailable contexts report that sprite assets must be loaded.
- Successful replacement raises exactly one event after `Current` is the new context and before the old context is disposed; the old context is disposed immediately afterward.
- A throwing test subscriber still cannot prevent old-context disposal.
- A failed ordinary sprite-manifest open raises no event and preserves the current context.

Expected red failure: no viewer availability or change event exists.

**Step 2: Implement optional catalog loading**

Mirror the existing appearance-availability pattern. `AssetContext.Create` first opens `SpriteAssetCache`, then loads `GraphicAnimationManifest` and calls `GraphicAssetCatalog.Create(cache.Manifest!, animations)` inside a catch for `GraphicAnimationManifestException`. Reusing the already-loaded `SpriteManifest` prevents a second file read from observing a different manifest during replacement. Store:

```csharp
public GraphicAssetCatalog? Graphics { get; }
public GraphicViewerAvailability GraphicViewerAvailability { get; }
```

Do not make sidecar loading part of `SpriteAssetCache.Open`; that would break ordinary map editing with old assets.

Add `public event EventHandler? CurrentChanged` to `AssetContextController`. Publish after the current-context swap and document propagation but before old-context disposal; wrap publication in `try/finally` so `replaced.Dispose()` always runs and a disposal exception cannot suppress notification after `_current` changed. Subscribers consume only `Current`; they must not retain the old context. All operations remain on the Avalonia/UI thread because existing asset-directory commands and viewer subscriptions run there; do not introduce background loading in this scope.

**Step 3: Run focused tests**

```bash
dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj --filter FullyQualifiedName~AssetContextControllerTests
```

Expected: PASS.

**Step 4: Commit**

```bash
git add src/MapEditor.App/Rendering tests/MapEditor.App.Tests/AssetContextControllerTests.cs tests/MapEditor.App.Tests/Fixtures/AssetFixture.cs
git commit -m "feat: expose graphic viewer asset metadata"
```

**Invariant-to-test matrix:**

| Invariant | Proved by |
|-----------|-----------|
| Old asset folders still open for map editing | Missing-sidecar test resolves an ordinary sprite |
| Failed sprite-manifest replacement is not published | Failure test checks identity and zero events |
| Viewer sees fully replaced state without suppressing teardown | Callback checks new `Current` and availability; post-call assertion checks old disposal |

### Task 2: Implement testable navigation, selection, zoom, and playback state

**Files:**
- Create: `src/MapEditor.App/ViewModels/GraphicViewerViewModel.cs`
- Create: `src/MapEditor.App/ViewModels/GraphicViewerCategoryFilter.cs`
- Create: `tests/MapEditor.App.Tests/GraphicViewerViewModelTests.cs`

**Mutation impact:**
- Source of truth changed: viewer-local state only; `GraphicAssetCatalog` and `SpriteManifest` remain immutable sources.
- Important readers: the two controls and viewer window added later.
- Derived/cached state affected: filtered sheet IDs, selected frame details, matching animations, frame position, and playback flag derive from the current catalog and selection.
- Required propagation sequence: asset bind resets all transient state; category change recomputes sheets then selects a valid first sheet; sheet change clears graphic/animation/playback; frame selection computes matching animations then chooses index zero and starts only if a match exists.
- Invariants to preserve: viewer state never mutates map documents, brush, undo, or dirty state; invalid sheet input leaves the prior valid selection intact; all category/equipment mappings remain visible.
- Observable proof required: tests query final public properties and use real Part 1 catalogs rather than mocks for indexing behavior.

**Step 1: Write failing state-machine tests**

Use in-memory `SpriteManifest` and `GraphicAnimationManifest` fixtures. Cover:

- Initial filter is `All` and uses catalog `GetSheets(null)`, not tile-filtered `AssetContext.SheetIds`.
- Filters appear in order: `All`, `Body`, `Hair`, `Eyes`, `Chest`, `Helm`, `Legs`, `Feet`, `Hand`, `Tiles`, `Spells`.
- Category change selects the first valid numeric sheet or no sheet.
- Numeric sheet input accepts only a sheet in the current filter; malformed/non-member input preserves prior state and returns false.
- Sheet change preserves zoom and clears frame, animation, frame index, and playback.
- Hit testing applies inverse zoom and half-open source rectangles, then chooses the lowest graphic ID for overlaps.
- Adjacent frame edges select only one rectangle.
- Frame selection loads every category/ID mapping and matching animation.
- The first matching animation is selected and starts; a frame with no animation remains selected and stopped.
- Choosing another animation resets position and starts playback.
- Tick, previous, and next wrap through frames.
- Pause, sheet change, and unavailable asset binding stop playback.
- Zoom clamps to 25%–800%; 100% is exact; Fit uses the smaller viewport/image scale and clamps it.

Expected red failure: viewer state types do not exist.

**Step 2: Implement the view model**

Use `INotifyPropertyChanged`, matching existing app view models. Keep a nullable `GraphicCategory` behind `GraphicViewerCategoryFilter.All`. Expose commands as methods used by code-behind rather than introducing a command framework absent from the app.

Use `SpriteManifest.GetFrames(selectedSheet)` for hit rectangles. Sort/canonical ordering should come from Part 1; do not rebuild category or reverse-animation indexes in Avalonia code.

Represent zoom as a `double` scale with `0.25` and `8.0` bounds. Hit-test rectangle bounds explicitly as half-open intervals so adjacent edges are deterministic.

**Step 3: Run tests**

```bash
dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj --filter FullyQualifiedName~GraphicViewerViewModelTests
```

Expected: PASS.

**Step 4: Commit**

```bash
git add src/MapEditor.App/ViewModels/GraphicViewer* tests/MapEditor.App.Tests/GraphicViewerViewModelTests.cs
git commit -m "feat: add graphic viewer state"
```

**Invariant-to-test matrix:**

| Invariant | Proved by |
|-----------|-----------|
| `All` includes non-tile sheets | Mixed-category catalog initial-state test |
| Overlap and edge selection are deterministic | Lowest-ID overlap and half-open-edge adversarial tests |
| Selection never touches map editing state | Real document bytes/dirty/history remain unchanged |
| Playback cannot survive invalid state | Sheet/unavailable transition tests |

### Task 3: Draw the full sheet and animation preview

**Files:**
- Create: `src/MapEditor.App/Controls/GraphicSheetControl.cs`
- Create: `src/MapEditor.App/Controls/AnimationPreviewControl.cs`
- Create: `tests/MapEditor.App.Tests/GraphicSheetControlTests.cs`
- Create: `tests/MapEditor.App.Tests/AnimationPreviewControlTests.cs`

**Mutation impact:**
- Source of truth changed: none; controls render the view model and current images.
- Important readers: Avalonia layout and pointer input; tests inspect draw operations and handled state.
- Derived/cached state affected: desired sheet-control size derives from bitmap dimensions and zoom; no image cache is owned by either control.
- Required propagation sequence: window assigns a non-owning image and view model, control invalidates measure/visual, render uses current immutable frame list; pointer input delegates state mutation to the view model, whose property changes drive window clock synchronization.
- Invariants to preserve: the full-sheet image is disposed only by the window; cache-owned animation images are disposed only by `SpriteAssetCache`; no visual child is created per frame.
- Observable proof required: draw-target tests assert source/destination geometry and ownership tests assert controls do not dispose assigned images.

**Step 1: Write failing sheet-control tests**

Follow the separately testable rendering pattern at `SpritePaletteControl.RenderPalette`. Cover:

- Full bitmap destination size equals pixel size times zoom.
- Every source rectangle gets a subtle scaled outline and selection gets a distinct outline.
- Desired size changes with image and zoom so a parent `ScrollViewer` receives correct extents.
- Left click converts coordinates through zoom and selects via the view model.
- Ctrl+wheel requests bounded zoom and sets `Handled`; plain wheel remains unhandled for the parent scroller.
- Rendering and input with no image/sheet do not throw.

Expected red failure: `GraphicSheetControl` does not exist.

**Step 2: Implement `GraphicSheetControl`**

Derive from `Control, ICustomHitTest`, make it focusable, and set nearest-neighbor interpolation. Accept a non-owning `AvaloniaSpriteSheetImage?` and `GraphicViewerViewModel`. Override measure using scaled bitmap dimensions. Draw the bitmap, then frame outlines, then selection. Keep normal wheel events unhandled.

**Step 3: Write failing preview tests**

Cover:

- Current frame resolves through `AssetContext.Resolve`.
- Source rectangle comes from `SpriteResolution.SourceRect`.
- Frames of different sizes draw bottom-center in a stable area based on manifest maximum dimensions.
- Cross-sheet animation frames resolve and draw in order.
- Missing/unreadable cached images produce an inline diagnostic state without throwing.
- The control never disposes `SpriteResolution.Image`.

Expected red failure: `AnimationPreviewControl` does not exist.

**Step 4: Implement `AnimationPreviewControl`**

Set nearest-neighbor interpolation. Resolve only the current animation frame through `AssetContext.Resolve(SpriteReference)`, cast a successful image to `AvaloniaSpriteSheetImage`, and draw at native dimensions:

```text
x = (previewWidth - frameWidth) / 2
y = previewHeight - frameHeight
```

Use the sprite manifest's maximum dimensions for stable desired size. Surface resolution diagnostics to the owning window/view model without throwing.

**Step 5: Run tests and commit**

```bash
dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj --filter "FullyQualifiedName~GraphicSheetControlTests|FullyQualifiedName~AnimationPreviewControlTests"
git add src/MapEditor.App/Controls/GraphicSheetControl.cs src/MapEditor.App/Controls/AnimationPreviewControl.cs tests/MapEditor.App.Tests
git commit -m "feat: render graphic sheets and animation previews"
```

**Invariant-to-test matrix:**

| Invariant | Proved by |
|-----------|-----------|
| Rendering uses nearest-neighbor scaled geometry | Exact draw-operation assertions |
| Parent scrolling remains functional | Plain-wheel unhandled test |
| Image ownership stays at window/cache boundaries | Disposal-spy tests |
| Preview anchor does not jump with frame size | Mixed-size frame geometry test |

### Task 4: Build the viewer window and playback clock

**Files:**
- Create: `src/MapEditor.App/Rendering/IPlaybackClock.cs`
- Create: `src/MapEditor.App/Rendering/DispatcherPlaybackClock.cs`
- Create: `src/MapEditor.App/Views/GraphicViewerWindow.axaml`
- Create: `src/MapEditor.App/Views/GraphicViewerWindow.axaml.cs`
- Create: `tests/MapEditor.App.Tests/Fakes/ManualPlaybackClock.cs`
- Create: `tests/MapEditor.App.Tests/GraphicViewerWindowTests.cs`

**Mutation impact:**
- Source of truth changed: window-owned current full-sheet image and subscription/timer lifecycle.
- Important readers: controls, toolbar bindings, playback buttons, and asset-change callback.
- Derived/cached state affected: loaded full sheet, inline load diagnostic, timer interval, and control bindings.
- Required propagation sequence on sheet/assets change: stop clock, clear playback/selection, detach image from control, dispose old full-sheet image, bind new catalog/context, load one new sheet image, assign it, invalidate layout/render.
- Invariants to preserve: only one full browsing sheet is owned at a time; old images and subscriptions are released exactly once; no timer tick can advance state after close or unavailability.
- Observable proof required: tests drive a manual clock and disposal spies, asserting final state rather than delays/callback counts alone.

**Step 1: Define and test the clock boundary**

Use:

```csharp
internal interface IPlaybackClock : IDisposable
{
    event EventHandler? Tick;
    TimeSpan Interval { get; set; }
    bool IsRunning { get; }
    void Start();
    void Stop();
}
```

`DispatcherPlaybackClock` wraps `Avalonia.Threading.DispatcherTimer`. `ManualPlaybackClock` raises ticks synchronously only while running. Add tests that 8 FPS sets a 125 ms interval and stop/dispose prevents advancement.

**Step 2: Add the AXAML layout**

Create a resizable 1100×750 window containing named controls for:

- Category selector.
- Editable numeric sheet selector.
- Zoom selector plus Fit and 100% buttons.
- Dark, scrollable `GraphicSheetControl`.
- Graphic ID, source rectangle, all category/equipment mappings, and matching-animation selector.
- `AnimationPreviewControl` with play/pause, previous, next, and frame-position display.
- Inline availability and image-load diagnostics.

Use existing dynamic theme resources from `src/MapEditor.App/Styles/EditorTheme.axaml:63-81`; use the dark artboard brush for image panes.

**Step 3: Write failing lifecycle and interaction tests**

Inject `ISpriteSheetLoader` and `IPlaybackClock`. Cover:

- Initial valid state loads exactly one `sheets/<id>.png`.
- Sheet change stops playback, disposes the old full-sheet image, then loads one replacement.
- Category, editable sheet, zoom, Fit, 100%, animation selector, and playback buttons propagate to the view model.
- Selecting a graphic with a matching animation sets `IsPlaying` and starts the clock automatically without pressing the play button.
- Every `IsPlaying`, selected-animation, and FPS property change synchronizes clock start/stop/interval; sheet and asset changes therefore stop it even when initiated by the sheet control.
- Clock ticks wrap frame position and invalidate preview.
- Missing/corrupt PNG leaves navigation enabled and shows the loader diagnostic.
- Asset replacement follows the full stop/clear/dispose/rebind/load sequence.
- Invalid replacement metadata leaves the window open and unavailable.
- Unmodified Space toggles playback when focus is not in editable text; it does not steal Space from the sheet editor.
- Close stops/disposes the clock, unsubscribes from `CurrentChanged`, detaches controls, and disposes the full-sheet image exactly once.

Expected red failure: the window does not exist.

**Step 4: Implement the window lifecycle**

Production construction uses `AvaloniaSpriteSheetLoader` and `DispatcherPlaybackClock`; keep an internal injectable constructor for headless tests. Build a sheet path from `AssetContext.Cache.AssetDirectory` and numeric sheet ID. Require successful loader results to contain `AvaloniaSpriteSheetImage`; reject malformed success results consistently with `SpriteAssetCache`.

Subscribe to `AssetContextController.CurrentChanged` and the view model's `PropertyChanged` once. On asset replacement, never dereference the prior context; consume only `Current`, whose publication is complete. Synchronize the clock whenever playback, selected animation, or FPS changes: stop first, set the selected animation interval, then restart only when `IsPlaying` remains true. On close, unsubscribe both handlers before disposing images and clock.

Override `OnKeyDown` for Space. Ignore already-handled events and editable text sources, then toggle and mark handled. Do not add a tunnel handler that would preempt editing controls.

**Step 5: Run tests and commit**

```bash
dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj --filter FullyQualifiedName~GraphicViewerWindowTests
git add src/MapEditor.App/Rendering/IPlaybackClock.cs src/MapEditor.App/Rendering/DispatcherPlaybackClock.cs src/MapEditor.App/Views/GraphicViewerWindow* tests/MapEditor.App.Tests
git commit -m "feat: add graphic viewer window"
```

**Invariant-to-test matrix:**

| Invariant | Proved by |
|-----------|-----------|
| At most one full-sheet image is owned | Loader/disposal sequence test across multiple changes |
| Automatic playback starts and closed/unavailable viewers cannot tick | Selection-start, sheet-stop, close, and invalid-reload manual-clock tests |
| Asset replacement never uses stale context | Replacement test disposes old context and renders new IDs |
| Editable sheet input keeps normal Space behavior | Focused editable-control keyboard test |

### Task 5: Integrate the singleton modeless viewer and smoke coverage

**Files:**
- Modify: `src/MapEditor.App/Views/MainWindow.axaml:11-46`
- Modify: `src/MapEditor.App/Views/MainWindow.axaml.cs:38-120`
- Modify: `tests/MapEditor.App.Tests/MainWindowTests.cs`
- Modify: `tests/MapEditor.App.Tests/MainWindowCloseTests.cs`
- Modify: `docs/map-editor-smoke.md`

**Mutation impact:**
- Source of truth changed: `MainWindow` owns the optional live viewer reference.
- Important readers: Tools menu command, editor shutdown, and headless window tests.
- Derived/cached state affected: none beyond modeless window lifecycle.
- Required propagation sequence on open: check ordinary assets, check viewer availability, focus existing viewer or construct and publish one instance, attach `Closed`, then call `Show(this)`. Teardown sequence: close viewer, let it stop/unsubscribe/dispose, clear reference, then dispose `_assets`.
- Invariants to preserve: invalid assets create no partial window; repeated commands never create duplicates; viewer remains modeless; editor shutdown destroys viewer before shared cache disposal.
- Observable proof required: real headless windows assert ownership, enabled editor state, singleton reuse, errors, and disposal order.

**Step 1: Add failing menu/open tests**

Add **Tools → Graphic Viewer** and tests expecting:

- Named Tools and viewer menu items exist.
- No loaded assets calls `ShowErrorAsync(new ErrorPresentation("Graphic Viewer", ...))` once and creates no viewer.
- Invalid/missing sidecar reports its exact availability reason and creates no viewer.
- Valid assets create an owned modeless window while `MainWindow.IsEnabled` remains true.
- Repeated command activates/reuses the same instance.
- Closing the viewer clears ownership so a later command creates a fresh instance.

Expected red failure: menu item and handler do not exist.

**Step 2: Implement singleton modeless ownership**

Store `GraphicViewerWindow? _graphicViewer`. The async click handler should:

1. Activate the existing visible instance and return.
2. Validate `_assets.Current.IsAvailable` and `GraphicViewerAvailability`.
3. Await `_dialogs.ShowErrorAsync(...)` and return on failure.
4. Construct the viewer, subscribe once to `Closed` to clear the field, assign the field, then call `Show(this)` rather than `ShowDialog`.

Expose the owned viewer through an internal read-only property only if tests need it; do not make it public API.

**Step 3: Add shutdown and non-mutation tests**

Cover:

- Asset replacement with valid metadata updates the open viewer.
- Replacement with invalid metadata leaves it open and unavailable.
- Closing `MainWindow` closes the viewer before `AssetContextController.Dispose` destroys cache images.
- Viewer selection/playback leaves active document bytes, brush, dirty state, undo, and redo unchanged.

Expected red failure: existing `Closed` handler disposes assets without closing a viewer.

**Step 4: Implement shutdown order**

In the existing `Closed` handler, mark `_closed`, close and clear `_graphicViewer`, then dispose `_assets`. Keep the viewer's own close path idempotent because owner shutdown and its `Closed` callback can both clear references.

**Step 5: Update smoke documentation**

Add a concise flow to `docs/map-editor-smoke.md`:

1. Regenerate assets so both manifests exist.
2. Load the asset directory and open Tools → Graphic Viewer.
3. Exercise every category, direct numeric sheet entry, Fit, 100%, min/max zoom, and scrolling.
4. Select normal, overlapping, and non-animated graphics.
5. Choose multiple matching animations and verify play/pause/step/wrap at 8 FPS.
6. Switch asset roots and verify valid reload plus invalid-sidecar unavailable state.
7. Close the viewer and editor while playback is active.

**Step 6: Run focused and full gates**

```bash
dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj --filter "FullyQualifiedName~GraphicViewer|FullyQualifiedName~MainWindowTests|FullyQualifiedName~MainWindowCloseTests"
dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj
dotnet test Goose2ClientGodot.sln
```

Expected: PASS. Existing warnings may remain; no new test failures.

Run the new hermetic converter gate from Part 1:

```bash
dotnet test tools/AssetConverter/tests/AssetConverter.Tests/AssetConverter.Tests.csproj --filter "FullyQualifiedName~AnimationManifestBuilderTests|FullyQualifiedName~ManifestFileStoreTests"
```

Expected: PASS without external datasets. Do not use the known four dataset-drift failures as acceptance criteria for this feature.

**Step 7: Commit**

```bash
git add src/MapEditor.App/Views/MainWindow.axaml src/MapEditor.App/Views/MainWindow.axaml.cs tests/MapEditor.App.Tests docs/map-editor-smoke.md
git commit -m "feat: integrate map editor graphic viewer"
```

**Invariant-to-test matrix:**

| Invariant | Proved by |
|-----------|-----------|
| Viewer is singleton and modeless | Repeated-command and enabled-owner headless tests |
| Invalid metadata publishes no window | Missing/malformed sidecar error tests |
| Viewer teardown precedes shared cache disposal | Main-window close disposal-order test |
| Viewer is strictly read-only | Document/brush/history non-mutation test |

## Part 2 completion criteria

- Tools → Graphic Viewer opens one modeless, reusable viewer.
- All approved categories and numeric sheet navigation work.
- Full-sheet selection, 25%–800% zoom, Fit, and 100% work with nearest-neighbor rendering.
- Matching animations play, pause, step, and wrap at 8 FPS.
- Asset replacement and shutdown release timers, subscriptions, and images deterministically.
- Missing sidecars block viewer opening but never block normal map editing.
- Main solution, app tests, and new hermetic converter tests pass.
