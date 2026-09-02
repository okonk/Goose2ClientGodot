using System;
using MapEditor.Core;

namespace Goose2Client
{
    internal static class MapTileUpdate
    {
        public static void Apply(MapDocument map, int x, int y, int flags, ReadOnlySpan<int> tiles, Action<int> redraw)
        {
            if (tiles.Length != MapDocument.LayerCount * 2)
            {
                throw new ArgumentException($"Expected {MapDocument.LayerCount * 2} tile values, got {tiles.Length}.", nameof(tiles));
            }

            map.SetFlags(x, y, flags);
            for (int layer = 0; layer < MapDocument.LayerCount; layer++)
            {
                int graphic = tiles[layer * 2];
                int sheet = tiles[layer * 2 + 1];
                var replacement = new MapTileLayer(sheet, sheet == 0 ? 0 : graphic);
                if (map[x, y].GetLayer(layer) == replacement) continue;

                map.SetLayer(x, y, layer, replacement);
                redraw(layer);
            }
        }
    }
}
