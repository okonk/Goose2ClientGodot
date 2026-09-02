using System;
using MapEditor.Rendering;
using Xunit;

namespace MapEditor.Rendering.Tests;

public class ViewportTransformTests
{
    private static readonly MapZoom[] Zooms =
    {
        MapZoom.Percent25,
        MapZoom.Percent50,
        MapZoom.Percent100,
        MapZoom.Percent200,
        MapZoom.Percent400
    };

    [Fact]
    public void GetScale_ReturnsExactlyPoint25Point5OneTwoFour()
    {
        Assert.Equal(0.25, MapZoomLevels.GetScale(MapZoom.Percent25));
        Assert.Equal(0.5, MapZoomLevels.GetScale(MapZoom.Percent50));
        Assert.Equal(1.0, MapZoomLevels.GetScale(MapZoom.Percent100));
        Assert.Equal(2.0, MapZoomLevels.GetScale(MapZoom.Percent200));
        Assert.Equal(4.0, MapZoomLevels.GetScale(MapZoom.Percent400));
    }

    [Fact]
    public void ZoomInAndOut_UseOnlyOrderedLevelsAndClampAtEnds()
    {
        MapZoom[] forward =
        {
            MapZoomLevels.ZoomIn(MapZoom.Percent25),
            MapZoomLevels.ZoomIn(MapZoom.Percent50),
            MapZoomLevels.ZoomIn(MapZoom.Percent100),
            MapZoomLevels.ZoomIn(MapZoom.Percent200),
            MapZoomLevels.ZoomIn(MapZoom.Percent400)
        };

        Assert.Equal(
            new[]
            {
                MapZoom.Percent50,
                MapZoom.Percent100,
                MapZoom.Percent200,
                MapZoom.Percent400,
                MapZoom.Percent400
            },
            forward);

        MapZoom[] backward =
        {
            MapZoomLevels.ZoomOut(MapZoom.Percent400),
            MapZoomLevels.ZoomOut(MapZoom.Percent200),
            MapZoomLevels.ZoomOut(MapZoom.Percent100),
            MapZoomLevels.ZoomOut(MapZoom.Percent50),
            MapZoomLevels.ZoomOut(MapZoom.Percent25)
        };

        Assert.Equal(
            new[]
            {
                MapZoom.Percent200,
                MapZoom.Percent100,
                MapZoom.Percent50,
                MapZoom.Percent25,
                MapZoom.Percent25
            },
            backward);

        Assert.Throws<ArgumentOutOfRangeException>(() => MapZoomLevels.GetScale((MapZoom)1));
        Assert.Throws<ArgumentOutOfRangeException>(() => MapZoomLevels.ZoomIn((MapZoom)0));
        Assert.Throws<ArgumentOutOfRangeException>(() => MapZoomLevels.ZoomOut((MapZoom)77));
    }

    [Fact]
    public void WorldAndScreen_RoundTripAtEveryZoom()
    {
        RenderPoint[] origins =
        {
            new(0.0, 0.0),
            new(10.5, -7.25),
            new(-33.5, 128.125),
            new(1234.567, -89.01)
        };

        RenderPoint[] points =
        {
            new(0.5, 0.5),
            new(31.75, 33.25),
            new(-10.5, 200.25),
            new(512.75, -64.5)
        };

        foreach (MapZoom zoom in Zooms)
        {
            foreach (RenderPoint origin in origins)
            {
                ViewportTransform viewport = new(new RenderSize(1024.0, 768.0), origin, zoom);
                foreach (RenderPoint point in points)
                {
                    RenderPoint screen = viewport.WorldToScreen(point);
                    RenderPoint roundTrip = viewport.ScreenToWorld(screen);
                    Assert.True(
                        Math.Abs(roundTrip.X - point.X) < 1e-9,
                        $"x {roundTrip.X} != {point.X} at zoom {zoom}, origin {origin}");
                    Assert.True(
                        Math.Abs(roundTrip.Y - point.Y) < 1e-9,
                        $"y {roundTrip.Y} != {point.Y} at zoom {zoom}, origin {origin}");

                    MapTileCoordinate tile = viewport.ScreenToTile(screen);
                    Assert.Equal((int)Math.Floor(point.X / 32.0), tile.X);
                    Assert.Equal((int)Math.Floor(point.Y / 32.0), tile.Y);
                }
            }
        }
    }

