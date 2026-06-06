using Goose2Client.Character;
using Xunit;

public class CharacterAnchorTests
{
    [Theory]
    [InlineData(48, -16)]   // baseline body height: just the -16 base
    [InlineData(64, -24)]   // -((64-48)/2) - 16 = -8 - 16
    [InlineData(96, -40)]   // -((96-48)/2) - 16 = -24 - 16
    [InlineData(32, -16)]   // shorter than 48 clamps the first term to 0
    public void OffsetY_matches_unity_formula(int height, int expected)
        => Assert.Equal(expected, CharacterAnchor.OffsetY(height));
}
