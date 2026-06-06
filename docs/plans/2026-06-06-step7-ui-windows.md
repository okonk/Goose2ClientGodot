# Step 7: UI Windows (§6) Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Port the ~40 Unity uGUI windows to Godot `Control` nodes — first the shared primitives (window base, slots, drag/drop, tooltips, data models), then every individual window — wired onto the persistent `GameManager.UiLayer` CanvasLayer and driven by the already-ported packet layer.

**Architecture:** Each window is a `.tscn` scene + a `partial class : Control` script in `Scripts/UI/`. A `GameHud : Control` root (mounted under `GameManager.UiLayer` on map entry) owns the always-present windows (vitals, inventory, hotbar, spellbook, character, chat, party, buffs, toolbar, debug, options) and the managers for server-spawned windows (vendor, bank, combine, quest, info). Item/spell drag-drop uses Godot's built-in `_GetDragData`/`_CanDropData`/`_DropData` (replacing Unity's custom `DragIcon`/`DropTarget`/`DropTargetManager`). Tooltips are a single `TooltipManager` autoload-style node holding four tooltip Controls that follow the mouse in `_Process`. All pure logic (tooltip text building, chat command parsing, stack-split math, cooldown math) lives in Godot-free classes that are unit-tested under `tests/Goose2Client.Tests/`.

**Tech Stack:** Godot 4.6 + C#/.NET (`net10.0`), xUnit tests, `System.Text.Json` for settings. Namespace `Goose2Client` (UI types in `Goose2Client.UI`).

---

## APIs verified (cited from source)

All of these were read and confirmed before writing this plan. Implementers should trust these citations over memory.

**Already exist in the Godot project — call directly:**
- `GameManager.Instance` singleton; `NetworkClient`, `PacketManager`, `UiLayer` (CanvasLayer), `CharacterSettings`, `Classes` (`Dictionary<int,string>`), `CurrentMap`, `SetPaused(bool)`, `LoadSettings(string)` — `Scripts/GameManager.cs:11,13,14,19,22,25,28,67,103`.
- `PacketManager.Listen<T>(Action<object>)` / `Remove<T>(Action<object>)` — `Scripts/Network/PacketManager.cs:12`. Callback receives `object`, cast to packet type. Real usage: `Scripts/MapManager.cs:43-56,81`.
- `NetworkClient` send helpers (ALL exist, `Scripts/Network/NetworkClient.cs`): `UseItem(int slot)`:170, `MoveItemInInventory(int,int)`:175, `SplitStackInInventory(int,int,int)`:180, `MoveInventoryToWindow(int,int,int)`:185, `MoveWindowToInventory(int,int,int)`:190, `MoveWindowToWindow(int,int,int,int)`:195, `Drop(int,int)`:200, `Pickup()`:205, `MoveSpell(int,int)`:210, `CastSpell(int,int)`:215, `Quit()`:220, `KillBuff(int)`:225, `OpenCombineBag()`:230, `WindowButtonClick(WindowButtons,int,int,int,int)`:235, `VendorPurchaseItem(int,int)`:240, `VendorSellItem(int,int,int)`:245, `LeftClick(int,int)`:250, `RightClick(int,int)`:255, `ChatMessage(string)`:260, `Command(string)`:265, `DestroyItem(int)`:275, `DestroySpell(int)`:280.
- `SpriteCache.Get(int sheet, int graphic) → AtlasTexture` (null when sheet==0/missing) — `Scripts/Map/SpriteCache.cs:23`. Sheets at `res://Assets/Sprites/sheets/{sheet}.png`.
- Enums already ported in `Scripts/Constants.cs`: `ItemUseType`, `ItemSlots`, `ItemMaterial`, `ItemSlotType`, `ItemFlags`, `ChatType`, `SpellTargetType`, `CharacterType`, `Options.TargetFiltering`. `Constants.SpellbookSlotsPerPage = 30`.
- `WindowFrames` enum (`Scripts/WindowFrames.cs`): `Inventory=2, Spellbook=3, Hotbar=4, Buffbar=5, Equipped=11, Chat=12, Vendor=13, Party=14, TenSlot=19, Quest=20, GenericInfo=22, Bank=26` (verify exact values in file).
- `WindowButtons` enum (`Scripts/WindowButtons.cs`): `Exit=0, Combine, Close, Back, Next, OK`.
- `CharacterSettings` (`Scripts/CharacterSettings.cs`): `HotkeySetting[] Hotkeys`, `Dictionary<string,WindowSettings> WindowSettings`, `Dictionary<string,object> Options`, `string MountName`; `WindowSettings { Vector2 Position }`; `HotkeySetting { int SlotNumber; SlotType Type }`.
- The tint shader pattern (reuse for item/spell icons) — `Scripts/Character/Character.cs:205-214` (`mix(tex.rgb, tint.rgb, tint.a)`); "no tint" = alpha 0.
- Label styling pattern (name labels) — `Scripts/Character/Character.cs:50-64`.
- InputMap actions already defined in `project.godot`: `ToggleInventory`, `ToggleSpellbook`, `ToggleCharacterWindow`, `ToggleMount`, `CycleHotbarPage`, `StartChat`, `SlashCommand`, `GuildCommand`, `TellCommand`, `ReplyCommand`, plus `TargetDown/Up/ConfirmTarget/CancelTarget`, emotes.

**Packet shapes (verified field names):**
- `InventorySlotPacket` (`Prefix "SIS"`) carries the full item record incl. `SlotNumber, GraphicId, GraphicFile, Title, Name, Surname, StackSize, Value, Flags, Description, MinDamage, MaxDamage, Delay, MaterialType, AC, HP, MP, SP, Strength, Stamina, Intelligence, Dexterity, *Resist, MinLevel, MaxLevel, ClassRestrictions1..3, Access, Gender, SpellEffect, SpellEffectChance, SlotType, UseType, GraphicR/G/B/A` — `Scripts/Network/Packets/InventorySlotPacket.cs`. **`SlotNumber` is already 0-based (`-1` applied in Parse, line 60).**
- `VendorSlotPacket` (`"SVS"`) and `BankSlotPacket` (`"SBS"`) **extend** `InventorySlotPacket` (same fields). `CombineBagSlotPacket` similar.
- `ClearInventorySlotPacket`/`ClearVendorPacket`/`ClearBankSlotPacket`/`ClearCombineBagSlotPacket` — carry `SlotNumber`.
- `StatusInfoPacket` (`"SNF"`): `GuildName, ClassName, Level, MaxHP/MP/SP (long), CurrentHP/MP/SP (long), Strength, Stamina, Intelligence, Dexterity, ArmorClass, *Resist, Gold (long)`.
- `SpellbookSlotPacket` (`"SSS"`): `SlotNumber, Name, AnimationId, SpellIndex, TargetType (SpellTargetType), GraphicId, GraphicFile, Cooldown (long ms)`.
- `BuffBarPacket` (`"BUF"`): `SlotNumber, GraphicId, GraphicFile, Name`.
- `GroupUpdatePacket` (`"GUD"`): `LineNumber, LoginId, Name, LevelClassName`.
- `VitalsPercentagePacket` (`"VPU"`): `LoginId, HPPercentage (float), MPPercentage (float)`.
- `ExperienceBarPacket` (`"TNL"`): `Percentage (float), Experience, ExperienceToNextLevel, ExperienceSold (long)`.
- `ChatPacket` (`"^"`): `LoginId, Message`. `TellPacket` (`"&"`): `Name, IsAfk, Message`. `ServerMessagePacket` (`"$"`): `ChatType, Message`. `HashMessagePacket`: `Message`.
- `MakeWindowPacket` (`"MKW"`): `WindowId, WindowFrame (WindowFrames), Title, bool[5] Buttons, NpcId, Unknown1, Unknown2`. `EndWindowPacket` (`"ENW"`): `WindowId`. `WindowLinePacket` (`"WNF"`): `WindowId, LineNumber, Text, StackSize, ItemId, GraphicSheet, GraphicId, GraphicR/G/B/A`.
- `CastPacket` (`"CST"`): `LoginId`.

