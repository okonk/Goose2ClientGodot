# UI Window Overhaul — Part 1: Foundations (Assets, Theme, Window Framework)

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Replace the invented, unstyled, overlapping Godot windows with the foundations needed for a pixel-faithful port of the Unity HUD: import the real window art + font, build a shared Theme, add a Unity→Godot coordinate helper, refactor `BaseWindow` to host fixed-size art, resize slots to 32px, and give every window a distinct default position + correct first-login visibility (with visibility persistence).

**Architecture:** The Unity client's windows are **fixed-size pre-drawn background sprites** (`character.png` 400×222, `inventory.png` 168×235, etc.) with widgets absolutely positioned on top at baked coordinates — NOT resizable 9-slice frames. The current Godot port invented a resizable `Panel`+`GridContainer` `BaseWindow` with arbitrary sizes, which is why nothing lines up and everything is muddy (no Theme → default dark Panel) and stacked (all at `offset (100,100)`). Part 1 builds the shared machinery; **Part 2** re-lays-out each individual window on top of it.

**Tech Stack:** Godot 4.6 / C# (.NET 10), xUnit. Unity reference (READ-ONLY): `/home/hayden/code/Goose2Client`. Never modify the Unity project.

**Decisions locked with the user (2026-06-07):**
- **Theming:** Port the real Unity sprite skins (pixel-faithful), not a hand-built flat theme.
- **Default windows on first login:** Minimal HUD — Vitals, Hotbar, Chat, Toolbar, Party, Buffs open; Inventory/Character/Spellbook **closed** (toggle with I/C/B). Persisted thereafter.
- **Scope:** This overhaul owns Theme + positions + visibility set + CharacterWindow rebuild + Vitals portrait node + **window-visibility persistence (Step 8 D1, moved here)**. The portrait's appearance-data *rendering* (Step 8 A1) stays in Step 8.
- **Sequencing:** This overhaul (Part 1 then Part 2) lands **before** the Step 8 plans.

---

## APIs / Facts Verified (`path:line`)

**Godot side (`/home/hayden/code/Goose2ClientGodot`):**
- `Scripts/UI/BaseWindow.cs:10-52` — `BaseWindow : Control`; resolves `Background`(Panel), `TitleBar`(Control), `TitleBar/CloseButton`(Button), `TitleBar/TitleLabel`(Label), `Content`(Control) by node path in `_Ready`; restores `Position` from `GameManager.Instance.CharacterSettings.GetWindowSettings(WindowName)` (`:33-38`); `Modulate = (1,1,1,0.7f)` always-on (`:47`); hover → 1.0 (`:82-84`); `Toggle()` (`:86`); `OnClosePressed()→Hide()` (`:88`); saves position on drag end (`:65-67`, `:73-76`).
- The protected properties `Background`/`Content`/`TitleLabel` are referenced **nowhere** outside `BaseWindow.cs` (grep confirmed) → safe to change `Background`'s node type.
- `Scripts/UI/GameHud.cs:35-77` — instantiates every window via `Add<T>(path)` (`:28-33`); order at `:50-63`; `SetAnchorsPreset(FullRect)` + `MouseFilter=Ignore` (`:38-39`); wires `Hotbar.InventoryWindow/SpellbookWindow` (`:70-71`); `Options.ToggleWindow` via toolbar (`:74-76`). Toggle input in `_UnhandledInput` (`:79-105`): `ToggleInventory`/`ToggleSpellbook`/`ToggleCharacterWindow` → `Inventory.Toggle()`/`Spellbook.Toggle()`/`Character.Toggle()` (`:85-90`).
- `Scripts/CharacterSettings.cs` — `WindowSettings { public Vector2 Position; }` (`:29-32`, **no `Visible` field yet**); `Dictionary<string, WindowSettings> WindowSettings` JSON-persisted; `GetWindowSettings(string)` (`:152`) returns null if absent; `SetWindowSetting(string, Vector2?)` (`:160`); `JsonOptions` has `IncludeFields=true` (`:36`); already in the test csproj. `Load()` is exception-safe (degrades to defaults).
- `tests/Goose2Client.Tests/Goose2Client.Tests.csproj:13-39` — explicit `<Compile Include="../../Scripts/...">` list; `CharacterSettings.cs` already included (`:21`); xunit 2.9.2; net10.0.
- Slot scenes are **40×40**: `Scenes/UI/ItemSlot.tscn` (`offset_right/bottom = 40`, Icon 40, Count offsets `-35`), `Scenes/UI/HotbarSlot.tscn` (40 + CooldownOverlay 40), `Scenes/UI/SpellSlot.tscn` (40 + CooldownOverlay 40). `Scripts/UI/ItemSlot.cs:79` builds drag preview from `_icon.Size` (auto-follows resize).
- `project.godot` has **no `[display]` section** and **no `gui/theme/custom`** → UI renders at native pixel size; no Theme assigned anywhere (confirmed: zero `.theme`/StyleBox in repo).
- Existing texture import preset (sample `Assets/Sprites/sheets/321.png.import`): `importer="texture"`, `compress/mode=0`, `mipmaps/generate=false`. Assets live under `Assets/` (e.g. `Assets/Sprites/`).

