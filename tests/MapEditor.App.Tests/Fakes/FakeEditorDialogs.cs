using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MapEditor.App.Dialogs;

namespace MapEditor.App.Tests.Fakes;

internal sealed class FakeEditorDialogs : IEditorDialogs
{
    public NewMapRequest? NewMapResult;
    public DirtyChoice DirtyResult = DirtyChoice.Cancel;
    public ExternalChangeChoice ExternalChangeResult = ExternalChangeChoice.Cancel;
    public Queue<ExternalChangeChoice>? ExternalChangeChoices;
    public string? OpenPickResult;
    public string? SavePickResult;
    public string? AssetDirectoryPickResult { get; set; }
    public string? LastSaveSuggestedName;
    public Exception? ShowNewMapException;
    public Exception? PickOpenException;
    public Exception? PickSaveException;
    public Exception? ShowDirtyException;
    public TaskCompletionSource<DirtyChoice>? DirtyGate;

    public int NewMapShown;
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

    public Task<DirtyChoice> ShowDirtyAsync(string displayName)
    {
        DirtyShown++;
        if (ShowDirtyException is { } exception)
        {
            return Task.FromException<DirtyChoice>(exception);
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

        return Task.FromResult(SavePickResult);
    }

    public Task<string?> PickAssetDirectoryAsync()
    {
        return Task.FromResult(AssetDirectoryPickResult);
    }

    public Task ShowErrorAsync(ErrorPresentation error)
    {
        Errors.Add(error);
        return Task.CompletedTask;
    }
}
