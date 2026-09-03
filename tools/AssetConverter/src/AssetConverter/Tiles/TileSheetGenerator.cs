using System.Linq;
using System.Text.Json;
using Goose2.AssetConverter.Adf;
using Goose2.AssetConverter.Aspereta;

namespace Goose2.AssetConverter.Tiles;

/// <summary>Computes the map-tile sheet set for an asset directory.
/// Rule: static (animation-free) graphic sheets that are neither character-part sheets
/// (compiled.enc) nor item-icon sheets (SpriteBundle iconSheets), plus every Aspereta
/// sheet from the manifest (Aspereta part/tile separation needs the Aspereta ADF data).</summary>
public static class TileSheetGenerator
{
    public const string OutputFileName = "tile-sheets.json";

    public static IReadOnlyList<int> Generate(string dataDir, string manifestPath, string iconSheetsPath)
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
