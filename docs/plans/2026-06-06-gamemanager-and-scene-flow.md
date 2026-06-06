# GameManager + Scene Flow — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Land Migration Plan **Step 4** (`MIGRATION_PLAN.md:296-298`, §2 + §3): turn the
connect-and-log network skeleton into a real **Login → LoadingMap → Map** scene flow driven by the
live server handshake. The deliverable is a running Godot app where the user types credentials into
an interactive **Login scene**, connects to the real server, logs in, and is carried through the
full `LOGIN → LCNT → SendCurrentMap → DoneLoadingMap` handshake into an (empty placeholder) **Map
scene** — at which point the server begins streaming live gameplay packets that the client
**queues during the transition and replays on unpause**. No tile rendering, no characters, no HUD
windows — those are Steps 5–7.

**Architecture:** `GameManager` (already an autoload `Node`) gains the scene-flow brain:
`ChangeMap`, a persistent-UI `CanvasLayer`, settings load/save, and the **Pause queue-and-replay**
fix. Unity's coroutine + additive-scene + `MoveGameObjectToScene` choreography
(`GameManager.LoadMapAsync`, `LoadingMapScene.LoadMapAsync`) collapses into Godot's
`GetTree().ChangeSceneToPacked(...)` + `async`/`await ToSignal` — because the persistent UI lives in
an **autoload `CanvasLayer` that survives scene swaps**, there is nothing to "move." The throwaway
`Bootstrap.cs` debug harness is replaced by a faithful port of `LoginScene/LoginButton.cs` as a real
Godot `Control` scene. Unity's `PlayerPrefs` credential autofill → a Godot `ConfigFile` at
`user://login.cfg`.

