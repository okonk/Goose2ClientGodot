using System.Text.Json;
using Goose2.AssetConverter.Adf;

namespace Goose2.AssetConverter.Manifest;

/// <summary>Emits a JSON manifest mapping every graphic sheet's frame index to its pixel rect,
/// so the Godot runtime can build an AtlasTexture for any (sheet, graphic) without re-parsing .adf.
/// Shape: { "tileSize": 32, "sheets": { "&lt;sheet&gt;": { "&lt;graphic&gt;": [x,y,w,h], ... }, ... } }.</summary>
public static class FrameManifestBuilder
{
    public static string Build(string dataDir, int[]? onlyFileNumbers = null)
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

        var root = new { tileSize = 32, sheets };
        return JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = false });
    }
}
