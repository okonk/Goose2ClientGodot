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

        var changes = new Dictionary<int, MapTileLayer>();
        foreach (var index in directlyChangedIndices.Distinct().OrderBy(static value => value))
        {
            if (index < 0 || index >= document.TileCount)
            {
                throw new ArgumentOutOfRangeException(nameof(directlyChangedIndices));
            }

            var x = index % document.Width;
            var y = index / document.Width;

            if (!centerOverrides.TryGetValue(index, out var requested))
            {
                (patch, failure) = Fail(layer, null, x, y, "Tile has no requested terrain center.");
                return false;
            }

            if (requested is null || !_catalog.TryGetTerrain(requested.Value, out var terrain))
            {
                (patch, failure) = Fail(layer, requested, x, y, $"Unknown terrain {requested}.");
                return false;
            }

            var candidates = _catalog.GetCandidates(terrain.Id)
                .Where(group => group.Pattern.Center == terrain.Id)
                .ToList();
            if (candidates.Count == 0)
            {
                (patch, failure) = Fail(layer, terrain.Id, x, y, $"Terrain {terrain.Name} has no candidate patterns.");
                return false;
            }

            var desired = BuildDesired(document, layer, x, y, centerOverrides, terrain.Id);
            var selection = TerrainPatternScorer.Select(desired, x, y, candidates);
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
        => (new TerrainResolvedPatch(layer, new Dictionary<int, MapTileLayer>()), new TerrainResolutionFailure(terrainId, x, y, message));

    private TerrainPattern BuildDesired(
        MapDocument document,
        int layer,
        int x,
        int y,
        IReadOnlyDictionary<int, Guid?> centerOverrides,
        Guid center)
    {
        var pattern = new TerrainPattern { Center = center };
        foreach (var (peer, dx, dy) in NeighborOffsets)
        {
            var neighborX = x + dx;
            var neighborY = y + dy;
            Guid? neighbor = null;
            if (neighborX >= 0 && neighborX < document.Width && neighborY >= 0 && neighborY < document.Height)
            {
                var neighborIndex = neighborY * document.Width + neighborX;
                if (!centerOverrides.TryGetValue(neighborIndex, out neighbor))
                {
                    neighbor = GetLogicalCenter(document.GetTile(neighborIndex).GetLayer(layer));
                }
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
