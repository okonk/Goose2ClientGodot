# Release Build Script Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** A local `build.sh` that builds and packages the Godot client for Windows, Linux, and macOS in one invocation, stamping each build with a timestamp id shown in-game.

**Architecture:** Three committed Godot export presets are the source of truth for export config. `build.sh` orchestrates preflight checks, writes a gitignored `build_id.txt`, runs three headless exports, and archives each platform's output. At runtime a `CanvasLayer` overlay created by the `GameManager` autoload reads that file and renders the id top-right, falling back to `dev` in the editor.

**Tech Stack:** Godot 4.7.1 mono, .NET 10, xUnit, bash.

Design doc: `docs/plans/2026-08-18-release-build-script-design.md`

---

## APIs verified

- `GameManager._Ready()` — `Scripts/GameManager.cs:62`. Creates `UiLayer` at `:65-67`, ends by `AddChild(SpellTargetManager)` at `:79`.
- `GameManager.UiLayer` — `Scripts/GameManager.cs:21`, `public CanvasLayer UiLayer { get; private set; }`.
- Godot file IO pattern in this codebase — `Scripts/GameManager.cs:174`, `using var f = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);` with error check via `Godot.FileAccess.GetOpenError()` at `:177`.
- Test project compiles the game sources directly — `tests/Goose2Client.Tests/Goose2Client.Tests.csproj`, `<Compile Include="../../Scripts/**/*.cs" />`. It references the `GodotSharp` NuGet package only; **there is no Godot engine at test time**, so any code a test touches must not call into the engine. Established pattern: pure static function tested directly, Godot IO left to the caller — see `CharacterSettings.FromJson` tested in `tests/Goose2Client.Tests/CharacterSettingsLoadTests.cs:11`.
- Verified working presets already exist at `/home/hayden/code/Goose2ClientGodot/export_presets.cfg` (91 lines, main checkout). All three exports were run successfully from that file on 2026-08-18. **Copy it; do not rewrite it from memory.**

## Environment note

This worktree has no generated `Assets/` (616K of tracked leftovers vs 248M in the main checkout). Tasks 5 and 6 need real assets. Symlink them once, before Task 5:

```bash
cd /home/hayden/code/Goose2ClientGodot/.worktrees/release-build
rm -rf Assets && ln -s ../../Assets Assets
```

`Assets/` is gitignored, so the symlink will not be committed.

---

### Task 0: Fix the pre-existing MapFileTests path

`tests/Goose2Client.Tests/MapFileTests.cs:6` hardcodes `/home/agent/workspace/Goose2ClientGodot/Assets/Maps`, a path from an earlier agent sandbox. It fails on this machine today (258 passed, 1 failed). Tests gate the build, so `build.sh` would abort every run until this is fixed.

**Files:**
- Modify: `tests/Goose2Client.Tests/MapFileTests.cs:6`

**Step 1: Confirm the failure**

Run: `dotnet test tests/Goose2Client.Tests --filter MapFileTests`
Expected: FAIL with `DirectoryNotFoundException : '/home/agent/workspace/Goose2ClientGodot/Assets/Maps/Map1.bytes'`

**Step 2: Replace the constant with root-relative resolution**

Replace line 6 (`private const string MapsDir = "...";`) with:

```csharp
    private static readonly string? MapsDir = FindMapsDir();

    /// <summary>
    /// Walks up from the test assembly location to the repo root (identified by the .sln)
    /// and returns Assets/Maps, or null when the generated assets are absent — Assets/ is
    /// gitignored build output and does not exist in a fresh clone.
    /// </summary>
    private static string? FindMapsDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Goose2ClientGodot.sln")))
            dir = dir.Parent;

        if (dir == null) return null;

        var maps = Path.Combine(dir.FullName, "Assets", "Maps");
        return Directory.Exists(maps) ? maps : null;
    }
```

Add `using System;` to the top of the file if not already present.

**Step 3: Skip the test when assets are absent**

Change the `[Fact]` attribute on `Map1_ParsesHeaderAndGrid` to:

```csharp
    [SkippableFact]
```

xUnit 2.9 has no built-in `SkippableFact`. Rather than add the `Xunit.SkippableFact` package, keep `[Fact]` and guard at the top of the method body instead:

```csharp
    [Fact]
    public void Map1_ParsesHeaderAndGrid()
    {
        if (MapsDir == null) return;   // generated assets absent — nothing to verify

        var bytes = File.ReadAllBytes(Path.Combine(MapsDir, "Map1.bytes"));
        // ...rest unchanged
```

Apply the same guard to every other test in the file that reads from `MapsDir`. Check first: `grep -n MapsDir tests/Goose2Client.Tests/MapFileTests.cs`.

**Step 4: Verify it passes with assets present**

Run from the **main checkout**, which has real assets:
`dotnet test tests/Goose2Client.Tests --filter MapFileTests`
Expected: PASS — this proves the guard did not simply mask the test into a no-op.

