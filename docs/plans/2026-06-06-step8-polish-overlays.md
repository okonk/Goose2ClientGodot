# Step 8: Polish — Overlays, Targeting, Paper-doll & Deferral Cleanup

> **Status:** SCOPING plan. This enumerates and scopes every item deferred to "Step 8"
> across Steps 6 + 7 so none fall through the cracks. Before *executing*, expand each task
> below into verified-API detail (read the cited Unity source + current Godot APIs and
> confirm signatures), the same way the Step 6/7 plans did. Then drive it task-by-task with
> the subagent-driven-development workflow.

**Goal:** Finish the faithful port by adding the world-space overlays, on-screen spell
targeting, the character paper-doll portrait, and the small carry-over hardening items that
Steps 6 and 7 explicitly deferred — then do the live end-to-end validation pass that the
headless environment could not.

**Reference source (READ-ONLY):** `/home/agent/workspace/Goose2Client/Assets/Scripts/`
(NOT `~/code`). Never modify the Unity project.

**Repo / branch:** `/home/agent/workspace/Goose2ClientGodot`. Create `feat/step8-polish` off
`master` after Step 7 lands.

**Conventions (unchanged from Step 7):**
- Each visual element = a `.tscn` scene + `partial class` script under `Scripts/` (overlays in
  `Scripts/Character/` or a new `Scripts/Overlays/`; UI in `Scripts/UI/`).
- Pure logic (timing, layout, easing math) goes in Godot-free classes added to the test csproj
  (`tests/Goose2Client.Tests/Goose2Client.Tests.csproj` uses **explicit** `<Compile Include>` —
  add each new pure file + its test). Godot-typed files are NOT added to the test project.
- Packet listeners: register in `_Ready` (named methods), guard with `_listenersRegistered`,
  remove in `_ExitTree`.
- Build gate: `dotnet build Goose2ClientGodot.csproj` = 0 errors; `dotnet test …` green.
- Two-stage review (spec, then code-quality) per task; kill subagents between tasks.

---

## Task checklist (every Step-6/7 deferral, explicitly)

### A. Carried-over UI stubs (Step 7) — highest priority, they are already half-wired

- [ ] **A1. `VitalsCharacterDisplay` paper-doll portrait.**
  - **Unity source:** `Assets/Scripts/UI/VitalsCharacterDisplay.cs` — it is a **static
    single-frame portrait** (NOT an animation): five layered `Image`s (body, hair, eyes, chest,
    helmet), refreshed on `GameManager.CharacterUpdated`. Humanoid branch when `BodyId < 100`
    (draw hair/eyes/chest/helmet, `yOffset = -20`); monster branch (`BodyId >= 100`) draws body
    only at `yOffset = 0` and clears the other layers. Tint via the `_Tint` shader (alpha =
    blend factor); "no tint" = alpha 0. Sizes each layer to `frame.Width/Height * 1.25`.
  - **Current Godot state:** `Scripts/UI/VitalsCharacterDisplay.cs` is a stub — resolves
    `LocalPlayer` in `Refresh()` and no-ops rendering. It is **NOT instantiated in any scene**
    and **not referenced anywhere**. (Fix the stub's doc comment: it says "idle-down paper-doll";
    it should be a static layered portrait per the Unity source.)
  - **Prerequisite (do first):** `Scripts/Character/Character.cs` consumes appearance data
    (`BodyId`/`HairId`/`FaceId`/displayed-equipment graphic+color) internally but does **not
    re-expose** it. Add read-only accessors (or an `AppearanceData` snapshot struct) so the
    portrait can rebuild without duplicating the equip-resolution logic.
  - **Godot target:** place a `VitalsCharacterDisplay` (5 `TextureRect`s, each with a per-node
    `_Tint` `ShaderMaterial`) inside the appropriate window scene (confirm whether it belongs on
    the **Character window** or the **Vitals window** — verify against the Unity prefab layout;
    the class name says Vitals but it renders the equipped character). Wire a `CharacterUpdated`
    signal/event on `GameManager` (Unity had `GameManager.CharacterUpdated`) and call `Refresh()`.
    Use `SpriteCache.Get(sheet, graphic)` for frames.
  - **Acceptance:** local player's body/hair/eyes/chest/helmet render and re-render on
    equip/appearance change; monster forms show body-only; dyed slots tint correctly; scene loads
    headless; smoke test still assembles with zero errors.

