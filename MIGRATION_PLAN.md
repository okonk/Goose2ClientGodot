# Goose2Client → Godot Migration Map (Phase 1)

> Phase 1 deliverable: an inventory of the Unity project and a system-by-system map to
> Godot equivalents, with risk flags and a recommended porting order. No code is ported
> in this phase. Later phases execute the order at the bottom.

Source (Unity): `~/code/Goose2Client`
Target (Godot 4.6, C#/.NET): `~/code/Goose2ClientGodot`
Original game data: `~/code/Illutia/{data,maps}` (4,956 `.adf`, `compiled.enc`, 114 `.map`)
Live server (for testing): `scyther.local:2006` — override the client via the `GOOSE_HOST` /
`GOOSE_PORT` env vars (default `game.illutia.net:2006`). Saved credentials live at
`user://login.cfg`.

## Locked-in strategy decisions

| Decision | Choice | Why |
|---|---|---|
| Source of truth | The **Unity project** | AsperetaClient is a *different* game; do not use it. |
| Language | **C# / .NET** | Project already has `[dotnet]`; the engine-agnostic core ports nearly 1:1. |
| Assets | **Regenerate from `.adf` source → Godot-native PNG/atlas** | Source data is intact; avoids unreadable Unity AssetBundles entirely. |

## What this project actually is

A networked multiplayer 2D RPG **client** (not a small game): ~140 C# scripts / ~9k LOC.

- **TCP text protocol** over a raw socket, `\x1`-delimited packets, ~55 packet types.
- **5-layer tilemap** world with runtime tile updates, map items, roof toggling.
- **Layered sprite-animation** characters (body + hair + eyes + equipment), 11 action
  types × 4 directions, driven by Unity's `Animator` + `AnimatorOverrideController`.
- **~40 uGUI windows** (inventory, hotbar, spellbook, chat, vendor, bank, party, etc.)
  with drag/drop and tooltips.

---

## System inventory & Unity → Godot mapping

Risk legend: 🟢 mechanical / near-verbatim · 🟡 needs redesign but well-understood · 🔴 hard, no direct equivalent.

### 1. Network & protocol — 🟢 ports ~1:1
Files: `Network/NetworkClient.cs`, `PacketManager.cs`, `PacketParser.cs`, `PacketHandler.cs`, `Network/Packets/*.cs` (~55)

- `NetworkClient` is a plain C# class using `System.Net.Sockets.Socket` — **no Unity
  dependency**. Copy verbatim. Only `Debug.Log` → `GD.Print`.
- ✅ **Decided & implemented (background thread).** The Unity version polled from
  `GameManager.Update()`. The Godot port moves the blocking `Receive` to a background
  `System.Threading` thread (`NetworkClient.ReceiveLoop`, `IsBackground`) and marshals every
  parsed packet to the main thread via `dispatcher.CallDeferred("HandlePacket", ...)`. The
  receive thread touches only the socket + `packetBuffer`; all Godot/scene-tree access (incl.
  `GD.Print` and the `SocketError` event) is marshaled. The old `Select(..., 500)` poll is gone.
  Step 6 character packet handlers therefore run on the main thread already — no extra marshaling.
- `PacketManager` pub/sub (`Listen<T>` / `Remove<T>` / `Handle`) is plain C#; keep as-is.
- Every `*Packet` class is a pure parser/POCO. Copy verbatim.

### 2. Singletons / managers → Godot autoloads — 🟡
Files: `GameManager.cs`, `PlayerInputManager.cs`, `AnimationManager.cs`, `SpellCooldownManager.cs`, `SpellTargetManager.cs`, `ResourceManager.cs`

- `GameManager` uses the `DontDestroyOnLoad` + static `Instance` singleton pattern →
  register as a Godot **autoload** in `project.godot`. `GameManager.Instance.X` calls
  stay valid.
- `Awake()` → `_EnterTree`/`_Ready`; `Start()` → `_Ready`; `Update()` → `_Process(delta)`;
  `OnDestroy()` → `_ExitTree` or `_Notification(NOTIFICATION_PREDELETE)`;
  `OnApplicationQuit` → `_Notification(NOTIFICATION_WM_CLOSE_REQUEST)`.
- `AnimationManager` is a plain class that parses two text files — copy verbatim; load the
  text via `FileAccess`/`ResourceLoader` instead of `Resources.Load<TextAsset>`.
- `ResourceManager` is the most Unity-coupled manager (`Resources.Load`, `SpriteAtlas`,
  `AssetBundle`). **Rewrite** against the new Godot asset layout (see §8) — the AssetBundle
  paths disappear entirely.

### 3. Scene flow & lifecycle — 🟡 (architectural redesign)
Files: `LoginScene/LoginButton.cs`, `LoadingScene/LoadingScene.cs`, `LoadingMapScene/LoadingMapScene.cs`, `GameManager.ChangeMap/LoadMapAsync`, `ResolutionScript.cs`

Flow: **Login → Loading → LoadingMap → Map** (repeating LoadingMap↔Map on every map change).

The Unity version does additive `SceneManager.LoadSceneAsync` + `MoveGameObjectToScene` to
**carry the persistent UI across scene loads**. Godot handles this differently and more
simply:

- Put the persistent HUD/UI under a **CanvasLayer in an autoload** (or a long-lived root
  node) so it survives `ChangeSceneToPacked` — no object-moving needed.
- Map changes: `GetTree().ChangeSceneToPacked(packedScene)` (or manual add/remove of a map
  subtree under a persistent root if you want to keep the UI node instances alive).
- Coroutines (`IEnumerator` + `yield return null` / `WaitForSecondsRealtime`) →
  `async`/`await` with `await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout)`
  and threaded `ResourceLoader.LoadThreadedRequest`/`LoadThreadedGet` for async map loads.

### 4. World / map rendering — 🔴
Files: `MapManager.cs`, `MapFile.cs`, `MapItem.cs`, prefabs `Camera`, `Lighting`, `MapItem`

- `MapFile` parses the map `.bytes` (tile/flags grid) — pure C#, port verbatim. Map source
  `.map` files at `~/code/Illutia/maps` (the converter copies them to `M*.bytes`).
- Unity **`Grid` + 5 `Tilemap` layers** → **NOT `TileMapLayer`/`TileSet`** (revised 2026-06-06,
  Step 5). The source frames are **arbitrary-rect and bottom-center-anchored** (a tree/roof
  sprite is taller than its 32 px tile); Godot's `TileSetAtlasSource` is grid-locked, so honoring
  the original §4 would force repacking every tile into a uniform grid and authoring per-tile
  `texture_origin`. Instead each of the 5 layers is a `MapLayer : Node2D` that **draws its tiles in
  `_Draw`** by blitting `AtlasTexture` regions straight off the original sheet PNGs, via a runtime
  `SpriteCache` (the Godot replacement for Unity's `ResourceManager.LoadSprite("{sheet}-{graphic}")`
  / `Helpers.GetSprite`) backed by a converter-emitted **frame-rect manifest** (`manifest.json`:
  `sheet → {graphic → [x,y,w,h]}`). Lossless, no repacking, y-sorts naturally with Step 6 entities.
- **Runtime tile updates** (`TileUpdatePacket`): mutate the in-memory `MapFile` cell
  (graphic/sheet, `sheet == 0` ⇒ clear) + `Flags`, then `QueueRedraw()` the affected `MapLayer`.
- **Roof layer** toggle (`SetActive`) → set the roof layer's `Visible` (`MapLayer[4].Visible = !IsRoof`).
- `SpriteRenderer.sortingOrder` for depth → `Node2D.YSortEnabled` plus per-layer `z_index`.
- **Camera**: Cinemachine `CinemachineVirtualCamera.Follow` → a `Camera2D` whose `GlobalPosition`
  is centred on the player's spawn tile. **Corrected 2026-06-06:** the server does **not** send
  `SetYourPositionPacket` (SUP) on map entry — only on warps / rejected moves / `/refresh`. At entry
  it sends `MakeCharacter` (MKC, carries each character's tile) then `SetYourCharacter` (SUC, which
  `LoginId` is you), so the camera reads the player's spawn tile from MKC+SUC (camera bootstrap
  only — no `Character` node; full character rendering is Step 6). SUP still recentres on
  warp/refresh. Drop Cinemachine entirely.
