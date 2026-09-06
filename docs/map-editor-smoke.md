# Map Editor — Local Desktop Smoke Path

## Purpose and scope

Local (non-CI) smoke path for the standalone map editor (`src/MapEditor.App`). It covers the
automated gates, the headed editor checklist, and the no-RID all-platform publish plus
per-host launch checks. It explicitly does **not** cover CI, code signing/notarization, or
installers — artifacts are raw self-contained archives.

## Automated gates

Run in order from the repo root; all must pass before the headed checklist.

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

Expected: Core 216 passed, Rendering 171 passed, App 284 passed, Godot 460 passed, and the
same four suites green in the solution run; Release build 0 errors; `bash -n` clean;
script tests 62/62; the publisher produces a new `build/map-editor/<BUILD_ID>` release
directory containing all four archives plus `BUILD-METADATA.txt`; both greps return empty
(no Avalonia/App in Core/Rendering, no Godot in App); `git diff --check` clean;
`git status --short` clean (`build/` is gitignored).

## Headed checklist

Requires a display-equipped host. Run after the automated gates pass.

1. **Startup and asset picker.** `dotnet run --project src/MapEditor.App/MapEditor.App.csproj`.
   On first launch (no persisted converter path) the folder picker appears. Cancel: editor
   remains usable with placeholder sheets/palette. Select the converter `Assets/Sprites`
   folder: sprite sheets and palette load, and the selection persists to the OS path
   (visible in the settings store) so the next launch skips the picker.
2. **New map defaults and validation.** New map defaults to 100×100. Enter width/height 0
   or 1001 and confirm both are rejected. After an accepted change, verify the window title
   shows the dirty marker.
3. **Painting, undo/redo, flags.** Select a sheet/frame, paint a fast sparse drag, erase,
   eyedrop, and toggle a loop. Verify one undo step per drag (not per pixel), that redo
   restores it, and that unknown flag values are preserved through save and reopen.
4. **Pan and zoom.** Pan with middle mouse and with Space+left. Zoom with the wheel and the
   +/- keys through 25/50/100/200/400. Verify the cursor stays anchored to the tile under it
   and that tile edges stay crisp (nearest-neighbor, no bilinear blur).
5. **Layer and view toggles.** Toggle each layer, the grid, and blocked cells. Verify hover
   and selected-tile highlighting and the status readout update; verify tall bottom-center
   sprites render at full height and anchor to the bottom.
6. **Malformed input and conflict handling.** Open a malformed map and a bad asset root: the
   prior document/context remains intact. Make an external same-length change to a saved
   map and reopen it: verify the Overwrite / Save As / Cancel prompt. Point the app at
   unwritable destinations and settings and verify the editor state remains usable.
7. **Dirty prompts and shortcuts.** Exercise New / Open / close with a dirty document:
   Save / Discard / Cancel each behave. Verify the native keyboard shortcuts on Linux,
   Windows, and macOS.
8. **Blocked rectangle gesture.** Select the Blocked tool and drag across a rectangle:
   the preview fill shows the block colour on the cells to be blocked, and releasing blocks
   the whole rectangle as a single undo entry. Hold Shift and drag: the preview shows the
   unblock colour and releasing clears the rectangle. Press Escape mid-drag (or let the
   pointer capture be lost): the drag cancels, no tiles change, and no undo entry is made.
9. **Resize round trip with undo.** Open Edit → Resize Map, enter the new size using the
   offset and absolute entries, and apply: the map resizes and the view-model coordinates
   shift with it. Undo and then redo the resize: the document and the view-model
   coordinates both return to their prior state. Verify the discard warning appears when
   the resize would drop content.
10. **Publish and per-host launch.** Run the mandatory no-RID all-platform publisher
   (`./build-map-editor.sh --skip-tests`) and record inspection of all four archives
   (see below). Run/extract the local Linux archive on the Linux host; on each target host
   launch the corresponding Windows x64 / macOS x64 / macOS arm64 archive. Keep archive
   inspection and target-host launch results as separate entries. The macOS artifacts are
   unsigned/unnotarized and may require an OS override to launch.
11. **Tabs.** Open three maps as tabs. Edit one and confirm only its dot appears. Copy in
   one and paste in another. Reorder by dragging. Ctrl+Tab through them. Close a dirty
   tab and cancel, then discard. Close the last tab and confirm a fresh Untitled appears.
   Quit with two dirty maps and cancel on the second.

## Archive inspection vs target-host launch

### Archive inspection (dev host)

Cross-publish all four RIDs from one host and inspect the release directory
`build/map-editor/<BUILD_ID>/`:

- `BUILD-METADATA.txt`: `BUILD_ID`, `GIT_SHA`, `DIRTY`, `RIDS=linux-x64 windows-x64 osx-x64 osx-arm64`.
- Linux `map-editor-<BUILD_ID>-linux-x64.tar.gz`: single top-level `map-editor-linux-x64/`
  dir; self-contained (runtime + `libcoreclr.so` present); `MapEditor.App` executable
  (755); `MapEditor.App.dll`, `MapEditor.App.deps.json`, `MapEditor.App.runtimeconfig.json`
  present.
- Windows `map-editor-<BUILD_ID>-windows-x64.zip`: single top-level
  `map-editor-windows-x64/` dir; self-contained; `MapEditor.App.exe` + `.dll` +
  `deps.json`/`runtimeconfig.json` present.