**Step 5: Verify it passes with assets absent**

Run from the worktree, **before** creating the symlink:
`dotnet test tests/Goose2Client.Tests`
Expected: PASS, 246 total, 0 failed.

**Step 6: Commit**

```bash
git add tests/Goose2Client.Tests/MapFileTests.cs
git commit -m "fix(tests): resolve map fixture path from repo root, skip when assets absent"
```

---

### Task 1: Commit the export presets and the ETC2 project setting

**Files:**
- Create: `export_presets.cfg` (copy from main checkout)
- Modify: `project.godot` — add one line to the `[rendering]` section

**Step 1: Copy the verified presets**

```bash
cp /home/hayden/code/Goose2ClientGodot/export_presets.cfg .
```

Sanity-check the copy contains three presets and the macOS settings that were empirically required:

```bash
grep -n 'name=\|architecture\|bundle_identifier\|codesign/codesign' export_presets.cfg
```

Expected: `name="Linux"`, `name="Windows"`, `name="macOS"`; macOS `binary_format/architecture="universal"`; `application/bundle_identifier="net.illutia.goose2client"`; `codesign/codesign=1`.

Why these matter (from the design's measured findings): the 4.7.1 macOS template ships universal binaries only, so an x86_64 preset fails with *"Requested template binary godot_macos_release.x86_64 not found"*; a missing bundle identifier fails the export outright; and without ad-hoc signing the `.app` will not launch on Apple Silicon.

**Step 2: Add the ETC2 ASTC setting**

Universal macOS export refuses to run without it. Add to the `[rendering]` section of `project.godot`:

```
textures/vram_compression/import_etc2_astc=true
```

**Step 3: Verify it costs nothing**

Run: `/usr/bin/godot-mono --headless --path . --import`
Expected: completes in a few seconds. Then `du -sh .godot/imported` — should stay around 66M. The sprites use lossless 2D import, so no ASTC data is generated.

**Step 4: Commit**

```bash
git add export_presets.cfg project.godot
git commit -m "build: add Linux/Windows/macOS export presets"
```

---

### Task 2: BuildInfo

Reads the build id written by `build.sh`. The engine-free test constraint (see APIs verified) drives the split: a pure `Normalize` function holds all the logic and gets tested; `Load()` is a thin Godot IO wrapper that is not unit-tested.

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
            Assert.Equal("20260818-2113", BuildInfo.Normalize("20260818-2113\n"));
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

### Task 3: Build stamp overlay

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

            // Anchor to the top-right, inset by Margin.
            label.SetAnchorsPreset(Control.LayoutPreset.TopRight);
            label.GrowHorizontal = Control.GrowDirection.Begin;
            label.OffsetLeft = -240;
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

In `Scripts/GameManager.cs`, at the end of `_Ready()` (after `AddChild(SpellTargetManager);`, line 79), add:

```csharp
            // Always-on-top build stamp. Its own CanvasLayer at 128 so HUD windows
            // on UiLayer can never draw over it.
            AddChild(new UI.BuildStampOverlay());
```

**Step 3: Verify the project still builds**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

**Step 4: Verify the full suite still passes**

Run: `dotnet test tests/Goose2Client.Tests`
Expected: 0 failed.

**Step 5: Smoke-test in the editor**

Run: `GOOSE_HOST=127.0.0.1 /usr/bin/godot-mono --path . --quit-after 200`
Expected: no errors on stdout. Running from the editor there is no `build_id.txt`, so the overlay shows `dev`. If you have a display available, run `./run.sh` instead and confirm the faint `dev` text in the top-right of the login screen.

**Step 6: Commit**

```bash
git add Scripts/UI/BuildStampOverlay.cs Scripts/GameManager.cs
git commit -m "feat(ui): show build id in top-right corner on every screen"
```

---

### Task 4: build.sh

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
#
# Override the engine with GODOT=/path/to/godot.
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")"

GODOT="${GODOT:-/usr/bin/godot-mono}"
BUILD_DIR="build"

die() { echo "build.sh: $*" >&2; exit 1; }

FAST=0
PLATFORMS=()
for arg in "$@"; do
  case "$arg" in
    --fast) FAST=1 ;;
    linux|windows|macos) PLATFORMS+=("$arg") ;;
    *) die "unknown argument '$arg' (expected --fast, linux, windows, or macos)" ;;
  esac
