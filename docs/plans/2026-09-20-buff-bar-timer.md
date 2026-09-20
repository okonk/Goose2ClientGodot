# Buff Bar Timer Implementation Plan

**Goal:** Show remaining time on each buff icon — a clockwise sweep that fills as expiry approaches, a countdown label, red tint ≤ 10s, icon blink ≤ 15s — by adding the total duration (ms) to the `BUF` packet.

**Architecture:** Server appends a 5th `durationMs` field to the existing `BUF` packet (no new packet, no periodic traffic; the bar is already re-sent on every add/renew/remove). The client parses the field, counts down locally from packet-arrival time (same convention as `CDR` cooldown sync), and drives a growth-mode `CooldownOverlay` plus an icon blink in `BuffEffect`.

**Tech Stack:** C# / .NET 10 (server `illutiagooseserver`, client `Goose2ClientGodot` Godot 4), xunit tests in both repos.

**Worktrees (do all work here, not in the main checkouts):**
- Server: `/home/agent/workspace/illutiagooseserver/.worktrees/buff-bar-timer` (branch `buff-bar-timer`)
- Client: `/home/agent/workspace/Goose2ClientGodot/.worktrees/buff-bar-timer` (branch `buff-bar-timer`)

**Design doc:** `docs/plans/2026-09-20-buff-bar-timer-design.md` (client worktree).

**Repo rule (AGENTS.md):** no comments or doc strings in new/modified code unless the "why" is non-obvious (e.g. wire-format detail, clock-drift workaround). Existing unrelated comments stay untouched.

**APIs verified:**

| API | Citation |
|---|---|
| `P.BuffBar` (current `Func<Buff?, int, string>`) | `illutiagooseserver/Goose/Packets.cs:659-667` |
| `Player.SendBuffBar` (only call site of `P.BuffBar`) | `illutiagooseserver/Goose/Player.cs:2560-2578` |
| `Buff.SpellEffect`, `Buff.TimeCast` | `illutiagooseserver/Goose/Buff.cs` |
| `SpellEffect.Duration` (`long`, in ticks) | `illutiagooseserver/Goose/SpellEffect.cs:123` |
| `GameWorld.TimerFrequency` (`long`, Stopwatch ticks/sec; = `Stopwatch.Frequency`) | `illutiagooseserver/Goose/GameWorld.cs:78,108` |
| `TestWorldFixture.CommandPlayerOn` → `CapturingPlayer` with `List<string> Sent`, `State = Ready` | `illutiagooseserver/TestSupport/TestWorldFixture.cs:85-105` |
| `TestWorldFixture.AddBaseSpellEffect(id, name, configure)` | `TestSupport/TestWorldFixture.cs:66` |
| `GooseSettings.BuffBarVisibleSize` (fixture default 0 → no empty-slot padding; set via ctor configure action) | `illutiagooseserver/Goose/GooseSettings.cs:149` |
| `PacketParser.GetInt64()`, `LengthRemaining()` | `Goose2ClientGodot/Scripts/Network/PacketParser.cs:66,104` |
| `CooldownOverlay.Update(remaining, total)`, `FormatCountdown`, `_Draw` pie | `Goose2ClientGodot/Scripts/UI/CooldownOverlay.cs:44-57,105-122` |
| Hotbar per-frame drive pattern | `Goose2ClientGodot/Scripts/UI/HotbarSlot.cs:65-71` |
| `Icon.Apply` does NOT touch `Modulate` (blink via Modulate is safe) | `Goose2ClientGodot/Scripts/UI/Icon.cs:14-30` |
| Overlay node pattern in a slot scene | `Goose2ClientGodot/Scenes/UI/HotbarSlot.tscn:39-47` |
| `BuffEffect.SetEffect/ClearEffect/BuildTooltip` | `Goose2ClientGodot/Scripts/UI/BuffEffect.cs:41-70` |
| `BuffEffectsWindow.OnBuffBar` → `slot.SetEffect(packet)` | `Goose2ClientGodot/Scripts/UI/BuffEffectsWindow.cs:56-61` |
| Client packet-test pattern (`new X().Parse(new PacketParser(...))`) | `Goose2ClientGodot/tests/Goose2Client.Tests/MapFlagsPacketTests.cs` |

