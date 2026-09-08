using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MapEditor.App.Dialogs;
using MapEditor.App.ViewModels;
using MapEditor.GameData.Connectivity;
using MapEditor.GameData.Rows;
using MapEditor.GameData.Sync;
using MapEditor.GameData.Validation;

namespace MapEditor.App.Connectivity;

internal interface IGameDataConnectivity
{
    bool IsConnected { get; }
    GameDataSyncCoordinator? Coordinator { get; }
    SpreadsheetReference? RememberedSpreadsheet { get; }
    bool TryRememberSpreadsheet(string? pastedUrl);
    Task ConnectAsync(CancellationToken cancellationToken);
    Task DisconnectAsync(CancellationToken cancellationToken);
}

// The composed owner is sealed; the bridge exposes its surface through the seam the workspace and
// controller depend on so tests can script connection and coordinator without Google types.
internal sealed class GameDataConnectivityBridge : IGameDataConnectivity
{
    private readonly GameDataConnectivity _owner;

    public GameDataConnectivityBridge(GameDataConnectivity owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    public bool IsConnected => _owner.IsConnected;

    public GameDataSyncCoordinator? Coordinator => _owner.Coordinator;

    public SpreadsheetReference? RememberedSpreadsheet => _owner.RememberedSpreadsheet;

    public bool TryRememberSpreadsheet(string? pastedUrl) => _owner.TryRememberSpreadsheet(pastedUrl);

    public Task ConnectAsync(CancellationToken cancellationToken) => _owner.ConnectAsync(cancellationToken);

    public Task DisconnectAsync(CancellationToken cancellationToken) => _owner.DisconnectAsync(cancellationToken);
}

internal sealed class GameDataCommandController
{
    private const string PullTitle = "Pull game data";
    private const string PushTitle = "Push game data";
    private const string UntitledName = "Untitled";

    private readonly IGameDataConnectivity? _connectivity;
    private readonly IEditorDialogs _dialogs;
    private readonly WorkspaceViewModel _workspace;
    private readonly SemaphoreSlim _commandGate = new(1, 1);

    public GameDataCommandController(
        IGameDataConnectivity? connectivity,
        IEditorDialogs dialogs,
        WorkspaceViewModel workspace)
    {
        _connectivity = connectivity;
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
    }

    public event Action? StateChanged;

    public bool IsConnected => _connectivity?.IsConnected ?? false;

    public bool CanPull => IsConnected;

    public bool CanPush(MapDocumentViewModel document)
        => IsConnected && document.GameData is { HasSession: true, RequiresPull: false };

    public Task<bool> ConnectAsync() => RunGuardedAsync(ConnectCoreAsync);

    public Task<bool> DisconnectAsync() => RunGuardedAsync(DisconnectCoreAsync);

    public Task<bool> PullAsync(MapDocumentViewModel document)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        return RunGuardedAsync(() => PullCoreAsync(document));
    }

