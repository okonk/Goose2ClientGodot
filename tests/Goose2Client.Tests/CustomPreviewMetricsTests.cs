using Godot;
using Goose2Client.UI;
using Xunit;

namespace Goose2Client.Tests;

public class CustomPreviewMetricsTests
{
    private static readonly Vector2 Control = new(100f, 100f);

    [Fact]
    public void Layout_SquareFillsControl()
    {
        var (size, pos) = CustomPreviewMetrics.Layout(new Vector2(50, 50), Control);
        Assert.Equal(new Vector2(100, 100), size);
        Assert.Equal(new Vector2(0, 0), pos);
    }

    [Fact]
    public void Layout_WideFitsWidthAnchoredBottom()
    {
        var (size, pos) = CustomPreviewMetrics.Layout(new Vector2(100, 25), Control);
        Assert.Equal(new Vector2(100, 25), size);
        Assert.Equal(new Vector2(0, 75), pos);
    }

    [Fact]
    public void Layout_TallFitsHeightCenteredHorizontally()
    {
        var (size, pos) = CustomPreviewMetrics.Layout(new Vector2(25, 100), Control);
        Assert.Equal(new Vector2(25, 100), size);
        Assert.Equal(new Vector2(37.5f, 0f), pos);
    }

    [Fact]
    public void Layout_SmallScalesUpToFill()
    {
        var (size, pos) = CustomPreviewMetrics.Layout(new Vector2(10, 20), Control);
        Assert.Equal(new Vector2(50, 100), size);
        Assert.Equal(new Vector2(25, 0), pos);
    }

    [Fact]
    public void Layout_LargerThanControlScalesDown()
    {
        var (size, pos) = CustomPreviewMetrics.Layout(new Vector2(200, 400), Control);
        Assert.Equal(new Vector2(50, 100), size);
        Assert.Equal(new Vector2(25, 0), pos);
    }

    [Fact]
    public void Layout_NonSquareControl()
    {
        var (size, pos) = CustomPreviewMetrics.Layout(new Vector2(64, 32), new Vector2(96, 48));
        Assert.Equal(new Vector2(96, 48), size);
        Assert.Equal(new Vector2(0, 0), pos);

        (size, pos) = CustomPreviewMetrics.Layout(new Vector2(32, 64), new Vector2(96, 48));
        Assert.Equal(new Vector2(24, 48), size);
        Assert.Equal(new Vector2(36, 0), pos);
    }

    [Fact]
    public void Layout_ZeroTextureIsZeroSizeBottomCenter()
    {
        var (size, pos) = CustomPreviewMetrics.Layout(Vector2.Zero, Control);
        Assert.Equal(Vector2.Zero, size);
        Assert.Equal(new Vector2(50, 100), pos);
    }
}
