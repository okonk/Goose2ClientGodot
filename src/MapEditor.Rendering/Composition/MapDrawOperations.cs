using System;
using System.Collections.Generic;
using MapEditor.GameData.Rows;

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

public enum GameDataMarkerKind
{
    Spawn,
    Warp
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

public readonly record struct GameDataMarkerDrawOperation(
    GameDataMarkerKind Kind,
    int OccurrenceIndex,
    MapTileCoordinate Tile,
    RenderRect DestinationRect,
    bool Selected,
    string Diagnostic,
    string? Name = null);

public readonly record struct GridLineDrawOperation(
    RenderPoint Start,
    RenderPoint End,
    RenderColor Color);

public readonly record struct NpcImageDrawOperation(
    int OccurrenceIndex,
    NpcPartSlot Slot,
    SpriteReference Reference,
    ISpriteSheetImage Image,
    SpriteSourceRect SourceRect,
    RenderRect DestinationRect,
    RgbaValue Tint,
    SpriteSampling Sampling);

public readonly record struct NpcPartPlaceholderDrawOperation(
    int OccurrenceIndex,
    NpcPartSlot Slot,
    SpriteResolutionStatus Reason,
    RenderRect DestinationRect,
    RenderColor FillColor,
    RenderColor StrokeColor,
    string Diagnostic);

public readonly record struct NpcSpawnAnchorDrawOperation(
    int OccurrenceIndex,
    MapTileCoordinate Tile,
    RenderRect DestinationRect,
    bool Selected,
    RenderColor FillColor,
    RenderColor StrokeColor,
    string? Diagnostic);

public readonly record struct NpcNameDrawOperation(
    int OccurrenceIndex,
    string Name,
    RenderPoint Center,
    double FontSize);

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
