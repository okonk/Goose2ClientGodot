using System;
using System.Collections;
using System.Collections.Generic;
using MapEditor.GameData.Rows;

namespace MapEditor.GameData.Editing;

public sealed class SheetEditSession
{
    private readonly List<NpcSpawnRow> _spawns;
    private readonly List<WarpRow> _warps;
    private readonly IReadOnlyList<NpcSpawnRow> _spawnsView;
    private readonly IReadOnlyList<WarpRow> _warpsView;
    private readonly LinkedList<SheetEditCommand> _undo = new();
    private readonly LinkedList<SheetEditCommand> _redo = new();
    private int _currentStateId;
    private int _pushedStateId;
    private int _nextStateId = 1;

    public SheetEditSession(IEnumerable<NpcSpawnRow> spawns, IEnumerable<WarpRow> warps)
    {
        _spawns = new List<NpcSpawnRow>(spawns ?? throw new ArgumentNullException(nameof(spawns)));
        _warps = new List<WarpRow>(warps ?? throw new ArgumentNullException(nameof(warps)));
        // ReadOnlyCollection<T> implements IList<T> and would stay castable to a mutable interface.
        _spawnsView = new LiveReadOnlyView<NpcSpawnRow>(_spawns);
        _warpsView = new LiveReadOnlyView<WarpRow>(_warps);
    }

    public IReadOnlyList<NpcSpawnRow> Spawns => _spawnsView;

    public IReadOnlyList<WarpRow> Warps => _warpsView;

    public bool IsDirty => _currentStateId != _pushedStateId;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public void AddSpawn(NpcSpawnRow row)
    {
        int beforeStateId = _currentStateId;
        int afterStateId = _nextStateId++;
        _spawns.Add(row);
        _currentStateId = afterStateId;
        Push(new SheetAddCommand<NpcSpawnRow>(_spawns, row, beforeStateId, afterStateId));
    }

    public void RemoveSpawnAt(int index)
    {
        NpcSpawnRow row = _spawns[index];
        int beforeStateId = _currentStateId;
        int afterStateId = _nextStateId++;
        _spawns.RemoveAt(index);
        _currentStateId = afterStateId;
        Push(new SheetRemoveCommand<NpcSpawnRow>(_spawns, index, row, beforeStateId, afterStateId));
    }

    public void MoveSpawn(int index, int mapX, int mapY)
    {
        NpcSpawnRow before = _spawns[index];
        if (before.MapX == mapX && before.MapY == mapY)
        {
            return;
        }

        NpcSpawnRow after = before with { MapX = mapX, MapY = mapY };
        int beforeStateId = _currentStateId;
        int afterStateId = _nextStateId++;
        _spawns[index] = after;
        _currentStateId = afterStateId;
        Push(new SheetMoveCommand<NpcSpawnRow>(_spawns, index, before, after, beforeStateId, afterStateId));
    }

    public void AddWarp(WarpRow row)
    {
        int beforeStateId = _currentStateId;
        int afterStateId = _nextStateId++;
        _warps.Add(row);
        _currentStateId = afterStateId;
        Push(new SheetAddCommand<WarpRow>(_warps, row, beforeStateId, afterStateId));
    }

    public void RemoveWarpAt(int index)
    {
        WarpRow row = _warps[index];
        int beforeStateId = _currentStateId;
        int afterStateId = _nextStateId++;
        _warps.RemoveAt(index);
        _currentStateId = afterStateId;
        Push(new SheetRemoveCommand<WarpRow>(_warps, index, row, beforeStateId, afterStateId));
    }

    public void MoveWarpSource(int index, int mapX, int mapY)
    {
        WarpRow before = _warps[index];
        if (before.MapX == mapX && before.MapY == mapY)
        {
            return;
        }

        WarpRow after = before with { MapX = mapX, MapY = mapY };
        int beforeStateId = _currentStateId;
        int afterStateId = _nextStateId++;
        _warps[index] = after;
        _currentStateId = afterStateId;
        Push(new SheetMoveCommand<WarpRow>(_warps, index, before, after, beforeStateId, afterStateId));
    }

    public bool Undo()
    {
        if (_undo.Count == 0)
        {
            return false;
        }

        SheetEditCommand command = _undo.Last!.Value;
        _undo.RemoveLast();
        command.Replay(reverse: true);
        _currentStateId = command.BeforeStateId;
        _redo.AddFirst(command);
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0)
        {
            return false;
        }

        SheetEditCommand command = _redo.First!.Value;
        _redo.RemoveFirst();
        command.Replay(reverse: false);
        _currentStateId = command.AfterStateId;
        _undo.AddLast(command);
        return true;
    }

    public void MarkPushed()
    {
        _pushedStateId = _currentStateId;
    }

    private void Push(SheetEditCommand command)
    {
        _redo.Clear();
        _undo.AddLast(command);
    }

    private sealed class LiveReadOnlyView<T> : IReadOnlyList<T>
    {
        private readonly List<T> _list;

        public LiveReadOnlyView(List<T> list) => _list = list;

        public T this[int index] => _list[index];

        public int Count => _list.Count;

        public IEnumerator<T> GetEnumerator() => _list.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => _list.GetEnumerator();
    }
}
