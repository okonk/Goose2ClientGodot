using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MapEditor.Rendering;

public readonly record struct GraphicAnimationKey(int OwnerSheet, int AnimationId);

public sealed record GraphicAnimation(GraphicAnimationKey Key, int FramesPerSecond, IReadOnlyList<SpriteReference> Frames);

public sealed class GraphicAnimationManifest
{
    public const string FileName = "animation-manifest.json";
    public const int SupportedVersion = 1;

    public int Version { get; }
    public IReadOnlyList<int> SheetIds { get; }
    public IReadOnlyList<GraphicAnimation> Animations { get; }

    private readonly IReadOnlyDictionary<int, IReadOnlyList<GraphicCategoryMapping>> _categoriesBySheet;
    private readonly IReadOnlyDictionary<GraphicAnimationKey, GraphicAnimation> _animationsByKey;

    private GraphicAnimationManifest(
        int version,
        IReadOnlyList<int> sheetIds,
        IReadOnlyList<GraphicAnimation> animations,
        IReadOnlyDictionary<int, IReadOnlyList<GraphicCategoryMapping>> categoriesBySheet,
        IReadOnlyDictionary<GraphicAnimationKey, GraphicAnimation> animationsByKey)
    {
        Version = version;
        SheetIds = sheetIds;
        Animations = animations;
        _categoriesBySheet = categoriesBySheet;
        _animationsByKey = animationsByKey;
    }

    public bool ContainsSheet(int sheet)
        => _categoriesBySheet.ContainsKey(sheet);

    public IReadOnlyList<GraphicCategoryMapping> GetCategories(int sheet)
        => _categoriesBySheet.TryGetValue(sheet, out IReadOnlyList<GraphicCategoryMapping>? categories)
            ? categories
            : Array.Empty<GraphicCategoryMapping>();

    public bool TryGetAnimation(GraphicAnimationKey key, out GraphicAnimation animation)
        => _animationsByKey.TryGetValue(key, out animation!);

