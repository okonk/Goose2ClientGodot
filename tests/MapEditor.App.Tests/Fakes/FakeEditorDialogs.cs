using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MapEditor.App.Dialogs;
using MapEditor.Core;
using MapEditor.GameData.Rows;
using MapEditor.GameData.Sync;

namespace MapEditor.App.Tests.Fakes;

internal sealed class FakeEditorDialogs : IEditorDialogs
{
    public NewMapRequest? NewMapResult;
    public MapTileRectangle? ResizeMapResult;
    public DirtyChoice DirtyResult = DirtyChoice.Cancel;
    public ExternalChangeChoice ExternalChangeResult = ExternalChangeChoice.Cancel;
    public Queue<ExternalChangeChoice>? ExternalChangeChoices;
    public string? OpenPickResult;
    public string? SavePickResult;
    public string? AssetDirectoryPickResult { get; set; }
    public int AssetDirectoryPickShown;
    public string? LastSaveSuggestedName;
    public string? SpreadsheetUrlResult;
    public int SpreadsheetUrlShown;
    public string? LastSpreadsheetUrlPrefill;
    public MapReference? MapConfirmationResult;
    public int MapConfirmationShown;
    public IReadOnlyList<MapReference>? LastMapConfirmationMaps;
    public MapReference? LastMapConfirmationSuggested;
    public string? LastMapConfirmationDocumentName;
    public SheetDirtyChoice SheetDirtyResult = SheetDirtyChoice.Cancel;
    public int SheetDirtyShown;
    public string? LastSheetDirtyDocumentName;
    public Queue<SheetDirtyChoice>? SheetDirtyChoices;
    public PushConflictChoice PushConflictResult = PushConflictChoice.Cancel;
    public int PushConflictShown;
    public string? LastPushConflictDocumentName;
    public Queue<PushConflictChoice>? PushConflictChoices;
    public Exception? ShowNewMapException;
    public Exception? PickOpenException;
    public Exception? PickAssetDirectoryException;
    public Exception? PickSaveException;
    public Exception? ShowDirtyException;
    public Exception? ShowSpreadsheetUrlException;
    public Exception? ShowMapConfirmationException;
    public Exception? ShowSheetDirtyException;
    public Exception? ShowPushConflictException;
    public Exception? ShowErrorException;
    public Task? ShowErrorGate;
    public TaskCompletionSource<DirtyChoice>? DirtyGate;
    public Queue<TaskCompletionSource<DirtyChoice>>? DirtyGates;
    public TaskCompletionSource<string?>? AssetDirectoryPickGate;
    public TaskCompletionSource<string?>? SavePickGate;

    public int NewMapShown;
    public int ResizeMapShown;
    public int DirtyShown;
    public int ExternalChangeShown;
    public int OpenPickShown;
    public int SavePickShown;
    public List<ErrorPresentation> Errors = new();

    public Task<NewMapRequest?> ShowNewMapAsync()
    {
        NewMapShown++;
        if (ShowNewMapException is { } exception)
        {
            return Task.FromException<NewMapRequest?>(exception);
        }

        return Task.FromResult(NewMapResult);
    }

    public Task<MapTileRectangle?> ShowResizeMapAsync(MapDocument document)
    {
        ResizeMapShown++;
        return Task.FromResult(ResizeMapResult);
    }

    public Task<DirtyChoice> ShowDirtyAsync(string displayName)
    {
        DirtyShown++;
        if (ShowDirtyException is { } exception)
        {
            return Task.FromException<DirtyChoice>(exception);
        }

        if (DirtyGates is { Count: > 0 } gates)
        {
            return gates.Dequeue().Task;
        }

        if (DirtyGate is { } gate)
        {
            return gate.Task;
        }

        return Task.FromResult(DirtyResult);
    }

    public Task<ExternalChangeChoice> ShowExternalChangeAsync(string path)
    {
        ExternalChangeShown++;
        ExternalChangeChoice choice = ExternalChangeChoices is { Count: > 0 } queue
            ? queue.Dequeue()
            : ExternalChangeResult;
        return Task.FromResult(choice);
    }

    public Task<string?> PickOpenMapAsync()
    {
        OpenPickShown++;
        if (PickOpenException is { } exception)
        {
            return Task.FromException<string?>(exception);
        }

        return Task.FromResult(OpenPickResult);
    }

    public Task<string?> PickSaveMapAsync(string suggestedName)
    {
        SavePickShown++;
        LastSaveSuggestedName = suggestedName;
        if (PickSaveException is { } exception)
        {
            return Task.FromException<string?>(exception);
        }

        if (SavePickGate is { } gate)
        {
            return gate.Task;
        }

        return Task.FromResult(SavePickResult);
    }

    public Task<string?> PickAssetDirectoryAsync()
    {
        AssetDirectoryPickShown++;
        if (PickAssetDirectoryException is { } exception)
        {
            return Task.FromException<string?>(exception);
        }

        if (AssetDirectoryPickGate is { } gate)
        {
            return gate.Task;
        }

        return Task.FromResult(AssetDirectoryPickResult);
    }

    public Task ShowErrorAsync(ErrorPresentation error)
    {
        Errors.Add(error);
        if (ShowErrorException is { } exception)
        {
            return Task.FromException(exception);
        }

        return ShowErrorGate ?? Task.CompletedTask;
    }

    public Task<string?> ShowSpreadsheetUrlAsync(string? prefill)
    {
        SpreadsheetUrlShown++;
        LastSpreadsheetUrlPrefill = prefill;
        if (ShowSpreadsheetUrlException is { } exception)
        {
            return Task.FromException<string?>(exception);
        }

        return Task.FromResult(SpreadsheetUrlResult);
    }

    public Task<MapReference?> ShowMapConfirmationAsync(IReadOnlyList<MapReference> maps, MapReference? suggested, string documentName)
    {
        MapConfirmationShown++;
        LastMapConfirmationMaps = maps;
        LastMapConfirmationSuggested = suggested;
        LastMapConfirmationDocumentName = documentName;
        if (ShowMapConfirmationException is { } exception)
        {
            return Task.FromException<MapReference?>(exception);
        }

        return Task.FromResult(MapConfirmationResult);
    }

    public Task<SheetDirtyChoice> ShowSheetDirtyAsync(string documentName)
    {
        SheetDirtyShown++;
        LastSheetDirtyDocumentName = documentName;
        if (ShowSheetDirtyException is { } exception)
        {
            return Task.FromException<SheetDirtyChoice>(exception);
        }

        SheetDirtyChoice choice = SheetDirtyChoices is { Count: > 0 } queue
            ? queue.Dequeue()
            : SheetDirtyResult;
        return Task.FromResult(choice);
    }

    public Task<PushConflictChoice> ShowPushConflictAsync(string documentName)
    {
        PushConflictShown++;
        LastPushConflictDocumentName = documentName;
        if (ShowPushConflictException is { } exception)
        {
            return Task.FromException<PushConflictChoice>(exception);
        }

        PushConflictChoice choice = PushConflictChoices is { Count: > 0 } queue
            ? queue.Dequeue()
            : PushConflictResult;
        return Task.FromResult(choice);
    }
}
