using MapEditor.Core;

namespace MapEditor.Rendering;

public sealed record MapRenderOptions(
    MapLayerVisibility VisibleLayers,
    bool ShowGrid,
    bool ShowBlocked,
    MapTileCoordinate? HoveredTile,
    MapTileCoordinate? SelectedTile,
    MapTileRectangle? SelectionRectangle = null,
    MapTileRectangle? PasteGhost = null)
{
    public static MapRenderOptions Default { get; } = new(MapLayerVisibility.All, false, false, null, null);
}

public readonly record struct MapRenderRequest(
    MapDocument Document,
    ViewportTransform Viewport,
    MapRenderOptions Options);
