using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Godot;

namespace Goose2Client.InputBindings;

public sealed record InputBindingParseResult(bool Success, IReadOnlyDictionary<string, IReadOnlyList<InputBinding>>? Overrides, string? Error)
{
    public static InputBindingParseResult Ok(IReadOnlyDictionary<string, IReadOnlyList<InputBinding>> overrides)
        => new(true, overrides, null);

    public static InputBindingParseResult Fail(string error)
        => new(false, null, error);
}

public static class InputBindingJson
{
    public const int SupportedVersion = 1;

    // Strict parsing: duplicate/unknown properties and wrong property order are rejected,
    // which default JsonSerializer DTO binding would silently accept.
    public static InputBindingParseResult Parse(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(bytes);
        }
        catch (JsonException ex)
        {
            return InputBindingParseResult.Fail($"Malformed JSON: {ex.Message}");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return InputBindingParseResult.Fail("Root must be a JSON object.");

            var rootProperties = root.EnumerateObject().ToList();
            if (rootProperties.Count != 2
                || rootProperties[0].Name != "version"
                || rootProperties[1].Name != "actions")
                return InputBindingParseResult.Fail("Root must contain exactly the properties 'version' then 'actions'.");

            if (rootProperties[0].Value.ValueKind != JsonValueKind.Number
                || !rootProperties[0].Value.TryGetInt64(out var version)
                || version != SupportedVersion)
                return InputBindingParseResult.Fail($"Unsupported version: {rootProperties[0].Value.GetRawText()}.");

            var actions = rootProperties[1].Value;
            if (actions.ValueKind != JsonValueKind.Object)
                return InputBindingParseResult.Fail("'actions' must be a JSON object.");

            var overrides = new Dictionary<string, IReadOnlyList<InputBinding>>();
            foreach (var property in actions.EnumerateObject())
            {
                if (!InputActionCatalog.IsRemappable(property.Name))
                    return InputBindingParseResult.Fail($"Unknown action: {property.Name}.");
                if (overrides.ContainsKey(property.Name))
                    return InputBindingParseResult.Fail($"Duplicate action: {property.Name}.");
                if (property.Value.ValueKind != JsonValueKind.Array)
                    return InputBindingParseResult.Fail($"Action '{property.Name}' must be a JSON array.");

                var bindings = new List<InputBinding>();
                foreach (var element in property.Value.EnumerateArray())
                {
                    var error = ParseBinding(element, out var binding);
                    if (error != null)
                        return InputBindingParseResult.Fail(error);
                    bindings.Add(binding);
                }

                var validation = InputBindingRules.ValidatePersisted(bindings);
                if (!validation.Success)
                    return InputBindingParseResult.Fail(validation.Error!);

                overrides[property.Name] = bindings.AsReadOnly();
            }

            return InputBindingParseResult.Ok(overrides);
        }
    }

    public static InputBindingParseResult Parse(string json)
        => Parse(Encoding.UTF8.GetBytes(json));

    private static string? ParseBinding(JsonElement element, out InputBinding binding)
    {
        binding = null!;
        if (element.ValueKind != JsonValueKind.Object)
            return "Each binding must be a JSON object.";

        var properties = element.EnumerateObject().ToList();
        if (properties.Count == 0
            || properties[0].Name != "kind"
            || properties[0].Value.ValueKind != JsonValueKind.String)
            return "Binding must start with a string 'kind' property.";

        var kind = properties[0].Value.GetString()!;
        switch (kind)
        {
            case "key":
            {
                var error = CheckShape(properties, "key", "physicalKey", "ctrl", "shift", "alt", "meta");
                if (error != null)
                    return error;

                var key = ReadEnum<Key>(properties[1], out error);
                if (key is null)
                    return error;

                var ctrl = ReadBool(properties[2], out error);
                if (error != null)
                    return error;
                var shift = ReadBool(properties[3], out error);
                if (error != null)
                    return error;
                var alt = ReadBool(properties[4], out error);
                if (error != null)
                    return error;
                var meta = ReadBool(properties[5], out error);
                if (error != null)
                    return error;

                binding = new InputBinding.Keyboard(key.Value, ctrl, shift, alt, meta);
                return null;
            }

            case "mouse":
            {
                var error = CheckShape(properties, "mouse", "button", "ctrl", "shift", "alt", "meta");
                if (error != null)
                    return error;

                var button = ReadEnum<MouseButton>(properties[1], out error);
                if (button is null)
                    return error;

                var ctrl = ReadBool(properties[2], out error);
                if (error != null)
                    return error;
                var shift = ReadBool(properties[3], out error);
                if (error != null)
                    return error;
                var alt = ReadBool(properties[4], out error);
                if (error != null)
                    return error;
                var meta = ReadBool(properties[5], out error);
                if (error != null)
                    return error;

                binding = new InputBinding.Mouse(button.Value, ctrl, shift, alt, meta);
                return null;
            }

            case "joyButton":
            {
                var error = CheckShape(properties, "joyButton", "button");
                if (error != null)
                    return error;

                var button = ReadEnum<JoyButton>(properties[1], out error);
                if (button is null)
                    return error;

                binding = new InputBinding.JoypadButton(button.Value);
                return null;
            }

            case "joyAxis":
            {
                var error = CheckShape(properties, "joyAxis", "axis", "direction");
                if (error != null)
                    return error;

                var axis = ReadEnum<JoyAxis>(properties[1], out error);
                if (axis is null)
                    return error;

                var direction = ReadInt64(properties[2], out error);
                if (error != null)
                    return error;
                if (direction is not (-1 or 1))
                    return "Axis direction must be -1 or 1.";

                binding = new InputBinding.JoypadAxis(axis.Value, (int)direction);
                return null;
            }

            default:
                return $"Unknown binding kind: {kind}.";
        }
    }

    private static string? CheckShape(IReadOnlyList<JsonProperty> properties, string kind, params string[] expected)
    {
        if (properties.Count != expected.Length + 1)
            return $"Binding kind '{kind}' must have exactly the properties: kind, {string.Join(", ", expected)}.";

        for (var i = 0; i < expected.Length; i++)
        {
            if (properties[i + 1].Name != expected[i])
                return $"Binding kind '{kind}' expects property '{expected[i]}' but found '{properties[i + 1].Name}'.";
        }

        return null;
    }

    private static bool ReadBool(JsonProperty property, out string? error)
    {
        if (property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            error = null;
            return property.Value.GetBoolean();
        }

        error = $"Binding property '{property.Name}' must be a boolean.";
        return false;
    }

    private static long ReadInt64(JsonProperty property, out string? error)
    {
        if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt64(out var value))
        {
            error = null;
            return value;
        }

        error = $"Binding property '{property.Name}' must be an integer.";
        return 0;
    }

    private static T? ReadEnum<T>(JsonProperty property, out string? error) where T : struct, Enum
    {
        var value = ReadInt64(property, out error);
        if (error != null)
            return null;

        var enumValue = (T)Enum.ToObject(typeof(T), Convert.ChangeType(value, typeof(T).GetEnumUnderlyingType()));
        if (!Enum.IsDefined<T>(enumValue))
        {
            error = $"Binding property '{property.Name}' is not a supported {typeof(T).Name} value.";
            return null;
        }

        return enumValue;
    }

    public static byte[] Serialize(InputBindingSet active, InputBindingSet factory)
    {
        ArgumentNullException.ThrowIfNull(active);
        ArgumentNullException.ThrowIfNull(factory);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", SupportedVersion);
            writer.WritePropertyName("actions");
            writer.WriteStartObject();

            foreach (var action in InputActionCatalog.Actions)
            {
                var bindings = active.GetBindings(action.Name);
                var defaults = factory.GetBindings(action.Name);
                if (bindings.SequenceEqual(defaults))
                    continue;

                writer.WritePropertyName(action.Name);
                writer.WriteStartArray();
                foreach (var binding in bindings)
                    WriteBinding(writer, binding);
                writer.WriteEndArray();
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static void WriteBinding(Utf8JsonWriter writer, InputBinding binding)
    {
        switch (binding)
        {
            case InputBinding.Keyboard keyboard:
                writer.WriteStartObject();
                writer.WriteString("kind", "key");
                writer.WriteNumber("physicalKey", (long)keyboard.PhysicalKey);
                writer.WriteBoolean("ctrl", keyboard.Ctrl);
                writer.WriteBoolean("shift", keyboard.Shift);
                writer.WriteBoolean("alt", keyboard.Alt);
                writer.WriteBoolean("meta", keyboard.Meta);
                writer.WriteEndObject();
                break;

            case InputBinding.Mouse mouse:
                writer.WriteStartObject();
                writer.WriteString("kind", "mouse");
                writer.WriteNumber("button", (long)mouse.Button);
                writer.WriteBoolean("ctrl", mouse.Ctrl);
                writer.WriteBoolean("shift", mouse.Shift);
                writer.WriteBoolean("alt", mouse.Alt);
                writer.WriteBoolean("meta", mouse.Meta);
                writer.WriteEndObject();
                break;

            case InputBinding.JoypadButton joypadButton:
                writer.WriteStartObject();
                writer.WriteString("kind", "joyButton");
                writer.WriteNumber("button", (long)joypadButton.Button);
                writer.WriteEndObject();
                break;

            case InputBinding.JoypadAxis joypadAxis:
                writer.WriteStartObject();
                writer.WriteString("kind", "joyAxis");
                writer.WriteNumber("axis", (long)joypadAxis.Axis);
                writer.WriteNumber("direction", joypadAxis.Direction);
                writer.WriteEndObject();
                break;
        }
    }
}
