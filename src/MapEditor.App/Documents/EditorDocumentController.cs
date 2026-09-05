using System;
using System.IO;
using System.Threading.Tasks;
using MapEditor.App.Dialogs;
using MapEditor.Core;

namespace MapEditor.App.Documents;

internal sealed class EditorDocumentController
{
    internal static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private readonly IEditorDialogs _dialogs;
    private readonly MapFileStore _store;
    private readonly Func<EditorDocument, string, bool> _isPathOwnedElsewhere;
    private EditorDocument _current;

    internal EditorDocumentController(
        IEditorDialogs dialogs,
        MapFileStore store,
        EditorDocument initial,
        Func<EditorDocument, string, bool> isPathOwnedElsewhere)
    {
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _current = initial ?? throw new ArgumentNullException(nameof(initial));
        _isPathOwnedElsewhere = isPathOwnedElsewhere ?? throw new ArgumentNullException(nameof(isPathOwnedElsewhere));
    }

    internal EditorDocumentController(IEditorDialogs dialogs, MapFileStore store, EditorDocument initial)
        : this(dialogs, store, initial, static (_, _) => false)
    {
    }

    internal EditorDocument Document => _current;

    internal event Action? StateChanged;

    internal async Task SaveAsync()
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

    internal async Task SaveAsAsync()
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
            if (_isPathOwnedElsewhere(_current, destination))
            {
                await _dialogs.ShowErrorAsync(new ErrorPresentation("Save map", $"{destination}: already open in another tab."));
                return;
            }

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

    internal bool Undo()
    {
        if (!_current.Session.Undo())
        {
            return false;
        }

        NotifyStateChanged();
        return true;
    }

    internal bool Redo()
    {
        if (!_current.Session.Redo())
        {
            return false;
        }

        NotifyStateChanged();
        return true;
    }

    // The caller owns re-entrancy (the window's close guard); this must not be
    // called concurrently with itself.
    internal async Task<bool> ConfirmCloseAsync()
    {
        try
        {
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

            return true;
        }
        catch (Exception ex)
        {
            await _dialogs.ShowErrorAsync(new ErrorPresentation("Close", ex.Message));
            return false;
        }
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

    internal static string DescribeFormatError(MapFormatError error) => error switch
    {
        MapFormatError.TruncatedHeader => "truncated header",
        MapFormatError.UnsupportedEditorVersion => "unsupported editor version",
        MapFormatError.InvalidDimensions => "invalid dimensions",
        MapFormatError.OversizedDimensions => "oversized dimensions",
        MapFormatError.LengthMismatch => "tile data length mismatch",
        _ => error.ToString()
    };
}
