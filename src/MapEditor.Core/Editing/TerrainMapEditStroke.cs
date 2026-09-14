using System;
using System.Collections.Generic;

namespace MapEditor.Core;

internal sealed class TerrainMapEditStroke
{
    private readonly MapDocument _document;
    private readonly ITerrainPatchResolver _resolver;
    private readonly int _layerIndex;
    private readonly Guid _terrainId;
    private readonly TerrainEditMode _mode;
    private readonly StrokeVisitBitmap _visited;
    private readonly MapLayerChangeAccumulator _accumulator;
    private Dictionary<int, Guid?> _centerOverrides;
    private MapCoordinate _sample;
    private bool _active;

    internal TerrainMapEditStroke(MapDocument document, ITerrainPatchResolver resolver, int layerIndex, Guid terrainId, TerrainEditMode mode)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(resolver);
        if (layerIndex < 0 || layerIndex >= MapDocument.LayerCount)
        {
            throw new ArgumentOutOfRangeException(nameof(layerIndex));
        }

        _document = document;
        _resolver = resolver;
        _layerIndex = layerIndex;
        _terrainId = terrainId;
        _mode = mode;
        _visited = new StrokeVisitBitmap(document.TileCount);
        _accumulator = new MapLayerChangeAccumulator(document, layerIndex, document.TileCount);
        _centerOverrides = new Dictionary<int, Guid?>();
    }

    internal ITerrainPatchResolver Resolver => _resolver;

    internal int LayerIndex => _layerIndex;

    internal Guid TerrainId => _terrainId;

    internal TerrainEditMode Mode => _mode;

    internal bool IsActive => _active;

    internal bool HasChanges => _accumulator.HasChanges;

    internal MapCoordinate PreviousSample => _sample;

    internal int IntentCount => _centerOverrides.Count;

    internal bool IsVisited(int index) => _visited.IsVisited(index);

    internal bool TryGetIntent(int index, out Guid? center) => _centerOverrides.TryGetValue(index, out center);

    internal TerrainEditResult Begin(int x, int y)
    {
        if (_active)
        {
            throw new InvalidOperationException();
        }

        ValidateCoordinate(x, y);
        var result = ApplySegment(new MapCoordinate(x, y), new MapCoordinate(x, y));
        if (result.IsActive)
        {
            _active = true;
        }

        return result;
    }

    internal TerrainEditResult Continue(int x, int y)
    {
        if (!_active)
        {
            throw new InvalidOperationException();
        }

        if (x < 0 || x >= _document.Width || y < 0 || y >= _document.Height)
        {
            return new TerrainEditResult(true, false, null);
        }

        return ApplySegment(_sample, new MapCoordinate(x, y));
    }

    internal List<MapLayerChange> Complete()
    {
        if (!_active)
        {
            throw new InvalidOperationException();
        }

        _active = false;
        return _accumulator.BuildChanges();
    }

    internal void Cancel()
    {
        if (!_active)
        {
            throw new InvalidOperationException();
        }

        _active = false;
        _accumulator.Restore();
        _centerOverrides.Clear();
        _visited.Clear();
    }

    private TerrainEditResult ApplySegment(MapCoordinate from, MapCoordinate to)
    {
        var staged = new List<(int Index, Guid? Center)>();
        foreach (var point in GridLine.Enumerate(from, to))
        {
            var index = point.Y * _document.Width + point.X;
            if (!_visited.TryMark(index))
            {
                continue;
            }

            if (_mode == TerrainEditMode.Paint)
            {
                staged.Add((index, _terrainId));
            }
            else if (_resolver.GetLogicalCenter(_document.GetTile(index).GetLayer(_layerIndex)) is not null)
            {
                staged.Add((index, null));
            }
        }

        var result = new TerrainEditResult(true, false, null);
        if (staged.Count == 0)
        {
            _sample = to;
            return result;
        }

        foreach (var (index, center) in staged)
        {
            _centerOverrides[index] = center;
        }

        if (!_resolver.TryResolveStaged(_document, _layerIndex, _centerOverrides, staged, out var patch, out var failure))
        {
            _accumulator.Restore();
            _centerOverrides.Clear();
            _visited.Clear();
            _active = false;
            return new TerrainEditResult(false, false, failure);
        }

        var changed = false;
        foreach (var (index, tile) in patch.Changes)
        {
            var before = _document.GetTile(index).GetLayer(_layerIndex);
            if (before == tile)
            {
                continue;
            }

            _document.SetLayer(index % _document.Width, index / _document.Width, _layerIndex, tile);
            _accumulator.Record(index, before);
            changed = true;
        }

        _sample = to;
        return new TerrainEditResult(true, changed, null);
    }

    private void ValidateCoordinate(int x, int y)
    {
        if (x < 0 || x >= _document.Width)
        {
            throw new ArgumentOutOfRangeException(nameof(x));
        }

        if (y < 0 || y >= _document.Height)
        {
            throw new ArgumentOutOfRangeException(nameof(y));
        }
    }
}
