# Battle Text Combine Implementation Plan

**Goal:** Client-side Options toggle that merges repeated damage/heal/XP battle-text numbers into one accumulating line per category per character, so combat-text spam stops obscuring sprites.

**Architecture:** A pure per-character state machine (`BattleTextManager<THandle>`) plus a pure parse/key rule (`BattleTextMergeRule`) decide when a `BT` packet starts an accumulator, grows one in place, or falls through to today's spawn path; thin Godot glue in `Character` executes those actions against `BattleText`/`BattleTextLine`, which gain a pinned "accumulating" mode. No server or wire changes.

**Tech Stack:** C# / .NET 10, Godot .NET 4.7.2 (`Godot.NET.Sdk/4.7.2`), xUnit.

Design doc: `docs/plans/2026-10-11-battle-text-combine-design.md`.

**Comment policy:** per `AGENTS.md`, no comments or doc strings in new/modified code **including tests**. The snippets below carry a few explanatory `//` marks for review; drop them when implementing, keeping at most the two non-obvious ones (the space-group-separator reason in the parse rule, the container-identity reason in the glue).

---

## APIs verified

- `BattleTextType` enum — `Scripts/Network/Packets/BattleTextPacket.cs:6-23` (Red1/2/4/5/61, Green7/8, Yellow60, White, Stunned10/50, Rooted11/51, Dodge20, Miss21).
- `MapManager.OnBattleText` → `Character.AddBattleText` — `Scripts/MapManager.cs:391-395`.
- `Character.AddBattleText` (container lazy-create) — `Scripts/Character/Character.cs:832-842`; `Character._Process` (unconditional) — `Character.cs:583-589`.
- `BattleText.AddText(BattleTextType, string, int, float, float)` — `Scripts/Overlays/BattleText.cs:26-56`; 18-line cap at `BattleText.cs:28`; push-up via `CurrentScreenOffsetY()`/`PlanPushUps` at `BattleText.cs:36-49`.
- `BattleTextLine` — `Scripts/Overlays/BattleTextLine.cs`: `LabelWidth=100`/`LabelHeight=16` (:7-8), `Initialize` (:15-32, sets `Lifetime` at :31, `AddChild(_label)` at :29 after `ApplyScale` at :28), `CurrentScreenOffsetY` dereferences `Lifetime` (:34-35), `ApplyScale` sets label `Size`/`Position` (:43-51), `Tick` writes `Position` (:53-58).
- `WorldOverlay._Process` no-ops when `Lifetime == null` — `Scripts/Overlays/WorldOverlay.cs:9-15` (guard at `:11`; `Tick` at `:13` is the only `Position` writer in `BattleTextLine`).
- `PacketParser` splits every field on `,` — `Scripts/Network/PacketParser.cs:13` (`Delimeter`), `GetString()` → `GetNextToken()` `:35-61,78-80`. **BT text therefore cannot contain a comma.**
- Server XP text uses a **space** group separator — `illutiagooseserver/Goose/Player.cs:1892-1893` (`NumberGroupSeparator = " "`), `:1942`. Real XP packets read `+1 234 XP`, never `+1,234 XP`.
- Server cooldown text is `Utils.FormatDuration` — `illutiagooseserver/Goose/Utils.cs:7-35`, producing `5s `, `1m 5s `, `1.5s `, `500ms` (digits immediately followed by a letter).
- `WorldTextBridge.Register` documents that `ApplyScale` runs before `AddChild` and font metrics are correct off-tree — `Scripts/WorldTextBridge.cs:38-45`.
- `WorldTextBridge.Clear()` `QueueFree`s all elements — `Scripts/WorldTextBridge.cs:53-58`, called from `Scripts/GameManager.cs:542` during `ChangeMap` while characters are still alive for ~2 frames (`WorldTextBridge.cs:108-110`).
- `Character.cs` has no `using Goose2Client.Overlays;` — `Scripts/Character/Character.cs:1-3`; overlay types are referenced qualified (`Overlays.BattleText`, `:59`, `:837`).
- `OverlayLifetime(double durationSeconds, double risePixelsPerSecond = 0)` — `Scripts/Overlays/OverlayLifetime.cs:8`.
- Bridge sweep `QueueFree`s elements whose `AnchorOwner` is invalid — `Scripts/WorldTextBridge.cs:99-105`; `DisplayScale`/`WorldScale` — `WorldTextBridge.cs:15,18`.
- Font measurement pattern — `Scripts/Overlays/ChatBubble.cs:113-124` (`font.GetStringSize(text, HorizontalAlignment.Left, -1, fontSize)`).
- `Options` key constants — `Scripts/Constants.cs:138-155`; `CharacterSettings.GetOption<T>(key, default)` — `Scripts/CharacterSettings.cs:243-248`; `Save()` — `CharacterSettings.cs:172`.
- `GameManager.NameDisplay` cached-flag pattern — `Scripts/GameManager.cs:66-67`, read in `LoadSettings` — `GameManager.cs:418-423`.
- `OptionsWindow` checkbox pattern — `Scripts/UI/OptionsWindow.cs:49-51` + handler `:147-150`; save+live pattern — `:200-205`.
- Scene rows: root 240×372 — `Scenes/UI/OptionsWindow.tscn:47-54`; NameDisplay row y=312-336 (:272-289); ResetLayout/KeyBindings buttons y=340-364 (:291-307).
- Existing tests that assert Options scene geometry — `tests/Goose2Client.Tests/KeyBindingsSceneTests.cs:98-119` (`Options_PlacesKeyBindingsBesideResetLayout`) and `tests/Goose2Client.Tests/TargetColorPickerSceneTests.cs:49-55` (`Options_ColorRowSitsAboveTheButtonRow`, asserts `offset_top = 340.0` on `ResetLayoutButton`).
- Target frameworks: game is `net8.0` / `Godot.NET.Sdk/4.7.2` (`Goose2ClientGodot.csproj:1-3`); tests are `net10.0` / `GodotSharp 4.6.2` and compile `Scripts/**/*.cs` directly (`tests/Goose2Client.Tests/Goose2Client.Tests.csproj:3,8`) — Godot-API claims in Tasks 3-4 are validated against the 4.7.x runtime by smoke test, not by the test project.
- Pure-rule test style — `tests/Goose2Client.Tests/BattleTextLayoutTests.cs`; scene-contract style — `KeyBindingsSceneTests.cs:12-33` (`RepositoryRoot`/`NodeBlock`).
- Tests: `dotnet test tests/Goose2Client.Tests` (baseline 1580 passing).

