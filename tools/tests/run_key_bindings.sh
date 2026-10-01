#!/bin/sh
set -e
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$ROOT"

if ! dotnet build Goose2ClientGodot.csproj; then
    echo "run_key_bindings: dotnet build failed — refusing to run a stale assembly" >&2
    exit 1
fi

mkdir -p Assets/Sprites Assets/Resources
if [ ! -f Assets/Sprites/manifest.json ]; then
    printf '{"sheets": {}}' > Assets/Sprites/manifest.json
fi
if [ ! -f Assets/Resources/AnimationHeights.txt ]; then
    : > Assets/Resources/AnimationHeights.txt
fi

GODOT_BIN="${GODOT_BIN:-$(command -v godot-mono || command -v godot)}"
if [ -z "${GODOT_BIN:-}" ]; then
    echo "run_key_bindings: no C#-capable Godot binary (godot-mono/godot) on PATH — set GODOT_BIN=/path/to/godot" >&2
    exit 2
fi

OUT="$(mktemp)"
STATUS=0
"$GODOT_BIN" --headless --path "$ROOT" -- +selftest=key_bindings >"$OUT" 2>&1 || STATUS=$?
cat "$OUT"
if [ "$STATUS" -ne 0 ]; then
    rm -f "$OUT"
    echo "run_key_bindings: godot exited with status $STATUS" >&2
    exit "$STATUS"
fi
if ! grep -F "[key_bindings_selftest] PASS" "$OUT" >/dev/null; then
    rm -f "$OUT"
    echo "run_key_bindings: PASS marker missing from godot output" >&2
    exit 1
fi
rm -f "$OUT"
