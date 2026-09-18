using Godot;
using Goose2Client.UI;
using Xunit;

namespace Goose2Client.Tests;

public class CustomWindowMetricsTests
{
    [Fact]
    public void SampleGradient_Corners()
    {
        Assert.Equal(new Vector3I(255, 0, 0), CustomWindowMetrics.SampleGradient(new Vector2(0, 0)));
        Assert.Equal(new Vector3I(0, 255, 0), CustomWindowMetrics.SampleGradient(new Vector2(1, 0)));
        Assert.Equal(new Vector3I(0, 0, 255), CustomWindowMetrics.SampleGradient(new Vector2(0, 1)));
        Assert.Equal(new Vector3I(255, 255, 255), CustomWindowMetrics.SampleGradient(new Vector2(1, 1)));
    }

    [Fact]
    public void SampleGradient_EdgeMidpoints()
    {
        Assert.Equal(new Vector3I(128, 128, 0), CustomWindowMetrics.SampleGradient(new Vector2(0.5f, 0)));
        Assert.Equal(new Vector3I(128, 255, 128), CustomWindowMetrics.SampleGradient(new Vector2(1, 0.5f)));
        Assert.Equal(new Vector3I(128, 0, 128), CustomWindowMetrics.SampleGradient(new Vector2(0, 0.5f)));
        Assert.Equal(new Vector3I(128, 128, 255), CustomWindowMetrics.SampleGradient(new Vector2(0.5f, 1)));
    }

    [Fact]
    public void SampleGradient_Center()
    {
        Assert.Equal(new Vector3I(128, 128, 128), CustomWindowMetrics.SampleGradient(new Vector2(0.5f, 0.5f)));
    }

    [Fact]
    public void SampleGradient_ClampsOutsideUnitSquare()
    {
        Assert.Equal(new Vector3I(255, 0, 0), CustomWindowMetrics.SampleGradient(new Vector2(-2f, -2f)));
        Assert.Equal(new Vector3I(255, 255, 255), CustomWindowMetrics.SampleGradient(new Vector2(3f, 3f)));
    }

    [Fact]
    public void Defaults_AreWhiteWithDefaultAlpha()
    {
        Assert.Equal(255, CustomWindowMetrics.DefaultR);
        Assert.Equal(255, CustomWindowMetrics.DefaultG);
        Assert.Equal(255, CustomWindowMetrics.DefaultB);
        Assert.Equal(160, CustomWindowMetrics.DefaultA);
    }
}
