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

    public override WindowFrames WindowFrame => WindowFrames.OptionList;

    protected override int LineCount => MaxLines;

    protected override Control CreateLine(int index)
    {
        var button = new Button
        {
            Name = "Line" + index,
            Text = " ",
            Flat = true,
            Visible = false,
            ClipText = true,
            Alignment = HorizontalAlignment.Left,
            FocusMode = FocusModeEnum.None,
        };
        UiScaleApplier.Instance.ApplyFontSize(button, LineFontSize);
        button.Pressed += () => LineClicked(index);
        return button;
    }

    protected override Vector2 LinePosition(int index, float factor)
        => OptionListMetrics.LinePosition(index, factor);

    protected override void SetLineText(int index, string text)
    {
        var button = (Button)_lines[index];
        button.Text = text;
        // Invisible rows let clicks fall through to the world; a disabled Button would
        // still swallow them (Godot hit-testing is not clipped to the parent rect).
        button.Visible = !string.IsNullOrEmpty(text);
    }

    public override void Relayout()
    {
        base.Relayout();
        var factor = UiScaleApplier.Instance.Factor;
        var lineSize = OptionListMetrics.LineSize(factor);
        for (int i = 0; i < _lines.Length; i++)
            ((Button)_lines[i]).Size = lineSize;
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

    private void LineClicked(int index)
    {
        GameManager.Instance.NetworkClient.WindowButtonClick(
            (WindowButtons)(LineClickOffset + index), WindowId, NpcId);
    }
}
