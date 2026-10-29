# Backpack Part 2 (Client) Implementation Plan

**Goal:** Render the server's backpack: a 15th equip slot, a multi-instance server-sized grid
window (`WindowFrames.GridContainer`), and a rebindable `ToggleBackpack` key (default N) that
sends `OBP`.

**Architecture:** `MakeWindowPacket`'s two trailing ints become `FrameParam1`/`FrameParam2`
(slot count, columns for this frame). A `GridContainerWindowManager :
BaseMultipleWindowManager<GridContainerWindow>` creates one window per server window id and also
routes `GWS`/`GWC` slot packets by id. The window builds `ItemSlot`s positioned by a pure
`GridContainerMetrics` class in `Relayout`, the same pattern as `OptionListWindow` +
`OptionListMetrics`. The toggle is a pure server round trip: the client never decides
open/closed.

**Tech Stack:** Godot 4.6 C# (GodotSharp 4.6.2), xUnit (`tests/Goose2Client.Tests` compiles
`Scripts/**/*.cs` directly; scene/contract tests read `.tscn`/source text).

**Design:** `illutiagooseserver/docs/plans/2026-10-29-backpack-design.md` (branch
`feature/backpack`). This is Part 2 of 2; Part 1 (server) is
`illutiagooseserver/docs/plans/2026-10-29-backpack-part1-server.md`.

**Conventions:** `AGENTS.md`: no new comments/doc strings unless the "why" is non-obvious.

---

## Server protocol this plan consumes (from Part 1)

| Packet | Format | Notes |
|--------|--------|-------|
| `MKW` | `MKW{id},32,Backpack,0,1,0,0,0,{npc=0},{slots},{columns}` | frame 32 = `GridContainer` |
| `GWS` / `GWC` | existing; 1-based slot, client parse subtracts 1 | `GenericWindowSlotPacket.cs`, `ClearGenericWindowSlotPacket.cs:18` |
| `ENW{id}` / `CLW{id}` | existing | server sends `CLW` on toggle-close and on unequip |
| `SIS46` / `CIS46` | equip slot 15 = wire 15 + 31 | client parse subtracts 1 → `SlotNumber` 45 → `CharacterWindow` index 14 |
| Client → `OBP` | no args | toggle |
| Client → `WBC` Close | existing | server removes the window |
| Client → `ITW`/`WTI`/`WTW` | existing, already generic over `WindowId` | `NetworkClient.cs:233-246` |

A second `MKW` with the **same id** means "resize": the server sends it after a backpack upgrade.

## APIs verified

