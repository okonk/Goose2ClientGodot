# All Aspereta Animations Implementation Plan

**Goal:** Convert every resolvable Aspereta compiled and standalone animation, preserve source timing, and support imported layered bodies consistently in the Godot client and Goose server.

**Architecture:** A shared `AsperetaAnimationCatalog` becomes the only owner of global frame/animation indexing, slot resolution, timing, claimed IDs, and diagnostics. Character resources, effects, and manifests consume the catalog; the client and server share the same layered-body ID rule without changing packet shape or the server remapper.

**Tech Stack:** C# / .NET 10 asset converter and tests; Godot C# client targeting .NET 8 with xUnit tests; Goose server C# with xUnit; Godot SpriteFrames `.tres` and JSON manifests.

---

## APIs verified

- Aspereta ADF records contain an interval byte that is currently discarded: `tools/AssetConverter/src/AssetConverter/Adf/AsperetaAdf.cs:33-55`. The source Aspereta client converts it to frame duration `1 / interval / 2`, so output FPS is `interval * 2`: `/home/agent/workspace/gooseclient/AsperetaClient/ResourceManager.cs:153-163`.
- The shared `Animation` model currently has frames and optional raw source frame IDs but no timing: `tools/AssetConverter/src/AssetConverter/Adf/IllutiaData.cs:117-131`.
- Aspereta `compiled.enc` has 32 indexes and currently exposes only state 1; raw type casting misreads source Hand as Illutia Eyes: `tools/AssetConverter/src/AssetConverter/Aspereta/AsperetaCompiledEnc.cs:6-34`.
- The existing monster converter mutates sheet models, filters to Body IDs 100+, reads only state 1, and enforces one sheet per animation/order: `tools/AssetConverter/src/AssetConverter/Aspereta/AsperetaMonsterConverter.cs:17-99,101-195`.
- `SpriteFramesWriter` already accepts a sheet and texture path per frame, so mixed-sheet output needs no file-format change: `tools/AssetConverter/src/AssetConverter/SpriteFrames/SpriteFramesWriter.cs:52-89,115-153`.
- Effects currently filter to `0..999` and `115000..115999`: `tools/AssetConverter/src/AssetConverter/Aspereta/AsperetaEffectsConverter.cs:30-40,71-105`.
- The animation manifest independently reconstructs Aspereta resolution and hardcodes 8 FPS: `tools/AssetConverter/src/AssetConverter/Manifest/AnimationManifestBuilder.cs:8-12,174-304`.
- `all` and `aspereta` currently run different animation phases: `tools/AssetConverter/src/AssetConverter/Program.cs:85-108,179-246`.
- Client clip candidates are centralized in `AnimationNames.Candidates`: `Scripts/Character/AnimationNames.cs:36-79`; runtime and preview callers select the first available candidate at `Scripts/Character/Character.cs:819-831` and `Scripts/UI/CustomPreviewControl.cs:113-122`.
- Client packet shape is selected by `BodyId < 100`: `Scripts/Network/Packets/MakeCharacterPacket.cs:51-92` and `Scripts/Network/Packets/UpdateCharacterPacket.cs:31-71`. Rendering separately strips layers for `bodyId >= 100`: `Scripts/Character/Character.cs:311-349`.
- Server MKC/CHP producers repeat `CurrentBodyID >= 100`: `/home/agent/workspace/illutiagooseserver/.worktrees/import-all-aspereta-animations/Goose/Packets.cs:111-278`, `Goose/Commands/GmHaxCommand.cs:15-41`, and `Goose/Data/Illutia/Scripts/NPC/BodyStealerNPC.csx:27-54`.
- The server’s native Aspereta protocol override is a different wire format and is not part of this change: `Goose/Data/Aspereta/Scripts/Global/Aspereta.csx:314-437`.

## Baseline

- Client suite: passing.
- Server suite: passing.
- Asset converter with local source paths: 130/134 passing. Four dataset-pinned Illutia tests already fail because local Illutia data differs from expected record/payload/frame fixtures. Preserve this baseline; new hermetic and Aspereta tests must pass.

---

### Task 1: Preserve Aspereta timing and decode every compiled type/state

**Files:**
- Modify: `tools/AssetConverter/src/AssetConverter/Adf/IllutiaData.cs:117-131`
- Modify: `tools/AssetConverter/src/AssetConverter/Adf/AsperetaAdf.cs:33-55`
- Modify: `tools/AssetConverter/src/AssetConverter/Aspereta/AsperetaCompiledEnc.cs:6-34`
- Modify: `tools/AssetConverter/tests/AssetConverter.Tests/Fixtures/AnimationSourceFixture.cs:81-145`
- Modify: `tools/AssetConverter/tests/AssetConverter.Tests/AsperetaAdfTests.cs`
- Create: `tools/AssetConverter/tests/AssetConverter.Tests/AsperetaCompiledEncTests.cs`

