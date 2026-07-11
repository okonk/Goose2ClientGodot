# Unity-Parity Bugfixes — Part 1: Critical & Networking Fixes

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Fix the highest-severity porting bugs from the 2026-07-11 Godot-vs-Unity comparison: dropped in-game map changes, the packet-drain pause leak, plus four small high-value logic fixes — and land the shared `GameColors` palette that Parts 2 and 3 build on.

**Architecture:** Every fix restores Unity-reference behavior (`/home/hayden/code/Goose2Client`, git HEAD) in the Godot port. Pure logic goes in engine-free classes under `Scripts/` so xUnit can cover it (repo convention); engine-touching code is verified by `dotnet build` + the manual E1 pass. No new subsystems — each task is a surgical diff.

**Tech Stack:** Godot 4.6 C# (GodotSharp), xUnit (`tests/Goose2Client.Tests`), .NET SDK.

**Series:** Part 1 of 3.
- **Part 1 (this file):** critical + networking + small logic fixes (Tasks 0–7)
- **Part 2** (`2026-07-11-unity-parity-part2-character-movement-animation.md`): cast animation, tap-to-turn, movement correctness, names, health bars — **requires Part 1** (GameColors)
- **Part 3** (`2026-07-11-unity-parity-part3-ui-and-world-polish.md`): chat colors, vitals level, hotbar, emotes, targeting persistence, tint/z-order/anchors — **requires Parts 1 and 2**

**Commands:**
- Build: `dotnet build Goose2ClientGodot.csproj` (run from repo root; expect 0 errors)
- Tests: `dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj` (all green; ~144 existing)

---

## APIs verified (citations into both repos)

| API / fact | Where verified |
|---|---|
| `PausablePacketQueue.Handle/Drain`, ctor `(Func<bool> isPaused, Action<string> dispatch)` | `Scripts/Network/PausablePacketQueue.cs:16-45` |
| Unity re-buffers remaining packets when a handler pauses mid-batch | Unity `NetworkClient.cs:100-104` (git HEAD; working tree has broken uncommitted edits — do NOT consult it) |
| `GameManager.SetPaused/ChangeMap/HandlePacket/EnsureHud/Hud/PacketManager` | `Scripts/GameManager.cs:85-128, 172-178` |
| Unity in-world listener `Listen<SendCurrentMapPacket>` → `ChangeMap` | Unity `MapManager.cs:45, 169-175` |
| Godot's only `SendCurrentMapPacket` listener is LoginScene, removed in `_ExitTree` | `Scripts/LoginScene/LoginScene.cs:40, 65, 116-120` |
| `SendCurrentMapPacket.MapFileName/.MapName` usage | `Scripts/LoginScene/LoginScene.cs:118-119` |
| `PacketManager.Listen<T>/Remove<T>` pattern | `Scripts/GameManager.cs:70-71, 182-183` |
| Unity clears/unfocuses chat on every map load | Unity `LoadingMapScene.cs:50-52` |
| Godot `ChatWindow.ClearAndUnfocus()` (currently private) | `Scripts/UI/ChatWindow.cs:151-155` |
| `GameHud.Chat` typed accessor | `Scripts/UI/GameHud.cs:18` |
| `TargetCycler.Next` idx==-1 backward bug | `Scripts/TargetCycler.cs:41-59` |
| Unity default weapon speed 1.0 s | Unity `MapManager.cs:30` (`WeaponSpeed = 1.0f`), consumed `PlayerController.cs:186` |
| `AttackGate.TryAttack(nowSeconds, weaponSpeedMs)`, `DefaultWindowSeconds = 0.5` | `Scripts/Character/AttackGate.cs:7-17` |
| Unity blocks attack while mounted | Unity `PlayerController.cs:189` (`!character.IsMounted`) |
| Godot attack input block (no mount check) | `Scripts/Character/Character.cs:378-386` |
| Unity game palette (Yellow 248,208,0 / Green 136,204,64 / Red 254,81,28 / Blue 0,146,255) | Unity `Colors.cs:9-15` |
| Unity health-bar colors (112,232,120 / 244,133,50 / 191,64,64) | Unity `CharacterHealthBar.cs:20-24` |
| Test framework: xUnit; GodotSharp referenced by the test project (Godot structs OK in tests) | `tests/Goose2Client.Tests/Goose2Client.Tests.csproj:8-11` |

