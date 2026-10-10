# Name Display Options — Part 1: Client Implementation Plan

**Goal:** Add a 3-way "Name Display" setting (Always / Players only / Never, default Players-only) plus a hover tooltip that reveals a name when it is not drawn overhead, and honour a server `HideName` flag that suppresses the overhead label.

**Architecture:** A pure `NameDisplayRule` decides overhead vs. tooltip; `GameManager` caches the mode so the per-frame `WorldTextBridge` check is a field read; a `MapManager` hover poll (driven from `WorldViewport._Input`) drives a new mouse-anchored `NameTooltipControl`. The server flag rides as a new trailing field on MKC, read guarded by `LengthRemaining()`.

**Tech Stack:** Godot 4 / C# (.NET), xUnit headless tests (`dotnet test tests/Goose2Client.Tests`).

Design: `docs/plans/2026-10-10-name-display-options-design.md`. Part 2 (server) is separate.

---

## APIs verified

- `CharacterType` enum `Scripts/Constants.cs:128` (`Player = 1`, `Monster = 2`, `Vendor/Banker/Quest = 10/11/12`, `Pet = 13`).
- `Options` constants class `Scripts/Constants.cs:138`; last key `TargetBoxColor` at `:153`.
- `GameManager.CanSeeInvisible` cache pattern `Scripts/GameManager.cs:64`; settings load `GameManager.LoadSettings` `Scripts/GameManager.cs:414-423`.
- `Character` state inputs: `IsGM` `Scripts/Character/Character.cs:20`, `IsHiddenFromViewer` `:22`, `IsRoofOccluded` `:25`, `CharacterType` `:35`, `FullName` `:13`; MKC path `SetAppearance(MakeCharacterPacket)` `:216`, name-label line `:239`.
- Pure-rule precedent `Scripts/Character/InvisibilityRule.cs:1-12`.
- `WorldTextBridge.UpdateProjection` `Scripts/WorldTextBridge.cs:89`; hidden check `:111`, roof check `:114`; loop var `element` is `IBridgedText`, `element.AnchorOwner` is `Character.Character`.
- `BridgedNameLabel` `Scripts/Overlays/BridgedNameLabel.cs:6` (namespace `Goose2Client.Overlays`).
- `MakeCharacterPacket.Parse` `Scripts/Network/Packets/MakeCharacterPacket.cs:38`; `return packet` `:95`; layered branch ends with mount `ParseItem(...,6,p)` `:74`, non-layered ends with `IsGM` `:91`.
- `PacketParser.LengthRemaining()` `Scripts/Network/PacketParser.cs:98` (returns 0 at end).
- `BodyClassification.IsLayered` `Scripts/Character/BodyClassification.cs:5`.
- `MapManager.CharacterAt(worldPos, skipHidden)` (private) `Scripts/MapManager.cs:346`; `HandleWorldClick` `:299`.
- `WorldViewport._Input` (mouse motion, display+occlusion gate) `Scripts/WorldViewport.cs:261`; inside-display branch `:270`; exit branch `:284`; `WindowToWorld` `:231`; click dispatch `:310`.
- `TooltipManager` `Scripts/UI/TooltipManager.cs:18` (`Instance`), fields `:20-23`, `ShowTextTooltip` `:79`, `HideAll` `:87`; scene `Scenes/UI/Tooltips.tscn` (TextTooltip node `:125`), instantiated in `Scripts/UI/GameHud.cs:73`.
- `TextTooltipControl` (mouse-anchored single label) `Scripts/UI/TextTooltipControl.cs`; `MapItemTooltipControl.PositionTooltip` `Scripts/UI/MapItemTooltipControl.cs:80`.
- `GameColors.Blue` `Scripts/GameColors.cs:20`.
- `OptionsWindow` option wiring `Scripts/UI/OptionsWindow.cs:47-49` (seed+`Toggled`), persist pattern `:183-188`; scene rows `Scenes/UI/OptionsWindow.tscn` (TargetColor row `:251-270` at y 284-308; buttons `:272-290` at y 312-336; window `offset_bottom = 344` `:54`).
- Test formats: pure-rule `tests/Goose2Client.Tests/InvisibilityRuleTests.cs`; MKC parse `tests/Goose2Client.Tests/CharacterPacketInvisibleTests.cs`; layered MKC sample `tests/Goose2Client.Tests/CharacterPacketAppearanceTests.cs:13-16`.

