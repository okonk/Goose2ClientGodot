using System;
using Godot;
using Goose2Client;
using Goose2Client.Network.Packets;

namespace Goose2Client.UI;

public partial class OptionListWindow : BaseMultipleWindow
{
    public const int MaxLines = 10;
    // WBC button id = LineClickOffset + line index; must match Goose.Window.LineClickOffset
    // (server). See aspereta-info/protocol.txt.
    public const int LineClickOffset = 20;

    private Label _heading;
    private bool _headingVisible;
    private int _maxLine = -1;

    public override WindowFrames WindowFrame => WindowFrames.OptionList;

    protected override int LineCount => MaxLines;

    public override void _Ready()
    {
        // Created before base._Ready so the first Relayout (fired by ScaleRegister) sees it.
        CreateHeading();
        base._Ready();
    }

    private void CreateHeading()
    {
        _heading = new Label
        {
            Name = "Heading",
            Text = "",
            Visible = false,
            ClipText = true,
            HorizontalAlignment = HorizontalAlignment.Left,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        UiScaleApplier.Instance.ApplyFontSize(_heading, LineFontSize);
        _heading.SetMeta(UiScaleLayout.SkipMeta, true);
        GetNode<Control>("Content").AddChild(_heading);
    }

    protected override Control CreateLine(int index)
    {
        // The Button is a full-row click layer (empty text); the label + icon are children
        // positioned explicitly so both share the row's vertical centre.
        var button = new Button
        {
            Name = "Line" + index,
            Text = "",
            Flat = true,
            Visible = false,
            FocusMode = FocusModeEnum.None,
        };
        button.Pressed += () => LineClicked(index);

        var icon = new TextureRect
        {
            Name = "Icon",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        button.AddChild(icon);

        var label = new Label
        {
            Name = "Label",
            Text = "",
            ClipText = true,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        UiScaleApplier.Instance.ApplyFontSize(label, LineFontSize);
        button.AddChild(label);
        return button;
    }

    protected override Vector2 LinePosition(int index, float factor)
        => OptionListMetrics.LinePosition(index, factor, _headingVisible);

    protected override void SetLineText(int index, string text)
    {
        var button = (Button)_lines[index];
        GetLabel(index).Text = text;
        var visible = !string.IsNullOrWhiteSpace(text);
        button.Visible = visible;
        if (visible)
        {
            _maxLine = Math.Max(_maxLine, index);
            return;
        }

        // Invisible rows let clicks fall through to the world; a disabled Button would
        // still swallow them (Godot hit-testing is not clipped to the parent rect).
        var icon = GetIcon(index);
        icon.Texture = null;
        icon.Visible = false;
        icon.Material = null;
        ResetLineColor(index);
    }

    public override void OnMakeWindow(MakeWindowPacket packet)
    {
        _maxLine = -1;
        _headingVisible = false;
        _heading.Text = "";
        _heading.Visible = false;
        base.OnMakeWindow(packet);
    }

    internal override void OnOpeningLine(string text)
    {
        _headingVisible = !string.IsNullOrWhiteSpace(text);
        _heading.Text = text;
        Relayout();
    }

    internal override void OnWindowLine(WindowLinePacket packet)
    {
        base.OnWindowLine(packet);
        if (packet.LineNumber < 0 || packet.LineNumber >= _lines.Length) return;
        ApplyLineGraphics(packet.LineNumber, packet);
    }

    private void ApplyLineGraphics(int index, WindowLinePacket p)
    {
        var icon = GetIcon(index);
        var tex = p.GraphicSheet != 0 ? GameManager.Instance.Sprites.Get(p.GraphicSheet, p.GraphicId) : null;
        icon.Texture = tex;
        icon.Visible = tex != null;
        icon.Material = null;

        if (p.HasColor)
            SetLineColor(index, new Color(p.GraphicR / 255f, p.GraphicG / 255f, p.GraphicB / 255f, 1f));
        else
            ResetLineColor(index);
    }

    public override void Relayout()
    {
        base.Relayout();
        var factor = UiScaleApplier.Instance.Factor;
        var lineSize = OptionListMetrics.LineSize(factor);
        var iconSize = UiScale.ScaleSize(OptionListMetrics.IconSize, factor);
        var iconX = UiScale.ScaleSize(OptionListMetrics.IconX, factor);
        var textX = OptionListMetrics.LineTextIndent(factor);
        var textWidth = OptionListMetrics.LineTextWidth(factor);
        for (int i = 0; i < _lines.Length; i++)
        {
            var button = (Button)_lines[i];
            button.Size = lineSize;
            var icon = GetIcon(i);
            icon.Size = new Vector2(iconSize, iconSize);
            icon.Position = new Vector2(iconX, (lineSize.Y - iconSize) / 2f);
            var label = GetLabel(i);
            label.Position = new Vector2(textX, 0f);
            label.Size = new Vector2(textWidth, lineSize.Y);
        }

        _heading.Visible = _headingVisible;
        if (_headingVisible)
        {
            _heading.Position = OptionListMetrics.HeadingPosition(factor);
            _heading.Size = OptionListMetrics.HeadingSize(factor);
        }

        Size = new Vector2(
            Size.X,
            OptionListMetrics.WindowHeight(Math.Max(_maxLine + 1, 1), factor, _headingVisible, AnyBottomButtonVisible()));
        PlaceBottomButtons(factor);
    }

    internal override void OnEndWindow()
    {
        base.OnEndWindow();
        // The snapshot apply inside Relayout resets the root rect to the tscn rect, which
        // drops the manager-set position; restore it afterwards.
        var pos = Position;
        Relayout();
        Position = pos;
    }

    private bool AnyBottomButtonVisible()
        => _backButton.Visible || _nextButton.Visible || _closeButton.Visible
            || (_okButton != null && _okButton.Visible);

    private void PlaceBottomButtons(float factor)
    {
        var y = OptionListMetrics.BottomButtonY(Size.Y, factor);
        var size = OptionListMetrics.BottomButtonSize(factor);
        foreach (var b in new[] { _backButton, _nextButton, _okButton, _closeButton })
        {
            if (b == null || !b.Visible) continue;
            var p = b.Position;
            b.Position = new Vector2(p.X, y);
            b.Size = size;
        }
    }

    private TextureRect GetIcon(int index)
        => ((Button)_lines[index]).GetNode<TextureRect>("Icon");

    private Label GetLabel(int index)
        => ((Button)_lines[index]).GetNode<Label>("Label");

    private void SetLineColor(int index, Color c)
        => GetLabel(index).AddThemeColorOverride("font_color", c);

    private void ResetLineColor(int index)
        => GetLabel(index).RemoveThemeColorOverride("font_color");

    private void LineClicked(int index)
    {
        GameManager.Instance.NetworkClient.WindowButtonClick(
            (WindowButtons)(LineClickOffset + index), WindowId, NpcId);
    }
}
