#!/usr/bin/env bash
# Builds an Android APK inside Docker, so no local Android SDK is needed.
#
#   ./build-android.sh            # -> build/android/Goose2Client.apk
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")"

die() { echo "build-android.sh: $*" >&2; exit 1; }

IMAGE="goose2-android-build"
TEMPLATE_VERSION="4.7.2.stable.mono"
TEMPLATE_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/godot/export_templates/$TEMPLATE_VERSION"
CACHE_DIR="${XDG_CACHE_HOME:-$HOME/.cache}/goose2-android"
OUT="build/android/Goose2Client.apk"

[ -f "$TEMPLATE_DIR/android_release.apk" ] || die "no Android export templates at '$TEMPLATE_DIR'"
[ -e Assets/Maps/Map1.map ] || die "missing generated assets — see build.sh for how to regenerate them"

docker build -t "$IMAGE" docker/android

# Pre-create the template mount point so Docker does not make its parents root-owned.
mkdir -p "$CACHE_DIR/.local/share/godot/export_templates/$TEMPLATE_VERSION" "$(dirname "$OUT")"

trap 'rm -f build_id.txt' EXIT
printf '%s\n' "$(date -u +%Y%m%dT%H%M%SZ)-$(git rev-parse --short HEAD)-android" > build_id.txt

docker run --rm \
  --user "$(id -u):$(id -g)" \
  -e HOME=/cache \
  -v "$CACHE_DIR:/cache" \
  -v "$TEMPLATE_DIR:/cache/.local/share/godot/export_templates/$TEMPLATE_VERSION:ro" \
  -v "$PWD:/project" \
  "$IMAGE" "/project/$OUT"

[ -s "$OUT" ] || die "export finished but '$OUT' is missing"
echo "==> $OUT ($(du -h "$OUT" | cut -f1))"
