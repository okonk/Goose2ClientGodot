#!/usr/bin/env bash
set -euo pipefail

repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
die() { echo "sign-macos.sh: $*" >&2; exit 1; }

[ "$#" -eq 2 ] || die "usage: $0 <Godot-macos.zip> <signed-macos.zip>"

for tool in unzip zip; do
  command -v "$tool" > /dev/null || die "required command '$tool' not found"
done

rcodesign="${RCODESIGN:-rcodesign}"
command -v "$rcodesign" > /dev/null || die "rcodesign not found; set RCODESIGN=/path/to/rcodesign"

input="$(cd "$(dirname "$1")" && pwd)/$(basename "$1")"
output="$(cd "$(dirname "$2")" && pwd)/$(basename "$2")"
[ -f "$input" ] || die "archive not found: $input"
[ ! -e "$output" ] || die "output already exists: $output"

stage="$(mktemp -d "$(dirname "$output")/.goose-macos.XXXXXX")"
trap 'rm -rf "$stage"' EXIT
mkdir -p "$stage/extracted"
unzip -q "$input" -d "$stage/extracted"

app="$stage/extracted/Goose2ClientGodot.app"
binary="$app/Contents/MacOS/Goose2ClientGodot"
[ -x "$binary" ] || die "client executable missing or not executable"

"$rcodesign" sign -C /dev/null --entitlements-xml-file "Contents/MacOS/Goose2ClientGodot:$repo_dir/macos.entitlements" "$app"
"$rcodesign" print-signature-info -C /dev/null "$binary" > "$stage/signature-info.txt"
count="$(grep -c 'entitlements_der_plist:' "$stage/signature-info.txt" || true)"
[ "$count" -eq 2 ] || die "expected parseable DER entitlements for both architectures"

(cd "$stage/extracted" && zip -qry "$stage/signed.zip" Goose2ClientGodot.app)
unzip -tqq "$stage/signed.zip"
[ -s "$stage/signed.zip" ] || die "signed archive is empty"
mv "$stage/signed.zip" "$output"
printf 'Signed macOS archive: %s\n' "$output"
