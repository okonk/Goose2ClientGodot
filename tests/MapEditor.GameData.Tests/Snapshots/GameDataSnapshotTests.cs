using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MapEditor.GameData.Rows;
using MapEditor.GameData.Snapshots;
using Xunit;

namespace MapEditor.GameData.Tests.Snapshots;

public class GameDataSnapshotTests
{
    private static NpcSpawnRow Spawn(int npcId, int mapId, int x, int y) => new(npcId, mapId, x, y);

    private static WarpRow Warp(int mapId, int x, int y, int warpId, int warpX, int warpY) =>
        new(mapId, x, y, warpId, warpX, warpY);

    [Fact]
    public void SpawnSnapshot_ReorderedRows_AreEqual()
    {
        var left = new SpawnSnapshot(new[] { Spawn(1, 10, 5, 6), Spawn(2, 10, 7, 8) });
        var right = new SpawnSnapshot(new[] { Spawn(2, 10, 7, 8), Spawn(1, 10, 5, 6) });

        Assert.True(left.Equals(right));
        Assert.True(right.Equals(left));
        Assert.Equal(left, right);
        Assert.Equal(left, (object)right);
    }

    [Fact]
    public void WarpSnapshot_ReorderedRows_AreEqual()
    {
        var left = new WarpSnapshot(new[] { Warp(10, 5, 6, 1, 20, 30), Warp(10, 7, 8, 2, 40, 50) });
        var right = new WarpSnapshot(new[] { Warp(10, 7, 8, 2, 40, 50), Warp(10, 5, 6, 1, 20, 30) });

        Assert.True(left.Equals(right));
        Assert.True(right.Equals(left));
        Assert.Equal(left, right);
        Assert.Equal(left, (object)right);
    }

    [Fact]
    public void SpawnSnapshot_DuplicateVsSingle_AreNotEqual()
    {
        var duplicated = new SpawnSnapshot(new[] { Spawn(1, 10, 5, 6), Spawn(1, 10, 5, 6) });
        var single = new SpawnSnapshot(new[] { Spawn(1, 10, 5, 6) });

        Assert.False(duplicated.Equals(single));
        Assert.False(single.Equals(duplicated));
        Assert.NotEqual(single, (object)duplicated);
    }

    [Fact]
    public void WarpSnapshot_DuplicateVsSingle_AreNotEqual()
    {
        var duplicated = new WarpSnapshot(new[] { Warp(10, 5, 6, 1, 20, 30), Warp(10, 5, 6, 1, 20, 30) });
        var single = new WarpSnapshot(new[] { Warp(10, 5, 6, 1, 20, 30) });

        Assert.False(duplicated.Equals(single));
        Assert.False(single.Equals(duplicated));
        Assert.NotEqual(single, (object)duplicated);
    }

    [Fact]
    public void SpawnSnapshot_DifferingDuplicateCounts_AreNotEqual()
    {
        var twice = new SpawnSnapshot(new[] { Spawn(1, 10, 5, 6), Spawn(1, 10, 5, 6), Spawn(2, 10, 7, 8) });
        var threeTimes = new SpawnSnapshot(new[] { Spawn(1, 10, 5, 6), Spawn(1, 10, 5, 6), Spawn(1, 10, 5, 6), Spawn(2, 10, 7, 8) });

        Assert.False(twice.Equals(threeTimes));
    }

    [Fact]
    public void WarpSnapshot_DifferingDuplicateCounts_AreNotEqual()
    {
        var twice = new WarpSnapshot(new[] { Warp(10, 5, 6, 1, 20, 30), Warp(10, 5, 6, 1, 20, 30), Warp(10, 7, 8, 2, 40, 50) });
        var threeTimes = new WarpSnapshot(new[] { Warp(10, 5, 6, 1, 20, 30), Warp(10, 5, 6, 1, 20, 30), Warp(10, 5, 6, 1, 20, 30), Warp(10, 7, 8, 2, 40, 50) });

        Assert.False(twice.Equals(threeTimes));
    }

