#!/usr/bin/env bash
# Publishes self-contained MapEditor.App desktop archives for the requested RIDs.
#
#   ./build-map-editor.sh [--skip-tests] [linux-x64|windows-x64|osx-x64|osx-arm64 ...]
#
# No RIDs (or all four) means all four. Output is staged under
# build/map-editor/.staging and only renamed into build/map-editor/<build-id>
# after every requested archive has been created and inspected, so a failed run
# never touches prior releases or leaves a partial release visible.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

ALL_RIDS=(linux-x64 windows-x64 osx-x64 osx-arm64)
APP_CSPROJ="src/MapEditor.App/MapEditor.App.csproj"
BUILD_ROOT="build/map-editor"
STAGING_ROOT="$BUILD_ROOT/.staging"
MAC_BUNDLE="Goose2MapEditor"
INFO_PLIST_TEMPLATE="src/MapEditor.App/Packaging/macos/Info.plist.template"

die() {
  echo "build-map-editor: $*" >&2
  exit 1
}

# The .NET 10 SDK renamed the Windows RIDs (windows-x64 -> win-x64); the
# publisher's platform names stay the contract RIDs, only the publish command
# uses the SDK's canonical RID.
sdk_rid() {
  case "$1" in
    windows-x64) echo win-x64 ;;
    *) echo "$1" ;;
  esac
}

for tool in dotnet git tar zip unzip; do
  command -v "$tool" >/dev/null 2>&1 || die "required tool not found on PATH: $tool"
done

SKIP_TESTS=0
REQUESTED=()
for arg in "$@"; do
  case "$arg" in
    --skip-tests) SKIP_TESTS=1 ;;
    -*) die "unknown option: $arg (usage: $0 [--skip-tests] [linux-x64|windows-x64|osx-x64|osx-arm64 ...])" ;;
    *) REQUESTED+=("$arg") ;;
  esac
done

declare -A KNOWN_RID=() SEEN_RID=()
for rid in "${ALL_RIDS[@]}"; do KNOWN_RID[$rid]=1; done
RIDS=()
for rid in "${REQUESTED[@]}"; do
  if [ -z "${KNOWN_RID[$rid]:-}" ]; then
    die "unknown RID: $rid (valid: ${ALL_RIDS[*]})"
  fi
  if [ -n "${SEEN_RID[$rid]:-}" ]; then
    continue
  fi
  SEEN_RID[$rid]=1
  RIDS+=("$rid")
