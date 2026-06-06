using System.IO;
using Goose2Client;
using Xunit;

public class MapFileTests
{
    private const string MapsDir = "/home/agent/workspace/Goose2ClientGodot/Assets/Maps";

    [Fact]
    public void Map1_ParsesHeaderAndGrid()
    {
        var bytes = File.ReadAllBytes(Path.Combine(MapsDir, "Map1.bytes"));
        var map = new MapFile(bytes);

        Assert.Equal(146, map.Version);
        Assert.Equal(10, map.EditorVersion);
        Assert.Equal(286, map.Width);
        Assert.Equal(194, map.Height);
        Assert.Equal(286 * 194, map.Tiles.Length);

        // File is at least header(12) + 34 bytes/tile (may have a trailer).
        Assert.True(bytes.Length >= 12 + 34 * map.Width * map.Height);

        // Indexer is (x, y) -> Tiles[y*Width + x]; first tile is reachable and well-formed.
        var t = map[0, 0];
        Assert.Equal(5, t.Layers.Length);
        Assert.All(t.Layers, l => Assert.NotNull(l));
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
