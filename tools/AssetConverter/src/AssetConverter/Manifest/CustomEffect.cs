using Goose2.AssetConverter.SpriteFrames;

namespace Goose2.AssetConverter.Manifest;

public sealed class CustomEffect
{
    // Above the Illutia (<400000) and Aspereta (700000+) effect ranges.
    public const int IdBase = 900000;

    public static readonly CustomEffect BossTarget = new(IdBase + 1, CustomAssetSheet.BossTarget, fps: 12);

    public static readonly IReadOnlyList<CustomEffect> All = [BossTarget];

    private CustomEffect(int id, CustomAssetSheet sheet, float fps)
    {
        Id = id;
        Sheet = sheet;
        Fps = fps;
    }

    public int Id { get; }
    public CustomAssetSheet Sheet { get; }
    public float Fps { get; }

    public string RelativeOutputPath => $"Assets/Sprites/Effects/{Id}/animations.tres";

    public SpriteFramesAnimationSpec BuildAnimation()
    {
        string texturePath = $"res://Assets/Sprites/sheets/{Sheet.SheetNumber}.png";
        var frames = Enumerable.Range(1, Sheet.GraphicCount)
            .Select(g => new SpriteFrameSpec(Sheet.SheetNumber, texturePath, Sheet.CreateFrame(g)))
            .ToList();
        return new SpriteFramesAnimationSpec(Id.ToString(), frames, Speed: Fps);
    }

    public static int WriteAll(string outRoot)
    {
        foreach (var effect in All)
        {
            string fullPath = Path.Combine(outRoot, effect.RelativeOutputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, SpriteFramesWriter.Build([effect.BuildAnimation()]));
        }
        return All.Count;
    }
}
