using Godot;

namespace Goose2Client.UI;

public partial class TouchButton : Control
{
    private static readonly Color Fill = new(1f, 1f, 1f, 0.18f);
    private static readonly Color PressedFill = new(1f, 1f, 1f, 0.4f);
    private static readonly Color Outline = new(1f, 1f, 1f, 0.55f);

    public StringName Action { get; init; } = "";
    public string Label { get; init; } = "";

    private int _finger = -1;

    public TouchButton() => MouseFilter = MouseFilterEnum.Stop;

    public static void SendAction(StringName action, bool pressed)
        => Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = pressed });

    public override bool _HasPoint(Vector2 point) => point.DistanceTo(Size / 2f) <= Size.X / 2f;

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventScreenTouch t)
        {
            if (t.Pressed && _finger < 0)
            {
                _finger = t.Index;
                SendAction(Action, true);
                QueueRedraw();
            }
            else if (!t.Pressed && t.Index == _finger)
                Release();
        }
        AcceptEvent();
    }

    public override void _Notification(int what)
    {
        if (what is (int)NotificationVisibilityChanged or (int)NotificationExitTree or (int)NotificationApplicationFocusOut)
            Release();
    }

    private void Release()
    {
        if (_finger < 0) return;
        _finger = -1;
        SendAction(Action, false);
        QueueRedraw();
    }

    public override void _Draw()
    {
        float r = Size.X / 2f;
        var center = Size / 2f;
        DrawCircle(center, r, _finger >= 0 ? PressedFill : Fill);
        DrawArc(center, r - 1f, 0f, Mathf.Tau, 48, Outline, 2f, true);

        var font = GetThemeDefaultFont();
        int fontSize = UiScaleApplier.Instance?.ScaleSize(12f) ?? 12;
        var text = font.GetStringSize(Label, HorizontalAlignment.Left, -1, fontSize);
        var origin = new Vector2(center.X - text.X / 2f, center.Y - text.Y / 2f + font.GetAscent(fontSize));
        DrawString(font, origin, Label, HorizontalAlignment.Left, -1, fontSize, Colors.White);
    }
}
