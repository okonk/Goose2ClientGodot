# Chat Window Tabs, Move, Resize Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Give the chat window filter tabs (All/Guild/Group + optional Chat/System + per-recipient tell tabs), make it movable by dragging the tab strip, and resizable from any edge or corner, with everything persisted per character.

**Architecture:** A pure `ChatLog` model owns tabs, routing, unread state and channel prefixing (unit-tested). `BaseWindow` gains opt-in resizing driven by a pure `WindowResize` helper (unit-tested). `ChatWindow` becomes a `BaseWindow` subclass and a thin view over `ChatLog`.

**Tech Stack:** Godot 4.6 C# (.NET 10), xUnit (`tests/Goose2Client.Tests`, which compiles `Scripts/**/*.cs` directly).

**Design doc:** `docs/plans/2026-09-24-chat-window-tabs-design.md`

**Code style (AGENTS.md):** do not add comments or doc strings to new or modified code unless the *why* is non-obvious. Leave unrelated existing comments alone; update or remove a comment on a line you touch if it becomes wrong.

---

## APIs verified

| API | Location |
|---|---|
| `ChatType` enum (Chat=1, Guild, Group, Melee, Spells, Tell, Server, Client=8), namespace `Goose2Client` | `Scripts/Constants.cs:108` |
| `GameColors.White/Yellow/Green/Red/Blue` (`Godot.Color`) | `Scripts/GameColors.cs:14-19` |
| `CharacterSettings` fields, `Load()` copy block, `ApplyDefaults()`, `Save()` | `Scripts/CharacterSettings.cs:50-140` |
| `SetWindowSetting(name, pos, size, factor, bool? visible, Vector2I canvas)` sets `Placed` | `Scripts/CharacterSettings.cs:202` |
| `WindowSettings { Position, Visible, CanvasSize, Size, Factor, Placed }` | `Scripts/CharacterSettings.cs:29-41` |
| `BaseWindow`: `_tscnSize`, `ScaleRegister()` (protected), `Relayout()` (virtual), `RepositionFromSaved()`, `MakeDragHandle(Control)` (protected), `OnTitleBarGuiInput`, `CancelDrag()`, `_Process` hover, `Toggle()`, `ResetToDefault()` (virtual), `Background` (protected) | `Scripts/UI/BaseWindow.cs` |
| `BaseWindow.BuildChrome` swaps a `TextureRect` named `Background` for a themed `WindowPanel` | `Scripts/UI/BaseWindow.cs:117-137` |
| Only external `CancelDrag()` caller: `UiScaleApplier.Apply` | `Scripts/UiScaleApplier.cs:143` |
| `UiScaleApplier.Instance`, `.Factor`, `.ScaleSize(float)`, `.ApplyFontSize(Control, float[, StringName])`; `Apply()` runs `Relayout()` on all windows then `RepositionFromSaved()` | `Scripts/UiScaleApplier.cs:18,60,64,97,100,132-175` |
| `UiScaleLayout.Snapshot/Apply` — non-container children get offsets scaled; container children get min size + `separation`/`h_separation`/`v_separation` scaled | `Scripts/UiScaleLayout.cs` |
| `WindowPlacement.ResolveScaled(...)`, `LegacyCanvas` | `Scripts/UI/WindowPlacement.cs:40` |
| `DefaultWindowLayout.Defaults` dictionary | `Scripts/UI/DefaultWindowLayout.cs:9` |
| `ChatCommandParser.Parse(input, aliases, handlerKeys)` | `Scripts/UI/ChatCommandParser.cs` |
| Tell echo `"[tell to] " + target.Name + ": " + text` (Tell-type) | `illutiagooseserver/Goose/Commands/TellCommand.cs` |
| `TellPacket { Name, IsAfk, Message }` | `Scripts/Network/Packets/TellPacket.cs` |
| GameHud uses `Chat.Toggle()`, `Chat.FocusChat(..)`, `Chat.ReplyToName`, `Chat.Typing` | `Scripts/UI/GameHud.cs:125-138` |
| Self-test chat asserts (paths `ChatLog`/`Input`, anchored offsets) | `Scripts/UiScaleSelfTest.cs:225-237` |
| Godot: `RichTextLabel.RemoveParagraph(int)`, `GetParagraphCount()`, `Clear()`; `PopupMenu.AddCheckItem(string,int,Key)`, `SetItemChecked(int,bool)`, `GetItemIndex(int)`, `IdPressed(long)`; `Control.CursorShape.Hsize/Vsize/Fdiagsize/Bdiagsize` | GodotSharp 4.6.2 `GodotSharp.xml` |
| Test settings subclass `TempCharacterSettings(dir)` (private nested) | `tests/Goose2Client.Tests/CharacterSettingsJsonTests.cs:330` |
| Viewport 1280×720, no stretch mode (viewport coords = canvas coords) | `project.godot:25-26` |

Commands used throughout:

- Unit tests: `dotnet test tests/Goose2Client.Tests`
- Filtered: `dotnet test tests/Goose2Client.Tests --filter FullyQualifiedName~ChatLogTests`
- Game build: `dotnet build Goose2ClientGodot.csproj`
- UI scale self-test (headless): `tools/tests/run_ui_scale.sh`

---

### Task 1: Persist enabled chat tabs in CharacterSettings

**Files:**
- Modify: `Scripts/CharacterSettings.cs`
- Test: `tests/Goose2Client.Tests/CharacterSettingsJsonTests.cs`

**Step 1: Write the failing tests** (add inside `CharacterSettingsJsonTests`, before the nested `TempCharacterSettings` class)

```csharp
        [Fact]
        public void ChatTabs_MissingField_DefaultsToGuildAndGroup()
        {
            var cs = CharacterSettings.FromJson("{}");
            Assert.Equal(new List<string> { "Guild", "Group" }, cs.ChatTabs);
        }

        [Fact]
        public void SetChatTabs_PersistsAcrossReload()
        {
            var dir = Path.Combine(Path.GetTempPath(), "gs2-" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            try
            {
                var cs = new TempCharacterSettings(dir);
                cs.SetChatTabs(new[] { "Group", "System" });

                var reloaded = new TempCharacterSettings(dir);
                Assert.True(reloaded.Load());
                Assert.Equal(new List<string> { "Group", "System" }, reloaded.ChatTabs);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void ResetWindowSettings_KeepsChatTabs()
        {
            var dir = Path.Combine(Path.GetTempPath(), "gs2-" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            try
            {
                var cs = new TempCharacterSettings(dir);
                cs.SetChatTabs(new[] { "Chat" });
                cs.ResetWindowSettings();
                Assert.Equal(new List<string> { "Chat" }, cs.ChatTabs);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
```

**Step 2: Run to verify failure**

Run: `dotnet test tests/Goose2Client.Tests --filter FullyQualifiedName~CharacterSettingsJsonTests`
Expected: build error — `CharacterSettings` has no `ChatTabs` / `SetChatTabs`.

**Step 3: Implement**

In `Scripts/CharacterSettings.cs`:

- Add a field after `public string MountName;`:
  ```csharp
        public List<string> ChatTabs;
  ```
- In `Load()`, after `this.MountName = deserialized.MountName;`:
  ```csharp
            this.ChatTabs = deserialized.ChatTabs;
  ```
- In `ApplyDefaults()`, after `Options ??= new();`:
  ```csharp
            ChatTabs ??= new List<string> { "Guild", "Group" };
  ```
- Add a method after `ResetWindowSettings()`:
  ```csharp
        public void SetChatTabs(IEnumerable<string> tabs)
        {
            ChatTabs = new List<string>(tabs);
            Save();
        }
  ```

