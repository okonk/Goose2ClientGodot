using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MapEditor.GameData.Connectivity;
using MapEditor.GameData.Replacement;
using MapEditor.GameData.Rows;
using MapEditor.GameData.Snapshots;
using MapEditor.GameData.Sync;
using MapEditor.GameData.Tests.Fakes;
using Xunit;

namespace MapEditor.GameData.Tests.Sync;

public class GameDataSyncCoordinatorTests
{
    private const string Sheet = "sheet-1";
    private const int Map = 5;
    private const int OtherMap = 99;
    private static readonly MapDimensions Dimensions = new(100, 100);
    private static readonly IReadOnlyDictionary<int, MapDimensions> NoOpenMaps =
        new Dictionary<int, MapDimensions>();

    private static MapReference MapRef(int id, string name = "m") => new(id, name, $"{name}.gmp");

    private static NpcAppearance Npc(int id) =>
        new(id, $"npc-{id}", 0, 0, new RgbaValue(0, 0, 0, 255), 0, 0, new RgbaValue(0, 0, 0, 255), "");

    private static RemoteRow<NpcSpawnRow> Spawn(int row, int npcId, int mapId, int x, int y)
        => new(row, new NpcSpawnRow(npcId, mapId, x, y));

    private static RemoteRow<WarpRow> Warp(int row, int mapId, int x, int y, int warpId, int warpX, int warpY)
        => new(row, new WarpRow(mapId, x, y, warpId, warpX, warpY));

    private static RemoteGameData BaseData() => new(
        new[] { MapRef(Map), MapRef(10) },
        new Dictionary<int, NpcAppearance> { [1] = Npc(1), [2] = Npc(2) },
        new[] { Spawn(2, 1, Map, 1, 2), Spawn(3, 2, Map, 5, 6) },
        new[] { Warp(2, Map, 3, 4, 10, 5, 6), Warp(3, Map, 7, 8, 10, 9, 10) });

    private static RemoteOwnedRows BaseOwnedRows() => new(
        new[] { Spawn(2, 1, Map, 1, 2), Spawn(3, 2, Map, 5, 6) },
        new[] { Warp(2, Map, 3, 4, 10, 5, 6), Warp(3, Map, 7, 8, 10, 9, 10) });

    private static GameDataSyncSession NewSession() => new(Sheet, Map, BaseData());

    private static (GameDataSyncCoordinator Coordinator, ScriptedGameDataGateway Gateway, List<TimeSpan> Delays)
        NewCoordinator()
    {
        var gateway = new ScriptedGameDataGateway();
        var delays = new List<TimeSpan>();
        var coordinator = new GameDataSyncCoordinator(gateway, (span, ct) =>
        {
            if (ct.IsCancellationRequested)
            {
                return Task.FromCanceled(ct);
            }

            delays.Add(span);
            return Task.CompletedTask;
        });
        return (coordinator, gateway, delays);
    }

    [Fact]
    public async Task LoadMapCatalog_ReturnsCatalogResult()
    {
        var (coordinator, gateway, _) = NewCoordinator();
        gateway.EnqueueMaps(new[] { MapRef(1), MapRef(2) });

        var result = await coordinator.LoadMapCatalogAsync(Sheet, CancellationToken.None);

        var catalog = Assert.IsType<MapCatalogResult>(result);
        Assert.Equal(new[] { MapRef(1), MapRef(2) }, catalog.Maps);
        var call = Assert.Single(gateway.Calls);
        Assert.Equal("ReadMapsAsync", call.Method);
        Assert.Equal(Sheet, call.SpreadsheetId);
    }

    [Fact]
    public async Task Pull_Succeeds_BuildsFreshSession_FilteredToTargetMap()
    {
        var (coordinator, gateway, _) = NewCoordinator();
        // Map name differs between catalog and pulled data; matching is by id only.
        gateway.EnqueueMaps(new[] { MapRef(Map, "renamed"), MapRef(10) });
        gateway.EnqueueGameData(new RemoteGameData(
            new[] { MapRef(Map), MapRef(10) },
            new Dictionary<int, NpcAppearance> { [1] = Npc(1), [2] = Npc(2) },
            new[] { Spawn(2, 1, Map, 1, 2), Spawn(3, 1, OtherMap, 1, 2), Spawn(4, 2, Map, 7, 8) },
            new[] { Warp(2, Map, 3, 4, 10, 5, 6), Warp(3, OtherMap, 7, 8, 10, 9, 10) }));

        var result = await coordinator.PullAsync(Sheet, Map, null, discardApproved: false, CancellationToken.None);

        var session = Assert.IsType<PullSucceededResult>(result).Session;
        Assert.Equal(Sheet, session.SpreadsheetId);
        Assert.Equal(Map, session.MapId);
        Assert.False(session.RequiresPull);
        Assert.False(session.Edits.IsDirty);
        Assert.Equal(new[] { new NpcSpawnRow(1, Map, 1, 2), new NpcSpawnRow(2, Map, 7, 8) }, session.Edits.Spawns);
        Assert.Equal(new[] { new WarpRow(Map, 3, 4, 10, 5, 6) }, session.Edits.Warps);
        Assert.Equal(new SpawnSnapshot(new[] { new NpcSpawnRow(1, Map, 1, 2), new NpcSpawnRow(2, Map, 7, 8) }),
            session.PulledSpawns);
        Assert.Equal(new WarpSnapshot(new[] { new WarpRow(Map, 3, 4, 10, 5, 6) }), session.PulledWarps);
        Assert.Equal(2, session.Npcs.Count);
        Assert.Equal(2, session.Maps.Count);
        Assert.Equal(new[] { "ReadMapsAsync", "ReadGameDataAsync" },
            gateway.Calls.Select(call => call.Method).ToArray());
    }

