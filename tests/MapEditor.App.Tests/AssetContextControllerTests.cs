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
using MapEditor.App.Terrain;
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

        bool opened = controller.TryOpen(secondDirectory);

        Assert.True(opened);
        Assert.NotSame(first, controller.Current);
        Assert.True(first.IsDisposed);
        Assert.False(controller.Current.IsDisposed);
        Assert.NotNull(controller.LastPublicationNotificationErrors);
        Assert.Equal(1, controller.LastPublicationNotificationErrors.Count);
        Assert.IsType<InvalidOperationException>(controller.LastPublicationNotificationErrors[0]);
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

public class AssetContextControllerTerrainPublicationTests : IDisposable
{
    private const string TwoSheetJson = """
        { "tileSize": 32, "sheets": {
          "1": { "10": [0, 0, 32, 32], "11": [32, 0, 32, 32] },
          "2": { "20": [0, 0, 32, 32] }
        } }
        """;

    private const string TwoTerrainCatalogJson = """
        { "version": 1,
          "terrains": [
            { "id": "11111111-1111-1111-1111-111111111111", "name": "Grass", "color": null },
            { "id": "22222222-2222-2222-2222-222222222222", "name": "Water", "color": null } ],
          "graphics": [
            { "sheet": 1, "graphic": 10, "center": "11111111-1111-1111-1111-111111111111",
              "north": null, "east": null, "south": null, "west": null,
              "northEast": null, "southEast": null, "southWest": null, "northWest": null },
            { "sheet": 1, "graphic": 11, "center": "22222222-2222-2222-2222-222222222222",
              "north": null, "east": null, "south": null, "west": null,
              "northEast": null, "southEast": null, "southWest": null, "northWest": null } ] }
        """;

    private static readonly Guid GrassId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid WaterId = new("22222222-2222-2222-2222-222222222222");

    private readonly string _directory = Directory.CreateTempSubdirectory("map-editor-terrain-pub-").FullName;
    private readonly FakeEditorDialogs _dialogs = new();
    private readonly WorkspaceViewModel _workspace;
    private readonly MapDocumentViewModel _viewModel;
    private readonly string _settingsPath;

    public AssetContextControllerTerrainPublicationTests()
    {
        _workspace = new WorkspaceViewModel(_dialogs, new MapFileStore());
        _viewModel = _workspace.ActiveDocument;
        _settingsPath = Path.Combine(_directory, "settings.json");
    }

    public void Dispose()
        => Directory.Delete(_directory, recursive: true);

    private AssetContextController CreateController()
        => new(_workspace, new AppSettingsStore(_settingsPath));

    private async Task AddDocumentAsync()
    {
        _dialogs.NewMapResult = new NewMapRequest(10, 10);
        await _workspace.NewAsync();
    }

    private string WriteAssetDirectory(string name)
    {
        string assetDirectory = Path.Combine(_directory, name);
        Directory.CreateDirectory(Path.Combine(assetDirectory, "sheets"));
        File.WriteAllText(Path.Combine(assetDirectory, "manifest.json"), TwoSheetJson);
        return assetDirectory;
    }

    private static TerrainCatalog ParseCatalog()
        => TerrainCatalogJson.Parse(AssetFixture.TerrainCatalogJson);

    private static TerrainCatalogPreparedSave PrepareSave(string assetDirectory, AssetContext context, TerrainCatalog catalog, TerrainFileRevision expectedRevision)
        => new TerrainCatalogFileStore().PrepareSave(assetDirectory, catalog, context.Cache.Manifest!, expectedRevision);

    private static TerrainCatalogSaveResult SaveResult(TerrainCatalogPreparedSave prepared, TerrainFileRevision revision)
        => new(prepared.OperationId, prepared.Catalog, prepared.Index, revision);

    [Fact]
    public void PrepareSave_FromOtherThread_Throws()
    {
        using AssetContextController controller = CreateController();
        string assetDirectory = WriteAssetDirectory("assets-thread");
        AssetFixture.WriteTerrainSidecar(assetDirectory, AssetFixture.TerrainCatalogJson);
        Assert.True(controller.TryOpen(assetDirectory));
        TerrainCatalogPreparedSave prepared = PrepareSave(assetDirectory, controller.Current, ParseCatalog(), controller.Current.Terrain.Revision);

        Assert.ThrowsAny<InvalidOperationException>(() => Task.Run(() =>
        {
            using TerrainOperationLease operation = controller.Gate.AcquireAsync().GetAwaiter().GetResult();
            controller.PrepareSave(operation, controller.Current, prepared);
        }).GetAwaiter().GetResult());
    }

    [Fact]
    public void RegisterTerrainGestureCancellation_FromOtherThread_Throws()
    {
        using AssetContextController controller = CreateController();

        Assert.ThrowsAny<InvalidOperationException>(() => Task.Run(() => controller.RegisterTerrainGestureCancellation(() => { })).GetAwaiter().GetResult());
    }

    [Fact]
    public void TryPrepareOpen_FromOtherThread_Throws()
    {
        using AssetContextController controller = CreateController();
        string assetDirectory = WriteAssetDirectory("assets-thread-root");

        Assert.ThrowsAny<InvalidOperationException>(() => Task.Run(() => controller.TryPrepareOpen(assetDirectory, out _, out _)).GetAwaiter().GetResult());
    }

