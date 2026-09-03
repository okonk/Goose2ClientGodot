# Map Editor UI & Tools — Design

Date: 2026-09-03
Branch: `map-editor-ui-tools`
Scope: `src/MapEditor.App`, `src/MapEditor.Core`, `src/MapEditor.Rendering` and their test projects.

## Summary

1. Replace the right panel's two groups (layer radio buttons + visibility checkboxes) with a single named, multi-selectable layer list with a per-row visibility checkbox.
2. Move Grid and Blocked display toggles from the right panel into a new checkable View menu.
3. Strip the toolbar down to tool toggles only (remove New/Open/Save/Undo/Redo/Zoom in/Zoom out; all covered by menu items and hotkeys).
4. Add three tools: Select (single-tile inspect), Multi-select (rectangle copy/paste across selected layers), and Flood fill (bounded, 4-directional).
5. The single "active layer" becomes a layer selection set (≥1 layer); editing tools act on the topmost selected layer; copy/paste acts on all selected layers.

## Layer naming

| Index | Name |
|---|---|
| 0 | Ground |
| 1 | Below Entities |
| 2 | Entities |
| 3 | Above Entities |
| 4 | Roof |

Rows display as "0 — Ground" style (index + name). The selected-tile readout's per-layer lines keep their `L0 …` index format.

## 1. Layer list & View menu

Right panel: a single list of 5 rows replaces the radio group, the visibility checkbox group, and the Grid/Blocked checkboxes. The "Selected tile" readout stays below the list.

- Each row: layer name (left) + visibility checkbox (row end).
- Plain click = select only that layer. Ctrl+click = toggle that layer in/out of the selection. Shift+click = range select.
- The selection never empties: toggling off the last selected layer instead single-selects it.
- Selected rows get a highlight background. Clicking the visibility checkbox does not change selection.
- Menu bar gains a **View** menu with two checkable items: "Grid" (default on) and "Blocked" (default off). No hotkeys for these.

Implementation notes:

- Rows are a StackPanel of 5 Grids (TextBlock + CheckBox) with row-level pointer handling; Avalonia ListBox does not give per-row checkboxes cleanly.
- ViewModel: the five `LayerNVisible` properties are replaced by one visibility-mask property (byte/int); the canvas already composes a mask for `MapLayerVisibility`.
- Layer names live in one shared constant array (App layer; the core stays name-agnostic).

## 2. Toolbar & hotkeys

Toolbar contains tool toggles only, left to right:

| Tool | Hotkey |
|---|---|
| Pencil | P |
| Eraser | E |
| Eyedropper | I |
| Blocked | X (was B) |
| Select | V |
| Multi-select | M |
| Flood fill | B |

New/Open/Save/Undo/Redo/Zoom in/Zoom out toolbar buttons are removed. Existing hotkeys unchanged: Ctrl+N/O/S, Ctrl+Z (undo), Ctrl+Y / Ctrl+Shift+Z (redo), +/- zoom.

## 3. Layer selection model

- `MapEditSession.ActiveLayer` (int) becomes a layer selection: a mask of selected layers (≥1) with the **topmost** = highest index in the set.
- Pencil, eraser, and flood fill apply to the topmost selected layer only.
- Eyedropper samples the topmost selected layer.
- Blocked toggle is unaffected (flags are per-tile).
- Multi-select copy/paste operates on **all** selected layers; paste writes each layer onto its corresponding layer.
- Changing the layer selection cancels paste mode.
- Undo/redo, dirty tracking, and the file format are untouched — layer changes are already recorded per layer.

## 4. New tools

### Select (V)

Click a tile: it becomes the selected tile (existing cyan outline) and the right-panel readout updates (coords, blocked, per-layer sheet/graphic). No document changes, no undo entry.

### Multi-select (M)

- Drag a rectangle; the region shows an outline overlay. The selection persists until a new drag.
- Ctrl+C copies the tiles in the rectangle for all selected layers into an app-level clipboard (fixed size + per-layer data).
- Ctrl+V (from any tool) enters paste mode if the clipboard is non-empty: a ghost of the copied tiles follows the cursor, top-left anchored to the hovered cell, drawn on their corresponding layers.
- Click applies the paste: one undoable command writing each layer's tiles to the matching layer, overwriting existing tiles.
- Esc, a click outside the map, or changing the layer selection cancels paste mode.
- Paste is clipped to the map: only in-bounds cells are written.

### Flood fill (B)

Single click on the topmost selected layer: BFS, 4-directional, fills the connected region of cells matching the clicked cell's tile (empty region if the cell is empty) with the current brush. Stops at any differing tile. One undoable command. A click on a cell already holding the brush tile produces no no-op undo entry.

### Core changes

- `MapEditTool` gains `Select`, `MultiSelect`, `FloodFill`.
- Select/MultiSelect make no document changes; the session treats them as non-stroking tools (no change buffers).
- Flood fill gets a click-only entry point in the session: BFS over the document using the existing visit bitmap, appending to the layer-change buffer, committed as one command.
- `MapRenderOptions` gains a selection-rectangle overlay and a paste-ghost option (offset + per-layer tile data).

## 5. Testing

- **Core** (`MapEditSessionTests` + new tests): flood fill — empty region fills everything; square boundary contains the fill; 4-directional so diagonal tiles leak; differing-tile region refilled with the brush; no-op click yields no undo entry; undo/redo restores state. Multi-layer selection — editing targets topmost only; invalid/empty selection rejected; eyedropper samples topmost.
- **App** (`MainWindowViewModelTests`, `MapCanvasTests`, `MainWindowTests`): selection-set logic (plain/Ctrl/Shift click, selection never empties); clipboard capture per selected layer; paste writes corresponding layers and clips at map edges; Esc cancels paste mode; toolbar/menu wiring (View menu toggles, hotkeys P/E/I/X/V/M/B).
- **Rendering** (`MapRendererTests`): selection-rectangle overlay and paste ghost drawn per layer.

## Deferred (consciously out of scope)

- No persistence of the clipboard or layer-selection state across app restarts (session-only).
- No layer resize/rename, no per-layer opacity, no paste scaling.
