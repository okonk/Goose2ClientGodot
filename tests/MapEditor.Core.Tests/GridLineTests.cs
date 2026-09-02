using System;
using System.Linq;
using MapEditor.Core;
using Xunit;

namespace MapEditor.Core.Tests;

public class GridLineTests
{
    [Fact]
    public void Enumerate_ZeroLengthContainsOnePoint()
    {
        var points = GridLine.Enumerate(new MapCoordinate(7, 3), new MapCoordinate(7, 3)).ToArray();

        Assert.Equal(new[] { new MapCoordinate(7, 3) }, points);
    }

    [Theory]
    [InlineData(0, 0, 4, 0)]
    [InlineData(4, 0, 0, 0)]
    [InlineData(0, 0, 0, 4)]
    [InlineData(0, 4, 0, 0)]
    public void Enumerate_HorizontalAndVerticalIncludeBothEndpoints(int startX, int startY, int endX, int endY)
    {
        var start = new MapCoordinate(startX, startY);
        var end = new MapCoordinate(endX, endY);
        var points = GridLine.Enumerate(start, end).ToArray();

        Assert.Equal(start, points[0]);
        Assert.Equal(end, points[^1]);
        Assert.Equal(Math.Max(Math.Abs(endX - startX), Math.Abs(endY - startY)) + 1, points.Length);
    }

    [Fact]
    public void Enumerate_DiagonalIncludesEveryCell()
    {
        Assert.Equal(
            new[]
            {
                new MapCoordinate(0, 0),
                new MapCoordinate(1, 1),
                new MapCoordinate(2, 2),
                new MapCoordinate(3, 3)
            },
            GridLine.Enumerate(new MapCoordinate(0, 0), new MapCoordinate(3, 3)).ToArray());

        Assert.Equal(
            new[]
            {
                new MapCoordinate(3, 3),
                new MapCoordinate(2, 2),
                new MapCoordinate(1, 1),
                new MapCoordinate(0, 0)
            },
            GridLine.Enumerate(new MapCoordinate(3, 3), new MapCoordinate(0, 0)).ToArray());

        Assert.Equal(
            new[]
            {
                new MapCoordinate(0, 0),
                new MapCoordinate(1, -1),
                new MapCoordinate(2, -2),
                new MapCoordinate(3, -3)
            },
            GridLine.Enumerate(new MapCoordinate(0, 0), new MapCoordinate(3, -3)).ToArray());

        Assert.Equal(
            new[]
            {
                new MapCoordinate(0, 3),
                new MapCoordinate(1, 2),
                new MapCoordinate(2, 1),
                new MapCoordinate(3, 0)
            },
            GridLine.Enumerate(new MapCoordinate(0, 3), new MapCoordinate(3, 0)).ToArray());
    }

    [Fact]
    public void Enumerate_ShallowLineMatchesLockedErrorTermSequence()
    {
        Assert.Equal(
            new[]
            {
                new MapCoordinate(0, 0),
                new MapCoordinate(1, 0),
                new MapCoordinate(2, 1),
                new MapCoordinate(3, 1),
                new MapCoordinate(4, 2),
                new MapCoordinate(5, 2)
            },
            GridLine.Enumerate(new MapCoordinate(0, 0), new MapCoordinate(5, 2)).ToArray());

        Assert.Equal(
            new[]
            {
                new MapCoordinate(5, 2),
                new MapCoordinate(4, 2),
                new MapCoordinate(3, 1),
                new MapCoordinate(2, 1),
                new MapCoordinate(1, 0),
                new MapCoordinate(0, 0)
            },
            GridLine.Enumerate(new MapCoordinate(5, 2), new MapCoordinate(0, 0)).ToArray());

        Assert.Equal(
            new[]
            {
                new MapCoordinate(0, 0),
                new MapCoordinate(-1, 0),
                new MapCoordinate(-2, -1),
                new MapCoordinate(-3, -1),
                new MapCoordinate(-4, -2),
                new MapCoordinate(-5, -2)
            },
            GridLine.Enumerate(new MapCoordinate(0, 0), new MapCoordinate(-5, -2)).ToArray());
    }

