# Map Editor Google Sheets Integration — Part 4: NPC Previews Implementation Plan

**Goal:** Generate and load static down-facing appearance assets, compose client-faithful NPC previews, stage entity rendering with layer-2 objects, and preserve reliable spawn editing when assets are missing or partial.

**Architecture:** `AssetConverter` exports a provider-neutral appearance manifest beside the existing map manifest. `MapEditor.Rendering` owns manifest loading, equipment parsing, appearance composition, and staged draw operations; `MapEditor.App` supplies pulled `NpcAppearance` values and renders operations through the Avalonia sink. Preview failures degrade to markers/anchors and never affect synchronization or map editing.

**Tech Stack:** C#/.NET 10; System.Text.Json; Avalonia 11; SixLabors.ImageSharp 3.1.12; xUnit.

---

> Implement with @executing-plans, one task and commit at a time. Parts 1–3 must be merged first. Run commands from `/home/agent/workspace/Goose2ClientGodot/.worktrees/map-editor-google-sheets`. Do not add comments or doc strings unless the repository exception applies.

## Scope

Included:

- `appearance-manifest.json` generation for static down-facing body and equipment frames;
- manifest loading and graceful file/entry/texture failure handling;
- client-faithful monster and humanoid composition, underwear fallback, equipment, and tint blending;
- staged rendering around map layer 2 with bottom-center Y sorting;
- preview toggle behavior, normal markers, selectable anchors/outlines, and partial-asset diagnostics;
- automated coverage and manual smoke documentation.

Excluded:

- animated previews, directions other than down, runtime parsing of Godot `.tres`, and changes to synchronization semantics;
- mounts, because the NPC sheet's `equipped_items` contains only the six chest/head/legs/feet/shield/weapon slots; live mount data is a separate packet field;
- preview persistence outside the per-document Part 3 toggle, GPU shader work, and any change to map Save, Pull, Push, validation, clipboard, or history.

## Verified repository and prerequisite facts

- The approved design requires a semantic sidecar rather than `.tres` parsing, disables only NPC art when the sidecar is absent/invalid, and keeps valid parts visible when another part is missing: `docs/plans/2026-09-06-map-editor-google-sheets-design.md:200-206`.
- The approved composition and staging contracts are monster-body-only at `body_id >= 100`, humanoid underwear/equipment, tint alpha as blend weight, static down-facing art, and layer-2/NPC Y sorting: `docs/plans/2026-09-06-map-editor-google-sheets-design.md:208-226`.
- Part 1 fixes `NpcAppearance` as body/body-state/body tint, face, hair/hair tint, and raw `EquippedItems`; it deliberately leaves equipment parsing to preview work: `docs/plans/2026-09-06-map-editor-google-sheets-part1-schema-foundation.md:63-74`, `docs/plans/2026-09-06-map-editor-google-sheets-part1-schema-foundation.md:425-430`.
- Part 3 owns per-tab overlay/preview state and introduces markers but explicitly excludes appearance art: `docs/plans/2026-09-06-map-editor-google-sheets-part3-editing.md:17-31`.
- Converter resources already retain source sheet/graphic metadata, and the generated down idle is the first frame of the down walking clip: `tools/AssetConverter/src/AssetConverter/SpriteFrames/CompiledAnimationBuilder.cs:93-107`. The merged metadata currently goes only to text resources: `tools/AssetConverter/src/AssetConverter/SpriteFrames/AnimationBatchConverter.cs:267-280`.
- `manifest` and `all` currently write only `manifest.json`; the `all` path already combines Illutia resources with synthesized Aspereta monster resources: `tools/AssetConverter/src/AssetConverter/Program.cs:128-155`, `tools/AssetConverter/src/AssetConverter/Program.cs:190-202`.
- The live client reads six equipment slots in chest, head, legs, feet, shield, weapon order. Each slot is `id,*` or `id,r,g,b,a`; mount follows separately and is not part of `equipped_items`: `Scripts/Network/Packets/MakeCharacterPacket.cs:97-132`.
- The live client removes all non-body parts for body IDs at least 100, applies male legs `3` and female chest/legs `8`/`4` only when empty, then builds slots: `Scripts/Character/Character.cs:258-296`, `Scripts/Character/CharacterLayout.cs:54-68`.
- Down-facing back-to-front slot order is body, eyes, feet, legs, chest, hair, helm, shield, weapon; shield and weapon remain in front when facing down, and both use `Hands`: `Scripts/Character/CharacterLayout.cs:5-16`, `Scripts/Character/CharacterLayout.cs:18-50`.
- Client tint computes `mix(texture.rgb, tint.rgb, tint.a)` while preserving source alpha: `Scripts/TintMaterial.cs:5-20`; zero tint alpha means no tint in appearance setup: `Scripts/Character/Character.cs:303-315`.
- Existing map rendering bottom-center anchors sprites but emits every visible layer before overlays: `src/MapEditor.Rendering/Composition/MapRenderer.cs:59-126`, `src/MapEditor.Rendering/Composition/MapRenderer.cs:128-140`. Existing operations have no appearance/tint/group concept: `src/MapEditor.Rendering/Composition/MapDrawOperations.cs:19-48`.
- `SpriteAssetCache` already distinguishes unavailable assets, unknown sheets/graphics, missing files, decode failure, and out-of-sheet frames while caching resolutions: `src/MapEditor.Rendering/Assets/SpriteAssetCache.cs:35-52`, `src/MapEditor.Rendering/Assets/SpriteAssetCache.cs:54-104`.
- `AssetContext` currently opens only `manifest.json`, and the controller atomically replaces a context only after creation succeeds: `src/MapEditor.App/Rendering/AssetContext.cs:26-43`, `src/MapEditor.App/Rendering/AssetContextController.cs:35-83`.
- `MapCanvas` is the composition boundary that derives render options from the active document: `src/MapEditor.App/Controls/MapCanvas.cs:474-498`. The Avalonia sink currently performs only untinted image and primitive drawing: `src/MapEditor.App/Rendering/AvaloniaMapDrawSink.cs:17-49`.

