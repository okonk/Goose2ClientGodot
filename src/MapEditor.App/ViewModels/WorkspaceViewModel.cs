using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MapEditor.App.Dialogs;
using MapEditor.App.Documents;
using MapEditor.Core;

namespace MapEditor.App.ViewModels;

internal sealed class WorkspaceViewModel : ViewModelBase
{
    private readonly IEditorDialogs _dialogs;
    private readonly MapFileStore _store;
    private readonly ObservableCollection<MapDocumentViewModel> _documents = new();
    private MapDocumentViewModel _activeDocument;

    internal WorkspaceViewModel(IEditorDialogs dialogs, MapFileStore store)
    {
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        Documents = new ReadOnlyObservableCollection<MapDocumentViewModel>(_documents);
        Clipboard = new SharedTileClipboard();
        MapDocumentViewModel initial = CreateDocument(MapDocument.Create(), null, null);
        _activeDocument = initial;
        _documents.Add(initial);
    }

    internal ReadOnlyObservableCollection<MapDocumentViewModel> Documents { get; }

    internal ObservableCollection<MapDocumentViewModel> DocumentCollection => _documents;

    internal MapDocumentViewModel ActiveDocument => _activeDocument;

    internal SharedTileClipboard Clipboard { get; }

    internal void Activate(MapDocumentViewModel document)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (_documents.IndexOf(document) < 0)
        {
            throw new ArgumentException("Document is not open in this workspace.", nameof(document));
        }

        SetField(ref _activeDocument, document, nameof(ActiveDocument));
    }

    internal async Task NewAsync()
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

            MapDocumentViewModel document = CreateDocument(MapDocument.Create(request.Width, request.Height), null, null);
            _documents.Add(document);
            Activate(document);
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

    internal async Task OpenAsync()
    {
        string? picked = null;
        try
        {
            picked = await _dialogs.PickOpenMapAsync();
            if (picked is null)
            {
                return;
            }

            string full = Path.GetFullPath(picked);
            MapDocumentViewModel? existing = _documents.FirstOrDefault(d =>
                d.Document.Path is { } owned && string.Equals(owned, full, EditorDocumentController.PathComparison));
            if (existing is not null)
            {
                Activate(existing);
                return;
            }

            OpenedMap opened;
            try
            {
                opened = _store.Open(full);
            }
            catch (MapFormatException ex)
            {
                await _dialogs.ShowErrorAsync(new ErrorPresentation("Open map", $"{picked}: {EditorDocumentController.DescribeFormatError(ex.Error)}."));
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

            MapDocumentViewModel document = CreateDocument(opened.Document, full, opened.Revision);
            _documents.Add(document);
            Activate(document);
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

    internal async Task<bool> CloseAsync(MapDocumentViewModel document)
    {
        if (_documents.IndexOf(document) < 0)
        {
            return false;
        }

        if (document.Session.IsDirty)
        {
            Activate(document);
        }

        if (!await document.ConfirmCloseAsync())
        {
            return false;
        }

        RemoveDocument(document);
        return true;
    }

    internal async Task<bool> CloseAllAsync()
    {
        List<MapDocumentViewModel> pending = new(_documents);
        foreach (MapDocumentViewModel document in pending)
        {
            if (_documents.IndexOf(document) < 0)
            {
                continue;
            }

            if (document.Session.IsDirty)
            {
                Activate(document);
            }

            if (!await document.ConfirmCloseAsync())
            {
                return false;
            }

            RemoveDocument(document);
        }

        return true;
    }

    internal void Move(int fromIndex, int toIndex)
    {
        _documents.Move(fromIndex, toIndex);
    }

    private MapDocumentViewModel CreateDocument(MapDocument map, string? path, MapFileRevision? revision)
    {
        var session = new MapEditSession(map, initiallyDirty: false);
        var editorDocument = new EditorDocument(session, path, revision);
        var controller = new EditorDocumentController(_dialogs, _store, editorDocument, IsPathOwnedElsewhere);
        return new MapDocumentViewModel(controller, Clipboard);
    }

    private bool IsPathOwnedElsewhere(EditorDocument asker, string fullPath)
        => _documents.Any(d => !ReferenceEquals(d.Document, asker) &&
                               d.Document.Path is { } owned &&
                               string.Equals(owned, fullPath, EditorDocumentController.PathComparison));

    private void RemoveDocument(MapDocumentViewModel document)
    {
        int index = _documents.IndexOf(document);
        if (ReferenceEquals(ActiveDocument, document))
        {
            if (_documents.Count == 1)
            {
                MapDocumentViewModel fresh = CreateDocument(MapDocument.Create(), null, null);
                _documents.Add(fresh);
                Activate(fresh);
            }
            else
            {
                int successorIndex = index < _documents.Count - 1 ? index + 1 : _documents.Count - 2;
                Activate(_documents[successorIndex]);
            }
        }

        // Activate the successor before removing: every collection observer reads ActiveDocument
        // while reacting, so it must already point at a member of the collection.
        _documents.Remove(document);
        document.Dispose();
    }
}