**Unity side (`/home/hayden/code/Goose2Client`) — verified by source audit:**
- Window background sprites (all in `Assets/Resources/UI/`), `dimensions | spriteBorder(L,B,R,T) | type(0=Simple,1=Sliced)`:
  - `inventory.png` 168×235 | 0,0,0,0 | Simple
  - `character.png` 400×222 | 0,0,0,0 | Simple
  - `spellbook-background.png` 128×196 | 0,0,0,0 | Simple
  - `hotbar.png` 333×36 | 3,3,3,3 | **Sliced**
  - `bank.png` 168×253 | 0,0,0,0 | Simple
  - `vendor.png` 168×276 | 0,0,0,0 | Simple
  - `10slot.png` 69×212 | 0,0,0,0 | Simple (CombineBag)
  - `quest.png` 260×291 | 5,36,12,20 | **Sliced**
  - `info.png` 252×140 | 3,3,10,20 | **Sliced**
  - `chat.png` 500×208 | 0,0,0,0 | Simple
  - `vitals-outline.png` 183×55 | 0,0,0,0 | Simple
- Buttons: `button-normal.png` 65×23, `button-pressed.png` 65×23 (Simple); `exitbutton.png` 32×32 (close); `optionsbutton.png` 32×32; `destroy.png` 32×32; `spellbook-back.png`/`spellbook-next.png` 24×24.
- Bars/decor: `vitals-hp-bar.png` 133×17, `vitals-mp-bar.png` 108×17, `vitals-character-circle.png` 53×53 (portrait mask), `vitals-level-circle.png` 19×19, `xp-bar.png` 331×15 / `xp-bar-outline.png` 333×17 (Sliced 3,3,3,3), `hotbar-slot-background.png` 32×32 (Sliced 3,3,3,3), `hotbar-up.png`/`hotbar-down.png` 16×16, `party-frame.png` 87×23, `party-hp-bar.png` 85×10, `party-mp-bar.png` 68×10.
- Font: `Assets/TextMesh Pro/Fonts/LiberationSans.ttf` (exists, 350KB). Sizes used: **10** (primary UI text/titles/stats/buttons), **12** (chat + debug), 8–9 (slot count badges). White text (`m_fontColor (1,1,1,1)`).
- Item slot is **32×32** in Unity (`Assets/Prefabs/UI/ItemSlot.prefab:39,252`).
- Window transparency: Unity `WindowTransparency.cs` — alpha **0.7** idle, **1.0** on hover (matches current Godot `BaseWindow` Modulate; keep it).
- Visibility/position persistence: Unity `CharacterSettings` persists **position only**, not visibility (`TitleBar.cs` applies saved pos on Awake). We are *extending* this to also persist visibility (D1).

---

## Conventions (apply to every task)
- Build gate per task: `dotnet build Goose2ClientGodot.csproj` → 0 errors; `dotnet test tests/Goose2Client.Tests/Goose2Client.Tests.csproj` → green.
- Pure logic → Godot-free class added to the test csproj (explicit `<Compile Include>`) + xUnit tests. Godot-typed files are NOT added to the test project.
- Keep node **names** that `*.cs` files resolve by path unchanged unless the task explicitly updates the `.cs` too.
- Commit after each task. Branch: `feat/ui-windows-part1`.
- Texture import: copy PNG into the repo, then generate the `.import` (Task 1 covers the mechanism). Never edit the Unity project.