## Locked behavioral contracts

### Appearance manifest

- Emit deterministic UTF-8 JSON at `Assets/Sprites/appearance-manifest.json` whenever `AnimationBatchConverter.Convert` writes character resources, including the `animations` and `all` commands. Do not check generated art or manifests into source control.
- Contract version 1 is:

```json
{"version":1,"parts":{"Body":{"1":{"noEquip":[1000,108760],"equip":[1001,108900]}},"Hair":{},"Eyes":{},"Chest":{},"Helm":{},"Legs":{},"Feet":{},"Hand":{}}}
```

- The eight exact, case-sensitive kinds are `Body`, `Hair`, `Eyes`, `Chest`, `Helm`, `Legs`, `Feet`, and `Hand`. IDs are positive decimal keys. `noEquip` is the first frame of `idle-no-equip-down`; `equip` is the first frame of `idle-equip-down`. Omit an unavailable variant, but omit an ID only if neither variant exists. Both variants may point to the same frame. Sort kinds in the listed order and IDs numerically; property order is `noEquip`, then `equip`.
- `Hand` serves both shield and weapon. Aspereta monster bodies enter `Body` through the same `extraResources` list used by `all`.
- Resolution chooses `equip` first when `BodyState != 3`, otherwise `noEquip` first, then the other variant. This mirrors the live idle candidate behavior without exporting animations.

### Composition, anchoring, and tint

- Parse exactly six equipment slots atomically in chest/head/legs/feet/shield/weapon order. Each begins with a non-negative invariant ID followed by either `*` or four byte values. Reject missing/extra/empty tokens, invalid IDs/bytes, and overflow as one equipment diagnostic; retain body/face/hair and use no equipment rather than shifting later slots.
- `body_id >= 100` never parses or emits humanoid parts. A non-positive body ID is a missing body part, not a crash.
- Humanoid order is body, eyes, feet, legs, chest, hair, helm, shield, weapon. Empty male body `1` legs become untinted legs `3`; empty female body `11` legs/chest become untinted legs `4` and chest `8`. Explicit equipment, including its tint, wins.
- A zero part ID emits nothing. A positive ID absent from the appearance manifest or failing sprite resolution emits one part placeholder/diagnostic and does not hide ready parts. Duplicate shield/weapon IDs remain two independently ordered slots.
- Every slot uses the client center-anchor formula `offsetY = max((height - 48) / 2, 0) - height / 2` with integer division. At tile bottom-center `(x*32+16, (y+1)*32)`, destination is `(anchorX-width/2, anchorY+offsetY-height/2, width, height)` before viewport scaling.
- Tint bytes remain unchanged in rendering contracts. Alpha zero draws the original source. Alpha above zero applies per channel `round(source*(255-A)/255 + tint*A/255)` and preserves source alpha exactly. Cache tinted frame bitmaps by source image identity, source rect, and RGBA; never mutate shared sheet images.

### Rendering and failure isolation

