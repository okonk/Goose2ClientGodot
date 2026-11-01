using Goose2.AssetConverter.Adf;
using Goose2.AssetConverter.SpriteFrames;

namespace Goose2.AssetConverter.Manifest;

public sealed class CustomBody
{
    public const int IdBase = 20000;

    public static readonly CustomBody RuneTable = new(IdBase, CustomAssetSheet.RuneTableBody);

    public static readonly IReadOnlyList<CustomBody> All = [RuneTable];

    private static readonly string[] ClipBases = ["idle", "idle-no-equip", "idle-equip", "walk", "walk-no-equip", "walk-equip"];

    private CustomBody(int id, CustomAssetSheet sheet)
    {
        Id = id;
        Sheet = sheet;
    }

    public int Id { get; }
    public CustomAssetSheet Sheet { get; }

    public static IReadOnlyList<CompiledSpriteFramesResource> BuildResources() =>
        All.Select(b => b.BuildResource()).ToList();

    public CompiledSpriteFramesResource BuildResource()
    {
        string texturePath = $"res://Assets/Sprites/sheets/{Sheet.SheetNumber}.png";
        var frames = Enumerable.Range(1, Sheet.GraphicCount)
            .Select(g => new SpriteFrameSpec(Sheet.SheetNumber, texturePath, Sheet.CreateFrame(g)))
            .ToList();

        var animations = new List<SpriteFramesAnimationSpec>();
        foreach (string clip in ClipBases)
            foreach (AnimationDirection direction in Enum.GetValues<AnimationDirection>())
                animations.Add(new SpriteFramesAnimationSpec($"{clip}-{AnimationNaming.DirectionName(direction)}", frames));

        string key = $"{AnimationType.Body}-{Id}";
        var heights = animations.ToDictionary(a => $"{key}-{a.Name}", _ => Sheet.CellHeight);
        var first = frames[0].Frame;
        var firstFrames = new Dictionary<string, AnimationFrameInfo>
        {
            [key] = new(Sheet.SheetNumber, first.Index, first.W, first.H),
        };

        return new CompiledSpriteFramesResource(
            AnimationType.Body,
            Id,
            AnimationNaming.ResourceRelativePath(AnimationType.Body, Id),
            animations,
            firstFrames,
            heights,
            Array.Empty<string>());
    }

    public static int WriteAll(string outRoot)
    {
        var resources = BuildResources();
        foreach (var resource in resources)
        {
            string fullPath = Path.Combine(outRoot, resource.RelativeOutputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, SpriteFramesWriter.Build(resource.Animations));
        }

        string resourcesDir = Path.Combine(outRoot, "Assets", "Resources");
        var heights = AnimationMetadataWriter.LoadHeights(Path.Combine(resourcesDir, "AnimationHeights.txt"));
        var firstFrames = AnimationMetadataWriter.LoadFirstFrames(Path.Combine(resourcesDir, "AnimationToFirstFrame.txt"));
        foreach (var resource in resources)
        {
            foreach (var (name, height) in resource.AnimationHeights)
                heights[name] = height;
            foreach (var (name, info) in resource.AnimationToFirstFrame)
                firstFrames[name] = info;
        }
        AnimationMetadataWriter.Write(resourcesDir, firstFrames, heights);
        return resources.Count;
    }

    public static void AddManifestSheets(
        SortedDictionary<int, AnimationManifestSheet> sheets,
        Dictionary<int, List<(string Name, int? Id)>> categories)
    {
        foreach (var body in All)
        {
            if (!sheets.TryAdd(body.Sheet.SheetNumber, new AnimationManifestSheet()))
                throw new InvalidDataException($"Custom body sheet {body.Sheet.SheetNumber} conflicts with a source sheet.");
            categories[body.Sheet.SheetNumber] = [(AnimationType.Body.ToString(), body.Id)];
        }
    }
}
