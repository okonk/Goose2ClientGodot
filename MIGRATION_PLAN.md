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
- It is currently polled from `GameManager.Update()`. In Godot, poll from the GameManager
  autoload's `_Process`, **or** move the blocking `Socket.Select`/`Receive` to a background
  `System.Threading` thread and marshal parsed packets back via `CallDeferred` (recommended —
  the current `Select(..., 500)` blocks the main thread for up to 500µs per frame).
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
| `com.unity.render-pipelines.universal` (URP, 2D lights) | Rendering/lighting | Godot 2D renderer + `PointLight2D`/`CanvasModulate` |
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
6. **Characters + animation (§5)** — the hardest redesign; do it after the atlas/SpriteFrames
   exist and one map renders. **Template: `~/code/3dMMO-Server/client/Assets/Scripts/Entity/Character.cs`.**
   *(large, 🔴 — but de-risked by the reference)*
7. **UI windows (§6)** — base window + slot + drag/drop primitives first, then fan out the
   ~40 windows (parallelizable). *(largest by volume)*
8. **Polish** — tooltips, emotes, spell/battle-text overlays, lighting, settings persistence.

## Open questions / risks to resolve before Phase 2

- **Animation redesign (§5)** — approach now **de-risked** by the `~/code/3dMMO-Server/client`
  reference (per-slot `AnimatedSprite2D` + `SpriteFrames` + C# state logic). Remaining unknown
  is the *layering*: prototype one character (body + one equipment layer, idle+walk×4 dirs)
  to confirm multiple stacked `AnimatedSprite2D`s stay frame-locked and z-order correctly,
  *before* committing to all 8 slots.
- **Threaded networking** — decide thread-vs-`_Process` polling now; it affects how packet
  handlers marshal back to the scene tree.
- **Exact tint/blend** — confirm whether `Modulate` reproduces the Unity `_Tint` shader, or
  if a `ShaderMaterial` is needed.
- **Coordinate scale** — lock "1 tile = 32 px" and a single tile↔world helper before §4/§5.

## Known follow-ups / deferred hardening (surfaced during Phase 1 network port)

These are real issues identified during the network-layer port + review. None block Phase 1
(they live on paths that are out of scope for it), but each must be addressed when the relevant
consuming layer lands:

- **`NetworkClient` graceful remote-close surfaces no event.** When the server closes the
  connection, `ReceiveLoop`'s blocking `Receive` returns 0 and the loop just exits — no
  `SocketError`/disconnect event is marshaled to the main thread. Fine for connect-and-exit
  validation, but the **reconnect / session layer** will have no hook to react to a dropped
  connection. Add a main-thread "Disconnected" event on the `received == 0` path then.
- **`Pause` drops packets instead of queue-and-replay.** ✅ Resolved (Step 4). `GameManager` now
  uses `PausablePacketQueue` to buffer packets FIFO while paused and drain on unpause via
  `GameManager.SetPaused(bool)`. See `Scripts/Network/PausablePacketQueue.cs` +
  `tests/Goose2Client.Tests/PauseQueueTests.cs`.
- **`CharacterSettings.Load()` is not defensive against corrupt/partial JSON.** A settings file
  that deserializes with `Hotkeys == null` makes the `(string)` ctor throw `NullReferenceException`
  at the `Hotkeys.Length` check. Faithful to the Unity source today; add a null-guard when the
  settings/UI layer actually reads/writes these files.