**Step 4: Run to verify pass**

Run: `dotnet test tests/Goose2Client.Tests --filter FullyQualifiedName~CharacterSettingsJsonTests`
Expected: all PASS.

**Step 5: Commit**

```bash
git add Scripts/CharacterSettings.cs tests/Goose2Client.Tests/CharacterSettingsJsonTests.cs
git commit -m "feat: persist enabled chat tabs per character"
```

---

### Task 2: ChatLog model (tabs, routing, unread, channel)

**Files:**
- Create: `Scripts/UI/ChatLog.cs`
- Test: `tests/Goose2Client.Tests/ChatLogTests.cs`

**Step 1: Write the failing tests**

```csharp
using System.Collections.Generic;
using System.Linq;
using Goose2Client.UI;
using Xunit;

namespace Goose2Client.Tests;

public class ChatLogTests
{
    private static ChatLog NewLog(params ChatTabKind[] enabled) => new(enabled);

    private static ChatTab Tab(ChatLog log, ChatTabKind kind) => log.Tabs.Single(t => t.Kind == kind);

    private static ChatTab TellTab(ChatLog log, string name) =>
        log.Tabs.Single(t => t.Kind == ChatTabKind.Tell && t.Label == name);

    [Fact]
    public void Constructor_OrdersAllThenEnabledFixedTabs()
    {
        var log = NewLog(ChatTabKind.System, ChatTabKind.Guild);
        Assert.Equal(new[] { "All", "Guild", "System" }, log.Tabs.Select(t => t.Label));
        Assert.Equal(ChatTabKind.All, log.Active.Kind);
    }

    [Theory]
    [InlineData(ChatType.Guild, ChatTabKind.Guild)]
    [InlineData(ChatType.Group, ChatTabKind.Group)]
    [InlineData(ChatType.Chat, ChatTabKind.Chat)]
    [InlineData(ChatType.Server, ChatTabKind.System)]
    [InlineData(ChatType.Client, ChatTabKind.System)]
    public void Add_RoutesToAllAndMatchingTab(ChatType type, ChatTabKind kind)
    {
        var log = NewLog(ChatTabKind.Guild, ChatTabKind.Group, ChatTabKind.Chat, ChatTabKind.System);
        log.Add("hello", type);

        Assert.Single(Tab(log, ChatTabKind.All).Lines);
        foreach (var tab in log.Tabs.Where(t => t.Kind != ChatTabKind.All))
            Assert.Equal(tab.Kind == kind ? 1 : 0, tab.Lines.Count);
    }

    [Theory]
    [InlineData(ChatType.Melee)]
    [InlineData(ChatType.Spells)]
    public void Add_CombatGoesToAllOnly(ChatType type)
    {
        var log = NewLog(ChatTabKind.Guild, ChatTabKind.Group, ChatTabKind.Chat, ChatTabKind.System);
        log.Add("hit", type);
        Assert.Equal(1, log.Tabs.Sum(t => t.Lines.Count));
    }

    [Fact]
    public void Add_DisabledOptionalTab_LineOnlyInAll()
    {
        var log = NewLog(ChatTabKind.Guild);
        log.Add("server says", ChatType.Server);
        Assert.Single(Tab(log, ChatTabKind.All).Lines);
        Assert.Empty(Tab(log, ChatTabKind.Guild).Lines);
    }

    [Fact]
    public void Add_IncomingTell_OpensTabNamedForSender()
    {
        var log = NewLog();
        log.Add("[tell from] Bob: hi", ChatType.Tell, "Bob");
        Assert.Single(TellTab(log, "Bob").Lines);
        Assert.Single(Tab(log, ChatTabKind.All).Lines);
    }

    [Fact]
    public void Add_TellToEcho_RoutesToRecipientTab()
    {
        var log = NewLog();
        log.Add("[tell to] Bob: hey", ChatType.Tell);
        Assert.Single(TellTab(log, "Bob").Lines);
    }

    [Fact]
    public void Add_TellNamesMatchCaseInsensitivelyAndKeepFirstLabel()
    {
        var log = NewLog();
        log.Add("[tell from] Bob: hi", ChatType.Tell, "Bob");
        log.Add("[tell to] bob: hey", ChatType.Tell);
        var tells = log.Tabs.Where(t => t.Kind == ChatTabKind.Tell).ToList();
        Assert.Single(tells);
        Assert.Equal("Bob", tells[0].Label);
        Assert.Equal(2, tells[0].Lines.Count);
    }

    [Fact]
    public void Add_UnprefixedTell_GoesToActiveTellTab()
    {
        var log = NewLog();
        log.Add("[tell to] Bob: hey", ChatType.Tell);
        log.Activate(TellTab(log, "Bob"));
        log.Add("Bob is not online.", ChatType.Tell);
        Assert.Equal(2, TellTab(log, "Bob").Lines.Count);
    }

    [Fact]
    public void Add_UnprefixedTell_WithNonTellActive_OnlyInAll()
    {
        var log = NewLog();
        log.Add("Bob is not online.", ChatType.Tell);
        Assert.Single(log.Tabs);
        Assert.Single(Tab(log, ChatTabKind.All).Lines);
    }

    [Fact]
    public void Add_ToInactiveTab_SetsUnread_AllNeverUnread()
    {
        var log = NewLog(ChatTabKind.Guild);
        log.Activate(Tab(log, ChatTabKind.Guild));
        log.Add("say", ChatType.Chat);
        log.Add("[tell from] Bob: hi", ChatType.Tell, "Bob");

        Assert.False(Tab(log, ChatTabKind.All).Unread);
        Assert.True(TellTab(log, "Bob").Unread);
        Assert.False(Tab(log, ChatTabKind.Guild).Unread);
    }

    [Fact]
    public void Activate_ClearsUnreadAndRaisesEvents()
    {
        var log = NewLog(ChatTabKind.Guild);
        log.Add("g", ChatType.Guild);
        int activeChanged = 0;
        log.ActiveChanged += () => activeChanged++;

        log.Activate(Tab(log, ChatTabKind.Guild));

        Assert.False(Tab(log, ChatTabKind.Guild).Unread);
        Assert.Equal(ChatTabKind.Guild, log.Active.Kind);
        Assert.Equal(1, activeChanged);
    }

    [Fact]
    public void Add_ToActiveTab_RaisesActiveLineAdded()
    {
        var log = NewLog();
        var lines = new List<string>();
        log.ActiveLineAdded += lines.Add;
        log.Add("hello", ChatType.Chat);
        Assert.Single(lines);
    }

    [Fact]
    public void Close_ActiveTellTab_FallsBackToAll()
    {
        var log = NewLog();
        log.Add("[tell to] Bob: hey", ChatType.Tell);
        var bob = TellTab(log, "Bob");
        log.Activate(bob);

        log.Close(bob);

        Assert.DoesNotContain(bob, log.Tabs);
        Assert.Equal(ChatTabKind.All, log.Active.Kind);
    }

    [Fact]
    public void Close_ThenNewTell_ReopensEmptyTab()
    {
        var log = NewLog();
        log.Add("[tell to] Bob: one", ChatType.Tell);
        log.Close(TellTab(log, "Bob"));
        log.Add("[tell to] Bob: two", ChatType.Tell);
        Assert.Single(TellTab(log, "Bob").Lines);
    }

    [Fact]
    public void Close_IgnoresNonTellTabs()
    {
        var log = NewLog(ChatTabKind.Guild);
        log.Close(Tab(log, ChatTabKind.Guild));
        log.Close(Tab(log, ChatTabKind.All));
        Assert.Equal(2, log.Tabs.Count);
    }

    [Fact]
    public void SetEnabled_InsertsInFixedOrderBeforeTells()
    {
        var log = NewLog(ChatTabKind.Guild, ChatTabKind.System);
        log.Add("[tell to] Bob: hey", ChatType.Tell);

        log.SetEnabled(ChatTabKind.Group, true);

        Assert.Equal(new[] { "All", "Guild", "Group", "System", "Bob" }, log.Tabs.Select(t => t.Label));
    }

    [Fact]
    public void SetEnabled_DisablingActiveTab_FallsBackToAll()
    {
        var log = NewLog(ChatTabKind.Guild);
        log.Activate(Tab(log, ChatTabKind.Guild));
        log.SetEnabled(ChatTabKind.Guild, false);
        Assert.False(log.IsEnabled(ChatTabKind.Guild));
        Assert.Equal(ChatTabKind.All, log.Active.Kind);
    }

    [Fact]
    public void SetEnabled_IgnoresAllAndTell()
    {
        var log = NewLog();
        log.SetEnabled(ChatTabKind.All, false);
        log.SetEnabled(ChatTabKind.Tell, true);
        Assert.Single(log.Tabs);
    }

    [Fact]
    public void EnabledKinds_ReflectsFixedOrder()
    {
        var log = NewLog(ChatTabKind.System, ChatTabKind.Guild);
        Assert.Equal(new[] { ChatTabKind.Guild, ChatTabKind.System }, log.EnabledKinds);
    }

    [Fact]
    public void Buffer_CapsAtMaxLines_DroppingOldest()
    {
        var log = NewLog();
        for (int i = 0; i < ChatLog.MaxLines + 5; i++)
            log.Add($"line {i}", ChatType.Chat);
        var all = Tab(log, ChatTabKind.All).Lines;
        Assert.Equal(ChatLog.MaxLines, all.Count);
        Assert.Contains("line 5[", all[0]);
    }

    [Theory]
    [InlineData(ChatTabKind.All, "hi", "hi")]
    [InlineData(ChatTabKind.Chat, "hi", "hi")]
    [InlineData(ChatTabKind.System, "hi", "hi")]
    [InlineData(ChatTabKind.Guild, "hi", "/guild hi")]
    [InlineData(ChatTabKind.Group, "hi", "/group hi")]
    [InlineData(ChatTabKind.Guild, "/who", "/who")]
    [InlineData(ChatTabKind.Guild, "", "")]
    public void ApplyChannel_PrefixesByActiveTab(ChatTabKind kind, string input, string expected)
    {
        var log = NewLog(ChatTabKind.Guild, ChatTabKind.Group, ChatTabKind.Chat, ChatTabKind.System);
        log.Activate(Tab(log, kind));
        Assert.Equal(expected, log.ApplyChannel(input));
    }

    [Fact]
    public void ApplyChannel_TellTab_PrefixesTellWithName()
    {
        var log = NewLog();
        log.Add("[tell from] Bob: hi", ChatType.Tell, "Bob");
        log.Activate(TellTab(log, "Bob"));
        Assert.Equal("/tell Bob yo", log.ApplyChannel("yo"));
        Assert.Equal("Tell Bob", log.ChannelName);
    }

    [Theory]
    [InlineData(ChatTabKind.All, "Say")]
    [InlineData(ChatTabKind.Guild, "Guild")]
    [InlineData(ChatTabKind.Group, "Group")]
    public void ChannelName_ByActiveTab(ChatTabKind kind, string expected)
    {
        var log = NewLog(ChatTabKind.Guild, ChatTabKind.Group);
        log.Activate(Tab(log, kind));
        Assert.Equal(expected, log.ChannelName);
    }

    [Fact]
    public void Format_EscapesBbcodeAndHeartAndColors()
    {
        Assert.Equal("[color=#f8d000]a [lb]b] ♥[/color]", ChatLog.Format("a [b] `", ChatType.Guild));
    }

    [Fact]
    public void ParseKinds_KeepsOnlyKnownOptionalKinds()
    {
        var kinds = ChatLog.ParseKinds(new[] { "Group", "All", "Tell", "Bogus", "system" });
        Assert.Equal(new[] { ChatTabKind.Group, ChatTabKind.System }, kinds);
    }
}
```

Note: `GameColors.Yellow` = `Rgb(248, 208, 0)` → `ToHtml(false)` = `f8d000`.

**Step 2: Run to verify failure**

Run: `dotnet test tests/Goose2Client.Tests --filter FullyQualifiedName~ChatLogTests`
Expected: build error — `ChatLog`, `ChatTab`, `ChatTabKind` not found.

**Step 3: Implement `Scripts/UI/ChatLog.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Goose2Client.UI;

