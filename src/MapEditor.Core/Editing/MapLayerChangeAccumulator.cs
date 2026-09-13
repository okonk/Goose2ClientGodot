using System;
using System.Collections.Generic;

namespace MapEditor.Core;

internal sealed class MapLayerChangeAccumulator
{
    private readonly MapDocument _document;
    private readonly int _layerIndex;
    private readonly SortedDictionary<int, CellChange> _cells = new();

    private readonly struct CellChange
    {
        public readonly MapTileLayer First;
        public readonly MapTileLayer Latest;

        public CellChange(MapTileLayer first, MapTileLayer latest)
        {
            First = first;
            Latest = latest;
        }
    }

    internal MapLayerChangeAccumulator(MapDocument document, int layerIndex, int tileCount)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (layerIndex < 0 || layerIndex >= MapDocument.LayerCount)
        {
            throw new ArgumentOutOfRangeException(nameof(layerIndex));
        }

        if (tileCount < 0 || tileCount != document.TileCount)
        {
            throw new ArgumentOutOfRangeException(nameof(tileCount));
        }

        _document = document;
        _layerIndex = layerIndex;
    }

    internal int LayerIndex => _layerIndex;

    internal int Count => _cells.Count;

    internal bool HasChanges => _cells.Count > 0;

    internal long RetainedBytes => checked(MapEditCommand.BaseCommandBytes + _cells.Count * MapEditCommand.LayerSlotBytes);

    internal void Record(int index, MapTileLayer before)
    {
        if (index < 0 || index >= _document.TileCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        var latest = _document.GetTile(index).GetLayer(_layerIndex);
        if (_cells.TryGetValue(index, out var cell))
        {
            if (latest == cell.First)
            {
                _cells.Remove(index);
            }
            else
            {
                _cells[index] = new CellChange(cell.First, latest);
            }
        }
        else if (latest != before)
        {
            _cells[index] = new CellChange(before, latest);
        }
    }

    internal void Restore()
    {
        foreach (var (index, cell) in _cells)
        {
            _document.SetLayer(index % _document.Width, index / _document.Width, _layerIndex, cell.First);
        }

        _cells.Clear();
    }

    internal List<MapLayerChange> BuildChanges()
    {
        var changes = new List<MapLayerChange>(_cells.Count);
        foreach (var (index, cell) in _cells)
        {
            changes.Add(new MapLayerChange(index % _document.Width, index / _document.Width, _layerIndex, cell.First, cell.Latest));
        }

        return changes;
    }
}
