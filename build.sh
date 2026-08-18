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
STAGING_DIR="build/.staging"
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
trap 'rm -f build_id.txt; rm -rf "${STAGING_DIR:?}"' EXIT
printf '%s\n' "$BUILD_ID" > build_id.txt
echo "==> Build id $BUILD_ID"

# --- Export ------------------------------------------------------------------

rm -rf "$BUILD_DIR"
mkdir -p "$BUILD_DIR"

# Everything is built and archived under staging, then published into $BUILD_DIR only
# once every requested platform has succeeded. A half-finished run must not leave a
# partial release set behind — one platform's archive alone is worse than nothing.
mkdir -p "$STAGING_DIR"

export_platform() {
  local plat="$1" preset="$2" binary="$3"
  echo "==> Exporting $plat"
  mkdir -p "$STAGING_DIR/$plat"
  "$GODOT" --headless --path . --export-release "$preset" "$STAGING_DIR/$plat/$binary" \
    || die "$plat export failed"
}

# Sets ARTIFACT_NAME rather than echoing it: bash does not propagate errexit out of a
# command substitution, so `X=$(archive_platform ...)` would swallow a failed tar/zip/mv
# and report a build that never produced an archive as a success.
ARTIFACT_NAME=""
archive_platform() {
  local plat="$1"
  local out
  case "$plat" in
    linux)
      out="Goose2Client-$BUILD_ID-linux.tar.gz"
      # tar, not zip: preserves the executable bit on the binary.
      tar -czf "$STAGING_DIR/$out" -C "$STAGING_DIR/$plat" . || die "linux archive failed"
      ;;
    windows)
      out="Goose2Client-$BUILD_ID-windows.zip"
      (cd "$STAGING_DIR/$plat" && zip -qr "../$out" .) || die "windows archive failed"
      ;;
    macos)
      # Godot already emits a self-contained, ad-hoc-signed .zip of the .app.
      out="Goose2Client-$BUILD_ID-macos.zip"
      mv "$STAGING_DIR/$plat/Goose2Client.zip" "$STAGING_DIR/$out" || die "macos archive failed"
      ;;
  esac
  [ -s "$STAGING_DIR/$out" ] || die "$plat archive '$out' is missing or empty"
  rm -rf "${STAGING_DIR:?}/$plat"
  ARTIFACT_NAME="$out"
}

ARTIFACTS=()
for plat in "${PLATFORMS[@]}"; do
  case "$plat" in
    linux)   export_platform linux   "Linux"   "Goose2Client.x86_64" ;;
    windows) export_platform windows "Windows" "Goose2Client.exe" ;;
    macos)   export_platform macos   "macOS"   "Goose2Client.zip" ;;
  esac
  archive_platform "$plat"
  ARTIFACTS+=("$ARTIFACT_NAME")
done

# --- Publish -----------------------------------------------------------------

for a in "${ARTIFACTS[@]}"; do
  mv "$STAGING_DIR/$a" "$BUILD_DIR/$a" || die "could not publish '$a'"
done

# --- Report ------------------------------------------------------------------

echo
echo "==> Build $BUILD_ID complete"
for a in "${ARTIFACTS[@]}"; do
  printf '    %-56s %s\n' "$BUILD_DIR/$a" "$(du -h "$BUILD_DIR/$a" | cut -f1)"
done
