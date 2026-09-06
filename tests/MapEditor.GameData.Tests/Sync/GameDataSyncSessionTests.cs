using System;
using System.Collections.Generic;
using MapEditor.GameData.Connectivity;
using MapEditor.GameData.Replacement;
using MapEditor.GameData.Rows;
using MapEditor.GameData.Snapshots;
using MapEditor.GameData.Sync;
using Xunit;

namespace MapEditor.GameData.Tests.Sync;

public class GameDataSyncSessionTests
{
    private const string Sheet = "sheet-1";
    private const int Map = 5;

    private static RemoteRow<NpcSpawnRow> Spawn(int row, int npcId, int mapId, int x, int y)
        => new(row, new NpcSpawnRow(npcId, mapId, x, y));

    private static RemoteRow<WarpRow> Warp(int row, int mapId, int x, int y, int warpId, int warpX, int warpY)
        => new(row, new WarpRow(mapId, x, y, warpId, warpX, warpY));

    private static RemoteGameData Data(
        IReadOnlyList<MapReference>? maps = null,
        IReadOnlyDictionary<int, NpcAppearance>? npcs = null,
        IReadOnlyList<RemoteRow<NpcSpawnRow>>? spawns = null,
        IReadOnlyList<RemoteRow<WarpRow>>? warps = null)
        => new(
            maps ?? Array.Empty<MapReference>(),
            npcs ?? new Dictionary<int, NpcAppearance>(),
            spawns ?? Array.Empty<RemoteRow<NpcSpawnRow>>(),
            warps ?? Array.Empty<RemoteRow<WarpRow>>());

    [Fact]
    public void Constructor_BindsMapAndBuildsCleanSessionFromPulledRows()
    {
        var data = Data(
            spawns: new[] { Spawn(2, 1, Map, 1, 2), Spawn(3, 2, Map, 3, 4) },
            warps: new[] { Warp(2, Map, 1, 2, 10, 20, 21) });

        var session = new GameDataSyncSession(Sheet, Map, data);

        Assert.Equal(Sheet, session.SpreadsheetId);
        Assert.Equal(Map, session.MapId);
        Assert.False(session.RequiresPull);
        Assert.False(session.Edits.IsDirty);
        Assert.Equal(new[] { new NpcSpawnRow(1, Map, 1, 2), new NpcSpawnRow(2, Map, 3, 4) }, session.Edits.Spawns);
        Assert.Equal(new[] { new WarpRow(Map, 1, 2, 10, 20, 21) }, session.Edits.Warps);
        Assert.Equal(new SpawnSnapshot(new[] { new NpcSpawnRow(1, Map, 1, 2), new NpcSpawnRow(2, Map, 3, 4) }), session.PulledSpawns);
        Assert.Equal(new WarpSnapshot(new[] { new WarpRow(Map, 1, 2, 10, 20, 21) }), session.PulledWarps);
    }

    [Fact]
    public void Constructor_FiltersToTargetMap_NeverOwnsOtherMapsRows()
    {
        var data = Data(
            spawns: new[] { Spawn(2, 1, Map, 1, 2), Spawn(3, 2, 99, 3, 4), Spawn(4, 3, Map, 5, 6) },
            warps: new[] { Warp(2, Map, 1, 2, 10, 20, 21), Warp(3, 99, 7, 8, 11, 22, 23) });

        var session = new GameDataSyncSession(Sheet, Map, data);

        Assert.Equal(new[] { new NpcSpawnRow(1, Map, 1, 2), new NpcSpawnRow(3, Map, 5, 6) }, session.Edits.Spawns);
        Assert.Equal(new[] { new WarpRow(Map, 1, 2, 10, 20, 21) }, session.Edits.Warps);
        Assert.Equal(new[] { new NpcSpawnRow(1, Map, 1, 2), new NpcSpawnRow(3, Map, 5, 6) }, session.PulledSpawns.Rows);
        Assert.Equal(new[] { new WarpRow(Map, 1, 2, 10, 20, 21) }, session.PulledWarps.Rows);
    }

    [Fact]
    public void Constructor_RetainsDuplicateRows()
    {
        var data = Data(spawns: new[] { Spawn(2, 1, Map, 1, 2), Spawn(3, 1, Map, 1, 2), Spawn(4, 2, Map, 3, 4) });

        var session = new GameDataSyncSession(Sheet, Map, data);

        Assert.Equal(
            new[] { new NpcSpawnRow(1, Map, 1, 2), new NpcSpawnRow(1, Map, 1, 2), new NpcSpawnRow(2, Map, 3, 4) },
            session.Edits.Spawns);
        Assert.Equal(3, session.PulledSpawns.Rows.Count);
    }

