using Godot;
using Goose2Client;
using Xunit;

namespace Goose2Client.Tests;

public class VitalsPortraitMetricsTests
{
    [Fact]
    public void Layout_At1x_Square_EqualsCurrentLiterals()
    {
        var (size, pos) = VitalsPortraitMetrics.Layout(new Vector2(32, 32), 20f, 1f);
        Assert.Equal(new Vector2(40, 40), size);
        Assert.Equal(new Vector2(6.5f, 26.5f), pos);
    }

    [Fact]
    public void Layout_At1x_NonSquare_EqualsCurrentLiterals()
    {
        var (size, pos) = VitalsPortraitMetrics.Layout(new Vector2(24, 32), 20f, 1f);
        Assert.Equal(new Vector2(30, 40), size);
        Assert.Equal(new Vector2(11.5f, 26.5f), pos);
    }

    [Fact]
    public void Layout_At1x_MonsterDropZero()
    {
        var (size, pos) = VitalsPortraitMetrics.Layout(new Vector2(24, 32), 0f, 1f);
        Assert.Equal(new Vector2(30, 40), size);
        Assert.Equal(new Vector2(11.5f, 6.5f), pos);
    }

    [Fact]
    public void Layout_At15x()
    {
        var (size, pos) = VitalsPortraitMetrics.Layout(new Vector2(32, 32), 20f, 1.5f);
        Assert.Equal(new Vector2(60, 60), size);
        Assert.Equal(new Vector2(9.75f, 39.75f), pos);
    }

    [Fact]
    public void Layout_At2x_Square()
    {
        var (size, pos) = VitalsPortraitMetrics.Layout(new Vector2(32, 32), 20f, 2f);
        Assert.Equal(new Vector2(80, 80), size);
        Assert.Equal(new Vector2(13f, 53f), pos);
    }

    [Fact]
    public void Layout_At2x_NonSquare()
    {
        var (size, pos) = VitalsPortraitMetrics.Layout(new Vector2(24, 32), 20f, 2f);
        Assert.Equal(new Vector2(60, 80), size);
        Assert.Equal(new Vector2(23f, 53f), pos);
    }

    [Fact]
    public void Layout_RoundTrips1To2To1()
    {
        var humanoid = VitalsPortraitMetrics.Layout(new Vector2(32, 32), 20f, 1f);
        var monster = VitalsPortraitMetrics.Layout(new Vector2(24, 32), 0f, 1f);
        VitalsPortraitMetrics.Layout(new Vector2(32, 32), 20f, 2f);
        VitalsPortraitMetrics.Layout(new Vector2(24, 32), 0f, 2f);
        Assert.Equal(humanoid, VitalsPortraitMetrics.Layout(new Vector2(32, 32), 20f, 1f));
        Assert.Equal(monster, VitalsPortraitMetrics.Layout(new Vector2(24, 32), 0f, 1f));
    }
}