**Tech Stack:** Godot 4.6 (.NET / C#), `GetTree().ChangeSceneToPacked`, `ToSignal`, Godot `Control`
UI nodes, `ConfigFile`. Target repo: `~/code/Goose2ClientGodot`. Unity source (read-only reference):
`~/code/Goose2Client`.

**Decisions locked for this plan:**
- **Login UX:** build a **real interactive Login scene** now (username/password `LineEdit`s, Login
  button, status label, credential autofill) — a faithful port of `LoginButton.cs`. Not a stub.
- **Handshake depth:** **full world entry.** Complete `DoneLoadingMap` + unpause so the server
  streams live gameplay packets. Those packets are *unhandled-but-benign* until Steps 5/6 register
  their handlers — verifying they **queue during the transition and drain on unpause** is part of
  done.
- **The Unity boot `LoadingScene` is DROPPED.** Its entire job was preloading 6 AssetBundles
  (`spriteatlas`, `prefabs`, `ui-prefabs`, `maps`, `body-1`, `spell-1080`) before showing login.
  Step 2 replaced AssetBundles with Godot-native resources loaded on demand, so this scene and the
  AssetBundle half of `ResourceManager` have no reason to exist. The app boots straight into Login.
- **Support managers are NOT ported this step.** `ResourceManager` (AssetBundle-coupled — obsolete
  as-is), `PlayerInputManager` (needed only by `PlayerController`, Step 6), `SpellTargetManager`
  (spell casting, gameplay), and `SpellCooldownManager` (trivial but spell-only) are all gated
  behind rendering/characters. None are touched by the Login/LoadingMap/Map skeleton. Defer all four.
- **Scene swap, not additive + reparent.** Use `ChangeSceneToPacked`; persistent UI is an autoload
  `CanvasLayer`. Do **not** port Unity's `LoadSceneMode.Additive` / `MoveGameObjectToScene` /
  `UnloadSceneAsync` dance.

---

## APIs verified

Unity source being ported (paths relative to `~/code/Goose2Client/Assets/Scripts/`), read 2026-06-06:

- `GameManager.cs`
  - `:90-95` `ChangeMap(mapFile, mapName)` → sets `NetworkClient.Pause = true`, starts
    `LoadMapAsync` coroutine.
  - `:97-127` `LoadMapAsync` → finds the `"UI"` GameObject, deactivates it, additively loads
    `LoadingMapScene`, **moves the UI to it**, unloads the previous scene, then calls
    `LoadingMapScene.Load(mapFile, mapName)`. **This entire choreography is replaced** by
    `ChangeSceneToPacked` + an autoload `CanvasLayer` (nothing to move).
  - `:53-68` `Awake()` → singleton + `DontDestroyOnLoad` + `new` of `NetworkClient`/`PacketManager`/
    `AnimationManager`/`SpellCooldownManager`. (Godot: already an autoload; only `NetworkClient` +
    `PacketManager` exist today — see baseline below. `AnimationManager`/`SpellCooldownManager`
    deferred.)
  - `:70-78` `Start()`/`OnDestroy()` → `Listen<ClassUpdatePacket>(OnClassUpdate)` /
    `Remove<…>`. `:129-134` `OnClassUpdate` fills `Classes[packet.ClassId] = packet.Name`.
  - `:85-88` `LoadSettings(characterName)` → `CharacterSettings = new CharacterSettings(name)`.
  - `:136-141` `OnApplicationQuit()` → `CharacterSettings?.Save()` + `NetworkClient?.Quit()`.
  - Fields/properties (`:13-41`): `NetworkClient`, `PacketManager`, `CharacterSettings`,
    `Classes (Dictionary<int,string>)`, `CharacterUpdated (event Action<Character>)`, plus
    forward-looking refs (`MapManager`, `Character`, `CurrentMap`, `ChatWindow`,
    `SpellTargetManager`, `AnimationManager`, `SpellCooldownManager`) — only `NetworkClient`,
    `PacketManager`, `CharacterSettings`, `Classes` are in scope this step.
- `LoginScene/LoginButton.cs`
  - `:12-15` serialized `nameInput` / `passwordInput` / `messageOverlay`.
  - `:40-57` `Start()` → instantiate GameManager (N/A in Godot — autoload), `Listen` LoginSuccess /
    LoginFail / SendCurrentMap, subscribe `Connected` / `SocketError` / `ConnectionError`, autofill
    from `PlayerPrefs.GetString("CharacterName"/"CharacterPassword")`.
  - `:87-99` `OnLoginClicked()` → validate (name > 2, password > 3), `SetMessage("Connecting...")`,
    `NetworkClient.Connect("game.illutia.net", 2006)`. **Host/port hardcoded.**
  - `:106-114` `OnConnected()` → `NetworkClient.Login(name, password)`.
  - `:116-129` `OnLoginSuccess()` → `SetMessage("Connected!")`, save creds to `PlayerPrefs`,
    `GameManager.LoadSettings(name)`, `NetworkClient.LoginContinued()`.
  - `:131-139` `OnLoginFail()` → `SetMessage(packet.Message)`.
  - `:141-147` `OnError()` → show socket/connection error message.
  - `:149-153` `OnSendCurrentMap()` → `GameManager.ChangeMap(packet.MapFileName, packet.MapName)`.
  - `:17-38` `Update()` → Tab cycles name↔password focus; Enter submits when password focused.
- `LoadingMapScene/LoadingMapScene.cs`
  - `:16-24` `Load(mapFile, mapName)` → sets label `"Loading {mapName}..."`, starts `LoadMapAsync`.
  - `:26-73` `LoadMapAsync` → loads map bytes, additively loads `MapScene`, moves EventSystem + UI,
    `ImportMap` (tilemap build — **Step 5, out of scope here**), `MapManager.OnMapLoaded`,
    `NetworkClient.DoneLoadingMap()`, `NetworkClient.Pause = false`. **For this step we keep only the
    label + `DoneLoadingMap` + unpause; the map import is a Step 5 placeholder.**
- `ResolutionScript.cs:5-21` — class `FPSScript`; only displays `Screen.currentResolution` in a
  label. Cosmetic; **skip** (optionally a tiny resolution label later).
- `LoadingScene/LoadingScene.cs` — AssetBundle preloader. **Dropped** (see decisions).
- `Network/NetworkClient.cs` send helpers used here: `Login(user, pass)` → `Send("LOGIN{u},{p},GooseClient")`,
  `LoginContinued()` → `Send("LCNT")`, `DoneLoadingMap()` → `Send("DLM")`, `Pong()`.

Target repo baseline — current Godot state (`~/code/Goose2ClientGodot`, read 2026-06-06):

- `Scripts/GameManager.cs` — `extends Node`; static `Instance`; `NetworkClient` + `PacketManager`
  properties; `_EnterTree` `new`s both (`new NetworkClient(this)`); `HandlePacket(string)`
  **currently `if (NetworkClient.Pause) return;` — the drop-on-pause bug to fix**; `_Notification`
  on `NotificationWMCloseRequest` → `NetworkClient?.Disconnect()`; `_ExitTree` → `Disconnect()`.
- `Scripts/Network/NetworkClient.cs` — `Connect(addr,port)` (blocking `socket.Connect`, then spawns
  the background receive thread; fires `Connected` on the calling/main thread), `Disconnect`,
  `Send`, `IsConnected`, `Pause`, events `Connected` / `ConnectionError` / `SocketError`, all typed
  send helpers incl. `Login` / `LoginContinued` / `DoneLoadingMap` / `Pong`. Receive thread marshals
  each packet via `dispatcher.CallDeferred("HandlePacket", packet)`.
- `Scripts/CharacterSettings.cs` — present; `System.Text.Json` Load/Save (ctor takes character name).
- `Scripts/Bootstrap.cs` + `Scenes/Main.tscn` — **throwaway** debug harness (env-var auto-connect /
  auto-login / 15s auto-quit). `Main.tscn` is the current `run/main_scene`. **Both removed this step.**
- `project.godot` — `[autoload] GameManager="*res://Scripts/GameManager.cs"`; full `[input]` map
  already present; `run/main_scene="res://Scenes/Main.tscn"`.
- Packets already ported: `LoginSuccessPacket` (`LOK`), `LoginFailPacket` (`LNO`),
  `SendCurrentMapPacket` (`SCM`), `ClassUpdatePacket` (`CUP`), `PingPacket` (`PING`).

---

## Conventions for the implementer

- **Source of truth is the Unity project.** Port `LoginButton.cs` / `LoadingMapScene.cs` by reading
  them, not this plan's summaries. Keep field names and flow identical; only the engine API changes.
- **Namespaces / layout:** everything under `Goose2Client.*`. Mirror the Unity tree:
  `Scripts/LoginScene/LoginScene.cs`, `Scripts/LoadingMapScene/LoadingMapScene.cs`,
  `Scripts/MapScene/MapScene.cs`. Scenes under `Scenes/`: `Login.tscn`, `LoadingMap.tscn`, `Map.tscn`.
- **Scene-lifecycle gotcha (load-bearing):** the Login scene `Listen<T>`s on the autoload
  `PacketManager` and subscribes to autoload-owned `NetworkClient` events. When `ChangeSceneToPacked`
  frees the Login scene, those handlers would dangle and fire into a freed node. **Every `Listen<T>`
  / `+=` in a scene script MUST be matched by a `Remove<T>` / `-=` in that scene's `_ExitTree`.**
- **Threading rule (unchanged):** packets still arrive on the receive thread and marshal to the main
  thread via `GameManager.HandlePacket`. All scene-flow code (`ChangeMap`, queue drain) runs on the
  main thread, so the Pause queue needs **no locking**.
- **`ChangeMap` runs on `GameManager` (the autoload), not on a scene.** Autoloads persist across
  `ChangeSceneToPacked`, so it owns the transition and the persistent `CanvasLayer`.
- **Connect blocks the main thread** (`socket.Connect` is synchronous, same as Unity). Acceptable for
  this step — the UI freezes briefly during connect/timeout. If it's annoying in practice, note it;
  offloading Connect to a thread is a later hardening, not part of this plan.
- **Validation is connection-based**, not unit tests: done = a running app that logs in against the
  live server and enters the world. The only worthwhile unit test is the **Pause queue-and-replay**
  ordering (pure main-thread logic).

---

### Task 0: Scene scaffolding + boot straight into Login

**Files:**
- Create: `Scenes/Login.tscn`, `Scenes/LoadingMap.tscn`, `Scenes/Map.tscn`
- Delete: `Scenes/Main.tscn`, `Scripts/Bootstrap.cs`
- Edit: `project.godot` (`run/main_scene`)

**Step 1:** Create `Scenes/Login.tscn` — a `Control` root (full-rect anchors) with child nodes for
the login form (built out in Task 3): two `LineEdit`s (name + password, password `Secret = true`), a
`Button` ("Login"), and a `Label` for status/messages. Attach `Scripts/LoginScene/LoginScene.cs`
(created in Task 3) to the root.

**Step 2:** Create `Scenes/LoadingMap.tscn` — a `Control` with a centered `Label` (the
"Loading {mapName}…" text). Attach `Scripts/LoadingMapScene/LoadingMapScene.cs` (Task 4).

**Step 3:** Create `Scenes/Map.tscn` — a **placeholder** `Node2D` root (this is where Step 5 builds
the `TileMapLayer` world). Optionally a `Label` "Map: (rendering lands in Step 5)". Attach
`Scripts/MapScene/MapScene.cs` (Task 4, may be near-empty).

**Step 4:** Delete `Scripts/Bootstrap.cs` and `Scenes/Main.tscn`. Set
`run/main_scene="res://Scenes/Login.tscn"` in `project.godot`.

**Step 5:** `dotnet build` clean; open the editor once — confirm it boots into the Login scene with
the autoload `GameManager` present and no errors.

**Step 6:** Commit: `chore: scaffold Login/LoadingMap/Map scenes, drop Bootstrap harness`.

---

### Task 1: Fix `GameManager` Pause — queue-and-replay (deferred follow-up)

**Files:**
- Edit: `Scripts/GameManager.cs`
- Edit: `MIGRATION_PLAN.md` (mark the follow-up resolved)
- Test: `tests/PauseQueueTests.cs`

This resolves the deferred-hardening item at `MIGRATION_PLAN.md:333-338` — it comes due now because
`ChangeMap` is the first code to actually set `Pause = true`.

**Step 1:** Replace the drop-on-pause body. Add a main-thread queue and a `SetPaused` toggle:
```csharp
private readonly Queue<string> _pausedPackets = new();

public void HandlePacket(string packet)
{
    if (NetworkClient.Pause) { _pausedPackets.Enqueue(packet); return; }
    PacketManager.Handle(packet);
}

public void SetPaused(bool paused)
{
    NetworkClient.Pause = paused;
    if (!paused)
        while (_pausedPackets.Count > 0)
            PacketManager.Handle(_pausedPackets.Dequeue());
}
```
All on the main thread (`HandlePacket` is the `CallDeferred` target; `SetPaused` is called from
`ChangeMap`), so no locking. Order is preserved: queued packets drain FIFO before any newly-arriving
packet (which now sees `Pause == false` and handles inline).

**Step 2:** Have everything that pauses go through `SetPaused` (not raw `NetworkClient.Pause`), so the
drain always fires on unpause. `ChangeMap` (Task 2) uses `SetPaused(true/false)`.

**Step 3:** Add `tests/PauseQueueTests.cs`: pause, feed 3 packets through `HandlePacket`, assert none
dispatched; unpause via `SetPaused(false)`, assert all 3 dispatched **in order**, then assert a
4th packet dispatches inline. (Use a fake/recording `PacketManager` or a registered listener to
observe dispatch order.)

**Step 4:** Update `MIGRATION_PLAN.md:333-338`: mark the "`Pause` drops packets" follow-up
**resolved (Step 4)** with a one-line note pointing at `GameManager.SetPaused`.

**Step 5:** Commit: `fix: queue-and-replay packets while paused instead of dropping them`.

---

### Task 2: `GameManager` scene-flow API — `ChangeMap`, persistent UI, settings

**Files:**
- Edit: `Scripts/GameManager.cs`

**Step 1: Persistent UI `CanvasLayer`.** In `_Ready`, create and `AddChild` a `CanvasLayer`
(`Name = "UiLayer"`) and expose it as `public CanvasLayer UiLayer { get; private set; }`. It is
**empty this step** — it's the survives-scene-swap home the HUD windows attach to in Step 7. This is
the Godot replacement for Unity's `MoveGameObjectToScene(ui, ...)` (`GameManager:115`,
`LoadingMapScene:47`): because the autoload's `CanvasLayer` persists across `ChangeSceneToPacked`,
nothing is ever moved.

