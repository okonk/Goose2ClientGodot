# UI Window Overhaul — Part 2: Per-Window Faithful Re-Layout + Live Tuning

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.
> **Depends on Part 1** (`2026-06-07-ui-windows-part1-foundations.md`) being merged: imported sprites/font, `GameTheme.tres`, `UnityRect`, `DefaultWindowLayout`, refactored `BaseWindow`, 32px slots, visibility persistence.

**Goal:** Re-lay-out each HUD window in Godot to match its Unity prefab pixel-for-pixel: fixed root size = the window's PNG, the PNG as the background, and every widget repositioned via `UnityRect.ToGodot(...)` from the cited prefab coordinates — then a live tuning pass against a real screenshot.

**Architecture:** One task per window (or small cluster). Each follows the same recipe (below). Coordinates come from the Unity prefab; final pixel nudges happen in Task 10 against the running client, because UI-on-art alignment is inherently visual. The `CharacterWindow` is a full rebuild (its current layout is wrong); the others are background + size + child-reposition.

**Tech Stack:** Godot 4.6 / C#, xUnit. Unity reference (READ-ONLY): `/home/hayden/code/Goose2Client/Assets/Prefabs/UI/*.prefab`.

---

## The per-window recipe (apply in every task below)

For window `W` with PNG `art.png` of size `PW×PH`:
1. **Root:** set the scene root `Control` to fixed size `offset_right=PW, offset_bottom=PH` (remove FullRect anchors). Position comes from `DefaultWindowLayout`/saved settings at runtime — do not hardcode `offset_left/top` to `(100,100)`.
2. **Background:** `Background` `TextureRect` (or `NinePatchRect` for Sliced art) filling the root, `texture = res://Assets/UI/art.png`, `mouse_filter=2 (Ignore)`. For Sliced art (`hotbar`, `quest`, `info`, `xp-bar*`, `hotbar-slot-background`), use `NinePatchRect` with `patch_margin_left/top/right/bottom` = the verified `spriteBorder`.
3. **Drag region (`TitleBar`):** transparent `Control`, `mouse_filter=0`, covering the draggable strip (Unity TitleBar GameObject region; if the whole frame drags, span the full window minus interactive widgets).
4. **Close button:** where Unity has one, a `Button` with `icon = res://Assets/UI/exitbutton.png`, flat style, placed at the Unity close-button position.
5. **Children:** for each widget, read its prefab `anchoredPosition`, `sizeDelta`, `anchorMin/Max`, `pivot`, compute `UnityRect.ToGodot(PW,PH, anchorX,anchorY, pivotX,pivotY, ax,ay, w,h)` and set `offset_left/top/right/bottom` accordingly. Keep node **names** the `.cs` resolves (do not rename without editing the script).
6. **Build** → 0 errors; scene loads headless. Commit. Final visual nudge in Task 10.

> **Conversion reminder (from Part 1 `UnityRect`):** `left = anchorX·PW + ax − pivotX·w`; `top = PH − anchorY·PH − ay − (1−pivotY)·h`. Most widgets use pivot `(0.5,0.5)`.

Branch: `feat/ui-windows-part2` (off master after Part 1 merges).

---

## Task 0: Branch
```bash
git checkout master && git pull && git checkout -b feat/ui-windows-part2
git commit --allow-empty -m "chore(ui): start window overhaul part 2 (per-window relayout)"
```

---

## Task 1: CharacterWindow — full rebuild (`character.png`, 400×222)

The current `CharacterWindow.tscn` is the most broken (wrong size, full-rect grid overlapping bare value labels, stats overflow by ~84px, no stat name labels). Rebuild it faithfully.

**Unity source:** `Assets/Prefabs/UI/CharacterCanvas.prefab` (root `CharacterPanel` 400×222, bg `character.png`). Equipment: **14 slots, 7 cols × 2 rows**, 32×32 each, in a `Slots` container. Stats use a **two-column layout**: static label GameObjects (left) + value GameObjects (right). Info text: `NameText`, `LevelClassText`, `GuildText` near the top; `Experience` (labeled) + `ExperienceSold`.

