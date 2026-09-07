#!/usr/bin/env bash
# Tests for build-map-editor.sh: temporary git repo + fake dotnet/tar/zip/unzip
# on PATH that record invocations and can be scripted to fail via fake-config.
set -u

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PUBLISHER="$SCRIPT_DIR/build-map-editor.sh"
TEMPLATE="$SCRIPT_DIR/src/MapEditor.App/Packaging/macos/Info.plist.template"

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

PASS=0
FAIL=0
ok() { PASS=$((PASS+1)); echo "ok   - $1"; }
bad() { FAIL=$((FAIL+1)); echo "FAIL - $1"; }
check() { if [ "$2" -eq 0 ]; then ok "$1"; else bad "$1"; fi; }

make_fakes() {
  local env="$1"
  local real_tar real_zip real_unzip real
  real_tar="$(command -v tar)"
  real_zip="$(command -v zip)"
  real_unzip="$(command -v unzip)"
  mkdir -p "$env/fake"

  cat > "$env/fake/dotnet" <<EOF
#!/usr/bin/env bash
echo "\$*" >> "\${FAKE_DOTNET_LOG:-/dev/null}"
if [ -n "\${FAKE_ENV:-}" ] && [ -f "\$FAKE_ENV/fake-config" ]; then
  . "\$FAKE_ENV/fake-config"
fi
case "\${1:-}" in
  test)
    exit 0
    ;;
  publish)
    rid=""
    out=""
    prev=""
    shift
    for a in "\$@"; do
      case "\$prev" in
        -r) rid="\$a" ;;
        -o) out="\$a" ;;
      esac
      prev="\$a"
    done
    if [ "\${FAKE_DOTNET_FAIL_RID:-}" = "\$rid" ]; then
      echo "fake dotnet: simulated publish failure for \$rid" >&2
      exit 1
    fi
    [ -n "\$out" ] || exit 2
    mkdir -p "\$out"
    host="MapEditor.App"
    if [ "\$rid" = "win-x64" ]; then host="MapEditor.App.exe"; fi
    printf '#!/bin/sh\nexit 0\n' > "\$out/\$host"
    chmod +x "\$out/\$host"
    if [ "\${FAKE_DOTNET_OMIT_DEPS_RID:-}" != "\$rid" ]; then
      printf '{}' > "\$out/MapEditor.App.deps.json"
      printf '{}' > "\$out/MapEditor.App.runtimeconfig.json"
    fi
    printf 'dll' > "\$out/MapEditor.App.dll"
    echo "fake dotnet: publish succeeded for \$rid"
    exit 0
    ;;
  *)
    exit 0
    ;;
esac
EOF

  for t in tar zip unzip; do
    case "$t" in
      tar) real="$real_tar" ;;
      zip) real="$real_zip" ;;
      unzip) real="$real_unzip" ;;
    esac
    cat > "$env/fake/$t" <<EOF
#!/usr/bin/env bash
echo "$t \$*" >> "\${FAKE_TOOL_LOG:-/dev/null}"
exec $real "\$@"
EOF
  done

  cat > "$env/fake/godot" <<'EOF'
#!/usr/bin/env bash
echo "godot $*" >> "${FAKE_GODOT_LOG:-/dev/null}"
exit 0
EOF

  chmod +x "$env/fake/"*
}

setup() {
  ENV="$WORK/env"
  rm -rf "$ENV"
  mkdir -p "$ENV/repo"
  (
    cd "$ENV/repo"
    git init -q
    git config user.email test@example.com
    git config user.name test
    printf 'init\n' > README.md
    git add README.md
    git commit -qm init
  )
  if [ -f "$PUBLISHER" ]; then
    cp "$PUBLISHER" "$ENV/repo/build-map-editor.sh"
    chmod +x "$ENV/repo/build-map-editor.sh"
  fi
  if [ -f "$TEMPLATE" ]; then
    mkdir -p "$ENV/repo/src/MapEditor.App/Packaging/macos"
    cp "$TEMPLATE" "$ENV/repo/src/MapEditor.App/Packaging/macos/Info.plist.template"
  fi
  cat > "$ENV/oauth.json" <<'EOF'
{
  "client": {
    "client_id": "fake-client-id.apps.googleusercontent.com",
    "client_secret": "fake-client-secret",
    "auth_uri": "https://accounts.google.com/o/oauth2/auth",
    "token_uri": "https://accounts.google.com/o/oauth2/token"
  }
}
EOF
  make_fakes "$ENV"
}

