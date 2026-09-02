using System;

namespace MapEditor.Rendering;

public readonly record struct RenderPoint(double X, double Y);

public readonly record struct RenderSize(double Width, double Height);

public readonly record struct RenderRect(double X, double Y, double Width, double Height);

public readonly record struct RenderColor(byte R, byte G, byte B, byte A);

public readonly record struct MapTileCoordinate(int X, int Y);

internal static class Geometry
{
    public static void CheckPoint(RenderPoint point, string name)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }

    public static void CheckSize(RenderSize size, string name)
    {
        if (!double.IsFinite(size.Width) || size.Width <= 0.0 || !double.IsFinite(size.Height) || size.Height <= 0.0)
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }

    public static void CheckRect(RenderRect rect, string name)
    {
        CheckPoint(new RenderPoint(rect.X, rect.Y), name);
        if (!double.IsFinite(rect.Width) || rect.Width < 0.0 || !double.IsFinite(rect.Height) || rect.Height < 0.0)
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }
}
