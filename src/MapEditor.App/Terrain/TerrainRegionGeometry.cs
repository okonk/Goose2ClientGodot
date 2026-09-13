using System.Collections.Generic;
using Avalonia;
using MapEditor.Core;

namespace MapEditor.App.Terrain;

internal static class TerrainRegionGeometry
{
    public const int FrameSize = 32;

    private static readonly IReadOnlyDictionary<TerrainPeer, IReadOnlyList<Point>> SourcePolygons =
        new Dictionary<TerrainPeer, IReadOnlyList<Point>>
        {
            [TerrainPeer.Center] = new[]
            {
                new Point(10, 8), new Point(22, 8), new Point(24, 10), new Point(24, 22),
                new Point(22, 24), new Point(10, 24), new Point(8, 22), new Point(8, 10)
            },
            [TerrainPeer.North] = new[] { new Point(8, 0), new Point(24, 0), new Point(22, 8), new Point(10, 8) },
            [TerrainPeer.East] = new[] { new Point(32, 8), new Point(32, 24), new Point(24, 22), new Point(24, 10) },
            [TerrainPeer.South] = new[] { new Point(24, 32), new Point(8, 32), new Point(10, 24), new Point(22, 24) },
            [TerrainPeer.West] = new[] { new Point(0, 24), new Point(0, 8), new Point(8, 10), new Point(8, 22) },
            [TerrainPeer.NorthEast] = new[]
            {
                new Point(24, 0), new Point(32, 0), new Point(32, 8), new Point(24, 10), new Point(22, 8)
            },
            [TerrainPeer.SouthEast] = new[]
            {
                new Point(32, 24), new Point(32, 32), new Point(24, 32), new Point(22, 24), new Point(24, 22)
            },
            [TerrainPeer.SouthWest] = new[]
            {
                new Point(8, 32), new Point(0, 32), new Point(0, 24), new Point(8, 22), new Point(10, 24)
            },
            [TerrainPeer.NorthWest] = new[]
            {
                new Point(0, 8), new Point(0, 0), new Point(8, 0), new Point(10, 8), new Point(8, 10)
            }
        };

    private static readonly TerrainPeer[] Precedence =
    {
        TerrainPeer.Center,
        TerrainPeer.North,
        TerrainPeer.East,
        TerrainPeer.South,
        TerrainPeer.West,
        TerrainPeer.NorthEast,
        TerrainPeer.SouthEast,
        TerrainPeer.SouthWest,
        TerrainPeer.NorthWest
    };

    public static IReadOnlyList<Point> GetSourcePolygon(TerrainPeer peer)
        => SourcePolygons[peer];

    public static IReadOnlyList<Point> ToScreenPolygon(TerrainPeer peer, Point frameOrigin, double zoom)
    {
        IReadOnlyList<Point> source = SourcePolygons[peer];
        Point[] screen = new Point[source.Count];
        for (int i = 0; i < source.Count; i++)
        {
            Point p = source[i];
            screen[i] = new Point(frameOrigin.X + p.X * zoom, frameOrigin.Y + p.Y * zoom);
        }

        return screen;
    }

    public static TerrainPeer? HitTestSource(Point sourcePoint)
    {
        foreach (TerrainPeer peer in Precedence)
        {
            if (Contains(SourcePolygons[peer], sourcePoint))
            {
                return peer;
            }
        }

        return null;
    }

    // All locked polygons are convex and consistently oriented, so a point is
    // inside (or on the boundary) iff every edge cross product is non-negative.
    private static bool Contains(IReadOnlyList<Point> polygon, Point point)
    {
        bool sawPositive = false;
        bool sawNegative = false;
        for (int i = 0; i < polygon.Count; i++)
        {
            Point a = polygon[i];
            Point b = polygon[(i + 1) % polygon.Count];
            double cross = (b.X - a.X) * (point.Y - a.Y) - (b.Y - a.Y) * (point.X - a.X);
            if (cross > 0)
            {
                sawPositive = true;
            }
            else if (cross < 0)
            {
                sawNegative = true;
            }

            if (sawPositive && sawNegative)
            {
                return false;
            }
        }

        return true;
    }
}
