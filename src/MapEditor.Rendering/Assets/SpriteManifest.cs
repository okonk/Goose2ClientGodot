using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MapEditor.Rendering;

public readonly record struct SpriteFrame(SpriteReference Reference, SpriteSourceRect SourceRect);

public sealed class SpriteManifest
{
    public const int RequiredTileSize = 32;

    public int TileSize { get; }
    public int MaxFrameWidth { get; }
    public int MaxFrameHeight { get; }
    public IReadOnlyList<int> SheetIds { get; }
    public IReadOnlyList<SpriteFrame> Frames { get; }

    private readonly IReadOnlyDictionary<int, IReadOnlyList<SpriteFrame>> _framesBySheet;

    private SpriteManifest(
        int tileSize,
        int maxFrameWidth,
        int maxFrameHeight,
        IReadOnlyList<int> sheetIds,
        IReadOnlyList<SpriteFrame> frames,
        IReadOnlyDictionary<int, IReadOnlyList<SpriteFrame>> framesBySheet)
    {
        TileSize = tileSize;
        MaxFrameWidth = maxFrameWidth;
        MaxFrameHeight = maxFrameHeight;
        SheetIds = sheetIds;
        Frames = frames;
        _framesBySheet = framesBySheet;
    }

    public bool ContainsSheet(int sheet)
        => _framesBySheet.ContainsKey(sheet);

    public bool TryGetSourceRect(SpriteReference reference, out SpriteSourceRect sourceRect)
    {
        if (_framesBySheet.TryGetValue(reference.Sheet, out IReadOnlyList<SpriteFrame>? frames))
        {
            foreach (SpriteFrame frame in frames)
            {
                if (frame.Reference.Graphic == reference.Graphic)
                {
                    sourceRect = frame.SourceRect;
                    return true;
                }
            }
        }

        sourceRect = default;
        return false;
    }

    public IReadOnlyList<SpriteFrame> GetFrames(int sheet)
        => _framesBySheet.TryGetValue(sheet, out IReadOnlyList<SpriteFrame>? frames) ? frames : Array.Empty<SpriteFrame>();

