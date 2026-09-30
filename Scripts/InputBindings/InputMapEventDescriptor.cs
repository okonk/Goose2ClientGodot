using Godot;

namespace Goose2Client.InputBindings;

public enum InputMapEventKind
{
    Keyboard,
    Mouse,
    JoypadButton,
    JoypadAxis
}

public sealed record InputMapEventDescriptor(
    InputMapEventKind Kind,
    Key PhysicalKey,
    Key LogicalKey,
    uint Unicode,
    bool Ctrl,
    bool Shift,
    bool Alt,
    bool Meta,
    MouseButton MouseButton,
    JoyButton JoyButton,
    JoyAxis JoyAxis,
    float AxisValue,
    int Device)
{
    public static InputMapEventDescriptor Keyboard(Key physicalKey, bool ctrl, bool shift, bool alt, bool meta) =>
        new(InputMapEventKind.Keyboard, physicalKey, Key.None, 0, ctrl, shift, alt, meta,
            MouseButton.None, JoyButton.Invalid, JoyAxis.Invalid, 0f, 0);

    public static InputMapEventDescriptor Mouse(MouseButton button, bool ctrl, bool shift, bool alt, bool meta) =>
        new(InputMapEventKind.Mouse, Key.None, Key.None, 0, ctrl, shift, alt, meta,
            button, JoyButton.Invalid, JoyAxis.Invalid, 0f, 0);

    public static InputMapEventDescriptor JoypadButton(JoyButton button) =>
        new(InputMapEventKind.JoypadButton, Key.None, Key.None, 0, false, false, false, false,
            MouseButton.None, button, JoyAxis.Invalid, 0f, -1);

    public static InputMapEventDescriptor JoypadAxis(JoyAxis axis, float axisValue) =>
        new(InputMapEventKind.JoypadAxis, Key.None, Key.None, 0, false, false, false, false,
            MouseButton.None, JoyButton.Invalid, axis, axisValue, -1);
}
