using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MapEditor.App.Rendering;
using MapEditor.App.Terrain;
using MapEditor.App.Tests.Fixtures;
using MapEditor.App.ViewModels;
using MapEditor.Core;
using MapEditor.Core.Terrain;
using MapEditor.Rendering.Terrain;
using Xunit;

namespace MapEditor.App.Tests;

public sealed class TerrainEditorEndToEndTests
{
    [AvaloniaFact]
    public void LoadSelectPaintUndoManageSaveReload_EndToEnd()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        string assetDirectory = WriteAssets(harness, TerrainCatalogJson.Serialize(ReplaceableCatalog()));
        LoadAssets(harness, assetDirectory);
        MapDocumentViewModel document = harness.ViewModel;
        string originalId = Assert.Single(harness.Assets.Current.Terrain.Runtime!.EnabledSets).Id;

        SelectTerrain(harness);
        Paint(harness, 0, 0);
        Assert.Equal(new MapTileLayer(1, 10), document.Session.Document[0, 0].GetLayer(0));
        Assert.Equal(1, document.Session.HistoryVersion);
        Assert.True(document.Undo());
        Assert.Equal(default, document.Session.Document[0, 0].GetLayer(0));
        Assert.True(document.Redo());
        Assert.Equal(new MapTileLayer(1, 10), document.Session.Document[0, 0].GetLayer(0));

        TerrainCatalogManager manager = OpenManager(harness);
        ReplaceVariants(manager, 20);
        Point canceledCell = TileCenter(harness, 1, 0);
        harness.Window.MouseDown(canceledCell, MouseButton.Left, RawInputModifiers.None);
        Assert.Equal(new MapTileLayer(1, 10), document.Session.Document[1, 0].GetLayer(0));
        long historyBeforeSave = document.Session.HistoryVersion;

        TerrainCatalogSaveResult result = manager.Save();

        Assert.Equal(TerrainCatalogSaveStatus.Succeeded, result.Status);
        Assert.Equal(default, document.Session.Document[1, 0].GetLayer(0));
        Assert.Equal(historyBeforeSave, document.Session.HistoryVersion);
        Assert.False(document.Session.HasActiveStroke);
        string catalogPath = Path.Combine(assetDirectory, "terrain-brushes.json");
        byte[] catalogBytes = File.ReadAllBytes(catalogPath);
        TerrainCatalog savedCatalog = TerrainCatalogJson.Parse(System.Text.Encoding.UTF8.GetString(catalogBytes));
        Assert.Equal(catalogBytes, System.Text.Encoding.UTF8.GetBytes(TerrainCatalogJson.Serialize(savedCatalog)));
        string changedId = Assert.Single(harness.Assets.Current.Terrain.Runtime!.EnabledSets).Id;
        Assert.NotEqual(originalId, changedId);
        Assert.Equal(changedId, document.SelectedTerrainId);
        Assert.All(savedCatalog.Sets.Single().Masks, mask => Assert.Equal(new TerrainGraphicReference(1, 20), Assert.Single(mask.Variants)));

        Paint(harness, 1, 0);
        Assert.Equal(new MapTileLayer(1, 20), document.Session.Document[1, 0].GetLayer(0));
        string mapPath = Path.Combine(harness.TempDirectory, "terrain-workflow.map");
        SaveMap(harness, mapPath);
        byte[] expectedMapBytes = MapCodec.Encode(document.Session.Document);
        Assert.Equal(expectedMapBytes, File.ReadAllBytes(mapPath));
        MapDocument savedMap = MapCodec.Decode(File.ReadAllBytes(mapPath));
        Assert.Equal(new MapTileLayer(1, 10), savedMap[0, 0].GetLayer(0));
        Assert.Equal(new MapTileLayer(1, 20), savedMap[1, 0].GetLayer(0));

