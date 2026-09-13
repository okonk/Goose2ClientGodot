using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MapEditor.App.Dialogs;
using MapEditor.App.Rendering;
using MapEditor.App.ViewModels;
using MapEditor.Core;
using MapEditor.Rendering;

namespace MapEditor.App.Terrain;

internal sealed class TerrainEditorController : IDisposable
{
    private static readonly TerrainCatalog EmptyCatalog = new(
        Array.Empty<TerrainDefinition>(),
        Array.Empty<TerrainGraphicDefinition>());

    private readonly AssetContext _context;
    private readonly SpriteManifest _manifest;
    private readonly string _assetDirectory;
    private readonly string _sourcePath;
    private readonly TerrainCatalogFileStore _store;
    private readonly ITerrainCatalogPublisher _publisher;
    private readonly IEditorDialogs _dialogs;
    private readonly List<Exception> _notificationFailures = new(4);

    private TerrainEditorSession _session = null!;
    private TerrainEditorViewModel _viewModel = null!;
    private TerrainFileRevision _revision;
    private bool _featuresEnabled;

    public TerrainOperationGate Gate { get; }

    public TerrainEditorSession Session => _session;

    public TerrainEditorViewModel ViewModel => _viewModel;

    public TerrainFileRevision Revision => _revision;

    public bool IsTerrainFeaturesEnabled => _featuresEnabled;

    public IReadOnlyList<Exception> NotificationFailures => _notificationFailures;

    internal event Action? StateChanged;

    internal TerrainEditorController(
        AssetContext context,
        TerrainCatalogFileStore store,
        ITerrainCatalogPublisher publisher,
        IEditorDialogs dialogs)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        Gate = new TerrainOperationGate();

