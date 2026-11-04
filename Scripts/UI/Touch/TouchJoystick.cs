using Godot;

namespace Goose2Client.UI;

public partial class TouchJoystick : Control
{
    public const int Up = 1, Down = 2, Left = 4, Right = 8;

    private const float DeadZone = 0.3f;
    private const float CardinalHalfAngle = 30f;

    private static readonly (int Bit, StringName Action)[] Actions =
        [(Up, "MoveUp"), (Down, "MoveDown"), (Left, "MoveLeft"), (Right, "MoveRight")];

    private static readonly Color BaseFill = new(1f, 1f, 1f, 0.12f);
    private static readonly Color Outline = new(1f, 1f, 1f, 0.45f);
    private static readonly Color KnobFill = new(1f, 1f, 1f, 0.35f);

    private int _finger = -1;
    private Vector2 _knob;
    private int _mask;

    public TouchJoystick() => MouseFilter = MouseFilterEnum.Stop;

    /// <summary><paramref name="v"/> is the knob offset normalized to the base radius (y down).
    /// Diagonals get a narrower band than cardinals so a slightly-off drag still walks straight.</summary>
    public static int DirectionMask(Vector2 v)
    {
        if (v.Length() < DeadZone) return 0;
        float deg = Mathf.PosMod(Mathf.RadToDeg(v.Angle()), 360f);
        if (Near(deg, 0f) || Near(deg, 360f)) return Right;
        if (Near(deg, 90f)) return Down;
        if (Near(deg, 180f)) return Left;
        if (Near(deg, 270f)) return Up;
        return deg < 90f ? Right | Down : deg < 180f ? Down | Left : deg < 270f ? Left | Up : Up | Right;
    }

    private static bool Near(float deg, float target) => Mathf.Abs(deg - target) <= CardinalHalfAngle;

    public override bool _HasPoint(Vector2 point) => point.DistanceTo(Size / 2f) <= Size.X / 2f;

    public override void _GuiInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventScreenTouch { Pressed: true } t when _finger < 0:
                _finger = t.Index;
                Track(t.Position);
                break;
            case InputEventScreenTouch { Pressed: false } t when t.Index == _finger:
                Release();
                break;
            case InputEventScreenDrag d when d.Index == _finger:
                Track(d.Position);
                break;
        }
        AcceptEvent();
    }

    public override void _Notification(int what)
    {
        if (what is (int)NotificationVisibilityChanged or (int)NotificationExitTree or (int)NotificationApplicationFocusOut)
            Release();
    }

    private void Track(Vector2 position)
    {
        float r = Size.X / 2f;
        _knob = (position - Size / 2f).LimitLength(r);
        SetMask(DirectionMask(_knob / r));
        QueueRedraw();
    }

    private void Release()
    {
        _finger = -1;
        _knob = Vector2.Zero;
        SetMask(0);
        QueueRedraw();
    }

    private void SetMask(int mask)
    {
        foreach (var (bit, action) in Actions)
            if ((mask & bit) != (_mask & bit))
                TouchButton.SendAction(action, (mask & bit) != 0);
        _mask = mask;
    }

    public override void _Draw()
    {
        float r = Size.X / 2f;
        var center = Size / 2f;
        DrawCircle(center, r, BaseFill);
        DrawArc(center, r - 1f, 0f, Mathf.Tau, 64, Outline, 2f, true);
        DrawCircle(center + _knob, r * 0.4f, KnobFill);
    }
}
