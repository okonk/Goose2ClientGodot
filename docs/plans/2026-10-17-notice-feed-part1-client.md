# Notice Feed — Part 1: Client Implementation Plan

**Goal:** Parse the new `NTF` packet and show each notice as a floating, fading text line below the minimap.

**Architecture:** `NoticePacket` parses `NTF<category>,<text>`. A pure `NoticeFeedModel` owns entries, the 6-entry cap and the 5 s lifetime/fade; `NoticeFeed` (code-built `Control`, like `MinimapControl`) listens for the packet, ticks the model each frame and mirrors it into outlined `Label`s. `NoticeColors` maps `NoticeType` → color on the client.

**Tech Stack:** Godot 4.6 C# (.NET 10), xUnit (`tests/Goose2Client.Tests`).

Design: `docs/plans/2026-10-17-notice-feed-design.md`. This is **Part 1 of 2**; Part 2 (server) is `docs/plans/2026-10-17-notice-feed-part2-server.md`. Part 1 needs no server change: `NTF` is simply never received until Part 2 ships. Follow `AGENTS.md`: no new comments/doc strings except non-obvious "why".

---

## APIs verified

- `PacketHandler` (`Prefix`, `Parse`) — `Scripts/Network/PacketHandler.cs:6-12`.
- `PacketParser.GetInt32` `:63`, `GetRemaining` (throws at end of packet) `:27-33`, `LengthRemaining` `:98`. Default delimiter `,` `:13`.
- Empty-remainder precedent: `AmountInfoPacket.cs:37` (`p.LengthRemaining() > 0 ? p.GetRemaining() : ""`); parse-test model `tests/Goose2Client.Tests/AmountInfoPacketTests.cs`.
- Prefix lookup is shortest-registered-prefix-first, max 8 chars — `Scripts/Network/PacketManager.cs:35-43` and `Handle` `:46-75`. No handler has prefix `N` or `NT` (all prefixes checked), so `NTF` is unambiguous. A parse exception is logged and the packet dropped (`:58-62`).
- `PacketManager.Listen<T>` `:12-20` / `Remove<T>` `:22-25` (`Remove` indexes `typeToHandler` directly, so only call it after `Listen`). Listener teardown precedent with a guard flag: `Scripts/UI/ChatWindow.cs:31,78-83,113-121`. `PacketManager.Clear()` is never called.
- Packets are dispatched on the game thread: `GameManager._Process` `Scripts/GameManager.cs:141-152` drains `_packetQueue` → `PacketManager.Handle`.
- `ChatType` enum lives in `Scripts/Constants.cs:108-118`, namespace `Goose2Client` — put `NoticeType` beside it.
- Colors: `GameColors.White/Yellow/Green` `Scripts/GameColors.cs:16-18`; `Yellow` = (248,208,0) = battle-text `Yellow60` (`Scripts/Overlays/BattleTextLayout.cs:78-79`); `Green` = (136,204,64) = heal text.
- Test project compiles all of `Scripts/**/*.cs` with GodotSharp referenced (`tests/Goose2Client.Tests/Goose2Client.Tests.csproj:8,14`), so `Godot.Color` is usable in tests (`MinimapColorsTests.cs`). Pure-C# precedent: `Scripts/Overlays/BattleTextManager.cs`.
- `MinimapControl` geometry: `BasePixels` `:15` (public), `BaseBorder` `:16`, `BaseNameHeight` `:18` (private); `_Ready` sets `OffsetTop = 28`, `OffsetRight = -8`, `OffsetBottom = 28 + BasePixels + 2 * BaseBorder` `:63-67`; name label sits at `OffsetTop = 2 .. 2 + BaseNameHeight` below it `:94-99`. `SetEnabled` → `Visible` `:43`.
- HUD build order: `Minimap` added at `Scripts/UI/GameHud.cs:78-79`, before all windows (so windows draw over it).
- Scaling: `IScalableWindow.Relayout` `Scripts/IScalableWindow.cs`; `UiScaleApplier.Instance.Factor` `:60`, `ScaleSize` `:64`, `RegisterWindow` `:68`, `UnregisterWindow` `:86`. `Apply` sets the theme default font size (`:147`) so unoverridden labels scale automatically, then calls every registered `Relayout` (`:163-164`). `UiScaleLayout.Apply` rounds offsets with `MathF.Round(x * factor, MidpointRounding.AwayFromZero)` (`Scripts/UiScaleLayout.cs:58-61`).

**Trap:** do **not** use `UiScaleApplier.ApplyFontSize` or `UiScaleLayout.Snapshot` for the per-notice labels. Both are build-time registries (`_fonts` only prunes dead controls during `Apply`/`UnregisterWindow`), and notices are created and freed continuously. Use the theme default font size, and set the outline size from `ScaleSize` at creation and in `Relayout`.

---

### Task 1: `NoticeType`, `NoticeColors`, `NoticePacket`

