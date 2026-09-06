using System.Collections.Generic;

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