**Step 2: `ChangeMap`.** Port `GameManager.ChangeMap` + `LoadMapAsync` (`:90-127`) to Godot
`async`/`await`, collapsing the additive-load/reparent/unload dance:
```csharp
public async void ChangeMap(string mapFile, string mapName)
{
    SetPaused(true);                                   // Task 1 — buffer packets during transition

    var loading = GD.Load<PackedScene>("res://Scenes/LoadingMap.tscn").Instantiate<LoadingMapScene>();
    GetTree().ChangeSceneToNode(loading);              // or ChangeSceneToPacked + grab the script
    loading.SetMapName(mapName);                       // "Loading {mapName}..."
    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    // Step 5 hook: build the TileMapLayer world from `mapFile` here. Placeholder for now.

    GetTree().ChangeSceneToPacked(GD.Load<PackedScene>("res://Scenes/Map.tscn"));
    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    NetworkClient.DoneLoadingMap();                    // "DLM" — tells server we're in the world
    SetPaused(false);                                  // drain queued gameplay packets
}
```
> **Note the deferred semantics:** `ChangeSceneToPacked`/`ChangeSceneToNode` swap at idle, so the
> `await ToSignal(..., ProcessFrame)` is what guarantees the new scene is current before the next
> line. Keep the `mapFile` parameter even though tile building is a Step 5 placeholder — it's the
> seam Step 5 fills, and it keeps the `SendCurrentMapPacket` → `ChangeMap` signature stable.

