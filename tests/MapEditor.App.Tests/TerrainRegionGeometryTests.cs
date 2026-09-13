using System;
using System.Collections.Generic;
using Avalonia;
using MapEditor.App.Terrain;
using MapEditor.Core;
using Xunit;

namespace MapEditor.App.Tests;

public class TerrainRegionGeometryTests
{
    [Fact]
    public void GetSourcePolygon_ReturnsLockedVertices_ForEveryPeer()
    {
        Assert.Equal(
            new[]
            {
                new Point(10, 8), new Point(22, 8), new Point(24, 10), new Point(24, 22),
                new Point(22, 24), new Point(10, 24), new Point(8, 22), new Point(8, 10)
            },
            TerrainRegionGeometry.GetSourcePolygon(TerrainPeer.Center));
        Assert.Equal(
            new[] { new Point(8, 0), new Point(24, 0), new Point(22, 8), new Point(10, 8) },
            TerrainRegionGeometry.GetSourcePolygon(TerrainPeer.North));
        Assert.Equal(
            new[] { new Point(32, 8), new Point(32, 24), new Point(24, 22), new Point(24, 10) },
            TerrainRegionGeometry.GetSourcePolygon(TerrainPeer.East));
        Assert.Equal(
            new[] { new Point(24, 32), new Point(8, 32), new Point(10, 24), new Point(22, 24) },
            TerrainRegionGeometry.GetSourcePolygon(TerrainPeer.South));
        Assert.Equal(
            new[] { new Point(0, 24), new Point(0, 8), new Point(8, 10), new Point(8, 22) },
            TerrainRegionGeometry.GetSourcePolygon(TerrainPeer.West));
        Assert.Equal(
            new[]
            {
                new Point(24, 0), new Point(32, 0), new Point(32, 8), new Point(24, 10), new Point(22, 8)
            },
            TerrainRegionGeometry.GetSourcePolygon(TerrainPeer.NorthEast));
        Assert.Equal(
            new[]
            {
                new Point(32, 24), new Point(32, 32), new Point(24, 32), new Point(22, 24), new Point(24, 22)
            },
            TerrainRegionGeometry.GetSourcePolygon(TerrainPeer.SouthEast));
        Assert.Equal(
            new[]
            {
                new Point(8, 32), new Point(0, 32), new Point(0, 24), new Point(8, 22), new Point(10, 24)
            },
            TerrainRegionGeometry.GetSourcePolygon(TerrainPeer.SouthWest));
        Assert.Equal(
            new[]
            {
                new Point(0, 8), new Point(0, 0), new Point(8, 0), new Point(10, 8), new Point(8, 10)
            },
            TerrainRegionGeometry.GetSourcePolygon(TerrainPeer.NorthWest));
    }

    [Fact]
    public void HitTest_InteriorPoints_ReturnOwningRegion()
    {
        Assert.Equal(TerrainPeer.Center, TerrainRegionGeometry.HitTestSource(new Point(16, 16)));
        Assert.Equal(TerrainPeer.North, TerrainRegionGeometry.HitTestSource(new Point(16, 3)));
        Assert.Equal(TerrainPeer.East, TerrainRegionGeometry.HitTestSource(new Point(28, 16)));
        Assert.Equal(TerrainPeer.South, TerrainRegionGeometry.HitTestSource(new Point(16, 28)));
        Assert.Equal(TerrainPeer.West, TerrainRegionGeometry.HitTestSource(new Point(3, 16)));
        Assert.Equal(TerrainPeer.NorthEast, TerrainRegionGeometry.HitTestSource(new Point(28, 3)));
        Assert.Equal(TerrainPeer.SouthEast, TerrainRegionGeometry.HitTestSource(new Point(28, 28)));
        Assert.Equal(TerrainPeer.SouthWest, TerrainRegionGeometry.HitTestSource(new Point(3, 28)));
        Assert.Equal(TerrainPeer.NorthWest, TerrainRegionGeometry.HitTestSource(new Point(3, 3)));
    }

    [Fact]
    public void HitTest_OutsidePoints_ReturnsNone()
    {
        Assert.Null(TerrainRegionGeometry.HitTestSource(new Point(-0.5, -0.5)));
        Assert.Null(TerrainRegionGeometry.HitTestSource(new Point(32.5, -0.5)));
        Assert.Null(TerrainRegionGeometry.HitTestSource(new Point(32.5, 32.5)));
        Assert.Null(TerrainRegionGeometry.HitTestSource(new Point(-0.5, 32.5)));
        Assert.Null(TerrainRegionGeometry.HitTestSource(new Point(-1, 16)));
        Assert.Null(TerrainRegionGeometry.HitTestSource(new Point(33, 16)));
        Assert.Null(TerrainRegionGeometry.HitTestSource(new Point(16, -1)));
        Assert.Null(TerrainRegionGeometry.HitTestSource(new Point(16, 33)));
    }