- Stages are visible layers 0–1; one entity stage containing layer-2 map objects and complete NPC groups; visible layers 3–4; blocked/grid; game-data markers/preview anchors; existing selection/tool overlays.
- Entity sort key is bottom-center world Y, then X, then kind (`MapObject` before `Npc`), then map row-major ordinal or spawn occurrence index. All operations for one NPC stay contiguous in slot order. Sorting is independent of sprite height and viewport scale.
- Normal mode emits the Part 3 compact marker and no appearance operations. Preview mode emits composed art/placeholders plus a selectable spawn anchor/outline and suppresses only the compact marker. The anchor is always present for every visible spawn, including unknown NPCs, malformed equipment, fully transparent art, and total part failure.
- Missing/invalid `appearance-manifest.json` makes `AppearanceAvailability` unavailable with one actionable diagnostic while leaving the map `SpriteAssetCache` usable. Preview mode then renders normal markers and reports unavailability nonmodally; it must not throw or silently clear the user's preview preference.
- A valid sidecar with bad entries is rejected globally. Missing semantic IDs, missing sheet frames/files, decode failures, and bounds failures are per-part diagnostics. Rendering diagnostics never alter rows, selection, dirty baselines, history, sync command enablement, or map saving.

## Mutation impact matrix

| Mutation | Owner/source of truth | Readers and required propagation | Failure atomicity |
|---|---|---|---|
| Animation conversion | converter resource list | `.tres`, metadata text, appearance sidecar | Build complete JSON, write and flush a same-directory temporary file, then atomically replace/move the sidecar; failure preserves the prior file |
| Asset-directory open | `AssetContextController` | map cache, palette, appearance catalog, all canvases/status | Bad map manifest preserves old context; bad/missing appearance sidecar publishes the new map context with previews unavailable |
| Pull/re-Pull or local spawn edit | Part 3 document game-data state | immutable preview inputs, marker inputs, canvas invalidation | Appearance derivation is read-only; malformed appearance/equipment cannot reject or rewrite pulled rows |
| Preview toggle | per-document Part 3 preference | effective mode, toolbar/menu check, status, canvas | Toggle changes presentation only and remains stored even when effective mode falls back to markers |
| Render/cache tint | App asset context | Avalonia sink and tinted-frame cache | Failed part/tint conversion emits a diagnostic placeholder/anchor; prior cached images remain valid |
| Asset-context replace/dispose | App controller | map images and tinted frame bitmaps | Publish candidate before disposing old context; dispose each owned original/tinted image exactly once |

## Invariant matrix

| Invariant | Required proof |
|---|---|
| Generated references are static down-facing frames and deterministic | converter unit snapshot plus repeated-build byte equality |
| Map assets work without a usable appearance sidecar | asset-context tests for absent/malformed sidecars |
| Monsters never leak humanoid parts | composer matrix at IDs 99/100 with populated equipment |
| Humanoid ordering, underwear, equipment parsing, and tint bytes match the client | table-driven composer/parser tests |
| Layer 2 and whole NPC groups share one stable Y sort | rendering operation-order tests with ties, duplicates, and tall sprites |
| Preview mode never removes the selectable spawn affordance | rendering tests and real pointer selection test |
| Asset failures never mutate game data or disable Save/Pull/Push | view-model/window tests using failing loaders/manifests |
| Tint preserves texture alpha and is not opacity | pixel-level sink tests at A=0, 128, and 255 |
| Context replacement owns/disposes all tinted images | controller/cache disposal tests |

## Task 1: Define and emit the static appearance manifest

**Files:**
- Create: `tools/AssetConverter/src/AssetConverter/Manifest/AppearanceManifestBuilder.cs`
- Create: `tools/AssetConverter/src/AssetConverter/Manifest/AppearanceManifestFileStore.cs`
- Modify: `tools/AssetConverter/src/AssetConverter/SpriteFrames/AnimationBatchConverter.cs`
- Modify: `tools/AssetConverter/src/AssetConverter/Program.cs`
- Create: `tools/AssetConverter/tests/AssetConverter.Tests/AppearanceManifestBuilderTests.cs`
- Modify: `tools/AssetConverter/tests/AssetConverter.Tests/AnimationBatchConverterTests.cs`

**Mutation impact:** Derive the sidecar only from already-built `CompiledSpriteFramesResource` values. Sidecar generation adds one output to animation conversion; it must not alter `.tres`, first-frame text, heights, warnings, counts, or source assets.

**Task invariant-test matrix:**