---

## Task 1: `NameDisplayMode` enum + pure `NameDisplayRule`

**Files:**
- Create: `Scripts/Character/NameDisplayRule.cs`
- Modify: `Scripts/Constants.cs:153` (add `Options.NameDisplay` key)
- Test: `tests/Goose2Client.Tests/NameDisplayRuleTests.cs`

**Step 1: Write the failing test**

`tests/Goose2Client.Tests/NameDisplayRuleTests.cs` — mirror `InvisibilityRuleTests.cs`:

```csharp
using Goose2Client.Character;
using Xunit;

namespace Goose2Client.Tests;

public class NameDisplayRuleTests
{
    [Theory]
    [InlineData(NameDisplayMode.Always, CharacterType.Player, false, true)]
    [InlineData(NameDisplayMode.Always, CharacterType.Monster, false, true)]
    [InlineData(NameDisplayMode.Always, CharacterType.Pet, false, true)]
    [InlineData(NameDisplayMode.PlayersOnly, CharacterType.Player, false, true)]
    [InlineData(NameDisplayMode.PlayersOnly, CharacterType.Monster, false, false)]
    [InlineData(NameDisplayMode.PlayersOnly, CharacterType.Vendor, false, false)]
    [InlineData(NameDisplayMode.PlayersOnly, CharacterType.Pet, false, false)]
    [InlineData(NameDisplayMode.Never, CharacterType.Player, false, false)]
    [InlineData(NameDisplayMode.Never, CharacterType.Monster, false, false)]
    [InlineData(NameDisplayMode.Always, CharacterType.Player, true, false)]      // adversarial: server override beats Always
    [InlineData(NameDisplayMode.PlayersOnly, CharacterType.Player, true, false)]
    public void ShouldRenderNameOverhead_MatchesTruthTable(NameDisplayMode mode, CharacterType type, bool serverHidden, bool expected)
        => Assert.Equal(expected, NameDisplayRule.ShouldRenderNameOverhead(mode, type, serverHidden));

    [Theory]
    [InlineData(true, false, false, true)]    // visible, not roof, not overhead -> tooltip
    [InlineData(true, false, true, false)]    // overhead shown -> no tooltip
    [InlineData(true, true, false, false)]    // roof-occluded -> no tooltip
    [InlineData(false, false, false, false)]  // hidden from viewer -> no tooltip
    public void ShouldShowNameTooltip_MatchesTruthTable(bool visibleToViewer, bool roofOccluded, bool overheadShown, bool expected)
        => Assert.Equal(expected, NameDisplayRule.ShouldShowNameTooltip(visibleToViewer, roofOccluded, overheadShown));
}
```

**Step 2: Run to verify it fails (red)** — `dotnet test tests/Goose2Client.Tests --filter NameDisplayRuleTests` → compile error (`NameDisplayMode` / `NameDisplayRule` undefined).

**Step 3: Implement**

`Scripts/Character/NameDisplayRule.cs`:

```csharp
namespace Goose2Client.Character
{
    public enum NameDisplayMode { Always = 0, PlayersOnly = 1, Never = 2 }

    public static class NameDisplayRule
    {
        public static bool ShouldRenderNameOverhead(NameDisplayMode mode, CharacterType type, bool serverHidden)
            => !serverHidden && mode switch
            {
                NameDisplayMode.Always => true,
                NameDisplayMode.PlayersOnly => type == CharacterType.Player,
                _ => false,
            };

        public static bool ShouldShowNameTooltip(bool visibleToViewer, bool roofOccluded, bool overheadShown)
            => visibleToViewer && !roofOccluded && !overheadShown;
    }
}
```

Add to `Scripts/Constants.cs` inside `static class Options` (after `TargetBoxColor` at `:153`):

```csharp
        /// <summary>Overhead name display: see <see cref="Goose2Client.Character.NameDisplayMode"/>. Default PlayersOnly.</summary>
        public const string NameDisplay = "NameDisplay";
```

