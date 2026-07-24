using System.Text.Json;
using Goose2.AssetConverter.Adf;
using Goose2.AssetConverter.Aspereta;

namespace Goose2.AssetConverter.Manifest;

/// <summary>Emits a JSON manifest mapping every graphic sheet's frame index to its pixel rect,
/// so the Godot runtime can build an AtlasTexture for any (sheet, graphic) without re-parsing .adf.
/// Shape: { "tileSize": 32, "sheets": { "&lt;sheet&gt;": { "&lt;graphic&gt;": [x,y,w,h], ... }, ... } }.</summary>
public static class FrameManifestBuilder
{
    public static string Build(string dataDir, int[]? onlyFileNumbers = null)
    {
        var sheets = BuildIllutiaSheets(dataDir, onlyFileNumbers);
        var root = new { tileSize = 32, sheets };
        return JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = false });
    }

    /// <summary>Illutia sheets plus every Aspereta sheet under its renumbered id, with
    /// every Aspereta graphic keyed as 700000 + original id. Aspereta frames are keyed
    /// under the injected id even when a matched Illutia twin exists — matched graphics
    /// are simply never referenced by converted data, so the duplicates are inert.</summary>
    public static string BuildCombined(string illutiaDataDir, string asperetaDataDir)
    {
        var sheets = BuildIllutiaSheets(illutiaDataDir, null);

        foreach (var (_, sheet) in AsperetaSheets.Load(asperetaDataDir))
        {
            var frames = new Dictionary<string, int[]>(sheet.Adf.Frames.Count);
            foreach (var f in sheet.Adf.Frames)
                frames[(AsperetaSheets.GraphicBase + f.Index).ToString()] =
                    new[] { f.X, f.Y, f.W, f.H };
            sheets[sheet.NewSheetNumber.ToString()] = frames;
        }

        var root = new { tileSize = 32, sheets };
        return JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = false });
    }

    private static SortedDictionary<string, Dictionary<string, int[]>> BuildIllutiaSheets(
        string dataDir, int[]? onlyFileNumbers)
    {
        var only = onlyFileNumbers is null ? null : new HashSet<int>(onlyFileNumbers);
        var sheets = new SortedDictionary<string, Dictionary<string, int[]>>();

        foreach (var file in Directory.EnumerateFiles(dataDir, "*.adf"))
        {
            int fileNumber = int.Parse(Path.GetFileNameWithoutExtension(file));
            if (only is not null && !only.Contains(fileNumber)) continue;

            AdfFile adf;
            try { adf = new AdfFile(file); }
            catch { continue; }
            if (adf.Type != AdfType.Graphic) continue;

            var frames = new Dictionary<string, int[]>(adf.Frames.Count);
            foreach (var f in adf.Frames)
                frames[f.Index.ToString()] = new[] { f.X, f.Y, f.W, f.H };

            sheets[fileNumber.ToString()] = frames;
        }

        return sheets;
    }
}
