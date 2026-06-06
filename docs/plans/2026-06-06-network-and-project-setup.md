# Network Layer + Project Setup — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Finish the Godot project scaffolding (Migration Plan Step 1) and port the entire
network/protocol layer (Step 3) into the Godot C# project, so the client can connect to the
live server and log every parsed packet. No rendering, no gameplay — the deliverable is a
running Godot app that establishes the TCP session, receives `\x1`-delimited packets on a
background thread, and dispatches them to typed handlers on the main thread.

**Architecture:** The protocol core (`PacketManager`, `PacketParser`, `PacketHandler`, the 46
`*Packet` POCOs) is pure `System.Net.Sockets` / pure C# with **zero Unity coupling** — copy it
near-verbatim (only `Debug.Log` → `GD.Print`). The one real redesign is `NetworkClient`: replace
Unity's per-frame `Socket.Select(..., 500)` poll (driven from `GameManager.Update()`) with a
**dedicated background receive thread** that does a blocking `Socket.Receive`, splits packets, and
marshals each complete packet back to the main thread via **`CallDeferred`** onto the `GameManager`
autoload. `GameManager` becomes a Godot **autoload** that owns the `NetworkClient` and runs
`PacketManager.Handle` on the main (scene-tree) thread. The single Newtonsoft dependency
(`CharacterSettings`) is reimplemented with **`System.Text.Json`**.