| Invariant | Test case |
|---|---|
| Exact v1 shape/order | mixed unsorted resources serialize to the locked one-line JSON in kind/ID/property order |
| Correct frame | multi-frame down clips choose frame zero; up/left/right and attack clips are ignored |
| Variant fidelity | no-equip only, equip only, both, and same-reference variants |
| Semantic mapping | all eight `AnimationType` values map exactly; shield/weapon are represented once as `Hand` |
| Combined monsters | `extraResources` Aspereta body appears beside Illutia body |
| Safe output | repeated build is byte-identical; conflicting duplicates and injected write/replace failures preserve the prior sidecar and leave no temporary file |

**Step 1: Write failing tests**

Build synthetic `CompiledSpriteFramesResource` instances without real ADF data. Assert exact JSON, numeric ordering (`2` before `10`), omitted absent variants, duplicate-equal coalescing, duplicate-conflict rejection, and frame-zero selection. Extend the batch converter temp-directory test to assert both metadata files and `Assets/Sprites/appearance-manifest.json` are emitted, with no count changes. Through an injectable file-operations seam matching the real create/write/flush/replace/delete calls, force failures before and during replacement and assert a pre-existing sidecar remains byte-for-byte intact with no orphan temporary file.

**Step 2: Run red**

```bash
dotnet test tools/AssetConverter/tests/AssetConverter.Tests/AssetConverter.Tests.csproj --filter 'FullyQualifiedName~AppearanceManifestBuilder|FullyQualifiedName~AnimationBatchConverter'
```

Expected: FAIL because no semantic sidecar builder/output exists.

**Step 3: Implement minimum**

Add a pure builder over resource animation specs. Select exact `idle-no-equip-down` and `idle-equip-down` names and their first frame references; do not parse emitted `.tres` or the legacy text file. Build JSON fully, write it to a uniquely named temporary file beside the destination, flush and close it, then use same-directory atomic replacement when the destination exists or an atomic move when it does not. Clean up the temporary path on every failure while preserving an existing destination. Use a narrow injectable file-operations seam so tests exercise failure ordering without mocking the manifest builder. Update `animations`/`all` console output and usage only enough to name the sidecar.

**Step 4: Run green**

```bash
dotnet test tools/AssetConverter/tests/AssetConverter.Tests/AssetConverter.Tests.csproj --filter 'FullyQualifiedName~AppearanceManifestBuilder|FullyQualifiedName~AnimationBatchConverter'
```

**Step 5: Commit**

```bash
git add tools/AssetConverter/src/AssetConverter/Manifest/AppearanceManifestBuilder.cs \
  tools/AssetConverter/src/AssetConverter/Manifest/AppearanceManifestFileStore.cs \
  tools/AssetConverter/src/AssetConverter/SpriteFrames/AnimationBatchConverter.cs \
  tools/AssetConverter/src/AssetConverter/Program.cs \
  tools/AssetConverter/tests/AssetConverter.Tests/AppearanceManifestBuilderTests.cs \
  tools/AssetConverter/tests/AssetConverter.Tests/AnimationBatchConverterTests.cs
git commit -m "feat: emit static appearance manifest"
```

## Task 2: Load appearance assets and expose partial availability

**Files:**
- Create: `src/MapEditor.Rendering/Appearance/AppearancePartKind.cs`
- Create: `src/MapEditor.Rendering/Appearance/AppearanceManifest.cs`
- Create: `src/MapEditor.Rendering/Appearance/AppearanceManifestException.cs`
- Create: `src/MapEditor.Rendering/Appearance/AppearanceAssetCatalog.cs`
- Modify: `src/MapEditor.App/Rendering/AssetContext.cs`
- Modify: `src/MapEditor.App/Rendering/AssetContextController.cs`
- Create: `tests/MapEditor.Rendering.Tests/AppearanceManifestTests.cs`
- Create: `tests/MapEditor.Rendering.Tests/AppearanceAssetCatalogTests.cs`
- Modify: `tests/MapEditor.App.Tests/AssetContextControllerTests.cs`

**Mutation impact:** `AssetContext` gains an appearance catalog and availability diagnostic while retaining one shared `SpriteAssetCache`. Map-manifest success still controls context publication; appearance-sidecar success controls only effective previews.

**Task invariant-test matrix:**

| Invariant | Test case |
|---|---|
| Strict contract | malformed JSON/root/version/kind/ID/variant/reference/duplicate property each reports source path and reason |
| Optional sidecar | absent or unreadable file opens map assets and reports unavailable preview |
| Atomic context | malformed sidecar publishes usable new map context; malformed `manifest.json` still preserves old context |
| Partial resolution | unknown semantic ID, unknown frame, missing PNG, decode failure, and out-of-bounds frame return distinct diagnostics |
| Shared ownership | repeated part references load one sheet once; context disposal disposes it once |
| Variant choice | body state 3 prefers no-equip; other states prefer equip; missing preferred variant falls back |