**Mutation impact:**
- Source of truth changed: decoded ADF `Animation` gains source timing; compiled entries expose the complete 32-slot schema.
- Important readers: monster/effect converters and `AnimationManifestBuilder`; these are migrated to the catalog in later tasks.
- Derived/cached state affected: generated `.tres` speed and manifest FPS. No runtime cache is mutated.
- Required propagation sequence: decode interval → retain it on `Animation` → catalog derives FPS → all writers copy FPS.
- Invariants to preserve: Illutia animations have no source interval and remain 8 FPS; Aspereta slot index is `motionOffset + facing * 4 + stateIndex`.
- Observable proof required: parser and accessor tests assert decoded values and exact slot positions.

**Step 1: Write failing parser and compiled-layout tests**

Extend the fixture writer so each animation definition can encode a chosen interval. Add tests proving:

- an encoded interval of 3 survives parsing;
- raw types 1–7 map to Body, Hair, Hand, Chest, Helm, Legs, Feet;
- raw type 3 is Hand, never Eyes;
- walk and attack accessors reach all four states for all facings;
- an unknown raw type throws a deterministic `InvalidDataException`.

The compiled API should use zero-based state indexes internally or one-based source states publicly, but not mix them. Prefer:

```csharp
public int Walk(int facing, int state) => Indexes[facing * 4 + state - 1];
public int Attack(int facing, int state) => Indexes[16 + facing * 4 + state - 1];
```

Validate `facing` in `0..3` and `state` in `1..4`.

**Step 2: Run tests to verify red**

Run:

```bash
dotnet test tools/AssetConverter/AssetConverter.sln --filter "FullyQualifiedName~AsperetaAdfTests|FullyQualifiedName~AsperetaCompiledEncTests"
```

Expected: interval/type/state assertions fail against discarded timing and direct enum casting.

**Step 3: Implement minimal decoding**

Add nullable source interval to `Animation`, assign `DecodeByte(reader.ReadByte(), offset)` in `AsperetaAdf.Load`, and replace raw type casting with an explicit switch. Keep Illutia construction unchanged so null means “no encoded timing.”

Do not add new comments unless an external wire-format constraint is not evident from the code.

**Step 4: Run focused tests**

Expected: all focused tests pass.

**Invariant-to-test matrix:**

| Invariant | Proved by |
|---|---|
| Aspereta timing survives parsing | interval parser test |
| Hand cannot become Eyes | raw type 3 regression test |
| Every schema slot is addressable | all-facing/all-state accessor theory |
| Illutia timing behavior is unchanged | existing SpriteFrames/Illutia tests |

**Step 5: Commit**

```bash
git add tools/AssetConverter/src/AssetConverter/Adf tools/AssetConverter/src/AssetConverter/Aspereta/AsperetaCompiledEnc.cs tools/AssetConverter/tests/AssetConverter.Tests
git commit -m "feat: preserve complete Aspereta animation metadata"
```

---

### Task 2: Add the shared deterministic animation catalog

**Files:**
- Create: `tools/AssetConverter/src/AssetConverter/Aspereta/AsperetaAnimationCatalog.cs`
- Create: `tools/AssetConverter/tests/AssetConverter.Tests/AsperetaAnimationCatalogTests.cs`
- Reuse: `tools/AssetConverter/src/AssetConverter/Aspereta/AsperetaSheets.cs:9-30`

**Helper contract:**

`AsperetaAnimationCatalog.Load(dataDir, compiledEncPath)` owns parsing and resolution only. It does not mutate `AdfFile.FileNumber`, write PNGs/resources/manifests, merge metadata, or suppress diagnostics. After return, all collections are deterministic and read-only; duplicate global ownership has already failed.

Expose enough immutable data for downstream consumers:

- loaded sheets;
- compiled entries;
- resolved definitions keyed by source animation ID;
- all nonzero compiled slots with type/resource/motion/state/facing/reference context and optional resolution;
- claimed IDs, including unresolved compiled references;
- unclaimed resolved definitions;
- unresolved compiled and standalone diagnostics.

A resolved frame carries both the assigned output sheet and `Frame`. A resolved animation carries source ID, FPS, direct-frame status, and ordered resolved frames.

