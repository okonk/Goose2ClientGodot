# Release Build Script — Design

Date: 2026-08-18
Branch: `feature/release-build`

## Goal

A local script that builds and packages the client for release on Windows, Linux, and
macOS in a single invocation, producing one archive per platform with a traceable build
identifier visible in-game.

Not in scope: CI, web export, code signing beyond ad-hoc, notarization, installers.
The script is a developer tool and is not shipped inside the build.

## Verified export behavior

Measured on 2026-08-18 with `godot-mono 4.7.1.stable.mono` and 4.7.1 mono export
templates. All three exports succeeded.

| Platform | Godot output | Archiving |
|---|---|---|
| Linux | `Goose2Client.x86_64` (71M) + `Goose2Client.pck` (46M) + `data_Goose2ClientGodot_linuxbsd_x86_64/` (77M) | `tar czf` — preserves the executable bit, which zip does not |
| Windows | `Goose2Client.exe` (105M) + `Goose2Client.pck` (46M) + `data_Goose2ClientGodot_windows_x86_64/` (77M) | `zip -r` |
| macOS | `Goose2Client.zip` (138M) — a self-contained, ad-hoc-signed universal `.app` carrying both arm64 and x86_64 .NET runtimes | none; rename in place |

Three constraints the test run surfaced:

- **macOS must be universal.** The 4.7.1 macOS template ships only universal binaries,
  so an x86_64-only preset fails with "Requested template binary
  `godot_macos_release.x86_64` not found". Universal in turn requires
  `rendering/textures/vram_compression/import_etc2_astc=true` in `project.godot`.
  Measured cost: none. Reimport took 2.8s and `.godot/imported` stayed at 66M, because
  the sprites use lossless 2D import and never generate ASTC data.
- **macOS needs a bundle identifier** (`net.illutia.goose2client`) and ad-hoc signing
  (`codesign/codesign=1`) to launch on Apple Silicon at all. Godot adds the "Disable
  Library Validation" entitlement automatically, which ad-hoc bundles require. The
  result is still unnotarized, so Mac users must right-click → Open the first time.
- **The `.app` is named `Goose2ClientGodot`**, inherited from `config/name`. Keeping it
  as-is for now.

## Components

**`export_presets.cfg`** (committed) — three release presets: `Linux`, `Windows`,
`macOS`. Committed rather than generated so the editor's Project → Export dialog and
the script share one source of truth. Consequence: a stray click in that dialog changes
what the script produces.

**`build.sh`** (repo root, committed) — the orchestrator. Roughly 100 lines of bash
under `set -euo pipefail`.

**`Scripts/BuildInfo.cs`** — reads `res://build_id.txt` via `FileAccess` and returns its
trimmed contents, or `"dev"` when the file is absent (i.e. running from the editor).

**`Scripts/UI/BuildStampOverlay.cs`** — a `CanvasLayer` at layer 128, created by
`GameManager._Ready()`, holding a `Label` anchored top-right with a small margin and
`MouseFilter = Ignore`. Deliberately separate from `GameManager.UiLayer` so HUD windows
can never draw over it. Nothing currently occupies that corner.

**`build_id.txt`** — gitignored, format `YYYYMMDD-HHMM`. Timestamp-only: self-contained,
monotonic, and needs no state file, which matters because `build/` is wiped each run and
so cannot carry a per-day counter.

## Script flow

    ./build.sh [--fast] [platform...]

1. **Resolve Godot** — `${GODOT:-/usr/bin/godot-mono}`; abort if not executable.
2. **Preflight** — export templates for the running engine version exist (the most
   likely failure after an engine upgrade); `Assets/` exists and is non-empty; client
   tests pass unless `--fast`; a dirty tree warns but does not block.
3. **Stamp** — compute the build id, write `build_id.txt`, register an `EXIT` trap to
   remove it. Without the trap a crashed run leaves a stale id behind and the editor
   then shows a fake build stamp instead of `dev`.
4. **Wipe** — `rm -rf build && mkdir -p build/{windows,linux,macos}`.
5. **Export** — per platform, `$GODOT --headless --path . --export-release "<preset>"
   build/<plat>/<binary>`, streaming output so MSBuild errors are visible immediately.
   The first export also performs the C# build.
6. **Archive** — Windows `zip -r`, Linux `tar czf`, macOS `mv`. Output name:
   `build/Goose2Client-<build-id>-<platform>.<ext>`. Staging directories are removed.
7. **Report** — each artifact path with its human-readable size.

Verification of `Assets/` is existence-and-non-empty only; the script does not regenerate
them. `Assets/` is gitignored derived output, so a release built against a stale or
missing `Assets/` would silently ship broken.

No arguments builds all three platforms; naming platforms builds only those.

## Error handling

Any failed step aborts with a one-line reason on stderr and a non-zero exit. A failed
platform export aborts the whole run rather than continuing, on the grounds that a
partial release set is worse than none. The `build_id.txt` trap always fires.

## Testing

`BuildInfo`'s absent-file → `"dev"` fallback gets a unit test. The script itself is not
unit-tested; verification is one real run plus launching each artifact. The macOS build
cannot be smoke-tested from Linux — that path ships unverified until someone runs it on
a Mac.

## Pre-existing blocker

`tests/Goose2Client.Tests/MapFileTests.cs:6` hardcodes
`/home/agent/workspace/Goose2ClientGodot/Assets/Maps`, a path from an earlier agent
sandbox. The test fails on this machine (258 passed, 1 failed) and has for some time.
Since tests gate the build, `build.sh` would abort on every run until it is fixed. First
task of the plan: resolve the directory relative to the repo root by walking up from
`AppContext.BaseDirectory` to find `Goose2ClientGodot.sln`, and skip the test when
`Assets/Maps` is absent, since a fresh clone has no generated assets.

## Open items

- The worktree has no `Assets/` (616K of tracked leftovers against 248M in the main
  checkout). A real export from the worktree needs assets regenerated or symlinked.
- Web export is deliberately excluded despite the templates being installed. It needs
  HTTP serving with COOP/COEP headers and has its own .NET constraints.