**Step 1: Write failing tests**

Use inline JSON and temporary asset directories. Cover every strict parse boundary, deterministic lookups, unavailable state, and all existing `SpriteResolutionStatus` values through the catalog. Extend controller tests to prove a missing/malformed appearance sidecar does not reject the directory, clear sheet IDs, replace settings incorrectly, or make map sprite resolution unavailable.

**Step 2: Run red**

```bash
dotnet test tests/MapEditor.Rendering.Tests/MapEditor.Rendering.Tests.csproj --filter 'FullyQualifiedName~AppearanceManifest|FullyQualifiedName~AppearanceAssetCatalog'
dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj --filter 'FullyQualifiedName~AssetContextController'
```

Expected: FAIL because `AssetContext` has no independently optional appearance catalog.

**Step 3: Implement minimum**

Parse with `JsonDocument` so duplicate and unknown structural values can be diagnosed consistently with `SpriteManifest`. `AppearanceAssetCatalog.TryResolve(kind, id, bodyState)` first resolves the semantic variant, then delegates sheet/graphic validation and texture ownership to `SpriteAssetCache`. Catch only expected sidecar I/O/parse failures in `AssetContext.Create`; do not catch programmer errors or map-manifest failures.

**Step 4: Run green**

```bash
dotnet test tests/MapEditor.Rendering.Tests/MapEditor.Rendering.Tests.csproj --filter 'FullyQualifiedName~AppearanceManifest|FullyQualifiedName~AppearanceAssetCatalog'
dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj --filter 'FullyQualifiedName~AssetContextController'
```

**Step 5: Commit**

```bash
git add src/MapEditor.Rendering/Appearance src/MapEditor.App/Rendering/AssetContext.cs \
  src/MapEditor.App/Rendering/AssetContextController.cs \
  tests/MapEditor.Rendering.Tests/AppearanceManifestTests.cs \
  tests/MapEditor.Rendering.Tests/AppearanceAssetCatalogTests.cs \
  tests/MapEditor.App.Tests/AssetContextControllerTests.cs
git commit -m "feat: load optional appearance assets"
```

## Task 3: Compose client-faithful NPC appearance operations

**Files:**
- Modify: `src/MapEditor.Rendering/MapEditor.Rendering.csproj`
- Create: `src/MapEditor.Rendering/Appearance/NpcEquipmentParser.cs`
- Create: `src/MapEditor.Rendering/Appearance/NpcAppearanceComposer.cs`
- Create: `src/MapEditor.Rendering/Appearance/NpcAppearanceOperations.cs`
- Create: `tests/MapEditor.Rendering.Tests/NpcEquipmentParserTests.cs`
- Create: `tests/MapEditor.Rendering.Tests/NpcAppearanceComposerTests.cs`

**Mutation impact:** Add a provider-neutral reference from Rendering to Part 1 `MapEditor.GameData` solely for immutable `NpcAppearance`/`RgbaValue`. Composition reads appearance/catalog state and returns immutable ready/placeholder operations; it owns no rows, images, or mutable cache state.

**Task invariant-test matrix:**

| Invariant | Test case |
|---|---|
| Exact wire-string parsing | six `id,*` slots; mixed tinted slots; whitespace/empty/missing/extra/overflow/bad byte rejection |
| Monster boundary | ID 99 composes humanoid; ID 100 and larger emit body only and ignore malformed equipment |
| Underwear | male/female empty defaults, explicit replacements, and non-1/11 body no fallback |
| Slot order | all populated slots equal the locked nine-slot down order; duplicate hands remain ordered shield then weapon |
| Tint semantics | body/hair/equipment retain exact RGBA; eyes/underwear are zero-alpha untinted |
| Partial assets | one missing manifest entry/texture adds one placeholder while all ready siblings remain |
| Geometry | short, 48px, 64px, odd-size frames match the client integer anchor formula at every zoom input |

**Step 1: Write failing parser and composer tests**

Use table-driven equipment strings, including the default `0,*,0,*,0,*,0,*,0,*,0,*`. Assert malformed equipment yields one diagnostic and no equipment slots but leaves body/eyes/hair. Build fake catalogs for all-ready and per-slot failures. Assert exact operation sequences, references, tints, semantic slot names, and unscaled destination rectangles.

