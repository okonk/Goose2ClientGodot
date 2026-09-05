using System;
using System.IO;
using MapEditor.App.Settings;
using MapEditor.App.ViewModels;
using MapEditor.Rendering;

namespace MapEditor.App.Rendering;

internal sealed class AssetContextController : IDisposable
{
    private readonly MapDocumentViewModel _viewModel;
    private readonly AppSettingsStore _settings;
    private readonly Func<string, AssetContext> _openContext;
    private AssetContext _current;

    public AssetContextController(MapDocumentViewModel viewModel, AppSettingsStore settings)
        : this(viewModel, settings, path => AssetContext.Create(path, new AvaloniaSpriteSheetLoader()))
    {
    }

    internal AssetContextController(MapDocumentViewModel viewModel, AppSettingsStore settings, Func<string, AssetContext> openContext)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _openContext = openContext ?? throw new ArgumentNullException(nameof(openContext));
        _current = AssetContext.CreateUnavailable();
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
        _viewModel.SetSheetIds(candidate.SheetIds);
        _viewModel.SelectedSheet = candidate.SheetIds.Count > 0 ? candidate.SheetIds[0] : 0;
        _viewModel.Refresh(EditorRefresh.Canvas | EditorRefresh.Palette);
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

    public void Dispose()
        => _current.Dispose();
}
