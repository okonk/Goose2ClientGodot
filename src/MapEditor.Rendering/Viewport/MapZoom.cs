using System;

namespace MapEditor.Rendering;

public enum MapZoom
{
    Percent25 = 25,
    Percent50 = 50,
    Percent100 = 100,
    Percent200 = 200,
    Percent400 = 400
}

public static class MapZoomLevels
{
    private static readonly MapZoom[] Levels =
    {
        MapZoom.Percent25,
        MapZoom.Percent50,
        MapZoom.Percent100,
        MapZoom.Percent200,
        MapZoom.Percent400
    };

    public static double GetScale(MapZoom zoom)
    {
        return Levels[Index(zoom)] switch
        {
            MapZoom.Percent25 => 0.25,
            MapZoom.Percent50 => 0.5,
            MapZoom.Percent100 => 1.0,
            MapZoom.Percent200 => 2.0,
            _ => 4.0
        };
    }

    public static MapZoom ZoomIn(MapZoom zoom)
    {
        int index = Index(zoom);
        return Levels[Math.Min(index + 1, Levels.Length - 1)];
    }

    public static MapZoom ZoomOut(MapZoom zoom)
    {
        int index = Index(zoom);
        return Levels[Math.Max(index - 1, 0)];
    }

    private static int Index(MapZoom zoom)
    {
        int index = Array.IndexOf(Levels, zoom);
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(zoom));
        }

        return index;
    }
}
