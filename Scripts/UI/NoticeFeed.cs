using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Goose2Client.Network.Packets;

namespace Goose2Client.UI;

public partial class NoticeFeed : Control, IScalableWindow
{
    public const int BaseWidth = 312;
    private const int BaseRightMargin = 8;
    private const int BaseGap = 4;
    private const int BaseOutline = 3;

    public MinimapControl Minimap { get; init; } = null!;

    private readonly NoticeFeedModel _model = new();
    private readonly List<(NoticeEntry Entry, Label Label)> _labels = new();
    private VBoxContainer _stack = null!;
    private bool _listenersRegistered;

    private static float S(float x)
        => MathF.Round(x * UiScaleApplier.Instance!.Factor, MidpointRounding.AwayFromZero);

    public void Relayout()
    {
        float top = Minimap.Visible ? MinimapControl.BaseFootprintBottom + BaseGap : MinimapControl.BaseTop;
        OffsetTop = S(top);
        OffsetBottom = S(top);
        OffsetRight = -S(BaseRightMargin);
        OffsetLeft = -S(BaseRightMargin + BaseWidth);
        foreach (var (_, label) in _labels)
            label.AddThemeConstantOverride("outline_size", UiScaleApplier.Instance!.ScaleSize(BaseOutline));
    }

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.TopRight);
        MouseFilter = MouseFilterEnum.Ignore;

        _stack = new VBoxContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _stack.AddThemeConstantOverride("separation", 0);
        _stack.SetAnchorsPreset(LayoutPreset.TopWide);
        AddChild(_stack);

        Minimap.VisibilityChanged += Relayout;
        GameManager.Instance.PacketManager.Listen<NoticePacket>(OnNotice);
        _listenersRegistered = true;

        UiScaleApplier.Instance!.RegisterWindow(this);
        Relayout();
    }

    public override void _ExitTree()
    {
        Minimap.VisibilityChanged -= Relayout;
        if (_listenersRegistered)
        {
            GameManager.Instance.PacketManager.Remove<NoticePacket>(OnNotice);
            _listenersRegistered = false;
        }
        UiScaleApplier.Instance?.UnregisterWindow(this);
    }

    public override void _Process(double delta)
    {
        if (_model.Entries.Count == 0) return;
        _model.Tick(delta);
        SyncLabels();
    }

    private void OnNotice(object o)
    {
        var p = (NoticePacket)o;
        _model.Add(p.Type, p.Text);
        SyncLabels();
    }

    private void SyncLabels()
    {
        for (int i = _labels.Count - 1; i >= 0; i--)
        {
            var (entry, label) = _labels[i];
            if (!_model.Entries.Contains(entry))
            {
                _labels.RemoveAt(i);
                // RemoveChild first: QueueFree keeps the label in the tree until frame end,
                // so a freed line would otherwise hold layout space for a frame.
                _stack.RemoveChild(label);
                label.QueueFree();
            }
        }

        foreach (var entry in _model.Entries)
        {
            if (_labels.Exists(l => l.Entry == entry)) continue;
            var label = new Label
            {
                Text = entry.Text,
                HorizontalAlignment = HorizontalAlignment.Right,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            label.AddThemeColorOverride("font_color", NoticeColors.For(entry.Type));
            label.AddThemeColorOverride("font_outline_color", Colors.Black);
            label.AddThemeConstantOverride("outline_size", UiScaleApplier.Instance!.ScaleSize(BaseOutline));
            _stack.AddChild(label);
            _labels.Add((entry, label));
        }

        foreach (var (entry, label) in _labels)
            label.Modulate = new Color(1f, 1f, 1f, NoticeFeedModel.Alpha(entry));
    }
}
