using System;
using System.Collections.Generic;
using System.Linq;

namespace MapEditor.Core.Terrain;

public static class TerrainMasks
{
    public const int North = 1;
    public const int East = 2;
    public const int South = 4;
    public const int West = 8;
    public const int NorthEast = 16;
    public const int SouthEast = 32;
    public const int SouthWest = 64;
    public const int NorthWest = 128;

    private static readonly IReadOnlyList<int> FourWayRequired = Array.AsReadOnly(Enumerable.Range(0, 16).ToArray());

    private static readonly IReadOnlyList<int> EightWayRequired = Array.AsReadOnly(new[]
    {
        0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15,
        19, 23, 27, 31, 38, 39, 46, 47, 55, 63, 76, 77, 78, 79,
        95, 110, 111, 127, 137, 139, 141, 143, 155, 159, 175, 191,
        205, 207, 223, 239, 255
    });

    public static int Normalize(int mask, TerrainTopology topology)
    {
        switch (topology)
        {
            case TerrainTopology.FourWay:
                return mask & 0x0F;
            case TerrainTopology.EightWay:
            {
                var normalized = mask & 0xFF;
                if ((normalized & (North | East)) != (North | East))
                {
                    normalized &= ~NorthEast;
                }

                if ((normalized & (South | East)) != (South | East))
                {
                    normalized &= ~SouthEast;
                }

                if ((normalized & (South | West)) != (South | West))
                {
                    normalized &= ~SouthWest;
                }

                if ((normalized & (North | West)) != (North | West))
                {
                    normalized &= ~NorthWest;
                }

                return normalized;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(topology));
        }
    }

    public static bool IsReachable(int mask, TerrainTopology topology) => Normalize(mask, topology) == mask;

    public static IReadOnlyList<int> Required(TerrainTopology topology)
    {
        switch (topology)
        {
            case TerrainTopology.FourWay:
                return FourWayRequired;
            case TerrainTopology.EightWay:
                return EightWayRequired;
            default:
                throw new ArgumentOutOfRangeException(nameof(topology));
        }
    }
}
