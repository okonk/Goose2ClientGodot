using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using MapEditor.App.Dialogs;
using MapEditor.App.Documents;
using MapEditor.App.Rendering;
using MapEditor.App.Settings;
using MapEditor.App.Tests.Fakes;
using MapEditor.App.Tests.Fixtures;
using MapEditor.App.ViewModels;
using MapEditor.GameData.Rows;
using MapEditor.Core;
using MapEditor.Core.Terrain;
using MapEditor.Rendering;
using MapEditor.Rendering.Terrain;
using Xunit;

namespace MapEditor.App.Tests;

public class AssetContextControllerTests : IDisposable
{
    private const string TwoSheetJson = """
        { "tileSize": 32, "sheets": {
          "1": { "10": [0, 0, 32, 32], "11": [32, 0, 32, 32] },
          "2": { "20": [0, 0, 32, 32] }
        } }
        """;

    private const string AppearanceSidecarJson = """
        { "version": 1, "parts": {
          "Body": { "1": { "noEquip": [1, 10], "equip": [2, 20] } },
          "Hair": {}, "Eyes": {}, "Chest": {}, "Helm": {}, "Legs": {}, "Feet": {}, "Hand": {}
        } }
        """;

    private readonly string _directory = Directory.CreateTempSubdirectory("map-editor-assets-ctx-").FullName;
    private readonly FakeEditorDialogs _dialogs = new();
    private readonly WorkspaceViewModel _workspace;
    private readonly MapDocumentViewModel _viewModel;
    private readonly string _settingsPath;

    public AssetContextControllerTests()
    {
        _workspace = new WorkspaceViewModel(_dialogs, new MapFileStore());
        _viewModel = _workspace.ActiveDocument;
        _settingsPath = Path.Combine(_directory, "settings.json");
    }

    public void Dispose()
        => Directory.Delete(_directory, recursive: true);

    private AssetContextController CreateController()
        => new(_workspace, new AppSettingsStore(_settingsPath));

    private Task AddDocumentAsync()
    {
        _dialogs.NewMapResult = new NewMapRequest(10, 10);
        return _workspace.NewAsync();
    }

    private string WriteAssetDirectory(string name, string manifestJson)
    {
        string assetDirectory = Path.Combine(_directory, name);
        Directory.CreateDirectory(Path.Combine(assetDirectory, "sheets"));
        File.WriteAllText(Path.Combine(assetDirectory, "manifest.json"), manifestJson);
        return assetDirectory;
    }

    private void WriteSettings(string? assetDirectory)
        => new AppSettingsStore(_settingsPath).Save(new AppSettings(assetDirectory));

    private static TerrainCatalog CreateTerrainCatalog(
        TerrainReviewStatus status = TerrainReviewStatus.Enabled,
        params TerrainGraphicReference[] references)
    {
        references = references.Length == 0 ? new[] { new TerrainGraphicReference(1, 10) } : references;
        string id = TerrainGeneratedId.Create(TerrainTopology.FourWay, references);
        TerrainSetDefinition set = new(
            id,
            "Grass",
            status,
            TerrainTopology.FourWay,
            new TerrainSetMetrics(1, 1, 1, 1, 0, 1, 0, 1, 1, 0, 1),
            TerrainMasks.Required(TerrainTopology.FourWay)
                .Select(mask => new TerrainMaskDefinition(mask, mask == 0 ? references : new[] { references[0] })),
            references.Select(reference => new TerrainMemberDefinition(reference, TerrainMemberProvenance.MapObserved)),
            Array.Empty<TerrainDiagnostic>());
        return new TerrainCatalog(
            TerrainCatalogJson.CurrentSchemaVersion,
            "test",
            "sha256:abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789",
            new TerrainGenerationSettings(2, 1, 1, 1, 1, 0.1, 0.5, 0.5, 0.1, 0.5, 0.9, 0.9),
            new[] { set },
            Array.Empty<TerrainDiagnostic>());
    }

    private static void AddThrowingTintFrame(AssetContext context, Exception failure)
    {
        IBitmapImpl implementation = DispatchProxy.Create<IBitmapImpl, ThrowingBitmapProxy>();
        ((ThrowingBitmapProxy)(object)implementation).Failure = failure;
        var bitmap = (Bitmap)Activator.CreateInstance(
            typeof(Bitmap),
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            new object[] { implementation },
            null)!;
        FieldInfo framesField = typeof(AvaloniaTintedSpriteCache).GetField("_frames", BindingFlags.Instance | BindingFlags.NonPublic)!;
        object frames = framesField.GetValue(context.TintCache)!;
        Type keyType = typeof(AvaloniaTintedSpriteCache).GetNestedType("TintKey", BindingFlags.NonPublic)!;
        object key = Activator.CreateInstance(
            keyType,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            null,
            new object[] { new object(), new SpriteSourceRect(0, 0, 1, 1), new RgbaValue(1, 2, 3, 4) },
            null)!;
        frames.GetType().GetMethod("Add")!.Invoke(
            frames,
            new object[] { key, new AvaloniaTintedSpriteCache.TintedFrame(bitmap, new SpriteSourceRect(0, 0, 1, 1)) });
    }