run() {
  local env="$1"
  shift
  (
    export FAKE_ENV="$env"
    export FAKE_DOTNET_LOG="$env/dotnet.log"
    export FAKE_TOOL_LOG="$env/tools.log"
    export FAKE_GODOT_LOG="$env/godot.log"
    case "${OAUTH_MODE:-valid}" in
      valid) export GOOSE2_MAP_EDITOR_GOOGLE_OAUTH_CLIENT="$env/oauth.json" ;;
      unset) unset GOOSE2_MAP_EDITOR_GOOGLE_OAUTH_CLIENT ;;
      missing) export GOOSE2_MAP_EDITOR_GOOGLE_OAUTH_CLIENT="$env/does-not-exist.json" ;;
      relative) export GOOSE2_MAP_EDITOR_GOOGLE_OAUTH_CLIENT="oauth.json" ;;
      web) export GOOSE2_MAP_EDITOR_GOOGLE_OAUTH_CLIENT="$env/oauth-web.json" ;;
      invalid) export GOOSE2_MAP_EDITOR_GOOGLE_OAUTH_CLIENT="$env/oauth-invalid.json" ;;
    esac
    PATH="$env/fake:$PATH"
    cd "$env/repo"
    ./build-map-editor.sh "$@" >"$env/stdout.log" 2>"$env/stderr.log"
  )
}

releases() {
  ls "$ENV/repo/build/map-editor" 2>/dev/null | grep -v '^\.staging$' || true
}

# Contract RID -> SDK canonical RID (.NET 10 renamed windows-x64 to win-x64)
sdk_rid() {
  case "$1" in
    windows-x64) echo win-x64 ;;
    *) echo "$1" ;;
  esac
}

echo "== 1. dirty tree accepted; publish reads current project, not git archive/HEAD =="
setup
mkdir -p "$ENV/repo/src/MapEditor.App"
printf '<Window xmlns="https://github.com/avaloniaui"></Window>\n' > "$ENV/repo/src/MapEditor.App/Untracked.xaml"
run "$ENV" --skip-tests linux-x64
rc=$?
check "exit 0 on dirty tree" $([ "$rc" -eq 0 ]; echo $?)
rels="$(releases)"
check "release dir marked -dirty" "$(case "$rels" in *-dirty) echo 0 ;; *) echo 1 ;; esac)"
grep -q '^publish src/MapEditor.App/MapEditor.App.csproj -c Release -r linux-x64 ' "$ENV/dotnet.log" 2>/dev/null
check "dotnet publish invoked directly on the current csproj" $?
if [ -f "$ENV/dotnet.log" ]; then
  ! grep -q 'git archive' "$ENV/dotnet.log"
else
  rc=1
fi
check "no git archive/HEAD based publish" $rc

echo "== 2. no-argument invocation publishes exactly all four RIDs =="
setup
run "$ENV" --skip-tests
rc=$?
check "exit 0" $([ "$rc" -eq 0 ]; echo $?)
n=$(grep -c '^publish ' "$ENV/dotnet.log" 2>/dev/null)
check "exactly 4 publish invocations" $([ "$n" -eq 4 ]; echo $?)
allok=0
for r in linux-x64 windows-x64 osx-x64 osx-arm64; do
  c=$(grep -c "^publish .* -r $(sdk_rid $r) " "$ENV/dotnet.log" 2>/dev/null)
  [ "$c" -eq 1 ] || allok=1
done
check "each of the four RIDs published exactly once" $allok
rels="$(releases)"
count=$(ls "$ENV/repo/build/map-editor/$rels" 2>/dev/null | wc -l)
check "release contains 4 archives + BUILD-METADATA.txt" $([ "$count" -eq 5 ]; echo $?)
grep -q '^RIDS=linux-x64 windows-x64 osx-x64 osx-arm64$' "$ENV/repo/build/map-editor/$rels/BUILD-METADATA.txt" 2>/dev/null
check "BUILD-METADATA.txt records the exact RIDs" $?