---

### Task 0: Setting plumbing

**Files:**
- Modify: `Scripts/Constants.cs:155` (end of `Options` class)
- Modify: `Scripts/GameManager.cs:66-67` (property) and `:418-423` (`LoadSettings`)

**Step 1: Add the key and cached flag**

```csharp
// Constants.cs, inside static class Options
public const string CombineBattleText = "CombineBattleText";
```

```csharp
// GameManager.cs, beside NameDisplay
public bool CombineBattleText { get; set; } = true;

// GameManager.LoadSettings, beside the NameDisplay read
CombineBattleText = CharacterSettings.GetOption<bool>(Options.CombineBattleText, true);
```

No test: a field default plus one dictionary read; the observable behavior is covered by Tasks 1-2 (pure logic) and Task 5 (UI wiring contract). Persistence needs no migration — `Options` is a free-form dictionary (`CharacterSettings.cs:54`), absent key → default `true`.

**Step 2: Commit**

```bash
git add Scripts/Constants.cs Scripts/GameManager.cs
git commit -m "feat(battle-text): CombineBattleText setting + cached flag"
```

---

### Task 1: `BattleTextMergeRule` (pure parse + key + format)

**Files:**
- Create: `Scripts/Overlays/BattleTextMergeRule.cs`
- Test: `tests/Goose2Client.Tests/BattleTextMergeRuleTests.cs`

**Step 1: Write the failing tests**

Adversarial cases matter most: grouping must key on (family, suffix) — a wrong implementation that keys on color family alone merges `+1 234 XP` with a bare yellow number, and one that keys on exact type splits `-123` (Red1) from a hypothetical Red2 damage. Malformed separators must not parse.