public enum ChatTabKind { All, Guild, Group, Chat, System, Tell }

public sealed class ChatTab
{
    internal readonly List<string> Buffer = new();

    internal ChatTab(ChatTabKind kind, string label)
    {
        Kind = kind;
        Label = label;
    }

    public ChatTabKind Kind { get; }
    public string Label { get; }
    public bool Unread { get; internal set; }
    public IReadOnlyList<string> Lines => Buffer;
}

public sealed class ChatLog
{
    public const int MaxLines = 500;

    // Server echo for outgoing tells (illutiagooseserver TellCommand): "[tell to] <Name>: <text>".
    private const string TellToPrefix = "[tell to] ";

    public static readonly ChatTabKind[] OptionalKinds =
        { ChatTabKind.Guild, ChatTabKind.Group, ChatTabKind.Chat, ChatTabKind.System };

    private static readonly Dictionary<ChatType, Color> TypeColors = new()
    {
        [ChatType.Chat] = GameColors.White,
        [ChatType.Guild] = GameColors.Yellow,
        [ChatType.Group] = GameColors.Green,
        [ChatType.Melee] = GameColors.Red,
        [ChatType.Spells] = GameColors.Blue,
        [ChatType.Tell] = GameColors.Blue,
        [ChatType.Server] = GameColors.Green,
    };

    private readonly List<ChatTab> _tabs = new();

    public event Action TabsChanged;
    public event Action ActiveChanged;
    public event Action<string> ActiveLineAdded;

    public ChatLog(IEnumerable<ChatTabKind> enabled)
    {
        _tabs.Add(new ChatTab(ChatTabKind.All, "All"));
        var set = enabled.ToHashSet();
        foreach (var kind in OptionalKinds)
            if (set.Contains(kind))
                _tabs.Add(new ChatTab(kind, kind.ToString()));
        Active = _tabs[0];
    }

    public IReadOnlyList<ChatTab> Tabs => _tabs;
    public ChatTab Active { get; private set; }
    public IEnumerable<ChatTabKind> EnabledKinds => OptionalKinds.Where(IsEnabled);

    public string ChannelName => Active.Kind switch
    {
        ChatTabKind.Guild => "Guild",
        ChatTabKind.Group => "Group",
        ChatTabKind.Tell => $"Tell {Active.Label}",
        _ => "Say"
    };

    public bool IsEnabled(ChatTabKind kind) => _tabs.Exists(t => t.Kind == kind);

    public static IEnumerable<ChatTabKind> ParseKinds(IEnumerable<string> names)
    {
        foreach (var name in names)
            if (Enum.TryParse<ChatTabKind>(name, true, out var kind) && Array.IndexOf(OptionalKinds, kind) >= 0)
                yield return kind;
    }