**Tech Stack:** Godot 4.6 (.NET / C#), `System.Net.Sockets`, `System.Threading`, `System.Text.Json`.
Target repo: `~/code/Goose2ClientGodot`. Unity source (read-only reference): `~/code/Goose2Client`.

**Decisions locked for this plan:**
- **Polling model:** background thread + `CallDeferred` (not `_Process` polling). The dedicated
  thread blocks on `Receive`, so the `Select(..., 500)` timeout poll is dropped entirely.
- **JSON:** `System.Text.Json` (System.Text.Json), **not** Newtonsoft. Update the Migration Plan's
  dependency table to match.

---

## APIs verified

Unity source being ported (paths relative to `~/code/Goose2Client/Assets/Scripts/`):

- `Network/NetworkClient.cs`
  - `:29-30` — `new Socket(SocketType.Stream, ProtocolType.Tcp)` + `socket.Connect(address, port)`.
  - `:58` — `Send` appends the `'\x1'` (SOH) delimiter; sends as ASCII.
  - `:69-113` — `Update()` poll: `:75` `Socket.Select(readSockets, null, null, 500)`, `:81-84`
    `byte[8192]` receive buffer + `socket.Receive`, `:87-105` accumulate into `packetBuffer` and
    split on `'\x1'`, `:98` `GameManager.Instance.PacketManager.Handle(packets[i])`.
  - `:109` — `Debug.Log($"Network Exception: {e}")` (only Unity coupling → `GD.Print`).
  - Public API: `Connect`, `Disconnect`, `Send(packet)`, `Update()`, `IsConnected`, `Pause`, plus
    ~35 typed send helpers (`Login`, `Move`, `Attack`, `CastSpell`, `ChatMessage`, …). Events:
    `ConnectionError(Exception)`, `Connected()`, `SocketError(Exception)`.
- `Network/PacketManager.cs:13-62` — `Listen<T>(Action<object>)`, `Remove<T>(Action<object>)`,
  `Clear()`, `Handle(string)`. Prefix match tries 0–8 char prefixes against a `handlers` dict;
  `:51` `Debug.Log(...)` on parse error. No Unity coupling.
- `Network/PacketParser.cs:14-105` — manual comma-delimited reader: `GetNextToken`, `GetInt32`,
  `GetInt64`, `GetBool`, `GetString`, `GetSubstring`, `Peek`, `GetRemaining`, `LengthRemaining`.
  No Unity coupling.
- `Network/PacketHandler.cs:8-19` — abstract base: `List<Action<object>> Observers`,
  `CallObservers(object)`. Subclasses override `string Prefix { get; }` and `object Parse(PacketParser)`.
- `Network/Packets/*.cs` — **46 packet classes** (full list in the inventory below), all inherit
  `PacketHandler`, override `Prefix` + `Parse`. **None use Newtonsoft.** Verify each for
  `UnityEngine` types during the port (color/equipment fields parse as ints/strings, but confirm).
- `GameManager.cs`
  - `:15,43-46` — static `instance` + `Instance` getter; `:53-68` `Awake()` dedupe + `DontDestroyOnLoad()`.
  - `:19,64` — owns `NetworkClient`; `:82` `Update()` calls `NetworkClient.Update()`.
  - `:72-78` — `Listen<ClassUpdatePacket>` / `Remove<…>` lifecycle.
  - `:90-127` — sets `NetworkClient.Pause = true` during map transitions.
- `PlayerInputManager.cs:33-50` — binds `playerInput.actions.FindAction("Hotkey{i}")` etc.
- `Assets/Resources/Input System/Controls.inputactions` — action maps **Player** (Move, Attack,
  PickUp, ToggleSpellbook/CharacterWindow/Inventory/Mount, Hotkey0-9, CycleHotbarPage, StartChat,
  Guild/Slash/Tell/ReplyCommand, 12 Emotes, RefreshPosition), **Targeting** (TargetUp/Down/Home,
  Confirm/CancelTarget), **UI** (Navigate, Submit, Cancel, Point, Click).
- `CharacterSettings.cs:6,78,110` — the **only** Newtonsoft usage:
  `using Newtonsoft.Json;`, `JsonConvert.DeserializeObject<CharacterSettings>(fileContents)`,
  `JsonConvert.SerializeObject(this)`. Fields (`:33-147`): `HotkeySetting[]`,
  `Dictionary<string, WindowSettings>` (window positions), `Dictionary<string, object>` options,
  `string MountName`.

Target repo current state — `~/code/Goose2ClientGodot/project.godot`:
- Godot 4.6, `[dotnet] project/assembly_name="Goose2ClientGodot"`.
- **No** `[autoload]` section, **no** `[input]` section. Carries 3D defaults to strip:
  `[physics] 3d/physics_engine="Jolt Physics"` and `[rendering] rendering_device/driver.windows="d3d12"`.
- No `.csproj`/`.sln` for the game yet (only the `tools/AssetConverter` solution exists).

### Full packet inventory (46) with authoritative prefixes

Read directly from each `public override string Prefix` getter in
`~/code/Goose2Client/Assets/Scripts/Network/Packets/*.cs` (2026-06-06). All 46 prefixes are
**unique** — no collisions in the dispatch table. Use these verbatim; do not re-guess.

| Packet class | Prefix | | Packet class | Prefix |
|---|---|---|---|---|
| `AdminModeActivatePacket` | `AMA` | | `MakeWindowPacket` | `MKW` |
| `AttackPacket` | `ATT` | | `MapObjectPacket` | `DOB` |
| `BankSlotPacket` | `SBS` | | `MoveCharacterPacket` | `MOC` |
| `BattleTextPacket` | `BT` | | `PingPacket` | `PING` |
| `BuffBarPacket` | `BUF` | | `SendCurrentMapPacket` | `SCM` |
| `CastPacket` | `CST` | | `SendMapNamePacket` | `SMN` |
| `ChangeHeadingPacket` | `CHH` | | `ServerMessagePacket` | `$` |
| `ChatPacket` | `^` | | `SetYourCharacterPacket` | `SUC` |
| `ClassUpdatePacket` | `CUP` | | `SetYourPositionPacket` | `SUP` |
| `ClearBankSlotPacket` | `CBS` | | `SpellbookSlotPacket` | `SSS` |
| `ClearCombineBagSlotPacket` | `CCS` | | `SpellCharacterPacket` | `SPP` |
| `ClearInventorySlotPacket` | `CIS` | | `SpellTilePacket` | `SPA` |
| `ClearVendorPacket` | `VCL` | | `StatusInfoPacket` | `SNF` |
| `CombineBagSlotPacket` | `SCS` | | `TellPacket` | `&` |
| `DoneSendingMapPacket` | `DSM` | | `TileUpdatePacket` | `TUP` |
| `EmotePacket` | `EMOT` | | `UpdateCharacterPacket` | `CHP` |
| `EndWindowPacket` | `ENW` | | `VendorSlotPacket` | `SVS` |
| `EraseCharacterPacket` | `ERC` | | `VitalsPercentagePacket` | `VPU` |
| `EraseObjectPacket` | `EOB` | | `WeaponSpeedPacket` | `WPS` |
| `ExperienceBarPacket` | `TNL` | | `WindowLinePacket` | `WNF` |
| `GroupUpdatePacket` | `GUD` | | | |
| `HashMessagePacket` | `#` | | | |
| `InventorySlotPacket` | `SIS` | | | |
| `LoginFailPacket` | `LNO` | | | |
| `LoginSuccessPacket` | `LOK` | | | |
| `MakeCharacterPacket` | `MKC` | | | |

> **Note the counter-intuitive pair:** `SpellCharacterPacket` is `SPP` and `SpellTilePacket` is
> `SPA` — the names read backwards relative to the prefixes, but this is what the source says.
> Port verbatim; do not "correct" them.

---

## Conventions for the implementer

- **Source of truth is the Unity project.** Open each file in `~/code/Goose2Client` and port it;
  do not reconstruct from this plan's summaries.
- **Verbatim where possible.** For the protocol core and packets, change *only* `Debug.Log` →
  `GD.Print` and namespaces. Keep field names, parse order, and logic identical.
- **Namespaces:** put everything under `Goose2Client.*` (e.g. `Goose2Client.Network`,
  `Goose2Client.Network.Packets`). Folder layout under `res://` mirrors the Unity `Scripts/` tree:
  `Scripts/Network/`, `Scripts/Network/Packets/`, `Scripts/GameManager.cs`, etc.
- **Threading rule:** the receive thread touches the socket and the raw byte/string buffer ONLY.
  Anything that reaches `PacketManager`, observers, or the scene tree must be marshaled to the main
  thread first. No Godot `Node` API is called from the receive thread except `CallDeferred` itself
  (which is documented thread-safe).
- **Validation is connection-based**, not unit tests: the definition of done is a running app that
  logs parsed packets from the real server. Add focused unit tests only for `PacketParser`
  (pure, easy) and the `System.Text.Json` round-trip of `CharacterSettings`.

---

### Task 0: Scaffold the Godot C# game project + strip 3D defaults

**Files:**
- Create: `Goose2ClientGodot.csproj`, `Goose2ClientGodot.sln` (Godot-style C# project at repo root)
- Edit: `project.godot` (remove 3D physics + d3d12 rendering defaults; add `[dotnet]` stays)
- Create: `Scripts/` folder tree (`Scripts/Network/Packets/`)

**Step 1:** Generate the game `.csproj`/`.sln`. Easiest path is to open the project once in the
Godot editor and add a trivial C# script (Godot writes the SDK-style `.csproj` targeting the
correct `net` version and `Godot.NET.Sdk`). Alternatively hand-author it to match a Godot 4.6 C#
template. Confirm `dotnet build` succeeds from the repo root.

**Step 2:** Add the JSON package decision to the game `.csproj`: `System.Text.Json` ships in the
.NET runtime, so **no PackageReference is needed** — just `using System.Text.Json;`. Do **not** add
Newtonsoft.

**Step 3:** Edit `project.godot` — delete `[physics] 3d/physics_engine` and
`[rendering] rendering_device/driver.windows` (2D client, no need for Jolt or a forced D3D12
driver). Leave `config/features` / `[dotnet]` as-is.

**Step 4:** `dotnet build` clean; open the editor once to confirm no import errors.

**Step 5:** Commit: `chore: scaffold Godot C# game project, drop 3D defaults`.

---

### Task 1: Port the InputMap from `Controls.inputactions`

**Files:**
- Edit: `project.godot` (add an `[input]` section)

**Step 1:** Read `~/code/Goose2Client/Assets/Resources/Input System/Controls.inputactions` and
enumerate every action + its bindings across the three maps (Player, Targeting, UI).

**Step 2:** For each action, add a Godot input action under `[input]` with the equivalent
`InputEventKey`/`InputEventMouseButton` events. Keep the **same action names** the code expects
(`Hotkey0`…`Hotkey9`, `Attack`, `PickUp`, `ToggleInventory`, `ConfirmTarget`, the 12 emotes, …) so
the later `PlayerInputManager` port reads `Input.IsActionPressed("Hotkey3")` unchanged.

**Step 3:** Open the editor → Project Settings → Input Map and confirm all actions appear with the
right bindings.

**Step 4:** Commit: `feat: port Unity input bindings to Godot InputMap`.

> This task only needs to be *complete enough to compile against* now; fine-tuning bindings can
> happen when `PlayerInputManager` is ported in a later step. Do not skip it — the action **names**
> are the contract.

---

### Task 2: Port the protocol primitives (`PacketParser`, `PacketHandler`)

**Files:**
- Create: `Scripts/Network/PacketParser.cs`
- Create: `Scripts/Network/PacketHandler.cs`
- Test: `tests/` (optional but recommended) `PacketParserTests.cs`

**Step 1:** Copy `PacketParser.cs` verbatim into `Goose2Client.Network`. It has no Unity coupling;
it should compile unchanged.

**Step 2:** Copy `PacketHandler.cs` verbatim (abstract base, `Observers`, `CallObservers`).

**Step 3 (recommended):** Add a small xUnit (or GdUnit) test for `PacketParser` covering
`GetNextToken`/`GetInt32`/`GetBool`/`Peek`/`GetSubstring` against a hand-built sample packet — this
is the one piece of pure logic worth pinning before everything depends on it.

**Step 4:** Commit: `feat: port PacketParser and PacketHandler`.

---

### Task 3: Port `PacketManager`

**Files:**
- Create: `Scripts/Network/PacketManager.cs`

**Step 1:** Copy `PacketManager.cs` verbatim. Replace the two `Debug.Log` calls (`:51`, `:61`) with
`GD.Print`. Keep `Listen<T>`/`Remove<T>`/`Clear`/`Handle` and the 0–8 char prefix-matching loop
identical.

**Step 2:** Confirm it builds against `PacketHandler`/`PacketParser` from Task 2.

**Step 3:** Commit: `feat: port PacketManager`.

---

### Task 4: Port all 46 packet classes

**Files:**
- Create: `Scripts/Network/Packets/<Name>Packet.cs` × 46

**Step 1:** Port each packet file verbatim into `Goose2Client.Network.Packets`. For each one:
- The `Prefix` is in the authoritative table above (verified 2026-06-06) — match it exactly.
  Still read the `Parse` body from source; only the prefixes are pre-confirmed.
- Swap any `UnityEngine` types you encounter for Godot/BCL equivalents
  (`UnityEngine.Color` → `Godot.Color`, `Vector2` → `Godot.Vector2`). Most packets are ints/strings
  and need no change — but **verify**, especially `UpdateCharacterPacket`, `MapObjectPacket`,
  `MakeCharacterPacket` (color/equipment fields).
- Replace any `Debug.Log` with `GD.Print`.

**Step 2:** This is mechanical, high-volume, and parallelizable — the 46 files are independent.
Consider fanning out with subagents (the inventory list above is the work-list), then build once at
the end.

**Step 3:** `dotnet build` clean — all 46 compile.

**Step 4:** Commit: `feat: port all 46 protocol packets`.

---

### Task 5: Rewrite `NetworkClient` — background receive thread + `CallDeferred`

**Files:**
- Create: `Scripts/Network/NetworkClient.cs`

This is the one real redesign. Port the socket setup and all ~35 typed send helpers verbatim, but
replace the `Update()`/`Select` poll with a dedicated thread.

**Step 1: Socket + send helpers (verbatim).** Copy `Connect`, `Disconnect`, `Send`, `IsConnected`,
`Pause`, and every typed send method (`Login`, `Move`, `Attack`, `CastSpell`, …) unchanged. `Send`
still appends `'\x1'` and writes ASCII. Sends happen on the main thread; concurrent with the receive
thread is safe (TCP sockets are full-duplex).

**Step 2: Receive thread.** Replace `Update()` with a `Start`/`Stop` receive loop:
- On `Connect` success, spawn `_recvThread = new Thread(ReceiveLoop) { IsBackground = true }`.
- `ReceiveLoop` does a **blocking** `socket.Receive(buffer)` (no `Select(..., 500)` — the dedicated
  thread is allowed to block). Convert to ASCII, append to `packetBuffer`, split on `'\x1'`, keep the
  trailing incomplete fragment, and marshal each complete packet to the main thread (Step 3).
- Loop exits when a cancellation flag is set or `Receive` returns 0 / throws (disconnect). Surface
  errors via the `SocketError`/`ConnectionError` events — **also marshaled** to the main thread.

**Step 3: Marshal to main thread via `CallDeferred`.** `NetworkClient` is a plain C# class, so it
can't `CallDeferred` on itself. Give it a reference to the `GameManager` autoload `Node` (injected in
Task 6) and, per complete packet, call:
```csharp
_gameManager.CallDeferred(GameManager.MethodName.HandlePacket, packet);
```
`GameManager.HandlePacket(string)` (Step in Task 6) runs on the main thread and calls
`PacketManager.Handle(packet)` — respecting the `Pause` flag there, not on the thread.