**Step 4: Run to verify it passes (green)** — `dotnet test tests/Goose2Client.Tests --filter NameDisplayRuleTests`.

**Step 5: Commit** — `git commit -am "feat(names): NameDisplayMode + name display rules"`.

---

## Task 2: `GameManager.NameDisplay` cache

**Files:**
- Modify: `Scripts/GameManager.cs:64` (add property), `:414-423` (`LoadSettings`)

**Mutation impact:**
- Source of truth changed: the persisted `Options.NameDisplay` int in `CharacterSettings`; the live mirror is the new `GameManager.NameDisplay`.
- Important readers: `Character.ShouldShowNameOverhead` (Task 3) → `WorldTextBridge` (Task 4) and the tooltip rule (Task 7); `OptionsWindow` (Task 5) writes it.
- Derived/cached state: none beyond this field. The `WorldTextBridge` re-reads `ShouldShowNameOverhead` every frame, so no explicit refresh/notify is required after a change.
- Propagation sequence: (1) `LoadSettings` sets `NameDisplay` from the persisted option at login; (2) the Options dropdown writes `Options.NameDisplay`, `Save()`s, and assigns `GameManager.NameDisplay` directly.
- Invariants: `NameDisplay` always holds a defined enum value (default `PlayersOnly`); a corrupt persisted int must not crash — normalize.
- Observable proof: Task 5 asserts the dropdown both persists and updates the live field; the rule tests (Task 1) cover the decision.

**Step 1: Implement**

Add near `CanSeeInvisible` (`Scripts/GameManager.cs:64`):

```csharp
        public Goose2Client.Character.NameDisplayMode NameDisplay { get; set; } =
            Goose2Client.Character.NameDisplayMode.PlayersOnly;
```

In `LoadSettings` (`Scripts/GameManager.cs:416`, after `CharacterSettings = new ...`):

```csharp
            int nd = CharacterSettings.GetOption<int>(Options.NameDisplay, (int)Goose2Client.Character.NameDisplayMode.PlayersOnly);
            NameDisplay = System.Enum.IsDefined(typeof(Goose2Client.Character.NameDisplayMode), nd)
                ? (Goose2Client.Character.NameDisplayMode)nd
                : Goose2Client.Character.NameDisplayMode.PlayersOnly;
```

**Step 2: Build** — `dotnet build Goose2ClientGodot.csproj` (or the tests project) succeeds.

**Step 3: Commit** — `git commit -am "feat(names): cache NameDisplayMode on GameManager at login"`.

---

## Task 3: `Character.NameHiddenByServer` + MKC parse

**Files:**
- Modify: `Scripts/Network/Packets/MakeCharacterPacket.cs` (add `HideName`, read trailing token)
- Modify: `Scripts/Character/Character.cs` (`NameHiddenByServer`, `ShouldShowNameOverhead`, set in MKC path)
- Test: `tests/Goose2Client.Tests/CharacterPacketHideNameTests.cs`

**Mutation impact:**
- Source of truth: the server's MKC trailing field → `MakeCharacterPacket.HideName` → `Character.NameHiddenByServer`.
- Important readers: `Character.ShouldShowNameOverhead` (this task) consumed by `WorldTextBridge` (Task 4) and the tooltip (Task 7).
- Derived/cached state: none; `ShouldShowNameOverhead` is computed on read.
- Propagation: `SetAppearance(MakeCharacterPacket)` sets `NameHiddenByServer`; the bridge reflects it on its next frame (no notify).
- Invariants: a packet without the trailing field → `HideName == false` (backward compatible); the value is fixed at spawn (CHP does not carry it).
- Observable proof: parse tests assert the final `HideName` value with and without the field, in both layered and monster branches; the existing `CharacterPacketInvisibleTests` / `CharacterPacketAppearanceTests` (no trailing field) must stay green.

**Step 1: Write the failing test**

`tests/Goose2Client.Tests/CharacterPacketHideNameTests.cs`:

