using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MapEditor.GameData.Connectivity;
using MapEditor.GameData.Replacement;
using MapEditor.GameData.Rows;

namespace MapEditor.GameData.Tests.Fakes;

public sealed class ScriptedGameDataGateway : IGameDataGateway
{
    public sealed record Call(
        string Method,
        string SpreadsheetId,
        int MapId,
        ReplacementPlan? SpawnPlan,
        ReplacementPlan? WarpPlan);

    public List<Call> Calls { get; } = new();

    public Action<CancellationToken>? OnReplaceDispatch { get; set; }

    private readonly Queue<object?> _maps = new();
    private readonly Queue<object?> _gameData = new();
    private readonly Queue<object?> _ownedRows = new();
    private readonly Queue<object?> _replace = new();

    public ScriptedGameDataGateway EnqueueMaps(IReadOnlyList<MapReference> result)
    {
        _maps.Enqueue(result);
        return this;
    }

    public ScriptedGameDataGateway EnqueueGameData(RemoteGameData result)
    {
        _gameData.Enqueue(result);
        return this;
    }

    public ScriptedGameDataGateway EnqueueOwnedRows(RemoteOwnedRows result)
    {
        _ownedRows.Enqueue(result);
        return this;
    }

    public ScriptedGameDataGateway EnqueueReplaceSuccess()
    {
        _replace.Enqueue(null);
        return this;
    }

    public ScriptedGameDataGateway EnqueueMapsFailure(GatewayFailureKind kind, bool requestWasRejected = false)
        => EnqueueFailure(_maps, kind, requestWasRejected);

    public ScriptedGameDataGateway EnqueueGameDataFailure(GatewayFailureKind kind, bool requestWasRejected = false)
        => EnqueueFailure(_gameData, kind, requestWasRejected);

    public ScriptedGameDataGateway EnqueueOwnedRowsFailure(GatewayFailureKind kind, bool requestWasRejected = false)
        => EnqueueFailure(_ownedRows, kind, requestWasRejected);

    public ScriptedGameDataGateway EnqueueReplaceFailure(GatewayFailureKind kind, bool requestWasRejected = false)
        => EnqueueFailure(_replace, kind, requestWasRejected);

    public ScriptedGameDataGateway EnqueueMapsException(Exception exception)
    {
        _maps.Enqueue(exception);
        return this;
    }

    public ScriptedGameDataGateway EnqueueGameDataException(Exception exception)
    {
        _gameData.Enqueue(exception);
        return this;
    }

    public ScriptedGameDataGateway EnqueueOwnedRowsException(Exception exception)
    {
        _ownedRows.Enqueue(exception);
        return this;
    }

    public ScriptedGameDataGateway EnqueueReplaceException(Exception exception)
    {
        _replace.Enqueue(exception);
        return this;
    }

    public Task<IReadOnlyList<MapReference>> ReadMapsAsync(string spreadsheetId, CancellationToken cancellationToken)
    {
        Calls.Add(new Call("ReadMapsAsync", spreadsheetId, 0, null, null));
        return Next<IReadOnlyList<MapReference>>(_maps);
    }

    public Task<RemoteGameData> ReadGameDataAsync(string spreadsheetId, int mapId, CancellationToken cancellationToken)
    {
        Calls.Add(new Call("ReadGameDataAsync", spreadsheetId, mapId, null, null));
        return Next<RemoteGameData>(_gameData);
    }

    public Task<RemoteOwnedRows> ReadOwnedRowsAsync(string spreadsheetId, int mapId, CancellationToken cancellationToken)
    {
        Calls.Add(new Call("ReadOwnedRowsAsync", spreadsheetId, mapId, null, null));
        return Next<RemoteOwnedRows>(_ownedRows);
    }

    public Task ReplaceOwnedRowsAsync(
        string spreadsheetId,
        ReplacementPlan spawnPlan,
        ReplacementPlan warpPlan,
        CancellationToken cancellationToken)
    {
        Calls.Add(new Call("ReplaceOwnedRowsAsync", spreadsheetId, 0, spawnPlan, warpPlan));
        OnReplaceDispatch?.Invoke(cancellationToken);
        return Next<Task>(_replace);
    }

    private ScriptedGameDataGateway EnqueueFailure(
        Queue<object?> queue,
        GatewayFailureKind kind,
        bool requestWasRejected)
    {
        queue.Enqueue(new GameDataGatewayException(kind, "scripted-sheet", requestWasRejected, "scripted failure"));
        return this;
    }

    private static Task<T> Next<T>(Queue<object?> queue)
    {
        if (queue.Count == 0)
        {
            throw new InvalidOperationException("No scripted result left for the call.");
        }

        var entry = queue.Dequeue();
        if (entry is Exception exception)
        {
            return Task.FromException<T>(exception);
        }

        return Task.FromResult((T)entry!);
    }
}
