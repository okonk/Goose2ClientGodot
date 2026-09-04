using System;

namespace MapEditor.Rendering;

public enum SpriteSampling
{
    NearestNeighbor
}

public enum CellOverlayKind
{
    Blocked,
    Selected,
    Hovered,
    PasteGhost,
    BlockPreview
}

public readonly record struct SpriteDrawOperation(
    int Layer,
    MapTileCoordinate Tile,
    SpriteReference Reference,
    ISpriteSheetImage Image,
    SpriteSourceRect SourceRect,
    RenderRect DestinationRect,
    SpriteSampling Sampling);

public readonly record struct PlaceholderDrawOperation(
    int Layer,
    MapTileCoordinate Tile,
    SpriteReference Reference,
    SpriteResolutionStatus Reason,
    RenderRect DestinationRect,
    RenderColor FillColor,
    RenderColor StrokeColor,
    string Diagnostic);

public readonly record struct CellOverlayDrawOperation(
    CellOverlayKind Kind,
    MapTileCoordinate Tile,
    RenderRect DestinationRect,
    RenderColor FillColor,
    RenderColor StrokeColor);

public readonly record struct GridLineDrawOperation(
    RenderPoint Start,
    RenderPoint End,
    RenderColor Color);

public sealed class MapRenderWorkLimitException : Exception
{
    public long RequestedTileReads { get; }

    public int MaximumTileReads { get; }

    public MapRenderWorkLimitException(long requestedTileReads, int maximumTileReads, string message)
        : base(message)
    {
        RequestedTileReads = requestedTileReads;
        MaximumTileReads = maximumTileReads;
    }
}
