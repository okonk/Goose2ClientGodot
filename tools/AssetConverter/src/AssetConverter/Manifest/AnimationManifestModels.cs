namespace Goose2.AssetConverter.Manifest;

public sealed class AnimationManifest
{
    public int Version { get; init; }
    public SortedDictionary<int, AnimationManifestSheet> Sheets { get; init; } = new();
    public List<AnimationManifestAnimation> Animations { get; init; } = new();
}

public sealed class AnimationManifestSheet
{
    public List<AnimationManifestCategory> Categories { get; init; } = new();
}

public sealed class AnimationManifestCategory
{
    public string Name { get; init; } = string.Empty;
    public int? Id { get; init; }
}

public sealed class AnimationManifestAnimation
{
    public int OwnerSheet { get; init; }
    public int Id { get; init; }
    public int Fps { get; init; }
    public List<int[]> Frames { get; init; } = new();
}