- macOS `Goose2MapEditor-<BUILD_ID>-osx-{x64,arm64}.app.zip`: `Goose2MapEditor.app/` bundle
  with `Contents/Info.plist` (CFBundleExecutable `Goose2MapEditor`, id
  `com.goose2.mapeditor`) and `Contents/MacOS/Goose2MapEditor` executable (755) plus
  `Goose2MapEditor.deps.json`/`runtimeconfig.json`.
- All four: no Godot engine files and no asset directory entries.

### Target-host launches (separate per-host entries)

- **Linux (dev host):** extract the Linux archive and run the executable. On a
  display-equipped host the editor window opens. On a headless host the expected evidence
  is the .NET runtime starting and the app reaching Avalonia X11 initialization before
  failing on missing display libraries (`DllNotFoundException: libX11.so.6` or
  "cannot open display") — a display failure, not a build failure.
- **Windows x64:** on a Windows x64 host, extract the zip and run `MapEditor.App.exe`;
  record the result.
- **macOS x64 / macOS arm64:** on each macOS host, unzip the `.app.zip` and launch the
  app; record the result. Artifacts are unsigned/unnotarized; the OS may require an
  override (e.g. Gatekeeper/`xattr`) to launch.

## Run log

### 2026-09-03 — Linux (headless container, no display server, no root)

Automated gates (all run from the repo root, baseline `0cc732c`):

- Core tests: 166/166 passed.
- Rendering tests: 159/159 passed.
- App tests: 183/183 passed.
- Godot tests: 459/459 passed.
- Solution test run: all four suites green (159 + 166 + 183 + 459).
- `dotnet build Goose2ClientGodot.sln -c Release`: 0 errors.
- `bash -n build-map-editor.sh`: clean.
- `bash tests/build-map-editor-script-tests.sh`: 62/62 passed.
- `./build-map-editor.sh --skip-tests`: success; new release dir
  `build/map-editor/20260903T020200Z-0cc732c` with all four archives and
  `BUILD-METADATA.txt` (`DIRTY=0`, `GIT_SHA=0cc732c`).
- Both isolation greps returned empty. `git diff --check` clean; `git status --short`
  clean.

Archive inspection of `20260903T020200Z-0cc732c`:

- Linux tar.gz: top-level `map-editor-linux-x64/`, self-contained runtime present,
  `MapEditor.App` 755, `.dll`/`deps.json`/`runtimeconfig.json` present.
- Windows zip: top-level `map-editor-windows-x64/`, 221 files, `MapEditor.App.exe` +
  `.dll` + jsons present.
- Both macOS `.app.zip`s (225 files each): `Goose2MapEditor.app/` bundle layout,
  `Info.plist` correct, `Contents/MacOS/Goose2MapEditor` 755, jsons present.
- No Godot engine files or asset entries in any archive.

Linux target-host launch (headless): extracted the Linux archive and ran
`MapEditor.App`. The .NET runtime started and the app reached Avalonia X11 initialization,
then failed on the missing display library, as expected on this host:

```
Unhandled exception. System.DllNotFoundException: Unable to load shared library 'libX11.so.6' or one of its dependencies. ...
map-editor-linux-x64/libX11.so.6: cannot open shared object file: No such file or directory
   at Avalonia.X11.XLib.XInitThreads()
   at Avalonia.X11.AvaloniaX11Platform.Initialize(X11PlatformOptions options)
   at Avalonia.AvaloniaX11PlatformExtensions.<>c.<UseX11>b__0_0()
   at Avalonia.AppBuilder.SetupUnsafe()
   at Avalonia.AppBuilder.Setup()
   at Avalonia.AppBuilder.SetupWithLifetime(IApplicationLifetime lifetime)
   at Avalonia.ClassicDesktopStyleApplicationLifetimeExtensions.StartWithClassicDesktopLifetime(AppBuilder builder, String[] args, Action`1 lifetimeBuilder)
   at MapEditor.App.Program.Main(String[] args) in src/MapEditor.App/Program.cs:line 10
```

This proves the artifact runs; the failure is the absent X11 stack, not a build defect.

**PENDING (require a display-equipped or target host):**

- Headed checklist items 1–7 (folder picker, new-map validation, paint/undo/flags,
  pan/zoom, layer toggles, malformed-input/conflict handling, dirty prompts and native
  shortcuts on Linux/Windows/macOS).
- Headed checklist item 8 target-host launches: Windows x64, macOS x64, macOS arm64
  (separate per-host entries; macOS artifacts unsigned/unnotarized).
- A full headed Linux run on a display-equipped host to confirm the editor window opens.

### 2026-09-06 — Linux (headless container, no display server, non-root)

Automated gates (all run from the repo root, baseline `efdb669`):

- Core tests: 216/216 passed.
- Rendering tests: 171/171 passed.
- App tests: 374/374 passed.
- `./build-map-editor.sh --skip-tests linux-x64`: success; new release dir
  `build/map-editor/20260906T005823Z-efdb669` with the linux-x64 archive and
  `BUILD-METADATA.txt` (`DIRTY=0`, `GIT_SHA=efdb669`, `RIDS=linux-x64`).

Linux target-host launch (headless): extracted the Linux archive and ran
`MapEditor.App`. The .NET runtime started and the app reached Avalonia X11 initialization,
then failed on the missing display library, as expected on this host
(`DllNotFoundException: libX11.so.6`).

**PENDING (require a display-equipped or target host):**

- Headed checklist item 11 (tabs: open three maps, per-tab edit, copy/paste across tabs,
  drag reorder, Ctrl+Tab, dirty-tab close cancel/discard, last-tab close, quit with two
  dirty maps).
- The headed items still pending from the 2026-09-03 entry above.
