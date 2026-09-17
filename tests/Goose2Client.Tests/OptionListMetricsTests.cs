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
    public void LinePosition_At1x(int index, float expectedY)
    {
        var p = OptionListMetrics.LinePosition(index, 1f);
        Assert.Equal(6f, p.X);
        Assert.Equal(expectedY, p.Y);
    }

    [Fact]
    public void LinePosition_At2x()
    {
        Assert.Equal(new Vector2(12, 44), OptionListMetrics.LinePosition(0, 2f));
        Assert.Equal(new Vector2(12, 182), OptionListMetrics.LinePosition(3, 2f));
        Assert.Equal(new Vector2(12, 458), OptionListMetrics.LinePosition(9, 2f));
    }

    [Fact]
    public void LineSize()
    {
        Assert.Equal(new Vector2(248, 23), OptionListMetrics.LineSize(1f));
        Assert.Equal(new Vector2(496, 46), OptionListMetrics.LineSize(2f));
    }

    [Theory]
    [InlineData(1, false, 51f)]
    [InlineData(2, true, 106f)]
    [InlineData(4, true, 152f)]
    [InlineData(10, false, 258f)]
    [InlineData(10, true, 290f)]
    public void WindowHeight_At1x(int lineCount, bool bottomButtons, float expected)
        => Assert.Equal(expected, OptionListMetrics.WindowHeight(lineCount, 1f, bottomButtons));

    [Fact]
    public void WindowHeight_At2x()
        => Assert.Equal(304f, OptionListMetrics.WindowHeight(4, 2f, true));

    [Fact]
    public void BottomButtons()
    {
        Assert.Equal(new Vector2(56, 26), OptionListMetrics.BottomButtonSize(1f));
        Assert.Equal(120f, OptionListMetrics.BottomButtonY(152f, 1f));
        Assert.Equal(240f, OptionListMetrics.BottomButtonY(304f, 2f));
    }
}