echo "== 3. explicit all-four invocation publishes exactly all four RIDs =="
setup
run "$ENV" --skip-tests linux-x64 windows-x64 osx-x64 osx-arm64
rc=$?
check "exit 0" $([ "$rc" -eq 0 ]; echo $?)
n=$(grep -c '^publish ' "$ENV/dotnet.log" 2>/dev/null)
check "exactly 4 publish invocations" $([ "$n" -eq 4 ]; echo $?)
allok=0
for r in linux-x64 windows-x64 osx-x64 osx-arm64; do
  c=$(grep -c "^publish .* -r $(sdk_rid $r) " "$ENV/dotnet.log" 2>/dev/null)
  [ "$c" -eq 1 ] || allok=1
done
check "each of the four RIDs published exactly once" $allok

echo "== 4. one-RID selection publishes only that RID =="
setup
run "$ENV" --skip-tests windows-x64
rc=$?
check "exit 0" $([ "$rc" -eq 0 ]; echo $?)
n=$(grep -c '^publish ' "$ENV/dotnet.log" 2>/dev/null)
check "exactly 1 publish invocation" $([ "$n" -eq 1 ]; echo $?)
grep -q '^publish .* -r win-x64 ' "$ENV/dotnet.log" 2>/dev/null
check "the single publish targets the windows-x64 SDK RID (win-x64)" $?
rels="$(releases)"
count=$(ls "$ENV/repo/build/map-editor/$rels" 2>/dev/null | wc -l)
check "release contains only the requested archive + metadata" $([ "$count" -eq 2 ]; echo $?)
grep -q 'windows-x64' "$ENV/repo/build/map-editor/$rels/BUILD-METADATA.txt" 2>/dev/null
check "metadata records windows-x64" $?
! grep -q 'linux-x64' "$ENV/repo/build/map-editor/$rels/BUILD-METADATA.txt" 2>/dev/null
check "metadata does not record unrequested RIDs" $?

echo "== 5. filtering and dedup: duplicates run once, unknowns rejected =="
setup
run "$ENV" --skip-tests linux-x64 linux-x64 windows-x64
rc=$?
check "exit 0" $([ "$rc" -eq 0 ]; echo $?)
n=$(grep -c '^publish ' "$ENV/dotnet.log" 2>/dev/null)
check "duplicate RID published once (2 total)" $([ "$n" -eq 2 ]; echo $?)
rels="$(releases)"
count=$(ls "$ENV/repo/build/map-editor/$rels" 2>/dev/null | wc -l)
check "release contains 2 archives + metadata" $([ "$count" -eq 3 ]; echo $?)
setup
run "$ENV" --skip-tests freebsd-amd64
check "unknown RID fails" $([ "$?" -ne 0 ]; echo $?)
setup
run "$ENV" --bogus
check "unknown option fails" $([ "$?" -ne 0 ]; echo $?)

echo "== 6. exact --self-contained true on every publish, nowhere else =="
setup
run "$ENV" --skip-tests
sc_true=$(grep -c -- '--self-contained true' "$ENV/dotnet.log" 2>/dev/null)
sc_any=$(grep -c -- '--self-contained' "$ENV/dotnet.log" 2>/dev/null)
check "all 4 publishes pass --self-contained true" $([ "$sc_true" -eq 4 ]; echo $?)
check "no other --self-contained form" $([ "$sc_any" -eq 4 ]; echo $?)

