using System.Text.Json;
using System.Text.Json.Serialization;
using Goose2.AssetConverter.Adf;
using Goose2.AssetConverter.Aspereta;

namespace Goose2.AssetConverter.Manifest;

public static class AnimationManifestBuilder
{
    private const int ManifestVersion = 1;
    private const int Fps = 8;
    private const string BodyCategory = "Body";
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
    {
        var itemSheets = LoadItemTileSheets(itemTileSheetsPath);
        var illutiaCategories = CollectSheetCategories(new CompiledEnc(illutiaCompiledEncPath));
        var (illutiaSheets, illutiaAnimations, illutiaAnimated) = CollectSheets(illutiaDataDir);
        var (asperetaSheets, asperetaAnimations, asperetaCategories) =
            LoadAspereta(asperetaDataDir, asperetaCompiledEncPath);

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
        LoadAspereta(string asperetaDataDir, string asperetaCompiledEncPath)
    {
        var aspereta = AsperetaSheets.Load(asperetaDataDir);

        var frameSheet = new Dictionary<int, int>();
        foreach (var (_, sheet) in aspereta)
            foreach (var frame in sheet.Adf.Frames)
            {
                if (frameSheet.TryGetValue(frame.Index, out var existing) && existing != sheet.NewSheetNumber)
                    throw new InvalidOperationException(
                        $"ambiguous Aspereta source frame {frame.Index} on sheets {existing} and {sheet.NewSheetNumber}");
                frameSheet[frame.Index] = sheet.NewSheetNumber;
            }

        var animDefs = new Dictionary<int, Animation>();
        foreach (var path in NumericAdfPaths(asperetaDataDir))
        {
            AdfFile adf;
            try { adf = AsperetaAdf.Load(path); }
            catch { continue; }
            if (adf.Type != AdfType.Graphic || adf.Animations is null) continue;
            foreach (var (animId, anim) in adf.Animations)
                animDefs[animId] = anim;
        }

        var resolved = new Dictionary<int, (int Id, List<(int Sheet, int Frame)> Frames)>();
        void TryImport(int sourceAnimId)
        {
            if (resolved.ContainsKey(sourceAnimId)) return;
            if (!animDefs.TryGetValue(sourceAnimId, out var anim))
            {
                if (frameSheet.TryGetValue(sourceAnimId, out var sheet))
                    resolved[sourceAnimId] = (sourceAnimId, new List<(int, int)> { (sheet, sourceAnimId) });
                return;
            }
            var frames = ResolveFrames(anim, frameSheet);
            if (frames is null) return;
            int id = AsperetaEffectsConverter.IsEffectId(sourceAnimId)
                ? AsperetaSheets.GraphicBase + sourceAnimId
                : sourceAnimId;
            resolved[sourceAnimId] = (id, frames);
        }

        var bodySheets = new Dictionary<int, HashSet<int>>();
        foreach (var entry in AsperetaCompiledEnc.Load(asperetaCompiledEncPath))
        {
            if (entry.Type != AnimationType.Body || entry.Id <= 100) continue;
            var reached = new HashSet<int>();
            for (int facing = 0; facing < 4; facing++)
            {
                foreach (var slot in new[] { entry.Walk(facing), entry.Attack(facing) })
                {
                    if (slot == 0) continue;
                    TryImport(slot);
                    if (resolved.TryGetValue(slot, out var r))
                        foreach (var (sheet, _) in r.Frames)
                            reached.Add(sheet);
                }
            }
            foreach (var sheet in reached)
            {
                if (!bodySheets.TryGetValue(sheet, out var bodies))
                    bodySheets[sheet] = bodies = new HashSet<int>();
                bodies.Add(entry.Id);
            }
        }

        var spellSheets = new HashSet<int>();
        foreach (var (animId, _) in animDefs)
        {
            if (!AsperetaEffectsConverter.IsEffectId(animId)) continue;
            TryImport(animId);
            if (resolved.TryGetValue(animId, out var r))
                foreach (var (sheet, _) in r.Frames)
                    spellSheets.Add(sheet);
        }

        var animations = new List<AnimationManifestAnimation>();
        foreach (var (_, r) in resolved)
        {
            var frames = new List<int[]>(r.Frames.Count);
            foreach (var (sheet, frame) in r.Frames)
                frames.Add(new[] { sheet, AsperetaSheets.GraphicBase + frame });
            animations.Add(new AnimationManifestAnimation
            {
                OwnerSheet = r.Frames[0].Sheet,
                Id = r.Id,
                Fps = Fps,
                Frames = frames,
            });
        }

        var categories = new Dictionary<int, List<(string Name, int? Id)>>();
        var sheetEntries = new SortedDictionary<int, AnimationManifestSheet>();
        foreach (var (_, sheet) in aspereta)
        {
            int number = sheet.NewSheetNumber;
            sheetEntries[number] = new AnimationManifestSheet();

            var list = new List<(string Name, int? Id)>();
            if (bodySheets.TryGetValue(number, out var bodies))
                foreach (var body in bodies)
                    list.Add((BodyCategory, AsperetaSheets.BodyBase + body));
            bool hasBody = list.Count > 0;
            bool hasSpell = spellSheets.Contains(number);
            if (hasSpell) list.Add((SpellsCategory, null));
            if (!hasBody && !hasSpell) list.Add((TilesCategory, null));
            categories[number] = list;
        }

        return (sheetEntries, animations, categories);
    }

    private static List<(int Sheet, int Frame)>? ResolveFrames(Animation anim, Dictionary<int, int> frameSheet)
    {
        IReadOnlyList<int> fids;
        if (anim.SourceFrameIds is { Count: > 0 })
            fids = anim.SourceFrameIds;
        else if (anim.Frames.Count > 0)
            fids = anim.Frames.Select(f => f.Index).ToList();
        else
            return null;

        var result = new List<(int, int)>(fids.Count);
        foreach (var fid in fids)
        {
            if (!frameSheet.TryGetValue(fid, out var sheet))
                return null;
            result.Add((sheet, fid));
        }
        return result;
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
