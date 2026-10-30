using Godot;
using Goose2Client;
using Xunit;

namespace Goose2Client.Tests;

public class GridContainerMetricsTests
{
    [Fact]
    public void SlotPosition_At1x()
    {
        Assert.Equal(new Vector2(2, 20), GridContainerMetrics.SlotPosition(0, 5, 1f));
        Assert.Equal(new Vector2(35, 20), GridContainerMetrics.SlotPosition(1, 5, 1f));
        Assert.Equal(new Vector2(2, 53), GridContainerMetrics.SlotPosition(5, 5, 1f));
        Assert.Equal(new Vector2(134, 53), GridContainerMetrics.SlotPosition(9, 5, 1f));
    }

    [Fact]
    public void SlotPosition_At2x()
    {
        Assert.Equal(new Vector2(4, 40), GridContainerMetrics.SlotPosition(0, 5, 2f));
        Assert.Equal(new Vector2(70, 40), GridContainerMetrics.SlotPosition(1, 5, 2f));
        Assert.Equal(new Vector2(4, 106), GridContainerMetrics.SlotPosition(5, 5, 2f));
    }

    [Fact]
    public void Rows()
    {
        Assert.Equal(2, GridContainerMetrics.Rows(10, 5));
        Assert.Equal(3, GridContainerMetrics.Rows(11, 5));
        Assert.Equal(1, GridContainerMetrics.Rows(5, 5));
        Assert.Equal(1, GridContainerMetrics.Rows(0, 5));
        Assert.Equal(10, GridContainerMetrics.Rows(10, 0));
    }

    [Fact]
    public void WindowSize()
    {
        Assert.Equal(new Vector2(168, 221), GridContainerMetrics.WindowSize(30, 5, 1f));
        Assert.Equal(new Vector2(336, 442), GridContainerMetrics.WindowSize(30, 5, 2f));
    }

    [Fact]
    public void WindowSize_MinimumTitleWidthAppliesForSingleColumn()
    {
        Assert.Equal(new Vector2(100, 56), GridContainerMetrics.WindowSize(1, 1, 1f));
        Assert.Equal(new Vector2(200, 112), GridContainerMetrics.WindowSize(1, 1, 2f));
    }

    [Fact]
    public void Sanitize()
    {
        Assert.Equal((200, 10), GridContainerMetrics.Sanitize(5000, 20));
        Assert.Equal((200, 10), GridContainerMetrics.Sanitize(5000, 15));
        Assert.Equal((0, 1), GridContainerMetrics.Sanitize(-5, 0));
        Assert.Equal((10, 5), GridContainerMetrics.Sanitize(10, 5));
    }
}