    public static string Format(string message, ChatType type)
    {
        message = message.Replace("[", "[lb]").Replace('`', '♥');
        var color = TypeColors.TryGetValue(type, out var c) ? c : GameColors.White;
        return $"[color=#{color.ToHtml(false)}]{message}[/color]";
    }

    public void Add(string message, ChatType type, string tellName = null)
    {
        var line = Format(message, type);
        Append(_tabs[0], line);
        var target = Route(message, type, tellName);
        if (target != null)
            Append(target, line);
    }

    public void Activate(ChatTab tab)
    {
        if (tab == Active || !_tabs.Contains(tab))
            return;
        Active = tab;
        tab.Unread = false;
        ActiveChanged?.Invoke();
        TabsChanged?.Invoke();
    }

    public void Close(ChatTab tab)
    {
        if (tab.Kind != ChatTabKind.Tell || !_tabs.Remove(tab))
            return;
        if (tab == Active)
        {
            Active = _tabs[0];
            ActiveChanged?.Invoke();
        }
        TabsChanged?.Invoke();
    }

    public void SetEnabled(ChatTabKind kind, bool enabled)
    {
        if (Array.IndexOf(OptionalKinds, kind) < 0 || IsEnabled(kind) == enabled)
            return;

        if (enabled)
        {
            int index = 1 + OptionalKinds.TakeWhile(k => k != kind).Count(IsEnabled);
            _tabs.Insert(index, new ChatTab(kind, kind.ToString()));
        }
        else
        {
            var tab = _tabs.Find(t => t.Kind == kind);
            _tabs.Remove(tab);
            if (tab == Active)
            {
                Active = _tabs[0];
                ActiveChanged?.Invoke();
            }
        }
        TabsChanged?.Invoke();
    }

    public string ApplyChannel(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text[0] == '/')
            return text;
        return Active.Kind switch
        {
            ChatTabKind.Guild => "/guild " + text,
            ChatTabKind.Group => "/group " + text,
            ChatTabKind.Tell => $"/tell {Active.Label} {text}",
            _ => text
        };
    }

    private ChatTab Route(string message, ChatType type, string tellName)
    {
        if (type == ChatType.Tell)
        {
            tellName ??= ParseTellTo(message);
            if (tellName != null)
                return GetOrOpenTell(tellName);
            return Active.Kind == ChatTabKind.Tell ? Active : null;
        }

        var kind = type switch
        {
            ChatType.Guild => ChatTabKind.Guild,
            ChatType.Group => ChatTabKind.Group,
            ChatType.Chat => ChatTabKind.Chat,
            ChatType.Server or ChatType.Client => ChatTabKind.System,
            _ => ChatTabKind.All
        };
        return kind == ChatTabKind.All ? null : _tabs.Find(t => t.Kind == kind);
    }

    private static string ParseTellTo(string message)
    {
        if (!message.StartsWith(TellToPrefix, StringComparison.Ordinal))
            return null;
        int colon = message.IndexOf(": ", TellToPrefix.Length, StringComparison.Ordinal);
        return colon > TellToPrefix.Length ? message.Substring(TellToPrefix.Length, colon - TellToPrefix.Length) : null;
    }

    private ChatTab GetOrOpenTell(string name)
    {
        var tab = _tabs.Find(t => t.Kind == ChatTabKind.Tell && string.Equals(t.Label, name, StringComparison.OrdinalIgnoreCase));
        if (tab != null)
            return tab;
        tab = new ChatTab(ChatTabKind.Tell, name);
        _tabs.Add(tab);
        TabsChanged?.Invoke();
        return tab;
    }

    private void Append(ChatTab tab, string line)
    {
        tab.Buffer.Add(line);
        if (tab.Buffer.Count > MaxLines)
            tab.Buffer.RemoveAt(0);

        if (tab == Active)
        {
            ActiveLineAdded?.Invoke(line);
            return;
        }
        if (tab.Kind == ChatTabKind.All || tab.Unread)
            return;
        tab.Unread = true;
        TabsChanged?.Invoke();
    }
}
```

**Step 4: Run to verify pass**

Run: `dotnet test tests/Goose2Client.Tests --filter FullyQualifiedName~ChatLogTests`
Expected: all PASS.

**Step 5: Commit**

```bash
git add Scripts/UI/ChatLog.cs tests/Goose2Client.Tests/ChatLogTests.cs
git commit -m "feat: add ChatLog tab model with routing and channel prefixing"
```

---

### Task 3: WindowResize pure helper

**Files:**
- Create: `Scripts/UI/WindowResize.cs`
- Test: `tests/Goose2Client.Tests/WindowResizeTests.cs`

**Step 1: Write the failing tests**

```csharp
using Godot;
using Goose2Client.UI;
using Xunit;

namespace Goose2Client.Tests;

public class WindowResizeTests
{
    private static readonly Rect2 Start = new(100, 100, 400, 200);
    private static readonly Vector2 Min = new(260, 110);
    private static readonly Vector2 Canvas = new(1280, 720);

    [Theory]
    [InlineData(ResizeEdge.Right, 50, 0, 100, 100, 450, 200)]
    [InlineData(ResizeEdge.Left, -30, 0, 70, 100, 430, 200)]
    [InlineData(ResizeEdge.Bottom, 0, 40, 100, 100, 400, 240)]
    [InlineData(ResizeEdge.Top, 0, -40, 100, 60, 400, 240)]
    [InlineData(ResizeEdge.Top | ResizeEdge.Right, 50, -20, 100, 80, 450, 220)]
    [InlineData(ResizeEdge.Bottom | ResizeEdge.Left, -10, 10, 90, 100, 410, 210)]
    public void Apply_MovesOnlyTheDraggedEdges(ResizeEdge edge, float dx, float dy, float x, float y, float w, float h)
    {
        Assert.Equal(new Rect2(x, y, w, h), WindowResize.Apply(Start, edge, new Vector2(dx, dy), Min, Canvas));
    }

    [Fact]
    public void Apply_LeftPastMinimum_PinsRightEdge()
    {
        Assert.Equal(new Rect2(240, 100, 260, 200), WindowResize.Apply(Start, ResizeEdge.Left, new Vector2(200, 0), Min, Canvas));
    }

    [Fact]
    public void Apply_RightPastMinimum_ClampsToMinWidth()
    {
        Assert.Equal(new Rect2(100, 100, 260, 200), WindowResize.Apply(Start, ResizeEdge.Right, new Vector2(-500, 0), Min, Canvas));
    }

    [Fact]
    public void Apply_TopBeyondCanvas_ClampsToZero()
    {
        Assert.Equal(new Rect2(100, 0, 400, 300), WindowResize.Apply(Start, ResizeEdge.Top, new Vector2(0, -150), Min, Canvas));
    }

    [Fact]
    public void Apply_BottomBeyondCanvas_ClampsToCanvas()
    {
        Assert.Equal(new Rect2(100, 100, 400, 620), WindowResize.Apply(Start, ResizeEdge.Bottom, new Vector2(0, 1000), Min, Canvas));
    }

    [Fact]
    public void ScaledSize_RescalesSavedSizeByFactorRatio()
    {
        Assert.Equal(new Vector2(800, 400), WindowResize.ScaledSize(new Vector2(600, 300), 1.5f, 2f, Min, Canvas));
    }

    [Fact]
    public void ScaledSize_ZeroSavedFactor_TreatedAsOne()
    {
        Assert.Equal(new Vector2(1000, 416), WindowResize.ScaledSize(new Vector2(500, 208), 0f, 2f, Min, Canvas));
    }