**Explicitly NOT fixed anywhere in this series (decided, with reasons):**
- **Ground-click Y convention** — Unity sends a flipped Y for ground clicks but server-space Y for character clicks over the same `LC`/`RC` message; Godot consistently sends server-space. Godot's version is almost certainly the correct one. Confirm against the live server during E1; don't change code now.
- **Character click hit-test picks last sibling, not Y-sort order** (`Scripts/MapManager.cs:232-235`) — only matters for overlapping characters on adjacent tiles; needs live confirmation before choosing a tiebreak.
- **Short (<48 px) spell effects sit 8 px lower than Unity** — the port deliberately re-derived spell anchoring; revisit only if E1 screenshots show it.
- **CHP appearance updates restart animation phase** — cosmetic hiccup; Unity's early-out optimization can be ported later if it bothers anyone.
- **Missing-texture map objects not registered** (`Scripts/MapManager.cs:327-328`) — Godot's behavior is safer than Unity's.

---

### Task 0: Shared game palette — `GameColors`

Unity's custom `Colors.cs` palette was never ported; Godot files silently resolved `Colors.*` to **Godot's** built-in palette. Create the shared class first — Part 2 (health bars) and Part 3 (chat colors) consume it.

**CRITICAL naming note:** do NOT name the class `Colors`. Godot source files `using Godot;` resolve bare `Colors` to `Godot.Colors`; adding a `Goose2Client.Colors` would silently re-bind every existing `Colors.X` reference in the `Goose2Client` namespace (e.g. `Character.cs:49,72`). `GameColors` cannot collide.

**Files:**
- Create: `Scripts/GameColors.cs`
- Test: `tests/Goose2Client.Tests/GameColorsTests.cs`

**Step 1: Write the failing test**

```csharp
using Goose2Client;
using Xunit;

namespace Goose2Client.Tests
{
    public class GameColorsTests
    {
        // Values from Unity Colors.cs:9-15 and CharacterHealthBar.cs:20-24
        [Theory]
        [InlineData("f8d000", nameof(GameColors.Yellow))]
        [InlineData("88cc40", nameof(GameColors.Green))]
        [InlineData("fe511c", nameof(GameColors.Red))]
        [InlineData("0092ff", nameof(GameColors.Blue))]
        [InlineData("70e878", nameof(GameColors.HpGreen))]
        [InlineData("f48532", nameof(GameColors.HpOrange))]
        [InlineData("bf4040", nameof(GameColors.HpRed))]
        public void Palette_MatchesUnityValues(string expectedHex, string name)
        {
            var color = (Godot.Color)typeof(GameColors).GetField(name).GetValue(null);
            Assert.Equal(expectedHex, color.ToHtml(false));
        }
    }
}
```

**Step 2: Run to verify it fails**

Run: `dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj --filter GameColorsTests`
Expected: FAIL — `GameColors` does not exist (compile error).

**Step 3: Implement**

```csharp
using Godot;

namespace Goose2Client
{
    /// <summary>The game palette — port of Unity Colors.cs (chat/UI) plus the overhead
    /// HP-bar colors from Unity CharacterHealthBar. Named GameColors, NOT Colors, so it
    /// can never shadow Godot.Colors in files that `using Godot`.</summary>
    public static class GameColors
    {
        public static readonly Color White = new(1f, 1f, 1f);
        public static readonly Color Yellow = Rgb(248, 208, 0);
        public static readonly Color Green = Rgb(136, 204, 64);
        public static readonly Color Red = Rgb(254, 81, 28);
        public static readonly Color Blue = Rgb(0, 146, 255);

        // Overhead character HP bar thresholds (Unity CharacterHealthBar.SetHPPercent)
        public static readonly Color HpGreen = Rgb(112, 232, 120);
        public static readonly Color HpOrange = Rgb(244, 133, 50);
        public static readonly Color HpRed = Rgb(191, 64, 64);

        private static Color Rgb(int r, int g, int b) => new(r / 255f, g / 255f, b / 255f);
    }
}
```

**Step 4: Run tests to verify pass**

Run: `dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj --filter GameColorsTests`
Expected: PASS (7 cases).

**Step 5: Commit**

```bash
git add Scripts/GameColors.cs tests/Goose2Client.Tests/GameColorsTests.cs
git commit -m "feat(ui): add GameColors palette (port of Unity Colors.cs + health-bar colors)"
```

