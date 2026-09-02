using System;
using MapEditor.Rendering;
using Xunit;

namespace MapEditor.Rendering.Tests;

public class ViewportCullingTests
{
    [Fact]
    public void Compute_Exact32PixelViewportContainsOneCellAtEveryZoom()
    {
        (MapZoom Zoom, double Width, double Height)[] viewports =
        {
            (MapZoom.Percent25, 8.0, 8.0),
            (MapZoom.Percent50, 16.0, 16.0),
            (MapZoom.Percent100, 32.0, 32.0),
            (MapZoom.Percent200, 64.0, 64.0),
            (MapZoom.Percent400, 128.0, 128.0)
        };

        foreach ((MapZoom zoom, double width, double height) in viewports)
        {
            ViewportTransform viewport = new(new RenderSize(width, height), new RenderPoint(96.0, 64.0), zoom);
            ViewportTileRanges ranges = ViewportCulling.Compute(viewport, 10, 10, 32, 32);
            Assert.Equal(new TileRange(3, 2, 3, 2), ranges.VisibleCells);
            Assert.Equal(new TileRange(3, 2, 3, 2), ranges.SpriteCandidates);
        }
    }

    [Fact]
    public void Compute_PartialEdgesIncludesIntersectingCellsUsingHalfOpenBounds()
    {
        ViewportTransform viewport = new(
            new RenderSize(64.0, 36.5),
            new RenderPoint(10.5, 30.25),
            MapZoom.Percent100);

        ViewportTileRanges ranges = ViewportCulling.Compute(viewport, 10, 10, 32, 32);

        Assert.Equal(new TileRange(0, 0, 2, 2), ranges.VisibleCells);
        Assert.Equal(new TileRange(0, 0, 2, 2), ranges.SpriteCandidates);
    }

    [Fact]
    public void Compute_ExactBoundaryDoesNotIncludeNextCell()
    {
        ViewportTransform left = new(new RenderSize(32.0, 32.0), new RenderPoint(64.0, 0.0), MapZoom.Percent100);
        Assert.Equal(new TileRange(2, 0, 2, 0), ViewportCulling.Compute(left, 10, 10, 32, 32).VisibleCells);

        ViewportTransform right = new(new RenderSize(32.0, 32.0), new RenderPoint(96.0, 0.0), MapZoom.Percent100);
        Assert.Equal(new TileRange(3, 0, 3, 0), ViewportCulling.Compute(right, 10, 10, 32, 32).VisibleCells);

        ViewportTransform bottom = new(new RenderSize(32.0, 32.0), new RenderPoint(0.0, 32.0), MapZoom.Percent100);
        Assert.Equal(new TileRange(0, 1, 0, 1), ViewportCulling.Compute(bottom, 10, 10, 32, 32).VisibleCells);
    }

    [Fact]
    public void Compute_ClampsNegativeOriginAndFarMapEdges()
    {
        ViewportTransform negative = new(
            new RenderSize(100.0, 100.0),
            new RenderPoint(-50.0, -60.0),
            MapZoom.Percent100);
        ViewportTileRanges near = ViewportCulling.Compute(negative, 10, 10, 32, 32);
        Assert.Equal(new TileRange(0, 0, 1, 1), near.VisibleCells);

        ViewportTransform far = new(
            new RenderSize(100.0, 100.0),
            new RenderPoint(250.0, 250.0),
            MapZoom.Percent100);
        ViewportTileRanges clipped = ViewportCulling.Compute(far, 10, 10, 32, 32);
        Assert.Equal(new TileRange(7, 7, 9, 9), clipped.VisibleCells);
    }