- ✅ **Coordinate system (resolved 2026-06-06)**: Godot 2D is **Y-down like the server's tile rows**,
  so there is **NO Y-flip** — tile `(x,y)` maps to world `(x,y)`. Unity's pervasive `map.Height - y`
  existed only to reach Unity's Y-up world and is intentionally absent. All tile↔world math lives in
  one helper, `MapCoords` (`TileSize = 32`, bottom-center anchoring); the flip is simply not there.

### 5. Characters & world entities — 🔴 (animation is the hardest part)
Files: `Character/Character.cs`, `PlayerController.cs`, `CharacterAnimation.cs`, `CharacterAnimationStateBehaviour.cs`, `CharacterHealthBar.cs`, `BattleText*.cs`, `ChatBubble.cs`, `SpellAnimation.cs`, `EmoteAnimation.cs`, `SpellTarget.cs`, `AnimationSlot.cs`, `CharacterSettings.cs`

- `Character` (424 LOC) is mostly state + packet handling → port to a `Node2D` script.
- `PlayerController` is `AddComponent`-ed at runtime onto the local player. Godot has no
  add-component model; instead attach the script/scene conditionally, or gate behavior with
  an `IsLocalPlayer` flag on the single Character script.

#### Animation redesign — validated against a working reference

> **Reference implementation: `~/code/3dMMO-Server/client`** — a production Godot 4.6 + C#
> MMO client that already solves this exact problem (no Unity `Animator`). Its
> `Assets/Scripts/Entity/Character.cs` is the template; mirror its conventions, then extend
> from its single-sprite model to Goose2's layered paper-doll. Key bits to copy directly:
> runtime `SpriteFrames` swap, `{state}-{direction}` naming, and the attack-lock.

**`CharacterAnimation` + `Animator` + `AnimatorOverrideController` is the marquee risk, but
the path is now known.** Unity uses one shared state machine (idle/walk/attack/cast × 4
directions) and *overrides* the clips per equipped graphic (body-1, helm-12, …). Godot has
no override-controller equivalent; reproduce it with **per-slot `AnimatedSprite2D` nodes +
runtime `SpriteFrames` swap + C# state logic**:

- **Layered nodes (the one extension over the reference).** The reference uses *one*
  `AnimatedSprite2D` per entity (pre-composed art). Goose2 is a paper-doll: body + hair +
  eyes + chest + helm + legs + feet + hand. Use **N stacked `AnimatedSprite2D` nodes, one
  per slot**, z-ordered, all children of the character body.
- **Per-graphic `SpriteFrames`, swapped on equip.** Each slot loads its own resource by id,
  exactly as the reference does for the body — this *is* the override-controller behavior:
  ```csharp
  // per slot, when equipment changes (cf. reference Character.SetBody)
  var frames = GD.Load<SpriteFrames>($"res://Assets/Sprites/{slot}/{graphicId}/animations.tres");
  slotSprite.SpriteFrames = frames;
  ```
- **Drive all layers together** so they stay frame-locked. Replace the Animator's
  bool/float/trigger params (`SetBool`, `SetFloat`, `SetTrigger`, `Constants.Attack`/`Cast`)
  with one call fanned out to every slot:
  ```csharp
  // cf. reference: animated.Play($"{state}-{direction.ToLower()}")
  foreach (var s in slots) s.Play($"{state}-{direction}");
  ```
  Use the reference's `{state}-{direction}` animation names (`idle-down`, `walk-left`,
  `attack-up`) as the convention the converter (§8) emits.
- **Attack-lock instead of trigger transitions.** Copy the reference's `isAttackAnimationLocked`
  + timer (duration from `SpriteFrames.GetFrameCount / GetAnimationSpeed`) so walk/idle don't
  clobber a mid-attack/cast animation. `CharacterAnimationStateBehaviour` (a Unity
  `StateMachineBehaviour`) → drop; handle completion via the timer or the
  `AnimatedSprite2D.AnimationFinished` signal.