    [Fact]
    public void ScaledSize_ClampsToMinAndCanvas()
    {
        Assert.Equal(new Vector2(260, 110), WindowResize.ScaledSize(new Vector2(100, 50), 1f, 1f, Min, Canvas));
        Assert.Equal(new Vector2(1280, 720), WindowResize.ScaledSize(new Vector2(2000, 1000), 1f, 1f, Min, Canvas));
    }
}
```

**Step 2: Run to verify failure**

Run: `dotnet test tests/Goose2Client.Tests --filter FullyQualifiedName~WindowResizeTests`
Expected: build error — `WindowResize` / `ResizeEdge` not found.

**Step 3: Implement `Scripts/UI/WindowResize.cs`**

```csharp
using System;
using Godot;

namespace Goose2Client.UI;

[Flags]
public enum ResizeEdge { None = 0, Left = 1, Top = 2, Right = 4, Bottom = 8 }

public static class WindowResize
{
    public static Rect2 Apply(Rect2 start, ResizeEdge edge, Vector2 delta, Vector2 minSize, Vector2 canvas)
    {
        float left = start.Position.X, top = start.Position.Y, right = start.End.X, bottom = start.End.Y;
        if (edge.HasFlag(ResizeEdge.Left))
            left = Mathf.Clamp(left + delta.X, 0f, right - minSize.X);
        if (edge.HasFlag(ResizeEdge.Right))
            right = Mathf.Clamp(right + delta.X, left + minSize.X, canvas.X);
        if (edge.HasFlag(ResizeEdge.Top))
            top = Mathf.Clamp(top + delta.Y, 0f, bottom - minSize.Y);
        if (edge.HasFlag(ResizeEdge.Bottom))
            bottom = Mathf.Clamp(bottom + delta.Y, top + minSize.Y, canvas.Y);
        return new Rect2(left, top, right - left, bottom - top);
    }

    public static Vector2 ScaledSize(Vector2 savedSize, float savedFactor, float factor, Vector2 minSize, Vector2 canvas)
    {
        var size = (savedSize * (factor / (savedFactor > 0f ? savedFactor : 1f))).Round();
        return new Vector2(
            Mathf.Clamp(size.X, minSize.X, Mathf.Max(minSize.X, canvas.X)),
            Mathf.Clamp(size.Y, minSize.Y, Mathf.Max(minSize.Y, canvas.Y)));
    }
}
```

**Step 4: Run to verify pass**

Run: `dotnet test tests/Goose2Client.Tests --filter FullyQualifiedName~WindowResizeTests`
Expected: all PASS.

**Step 5: Commit**

```bash
git add Scripts/UI/WindowResize.cs tests/Goose2Client.Tests/WindowResizeTests.cs
git commit -m "feat: add WindowResize rect and saved-size math"
```

---

### Task 4: Opt-in resizing and hover-opacity hook in BaseWindow

No unit test is possible for the Godot node code; the math is covered by Task 3, and behavior is verified by the self-test (Task 7) and manual checks. Non-resizable windows must behave exactly as before.

**Files:**
- Modify: `Scripts/UI/BaseWindow.cs`

**Step 1: Add members** (near the other private fields / constants)

```csharp
    private const float ResizeEdgeThickness = 5f;
    private const float ResizeCornerSize = 10f;

    private ResizeEdge _resizeEdge;
    private Rect2 _preResizeRect;
    private Vector2 _resizeStartMouse;

    protected virtual bool Resizable => false;
    protected virtual Vector2 MinResizeSize => Vector2.Zero;
```

**Step 2: Hover-opacity hook.** Add:

```csharp
    protected virtual void ApplyHoverOpacity(float alpha) => Modulate = new Color(1, 1, 1, alpha);
```

Replace `Modulate = new Color(1, 1, 1, UnhoveredOpacity);` in `_Ready` with `ApplyHoverOpacity(UnhoveredOpacity);`, and in `_Process` replace `Modulate = new Color(1, 1, 1, inside ? HoverOpacity : UnhoveredOpacity);` with `ApplyHoverOpacity(inside ? HoverOpacity : UnhoveredOpacity);`.

**Step 3: Build handles in `_Ready`.** Directly after the block that moves the title bar to the top (`if (_titleBar != null) MoveChild(_titleBar, GetChildCount() - 1);`) and before `_chromeReady = true;`:

```csharp
        if (Resizable)
            BuildResizeHandles();
```

Add the methods:

```csharp
    private void BuildResizeHandles()
    {
        AddResizeHandle(ResizeEdge.Left, CursorShape.Hsize);
        AddResizeHandle(ResizeEdge.Right, CursorShape.Hsize);
        AddResizeHandle(ResizeEdge.Top, CursorShape.Vsize);
        AddResizeHandle(ResizeEdge.Bottom, CursorShape.Vsize);
        AddResizeHandle(ResizeEdge.Top | ResizeEdge.Left, CursorShape.Fdiagsize);
        AddResizeHandle(ResizeEdge.Bottom | ResizeEdge.Right, CursorShape.Fdiagsize);
        AddResizeHandle(ResizeEdge.Top | ResizeEdge.Right, CursorShape.Bdiagsize);
        AddResizeHandle(ResizeEdge.Bottom | ResizeEdge.Left, CursorShape.Bdiagsize);
    }

    private void AddResizeHandle(ResizeEdge edge, CursorShape cursor)
    {
        bool left = edge.HasFlag(ResizeEdge.Left), right = edge.HasFlag(ResizeEdge.Right);
        bool top = edge.HasFlag(ResizeEdge.Top), bottom = edge.HasFlag(ResizeEdge.Bottom);
        float span = (left || right) && (top || bottom) ? ResizeCornerSize : ResizeEdgeThickness;

        var handle = new Control { Name = $"Resize{edge}", MouseFilter = MouseFilterEnum.Stop, MouseDefaultCursorShape = cursor };
        handle.AnchorLeft = right ? 1f : 0f;
        handle.AnchorRight = left ? 0f : 1f;
        handle.AnchorTop = bottom ? 1f : 0f;
        handle.AnchorBottom = top ? 0f : 1f;
        handle.OffsetLeft = left ? 0f : right ? -span : ResizeCornerSize;
        handle.OffsetRight = left ? span : right ? 0f : -ResizeCornerSize;
        handle.OffsetTop = top ? 0f : bottom ? -span : ResizeCornerSize;
        handle.OffsetBottom = top ? span : bottom ? 0f : -ResizeCornerSize;
        handle.GuiInput += e => OnResizeHandleGuiInput(e, edge);
        AddChild(handle);
    }

    private void OnResizeHandleGuiInput(InputEvent @event, ResizeEdge edge)
    {
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left } mb)
        {
            if (mb.Pressed)
            {
                _dragCancelled = false;
                _resizeEdge = edge;
                _preResizeRect = new Rect2(Position, Size);
                _resizeStartMouse = GetGlobalMousePosition();
            }
            else if (_resizeEdge != ResizeEdge.None)
            {
                EndResize();
            }
        }
        else if (@event is InputEventMouseMotion && _resizeEdge != ResizeEdge.None)
        {
            if (!Input.IsMouseButtonPressed(MouseButton.Left))
            {
                EndResize();
                return;
            }
            var rect = WindowResize.Apply(_preResizeRect, _resizeEdge, GetGlobalMousePosition() - _resizeStartMouse,
                MinResizeSize * UiScaleApplier.Instance.Factor, GetTree().Root.GetVisibleRect().Size);
            Position = rect.Position;
            Size = rect.Size;
        }
    }

    private void EndResize()
    {
        _resizeEdge = ResizeEdge.None;
        if (_dragCancelled)
            _dragCancelled = false;
        else
            SavePlacement();
    }

    private Vector2 ResizableSize()
    {
        var factor = UiScaleApplier.Instance.Factor;
        var ws = GameManager.Instance?.CharacterSettings?.GetWindowSettings(WindowName);
        bool saved = ws != null && ws.Placed && ws.Size != default;
        return WindowResize.ScaledSize(saved ? ws.Size : _tscnSize, saved ? ws.Factor : 1f, factor,
            MinResizeSize * factor, GetTree().Root.GetVisibleRect().Size);
    }
