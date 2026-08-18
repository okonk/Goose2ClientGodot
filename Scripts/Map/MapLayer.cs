using Godot;
using Goose2Client;

namespace Goose2Client.Map;

/// <summary>Renders a single flat map layer (0,1,3,4) as a <see cref="TileMapLayer"/> so Godot
/// culls off-screen tiles. Layer 2 stays on <see cref="ObjectLayer"/> for per-sprite Y-sort with
/// characters. Cell art is bottom-center anchored via <see cref="MapTileCatalog"/>.</summary>
public partial class MapLayer : TileMapLayer
{
    private MapFile _map;
    private int _layer;
    private MapTileCatalog _catalog;

    public void Setup(MapFile map, int layer, MapTileCatalog catalog)
    {
        _map = map;
        _layer = layer;
        _catalog = catalog;
        TileSet = catalog.TileSet;
        ZIndex = layer * 10;     // 0,10,20,30,40. Matches Unity sorting layers: dropped items sit at
                                 // z=14 and characters at z=15 (both below Objects 1 @20); roofs @40.

        for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
                RefreshCell(x, y);
    }

    /// <summary>Sync one cell from current map data (initial fill + TileUpdate).</summary>
    public void RefreshCell(int x, int y)
    {
        var coords = new Vector2I(x, y);
        var l = _map[x, y].Layers[_layer];
        if (l.Graphic == 0 || !_catalog.TryGetTile(l.Sheet, l.Graphic, out int sourceId, out var atlas))
        {
            EraseCell(coords);
            return;
        }

        SetCell(coords, sourceId, atlas);
    }
}