- [ ] **A2. `SpellTargetManager` on-screen targeting.**
  - **Unity source:** `Assets/Scripts/SpellTargetManager.cs` (singleton; `IsTargeting`,
    `Target`, `Cast(SpellInfo)`) + `Assets/Scripts/SpellTarget.cs`. Honors
    `SpellTargetType` and the `Options.TargetFiltering` setting (both already in
    `Scripts/Constants.cs`). InputMap actions already exist: `TargetDown/Up/ConfirmTarget/
    CancelTarget`.
  - **Current Godot state:** `Scripts/SpellTargetManager.cs` is a stub; `SpellbookWindow.UseSpell`
    already calls it (`// TODO(step8)`). `GameManager.IsTargeting` exists.
  - **Godot target:** implement targeting mode — select/cycle valid `Character` targets, draw a
    target reticle overlay, confirm → `NetworkClient.CastSpell(slot, targetId)`, cancel → exit.
    Pure target-selection/cycling logic (filter + next/prev) → Godot-free + unit-tested.
  - **Acceptance:** casting a targeted spell enters targeting; cycle/confirm/cancel work; filtering
    respects the option; non-targeted spells cast immediately (unchanged).

- [ ] **A3. Sprite-accurate character click + map-item hover tooltip.**
  - **Current Godot state:** `MapManager._UnhandledInput` resolves clicks by **tile** (foot-tile →
    server coords). `// TODO(step8): sprite-accurate character body hit-testing + map-item hover
    tooltip` at `MapManager.cs:190`.
  - **Unity source:** `UI/CharacterClickHandler.cs` (per-sprite raycast click + hover),
    `UI/MapItemTooltip.cs` (already ported as `MapItemTooltipControl`).
  - **Godot target:** pixel/body-accurate hit-testing (per-`Character` `Area2D`/collision or
    sprite-rect test) so clicking the *body* targets the character; on-hover map-item tooltip via
    `MapItemTooltipControl`. **Blocker noted in Step 7:** `MapItem` doesn't carry `ItemStats`, and
    the Control-parent tooltip lifetime doesn't fit a world-space `Sprite2D` — resolve by adding
    item data to `MapItem` and giving the map-item tooltip a world→screen follow path.
  - **Acceptance:** clicking a character's drawn body (not just its foot tile) targets them;
    hovering a ground item shows its name/bind tooltip.

### B. World-space overlays (the roadmap "Step 8 overlays")

- [ ] **B1. Battle text.** Unity: `Character/BattleText.cs` + `Character/BattleTextLine.cs`;
  packet `Network/Packets/BattleTextPacket.cs` (verify it's ported under
  `Scripts/Network/Packets/`). Floating combat numbers/text rising + fading above a character.
  Pure easing/lifetime math → unit-tested.
- [ ] **B2. Chat bubble.** Unity: `ChatBubble.cs`. Speech bubble above a character on `ChatMessage`
  (local-area chat type). Timed auto-hide; pure timeout logic testable.
- [ ] **B3. Emote animation.** Unity: `EmoteAnimation.cs`; packet `Network/Packets/EmotePacket.cs`.
  Emote InputMap actions already defined in `project.godot`.
- [ ] **B4. Spell animation overlay.** Unity: `SpellAnimation.cs`. On-cast/on-hit spell visual
  played at a target/world position.

### C. Lighting & visual polish

