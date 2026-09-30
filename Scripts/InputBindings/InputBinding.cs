using System;
using System.Collections.Generic;
using Godot;

namespace Goose2Client.InputBindings;

public abstract record InputBinding
{
    public sealed record Keyboard(Key PhysicalKey, bool Ctrl, bool Shift, bool Alt, bool Meta) : InputBinding;

    public sealed record Mouse(MouseButton Button, bool Ctrl, bool Shift, bool Alt, bool Meta) : InputBinding;

    public sealed record JoypadButton(JoyButton Button) : InputBinding;

    public sealed record JoypadAxis(JoyAxis Axis, int Direction) : InputBinding;
}

public sealed record InputBindingValidationResult(bool Success, IReadOnlyList<InputBinding> Bindings, string? Error);

public static class InputBindingRules
{
    // Godot encodes modifier state in the top nibble of Key values (KEY_MASK_*).
    private const int ModifierMaskBits = unchecked((int)0xF0000000);

    public static InputBindingValidationResult NormalizeForEditor(IEnumerable<InputBinding> bindings)
    {
        var result = new List<InputBinding>();
        var seen = new HashSet<InputBinding>();

        foreach (var binding in bindings)
        {
            var error = Validate(binding);
            if (error != null)
                return new InputBindingValidationResult(false, Array.Empty<InputBinding>(), error);

            if (seen.Add(binding))
                result.Add(binding);
        }

        return new InputBindingValidationResult(true, result.AsReadOnly(), null);
    }

    public static InputBindingValidationResult ValidatePersisted(IEnumerable<InputBinding> bindings)
    {
        var seen = new HashSet<InputBinding>();

        foreach (var binding in bindings)
        {
            var error = Validate(binding);
            if (error != null)
                return new InputBindingValidationResult(false, Array.Empty<InputBinding>(), error);

            if (!seen.Add(binding))
                return new InputBindingValidationResult(false, Array.Empty<InputBinding>(), $"Duplicate binding: {binding}");
        }

        return new InputBindingValidationResult(true, Array.Empty<InputBinding>(), null);
    }

    private static string? Validate(InputBinding binding)
    {
        if (binding is null)
            return "Binding must not be null.";

        switch (binding)
        {
            case InputBinding.Keyboard { PhysicalKey: Key.None }:
                return "Keyboard binding requires a physical key.";
            case InputBinding.Keyboard { PhysicalKey: Key.Ctrl or Key.Shift or Key.Alt or Key.Meta }:
                return "Modifier keys cannot be standalone bindings.";
            case InputBinding.Keyboard keyboard when ((int)keyboard.PhysicalKey & ModifierMaskBits) != 0:
                return "Physical key value must not embed modifier mask bits.";
            case InputBinding.Mouse { Button: MouseButton.None or MouseButton.Left or MouseButton.Right }:
                return "Left and right mouse buttons are reserved.";
            case InputBinding.JoypadButton { Button: JoyButton.Invalid or >= JoyButton.Max }:
                return "Invalid gamepad button.";
            case InputBinding.JoypadAxis { Axis: JoyAxis.Invalid or >= JoyAxis.Max }:
                return "Invalid gamepad axis.";
            case InputBinding.JoypadAxis { Direction: not (-1 or 1) }:
                return "Axis direction must be -1 or +1.";
            default:
                return null;
        }
    }
}
