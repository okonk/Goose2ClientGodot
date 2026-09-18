using Godot;
using Goose2Client.UI;
using Xunit;

namespace Goose2Client.Tests;

public class CustomPreviewMetricsTests
{
    private static readonly Vector2 Control = new(128f, 128f);

    [Fact]
    public void Scale_UsesCommonScaleForAllFrames()
    {
        Assert.Equal(2.0f, CustomPreviewMetrics.Scale(Control));
        Assert.Equal(0f, CustomPreviewMetrics.Scale(Vector2.Zero));
        Assert.Equal(0f, CustomPreviewMetrics.Scale(new Vector2(128, 0)));
    }

    [Fact]
    public void Layout_AllFramesShareScale()
    {
        var (bodySize, _) = CustomPreviewMetrics.Layout(new Vector2(48, 48), Control);
        var (weaponSize, _) = CustomPreviewMetrics.Layout(new Vector2(48, 96), Control);
        Assert.Equal(96f, bodySize.X);
        Assert.Equal(96f, weaponSize.X);
        Assert.Equal(96f, weaponSize.Y / 2f);
    }

    [Fact]
    public void Layout_ShortFrameSitsOnGroundLine()
    {
        var (size, pos) = CustomPreviewMetrics.Layout(new Vector2(48, 48), Control);
        Assert.Equal(new Vector2(96, 96), size);
        Assert.Equal(new Vector2(16, 16), pos);
        Assert.Equal(112f, pos.Y + size.Y);
    }

    [Fact]
    public void Layout_StandardFrameAtLockedScale()
    {
        var (size, pos) = CustomPreviewMetrics.Layout(new Vector2(48, 64), Control);
        Assert.Equal(new Vector2(96, 128), size);
        Assert.Equal(new Vector2(16, 0), pos);
    }

    [Fact]
    public void Layout_TallFrameOverhangsBelowControl()
    {
        var (size, pos) = CustomPreviewMetrics.Layout(new Vector2(48, 96), Control);
        Assert.Equal(new Vector2(96, 192), size);
        Assert.Equal(new Vector2(16, -32), pos);
        Assert.True(pos.Y + size.Y > Control.Y);
    }

    [Fact]
    public void Layout_ScaleLockedForNarrowControl()
    {
        var control = new Vector2(64f, 128f);
        Assert.Equal(2.0f, CustomPreviewMetrics.Scale(control));
        var (size, pos) = CustomPreviewMetrics.Layout(new Vector2(48, 48), control);
        Assert.Equal(new Vector2(96, 96), size);
        Assert.Equal(new Vector2(-16, 16), pos);
    }

    [Fact]
    public void Layout_HorizontallyCentered()
    {
        var (size, pos) = CustomPreviewMetrics.Layout(new Vector2(24, 48), Control);
        Assert.Equal(new Vector2(48, 96), size);
        Assert.Equal(new Vector2(40, 16), pos);
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