**Mutation impact:**
- Source of truth changed: resolution moves from three independent dictionaries into one catalog.
- Important readers: character builder, effect converter, frame/animation manifests, CLI pipeline.
- Derived/cached state affected: claimed/unclaimed sets and diagnostics are computed at load and thereafter immutable.
- Required propagation sequence: numerically load sheets → reject duplicate ownership → resolve all definitions → claim every nonzero compiled ID → resolve definition first, frame fallback second → partition standalone definitions.
- Invariants to preserve: zero is never a slot; claimed unresolved IDs never become effects; source frame order and per-frame sheet ownership remain intact.
- Observable proof required: hermetic fixtures assert catalog projections, not implementation dictionary calls.

**Step 1: Write failing catalog tests**

Cover:

- numeric sheet ranking independent of file creation order;
- malformed/sound/empty handling;
- all seven types and all state/motion slots;
- zero-slot omission;
- interval 3 → 6 FPS and interval 5 → 10 FPS;
- direct-frame fallback → one frame at 8 FPS;
- cross-sheet ordered frames;
- unresolved compiled context includes every identifying field;
- unresolved standalone diagnostic;
- duplicate frame IDs and duplicate animation IDs fail and name both source files;
- all nonzero IDs are claimed even when unresolved;
- repeated/reversed fixture creation yields equivalent ordered projections.

**Step 2: Run red**

```bash
dotnet test tools/AssetConverter/AssetConverter.sln --filter FullyQualifiedName~AsperetaAnimationCatalogTests
```

Expected: compile failure because the catalog does not exist.

**Step 3: Implement the catalog**

Use numeric ADF ordering, not `Directory.EnumerateFiles` order. Prefer animation definitions over direct-frame fallback when a reference ID resolves in both namespaces. Preserve the existing rank-based sheet numbering from `AsperetaSheets` without mutating loaded ADF objects.

Duplicate ownership is fatal. Missing references are diagnostics. Do not special-case known ID 1 in production code.

**Step 4: Run green and all hermetic manifest fixtures**

```bash
dotnet test tools/AssetConverter/AssetConverter.sln --filter "FullyQualifiedName~AsperetaAnimationCatalogTests|FullyQualifiedName~AnimationManifestBuilderTests"
```

The catalog tests pass; pre-migration manifest tests remain green.

**Invariant-to-test matrix:**

| Invariant | Proved by |
|---|---|
| No dictionary overwrite can choose ownership silently | duplicate frame/animation adversarial tests |
| Claimed unresolved IDs cannot leak into effects | unresolved claimed-ID test |
| Direct frames are not fabricated multi-frame definitions | singleton test |
| File-system ordering cannot affect output | reversed creation-order test |

**Step 5: Commit**

```bash
git add tools/AssetConverter/src/AssetConverter/Aspereta/AsperetaAnimationCatalog.cs tools/AssetConverter/tests/AssetConverter.Tests/AsperetaAnimationCatalogTests.cs
git commit -m "feat: catalog all Aspereta animations"
```

---

### Task 3: Build resources for every compiled entry

**Files:**
- Create: `tools/AssetConverter/src/AssetConverter/Aspereta/AsperetaAnimationResourceBuilder.cs`
- Create: `tools/AssetConverter/tests/AssetConverter.Tests/AsperetaAnimationResourceBuilderTests.cs`
- Remove after migration: `tools/AssetConverter/src/AssetConverter/Aspereta/AsperetaMonsterConverter.cs`
- Modify/replace: `tools/AssetConverter/tests/AssetConverter.Tests/AsperetaMonsterConverterTests.cs`
- Reuse: `tools/AssetConverter/src/AssetConverter/SpriteFrames/AnimationMetadata.cs`
- Reuse: `tools/AssetConverter/src/AssetConverter/SpriteFrames/AnimationNaming.cs:20-112`
- Reuse: `tools/AssetConverter/src/AssetConverter/SpriteFrames/SpriteFramesWriter.cs:52-153`

**Helper contract:**

`AsperetaAnimationResourceBuilder.Build(catalog)` returns resource specifications and diagnostics. It does not write files or mutate the catalog. Every resolvable slot becomes one source clip; every derived idle or alias is backed by an existing resolved walk clip and retains its frames/FPS.

**Mutation impact:**
- Source of truth changed: generated Aspereta resources expand from 67 monster bodies/state 1 to all 243 compiled entries/all populated states.
- Important readers: CLI resource writer, metadata merger, client resource loader by category/ID.
- Derived/cached state affected: first-frame and height metadata in each resource result.
- Required propagation sequence: group slots by compiled entry → convert facing → emit source clips → derive idles → derive state-1 compatibility aliases → derive generic equipped aliases per direction → compute metadata.
- Invariants to preserve: output ID is always `10000 + source ID`; unresolved slots do not discard sibling clips; aliases never come from zero/unresolved slots.
- Observable proof required: tests inspect both resource models and serialized `.tres`.