> **Alternative considered:** push packet strings into a `System.Collections.Concurrent.ConcurrentQueue<string>`
> and drain it in `GameManager._Process`. Equivalent and avoids per-packet deferred-call overhead, but
> we committed to `CallDeferred` for this plan. Note the tradeoff in a comment; revisit only if profiling
> shows the deferred calls are hot.

**Step 4: Clean shutdown.** `Disconnect` sets the cancel flag, calls `socket.Close()` (unblocks the
blocked `Receive`), then `_recvThread.Join()` with a short timeout. Ensure `Disconnect` is invoked
from `GameManager`'s `NOTIFICATION_WM_CLOSE_REQUEST` / `_ExitTree` so the thread never outlives the app.

**Step 5:** Replace the `:109` `Debug.Log` with `GD.Print`. Build clean.

**Step 6:** Commit: `feat: port NetworkClient with threaded receive + CallDeferred dispatch`.

---

### Task 6: `GameManager` autoload owning the network session

**Files:**
- Create: `Scripts/GameManager.cs`
- Edit: `project.godot` (register `[autoload]`)

**Step 1:** Port `GameManager` as a `Node` (or `Node2D`) autoload. Map the lifecycle:
- `Awake()` dedupe + `DontDestroyOnLoad` → autoload registration in `project.godot`
  (`[autoload] GameManager="*res://Scripts/GameManager.cs"`). Keep the static `Instance` getter so
  `GameManager.Instance.PacketManager` calls stay valid.
