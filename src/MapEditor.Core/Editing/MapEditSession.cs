using System;

namespace MapEditor.Core;

public sealed class MapEditSession
{
    private readonly MapDocument _document;
    private readonly MapEditHistory _history = new();
    private MapEditStroke? _stroke;
    private int _activeLayer;
    private MapTileLayer _selectedTileLayer;
    private int _currentStateId;
    private int? _savedStateId;
    private int _nextStateId = 1;

    public MapEditSession(MapDocument document, bool initiallyDirty = false)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _savedStateId = initiallyDirty ? null : 0;
    }

    public MapDocument Document => _document;

    public int ActiveLayer
    {
        get => _activeLayer;
        set
        {
            if (value < 0 || value >= MapDocument.LayerCount)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            _activeLayer = value;
        }
    }

    public MapTileLayer SelectedTileLayer
    {
        get => _selectedTileLayer;
        set => _selectedTileLayer = value;
    }

    public bool HasActiveStroke => _stroke != null;

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

        _stroke = new MapEditStroke(tool, _activeLayer, _selectedTileLayer, _selectedTileLayer, _document.TileCount);
        if (tool == MapEditTool.Eyedropper)
        {
            _selectedTileLayer = _document[x, y].GetLayer(_activeLayer);
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

        int beforeStateId = _currentStateId;
        int afterStateId = _nextStateId++;
        MapEditCommand command = stroke.LayerChanges is { } layerChanges
            ? MapEditCommand.ForLayerChanges(layerChanges, beforeStateId, afterStateId)
            : MapEditCommand.ForFlagsChanges(stroke.FlagsChanges!, beforeStateId, afterStateId);
        _history.PushUndo(command);
        _currentStateId = afterStateId;
        stroke.Release();
        return true;
    }

    public void CancelStroke()
    {
        MapEditStroke stroke = _stroke ?? throw new InvalidOperationException();
        _stroke = null;
        if (stroke.LayerChanges is { } layerChanges)
        {
            ReplayLayerChanges(layerChanges, _document, reverse: true, before: true);
        }
        else if (stroke.FlagsChanges is { } flagsChanges)
        {
            ReplayFlagsChanges(flagsChanges, _document, reverse: true, before: true);
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

        ReplayCommand(command, reverse: true, before: true);
        _currentStateId = command.BeforeStateId;
        _history.MoveUndoToRedo(command);
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

        ReplayCommand(command, reverse: false, before: false);
        _currentStateId = command.AfterStateId;
        _history.MoveRedoToUndo(command);
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

    internal MapEditHistory History => _history;

    internal MapEditStroke? ActiveStroke => _stroke;

    internal int CurrentStateId => _currentStateId;

    internal int? SavedStateId => _savedStateId;

    private void ReplayCommand(MapEditCommand command, bool reverse, bool before)
    {
        if (command.LayerChanges is { } layerChanges)
        {
            ReplayLayerChanges(layerChanges, _document, reverse, before);
        }
        else
        {
            ReplayFlagsChanges(command.FlagsChanges!, _document, reverse, before);
        }
    }

    private static void ReplayLayerChanges(MapEditChangeBuffer<MapLayerChange> buffer, MapDocument document, bool reverse, bool before)
    {
        for (int s = reverse ? buffer.SegmentCount - 1 : 0; reverse ? s >= 0 : s < buffer.SegmentCount; s += reverse ? -1 : 1)
        {
            MapLayerChange[] segment = buffer.GetSegment(s);
            int length = buffer.GetSegmentLength(s);
            for (int i = reverse ? length - 1 : 0; reverse ? i >= 0 : i < length; i += reverse ? -1 : 1)
            {
                MapLayerChange change = segment[i];
                document.SetLayer(change.X, change.Y, change.LayerIndex, before ? change.Before : change.After);
            }
        }
    }

    private static void ReplayFlagsChanges(MapEditChangeBuffer<MapFlagsChange> buffer, MapDocument document, bool reverse, bool before)
    {
        for (int s = reverse ? buffer.SegmentCount - 1 : 0; reverse ? s >= 0 : s < buffer.SegmentCount; s += reverse ? -1 : 1)
        {
            MapFlagsChange[] segment = buffer.GetSegment(s);
            int length = buffer.GetSegmentLength(s);
            for (int i = reverse ? length - 1 : 0; reverse ? i >= 0 : i < length; i += reverse ? -1 : 1)
            {
                MapFlagsChange change = segment[i];
                document.SetFlags(change.X, change.Y, before ? change.BeforeFlags : change.AfterFlags);
            }
        }
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