    [Fact]
    public void CommitSave_FromOtherThread_Throws()
    {
        using AssetContextController controller = CreateController();
        string assetDirectory = WriteAssetDirectory("assets-thread-commit-terrain");
        AssetFixture.WriteTerrainSidecar(assetDirectory, AssetFixture.TerrainCatalogJson);
        Assert.True(controller.TryOpen(assetDirectory));
        AssetContext context = controller.Current;
        TerrainCatalogLoadResult before = context.Terrain;
        TerrainCatalogPreparedSave prepared = PrepareSave(assetDirectory, context, ParseCatalog(), before.Revision);
        using TerrainOperationLease operation = controller.Gate.AcquireAsync().GetAwaiter().GetResult();
        ITerrainCatalogSavePublication publication = controller.PrepareSave(operation, context, prepared);
        TerrainCatalogSaveResult result = SaveResult(prepared, TerrainFileRevision.FromBytes(prepared.CanonicalBytes));

        Assert.ThrowsAny<InvalidOperationException>(() => Task.Run(() => publication.Commit(result)).GetAwaiter().GetResult());

        Assert.Same(before, context.Terrain);
        publication.Dispose();
    }

    [Fact]
    public void CommitPreparedOpen_FromOtherThread_Throws()
    {
        using AssetContextController controller = CreateController();
        string assetDirectory = WriteAssetDirectory("assets-thread-commit");
        Assert.True(controller.TryPrepareOpen(assetDirectory, out PreparedAssetContext? prepared, out _));

        Assert.ThrowsAny<InvalidOperationException>(() => Task.Run(() =>
        {
            using TerrainOperationLease operation = controller.Gate.AcquireAsync().GetAwaiter().GetResult();
            controller.CommitPreparedOpen(operation, prepared!);
        }).GetAwaiter().GetResult());
    }

    [Fact]
    public void PrepareSave_WithStaleExpectedContext_Throws()
    {
        using AssetContextController controller = CreateController();
        string firstDirectory = WriteAssetDirectory("assets-stale-first");
        AssetFixture.WriteTerrainSidecar(firstDirectory, AssetFixture.TerrainCatalogJson);
        Assert.True(controller.TryOpen(firstDirectory));
        AssetContext stale = controller.Current;
        string secondDirectory = WriteAssetDirectory("assets-stale-second");
        AssetFixture.WriteTerrainSidecar(secondDirectory, AssetFixture.TerrainCatalogJson);
        Assert.True(controller.TryOpen(secondDirectory));
        TerrainCatalogPreparedSave prepared = PrepareSave(secondDirectory, controller.Current, ParseCatalog(), controller.Current.Terrain.Revision);
        using TerrainOperationLease operation = controller.Gate.AcquireAsync().GetAwaiter().GetResult();

        Assert.Throws<InvalidOperationException>(() => controller.PrepareSave(operation, stale, prepared));
    }

    [Fact]
    public void PrepareSave_WithMismatchedGateLease_Throws()
    {
        using AssetContextController controller = CreateController();
        string assetDirectory = WriteAssetDirectory("assets-lease");
        AssetFixture.WriteTerrainSidecar(assetDirectory, AssetFixture.TerrainCatalogJson);
        Assert.True(controller.TryOpen(assetDirectory));
        TerrainCatalogPreparedSave prepared = PrepareSave(assetDirectory, controller.Current, ParseCatalog(), controller.Current.Terrain.Revision);
        using TerrainOperationGate otherGate = new();
        using TerrainOperationLease operation = otherGate.AcquireAsync().GetAwaiter().GetResult();

        Assert.Throws<InvalidOperationException>(() => controller.PrepareSave(operation, controller.Current, prepared));
    }

    [Fact]
    public void PrepareSave_WithInvalidPreparedCandidate_Throws()
    {
        using AssetContextController controller = CreateController();
        string assetDirectory = WriteAssetDirectory("assets-invalid-prepared");
        AssetFixture.WriteTerrainSidecar(assetDirectory, AssetFixture.TerrainCatalogJson);
        Assert.True(controller.TryOpen(assetDirectory));
        Guid grass = new("11111111-1111-1111-1111-111111111111");
        var invalidCatalog = new TerrainCatalog(
            new[] { new TerrainDefinition(grass, "Grass", null) },
            new[] { new TerrainGraphicDefinition(new TerrainGraphicReference(9, 10), new TerrainPattern(Center: grass)) });
        var prepared = new TerrainCatalogPreparedSave(
            "op-invalid",
            Array.Empty<byte>(),
            invalidCatalog,
            null,
            Path.Combine(assetDirectory, TerrainAssetCatalog.FileName),
            controller.Current.Terrain.Revision);
        using TerrainOperationLease operation = controller.Gate.AcquireAsync().GetAwaiter().GetResult();

        Assert.Throws<InvalidOperationException>(() => controller.PrepareSave(operation, controller.Current, prepared));
    }

    [Fact]
    public void PrepareSave_WhilePublicationPending_Throws()
    {
        using AssetContextController controller = CreateController();
        string assetDirectory = WriteAssetDirectory("assets-reentrant");
        AssetFixture.WriteTerrainSidecar(assetDirectory, AssetFixture.TerrainCatalogJson);
        Assert.True(controller.TryOpen(assetDirectory));
        AssetContext context = controller.Current;
        TerrainCatalogPreparedSave prepared = PrepareSave(assetDirectory, context, ParseCatalog(), context.Terrain.Revision);
        using TerrainOperationLease operation = controller.Gate.AcquireAsync().GetAwaiter().GetResult();
        ITerrainCatalogSavePublication first = controller.PrepareSave(operation, context, prepared);

        try
        {
            Assert.Throws<InvalidOperationException>(() => controller.PrepareSave(operation, context, prepared));
        }
        finally
        {
            first.Dispose();
        }
    }

