using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using MapEditor.App.Settings;
using MapEditor.App.ViewModels;
using MapEditor.Rendering;
using MapEditor.Rendering.Terrain;

namespace MapEditor.App.Rendering;

internal sealed class AssetContextController : IDisposable
{
    private const string BusyMessage = "An asset replacement is already in progress.";

    private readonly WorkspaceViewModel _workspace;
    private readonly INotifyCollectionChanged _documents;
    private readonly AppSettingsStore _settings;
    private readonly Func<string, AssetContext> _openContext;
    private readonly int _creatingThreadId;
    private readonly List<CancellationRegistration> _cancellations = new();
    private AssetContext _current;
    private TerrainReplacementPlan? _replacement;
    private long _nextCancellationOrder;
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
        _current = AssetContext.CreateUnavailable();
        _documents = workspace.Documents;
        foreach (MapDocumentViewModel document in workspace.Documents)
        {
            SeedDocument(document, _current);
        }

        _documents.CollectionChanged += OnDocumentsChanged;
    }

    public AssetContext Current => _current;

    public bool TryOpen(string path) => TryOpen(path, out _, out _);

    internal bool TryOpen(string path, out Exception? failure)
        => TryOpen(path, out failure, out _);

    internal bool TryOpen(
        string path,
        out Exception? failure,
        out IReadOnlyList<TerrainOperationWarning> warnings)
    {
        failure = null;
        warnings = Array.Empty<TerrainOperationWarning>();
        EnsureThreadAndNotDisposed();
        if (_replacement is not null)
        {
            failure = new InvalidOperationException(BusyMessage);
            return false;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException)
        {
            failure = ex;
            return false;
        }

        TerrainReplacementPlan plan = Reserve();
        try
        {
            plan.Candidate = _openContext(fullPath);
            plan.Replaced = _current;
            plan.Documents = PlanDocuments(plan.Candidate, null);
            IReadOnlyList<Exception> cancellationFailures = CancelTerrainGestures();
            if (cancellationFailures.Count > 0)
            {
                failure = new AggregateException("One or more terrain gesture cancellations failed.", cancellationFailures);
                AbandonTerrainReplacement(plan);
                return false;
            }
        }
        catch (OutOfMemoryException)
        {
            AbandonTerrainReplacement(plan);
            throw;
        }
        catch (Exception ex)
        {
            failure = ex;
            AbandonTerrainReplacement(plan);
            return false;
        }

        var collected = new List<TerrainOperationWarning>();
        try
        {
            collected.AddRange(PublishCore(plan));
            try
            {
                _settings.Update(current => current with { AssetDirectory = fullPath });
            }
            catch (Exception ex)
            {
                collected.Add(new TerrainOperationWarning(
                    "settings",
                    $"Assets were published, but saving the asset directory failed: {ex.Message}",
                    ex));
            }

            try
            {
                plan.Replaced!.Dispose();
            }
            catch (Exception ex)
            {
                collected.Add(new TerrainOperationWarning(
                    "old-context-disposal",
                    $"Assets were published, but disposing the previous asset context failed: {ex.Message}",
                    ex));
            }
        }
        finally
        {
            CompletePublication(plan);
        }

        warnings = collected.AsReadOnly();
        return true;
    }

    internal IDisposable RegisterTerrainGestureCancellation(Action cancellation)
    {
        EnsureThreadAndNotDisposed();
        ArgumentNullException.ThrowIfNull(cancellation);
        var registration = new CancellationRegistration(this, cancellation, _nextCancellationOrder++);
        _cancellations.Add(registration);
        return registration;
    }

    internal TerrainReplacementPreparation PrepareTerrainReplacement(
        AssetContext expectedContext,
        TerrainAssetLoadResult replacement,
        IReadOnlyDictionary<string, string>? idRekeys = null)
        => PrepareTerrainReplacementCore(expectedContext, replacement, idRekeys, null);

    internal TerrainReplacementPreparation PrepareTerrainReplacement(
        AssetContext expectedContext,
        TerrainAssetLoadResult replacement,
        IReadOnlyDictionary<string, string>? idRekeys,
        TerrainManagerPublicationPlan managerPublication)
    {
        ArgumentNullException.ThrowIfNull(managerPublication);
        if (managerPublication.IsConsumed)
            throw new InvalidOperationException("The terrain manager publication plan has already been consumed.");
        return PrepareTerrainReplacementCore(expectedContext, replacement, idRekeys, managerPublication);
    }

    private TerrainReplacementPreparation PrepareTerrainReplacementCore(
        AssetContext expectedContext,
        TerrainAssetLoadResult replacement,
        IReadOnlyDictionary<string, string>? idRekeys,
        TerrainManagerPublicationPlan? managerPublication)
    {
        EnsureThreadAndNotDisposed();
        if (_replacement is not null)
            throw new InvalidOperationException(BusyMessage);

        ArgumentNullException.ThrowIfNull(expectedContext);
        ArgumentNullException.ThrowIfNull(replacement);
        Dictionary<string, string>? copiedRekeys = idRekeys is null
            ? null
            : new Dictionary<string, string>(idRekeys, StringComparer.Ordinal);
        if (!ReferenceEquals(_current, expectedContext))
            return new TerrainReplacementPreparation(false, null, Array.Empty<Exception>());

        TerrainReplacementPlan plan = Reserve();
        try
        {
            plan.Replaced = expectedContext;
            plan.Candidate = expectedContext.WithTerrain(replacement);
            plan.Documents = PlanDocuments(plan.Candidate, copiedRekeys, terrainOnly: true);
            plan.ManagerPublication = managerPublication;
            IReadOnlyList<Exception> failures = CancelTerrainGestures();
            if (failures.Count > 0)
            {
                AbandonTerrainReplacement(plan);
                return new TerrainReplacementPreparation(true, null, failures);
            }

            return new TerrainReplacementPreparation(true, plan, Array.Empty<Exception>());
        }
        catch
        {
            AbandonTerrainReplacement(plan);
            throw;
        }
    }

    internal TerrainPublicationResult PublishTerrain(TerrainReplacementPlan plan)
        => PublishTerrainCore(plan, null);

    internal TerrainPublicationResult PublishTerrain(
        TerrainReplacementPlan plan,
        TerrainManagerPublicationPlan managerPublication)
    {
        ArgumentNullException.ThrowIfNull(managerPublication);
        return PublishTerrainCore(plan, managerPublication);
    }

    private TerrainPublicationResult PublishTerrainCore(
        TerrainReplacementPlan plan,
        TerrainManagerPublicationPlan? managerPublication)
    {
        EnsureThreadAndNotDisposed();
        ValidatePlan(plan);
        if (!ReferenceEquals(plan.ManagerPublication, managerPublication) || managerPublication?.IsConsumed == true)
            throw new InvalidOperationException("The terrain manager publication plan does not match the reserved replacement.");
        IReadOnlyList<TerrainOperationWarning> warnings;
        try
        {
            warnings = PublishCore(plan, managerPublication);
        }
        finally
        {
            CompletePublication(plan);
        }

        return new TerrainPublicationResult(warnings);
    }

    internal void AbandonTerrainReplacement(TerrainReplacementPlan plan)
    {
        if (Environment.CurrentManagedThreadId != _creatingThreadId || plan is null ||
            !ReferenceEquals(plan.Owner, this) || plan.State != TerrainReplacementPlanState.Reserved ||
            !ReferenceEquals(_replacement, plan))
        {
            return;
        }

        plan.State = TerrainReplacementPlanState.Abandoned;
        if (plan.ManagerPublication is not null)
            plan.ManagerPublication.IsConsumed = true;
        _replacement = null;
        try
        {
            plan.Candidate?.Dispose();
        }
        catch
        {
        }
    }

    private TerrainReplacementPlan Reserve()
    {
        var plan = new TerrainReplacementPlan(this);
        _replacement = plan;
        return plan;
    }

    private IReadOnlyList<PlannedDocumentAssetState> PlanDocuments(
        AssetContext candidate,
        IReadOnlyDictionary<string, string>? idRekeys,
        bool terrainOnly = false)
    {
        var plans = new List<PlannedDocumentAssetState>(_workspace.Documents.Count);
        foreach (MapDocumentViewModel document in _workspace.Documents)
        {
            MapDocumentAssetState assets = terrainOnly
                ? document.PlanAssetState()
                : document.PlanAssetState(candidate.SheetIds);
            plans.Add(new PlannedDocumentAssetState(
                document,
                assets,
                document.PlanTerrainState(candidate.Terrain, idRekeys)));
        }

        return plans.AsReadOnly();
    }

    private IReadOnlyList<Exception> CancelTerrainGestures()
    {
        Action[] callbacks = _cancellations
            .Where(registration => registration.IsActive)
            .OrderBy(registration => registration.Order)
            .Select(registration => registration.Callback)
            .ToArray();
        var failures = new List<Exception>();
        foreach (Action callback in callbacks)
        {
            try
            {
                callback();
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }
        }

        return failures.AsReadOnly();
    }

    private IReadOnlyList<TerrainOperationWarning> PublishCore(
        TerrainReplacementPlan plan,
        TerrainManagerPublicationPlan? managerPublication = null)
    {
        ValidatePlan(plan);
        AssetContext candidate = plan.Candidate!;
        AssetContext replaced = plan.Replaced!;
        if (ReferenceEquals(candidate.Cache, replaced.Cache))
        {
            replaced.TransferOwnershipTo(candidate);
        }

        _current = candidate;
        foreach (PlannedDocumentAssetState documentPlan in plan.Documents)
        {
            documentPlan.Document.CommitAssetState(documentPlan.AssetState);
            documentPlan.Document.CommitTerrainState(documentPlan.TerrainState);
        }

        managerPublication?.Manager.CommitSavedCatalog(managerPublication);
        if (managerPublication is not null)
            managerPublication.IsConsumed = true;

        var warnings = new List<TerrainOperationWarning>();
        for (var i = 0; i < plan.Documents.Count; i++)
        {
            PlannedDocumentAssetState documentPlan = plan.Documents[i];
            string[] properties = documentPlan.AssetState.ChangedProperties
                .Concat(documentPlan.TerrainState.ChangedProperties)
                .ToArray();
            int documentIndex = i;
            documentPlan.Document.NotifyAssetPublication(properties, (kind, property, exception) =>
            {
                if (kind == "PropertyChanged")
                {
                    warnings.Add(new TerrainOperationWarning(
                        $"document:{documentIndex}:PropertyChanged:{property}",
                        $"Document {documentIndex} property '{property}' observer failed after asset publication: {exception.Message}",
                        exception));
                }
                else
                {
                    string observer = kind == "CanvasInvalidated" ? "canvas" : "palette";
                    warnings.Add(new TerrainOperationWarning(
                        $"document:{documentIndex}:{kind}",
                        $"Document {documentIndex} {observer} observer failed after asset publication: {exception.Message}",
                        exception));
                }
            });
        }

        if (managerPublication is not null)
        {
            managerPublication.Manager.NotifySavedCatalog(managerPublication, (property, exception) =>
                warnings.Add(new TerrainOperationWarning(
                    $"terrain-manager:PropertyChanged:{property}",
                    $"Terrain manager property '{property}' observer failed after catalog publication: {exception.Message}",
                    exception)));
        }

        return warnings.AsReadOnly();
    }

    private void ValidatePlan(TerrainReplacementPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!ReferenceEquals(plan.Owner, this))
        {
            throw new ArgumentException("The terrain replacement plan belongs to another controller.", nameof(plan));
        }

        if (plan.State != TerrainReplacementPlanState.Reserved || !ReferenceEquals(_replacement, plan))
        {
            throw new InvalidOperationException("The terrain replacement plan is no longer reserved.");
        }
    }

    private void CompletePublication(TerrainReplacementPlan plan)
    {
        if (ReferenceEquals(_replacement, plan) && plan.State == TerrainReplacementPlanState.Reserved)
        {
            plan.State = TerrainReplacementPlanState.Published;
            _replacement = null;
        }
    }

    private void OnDocumentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || e.NewItems is null)
        {
            return;
        }

        foreach (MapDocumentViewModel document in e.NewItems.OfType<MapDocumentViewModel>())
        {
            SeedDocument(document, _current);
        }
    }

    private static void SeedDocument(MapDocumentViewModel document, AssetContext context)
    {
        document.CommitAssetState(document.PlanAssetState(context.SheetIds));
        document.CommitTerrainState(document.PlanTerrainState(context.Terrain));
    }

    private void EnsureThreadAndNotDisposed()
    {
        if (Environment.CurrentManagedThreadId != _creatingThreadId)
        {
            throw new InvalidOperationException("Asset context operations must run on the creating thread.");
        }

        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (Environment.CurrentManagedThreadId != _creatingThreadId)
        {
            throw new InvalidOperationException("Asset context operations must run on the creating thread.");
        }

        if (_disposed)
        {
            return;
        }

        if (_replacement is not null)
        {
            throw new InvalidOperationException(BusyMessage);
        }

        _disposed = true;
        _documents.CollectionChanged -= OnDocumentsChanged;
        _current.Dispose();
    }

    private sealed class CancellationRegistration : IDisposable
    {
        private readonly AssetContextController _owner;

        internal CancellationRegistration(AssetContextController owner, Action callback, long order)
        {
            _owner = owner;
            Callback = callback;
            Order = order;
            IsActive = true;
        }

        internal Action Callback { get; }
        internal long Order { get; }
        internal bool IsActive { get; private set; }

        public void Dispose()
        {
            if (!IsActive)
            {
                return;
            }

            if (Environment.CurrentManagedThreadId != _owner._creatingThreadId)
            {
                throw new InvalidOperationException("Asset context operations must run on the creating thread.");
            }

            IsActive = false;
        }
    }
}