**Step 1: Write failing builder tests**

Assert:

- all seven categories use the existing folder mapping and offset IDs;
- facing mapping remains Left←source Left(3), Down←Down(2), Right←Right(1), Up←Up(0);
- states map to approved names;
- each walk source creates matching idle from frame 0;
- source FPS survives source clips, idles, and aliases;
- `walk-equip`/`idle-equip` preference is state 4, then 3, then 2 per direction;
- state-1 `walk-*`, `attack-*`, and `idle-*` compatibility aliases remain;
- zero slots create nothing;
- one unresolved slot leaves all sibling clips/resources present;
- direct-frame slots emit one frame at 8 FPS;
- a multi-sheet animation serializes distinct texture resources;
- first-frame/height keys use output type and offset ID;
- ordering and serialized output are deterministic.

**Step 2: Run red**

```bash
dotnet test tools/AssetConverter/AssetConverter.sln --filter FullyQualifiedName~AsperetaAnimationResourceBuilderTests
```

**Step 3: Implement direct resource construction**

Build `SpriteFrameSpec` from each catalog frame’s sheet; do not adapt Aspereta into `CompiledAnimation.AnimationFiles`, which cannot represent mixed sheets. Copy `Speed` explicitly when constructing aliases—the positional alias constructor otherwise defaults to 8 FPS.

Resources with no resolved clips are reported and omitted because `SpriteFramesWriter.Build` rejects an empty animation list.

**Step 4: Run green**

Run the builder and SpriteFrames writer tests. Replace obsolete monster-only assertions with a real-data integration test that computes completeness from `compiled.enc` rather than pinning only 67 monsters.

**Invariant-to-test matrix:**

| Invariant | Proved by |
|---|---|
| Every populated resolvable state remains represented | all-state clip-name test |
| Missing source data does not erase valid siblings | one-unresolved-slot regression test |
| Mixed sheets remain renderable | serialized ext-resource test |
| Timing cannot reset through aliases | non-8-FPS alias/idle test |

**Step 5: Commit**

```bash
git add tools/AssetConverter/src/AssetConverter/Aspereta tools/AssetConverter/tests/AssetConverter.Tests
git commit -m "feat: build every Aspereta compiled animation"
```

---

### Task 4: Emit every unclaimed definition as an effect

**Files:**
- Modify: `tools/AssetConverter/src/AssetConverter/Aspereta/AsperetaEffectsConverter.cs:1-199`
- Modify: `tools/AssetConverter/tests/AssetConverter.Tests/AsperetaEffectsConverterTests.cs`

**Mutation impact:**
- Source of truth changed: effect eligibility becomes “resolved and not claimed by compiled data,” replacing hardcoded ranges.
- Important readers: `Program.cs`, effect `.tres` loader, animation-height metadata.
- Derived/cached state affected: effect count and height metadata gain 55xxx/95xxx/96xxx/97xxx outputs.
- Required propagation sequence: consume catalog standalone set → offset resource and clip ID → preserve ordered frame sheets/FPS → write `.tres` → merge height metadata.
- Invariants to preserve: claimed IDs never become effects; unresolved standalone definitions are diagnostics, not empty files.
- Observable proof required: tests inspect emitted files, speed, textures, and metadata.

**Step 1: Replace range tests with failing eligibility tests**

Use fixtures for representative source IDs 55228, 95170, 96230, and 97090 as well as old emote/spell ranges. Assert all unclaimed IDs emit `Effects/{700000 + id}/animations.tres`, while a compiled-claimed ID does not.

Add non-8-FPS and mixed-sheet assertions. Add an unresolved standalone definition and assert it is reported without a file.

**Step 2: Run red**

```bash
dotnet test tools/AssetConverter/AssetConverter.sln --filter FullyQualifiedName~AsperetaEffectsConverterTests
```

Expected: former out-of-range IDs are skipped and timing remains 8.

**Step 3: Convert from the catalog**

Change the converter API to accept `AsperetaAnimationCatalog` and output root. Remove `IsEffectId`, range constants, and `SkippedOutOfRange`. Iterate standalone definitions by source ID and build per-frame specs directly.

**Step 4: Run green**

Assert deterministic repeated output and exact metadata keys.

**Invariant-to-test matrix:**

| Invariant | Proved by |
|---|---|
| Eligibility has no residual range filter | four former out-of-range tests |
| Compiled ownership wins | claimed-definition adversarial test |
| Effects retain source timing | interval 10 → 20 FPS `.tres` test |