- `Awake()` field init (`NetworkClient`, `PacketManager`, …) → `_EnterTree`/`_Ready`.
- Inject `this` into `NetworkClient` so it can `CallDeferred` back (Task 5, Step 3).

**Step 2:** Add the main-thread entry point:
```csharp
public void HandlePacket(string packet)
{
    if (NetworkClient.Pause) return;
    PacketManager.Handle(packet);
}
```
This replaces the old `Update()` → `NetworkClient.Update()` poll entirely — there is no per-frame
socket work anymore. Keep the `Pause` flag for map transitions (`:90-127`).

**Step 3:** Wire app-shutdown: override `_Notification`; on `NOTIFICATION_WM_CLOSE_REQUEST` call
`NetworkClient.Disconnect()`.

**Step 4:** Build clean; launch the editor and confirm the autoload loads with no errors (it won't
connect yet — that's Task 8).

**Step 5:** Commit: `feat: add GameManager autoload owning the network session`.

---

### Task 7: Reimplement `CharacterSettings` with `System.Text.Json`

**Files:**
- Create: `Scripts/CharacterSettings.cs`
- Test: `tests/CharacterSettingsJsonTests.cs`

**Step 1:** Port `CharacterSettings` (`:33-147`). Replace:
- `using Newtonsoft.Json;` → `using System.Text.Json;` (+ `System.Text.Json.Serialization` if needed).
- `:78` `JsonConvert.DeserializeObject<CharacterSettings>(fileContents)` →
  `JsonSerializer.Deserialize<CharacterSettings>(fileContents)`.
- `:110` `JsonConvert.SerializeObject(this)` → `JsonSerializer.Serialize(this)`.

**Step 2: Watch the `System.Text.Json` gotchas** (it's stricter than Newtonsoft):
- It serializes **properties** by default, not fields — make the persisted members properties, or add
  `[JsonInclude]`, or set `IncludeFields = true` in options.
- `Dictionary<string, object>` values deserialize to `JsonElement`, not boxed primitives like
  Newtonsoft gave. Audit every read of the options dictionary and convert via `JsonElement` (or change
  the type to something concrete). **This is the only non-mechanical part of the port** — verify the
  on-disk shape still round-trips.
- File I/O moves from Unity paths to Godot `user://` (`ProjectSettings.GlobalizePath` / `FileAccess`).

**Step 3:** Add a round-trip test: construct a `CharacterSettings` with hotkeys + window positions +
options, serialize, deserialize, assert equality. This guards the `JsonElement`/field-vs-property
traps.

**Step 4:** Update `MIGRATION_PLAN.md`'s dependency table: change the Newtonsoft row to
`System.Text.Json` and note the `Dictionary<string,object>` → `JsonElement` caveat.

**Step 5:** Commit: `feat: port CharacterSettings to System.Text.Json`.

---

### Task 8: Live validation — connect and log packets

**Files:**
- Create: a minimal bootstrap scene (`Scenes/Main.tscn`) + a tiny script that calls
  `GameManager.Instance.NetworkClient.Connect(...)` (and `Login` if credentials are available).

**Step 1:** Wire a temporary listener in `GameManager._Ready` (or the bootstrap script) that
`Listen<T>`s a few high-traffic packets (`PingPacket`, `ServerMessagePacket`, `LoginFailPacket`,
`LoginSuccessPacket`) and `GD.Print`s their parsed fields.

**Step 2:** Run the project, point it at the real server (confirm host/port from the Unity
`LoginButton`/config). Confirm in the Godot output:
- TCP connection established (the `Connected` event fires on the main thread).
- Packets arrive, are split on `\x1`, dispatched on the main thread, and parse without the
  `PacketManager` error log firing.
- A clean `PingPacket` → `Pong()` round-trip (port the ping response if it's trivial), or at minimum
  that pings are received and parsed.

**Step 3:** Confirm clean shutdown: close the window, verify the receive thread exits (no hang, no
"thread still running" warning).

**Step 4:** Remove the temporary debug listeners (or gate them behind a debug flag).

**Step 5:** Commit: `feat: live-validate networked packet dispatch against server`.

---

## Definition of done

- `dotnet build` clean for the game project; editor opens with no import/autoload errors.
- `project.godot` has the autoload(s) and the full `[input]` action set; 3D defaults removed.
- Protocol core + all 46 packets ported, building, no Newtonsoft anywhere in the tree.
- `NetworkClient` runs a background receive thread; packets dispatch on the main thread via
  `CallDeferred`; thread shuts down cleanly on exit.
- Running the app connects to the live server and logs parsed packets; ping round-trips.
- `CharacterSettings` round-trips through `System.Text.Json` (test green).

## Explicitly out of scope (next plans)

- Scene flow (Login → Loading → Map) beyond a throwaway bootstrap scene — that's Step 4.
- Map rendering / `TileMapLayer` — Step 5.
- Characters, the layered animation system, UI windows — Steps 6–7.
- Full `PlayerInputManager` behavior (this plan only lands the InputMap **names**).