    [Fact]
    public void SpawnSnapshot_EmptySnapshots_AreEqual()
    {
        var left = new SpawnSnapshot(Array.Empty<NpcSpawnRow>());
        var right = new SpawnSnapshot(Enumerable.Empty<NpcSpawnRow>());

        Assert.True(left.Equals(right));
        Assert.Empty(left.Rows);
        Assert.Empty(right.Rows);
    }

    [Fact]
    public void WarpSnapshot_EmptySnapshots_AreEqual()
    {
        var left = new WarpSnapshot(Array.Empty<WarpRow>());
        var right = new WarpSnapshot(Enumerable.Empty<WarpRow>());

        Assert.True(left.Equals(right));
        Assert.Empty(left.Rows);
        Assert.Empty(right.Rows);
    }

    [Fact]
    public void SpawnSnapshot_ReorderedMultisets_HaveEqualHashCodes()
    {
        var left = new SpawnSnapshot(new[] { Spawn(1, 10, 5, 6), Spawn(1, 10, 5, 6), Spawn(2, 10, 7, 8) });
        var right = new SpawnSnapshot(new[] { Spawn(2, 10, 7, 8), Spawn(1, 10, 5, 6), Spawn(1, 10, 5, 6) });

        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void WarpSnapshot_ReorderedMultisets_HaveEqualHashCodes()
    {
        var left = new WarpSnapshot(new[] { Warp(10, 5, 6, 1, 20, 30), Warp(10, 5, 6, 1, 20, 30), Warp(10, 7, 8, 2, 40, 50) });
        var right = new WarpSnapshot(new[] { Warp(10, 7, 8, 2, 40, 50), Warp(10, 5, 6, 1, 20, 30), Warp(10, 5, 6, 1, 20, 30) });

        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void SpawnSnapshot_CallerListMutatedAfterConstruction_SnapshotUnchanged()
    {
        var source = new List<NpcSpawnRow> { Spawn(1, 10, 5, 6) };
        var snapshot = new SpawnSnapshot(source);
        var before = new SpawnSnapshot(new[] { Spawn(1, 10, 5, 6) });

        source.Add(Spawn(2, 10, 7, 8));
        source[0] = Spawn(9, 99, 1, 1);
        source.Remove(Spawn(9, 99, 1, 1));

        Assert.Equal(before, snapshot);
        var rows = snapshot.Rows;
        Assert.Single(rows);
        Assert.Equal(Spawn(1, 10, 5, 6), rows[0]);
    }

    [Fact]
    public void WarpSnapshot_CallerListMutatedAfterConstruction_SnapshotUnchanged()
    {
        var source = new List<WarpRow> { Warp(10, 5, 6, 1, 20, 30) };
        var snapshot = new WarpSnapshot(source);
        var before = new WarpSnapshot(new[] { Warp(10, 5, 6, 1, 20, 30) });

        source.Add(Warp(10, 7, 8, 2, 40, 50));
        source[0] = Warp(99, 1, 1, 9, 2, 2);

        Assert.Equal(before, snapshot);
        var rows = snapshot.Rows;
        Assert.Single(rows);
        Assert.Equal(Warp(10, 5, 6, 1, 20, 30), rows[0]);
    }

    [Fact]
    public void SpawnSnapshot_RowsCannotBeMutatedThroughMutableCast()
    {
        var snapshot = new SpawnSnapshot(new[] { Spawn(1, 10, 5, 6) });

        var mutable = Assert.IsAssignableFrom<IList<NpcSpawnRow>>(snapshot.Rows);
        Assert.Throws<NotSupportedException>(() => mutable.Add(Spawn(2, 10, 7, 8)));
        Assert.Throws<NotSupportedException>(() => mutable.Clear());
        Assert.Throws<NotSupportedException>(() => _ = mutable[0] = Spawn(2, 10, 7, 8));
        Assert.Throws<NotSupportedException>(() => mutable.Remove(Spawn(1, 10, 5, 6)));
    }

    [Fact]
    public void WarpSnapshot_RowsCannotBeMutatedThroughMutableCast()
    {
        var snapshot = new WarpSnapshot(new[] { Warp(10, 5, 6, 1, 20, 30) });

        var mutable = Assert.IsAssignableFrom<IList<WarpRow>>(snapshot.Rows);
        Assert.Throws<NotSupportedException>(() => mutable.Add(Warp(10, 7, 8, 2, 40, 50)));
        Assert.Throws<NotSupportedException>(() => mutable.Clear());
        Assert.Throws<NotSupportedException>(() => _ = mutable[0] = Warp(10, 7, 8, 2, 40, 50));
        Assert.Throws<NotSupportedException>(() => mutable.Remove(Warp(10, 5, 6, 1, 20, 30)));
    }

    [Fact]
    public void SpawnSnapshot_NullRows_ThrowsArgumentNullException()
    {
        NpcSpawnRow[]? rows = null;

        var ex = Assert.Throws<ArgumentNullException>(() => new SpawnSnapshot(rows!));
        Assert.Contains("rows", ex.ParamName);
    }

    [Fact]
    public void WarpSnapshot_NullRows_ThrowsArgumentNullException()
    {
        WarpRow[]? rows = null;

        var ex = Assert.Throws<ArgumentNullException>(() => new WarpSnapshot(rows!));
        Assert.Contains("rows", ex.ParamName);
    }

    [Fact]
    public void SnapshotComparer_SpawnReorderedMultisets_AreEqual()
    {
        var left = new[] { Spawn(1, 10, 5, 6), Spawn(1, 10, 5, 6), Spawn(2, 10, 7, 8) };
        var right = new[] { Spawn(2, 10, 7, 8), Spawn(1, 10, 5, 6), Spawn(1, 10, 5, 6) };

        Assert.True(SnapshotComparer.Equals(left, right));
        Assert.True(SnapshotComparer.Equals(right, left));
        Assert.True(SnapshotComparer.Equals(Enumerable.Empty<NpcSpawnRow>(), Array.Empty<NpcSpawnRow>()));
    }

    [Fact]
    public void SnapshotComparer_SpawnDifferingMultiplicity_AreNotEqual()
    {
        var left = new[] { Spawn(1, 10, 5, 6), Spawn(1, 10, 5, 6) };
        var right = new[] { Spawn(1, 10, 5, 6) };

        Assert.False(SnapshotComparer.Equals(left, right));
        Assert.False(SnapshotComparer.Equals(right, left));
    }

    [Fact]
    public void SnapshotComparer_WarpReorderedMultisets_AreEqual()
    {
        var left = new[] { Warp(10, 5, 6, 1, 20, 30), Warp(10, 5, 6, 1, 20, 30), Warp(10, 7, 8, 2, 40, 50) };
        var right = new[] { Warp(10, 7, 8, 2, 40, 50), Warp(10, 5, 6, 1, 20, 30), Warp(10, 5, 6, 1, 20, 30) };

        Assert.True(SnapshotComparer.Equals(left, right));
        Assert.True(SnapshotComparer.Equals(right, left));
        Assert.True(SnapshotComparer.Equals(Enumerable.Empty<WarpRow>(), Array.Empty<WarpRow>()));
    }

    [Fact]
    public void SnapshotComparer_WarpDifferingMultiplicity_AreNotEqual()
    {
        var left = new[] { Warp(10, 5, 6, 1, 20, 30), Warp(10, 5, 6, 1, 20, 30) };
        var right = new[] { Warp(10, 5, 6, 1, 20, 30) };

        Assert.False(SnapshotComparer.Equals(left, right));
        Assert.False(SnapshotComparer.Equals(right, left));
    }
}