---

### Task 1: Server — duration field in `BUF` packet

**Worktree:** server. **Files:**
- Modify: `Goose/Packets.cs:659-667` (`P.BuffBar`)
- Modify: `Goose/Player.cs:2560-2578` (`SendBuffBar`)
- Create: `Goose.Tests/BuffBarPacketTests.cs`

**Mutation impact:**
- Source of truth: `Buff.SpellEffect.Duration` (ticks) + `world.TimerFrequency` (ticks/sec) — the packet is built fresh on every `SendBuffBar` call; nothing is cached.
- Readers: only `Player.SendBuffBar` calls `P.BuffBar` (verified: single call site). Client reader is `BuffBarPacket.Parse` (Task 2).
- Derived/cached state: none.
- Propagation: no sequence needed — `SendBuffBar` is already invoked on every add/renew/remove/login (Player.cs:2008, 2354, 2387, 2553, LoginContinuedEvent.cs:67, DoneLoadingMapEvent.cs:95).
- Invariants: empty-slot form stays exactly `"BUF" + index` (no trailing comma); timed buff → `Duration * 1000 / TimerFrequency` ms; permanent buff (`Duration == 0`) → `0`.
- Observable proof: `CapturingPlayer.Sent` assertions on the exact wire strings below.

**Step 1: Write the failing tests**

`Goose.Tests/BuffBarPacketTests.cs` (pattern from `BuffNullGuardTests.cs`):

```csharp
using Goose.Testing;

namespace Goose.Tests;

public class BuffBarPacketTests
{
    [Fact]
    public void SendBuffBar_TimedBuff_SendsDurationMs()
    {
        using var fixture = new TestWorldFixture(s => s.BuffBarVisibleSize = 3);
        var map = fixture.AddBaseMap(1, "m");
        var player = fixture.CommandPlayerOn(map, 1, 1);
        var effect = fixture.AddBaseSpellEffect(1, "Speed",
            e => { e.Duration = (long)(120.0 * fixture.World.TimerFrequency); e.BuffGraphic = 5; e.BuffGraphicFile = 12; });
        player.Buffs.Add(new Buff { Caster = player, Target = player, SpellEffect = effect });

        player.SendBuffBar(fixture.World);

        Assert.Contains("BUF1,5,12,Speed,120000", player.Sent);
        Assert.Contains("BUF2", player.Sent);
        Assert.Contains("BUF3", player.Sent);
    }

    [Fact]
    public void SendBuffBar_PermanentBuff_SendsZeroDuration()
    {
        // Duration 0 → "BUF1,5,12,Speed,0" (client shows no sweep for 0)
        using var fixture = new TestWorldFixture();
        var map = fixture.AddBaseMap(1, "m");
        var player = fixture.CommandPlayerOn(map, 1, 1);
        var effect = fixture.AddBaseSpellEffect(1, "Charm",
            e => { e.Duration = 0; e.BuffGraphic = 5; e.BuffGraphicFile = 12; });
        player.Buffs.Add(new Buff { Caster = player, Target = player, SpellEffect = effect });

        player.SendBuffBar(fixture.World);

        Assert.Contains("BUF1,5,12,Charm,0", player.Sent);
    }

    [Fact]
    public void SendBuffBar_EmptySlot_HasNoDurationField()
    {
        // Adversarial: catches unconditionally appending ",0" to empty slots
        using var fixture = new TestWorldFixture(s => s.BuffBarVisibleSize = 2);
        var map = fixture.AddBaseMap(1, "m");
        var player = fixture.CommandPlayerOn(map, 1, 1);

        player.SendBuffBar(fixture.World);

        Assert.Contains("BUF1", player.Sent);
        Assert.Contains("BUF2", player.Sent);
        Assert.DoesNotContain("BUF1,0", player.Sent);
    }
}
```

