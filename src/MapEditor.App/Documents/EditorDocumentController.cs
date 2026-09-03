using System;
using System.IO;
using System.Threading.Tasks;
using MapEditor.App.Dialogs;
using MapEditor.Core;

namespace MapEditor.App.Documents;

internal sealed class EditorDocumentController
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private readonly IEditorDialogs _dialogs;
    private readonly MapFileStore _store;
    private EditorDocument _current;
    private bool _closeGuardRunning;
    private bool _closeApproved;

    public EditorDocumentController(IEditorDialogs dialogs, MapFileStore store)
    {
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _current = new EditorDocument(new MapEditSession(MapDocument.Create(), initiallyDirty: false), null, null);
    }

    public EditorDocument Document => _current;

    public event Action? StateChanged;

    public async Task NewAsync()
    {
        try
        {
            NewMapRequest? request = await _dialogs.ShowNewMapAsync();
            if (request is null ||
                request.Width < MapDocument.MinDimension || request.Width > MapDocument.MaxDimension ||
                request.Height < MapDocument.MinDimension || request.Height > MapDocument.MaxDimension)
            {
                return;
            }

            if (!await ResolveDirtyAsync())
            {
                return;
            }

            var session = new MapEditSession(MapDocument.Create(request.Width, request.Height), initiallyDirty: false);
            _current = new EditorDocument(session, null, null);
            NotifyStateChanged();
        }
        catch (OutOfMemoryException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await _dialogs.ShowErrorAsync(new ErrorPresentation("New map", ex.Message));
        }
    }

    public async Task OpenAsync()
    {
        string? picked = null;
        try
        {
            picked = await _dialogs.PickOpenMapAsync();
            if (picked is null)
            {
                return;
            }

            if (!await ResolveDirtyAsync())
            {
                return;
            }

            OpenedMap opened;
            try
            {
                opened = _store.Open(picked);
            }
            catch (MapFormatException ex)
            {
                await _dialogs.ShowErrorAsync(new ErrorPresentation("Open map", $"{picked}: {DescribeFormatError(ex.Error)}."));
                return;
            }
            catch (MapValidationException ex)
            {
                await _dialogs.ShowErrorAsync(new ErrorPresentation("Open map", $"{picked}: {ex.Message}"));
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PathTooLongException)
            {
                await _dialogs.ShowErrorAsync(new ErrorPresentation("Open map", $"{picked}: {ex.Message}"));
                return;
            }

            var session = new MapEditSession(opened.Document, initiallyDirty: false);
            _current = new EditorDocument(session, Path.GetFullPath(picked), opened.Revision);
            NotifyStateChanged();
        }
        catch (OutOfMemoryException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await _dialogs.ShowErrorAsync(new ErrorPresentation("Open map", picked is { } path ? $"{path}: {ex.Message}" : ex.Message));
        }
    }

    public async Task SaveAsync()
    {
        try
        {
            if (_current.Path is not { } path)
            {
                await SaveAsAsync();
                return;
            }

            await SaveCoreAsync(path, _current.Revision);
        }
        catch (OutOfMemoryException)
        {
            throw;
        }
        catch (Exception ex)
        {
            string? path = _current.Path;
            await _dialogs.ShowErrorAsync(new ErrorPresentation("Save map", path is { } known ? $"{known}: {ex.Message}" : ex.Message));
        }
    }

    public async Task SaveAsAsync()
    {
        string? destination = null;
        try
        {
            string suggested = _current.Path is { } path ? Path.GetFileName(path) : "Untitled";
            string? picked = await _dialogs.PickSaveMapAsync(suggested);
            if (picked is null)
            {
                return;
            }

            destination = Path.GetFullPath(picked);
            // Re-selecting the current path must keep the external-change guard active.
            MapFileRevision? expected = string.Equals(destination, _current.Path, PathComparison)
                ? _current.Revision
                : null;
            await SaveCoreAsync(destination, expected);
        }
        catch (OutOfMemoryException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await _dialogs.ShowErrorAsync(new ErrorPresentation("Save map", destination is { } path ? $"{path}: {ex.Message}" : ex.Message));
        }
    }

    public bool Undo()
    {
        if (!_current.Session.Undo())
        {
            return false;
        }

        NotifyStateChanged();
        return true;
    }

    public bool Redo()
    {
        if (!_current.Session.Redo())
        {
            return false;
        }

        NotifyStateChanged();
        return true;
    }

    public Task<bool> RequestCloseAsync()
    {
        // The window cancels every Closing event and re-invokes Close() only after approval;
        // the running flag keeps re-entrant Closing events from stacking prompts.
        if (_closeGuardRunning)
        {
            return Task.FromResult(false);
        }

        _closeGuardRunning = true;
        return RequestCloseCoreAsync();
    }

    private async Task<bool> RequestCloseCoreAsync()
    {
        try
        {
            if (_closeApproved)
            {
                return true;
            }

            if (_current.Session.IsDirty)
            {
                DirtyChoice choice = await _dialogs.ShowDirtyAsync(DisplayName);
                if (choice == DirtyChoice.Cancel)
                {
                    return false;
                }

                if (choice == DirtyChoice.Save)
                {
                    await SaveAsync();
                    if (_current.Session.IsDirty)
                    {
                        return false;
                    }
                }
            }

            _closeApproved = true;
            return true;
        }
        catch (Exception ex)
        {
            await _dialogs.ShowErrorAsync(new ErrorPresentation("Close", ex.Message));
            return false;
        }
        finally
        {
            _closeGuardRunning = false;
        }
    }

    private async Task<bool> ResolveDirtyAsync()
    {
        if (!_current.Session.IsDirty)
        {
            return true;
        }

        DirtyChoice choice = await _dialogs.ShowDirtyAsync(DisplayName);
        if (choice == DirtyChoice.Cancel)
        {
            return false;
        }

        if (choice == DirtyChoice.Discard)
        {
            return true;
        }

        await SaveAsync();
        return !_current.Session.IsDirty;
    }

    private async Task SaveCoreAsync(string path, MapFileRevision? expectedRevision)
    {
        MapFileRevision revision;
        try
        {
            revision = _store.Save(path, _current.Session.Document, expectedRevision);
        }
        catch (MapExternalChangeException ex)
        {
            ExternalChangeChoice choice = await _dialogs.ShowExternalChangeAsync(ex.Path);
            switch (choice)
            {
                case ExternalChangeChoice.Overwrite:
                    // Deliberately drops the revision guard, but only after explicit confirmation.
                    await SaveCoreAsync(path, null);
                    return;
                case ExternalChangeChoice.SaveAs:
                    await SaveAsAsync();
                    return;
                default:
                    return;
            }
        }
        catch (MapValidationException ex)
        {
            await _dialogs.ShowErrorAsync(new ErrorPresentation("Save map", $"{path}: {ex.Message}"));
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PathTooLongException)
        {
            await _dialogs.ShowErrorAsync(new ErrorPresentation("Save map", $"{path}: {ex.Message}"));
            return;
        }

        _current = new EditorDocument(_current.Session, path, revision);
        _current.Session.MarkSaved();
        NotifyStateChanged();
    }

    private string DisplayName => _current.Path is { } path ? Path.GetFileName(path) : "Untitled";

    private void NotifyStateChanged()
    {
        StateChanged?.Invoke();
    }

    private static string DescribeFormatError(MapFormatError error) => error switch
    {
        MapFormatError.TruncatedHeader => "truncated header",
        MapFormatError.UnsupportedEditorVersion => "unsupported editor version",
        MapFormatError.InvalidDimensions => "invalid dimensions",
        MapFormatError.OversizedDimensions => "oversized dimensions",
        MapFormatError.LengthMismatch => "tile data length mismatch",
        _ => error.ToString()
    };
}
