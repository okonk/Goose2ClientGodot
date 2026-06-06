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

    private Label[] _lines;
    private Button _backButton;
    private Button _nextButton;

    public Action<BaseMultipleWindow> OnCloseWindow { get; set; }

    public int WindowId { get; private set; }
    public abstract WindowFrames WindowFrame { get; }
    public int NpcId { get; private set; }

    public override void _Ready()
    {
        base._Ready();

        // Server-spawned — hidden until a MakeWindow/EndWindow pair arrives.
        Visible = false;

        // Resolve paging buttons
        _backButton = GetNode<Button>("Content/BackButton");
        _nextButton = GetNode<Button>("Content/NextButton");
        _backButton.Pressed += BackClicked;
        _nextButton.Pressed += NextClicked;

        // Create line labels at runtime
        _lines = new Label[LineCount];
        var linesContainer = GetNode<VBoxContainer>("Content/Lines");
        for (int i = 0; i < LineCount; i++)
        {
            var label = new Label { Text = " " };
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

        // Buttons is bool[5]; Back=3 → index 2, Next=4 → index 3
        if (packet.Buttons != null && packet.Buttons.Length >= 4)
        {
            _backButton.Visible = packet.Buttons[(int)WindowButtons.Back - 1];
            _nextButton.Visible = packet.Buttons[(int)WindowButtons.Next - 1];
        }

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
