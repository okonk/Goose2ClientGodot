#!/usr/bin/env bash
set -euo pipefail

repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
test_dir="$(mktemp -d)"
trap 'rm -rf "$test_dir"' EXIT

mkdir -p "$test_dir/bin" "$test_dir/source/Goose2ClientGodot.app/Contents/MacOS"

cat > "$test_dir/bin/rcodesign" <<'EOF'
#!/usr/bin/env bash
printf '%s\n' "$*" >> "$CALLS"
case "$1" in
  sign)
    [ "${FAIL_SIGN:-0}" = 0 ] || exit 4
    [ "$2" = -C ] && [ "$3" = /dev/null ]
    [ "$4" = --entitlements-xml-file ]
    [ -f "${5#*:}" ]
    [ -d "$6" ]
    touch "$6/signed-marker"
    ;;
  print-signature-info)
    [ "${FAIL_PRINT:-0}" = 0 ] || exit 5
    printf '%s\n' 'entitlements_der_plist:'
    if [ "${BAD_DER:-0}" = 0 ]; then
      printf '%s\n' 'entitlements_der_plist:'
    fi
    ;;
  *) exit 2 ;;
esac
EOF

cat > "$test_dir/source/Goose2ClientGodot.app/Contents/MacOS/Goose2ClientGodot" <<'EOF'
#!/usr/bin/env bash
exit 0
EOF

chmod +x "$test_dir/bin/rcodesign" "$test_dir/source/Goose2ClientGodot.app/Contents/MacOS/Goose2ClientGodot"
(cd "$test_dir/source" && zip -qr "$test_dir/client.zip" Goose2ClientGodot.app)

CALLS="$test_dir/calls" RCODESIGN="$test_dir/bin/rcodesign" bash "$repo_dir/sign-macos.sh" "$test_dir/client.zip" "$test_dir/signed.zip"

test -s "$test_dir/signed.zip"
unzip -tqq "$test_dir/signed.zip"
unzip -Z1 "$test_dir/signed.zip" | rg -q '^Goose2ClientGodot.app/signed-marker$'
rg -q '^sign -C /dev/null --entitlements-xml-file Contents/MacOS/Goose2ClientGodot:' "$test_dir/calls"
rg -q '^print-signature-info -C /dev/null ' "$test_dir/calls"

if CALLS="$test_dir/calls" RCODESIGN="$test_dir/bin/rcodesign" FAIL_SIGN=1 bash "$repo_dir/sign-macos.sh" "$test_dir/client.zip" "$test_dir/failed-sign.zip"; then
  exit 1
fi
test ! -e "$test_dir/failed-sign.zip"

if CALLS="$test_dir/calls" RCODESIGN="$test_dir/bin/rcodesign" FAIL_PRINT=1 bash "$repo_dir/sign-macos.sh" "$test_dir/client.zip" "$test_dir/failed-print.zip"; then
  exit 1
fi
test ! -e "$test_dir/failed-print.zip"

if CALLS="$test_dir/calls" RCODESIGN="$test_dir/bin/rcodesign" BAD_DER=1 bash "$repo_dir/sign-macos.sh" "$test_dir/client.zip" "$test_dir/failed-der.zip"; then
  exit 1
fi
test ! -e "$test_dir/failed-der.zip"
