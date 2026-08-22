using Goose2Client;
using Xunit;

namespace Goose2Client.Tests;

public class TooltipMetricsTests
{
    [Fact]
    public void ItemMetrics_At1x_EqualsCurrentLiterals()
        => Assert.Equal((52, 10, 49, 51, 10, 32, 10, 60, 10, 23, 36), TooltipMetrics.ItemMetrics(1f));

    [Fact]
    public void ItemMetrics_At15x()
        => Assert.Equal((78, 15, 74, 77, 15, 48, 15, 90, 15, 35, 54), TooltipMetrics.ItemMetrics(1.5f));

    [Fact]
    public void ItemMetrics_At2x()
        => Assert.Equal((104, 20, 98, 102, 20, 64, 20, 120, 20, 46, 72), TooltipMetrics.ItemMetrics(2f));

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
