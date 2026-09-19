using Godot;
using Goose2Client.UI;
using Xunit;

namespace Goose2Client.Tests;

public class CustomPreviewMetricsTests
{
    private static readonly Vector2 Control = new(128f, 128f);
    private static readonly Vector2 DesignedViewport = new(144f, 166f);

    [Fact]
    public void Scale_UsesCommonScaleForAllFrames()
    {
        Assert.Equal(3.0f, CustomPreviewMetrics.Scale(Control));
        Assert.Equal(0f, CustomPreviewMetrics.Scale(Vector2.Zero));
        Assert.Equal(0f, CustomPreviewMetrics.Scale(new Vector2(128, 0)));
    }

    [Fact]
    public void Layout_AllFramesShareScale()
    {
        var (bodySize, _) = CustomPreviewMetrics.Layout(new Vector2(48, 48), Control);
        var (weaponSize, _) = CustomPreviewMetrics.Layout(new Vector2(48, 96), Control);
        Assert.Equal(144f, bodySize.X);
        Assert.Equal(144f, weaponSize.X);
        Assert.Equal(144f, weaponSize.Y / 2f);
    }

    [Fact]
    public void Layout_DesignViewport_FitsStandardFrame()
    {
        var (size, pos) = CustomPreviewMetrics.Layout(new Vector2(48, 48), DesignedViewport);
        Assert.Equal(new Vector2(144, 144), size);
        Assert.Equal(0f, pos.X);
        Assert.True(pos.Y >= 0f);
        Assert.True(pos.Y + size.Y <= DesignedViewport.Y);
    }

    [Fact]
    public void Layout_DesignViewport_TallFrameOverflowsVertically()
    {
        var (size, pos) = CustomPreviewMetrics.Layout(new Vector2(48, 96), DesignedViewport);
        Assert.Equal(new Vector2(144, 288), size);
        Assert.Equal(0f, pos.X);
        Assert.True(pos.X + size.X <= DesignedViewport.X);
        Assert.True(pos.Y < 0f);
        Assert.True(pos.Y + size.Y > DesignedViewport.Y);
    }

    [Fact]
    public void Layout_ShortFrameSitsOnGroundLine()
    {
        var (size, pos) = CustomPreviewMetrics.Layout(new Vector2(48, 48), Control);
        Assert.Equal(new Vector2(144, 144), size);
        Assert.Equal(new Vector2(-8, -32), pos);
        Assert.Equal(112f, pos.Y + size.Y);
    }

    [Fact]
    public void Layout_StandardFrameAtLockedScale()
    {
        var (size, pos) = CustomPreviewMetrics.Layout(new Vector2(48, 64), Control);
        Assert.Equal(new Vector2(144, 192), size);
        Assert.Equal(new Vector2(-8, -56), pos);
    }

    [Fact]
    public void Layout_TallFrameOverhangsBelowControl()
    {
        var (size, pos) = CustomPreviewMetrics.Layout(new Vector2(48, 96), Control);
        Assert.Equal(new Vector2(144, 288), size);
        Assert.Equal(new Vector2(-8, -104), pos);
        Assert.True(pos.Y + size.Y > Control.Y);
    }

    [Fact]
    public void Layout_ScaleLockedForNarrowControl()
    {
        var control = new Vector2(64f, 128f);
        Assert.Equal(3.0f, CustomPreviewMetrics.Scale(control));
        var (size, pos) = CustomPreviewMetrics.Layout(new Vector2(48, 48), control);
        Assert.Equal(new Vector2(144, 144), size);
        Assert.Equal(new Vector2(-40, -32), pos);
    }

    [Fact]
    public void Layout_HorizontallyCentered()
    {
        var (size, pos) = CustomPreviewMetrics.Layout(new Vector2(24, 48), Control);
        Assert.Equal(new Vector2(72, 144), size);
        Assert.Equal(new Vector2(28, -32), pos);
        Assert.Equal(64f, pos.X + size.X / 2f);
    }

    [Fact]
    public void Layout_ZeroControlIsZero()
    {
        var (size, pos) = CustomPreviewMetrics.Layout(new Vector2(48, 64), Vector2.Zero);
        Assert.Equal(Vector2.Zero, size);
        Assert.Equal(Vector2.Zero, pos);
    }
}