**Step 2: Run red**

```bash
dotnet test tests/MapEditor.Rendering.Tests/MapEditor.Rendering.Tests.csproj --filter 'FullyQualifiedName~NpcEquipmentParser|FullyQualifiedName~NpcAppearanceComposer'
```

Expected: FAIL because raw equipment and `NpcAppearance` have no rendering translation.

**Step 3: Implement minimum**

Parse all tokens into a temporary six-slot array and publish only after complete validation. Keep underwear and monster rules in the composer, not App. Return one `NpcAppearanceGroup` carrying occurrence/tile/sort anchor and ordered `NpcPartDrawOperation` values; represent equipment parse failure as a group diagnostic for the later anchor, not a fake shifted part. Reuse catalog resolution statuses verbatim.

**Step 4: Run green**

```bash
dotnet test tests/MapEditor.Rendering.Tests/MapEditor.Rendering.Tests.csproj --filter 'FullyQualifiedName~NpcEquipmentParser|FullyQualifiedName~NpcAppearanceComposer'
```

**Step 5: Commit**

```bash
git add src/MapEditor.Rendering/MapEditor.Rendering.csproj src/MapEditor.Rendering/Appearance \
  tests/MapEditor.Rendering.Tests/NpcEquipmentParserTests.cs \
  tests/MapEditor.Rendering.Tests/NpcAppearanceComposerTests.cs
git commit -m "feat: compose client-faithful npc previews"
```

## Task 4: Stage map objects and NPC groups with stable Y sorting

**Files:**
- Modify: `src/MapEditor.Rendering/Composition/MapRenderOptions.cs`
- Modify: `src/MapEditor.Rendering/Composition/MapDrawOperations.cs`
- Modify: `src/MapEditor.Rendering/Composition/IMapDrawSink.cs`
- Modify: `src/MapEditor.Rendering/Composition/MapRenderer.cs`
- Modify: `tests/MapEditor.Rendering.Tests/Fakes/RecordingMapDrawSink.cs`
- Modify: `tests/MapEditor.Rendering.Tests/MapRendererTests.cs`
- Create: `tests/MapEditor.Rendering.Tests/NpcPreviewRenderingTests.cs`

**Mutation impact:** Extend immutable render options with preview inputs and appearance mode/catalog. Refactor only emission order: layer data and map documents remain unchanged. Layer-2 resolutions and complete composed NPC groups are buffered for one bounded entity sort; other layers retain row-major emission.

**Task invariant-test matrix:**

| Invariant | Test case |
|---|---|
| Stage boundaries | layers 0/1 precede entities; layers 3/4 follow; blocked/grid/markers/selections remain overlays |
| Stable entity order | interleaved map objects/NPCs at different Y, same Y/X ties, duplicate spawns, and repeated render equality |
| Group contiguity | a tall multi-part NPC is never interleaved with another NPC or map object |
| Sort anchor | sprite height and zoom do not change bottom-center key; X/kind/ordinal break ties exactly |
| Culling/work bound | offscreen groups emit nothing; partially visible tall parts emit; work-limit accounting includes entity candidates |
| Mode split | normal emits markers only; preview emits parts/placeholders and anchors but no compact spawn marker |
| Existing behavior | no-NPC request preserves prior layer order, placeholder, culling, grid, and selection assertions |

**Step 1: Write failing operation-order tests**

Record all sink calls with typed operations. Construct maps with layers 0–4 and spawn groups above, on, and below layer-2 objects. Assert the exact total sequence and tie keys, group contiguity, viewport clipping, nearest-neighbor sampling, placeholder placement, and overlay order. Keep a regression test with empty preview inputs whose call sequence matches existing tests byte-for-byte at the operation level.

**Step 2: Run red**

```bash
dotnet test tests/MapEditor.Rendering.Tests/MapEditor.Rendering.Tests.csproj --filter 'FullyQualifiedName~NpcPreviewRendering|FullyQualifiedName~MapRenderer'
```

Expected: FAIL because all five layers are emitted flat and the sink has no NPC/group operations.

**Step 3: Implement minimum**

Extract reusable map-layer emission, but buffer only visible layer-2 entries and visible NPC groups. Count candidate reads before allocation and retain `MaximumTileReadsPerRender`. Sort explicit entity records with the locked comparer; emit every group part before the next entity. Add distinct sink operations for tinted NPC image, part placeholder, compact marker, and preview anchor so App never infers semantics from colors.

**Step 4: Run green**

