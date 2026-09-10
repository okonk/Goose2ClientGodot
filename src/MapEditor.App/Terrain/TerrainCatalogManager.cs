using MapEditor.App.Rendering;
using MapEditor.App.ViewModels;
using MapEditor.Core.Terrain;
using MapEditor.Rendering;
using MapEditor.Rendering.Terrain;

namespace MapEditor.App.Terrain;

internal sealed class TerrainCatalogManager
{
    private const string BusyMessage = "An asset replacement is already in progress.";
    private const string SourceChangedMessage = "The asset context changed while Terrain Sets was open. Discard this draft and reopen Terrain Sets against the current assets.";

    private readonly AssetContextController _assets;
    private readonly AssetContext _sourceContext;
    private readonly SpriteManifest _manifest;
    private readonly TerrainCatalogFileStore _fileStore;
    private readonly int _creatingThreadId;
    private bool _saveInProgress;

    internal TerrainCatalogManager(
        AssetContextController assets,
        AssetContext sourceContext,
        TerrainCatalogFileStore fileStore)
    {
        _assets = assets ?? throw new ArgumentNullException(nameof(assets));
        _sourceContext = sourceContext ?? throw new ArgumentNullException(nameof(sourceContext));
        _fileStore = fileStore ?? throw new ArgumentNullException(nameof(fileStore));
        if (!ReferenceEquals(assets.Current, sourceContext))
            throw new ArgumentException("The source context must be the current asset context.", nameof(sourceContext));
        if (sourceContext.Cache.Manifest is not { } manifest)
            throw new ArgumentException("The source context must have a sprite manifest and cache.", nameof(sourceContext));
        if (sourceContext.Terrain.Source is not { } source)
            throw new ArgumentException("The source context must have a terrain catalog.", nameof(sourceContext));

        AssetDirectory = Path.GetFullPath(sourceContext.Cache.AssetDirectory!);
        _manifest = manifest;
        _creatingThreadId = Environment.CurrentManagedThreadId;
        ViewModel = new TerrainSetsManagerViewModel(new TerrainCatalogDraft(source, manifest));
    }

    internal TerrainSetsManagerViewModel ViewModel { get; }
    internal string AssetDirectory { get; }
    internal AssetContext SourceContext => _sourceContext;

    internal TerrainCatalogSaveResult Save()
    {
        if (Environment.CurrentManagedThreadId != _creatingThreadId || _saveInProgress)
            return Failure(TerrainCatalogSaveStatus.ReplacementInProgress, new InvalidOperationException(BusyMessage));

        _saveInProgress = true;
        try
        {
            if (!ViewModel.IsDirty)
                return Result(TerrainCatalogSaveStatus.NoChanges);
            if (!SourceMatches())
                return Failure(TerrainCatalogSaveStatus.SourceContextChanged, new InvalidOperationException(SourceChangedMessage));

            TerrainCatalog catalog = ViewModel.Build();
            TerrainAssetLoadResult replacement = TerrainAssetCatalog.Validate(catalog, _manifest);
            if (replacement.Issues.Count > 0)
                return new TerrainCatalogSaveResult(TerrainCatalogSaveStatus.InvalidDraft, replacement.Issues, null, Array.Empty<TerrainOperationWarning>());

            string serialized = TerrainCatalogJson.Serialize(catalog);
            IReadOnlyDictionary<string, string> rekeys = ViewModel.BuildPublishedIdRekeys();
            TerrainManagerPublicationPlan managerPublication = ViewModel.PlanAcceptSavedCatalog();
            if (!SourceMatches())
                return Failure(TerrainCatalogSaveStatus.SourceContextChanged, new InvalidOperationException(SourceChangedMessage));

            TerrainReplacementPreparation preparation;
            try
            {
                preparation = _assets.PrepareTerrainReplacement(
                    _sourceContext,
                    replacement,
                    rekeys,
                    managerPublication);
            }
            catch (InvalidOperationException ex) when (ex.Message == BusyMessage)
            {
                return Failure(TerrainCatalogSaveStatus.ReplacementInProgress, ex);
            }

            if (!preparation.ContextMatches)
                return Failure(TerrainCatalogSaveStatus.SourceContextChanged, new InvalidOperationException(SourceChangedMessage));
            if (preparation.CancellationFailures.Count > 0)
            {
                return Failure(
                    TerrainCatalogSaveStatus.GestureCancellationFailed,
                    new AggregateException("One or more terrain gesture cancellations failed.", preparation.CancellationFailures));
            }

            TerrainReplacementPlan plan = preparation.Plan!;
            try
            {
                try
                {
                    _fileStore.Write(AssetDirectory, serialized);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    return Failure(TerrainCatalogSaveStatus.WriteFailed, ex);
                }

                TerrainPublicationResult publication = _assets.PublishTerrain(plan, managerPublication);
                return new TerrainCatalogSaveResult(
                    TerrainCatalogSaveStatus.Succeeded,
                    Array.Empty<TerrainValidationIssue>(),
                    null,
                    publication.Warnings);
            }
            finally
            {
                _assets.AbandonTerrainReplacement(plan);
            }
        }
        finally
        {
            _saveInProgress = false;
        }
    }

    private bool SourceMatches()
    {
        if (!ReferenceEquals(_assets.Current, _sourceContext) ||
            !ReferenceEquals(_sourceContext.Cache.Manifest, _manifest))
            return false;
        try
        {
            return string.Equals(Path.GetFullPath(_sourceContext.Cache.AssetDirectory!), AssetDirectory, StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static TerrainCatalogSaveResult Result(TerrainCatalogSaveStatus status)
        => new(status, Array.Empty<TerrainValidationIssue>(), null, Array.Empty<TerrainOperationWarning>());

    private static TerrainCatalogSaveResult Failure(TerrainCatalogSaveStatus status, Exception failure)
        => new(status, Array.Empty<TerrainValidationIssue>(), failure, Array.Empty<TerrainOperationWarning>());
}