```csharp
using Goose2Client.Overlays;
using Xunit;
using BattleTextType = Goose2Client.Network.Packets.BattleTextType;

public class BattleTextMergeRuleTests
{
    [Theory]
    [InlineData(BattleTextType.Red1, "-123", -123L, "")]
    [InlineData(BattleTextType.Red2, "-123", -123L, "")]
    [InlineData(BattleTextType.Red61, "-1", -1L, "")]
    [InlineData(BattleTextType.Green7, "+456", 456L, "")]
    [InlineData(BattleTextType.Green8, "+456", 456L, "")]
    [InlineData(BattleTextType.Yellow60, "+1 234 XP", 1234L, "XP")]
    [InlineData(BattleTextType.Yellow60, "+1,234 XP", 1234L, "XP")]
    [InlineData(BattleTextType.Yellow60, "123", 123L, "")]
    public void TryGet_ParsesValueAndSuffix(BattleTextType type, string text, long value, string suffix)
    {
        Assert.True(BattleTextMergeRule.TryGet(type, text, out var key, out var parsed));
        Assert.Equal(value, parsed);
        Assert.Equal(suffix, key.Suffix);
    }

    [Fact]
    public void Keys_SameFamilySameSuffix_ShareAcrossTypes()
    {
        BattleTextMergeRule.TryGet(BattleTextType.Red1, "-123", out var a, out _);
        BattleTextMergeRule.TryGet(BattleTextType.Red5, "-123", out var b, out _);
        BattleTextMergeRule.TryGet(BattleTextType.Green7, "+5", out var c, out _);
        BattleTextMergeRule.TryGet(BattleTextType.Green8, "+5", out var d, out _);
        Assert.Equal(a, b);
        Assert.Equal(c, d);
        Assert.NotEqual(a, c);
    }

    [Theory]
    [InlineData(BattleTextType.Yellow60, "Level Up!")]
    [InlineData(BattleTextType.Yellow60, "Fizzle")]
    [InlineData(BattleTextType.Yellow60, "+1,2,3")]      // bad separator grouping
    [InlineData(BattleTextType.Yellow60, "+1,23")]       // short group
    [InlineData(BattleTextType.Yellow60, "12KB")]
    [InlineData(BattleTextType.Yellow60, "5s ")]
    [InlineData(BattleTextType.Yellow60, "1m 5s ")]
    [InlineData(BattleTextType.Yellow60, "1.5s ")]
    [InlineData(BattleTextType.Yellow60, "500ms")]
    [InlineData(BattleTextType.Yellow60, "")]
    [InlineData(BattleTextType.Stunned50, "")]
    [InlineData(BattleTextType.Miss21, "21")]            // keyword types never merge
    [InlineData(BattleTextType.Dodge20, "20")]
    [InlineData(BattleTextType.White, "5")]
    public void TryGet_PassThrough(BattleTextType type, string text)
        => Assert.False(BattleTextMergeRule.TryGet(type, text, out _, out _));

    [Fact]
    public void TryGet_XpAndBareYellow_GetDifferentKeys()
    {
        BattleTextMergeRule.TryGet(BattleTextType.Yellow60, "+1 234 XP", out var xp, out _);
        BattleTextMergeRule.TryGet(BattleTextType.Yellow60, "+5", out var bare, out _);
        Assert.NotEqual(xp, bare);
    }

    [Theory]
    [InlineData(-85L, "", "-85")]
    [InlineData(-1234L, "", "-1,234")]
    [InlineData(120L, "", "+120")]
    [InlineData(1234567L, "XP", "+1,234,567 XP")]
    [InlineData(0L, "", "+0")]
    public void Format_N0Signed(long total, string suffix, string expected)
    {
        var key = new BattleTextMergeKey(BattleTextFamily.Red, suffix);
        Assert.Equal(expected, BattleTextMergeRule.Format(key, total));
    }
}
```

**Step 2: Run to verify red** — `dotnet test tests/Goose2Client.Tests --filter BattleTextMergeRuleTests` → compile failure (types missing).

**Step 3: Implement**

