# Map editor tabs and cross-map copy/paste

## Goal

Open several maps at once in the map editor, each in its own tab, and copy tiles
from one map into another.

## Decisions

- Every piece of editing state is per tab. Only the theme, the asset directory,
  and the tile clipboard are app-wide.
- New and Open always add a tab. Nothing replaces a document in place, so the
  dirty prompt moves from New/Open to tab close.
- Opening a file that is already open activates that tab instead of duplicating it.
- Closing the last tab leaves a fresh empty Untitled tab, not a closed window.
- Tabs are not persisted; every launch starts with one Untitled tab.
- Untitled tabs are not numbered.

## Architecture

### `MapDocumentViewModel`

`MainWindowViewModel` renamed, one instance per tab. Keeps session, active tool,
brush, selected sheet, layer selection and visibility, layer anchor, hover,
selection rectangle, paste mode, zoom readout, undo/redo, title and dirty state.
It owns its own `EditorDocumentController`.

Two pieces move in and out:

- The `_clipboard` field leaves, replaced by an injected `SharedTileClipboard`.
- `MainWindow._layerAnchor` moves in, because layer selection is per tab and a
  window-level anchor would range-select from another tab's anchor.

`EditorRefresh.Document` and the "session changed under us" branch of `Refresh`
are deleted: no document is ever swapped inside a view model again.

### `EditorDocumentController`

Becomes strictly per-document. Keeps `SaveAsync`, `SaveAsAsync`, `Undo`, `Redo`
and the dirty prompt for its own document. Loses `NewAsync`, `OpenAsync` and
`ResolveDirtyAsync` — creating a document is now a workspace concern. Loses its
`_closeGuardRunning` / `_closeApproved` fields; the window owns re-entrancy, and
per-document guards would let two prompts stack.

### `WorkspaceViewModel` (new)

Owns `ObservableCollection<MapDocumentViewModel> Documents`, `ActiveDocument` and
the shared clipboard. Handles `NewAsync`, `OpenAsync`, `CloseAsync(document)`,
`CloseAllAsync`, activation and reordering. Open failures surface the existing
messages (the `DescribeFormatError` table, `MapValidationException`, IO / access
/ path) and create no tab.

### `SharedTileClipboard` (new)

A single mutable slot holding `TileClipboard?` plus a `Changed` event,
constructed once by the workspace and handed to every document view model.

This is the whole cross-map paste feature. `TileClipboard` already stores raw
`MapTileLayer[]` with no reference to its source document; `ApplyPasteAt`
already clips to the destination's dimensions and pairs layers top-down against
the destination's `SelectedLayers`. No tile logic changes.

### Views

`MainWindow` holds `Dictionary<MapDocumentViewModel, MapCanvas>`, creating a
canvas when a tab opens and dropping it when the tab closes, so views stay out of
the view models. Per-tab zoom, scroll and in-flight gesture state then live
naturally in each canvas.

A tab strip (`ItemsControl`) docks full width under the menu, above `Body`; the
active document's canvas becomes `CanvasHost.Child`. The strip is always visible,
even with one tab, so the canvas never reflows.

On activation the window:

1. calls `FinishInteraction(commit: true)` on the outgoing canvas,
2. cancels the outgoing document's paste mode,
3. swaps `CanvasHost.Child` and `PaletteBorder.Child` to the incoming
   document's canvas and palette,
4. detaches `PropertyChanged` / `CanvasInvalidated` from the old view model and
   attaches to the new,
5. swaps `DataContext` and re-runs `SyncToolButtons`, `SyncLayerRows`,
   `SyncBrushFields`, `SyncReadouts` and the title.

Hover needs no explicit handling: `MapCanvas` already attaches and detaches its
window `PointerMoved` handler in `OnAttachedToVisualTree` /
`OnDetachedFromVisualTree` (`src/MapEditor.App/Controls/MapCanvas.cs:168-186`),
so swapping `CanvasHost.Child` removes the outgoing canvas from the tree and
stops it tracking the cursor.

`SpritePaletteControl` is also per document, held in the same bundle as the
canvas, which gives per-tab scroll offset for free. The window's shared
`PaletteBar` scroll bar is bound to the incoming palette on activation
(`BindScrollBar`, `SpritePaletteControl.cs:77`) and unbound from the outgoing
one, so an inactive palette never reacts to the bar.

`AssetContextController` stays app-wide and disposed once on window close. It
currently holds a single view model (`AssetContextController.cs:11`); it becomes
workspace-aware, seeding each newly added document with the current sheet ids and
looping over `Documents` on `TryOpen` so every tab's palette is invalidated.

## Tab strip

Each tab shows the file name (or "Untitled"), a dirty dot bound to `IsDirty`, a
close button, and the full path as a tooltip. `Refresh(Title | Commands)` must
raise change notification for both `TabTitle` and `IsDirty`. Styles join the
existing `card` / `row` / `chrome` classes in `EditorTheme.axaml`.

Tabs shrink to a minimum width and then scroll horizontally; there is no cap on
open tabs. Activating an already-open file scrolls its tab into view.

### Input

- Ctrl/Cmd+T new tab, Ctrl+W close active tab.
- Ctrl+Tab / Ctrl+Shift+Tab cycle with wrapping.
- Ctrl+1..8 jump to that index; Ctrl+9 jumps to the last tab.
- Middle-click on a tab closes it through the same path as the close button.
- Drag a tab past a neighbour's midpoint to reorder; Escape cancels back to the
  original order. Reordering never changes which tab is active.

Ctrl+Tab is a known risk: Avalonia's focus manager consumes Tab for directional
navigation and may run before `Window.OnKeyDown`. Register the tab shortcuts as a
tunnelling `KeyDownEvent` handler from the start, the way `PointerPressedEvent`
already is.

## Close and quit

Closing a clean tab drops it and activates the neighbour to the right, or to the
left when it was last. Closing a dirty tab first activates it, then shows the
existing Save / Discard / Cancel dialog. Cancel aborts. Save runs that document's
`SaveAsync`, which may open the Save-As picker; if the document is still dirty
afterwards the close aborts too.

Quitting keeps the existing `OnClosing` guards and cancel-then-re-`Close()`
dance, but walks `Documents` in tab order, activating and prompting each dirty
document. Cancel on any tab aborts the quit and leaves that tab active.

`MainWindow` gains a single `_commandRunning` flag. While it is set, tab
activation, tab close and the file commands are ignored. Today `RunCommandAsync`
is fire-and-forget and nothing prevents a second command starting while a dialog
is open; with tabs that would let a Save-As picker for one tab resolve after the
user has moved to another. This is a real bug the feature forces into the open.

## Testing

New `WorkspaceViewModelTests`: New and Open add tabs; opening an already-open
path activates the existing tab; closing activates the correct neighbour;
closing the last tab yields a fresh clean Untitled; reordering does not change
activation; dirty close honours Save, Discard and Cancel through
`FakeEditorDialogs`.

New cross-map paste tests (plain view-model tests, no UI): copy a region from one
document and paste into a smaller one (clipping), and into one with different
`SelectedLayers` (top-down layer pairing).

Extended `MainWindowCloseTests` via `MainWindowHarness` for quit with several
dirty tabs, including cancel on the second tab. Extended `ShortcutTests` for
Ctrl+T / W / Tab / 1-9. Extended `MapCanvasTests`: an in-flight stroke commits
when the tab changes, and each tab keeps its own zoom and scroll across a switch
and back. `MainWindowViewModelTests` is renamed with its class.

## Deferred

- Session restore of open tabs across launches.
- A combined "save these N maps?" quit dialog; prompting per dirty tab reuses the
  existing dialog.
