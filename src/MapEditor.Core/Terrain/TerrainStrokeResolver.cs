using System;
using System.Collections.Generic;
using System.Linq;
using MapEditor.Core;

namespace MapEditor.Core.Terrain;

internal readonly record struct TerrainCellIntent(
    int CellIndex,
    TerrainRuntimeSet? Owner);

internal readonly record struct TerrainDisplacedOwner(
    int CellIndex,
    TerrainRuntimeSet Owner);

internal sealed class TerrainCumulativeIntent
{
    private readonly Dictionary<int, TerrainRuntimeSet?> _owners = new();

    internal int Count => _owners.Count;

    internal bool TryGetOwner(int cellIndex, out TerrainRuntimeSet? owner) =>
        _owners.TryGetValue(cellIndex, out owner);

    internal void Publish(IReadOnlyList<TerrainCellIntent> additions)
    {
        ArgumentNullException.ThrowIfNull(additions);
        foreach (var addition in additions)
        {
            _owners[addition.CellIndex] = addition.Owner;
        }
    }
}

internal sealed class TerrainDisplacedOwners
{
    private readonly Dictionary<int, TerrainRuntimeSet> _owners = new();

    internal int Count => _owners.Count;

    internal bool TryGetFirst(int cellIndex, out TerrainRuntimeSet owner)
    {
        if (_owners.TryGetValue(cellIndex, out var value))
        {
            owner = value;
            return true;
        }

        owner = null!;
        return false;
    }

    internal void Publish(IReadOnlyList<TerrainDisplacedOwner> additions)
    {
        ArgumentNullException.ThrowIfNull(additions);
        foreach (var addition in additions)
        {
            _owners.TryAdd(addition.CellIndex, addition.Owner);
        }
    }
}

internal readonly record struct TerrainPatchCell(
    int CellIndex,
    MapTileLayer Target);

internal sealed class TerrainMapPatch
{
    private readonly IReadOnlyList<TerrainPatchCell> _cells;

    internal IReadOnlyList<TerrainPatchCell> Cells => _cells;

    internal TerrainMapPatch(IEnumerable<TerrainPatchCell> cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        var list = new List<TerrainPatchCell>();
        foreach (var cell in cells)
        {
            list.Add(cell);
        }
        _cells = Array.AsReadOnly(list.ToArray());
    }
}

internal readonly record struct TerrainStrokeResolveRequest(
    MapDocument Document,
    int LayerIndex,
    TerrainMapResolver Resolver,
    TerrainRuntimeSet SelectedTerrain,
    TerrainEditMode Mode,
    TerrainCumulativeIntent CumulativeIntent,
    TerrainDisplacedOwners DisplacedOwners,
    IReadOnlyList<int> NewlyVisitedIndices);

internal sealed class TerrainStrokeResolution
{
    private TerrainStrokeResolution(
        bool succeeded,
        TerrainMapPatch? patch,
        IReadOnlyList<TerrainCellIntent> intentAdditions,
        IReadOnlyList<TerrainDisplacedOwner> displacedOwnerAdditions,
        int inspectedCellCount,
        TerrainEditFailure? failure)
    {
        Succeeded = succeeded;
        Patch = patch;
        IntentAdditions = intentAdditions;
        DisplacedOwnerAdditions = displacedOwnerAdditions;
        InspectedCellCount = inspectedCellCount;
        Failure = failure;
    }

    internal bool Succeeded { get; }

    internal TerrainMapPatch? Patch { get; }

    internal IReadOnlyList<TerrainCellIntent> IntentAdditions { get; }

    internal IReadOnlyList<TerrainDisplacedOwner> DisplacedOwnerAdditions { get; }

    internal int InspectedCellCount { get; }

    internal TerrainEditFailure? Failure { get; }