**Files:**
- Rewrite: `Scenes/UI/CharacterWindow.tscn`
- Modify: `Scripts/UI/CharacterWindow.cs` (node paths only if names change — prefer keeping `Content/NameText`, `Content/StrengthText`, … `Content/SlotGrid` so `_Ready` (`:50-78`) is untouched)

**Steps:**
1. Root → 400×222; `Background` `TextureRect` = `character.png`; transparent `TitleBar` drag strip across the top; `CloseButton` with `exitbutton.png` at the Unity close position.
2. `Content/SlotGrid` `GridContainer` `columns=7`: position the grid so its 7×2 of 32px slots sits over the art's equipment cells. Read the `Slots` container origin + each slot `anchoredPosition` from the prefab; if Unity used a `GridLayoutGroup` with cell+spacing, replicate via `GridContainer` `theme_override_constants/h_separation`/`v_separation`; if slots are individually placed, switch `SlotGrid` to a plain `Control` and place each `ItemSlot` at its converted offset. **Verify which** by reading the prefab — keep `_Ready`'s `grid.AddChild(slot)` working (if switching to a `Control`, it still accepts `AddChild`; keep the node name `SlotGrid`).
3. Reposition the info labels using `UnityRect`. Verified example: `NameText` (anchor (0,1), pivot (0.5,0.5), ax=55.41, ay=−15.59, size 100.82×11.18) → **`offset_left=5, offset_top=10, offset_right≈105.8, offset_bottom≈21.2`**. Convert `LevelClassText`, `GuildText`, `ExperienceText` (+ its "Experience" static label), `ExperienceSoldText` likewise from the prefab.
4. Add the **static stat-name labels** (new `Label` nodes; not resolved by the script — give them any unique names) at the converted "labels column" positions with text: `Strength`, `Stamina`, `Intelligence`, `Dexterity`, `Armor`, `Fire`, `Water`, `Earth`, `Air`, `Spirit`. Place the **value** labels (`StrengthText`…`SpiritResistText`, `ACText`) — keep these names — at the converted "values column" positions, right-aligned (`horizontal_alignment=2`).
5. Build → 0 errors; confirm `_Ready` still resolves every `Content/...Text` and `Content/SlotGrid`. Commit: `feat(ui): rebuild CharacterWindow to match Unity (character.png, 7x2 grid, labeled stats)`.

---

## Task 2: VitalsWindow — skin + portrait node (`vitals-outline.png`, 183×55)

**Unity source:** `Assets/Prefabs/UI/VitalsCanvas.prefab` — root 183×55, bg `vitals-outline.png`. HP bar (133×17 @ anchoredPos (24,9)), MP bar (108×17 @ (11.5,−9)), Level (19×19 @ (−44.5,−18.5)), and the **VitalsCharacterDisplay portrait** (53×53 container @ (−64,0)) with a circular mask (`vitals-character-circle.png`) and 5 layer images (body/eyes/hair/chest/helmet, render order body→helmet).

**Files:**
- Rewrite: `Scenes/UI/VitalsWindow.tscn`
- Modify: `Scripts/UI/VitalsWindow.cs` (only if node names change — keep `HpBar`/`MpBar`/`HpText`/`MpText`/`LevelText`)
- Move + fix doc: `Scripts/UI/VitalsCharacterDisplay.cs` — correct its comment ("Vitals window static layered portrait", not "Character window idle-down").

