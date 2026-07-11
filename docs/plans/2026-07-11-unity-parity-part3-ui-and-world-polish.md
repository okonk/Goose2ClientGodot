# Unity-Parity Bugfixes — Part 3: UI & World Polish

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Finish the 2026-07-11 Unity-parity pass: chat palette, the hidden vitals Level number, hotbar responsiveness, emote hotkeys, monster-body slot stripping, spell-target persistence, spell z-order, dropped-item tint, overlay anchoring, and two small map fixes.

**Architecture:** Every fix restores Unity-reference behavior (`/home/hayden/code/Goose2Client`, git HEAD). Pure logic goes in engine-free classes so xUnit can cover it; engine/scene changes are verified by `dotnet build`, the headless scene check, and the manual E1 pass.

**Tech Stack:** Godot 4.6 C# (GodotSharp), xUnit (`tests/Goose2Client.Tests`), .NET SDK.

**Series:** Part 3 of 3. **Prerequisites: Parts 1 and 2 are merged.**
- Part 1 (`...part1-critical-and-networking.md`) provides `GameColors` (Task 1 here).
- Part 2 (`...part2-character-movement-animation.md`) provides `_lockedMotion` on `Character` (Task 9's per-state `Height` reads it).

**Commands:**
- Build: `dotnet build Goose2ClientGodot.csproj` (run from repo root; expect 0 errors)
- Tests: `dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj` (all green)
- Scene check (only if a `godot` binary is on PATH — none in the authoring environment):
  `godot --headless --script tools/check_scene.gd -- res://Scenes/UI/VitalsWindow.tscn`

---

## APIs verified (citations into both repos)

| API / fact | Where verified |
|---|---|
| Unity chat type→color mapping | Unity `ChatWindow.cs:48-54` |
| Godot chat colors use `Godot.Colors` built-ins; fallback `Colors.White` | `Scripts/UI/ChatWindow.cs:69-77, 116` |
| VitalsWindow `LevelText` label exists + is populated, but is an earlier sibling than `Portrait`, whose `Mask` TextureRect draws `vitals-character-circle.png` over rect (1,1)-(54,54); LevelText rect (37,36)-(56,56) is underneath | `Scenes/UI/VitalsWindow.tscn:73-99`, `Scripts/UI/VitalsWindow.cs:28, 69-70` |
| Unity hotbar: press event arms `buttonRepeatDelayTime = 0.1f` → fires next frame; latched until release (no dropped taps) | Unity `HotbarWindow.cs:252-271` |
| Godot hotbar: 0.1 s polling only (first press delayed, short taps dropped) | `Scripts/UI/HotbarWindow.cs:325-342` |
| Unity emote ids/graphics: Heart 1080/8 … Wink 1091/10; `/refresh` = Ctrl+R | Unity `PlayerController.cs:32-55` |
| Unity emote key bindings Shift+1…9, Shift+minus, Shift+equals (Sleep AND Annoyed both Shift+6 — a Unity data bug) | `Assets/Resources/Input System/Controls.inputactions` (parsed) |
| `NetworkClient.Emote(int animationId, int graphicFile)` `:276`, `Command(string)` `:271` — zero current callers of `Emote` | `Scripts/Network/NetworkClient.cs:271-276` |
| `GameHud._UnhandledInput` action chain (chat/targeting guards at top) | `Scripts/UI/GameHud.cs:85-116` |
| Unity builds hair/face/equipment only when `BodyId < 100`, destroys them when ≥100 on update | Unity `Character.cs:70-84, 132-158` |
| Godot `ApplyAppearance` builds all 10 slots unconditionally; `ApplySlot(id<=0)` → `RemoveSlot` | `Scripts/Character/Character.cs:153-233` |
| Unity keeps `Target` across targeting sessions; re-seeds to player only when null/filter-mismatched | Unity `SpellTargetManager.cs:53-85, 115-124` |
| Godot resets `_target` to player each `Cast`; nulls it in `ExitTargeting`; `mm.GetCharacter/LocalPlayer` | `Scripts/SpellTargetManager.cs:51-65, 140-150`; `Scripts/MapManager.cs:32-35` |
| Godot spell z: `ZIndex = 20` **relative** under `Objects` (z 14) / `Character` (inside z-15 container) → effective 34/35, above Objects 2 (z 30) | `Scripts/MapManager.cs:288`, `Scripts/Character/Character.cs:501`, `Scenes/Map.tscn` (Objects z 14), `Scripts/MapManager.cs:63` (chars/objects1 z 15) |
| Unity spell effects sort on layer "Objects 1" order 100 (below "Objects 2") | Unity `Prefabs/SpellAnimation.prefab` (sorting layer/order fields) |
| Unity tint shader = alpha-weighted **lerp of RGB only** (sprite alpha untouched); Godot already ports it privately for characters | Unity `Resources/Shaders/CharacterAnimation.shader:80`; `Scripts/Character/Character.cs:235-246` |
| Godot `MapItem` tints via `Modulate` (multiply incl. alpha) | `Scripts/Map/MapItem.cs:19` |
| Godot overhead name/bars at fixed −74/−56/−53; Unity derives from body height (`bodyHeight/32`, `(bodyHeight−13)/32`) | `Scripts/Character/Character.cs:44-90`; Unity `Character.cs:214, 219-222` |
| Unity re-reads Height on every animator state change | Unity `CharacterAnimationStateBehaviour.cs:9-18` |
| Converter art is BottomCenter-pivot (emotes included); Godot `EmoteAnimation` sprite is centered with no offset; `SpellAnimation` documents the deliberate center-pivot correction | Unity `Editor/ToolsMenu.cs:446`; `Scripts/Overlays/EmoteAnimation.cs`; `Scripts/Overlays/SpellAnimation.cs:36-40` |
| Godot chat bubble anchored by its top (`_background.Position = (-w/2, 0)`) while `Character.cs:495` computes Unity's **center** formula | `Scripts/Overlays/ChatBubble.cs:105-108`, `Scripts/Character/Character.cs:489-495` |
| Unity early-returns when SetYourCharacter's login id is unknown | Unity `MapManager.cs:121-122` |
| Godot fires `OnCharacterUpdated(null)` in that case | `Scripts/MapManager.cs:148-154` |
| `MapCoords.WorldToTile` truncates toward zero (doc claims floor) | `Scripts/Map/MapCoords.cs:21-23` |
| Headless scene-check harness | `tools/check_scene.gd` |

---

### Task 1: Chat colors → game palette

`Scripts/UI/ChatWindow.cs:70-76` uses Godot built-ins (`#FFFF00`, `#FF0000`, `#0000FF`, `#008000`); Unity uses the custom palette (Unity `ChatWindow.cs:48-54` + `Colors.cs:9-15`). Pure-blue spell text is near-unreadable on the dark log.

**Files:**
- Modify: `Scripts/UI/ChatWindow.cs:70-76, 116`

**Step 1:** Replace the color table:

```csharp
// Chat type colors (Unity ChatWindow.cs:48-54, game palette from Unity Colors.cs)
_chatColors[ChatType.Chat] = GameColors.White;
_chatColors[ChatType.Guild] = GameColors.Yellow;
_chatColors[ChatType.Group] = GameColors.Yellow;
_chatColors[ChatType.Melee] = GameColors.Red;
_chatColors[ChatType.Spells] = GameColors.Blue;
_chatColors[ChatType.Tell] = GameColors.Yellow;
_chatColors[ChatType.Server] = GameColors.Green;
```

**Step 2:** `AddChatLine` fallback (`:116`): `Colors.White` → `GameColors.White`.

**Step 3:** Build → 0 errors. **Step 4: Commit**

```bash
git add Scripts/UI/ChatWindow.cs
git commit -m "fix(ui): chat lines use the game palette, not Godot built-in colors"
```

---

### Task 2: Vitals HUD — Level number hidden behind the portrait circle

`LevelText` (rect 37,36–56,56) is an earlier sibling than `Portrait`, whose `Mask` TextureRect paints the opaque `vitals-character-circle.png` over rect 1,1–54,54 — Godot draws later siblings on top, so the circle art covers the number. The label and its data feed are otherwise correct (`Scripts/UI/VitalsWindow.cs:28,69-70`).

**Files:**
- Modify: `Scenes/UI/VitalsWindow.tscn` (node order only)

**Step 1:** Move the entire `LevelText` node block (`Scenes/UI/VitalsWindow.tscn:73-81`):

```
[node name="LevelText" type="Label" parent="."]
layout_mode = 0
offset_left = 37.0
offset_top = 36.0
offset_right = 56.0
offset_bottom = 56.0
mouse_filter = 0
horizontal_alignment = 1
vertical_alignment = 1
```

from its current position (between `MpText` and `Portrait`) to the **end of the file**, after the `Helmet` node. Sibling order in a `.tscn` defines child order; `LevelText` becomes the last child of the window root and draws on top of the portrait circle. Do not change any property values, and note `GetNode<Label>("LevelText")` in `VitalsWindow.cs:28` is path-based, so no code change is needed.

**Step 2 (only if a `godot` binary is on PATH):**
Run: `godot --headless --script tools/check_scene.gd -- res://Scenes/UI/VitalsWindow.tscn`
Expected: `OK load: res://Scenes/UI/VitalsWindow.tscn`. If no binary is available, `git diff` the file and confirm the block moved verbatim.

**Step 3: Commit**

```bash
git add Scenes/UI/VitalsWindow.tscn
git commit -m "fix(ui): draw vitals Level number above the portrait circle"
```

---

### Task 3: Hotbar hotkeys — fire on the press frame, never drop a tap

Unity's press event arms the shared repeat timer so the slot fires on the next frame and latches until release (Unity `HotbarWindow.cs:252-271`). Godot samples `Input.IsActionPressed` only every 0.1 s: first press is up to 100 ms late and a shorter tap is eaten.

**Files:**
- Modify: `Scripts/UI/HotbarWindow.cs:325-342` (`_Process`)

**Step 1:** Replace `_Process`:

```csharp
public override void _Process(double delta)
{
    _repeatTimer += (float)delta;
    bool repeatTick = _repeatTimer >= 0.1f;
    if (repeatTick) _repeatTimer = 0;

    // Guard against typing in input fields
    if (GetViewport().GuiGetFocusOwner() is LineEdit) return;
    // Spell targeting captures input — don't fire hotkeys while choosing a target.
    if (GameManager.Instance.IsTargeting) return;

    for (int i = 0; i < SlotsPerPage; i++)
    {
        string action = i == 9 ? "Hotkey0" : $"Hotkey{i + 1}";
        // exactMatch: Shift+1 must NOT count as Hotkey1 — Shift+digit is the emote layer (Task 4).
        if (Input.IsActionJustPressed(action, exactMatch: true))
        {
            UseSlot(i);          // fire on the press frame (Unity fires on the frame after the press event)
            _repeatTimer = 0;    // restart the shared 0.1s repeat window
        }
        else if (repeatTick && Input.IsActionPressed(action, exactMatch: true))
        {
            UseSlot(i);          // held key repeats every 0.1s, same cadence as Unity
        }
    }
}
```

**Step 2:** Build → 0 errors. **Step 3: Commit**

```bash
git add Scripts/UI/HotbarWindow.cs
git commit -m "fix(ui): hotbar hotkeys fire on press frame; short taps no longer dropped"
```

---

### Task 4: Emote hotkeys and Ctrl+R `/refresh`

`NetworkClient.Emote` (`Scripts/Network/NetworkClient.cs:276`) has zero callers. Unity binds 12 emotes to Shift+digit/minus/equals and `/refresh` to Ctrl+R (Unity `PlayerController.cs:32-55` + `Controls.inputactions`). Land Task 3 first (its `exactMatch` guard keeps Shift+digit out of the hotbar).

**Files:**
- Modify: `project.godot` (`[input]` section)
- Modify: `Scripts/UI/GameHud.cs:85-116` (`_UnhandledInput`)

**Step 1: Add input actions.** In `project.godot`'s `[input]` section, append one entry per action, using the exact single-event format already used by `Attack=` (`"deadzone": 0.5`, one `Object(InputEventKey, ...)` per event, all fields present). Set `"shift_pressed":true` and the listed `physical_keycode` (`"ctrl_pressed":true` for RefreshPosition):

| Action | physical_keycode | Modifier | Emote call (Unity PlayerController.cs:32-43) |
|---|---|---|---|
| EmoteHeart | 49 (`1`) | shift | `Emote(1080, 8)` |
| EmoteQuestion | 50 (`2`) | shift | `Emote(1081, 8)` |
| EmoteDots | 51 (`3`) | shift | `Emote(1083, 8)` |
| EmotePoop | 52 (`4`) | shift | `Emote(1084, 9)` |
| EmoteSurprised | 53 (`5`) | shift | `Emote(1085, 9)` |
| EmoteSleep | 54 (`6`) | shift | `Emote(1086, 9)` |
| EmoteAnnoyed | 48 (`0`) | shift | `Emote(1087, 9)` — **deviation:** Unity binds this to Shift+6 too (a data bug that double-fires); Shift+0 is free |
| EmoteSweat | 55 (`7`) | shift | `Emote(1088, 10)` |
| EmoteMusic | 56 (`8`) | shift | `Emote(1089, 10)` |
| EmoteWink | 57 (`9`) | shift | `Emote(1091, 10)` |
| EmoteTrash | 45 (`-`) | shift | `Emote(1082, 8)` |
| EmoteDollar | 61 (`=`) | shift | `Emote(1090, 10)` |
| RefreshPosition | 82 (`R`) | ctrl | `Command("/refresh")` |

Template (copy per row, substituting name/keycode/modifier flags):

```
EmoteHeart={
"deadzone": 0.5,
"events": [Object(InputEventKey,"resource_local_to_scene":false,"resource_name":"","device":0,"window_id":0,"alt_pressed":false,"shift_pressed":true,"ctrl_pressed":false,"meta_pressed":false,"pressed":false,"keycode":0,"physical_keycode":49,"key_label":0,"unicode":0,"location":0,"echo":false,"script":null)
]
}
```

**Step 2: Route in GameHud.** Append to the `_UnhandledInput` else-if chain (`Scripts/UI/GameHud.cs:114-115`, after `ReplyCommand`):

```csharp
else if (@event.IsActionPressed("EmoteHeart")) SendEmote(1080, 8);
else if (@event.IsActionPressed("EmoteQuestion")) SendEmote(1081, 8);
else if (@event.IsActionPressed("EmoteDots")) SendEmote(1083, 8);
else if (@event.IsActionPressed("EmotePoop")) SendEmote(1084, 9);
else if (@event.IsActionPressed("EmoteSurprised")) SendEmote(1085, 9);
else if (@event.IsActionPressed("EmoteSleep")) SendEmote(1086, 9);
else if (@event.IsActionPressed("EmoteAnnoyed")) SendEmote(1087, 9);
else if (@event.IsActionPressed("EmoteSweat")) SendEmote(1088, 10);
else if (@event.IsActionPressed("EmoteMusic")) SendEmote(1089, 10);
else if (@event.IsActionPressed("EmoteWink")) SendEmote(1091, 10);
else if (@event.IsActionPressed("EmoteTrash")) SendEmote(1082, 8);
else if (@event.IsActionPressed("EmoteDollar")) SendEmote(1090, 10);
else if (@event.IsActionPressed("RefreshPosition"))
    GameManager.Instance.NetworkClient.Command("/refresh");
```

and the helper:

```csharp
// Animation/graphic id pairs from Unity PlayerController.cs:32-43.
private static void SendEmote(int animationId, int graphicFile)
    => GameManager.Instance.NetworkClient.Emote(animationId, graphicFile);
```

(The chat/targeting guards at the top of `_UnhandledInput` already suppress emotes while typing or targeting, matching Unity's input-map switching.)

**Step 3:** Build → 0 errors. **Step 4: Commit**

```bash
git add project.godot Scripts/UI/GameHud.cs
git commit -m "feat(input): emote hotkeys (Shift+digit) and Ctrl+R /refresh (Unity parity)"
```

---

### Task 5: Monster bodies (id ≥ 100) must not render hair/face/equipment

Unity only creates those slots for `BodyId < 100` and destroys them when a body becomes ≥ 100 (Unity `Character.cs:70-84, 147-158`). Godot builds all 10 slots for any body, so a morph with stale slot ids renders equipment on the monster.

**Files:**
- Modify: `Scripts/Character/Character.cs:153-186` (`ApplyAppearance`)

**Step 1:** In `ApplyAppearance`, insert after the `Equip(...)`/`IsMounted` block (`:157-164`) and **before** the underwear defaults:

```csharp
// Unity renders only the Body slot for monster/morph bodies (>= 100): create-path
// Character.cs:70-84 skips the rest, update-path :147-158 destroys them. Zeroed ids
// flow into ApplySlot below, which RemoveSlot()s each — covering the CHP update case.
if (bodyId >= 100)
{
    hairId = 0; faceId = 0; chestId = 0; helmId = 0; legsId = 0;
    feetId = 0; shieldId = 0; weaponId = 0; mountId = 0;
    IsMounted = false;
}
```

Note `faceId` is a parameter — reassigning it is fine. Underwear defaults stay after the gate (they only apply to bodies 1/11 anyway, but the order keeps intent obvious).

**Step 2:** Build → 0 errors. **Step 3: Commit**

```bash
git add Scripts/Character/Character.cs
git commit -m "fix(character): strip hair/face/equipment slots for monster bodies (id >= 100)"
```

---

### Task 6: Spell targeting remembers the last confirmed target

Unity keeps `Target` between casts (`Cast` → `SetTarget(Target)`, `RemoveTarget` doesn't null it) and falls back to the player only when it's missing or fails the filter (Unity `SpellTargetManager.cs:53-85,115-124`). Godot re-seeds to the local player on every cast.

**Files:**
- Modify: `Scripts/SpellTargetManager.cs:51-65, 140-150`

**Step 1:** Replace `Cast` and add the filter helper:

```csharp
/// <summary>Begin targeting for the given spell.</summary>
public void Cast(SpellInfo info)
{
    _pendingSpell = info;
    IsTargeting = true;

    // Drop focus from whatever button/slot launched the cast so it can't grab navigation keys.
    GetViewport().GuiReleaseFocus();

    var mm = GameManager.Instance.CurrentMapManager;
    if (mm == null) { ExitTargeting(); return; }

    // Unity keeps the last confirmed target between casts (SetTarget(Target)); reseed to the
    // local player only when it's gone (freed/erased/map change) or fails the type filter.
    if (_target == null
        || !GodotObject.IsInstanceValid(_target)
        || mm.GetCharacter(_target.LoginId) != _target
        || FilterRejects(_target))
    {
        _target = mm.LocalPlayer;
    }
    PositionReticle();
}

/// <summary>Unity SetTarget's filter-mismatch test (SpellTargetManager.cs:73-78).</summary>
private bool FilterRejects(Character.Character target)
{
    var filteringEnabled = GameManager.Instance.CharacterSettings.GetOption<bool>(Options.TargetFiltering, true);
    if (!filteringEnabled) return false;
    return (_pendingSpell.TargetType != SpellTargetType.Player && target.CharacterType == CharacterType.Player)
        || (_pendingSpell.TargetType == SpellTargetType.Player && target.CharacterType != CharacterType.Player);
}
```

**Step 2:** In `ExitTargeting` (`:140-150`), delete the `_target = null;` line (keep `IsTargeting = false`, `_pendingSpell = null`, and the reticle teardown). The stale-reference risk is covered by the validity checks in `Cast`.

**Step 3:** Build → 0 errors. **Step 4: Commit**

```bash
git add Scripts/SpellTargetManager.cs
git commit -m "fix(targeting): remember last confirmed target across casts (Unity parity)"
```

---

### Task 7: Spell effects draw below "Objects 2" (absolute z 20)

Unity spell effects sort on layer "Objects 1" order 100 — above characters, **below** the "Objects 2" band. Godot's `ZIndex = 20` is *relative*: under `Objects` (z 14) it lands at 34, and as a `Character` child (inside the z-15 band) at 35 — both above Objects 2 (z 30), contradicting the code's own intent comment.

**Files:**
- Modify: `Scripts/MapManager.cs:288` (`OnSpellTile`)
- Modify: `Scripts/Character/Character.cs:501` (`ShowSpell`)

**Step 1:** `OnSpellTile`:

```csharp
// Absolute z 20: above characters/Objects1 (15), below Objects2 (30) — Unity puts spell
// effects on sorting layer "Objects 1" order 100. Relative z would land at 14+20=34.
var s = new Goose2Client.Overlays.SpellAnimation { Name = $"SpellTile({p.AnimationId})", ZIndex = 20, ZAsRelative = false };
```

**Step 2:** `ShowSpell` — same change:

```csharp
var s = new Overlays.SpellAnimation { Name = "Spell", ZIndex = 20, ZAsRelative = false };
```

Leave chat bubbles, emotes, battle text, and name labels relative (they intentionally render above the world, like Unity's Names/BattleText layers).

**Step 3:** Build → 0 errors. **Step 4: Commit**

```bash
git add Scripts/MapManager.cs Scripts/Character/Character.cs
git commit -m "fix(overlays): spell effects use absolute z 20 so Objects 2 draws over them"
```

---

### Task 8: Dropped-item tint uses the lerp shader, not Modulate

`Modulate` multiplies RGBA (stripping channels and making the sprite translucent); Unity's shader lerps RGB toward the tint by tint.a and leaves sprite alpha alone (Unity `CharacterAnimation.shader:80`). The correct shader already exists privately in `Character` — extract and share it.

**Files:**
- Create: `Scripts/TintMaterial.cs`
- Modify: `Scripts/Character/Character.cs:221-246` (use shared shader; delete private copy)
- Modify: `Scripts/Map/MapItem.cs:5-6, 19`

**Step 1:** Create `Scripts/TintMaterial.cs` (code moved verbatim from `Character.cs:237-246`):

```csharp
using Godot;

namespace Goose2Client
{
    /// <summary>Faithful port of Unity Custom/CharacterAnimation _Tint (shader line 80):
    /// tint.a lerps the texture rgb toward the tint rgb; final opacity is always the
    /// texture's own alpha, so a tint never fades the sprite.</summary>
    public static class TintMaterial
    {
        private static Shader _shader;
        public static Shader Shader => _shader ??= new Shader
        {
            Code = @"shader_type canvas_item;
uniform vec4 tint : source_color = vec4(0.0);
void fragment() {
    vec4 tex = texture(TEXTURE, UV);
    COLOR = vec4(mix(tex.rgb, tint.rgb, tint.a), tex.a) * COLOR;
}"
        };

        public static ShaderMaterial Make(Color tint)
        {
            var mat = new ShaderMaterial { Shader = Shader };
            mat.SetShaderParameter("tint", tint);
            return mat;
        }
    }
}
```

**Step 2:** In `Character.cs`, delete the private `_tintShader`/`TintShader` members (`:235-246`) and change `ApplySlot`'s tint branch (`:225-226`) to:

```csharp
if (s.Sprite.Material is not ShaderMaterial mat)
    s.Sprite.Material = mat = new ShaderMaterial { Shader = TintMaterial.Shader };
mat.SetShaderParameter("tint", tint);
```

(Keep the existing material-reuse structure; only the shader source moves.)

**Step 3:** In `MapItem.Setup` (`Scripts/Map/MapItem.cs:19`):

```csharp
if (tint.A > 0) Material = TintMaterial.Make(tint);   // Unity lerp-tint shader, NOT Modulate
```

and update the class doc comment (`:5-6`) to say "tint via the shared TintMaterial lerp shader (port of Unity's material `_Tint`)".

**Step 4:** Build → 0 errors; full test suite green. **Step 5: Commit**

```bash
git add Scripts/TintMaterial.cs Scripts/Character/Character.cs Scripts/Map/MapItem.cs
git commit -m "fix(map): dropped-item tint uses the Unity lerp shader instead of Modulate"
```

---

### Task 9: Overlay anchors — body-height-derived labels/bars, per-state Height, emote pivot, bubble center

Four related anchoring fixes. **Requires Part 2 Task 1** (`_lockedMotion`).

**Files:**
- Modify: `Scripts/Character/Character.cs` (`Height` at `:255-258`; `EnsureBars`/`EnsureNameLabel` at `:41-91`; both `SetAppearance` overloads)
- Modify: `Scripts/Overlays/EmoteAnimation.cs` (`Setup`)
- Modify: `Scripts/Overlays/ChatBubble.cs:105-108`

**Step 1: Per-state `Height`.** Unity re-reads the height on every animator state change (`CharacterAnimationStateBehaviour.cs`), so a mounted body reports 80, not the 48 idle height. Replace the property (`:255-258`):

```csharp
public int Height =>
    _slots.TryGetValue(CharacterSlot.Body, out var b)
        ? _heights.GetHeight($"Body-{b.GraphicId}-{ResolveClip(b, CharacterMotion.State(IsMoving, _lockedMotion, IsMounted), BodyState) ?? "idle-down"}")
        : 0;
```

**Step 2: Height-derived name/bar anchors.** Unity: name pivot at `bodyHeight/32`, bars at `(bodyHeight-13)/32` (Unity `Character.cs:214,219-222`). The Godot constants (−74/−56/−53) were tuned for the standard 48 px body; derive them so tall monsters don't wear their name inside the sprite — the formulas below reproduce the current values exactly at H = 48. Add:

```csharp
/// <summary>Re-anchor the name label and HP/MP bars to the current body height
/// (Unity derives both from bodyHeight; fixed offsets bury them inside tall bodies).
/// At the standard 48px body this yields the original -56/-53/-74 positions.</summary>
private void RepositionOverlays()
{
    int h = Height <= 0 ? 48 : Height;
    if (_hpBar != null) _hpBar.Position = new Vector2(-16, -(h + 8));
    if (_mpBar != null) _mpBar.Position = new Vector2(-16, -(h + 5));
    if (_nameLabel != null) _nameLabel.Position = new Vector2(-50, -(h + 26));
}
```

Call `RepositionOverlays();`:
- at the end of `EnsureBars` and `EnsureNameLabel` (the hardcoded initializer positions become the pre-reposition defaults);
- at the end of **both** `SetAppearance` overloads (after `PlayState();`) so a body change (CHP morph, mount) re-anchors.

**Step 3: Emote bottom pivot.** All converter art is BottomCenter-pivot (Unity `ToolsMenu.cs:446`); Godot's centered `AnimatedSprite2D` draws the emote half its height too low. In `EmoteAnimation.Setup` (`Scripts/Overlays/EmoteAnimation.cs`), after `_sprite.Play(clip);`:

```csharp
// Anchor by the sprite's BOTTOM edge like Unity's BottomCenter pivot (ToolsMenu.cs:446):
// AnimatedSprite2D is centered, so lift by half the frame height. SpellAnimation deliberately
// stays centered (see its comment) — emotes are the odd one out.
var firstFrame = frames.GetFrameTexture(clip, 0);
if (firstFrame != null)
    _sprite.Offset = new Vector2(0, -firstFrame.GetHeight() / 2f);
```

**Step 4: Chat bubble center anchor.** `Character.cs:489-495` computes Unity's **center** formula, but the bubble extends downward from the node origin. Change `Scripts/Overlays/ChatBubble.cs:105-108`:

```csharp
// Anchor the node origin at the bubble CENTER, matching the Unity prefab's centered
// pivots — Character.ShowChatBubble positions this node with Unity's center formula.
_background.Position = new Vector2(-bgSize.X / 2f, -bgSize.Y / 2f);
```

(The label is a child of `_background` and follows automatically; `BackgroundHeight` semantics are unchanged.)

**Step 5:** Build → 0 errors; full suite green (no pure-logic change). These four are visual — flag them for the E1 screenshot pass in Task 11.

**Step 6: Commit**

```bash
git add Scripts/Character/Character.cs Scripts/Overlays/EmoteAnimation.cs Scripts/Overlays/ChatBubble.cs
git commit -m "fix(overlays): height-derived name/bar anchors, per-state Height, emote pivot, bubble centering"
```

---

### Task 10: `OnSetYourCharacter` null guard + `WorldToTile` floors negatives

Two independent one-liners. Unity early-returns when the SetYourCharacter id is unknown (Unity `MapManager.cs:121-122`); Godot fires `CharacterUpdated(null)`. And `MapCoords.WorldToTile`'s doc says "floor" but `(int)` truncates toward zero, so clicks just off the map's top/left edge report tile 0.

**Files:**
- Modify: `Scripts/MapManager.cs:148-154`
- Modify: `Scripts/Map/MapCoords.cs:21-23`
- Test: `tests/Goose2Client.Tests/MapCoordsTests.cs` (append)

**Step 1: Failing test:**

```csharp
[Fact]
public void WorldToTile_NegativeCoords_FloorsToNegativeTile()
{
    var (x, y) = MapCoords.WorldToTile(new Godot.Vector2(-0.5f, -0.5f));
    Assert.Equal(-1, x);
    Assert.Equal(-1, y);
}
```

**Step 2: FAIL** (returns 0,0). **Step 3: Implement:**

```csharp
/// <summary>World pixel → tile coords (floor — negatives round down, not toward zero).</summary>
public static (int x, int y) WorldToTile(Vector2 world)
    => (Mathf.FloorToInt(world.X / TileSize), Mathf.FloorToInt(world.Y / TileSize));
```

**Step 4:** Replace `OnSetYourCharacter` (`Scripts/MapManager.cs:148-154`):

```csharp
private void OnSetYourCharacter(object packetObj)
{
    var p = (SetYourCharacterPacket)packetObj;
    _myLoginId = p.LoginId;
    if (!_characters.TryGetValue(p.LoginId, out var c))
        return;   // Unity returns when unknown (MapManager.cs:121-122); MKC will attach later
    AttachLocalPlayer(c);
    GameManager.Instance.OnCharacterUpdated(c);
}
```

**Step 5:** `dotnet test ... --filter MapCoordsTests` → PASS; build → 0 errors. **Step 6: Commit**

```bash
git add Scripts/MapManager.cs Scripts/Map/MapCoords.cs tests/Goose2Client.Tests/MapCoordsTests.cs
git commit -m "fix(map): SetYourCharacter null guard; WorldToTile floors negative coords"
```

---

### Task 11: Series verification + docs

**Step 1: Full suite.**
- `dotnet build Goose2ClientGodot.csproj` → 0 errors, 0 new warnings.
- `dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj` → everything green (Parts 1+2 totals + MapCoords 1).
- If a `godot` binary is available: `godot --headless --script tools/check_scene.gd -- res://Scenes/UI/VitalsWindow.tscn res://Scenes/UI/GameHud.tscn res://Scenes/Map.tscn` → all `OK load`.

**Step 2: Update `MIGRATION_PLAN.md`.** Add "2026-07-11 Unity-parity bugfix pass — Part 3 (series complete)" listing the fixes, and append to the manual E1 checklist:
- chat colors match Unity palette; Level number visible in vitals HUD
- hotbar responsiveness (instant first press, taps never eaten); Shift+digit emotes; Ctrl+R refresh
- morphing into a monster body strips hair/equipment
- repeat-casting keeps the previous target
- tile-spell layering under Objects 2; dyed dropped-item tint looks like Unity
- emote/bubble heights on tall/mounted bodies; name/bars above tall monsters

**Step 3: Final commit**

```bash
git add MIGRATION_PLAN.md
git commit -m "docs: record Unity-parity bugfix pass Part 3 (series complete)"
```