```bash
dotnet test tests/MapEditor.Rendering.Tests/MapEditor.Rendering.Tests.csproj --filter 'FullyQualifiedName~NpcPreviewRendering|FullyQualifiedName~MapRenderer'
```

**Step 5: Commit**

```bash
git add src/MapEditor.Rendering/Composition \
  tests/MapEditor.Rendering.Tests/Fakes/RecordingMapDrawSink.cs \
  tests/MapEditor.Rendering.Tests/MapRendererTests.cs \
  tests/MapEditor.Rendering.Tests/NpcPreviewRenderingTests.cs
git commit -m "feat: stage and y-sort npc previews"
```

## Task 5: Integrate preview controls, resilient anchors, and smoke coverage

**Files:**
- Modify: `src/MapEditor.App/MapEditor.App.csproj`
- Modify: `src/MapEditor.App/Rendering/AssetContext.cs`
- Create: `src/MapEditor.App/Rendering/AvaloniaTintedSpriteCache.cs`
- Modify: `src/MapEditor.App/Rendering/AvaloniaMapDrawSink.cs`
- Modify: `src/MapEditor.App/Rendering/IMapDrawTarget.cs`
- Modify: `src/MapEditor.App/Controls/MapCanvas.cs`
- Modify: `src/MapEditor.App/ViewModels/MapDocumentViewModel.cs`
- Modify: `src/MapEditor.App/Views/MainWindow.axaml`
- Modify: `src/MapEditor.App/Views/MainWindow.axaml.cs`
- Modify: `tests/MapEditor.App.Tests/Fakes/RecordingMapDrawTarget.cs`
- Create: `tests/MapEditor.App.Tests/AvaloniaTintedSpriteCacheTests.cs`
- Modify: `tests/MapEditor.App.Tests/AvaloniaMapDrawSinkTests.cs`
- Create: `tests/MapEditor.App.Tests/NpcPreviewCanvasTests.cs`
- Modify: `tests/MapEditor.App.Tests/MainWindowGameDataTests.cs`
- Modify: `tests/MapEditor.App.Tests/AssetContextControllerTests.cs`
- Modify: `docs/map-editor-google-sheets-setup.md`

**Mutation impact:** Derive immutable `(spawn occurrence, NpcAppearance?)` preview inputs from the active Part 3 session on each render request. The existing toggle remains the preference; effective preview availability and status are derived. The App-only tint cache owns generated frame bitmaps and is disposed with its asset context.

**Task invariant-test matrix:**

| Invariant | Test case |
|---|---|
| Exact pixels | 2×2 RGBA fixture at tint A=0/128/255 matches blend formula and source alpha byte-for-byte |
| Cache lifecycle | repeated same key reuses one bitmap; tint/rect/image changes key; context replacement disposes all outputs |
| Input freshness | Pull replacement, add/move/delete, undo/redo, and NPC property change redraw current occurrences only |
| Toggle isolation | switching mode changes operations/status only, with zero row/history/dirty/sync-command changes |
| Anchor selection | transparent, unknown-NPC, malformed-equipment, and all-assets-missing previews remain clickable by occurrence |
| Graceful fallback | absent/invalid sidecar shows normal marker plus nonmodal status; one bad part shows remaining art and diagnostic anchor |
| Per-tab state | two tabs retain independent preference/effective rendering while sharing one appearance catalog |
| Smoke path | generated tiny manifests+PNG open, compose, tint, Y-sort, toggle, and select through a real headless canvas |

**Step 1: Write failing pixel and sink tests**

Add ImageSharp 3.1.12 to App. Generate a tiny PNG with opaque, translucent, and transparent pixels; assert the locked integer blend and unchanged alpha. Test cache keys/disposal and that sink destinations use the tinted frame's local source rect while untinted map sprites still use the sheet source rect.

**Step 2: Write failing real UI and smoke tests**

Under a shown headless window, attach pulled NPC appearances and duplicate spawn occurrences. Drive the actual menu/toolbar preview toggle and raw pointer selection. Cover tab switching, normal/preview operation changes, always-present anchors, unavailable status text, a missing part with surviving siblings, malformed equipment, unknown NPC, and asset-context replacement. Assert Save, Pull, Push, undo/redo, clipboard, and dirty properties are unchanged by toggling/failures.

Create one hermetic smoke test that writes `manifest.json`, `appearance-manifest.json`, and a tiny sheet PNG to a temp directory, opens it through `AssetContextController`, renders layer-2 art plus two NPCs, verifies tinted output/order, toggles back to markers, and selects each spawn. It must not use live Google credentials or repository-local game assets.

**Step 3: Run red**