    [Fact]
    public void Enumerate_SteepLineMatchesLockedErrorTermSequence()
    {
        Assert.Equal(
            new[]
            {
                new MapCoordinate(0, 0),
                new MapCoordinate(0, 1),
                new MapCoordinate(1, 2),
                new MapCoordinate(1, 3),
                new MapCoordinate(2, 4),
                new MapCoordinate(2, 5)
            },
            GridLine.Enumerate(new MapCoordinate(0, 0), new MapCoordinate(2, 5)).ToArray());

        Assert.Equal(
            new[]
            {
                new MapCoordinate(2, 5),
                new MapCoordinate(2, 4),
                new MapCoordinate(1, 3),
                new MapCoordinate(1, 2),
                new MapCoordinate(0, 1),
                new MapCoordinate(0, 0)
            },
            GridLine.Enumerate(new MapCoordinate(2, 5), new MapCoordinate(0, 0)).ToArray());

        Assert.Equal(
            new[]
            {
                new MapCoordinate(0, 0),
                new MapCoordinate(0, -1),
                new MapCoordinate(-1, -2),
                new MapCoordinate(-1, -3),
                new MapCoordinate(-2, -4),
                new MapCoordinate(-2, -5)
            },
            GridLine.Enumerate(new MapCoordinate(0, 0), new MapCoordinate(-2, -5)).ToArray());
    }

    [Theory]
    [InlineData(9, 7)]
    [InlineData(7, 9)]
    [InlineData(3, 9)]
    [InlineData(1, 7)]
    [InlineData(1, 3)]
    [InlineData(3, 1)]
    [InlineData(7, 1)]
    [InlineData(9, 3)]
    public void Enumerate_AllOctantsAreMonotonicConnectedAndExpectedLength(int endX, int endY)
    {
        var start = new MapCoordinate(5, 5);
        var end = new MapCoordinate(endX, endY);
        var points = GridLine.Enumerate(start, end).ToArray();

        Assert.Equal(Math.Max(Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y)) + 1, points.Length);
        Assert.Equal(start, points[0]);
        Assert.Equal(end, points[^1]);
        Assert.Equal(points.Length, points.Distinct().Count());

        for (int i = 1; i < points.Length; i++)
        {
            int deltaX = points[i].X - points[i - 1].X;
            int deltaY = points[i].Y - points[i - 1].Y;

            Assert.Equal(1, Math.Max(Math.Abs(deltaX), Math.Abs(deltaY)));
            if (end.X > start.X)
            {
                Assert.InRange(deltaX, 0, 1);
            }
            if (end.X < start.X)
            {
                Assert.InRange(deltaX, -1, 0);
            }
            if (end.Y > start.Y)
            {
                Assert.InRange(deltaY, 0, 1);
            }
            if (end.Y < start.Y)
            {
                Assert.InRange(deltaY, -1, 0);
            }
        }
    }

    [Theory]
    [InlineData(0, 0, 999, 999)]
    [InlineData(999, 999, 0, 0)]
    [InlineData(0, 0, 999, 1)]
    [InlineData(999, 0, 0, 999)]
    [InlineData(0, 999, 999, 0)]
    public void Enumerate_NearMaximumMapCoordinatesStaysInBounds(int startX, int startY, int endX, int endY)
    {
        var points = GridLine.Enumerate(new MapCoordinate(startX, startY), new MapCoordinate(endX, endY)).ToArray();

        Assert.Equal(Math.Max(Math.Abs(endX - startX), Math.Abs(endY - startY)) + 1, points.Length);
        Assert.All(
            points,
            point =>
            {
                Assert.InRange(point.X, 0, 999);
                Assert.InRange(point.Y, 0, 999);
            });
    }
}