    [Fact]
    public void Compute_ViewportOutsideMapHasNoCellsAndOnlyOverlappingEdgeCandidates()
    {
        (string Side, double X, double Y)[] cases =
        {
            ("right-near", 320.0, 0.0),
            ("right-far", 384.0, 0.0),
            ("left-near", -32.0, 0.0),
            ("left-far", -96.0, 0.0),
            ("top-near", 0.0, -32.0),
            ("top-far", 0.0, -96.0),
            ("bottom-near", 0.0, 320.0),
            ("bottom-far", 0.0, 384.0)
        };

        (TileRange Visible, TileRange Candidates)[] expected =
        {
            (TileRange.Empty, new TileRange(9, 0, 9, 2)),
            (TileRange.Empty, TileRange.Empty),
            (TileRange.Empty, new TileRange(0, 0, 0, 2)),
            (TileRange.Empty, TileRange.Empty),
            (TileRange.Empty, new TileRange(0, 0, 1, 1)),
            (TileRange.Empty, TileRange.Empty),
            (TileRange.Empty, TileRange.Empty),
            (TileRange.Empty, TileRange.Empty)
        };

        for (int i = 0; i < cases.Length; i++)
        {
            (string side, double x, double y) = cases[i];
            ViewportTransform viewport = new(new RenderSize(32.0, 32.0), new RenderPoint(x, y), MapZoom.Percent100);
            ViewportTileRanges ranges = ViewportCulling.Compute(viewport, 10, 10, 96, 96);
            Assert.True(ranges.VisibleCells.IsEmpty, $"{side}: visible cells should be empty");
            Assert.Equal(expected[i].Visible, ranges.VisibleCells);
            Assert.Equal(expected[i].Candidates, ranges.SpriteCandidates);
        }
    }

    [Fact]
    public void Compute_OneByOneAnd1000By1000MapsStayWithinBounds()
    {
        ViewportTransform oneByOne = new(new RenderSize(32.0, 32.0), new RenderPoint(0.0, 0.0), MapZoom.Percent100);
        ViewportTileRanges small = ViewportCulling.Compute(oneByOne, 1, 1, 96, 96);
        Assert.Equal(new TileRange(0, 0, 0, 0), small.VisibleCells);
        Assert.Equal(new TileRange(0, 0, 0, 0), small.SpriteCandidates);

        ViewportTransform outsideOneByOne = new(
            new RenderSize(32.0, 32.0),
            new RenderPoint(100.0, 100.0),
            MapZoom.Percent100);
        ViewportTileRanges outside = ViewportCulling.Compute(outsideOneByOne, 1, 1, 32, 32);
        Assert.True(outside.VisibleCells.IsEmpty);
        Assert.True(outside.SpriteCandidates.IsEmpty);

        ViewportTransform big = new(new RenderSize(8000.0, 8000.0), new RenderPoint(0.0, 0.0), MapZoom.Percent25);
        ViewportTileRanges large = ViewportCulling.Compute(big, 1000, 1000, 32, 32);
        Assert.Equal(new TileRange(0, 0, 999, 999), large.VisibleCells);
        Assert.Equal(new TileRange(0, 0, 999, 999), large.SpriteCandidates);

        ViewportTransform bigCorner = new(
            new RenderSize(64.0, 64.0),
            new RenderPoint(31968.0, 31968.0),
            MapZoom.Percent100);
        ViewportTileRanges corner = ViewportCulling.Compute(bigCorner, 1000, 1000, 32, 32);
        Assert.Equal(new TileRange(999, 999, 999, 999), corner.VisibleCells);
    }

    [Fact]
    public void Compute_WideFrameAddsSymmetricCandidateColumns()
    {
        int[] widths = { 32, 33, 96 };
        TileRange[] expected =
        {
            new TileRange(4, 4, 4, 4),
            new TileRange(3, 4, 5, 4),
            new TileRange(3, 4, 5, 4)
        };

        for (int i = 0; i < widths.Length; i++)
        {
            ViewportTransform viewport = new(
                new RenderSize(32.0, 32.0),
                new RenderPoint(128.0, 128.0),
                MapZoom.Percent100);
            ViewportTileRanges ranges = ViewportCulling.Compute(viewport, 10, 10, widths[i], 32);
            Assert.Equal(new TileRange(4, 4, 4, 4), ranges.VisibleCells);
            Assert.Equal(expected[i], ranges.SpriteCandidates);
        }
    }

