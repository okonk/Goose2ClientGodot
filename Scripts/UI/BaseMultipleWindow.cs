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
    protected override bool DefaultVisible => false;

    protected virtual int LineCount => 20;

    protected const int LineFontSize = 10;
    private const int ButtonFontSize = 12;

    protected Control[] _lines;
    protected Button _backButton;
    protected Button _nextButton;
    protected Button? _okButton;
    protected Button _closeButton;

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
        _okButton = GetNodeOrNull<Button>("Content/OkButton");
        _closeButton = GetNode<Button>("Content/CloseButton");
        _backButton.Visible = false;
        _nextButton.Visible = false;
        if (_okButton != null) _okButton.Visible = false;
        _closeButton.Visible = false;
        _backButton.Pressed += BackClicked;
        _nextButton.Pressed += NextClicked;
        if (_okButton != null) _okButton.Pressed += OkClicked;
        _closeButton.Pressed += CloseWindow;
        var applier = UiScaleApplier.Instance;
        foreach (var b in new[] { _backButton, _nextButton, _okButton, _closeButton })
            if (b != null) applier.ApplyFontSize(b, ButtonFontSize);

        _lines = new Control[LineCount];
        var content = GetNode<Control>("Content");
        for (int i = 0; i < LineCount; i++)
        {
            var line = CreateLine(i);
            // Line geometry is owned by the metrics + Relayout override; the generic snapshot
            // must not capture it (a snapshot record would double-scale already-scaled offsets).
            line.SetMeta(UiScaleLayout.SkipMeta, true);
            line.Position = LinePosition(i, applier.Factor);
            content.AddChild(line);
            _lines[i] = line;
        }

        ScaleRegister();
    }

    public override void Relayout()
    {
        base.Relayout();
        var factor = UiScaleApplier.Instance.Factor;
        for (int i = 0; i < _lines.Length; i++)
            _lines[i].Position = LinePosition(i, factor);
    }

    protected virtual Control CreateLine(int index)
    {
        var label = new Label { Text = " ", Name = "Line" + index };
        UiScaleApplier.Instance.ApplyFontSize(label, LineFontSize);
        return label;
    }

    protected virtual Vector2 LinePosition(int index, float factor)
        => MultiWindowMetrics.LinePosition(index, factor);

    protected virtual void SetLineText(int index, string text)
    {
        if (_lines[index] is Label label)
            label.Text = text + " ";
    }

    /// <summary>Called by the manager when a MakeWindowPacket arrives for this window.</summary>
    public virtual void OnMakeWindow(MakeWindowPacket packet)
    {
        NpcId = packet.NpcId;
        Title = packet.Title;
        WindowId = packet.WindowId;

        // Bottom Close/Back/Next visibility comes from MakeWindow.Buttons (Goose2 enum).
        _closeButton.Visible = WindowButtonFlags.IsEnabled(packet.Buttons, WindowButtons.Close);
        _backButton.Visible = WindowButtonFlags.IsEnabled(packet.Buttons, WindowButtons.Back);
        _nextButton.Visible = WindowButtonFlags.IsEnabled(packet.Buttons, WindowButtons.Next);
        if (_okButton != null)
            _okButton.Visible = WindowButtonFlags.IsEnabled(packet.Buttons, WindowButtons.OK);

        // Clear all lines
        for (int i = 0; i < _lines.Length; i++)
            SetLineText(i, "");
    }

    /// <summary>Called by the manager when an EndWindowPacket arrives for this window.</summary>
    internal virtual void OnEndWindow()
    {
        Visible = true;
    }

    /// <summary>Called by the manager when a WindowLinePacket arrives for this window.</summary>
    internal void OnWindowLine(WindowLinePacket packet)
    {
        if (packet.LineNumber < 0 || packet.LineNumber >= _lines.Length) return;
        SetLineText(packet.LineNumber, packet.Text);
    }

    protected override void OnClosePressed()
    {
        CloseWindow();
    }

    public void CloseWindow()
    {
        GameManager.Instance.NetworkClient.WindowButtonClick(WindowButtons.Close, WindowId, NpcId);
        Free();
    }

    // Server-initiated close (CLW): free without sending a WBC back.
    internal void Free()
    {
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

    public void OkClicked()
    {
        GameManager.Instance.NetworkClient.WindowButtonClick(WindowButtons.OK, WindowId, NpcId);
    }
}