**Steps:**
1. Root → 183×55; `Background` `TextureRect` = `vitals-outline.png`. This is always-on HUD: set its scene `offset_left/top` to a top-left screen position (Unity anchored it top-left; e.g. `(0,0)` or a small inset) — it is NOT a `BaseWindow`, so it keeps a scene position.
2. Reposition `HpBar`/`MpBar`/`LevelText` via `UnityRect`. Verified: HP bar → `offset_left=49, offset_top=10` (size 133×17); MP bar (anchoredPos (11.5,−9), 108×17) → `left=37.5, top=36.5`; Level (anchoredPos (−44.5,−18.5), 19×19) → `left=37, top=37`. (Tune in Task 10.) Use `vitals-hp-bar.png`/`vitals-mp-bar.png` as the bar `texture_progress`.
3. Add the **portrait** as a child node tree matching Unity: a 53×53 `Control` "Portrait" at converted (−64,0); a circular mask (Godot: a `TextureRect` of `vitals-character-circle.png` as a frame, plus the 5 layer `TextureRect`s body/eyes/hair/chest/helmet stacked, each 53×53, render order body(bottom)→helmet(top)). Attach the `VitalsCharacterDisplay` script to "Portrait". **Leave the layer textures empty** — the appearance-data *rendering* is Step 8 A1; this task only builds the node skeleton + slot.
4. Build → 0 errors; `VitalsWindow._Ready` resolves all nodes; scene loads headless. Commit: `feat(ui): skin VitalsWindow + add portrait node skeleton (A1 slot)`.

> Cross-link: Step 8 Part 1 Task 8 (A1 portrait) now renders into this node tree instead of building it.

---

## Task 3: InventoryWindow (`inventory.png`, 168×235)

**Unity source:** `Assets/Prefabs/UI/InventoryCanvas.prefab` (bg `inventory.png`, gold text, 30-slot grid).

**Steps (recipe):** root 168×235; `Background` `TextureRect` = `inventory.png`; keep `Content/SlotGrid` (`columns=5`) positioned over the art's slot cells (6 rows × 5 = 30 of 32px = 160×192 — fits 168 wide with margins; read prefab grid origin/cell/spacing and set `SlotGrid` offsets + `h/v_separation`); keep `Content/GoldText` repositioned to the art's gold readout. `WindowName="Inventory"` stays. Build, commit: `feat(ui): skin+align InventoryWindow`.

---

## Task 4: SpellbookWindow (`spellbook-background.png`, 128×196)

**Unity source:** `Assets/Prefabs/UI/SpellbookCanvas.prefab` — bg `spellbook-background.png`; page nav uses `spellbook-back.png`/`spellbook-next.png` (24×24) buttons, not text "<"/">".

**Steps:** root 128×196; `Background` `TextureRect`; set the existing `BackButton`/`NextButton` to `icon=spellbook-back/next.png`, flat, sized 24×24 at converted positions; position the spell-slot page container over the art. Keep node names `SpellbookWindow.cs`/`SpellbookPage.cs` resolve. Build, commit: `feat(ui): skin SpellbookWindow + sprite page buttons`.

---

## Task 5: HotbarWindow (`hotbar.png`, 333×36, **9-slice 3,3,3,3**)

**Unity source:** `Assets/Prefabs/UI/HotbarCanvas.prefab` — bg `hotbar.png` (Sliced), per-slot `hotbar-slot-background.png` (32×32 Sliced), page up/down `hotbar-up.png`/`hotbar-down.png` (16×16), XP via `xp-bar.png`/`xp-bar-outline.png` (Sliced 3,3,3,3).

**Steps:** root 333×36; `Background` → **`NinePatchRect`** = `hotbar.png`, `patch_margin_* = 3`. Replace BackButton/NextButton with `hotbar-up/down.png` icon buttons at converted positions. XpBar: `NinePatchRect`/`TextureProgressBar` using `xp-bar*.png`. Slot row positioned over the art; HotbarSlot already 32px (Part 1). This is always-on HUD — give it a bottom-center scene position. Keep `HotbarWindow.cs` node names. Build, commit: `feat(ui): skin HotbarWindow (9-slice) + sprite nav/xp`.

---

## Task 6: Vendor / Bank / CombineBag (`vendor.png` 168×276, `bank.png` 168×253, `10slot.png` 69×212)

These are server-spawned `BaseWindow`s (already hidden until packet). Apply the recipe to each.

**Steps:** for each — root to PNG size; `Background` `TextureRect` = the PNG; align the slot grid (Vendor 40-slot, Bank 30-slot, Combine 10-slot per Step 7 consts) over the art's cells; close button `exitbutton.png`; keep `WindowName` + node names. Build, commit: `feat(ui): skin Vendor/Bank/CombineBag windows`.

---

## Task 7: ChatWindow (`chat.png`, 500×208)