| API | Location |
|-----|----------|
| `WindowFrames` enum (Amount = 31 last) | `Scripts/WindowFrames.cs` |
| `MakeWindowPacket` (`Unknown1`, `Unknown2` trailing ints) | `Scripts/Network/Packets/MakeWindowPacket.cs:6-32` |
| Other `Unknown1/2` writers | `Scripts/UiScaleSelfTest.cs:415-416` (only one) |
| `BaseMultipleWindowManager<T>` (private `_windows`, MKW create/reuse by id, ENW/CLW routing, cascade) | `Scripts/UI/BaseMultipleWindowManager.cs:13-118` |
| `BaseMultipleWindow` (`LineCount` virtual, needs `Content/BackButton`, `Content/NextButton`, `Content/CloseButton`; `OnMakeWindow` virtual; `OnEndWindow` internal virtual; `CloseWindow` sends WBC Close; `FreeWindow`) | `Scripts/UI/BaseMultipleWindow.cs` |
| `OptionListWindow.Relayout` / `OnEndWindow` (resize in `Relayout`, preserve `Position` across Relayout) | `Scripts/UI/OptionListWindow.cs:158-212` |
| `UiScaleLayout.SkipMeta` (runtime-created children must skip the snapshot) | `Scripts/UiScaleLayout.cs:9`, `:94` |
| `UiScale.ScaleSize(float, float)` | `Scripts/UiScale.cs:47` |
| `GenericContainerWindow` (slot creation from `ItemSlot.tscn`, `DropItem`, `OnItemDropOnWindow`, `AcceptsItemDrops`) | `Scripts/UI/GenericContainerWindow.cs:13-140` |
| `BaseWindow.NearestSlot`, `AcceptsItemDrops`, `OnItemDropOnWindow` | `Scripts/UI/BaseWindow.cs:507-528` |
| `ItemSlot` scene 32×32 | `Scenes/UI/ItemSlot.tscn:8` |
| Generic container scene layout (1px grid separation, 2px left / 20px top inset) | `Scenes/UI/GenericContainerWindow.tscn` |
| `InventoryWindow.DropItem` routes any non-vendor window via `MoveWindowToInventory(fromWindow.WindowId, ...)` | `Scripts/UI/InventoryWindow.cs:93-111` |
| `NetworkClient.OpenCombineBag` (`OCB`) pattern | `Scripts/Network/NetworkClient.cs:283-286` |
| `CharacterWindow.EquippedSlotCount = 14`, `FirstSlotNumber = 31`, bounds-checked handlers | `Scripts/UI/CharacterWindow.cs:15-16`, `:69-76`, `:110-123` |
| `CharacterEquipmentLayout.Slots` (0..13), Mount (152,174), Helmet row y = 34 | `Scripts/UI/CharacterEquipmentLayout.cs:15-31` |
| `GameHud` multi-window manager construction, `_UnhandledInput`-style toggle chain | `Scripts/UI/GameHud.cs:110-115`, `:150-170` |
| `InputActionCatalog.Actions` (49 entries, Windows category) | `Scripts/InputBindings/InputActionCatalog.cs:17-60` |
| Factory defaults are captured from the Godot InputMap (`project.godot`) | `Scripts/InputBindings/InputBindingService.cs:105-111` |
| `project.godot` input map entry format (`ToggleInventory`, physical_keycode 73) | `project.godot:97-101` |
| N (physical_keycode 78) is unbound | `grep 'physical_keycode":78' project.godot` → no match |
| Catalog tests pin 49 actions and categories | `tests/Goose2Client.Tests/InputActionCatalogTests.cs:13-52` |
| Self-test hardcodes 49 | `Scripts/KeyBindingsSelfTest.cs:117`, `:137`, `:339`, `:343`, `:350` |
| `DefaultWindowLayout.Defaults` / `IsDialog` | `Scripts/UI/DefaultWindowLayout.cs:9-34` |
| Packet parse test harness `new PacketParser(text, prefix)` | `tests/Goose2Client.Tests/GenericWindowSlotPacketTests.cs:72` |
| Scene contract test pattern | `tests/Goose2Client.Tests/GenericContainerWindowSceneTests.cs` |
| HUD ordering contract tests (`new QuestWindowManager()` index checks) | `tests/Goose2Client.Tests/KeyBindingsIntegrationContractTests.cs:45-51`, `LogViewerIntegrationContractTests.cs:44-50` |

## Test commands

```bash
dotnet test tests/Goose2Client.Tests --filter "FullyQualifiedName~GridContainer|FullyQualifiedName~Backpack|FullyQualifiedName~InputActionCatalog|FullyQualifiedName~CharacterEquipmentLayout|FullyQualifiedName~MakeWindow"
dotnet test tests/Goose2Client.Tests
```

Baseline at `f010df0`: 1716 passed, 0 failed.

---

### Task 0: Protocol prerequisites

