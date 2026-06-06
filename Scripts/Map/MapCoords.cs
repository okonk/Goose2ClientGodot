using Godot;

namespace Goose2Client.Map;

/// <summary>The ONE place tile↔world conversion happens. Godot 2D is Y-down (like the server's
/// tile rows), so there is NO vertical flip here — tile (x,y) maps to world (x,y). Unity's pervasive
/// `map.Height - y` was only to reach Unity's Y-up world and is intentionally absent.</summary>
public static class MapCoords
{
    public const int TileSize = 32;   // 1 tile = 32 px (Unity pixelsPerUnit = 32)

    /// <summary>Center of tile (x,y) in world pixels.</summary>
    public static Vector2 TileCenter(int x, int y)
        => new(x * TileSize + TileSize / 2f, y * TileSize + TileSize / 2f);

    /// <summary>Bottom-center of tile (x,y): horizontal center, bottom edge of the cell.
    /// Tiles/items anchor here so tall sprites grow upward.</summary>
    public static Vector2 TileBottomCenter(int x, int y)
        => new(x * TileSize + TileSize / 2f, (y + 1) * TileSize);

    /// <summary>World pixel → tile coords (floor).</summary>
    public static (int x, int y) WorldToTile(Vector2 world)
        => ((int)(world.X / TileSize), (int)(world.Y / TileSize));
}
