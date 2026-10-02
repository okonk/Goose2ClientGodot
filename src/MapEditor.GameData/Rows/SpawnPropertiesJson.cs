using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace MapEditor.GameData.Rows;

public enum SpawnMoveOverride { Default, Movable, Stationary }

public static class SpawnPropertiesJson
{
    public static bool TryRead(string properties, out SpawnMoveOverride value)
    {
        value = SpawnMoveOverride.Default;
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
            return TryReadValue(document.RootElement, out value);
        }
    }

    private static bool TryReadValue(JsonElement root, out SpawnMoveOverride value)
    {
        value = SpawnMoveOverride.Default;
        if (!root.TryGetProperty("canMove", out var canMove))
        {
            return true;
        }

        // The server crashes on startup for non-boolean canMove values (e.g. {"canMove":1});
        // refuse to treat them as editable.
        if (canMove.ValueKind == JsonValueKind.True)
        {
            value = SpawnMoveOverride.Movable;
            return true;
        }

        if (canMove.ValueKind == JsonValueKind.False)
        {
            value = SpawnMoveOverride.Stationary;
            return true;
        }

        return false;
    }

    public static bool TryWrite(string properties, SpawnMoveOverride value, out string result)
    {
        result = "";
        if (string.IsNullOrWhiteSpace(properties))
        {
            if (value == SpawnMoveOverride.Default)
            {
                return true;
            }

            return Write(value, null, ref result);
        }

        if (!TryGetObject(properties, out var document))
        {
            return false;
        }

        using (document)
        {
            if (!TryReadValue(document.RootElement, out _))
            {
                return false;
            }

            return Write(value, document.RootElement, ref result);
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

    private static bool Write(SpawnMoveOverride value, JsonElement? root, ref string result)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }))
        {
            writer.WriteStartObject();
            var wroteCanMove = false;
            if (root is { } objectRoot)
            {
                foreach (var property in objectRoot.EnumerateObject())
                {
                    if (property.Name != "canMove")
                    {
                        writer.WritePropertyName(property.Name);
                        property.Value.WriteTo(writer);
                        continue;
                    }

                    if (value == SpawnMoveOverride.Default)
                    {
                        continue;
                    }

                    writer.WriteBoolean("canMove", value == SpawnMoveOverride.Movable);
                    wroteCanMove = true;
                }
            }

            if (!wroteCanMove)
            {
                switch (value)
                {
                    case SpawnMoveOverride.Movable:
                        writer.WriteBoolean("canMove", true);
                        break;
                    case SpawnMoveOverride.Stationary:
                        writer.WriteBoolean("canMove", false);
                        break;
                }
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
