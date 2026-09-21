# Linux macOS Signing Plan

**Goal:** Produce a macOS ZIP on Linux with a signature accepted by Apple silicon.

**Evidence:** The Godot 4.7.2 built-in signature failed macOS 27 DER parsing. Re-signing the same app with `rcodesign` 0.29.0 on Linux, with the four .NET runtime entitlements, produced a ZIP that launched on the target Mac.

## Implementation

1. Add a shell test that packages a small app ZIP, stubs `rcodesign`, checks the signing command and verifies failed signing never publishes an output ZIP.
2. Add the observed .NET entitlements plist and a Linux signing script that extracts the Godot ZIP, re-signs the app with `rcodesign`, checks that both universal slices have parseable DER entitlements, and publishes a tested ZIP.
3. Make `build.sh` require `rcodesign` for macOS builds and call the signing script before publishing the final `-macos.zip` artifact.
4. Replace the Mac finalization instructions with the Linux signing requirements and retain the target Mac UI smoke test.
5. Run the shell test and sign the existing Godot archive with the new script. Check ZIP integrity and parsed signatures. Do not rerun `build.sh` because it deletes the existing build directory.
