using System;
using System.IO;
using System.Linq;
using Goose2Client;
using Xunit;

public class MapFileTests
{
    private static string FixturePath =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Map10x10.bytes");

    [Fact]
    public void Fixture_ParsesHeaderAndGrid()
    {
        var bytes = File.ReadAllBytes(FixturePath);
        var map = new MapFile(bytes);

        // Real values carved from Map1.bytes — see tools/gen-map-fixture.py.
        Assert.Equal(146, map.Version);
        Assert.Equal(10, map.EditorVersion);
        Assert.Equal(10, map.Width);
        Assert.Equal(10, map.Height);
        Assert.Equal(100, map.Tiles.Length);

        // header(12) + 34 bytes/tile, exactly — the carved fixture has no trailer.
        Assert.Equal(12 + 34 * map.Width * map.Height, bytes.Length);

        // Indexer is (x, y) -> Tiles[y*Width + x]; first tile is reachable and well-formed.
        var t = map[0, 0];
        Assert.Equal(5, t.Layers.Length);
        Assert.All(t.Layers, l => Assert.NotNull(l));
        Assert.Equal(421500, t.Layers[0].Graphic);
        Assert.Equal(2286, t.Layers[0].Sheet);
        Assert.False(t.IsBlocked);

        // The region was picked for variety: some tiles carry the blocked bit.
        Assert.Equal(6, map.Tiles.Count(x => x.IsBlocked));
    }

    [Fact]
    public void MapTile_FlagsAndRoofDerive()
    {
        var blocked = new MapTile { Flags = 2 };
        Assert.True(blocked.IsBlocked);

        var open = new MapTile { Flags = 0 };
        Assert.False(open.IsBlocked);
        Assert.False(open.IsRoof);

        open.Layers[4].Graphic = 99;
        Assert.True(open.IsRoof);
    }
}
