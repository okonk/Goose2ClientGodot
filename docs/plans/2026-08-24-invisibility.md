# Invisibility (Client) Implementation Plan

**Goal:** Render invisible characters per the wire protocol — hidden when the viewer can't
see them, translucent (50% alpha sprite-only) when they can, with self always translucent
when invisible; invisible-to-viewer characters excluded from spell targeting.

**Architecture:** State-driven recompute. `Character` stores `IsInvisible` (MKC/CHP),
`GameManager` stores `CanSeeInvisible` (SINVS), and `Character.ApplyInvisibility()`
recomputes the render state from both plus `IsLocalPlayer`. A pure `Evaluate` function
holds the rule and is unit-tested; node-level wiring (node `Visible`, slot `Modulate`,
bridge label suppression, spell-target exclusion) reads the evaluated state.

**Tech Stack:** C# / Godot 4 (GodotSharp), xunit headless tests (`tests/Goose2Client.Tests`).

**Design doc:** `docs/plans/2026-08-24-invisibility-design.md`

## APIs verified

- `PacketParser.GetInt32()` — `Scripts/Network/PacketParser.cs:63` (one token);
  `PacketParser.GetString()` — `:78` (one token). Both consume exactly one comma-delimited
  token, so the monster-branch type change re-aligns nothing.
- Packet-handler template: `Scripts/Network/Packets/AdminModeActivatePacket.cs` (prefix
  property + `Parse(PacketParser)`); registration: `PacketManager.Listen<T>` —
  `Scripts/Network/PacketManager.cs:13`.
- `MapManager` listener registration/removal pairs: `Scripts/MapManager.cs:71-90` /
  `:105-124`; `Characters` — `IEnumerable<Character.Character>` at `:38`;
  `OnMakeCharacter` `:138`, `OnUpdateCharacter` `:181`, `AttachLocalPlayer` `:213`.
- `Character`: `IsLocalPlayer` `:19`, `_slots` (Dictionary<CharacterSlot, Slot> of
  AnimatedSprite2D) `:30`, `SetAppearance(MakeCharacterPacket)` `:171`,
  `SetAppearance(UpdateCharacterPacket)` `:214`. `ApplySlot` / `PlayCurrent` never touch
  `Modulate`, so a Modulate set by invisibility survives animation/slot-rebuild ordering as
  long as `ApplyInvisibility()` runs **after** `ApplyAppearance`.
- `WorldTextBridge.UpdateProjection` per-element visibility loop:
  `Scripts/WorldTextBridge.cs:89-108` (insert after the viewport check at `:100`) (overwrites `item.Visible` every frame — the bridge
  must consult hidden state or labels reappear).
- `GameManager`: `SpellTargetManager` (public, `:39`), `IsTargeting` `:42`,
  `HandlePacket` `:157`.
- `SpellTargetManager`: `_target` `:9`, `Cast` `:57` (remembered-target validity block
  `:64-70`), `CycleTarget` `:88` with candidates from `mm.Characters` at `:93`.
