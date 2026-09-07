using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace MapEditor.Rendering;

public readonly record struct AppearancePartEntry(SpriteReference? NoEquip, SpriteReference? Equip);

public sealed class AppearanceManifest
{
    public const int RequiredVersion = 1;

    private readonly IReadOnlyDictionary<AppearancePartKind, IReadOnlyDictionary<int, AppearancePartEntry>> _parts;

    private AppearanceManifest(IReadOnlyDictionary<AppearancePartKind, IReadOnlyDictionary<int, AppearancePartEntry>> parts)
    {
        _parts = parts;
    }

    public bool TryGetReference(AppearancePartKind kind, int id, int bodyState, out SpriteReference reference)
    {
        if (_parts.TryGetValue(kind, out IReadOnlyDictionary<int, AppearancePartEntry>? entries)
            && entries.TryGetValue(id, out AppearancePartEntry entry))
        {
            // BodyState 3 is the live client's idle candidate, which prefers the no-equip clip.
            SpriteReference? preferred = bodyState == 3 ? entry.NoEquip : entry.Equip;
            SpriteReference? fallback = bodyState == 3 ? entry.Equip : entry.NoEquip;
            reference = preferred ?? fallback ?? default;
            return reference != default;
        }

        reference = default;
        return false;
    }

    public static AppearanceManifest Parse(string json, string sourcePath = "<memory>")
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new AppearanceManifestException(
                AppearanceManifestError.MalformedJson,
                sourcePath,
                $"Appearance manifest is not valid JSON: {sourcePath}: {ex.Message}",
                ex);
        }

        using (document)
        {
            return ParseDocument(document.RootElement, sourcePath);
        }
    }

    public static AppearanceManifest Load(string assetDirectory)
    {
        if (string.IsNullOrWhiteSpace(assetDirectory))
        {
            throw new ArgumentException("Asset directory is required.", nameof(assetDirectory));
        }

        string manifestPath = Path.Combine(Path.GetFullPath(assetDirectory), "appearance-manifest.json");

        if (Directory.Exists(manifestPath))
        {
            throw new AppearanceManifestException(
                AppearanceManifestError.ReadFailed,
                manifestPath,
                $"Appearance manifest path is a directory: {manifestPath}",
                new IOException($"Appearance manifest path is a directory: {manifestPath}"));
        }

        if (!File.Exists(manifestPath))
        {
            throw new AppearanceManifestException(
                AppearanceManifestError.ManifestNotFound,
                manifestPath,
                $"Appearance manifest not found: {manifestPath}");
        }

        string json;
        try
        {
            json = File.ReadAllText(manifestPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new AppearanceManifestException(
                AppearanceManifestError.ReadFailed,
                manifestPath,
                $"Failed to read appearance manifest: {manifestPath}",
                ex);
        }

        return Parse(json, manifestPath);
    }

    private static AppearanceManifest ParseDocument(JsonElement root, string sourcePath)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw Fail(AppearanceManifestError.InvalidRoot, sourcePath, "Appearance manifest root must be a JSON object.");
        }

        int? version = null;
        int versionCount = 0;
        int partsCount = 0;
        JsonElement? parts = null;

        foreach (JsonProperty property in root.EnumerateObject())
        {
            switch (property.Name)
            {
                case "version":
                    versionCount++;
                    if (versionCount == 1 && property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out int value))
                    {
                        version = value;
                    }
                    break;
                case "parts":
                    partsCount++;
                    if (partsCount == 1 && property.Value.ValueKind == JsonValueKind.Object)
                    {
                        parts = property.Value;
                    }
                    break;
            }
        }

        if (versionCount > 1)
        {
            throw Fail(AppearanceManifestError.InvalidRoot, sourcePath, "Duplicate required root property 'version'.");
        }

        if (partsCount > 1)
        {
            throw Fail(AppearanceManifestError.InvalidRoot, sourcePath, "Duplicate required root property 'parts'.");
        }

        if (version is null)
        {
            throw Fail(AppearanceManifestError.MissingVersion, sourcePath, "Required integer root property 'version' is missing.");
        }

        if (version != RequiredVersion)
        {
            throw Fail(AppearanceManifestError.UnsupportedVersion, sourcePath, $"Root property 'version' must be {RequiredVersion}, got {version.Value}.");
        }

        if (parts is null)
        {
            throw Fail(AppearanceManifestError.MissingParts, sourcePath, "Required object root property 'parts' is missing.");
        }

        Dictionary<AppearancePartKind, Dictionary<int, AppearancePartEntry>> partsMap = new();

        foreach (JsonProperty kindProperty in parts.Value.EnumerateObject())
        {
            if (!TryParseKind(kindProperty.Name, out AppearancePartKind kind))
            {
                throw Fail(AppearanceManifestError.InvalidPartKind, sourcePath, $"Unknown appearance part kind '{kindProperty.Name}'. Expected one of Body, Hair, Eyes, Chest, Helm, Legs, Feet, Hand.");
            }

            if (!partsMap.TryAdd(kind, new Dictionary<int, AppearancePartEntry>()))
            {
                throw Fail(AppearanceManifestError.DuplicatePartKind, sourcePath, $"Duplicate appearance part kind '{kindProperty.Name}'.");
            }

            if (kindProperty.Value.ValueKind != JsonValueKind.Object)
            {
                throw Fail(AppearanceManifestError.InvalidPartKind, sourcePath, $"Appearance part '{kindProperty.Name}' must be a JSON object of part id entries.");
            }

            foreach (JsonProperty idProperty in kindProperty.Value.EnumerateObject())
            {
                if (!TryParsePositiveId(idProperty.Name, out int id))
                {
                    throw Fail(AppearanceManifestError.InvalidPartId, sourcePath, $"Invalid {kind} part id '{idProperty.Name}'.");
                }

                AppearancePartEntry entry = ParseEntry(idProperty.Value, kind, id, sourcePath);
                if (!partsMap[kind].TryAdd(id, entry))
                {
                    throw Fail(AppearanceManifestError.DuplicatePartId, sourcePath, $"Duplicate {kind} part id '{idProperty.Name}'.");
                }
            }
        }

        Dictionary<AppearancePartKind, IReadOnlyDictionary<int, AppearancePartEntry>> published = new();
        foreach ((AppearancePartKind kind, Dictionary<int, AppearancePartEntry> entries) in partsMap)
        {
            published[kind] = entries;
        }

        return new AppearanceManifest(published);
    }

    private static AppearancePartEntry ParseEntry(JsonElement element, AppearancePartKind kind, int id, string sourcePath)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Fail(AppearanceManifestError.InvalidPartEntry, sourcePath, $"{kind} part {id} must be a JSON object of variant references.");
        }

        SpriteReference? noEquip = null;
        SpriteReference? equip = null;
        int noEquipCount = 0;
        int equipCount = 0;

        foreach (JsonProperty property in element.EnumerateObject())
        {
            switch (property.Name)
            {
                case "noEquip":
                    noEquipCount++;
                    if (noEquipCount == 1)
                    {
                        noEquip = ParseReference(property.Value, kind, id, "noEquip", sourcePath);
                    }
                    break;
                case "equip":
                    equipCount++;
                    if (equipCount == 1)
                    {
                        equip = ParseReference(property.Value, kind, id, "equip", sourcePath);
                    }
                    break;
                default:
                    throw Fail(AppearanceManifestError.InvalidVariant, sourcePath, $"Unknown variant property '{property.Name}' for {kind} part {id}. Expected 'noEquip' or 'equip'.");
            }
        }

        if (noEquipCount > 1)
        {
            throw Fail(AppearanceManifestError.DuplicateVariant, sourcePath, $"Duplicate variant property 'noEquip' for {kind} part {id}.");
        }

        if (equipCount > 1)
        {
            throw Fail(AppearanceManifestError.DuplicateVariant, sourcePath, $"Duplicate variant property 'equip' for {kind} part {id}.");
        }

        return new AppearancePartEntry(noEquip, equip);
    }

    private static SpriteReference ParseReference(JsonElement element, AppearancePartKind kind, int id, string variant, string sourcePath)
    {
        string detail = $"{kind} part {id} property '{variant}' must be an array [sheet, frame] of integers.";

        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() != 2)
        {
            throw Fail(AppearanceManifestError.InvalidReference, sourcePath, detail);
        }

        int[] values = new int[2];

        for (int i = 0; i < 2; i++)
        {
            JsonElement member = element[i];
            if (member.ValueKind != JsonValueKind.Number || !member.TryGetInt32(out values[i]))
            {
                throw Fail(AppearanceManifestError.InvalidReference, sourcePath, detail);
            }
        }

        return new SpriteReference(values[0], values[1]);
    }

    private static readonly Dictionary<string, AppearancePartKind> KindNames = new()
    {
        ["Body"] = AppearancePartKind.Body,
        ["Hair"] = AppearancePartKind.Hair,
        ["Eyes"] = AppearancePartKind.Eyes,
        ["Chest"] = AppearancePartKind.Chest,
        ["Helm"] = AppearancePartKind.Helm,
        ["Legs"] = AppearancePartKind.Legs,
        ["Feet"] = AppearancePartKind.Feet,
        ["Hand"] = AppearancePartKind.Hand,
    };

    private static bool TryParseKind(string name, out AppearancePartKind kind)
        => KindNames.TryGetValue(name, out kind);

    private static bool TryParsePositiveId(string name, out int value)
    {
        value = 0;

        if (name.Length is 0 or > 10)
        {
            return false;
        }

        foreach (char c in name)
        {
            if (c is < '0' or > '9')
            {
                return false;
            }
        }

        return int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value > 0;
    }

    private static AppearanceManifestException Fail(AppearanceManifestError error, string sourcePath, string message)
        => new(error, sourcePath, message);
}
