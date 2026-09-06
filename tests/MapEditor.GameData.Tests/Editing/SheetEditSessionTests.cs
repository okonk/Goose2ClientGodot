using System;
using System.Collections.Generic;
using MapEditor.GameData.Editing;
using MapEditor.GameData.Rows;
using Xunit;

namespace MapEditor.GameData.Tests.Editing;

public class SheetEditSessionTests
{
    private static NpcSpawnRow Spawn(int npcId, int mapId, int mapX, int mapY) => new(npcId, mapId, mapX, mapY);

    private static WarpRow Warp(int mapId, int mapX, int mapY, int warpId, int warpX, int warpY) => new(mapId, mapX, mapY, warpId, warpX, warpY);

    [Fact]
    public void AddSpawn_AppendsRow_UndoAndRedoRestoreRows()
    {
        var session = new SheetEditSession(new[] { Spawn(1, 10, 5, 6) }, Array.Empty<WarpRow>());
        Assert.False(session.IsDirty);
        Assert.False(session.CanUndo);
        Assert.False(session.CanRedo);

        session.AddSpawn(Spawn(2, 10, 7, 8));
        Assert.Equal(new[] { Spawn(1, 10, 5, 6), Spawn(2, 10, 7, 8) }, session.Spawns);
        Assert.True(session.IsDirty);
        Assert.True(session.CanUndo);

        Assert.True(session.Undo());
        Assert.Equal(new[] { Spawn(1, 10, 5, 6) }, session.Spawns);
        Assert.False(session.IsDirty);
        Assert.True(session.CanRedo);

        Assert.True(session.Redo());
        Assert.Equal(new[] { Spawn(1, 10, 5, 6), Spawn(2, 10, 7, 8) }, session.Spawns);
        Assert.True(session.IsDirty);
        Assert.True(session.CanUndo);
        Assert.False(session.CanRedo);
    }

    [Fact]
    public void RemoveSpawnAt_RemovesExactDuplicateOccurrence_UndoRestoresOrder()
    {
        var first = Spawn(9, 1, 1, 1);
        var second = Spawn(9, 1, 1, 1);
        var third = Spawn(3, 2, 4, 5);
        var session = new SheetEditSession(new[] { first, second, third }, Array.Empty<WarpRow>());

        session.RemoveSpawnAt(1);
        Assert.Equal(new[] { first, third }, session.Spawns);

        Assert.True(session.Undo());
        Assert.Equal(new[] { first, second, third }, session.Spawns);
    }

    [Fact]
    public void MoveSpawn_ChangesOnlyCoordinates_UndoRestores()
    {
        var session = new SheetEditSession(new[] { Spawn(7, 42, 1, 2) }, Array.Empty<WarpRow>());

        session.MoveSpawn(0, 30, 40);
        Assert.Equal(new NpcSpawnRow(7, 42, 30, 40), session.Spawns[0]);

        Assert.True(session.Undo());
        Assert.Equal(new NpcSpawnRow(7, 42, 1, 2), session.Spawns[0]);
    }