**Step 3: Settings + class table.** Port the small `GameManager` surface the flow uses:
- `public CharacterSettings CharacterSettings { get; set; }` + `LoadSettings(string characterName)`
  (`:85-88`): `CharacterSettings = new CharacterSettings(characterName);`.
- `public Dictionary<int,string> Classes { get; } = new();` + a `Listen<ClassUpdatePacket>` in
  `_Ready` whose handler does `Classes[p.ClassId] = p.Name` (`:70-72,129-134`); `Remove` it in
  `_ExitTree`. (GameManager is an autoload, so its own listeners live for the app's lifetime —
  registering in `_Ready` and removing in `_ExitTree` is correct and symmetric.)

**Step 4: Save-on-quit.** Extend the existing `_Notification` `NotificationWMCloseRequest` handler
to mirror `OnApplicationQuit` (`:136-141`): `CharacterSettings?.Save();` **before**
`NetworkClient?.Disconnect();`.

**Step 5:** `dotnet build` clean; open the editor and confirm the autoload loads with the new
`UiLayer` child and no errors.

**Step 6:** Commit: `feat: add ChangeMap, persistent UI CanvasLayer, settings to GameManager`.

---

### Task 3: Login scene — interactive UI + connect/login flow

**Files:**
- Create: `Scripts/LoginScene/LoginScene.cs`
- Edit: `Scenes/Login.tscn` (wire node paths / exports)

