using Goose2.AssetConverter.Adf;

namespace Goose2.AssetConverter.Aspereta;

public sealed record AsperetaSheet(AdfFile Adf, int NewSheetNumber);

public static class AsperetaSheets
{
    public const int SheetBase = 20000;
    public const int GraphicBase = 700000;
    public const int BodyBase = 10000;
    public const int MapNumberBase = 10000;

    public static IReadOnlyDictionary<int, AsperetaSheet> Load(string asperetaDataDir)
    {
        var graphics = new SortedDictionary<int, AdfFile>();
        foreach (var file in Directory.EnumerateFiles(asperetaDataDir, "*.adf"))
        {
            AdfFile adf;
            try { adf = AsperetaAdf.Load(file); }
            catch { continue; }
            if (adf.Type != AdfType.Graphic || adf.Frames.Count == 0) continue;
            graphics[adf.FileNumber] = adf;
        }

        var result = new Dictionary<int, AsperetaSheet>(graphics.Count);
        int rank = 0;
        foreach (var (fileNumber, adf) in graphics)
            result[fileNumber] = new AsperetaSheet(adf, SheetBase + rank++);
        return result;
    }
}