    [Fact]
    public void DisposeUncommittedPublication_PublishesNothingAndReleasesReservation()
    {
        using AssetContextController controller = CreateController();
        string assetDirectory = WriteAssetDirectory("assets-uncommitted");
        AssetFixture.WriteTerrainSidecar(assetDirectory, AssetFixture.TerrainCatalogJson);
        Assert.True(controller.TryOpen(assetDirectory));
        AssetContext context = controller.Current;
        TerrainCatalogLoadResult before = context.Terrain;
        TerrainCatalogPreparedSave prepared = PrepareSave(assetDirectory, context, ParseCatalog(), before.Revision);
        using TerrainOperationLease operation = controller.Gate.AcquireAsync().GetAwaiter().GetResult();
        ITerrainCatalogSavePublication publication = controller.PrepareSave(operation, context, prepared);
        int events = 0;
        controller.TerrainCatalogChanged += (_, _) => events++;

        publication.Dispose();

        Assert.Same(before, context.Terrain);
        Assert.Same(before, _viewModel.Terrain);
        Assert.Equal(0, events);
        ITerrainCatalogSavePublication second = controller.PrepareSave(operation, context, prepared);
        second.Commit(SaveResult(prepared, TerrainFileRevision.FromBytes(prepared.CanonicalBytes)));
        Assert.NotSame(before, controller.Current.Terrain);
        Assert.Equal(1, events);
    }

    [Fact]
    public void CommitTerrain_PreservesContextCacheRendererAndTint()
    {
        using AssetContextController controller = CreateController();
        string assetDirectory = WriteAssetDirectory("assets-identity");
        AssetFixture.WriteTerrainSidecar(assetDirectory, AssetFixture.TerrainCatalogJson);
        Assert.True(controller.TryOpen(assetDirectory));
        AssetContext context = controller.Current;
        SpriteAssetCache cache = context.Cache;
        MapRenderer renderer = context.Renderer;
        AvaloniaTintedSpriteCache tint = context.TintCache;
        TerrainCatalogLoadResult before = context.Terrain;
        TerrainCatalogPreparedSave prepared = PrepareSave(assetDirectory, context, ParseCatalog(), before.Revision);
        TerrainFileRevision revision = TerrainFileRevision.FromBytes(prepared.CanonicalBytes);
        using TerrainOperationLease operation = controller.Gate.AcquireAsync().GetAwaiter().GetResult();
        ITerrainCatalogSavePublication publication = controller.PrepareSave(operation, context, prepared);

        publication.Commit(SaveResult(prepared, revision));

        Assert.Same(context, controller.Current);
        Assert.Same(cache, context.Cache);
        Assert.Same(renderer, context.Renderer);
        Assert.Same(tint, context.TintCache);
        TerrainCatalogLoadResult after = context.Terrain;
        Assert.NotSame(before, after);
        Assert.True(after.IsValid);
        Assert.Equal(revision, after.Revision);
        Assert.Same(prepared.Catalog, after.Catalog);
        Assert.Same(prepared.Index, after.Index);
        Assert.Same(after, _viewModel.Terrain);
    }

    [Fact]
    public async Task CommitTerrain_ObserverSeesEveryDocumentReconciled()
    {
        await AddDocumentAsync();
        MapDocumentViewModel second = _workspace.ActiveDocument;
        using AssetContextController controller = CreateController();
        string assetDirectory = WriteAssetDirectory("assets-observers");
        AssetFixture.WriteTerrainSidecar(assetDirectory, AssetFixture.TerrainCatalogJson);
        Assert.True(controller.TryOpen(assetDirectory));
        AssetContext context = controller.Current;
        TerrainCatalogPreparedSave prepared = PrepareSave(assetDirectory, context, ParseCatalog(), context.Terrain.Revision);
        using TerrainOperationLease operation = await controller.Gate.AcquireAsync();
        bool firstDocumentSawAllReconciled = false;
        bool catalogChangedSawAllReconciled = false;
        _viewModel.CanvasInvalidated += () =>
        {
            firstDocumentSawAllReconciled = _workspace.Documents.All(document => ReferenceEquals(document.Terrain, controller.Current.Terrain));
        };
        controller.TerrainCatalogChanged += (_, e) =>
        {
            catalogChangedSawAllReconciled =
                ReferenceEquals(e.Terrain, controller.Current.Terrain) &&
                _workspace.Documents.All(document => ReferenceEquals(document.Terrain, controller.Current.Terrain));
        };
        ITerrainCatalogSavePublication publication = controller.PrepareSave(operation, context, prepared);

        publication.Commit(SaveResult(prepared, TerrainFileRevision.FromBytes(prepared.CanonicalBytes)));

        Assert.True(firstDocumentSawAllReconciled);
        Assert.True(catalogChangedSawAllReconciled);
        Assert.Same(controller.Current.Terrain, second.Terrain);
    }

    [Fact]
    public async Task CommitTerrain_ThrowingDocumentNotification_RecordsFailureAndNotifiesRemainingDocuments()
    {
        await AddDocumentAsync();
        MapDocumentViewModel second = _workspace.ActiveDocument;
        using AssetContextController controller = CreateController();
        string assetDirectory = WriteAssetDirectory("assets-callback-failure");
        AssetFixture.WriteTerrainSidecar(assetDirectory, AssetFixture.TerrainCatalogJson);
        Assert.True(controller.TryOpen(assetDirectory));
        AssetContext context = controller.Current;
        TerrainCatalogPreparedSave prepared = PrepareSave(assetDirectory, context, ParseCatalog(), context.Terrain.Revision);
        using TerrainOperationLease operation = await controller.Gate.AcquireAsync();
        int firstPalette = 0;
        _viewModel.CanvasInvalidated += () => throw new InvalidOperationException("canvas failure");
        _viewModel.PaletteInvalidated += () => firstPalette++;
        int secondCanvas = 0;
        int secondPalette = 0;
        second.CanvasInvalidated += () => secondCanvas++;
        second.PaletteInvalidated += () => secondPalette++;
        int catalogEvents = 0;
        controller.TerrainCatalogChanged += (_, _) => catalogEvents++;
        ITerrainCatalogSavePublication publication = controller.PrepareSave(operation, context, prepared);

        publication.Commit(SaveResult(prepared, TerrainFileRevision.FromBytes(prepared.CanonicalBytes)));

        Assert.Equal(1, firstPalette);
        Assert.Equal(1, secondCanvas);
        Assert.Equal(1, secondPalette);
        Assert.Equal(1, catalogEvents);
        Assert.NotNull(controller.LastPublicationNotificationErrors);
        Assert.Equal(1, controller.LastPublicationNotificationErrors.Count);
        Assert.IsType<InvalidOperationException>(controller.LastPublicationNotificationErrors[0]);
        Assert.Same(controller.Current.Terrain, _viewModel.Terrain);
        Assert.Same(controller.Current.Terrain, second.Terrain);
    }