Faithful port of `LoginScene/LoginButton.cs` as a Godot `Control` script.

**Step 1: Node wiring.** Reference the form nodes via `[Export] NodePath` or `GetNode` in `_Ready`:
name `LineEdit`, password `LineEdit` (`Secret = true`), Login `Button`, status `Label`. Connect the
button `Pressed` signal to `OnLoginClicked`. Wire Enter-to-submit via the password `LineEdit`'s
`text_submitted` signal (cleaner than Unity's per-frame Tab/Enter polling in `Update():17-38`; Tab
focus cycling is `ui_focus_next` for free).

**Step 2: Connect/login flow (port `:87-153`).**
- `_Ready`: autofill name/password from `ConfigFile` (Step 4), then
  - `gm.PacketManager.Listen<LoginSuccessPacket>(OnLoginSuccess)`,
    `Listen<LoginFailPacket>(OnLoginFail)`, `Listen<SendCurrentMapPacket>(OnSendCurrentMap)`;
  - `gm.NetworkClient.Connected += OnConnected;` + `ConnectionError += OnError;`
    `SocketError += OnError;`.
- `OnLoginClicked`: validate (`name.Length > 2 && password.Length > 3`), set status "Connecting…",
  disable the button, `gm.NetworkClient.Connect("game.illutia.net", 2006)` (host/port default — see
  Step 5).