```bash
dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj --filter 'FullyQualifiedName~AvaloniaTintedSpriteCache|FullyQualifiedName~AvaloniaMapDrawSink|FullyQualifiedName~NpcPreviewCanvas|FullyQualifiedName~MainWindowGameData'
```

Expected: FAIL because App neither supplies appearance inputs nor renders tint/anchors/fallback status.

**Step 4: Implement minimum**

Use ImageSharp only inside `AvaloniaTintedSpriteCache` to crop and blend source-frame pixels, encode a frame bitmap, and cache it. Pass the cache into `AvaloniaMapDrawSink`; do not add pixel concerns to Rendering or GameData. Build preview inputs by occurrence index from current rows and NPC lookup. Keep hit testing on Part 3 row/tile identity rather than sprite rectangles. Bind checked state to preference and expose a separate effective/unavailable status; unavailable fallback must not auto-uncheck the preference.

Append a manual smoke section to the existing setup guide covering converter generation, asset-directory open, normal/preview toggle, male/female underwear, monster, tinted equipment, overlap selection, layer-2 crossing/Y order, missing sidecar, malformed sidecar, missing part, missing PNG, Save while previews fail, and restoration after reopening valid assets. State expected marker/anchor and nonmutation behavior for each failure.

**Step 5: Run green and focused regressions**

```bash
dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj --filter 'FullyQualifiedName~AvaloniaTintedSpriteCache|FullyQualifiedName~AvaloniaMapDrawSink|FullyQualifiedName~NpcPreviewCanvas|FullyQualifiedName~MainWindowGameData|FullyQualifiedName~AssetContextController'
dotnet test tests/MapEditor.Rendering.Tests/MapEditor.Rendering.Tests.csproj
dotnet test tools/AssetConverter/tests/AssetConverter.Tests/AssetConverter.Tests.csproj
```

**Step 6: Commit**

```bash
git add src/MapEditor.App/MapEditor.App.csproj src/MapEditor.App/Rendering \
  src/MapEditor.App/Controls/MapCanvas.cs src/MapEditor.App/ViewModels/MapDocumentViewModel.cs \
  src/MapEditor.App/Views/MainWindow.axaml src/MapEditor.App/Views/MainWindow.axaml.cs \
  tests/MapEditor.App.Tests/Fakes/RecordingMapDrawTarget.cs \
  tests/MapEditor.App.Tests/AvaloniaTintedSpriteCacheTests.cs \
  tests/MapEditor.App.Tests/AvaloniaMapDrawSinkTests.cs \
  tests/MapEditor.App.Tests/NpcPreviewCanvasTests.cs \
  tests/MapEditor.App.Tests/MainWindowGameDataTests.cs \
  tests/MapEditor.App.Tests/AssetContextControllerTests.cs docs/map-editor-google-sheets-setup.md
git commit -m "feat: integrate resilient npc preview controls"
```

## Final red-team verification

```bash
dotnet test tools/AssetConverter/tests/AssetConverter.Tests/AssetConverter.Tests.csproj
dotnet test tests/MapEditor.GameData.Tests/MapEditor.GameData.Tests.csproj
dotnet test tests/MapEditor.Rendering.Tests/MapEditor.Rendering.Tests.csproj
dotnet test tests/MapEditor.App.Tests/MapEditor.App.Tests.csproj
dotnet test Goose2ClientGodot.sln
dotnet build Goose2ClientGodot.sln
git diff --check
git status --short
```

Review the final diff and test names to confirm:

- exactly five task commits exist, each green before the next starts;
- no `.tres` parser, animation clock, non-down direction, mount preview, Google dependency, credential, generated sprite, PNG, or generated manifest was added;
- converter output is deterministic and contains both Illutia and extra Aspereta body resources without changing existing animation artifacts/counts;
- appearance-sidecar failure cannot reject otherwise valid map assets, and per-part resolution failures retain siblings;
- monster cutoff, equipment grammar/order, underwear IDs, slot order, hand reuse, anchor formula, and tint pixels match the cited client behavior;
- layer 2 is emitted exactly once in the shared entity stage, NPC groups are contiguous, and tie ordering is deterministic;
- normal markers and preview anchors preserve occurrence-based selection, including duplicates and total art failure;
- preview preference remains per tab and presentation-only; no preview path invokes or changes Pull, Push, map Save, validation, clipboard, resize, dirty baselines, or either history;
- all original and generated bitmaps are disposed exactly once on context replacement/shutdown;
- the manual smoke section records expected fallback behavior and requires no live credentials for asset checks.
