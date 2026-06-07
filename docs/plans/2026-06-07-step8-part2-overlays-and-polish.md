# Step 8 — Part 2: Overlays, Clicks, Polish & Validation

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans (or subagent-driven-development) to implement this plan task-by-task.
>
> This is **Part 2 of 2** of the execution-ready Step 8 plan. **Part 1 must be merged first** —
> `docs/plans/2026-06-07-step8-part1-correctness-and-foundations.md` builds the prerequisites this part
> consumes (`Character.GetAppearance/Height/CharacterType/Cast()/AddBattleText`,
> `GameManager.CharacterUpdated`, `MapManager.Characters`, `Overlays.OverlayLifetime`/`WorldOverlay`).
> Read Part 1's **Review findings** and **APIs Verified** sections — they apply here too and are not
> repeated in full.

**Goal (Part 2):** Add the four world-space overlays, body-accurate character clicks + ground-item
tooltips, visual polish, per-window visibility persistence, and the live end-to-end validation pass
the headless environment couldn't run.

**Architecture:** Overlays subclass `Overlays.WorldOverlay` (Part 1 Task 6) and instance as children
of the target `Character` (or the map, at a tile). Pure layout/timing logic → Godot-free + tested.
**Unit conversion note:** Unity overlay positions divide by 32 (world units); **Godot positions are
pixels** — drop the `/32` and offset relative to `Character.Height` (px). Overlay packet listeners go
in BOTH the `_Ready` listen block and `_ExitTree` remove block of `MapManager.cs` (`_listenersRegistered`
already guards, `:65`/`:77`).

**Tech Stack:** Godot 4.6 / C# (.NET 10), xUnit (explicit `<Compile Include>`).

**Repo / branch:** off `master` after Part 1 merges → `feat/step8-part2`.

**Build gate (every task):** `dotnet build Goose2ClientGodot.csproj` (0 errors) +
`dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj` (green).

---

## APIs Verified (Part-2 relevant; see Part 1 for the full list)

Godot port (`Scripts/`):
- `MapManager.cs:52-65`/`:78-90` listener register/remove block; `:182-196` `_UnhandledInput`
  (tile-only click, TODO `:190`); `:234-248` `OnMapObject` (where to store `ItemStats`); `:243` item create.
- `Map/MapItem.cs:7` `MapItem : Sprite2D`, `Setup(tex,tileX,tileY,tint)` — **no** `ItemStats`, no hover.
- `ItemStats.cs:101-118` `ItemStats.FromPacket(MapObjectPacket)` exists.
- `UI/MapItemTooltipControl.cs` — `SetItem(ItemStats, Control parent)`, `_Process` auto-hides on
  `!_parent.IsVisibleInTree()`, `PositionTooltip()` follows mouse. Never invoked yet.
- `UI/TooltipManager.cs` — `ShowMapItemTooltip/HideMapItemTooltip/HideMapItemTooltipIfMatching` (wired).
- `CharacterSettings.cs:29-32` `WindowSettings { Vector2 Position; }` (no `Visible`); `:152`
  `GetWindowSettings`; `:160` `SetWindowSetting(name, Vector2?)`; `JsonOptions` `:36`.
- `UI/BaseWindow.cs:33-38` restore position; `:86` `Toggle()`; `:88` `OnClosePressed`→`Hide()`.
- `Character/Character.cs:175-205` slots built (`AnimatedSprite2D` per `CharacterSlot`); `:209-218` tint
  shader; **Part 1 added** `GetAppearance()`, `Height`, `CharacterType`, `Cast()`, `AddBattleText`.
- Overlay packets present & field-matched: `BattleTextPacket` (`LoginId`,`BattleTextType`,`Text`,`Name`),
  `ChatPacket` (`LoginId`,`Message`), `EmotePacket` (`LoginId`,`AnimationId`,`GraphicFile`),
  `CastPacket` (`LoginId`), `SpellCharacterPacket` (`LoginId`,`AnimationId`), `SpellTilePacket`
  (`TileX`,`TileY`,`AnimationId`).

Unity reference overlay constants (verified):
- BattleText: max **18** lines (`BattleText.cs:15`); rise **1.0 u/s** (`BattleTextLine.cs:68`); life
  **1.0s** (`:65`); spread x∈{12,-4,4}px, y rows of 8px max 2 (`BattleText.cs:45-54`); colors
  red(154,0,0)/green(136,204,64)/yellow(248,208,0) (`BattleTextLine.cs:11-13`); text overrides
  Stunned→"STUNNED", Rooted→"ROOTED", Dodge20→"DODGE", Miss21→"MISS" (`:34-47`).