**Verified MISSING (built in Task 0 prerequisites):** `ItemStats`, `SpellInfo`, `Helpers`, `SpellCooldownManager`, `SpellTargetManager` (stub), a shared `SpriteCache` accessor, `GameManager.IsTargeting`, a local-player accessor, `MapManager.GetCharacter`/`MyLoginId` (currently `_characters` + `_myLoginId` are private — `Scripts/MapManager.cs:17-19`).

**Reference source (Unity, port-from):** `~/code/Goose2Client/Assets/Scripts/UI/*.cs` and `~/code/Goose2Client/Assets/Scripts/{Helpers,ItemStats,SpellInfo,SpellCooldownManager}.cs`. Cited inline per task.

---

## Conventions for every task

- **Namespace:** `Goose2Client.UI` for windows/slots/tooltips; `Goose2Client` for data models/helpers (matches `ItemStats` etc.). File-scoped `namespace X;`.
- **Listeners:** register in `_Ready()`, ALWAYS `Remove<T>` the same delegate in `_ExitTree()` (pattern: `Scripts/MapManager.cs:43-75`). Store the `Action<object>` callbacks as methods, not lambdas, so they can be removed.
- **Sprites:** UI icons come from a shared `SpriteCache` via `GameManager.Instance.Sprites.Get(file, id)` (added in Task 0). Apply tint with the shared `IconTint` shader material (Task 2) only when `a > 0`.
- **Mount target:** every window node is added under the `GameHud` (Task 25), itself under `GameManager.Instance.UiLayer`.
- **Tests:** pure logic only (no `Node`/`Control`), under `tests/Goose2Client.Tests/`, xUnit, added to the `.csproj` `<Compile Include>` list. Run: `dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj`.
- **Build check:** after scene/script tasks that can't be unit-tested, run `dotnet build Goose2ClientGodot.csproj` (or the solution) and confirm 0 errors before commit.
- **Commit** after each task.

---

# PHASE 0 — Prerequisites

### Task 0a: Port `ItemStats` data model

**Files:**
- Create: `Scripts/ItemStats.cs`
- Test: `tests/Goose2Client.Tests/ItemStatsTests.cs`

**Step 1: Write the failing test**

```csharp
using Goose2Client;
using Goose2Client.Network.Packets;
using Xunit;

public class ItemStatsTests
{
    [Fact]
    public void FromPacket_copies_core_fields()
    {
        var p = new InventorySlotPacket { SlotNumber = 5, GraphicId = 101, GraphicFile = 7,
            Name = "Sword", StackSize = 3, MaxDamage = 10, UseType = ItemUseType.Weapon };
        var s = ItemStats.FromPacket(p);
        Assert.Equal(5, s.SlotNumber);
        Assert.Equal(101, s.GraphicId);
        Assert.Equal("Sword", s.Name);
        Assert.Equal(3, s.StackSize);
        Assert.Equal(ItemUseType.Weapon, s.UseType);
    }
}
```

**Step 2: Run, verify it fails** (`ItemStats` not defined).
`dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj --filter ItemStatsTests`

**Step 3: Implement** — port verbatim from `~/code/Goose2Client/Assets/Scripts/ItemStats.cs` (a plain POCO with all 40+ properties + `static ItemStats FromPacket(InventorySlotPacket)`). Add the second overload `FromPacket(MapObjectPacket)` ONLY if `MapObjectPacket` carries the same item fields (check `Scripts/Network/Packets/MapObjectPacket.cs`; if the fields differ, port only the inventory overload and note it). Namespace `Goose2Client`. No Godot types.

**Step 4:** Add `../../Scripts/ItemStats.cs` and the new test to the `.csproj` `<Compile Include>` list. Run the test → PASS.

**Step 5: Commit** `feat(ui): port ItemStats data model`

---

### Task 0b: Port `SpellInfo` data model

**Files:** Create `Scripts/SpellInfo.cs`; Test `tests/Goose2Client.Tests/SpellInfoTests.cs`

Port verbatim from `~/code/Goose2Client/Assets/Scripts/SpellInfo.cs`: properties `SlotNumber, Name, TargetType, GraphicId, GraphicFile, Cooldown (TimeSpan)`; `static SpellInfo FromPacket(SpellbookSlotPacket)` with `Cooldown = TimeSpan.FromMilliseconds(packet.Cooldown)`. Test: `FromPacket` maps `Cooldown=2000` → `TimeSpan.FromSeconds(2)` and copies `Name`/`TargetType`. Add to `.csproj`. Commit `feat(ui): port SpellInfo data model`.

---

### Task 0c: Port `Helpers` (duration format + stack split)

**Files:** Create `Scripts/Helpers.cs`; Test `tests/Goose2Client.Tests/HelpersTests.cs`

Port from `~/code/Goose2Client/Assets/Scripts/Helpers.cs` but adapt for Godot:
- `static string FormatDuration(this TimeSpan t)` — copy verbatim (pure logic).
- `static int GetStackSplitAmount(int initialStack)` — copy logic, but replace Unity `Keyboard.current.ctrlKey/shiftKey` with Godot `Input.IsKeyPressed(Key.Ctrl)` / `Input.IsKeyPressed(Key.Shift)`. **Because `Input` is a Godot static, split this:** put the pure rule in a testable `static int StackSplit(int initialStack, bool ctrl, bool shift)` and have `GetStackSplitAmount` call it with the live `Input` state. Rule: `initialStack==1 → 1`; `ctrl → 1`; `shift → initialStack/2`; else `initialStack`.
- DROP `GetSprite` (Unity ResourceManager) — UI uses `SpriteCache` instead (Task 2).

