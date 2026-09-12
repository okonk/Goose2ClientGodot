using System.Text.Json;
using System.Text.Json.Serialization;
using Goose2.AssetConverter.Adf;

namespace Goose2.AssetConverter.Manifest;

public static class AnimationManifestBuilder
{
    private const int ManifestVersion = 1;
    private const int Fps = 8;
    private const string SpellsCategory = "Spells";
    private const string TilesCategory = "Tiles";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Build(string illutiaDataDir, string compiledEncPath)
    {
        var sheetCategories = CollectSheetCategories(new CompiledEnc(compiledEncPath));
        var (sheets, animations, animatedSheets) = CollectSheets(illutiaDataDir);

        foreach (var sheet in sheetCategories.Keys)
            if (!sheets.ContainsKey(sheet))
                sheets[sheet] = new AnimationManifestSheet();

        foreach (var (sheet, entry) in sheets)
        {
            if (sheetCategories.TryGetValue(sheet, out var categories))
            {
                foreach (var (name, id) in categories
                    .OrderBy(c => c.Name, StringComparer.Ordinal)
                    .ThenBy(c => c.Id))
                    entry.Categories.Add(new AnimationManifestCategory { Name = name, Id = id });
            }
            else
            {
                entry.Categories.Add(new AnimationManifestCategory
                {
                    Name = animatedSheets.Contains(sheet) ? SpellsCategory : TilesCategory,
                });
            }
        }

        animations.Sort(static (a, b) =>
            a.OwnerSheet != b.OwnerSheet
                ? a.OwnerSheet.CompareTo(b.OwnerSheet)
                : a.Id.CompareTo(b.Id));

        return JsonSerializer.Serialize(new AnimationManifest
        {
            Version = ManifestVersion,
            Sheets = sheets,
            Animations = animations,
        }, SerializerOptions);
    }

    private static Dictionary<int, List<(string Name, int Id)>> CollectSheetCategories(CompiledEnc compiled)
    {
        var categories = new Dictionary<int, List<(string Name, int Id)>>();
        foreach (var animation in compiled.CompiledAnimations)
            foreach (var sheet in animation.AnimationFiles)
            {
                if (sheet == 0) continue;
                if (!categories.TryGetValue(sheet, out var list))
                    categories[sheet] = list = new List<(string Name, int Id)>();
                list.Add((animation.Type.ToString(), animation.Id));
            }
        return categories;
    }

    private static (SortedDictionary<int, AnimationManifestSheet> Sheets,
        List<AnimationManifestAnimation> Animations,
        HashSet<int> AnimatedSheets) CollectSheets(string dataDir)
    {
        var sheets = new SortedDictionary<int, AnimationManifestSheet>();
        var animations = new List<AnimationManifestAnimation>();
        var animatedSheets = new HashSet<int>();

        foreach (var path in NumericAdfPaths(dataDir))
        {
            AdfFile adf;
            try { adf = new AdfFile(path); }
            catch { continue; }
            if (adf.Type != AdfType.Graphic) continue;

            if (!sheets.ContainsKey(adf.FileNumber))
                sheets[adf.FileNumber] = new AnimationManifestSheet();

            if (adf.Animations is null) continue;
            animatedSheets.Add(adf.FileNumber);

            foreach (var (id, animation) in adf.Animations)
            {
                var frames = new List<int[]>(animation.Frames.Count);
                foreach (var frame in animation.Frames)
                    frames.Add(new[] { adf.FileNumber, frame.Index });
                animations.Add(new AnimationManifestAnimation
                {
                    OwnerSheet = adf.FileNumber,
                    Id = id,
                    Fps = Fps,
                    Frames = frames,
                });
            }
        }

        return (sheets, animations, animatedSheets);
    }

    private static List<string> NumericAdfPaths(string dataDir)
    {
        var paths = new List<(int Number, string Path)>();
        foreach (var path in Directory.EnumerateFiles(dataDir, "*.adf"))
            if (int.TryParse(Path.GetFileNameWithoutExtension(path), out var number))
                paths.Add((number, path));
        paths.Sort(static (a, b) => a.Number.CompareTo(b.Number));
        return paths.Select(p => p.Path).ToList();
    }
}