    [Fact]
    public void CommitTerrain_ThrowingCatalogChangedSubscriber_DoesNotPreventLaterSubscribers()
    {
        using AssetContextController controller = CreateController();
        string assetDirectory = WriteAssetDirectory("assets-subscriber-failure");
        AssetFixture.WriteTerrainSidecar(assetDirectory, AssetFixture.TerrainCatalogJson);
        Assert.True(controller.TryOpen(assetDirectory));
        AssetContext context = controller.Current;
        TerrainCatalogPreparedSave prepared = PrepareSave(assetDirectory, context, ParseCatalog(), context.Terrain.Revision);
        using TerrainOperationLease operation = controller.Gate.AcquireAsync().GetAwaiter().GetResult();
        int laterSubscribers = 0;
        controller.TerrainCatalogChanged += (_, _) => throw new InvalidOperationException("first subscriber failure");
        controller.TerrainCatalogChanged += (_, _) => laterSubscribers++;
        ITerrainCatalogSavePublication publication = controller.PrepareSave(operation, context, prepared);

        publication.Commit(SaveResult(prepared, TerrainFileRevision.FromBytes(prepared.CanonicalBytes)));

        Assert.Equal(1, laterSubscribers);
        Assert.NotNull(controller.LastPublicationNotificationErrors);
        Assert.Equal(1, controller.LastPublicationNotificationErrors.Count);
        Assert.IsType<InvalidOperationException>(controller.LastPublicationNotificationErrors[0]);
    }

    [Fact]
    public void CommitLoaded_ValidReload_PublishesLoadedResult()
    {
        using AssetContextController controller = CreateController();
        string assetDirectory = WriteAssetDirectory("assets-loaded-valid");
        Assert.True(controller.TryOpen(assetDirectory));
        AssetContext context = controller.Current;
        TerrainCatalog catalog = ParseCatalog();
        var loaded = TerrainCatalogLoadResult.Valid(
            Path.Combine(assetDirectory, TerrainAssetCatalog.FileName),
            new TerrainFileRevision(true, "abc123"),
            catalog,
            TerrainAssetCatalog.Validate(catalog, context.Cache.Manifest!).Index!,
            Array.Empty<TerrainValidationIssue>());
        using TerrainOperationLease operation = controller.Gate.AcquireAsync().GetAwaiter().GetResult();
        ITerrainCatalogLoadedPublication publication = controller.PrepareLoaded(operation, context, loaded, TerrainLoadedPublicationKind.ValidReload);

        publication.Commit();

        Assert.Same(context, controller.Current);
        Assert.Same(loaded, context.Terrain);
        Assert.Same(loaded, _viewModel.Terrain);
        Assert.NotNull(controller.LastPublicationNotificationErrors);
        Assert.Equal(0, controller.LastPublicationNotificationErrors.Count);
    }

    [Fact]
    public void CommitLoaded_ConfirmedMalformedReload_PublishesInvalidTerrainWithoutAffectingAssets()
    {
        using AssetContextController controller = CreateController();
        string assetDirectory = WriteAssetDirectory("assets-loaded-malformed");
        File.WriteAllBytes(Path.Combine(assetDirectory, "sheets", "1.png"), AssetFixture.PngSheet.Create(64, 64));
        AssetFixture.WriteTerrainSidecar(assetDirectory, AssetFixture.TerrainCatalogJson);
        Assert.True(controller.TryOpen(assetDirectory));
        AssetContext context = controller.Current;
        SpriteAssetCache cache = context.Cache;
        var loaded = TerrainCatalogLoadResult.Invalid(
            Path.Combine(assetDirectory, TerrainAssetCatalog.FileName),
            new TerrainFileRevision(true, "deadbeef"),
            new[] { new TerrainValidationIssue(TerrainValidationSeverity.Error, TerrainValidationCode.MissingSpriteFrame, "boom") },
            "Terrain validation failed");
        using TerrainOperationLease operation = controller.Gate.AcquireAsync().GetAwaiter().GetResult();
        ITerrainCatalogLoadedPublication publication = controller.PrepareLoaded(operation, context, loaded, TerrainLoadedPublicationKind.ConfirmedMalformedReload);

        publication.Commit();

        Assert.Same(loaded, context.Terrain);
        Assert.False(context.Terrain.IsValid);
        Assert.False(context.Terrain.CanPaint);
        Assert.Same(cache, context.Cache);
        Assert.True(context.IsAvailable);
        Assert.Same(loaded, _viewModel.Terrain);
    }

    [Fact]
    public void PrepareLoaded_WithMismatchedKind_Throws()
    {
        using AssetContextController controller = CreateController();
        string assetDirectory = WriteAssetDirectory("assets-loaded-kind");
        Assert.True(controller.TryOpen(assetDirectory));
        AssetContext context = controller.Current;
        var valid = TerrainCatalogLoadResult.ValidEmpty(Path.Combine(assetDirectory, TerrainAssetCatalog.FileName));
        var invalidWithoutRevision = TerrainCatalogLoadResult.Invalid(
            Path.Combine(assetDirectory, TerrainAssetCatalog.FileName),
            TerrainFileRevision.Missing,
            Array.Empty<TerrainValidationIssue>(),
            "bad");
        using TerrainOperationLease operation = controller.Gate.AcquireAsync().GetAwaiter().GetResult();

        Assert.Throws<InvalidOperationException>(() => controller.PrepareLoaded(operation, context, invalidWithoutRevision, TerrainLoadedPublicationKind.ValidReload));
        Assert.Throws<InvalidOperationException>(() => controller.PrepareLoaded(operation, context, valid, TerrainLoadedPublicationKind.ConfirmedMalformedReload));
        Assert.Throws<InvalidOperationException>(() => controller.PrepareLoaded(operation, context, invalidWithoutRevision, TerrainLoadedPublicationKind.ConfirmedMalformedReload));
    }