**Files:**
- Modify: `Scripts/Constants.cs` (add enum after `ChatType`, `:118`)
- Create: `Scripts/UI/NoticeColors.cs`
- Create: `Scripts/Network/Packets/NoticePacket.cs`
- Test: `tests/Goose2Client.Tests/NoticePacketTests.cs`, `tests/Goose2Client.Tests/NoticeColorsTests.cs`

```csharp
public enum NoticeType
{
    Info = 0,
    Quest = 1,
    BuffOn = 2,
    BuffOff = 3,
}
```

`NoticeColors.For(NoticeType type) → Color`: Info → `GameColors.White`, Quest → `GameColors.Yellow`, BuffOn → `GameColors.Green`, BuffOff → `new Color(0.7f, 0.7f, 0.7f)`; **any other value → `GameColors.White`**.

`NoticePacket : PacketHandler` (namespace `Goose2Client.Network.Packets`, `public class`): `Prefix = "NTF"`, properties `NoticeType Type`, `string Text = ""`. Parse: `Type = (NoticeType)p.GetInt32()`, `Text = p.LengthRemaining() > 0 ? p.GetRemaining() : ""`.

**Step 1: Failing tests**

`NoticePacketTests` (follow `AmountInfoPacketTests` style):
- `NTF1,Quest Credit: The Hunter's Request (3/10)` → Quest, exact text.
- **Adversarial:** `NTF2,You feel strong, and fast.` → text keeps the comma (fails if `GetString()` is used for text).
- `NTF0,` → `""` without throwing; `NTF0` (no comma) → `""` without throwing.
- `NTF9,hi` → `(int)Type == 9` (unknown categories survive parsing).
- `NTFx,hi` → throws (so `PacketManager.Handle` drops it).

`NoticeColorsTests`: each defined type maps to its color; **`(NoticeType)99` → White** (fails on a `switch` with no default or a throwing default).

**Step 2: Red** — `dotnet test tests/Goose2Client.Tests --filter "FullyQualifiedName~Notice"` → fails to compile (types missing).

**Step 3: Implement** as specified above.

**Step 4: Green** — same command, all pass.

**Step 5: Commit** — `feat(notice-feed): NTF packet and notice colors`.

---

### Task 2: `NoticeFeedModel`

