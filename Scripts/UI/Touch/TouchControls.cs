using Godot;
using System.Linq;

namespace Goose2Client.UI;

public partial class TouchControls : Control, IScalableWindow
{
    public const string DesktopFlag = "--touch-controls";

    private const float JoystickInset = 170f;
    private const float Margin = 16f;
    private const float JoystickDiameter = 150f;
    private const float AttackDiameter = 88f;
    private const float SmallDiameter = 48f;
    private const float ArcRadius = 112f;
    private const float ToolbarClearance = 56f;

    public static bool Enabled { get; private set; }

    public static float ActiveLeftInset => Enabled ? JoystickInset : 0f;

    private TouchJoystick _joystick = null!;
    private TouchButton _attack = null!;
    private TouchButton[] _arc = null!;

    public static void Configure()
    {
        bool desktop = OS.GetCmdlineUserArgs().Contains(DesktopFlag);
        Enabled = OS.HasFeature("android") || desktop;
        if (desktop)
            Input.EmulateTouchFromMouse = true;
    }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _joystick = new TouchJoystick();
        AddChild(_joystick);
        _attack = new TouchButton { Action = "Attack", Label = "Attack" };
        AddChild(_attack);
        _arc =
        [
            new TouchButton { Action = "ToggleInventory", Label = "Inv" },
            new TouchButton { Action = "ToggleBackpack", Label = "Bag" },
            new TouchButton { Action = "ToggleSpellbook", Label = "Spells" },
            new TouchButton { Action = "ToggleMount", Label = "Mount" },
        ];
        foreach (var b in _arc)
            AddChild(b);

        var applier = UiScaleApplier.Instance!;
        applier.RegisterWindow(this);
        TreeExited += () => applier.UnregisterWindow(this);
        Resized += Relayout;
        Relayout();
    }

    public void Relayout()
    {
        float f = UiScaleApplier.Instance!.Factor;
        var canvas = Size;

        float joystick = JoystickDiameter * f;
        _joystick.Position = new Vector2(Margin * f, canvas.Y - Margin * f - joystick);
        _joystick.Size = new Vector2(joystick, joystick);

        var attackCenter = new Vector2(
            canvas.X - (Margin + AttackDiameter / 2f) * f,
            canvas.Y - (ToolbarClearance + AttackDiameter / 2f) * f);
        Place(_attack, attackCenter, AttackDiameter * f);

        for (int i = 0; i < _arc.Length; i++)
        {
            float angle = Mathf.DegToRad(180f + 90f * i / (_arc.Length - 1));
            Place(_arc[i], attackCenter + Vector2.FromAngle(angle) * ArcRadius * f, SmallDiameter * f);
        }
    }

    private static void Place(Control c, Vector2 center, float diameter)
    {
        c.Size = new Vector2(diameter, diameter);
        c.Position = center - c.Size / 2f;
        c.QueueRedraw();
    }
}