`AddBaseSpellEffect` sets `SpellEffect.Name = name` (`TestWorldFixture.cs:66-73`, verified). `CommandPlayerOn` leaves `State = Ready` (`= 3`), which passes `SendBuffBar`'s `State <= LoadingGame` guard (`Player.cs:61-67,2561`).

**Step 2: Run to verify red**

```bash
cd /home/agent/workspace/illutiagooseserver/.worktrees/buff-bar-timer
dotnet test Goose.Tests --filter FullyQualifiedName~BuffBarPacketTests
```
Expected: FAIL — `P.BuffBar` still takes 2 args (compile error) or the asserted strings are absent.

**Step 3: Implement**

`Goose/Packets.cs` — change the delegate to carry the duration:

```csharp
public static Func<Buff?, int, long, string> BuffBar = (buff, index, durationMs) =>
{
    if (buff is null)
    {
        return "BUF" + index;
    }

    return "BUF" + index + "," + buff.SpellEffect.BuffGraphic + "," + buff.SpellEffect.BuffGraphicFile + "," + buff.SpellEffect.Name + "," + durationMs;
};
```

`Goose/Player.cs` `SendBuffBar` — compute ms at the only call site (`world` is in scope):

```csharp
world.Send(this, P.BuffBar(buff, i, buff.SpellEffect.Duration * 1000 / world.TimerFrequency));
```

Empty-slot call becomes `P.BuffBar(null, i, 0)`.

**Step 4: Run to verify green**

```bash
dotnet test Goose.Tests --filter FullyQualifiedName~BuffBarPacketTests
dotnet test
```
Expected: new tests pass; full suite green (baseline 926 + 284).

**Step 5: Commit**

```bash
git add Goose/Packets.cs Goose/Player.cs Goose.Tests/BuffBarPacketTests.cs
git commit -m "feat(net): include buff duration in BUF packet"
```

---

### Task 2: Client — parse `DurationMs` in `BuffBarPacket`

**Worktree:** client. **Files:**
- Modify: `Scripts/Network/Packets/BuffBarPacket.cs`
- Create: `tests/Goose2Client.Tests/BuffBarPacketTests.cs`

**Mutation impact:**
- Source of truth: the wire packet; `DurationMs` is additive with default `0`.
- Readers: `BuffEffectsWindow.OnBuffBar` → `BuffEffect.SetEffect` (Task 4 consumes it).
- Back-compat: old server (no 5th field) → `DurationMs` stays 0 → no sweep. `LengthRemaining()` is 0 after the name when the field is absent (`PacketParser.cs:104-110`).
- Invariants: 4-field packet parses exactly as before; 5th field is a `long` via `GetInt64` (`PacketParser.cs:66`).
- Observable proof: parse tests below, including the old-server shape.

**Step 1: Write the failing tests**

`tests/Goose2Client.Tests/BuffBarPacketTests.cs`:

```csharp
using Goose2Client.Network;
using Goose2Client.Network.Packets;
using Xunit;

namespace Goose2Client.Network.Packets.Tests
{
    public class BuffBarPacketTests
    {
        [Fact]
        public void ParsesDurationMs()
        {
            var p = (BuffBarPacket)new BuffBarPacket().Parse(new PacketParser("BUF1,5,12,Speed,120000", "BUF"));
            Assert.Equal(0, p.SlotNumber);
            Assert.Equal(5, p.GraphicId);
            Assert.Equal(12, p.GraphicFile);
            Assert.Equal("Speed", p.Name);
            Assert.Equal(120000, p.DurationMs);
        }

        [Fact]
        public void OldServerPacketWithoutDuration_ParsesZero()
        {
            var p = (BuffBarPacket)new BuffBarPacket().Parse(new PacketParser("BUF1,5,12,Speed", "BUF"));
            Assert.Equal("Speed", p.Name);
            Assert.Equal(0, p.DurationMs);
        }

        [Fact]
        public void EmptySlot_ParsesDefaults()
        {
            var p = (BuffBarPacket)new BuffBarPacket().Parse(new PacketParser("BUF3", "BUF"));
            Assert.Equal(2, p.SlotNumber);
            Assert.Equal(0, p.DurationMs);
            Assert.Null(p.Name);
        }
    }
}
```

