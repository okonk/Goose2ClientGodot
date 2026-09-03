# Portable Map Editor Design

## Goal

Build a standalone map editor for Windows, macOS, and Linux. The first release supports creating maps, opening and saving the existing binary map format, painting five tile layers, editing blocked flags, and undo/redo. Rendering should be close to the game client but does not need pixel-identical engine behavior.

## Technology

Use C# on .NET 8 with Avalonia 11. This keeps the editor portable, provides conventional desktop controls and input behavior, and allows the existing C# map model and asset pipeline to be reused without coupling the editor to Godot.

The editor will be distributed as self-contained applications. Users will not need a separate .NET installation.

## Architecture

The editor consists of three projects:

- `MapEditor.Core`: engine-independent map model, validation, binary serialization, edit commands, and undo/redo.
- `MapEditor.Rendering`: asset manifest loading, sprite caching, viewport calculations, culling, and platform-independent draw operations.
- `MapEditor.App`: Avalonia windows, controls, file dialogs, settings, keyboard shortcuts, and the map canvas.

The Godot client references `MapEditor.Core` for the shared map model and legacy codec. The editor does not reference Godot. Existing runtime callers continue to use the same map concepts while serialization gains validation and write support.

The map canvas is a custom Avalonia `Control`. Rendering logic produces draw operations that the control executes, keeping viewport and composition behavior testable without a graphics backend.

## Map model and format

The shared model represents:

- Map version
- Editor version
- Width and height
- A row-major tile collection
- Five `(sheet, graphic)` layers per tile
- The complete integer flag field per tile

The codec reads and writes the existing little-endian binary format:

- Header: `Int16 Version`, `Int16 EditorVersion`, `Int32 Width`, `Int32 Height`
- Each tile: `Int32 Flags`
- Each of five layers: `Int32 Graphic`, `Int16 Sheet`

Loading validates header availability, dimensions, tile-count arithmetic, and exact stream length before replacing the active document. Incompatible layouts are rejected instead of being rewritten. Unknown flag bits are retained unchanged.

Saving validates the model, writes a temporary file beside the destination, flushes and closes it, then replaces the destination atomically where supported. A failed save leaves the document dirty and the previous file intact.

If a source file changes after opening, the editor warns before overwriting it. Concurrent changes are not merged.

## New maps

`File > New Map` opens a dimensions dialog. Width and height default to 100 and accept values from 1 through 1000. A new map uses `Version = 1`, `EditorVersion = 10`, five empty layers per tile, and zero flags.

A new document has no path, so Save invokes Save As. Creating or opening a document prompts before discarding unsaved work and resets undo history. Existing-map resizing is deferred.

The model stores tile data directly and the renderer creates no UI or graphics object per map tile. A 1000×1000 empty map therefore has bounded model storage and only visible tiles participate in drawing.

## Assets and rendering

The editor consumes existing asset-converter output:

- Per-sheet PNG files
- `manifest.json` entries mapping `(sheet, graphic)` to source rectangles
- 32×32 map cells
- Bottom-center sprite anchoring

The asset directory is selected on first launch and stored in the OS-standard user settings directory. No machine-specific path is compiled into the application. Manifest validation reports missing or malformed data with actionable errors.

The canvas draws visible content in layer order from 0 through 4. It computes the visible tile range from the viewport and emits draw operations only for that range. Sheet images and decoded sprite metadata are cached. Missing sheets or graphics render as conspicuous placeholders while their numeric references remain editable and saveable.

Canvas navigation supports panning and cursor-centered zoom. Zoom is restricted to 25%, 50%, 100%, 200%, and 400%, using nearest-neighbor filtering. Optional overlays show tile boundaries, the hovered or selected cell, and blocked tiles. Each map layer can be shown or hidden independently.

Rendering uses the same source rectangles, layer order, coordinates, cell size, and bottom-center anchoring as the client. Minor differences caused by Avalonia and Godot rendering are acceptable.

## Editing workflow

The main window has:

- File and Edit menus plus tool buttons at the top
- A sprite-sheet selector and graphic grid on the left
- The map canvas in the center
- Layer visibility, active-layer selection, and tile properties on the right
- Cursor coordinates, zoom, and map dimensions in a status bar

The initial tools are:

- Pencil
- Eraser
- Eyedropper
- Blocked-tile toggle

Painting changes only the active layer. Erasing clears that layer without changing flags or other layers. The blocked control changes only the known blocked bit and preserves every other flag bit.

Click-drag painting interpolates between sampled cells so quick pointer movement does not leave gaps. A continuous drag is one command and one undo step. Commands retain enough before-and-after state for deterministic undo and redo. Undo storage has a configurable memory cap.

The document tracks dirty state and prompts before close, open, or new operations that would discard changes. Standard shortcuts include Save, Undo, and Redo with platform-appropriate modifiers.

Rectangle fill, flood fill, multi-cell selection, asset search, favorites, and existing-map resizing are deferred.

## Error handling

Map format errors, asset errors, and invalid sprite references are reported separately. A map can open and save when art is unavailable because map data stores numeric references rather than image content.

The editor does not replace the active document until a requested open succeeds. Failed saves preserve the active document, dirty state, and prior destination file. User-facing errors identify the affected path and operation without terminating the application.

Crash recovery and periodic autosave are deferred. Atomic save protects existing files but does not recover unsaved edits after a process or machine failure.

## Testing

Automated tests cover:

- Existing fixture decoding
- Decode and encode round trips preserving every field
- Truncated, malformed, oversized, and incompatible input
- New-map dimensions, defaults, and limits
- Coordinate transformations at every zoom level
- Viewport tile-range calculations
- Layer order, source rectangles, and bottom-center draw positions
- Layer-specific painting and erasing
- Blocked-bit changes that preserve unknown bits
- Drag interpolation and command grouping
- Undo, redo, memory limits, and dirty-state transitions
- Missing and malformed asset manifests
- Missing sprite references
- Atomic-save behavior and external-change detection

Rendering tests verify platform-independent draw operations rather than pixel screenshots. A local manual smoke pass checks dialogs, shortcuts, pointer interaction, nearest-neighbor rendering, and visual composition. Pixel-perfect screenshot tests are deferred.

## Local builds

No CI is included initially. A local editor build script:

- Builds from the current working tree, including uncommitted changes
- Runs editor tests unless explicitly skipped
- Publishes self-contained applications
- Supports Linux x64, Windows x64, macOS x64, and macOS ARM64
- Places archives under `build/map-editor/`
- Does not require Godot or generated game assets
- Stages outputs and leaves previous successful artifacts intact if a build fails

The initial release process excludes installers, signing, notarization, automatic updates, AppImage, Flatpak, and other package-manager formats.

## Deferred scope

- Existing-map resizing
- Rectangle and flood-fill tools
- Multi-cell selection and clipboard operations
- Asset search and favorites
- Autosave and crash recovery
- Concurrent file merging
- Pixel screenshot testing
- CI and automated cross-platform execution
- Installers, signing, notarization, and updates