**Test** (`StackSplit` only — Godot-free): `StackSplit(1,false,false)==1`, `StackSplit(10,true,false)==1`, `StackSplit(10,false,true)==5`, `StackSplit(10,false,false)==10`. Add `Helpers.cs` (the `StackSplit` part — keep the `Input`-touching wrapper in a `#if !GODOT_TEST`-free separate region or guard the file so the test compiles; simplest: put `StackSplit` + `FormatDuration` in `Scripts/Helpers.cs` with NO `using Godot`, and the `Input`-reading `GetStackSplitAmount` in a separate `Scripts/Helpers.Godot.cs` partial that is NOT compiled into the test project). Commit `feat(ui): port Helpers (FormatDuration + stack split)`.

---

### Task 0d: Port `SpellCooldownManager`

**Files:** Create `Scripts/SpellCooldownManager.cs`; Test `tests/Goose2Client.Tests/SpellCooldownManagerTests.cs`

Port verbatim from `~/code/Goose2Client/Assets/Scripts/SpellCooldownManager.cs` (pure C#, uses `DateTimeOffset.UtcNow`, no Unity types): `Dictionary<int,DateTimeOffset> lastCastTimes`; `TimeSpan GetCooldownRemaining(SpellInfo)`, `void Swap(int,int)`, `void Cast(int)`, `void Clear(int)`.

**Test:** a spell with `SlotNumber=1, Cooldown=TimeSpan.FromHours(1)`: before `Cast` → `GetCooldownRemaining == Zero`; after `Cast(1)` → remaining `> TimeSpan.FromMinutes(59)`; after `Clear(1)` → `Zero`. `Swap` moves a cast time from slot to slot. Add to `.csproj`. Commit `feat(ui): port SpellCooldownManager`.

---

### Task 0e: GameManager + MapManager surface additions

**Files:** Modify `Scripts/GameManager.cs`, `Scripts/MapManager.cs`; Create `Scripts/SpellTargetManager.cs`

Add to `GameManager` (the managers/accessors windows need):
- `public SpriteCache Sprites { get; private set; }` — initialize in `_Ready()`: `Sprites = new SpriteCache();` (after the existing setup). This is the shared UI/icon sprite cache.
- `public SpellCooldownManager SpellCooldownManager { get; } = new();`
- `public SpellTargetManager SpellTargetManager { get; private set; }` — `new SpellTargetManager()` in `_Ready()`.
- `public bool IsTargeting => SpellTargetManager?.IsTargeting ?? false;`
- `public MapManager CurrentMapManager { get; set; }` — set by `MapManager._Ready()` (`GameManager.Instance.CurrentMapManager = this;`) and cleared in its `_ExitTree`.
- `public void Quit()` → `GetTree().Quit();` (used by Toolbar Exit).

Add to `MapManager` (currently `_characters` + `_myLoginId` are private, `Scripts/MapManager.cs:17-19`):
- `public int MyLoginId => _myLoginId;`
- `public Character.Character GetCharacter(int loginId) => _characters.TryGetValue(loginId, out var c) ? c : null;`
- `public Character.Character LocalPlayer => GetCharacter(_myLoginId);`
- In `_Ready()` set `GameManager.Instance.CurrentMapManager = this;`.

Create `Scripts/SpellTargetManager.cs` as a **minimal stub** (full targeting is Step 8 polish): `public bool IsTargeting { get; private set; }` and `public void Cast(SpellInfo info) { /* TODO Step 8: enter targeting mode */ }`. Mark with a `// TODO(step8)` comment. This satisfies `SpellbookWindow`'s delegation without implementing targeting now.

No unit test (Godot `Node` surface). **Step: `dotnet build` → 0 errors.** Commit `feat(ui): add SpriteCache/cooldown/target accessors to GameManager + MapManager`.

---

# PHASE A — Primitives

### Task 1: `IconTint` shared shader material + `Goose2Client.UI` icon helper

**Files:** Create `Scripts/UI/Icon.cs`

A tiny static used by every slot/tooltip to render an item/spell icon into a `TextureRect`:

```csharp
using Godot;
namespace Goose2Client.UI;

public static class Icon
{
    private static Shader _tintShader;
    // Faithful to Character.cs:205-214 — tint.a is a BLEND factor, not opacity.
    private static Shader TintShader => _tintShader ??= new Shader { Code = @"shader_type canvas_item;
uniform vec4 tint : source_color = vec4(0.0);
void fragment() { vec4 t = texture(TEXTURE, UV); COLOR = vec4(mix(t.rgb, tint.rgb, tint.a), t.a) * COLOR; }" };

    /// <summary>Show graphic (file,id) tinted by rgba (0-255) in the TextureRect, or hide it.</summary>
    public static void Apply(TextureRect rect, int file, int id, int r, int g, int b, int a)
    {
        var tex = GameManager.Instance.Sprites.Get(file, id);
        rect.Texture = tex;
        rect.Visible = tex != null;
        rect.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
        if (tex == null) return;
        if (a > 0)
        {
            if (rect.Material is not ShaderMaterial m) rect.Material = m = new ShaderMaterial { Shader = TintShader };
            m.SetShaderParameter("tint", new Color(r / 255f, g / 255f, b / 255f, a / 255f));
        }
        else rect.Material = null;
    }

    public static void Clear(TextureRect rect) { rect.Texture = null; rect.Visible = false; rect.Material = null; }
}
```

No unit test (Godot types). `dotnet build` → 0 errors. Commit `feat(ui): icon tint helper`.

---

### Task 2: `BaseWindow` scene + script (frame, title-bar drag, hover transparency, show/hide, persisted position)

Replaces Unity `TitleBar`, `WindowTransparency`, `BackgroundTransparency`, `IWindow`. (`NonDrawingGraphic`, `DragIcon`, `DropTargetManager` are DROPPED — Godot has built-in equivalents.)

**Files:** Create `Scenes/UI/BaseWindow.tscn`, `Scripts/UI/BaseWindow.cs`, `Scripts/UI/IWindow.cs`

`IWindow.cs`:
```csharp
namespace Goose2Client.UI;
public interface IWindow { WindowFrames WindowFrame { get; } int WindowId { get; } }
```

`BaseWindow.cs` (`partial class BaseWindow : Control`):
- Exports/children: a `Panel`/`NinePatchRect` background, a `TitleBar` Control (top strip) holding a `Label TitleLabel` and a close `Button`, and a `Control Content` container where subclasses add their widgets.
- `[Export] public string WindowName` — settings key for persisted position.
- **Title-bar drag:** on `TitleBar`, handle `_GuiInput`: when `InputEventMouseButton Left` pressed start drag, on `InputEventMouseMotion` while dragging do `Position += motion.Relative`. On release, persist via `GameManager.Instance.CharacterSettings.WindowSettings[WindowName] = new WindowSettings{ Position = Position }` (mirror Unity `TitleBar.OnEndDrag`). Guard `WindowName != null`.
- **Restore position** in `_Ready()` from `CharacterSettings.WindowSettings` if present (mirror Unity `TitleBar.Awake`).
- **Hover transparency** (Unity `WindowTransparency`): `MouseEntered → Modulate = new Color(1,1,1,1)`; `MouseExited → Modulate = new Color(1,1,1,0.7f)`. Use `Modulate` (whole-window alpha is acceptable here; Unity used a CanvasGroup).
- `public void Toggle() => Visible = !Visible;` `public void Show()/Hide()` via `Visible`.
- Close button → `Hide()` (server-spawned windows override to send `WindowButtonClick(Close,...)`).

Build a clean `.tscn` with the node tree above. No unit test. `dotnet build` → 0 errors; open scene in editor to confirm tree. Commit `feat(ui): BaseWindow (title drag, transparency, persisted position)`.

---

### Task 3: `TooltipManager` + the four tooltips (text builders unit-tested)

Replaces `TooltipManager`, `ItemTooltip`, `SpellTooltip`, `TextTooltip`, `MapItemTooltip`, `TextTooltipEventHandler`.

**Files:** Create `Scripts/UI/ItemTooltipText.cs` (pure), `Scripts/UI/TooltipManager.cs`, `Scenes/UI/Tooltips.tscn`; Test `tests/Goose2Client.Tests/ItemTooltipTextTests.cs`

**Step 1 — pure text builder (the hard part of `ItemTooltip`).** Port the stat-line construction from `~/code/Goose2Client/Assets/Scripts/UI/ItemTooltip.cs:39-135` into a Godot-free function returning a list of `(string text, Color color)` — but represent color as a small enum/struct so it stays Godot-free, OR use `System.Drawing`-free `(string, ItemTooltipColor)` enum and map enum→Godot Color in the Control. Define `ItemTooltipColor { Description, AC, WeaponDamage, HpMp, Stat, Resistance, Requirement, Effect, Value }` with the RGB values from `ItemTooltip.cs:24-32`. Function signature:
```csharp
public static List<(string Text, ItemTooltipColor Color)> Build(ItemStats s, Func<int,string> className);
```
Faithfully reproduce, in order: description; `"{Min}-{Max} Damage / {Delay/10.0f}s Delay"` when `MaxDamage!=0`; `"{AC} Armor"`; HP/MP/SP lines; stat bonuses (`+{v} {Stat}`); elemental resistances; class restrictions (`ClassRestrictions1/2/3` via `className`); level reqs (`Requires level {Min} to {Max}`); spell effects (split `;`, show chance when `!=100`); blank separator; value (`"No Value"` or `"{Value:N0} {gold|credits}"`, credits when `Flags.HasFlag(ItemFlags.Donation)`). Include the type/material/slot/flag text helpers (`ItemTooltip.cs:173-230`).

**Step 2 — test it.** Assert ordering and text for: a weapon (damage line present, correct delay formatting), armor (AC line), a stacked potion (value line "N gold"), a donation item (value "credits"), class-restricted item. ~6 facts. Add to `.csproj`.

**Step 3 — Controls.** `Tooltips.tscn` holds four `Control`s (Item, Spell, Text, MapItem). `TooltipManager : Control` (mounted under HUD, high `z`/CanvasLayer-top) exposes:
```csharp
void ShowItemTooltip(ItemStats stats, Control parent);  void HideItemTooltip();
void ShowSpellTooltip(SpellInfo spell, Control parent);  void HideSpellTooltip();
void ShowMapItemTooltip(ItemStats stats, Control parent); void HideMapItemTooltip(); void HideMapItemTooltipIfMatching(ItemStats stats);
void ShowTextTooltip(string text, Control parent);        void HideTextTooltip();
```
Mirror Unity `TooltipManager.cs:37-86`. Each tooltip in `_Process`: if `parent==null || !parent.IsVisibleInTree()` → hide; else position at `GetGlobalMousePosition()` with the edge-pivot adjustment from `ItemTooltip.cs:141-160` (default top-right; flip to top-left when overflowing left; shift up when overflowing bottom). Item tooltip renders the icon (Task 1 `Icon.Apply`) + the `ItemTooltipText.Build` lines (color via enum map). Spell tooltip shows `"{Name}"` or `"{Name} ({remaining} remaining)"` using `GameManager.Instance.SpellCooldownManager.GetCooldownRemaining(spell).FormatDuration()`, refreshed every 0.5s (`SpellTooltip.cs:29-71`). Provide a `TooltipManager.Instance` static (set in `_Ready`).

`TextTooltipEventHandler` → a reusable helper: any Control wanting a text tooltip connects `mouse_entered`/`mouse_exited` to `ShowTextTooltip(text, self)` / `HideTextTooltip()`.

Commit `feat(ui): TooltipManager + item/spell/text/mapitem tooltips`.

---

### Task 4: `ItemSlot` control + drag/drop

**Files:** Create `Scenes/UI/ItemSlot.tscn`, `Scripts/UI/ItemSlot.cs`. Port from `~/code/Goose2Client/Assets/Scripts/UI/ItemSlot.cs`.

`ItemSlot : Panel` (or `Control`) holds a `TextureRect Icon` + `Label Count`. Public surface mirroring Unity:
- `ItemStats Stats { get; }`, `bool HasItem => Stats != null`, `int StackSize`, `int SlotNumber {get;set;}`, `IWindow Window {get;set;}`.
- `Action<ItemStats> OnDoubleClick`, `Action<IWindow,int,int> OnDropItem` (fromWindow, fromSlot, toSlot).
- `void SetItem(ItemStats)` → `Icon.Apply(Icon, GraphicFile, GraphicId, GraphicR..A)`, count text visible when `StackSize>1`.
- `void ClearItem()` → `Icon.Clear`, hide count, keep node interactive.
- Hover → `TooltipManager.Instance.ShowItemTooltip(Stats, this)` / hide (`ItemSlot.cs:48-56`).
- Double-click (left, `clickCount>=2`) → `OnDoubleClick?.Invoke(Stats)` (`ItemSlot.cs:59-65`).

**Drag/drop via Godot built-ins (replaces DragIcon/DropTarget plumbing):**
- `override Variant _GetDragData(Vector2 pos)`: if `!HasItem` return default; set a drag preview `TextureRect` (copy of icon) via `SetDragPreview`; return a `Godot.Collections.Dictionary` `{ "kind":"item", "slot": this }` (store a ref to this slot, e.g. via `Variant.From(this)`).
- `override bool _CanDropData(Vector2 pos, Variant data)`: true when data kind is `item` (or spell→item not allowed). 
- `override void _DropData(Vector2 pos, Variant data)`: get source `ItemSlot`; if `src.HasItem` → `OnDropItem?.Invoke(src.Window, src.SlotNumber, SlotNumber)`.

(The "drop on world to drop item" + "drop on destroy button" cases are Tasks 6/7.) Commit `feat(ui): ItemSlot control with built-in drag/drop`.

---

### Task 5: `SpellSlot` control + cooldown overlay

**Files:** Create `Scenes/UI/SpellSlot.tscn`, `Scripts/UI/SpellSlot.cs`. Port from `~/code/Goose2Client/Assets/Scripts/UI/SpellSlot.cs`.

`SpellSlot : Panel` holds `TextureRect Icon` + a radial cooldown overlay (`TextureProgressBar` in radial mode, or a `ColorRect` with a shader; simplest: `TextureProgressBar` `FillMode = ClockwiseAndCounterClockwise`). Public surface:
- `SpellInfo Info {get;}`, `bool HasSpell`, `int SlotNumber {get;set;}`, `IWindow Window {get;set;}`.
- `Action<SpellInfo> OnDoubleClick`, `Action<int,int> OnMoveSpell` (fromSlot, toSlot).
- `SetSpell(SpellInfo)` — if `Name` empty → `ClearSpell`; else icon (no tint, white). `ClearSpell()` hides icon but keeps interactive.
- Hover → spell tooltip. Double-click → if on cooldown (`GetCooldownRemaining > Zero`) do nothing, else `OnDoubleClick?.Invoke(Info)` (`SpellSlot.cs:59-70`).
- `_Process`: update overlay fill = `remaining.TotalMs / Info.Cooldown.TotalMs` (`SpellSlot.cs:87-101`).
- Drag/drop: `_GetDragData` returns `{kind:"spell", slot:this}` when `HasSpell`; `_DropData` from another spell slot → `OnMoveSpell?.Invoke(src.SlotNumber, SlotNumber)`.

Commit `feat(ui): SpellSlot control with cooldown overlay`.

---

### Task 6: `HotbarSlot` control (item OR spell, swap logic)

**Files:** Create `Scenes/UI/HotbarSlot.tscn`, `Scripts/UI/HotbarSlot.cs`. Port from `~/code/Goose2Client/Assets/Scripts/UI/HotbarSlot.cs` (204 LOC — the densest primitive).

`HotbarSlot : Panel` can hold **either** an item or a spell. Public surface per Unity:
- `Action<int> OnUseSlot`, `int SlotNumber`, `IWindow Window`, `int itemSlot=-1`, `int spellSlot=-1`, `ItemStats ItemStats`, `SpellInfo SpellInfo`, `bool IsEmpty`, `bool CanUse()`.
- `SetItem(ItemStats)`, `SetSpell(SpellInfo)`, `Clear(int keepItemSlot=-1, int keepSpellSlot=-1)`.
- Sync hooks: `OnInventorySlot(ItemStats)` (update when `itemSlot==stats.SlotNumber`), `OnClearInventorySlot(int)` (clear keeping slot link), `OnSpellbookSlot(SpellInfo)`.
- `SaveSlots()` → `((HotbarWindow)Window).SaveSlotsDelayed()`.
- Double-click → `OnUseSlot?.Invoke(SlotNumber)`. `_Process` cooldown overlay like SpellSlot.
- **`_DropData` four-way** (`HotbarSlot.cs:48-87`): source SpellSlot → `SetSpell`+save; source ItemSlot (window Inventory or Equipped) → `SetItem`+save; source HotbarSlot → swap contents both ways + save; else ignore. `_GetDragData` returns `{kind:"hotbar", slot:this}` when `!IsEmpty`.
- Hover → spell tooltip (spell) or text tooltip with item name (item).

**Pure-logic extraction + test:** the swap decision is fiddly; extract `HotbarSwap.Resolve(...)` returning what each slot should hold, and unit-test the swap (item↔spell, item↔empty, spell↔spell). Add to `.csproj`. Commit `feat(ui): HotbarSlot (item/spell, swap) + swap test`.

---

### Task 7: World drop target + `DestroyButton`

**Files:** Create `Scripts/UI/WorldDropTarget.cs`, `Scripts/UI/DestroyButton.cs` (+ tiny scenes or nodes in HUD).

- `WorldDropTarget : Control` (a full-screen-behind-windows drop region, mouse_filter Pass): `_CanDropData` true for item/hotbar kinds; `_DropData`: item from Inventory window → `NetworkClient.Drop(src.SlotNumber, Helpers.GetStackSplitAmount(src.StackSize))` (`DropTarget.cs:11-26`); hotbar → `src.Clear()`.
- `DestroyButton : Button` (or Panel) `_DropData` (`DestroyButton.cs:11-33`): item from Inventory → `NetworkClient.DestroyItem(src.SlotNumber)`; spell → `NetworkClient.DestroySpell(src.SlotNumber)`; hotbar → `src.Clear()`.

Commit `feat(ui): world drop target + destroy button`.

---

# PHASE B — Windows (fan-out; each is independent once primitives exist)

> Each window: build `Scenes/UI/<Name>.tscn` (root extends `BaseWindow` where it has a frame) + `Scripts/UI/<Name>.cs`. Register listeners in `_Ready`, remove in `_ExitTree`. Verify against the cited Unity file. After each: `dotnet build` 0 errors + commit.

### Task 8: `VitalsWindow`
Port `~/code/Goose2Client/Assets/Scripts/UI/VitalsWindow.cs`. Display-only HP/MP bars + HP/MP/Level text + per-bar text tooltips.
- **Listens:** `StatusInfoPacket` → set HP bar fill `CurrentHP/MaxHP`, MP fill `CurrentMP/MaxMP`, level text. **Resolves the Step-6 "MP bar" follow-up.**
- **Sends:** none. No targeting interactions.
Commit `feat(ui): VitalsWindow`.

### Task 9: `InventoryWindow`
Port `~/code/Goose2Client/Assets/Scripts/UI/InventoryWindow.cs`. `WindowFrame=Inventory`. Grid of `ItemSlot`s + gold `Label`.
- **Listens:** `InventorySlotPacket` → `slots[p.SlotNumber].SetItem(ItemStats.FromPacket(p))`; `ClearInventorySlotPacket` → `slots[p.SlotNumber].ClearItem()`; `StatusInfoPacket` → gold text (`{Gold:N0}`).
- Wire each slot: `OnDoubleClick = UseItem` → `NetworkClient.UseItem(stats.SlotNumber)`; `OnDropItem = DropItem` implementing `InventoryWindow.cs:88-105`: same-window → `MoveItemInInventory` or, when split (`GetStackSplitAmount != StackSize`), `SplitStackInInventory(from,to,amount)`; from Vendor window → `VendorPurchaseItem(npcId, fromSlot)`; from other window (Bank/Combine) → `MoveWindowToInventory(fromWindowId, fromSlot, toSlot)`.
- `GetSlot(int) → ItemStats` (used by VendorWindow sell). Toggle via `ToggleInventory` action.
Commit `feat(ui): InventoryWindow`.

### Task 10: `CharacterWindow` (+ `VitalsCharacterDisplay`)
Port `~/code/Goose2Client/Assets/Scripts/UI/CharacterWindow.cs` (`WindowFrame=Equipped`). Equipped-item slots (UI index i ↔ packet `SlotNumber = 31+i`, `firstSlotNumber=31`, `CharacterWindow.cs:36,73-86`); name/level/class/guild/exp labels; STR/STA/INT/DEX/AC + 5 resistances.
- **Listens:** `InventorySlotPacket`/`ClearInventorySlotPacket` (only `SlotNumber>=31`), `StatusInfoPacket` (stats/resist/level/name), `ExperienceBarPacket` (exp totals).
- **Sends:** double-click equipped item → `UseItem(SlotNumber)`.
- `VitalsCharacterDisplay`: render the local player's paper-doll. Reuse the Step-6 `Character` rendering — instance the player's appearance as a static idle-down preview (simplest faithful approach: a small `Character` node playing `idle-down`, or first-frame layers). Drives off `GameManager.Instance.CurrentMapManager.LocalPlayer`. Mark anything beyond a static preview `// TODO(step8)`.
Toggle via `ToggleCharacterWindow`. Commit `feat(ui): CharacterWindow + character display`.

### Task 11: `SpellbookWindow` (+ `SpellbookPage`, `SpellbookButton`)
Port `~/code/Goose2Client/Assets/Scripts/UI/SpellbookWindow.cs` (`WindowFrame=Spellbook`, 30 slots/page via `Constants.SpellbookSlotsPerPage`). Paged grid of `SpellSlot`s; page numbering global (`startSlotIndex + j`, `SpellbookWindow.cs:40`); Back/Next buttons.
- **Listens:** `SpellbookSlotPacket` → `GetSlot(p.SlotNumber).SetSpell(SpellInfo.FromPacket(p))`.
- **Sends:** `UseSpell(SpellInfo)` (`SpellbookWindow.cs:80-86`): if `TargetType==None` → `CastSpell(slot, GameManager.Instance.CurrentMapManager.MyLoginId)` + `SpellCooldownManager.Cast(slot)`; else → `GameManager.Instance.SpellTargetManager.Cast(info)` (stub). Block when `LocalPlayer.IsMounted` or `IsTargeting`. `MoveSpell(from,to)` → `NetworkClient.MoveSpell` (`:147`); also the paged auto-place overloads (`MoveSpell(from, forward)` scanning for first empty slot, `:120-140`).
- `SpellbookButton`: click → `OnBack/NextClicked`; drop spell onto it → `MoveSpell(slot, isNext)`. `SpellbookPage`: container `{ startSlotIndex, SpellSlot[] slots }`.
Toggle via `ToggleSpellbook`. **Extract page-find/auto-place logic to a pure helper and unit-test** (find slot by global number across pages; first-empty forward/back). Commit `feat(ui): SpellbookWindow + paging`.

### Task 12: `HotbarWindow` (+ `HotbarPage`, `Toolbar`/`ToolbarItem`)
Port `~/code/Goose2Client/Assets/Scripts/UI/HotbarWindow.cs` (319 LOC — heaviest). `WindowFrame=Hotbar`. `HotbarPage[]` (10 `HotbarSlot`s each), only one visible; XP bar fill+text+tooltip; Back/Next page buttons (`CycleHotbarPage` action).
- **Listens:** `ExperienceBarPacket` → XP fill+text; `InventorySlotPacket`/`ClearInventorySlotPacket` → broadcast to all pages' slots (`slot.OnInventorySlot`/`OnClearInventorySlot`) + mount tracking (`mountSlots` dict, mount slot `30+14=44`, `HotbarWindow.cs:31-33,189-221`); `SpellbookSlotPacket` → broadcast `slot.OnSpellbookSlot`.
- **Sends:** `UseSlot` → item: `inventoryWindow.UseItem` / spell: `spellbookWindow.UseSpell` (`:238-249`); mount toggle `OnToggleMount` → `UseItem` (`:273-287`).
- Load/save slots to `CharacterSettings.Hotkeys[]` indexed `page*slots.Length + i` (`:109-134`); `SaveSlotsDelayed()` coroutine → use `await ToSignal(GetTree().CreateTimer(x), ...)` or a one-shot `Timer`. Hotkey repeat via `_Process` (0.1s delay, `:258-270`) reading the hotbar number actions.
- `Toolbar`/`ToolbarItem` (port `ToolbarItem.cs`): four buttons — Destroy (no-op / hosts `DestroyButton`), CombineBag → `OpenCombineBag()`, Options → toggle `OptionsWindow`, Exit → `GameManager.Instance.Quit()`.
Commit `feat(ui): HotbarWindow + toolbar`.

### Task 13: `ChatWindow`
Port `~/code/Goose2Client/Assets/Scripts/UI/ChatWindow.cs` (307 LOC). Scrollable `chatContainer` (use `RichTextLabel` with BBCode, or a `VBoxContainer` of `Label`s in a `ScrollContainer`), a `LineEdit` input, scrollbar, hover-alpha.
- **Listens:** `ChatPacket`→`AddChatLine(msg, ChatType.Chat)`; `HashMessagePacket`→`Chat`; `ServerMessagePacket`→`AddChatLine(msg, p.ChatType)`; `TellPacket`→ store `replyToName`, render `"[tell from] {Name}: {Message}"` as `ChatType.Tell`.
- **Color map** (`ChatWindow.cs` startup): Chat=White, Guild/Group/Tell=Yellow, Melee=Red, Spells=Blue, Server=Green. Backtick→heart (`♥`).
- **Input/commands:** Enter → if starts `/` → `HandleCommand`; else `NetworkClient.ChatMessage(msg[..min(len,200)])`. Up/Down arrows traverse `inputHistory` (dedupe consecutive). Escape clears+blurs. Aliases (`ChatWindow.cs:56-72`): `/t→/tell, /ga→/groupadd, /gr→/groupremove, /gu→/guild, /g→/group, /→/who, /r→/random 1000, /h→Hello there!`. `CommandHandlers` dict (built-in `/quit`→`GameManager.Quit`). Unknown `/cmd` → `NetworkClient.Command(fullCommand)`.
- Focus triggers via the input actions: `StartChat`→focus `""`, `SlashCommand`→`"/"`, `GuildCommand`→`"/guild "`, `TellCommand`→`"/tell "`, `ReplyCommand`→`"/tell {replyToName} "`. Expose `bool Typing` (input focused) so the player movement code can ignore movement keys while typing — **wire `Character.ProcessLocalInput` to early-return when `GameHud.Chat.Typing`** (small edit to `Scripts/Character/Character.cs:309`).
- **Pure-logic extraction + test:** put command parsing (alias expansion, split command/args, handler-vs-server decision, 200-char truncation) in `ChatCommandParser` and unit-test ~6 cases (`/t Bob hi`→`/tell Bob hi`, `/`→`/who`, plain text truncation, `/quit` routed to handler). Add to `.csproj`.
Commit `feat(ui): ChatWindow + command parser`.

### Task 14: `VendorWindow`
Port `~/code/Goose2Client/Assets/Scripts/UI/VendorWindow.cs` (`WindowFrame=Vendor`, server-spawned). Slots + title.
- **Listens:** `MakeWindowPacket` (filter `WindowFrame==Vendor`) → set `NpcId/Title/WindowId`; `EndWindowPacket` → show panel when ids match; `VendorSlotPacket` → `slots[p.SlotNumber].SetItem(ItemStats.FromPacket(p))`; `ClearVendorPacket` → clear all slots.
- **Sends:** double-click slot → `BuyItem` → `VendorPurchaseItem(NpcId, stats.SlotNumber)`. Sell (item dropped from Inventory onto vendor, routed by InventoryWindow) → `VendorSellItem(NpcId, fromSlot, GetStackSplitAmount(stack))`. Close → `WindowButtonClick(Close, WindowId, NpcId)`.
Commit `feat(ui): VendorWindow`.

### Task 15: `BankWindow`
Port `~/code/Goose2Client/Assets/Scripts/UI/BankWindow.cs` (`WindowFrame=Bank`, server-spawned). Slots + title + Back/Next (from `MakeWindowPacket.Buttons`).
- **Listens:** `MakeWindowPacket` (filter Bank), `EndWindowPacket`, `BankSlotPacket`→set, `ClearBankSlotPacket`→clear.
- **Sends:** inventory→bank drop → `MoveInventoryToWindow(invSlot, WindowId, bankSlot)`; window→bank → `MoveWindowToWindow(fromWin, fromSlot, WindowId, toSlot)` (ignore Vendor source); Close/Next/Back → `WindowButtonClick`.
Commit `feat(ui): BankWindow`.

### Task 16: `CombineBagContainerWindow`
Port `~/code/Goose2Client/Assets/Scripts/UI/CombineBagContainerWindow.cs` (`WindowId` hardcoded `22`, `WindowFrame=TenSlot`). 10 slots + Combine button.
- **Listens:** `EndWindowPacket` (id==22)→show; `CombineBagSlotPacket`→set; `ClearCombineBagSlotPacket`→clear.
- **Sends:** inventory→bag → `MoveInventoryToWindow(invSlot, 22, slot)`; window→bag → `MoveWindowToWindow`; Close → `WindowButtonClick(Close,22,0)`; Combine → `WindowButtonClick(Combine,22,0)`.
Commit `feat(ui): CombineBagContainerWindow`.

### Task 17: `PartyWindow` (+ `PartyMember`)
Port `~/code/Goose2Client/Assets/Scripts/UI/PartyWindow.cs` + `PartyMember.cs`. List of `PartyMember`s; scrollbar alpha on hover.
- **Listens:** `GroupUpdatePacket` → `members[p.LineNumber].OnGroupUpdate(p)` (name + level/class); `VitalsPercentagePacket` → find member by `LoginId`, set HP/MP fills; `EraseCharacterPacket` → clear member by id; `MakeCharacterPacket` → refresh member vitals.
- `PartyMember`: `PlayerId`, name text, HP/MP fills; `PlayerId==0` = empty slot. Queries `GameManager.Instance.CurrentMapManager.GetCharacter(PlayerId)` for current vitals (fallback 1.0).
- **Sends:** none directly.
Commit `feat(ui): PartyWindow + members`.

### Task 18: `BuffEffectsWindow` (+ `BuffEffect`)
Port `~/code/Goose2Client/Assets/Scripts/UI/BuffEffectsWindow.cs` + `BuffEffect.cs` (`WindowFrame=Buffbar`). Row of buff icon slots.
- **Listens:** `BuffBarPacket` → `slots[p.SlotNumber]` set icon (`Icon.Apply(file,id)`) + tooltip name, or clear when empty.
- **Sends:** double-click buff → `KillBuff(index+1)` (server is 1-indexed, `BuffEffectsWindow.cs:39`).
Commit `feat(ui): BuffEffectsWindow`.

### Task 19: `BaseMultipleWindow` + manager, then `QuestWindow` + `InfoWindow`
Port `~/code/Goose2Client/Assets/Scripts/UI/{BaseMultipleWindow,BaseMultipleWindowManager,QuestWindow,QuestWindowManager,InfoWindow,InfoWindowCreator}.cs`. These are multi-instance text-line dialog windows.
- `BaseMultipleWindow : BaseWindow` — `OnMakeWindow(MakeWindowPacket)` (title + Back/Next/Close buttons from `Buttons[]`), line labels set by `WindowLinePacket`, `CloseWindow/NextClicked/BackClicked` → `WindowButtonClick(Close/Next/Back, WindowId, NpcId)` (`BaseMultipleWindow.cs:60-77`). Replace Unity's `packetBuffer`+`Update()` dequeue with direct main-thread handling (packets already arrive on main thread).
- `BaseMultipleWindowManager<T>` — `Dictionary<int,T> windows` keyed by `WindowId`; on `MakeWindowPacket` (filter `WindowFrame==this.WindowFrame`) instantiate `PrefabPath` scene, populate, store; route `EndWindowPacket`/`WindowLinePacket` by id; destroy on close.
- `QuestWindowManager : BaseMultipleWindowManager<QuestWindow>` (`PrefabPath` → `Scenes/UI/QuestWindow.tscn`, `WindowFrame=Quest`). `InfoWindowCreator : BaseMultipleWindowManager<InfoWindow>` (`WindowFrame=GenericInfo`).
Commit `feat(ui): multi-window base + Quest/Info windows`.

### Task 20: `OptionsWindow`
Port `~/code/Goose2Client/Assets/Scripts/UI/OptionsWindow.cs`. Single "Target Filtering" `CheckBox` bound to `CharacterSettings.Options[Options.TargetFiltering]`; load default `true`; save on close (`CharacterSettings.Save()`). Opened from Toolbar.
Commit `feat(ui): OptionsWindow`.

### Task 21: `DebugWindow`
Port `~/code/Goose2Client/Assets/Scripts/UI/DebugWindow.cs`. FPS label (use `Engine.GetFramesPerSecond()`, refresh ~0.5s) + version label (`ProjectSettings.GetSetting("application/config/version")` or a constant). Static `FramesPerSecond` exposed if other code reads it.
Commit `feat(ui): DebugWindow`.

### Task 22: `MapClickHandler` + `CharacterClickHandler`
Port `~/code/Goose2Client/Assets/Scripts/UI/{MapClickHandler,CharacterClickHandler}.cs`.
- Map click: on the map area, left/right click → tile coord via `MapCoords` (inverse of `TileBottomCenter`) → `NetworkClient.LeftClick(x,y)` / `RightClick(x,y)`. Implement in `MapManager` `_UnhandledInput` (Godot has no uGUI EventSystem). Add a `MapCoords.WorldToTile(Vector2)` inverse helper + unit-test round-trip (`TileBottomCenter` then `WorldToTile` returns the tile).
- Character click: on the Step-6 `Character` node, left/right click → `LeftClick(c.X, c.Y)` / `RightClick(c.X, c.Y)`; hover → map-item tooltip if an item is on that tile. Add a small `Area2D`/`_Input` hit test on the Character, or route through `MapManager` by tile.
Commit `feat(ui): map + character click handlers`.

---

# PHASE C — Assembly & wiring

### Task 23: `GameHud` — assemble persistent windows + input toggles
**Files:** Create `Scenes/UI/GameHud.tscn`, `Scripts/UI/GameHud.cs`.
- `GameHud : Control` (full-rect) owns instances of: VitalsWindow, InventoryWindow, CharacterWindow, SpellbookWindow, HotbarWindow+Toolbar, ChatWindow, PartyWindow, BuffEffectsWindow, DebugWindow, OptionsWindow, plus `TooltipManager`, `WorldDropTarget`, and the managers (Vendor, Bank, Combine, Quest, Info). Expose typed accessors (e.g. `Chat`, `Inventory`).
- On map entry, `MapManager` (or `GameManager.ChangeMap`) instantiates `GameHud` under `GameManager.Instance.UiLayer` (once; persists across map swaps). Tear down on logout.
- `_UnhandledInput`/`_Input`: `ToggleInventory`→Inventory.Toggle, `ToggleSpellbook`, `ToggleCharacterWindow`, `CycleHotbarPage`→Hotbar page, chat focus actions→Chat. Ensure chat-focused state suppresses movement (already wired in Task 13).
- Restore each window's saved position/visibility from `CharacterSettings`.
Commit `feat(ui): GameHud assembly + input toggles`.

### Task 24: Settings persistence hardening (deferred follow-up)
Resolve the migration-plan follow-up: `CharacterSettings.Load()` null-guard so a settings file deserializing with `Hotkeys == null` / `WindowSettings == null` / `Options == null` doesn't throw. Add defaults in `Load()` and a unit test feeding partial/corrupt JSON. Wire `Save()` to be called on window move (Task 2) and options change (Task 20). Add to `.csproj`. Commit `fix(settings): null-guard CharacterSettings.Load + persist window/options`.

### Task 25: End-to-end live validation
Run against `scyther.local:2006` (`GOOSE_HOST=scyther.local GOOSE_PORT=2006`). Log in, enter the map, and confirm: inventory populates and gold shows; double-click uses/equips; drag item inventory→inventory (move + shift-split), →hotbar, →world (drop), →destroy; hotbar use + paging; spellbook cast (non-targeted) + cooldown overlay; open a vendor (buy/sell) and bank (deposit/withdraw) and combine bag; chat send + `/` command + tell/reply + history; party member vitals; buff add/remove; vitals HP+MP; options toggle persists; window drag persists across relog. Capture a screenshot via the `run` skill. Record results + any new deferred items in `MIGRATION_PLAN.md` (mark Step 7 landed). Commit `docs: record Step 7 (UI windows) landed`.

---

## Coverage checklist (every Unity UI file accounted for)

| Unity file | Task | Disposition |
|---|---|---|
| IWindow | 2 | ported (interface) |
| BaseMultipleWindow / Manager | 19 | ported |
| TitleBar | 2 | folded into BaseWindow drag |
| WindowTransparency / BackgroundTransparency | 2 | folded into BaseWindow Modulate |
| NonDrawingGraphic | — | DROPPED (Godot `mouse_filter`) |
| DragIcon | 4 | DROPPED → `SetDragPreview` |
| DropTarget / DropTargetManager | 7 | replaced by `WorldDropTarget` + built-in `_DropData` |
| TooltipManager / ItemTooltip / SpellTooltip / TextTooltip / MapItemTooltip / TextTooltipEventHandler | 3 | ported |
| ItemSlot | 4 | ported |
| SpellSlot | 5 | ported |
| HotbarSlot | 6 | ported |
| DestroyButton | 7 | ported |
| InventoryWindow | 9 | ported |
| CharacterWindow / CharacterClickHandler | 10 / 22 | ported |
| VitalsWindow / VitalsCharacterDisplay | 8 / 10 | ported |
| SpellbookWindow / SpellbookPage / SpellbookButton | 11 | ported |
| HotbarWindow / HotbarPage / ToolbarItem | 12 | ported |
| ChatWindow | 13 | ported |
| VendorWindow | 14 | ported |
| BankWindow | 15 | ported |
| CombineBagContainerWindow | 16 | ported |
| PartyWindow / PartyMember | 17 | ported |
| BuffEffectsWindow / BuffEffect | 18 | ported |
| QuestWindow / QuestWindowManager / InfoWindow / InfoWindowCreator | 19 | ported |
| OptionsWindow | 20 | ported |
| DebugWindow | 21 | ported |
| MapClickHandler | 22 | ported |

Supporting (non-UI dir): `ItemStats`, `SpellInfo`, `Helpers`, `SpellCooldownManager`, `SpellTargetManager` (stub) → Phase 0.

## Risks / call-outs
- **`SpellTargetManager` is stubbed** (Task 0e) — targeted spell casting + on-screen targeting is Step 8. SpellbookWindow wires the call but it no-ops.
- **`VitalsCharacterDisplay`** renders a static paper-doll preview only; animated/posed display is Step-8 polish.
- **Missing converter assets** (e.g. some item/spell graphics) — slots degrade gracefully (icon hidden) like the Step-6 character slots; note any gaps for converter follow-up.
- **`MapObjectPacket` ItemStats overload** (Task 0a) — confirm field parity before porting the second `FromPacket`.
- Drag/drop "kind" routing relies on storing slot refs in a `Variant` — verify `Variant.From(this)` round-trips a C# `Control` reference in your Godot version; if not, store via a static drag-context object instead.
```