    [Fact]
    public void ScreenToTile_FloorsNegativeWorldCoordinates()
    {
        ViewportTransform viewport = new(
            new RenderSize(100.0, 100.0),
            new RenderPoint(0.0, 0.0),
            MapZoom.Percent100);

        Assert.Equal(new MapTileCoordinate(-1, -1), viewport.ScreenToTile(new RenderPoint(-0.5, -0.5)));
        Assert.Equal(new MapTileCoordinate(-1, -2), viewport.ScreenToTile(new RenderPoint(-31.9, -64.0)));
        Assert.Equal(new MapTileCoordinate(0, -1), viewport.ScreenToTile(new RenderPoint(0.0, -32.0)));
        Assert.Equal(new MapTileCoordinate(-2, 0), viewport.ScreenToTile(new RenderPoint(-64.0, 0.0)));
    }

    [Fact]
    public void PanByScreenDelta_UsesInverseScaleAndExpectedDirection()
    {
        (MapZoom Zoom, double ExpectedX, double ExpectedY)[] expected =
        {
            (MapZoom.Percent25, -156.0, -178.0),
            (MapZoom.Percent50, -28.0, -114.0),
            (MapZoom.Percent100, 36.0, -82.0),
            (MapZoom.Percent200, 68.0, -66.0),
            (MapZoom.Percent400, 84.0, -58.0)
        };

        foreach ((MapZoom zoom, double expectedX, double expectedY) in expected)
        {
            ViewportTransform viewport = new(
                new RenderSize(800.0, 600.0),
                new RenderPoint(100.0, -50.0),
                zoom);
            ViewportTransform panned = viewport.PanByScreenDelta(new RenderPoint(64.0, 32.0));
            Assert.Equal(zoom, panned.Zoom);
            Assert.Equal(expectedX, panned.WorldOrigin.X, 9);
            Assert.Equal(expectedY, panned.WorldOrigin.Y, 9);
        }
    }

    [Fact]
    public void ZoomAt_PreservesWorldPointUnderCursorAtEveryAdjacentPair()
    {
        (MapZoom From, MapZoom To)[] pairs =
        {
            (MapZoom.Percent25, MapZoom.Percent50),
            (MapZoom.Percent50, MapZoom.Percent100),
            (MapZoom.Percent100, MapZoom.Percent200),
            (MapZoom.Percent200, MapZoom.Percent400),
            (MapZoom.Percent50, MapZoom.Percent25),
            (MapZoom.Percent100, MapZoom.Percent50),
            (MapZoom.Percent200, MapZoom.Percent100),
            (MapZoom.Percent400, MapZoom.Percent200)
        };

        RenderPoint[] anchors =
        {
            new(512.0, 384.0),
            new(37.5, 12.75)
        };

        foreach ((MapZoom from, MapZoom to) in pairs)
        {
            foreach (RenderPoint anchor in anchors)
            {
                ViewportTransform viewport = new(
                    new RenderSize(1024.0, 768.0),
                    new RenderPoint(1000.0, -200.0),
                    from);
                RenderPoint worldUnderCursor = viewport.ScreenToWorld(anchor);
                ViewportTransform zoomed = viewport.ZoomAt(to, anchor);
                Assert.Equal(to, zoomed.Zoom);
                RenderPoint worldAfter = zoomed.ScreenToWorld(anchor);
                Assert.True(
                    Math.Abs(worldAfter.X - worldUnderCursor.X) < 1e-9,
                    $"x {worldAfter.X} != {worldUnderCursor.X} for {from}->{to} at {anchor}");
                Assert.True(
                    Math.Abs(worldAfter.Y - worldUnderCursor.Y) < 1e-9,
                    $"y {worldAfter.Y} != {worldUnderCursor.Y} for {from}->{to} at {anchor}");
            }
        }
    }

