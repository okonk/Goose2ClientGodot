using System;

namespace MapEditor.Core;

public readonly record struct MapTileRectangle(int X, int Y, int Width, int Height)
{
    public MapTileRectangle? ClipTo(int boundsWidth, int boundsHeight)
    {
        long left = Math.Max(0, X);
        long right = Math.Min((long)boundsWidth, X + (long)Width);
        long top = Math.Max(0, Y);
        long bottom = Math.Min((long)boundsHeight, Y + (long)Height);
        int width = (int)(right - left);
        int height = (int)(bottom - top);
        return width > 0 && height > 0 ? new MapTileRectangle((int)left, (int)top, width, height) : null;
    }
}