**Step 2: Run to verify red**

```bash
cd /home/agent/workspace/Goose2ClientGodot/.worktrees/buff-bar-timer
dotnet test tests/Goose2Client.Tests --filter FullyQualifiedName~BuffBarPacketTests
```
Expected: compile error — `DurationMs` does not exist.

**Step 3: Implement**

In `BuffBarPacket.Parse`, after reading `Name` (inside the existing `LengthRemaining() > 0` block):

```csharp
if (p.LengthRemaining() > 0)
    packet.DurationMs = p.GetInt64();
```

Add `public long DurationMs { get; set; }`.

**Step 4: Run to verify green**

```bash
dotnet test tests/Goose2Client.Tests
```
Expected: all pass (baseline 600 + 3 new).

**Step 5: Commit**

```bash
git add Scripts/Network/Packets/BuffBarPacket.cs tests/Goose2Client.Tests/BuffBarPacketTests.cs
git commit -m "feat(net): parse buff duration from BUF packet"
```

---

### Task 3: Client — `CooldownOverlay` growth mode + danger tint

**Worktree:** client. **Files:**
- Modify: `Scripts/UI/CooldownOverlay.cs`
- Create: `tests/Goose2Client.Tests/CooldownOverlayGrowthTests.cs`

The pie/label rendering itself is Godot `_Draw` — not unit-testable in this repo's xunit harness (existing `CooldownOverlayTests` only cover `FormatCountdown`). So the decision logic is extracted into pure statics that ARE tested; the `_Draw` wiring is verified by hand (Task 5).

**Step 1: Write the failing tests**

`tests/Goose2Client.Tests/CooldownOverlayGrowthTests.cs`:

```csharp
using Goose2Client.UI;
using Xunit;

namespace Goose2Client.Tests
{
    public class CooldownOverlayGrowthTests
    {
        [Theory]
        [InlineData(1.0, 1.0, false, 1.0)]   // shrinking: full cover at cast
        [InlineData(0.5, 1.0, false, 0.5)]
        [InlineData(0.0, 1.0, false, 0.0)]   // shrinking: gone when done
        [InlineData(1.0, 1.0, true, 0.0)]    // growth: empty when fresh
        [InlineData(0.5, 1.0, true, 0.5)]
        [InlineData(0.0, 1.0, true, 1.0)]    // growth: full at expiry
        [InlineData(-1.0, 1.0, true, 1.0)]   // growth: clamped past expiry
        [InlineData(2.0, 1.0, false, 1.0)]   // shrinking: clamped
        [InlineData(0.5, 0.0, true, 0.0)]    // zero total: no pie
        public void ComputeProgress_MatchesMode(double remaining, double total, bool growth, float expected)
        {
            Assert.Equal(expected, CooldownOverlay.ComputeProgress(remaining, total, growth), 3);
        }

        [Fact]
        public void BlinkAlpha_OscillatesBetween03And10_WithOneSecondPeriod()
        {
            for (double t = 0; t < 10; t += 0.01)
            {
                var a = BuffEffect.BlinkAlpha(t);
                Assert.InRange(a, 0.3f, 1.0f);
            }

            for (double t = 0; t < 5; t += 0.37)
                Assert.Equal(BuffEffect.BlinkAlpha(t), BuffEffect.BlinkAlpha(t + 1), 3);
        }
    }
}
```

**Step 2: Run to verify red** — same command shape as Task 2; expected: compile errors, `ComputeProgress`/`BlinkAlpha` missing.

**Step 3: Implement**

`Scripts/UI/CooldownOverlay.cs`:

- Add `internal static float ComputeProgress(double remainingSeconds, double totalSeconds, bool growthMode)`:
  - `totalSeconds <= 0` → `0f`
  - shrinking: `Clamp(remaining / total)` (current behavior)
  - growth: `Clamp(1 - remaining / total)`
