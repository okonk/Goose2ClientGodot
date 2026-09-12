# Graphic Viewer Design

## Goal

Add a read-only graphic viewer to the map editor for browsing full sprite sheets by category and sheet ID, inspecting individual graphics, and previewing their animations.

## Scope

The viewer will:

- Open as a separate modeless window from the map editor.
- Browse complete sheets by numeric sheet ID.
- Filter sheets by `All`, `Body`, `Hair`, `Eyes`, `Chest`, `Helm`, `Legs`, `Feet`, `Hand`, `Tiles`, and `Spells`.
- Select graphics directly on the sheet and show their metadata.
- Show all animations containing the selected graphic.
- Play, pause, and step through an animation at 8 FPS.
- Support scrolling, 25%–800% zoom, Fit, and 100% actions.

The viewer will not import, export, edit, or apply graphics to a map. Generated assets remain uncommitted and must be regenerated with the asset converter.

## Asset metadata

Add a versioned `Assets/Sprites/animation-manifest.json` beside `manifest.json`. The converter will emit it from the same source data and use the same normalized sheet and graphic IDs as the frame manifest.

The sidecar will contain:

- Sheet-to-category membership.
- Equipment category and equipment ID mappings.
- Animation IDs.
- Ordered `(sheet, graphic)` frame references.
- An 8 FPS playback rate matching the current runtime convention.

The map editor rendering library will parse the sidecar into backend-neutral models and build indexes for:

- Category to eligible sheets.
- Sheet to category and equipment metadata.
- Frame reference to matching animations.
- Animation ID to ordered frame sequence.

The `Spells` category will retain the legacy viewer's behavior: sheets with standalone animations that are not assigned to compiled equipment. This can include emotes or other animated effects because the source data does not distinguish them reliably.

Both converter commands that generate the sprite manifest, including the full `all` flow, will also generate the animation sidecar. Output will be deterministic. The viewer will strictly cross-validate sidecar frame references against `manifest.json` rather than introducing a multi-file transaction system.

## Window and interaction design

Add **Tools → Graphic Viewer**. One viewer window may exist per editor instance. Invoking the command while it is open focuses the existing window.

The initial window size will be approximately 1100×750 and will be resizable. It will contain:

- A top toolbar with category selector, editable sheet-ID selector, zoom selector, Fit, and 100% actions.
- A scrollable main sheet pane on a dark background with nearest-neighbor rendering, subtle frame outlines, and a prominent selected-frame outline.
- A details pane showing graphic ID, source rectangle, category mappings, equipment IDs, and matching animation selection.
- An animation pane beside the sheet with play/pause, previous frame, next frame, and frame-position controls.

Changing category filters the available sheet IDs. Changing sheets preserves zoom where practical and clears frame selection. Clicking a frame selects the lowest graphic ID whose rectangle contains the click. This preserves the legacy viewer's deterministic first-match behavior for overlapping rectangles.

Selecting a frame automatically selects and plays its first matching animation. A selector exposes any additional matching animations. A graphic without matching animations remains selectable and shows an empty animation state. Animation frames are bottom-center anchored within a stable preview area.

Mouse-wheel input scrolls the sheet and Ctrl+wheel zooms it. Space toggles playback while the viewer has focus. Playback and stepping wrap through the ordered frame list.

If a sheet maps to multiple equipment records, the details pane lists every category and ID mapping rather than choosing one.

## Lifecycle and failures

The viewer uses the current map-editor asset directory. The Avalonia layer loads only the current full-sheet PNG and disposes it when the sheet changes or the window closes. Animation frames resolve through the existing sprite cache.

The asset context controller will publish asset changes. An open viewer responds by stopping playback, clearing selection, disposing the prior sheet image, and loading the replacement manifests.

Opening the viewer shows an error and does not create the window when:

- No asset directory is loaded.
- `animation-manifest.json` is missing, malformed, or uses an unsupported version.
- The sidecar does not agree with `manifest.json`.

The requirement for the sidecar applies to the viewer, not to normal map editing. Existing asset folders remain usable by the editor but must be regenerated before opening the viewer.

A missing or unreadable selected PNG produces an inline error while leaving navigation available. If replacement assets are invalid while the viewer is open, the window remains open in an unavailable state and displays the reason.

Playback stops when paused, the window closes, the selected sheet changes, or assets become unavailable. Closing the editor closes and disposes the viewer.

## Testing

Converter tests will use hermetic fixtures to verify:

- Deterministic sidecar generation.
- Category and equipment mappings.
- Animation IDs and ordered frame references.
- Normalized Illutia and Aspereta references.
- Cross-sheet animation references where present.
- Legacy `Spells` classification.

Rendering-library tests will verify parsing, schema failures, duplicate handling, category indexes, reverse frame indexes, and strict cross-validation with `manifest.json`.

Avalonia headless tests will verify menu and singleton-window behavior, selection geometry, overlapping-frame selection, category filtering, sheet changes, zoom state, playback state, frame stepping, asset reload, missing images, and disposal. Tests will validate state and geometry rather than platform-specific rendered pixels.

The map-editor smoke guide will cover loading regenerated assets, opening the viewer, changing categories and sheets, selecting a graphic, zooming, and playing an animation.

## Known problem outside scope

The AssetConverter test project mixes unit tests with integration and golden tests that depend on external Illutia and Aspereta datasets. Eighteen of its twenty-five test files reference machine-local paths. Environment variables can redirect those paths, but tests neither isolate nor automatically skip missing datasets. Even with local data configured, four baseline tests currently fail because their pinned expectations have drifted from the installed datasets. Separating hermetic and external-data tests is desirable but is not part of this work.
