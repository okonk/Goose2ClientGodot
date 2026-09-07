using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MapEditor.GameData.Connectivity;
using MapEditor.GameData.Replacement;
using MapEditor.GameData.Rows;
using MapEditor.GameData.Schema;
using MapEditor.GameData.Snapshots;
using MapEditor.GameData.Validation;

namespace MapEditor.GameData.Sync;

public sealed class GameDataSyncCoordinator
{
    private const string MapNotFoundCode = "sync-map-not-found";
    private const string MapDuplicateCode = "sync-map-duplicate";
    private const string PullRequiredCode = "sync-pull-required";

    private readonly IGameDataGateway _gateway;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly GameDataValidator _validator;
    private readonly ReplacementPlanner _planner;

    public GameDataSyncCoordinator(IGameDataGateway gateway)
        : this(gateway, (timeSpan, cancellationToken) => Task.Delay(timeSpan, cancellationToken))
    {
    }

    public GameDataSyncCoordinator(IGameDataGateway gateway, Func<TimeSpan, CancellationToken, Task> delay)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _delay = delay ?? throw new ArgumentNullException(nameof(delay));
        var schema = GameDataSchema.LoadEmbedded();
        _validator = new GameDataValidator(schema);
        _planner = new ReplacementPlanner(new SheetRowMapper(schema));
    }

    public async Task<SyncResult> LoadMapCatalogAsync(string spreadsheetId, CancellationToken cancellationToken)
    {
        var maps = await LoadCatalogAsync(spreadsheetId, cancellationToken).ConfigureAwait(false);
        return new MapCatalogResult(maps);
    }

    public async Task<SyncResult> PullAsync(
        string spreadsheetId,
        int mapId,
        GameDataSyncSession? current,
        bool discardApproved,
        CancellationToken cancellationToken)
    {
        if (spreadsheetId is null)
        {
            throw new ArgumentNullException(nameof(spreadsheetId));
        }

        if (current is not null && current.Edits.IsDirty && !discardApproved)
        {
            return new DirtyLocalRejectedResult();
        }

        var maps = await LoadCatalogAsync(spreadsheetId, cancellationToken).ConfigureAwait(false);
        if (!maps.Any(map => map.MapId == mapId))
        {
            return new ValidationRejectedResult(SingleIssue(
                MapNotFoundCode,
                $"Map '{mapId}' was not found in spreadsheet '{spreadsheetId}'."));
        }

        var pulled = await RetryPolicy.RunReadAsync(
            ct => _gateway.ReadGameDataAsync(spreadsheetId, mapId, ct),
            _delay,
            cancellationToken).ConfigureAwait(false);

        return new PullSucceededResult(new GameDataSyncSession(spreadsheetId, mapId, pulled));
    }

    public async Task<SyncResult> PushAsync(
        GameDataSyncSession session,
        MapDimensions currentMapDimensions,
        IReadOnlyDictionary<int, MapDimensions> openMapDimensions,
        PushConflictChoice? choice,
        RemoteOwnedRows? conflictRows,
        CancellationToken cancellationToken)
    {
        if (session is null)
        {
            throw new ArgumentNullException(nameof(session));
        }
        if (openMapDimensions is null)
        {
            throw new ArgumentNullException(nameof(openMapDimensions));
        }

        if (session.RequiresPull)
        {
            return new ValidationRejectedResult(SingleIssue(
                PullRequiredCode,
                "A previous push outcome is unconfirmed; complete a Pull before pushing again."));
        }

        var mapsByMapId = new Dictionary<int, MapReference>();
        foreach (var map in session.Maps)
        {
            if (!mapsByMapId.TryAdd(map.MapId, map))
            {
                return new ValidationRejectedResult(SingleIssue(
                    MapDuplicateCode,
                    $"Spreadsheet '{session.SpreadsheetId}' contains duplicate map id {map.MapId}."));
            }
        }
        var spawnValidation = _validator.ValidateSpawns(session.Edits.Spawns, session.Npcs, currentMapDimensions);
        var warpValidation = _validator.ValidateWarps(session.Edits.Warps, mapsByMapId, openMapDimensions, currentMapDimensions);
        if (!spawnValidation.IsValid || !warpValidation.IsValid)
        {
            return new ValidationRejectedResult(new ValidationResult(
                spawnValidation.Errors.Concat(warpValidation.Errors).ToList().AsReadOnly()));
        }

        bool freshRead;
        RemoteOwnedRows latest;
        if (choice == PushConflictChoice.Overwrite && conflictRows is not null)
        {
            // Accepted race: the remote may change between the conflict read and this write; the
            // write blindly replaces the owned rows, and the window is exactly the conflict-read/write window.
            latest = conflictRows;
            freshRead = false;
        }
        else
        {
            latest = await RetryPolicy.RunReadAsync(
                ct => _gateway.ReadOwnedRowsAsync(session.SpreadsheetId, session.MapId, ct),
                _delay,
                cancellationToken).ConfigureAwait(false);
            freshRead = true;
        }

        // Pulled snapshots hold only the target map's rows; compare against the same subset.
        var latestSpawns = latest.Spawns
            .Where(row => row.Value.MapId == session.MapId)
            .Select(row => row.Value)
            .ToList();
        var latestWarps = latest.Warps
            .Where(row => row.Value.MapId == session.MapId)
            .Select(row => row.Value)
            .ToList();

        if (freshRead && (
            !SnapshotComparer.Equals(session.PulledSpawns.Rows, latestSpawns) ||
            !SnapshotComparer.Equals(session.PulledWarps.Rows, latestWarps)))
        {
            return choice switch
            {
                PushConflictChoice.PullInstead => new PullInsteadRequestedResult(),
                PushConflictChoice.Cancel => new CancelledResult(),
                _ => new PushConflictResult(latest)
            };
        }

        if (!session.Edits.IsDirty)
        {
            session.MarkPushed();
            return new PushedResult();
        }

        var spawnPlan = _planner.PlanSpawnReplacement(latest.Spawns, session.Edits.Spawns, session.MapId);
        var warpPlan = _planner.PlanWarpReplacement(latest.Warps, session.Edits.Warps, session.MapId);

        async Task<SyncResult> DispatchAsync()
        {
            try
            {
                await _gateway.ReplaceOwnedRowsAsync(session.SpreadsheetId, spawnPlan, warpPlan, cancellationToken)
                    .ConfigureAwait(false);
                session.MarkPushed();
                return new PushedResult();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                session.MarkAmbiguous();
                return new AmbiguousResult();
            }
            catch (GameDataGatewayException ex) when (RetryPolicy.IsWriteRetriable(ex.Kind, ex.RequestWasRejected))
            {
                throw;
            }
            catch (Exception)
            {
                session.MarkAmbiguous();
                return new AmbiguousResult();
            }
        }

        for (var attempt = 1; attempt < RetryPolicy.MaxAttempts; attempt++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return new CancelledResult();
            }

            try
            {
                return await DispatchAsync().ConfigureAwait(false);
            }
            catch (GameDataGatewayException)
            {
                await _delay(RetryPolicy.Delays[attempt - 1], cancellationToken).ConfigureAwait(false);
            }
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return new CancelledResult();
        }

        return await DispatchAsync().ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<MapReference>> LoadCatalogAsync(
        string spreadsheetId,
        CancellationToken cancellationToken)
    {
        if (spreadsheetId is null)
        {
            throw new ArgumentNullException(nameof(spreadsheetId));
        }

        return await RetryPolicy.RunReadAsync(
            ct => _gateway.ReadMapsAsync(spreadsheetId, ct),
            _delay,
            cancellationToken).ConfigureAwait(false);
    }

    private static ValidationResult SingleIssue(string code, string message)
        => new(new[] { new ValidationIssue(code, message) }.AsReadOnly());
}