    public static SpriteManifest Parse(string json, string sourcePath = "<memory>")
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new SpriteManifestException(
                SpriteManifestError.MalformedJson,
                sourcePath,
                $"Manifest is not valid JSON: {sourcePath}: {ex.Message}",
                ex);
        }

        using (document)
        {
            return ParseDocument(document.RootElement, sourcePath);
        }
    }

    public static SpriteManifest Load(string assetDirectory)
    {
        if (string.IsNullOrWhiteSpace(assetDirectory))
        {
            throw new ArgumentException("Asset directory is required.", nameof(assetDirectory));
        }

        string manifestPath = Path.Combine(Path.GetFullPath(assetDirectory), "manifest.json");

        if (Directory.Exists(manifestPath))
        {
            throw new SpriteManifestException(
                SpriteManifestError.ReadFailed,
                manifestPath,
                $"Manifest path is a directory: {manifestPath}",
                new IOException($"Manifest path is a directory: {manifestPath}"));
        }

        if (!File.Exists(manifestPath))
        {
            throw new SpriteManifestException(
                SpriteManifestError.ManifestNotFound,
                manifestPath,
                $"Manifest not found: {manifestPath}");
        }

        string json;
        try
        {
            json = File.ReadAllText(manifestPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new SpriteManifestException(
                SpriteManifestError.ReadFailed,
                manifestPath,
                $"Failed to read manifest: {manifestPath}",
                ex);
        }

        return Parse(json, manifestPath);
    }

    private static SpriteManifest ParseDocument(JsonElement root, string sourcePath)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw Fail(SpriteManifestError.InvalidRoot, sourcePath, "Manifest root must be a JSON object.");
        }

        int? tileSize = null;
        int tileSizeCount = 0;
        int sheetsCount = 0;
        JsonElement? sheets = null;

        foreach (JsonProperty property in root.EnumerateObject())
        {
            switch (property.Name)
            {
                case "tileSize":
                    tileSizeCount++;
                    if (tileSizeCount == 1 && property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out int value))
                    {
                        tileSize = value;
                    }
                    break;
                case "sheets":
                    sheetsCount++;
                    if (sheetsCount == 1 && property.Value.ValueKind == JsonValueKind.Object)
                    {
                        sheets = property.Value;
                    }
                    break;
            }
        }

        if (tileSizeCount > 1)
        {
            throw Fail(SpriteManifestError.InvalidRoot, sourcePath, "Duplicate required root property 'tileSize'.");
        }

        if (sheetsCount > 1)
        {
            throw Fail(SpriteManifestError.InvalidRoot, sourcePath, "Duplicate required root property 'sheets'.");
        }

        if (tileSize is null)
        {
            throw Fail(SpriteManifestError.MissingTileSize, sourcePath, "Required integer root property 'tileSize' is missing.");
        }

        if (tileSize != RequiredTileSize)
        {
            throw Fail(SpriteManifestError.UnsupportedTileSize, sourcePath, $"Root property 'tileSize' must be {RequiredTileSize}, got {tileSize.Value}.");
        }

        if (sheets is null)
        {
            throw Fail(SpriteManifestError.MissingSheets, sourcePath, "Required object root property 'sheets' is missing.");
        }

        Dictionary<int, List<SpriteFrame>> framesBySheet = new();

        foreach (JsonProperty sheetProperty in sheets.Value.EnumerateObject())
        {
            if (!TryParseId(sheetProperty.Name, out int sheetId))
            {
                throw Fail(SpriteManifestError.InvalidSheetId, sourcePath, $"Invalid sheet id '{sheetProperty.Name}'.");
            }

            if (!framesBySheet.TryAdd(sheetId, new List<SpriteFrame>()))
            {
                throw Fail(SpriteManifestError.DuplicateSheetId, sourcePath, $"Duplicate sheet id '{sheetProperty.Name}'.");
            }

            if (sheetProperty.Value.ValueKind != JsonValueKind.Object)
            {
                throw Fail(SpriteManifestError.InvalidSheetFrames, sourcePath, $"Sheet {sheetId} frames must be a JSON object.");
            }

            HashSet<int> seenGraphics = new();

            foreach (JsonProperty frameProperty in sheetProperty.Value.EnumerateObject())
            {
                if (!TryParseId(frameProperty.Name, out int graphicId))
                {
                    throw Fail(SpriteManifestError.InvalidGraphicId, sourcePath, $"Invalid graphic id '{frameProperty.Name}' in sheet {sheetId}.");
                }

                if (!seenGraphics.Add(graphicId))
                {
                    throw Fail(SpriteManifestError.DuplicateGraphicId, sourcePath, $"Duplicate graphic id '{frameProperty.Name}' in sheet {sheetId}.");
                }

                SpriteSourceRect rect = ParseRect(frameProperty.Value, sheetId, graphicId, sourcePath);
                framesBySheet[sheetId].Add(new SpriteFrame(new SpriteReference(sheetId, graphicId), rect));
            }
        }

        int maxWidth = RequiredTileSize;
        int maxHeight = RequiredTileSize;
        List<SpriteFrame> allFrames = new();

        foreach ((int sheetId, List<SpriteFrame> frames) in framesBySheet)
        {
            frames.Sort((a, b) => a.Reference.Graphic.CompareTo(b.Reference.Graphic));

            foreach (SpriteFrame frame in frames)
            {
                maxWidth = Math.Max(maxWidth, frame.SourceRect.Width);
                maxHeight = Math.Max(maxHeight, frame.SourceRect.Height);
                allFrames.Add(frame);
            }
        }

        allFrames.Sort((a, b) =>
        {
            int bySheet = a.Reference.Sheet.CompareTo(b.Reference.Sheet);
            return bySheet != 0 ? bySheet : a.Reference.Graphic.CompareTo(b.Reference.Graphic);
        });

        Dictionary<int, IReadOnlyList<SpriteFrame>> published = new();
        foreach ((int sheetId, List<SpriteFrame> frames) in framesBySheet)
        {
            published[sheetId] = frames.AsReadOnly();
        }

        return new SpriteManifest(
            RequiredTileSize,
            maxWidth,
            maxHeight,
            framesBySheet.Keys.OrderBy(id => id).ToList().AsReadOnly(),
            allFrames.AsReadOnly(),
            published);
    }

    private static SpriteSourceRect ParseRect(JsonElement element, int sheet, int graphic, string sourcePath)
    {
        string detail = $"Frame for sheet {sheet} graphic {graphic} must be an array [x, y, width, height] of integers.";

        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() != 4)
        {
            throw Fail(SpriteManifestError.InvalidFrameRect, sourcePath, detail);
        }

        int[] values = new int[4];

        for (int i = 0; i < 4; i++)
        {
            JsonElement member = element[i];
            if (member.ValueKind != JsonValueKind.Number || !member.TryGetInt32(out values[i]))
            {
                throw Fail(SpriteManifestError.InvalidFrameRect, sourcePath, detail);
            }
        }

        int x = values[0];
        int y = values[1];
        int width = values[2];
        int height = values[3];

        if (x < 0 || y < 0 || width <= 0 || height <= 0)
        {
            throw Fail(SpriteManifestError.InvalidFrameRect, sourcePath, detail);
        }

        try
        {
            _ = checked(x + width);
            _ = checked(y + height);
        }
        catch (OverflowException)
        {
            throw Fail(SpriteManifestError.InvalidFrameRect, sourcePath, detail);
        }

        return new SpriteSourceRect(x, y, width, height);
    }

    private static bool TryParseId(string name, out int value)
    {
        value = 0;

        // Map ids are signed Int32 (0 reserved for empty); converter manifests emit negative ids.
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

        if (!int.TryParse(name, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value))
        {
            return false;
        }

        return value != 0;
    }

    private static SpriteManifestException Fail(SpriteManifestError error, string sourcePath, string message)
        => new(error, sourcePath, message);
}