- [ ] **C1. 2D lighting.** Unity used URP 2D lights. Godot target: `PointLight2D` +
  `CanvasModulate` for day/night / light sources (see MIGRATION_PLAN tech-mapping table). Confirm
  what drives lighting server-side before building.
- [ ] **C2. `TextureProgressBar` art.** HP/MP/XP/cooldown bars drive correct `Value` but render
  nothing without a `texture_progress` asset. Assign bar art.
- [ ] **C3. Dyed-gear color space.** If dyed gear looks off vs Unity, revisit sRGB/linear in the
  `_Tint` shader (`source_color` hint / mix space). (Step 6 deferral.)

### D. Settings & small hardening

- [ ] **D1. Window visibility persistence.** Positions persist via
  `CharacterSettings.WindowSettings`; per-window *visibility* (open/closed) is not saved yet —
  add it if desired. (Step 7 deferral.)
- [ ] **D2. `NetworkClient` "Disconnected" event.** On `Receive() == 0` (graceful remote close),
  marshal a main-thread disconnect event so a future reconnect/session layer has a hook.
  `ReceiveLoop` currently just exits silently. (Step 4/network deferral.)
- [ ] **D3. `IsValidMove` occupancy check.** Local-player prediction blocks only on map-blocked
  tiles, not tiles occupied by another character — add a `_characters` occupancy test. (Step 6
  deferral.)
- [ ] **D4. Converter asset gaps (e.g. `Hair/16`).** Server sends graphic ids the converter didn't
  emit; slots skip gracefully (renders bald). Regenerate assets / fix converter coverage. (Step 6
  deferral — may belong to the asset-pipeline, not this branch.)
- [ ] **D5. Verify staff/2h/bow attack clips (BodyState 5/6/7).** Implemented but only 1hand
  (state 4) was live-verified — confirm the others when such a character is available. (Step 6
  deferral; folds into E1.)

### E. Validation (the Step 7 gap)

- [ ] **E1. Live end-to-end pass** against `GOOSE_HOST=scyther.local GOOSE_PORT=2006` on a desktop
  with a display (this environment had none + no `run` skill + no test credentials). Walk every
  flow Step 7 could only smoke-test headless: drag inventory→inventory/hotbar/world/destroy,
  hotbar use + paging, spellbook cast + cooldown + **targeting (A2)**, vendor buy/sell, bank
  deposit/withdraw, combine, chat send + `/`-commands + tell/reply + history, party vitals, buff
  add/remove, options persistence, **window position + visibility persistence across relog**, and
  capture screenshots. Confirm the Step 7 "reasonable default" slot counts (inventory 30,
  equipped 14, spellbook 8×30, hotbar 3×10, vendor 40, bank 30, combine 10, party 8, buffs 20)
  against the live server and adjust consts if needed. Verify B1–B4 overlays in-world and D5
  attack clips. **Part 1 bugfix additions (2026-07-11):**
  - Warp between maps via a door/teleport tile, including a back-to-back warp
  - Chat clears and unfocuses on death/warp
  - First "previous target" press reaches the bottom-most candidate
  - Attack cadence before the first WPS packet ≈ 1/s; mounted attack suppressed

---

## Suggested order
1. **A1** (paper-doll + Character appearance accessors) — self-contained, unblocks the character UI.
2. **A2** (targeting) — completes the spellbook flow already wired in Step 7.
3. **B1–B4** (overlays) — share a "world-space follow + timed fade" pattern; build that helper once.
4. **A3** (sprite clicks + map-item tooltip) — needs `MapItem` data + world→screen tooltip follow.
5. **C/D** small items, then **E1** the full live validation pass (covers D5 and B verification).

## Notes
- Items D4 (asset coverage) and C1 (lighting) may be out of scope for a UI/overlay branch — split
  them out if they balloon. D2/D3 are tiny and safe to fold in.
- Update `MIGRATION_PLAN.md`'s deferred sections to mark each item ✅ Resolved (Step 8) as it lands,
  exactly as Steps 6/7 did.
