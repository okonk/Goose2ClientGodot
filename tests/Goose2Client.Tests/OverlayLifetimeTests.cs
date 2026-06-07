using Goose2Client.Overlays;
using Xunit;

public class OverlayLifetimeTests
{
    [Fact] public void NotExpiredBeforeDuration()
    { var l = new OverlayLifetime(1.0); l.Advance(0.5); Assert.False(l.Expired); }

    [Fact] public void ExpiredAtDuration()
    { var l = new OverlayLifetime(1.0); l.Advance(1.0); Assert.True(l.Expired); }

    [Fact] public void RiseAccumulatesAtRate()
    { var l = new OverlayLifetime(1.0, 32); l.Advance(0.5); Assert.Equal(16.0, l.RiseOffsetPixels, 3); }
}
