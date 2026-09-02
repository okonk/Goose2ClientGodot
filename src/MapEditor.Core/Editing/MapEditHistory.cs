using System.Collections.Generic;

namespace MapEditor.Core;

internal sealed class MapEditHistory
{
    private readonly LinkedList<MapEditCommand> _undo = new();
    private readonly LinkedList<MapEditCommand> _redo = new();
    private long _capBytes;
    private long _usedBytes;

    internal MapEditHistory(long capBytes)
    {
        _capBytes = capBytes;
    }

    internal int UndoCount => _undo.Count;

    internal int RedoCount => _redo.Count;

    internal long CapBytes => _capBytes;

    internal long UsedBytes => _usedBytes;

    internal MapEditCommand? PeekUndo() => _undo.Last?.Value;

    internal MapEditCommand? PeekRedo() => _redo.First?.Value;

    internal void PushUndo(MapEditCommand command)
    {
        foreach (MapEditCommand discarded in _redo)
        {
            _usedBytes -= discarded.AccountedSizeBytes;
        }

        _redo.Clear();
        _undo.AddLast(command);
        _usedBytes += command.AccountedSizeBytes;
        EvictToCap();
    }

    internal void MoveUndoToRedo(MapEditCommand command)
    {
        _undo.RemoveLast();
        _redo.AddFirst(command);
    }

    internal void MoveRedoToUndo(MapEditCommand command)
    {
        _redo.RemoveFirst();
        _undo.AddLast(command);
    }

    internal void SetCap(long capBytes)
    {
        _capBytes = capBytes;
        EvictToCap();
    }

    private void EvictToCap()
    {
        while (_usedBytes > _capBytes)
        {
            MapEditCommand? evicted = null;
            if (_undo.Count > 0)
            {
                evicted = _undo.First!.Value;
                _undo.RemoveFirst();
            }
            else
            {
                evicted = _redo.Last!.Value;
                _redo.RemoveLast();
            }

            _usedBytes -= evicted!.AccountedSizeBytes;
        }
    }
}