---

### Task 1: `PausablePacketQueue.Drain` must stop when a drained packet re-pauses

Unity re-buffers the rest of the batch the moment a handler sets `Pause` (Unity `NetworkClient.cs:100-104` at HEAD). Godot's drain loop ignores the pause flag, so a `SendCurrentMapPacket` handled mid-drain (back-to-back map changes — and Task 2 makes this reachable) lets the rest of the queue leak into the wrong map context.

**Files:**
- Modify: `Scripts/Network/PausablePacketQueue.cs:41-45`
- Test: `tests/Goose2Client.Tests/PauseQueueTests.cs` (append)

**Step 1: Write the failing test** (append to the existing class; it already uses the `bool paused` + `List<string> recorded` pattern)

```csharp
[Fact]
public void Drain_StopsWhenDispatchRePauses_AndPreservesRemainder()
{
    bool paused = true;
    var recorded = new List<string>();
    PausablePacketQueue queue = null;
    queue = new PausablePacketQueue(() => paused, s =>
    {
        recorded.Add(s);
        if (s == "MAP_CHANGE") paused = true;   // handler triggers another map change
    });

    queue.Handle("A");
    queue.Handle("MAP_CHANGE");
    queue.Handle("B");   // belongs to the NEXT map — must not dispatch yet

    paused = false;
    queue.Drain();

    Assert.Equal(new[] { "A", "MAP_CHANGE" }, recorded);
    Assert.Equal(1, queue.Count);   // "B" still buffered

    paused = false;
    queue.Drain();
    Assert.Equal(new[] { "A", "MAP_CHANGE", "B" }, recorded);
}
```

**Step 2: Run to verify it fails**

Run: `dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj --filter Drain_StopsWhenDispatchRePauses_AndPreservesRemainder`
Expected: FAIL — actual recorded is `A, MAP_CHANGE, B` on the first drain.

**Step 3: Implement** — replace the `Drain` body:

```csharp
/// <summary>
/// Drain queued packets in FIFO order. Stops immediately if a dispatched packet
/// re-pauses the client (e.g. a map-change handler), leaving the remainder buffered
/// for the next unpause — mirrors Unity NetworkClient's mid-batch re-buffer.
/// Safe to call on an empty queue (no-op).
/// </summary>
public void Drain()
{
    while (_queued.Count > 0 && !_isPaused())
        _dispatch(_queued.Dequeue());
}
```

**Step 4: Run the full test file** — `dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj --filter PauseQueueTests` → all PASS.

**Step 5: Commit**

```bash
git add Scripts/Network/PausablePacketQueue.cs tests/Goose2Client.Tests/PauseQueueTests.cs
git commit -m "fix(network): stop Drain when a dispatched packet re-pauses (Unity parity)"
```

---

### Task 2: In-game map changes — listen for `SendCurrentMapPacket` after login (headline bug)

After the login scene frees itself, **nothing** listens for `SendCurrentMapPacket`; warps/doors/death-recalls are silently dropped. Unity's in-world `MapManager` listens (Unity `MapManager.cs:45,169-175`). In Godot the natural owner is the persistent `GameManager` (it owns `ChangeMap` and outlives scene swaps). Move the responsibility there and delete LoginScene's duplicate so login doesn't double-fire.

**Files:**
- Modify: `Scripts/GameManager.cs` (`_Ready`, `_ExitTree`, new handler)
- Modify: `Scripts/LoginScene/LoginScene.cs:40, 65, 116-120` (remove listener + handler)

**Step 1: Add the listener to GameManager.** In `_Ready` (`Scripts/GameManager.cs:70-71`), after the `PingPacket` line:

```csharp
// In-world map changes (warp, door, death recall). Unity's MapManager listened for this
// (MapManager.cs:45); here the persistent GameManager owns it so the subscription
// survives scene swaps — the login scene's copy was removed with the scene.
PacketManager.Listen<SendCurrentMapPacket>(OnSendCurrentMap);
```

Add the handler next to `OnPing` (`Scripts/GameManager.cs:136`):

```csharp
private void OnSendCurrentMap(object packetObj)
{
    var p = (SendCurrentMapPacket)packetObj;
    ChangeMap(p.MapFileName, p.MapName);
}
```

In `_ExitTree` (`Scripts/GameManager.cs:180-185`), add symmetric removal:

```csharp
PacketManager.Remove<SendCurrentMapPacket>(OnSendCurrentMap);
```

**Step 2: Delete LoginScene's copy.** In `Scripts/LoginScene/LoginScene.cs` remove:
- line 40: `gm.PacketManager.Listen<SendCurrentMapPacket>(OnSendCurrentMap);`
- line 65: `gm.PacketManager.Remove<SendCurrentMapPacket>(OnSendCurrentMap);`
- lines 116-120: the whole `OnSendCurrentMap` method.

The login flow is unchanged: the packet now reaches GameManager's identical handler instead.

**Step 3: Trace the back-to-back case (no code, just confirm while reviewing):** during `ChangeMap` the client is paused, so a second `SendCurrentMapPacket` queues; the `finally` `SetPaused(false)` → `Drain()` dispatches it → `OnSendCurrentMap` → `ChangeMap` → `SetPaused(true)` → Task 1's fixed `Drain` stops, preserving the new map's packets. This is exactly why Task 1 sequences first.

**Step 4: Build** — `dotnet build Goose2ClientGodot.csproj` → 0 errors. Full test suite still green.

**Step 5: Commit**

```bash
git add Scripts/GameManager.cs Scripts/LoginScene/LoginScene.cs
git commit -m "fix(network): handle SendCurrentMapPacket for in-game map changes (was login-only)"
```

---

### Task 3: Clear/unfocus chat on every map change

Unity: `LoadingMapScene.cs:50-52` (`chatWindow.ClearAndRemoveFocus()`). Godot never does this — dying/warping while typing leaves focus in chat and movement keys keep typing.

**Files:**
- Modify: `Scripts/UI/ChatWindow.cs:151` (visibility)
- Modify: `Scripts/GameManager.cs:106-108` (`ChangeMap`)

**Step 1:** Make the method public — `Scripts/UI/ChatWindow.cs:151`: `private void ClearAndUnfocus()` → `public void ClearAndUnfocus()`.

**Step 2:** At the top of `GameManager.ChangeMap` (before `SetPaused(true)`):

```csharp
// Unity clears/unfocuses chat on every map load (LoadingMapScene.cs:50-52)
if (Hud != null && GodotObject.IsInstanceValid(Hud))
    Hud.Chat?.ClearAndUnfocus();
```

(`Hud` is null on the first, login-time map change — that's fine, there's no chat yet.)

**Step 3: Build** → 0 errors. **Step 4: Commit**

```bash
git add Scripts/UI/ChatWindow.cs Scripts/GameManager.cs
git commit -m "fix(ui): clear and unfocus chat input on map change (Unity parity)"
```

---

### Task 4: `TargetCycler` backward-cycle off-by-one

When the current target isn't in the filtered list (`idx == -1` — always true on the FIRST cycle press, because targeting seeds with the local player who is filtered out for NPC spells), the backward branch computes `(-1 - 1 + n) % n = n - 2`, skipping the bottom-most candidate.

**Files:**
- Modify: `Scripts/TargetCycler.cs:51-59`
- Test: `tests/Goose2Client.Tests/TargetCyclerTests.cs` (append)