done
if [ ${#RIDS[@]} -eq 0 ]; then
  RIDS=("${ALL_RIDS[@]}")
fi

UTC_STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
GIT_SHA="$(git rev-parse --short HEAD)"
DIRTY=0
if [ -n "$(git status --porcelain)" ]; then
  DIRTY=1
fi
BUILD_ID="${UTC_STAMP}-${GIT_SHA}"
if [ "$DIRTY" -eq 1 ]; then
  BUILD_ID="${BUILD_ID}-dirty"
fi

STAGE="$SCRIPT_DIR/$STAGING_ROOT/${BUILD_ID}.$RANDOM"
mkdir -p "$STAGE/publish" "$STAGE/logs" "$STAGE/archives" "$STAGE/release" "$STAGE/inspect"

report_failure() {
  local rc=$?
  if [ "$rc" -ne 0 ]; then
    echo "build-map-editor: FAILED (exit code $rc)" >&2
    echo "build-map-editor: preserved staging: $STAGE" >&2
    echo "build-map-editor: preserved publish logs: $STAGE/logs" >&2
  fi
}
trap report_failure EXIT

if [ "$SKIP_TESTS" -eq 0 ]; then
  for suite in \
    tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj \
    tests/MapEditor.Rendering.Tests/MapEditor.Rendering.Tests.csproj \
    tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj
  do
    echo "build-map-editor: dotnet test $suite"
    dotnet test "$suite" -v minimal
  done
fi

for rid in "${RIDS[@]}"; do
  echo "build-map-editor: dotnet publish $rid"
  out="$STAGE/publish/$rid"
  dotnet publish "$APP_CSPROJ" -c Release -r "$(sdk_rid "$rid")" --self-contained true \
    -p:PublishSingleFile=false -p:PublishTrimmed=false -o "$out" \
    2>&1 | tee "$STAGE/logs/$rid.publish.log"
done

assert_entry() {
  grep -qxF "$2" <<<"$1" || die "archive is missing required entry: $2"
}

assert_no_godot_or_assets() {
  if grep -qiE '(^|/)godot' <<<"$1"; then
    die "archive contains a Godot binary; refusing to publish"
  fi
  if grep -qiE '(^|/)assets(/|$)' <<<"$1"; then
    die "archive contains an asset directory; refusing to publish"
  fi
}

for rid in "${RIDS[@]}"; do
  out="$STAGE/publish/$rid"
  dist_dir="$STAGE/dist/$rid"
  mkdir -p "$dist_dir"

  case "$rid" in
    linux-x64)
      top="map-editor-linux-x64"
      mkdir -p "$dist_dir/$top"
      cp -a "$out/." "$dist_dir/$top/"
      [ -x "$dist_dir/$top/MapEditor.App" ] || die "linux-x64 app host missing or not executable"
      archive="$STAGE/archives/map-editor-${BUILD_ID}-linux-x64.tar.gz"
      tar -czf "$archive" -C "$dist_dir" "$top"
      listing="$(tar -tzf "$archive")"
      assert_entry "$listing" "$top/MapEditor.App"
      assert_entry "$listing" "$top/MapEditor.App.deps.json"
      assert_entry "$listing" "$top/MapEditor.App.runtimeconfig.json"
      if ! tar -tvzf "$archive" | grep -E '^-rwx' | awk '{print $NF}' | grep -qx "$top/MapEditor.App"; then
        die "linux-x64 app host lost executable mode inside the archive"
      fi
      assert_no_godot_or_assets "$listing"
      ;;
    windows-x64)
      top="map-editor-windows-x64"
      mkdir -p "$dist_dir/$top"
      cp -a "$out/." "$dist_dir/$top/"
      [ -f "$dist_dir/$top/MapEditor.App.exe" ] || die "windows-x64 app host missing"
      archive="$STAGE/archives/map-editor-${BUILD_ID}-windows-x64.zip"
      (cd "$dist_dir" && zip -q -r -X "$archive" "$top")
      unzip -t "$archive" >/dev/null
      listing="$(unzip -Z1 "$archive")"
      assert_entry "$listing" "$top/MapEditor.App.exe"
      assert_entry "$listing" "$top/MapEditor.App.deps.json"
      assert_entry "$listing" "$top/MapEditor.App.runtimeconfig.json"
      assert_no_godot_or_assets "$listing"
      ;;
    osx-x64|osx-arm64)
      bundle="$dist_dir/$MAC_BUNDLE.app"
      mkdir -p "$bundle/Contents/MacOS" "$bundle/Contents/Resources"
      cp -a "$out/." "$bundle/Contents/MacOS/"
      mv "$bundle/Contents/MacOS/MapEditor.App" "$bundle/Contents/MacOS/$MAC_BUNDLE"
      mv "$bundle/Contents/MacOS/MapEditor.App.deps.json" "$bundle/Contents/MacOS/$MAC_BUNDLE.deps.json"
      mv "$bundle/Contents/MacOS/MapEditor.App.runtimeconfig.json" "$bundle/Contents/MacOS/$MAC_BUNDLE.runtimeconfig.json"
      sed "s/__CFBUNDLE_EXECUTABLE__/$MAC_BUNDLE/" "$INFO_PLIST_TEMPLATE" > "$bundle/Contents/Info.plist"
      [ -x "$bundle/Contents/MacOS/$MAC_BUNDLE" ] || die "$rid app host missing or not executable"
      archive="$STAGE/archives/${MAC_BUNDLE}-${BUILD_ID}-${rid}.app.zip"
      (cd "$dist_dir" && zip -q -r -X "$archive" "$MAC_BUNDLE.app")
      unzip -t "$archive" >/dev/null
      listing="$(unzip -Z1 "$archive")"
      assert_entry "$listing" "$MAC_BUNDLE.app/Contents/MacOS/$MAC_BUNDLE"
      assert_entry "$listing" "$MAC_BUNDLE.app/Contents/MacOS/$MAC_BUNDLE.deps.json"
      assert_entry "$listing" "$MAC_BUNDLE.app/Contents/MacOS/$MAC_BUNDLE.runtimeconfig.json"
      assert_entry "$listing" "$MAC_BUNDLE.app/Contents/Info.plist"
      extract="$STAGE/inspect/$rid"
      mkdir -p "$extract"
      unzip -q -o "$archive" -d "$extract"
      [ -x "$extract/$MAC_BUNDLE.app/Contents/MacOS/$MAC_BUNDLE" ] || die "$rid app host lost executable mode inside the archive"
      assert_no_godot_or_assets "$listing"
      ;;
  esac
  echo "build-map-editor: inspected $archive"
done

{
  echo "BUILD_ID=$BUILD_ID"
  echo "GIT_SHA=$GIT_SHA"
  echo "DIRTY=$DIRTY"
  echo "RIDS=${RIDS[*]}"
  echo "CREATED_UTC=$UTC_STAMP"
} > "$STAGE/release/BUILD-METADATA.txt"

mv "$STAGE"/archives/* "$STAGE/release/"

RELEASE_DIR="$BUILD_ROOT/$BUILD_ID"
if [ -e "$RELEASE_DIR" ]; then
  die "release directory already exists: $RELEASE_DIR (refusing to overwrite)"
fi
# Same-filesystem rename: the release appears atomically, never partially.
mv "$STAGE/release" "$RELEASE_DIR"

rm -rf "$STAGE"

echo "build-map-editor: published release $RELEASE_DIR"
du -sh "$RELEASE_DIR"/*
