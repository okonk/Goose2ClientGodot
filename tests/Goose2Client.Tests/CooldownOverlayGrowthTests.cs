using Goose2Client.UI;
using Xunit;

namespace Goose2Client.Tests
{
    public class CooldownOverlayGrowthTests
    {
        [Theory]
        [InlineData(1.0, 1.0, false, 1.0)]   // shrinking: full cover at cast
        [InlineData(0.5, 1.0, false, 0.5)]
        [InlineData(0.0, 1.0, false, 0.0)]   // shrinking: gone when done
        [InlineData(1.0, 1.0, true, 0.0)]    // growth: empty when fresh
        [InlineData(0.5, 1.0, true, 0.5)]
        [InlineData(0.0, 1.0, true, 1.0)]    // growth: full at expiry
        [InlineData(-1.0, 1.0, true, 1.0)]   // growth: clamped past expiry
        [InlineData(2.0, 1.0, false, 1.0)]   // shrinking: clamped
        [InlineData(0.5, 0.0, true, 0.0)]    // zero total: no pie
        public void ComputeProgress_MatchesMode(double remaining, double total, bool growth, float expected)
        {
            Assert.Equal(expected, CooldownOverlay.ComputeProgress(remaining, total, growth), 3);
        }

        [Fact]
        public void BlinkAlpha_OscillatesBetween03And10_WithOneSecondPeriod()
        {
            for (double t = 0; t < 10; t += 0.01)
            {
                var a = BuffEffect.BlinkAlpha(t);
                Assert.InRange(a, 0.3f, 1.0f);
            }

            for (double t = 0; t < 5; t += 0.37)
                Assert.Equal(BuffEffect.BlinkAlpha(t), BuffEffect.BlinkAlpha(t + 1), 3);
        }
    }
}