**Step 1: Write the failing test** (match the existing test file's construction style for `TargetCandidate` — check its helpers first and reuse them):

```csharp
[Fact]
public void Next_CurrentNotInFilteredList_SearchUp_ReturnsLastCandidate()
{
    // Three NPCs sorted by position key; "current" is the (player) target that the
    // NPC filter removed, so it is not present in the candidate list at all.
    var npcs = new[]
    {
        new TargetCandidate(1, 1, 1, CharacterType.NPC),
        new TargetCandidate(2, 2, 1, CharacterType.NPC),
        new TargetCandidate(3, 3, 1, CharacterType.NPC),
    };
    var current = new TargetCandidate(99, 1, 1, CharacterType.Player);

    var next = TargetCycler.Next(npcs, current, player: (1, 1), mapWidth: 100,
        SpellTargetType.NPC, filteringEnabled: true, searchDown: false);

    Assert.Equal(3, next.Value.LoginId);   // last, not n-2 (which would be LoginId 2)
}
```

**Step 2: Run to verify FAIL** — `dotnet test ... --filter Next_CurrentNotInFilteredList_SearchUp_ReturnsLastCandidate` — actual is LoginId 2.

**Step 3: Implement** — replace the wrap block (`Scripts/TargetCycler.cs:51-59`):

```csharp
// Move to next (or previous) with wrap. When the current target isn't in the
// filtered list (idx == -1), "next" is the first entry and "previous" is the last.
if (idx == -1)
    idx = searchDown ? 0 : filtered.Count - 1;
else
    idx = searchDown ? (idx + 1) % filtered.Count
                     : (idx - 1 + filtered.Count) % filtered.Count;
```

**Step 4:** `dotnet test ... --filter TargetCyclerTests` → all PASS.

**Step 5: Commit**

```bash
git add Scripts/TargetCycler.cs tests/Goose2Client.Tests/TargetCyclerTests.cs
git commit -m "fix(targeting): backward cycle lands on last candidate when current is filtered out"
```

---

### Task 5: Pre-WPS attack throttle — default 1.0 s, not 0.5 s

Unity defaults `WeaponSpeed = 1.0f` seconds (Unity `MapManager.cs:30`) until the first WPS packet; Godot's `AttackGate` falls back to 0.5 s, letting players attack twice as fast right after map load.

**Files:**
- Modify: `Scripts/Character/AttackGate.cs:7`
- Test: `tests/Goose2Client.Tests/AttackGateTests.cs`

**Step 1: Update/extend the tests.** Open `AttackGateTests.cs`; change any expectation built on the 0.5 s default and add:

```csharp
[Fact]
public void TryAttack_NoWeaponSpeedYet_UsesUnityOneSecondDefault()
{
    var gate = new AttackGate();
    Assert.True(gate.TryAttack(10.0, weaponSpeedMs: 0));
    Assert.False(gate.TryAttack(10.7, weaponSpeedMs: 0));   // would pass under the old 0.5s
    Assert.True(gate.TryAttack(11.0, weaponSpeedMs: 0));
}
```

**Step 2: Run to verify FAIL.**

**Step 3: Implement** — `Scripts/Character/AttackGate.cs:7`:

```csharp
public const double DefaultWindowSeconds = 1.0;   // Unity MapManager.cs:30 default until first WPS
```

(The 0.5 s figure lives on independently as `Character.AttackDuration`'s clip-length fallback — that one is a Unity-unrelated animation fallback; leave it.)

**Step 4:** `dotnet test ... --filter AttackGateTests` → PASS. **Step 5: Commit**

```bash
git add Scripts/Character/AttackGate.cs tests/Goose2Client.Tests/AttackGateTests.cs
git commit -m "fix(combat): pre-WPS attack window is 1.0s like Unity, not 0.5s"
```

---

### Task 6: No attacking while mounted

Unity: `PlayerController.cs:189` gates on `!character.IsMounted`. Godot's attack block (`Scripts/Character/Character.cs:378-386`) has no check — mounted players swing and send `ATT`.

**Files:**
- Modify: `Scripts/Character/Character.cs:378`

**Step 1:** Change the attack condition:

```csharp
// Held button keeps swinging; AttackGate throttles repeats to the weapon-speed interval.
// Unity suppresses attacks entirely while mounted (PlayerController.cs:189).
if (!IsMounted && Input.IsActionPressed("Attack"))
```

**Step 2:** Build → 0 errors. **Step 3: Commit**

```bash
git add Scripts/Character/Character.cs
git commit -m "fix(combat): suppress attack input while mounted (Unity parity)"
```

---

### Task 7: Part 1 verification + docs note

**Step 1:**
- `dotnet build Goose2ClientGodot.csproj` → 0 errors, 0 new warnings.
- `dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj` → all green (existing ~144 + GameColors 7 + PauseQueue 1 + TargetCycler 1 + AttackGate 1).

**Step 2: Update `MIGRATION_PLAN.md`.** Add a dated section "2026-07-11 Unity-parity bugfix pass — Part 1" with one line per fix (reference this plan file), and append to the manual E1 checklist:
- warp between maps via a door/teleport tile (headline fix), including a back-to-back warp
- chat clears/unfocuses on death/warp
- first "previous target" press reaches the bottom-most candidate
- attack cadence before the first WPS packet ≈ 1/s; mounted attack suppressed

**Step 3: Commit**

```bash
git add MIGRATION_PLAN.md
git commit -m "docs: record Unity-parity bugfix pass Part 1"
```