        var load = context.Terrain;
        _manifest = context.Cache.Manifest ?? throw new InvalidOperationException("Sprite assets are unavailable.");
        _assetDirectory = Path.GetDirectoryName(load.SourcePath) ?? string.Empty;
        _sourcePath = Path.Combine(_assetDirectory, TerrainAssetCatalog.FileName);
        _revision = load.Revision;
        _featuresEnabled = load.IsValid;
        CreateEditor(load.Catalog ?? EmptyCatalog);
    }

    internal async Task SaveAsync()
    {
        using var operation = await Gate.AcquireAsync();
        try
        {
            await SaveWithLeaseAsync(operation);
        }
        catch (OutOfMemoryException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await _dialogs.ShowErrorAsync(new ErrorPresentation("Save terrain", ex.Message));
        }
    }

    internal async Task SaveWithLeaseAsync(TerrainOperationLease operation)
    {
        Gate.VerifyCurrent(operation);

        if (!_viewModel.CommitPending() || !_session.IsDirty)
        {
            return;
        }

        _viewModel.CancelRegionStroke();
        var preparedMarkSaved = _session.PrepareMarkSaved(_session.CurrentCatalog);
        var expectedRevision = _revision;

        while (true)
        {
            TerrainCatalogPreparedSave prepared;
            try
            {
                prepared = _store.PrepareSave(_assetDirectory, _session.CurrentCatalog, _manifest, expectedRevision);
            }
            catch (TerrainCatalogValidationException ex)
            {
                await _dialogs.ShowErrorAsync(new ErrorPresentation("Save terrain", ex.Message));
                return;
            }

            ITerrainCatalogSavePublication publication;
            try
            {
                publication = _publisher.PrepareSave(operation, _context, prepared);
            }
            catch (Exception ex)
            {
                await _dialogs.ShowErrorAsync(new ErrorPresentation("Save terrain", ex.Message));
                return;
            }

            TerrainCatalogSaveResult result;
            try
            {
                result = _store.Save(prepared);
            }
            catch (TerrainExternalChangeException ex)
            {
                publication.Dispose();
                TerrainExternalChangeChoice choice = await _dialogs.ConfirmReplaceTerrainCatalogAsync(ex.Path);
                switch (choice)
                {
                    case TerrainExternalChangeChoice.Cancel:
                        return;
                    case TerrainExternalChangeChoice.Reload:
                        await ReloadExternalAsync(operation);
                        return;
                    default:
                        // Best-effort re-read: the store re-checks the revision just before the move,
                        // but that check and the move are not atomic, so a concurrent writer in the
                        // gap is not always detected.
                        expectedRevision = _store.Open(_assetDirectory, _manifest).Revision;
                        continue;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                publication.Dispose();
                await _dialogs.ShowErrorAsync(new ErrorPresentation("Save terrain", $"{prepared.SourcePath}: {ex.Message}"));
                return;
            }

            try
            {
                publication.Commit(result);
            }
            catch
            {
                publication.Dispose();
                throw;
            }

            _session.ApplyMarkSaved(preparedMarkSaved);
            _revision = result.Revision;
            NotifySaved(preparedMarkSaved);
            return;
        }
    }

    internal async Task ReplaceWithEmptyCatalogAsync()
    {
        using var operation = await Gate.AcquireAsync();
        if (!await _dialogs.ConfirmReplaceMalformedExternalTerrainAsync(_sourcePath))
        {
            return;
        }

        ReplaceEditor(EmptyCatalog);
        _featuresEnabled = false;
        NotifyStateChanged();
    }

    public void Dispose()
    {
        _viewModel.Dispose();
        Gate.Dispose();
    }

    private async Task ReloadExternalAsync(TerrainOperationLease operation)
    {
        TerrainCatalogLoadResult loaded = _store.Open(_assetDirectory, _manifest);
        if (loaded.IsValid)
        {
            var replacement = CreateReplacement(loaded.Catalog ?? EmptyCatalog);
            var publication = _publisher.PrepareLoaded(operation, _context, loaded, TerrainLoadedPublicationKind.ValidReload);
            try
            {
                publication.Commit();
            }
            catch
            {
                publication.Dispose();
                throw;
            }

            ApplyReplacement(replacement);
            _revision = loaded.Revision;
            _featuresEnabled = true;
            NotifyStateChanged();
            return;
        }

        if (!await _dialogs.ConfirmReplaceMalformedExternalTerrainAsync(loaded.SourcePath))
        {
            return;
        }

        var recovery = CreateReplacement(EmptyCatalog);
        var recoveryPublication = _publisher.PrepareLoaded(operation, _context, loaded, TerrainLoadedPublicationKind.ConfirmedMalformedReload);
        try
        {
            recoveryPublication.Commit();
        }
        catch
        {
            recoveryPublication.Dispose();
            throw;
        }

        ApplyReplacement(recovery);
        _revision = loaded.Revision;
        _featuresEnabled = false;
        NotifyStateChanged();
    }

    private void CreateEditor(TerrainCatalog baseline)
    {
        var replacement = CreateReplacement(baseline);
        _session = replacement.Session;
        _viewModel = replacement.ViewModel;
    }

    private (TerrainEditorSession Session, TerrainEditorViewModel ViewModel) CreateReplacement(TerrainCatalog baseline)
    {
        var session = new TerrainEditorSession(baseline, _manifest);
        return (session, new TerrainEditorViewModel(session, _manifest));
    }

    private void ApplyReplacement((TerrainEditorSession Session, TerrainEditorViewModel ViewModel) replacement)
    {
        _viewModel.Dispose();
        _session = replacement.Session;
        _viewModel = replacement.ViewModel;
    }

    private void ReplaceEditor(TerrainCatalog baseline)
        => ApplyReplacement(CreateReplacement(baseline));

    private void NotifySaved(TerrainEditorPreparedMarkSaved prepared)
    {
        NotifyGuarded(() => _session.NotifyMarkSaved(prepared));
        NotifyActionSubscribers(StateChanged);
    }

    private void NotifyStateChanged()
    {
        NotifyActionSubscribers(StateChanged);
    }

    private void NotifyGuarded(Action stage)
    {
        try
        {
            stage();
        }
        catch (Exception ex)
        {
            _notificationFailures.Add(ex);
        }
    }

    private void NotifyActionSubscribers(Action? subscribers)
    {
        if (subscribers is null)
        {
            return;
        }

        foreach (Delegate handler in subscribers.GetInvocationList())
        {
            NotifyGuarded(() => ((Action)handler)());
        }
    }
}
