using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using MapEditor.Core;

namespace MapEditor.Rendering;

public static class TerrainCatalogJson
{
    private const int SupportedVersion = 1;
    private const string DefaultSourcePath = "<memory>";

    private static readonly string[] RootProperties = { "version", "terrains", "graphics" };
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly string[] TerrainProperties = { "id", "name", "color" };
    private static readonly string[] GraphicProperties =
    {
        "sheet", "graphic", "center", "north", "east", "south", "west",
        "northEast", "southEast", "southWest", "northWest"
    };

    public static TerrainCatalog Parse(string json, string sourcePath = DefaultSourcePath)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new TerrainCatalogFormatException($"Malformed JSON: {ex.Message}", sourcePath, ex);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw Fail(sourcePath, "Root must be a JSON object.");
            }

            RejectUnknownProperties(root, "root", RootProperties, sourcePath);

            if (!root.TryGetProperty("version", out var version))
            {
                throw Fail(sourcePath, "Missing property 'version'.");
            }

            if (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var versionValue))
            {
                throw Fail(sourcePath, "Property 'version' must be an integer.");
            }

            if (versionValue != SupportedVersion)
            {
                throw Fail(sourcePath, $"Unsupported version {versionValue}.");
            }

            if (!root.TryGetProperty("terrains", out var terrains))
            {
                throw Fail(sourcePath, "Missing property 'terrains'.");
            }

            if (!root.TryGetProperty("graphics", out var graphics))
            {
                throw Fail(sourcePath, "Missing property 'graphics'.");
            }

            return new TerrainCatalog(
                ParseTerrains(terrains, sourcePath),
                ParseGraphics(graphics, sourcePath));
        }
    }

    public static TerrainCatalog Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        string json;
        try
        {
            json = StrictUtf8.GetString(bytes);
        }
        catch (Exception ex) when (ex is DecoderFallbackException or ArgumentException)
        {
            throw Fail(path, $"Invalid UTF-8: {ex.Message}");
        }

        return Parse(json, path);
    }

    public static string Serialize(TerrainCatalog catalog)
    {
        using var stream = new MemoryStream();
        var options = new JsonWriterOptions { Indented = true };
        using (var writer = new Utf8JsonWriter(stream, options))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", SupportedVersion);

            writer.WritePropertyName("terrains");
            writer.WriteStartArray();
            foreach (var terrain in catalog.Terrains.OrderBy(t => t.Id.ToString()))
            {
                writer.WriteStartObject();
                writer.WriteString("id", terrain.Id.ToString());
                writer.WriteString("name", terrain.Name);
                if (terrain.ColorOverride is { } color)
                {
                    writer.WriteString("color", FormatColor(color));
                }
                else
                {
                    writer.WriteNull("color");
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            writer.WritePropertyName("graphics");
            writer.WriteStartArray();
            foreach (var graphic in catalog.Graphics
                .OrderBy(g => g.Reference.Sheet)
                .ThenBy(g => g.Reference.Graphic))
            {
                writer.WriteStartObject();
                writer.WriteNumber("sheet", graphic.Reference.Sheet);
                writer.WriteNumber("graphic", graphic.Reference.Graphic);
                WritePeer(writer, "center", graphic.Pattern.Center);
                WritePeer(writer, "north", graphic.Pattern.North);
                WritePeer(writer, "east", graphic.Pattern.East);
                WritePeer(writer, "south", graphic.Pattern.South);
                WritePeer(writer, "west", graphic.Pattern.West);
                WritePeer(writer, "northEast", graphic.Pattern.NorthEast);
                WritePeer(writer, "southEast", graphic.Pattern.SouthEast);
                WritePeer(writer, "southWest", graphic.Pattern.SouthWest);
                WritePeer(writer, "northWest", graphic.Pattern.NorthWest);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    private static List<TerrainDefinition> ParseTerrains(JsonElement array, string sourcePath)
    {
        if (array.ValueKind != JsonValueKind.Array)
        {
            throw Fail(sourcePath, "Property 'terrains' must be an array.");
        }

        var terrains = new List<TerrainDefinition>();
        for (var index = 0; index < array.GetArrayLength(); index++)
        {
            var element = array[index];
            var context = $"terrain at index {index}";
            if (element.ValueKind != JsonValueKind.Object)
            {
                throw Fail(sourcePath, $"{context} must be an object.");
            }

            RejectUnknownProperties(element, context, TerrainProperties, sourcePath);

            var id = ParseGuid(element, "id", context, sourcePath);
            var name = ParseRequiredString(element, "name", context, sourcePath);
            var color = ParseColor(element, context, sourcePath);
            terrains.Add(new TerrainDefinition(id, name, color));
        }

        return terrains;
    }

    private static List<TerrainGraphicDefinition> ParseGraphics(JsonElement array, string sourcePath)
    {
        if (array.ValueKind != JsonValueKind.Array)
        {
            throw Fail(sourcePath, "Property 'graphics' must be an array.");
        }

        var graphics = new List<TerrainGraphicDefinition>();
        for (var index = 0; index < array.GetArrayLength(); index++)
        {
            var element = array[index];
            var context = $"graphic at index {index}";
            if (element.ValueKind != JsonValueKind.Object)
            {
                throw Fail(sourcePath, $"{context} must be an object.");
            }

            RejectUnknownProperties(element, context, GraphicProperties, sourcePath);

            var sheet = ParseInt(element, "sheet", context, sourcePath);
            var graphic = ParseInt(element, "graphic", context, sourcePath);
            var pattern = new TerrainPattern(
                Center: ParsePeer(element, "center", context, sourcePath),
                North: ParsePeer(element, "north", context, sourcePath),
                East: ParsePeer(element, "east", context, sourcePath),
                South: ParsePeer(element, "south", context, sourcePath),
                West: ParsePeer(element, "west", context, sourcePath),
                NorthEast: ParsePeer(element, "northEast", context, sourcePath),
                SouthEast: ParsePeer(element, "southEast", context, sourcePath),
                SouthWest: ParsePeer(element, "southWest", context, sourcePath),
                NorthWest: ParsePeer(element, "northWest", context, sourcePath));
            graphics.Add(new TerrainGraphicDefinition(new TerrainGraphicReference(sheet, graphic), pattern));
        }

        return graphics;
    }

    private static void RejectUnknownProperties(JsonElement obj, string context, string[] known, string sourcePath)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in obj.EnumerateObject())
        {
            if (!seen.Add(property.Name))
            {
                throw Fail(sourcePath, $"Duplicate property '{property.Name}' in {context}.");
            }

            if (Array.IndexOf(known, property.Name) < 0)
            {
                throw Fail(sourcePath, $"Unknown property '{property.Name}' in {context}.");
            }
        }
    }

    private static string ParseRequiredString(JsonElement obj, string name, string context, string sourcePath)
    {
        if (!obj.TryGetProperty(name, out var value))
        {
            throw Fail(sourcePath, $"Missing property '{name}' in {context}.");
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw Fail(sourcePath, $"Property '{name}' in {context} must be a string.");
        }

        return value.GetString()!;
    }

    private static int ParseInt(JsonElement obj, string name, string context, string sourcePath)
    {
        if (!obj.TryGetProperty(name, out var value))
        {
            throw Fail(sourcePath, $"Missing property '{name}' in {context}.");
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result))
        {
            throw Fail(sourcePath, $"Property '{name}' in {context} must be a 32-bit integer.");
        }

        return result;
    }

    private static Guid ParseGuid(JsonElement obj, string name, string context, string sourcePath)
    {
        var value = ParseRequiredString(obj, name, context, sourcePath);
        if (!Guid.TryParse(value, out var guid))
        {
            throw Fail(sourcePath, $"Property '{name}' in {context} is not a valid GUID: '{value}'.");
        }

        return guid;
    }

    private static Guid? ParsePeer(JsonElement obj, string name, string context, string sourcePath)
    {
        if (!obj.TryGetProperty(name, out var value))
        {
            throw Fail(sourcePath, $"Missing property '{name}' in {context}.");
        }

        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw Fail(sourcePath, $"Property '{name}' in {context} must be a GUID string or null.");
        }

        var text = value.GetString()!;
        if (!Guid.TryParse(text, out var guid))
        {
            throw Fail(sourcePath, $"Property '{name}' in {context} is not a valid GUID: '{text}'.");
        }

        return guid;
    }

    private static TerrainColor? ParseColor(JsonElement obj, string context, string sourcePath)
    {
        if (!obj.TryGetProperty("color", out var value))
        {
            throw Fail(sourcePath, $"Missing property 'color' in {context}.");
        }

        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw Fail(sourcePath, $"Property 'color' in {context} must be a '#RRGGBB' string or null.");
        }

        var text = value.GetString()!;
        if (text.Length != 7
            || text[0] != '#'
            || !TryParseHex(text, 1, out var r)
            || !TryParseHex(text, 3, out var g)
            || !TryParseHex(text, 5, out var b))
        {
            throw Fail(sourcePath, $"Property 'color' in {context} is not a valid '#RRGGBB' color: '{text}'.");
        }

        return new TerrainColor(r, g, b);
    }

    private static readonly NumberStyles StrictHex = NumberStyles.HexNumber
        & ~NumberStyles.AllowLeadingWhite
        & ~NumberStyles.AllowTrailingWhite;

    private static bool TryParseHex(string text, int offset, out byte value)
    {
        try
        {
            value = byte.Parse(text.AsSpan(offset, 2), StrictHex);
            return true;
        }
        catch (FormatException)
        {
            value = 0;
            return false;
        }
    }

    private static string FormatColor(TerrainColor color)
    {
        return "#"
            + color.R.ToString("X2", CultureInfo.InvariantCulture)
            + color.G.ToString("X2", CultureInfo.InvariantCulture)
            + color.B.ToString("X2", CultureInfo.InvariantCulture);
    }

    private static void WritePeer(Utf8JsonWriter writer, string name, Guid? value)
    {
        if (value is { } guid)
        {
            writer.WriteString(name, guid.ToString());
        }
        else
        {
            writer.WriteNull(name);
        }
    }

    private static TerrainCatalogFormatException Fail(string sourcePath, string message)
        => new(message, sourcePath);
}
