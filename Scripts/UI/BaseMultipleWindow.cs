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

    private const int LineFontSize = 10;
    // Row pitch copied from Unity's line prefabs (m_SizeDelta.y = 11.18). Unity's TMP renders
    // size-10 LiberationSans with ~11.2 px line height (ascent+descent), but Godot's Label
    // derives line height from hhea metrics: 13 px per line at size 10 — 1.8 px extra per line,
    // which accumulates to ~1 line of overflow in these fixed-height windows. So the labels are
    // positioned manually at this pitch (like Unity's fixed 11.18 px rows) instead of being
    // stacked in a VBoxContainer. Each label keeps its natural 13 px height, so a row's ink
    // overlaps the next by 1.8 px — the same overlap Unity's 11.18 px rows have.
    private const float LineRowHeight = 11.18f;
    // Top-left of the line area in both InfoWindow and QuestWindow (matches the old Lines rect).
    private static readonly Vector2 LinesOrigin = new(6, 22);
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
        var applier = UiScaleApplier.Instance;
        foreach (var b in new[] { _backButton, _nextButton, _closeButton })
            applier.ApplyFontSize(b, ButtonFontSize);

        // Create line labels at runtime, stacked at Unity's 11.18 px row pitch (see LineRowHeight).
        _lines = new Label[LineCount];
        var content = GetNode<Control>("Content");
        for (int i = 0; i < LineCount; i++)
        {
            var label = new Label { Text = " " };
            applier.ApplyFontSize(label, LineFontSize);
            // Line geometry is owned by the metrics + Relayout override; the generic snapshot
            // must not capture it (a snapshot record would double-scale already-scaled offsets).
            label.SetMeta(UiScaleLayout.SkipMeta, true);
            label.Position = LinesOrigin + new Vector2(0, i * LineRowHeight);
            content.AddChild(label);
            _lines[i] = label;
        }

        ScaleRegister();
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
