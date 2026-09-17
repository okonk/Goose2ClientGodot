using System;
using Godot;
using Goose2Client;

namespace Goose2Client.UI;

public partial class OptionListWindow : BaseMultipleWindow
{
    public const int MaxLines = 10;
    public const int LineClickOffset = 20;

    private const int LinePaddingY = 5;
    private const int LineTextHeight = 13;
    private const int LineHeight = LineTextHeight + LinePaddingY * 2;
    private const float LinesOriginX = 6f;
    private const float LinesOriginY = 22f;
    private const float LinesWidth = 248f;
    private const float BottomMargin = 6f;
    private const float ButtonGap = 6f;
    private const float ButtonRowHeight = 26f;
    private const float ButtonWidth = 56f;

    private int _maxLine = -1;

    public override WindowFrames WindowFrame => WindowFrames.OptionList;

    protected override int LineCount => MaxLines;

    protected override Control CreateLine(int index)
    {
        var button = new Button
        {
            Name = "Line" + index,
            Text = " ",
            Flat = true,
            Disabled = true,
            Alignment = HorizontalAlignment.Left,
            FocusMode = FocusModeEnum.None,
        };
        UiScaleApplier.Instance.ApplyFontSize(button, LineFontSize);
        button.Pressed += () => LineClicked(index);
        return button;
    }

    protected override Vector2 LinePosition(int index, float factor)
    {
        var pos = new Vector2(LinesOriginX, LinesOriginY + index * LineHeight);
        if (factor == 1f)
            return pos;
        return new Vector2(
            UiScale.ScaleCoordinate(pos.X, factor),
            UiScale.ScaleCoordinate(pos.Y, factor));
    }

    protected override void SetLineText(int index, string text)
    {
        var button = (Button)_lines[index];
        button.Text = text;
        button.Disabled = string.IsNullOrEmpty(text);
        if (!string.IsNullOrEmpty(text) && index > _maxLine)
            _maxLine = index;
    }

    public override void Relayout()
    {
        base.Relayout();
        var factor = UiScaleApplier.Instance.Factor;
        var lineSize = new Vector2(UiScale.ScaleSize(LinesWidth, factor), UiScale.ScaleSize(LineHeight, factor));
        var padding = UiScale.ScaleSize(LinePaddingY, factor);
        for (int i = 0; i < _lines.Length; i++)
        {
            var line = (Button)_lines[i];
            line.Size = lineSize;
            line.AddThemeConstantOverride("margin_top", padding);
            line.AddThemeConstantOverride("margin_bottom", padding);
        }
        Size = ComputedSize(factor);
        PlaceBottomButtons(factor);
    }

    internal override void OnEndWindow()
    {
        base.OnEndWindow();
        // The snapshot apply inside Relayout resets the root rect to the tscn size; the
        // manager-set position must survive the resize.
        var pos = Position;
        Relayout();
        Position = pos;
    }

    private Vector2 ComputedSize(float factor)
    {
        int lines = Math.Max(_maxLine + 1, 1);
        float h = LinesOriginY + lines * LineHeight + BottomMargin;
        if (AnyBottomButtonVisible())
            h += ButtonGap + ButtonRowHeight;
        return new Vector2(Size.X, UiScale.ScaleSize(h, factor));
    }

    private bool AnyBottomButtonVisible()
        => _backButton.Visible || _nextButton.Visible || _closeButton.Visible
            || (_okButton != null && _okButton.Visible);

    private void PlaceBottomButtons(float factor)
    {
        var y = Size.Y - UiScale.ScaleSize(BottomMargin + ButtonRowHeight, factor);
        var size = new Vector2(UiScale.ScaleSize(ButtonWidth, factor), UiScale.ScaleSize(ButtonRowHeight, factor));
        foreach (var b in new[] { _backButton, _nextButton, _okButton, _closeButton })
        {
            if (b == null || !b.Visible) continue;
            var p = b.Position;
            b.Position = new Vector2(p.X, y);
            b.Size = size;
        }
    }

    private void LineClicked(int index)
    {
        GameManager.Instance.NetworkClient.WindowButtonClick(
            (WindowButtons)(LineClickOffset + index), WindowId, NpcId);
    }
}
