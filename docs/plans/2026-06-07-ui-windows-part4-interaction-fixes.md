# UI Windows Part 4 — Interaction & Visual Fixes Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Fix the round-2 UI bugs found after Part 3: tooltips rendering behind windows and with no background; vitals/inventory text alignment; the always-visible party bar; the dead exit button and non-draggable Character window; and the bare hotbar (no slot frames, no number labels, undersized icons). Spell *targeting* is diagnosed and cross-linked to the existing Step 8 task (see Task 8).

**Architecture:** All HUD UI lives under one `GameHud` Control inside a single `CanvasLayer` (`GameManager.UiLayer`). Windows derive from `BaseWindow` (title-bar drag, hover transparency, persisted position, close button). Slots (`ItemSlot`/`SpellSlot`/`HotbarSlot`) are `Panel`s instantiated into `GridContainer`s. Tooltips are four `Control`s under one `TooltipManager`. The fixes are almost entirely scene-layer (`.tscn`) plus a handful of small C# changes; the only new *pure logic* is the hotbar slot-number label mapping, which gets a real unit test.

**Tech Stack:** Godot 4.6 / C# (.NET 10), xUnit. Read-only Unity reference at `/home/hayden/code/Goose2Client`. No Godot binary in this environment — validation is build gate + `dotnet test` + a first-class live visual checklist (Task 9).

---

## Why this isn't classic TDD

Eight of the nine fixes change Godot scene files (`.tscn` node properties) or do one-line C# wiring (CanvasLayer parenting, `MoveChild`, a `Visible = false`). None introduce new pure functions, so there is nothing a unit test can meaningfully assert that the build + a visual check don't already cover — fabricating xUnit tests that assert `.tscn` string contents would be theater, not verification.

**The one exception is real TDD:** the hotbar slot-number mapping (`index 9 → "0"`, otherwise `index + 1`) is pure logic. Task 6 extracts it to a static method `HotbarSlot.SlotLabel(int)` and tests it first.

Everything else is gated by three things:
1. **Build gate** — `dotnet build` with **0 errors** after every C# change.
2. **Regression gate** — `dotnet test` stays **green** (no existing test is broken).
3. **Live visual checklist** — Task 9 enumerates each bug + the exact on-screen expectation, run against `scyther.local:2006`. This is the primary evidence for scene-layer work and is treated as a first-class deliverable, not an afterthought.

---

## APIs / Facts Verified (path:line)

### Godot port (`/home/hayden/code/Goose2ClientGodot`)
- **Tooltip z-order:** All HUD UI is one CanvasLayer — `Scripts/GameManager.cs:62` creates the only `UiLayer = new CanvasLayer()`, `:170` adds `GameHud` to it. `Scripts/UI/GameHud.cs:47` adds `Tooltips.tscn` as a GameHud child **before** every window (`:50-63`). Sibling Controls draw in tree order → windows drawn later occlude the tooltip. Nothing raises it (`TooltipManager.cs:33-37` only sets `Visible`). `GameHud.Add<T>` helper at `GameHud.cs:28-33`.
- **Tooltip positioning:** `ItemTooltipControl._Process` (`Scripts/UI/ItemTooltipControl.cs:54-77`) uses `GetGlobalMousePosition()` + `GlobalPosition` + `Size` — works unchanged under a CanvasLayer (default identity transform). `SetItem` builds stat lines into `_statsVBox` (`:37-51`); blank separator lines are added but set `Visible = false` (`:49`) → invisible children take no space in a `VBoxContainer`.
- **Tooltip background (missing):** `Scenes/UI/Tooltips.tscn` — `ItemTooltip` (`:17-58`), `SpellTooltip` (`:60-71`), `TextTooltip` (`:73-84`), `MapItemTooltip` (`:86-107`) have **no background node**. Theme maps `Panel/styles/panel` to a `StyleBoxEmpty` (`Assets/UI/GameTheme.tres:13,23`) so a bare Panel draws nothing.
- **Window drag + close button:** `Scripts/UI/BaseWindow.cs` — `_Ready` (`:28-64`); resolves `_titleBar` (`:30`), `_closeButton = GetNodeOrNull<Button>("TitleBar/CloseButton")` (`:31`), `Content` (`:33`); sets `Content.MouseFilter = Ignore` (`:41-42`); restores position (`:46-50`); wires `_titleBar.GuiInput += OnTitleBarGuiInput` (`:53-54`) and `_closeButton.Pressed += OnClosePressed` (`:62-63`). `OnClosePressed()` (`:105-110`) calls `Hide()` + persists `Visible=false`. **The wiring is correct** — the failure is per-scene occlusion of the TitleBar:
  - `Scenes/UI/InventoryWindow.tscn` — `TitleBar` y0–19 STOP (`:26-34`), `CloseButton` y2–18 (`:36-45`); Content children `SlotGrid` y20+ (`:54-60`), `GoldText` y221+ (`:66-72`) never cover the titlebar → drag + close **work**.
  - `Scenes/UI/SpellbookWindow.tscn` — `TitleBar` y0–19 (`:29-37`), `CloseButton` y2–18 (`:39-48`); Content children `Pages` y19+ (`:57-64`), Back/Next y170+ (`:66-88`) never cover it → **work**.
  - `Scenes/UI/CharacterWindow.tscn` — `TitleBar` y0–19 (`:26-34`), `CloseButton` y1–17 (`:36-45`); but `Content/SlotGrid` is `anchors_preset = 15` **full-rect** `mouse_filter = 1` PASS (`:54-59`), declared after TitleBar → it draws over the *entire* window including the titlebar + close button. PASS forwards unhandled events to its **parent** (`Content`, now Ignore), never to the `TitleBar` **sibling** → drag **and** close are dead. **Only Character is mechanically broken.**
  - No global input handler steals clicks: only `_UnhandledInput` (`Scripts/UI/GameHud.cs:79`, `Scripts/MapManager.cs:182`), which runs *after* GUI input.
