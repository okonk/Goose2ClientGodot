using System;
using System.Collections.Generic;
using System.Linq;

namespace MapEditor.Core;

internal interface ITerrainPatchResolver
{
    Guid? GetLogicalCenter(MapTileLayer graphic);

    bool TryResolvePatch(
        MapDocument document,
        int layer,
        IReadOnlyDictionary<int, Guid?> centerOverrides,
        IReadOnlyCollection<int> directlyChangedIndices,
        out TerrainResolvedPatch patch,
        out TerrainResolutionFailure? failure);
}

public sealed class TerrainMapResolver : ITerrainPatchResolver
{
    private static readonly (TerrainPeer Peer, int Dx, int Dy)[] NeighborOffsets =
    [
        (TerrainPeer.North, 0, -1),
        (TerrainPeer.East, 1, 0),
        (TerrainPeer.South, 0, 1),
        (TerrainPeer.West, -1, 0),
        (TerrainPeer.NorthEast, 1, -1),
        (TerrainPeer.SouthEast, 1, 1),
        (TerrainPeer.SouthWest, -1, 1),
        (TerrainPeer.NorthWest, -1, -1)
    ];

    private readonly TerrainCatalogIndex _catalog;

    public TerrainMapResolver(TerrainCatalogIndex catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
    }

    public Guid? GetLogicalCenter(MapTileLayer graphic)
    {
        if (graphic.Graphic == 0)
        {
            return null;
        }

        return _catalog.TryGetGraphic(new TerrainGraphicReference(graphic.Sheet, graphic.Graphic), out var definition)
            ? definition.Pattern.Center
            : null;
    }

    internal bool TryResolvePatch(
        MapDocument document,
        int layer,
        IReadOnlyDictionary<int, Guid?> centerOverrides,
        IReadOnlyCollection<int> directlyChangedIndices,
        out TerrainResolvedPatch patch,
        out TerrainResolutionFailure? failure)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(centerOverrides);
        ArgumentNullException.ThrowIfNull(directlyChangedIndices);
        if (layer < 0 || layer >= MapDocument.LayerCount)
        {
            throw new ArgumentOutOfRangeException(nameof(layer));
        }

        foreach (var index in centerOverrides.Keys.OrderBy(static value => value))
        {
            if (index < 0 || index >= document.TileCount)
            {
                throw new ArgumentOutOfRangeException(nameof(centerOverrides));
            }

            var center = centerOverrides[index];
            if (center is not null && !_catalog.TryGetTerrain(center.Value, out _))
            {
                (patch, failure) = Fail(layer, center.Value, index % document.Width, index / document.Width, $"Unknown terrain {center}.");
                return false;
            }
        }

        var direct = directlyChangedIndices.Distinct().OrderBy(static value => value).ToList();
        foreach (var index in direct)
        {
            if (index < 0 || index >= document.TileCount)
            {
                throw new ArgumentOutOfRangeException(nameof(directlyChangedIndices));
            }
        }

        Guid? FinalCenter(int index)
            => centerOverrides.TryGetValue(index, out var center)
                ? center
                : GetLogicalCenter(document.GetTile(index).GetLayer(layer));

        void AddHalo(SortedSet<int> cells, int index)
        {
            var x = index % document.Width;
            var y = index / document.Width;
            foreach (var (_, dx, dy) in NeighborOffsets)
            {
                var neighborX = x + dx;
                var neighborY = y + dy;
                if (neighborX < 0 || neighborX >= document.Width || neighborY < 0 || neighborY >= document.Height)
                {
                    continue;
                }

                var neighbor = neighborY * document.Width + neighborX;
                if (FinalCenter(neighbor) is not null)
                {
                    cells.Add(neighbor);
                }
            }
        }

        var affected = new SortedSet<int>();
        foreach (var index in direct)
        {
            if (!centerOverrides.ContainsKey(index))
            {
                (patch, failure) = Fail(layer, null, index % document.Width, index / document.Width, "Tile has no requested terrain center.");
                return false;
            }

            if (centerOverrides[index] is null && GetLogicalCenter(document.GetTile(index).GetLayer(layer)) is null)
            {
                continue;
            }

            affected.Add(index);
            AddHalo(affected, index);
        }

        var changes = new SortedDictionary<int, MapTileLayer>();
        foreach (var index in affected)
        {
            var x = index % document.Width;
            var y = index / document.Width;
            var center = FinalCenter(index);
            if (center is null)
            {
                changes[index] = default;
                continue;
            }

            if (!_catalog.TryGetTerrain(center.Value, out var terrain))
            {
                (patch, failure) = Fail(layer, center.Value, x, y, $"Unknown terrain {center}.");
                return false;
            }

            var candidates = _catalog.GetCandidates(center.Value)
                .Where(group => group.Pattern.Center == center.Value)
                .ToList();
            if (candidates.Count == 0)
            {
                (patch, failure) = Fail(layer, center.Value, x, y, $"Terrain {terrain.Name} has no candidate patterns.");
                return false;
            }

            var selection = TerrainPatternScorer.Select(BuildDesired(document, x, y, FinalCenter), x, y, candidates);
            changes[index] = new MapTileLayer(selection.Variant.Reference.Sheet, selection.Variant.Reference.Graphic);
        }

        patch = new TerrainResolvedPatch(layer, changes);
        failure = null;
        return true;
    }

    private static (TerrainResolvedPatch Patch, TerrainResolutionFailure? Failure) Fail(
        int layer,
        Guid? terrainId,
        int x,
        int y,
        string message)
        => (new TerrainResolvedPatch(layer, new SortedDictionary<int, MapTileLayer>()), new TerrainResolutionFailure(terrainId, x, y, message));

    private TerrainPattern BuildDesired(
        MapDocument document,
        int x,
        int y,
        Func<int, Guid?> centerAt)
    {
        var pattern = new TerrainPattern { Center = centerAt(y * document.Width + x) };
        foreach (var (peer, dx, dy) in NeighborOffsets)
        {
            var neighborX = x + dx;
            var neighborY = y + dy;
            Guid? neighbor = null;
            if (neighborX >= 0 && neighborX < document.Width && neighborY >= 0 && neighborY < document.Height)
            {
                neighbor = centerAt(neighborY * document.Width + neighborX);
            }

            pattern = peer switch
            {
                TerrainPeer.North => pattern with { North = neighbor },
                TerrainPeer.East => pattern with { East = neighbor },
                TerrainPeer.South => pattern with { South = neighbor },
                TerrainPeer.West => pattern with { West = neighbor },
                TerrainPeer.NorthEast => pattern with { NorthEast = neighbor },
                TerrainPeer.SouthEast => pattern with { SouthEast = neighbor },
                TerrainPeer.SouthWest => pattern with { SouthWest = neighbor },
                _ => pattern with { NorthWest = neighbor }
            };
        }

        return pattern;
    }

    bool ITerrainPatchResolver.TryResolvePatch(
        MapDocument document,
        int layer,
        IReadOnlyDictionary<int, Guid?> centerOverrides,
        IReadOnlyCollection<int> directlyChangedIndices,
        out TerrainResolvedPatch patch,
        out TerrainResolutionFailure? failure)
        => TryResolvePatch(document, layer, centerOverrides, directlyChangedIndices, out patch, out failure);
}