    internal static TerrainStrokeResolution Success(
        TerrainMapPatch patch,
        IEnumerable<TerrainCellIntent> intentAdditions,
        IEnumerable<TerrainDisplacedOwner> displacedOwnerAdditions,
        int inspectedCellCount)
    {
        ArgumentNullException.ThrowIfNull(patch);
        return new TerrainStrokeResolution(
            true,
            patch,
            Copy(intentAdditions),
            Copy(displacedOwnerAdditions),
            inspectedCellCount,
            null);
    }

    internal static TerrainStrokeResolution Failed(TerrainEditFailure failure, int inspectedCellCount)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new TerrainStrokeResolution(
            false,
            null,
            Array.Empty<TerrainCellIntent>(),
            Array.Empty<TerrainDisplacedOwner>(),
            inspectedCellCount,
            failure);
    }

    private static IReadOnlyList<T> Copy<T>(IEnumerable<T> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var items = new List<T>();
        foreach (var item in source)
        {
            items.Add(item);
        }

        return Array.AsReadOnly(items.ToArray());
    }
}

internal static class TerrainStrokeResolver
{
    private static readonly (int Bit, int Dx, int Dy)[] FourWayOffsets =
    [
        (TerrainMasks.North, 0, -1),
        (TerrainMasks.East, 1, 0),
        (TerrainMasks.South, 0, 1),
        (TerrainMasks.West, -1, 0)
    ];

    private static readonly (int Bit, int Dx, int Dy)[] EightWayOffsets =
    [
        (TerrainMasks.North, 0, -1),
        (TerrainMasks.East, 1, 0),
        (TerrainMasks.South, 0, 1),
        (TerrainMasks.West, -1, 0),
        (TerrainMasks.NorthEast, 1, -1),
        (TerrainMasks.SouthEast, 1, 1),
        (TerrainMasks.SouthWest, -1, 1),
        (TerrainMasks.NorthWest, -1, -1)
    ];

    internal static TerrainStrokeResolution Resolve(in TerrainStrokeResolveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.Document);
        ArgumentNullException.ThrowIfNull(request.Resolver);
        ArgumentNullException.ThrowIfNull(request.SelectedTerrain);
        ArgumentNullException.ThrowIfNull(request.CumulativeIntent);
        ArgumentNullException.ThrowIfNull(request.DisplacedOwners);
        ArgumentNullException.ThrowIfNull(request.NewlyVisitedIndices);

        var document = request.Document;
        var width = document.Width;
        var height = document.Height;
        var selected = request.SelectedTerrain;
        var staged = new Dictionary<int, TerrainRuntimeSet?>();
        var directEraseCells = new HashSet<int>();
        var workItems = new HashSet<(TerrainRuntimeSet Owner, int Cell)>();
        var intentAdditions = new List<TerrainCellIntent>();
        var displacedOwnerAdditions = new List<TerrainDisplacedOwner>();

        foreach (var cell in request.NewlyVisitedIndices)
        {
            var preOwner = GetConceptualOwner(in request, staged, cell);

            if (request.Mode == TerrainEditMode.Paint)
            {
                staged[cell] = selected;
                intentAdditions.Add(new TerrainCellIntent(cell, selected));
                AddWorkItems(workItems, selected, cell, width, height);
                if (preOwner is not null && !ReferenceEquals(preOwner, selected))
                {
                    displacedOwnerAdditions.Add(new TerrainDisplacedOwner(cell, preOwner));
                    AddWorkItems(workItems, preOwner, cell, width, height);
                }
            }
            else if (ReferenceEquals(preOwner, selected))
            {
                staged[cell] = null;
                directEraseCells.Add(cell);
                intentAdditions.Add(new TerrainCellIntent(cell, null));
                AddWorkItems(workItems, selected, cell, width, height);
            }
        }

