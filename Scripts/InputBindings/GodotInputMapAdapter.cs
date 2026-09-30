using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Goose2Client.InputBindings;

public sealed class GodotInputMapAdapter : IInputMapAdapter
{
    private readonly IInputMapSurface _surface;

    public GodotInputMapAdapter(IInputMapSurface surface) => _surface = surface;

    public InputBindingSet CaptureFactory(IReadOnlyList<InputActionDefinition> catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var bindings = new Dictionary<string, IReadOnlyList<InputBinding>>(catalog.Count);
        foreach (var action in catalog)
        {
            if (!_surface.HasAction(action.Name))
                throw new InputMapConfigurationException($"Factory action '{action.Name}' is missing from the runtime input map.");

            _surface.GetActionDeadzone(action.Name);

            var descriptors = _surface.GetActionEvents(action.Name);
            var converted = new List<InputBinding>(descriptors.Count);
            foreach (var descriptor in descriptors)
            {
                var error = ValidateFactoryDescriptor(action.Name, descriptor);
                if (error != null)
                    throw new InputMapConfigurationException(error);

                converted.Add(ToBinding(descriptor));
            }

            var validation = InputBindingRules.ValidatePersisted(converted);
            if (!validation.Success)
                throw new InputMapConfigurationException($"Factory action '{action.Name}': {validation.Error}");

            bindings[action.Name] = converted.AsReadOnly();
        }

        return new InputBindingSet(bindings);
    }

    public void Replace(InputBindingSet previous, InputBindingSet next)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(next);

        var rollback = PrepareAll(previous);
        var incoming = PrepareAll(next);

        try
        {
            foreach (var action in InputActionCatalog.Actions)
            {
                _surface.EraseActionEvents(action.Name);
                foreach (var descriptor in incoming[action.Name])
                    _surface.AddActionEvent(action.Name, descriptor);
            }
        }
        catch (Exception original)
        {
            Exception? restorationFailure = null;
            foreach (var action in InputActionCatalog.Actions)
            {
                try
                {
                    _surface.EraseActionEvents(action.Name);
                    foreach (var descriptor in rollback[action.Name])
                        _surface.AddActionEvent(action.Name, descriptor);
                }
                catch (Exception failure)
                {
                    restorationFailure ??= failure;
                }
            }

            throw new InputMapAdapterFailure(restorationFailure == null, original, restorationFailure);
        }
    }

    private static Dictionary<string, IReadOnlyList<InputMapEventDescriptor>> PrepareAll(InputBindingSet set)
    {
        var prepared = new Dictionary<string, IReadOnlyList<InputMapEventDescriptor>>();
        foreach (var action in InputActionCatalog.Actions)
        {
            var result = InputBindingRules.ValidatePersisted(set.GetBindings(action.Name));
            if (!result.Success)
                throw new InputMapConfigurationException($"Action '{action.Name}': {result.Error}");

            prepared[action.Name] = set.GetBindings(action.Name)
                .Select(ToDescriptor)
                .ToList()
                .AsReadOnly();
        }

        return prepared;
    }

    private static string? ValidateFactoryDescriptor(string action, InputMapEventDescriptor descriptor)
    {
        switch (descriptor.Kind)
        {
            case InputMapEventKind.Keyboard:
                if (descriptor.PhysicalKey == Key.None)
                    return $"Factory action '{action}' has a keyboard event without a physical key.";
                if (descriptor.LogicalKey != Key.None)
                    return $"Factory action '{action}' has a logical keyboard event {descriptor.LogicalKey}.";
                if (descriptor.Unicode != 0)
                    return $"Factory action '{action}' has a unicode keyboard event (U+{descriptor.Unicode:X4}).";
                return null;
            case InputMapEventKind.Mouse:
                if (descriptor.MouseButton is MouseButton.None or MouseButton.Left or MouseButton.Right or > MouseButton.Xbutton2)
                    return $"Factory action '{action}' has an invalid mouse event {descriptor.MouseButton}.";
                return null;
            case InputMapEventKind.JoypadButton:
                if (descriptor.JoyButton is JoyButton.Invalid or >= JoyButton.Max)
                    return $"Factory action '{action}' has an invalid joypad button event {descriptor.JoyButton}.";
                return null;
            case InputMapEventKind.JoypadAxis:
                if (descriptor.JoyAxis is JoyAxis.Invalid or >= JoyAxis.Max)
                    return $"Factory action '{action}' has an invalid joypad axis event {descriptor.JoyAxis}.";
                if (descriptor.AxisValue is not (-1f or +1f))
                    return $"Factory action '{action}' has a non-canonical axis value {descriptor.AxisValue} for {descriptor.JoyAxis}.";
                return null;
            default:
                return null;
        }
    }

    private static InputMapEventDescriptor ToDescriptor(InputBinding binding) => binding switch
    {
        InputBinding.Keyboard k => InputMapEventDescriptor.Keyboard(k.PhysicalKey, k.Ctrl, k.Shift, k.Alt, k.Meta),
        InputBinding.Mouse m => InputMapEventDescriptor.Mouse(m.Button, m.Ctrl, m.Shift, m.Alt, m.Meta),
        InputBinding.JoypadButton j => InputMapEventDescriptor.JoypadButton(j.Button),
        InputBinding.JoypadAxis a => InputMapEventDescriptor.JoypadAxis(a.Axis, a.Direction),
        _ => throw new InputMapConfigurationException($"Unsupported binding: {binding}")
    };

    private static InputBinding ToBinding(InputMapEventDescriptor descriptor) => descriptor.Kind switch
    {
        InputMapEventKind.Keyboard => new InputBinding.Keyboard(descriptor.PhysicalKey, descriptor.Ctrl, descriptor.Shift, descriptor.Alt, descriptor.Meta),
        InputMapEventKind.Mouse => new InputBinding.Mouse(descriptor.MouseButton, descriptor.Ctrl, descriptor.Shift, descriptor.Alt, descriptor.Meta),
        InputMapEventKind.JoypadButton => new InputBinding.JoypadButton(descriptor.JoyButton),
        InputMapEventKind.JoypadAxis => new InputBinding.JoypadAxis(descriptor.JoyAxis, (int)descriptor.AxisValue),
        _ => throw new InputMapConfigurationException($"Unsupported event kind: {descriptor.Kind}")
    };
}
