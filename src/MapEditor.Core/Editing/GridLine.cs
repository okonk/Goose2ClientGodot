using System;
using System.Collections.Generic;

namespace MapEditor.Core;

internal static class GridLine
{
    internal static IEnumerable<MapCoordinate> Enumerate(MapCoordinate start, MapCoordinate end)
    {
        int dx = end.X - start.X;
        int dy = end.Y - start.Y;
        int sx = Math.Sign(dx);
        int sy = Math.Sign(dy);
        int adx = Math.Abs(dx);
        int ady = Math.Abs(dy);
        int error = adx - ady;
        int x = start.X;
        int y = start.Y;

        yield return new MapCoordinate(x, y);

        for (int step = 0; step < Math.Max(adx, ady); step++)
        {
            int twiceError = 2 * error;
            if (twiceError > -ady)
            {
                x += sx;
                error -= ady;
            }
            if (twiceError < adx)
            {
                y += sy;
                error += adx;
            }

            yield return new MapCoordinate(x, y);
        }
    }
}