    [Fact]
    public void RegisterTerrainGestureCancellation_InvokesSnapshotAndAllowsSelfUnregistration()
    {
        using AssetContextController controller = CreateController();
        string assetDirectory = WriteAssetDirectory("assets-gesture");
        AssetFixture.WriteTerrainSidecar(assetDirectory, AssetFixture.TerrainCatalogJson);
        Assert.True(controller.TryOpen(assetDirectory));
        AssetContext context = controller.Current;
        TerrainCatalogPreparedSave prepared = PrepareSave(assetDirectory, context, ParseCatalog(), context.Terrain.Revision);
        using TerrainOperationLease operation = controller.Gate.AcquireAsync().GetAwaiter().GetResult();
        int calls = 0;
        IDisposable? registration = null;
        registration = controller.RegisterTerrainGestureCancellation(() =>
        {
            calls++;
            registration.Dispose();
        });
        ITerrainCatalogSavePublication first = controller.PrepareSave(operation, context, prepared);
        first.Dispose();
        ITerrainCatalogSavePublication second = controller.PrepareSave(operation, context, prepared);
        second.Dispose();

        Assert.Equal(1, calls);
        registration.Dispose();
    }

    [Fact]
    public void RegisterTerrainGestureCancellation_ThrowingCallback_FailsPrepareAndReleasesReservation()
    {
        using AssetContextController controller = CreateController();
        string assetDirectory = WriteAssetDirectory("assets-gesture-throw");
        AssetFixture.WriteTerrainSidecar(assetDirectory, AssetFixture.TerrainCatalogJson);
        Assert.True(controller.TryOpen(assetDirectory));
        AssetContext context = controller.Current;
        TerrainCatalogLoadResult before = context.Terrain;
        TerrainCatalogPreparedSave prepared = PrepareSave(assetDirectory, context, ParseCatalog(), before.Revision);
        using TerrainOperationLease operation = controller.Gate.AcquireAsync().GetAwaiter().GetResult();
        IDisposable registration = controller.RegisterTerrainGestureCancellation(() => throw new InvalidOperationException("gesture failure"));

        Assert.Throws<InvalidOperationException>(() => controller.PrepareSave(operation, context, prepared));

        Assert.Same(before, context.Terrain);
        registration.Dispose();
        ITerrainCatalogSavePublication second = controller.PrepareSave(operation, context, prepared);
        second.Commit(SaveResult(prepared, TerrainFileRevision.FromBytes(prepared.CanonicalBytes)));
        Assert.NotSame(before, context.Terrain);
    }

    [Fact]
    public void TryPrepareOpen_PreservesCurrentUntilCommit()
    {
        using AssetContextController controller = CreateController();
        string firstDirectory = WriteAssetDirectory("assets-root-first");
        Assert.True(controller.TryOpen(firstDirectory));
        AssetContext first = controller.Current;
        int events = 0;
        controller.CurrentChanged += (_, _) => events++;
        string secondDirectory = WriteAssetDirectory("assets-root-second");

        bool preparedOk = controller.TryPrepareOpen(secondDirectory, out PreparedAssetContext? prepared, out Exception? failure);

        Assert.True(preparedOk);
        Assert.Null(failure);
        Assert.NotNull(prepared);
        Assert.Same(first, controller.Current);
        Assert.Equal(0, events);
        Assert.False(prepared!.Context.IsDisposed);
        using TerrainOperationLease operation = controller.Gate.AcquireAsync().GetAwaiter().GetResult();
        controller.CommitPreparedOpen(operation, prepared);
        Assert.Same(prepared.Context, controller.Current);
        Assert.Equal(1, events);
        Assert.True(first.IsDisposed);
        prepared.Dispose();
        Assert.False(prepared.Context.IsDisposed);
    }

    [Fact]
    public void TryPrepareOpen_Failure_PreservesCurrentAndReportsFailure()
    {
        using AssetContextController controller = CreateController();
        string firstDirectory = WriteAssetDirectory("assets-root-bad-first");
        Assert.True(controller.TryOpen(firstDirectory));
        AssetContext first = controller.Current;
        string badDirectory = WriteAssetDirectory("assets-root-bad");
        File.WriteAllText(Path.Combine(badDirectory, "manifest.json"), "this is not json");

        bool preparedOk = controller.TryPrepareOpen(badDirectory, out PreparedAssetContext? prepared, out Exception? failure);

        Assert.False(preparedOk);
        Assert.Null(prepared);
        Assert.IsType<SpriteManifestException>(failure);
        Assert.Same(first, controller.Current);
        Assert.False(first.IsDisposed);
    }

    [Fact]
    public void DiscardPreparedOpen_DisposesCandidateAndPreservesCurrent()
    {
        using AssetContextController controller = CreateController();
        string firstDirectory = WriteAssetDirectory("assets-root-discard-first");
        Assert.True(controller.TryOpen(firstDirectory));
        AssetContext first = controller.Current;
        int events = 0;
        controller.CurrentChanged += (_, _) => events++;
        string secondDirectory = WriteAssetDirectory("assets-root-discard");
        Assert.True(controller.TryPrepareOpen(secondDirectory, out PreparedAssetContext? prepared, out _));

        prepared!.Dispose();

        Assert.True(prepared.Context.IsDisposed);
        Assert.Same(first, controller.Current);
        Assert.Equal(0, events);
        Assert.False(first.IsDisposed);
    }

