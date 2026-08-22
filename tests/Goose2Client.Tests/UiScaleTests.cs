using System;
using Godot;
using Xunit;

namespace Goose2Client.Tests;

public class UiScaleTests
{
    [Theory]
    [InlineData(0.4f, 1f)]
    [InlineData(0.9f, 1f)]
    [InlineData(1.25f, 1.5f)]
    [InlineData(1.7f, 1.5f)]
    [InlineData(2.3f, 2.5f)]
    [InlineData(3.4f, 3f)]
    [InlineData(4.2f, 3f)]
    [InlineData(-1f, 1f)]
    public void NormalizeFactor_SnapsToHalfStepsAndClamps(float raw, float expected)
    {
        Assert.Equal(expected, UiScale.NormalizeFactor(raw));
    }

    [Fact]
    public void NormalizeFactor_RejectsNaN()
    {
        Assert.Equal(UiScale.MinFactor, UiScale.NormalizeFactor(float.NaN));
    }

    [Fact]
    public void CurrentFactor_IsPlainState()
    {
        var ui = new UiScale { CurrentFactor = 2.5f };
        Assert.Equal(25, ui.ScaleSize(10f));
        Assert.Equal(3f, UiScale.NormalizeFactor(3.4f));
        Assert.Equal(2, UiScale.AutoFactor(1080));
        Assert.Equal(2.5f, ui.CurrentFactor);
    }

    [Theory]
    [InlineData(719, 1)]
    [InlineData(720, 1)]
    [InlineData(1079, 1)]
    [InlineData(1080, 2)]
    [InlineData(1439, 2)]
    [InlineData(1440, 3)]
    [InlineData(2880, 3)]
    public void AutoFactor_Boundaries(int windowHeightPx, int expected)
    {
        Assert.Equal(expected, UiScale.AutoFactor(windowHeightPx));
    }

    [Fact]
    public void ScaleSize_RoundsHalfAwayFromZero()
    {
        var ui = new UiScale { CurrentFactor = 1.5f };
        Assert.Equal(15, ui.ScaleSize(10f));

        ui.CurrentFactor = 2.5f;
        Assert.Equal(8, ui.ScaleSize(3f));
    }

    [Fact]
    public void ScaleSize_StaticTwoArg()
    {
        Assert.Equal(15, UiScale.ScaleSize(10f, 1.5f));
        Assert.Equal(8, UiScale.ScaleSize(3f, 2.5f));
        Assert.Equal(1, UiScale.ScaleSize(1f, 1f));
        Assert.Equal(1, UiScale.ScaleSize(0f, 3f));
    }

    [Fact]
    public void ScaleCoordinate_ZeroAndNegative()
    {
        Assert.Equal(0, UiScale.ScaleCoordinate(0f, 1f));
        Assert.Equal(0, UiScale.ScaleCoordinate(0f, 2f));
        Assert.Equal(-10, UiScale.ScaleCoordinate(-5f, 2f));
    }

    [Fact]
    public void ScaleSize_FloorsToCoordinate()
    {
        Assert.Equal(Math.Max(1, UiScale.ScaleCoordinate(10f, 1.5f)), UiScale.ScaleSize(10f, 1.5f));
        Assert.Equal(Math.Max(1, UiScale.ScaleCoordinate(3f, 2.5f)), UiScale.ScaleSize(3f, 2.5f));
        Assert.Equal(Math.Max(1, UiScale.ScaleCoordinate(1f, 1f)), UiScale.ScaleSize(1f, 1f));
        Assert.Equal(Math.Max(1, UiScale.ScaleCoordinate(0f, 3f)), UiScale.ScaleSize(0f, 3f));
        Assert.Equal(Math.Max(1, UiScale.ScaleCoordinate(-5f, 2f)), UiScale.ScaleSize(-5f, 2f));
    }

    [Fact]
    public void ScaleSize_MinOneGuard()
    {
        var ui = new UiScale { CurrentFactor = 1f };
        Assert.Equal(1, ui.ScaleSize(0f));
        Assert.Equal(1, ui.ScaleSize(1f));
    }

    [Fact]
    public void ScaleSizeI_PerAxis()
    {
        var ui = new UiScale { CurrentFactor = 2f };
        Assert.Equal(new Vector2I(64, 110), ui.ScaleSizeI(new Vector2I(32, 55)));
    }
}
