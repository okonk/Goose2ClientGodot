using System;
using Godot;
using Goose2Client;
using Xunit;

public class WorldViewportScaleTests
{
    // ── Compute: Integer2x mode ──

    [Theory]
    [InlineData(1920, 1080, 2, 960, 540, 0, 0, 1920, 1080)]
    [InlineData(2560, 1440, 2, 1280, 720, 0, 0, 2560, 1440)]
    [InlineData(3840, 2160, 3, 1280, 720, 0, 0, 3840, 2160)]
    [InlineData(1280, 720, 2, 640, 360, 0, 0, 1280, 720)]
    [InlineData(1600, 900, 2, 800, 450, 0, 0, 1600, 900)]
    [InlineData(3440, 1440, 3, 1146, 480, 1, 0, 3438, 1440)]
    [InlineData(1921, 1081, 2, 960, 540, 0, 0, 1920, 1080)]
    [InlineData(3050, 305, 3, 1016, 101, 1, 1, 3048, 303)]
    public void Compute_Integer2x_usesExpectedFourTuple(
        int ww, int wh, int scale, int subX, int subY, int originX, int originY, int dispX, int dispY)
    {
        var layout = WorldViewportScale.Compute(WorldRenderMode.Integer2x, new Vector2I(ww, wh));
        Assert.Equal(new WorldViewportLayout(scale, new Vector2I(subX, subY), new Vector2I(originX, originY), new Vector2I(dispX, dispY)), layout);
    }

    [Fact]
    public void Compute_Native1x_usesFullWindow()
    {
        var layout = WorldViewportScale.Compute(WorldRenderMode.Native1x, new Vector2I(1920, 1080));
        Assert.Equal(new WorldViewportLayout(1, new Vector2I(1920, 1080), new Vector2I(0, 0), new Vector2I(1920, 1080)), layout);
    }

    [Theory]
    [InlineData(1, 720)]
    [InlineData(1280, 1)]
    [InlineData(0, 720)]
    public void Compute_WindowSmallerThanTwoPixelsOnAnyAxis_throws(int ww, int wh)
    {
        var window = new Vector2I(ww, wh);
        Assert.Throws<ArgumentOutOfRangeException>(() => WorldViewportScale.Compute(WorldRenderMode.Integer2x, window));
        Assert.Throws<ArgumentOutOfRangeException>(() => WorldViewportScale.Compute(WorldRenderMode.Native1x, window));
    }

    // ── IsInsideDisplay: gutter rejection on all four edges ──

    [Theory]
    [InlineData(0, 720)]   // left gutter
    [InlineData(3439, 720)] // right gutter
    public void IsInsideDisplay_WideLayout_rejectsLeftAndRightGutters(int x, int y)
    {
        var layout = new WorldViewportLayout(3, new Vector2I(1146, 480), new Vector2I(1, 0), new Vector2I(3438, 1440));
        Assert.False(WorldViewportScale.IsInsideDisplay(layout, new Vector2I(x, y)));
    }

    [Theory]
    [InlineData(1, 720)]
    [InlineData(3438, 720)]
    public void IsInsideDisplay_WideLayout_acceptsEdgePixels(int x, int y)
    {
        var layout = new WorldViewportLayout(3, new Vector2I(1146, 480), new Vector2I(1, 0), new Vector2I(3438, 1440));
        Assert.True(WorldViewportScale.IsInsideDisplay(layout, new Vector2I(x, y)));
    }

