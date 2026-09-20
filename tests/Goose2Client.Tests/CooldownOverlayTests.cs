using Goose2Client.UI;
using Xunit;

namespace Goose2Client.Tests
{
    public class CooldownOverlayTests
    {
        [Theory]
        [InlineData(0.5, "0.5")]
        [InlineData(1.0, "1")]
        [InlineData(45.3, "46")]
        [InlineData(59.9, "1:00")]
        [InlineData(60.0, "1:00")]
        [InlineData(90.2, "1:31")]
        [InlineData(125.0, "2:05")]
        [InlineData(3599.9, "1h")]
        [InlineData(3600.0, "1h")]
        [InlineData(3661.0, "1h1m")]
        [InlineData(7200.0, "2h")]
        [InlineData(8705.0, "2h25m")]
        [InlineData(8765.4, "2h26m")]
        public void FormatCountdown_FormatsByMagnitude(double remainingSeconds, string expected)
        {
            Assert.Equal(expected, CooldownOverlay.FormatCountdown(remainingSeconds));
        }
    }
}