- **Foot anchoring & heights.** The reference anchors via `animated.Offset.Y = footOffsetY`
  and per-body dimensions from `bodies.json`. Goose2's `AnimationHeights.txt` /
  `AnimationToFirstFrame.txt` map onto the same idea — carry them into a metadata sidecar
  (§8) and apply per-slot offset so layers align. **Load-bearing — do not drop.**
- **Per-graphic tint** (`material.SetColor("_Tint")`) → `CanvasItem.Modulate`/`SelfModulate`
  per slot for a simple multiply, or a small `ShaderMaterial` if the blend must match exactly.

- Overlay entities (battle text, chat bubble, health bar, spell/emote animations) → small
  Godot scenes instanced as children; `Instantiate(prefab, parent)` →
  `packedScene.Instantiate()` + `AddChild`. (The reference does precisely this for
  `FloatingBattleText`, `ChatBubble`, `EntityBars`, and `SkillEffect`.)

### 6. UI — 🟡 high volume, mechanical (~40 windows)
Files: all of `Scripts/UI/*.cs`, all `Prefabs/UI/*.prefab`

- uGUI `Canvas`/`Image`/`Text`/`Button` → Godot `Control` tree (`Panel`, `TextureRect`,
  `Label`/`RichTextLabel`, `Button`, `NinePatchRect` for window frames).
- **Drag & drop** (`DragIcon`, `DropTarget`, `DropTargetManager`, item/spell/hotbar slots) →
  Godot's built-in `_GetDragData` / `_CanDropData` / `_DropData`. This replaces the custom
  pointer plumbing and is cleaner.
- **Tooltips** (`TooltipManager`, `ItemTooltip`, `SpellTooltip`, etc.) → a Godot tooltip
  Control toggled on `mouse_entered`/`mouse_exited`, positioned manually.
- **TextMeshPro** → Godot `Label`/`RichTextLabel` + imported `FontFile`s.
- `BaseMultipleWindow*`, `TitleBar`, `WindowTransparency`, `IWindow` → a shared base
  `Control` scene + script; window transparency → `modulate.a` / theme.
- This is the single largest bucket by file count but each window is an isolated,
  prefab→`.tscn` + script port. Good candidate to parallelize once the base window scene,
  slot, and drag/drop primitives exist.

### 7. Pure data / utility — 🟢 copy verbatim
Files: `Constants.cs`, `Direction.cs`, `Colors.cs`, `ColorExtensions.cs`, `Helpers.cs`, `ItemStats.cs`, `SpellInfo.cs`, `WindowButtons.cs`, `WindowFrames.cs`

- Mostly engine-agnostic. `Helpers.GetSprite` and `ColorH`/`Color` touch UnityEngine →
  swap `UnityEngine.Color` for `Godot.Color` and re-point `GetSprite` at the new atlas.

### 8. Asset pipeline (the converter) — 🔴 rebuild, but logic is reusable
Files: `Editor/IllutiaData.cs`, `Editor/GifLoader.cs`, `Editor/ToolsMenu.cs`, `Editor/BuildScript.cs`, `Editor/NonDrawingGraphicEditor.cs`

The converter reads `~/code/Illutia/data/*.adf` + `compiled.enc` and produces spritesheets,
animation frame sequences, and maps. **Split it: keep the parsing, replace the Unity output.**

