using Goose2Client;
using Xunit;

namespace Goose2Client.Tests;

public class TooltipMetricsTests
{
    [Fact]
    public void ItemMetrics_At1x_EqualsCurrentLiterals()
        => Assert.Equal((40, 9, 46, 48, 4, 32, 4, 60), TooltipMetrics.ItemMetrics(1f));

    [Fact]
    public void ItemMetrics_At15x()
        => Assert.Equal((60, 14, 69, 72, 6, 48, 6, 90), TooltipMetrics.ItemMetrics(1.5f));

    [Fact]
    public void ItemMetrics_At2x()
        => Assert.Equal((80, 18, 92, 96, 8, 64, 8, 120), TooltipMetrics.ItemMetrics(2f));

    [Fact]
    public void ItemMetrics_RoundTrips1To2To1()
    {
        var at1 = TooltipMetrics.ItemMetrics(1f);
        TooltipMetrics.ItemMetrics(2f);
        Assert.Equal(at1, TooltipMetrics.ItemMetrics(1f));
    }

    [Fact]
    public void TextPad_At1x_EqualsCurrentLiterals()
        => Assert.Equal((8, 4), TooltipMetrics.TextPad(1f));

    [Fact]
    public void TextPad_At15x()
        => Assert.Equal((12, 6), TooltipMetrics.TextPad(1.5f));

    [Fact]
    public void TextPad_At2x()
        => Assert.Equal((16, 8), TooltipMetrics.TextPad(2f));

    [Fact]
    public void TextPad_RoundTrips1To2To1()
    {
        var at1 = TooltipMetrics.TextPad(1f);
        TooltipMetrics.TextPad(2f);
        Assert.Equal(at1, TooltipMetrics.TextPad(1f));
    }

    [Fact]
    public void MapItemMetrics_At1x_EqualsCurrentLiterals()
        => Assert.Equal((6, 4, 2, 4, 400), TooltipMetrics.MapItemMetrics(1f));

    [Fact]
    public void MapItemMetrics_At15x()
        => Assert.Equal((9, 6, 3, 6, 600), TooltipMetrics.MapItemMetrics(1.5f));

    [Fact]
    public void MapItemMetrics_At2x()
        => Assert.Equal((12, 8, 4, 8, 800), TooltipMetrics.MapItemMetrics(2f));

    [Fact]
    public void MapItemMetrics_RoundTrips1To2To1()
    {
        var at1 = TooltipMetrics.MapItemMetrics(1f);
        TooltipMetrics.MapItemMetrics(2f);
        Assert.Equal(at1, TooltipMetrics.MapItemMetrics(1f));
    }
}
