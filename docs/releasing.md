# Releasing

How to build and package the Godot client for Windows, Linux, and macOS.

Everything is driven by `build.sh` at the repo root. It is a local dev tool and is not
shipped inside the build.

## Host requirements

**Linux only.** The export-template lookup assumes the XDG layout
(`${XDG_DATA_HOME:-$HOME/.local/share}/godot/export_templates/`); macOS and Windows hosts
put templates elsewhere and are not supported by the script.

On PATH: `dotnet`, `git`, `tar`, `zip`, `du`. Plus a Godot **mono** build — the script
looks for `/usr/bin/godot-mono` and honours `GODOT=/path/to/godot`.

## Godot and export templates

The engine and its export templates must be the same version, and the templates must be
the **mono** set. The current pair is **4.7.1** (`4.7.1.stable.mono`). Install them from
the editor: **Editor → Manage Export Templates**.

`build.sh` checks this up front and names the missing directory, because a version
mismatch is the failure you hit after every engine upgrade and Godot otherwise reports it
from deep inside the export.

## Asset generation

`Assets/Maps`, `Assets/Resources`, and `Assets/Sprites` are generated from the original
game data and are gitignored. Only `Assets/UI` is tracked (it is hand-made). A fresh
clone therefore cannot build until the assets are generated.

The source data lives outside this repo and its location comes from four environment
variables — see `tools/AssetConverter/src/AssetConverter/Paths.cs:10-17`:

```bash
ILLUTIA_DATA=... ILLUTIA_MAPS=... ASPERETA_DATA=... ASPERETA_MAPS=... \
  dotnet run --project tools/AssetConverter/src/AssetConverter -- all "$PWD"
```

Use `all` — it runs the full pipeline (sprite sheets, Aspereta monsters, maps). The
`batch` subcommand only produces sprite sheets and leaves the build unshippable.

`build.sh` does not regenerate assets. It checks one sentinel per generated subtree
(`Assets/Maps/Map1.bytes`, `Assets/Sprites/manifest.json`,
`Assets/Resources/AnimationHeights.txt`) and aborts with the command above if any is
missing. A merely non-empty `Assets/` is not enough: the tracked `Assets/UI` alone would
satisfy that check while the client still has no sprites or maps.

## Running a build

```bash
./build.sh                    # all three platforms
./build.sh linux windows      # only those
./build.sh --fast             # skip the test suite
./build.sh --allow-dirty      # permit uncommitted changes (marks the build id -dirty)
```

A full three-platform run takes roughly 2–3 minutes; the first export also performs the
C# build.

- `--fast` skips `dotnet test`. Tests otherwise gate the build and a failure aborts it.
- `--allow-dirty` permits a dirty working tree. By default a dirty tree **aborts** the
  build, because a build you cannot trace back to a commit is not a release.

### The build id

Every build is stamped `<UTC>-<short-sha>`, e.g. `20260818T102206Z-38d2dfb`, with `-dirty`
appended when built from a dirty tree via `--allow-dirty`. It is collision-free,
monotonic, and traceable to a commit.

The script writes it to a gitignored `build_id.txt`, which is exported into the `.pck`
(`include_filter` in each preset — it is a plain file, not an imported resource, so
`all_resources` would otherwise leave it out). The running client displays it in the
top-right corner of every screen. An EXIT trap removes the file even on failure, so the
editor shows `dev` rather than a stale id.

Builds are staged under `build/.staging` and published into `build/` only after **every**
requested platform succeeds — a partial release set is worse than none.

## Outputs

Three archives in `build/`, approximate sizes:

| Artifact | Size |
| --- | --- |
| `Goose2Client-<id>-linux.tar.gz` | ~77M |
| `Goose2Client-<id>-windows.zip` | ~87M |
| `Goose2Client-<id>-macos.zip` | ~138M |

Linux uses tar rather than zip to preserve the executable bit on the binary.

## macOS limitations

The macOS artifact is a **universal** binary (the 4.7.1 template ships universal only; an
x86_64-specific preset fails outright), **ad-hoc signed**, and **not notarized**.

Ad-hoc signing is not optional: without it the `.app` will not launch on Apple Silicon at
all. Because it is unnotarized, a user's first launch needs **right-click → Open** rather
than a double-click.

Proper signing and notarization would need an Apple Developer account and are not
implemented. The macOS artifact also cannot be smoke-tested from a Linux build host — it
is the one output that ships unverified.

## Validating artifacts

```bash
# Integrity
tar -tzf build/Goose2Client-*-linux.tar.gz >/dev/null && echo "linux ok"
unzip -t build/Goose2Client-*-windows.zip >/dev/null && echo "windows ok"
unzip -t build/Goose2Client-*-macos.zip   >/dev/null && echo "macos ok"

# The executable bit survived the tar — must show -rwx, or the archive is
# broken for users.
tar -tvzf build/Goose2Client-*-linux.tar.gz | grep 'Goose2Client\.x86_64$'

# The build stamp reached the running client. Extract somewhere empty: this
# also proves the client reads its assets from the .pck rather than from a
# checkout that happens to be nearby.
V="$(mktemp -d)"
tar -xzf build/Goose2Client-*-linux.tar.gz -C "$V"
(cd "$V" && ./Goose2Client.x86_64)
```

The top-right corner must show the build id, **not** `dev`. `dev` means `build_id.txt`
never made it into the `.pck`; a missing stamp entirely means startup threw before the
overlay was created — check the client's stderr.

To point the client at a local server, set `GOOSE_HOST` / `GOOSE_PORT`
(`Scripts/LoginScene/LoginScene.cs:20-21`); they default to `game.illutia.net:2006`.
