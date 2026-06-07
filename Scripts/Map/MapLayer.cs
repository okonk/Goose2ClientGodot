using Godot;
using Goose2Client;

namespace Goose2Client.Map;

/// <summary>Renders a single map layer (0..4) by drawing each non-empty cell's AtlasTexture at the
/// cell's bottom-center. No TileMapLayer: the source art is arbitrary-rect + bottom-center anchored,
/// which draws directly here. z_index orders the 5 layers; layer 4 (roofs) toggles Visible.</summary>
public partial class MapLayer : Node2D
{
    private MapFile _map;
    private int _layer;
    private SpriteCache _cache;

    public void Setup(MapFile map, int layer, SpriteCache cache)
    {
        _map = map;
        _layer = layer;
        _cache = cache;
        ZIndex = layer * 10;     // 0,10,20,30,40. Matches Unity sorting layers: dropped items sit at
                                 // z=14 and characters at z=15 (both below Objects 1 @20); roofs @40.
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_map == null) return;

        for (int y = 0; y < _map.Height; y++)
        {
            for (int x = 0; x < _map.Width; x++)
            {
                var l = _map[x, y].Layers[_layer];
                if (l.Graphic == 0) continue;             // empty cell

                var tex = _cache.Get(l.Sheet, l.Graphic);
                if (tex == null) continue;

                DrawCell(x, y, tex);
            }
        }
    }

    private void DrawCell(int x, int y, Texture2D tex)
    {
        var size = tex.GetSize();
        var anchor = MapCoords.TileBottomCenter(x, y);    // bottom-center of the cell
        var topLeft = new Vector2(anchor.X - size.X / 2f, anchor.Y - size.Y);
        DrawTexture(tex, topLeft);
    }
}
