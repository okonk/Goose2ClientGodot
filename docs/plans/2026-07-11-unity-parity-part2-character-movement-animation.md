# Unity-Parity Bugfixes — Part 2: Character Movement & Animation

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Restore Unity-parity character behavior in the Godot port: the cast animation, tap-to-turn / staircase-diagonal movement, chained-move and teleport correctness, Title/Surname display, and overhead health-bar auto-hide.

**Architecture:** Every fix restores Unity-reference behavior (`/home/hayden/code/Goose2Client`, git HEAD). Pure logic goes in engine-free classes under `Scripts/Character/` so xUnit can cover it (repo convention); engine-touching code is verified by `dotnet build` + the manual E1 pass.

**Tech Stack:** Godot 4.6 C# (GodotSharp), xUnit (`tests/Goose2Client.Tests`), .NET SDK.

**Series:** Part 2 of 3. **Prerequisite: Part 1 is merged** (`2026-07-11-unity-parity-part1-critical-and-networking.md`) — Task 5 here uses `GameColors` from Part 1's Task 0. Part 3 depends on this part (its `Height` fix reads the `_lockedMotion` field introduced by Task 1 here).

**Commands:**
- Build: `dotnet build Goose2ClientGodot.csproj` (run from repo root; expect 0 errors)
- Tests: `dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj` (all green)

---

## APIs verified (citations into both repos)

| API / fact | Where verified |
|---|---|
| Unity cast = animator trigger `DoCast` (distinct state) | Unity `Character.cs:351-355`, `CharacterAnimation.cs:97-100` |
| Godot `Cast()` reuses the attack lock; `CharacterMotion.State` returns `"attack"` when locked; no cast motion in `AnimationNames` | `Scripts/Character/Character.cs:274-279`, `CharacterMotion.cs:5-10`, `AnimationNames.cs:34-55` |
| `cast-{up,down,left,right}` clips exist for Bodies/Hair/Chest/Eyes/Legs; **absent** for Hands (weapons) | grep of `Assets/Sprites/{Bodies/1,Hair/1,Chest/1,Eyes/1,Legs/3,Hands/180}/animations.tres` |
| The step8 plan required this exact follow-up ("extend CharacterMotion.State/AnimationNames … if cast clips exist") | `docs/plans/2026-06-07-step8-part1-correctness-and-foundations.md:354-356` |
| Unity tap-to-turn: move starts after ≥0.1 s hold; shorter tap = face-turn + `Face` send; diagonal axis alternation | Unity `PlayerController.cs:62-87, 100-122, 124-150` |
| Unity chained-move snap `transform.position = lastTarget` and facing priority Down→Right→Up→Left | Unity `Character.cs:357-380` |
| Unity teleport resets walk anim (`SetPosition` → `SetMoving(false)`) | Unity `Character.cs:382-391` |
| Godot `MoveTo/TeleportTo/_Process/ProcessLocalInput/Delta` | `Scripts/Character/Character.cs:329-420` |
| `FullName = $"{Title} {Name} {Surname}".Trim()`; used for window + overhead name | Unity `Character.cs:28, 196-217`; Unity `CharacterWindow.cs:97-98` |
| Godot drops Title/Surname (`CharacterName = p.Name`); packet parses them | `Scripts/Character/Character.cs:115-136`; `Scripts/Network/Packets/MakeCharacterPacket.cs:11-12,44-45` |
| Godot CharacterWindow shows `lp.CharacterName` | `Scripts/UI/CharacterWindow.cs:134` |
| Unity health-bar autohide: show on change; hide 2 s after HP hits 100 % (an ≠100 % value — HP or MP — cancels via `shouldHide=false`); fresh spawn seeds `UpdateHPMP(hp, 1)` | Unity `CharacterHealthBar.cs:15-72`, `Character.cs:68` |
| Godot bars always visible; Godot-palette colors; `SetVitals`/`EnsureBars` | `Scripts/Character/Character.cs:41-74` |
| `GameColors.HpGreen/HpOrange/HpRed` (Part 1 Task 0) | `Scripts/GameColors.cs` |
| `Time.GetTicksMsec()` already used for attack timing | `Scripts/Character/Character.cs:381` |
| Test framework: xUnit; GodotSharp referenced by the test project | `tests/Goose2Client.Tests/Goose2Client.Tests.csproj:8-11` |