- **Vitals alignment:** `Scenes/UI/VitalsWindow.tscn` — `HpText` `horizontal_alignment = 1` (CENTER) at `:58`, `MpText` at `:68`. Text set in `Scripts/UI/VitalsWindow.cs:56,60` with no code-side alignment.
- **Inventory count alignment:** `Scenes/UI/ItemSlot.tscn` — `Count` label (`:25-40`): `horizontal_alignment = 1` (CENTER, `:40`), pinned **bottom-right** (`anchor_top=anchor_bottom=1.0` `:31,33`; `offset_top=-30 offset_bottom=-2` `:35,37`). `ItemSlot.tscn` is instantiated by Inventory, Vendor, Bank, CombineBag, **and** Character (`grep`), so the fix is global and correct everywhere a stack count appears.
- **Hotbar slot:** `Scenes/UI/HotbarSlot.tscn` — root `Panel` 32×32 (`:5-13`); `Icon` TextureRect 32×32 with `expand_mode = 1`, **`stretch_mode = 2`** (KEEP — draws at native size, top-left; small sprites look tiny) (`:15-23`); `Count` label bottom-right centered (`:38-53`); **no background, no slot-number label.** `Scripts/UI/HotbarSlot.cs` — fields `_icon/_count/_cooldownOverlay` only (`:13-15`); `SlotNumber` is a bare auto-property (`:20`), assigned **after** instantiation at `Scripts/UI/HotbarWindow.cs:87` (so it is *not* set when `_Ready` runs); never rendered. `_Ready` binds nodes (`:43-51`). Hotkey index→label mapping already exists: `i == 9 ? "Hotkey0" : $"Hotkey{i+1}"` (`Scripts/UI/HotbarWindow.cs:334`) → index 9 = "0", else index+1. `Icon.Apply` (`Scripts/UI/Icon.cs:13-26`) sets texture/filter/tint but **not** stretch, so the scene value governs.
- **Hotbar asset present:** `Assets/UI/hotbar-slot-background.png` (+`.import`) exists (imported Part 1).
- **Party always-visible:** `Scenes/UI/PartyMember.tscn` — `Content` node (`:18-23`) has **no `visible` property** → defaults `Visible = true`; it holds `Background` (party-frame.png), `HpBar`, `MpBar` (`:25-62`). `Scripts/UI/PartyMember.cs` — `_Ready` (`:16-22`) binds `_content`; `OnGroupUpdate` sets `_content.Visible = PlayerId != 0` (`:27`) but that runs **only** on a `GroupUpdatePacket`. With no party, nothing ever hides slot 0 → empty frame shows. `party-frame.png` asset present.
- **Spell casting:** `Scripts/UI/SpellbookWindow.cs` `UseSpell` (`:108-124`): `TargetType.None` → `SpellCooldownManager.Cast` + `NetworkClient.CastSpell` (`:113-119`, **works**); else → `GameManager.Instance.SpellTargetManager?.Cast(info)` (`:122`). `Scripts/SpellTargetManager.cs:10-13` `Cast` is a **no-op stub** (non-null instance, constructed in `GameManager`). Slot input is sound: `SpellSlot.tscn` Panel STOP + `custom_minimum_size` (post-Part-3); double-click via `InputEventMouseButton.DoubleClick` (`Scripts/UI/SpellSlot.cs:63-75`), same mechanism as the working `ItemSlot`. → **Targeted spells silently do nothing** because targeting is unimplemented. Already scoped: `docs/plans/2026-06-07-step8-part1-correctness-and-foundations.md:538-629` (Task 9).

