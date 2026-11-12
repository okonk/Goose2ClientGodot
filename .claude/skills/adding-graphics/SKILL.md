---
name: adding-graphics
description: Checklist for adding or changing game graphics — custom tile sheets, item icons, portraits, UI icons — so they reach the client, the map editor and the server's data editor. Use whenever a PNG is added to tools/AssetConverter/data/custom-assets or Assets/UI, a CustomAssetSheet is added or changed, or the user says new tiles/items/icons are not showing up somewhere.
---

# Adding graphics

A graphic has to land in up to three consumers: the Godot client, the map editor, and
the server repo's Google Sheets data editor. Several of the files involved are
gitignored and generated, so a missed step fails silently: the art just doesn't appear.

First decide which kind it is:

| Kind | Where it lives | Steps |
| --- | --- | --- |
| UI-only icon (buttons, HUD) | `Assets/UI/*.png`, referenced from a scene or script | 1 only |
| Map tile / scenery | custom sheet, `isTileSheet: true` | 2–6 |
| Item icon / inventory art | custom sheet, `isTileSheet: false` | 2–4, 7, 8 |
| Portrait / reward icon | existing custom sheet (30002 portraits, 30000 icons) | 2–4 |

## 1. UI-only icons

Drop the PNG in `Assets/UI/`, reference it from the scene, then import:

    /usr/bin/godot-mono --headless --path . --import --quit

Commit the `.png` and its `.import`. Nothing else consumes these.

## 2. Register the sheet

Put the cell PNGs in `tools/AssetConverter/data/custom-assets/` and register them in
`tools/AssetConverter/src/AssetConverter/Manifest/CustomAssetSheet.cs`: either a new
`CustomAssetSheet` (next free number after the highest `new(300xx`, and add it to `All`) or
a new graphic on an existing sheet. Graphic numbers already placed on maps or used by items
must never change.

**Layout matters.** The map editor palette lists a sheet's graphics in order and wraps at
the panel width; it ignores the sheet image's grid. The original sheets keep their shape
because empty cells are real, fully transparent graphics.

- Use `Numbered([...], columns: N)` and put `null` for every empty cell. A `null` is still
  emitted as a transparent graphic, which is what keeps multi-cell pieces (2x2 lakes, walls)
  lined up.
- A sheet derived from an original sheet must use the original's column count and cell
  positions. Most originals are 10 wide; e.g. 30020 mirrors the purple block of 2435.
- Before renumbering an existing custom sheet, check no map uses it: scan `Assets/Maps`,
  `../illutiagooseserver`, `../goosemaps` and `../Maps` for the sheet number. A `.map` is
  a 12-byte header (int16 version, int16 editor version, int32 width, int32 height), then
  per tile int32 flags + five (int32 graphic, int16 sheet).

## 3. Tests

Update `tools/AssetConverter/tests/AssetConverter.Tests/`:
- `CustomAssetSheetTests.cs`: the sheet's pixel size in
  `Write_ClearsHiddenColorOfFullyTransparentPixels`, plus any frame assertions.
- New tile sheet: the sheet lists in `AnimationManifestBuilderTests.cs` and
  `TileSheetGeneratorTests.cs`, and `TileSheetNumbers` in `CustomAssetSheetTests.cs`.

    dotnet test tools/AssetConverter/tests/AssetConverter.Tests

About 30 tests fail on this machine regardless (they want the original game data at
`/home/agent/workspace/...`). Compare the failure list against `git stash` rather than
expecting green.

## 4. Regenerate client data

`Assets/Sprites/manifest.json` is tracked; the sheet PNGs under `Assets/Sprites/sheets/`
are gitignored.

**Never run the converter's `manifest` subcommand against the real manifest.** It rebuilds
from Illutia data only and silently deletes every Aspereta sheet (20000+). Either run the
full pipeline (`all`, needs both Illutia and Aspereta data, see `docs/releasing.md`) or
generate into a scratch directory and copy only your sheets across:

    export ILLUTIA_DATA=/home/hayden/code/Illutia/data
    OUT=<scratch dir>
    dotnet run --project tools/AssetConverter/src/AssetConverter -- batch $OUT/sheets
    dotnet run --project tools/AssetConverter/src/AssetConverter -- manifest $OUT/manifest.json

Then copy `$OUT/sheets/<sheet>.png` into `Assets/Sprites/sheets/`, and copy only
`sheets["<sheet>"]` from `$OUT/manifest.json` into `Assets/Sprites/manifest.json`. The
manifest is single-line compact JSON; `json.dumps(m, separators=(',', ':'))` round-trips
it byte-for-byte. Check the sheet count is unchanged (other than any sheet you added).

A new tile sheet also needs its `"<sheet>": {"categories": [{"name": "Tiles"}]}` entry in
`Assets/Sprites/animation-manifest.json`; copy it from `$OUT/animation-manifest.json`.

Import into Godot:

    /usr/bin/godot-mono --headless --path . --import --quit

## 5. Map editor tile list

The map editor shows only the sheets listed in `Assets/Sprites/tile-sheets.json`
(gitignored). Add a new tile sheet's number by hand, keeping the list sorted.

Don't regenerate it with the converter's `tiles` command: that brings back sheets 3056
and 5216, which were removed on purpose (see `tile-sheets.json.bak-3056-5216`).

## 6. Rebuild the map editor

    GOOSE2_MAP_EDITOR_GOOGLE_OAUTH_CLIENT=$HOME/Downloads/google-oauth-client.json ./build-map-editor.sh

The archives bundle `Assets/Sprites` as it is on disk, so steps 4–5 must be done first.
Commit before building, or the build id ends in `-dirty`.

## 7. Item sheets: client and server lists

- Add the sheet to `tools/AssetConverter/data/item-tile-sheets.json` (client). This puts it
  under the ItemTiles category in `animation-manifest.json`.
- If it is an item-icon sheet, add it to `iconSheets` in
  `../illutiagooseserver/tools/SpriteBundle/sheets.json` (server repo, commit there).

## 8. Server data editor sprites

The Apps Script data editor in `../illutiagooseserver/tools/DataEditor` embeds the client's
art. Rebuild after any item or icon art change:

    cd ../illutiagooseserver
    dotnet run --project tools/SpriteBundle -- /home/hayden/code/Goose2ClientGodot/Assets/Sprites tools/DataEditor
    cd tools/DataEditor && node build.mjs && clasp push

The `sprites-*.html` bundles are gitignored for licensing reasons. Never `git add -f`
them. `.clasp.json` has `rootDir: dist`, so `clasp push` uploads the built `dist/`. See
`../illutiagooseserver/tools/README.md`.
