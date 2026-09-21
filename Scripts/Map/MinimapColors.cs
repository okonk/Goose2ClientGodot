using System;
using Godot;
using MapEditor.Core;

namespace Goose2Client.Map;

public static class MinimapColors
{
    private static readonly int[] LayerOrder = { 4, 3, 2, 0 };

    public static Color? PickColor(MapTile tile, Func<int, int, Color?> provider)
    {
        foreach (int layer in LayerOrder)
        {
            var l = tile.GetLayer(layer);
            if (l.Sheet == 0 || l.Graphic == 0) continue;
            var c = provider(l.Sheet, l.Graphic);
            if (c != null) return c;
        }
        return null;
    }

    public static Color[] Build(MapDocument map, Func<int, int, Color?> provider)
    {
        var colors = new Color[map.Width * map.Height];
        for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
                colors[y * map.Width + x] = PickColor(map[x, y], provider) ?? Colors.Black;
        return colors;
    }
}