```csharp
using Goose2Client.Network;
using Goose2Client.Network.Packets;
using Xunit;

namespace Goose2Client.Network.Packets.Tests;

public class CharacterPacketHideNameTests
{
    // Layered player MKC (reuses the appearance-test sample) with HideName=1 appended after mount.
    [Fact]
    public void LayeredMkc_TrailingHideName1_IsParsed()
    {
        var raw = "MKC42,1,Asp,T1,S1,G1,10,20,2,75,10001,10,20,30,40,4,10070,"
            + "11,100,90,80,255,12,90,80,70,255,13,80,70,60,255,14,70,60,50,255,"
            + "15,60,50,40,255,16,50,40,30,255,111,222,33,44,1,10002,123,1,10040,5,6,7,8,1";
        var p = (MakeCharacterPacket)new MakeCharacterPacket().Parse(new PacketParser(raw, "MKC"));
        Assert.True(p.HideName);
    }

    [Fact]
    public void MonsterMkc_TrailingHideName1_IsParsed()
    {
        // Known-good monster MKC from CharacterPacketInvisibleTests, with HideName=1 appended after IsGM.
        var raw = "MKC7,1,Mon,,,0,3,4,1,50,255,0,0,255,0,1,999,1,1";
        var p = (MakeCharacterPacket)new MakeCharacterPacket().Parse(new PacketParser(raw, "MKC"));
        Assert.Equal(999, p.MoveSpeed);
        Assert.True(p.IsGM);
        Assert.True(p.HideName);
    }

    // Regression: an older server sends no trailing field -> defaults false, existing fields intact.
    [Fact]
    public void Mkc_NoTrailingHideName_DefaultsFalse()
    {
        var raw = "MKC7,1,Mon,,,0,3,4,1,50,255,0,0,255,0,1,999,1";   // identical to the invisible-test string
        var p = (MakeCharacterPacket)new MakeCharacterPacket().Parse(new PacketParser(raw, "MKC"));
        Assert.False(p.HideName);
        Assert.Equal(999, p.MoveSpeed);
        Assert.True(p.IsGM);
    }
}
```

**Step 2: Run to verify it fails (red)** — `dotnet test tests/Goose2Client.Tests --filter CharacterPacketHideNameTests` → `HideName` undefined.

**Step 3: Implement**

`MakeCharacterPacket.cs`: add property `public bool HideName { get; set; }` near `IsGM` (`:34`). Before `return packet` (`:95`), after the layered/non-layered `if/else`, add a single guarded read (both branches end immediately before the trailing field — layered after mount `:74`, non-layered after `IsGM` `:91`):

```csharp
            if (p.LengthRemaining() > 0)
                packet.HideName = p.GetInt32() != 0;
```

`Character.cs`: add near `IsHiddenFromViewer` (`:22`):

```csharp
        public bool NameHiddenByServer { get; private set; }
        public bool ShouldShowNameOverhead => NameDisplayRule.ShouldRenderNameOverhead(
            GameManager.Instance?.NameDisplay ?? NameDisplayMode.PlayersOnly, CharacterType, NameHiddenByServer);
```

In `SetAppearance(MakeCharacterPacket)` set it alongside `IsInvisible` (`Character.cs:238`):

```csharp
            NameHiddenByServer = p.HideName;
```

**Step 4: Run to verify it passes (green)** — `dotnet test tests/Goose2Client.Tests --filter "CharacterPacketHideNameTests|CharacterPacketInvisibleTests|CharacterPacketAppearanceTests"` (all pass; the no-field regression proves the guard).

**Step 5: Commit** — `git commit -am "feat(names): parse MKC HideName into Character.NameHiddenByServer"`.

---

## Task 4: `WorldTextBridge` name-only gate

**Files:**
- Modify: `Scripts/WorldTextBridge.cs:114` (after the roof check)

**Step 1: Implement**

In `UpdateProjection`, after the roof-occlusion check (`WorldTextBridge.cs:114`) and before projecting, add a name-only gate. Only `BridgedNameLabel` is affected — chat bubbles and battle text keep their current behaviour:

```csharp
                if (element is Goose2Client.Overlays.BridgedNameLabel && !element.AnchorOwner.ShouldShowNameOverhead)
                { item.Visible = false; continue; }
```