    [Fact]
    public async Task Pull_DirtySessionWithoutApprovedDiscard_RejectsBeforeAnyNetworkCall()
    {
        var (coordinator, gateway, _) = NewCoordinator();
        var session = NewSession();
        session.Edits.AddSpawn(new NpcSpawnRow(2, Map, 9, 9));

        var result = await coordinator.PullAsync(Sheet, Map, session, discardApproved: false, CancellationToken.None);

        Assert.IsType<DirtyLocalRejectedResult>(result);
        Assert.Empty(gateway.Calls);
    }

    [Fact]
    public async Task Pull_DirtySessionWithApprovedDiscard_CompletelyReplacesState()
    {
        var (coordinator, gateway, _) = NewCoordinator();
        var session = NewSession();
        session.Edits.AddSpawn(new NpcSpawnRow(2, Map, 9, 9));
        gateway.EnqueueMaps(new[] { MapRef(Map), MapRef(10) });
        gateway.EnqueueGameData(BaseData());

        var result = await coordinator.PullAsync(Sheet, Map, session, discardApproved: true, CancellationToken.None);

        var fresh = Assert.IsType<PullSucceededResult>(result).Session;
        Assert.NotSame(session, fresh);
        Assert.False(fresh.Edits.IsDirty);
        Assert.True(session.Edits.IsDirty);
        Assert.Equal(2, fresh.Npcs.Count);
    }

    [Fact]
    public async Task Pull_UnknownSelectedMapId_ReturnsValidationRejected_WithoutReadingGameData()
    {
        var (coordinator, gateway, _) = NewCoordinator();
        gateway.EnqueueMaps(new[] { MapRef(1) });

        var result = await coordinator.PullAsync(Sheet, Map, null, discardApproved: false, CancellationToken.None);

        var rejected = Assert.IsType<ValidationRejectedResult>(result);
        Assert.False(rejected.Validation.IsValid);
        var issue = Assert.Single(rejected.Validation.Errors);
        Assert.Equal("sync-map-not-found", issue.Code);
        Assert.Equal(new[] { "ReadMapsAsync" }, gateway.Calls.Select(call => call.Method).ToArray());
    }

    [Theory]
    [InlineData("maps")]
    [InlineData("npcs")]
    [InlineData("spawn-header")]
    [InlineData("spawn-row")]
    [InlineData("warp-header")]
    [InlineData("warp-row")]
    public async Task Pull_LateFailure_LeavesCurrentSessionUnchanged(string stage)
    {
        var (coordinator, gateway, _) = NewCoordinator();
        var session = NewSession();
        session.Edits.AddSpawn(new NpcSpawnRow(2, Map, 9, 9));
        Assert.True(session.Edits.CanUndo);
        Assert.False(session.Edits.CanRedo);

        if (stage == "maps")
        {
            gateway.EnqueueMapsFailure(GatewayFailureKind.Schema);
        }
        else
        {
            gateway.EnqueueMaps(new[] { MapRef(Map), MapRef(10) });
            gateway.EnqueueGameDataFailure(GatewayFailureKind.Schema);
        }

        await Assert.ThrowsAsync<GameDataGatewayException>(
            () => coordinator.PullAsync(Sheet, Map, session, discardApproved: true, CancellationToken.None));

        Assert.Equal(Sheet, session.SpreadsheetId);
        Assert.Equal(Map, session.MapId);
        Assert.False(session.RequiresPull);
        Assert.True(session.Edits.IsDirty);
        Assert.True(session.Edits.CanUndo);
        Assert.False(session.Edits.CanRedo);
        Assert.Equal(new[] { new NpcSpawnRow(1, Map, 1, 2), new NpcSpawnRow(2, Map, 5, 6), new NpcSpawnRow(2, Map, 9, 9) },
            session.Edits.Spawns);
        Assert.Equal(new[] { new WarpRow(Map, 3, 4, 10, 5, 6), new WarpRow(Map, 7, 8, 10, 9, 10) }, session.Edits.Warps);
        Assert.Equal(new SpawnSnapshot(new[] { new NpcSpawnRow(1, Map, 1, 2), new NpcSpawnRow(2, Map, 5, 6) }),
            session.PulledSpawns);
        Assert.Equal(new WarpSnapshot(new[] { new WarpRow(Map, 3, 4, 10, 5, 6), new WarpRow(Map, 7, 8, 10, 9, 10) }),
            session.PulledWarps);
        Assert.Equal(2, session.Npcs.Count);
        Assert.Equal(2, session.Maps.Count);
    }