---

## Task 0: Branch + asset directory

**Files:**
- Create dir: `Assets/UI/` and `Assets/UI/Fonts/`

**Steps:**
1. `git checkout -b feat/ui-windows-part1`
2. `mkdir -p Assets/UI/Fonts`
3. Commit: `git commit --allow-empty -m "chore(ui): start window overhaul part 1 (foundations)"`

---

## Task 1: Import the Unity window sprite art

**Files:**
- Copy: 20 PNGs from `/home/hayden/code/Goose2Client/Assets/Resources/UI/` → `Assets/UI/`
- Create: `Assets/UI/*.png.import` (one per PNG)

**Step 1: Copy the sprites we need.** Run:
```bash
SRC=/home/hayden/code/Goose2Client/Assets/Resources/UI
DST=/home/hayden/code/Goose2ClientGodot/Assets/UI
for f in inventory character spellbook-background hotbar bank vendor 10slot quest info chat \
         vitals-outline button-normal button-pressed exitbutton optionsbutton destroy \
         spellbook-back spellbook-next hotbar-slot-background hotbar-up hotbar-down \
         xp-bar xp-bar-outline vitals-hp-bar vitals-mp-bar vitals-character-circle vitals-level-circle \
         party-frame party-hp-bar party-mp-bar; do
  cp "$SRC/$f.png" "$DST/$f.png"
done
ls -1 "$DST"/*.png | wc -l   # expect 31
```

**Step 2: Generate `.import` files.** Preferred: open the project headless so Godot imports them:
```bash
cd /home/hayden/code/Goose2ClientGodot
godot --headless --import --quit 2>&1 | tail -20   # or: godot4 / the godot binary on PATH
```
Expected: `.import` files appear next to each PNG; exit 0.

**Fallback if no `godot` binary is available** (hand-author each `.import`; use **Nearest** filter for crisp pixel-art UI). Template per file (replace `<NAME>` and pick a unique `uid`):
```ini
[remap]
importer="texture"
type="CompressedTexture2D"
uid="uid://ui_<NAME>"
path="res://.godot/imported/<NAME>.png-<hash>.ctex"
metadata={"vram_texture": false}

[deps]
source_file="res://Assets/UI/<NAME>.png"
dest_files=["res://.godot/imported/<NAME>.png-<hash>.ctex"]

[params]
compress/mode=0
mipmaps/generate=false
detect_3d/compress_to=0
process/fix_alpha_border=true
```
(Hand-authoring hashes is fiddly — strongly prefer the headless import. If hand-authoring, the simplest robust path is to commit the PNGs and run the import once on a machine with Godot before Part 2.)

**Step 3: Set crisp UI filtering.** In `project.godot`, ensure pixel-art UI stays sharp by adding under `[rendering]` (Nearest = 0):
```ini
[rendering]

textures/canvas_textures/default_texture_filter=0
```
Verify this does not blur the in-world map (map sheets already import without filtering issues; Nearest is correct for this art style).

**Step 4: Commit:** `git add Assets/UI project.godot && git commit -m "feat(ui): import Unity window sprite art (nearest filter)"`

---

## Task 2: Import the LiberationSans font

**Files:**
- Copy: `/home/hayden/code/Goose2Client/Assets/TextMesh Pro/Fonts/LiberationSans.ttf` → `Assets/UI/Fonts/LiberationSans.ttf`
- Create: `Assets/UI/Fonts/LiberationSans.ttf.import`

**Steps:**
1. `cp "/home/hayden/code/Goose2Client/Assets/TextMesh Pro/Fonts/LiberationSans.ttf" Assets/UI/Fonts/`
2. Generate the `.import` (headless import as in Task 1, or hand-author with `importer="font_data_dynamic"`).
3. Commit: `git add Assets/UI/Fonts && git commit -m "feat(ui): import LiberationSans font"`

---

## Task 3: Shared Theme resource

**Files:**
- Create: `Assets/UI/GameTheme.tres`
- Modify: `project.godot` (`gui/theme/custom`)

