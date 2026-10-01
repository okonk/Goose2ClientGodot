using System;
using System.Collections.Generic;
using Godot;

namespace Goose2Client.InputBindings;

public interface IInputBindingLabelProvider
{
    string? GetKeyboardLabel(Key physicalKey);
}

public sealed class GodotInputBindingLabelProvider : IInputBindingLabelProvider
{
    public string? GetKeyboardLabel(Key physicalKey)
    {
        if (physicalKey == Key.None)
            return null;
        if ((string)DisplayServer.GetName() == "headless")
            return null;
        var label = OS.GetKeycodeString(DisplayServer.KeyboardGetLabelFromPhysical(physicalKey));
        return string.IsNullOrEmpty(label) ? null : label;
    }
}

public static class InputBindingDisplay
{
    public static string Format(InputBinding binding, IInputBindingLabelProvider labels)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(labels);

        switch (binding)
        {
            case InputBinding.Keyboard keyboard:
                var label = labels.GetKeyboardLabel(keyboard.PhysicalKey);
                var keyName = string.IsNullOrEmpty(label) ? keyboard.PhysicalKey.ToString() : label;
                return WithModifiers(Modifiers(keyboard.Ctrl, keyboard.Shift, keyboard.Alt, keyboard.Meta), keyName);
            case InputBinding.Mouse mouse:
                return WithModifiers(Modifiers(mouse.Ctrl, mouse.Shift, mouse.Alt, mouse.Meta), mouse.Button.ToString());
            case InputBinding.JoypadButton button:
                return $"Gamepad {button.Button}";
            case InputBinding.JoypadAxis axis:
                return $"Gamepad {axis.Axis} {(axis.Direction > 0 ? "+" : "-")}";
            default:
                throw new NotSupportedException($"Unsupported binding type {binding.GetType().Name}.");
        }
    }

    private static string Modifiers(bool ctrl, bool shift, bool alt, bool meta)
    {
        var parts = new List<string>(4);
        if (ctrl)
            parts.Add("Ctrl");
        if (shift)
            parts.Add("Shift");
        if (alt)
            parts.Add("Alt");
        if (meta)
            parts.Add("Meta");
        return string.Join("+", parts);
    }

    private static string WithModifiers(string modifiers, string key) =>
        modifiers.Length == 0 ? key : $"{modifiers}+{key}";
}