- `public enum BattleTextFamily { Red, Green, Yellow }`
- `public readonly record struct BattleTextMergeKey(BattleTextFamily Family, string Suffix)` — usable as a `Dictionary` key.
- `public static class BattleTextMergeRule`:
  - `TryGet(BattleTextType type, string? text, out BattleTextMergeKey key, out long value)`:
    - Family map: `Red1/Red2/Red4/Red5/Red61 → Red`, `Green7/Green8 → Green`, `Yellow60 → Yellow`; every other type (White, Stunned, Rooted, Dodge, Miss) → false.
    - Parse with a manual scan (no regex needed): optional `+`/`-`; then digits where a group separator — **`,` or a single space** — must be followed by exactly three digits (first group 1-3 digits). Require at least one digit; `long.TryParse` on the separator-stripped digits. The space separator is what the server actually sends for XP (`Player.cs:1892-1893`); the comma form is accepted for robustness but can never arrive, since `,` is the packet field delimiter (`PacketParser.cs:13`).
    - A non-empty suffix must be preceded by **at least one whitespace character**. Without that rule `Utils.FormatDuration` text (`5s `, `1m 5s `, `500ms`) and `12KB` would parse as numbers with suffixes and start summing seconds.
    - The suffix is the remaining text trimmed (e.g. `"XP"`); empty when the text was purely numeric.
  - `Format(BattleTextMergeKey key, long total)` → `(total >= 0 ? "+" : "") + total.ToString("N0", CultureInfo.InvariantCulture)` + `(key.Suffix.Length > 0 ? " " + key.Suffix : "")`. `>=` so a zero heal/XP keeps the `+` shape the server always emits (`Packets.cs:380`).

**Step 4: Run to verify green** — same command, all pass.

| Invariant | Proved by |
|---|---|
| Same color family + suffix ⇒ same key (regardless of exact type) | `Keys_SameFamilySameSuffix_ShareAcrossTypes`, `TryGet_ParsesValueAndSuffix` |
| Different suffix within a family ⇒ different keys | `TryGet_XpAndBareYellow_GetDifferentKeys` |
| Keyword types never merge even with numeric text | `TryGet_PassThrough` (Miss21/Dodge20 cases) |
| Malformed numbers pass through | `TryGet_PassThrough` separator cases |
| Duration/cooldown text never becomes an accumulator | `TryGet_PassThrough` (`5s `, `1m 5s `, `500ms`) (adversarial: a whitespace-optional suffix rule fails) |

**Step 5: Commit** `git commit -am "feat(battle-text): pure merge rule for damage/heal/XP totals"`

---

### Task 2: `BattleTextManager<THandle>` (pure state machine)

**Files:**
- Create: `Scripts/Overlays/BattleTextManager.cs`
- Test: `tests/Goose2Client.Tests/BattleTextManagerTests.cs`

**Step 1: Write the failing tests**

Time is driven entirely by `Tick(delta)` (no wall clock) so tests are deterministic. `THandle` lets tests use `string` handles; the glue uses `BattleTextLine`.

```csharp
using Goose2Client.Overlays;
using Xunit;

public class BattleTextManagerTests
{
    private static readonly BattleTextMergeKey Damage = new(BattleTextFamily.Red, "");
    private static readonly BattleTextMergeKey Heal = new(BattleTextFamily.Green, "");
    private static readonly BattleTextMergeKey Xp = new(BattleTextFamily.Yellow, "XP");

    [Fact]
    public void FirstPacket_StartsAccumulator()
    {
        var m = new BattleTextManager<string>();
        var r = m.OnPacket(Damage, -12);
        Assert.Equal(BattleTextMergeAction.Start, r.Action);
        Assert.Equal("-12", r.DisplayText);
    }

    [Fact]
    public void LaterPackets_AccumulateAndFormat()
    {
        var m = new BattleTextManager<string>();
        m.OnPacket(Damage, -12);
        Assert.Equal(BattleTextMergeAction.Accumulate, m.OnPacket(Damage, -25).Action);
        Assert.Equal("-37", m.OnPacket(Damage, 0).DisplayText);
        Assert.Equal("-1,237", m.OnPacket(Damage, -1200).DisplayText);
    }

    [Fact]
    public void Keys_AreIsolated()
    {
        var m = new BattleTextManager<string>();
        m.OnPacket(Damage, -10);
        Assert.Equal(BattleTextMergeAction.Start, m.OnPacket(Heal, 5).Action);
        Assert.Equal(BattleTextMergeAction.Start, m.OnPacket(Xp, 100).Action);
        Assert.Equal("+150 XP", m.OnPacket(Xp, 50).DisplayText);
    }

    [Fact]
    public void Gap_Finalizes_AndNextPacketStartsFresh()
    {
        var m = new BattleTextManager<string>();
        m.OnPacket(Damage, -10);
        m.Attach(Damage, "line");
        Assert.Empty(m.Tick(0.9));
        var finalized = m.Tick(0.15);
        Assert.Single(finalized);
        Assert.Equal(Damage, finalized[0].Key);
        Assert.Equal("line", finalized[0].Handle);
        Assert.Equal(BattleTextMergeAction.Start, m.OnPacket(Damage, -7).Action);
    }

    [Fact]
    public void Cap_FinalizesEvenWithoutGap()
    {
        var m = new BattleTextManager<string>();
        m.OnPacket(Damage, -10);
        m.Attach(Damage, "line");
        for (int i = 1; i <= 8; i++)
        {
            m.OnPacket(Damage, -1);
            var finalized = m.Tick(0.25);
            if (finalized.Count == 0) continue;
            Assert.Equal(8, i);
            Assert.True(m.FinalizedByCap);
            return;
        }
        Assert.Fail("cap never finalized");
    }

    [Fact]
    public void Cancel_DropsRecordWithoutFinalizeSignal()
    {
        var m = new BattleTextManager<string>();
        m.OnPacket(Damage, -10);
        m.Attach(Damage, "line");
        m.Cancel(Damage);
        Assert.Empty(m.Tick(5.0));
        Assert.Equal(BattleTextMergeAction.Start, m.OnPacket(Damage, -3).Action);
    }

    [Fact]
    public void Attach_TryGetHandle_RoundTrips()
    {
        var m = new BattleTextManager<string>();
        m.OnPacket(Damage, -10);
        Assert.False(m.TryGetHandle(Damage, out _));   // not attached yet
        m.Attach(Damage, "line");
        Assert.True(m.TryGetHandle(Damage, out var h));
        Assert.Equal("line", h);
    }
}
```