`ShouldShowNameOverhead` already folds in the mode, character type, and `NameHiddenByServer`, so no other bridge change is needed. The bridge runs every frame, so a mode change (Task 2/5) is reflected with no explicit refresh.

**Step 2: Build** — `dotnet build Goose2ClientGodot.csproj`.

**Step 3: Commit** — `git commit -am "feat(names): bridge hides overhead name label when ShouldShowNameOverhead is false"`.

Manual check (deferred to integration smoke): with the dropdown on "Never", no overhead names; "Players only" shows only players; a server-hidden character shows none even on "Always".

---

## Task 5: Options window dropdown

**Files:**
- Modify: `Scenes/UI/OptionsWindow.tscn` (add row, shift buttons, grow window)
- Modify: `Scripts/UI/OptionsWindow.cs` (`_Ready` seed + handler)

**Mutation impact:**
- Source of truth: `CharacterSettings.Options[Options.NameDisplay]` (persisted); live mirror `GameManager.NameDisplay`.
- Propagation on change: write the option → `Save()` → set `GameManager.NameDisplay`. The bridge (Task 4) picks it up next frame; the tooltip (Task 7) reads it live.
- Invariant: the dropdown `Selected` index maps 1:1 to the enum value (Always=0, PlayersOnly=1, Never=2).

**Step 1: Scene** (`Scenes/UI/OptionsWindow.tscn`)

Insert after the TargetColor label (`:270`), before `ResetLayoutButton`:

```
[node name="NameDisplayLabel" type="Label" parent="Content"]
layout_mode = 2
anchors_preset = 0
offset_left = 8.0
offset_top = 312.0
offset_right = 96.0
offset_bottom = 336.0
mouse_filter = 1
text = "Name Display"
vertical_alignment = 1

[node name="NameDisplayOption" type="OptionButton" parent="Content"]
layout_mode = 2
anchors_preset = 0
offset_left = 100.0
offset_top = 312.0
offset_right = 232.0
offset_bottom = 336.0
```

Shift `ResetLayoutButton` (`:272`) and `KeyBindingsButton` (`:282`) `offset_top`/`offset_bottom` from 312/336 → 340/364. Grow the window: root `offset_bottom` `:54` 344 → 372.

**Step 2: Code** (`Scripts/UI/OptionsWindow.cs`)

Add field `private OptionButton _nameDisplay = null!;`. In `_Ready`, after the target-color wiring (`:98`), seed from the setting:

```csharp
        _nameDisplay = GetNode<OptionButton>("Content/NameDisplayOption");
        foreach (var item in new[] { "Always", "Players only", "Never" })
            _nameDisplay.AddItem(item);
        int nd = GameManager.Instance.CharacterSettings.GetOption<int>(Options.NameDisplay, (int)NameDisplayMode.PlayersOnly);
        _nameDisplay.Selected = System.Enum.IsDefined(typeof(NameDisplayMode), nd) ? nd : (int)NameDisplayMode.PlayersOnly;
        _nameDisplay.ItemSelected += OnNameDisplayChanged;
```

Add the handler (persist + live update, matching `OnMinimapChanged` `:183`):

```csharp
    private void OnNameDisplayChanged(long index)
    {
        var cs = GameManager.Instance.CharacterSettings;
        cs.Options[Options.NameDisplay] = (int)(NameDisplayMode)index;
        cs.Save();
        GameManager.Instance.NameDisplay = (NameDisplayMode)index;
    }
```

Add `using Goose2Client.Character;` (for `NameDisplayMode`) if not already present.

**Step 3: Build + manual** — `dotnet build Goose2ClientGodot.csproj`; open Options, confirm the dropdown reflects the saved value, and switching it updates overhead names immediately and survives relog.

**Step 4: Commit** — `git commit -am "feat(ui): Name Display dropdown in Options window"`.

---

## Task 6: `NameTooltipControl` + `TooltipManager`

**Files:**
- Create: `Scripts/UI/NameTooltipControl.cs`
- Create: `Scenes/UI/NameTooltipControl.tscn` (or inline node in `Tooltips.tscn`)
- Modify: `Scenes/UI/Tooltips.tscn` (add `NameTooltip` node)
- Modify: `Scripts/UI/TooltipManager.cs` (`_Ready` node, `ShowNameTooltip`/`HideNameTooltip`, expose `MapItemTooltip`)
- Test: `tests/Goose2Client.Tests/NameTooltipRuleTests.cs` (rule already covered in Task 1 — this task adds no new pure logic; see note)

