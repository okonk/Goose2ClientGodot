using System.Collections.Generic;

namespace MapEditor.Core;

internal sealed class MapEditHistory
{
    private readonly List<MapEditCommand> _undo = new();
    private readonly List<MapEditCommand> _redo = new();

    internal int UndoCount => _undo.Count;

    internal int RedoCount => _redo.Count;

    internal MapEditCommand? PeekUndo() => _undo.Count > 0 ? _undo[^1] : null;

    internal MapEditCommand? PeekRedo() => _redo.Count > 0 ? _redo[^1] : null;

    internal void PushUndo(MapEditCommand command)
    {
        _undo.Add(command);
        _redo.Clear();
    }

    internal void MoveUndoToRedo(MapEditCommand command)
    {
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(command);
    }

    internal void MoveRedoToUndo(MapEditCommand command)
    {
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(command);
    }
}
