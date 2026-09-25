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
    public void Add_IncomingTell_TellTabShowsSenderNameOnly_AllKeepsPrefix()
    {
        var log = NewLog();
        log.Add("[tell from] Bob: hi", ChatType.Tell, "Bob");
        Assert.Equal(ChatLog.Format("Bob: hi", ChatType.Tell), TellTab(log, "Bob").Lines[0]);
        Assert.Equal(ChatLog.Format("[tell from] Bob: hi", ChatType.Tell), Tab(log, ChatTabKind.All).Lines[0]);
    }

    [Fact]
    public void Add_TellToEcho_TellTabShowsSelfName_AllKeepsPrefix()
    {
        var log = NewLog();
        log.SelfName = "Hayden";
        log.Add("[tell to] Bob: hey", ChatType.Tell);
        Assert.Equal(ChatLog.Format("Hayden: hey", ChatType.Tell), TellTab(log, "Bob").Lines[0]);
        Assert.Equal(ChatLog.Format("[tell to] Bob: hey", ChatType.Tell), Tab(log, ChatTabKind.All).Lines[0]);
    }

    [Fact]
    public void Add_TellToEcho_NoSelfName_FallsBackToYou()
    {
        var log = NewLog();
        log.Add("[tell to] Bob: hey", ChatType.Tell);
        Assert.Equal(ChatLog.Format("You: hey", ChatType.Tell), TellTab(log, "Bob").Lines[0]);
    }

    [Fact]
    public void Add_UnprefixedTell_TellTabShowsMessageAsIs()
    {
        var log = NewLog();
        log.Add("[tell to] Bob: hey", ChatType.Tell);
        log.Activate(TellTab(log, "Bob"));
        log.Add("Bob is not online.", ChatType.Tell);
        Assert.Equal(ChatLog.Format("Bob is not online.", ChatType.Tell), TellTab(log, "Bob").Lines[1]);
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
    public void Add_TellToWithEmptyName_DoesNotOpenTellTab()
    {
        var log = NewLog();
        log.Add("[tell to] : hi", ChatType.Tell);
        Assert.Single(log.Tabs);
    }

    [Fact]
    public void Add_TellToPrefixOnly_DoesNotOpenTellTab()
    {
        var log = NewLog();
        log.Add("[tell to] ", ChatType.Tell);
        Assert.Single(log.Tabs);
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
    public void SetEnabled_InsertsAfterEnabledKindsOnly_WhenEarlierKindDisabled()
    {
        var log = NewLog(ChatTabKind.Guild);
        log.Add("[tell to] Bob: hey", ChatType.Tell);

        log.SetEnabled(ChatTabKind.Chat, true);

        Assert.Equal(new[] { "All", "Guild", "Chat", "Bob" }, log.Tabs.Select(t => t.Label));
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