    [Fact]
    public void Compute_TallFrameAddsOnlyCandidateRowsBelow()
    {
        int[] heights = { 32, 33, 96 };
        TileRange[] expected =
        {
            new TileRange(4, 4, 4, 4),
            new TileRange(4, 4, 4, 5),
            new TileRange(4, 4, 4, 6)
        };

        for (int i = 0; i < heights.Length; i++)
        {
            ViewportTransform viewport = new(
                new RenderSize(32.0, 32.0),
                new RenderPoint(128.0, 128.0),
                MapZoom.Percent100);
            ViewportTileRanges ranges = ViewportCulling.Compute(viewport, 10, 10, 32, heights[i]);
            Assert.Equal(new TileRange(4, 4, 4, 4), ranges.VisibleCells);
            Assert.Equal(expected[i], ranges.SpriteCandidates);
        }
    }

    [Fact]
    public void Compute_ZoomChangesWorldExtentButNotRules()
    {
        (MapZoom Zoom, double Width, double Height)[] viewports =
        {
            (MapZoom.Percent25, 16.0, 16.0),
            (MapZoom.Percent50, 32.0, 32.0),
            (MapZoom.Percent100, 64.0, 64.0),
            (MapZoom.Percent200, 128.0, 128.0),
            (MapZoom.Percent400, 256.0, 256.0)
        };

        foreach ((MapZoom zoom, double width, double height) in viewports)
        {
            ViewportTransform viewport = new(new RenderSize(width, height), new RenderPoint(32.0, 0.0), zoom);
            ViewportTileRanges ranges = ViewportCulling.Compute(viewport, 10, 10, 32, 32);
            Assert.Equal(new TileRange(1, 0, 2, 1), ranges.VisibleCells);
            Assert.Equal(new TileRange(1, 0, 2, 1), ranges.SpriteCandidates);
        }
    }

    [Fact]
    public void Compute_Int32MaxSpriteExtentsClampsWithConstantTimeRangeMath()
    {
        ViewportTransform viewport = new(
            new RenderSize(32.0, 32.0),
            new RenderPoint(0.0, 0.0),
            MapZoom.Percent100);

        ViewportTileRanges ranges = ViewportCulling.Compute(viewport, 10, 10, int.MaxValue, int.MaxValue);

        Assert.Equal(new TileRange(0, 0, 0, 0), ranges.VisibleCells);
        Assert.Equal(new TileRange(0, 0, 9, 9), ranges.SpriteCandidates);
    }

    [Fact]
    public void Compute_RejectsInvalidDimensionsOrSpriteExtents()
    {
        ViewportTransform viewport = new(
            new RenderSize(32.0, 32.0),
            new RenderPoint(0.0, 0.0),
            MapZoom.Percent100);

        int[] badMapDimensions = { -1, 0, 1001 };
        foreach (int width in badMapDimensions)
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => ViewportCulling.Compute(viewport, width, 10, 32, 32));
        }

        foreach (int height in badMapDimensions)
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => ViewportCulling.Compute(viewport, 10, height, 32, 32));
        }

        int[] badSpriteExtents = { -1, 0 };
        foreach (int extent in badSpriteExtents)
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => ViewportCulling.Compute(viewport, 10, 10, extent, 32));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => ViewportCulling.Compute(viewport, 10, 10, 32, extent));
        }
    }

    [Fact]
    public void TileRange_EmptyHasZeroSizeAndContainsNothing()
    {
        TileRange empty = TileRange.Empty;
        Assert.True(empty.IsEmpty);
        Assert.Equal(0, empty.Width);
        Assert.Equal(0, empty.Height);
        Assert.False(empty.Contains(0, 0));
        Assert.False(empty.Contains(-5, -5));
        Assert.False(empty.Contains(999, 999));

        TileRange range = new(0, 0, 2, 2);
        Assert.False(range.IsEmpty);
        Assert.Equal(3, range.Width);
        Assert.Equal(3, range.Height);
        Assert.True(range.Contains(0, 0));
        Assert.True(range.Contains(2, 2));
        Assert.False(range.Contains(3, 2));
        Assert.False(range.Contains(0, 3));
        Assert.False(range.Contains(-1, 1));
    }
}
