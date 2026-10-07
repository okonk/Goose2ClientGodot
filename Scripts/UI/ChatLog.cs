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
    private const string TellFromPrefix = "[tell from] ";

    // illutiagooseserver LoginEvent/LogoutEvent: "<Name> has joined the world." / "<Name> has left the world."
    private static readonly string[] PresenceSuffixes = { " has joined the world.", " has left the world." };

    // illutiagooseserver WhoCommand ends its /who reply with "[Matched N players]".
    private const string WhoTerminatorPrefix = "[Matched ";

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
    private ChatTab? _whoTab;

    // Local player's display name for outgoing tell echoes; null until the character is
    // attached, in which case echoes fall back to "You".
    public string? SelfName { get; set; }

    public event Action? TabsChanged;
    public event Action? ActiveChanged;
    public event Action<string>? ActiveLineAdded;

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
        message = ChatText.ToDisplay(message.Replace("[", "[lb]"));
        var color = TypeColors.TryGetValue(type, out var c) ? c : GameColors.White;
        return $"[color=#{color.ToHtml(false)}]{message}[/color]";
    }

    public void Add(string message, ChatType type, string? tellName = null)
    {
        var line = Format(message, type);
        Append(_tabs[0], line);
        var target = Route(message, type, tellName);
        if (target != null)
            Append(target, type == ChatType.Tell ? Format(TellTabText(message), type) : line);

        var presence = PresenceTab(message);
        if (presence != null && presence != target)
            Append(presence, line);

        AppendWhoEcho(message, type, line, target);
    }

    // Latches the tab that should mirror the reply to the /who the player is about to send.
    public void ExpectWhoResponse()
    {
        _whoTab = Active;
    }

    // Who reply lines are the only chat-type messages starting with "[MapName]"; any other line
    // ends the burst, as does the "[Matched N players]" terminator.
    private void AppendWhoEcho(string message, ChatType type, string line, ChatTab? target)
    {
        if (_whoTab == null)
            return;
        if (type != ChatType.Chat || !message.StartsWith('['))
        {
            _whoTab = null;
            return;
        }
        if (_whoTab != _tabs[0] && _whoTab != target && _tabs.Contains(_whoTab))
            Append(_whoTab, line);
        if (message.StartsWith(WhoTerminatorPrefix, StringComparison.Ordinal))
            _whoTab = null;
    }

    private ChatTab? PresenceTab(string message)
    {
        foreach (var suffix in PresenceSuffixes)
        {
            if (!message.EndsWith(suffix, StringComparison.Ordinal))
                continue;
            string name = message.Substring(0, message.Length - suffix.Length);
            if (name.Length == 0)
                return null;
            return _tabs.Find(t => t.Kind == ChatTabKind.Tell && string.Equals(t.Label, name, StringComparison.OrdinalIgnoreCase));
        }
        return null;
    }

    // The tell tab is a 1:1 conversation: drop the [tell from]/[tell to] prefix and show the
    // sender. Outgoing echoes were sent by the player, so they are labeled with the player name.
    private string TellTabText(string message)
    {
        if (message.StartsWith(TellFromPrefix, StringComparison.Ordinal))
            return message.Substring(TellFromPrefix.Length);
        if (message.StartsWith(TellToPrefix, StringComparison.Ordinal))
        {
            int colon = message.IndexOf(": ", TellToPrefix.Length, StringComparison.Ordinal);
            string self = string.IsNullOrEmpty(SelfName) ? "You" : SelfName;
            return colon > TellToPrefix.Length ? self + ": " + message.Substring(colon + 2) : message;
        }
        return message;
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
            _tabs.Remove(tab!);
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

    private ChatTab? Route(string message, ChatType type, string? tellName)
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

    private static string? ParseTellTo(string message)
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