```

**Step 4: Extract the shared save.** Add:

```csharp
    private void SavePlacement()
    {
        if (WindowName == null) return;
        // Live Visible, not null: a fresh WindowSettings entry defaults to Visible=false,
        // and a null here would persist that and hide the window on next login.
        GameManager.Instance.CharacterSettings.SetWindowSetting(WindowName, Position, Size, UiScaleApplier.Instance != null ? UiScaleApplier.Instance.Factor : 1f, Visible, (Vector2I)GetTree().Root.GetVisibleRect().Size);
    }
```

In `OnTitleBarGuiInput`, replace both `else if (WindowName != null) ... SetWindowSetting(...)` branches (the release branch including its two-line comment, and the motion-without-button branch) with `else SavePlacement();`.

**Step 5: Size from saved on relayout and reset.**

```csharp
    public virtual void Relayout()
    {
        UiScaleLayout.Apply(_geom, UiScaleApplier.Instance.Factor);
        if (Resizable)
            Size = ResizableSize();
    }
```

```csharp
    public virtual void ResetToDefault()
    {
        Visible = DefaultVisible;
        if (Resizable)
            Size = ResizableSize();
        RepositionFromSaved();
    }
```

**Step 6: Cancel restores the pre-resize rect.** Replace `CancelDrag` with:

```csharp
    public void CancelDrag()
    {
        if (_resizeEdge != ResizeEdge.None)
        {
            _resizeEdge = ResizeEdge.None;
            _dragCancelled = true;
            Position = _preResizeRect.Position;
            Size = _preResizeRect.Size;
            return;
        }
        if (!_dragging) return;
        _dragging = false;
        _dragCancelled = true;
        Position = _preDragPosition;
    }
```

**Step 7: Build and run all tests**

Run: `dotnet build Goose2ClientGodot.csproj && dotnet test tests/Goose2Client.Tests`
Expected: `0 Error(s)`; all tests PASS (no behavior change for existing windows).

**Step 8: Commit**

```bash
git add Scripts/UI/BaseWindow.cs
git commit -m "feat: add opt-in edge and corner resizing to BaseWindow"
```

---

### Task 5: Default chat placement

**Files:**
- Modify: `Scripts/UI/DefaultWindowLayout.cs`
- Test: `tests/Goose2Client.Tests/WindowPlacementTests.cs`

**Step 1: Write the failing test** (add to `WindowPlacementTests`)

```csharp
    [Fact]
    public void ChatDefault_StaysBottomLeftOn1080()
    {
        var size = new Vector2(500, 208);
        var pos = WindowPlacement.ResolveScaled(DefaultWindowLayout.For("Chat"), size, 1f, C720, size, 1f, C1080);
        Assert.Equal(new Vector2(8, 1080 - 208 - 5), pos);
    }
```

**Step 2: Run to verify failure**

Run: `dotnet test tests/Goose2Client.Tests --filter FullyQualifiedName~ChatDefault`
Expected: FAIL — `For("Chat")` returns the fallback (100,100).

**Step 3: Implement.** In `DefaultWindowLayout.Defaults` add:

```csharp
        ["Chat"]      = new Vector2(8, 507),
```

**Step 4: Run to verify pass**

Run: `dotnet test tests/Goose2Client.Tests --filter FullyQualifiedName~WindowPlacementTests`
Expected: all PASS.

**Step 5: Commit**

```bash
git add Scripts/UI/DefaultWindowLayout.cs tests/Goose2Client.Tests/WindowPlacementTests.cs
git commit -m "feat: default chat window placement bottom-left"
```

---

### Task 6: Convert ChatWindow to a tabbed, resizable BaseWindow

**Files:**
- Modify: `Scenes/UI/ChatWindow.tscn` (full rewrite)
- Modify: `Scripts/UI/ChatWindow.cs` (full rewrite)

`Assets/UI/chat.png` is no longer referenced by the scene; leave the asset file in place (removal is out of scope).

**Step 1: Rewrite `Scenes/UI/ChatWindow.tscn`**

```
[gd_scene load_steps=4 format=3]

[ext_resource type="Script" path="res://Scripts/UI/ChatWindow.cs" id="1_chat"]
[ext_resource type="Theme" path="res://Assets/UI/GameTheme.tres" id="theme"]

[sub_resource type="StyleBoxEmpty" id="chat_log_transparent"]

[node name="ChatWindow" type="Control"]
script = ExtResource("1_chat")
theme = ExtResource("theme")
layout_mode = 3
offset_right = 500.0
offset_bottom = 208.0
WindowName = "Chat"

[node name="Background" type="TextureRect" parent="."]
layout_mode = 1
anchors_preset = 15
anchor_right = 1.0
anchor_bottom = 1.0
mouse_filter = 2

[node name="Content" type="VBoxContainer" parent="."]
layout_mode = 1
anchors_preset = 15
anchor_right = 1.0
anchor_bottom = 1.0
offset_left = 4.0
offset_top = 4.0
offset_right = -4.0
offset_bottom = -4.0
theme_override_constants/separation = 2

[node name="TabRow" type="HBoxContainer" parent="Content"]
layout_mode = 2
theme_override_constants/separation = 2

[node name="TabScroll" type="ScrollContainer" parent="Content/TabRow"]
layout_mode = 2
size_flags_horizontal = 3

[node name="Tabs" type="HBoxContainer" parent="Content/TabRow/TabScroll"]
layout_mode = 2
size_flags_horizontal = 3
theme_override_constants/separation = 2

[node name="DragFiller" type="Control" parent="Content/TabRow/TabScroll/Tabs"]
custom_minimum_size = Vector2(24, 18)
layout_mode = 2
size_flags_horizontal = 3

[node name="ScrollLeft" type="Button" parent="Content/TabRow"]
visible = false
layout_mode = 2
focus_mode = 0
text = "<"

[node name="ScrollRight" type="Button" parent="Content/TabRow"]
visible = false
layout_mode = 2
focus_mode = 0
text = ">"

[node name="ChatLog" type="RichTextLabel" parent="Content"]
layout_mode = 2
size_flags_vertical = 3
mouse_filter = 2
theme_override_styles/normal = SubResource("chat_log_transparent")
bbcode_enabled = true
scroll_following = true

[node name="Input" type="LineEdit" parent="Content"]
layout_mode = 2
```

(`Background` is an empty `TextureRect` on purpose: `BaseWindow.BuildChrome` replaces it with the themed `WindowPanel`, the same convention as `Scenes/UI/HairdyeWindow.tscn`. Scroll modes are set in code, not the tscn, so the enum values are explicit. `Tabs` has `size_flags_horizontal = 3` so the ScrollContainer stretches it to full width when tabs fit, which lets `DragFiller` take the empty space.)

**Step 2: Rewrite `Scripts/UI/ChatWindow.cs`**

Keep the alias table, command handlers, history navigation, `FocusChat`, `ClearAndUnfocus`, `OnQuitCommand`, `OnHairdyeCommand` exactly as today. Remove: `_panel`, `_geom`, `_chatColors`, `SetAlpha`, `Relayout`, `Toggle` (inherited from `BaseWindow`, which also persists visibility), `IScalableWindow`.

```csharp
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Goose2Client;
using Goose2Client.Network.Packets;