- Add optional params to `Update`: `public void Update(double remainingSeconds, double totalSeconds, bool growthMode = false, double dangerSeconds = 0)`. Existing hotbar call site (`HotbarSlot.cs:71`) is untouched.
  - `_progress = ComputeProgress(remainingSeconds, totalSeconds, growthMode)`
  - store `_remaining = remainingSeconds; _dangerSeconds = dangerSeconds;`
  - **Early-return change (critical):** the current `if (remainingSeconds <= 0 || totalSeconds <= 0)` hides the overlay. Keep that for shrinking mode and for `totalSeconds <= 0`, but in growth mode with `remainingSeconds <= 0` do NOT hide — set `_progress = 1f`, keep the label (shows "0"), and redraw. Rationale: with a client clock ahead of the server, remaining can hit 0 a beat before the server's remove packet; the pie must stay full/red until the slot is actually cleared (design: "fully covered at expiry until the server's remove packet lands").
- In `_Draw`, pie color: `new Color(0.8f, 0.1f, 0.1f, 0.7f)` when `_dangerSeconds > 0 && _remaining <= _dangerSeconds`, else the current `new Color(0, 0, 0, 0.7f)`. Label stays white (unchanged).

`Scripts/UI/BuffEffect.cs` (needed by the test; full behavior lands in Task 4):

- Add `internal static float BlinkAlpha(double nowSeconds) => 0.65f + 0.35f * (float)Math.Sin(2 * Math.PI * nowSeconds);`

**Step 4: Run to verify green**

```bash
dotnet test tests/Goose2Client.Tests
```
Expected: all pass.

**Step 5: Commit**

```bash
git add Scripts/UI/CooldownOverlay.cs Scripts/UI/BuffEffect.cs tests/Goose2Client.Tests/CooldownOverlayGrowthTests.cs
git commit -m "feat(ui): growth mode and danger tint for cooldown overlay"
```

---

### Task 4: Client — `BuffEffect` sweep, blink, tooltip

**Worktree:** client. **Files:**
- Modify: `Scenes/UI/BuffEffect.tscn`
- Modify: `Scripts/UI/BuffEffect.cs`

**Mutation impact:**
- Source of truth: `BuffBarPacket.DurationMs` + local arrival clock (`_expiresAt`), mirroring `SpellCooldownManager.Sync`'s `UtcNow + remaining` convention.
- Readers: only the slot's own `_Process` + tooltip.
- Invariants:
  - empty slot (`_effectName == null`) → `_Process` no-ops (Godot runs `_Process` on hidden nodes; must not touch overlay/modulate).
  - `DurationMs == 0` → overlay hidden, no blink, tooltip = name only.
  - `ClearEffect` must hide the overlay and reset `_icon.Modulate` to white — `_Process` won't do it because of the empty-slot early return.
  - blink uses monotonic `Time.GetTicksMsec()` (not wall clock) so the phase never jumps.
- Observable proof: covered by hand verification in Task 5 (rendering is not unit-testable here); state logic is covered by Tasks 2–3 tests.

**Step 1: Scene**

`Scenes/UI/BuffEffect.tscn` — add the ext_resource (mirroring `HotbarSlot.tscn:39-47`, sized 20×20):

```
[ext_resource type="Script" path="res://Scripts/UI/CooldownOverlay.cs" id="2_sweep"]
```

and after the `Icon` node:

```
[node name="Sweep" type="Control" parent="."]
layout_mode = 0
mouse_filter = 2
visible = false
script = ExtResource("2_sweep")
offset_left = 0.0
offset_top = 0.0
offset_right = 20.0
offset_bottom = 20.0
```

(If Godot reorders/renames ext_resource ids when the file is opened in the editor, keep the node name `Sweep` — the code references it by name.)

**Step 2: `BuffEffect` code**

- Fields: `private CooldownOverlay _sweep; private long _durationMs; private DateTimeOffset _expiresAt;`
- `_Ready`: `_sweep = GetNode<CooldownOverlay>("Sweep");`
- `SetEffect`:
  - `_durationMs = packet.DurationMs;`
  - `_expiresAt = DateTimeOffset.UtcNow.AddMilliseconds(_durationMs);`
  - `_icon.Modulate = Colors.White;`
  - tooltip: `BuildTooltip(packet.Name, _durationMs > 0 ? CooldownOverlay.FormatCountdown(_durationMs / 1000.0) : null)` — replaces the current `durationText: null` placeholder.