echo "== 7. tests run by default, skipped with --skip-tests =="
setup
run "$ENV"
rc=$?
check "default run exit 0" $([ "$rc" -eq 0 ]; echo $?)
ntest=$(grep -c '^test ' "$ENV/dotnet.log" 2>/dev/null)
check "default run executes 3 test suites" $([ "$ntest" -eq 3 ]; echo $?)
grep -q '^test tests/MapEditor.Core.Tests/MapEditor.Core.Tests.csproj -v minimal$' "$ENV/dotnet.log" 2>/dev/null
check "Core suite invoked" $?
grep -q '^test tests/MapEditor.Rendering.Tests/MapEditor.Rendering.Tests.csproj -v minimal$' "$ENV/dotnet.log" 2>/dev/null
check "Rendering suite invoked" $?
grep -q '^test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj -v minimal$' "$ENV/dotnet.log" 2>/dev/null
check "App suite invoked" $?
setup
run "$ENV" --skip-tests
rc=$?
check "skip-tests run exit 0" $([ "$rc" -eq 0 ]; echo $?)
ntest=$(grep -c '^test ' "$ENV/dotnet.log" 2>/dev/null)
check "skip-tests runs no suites" $([ "$ntest" -eq 0 ]; echo $?)

echo "== 8. failing fourth RID preserves old release and stage/logs =="
setup
mkdir -p "$ENV/repo/build/map-editor/old-release"
printf 'old\n' > "$ENV/repo/build/map-editor/old-release/old.txt"
old_sha=$(sha256sum "$ENV/repo/build/map-editor/old-release/old.txt" | cut -d' ' -f1)
printf 'FAKE_DOTNET_FAIL_RID=osx-arm64\n' > "$ENV/fake-config"
run "$ENV" --skip-tests
rc=$?
check "exit nonzero when fourth RID publish fails" $([ "$rc" -ne 0 ]; echo $?)
new_sha=$(sha256sum "$ENV/repo/build/map-editor/old-release/old.txt" | cut -d' ' -f1)
check "old release byte-identical" $([ "$old_sha" = "$new_sha" ]; echo $?)
check "no new release published" $([ "$(releases)" = "old-release" ]; echo $?)
stage_ls=$(ls "$ENV/repo/build/map-editor/.staging" 2>/dev/null)
check "failed stage preserved" $([ -n "$stage_ls" ]; echo $?)
logs_ok=0
for r in linux-x64 windows-x64 osx-x64 osx-arm64; do
  [ -s "$ENV/repo/build/map-editor/.staging/$stage_ls/logs/$r.publish.log" ] || logs_ok=1
done
check "all per-RID publish logs preserved" $logs_ok
grep -q 'staging' "$ENV/stderr.log" 2>/dev/null
check "failure message prints preserved staging path" $?

echo "== 9. archive inspection failure (missing deps.json) publishes nothing =="
setup
printf 'FAKE_DOTNET_OMIT_DEPS_RID=linux-x64\n' > "$ENV/fake-config"
run "$ENV" --skip-tests linux-x64
rc=$?
check "exit nonzero on inspection failure" $([ "$rc" -ne 0 ]; echo $?)
check "no release published" $([ -z "$(releases)" ]; echo $?)
stage_ls=$(ls "$ENV/repo/build/map-editor/.staging" 2>/dev/null)
check "failed stage with logs preserved" $([ -n "$stage_ls" ] && [ -s "$ENV/repo/build/map-editor/.staging/$stage_ls/logs/linux-x64.publish.log" ]; echo $?)

echo "== 10. success: one rename, only requested set visible, staging shell removed =="
setup
mkdir -p "$ENV/repo/build/map-editor/old-release"
printf 'old\n' > "$ENV/repo/build/map-editor/old-release/old.txt"
old_sha=$(sha256sum "$ENV/repo/build/map-editor/old-release/old.txt" | cut -d' ' -f1)
run "$ENV" --skip-tests linux-x64
rc=$?
check "exit 0" $([ "$rc" -eq 0 ]; echo $?)
rels="$(releases)"
set -- $rels
nrels=$#
check "exactly one new release dir" $([ "$nrels" -eq 2 ]; echo $?)
newrel=""
for r in "$@"; do
  [ "$r" = "old-release" ] || newrel="$r"
