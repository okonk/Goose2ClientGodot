using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using MapEditor.App.Dialogs;
using MapEditor.App.Documents;
using MapEditor.App.Rendering;
using MapEditor.App.Settings;
using MapEditor.App.Tests.Fakes;
using MapEditor.App.Tests.Fixtures;
using MapEditor.App.ViewModels;
using MapEditor.Core;
using MapEditor.Rendering;
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
}
