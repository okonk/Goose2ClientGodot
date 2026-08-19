using Godot;
using Goose2Client.UI;
using Xunit;

namespace Goose2Client.Tests;

public class WindowPlacementTests
{
    private static readonly Vector2I C720 = new(1280, 720);
    private static readonly Vector2I C1080 = new(1920, 1080);

    // 720p identity: savedCanvas == currentCanvas → exactly today's behavior (saved
    // position clamped into the canvas / title bar).
    [Theory]
    [InlineData(10, 10, 300, 200)]
    [InlineData(640, 360, 300, 200)]
    [InlineData(1100, 600, 300, 200)]
    [InlineData(0, 0, 300, 200)]
    [InlineData(980, 696, 300, 200)] // rightmost/bottommost valid position
    [InlineData(1200, 700, 300, 200)] // out of bounds → clamped to (980, 696)
    [InlineData(-5, -5, 300, 200)] // out of bounds → clamped to (0, 0)
    [InlineData(520, 679, 36, 36)] // Hotbar default
    [InlineData(900, 360, 340, 420)] // Inventory default (right-anchored)
    [InlineData(0, 0, 638, 360)] // BuffEffectsWindow, the largest real window
    public void Identity_SameCanvas_ReturnsClampedSavedPosition(int px, int py, int w, int h)
    {
        var expected = new Vector2(
            Mathf.Clamp(px, 0f, 1280f - w),
            Mathf.Clamp(py, 0f, 720f - WindowPlacement.TitleBarHeight));

        Assert.Equal(expected, WindowPlacement.Resolve(new Vector2(px, py), new Vector2(w, h), C720, C720));
    }

    [Fact]
    public void CrossCanvas_HotbarBottomEdge_SticksToBottom()
    {
        // Hotbar saved at 720p with a 5px bottom offset (679 + 36 = 715) must restore
        // at 1080p with the same 5px bottom offset (1039 + 36 = 1075).
        var p = WindowPlacement.Resolve(new Vector2(520, 679), new Vector2(36, 36), C720, C1080);
        Assert.Equal(new Vector2(520, 1039), p);

        // That 1080p save round-trips exactly at 1080p (identity — not 1044).
        var p2 = WindowPlacement.Resolve(p, new Vector2(36, 36), C1080, C1080);
        Assert.Equal(new Vector2(520, 1039), p2);

        // And back to 720p → the original position.
        var p3 = WindowPlacement.Resolve(p2, new Vector2(36, 36), C1080, C720);
        Assert.Equal(new Vector2(520, 679), p3);
    }

    [Fact]
    public void CrossCanvas_RightEdge_SticksToRight()
    {
        // (900, 360) with w=340 → right offset in 1280 canvas = 1280 - (900+340) = 40.
        var p = WindowPlacement.Resolve(new Vector2(900, 360), new Vector2(340, 420), C720, C1080);
        Assert.Equal(1920f - 340f - 40f, p.X); // 1540
    }

    [Fact]
    public void CrossCanvas_MidScreen_KeepsSavedCoordinate()
    {
        // w=320 in 1280 → left = right = 480 (equidistant); h=400 in 720 → 160/160.
        // Must NOT jump to an edge at 1080p.
        var p = WindowPlacement.Resolve(new Vector2(480, 160), new Vector2(320, 400), C720, C1080);
        Assert.Equal(new Vector2(480, 160), p);
    }

    [Fact]
    public void Clamp_WindowTallerThanCanvas_TitleBarStaysInside()
    {
        // Synthetic 300x500 window (no current window is this large) at the 640x360 min canvas.
        var p = WindowPlacement.Resolve(new Vector2(1100, 600), new Vector2(300, 500), C720, new Vector2I(640, 360));
        Assert.InRange(p.X, 0f, 640f - 300f);
        Assert.InRange(p.Y, 0f, 360f - WindowPlacement.TitleBarHeight); // y ≤ 336
    }
}
