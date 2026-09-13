using System;

namespace MapEditor.Core;

public enum TerrainPeer
{
    Center,
    North,
    East,
    South,
    West,
    NorthEast,
    SouthEast,
    SouthWest,
    NorthWest
}

public readonly record struct TerrainPattern(
    Guid? Center = null,
    Guid? North = null,
    Guid? East = null,
    Guid? South = null,
    Guid? West = null,
    Guid? NorthEast = null,
    Guid? SouthEast = null,
    Guid? SouthWest = null,
    Guid? NorthWest = null)
{
    public Guid? Get(TerrainPeer peer)
    {
        return peer switch
        {
            TerrainPeer.Center => Center,
            TerrainPeer.North => North,
            TerrainPeer.East => East,
            TerrainPeer.South => South,
            TerrainPeer.West => West,
            TerrainPeer.NorthEast => NorthEast,
            TerrainPeer.SouthEast => SouthEast,
            TerrainPeer.SouthWest => SouthWest,
            TerrainPeer.NorthWest => NorthWest,
            _ => throw new ArgumentOutOfRangeException(nameof(peer))
        };
    }
}