        using MainWindowHarness reopened = MainWindowHarness.Create();
        LoadAssets(reopened, assetDirectory);
        OpenMap(reopened, mapPath);
        MapDocumentViewModel reopenedDocument = reopened.Workspace.ActiveDocument;
        Assert.Equal(changedId, Assert.Single(reopened.Assets.Current.Terrain.Runtime!.EnabledSets).Id);
        Assert.Equal(expectedMapBytes, MapCodec.Encode(reopenedDocument.Session.Document));
        Assert.False(reopenedDocument.Session.CanUndo);
        SelectTerrain(reopened);
        Paint(reopened, 2, 0);
        Assert.Equal(new MapTileLayer(1, 20), reopenedDocument.Session.Document[2, 0].GetLayer(0));
        Assert.Equal(new MapTileLayer(1, 10), reopenedDocument.Session.Document[0, 0].GetLayer(0));
    }

    [AvaloniaFact]
    public void MalformedTerrain_ManualTileEditingAndMapSaveStillWork_EndToEnd()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        string assetDirectory = WriteAssets(harness, "{ not-json");
        LoadAssets(harness, assetDirectory);
        MapDocumentViewModel document = harness.ViewModel;

        Assert.True(harness.Assets.Current.IsAvailable);
        Assert.False(document.IsTerrainAvailable);
        Assert.Equal(MapEditTool.Pencil, document.ActiveTool);
        document.Brush = new MapTileLayer(1, 10);
        Paint(harness, 0, 0);
        Assert.Equal(new MapTileLayer(1, 10), document.Session.Document[0, 0].GetLayer(0));

        harness.Window.FindControl<ToggleButton>("EraserTool")!.IsChecked = true;
        Paint(harness, 0, 0);
        Assert.Equal(default, document.Session.Document[0, 0].GetLayer(0));
        harness.Window.FindControl<ToggleButton>("PencilTool")!.IsChecked = true;
        document.Brush = new MapTileLayer(1, 11);
        Paint(harness, 1, 0);

        string mapPath = Path.Combine(harness.TempDirectory, "malformed-terrain.map");
        SaveMap(harness, mapPath);
        byte[] diskBytes = File.ReadAllBytes(mapPath);
        Assert.Equal(MapCodec.Encode(document.Session.Document), diskBytes);
        Assert.Equal(new MapTileLayer(1, 11), MapCodec.Decode(diskBytes)[1, 0].GetLayer(0));

        using MainWindowHarness reopened = MainWindowHarness.Create();
        LoadAssets(reopened, assetDirectory);
        OpenMap(reopened, mapPath);
        MapDocumentViewModel reopenedDocument = reopened.Workspace.ActiveDocument;
        Assert.Equal(new MapTileLayer(1, 11), reopenedDocument.Session.Document[1, 0].GetLayer(0));
        Assert.False(reopenedDocument.IsTerrainAvailable);
        Assert.False(reopenedDocument.Session.CanUndo);
    }

    [AvaloniaFact]
    public void SaveReplacementDuringPreview_CancelsThenNextGestureUsesOnlyNewCatalog()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        string assetDirectory = WriteAssets(harness, TerrainCatalogJson.Serialize(ReplaceableCatalog()));
        LoadAssets(harness, assetDirectory);
        SelectTerrain(harness);
        TerrainCatalogManager manager = OpenManager(harness);
        ReplaceVariants(manager, 20);
        byte[] before = MapCodec.Encode(harness.ViewModel.Session.Document);
        long version = harness.ViewModel.Session.HistoryVersion;

        Point first = TileCenter(harness, 0, 0);
        harness.Window.MouseDown(first, MouseButton.Left, RawInputModifiers.None);
        Assert.Equal(new MapTileLayer(1, 10), harness.ViewModel.Session.Document[0, 0].GetLayer(0));
        Assert.Equal(TerrainCatalogSaveStatus.Succeeded, manager.Save().Status);

        Assert.Equal(before, MapCodec.Encode(harness.ViewModel.Session.Document));
        Assert.Equal(version, harness.ViewModel.Session.HistoryVersion);
        Assert.False(harness.ViewModel.Session.HasActiveStroke);
        harness.Window.MouseUp(first, MouseButton.Left, RawInputModifiers.None);
        Assert.Equal(before, MapCodec.Encode(harness.ViewModel.Session.Document));

        Paint(harness, 1, 0);
        Assert.Equal(default, harness.ViewModel.Session.Document[0, 0].GetLayer(0));
        Assert.Equal(new MapTileLayer(1, 20), harness.ViewModel.Session.Document[1, 0].GetLayer(0));
        Assert.DoesNotContain(Enumerable.Range(0, harness.ViewModel.Session.Document.TileCount)
            .SelectMany(index => Enumerable.Range(0, MapDocument.LayerCount)
                .Select(layer => harness.ViewModel.Session.Document.GetTile(index).GetLayer(layer))),
            value => value == new MapTileLayer(1, 10));
    }

    private static TerrainCatalog ReplaceableCatalog()
    {
        TerrainCatalog source = TerrainTestData.Catalog(("Grass", 10));
        TerrainSetDefinition set = Assert.Single(source.Sets);
        TerrainSetDefinition replaceable = new(
            set.Id,
            set.DisplayName,
            set.Status,
            set.Topology,
            set.Metrics,
            set.Masks,
            set.Members.Select(member => new TerrainMemberDefinition(member.Reference, TerrainMemberProvenance.ImageOnly)),
            set.Diagnostics);
        return new TerrainCatalog(
            source.SchemaVersion,
            source.GeneratorVersion,
            source.CorpusFingerprint,
            source.Settings,
            new[] { replaceable },
            source.Diagnostics);
    }

    private static string WriteAssets(MainWindowHarness harness, string terrainJson)
    {
        string directory = Path.Combine(harness.TempDirectory, $"terrain-e2e-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(directory, "sheets"));
        File.WriteAllText(Path.Combine(directory, "manifest.json"),
            "{\"tileSize\":32,\"sheets\":{\"1\":{\"10\":[0,0,32,32],\"11\":[32,0,32,32],\"20\":[64,0,32,32],\"21\":[96,0,32,32]}}}");
        File.WriteAllBytes(Path.Combine(directory, "sheets", "1.png"), AssetFixture.PngSheet.Create(128, 32));
        File.WriteAllText(Path.Combine(directory, "terrain-brushes.json"), terrainJson);
        return directory;
    }

    private static void LoadAssets(MainWindowHarness harness, string assetDirectory)
    {
        harness.Dialogs.AssetDirectoryPickResult = assetDirectory;
        harness.Window.FindControl<Button>("LoadAssetsButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Path.GetFullPath(assetDirectory), harness.Assets.Current.Cache.AssetDirectory);
    }

    private static void SelectTerrain(MainWindowHarness harness)
    {
        harness.Window.FindControl<ToggleButton>("TerrainPaletteTab")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Point point = harness.Window.TerrainPalette.TranslatePoint(new Point(20, 20), harness.Window)
            ?? throw new InvalidOperationException("Terrain palette is not attached.");
        harness.Window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        Assert.Equal(MapEditTool.Terrain, harness.Workspace.ActiveDocument.ActiveTool);
    }

    private static TerrainCatalogManager OpenManager(MainWindowHarness harness)
    {
        harness.Window.FindControl<MenuItem>("TerrainSetsCommand")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        return Assert.IsType<TerrainCatalogManager>(harness.Dialogs.LastTerrainCatalogManager);
    }

    private static void ReplaceVariants(TerrainCatalogManager manager, int graphic)
    {
        TerrainDraftKey key = Assert.Single(manager.ViewModel.Sets).Key;
        foreach (int mask in TerrainMasks.Required(TerrainTopology.FourWay))
        {
            Assert.True(manager.ViewModel.AddVariant(key, mask, new TerrainGraphicReference(1, graphic)).Succeeded);
            Assert.True(manager.ViewModel.RemoveVariant(key, mask, 0).Succeeded);
        }
    }

    private static Point TileCenter(MainWindowHarness harness, int x, int y)
        => harness.Window.Canvas.TranslatePoint(new Point(x * 32 + 16, y * 32 + 16), harness.Window)
           ?? throw new InvalidOperationException("Map canvas is not attached.");

    private static void Paint(MainWindowHarness harness, int x, int y)
    {
        Point point = TileCenter(harness, x, y);
        harness.Window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
    }

    private static void SaveMap(MainWindowHarness harness, string path)
    {
        harness.Dialogs.SavePickResult = path;
        harness.Window.FindControl<MenuItem>("SaveAsCommand")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(harness.Dialogs.Errors);
    }

    private static void OpenMap(MainWindowHarness harness, string path)
    {
        harness.Dialogs.OpenPickResult = path;
        harness.Window.FindControl<MenuItem>("OpenCommand")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(harness.Dialogs.Errors);
    }
}
