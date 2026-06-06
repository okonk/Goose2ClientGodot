using Godot;
using Goose2Client.Map;
using Xunit;

public class MapCoordsTests
{
    [Fact]
    public void TileSize_Is32() => Assert.Equal(32, MapCoords.TileSize);

    [Fact]
    public void TileCenter_NoYFlip()
    {
        // Tile (0,0) is top-left of the world (Godot Y-down) — center at (16,16), NOT flipped.
        Assert.Equal(new Vector2(16, 16), MapCoords.TileCenter(0, 0));
        Assert.Equal(new Vector2(48, 80), MapCoords.TileCenter(1, 2));
    }

    [Fact]
    public void TileBottomCenter_IsCellBottomEdgeMidpoint()
    {
        // Bottom-center anchor of tile (0,0): x=16, y=32 (bottom edge of the 32px cell).
        Assert.Equal(new Vector2(16, 32), MapCoords.TileBottomCenter(0, 0));
        Assert.Equal(new Vector2(48, 96), MapCoords.TileBottomCenter(1, 2));
    }

    [Fact]
    public void WorldToTile_RoundTrips()
    {
        Assert.Equal((3, 5), MapCoords.WorldToTile(new Vector2(3 * 32 + 5, 5 * 32 + 5)));
    }
}
