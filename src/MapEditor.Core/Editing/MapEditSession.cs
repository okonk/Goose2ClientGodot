using System;
using System.Collections.Generic;

namespace MapEditor.Core;

public sealed class MapEditSession
{
    public const long DefaultRetainedHistoryCapBytes = 64 * 1024 * 1024;

    private readonly MapDocument _document;
    private readonly MapEditHistory _history;
    private MapEditStroke? _stroke;
    private byte _selectedLayers = 1;
    private MapTileLayer _selectedTileLayer;
    private int _currentStateId;
    private int? _savedStateId;
    private int _nextStateId = 1;

    public MapEditSession(MapDocument document, bool initiallyDirty = false, long retainedHistoryCapBytes = DefaultRetainedHistoryCapBytes)
    {
        if (retainedHistoryCapBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(retainedHistoryCapBytes));
        }

        _document = document ?? throw new ArgumentNullException(nameof(document));
        _savedStateId = initiallyDirty ? null : 0;
        _history = new MapEditHistory(retainedHistoryCapBytes);
    }

    public MapDocument Document => _document;

    public long RetainedHistoryCapBytes => _history.CapBytes;

    public long RetainedHistoryUsedBytes => _history.UsedBytes;

    public byte SelectedLayers
    {
        get => _selectedLayers;
        set
        {
            if (value == 0 || value >= 1 << MapDocument.LayerCount)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            _selectedLayers = value;
        }
    }

    public int TopLayer
    {
        get
        {
            for (int layer = MapDocument.LayerCount - 1; layer >= 0; layer--)
            {
                if ((_selectedLayers & (1 << layer)) != 0)
                {
                    return layer;
                }
            }

            return -1;
        }
    }

    public MapTileLayer SelectedTileLayer
    {
        get => _selectedTileLayer;
        set => _selectedTileLayer = value;
    }

    public bool HasActiveStroke => _stroke != null;

    public event Action<MapResizeTransform>? Resized;

    public bool CanUndo => _stroke == null && _history.UndoCount > 0;

    public bool CanRedo => _stroke == null && _history.RedoCount > 0;

    public bool IsDirty
    {
        get
        {
            if (_stroke is { HasDeltas: true })
            {
                return true;
            }

            if (_savedStateId == null)
            {
                return true;
            }

            return _currentStateId != _savedStateId;
        }
    }

    public void BeginStroke(MapEditTool tool, int x, int y)
    {
        ValidateTool(tool);
        ValidateCoordinate(x, y);
        if (_stroke != null)
        {
            throw new InvalidOperationException();
        }

        _stroke = new MapEditStroke(tool, TopLayer, _selectedTileLayer, _selectedTileLayer, _document.TileCount);
        if (tool == MapEditTool.Eyedropper)
        {
            _selectedTileLayer = _document[x, y].GetLayer(TopLayer);
        }
        else
        {
            _stroke.ApplyFirstSample(x, y, _document);
        }
    }

    public void ContinueStroke(int x, int y)
    {
        MapEditStroke stroke = _stroke ?? throw new InvalidOperationException();
        ValidateCoordinate(x, y);
        if (stroke.Tool == MapEditTool.Eyedropper)
        {
            _selectedTileLayer = _document[x, y].GetLayer(stroke.LayerIndex);
            return;
        }

        stroke.ApplySegment(stroke.PreviousSample, new MapCoordinate(x, y), _document);
    }

    public bool CompleteStroke()
    {
        MapEditStroke stroke = _stroke ?? throw new InvalidOperationException();
        _stroke = null;
        if (!stroke.HasDeltas)
        {
            stroke.Release();
            return false;
        }

        if (stroke.LayerChanges is { } layerChanges)
        {
            PushLayerCommand(layerChanges);
        }
        else
        {
            int beforeStateId = _currentStateId;
            int afterStateId = _nextStateId++;
            _currentStateId = afterStateId;
            _history.PushUndo(new MapFlagsChangesCommand(stroke.FlagsChanges!, beforeStateId, afterStateId));
        }

        stroke.Release();
        return true;
    }

    public bool ApplyFloodFill(int x, int y)
    {
        ValidateCoordinate(x, y);
        if (_stroke != null)
        {
            throw new InvalidOperationException();
        }

        int layer = TopLayer;
        MapTileLayer target = _selectedTileLayer;
        MapTileLayer startValue = _document[x, y].GetLayer(layer);
        if (startValue == target)
        {
            return false;
        }

        int width = _document.Width;
        int height = _document.Height;
        var visited = new StrokeVisitBitmap(_document.TileCount);
        var changes = new MapEditChangeBuffer<MapLayerChange>();
        var frontier = new Queue<int>();
        int start = y * width + x;
        visited.TryMark(start);
        frontier.Enqueue(start);
        while (frontier.Count > 0)
        {
            int index = frontier.Dequeue();
            int cx = index % width;
            int cy = index / width;
            changes.Append(new MapLayerChange(cx, cy, layer, startValue, target));
            _document.SetLayer(cx, cy, layer, target);

            if (cx > 0)
            {
                TryVisit(index - 1);
            }

            if (cx < width - 1)
            {
                TryVisit(index + 1);
            }

            if (cy > 0)
            {
                TryVisit(index - width);
            }

            if (cy < height - 1)
            {
                TryVisit(index + width);
            }
        }

        PushLayerCommand(changes);
        return true;

        void TryVisit(int ni)
        {
            if (!visited.TryMark(ni))
            {
                return;
            }

            if (_document[ni % width, ni / width].GetLayer(layer) != startValue)
            {
                return;
            }

            frontier.Enqueue(ni);
        }
    }

    public bool ApplyLayerPatch(int originX, int originY, int width, int height, MapTileLayer[]?[] layerTiles)
    {
        if (_stroke != null)
        {
            throw new InvalidOperationException();
        }

        if (layerTiles is null || layerTiles.Length != MapDocument.LayerCount)
        {
            throw new ArgumentException(nameof(layerTiles));
        }

        if (width <= 0 || height <= 0 || originX < 0 || originY < 0 ||
            width > _document.Width - originX || height > _document.Height - originY)
        {
            throw new ArgumentOutOfRangeException();
        }

        for (int layer = 0; layer < MapDocument.LayerCount; layer++)
        {
            if (layerTiles[layer] is { } tiles && tiles.Length != width * height)
            {
                throw new ArgumentException(nameof(layerTiles));
            }
        }

        var changes = new MapEditChangeBuffer<MapLayerChange>();
        for (int layer = 0; layer < MapDocument.LayerCount; layer++)
        {
            if (layerTiles[layer] is not { } tiles)
            {
                continue;
            }

            for (int row = 0; row < height; row++)
            {
                for (int col = 0; col < width; col++)
                {
                    int x = originX + col;
                    int y = originY + row;
                    MapTileLayer target = tiles[row * width + col];
                    MapTileLayer current = _document[x, y].GetLayer(layer);
                    if (current != target)
                    {
                        changes.Append(new MapLayerChange(x, y, layer, current, target));
                        _document.SetLayer(x, y, layer, target);
                    }
                }
            }
        }

        if (changes.Count == 0)
        {
            return false;
        }

        PushLayerCommand(changes);
        return true;
    }

    public bool ApplyResize(MapTileRectangle window)
    {
        if (_stroke != null)
        {
            throw new InvalidOperationException();
        }

        long width = window.Width;
        long height = window.Height;
        long x = window.X;
        long y = window.Y;
        if (width < MapDocument.MinDimension || width > MapDocument.MaxDimension ||
            height < MapDocument.MinDimension || height > MapDocument.MaxDimension ||
            x < -MapDocument.MaxDimension || x > MapDocument.MaxDimension ||
            y < -MapDocument.MaxDimension || y > MapDocument.MaxDimension)
        {
            throw new ArgumentOutOfRangeException(nameof(window));
        }

        int oldWidth = _document.Width;
        int oldHeight = _document.Height;
        if (window.X == 0 && window.Y == 0 && window.Width == oldWidth && window.Height == oldHeight)
        {
            return false;
        }

        int beforeStateId = _currentStateId;
        int afterStateId = _nextStateId++;
        var command = new MapResizeCommand(window, oldWidth, oldHeight, beforeStateId, afterStateId);

        for (int tileY = 0; tileY < oldHeight; tileY++)
        {
            for (int tileX = 0; tileX < oldWidth; tileX++)
            {
                if (tileX >= window.X && tileX < window.X + window.Width &&
                    tileY >= window.Y && tileY < window.Y + window.Height)
                {
                    continue;
                }

                MapTile tile = _document[tileX, tileY];
                if (!tile.IsEmpty)
                {
                    command.AppendSnapshot(new MapTileSnapshot(tileX, tileY, tile));
                }
            }
        }

        _document.ResizeTo(window);
        _currentStateId = afterStateId;
        _history.PushUndo(command);
        Resized?.Invoke(command.TransformFor(reverse: false));
        return true;
    }

    public void CancelStroke()
    {
        MapEditStroke stroke = _stroke ?? throw new InvalidOperationException();
        _stroke = null;
        if (stroke.LayerChanges is { } layerChanges)
        {
            new MapLayerChangesCommand(layerChanges, 0, 0).Replay(_document, reverse: true, before: true);
        }
        else if (stroke.FlagsChanges is { } flagsChanges)
        {
            new MapFlagsChangesCommand(flagsChanges, 0, 0).Replay(_document, reverse: true, before: true);
        }

        if (stroke.Tool == MapEditTool.Eyedropper)
        {
            _selectedTileLayer = stroke.PreviousBrush;
        }

        stroke.Release();
    }

    public bool Undo()
    {
        if (_stroke != null)
        {
            throw new InvalidOperationException();
        }

        MapEditCommand? command = _history.PeekUndo();
        if (command == null)
        {
            return false;
        }

        command.Replay(_document, reverse: true, before: true);
        _currentStateId = command.BeforeStateId;
        _history.MoveUndoToRedo(command);
        if (command is MapResizeCommand resize)
        {
            Resized?.Invoke(resize.TransformFor(reverse: true));
        }
        return true;
    }

    public bool Redo()
    {
        if (_stroke != null)
        {
            throw new InvalidOperationException();
        }

        MapEditCommand? command = _history.PeekRedo();
        if (command == null)
        {
            return false;
        }

        command.Replay(_document, reverse: false, before: false);
        _currentStateId = command.AfterStateId;
        _history.MoveRedoToUndo(command);
        if (command is MapResizeCommand resize)
        {
            Resized?.Invoke(resize.TransformFor(reverse: false));
        }
        return true;
    }

    public void MarkSaved()
    {
        if (_stroke != null)
        {
            throw new InvalidOperationException();
        }

        _savedStateId = _currentStateId;
    }

    public void SetRetainedHistoryCap(long bytes)
    {
        if (bytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bytes));
        }

        if (_stroke != null)
        {
            throw new InvalidOperationException();
        }

        _history.SetCap(bytes);
    }

    internal MapEditHistory History => _history;

    internal MapEditStroke? ActiveStroke => _stroke;

    internal int CurrentStateId => _currentStateId;

    internal int? SavedStateId => _savedStateId;

    private void PushLayerCommand(MapEditChangeBuffer<MapLayerChange> changes)
    {
        int beforeStateId = _currentStateId;
        int afterStateId = _nextStateId++;
        _currentStateId = afterStateId;
        _history.PushUndo(new MapLayerChangesCommand(changes, beforeStateId, afterStateId));
    }

    private static void ValidateTool(MapEditTool tool)
    {
        if (tool < MapEditTool.Pencil || tool > MapEditTool.BlockedToggle)
        {
            throw new ArgumentOutOfRangeException(nameof(tool));
        }
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
