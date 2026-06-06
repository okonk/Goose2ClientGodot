using Goose2Client.Map;
using Xunit;

public class SpriteManifestTests
{
    private const string Json =
        "{\"tileSize\":32,\"sheets\":{\"1000\":{\"108760\":[0,0,48,64],\"108767\":[48,192,48,64]}}}";

    [Fact]
    public void TryGetRect_ReturnsRectForKnownSheetGraphic()
    {
        var m = SpriteManifest.Parse(Json);

        Assert.True(m.TryGetRect(1000, 108760, out var r));
        Assert.Equal((0, 0, 48, 64), (r.X, r.Y, r.W, r.H));

        Assert.True(m.TryGetRect(1000, 108767, out var r2));
        Assert.Equal((48, 192, 48, 64), (r2.X, r2.Y, r2.W, r2.H));
    }

    [Fact]
    public void TryGetRect_FalseForUnknown()
    {
        var m = SpriteManifest.Parse(Json);
        Assert.False(m.TryGetRect(9999, 1, out _));
        Assert.False(m.TryGetRect(1000, 1, out _));
    }
}
