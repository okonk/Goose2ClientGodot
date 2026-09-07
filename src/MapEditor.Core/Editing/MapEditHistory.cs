using System;
using System.Collections.Generic;

namespace MapEditor.Core;

internal sealed class MapEditHistory
{
    private readonly LinkedList<MapEditCommand> _undo = new();
    private readonly LinkedList<MapEditCommand> _redo = new();
    private long _capBytes;
    private long _usedBytes;
    private long _historyVersion;

    internal MapEditHistory(long capBytes)
    {
        _capBytes = capBytes;
    }

    internal int UndoCount => _undo.Count;

    internal int RedoCount => _redo.Count;

    internal long CapBytes => _capBytes;

    internal long UsedBytes => _usedBytes;

    internal long HistoryVersion => _historyVersion;

    internal event Action? HistoryChanged;

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
        Bump();
    }

    internal void MoveUndoToRedo(MapEditCommand command)
    {
        _undo.RemoveLast();
        _redo.AddFirst(command);
        Bump();
    }

    internal void MoveRedoToUndo(MapEditCommand command)
    {
        _redo.RemoveFirst();
        _undo.AddLast(command);
        Bump();
    }

    internal void DiscardRedo()
    {
        if (_redo.Count == 0)
        {
            return;
        }

        foreach (MapEditCommand discarded in _redo)
        {
            _usedBytes -= discarded.AccountedSizeBytes;
        }

        _redo.Clear();
        Bump();
    }

    internal void Clear()
    {
        if (_undo.Count == 0 && _redo.Count == 0)
        {
            return;
        }

        foreach (MapEditCommand command in _undo)
        {
            _usedBytes -= command.AccountedSizeBytes;
        }

        foreach (MapEditCommand command in _redo)
        {
            _usedBytes -= command.AccountedSizeBytes;
        }

        _undo.Clear();
        _redo.Clear();
        Bump();
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

    private void Bump()
    {
        _historyVersion++;
        HistoryChanged?.Invoke();
    }
}