**Files:**
- Modify: `Scripts/WindowFrames.cs` — `GridContainer = 32`.
- Modify: `Scripts/Network/Packets/MakeWindowPacket.cs` — rename `Unknown1`/`Unknown2` to `FrameParam1`/`FrameParam2` (one line comment: for `GridContainer` they are slot count and columns, a wire detail that isn't visible in the code).
- Modify: `Scripts/UiScaleSelfTest.cs:415-416` — follow the rename.
- Modify: `Scripts/Network/NetworkClient.cs` — `public void ToggleBackpack() => Send("OBP");` next to `OpenCombineBag`.
- Test: `tests/Goose2Client.Tests/MakeWindowPacketTests.cs` (new).

**Tests:**
- `Parse_GridContainer_ReadsSlotsAndColumns`: `MKW1042,32,Backpack,0,1,0,0,0,0,20,5` → frame `GridContainer`, `FrameParam1 == 20`, `FrameParam2 == 5`, `Buttons[1] == true`.
- Regression: an existing vendor MKW (`MKW1001,13,Welcome to my shop!,0,1,0,0,0,1201,0,0`, from the comment at `MakeWindowPacket.cs:20`) still parses `NpcId == 1201`, params 0.

**Commit** `feat(backpack): GridContainer frame and MKW frame params`

---

### Task 1: GridContainerMetrics (pure layout)

**Files:**
- Create: `Scripts/GridContainerMetrics.cs` (static, like `Scripts/OptionListMetrics.cs`).
- Test: `tests/Goose2Client.Tests/GridContainerMetricsTests.cs`.

**API** (all inputs 1x base; `factor` applied with `UiScale.ScaleSize`/`ScaleCoordinate`):
- `const int SlotSize = 32, Gap = 1, Left = 2, Top = 20, Right = 2, Bottom = 4;` (matches `GenericContainerWindow.tscn` insets).
- `int Rows(int slots, int columns)` → `ceil(slots / max(columns,1))`, minimum 1.
- `Vector2 SlotPosition(int index, int columns, float factor)`.
- `Vector2 WindowSize(int slots, int columns, float factor)` — width = `Left + cols*SlotSize + (cols-1)*Gap + Right`; height = `Top + rows*SlotSize + (rows-1)*Gap + Bottom`. Width never narrower than a minimum title width (e.g. 100) so "Backpack" plus the close button fit for a 2-column backpack.
- `(int Slots, int Columns) Sanitize(int slots, int columns)` → clamps `columns` to `[1, 10]` and `slots` to `[0, 200]`, so a bad packet can't allocate thousands of nodes.

**Tests (red → green):** positions at 1x and 2x; 10 slots × 5 columns → 2 rows; 11 × 5 → 3 rows; columns 0 → treated as 1; slots 5000 → 200 (adversarial); the minimum width applies for 1 column.

**Commit** `feat(backpack): grid container layout metrics`

---

### Task 2: GridContainerWindow + manager + scene

**Files:**
- Modify: `Scripts/UI/BaseMultipleWindowManager.cs` — add `protected bool TryGetWindow(int windowId, out T window) => _windows.TryGetValue(windowId, out window!);`. This is the only base change: no behaviour change for existing managers.
- Create: `Scripts/UI/GridContainerWindowManager.cs` — `PrefabPath => "res://Scenes/UI/GridContainerWindow.tscn"`, `MatchesFrame(f) => f == WindowFrames.GridContainer`; in `_Ready` call `base._Ready()` then `Listen<GenericWindowSlotPacket>` / `Listen<ClearGenericWindowSlotPacket>` routing through `TryGetWindow`; mirror removal in `_ExitTree` (call base).
- Create: `Scripts/UI/GridContainerWindow.cs` — `GridContainerWindow : BaseMultipleWindow`:
  - `WindowFrame => WindowFrames.GridContainer`; `LineCount => 0`.
  - `OnMakeWindow(packet)`: `base.OnMakeWindow(packet)`; `(slots, cols) = GridContainerMetrics.Sanitize(packet.FrameParam1, packet.FrameParam2)`; if the shape changed, `RebuildSlots(slots, cols)` (free old `ItemSlot` nodes with `QueueFree`, create `slots` new ones from `ItemSlot.tscn` under `Content`, `SetMeta(UiScaleLayout.SkipMeta, true)`, `SlotNumber = i`, `Window = this`, `OnDropItem = DropItem`); else clear every slot (`ClearItem`) so a resend repopulates cleanly.
  - `SetSlot(int slotNumber, ItemStats)` / `ClearSlot(int slotNumber)`: bounds-checked like `GenericContainerWindow.cs:91-105`.
  - `Relayout()`: `base.Relayout()`, then position each slot with `GridContainerMetrics.SlotPosition`, size each slot `ScaleSize(SlotSize)`, `Size = WindowSize(...)`; keep the title bar width in step (`TitleBar` width = `Size.X`, close button right-aligned), following how `OptionListWindow` sizes only what it owns.
  - `OnEndWindow()`: same as `OptionListWindow.cs:204-212`: base, then `Relayout()` preserving `Position`.
  - `DropItem` / `OnItemDropOnWindow` / `AcceptsItemDrops`: copy `GenericContainerWindow.cs:107-140` (inventory → `MoveInventoryToWindow`; other non-vendor windows → `MoveWindowToWindow`).
- Create: `Scenes/UI/GridContainerWindow.tscn` — based on `GenericContainerWindow.tscn` but without `SlotGrid`, plus a hidden `Content/CloseButton` (required by `BaseMultipleWindow._Ready`), `WindowName = "GridContainer"`.
- Modify: `Scripts/UI/GameHud.cs:110-115` — `AddChild(new GridContainerWindowManager());` after `OptionListWindows`. Keep it after `KeyBindings`/`LogViewer` creation so the existing ordering contracts hold.
- Modify: `Scripts/UI/DefaultWindowLayout.cs` — `["GridContainer"] = new Vector2(740, 360)` (left of the inventory default at 900,360); **not** a dialog, so its saved position persists like the inventory's.
- Test: `tests/Goose2Client.Tests/GridContainerWindowSceneTests.cs` (scene contract, pattern of `GenericContainerWindowSceneTests`), `tests/Goose2Client.Tests/GridContainerIntegrationContractTests.cs` (source contracts: manager registered in `GameHud`, listens to `GenericWindowSlotPacket` and `ClearGenericWindowSlotPacket`, removes both in `_ExitTree`), update `DefaultWindowLayoutTests` (`IsDialog("GridContainer") == false`, `For("GridContainer")`).

**Lifecycle / publication:**
- Create: the manager instantiates the window, adds it to the tree (`_Ready` builds chrome, registers scaling), stores it in `_windows`, then calls `OnMakeWindow` (builds slots). Slot packets arriving before `ENW` find the window via `TryGetWindow`, so they're applied while it's still hidden. `ENW` shows it.
- Resize: same-id MKW → `OnMakeWindow` → rebuild or clear → GWS/GWC repopulate → ENW → `Relayout`.
- Teardown: user close → `CloseWindow` (WBC Close + `FreeWindow` → `OnCloseWindow` removes it from `_windows`); server `CLW` → manager `FreeWindow` without WBC (`BaseMultipleWindowManager.cs:77-81`). Late GWS for a freed id finds nothing and is dropped.
- Slot nodes created at runtime must carry `SkipMeta`, or the UI-scale snapshot would capture already-scaled geometry (`BaseMultipleWindow.cs` lines comment on the same trap for lines).

**Invariant → test matrix:**

| Invariant | Proved by |
|-----------|-----------|
| Scene has the nodes `BaseMultipleWindow` requires | `GridContainerWindowSceneTests` (`Content/BackButton`, `Content/NextButton`, `Content/CloseButton`, `TitleBar/CloseButton`, `WindowName = "GridContainer"`, script path) |
| Manager registered and routes slot packets | `GridContainerIntegrationContractTests` |
| Layout math | Task 1 tests (the window delegates all geometry to the metrics) |
| **Adversarial:** a hostile/buggy MKW can't allocate unbounded nodes | `Sanitize` test in Task 1 + contract test that `OnMakeWindow` calls `GridContainerMetrics.Sanitize` |
| Not a dialog | `DefaultWindowLayoutTests` |

Manual (Task 5): open, resize via upgrade, close via X, close via N, drag both ways, UI scale 1x→2x→1x.

**Commit** `feat(backpack): multi-instance grid container window`

---

### Task 3: Backpack equip slot

**Files:**
- Modify: `Scripts/UI/CharacterEquipmentLayout.cs` — add `new Vector2(152, 34), // 14 Backpack`; update the range text in the exception message/summary (`0–14`).
- Modify: `Scripts/UI/CharacterWindow.cs:15` — `EquippedSlotCount = 15`.
- Test: `tests/Goose2Client.Tests/CharacterEquipmentLayoutTests.cs` — `SlotOffset_Index14_Backpack` = (152, 34); **adversarial** `SlotOffset_Index15_Throws`. Check `MultiWindowMetricsTests.cs:23` (`[InlineData(13)]`) is unrelated to equip slots before touching it.

**Mutation impact:** `_slots` length 14 → 15; `OnInventorySlot`/`OnClearInventorySlot` already bounds-check (`CharacterWindow.cs:113-114`, `:121-122`), so wire 46 now lands in index 14 instead of being dropped. Equip/unequip/use from the character window work unchanged (the slot number round-trips through `UseItem`).

**Commit** `feat(backpack): backpack equip slot on the character window`

---

### Task 4: ToggleBackpack key binding

**Files:**
- Modify: `Scripts/InputBindings/InputActionCatalog.cs` — after `ToggleCharacterWindow`: `new("ToggleBackpack", "Backpack", "Windows", [new(BindingContext.Normal, "ToggleBackpack")])`.
- Modify: `project.godot` — add a `ToggleBackpack={...}` input entry copied from `ToggleInventory` (`:97-101`) with `"physical_keycode":78`.
- Modify: `Scripts/UI/GameHud.cs` toggle chain (`:151-157`) — `else if (@event.IsActionPressed("ToggleBackpack", exactMatch: true)) GameManager.Instance.NetworkClient.ToggleBackpack();`. It stays below the `LineEdit` focus guard, so typing "n" in chat doesn't toggle.
- Modify: `tests/Goose2Client.Tests/InputActionCatalogTests.cs` — add to `ExpectedActions` (after `ToggleCharacterWindow`) and `ExpectedCategories`; 49 → 50 in the count and the test name.
- Modify: `Scripts/KeyBindingsSelfTest.cs:117`, `:137`, `:339`, `:343`, `:350` — 49 → 50. Better: replace the literals with `InputActionCatalog.Actions.Count` so the next action doesn't repeat this.
- Test: new `KeyBindingsBackpackContractTests` (or extend `InputMapContractTests`): `project.godot` contains a `ToggleBackpack=` entry with `"physical_keycode":78`, and no other action uses physical keycode 78 (**adversarial**: a future default collision fails here).

**Commit** `feat(backpack): rebindable ToggleBackpack action, default N`

---

### Task 5: Verification

- `dotnet test tests/Goose2Client.Tests`: all green, count = 1716 + new tests.
- Godot self-tests: run the key-bindings and UI-scale self-tests the way the repo runs them (check `run.sh` / `docs/` for the `--selftest` invocation before running) and confirm the `[key_bindings_selftest] OK` lines.
- Manual smoke against a Part 1 server (`feature/backpack`):
  1. N with no backpack → notice, no window.
  2. Use a backpack in inventory → equips into the top-right slot and the window opens (10 slots, 2 rows).
  3. Drag an item in and out; drag between backpack and bank/community chest.
  4. N closes; N reopens with the items; the X button closes.
  5. Try to unequip a non-empty backpack → notice, nothing moves.
  6. Complete the 10→20 upgrade quest with the window open → the window grows in place, items keep their slots, tooltip says "Holds 20 items."
  7. UI scale 1x → 2x → 1x with the window open: no drift, slots aligned.
  8. Rebind ToggleBackpack in the key-bindings window; the new key works and N no longer does.

---

## Final review notes

- **Threading:** all packet handlers run on the main thread (`BaseMultipleWindowManager` summary), with no marshalling needed.
- **Persistence:** the window position is saved per `WindowName` ("GridContainer") by the existing `BaseWindow` code. If a second GridContainer use case appears, give each use its own scene `WindowName` or key the position by title.
- **Compatibility:** an old client talking to a new server ignores frame 32 MKWs (no manager matches) and drops equip slot 14 (bounds check). A new client talking to an old server never receives frame 32. `OBP` to an old server matches no prefix, and `EventHandler.AddEvent` returns false (`illutiagooseserver Goose/EventHandler.cs:253-256`), so it's ignored. Shipping order doesn't matter.
- **Design alignment:** checked against the design doc: frame 32, MKW `slots,columns`, `OBP`, slot (152,34), N default, multi-instance per window id.