- ✅ **Port verbatim** (pure C#, no Unity): `AdfFile`/`CompiledEnc`/`Animation`/`Frame`
  (`IllutiaData.cs`) and the `GifLoader` GIF decoder. These decode the `.adf` container,
  the encoded frame/animation tables, and the embedded GIF pixels.
- 🔁 **Replace the Unity-specific output** in `ToolsMenu.cs`:
  - `Texture2D` + `EncodeToPNG` → Godot `Image.SavePng` (the `flippedTexData` Y-flip step
    may differ — verify against Godot's image origin).
  - Unity sprite-slicing (`SpriteRect`, `PostProcessImportSpritesheet`, the
    `{fileId}-{graphicId}` sub-sprites, `BottomCenter` alignment, `pixelsPerUnit=32`,
    `FilterMode.Point`) → emit a **frame-rect manifest** (JSON/`.tres`) and build
    `AtlasTexture`s / `SpriteFrames` from it at import or runtime.
  - `CreateAnimation` (`.anim` clips) + `BuildAssetBundles` (per-graphic bundles) →
    **`SpriteFrames` `.tres` resources** (one per `Type-Id`, e.g. `Body-101`) with the same
    frame sequences and 8 fps. Drop AssetBundles entirely. **Emit the exact format the
    reference project uses** (see spec below) so it drops into the §5 layered animation
    system unchanged.

  **Target `SpriteFrames` format** (from `~/code/3dMMO-Server/client/Assets/Sprites/Bodies/1/animations.tres`):
  ```gdresource
  [gd_resource type="SpriteFrames" format=3 uid="..."]
  [ext_resource type="Texture2D" uid="..." path="res://.../<sheet>.png" id="1"]

  [sub_resource type="AtlasTexture" id="AtlasTexture_xxxx"]
  atlas = ExtResource("1")
  region = Rect2(x, y, w, h)        # one per frame; x/y/w/h from the .adf Frame rects
  # ... one AtlasTexture sub_resource per frame ...

  [resource]
  animations = [{
    "frames": [{"duration": 1.0, "texture": SubResource("AtlasTexture_xxxx")}, ...],
    "loop": true,
    "name": &"walk-down",         # convention: "<state>-<direction>", lowercase
    "speed": 8.0                  # fps
  }, ... ]                        # 11 states × 4 directions (skip states with no frames)
  ```
  Plus a `bodies.json`-style sidecar per `Type-Id` carrying `footOffsetY` /
  height (sourced from `AnimationHeights.txt`) for per-slot offset alignment.
  - `AnimationToFirstFrame.txt` / `AnimationHeights.txt` outputs → keep as-is (plain text),
    they already feed `AnimationManager`.
  - `CopyMaps` → copy `.map` → Godot-loadable bytes (or parse straight to a map resource).
- 🗑️ **Drop**: `BuildScript.cs` (Unity batch build), `NonDrawingGraphicEditor.cs` (uGUI
  editor inspector). Replace with Godot export presets / a plain Control.
- **Run it as**: a standalone C# console tool or a Godot `@tool`/`EditorPlugin` that writes
  into `res://` once. It's a one-time (re-runnable) build step, not shipped runtime code.

### Reference: depth & complexity by file
| Heaviest files | LOC | Bucket |
|---|---|---|
| `Character/Character.cs` | 424 | §5 |
| `MapManager.cs` | 417 | §4 |
| `Editor/ToolsMenu.cs` | 456 | §8 |
| `Editor/GifLoader.cs` | 397 | §8 |
| `UI/HotbarWindow.cs` | 319 | §6 |
| `UI/ChatWindow.cs` | 307 | §6 |
| `Editor/IllutiaData.cs` | 268 | §8 |
| `Network/NetworkClient.cs` | 264 | §1 |
| `SpellTargetManager.cs` | 238 | §2 |
| `UI/ItemTooltip.cs` | 232 | §6 |

---

## Dependency replacement table

| Unity dependency | Used for | Godot replacement |
|---|---|---|
| `com.unity.cinemachine` | Camera follow | `Camera2D` (+ small follow script) |
| `com.unity.inputsystem` (`.inputactions`, `PlayerInput`) | Input | Godot `InputMap` + `Input`/`_UnhandledInput` |
| `com.unity.textmeshpro` | Text | `Label`/`RichTextLabel` + `FontFile` |
| `com.unity.ugui` (Canvas) | UI | `Control` nodes + `Theme` |
| `com.unity.render-pipelines.universal` (URP, 2D lights) | Rendering/lighting | Godot 2D renderer — **C1 intentionally skipped**: Unity used a single static global white `Light2D` (no gameplay lighting packets from server); no lighting node needed. At most a fixed-ambient `CanvasModulate` could be added later if E1 live diff requires it. |
| `*.assetbundle` / StreamingAssets | Packaged graphics/anims | **Removed** — Godot-native atlases/`SpriteFrames` (§8) |
| `SpriteAtlas` | Sprite lookup by id | `AtlasTexture` regions over spritesheet PNGs |
| `Animator` / `AnimatorOverrideController` | Character animation | `AnimatedSprite2D` + `SpriteFrames` + C# state logic |
| Tilemap modules | Map layers | 5 `MapLayer` `Node2D`s drawing `AtlasTexture` regions off sheet PNGs (no `TileMapLayer`/`TileSet` — art is arbitrary-rect + bottom-center-anchored) |
| `com.unity.nuget.newtonsoft-json` | JSON (settings, item data) | `System.Text.Json` — **note**: `Dictionary<string,object>` values deserialize to `JsonElement` (not boxed primitives); reads must convert via `JsonElement` |

---

## Lifecycle / API cheat-sheet (apply throughout)

| Unity | Godot (C#) |
|---|---|
| `MonoBehaviour` | `Node` / `Node2D` / `Control` |
| `Awake` / `Start` | `_EnterTree` / `_Ready` |
| `Update` / `FixedUpdate` | `_Process(delta)` / `_PhysicsProcess(delta)` |
| `OnDestroy` / `OnApplicationQuit` | `_ExitTree` / `NOTIFICATION_WM_CLOSE_REQUEST` |
| `[SerializeField]` | `[Export]` |
| `Instantiate(prefab)` | `packedScene.Instantiate()` |
| `GetComponent<T>()` | `GetNode<T>()` / typed child / script field |
| `AddComponent<T>()` | attach scene/script or flag on node (no component model) |
| `DontDestroyOnLoad` + static `Instance` | **Autoload** singleton |
| `IEnumerator` coroutine + `yield` | `async`/`await` + `ToSignal` / `Tween` |
| C# `event Action` | keep, or `[Signal]` |
| `Resources.Load` | `GD.Load` / `ResourceLoader` / `preload` |
| `Vector3` (Y-up) | `Vector2` (Y-down) — flip Y *(except map tiles: no flip, see §4 / `MapCoords`)* |

---

## Recommended porting order (sets up Phases 2+)

Each step is independently testable; the order front-loads the foundations the rest depends on.

1. **Project setup** — autoload list, `InputMap` (port the `.inputactions` bindings),
   folder layout, NuGet for Newtonsoft. *(small)*
2. **Asset pipeline (§8)** — port `.adf`/`compiled.enc`/GIF parsers, emit Godot atlases +
   `SpriteFrames` + map data from `~/code/Illutia`. **Do this early — everything visual
   depends on it.** *(large, isolated, no gameplay needed to validate — eyeball the output)*
3. **Network + packets (§1)** — `NetworkClient`, `PacketManager`, all packets. Validate by
   connecting to the server and logging parsed packets. *(medium, near-verbatim)*
4. **GameManager + scene flow (§2, §3)** — autoloads, Login → Loading → Map skeleton with
   persistent-UI CanvasLayer. *(medium)* — ✅ **Landed (2026-06-06).** Interactive Login scene
   drives the full `LOGIN → LCNT → SCM → DLM` handshake into the Map placeholder; persistent
   `GameManager.UiLayer` CanvasLayer; `Pause` queue-and-replay (`GameManager.SetPaused` +
   `PausablePacketQueue`). Live-validated against the server: login succeeds, 161 gameplay
   packets buffer during the transition and drain in order on unpause; login-fail and
   connection-error paths surface the message and re-enable retry. The boot AssetBundle
   `LoadingScene` is dropped. (Map rendering itself is Step 5.)
5. **Map rendering (§4)** — ✅ **Landed (2026-06-06).** `MapFile` binary parser (golden-tested vs
   real maps); 5-layer rendering via `MapLayer` `_Draw` + `AtlasTexture` regions off sheet PNGs
   (`SpriteCache` + frame-rect manifest, **no `TileMapLayer`/`TileSet`**); dropped map items with
   tint (`Modulate`); runtime `TileUpdatePacket` repaint; roof toggle; `Camera2D` centred on the
   spawn tile read from `MakeCharacter`+`SetYourCharacter` (the server sends no `SUP` at entry);
   **no Y-flip** (single `MapCoords` helper). Converter extended to emit `manifest.json`.
   Live-validated against `scyther.local:2006`: real map (Map2, 500×215) parses, sheet `AtlasTexture`s
   resolve, and the camera centres on the real spawn tile. `GameManager.LoadMap` resolves the
   server's `MapFileName` (e.g. `Map2.map`) to `Assets/Maps/Map2.bytes` by basename. Character
   rendering and movement remain **Step 6**.
6. **Characters + animation (§5)** — ✅ **Landed (2026-06-06).** 10-slot node-per-`AnimatedSprite2D`
   paper-doll (`Character : Node2D`; mount = body, shield + weapon = hands) loading per-graphic
   `SpriteFrames` `.tres`; per-direction shield/weapon z-order via sibling order; tile-bottom anchor
   so feet sit on the tile; `MapManager` `LoginId→Character` registry routing
   MKC/MOC/CHH/CHP/ERC/ATT/VPU; predictive local-player input (move/face/attack) gated by SUC;
   attack-lock timed off the body clip; **`BodyState`-driven equip/no-equip + weapon-type attack
   clips** (1hand/staff/2hand/bow); name label + HP bar; **tint via a faithful `_Tint`-equivalent
   shader** (alpha = blend factor, dyed slots only). Pure logic (anchor, layout, underwear,
   motion-state, animation names/heights) is Godot-free and unit-tested (106 tests). Live-validated
   against `scyther.local:2006`: players/monsters/NPCs spawn and assemble (up to 9 stacked slots)
   with zero runtime errors; equipped NPCs resolve `idle-equip`/`attack-1hand`.
7. **UI windows (§6)** — ✅ **Landed (2026-06-06).** All ~40 uGUI windows ported to Godot
   `Control` nodes. Shared primitives first: `BaseWindow` (title-bar drag + hover transparency +
   persisted position, replacing Unity `TitleBar`/`WindowTransparency`), `ItemSlot`/`SpellSlot`/
   `HotbarSlot` using Godot's **built-in `_GetDragData`/`_CanDropData`/`_DropData`** (replacing the
   custom `DragIcon`/`DropTarget`/`DropTargetManager` plumbing; drag payload = `{kind, slot}` dict),
   `TooltipManager` + 4 tooltips, `Icon` tint helper (reuses the `_Tint` blend shader), `WorldDropTarget`
   + `DestroyButton`. Then every window: Vitals (HP **+ MP** bars + level), Inventory (gold + move/
   split/vendor/cross-window drops), Character (equipped 31+i + stats/resists/exp), Spellbook (8×30
   paged, cast/move/auto-place), Hotbar (3×10 + XP bar + mount tracking + debounced save + hotkey
   repeat) + Toolbar, Chat (BBCode log + command parser + history; movement suppressed while typing),
   Vendor/Bank/CombineBag (server-spawned), Party, BuffEffects, Quest/Info (multi-window manager),
   Options, Debug, plus map/character click→`LeftClick`/`RightClick` in `MapManager._UnhandledInput`.
   Assembled under a persistent `GameHud` (mounted on `GameManager.UiLayer` on map entry, wires
   input toggles + cross-refs). **Pure logic is Godot-free and unit-tested (144 tests):** item-tooltip
   text, chat command parsing, stack-split, spell paging, hotbar swap, spell-cooldown, settings
   null-guarding. All 23 UI scenes load headless; the **full HUD (18 nodes / all windows) assembles
   at runtime with zero errors** (headless smoke test). **Not yet done in this environment:** live
   in-game E2E (interactive drag/drop, vendor/bank/combine flows, chat round-trip, screenshots) —
   blocked by no display/Xvfb, no `run` skill, and no test credentials; see Step 7 deferred.

   **UI Window Overhaul — Part 1 + Part 2 (2026-06-07).** ✅ **Landed.** The windows were
   re-skinned and re-laid-out to match the Unity prefabs pixel-for-pixel. *Part 1 (merged):*
   imported the Unity window sprite art + LiberationSans font, a shared `GameTheme.tres`
   (white text, button/panel skins), a tested `UnityRect` Unity→Godot RectTransform converter,
   `DefaultWindowLayout`, refactored `BaseWindow` to host a sprite background + transparent drag
   region, 32px slots, and window-visibility persistence (Step 8 **D1** ✅). *Part 2 (this branch,
   `feat/ui-windows-part2`):* each window's root sized to its PNG with the PNG as background and
   every widget repositioned from the cited prefab coordinates via `UnityRect`:
   **CharacterWindow** fully rebuilt (character.png 400×222, anatomical 14-slot equipment doll
   positioned by the new tested `CharacterEquipmentLayout` using Unity's serialized `slots[]`
   order, two-column labeled stats/resists); **VitalsWindow** skinned (vitals-outline.png) + a
   portrait node skeleton (`Portrait`→`Mask`→Body/Eyes/Hair/Chest/Helmet + circle frame) for the
   Step 8 **A1** renderer to fill; **Inventory** (5×6), **Spellbook** (24px slots, sprite page
   buttons), **Hotbar** (9-slice `hotbar.png` + sprite up/down + xp-bar), **Vendor/Bank/CombineBag**
   (CombineBag corrected to 2 columns), **Chat** (chat.png, size-12 log), **Party** (per-member
   party-frame + HP/MP bar sprites), **Buffs** (20px overlay row), **Options** (StyleBox panel),
   **Info/Quest** (`NinePatchRect` with verified `spriteBorder` patch margins), **Debug** (themed
   corner overlay). All 24 UI scenes load headless; build clean; 156 unit tests pass.
   **Still deferred (owned by Step 8):** the **Task 10 live visual pixel-tuning pass + screenshots**
   (needs a desktop display + the live server + credentials — the same environment gap as Step 7/8
   E1, not runnable headless here); plus the Step 8 items below — **A1** portrait *rendering* into
   the new Vitals node tree, **A2** targeting, **B** world overlays, etc.

   **UI Window Overhaul — Part 3: interaction fixes (2026-06-07).** ✅ **Landed** (branch
   `fix/ui-window-interaction`). Fixed the six interaction regressions found after Part 1/Part 2:
   (1) **window dragging restored** — `BaseWindow._Ready` now sets the full-rect `Content`
   (`MouseFilter=Pass`) to `Ignore` so it stops occluding the `TitleBar` and swallowing its drag
   clicks (one fix covers all nine `BaseWindow` subclasses); (2) **inventory icons no longer stack
   in slot 1** + (3) **item/spell tooltips** + (4) **spellbook cast** + (5) **hotbar slots visible**
   — all four collapse to one root cause: the `ItemSlot`/`SpellSlot`/`HotbarSlot` scenes had no
   `custom_minimum_size`, so inside a `GridContainer` every slot sized to `(0,0)`; added
   `custom_minimum_size` (32/24/32) so slots lay out, gaining real hover/click rects (covers
   Inventory/Vendor/Bank/CombineBag/Spellbook/Hotbar; CharacterWindow's plain-`Control` equipment
   doll is unchanged since its slots were already 32×32); (6) **toolbar** rebuilt from a top-left
   text strip into four 32px icon `Button`s anchored bottom-right (8px margin), importing the
   missing `combinebagbutton.png` asset (skipped by Part 1's import loop) and preserving the
   `OptionsButton` node name + `ItemType` exports so `GameHud` wiring keeps working. Also **docked
   the (still non-draggable) Hotbar bottom-center** via scene anchors plus a new
   `BaseWindow.UseFixedDockLayout` opt-out (so `_Ready` doesn't clobber the anchors with a saved
   `Position`). Build clean; 156 unit tests still pass; all scenes load headless.
   **Known limitations (intentional, not bugs):** the Hotbar stays non-draggable by design (no
   title-bar room on the 36px art — it docks instead); `ChatWindow` drag is out of scope (it's a
   plain `Control`, not a `BaseWindow`, and was not reported). **Deferred:** the live in-game
   visual acceptance checklist (drag/tooltips/cast/hotbar/toolbar against `scyther.local:2006`)
   needs a desktop display + the live server + credentials — the same environment gap as the
   Part 1/2 Task 10 pass — so it is owned by the user's live run, not landed headless here.

   **UI Window Overhaul — Part 4: interaction & visual fixes (2026-06-07).** ✅ **Landed** (branch
   `fix/ui-windows-part4`). Fixed the round-2 bugs found after Part 3: (1) **tooltips render above
   all windows** — moved them onto a dedicated high `CanvasLayer` (Layer 100) instead of being
   GameHud siblings that windows occluded (mirrors Unity's tooltip canvas `sortingOrder 10000`);
   (2) **tooltips have a solid black background** — added a `StyleBoxFlat` Panel as the first child
   of each tooltip, with the item tooltip sized to its content each frame so the full-rect panel
   covers icon + text; (3) **window drag + exit button work on every window incl. Character** —
   `BaseWindow._Ready` now raises the `TitleBar` to the topmost sibling (`MoveChild`) so its drag
   region and `CloseButton` always win even under a full-rect `Content` child (CharacterWindow's
   `SlotGrid`); (4) **vitals HP/MP numbers left-aligned**; (5) **inventory stack count moved to the
   slot's top-right, right-aligned** (shared `ItemSlot.tscn`, so vendor/bank/combine/character match);
   (6) **hotbar slots gained frames, 1–0 number labels, and full-size icons** — added a slot
   background + a `SlotNumber` label driven by a new unit-tested pure helper
   `HotbarSlot.SlotLabel(index)` (9→"0", else index+1), and fixed the `stretch_mode = KEEP` icon bug
   on `HotbarSlot`/`ItemSlot`/`SpellSlot` (now `SCALE`, fills the cell); (7) **party slots hidden
   until a real `GroupUpdate`** — `Content` defaults `visible = false` in the scene + a defensive
   `_Ready` guard, revealed by the existing `OnGroupUpdate` toggle. Build clean (0 errors); unit
   tests now **160** (added 4 `HotbarSlot.SlotLabel` cases via TDD); the test project's `Compile`
   list was switched to a `Scripts/**/*.cs` glob so `HotbarSlot` (which transitively needs
   `GameManager`) compiles into the test assembly. **Carry-forward:** on-screen spell *targeting*
   (`SpellTargetManager` is still a stub) is deliberately out of scope here — it's a gameplay system
   tracked as Step 8 Task 9 (A2); self-cast spells already work. **Deferred:** the live in-game
   visual acceptance checklist (tooltips/drag/close/hotbar/party against `scyther.local:2006`) needs
   a desktop display + live server, so it is owned by the user's live run, not landed headless here.
8. **Polish** — character paper-doll portrait (`VitalsCharacterDisplay`), on-screen spell
   **targeting** (`SpellTargetManager`), sprite-accurate character clicks + map-item hover tooltip,
   world-space **overlays** (chat bubble, battle text, emote, spell animation), 2D lighting,
   `TextureProgressBar` art, window-visibility persistence, and the **live end-to-end validation
   pass** Step 7 couldn't run headless. Every Step-6/7 deferral below is scoped as an explicit task
   in **`docs/plans/2026-06-06-step8-polish-overlays.md`** so nothing falls through the cracks.

   **Step 8 — Part 1 + Part 2 code merged to `master` (2026-06-07).** ✅ **Code landed** (pending
   live E1). *Part 1* (`feat/step8-part1`, merged): idle PING/PONG keepalive, weapon-speed attack
   gating, occupancy-blocked prediction, graceful-close Disconnected event, `Character` appearance/
   `Height`/`CharacterType`/`Cast()` accessors + `CharacterUpdated`, `Overlays.OverlayLifetime`
   (tested) + `WorldOverlay` base, paper-doll portrait (**A1**), on-screen spell targeting (**A2**).
   *Part 2* (`feat/step8-part2`, merged via plan `docs/plans/2026-06-07-step8-part2-overlays-and-polish.md`):
   floating **battle text (B1)**, **chat bubble (B2)**, **emote (B3)**, **spell cast/impact +
   remote caster pose (B4)** — all `WorldOverlay`-based with self-freeing lifetimes and pure,
   unit-tested layout logic; **body-accurate character clicks + ground-item hover tooltip (A3)**;
   spell-cooldown bar art (**C2**); per-window **visibility restore on load (D1)**; **D4** (Hair/16
   source-data gap) and **D5** (staff/2h/bow attack clips) data-verified. **C1** (2D lighting)
   intentionally skipped (no server light packet); **C3** (dyed-gear color space) deferred to E1.
   Build clean (0 errors); **206 unit tests pass**; full headless smoke zero-error. **NOT yet done:**
   **Task 10 / E1** — the live interactive validation pass + screenshots against
   `scyther.local:2006` (needs a desktop display + real credentials); all eye-verified pixel offsets
   (overlay positions, dyed-gear color space, ambient) and slot-count vs-live confirmation remain
   owned by that live run. Step 8 is therefore **code-complete and merged, but not declared fully
   “Landed” until E1 passes.**

## 2026-07-11 Unity-parity bugfix pass — Part 1

> Plan: `docs/plans/2026-07-11-unity-parity-part1-critical-and-networking.md`

- **GameColors shared palette** — ported Unity `Colors.cs` + health-bar colors into `GameColors` (not `Colors` to avoid Godot collision)
- **Pause-aware packet drain** — `PausablePacketQueue.Drain` stops mid-batch when a handler re-pauses (back-to-back map changes)
- **Persistent in-game `SendCurrentMapPacket` listener** — moved from login-scene-only to `GameManager` so warps/doors/death-recalls work in-world
- **Chat clear/unfocus on map changes** — `ChatWindow.ClearAndUnfocus()` called at top of `ChangeMap` (Unity `LoadingMapScene` parity)
- **Backward target-cycle fix** — `TargetCycler.Next` with `idx == -1` now returns last candidate, not n−2
- **1.0 s pre-WPS attack default** — `AttackGate.DefaultWindowSeconds` raised from 0.5 s to 1.0 s (Unity `MapManager` default)
- **Mounted attack suppression** — `Character` attack input gated on `!IsMounted` (Unity `PlayerController` parity)

## 2026-07-11 Unity-parity bugfix pass — Part 2

> Plan: `docs/plans/2026-07-11-unity-parity-part2-character-movement-animation.md`

- **Cast animation** — `CharacterMotion.State` / `AnimationNames` extended with "cast" motion; `Cast()` plays `cast-<dir>` clips on body/hair/chest/eyes/legs while weapons idle (Unity `DoCast` trigger parity)
- **Tap-to-turn + held-diagonal alternation** — `MovementInput.Resolve` with 0.1 s hold threshold; tap turns in place and sends `Face`; held diagonal alternates axes for staircase walking
- **Chained-move snap / teleport anim reset / facing priority** — `MoveTo` snaps `_moving` characters to `_targetPosition` before each step; `TeleportTo` calls `PlayState()` to drop walk→idle; facing uses Down→Right→Up→Left priority
- **Title/Surname full names** — `NameFormatting.FullName` trims null/empty title + surname; overhead label and Character window both display the full name
- **Health bar auto-hide + Unity colors** — `HealthBarAutoHide` hides bars 2 s after full HP/MP, re-shows on any vitals change; bar colors use `GameColors.HpGreen/Orange/Red`

## Open questions / risks to resolve before Phase 2

- **Animation redesign (§5)** — approach **de-risked** by the `~/code/3dMMO-Server/client`
  reference (per-slot `AnimatedSprite2D` + `SpriteFrames` + C# state logic). ✅ **Decided
  (2026-06-06): skip the layering prototype** — assume stacked `AnimatedSprite2D`s stay
  frame-locked and z-order correctly, and build all 8 slots directly. Revisit only if Step 6
  surfaces an actual frame-lock/z-order problem.
- **Threaded networking** — ✅ **Decided & implemented**: background receive thread +
  `CallDeferred` marshaling (see §1). Packet handlers run on the main thread; no further work.
- **Exact tint/blend** — ✅ **Resolved (2026-06-06): `ShaderMaterial` after all.** `Modulate`'s
  alpha is opacity, but the Unity `_Tint` shader treats tint alpha as a *blend factor* (lerp
  texture→tint, leaving opacity = the texture's own alpha). Ported that as a per-slot canvas
  shader, applied **only to dyed slots** — untinted slots keep the default canvas path to avoid a
  global color-management shift. "No tint" is therefore alpha 0, never white.
- **Coordinate scale** — ✅ Resolved: "1 tile = 32 px", single `MapCoords` helper (see §4).

## Known follow-ups / deferred hardening (surfaced during Phase 1 network port)

These are real issues identified during the network-layer port + review. None block Phase 1
(they live on paths that are out of scope for it), but each must be addressed when the relevant
consuming layer lands:

- **`NetworkClient` graceful remote-close surfaces no event.** When the server closes the
  connection, `ReceiveLoop`'s blocking `Receive` returns 0 and the loop just exits — no
  `SocketError`/disconnect event is marshaled to the main thread. Fine for connect-and-exit
  validation, but the **reconnect / session layer** will have no hook to react to a dropped
  connection. Add a main-thread "Disconnected" event on the `received == 0` path then.
  → **Step 8 task D2** (`docs/plans/2026-06-06-step8-polish-overlays.md`).
- **`Pause` drops packets instead of queue-and-replay.** ✅ Resolved (Step 4). `GameManager` now
  uses `PausablePacketQueue` to buffer packets FIFO while paused and drain on unpause via
  `GameManager.SetPaused(bool)`. See `Scripts/Network/PausablePacketQueue.cs` +
  `tests/Goose2Client.Tests/PauseQueueTests.cs`.
- **`CharacterSettings.Load()` is not defensive against corrupt/partial JSON.** ✅ **Resolved
  (Step 7).** `ApplyDefaults()` null-guards `Hotkeys`/`WindowSettings`/`Options`, `Load()` guards a
  null deserialization result, and `FromJson(string)` degrades to defaults on **any** parse failure.
  Unit-tested (corrupt/empty/explicit-null/partial/null-input) in `CharacterSettingsLoadTests.cs`.

### Step 6 deferred

Surfaced during the Step 6 characters port; none block Step 6 itself:

- **Occupancy check in `IsValidMove`.** Local-player prediction only blocks on map-blocked tiles;
  it does not reject tiles occupied by another character yet. Add a `_characters` occupancy test
  when the collision/combat layer needs it. → **Step 8 task D3**.
- **Missing converter assets (e.g. `Hair/16`).** ✅ **Resolved as source-data / out of scope (Step 8
  D4).** Confirmed: `Assets/Sprites/Hair/` holds ids 1–15 and 17–28 — id 16 is genuinely absent. The
  converter emits exactly the ids present in `compiled.enc`, so a missing id is a **source-data hole,
  not a converter bug**. The client degrades gracefully: `Character.ApplySlot` checks
  `ResourceLoader.Exists` and calls `RemoveSlot` on a missing `.tres`, so the affected slot simply
  renders nothing (bald) with no error. No code change. Backlog: re-run the converter if the source
  `Hair/16` asset is ever obtained. → **Step 8 task D4** (asset pipeline, not the UI branch).
- **MP bar.** ✅ **Resolved (Step 7).** `VitalsWindow` renders both HP and MP bars from
  `StatusInfoPacket`. (`Character.SetVitals` also now stores `HPPercent`/`MPPercent` for the party UI.)
- **Dyed-gear color space (C3).** **Conditional, deferred to live E1 (Task 10).** The `_Tint` shader
  mixes in Godot's sample space; only revisit the `source_color` hint / sRGB-vs-linear mix if E1
  shows dyed gear looks off vs Unity. No code change now (may be a no-op).
- **Staff / 2h / bow attack clips (BodyState 5/6/7).** ✅ **Data-verified (Step 8 D5).** The runtime
  `AnimationNames.AttackVariant` map (4=1hand, 5=staff, 6=2hand, 7=bow) matches the converter's
  `AnimationNaming` clip names. Asset inspection confirmed the converter emitted complete 4-direction
  clip sets: weapon (`Hands/`) graphics carry `attack-1hand` (148 files), `attack-staff` (44),
  `attack-2hand` (30), `attack-bow` (1, `Hands/168`), and `Bodies/` carry matching attack poses
  (1hand 6, staff/2hand/bow 4 each) — each representative file (e.g. `Hands/180` staff, `Hands/210`
  2hand, `Hands/168` bow) has up/down/left/right. Missing cases fall back gracefully via the
  candidate chain (`attack-<variant>` → `attack-1hand` → `attack` → `attack-no-equip` → `idle`).
  Final in-motion confirmation is part of live E1 (Task 10). → **Step 8 task D5/E1**.
- **Step-8 overlays.** Chat bubble, battle text, spell/emote overlays (out of Step 6 scope).
  → **Step 8 tasks B1–B4**.

### Step 7 deferred

Surfaced during the Step 7 UI-windows port; none block Step 7 itself:

- **Live in-game E2E + screenshots not performed.** Build is clean, 144 unit tests pass, all 23 UI
  scenes load headless, and the full `GameHud` (18 nodes / every window) assembles at runtime with
  zero errors (headless smoke test). But interactive validation against `scyther.local:2006`
  (drag inventory→inventory/hotbar/world/destroy, hotbar use + paging, spellbook cast + cooldown,
  vendor buy/sell, bank deposit/withdraw, combine, chat send + `/`-commands + tell/reply + history,
  party vitals, buff add/remove, options persistence, window-drag persistence across relog) and a
  screenshot were **not** done — the environment has no display/Xvfb, no `run` skill, and no test
  credentials. Run this manually on a desktop with `GOOSE_HOST=scyther.local GOOSE_PORT=2006`.
All Step-7 deferrals are scoped as explicit tasks in
**`docs/plans/2026-06-06-step8-polish-overlays.md`** (task ids in brackets below).

- **`SpellTargetManager` is a stub.** Targeted spell casting + on-screen targeting is Step 8;
  `SpellbookWindow.UseSpell` wires the call but it no-ops (`// TODO(step8)`). → **task A2**.
- **`VitalsCharacterDisplay` is a stub.** Resolves the local player but renders nothing. The
  layered-portrait **node tree now exists** in `VitalsWindow.tscn` (`Portrait`→`Mask`→Body/Eyes/
  Hair/Chest/Helmet TextureRects + circle frame, script attached) as of the UI overhaul Part 2 —
  A1 just needs to *render* appearance data into those (empty) layer textures. The Unity original
  is a static layered portrait (body/hair/eyes/chest/helmet), not an animation — needs `Character`
  to expose appearance data first. → **task A1** (incl. the Character-appearance-accessor prerequisite).
- **Sprite-accurate character clicks + map-item hover tooltip.** `MapManager._UnhandledInput`
  resolves clicks by tile (clicking a character's foot tile targets them via server coords), but
  pixel-accurate body hit-testing and the on-hover map-item tooltip are deferred to Step 8
  (`MapItem` doesn't carry `ItemStats`, and the Control-parent tooltip lifetime doesn't fit a
  world-space `Sprite2D`). → **task A3**.
- **BBCode chat injection hardened.** Player chat is escaped (`[`→`[lb]`) before rendering in the
  BBCode log, preventing markup/URL injection — noted here as a security decision, not a gap.
- **Slot counts are reasonable defaults, tune live.** Inventory 30, equipped 14, spellbook 8×30,
  hotbar 3×10, vendor 40, bank 30, combine 10, party 8, buffs 20 — all bounds-guarded; confirm
  against the live server's actual ranges during the manual E2E and adjust the consts if needed.
- **Window default positions/visibility.** Positions persist via `CharacterSettings.WindowSettings`;
  per-window saved *visibility* now persists too (`WindowSettings.Visible`, saved on toggle/close,
  restored in `_Ready` as of Step 8 (D1 finish)). → **task D1 ✅ Resolved (UI windows overhaul Part 1).** The Step 8 D1 item
  is satisfied here.
- **TextureProgressBar visuals (C2).** ✅ **Resolved (Step 8).** HP/MP bars (`VitalsWindow.tscn`) and
  XP bar (`HotbarWindow.tscn`) already had `texture_progress` art. Cooldown-bar radial overlay now
  has `Assets/UI/cooldown-fill.tres` (solid-white GradientTexture2D) assigned to `CooldownOverlay`
  in both `SpellSlot.tscn` and `HotbarSlot.tscn`. Full visual confirmation is part of live E1 / Task 10.
- **Live in-game E2E + screenshots** (top of this list) → **task E1**.
