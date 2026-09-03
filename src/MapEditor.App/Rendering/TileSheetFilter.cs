using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MapEditor.App.Rendering;

internal static class TileSheetFilter
{
    public const string FileName = "tile-sheets.json";

    public static IReadOnlySet<int>? Load(string assetDirectory)
    {
        string path = Path.Combine(assetDirectory, FileName);
        if (!File.Exists(path))
        {
            return null;
        }

        // A corrupt optional filter must not block asset loading; unfiltered is the fallback.
        try
        {
            int[]? sheets = JsonSerializer.Deserialize<int[]>(File.ReadAllText(path));
            return sheets is null ? null : new HashSet<int>(sheets);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static IReadOnlyList<int> Apply(IReadOnlyList<int> sheetIds, IReadOnlySet<int>? filter)
    {
        if (filter is null)
        {
            return sheetIds;
        }

        return sheetIds.Where(filter.Contains).ToList();
    }
}