### Unity reference (`/home/hayden/code/Goose2Client`, READ-ONLY)
- Tooltip canvas `sortingOrder: 10000` (`Assets/Prefabs/UI/TooltipManager.prefab:46-67`); windows use 99–100 → tooltip always on top.
- Item tooltip background: root `Image`, sprite `tooltip.png` (`Assets/Prefabs/UI/ItemTooltip.prefab:291-320`), `m_Color {1,1,1,1}`; `tooltip.png` center pixel RGBA `(0,0,0,255)` (solid black), rounded transparent corners.
- Vitals HP/MP value text `m_HorizontalAlignment: 1` = **Left** (`Assets/Prefabs/UI/VitalsCanvas.prefab:104,406`), left-edge anchored.
- Item count text `m_HorizontalAlignment: 4` = **Right**, `m_VerticalAlignment: 256` = **Top**, raised toward slot top (`Assets/Prefabs/UI/ItemSlot.prefab:179-180,112-116`).
- Hotbar slot: 32×32 `Background` image `hotbar-slot-background.png`, always active (`Assets/Prefabs/UI/HotbarSlot.prefab:378-453`); `SlotNumber` TMP label base text "1" (`:454-589`); `Image` icon stretches to 32×32 with `m_PreserveAspect: 0` (fills the cell). Number text baked per slot in `HotbarPage.prefab` (overrides → 2,3,…,9,0).
- Party member `Content` `m_IsActive: 0` (hidden by default) in the prefab (`Assets/Prefabs/UI/PartyMember.prefab:396`); revealed only when `GroupUpdatePacket.LoginId != 0` (`Assets/Scripts/UI/PartyMember.cs:18-23`).
- Window close = hide: `Assets/Scripts/UI/InventoryWindow.cs:113-116` `CloseWindow() { panel.SetActive(false); }`. Targeted-spell cast enters a fully-implemented targeting routine `SpellTargetManager.cs:164-237` (the part the Godot stub omits).

---

## Task 0: Prerequisites

**Files:** none (branch + baseline only).

**Step 1: Branch from master**

```bash
cd /home/hayden/code/Goose2ClientGodot
git checkout master && git pull --ff-only 2>/dev/null; git checkout -b fix/ui-windows-part4
```

**Step 2: Confirm a clean build + green tests baseline**

```bash
dotnet build Goose2ClientGodot.csproj
dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj
```
Expected: build **0 errors**; tests **all pass**. (Establishes that any later red is caused by this work.)

**Step 3: Confirm required assets are already imported**

```bash
ls Assets/UI/hotbar-slot-background.png.import Assets/UI/party-frame.png.import Assets/UI/exitbutton.png.import
```
Expected: all three exist. (No new asset import is needed — the tooltip background uses a `StyleBoxFlat`, not a sprite. See Task 2 note.)

No commit for Task 0.

---

## Task 1: Tooltips render above all windows (dedicated CanvasLayer)

**Why:** Tooltips are GameHud children added before the windows, so windows occlude them. Mirror Unity's separate high-sort canvas by putting the tooltips on their own `CanvasLayer` with a higher `Layer` than the UI layer.

**Files:**
- Modify: `Scripts/UI/GameHud.cs:46-47`

**Step 1: Replace the tooltip instantiation**

In `Scripts/UI/GameHud.cs`, replace:

```csharp
        // 3. Tooltips (sets TooltipManager.Instance).
        Add<Control>("res://Scenes/UI/Tooltips.tscn");
```

with:

```csharp
        // 3. Tooltips on a dedicated high CanvasLayer so they always render above every
        //    window (mirrors Unity's tooltip Canvas sortingOrder 10000). The HUD itself
        //    sits in GameManager.UiLayer (Layer 1); 100 guarantees tooltips win even over
        //    runtime-created windows (Info/Quest). TooltipManager._Ready still sets Instance.
        var tooltipLayer = new CanvasLayer { Layer = 100 };
        AddChild(tooltipLayer);
        var tooltips = GD.Load<PackedScene>("res://Scenes/UI/Tooltips.tscn").Instantiate<Control>();
        tooltipLayer.AddChild(tooltips);
```

