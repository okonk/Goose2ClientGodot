using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace MapEditor.GameData.Rows;

public enum SpawnMoveOverride { Default, Movable, Stationary }

// Values match the server's Goose.Direction, which is what the "facing" key holds.
public enum SpawnFacingOverride { Default = 0, Up = 1, Right = 2, Down = 3, Left = 4 }

public static class SpawnPropertiesJson
{
    private const string CanMoveKey = "canMove";
    private const string FacingKey = "facing";

    private delegate bool ValueParser<T>(JsonElement element, out T value);

    public static bool TryRead(string properties, out SpawnMoveOverride value) =>
        TryReadKey(properties, CanMoveKey, TryParseCanMove, out value);

    public static bool TryReadFacing(string properties, out SpawnFacingOverride value) =>
        TryReadKey(properties, FacingKey, TryParseFacing, out value);

    public static bool TryWrite(string properties, SpawnMoveOverride value, out string result) =>
        TryWriteKey(properties, CanMoveKey, TryParseCanMove, value,
            writer => writer.WriteBooleanValue(value == SpawnMoveOverride.Movable), out result);

    public static bool TryWriteFacing(string properties, SpawnFacingOverride value, out string result) =>
        TryWriteKey(properties, FacingKey, TryParseFacing, value,
            writer => writer.WriteNumberValue((int)value), out result);

    private static bool TryParseCanMove(JsonElement element, out SpawnMoveOverride value)
    {
        // The server crashes on startup for non-boolean canMove values (e.g. {"canMove":1});
        // refuse to treat them as editable.
        value = element.ValueKind switch
        {
            JsonValueKind.True => SpawnMoveOverride.Movable,
            JsonValueKind.False => SpawnMoveOverride.Stationary,
            _ => SpawnMoveOverride.Default,
        };
        return value != SpawnMoveOverride.Default;
    }

    private static bool TryParseFacing(JsonElement element, out SpawnFacingOverride value)
    {
        value = SpawnFacingOverride.Default;
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out int number) || number is < 1 or > 4)
        {
            return false;
        }

        value = (SpawnFacingOverride)number;
        return true;
    }

    private static bool TryReadKey<T>(string properties, string key, ValueParser<T> parse, out T value)
        where T : struct, Enum
    {
        value = default;
        if (string.IsNullOrWhiteSpace(properties))
        {
            return true;
        }

        if (!TryGetObject(properties, out var document))
        {
            return false;
        }

        using (document)
        {
            return TryReadValue(document.RootElement, key, parse, out value);
        }
    }

    private static bool TryReadValue<T>(JsonElement root, string key, ValueParser<T> parse, out T value)
        where T : struct, Enum
    {
        value = default;
        return !root.TryGetProperty(key, out var element) || parse(element, out value);
    }

    private static bool TryWriteKey<T>(string properties, string key, ValueParser<T> parse, T value,
        Action<Utf8JsonWriter> writeValue, out string result)
        where T : struct, Enum
    {
        result = "";
        Action<Utf8JsonWriter>? write = EqualityComparer<T>.Default.Equals(value, default) ? null : writeValue;
        if (string.IsNullOrWhiteSpace(properties))
        {
            if (write is null)
            {
                return true;
            }

            return Write(key, write, null, ref result);
        }

        if (!TryGetObject(properties, out var document))
        {
            return false;
        }

        using (document)
        {
            if (!TryReadValue(document.RootElement, key, parse, out _))
            {
                return false;
            }

            return Write(key, write, document.RootElement, ref result);
        }
    }

    private static bool TryGetObject(string properties, out JsonDocument document)
    {
        document = null!;
        try
        {
            document = JsonDocument.Parse(properties);
        }
        catch (JsonException)
        {
            return false;
        }

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            document.Dispose();
            document = null!;
            return false;
        }

        return true;
    }

    private static bool Write(string key, Action<Utf8JsonWriter>? writeValue, JsonElement? root, ref string result)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }))
        {
            writer.WriteStartObject();
            var wroteKey = false;
            if (root is { } objectRoot)
            {
                foreach (var property in objectRoot.EnumerateObject())
                {
                    if (property.Name != key)
                    {
                        writer.WritePropertyName(property.Name);
                        property.Value.WriteTo(writer);
                        continue;
                    }

                    if (writeValue is null)
                    {
                        continue;
                    }

                    writer.WritePropertyName(key);
                    writeValue(writer);
                    wroteKey = true;
                }
            }

            if (!wroteKey && writeValue is not null)
            {
                writer.WritePropertyName(key);
                writeValue(writer);
            }

            writer.WriteEndObject();
        }

        if (stream.Length <= 2)
        {
            result = "";
        }
        else
        {
            result = Encoding.UTF8.GetString(stream.ToArray());
        }

        return true;
    }
}
