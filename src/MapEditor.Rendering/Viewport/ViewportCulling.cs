using System;

namespace MapEditor.Rendering;

public readonly record struct TileRange(int MinX, int MinY, int MaxX, int MaxY)
{
    public static TileRange Empty { get; } = new(0, 0, -1, -1);

    public bool IsEmpty => MinX > MaxX || MinY > MaxY;

    public int Width => IsEmpty ? 0 : checked(MaxX - MinX + 1);

    public int Height => IsEmpty ? 0 : checked(MaxY - MinY + 1);

    public bool Contains(int x, int y) => !IsEmpty && x >= MinX && x <= MaxX && y >= MinY && y <= MaxY;
}

public readonly record struct ViewportTileRanges(
    TileRange VisibleCells,
    TileRange SpriteCandidates);

public static class ViewportCulling
{
    public const int TileSize = 32;

    public static ViewportTileRanges Compute(
        ViewportTransform viewport,
        int mapWidth,
        int mapHeight,
        int maxSpriteWidth,
        int maxSpriteHeight)
    {
        if (mapWidth < 1 || mapWidth > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(mapWidth));
        }

        if (mapHeight < 1 || mapHeight > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(mapHeight));
        }

        if (maxSpriteWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxSpriteWidth));
        }

        if (maxSpriteHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxSpriteHeight));
        }

        RenderRect world = viewport.VisibleWorldRect;
        double cellMinX = Math.Floor(world.X / TileSize);
        double cellMaxX = Math.Ceiling((world.X + world.Width) / TileSize) - 1.0;
        double cellMinY = Math.Floor(world.Y / TileSize);
        double cellMaxY = Math.Ceiling((world.Y + world.Height) / TileSize) - 1.0;

        TileRange visible = ClampToMap(cellMinX, cellMinY, cellMaxX, cellMaxY, mapWidth, mapHeight);
        long sidePadding = HalfOverhangCells(maxSpriteWidth);
        long downPadding = DownwardOverhangCells(maxSpriteHeight);
        TileRange candidates = ClampToMap(
            cellMinX - sidePadding,
            cellMinY,
            cellMaxX + sidePadding,
            cellMaxY + downPadding,
            mapWidth,
            mapHeight);

        return new ViewportTileRanges(visible, candidates);
    }

    private static long HalfOverhangCells(int spriteWidth)
    {
        int overhang = checked(spriteWidth - TileSize);
        if (overhang <= 0)
        {
            return 0;
        }

        return ((long)overhang + 2 * TileSize - 1) / (2 * TileSize);
    }

    private static long DownwardOverhangCells(int spriteHeight)
    {
        int overhang = checked(spriteHeight - TileSize);
        if (overhang <= 0)
        {
            return 0;
        }

        return ((long)overhang + TileSize - 1) / TileSize;
    }

    private static TileRange ClampToMap(
        double minX,
        double minY,
        double maxX,
        double maxY,
        int mapWidth,
        int mapHeight)
    {
        if (maxX < 0.0 || minX > mapWidth - 1 || maxY < 0.0 || minY > mapHeight - 1)
        {
            return TileRange.Empty;
        }

        return new TileRange(
            (int)Math.Clamp(minX, 0.0, mapWidth - 1.0),
            (int)Math.Clamp(minY, 0.0, mapHeight - 1.0),
            (int)Math.Clamp(maxX, 0.0, mapWidth - 1.0),
            (int)Math.Clamp(maxY, 0.0, mapHeight - 1.0));
    }
}