**Step 2: Build**

Run: `dotnet build Goose2ClientGodot.csproj`
Expected: **0 errors**. (Functional proof is the Task 9 visual check — tooltip floats above the inventory window.)

**Step 3: Commit**

```bash
git add Scripts/UI/GameHud.cs
git commit -m "fix(ui): render tooltips on a dedicated high CanvasLayer (above windows)"
```

---

## Task 2: Item tooltip gets a solid black background

**Why:** The tooltip Controls have no background node, so they're transparent. Add a black `Panel` background that follows the dynamically-sized content. The item tooltip grows with its stat lines, so its size is computed each frame from the stats VBox's combined minimum size; the spell/text/map tooltips are fixed-size, so they get fixed-size backgrounds.

**Files:**
- Modify: `Scenes/UI/Tooltips.tscn`
- Modify: `Scripts/UI/ItemTooltipControl.cs`

**Step 1: Add a StyleBoxFlat + Background panels to `Tooltips.tscn`**

In `Scenes/UI/Tooltips.tscn`, bump `load_steps` on line 1 from `6` to `7` and add a black stylebox sub-resource just after the `[ext_resource ...]` block (before the first `[node ...]`):

```
[sub_resource type="StyleBoxFlat" id="sb_tooltip_bg"]
bg_color = Color(0, 0, 0, 0.9)
corner_radius_top_left = 3
corner_radius_top_right = 3
corner_radius_bottom_right = 3
corner_radius_bottom_left = 3
```

Then add a `Background` Panel as the **first child** of each tooltip node (declaration order = draw order, so it must precede the content nodes). Insert immediately after each tooltip's `[node ...]` header line:

For `ItemTooltip` (anchored full-rect — its size is driven from code in Step 2):
```
[node name="Background" type="Panel" parent="ItemTooltip"]
layout_mode = 0
anchors_preset = 15
anchor_right = 1.0
anchor_bottom = 1.0
mouse_filter = 2
theme_override_styles/panel = SubResource("sb_tooltip_bg")
```

For `SpellTooltip`, `TextTooltip` (fixed 200×24 content), insert under each:
```
[node name="Background" type="Panel" parent="SpellTooltip"]
layout_mode = 0
offset_right = 200.0
offset_bottom = 24.0
mouse_filter = 2
theme_override_styles/panel = SubResource("sb_tooltip_bg")
```
(repeat with `parent="TextTooltip"`.)

For `MapItemTooltip` (content spans 200×40):
```
[node name="Background" type="Panel" parent="MapItemTooltip"]
layout_mode = 0
offset_right = 200.0
offset_bottom = 40.0
mouse_filter = 2
theme_override_styles/panel = SubResource("sb_tooltip_bg")
```

> Faithful alternative (optional, not required): import `Goose2Client/Assets/Resources/UI/tooltip.png` and use a `NinePatchRect` for true rounded-corner art. The `StyleBoxFlat` above satisfies the requirement ("black background") with no asset-import risk.

**Step 2: Size the item tooltip to its content so the full-rect background covers it**

In `Scripts/UI/ItemTooltipControl.cs`, the `ItemTooltip` Control currently has no explicit size, so a full-rect background would be zero-sized. Drive `Size` from the laid-out content in `_Process`, before positioning. Replace the body of `_Process` (`:54-63`):

```csharp
        public override void _Process(double delta)
        {
            if (_parent == null || !_parent.IsVisibleInTree())
            {
                Visible = false;
                return;
            }

            // Size to content so the full-rect Background panel covers icon + text.
            // Header block runs to y≈46 (Flags label bottom); StatsVBox starts at y=48
            // and its combined minimum size reflects only the visible stat lines.
            float statsHeight = _statsVBox.GetCombinedMinimumSize().Y;
            float height = Mathf.Max(46f, 48f + statsHeight) + 4f;
            Size = new Vector2(264f, height);

            PositionTooltip();
        }
```

**Step 3: Build**

Run: `dotnet build Goose2ClientGodot.csproj`
Expected: **0 errors**. (Visual check in Task 9: hover an item → opaque black panel sized to the text, nothing clipped, no large empty black area.)

**Step 4: Commit**

```bash
git add Scenes/UI/Tooltips.tscn Scripts/UI/ItemTooltipControl.cs
git commit -m "fix(ui): give tooltips a solid black background (item tooltip sizes to content)"
```