done
count=$(ls "$ENV/repo/build/map-editor/$newrel" 2>/dev/null | wc -l)
check "release contains exactly the requested archive + metadata" $([ "$count" -eq 2 ]; echo $?)
grep -q 'linux-x64' "$ENV/repo/build/map-editor/$newrel/BUILD-METADATA.txt" 2>/dev/null
check "metadata records the requested RID" $?
new_sha=$(sha256sum "$ENV/repo/build/map-editor/old-release/old.txt" | cut -d' ' -f1)
check "old release untouched" $([ "$old_sha" = "$new_sha" ]; echo $?)
check "this run's staging shell removed" $([ -z "$(ls -A "$ENV/repo/build/map-editor/.staging" 2>/dev/null)" ]; echo $?)
grep -q 'linux-x64.tar.gz' "$ENV/stdout.log" 2>/dev/null
check "artifact sizes printed on success" $?

echo "== 11. mac archives: separate x64/arm64 .app bundles, correct structure =="
setup
run "$ENV" --skip-tests osx-x64 osx-arm64
rc=$?
check "exit 0" $([ "$rc" -eq 0 ]; echo $?)
rels="$(releases)"
z64=$(ls "$ENV/repo/build/map-editor/$rels" 2>/dev/null | grep -- '-osx-x64\.app\.zip$' || true)
zarm=$(ls "$ENV/repo/build/map-editor/$rels" 2>/dev/null | grep -- '-osx-arm64\.app\.zip$' || true)
check "separate osx-x64 .app archive" $([ -n "$z64" ]; echo $?)
check "separate osx-arm64 .app archive" $([ -n "$zarm" ]; echo $?)
mkdir -p "$ENV/mac"
unzip -q -o "$ENV/repo/build/map-editor/$rels/$z64" -d "$ENV/mac" 2>/dev/null
b="$ENV/mac/Goose2MapEditor.app"
check "bundle executable present and executable" $([ -x "$b/Contents/MacOS/Goose2MapEditor" ]; echo $?)
check "renamed deps.json present" $([ -f "$b/Contents/MacOS/Goose2MapEditor.deps.json" ]; echo $?)
check "renamed runtimeconfig.json present" $([ -f "$b/Contents/MacOS/Goose2MapEditor.runtimeconfig.json" ]; echo $?)
check "Info.plist present" $([ -f "$b/Contents/Info.plist" ]; echo $?)
grep -A1 'CFBundleExecutable' "$b/Contents/Info.plist" 2>/dev/null | grep -q '<string>Goose2MapEditor</string>'
check "CFBundleExecutable names the bundle executable" $?
if [ -f "$b/Contents/Info.plist" ]; then
  ! grep -q '__CFBUNDLE_EXECUTABLE__' "$b/Contents/Info.plist"
else
  rc=1
fi
check "no unsubstituted template placeholder" $rc

echo "== 12. no Godot or asset lookup; archives contain neither =="
setup
run "$ENV" --skip-tests linux-x64
rc=$?
check "exit 0" $([ "$rc" -eq 0 ]; echo $?)
check "godot never invoked" $([ ! -s "$ENV/godot.log" ]; echo $?)
rels="$(releases)"
tarball=$(ls "$ENV/repo/build/map-editor/$rels" 2>/dev/null | grep -- '\.tar\.gz$' || true)
listing=$(tar -tzf "$ENV/repo/build/map-editor/$rels/$tarball" 2>/dev/null)
! grep -qiE '(^|/)godot' <<<"$listing"
check "archive has no godot entries" $?
! grep -qiE '(^|/)assets(/|$)' <<<"$listing"
check "archive has no asset directory entries" $?

echo "== 13. refuses to overwrite an existing release directory =="
setup
passed=1
for attempt in 1 2 3 4 5; do
  suffix=""
  if [ -n "$(git -C "$ENV/repo" status --porcelain)" ]; then suffix="-dirty"; fi
  id="$(date -u +%Y%m%dT%H%M%SZ)-$(git -C "$ENV/repo" rev-parse --short HEAD)$suffix"
  mkdir -p "$ENV/repo/build/map-editor/$id"
  printf 'keep\n' > "$ENV/repo/build/map-editor/$id/keep.txt"
  run "$ENV" --skip-tests linux-x64
  rc=$?
  if [ "$rc" -ne 0 ] && [ "$(cat "$ENV/repo/build/map-editor/$id/keep.txt" 2>/dev/null)" = "keep" ]; then
    passed=0
    break
  fi
  sleep 1
