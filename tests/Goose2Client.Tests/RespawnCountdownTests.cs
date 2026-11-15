using Goose2Client.Character;
using Xunit;

public class RespawnCountdownTests
{
    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(7, "0:07")]
    [InlineData(47, "0:47")]
    [InlineData(60, "1:00")]
    [InlineData(-3, "0:00")]
    public void Format_is_minutes_and_padded_seconds(int seconds, string expected)
        => Assert.Equal(expected, RespawnCountdown.Format(seconds));

    [Theory]
    [InlineData(10_000UL, 10_000UL, 0)]
    [InlineData(10_000UL, 9_001UL, 1)]
    [InlineData(10_000UL, 9_000UL, 1)]
    [InlineData(10_000UL, 8_999UL, 2)]
    [InlineData(10_000UL, 12_000UL, 0)]
    public void SecondsLeft_rounds_up_and_clamps(ulong deadline, ulong now, int expected)
        => Assert.Equal(expected, RespawnCountdown.SecondsLeft(deadline, now));

    [Fact]
    public void Deadline_adds_seconds()
        => Assert.Equal(65_000UL, RespawnCountdown.Deadline(5_000UL, 60));

    [Fact]
    public void NameLabel_appends_the_countdown()
        => Assert.Equal("Asp (0:47)", RespawnCountdown.NameLabel("Asp", 47));
}
