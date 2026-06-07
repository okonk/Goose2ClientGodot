using Goose2Client.Overlays;
using Xunit;

public class SpellLayoutTests
{
    [Theory]
    [InlineData(64, -32)]   // (64-48)/2 = 8; -(8+24) = -32
    [InlineData(48, -24)]   // (48-48)/2 = 0; -(0+24) = -24
    [InlineData(100, -50)]  // (100-48)/2 = 26; -(26+24) = -50
    [InlineData(40, -24)]   // max((40-48)/2,0) = 0 (clamp negative to 0); -(0+24) = -24
    [InlineData(49, -24)]   // (49-48)/2 = 0 (integer div); -(0+24) = -24
    public void VerticalOffsetPixels_returns_unity_sign_value(int height, int expected)
        => Assert.Equal(expected, SpellLayout.VerticalOffsetPixels(height));
}