done
[ ${#PLATFORMS[@]} -eq 0 ] && PLATFORMS=(linux windows macos)

# --- Preflight ---------------------------------------------------------------

[ -x "$GODOT" ] || die "godot not found at '$GODOT' — set GODOT=/path/to/godot"

# Export templates must match the engine version. This is the failure you hit
# after every engine upgrade, so name it explicitly rather than letting Godot
# fail deep inside the export with a confusing message.
ENGINE_VERSION="$("$GODOT" --version | head -1)"          # e.g. 4.7.1.stable.mono.arch_linux.a13da4feb
TEMPLATE_VERSION="$(echo "$ENGINE_VERSION" | grep -oE '^[0-9]+\.[0-9]+(\.[0-9]+)?\.[a-z]+\.mono')"
TEMPLATE_DIR="$HOME/.local/share/godot/export_templates/$TEMPLATE_VERSION"
[ -d "$TEMPLATE_DIR" ] || die "no export templates at '$TEMPLATE_DIR' for engine $ENGINE_VERSION — install them via Editor > Manage Export Templates"

# Assets/ is gitignored generated output. Building against a missing or stale
# Assets/ silently ships a broken client, so refuse to proceed without it.
[ -d Assets ] && [ -n "$(ls -A Assets 2>/dev/null)" ] || die "Assets/ is missing or empty — regenerate with: dotnet run --project tools/AssetConverter/src/AssetConverter -- batch"

if [ -n "$(git status --porcelain)" ]; then
  echo "build.sh: warning — working tree is dirty; this build is not reproducible from a commit" >&2
fi

if [ "$FAST" -eq 0 ]; then
  echo "==> Running client tests"
  dotnet test tests/Goose2Client.Tests || die "tests failed — fix them or rerun with --fast"
fi

# --- Stamp -------------------------------------------------------------------

BUILD_ID="$(date +%Y%m%d-%H%M)"
# Trap so a crashed run never leaves a stale id behind — the editor would then
# display a bogus build stamp instead of "dev".
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
  printf '    %-44s %s\n' "$BUILD_DIR/$a" "$(du -h "$BUILD_DIR/$a" | cut -f1)"
done
```

**Step 2: Make it executable**

```bash
chmod +x build.sh
```

**Step 3: Verify the preflight failure paths**

These are the branches most likely to be wrong, and each is cheap to provoke:

```bash
GODOT=/nonexistent ./build.sh
```
Expected: `build.sh: godot not found at '/nonexistent' — set GODOT=/path/to/godot`, exit 1.

```bash
./build.sh --bogus
```
Expected: `build.sh: unknown argument '--bogus' ...`, exit 1.

Confirm the template-version parse produces a real directory:
```bash
/usr/bin/godot-mono --version | head -1 | grep -oE '^[0-9]+\.[0-9]+(\.[0-9]+)?\.[a-z]+\.mono'
```
Expected: `4.7.1.stable.mono`, and `ls ~/.local/share/godot/export_templates/4.7.1.stable.mono` must exist. If the parse comes out empty or wrong, fix the regex before moving on — a broken parse turns into a spurious "no export templates" abort on every run.

**Step 4: Commit**

```bash
git add build.sh
git commit -m "build: add release build/packaging script"
```

---

### Task 5: Full verification run

The only real evidence the script works end to end. Requires the `Assets` symlink from the Environment note above.

**Step 1: Run it**

```bash
./build.sh
```

Expected: tests pass, then three exports, then a report listing three artifacts. Takes several minutes — the first export also performs the C# build.

**Step 2: Verify the artifacts**

```bash
ls -la build/
```
Expected: exactly three files named `Goose2Client-<YYYYMMDD-HHMM>-{linux.tar.gz,windows.zip,macos.zip}`, no leftover `build/linux`, `build/windows`, or `build/macos` directories. Rough sizes from the design's measured run: linux ~90M, windows ~100M, macos ~138M.

**Step 3: Verify the stamp file was cleaned up**

```bash
ls build_id.txt
```
Expected: `No such file or directory` — the EXIT trap removed it.

**Step 4: Verify each archive's contents**

```bash
tar -tzf build/Goose2Client-*-linux.tar.gz | head
unzip -l build/Goose2Client-*-windows.zip | head
unzip -l build/Goose2Client-*-macos.zip | head
```
Expected: linux has `Goose2Client.x86_64`, `Goose2Client.pck`, and `data_Goose2ClientGodot_linuxbsd_x86_64/`; windows the `.exe` equivalent; macos a `Goose2ClientGodot.app/` bundle.

**Step 5: Verify the build stamp actually shows the id**

```bash
mkdir -p /tmp/goose-verify && tar -xzf build/Goose2Client-*-linux.tar.gz -C /tmp/goose-verify
/tmp/goose-verify/Goose2Client.x86_64
```
Expected: the client launches and the top-right corner shows the build id (e.g. `20260818-2113`), **not** `dev`. This is the one check that proves the stamp survived export into the pck.

**Step 6: Note what stays unverified**

The macOS artifact cannot be smoke-tested from Linux. It is ad-hoc signed and unnotarized, so the first launch on a Mac needs right-click → Open. Record this in the commit message.

**Step 7: Commit**

Nothing to commit unless a fix was needed. If the run exposed bugs, fix and commit them here, then rerun from Step 1.