    public Task<bool> PushAsync(MapDocumentViewModel document)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        return RunGuardedAsync(() => PushCoreAsync(document));
    }

    // A second caller while a command is in flight is rejected immediately so that concurrent
    // Push/Pull pairs cannot interleave around a session swap; internal pushes route around this gate.
    private async Task<bool> RunGuardedAsync(Func<Task<bool>> command)
    {
        if (!_commandGate.Wait(0))
        {
            return false;
        }

        try
        {
            return await command();
        }
        finally
        {
            _commandGate.Release();
        }
    }

    private async Task<bool> ConnectCoreAsync()
    {
        if (_connectivity is null)
        {
            await ShowErrorAsync("Connect Google", "Google connectivity is not available.");
            return false;
        }

        if (_connectivity.IsConnected)
        {
            return true;
        }

        try
        {
            await _connectivity.ConnectAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Connect Google", ex.Message);
            return false;
        }

        RaiseStateChanged();
        return true;
    }

    private async Task<bool> DisconnectCoreAsync()
    {
        if (_connectivity is null || !_connectivity.IsConnected)
        {
            return true;
        }

        foreach (var document in _workspace.Documents.ToList())
        {
            if (_workspace.Documents.IndexOf(document) < 0 || document.GameData is not { IsDirty: true })
            {
                continue;
            }

            _workspace.Activate(document);
            SheetDirtyChoice choice = await _dialogs.ShowSheetDirtyAsync(DocumentName(document));
            if (choice == SheetDirtyChoice.Cancel)
            {
                return false;
            }

            if (choice == SheetDirtyChoice.Push && !await PushCoreAsync(document))
            {
                return false;
            }
        }

        try
        {
            await _connectivity.DisconnectAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Disconnect Google", ex.Message);
            return false;
        }

        RaiseStateChanged();
        return true;
    }

    private async Task<bool> PullCoreAsync(MapDocumentViewModel document)
    {
        if (_connectivity is null || !_connectivity.IsConnected || _connectivity.Coordinator is not { } coordinator)
        {
            await ShowErrorAsync(PullTitle, "Connect to Google before pulling.");
            return false;
        }

        if (document.GameData is not { } state)
        {
            await ShowErrorAsync(PullTitle, "This tab has no game data state.");
            return false;
        }

        string? entered = await _dialogs.ShowSpreadsheetUrlAsync(_connectivity.RememberedSpreadsheet?.CanonicalUrl);
        if (entered is null)
        {
            return false;
        }

        if (!_connectivity.TryRememberSpreadsheet(entered))
        {
            await ShowErrorAsync(PullTitle, $"'{entered}' is not a valid Google spreadsheet URL.");
            return false;
        }

        string spreadsheetId = _connectivity.RememberedSpreadsheet!.Value.Id;

        SyncResult catalog;
        try
        {
            catalog = await coordinator.LoadMapCatalogAsync(spreadsheetId, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (GameDataGatewayException ex) when (ex.Kind == GatewayFailureKind.Authentication)
        {
            await ShowErrorAsync(PullTitle, "Google rejected the stored authorization. Connect again before pulling.");
            return false;
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(PullTitle, ex.Message);
            return false;
        }

        if (catalog is not MapCatalogResult { } mapsResult)
        {
            return await PresentFailureAsync(catalog, PullTitle);
        }

        var maps = mapsResult.Maps.OrderBy(map => map.MapId).ToList();
        MapReference? confirmed = await _dialogs.ShowMapConfirmationAsync(maps, SuggestMap(maps, document), DocumentName(document));
        if (confirmed is not { } map)
        {
            return false;
        }

        bool discardApproved = false;
        if (state.IsDirty)
        {
            SheetDirtyChoice choice = await _dialogs.ShowSheetDirtyAsync(DocumentName(document));
            if (choice == SheetDirtyChoice.Cancel)
            {
                return false;
            }

            if (choice == SheetDirtyChoice.Push)
            {
                if (!await PushCoreAsync(document))
                {
                    return false;
                }
            }
            else
            {
                discardApproved = true;
            }
        }

        SyncResult pull;
        try
        {
            pull = await coordinator.PullAsync(spreadsheetId, map.MapId, state.Session, discardApproved, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (GameDataGatewayException ex) when (ex.Kind == GatewayFailureKind.Authentication)
        {
            await ShowErrorAsync(PullTitle, "Google rejected the stored authorization. Connect again before pulling.");
            return false;
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(PullTitle, ex.Message);
            return false;
        }

        if (pull is PullSucceededResult { } succeeded)
        {
            state.AttachSession(succeeded.Session);
            RaiseStateChanged();
            return true;
        }

        return await PresentFailureAsync(pull, PullTitle);
    }

    private async Task<bool> PushCoreAsync(MapDocumentViewModel document)
    {
        if (_connectivity is null || !_connectivity.IsConnected || _connectivity.Coordinator is not { } coordinator)
        {
            await ShowErrorAsync(PushTitle, "Connect to Google before pushing.");
            return false;
        }

        if (document.GameData is not { HasSession: true } state || state.Session is not { } session)
        {
            await ShowErrorAsync(PushTitle, "This tab has no pulled game data.");
            return false;
        }

        var current = new MapDimensions(document.MapWidth, document.MapHeight);
        var open = OpenDimensions(document, session);

        SyncResult result;
        try
        {
            result = await coordinator.PushAsync(session, current, open, null, null, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (GameDataGatewayException ex) when (ex.Kind == GatewayFailureKind.Authentication)
        {
            await ShowErrorAsync(PushTitle, "Google rejected the stored authorization. Connect again before pushing.");
            return false;
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(PushTitle, ex.Message);
            return false;
        }

        return await ResolvePushAsync(coordinator, document, current, open, result);
    }

    private async Task<bool> ResolvePushAsync(
        GameDataSyncCoordinator coordinator,
        MapDocumentViewModel document,
        MapDimensions current,
        IReadOnlyDictionary<int, MapDimensions> open,
        SyncResult result)
    {
        GameDataSyncSession session = document.GameData!.Session!;
        while (true)
        {
            switch (result)
            {
                case PushedResult { } pushed:
                    await _dialogs.ShowInfoAsync(PushTitle, PushSuccessMessage(pushed.ChangedRows));
                    RaiseStateChanged();
                    return true;
                case AmbiguousResult:
                    await ShowErrorAsync(PushTitle, "The push result is unconfirmed. Pull before pushing again.");
                    return false;
                case CancelledResult:
                    return false;
                case ValidationRejectedResult { } validation:
                    await ShowValidationAsync(validation.Validation, PushTitle);
                    return false;
                case PushConflictResult { } conflict:
                {
                    PushConflictChoice choice = await _dialogs.ShowPushConflictAsync(DocumentName(document));
                    if (choice == PushConflictChoice.Cancel)
                    {
                        return false;
                    }

                    try
                    {
                        result = await coordinator.PushAsync(
                            session,
                            current,
                            open,
                            choice,
                            choice == PushConflictChoice.Overwrite ? conflict.LatestRemoteRows : null,
                            CancellationToken.None);
                    }
                    catch (OperationCanceledException)
                    {
                        return false;
                    }
                    catch (GameDataGatewayException ex) when (ex.Kind == GatewayFailureKind.Authentication)
                    {
                        await ShowErrorAsync(PushTitle, "Google rejected the stored authorization. Connect again before pushing.");
                        return false;
                    }
                    catch (Exception ex)
                    {
                        await ShowErrorAsync(PushTitle, ex.Message);
                        return false;
                    }

                    if (result is PullInsteadRequestedResult)
                    {
                        return await PullInsteadAsync(coordinator, document, session);
                    }

                    break;
                }
                default:
                    await ShowErrorAsync(PushTitle, "The push could not be completed.");
                    return false;
            }
        }
    }

    private async Task<bool> PullInsteadAsync(
        GameDataSyncCoordinator coordinator,
        MapDocumentViewModel document,
        GameDataSyncSession session)
    {
        try
        {
            SyncResult pull = await coordinator.PullAsync(session.SpreadsheetId, session.MapId, session, true, CancellationToken.None);
            if (pull is PullSucceededResult { } succeeded)
            {
                document.GameData!.AttachSession(succeeded.Session);
                RaiseStateChanged();
                return true;
            }

            return await PresentFailureAsync(pull, PushTitle);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(PushTitle, ex.Message);
            return false;
        }
    }

    private Dictionary<int, MapDimensions> OpenDimensions(MapDocumentViewModel document, GameDataSyncSession session)
    {
        var dimensions = new Dictionary<int, MapDimensions>();
        foreach (var other in _workspace.Documents)
        {
            if (ReferenceEquals(other, document) || other.GameData?.Session is not { } otherSession)
            {
                continue;
            }

            if (!string.Equals(otherSession.SpreadsheetId, session.SpreadsheetId, StringComparison.Ordinal))
            {
                continue;
            }

            dimensions[otherSession.MapId] = new MapDimensions(other.MapWidth, other.MapHeight);
        }

        return dimensions;
    }

    private static MapReference? SuggestMap(IReadOnlyList<MapReference> maps, MapDocumentViewModel document)
    {
        if (document.GameData?.ConfirmedMapId is { } mapId)
        {
            foreach (MapReference map in maps)
            {
                if (map.MapId == mapId)
                {
                    return map;
                }
            }
        }

        if (document.Document.Path is not { } path)
        {
            return null;
        }

        string basename = Path.GetFileNameWithoutExtension(path);
        var matches = maps
            .Where(map => string.Equals(Path.GetFileNameWithoutExtension(map.MapFilename), basename, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    private static string PushSuccessMessage(int changedRows)
        => changedRows == 0
            ? "Nothing to push — the sheet already matches this tab's game data."
            : $"Successfully pushed {changedRows} {(changedRows == 1 ? "row" : "rows")} to the sheet.";

    private async Task<bool> PresentFailureAsync(SyncResult result, string title)
    {
        switch (result)
        {
            case ValidationRejectedResult { } validation:
                await ShowValidationAsync(validation.Validation, title);
                return false;
            case DirtyLocalRejectedResult:
                await ShowErrorAsync(title, "Local sheet data is dirty; push or discard it before pulling.");
                return false;
            case CancelledResult:
                return false;
            default:
                await ShowErrorAsync(title, "The operation could not be completed.");
                return false;
        }
    }

    private Task ShowValidationAsync(ValidationResult validation, string title)
    {
        string message = string.Join(Environment.NewLine, validation.Errors.Select(issue => issue.Message));
        return ShowErrorAsync(title, message);
    }

    private Task ShowErrorAsync(string title, string message)
        => _dialogs.ShowErrorAsync(new ErrorPresentation(title, message));

    private static string DocumentName(MapDocumentViewModel document)
        => document.Document.Path is { } path ? Path.GetFileName(path) : UntitledName;

    private void RaiseStateChanged() => StateChanged?.Invoke();
}
