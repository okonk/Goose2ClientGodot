using Godot;
using Goose2Client;
using Xunit;

namespace Goose2Client.Tests;

public class MultiWindowMetricsTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(18)]
    [InlineData(19)]
    public void LinePosition_At1x_EqualsBaseFloatsExactly(int index)
    {
        var expected = new Vector2(6f, 22f + index * 11.18f);
        var actual = MultiWindowMetrics.LinePosition(index, 1f);
        Assert.Equal(expected.X, actual.X);
        Assert.Equal(expected.Y, actual.Y);
    }

    [Fact]
    public void LinePosition_At2x()
    {
        Assert.Equal(new Vector2(12, 44), MultiWindowMetrics.LinePosition(0, 2f));
        Assert.Equal(new Vector2(12, 66), MultiWindowMetrics.LinePosition(1, 2f));
        Assert.Equal(new Vector2(12, 469), MultiWindowMetrics.LinePosition(19, 2f));
    }

    [Fact]
    public void LinePosition_At15x()
    {
        Assert.Equal(new Vector2(9, 33), MultiWindowMetrics.LinePosition(0, 1.5f));
        Assert.Equal(new Vector2(9, 50), MultiWindowMetrics.LinePosition(1, 1.5f));
        Assert.Equal(new Vector2(9, 352), MultiWindowMetrics.LinePosition(19, 1.5f));
    }

    [Fact]
    public void LinePosition_RoundTrips1To2To1()
    {
        for (int i = 0; i < 20; i++)
        {
            var at1 = MultiWindowMetrics.LinePosition(i, 1f);
            MultiWindowMetrics.LinePosition(i, 2f);
            var back = MultiWindowMetrics.LinePosition(i, 1f);
            Assert.Equal(at1.X, back.X);
            Assert.Equal(at1.Y, back.Y);
        }
    }
}
