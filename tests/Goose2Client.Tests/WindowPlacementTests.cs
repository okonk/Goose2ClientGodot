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
    public void CrossCanvas_MiddleBand_KeepsSavedCoordinate()
    {
        // Hotbar (520, 679) 351x36 saved at 720p: left = 520 ≥ 320 AND right = 409 ≥ 320
        // (0.25 of 1280) → middle-parked, keeps x; bottom = 5 < 180 → sticks to bottom.
        var p = WindowPlacement.Resolve(new Vector2(520, 679), new Vector2(351, 36), C720, C1080);
        Assert.Equal(new Vector2(520, 1039), p);
    }

    [Fact]
    public void CrossCanvas_MiddleBand_RoundTrips()
    {
        // 1080p save (520, 1039) → restore at 720p: x left-stick (520 < 1049),
        // y bottom offset 5 → (520, 679).
        var p = WindowPlacement.Resolve(new Vector2(520, 1039), new Vector2(351, 36), C1080, C720);
        Assert.Equal(new Vector2(520, 679), p);

        // Back to 1080p: x middle-band (520 ≥ 480, 1049 ≥ 480), y bottom-stick.
        var p2 = WindowPlacement.Resolve(p, new Vector2(351, 36), C720, C1080);
        Assert.Equal(new Vector2(520, 1039), p2);
    }

    [Fact]
    public void MiddleBand_WideWindowEquidistant_FallsThroughAndKeepsSaved()
    {
        // 351px hotbar on a 640px canvas: band threshold = 0.25*640 = 160, and the offsets
        // around the equidistant point (640 - 144.5 - 351 == 144.5) are below it, so the
        // final equidistant/keep branch fires (not the band). Wide centered window stays put.
        var c = new Vector2I(640, 360);
        Assert.Equal(new Vector2(144.5f, 10),
            WindowPlacement.Resolve(new Vector2(144.5f, 10), new Vector2(351, 36), c, c));

        // One pixel off equidistant reaches the sub-band left/right-stick branches (identity
        // on the same canvas — pinned so the branches stay reachable and keep the coordinate).
        Assert.Equal(144f, WindowPlacement.Resolve(new Vector2(144f, 10), new Vector2(351, 36), c, c).X);
        Assert.Equal(145f, WindowPlacement.Resolve(new Vector2(145f, 10), new Vector2(351, 36), c, c).X);
    }

    [Theory]
    [InlineData(320, 640, 320)] // exactly 1/4 from both edges (centered in 1280) → in the band
    [InlineData(560, 400, 560)] // left 560, right EXACTLY 1/4 of 1280 → in the band (>= is inclusive)
    public void MiddleBand_ExactQuarterBoundary_IsInBand(int x, int w, int expectedX)
    {
        // If the right offset were below the threshold, case 2 would right-stick to 1920-400-320 = 1200.
        var p = WindowPlacement.Resolve(new Vector2(x, 10), new Vector2(w, 100), C720, C1080);
        Assert.Equal(expectedX, p.X);
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

    [Theory]
    [InlineData(1920, 1080, 260, 291, 830, 394)]   // Quest @ 1080p (394.5 truncated)
    [InlineData(1280, 720, 260, 291, 510, 214)]    // Quest @ 720p (214.5 truncated)
    [InlineData(640, 360, 900, 900, 0, 0)]         // window larger than canvas → (0, 0)
    public void Center_FirstRunDialog_CenteredInCanvas(int cw, int ch, int w, int h, int ex, int ey)
    {
        Assert.Equal(new Vector2(ex, ey),
            WindowPlacement.Center(new Vector2I(cw, ch), new Vector2(w, h)));
    }

    [Fact]
    public void Clamp_WindowTallerThanCanvas_TitleBarStaysInside()
    {
        // Synthetic 300x500 window (no current window is this large) at the 640x360 min canvas.
        var p = WindowPlacement.Resolve(new Vector2(1100, 600), new Vector2(300, 500), C720, new Vector2I(640, 360));
        Assert.InRange(p.X, 0f, 640f - 300f);
        Assert.InRange(p.Y, 0f, 360f - WindowPlacement.TitleBarHeight); // y ≤ 336
    }

    [Fact]
    public void Resolve_DelegatesToResolveScaled()
    {
        // savedSize == windowSize, factors 1, marginScale 1 → identical arithmetic per float input.
        var samples = new (Vector2 Pos, Vector2 Size, Vector2I Saved, Vector2I Current)[]
        {
            (new Vector2(520, 679), new Vector2(351, 36), C720, C720),
            (new Vector2(144.5f, 10), new Vector2(351, 36), new Vector2I(640, 360), new Vector2I(640, 360)),
            (new Vector2(980.25f, 695.75f), new Vector2(300, 200), C720, C1080),
            (new Vector2(0, 0), new Vector2(300, 200), C1080, C720),
            (new Vector2(480.5f, 160.25f), new Vector2(320, 400), C720, C1080),
            (new Vector2(-5, -5), new Vector2(638, 360), C720, C720),
        };
        foreach (var s in samples)
            Assert.Equal(
                WindowPlacement.Resolve(s.Pos, s.Size, s.Saved, s.Current),
                WindowPlacement.ResolveScaled(s.Pos, s.Size, 1f, s.Saved, s.Size, 1f, s.Current));
    }

    [Fact]
    public void ResolveScaled_HotbarCommitAt2x()
    {
        // Real hotbar quad: (520, 679) 351x36 @ f1 on C720; window now (702, 72) @ f2, C720.
        // x: left 520 ≥ 320 AND right 1280 − (520+351) == 409 ≥ 320 → band → kept 520 (fits: ≤ 578).
        // y: top 679 ≥ 180, bottom 720 − (679+36) == 5 < 180 → trailing → 720 − 72 − (5×2) == 638.
        var p = WindowPlacement.ResolveScaled(new Vector2(520, 679), new Vector2(351, 36), 1f, C720,
            new Vector2(702, 72), 2f, C720);
        Assert.Equal(new Vector2(520, 638), p);
    }

    [Fact]
    public void ResolveScaled_ScaleCommitRoundTrips()
    {
        // Invariant quad (520, 679) 351x36 @ f1 C720: deriving back at the saved factor
        // returns the saved position EXACTLY at every factor in between.
        Vector2 At(float sizeW, float sizeH, float factor)
            => WindowPlacement.ResolveScaled(new Vector2(520, 679), new Vector2(351, 36), 1f, C720,
                new Vector2(sizeW, sizeH), factor, C720);

        // @1× (351, 36): x band-kept 520; y 720 − 36 − 5 == 679.
        Assert.Equal(new Vector2(520, 679), At(351, 36, 1f));
        // @1.5× (527, 54): x band-kept 520; y 720 − 54 − (5×1.5 == 7.5) == 658.5.
        Assert.Equal(new Vector2(520, 658.5f), At(527, 54, 1.5f));
        // @2× (702, 72): y 720 − 72 − (5×2) == 638.
        Assert.Equal(new Vector2(520, 638), At(702, 72, 2f));
    }

    [Fact]
    public void ResolveScaled_DragAtScale_CommitAndRoundTrips()
    {
        // 400-wide window dragged at 2×: quad ((800, 600), (400, 72), 2, C720).
        Vector2 At(float sizeW, float sizeH, float factor)
            => WindowPlacement.ResolveScaled(new Vector2(800, 600), new Vector2(400, 72), 2f, C720,
                new Vector2(sizeW, sizeH), factor, C720);

        // Commit to 1× (200, 36), ms 0.5:
        // x: left 800 ≥ 320, right 1280 − 1200 == 80 < 320 → trailing → 1280 − 200 − (80×0.5) == 1040;
        // y: top 600 ≥ 180, bottom 720 − 672 == 48 < 180 → trailing → 720 − 36 − (48×0.5) == 660.
        Assert.Equal(new Vector2(1040, 660), At(200, 36, 1f));
        // Back at 2× (400, 72), ms 1 → exactly the saved position.
        Assert.Equal(new Vector2(800, 600), At(400, 72, 2f));
    }

    [Fact]
    public void ResolveScaled_LeadingMarginScales()
    {
        // Quad ((100, 679), (351, 36), 1, C720) at (702, 72) @ 2, C720.
        // x: left 100 < 320 → not band; left < right (829) → leading → 100×2 == 200 (fits: ≤ 578).
        // y: bottom 5 < 180 → trailing → 638.
        var p = WindowPlacement.ResolveScaled(new Vector2(100, 679), new Vector2(351, 36), 1f, C720,
            new Vector2(702, 72), 2f, C720);
        Assert.Equal(new Vector2(200, 638), p);
    }

    [Fact]
    public void ResolveScaled_ClampWhenScaledWindowExceedsCanvas()
    {
        // Same quad on a 1280×60 canvas: y trailing → 60 − 72 − 10 == −22 → clamped to 0;
        // x leading 200.
        var p = WindowPlacement.ResolveScaled(new Vector2(100, 679), new Vector2(351, 36), 1f, C720,
            new Vector2(702, 72), 2f, new Vector2I(1280, 60));
        Assert.Equal(new Vector2(200, 0), p);
    }

    [Fact]
    public void ResolveScaled_TitleBarAllowance_Scaled()
    {
        // Quad ((100, 700), (100, 100), 1, C720), (100, 100) @ 1.
        // x: left 100 < 320 → leading → 100. y: bottom 720 − 800 == −80 → trailing →
        // 720 − 100 − (−80×1) == 700 → clamped by the allowance: 696 (24) / 672 (48).
        Vector2 At(int allowance)
            => WindowPlacement.ResolveScaled(new Vector2(100, 700), new Vector2(100, 100), 1f, C720,
                new Vector2(100, 100), 1f, C720, allowance);

        Assert.Equal(new Vector2(100, 696), At(24));
        Assert.Equal(new Vector2(100, 672), At(48));
    }

    [Fact]
    public void ResolveScaled_CorruptSavedFactorFallsBackTo1()
    {
        Vector2 At(float savedFactor)
            => WindowPlacement.ResolveScaled(new Vector2(100, 679), new Vector2(351, 36), savedFactor, C720,
                new Vector2(702, 72), 2f, C720);

        Assert.Equal(At(1f), At(0f));
        Assert.Equal(At(1f), At(-1f));
    }
}