    [Fact]
    public void CommitPreparedOpen_ThrowingGestureCancellation_PreservesCurrentAndSkipsParticipant()
    {
        using AssetContextController controller = CreateController();
        string firstDirectory = WriteAssetDirectory("assets-root-cancel-first");
        Assert.True(controller.TryOpen(firstDirectory));
        AssetContext first = controller.Current;
        string secondDirectory = WriteAssetDirectory("assets-root-cancel");
        Assert.True(controller.TryPrepareOpen(secondDirectory, out PreparedAssetContext? prepared, out _));
        using TerrainOperationLease operation = controller.Gate.AcquireAsync().GetAwaiter().GetResult();
        var participant = new RecordingReconciliation();
        int events = 0;
        controller.CurrentChanged += (_, _) => events++;
        IDisposable registration = controller.RegisterTerrainGestureCancellation(() => throw new InvalidOperationException("gesture failure"));

        Assert.Throws<InvalidOperationException>(() => controller.CommitPreparedOpen(operation, prepared, participant));

        Assert.Same(first, controller.Current);
        Assert.Equal(0, events);
        Assert.False(participant.Applied);
        Assert.False(participant.Notified);
        Assert.False(prepared!.Context.IsDisposed);
        registration.Dispose();
        prepared.Dispose();
        Assert.True(prepared.Context.IsDisposed);
    }

    [Fact]
    public void CommitPreparedOpen_ParticipantAppliesBeforeFirstRootObserver()
    {
        using AssetContextController controller = CreateController();
        string assetDirectory = WriteAssetDirectory("assets-root-participant");
        Assert.True(controller.TryPrepareOpen(assetDirectory, out PreparedAssetContext? prepared, out _));
        using TerrainOperationLease operation = controller.Gate.AcquireAsync().GetAwaiter().GetResult();
        var timeline = new List<string>();
        var participant = new RecordingReconciliation { Timeline = timeline };
        _viewModel.CanvasInvalidated += () => timeline.Add("document.canvas");
        controller.CurrentChanged += (_, _) => timeline.Add("current.changed");

        controller.CommitPreparedOpen(operation, prepared, participant);

        Assert.Equal(new[] { "participant.apply", "document.canvas", "participant.notify", "current.changed" }, timeline);
    }

    [Fact]
    public void CommitPreparedOpen_RaisesCurrentChangedOnceAndNoTerrainCatalogChanged()
    {
        using AssetContextController controller = CreateController();
        string assetDirectory = WriteAssetDirectory("assets-root-events");
        Assert.True(controller.TryPrepareOpen(assetDirectory, out PreparedAssetContext? prepared, out _));
        using TerrainOperationLease operation = controller.Gate.AcquireAsync().GetAwaiter().GetResult();
        int currentEvents = 0;
        int terrainEvents = 0;
        controller.CurrentChanged += (_, _) => currentEvents++;
        controller.TerrainCatalogChanged += (_, _) => terrainEvents++;

        controller.CommitPreparedOpen(operation, prepared);

        Assert.Equal(1, currentEvents);
        Assert.Equal(0, terrainEvents);
    }

    [Fact]
    public async Task DocumentAddedAfterTerrainCommit_UsesCurrentTerrain()
    {
        using AssetContextController controller = CreateController();
        string assetDirectory = WriteAssetDirectory("assets-late-terrain");
        AssetFixture.WriteTerrainSidecar(assetDirectory, AssetFixture.TerrainCatalogJson);
        Assert.True(controller.TryOpen(assetDirectory));
        AssetContext context = controller.Current;
        TerrainCatalogPreparedSave prepared = PrepareSave(assetDirectory, context, ParseCatalog(), context.Terrain.Revision);
        using TerrainOperationLease operation = await controller.Gate.AcquireAsync();
        ITerrainCatalogSavePublication publication = controller.PrepareSave(operation, context, prepared);
        publication.Commit(SaveResult(prepared, TerrainFileRevision.FromBytes(prepared.CanonicalBytes)));
        TerrainCatalogLoadResult published = controller.Current.Terrain;

        await AddDocumentAsync();

        Assert.Same(published, _workspace.ActiveDocument.Terrain);
    }

    [Fact]
    public void CommitPreparedOpen_WhilePublicationPending_Throws()
    {
        using AssetContextController controller = CreateController();
        string firstDirectory = WriteAssetDirectory("assets-root-interleave-first");
        AssetFixture.WriteTerrainSidecar(firstDirectory, AssetFixture.TerrainCatalogJson);
        Assert.True(controller.TryOpen(firstDirectory));
        AssetContext context = controller.Current;
        TerrainCatalogPreparedSave prepared = PrepareSave(firstDirectory, context, ParseCatalog(), context.Terrain.Revision);
        using TerrainOperationLease operation = controller.Gate.AcquireAsync().GetAwaiter().GetResult();
        ITerrainCatalogSavePublication publication = controller.PrepareSave(operation, context, prepared);
        string secondDirectory = WriteAssetDirectory("assets-root-interleave-second");
        Assert.True(controller.TryPrepareOpen(secondDirectory, out PreparedAssetContext? root, out _));
        int events = 0;
        controller.CurrentChanged += (_, _) => events++;

        Assert.Throws<InvalidOperationException>(() => controller.CommitPreparedOpen(operation, root));

        Assert.Same(context, controller.Current);
        Assert.Equal(0, events);
        Assert.False(root!.Context.IsDisposed);
        publication.Dispose();
        root.Dispose();
    }