**Files:**
- Create: `Scripts/UI/NoticeFeedModel.cs` (pure C#, no Godot node types)
- Test: `tests/Goose2Client.Tests/NoticeFeedModelTests.cs`

**Surface:**

```csharp
public sealed class NoticeEntry
{
    public NoticeType Type { get; }
    public string Text { get; }
    public double Age { get; internal set; }
}

public sealed class NoticeFeedModel
{
    public const int MaxEntries = 6;
    public const double Lifetime = 5.0;
    public const double FadeDuration = 1.0;

    public IReadOnlyList<NoticeEntry> Entries { get; }   // oldest first, newest last
    public NoticeEntry Add(NoticeType type, string text);
    public void Tick(double delta);
    public static float Alpha(NoticeEntry e);
}
```

**Invariants / behavior:**
- `Add` appends at the end with `Age = 0`; if that makes `Count > MaxEntries`, remove from the front until `Count == MaxEntries`.
- `Tick` adds `delta` to every entry's `Age`, then removes every entry with `Age >= Lifetime`. Entries stay in insertion order.
- `Alpha`: `1` while `Age <= Lifetime - FadeDuration`, then linear down to `0` at `Lifetime`; clamp to `[0,1]`.
- Each entry ages on its own: a new notice never resets older entries' timers.

| Invariant | Proved by |
|---|---|
| Newest last, insertion order kept | `Add_AppendsNewestLast` |
| 7th add evicts the **oldest**, not the newest | `Add_SeventhEvictsOldest` (adversarial: add "1".."7", assert texts are "2".."7") |
| Expiry at exactly `Lifetime` | `Tick_RemovesEntryAtLifetime` (4.99 kept, +0.01 removed) |
| Independent ages | `Tick_ExpiresOnlyOldEntries` (add A, tick 3, add B, tick 2.5 → only B remains) |
| Fade curve | `Alpha_SolidThenLinearFade` (Age 0 → 1, 4 → 1, 4.5 → 0.5, 5 → 0) |

**Steps:** red (compile fail) → implement → green with `dotnet test tests/Goose2Client.Tests --filter "FullyQualifiedName~NoticeFeedModel"` → commit `feat(notice-feed): notice feed model`.

---

### Task 3: `NoticeFeed` control + HUD wiring

**Files:**
- Modify: `Scripts/UI/MinimapControl.cs` (expose footprint constants)
- Create: `Scripts/UI/NoticeFeed.cs`
- Modify: `Scripts/UI/GameHud.cs:32,78-79` (property + construction)

**Prerequisite (MinimapControl):** add `public const int BaseTop = 28;` and use it in `_Ready` in place of the literal `28` (`:63,66`). Add `public const int BaseFootprintBottom = BaseTop + BasePixels + 2 * BaseBorder + 2 + BaseNameHeight;` (= 176, the bottom of the name label). This stops the feed's position from drifting if the minimap's geometry changes. Pure refactor: minimap layout at 1× is unchanged.

**NoticeFeed : Control, IScalableWindow** (namespace `Goose2Client.UI`):

- `public MinimapControl Minimap { get; init; } = null!;` set by the HUD before `AddChild`. No constructor arguments: Godot C# nodes need a parameterless constructor (hot reload), and no node in `Scripts/UI` takes one.
- Base geometry constants: `BaseWidth = 240`, `BaseRightMargin = 8` (matches minimap `OffsetRight = -8`), `BaseGap = 4`, `BaseOutline = 3` (matches minimap name).
- `_Ready`:
  - `SetAnchorsPreset(LayoutPreset.TopRight)`, `MouseFilter = Ignore`.
  - A child `VBoxContainer` (`MouseFilter = Ignore`, `SetAnchorsPreset(LayoutPreset.TopWide)`, separation constant 0).
  - Subscribe `Minimap.VisibilityChanged += Relayout`.
  - `PacketManager.Listen<NoticePacket>(OnNotice)` with a `_listenersRegistered` flag (ChatWindow pattern).
  - `UiScaleApplier.Instance!.RegisterWindow(this)`, `Relayout()`.
- `_ExitTree`: unsubscribe `VisibilityChanged`; `Remove<NoticePacket>` only if registered; `UnregisterWindow(this)`.
- `Relayout()`: `top = Minimap.Visible ? MinimapControl.BaseFootprintBottom + BaseGap : MinimapControl.BaseTop`. Set `OffsetTop = OffsetBottom = S(top)`, `OffsetRight = -S(BaseRightMargin)`, `OffsetLeft = -S(BaseRightMargin + BaseWidth)`, where `S(x) = MathF.Round(x * factor, MidpointRounding.AwayFromZero)` (same rounding as `UiScaleLayout.Apply`, so the feed's right edge stays aligned with the minimap's at every scale). Set each live label's `outline_size` to `ScaleSize(BaseOutline)`.
- `OnNotice(object o)`: `_model.Add(p.Type, p.Text)`, then `SyncLabels()`.
- `_Process(delta)`: return early if `_model.Entries.Count == 0`; else `_model.Tick(delta)`, `SyncLabels()`.
- `SyncLabels()`: keep a `List<(NoticeEntry Entry, Label Label)>`.
  1. `QueueFree` and drop pairs whose entry is no longer in `_model.Entries` (reference equality).
  2. For each model entry without a label, append a `Label` to the VBox. Settings: `Text`, `HorizontalAlignment = Left`, `AutowrapMode = TextServer.AutowrapMode.WordSmart`, `MouseFilter = Ignore`, `font_color` = `NoticeColors.For(type)`, `font_outline_color` = `Colors.Black`, `outline_size` = `ScaleSize(BaseOutline)`.
  3. Set each label's `Modulate.A = NoticeFeedModel.Alpha(entry)`.

  Labels are appended in model order, so VBox order matches oldest-first/newest-bottom. `QueueFree` keeps a freed label in the tree until frame end. Use `RemoveChild` before `QueueFree` so evicted lines don't hold layout space for a frame.

**GameHud:** add `public NoticeFeed Notices { get; private set; } = null!;` and, right after `AddChild(Minimap)` (`:79`), `Notices = new NoticeFeed { Minimap = Minimap }; AddChild(Notices);` so the feed also sits behind every window.

**Mutation impact:**
- Source of truth: `NoticeFeedModel.Entries` (owned by the feed; no persistence, no other readers).
- Derived state: the label list, rebuilt only by `SyncLabels()` after every `Add` and `Tick`. No other derived state found.
- Minimap visibility, the only external input, propagates through `VisibilityChanged → Relayout`. `OptionsWindow.OnMinimapChanged` → `Minimap.SetEnabled` (`OptionsWindow.cs:214-218`, `MinimapControl.cs:43`) raises it.

**Verification:**
- `dotnet build` (via `./build.sh` or `godot-mono --headless --path . --build-solutions --quit`) succeeds; `dotnet test tests/Goose2Client.Tests` stays green (1669 baseline + new).
- No unit test for the node itself (Godot nodes aren't instantiable in the xUnit host); behavior is covered by Task 2. The visual check is Part 2's end-to-end smoke task.

**Commit:** `feat(notice-feed): floating notice feed below the minimap`.

---

## Final review notes

- Threading: all mutation happens on the game thread (packet dispatch and `_Process`).
- Lifecycle: listener registered in `_Ready` after the node is in the tree, removed in `_ExitTree` behind the guard. The scale registration mirrors `MinimapControl` (`:107-110`).
- Persistence: none.
- Design alignment: colors, 6-entry cap, 5 s (4 + 1 fade), newest at bottom, left-aligned text, 240 px block right-aligned to the minimap, moves to y=28 when the minimap is hidden, no frame, mouse ignored, behind windows. All match the design doc.