    [Fact]
    public async Task Read_RetriesRateLimitedThenTemporary_WithExactDelays()
    {
        var (coordinator, gateway, delays) = NewCoordinator();
        gateway.EnqueueMapsFailure(GatewayFailureKind.RateLimited);
        gateway.EnqueueMapsFailure(GatewayFailureKind.Temporary);
        gateway.EnqueueMaps(new[] { MapRef(1) });

        var result = await coordinator.LoadMapCatalogAsync(Sheet, CancellationToken.None);

        Assert.IsType<MapCatalogResult>(result);
        Assert.Equal(3, gateway.Calls.Count);
        Assert.Equal(new[] { TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(1) }, delays);
    }

    [Fact]
    public async Task Read_TransportFailure_RetriesOnceOnSecondAttempt()
    {
        var (coordinator, gateway, delays) = NewCoordinator();
        gateway.EnqueueMaps(new[] { MapRef(Map), MapRef(10) });
        gateway.EnqueueGameDataFailure(GatewayFailureKind.Transport);
        gateway.EnqueueGameData(BaseData());

        var result = await coordinator.PullAsync(Sheet, Map, null, discardApproved: false, CancellationToken.None);

        Assert.IsType<PullSucceededResult>(result);
        Assert.Equal(2, gateway.Calls.Count(c => c.Method == "ReadGameDataAsync"));
        Assert.Equal(new[] { TimeSpan.FromMilliseconds(250) }, delays);
    }

    [Fact]
    public async Task Read_OwnedRowsPreflight_RetriesRateLimited_WithExactDelays()
    {
        var (coordinator, gateway, delays) = NewCoordinator();
        var session = NewSession();
        session.Edits.AddSpawn(new NpcSpawnRow(2, Map, 9, 9));
        gateway.EnqueueOwnedRowsFailure(GatewayFailureKind.RateLimited);
        gateway.EnqueueOwnedRows(BaseOwnedRows());
        gateway.EnqueueReplaceSuccess();

        var result = await coordinator.PushAsync(session, Dimensions, NoOpenMaps, null, null, CancellationToken.None);

        Assert.IsType<PushedResult>(result);
        Assert.Equal(2, gateway.Calls.Count(c => c.Method == "ReadOwnedRowsAsync"));
        Assert.Equal(new[] { TimeSpan.FromMilliseconds(250) }, delays);
    }

    [Fact]
    public async Task Read_RetriesExhausted_ThrowsLastException_AfterTwoDelays()
    {
        var (coordinator, gateway, delays) = NewCoordinator();
        gateway.EnqueueMapsFailure(GatewayFailureKind.RateLimited);
        gateway.EnqueueMapsFailure(GatewayFailureKind.Temporary);
        gateway.EnqueueMapsFailure(GatewayFailureKind.Transport);

        var ex = await Assert.ThrowsAsync<GameDataGatewayException>(
            () => coordinator.LoadMapCatalogAsync(Sheet, CancellationToken.None));

        Assert.Equal(GatewayFailureKind.Transport, ex.Kind);
        Assert.Equal(3, gateway.Calls.Count);
        Assert.Equal(new[] { TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(1) }, delays);
    }