    [Fact]
    public void CommitPreparedOpen_ThrowingOldContextDisposal_RecordsFailureWithoutThrowing()
    {
        using AssetContextController controller = new(
            _workspace,
            new AppSettingsStore(_settingsPath),
            path => AssetContext.Create(path, new ThrowingDisposeSheetLoader()));
        string firstDirectory = WriteAssetDirectory("assets-root-dispose-first");
        Assert.True(controller.TryOpen(firstDirectory));
        AssetContext first = controller.Current;
        Assert.Equal(SpriteResolutionStatus.Ready, first.Resolve(new SpriteReference(1, 10)).Status);
        string secondDirectory = WriteAssetDirectory("assets-root-dispose-second");
        Assert.True(controller.TryPrepareOpen(secondDirectory, out PreparedAssetContext? prepared, out _));
        using TerrainOperationLease operation = controller.Gate.AcquireAsync().GetAwaiter().GetResult();
        _viewModel.PropertyChanged += (_, _) => throw new InvalidOperationException("property failure");
        _viewModel.CanvasInvalidated += () => throw new InvalidOperationException("canvas failure");
        _viewModel.PaletteInvalidated += () => throw new InvalidOperationException("palette failure");
        controller.CurrentChanged += (_, _) => throw new InvalidOperationException("subscriber failure");

        controller.CommitPreparedOpen(operation, prepared);

        Assert.Same(prepared.Context, controller.Current);
        Assert.True(first.IsDisposed);
        Assert.NotNull(controller.LastPublicationNotificationErrors);
        Assert.Equal(5, controller.LastPublicationNotificationErrors.Count);
        Assert.Equal("image disposal failure", controller.LastPublicationNotificationErrors[4].Message);
    }

    [Fact]
    public async Task CommitTerrain_PerDocumentSelectionsSurvivePublication()
    {
        await AddDocumentAsync();
        MapDocumentViewModel second = _workspace.ActiveDocument;
        using AssetContextController controller = CreateController();
        string assetDirectory = WriteAssetDirectory("assets-selection-survive");
        AssetFixture.WriteTerrainSidecar(assetDirectory, TwoTerrainCatalogJson);
        Assert.True(controller.TryOpen(assetDirectory));
        AssetContext context = controller.Current;
        _viewModel.SelectTerrain(GrassId);
        second.SelectTerrain(WaterId);
        TerrainCatalog catalog = TerrainCatalogJson.Parse(TwoTerrainCatalogJson);
        TerrainCatalogPreparedSave prepared = PrepareSave(assetDirectory, context, catalog, context.Terrain.Revision);
        using TerrainOperationLease operation = await controller.Gate.AcquireAsync();
        ITerrainCatalogSavePublication publication = controller.PrepareSave(operation, context, prepared);

        publication.Commit(SaveResult(prepared, TerrainFileRevision.FromBytes(prepared.CanonicalBytes)));

        Assert.Equal(GrassId, _viewModel.SelectedTerrainId);
        Assert.Equal(WaterId, second.SelectedTerrainId);
        Assert.Equal(MapEditTool.Terrain, _viewModel.ActiveTool);
        Assert.Equal(MapEditTool.Terrain, second.ActiveTool);
        Assert.Same(context.Terrain, _viewModel.Terrain);
        Assert.Same(context.Terrain, second.Terrain);
        Assert.NotNull(controller.LastPublicationNotificationErrors);
        Assert.Equal(0, controller.LastPublicationNotificationErrors.Count);
    }

    [Fact]
    public void CommitLoaded_RemovedSelectedTerrain_FallsBackToPencilAndPreservesMapBytes()
    {
        using AssetContextController controller = CreateController();
        string assetDirectory = WriteAssetDirectory("assets-selection-removed");
        File.WriteAllBytes(Path.Combine(assetDirectory, "sheets", "1.png"), AssetFixture.PngSheet.Create(64, 64));
        AssetFixture.WriteTerrainSidecar(assetDirectory, TwoTerrainCatalogJson);
        Assert.True(controller.TryOpen(assetDirectory));
        AssetContext context = controller.Current;
        _viewModel.SelectTerrain(GrassId);
        MapEditSession session = _viewModel.Session;
        session.SelectedTileLayer = new MapTileLayer(5, 5);
        session.BeginStroke(MapEditTool.Pencil, 0, 0);
        Assert.True(session.CompleteStroke());
        byte[] bytesBefore = MapCodec.Encode(session.Document);
        bool canUndoBefore = _viewModel.CanUndo;
        bool dirtyBefore = _viewModel.IsDirty;
        TerrainCatalog waterOnly = new(
            new[] { new TerrainDefinition(WaterId, "Water", null) },
            new[] { new TerrainGraphicDefinition(new TerrainGraphicReference(1, 11), new TerrainPattern(Center: WaterId)) });
        TerrainCatalogValidationResult validation = TerrainAssetCatalog.Validate(waterOnly, context.Cache.Manifest!);
        var loaded = TerrainCatalogLoadResult.Valid(
            Path.Combine(assetDirectory, TerrainAssetCatalog.FileName),
            new TerrainFileRevision(true, "water-only"),
            waterOnly,
            validation.Index!,
            validation.Issues);
        using TerrainOperationLease operation = controller.Gate.AcquireAsync().GetAwaiter().GetResult();
        ITerrainCatalogLoadedPublication publication = controller.PrepareLoaded(operation, context, loaded, TerrainLoadedPublicationKind.ValidReload);

        publication.Commit();

        Assert.Null(_viewModel.SelectedTerrainId);
        Assert.Equal(MapEditTool.Pencil, _viewModel.ActiveTool);
        Assert.Equal(bytesBefore, MapCodec.Encode(session.Document));
        Assert.Equal(canUndoBefore, _viewModel.CanUndo);
        Assert.Equal(dirtyBefore, _viewModel.IsDirty);
        Assert.Throws<InvalidOperationException>(() => _viewModel.ActiveTool = MapEditTool.Terrain);
    }