---

## Task 3: Restore dragging + the exit button on every window

**Why:** The Part 3 fix only neutralized the `Content` node. On Character, `Content/SlotGrid` is a full-rect PASS control that re-occludes the title bar (killing both drag and close). Rather than patch one scene, make the `TitleBar` the topmost sibling in `BaseWindow` so its drag zone and `CloseButton` always win — this fixes Character and hardens every window (including runtime-built ones) against this whole class of occlusion. The close handler (`OnClosePressed → Hide()`) already exists and is correct.

**Files:**
- Modify: `Scripts/UI/BaseWindow.cs` (`_Ready`, after the close-button wiring at `:62-63`)

**Step 1: Raise the TitleBar to the front in `BaseWindow._Ready`**

In `Scripts/UI/BaseWindow.cs`, after the close-button block (`:62-63`):

```csharp
        // Close button
        if (_closeButton != null)
            _closeButton.Pressed += OnClosePressed;
```

add:

```csharp
        // Keep the title bar (and its CloseButton) the topmost sibling so its drag region
        // and close button always receive clicks, even when a full-rect Content child
        // (e.g. CharacterWindow's SlotGrid) would otherwise occlude them. Sibling pick
        // order follows tree order; last child = drawn on top = picked first.
        if (_titleBar != null)
            MoveChild(_titleBar, GetChildCount() - 1);
```

**Step 2: Build**

Run: `dotnet build Goose2ClientGodot.csproj`
Expected: **0 errors**.

**Step 3: Commit**

```bash
git add Scripts/UI/BaseWindow.cs
git commit -m "fix(ui): raise TitleBar to front so drag + close work on all windows (incl. Character)"
```

> Verified in Task 9: Character window now drags by its title bar; the exit (X) button closes Inventory, Spellbook, **and** Character.

---

## Task 4: Left-align the vitals bar numbers

**Files:**
- Modify: `Scenes/UI/VitalsWindow.tscn:58,68`

**Step 1: Change HP/MP horizontal alignment to LEFT**

In `Scenes/UI/VitalsWindow.tscn`, in the `HpText` node change `horizontal_alignment = 1` (line 58) to `horizontal_alignment = 0`, and in the `MpText` node change `horizontal_alignment = 1` (line 68) to `horizontal_alignment = 0`. Leave `vertical_alignment = 1` on both (vertical centering inside the bar reads correctly).

**Step 2: Commit** (no build needed — pure scene change)

```bash
git add Scenes/UI/VitalsWindow.tscn
git commit -m "fix(ui): left-align vitals HP/MP value labels (match Unity)"
```

---

## Task 5: Inventory item count → top-right

**Why:** The stack-count label is centered in a box pinned to the slot's bottom-right. Unity right-aligns it at the top of the slot. `ItemSlot.tscn` is shared by Inventory/Vendor/Bank/CombineBag/Character, so this corrects the count everywhere.

**Files:**
- Modify: `Scenes/UI/ItemSlot.tscn:29-40` (the `Count` node)

**Step 1: Repin the `Count` node to the top-right and right-align text**

In `Scenes/UI/ItemSlot.tscn`, replace the `Count` node's anchor/offset/alignment block (`:29-40`) so it reads:

```
anchors_preset = 3
anchor_left = 1.0
anchor_top = 0.0
anchor_right = 1.0
anchor_bottom = 0.0
offset_left = -30.0
offset_top = 0.0
offset_right = -2.0
offset_bottom = 12.0
grow_horizontal = 0
grow_vertical = 1
horizontal_alignment = 2
vertical_alignment = 0
```

(Keep `layout_mode = 0`, `mouse_filter = 2`, `visible = false` on the lines above.)

**Step 2: Commit**

```bash
git add Scenes/UI/ItemSlot.tscn
git commit -m "fix(ui): align inventory item count to top-right of the slot"
```

---

## Task 6: Hotbar — slot frames, number labels, and properly-sized icons

**Why:** Hotbar slots have no background (empty slots invisible), no 1–0 number labels, and the icon uses `stretch_mode = KEEP` (native size, looks tiny). Add a slot background + a number label, and scale the icon to fill. The number mapping is pure logic → real unit test first.

**Files:**
- Modify: `Scripts/UI/HotbarSlot.cs`
- Modify: `Scenes/UI/HotbarSlot.tscn`
- Test: `tests/Goose2Client.Tests/UI/HotbarSlotTests.cs` (create)

**Step 1: Write the failing test for the slot-number mapping**

