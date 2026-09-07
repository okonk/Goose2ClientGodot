using System.Collections.Generic;
using MapEditor.GameData.Rows;

namespace MapEditor.GameData.Editing;

internal abstract class SheetEditCommand
{
    protected SheetEditCommand(int beforeStateId, int afterStateId)
    {
        BeforeStateId = beforeStateId;
        AfterStateId = afterStateId;
    }

    internal int BeforeStateId { get; }

    internal int AfterStateId { get; }

    internal abstract void Replay(bool reverse);
}

internal sealed class SheetAddCommand<T> : SheetEditCommand
{
    private readonly IList<T> _rows;
    private readonly T _row;

    internal SheetAddCommand(IList<T> rows, T row, int beforeStateId, int afterStateId)
        : base(beforeStateId, afterStateId)
    {
        _rows = rows;
        _row = row;
    }

    internal override void Replay(bool reverse)
    {
        if (reverse)
        {
            _rows.RemoveAt(_rows.Count - 1);
        }
        else
        {
            _rows.Add(_row);
        }
    }
}

internal sealed class SheetRemoveCommand<T> : SheetEditCommand
{
    private readonly IList<T> _rows;
    private readonly int _index;
    private readonly T _row;

    internal SheetRemoveCommand(IList<T> rows, int index, T row, int beforeStateId, int afterStateId)
        : base(beforeStateId, afterStateId)
    {
        _rows = rows;
        _index = index;
        _row = row;
    }

    internal override void Replay(bool reverse)
    {
        if (reverse)
        {
            _rows.Insert(_index, _row);
        }
        else
        {
            _rows.RemoveAt(_index);
        }
    }
}

internal sealed class SheetMoveCommand<T> : SheetEditCommand
{
    private readonly IList<T> _rows;
    private readonly int _index;
    private readonly T _beforeRow;
    private readonly T _afterRow;

    internal SheetMoveCommand(IList<T> rows, int index, T beforeRow, T afterRow, int beforeStateId, int afterStateId)
        : base(beforeStateId, afterStateId)
    {
        _rows = rows;
        _index = index;
        _beforeRow = beforeRow;
        _afterRow = afterRow;
    }

    internal override void Replay(bool reverse)
    {
        _rows[_index] = reverse ? _beforeRow : _afterRow;
    }
}

internal sealed class SheetUpdateCommand<T> : SheetEditCommand
{
    private readonly IList<T> _rows;
    private readonly int _index;
    private readonly T _beforeRow;
    private readonly T _afterRow;

    internal SheetUpdateCommand(IList<T> rows, int index, T beforeRow, T afterRow, int beforeStateId, int afterStateId)
        : base(beforeStateId, afterStateId)
    {
        _rows = rows;
        _index = index;
        _beforeRow = beforeRow;
        _afterRow = afterRow;
    }

    internal override void Replay(bool reverse)
    {
        _rows[_index] = reverse ? _beforeRow : _afterRow;
    }
}

internal sealed class SheetBulkPairCommand : SheetEditCommand
{
    private readonly IList<NpcSpawnRow> _spawns;
    private readonly IList<WarpRow> _warps;
    private readonly NpcSpawnRow[] _beforeSpawns;
    private readonly WarpRow[] _beforeWarps;
    private readonly NpcSpawnRow[] _afterSpawns;
    private readonly WarpRow[] _afterWarps;

    internal SheetBulkPairCommand(
        IList<NpcSpawnRow> spawns,
        IList<WarpRow> warps,
        NpcSpawnRow[] beforeSpawns,
        WarpRow[] beforeWarps,
        NpcSpawnRow[] afterSpawns,
        WarpRow[] afterWarps,
        int beforeStateId,
        int afterStateId)
        : base(beforeStateId, afterStateId)
    {
        _spawns = spawns;
        _warps = warps;
        _beforeSpawns = beforeSpawns;
        _beforeWarps = beforeWarps;
        _afterSpawns = afterSpawns;
        _afterWarps = afterWarps;
    }

    internal override void Replay(bool reverse)
    {
        NpcSpawnRow[] spawnTarget = reverse ? _beforeSpawns : _afterSpawns;
        WarpRow[] warpTarget = reverse ? _beforeWarps : _afterWarps;
        _spawns.Clear();
        foreach (NpcSpawnRow row in spawnTarget)
        {
            _spawns.Add(row);
        }

        _warps.Clear();
        foreach (WarpRow row in warpTarget)
        {
            _warps.Add(row);
        }
    }
}

internal sealed class SheetBulkCommand<T> : SheetEditCommand
{
    private readonly IList<T> _rows;
    private readonly T[] _before;
    private readonly T[] _after;

    internal SheetBulkCommand(IList<T> rows, T[] before, T[] after, int beforeStateId, int afterStateId)
        : base(beforeStateId, afterStateId)
    {
        _rows = rows;
        _before = before;
        _after = after;
    }

    internal override void Replay(bool reverse)
    {
        T[] target = reverse ? _before : _after;
        _rows.Clear();
        foreach (T row in target)
        {
            _rows.Add(row);
        }
    }
}