**Step 5: Commit**

```bash
git add tools/AssetConverter/src/AssetConverter/Aspereta/AsperetaEffectsConverter.cs tools/AssetConverter/tests/AssetConverter.Tests/AsperetaEffectsConverterTests.cs
git commit -m "feat: emit all standalone Aspereta animations"
```

---

### Task 5: Drive combined manifests from the catalog

**Files:**
- Modify: `tools/AssetConverter/src/AssetConverter/Manifest/AnimationManifestBuilder.cs:35-304`
- Modify: `tools/AssetConverter/src/AssetConverter/Manifest/FrameManifestBuilder.cs:20-43`
- Modify: `tools/AssetConverter/tests/AssetConverter.Tests/AnimationManifestBuilderTests.cs:484-818`
- Modify as needed: `tools/AssetConverter/tests/AssetConverter.Tests/FrameManifestBuilderTests.cs`

**Helper contract:**

Add catalog-accepting combined-manifest overloads. They serialize catalog state but do not load Aspereta data again or mutate the catalog. Existing public signatures may delegate by loading a catalog for compatibility, but the CLI must pass its already-loaded instance.

**Mutation impact:**
- Source of truth changed: Aspereta manifest membership, timing, and categories come from the shared catalog.
- Important readers: map-editor `GraphicAnimationManifest`, graphic viewer playback, appearance-manifest generation.
- Derived/cached state affected: JSON sheet categories and animation lists.
- Required propagation sequence: catalog resolved references → normalize frame IDs → assign owner sheet from first frame → attach categories to every reached sheet → deterministic sort → serialize.
- Invariants to preserve: Illutia remains 8 FPS; every manifest frame resolves in frame manifest; raw Aspereta frame IDs become `700000 + frameId`.
- Observable proof required: parse JSON and cross-check against catalog/frame manifest.

**Step 1: Add failing combined-manifest tests**

Cover:

- all compiled types/resources and all populated states, not only monster state 1;
- definition-backed source FPS and direct-frame 8 FPS;
- every unclaimed definition including former out-of-range IDs;
- claimed unresolved IDs do not reappear as effects;
- cross-sheet frame order;
- categories include `{AnimationType, 10000 + source resource ID}` for all reached sheets;
- unclaimed sheets retain `Spells`; untouched graphic sheets retain `Tiles`;
- every emitted frame resolves in combined frame manifest;
- repeated/reversed fixtures serialize byte-identically;
- Illutia entries stay at 8 FPS.

**Step 2: Run red**

```bash
dotnet test tools/AssetConverter/AssetConverter.sln --filter "FullyQualifiedName~AnimationManifestBuilderTests|FullyQualifiedName~FrameManifestBuilderTests"
```

**Step 3: Replace independent Aspereta resolution**

Delete the private Aspereta frame/animation reconstruction and effect-range checks. Serialize distinct resolved compiled references plus standalone definitions. If one source reference appears in multiple resources, emit one manifest animation record per existing manifest identity rule, while categories retain every owning resource.

**Step 4: Run green and downstream manifest tests**

```bash
dotnet test tools/AssetConverter/AssetConverter.sln --filter "FullyQualifiedName~AnimationManifestBuilderTests|FullyQualifiedName~FrameManifestBuilderTests|FullyQualifiedName~GraphicAnimationManifestTests|FullyQualifiedName~GraphicAssetCatalogTests"
```

Run map-editor rendering tests from the root separately if their project is not part of the converter solution.

**Invariant-to-test matrix:**

| Invariant | Proved by |
|---|---|
| Manifest and runtime resources use the same resolved data | catalog completeness comparison |
| No dangling graphic references | combined referential-integrity test |
| Viewer speed matches source | 6/10/20 FPS JSON tests |
| Illutia output does not change speed | Illutia 8 FPS regression test |

**Step 5: Commit**

```bash
git add tools/AssetConverter/src/AssetConverter/Manifest tools/AssetConverter/tests/AssetConverter.Tests
git commit -m "feat: manifest every Aspereta animation"
```

---

### Task 6: Unify CLI conversion and regenerate committed outputs

**Files:**
- Create: `tools/AssetConverter/src/AssetConverter/Aspereta/AsperetaConversionPipeline.cs`
- Create: `tools/AssetConverter/tests/AssetConverter.Tests/AsperetaConversionPipelineTests.cs`
- Modify: `tools/AssetConverter/src/AssetConverter/Program.cs:85-108,179-299`
- Modify as needed: `tools/AssetConverter/src/AssetConverter/SpriteFrames/AnimationBatchConverter.cs`
- Generate and force-add selected files under:
  - `Assets/Sprites/{category}/{10000 + compiled source ID}/animations.tres` for catalog compiled entries
  - `Assets/Sprites/Effects/{700000 + standalone source ID}/animations.tres` for catalog standalone definitions
  - `Assets/Sprites/manifest.json`
  - `Assets/Sprites/animation-manifest.json`
  - `Assets/Sprites/appearance-manifest.json`