    [Theory]
    [InlineData(1920, 540)] // right gutter
    [InlineData(960, 1080)] // bottom gutter
    public void IsInsideDisplay_ExactFitLayout_rejectsRightAndBottomGutters(int x, int y)
    {
        var layout = new WorldViewportLayout(2, new Vector2I(960, 540), new Vector2I(0, 0), new Vector2I(1920, 1080));
        Assert.False(WorldViewportScale.IsInsideDisplay(layout, new Vector2I(x, y)));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1919, 1079)]
    public void IsInsideDisplay_ExactFitLayout_acceptsEdgePixels(int x, int y)
    {
        var layout = new WorldViewportLayout(2, new Vector2I(960, 540), new Vector2I(0, 0), new Vector2I(1920, 1080));
        Assert.True(WorldViewportScale.IsInsideDisplay(layout, new Vector2I(x, y)));
    }

    [Fact]
    public void IsInsideDisplay_TopGutter()
    {
        var layout = WorldViewportScale.Compute(WorldRenderMode.Integer2x, new Vector2I(800, 2163));
        Assert.Equal(new WorldViewportLayout(4, new Vector2I(200, 540), new Vector2I(0, 1), new Vector2I(800, 2160)), layout);
        Assert.False(WorldViewportScale.IsInsideDisplay(layout, new Vector2I(400, 0)));  // top gutter
        Assert.True(WorldViewportScale.IsInsideDisplay(layout, new Vector2I(400, 1)));
    }

    // ── CameraParityOffset ──

    [Theory]
    [InlineData(1280, 720, 0.0f, 0.0f)]
    [InlineData(639, 701, 0.5f, 0.5f)]
    [InlineData(639, 720, 0.5f, 0.0f)]
    [InlineData(1280, 701, 0.0f, 0.5f)]
    [InlineData(1281, 721, 0.5f, 0.5f)]
    public void CameraParityOffset_isHalfPixelOnOddAxesOnly(int x, int y, float offX, float offY)
    {
        Assert.Equal(new Vector2(offX, offY), WorldViewportScale.CameraParityOffset(new Vector2I(x, y)));
    }

    [Fact]
    public void CameraParityOffset_reportedMaximizedWindow_639x701_getsHalfPixelBothAxes()
    {
        var layout = WorldViewportScale.Compute(WorldRenderMode.Integer2x, new Vector2I(1278, 1402));
        Assert.Equal(new Vector2I(639, 701), layout.SubViewportSize);
        Assert.Equal(new Vector2(0.5f, 0.5f), WorldViewportScale.CameraParityOffset(layout.SubViewportSize));
    }

    // ── Property tests over a range of window sizes ──

    [Theory]
    [InlineData(WorldRenderMode.Integer2x)]
    [InlineData(WorldRenderMode.Native1x)]
    public void Compute_Property_invariantsHoldAcrossAllWindowSizes(WorldRenderMode mode)
    {
        for (int w = 320; w <= 5120; w += 7)
        {
            for (int h = 200; h <= 3200; h += 7)
            {
                var layout = WorldViewportScale.Compute(mode, new Vector2I(w, h));

                // I1 + I3: uniform integer display scale, sub-viewport at least 1x1,
                // remainder less than the scale on each axis.
                Assert.True(layout.SubViewportSize.X >= 1 && layout.SubViewportSize.Y >= 1,
                    $"{mode} {w}x{h}: SubViewportSize {layout.SubViewportSize} < (1,1)");
                Assert.True(layout.DisplaySize.X == layout.SubViewportSize.X * layout.Scale,
                    $"{mode} {w}x{h}: DisplaySize.X {layout.DisplaySize.X} != SubViewportSize.X {layout.SubViewportSize.X} * Scale {layout.Scale}");
                Assert.True(layout.DisplaySize.Y == layout.SubViewportSize.Y * layout.Scale,
                    $"{mode} {w}x{h}: DisplaySize.Y {layout.DisplaySize.Y} != SubViewportSize.Y {layout.SubViewportSize.Y} * Scale {layout.Scale}");
                Assert.True(layout.DisplaySize.X <= w && w - layout.DisplaySize.X < layout.Scale,
                    $"{mode} {w}x{h}: X remainder {w - layout.DisplaySize.X} not in [0, {layout.Scale})");
                Assert.True(layout.DisplaySize.Y <= h && h - layout.DisplaySize.Y < layout.Scale,
                    $"{mode} {w}x{h}: Y remainder {h - layout.DisplaySize.Y} not in [0, {layout.Scale})");

                // I2: Integer2x mode must be at least 2x and within the cap.
                if (mode == WorldRenderMode.Integer2x)
                {
                    Assert.True(layout.Scale >= 2, $"{mode} {w}x{h}: Scale {layout.Scale} < 2");
                    Assert.True(layout.SubViewportSize.X <= WorldViewportScale.Cap.X,
                        $"{mode} {w}x{h}: SubViewportSize.X {layout.SubViewportSize.X} > cap {WorldViewportScale.Cap.X}");
                    Assert.True(layout.SubViewportSize.Y <= WorldViewportScale.Cap.Y,
                        $"{mode} {w}x{h}: SubViewportSize.Y {layout.SubViewportSize.Y} > cap {WorldViewportScale.Cap.Y}");
                }
            }
        }
    }
}