**Step 1:** Author `Assets/UI/GameTheme.tres` — a `Theme` with:
- `default_font` = `Assets/UI/Fonts/LiberationSans.ttf`, `default_font_size = 10`.
- `Label`: `font_color = Color(1,1,1,1)`.
- `Button`: `normal` = `StyleBoxTexture` over `button-normal.png`, `pressed`/`hover` = `StyleBoxTexture` over `button-pressed.png`; `font_color` white; flat where the per-window scene supplies its own icon button (those override locally in Part 2).
- `Panel`: `panel` = empty/transparent `StyleBoxEmpty` (windows draw their own art via TextureRect; the default dark Panel must NOT show through).

`.tres` skeleton (fill real `uid`/`id` from the imported resources):
```ini
[gd_resource type="Theme" load_steps=5 format=3]

[ext_resource type="FontFile" path="res://Assets/UI/Fonts/LiberationSans.ttf" id="font"]
[ext_resource type="Texture2D" path="res://Assets/UI/button-normal.png" id="btn_n"]
[ext_resource type="Texture2D" path="res://Assets/UI/button-pressed.png" id="btn_p"]

[sub_resource type="StyleBoxTexture" id="sb_btn_n"]
texture = ExtResource("btn_n")

[sub_resource type="StyleBoxTexture" id="sb_btn_p"]
texture = ExtResource("btn_p")

[sub_resource type="StyleBoxEmpty" id="sb_empty"]

[resource]
default_font = ExtResource("font")
default_font_size = 10
Label/colors/font_color = Color(1, 1, 1, 1)
Button/styles/normal = SubResource("sb_btn_n")
Button/styles/hover = SubResource("sb_btn_n")
Button/styles/pressed = SubResource("sb_btn_p")
Button/colors/font_color = Color(1, 1, 1, 1)
Panel/styles/panel = SubResource("sb_empty")
```

**Step 2:** Assign globally — add under `[gui]` in `project.godot`:
```ini
[gui]

theme/custom="res://Assets/UI/GameTheme.tres"
```

**Step 3:** Build + commit. (Visual verification happens in Part 2 once windows have art.)
`git add Assets/UI/GameTheme.tres project.godot && git commit -m "feat(ui): shared GameTheme (LiberationSans, white text, button skins, transparent panels)"`

---

## Task 4: Unity→Godot coordinate helper (pure, TDD)

The Unity prefabs store widget positions as RectTransform `anchoredPosition`/`sizeDelta` with y-up center origins. We will convert dozens of these in Part 2; do it with one tested helper instead of ad-hoc math.

**Files:**
- Create: `Scripts/UI/UnityRect.cs`
- Test: `tests/Goose2Client.Tests/UnityRectTests.cs`
- Modify: `tests/Goose2Client.Tests/Goose2Client.Tests.csproj` (add both `UnityRect.cs` compile include + the test)

**Step 1: Failing test** `tests/Goose2Client.Tests/UnityRectTests.cs`:
```csharp
using Goose2Client.UI;
using Xunit;

public class UnityRectTests
{
    [Fact]
    public void TopLeftAnchor_CenterPivot_Converts()
    {
        // CharacterCanvas NameText: parent 400x222, anchor (0,1), pivot (0.5,0.5),
        // anchoredPos (55.41,-15.59), size (100.82,11.18)  -> expect (5,10)
        var r = UnityRect.ToGodot(400, 222, 0f, 1f, 0.5f, 0.5f, 55.41f, -15.59f, 100.82f, 11.18f);
        Assert.Equal(5f, r.Left, 1);
        Assert.Equal(10f, r.Top, 1);
        Assert.Equal(100.82f, r.Width, 2);
        Assert.Equal(11.18f, r.Height, 2);
    }

    [Fact]
    public void CenterAnchor_CenterPivot_Converts()
    {
        // VitalsCanvas HP bar: parent 183x55, anchor (0.5,0.5), pivot (0.5,0.5),
        // anchoredPos (24,9), size (133,17) -> expect (49,10)
        var r = UnityRect.ToGodot(183, 55, 0.5f, 0.5f, 0.5f, 0.5f, 24f, 9f, 133f, 17f);
        Assert.Equal(49f, r.Left, 1);
        Assert.Equal(10f, r.Top, 1);
    }
}
```

**Step 2:** Run → FAIL (type missing).

