#!/usr/bin/env bash
# Runs as the host user with $HOME on a persistent host cache dir, so the keystore
# survives rebuilds (a changed signing key blocks installing over the old APK).
set -euo pipefail

KEYSTORE="$HOME/goose2-release.keystore"
KEYSTORE_PASS="goose2android"

if [ ! -f "$KEYSTORE" ]; then
  keytool -genkeypair -v -keystore "$KEYSTORE" -alias goose2 -keyalg RSA -keysize 2048 \
    -validity 10000 -storepass "$KEYSTORE_PASS" -keypass "$KEYSTORE_PASS" \
    -dname "CN=Goose2 Client" > /dev/null
fi

mkdir -p "$HOME/.config/godot"
cat > "$HOME/.config/godot/editor_settings-4.7.tres" <<EOF
[gd_resource type="EditorSettings" format=3]

[resource]
export/android/android_sdk_path = "$ANDROID_HOME"
export/android/java_sdk_path = "$JAVA_HOME"
EOF

export GODOT_ANDROID_KEYSTORE_RELEASE_PATH="$KEYSTORE"
export GODOT_ANDROID_KEYSTORE_RELEASE_USER="goose2"
export GODOT_ANDROID_KEYSTORE_RELEASE_PASSWORD="$KEYSTORE_PASS"

exec godot --headless --path /project --install-android-build-template --export-release Android "$@"
