# Release Build Script Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** A local `build.sh` that builds and packages the Godot client for Windows, Linux, and macOS in one invocation, stamping each build with a commit-traceable id shown in-game.

**Architecture:** Three committed Godot export presets are the source of truth for export config. `build.sh` orchestrates preflight checks, writes a gitignored `build_id.txt`, runs three headless exports, and archives each platform's output. At runtime a `CanvasLayer` overlay created by the `GameManager` autoload reads that file and renders the id top-right, falling back to `dev` in the editor.

**Tech Stack:** Godot 4.7.1 mono; the game targets `net8.0` (`Goose2ClientGodot.csproj:3`) while the test project targets `net10.0` and pins `GodotSharp` 4.6.2 against a 4.7 engine (`tests/Goose2Client.Tests/Goose2Client.Tests.csproj:3,8`) — a pre-existing mismatch this plan does not address. xUnit 2.9, bash.

Design doc: `docs/plans/2026-08-18-release-build-script-design.md`

This plan incorporates review feedback dated 2026-08-18; see "Review decisions" below.

---

## APIs verified

- `GameManager._Ready()` — `Scripts/GameManager.cs:62`. Creates `UiLayer` at `:65-67`, ends by `AddChild(SpellTargetManager)` at `:79`.
- `GameManager.UiLayer` — `Scripts/GameManager.cs:21`, `public CanvasLayer UiLayer { get; private set; }`.
- Godot file IO pattern in this codebase — `Scripts/GameManager.cs:174`, `using var f = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);`, error checked via `Godot.FileAccess.GetOpenError()` at `:177`.
- Test project compiles the game sources directly — `tests/Goose2Client.Tests/Goose2Client.Tests.csproj`, `<Compile Include="../../Scripts/**/*.cs" />`. It references the `GodotSharp` NuGet package only; **there is no Godot engine at test time**, so anything a test touches must not call into the engine. Established pattern: pure static function tested directly, Godot IO left to the caller — see `CharacterSettings.FromJson` tested at `tests/Goose2Client.Tests/CharacterSettingsLoadTests.cs:11`.
- Asset generation entry point — `tools/AssetConverter/src/AssetConverter/Program.cs:138`, `if (args.Length >= 1 && args[0] == "all")`, taking an optional repo-root argument at `:140-141`. The `batch` subcommand only produces sprite sheets; `all` is the full pipeline (sheets, Aspereta monsters, maps).
- Asset source paths come from environment variables with sandbox defaults that do not exist on this machine — `tools/AssetConverter/src/AssetConverter/Paths.cs:10-17`: `ILLUTIA_DATA`, `ILLUTIA_MAPS`, `ASPERETA_DATA`, `ASPERETA_MAPS`. They must be set to run the converter here.

## Repository facts this plan depends on

Verified 2026-08-18:

- **Only `Assets/UI` is tracked** (66 files: PNGs, `.import` sidecars, `Assets/UI/Fonts/LiberationSans.ttf`). `Assets/Maps`, `Assets/Resources`, and `Assets/Sprites` are generated and gitignored. Never `rm -rf Assets` — it would stage 66 tracked deletions.
- **`Assets/Maps/Map1.bytes` is 1.8 MB** across 160 map files — too large to commit. Task 1 instead uses a 3412-byte fixture carved from it, committed at `tests/Goose2Client.Tests/Fixtures/Map10x10.bytes`.
- **The main checkout is on `master`** and cannot be used to verify changes made on this branch.
- **`export_presets.cfg` and `run.sh` are untracked** in the main checkout, so neither exists in this worktree.
- **Test totals differ by checkout and this is expected:** 259 in the main checkout, 246 here. The 13-test gap is uncommitted test work on `master` (`WindowButtonFlagsTests.cs` is untracked, several test files modified), not asset-dependent tests. Treat the exact totals as informational; what matters is **0 failed**.

## Review decisions

Settled with the user after review, overriding earlier choices in the design doc:

- **Build id is `<UTC>-<short-sha>[-dirty]`** (e.g. `20260818T091305Z-40f2dbe-dirty`), not bare `YYYYMMDD-HHMM`. Collision-free, monotonic, and traceable to a commit.
- **A dirty tree aborts the build** unless `--allow-dirty` is passed. Reverses the design doc's warn-and-proceed.
- **No SHA-256 checksums.** Local-only tool with no distribution channel yet; revisit when there is a download page.

