using MapEditor.Core.Terrain;

namespace Goose2.AssetConverter.Terrain;

public sealed record TerrainImageFeatures(
    TerrainGraphicReference Reference,
    double[] AlphaCells,
    double[] Palette,
    double[] PerceptualCells,
    double[] Corners,
    double[] Edges,
    ulong PerceptualHash,
    TerrainFeatureBuckets Buckets)
{
    public const int FrameSize = 32;
    public const int AlphaCellCount = 64;
    public const int PaletteBinCount = 65;
    public const int PerceptualValueCount = 320;
    public const int CornerValueCount = 20;
    public const int EdgeValueCount = 640;
    public const int ChannelCount = 5;
}