**Helper contract:**

`AsperetaConversionPipeline.Convert(dataDir, compiledPath, outRoot)` loads one catalog, builds/writes compiled resources, writes standalone effects, merges metadata, and returns catalog/results/diagnostics/counts. It does not convert maps or Illutia sheets; command orchestration retains those responsibilities.

**Mutation impact:**
- Source of truth changed: `all` and `aspereta` invoke one complete Aspereta animation phase instead of asymmetric monster/effect phases.
- Important readers: generated resource files, metadata stores, three manifests, CLI diagnostics.
- Derived/cached state affected: output files only; writes are deterministic replacements.
- Required propagation sequence: load catalog once → build resources → write nonempty resources → write effects → merge metadata once → build manifests from same catalog → print diagnostics.
- Invariants to preserve: `aspereta` must not replace combined metadata/appearance data with an Aspereta-only subset; partial slot diagnostics do not cause malformed empty resources.
- Observable proof required: run both command paths in temporary roots and compare Aspereta output bytes.

**Step 1: Write failing pipeline tests**

Test a hermetic pipeline root for complete resources/effects/manifests, propagated diagnostics, deterministic repeated conversion, and matching Aspereta outputs from the orchestration used by both commands.

**Step 2: Run red**

```bash
dotnet test tools/AssetConverter/AssetConverter.sln --filter FullyQualifiedName~AsperetaConversionPipelineTests
```

**Step 3: Implement and wire both commands**

Remove the monster-specific write loop and old range counters. Both `all` and `aspereta` load one catalog and write all compiled resources/effects. Keep `all` authoritative for complete Illutia+Aspereta metadata. For `aspereta`, load Illutia resource models when rebuilding appearance metadata rather than publishing an Aspereta-only file.

Retain or update `aspereta-body` only as a thin selector over the all-entry builder; it must not call the removed monster converter.

**Step 4: Run hermetic and baseline suites**

```bash
dotnet test tools/AssetConverter/AssetConverter.sln --filter "FullyQualifiedName~Aspereta|FullyQualifiedName~AnimationManifestBuilderTests"
```

Then run the full converter suite with verified local paths. Expected: all new tests pass; only the four documented Illutia dataset failures may remain.

**Step 5: Generate twice and verify determinism**

From the client worktree:

```bash
export ASPERETA_DATA=/home/agent/workspace/gooseclient/AsperetaClient/bin/Release/net8/data
export ASPERETA_MAPS=/home/agent/workspace/gooseclient/AsperetaClient/bin/Release/net8/maps
export ILLUTIA_DATA=/home/agent/workspace/Illutia/data
export ILLUTIA_MAPS=/home/agent/workspace/Illutia/maps
dotnet run --project tools/AssetConverter/src/AssetConverter -- all "$PWD"
```

Copy hashes for selected generated outputs, run the same command again, and assert hashes are unchanged. Also run `aspereta` into a temporary root with the required mapping file and compare its Aspereta `.tres`/manifest bytes.

Inspect:

- exactly four unresolved ID 1 diagnostics for Hair 70–73 state-2 walking;
- no zero-frame or zero-speed clips;
- standalone outputs include 55228, 95170, 96230, and 97090 after the 700000 offset;
- all referenced `20xxx` sheets exist in generated output;
- no server-remapper file changed.

Generated assets remain globally ignored by repository policy. Force-add only the approved imported resources and manifests using explicit catalog-derived path lists; do not force-add PNGs, maps, metadata, `.godot` imports, or unrelated Illutia resources.

**Invariant-to-test matrix:**

| Invariant | Proved by |
|---|---|
| Both CLI paths use complete conversion | temporary-root pipeline/CLI tests |
| One catalog drives every output | diagnostics/count cross-check |
| Generated output is reproducible | two-run hash comparison |
| Ignored unrelated assets are not committed | explicit `git status --short` review |

**Step 6: Commit**

```bash
git add tools/AssetConverter/src/AssetConverter tools/AssetConverter/tests/AssetConverter.Tests
git add -f <catalog-derived imported .tres paths> Assets/Sprites/manifest.json Assets/Sprites/animation-manifest.json Assets/Sprites/appearance-manifest.json
git commit -m "feat: import all Aspereta animation assets"
```

