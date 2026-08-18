using System;
using Godot;
using Goose2Client;
using Goose2Client.Network.Packets;

namespace Goose2Client.UI;

/// <summary>
/// Base for multi-instance text-line dialog windows (NPC dialogs, quests, info).
/// Replaces Unity packetBuffer + Update dequeue with direct main-thread handling:
/// the manager calls OnMakeWindow/OnEndWindow/OnWindowLine directly when packets arrive.
/// </summary>
public abstract partial class BaseMultipleWindow : BaseWindow, IWindow
{
    public const int LineCount = 20;

    // The server pre-wraps each line for the original window width at this font size (matches the
    // Unity client's LiberationSans @ 10). Code-created labels otherwise fall back to Godot's
    // built-in 16px default — the theme's default_font_size isn't applied to runtime children —
    // which overflows the fixed-width window. Keep this in sync with the line wrapping the server assumes.
    private const int LineFontSize = 10;
    private const int ButtonFontSize = 12;

    private Label[] _lines;
    private Button _backButton;
    private Button _nextButton;
    private Button _closeButton;

    public Action<BaseMultipleWindow> OnCloseWindow { get; set; }

    public int WindowId { get; private set; }
    public abstract WindowFrames WindowFrame { get; }
    public int NpcId { get; private set; }

    public override void _Ready()
    {
        base._Ready();

        // Server-spawned — hidden until a MakeWindow/EndWindow pair arrives.
        Visible = false;

        // Resolve paging/close buttons (Back/Close/Next along the bottom).
        // Hidden until MakeWindow.Buttons says otherwise.
        _backButton = GetNode<Button>("Content/BackButton");
        _nextButton = GetNode<Button>("Content/NextButton");
        _closeButton = GetNode<Button>("Content/CloseButton");
        _backButton.Visible = false;
        _nextButton.Visible = false;
        _closeButton.Visible = false;
        _backButton.Pressed += BackClicked;
        _nextButton.Pressed += NextClicked;
        _closeButton.Pressed += CloseWindow;
        foreach (var b in new[] { _backButton, _nextButton, _closeButton })
            b.AddThemeFontSizeOverride("font_size", ButtonFontSize);

        // Create line labels at runtime. Pack them tight (separation 0) and pin the font size so
        // the server's pre-wrapped lines fit the window exactly as they do in the Unity client.
        _lines = new Label[LineCount];
        var linesContainer = GetNode<VBoxContainer>("Content/Lines");
        linesContainer.AddThemeConstantOverride("separation", 0);
        for (int i = 0; i < LineCount; i++)
        {
            var label = new Label { Text = " " };
            label.AddThemeFontSizeOverride("font_size", LineFontSize);
            linesContainer.AddChild(label);
            _lines[i] = label;
        }
    }

    /// <summary>Called by the manager when a MakeWindowPacket arrives for this window.</summary>
    public void OnMakeWindow(MakeWindowPacket packet)
    {
        NpcId = packet.NpcId;
        Title = packet.Title;
        WindowId = packet.WindowId;

        // Bottom Close/Back/Next visibility comes from MakeWindow.Buttons (Goose2 enum).
        _closeButton.Visible = WindowButtonFlags.IsEnabled(packet.Buttons, WindowButtons.Close);
        _backButton.Visible = WindowButtonFlags.IsEnabled(packet.Buttons, WindowButtons.Back);
        _nextButton.Visible = WindowButtonFlags.IsEnabled(packet.Buttons, WindowButtons.Next);

        // Clear all lines
        foreach (var l in _lines)
            l.Text = " ";
    }

    /// <summary>Called by the manager when an EndWindowPacket arrives for this window.</summary>
    internal void OnEndWindow()
    {
        Visible = true;
    }

    /// <summary>Called by the manager when a WindowLinePacket arrives for this window.</summary>
    internal void OnWindowLine(WindowLinePacket packet)
    {
        if (packet.LineNumber < 0 || packet.LineNumber >= _lines.Length) return;
        _lines[packet.LineNumber].Text = packet.Text + " ";
    }

    protected override void OnClosePressed()
    {
        CloseWindow();
    }

    public void CloseWindow()
    {
        GameManager.Instance.NetworkClient.WindowButtonClick(WindowButtons.Close, WindowId, NpcId);
        Visible = false;
        OnCloseWindow?.Invoke(this);
        QueueFree();
    }

    public void NextClicked()
    {
        GameManager.Instance.NetworkClient.WindowButtonClick(WindowButtons.Next, WindowId, NpcId);
    }

    public void BackClicked()
    {
        GameManager.Instance.NetworkClient.WindowButtonClick(WindowButtons.Back, WindowId, NpcId);
    }
}