- ChatBubble: life **3.0s** (`ChatBubble.cs:23`); maxWidth 250, padding (7,5) (`:18-19`); y-position
  magic 0.4355469 (`MapManager.cs:371`); **one** bubble per character (`:362`).
- Spell: yOffset `-max((h-48)/2,0)-24` (`SpellAnimation.cs:23`); life = clip length (`:51`); height
  default 64 (`AnimationManager.cs:84`). Emote pos `(0.5, y/32-0.75)` (`EmoteAnimation.cs:10-11`).
- `MapManager.cs:243-251` `OnCast`→`Character.Cast()`; `:267-292` spell/battletext; `:331-372` emote/bubble.
- `MapItem.cs` carries `ItemStats Item`; `UI/CharacterClickHandler.cs` rect hit-test (BoxCollider, no
  alpha threshold), left/right click send char tile X/Y.

---

## Task 0: Branch setup
`git checkout master && git pull` (ensure Part 1 merged) `&& git checkout -b feat/step8-part2`.
Confirm build + tests green before starting.

---

## Task 1: B1 — Battle text

**Pure logic** (`Scripts/Overlays/BattleTextLayout.cs` + `tests/.../BattleTextLayoutTests.cs`):
the spread offset cycle (`BattleText.cs:17-55`: position cycles 0..8; `y += min(childCount/3,2)*8`;
x ∈ {12,-4,4}) and the color/text-override map (`BattleTextLine.cs:11-48`). Test the offset cycle and
**every** `BattleTextType` → (color, displayText) mapping.

**Godot:**
- `Scripts/Overlays/BattleText.cs` — container (Part 1 added the lazy hook + a no-op stub; replace the
  stub with the real node). Caps at **18** lines (`BattleText.cs:15`); positions each line via
  `BattleTextLayout`.
- `Scripts/Overlays/BattleTextLine.cs : WorldOverlay` — a `Label` (outline like the name label
  `Character.cs:54-69`); `OverlayLifetime(1.0, risePixelsPerSecond: <1u≈ px/s>)`; color/text from
  `BattleTextLayout`.

**Listener:** `MapManager` `Listen<BattleTextPacket>(OnBattleText)` (+ remove) →
`GetCharacter(p.LoginId)?.AddBattleText(p.BattleTextType, p.Text)`.

**Steps:** failing layout tests → run FAIL → implement layout + add to csproj → run PASS → build the
two Godot nodes + listener → build 0 errors, smoke zero-error → commit
`feat(overlays): floating battle text (B1)`.

---

## Task 2: B2 — Chat bubble

**Pure logic** (`Scripts/Overlays/ChatBubbleLayout.cs` + tests): size clamp (maxWidth 250, padding
(7,5)) and the **3.0s** lifetime. Test the clamp at/above/below maxWidth and the timeout.

**Godot:** `Scripts/Overlays/ChatBubble.cs : WorldOverlay` — `Label`/`RichTextLabel` + `Panel`/
`NinePatchRect` background; positioned above head using `Character.Height` (port the `0.4355469`
constant as px, verify by eye); **one** bubble per character (destroy existing first, Unity
`MapManager.cs:362`).

**Listener:** `MapManager` `Listen<ChatPacket>(OnChatBubble)` (+ remove) → spawn/replace bubble on the
character. **Note:** `ChatWindow` already listens to `ChatPacket` for the text log — a second listener
is fine (PacketManager is multi-subscriber); confirm both coexist.

**Steps:** failing layout/lifetime tests → FAIL → implement + csproj → PASS → Godot node + listener →
build/smoke green → commit `feat(overlays): speech chat bubble above speaker (B2)`.

---

## Task 3: B3 — Emote

**Godot:** `Scripts/Overlays/EmoteAnimation.cs : WorldOverlay` — `AnimatedSprite2D` playing the emote's
`SpriteFrames` (resolve by `AnimationId`), positioned `(0.5*32, Height − 0.75*32)`-equiv in px (Unity
`EmoteAnimation.cs:10-11`), life = clip length, self-free on `AnimationFinished`; destroy existing
emote first (Unity `MapManager.cs:343`).

**Verify before coding:** the converter's emote/animation `SpriteFrames` output folder + naming —
confirm the `res://Assets/Sprites/...` path for emote id `AnimationId`.

**Listener:** `MapManager` `Listen<EmotePacket>(OnEmote)` (+ remove) → `ShowEmote(p.AnimationId, character)`.

**Steps:** verify asset path → implement node + listener → build/smoke green → commit
`feat(overlays): emote animations over characters (B3)`.

---

## Task 4: B4 — Spell animation + CastPacket