---

### Task 7: Update client clip selection and layered-body handling

**Files:**
- Create: `Scripts/Character/BodyClassification.cs`
- Create: `tests/Goose2Client.Tests/BodyClassificationTests.cs`
- Create: `tests/Goose2Client.Tests/CharacterPacketAppearanceTests.cs`
- Modify: `Scripts/Character/AnimationNames.cs:36-79`
- Modify: `Scripts/Network/Packets/MakeCharacterPacket.cs:51-92`
- Modify: `Scripts/Network/Packets/UpdateCharacterPacket.cs:31-71`
- Modify: `Scripts/Character/Character.cs:311-349`
- Modify: `tests/Goose2Client.Tests/AnimationNamesTests.cs:29-100`

**Helper contract:**

`BodyClassification.IsLayered(bodyId)` is pure and returns true exactly for `bodyId < 100` or `10000 <= bodyId <= 10099`. It does not inspect character type, packet type, resource existence, or server data.

**Mutation impact:**
- Source of truth changed: body ID classification and idle/walk candidate ordering.
- Important readers: MKC/CHP parsers, `Character.ApplyAppearance`, runtime `ResolveClip`, custom preview.
- Derived/cached state affected: parsed packet shape and `_appearance`/slot nodes after `ApplyAppearance`.
- Required propagation sequence: parse layered fields using classifier → retain them in packet DTO → `Character.ApplyAppearance` uses same classifier → candidate list prefers state-specific clip → `ResolveClip` falls back for old resources.
- Invariants to preserve: packet field order is unchanged; 10100 remains compact; existing assets without new clips select `*-equip` exactly as before.
- Observable proof required: parse distinctive full packets and assert trailing alignment; inspect exact candidate lists.

**Step 1: Write failing classifier, packet, and candidate tests**

Boundary matrix: 99 true, 100 false, 9999 false, 10000 true, 10001 true, 10099 true, 10100 false.

Build full MKC and CHP strings for body 10001 with distinctive hair, six equipment slots, colors, invisibility, face, speed, GM, and mount values. Keep shield or weapon nonzero so the existing parser correction does not reset BodyState. Assert all trailing values, not merely successful parsing. Retain body 150/10100 compact alignment tests.

Expected candidate bases:

- state 4: `idle/walk-1hand`, `idle/walk-equip`, generic, no-equip;
- state 5: `idle/walk-staff`, `idle/walk-equip`, generic, no-equip;
- states 6/7: current generic equipped ordering;
- state 3: current unarmed ordering;
- attack lists unchanged.

**Step 2: Run red**

```bash
dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj --filter "FullyQualifiedName~BodyClassificationTests|FullyQualifiedName~CharacterPacketAppearanceTests|FullyQualifiedName~AnimationNamesTests"
```

**Step 3: Implement classifier and consumers**

Replace packet thresholds and the rendering strip condition with the helper. Update only idle/walk candidate construction; leave `AttackVariant`, cast, mounted, and blank-on-missing behavior unchanged.

**Step 4: Run focused and full client suites**

```bash
dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj
dotnet test Goose2ClientGodot.sln
```

**Invariant-to-test matrix:**

| Invariant | Proved by |
|---|---|
| Client consumes exactly the server’s selected packet shape | full MKC/CHP 10001 and 10100 tests |
| Parsed layers are not immediately stripped | `ApplyAppearance`/slot regression test or existing character harness extended for 10001 |
| Existing Goose2 resources still resolve | exact fallback-list tests |
| Attack behavior is unchanged | existing attack-list assertions |

**Step 5: Commit**

```bash
git add Scripts/Character Scripts/Network/Packets tests/Goose2Client.Tests
git commit -m "feat: support layered imported Aspereta bodies"
```

---

### Task 8: Apply matching body classification to server packet producers

Work in `/home/agent/workspace/illutiagooseserver/.worktrees/import-all-aspereta-animations`.

**Files:**
- Create: `Goose/BodyClassification.cs`
- Create: `Goose.Tests/BodyClassificationTests.cs`
- Create: `Goose.Tests/CharacterAppearancePacketTests.cs`
- Modify: `Goose/Packets.cs:111-278`
- Modify: `Goose/Commands/GmHaxCommand.cs:15-41`
- Modify: `Goose/Data/Illutia/Scripts/NPC/BodyStealerNPC.csx:27-54`
- Modify: `Goose.Tests/Part3GmAdminTests.cs:337-347`
- Reuse fixture patterns: `Goose.Tests/InvisibilityPacketTests.cs:22-139`

**Helper contract:**

