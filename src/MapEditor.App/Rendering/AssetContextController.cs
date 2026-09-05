using System;
using System.IO;
using System.Collections.Specialized;
using MapEditor.App.Settings;
using MapEditor.App.ViewModels;
using MapEditor.Rendering;

namespace MapEditor.App.Rendering;

internal sealed class AssetContextController : IDisposable
{
    private readonly WorkspaceViewModel _workspace;
    private readonly INotifyCollectionChanged _documents;
    private readonly AppSettingsStore _settings;
    private readonly Func<string, AssetContext> _openContext;
    private AssetContext _current;

    public AssetContextController(WorkspaceViewModel workspace, AppSettingsStore settings)
        : this(workspace, settings, path => AssetContext.Create(path, new AvaloniaSpriteSheetLoader()))
    {
    }

    internal AssetContextController(WorkspaceViewModel workspace, AppSettingsStore settings, Func<string, AssetContext> openContext)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _openContext = openContext ?? throw new ArgumentNullException(nameof(openContext));
        _current = AssetContext.CreateUnavailable();
        _documents = workspace.Documents;
        _documents.CollectionChanged += OnDocumentsChanged;
    }

    public AssetContext Current => _current;

    public bool TryOpen(string path) => TryOpen(path, out _);

    internal bool TryOpen(string path, out Exception? failure)
    {
        failure = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string fullPath;
        AssetContext candidate;
        try
        {
            fullPath = Path.GetFullPath(path);
            candidate = _openContext(fullPath);
        }
        catch (SpriteManifestException ex)
        {
            failure = ex;
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }

        AssetContext replaced = _current;
        _current = candidate;
        foreach (MapDocumentViewModel document in _workspace.Documents)
        {
            document.SetSheetIds(candidate.SheetIds);
            document.SelectedSheet = candidate.SheetIds.Count > 0 ? candidate.SheetIds[0] : 0;
            document.Refresh(EditorRefresh.Canvas | EditorRefresh.Palette);
        }
        try
        {
            _settings.Update(current => current with { AssetDirectory = fullPath });
        }
        catch (AppSettingsException)
        {
        }

        replaced.Dispose();
        return true;
    }

    private void OnDocumentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || e.NewItems is null)
        {
            return;
        }

        foreach (MapDocumentViewModel document in e.NewItems.OfType<MapDocumentViewModel>())
        {
            document.SetSheetIds(_current.SheetIds);
            document.SelectedSheet = _current.SheetIds.Count > 0 ? _current.SheetIds[0] : 0;
        }
    }

    public void Dispose()
    {
        _documents.CollectionChanged -= OnDocumentsChanged;
        _current.Dispose();
    }
}
