using Godot;
using Xunit;

namespace Goose2Client.Tests;

public class PartyMemberMetricsTests
{
    [Fact]
    public void MinSize_1x_IsTscnBase()
        => Assert.Equal(new Vector2I(87, 33), PartyMemberMetrics.MinSize(1f));

    [Fact]
    public void MinSize_1_5x_RoundsHalfAway()
        => Assert.Equal(new Vector2I(131, 50), PartyMemberMetrics.MinSize(1.5f));

    [Fact]
    public void MinSize_2x()
        => Assert.Equal(new Vector2I(174, 66), PartyMemberMetrics.MinSize(2f));

    [Fact]
    public void MinSize_RoundTrip()
    {
        var one = PartyMemberMetrics.MinSize(1f);
        PartyMemberMetrics.MinSize(2f);
        Assert.Equal(one, PartyMemberMetrics.MinSize(1f));
    }
}