Create `tests/Goose2Client.Tests/UI/HotbarSlotTests.cs`:

```csharp
using Goose2Client.UI;
using Xunit;

namespace Goose2Client.Tests.UI;

public class HotbarSlotTests
{
    [Theory]
    [InlineData(0, "1")]
    [InlineData(1, "2")]
    [InlineData(8, "9")]
    [InlineData(9, "0")]
    public void SlotLabel_maps_index_to_hotkey_digit(int index, string expected)
    {
        Assert.Equal(expected, HotbarSlot.SlotLabel(index));
    }
}
```

> If `tests/...Tests/UI/` doesn't exist yet, create the folder. Match the test project's existing namespace convention if it differs from `Goose2Client.Tests.UI` (check a neighboring test file first).

**Step 2: Run it — expect failure**

Run: `dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj --filter HotbarSlotTests`
Expected: **FAIL** — `HotbarSlot` has no `SlotLabel` method (compile error).

**Step 3: Add the pure helper + wire the label in `HotbarSlot.cs`**

In `Scripts/UI/HotbarSlot.cs`:

(a) Replace the bare `SlotNumber` auto-property (`:20`) with a backing field + setter that updates the label, and add the pure helper + a label field:

Change the fields block (`:13-15`) to add `_slotNumberLabel`:
```csharp
        private TextureRect _icon;
        private Label _count;
        private Label _slotNumberLabel;
        private TextureProgressBar _cooldownOverlay;
```

Replace `public int SlotNumber { get; set; }` (`:20`) with:
```csharp
        private int _slotNumber;
        public int SlotNumber
        {
            get => _slotNumber;
            set { _slotNumber = value; if (_slotNumberLabel != null) _slotNumberLabel.Text = SlotLabel(value); }
        }

        /// <summary>Hotkey digit for a 0-based slot index: 9 → "0", otherwise index + 1.
        /// Matches the hotkey action mapping in HotbarWindow._Process.</summary>
        public static string SlotLabel(int index) => index == 9 ? "0" : (index + 1).ToString();
```

(b) In `_Ready` (`:43-51`), bind the label and apply the current value (it is assigned after instantiation, so the setter may have already run, but bind-and-set is safe):
```csharp
        public override void _Ready()
        {
            _icon = GetNode<TextureRect>("Icon");
            _count = GetNode<Label>("Count");
            _slotNumberLabel = GetNode<Label>("SlotNumber");
            _slotNumberLabel.Text = SlotLabel(_slotNumber);
            _cooldownOverlay = GetNode<TextureProgressBar>("CooldownOverlay");

            MouseEntered += OnMouseEntered;
            MouseExited += OnMouseExited;
        }
```

**Step 4: Run the test — expect pass**

Run: `dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj --filter HotbarSlotTests`
Expected: **PASS** (4 cases).

**Step 5: Overwrite `Scenes/UI/HotbarSlot.tscn` with background + number label + scaled icon**

Replace the whole file with (adds `Background` as first child, `SlotNumber` label, fixes `stretch_mode`, moves `Count` to top-right):

```
[gd_scene load_steps=3 format=3]

[ext_resource type="Script" path="res://Scripts/UI/HotbarSlot.cs" id="1_hotbar"]
[ext_resource type="Texture2D" path="res://Assets/UI/hotbar-slot-background.png" id="bg_tex"]

[node name="HotbarSlot" type="Panel"]
script = ExtResource("1_hotbar")
custom_minimum_size = Vector2(32, 32)
layout_mode = 0
mouse_filter = 0
offset_left = 0.0
offset_top = 0.0
offset_right = 32.0
offset_bottom = 32.0

[node name="Background" type="TextureRect" parent="."]
layout_mode = 0
mouse_filter = 2
expand_mode = 1
stretch_mode = 0
offset_left = 0.0
offset_top = 0.0
offset_right = 32.0
offset_bottom = 32.0
texture = ExtResource("bg_tex")

[node name="Icon" type="TextureRect" parent="."]
layout_mode = 0
mouse_filter = 2
expand_mode = 1
stretch_mode = 0
offset_left = 0.0
offset_top = 0.0
offset_right = 32.0
offset_bottom = 32.0

[node name="CooldownOverlay" type="TextureProgressBar" parent="."]
layout_mode = 0
mouse_filter = 2
visible = false
max_value = 1.0
value = 0.0
fill_mode = 4
tint_progress = Color(0, 0, 0, 0.5)
offset_left = 0.0
offset_top = 0.0
offset_right = 32.0
offset_bottom = 32.0

[node name="Count" type="Label" parent="."]
layout_mode = 0
mouse_filter = 2
visible = false
anchors_preset = 3
anchor_left = 1.0
anchor_top = 0.0
anchor_right = 1.0
anchor_bottom = 0.0
offset_left = -30.0
offset_top = 0.0
offset_right = -2.0
offset_bottom = 12.0
grow_horizontal = 0
grow_vertical = 1
horizontal_alignment = 2
vertical_alignment = 0

[node name="SlotNumber" type="Label" parent="."]
layout_mode = 0
mouse_filter = 2
anchors_preset = 0
offset_left = 17.0
offset_top = 18.0
offset_right = 31.0
offset_bottom = 31.0
horizontal_alignment = 2
vertical_alignment = 2
```

