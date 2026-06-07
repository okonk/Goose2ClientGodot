# Step 8 — Part 1: Correctness Fixes & Foundations

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans (or subagent-driven-development) to implement this plan task-by-task.
>
> This is **Part 1 of 2** of the execution-ready Step 8 plan (supersedes the scoping doc
> `docs/plans/2026-06-06-step8-polish-overlays.md`). Part 2 is
> `docs/plans/2026-06-07-step8-part2-overlays-and-polish.md`. Every task was verified against the
> Unity source AND the current Godot port — see **APIs Verified**.

**Goal (Part 1):** Fix two silently-broken networking behaviours found in review, add the small
hardening items, then build all the Character/MapManager/GameManager hooks and the two half-wired
interactive systems (paper-doll portrait, spell targeting). Part 1 leaves the engine ready so Part 2
is pure visual-overlay + polish work.

**Architecture:** Same conventions as Steps 6/7. Each visual element = a `.tscn` + `partial class`
under `Scripts/`. Pure logic (timing, layout, cycling) goes in **Godot-free** classes (kept Godot-free
so they're unit-testable) and is unit-tested; the test csproj auto-includes them via its
`Scripts/**/*.cs` glob (see below). Godot-typed files are validated by the headless smoke test + the
Part 2 live E2E. Packet listeners register in `_Ready` (named methods, `_listenersRegistered` guard) and
remove in `_ExitTree`.

**Tech Stack:** Godot 4.6 / C# (.NET 10), xUnit (`tests/Goose2Client.Tests`, `Scripts/**/*.cs` glob —
**no per-file `<Compile Include>`**), raw-socket text protocol.

**Repo / branch:** `/home/hayden/code/Goose2ClientGodot`, currently `master` (clean). Part 1 runs on
`feat/step8-part1` (Task 0). Merge before starting Part 2.

**Build gate (run after every task):**
- `dotnet build Goose2ClientGodot.csproj` → 0 errors
- `dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj` → green

---

## Review findings (drives both parts)

A four-agent review compared the Godot port against the Unity source. The known Step-6/7 deferrals
(A1–A3, B1–B4, C/D, E1) were confirmed. **Two CRITICAL gaps NOT previously tracked were found** and
are the first work in Part 1:

1. **`PingPacket` keepalive is never handled (CRITICAL).** `NetworkClient.Pong()` exists
   (`Scripts/Network/NetworkClient.cs:145`) but **no listener calls it**. Unity replied to every
   server `PING` (`Goose2Client/Assets/Scripts/MapManager.cs:112`). The server drops idle
   connections on its ping-timeout. → **Task 1**.
2. **`WeaponSpeedPacket` is never handled (CRITICAL).** Godot fires `Attack()` on every keypress
   throttled only by the local clip length (`Character.cs:319`), ignoring the server-set weapon
   speed. Unity gated on `MapManager.WeaponSpeed` (`PlayerController.cs:186`). → **Task 2**.