**Godot:** `Scripts/Overlays/SpellAnimation.cs : WorldOverlay` — `AnimatedSprite2D` for spell id; life
= clip length; offset `-max((Height-48)/2,0)-24`-equiv px (Unity `SpellAnimation.cs:23`); height
default 64.

**Verify before coding:** spell `SpriteFrames` path/naming (Unity `spell-{id}`) — confirm the
converter emitted them and the load path.

**Listeners** in `MapManager` (all + matching removes):
- `Listen<SpellCharacterPacket>(OnSpellCharacter)` → spawn on the character (`p.LoginId`).
- `Listen<SpellTilePacket>(OnSpellTile)` → spawn at tile `(p.TileX, p.TileY)` under the map via
  `MapCoords` (packet already applied the `-1`/`+0.5` offsets).
- `Listen<CastPacket>(OnCast)` → `GetCharacter(p.LoginId)?.Cast()` — the **caster's** pose (the
  under-scoped review item; `Cast()` was added in Part 1 Task 5).

**Steps:** verify asset path → implement node + 3 listeners → build/smoke green → commit
`feat(overlays): spell cast + impact animations incl. remote caster pose (B4)`.

---

## Task 5: A3 — Body-accurate character clicks + map-item hover tooltip

**Files:** `Scripts/Map/MapItem.cs`, `Scripts/MapManager.cs:182-196` & `:234-248`,
`Scripts/Character/Character.cs`, `Scripts/UI/MapItemTooltipControl.cs`, `Scripts/UI/TooltipManager.cs`.

**Step 1 — MapItem carries data + hover.** Add `ItemStats Item` to `MapItem`; since `Sprite2D` doesn't
get GUI mouse events, add an `Area2D` child with a `CollisionShape2D` sized to the texture
(`input_pickable`), wired to `mouse_entered`/`mouse_exited` → `TooltipManager.ShowMapItemTooltip(Item,…)`
/ `HideMapItemTooltip()`. Set `Item = ItemStats.FromPacket(p)` in `MapManager.OnMapObject` (`:243`).
**Step 2 — tooltip parent lifetime.** `MapItemTooltipControl` auto-hides on `!_parent.IsVisibleInTree()`,
which assumes a `Control`. Change `SetItem` to accept the `MapItem` (`Node2D`) and null-check
`IsInstanceValid(_owner) && _owner.Visible` instead; update the `TooltipManager` call site.
**Step 3 — character body click.** Add a body-rect hit-test on `Character` (mirrors Unity's
`BoxCollider2D` — a rectangle, NOT per-pixel):
```csharp
public bool ContainsPoint(Vector2 worldPoint)
{
    if (!_slots.TryGetValue(CharacterSlot.Body, out var b) || b.Sprite.SpriteFrames == null) return false;
    var tex = b.Sprite.SpriteFrames.GetFrameTexture(b.Sprite.Animation, b.Sprite.Frame);
    if (tex == null) return false;
    var size = tex.GetSize();
    var rect = new Rect2(GlobalPosition + b.Sprite.Offset - new Vector2(size.X/2, size.Y), size);
    return rect.HasPoint(worldPoint);
}
```
In `MapManager._UnhandledInput` (replace TODO `:190`): find the topmost character whose
`ContainsPoint(mouseWorld)` is true; if found send `LeftClick/RightClick(c.X, c.Y)`; else fall back to
the tile under the cursor (current behavior).
**Step 4 — build + test; headless smoke zero-error. Step 5 — commit**
`feat(world): body-accurate character clicks + ground-item hover tooltip (A3)`.
**Acceptance:** clicking a character's drawn body (overhang included) targets them; hovering a ground
item shows its name/bind tooltip.

---

## Task 6: C — Visual polish (bars art, color space, lighting)

- **C2 — `TextureProgressBar` art.** HP/MP/XP/cooldown bars drive `Value` but render nothing without a
  `texture_progress`. Author/assign simple bar textures in `VitalsWindow`, `HotbarWindow` (XP), spell
  cooldown. Verify each scene loads headless.
- **C3 — dyed-gear color space (conditional).** Only if E1 shows dyed gear looks off vs Unity: revisit
  the `_Tint` shader `source_color` hint / sRGB-vs-linear mix space (`Character.cs:212-217`). May be a no-op.
- **C1 — 2D lighting (conditional/optional).** Review confirmed **no server light packet** — Unity uses
  one static global Light2D at full white. At most a `CanvasModulate` for fixed ambient; build only if a
  visual diff vs Unity needs it, else document as intentionally skipped.

**Commit:** `feat(ui): TextureProgressBar art; review dyed-gear color space + ambient (C)`.

---

## Task 7: D1 — Window visibility persistence

