using System;
using Godot;

namespace Goose2Client.InputBindings;

public interface IInputReleaseState
{
    bool IsPhysicalKeyPressed(Key key);

    bool IsMouseButtonPressed(MouseButton button);

    bool IsJoyButtonPressed(int device, JoyButton button);

    float GetJoyAxis(int device, JoyAxis axis);
}

public enum InputReleaseGateKind
{
    Immediate,
    NextFrame,
    PhysicalKey,
    MouseButton,
    JoypadButton,
    JoypadAxis
}

public sealed record InputReleaseGate(
    InputReleaseGateKind Kind,
    Key PhysicalKey = Key.None,
    MouseButton MouseButton = MouseButton.None,
    int JoypadDevice = -1,
    JoyButton JoypadButton = JoyButton.Invalid,
    JoyAxis JoypadAxis = JoyAxis.Invalid)
{
    public static InputReleaseGate Immediate => new(InputReleaseGateKind.Immediate);

    public static InputReleaseGate NextFrame => new(InputReleaseGateKind.NextFrame);

    public static InputReleaseGate ForPhysicalKey(Key key) =>
        new(InputReleaseGateKind.PhysicalKey, PhysicalKey: key);

    public static InputReleaseGate ForMouseButton(MouseButton button) =>
        new(InputReleaseGateKind.MouseButton, MouseButton: button);

    public static InputReleaseGate ForJoypadButton(int device, JoyButton button) =>
        new(InputReleaseGateKind.JoypadButton, JoypadDevice: device, JoypadButton: button);

    public static InputReleaseGate ForJoypadAxis(int device, JoyAxis axis) =>
        new(InputReleaseGateKind.JoypadAxis, JoypadDevice: device, JoypadAxis: axis);

    public bool IsSatisfied(IInputReleaseState input)
    {
        ArgumentNullException.ThrowIfNull(input);

        return Kind switch
        {
            InputReleaseGateKind.Immediate or InputReleaseGateKind.NextFrame => true,
            InputReleaseGateKind.PhysicalKey => !input.IsPhysicalKeyPressed(PhysicalKey),
            InputReleaseGateKind.MouseButton => !input.IsMouseButtonPressed(MouseButton),
            InputReleaseGateKind.JoypadButton => !input.IsJoyButtonPressed(JoypadDevice, JoypadButton),
            InputReleaseGateKind.JoypadAxis =>
                MathF.Abs(input.GetJoyAxis(JoypadDevice, JoypadAxis)) <= KeyBindingCaptureState.NeutralThreshold,
            _ => true
        };
    }
}
