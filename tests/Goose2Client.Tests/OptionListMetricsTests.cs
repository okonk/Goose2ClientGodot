using Godot;
using Goose2Client;
using Xunit;

namespace Goose2Client.Tests;

public class OptionListMetricsTests
{
    [Theory]
    [InlineData(0, 22f)]
    [InlineData(1, 45f)]
    [InlineData(2, 68f)]
    [InlineData(3, 91f)]
    [InlineData(4, 114f)]
    [InlineData(5, 137f)]
    [InlineData(6, 160f)]
    [InlineData(7, 183f)]
    [InlineData(8, 206f)]
    [InlineData(9, 229f)]
    public void LinePosition_NoIcon_NoHeading_At1x(int index, float expectedY)
    {
        var p = OptionListMetrics.LinePosition(index, 1f, false, false);
        Assert.Equal(6f, p.X);
        Assert.Equal(expectedY, p.Y);
    }

    [Theory]
    [InlineData(0, 22f)]
    [InlineData(1, 58f)]
    [InlineData(3, 130f)]
    [InlineData(9, 346f)]
    public void LinePosition_WithIcon_NoHeading_At1x(int index, float expectedY)
    {
        var p = OptionListMetrics.LinePosition(index, 1f, false, true);
        Assert.Equal(6f, p.X);
        Assert.Equal(expectedY, p.Y);
    }

    [Fact]
    public void LinePosition_WithHeading_ShiftsDownOneHeadingRow()
    {
        Assert.Equal(new Vector2(6, 40), OptionListMetrics.LinePosition(0, 1f, true, false));
        Assert.Equal(new Vector2(6, 63), OptionListMetrics.LinePosition(1, 1f, true, false));
        Assert.Equal(new Vector2(6, 40), OptionListMetrics.LinePosition(0, 1f, true, true));
    }

    [Fact]
    public void LinePosition_At2x()
    {
        Assert.Equal(new Vector2(12, 44), OptionListMetrics.LinePosition(0, 2f, false, false));
        Assert.Equal(new Vector2(12, 182), OptionListMetrics.LinePosition(3, 2f, false, false));
        Assert.Equal(new Vector2(12, 458), OptionListMetrics.LinePosition(9, 2f, false, false));
        Assert.Equal(new Vector2(12, 260), OptionListMetrics.LinePosition(3, 2f, false, true));
    }

    [Fact]
    public void LineSize()
    {
        Assert.Equal(new Vector2(248, 23), OptionListMetrics.LineSize(1f, false));
        Assert.Equal(new Vector2(496, 46), OptionListMetrics.LineSize(2f, false));
        Assert.Equal(new Vector2(248, 36), OptionListMetrics.LineSize(1f, true));
        Assert.Equal(new Vector2(496, 72), OptionListMetrics.LineSize(2f, true));
    }

    [Fact]
    public void Heading()
    {
        Assert.Equal(new Vector2(6, 22), OptionListMetrics.HeadingPosition(1f));
        Assert.Equal(new Vector2(248, 18), OptionListMetrics.HeadingSize(1f));
    }

    [Fact]
    public void LineTextIndent()
    {
        Assert.Equal(36, OptionListMetrics.LineTextIndent(1f));
        Assert.Equal(72, OptionListMetrics.LineTextIndent(2f));
    }

    [Fact]
    public void WindowHeight()
    {
        // Icon-less rows are tighter (23px) than icon rows (36px).
        Assert.Equal(146, OptionListMetrics.WindowHeight(4, 1f, false, true, false));
        Assert.Equal(292, OptionListMetrics.WindowHeight(4, 2f, false, true, false));
        Assert.Equal(100, OptionListMetrics.WindowHeight(2, 1f, false, true, false));
        Assert.Equal(97, OptionListMetrics.WindowHeight(3, 1f, false, false, false));
        Assert.Equal(69, OptionListMetrics.WindowHeight(1, 1f, true, false, false));
        Assert.Equal(198, OptionListMetrics.WindowHeight(4, 1f, false, true, true));
        Assert.Equal(396, OptionListMetrics.WindowHeight(4, 2f, false, true, true));
    }

    [Fact]
    public void BottomButtons()
    {
        Assert.Equal(new Vector2(74, 26), OptionListMetrics.BottomButtonSize(1f));
        Assert.Equal(120f, OptionListMetrics.BottomButtonY(152f, 1f));
        Assert.Equal(240f, OptionListMetrics.BottomButtonY(304f, 2f));
    }
}
