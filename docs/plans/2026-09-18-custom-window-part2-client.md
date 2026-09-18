# Custom Item Window — Part 2: Client Implementation Plan

**Goal:** Add the Godot client side of the custom item window: the `CustomWindow` (frame 28) with two drop slots, a live full-body preview, 2D gradient + RGB/alpha sliders, a name field, and the `CWS`/`CWC`/`CWG` packet wiring.

**Architecture:** A single `CustomWindow` (BaseWindow + IWindow) in the HUD — the `BankWindow` pattern (`BankWindow.cs:50-135`: HUD child listening for `MKW` matched by frame, storing `WindowId`, WBC on close) — with two drop slots, a live full-body preview, 2D gradient + RGB/alpha sliders, and a name field. (The `BaseMultipleWindowManager` pattern is not usable: its `where T : BaseMultipleWindow` constraint forces the text-line dialog layout, `BaseMultipleWindowManager.cs:14`.) Drops reuse the native Godot drag/drop payload (`{kind: "item", slot: ItemSlot}`) with client-side pre-validation; accepted drops send `CWS<look>,<stats>` (1-based server slot ids, `0` = empty). `CWG<equippedId>,<pose>` drives a static full-body preview composed from the local player's `Character` slot graphics, with the target slot swapped to the custom graphic and tinted via the shared lerp-tint shader; slider/gradient input updates only the shader param. Create sends `CWC` and disables until `CLW`.

**Tech Stack:** C# / Godot 4, xUnit (`tests/Goose2Client.Tests`), existing sprite assets under `res://Assets/Sprites/`.

**Design doc:** `docs/plans/2026-09-18-custom-window-design.md` (Part 2 of 2; Part 1 is the server, plan at `illutiagooseserver/.worktrees/custom-window/docs/plans/2026-09-18-custom-window-part1-server.md`).

Work in the worktree: `/home/agent/workspace/Goose2ClientGodot/.worktrees/custom-window` (branch `custom-window`).

## Protocol contract (fixed by the design doc / Part 1 plan)

- `CWS<lookInvSlot>,<statsInvSlot>` — client→server, full two-slot state on every drop/clear, `0` = empty. **1-based server inventory slot numbers.**
- `CWG<equippedId>,<pose>` — server→client, only in reply to a valid `CWS` with a non-zero look slot. `pose` = look item `BodyState`.
- `CWC<lookInvSlot>,<statsInvSlot>,<r>,<g>,<b>,<a>,<name>` — client→server. RGB 0–255, A 0–200. Name trimmed, commas stripped, ≤255 chars.
- Window: `MKW` with `WindowFrames.Custom = 28`, title `Custom`, buttons `0,1,0,0,1` (close + OK-as-Create). Server closes with `CLW` on successful create.

## APIs verified