- `OnConnected`: `gm.NetworkClient.Login(name, password)`.
- `OnLoginSuccess`: status "Connected!", **save creds** (Step 4), `gm.LoadSettings(name)`,
  `gm.NetworkClient.LoginContinued()`.
- `OnLoginFail`: status = `((LoginFailPacket)p).Message`, re-enable the button.
- `OnError(Exception e)`: status = error message, re-enable the button.
- `OnSendCurrentMap`: `gm.ChangeMap(p.MapFileName, p.MapName)`.

**Step 3: Main-thread note.** `Connected` fires on the calling (main) thread; `LoginSuccess` /
`LoginFail` / `SendCurrentMap` arrive via `CallDeferred` → already main-thread. So all handlers may
touch UI nodes directly — no extra marshaling.

**Step 4: Lifecycle cleanup (the gotcha).** In `_ExitTree`, `Remove<T>` all three listeners and `-=`
all three event handlers. `ChangeMap` frees this scene; without cleanup the freed node's handlers
would fire on the next packet/connect and crash. **Do not skip.**

**Step 5: Server config.** Default to `"game.illutia.net"` / `2006` (matches `LoginButton:98`).
Allow override via env (`GOOSE_HOST` / `GOOSE_PORT`) to preserve the headless-testing path the old
`Bootstrap.cs` had. (A visible host/port field can come later; not needed now.)

**Step 6:** `dotnet build` clean; run the app — the Login form renders, Tab/Enter work, the button
is interactive.

**Step 7:** Commit: `feat: port LoginScene with interactive connect/login UI`.

---

### Task 4: LoadingMap + Map scene scripts + credential store

**Files:**
- Create: `Scripts/LoadingMapScene/LoadingMapScene.cs`
- Create: `Scripts/MapScene/MapScene.cs`
- Create: `Scripts/LoginScene/LoginCredentialStore.cs` (or fold into LoginScene)

**Step 1: `LoadingMapScene.cs`.** Port only the transition-visible part of `LoadingMapScene` (`:16-24`):
a `SetMapName(string)` that sets the centered `Label` to `"Loading {mapName}..."`. The actual map
build (`LoadMapAsync` `:26-73`: `ImportMap`, tilemap layers, `MapManager.OnMapLoaded`) is **Step 5** —
leave a `// Step 5: build TileMapLayer world here` marker. `DoneLoadingMap` + unpause live in
`GameManager.ChangeMap` (Task 2), not here.

**Step 2: `MapScene.cs`.** Near-empty placeholder `Node2D` script (a `_Ready` `GD.Print("Entered
map")` is enough). Step 5 grows this into the world root.

