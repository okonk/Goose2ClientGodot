using System.Collections.Generic;
using Godot;

namespace Goose2Client.Map;

/// <summary>Builds a shared runtime <see cref="TileSet"/> for map layers from arbitrary-rect sheet
/// graphics. Each unique (sheet, graphic) becomes one <see cref="TileSetAtlasSource"/> whose single
/// tile uses <see cref="MapCoords.BottomCenterTextureOrigin"/> so tall sprites match the old
/// <c>MapLayer._Draw</c> bottom-center anchor.</summary>
public sealed class MapTileCatalog
{
    private readonly SpriteCache _cache;
    private readonly Dictionary<(int Sheet, int Graphic), int> _sourceIds = new();
    private int _nextSourceId = 1;

    public TileSet TileSet { get; }

    public MapTileCatalog(SpriteCache cache)
    {
        _cache = cache;
        TileSet = new TileSet
        {
            TileSize = new Vector2I(MapCoords.TileSize, MapCoords.TileSize),
        };
    }

    /// <summary>Ensure (sheet, graphic) exists in the TileSet. Returns false when the graphic is
    /// missing or sheet==0. Atlas coords are always (0,0) — each source holds one tile.</summary>
    public bool TryGetTile(int sheet, int graphic, out int sourceId, out Vector2I atlasCoords)
    {
        atlasCoords = Vector2I.Zero;
        sourceId = -1;
        if (sheet == 0 || graphic == 0) return false;

        if (_sourceIds.TryGetValue((sheet, graphic), out sourceId))
            return true;

        var tex = _cache.Get(sheet, graphic);
        if (tex == null) return false;

        var size = tex.GetSize();
        int w = Mathf.Max(1, Mathf.RoundToInt(size.X));
        int h = Mathf.Max(1, Mathf.RoundToInt(size.Y));

        var source = new TileSetAtlasSource
        {
            Texture = tex,
            TextureRegionSize = new Vector2I(w, h),
        };
        source.CreateTile(Vector2I.Zero);
        source.GetTileData(Vector2I.Zero, 0).TextureOrigin = MapCoords.BottomCenterTextureOrigin(w, h);

        sourceId = _nextSourceId++;
        TileSet.AddSource(source, sourceId);
        _sourceIds[(sheet, graphic)] = sourceId;
        return true;
    }
}