| API | Citation |
| --- | --- |
| Client `WindowFrames` enum (last = `OptionList = 27`) | `Scripts/WindowFrames.cs:3-31` |
| `BaseMultipleWindowManager<T>` constraint (`where T : BaseMultipleWindow`) — why the manager pattern is NOT used | `Scripts/UI/BaseMultipleWindowManager.cs:13` |
| Single-instance server-window pattern: HUD child, `Listen<MakeWindowPacket>` matched by frame, `WindowId` stored, `OnClosePressed` → WBC Close + Hide | `Scripts/UI/BankWindow.cs:24, 57-61, 74-91, 131-135`, `Scripts/UI/GameHud.cs:56-69` |
| `BaseWindow`: `Title`, `DefaultVisible`, `WindowName` (`BaseWindow.cs:13`), TitleBar/CloseButton/TitleLabel/Content resolved via `GetNodeOrNull` (`BaseWindow.cs:53-57`), `OnClosePressed` virtual (`BaseWindow.cs:219`) | `Scripts/UI/BaseWindow.cs` |
| `IWindow`: `WindowId`, `WindowFrame` | used by `WorldDropTarget.cs:37`, `InventoryWindow.cs:20-21` |
| Drag payload: `_GetDragData` returns `Dictionary { "kind": "item", "slot": ItemSlot }`; drop targets implement `_CanDropData`/`_DropData` and read the dictionary **directly** — `ItemSlot.OnDropItem` passes only `(IWindow, int, int)` and discards the source control's `Stats`, so the custom slots must NOT rely on it | `Scripts/UI/ItemSlot.cs:68-107`, `Scripts/UI/WorldDropTarget.cs:17-56` |
| `ItemSlot` control: `Stats`, `HasItem`, `SlotNumber`, `Window`, `SetItem`, `ClearItem`, `OnDropItem(srcWindow, srcSlot, dstSlot)` | `Scripts/UI/ItemSlot.cs:14-46` |
| Inventory slot numbers are **0-based client-side** (`SlotNumber = p.GetInt32() - 1`); outbound packets add 1 (`Send($"DRP{fromSlot + 1},...")`) | `Scripts/Network/Packets/InventorySlotPacket.cs:64`, `Scripts/Network/NetworkClient.cs:212-215` |
| `NetworkClient.Send(string)` (main thread) and per-packet helper pattern | `Scripts/Network/NetworkClient.cs:80-90, 176-258` |
| Packet registration: `PacketManager.Listen<T>` keys handlers by `Prefix`; `Parse(new PacketParser(data, prefix))` | `Scripts/Network/PacketManager.cs:11-60`, `Scripts/Network/PacketHandler.cs:5-20` |
| Packet parse test pattern | `tests/Goose2Client.Tests/MapFlagsPacketTests.cs:8-14` |
| Client item enums: `ItemUseType` (Armor=2, Weapon=3), `ItemSlotType` (Helmet=1, Chestpiece=2, Pauldrons=3, Gloves=4, Pants=5, Shoes=6, Cloak=7, Belt=8, Necklace=9, Bracelet=10, Weapon=11, Shield=12, Mount=13) | `Scripts/Constants.cs:28-38, 76-92` |
| Server sends `item.BodyType` (1H and 2H both = 11) and `(int)item.UseType` in the item slot packet | `illutiagooseserver Goose/Packets.cs:473-474`, `Goose/ItemTemplate.cs:164-187` |
| Sprite asset path per slot: `res://Assets/Sprites/{TypeFolder(slot)}/{id}/animations.tres`; folder map (Chest/Helms/Legs/Feet/Hands/Bodies/Hair/Eyes) | `Scripts/Character/Character.cs:321`, `Scripts/Character/CharacterLayout.cs:39-52` |
| Clip resolution: `AnimationNames.Candidates(motion, bodyState, direction)` — Hands idle clips are `idle-equip`/`idle`/`idle-no-equip` variants chosen by equipped state (`bodyState != 3`); missing clip → hidden, never substituted | `Scripts/Character/AnimationNames.cs:30-62`, `Scripts/Character/Character.cs:716-724` |
| Slot tint: `ShaderMaterial { Shader = TintMaterial.Shader }`, param `"tint"`, alpha = blend factor, 0 = no tint; protocol alpha converts as `a / 255f` (custom A maxes at 200 but still divides by 255) | `Scripts/Character/Character.cs:303-314, 344-355`, `Scripts/TintMaterial.cs:11-31` |
| Static idle-frame pick pattern (`idle-down` else `idle`, frame 0) | `Scripts/UI/VitalsCharacterDisplay.cs:49-66` |
| Local player + appearance-update event: `GameManager.Instance.CurrentMapManager.LocalPlayer`, `GameManager.Instance.CharacterUpdated` (subscribed in `_Ready`, filtered to the local player) | `Scripts/UI/VitalsCharacterDisplay.cs:11-20`, `Scripts/GameManager.cs:58-59` |
| `Character` private slot store: `_slots` (`Slot { AnimatedSprite2D Sprite; int GraphicId }`) — no public per-slot accessor exists yet | `Scripts/Character/Character.cs:41, 326-359` |
| Metrics test pattern | `tests/Goose2Client.Tests/OptionListMetricsTests.cs` (mirrors `Scripts/OptionListMetrics.cs`) |
| Scene pattern for a manager-owned window (Background/TitleBar/TitleLabel/CloseButton/Content) | `Scenes/UI/OptionListWindow.tscn` |
| `WindowButtons` enum (OK = 5) and `WindowButtonFlags.IsEnabled` | `Scripts/WindowButtons.cs:3-10`, `Scripts/WindowButtonFlags.cs` |

