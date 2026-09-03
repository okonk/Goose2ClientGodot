using System.Buffers.Binary;
using System.Linq;
using System.Text.Json;
using Goose2.AssetConverter.Adf;
using Goose2.AssetConverter.Aspereta;

namespace Goose2.AssetConverter.Tiles;

/// <summary>Computes the map-tile sheet set for an asset directory.
/// Rule: static (animation-free) graphic sheets that are neither character-part sheets
/// (compiled.enc) nor item-icon sheets (SpriteBundle iconSheets), plus every sheet the
/// converted maps reference (sheets are dual-use: items may pull from tile sheets), plus
/// every Aspereta sheet from the manifest (Aspereta part/tile separation needs the
/// Aspereta ADF data).</summary>
public static class TileSheetGenerator
{
    public const string OutputFileName = "tile-sheets.json";

    public static IReadOnlyList<int> Generate(string dataDir, string manifestPath, string iconSheetsPath, string mapsDir)
    {
        HashSet<int> iconSheets = LoadIconSheets(iconSheetsPath);
        var enc = new CompiledEnc(Path.Combine(dataDir, "compiled.enc"));

        var tiles = new SortedSet<int>();

        foreach (string file in Directory.EnumerateFiles(dataDir, "*.adf"))
        {
            AdfFile adf;
            try
            {
                adf = new AdfFile(file);
            }
            catch
            {
                continue;
            }

            if (adf.Type != AdfType.Graphic || adf.AnimationCount > 0)
            {
                continue;
            }

            int sheet = adf.FileNumber;
            if (enc.SheetToAnimation.ContainsKey(sheet) || iconSheets.Contains(sheet))
            {
                continue;
            }

            tiles.Add(sheet);
        }

        AddMapReferencedSheets(mapsDir, tiles);

        foreach (int sheet in LoadManifestSheets(manifestPath))
        {
            if (sheet >= AsperetaSheets.SheetBase)
            {
                tiles.Add(sheet);
            }
        }

        return tiles.ToList();
    }

    public static void Write(string assetDir, IReadOnlyList<int> sheets)
    {
        string path = Path.Combine(assetDir, OutputFileName);
        File.WriteAllText(path, JsonSerializer.Serialize(sheets));
    }

    private static void AddMapReferencedSheets(string mapsDir, SortedSet<int> tiles)
    {
        if (!Directory.Exists(mapsDir))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(mapsDir, "*.bytes"))
        {
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(file);
            }
            catch (IOException)
            {
                continue;
            }

            // Layout matches MapCodec: 12-byte header (Int16 version, Int16 editor version,
            // Int32 width, Int32 height), then per tile Int32 flags + five (Int32 graphic, Int16 sheet).
            // Converted Illutia maps carry a fixed trailer after the tile data; parse the tile
            // region and ignore anything after it.
            if (bytes.Length < 12)
            {
                continue;
            }

            int width = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4));
            int height = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8));
            if (width <= 0 || height <= 0 || bytes.Length < 12 + width * height * 34)
            {
                continue;
            }

            int offset = 12;
            for (int i = 0; i < width * height; i++)
            {
                offset += 4;
                for (int layer = 0; layer < 5; layer++)
                {
                    offset += 4;
                    int sheet = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(offset));
                    offset += 2;
                    if (sheet != 0)
                    {
                        tiles.Add(sheet);
                    }
                }
            }
        }
    }

    private static HashSet<int> LoadIconSheets(string path)
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        if (!doc.RootElement.TryGetProperty("iconSheets", out var element) || element.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"{path} has no 'iconSheets' array.");
        }

        var result = new HashSet<int>();
        foreach (JsonElement item in element.EnumerateArray())
        {
            if (!item.TryGetInt32(out int sheet))
            {
                throw new InvalidDataException($"{path} 'iconSheets' contains a non-integer entry.");
            }

            result.Add(sheet);
        }

        return result;
    }

    private static IEnumerable<int> LoadManifestSheets(string path)
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        if (!doc.RootElement.TryGetProperty("sheets", out var element) || element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"{path} has no 'sheets' object.");
        }

        foreach (JsonProperty prop in element.EnumerateObject())
        {
            if (int.TryParse(prop.Name, out int sheet))
            {
                yield return sheet;
            }
        }
    }
}
