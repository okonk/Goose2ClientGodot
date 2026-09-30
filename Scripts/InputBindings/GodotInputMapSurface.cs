using System;
using System.Collections.Generic;
using Godot;

namespace Goose2Client.InputBindings;

public sealed class GodotInputMapSurface : IInputMapSurface
{
    public bool HasAction(string action) => InputMap.HasAction(action);

    public float GetActionDeadzone(string action) => InputMap.ActionGetDeadzone(action);

    public IReadOnlyList<InputMapEventDescriptor> GetActionEvents(string action)
    {
        var result = new List<InputMapEventDescriptor>();
        foreach (var @event in InputMap.ActionGetEvents(action))
            result.Add(ToDescriptor(@event));

        return result;
    }

    public void EraseActionEvents(string action) => InputMap.ActionEraseEvents(action);

    public void AddActionEvent(string action, InputMapEventDescriptor descriptor) =>
        InputMap.ActionAddEvent(action, ToEvent(descriptor));

    private static InputMapEventDescriptor ToDescriptor(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventKey key:
                return new InputMapEventDescriptor(
                    InputMapEventKind.Keyboard, key.PhysicalKeycode, key.Keycode, (uint)key.Unicode,
                    key.CtrlPressed, key.ShiftPressed, key.AltPressed, key.MetaPressed,
                    MouseButton.None, JoyButton.Invalid, JoyAxis.Invalid, 0f, 0);
            case InputEventMouseButton mouse:
                return new InputMapEventDescriptor(
                    InputMapEventKind.Mouse, Key.None, Key.None, 0,
                    mouse.CtrlPressed, mouse.ShiftPressed, mouse.AltPressed, mouse.MetaPressed,
                    mouse.ButtonIndex, JoyButton.Invalid, JoyAxis.Invalid, 0f, 0);
            case InputEventJoypadButton button:
                return new InputMapEventDescriptor(
                    InputMapEventKind.JoypadButton, Key.None, Key.None, 0,
                    false, false, false, false,
                    MouseButton.None, button.ButtonIndex, JoyAxis.Invalid, 0f, button.Device);
            case InputEventJoypadMotion motion:
                return new InputMapEventDescriptor(
                    InputMapEventKind.JoypadAxis, Key.None, Key.None, 0,
                    false, false, false, false,
                    MouseButton.None, JoyButton.Invalid, motion.Axis, motion.AxisValue, motion.Device);
            default:
                throw new InputMapConfigurationException($"Unsupported factory event class: {@event.GetType().Name}.");
        }
    }

    private static InputEvent ToEvent(InputMapEventDescriptor d)
    {
        switch (d.Kind)
        {
            case InputMapEventKind.Keyboard:
                return new InputEventKey
                {
                    PhysicalKeycode = d.PhysicalKey,
                    CtrlPressed = d.Ctrl,
                    ShiftPressed = d.Shift,
                    AltPressed = d.Alt,
                    MetaPressed = d.Meta
                };
            case InputMapEventKind.Mouse:
                return new InputEventMouseButton
                {
                    ButtonIndex = d.MouseButton,
                    CtrlPressed = d.Ctrl,
                    ShiftPressed = d.Shift,
                    AltPressed = d.Alt,
                    MetaPressed = d.Meta
                };
            case InputMapEventKind.JoypadButton:
                return new InputEventJoypadButton
                {
                    Device = -1,
                    ButtonIndex = d.JoyButton
                };
            case InputMapEventKind.JoypadAxis:
                return new InputEventJoypadMotion
                {
                    Device = -1,
                    Axis = d.JoyAxis,
                    AxisValue = d.AxisValue
                };
            default:
                throw new InputMapConfigurationException($"Unsupported event kind: {d.Kind}.");
        }
    }
}
