using System.Collections.Generic;
using System.Linq;
using Goose2Client;
using MapEditor.Core;
using Xunit;

public class MapItemKeyTests
{
    [Fact]
    public void ItemKey_IsUniquePerTileWhenWidthExceedsHeight()
    {
        MapDocument map = MapDocument.Create(5, 3);
        var keys = new List<int>();
        for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
                keys.Add(MapManager.ItemKey(map, x, y));

        Assert.Equal(map.TileCount, keys.Distinct().Count());
    }
}