    [Fact]
    public async Task Read_CancellationDuringDelay_PropagatesUnwrapped()
    {
        var gateway = new ScriptedGameDataGateway();
        gateway.EnqueueMapsFailure(GatewayFailureKind.RateLimited);
        var cts = new CancellationTokenSource();
        var coordinator = new GameDataSyncCoordinator(gateway, (span, ct) =>
        {
            cts.Cancel();
            return Task.FromCanceled(cts.Token);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => coordinator.LoadMapCatalogAsync(Sheet, cts.Token));

        Assert.Single(gateway.Calls);
    }

    [Theory]
    [InlineData(GatewayFailureKind.Authentication)]
    [InlineData(GatewayFailureKind.Permission)]
    [InlineData(GatewayFailureKind.NotFound)]
    [InlineData(GatewayFailureKind.Schema)]
    public async Task Read_PermanentFailure_DoesNotRetry(GatewayFailureKind kind)
    {
        var (coordinator, gateway, delays) = NewCoordinator();
        gateway.EnqueueMapsFailure(kind);

        var ex = await Assert.ThrowsAsync<GameDataGatewayException>(
            () => coordinator.LoadMapCatalogAsync(Sheet, CancellationToken.None));

        Assert.Equal(kind, ex.Kind);
        Assert.Single(gateway.Calls);
        Assert.Empty(delays);
    }

    [Fact]
    public async Task Read_NonGatewayException_DoesNotRetry()
    {
        var (coordinator, gateway, delays) = NewCoordinator();
        gateway.EnqueueMapsException(new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.LoadMapCatalogAsync(Sheet, CancellationToken.None));

        Assert.Single(gateway.Calls);
        Assert.Empty(delays);
    }

    [Fact]
    public async Task Push_InvalidLocalEdits_RejectsBeforeAnyGatewayCall()
    {
        var (coordinator, gateway, _) = NewCoordinator();
        var session = NewSession();
        session.Edits.AddSpawn(new NpcSpawnRow(42, Map, 5, 5));
        session.Edits.AddWarp(new WarpRow(Map, 6, 7, 999, 1, 1));

        var result = await coordinator.PushAsync(session, Dimensions, NoOpenMaps, null, null, CancellationToken.None);

        var rejected = Assert.IsType<ValidationRejectedResult>(result);
        Assert.False(rejected.Validation.IsValid);
        Assert.Contains(rejected.Validation.Errors, issue => issue.Code == "spawn-npc-not-found");
        Assert.Contains(rejected.Validation.Errors, issue => issue.Code == "warp-dest-map-not-found");
        Assert.Empty(gateway.Calls);
    }

    [Fact]
    public async Task Push_RequiresPull_BlocksBeforeAnyGatewayCall()
    {
        var (coordinator, gateway, _) = NewCoordinator();
        var session = NewSession();
        session.MarkAmbiguous();

        var result = await coordinator.PushAsync(session, Dimensions, NoOpenMaps, null, null, CancellationToken.None);

        var rejected = Assert.IsType<ValidationRejectedResult>(result);
        Assert.False(rejected.Validation.IsValid);
        Assert.Equal("sync-pull-required", Assert.Single(rejected.Validation.Errors).Code);
        Assert.Empty(gateway.Calls);
    }

    [Fact]
    public async Task Push_DuplicateCatalogMapId_RejectsBeforeAnyGatewayCall()
    {
        var (coordinator, gateway, _) = NewCoordinator();
        var session = new GameDataSyncSession(Sheet, Map, new RemoteGameData(
            new[] { MapRef(Map), MapRef(Map, "dup"), MapRef(10) },
            new Dictionary<int, NpcAppearance> { [1] = Npc(1), [2] = Npc(2) },
            new[] { Spawn(2, 1, Map, 1, 2), Spawn(3, 2, Map, 5, 6) },
            new[] { Warp(2, Map, 3, 4, 10, 5, 6), Warp(3, Map, 7, 8, 10, 9, 10) }));

        var result = await coordinator.PushAsync(session, Dimensions, NoOpenMaps, null, null, CancellationToken.None);

        var rejected = Assert.IsType<ValidationRejectedResult>(result);
        Assert.Equal("sync-map-duplicate", Assert.Single(rejected.Validation.Errors).Code);
        Assert.Empty(gateway.Calls);
    }

    [Fact]
    public async Task Push_UnchangedRemoteInDifferentOrder_NoGatewayWrite_MarksPushed()
    {
        var (coordinator, gateway, _) = NewCoordinator();
        var session = NewSession();
        gateway.EnqueueOwnedRows(new RemoteOwnedRows(
            new[] { Spawn(9, 2, Map, 5, 6), Spawn(8, 1, Map, 1, 2) },
            new[] { Warp(9, Map, 7, 8, 10, 9, 10), Warp(8, Map, 3, 4, 10, 5, 6) }));

        var result = await coordinator.PushAsync(session, Dimensions, NoOpenMaps, null, null, CancellationToken.None);

        Assert.IsType<PushedResult>(result);
        Assert.False(session.Edits.IsDirty);
        Assert.DoesNotContain(gateway.Calls, call => call.Method == "ReplaceOwnedRowsAsync");
    }

    [Fact]
    public async Task Push_RemoteDuplicateMultiplicity_ConflictContainsLatestRows()
    {
        var (coordinator, gateway, _) = NewCoordinator();
        var session = NewSession();
        gateway.EnqueueOwnedRows(new RemoteOwnedRows(
            new[] { Spawn(2, 1, Map, 1, 2), Spawn(3, 1, Map, 1, 2), Spawn(4, 2, Map, 5, 6) },
            new[] { Warp(2, Map, 3, 4, 10, 5, 6), Warp(3, Map, 7, 8, 10, 9, 10) }));

        var result = await coordinator.PushAsync(session, Dimensions, NoOpenMaps, null, null, CancellationToken.None);

        var conflict = Assert.IsType<PushConflictResult>(result);
        Assert.Equal(new[] { Spawn(2, 1, Map, 1, 2), Spawn(3, 1, Map, 1, 2), Spawn(4, 2, Map, 5, 6) },
            conflict.LatestRemoteRows.Spawns);
        Assert.Equal(new[] { Warp(2, Map, 3, 4, 10, 5, 6), Warp(3, Map, 7, 8, 10, 9, 10) },
            conflict.LatestRemoteRows.Warps);
        Assert.DoesNotContain(gateway.Calls, call => call.Method == "ReplaceOwnedRowsAsync");
    }

    [Fact]
    public async Task Push_Conflict_Cancel_MakesNoChange()
    {
        var (coordinator, gateway, _) = NewCoordinator();
        var session = NewSession();
        session.Edits.AddSpawn(new NpcSpawnRow(2, Map, 9, 9));
        gateway.EnqueueOwnedRows(new RemoteOwnedRows(
            new[] { Spawn(2, 1, Map, 1, 2), Spawn(3, 2, Map, 5, 6), Spawn(4, 1, Map, 8, 8) },
            new[] { Warp(2, Map, 3, 4, 10, 5, 6), Warp(3, Map, 7, 8, 10, 9, 10) }));

        var result = await coordinator.PushAsync(session, Dimensions, NoOpenMaps,
            PushConflictChoice.Cancel, null, CancellationToken.None);

        Assert.IsType<CancelledResult>(result);
        Assert.True(session.Edits.IsDirty);
        Assert.True(session.Edits.CanUndo);
        Assert.Equal(new SpawnSnapshot(new[] { new NpcSpawnRow(1, Map, 1, 2), new NpcSpawnRow(2, Map, 5, 6) }),
            session.PulledSpawns);
        Assert.False(session.RequiresPull);
        Assert.DoesNotContain(gateway.Calls, call => call.Method == "ReplaceOwnedRowsAsync");
    }

    [Fact]
    public async Task Push_Conflict_PullInstead_ReturnsInstructionWithoutMutatingState()
    {
        var (coordinator, gateway, _) = NewCoordinator();
        var session = NewSession();
        session.Edits.AddSpawn(new NpcSpawnRow(2, Map, 9, 9));
        gateway.EnqueueOwnedRows(new RemoteOwnedRows(
            new[] { Spawn(2, 1, Map, 1, 2), Spawn(3, 2, Map, 5, 6), Spawn(4, 1, Map, 8, 8) },
            new[] { Warp(2, Map, 3, 4, 10, 5, 6), Warp(3, Map, 7, 8, 10, 9, 10) }));

        var result = await coordinator.PushAsync(session, Dimensions, NoOpenMaps,
            PushConflictChoice.PullInstead, null, CancellationToken.None);

        Assert.IsType<PullInsteadRequestedResult>(result);
        Assert.True(session.Edits.IsDirty);
        Assert.Equal(new SpawnSnapshot(new[] { new NpcSpawnRow(1, Map, 1, 2), new NpcSpawnRow(2, Map, 5, 6) }),
            session.PulledSpawns);
        Assert.False(session.RequiresPull);
        Assert.DoesNotContain(gateway.Calls, call => call.Method == "ReplaceOwnedRowsAsync");
    }

    [Fact]
    public async Task Push_OverwriteWithoutCapturedRows_ReturnsConflictAgain()
    {
        var (coordinator, gateway, _) = NewCoordinator();
        var session = NewSession();
        gateway.EnqueueOwnedRows(new RemoteOwnedRows(
            new[] { Spawn(2, 1, Map, 1, 2), Spawn(3, 2, Map, 5, 6), Spawn(4, 1, Map, 8, 8) },
            new[] { Warp(2, Map, 3, 4, 10, 5, 6), Warp(3, Map, 7, 8, 10, 9, 10) }));

        var result = await coordinator.PushAsync(session, Dimensions, NoOpenMaps,
            PushConflictChoice.Overwrite, null, CancellationToken.None);

        var conflict = Assert.IsType<PushConflictResult>(result);
        Assert.Equal(3, conflict.LatestRemoteRows.Spawns.Count);
        Assert.DoesNotContain(gateway.Calls, call => call.Method == "ReplaceOwnedRowsAsync");
    }

    [Fact]
    public async Task Push_OverwriteWithCapturedRows_PlansAgainstSuppliedRows_WithoutReReading()
    {
        var (coordinator, gateway, _) = NewCoordinator();
        var session = NewSession();
        session.Edits.AddSpawn(new NpcSpawnRow(2, Map, 9, 9));
        var conflictRows = new RemoteOwnedRows(
            new[] { Spawn(2, 1, Map, 1, 2), Spawn(3, 1, Map, 4, 4), Spawn(4, 2, Map, 5, 6) },
            new[] { Warp(2, Map, 3, 4, 10, 5, 6), Warp(3, Map, 7, 8, 10, 9, 10) });
        gateway.EnqueueReplaceSuccess();

        var result = await coordinator.PushAsync(session, Dimensions, NoOpenMaps,
            PushConflictChoice.Overwrite, conflictRows, CancellationToken.None);

        Assert.IsType<PushedResult>(result);
        Assert.DoesNotContain(gateway.Calls, call => call.Method == "ReadOwnedRowsAsync");
        var write = Assert.Single(gateway.Calls, call => call.Method == "ReplaceOwnedRowsAsync");
        Assert.NotNull(write.SpawnPlan);
        Assert.NotNull(write.WarpPlan);
        Assert.Equal("NPC Spawns", write.SpawnPlan.Sheet);
        Assert.Equal(new[] { new RowDelete(3) }, write.SpawnPlan.Deletes);
        var insert = Assert.Single(write.SpawnPlan.Inserts);
        Assert.Equal(new[] { "2", "5", "9", "9" }, insert.CellValues);
        Assert.Equal("Warptiles", write.WarpPlan.Sheet);
        Assert.Empty(write.WarpPlan.Deletes);
        Assert.Empty(write.WarpPlan.Inserts);
        Assert.False(session.Edits.IsDirty);
        Assert.Equal(new SpawnSnapshot(session.Edits.Spawns), session.PulledSpawns);
    }

    [Fact]
    public async Task Push_OverwriteWithCapturedRows_NotDirty_MarksPushedWithoutWrite()
    {
        var (coordinator, gateway, _) = NewCoordinator();
        var session = NewSession();
        var conflictRows = new RemoteOwnedRows(
            new[] { Spawn(2, 1, Map, 1, 2), Spawn(3, 2, Map, 5, 6) },
            new[] { Warp(2, Map, 3, 4, 10, 5, 6), Warp(3, Map, 7, 8, 10, 9, 10) });

        var result = await coordinator.PushAsync(session, Dimensions, NoOpenMaps,
            PushConflictChoice.Overwrite, conflictRows, CancellationToken.None);

        Assert.IsType<PushedResult>(result);
        Assert.Empty(gateway.Calls);
        Assert.False(session.Edits.IsDirty);
    }

    [Fact]
    public async Task Push_Success_PromotesSnapshotsAndMarksPushed_SingleMutationCall()
    {
        var (coordinator, gateway, delays) = NewCoordinator();
        var session = NewSession();
        session.Edits.AddSpawn(new NpcSpawnRow(2, Map, 9, 9));
        gateway.EnqueueOwnedRows(BaseOwnedRows());
        gateway.EnqueueReplaceSuccess();

        var result = await coordinator.PushAsync(session, Dimensions, NoOpenMaps, null, null, CancellationToken.None);

        Assert.IsType<PushedResult>(result);
        Assert.False(session.Edits.IsDirty);
        Assert.Equal(new SpawnSnapshot(session.Edits.Spawns), session.PulledSpawns);
        Assert.Equal(new WarpSnapshot(session.Edits.Warps), session.PulledWarps);
        Assert.False(session.RequiresPull);
        var write = Assert.Single(gateway.Calls, call => call.Method == "ReplaceOwnedRowsAsync");
        Assert.NotNull(write.SpawnPlan);
        Assert.Empty(write.SpawnPlan.Deletes);
        Assert.Equal(new[] { "2", "5", "9", "9" }, Assert.Single(write.SpawnPlan.Inserts).CellValues);
        Assert.Empty(delays);
    }

    [Fact]
    public async Task Push_OtherMapRows_NeverEnterPlans()
    {
        var (coordinator, gateway, _) = NewCoordinator();
        var session = NewSession();
        session.Edits.RemoveSpawnAt(1);
        session.Edits.AddSpawn(new NpcSpawnRow(2, Map, 9, 9));
        gateway.EnqueueOwnedRows(new RemoteOwnedRows(
            new[]
            {
                Spawn(2, 1, Map, 1, 2),
                Spawn(3, 1, OtherMap, 1, 2),
                Spawn(4, 2, Map, 5, 6),
                Spawn(5, 1, OtherMap, 9, 9)
            },
            new[]
            {
                Warp(2, Map, 3, 4, 10, 5, 6),
                Warp(3, OtherMap, 7, 8, 10, 9, 10),
                Warp(4, Map, 7, 8, 10, 9, 10)
            }));
        gateway.EnqueueReplaceSuccess();

        var result = await coordinator.PushAsync(session, Dimensions, NoOpenMaps, null, null, CancellationToken.None);

        Assert.IsType<PushedResult>(result);
        var write = Assert.Single(gateway.Calls, call => call.Method == "ReplaceOwnedRowsAsync");
        Assert.NotNull(write.SpawnPlan);
        Assert.NotNull(write.WarpPlan);
        Assert.Equal(new[] { new RowDelete(4) }, write.SpawnPlan.Deletes);
        Assert.Equal(new[] { "2", "5", "9", "9" }, Assert.Single(write.SpawnPlan.Inserts).CellValues);
        Assert.Empty(write.WarpPlan.Deletes);
        Assert.Empty(write.WarpPlan.Inserts);
    }

    [Fact]
    public async Task Push_PreflightHeaderFailure_PropagatesWithoutWriteOrLatch()
    {
        var (coordinator, gateway, _) = NewCoordinator();
        var session = NewSession();
        session.Edits.AddSpawn(new NpcSpawnRow(2, Map, 9, 9));
        gateway.EnqueueOwnedRowsFailure(GatewayFailureKind.Schema);

        var ex = await Assert.ThrowsAsync<GameDataGatewayException>(
            () => coordinator.PushAsync(session, Dimensions, NoOpenMaps, null, null, CancellationToken.None));

        Assert.Equal(GatewayFailureKind.Schema, ex.Kind);
        Assert.DoesNotContain(gateway.Calls, call => call.Method == "ReplaceOwnedRowsAsync");
        Assert.True(session.Edits.IsDirty);
        Assert.False(session.RequiresPull);
    }

    [Fact]
    public async Task Write_RetriesExplicitRejectedRateLimit_UpToThreeAttempts()
    {
        var (coordinator, gateway, delays) = NewCoordinator();
        var session = NewSession();
        session.Edits.AddSpawn(new NpcSpawnRow(2, Map, 9, 9));
        gateway.EnqueueOwnedRows(BaseOwnedRows());
        gateway.EnqueueReplaceFailure(GatewayFailureKind.RateLimited, requestWasRejected: true);
        gateway.EnqueueReplaceFailure(GatewayFailureKind.RateLimited, requestWasRejected: true);
        gateway.EnqueueReplaceSuccess();

        var result = await coordinator.PushAsync(session, Dimensions, NoOpenMaps, null, null, CancellationToken.None);

        Assert.IsType<PushedResult>(result);
        Assert.Equal(3, gateway.Calls.Count(call => call.Method == "ReplaceOwnedRowsAsync"));
        Assert.Equal(new[] { TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(1) }, delays);
        Assert.False(session.Edits.IsDirty);
    }

    [Fact]
    public async Task Write_RejectedRateLimitExhausted_ThrowsAndStaysDirtyButNotAmbiguous()
    {
        var (coordinator, gateway, delays) = NewCoordinator();
        var session = NewSession();
        session.Edits.AddSpawn(new NpcSpawnRow(2, Map, 9, 9));
        gateway.EnqueueOwnedRows(BaseOwnedRows());
        gateway.EnqueueReplaceFailure(GatewayFailureKind.RateLimited, requestWasRejected: true);
        gateway.EnqueueReplaceFailure(GatewayFailureKind.RateLimited, requestWasRejected: true);
        gateway.EnqueueReplaceFailure(GatewayFailureKind.RateLimited, requestWasRejected: true);

        var ex = await Assert.ThrowsAsync<GameDataGatewayException>(
            () => coordinator.PushAsync(session, Dimensions, NoOpenMaps, null, null, CancellationToken.None));

        Assert.Equal(GatewayFailureKind.RateLimited, ex.Kind);
        Assert.True(ex.RequestWasRejected);
        Assert.Equal(3, gateway.Calls.Count(call => call.Method == "ReplaceOwnedRowsAsync"));
        Assert.Equal(new[] { TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(1) }, delays);
        Assert.True(session.Edits.IsDirty);
        Assert.False(session.RequiresPull);
    }

    [Fact]
    public async Task Write_NonRejectedRateLimit_DoesNotRetry_IsAmbiguous()
    {
        var (coordinator, gateway, delays) = NewCoordinator();
        var session = NewSession();
        session.Edits.AddSpawn(new NpcSpawnRow(2, Map, 9, 9));
        gateway.EnqueueOwnedRows(BaseOwnedRows());
        gateway.EnqueueReplaceFailure(GatewayFailureKind.RateLimited, requestWasRejected: false);

        var result = await coordinator.PushAsync(session, Dimensions, NoOpenMaps, null, null, CancellationToken.None);

        Assert.IsType<AmbiguousResult>(result);
        Assert.Single(gateway.Calls, call => call.Method == "ReplaceOwnedRowsAsync");
        Assert.Empty(delays);
        Assert.True(session.Edits.IsDirty);
        Assert.True(session.RequiresPull);
    }

    [Theory]
    [InlineData(GatewayFailureKind.Authentication)]
    [InlineData(GatewayFailureKind.Permission)]
    public async Task Write_AuthenticationOrPermission_DoesNotRetry_IsAmbiguous(GatewayFailureKind kind)
    {
        var (coordinator, gateway, delays) = NewCoordinator();
        var session = NewSession();
        session.Edits.AddSpawn(new NpcSpawnRow(2, Map, 9, 9));
        gateway.EnqueueOwnedRows(BaseOwnedRows());
        gateway.EnqueueReplaceFailure(kind);

        var result = await coordinator.PushAsync(session, Dimensions, NoOpenMaps, null, null, CancellationToken.None);

        Assert.IsType<AmbiguousResult>(result);
        Assert.Single(gateway.Calls, call => call.Method == "ReplaceOwnedRowsAsync");
        Assert.Empty(delays);
        Assert.True(session.Edits.IsDirty);
        Assert.True(session.RequiresPull);
    }

    [Theory]
    [InlineData(GatewayFailureKind.Temporary)]
    [InlineData(GatewayFailureKind.Transport)]
    [InlineData(GatewayFailureKind.Schema)]
    public async Task Write_5xxTimeoutResetOrMalformedSuccess_IsAmbiguous(GatewayFailureKind kind)
    {
        var (coordinator, gateway, delays) = NewCoordinator();
        var session = NewSession();
        session.Edits.AddSpawn(new NpcSpawnRow(2, Map, 9, 9));
        gateway.EnqueueOwnedRows(BaseOwnedRows());
        gateway.EnqueueReplaceFailure(kind);

        var result = await coordinator.PushAsync(session, Dimensions, NoOpenMaps, null, null, CancellationToken.None);

        Assert.IsType<AmbiguousResult>(result);
        Assert.Single(gateway.Calls, call => call.Method == "ReplaceOwnedRowsAsync");
        Assert.Empty(delays);
        Assert.True(session.Edits.IsDirty);
        Assert.True(session.RequiresPull);
    }

    [Fact]
    public async Task Write_CancellationAfterDispatch_IsAmbiguous()
    {
        var (coordinator, gateway, _) = NewCoordinator();
        var session = NewSession();
        session.Edits.AddSpawn(new NpcSpawnRow(2, Map, 9, 9));
        var cts = new CancellationTokenSource();
        gateway.EnqueueOwnedRows(BaseOwnedRows());
        gateway.OnReplaceDispatch = ct => cts.Cancel();
        gateway.EnqueueReplaceException(new OperationCanceledException("cancelled during dispatch"));

        var result = await coordinator.PushAsync(session, Dimensions, NoOpenMaps, null, null, cts.Token);

        Assert.IsType<AmbiguousResult>(result);
        Assert.True(session.Edits.IsDirty);
        Assert.True(session.RequiresPull);
    }

    [Fact]
    public async Task Write_CancellationBeforeDispatch_ReturnsCancelled_WithoutLatching()
    {
        var (coordinator, gateway, _) = NewCoordinator();
        var session = NewSession();
        session.Edits.AddSpawn(new NpcSpawnRow(2, Map, 9, 9));
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var conflictRows = new RemoteOwnedRows(
            new[] { Spawn(2, 1, Map, 1, 2), Spawn(3, 2, Map, 5, 6) },
            new[] { Warp(2, Map, 3, 4, 10, 5, 6), Warp(3, Map, 7, 8, 10, 9, 10) });

        var result = await coordinator.PushAsync(session, Dimensions, NoOpenMaps,
            PushConflictChoice.Overwrite, conflictRows, cts.Token);

        Assert.IsType<CancelledResult>(result);
        Assert.Empty(gateway.Calls);
        Assert.True(session.Edits.IsDirty);
        Assert.False(session.RequiresPull);
    }

    [Fact]
    public async Task Push_AfterAmbiguous_IsBlockedWithoutAnyGatewayCall()
    {
        var (coordinator, gateway, _) = NewCoordinator();
        var session = NewSession();
        session.Edits.AddSpawn(new NpcSpawnRow(2, Map, 9, 9));
        gateway.EnqueueOwnedRows(BaseOwnedRows());
        gateway.EnqueueReplaceFailure(GatewayFailureKind.Transport);

        var first = await coordinator.PushAsync(session, Dimensions, NoOpenMaps, null, null, CancellationToken.None);
        Assert.IsType<AmbiguousResult>(first);
        var callsAfterFirst = gateway.Calls.Count;

        var second = await coordinator.PushAsync(session, Dimensions, NoOpenMaps, null, null, CancellationToken.None);

        var rejected = Assert.IsType<ValidationRejectedResult>(second);
        Assert.Equal("sync-pull-required", Assert.Single(rejected.Validation.Errors).Code);
        Assert.Equal(callsAfterFirst, gateway.Calls.Count);
    }

    [Fact]
    public async Task Pull_AfterAmbiguous_ClearsLatchInFreshSession()
    {
        var (coordinator, gateway, _) = NewCoordinator();
        var session = NewSession();
        session.Edits.AddSpawn(new NpcSpawnRow(2, Map, 9, 9));
        session.MarkAmbiguous();
        Assert.True(session.RequiresPull);
        gateway.EnqueueMaps(new[] { MapRef(Map), MapRef(10) });
        gateway.EnqueueGameData(BaseData());

        var result = await coordinator.PullAsync(Sheet, Map, session, discardApproved: true, CancellationToken.None);

        var fresh = Assert.IsType<PullSucceededResult>(result).Session;
        Assert.False(fresh.RequiresPull);
        Assert.False(fresh.Edits.IsDirty);
        Assert.True(session.RequiresPull);
    }
}