**Step 2: Red** — `dotnet test tests/Goose2Client.Tests --filter BattleTextManagerTests` → compile failure.

**Step 3: Implement**

- `public enum BattleTextMergeAction { Start, Accumulate }`
- `public sealed record BattleTextMergeResult(BattleTextMergeAction Action, string DisplayText)`
- `public sealed class BattleTextManager<THandle>`:
  - Constants `GapSeconds = 1.0`, `CapSeconds = 2.0`.
  - `Dictionary<BattleTextMergeKey, Record> _open` where `Record` holds `long Total`, `double Age`, `double SinceLastHit`, `THandle Handle`.
  - `OnPacket(key, long value)` → if open: `Total += value`, `SinceLastHit = 0`, return `Accumulate(Format)`. Else: insert `Total = value`, return `Start(Format)`. Format via `BattleTextMergeRule.Format`.
  - `Attach(key, handle)` / `Cancel(key)` — record bookkeeping only; `Attach` on an unknown key is a no-op (the record was finalized or cancelled between `Start` and `Attach` — cannot happen in the glue's synchronous flow, but must not throw).
  - `TryGetHandle(key, out THandle handle)` returns **false** when there is no open record *or* the record has not been attached yet (`EqualityComparer<THandle>.Default.Equals(record.Handle, default)`), so the glue never mistakes an unattached record for a live line.
  - `bool FinalizedByCap` — true after the most recent `Tick` finalized at least one record by the cap rather than the gap (test observability only).
  - `IReadOnlyList<(BattleTextMergeKey Key, THandle Handle)> Tick(double delta)` — advance `Age`/`SinceLastHit` for all records, remove and return those with `SinceLastHit >= GapSeconds || Age >= CapSeconds`.

**Step 4: Green**, then commit `git commit -am "feat(battle-text): per-character accumulator state machine"`.

| Invariant | Proved by |
|---|---|
| Gap closes a line ~1s after the last hit | `Gap_Finalizes_AndNextPacketStartsFresh` |
| Endless hits still close at the cap | `Cap_FinalizesEvenWithoutGap` (adversarial: gap-only impl fails; asserts `FinalizedByCap` so a gap bug cannot pass it) |
| Categories never cross | `Keys_AreIsolated` |
| Cancelled record emits no finalize | `Cancel_DropsRecordWithoutFinalizeSignal` |

---

### Task 3: `BattleTextLine` accumulating mode

**Files:**
- Modify: `Scripts/Overlays/BattleTextLine.cs`

Godot-node behavior with no headless test surface in this repo (no test instantiates a node). Tasks 1-2 cover the decision logic; Task 6 is the only proof for this task.

**Step 1: Implement**

Keep the existing `Initialize` order (`ApplyScale` at `:28` before `AddChild` at `:29`) — `WorldTextBridge.cs:38-39` records that font metrics are correct off-tree, so no reordering is needed.

- `Initialize(Color, string, Vector2 baseOffset, float textScale, float worldScale, bool accumulating = false)`:
  - Assign `Lifetime = accumulating ? null : new OverlayLifetime(1.0, risePixelsPerSecond: 32)` **before** `ApplyScale`, so the null check inside `ApplyScale` (below) sees the right mode.
  - Set `Position = baseOffset * worldScale` once. Required: `Tick` (`:53-58`) is the only current writer of `Position` and does not run while `Lifetime == null` (`WorldOverlay.cs:11`).
- `CurrentScreenOffsetY()` (`:34-35`) must not dereference a null `Lifetime`: `(_baseOffset.Y - (float)(Lifetime?.RiseOffsetPixels ?? 0.0)) * _worldScale`. The `(float)` cast is required — `RiseOffsetPixels` is `double` (`OverlayLifetime.cs:12`), and without it the expression is `double` where the method returns `float` (CS0266). **Adversarial:** without the null check, opening a second accumulator (or any keyword line) while one is pinned throws inside `BattleText.AddText`'s push-up pass (`BattleText.cs:43`).
- **`PushUpScreenPixels` (`:37-41`) must re-apply the position for pinned lines:** after mutating `_baseOffset.Y`, add `if (Lifetime == null) Position = _baseOffset * _worldScale;`. **This is the plan's most important fix.** `Tick` is the only other writer of `Position` and never runs while accumulating, so without it a push-up moves `_baseOffset` but the line keeps drawing at the anchor: the new line draws on top of the pinned one, the pinned line then *reports* an offset it never visually took (so `PlanPushUps` never pushes it again), `_baseOffset` drifts upward with every subsequent spawn, and `BeginRise()` teleports the line up by that phantom distance on its first `Tick`. Affects red-vs-green accumulators and every keyword/`Level Up!` line landing near a pinned one (smoke steps 2 and 4).
- `SetAccumulatedText(string text)` — set `_label.Text`. No width handling: `Label.clip_text` defaults to `false` and the label is centered, so a long total overflows symmetrically and stays readable; resizing would also invalidate the hand-computed `BattleText.ScreenBounds` cull rect (`BattleText.cs:14-17`).
- `BeginRise()` — `Lifetime = new OverlayLifetime(1.0, risePixelsPerSecond: 32)`; from here `Tick` drives it like any normal line. `Tick` dereferences `Lifetime` unguarded at `:57`, which stays safe because `WorldOverlay._Process` returns before calling it when null.
- `ApplyScale` (`:43-51`) — after updating the label, also refresh `Position = _baseOffset * _worldScale` when `Lifetime == null`, so a UI/world-scale change (`WorldTextBridge.cs:86` calls `ApplyScale` on every element) doesn't leave a pinned line mispositioned.

**Step 2:** `dotnet test tests/Goose2Client.Tests` stays green. Note plainly: **nothing in the suite exercises `BattleTextLine` or `BattleText`** (`BattleTextLayoutTests` calls only the pure `ComputeSpreadOffset`/`PlanPushUps`/`Resolve`), so this task is verified by Task 6 smoke steps 2, 4, and 7 — not by tests. Commit `git commit -am "feat(battle-text): BattleTextLine accumulating mode"`.

**Invariant → proof (Task 3):**

| Invariant | Proved by |
|---|---|
| A pinned line never overlaps a line spawned at the anchor | smoke 2, 4 (the `PushUpScreenPixels` fix above is what makes this true) |
| A pinned line survives a scale change | smoke 7 |
| Non-accumulating lines are unchanged | existing `BattleTextLayoutTests` + smoke 1 |

---

### Task 4: `BattleText.AddText` handle + `Character` glue

**Files:**
- Modify: `Scripts/Overlays/BattleText.cs:26-56`
- Modify: `Scripts/Character/Character.cs:59, 583-589, 832-842`

**Mutation impact:**
- Source of truth changed: `BattleText` child list (`BattleText.cs:26`) gains lines whose `Lifetime` is null; `Character` gains `_battleTextManager` state (`Character.cs:59` field region).
- Important readers: `BattleText.ApplyScale` iterates children (`BattleText.cs:22-23` — unaffected, `BattleTextLine.ApplyScale` handles both modes); push-up pass reads `CurrentScreenOffsetY()` (`BattleText.cs:43` — fixed in Task 3); bridge `ApplyScale` sweep (`WorldTextBridge.cs:86`); bridge free-sweep (`WorldTextBridge.cs:99-105`) can free the container under the manager's handles.
- Derived state: manager records must mirror live lines. Propagation: every record mutation happens synchronously inside `AddBattleText`/`_Process` on the main thread — no cross-thread marshalling exists in this codebase.
- Invariants: every record either names a line in the current container or is dropped on next touch; no record outlives its character; refused spawns (18-line cap) never leave a record.
- **The dangling-handle path is `WorldTextBridge.Clear()` (`WorldTextBridge.cs:53-58`, called from `GameManager.cs:542` during `ChangeMap`), not the free-sweep at `:99-105`** — that sweep only frees elements whose `AnchorOwner` is already gone, i.e. the `Character` itself is freed and its manager died with it. `Clear()` frees the container while the character lives for ~2 more frames, and `GodotObject.IsInstanceValid` returns **true** for a `QueueFree`d-but-not-yet-freed object, so an `IsInstanceValid` check alone cannot detect it: the glue would keep rewriting a doomed label and silently drop packets until the gap closes. Detect staleness by container identity instead.
- Observable proof: Task 6 manual smoke steps 2, 4, 8, 9 (node lifecycle isn't headless-testable here); the decision logic behind each branch is covered by Tasks 1-2 unit tests.

**Step 1: `BattleText.AddText` returns the line**

```csharp
public BattleTextLine? AddText(BattleTextType type, string text, int characterHeight,
    float textScale, float worldScale, bool accumulating = false)
{
    if (GetChildCount() >= 18) return null;
    ...
    line.Initialize(color, displayText, offset, textScale, worldScale, accumulating);
    return line;
}
```

Existing callers pass nothing new (default `false`).

**Step 2: `Character` glue**

Field (next to `_battleText`, `Character.cs:59`; `Character.cs` has no `using Goose2Client.Overlays`, so qualify the manager type — line handles live inside it, not as fields):

```csharp
private Overlays.BattleTextManager<BattleTextLine>? _battleTextManager;
```

`AddBattleText` sequence (after the existing container ensure, `Character.cs:834-840`):

1. `_battleTextManager ??= new();` then, if `GameManager.Instance.CombineBattleText && Overlays.BattleTextMergeRule.TryGet(type, text, out var key, out var value)`:
   `var result = _battleTextManager.OnPacket(key, value);`
   - **Start** → `var line = _battleText.AddText(type, result.DisplayText, Height, bridge.DisplayScale, bridge.WorldScale, accumulating: true);` then `if (line != null) manager.Attach(key, line); else manager.Cancel(key);` (a conditional expression is not a statement in C#). Cap-refused packets drop, matching today's cap behavior. Return.
   - **Accumulate** → `manager.TryGetHandle(key, out var handle)`: live when `GodotObject.IsInstanceValid(handle) && handle.GetParent() == _battleText` (the container-identity check is what catches the `Clear()` case that `IsInstanceValid` cannot) → `handle.SetAccumulatedText(result.DisplayText)`. Otherwise → `manager.Cancel(key)`, re-run `OnPacket` (now returns **Start**), and follow the Start branch. This is the stale-container recovery.
2. Otherwise → `_battleText.AddText(type, text, Height, bridge.DisplayScale, bridge.WorldScale);` exactly as today (`Character.cs:841`).

`_Process` (`Character.cs:583-589`): append (`BeginRise` on a `QueueFree`d-but-not-yet-freed line is harmless — it never ticks before freeing)

```csharp
if (_battleTextManager != null)
    foreach (var (_, line) in _battleTextManager.Tick(delta))
        if (GodotObject.IsInstanceValid(line)) line.BeginRise();
```

`Character._Process` runs unconditionally while the character is in the tree, so the cap always finalizes pinned lines — including after the option is toggled off (no `FinalizeAll` wiring needed). The manager needs no teardown: it is plain state owned by the `Character` and dies with it.

**Step 3:** full test run green; commit `git commit -am "feat(battle-text): character glue routes battle text through the accumulator"`.

---

### Task 5: Options window row

**Files:**
- Modify: `Scenes/UI/OptionsWindow.tscn` (new checkbox at y=340-364; `ResetLayoutButton`/`KeyBindingsButton` shift to y=368-392; root `offset_bottom` 372→400)
- Modify: `Scripts/UI/OptionsWindow.cs` (field + seed + handler, pattern of `:49-51`/`:200-205`)
- Modify: `tests/Goose2Client.Tests/KeyBindingsSceneTests.cs:98-119` (root height + button row geometry)
- Modify: `tests/Goose2Client.Tests/TargetColorPickerSceneTests.cs:49-55` (`Options_ColorRowSitsAboveTheButtonRow` asserts `offset_top = 340.0` on `ResetLayoutButton` → `368.0`)
- Test: `tests/Goose2Client.Tests/CombineBattleTextSceneTests.cs`

**Step 1: Write the failing scene/contract tests**

```csharp
public class CombineBattleTextSceneTests
{
    // RepositoryRoot/Read/NodeBlock helpers copied from KeyBindingsSceneTests.cs:12-33.

    [Fact]
    public void Scene_HasCombineCheck_BelowNameDisplay_AboveButtons()
    {
        var s = OptionsScene();
        var check = NodeBlock(s, "CombineBattleTextCheck");
        Assert.Contains("type=\"CheckBox\"", check);
        Assert.Contains("parent=\"Content\"", check);
        Assert.Contains("offset_top = 340.0", check);
        Assert.Contains("offset_bottom = 364.0", check);
        Assert.Contains("text = \"Combine damage & heal numbers\"", check);
    }

    [Fact]
    public void Source_ReadsWritesOptionAndLiveFlag()
    {
        var src = Read("Scripts/UI/OptionsWindow.cs");
        Assert.Contains("GetNode<CheckBox>(\"Content/CombineBattleTextCheck\")", src);
        Assert.Contains("GetOption<bool>(Options.CombineBattleText, true)", src);
        Assert.Contains("cs.Options[Options.CombineBattleText] = pressed", src);
        Assert.Contains("GameManager.Instance.CombineBattleText = pressed", src);
    }
}
```

Update **both** existing geometry tests: `Options_PlacesKeyBindingsBesideResetLayout` (root `offset_bottom = 400.0`, both buttons `offset_top = 368.0` / `offset_bottom = 392.0`) and `Options_ColorRowSitsAboveTheButtonRow` (`ResetLayoutButton` `offset_top = 368.0`). `TargetColorSwatch` at y=284 is unchanged.

**Step 2: Red** — `dotnet test tests/Goose2Client.Tests --filter CombineBattleText` → node not found.

**Step 3: Implement** — scene node matching the `TargetFilteringCheck` block shape (`OptionsWindow.tscn:103-110`); wiring:

```csharp
_combineBattleText = GetNode<CheckBox>("Content/CombineBattleTextCheck");
_combineBattleText.ButtonPressed = GameManager.Instance.CharacterSettings
    .GetOption<bool>(Options.CombineBattleText, true);
_combineBattleText.Toggled += OnCombineBattleTextChanged;

private void OnCombineBattleTextChanged(bool pressed)
{
    var cs = GameManager.Instance.CharacterSettings;
    cs.Options[Options.CombineBattleText] = pressed;
    cs.Save();
    GameManager.Instance.CombineBattleText = pressed;
}
```

**Step 4: Green** — full suite green (including the updated KeyBindings geometry test). Commit `git commit -am "feat(battle-text): Combine damage & heal numbers option"`

---

### Task 6: Manual smoke

`run.sh` is gitignored (`.gitignore:52`) and absent from the worktree — copy it from the main checkout (`cp ../../run.sh .`) or invoke the editor directly (`godot-mono --path .`). Then verify:

1. Fighting a monster: one red line per target that grows in place, rising away ~1s after hits stop; no tall column.
2. Healing: independent green accumulator; red and green never overlap (second Start pushes the pinned line up).
3. Grinding: XP totals merge into `+N,NNN XP` lines.
4. `MISS`/`STUNNED` keywords still spawn their own lines and never overlap pinned accumulators.
5. Endless DoT (poison a target and watch): a fresh red line every ~2s, nothing pinned forever.
6. Toggle the option off mid-fight: pinned lines finalize within ~2s; per-hit spam resumes. Toggle on: merging resumes.
7. Change UI scale (Options → Scale) while a line is pinned: it stays centered on the character.
8. Change maps (or log out/in) while a line is pinned: no frozen number left on screen, and merging resumes normally on the new map.
9. Long totals: a merged line over 1,000,000 renders fully (labels are not clipped) and stays centered.
10. Full `dotnet test tests/Goose2Client.Tests` green.
