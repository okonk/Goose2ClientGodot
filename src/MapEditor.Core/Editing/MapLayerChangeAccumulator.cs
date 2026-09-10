using System;
using System.Collections.Generic;
using MapEditor.Core.Terrain;

namespace MapEditor.Core;

internal sealed class MapLayerChangeAccumulator
{
    private readonly SortedDictionary<int, (int X, int Y, MapTileLayer First, MapTileLayer Latest)> _entries = new();
    private int _netChangeCount;

    internal int Count => _entries.Count;

    internal bool HasNetChanges => _netChangeCount > 0;

    internal bool Apply(
        MapDocument document,
        int layerIndex,
        TerrainMapPatch patch)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(patch);

        var width = document.Width;
        var height = document.Height;
        var cells = patch.Cells;
        var seen = new HashSet<int>(cells.Count);
        foreach (var cell in cells)
        {
            if (cell.CellIndex < 0 || cell.CellIndex >= width * height || !seen.Add(cell.CellIndex))
            {
                throw new ArgumentOutOfRangeException(nameof(patch));
            }
        }

        foreach (var cell in cells)
        {
            bool exists = _entries.TryGetValue(cell.CellIndex, out var entry);
            bool wasNetChange = exists && entry.First != entry.Latest;
            if (!exists)
            {
                entry = (cell.CellIndex % width, cell.CellIndex / width, document.GetTile(cell.CellIndex).GetLayer(layerIndex), cell.Target);
            }
            else
            {
                entry.Latest = cell.Target;
            }

            bool isNetChange = entry.First != entry.Latest;
            if (wasNetChange != isNetChange)
            {
                _netChangeCount += isNetChange ? 1 : -1;
            }

            _entries[cell.CellIndex] = entry;
        }

        var changed = false;
        foreach (var cell in cells)
        {
            if (cell.Target != document.GetTile(cell.CellIndex).GetLayer(layerIndex))
            {
                changed = true;
            }

            document.SetLayer(cell.CellIndex % width, cell.CellIndex / width, layerIndex, cell.Target);
        }

        return changed;
    }

    internal void Restore(MapDocument document, int layerIndex)
    {
        ArgumentNullException.ThrowIfNull(document);
        foreach (var entry in _entries)
        {
            document.SetLayer(entry.Value.X, entry.Value.Y, layerIndex, entry.Value.First);
        }
    }

    internal MapEditChangeBuffer<MapLayerChange> BuildChanges(int layerIndex)
    {
        var buffer = new MapEditChangeBuffer<MapLayerChange>();
        foreach (var entry in _entries)
        {
            if (entry.Value.First == entry.Value.Latest)
            {
                continue;
            }

            buffer.Append(new MapLayerChange(entry.Value.X, entry.Value.Y, layerIndex, entry.Value.First, entry.Value.Latest));
        }

        return buffer;
    }
}
