using Goose2Client.Character;
using Xunit;

public class CharacterAnchorTests
{
    [Theory]
    [InlineData(48, -24)]   // standard sprite: center 24px up so feet sit on the tile bottom
    [InlineData(64, -24)]   // h>=48 collapses to a constant -24 (taller sprites overhang downward)
    [InlineData(96, -24)]   // still -24
    [InlineData(32, -16)]   // short sprite: pure feet-align -h/2 = -16
    [InlineData(24, -12)]   // short sprite: -h/2 = -12
    public void OffsetY_aligns_feet_to_tile_bottom(int height, int expected)
        => Assert.Equal(expected, CharacterAnchor.OffsetY(height));
}