        var patchCells = new SortedDictionary<int, MapTileLayer>();
        var inspectedCellCount = 0;
        TerrainEditFailure? failure = null;
        foreach (var (owner, cell) in workItems
            .OrderBy(item => item.Cell)
            .ThenBy(item => item.Owner.Id, StringComparer.Ordinal))
        {
            inspectedCellCount++;
            var finalOwner = GetConceptualOwner(in request, staged, cell);
            MapTileLayer? target = null;
            if (ReferenceEquals(finalOwner, owner))
            {
                var lookup = request.Resolver.ResolveVariant(
                    owner,
                    ComputeMask(in request, staged, owner, cell, width, height),
                    cell % width,
                    cell / width);
                if (!lookup.Succeeded)
                {
                    failure ??= lookup.Failure;
                    continue;
                }

                target = new MapTileLayer(lookup.Variant.Sheet, lookup.Variant.Graphic);
            }
            else if (finalOwner is null && directEraseCells.Contains(cell))
            {
                target = new MapTileLayer(0, 0);
            }
            else
            {
                continue;
            }

            if (target != document.GetTile(cell).GetLayer(request.LayerIndex))
            {
                patchCells[cell] = target.Value;
            }
        }

        if (failure is { } firstFailure)
        {
            return TerrainStrokeResolution.Failed(firstFailure, inspectedCellCount);
        }

        var cells = new List<TerrainPatchCell>();
        foreach (var (cell, target) in patchCells)
        {
            cells.Add(new TerrainPatchCell(cell, target));
        }

        return TerrainStrokeResolution.Success(
            new TerrainMapPatch(cells),
            intentAdditions,
            displacedOwnerAdditions,
            inspectedCellCount);
    }

    private static void AddWorkItems(
        HashSet<(TerrainRuntimeSet Owner, int Cell)> workItems,
        TerrainRuntimeSet owner,
        int cell,
        int width,
        int height)
    {
        var x = cell % width;
        var y = cell / width;
        workItems.Add((owner, cell));
        foreach (var (_, dx, dy) in Offsets(owner.Topology))
        {
            var neighborX = x + dx;
            var neighborY = y + dy;
            if (neighborX >= 0 && neighborX < width && neighborY >= 0 && neighborY < height)
            {
                workItems.Add((owner, neighborY * width + neighborX));
            }
        }
    }

    private static int ComputeMask(
        in TerrainStrokeResolveRequest request,
        Dictionary<int, TerrainRuntimeSet?> staged,
        TerrainRuntimeSet owner,
        int cell,
        int width,
        int height)
    {
        var x = cell % width;
        var y = cell / width;
        var mask = 0;
        foreach (var (bit, dx, dy) in Offsets(owner.Topology))
        {
            var neighborX = x + dx;
            var neighborY = y + dy;
            if (neighborX < 0 || neighborX >= width || neighborY < 0 || neighborY >= height)
            {
                continue;
            }

            if (ReferenceEquals(GetConceptualOwner(in request, staged, neighborY * width + neighborX), owner))
            {
                mask |= bit;
            }
        }

        return mask;
    }

    private static TerrainRuntimeSet? GetConceptualOwner(
        in TerrainStrokeResolveRequest request,
        Dictionary<int, TerrainRuntimeSet?> staged,
        int cell)
    {
        if (staged.TryGetValue(cell, out var stagedOwner))
        {
            return stagedOwner;
        }

        if (request.CumulativeIntent.TryGetOwner(cell, out var cumulativeOwner))
        {
            return cumulativeOwner;
        }

        var layer = request.Document.GetTile(cell).GetLayer(request.LayerIndex);
        if (layer.Graphic == 0)
        {
            return null;
        }

        return request.Resolver.TryGetOwner(new TerrainGraphicReference(layer.Sheet, layer.Graphic), out var owner)
            ? owner
            : null;
    }

    private static (int Bit, int Dx, int Dy)[] Offsets(TerrainTopology topology) =>
        topology == TerrainTopology.FourWay ? FourWayOffsets : EightWayOffsets;
}
