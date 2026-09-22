#!/bin/sh
# Godot does not rebuild the C# assembly itself; a stale build silently runs the old code.
set -e
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$ROOT"

if ! dotnet build Goose2ClientGodot.csproj; then
    echo "run_character_icon: dotnet build failed — refusing to run a stale assembly" >&2
    exit 1
fi

# Generated assets that exist only in the main checkout. SpriteCache throws without the
# manifest (aborting GameManager._Ready before the self-test dispatch) and Character._Ready
# throws without the heights file.
if [ ! -f Assets/Sprites/manifest.json ]; then
    printf '{"sheets": {}}' > Assets/Sprites/manifest.json
fi
if [ ! -f Assets/Resources/AnimationHeights.txt ]; then
    : > Assets/Resources/AnimationHeights.txt
fi

GODOT_BIN="${GODOT_BIN:-$(command -v godot-mono || command -v godot)}"
if [ -z "${GODOT_BIN:-}" ]; then
    echo "run_character_icon: no C#-capable Godot binary (godot-mono/godot) on PATH — set GODOT_BIN=/path/to/godot" >&2
    exit 2
fi
exec "$GODOT_BIN" --headless --path "$ROOT" -- +selftest=character_icon