- `ClearEffect`: `_durationMs = 0; _sweep.Visible = false; _icon.Modulate = Colors.White;`
- Add `_Process`:

```csharp
public override void _Process(double delta)
{
    if (_effectName == null || _durationMs <= 0)
        return;

    var remaining = (_expiresAt - DateTimeOffset.UtcNow).TotalSeconds;
    _sweep.Update(remaining, _durationMs / 1000.0, growthMode: true, dangerSeconds: 10);

    _icon.Modulate = remaining <= 15
        ? new Color(1, 1, 1, BlinkAlpha(Time.GetTicksMsec() / 1000.0))
        : Colors.White;
}
```

Note: when `remaining <= 0` the overlay stays full red (Task 3's growth-mode early-return change) until the server's remove packet lands — expected, per design.

**Step 3: Verify**

```bash
dotnet test tests/Goose2Client.Tests
```
Expected: all pass (no new unit tests; rendering verified in Task 5).

**Step 4: Commit**

```bash
git add Scenes/UI/BuffEffect.tscn Scripts/UI/BuffEffect.cs
git commit -m "feat(ui): buff bar sweep, expiry blink and duration tooltip"
```

---

### Task 5: End-to-end manual verification

Run the server from its worktree and the client (pointed at it), then with a timed buff active (e.g. a haste/speed buff with a known duration) check:

1. Sweep starts empty, sweeps clockwise from the top, fully covers the icon at expiry; direction is opposite of the hotbar cooldown pie.
2. Countdown label on the icon matches `FormatCountdown` (mm:ss at ≥ 1 min).
3. Last 15s: icon blinks (~1 Hz, alpha 0.3–1.0). Last 10s: pie is red.
4. Tooltip shows `Name\n<total duration>`.
5. Re-casting/renewing the buff resets the sweep to empty.
6. On expiry the server's remove packet clears the slot (icon disappears) within a beat of the sweep completing.
7. Permanent buff (`Duration == 0`, e.g. an item buff) shows no sweep, no blink, name-only tooltip.
8. Hotbar cooldowns unchanged (shrinking black pie, same as before).

No commit for this task; report any visual discrepancies and fix in Task 3/4 files.

---

## Invariant-to-test matrix

| Invariant | Proved by |
|---|---|
| Timed buff sends `Duration*1000/TimerFrequency` ms | `SendBuffBar_TimedBuff_SendsDurationMs` |
| Permanent buff sends `0` | `SendBuffBar_PermanentBuff_SendsZeroDuration` |
| Empty slot wire format unchanged (no trailing field) | `SendBuffBar_EmptySlot_HasNoDurationField` (adversarial) |
| Client parses 5th field | `ParsesDurationMs` |
| Old-server packet (4 fields) → `DurationMs == 0` | `OldServerPacketWithoutDuration_ParsesZero` (adversarial/back-compat) |
| Growth pie: empty when fresh, full at expiry, clamped | `ComputeProgress_MatchesMode` (adversarial: fresh buff must be 0, not 1) |
| Shrinking mode unchanged for hotbar | `ComputeProgress_MatchesMode` shrinking rows |
| Blink alpha bounded 0.3–1.0, 1 Hz period | `BlinkAlpha_OscillatesBetween03And10_WithOneSecondPeriod` |
| Sweep/blink/red render correctly | Task 5 manual checklist (not unit-testable) |
| Renew resets sweep | Task 5 item 5 (server re-sends full bar; client re-sets `_expiresAt`) |

## Deferred / accepted risks (from design)

- Clock/latency drift between server deadline and client sweep — accepted; corrected by the full-bar resend on removal.
- Buffs < 15s blink for their whole life; < 10s are red the whole life — accepted.
- New server + old client would read the duration into the buff name — non-issue (single client, lockstep deploy).