**Step 3: Implement** `Scripts/UI/UnityRect.cs`:
```csharp
namespace Goose2Client.UI;

/// <summary>A Godot Control rect in top-left/y-down pixel coordinates.</summary>
public readonly struct GodotRect
{
    public readonly float Left, Top, Width, Height;
    public GodotRect(float left, float top, float width, float height)
    { Left = left; Top = top; Width = width; Height = height; }
}

/// <summary>
/// Converts a Unity RectTransform (y-up, center-origin anchors/pivot) into Godot
/// Control offsets (y-down, top-left). Assumes a point anchor (anchorMin == anchorMax).
/// </summary>
public static class UnityRect
{
    public static GodotRect ToGodot(
        float parentW, float parentH,
        float anchorX, float anchorY,
        float pivotX, float pivotY,
        float anchoredX, float anchoredY,
        float w, float h)
    {
        float left = anchorX * parentW + anchoredX - pivotX * w;
        float top  = parentH - anchorY * parentH - anchoredY - (1f - pivotY) * h;
        return new GodotRect(left, top, w, h);
    }
}
```

**Step 4:** Add to `Goose2Client.Tests.csproj` ItemGroup:
```xml
    <Compile Include="../../Scripts/UI/UnityRect.cs" />
```
(The test file is auto-globbed by the SDK; only the `Scripts/` file needs an explicit include.)

**Step 5:** Run → PASS. Commit: `git add Scripts/UI/UnityRect.cs tests/ && git commit -m "feat(ui): tested Unity->Godot RectTransform coordinate helper"`

---

## Task 5: Refactor `BaseWindow` for fixed-size art windows

Make `BaseWindow` host a sprite background + transparent drag region + sprite close button, at a fixed size, while keeping drag + position persistence. This unblocks every Part-2 window rebuild.

**Files:**
- Modify: `Scripts/UI/BaseWindow.cs`
- Modify: `Scenes/UI/BaseWindow.tscn` (template other windows copy in Part 2)

**Step 1:** In `Scenes/UI/BaseWindow.tscn`:
- Root `Control`: remove the `FullRect`-style anchors; set a fixed size via `offset_right/offset_bottom` (the per-window PNG size; the template can stay 220×160).
- `Background`: change type `Panel` → `TextureRect` (no texture in the template; each window sets its own). Keep `anchors_preset=15` (fills root), `mouse_filter=2` (ignore), `texture_filter` inherits Nearest.
- `TitleBar`: keep as a transparent `Control` (not `Panel`) spanning the top drag strip (height = window-specific; default full-width × 24). `mouse_filter=0` so it captures drag. No visible art.
- `TitleBar/CloseButton`: keep a `Button`; in Part 2 windows that have a close affordance it gets `exitbutton.png` via `icon`/theme override. Template keeps text "X".
- `Content`: keep a transparent `Control` fill (children added per window).

**Step 2:** In `Scripts/UI/BaseWindow.cs`:
- Change the `Background` field/property type from `Panel` to `TextureRect` (or just `Control`), updating `GetNodeOrNull<...>("Background")`. (No external refs — verified.)
- Keep `Modulate = (1,1,1,0.7f)` idle + hover→1.0 (matches Unity `WindowTransparency`).
- Leave drag + `GetWindowSettings`/`SetWindowSetting` position logic intact (Task 9 extends it for visibility).

Concrete edit to `_Ready` node resolution:
```csharp
private TextureRect _background;        // was: Panel Background
...
_background = GetNodeOrNull<TextureRect>("Background");
```
(Drop the unused `protected Panel Background` property, or retype it to `TextureRect`.)

