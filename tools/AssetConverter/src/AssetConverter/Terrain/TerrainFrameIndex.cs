using MapEditor.Core.Terrain;

namespace Goose2.AssetConverter.Terrain;

public readonly record struct TerrainFrameRect(int X, int Y, int Width, int Height);

public readonly record struct TerrainFrame(TerrainGraphicReference Reference, TerrainFrameRect Rect);

public sealed class TerrainFrameIndex
{
    private readonly IReadOnlyDictionary<int, IReadOnlyList<TerrainFrame>> _framesBySheet;

    public int TileSize { get; }
    public IReadOnlyList<int> Sheets { get; }
    public IReadOnlyList<TerrainFrame> Frames { get; }

    internal TerrainFrameIndex(
        int tileSize,
        IReadOnlyList<int> sheets,
        IReadOnlyList<TerrainFrame> frames,
        IReadOnlyDictionary<int, IReadOnlyList<TerrainFrame>> framesBySheet)
    {
        TileSize = tileSize;
        Sheets = sheets;
        Frames = frames;
        _framesBySheet = framesBySheet;
    }

    public bool TryGetRect(TerrainGraphicReference reference, out TerrainFrameRect rect)
    {
        if (_framesBySheet.TryGetValue(reference.Sheet, out IReadOnlyList<TerrainFrame>? frames))
        {
            foreach (var frame in frames)
            {
                if (frame.Reference.Graphic == reference.Graphic)
                {
                    rect = frame.Rect;
                    return true;
                }
            }
        }

        rect = default;
        return false;
    }

    public IReadOnlyList<TerrainFrame> GetFrames(int sheet)
        => _framesBySheet.TryGetValue(sheet, out IReadOnlyList<TerrainFrame>? frames) ? frames : Array.Empty<TerrainFrame>();
}