---

### Task 0: Install generated assets in this worktree

Prerequisite for Tasks 1, 5, and 6. Symlink the three **generated** subdirectories individually, leaving tracked `Assets/UI` untouched.

**Step 1: Link the generated subdirectories**

```bash
cd /home/hayden/code/Goose2ClientGodot/.worktrees/release-build
for d in Maps Resources Sprites; do
  rm -rf "Assets/$d"
  ln -s "../../../Assets/$d" "Assets/$d"
done
```

The link target is relative to `Assets/`, hence three levels up: `Assets/` → worktree root → `.worktrees/` → repo root.

**Step 2: Verify no tracked file was disturbed**

```bash
git status --porcelain -- Assets
```
Expected: **empty output.** If anything appears, a tracked file under `Assets/UI` was clobbered — restore with `git checkout -- Assets` and investigate before continuing.

**Step 3: Verify the sentinels resolve**

```bash
for f in Assets/Maps/Map1.bytes Assets/Sprites/manifest.json Assets/Resources/AnimationHeights.txt; do
  [ -e "$f" ] && echo "ok $f" || echo "MISSING $f"
done
```
Expected: three `ok` lines.

Nothing to commit — `Assets/Maps`, `Assets/Resources`, and `Assets/Sprites` are gitignored.

---

### Task 1: Point MapFileTests at a committed fixture

`tests/Goose2Client.Tests/MapFileTests.cs:7` hardcodes `/home/agent/workspace/Goose2ClientGodot/Assets/Maps`, a path from an earlier agent sandbox, and reads the 1.8 MB `Map1.bytes` out of gitignored generated output. It fails on this machine today. Tests gate the build, so `build.sh` would abort on every run until this is fixed.

The fixture and its generator are **already committed on this branch** (see below), so the test becomes independent of generated assets — no skip mechanism, no new package, and it runs in a fresh clone.

**Files:**
- Already present: `tests/Goose2Client.Tests/Fixtures/Map10x10.bytes` (3412 bytes), `tools/gen-map-fixture.py`
- Modify: `tests/Goose2Client.Tests/MapFileTests.cs`
- Modify: `tests/Goose2Client.Tests/Goose2Client.Tests.csproj` (copy the fixture to the output directory)

**About the fixture.** It is the real 10x10 tile region at (row 100, col 100) of `Assets/Maps/Map1.bytes` with the header dimensions rewritten — genuine game data, not bytes synthesized from a reading of the format, so a parser change is still checked against a real file. That region was chosen for variety: 45 open tiles, 6 blocked, 49 carrying flag 16. Regenerate with `python3 tools/gen-map-fixture.py` from the repo root with assets present; output is deterministic.

Verified fixture values: version 146, editor version 10, 10x10, tile[0,0] flags 0 with layer 0 = graphic 421500 / sheet 2286, and 6 tiles with the blocked bit set.

**Step 1: Confirm the current failure**

Run: `dotnet test tests/Goose2Client.Tests --filter MapFileTests`
Expected: FAIL — `DirectoryNotFoundException : '/home/agent/workspace/Goose2ClientGodot/Assets/Maps/Map1.bytes'`. It fails even with assets linked, because the path is absolute and wrong.

**Step 2: Copy the fixture to the test output directory**

In `tests/Goose2Client.Tests/Goose2Client.Tests.csproj`, add a new `ItemGroup`:

```xml
  <ItemGroup>
    <None Include="Fixtures/**" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
```

**Step 3: Rewrite the test against the fixture**

Replace the `MapsDir` constant (line 7) and the body of `Map1_ParsesHeaderAndGrid` with:

```csharp
    private static string FixturePath =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Map10x10.bytes");

    [Fact]
    public void Fixture_ParsesHeaderAndGrid()
    {
        var bytes = File.ReadAllBytes(FixturePath);
        var map = new MapFile(bytes);

        // Real values carved from Map1.bytes — see tools/gen-map-fixture.py.
        Assert.Equal(146, map.Version);
        Assert.Equal(10, map.EditorVersion);
        Assert.Equal(10, map.Width);
        Assert.Equal(10, map.Height);
        Assert.Equal(100, map.Tiles.Length);

        // header(12) + 34 bytes/tile, exactly — the carved fixture has no trailer.
        Assert.Equal(12 + 34 * map.Width * map.Height, bytes.Length);

        // Indexer is (x, y) -> Tiles[y*Width + x]; first tile is reachable and well-formed.
        var t = map[0, 0];
        Assert.Equal(5, t.Layers.Length);
        Assert.All(t.Layers, l => Assert.NotNull(l));
        Assert.Equal(421500, t.Layers[0].Graphic);
        Assert.Equal(2286, t.Layers[0].Sheet);
        Assert.False(t.IsBlocked);

        // The region was picked for variety: some tiles carry the blocked bit.
        Assert.Equal(6, map.Tiles.Count(x => x.IsBlocked));
    }
```