- Test pattern: `tests/Goose2Client.Tests/AdminModeActivatePacketTests.cs`
  (`new PacketParser("AMA123,1", "AMA")`). Test project compiles all of `Scripts/**`
  (`tests/Goose2Client.Tests/Goose2Client.Tests.csproj`), runs via `dotnet test
  tests/Goose2Client.Tests`. No test in the repo instantiates a scene-tree node —
  **unit tests cover pure logic only**; node-level behavior is compile-gated and
  manually verified (server doesn't send these packets yet, see final task).
- Repo convention (`AGENTS.md`): no comments/doc strings unless the "why" is non-obvious.

---

### Task 1: SeeInvisiblePacket

**Files:**
- Create: `Scripts/Network/Packets/SeeInvisiblePacket.cs`
- Test: `tests/Goose2Client.Tests/SeeInvisiblePacketTests.cs`

**Step 1: Write the failing test**

```csharp
using Goose2Client.Network;
using Xunit;

namespace Goose2Client.Network.Packets.Tests;

public class SeeInvisiblePacketTests
{
    [Fact]
    public void Parse_Seen_CanSeeIsTrue()
    {
        var p = (SeeInvisiblePacket)new SeeInvisiblePacket().Parse(new PacketParser("SINVS1", "SINVS"));
        Assert.True(p.CanSee);
    }

    [Fact]
    public void Parse_NotSeen_CanSeeIsFalse()
    {
        var p = (SeeInvisiblePacket)new SeeInvisiblePacket().Parse(new PacketParser("SINVS0", "SINVS"));
        Assert.False(p.CanSee);
    }
}
```

**Step 2: Red** — Run: `dotnet test tests/Goose2Client.Tests --filter SeeInvisiblePacketTests`
Expected: compile failure, `SeeInvisiblePacket` doesn't exist.

**Step 3: Implement** — model on `AdminModeActivatePacket`: `public class
SeeInvisiblePacket : PacketHandler`, `Prefix => "SINVS"`, property
`public bool CanSee { get; set; }`, `Parse` sets `CanSee = p.GetInt32() == 1` (mirror the
existing `PacketHandler` shape — object returned, `override object Parse`).

**Step 4: Green** — same command, 2 passed.

**Step 5: Commit** — `git commit -m "feat: parse SINVS packet"` (add both files).

---

### Task 2: Monster MKC/CHP read Invisible as int

**Files:**
- Modify: `Scripts/Network/Packets/MakeCharacterPacket.cs` (monster `else` branch, `p.GetString(); // invisible`)
- Modify: `Scripts/Network/Packets/UpdateCharacterPacket.cs` (monster `else` branch, same line)
- Test: `tests/Goose2Client.Tests/CharacterPacketInvisibleTests.cs`

**Mutation impact:**
- Source of truth changed: the parser's interpretation of token 17 (MKC) / token 8 (CHP)
  in the monster branch.
- Important readers: today none (field parsed-then-dropped); after Task 4,
  `Character.SetAppearance`.
- Derived/cached state affected: none. `GetInt32` and `GetString` each consume exactly one
  token (`PacketParser.cs:63,:78`), so all later token reads (MoveSpeed, IsGM) keep their
  positions.
- Required propagation sequence: change the read, nothing else to propagate.
- Invariants to preserve: token count per packet unchanged; normal-character branch
  untouched.
- Observable proof: the new parse tests below assert the stored value (an old parser leaves
  `Invisible` at its default 0 — the adversarial red).

**Step 1: Write the failing tests**

Fixtures match exactly the tokens the parsers read. Monster MKC = 19 tokens:
loginid, charType, name, title, surname, guild, x, y, facing, hp, bodyid, bodyR..A,
bodystate, **invisible**, movespeed, isgm. BodyId ≥ 100 takes the monster branch.

```csharp
using Goose2Client.Network;
using Xunit;

namespace Goose2Client.Network.Packets.Tests;

public class CharacterPacketInvisibleTests
{
    [Fact]
    public void MonsterMkc_InvisibleToken_IsStoredAndTrailingFieldsAlign()
    {
        var raw = "MKC7,1,Mon,,,0,3,4,1,50,150,255,0,0,255,0,1,999,1";
        var p = (MakeCharacterPacket)new MakeCharacterPacket().Parse(new PacketParser(raw, "MKC"));
        Assert.Equal(1, p.Invisible);
        Assert.Equal(999, p.MoveSpeed);
        Assert.True(p.IsGM);
    }

    [Fact]
    public void MonsterChp_InvisibleToken_IsStoredAndTrailingFieldsAlign()
    {
        var raw = "CHP8,150,255,0,0,255,0,1,888";
        var p = (UpdateCharacterPacket)new UpdateCharacterPacket().Parse(new PacketParser(raw, "CHP"));
        Assert.Equal(1, p.Invisible);
        Assert.Equal(888, p.MoveSpeed);
    }
}
```

(Verify token counts against the parser when writing — if the raw string doesn't parse
cleanly, fix the fixture to the exact read sequence, not the parser.)

**Step 2: Red** — Run: `dotnet test tests/Goose2Client.Tests --filter CharacterPacketInvisibleTests`
Expected: `Invisible` assertions fail (old `GetString` discards the token, property stays 0).

**Step 3: Implement** — in both monster branches replace
`p.GetString(); // invisible` with `packet.Invisible = p.GetInt32();` (drop the comment —
the property name carries the meaning).

**Step 4: Green** — same command, 2 passed. Full suite: `dotnet test tests/Goose2Client.Tests`.

**Step 5: Commit** — `git commit -m "feat: monster MKC/CHP parse Invisible as int"`.

---

### Task 3: InvisibilityRule.Evaluate (pure decision)

**Files:**
- Create: `Scripts/Character/InvisibilityRule.cs`
- Test: `tests/Goose2Client.Tests/InvisibilityRuleTests.cs`

**Step 1: Write the failing test** — full truth table (adversarial rows: the
isInvisible ∧ isLocalPlayer ∧ ¬canSee row must be Translucent, and
isInvisible ∧ ¬isLocalPlayer ∧ ¬canSee must be Hidden — the two outcomes a
"forgot the self-exception" or "forgot SINVS" implementation gets wrong):

```csharp
using Goose2Client.Character;
using Xunit;

namespace Goose2Client.Tests;

public class InvisibilityRuleTests
{
    [Theory]
    [InlineData(false, false, false, InvisibilityRule.Normal)]
    [InlineData(false, false, true,  InvisibilityRule.Normal)]
    [InlineData(false, true,  false, InvisibilityRule.Normal)]
    [InlineData(false, true,  true,  InvisibilityRule.Normal)]
    [InlineData(true,  false, false, InvisibilityRule.Hidden)]
    [InlineData(true,  false, true,  InvisibilityRule.Translucent)]
    [InlineData(true,  true,  false, InvisibilityRule.Translucent)]
    [InlineData(true,  true,  true,  InvisibilityRule.Translucent)]
    public void Evaluate_MatchesTruthTable(bool isInvisible, bool canSee, bool isLocal, InvisibilityRule expected)
        => Assert.Equal(expected, InvisibilityRule.Evaluate(isInvisible, canSee, isLocal));
}
```

**Step 2: Red** — Run: `dotnet test tests/Goose2Client.Tests --filter InvisibilityRuleTests`
Expected: compile failure, type missing.

**Step 3: Implement**

```csharp
namespace Goose2Client.Character
{
    public enum InvisibilityRule
    {
        Normal, Translucent, Hidden,

        public static InvisibilityRule Evaluate(bool isInvisible, bool canSeeInvisible, bool isLocalPlayer)
            => isInvisible
                ? (isLocalPlayer || canSeeInvisible ? Translucent : Hidden)
                : Normal;
    }
}
```

**Step 4: Green** — same command, 8 passed.

**Step 5: Commit** — `git commit -m "feat: invisibility decision rule"`.

---

### Task 4: Character state + ApplyInvisibility

**Files:**
- Modify: `Scripts/Character/Character.cs` (fields ~`:19-31`, `SetAppearance` `:171`, `:214`)
- Modify: `Scripts/SpellTargetManager.cs` (new public reset method)

No unit tests (node-level; repo tests are pure-logic only). Gate: full suite + compile.

**Mutation impact:**
- Source of truth added: `Character.IsHiddenFromViewer` (evaluated hidden state).
- Important readers: `WorldTextBridge.UpdateProjection` (Task 6),
  `SpellTargetManager` (Task 7). Node `Visible` on Character — no existing reader depends
  on it being true.
- Derived/cached state affected: none — nothing caches per-character visibility.
- Required propagation sequence: `ApplyInvisibility()` must run **after**
  `ApplyAppearance` inside both `SetAppearance` overloads (slot sprites may have been
  recreated; `ApplySlot`/`PlayCurrent` never reset `Modulate`, so ordering is the only
  requirement).
- Invariants to preserve: HP/MP bar auto-hide (`ApplyBarVisibility`) toggles child
  `Visible`, not the parent — parent-hidden stays hidden; movement/vitals simulation
  untouched; `ContainsPoint` (click/hit-test) untouched.
- Observable proof: deferred to in-game smoke (final task) — node wiring has no pure
  surface to assert on in this repo's test setup.

**Steps:**

1. Add to `Character`: `public bool IsInvisible { get; private set; }`,
   `public bool IsHiddenFromViewer { get; private set; }`, and a private
   `_hiddenBeforeApply` (flip-detection for the spell-target reset).
2. `SetAppearance(MakeCharacterPacket)`: store `IsInvisible = p.Invisible != 0`; call
   `ApplyInvisibility()` as the **last** statement (after `SetVitals`).
3. `SetAppearance(UpdateCharacterPacket)`: same two changes.
4. `ApplyInvisibility()` (public — the Task 5 SINVS handler calls it):

   ```csharp
   private bool _hiddenBeforeApply;
   public void ApplyInvisibility()
   {
       var rule = InvisibilityRule.Evaluate(
           IsInvisible, GameManager.Instance?.CanSeeInvisible ?? false, IsLocalPlayer);
       bool hidden = rule == InvisibilityRule.Hidden;
       IsHiddenFromViewer = hidden;
       Visible = !hidden;
       float a = rule == InvisibilityRule.Translucent ? 0.5f : 1f;
       foreach (var s in _slots.Values) s.Sprite.Modulate = new Color(1f, 1f, 1f, a);
       if (hidden && !_hiddenBeforeApply)
           GameManager.Instance?.SpellTargetManager?.OnCharacterBecameHidden(this);
       _hiddenBeforeApply = hidden;
   }
   ```

5. `SpellTargetManager`: add `public void OnCharacterBecameHidden(Character.Character c)` —
   if `c == _target`, set `_target = GameManager.Instance.CurrentMapManager?.LocalPlayer`
   and reposition only while actively targeting: `if (IsTargeting) PositionReticle();`
   (unconditional reposition would instantiate a stray reticle in the world — it is only
   freed by `ExitTargeting`).

**Gate:** `dotnet test tests/Goose2Client.Tests` — all green, no *new* warnings (the
baseline build already emits many pre-existing nullable warnings).
**Commit:** `git commit -m "feat: character invisibility render state"`.

---

### Task 5: SINVS handling + local-player attach trigger

**Files:**
- Modify: `Scripts/GameManager.cs` (new `CanSeeInvisible` property near `:42`)
- Modify: `Scripts/MapManager.cs` (listen `:71-90`, remove `:105-124`, handler, `AttachLocalPlayer` `:213`)

No unit tests (node-level). Gate: full suite.

**Mutation impact:**
- Source of truth added: `GameManager.CanSeeInvisible` (default false).
- Important readers: `Character.ApplyInvisibility` (Task 4).
- Derived/cached state affected: none — each character's render state is recomputed, not
  cached independently.
- Required propagation sequence: SINVS handler sets the flag **first**, then calls
   `ApplyInvisibility()` on every `_characters.Values` (flag-then-loop order matters).
- Invariants to preserve: flag persists across map changes (lives on persistent
  `GameManager`, listener is per-map — server sends SINVS after map load, so the
  per-map listener is safe); map re-entry gets the next SINVS.
- Observable proof: deferred to in-game smoke (final task).

**Steps:**

1. `GameManager`: `public bool CanSeeInvisible { get; set; }` (default false).
2. `MapManager._Ready` / `_ExitTree`: `pm.Listen<SeeInvisiblePacket>(OnSeeInvisible)` /
   `pm.Remove<...>` next to the other character listeners.
3. Handler:

   ```csharp
   private void OnSeeInvisible(object packetObj)
   {
       GameManager.Instance.CanSeeInvisible = ((SeeInvisiblePacket)packetObj).CanSee;
       foreach (var c in _characters.Values) c.ApplyInvisibility();
   }
   ```

   Add a one-line wire-protocol comment (AGENTS.md-permitted): the per-map listener is
   safe only because the server sends SINVS after map load — a pre-map SINVS would be
   dropped.
4. `AttachLocalPlayer`: after `c.IsLocalPlayer = true;`, call `c.ApplyInvisibility()` —
   the "self" exception applies retroactively (attach happens after the MKC's own
   `SetAppearance`, in both the MKC path `:138` and the SYC path `:159`).

**Gate:** full suite green.
**Commit:** `git commit -m "feat: SINVS packet drives can-see-invisible state"`.

---

### Task 6: WorldTextBridge hides labels of hidden characters

**Files:**
- Modify: `Scripts/WorldTextBridge.cs` (`UpdateProjection`, `:89-108`)

No unit tests (node-level). Gate: full suite.

**Mutation impact:**
- Source of truth read: `Character.IsHiddenFromViewer` (Task 4).
- Important readers changed: bridged elements (name label, chat bubble, battle text) —
  `UpdateProjection` overwrites `item.Visible` every frame, so without this check the
  bridge would re-show labels of hidden characters immediately.
- Derived/cached state affected: none.
- Required propagation sequence: none — the projection re-reads the flag each frame;
  no event needed.
- Invariants to preserve: culling behavior unchanged for visible characters; label
  re-registration on un-hide happens automatically on the next projection frame (element
  stays registered while its anchor is alive).
- Observable proof: deferred to in-game smoke (final task).

**Step:** in the per-element loop, after the `AnchorOwner.GetViewport() !=
_worldViewport.Current` check, add:

```csharp
if (element.AnchorOwner.IsHiddenFromViewer) { item.Visible = false; continue; }
```

(`IBridgedText.AnchorOwner` is already typed `Character.Character` —
`Scripts/IBridgedText.cs`.)

**Gate:** full suite green.
**Commit:** `git commit -m "feat: bridge hides nameplates of invisible characters"`.

---

### Task 7: Spell targeting excludes hidden characters

**Files:**
- Modify: `Scripts/SpellTargetManager.cs` (`CycleTarget` `:88`, `Cast` `:57-71`)

No unit tests (needs live MapManager/Character; `TargetCycler` itself is pure but the
filter lives in the manager — unit proof deferred, see matrix). Gate: full suite.

**Mutation impact:**
- Source of truth read: `Character.IsHiddenFromViewer`.
- Important readers changed: target cycling candidate list; remembered-target validity.
- Derived/cached state affected: none.
- Required propagation sequence: mid-session "became hidden" resets are already handled
  by `OnCharacterBecameHidden` (Task 4) — `Cast`/`CycleTarget` changes only guard the
  entry paths.
- Invariants to preserve: the local player is never `IsHiddenFromViewer` (self is at worst
  translucent), so targeting can always fall back to self; click-to-move/click-to-attack
  and `ContainsPoint` deliberately unchanged.
- Observable proof: deferred to in-game smoke (final task).

**Steps:**

1. `CycleTarget`: filter the candidate source —
   `mm.Characters.Where(c => !c.IsHiddenFromViewer)` before building
   `TargetCandidate`s (`:93`).
2. `Cast` remembered-target validity block (`:64-70`): add
   `|| _target.IsHiddenFromViewer` at the **end** of the `||` chain (after the null /
   `IsInstanceValid` / `GetCharacter` checks), so a dead `_target` can't NRE.

**Gate:** full suite green.
**Commit:** `git commit -m "feat: spell targeting skips invisible-to-viewer characters"`.

---

### Task 8: Full verification

**Step 1:** `dotnet test tests/Goose2Client.Tests` — expect all tests pass
(438 baseline + 12 new).

**Step 2:** `git log --oneline` — confirm one commit per task boundary
(`feat:` prefixes).

**Step 3: In-game smoke (manual — server does not send SINVS yet).** When the server
starts sending the packets, verify in-game:
- Invisible non-local char + `SINVS0` → not drawn at all (no sprite, name, bars, bubble)
  but still blocks the tile.
- Same char + `SINVS1` → sprite at 50% alpha, name/bars fully opaque.
- Own character invisible + `SINVS0` → translucent, visible.
- `SINVS` flip 0→1 mid-scene → all characters re-evaluate immediately.
- Spell-target cycling never lands on a hidden character; confirming with a hidden
  remembered target falls back to self.

**Step 4:** final commit if any cleanup: `git commit -m "chore: invisibility follow-ups"`.

---

## Invariant-to-test matrix

| Invariant | Proved by |
|-----------|-----------|
| Hidden iff invisible ∧ ¬self ∧ ¬canSee; self always translucent when invisible | `InvisibilityRuleTests` full 8-row truth table (adversarial: the two rows a "forgot self-exception" or "forgot SINVS" implementation flips) |
| SINVS1/0 wire format | `SeeInvisiblePacketTests` |
| Monster MKC/CHP store Invisible; trailing tokens still align | `CharacterPacketInvisibleTests` (adversarial: old parser leaves `Invisible` at 0 → red) |
| Node `Visible`/`Modulate` applied per rule; labels suppressed by bridge | Deferred: no scene-tree tests in repo; Task 8 in-game smoke |
| Hidden chars excluded from spell cycling / remembered target | Deferred: needs live MapManager/Character; Task 8 in-game smoke |
| Hidden chars still block tiles | Existing `IsValidMove` occupancy check, unchanged — no test added (pre-existing behavior, no mutation) |