Persist open/closed per window (positions already persist). Additive (Unity also saved Position only).

**Files:** `Scripts/CharacterSettings.cs` (+ `tests/.../CharacterSettingsJsonTests.cs` — already in test
csproj), `Scripts/UI/BaseWindow.cs`.

**Step 1 — failing test:** round-trip a `WindowSettings` with `Visible=false` through `Serialize`/
`FromJson`; assert it survives.
**Step 2 — run FAIL. Step 3 — implement:** add `public bool Visible = true;` to `WindowSettings`
(`:29-32`); extend `SetWindowSetting` with an optional `bool? visible = null` to persist it.
**Step 4 — run PASS. Step 5 — wire BaseWindow:** restore `Visible = ws.Visible` in `_Ready` (`:33-38`);
persist via `SetWindowSetting(WindowName, visible: Visible)` in `Toggle()`/`OnClosePressed()`/show.
**Step 6 — build + test green; HUD smoke zero-error. Step 7 — commit**
`feat(ui): persist per-window visibility across relog (D1)`.

---

## Task 8: D4 — Converter asset gap (Hair/16 etc.) — DOCUMENT ONLY

Confirmed: `Assets/Sprites/Hair/` has ids 1–15, 17–28 (16 absent). The converter emits exactly the ids
present in `compiled.enc`; a missing id is a **source-data hole**, not a converter bug, and the client
degrades gracefully (`Character.ApplySlot:177-179` renders bald on missing `.tres`). **No code change.**
Add a note to `MIGRATION_PLAN.md` D4 marking it source-data / out of scope, + optional backlog item to
re-run the converter if the source asset is obtained.
**Commit:** `docs: mark D4 (Hair/16 gap) as source-data, out of scope; graceful fallback confirmed`.

---

## Task 9: D5 — Verify staff/2h/bow attack clips (data check)

No code change expected: converter names (`AnimationNaming.cs:33-43`) and runtime candidates
(`AnimationNames.cs:21-55`) already match for BodyState 4–7. Confirm representative staff (5) / 2h (6) /
bow (7) weapon graphic `.tres` files contain `attack-staff/2hand/bow-<dir>` (or correctly fall back).
Do this as a data inspection now and again live in Task 10. If a clip is missing where it should exist,
file a converter coverage bug.
**Commit (if notes only):** `docs: record D5 attack-clip verification results`.

---

## Task 10: E1 — Live end-to-end validation pass

**Environment:** a desktop with a display, real test credentials.
```bash
GOOSE_HOST=scyther.local GOOSE_PORT=2006 godot --path /home/hayden/code/Goose2ClientGodot
```
**Walk every flow (capture screenshots):**
- **Part 1 critical fixes:** idle past the server ping interval → stays connected; attack cadence
  respects weapon speed; can't walk onto occupied tiles.
- **Step-7 backlog:** drag inventory→inventory/hotbar/world/destroy; hotbar use + paging; spellbook
  cast + cooldown + **targeting (A2)**; vendor buy/sell; bank deposit/withdraw; combine; chat send +
  `/`-commands + tell/reply + history; party vitals; buff add/remove; options persistence; **window
  position + visibility persistence across relog (D1)**.
- **Step-8 features:** paper-doll portrait updates on equip (A1); battle text / chat bubble / emote /
  spell animations in-world (B1–B4); remote caster cast pose (CST/B4); body-accurate clicks +
  ground-item tooltip (A3); **staff/2h/bow attack clips (D5)**.
- **Slot counts:** confirm Step-7 defaults (inventory 30, equipped 14, spellbook 8×30, hotbar 3×10,
  vendor 40, bank 30, combine 10, party 8, buffs 20) vs live; adjust consts if needed.
- **Visual diffs:** dyed gear (C3), bar art (C2), ambient/lighting (C1) — fix or confirm skip.

**On completion:** update `MIGRATION_PLAN.md` — mark Step 8 ✅ Landed and tick each deferred item
(Step-6/7 lists + the two new critical fixes) ✅ Resolved (Step 8). Final commit:
`docs(step8): record Step 8 landed + live E2E results`. Merge `feat/step8-part2` — port complete.

---

## Open verifications resolved during execution (no plan-time punts)
- **Tasks 3/4:** confirm the converter's emote/spell `SpriteFrames` output folder + naming before the
  resource load.
- **Task 2:** confirm the ChatBubble y-position pixel offset by eye (Unity used `Height/32 + …0.4355469`).
- **Task 5:** confirm the `Area2D`/`input_pickable` hover approach vs a `MapManager` mouse-motion poll
  of `_mapObjects` — pick whichever fits the existing input flow with least duplication.