    [Fact]
    public void MoveSpawn_ToSameCoordinates_IsNoOp()
    {
        var session = new SheetEditSession(new[] { Spawn(7, 42, 1, 2) }, Array.Empty<WarpRow>());

        session.MoveSpawn(0, 1, 2);

        Assert.Equal(new[] { Spawn(7, 42, 1, 2) }, session.Spawns);
        Assert.False(session.CanUndo);
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void AddWarp_AppendsRow_UndoAndRedoRestoreRows()
    {
        var session = new SheetEditSession(Array.Empty<NpcSpawnRow>(), new[] { Warp(10, 1, 2, 55, 30, 40) });

        session.AddWarp(Warp(10, 9, 9, 56, 31, 41));
        Assert.Equal(new[] { Warp(10, 1, 2, 55, 30, 40), Warp(10, 9, 9, 56, 31, 41) }, session.Warps);
        Assert.True(session.IsDirty);

        Assert.True(session.Undo());
        Assert.Equal(new[] { Warp(10, 1, 2, 55, 30, 40) }, session.Warps);
        Assert.False(session.IsDirty);

        Assert.True(session.Redo());
        Assert.Equal(new[] { Warp(10, 1, 2, 55, 30, 40), Warp(10, 9, 9, 56, 31, 41) }, session.Warps);
        Assert.True(session.IsDirty);
    }

    [Fact]
    public void RemoveWarpAt_RemovesExactOccurrence_UndoRestoresOrder()
    {
        var first = Warp(10, 1, 2, 55, 30, 40);
        var second = Warp(10, 1, 2, 55, 30, 40);
        var third = Warp(11, 3, 4, 57, 32, 42);
        var session = new SheetEditSession(Array.Empty<NpcSpawnRow>(), new[] { first, second, third });

        session.RemoveWarpAt(0);
        Assert.Equal(new[] { second, third }, session.Warps);

        Assert.True(session.Undo());
        Assert.Equal(new[] { first, second, third }, session.Warps);
    }

    [Fact]
    public void MoveWarpSource_ChangesOnlySourceCoordinates_UndoRestores()
    {
        var session = new SheetEditSession(Array.Empty<NpcSpawnRow>(), new[] { Warp(10, 1, 2, 55, 30, 40) });

        session.MoveWarpSource(0, 7, 8);
        Assert.Equal(new WarpRow(10, 7, 8, 55, 30, 40), session.Warps[0]);

        Assert.True(session.Undo());
        Assert.Equal(new WarpRow(10, 1, 2, 55, 30, 40), session.Warps[0]);
    }

    [Fact]
    public void NewEditAfterUndo_ClearsRedo()
    {
        var session = new SheetEditSession(Array.Empty<NpcSpawnRow>(), Array.Empty<WarpRow>());

        session.AddSpawn(Spawn(1, 1, 1, 1));
        Assert.True(session.Undo());
        Assert.True(session.CanRedo);

        session.AddSpawn(Spawn(2, 2, 2, 2));
        Assert.False(session.CanRedo);
        Assert.Equal(new[] { Spawn(2, 2, 2, 2) }, session.Spawns);
    }

    [Fact]
    public void IsDirty_TracksPushedBaselineThroughUndo()
    {
        var session = new SheetEditSession(Array.Empty<NpcSpawnRow>(), Array.Empty<WarpRow>());
        Assert.False(session.IsDirty);

        session.AddSpawn(Spawn(1, 1, 1, 1));
        Assert.True(session.IsDirty);

        Assert.True(session.Undo());
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void MarkPushed_SetsBaselineWithoutClearingHistory()
    {
        var session = new SheetEditSession(Array.Empty<NpcSpawnRow>(), Array.Empty<WarpRow>());

        session.AddSpawn(Spawn(1, 1, 1, 1));
        session.MarkPushed();
        Assert.False(session.IsDirty);
        Assert.True(session.CanUndo);

        session.AddSpawn(Spawn(2, 2, 2, 2));
        Assert.True(session.IsDirty);

        Assert.True(session.Undo());
        Assert.Equal(new[] { Spawn(1, 1, 1, 1) }, session.Spawns);
        Assert.False(session.IsDirty);

        Assert.True(session.Undo());
        Assert.Empty(session.Spawns);
        Assert.True(session.IsDirty);
    }

    [Fact]
    public void RemoveSpawnAt_InvalidIndex_ThrowsWithoutMutation()
    {
        var session = new SheetEditSession(new[] { Spawn(1, 1, 1, 1) }, Array.Empty<WarpRow>());
        session.AddSpawn(Spawn(2, 2, 2, 2));

        Assert.Throws<ArgumentOutOfRangeException>(() => session.RemoveSpawnAt(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.RemoveSpawnAt(2));

        Assert.Equal(new[] { Spawn(1, 1, 1, 1), Spawn(2, 2, 2, 2) }, session.Spawns);
        Assert.True(session.CanUndo);
        Assert.True(session.Undo());
        Assert.Equal(new[] { Spawn(1, 1, 1, 1) }, session.Spawns);
    }

    [Fact]
    public void MoveSpawn_InvalidIndex_ThrowsWithoutMutation()
    {
        var session = new SheetEditSession(new[] { Spawn(1, 1, 1, 1) }, Array.Empty<WarpRow>());
        session.AddSpawn(Spawn(2, 2, 2, 2));

        Assert.Throws<ArgumentOutOfRangeException>(() => session.MoveSpawn(-1, 9, 9));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.MoveSpawn(2, 9, 9));

        Assert.Equal(new[] { Spawn(1, 1, 1, 1), Spawn(2, 2, 2, 2) }, session.Spawns);
        Assert.True(session.CanUndo);
        Assert.True(session.Undo());
        Assert.Equal(new[] { Spawn(1, 1, 1, 1) }, session.Spawns);
    }

    [Fact]
    public void MoveWarpSource_InvalidIndex_ThrowsWithoutMutation()
    {
        var session = new SheetEditSession(Array.Empty<NpcSpawnRow>(), new[] { Warp(10, 1, 2, 55, 30, 40) });
        session.AddWarp(Warp(11, 3, 4, 57, 32, 42));

        Assert.Throws<ArgumentOutOfRangeException>(() => session.MoveWarpSource(-1, 9, 9));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.MoveWarpSource(2, 9, 9));

        Assert.Equal(new[] { Warp(10, 1, 2, 55, 30, 40), Warp(11, 3, 4, 57, 32, 42) }, session.Warps);
        Assert.True(session.CanUndo);
        Assert.True(session.Undo());
        Assert.Equal(new[] { Warp(10, 1, 2, 55, 30, 40) }, session.Warps);
    }

    [Fact]
    public void UndoAndRedo_EmptyHistory_ReturnFalse()
    {
        var session = new SheetEditSession(Array.Empty<NpcSpawnRow>(), Array.Empty<WarpRow>());

        Assert.False(session.Undo());
        Assert.False(session.Redo());
        Assert.Empty(session.Spawns);
        Assert.Empty(session.Warps);
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void UndoAll_RestoresOriginalRowsAndOrder()
    {
        var original = new[] { Spawn(1, 1, 1, 1), Spawn(2, 2, 2, 2), Spawn(3, 3, 3, 3) };
        var session = new SheetEditSession(original, Array.Empty<WarpRow>());

        session.AddSpawn(Spawn(4, 4, 4, 4));
        session.RemoveSpawnAt(0);
        session.MoveSpawn(0, 50, 60);

        while (session.Undo())
        {
        }

        Assert.Equal(original, session.Spawns);
        Assert.False(session.IsDirty);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void Constructor_DefensivelyCopiesInputs()
    {
        var spawns = new List<NpcSpawnRow> { Spawn(1, 1, 1, 1) };
        var warps = new List<WarpRow> { Warp(10, 1, 2, 55, 30, 40) };
        var session = new SheetEditSession(spawns, warps);

        spawns.Add(Spawn(2, 2, 2, 2));
        warps.Clear();

        Assert.Equal(new[] { Spawn(1, 1, 1, 1) }, session.Spawns);
        Assert.Equal(new[] { Warp(10, 1, 2, 55, 30, 40) }, session.Warps);
    }

    [Fact]
    public void Sessions_DonShareRowsOrHistory()
    {
        var shared = new[] { Spawn(1, 1, 1, 1) };
        var first = new SheetEditSession(shared, Array.Empty<WarpRow>());
        var second = new SheetEditSession(shared, Array.Empty<WarpRow>());

        first.AddSpawn(Spawn(2, 2, 2, 2));
        first.MoveSpawn(0, 5, 6);

        Assert.Equal(new[] { Spawn(1, 1, 1, 1) }, second.Spawns);
        Assert.False(second.CanUndo);
        Assert.False(second.CanRedo);
        Assert.False(second.IsDirty);

        Assert.True(first.Undo());
        Assert.True(first.Undo());
        Assert.Equal(new[] { Spawn(1, 1, 1, 1) }, first.Spawns);
        Assert.False(first.IsDirty);
    }

    [Fact]
    public void SpawnsAndWarpsViews_CannotBeCastToMutableInterfaces_AndReflectEdits()
    {
        var session = new SheetEditSession(new[] { Spawn(1, 10, 5, 6) }, new[] { Warp(10, 1, 2, 55, 30, 40) });

        Assert.Throws<InvalidCastException>(() => (IList<NpcSpawnRow>)session.Spawns);
        Assert.Throws<InvalidCastException>(() => (IList<WarpRow>)session.Warps);
        Assert.Throws<InvalidCastException>(() => (List<NpcSpawnRow>)session.Spawns);
        Assert.Throws<InvalidCastException>(() => (List<WarpRow>)session.Warps);

        session.AddSpawn(Spawn(2, 10, 7, 8));
        Assert.Equal(2, session.Spawns.Count);
        Assert.Equal(Spawn(2, 10, 7, 8), session.Spawns[1]);
    }
}
