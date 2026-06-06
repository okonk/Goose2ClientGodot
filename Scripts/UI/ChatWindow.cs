using Godot;
using System;
using System.Collections.Generic;
using Goose2Client;
using Goose2Client.Network.Packets;

namespace Goose2Client.UI;

/// <summary>
/// Chat window — plain Control panel with hover alpha, chat log, and input line.
/// Not a BaseWindow (no title bar). GameHud routes focus actions here via FocusChat().
/// </summary>
public partial class ChatWindow : Control
{
    private RichTextLabel _chatLog;
    private LineEdit _input;
    private Panel _panel;

    private bool _listenersRegistered;

    public bool Typing => _input.HasFocus();
    public string ReplyToName { get; private set; }

    private readonly Dictionary<string, string> _aliases = new();
    private readonly Dictionary<string, Action<string, string>> _commandHandlers = new();
    private readonly Dictionary<ChatType, Color> _chatColors = new();
    private readonly List<string> _inputHistory = new();
    private int _historyIndex = 0;

    public override void _Ready()
    {
        _chatLog = GetNode<RichTextLabel>("ChatLog");
        _chatLog.BbcodeEnabled = true;
        _chatLog.ScrollFollowing = true;

        _input = GetNode<LineEdit>("Input");
        _panel = GetNode<Panel>("Panel");

        SetAlpha(0.7f);

        // Register packet listeners
        GameManager.Instance.PacketManager.Listen<ChatPacket>(OnChat);
        GameManager.Instance.PacketManager.Listen<HashMessagePacket>(OnHashMessage);
        GameManager.Instance.PacketManager.Listen<ServerMessagePacket>(OnServerMessage);
        GameManager.Instance.PacketManager.Listen<TellPacket>(OnTell);
        _listenersRegistered = true;

        // Input signals
        _input.TextSubmitted += OnTextSubmitted;
        _input.GuiInput += OnInputGuiInput;

        // Hover alpha
        MouseEntered += () => SetAlpha(1f);
        MouseExited += () => SetAlpha(0.7f);

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

        // Chat type colors
        _chatColors[ChatType.Chat] = Colors.White;
        _chatColors[ChatType.Guild] = Colors.Yellow;
        _chatColors[ChatType.Group] = Colors.Yellow;
        _chatColors[ChatType.Melee] = Colors.Red;
        _chatColors[ChatType.Spells] = Colors.Blue;
        _chatColors[ChatType.Tell] = Colors.Yellow;
        _chatColors[ChatType.Server] = Colors.Green;
    }

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
        AddChatLine($"[tell from] {p.Name}: {p.Message}", ChatType.Tell);
    }

    public void AddChatLine(string message, ChatType chatType)
    {
        message = message.Replace("[", "[lb]").Replace('`', '\u2665');
        var color = _chatColors.TryGetValue(chatType, out var c) ? c : Colors.White;
        _chatLog.AppendText($"[color=#{color.ToHtml(false)}]{message}[/color]\n");
    }

    private void OnTextSubmitted(string text)
    {
        var result = ChatCommandParser.Parse(text, _aliases, _commandHandlers.Keys);

        switch (result.Kind)
        {
            case ChatActionKind.ChatMessage:
                GameManager.Instance.NetworkClient.ChatMessage(result.Text);
                break;
            case ChatActionKind.Command:
                GameManager.Instance.NetworkClient.Command(result.Text);
                break;
            case ChatActionKind.Handler:
                if (_commandHandlers.TryGetValue(result.Text.ToLowerInvariant(), out var h))
                    h(result.Text, result.Arguments);
                break;
            case ChatActionKind.None:
                // do nothing
                break;
        }

        if (result.Kind != ChatActionKind.None)
        {
            if (_inputHistory.Count == 0 || _inputHistory[^1] != text)
                _inputHistory.Add(text);
            _historyIndex = _inputHistory.Count;
        }

        ClearAndUnfocus();
    }

    private void ClearAndUnfocus()
    {
        _input.Text = "";
        _input.ReleaseFocus();
    }

    /// <summary>
    /// Called by GameHud to focus the chat input with an optional prefix.
    /// </summary>
    public void FocusChat(string prefill)
    {
        _input.Text = prefill;
        _input.GrabFocus();
        _input.CaretColumn = prefill.Length;
    }

    private void OnInputGuiInput(InputEvent @event)
    {
        if (@event is InputEventKey k && k.Pressed)
        {
            if (k.Keycode == Key.Up)
                HistoryUp();
            else if (k.Keycode == Key.Down)
                HistoryDown();
            else if (k.Keycode == Key.Escape)
            {
                ClearAndUnfocus();
                _historyIndex = _inputHistory.Count;
            }
        }
    }

    private void HistoryUp()
    {
        if (_inputHistory.Count == 0) return;
        _historyIndex = Math.Max(0, _historyIndex - 1);
        _input.Text = _inputHistory[_historyIndex];
        _input.CaretColumn = _input.Text.Length;
    }

    private void HistoryDown()
    {
        if (_inputHistory.Count == 0) return;
        _historyIndex++;
        if (_historyIndex < _inputHistory.Count)
        {
            _input.Text = _inputHistory[_historyIndex];
            _input.CaretColumn = _input.Text.Length;
        }
        else
        {
            _input.Text = "";
            _historyIndex = _inputHistory.Count;
        }
    }

    private void SetAlpha(float a)
    {
        _panel.Modulate = new Color(1, 1, 1, a);
    }

    private void OnQuitCommand(string command, string arguments)
    {
        GameManager.Instance.Quit();
    }
}
