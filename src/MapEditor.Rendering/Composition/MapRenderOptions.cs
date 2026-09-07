using System.Collections.Generic;
using MapEditor.Core;

namespace MapEditor.Rendering;

public sealed record MapRenderOptions(
    MapLayerVisibility VisibleLayers,
    bool ShowGrid,
    bool ShowBlocked,
    MapTileCoordinate? HoveredTile,
    MapTileCoordinate? SelectedTile,
    MapTileRectangle? SelectionRectangle = null,
    MapTileRectangle? PasteGhost = null,
    BlockPreview? BlockPreview = null,
    IReadOnlyList<GameDataMarkerInput>? SpawnMarkers = null,
    IReadOnlyList<GameDataMarkerInput>? WarpMarkers = null,
    bool PreviewMode = false)
{
    public static MapRenderOptions Default { get; } = new(MapLayerVisibility.All, false, false, null, null);
}

public readonly record struct GameDataMarkerInput(
    int OccurrenceIndex,
    MapTileCoordinate Tile,
    bool Selected,
    string Diagnostic);

public readonly record struct BlockPreview(MapTileRectangle Rectangle, bool Blocked);

public readonly record struct MapRenderRequest(
    MapDocument Document,
    ViewportTransform Viewport,
    MapRenderOptions Options);
