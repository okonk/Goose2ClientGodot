using MapEditor.Core.Terrain;

namespace Goose2.AssetConverter.Terrain;

public sealed record TerrainRegionPlacement(int X, int Y, TerrainGraphicReference Reference, int Mask, double Weight);

public sealed record TerrainRegion(int MinimumX, int MinimumY, IReadOnlyList<TerrainRegionPlacement> Placements);

public sealed record TerrainRegionMiningResult(IReadOnlyList<TerrainRegion> Regions, int DiagonalTrials);

public static class TerrainRegionMiner
{
    private static readonly (int DX, int DY)[] Cardinals =
    [
        (0, -1),
        (1, 0),
        (0, 1),
        (-1, 0),
    ];

    private static readonly (int DX, int DY)[] Corners =
    [
        (-1, -1),
        (1, -1),
        (1, 1),
        (-1, 1),
    ];

    public static TerrainRegionMiningResult Mine(
        TerrainDecodedMap map,
        IReadOnlyCollection<TerrainGraphicReference> family)
    {
        var isFamily = family.ToHashSet();
        var visited = new bool[map.Width, map.Height];
        var regions = new List<TerrainRegion>();
        var diagonalTrials = 0;

        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                if (visited[x, y] || !isFamily.Contains(Reference(map, x, y)))
                {
                    continue;
                }

                var placements = new List<TerrainRegionPlacement>();
                var minimumX = x;
                var minimumY = y;
                var stack = new Stack<(int X, int Y)>();
                stack.Push((x, y));
                visited[x, y] = true;

                while (stack.Count > 0)
                {
                    var (cx, cy) = stack.Pop();
                    if (cx < minimumX || (cx == minimumX && cy < minimumY))
                    {
                        minimumX = cx;
                        minimumY = cy;
                    }

                    var (reference, mask) = Placement(map, cx, cy, isFamily);
                    placements.Add(new TerrainRegionPlacement(cx, cy, reference, mask, 0.0));
                    diagonalTrials += CornerTrials(mask);

                    // N/E/S/W visit order is required by the mining contract; output order is fixed by the final sort.
                    for (var i = 0; i < Cardinals.Length; i++)
                    {
                        var (nx, ny) = (cx + Cardinals[i].DX, cy + Cardinals[i].DY);
                        if (nx < 0 || ny < 0 || nx >= map.Width || ny >= map.Height || visited[nx, ny])
                        {
                            continue;
                        }

                        if (!isFamily.Contains(Reference(map, nx, ny)))
                        {
                            continue;
                        }

                        visited[nx, ny] = true;
                        stack.Push((nx, ny));
                    }
                }

                placements.Sort((a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));
                regions.Add(new TerrainRegion(
                    minimumX,
                    minimumY,
                    placements.Select(placement => placement with { Weight = 0.0 }).ToList().AsReadOnly()));
            }
        }

        regions.Sort((a, b) => a.MinimumY != b.MinimumY ? a.MinimumY.CompareTo(b.MinimumY) : a.MinimumX.CompareTo(b.MinimumX));
        for (var i = 0; i < regions.Count; i++)
        {
            var region = regions[i];
            var weight = 1.0 / (regions.Count * region.Placements.Count);
            regions[i] = region with
            {
                Placements = region.Placements.Select(placement => placement with { Weight = weight }).ToList().AsReadOnly(),
            };
        }

        return new TerrainRegionMiningResult(regions.AsReadOnly(), diagonalTrials);
    }

    private static TerrainGraphicReference Reference(TerrainDecodedMap map, int x, int y)
    {
        var (sheet, graphic) = map.GetLayer(x, y);
        return new TerrainGraphicReference(sheet, graphic);
    }

    private static (TerrainGraphicReference Reference, int Mask) Placement(
        TerrainDecodedMap map,
        int x,
        int y,
        HashSet<TerrainGraphicReference> isFamily)
    {
        var raw = 0;
        for (var i = 0; i < Cardinals.Length; i++)
        {
            var (nx, ny) = (x + Cardinals[i].DX, y + Cardinals[i].DY);
            if (nx < 0 || ny < 0 || nx >= map.Width || ny >= map.Height)
            {
                continue;
            }

            if (isFamily.Contains(Reference(map, nx, ny)))
            {
                raw |= 1 << i;
            }
        }

        for (var i = 0; i < Corners.Length; i++)
        {
            var (nx, ny) = (x + Corners[i].DX, y + Corners[i].DY);
            if (nx < 0 || ny < 0 || nx >= map.Width || ny >= map.Height)
            {
                continue;
            }

            if (isFamily.Contains(Reference(map, nx, ny)))
            {
                raw |= i switch
                {
                    0 => TerrainMasks.NorthWest,
                    1 => TerrainMasks.NorthEast,
                    2 => TerrainMasks.SouthEast,
                    _ => TerrainMasks.SouthWest,
                };
            }
        }

        return (Reference(map, x, y), TerrainMasks.Normalize(raw, TerrainTopology.EightWay));
    }

    private static int CornerTrials(int mask)
    {
        var trials = 0;
        for (var i = 0; i < Corners.Length; i++)
        {
            var northBit = i is 0 or 1 ? TerrainMasks.North : TerrainMasks.South;
            var eastBit = i is 1 or 2 ? TerrainMasks.East : TerrainMasks.West;
            if ((mask & (northBit | eastBit)) == (northBit | eastBit))
            {
                trials++;
            }
        }

        return trials;
    }
}