namespace Goose2Client.UI;

/// <summary>
/// Chat window: tabbed log (<see cref="ChatLog"/>) plus input line. Dragged by the tab strip's
/// empty space and resizable from any edge. GameHud routes focus actions here via FocusChat().
/// </summary>
public partial class ChatWindow : BaseWindow
{
    private static readonly Vector2 MinSize = new(260, 110);
    private const float TabScrollStep = 60f;

    private RichTextLabel _chatLog;
    private LineEdit _input;
    private ScrollContainer _tabScroll;
    private HBoxContainer _tabStrip;
    private Control _dragFiller;
    private Button _scrollLeft;
    private Button _scrollRight;
    private PopupMenu _tabMenu;
    private ChatLog _log;
    private bool _tabsDirty;

    private bool _listenersRegistered;

    protected override bool Resizable => true;
    protected override Vector2 MinResizeSize => MinSize;

    public bool Typing => _input.HasFocus();
    public string ReplyToName { get; private set; }

    private readonly Dictionary<string, string> _aliases = new();
    private readonly Dictionary<string, Action<string, string>> _commandHandlers = new();
    private readonly List<string> _inputHistory = new();
    private int _historyIndex = 0;

    public override void _Ready()
    {
        base._Ready();

        _chatLog = GetNode<RichTextLabel>("Content/ChatLog");
        _input = GetNode<LineEdit>("Content/Input");
        _tabScroll = GetNode<ScrollContainer>("Content/TabRow/TabScroll");
        _tabStrip = GetNode<HBoxContainer>("Content/TabRow/TabScroll/Tabs");
        _dragFiller = GetNode<Control>("Content/TabRow/TabScroll/Tabs/DragFiller");
        _scrollLeft = GetNode<Button>("Content/TabRow/ScrollLeft");
        _scrollRight = GetNode<Button>("Content/TabRow/ScrollRight");
        MakeDragHandle(_dragFiller);
        _dragFiller.GuiInput += OnTabStripGuiInput;

        _tabScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.ShowNever;
        _tabScroll.VerticalScrollMode = ScrollContainer.ScrollMode.Disabled;
        var bar = _tabScroll.GetHScrollBar();
        bar.Changed += UpdateScrollButtons;
        bar.ValueChanged += _ => UpdateScrollButtons();
        _scrollLeft.Pressed += () => ScrollTabs(-1);
        _scrollRight.Pressed += () => ScrollTabs(1);

        _log = new ChatLog(ChatLog.ParseKinds(GameManager.Instance.CharacterSettings.ChatTabs));
        _log.TabsChanged += QueueRebuildTabs;
        _log.ActiveChanged += OnActiveChanged;
        _log.ActiveLineAdded += AppendToView;
        BuildTabMenu();
        RebuildTabs();

        // Register packet listeners
        GameManager.Instance.PacketManager.Listen<ChatPacket>(OnChat);
        GameManager.Instance.PacketManager.Listen<HashMessagePacket>(OnHashMessage);
        GameManager.Instance.PacketManager.Listen<ServerMessagePacket>(OnServerMessage);
        GameManager.Instance.PacketManager.Listen<TellPacket>(OnTell);
        _listenersRegistered = true;

        // Input signals
        _input.TextSubmitted += OnTextSubmitted;
        _input.GuiInput += OnInputGuiInput;

        // Populate aliases (lowercase keys)
        _aliases["/t"] = "/tell";
        _aliases["/ga"] = "/groupadd";
        _aliases["/gr"] = "/groupremove";
        _aliases["/gu"] = "/guild";
        _aliases["/g"] = "/group";
        _aliases["/"] = "/who";
        _aliases["/r"] = "/random 1000";
        _aliases["/h"] = "Hello there!";

        // Command handlers
        _commandHandlers["/quit"] = OnQuitCommand;
        _commandHandlers["/hairdye"] = OnHairdyeCommand;

        var applier = UiScaleApplier.Instance;
        applier.ApplyFontSize(_chatLog, 12, new StringName("normal_font_size"));
        applier.ApplyFontSize(_input, 12);
        ScaleRegister();
    }

    // Fade only the frame: the log text stays fully readable when the cursor is elsewhere.
    protected override void ApplyHoverOpacity(float alpha) => Background.Modulate = new Color(1, 1, 1, alpha);

    public override void _ExitTree()
    {
        if (!_listenersRegistered) return;
        GameManager.Instance.PacketManager.Remove<ChatPacket>(OnChat);
        GameManager.Instance.PacketManager.Remove<HashMessagePacket>(OnHashMessage);
        GameManager.Instance.PacketManager.Remove<ServerMessagePacket>(OnServerMessage);
        GameManager.Instance.PacketManager.Remove<TellPacket>(OnTell);
    }

    private void OnChat(object o)
    {
        var p = (ChatPacket)o;
        AddChatLine(p.Message, ChatType.Chat);
    }

    private void OnHashMessage(object o)
    {
        var p = (HashMessagePacket)o;
        AddChatLine(p.Message, ChatType.Chat);
    }

    private void OnServerMessage(object o)
    {
        var p = (ServerMessagePacket)o;
        AddChatLine(p.Message, p.ChatType);
    }

    private void OnTell(object o)
    {
        var p = (TellPacket)o;
        ReplyToName = p.Name;
        _log.Add($"[tell from] {p.Name}: {p.Message}", ChatType.Tell, p.Name);
    }

    public void AddChatLine(string message, ChatType chatType) => _log.Add(message, chatType);

    private void OnActiveChanged()
    {
        RenderActive();
        ScrollActiveTabIntoView();
    }