    [Fact]
    public void Constructor_SeedsExistingInitialDocumentWithNoAssetsTerrainDiagnostic()
    {
        using AssetContextController controller = CreateController();

        Assert.False(controller.Current.Terrain.Availability.IsToolAvailable);
        Assert.False(_viewModel.IsTerrainAvailable);
        Assert.Null(_viewModel.SelectedTerrainId);
        Assert.Contains("Sprite assets are unavailable", _viewModel.TerrainDiagnostic);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TryOpen_MissingOrMalformedTerrain_PublishesSpritesAndActionableTerrainState(bool malformed)
    {
        using AssetContextController controller = CreateController();
        string assetDirectory = WriteAssetDirectory("assets-unavailable-terrain-" + malformed, TwoSheetJson);
        if (malformed)
        {
            File.WriteAllText(Path.Combine(assetDirectory, "terrain-brushes.json"), "not json");
        }

        Assert.True(controller.TryOpen(assetDirectory));

        Assert.True(controller.Current.IsAvailable);
        Assert.Null(controller.Current.Terrain.Source);
        Assert.False(controller.Current.Terrain.Availability.IsToolAvailable);
        Assert.False(_viewModel.IsTerrainAvailable);
        Assert.Contains("terrain-brushes.json", _viewModel.TerrainDiagnostic);
        if (malformed)
        {
            Assert.Contains("MalformedJson", _viewModel.TerrainDiagnostic);
        }
    }

    [Fact]
    public void TryOpen_InvalidEnabledFrames_RetainsSourceForManagerButNoRuntime()
    {
        using AssetFixture fixture = new();
        fixture.WriteManifest("""{ "tileSize": 32, "sheets": { "1": { "10": [0,0,16,16] } } }""");
        TerrainCatalog catalog = CreateTerrainCatalog(
            TerrainReviewStatus.Enabled,
            new TerrainGraphicReference(1, 10),
            new TerrainGraphicReference(1, 11));
        fixture.WriteTerrainCatalog(catalog);
        using AssetContextController controller = CreateController();

        Assert.True(controller.TryOpen(fixture.AssetDirectory));

        Assert.NotNull(controller.Current.Terrain.Source);
        Assert.Equal(TerrainCatalogJson.Serialize(catalog), TerrainCatalogJson.Serialize(controller.Current.Terrain.Source!));
        Assert.Null(controller.Current.Terrain.Runtime);
        Assert.False(controller.Current.Terrain.Availability.IsToolAvailable);
        Assert.False(_viewModel.IsTerrainAvailable);
        Assert.True(controller.Current.IsAvailable);
        Assert.Equal(new[] { 1 }, controller.Current.SheetIds);
    }

    [Fact]
    public void TryOpen_ValidEmptyTerrain_PublishesSourceButNoUsableTool()
    {
        using AssetFixture fixture = new();
        fixture.WriteManifest(TwoSheetJson);
        TerrainCatalog catalog = CreateTerrainCatalog(TerrainReviewStatus.Pending);
        fixture.WriteTerrainCatalog(catalog);
        using AssetContextController controller = CreateController();

        Assert.True(controller.TryOpen(fixture.AssetDirectory));

        Assert.NotNull(controller.Current.Terrain.Source);
        Assert.Equal(TerrainCatalogJson.Serialize(catalog), TerrainCatalogJson.Serialize(controller.Current.Terrain.Source!));
        Assert.NotNull(controller.Current.Terrain.Runtime);
        Assert.Empty(controller.Current.Terrain.Runtime!.EnabledSets);
        Assert.True(controller.Current.Terrain.Availability.IsCatalogValid);
        Assert.False(controller.Current.Terrain.Availability.IsToolAvailable);
        Assert.False(_viewModel.IsTerrainAvailable);
        Assert.Equal(
            "Terrain catalog has no enabled terrain sets. Open Edit > Terrain Sets… to enable a complete set.",
            _viewModel.TerrainDiagnostic);
    }

    [Fact]
    public async Task TryOpen_ValidTerrain_SeedsEveryExistingAndLateDocument()
    {
        await AddDocumentAsync();
        using AssetFixture fixture = new();
        fixture.WriteManifest(TwoSheetJson);
        TerrainCatalog catalog = CreateTerrainCatalog();
        fixture.WriteTerrainCatalog(catalog);
        using AssetContextController controller = CreateController();
        string expectedId = catalog.Sets.Single().Id;

        Assert.True(controller.TryOpen(fixture.AssetDirectory));

        foreach (MapDocumentViewModel document in _workspace.Documents)
        {
            Assert.Equal(expectedId, document.SelectedTerrainId);
            Assert.True(document.IsTerrainAvailable);
            Assert.Null(document.TerrainDiagnostic);
        }

        await AddDocumentAsync();

        Assert.Equal(expectedId, _workspace.ActiveDocument.SelectedTerrainId);
        Assert.True(_workspace.ActiveDocument.IsTerrainAvailable);
        Assert.Null(_workspace.ActiveDocument.TerrainDiagnostic);
    }

    [Fact]
    public void PrepareTerrainReplacement_InvokesThreeRegistrationsInOrderAndContinuesAfterMiddleFailure()
    {
        using AssetContextController controller = CreateController();
        var calls = new List<int>();
        using IDisposable first = controller.RegisterTerrainGestureCancellation(() => calls.Add(1));
        using IDisposable second = controller.RegisterTerrainGestureCancellation(() =>
        {
            calls.Add(2);
            throw new InvalidOperationException("middle");
        });
        using IDisposable third = controller.RegisterTerrainGestureCancellation(() => calls.Add(3));

        TerrainReplacementPreparation preparation = controller.PrepareTerrainReplacement(
            controller.Current,
            TerrainAssetLoadResult.Unavailable("replacement"));

        Assert.False(preparation.Succeeded);
        Assert.Null(preparation.Plan);
        Assert.Equal(new[] { 1, 2, 3 }, calls);
        Assert.Equal("middle", Assert.Single(preparation.CancellationFailures).Message);
    }

    [Fact]
    public void PrepareTerrainReplacement_DisposedRegistrationIsNotInvoked()
    {
        using AssetContextController controller = CreateController();
        var calls = 0;
        IDisposable registration = controller.RegisterTerrainGestureCancellation(() => calls++);
        registration.Dispose();
        registration.Dispose();

        TerrainReplacementPreparation preparation = controller.PrepareTerrainReplacement(
            controller.Current,
            TerrainAssetLoadResult.Unavailable("replacement"));

        Assert.True(preparation.Succeeded);
        Assert.Equal(0, calls);
        controller.AbandonTerrainReplacement(preparation.Plan!);
    }

    [Fact]
    public void CancellationRegistration_DisposeAfterControllerDisposeIsNonthrowingAndDoesNotMutateState()
    {
        AssetContextController controller = CreateController();
        AssetContext context = controller.Current;
        var calls = 0;
        IDisposable registration = controller.RegisterTerrainGestureCancellation(() => calls++);
        controller.Dispose();
        Exception? wrongThreadFailure = null;
        var thread = new Thread(() => wrongThreadFailure = Record.Exception(registration.Dispose));
        thread.Start();
        thread.Join();

        Assert.IsType<InvalidOperationException>(wrongThreadFailure);
        Assert.Null(Record.Exception(registration.Dispose));
        Assert.Null(Record.Exception(registration.Dispose));
        Assert.Same(context, controller.Current);
        Assert.True(context.IsDisposed);
        Assert.Equal(0, calls);
    }

    [AvaloniaFact]
    public void PrepareTerrainReplacement_WrongThreadFailsBeforeCallbacksOrState()
    {
        using AssetContextController controller = CreateController();
        var calls = 0;
        using IDisposable registration = controller.RegisterTerrainGestureCancellation(() => calls++);
        AssetContext context = controller.Current;
        Exception? failure = null;
        var thread = new Thread(() => failure = Record.Exception(() => controller.PrepareTerrainReplacement(
            context,
            TerrainAssetLoadResult.Unavailable("replacement"))));

        thread.Start();
        thread.Join();

        Assert.IsType<InvalidOperationException>(failure);
        Assert.Equal(0, calls);
        Assert.Same(context, controller.Current);
    }

    [Fact]
    public void PrepareTerrainReplacement_CancellationCallbackNestedTryOpenAndDisposeAreRejectedBeforeNestedMutation()
    {
        using AssetContextController controller = CreateController();
        Exception? openFailure = null;
        Exception? disposeFailure = null;
        using IDisposable registration = controller.RegisterTerrainGestureCancellation(() =>
        {
            Assert.False(controller.TryOpen("ignored", out openFailure));
            disposeFailure = Record.Exception(controller.Dispose);
        });

        TerrainReplacementPreparation preparation = controller.PrepareTerrainReplacement(
            controller.Current,
            TerrainAssetLoadResult.Unavailable("replacement"));

        Assert.True(preparation.Succeeded);
        Assert.Equal("An asset replacement is already in progress.", openFailure!.Message);
        Assert.Equal("An asset replacement is already in progress.", disposeFailure!.Message);
        controller.AbandonTerrainReplacement(preparation.Plan!);
    }

    [Fact]
    public void PrepareTerrainReplacement_CancellationFailureAbandonsReservationAndNextPreparationSucceeds()
    {
        using AssetContextController controller = CreateController();
        var calls = new List<int>();
        using IDisposable first = controller.RegisterTerrainGestureCancellation(() => calls.Add(1));
        IDisposable failing = controller.RegisterTerrainGestureCancellation(() =>
        {
            calls.Add(2);
            throw new InvalidOperationException("failure");
        });

        TerrainReplacementPreparation failed = controller.PrepareTerrainReplacement(
            controller.Current,
            TerrainAssetLoadResult.Unavailable("failed"));

        Assert.True(failed.ContextMatches);
        Assert.Null(failed.Plan);
        Assert.Equal(new[] { 1, 2 }, calls);
        Assert.Equal("failure", Assert.Single(failed.CancellationFailures).Message);
        failing.Dispose();

        TerrainReplacementPreparation next = controller.PrepareTerrainReplacement(
            controller.Current,
            TerrainAssetLoadResult.Unavailable("next"));

        Assert.True(next.Succeeded);
        controller.AbandonTerrainReplacement(next.Plan!);
    }

    [AvaloniaFact]
    public void AbandonTerrainReplacement_IsNonthrowingForNullForeignWrongThreadAndReusedPlansWithoutReleasingAnotherReservation()
    {
        using AssetContextController controller = CreateController();
        using AssetContextController foreignController = CreateController();
        TerrainReplacementPlan foreign = foreignController.PrepareTerrainReplacement(
            foreignController.Current,
            TerrainAssetLoadResult.Unavailable("foreign")).Plan!;
        TerrainReplacementPlan published = controller.PrepareTerrainReplacement(
            controller.Current,
            TerrainAssetLoadResult.Unavailable("published")).Plan!;
        controller.PublishTerrain(published);
        TerrainReplacementPlan abandoned = controller.PrepareTerrainReplacement(
            controller.Current,
            TerrainAssetLoadResult.Unavailable("abandoned")).Plan!;
        controller.AbandonTerrainReplacement(abandoned);
        TerrainReplacementPlan live = controller.PrepareTerrainReplacement(
            controller.Current,
            TerrainAssetLoadResult.Unavailable("live")).Plan!;
        var noncurrent = new TerrainReplacementPlan(controller);
        Exception? wrongThreadFailure = null;
        var thread = new Thread(() => wrongThreadFailure = Record.Exception(() => controller.AbandonTerrainReplacement(live)));

        Assert.Null(Record.Exception(() => controller.AbandonTerrainReplacement(null!)));
        Assert.Null(Record.Exception(() => controller.AbandonTerrainReplacement(foreign)));
        Assert.Null(Record.Exception(() => controller.AbandonTerrainReplacement(published)));
        Assert.Null(Record.Exception(() => controller.AbandonTerrainReplacement(abandoned)));
        Assert.Null(Record.Exception(() => controller.AbandonTerrainReplacement(noncurrent)));
        thread.Start();
        thread.Join();
        Assert.Null(wrongThreadFailure);
        AssetContext liveCandidate = live.Candidate!;
        TerrainPublicationResult publication = controller.PublishTerrain(live);
        Assert.Empty(publication.Warnings);
        Assert.Same(liveCandidate, controller.Current);
        foreignController.AbandonTerrainReplacement(foreign);
    }

    [Fact]
    public void PublishTerrain_ForeignPlanThrowsArgumentExceptionAndReusedPlanThrowsInvalidOperationBeforeMutation()
    {
        using AssetContextController controller = CreateController();
        using AssetContextController foreignController = CreateController();
        TerrainReplacementPlan foreign = foreignController.PrepareTerrainReplacement(
            foreignController.Current,
            TerrainAssetLoadResult.Unavailable("foreign")).Plan!;
        AssetContext before = controller.Current;

        ArgumentException foreignFailure = Assert.Throws<ArgumentException>(() => controller.PublishTerrain(foreign));

        Assert.Equal("plan", foreignFailure.ParamName);
        Assert.Same(before, controller.Current);
        foreignController.AbandonTerrainReplacement(foreign);
        TerrainAssetLoadResult replacement = TerrainAssetLoadResult.Unavailable("replacement");
        TerrainReplacementPlan plan = controller.PrepareTerrainReplacement(controller.Current, replacement).Plan!;
        controller.PublishTerrain(plan);
        AssetContext published = controller.Current;

        Assert.Throws<InvalidOperationException>(() => controller.PublishTerrain(plan));
        Assert.Same(published, controller.Current);
        Assert.Same(replacement, controller.Current.Terrain);
    }

    [Fact]
    public void TryOpen_PersistsAssetDirectoryWithoutDiscardingTheTheme()
    {
        var store = new AppSettingsStore(_settingsPath);
        store.Save(new AppSettings(null, AppTheme.Light));
        using AssetContextController controller = CreateController();
        string assetDirectory = WriteAssetDirectory("assets-theme", TwoSheetJson);

        Assert.True(controller.TryOpen(assetDirectory));

        Assert.Equal(new AppSettings(assetDirectory, AppTheme.Light), store.Load());
    }

    [Fact]
    public void TryOpen_WithTileSheetSidecar_FiltersPublishedSheetIds()
    {
        using AssetContextController controller = CreateController();
        string assetDirectory = WriteAssetDirectory("assets-tiles", TwoSheetJson);
        File.WriteAllText(Path.Combine(assetDirectory, "tile-sheets.json"), "[2]");

        bool opened = controller.TryOpen(assetDirectory);

        Assert.True(opened);
        Assert.Equal(new[] { 2 }, controller.Current.SheetIds);
        Assert.Equal(2, _viewModel.SelectedSheet);
        Assert.Single(controller.Current.GetFrames(2));
    }

    [Fact]
    public void TryOpen_WithInvalidTileSheetSidecar_FallsBackToAllSheets()
    {
        using AssetContextController controller = CreateController();
        string assetDirectory = WriteAssetDirectory("assets-bad-tiles", TwoSheetJson);
        File.WriteAllText(Path.Combine(assetDirectory, "tile-sheets.json"), "not json");

        bool opened = controller.TryOpen(assetDirectory);

        Assert.True(opened);
        Assert.Equal(new[] { 1, 2 }, controller.Current.SheetIds);
    }

    [Fact]
    public void InitialContext_IsUnavailableWithEmptyPalette()
    {
        using AssetContextController controller = CreateController();

        Assert.False(controller.Current.IsAvailable);
        Assert.Empty(controller.Current.SheetIds);
        Assert.Empty(controller.Current.GetFrames(1));
        Assert.True(controller.Current.IsDisposed == false);
    }

    [Fact]
    public void InitialContext_AppearanceIsUnavailableWithActionableDiagnostic()
    {
        using AssetContextController controller = CreateController();
        AssetContext context = controller.Current;

        Assert.Null(context.Appearance);
        Assert.False(context.AppearanceAvailability.IsAvailable);
        Assert.False(string.IsNullOrEmpty(context.AppearanceAvailability.Diagnostic));
    }

    [AvaloniaFact]
    public void TryOpen_MissingAppearanceSidecar_OpensDirectoryAndKeepsMapResolutionAvailable()
    {
        using AssetContextController controller = CreateController();
        string assetDirectory = WriteAssetDirectory("assets-no-appearance", TwoSheetJson);
        File.WriteAllBytes(Path.Combine(assetDirectory, "sheets", "1.png"), AssetFixture.PngSheet.Create(64, 64));

        bool opened = controller.TryOpen(assetDirectory);

        Assert.True(opened);
        AssetContext context = controller.Current;
        Assert.True(context.IsAvailable);
        Assert.Null(context.Appearance);
        Assert.False(context.AppearanceAvailability.IsAvailable);
        Assert.False(string.IsNullOrEmpty(context.AppearanceAvailability.Diagnostic));
        Assert.Equal(new[] { 1, 2 }, context.SheetIds);
        Assert.Equal(new[] { 1, 2 }, _viewModel.SheetIds);
        Assert.Equal(2, context.GetFrames(1).Count);
        Assert.Equal(SpriteResolutionStatus.Ready, context.Resolve(new SpriteReference(1, 10)).Status);
        Assert.Equal(Path.GetFullPath(assetDirectory), new AppSettingsStore(_settingsPath).Load().AssetDirectory);
    }

    [Fact]
    public void TryOpen_MalformedAppearanceSidecar_PublishesUsableContextAndUpdatesSettings()
    {
        using AssetContextController controller = CreateController();
        string oldDirectory = WriteAssetDirectory("assets-old", TwoSheetJson);
        Assert.True(controller.TryOpen(oldDirectory));
        AssetContext oldContext = controller.Current;
        WriteSettings(oldDirectory);
        string badDirectory = WriteAssetDirectory("assets-bad-appearance", TwoSheetJson);
        File.WriteAllText(Path.Combine(badDirectory, "appearance-manifest.json"), "not json");

        bool opened = controller.TryOpen(badDirectory);

        Assert.True(opened);
        Assert.NotSame(oldContext, controller.Current);
        Assert.True(oldContext.IsDisposed);
        AssetContext context = controller.Current;
        Assert.True(context.IsAvailable);
        Assert.Null(context.Appearance);
        Assert.False(context.AppearanceAvailability.IsAvailable);
        Assert.False(string.IsNullOrEmpty(context.AppearanceAvailability.Diagnostic));
        Assert.Equal(new[] { 1, 2 }, context.SheetIds);
        Assert.Equal(new[] { 1, 2 }, _viewModel.SheetIds);
        Assert.Equal(2, context.GetFrames(1).Count);
        Assert.Equal(Path.GetFullPath(badDirectory), new AppSettingsStore(_settingsPath).Load().AssetDirectory);
    }

    [AvaloniaFact]
    public void TryOpen_ValidAppearanceSidecar_ResolvesPartsThroughTheSharedMapCache()
    {
        using AssetContextController controller = CreateController();
        string assetDirectory = WriteAssetDirectory("assets-appearance", TwoSheetJson);
        File.WriteAllBytes(Path.Combine(assetDirectory, "sheets", "1.png"), AssetFixture.PngSheet.Create(64, 64));
        File.WriteAllBytes(Path.Combine(assetDirectory, "sheets", "2.png"), AssetFixture.PngSheet.Create(64, 64));
        File.WriteAllText(Path.Combine(assetDirectory, "appearance-manifest.json"), AppearanceSidecarJson);

        bool opened = controller.TryOpen(assetDirectory);

        Assert.True(opened);
        AssetContext context = controller.Current;
        Assert.True(context.AppearanceAvailability.IsAvailable);
        Assert.Null(context.AppearanceAvailability.Diagnostic);
        Assert.NotNull(context.Appearance);
        Assert.Same(context.Cache, context.Appearance!.Cache);
        Assert.True(context.Appearance.TryResolve(AppearancePartKind.Body, 1, 3, out SpriteResolution noEquip));
        Assert.Equal(SpriteResolutionStatus.Ready, noEquip.Status);
        Assert.Equal(new SpriteReference(1, 10), noEquip.Reference);
        Assert.True(context.Appearance.TryResolve(AppearancePartKind.Body, 1, 0, out SpriteResolution equip));
        Assert.Equal(new SpriteReference(2, 20), equip.Reference);
        Assert.Same(noEquip.Image, context.Resolve(new SpriteReference(1, 10)).Image);
        controller.Dispose();
    }

    [Fact]
    public void TryOpen_ValidDirectory_PublishesContextUpdatesViewModelAndPersistsSettings()
    {
        using AssetContextController controller = CreateController();
        string assetDirectory = WriteAssetDirectory("assets-good", TwoSheetJson);
        int canvasInvalidations = 0;
        int paletteInvalidations = 0;
        _viewModel.CanvasInvalidated += () => canvasInvalidations++;
        _viewModel.PaletteInvalidated += () => paletteInvalidations++;

        bool opened = controller.TryOpen(assetDirectory);

        Assert.True(opened);
        AssetContext context = controller.Current;
        Assert.True(context.IsAvailable);
        Assert.Equal(Path.GetFullPath(assetDirectory), context.Cache.AssetDirectory);
        Assert.Equal(new[] { 1, 2 }, context.SheetIds);
        Assert.Equal(2, context.GetFrames(1).Count);
        Assert.Equal(new[] { 1, 2 }, _viewModel.SheetIds);
        Assert.Equal(1, _viewModel.SelectedSheet);
        Assert.Equal(1, canvasInvalidations);
        Assert.Equal(1, paletteInvalidations);
        Assert.Equal(Path.GetFullPath(assetDirectory), new AppSettingsStore(_settingsPath).Load().AssetDirectory);
    }

    [Fact]
    public void TryOpen_MessyPath_PublishesExactFullPath()
    {
        using AssetContextController controller = CreateController();
        string assetDirectory = WriteAssetDirectory("assets-good", TwoSheetJson);
        string messy = Path.Combine(assetDirectory, ".", "sheets", "..");

        bool opened = controller.TryOpen(messy);

        Assert.True(opened);
        Assert.Equal(Path.GetFullPath(assetDirectory), controller.Current.Cache.AssetDirectory);
    }

    [Fact]
    public void TryOpen_OpenContextFakeReentryIsRejectedAndOuterContextDirectoryWins()
    {
        AssetContextController? controller = null;
        Exception? nestedFailure = null;
        Exception? disposeFailure = null;
        string? openedPath = null;
        controller = new AssetContextController(
            _workspace,
            new AppSettingsStore(_settingsPath),
            path =>
            {
                openedPath = path;
                Assert.False(controller!.TryOpen(Path.Combine(_directory, "nested"), out nestedFailure));
                disposeFailure = Record.Exception(controller.Dispose);
                return AssetContext.Create(path, new CountingSpriteSheetLoader());
            });
        string outerDirectory = WriteAssetDirectory("assets-outer", TwoSheetJson);

        Assert.True(controller.TryOpen(outerDirectory));

        Assert.Equal("An asset replacement is already in progress.", nestedFailure!.Message);
        Assert.Equal("An asset replacement is already in progress.", disposeFailure!.Message);
        Assert.Equal(Path.GetFullPath(outerDirectory), openedPath);
        Assert.Equal(Path.GetFullPath(outerDirectory), controller.Current.Cache.AssetDirectory);
        controller.Dispose();
    }

    [Fact]
    public void TryOpen_CancellationFailurePreservesContextDirectorySettingsMapAndHistory()
    {
        using AssetContextController controller = CreateController();
        string oldDirectory = WriteAssetDirectory("assets-cancel-old", TwoSheetJson);
        Assert.True(controller.TryOpen(oldDirectory));
        AssetContext oldContext = controller.Current;
        _viewModel.Brush = new MapTileLayer(1, 10);
        _viewModel.Session.BeginStroke(MapEditTool.Pencil, 2, 3);
        Assert.True(_viewModel.Session.CompleteStroke());
        byte[] mapBefore = MapCodec.Encode(_viewModel.Session.Document);
        long historyBefore = _viewModel.Session.HistoryVersion;
        AppSettings settingsBefore = new AppSettingsStore(_settingsPath).Load();
        string newDirectory = WriteAssetDirectory("assets-cancel-new", TwoSheetJson);
        var cancellations = 0;
        using IDisposable registration = controller.RegisterTerrainGestureCancellation(() =>
        {
            cancellations++;
            throw new InvalidOperationException("cancel failed");
        });

        Assert.False(controller.TryOpen(newDirectory, out Exception? failure));

        Assert.IsType<AggregateException>(failure);
        Assert.Equal(1, cancellations);
        Assert.Same(oldContext, controller.Current);
        Assert.Equal(Path.GetFullPath(oldDirectory), controller.Current.Cache.AssetDirectory);
        Assert.Equal(settingsBefore, new AppSettingsStore(_settingsPath).Load());
        Assert.Equal(mapBefore, MapCodec.Encode(_viewModel.Session.Document));
        Assert.Equal(historyBefore, _viewModel.Session.HistoryVersion);
    }

    [Fact]
    public async Task TryOpen_FirstDocumentFirstObserverNestedTryOpenAndDisposeAreRejectedWhileAllStateAndLaterObserversCommit()
    {
        await AddDocumentAsync();
        using AssetContextController controller = CreateController();
        string assetDirectory = WriteAssetDirectory("assets-observer-reentry", TwoSheetJson);
        Exception? nestedFailure = null;
        Exception? disposeFailure = null;
        var firstCall = true;
        var laterCalls = new Dictionary<MapDocumentViewModel, int>();
        MapDocumentViewModel firstDocument = _workspace.Documents[0];
        firstDocument.PropertyChanged += (_, _) =>
        {
            if (!firstCall)
            {
                return;
            }

            firstCall = false;
            Assert.False(controller.TryOpen("nested", out nestedFailure));
            disposeFailure = Record.Exception(controller.Dispose);
        };
        foreach (MapDocumentViewModel document in _workspace.Documents)
        {
            laterCalls[document] = 0;
            document.PropertyChanged += (_, _) => laterCalls[document]++;
        }

        Assert.True(controller.TryOpen(assetDirectory));

        Assert.Equal("An asset replacement is already in progress.", nestedFailure!.Message);
        Assert.Equal("An asset replacement is already in progress.", disposeFailure!.Message);
        Assert.All(_workspace.Documents, document => Assert.Equal(new[] { 1, 2 }, document.SheetIds));
        Assert.All(laterCalls.Values, calls => Assert.True(calls > 0));
    }

    [Fact]
    public void TryOpen_PostPublicationObserverSettingsOrOldDisposeFailureReturnsSuccessWithOrderedWarningsAndReleasesReservation()
    {
        File.WriteAllText(Path.Combine(_directory, "settings-blocker"), "blocker");
        var firstImage = new ThrowingSpriteSheetImage(new InvalidOperationException("old dispose failed"));
        var imageCount = 0;
        using AssetContextController controller = new(
            _workspace,
            new AppSettingsStore(Path.Combine(_directory, "settings-blocker", "settings.json")),
            path => AssetContext.Create(
                path,
                new CountingSpriteSheetLoader(_ => SpriteSheetLoadResult.Success(
                    imageCount++ == 0 ? firstImage : new CountingSpriteSheetImage(64, 64)))));
        string firstDirectory = WriteAssetDirectory("assets-warning-first", """{ "tileSize": 32, "sheets": { "1": { "10": [0,0,32,32] } } }""");
        string secondDirectory = WriteAssetDirectory("assets-warning-second", """{ "tileSize": 32, "sheets": { "2": { "20": [0,0,32,32] } } }""");
        string thirdDirectory = WriteAssetDirectory("assets-warning-third", TwoSheetJson);
        Assert.True(controller.TryOpen(firstDirectory));
        Assert.Equal(SpriteResolutionStatus.Ready, controller.Current.Resolve(new SpriteReference(1, 10)).Status);
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MapDocumentViewModel.SheetIds))
            {
                throw new InvalidOperationException("observer failed");
            }
        };

        bool opened = controller.TryOpen(secondDirectory, out Exception? failure, out IReadOnlyList<TerrainOperationWarning> warnings);

        Assert.True(opened);
        Assert.Null(failure);
        Assert.Equal(new[]
        {
            ("document:0:PropertyChanged:SheetIds", "Document 0 property 'SheetIds' observer failed after asset publication: observer failed"),
            ("settings", "Assets were published, but saving the asset directory failed: Settings file '" + Path.Combine(_directory, "settings-blocker", "settings.json") + "': could not be saved."),
            ("old-context-disposal", "Assets were published, but disposing the previous asset context failed: old dispose failed")
        }, warnings.Select(warning => (warning.Scope, warning.Message)));
        Assert.True(controller.TryOpen(thirdDirectory, out _, out _));
    }

    [Fact]
    public void TryOpen_MissingManifest_PreservesOldContextAndSettings()
    {
        using AssetContextController controller = CreateController();
        string oldDirectory = WriteAssetDirectory("assets-old", TwoSheetJson);
        Assert.True(controller.TryOpen(oldDirectory));
        AssetContext oldContext = controller.Current;
        WriteSettings(oldDirectory);
        string emptyDirectory = Path.Combine(_directory, "assets-empty");
        Directory.CreateDirectory(emptyDirectory);

        bool opened = controller.TryOpen(emptyDirectory);

        Assert.False(opened);
        Assert.Same(oldContext, controller.Current);
        Assert.False(oldContext.IsDisposed);
        Assert.Equal(new[] { 1, 2 }, _viewModel.SheetIds);
        Assert.Equal(oldDirectory, new AppSettingsStore(_settingsPath).Load().AssetDirectory);
    }

    [Fact]
    public void TryOpen_BadBaseManifest_DoesNotCancelOrChangePublishedTerrain()
    {
        using AssetFixture fixture = new();
        fixture.WriteManifest(TwoSheetJson);
        TerrainCatalog catalog = CreateTerrainCatalog();
        fixture.WriteTerrainCatalog(catalog);
        using AssetContextController controller = CreateController();
        Assert.True(controller.TryOpen(fixture.AssetDirectory));
        AssetContext before = controller.Current;
        TerrainAssetLoadResult terrainBefore = before.Terrain;
        string badDirectory = WriteAssetDirectory("assets-corrupt-base", "not json");
        var cancellations = 0;
        using IDisposable registration = controller.RegisterTerrainGestureCancellation(() => cancellations++);

        Assert.False(controller.TryOpen(badDirectory));

        Assert.Equal(0, cancellations);
        Assert.Same(before, controller.Current);
        Assert.Same(terrainBefore, controller.Current.Terrain);
        Assert.Equal(
            TerrainCatalogJson.Serialize(catalog),
            TerrainCatalogJson.Serialize(controller.Current.Terrain.Source!));
        Assert.True(controller.Current.Terrain.Availability.IsToolAvailable);
    }

    [Fact]
    public void TryOpen_MalformedManifest_PreservesOldContext()
    {
        using AssetContextController controller = CreateController();
        string oldDirectory = WriteAssetDirectory("assets-old", TwoSheetJson);
        Assert.True(controller.TryOpen(oldDirectory));
        AssetContext oldContext = controller.Current;
        string badDirectory = WriteAssetDirectory("assets-bad", "this is not json");

        bool opened = controller.TryOpen(badDirectory);

        Assert.False(opened);
        Assert.Same(oldContext, controller.Current);
        Assert.False(oldContext.IsDisposed);
    }

    [Fact]
    public void TryOpen_PathWithInvalidCharacter_PreservesOldContextAndSettings()
    {
        using AssetContextController controller = CreateController();
        string oldDirectory = WriteAssetDirectory("assets-old", TwoSheetJson);
        Assert.True(controller.TryOpen(oldDirectory));
        AssetContext oldContext = controller.Current;
        WriteSettings(oldDirectory);
        string settingsBefore = File.ReadAllText(_settingsPath);

        bool opened = controller.TryOpen("bad\0path");

        Assert.False(opened);
        Assert.Same(oldContext, controller.Current);
        Assert.False(oldContext.IsDisposed);
        Assert.Equal(settingsBefore, File.ReadAllText(_settingsPath));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TryOpen_EmptyPath_PreservesOldContext(string path)
    {
        using AssetContextController controller = CreateController();
        AssetContext oldContext = controller.Current;

        bool opened = controller.TryOpen(path);

        Assert.False(opened);
        Assert.Same(oldContext, controller.Current);
    }

    [Fact]
    public void TryOpen_SecondSuccess_SwapsAndDisposesReplacedContextAfterPublication()
    {
        using AssetContextController controller = CreateController();
        string firstDirectory = WriteAssetDirectory("assets-first", TwoSheetJson);
        string secondDirectory = WriteAssetDirectory("assets-second", TwoSheetJson);
        Assert.True(controller.TryOpen(firstDirectory));
        AssetContext first = controller.Current;
        Assert.True(controller.TryOpen(secondDirectory));
        AssetContext second = controller.Current;

        Assert.NotSame(first, second);
        Assert.Same(second, controller.Current);
        Assert.True(first.IsDisposed);
        Assert.False(second.IsDisposed);
        Assert.Equal(Path.GetFullPath(secondDirectory), new AppSettingsStore(_settingsPath).Load().AssetDirectory);
    }

    [AvaloniaFact]
    public void TryOpen_SecondSuccess_DisposesReplacedTintCacheFrames()
    {
        using AssetContextController controller = CreateController();
        string firstDirectory = WriteAssetDirectory("assets-tint-first", TwoSheetJson);
        File.WriteAllBytes(Path.Combine(firstDirectory, "sheets", "1.png"), AssetFixture.PngSheet.Create(64, 64));
        Assert.True(controller.TryOpen(firstDirectory));
        AssetContext first = controller.Current;
        AvaloniaSpriteSheetImage image = (AvaloniaSpriteSheetImage)first.Resolve(new SpriteReference(1, 10)).Image!;
        first.TintCache.TryGet(image, new SpriteSourceRect(0, 0, 32, 32), new RgbaValue(200, 100, 50, 128), out AvaloniaTintedSpriteCache.TintedFrame frame);
        string secondDirectory = WriteAssetDirectory("assets-tint-second", TwoSheetJson);

        bool opened = controller.TryOpen(secondDirectory);

        Assert.True(opened);
        Assert.True(first.IsDisposed);
        Assert.ThrowsAny<Exception>(() => frame.Bitmap.PixelSize);
    }

    [Fact]
    public void TryOpen_SettingsFailure_KeepsNewlyLoadedContextAndDisposesReplaced()
    {
        File.WriteAllText(Path.Combine(_directory, "blocker"), "not a directory");
        AssetContextController controller = new(
            _workspace,
            new AppSettingsStore(Path.Combine(_directory, "blocker", "settings.json")));
        string firstDirectory = WriteAssetDirectory("assets-first", TwoSheetJson);
        Assert.True(controller.TryOpen(firstDirectory));
        AssetContext first = controller.Current;
        string secondDirectory = WriteAssetDirectory("assets-second", TwoSheetJson);

        bool opened = controller.TryOpen(secondDirectory);

        Assert.True(opened);
        Assert.NotSame(first, controller.Current);
        Assert.True(first.IsDisposed);
        Assert.False(controller.Current.IsDisposed);
        Assert.Equal(Path.GetFullPath(secondDirectory), controller.Current.Cache.AssetDirectory);
        controller.Dispose();
    }

    [Fact]
    public void UnavailableContext_EmptyGraphicResolvesEmptyAndNonEmptyResolvesAssetsUnavailable()
    {
        using AssetContextController controller = CreateController();
        AssetContext context = controller.Current;

        SpriteResolution empty = context.Resolve(new SpriteReference(1, 0));
        SpriteResolution nonEmpty = context.Resolve(new SpriteReference(1, 5));
        SpriteResolution negative = context.Resolve(new SpriteReference(-3, -7));

        Assert.Equal(SpriteResolutionStatus.Empty, empty.Status);
        Assert.Null(empty.Image);
        Assert.Equal(SpriteResolutionStatus.AssetsUnavailable, nonEmpty.Status);
        Assert.Null(nonEmpty.Image);
        Assert.Equal(SpriteResolutionStatus.AssetsUnavailable, negative.Status);
        Assert.Null(negative.Image);
    }

    [Fact]
    public void UnavailableContext_RendererEmitsPlaceholdersForEveryNonEmptyReference()
    {
        using AssetContextController controller = CreateController();
        MapDocument document = MapDocument.Create(3, 1);
        document.SetLayer(0, 0, 0, new MapTileLayer(1, 5));
        document.SetLayer(1, 0, 1, new MapTileLayer(0, -7));
        document.SetLayer(2, 0, 2, new MapTileLayer(-3, 9));
        MapRenderRequest request = new(
            document,
            new ViewportTransform(new RenderSize(96, 32), new RenderPoint(0, 0), MapZoom.Percent100),
            MapRenderOptions.Default);

        RecordingMapDrawSink sink = new();
        controller.Current.Renderer.Render(request, sink);

        Assert.Equal(3, sink.CallCount);
        Assert.All(
            sink.Calls.Cast<PlaceholderDrawOperation>(),
            operation =>
            {
                Assert.Equal(SpriteResolutionStatus.AssetsUnavailable, operation.Reason);
                Assert.False(string.IsNullOrEmpty(operation.Diagnostic));
            });
    }

    [Fact]
    public async Task TryOpen_WithSeveralDocuments_UpdatesEveryOne()
    {
        await AddDocumentAsync();
        await AddDocumentAsync();
        using AssetContextController controller = CreateController();
        string assetDirectory = WriteAssetDirectory("assets-multi", TwoSheetJson);
        var canvasInvalidations = new Dictionary<MapDocumentViewModel, int>();
        var paletteInvalidations = new Dictionary<MapDocumentViewModel, int>();
        foreach (MapDocumentViewModel document in _workspace.Documents)
        {
            canvasInvalidations[document] = 0;
            paletteInvalidations[document] = 0;
            document.CanvasInvalidated += () => canvasInvalidations[document]++;
            document.PaletteInvalidated += () => paletteInvalidations[document]++;
        }

        bool opened = controller.TryOpen(assetDirectory);

        Assert.True(opened);
        foreach (MapDocumentViewModel document in _workspace.Documents)
        {
            Assert.Equal(new[] { 1, 2 }, document.SheetIds);
            Assert.Equal(1, document.SelectedSheet);
            Assert.Equal(1, canvasInvalidations[document]);
            Assert.Equal(1, paletteInvalidations[document]);
        }
    }

    [Fact]
    public async Task Move_DoesNotReseedTheMovedDocument()
    {
        await AddDocumentAsync();
        MapDocumentViewModel moved = _workspace.ActiveDocument;
        using AssetContextController controller = CreateController();
        string assetDirectory = WriteAssetDirectory("assets-move", TwoSheetJson);
        Assert.True(controller.TryOpen(assetDirectory));
        moved.SelectedSheet = 2;

        _workspace.Move(1, 0);

        Assert.Equal(new[] { 1, 2 }, moved.SheetIds);
        Assert.Equal(2, moved.SelectedSheet);
        Assert.Same(moved, _workspace.Documents[0]);
    }

    [Fact]
    public async Task DocumentAddedAfterOpen_IsSeededWithCurrentSheetIds()
    {
        using AssetContextController controller = CreateController();
        string assetDirectory = WriteAssetDirectory("assets-late", TwoSheetJson);
        Assert.True(controller.TryOpen(assetDirectory));

        await AddDocumentAsync();

        MapDocumentViewModel late = _workspace.ActiveDocument;
        Assert.Equal(new[] { 1, 2 }, late.SheetIds);
        Assert.Equal(1, late.SelectedSheet);
    }

    [AvaloniaFact]
    public void AssetContext_DisposeAttemptsTintAfterCacheFailureAndPreservesFirstException()
    {
        string bothDirectory = WriteAssetDirectory("assets-dispose-both", """{ "tileSize": 32, "sheets": { "1": { "10": [0,0,32,32] } } }""");
        var cacheFailure = new InvalidOperationException("cache");
        var tintFailure = new InvalidOperationException("tint");
        AssetContext both = AssetContext.Create(
            bothDirectory,
            new CountingSpriteSheetLoader(_ => SpriteSheetLoadResult.Success(new ThrowingSpriteSheetImage(cacheFailure))));
        Assert.Equal(SpriteResolutionStatus.Ready, both.Resolve(new SpriteReference(1, 10)).Status);
        AddThrowingTintFrame(both, tintFailure);

        Exception first = Assert.Throws<InvalidOperationException>(both.Dispose);

        Assert.Same(cacheFailure, first);
        Assert.Same(tintFailure, first.Data["AssetContext.AdditionalDisposeException"]);

        string tintDirectory = WriteAssetDirectory("assets-dispose-tint", TwoSheetJson);
        AssetContext tintOnly = AssetContext.Create(tintDirectory, new CountingSpriteSheetLoader());
        var onlyFailure = new InvalidOperationException("only tint");
        AddThrowingTintFrame(tintOnly, onlyFailure);

        Assert.Same(onlyFailure, Assert.Throws<InvalidOperationException>(tintOnly.Dispose));
    }

    [Fact]
    public async Task Dispose_StopsAssetNotifications()
    {
        AssetContextController controller = CreateController();
        string assetDirectory = WriteAssetDirectory("assets-dispose", TwoSheetJson);
        Assert.True(controller.TryOpen(assetDirectory));
        controller.Dispose();

        await AddDocumentAsync();

        MapDocumentViewModel late = _workspace.ActiveDocument;
        Assert.Empty(late.SheetIds);
        Assert.Equal(0, late.SelectedSheet);
    }

    private sealed class ThrowingSpriteSheetImage : ISpriteSheetImage
    {
        private readonly Exception _failure;

        public ThrowingSpriteSheetImage(Exception failure)
            => _failure = failure;

        public int PixelWidth => 64;
        public int PixelHeight => 64;

        public void Dispose()
            => throw _failure;
    }

    public class ThrowingBitmapProxy : DispatchProxy
    {
        public Exception Failure { get; set; } = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => targetMethod!.Name switch
            {
                "get_Dpi" => new Vector(96, 96),
                "get_PixelSize" => new PixelSize(1, 1),
                "get_Version" => 1,
                nameof(IDisposable.Dispose) => throw Failure,
                _ => null
            };
    }
}