Add `using System;` and `using System.Linq;` to the top of the file. `MapTile_FlagsAndRoofDerive` is unchanged — it already covers `IsRoof`, which no tile in this region exercises.

**Step 4: Run the test**

Run: `dotnet test tests/Goose2Client.Tests --filter MapFileTests`
Expected: PASS, 2 tests, 0 skipped.

**Step 5: Prove it no longer depends on generated assets**

```bash
mv Assets/Maps /tmp/maps-parked
dotnet test tests/Goose2Client.Tests --filter MapFileTests
mv /tmp/maps-parked Assets/Maps
git status --porcelain -- Assets   # must be empty
```
Expected: PASS with the maps absent. This is the point of the change — the suite runs in a fresh clone.

**Step 6: Verify the whole suite**

Run: `dotnet test tests/Goose2Client.Tests`
Expected: 0 failed.

**Step 7: Commit**

```bash
git add tests/Goose2Client.Tests/MapFileTests.cs tests/Goose2Client.Tests/Goose2Client.Tests.csproj
git commit -m "test(map): parse a committed 10x10 fixture instead of generated assets"
```

---

### Task 2: Commit the export presets and the ETC2 project setting

The presets below are the reviewed source of truth, reproduced in full so this plan does not depend on an untracked file in a dirty checkout. They are the exact contents that exported all three platforms successfully on 2026-08-18.

**Files:**
- Create: `export_presets.cfg`
- Modify: `project.godot` — one line in `[rendering]`

**Step 1: Write the presets**

Create `export_presets.cfg` with exactly:

```ini
[preset.0]

name="Linux"
platform="Linux"
runnable=true
advanced_options=false
dedicated_server=false
custom_features=""
export_filter="all_resources"
include_filter=""
exclude_filter=""
export_path="build/linux/Goose2Client.x86_64"
patches=PackedStringArray()
encryption_include_filters=""
encryption_exclude_filters=""
seed=0
encrypt_pck=false
encrypt_directory=false
script_export_mode=2

[preset.0.options]

binary_format/embed_pck=false
binary_format/architecture="x86_64"
dotnet/include_scripts_content=false
dotnet/include_debug_symbols=false
dotnet/embed_build_outputs=false

[preset.1]

name="Windows"
platform="Windows Desktop"
runnable=true
advanced_options=false
dedicated_server=false
custom_features=""
export_filter="all_resources"
include_filter=""
exclude_filter=""
export_path="build/windows/Goose2Client.exe"
patches=PackedStringArray()
encryption_include_filters=""
encryption_exclude_filters=""
seed=0
encrypt_pck=false
encrypt_directory=false
script_export_mode=2

[preset.1.options]

binary_format/embed_pck=false
binary_format/architecture="x86_64"
codesign/enable=false
application/console_wrapper_icon=""
dotnet/include_scripts_content=false
dotnet/include_debug_symbols=false
dotnet/embed_build_outputs=false

[preset.2]

name="macOS"
platform="macOS"
runnable=true
advanced_options=false
dedicated_server=false
custom_features=""
export_filter="all_resources"
include_filter=""
exclude_filter=""
export_path="build/macos/Goose2Client.zip"
patches=PackedStringArray()
encryption_include_filters=""
encryption_exclude_filters=""
seed=0
encrypt_pck=false
encrypt_directory=false
script_export_mode=2

[preset.2.options]

export/distribution_type=0
application/bundle_identifier="net.illutia.goose2client"
application/short_version="1.0.0"
application/version="1.0.0"
binary_format/architecture="universal"
codesign/codesign=1
codesign/enable=false
notarization/notarization=0
dotnet/include_scripts_content=false
dotnet/include_debug_symbols=false
dotnet/embed_build_outputs=false
```

