# Aspereta equipment/body mapping — review outcomes & remap rules

Source of truth: `tools/AssetConverter/data/aspereta-item-mapping.tsv`
(reviewed by Hayden in the asp-ill-matcher tool, 2026-07-24).
Columns: `asp_type, asp_id, decision, ill_type, ill_id, source, dye`.

## Stats
- 170 `match` (accepted), 58 `inject`, 15 `match-unreviewed`.

## Rules for the server-data remap plan

1. **`match-unreviewed` = skip.** The 15 pending rows are all Hair 6–15 / 51–55 →
   Eyes:16 — known shape-pass false positives. Aspereta-only hairs are believed
   unused; treat them as unmapped (fall back to a default hair if a character
   references one, do not use Eyes:16).

2. **Cross-slot matches change the item's slot.** Two chest items exist in
   Illutia as helms — the server item must move slots when remapped:
   - `Chest:27 -> Helm:60` (Illutia turned it into a helm; confirmed by Hayden)
   - `Chest:19 -> Helm:11` (same art as Aspereta Helm:15; Illutia only has the helm)

3. **Hair→Eyes matches are a slot change too.** Aspereta "Hair" 70–73 are eye
   sprites (Aspereta has no Eyes type); they map to Illutia Eyes 1/1/3/2 and
   must be remapped into the face/eye field, not hair.

4. **Dye column: only apply when the Aspereta item has NO color of its own.**
   If the Aspereta server item already sets a graphic color, keep it; the TSV
   dye (e.g. `Feet:1 -> Feet:2 #b5835a`) is the fallback tint that makes the
   Illutia base art look like the Aspereta original. 15 rows carry a dye.

5. **Dangling Aspereta bodies 150–152, 154–157** reference animation IDs
   (120xxx/150xxx) whose .adf files never shipped in any known client —
   invisible even in the original client. Marked `inject`, believed unused;
   verify against the Goose DB before spending effort.

## Server-data remap: source workbooks (Downloads/)
- Base (items, NPCs, spells, spawns, drops, vendors, maps, combos, warptiles):
  `Aspereta Goose Data.xlsx`
- Overlay — copy these sheets verbatim from `Xendria Aspereta Goose Data.xlsx`
  (the live Xendria server data): **Quests, Quest Reqs, Quest Rewards, Titles,
  Surnames, Classes, Class Info, Class Levelup Spells**.
  Validate that item/NPC/spell ids referenced by quest reqs/rewards and class
  levelup spells exist in the base sheets (ids are unchanged by the remap).
- Graphics target ids come from `Illutia Goose Data.xlsx` + the mapping tables.
- **NPC merge for quests** (so Xendria quests work):
  - Add the 135 Xendria-only NPCs (ids 183–317) and their 3,293 spawn rows.
  - Add the Xendria-only Maps-sheet rows referenced by those spawns
    (ids 47, 52, 60, 61, 69, 75–79, 83, 87, 132, 160) — all are aliases of
    existing map files (Map2/3/8/16/17/25/31–36/40/44.map), instanced/level-
    gated copies; their `filename` gets the same Map→10000+N remap as the rest.
  - Copy the `quest ids` column from Xendria onto shared NPCs (affects 98, 99).
  - Otherwise base Aspereta wins for the 182 shared NPCs. Xendria has a few
    substantive edits (npc 84 rename 'Bill Nye the Dye Guy', npc 79 respawn
    28800→14400, npc 177 hp 70M→120M, npc 180 exp 300000) — NOT taken; revisit
    if Xendria balance is preferred.

## Spells treatment (agreed 2026-07-24)
- Spellbook icons + buff graphics: per-frame tile mapping (matched → Illutia
  tile, else injected 700000+ / sheet 20000+rank).
- Spell effect animations (animation/cast/attack): always inject — remap
  `animation file` to 20000+rank, keep animation id. Hand-upgrade later if wanted.
- Prerequisite for the asset pipeline: generate effect SpriteFrames for injected
  Aspereta sheets referenced by Spell Effects (AnimationBatchConverter currently
  does includeEffects only for Illutia files).
- Item tile lookup rule: matched `equip display` → copy `graphic tile`/`graphic
  file` from an Illutia item with that display (prefer same slot); fall back to
  tile mapping table if no Illutia item uses it.

## Companion table (separate, still to generate in the pipeline plan)
`aspereta-mapping.tsv` — per-graphic tile/icon/spell mapping for map + item
graphic conversion (Task 2 of `2026-07-20-aspereta-asset-pipeline.md`).
