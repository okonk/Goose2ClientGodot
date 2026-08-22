#!/bin/sh
GODOT_BIN="${GODOT_BIN:-$(command -v godot-mono || command -v godot)}"
if [ -z "${GODOT_BIN:-}" ]; then
    echo "run_ui_scale: no C#-capable Godot binary (godot-mono/godot) on PATH — set GODOT_BIN=/path/to/godot" >&2
    exit 2
fi
exec "$GODOT_BIN" --headless --path "$(dirname "$0")/../.." -- +selftest=ui_scale