done
check "existing release dir not overwritten" $passed
echo "== 14. missing/invalid OAuth client JSON fails before any publish =="
for mode in unset missing relative web invalid; do
  setup
  if [ "$mode" = "web" ]; then
    cat > "$ENV/oauth-web.json" <<'EOF'
{ "web": { "client_id": "fake", "client_secret": "fake" } }
EOF
  fi
  if [ "$mode" = "invalid" ]; then
    printf '{ not json\n' > "$ENV/oauth-invalid.json"
  fi
  OAUTH_MODE="$mode"
  run "$ENV" --skip-tests linux-x64
  rc=$?
  OAUTH_MODE=valid
  check "exit nonzero for $mode oauth client" $([ "$rc" -ne 0 ]; echo $?)
  if [ -f "$ENV/dotnet.log" ]; then
    ! grep -q '^publish ' "$ENV/dotnet.log"
  else
    rc=0
  fi
  check "no publish invocation for $mode oauth client" $rc
  check "no release published for $mode oauth client" $([ -z "$(releases)" ]; echo $?)
  check "failure names the env var ($mode)" $(grep -q 'GOOSE2_MAP_EDITOR_GOOGLE_OAUTH_CLIENT' "$ENV/stderr.log" 2>/dev/null; echo $?)
done

echo "== 15. valid OAuth client staged beside every executable, no token directory =="
setup
run "$ENV" --skip-tests
rc=$?
check "exit 0" $([ "$rc" -eq 0 ]; echo $?)
rels="$(releases)"
release="$ENV/repo/build/map-editor/$rels"
tarball=$(ls "$release" 2>/dev/null | grep -- '\.tar\.gz$' || true)
zipwin=$(ls "$release" 2>/dev/null | grep -- 'windows-x64\.zip$' || true)
z64=$(ls "$release" 2>/dev/null | grep -- '-osx-x64\.app\.zip$' || true)
zarm=$(ls "$release" 2>/dev/null | grep -- '-osx-arm64\.app\.zip$' || true)
listing=$(tar -tzf "$release/$tarball" 2>/dev/null)
grep -qx 'map-editor-linux-x64/google-oauth-client.json' <<<"$listing"
check "linux archive has google-oauth-client.json beside the host" $?
check "linux archive has exactly one client json" $([ "$(grep -cxF 'map-editor-linux-x64/google-oauth-client.json' <<<"$listing")" -eq 1 ]; echo $?)
! grep -qiE '(^|/)google-tokens' <<<"$listing"
check "linux archive has no google-tokens entry" $?
! grep -qxF 'oauth.json' <<<"$listing"
check "linux archive does not carry the source under its original name" $?
winlisting=$(unzip -Z1 "$release/$zipwin" 2>/dev/null)
grep -qx 'map-editor-windows-x64/google-oauth-client.json' <<<"$winlisting"
check "windows archive has client json beside the executable" $?
check "windows archive has exactly one client json" $([ "$(grep -cxF 'map-editor-windows-x64/google-oauth-client.json' <<<"$winlisting")" -eq 1 ]; echo $?)
! grep -qiE '(^|/)google-tokens' <<<"$winlisting"
check "windows archive has no google-tokens entry" $?
macok=0
for z in "$z64" "$zarm"; do
  mlisting=$(unzip -Z1 "$release/$z" 2>/dev/null)
  grep -qx 'Goose2MapEditor.app/Contents/MacOS/google-oauth-client.json' <<<"$mlisting" || macok=1
  [ "$(grep -cxF 'Goose2MapEditor.app/Contents/MacOS/google-oauth-client.json' <<<"$mlisting")" -eq 1 ] || macok=1
  grep -qiE '(^|/)google-tokens' <<<"$mlisting" && macok=1
done
check "both mac archives have exactly one client json beside the executable" $macok
mkdir -p "$ENV/extract"
tar -xzf "$release/$tarball" -C "$ENV/extract"
cmp -s "$ENV/extract/map-editor-linux-x64/google-oauth-client.json" "$ENV/oauth.json"
check "archived client json is byte-identical to the source" $?



echo
echo "passed: $PASS  failed: $FAIL"
if [ "$FAIL" -gt 0 ]; then
  exit 1
fi