    [Fact]
    public void WorldRectToScreen_ScalesOriginAndBothDimensions()
    {
        ViewportTransform viewport = new(
            new RenderSize(1024.0, 768.0),
            new RenderPoint(10.0, -20.0),
            MapZoom.Percent200);

        RenderRect screen = viewport.WorldToScreen(new RenderRect(5.0, 15.0, 32.0, 64.0));

        Assert.Equal(-10.0, screen.X, 9);
        Assert.Equal(70.0, screen.Y, 9);
        Assert.Equal(64.0, screen.Width, 9);
        Assert.Equal(128.0, screen.Height, 9);
    }

    [Fact]
    public void ConstructorAndMethods_RejectNonFiniteOrNonPositiveGeometryAndUndefinedZoom()
    {
        RenderSize validSize = new(800.0, 600.0);
        RenderPoint validOrigin = new(10.0, -5.0);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ViewportTransform(new RenderSize(double.NaN, 600.0), validOrigin, MapZoom.Percent100));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ViewportTransform(new RenderSize(double.PositiveInfinity, 600.0), validOrigin, MapZoom.Percent100));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ViewportTransform(new RenderSize(800.0, 0.0), validOrigin, MapZoom.Percent100));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ViewportTransform(new RenderSize(800.0, -1.0), validOrigin, MapZoom.Percent100));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ViewportTransform(validSize, new RenderPoint(double.NaN, 0.0), MapZoom.Percent100));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ViewportTransform(validSize, new RenderPoint(0.0, double.NegativeInfinity), MapZoom.Percent100));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ViewportTransform(validSize, validOrigin, (MapZoom)0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ViewportTransform(validSize, validOrigin, (MapZoom)1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ViewportTransform(validSize, validOrigin, (MapZoom)77));

        ViewportTransform viewport = new(validSize, validOrigin, MapZoom.Percent100);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => viewport.WorldToScreen(new RenderPoint(double.NaN, 0.0)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => viewport.ScreenToWorld(new RenderPoint(0.0, double.PositiveInfinity)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => viewport.ScreenToTile(new RenderPoint(double.NaN, double.NaN)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => viewport.WorldToScreen(new RenderRect(0.0, 0.0, double.NaN, 32.0)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => viewport.WorldToScreen(new RenderRect(0.0, 0.0, 32.0, -1.0)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => viewport.WithWorldOrigin(new RenderPoint(0.0, double.NaN)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => viewport.PanByScreenDelta(new RenderPoint(double.NaN, 0.0)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => viewport.ZoomAt((MapZoom)5, new RenderPoint(100.0, 100.0)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => viewport.ZoomAt(MapZoom.Percent200, new RenderPoint(double.PositiveInfinity, 0.0)));
    }

    [Fact]
    public void TransformMethods_DoNotMutateOriginalValue()
    {
        ViewportTransform viewport = new(
            new RenderSize(1024.0, 768.0),
            new RenderPoint(40.0, -12.5),
            MapZoom.Percent50);

        RenderSize size = viewport.ViewportSize;
        RenderPoint origin = viewport.WorldOrigin;
        MapZoom zoom = viewport.Zoom;

        viewport.WorldToScreen(new RenderPoint(1.0, 2.0));
        viewport.WorldToScreen(new RenderRect(1.0, 2.0, 32.0, 32.0));
        viewport.ScreenToWorld(new RenderPoint(1.0, 2.0));
        viewport.ScreenToTile(new RenderPoint(1.0, 2.0));
        viewport.WithWorldOrigin(new RenderPoint(99.0, 99.0));
        viewport.PanByScreenDelta(new RenderPoint(64.0, 32.0));
        viewport.ZoomAt(MapZoom.Percent200, new RenderPoint(512.0, 384.0));

        Assert.Equal(size, viewport.ViewportSize);
        Assert.Equal(origin, viewport.WorldOrigin);
        Assert.Equal(zoom, viewport.Zoom);
    }
}