    // EnsureControlVisible needs the rebuilt tab row laid out, which happens next frame.
    private async void ScrollActiveTabIntoView()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!IsInstanceValid(this)) return;
        foreach (Node child in _tabStrip.GetChildren())
            if (child is Button { ButtonPressed: true } active)
                _tabScroll.EnsureControlVisible(active);
    }

    private void ScrollTabs(int direction)
    {
        _tabScroll.ScrollHorizontal += direction * UiScaleApplier.Instance.ScaleSize(TabScrollStep);
    }

    private void UpdateScrollButtons()
    {
        var bar = _tabScroll.GetHScrollBar();
        bool overflow = bar.MaxValue > bar.Page;
        _scrollLeft.Visible = overflow;
        _scrollRight.Visible = overflow;
        _scrollLeft.Disabled = bar.Value <= 0;
        _scrollRight.Disabled = bar.Value >= bar.MaxValue - bar.Page;
    }

    private void RenderActive()
    {
        _chatLog.Clear();
        foreach (var line in _log.Active.Lines)
            _chatLog.AppendText(line + "\n");
    }

    private void AppendToView(string line)
    {
        _chatLog.AppendText(line + "\n");
        // +1: the trailing "\n" leaves an empty last paragraph.
        while (_chatLog.GetParagraphCount() > ChatLog.MaxLines + 1)
            _chatLog.RemoveParagraph(0);
    }

    // Deferred: a tab button's Pressed handler triggers the rebuild that frees that button.
    private void QueueRebuildTabs()
    {
        if (_tabsDirty) return;
        _tabsDirty = true;
        Callable.From(RebuildTabs).CallDeferred();
    }

    private void RebuildTabs()
    {
        _tabsDirty = false;
        foreach (Node child in _tabStrip.GetChildren())
        {
            if (child == _dragFiller) continue;
            _tabStrip.RemoveChild(child);
            child.QueueFree();
        }

        var group = new ButtonGroup();
        int index = 0;
        foreach (var tab in _log.Tabs)
        {
            var button = new Button
            {
                Text = tab.Label,
                ToggleMode = true,
                ButtonGroup = group,
                ButtonPressed = tab == _log.Active,
                FocusMode = FocusModeEnum.None
            };
            if (tab.Unread)
                button.AddThemeColorOverride("font_color", GameColors.Yellow);
            button.Pressed += () => _log.Activate(tab);
            button.GuiInput += OnTabStripGuiInput;
            _tabStrip.AddChild(button);
            _tabStrip.MoveChild(button, index++);

            if (tab.Kind != ChatTabKind.Tell) continue;
            var close = new Button { Text = "×", Flat = true, FocusMode = FocusModeEnum.None };
            close.Pressed += () => _log.Close(tab);
            _tabStrip.AddChild(close);
            _tabStrip.MoveChild(close, index++);
        }

        _input.PlaceholderText = _log.ChannelName;
    }

    private void BuildTabMenu()
    {
        _tabMenu = new PopupMenu();
        foreach (var kind in ChatLog.OptionalKinds)
            _tabMenu.AddCheckItem(kind.ToString(), (int)kind);
        _tabMenu.IdPressed += OnTabMenuIdPressed;
        AddChild(_tabMenu);
    }

    private void OnTabStripGuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true })
            return;
        foreach (var kind in ChatLog.OptionalKinds)
            _tabMenu.SetItemChecked(_tabMenu.GetItemIndex((int)kind), _log.IsEnabled(kind));
        _tabMenu.Position = (Vector2I)GetViewport().GetMousePosition();
        _tabMenu.Popup();
        AcceptEvent();
    }

    private void OnTabMenuIdPressed(long id)
    {
        var kind = (ChatTabKind)id;
        _log.SetEnabled(kind, !_log.IsEnabled(kind));
        GameManager.Instance.CharacterSettings.SetChatTabs(_log.EnabledKinds.Select(k => k.ToString()));
    }

    private void OnTextSubmitted(string text)
    {
        var result = ChatCommandParser.Parse(_log.ApplyChannel(text), _aliases, _commandHandlers.Keys);

        // ... switch + history block unchanged from the current file ...

        ClearAndUnfocus();
    }

    // ClearAndUnfocus, FocusChat, OnInputGuiInput, HistoryUp, HistoryDown,
    // OnQuitCommand, OnHairdyeCommand: copy unchanged from the current file.
    // Drop the "Called by GameHud to focus..." summary that sat above the removed Toggle();
    // move it above FocusChat if keeping it.
}
```

(The `// ...` lines above are plan shorthand for "copy the existing method bodies verbatim" — do not leave them in the file.)

**Step 3: Build and run tests**

Run: `dotnet build Goose2ClientGodot.csproj && dotnet test tests/Goose2Client.Tests`
Expected: `0 Error(s)`; all tests PASS.

**Step 4: Commit**

```bash
git add Scenes/UI/ChatWindow.tscn Scripts/UI/ChatWindow.cs
git commit -m "feat: tabbed, movable, resizable chat window"
```

---

### Task 7: Update UI scale self-test and verify end to end

**Files:**
- Modify: `Scripts/UiScaleSelfTest.cs:225-237`

**Step 1: Update the chat assertions.** Node paths moved under `Content`, and chat is no longer bottom-anchored; at 2× with no saved chat entry it is 1000×416, placed at the default (8,507)→ bottom-stuck with a 5px×2 margin.

Replace:

```csharp
        CheckFont(gm.Hud.Chat.GetNode<RichTextLabel>("ChatLog"), new StringName("normal_font_size"), 12f);
        CheckFont(gm.Hud.Chat.GetNode<LineEdit>("Input"), new StringName("font_size"), 12f);
```

with:

```csharp
        CheckFont(gm.Hud.Chat.GetNode<RichTextLabel>("Content/ChatLog"), new StringName("normal_font_size"), 12f);
        CheckFont(gm.Hud.Chat.GetNode<LineEdit>("Content/Input"), new StringName("font_size"), 12f);
```

and replace:

```csharp
        Assert(gm.Hud.Chat.OffsetTop == -426 && gm.Hud.Chat.OffsetBottom == -10,
            $"chat offsets top={gm.Hud.Chat.OffsetTop} bottom={gm.Hud.Chat.OffsetBottom} != (-426, -10)");
```

with:

```csharp
        Assert(gm.Hud.Chat.Size == new Vector2(1000, 416), $"chat size {gm.Hud.Chat.Size} != (1000, 416)");
        Assert(gm.Hud.Chat.Position == new Vector2(16, canvas.Y - 426),
            $"chat pos {gm.Hud.Chat.Position} != (16, {canvas.Y - 426})");
```

(`canvas` is the `Vector2I` local declared at `Scripts/UiScaleSelfTest.cs:90`.)

**Step 2: Run the self-test**

Run: `tools/tests/run_ui_scale.sh`
Expected: exits 0 and prints no `FAIL`. If the saved self-test profile `user://ui-scale-selftest-settings.json` contains a `"Chat"` window entry from a manual run, delete that file and rerun.

**Step 3: Run the full unit suite**

Run: `dotnet test tests/Goose2Client.Tests`
Expected: all PASS (baseline was 723; now 723 + new tests).

**Step 4: Manual verification** (launch the client against a server, e.g. copy `../../run.sh` into the worktree or run `/usr/bin/godot-mono --path .`)

- [ ] First login: chat bottom-left, tabs `All | Guild | Group`, placeholder "Say", themed panel background, text not faded when cursor is away.
- [ ] Drag empty tab-strip space → window moves; clicking a tab does not move it.
- [ ] Resize from each of the 4 edges and 4 corners; cursor changes; cannot shrink below min; cannot drag past screen edges.
- [ ] Change UI scale in Options while resized → chat keeps proportional size and bottom-left anchoring.
- [ ] Relog → position, size, visibility and tab configuration restored.
- [ ] Right-click tab strip → menu with Guild/Group/Chat/System checked correctly; toggling adds/removes tabs; disabling the active tab switches to All.
- [ ] `/tell <name> hi` → "<name>" tab opens with the `[tell to]` line; receive a tell → same tab (or new one) with yellow unread label when inactive; clicking clears it; × closes it; next tell reopens it.
- [ ] In Guild tab, type `hi` → sent as guild chat; in a tell tab → sent as tell; `/who` in any tab works normally.
- [ ] Enter / `/` / guild / tell / R reply hotkeys still prefill as before; `ToggleChat` hides and the hidden state survives relog.
- [ ] Options → reset window layout: chat returns to default size and position; tabs unchanged.
- [ ] Open many tell tabs → row does not wrap or widen the window; `<` `>` appear at the right end, scroll the tabs, and disable at each end; they hide again once tabs fit (close tabs or widen the window).
- [ ] With tabs overflowing, clicking a partly hidden tab or activating one via a new tell scrolls it into view; unread updates do not move the scroll position.
- [ ] With tabs overflowing, scroll fully right → drag filler at the end still moves the window.

**Step 5: Commit**

```bash
git add Scripts/UiScaleSelfTest.cs
git commit -m "test: update ui scale self-test for resizable chat window"
```
