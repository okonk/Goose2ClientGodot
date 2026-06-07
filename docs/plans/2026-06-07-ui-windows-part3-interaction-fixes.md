# UI Windows Part 3 — Interaction Fixes Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Fix the six interaction regressions found after the Part 1/Part 2 window overhaul: windows can't be dragged, inventory icons all stack in slot 1, item/spell tooltips don't appear, spellbook won't cast, the hotbar shows no slots, and the toolbar is a top-left text strip instead of bottom-right icon buttons.

**Architecture:** These are scene-layer + one-line-code fixes, not new features. Five of the six bugs collapse into **two** root causes: (1) every window's full-rect `Content` node (`MouseFilter=Pass`) is drawn on top of the `TitleBar` and swallows its clicks, killing dragging; (2) the three slot scenes (`ItemSlot`/`SpellSlot`/`HotbarSlot`) have no `custom_minimum_size`, so inside a `GridContainer` every slot collapses to (0,0) and stacks at the grid origin — which simultaneously breaks icon placement, hover tooltips, and click-to-cast. The toolbar is an independent re-skin (bottom-right anchor + 32px icon buttons), and needs one Unity asset (`combinebagbutton.png`) that Part 1 never imported. Finally, the Hotbar — which has no room for a draggable title bar on its 36px art — is anchored to the **bottom-center** of the screen so it docks correctly without dragging (instead of its current fixed `(410, 600)`).

**Tech Stack:** Godot 4.6 `.tscn` scenes + C# (`partial class : Control`); xUnit for the (unchanged) pure-logic suite; no headless Godot binary is available in this environment.

---

## Why this isn't classic TDD

Five of these six fixes are **Godot scene-resource edits** (`.tscn`), and the sixth is a single `MouseFilter` assignment. None introduce new pure logic, so there is nothing new to unit-test — and Godot's layout/input engine (which is what actually consumes `custom_minimum_size`, `mouse_filter`, anchors) can't be exercised without the editor/runtime, which isn't present here. Forcing fake unit tests on scene properties would be theater.

The honest verification strategy for this plan is therefore:

1. **Build gate** — `dotnet build` stays at 0 errors (guards the one C# edit).
2. **Regression gate** — the existing `dotnet test` suite stays green (guards that nothing in `Scripts/` broke).
3. **Visual acceptance** — a concrete, per-bug checklist run live against `scyther.local:2006` (Task 4). This is the real test for UI-on-art interaction and is treated as a first-class gate, not an afterthought.

Each task below lists its own acceptance so the implementer knows exactly what "done" looks like before committing.

---

## APIs / Facts Verified (path:line)

**Drag bug:**
- `Scripts/UI/BaseWindow.cs:29` — `Content = GetNodeOrNull<Control>("Content");` (so a code fix in `_Ready` reaches every window).
- `Scripts/UI/BaseWindow.cs:40-41` — `if (_titleBar != null) _titleBar.GuiInput += OnTitleBarGuiInput;` (drag handler is wired to the TitleBar's `GuiInput`).
- `Scripts/UI/BaseWindow.cs:53-79` — `OnTitleBarGuiInput` implements press/drag/release; it can only fire if the TitleBar actually receives the click.
- `Scenes/UI/InventoryWindow.tscn:47-52` — `Content` is `anchors_preset = 15` (full rect) with `mouse_filter = 1` (Pass), declared **after** `TitleBar` (so drawn on top). Godot `MOUSE_FILTER_PASS` propagates an unhandled event to the **parent**, never to a sibling, so the TitleBar never sees the click. Same pattern in `CharacterWindow.tscn:47-52`, `HotbarWindow.tscn:43-48`, and the other relayout scenes.
- Windows that subclass `BaseWindow` (so the fix covers them): `Scripts/UI/{OptionsWindow,SpellbookWindow,BankWindow,HotbarWindow,VendorWindow,InventoryWindow,CharacterWindow,CombineBagContainerWindow}.cs` + `BaseMultipleWindow.cs` (Quest/Info). `Scripts/UI/ChatWindow.cs:13` is `: Control` (NOT a BaseWindow) — out of scope.

**Slot-collapse bug (one fix, four windows):**
- `Scenes/UI/ItemSlot.tscn:5-12` — root `Panel`, `layout_mode = 0`, offsets `0..32`, **no `custom_minimum_size`**. A container ignores layout_mode-0 offsets and sizes the child to its minimum size = `(0,0)`.
- `Scenes/UI/SpellSlot.tscn:5-12` — root `Panel`, offsets `0..24`, no `custom_minimum_size`.
- `Scenes/UI/HotbarSlot.tscn:5-12` — root `Panel`, offsets `0..32`, no `custom_minimum_size`.
- `Scripts/UI/InventoryWindow.cs:30` — `var grid = GetNode<GridContainer>("Content/SlotGrid");` then `grid.AddChild(slot)` (slots become grid children → collapse).
- `Scripts/UI/SpellbookWindow.cs:44` — `var grid = new GridContainer { Columns = 5 };` (same).
- `Scripts/UI/HotbarWindow.cs:74` — `var grid = new GridContainer { Columns = SlotsPerPage };` (same).
- `Scripts/UI/VendorWindow.cs:32`, `Scripts/UI/BankWindow.cs:41`, `Scripts/UI/CombineBagContainerWindow.cs:38` — all `GetNode<GridContainer>("Content/SlotGrid")` reusing `ItemSlot.tscn` → all fixed by the ItemSlot edit.
- `Scripts/UI/CharacterWindow.cs:68-74` — slots go into a plain `Control` (`type="Control"` at `CharacterWindow.tscn:54`) with `slot.Position = CharacterEquipmentLayout.SlotOffset(i)`. A plain Control is **not** a container, so the slot keeps its `0..32` rect today (this is why character equipment already renders). Adding `custom_minimum_size = (32,32)` matches the existing size → **no regression**.
- Hover tooltips fire from `Scripts/UI/ItemSlot.cs:49-58` (`OnMouseEntered → TooltipManager.Instance.ShowItemTooltip`) and `Scripts/UI/SpellSlot.cs:52-61`; these require a non-zero hover rect. Cast/use fire from `Scripts/UI/ItemSlot.cs:60-69` and `Scripts/UI/SpellSlot.cs:63-75` on **double-click** (`mb.DoubleClick`), which requires a clickable rect. So sizing the slots fixes tooltips (bug 3), spellbook cast (bug 4), and hotbar slot visibility (bug 5) together.
- `TooltipManager.Instance` is created in the HUD (`Scripts/UI/GameHud.cs:47` loads `Tooltips.tscn`, "sets TooltipManager.Instance" per `GameHud.cs:46`) — so the manager exists; only the missing hover rect was blocking it.

**Toolbar bug:**
- `Scenes/UI/Toolbar.tscn` — root is an `HBoxContainer` at `offset 0,0` with four **text** `Button`s (`Destroy` / `Combine Bag` / `Options` / `Exit`).
- `Scripts/UI/ToolbarItem.cs:7-13` — `enum ToolbarItemType { Destroy=0, CombineBag=1, Options=2, Exit=3 }`; current scene sets `ItemType = 1/2/3` on the three `ToolbarItem` buttons (Destroy uses the separate `DestroyButton.cs`).
- `Scripts/UI/GameHud.cs:55` — `var toolbar = Add<Control>("res://Scenes/UI/Toolbar.tscn");` (root must stay a `Control`; `HBoxContainer` is a `Control` ✓).
- `Scripts/UI/GameHud.cs:74-76` — `toolbar.GetNodeOrNull<ToolbarItem>("OptionsButton")` then `optionsBtn.OnOptions = Options.ToggleWindow;` → the node **name** `OptionsButton` must be preserved in the rebuilt scene.
- `Scripts/UI/DestroyButton.cs:8-46` — `DestroyButton : Button` is a pure drop target (no `Pressed` handler), so it must keep its script on the Destroy button.
- Unity reference `…/Goose2Client/Assets/Prefabs/UI/ToolbarCanvas.prefab` — `ToolbarWindow` RectTransform `m_AnchoredPosition: {x: 568, y: -339}` on the 1280×720 reference canvas (center origin) → screen ≈ `(1208, 699)`, i.e. **bottom-right**, ~8px from each edge; each `ToolbarItem` `m_SizeDelta: {x: 32, y: 32}`.
- Icons present in `Assets/UI/`: `destroy.png`, `exitbutton.png`, `optionsbutton.png` (all 32×32, with `.import`). **Missing:** `combinebagbutton.png` (exists at `…/Goose2Client/Assets/Resources/UI/combinebagbutton.png`, 32×32) — Part 1's import loop skipped it.
- `Assets/UI/destroy.png.import` — the import template to clone for the new asset (`importer="texture"`, `type="CompressedTexture2D"`, `compress/mode=0`, `mipmaps/generate=false`).
- `project.godot:336` — `textures/canvas_textures/default_texture_filter=0` (Nearest) is already set globally, so the new icon inherits crisp pixel filtering.

**Hotbar dock (Task 4):**
- `Scripts/UI/DefaultWindowLayout.cs:13` — `["Hotbar"] = new Vector2(410, 600)` — a fixed absolute default; not bottom-center and not responsive to window size.
- `Scripts/UI/BaseWindow.cs:32-37` — `_Ready` **always** sets `Position` from saved/default for any `WindowName != null`. That would clobber scene anchors, so a docked window needs an opt-out (added in Task 4 as `UseFixedDockLayout`).
- `project.godot` has **no `[display]` section** → default viewport, native-pixel UI, resizable window. A one-time absolute position won't stay centered, so the dock must use **anchors**, not a computed `Position`.
- `Scenes/UI/HotbarWindow.tscn:11-19` — root currently top-left-anchored, `offset 0..333 / 0..36`.
- Unity `…/Goose2Client/Assets/Prefabs/UI/HotbarCanvas.prefab` — `HotbarWindow` `m_AnchoredPosition: {x: 62, y: -329}` on the 1280×720 canvas (≈ screen `(702, 689)`): docked at the bottom, slightly **right** of center; the inner `Hotbar` child is offset `(0, -8)` (8px up from the bottom). The user wants it **centered**, so we center horizontally (vs Unity's +62) and keep the ~8px bottom inset.

**Known limitations (intentionally out of scope — note to user, do not "fix"):**
- The Hotbar stays **non-draggable** by design (`HotbarWindow.tscn:33-40` — its `TitleBar` has `offset_bottom = 0.0`; the 333×36 art has no room for a title strip). Task 4 makes it dock correctly bottom-center so dragging isn't needed; don't invent a drag region.
- `ChatWindow` is a plain `Control`, not a `BaseWindow`; its drag (if desired) is a separate task and was not reported.

---

## Task 0: Branch + import the missing toolbar icon

**Files:**
- Create: `Assets/UI/combinebagbutton.png` (copied from the read-only Unity reference)
- Create: `Assets/UI/combinebagbutton.png.import`

**Step 1: Create the working branch**

```bash
cd /home/hayden/code/Goose2ClientGodot
git checkout -b fix/ui-window-interaction
```

**Step 2: Copy the icon from the Unity reference (read-only source — copy only, never modify it)**

```bash
cp /home/hayden/code/Goose2Client/Assets/Resources/UI/combinebagbutton.png \
   Assets/UI/combinebagbutton.png
```

Verify: `ls -l Assets/UI/combinebagbutton.png` shows a 32×32 PNG (~hundreds of bytes).

**Step 3: Create the `.import` sidecar**

> **Preferred** (if a `godot` binary is on PATH): run `godot --headless --import` from the project root and skip the hand-authored file below — Godot will generate a correct `.import` automatically. No binary is available in this environment, so hand-author it; Godot will regenerate the `uid`, `path` hash, and the `.ctex` on the next editor launch (this is expected and harmless).

Create `Assets/UI/combinebagbutton.png.import`:

```ini
[remap]

importer="texture"
type="CompressedTexture2D"
uid="uid://b1cmbnbg32tlb"
path="res://.godot/imported/combinebagbutton.png-0123456789abcdef0123456789abcdef.ctex"
metadata={
"vram_texture": false
}

[deps]

source_file="res://Assets/UI/combinebagbutton.png"
dest_files=["res://.godot/imported/combinebagbutton.png-0123456789abcdef0123456789abcdef.ctex"]

[params]

compress/mode=0
compress/high_quality=false
compress/lossy_quality=0.7
compress/uastc_level=0
compress/rdo_quality_loss=0.0
compress/hdr_compression=1
compress/normal_map=0
compress/channel_pack=0
mipmaps/generate=false
mipmaps/limit=-1
roughness/mode=0
roughness/src_normal=""
process/channel_remap/red=0
process/channel_remap/green=1
process/channel_remap/blue=2
process/channel_remap/alpha=3
process/fix_alpha_border=true
process/premult_alpha=false
process/normal_map_invert_y=false
process/hdr_as_srgb=false
process/hdr_clamp_exposure=false
process/size_limit=0
detect_3d/compress_to=1
```

**Step 4: Commit**

```bash
git add Assets/UI/combinebagbutton.png Assets/UI/combinebagbutton.png.import
git commit -m "chore(ui): import combinebagbutton.png (missed in Part 1 import)"
```

**Acceptance:** `Assets/UI/combinebagbutton.png` + `.import` exist; nothing else changed. (The icon won't render until Task 3 references it.)

---

## Task 1: Restore window dragging (BaseWindow `Content` occlusion)

**Files:**
- Modify: `Scripts/UI/BaseWindow.cs:24-37` (`_Ready`, after the node lookups)

**Root cause recap:** `Content` is a full-rect node with `mouse_filter = Pass`, declared after `TitleBar` so it's drawn on top. A click in the title-bar region hits `Content`; `Pass` forwards the unhandled event to `Content`'s **parent** (the window root), never to the `TitleBar` sibling — so `OnTitleBarGuiInput` (`BaseWindow.cs:53`) never runs. Making `Content` transparent to the mouse lets the `TitleBar` receive drag clicks. `Content`'s interactive descendants (slots, buttons, bars) have their own `MouseFilter` and are unaffected, because `mouse_filter` is per-node and does not cascade to children.

**Step 1: Add the fix in `_Ready`**

In `Scripts/UI/BaseWindow.cs`, change:

```csharp
        Content = GetNodeOrNull<Control>("Content");
        Background = GetNodeOrNull<TextureRect>("Background");
```

to:

```csharp
        Content = GetNodeOrNull<Control>("Content");
        Background = GetNodeOrNull<TextureRect>("Background");

        // The full-rect Content (MouseFilter=Pass) is drawn on top of the TitleBar and
        // swallows its clicks — Pass forwards unhandled events to the PARENT, never to the
        // TitleBar sibling — which kills title-bar dragging. Make Content transparent to the
        // mouse so the TitleBar receives drag clicks. Interactive descendants (slots, buttons,
        // bars) keep their own MouseFilter and are unaffected (mouse_filter does not cascade).
        if (Content != null)
            Content.MouseFilter = MouseFilterEnum.Ignore;
```

**Step 2: Build**

```bash
dotnet build Goose2ClientGodot.csproj
```

Expected: `Build succeeded.` with `0 Error(s)`.

**Step 3: Commit**

```bash
git add Scripts/UI/BaseWindow.cs
git commit -m "fix(ui): restore window dragging (Content no longer occludes TitleBar)"
```

**Acceptance (visual, verified in Task 4):** Inventory, Character, Spellbook, Vendor, Bank, CombineBag, Options windows can be dragged by their title bar; slots/buttons inside still respond to clicks; drag-position persistence still saves on release (`BaseWindow.cs:64-66`).

---

## Task 2: Fix slot sizing (`ItemSlot` / `SpellSlot` / `HotbarSlot`)

**Files:**
- Modify: `Scenes/UI/ItemSlot.tscn:5-12` (root node)
- Modify: `Scenes/UI/SpellSlot.tscn:5-12` (root node)
- Modify: `Scenes/UI/HotbarSlot.tscn:5-12` (root node)

**Root cause recap:** A `GridContainer` sizes each child to its **minimum size** and ignores layout_mode-0 offsets. With no `custom_minimum_size`, every slot's min size is `(0,0)`, so all slots collapse onto the grid origin — icons pile into "slot 1" (bug 2), there's no hover rect (bug 3 tooltips), no click rect (bug 4 cast), and the hotbar's slots are invisible (bug 5). Setting `custom_minimum_size` gives the container a real cell size. The slot's `Icon`/`Count`/`CooldownOverlay` children use layout_mode-0 offsets matching the slot dimensions, so they fill the now-correctly-sized slot.

**Step 1: Size the ItemSlot (32×32) — covers Inventory, Vendor, Bank, CombineBag**

In `Scenes/UI/ItemSlot.tscn`, change:

```
[node name="ItemSlot" type="Panel"]
script = ExtResource("1_itemslot")
layout_mode = 0
mouse_filter = 0
```

to:

```
[node name="ItemSlot" type="Panel"]
script = ExtResource("1_itemslot")
custom_minimum_size = Vector2(32, 32)
layout_mode = 0
mouse_filter = 0
```

**Step 2: Size the SpellSlot (24×24) — covers Spellbook**

In `Scenes/UI/SpellSlot.tscn`, change:

```
[node name="SpellSlot" type="Panel"]
script = ExtResource("1_spells")
layout_mode = 0
mouse_filter = 0
```

to:

```
[node name="SpellSlot" type="Panel"]
script = ExtResource("1_spells")
custom_minimum_size = Vector2(24, 24)
layout_mode = 0
mouse_filter = 0
```

**Step 3: Size the HotbarSlot (32×32) — covers Hotbar**

In `Scenes/UI/HotbarSlot.tscn`, change:

```
[node name="HotbarSlot" type="Panel"]
script = ExtResource("1_hotbar")
layout_mode = 0
mouse_filter = 0
```

to:

```
[node name="HotbarSlot" type="Panel"]
script = ExtResource("1_hotbar")
custom_minimum_size = Vector2(32, 32)
layout_mode = 0
mouse_filter = 0
```

**Step 4: Build (sanity — no C# changed, must still pass)**

```bash
dotnet build Goose2ClientGodot.csproj
```

Expected: `Build succeeded.` `0 Error(s)`.

**Step 5: Commit**

```bash
git add Scenes/UI/ItemSlot.tscn Scenes/UI/SpellSlot.tscn Scenes/UI/HotbarSlot.tscn
git commit -m "fix(ui): give slots a custom_minimum_size so they lay out in their GridContainer"
```

**Acceptance (visual, verified in Task 4):** Inventory items appear in their correct slots (not stacked in slot 1); hovering an item/spell shows its tooltip; double-clicking a spell casts it (or enters targeting for targeted spells); the hotbar shows a full 10-slot row per page; Vendor/Bank/CombineBag grids lay out correctly; Character equipment slots are unchanged.

---

## Task 3: Rebuild the toolbar (bottom-right, icon buttons)

**Files:**
- Overwrite: `Scenes/UI/Toolbar.tscn`

**Layout math:** 4 buttons × 32px + 3 gaps × 2px = **134px wide × 32px tall**, anchored to the bottom-right corner with an 8px margin (matches Unity's ~8px corner inset). With all anchors at `1.0`: `offset_right = -8`, `offset_bottom = -8`, `offset_left = -(134+8) = -142`, `offset_top = -(32+8) = -40`. `grow_horizontal/vertical = 0` (BEGIN) so the box grows up-and-left from the corner. Node **names** (`OptionsButton`, plus `DestroyButton` keeping `DestroyButton.cs`) and the `ItemType` exports are preserved so `GameHud.cs:74-76` and `ToolbarItem.OnPressed` keep working.

**Step 1: Overwrite `Scenes/UI/Toolbar.tscn` with the icon layout**

```
[gd_scene load_steps=7 format=3]

[ext_resource type="Script" path="res://Scripts/UI/DestroyButton.cs" id="1_db"]
[ext_resource type="Script" path="res://Scripts/UI/ToolbarItem.cs" id="2_ti"]
[ext_resource type="Texture2D" path="res://Assets/UI/destroy.png" id="3_destroy"]
[ext_resource type="Texture2D" path="res://Assets/UI/combinebagbutton.png" id="4_combine"]
[ext_resource type="Texture2D" path="res://Assets/UI/optionsbutton.png" id="5_options"]
[ext_resource type="Texture2D" path="res://Assets/UI/exitbutton.png" id="6_exit"]

[node name="Toolbar" type="HBoxContainer"]
layout_mode = 3
anchor_left = 1.0
anchor_top = 1.0
anchor_right = 1.0
anchor_bottom = 1.0
offset_left = -142.0
offset_top = -40.0
offset_right = -8.0
offset_bottom = -8.0
grow_horizontal = 0
grow_vertical = 0
theme_override_constants/separation = 2

[node name="DestroyButton" type="Button" parent="."]
custom_minimum_size = Vector2(32, 32)
layout_mode = 2
script = ExtResource("1_db")
flat = true
icon = ExtResource("3_destroy")
expand_icon = true

[node name="CombineBagButton" type="Button" parent="."]
custom_minimum_size = Vector2(32, 32)
layout_mode = 2
script = ExtResource("2_ti")
flat = true
icon = ExtResource("4_combine")
expand_icon = true
ItemType = 1

[node name="OptionsButton" type="Button" parent="."]
custom_minimum_size = Vector2(32, 32)
layout_mode = 2
script = ExtResource("2_ti")
flat = true
icon = ExtResource("5_options")
expand_icon = true
ItemType = 2

[node name="ExitButton" type="Button" parent="."]
custom_minimum_size = Vector2(32, 32)
layout_mode = 2
script = ExtResource("2_ti")
flat = true
icon = ExtResource("6_exit")
expand_icon = true
ItemType = 3
```

**Step 2: Build (sanity — `GameHud` still resolves `OptionsButton` by name)**

```bash
dotnet build Goose2ClientGodot.csproj
```

Expected: `Build succeeded.` `0 Error(s)`.

**Step 3: Commit**

```bash
git add Scenes/UI/Toolbar.tscn
git commit -m "feat(ui): move toolbar to bottom-right with 32px icon buttons"
```

**Acceptance (visual, verified in Task 4):** four 32px icon buttons sit in the bottom-right corner (Destroy, Combine Bag, Options, Exit); Options toggles the Options window; Combine Bag opens the combine bag; Exit quits; the Destroy button still accepts dropped items/spells/hotbar entries (`DestroyButton.cs`).

---

## Task 4: Dock the Hotbar bottom-center

**Files:**
- Modify: `Scripts/UI/BaseWindow.cs` (add a `UseFixedDockLayout` opt-out; guard the `Position` restore)
- Modify: `Scripts/UI/HotbarWindow.cs` (override `UseFixedDockLayout => true`)
- Modify: `Scenes/UI/HotbarWindow.tscn:11-19` (root → bottom-center anchors)

**Why anchors, not a computed position:** the window is resizable with no `[display]` lock, so a one-time `Position` would drift off-center on resize. Anchoring `anchor_left = anchor_right = 0.5`, `anchor_top = anchor_bottom = 1.0` keeps the 333px bar horizontally centered and pinned to the bottom at any size. But `BaseWindow._Ready` unconditionally assigns `Position` (which rewrites a Control's offsets), so we add a `protected virtual bool UseFixedDockLayout` the Hotbar overrides to keep the engine from clobbering its anchors.

**Step 1: Add the opt-out hook to `BaseWindow`**

In `Scripts/UI/BaseWindow.cs`, add the virtual property next to the other members (e.g. just under `public string Title { ... }`):

```csharp
    /// <summary>When true, BaseWindow leaves Position alone so the scene's anchors govern
    /// placement. Used by always-docked, non-draggable windows like the Hotbar.</summary>
    protected virtual bool UseFixedDockLayout => false;
```

Then change the position-restore block:

```csharp
        // Restore persisted position (or first-run default)
        if (WindowName != null)
        {
            var ws = GameManager.Instance.CharacterSettings.GetWindowSettings(WindowName);
            Position = ws != null ? ws.Position : DefaultWindowLayout.For(WindowName);
        }
```

to:

```csharp
        // Restore persisted position (or first-run default). Skipped for fixed-dock windows
        // (e.g. the Hotbar), which anchor themselves in their scene and aren't draggable.
        if (WindowName != null && !UseFixedDockLayout)
        {
            var ws = GameManager.Instance.CharacterSettings.GetWindowSettings(WindowName);
            Position = ws != null ? ws.Position : DefaultWindowLayout.For(WindowName);
        }
```

**Step 2: Opt the Hotbar in**

In `Scripts/UI/HotbarWindow.cs`, add after `public WindowFrames WindowFrame => WindowFrames.Hotbar;` (line 39):

```csharp
    protected override bool UseFixedDockLayout => true;
```

**Step 3: Anchor the Hotbar scene bottom-center**

In `Scenes/UI/HotbarWindow.tscn`, change the root node:

```
[node name="HotbarWindow" type="Control"]
script = ExtResource("1_hw")
theme = ExtResource("theme")
layout_mode = 3
offset_left = 0.0
offset_top = 0.0
offset_right = 333.0
offset_bottom = 36.0
WindowName = "Hotbar"
```

to:

```
[node name="HotbarWindow" type="Control"]
script = ExtResource("1_hw")
theme = ExtResource("theme")
layout_mode = 3
anchor_left = 0.5
anchor_top = 1.0
anchor_right = 0.5
anchor_bottom = 1.0
offset_left = -166.0
offset_top = -44.0
offset_right = 167.0
offset_bottom = -8.0
grow_horizontal = 2
grow_vertical = 0
WindowName = "Hotbar"
```

Geometry check: width `= 167 − (−166) = 333`; height `= −8 − (−44) = 36`; centered horizontally (0.5px right bias, invisible); pinned 8px above the bottom; `grow_vertical = 0` (BEGIN) so it grows upward from the bottom anchor. The XP bar (`XpBar` at `offset_top = -17`) rides 17px above the bar — comfortably on-screen.

**Step 4: Build**

```bash
dotnet build Goose2ClientGodot.csproj
```

Expected: `Build succeeded.` `0 Error(s)`.

**Step 5: Commit**

```bash
git add Scripts/UI/BaseWindow.cs Scripts/UI/HotbarWindow.cs Scenes/UI/HotbarWindow.tscn
git commit -m "fix(ui): dock the (non-draggable) hotbar to bottom-center via anchors"
```

**Acceptance (visual, verified in Task 5):** the hotbar sits horizontally centered, ~8px above the screen bottom, and stays centered/docked when the window is resized; the XP bar sits just above it; no stale `(410, 600)` placement.

---

## Task 5: Build + test gate, then live visual validation

**Files:**
- Modify: `MIGRATION_PLAN.md` (mark the Part 3 follow-ups resolved, consistent with Steps 6/7)

**Step 1: Full build**

```bash
dotnet build Goose2ClientGodot.csproj
```

Expected: `Build succeeded.` `0 Error(s)`.

**Step 2: Regression test suite**

```bash
dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj
```

Expected: `Passed!` — `Failed: 0`. (No tests were added; this proves nothing in `Scripts/` regressed.)

**Step 3: Live visual validation**

Launch the client against the test server and walk the checklist. The user runs the editor/game (no headless display here); when the editor opens it will reimport `combinebagbutton.png` (regenerating its `.ctex`/`uid`) — expected.

```bash
GOOSE_HOST=scyther.local GOOSE_PORT=2006 <run the Godot client>
```

Per-bug acceptance checklist:

- [ ] **Drag** — drag Inventory / Character / Spellbook / Vendor / Bank / CombineBag / Options by the title bar; release persists position across relog.
- [ ] **Inventory icons** — items render in their correct slots, not stacked in slot 1; 5-column grid.
- [ ] **Tooltips** — hovering an inventory item, an equipped item, and a spell each shows its tooltip; hovering the XP bar shows the XP tooltip.
- [ ] **Spellbook** — double-click a non-targeted spell casts it; double-click a targeted spell enters targeting; on-cooldown spells don't fire.
- [ ] **Hotbar slots** — a full 10-slot row is visible per page; paging works; drag item/spell onto a hotbar slot then use it.
- [ ] **Hotbar dock** — centered horizontally, ~8px above the bottom; stays centered/docked after resizing the window; XP bar sits just above it.
- [ ] **Toolbar** — four icon buttons bottom-right; Options toggles, Combine Bag opens, Exit quits, Destroy accepts a dropped item.
- [ ] **No new overlap / regressions** — minimal-HUD default still holds (Inventory/Character/Spellbook start closed; I/C/B toggle).

**Step 4: Record outcome + commit docs**

Update `MIGRATION_PLAN.md` to note "UI Windows Part 3 (interaction fixes) landed" with the six items resolved plus the hotbar bottom-center dock (and the two known limitations: Hotbar still non-draggable by design, ChatWindow drag deferred).

```bash
git add MIGRATION_PLAN.md
git commit -m "docs(migration): record UI Windows Part 3 interaction fixes landed"
```

**Acceptance:** build + tests green; every checklist box ticked or its deviation recorded; docs updated.

---

## Notes
- **Scope:** the Hotbar is docked bottom-center (Task 4) but stays **non-draggable** (no title-bar room on the 36px art); ChatWindow dragging (not a `BaseWindow`; unreported) is excluded. Flag both to the user rather than silently expanding scope.
- **If a `godot` binary appears**, regenerate `combinebagbutton.png.import` via `godot --headless --import` and re-commit the corrected sidecar.
- **DRY:** the slot-size fix is applied once per slot scene and automatically covers all six windows that reuse those scenes; the drag fix is applied once in `BaseWindow` and covers all nine subclasses.
