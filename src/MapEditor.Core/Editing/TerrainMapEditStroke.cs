using System.Collections.Generic;
using MapEditor.Core.Terrain;

namespace MapEditor.Core;

internal sealed class TerrainMapEditStroke
{
    private readonly TerrainMapResolver _resolver;
    private readonly TerrainRuntimeSet _terrain;
    private readonly TerrainEditMode _mode;
    private readonly int _layerIndex;
    private MapCoordinate _sample;
    private StrokeVisitBitmap? _visited;
    private TerrainCumulativeIntent? _intent;
    private TerrainDisplacedOwners? _displacedOwners;
    private MapLayerChangeAccumulator? _accumulator;
    private long _totalInspectedCellCount;

    internal TerrainMapEditStroke(
        TerrainMapResolver resolver,
        TerrainRuntimeSet terrain,
        TerrainEditMode mode,
        int layerIndex,
        int tileCount)
    {
        _resolver = resolver;
        _terrain = terrain;
        _mode = mode;
        _layerIndex = layerIndex;
        _visited = new StrokeVisitBitmap(tileCount);
        _intent = new TerrainCumulativeIntent();
        _displacedOwners = new TerrainDisplacedOwners();
        _accumulator = new MapLayerChangeAccumulator();
    }

    internal int LayerIndex => _layerIndex;

    internal MapCoordinate PreviousSample => _sample;

    internal StrokeVisitBitmap Visited => _visited!;

    internal int IntentCount => _intent?.Count ?? 0;

    internal int DisplacedOwnerCount => _displacedOwners?.Count ?? 0;

    internal int AccumulatedCellCount => _accumulator?.Count ?? 0;

    internal long TotalInspectedCellCount => _totalInspectedCellCount;

    internal bool HasNetChanges => _accumulator is { HasNetChanges: true };

    internal TerrainStrokeUpdate ApplyFirstSample(int x, int y, MapDocument document) =>
        Apply(new MapCoordinate(x, y), new MapCoordinate(x, y), document);

    internal TerrainStrokeUpdate ApplySegment(int x, int y, MapDocument document) =>
        Apply(_sample, new MapCoordinate(x, y), document);

    internal void Restore(MapDocument document) => _accumulator!.Restore(document, _layerIndex);

    internal MapEditChangeBuffer<MapLayerChange> BuildChanges() => _accumulator!.BuildChanges(_layerIndex);

    internal void Release()
    {
        _visited = null;
        _intent = null;
        _displacedOwners = null;
        _accumulator = null;
    }

    private TerrainStrokeUpdate Apply(MapCoordinate from, MapCoordinate to, MapDocument document)
    {
        var newlyVisited = new List<int>();
        var staged = new HashSet<int>();
        foreach (var point in GridLine.Enumerate(from, to))
        {
            int index = point.Y * document.Width + point.X;
            if (!Visited.IsVisited(index) && staged.Add(index))
            {
                newlyVisited.Add(index);
            }
        }

        var request = new TerrainStrokeResolveRequest(
            document,
            _layerIndex,
            _resolver,
            _terrain,
            _mode,
            _intent!,
            _displacedOwners!,
            newlyVisited);
        TerrainStrokeResolution resolution = TerrainStrokeResolver.Resolve(in request);
        _totalInspectedCellCount += resolution.InspectedCellCount;
        if (!resolution.Succeeded)
        {
            return new TerrainStrokeUpdate(false, false, resolution.Failure);
        }

        bool changed = _accumulator!.Apply(document, _layerIndex, resolution.Patch!);
        foreach (int index in newlyVisited)
        {
            Visited.TryMark(index);
        }

        _intent!.Publish(resolution.IntentAdditions);
        _displacedOwners!.Publish(resolution.DisplacedOwnerAdditions);
        _sample = to;
        return new TerrainStrokeUpdate(true, changed, null);
    }
}
