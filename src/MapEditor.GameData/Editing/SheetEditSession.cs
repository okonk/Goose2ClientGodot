using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
    private long _historyVersion;

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

    public long HistoryVersion => _historyVersion;

    public event Action? HistoryChanged;

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

    public void UpdateSpawn(int index, NpcSpawnRow row)
    {
        NpcSpawnRow before = _spawns[index];
        if (before == row)
        {
            return;
        }

        int beforeStateId = _currentStateId;
        int afterStateId = _nextStateId++;
        _spawns[index] = row;
        _currentStateId = afterStateId;
        Push(new SheetUpdateCommand<NpcSpawnRow>(_spawns, index, before, row, beforeStateId, afterStateId));
    }

    public void ReplaceSpawns(IReadOnlyList<NpcSpawnRow> rows)
    {
        if (rows is null)
        {
            throw new ArgumentNullException(nameof(rows));
        }

        NpcSpawnRow[] before = _spawns.ToArray();
        NpcSpawnRow[] after = rows.ToArray();
        if (RowsEqual(before, after))
        {
            return;
        }

        int beforeStateId = _currentStateId;
        int afterStateId = _nextStateId++;
        _spawns.Clear();
        _spawns.AddRange(after);
        _currentStateId = afterStateId;
        Push(new SheetBulkCommand<NpcSpawnRow>(_spawns, before, after, beforeStateId, afterStateId));
    }

    public void TransformSpawns(Func<NpcSpawnRow, NpcSpawnRow> transform)
    {
        if (transform is null)
        {
            throw new ArgumentNullException(nameof(transform));
        }

        NpcSpawnRow[] before = _spawns.ToArray();
        var after = new NpcSpawnRow[before.Length];
        bool changed = false;
        for (int i = 0; i < before.Length; i++)
        {
            after[i] = transform(before[i]);
            changed |= after[i] != before[i];
        }

        if (!changed)
        {
            return;
        }

        int beforeStateId = _currentStateId;
        int afterStateId = _nextStateId++;
        _spawns.Clear();
        _spawns.AddRange(after);
        _currentStateId = afterStateId;
        Push(new SheetBulkCommand<NpcSpawnRow>(_spawns, before, after, beforeStateId, afterStateId));
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

    public void UpdateWarp(int index, WarpRow row)
    {
        WarpRow before = _warps[index];
        if (before == row)
        {
            return;
        }

        int beforeStateId = _currentStateId;
        int afterStateId = _nextStateId++;
        _warps[index] = row;
        _currentStateId = afterStateId;
        Push(new SheetUpdateCommand<WarpRow>(_warps, index, before, row, beforeStateId, afterStateId));
    }

    public void ReplaceWarps(IReadOnlyList<WarpRow> rows)
    {
        if (rows is null)
        {
            throw new ArgumentNullException(nameof(rows));
        }

        WarpRow[] before = _warps.ToArray();
        WarpRow[] after = rows.ToArray();
        if (RowsEqual(before, after))
        {
            return;
        }

        int beforeStateId = _currentStateId;
        int afterStateId = _nextStateId++;
        _warps.Clear();
        _warps.AddRange(after);
        _currentStateId = afterStateId;
        Push(new SheetBulkCommand<WarpRow>(_warps, before, after, beforeStateId, afterStateId));
    }

    public void TransformWarps(Func<WarpRow, WarpRow> transform)
    {
        if (transform is null)
        {
            throw new ArgumentNullException(nameof(transform));
        }

        WarpRow[] before = _warps.ToArray();
        var after = new WarpRow[before.Length];
        bool changed = false;
        for (int i = 0; i < before.Length; i++)
        {
            after[i] = transform(before[i]);
            changed |= after[i] != before[i];
        }

        if (!changed)
        {
            return;
        }

        int beforeStateId = _currentStateId;
        int afterStateId = _nextStateId++;
        _warps.Clear();
        _warps.AddRange(after);
        _currentStateId = afterStateId;
        Push(new SheetBulkCommand<WarpRow>(_warps, before, after, beforeStateId, afterStateId));
    }

    public bool ReplaceAll(IReadOnlyList<NpcSpawnRow> spawns, IReadOnlyList<WarpRow> warps)
    {
        if (spawns is null)
        {
            throw new ArgumentNullException(nameof(spawns));
        }

        if (warps is null)
        {
            throw new ArgumentNullException(nameof(warps));
        }

        NpcSpawnRow[] beforeSpawns = _spawns.ToArray();
        WarpRow[] beforeWarps = _warps.ToArray();
        NpcSpawnRow[] afterSpawns = spawns.ToArray();
        WarpRow[] afterWarps = warps.ToArray();
        if (RowsEqual(beforeSpawns, afterSpawns) && RowsEqual(beforeWarps, afterWarps))
        {
            return false;
        }

        int beforeStateId = _currentStateId;
        int afterStateId = _nextStateId++;
        _spawns.Clear();
        _spawns.AddRange(afterSpawns);
        _warps.Clear();
        _warps.AddRange(afterWarps);
        _currentStateId = afterStateId;
        Push(new SheetBulkPairCommand(_spawns, _warps, beforeSpawns, beforeWarps, afterSpawns, afterWarps, beforeStateId, afterStateId));
        return true;
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
        Bump();
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
        Bump();
        return true;
    }

    public void MarkPushed()
    {
        _pushedStateId = _currentStateId;
    }

    public void DiscardRedo()
    {
        if (_redo.Count == 0)
        {
            return;
        }

        _redo.Clear();
        Bump();
    }

    public void ClearHistory()
    {
        if (_undo.Count == 0 && _redo.Count == 0)
        {
            return;
        }

        _undo.Clear();
        _redo.Clear();
        Bump();
    }

    private void Push(SheetEditCommand command)
    {
        _redo.Clear();
        _undo.AddLast(command);
        Bump();
    }

    private void Bump()
    {
        _historyVersion++;
        HistoryChanged?.Invoke();
    }

    private static bool RowsEqual<T>(T[] before, T[] after) where T : struct
    {
        if (before.Length != after.Length)
        {
            return false;
        }

        for (int i = 0; i < before.Length; i++)
        {
            if (!before[i].Equals(after[i]))
            {
                return false;
            }
        }

        return true;
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