3. **`CastPacket` (remote caster's cast pose) was under-scoped** — the `Character.Cast()` hook is
   built in Part 1 Task 5; the listener lands in Part 2 (B4).

Confirmed **non-gaps** (do NOT build): audio (Unity plays none), lighting beyond a static global
light (no server light packet — C1 is an optional `CanvasModulate`, Part 2), packets `DSM`/`SMN`/`AMA`
(unused even in Unity).

---

## APIs Verified (citations — confirmed by reading source; shared by both parts)

Godot port (`Scripts/`):
- `Character/Character.cs:7` `Character : Node2D`; public surface: `LoginId`,`CharacterName`,`X`,`Y`,
  `Facing`,`MoveSpeed`,`IsMounted`,`IsLocalPlayer`,`HPPercent`,`MPPercent`,`BodyState` — **no**
  `CharacterType`, **no** `Height`, **no** appearance accessors, **no** `Cast()`/`AddBattleText`.
- `Character.cs:129-158` `ApplyAppearance(...)` — appearance args are method-locals, discarded.
- `Character.cs:251` `TriggerAttack()`, `:258` `AttackDuration()`, `:319` attack send,
  `:368-382` `PlayCurrent()`, `:386` `ResolveClip(slot,motion,state)`.
- `Character.cs:209-218` tint shader (alpha = blend factor); reusable for portrait/overlays.
- `MapManager.cs:19` `_characters` dict; `:28` `GetCharacter`; `:31` `LocalPlayer`; `:94` `IsValidMove`
  (no occupancy); `:52-65`/`:78-90` listener block; `:182-196` `_UnhandledInput` (tile-only, TODO `:190`).
- `GameManager.cs:13` `Instance`; `:15` `NetworkClient`; `:39` `SpellTargetManager`; `:42` `IsTargeting`;
  `:45` `CurrentMapManager`; `:67`/`:175` app-lifetime `Listen/Remove<ClassUpdatePacket>` (pattern to copy).
- `NetworkClient.cs:11-13` events (`ConnectionError`,`Connected`,`SocketError` — **no** `Disconnected`);
  `:101-102` `received==0` silent break; `:145` `Pong()`; `:165` `Attack()`; `:215` `CastSpell(slot,targetId)`.
- `SpellTargetManager.cs` — stub (`IsTargeting`, `Cast(SpellInfo)` no-op).
- `UI/VitalsCharacterDisplay.cs` — stub (resolves `LocalPlayer`, renders nothing). **Now attached** to
  `Scenes/UI/VitalsWindow.tscn` (the `Portrait` node, script `id=2_char`) with layer children under
  `Portrait/Mask/`: `Body`,`Eyes`,`Hair`,`Chest`,`Helmet` (TextureRects, `expand_mode=1`), plus
  `Portrait/CircleFrame` — added by UI overhaul Part 2. **Task 8 adapts to these, does NOT create them**
  (note `Eyes` not `Face`, `Helmet` not `Helm`, all under `Mask/`).
- `UI/SpellbookWindow.cs:108` `UseSpell` (drifted from `:97`) → `SpellTargetManager?.Cast(info)` in the
  non-`None`-target branch (Task 9 leaves `UseSpell` unchanged, so the drift is cosmetic).
- `CharacterSettings.cs:189` `GetOption<T>` (drifted from `:176` after the D1 visibility fields landed);
  `Constants.cs:114` `SpellTargetType {None=0,NPC=1,NPCPlayer=2,
  Player=3}`; `:122` `CharacterType {Player=1,Monster=2,Vendor=10,Banker=11,Quest=12}`; `:133`
  `Options.TargetFiltering`.
- `Character/AnimationNames.cs:21-28` `AttackVariant`: 4=1hand,5=staff,6=2hand,7=bow,_=no-equip;
  `:34-55` `Candidates(motion,state,facing)`. `Character/AnimationHeights.cs:28` `GetHeight(name)` default 64.
- Packets present & field-matched: `PingPacket` (prefix `PING`, no fields), `WeaponSpeedPacket`
  (prefix `WPS`, `int Speed`), plus the Part-2 overlay packets.
- `project.godot` InputMap: `TargetDown`,`TargetUp`,`ConfirmTarget`,`TargetHome`,`CancelTarget` exist.
- Test csproj globs `<Compile Include="../../Scripts/**/*.cs" />` (switched from explicit includes in UI
  overhaul Part 4). **Do NOT add per-file `<Compile Include>` lines** — new `Scripts/` files are
  auto-included, and an explicit duplicate triggers build error **NETSDK1022**. Test files in `tests/`
  are auto-globbed by the SDK too. (The whole Scripts tree — incl. Godot-typed classes — now compiles
  into the test assembly via the `GodotSharp` PackageReference, so the "Godot-free" rule is about
  *testability*, not the csproj.)

Unity reference (`/home/hayden/code/Goose2Client/Assets/Scripts/`, READ-ONLY):
- `MapManager.cs:112` Ping→Pong; `:183-195` `IsValidMove` incl. occupancy `Any(c=>c.X==x&&c.Y==y)` `:191`;
  `:253-258` `OnWeaponSpeed`. `PlayerController.cs:186` attack gated on `WeaponSpeed`.
- `GameManager.cs:41` `event Action<Character> CharacterUpdated`; `:48-51` `OnCharacterUpdated`.
- `Character.cs:30` `Height => body.Height`; `:351-355` `Cast()`; `:420-423` `AddBattleText`.
- `UI/VitalsCharacterDisplay.cs` — static 5-layer portrait (body/hair/eyes/chest/helm), humanoid
  `BodyId<100` (yOffset −20) vs monster `>=100` (body only, yOffset 0), size ×1.25, `_Tint` alpha=blend;
  on `VitalsCanvas.prefab` → **Vitals window**.
- `SpellTargetManager.cs` — `Cast`/`SetTarget`/`SwitchTarget`/`RemoveTarget`, `GetNextSpellCastTarget
  (searchDown)` (filter by `SpellTargetType` + view window |dx|≤10,|dy|≤8 + next/prev wrap by
  `Y*Width+X`); confirm→`CastSpell(slot,Target.LoginId)`. `SpellTarget.cs` empty (prefab-only reticle).

---

## Task 0: Branch setup

**Step 1:** `cd /home/hayden/code/Goose2ClientGodot && git checkout -b feat/step8-part1`
**Step 2:** Confirm baseline green:
```bash
dotnet build Goose2ClientGodot.csproj
dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj
```
Expected: 0 build errors; **159/160 tests pass**. The one known failure,
`MapFileTests.Map1_ParsesHeaderAndGrid`, is **pre-existing and environmental** — it hardcodes a path
from another sandbox (`MapFileTests.cs:7` → `/home/agent/workspace/...`) and reads `Map1.bytes`, which
isn't in this checkout (only `Map100+` exist). It is unrelated to Step 8 — do **not** treat it as a
regression. (Optional: derive `MapsDir` from the repo root and/or guard on the asset to get a fully
green baseline.) Commit nothing yet.

---

## Task 1: Ping keepalive (CRITICAL) — `PingPacket` → `Pong()`

Connection persists across maps, so register at app-lifetime in `GameManager` (mirror the existing
`ClassUpdatePacket` listener), not `MapManager`.

**Files:** Modify `Scripts/GameManager.cs` (`_Ready` `:67`, `_ExitTree` `:175`, add handler).

**Step 1:** In `_Ready` after the `ClassUpdatePacket` listen (`:67`): `PacketManager.Listen<PingPacket>(OnPing);`
**Step 2:** Handler near `OnClassUpdate` (`:131`):
```csharp
private void OnPing(object packetObj) => NetworkClient.Pong();
```
**Step 3:** In `_ExitTree` (`:175`): `PacketManager.Remove<PingPacket>(OnPing);`
**Step 4:** Build → 0 errors. **Step 5:** Commit.
```bash
git commit -am "fix(net): reply to server PING with PONG so idle sessions don't time out"
```

---

## Task 2: Weapon-speed attack gating (CRITICAL) — `WeaponSpeedPacket`

Extract the cadence decision into a pure helper, store the speed on `MapManager`, consult before sending.

**Verify first:** read Unity `PlayerController.cs:180-195` + `MapManager.cs:253-258` to confirm the
units of `WeaponSpeed` (plan assumes **milliseconds between allowed attacks** — the `WPS` `int Speed`)
and the default before any `WPS` arrives. Adjust the divisor if Unity differs.

**Files:**
- Create: `Scripts/Character/AttackGate.cs` + `tests/Goose2Client.Tests/AttackGateTests.cs`
- Modify: test csproj, `Scripts/MapManager.cs`, `Scripts/Character/Character.cs:319`

**Step 1 — failing test.** `AttackGateTests.cs`:
```csharp
using Goose2Client.Character;
using Xunit;

public class AttackGateTests
{
    [Fact] public void FirstAttackAlwaysAllowed()
        => Assert.True(new AttackGate().TryAttack(0.0, 1000));

    [Fact] public void SecondAttackBlockedWithinWindow()
    {
        var g = new AttackGate();
        Assert.True(g.TryAttack(0.0, 1000));
        Assert.False(g.TryAttack(0.5, 1000));
    }

    [Fact] public void SecondAttackAllowedAfterWindow()
    {
        var g = new AttackGate();
        Assert.True(g.TryAttack(0.0, 1000));
        Assert.True(g.TryAttack(1.0, 1000));
    }

    [Fact] public void ZeroSpeedFallsBackToDefault()
    {
        var g = new AttackGate();
        Assert.True(g.TryAttack(0.0, 0));
        Assert.False(g.TryAttack(0.1, 0));
    }
}
```
**Step 2 — run, expect FAIL:** `dotnet test … --filter AttackGateTests`
**Step 3 — implement** `Scripts/Character/AttackGate.cs`:
```csharp
namespace Goose2Client.Character
{
    /// <summary>Throttles local attack sends to the server weapon speed
    /// (Unity PlayerController gated on MapManager.WeaponSpeed). Pure/testable.</summary>
    public sealed class AttackGate
    {
        public const double DefaultWindowSeconds = 0.5;   // matches Character.AttackDuration fallback
        private double _lastAttack = double.NegativeInfinity;

        /// <param name="weaponSpeedMs">Server WPS value (ms between attacks); ≤0 ⇒ default.</param>
        public bool TryAttack(double nowSeconds, int weaponSpeedMs)
        {
            double window = weaponSpeedMs > 0 ? weaponSpeedMs / 1000.0 : DefaultWindowSeconds;
            if (nowSeconds - _lastAttack < window) return false;
            _lastAttack = nowSeconds;
            return true;
        }
    }
}
```
**Step 4 — (no csproj edit needed):** the test csproj globs `Scripts/**/*.cs`, so `AttackGate.cs` is
auto-included. **Step 5 — run, expect PASS.**
**Step 6 — wire MapManager** (property + listener in `_Ready`/`_ExitTree`):
```csharp
public int WeaponSpeed { get; private set; }
// _Ready listener block:  pm.Listen<WeaponSpeedPacket>(OnWeaponSpeed);
// _ExitTree remove block:  pm.Remove<WeaponSpeedPacket>(OnWeaponSpeed);
private void OnWeaponSpeed(object packetObj) => WeaponSpeed = ((WeaponSpeedPacket)packetObj).Speed;
```
**Step 7 — gate the send in Character.** Add `private readonly AttackGate _attackGate = new();` and
change `Character.cs:319`:
```csharp
if (Input.IsActionJustPressed("Attack"))
{
    int ws = GameManager.Instance.CurrentMapManager?.WeaponSpeed ?? 0;
    if (_attackGate.TryAttack(Time.GetTicksMsec() / 1000.0, ws))
    {
        TriggerAttack();
        GameManager.Instance.NetworkClient.Attack();
    }
}
```
**Step 8 — build + test green. Step 9 — commit.**
```bash
git add Scripts/Character/AttackGate.cs tests/Goose2Client.Tests/AttackGateTests.cs \
        Scripts/MapManager.cs Scripts/Character/Character.cs
git commit -m "fix(combat): gate local attacks on server WeaponSpeed (WPS) instead of clip length"
```

---

## Task 3: `IsValidMove` occupancy check (D3)

Restore Unity's occupied-tile block (`MapManager.cs:191`).

**Files:** Modify `Scripts/MapManager.cs:94`.

**Step 1:**
```csharp
public bool IsValidMove(int x, int y)
{
    if (_map == null || x < 0 || y < 0 || x >= _map.Width || y >= _map.Height) return false;
    if (_map[x, y].IsBlocked) return false;
    foreach (var c in _characters.Values)
        if (c.X == x && c.Y == y) return false;
    return true;
}
```
**Step 2:** Build → 0 errors. **Step 3:** Commit.
```bash
git commit -am "fix(move): block local-player prediction onto tiles occupied by another character"
```

---

## Task 4: `NetworkClient` "Disconnected" event (D2)

Hook for a future reconnect/session layer on graceful remote close, marshaled to the main thread.

**Files:** Modify `Scripts/Network/NetworkClient.cs:11-13` and `:101-102`.

**Step 1:** Add `public event Action? Disconnected;` (`:13`).
**Step 2:** Fire on the graceful-close path (`:101-102`):
```csharp
int received = socket!.Receive(buffer);
if (received == 0)
{
    if (running)
        Callable.From(() => Disconnected?.Invoke()).CallDeferred();   // main-thread, like SocketError
    break;
}
```
**Step 3:** Build → 0 errors. **Step 4:** Commit.
```bash
git commit -am "feat(net): fire main-thread Disconnected event on graceful remote close"
```
> No consumer required yet; optionally subscribe in `GameManager` to log it.

---

## Task 5: Shared prerequisites — Character appearance/Height/CharacterType/Cast, `CharacterUpdated`, `Characters`

Everything visual (Tasks 7–9, and Part 2) needs data the Godot `Character`/`MapManager`/`GameManager`
don't expose yet. Build it here.

**Files:**
- Create: `Scripts/Character/AppearanceData.cs` (holds `Godot.Color` — **not** in the test csproj).
- Modify: `Scripts/Character/Character.cs`, `Scripts/MapManager.cs`, `Scripts/GameManager.cs`.

**Step 1 — `CharacterType` on Character.** Verify the field name in
`Scripts/Network/Packets/MakeCharacterPacket.cs`, then add `public CharacterType CharacterType { get;
private set; }` and set it in both `SetAppearance` overloads (`:93`,`:116`) from the packet (MKC has it).

**Step 2 — appearance snapshot.** `Scripts/Character/AppearanceData.cs`:
```csharp
using Godot;
namespace Goose2Client.Character
{
    /// <summary>Read-only appearance snapshot for the portrait (A1). Equipment tint Color alpha is the
    /// blend factor (A==0 ⇒ no tint).</summary>
    public readonly struct AppearanceData
    {
        public readonly int BodyId; public readonly Color BodyColor;
        public readonly int HairId; public readonly Color HairColor;
        public readonly int FaceId;
        public readonly int ChestId; public readonly Color ChestColor;
        public readonly int HelmId;  public readonly Color HelmColor;
        public readonly bool IsMonster;   // BodyId >= 100
        public AppearanceData(int bodyId, Color bodyColor, int hairId, Color hairColor, int faceId,
            int chestId, Color chestColor, int helmId, Color helmColor)
        {
            BodyId = bodyId; BodyColor = bodyColor; HairId = hairId; HairColor = hairColor;
            FaceId = faceId; ChestId = chestId; ChestColor = chestColor; HelmId = helmId;
            HelmColor = helmColor; IsMonster = bodyId >= 100;
        }
    }
}
```
In `Character.cs`, store the resolved values (reuse the `Equip`/underwear results already computed in
`ApplyAppearance` `:133-146`) into `private AppearanceData _appearance;` at the end of `ApplyAppearance`,
and add `public AppearanceData GetAppearance() => _appearance;`.

**Step 3 — `Height` property** (Unity `Height => body.Height`):
```csharp
public int Height =>
    _slots.TryGetValue(CharacterSlot.Body, out var b)
        ? _heights.GetHeight($"Body-{b.GraphicId}-{ResolveClip(b, "idle", BodyState) ?? "idle-down"}")
        : 0;
```

**Step 4 — `Cast()` on Character** (remote caster pose; consumed by Part 2 B4):
```csharp
/// <summary>Play the caster's spell-cast pose. Locked like an attack so walk/idle don't clobber it.</summary>
public void Cast()
{
    _attackLocked = true;
    _attackTimer = AttackDuration();
    PlayCurrent();
}
```
> **Verify:** does the converter emit a `cast-<dir>` clip family? If yes, extend
> `CharacterMotion.State`/`AnimationNames` to return "cast" while a cast lock is active (mirror attack)
> so `Cast()` swings the cast clip; if no, document the attack-clip fallback + leave a TODO.

**Step 5 — `CharacterUpdated` event on GameManager** (mirror Unity `GameManager.cs:41`):
```csharp
public event System.Action<Character.Character> CharacterUpdated;
public void OnCharacterUpdated(Character.Character c) => CharacterUpdated?.Invoke(c);
```
Fire from `MapManager` for the **local player** at the end of `OnMakeCharacter` (`:108`),
`OnUpdateCharacter` (`:133`), `OnSetYourCharacter` (`:115`):
`if (c == _localPlayer || c.LoginId == _myLoginId) GameManager.Instance.OnCharacterUpdated(c);`

**Step 6 — `Characters` enumerable on MapManager** (for targeting, Task 9):
```csharp
public System.Collections.Generic.IEnumerable<Character.Character> Characters => _characters.Values;
```
**Step 7 — build (0 errors); existing tests green. Step 8 — commit.**
```bash
git add Scripts/Character/AppearanceData.cs Scripts/Character/Character.cs Scripts/MapManager.cs Scripts/GameManager.cs
git commit -m "feat(character): expose appearance snapshot, Height, CharacterType, Cast(); add CharacterUpdated + Characters"
```

---

## Task 6: World-overlay base — timed-fade + follow helper (foundation for Part 2 B1–B4)

**Files:**
- Create: `Scripts/Overlays/OverlayLifetime.cs` + `tests/Goose2Client.Tests/OverlayLifetimeTests.cs`
- Modify: test csproj
- Create: `Scripts/Overlays/WorldOverlay.cs` (Godot base `Node2D`)

**Step 1 — failing tests.** `OverlayLifetimeTests.cs`:
```csharp
using Goose2Client.Overlays;
using Xunit;

public class OverlayLifetimeTests
{
    [Fact] public void NotExpiredBeforeDuration()
    { var l = new OverlayLifetime(1.0); l.Advance(0.5); Assert.False(l.Expired); }

    [Fact] public void ExpiredAtDuration()
    { var l = new OverlayLifetime(1.0); l.Advance(1.0); Assert.True(l.Expired); }

    [Fact] public void RiseAccumulatesAtRate()
    { var l = new OverlayLifetime(1.0, 32); l.Advance(0.5); Assert.Equal(16.0, l.RiseOffsetPixels, 3); }
}
```
**Step 2 — run, expect FAIL. Step 3 — implement** `Scripts/Overlays/OverlayLifetime.cs`:
```csharp
namespace Goose2Client.Overlays
{
    /// <summary>Pure lifetime/rise accumulator for world overlays (battle text, bubbles, spell fx).</summary>
    public sealed class OverlayLifetime
    {
        private readonly double _duration, _riseRate;
        public double Elapsed { get; private set; }
        public OverlayLifetime(double durationSeconds, double risePixelsPerSecond = 0)
        { _duration = durationSeconds; _riseRate = risePixelsPerSecond; }
        public void Advance(double delta) => Elapsed += delta;
        public bool Expired => Elapsed >= _duration;
        public double RiseOffsetPixels => Elapsed * _riseRate;
    }
}
```
**Step 4 — (no csproj edit; auto-included via the glob). Step 5 — run, expect PASS.**
**Step 6 — Godot base** `Scripts/Overlays/WorldOverlay.cs`:
```csharp
using Godot;
namespace Goose2Client.Overlays
{
    /// <summary>Base for a transient overlay parented to a Character/tile. Self-frees when its
    /// OverlayLifetime expires. Subclasses set Lifetime in _Ready and override Tick for visuals.</summary>
    public partial class WorldOverlay : Node2D
    {
        protected OverlayLifetime Lifetime;
        public override void _Process(double delta)
        {
            if (Lifetime == null) return;
            Lifetime.Advance(delta);
            Tick(delta);
            if (Lifetime.Expired) QueueFree();
        }
        protected virtual void Tick(double delta) { }
    }
}
```
**Step 7 — build + test green. Step 8 — commit.**
```bash
git add Scripts/Overlays/OverlayLifetime.cs Scripts/Overlays/WorldOverlay.cs \
        tests/Goose2Client.Tests/OverlayLifetimeTests.cs
git commit -m "feat(overlays): add OverlayLifetime (tested) + WorldOverlay base node"
```

---

## Task 7: `AddBattleText` plumbing on Character (foundation for Part 2 B1)

Add the Character-side hook now (the visual `BattleText` node + layout lands in Part 2 B1). Mirror
Unity `Character.cs:420`. Add a lazy battle-text container child (ZIndex 20, like the name/HP bar):
```csharp
public void AddBattleText(BattleTextType type, string text)
{
    _battleText ??= EnsureBattleTextContainer();   // a Node2D child, ZIndex 20
    _battleText.AddText(type, text, Height);        // Overlays.BattleText.AddText — built in Part 2 B1
}
```
> Because the `Overlays.BattleText` type is built in Part 2, in Part 1 add **only** the method
> signature + container scaffold guarded so it compiles (e.g. a minimal `Overlays.BattleText : Node2D`
> stub with `AddText(BattleTextType,string,int)` that no-ops, replaced by the real node in Part 2).
> This keeps Part 1 building green while exposing the hook. Alternatively defer the whole method to
> Part 2 B1 — **decide at execution time**: if Part 1 and Part 2 are separate PRs, prefer the no-op
> stub so `MapManager`'s future `BattleTextPacket` listener has a stable target.

**Step 1 — implement the method + minimal stub node. Step 2 — build 0 errors; smoke test zero-error.
Step 3 — commit.**
```bash
git commit -am "feat(character): AddBattleText hook + container scaffold (B1 visual lands in Part 2)"
```

---

## Task 8: A1 — `VitalsCharacterDisplay` paper-doll portrait

Static 5-layer portrait of the local player; refresh on `CharacterUpdated`. Uses the per-graphic
`SpriteFrames` (idle-down frame 0) as a static texture — **no** `AnimationManager.GetFrame` port needed.

**Decision (verified — now confirmed by the actual scene):** Unity places this on the **Vitals window**
(`VitalsCanvas.prefab`), and **UI overhaul Part 2 already built the node tree** in
`Scenes/UI/VitalsWindow.tscn`. **Adapt to it; do NOT recreate it:** the `Portrait` node (a `Control`)
**already has `VitalsCharacterDisplay.cs` attached**, with layer children under `Portrait/Mask/`:
`Body`, `Eyes`, `Hair`, `Chest`, `Helmet` (TextureRects, fixed 53×53, `expand_mode=1`), plus a
`Portrait/CircleFrame` overlay. **Node names differ from the original draft** — it's `Eyes` (not `Face`),
`Helmet` (not `Helm`), and every layer is under `Mask/`. The script extends `Control` and is attached to
`Portrait`, so its `GetNode` paths are relative to `Portrait` (e.g. `"Mask/Body"`, `"Mask/Helmet"`), and
the layers are `TextureRect`s — set `.Texture`.

**Files:** Modify `Scripts/UI/VitalsCharacterDisplay.cs` only (the scene tree + `VitalsWindow.cs` wiring
already exist — verify/adjust, don't rebuild).

**Step 1 — implement the control:**
```csharp
public override void _Ready()
{
    GameManager.Instance.CharacterUpdated += OnCharacterUpdated;
    Refresh();
}
public override void _ExitTree() => GameManager.Instance.CharacterUpdated -= OnCharacterUpdated;

private void OnCharacterUpdated(Character.Character c)
{
    if (c == GameManager.Instance.CurrentMapManager?.LocalPlayer) Refresh();
}

public void Refresh()
{
    _localPlayer = GameManager.Instance.CurrentMapManager?.LocalPlayer;
    if (_localPlayer == null) { HideAll(); return; }
    var a = _localPlayer.GetAppearance();

    // 1st arg = scene node path (relative to Portrait, under Mask/); 2nd arg = Assets/Sprites folder.
    SetLayer("Mask/Body", "Body", a.BodyId, a.BodyColor, a.IsMonster ? 0 : -20);
    if (a.IsMonster) { ClearLayer("Mask/Hair"); ClearLayer("Mask/Eyes"); ClearLayer("Mask/Chest"); ClearLayer("Mask/Helmet"); return; }
    SetLayer("Mask/Hair", "Hair", a.HairId, a.HairColor, -20);
    SetLayer("Mask/Eyes", "Eyes", a.FaceId, new Color(0,0,0,0), -20);
    SetLayer("Mask/Chest", "Chest", a.ChestId, a.ChestColor, -20);
    SetLayer("Mask/Helmet", "Helm", a.HelmId, a.HelmColor, -20);   // node "Helmet", asset folder "Helm"
}
```
**Step 2 — layer helper** (idle-down frame 0 as static texture, ×1.25, tint shader reused from
`Character.cs:212-217`):
```csharp
private void SetLayer(string node, string folder, int graphicId, Color tint, int yOffset)
{
    var rect = GetNode<TextureRect>(node);
    if (graphicId <= 0) { rect.Visible = false; return; }
    var path = $"res://Assets/Sprites/{folder}/{graphicId}/animations.tres";
    if (!ResourceLoader.Exists(path)) { rect.Visible = false; return; }
    var frames = GD.Load<SpriteFrames>(path);
    string anim = frames.HasAnimation("idle-down") ? "idle-down"
                : frames.HasAnimation("idle") ? "idle" : null;
    if (anim == null || frames.GetFrameCount(anim) == 0) { rect.Visible = false; return; }
    var tex = frames.GetFrameTexture(anim, 0);
    rect.Texture = tex; rect.Visible = true;
    rect.CustomMinimumSize = rect.Size = tex.GetSize() * 1.25f;
    rect.Position = new Vector2(0, yOffset);
    ApplyTint(rect, tint);   // tint.A==0 ⇒ remove material; else shader
}
```
> Factor the tint shader into a shared helper to avoid a 3rd copy (`Character.cs:209-218`, and
> `Scripts/UI/Icon.cs` already reuses the `_Tint` blend shader per Step 7). Reuse `Icon`'s helper if
> exposed; else add `Scripts/UI/TintShader.cs` (`static Shader` + `Apply(CanvasItem,Color)`) and
> refactor both — small optional DRY pass.

**Step 3 — scene (already built; verify/adjust only, do NOT recreate).** The
`Portrait`→`Mask`→`Body/Eyes/Hair/Chest/Helmet` (+`CircleFrame`) tree already exists in
`Scenes/UI/VitalsWindow.tscn` with the script attached (UI overhaul Part 2). Verify: child order under
`Mask` is back-to-front (`Body → Eyes → Hair → Chest → Helmet` — matches the scene); set
`texture_filter = Nearest` on the layers if not already; confirm `VitalsWindow.cs` calls
`Portrait.Refresh()` on show (wire it if missing). **Caveat:** the layers are fixed 53×53 with
`expand_mode=1`, so the `×1.25` resize in Step 2's helper may fight the scene layout — if so, drop the
explicit `Size`/`Position` set and let the scene anchors place them (decide live).
**Step 4 — headless scene-load check** (existing smoke harness): no parse/script errors.
**Step 5 — build (0 errors); full-HUD smoke still zero-error. Step 6 — commit.**
```bash
git add Scripts/UI/VitalsCharacterDisplay.cs Scenes/UI/VitalsWindow.tscn Scripts/UI/VitalsWindow.cs
git commit -m "feat(ui): render local-player paper-doll portrait in the Vitals window (A1)"
```
**Acceptance:** body/hair/eyes/chest/helm render + re-render on equip; monster forms body-only; dyed
slots tint; scene + HUD load headless zero-error. (Visual confirm → Part 2 E1.)

---

## Task 9: A2 — On-screen spell targeting (`SpellTargetManager`)

Enter targeting on a targeted cast, cycle valid characters, draw a reticle, confirm →
`CastSpell(slot, target.LoginId)`, cancel → exit. Cycling logic is pure + heavily tested.

**Files:**
- Create: `Scripts/TargetCycler.cs` + `tests/Goose2Client.Tests/TargetCyclerTests.cs`; modify test csproj
- Rewrite: `Scripts/SpellTargetManager.cs` (make it a `Node` under `GameManager` so it gets input, OR
  route input from `MapManager._UnhandledInput`)
- Create: `Scenes/UI/SpellTarget.tscn` + `Scripts/SpellTarget.cs` (reticle)

**Step 1 — failing tests** (`TargetCyclerTests.cs`):
```csharp
using System.Collections.Generic;
using Goose2Client;
using Xunit;

public class TargetCyclerTests
{
    private static TargetCandidate C(int id,int x,int y,CharacterType t)=>new(id,x,y,t);

    [Fact] public void FiltersToPlayersWhenTargetTypeIsPlayer()
    {
        var all = new List<TargetCandidate>{ C(1,5,5,CharacterType.Monster), C(2,6,5,CharacterType.Player) };
        Assert.Equal(2, TargetCycler.Next(all, null, (5,5), 100, SpellTargetType.Player, true, true)?.LoginId);
    }

    [Fact] public void FiltersOutPlayersWhenTargetTypeIsNpc()
    {
        var all = new List<TargetCandidate>{ C(1,5,5,CharacterType.Monster), C(2,6,5,CharacterType.Player) };
        Assert.Equal(1, TargetCycler.Next(all, null, (5,5), 100, SpellTargetType.NPC, true, true)?.LoginId);
    }

    [Fact] public void SkipsCandidatesOutsideViewWindow()
    {
        var all = new List<TargetCandidate>{ C(1,50,50,CharacterType.Monster) };
        Assert.Null(TargetCycler.Next(all, null, (5,5), 100, SpellTargetType.NPC, true, true));
    }

    [Fact] public void WrapsAroundWhenSearchingPastEnd()
    {
        var all = new List<TargetCandidate>{ C(1,5,5,CharacterType.Monster), C(2,7,5,CharacterType.Monster) };
        Assert.Equal(1, TargetCycler.Next(all, C(2,7,5,CharacterType.Monster), (6,5), 100,
            SpellTargetType.NPC, true, true)?.LoginId);
    }

    [Fact] public void NoFilteringForNpcPlayerType()
    {
        var all = new List<TargetCandidate>{ C(1,5,5,CharacterType.Player), C(2,6,5,CharacterType.Monster) };
        Assert.NotNull(TargetCycler.Next(all, null, (5,5), 100, SpellTargetType.NPCPlayer, true, true));
    }
}
```
**Step 2 — run, expect FAIL. Step 3 — implement** `Scripts/TargetCycler.cs`, porting Unity
`GetNextSpellCastTarget` (`SpellTargetManager.cs:164-237`) exactly — position key `Y*mapWidth+X`,
view window `|dx|≤10 && |dy|≤8`, filter by `SpellTargetType` (skip filter for `NPCPlayer`), next/prev
wrap. Static + data-only:
```csharp
namespace Goose2Client
{
    public readonly record struct TargetCandidate(int LoginId, int X, int Y, CharacterType Type);
    public static class TargetCycler
    {
        public const int ViewRangeX = 10, ViewRangeY = 8;
        public static TargetCandidate? Next(
            System.Collections.Generic.IEnumerable<TargetCandidate> all, TargetCandidate? current,
            (int x,int y) player, int mapWidth, SpellTargetType type, bool filteringEnabled, bool searchDown)
        {
            // ... faithful port of Unity lines 164-237; tests above lock the contract ...
        }
    }
}
```
**Step 4 — (no csproj edit; auto-included via the glob). Step 5 — run, expect PASS.**
**Step 6 — reticle scene** `Scenes/UI/SpellTarget.tscn` (Node2D + reticle Sprite); `SpellTarget.cs`
thin. `ResizeTarget` math from Unity (`heightScaled=max(1,Height/32)`, `width=max(1,h*0.75)`,
`yOffset=(h-1)*0.5`) → convert to pixels using `Character.Height`.
**Step 7 — implement SpellTargetManager:** `Cast(SpellInfo)` seeds target + reticle, sets
`IsTargeting`; `TargetDown/Up` build `TargetCandidate`s from `MapManager.Characters` and call
`TargetCycler.Next(...)` with `CharacterSettings.GetOption<bool>(Options.TargetFiltering, true)`;
`TargetHome` → local player; `ConfirmTarget` → `SpellCooldownManager.Cast(slot)` +
`NetworkClient.CastSpell(slot, Target.LoginId)`, exit; `CancelTarget` → exit, no cast. Gate targeting
input on `IsTargeting` (Godot has no Unity-style input-map switch).
**Step 8 — build + test green; SpellTarget.tscn loads headless; HUD smoke zero-error. Step 9 — commit.**
```bash
git add Scripts/TargetCycler.cs tests/Goose2Client.Tests/TargetCyclerTests.cs \
        Scripts/SpellTargetManager.cs Scripts/SpellTarget.cs Scenes/UI/SpellTarget.tscn
git commit -m "feat(spells): on-screen spell targeting — TargetCycler (tested) + reticle + confirm/cancel (A2)"
```
**Acceptance:** targeted cast enters targeting; cycle/home/confirm/cancel work; filtering respects the
option; non-targeted spells still cast immediately (`SpellbookWindow.UseSpell` unchanged). Live → Part 2 E1.

---

## Part 1 wrap-up
Build green, all tests pass, headless HUD smoke zero-error. Merge `feat/step8-part1` to `master`.
Then start Part 2 (`docs/plans/2026-06-07-step8-part2-overlays-and-polish.md`).

## Open verifications resolved during execution (no plan-time punts)
- **Task 2:** confirm `WPS.Speed` units (assumed ms/attack) vs Unity `PlayerController.cs:180-195`.
- **Task 5:** confirm `MakeCharacterPacket.CharacterType` field name; confirm whether a `cast-<dir>`
  clip family exists (drives whether `Cast()` swings a real cast clip or reuses attack).
- **Task 7:** decide stub-node vs defer for `AddBattleText` based on PR boundary (prefer no-op stub).
- **Task 8:** ✅ resolved — Vitals placement is confirmed by the live `VitalsWindow.tscn` (the
  `Portrait` node tree already exists; adapt to its node names, see Task 8).