**Step 6: Fix the same `stretch_mode = KEEP` icon bug on `ItemSlot` and `SpellSlot`**

The inventory/spell/character/vendor/bank icons share the identical `stretch_mode = 2` (`Scenes/UI/ItemSlot.tscn:19`, `Scenes/UI/SpellSlot.tscn:19`). Change each to `stretch_mode = 0` (SCALE) so icons fill their slots like Unity (`m_PreserveAspect: 0`). Texture filter is already Nearest, so pixel-art stays crisp.

**Step 7: Build + full test run**

Run: `dotnet build Goose2ClientGodot.csproj` → **0 errors**
Run: `dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj` → **all pass**

**Step 8: Commit**

```bash
git add Scripts/UI/HotbarSlot.cs Scenes/UI/HotbarSlot.tscn Scenes/UI/ItemSlot.tscn Scenes/UI/SpellSlot.tscn tests/Goose2Client.Tests/UI/HotbarSlotTests.cs
git commit -m "fix(ui): hotbar slot frames + 1-0 number labels; scale slot icons to fill"
```

> Visual check (Task 9): empty hotbar slots show a frame; each slot shows its number 1…9,0; item/spell icons fill the cell. Confirm inventory/spell icons still look right after the stretch change.

---

## Task 7: Hide party slots until actually in a party

**Why:** The `Content` node defaults visible; nothing hides it until a `GroupUpdatePacket` arrives. Match Unity's prefab default (`Content` inactive) so empty slots stay hidden; the existing `OnGroupUpdate` toggle (`PartyMember.cs:27`) reveals them on a real party update.

**Files:**
- Modify: `Scenes/UI/PartyMember.tscn:18-23` (the `Content` node)
- Modify: `Scripts/UI/PartyMember.cs:16-22` (`_Ready`) — belt-and-suspenders

**Step 1: Default the `Content` node hidden in the scene**

In `Scenes/UI/PartyMember.tscn`, add `visible = false` to the `Content` node block (`:18-23`), e.g. directly under `layout_mode = 0`:

```
[node name="Content" type="Control" parent="."]
layout_mode = 0
visible = false
offset_left = 0.0
offset_top = 0.0
offset_right = 87.0
offset_bottom = 33.0
```

**Step 2: Also hide it in code (guards against scene drift)**

In `Scripts/UI/PartyMember.cs` `_Ready` (`:16-22`), after `_content = GetNode<Control>("Content");` add:

```csharp
        _content.Visible = false;
```

**Step 3: Build**

Run: `dotnet build Goose2ClientGodot.csproj` → **0 errors**.

**Step 4: Commit**

```bash
git add Scenes/UI/PartyMember.tscn Scripts/UI/PartyMember.cs
git commit -m "fix(ui): hide party member slots until a GroupUpdate with a real member arrives"
```

> Visual check (Task 9): no party frame under the vitals bar when not grouped.

---

## Task 8: Spell casting — diagnosis & scope decision (NOT a UI bug)

**This task changes no code by default — read and decide.**

**Finding (verified):** Spell *input* works — slots are clickable and double-click is detected. `SpellbookWindow.UseSpell` (`Scripts/UI/SpellbookWindow.cs:108-124`) routes:
- `TargetType.None` (self-cast) → `NetworkClient.CastSpell(...)` — **already works** (`:113-119`).
- Any targeted spell → `SpellTargetManager.Cast(info)` (`:122`), which is a **no-op stub** (`Scripts/SpellTargetManager.cs:10-13`). The instance is non-null, so the call silently does nothing → "casting does nothing" for the common case.