Server `BodyClassification.IsLayered` uses the exact same pure ID rule as the client. It is public so current-protocol scripts can use it. It does not modify body IDs, remap data, or decide animation clips.

**Mutation impact:**
- Source of truth changed: server current-protocol packet producers classify 10000–10099 as layered.
- Important readers: Godot MKC/CHP parsers and any current-protocol packet consumer.
- Derived/cached state affected: none; packet strings are built on demand.
- Required propagation sequence: resolve player weapon pose as today → classify current body → include actual pose/layer fields or force compact pose 3/omit fields → send unchanged prefix/order.
- Invariants to preserve: invisibility, speed, GM/name-color, commas, and trailing mount positions stay aligned; pets still dispatch through pet delegates; native Aspereta override remains untouched.
- Observable proof required: exact/tokenized packet assertions for all six `P` delegates plus `/gmhax`.

**Step 1: Write failing boundary and packet-generation tests**

Use real `Player`, `NPC`, `Pet`, and `Inventory` setup following `InvisibilityPacketTests`. For body 10001 assert actual pose, hair/equipment/colors/face/mount, invisibility, speed, and MKC GM/name-color positions. For 10100 assert compact pose 3 and omission of layered fields while always-present fields remain aligned.

Cover:

- `P.MakeCharacter`
- `P.UpdateCharacter`
- `P.MakeNPCCharacter`
- `P.UpdateNPC`
- `P.MakePetCharacter`
- `P.UpdatePet`
- `/gmhax` current-protocol CHP path

**Step 2: Run red**

```bash
dotnet test Goose.Tests/Goose.Tests.csproj --filter "FullyQualifiedName~BodyClassificationTests|FullyQualifiedName~CharacterAppearancePacketTests|FullyQualifiedName~Part3GmAdminTests"
```

Expected: body 10001 receives compact output under the old threshold.

**Step 3: Implement and replace current-protocol thresholds**

Compute classification once per packet delegate/command and use it for every conditional field. Update `BodyStealerNPC.csx` to classify `transPlayer.CurrentBodyID`. Do not touch `tools/server-remap` or `Goose/Data/Aspereta/Scripts/Global/Aspereta.csx`.

**Step 4: Run focused and full server suites**

```bash
dotnet test Goose.Tests/Goose.Tests.csproj
dotnet test Goose.IntegrationTests/Goose.IntegrationTests.csproj
dotnet test Goose.sln
```

Audit:

```bash
git grep -n 'CurrentBodyID >= 100' -- Goose/Packets.cs Goose/Commands Goose/Data/Illutia
```

Expected: no old threshold remains in current-protocol producers.

**Invariant-to-test matrix:**

| Invariant | Proved by |
|---|---|
| Server shape agrees with client shape at both boundaries | 10001/10100 packet theories |
| All entity producers agree | six-delegate theory/helper |
| Weapon pose resolution remains authoritative for players | equipped-weapon pose assertion |
| Remapper/native protocol are unchanged | scoped git diff and grep audit |

**Step 5: Commit in the server repository**

```bash
git add Goose/BodyClassification.cs Goose/Packets.cs Goose/Commands/GmHaxCommand.cs Goose/Data/Illutia/Scripts/NPC/BodyStealerNPC.csx Goose.Tests
git commit -m "feat: support layered imported body IDs"
```

---

## Final verification

From the client worktree:

```bash
dotnet test Goose2ClientGodot.sln
ASPERETA_DATA=/home/agent/workspace/gooseclient/AsperetaClient/bin/Release/net8/data \
ASPERETA_MAPS=/home/agent/workspace/gooseclient/AsperetaClient/bin/Release/net8/maps \
ILLUTIA_DATA=/home/agent/workspace/Illutia/data \
ILLUTIA_MAPS=/home/agent/workspace/Illutia/maps \
dotnet test tools/AssetConverter/AssetConverter.sln
```

Expected: client passes; all new converter/Aspereta tests pass; only the four documented local Illutia dataset baseline failures may remain.

From the server worktree:

```bash
dotnet test Goose.sln
```

Expected: pass with the existing intentional skipped asset-bundle tests.

Review both repositories:

```bash
git status --short
git diff --check
git log --oneline --decorate -10
```

Confirm:

- no server-remapper changes;
- no native Aspereta protocol changes;
- no generated PNGs/maps/metadata or `.godot` files staged;
- every committed `.tres` is catalog-derived and nonempty;
- manifests refer only to generated sheets/frames;
- source intervals produce 6, 8, 10, and 20 FPS where present;
- the four known Hair ID 1 references are diagnostics, not fabricated clips.
