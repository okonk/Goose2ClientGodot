using System;

namespace MapEditor.Core;

internal sealed class MapEditStroke
{
    private readonly MapEditTool _tool;
    private readonly int _layerIndex;
    private readonly MapTileLayer _brush;
    private readonly MapTileLayer _previousBrush;
    private MapCoordinate _sample;
    private StrokeVisitBitmap? _visited;
    private MapEditChangeBuffer<MapLayerChange>? _layerChanges;
    private MapEditChangeBuffer<MapFlagsChange>? _flagsChanges;

    internal MapEditStroke(MapEditTool tool, int layerIndex, MapTileLayer brush, MapTileLayer previousBrush, int tileCount)
    {
        _tool = tool;
        _layerIndex = layerIndex;
        _brush = brush;
        _previousBrush = previousBrush;
        if (tool != MapEditTool.Eyedropper)
        {
            _visited = new StrokeVisitBitmap(tileCount);
            if (tool == MapEditTool.BlockedToggle)
            {
                _flagsChanges = new MapEditChangeBuffer<MapFlagsChange>();
            }
            else
            {
                _layerChanges = new MapEditChangeBuffer<MapLayerChange>();
            }
        }
    }

    internal MapEditTool Tool => _tool;

    internal int LayerIndex => _layerIndex;

    internal MapTileLayer Brush => _brush;

    internal MapTileLayer PreviousBrush => _previousBrush;

    internal MapCoordinate PreviousSample => _sample;

    internal StrokeVisitBitmap? Visited => _visited;

    internal MapEditChangeBuffer<MapLayerChange>? LayerChanges => _layerChanges;

    internal MapEditChangeBuffer<MapFlagsChange>? FlagsChanges => _flagsChanges;

    internal bool HasDeltas => (_layerChanges?.Count ?? 0) + (_flagsChanges?.Count ?? 0) > 0;

    internal void ApplyFirstSample(int x, int y, MapDocument document)
    {
        ApplySegment(new MapCoordinate(x, y), new MapCoordinate(x, y), document);
    }

    internal void ApplySegment(MapCoordinate from, MapCoordinate to, MapDocument document)
    {
        foreach (var point in GridLine.Enumerate(from, to))
        {
            int index = point.Y * document.Width + point.X;
            if (!_visited!.TryMark(index))
            {
                continue;
            }

            switch (_tool)
            {
                case MapEditTool.Pencil:
                    ApplyLayer(point, _brush, document);
                    break;
                case MapEditTool.Eraser:
                    ApplyLayer(point, new MapTileLayer(0, 0), document);
                    break;
                case MapEditTool.BlockedToggle:
                    int oldFlags = document[point.X, point.Y].Flags;
                    int newFlags = oldFlags ^ MapDocument.BlockedFlag;
                    _flagsChanges!.Append(new MapFlagsChange(point.X, point.Y, oldFlags, newFlags));
                    document.SetFlags(point.X, point.Y, newFlags);
                    break;
            }
        }

        _sample = to;
    }

    private void ApplyLayer(MapCoordinate point, MapTileLayer target, MapDocument document)
    {
        MapTileLayer current = document[point.X, point.Y].GetLayer(_layerIndex);
        if (current != target)
        {
            _layerChanges!.Append(new MapLayerChange(point.X, point.Y, _layerIndex, current, target));
            document.SetLayer(point.X, point.Y, _layerIndex, target);
        }
    }

    internal void Release()
    {
        _visited = null;
        _layerChanges = null;
        _flagsChanges = null;
    }
}
