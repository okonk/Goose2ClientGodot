using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using MapEditor.App.Dialogs;
using MapEditor.App.Documents;
using MapEditor.App.Rendering;
using MapEditor.App.Settings;
using MapEditor.App.Tests.Fakes;
using MapEditor.App.Tests.Fixtures;
using MapEditor.App.ViewModels;
using MapEditor.GameData.Rows;
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
        Assert.Null(context.Graphics);
        Assert.False(context.GraphicViewerAvailability.IsAvailable);
        Assert.Equal("Animation manifest not found: " + Path.GetFullPath(Path.Combine(assetDirectory, "animation-manifest.json")), context.GraphicViewerAvailability.Diagnostic);
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
    public void InitialContext_TerrainIsUnavailableWithActionableDiagnostic()
    {
        using AssetContextController controller = CreateController();
        TerrainCatalogLoadResult terrain = controller.Current.Terrain;

        Assert.False(terrain.IsValid);
        Assert.False(terrain.CanAuthor);
        Assert.False(terrain.CanPaint);
        Assert.Null(terrain.Catalog);
        Assert.Null(terrain.Index);
        Assert.Equal(TerrainFileRevision.Missing, terrain.Revision);
        Assert.False(string.IsNullOrEmpty(terrain.Diagnostic));
    }

    [Fact]
    public void TryOpen_MissingTerrainSidecar_PublishesValidEmptyTerrainWithExactSourcePath()
    {
        using AssetContextController controller = CreateController();
        string assetDirectory = WriteAssetDirectory("assets-no-terrain", TwoSheetJson);

        bool opened = controller.TryOpen(assetDirectory);

        Assert.True(opened);
        TerrainCatalogLoadResult terrain = controller.Current.Terrain;
        Assert.True(terrain.IsValid);
        Assert.True(terrain.CanAuthor);
        Assert.False(terrain.CanPaint);
        Assert.Null(terrain.Catalog);
        Assert.Null(terrain.Index);
        Assert.Empty(terrain.Issues);
        Assert.Equal(TerrainFileRevision.Missing, terrain.Revision);
        Assert.Equal(Path.GetFullPath(Path.Combine(assetDirectory, TerrainAssetCatalog.FileName)), terrain.SourcePath);
        Assert.Null(terrain.Diagnostic);
    }

    [Fact]
    public void TryOpen_ValidTerrainSidecar_ExposesCatalogIndexRevisionAndSourcePath()
    {
        using AssetContextController controller = CreateController();
        string assetDirectory = WriteAssetDirectory("assets-terrain", TwoSheetJson);
        File.WriteAllBytes(Path.Combine(assetDirectory, "sheets", "1.png"), AssetFixture.PngSheet.Create(64, 64));
        AssetFixture.WriteTerrainSidecar(assetDirectory, AssetFixture.TerrainCatalogJson);
        string expectedHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(assetDirectory, TerrainAssetCatalog.FileName)))).ToLowerInvariant();

        bool opened = controller.TryOpen(assetDirectory);

        Assert.True(opened);
        TerrainCatalogLoadResult terrain = controller.Current.Terrain;
        Assert.True(terrain.IsValid);
        Assert.True(terrain.CanAuthor);
        Assert.True(terrain.CanPaint);
        Assert.NotNull(terrain.Catalog);
        Assert.NotNull(terrain.Index);
        Assert.Single(terrain.Catalog.Terrains);
        Assert.Equal(new TerrainFileRevision(true, expectedHash), terrain.Revision);
        Assert.Equal(Path.GetFullPath(Path.Combine(assetDirectory, TerrainAssetCatalog.FileName)), terrain.SourcePath);
        Assert.Null(terrain.Diagnostic);
    }

    [AvaloniaFact]
    public void TryOpen_MalformedTerrain_KeepsSpriteContextUsable()
    {
        using AssetContextController controller = CreateController();
        string assetDirectory = WriteAssetDirectory("assets-bad-terrain", TwoSheetJson);
        File.WriteAllBytes(Path.Combine(assetDirectory, "sheets", "1.png"), AssetFixture.PngSheet.Create(64, 64));
        File.WriteAllBytes(Path.Combine(assetDirectory, "sheets", "2.png"), AssetFixture.PngSheet.Create(64, 64));
        AssetFixture.WriteAnimationSidecar(assetDirectory, AssetFixture.AnimationSidecarJson);
        AssetFixture.WriteTerrainSidecar(assetDirectory, "not json");

        bool opened = controller.TryOpen(assetDirectory);

        Assert.True(opened);
        AssetContext context = controller.Current;
        Assert.True(context.IsAvailable);
        Assert.False(context.Terrain.IsValid);
        Assert.False(context.Terrain.CanPaint);
        Assert.Contains("Malformed JSON", context.Terrain.Diagnostic);
        Assert.Equal(new[] { 1, 2 }, context.SheetIds);
        Assert.Equal(new[] { 1, 2 }, _viewModel.SheetIds);
        Assert.Equal(1, _viewModel.SelectedSheet);
        Assert.Equal(2, context.GetFrames(1).Count);
        Assert.Equal(SpriteResolutionStatus.Ready, context.Resolve(new SpriteReference(1, 10)).Status);
        Assert.Null(context.Appearance);
        Assert.False(context.AppearanceAvailability.IsAvailable);
        Assert.NotNull(context.Graphics);
        Assert.True(context.GraphicViewerAvailability.IsAvailable);
    }

    [AvaloniaFact]
    public void TryOpen_UnsupportedVersionTerrain_KeepsSpriteContextUsableWithTypedDiagnostic()
    {
        using AssetContextController controller = CreateController();
        string assetDirectory = WriteAssetDirectory("assets-terrain-v2", TwoSheetJson);
        File.WriteAllBytes(Path.Combine(assetDirectory, "sheets", "1.png"), AssetFixture.PngSheet.Create(64, 64));
        AssetFixture.WriteTerrainSidecar(assetDirectory, AssetFixture.TerrainCatalogJson.Replace("\"version\": 1", "\"version\": 2"));

        bool opened = controller.TryOpen(assetDirectory);

        Assert.True(opened);
        AssetContext context = controller.Current;
        Assert.False(context.Terrain.IsValid);
        Assert.False(context.Terrain.CanPaint);
        Assert.Contains("Unsupported version 2.", context.Terrain.Diagnostic);
        Assert.Equal(new[] { 1, 2 }, context.SheetIds);
        Assert.Equal(SpriteResolutionStatus.Ready, context.Resolve(new SpriteReference(1, 10)).Status);
    }

    [AvaloniaFact]
    public void TryOpen_SemanticallyInvalidTerrain_KeepsSpriteContextUsableWithTypedDiagnostic()
    {
        using AssetContextController controller = CreateController();
        string assetDirectory = WriteAssetDirectory("assets-terrain-unknown-peer", TwoSheetJson);
        File.WriteAllBytes(Path.Combine(assetDirectory, "sheets", "1.png"), AssetFixture.PngSheet.Create(64, 64));
        AssetFixture.WriteTerrainSidecar(assetDirectory, AssetFixture.TerrainCatalogJson.Replace("\"center\": \"11111111-1111-1111-1111-111111111111\"", "\"center\": \"22222222-2222-2222-2222-222222222222\""));

        bool opened = controller.TryOpen(assetDirectory);

        Assert.True(opened);
        AssetContext context = controller.Current;
        Assert.False(context.Terrain.IsValid);
        Assert.False(context.Terrain.CanPaint);
        Assert.Contains("references unknown terrain", context.Terrain.Diagnostic);
        Assert.Equal(new[] { 1, 2 }, context.SheetIds);
        Assert.Equal(SpriteResolutionStatus.Ready, context.Resolve(new SpriteReference(1, 10)).Status);
    }

    [AvaloniaFact]
    public void TryOpen_ManifestInvalidTerrain_KeepsSpriteContextUsableWithTypedDiagnostic()
    {
        using AssetContextController controller = CreateController();
        string assetDirectory = WriteAssetDirectory("assets-terrain-bad-sheet", TwoSheetJson);
        File.WriteAllBytes(Path.Combine(assetDirectory, "sheets", "1.png"), AssetFixture.PngSheet.Create(64, 64));
        AssetFixture.WriteTerrainSidecar(assetDirectory, AssetFixture.TerrainCatalogJson.Replace("\"sheet\": 1", "\"sheet\": 9"));

        bool opened = controller.TryOpen(assetDirectory);

        Assert.True(opened);
        AssetContext context = controller.Current;
        Assert.False(context.Terrain.IsValid);
        Assert.False(context.Terrain.CanPaint);
        Assert.Contains("is not declared in the sprite manifest", context.Terrain.Diagnostic);
        Assert.Equal(new[] { 1, 2 }, context.SheetIds);
        Assert.Equal(SpriteResolutionStatus.Ready, context.Resolve(new SpriteReference(1, 10)).Status);
    }

    [Fact]
    public void TryOpen_TerrainLoadDoesNotDecodeMissingSheets()
    {
        using AssetContextController controller = CreateController();
        string assetDirectory = WriteAssetDirectory("assets-no-sheets", TwoSheetJson);
        AssetFixture.WriteTerrainSidecar(assetDirectory, AssetFixture.TerrainCatalogJson);

        bool opened = controller.TryOpen(assetDirectory);

        Assert.True(opened);
        Assert.True(controller.Current.Terrain.IsValid);
    }

    [Fact]
    public void TryOpen_SecondSuccess_WithTerrainSidecar_RaisesCurrentChangedOnceAndDisposesOnlyReplaced()
    {
        using AssetContextController controller = CreateController();
        string firstDirectory = WriteAssetDirectory("assets-first", TwoSheetJson);
        Assert.True(controller.TryOpen(firstDirectory));
        AssetContext first = controller.Current;
        int events = 0;
        AssetContext? observedCurrent = null;
        bool observedOldDisposed = true;
        controller.CurrentChanged += (_, _) =>
        {
            events++;
            observedCurrent = controller.Current;
            observedOldDisposed = first.IsDisposed;
        };
        string secondDirectory = WriteAssetDirectory("assets-second", TwoSheetJson);
        AssetFixture.WriteTerrainSidecar(secondDirectory, AssetFixture.TerrainCatalogJson);

        bool opened = controller.TryOpen(secondDirectory);

        Assert.True(opened);
        AssetContext second = controller.Current;
        Assert.Equal(1, events);
        Assert.Same(second, observedCurrent);
        Assert.False(observedOldDisposed);
        Assert.True(first.IsDisposed);
        Assert.False(second.IsDisposed);
        Assert.False(first.Terrain.CanPaint);
        Assert.True(second.Terrain.IsValid);
        Assert.True(second.Terrain.CanPaint);
    }

    [Fact]
    public void TryOpen_InvalidBaseManifest_PreservesCurrent()
    {
        using AssetContextController controller = CreateController();
        string oldDirectory = WriteAssetDirectory("assets-old", TwoSheetJson);
        Assert.True(controller.TryOpen(oldDirectory));
        AssetContext oldContext = controller.Current;
        int events = 0;
        controller.CurrentChanged += (_, _) => events++;
        string badDirectory = WriteAssetDirectory("assets-bad-manifest", "this is not json");
        AssetFixture.WriteTerrainSidecar(badDirectory, AssetFixture.TerrainCatalogJson);

        bool opened = controller.TryOpen(badDirectory);

        Assert.False(opened);
        Assert.Equal(0, events);
        Assert.Same(oldContext, controller.Current);
        Assert.False(oldContext.IsDisposed);
    }

    [AvaloniaFact]
    public void TryOpen_ValidAnimationSidecar_ProducesGraphicCatalogAndAvailableViewer()
    {
        using AssetContextController controller = CreateController();
        string assetDirectory = AssetFixture.WriteAssetDirectory(_directory, "assets-graphics");
        AssetFixture.WriteAnimationSidecar(assetDirectory, AssetFixture.AnimationSidecarJson);

        bool opened = controller.TryOpen(assetDirectory);

        Assert.True(opened);
        AssetContext context = controller.Current;
        Assert.True(context.IsAvailable);
        Assert.NotNull(context.Graphics);
        Assert.True(context.GraphicViewerAvailability.IsAvailable);
        Assert.Null(context.GraphicViewerAvailability.Diagnostic);
        Assert.Equal(new[] { 1, 2 }, context.Graphics.GetSheets(null).ToArray());
        Assert.Equal(new[] { 1 }, context.Graphics.GetSheets(GraphicCategory.Body).ToArray());
        Assert.Equal(new[] { new GraphicCategoryMapping(GraphicCategory.Body, 1), new GraphicCategoryMapping(GraphicCategory.Tiles, null) }, context.Graphics.GetMappings(1));
        Assert.True(context.Graphics.TryGetAnimation(new GraphicAnimationKey(1, 10), out GraphicAnimation animation));
        Assert.Equal(new[] { new SpriteReference(1, 10), new SpriteReference(1, 11) }, animation.Frames);
        Assert.Equal(new[] { animation }, context.Graphics.GetAnimations(new SpriteReference(1, 11)));
        Assert.Equal(SpriteResolutionStatus.Ready, context.Resolve(new SpriteReference(1, 10)).Status);
    }

    [AvaloniaTheory]
    [InlineData("not json", "Animation manifest is not valid JSON: ")]
    [InlineData("""{ "version": 2, "sheets": {}, "animations": [] }""", "Root property 'version' must be 1.")]
    [InlineData("""{ "version": 1, "sheets": { "9": { "categories": [ { "name": "Tiles" } ] } }, "animations": [] }""", "Categorized sheet 9 is missing from the sprite manifest.")]
    public void TryOpen_InvalidAnimationSidecar_KeepsSpritesUsableWithTypedDiagnostic(string sidecarJson, string diagnostic)
    {
        using AssetContextController controller = CreateController();
        string assetDirectory = AssetFixture.WriteAssetDirectory(_directory, "assets-bad-graphics");
        AssetFixture.WriteAnimationSidecar(assetDirectory, sidecarJson);

        bool opened = controller.TryOpen(assetDirectory);

        Assert.True(opened);
        AssetContext context = controller.Current;
        Assert.True(context.IsAvailable);
        Assert.Null(context.Graphics);
        Assert.False(context.GraphicViewerAvailability.IsAvailable);
        Assert.Contains(diagnostic, context.GraphicViewerAvailability.Diagnostic);
        Assert.Equal(new[] { 1, 2 }, context.SheetIds);
        Assert.Equal(2, context.GetFrames(1).Count);
        Assert.Equal(SpriteResolutionStatus.Ready, context.Resolve(new SpriteReference(1, 10)).Status);
    }

    [Fact]
    public void InitialContext_GraphicViewerIsUnavailableWithActionableDiagnostic()
    {
        using AssetContextController controller = CreateController();
        AssetContext context = controller.Current;

        Assert.Null(context.Graphics);
        Assert.False(context.GraphicViewerAvailability.IsAvailable);
        Assert.Equal("Sprite assets are unavailable; load an asset directory to resolve graphic viewer previews.", context.GraphicViewerAvailability.Diagnostic);
    }

    [Fact]
    public void TryOpen_SecondSuccess_RaisesCurrentChangedOnceAfterSwapBeforeOldDisposal()
    {
        using AssetContextController controller = CreateController();
        string firstDirectory = WriteAssetDirectory("assets-first", TwoSheetJson);
        Assert.True(controller.TryOpen(firstDirectory));
        AssetContext first = controller.Current;
        int events = 0;
        AssetContext? observedCurrent = null;
        bool observedOldDisposed = true;
        controller.CurrentChanged += (_, _) =>
        {
            events++;
            observedCurrent = controller.Current;
            observedOldDisposed = first.IsDisposed;
        };
        string secondDirectory = WriteAssetDirectory("assets-second", TwoSheetJson);

        bool opened = controller.TryOpen(secondDirectory);

        Assert.True(opened);
        AssetContext second = controller.Current;
        Assert.Equal(1, events);
        Assert.Same(second, observedCurrent);
        Assert.False(observedOldDisposed);
        Assert.True(first.IsDisposed);
        Assert.False(second.IsDisposed);
    }

    [Fact]
    public void TryOpen_ThrowingCurrentChangedSubscriber_CannotPreventOldContextDisposal()
    {
        using AssetContextController controller = CreateController();
        string firstDirectory = WriteAssetDirectory("assets-first", TwoSheetJson);
        Assert.True(controller.TryOpen(firstDirectory));
        AssetContext first = controller.Current;
        controller.CurrentChanged += (_, _) => throw new InvalidOperationException("subscriber failure");
        string secondDirectory = WriteAssetDirectory("assets-second", TwoSheetJson);

        Assert.Throws<InvalidOperationException>(() => controller.TryOpen(secondDirectory));

        Assert.NotSame(first, controller.Current);
        Assert.True(first.IsDisposed);
        Assert.False(controller.Current.IsDisposed);
    }

    [Fact]
    public void TryOpen_FailedManifestOpen_RaisesNoCurrentChangedEvent()
    {
        using AssetContextController controller = CreateController();
        string firstDirectory = WriteAssetDirectory("assets-first", TwoSheetJson);
        Assert.True(controller.TryOpen(firstDirectory));
        AssetContext first = controller.Current;
        int events = 0;
        controller.CurrentChanged += (_, _) => events++;
        string badDirectory = WriteAssetDirectory("assets-bad", "this is not json");

        bool opened = controller.TryOpen(badDirectory);

        Assert.False(opened);
        Assert.Equal(0, events);
        Assert.Same(first, controller.Current);
        Assert.False(first.IsDisposed);
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