## Slot-type mapping (client pre-validation + preview target)

Server rules (design doc): armor/weapon only; exclude ring/necklace/pauldrons/cloak/belt/gloves; same type with the 1H/2H weapon exception. In client terms (`ItemSlotType`):

- Allowed use types: `ItemUseType.Armor`, `ItemUseType.Weapon`.
- Excluded slot types: `Pauldrons`, `Gloves`, `Cloak`, `Belt`, `Necklace`, `Bracelet`.
- Same-type rule: `a.SlotType == b.SlotType` — 1H and 2H are both `Weapon` (11) on the wire (`ItemTemplate.cs:176-182`), so the exception falls out for free.
- Preview target (`ItemSlotType` → `CharacterSlot`): `Helmet→Helm`, `Chestpiece→Chest`, `Pants→Legs`, `Shoes→Feet`, `Weapon→Weapon`, `Shield→Shield`, `Mount→` no layer (design: skip mounts), everything else → no layer (excluded types can't be dropped anyway).

---

### Task 1: `WindowFrames.Custom`, `CWG` packet, outbound `CWS`/`CWC`

**Files:**
- Modify: `Scripts/WindowFrames.cs` — add `Custom = 28` after `OptionList = 27`
- Create: `Scripts/Network/Packets/CustomWindowGraphicPacket.cs`
- Modify: `Scripts/Network/NetworkClient.cs` — two send helpers
- Test: `tests/Goose2Client.Tests/CustomWindowPacketsTests.cs`

**Step 1: Write the failing tests**

`tests/Goose2Client.Tests/CustomWindowPacketsTests.cs` (pattern `MapFlagsPacketTests.cs:8-14`):

- `CWG` parse: `new CustomWindowGraphicPacket().Parse(new PacketParser("CWG777,6", "CWG"))` → `EquippedId == 777`, `Pose == 6`.
- `CWG` with `EquippedId == 0` parses (0 is valid — no equipped art).
- Outbound format (pure statics on the packet class or a `CustomWindowPackets` helper, so the 0-based→1-based convention is testable without a `NetworkClient`): `FormatCws(5, 6) == "CWS5,6"`, `FormatCws(0, 3) == "CWS0,3"` (empty look = 0), `FormatCwc(5, 6, 10, 20, 30, 40, "My Sword") == "CWC5,6,10,20,30,40,My Sword"`. `NetworkClient` helpers delegate to these.

Run: `dotnet test tests/Goose2Client.Tests --filter CustomWindowPacketsTests` — expected FAIL (type missing).

**Step 2: Implement**

- `CustomWindowGraphicPacket : PacketHandler`, `Prefix => "CWG"`, `Parse`: `EquippedId = p.GetInt32(); Pose = p.GetInt32();` (pattern `MakeWindowPacket.cs:14-28`).
- `NetworkClient` helpers (pattern `WindowButtonClick`, `NetworkClient.cs:252-256`), delegating to the pure format functions:

```csharp
public void CustomWindowSlots(int lookSlot, int statsSlot)
{
    Send($"CWS{lookSlot},{statsSlot}");
}

public void CustomWindowCreate(int lookSlot, int statsSlot, int r, int g, int b, int a, string name)
{
    Send($"CWC{lookSlot},{statsSlot},{r},{g},{b},{a},{name}");
}
```

Callers pass **1-based server slot ids** (`itemSlot.SlotNumber + 1`, `0` for empty) — same convention as `Drop`/`MoveItemInInventory` (`NetworkClient.cs:187-190, 212-215`).

**Step 3: Green**

Run: `dotnet test tests/Goose2Client.Tests` — expected: all pass (480 baseline + new).

**Step 4: Commit**

```bash
git add Scripts/WindowFrames.cs Scripts/Network/Packets/CustomWindowGraphicPacket.cs \
        Scripts/Network/NetworkClient.cs tests/Goose2Client.Tests/CustomWindowPacketsTests.cs
git commit -m "feat(net): custom window packets (CWG in, CWS/CWC out)"
```

---

### Task 2: Drop pre-validation (pure logic)

**Files:**
- Create: `Scripts/UI/CustomWindowValidation.cs`
- Test: `tests/Goose2Client.Tests/CustomWindowValidationTests.cs`

**Step 1: Write the failing tests**

`CustomWindowValidation` — static, pure, no Godot scene types (pattern: `WindowButtonFlagsTests`):

```csharp
public static class CustomWindowValidation
{
    public static bool IsValidCandidate(ItemStats item);
    public static bool TypesCompatible(ItemStats a, ItemStats b);
    public static bool IsInventorySource(IWindow srcWindow);   // WindowFrame == Inventory
    public static CharacterSlot? PreviewTarget(ItemStats item);   // null for Mount/excluded
}
```

Tests (construct `ItemStats` literals — plain POCO, `Scripts/ItemStats.cs:5`):

- `IsValidCandidate`: Armor + Chestpiece → true; Weapon + Weapon(11) → true; each excluded type (Pauldrons, Gloves, Cloak, Belt, Necklace, Bracelet) → false; `ItemUseType.NoUse`/`OneTime` → false (adversarial: catches a check that only looks at slot type).
- `TypesCompatible`: Chestpiece+Chestpiece → true; Chestpiece+Helmet → false (adversarial: catches dropping the same-type rule); Weapon+Weapon → true (1H/2H both 11 — the exception); Mount+Mount → true (mounts pass the rules; only the preview skips them).
- `IsInventorySource`: inventory window → true; equipped/combine-bag/vendor frames → false.
- `PreviewTarget`: the six mappings above; Mount → null; excluded types → null.

Run: `dotnet test tests/Goose2Client.Tests --filter CustomWindowValidationTests` — expected FAIL.

**Step 2: Implement** per the slot-type mapping table above.

**Step 3: Green + full suite**

Run: `dotnet test tests/Goose2Client.Tests` — expected: all pass.

**Step 4: Commit**

```bash
git add Scripts/UI/CustomWindowValidation.cs tests/Goose2Client.Tests/CustomWindowValidationTests.cs
git commit -m "feat(ui): custom window drop pre-validation"
```

---

### Task 3: `Character` slot-graphic snapshot API

**Files:**
- Modify: `Scripts/Character/Character.cs` — public accessor over `_slots`
- Test: `tests/Goose2Client.Tests/CharacterSlotSnapshotTests.cs` (only if the logic is non-trivial; otherwise covered by Task 4's manual E2E — see note)

The preview needs every slot's graphic id + current tint from the local player's `Character`, which today only exposes `GetAppearance()` (body/hair/face/chest/helm, `Character.cs:361`). Add:

```csharp
public bool TryGetSlotGraphic(CharacterSlot slot, out int graphicId, out Color tint)
```

(no doc comment — repo rule, `AGENTS.md`)

- Reads `_slots` (`Character.cs:41`): `graphicId = s.GraphicId`; `tint` from `s.Sprite.Material`'s `"tint"` shader param when it is a `ShaderMaterial`, else `new Color(0,0,0,0)` (mirrors `NoTint`, `Character.cs:317`).
- Returns false when the slot has no entry (empty slot / missing art — `ApplySlot` removes slots with `graphicId <= 0` or missing assets, `Character.cs:321-323`).
- The ids in `_slots` are the same wire ids the server uses for equipped art (`eq[i][0]` from UCP flows straight into `ApplySlot`, `Character.cs:306-314`) — the same id space as `CWG`'s `equippedId` (`item.GraphicEquipped`).

**Mutation impact:** none — read-only accessor over existing state; no propagation needed. Invariant: values match what the character renders (same `_slots` source of truth). Proof: Task 4's manual E2E (preview background matches the on-map character); a headless unit test of a private node's material state is not practical in `Goose2Client.Tests` (no Godot runtime there) — explicitly deferred to E2E.

**Step 1:** implement the accessor. **Step 2:** `dotnet build Goose2ClientGodot.sln` (or the client csproj) compiles. **Step 3: Commit**

```bash
git add Scripts/Character/Character.cs
git commit -m "feat(character): expose per-slot graphic snapshot for previews"
```

---

### Task 4: Full-body preview control

**Files:**
- Create: `Scripts/UI/CustomPreviewControl.cs`
- Test: `tests/Goose2Client.Tests/CustomPreviewMetricsTests.cs` (layout math only)

**`CustomPreviewControl : Control`** — a static, non-animated composite of the local player:

- `Refresh()` — for each `CharacterSlot` in `CharacterLayout.All` **except `Mount`** (design: skip mounts):
  - source = `GameManager.Instance.CurrentMapManager?.LocalPlayer`; null → hide all.
  - `source.TryGetSlotGraphic(slot, out var id, out var tint)` (Task 3); false → hide that layer.
  - **Replaced slot:** if a custom graphic is set for this slot (see below), use the custom `equippedId` and the current picker RGBA instead of the character's id/tint. A custom `equippedId == 0` keeps the layer **replaced but hidden** (the chosen look intentionally has no equipped art — do NOT restore the player's normal equipment, design: "0 allowed, no special handling").
  - Load `res://Assets/Sprites/{CharacterLayout.TypeFolder(slot)}/{id}/animations.tres` (`Character.cs:321`); missing → hide layer.
  - Clip: first candidate from `AnimationNames.Candidates("idle", bodyState, Direction.Down)` that the `SpriteFrames` contains, frame 0 (`AnimationNames.cs:30-62`, `Character.ResolveClip` pattern `Character.cs:716-724`); `bodyState` = the CWG `pose` for the replaced slot, the local player's `BodyState` for background slots; no candidate → hide the layer (missing art hides, never substitutes).
  - `TextureFilter = Nearest`.
  - Tint: `ShaderMaterial { Shader = TintMaterial.Shader }` with `"tint"` = `Color(r / 255f, g / 255f, b / 255f, a / 255f)` — **alpha always divides by 255** even though A maxes at 200 (`Character.cs:303-314`); a == 0 → no material.
  - Layer positions/sizes: a `CustomPreviewMetrics` static class (pure math, unit-tested) — scale each layer to fit the control rect, anchored bottom-center; child order follows `CharacterLayout.All` back-to-front (first child = drawn first = bottom, matching the character's draw order).
- `SetCustomGraphic(CharacterSlot? slot, int equippedId, int pose)` / `ClearCustomGraphic()` — stores the replacement target (`null` slot = no replacement, e.g. mounts); `pose` stored for clip resolution. Call this **immediately** on any look-slot change or clear (before any `CWG` arrives) so a stale replacement never lingers — with `equippedId = 0` as a placeholder that hides the layer.
- `SetTint(int r, int g, int b, int a)` — updates **only** the replaced slot's shader param (`mat.SetShaderParameter("tint", ...)`); no re-load, no re-layout. This is the live-update path.
- Subscribes to `GameManager.Instance.CharacterUpdated` (local player only) → `Refresh()` (design gap 3: re-equip while open rebuilds the background).
- `HideAll()` on exit.

**Tests:** `CustomPreviewMetricsTests` — layer scale/position math for a few aspect ratios (pattern `OptionListMetricsTests`). Asset loading / tinting is Godot-runtime behaviour → covered by Task 6 E2E, stated explicitly.

**Step 1:** metrics tests (red) → **Step 2:** implement control + metrics (green) → **Step 3:** `dotnet test tests/Goose2Client.Tests` all pass → **Step 4: Commit**

```bash
git add Scripts/UI/CustomPreviewControl.cs Scripts/UI/CustomPreviewMetrics.cs \
        tests/Goose2Client.Tests/CustomPreviewMetricsTests.cs
git commit -m "feat(ui): full-body custom preview with live tint"
```

---

### Task 5: `CustomWindow` UI, scene, colour picker

**Files:**
- Create: `Scripts/UI/CustomWindow.cs`, `Scenes/UI/CustomWindow.tscn`
- Modify: `Scripts/UI/GameHud.cs` — add the window to the HUD (pattern `GameHud.cs:56-69`, `Add<CustomWindow>("res://Scenes/UI/CustomWindow.tscn")`)
- Test: `tests/Goose2Client.Tests/CustomWindowMetricsTests.cs`

**`CustomWindow : BaseWindow, IWindow`** — `WindowFrame => WindowFrames.Custom`, `WindowId` tracked from `MKW` (pattern `BankWindow.cs:20-21, 81`), `DefaultVisible => false` (server-spawned), `WindowName = "Custom"`.

Packet listeners in `_Ready`/`_ExitTree` (pattern `BankWindow.cs:43-46, 59-64`): `MakeWindowPacket` (frame `Custom` → show, title, `WindowId`, `Preview.Refresh()`, re-enable Create), `EndWindowPacket` (matching id → `Visible = true`, `BankWindow.cs:88-91`), `CloseWindowPacket` (matching id → hide + full state reset: slots, ids, pending, preview, name, sliders — the server only sends CLW on successful create, `BaseMultipleWindowManager.cs:73-77` shows the packet shape), `CustomWindowGraphicPacket` (Task 5 behaviour below), `ServerMessagePacket` (pending-`CWS` error handling below).

Scene layout (tscn, pattern `OptionListWindow.tscn` — Background/TitleBar/TitleLabel/CloseButton/Content; `BaseWindow._Ready` resolves those node names via `GetNodeOrNull`, `BaseWindow.cs:53-57`):

- Two drop slots (`Content/LookSlot`, `Content/StatsSlot`) — dedicated `CustomWindowSlot : Panel` controls (icon `TextureRect` + `Label`), **not** `ItemSlot` controls: `ItemSlot.OnDropItem` passes only `(IWindow, int, int)` and the window would have no access to the source item's `Stats` for pre-validation (`ItemSlot.cs:101-107`). Each implements `_CanDropData`/`_DropData` and reads the drag dictionary directly (`{ "kind": "item", "slot": ItemSlot }`, `ItemSlot.cs:68-88`) to get the source `ItemSlot`'s `Stats` and `Window`.
- `Content/Preview` — the `CustomPreviewControl`.
- Colour section: `Content/Gradient` (2D RGB cross-gradient `TextureRect`, generated at `_Ready` via `Image.Create` — red/green/blue/white corners — with `_GuiInput` click/drag), four `HSlider`s (R/G/B `MinValue=0` `MaxValue=255`, A `MinValue=0` `MaxValue=200`) + value `Label`s.
- `Content/NameField` — `LineEdit`, `MaxLength = 255`, commas filtered on `TextChanged` (strip `,`).
- `Content/CreateButton` — the OK button (visible per `MKW` flags; the window is only created by the server with `0,1,0,0,1`).

**Gradient mapping (fully specified):** corners are red (top-left), green (top-right), blue (bottom-left), white (bottom-right). Gradient input (click/drag) maps position → RGB by bilinear interpolation of the four corner colours; that RGB drives the sliders and the preview. Slider input drives the preview directly; the gradient **cursor** (a small marker `TextureRect`) moves only on gradient input — an arbitrary slider RGB has no unique position on the pad, so no inverse mapping is attempted.

Behaviour:

- **State:** `_lookInvSlotId` / `_statsInvSlotId` (1-based server ids, 0 = empty), `_pendingCws` (bool), `_cwcPending` (bool), current RGBA, look item `ItemStats` (kept for `PreviewTarget`).
- **Drop handling** (each `CustomWindowSlot._DropData`):
  - Source from the drag dictionary: `src` = the `ItemSlot` control.
  - Accept an inventory drop if `src.Window.WindowFrame == WindowFrames.Inventory` (design: inventory only) and `CustomWindowValidation.IsValidCandidate(src.Stats)`; when the other window slot is filled, also require `TypesCompatible`.
  - Accept a drop **from the other custom slot** (move between slots — copy the stored inventory id, do NOT re-derive from the control) and a drop **from this custom slot's own item** (clear).
  - Rejected drops: do nothing (native drag leaves the source item in place).
  - On accept: set/clear the slot icon, update `_lookInvSlotId`/`_statsInvSlotId`, and — if the look slot changed — immediately `Preview.SetCustomGraphic(PreviewTarget(lookStats) ?? null, 0, 0)` (hide the replaced layer until the `CWG` arrives; a look clear gets no `CWG`, so this is the only invalidation path).
  - Send `NetworkClient.CustomWindowSlots(_lookInvSlotId, _statsInvSlotId)`; set `_pendingCws = _lookInvSlotId != 0` (a `CWS` with look = 0 produces neither `CWG` nor an error, so only look-bearing requests are trackable).
- **`CWG`**: if `_pendingCws` → clear it; apply to the preview: `EquippedId > 0` → `Preview.SetCustomGraphic(CustomWindowValidation.PreviewTarget(_lookStats), EquippedId, Pose)`; `EquippedId == 0` → `Preview.SetCustomGraphic(PreviewTarget(_lookStats), 0, Pose)` (replaced-but-hidden, Task 4). Rapid `CWS` requests: responses arrive in order and each reflects the server's view of the latest look slot, so applying every `CWG` converges to the correct final state; a transient stale frame mid-flight is accepted (no request id in the fixed protocol).
- **Server message** (`Listen<ServerMessagePacket>`): if `_pendingCws` → clear it, clear the look slot (icon + id + `Preview.SetCustomGraphic(null, 0, 0)`). If `_cwcPending` → clear it and re-enable Create (the server keeps the window open on create failure — without this the button would stay disabled forever). Known limitation (accepted, fixed protocol): an unrelated server message inside the pending window can clear a valid look slot; the player re-drops.
- **Live tint:** slider/gradient input → `Preview.SetTint(r, g, b, a)` (shader param only).
- **Create:** enabled when both slots filled, name non-empty after trim, and not `_cwcPending` (RGBA is always in range — sliders are clamped). On press: `name = NameField.Text.Trim().Replace(",", "")`; `_cwcPending = true`; disable the button; `NetworkClient.CustomWindowCreate(_lookInvSlotId, _statsSlotId, r, g, b, a, name)`.
- **Reset** (one routine, used by both paths): clear slot icons + ids, `_pendingCws`/`_cwcPending`, RGBA to defaults, name field, gradient cursor, `Preview.SetCustomGraphic(null, 0, 0)` + `Preview.Refresh()`, refresh CreateButton visibility from the latest `MKW` flags.
- **`MKW`** (frame `Custom`): `Visible = true`, `Title = packet.Title`, `WindowId = packet.WindowId`, `Preview.Refresh()`, re-enable Create per the enable rule.
- **`CLW`** (matching id): hide + **Reset**.
- **Close:** override `OnClosePressed` (pattern `BankWindow.cs:131-135`): `NetworkClient.WindowButtonClick(WindowButtons.Close, WindowId, 0)` + **Reset** + `Hide()` (server just removes the window, no CLW echo — `illutiagooseserver Goose/CustomWindow` overrides `Clicked`; design: X and CLW both discard everything).

**Step 1:** write `CustomWindowMetricsTests` (red) → **Step 2:** implement window/scene/metrics (green: `dotnet test tests/Goose2Client.Tests` all pass; `dotnet build` clean) → **Step 3: Commit**

```bash
git add Scripts/UI/CustomWindow.cs Scenes/UI/CustomWindow.tscn \
        Scripts/UI/GameHud.cs tests/Goose2Client.Tests/CustomWindowMetricsTests.cs
git commit -m "feat(ui): custom window with drop slots, colour picker, and create flow"
```

---

### Task 6: Manual end-to-end

Prerequisite: Part 1 server built and running (dev server + `/updatesql` or restart so the ticket's `script path` is live), client run via `run.sh`.

Checklist:

1. Use a custom ticket (id 643) from inventory → window opens; ticket not consumed; using it again refuses.
2. Drag a weapon into the Look slot → preview shows the full body with that weapon's equipped art; drag armor into Stats → both icons shown; preview updates on `CWG`.
3. Drag a non-equipment item / a ring / an item from the combine bag → drop rejected, item stays put.
4. Drag chest into Look, helm into Stats → rejected (same-type rule); 1H + 2H weapons → accepted.
5. Move sliders + click the gradient → preview re-tints live; A slider tops out at 200.
6. Re-equip on the map while the window is open → preview background rebuilds.
7. Create with a valid name → all three items consumed, new item in inventory with the look/stats/RGBA/name, window closes.
8. Create with an empty name → button disabled; (via debug) a moved source item → server message, nothing consumed, window stays open.
9. Close the window without creating → nothing consumed.

No commit (verification only); note results in the PR description.

---

## Invariant-to-test matrix

| Invariant | Proved by |
| --- | --- |
| `CWG` parses equipped id + pose (incl. id 0) | `CustomWindowPacketsTests` (Task 1) |
| Outbound `CWS`/`CWC` use 1-based server slot ids | `FormatCws`/`FormatCwc` pure-function tests (Task 1) + E2E step 7 (Task 6) |
| Drop pre-validation matches server rules (use type, exclusions, same-type, 1H/2H, inventory-only source) | `CustomWindowValidationTests` (Task 2) |
| Inventory-only drop source | `CustomWindow` drop handler check + E2E step 3 (Tasks 5, 6) |
| Preview clip resolution matches the character renderer (idle-equip variants, pose for weapons, missing → hidden) | `AnimationNames.Candidates` reuse (Task 4, `AnimationNames.cs:30-62`); E2E step 2 (Task 6) |
| `GraphicEquipped == 0` → replaced layer hidden, normal equipment NOT restored | `SetCustomGraphic(slot, 0, pose)` semantics (Task 4); E2E (Task 6) |
| Look clear/replace invalidates the preview immediately (no stale replacement) | immediate `SetCustomGraphic(..., 0, 0)` on look change (Task 5) |
| Failed create is retryable (button re-enabled on error) | `_cwcPending` + `ServerMessagePacket` handling (Task 5); E2E step 8 (Task 6) |
| Live tint updates without re-render/server traffic | `SetTint` touches only the shader param (Task 4); E2E step 5 |
| A capped at 200 client-side | slider `MaxValue = 200` (Task 5); server backstop in Part 1 |
| Preview rebuilds on local re-equip | `CharacterUpdated` subscription (Task 4); E2E step 6 |
| Create disabled until slots + name valid | `CustomWindow` enable logic (Task 5); E2E steps 7-8 |
| Preview shows local player's full body with target slot replaced | E2E steps 2, 5 (Task 6); snapshot API reads the same `_slots` source of truth as the renderer (`Character.cs:41`) |
| Window hidden + reset on `CLW` **and** on X-close; close sends WBC not CLW | single Reset routine used by both paths + `OnClosePressed` override (`BankWindow.cs:131-135` pattern); E2E steps 7, 9 (Task 6) |

## Design alignment

- Packet strings exactly as the design doc: `CWS<l>,<s>`, `CWG<id>,<pose>`, `CWC<l>,<s>,<r>,<g>,<b>,<a>,<name>`.
- Frame 28, title `Custom`, buttons `0,1,0,0,1`; Create = OK button.
- Gradient + RGB sliders + A slider (0–200); full-body preview; inventory-only drops; mounts pass validation but have no preview layer; `GraphicEquipped == 0` allowed with no special handling.
- Error handling: pending-`CWS` server message clears the slot; `CWC` failure leaves the window open.
