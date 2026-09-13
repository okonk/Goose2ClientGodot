using System;
using System.Collections.Generic;
using Avalonia;

namespace MapEditor.App.Terrain;

internal static class TerrainSheetLine
{
    public static int StepCount(Point from, Point to)
        => (int)Math.Ceiling(Math.Max(Math.Abs(to.X - from.X), Math.Abs(to.Y - from.Y)));

    public static void AppendSamples(Point from, Point to, List<Point> samples)
    {
        int steps = StepCount(from, to);
        for (int i = 0; i <= steps; i++)
        {
            double t = steps == 0 ? 0 : (double)i / steps;
            samples.Add(new Point(from.X + (to.X - from.X) * t, from.Y + (to.Y - from.Y) * t));
        }
    }
}
