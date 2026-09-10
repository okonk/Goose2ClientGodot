using System;
using System.Collections.Generic;
using MapEditor.Core.Terrain;

namespace MapEditor.Core;

internal sealed class MapLayerChangeAccumulator
{
    private readonly SortedDictionary<int, (int X, int Y, MapTileLayer First, MapTileLayer Latest)> _entries = new();

    internal int Count => _entries.Count;

    internal bool HasNetChanges
    {
        get
        {
            foreach (var entry in _entries.Values)
            {
                if (entry.First != entry.Latest)
                {
                    return true;
                }
            }

            return false;
        }
    }

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
            if (!_entries.TryGetValue(cell.CellIndex, out var entry))
            {
                entry = (cell.CellIndex % width, cell.CellIndex / width, document.GetTile(cell.CellIndex).GetLayer(layerIndex), cell.Target);
            }
            else
            {
                entry.Latest = cell.Target;
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