On-screen spell targeting is a substantial gameplay system (enter targeting mode, enumerate valid characters, draw a reticle, cycle/confirm/cancel, then cast). It is **already specified in detail** at `docs/plans/2026-06-07-step8-part1-correctness-and-foundations.md:538-629` (Task 9, "A2 — On-screen spell targeting"), faithful to Unity `SpellTargetManager.cs:164-237`.

**Recommendation:** Keep this Part-4 plan UI-focused and execute spell targeting via the existing Step 8 Task 9 (its own focused effort). Do **not** half-implement targeting here. If the user wants it folded in, pull Step 8 Task 9 forward as a dedicated branch rather than appending it to this plan.

**No commit.** (If, during Task 9, even *self-cast* spells do nothing, investigate whether `SpellbookSlotPacket`s are populating slots — that would be a data/server issue, not this port.)

---

## Task 9: Validation — build, tests, live visual checklist

**Files:**
- Modify (docs): `MIGRATION_PLAN.md` (record Part 4 landed + carry-forwards)

**Step 1: Final build + full test suite**

```bash
dotnet build Goose2ClientGodot.csproj
dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj
```
Expected: build **0 errors**; **all tests pass** (including the 4 new `HotbarSlot.SlotLabel` cases).

**Step 2: Live visual checklist (primary evidence)**

Launch the Godot editor/client against the live server and verify each item:

```bash
GOOSE_HOST=scyther.local GOOSE_PORT=2006   # (set however the project consumes these)
```

| # | Bug | Expected after fix |
|---|-----|--------------------|
| 1 | Tooltip z-order | Open inventory; hover an item → tooltip floats **above** the window, fully visible. |
| 2 | Tooltip background | Item tooltip has a **solid black** panel sized to its text (no transparency, no big empty area). |
| 3 | Vitals alignment | HP/MP numbers are **left-aligned** in their bars. |
| 4 | Inventory count | Stack count sits at the slot's **top-right**, right-aligned. |
| 5 | Hotbar frames | Empty hotbar slots show a **visible frame**. |
| 6 | Hotbar numbers | Each slot shows its number **1,2,…,9,0**. |
| 7 | Hotbar icons | Item/spell icons **fill** the 32px cell (not tiny). |
| 8 | Inventory/spell icons | Still look correct after the stretch change (regression check). |
| 9 | Party bar | **No** party frame under the vitals when not in a party. |
| 10 | Character drag | Character window **drags** by its title bar. |
| 11 | Exit button | The X closes **Inventory, Spellbook, and Character**. |
| 12 | Self-cast spell | A `None`-target spell double-click sends a cast (per Task 8). Targeted spells remain inert pending Step 8 Task 9. |

**Step 3: Update `MIGRATION_PLAN.md`**

Record: "UI Windows Part 4 landed — tooltip layering + black background, window drag/close hardening (TitleBar-to-front), vitals/inventory alignment, hotbar slot frames + number labels + icon scaling, party-slot gating. Carry-forward: on-screen spell targeting tracked in Step 8 Task 9."

**Step 4: Commit**

```bash
git add MIGRATION_PLAN.md
git commit -m "docs(migration): record UI Windows Part 4 interaction/visual fixes"
```

**Step 5: Merge**

Open a PR (or fast-forward merge) `fix/ui-windows-part4` → `master` once the checklist passes.

---

## Notes & known limitations

- **Spell targeting is deliberately out of scope** (Task 8) — it's a gameplay system already planned as Step 8 Task 9, not a UI-polish bug. Self-cast already works.
- **Tooltip background is a flat black `StyleBoxFlat`**, not the Unity rounded sprite `tooltip.png`. It satisfies "black background"; a `NinePatchRect` with the imported sprite is the faithful upgrade if desired.
- **Hotbar stays non-draggable** (fixed bottom-center dock from Part 3) — unchanged here; we only populated its slots.
- **Item-count fix is global** — `ItemSlot.tscn` is shared, so vendor/bank/combine-bag/character counts also move to top-right (correct, matches Unity).
- **`TitleBar`-to-front** is a one-line general fix in `BaseWindow`; it supersedes a per-scene `SlotGrid` mouse_filter patch and protects future/runtime windows from the same occlusion class.
- **`GetCombinedMinimumSize()`** drives the item-tooltip height each frame; if the black panel ever looks slightly short/tall against a font change, adjust the `+4f` padding / `48f` stats offset in `ItemTooltipControl._Process`.
- **Uncommitted `project.godot`** (a `[gui]`/whitespace reordering) is unrelated to this work — leave it or revert it separately; do not bundle it into these commits.
```
