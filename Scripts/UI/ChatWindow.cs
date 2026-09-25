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

    public void ClearAndUnfocus()
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
            {
                HistoryUp();
                // Without this, LineEdit's own KEY_UP handling runs after our signal and
                // moves the caret back to column 0, so the caret-set in HistoryUp is lost.
                GetViewport().SetInputAsHandled();
            }
            else if (k.Keycode == Key.Down)
            {
                HistoryDown();
                GetViewport().SetInputAsHandled();
            }
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

    private void OnQuitCommand(string command, string arguments)
    {
        GameManager.Instance.Quit();
    }

    private void OnHairdyeCommand(string command, string arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            GameManager.Instance.Hud?.Hairdye.Open();
            return;
        }
        GameManager.Instance.NetworkClient.Command($"{command} {arguments}");
    }
}