---

### Task 1: Casting plays the cast clip, not the attack swing

The converter emitted `cast-<dir>` clips (verified for Bodies/Hair/Chest/Eyes/Legs; Hands has none), but `Cast()` reuses the attack lock and `AnimationNames` has no cast motion — exactly the gap flagged in `docs/plans/2026-06-07-step8-part1-correctness-and-foundations.md:354-356`.

**Files:**
- Modify: `Scripts/Character/AnimationNames.cs:34-55`
- Modify: `Scripts/Character/CharacterMotion.cs:5-10`
- Modify: `Scripts/Character/Character.cs` (lock fields, `Cast`, `TriggerAttack`, `AttackDuration`, `PlayCurrent`)
- Tests: `tests/Goose2Client.Tests/AnimationNamesTests.cs`, `tests/Goose2Client.Tests/CharacterMotionTests.cs`

**Step 1: Failing tests for the pure logic** (append; mirror each file's existing style):

```csharp
// AnimationNamesTests.cs
[Fact]
public void Candidates_Cast_Equipped_PrefersCastThenIdles()
{
    var c = AnimationNames.Candidates("cast", bodyState: 4, Direction.Down);
    Assert.Equal(new[] { "cast-down", "idle-equip-down", "idle-down" }, c);
}

[Fact]
public void Candidates_Cast_Unarmed_PrefersCastThenIdles()
{
    var c = AnimationNames.Candidates("cast", bodyState: 3, Direction.Left);
    Assert.Equal(new[] { "cast-left", "idle-no-equip-left", "idle-left" }, c);
}

// CharacterMotionTests.cs — update ALL existing State(...) calls to the new signature:
// old: CharacterMotion.State(isMoving, attackLocked: true, isMounted)
// new: CharacterMotion.State(isMoving, lockedMotion: "attack", isMounted)
[Fact]
public void State_CastLock_ReturnsCast()
{
    Assert.Equal("cast", CharacterMotion.State(isMoving: false, lockedMotion: "cast", isMounted: false));
}
```

**Step 2: Run to verify FAIL** (compile errors on the new signature are the expected failure mode).

**Step 3: Implement the pure parts.**

`Scripts/Character/CharacterMotion.cs` — replace `State`:

```csharp
/// <summary>lockedMotion is "attack" or "cast" while an action lock is active, else null.</summary>
public static string State(bool isMoving, string lockedMotion, bool isMounted)
{
    if (lockedMotion != null) return lockedMotion;
    if (isMounted) return isMoving ? "mounted-walk" : "mounted-idle";
    return isMoving ? "walk" : "idle";
}
```

`Scripts/Character/AnimationNames.cs` — add a case to the `bases` switch (after `"attack"`):

```csharp
// Weapon (Hands) sheets carry no cast clips — they fall through to the idle pose,
// which matches Unity (its SpellCast state had no weapon clip either).
"cast" => equipped
    ? new List<string> { "cast", "idle-equip", "idle" }
    : new List<string> { "cast", "idle-no-equip", "idle" },
```

**Step 4: Rewire `Character`** (`Scripts/Character/Character.cs`):

Replace the lock fields/methods (currently lines 273-279, 303-327, 422-431, 440):

```csharp
private string _lockedMotion;                        // "attack" | "cast" | null
protected bool AttackLocked => _lockedMotion != null;

/// <summary>Play the caster's spell-cast pose. Locked like an attack so walk/idle don't clobber it.</summary>
public void Cast() => BeginLock("cast");

public void TriggerAttack() => BeginLock("attack");

private void BeginLock(string motion)
{
    _lockedMotion = motion;
    _attackTimer = LockDuration(motion);
    PlayCurrent();
}

/// <summary>Time the lock to the Body's actual clip for this motion (weapon-type aware); fallback 0.5s.</summary>
private double LockDuration(string motion)
{
    if (_slots.TryGetValue(CharacterSlot.Body, out var body) &&
        ResolveClip(body, motion, BodyState) is { } clip &&
        body.Sprite.SpriteFrames is { } frames)
    {
        int n = frames.GetFrameCount(clip);
        float fps = (float)frames.GetAnimationSpeed(clip);
        if (fps > 0) return n / fps;
    }
    return 0.5;
}
```

- Delete the old `_attackLocked` bool and `AttackDuration()`; keep `_attackTimer`.
- `TickAttackLock` (`:422-431`): `_attackLocked = false;` → `_lockedMotion = null;`
- `PlayCurrent` (`:440`): `CharacterMotion.State(IsMoving, AttackLocked, slotMounted)` → `CharacterMotion.State(IsMoving, _lockedMotion, slotMounted)`

**Step 5:** `dotnet build Goose2ClientGodot.csproj` (0 errors) + full `dotnet test` (all green — includes the updated CharacterMotionTests).

**Step 6: Commit**

```bash
git add Scripts/Character/ tests/Goose2Client.Tests/
git commit -m "fix(character): play cast-<dir> clips on CST instead of the attack swing"
```

---

### Task 2: Tap-to-turn + held-diagonal alternation

Unity: movement starts only after ≥0.1 s of hold; a shorter tap turns in place and sends `Face` (`PlayerController.cs:62-87,124-150`); when both axes are held it alternates horizontal/vertical (`:100-122`, staircase diagonals). Godot steps instantly and can only turn by walking into a wall.

**Files:**
- Create: `Scripts/Character/MovementInput.cs`
- Modify: `Scripts/Character/Character.cs:367-411` (`ProcessLocalInput` movement block + fields)
- Test: `tests/Goose2Client.Tests/MovementInputTests.cs`

**Step 1: Failing tests for the pure resolver**

```csharp
using Goose2Client;
using Goose2Client.Character;
using Xunit;

namespace Goose2Client.Tests
{
    public class MovementInputTests
    {
        [Fact]
        public void Resolve_NoKeys_ReturnsNull()
        {
            bool wasVertical = false;
            Assert.Null(MovementInput.Resolve(false, false, false, false, ref wasVertical));
        }

        [Theory]
        [InlineData(true, false, false, false, Direction.Up)]
        [InlineData(false, true, false, false, Direction.Down)]
        [InlineData(false, false, true, false, Direction.Left)]
        [InlineData(false, false, false, true, Direction.Right)]
        public void Resolve_SingleKey_ReturnsThatDirection(bool up, bool down, bool left, bool right, Direction expected)
        {
            bool wasVertical = false;
            Assert.Equal(expected, MovementInput.Resolve(up, down, left, right, ref wasVertical));
        }

        [Fact]
        public void Resolve_HeldDiagonal_AlternatesAxes()
        {
            // Unity SetInputLastPressed: when both axes are held, the axis NOT used last wins.
            bool wasVertical = true;
            Assert.Equal(Direction.Right, MovementInput.Resolve(true, false, false, true, ref wasVertical));
            Assert.False(wasVertical);
            Assert.Equal(Direction.Up, MovementInput.Resolve(true, false, false, true, ref wasVertical));
            Assert.True(wasVertical);
        }
    }
}
```

**Step 2: Run to verify FAIL** (class missing).

**Step 3: Implement** `Scripts/Character/MovementInput.cs`:

```csharp
namespace Goose2Client.Character
{
    /// <summary>Pure port of Unity PlayerController.SetInputLastPressed + GetDirection:
    /// resolve held direction keys to one direction, alternating axes while a diagonal
    /// is held so the character walks a staircase instead of a straight line.</summary>
    public static class MovementInput
    {
        public static Direction? Resolve(bool up, bool down, bool left, bool right, ref bool wasMovingVertical)
        {
            bool horizontal = left || right;
            bool vertical = up || down;

            if (horizontal && vertical)
            {
                if (wasMovingVertical) vertical = false;
                else horizontal = false;
            }

            if (horizontal)
            {
                wasMovingVertical = false;
                return left ? Direction.Left : Direction.Right;   // Unity GetDirection: dx first
            }
            if (vertical)
            {
                wasMovingVertical = true;
                return up ? Direction.Up : Direction.Down;
            }
            return null;
        }
    }
}
```

**Step 4:** `dotnet test ... --filter MovementInputTests` → PASS.

**Step 5: Rewire `Character.ProcessLocalInput`.** Replace the fields at `Scripts/Character/Character.cs:367-368`:

```csharp
private const double MoveStartDelay = 0.1;   // Unity: hold >= 0.1s to step; shorter tap = turn in place
private double _movePressedTime;
private bool _wasMovingVertical;
private Direction? _heldDir;
```

Replace the movement block (currently lines 388-410, everything after the attack block):

```csharp
var dir = MovementInput.Resolve(
    Input.IsActionPressed("MoveUp"), Input.IsActionPressed("MoveDown"),
    Input.IsActionPressed("MoveLeft"), Input.IsActionPressed("MoveRight"),
    ref _wasMovingVertical);

if (dir == null)
{
    // Released: a tap shorter than the start delay spins in place (Unity OnMove zero-input branch).
    if (_heldDir is { } tapped && _movePressedTime < MoveStartDelay)
    {
        SetFacing(tapped);
        GameManager.Instance.NetworkClient.Face(tapped);
    }
    _heldDir = null;
    _movePressedTime = 0;
    return;
}

_heldDir = dir;
if (_moving) return;

_movePressedTime += delta;   // accumulates only while standing (Unity MoveUpdate early-outs on Moving)
if (_movePressedTime < MoveStartDelay) return;

var (dx, dy) = Delta(dir.Value);
int nx = X + dx, ny = Y + dy;
var map = GetParent()?.GetParent() as Goose2Client.MapManager;   // Characters -> Map(MapManager)
if (map != null && map.IsValidMove(nx, ny))
{
    MoveTo(nx, ny);
    GameManager.Instance.NetworkClient.Move(dir.Value);
}
else if (Facing != dir.Value)   // deviation from Unity: it re-sends FAC every frame while blocked; we don't spam
{
    SetFacing(dir.Value);
    GameManager.Instance.NetworkClient.Face(dir.Value);
}
```

Delete `MoveRepeatDelay` and `_moveCooldown` (and the `_moveCooldown -= delta;` line at `:375` and the `_moving || _moveCooldown > 0` early-out at `:388`) — Unity paces steps purely on `Moving`, and after the first 0.1 s hold the accumulated time keeps continuous movement seamless.

**Step 6:** `dotnet build` → 0 errors; full test suite green.

**Step 7: Commit**

```bash
git add Scripts/Character/ tests/Goose2Client.Tests/MovementInputTests.cs
git commit -m "feat(character): tap-to-turn and held-diagonal staircase movement (Unity parity)"
```

---

### Task 3: Movement correctness — chained-move snap, teleport anim reset, facing priority

Three small `Character` fixes, all cited from Unity `Character.cs:357-391`.

**Files:**
- Modify: `Scripts/Character/Character.cs:329-348` (`MoveTo`, `TeleportTo`)

**Step 1:** Replace `MoveTo`:

```csharp
/// <summary>Server (or local prediction) says this character stepped to (x,y).</summary>
public void MoveTo(int x, int y)
{
    // Chained MOC packets: start each step from the completed previous tile
    // (Unity Character.Move: transform.position = lastTarget) so fast movers stay on-grid.
    if (_moving) Position = _targetPosition;

    // Unity facing priority: Down, Right, Up, Left (Character.Move:364-372). Zero delta keeps facing.
    if (y > Y) Facing = Direction.Down;
    else if (x > X) Facing = Direction.Right;
    else if (y < Y) Facing = Direction.Up;
    else if (x < X) Facing = Direction.Left;

    X = x; Y = y;
    _targetPosition = Goose2Client.Map.MapCoords.TileBottomCenter(x, y);
    _moving = true;
    ApplyDrawOrder();   // facing may have changed -> reorder shield/weapon
    PlayState();
}
```

**Step 2:** In `TeleportTo`, add after `_moving = false;`:

```csharp
PlayState();   // Unity SetPosition -> SetMoving(false): drop the walk clip back to idle
```

**Step 3:** Build → 0 errors. **Step 4: Commit**

```bash
git add Scripts/Character/Character.cs
git commit -m "fix(character): snap chained moves to previous target; reset walk anim on teleport"
```

---

### Task 4: Display Title + Surname in names

`MakeCharacterPacket` parses `Title`/`Surname` but nothing consumes them; both the overhead label and the Character window show the bare name. Unity shows `FullName` in both (Unity `Character.cs:28,205`; `CharacterWindow.cs:97-98`).

**Files:**
- Create: `Scripts/Character/NameFormatting.cs`
- Modify: `Scripts/Character/Character.cs` (props at `:9-12`; `SetAppearance(MakeCharacterPacket)` at `:115-136`; `EnsureNameLabel` at `:81`)
- Modify: `Scripts/UI/CharacterWindow.cs:134`
- Test: `tests/Goose2Client.Tests/NameFormattingTests.cs`

**Step 1: Failing test**

```csharp
using Goose2Client.Character;
using Xunit;

namespace Goose2Client.Tests
{
    public class NameFormattingTests
    {
        [Theory]
        [InlineData("Sir", "Bob", "the Brave", "Sir Bob the Brave")]
        [InlineData("", "Bob", "", "Bob")]
        [InlineData(null, "Bob", null, "Bob")]
        [InlineData("", "Bob", "the Brave", "Bob the Brave")]
        [InlineData("Sir", "Bob", "", "Sir Bob")]
        public void FullName_TrimsMissingParts(string title, string name, string surname, string expected)
        {
            Assert.Equal(expected, NameFormatting.FullName(title, name, surname));
        }
    }
}
```

**Step 2: FAIL** (class missing). **Step 3: Implement:**

```csharp
namespace Goose2Client.Character
{
    /// <summary>Unity Character.FullName: $"{Title} {Name} {Surname}".Trim().</summary>
    public static class NameFormatting
    {
        public static string FullName(string title, string name, string surname)
            => $"{title} {name} {surname}".Trim();
    }
}
```

**Step 4: Wire into `Character`.** Add next to `CharacterName` (`Scripts/Character/Character.cs:10`):

```csharp
public string Title { get; private set; }
public string Surname { get; private set; }
public string FullName => NameFormatting.FullName(Title, CharacterName, Surname);
```

In `SetAppearance(MakeCharacterPacket p)` (`:118`), after `CharacterName = p.Name;`:

```csharp
Title = p.Title;
Surname = p.Surname;
```

Change both label writes: `EnsureNameLabel`'s `Text = CharacterName` (`:81`) → `Text = FullName`, and `_nameLabel.Text = CharacterName;` (`:134`) → `_nameLabel.Text = FullName;`.

**Step 5: CharacterWindow.** `Scripts/UI/CharacterWindow.cs:134`: `_nameText.Text = lp.CharacterName;` → `_nameText.Text = lp.FullName;`

**Step 6:** `dotnet test ... --filter NameFormattingTests` → PASS; build → 0 errors. **Step 7: Commit**

```bash
git add Scripts/Character/ Scripts/UI/CharacterWindow.cs tests/Goose2Client.Tests/NameFormattingTests.cs
git commit -m "fix(character): show Title/Surname in overhead label and Character window"
```

---

### Task 5: Overhead health bars auto-hide at full vitals + Unity bar colors

Unity hides the bars 2 s after HP reaches 100 % (an under-100 % value — HP **or** MP — cancels the hide), and shows them again on any change; fresh full-HP spawns therefore hide after 2 s (Unity `CharacterHealthBar.cs:15-72`, seeded by `UpdateHPMP(hp, 1)` at `Character.cs:68`). Godot bars are permanently visible and use Godot palette colors. Uses `GameColors` from Part 1.

**Files:**
- Create: `Scripts/Character/HealthBarAutoHide.cs`
- Modify: `Scripts/Character/Character.cs` (`SetVitals` at `:66-74`, `_Process` at `:350-365`, `EnsureBars` at `:49`)
- Test: `tests/Goose2Client.Tests/HealthBarAutoHideTests.cs`

**Step 1: Failing tests**

```csharp
using Goose2Client.Character;
using Xunit;

namespace Goose2Client.Tests
{
    public class HealthBarAutoHideTests
    {
        [Fact]
        public void FullVitals_HideAfterTwoSeconds()
        {
            var v = new HealthBarAutoHide();
            v.OnVitalsChanged(1f, 1f, nowSeconds: 10.0);
            Assert.True(v.Tick(11.9));
            Assert.False(v.Tick(12.0));
        }

        [Fact]
        public void DamagedVitals_StayVisible()
        {
            var v = new HealthBarAutoHide();
            v.OnVitalsChanged(0.5f, 1f, 10.0);
            Assert.True(v.Tick(100.0));
        }

        [Fact]
        public void PartialMana_CancelsHide()   // Unity SetBar sets shouldHide=false for any !=1 value
        {
            var v = new HealthBarAutoHide();
            v.OnVitalsChanged(1f, 0.4f, 10.0);
            Assert.True(v.Tick(100.0));
        }

        [Fact]
        public void ChangeAfterHide_ShowsAgain()
        {
            var v = new HealthBarAutoHide();
            v.OnVitalsChanged(1f, 1f, 10.0);
            v.Tick(13.0);
            v.OnVitalsChanged(0.9f, 1f, 14.0);
            Assert.True(v.Tick(14.0));
        }
    }
}
```

**Step 2: FAIL.** **Step 3: Implement:**

```csharp
namespace Goose2Client.Character
{
    /// <summary>Port of Unity CharacterHealthBar visibility: bars show on any vitals change
    /// and auto-hide 2s after BOTH sit at 100% (any partial value cancels the pending hide,
    /// faithful to Unity SetBar's shouldHide=false path). Pure/testable.</summary>
    public sealed class HealthBarAutoHide
    {
        public const double HideDelaySeconds = 2.0;
        private double _hideAt = double.PositiveInfinity;

        public bool Visible { get; private set; } = true;

        public void OnVitalsChanged(float hpPercent, float mpPercent, double nowSeconds)
        {
            Visible = true;
            _hideAt = (hpPercent >= 1f && mpPercent >= 1f)
                ? nowSeconds + HideDelaySeconds
                : double.PositiveInfinity;
        }

        /// <summary>Advance time; returns current visibility.</summary>
        public bool Tick(double nowSeconds)
        {
            if (nowSeconds >= _hideAt)
            {
                Visible = false;
                _hideAt = double.PositiveInfinity;
            }
            return Visible;
        }
    }
}
```

**Step 4: Wire into `Character`.** Add field near `_hpBar` (`:31`):

```csharp
private readonly HealthBarAutoHide _barVisibility = new();
```

In `SetVitals` (`:66-74`): change the HP color line to the Unity palette and register the change:

```csharp
_hpBar.Color = hpPercent > 0.66f ? GameColors.HpGreen
             : hpPercent > 0.33f ? GameColors.HpOrange
             : GameColors.HpRed;
```

and append at the end of `SetVitals`:

```csharp
_barVisibility.OnVitalsChanged(hpPercent, mpPercent, Time.GetTicksMsec() / 1000.0);
ApplyBarVisibility();
```

Add the helper and call it from `_Process` (after `TickAttackLock(delta);` at `:364`):

```csharp
private void ApplyBarVisibility()
{
    bool visible = _barVisibility.Tick(Time.GetTicksMsec() / 1000.0);
    if (_hpBar != null) _hpBar.Visible = visible;
    if (_mpBar != null) _mpBar.Visible = visible;
}
```

```csharp
ApplyBarVisibility();   // in _Process
```

Also change the initial bar color in `EnsureBars` (`:49`): `Color = Colors.Green` → `Color = GameColors.HpGreen`.

**Step 5:** `dotnet test ... --filter HealthBarAutoHideTests` → PASS; build → 0 errors. **Step 6: Commit**

```bash
git add Scripts/Character/ tests/Goose2Client.Tests/HealthBarAutoHideTests.cs
git commit -m "fix(character): auto-hide overhead bars at full vitals; Unity bar colors"
```

---

### Task 6: Part 2 verification + docs note

**Step 1:**
- `dotnet build Goose2ClientGodot.csproj` → 0 errors, 0 new warnings.
- `dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj` → all green (Part 1 totals + AnimationNames 2 + CharacterMotion 1 + MovementInput 3 + NameFormatting 5 + HealthBarAutoHide 4).

**Step 2: Update `MIGRATION_PLAN.md`.** Add "2026-07-11 Unity-parity bugfix pass — Part 2" with one line per fix (reference this plan file), and append to the manual E1 checklist:
- cast pose shows the cast clip (self-cast and remote caster); attack still swings the weapon clip
- tap-turn in place toward an open tile; held-diagonal staircase walk
- `/refresh`-style position snap drops the walk animation; fast remote movers stay on-grid
- titled character shows "Title Name Surname" overhead + in Character window
- overhead bars hide 2 s after full HP/MP, reappear on damage

**Step 3: Commit**

```bash
git add MIGRATION_PLAN.md
git commit -m "docs: record Unity-parity bugfix pass Part 2"
```