Why the macOS settings are what they are, from the design's measured findings: the 4.7.1 macOS template ships **universal binaries only**, so an x86_64 preset fails with *"Requested template binary godot_macos_release.x86_64 not found"*; a missing `bundle_identifier` fails the export outright; and without ad-hoc signing (`codesign/codesign=1`) the `.app` will not launch on Apple Silicon at all.

**Step 2: Add the ETC2 ASTC setting**

Universal macOS export refuses to run without it. Add to the `[rendering]` section of `project.godot`:

```
textures/vram_compression/import_etc2_astc=true
```

**Step 3: Verify it costs nothing**

```bash
du -sh .godot/imported 2>/dev/null || echo "no import cache yet"
/usr/bin/godot-mono --headless --path . --import
du -sh .godot/imported
```
Expected: the import completes in seconds and the cache does not grow meaningfully between the two measurements. The sprites use lossless 2D import, so no ASTC data is generated. (The design doc's "around 66M" figure was measured in the main checkout; this worktree builds its own cache from scratch on first import, so compare before-and-after here rather than against that number.)

**Step 4: Commit**

```bash
git add export_presets.cfg project.godot
git commit -m "build: add Linux/Windows/macOS export presets"
```

---

### Task 3: BuildInfo

Reads the build id written by `build.sh`. The engine-free test constraint drives the split: a pure `Normalize` holds the logic and is tested; `Load` is a thin Godot IO wrapper that is not unit-tested.

**Files:**
- Create: `Scripts/BuildInfo.cs`
- Test: `tests/Goose2Client.Tests/BuildInfoTests.cs`

**Step 1: Write the failing test**

```csharp
using Xunit;

namespace Goose2Client.Tests
{
    public class BuildInfoTests
    {
        [Fact]
        public void Normalize_NullInput_ReturnsDev()
        {
            Assert.Equal("dev", BuildInfo.Normalize(null));
        }

        [Fact]
        public void Normalize_EmptyOrWhitespace_ReturnsDev()
        {
            Assert.Equal("dev", BuildInfo.Normalize(""));
            Assert.Equal("dev", BuildInfo.Normalize("   \n"));
        }

        [Fact]
        public void Normalize_TrimsSurroundingWhitespace()
        {
            Assert.Equal("20260818T091305Z-40f2dbe", BuildInfo.Normalize("20260818T091305Z-40f2dbe\n"));
        }
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/Goose2Client.Tests --filter BuildInfoTests`
Expected: FAIL — compile error, `BuildInfo` does not exist.

**Step 3: Write the implementation**

`Scripts/BuildInfo.cs`:

```csharp
namespace Goose2Client
{
    /// <summary>
    /// The build identifier stamped by build.sh, or "dev" when running from the editor.
    /// </summary>
    public static class BuildInfo
    {
        private const string BuildIdPath = "res://build_id.txt";

        private static string? cached;

        /// <summary>Build id for display. Read once, then cached for the process lifetime.</summary>
        public static string Id => cached ??= Normalize(ReadBuildIdFile());

        /// <summary>
        /// Pure fallback logic, split out so it is testable — the test project has no Godot
        /// engine and cannot call FileAccess.
        /// </summary>
        public static string Normalize(string? raw)
            => string.IsNullOrWhiteSpace(raw) ? "dev" : raw.Trim();

        private static string? ReadBuildIdFile()
        {
            if (!Godot.FileAccess.FileExists(BuildIdPath)) return null;

            using var f = Godot.FileAccess.Open(BuildIdPath, Godot.FileAccess.ModeFlags.Read);
            return f?.GetAsText();
        }
    }
}
```

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/Goose2Client.Tests --filter BuildInfoTests`
Expected: PASS, 3 tests.

**Step 5: Gitignore the stamp file**

Add to `.gitignore`, under the existing `# ---- Godot 4+ ----` block:

```
# Build stamp written by build.sh, removed on exit
/build_id.txt
```

**Step 6: Commit**

```bash
git add Scripts/BuildInfo.cs tests/Goose2Client.Tests/BuildInfoTests.cs .gitignore
git commit -m "feat(build): add BuildInfo build id reader with dev fallback"
```

---

### Task 4: Build stamp overlay

**Files:**
- Create: `Scripts/UI/BuildStampOverlay.cs`
- Modify: `Scripts/GameManager.cs:62-79` (`_Ready`)

**Step 1: Write the overlay**

Layer 128 keeps it above `UiLayer` (default layer 1), so HUD windows can never cover it. `MouseFilter.Ignore` keeps it from eating clicks. Nothing currently occupies the top-right corner.

`Scripts/UI/BuildStampOverlay.cs`:

```csharp
using Godot;

namespace Goose2Client.UI
{
    /// <summary>
    /// Always-on-top build identifier, drawn in the top-right corner of every screen.
    /// Owned by the GameManager autoload so it survives scene swaps.
    /// </summary>
    public partial class BuildStampOverlay : CanvasLayer
    {
        private const int Margin = 6;

        public override void _Ready()
        {
            Layer = 128;
            Name = "BuildStampOverlay";

            var label = new Label
            {
                Name = "BuildIdLabel",
                Text = BuildInfo.Id,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                HorizontalAlignment = HorizontalAlignment.Right,
            };

            // Anchor to the top-right, inset by Margin. Wide enough for the longest id
            // form: <UTC>-<short-sha>-dirty.
            label.SetAnchorsPreset(Control.LayoutPreset.TopRight);
            label.GrowHorizontal = Control.GrowDirection.Begin;
            label.OffsetLeft = -320;
            label.OffsetRight = -Margin;
            label.OffsetTop = Margin;
            label.OffsetBottom = Margin + 20;

            label.Modulate = new Color(1, 1, 1, 0.45f);

            AddChild(label);
        }
    }
}
```

**Step 2: Wire it into GameManager**

At the end of `_Ready()` in `Scripts/GameManager.cs` (after `AddChild(SpellTargetManager);`, line 79):

```csharp
            // Always-on-top build stamp. Its own CanvasLayer at 128 so HUD windows
            // on UiLayer can never draw over it.
            AddChild(new UI.BuildStampOverlay());
```

**Step 3: Verify the project builds**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

**Step 4: Verify the suite still passes**

Run: `dotnet test tests/Goose2Client.Tests`
Expected: 0 failed.

**Step 5: Defer the visual check**

There is no `run.sh` in this worktree (untracked in the main checkout), and a headless run cannot show the overlay. The real visual confirmation is Task 6 Step 5, which launches the exported Linux client and reads the stamp off the screen. Do not fabricate a smoke test here.

If you want an early look and have a display, run the editor directly — expect a connection failure to the default server, which is unrelated to the overlay:

```bash
GOOSE_HOST=127.0.0.1 GOOSE_PORT=2006 /usr/bin/godot-mono --path . --display-driver wayland
```
Expected: faint `dev` text in the top-right of the login screen.

**Step 6: Commit**

```bash
git add Scripts/UI/BuildStampOverlay.cs Scripts/GameManager.cs
git commit -m "feat(ui): show build id in top-right corner on every screen"
```

---

### Task 5: build.sh

**Files:**
- Create: `build.sh` (repo root, mode 755)

**Step 1: Write the script**

```bash
#!/usr/bin/env bash
# Builds and packages the client for release. Local dev tool — not shipped in the build.
#
#   ./build.sh                    # all three platforms
#   ./build.sh linux windows      # only those
#   ./build.sh --fast             # skip the test suite
#   ./build.sh --allow-dirty      # permit uncommitted changes (marks the build id -dirty)
#
# Override the engine with GODOT=/path/to/godot.
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")"

GODOT="${GODOT:-/usr/bin/godot-mono}"
BUILD_DIR="build"
PRESETS_FILE="export_presets.cfg"

die() { echo "build.sh: $*" >&2; exit 1; }

FAST=0
ALLOW_DIRTY=0
REQUESTED=()
for arg in "$@"; do
  case "$arg" in
    --fast)        FAST=1 ;;
    --allow-dirty) ALLOW_DIRTY=1 ;;
    linux|windows|macos) REQUESTED+=("$arg") ;;
    *) die "unknown argument '$arg' (expected --fast, --allow-dirty, linux, windows, or macos)" ;;
  esac
done
[ ${#REQUESTED[@]} -eq 0 ] && REQUESTED=(linux windows macos)

# Deduplicate while preserving order, so `./build.sh linux linux` exports once.
PLATFORMS=()
for p in "${REQUESTED[@]}"; do
  case " ${PLATFORMS[*]-} " in *" $p "*) continue ;; esac
  PLATFORMS+=("$p")
done

# --- Preflight ---------------------------------------------------------------

for cmd in dotnet git tar zip du; do
  command -v "$cmd" >/dev/null || die "required command '$cmd' not found on PATH"
done

[ -x "$GODOT" ] || die "godot not found at '$GODOT' — set GODOT=/path/to/godot"

# Export templates must match the engine version. This is the failure you hit after
# every engine upgrade, so name it explicitly rather than letting Godot fail deep
# inside the export with a confusing message.
ENGINE_VERSION="$("$GODOT" --version | head -1)"   # e.g. 4.7.1.stable.mono.arch_linux.a13da4feb
TEMPLATE_VERSION="$(echo "$ENGINE_VERSION" | grep -oE '^[0-9]+\.[0-9]+(\.[0-9]+)?\.[a-z]+\.mono')" \
  || die "could not parse a template version out of engine version '$ENGINE_VERSION'"
[ -n "$TEMPLATE_VERSION" ] || die "could not parse a template version out of engine version '$ENGINE_VERSION'"

# Linux/XDG layout. Godot uses ~/.local/share unless XDG_DATA_HOME is set; macOS and
# Windows hosts use different locations and are not supported by this script.
TEMPLATE_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/godot/export_templates/$TEMPLATE_VERSION"
[ -d "$TEMPLATE_DIR" ] || die "no export templates at '$TEMPLATE_DIR' for engine $ENGINE_VERSION — install them via Editor > Manage Export Templates"

[ -f "$PRESETS_FILE" ] || die "$PRESETS_FILE not found"
for preset in Linux Windows macOS; do
  grep -q "^name=\"$preset\"\$" "$PRESETS_FILE" || die "preset '$preset' missing from $PRESETS_FILE"
done

# Assets/ is generated, gitignored output. A non-empty Assets/ is not enough — the
# tracked Assets/UI alone would satisfy that while the client is still unshippable.
# Check one sentinel per generated subtree instead.
for sentinel in Assets/Maps/Map1.bytes Assets/Sprites/manifest.json Assets/Resources/AnimationHeights.txt; do
  [ -e "$sentinel" ] || die "missing generated asset '$sentinel' — regenerate with:
    ILLUTIA_DATA=... ILLUTIA_MAPS=... ASPERETA_DATA=... ASPERETA_MAPS=... \\
      dotnet run --project tools/AssetConverter/src/AssetConverter -- all \"\$PWD\"
  (see tools/AssetConverter/src/AssetConverter/Paths.cs for the source paths)"
done

DIRTY=0
if [ -n "$(git status --porcelain)" ]; then
  DIRTY=1
  [ "$ALLOW_DIRTY" -eq 1 ] || die "working tree is dirty — commit, or rerun with --allow-dirty"
  echo "build.sh: warning — building from a dirty tree; the build id will be marked -dirty" >&2
fi

if [ "$FAST" -eq 0 ]; then
  echo "==> Running client tests"
  dotnet test tests/Goose2Client.Tests || die "tests failed — fix them or rerun with --fast"
fi

# --- Stamp -------------------------------------------------------------------

BUILD_ID="$(date -u +%Y%m%dT%H%M%SZ)-$(git rev-parse --short HEAD)"
[ "$DIRTY" -eq 1 ] && BUILD_ID="$BUILD_ID-dirty"
# Trap so a crashed or failed run never leaves a stale id behind — the editor would
# then display a bogus build stamp instead of "dev".
trap 'rm -f build_id.txt' EXIT
printf '%s\n' "$BUILD_ID" > build_id.txt
echo "==> Build id $BUILD_ID"

# --- Export ------------------------------------------------------------------

rm -rf "$BUILD_DIR"
mkdir -p "$BUILD_DIR"

export_platform() {
  local plat="$1" preset="$2" binary="$3"
  echo "==> Exporting $plat"
  mkdir -p "$BUILD_DIR/$plat"
  "$GODOT" --headless --path . --export-release "$preset" "$BUILD_DIR/$plat/$binary" \
    || die "$plat export failed"
}

archive_platform() {
  local plat="$1"
  local out
  case "$plat" in
    linux)
      out="Goose2Client-$BUILD_ID-linux.tar.gz"
      # tar, not zip: preserves the executable bit on the binary.
      tar -czf "$BUILD_DIR/$out" -C "$BUILD_DIR/$plat" .
      ;;
    windows)
      out="Goose2Client-$BUILD_ID-windows.zip"
      (cd "$BUILD_DIR/$plat" && zip -qr "../$out" .)
      ;;
    macos)
      # Godot already emits a self-contained, ad-hoc-signed .zip of the .app.
      out="Goose2Client-$BUILD_ID-macos.zip"
      mv "$BUILD_DIR/$plat/Goose2Client.zip" "$BUILD_DIR/$out"
      ;;
  esac
  rm -rf "${BUILD_DIR:?}/$plat"
  echo "$out"
}

ARTIFACTS=()
for plat in "${PLATFORMS[@]}"; do
  case "$plat" in
    linux)   export_platform linux   "Linux"   "Goose2Client.x86_64" ;;
    windows) export_platform windows "Windows" "Goose2Client.exe" ;;
    macos)   export_platform macos   "macOS"   "Goose2Client.zip" ;;
  esac
  ARTIFACTS+=("$(archive_platform "$plat")")
done

# --- Report ------------------------------------------------------------------

echo
echo "==> Build $BUILD_ID complete"
for a in "${ARTIFACTS[@]}"; do
  printf '    %-56s %s\n' "$BUILD_DIR/$a" "$(du -h "$BUILD_DIR/$a" | cut -f1)"
done
```

**Step 2: Make it executable and syntax-check it**

```bash
chmod +x build.sh
bash -n build.sh
```
Expected: no output from `bash -n`.

**Step 3: Verify the preflight failure paths**

These branches are the most likely to be wrong and each is cheap to provoke. Every one must exit non-zero with the named message.

```bash
GODOT=/nonexistent ./build.sh; echo "exit=$?"
```
Expected: `godot not found at '/nonexistent' ...`, exit 1.

```bash
./build.sh --bogus; echo "exit=$?"
```
Expected: `unknown argument '--bogus' ...`, exit 1.

```bash
mv export_presets.cfg /tmp/presets-parked && ./build.sh; echo "exit=$?"
mv /tmp/presets-parked export_presets.cfg
```
Expected: `export_presets.cfg not found`, exit 1.

```bash
sed -i 's/^name="macOS"$/name="macOSX"/' export_presets.cfg && ./build.sh; echo "exit=$?"
git checkout -- export_presets.cfg
```
Expected: `preset 'macOS' missing from export_presets.cfg`, exit 1.

```bash
mv Assets/Maps /tmp/maps-parked && ./build.sh; echo "exit=$?"
mv /tmp/maps-parked Assets/Maps
```
Expected: `missing generated asset 'Assets/Maps/Map1.bytes'` plus the regeneration command, exit 1.

```bash
./build.sh --fast   # with the tree dirty from in-progress work
```
Expected: `working tree is dirty — commit, or rerun with --allow-dirty`, exit 1. Then confirm `./build.sh --fast --allow-dirty` gets past the check and prints a build id ending in `-dirty`.

Confirm the template-version parse resolves to a real directory — a broken regex turns into a spurious "no export templates" abort on every run:
```bash
/usr/bin/godot-mono --version | head -1 | grep -oE '^[0-9]+\.[0-9]+(\.[0-9]+)?\.[a-z]+\.mono'
ls -d "${XDG_DATA_HOME:-$HOME/.local/share}/godot/export_templates/4.7.1.stable.mono"
```
Expected: `4.7.1.stable.mono`, and the directory exists.

Verify duplicate arguments export once:
```bash
bash -x ./build.sh linux linux --fast 2>&1 | grep -c "==> Exporting linux"
```
Expected: `1`.

**Step 4: Verify the trap fires on a failed export**

```bash
mv "${XDG_DATA_HOME:-$HOME/.local/share}/godot/export_templates/4.7.1.stable.mono/linux_release.x86_64" /tmp/
./build.sh linux --fast --allow-dirty; echo "exit=$?"
ls build_id.txt
mv /tmp/linux_release.x86_64 "${XDG_DATA_HOME:-$HOME/.local/share}/godot/export_templates/4.7.1.stable.mono/"
```
Expected: the export fails, exit non-zero, and `ls build_id.txt` reports **No such file or directory** — the EXIT trap cleaned up despite the failure.

**Step 5: Commit**

```bash
git add build.sh
git commit -m "build: add release build/packaging script"
```

---

### Task 6: Full verification run

The only real evidence the script works end to end. Requires Task 0's asset links.

**Step 1: Run it**

```bash
./build.sh
```

Expected: tests pass, three exports, then a report listing three artifacts. Several minutes — the first export also performs the C# build. If the tree is dirty from in-progress work, use `--allow-dirty` and expect a `-dirty` build id.

**Step 2: Verify the artifacts**

```bash
ls -la build/
```
Expected: exactly three files, `Goose2Client-<id>-{linux.tar.gz,windows.zip,macos.zip}`, with no leftover `build/linux`, `build/windows`, or `build/macos` directories. Rough sizes measured on 2026-08-18: linux ~90M, windows ~100M, macos ~138M.

**Step 3: Verify the stamp file was cleaned up**

```bash
ls build_id.txt
```
Expected: `No such file or directory`.

**Step 4: Verify archive integrity and contents**

```bash
tar -tzf build/Goose2Client-*-linux.tar.gz >/dev/null && echo "linux archive ok"
unzip -t build/Goose2Client-*-windows.zip >/dev/null && echo "windows archive ok"
unzip -t build/Goose2Client-*-macos.zip >/dev/null && echo "macos archive ok"

tar -tzf build/Goose2Client-*-linux.tar.gz | head
unzip -l build/Goose2Client-*-windows.zip | head
unzip -l build/Goose2Client-*-macos.zip | head
```
Expected: three `ok` lines; linux contains `Goose2Client.x86_64`, `Goose2Client.pck`, and `data_Goose2ClientGodot_linuxbsd_x86_64/`; windows the `.exe` equivalent; macos a `Goose2ClientGodot.app/` bundle.

**Step 5: Verify the executable bit survived the tar**

```bash
tar -tvzf build/Goose2Client-*-linux.tar.gz | grep 'Goose2Client\.x86_64$'
```
Expected: permissions beginning `-rwx`. This is the whole reason Linux uses tar rather than zip; if it shows `-rw-`, the archive is broken for users.

**Step 6: Verify the build stamp reaches the running client**

```bash
VERIFY_DIR="$(mktemp -d)"
tar -xzf build/Goose2Client-*-linux.tar.gz -C "$VERIFY_DIR"
"$VERIFY_DIR/Goose2Client.x86_64"
```
Expected: the client launches and the top-right corner shows the build id (e.g. `20260818T091305Z-40f2dbe`), **not** `dev`. This is the one check proving the stamp survived export into the pck. Clean up with `rm -rf "$VERIFY_DIR"` afterwards.

**Step 7: Note what stays unverified**

The macOS artifact cannot be smoke-tested from Linux. It is ad-hoc signed and unnotarized, so its first launch on a Mac needs right-click → Open. This belongs in the release doc (Task 7), not only in a commit message.

**Step 8: Commit**

Nothing to commit unless a fix was needed. If the run exposed bugs, fix and commit them here, then rerun from Step 1.

---

### Task 7: Release documentation

`build.sh --help` output and commit messages are not durable documentation for a process someone runs a few times a year.

**Files:**
- Create: `docs/releasing.md`

**Step 1: Write the document**

Cover, briefly and concretely:

- **Host requirements** — Linux only; the template lookup assumes the XDG layout. `dotnet`, `git`, `tar`, `zip`, `du` on PATH, plus a Godot mono build.
- **Godot and export templates** — the engine version and its matching mono templates must agree; install via Editor → Manage Export Templates. Name the current pair (4.7.1).
- **Asset generation** — `Assets/Maps`, `Assets/Resources`, and `Assets/Sprites` are generated and gitignored; only `Assets/UI` is tracked. Give the full command with the `ILLUTIA_*`/`ASPERETA_*` environment variables, citing `tools/AssetConverter/src/AssetConverter/Paths.cs:10-17`.
- **Running a build** — the four invocation forms, what `--fast` and `--allow-dirty` skip, and what the build id means (including `-dirty`).
- **Outputs** — the three artifact names and their approximate sizes.
- **macOS limitations** — universal, ad-hoc signed, unnotarized; first launch needs right-click → Open; proper signing and notarization would need an Apple Developer account and are not implemented.
- **Validating artifacts** — the integrity, exec-bit, and build-stamp checks from Task 6.

**Step 2: Commit**

```bash
git add docs/releasing.md
git commit -m "docs: how to build and package a release"
```