    [Fact]
    public void RemoteGameData_DefensivelyCopiesAllCollections()
    {
        var maps = new List<MapReference> { new(1, "a", "a.gmp") };
        var npcs = new Dictionary<int, NpcAppearance>
        {
            [1] = new(1, "npc", 0, 0, new RgbaValue(0, 0, 0, 255), 0, 0, new RgbaValue(0, 0, 0, 255), "")
        };
        var spawns = new List<RemoteRow<NpcSpawnRow>> { Spawn(2, 1, Map, 1, 2) };
        var warps = new List<RemoteRow<WarpRow>> { Warp(2, Map, 1, 2, 10, 20, 21) };

        var data = new RemoteGameData(maps, npcs, spawns, warps);

        maps.Add(new(2, "b", "b.gmp"));
        npcs.Remove(1);
        spawns.Add(Spawn(3, 2, Map, 3, 4));
        warps.Clear();

        Assert.Single(data.Maps);
        Assert.Single(data.Npcs);
        Assert.Single(data.Spawns);
        Assert.Single(data.Warps);
    }

    [Fact]
    public void RemoteOwnedRows_DefensivelyCopiesCollections()
    {
        var spawns = new List<RemoteRow<NpcSpawnRow>> { Spawn(2, 1, Map, 1, 2) };
        var warps = new List<RemoteRow<WarpRow>> { Warp(2, Map, 1, 2, 10, 20, 21) };

        var rows = new RemoteOwnedRows(spawns, warps);

        spawns.Clear();
        warps.Clear();

        Assert.Single(rows.Spawns);
        Assert.Single(rows.Warps);
    }

    [Fact]
    public void MarkPushed_PromotesEditBaseline_WithoutClearingAmbiguousLatch()
    {
        var session = new GameDataSyncSession(Sheet, Map, Data(spawns: new[] { Spawn(2, 1, Map, 1, 2) }));
        session.MarkAmbiguous();
        Assert.True(session.RequiresPull);

        session.Edits.AddSpawn(new NpcSpawnRow(2, Map, 9, 9));
        Assert.True(session.Edits.IsDirty);

        session.MarkPushed();

        Assert.False(session.Edits.IsDirty);
        Assert.True(session.RequiresPull);
    }

    [Fact]
    public void ApplyPulled_ReplacesSnapshotsAndEdits_FiltersAndClearsRequiresPull()
    {
        var session = new GameDataSyncSession(Sheet, Map, Data(spawns: new[] { Spawn(2, 1, Map, 1, 2) }));
        session.MarkAmbiguous();
        session.Edits.AddSpawn(new NpcSpawnRow(2, Map, 9, 9));
        Assert.True(session.RequiresPull);
        Assert.True(session.Edits.IsDirty);

        session.ApplyPulled(Data(
            spawns: new[] { Spawn(2, 1, Map, 5, 5), Spawn(3, 2, 99, 7, 8) },
            warps: new[] { Warp(2, Map, 3, 4, 10, 20, 21) }));

        Assert.Equal(new[] { new NpcSpawnRow(1, Map, 5, 5) }, session.Edits.Spawns);
        Assert.Equal(new[] { new WarpRow(Map, 3, 4, 10, 20, 21) }, session.Edits.Warps);
        Assert.Equal(new SpawnSnapshot(new[] { new NpcSpawnRow(1, Map, 5, 5) }), session.PulledSpawns);
        Assert.Equal(new WarpSnapshot(new[] { new WarpRow(Map, 3, 4, 10, 20, 21) }), session.PulledWarps);
        Assert.False(session.RequiresPull);
        Assert.False(session.Edits.IsDirty);
    }

    [Fact]
    public void MarkAmbiguous_LatchesRequiresPull_OnlyCompletePullClears()
    {
        var session = new GameDataSyncSession(Sheet, Map, Data());
        Assert.False(session.RequiresPull);

        session.MarkAmbiguous();
        Assert.True(session.RequiresPull);

        session.MarkPushed();
        Assert.True(session.RequiresPull);

        session.ApplyPulled(Data());
        Assert.False(session.RequiresPull);
    }

    [Fact]
    public void Constructor_AllRowsOtherMaps_BuildsEmptyRowsAndSnapshots()
    {
        var data = Data(
            spawns: new[] { Spawn(2, 1, 99, 1, 2), Spawn(3, 2, 100, 3, 4) },
            warps: new[] { Warp(2, 99, 1, 2, 10, 20, 21), Warp(3, 100, 7, 8, 11, 22, 23) });

        var session = new GameDataSyncSession(Sheet, Map, data);

        Assert.Empty(session.Edits.Spawns);
        Assert.Empty(session.Edits.Warps);
        Assert.Empty(session.PulledSpawns.Rows);
        Assert.Empty(session.PulledWarps.Rows);
        Assert.False(session.RequiresPull);
        Assert.False(session.Edits.IsDirty);
    }

    [Fact]
    public void Constructor_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new GameDataSyncSession(null!, Map, Data()));
        Assert.Throws<ArgumentNullException>(() => new GameDataSyncSession(Sheet, Map, null!));
    }
}