    [Fact]
    public void CommitLoaded_RemovedGraphics_DoesNotMutateMapCells()
    {
        using AssetContextController controller = CreateController();
        string assetDirectory = WriteAssetDirectory("assets-removed-graphics");
        File.WriteAllBytes(Path.Combine(assetDirectory, "sheets", "1.png"), AssetFixture.PngSheet.Create(64, 64));
        AssetFixture.WriteTerrainSidecar(assetDirectory, TwoTerrainCatalogJson);
        Assert.True(controller.TryOpen(assetDirectory));
        AssetContext context = controller.Current;
        _viewModel.SelectTerrain(GrassId);
        MapEditSession session = _viewModel.Session;
        session.SelectedTileLayer = new MapTileLayer(5, 5);
        session.BeginStroke(MapEditTool.Pencil, 0, 0);
        Assert.True(session.CompleteStroke());
        byte[] bytesBefore = MapCodec.Encode(session.Document);
        bool canUndoBefore = _viewModel.CanUndo;
        bool dirtyBefore = _viewModel.IsDirty;
        var removedCatalog = new TerrainCatalog(
            new[]
            {
                new TerrainDefinition(GrassId, "Grass", null),
                new TerrainDefinition(WaterId, "Water", null)
            },
            new[] { new TerrainGraphicDefinition(new TerrainGraphicReference(1, 10), new TerrainPattern(Center: GrassId)) });
        TerrainCatalogValidationResult validation = TerrainAssetCatalog.Validate(removedCatalog, context.Cache.Manifest!);
        Assert.False(validation.IsValid);
        var loaded = TerrainCatalogLoadResult.Invalid(
            Path.Combine(assetDirectory, TerrainAssetCatalog.FileName),
            new TerrainFileRevision(true, "removed-graphics"),
            validation.Issues,
            "Terrain validation failed");
        using TerrainOperationLease operation = controller.Gate.AcquireAsync().GetAwaiter().GetResult();
        ITerrainCatalogLoadedPublication publication = controller.PrepareLoaded(operation, context, loaded, TerrainLoadedPublicationKind.ConfirmedMalformedReload);

        publication.Commit();

        Assert.Equal(bytesBefore, MapCodec.Encode(session.Document));
        Assert.Equal(canUndoBefore, _viewModel.CanUndo);
        Assert.Equal(dirtyBefore, _viewModel.IsDirty);
        Assert.Null(_viewModel.SelectedTerrainId);
        Assert.Equal(MapEditTool.Pencil, _viewModel.ActiveTool);
        Assert.Empty(_viewModel.Terrains);
        Assert.Same(context.Cache, controller.Current.Cache);
    }

    [Fact]
    public void CommitLoaded_MalformedTerrain_DisablesOnlyTerrainProperties()
    {
        using AssetContextController controller = CreateController();
        string assetDirectory = WriteAssetDirectory("assets-malformed-only-terrain");
        File.WriteAllBytes(Path.Combine(assetDirectory, "sheets", "1.png"), AssetFixture.PngSheet.Create(64, 64));
        AssetFixture.WriteTerrainSidecar(assetDirectory, TwoTerrainCatalogJson);
        Assert.True(controller.TryOpen(assetDirectory));
        AssetContext context = controller.Current;
        _viewModel.SelectTerrain(GrassId);
        Assert.Equal(MapEditTool.Terrain, _viewModel.ActiveTool);
        var loaded = TerrainCatalogLoadResult.Invalid(
            Path.Combine(assetDirectory, TerrainAssetCatalog.FileName),
            new TerrainFileRevision(true, "malformed"),
            new[] { new TerrainValidationIssue(TerrainValidationSeverity.Error, TerrainValidationCode.MissingSpriteFrame, "boom") },
            "Terrain validation failed");
        using TerrainOperationLease operation = controller.Gate.AcquireAsync().GetAwaiter().GetResult();
        ITerrainCatalogLoadedPublication publication = controller.PrepareLoaded(operation, context, loaded, TerrainLoadedPublicationKind.ConfirmedMalformedReload);

        publication.Commit();

        Assert.Null(_viewModel.SelectedTerrainId);
        Assert.Empty(_viewModel.Terrains);
        Assert.False(_viewModel.TerrainAvailability!.IsValid);
        Assert.Equal("Terrain validation failed", _viewModel.TerrainAvailability.Diagnostic);
        Assert.Equal(MapEditTool.Pencil, _viewModel.ActiveTool);
        Assert.Throws<InvalidOperationException>(() => _viewModel.ActiveTool = MapEditTool.Terrain);
        _viewModel.ActiveTool = MapEditTool.Eraser;
        Assert.Equal(MapEditTool.Eraser, _viewModel.ActiveTool);
        _viewModel.Brush = new MapTileLayer(9, 9);
        Assert.Equal(new MapTileLayer(9, 9), _viewModel.Brush);
        Assert.Equal(new[] { 1, 2 }, _viewModel.SheetIds);
        Assert.True(controller.Current.IsAvailable);
        Assert.Same(context.Cache, controller.Current.Cache);
    }

    private sealed class RecordingReconciliation : IPreparedAssetReconciliation
    {
        public bool Applied;
        public bool Notified;
        public List<string>? Timeline;

        public void Apply()
        {
            Applied = true;
            Timeline?.Add("participant.apply");
        }

        public void Notify()
        {
            Notified = true;
            Timeline?.Add("participant.notify");
        }
    }

    private sealed class ThrowingDisposeSheetLoader : ISpriteSheetLoader
    {
        public SpriteSheetLoadResult Load(string path) => SpriteSheetLoadResult.Success(new ThrowingDisposeImage());
    }

    private sealed class ThrowingDisposeImage : ISpriteSheetImage
    {
        public int PixelWidth => 64;

        public int PixelHeight => 64;

        public void Dispose() => throw new InvalidOperationException("image disposal failure");
    }
}