    public static GraphicAnimationManifest Parse(string json, string sourcePath = "<memory>")
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new GraphicAnimationManifestException(
                GraphicAnimationManifestError.MalformedJson,
                sourcePath,
                $"Animation manifest is not valid JSON: {sourcePath}: {ex.Message}",
                ex);
        }

        using (document)
        {
            return ParseDocument(document.RootElement, sourcePath);
        }
    }

    public static GraphicAnimationManifest Load(string assetDirectory)
    {
        if (string.IsNullOrWhiteSpace(assetDirectory))
        {
            throw new ArgumentException("Asset directory is required.", nameof(assetDirectory));
        }

        string manifestPath = Path.Combine(Path.GetFullPath(assetDirectory), FileName);

        if (Directory.Exists(manifestPath))
        {
            throw new GraphicAnimationManifestException(
                GraphicAnimationManifestError.ReadFailed,
                manifestPath,
                $"Animation manifest path is a directory: {manifestPath}",
                new IOException($"Animation manifest path is a directory: {manifestPath}"));
        }

        if (!File.Exists(manifestPath))
        {
            throw new GraphicAnimationManifestException(
                GraphicAnimationManifestError.ManifestNotFound,
                manifestPath,
                $"Animation manifest not found: {manifestPath}");
        }

        string json;
        try
        {
            json = File.ReadAllText(manifestPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new GraphicAnimationManifestException(
                GraphicAnimationManifestError.ReadFailed,
                manifestPath,
                $"Failed to read animation manifest: {manifestPath}",
                ex);
        }

        return Parse(json, manifestPath);
    }

    private static GraphicAnimationManifest ParseDocument(JsonElement root, string sourcePath)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw Fail(GraphicAnimationManifestError.InvalidRoot, sourcePath, "Animation manifest root must be a JSON object.");
        }

        int versionCount = 0;
        int? version = null;
        int sheetsCount = 0;
        JsonElement? sheets = null;
        int animationsCount = 0;
        JsonElement? animations = null;

        foreach (JsonProperty property in root.EnumerateObject())
        {
            switch (property.Name)
            {
                case "version":
                    versionCount++;
                    if (versionCount == 1 && property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out int versionValue))
                    {
                        version = versionValue;
                    }
                    break;
                case "sheets":
                    sheetsCount++;
                    if (sheetsCount == 1 && property.Value.ValueKind == JsonValueKind.Object)
                    {
                        sheets = property.Value;
                    }
                    break;
                case "animations":
                    animationsCount++;
                    if (animationsCount == 1 && property.Value.ValueKind == JsonValueKind.Array)
                    {
                        animations = property.Value;
                    }
                    break;
            }
        }

        if (versionCount > 1)
        {
            throw Fail(GraphicAnimationManifestError.InvalidRoot, sourcePath, "Duplicate required root property 'version'.");
        }

        if (sheetsCount > 1)
        {
            throw Fail(GraphicAnimationManifestError.InvalidRoot, sourcePath, "Duplicate required root property 'sheets'.");
        }

        if (animationsCount > 1)
        {
            throw Fail(GraphicAnimationManifestError.InvalidRoot, sourcePath, "Duplicate required root property 'animations'.");
        }

        if (version is null || version != SupportedVersion)
        {
            throw Fail(GraphicAnimationManifestError.UnsupportedVersion, sourcePath, $"Root property 'version' must be {SupportedVersion}.");
        }

        if (sheets is null)
        {
            throw Fail(GraphicAnimationManifestError.MissingSheets, sourcePath, "Required object root property 'sheets' is missing.");
        }

        if (animations is null)
        {
            throw Fail(GraphicAnimationManifestError.MissingAnimations, sourcePath, "Required array root property 'animations' is missing.");
        }

        Dictionary<int, List<GraphicCategoryMapping>> categoriesBySheet = new();

        foreach (JsonProperty sheetProperty in sheets.Value.EnumerateObject())
        {
            if (!TryParseId(sheetProperty.Name, out int sheetId))
            {
                throw Fail(GraphicAnimationManifestError.InvalidSheetId, sourcePath, $"Invalid sheet id '{sheetProperty.Name}'.");
            }

            if (!categoriesBySheet.TryAdd(sheetId, new List<GraphicCategoryMapping>()))
            {
                throw Fail(GraphicAnimationManifestError.DuplicateSheetId, sourcePath, $"Duplicate sheet id '{sheetProperty.Name}'.");
            }

            if (sheetProperty.Value.ValueKind != JsonValueKind.Object)
            {
                throw Fail(GraphicAnimationManifestError.InvalidSheetEntry, sourcePath, $"Sheet {sheetId} entry must be a JSON object.");
            }

            ParseSheetCategories(sheetProperty.Value, sheetId, categoriesBySheet[sheetId], sourcePath);
        }

        Dictionary<GraphicAnimationKey, GraphicAnimation> animationsByKey = new();

        foreach (JsonElement animationElement in animations.Value.EnumerateArray())
        {
            ParseAnimation(animationElement, animationsByKey, sourcePath);
        }

        Dictionary<int, IReadOnlyList<GraphicCategoryMapping>> publishedCategories = new();
        foreach ((int sheetId, List<GraphicCategoryMapping> categories) in categoriesBySheet)
        {
            categories.Sort((a, b) =>
            {
                int byCategory = a.Category.CompareTo(b.Category);
                return byCategory != 0 ? byCategory : (a.Id ?? int.MinValue).CompareTo(b.Id ?? int.MinValue);
            });

            publishedCategories[sheetId] = categories.AsReadOnly();
        }

        List<GraphicAnimation> sortedAnimations = new(animationsByKey.Values);
        sortedAnimations.Sort((a, b) =>
        {
            int byOwner = a.Key.OwnerSheet.CompareTo(b.Key.OwnerSheet);
            return byOwner != 0 ? byOwner : a.Key.AnimationId.CompareTo(b.Key.AnimationId);
        });

        return new GraphicAnimationManifest(
            SupportedVersion,
            categoriesBySheet.Keys.OrderBy(id => id).ToList().AsReadOnly(),
            sortedAnimations.AsReadOnly(),
            publishedCategories,
            animationsByKey);
    }

    private static void ParseSheetCategories(JsonElement sheet, int sheetId, List<GraphicCategoryMapping> categories, string sourcePath)
    {
        int categoriesCount = 0;
        JsonElement? categoriesElement = null;

        foreach (JsonProperty property in sheet.EnumerateObject())
        {
            if (property.Name != "categories")
            {
                continue;
            }

            categoriesCount++;
            if (categoriesCount == 1 && property.Value.ValueKind == JsonValueKind.Array)
            {
                categoriesElement = property.Value;
            }
        }

        if (categoriesCount > 1)
        {
            throw Fail(GraphicAnimationManifestError.InvalidCategories, sourcePath, $"Duplicate 'categories' property in sheet {sheetId}.");
        }

        if (categoriesElement is null)
        {
            throw Fail(GraphicAnimationManifestError.MissingCategories, sourcePath, $"Required array property 'categories' is missing in sheet {sheetId}.");
        }

        HashSet<(GraphicCategory, int?)> seenMappings = new();

        foreach (JsonElement categoryElement in categoriesElement.Value.EnumerateArray())
        {
            if (categoryElement.ValueKind != JsonValueKind.Object)
            {
                throw Fail(GraphicAnimationManifestError.InvalidCategoryMapping, sourcePath, $"Category in sheet {sheetId} must be a JSON object.");
            }

            int nameCount = 0;
            string? name = null;
            int idCount = 0;
            int? id = null;

            foreach (JsonProperty property in categoryElement.EnumerateObject())
            {
                switch (property.Name)
                {
                    case "name":
                        nameCount++;
                        if (nameCount == 1 && property.Value.ValueKind == JsonValueKind.String)
                        {
                            name = property.Value.GetString();
                        }
                        break;
                    case "id":
                        idCount++;
                        if (idCount == 1 && property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out int idValue))
                        {
                            id = idValue;
                        }
                        break;
                }
            }

            if (nameCount > 1)
            {
                throw Fail(GraphicAnimationManifestError.InvalidCategoryName, sourcePath, $"Duplicate 'name' property in category of sheet {sheetId}.");
            }

            if (idCount > 1)
            {
                throw Fail(GraphicAnimationManifestError.InvalidCategoryMapping, sourcePath, $"Duplicate 'id' property in category of sheet {sheetId}.");
            }

            if (name is null)
            {
                throw Fail(GraphicAnimationManifestError.InvalidCategoryName, sourcePath, $"Category in sheet {sheetId} must have a string 'name' property.");
            }

            if (!TryParseCategory(name, out GraphicCategory category))
            {
                throw Fail(GraphicAnimationManifestError.InvalidCategoryName, sourcePath, $"Unknown category name '{name}' in sheet {sheetId}.");
            }

            bool equipment = category is not (GraphicCategory.Tiles or GraphicCategory.Spells);
            if (equipment && id is null)
            {
                throw Fail(GraphicAnimationManifestError.InvalidCategoryMapping, sourcePath, $"Category '{name}' in sheet {sheetId} requires an integer 'id' property.");
            }

            if (!equipment && idCount > 0)
            {
                throw Fail(GraphicAnimationManifestError.InvalidCategoryMapping, sourcePath, $"Category '{name}' in sheet {sheetId} must not have an 'id' property.");
            }

            if (!seenMappings.Add((category, id)))
            {
                throw Fail(GraphicAnimationManifestError.DuplicateCategoryMapping, sourcePath, $"Duplicate category mapping '{name}' in sheet {sheetId}.");
            }

            categories.Add(new GraphicCategoryMapping(category, id));
        }
    }

    private static void ParseAnimation(
        JsonElement animationElement,
        Dictionary<GraphicAnimationKey, GraphicAnimation> animationsByKey,
        string sourcePath)
    {
        if (animationElement.ValueKind != JsonValueKind.Object)
        {
            throw Fail(GraphicAnimationManifestError.InvalidAnimation, sourcePath, "Animation must be a JSON object.");
        }

        int ownerSheetCount = 0;
        int? ownerSheet = null;
        int idCount = 0;
        int? animationId = null;
        int fpsCount = 0;
        int? fps = null;
        int framesCount = 0;
        JsonElement? frames = null;

        foreach (JsonProperty property in animationElement.EnumerateObject())
        {
            switch (property.Name)
            {
                case "ownerSheet":
                    ownerSheetCount++;
                    if (ownerSheetCount == 1 && property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out int owner))
                    {
                        ownerSheet = owner;
                    }
                    break;
                case "id":
                    idCount++;
                    if (idCount == 1 && property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out int id))
                    {
                        animationId = id;
                    }
                    break;
                case "fps":
                    fpsCount++;
                    if (fpsCount == 1 && property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out int fpsValue) && fpsValue > 0)
                    {
                        fps = fpsValue;
                    }
                    break;
                case "frames":
                    framesCount++;
                    if (framesCount == 1 && property.Value.ValueKind == JsonValueKind.Array)
                    {
                        frames = property.Value;
                    }
                    break;
            }
        }

        if (ownerSheetCount > 1 || idCount > 1 || fpsCount > 1 || framesCount > 1)
        {
            throw Fail(GraphicAnimationManifestError.InvalidAnimation, sourcePath, "Animation has a duplicate property.");
        }

        if (ownerSheet is null || animationId is null || fps is null)
        {
            throw Fail(GraphicAnimationManifestError.InvalidAnimation, sourcePath, "Animation must have integer 'ownerSheet' and 'id' properties and a positive integer 'fps' property.");
        }

        if (frames is null)
        {
            throw Fail(GraphicAnimationManifestError.InvalidAnimation, sourcePath, "Animation must have an array 'frames' property.");
        }

        List<SpriteReference> parsedFrames = new();

        foreach (JsonElement frameElement in frames.Value.EnumerateArray())
        {
            if (frameElement.ValueKind != JsonValueKind.Array
                || frameElement.GetArrayLength() != 2
                || frameElement[0].ValueKind != JsonValueKind.Number
                || !frameElement[0].TryGetInt32(out int frameSheet)
                || frameElement[1].ValueKind != JsonValueKind.Number
                || !frameElement[1].TryGetInt32(out int frameGraphic))
            {
                throw Fail(GraphicAnimationManifestError.InvalidFrame, sourcePath, $"Animation ({ownerSheet}, {animationId}) frames must be [sheet, graphic] integer pairs.");
            }

            // SpriteAssetCache.Resolve reserves graphic 0 as empty and cannot preview it.
            if (frameGraphic == 0)
            {
                throw Fail(GraphicAnimationManifestError.EmptyFrameGraphic, sourcePath, $"Animation ({ownerSheet}, {animationId}) frames cannot reference graphic 0.");
            }

            parsedFrames.Add(new SpriteReference(frameSheet, frameGraphic));
        }

        if (parsedFrames.Count == 0)
        {
            throw Fail(GraphicAnimationManifestError.EmptyAnimationFrames, sourcePath, $"Animation ({ownerSheet}, {animationId}) must have at least one frame.");
        }

        GraphicAnimationKey key = new(ownerSheet.Value, animationId.Value);
        if (!animationsByKey.TryAdd(key, new GraphicAnimation(key, fps.Value, parsedFrames.AsReadOnly())))
        {
            throw Fail(GraphicAnimationManifestError.DuplicateAnimation, sourcePath, $"Duplicate animation ({ownerSheet}, {animationId}).");
        }
    }

    private static readonly Dictionary<string, GraphicCategory> CategoryNames = new(StringComparer.Ordinal)
    {
        ["Body"] = GraphicCategory.Body,
        ["Hair"] = GraphicCategory.Hair,
        ["Eyes"] = GraphicCategory.Eyes,
        ["Chest"] = GraphicCategory.Chest,
        ["Helm"] = GraphicCategory.Helm,
        ["Legs"] = GraphicCategory.Legs,
        ["Feet"] = GraphicCategory.Feet,
        ["Hand"] = GraphicCategory.Hand,
        ["Tiles"] = GraphicCategory.Tiles,
        ["Spells"] = GraphicCategory.Spells
    };

    private static bool TryParseCategory(string name, out GraphicCategory category)
        => CategoryNames.TryGetValue(name, out category);

    private static bool TryParseId(string name, out int value)
    {
        value = 0;

        int start = name.Length > 0 && name[0] == '-' ? 1 : 0;

        if (start == name.Length || name.Length - start > 10)
        {
            return false;
        }

        for (int i = start; i < name.Length; i++)
        {
            if (name[i] is < '0' or > '9')
            {
                return false;
            }
        }

        return int.TryParse(name, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    }

    private static GraphicAnimationManifestException Fail(GraphicAnimationManifestError error, string sourcePath, string message)
        => new(error, sourcePath, message);
}
