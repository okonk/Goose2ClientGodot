using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using MapEditor.GameData.Rows;

namespace MapEditor.GameData.Snapshots;

public sealed class SpawnSnapshot : IEquatable<SpawnSnapshot>
{
    private readonly NpcSpawnRow[] _rows;

    public SpawnSnapshot(IEnumerable<NpcSpawnRow> rows)
    {
        if (rows is null)
            throw new ArgumentNullException(nameof(rows));

        _rows = rows.ToArray();
        Rows = new ReadOnlyCollection<NpcSpawnRow>(_rows);
    }

    public IReadOnlyList<NpcSpawnRow> Rows { get; }

    public bool Equals(SpawnSnapshot? other)
    {
        if (other is null)
            return false;

        return SnapshotComparer.Equals(_rows, other._rows);
    }

    public override bool Equals(object? obj) => obj is SpawnSnapshot other && Equals(other);

    public override int GetHashCode() => SnapshotComparer.Hash(_rows);
}

public sealed class WarpSnapshot : IEquatable<WarpSnapshot>
{
    private readonly WarpRow[] _rows;

    public WarpSnapshot(IEnumerable<WarpRow> rows)
    {
        if (rows is null)
            throw new ArgumentNullException(nameof(rows));

        _rows = rows.ToArray();
        Rows = new ReadOnlyCollection<WarpRow>(_rows);
    }

    public IReadOnlyList<WarpRow> Rows { get; }

    public bool Equals(WarpSnapshot? other)
    {
        if (other is null)
            return false;

        return SnapshotComparer.Equals(_rows, other._rows);
    }

    public override bool Equals(object? obj) => obj is WarpSnapshot other && Equals(other);

    public override int GetHashCode() => SnapshotComparer.Hash(_rows);
}