**Step 3: Credential store (replaces `PlayerPrefs`).** Implement a tiny `ConfigFile` wrapper at
`user://login.cfg` with `Load() -> (name, password)` and `Save(name, password)`, mirroring
`PlayerPrefs.GetString/SetString("CharacterName"/"CharacterPassword")` (`LoginButton:53-54,123-124`).
Wire it into `LoginScene._Ready` (autofill) and `OnLoginSuccess` (save).
> Plaintext on disk, same as the Unity original — faithful port, not a security upgrade. Note it;
> hardening (or dropping password persistence) is out of scope.

**Step 4:** `dotnet build` clean.

**Step 5:** Commit: `feat: add LoadingMap/Map scene scripts and login credential store`.

---

### Task 5: Live validation — log in and enter the world

**Files:** none (validation), then `MIGRATION_PLAN.md` checkbox update.

**Step 1:** Run the app against the real server (`game.illutia.net:2006`). Type real credentials
(or autofilled). Confirm in the Godot output / on screen:
- `Connected` fires; `LOGIN` sent; on success the status shows "Connected!" and `LCNT` is sent.
- `SendCurrentMapPacket` arrives → `ChangeMap` → the **LoadingMap** "Loading {mapName}…" screen shows,
  then the **Map** placeholder scene appears.
- `DoneLoadingMap` (`DLM`) is sent; `Pause` flips to `false` and the **queued gameplay packets drain
  in order** (you'll see a burst of `MapObject` / `MakeCharacter` / vitals packets dispatched right
  after unpause — unhandled-but-benign at this step; confirm no exceptions, just any "no handler"
  noise).
- A bad password shows the server's `LoginFailPacket.Message` and re-enables the button (retry works).

**Step 2:** Confirm clean shutdown: close the window → `CharacterSettings.Save()` runs (if loaded),
`NetworkClient.Disconnect()` joins the receive thread, no hang / no "thread still running" warning.

**Step 3:** Sanity-check the Pause queue under real load: add a temporary log of
`_pausedPackets.Count` at drain time to confirm packets actually buffered during the transition (then
remove it).

**Step 4:** Tick Step 4 in `MIGRATION_PLAN.md`'s "Recommended porting order" and note GameManager
scene-flow is landed.

**Step 5:** Commit: `feat: live-validate Login→world handshake against server`.

---

## Definition of done

- App boots into an interactive **Login scene**; the Unity boot AssetBundle `LoadingScene` is gone.
- Typing credentials connects to the live server, logs in, and drives the full
  `LOGIN → LCNT → SendCurrentMap → DoneLoadingMap` handshake into the **Map** placeholder scene.
- Persistent UI `CanvasLayer` lives on the `GameManager` autoload and survives the scene swap
  (empty for now; Step 7 fills it).
- **Pause queue-and-replay** works: packets received during the map transition are buffered and
  replayed in order on unpause (test green; verified live). `MIGRATION_PLAN.md` follow-up marked
  resolved.
- Login failure surfaces the server message and allows retry; clean shutdown saves settings and
  joins the receive thread.
- `dotnet build` clean; editor opens with no errors.

## Explicitly out of scope (next plans)

- **Map rendering** — `MapFile` parse, `TileMapLayer`/`TileSet` build, camera, tile updates. The
  `ChangeMap` / `LoadingMapScene` map-build seam is a labeled placeholder. **Step 5.**
- **Characters + animation**, `PlayerController`, and therefore `PlayerInputManager`. **Step 6.**
- **HUD / UI windows** (chat, inventory, hotbar, …) that populate the persistent `CanvasLayer`,
  plus `SpellTargetManager` / `SpellCooldownManager`. **Steps 7–8.**
- `ResourceManager` (Godot-native asset lookup) — rebuilt against the Step 2 asset layout when
  rendering needs it. **Step 5+.**
- Connect-off-the-main-thread, password-at-rest hardening, the two other deferred follow-ups
  (remote-close "Disconnected" event; `CharacterSettings` null-guard) — due with their consuming
  layers (`MIGRATION_PLAN.md:328-343`).
