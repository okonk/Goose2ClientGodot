using System;
using Godot;
using MapEditor.Core;

namespace Goose2Client.Map;

public static class MinimapColors
{
    private static readonly int[] LayerOrder = { 4, 3, 2, 0 };

    /// <summary>Tiles with no colored layer: the theme's panel navy, not pure black.</summary>
    public static readonly Color Empty = new(0.047059f, 0.066667f, 0.12549f);

    /// <summary>Maps fill void with black-ish tiles; at or below this max channel a tile reads as empty.</summary>
    public const float NearBlackMax = 24f / 255f;

    public static Color? PickColor(MapTile tile, Func<int, int, Color?> provider)
    {
        foreach (int layer in LayerOrder)
        {
            var l = tile.GetLayer(layer);
            if (l.Sheet == 0 || l.Graphic == 0) continue;
            var c = provider(l.Sheet, l.Graphic);
            if (c != null) return IsNearBlack(c.Value) ? Empty : c;
        }
        return null;
    }

    public static bool IsNearBlack(Color c) => Mathf.Max(c.R, Mathf.Max(c.G, c.B)) <= NearBlackMax;

    public static Color[] Build(MapDocument map, Func<int, int, Color?> provider)
    {
        var colors = new Color[map.Width * map.Height];
        for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
                colors[y * map.Width + x] = PickColor(map[x, y], provider) ?? Empty;
        return colors;
    }
}
