using System;
using System.IO;
using System.Linq;
using MapEditor.App.Dialogs;
using MapEditor.App.Documents;
using MapEditor.App.Rendering;
using MapEditor.App.Settings;
using MapEditor.App.Tests.Fakes;
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

    private readonly string _directory = Directory.CreateTempSubdirectory("map-editor-assets-ctx-").FullName;
    private readonly FakeEditorDialogs _dialogs = new();
    private readonly EditorDocumentController _documentController;
    private readonly MapDocumentViewModel _viewModel;
    private readonly string _settingsPath;

    public AssetContextControllerTests()
    {
        _documentController = new EditorDocumentController(_dialogs, new MapFileStore(),
            new EditorDocument(new MapEditSession(MapDocument.Create(), initiallyDirty: false), null, null));
        _viewModel = new MapDocumentViewModel(_documentController, new SharedTileClipboard());
        _settingsPath = Path.Combine(_directory, "settings.json");
    }

    public void Dispose()
        => Directory.Delete(_directory, recursive: true);

    private AssetContextController CreateController()
        => new(_viewModel, new AppSettingsStore(_settingsPath));

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
            _viewModel,
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
}