**Step 3:** Build → 0 errors. Smoke: the existing windows still instantiate (they'll be re-laid-out in Part 2; for now they may look bare — acceptable, Part 1 is foundations). Commit: `git commit -am "refactor(ui): BaseWindow hosts sprite background + transparent drag region"`

---

## Task 6: Resize slots 40px → 32px

Unity slots are 32×32; the window art has 32px cells. Resize the three slot scenes so Part-2 grids align to the art.

**Files:**
- Modify: `Scenes/UI/ItemSlot.tscn`, `Scenes/UI/HotbarSlot.tscn`, `Scenes/UI/SpellSlot.tscn`

**Steps:**
1. In each scene set the root and `Icon`/`CooldownOverlay` `offset_right`/`offset_bottom` from `40` → `32`.
2. In `ItemSlot.tscn`, adjust the `Count` label offsets proportionally (e.g. `offset_left/top = -30`, keep `-2` right/bottom) so the stack badge fits a 32px slot.
3. `Scripts/UI/ItemSlot.cs:79` builds the drag preview from `_icon.Size` — auto-follows; no code change.
4. Build → 0 errors. Commit: `git commit -am "fix(ui): slots 32x32 to match Unity art cells"`

---

## Task 7: Central default-position table + apply on first run

Stop every window opening at `(100,100)`. Define a distinct default screen position per window, applied when there is no saved setting.

**Files:**
- Create: `Scripts/UI/DefaultWindowLayout.cs` (pure: name → default `Vector2`; testable)
- Modify: `Scripts/UI/BaseWindow.cs` (use default when `GetWindowSettings` is null)
- Test: `tests/Goose2Client.Tests/DefaultWindowLayoutTests.cs` + csproj include

**Step 1: Failing test** — assert distinct, known defaults and that unknown names fall back to a sane value:
```csharp
using Godot;
using Goose2Client.UI;
using Xunit;

public class DefaultWindowLayoutTests
{
    [Fact] public void KnownWindows_HaveDistinctPositions()
    {
        var inv = DefaultWindowLayout.For("Inventory");
        var chr = DefaultWindowLayout.For("Character");
        Assert.NotEqual(inv, chr);
    }
    [Fact] public void Unknown_FallsBackToOrigin_Offset()
        => Assert.Equal(new Vector2(100, 100), DefaultWindowLayout.For("Nope"));
}
```
> NOTE: `Vector2` comes from GodotSharp, referenced by the test csproj (`:8`) — no Godot runtime needed for struct math.

**Step 2:** Run → FAIL.

**Step 3: Implement** `Scripts/UI/DefaultWindowLayout.cs` with a dictionary of sensible, non-overlapping defaults (final pixel values tuned against the live screenshot in Part 2 — these are the starting layout). Example anchoring against a 1152×648 default window:
```csharp
using Godot;
using System.Collections.Generic;

namespace Goose2Client.UI;

/// <summary>First-run window positions (used when no saved CharacterSettings position exists).</summary>
public static class DefaultWindowLayout
{
    private static readonly Dictionary<string, Vector2> Defaults = new()
    {
        ["Inventory"] = new Vector2(900, 360),
        ["Character"] = new Vector2(380, 120),
        ["Spellbook"] = new Vector2(700, 120),
        ["Hotbar"]    = new Vector2(410, 600),
        ["Vendor"]    = new Vector2(300, 200),
        ["Bank"]      = new Vector2(300, 200),
        ["CombineBag"]= new Vector2(540, 220),
        ["Options"]   = new Vector2(460, 260),
    };

    public static Vector2 For(string windowName)
        => windowName != null && Defaults.TryGetValue(windowName, out var p) ? p : new Vector2(100, 100);
}
```

**Step 4:** In `BaseWindow._Ready` position-restore block:
```csharp
if (WindowName != null)
{
    var ws = GameManager.Instance.CharacterSettings.GetWindowSettings(WindowName);
    Position = ws != null ? ws.Position : DefaultWindowLayout.For(WindowName);
}
```
> Windows positioned outside `BaseWindow` (Vitals/Chat/Party/Buffs/Debug/Toolbar — always-on HUD anchored to screen corners) keep their scene offsets; Part 2 sets those.

**Step 5:** Add `Scripts/UI/DefaultWindowLayout.cs` to the test csproj, run → PASS. Commit: `git commit -am "feat(ui): distinct default window positions"`

---

## Task 8: Minimal-HUD default visibility

Inventory / Character / Spellbook start **hidden** on first login (toggle to open). Everything else keeps its current default.

**Files:**
- Modify: `Scripts/UI/InventoryWindow.cs`, `Scripts/UI/CharacterWindow.cs`, `Scripts/UI/SpellbookWindow.cs` (`_Ready`)

**Step 1:** In each of the three `_Ready` methods (after `base._Ready()` / listener setup), default to hidden **unless a saved visibility says otherwise** (Task 9 supplies the saved value; until then, hidden):
```csharp
// First-login default: closed. Task 9 overrides from saved visibility.
if (GameManager.Instance.CharacterSettings.GetWindowSettings(WindowName)?.Visible is not bool v)
    Visible = false;
else
    Visible = v;
```
> Implement this only after Task 9 adds `Visible`; if executing Task 8 first, temporarily use `Visible = false;` and fold the saved-visibility branch in during Task 9.

**Step 2:** Confirm the toggle path still works: `GameHud._UnhandledInput` → `Inventory.Toggle()` etc. (`BaseWindow.Toggle()` flips `Visible`).

**Step 3:** Build, manual-smoke note (verified live in Part 2). Commit: `git commit -am "feat(ui): minimal-HUD default (inventory/character/spellbook closed)"`

---

## Task 9: Persist window visibility (Step 8 D1, moved here)

Persist open/closed alongside position so the HUD restores across relog.

**Files:**
- Modify: `Scripts/CharacterSettings.cs` (add `Visible` to `WindowSettings`; setter overload)
- Modify: `Scripts/UI/BaseWindow.cs` (restore + save visibility)
- Test: `tests/Goose2Client.Tests/CharacterSettingsTests.cs` (extend; csproj already includes `CharacterSettings.cs`)

**Step 1: Failing test** — round-trip visibility through JSON:
```csharp
[Fact]
public void WindowVisibility_RoundTrips()
{
    var s = new CharacterSettings();
    s.SetWindowSetting("Inventory", new Vector2(10, 20), visible: true);
    var json = s.ToJson();
    var back = CharacterSettings.FromJson(json);
    var ws = back.GetWindowSettings("Inventory");
    Assert.NotNull(ws);
    Assert.True(ws.Visible);
    Assert.Equal(new Vector2(10, 20), ws.Position);
}
```
> Confirm the exact serialization method names (`ToJson`/`FromJson`) against `CharacterSettings.cs` and match them; adjust the test to the real API before running.

**Step 2:** Run → FAIL (no `visible` param / `Visible` field).

**Step 3: Implement** in `CharacterSettings.cs`:
- Add `public bool Visible;` to `WindowSettings` (struct/class at `:29-32`). Default `false` is fine for the three toggle windows; always-on windows never query it.
- Add an overload preserving the existing one:
```csharp
public void SetWindowSetting(string name, Vector2? position, bool visible)
{
    var ws = GetOrCreateWindowSettings(name);   // mirror existing SetWindowSetting(:160) creation path
    if (position.HasValue) ws.Position = position.Value;
    ws.Visible = visible;
}
```
Keep the existing `SetWindowSetting(name, Vector2?)` delegating with the current visibility (or `false`).

**Step 4:** In `BaseWindow`:
- On `Toggle()` and `OnClosePressed()`, persist visibility:
```csharp
public void Toggle()
{
    Visible = !Visible;
    if (WindowName != null)
        GameManager.Instance.CharacterSettings.SetWindowSetting(WindowName, Position, Visible);
}
protected virtual void OnClosePressed()
{
    Hide();
    if (WindowName != null)
        GameManager.Instance.CharacterSettings.SetWindowSetting(WindowName, Position, false);
}
```
- Fold the saved-visibility branch into Task 8's block (restore `Visible` from `GetWindowSettings(WindowName)?.Visible`).

**Step 5:** Run tests → PASS; build → 0 errors. Commit: `git commit -am "feat(ui): persist window visibility across relog (D1)"`

---

## Part 1 Done — Checkpoint

- [ ] All UI sprites + font imported; project builds.
- [ ] `GameTheme.tres` assigned; default Panel no longer renders dark.
- [ ] `UnityRect` + `DefaultWindowLayout` tested green.
- [ ] `BaseWindow` hosts a `TextureRect` background + transparent drag + sprite close button.
- [ ] Slots are 32px.
- [ ] Windows open at distinct positions; Inventory/Character/Spellbook start closed; visibility persists.

> Windows will still look unfinished (no per-window art/coordinates yet) — that is **Part 2**. Do not attempt to pixel-tune individual windows here.

**Update `MIGRATION_PLAN.md`:** mark D1 (window visibility persistence) ✅ Resolved (UI overhaul), and note the Step 8 D1 item is satisfied here.

---

## Handoff
Merge `feat/ui-windows-part1`, then proceed to **`docs/plans/2026-06-07-ui-windows-part2-relayout.md`** (per-window faithful re-layout + live tuning), which depends on every foundation above.
