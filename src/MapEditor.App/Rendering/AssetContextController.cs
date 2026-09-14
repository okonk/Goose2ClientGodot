using System;
using System.IO;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using MapEditor.App.Settings;
using MapEditor.App.Terrain;
using MapEditor.App.ViewModels;
using MapEditor.Core;
using MapEditor.Rendering;

namespace MapEditor.App.Rendering;

internal sealed class AssetContextController : IDisposable, ITerrainCatalogPublisher
{
    private static readonly Delegate[] EmptyHandlers = Array.Empty<Delegate>();

    private readonly WorkspaceViewModel _workspace;
    private readonly INotifyCollectionChanged _documents;
    private readonly AppSettingsStore _settings;
    private readonly Func<string, AssetContext> _openContext;
    private readonly int _creatingThreadId;
    private readonly TerrainOperationGate _gate;
    private readonly List<GestureCancellation> _gestureCancellations = new();
    private AssetContext _current;
    private TerrainPublication? _publication;
    private bool _disposed;

    public AssetContextController(WorkspaceViewModel workspace, AppSettingsStore settings)
        : this(workspace, settings, path => AssetContext.Create(path, new AvaloniaSpriteSheetLoader()))
    {
    }

    internal AssetContextController(WorkspaceViewModel workspace, AppSettingsStore settings, Func<string, AssetContext> openContext)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _openContext = openContext ?? throw new ArgumentNullException(nameof(openContext));
        _creatingThreadId = Environment.CurrentManagedThreadId;
        _gate = new TerrainOperationGate();
        _current = AssetContext.CreateUnavailable();
        _documents = workspace.Documents;
        _documents.CollectionChanged += OnDocumentsChanged;
    }

    public AssetContext Current => _current;

    public TerrainOperationGate Gate => _gate;

    public event EventHandler? CurrentChanged;

    public event EventHandler<TerrainCatalogChangedEventArgs>? TerrainCatalogChanged;

    public PublicationNotificationErrors? LastPublicationNotificationErrors { get; private set; }

    public IDisposable RegisterTerrainGestureCancellation(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        VerifyCreatingThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        var registration = new GestureCancellation(this, callback);
        _gestureCancellations.Add(registration);
        return registration;
    }

    public ITerrainCatalogSavePublication PrepareSave(
        TerrainOperationLease operation,
        AssetContext expectedContext,
        TerrainCatalogPreparedSave preparedSave)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(expectedContext);
        ArgumentNullException.ThrowIfNull(preparedSave);
        VerifyPublicationPreconditions(operation, expectedContext);
        SpriteManifest? manifest = _current.Cache.Manifest;
        if (manifest is null)
        {
            throw new InvalidOperationException("Sprite assets are unavailable; the prepared terrain catalog cannot be validated.");
        }

        TerrainCatalogValidationResult validation = TerrainAssetCatalog.Validate(preparedSave.Catalog, manifest);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException($"The prepared terrain catalog is invalid: {FirstValidationMessage(validation)}.");
        }

        var publication = new TerrainPublication
        {
            PreparedCatalog = preparedSave.Catalog,
            PreparedIndex = preparedSave.Index
        };
        _publication = publication;
        try
        {
            TerrainDocumentReconciliation[] plans = PrepareDocumentPlans(publication);
            Delegate[] handlers = TerrainCatalogChanged?.GetInvocationList() ?? EmptyHandlers;
            var arguments = new TerrainCatalogChangedEventArgs(publication);
            var errors = new PublicationNotificationErrors(plans.Length * 3 + handlers.Length);
            InvokeGestureCancellations();
            return new SavePublication(this, preparedSave, validation.Issues, publication, plans, handlers, arguments, errors);
        }
        catch
        {
            _publication = null;
            throw;
        }
    }

    public ITerrainCatalogLoadedPublication PrepareLoaded(
        TerrainOperationLease operation,
        AssetContext expectedContext,
        TerrainCatalogLoadResult loadedReplacement,
        TerrainLoadedPublicationKind kind)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(expectedContext);
        ArgumentNullException.ThrowIfNull(loadedReplacement);
        switch (kind)
        {
            case TerrainLoadedPublicationKind.ValidReload:
                if (!loadedReplacement.IsValid)
                {
                    throw new InvalidOperationException("A valid reload publication requires a valid terrain load result.");
                }
                break;
            case TerrainLoadedPublicationKind.ConfirmedMalformedReload:
                if (loadedReplacement.IsValid || !loadedReplacement.Revision.Exists)
                {
                    throw new InvalidOperationException("A confirmed malformed reload publication requires an invalid revision-bearing terrain load result.");
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }

        VerifyPublicationPreconditions(operation, expectedContext);

        var publication = new TerrainPublication { Result = loadedReplacement };
        _publication = publication;
        try
        {
            TerrainDocumentReconciliation[] plans = PrepareDocumentPlans(publication);
            Delegate[] handlers = TerrainCatalogChanged?.GetInvocationList() ?? EmptyHandlers;
            var arguments = new TerrainCatalogChangedEventArgs(publication);
            var errors = new PublicationNotificationErrors(plans.Length * 3 + handlers.Length);
            InvokeGestureCancellations();
            return new LoadedPublication(this, publication, plans, handlers, arguments, errors);
        }
        catch
        {
            _publication = null;
            throw;
        }
    }

    public bool TryPrepareOpen(string path, out PreparedAssetContext? prepared, out Exception? failure)
    {
        VerifyCreatingThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        prepared = null;
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

        prepared = new PreparedAssetContext(candidate, fullPath);
        return true;
    }

    public void CommitPreparedOpen(TerrainOperationLease operation, PreparedAssetContext prepared, IPreparedAssetReconciliation? participant = null)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(prepared);
        VerifyCreatingThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (prepared.IsDisposed)
        {
            throw new ObjectDisposedException(nameof(PreparedAssetContext));
        }

        _gate.VerifyCurrent(operation);
        if (_publication is not null)
        {
            throw new InvalidOperationException("A terrain publication is in progress; the asset root cannot be swapped.");
        }

        List<TerrainDocumentReconciliation> plans = new();
        foreach (MapDocumentViewModel document in _workspace.Documents)
        {
            plans.Add(((ITerrainDocumentReconciler)document).PrepareRootPublication(prepared.Context));
        }

        Delegate[] currentChangedHandlers = CurrentChanged?.GetInvocationList() ?? EmptyHandlers;
        var errors = new PublicationNotificationErrors(plans.Count * 3 + (participant is null ? 0 : 1) + currentChangedHandlers.Length + 1);
        InvokeGestureCancellations();

        AssetContext replaced = _current;
        _current = prepared.Context;
        prepared.MarkConsumed();
        try
        {
            _settings.Update(current => current with { AssetDirectory = prepared.FullPath });
        }
        catch (AppSettingsException)
        {
        }

        foreach (TerrainDocumentReconciliation plan in plans)
        {
            plan.Apply();
        }

        participant?.Apply();

        foreach (TerrainDocumentReconciliation plan in plans)
        {
            plan.Notify(errors);
        }

        if (participant is not null)
        {
            NotifyGuarded(errors, participant.Notify);
        }

        foreach (Delegate handler in currentChangedHandlers)
        {
            try
            {
                ((EventHandler)handler)(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                errors.TryAdd(ex);
            }
        }

        try
        {
            replaced.Dispose();
        }
        catch (Exception ex)
        {
            errors.TryAdd(ex);
        }

        LastPublicationNotificationErrors = errors;
    }

    internal bool TryOpen(string path) => TryOpen(path, out _);

    internal bool TryOpen(string path, out Exception? failure)
    {
        if (!TryPrepareOpen(path, out PreparedAssetContext? prepared, out failure))
        {
            return false;
        }

        using (prepared)
        {
            if (!_gate.TryAcquire(out TerrainOperationLease? operation))
            {
                failure = new InvalidOperationException("The terrain operation gate is busy.");
                return false;
            }

            using (operation)
            {
                CommitPreparedOpen(operation!, prepared!);
            }
        }

        return true;
    }

    public void Dispose()
    {
        _disposed = true;
        _documents.CollectionChanged -= OnDocumentsChanged;
        _gate.Dispose();
        _current.Dispose();
    }

    private void VerifyPublicationPreconditions(TerrainOperationLease operation, AssetContext expectedContext)
    {
        VerifyCreatingThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        _gate.VerifyCurrent(operation);
        if (_publication is not null)
        {
            throw new InvalidOperationException("A terrain publication is already in progress.");
        }

        if (!ReferenceEquals(expectedContext, _current))
        {
            throw new InvalidOperationException("The expected asset context is no longer current.");
        }
    }

    private TerrainDocumentReconciliation[] PrepareDocumentPlans(TerrainPublication publication)
    {
        var plans = new List<TerrainDocumentReconciliation>();
        foreach (MapDocumentViewModel document in _workspace.Documents)
        {
            plans.Add(((ITerrainDocumentReconciler)document).PrepareTerrainPublication(publication));
        }

        return plans.ToArray();
    }

    private void InvokeGestureCancellations()
    {
        GestureCancellation[] snapshot = _gestureCancellations.ToArray();
        foreach (GestureCancellation cancellation in snapshot)
        {
            cancellation.Invoke();
        }
    }

    private void CommitTerrain(
        TerrainCatalogLoadResult terrain,
        TerrainPublication publication,
        TerrainDocumentReconciliation[] plans,
        Delegate[] catalogChangedHandlers,
        TerrainCatalogChangedEventArgs arguments,
        PublicationNotificationErrors errors)
    {
        publication.Result = terrain;
        _current.ReplaceTerrain(terrain);
        foreach (TerrainDocumentReconciliation plan in plans)
        {
            plan.Apply();
        }

        foreach (TerrainDocumentReconciliation plan in plans)
        {
            plan.Notify(errors);
        }

        foreach (Delegate handler in catalogChangedHandlers)
        {
            try
            {
                ((EventHandler<TerrainCatalogChangedEventArgs>)handler)(this, arguments);
            }
            catch (Exception ex)
            {
                errors.TryAdd(ex);
            }
        }

        LastPublicationNotificationErrors = errors;
        _publication = null;
    }

    private void ReleasePublication(TerrainPublication publication)
    {
        if (ReferenceEquals(_publication, publication))
        {
            _publication = null;
        }
    }

    private void VerifyCreatingThread()
    {
        if (Environment.CurrentManagedThreadId != _creatingThreadId)
        {
            throw new InvalidOperationException("This operation must run on the thread that created the asset context controller.");
        }
    }

    private static void NotifyGuarded(PublicationNotificationErrors errors, Action notification)
    {
        try
        {
            notification();
        }
        catch (Exception ex)
        {
            errors.TryAdd(ex);
        }
    }

    private static string FirstValidationMessage(TerrainCatalogValidationResult validation)
        => validation.Issues.First(issue => issue.Severity == TerrainValidationSeverity.Error).Message;

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
            document.SetTerrain(_current.Terrain);
        }
    }

    private sealed class SavePublication : ITerrainCatalogSavePublication
    {
        private readonly AssetContextController _owner;
        private readonly string _operationId;
        private readonly TerrainCatalog _catalog;
        private readonly TerrainCatalogIndex? _index;
        private readonly string _sourcePath;
        private readonly IReadOnlyList<TerrainValidationIssue> _issues;
        private readonly TerrainPublication _publication;
        private readonly TerrainDocumentReconciliation[] _plans;
        private readonly Delegate[] _catalogChangedHandlers;
        private readonly TerrainCatalogChangedEventArgs _arguments;
        private readonly PublicationNotificationErrors _errors;
        private bool _finished;

        internal SavePublication(
            AssetContextController owner,
            TerrainCatalogPreparedSave preparedSave,
            IReadOnlyList<TerrainValidationIssue> issues,
            TerrainPublication publication,
            TerrainDocumentReconciliation[] plans,
            Delegate[] catalogChangedHandlers,
            TerrainCatalogChangedEventArgs arguments,
            PublicationNotificationErrors errors)
        {
            _owner = owner;
            _operationId = preparedSave.OperationId;
            _catalog = preparedSave.Catalog;
            _index = preparedSave.Index;
            _sourcePath = preparedSave.SourcePath;
            _issues = issues;
            _publication = publication;
            _plans = plans;
            _catalogChangedHandlers = catalogChangedHandlers;
            _arguments = arguments;
            _errors = errors;
        }

        public void Commit(TerrainCatalogSaveResult durableReplacement)
        {
            _owner.VerifyCreatingThread();
            if (_finished)
            {
                throw new ObjectDisposedException(nameof(SavePublication));
            }

            if (durableReplacement.OperationId != _operationId)
            {
                throw new InvalidOperationException("The save result does not belong to this prepared save.");
            }

            var terrain = TerrainCatalogLoadResult.Valid(_sourcePath, durableReplacement.Revision, _catalog, _index, _issues);
            _owner.CommitTerrain(terrain, _publication, _plans, _catalogChangedHandlers, _arguments, _errors);
            _finished = true;
        }

        public void Dispose()
        {
            if (_finished)
            {
                return;
            }

            _finished = true;
            _owner.ReleasePublication(_publication);
        }
    }

    private sealed class LoadedPublication : ITerrainCatalogLoadedPublication
    {
        private readonly AssetContextController _owner;
        private readonly TerrainPublication _publication;
        private readonly TerrainDocumentReconciliation[] _plans;
        private readonly Delegate[] _catalogChangedHandlers;
        private readonly TerrainCatalogChangedEventArgs _arguments;
        private readonly PublicationNotificationErrors _errors;
        private bool _finished;

        internal LoadedPublication(
            AssetContextController owner,
            TerrainPublication publication,
            TerrainDocumentReconciliation[] plans,
            Delegate[] catalogChangedHandlers,
            TerrainCatalogChangedEventArgs arguments,
            PublicationNotificationErrors errors)
        {
            _owner = owner;
            _publication = publication;
            _plans = plans;
            _catalogChangedHandlers = catalogChangedHandlers;
            _arguments = arguments;
            _errors = errors;
        }

        public void Commit()
        {
            _owner.VerifyCreatingThread();
            if (_finished)
            {
                throw new ObjectDisposedException(nameof(LoadedPublication));
            }

            _owner.CommitTerrain(_publication.Result!, _publication, _plans, _catalogChangedHandlers, _arguments, _errors);
            _finished = true;
        }

        public void Dispose()
        {
            if (_finished)
            {
                return;
            }

            _finished = true;
            _owner.ReleasePublication(_publication);
        }
    }

    private sealed class GestureCancellation : IDisposable
    {
        private readonly AssetContextController _owner;
        private readonly Action _callback;
        private bool _disposed;

        internal GestureCancellation(AssetContextController owner, Action callback)
        {
            _owner = owner;
            _callback = callback;
        }

        internal void Invoke() => _callback();

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _owner.VerifyCreatingThread();
            _owner._gestureCancellations.Remove(this);
        }
    }
}
