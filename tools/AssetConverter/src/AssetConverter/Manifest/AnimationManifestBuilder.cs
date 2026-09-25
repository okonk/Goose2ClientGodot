using System.Text.Json;
using System.Text.Json.Serialization;
using Goose2.AssetConverter.Adf;
using Goose2.AssetConverter.Aspereta;

namespace Goose2.AssetConverter.Manifest;

public static class AnimationManifestBuilder
{
    private const int ManifestVersion = 1;
    private const int Fps = 8;
    private const string SpellsCategory = "Spells";
    private const string TilesCategory = "Tiles";
    private const string ItemTilesCategory = "ItemTiles";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Build(string illutiaDataDir, string compiledEncPath, string itemTileSheetsPath)
    {
        var itemSheets = LoadItemTileSheets(itemTileSheetsPath);
        var sheetCategories = CollectSheetCategories(new CompiledEnc(compiledEncPath));
        var (sheets, animations, animatedSheets) = CollectSheets(illutiaDataDir);

        foreach (var sheet in sheetCategories.Keys.Where(s => !sheets.ContainsKey(s)).ToList())
            sheetCategories.Remove(sheet);

        return Serialize(sheets, animations, sheetCategories, animatedSheets, itemSheets);
    }

    public static string BuildCombined(
        string illutiaDataDir,
        string illutiaCompiledEncPath,
        string asperetaDataDir,
        string asperetaCompiledEncPath,
        string itemTileSheetsPath)
        => BuildCombined(
            illutiaDataDir, illutiaCompiledEncPath,
            AsperetaAnimationCatalog.Load(asperetaDataDir, asperetaCompiledEncPath),
            itemTileSheetsPath);

    public static string BuildCombined(
        string illutiaDataDir,
        string illutiaCompiledEncPath,
        AsperetaAnimationCatalog aspereta,
        string itemTileSheetsPath)
    {
        var itemSheets = LoadItemTileSheets(itemTileSheetsPath);
        var illutiaCategories = CollectSheetCategories(new CompiledEnc(illutiaCompiledEncPath));
        var (illutiaSheets, illutiaAnimations, illutiaAnimated) = CollectSheets(illutiaDataDir);
        var (asperetaSheets, asperetaAnimations, asperetaCategories) =
            CollectAspereta(aspereta);

        var sheets = new SortedDictionary<int, AnimationManifestSheet>(illutiaSheets);
        foreach (var (number, sheet) in asperetaSheets)
            sheets[number] = sheet;
        foreach (var sheet in illutiaCategories.Keys.Where(s => !sheets.ContainsKey(s)).ToList())
            illutiaCategories.Remove(sheet);

        var categories = new Dictionary<int, List<(string Name, int? Id)>>(illutiaCategories);
        foreach (var (number, list) in asperetaCategories)
            categories[number] = list;

        var animations = new List<AnimationManifestAnimation>(illutiaAnimations);
        animations.AddRange(asperetaAnimations);

        return Serialize(sheets, animations, categories, illutiaAnimated, itemSheets);
    }

    private static HashSet<int> LoadItemTileSheets(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"item tile sheets file not found: {path}", path);
        int[] sheets;
        try
        {
            sheets = JsonSerializer.Deserialize<int[]>(File.ReadAllText(path))
                ?? throw new InvalidOperationException($"malformed item tile sheets file: {path}");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"malformed item tile sheets file: {path}", ex);
        }
        return new HashSet<int>(sheets);
    }

    private static string Serialize(
        SortedDictionary<int, AnimationManifestSheet> sheets,
        List<AnimationManifestAnimation> animations,
        Dictionary<int, List<(string Name, int? Id)>> categories,
        HashSet<int> animatedSheets,
        HashSet<int> itemSheets)
    {
        foreach (var (sheet, entry) in sheets)
        {
            var list = new List<(string Name, int? Id)>();
            if (categories.TryGetValue(sheet, out var existing))
                list.AddRange(existing);
            else
                list.Add((animatedSheets.Contains(sheet) ? SpellsCategory : TilesCategory, null));
            if (itemSheets.Contains(sheet) && !list.Any(c => c.Name == ItemTilesCategory))
                list.Add((ItemTilesCategory, null));
            foreach (var (name, id) in list
                .OrderBy(c => c.Name, StringComparer.Ordinal)
                .ThenBy(c => c.Id))
                entry.Categories.Add(new AnimationManifestCategory { Name = name, Id = id });
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

    private static Dictionary<int, List<(string Name, int? Id)>> CollectSheetCategories(CompiledEnc compiled)
    {
        var categories = new Dictionary<int, List<(string Name, int? Id)>>();
        foreach (var animation in compiled.CompiledAnimations)
            foreach (var sheet in animation.AnimationFiles)
            {
                if (sheet == 0) continue;
                if (!categories.TryGetValue(sheet, out var list))
                    categories[sheet] = list = new List<(string Name, int? Id)>();
                var mapping = (animation.Type.ToString(), (int?)animation.Id);
                if (!list.Contains(mapping))
                    list.Add(mapping);
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

    private static (SortedDictionary<int, AnimationManifestSheet> Sheets,
        List<AnimationManifestAnimation> Animations,
        Dictionary<int, List<(string Name, int? Id)>> Categories)
        CollectAspereta(AsperetaAnimationCatalog catalog)
    {
        var animations = new List<AnimationManifestAnimation>();
        var typeCategories = new Dictionary<int, HashSet<(string Name, int? Id)>>();
        var unclaimedSheets = new HashSet<int>();

        foreach (var (sourceId, resolution) in catalog.Resolved)
        {
            bool claimed = catalog.ClaimedIds.Contains(sourceId);
            var frames = new List<int[]>(resolution.Frames.Count);
            foreach (var frame in resolution.Frames)
                frames.Add(new[] { frame.Sheet, AsperetaSheets.GraphicBase + frame.Frame.Index });
            animations.Add(new AnimationManifestAnimation
            {
                OwnerSheet = frames[0][0],
                Id = claimed ? sourceId : AsperetaSheets.GraphicBase + sourceId,
                Fps = resolution.Fps,
                Frames = frames,
            });
            if (!claimed)
                foreach (var frame in resolution.Frames)
                    unclaimedSheets.Add(frame.Sheet);
        }

        foreach (var slot in catalog.CompiledSlots)
        {
            if (slot.Resolution is null) continue;
            foreach (var frame in slot.Resolution.Frames)
            {
                if (!typeCategories.TryGetValue(frame.Sheet, out var set))
                    typeCategories[frame.Sheet] = set = new HashSet<(string, int?)>();
                set.Add((slot.Type.ToString(), AsperetaSheets.BodyBase + slot.ResourceId));
            }
        }

        var categories = new Dictionary<int, List<(string Name, int? Id)>>();
        var sheetEntries = new SortedDictionary<int, AnimationManifestSheet>();
        foreach (var (_, sheet) in catalog.Sheets)
        {
            int number = sheet.NewSheetNumber;
            sheetEntries[number] = new AnimationManifestSheet();

            var list = new List<(string Name, int? Id)>();
            if (typeCategories.TryGetValue(number, out var set))
                list.AddRange(set);
            if (unclaimedSheets.Contains(number))
                list.Add((SpellsCategory, null));
            if (list.Count == 0)
                list.Add((TilesCategory, null));
            categories[number] = list;
        }

        return (sheetEntries, animations, categories);
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
