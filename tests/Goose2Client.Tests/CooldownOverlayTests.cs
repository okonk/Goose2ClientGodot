using Goose2Client.UI;
using Xunit;

namespace Goose2Client.Tests
{
    public class CooldownOverlayTests
    {
        [Theory]
        [InlineData(90.2, "1:31")]
        [InlineData(60.0, "1:00")]
        [InlineData(59.9, "60")]
        [InlineData(125.0, "2:05")]
        [InlineData(7200.0, "120:00")]
        [InlineData(45.3, "46")]
        [InlineData(1.0, "1")]
        [InlineData(0.5, "0.5")]
        public void FormatCountdown_FormatsByMagnitude(double remainingSeconds, string expected)
        {
            Assert.Equal(expected, CooldownOverlay.FormatCountdown(remainingSeconds));
        }
    }
}