**Unity source:** `Assets/Prefabs/UI/ChatWindowCanvas.prefab` — bg `chat.png`; chat text size **12**. Current Godot `ChatWindow` calls `SetAlpha(0.7f)` separately — keep that.

**Steps:** root 500×208; `Background` `TextureRect` = `chat.png`; position the chat log + `LineEdit` input over the art; ensure chat text uses size 12 (theme override on the log). Always-on HUD: bottom-left scene position. Keep `ChatWindow.cs` node names + focus logic. Build, commit: `feat(ui): skin ChatWindow`.

---

## Task 8: Party / Buffs (`party-frame.png` 87×23 per member; Buffs overlay)

**Unity source:** `PartyCanvas.prefab` (per-member `party-frame.png` + `party-hp-bar.png`/`party-mp-bar.png`), `BuffEffectsCanvas.prefab` (overlay row, no window bg).

**Steps:** PartyWindow — skin each `PartyMember` row with `party-frame.png` + the HP/MP bar sprites at converted positions (keep `PartyMember.cs` node names). BuffEffectsWindow — overlay row of buff icons, no background (matches Unity); just confirm position + slot size. Both always-on HUD; give screen positions. Build, commit: `feat(ui): skin Party + Buffs`.

---

## Task 9: Options / Info / Quest / Debug

**Unity source:** Options used Unity's default panel (no custom skin) — give it a simple skinned `BaseWindow` (reuse `inventory`/generic frame OR a `StyleBoxFlat` panel — Options has no dedicated PNG). Info (`info.png` 252×140, **9-slice 3,3,10,20**) and Quest (`quest.png` 260×291, **9-slice 5,36,12,20**) use `NinePatchRect`. Debug (`DebugCanvas`) has no fixed bg — keep transparent FPS/version overlay, just ensure it reads via the theme (size 12) and sits in a corner.

**Steps:** OptionsWindow — root + a panel background (no PNG → keep a minimal `StyleBoxFlat` only here, or reuse a generic frame), close button. InfoWindow/QuestWindow — `NinePatchRect` backgrounds with the verified patch margins; these are `BaseMultipleWindow`-derived/server-driven, keep their managers working. DebugWindow — corner overlay, theme-styled. Build, commit: `feat(ui): skin Options/Info/Quest + theme Debug`.

---

## Task 10: Live visual tuning + validation pass

UI-on-art alignment must be confirmed against the running client; do final pixel nudges here.

**Prereq:** a desktop with a display + the live server (`GOOSE_HOST=scyther.local GOOSE_PORT=2006`) + valid credentials. (This is the same environment gap Step 7/Step 8 E1 noted.)

**Steps:**
1. Launch the client; log in.
2. For each window (open the toggle ones with I/C/B; trigger server ones via vendor/bank/combine; observe always-on HUD): screenshot and compare to the Unity client's look.
3. Nudge offsets where widgets sit off their art cells. Re-run after each scene edit.
4. Verify: no window opens at `(100,100)` overlap; Inventory/Character/Spellbook start closed and toggle open at distinct positions; positions **and** visibility persist across relog; text is white/readable (LiberationSans); slots align to art cells; CharacterWindow shows labeled stats + 7×2 equipment + (Step 8) portrait on Vitals; hover transparency 0.7↔1.0 works.
5. Capture before/after screenshots into `docs/` for the record.
6. Commit any tuning: `fix(ui): live-tuned window widget offsets`.

---

## Part 2 Done — Checkpoint
- [ ] Every window sized to its PNG with the correct art background.
- [ ] All widgets aligned to art cells (live-tuned).
- [ ] CharacterWindow rebuilt; Vitals has the portrait node (A1 renders into it later).
- [ ] No overlap; minimal-HUD default; position+visibility persist.
- [ ] Screenshots captured.

**Update `MIGRATION_PLAN.md`:** record the UI window overhaul landed (faithful sprite skins + layout); note the deferred Step 8 items still owned by Step 8 (A1 portrait rendering, A2 targeting, B overlays, etc.). Then resume the Step 8 plans (`2026-06-07-step8-part1-…` / `-part2-…`).
