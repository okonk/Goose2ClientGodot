using System.Collections.Generic;
using Godot;
using Goose2Client;

namespace Goose2Client.Map;

/// <summary>Renders map layer 2 ("Objects 1": trees, building walls, etc.) as individual
/// bottom-anchored Sprite2D nodes so they Y-sort per-object against the characters, which share this
/// node's z_index. Mirrors Unity, where the "Objects 1" tilemap is Individual-mode + BottomLeft sort
/// on the same sorting layer as characters: the player passes in front of an object's base and behind
/// its top. (The flat ground/roof layers stay in the immediate-draw MapLayer, which can't Y-sort
/// against sibling character nodes because it draws a whole layer as one node.)</summary>
public partial class ObjectLayer : Node2D
{
    private MapFile _map;
    private int _layer;
    private SpriteCache _cache;
    private readonly Dictionary<(int X, int Y), Sprite2D> _sprites = new();

    public void Setup(MapFile map, int layer, SpriteCache cache)
    {
        _map = map;
        _layer = layer;
        _cache = cache;
        for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
                RefreshCell(x, y);
    }

    /// <summary>Rebuild a single cell's sprite from current map data (called on TileUpdate).</summary>
    public void RefreshCell(int x, int y)
    {
        if (_sprites.Remove((x, y), out var old))
            old.QueueFree();

        var l = _map[x, y].Layers[_layer];
        if (l.Graphic == 0) return;                       // empty cell

        var tex = _cache.Get(l.Sheet, l.Graphic);
        if (tex == null) return;

        var size = tex.GetSize();
        // Node origin sits at the cell's bottom-center — the Y-sort anchor (the object's footing). The
        // texture is offset up and left so it grows upward from that base, an exact match for
        // MapLayer.DrawCell's top-left placement. Texture filtering is left inherited (project default
        // Nearest), identical to MapLayer.
        var sprite = new Sprite2D
        {
            Texture = tex,
            Centered = false,
            Offset = new Vector2(-size.X / 2f, -size.Y),
            Position = MapCoords.TileBottomCenter(x, y),
        };
        AddChild(sprite);
        _sprites[(x, y)] = sprite;
    }
}