**Contract:**
- Owns: a mouse-anchored label showing the hovered character's name; positions itself below the map-item tooltip when that tooltip is visible (stacking).
- Does NOT mutate: character state, the setting, or the item tooltip. It only reads the hovered `Character` and `TooltipManager.MapItemTooltip`.
- Precondition: `TooltipManager.Instance` ready; a hovered `Character` set via `ShowNameTooltip`.
- Postcondition: while shown, each frame it re-evaluates `NameDisplayRule.ShouldShowNameTooltip(!owner.IsHiddenFromViewer, owner.IsRoofOccluded, owner.ShouldShowNameOverhead)`; if false or owner invalid → hides. This makes a mode change or the character moving under/over a roof reflect without waiting for mouse motion.
- Safe in every mode: in "Always" the rule is false for overhead-shown characters, so it stays hidden.

**Step 1: Implement**

`Scripts/UI/NameTooltipControl.cs` (model on `TextTooltipControl.cs`, but owner is a `Character.Character` and it applies a color + stacking):

```csharp
using Godot;

namespace Goose2Client.UI
{
    // Fully-qualify Goose2Client.Character.Character: from this namespace the bare `Character`
    // resolves to the namespace, not the class (see BridgedNameLabel.cs:40).
    public partial class NameTooltipControl : Control
    {
        private Label _label = null!;
        private Goose2Client.Character.Character _owner = null!;

        public override void _Ready() => _label = GetNode<Label>("Label");

        public void SetCharacter(Goose2Client.Character.Character c)
        {
            _owner = c;
            if (c != null)
            {
                _label.Text = c.FullName;
                _label.AddThemeColorOverride("font_color", c.IsGM ? GameColors.Blue : new Color(1, 1, 1));
            }
        }

        public override void _Process(double delta)
        {
            if (_owner == null || !GodotObject.IsInstanceValid(_owner)
                || !Goose2Client.Character.NameDisplayRule.ShouldShowNameTooltip(
                       !_owner.IsHiddenFromViewer, _owner.IsRoofOccluded, _owner.ShouldShowNameOverhead))
            {
                Visible = false;
                return;
            }

            var pad = TooltipMetrics.TextPad(UiScaleApplier.Instance!.Factor);
            Size = _label.GetCombinedMinimumSize() + new Vector2(pad.W, pad.H);
            _label.OffsetLeft = pad.W / 2;
            _label.OffsetTop = pad.H / 2;
            _label.OffsetRight = -pad.W / 2;
            _label.OffsetBottom = -pad.H / 2;

            var mouse = GetGlobalMousePosition();
            var vp = GetViewportRect().Size;
            float x = mouse.X - Size.X;
            if (x < 0) x = mouse.X;
            float y = mouse.Y;

            var item = TooltipManager.Instance?.MapItemTooltip;
            if (item != null && item.Visible)
                y = item.GlobalPosition.Y + item.Size.Y + 2f;   // stack below the item tooltip

            if (y + Size.Y > vp.Y) y = vp.Y - Size.Y;
            GlobalPosition = new Vector2(x, y);
        }
    }
}
```

`Scenes/UI/Tooltips.tscn`: add an ext_resource for the script and a `NameTooltip` node modeled on `TextTooltip` (`:125-148`) — a `Control` with `mouse_filter = 2`, `visible = false`, `metadata/ui_scale_skip = true`, a full-rect `Background` Panel using `SubResource("sb_tooltip_bg")`, and a `Label`. Bump `load_steps`.

`Scripts/UI/TooltipManager.cs`: add field `private NameTooltipControl _nameTooltip = null!;`, `public MapItemTooltipControl MapItemTooltip => _mapItemTooltip;`, resolve `_nameTooltip = GetNode<NameTooltipControl>("NameTooltip");` in `_Ready` (`:34` area), and:

```csharp
        public void ShowNameTooltip(Character.Character c) { _nameTooltip.SetCharacter(c); _nameTooltip.Visible = c != null; }
        public void HideNameTooltip() => _nameTooltip.Visible = false;
```

Add `_nameTooltip.Visible = false;` to `HideAll` (`:87`). Add `using Goose2Client.Character;`.

**Step 2: Build** — `dotnet build Goose2ClientGodot.csproj`.

**Step 3: Commit** — `git commit -am "feat(ui): NameTooltipControl for hover name fallback"`.

---

## Task 7: Hover poll (`MapManager` + `WorldViewport`)

**Files:**
- Modify: `Scripts/MapManager.cs` (add `HandleWorldHover` / `ClearWorldHover`)
- Modify: `Scripts/WorldViewport.cs:270,284` (dispatch motion / exit to the hover poll)

**Contract:**
- `MapManager.HandleWorldHover(Vector2 worldPos)`: `var hit = CharacterAt(worldPos); TooltipManager.Instance?.ShowNameTooltip(hit);` — sets the tooltip's owner to the topmost character under the cursor (or null → hidden). The tooltip applies the show/hide rule itself (Task 6), so this method only answers "which character is hovered".
- `MapManager.ClearWorldHover()`: `TooltipManager.Instance?.HideNameTooltip();`.
- Precondition: called on the game thread from input processing; `CurrentMapManager` valid.
- Postcondition: the name tooltip's owner reflects the character under the cursor; visibility is decided by the tooltip each frame.

**Step 1: Implement `MapManager`**

Add public methods (reuse the private `CharacterAt`, `Scripts/MapManager.cs:346`):

```csharp
    public void HandleWorldHover(Vector2 worldPos)
        => Goose2Client.UI.TooltipManager.Instance?.ShowNameTooltip(CharacterAt(worldPos));

    public void ClearWorldHover()
        => Goose2Client.UI.TooltipManager.Instance?.HideNameTooltip();
```

**Step 2: Implement `WorldViewport`**

In `_Input` inside the display branch (`WorldViewport.cs:270`, after the `PushInput` block), dispatch the hover:

```csharp
                GameManager.Instance.CurrentMapManager?.HandleWorldHover(WindowToWorld(motion.Position));
```

In the exit branch (`:284`, alongside `NotifyMouseExited`):

```csharp
                GameManager.Instance.CurrentMapManager?.ClearWorldHover();
```

**Step 3: Build + integration smoke** — `dotnet build Goose2ClientGodot.csproj`, run the client:
- "Players only": hovering a monster shows its name tooltip; hovering a player shows none (already overhead).
- "Never": hovering any visible character shows its name; GM names blue.
- A server-hidden name (Part 2) tooltips on hover even in "Always".
- Hovering a character standing on an item shows both tooltips stacked (name below item).
- A character under a visible roof shows no tooltip.

**Step 4: Commit** — `git commit -am "feat(names): world hover drives name tooltip fallback"`.

---

## Invariant-to-test matrix

| Invariant | Proved by |
|-----------|-----------|
| Server `HideName` overrides any mode for overhead | `NameDisplayRuleTests.ShouldRenderNameOverhead_MatchesTruthTable` (serverHidden rows) |
| PlayersOnly shows overhead only for `CharacterType.Player` | same theory, `PlayersOnly` rows |
| Tooltip is the fallback (never when overhead shown) | `ShouldShowNameTooltip_MatchesTruthTable` |
| Tooltip suppressed under roof / when hidden-from-viewer | same theory |
| MKC without the trailing field defaults to not-hidden (old server) | `CharacterPacketHideNameTests.Mkc_NoTrailingHideName_DefaultsFalse` + existing invisible/appearance MKC tests stay green |
| MKC trailing field parsed in layered and monster branches | `LayeredMkc_TrailingHideName1_IsParsed`, `MonsterMkc_TrailingHideName1_IsParsed` |
| Live mode change reflects without relog | manual smoke (Task 5/7); bridge re-reads `ShouldShowNameOverhead` each frame (Task 4) |

## Deferred

- Server `HideName` emission + `hide_name` NPC data field — Part 2.
- Character walking out from under a *stationary* cursor keeps its tooltip until the next motion (parity with the existing item tooltip); acceptable.