    [Fact]
    public void HitTest_SharedBoundaries_UsesDocumentedPrecedence()
    {
        Assert.Equal(TerrainPeer.Center, TerrainRegionGeometry.HitTestSource(new Point(16, 8)));
        Assert.Equal(TerrainPeer.Center, TerrainRegionGeometry.HitTestSource(new Point(24, 16)));
        Assert.Equal(TerrainPeer.Center, TerrainRegionGeometry.HitTestSource(new Point(16, 24)));
        Assert.Equal(TerrainPeer.Center, TerrainRegionGeometry.HitTestSource(new Point(8, 16)));
        Assert.Equal(TerrainPeer.North, TerrainRegionGeometry.HitTestSource(new Point(23, 4)));
        Assert.Equal(TerrainPeer.North, TerrainRegionGeometry.HitTestSource(new Point(9, 4)));
        Assert.Equal(TerrainPeer.East, TerrainRegionGeometry.HitTestSource(new Point(28, 9)));
        Assert.Equal(TerrainPeer.East, TerrainRegionGeometry.HitTestSource(new Point(28, 23)));
        Assert.Equal(TerrainPeer.South, TerrainRegionGeometry.HitTestSource(new Point(23, 28)));
        Assert.Equal(TerrainPeer.South, TerrainRegionGeometry.HitTestSource(new Point(9, 28)));
        Assert.Equal(TerrainPeer.West, TerrainRegionGeometry.HitTestSource(new Point(4, 9)));
        Assert.Equal(TerrainPeer.West, TerrainRegionGeometry.HitTestSource(new Point(4, 23)));
    }

    [Fact]
    public void HitTest_EveryVertex_ReturnsPrecedenceOwner()
    {
        foreach (TerrainPeer peer in (TerrainPeer[])Enum.GetValues(typeof(TerrainPeer)))
        {
            foreach (Point vertex in TerrainRegionGeometry.GetSourcePolygon(peer))
            {
                Assert.Equal(ExpectedOwner(vertex), TerrainRegionGeometry.HitTestSource(vertex));
            }
        }
    }

    [Fact]
    public void ToScreenPolygon_TranslatesByFrameOriginAndScalesByZoom()
    {
        Point origin = new(100, 200);
        double zoom = 2.5;
        foreach (TerrainPeer peer in (TerrainPeer[])Enum.GetValues(typeof(TerrainPeer)))
        {
            IReadOnlyList<Point> source = TerrainRegionGeometry.GetSourcePolygon(peer);
            IReadOnlyList<Point> screen = TerrainRegionGeometry.ToScreenPolygon(peer, origin, zoom);
            Assert.Equal(source.Count, screen.Count);
            for (int i = 0; i < source.Count; i++)
            {
                Assert.Equal(new Point(origin.X + source[i].X * zoom, origin.Y + source[i].Y * zoom), screen[i]);
            }

            Assert.Equal(
                new[]
                {
                    new Point(10, 8), new Point(22, 8), new Point(24, 10), new Point(24, 22),
                    new Point(22, 24), new Point(10, 24), new Point(8, 22), new Point(8, 10)
                },
                TerrainRegionGeometry.GetSourcePolygon(TerrainPeer.Center));
        }
    }

    [Fact]
    public void HitTest_IsInverseOfScreenTransform_ForEveryRegion()
    {
        Point origin = new(50, -30);
        double zoom = 3.0;
        foreach (TerrainPeer peer in (TerrainPeer[])Enum.GetValues(typeof(TerrainPeer)))
        {
            Point sourcePoint = InteriorPoint(peer);
            Point screenPoint = new(origin.X + sourcePoint.X * zoom, origin.Y + sourcePoint.Y * zoom);
            Point backToSource = new((screenPoint.X - origin.X) / zoom, (screenPoint.Y - origin.Y) / zoom);
            Assert.Equal(peer, TerrainRegionGeometry.HitTestSource(backToSource));
        }
    }

    private static Point InteriorPoint(TerrainPeer peer)
        => peer switch
        {
            TerrainPeer.Center => new(16, 16),
            TerrainPeer.North => new(16, 3),
            TerrainPeer.East => new(28, 16),
            TerrainPeer.South => new(16, 28),
            TerrainPeer.West => new(3, 16),
            TerrainPeer.NorthEast => new(28, 3),
            TerrainPeer.SouthEast => new(28, 28),
            TerrainPeer.SouthWest => new(3, 28),
            TerrainPeer.NorthWest => new(3, 3),
            _ => throw new ArgumentOutOfRangeException(nameof(peer))
        };

    private static TerrainPeer ExpectedOwner(Point vertex)
        => (vertex.X, vertex.Y) switch
        {
            (10, 8) or (22, 8) or (24, 10) or (24, 22) or (22, 24) or (10, 24) or (8, 22) or (8, 10)
                => TerrainPeer.Center,
            (8, 0) or (24, 0) => TerrainPeer.North,
            (32, 8) or (32, 24) => TerrainPeer.East,
            (24, 32) or (8, 32) => TerrainPeer.South,
            (0, 24) or (0, 8) => TerrainPeer.West,
            (32, 0) => TerrainPeer.NorthEast,
            (32, 32) => TerrainPeer.SouthEast,
            (0, 32) => TerrainPeer.SouthWest,
            (0, 0) => TerrainPeer.NorthWest,
            _ => throw new ArgumentOutOfRangeException(nameof(vertex))
        };
}
