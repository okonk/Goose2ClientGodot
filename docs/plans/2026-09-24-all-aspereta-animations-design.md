# All Aspereta Animations Design

## Goal

Import every resolvable Aspereta animation into Goose2 instead of converting only monster walk/attack and selected effect ranges. Preserve source timing, expose all compiled character and equipment resources, and keep imported layered bodies compatible with the existing client/server packet format.

The server remapper remains unchanged.

## Shared animation catalog

Add a shared Aspereta animation catalog to the asset converter. It loads all valid graphic ADF sheets and `compiled.enc`, then builds deterministic global indexes for frames and animation definitions.

The catalog:

- Resolves animation frames across sheets.
- Rejects duplicate global frame or animation IDs as ambiguous.
- Preserves each animation's encoded interval and derives FPS as `interval * 2`.
- Resolves a compiled slot that references a frame directly as a one-frame animation at 8 FPS.
- Records every nonzero compiled slot and does not create clips for zero slots.
- Tracks animation IDs claimed by compiled character or equipment entries.
- Reports unresolved references with type, resource ID, motion, state, facing, and referenced ID.

An unresolved compiled slot skips only that clip. An unresolved standalone animation is reported and skipped. The known source-data exception is frame/animation ID `1`, referenced by four state-2 walking slots for Hair IDs 70–73.

Malformed, sound, and empty ADF files retain their current handling.

## Outputs

Character resources are generated for every `compiled.enc` entry using `10000 + source ID` in the corresponding existing category:

- Body
- Hair
- Hand
- Chest
- Helm
- Legs
- Feet

The Aspereta on-disk type mapping is decoded explicitly so Hand does not pass through Illutia's Eyes enum position.

Every resolvable ADF animation not claimed by `compiled.enc` is emitted as `Assets/Sprites/Effects/{700000 + animation ID}/animations.tres`. This includes definitions outside the old emote and spell ranges, including the `55xxx`, `95xxx`, `96xxx`, and `97xxx` ranges.

The combined animation manifest includes all resolved compiled and standalone animations and their source FPS. Both the `all` and `aspereta` commands run complete Aspereta conversion. Generated `.tres` resources and manifests are committed.

Resource building supports animation frames on different PNG sheets rather than requiring one sheet per animation order.

## State and clip mapping

Directions retain the existing Aspereta-to-Goose2 facing conversion.

| Aspereta state | Walk clip | Attack clip |
|---|---|---|
| 1 | `walk-no-equip` | `attack-no-equip` |
| 2 | `walk-asp-state-2` | `attack-asp-state-2` |
| 3 | `walk-staff` | `attack-staff` |
| 4 | `walk-1hand` | `attack-1hand` |

State 2 uses explicit source-specific names because its protocol meaning is unclear. Its populated animations are preserved but are not selected by normal Goose2 body states.

Each populated walk clip receives an idle clip derived from its first frame at the same FPS. Imported resources receive `walk-equip` and `idle-equip` aliases from the first populated equipped variant, preferring state 4, then state 3, then state 2. Existing generic aliases remain where applicable. Zero slots produce no clips or aliases.

Existing Illutia resources remain unchanged at the converter's established 8 FPS convention. Aspereta aliases and idle clips retain their source animation's FPS. Existing looping behavior remains unchanged because ADF does not encode loop semantics.

## Client animation selection

For idle and walking, the client selects state-specific clips before existing generic fallbacks:

- BodyState 4 prefers `idle-1hand` and `walk-1hand`.
- BodyState 5 prefers `idle-staff` and `walk-staff`.
- Other equipped states retain generic `idle-equip` and `walk-equip` behavior.

Existing attack selection remains compatible. Existing Goose2 resources do not contain the new names and therefore continue through the current generic fallbacks without behavioral changes.

## Layered body classification

No packet fields or packet ordering change. Client and server use the same body classification rule:

- IDs below 100 use layered appearance.
- IDs from 10000 through 10099 use layered appearance.
- Other IDs at or above 100 use monster/illusion appearance.

The server uses this classification when deciding whether character packets contain hair, face, equipment, colors, mount data, and the actual body state. The client uses it when parsing those packets. Imported player bodies such as 10001 therefore consume layered fields, while imported monsters 10100 and above retain the compact monster packet shape.

The server remapper and its existing asset decisions are not modified.

## Diagnostics and testing

Hermetic converter tests cover:

- Every Aspereta source type and state.
- Explicit Hand decoding.
- Zero slots.
- Direct-frame singleton slots.
- Cross-sheet resolution.
- Unresolved-slot diagnostics.
- Duplicate global ID rejection.
- Source FPS in resources, aliases, idles, effects, and manifests.
- All unclaimed definitions becoming effects.
- Complete manifest inclusion.
- ID offsets and deterministic output.

Client tests cover state-specific clip preference, generic fallback, and layered-body packet parsing. Server tests cover packet generation for layered body 10001 and monster body 10100. Existing Illutia behavior remains covered by the current suites.

## Baseline test status

At design time, the client and server suites pass. With local source-data paths configured, 130 of 134 asset-converter tests pass. Four existing dataset-pinned Illutia tests fail because the local Illutia data differs from their recorded expectations: compiled record count, two payload fixtures, and one frame offset. These failures are unrelated to this work and are retained as the comparison baseline.
