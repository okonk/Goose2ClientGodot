using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using MapEditor.App.Controls;
using MapEditor.App.Dialogs;
using MapEditor.App.Documents;
using MapEditor.App.Rendering;
using MapEditor.App.Settings;
using MapEditor.App.Tests.Fakes;
using MapEditor.App.Tests.Fixtures;
using MapEditor.App.ViewModels;
using MapEditor.Core;
using MapEditor.GameData.Connectivity;
using MapEditor.GameData.Replacement;
using MapEditor.GameData.Rows;
using MapEditor.GameData.Sync;
using MapEditor.Rendering;
using Xunit;

namespace MapEditor.App.Tests;

public class NpcPreviewCanvasTests
{
    private const int CanvasWidth = 300;
    private const int CanvasHeight = 200;
    private const int MapSize = 8;
    private const int Cell = 32;

    private static readonly MapReference Map10 = new(10, "Dungeon", "dungeon.bytes");

    // Mirror MapRenderer.MapRenderPalette; the palette is internal to MapEditor.Rendering.
    private static readonly Color NpcAnchorFill = Color.FromArgb(0x80, 0xFF, 0xA0, 0x40);
    private static readonly Color PlaceholderFill = Color.FromArgb(0xCC, 0xFF, 0x00, 0xFF);

    private static readonly string MapManifestJson = """
        { "tileSize": 32, "sheets": {
          "1": { "10": [0, 0, 32, 32], "11": [32, 0, 32, 32] },
          "1000": { "100": [0, 0, 32, 32], "200": [32, 0, 32, 32], "300": [64, 0, 32, 32] }
        } }
        """;

    private static readonly string AppearanceManifestJson = """
        { "version": 1, "parts": {
          "Body": { "1": { "noEquip": [1000, 100] }, "2": { "noEquip": [1000, 200] } },
          "Hair": { "5": { "noEquip": [1000, 300] } },
          "Eyes": {}, "Helm": {}, "Feet": {}, "Hand": {},
          "Legs": { "3": { "noEquip": [1000, 100] }, "4": { "noEquip": [1000, 200] } },
          "Chest": { "8": { "noEquip": [1000, 300] } }
        } }
        """;

    private static readonly NpcAppearance Npc1 = new(
        1, "Goose", 0, 1, new RgbaValue(200, 100, 50, 128), 0, 0, new RgbaValue(0, 0, 0, 0), NpcEquipmentParser.DefaultEquippedItems);
    private static readonly NpcAppearance Npc2 = new(
        2, "Duck", 0, 2, new RgbaValue(0, 0, 0, 0), 0, 5, new RgbaValue(0, 0, 0, 0), NpcEquipmentParser.DefaultEquippedItems);

    internal sealed record Harness(MapCanvas Canvas, MapDocumentViewModel ViewModel, Window Window, AssetContextController Assets, MapCanvas? Canvas2)
    {
        public MapDocumentViewModel? SecondDocument { get; init; }
    }

    [AvaloniaFact]
    public void PreviewMode_ComposesAGroupForEverySpawnOccurrenceAndSuppressesMarkers()
    {
        Harness harness = CreateHarness();
        MapDocumentViewModel viewModel = harness.ViewModel;
        viewModel.GameData!.AttachSession(Session(spawns: new[] { new NpcSpawnRow(1, 10, 1, 1), new NpcSpawnRow(2, 10, 1, 1) }));
        viewModel.GameData.PreviewMode = true;

        RecordingMapDrawTarget target = new();
        harness.Canvas.RenderMap(target);

        Assert.Equal(2, CountRectangles(target, NpcAnchorFill));
        Assert.Equal(4, target.Images.Count);
        Bitmap sheet1000 = ((AvaloniaSpriteSheetImage)harness.Assets.Current.Resolve(new SpriteReference(1000, 100)).Image!).Bitmap;
        Assert.Contains(target.Images, draw => ReferenceEquals(draw.Image, sheet1000) && draw.Source == new Rect(0, 0, 32, 32));
        Assert.Contains(target.Images, draw => ReferenceEquals(draw.Image, sheet1000) && draw.Source == new Rect(32, 0, 32, 32));
        Assert.Contains(target.Images, draw => ReferenceEquals(draw.Image, sheet1000) && draw.Source == new Rect(64, 0, 32, 32));
        Assert.Single(target.Images, draw => !ReferenceEquals(draw.Image, sheet1000) && draw.Source == new Rect(0, 0, 32, 32));
    }

    [AvaloniaFact]
    public void PreviewMode_RedrawsOnlyTheCurrentOccurrencesAfterEditsUndoRedoAndPull()
    {
        Harness harness = CreateHarness();
        MapDocumentViewModel viewModel = harness.ViewModel;
        viewModel.GameData!.AttachSession(Session(spawns: new[] { new NpcSpawnRow(1, 10, 1, 1) }));
        viewModel.GameData.PreviewMode = true;
        viewModel.GameData.SelectedNpcId = 2;

        AssertAnchorTiles(harness, new[] { (1, 1) });

        viewModel.AddSpawnAt(3, 2);
        AssertAnchorTiles(harness, new[] { (1, 1), (3, 2) });

        viewModel.MoveSpawnTo(1, 4, 0);
        AssertAnchorTiles(harness, new[] { (1, 1), (4, 0) });

        viewModel.SelectSpawn(1);
        viewModel.RemoveSelectedSpawn();
        AssertAnchorTiles(harness, new[] { (1, 1) });

        Assert.True(viewModel.Undo());
        AssertAnchorTiles(harness, new[] { (1, 1), (4, 0) });
        Assert.True(viewModel.Redo());
        AssertAnchorTiles(harness, new[] { (1, 1) });

        viewModel.SelectSpawn(0);
        viewModel.CommitSpawnNpc(2);
        Assert.Equal(2, viewModel.GameData.Session!.Edits.Spawns[0].NpcId);
        AssertAnchorTiles(harness, new[] { (1, 1) });

        viewModel.GameData.AttachSession(Session(spawns: new[] { new NpcSpawnRow(2, 10, 5, 5) }));
        AssertAnchorTiles(harness, new[] { (5, 5) });
    }

    [AvaloniaFact]
    public void PreviewToggle_DoesNotMutateRowsHistoryDirtySelectionOrClipboard()
    {
        Harness harness = CreateHarness();
        MapDocumentViewModel viewModel = harness.ViewModel;
        viewModel.GameData!.AttachSession(Session(spawns: new[] { new NpcSpawnRow(1, 10, 1, 1) }));
        viewModel.GameData.SelectedNpcId = 2;
        viewModel.AddSpawnAt(3, 2);
        NpcSpawnRow[] rows = viewModel.GameData.Session!.Edits.Spawns.ToArray();
        bool canUndo = viewModel.CanUndo;
        bool canRedo = viewModel.CanRedo;
        bool dirty = viewModel.IsDirty;
        bool canSave = viewModel.CanSave;
        int? selectedSpawn = viewModel.GameData.SelectedSpawn;
        TileClipboard? clipboard = viewModel.Clipboard;

        viewModel.GameData.PreviewMode = true;
        harness.Canvas.RenderMap(new RecordingMapDrawTarget());
        viewModel.GameData.PreviewMode = false;
        harness.Canvas.RenderMap(new RecordingMapDrawTarget());

        Assert.Equal(rows, viewModel.GameData.Session.Edits.Spawns.ToArray());
        Assert.Equal(canUndo, viewModel.CanUndo);
        Assert.Equal(canRedo, viewModel.CanRedo);
        Assert.Equal(dirty, viewModel.IsDirty);
        Assert.Equal(canSave, viewModel.CanSave);
        Assert.Equal(selectedSpawn, viewModel.GameData.SelectedSpawn);
        Assert.Same(clipboard, viewModel.Clipboard);
        Assert.False(viewModel.GameData.PreviewMode);
    }

    [AvaloniaFact]
    public void UnknownNpc_PreviewShowsPlaceholderAndAnchorAndStaysClickable()
    {
        Harness harness = CreateHarness();
        MapDocumentViewModel viewModel = harness.ViewModel;
        viewModel.GameData!.AttachSession(Session(spawns: new[] { new NpcSpawnRow(999, 10, 1, 1) }));
        viewModel.GameData.PreviewMode = true;
        viewModel.GameData.ActiveTool = GameDataTool.Spawn;

        RecordingMapDrawTarget target = new();
        harness.Canvas.RenderMap(target);
        Assert.Equal(1, CountRectangles(target, NpcAnchorFill));
        Assert.Equal(1, CountRectangles(target, PlaceholderFill));
        Assert.Empty(target.Images);

        Point tile = Point(1, 1);
        harness.Window.MouseDown(tile, MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseUp(tile, MouseButton.Left, RawInputModifiers.None);
        Assert.Equal(0, viewModel.GameData.SelectedSpawn);
    }

    [AvaloniaFact]
    public void MalformedEquipment_AnchorCarriesTheDiagnosticAndStaysClickable()
    {
        Harness harness = CreateHarness();
        MapDocumentViewModel viewModel = harness.ViewModel;
        NpcAppearance broken = Npc1 with { EquippedItems = "garbage" };
        viewModel.GameData!.AttachSession(Session(npcs: new Dictionary<int, NpcAppearance> { [1] = broken }, spawns: new[] { new NpcSpawnRow(1, 10, 1, 1) }));
        viewModel.GameData.PreviewMode = true;
        viewModel.GameData.ActiveTool = GameDataTool.Spawn;

        RecordingMapDrawTarget target = new();
        harness.Canvas.RenderMap(target);
        Assert.Equal(1, CountRectangles(target, NpcAnchorFill));
        Assert.Equal(2, target.Images.Count);

        Point tile = Point(1, 1);
        harness.Window.MouseDown(tile, MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseUp(tile, MouseButton.Left, RawInputModifiers.None);
        Assert.Equal(0, viewModel.GameData.SelectedSpawn);
    }

    [AvaloniaFact]
    public void Names_RenderOnMarkerBoxesByDefault_AndHideWhenToggledOff()
    {
        Harness harness = CreateHarness();
        MapDocumentViewModel viewModel = harness.ViewModel;
        viewModel.GameData!.AttachSession(Session(
            spawns: new[] { new NpcSpawnRow(1, 10, 1, 1), new NpcSpawnRow(999, 10, 3, 1) },
            warps: new[] { new WarpRow(10, 2, 2, 10, 5, 6) }));

        RecordingMapDrawTarget target = new();
        harness.Canvas.RenderMap(target);

        Assert.Equal(3, target.Texts.Count);
        RecordingMapDrawTarget.TextDraw goose = target.Texts.Single(text => text.Text == "Goose");
        Assert.Equal(new Point(48, 48), goose.Center);
        Assert.Equal(11.2, goose.FontSize);
        Assert.Equal("999", target.Texts.Single(text => text.Text != "Goose" && text.Text != "Dungeon").Text);
        RecordingMapDrawTarget.TextDraw warp = target.Texts.Single(text => text.Text == "Dungeon");
        Assert.Equal(new Point(80, 80), warp.Center);

        viewModel.GameData.ShowNames = false;
        RecordingMapDrawTarget hidden = new();
        harness.Canvas.RenderMap(hidden);
        Assert.Empty(hidden.Texts);
    }

    [AvaloniaFact]
    public void Names_RenderAboveTheHeadInPreviewMode()
    {
        Harness harness = CreateHarness();
        MapDocumentViewModel viewModel = harness.ViewModel;
        viewModel.GameData!.AttachSession(Session(spawns: new[] { new NpcSpawnRow(1, 10, 1, 1) }));
        viewModel.GameData.PreviewMode = true;

        RecordingMapDrawTarget target = new();
        harness.Canvas.RenderMap(target);

        RecordingMapDrawTarget.TextDraw text = target.Texts.Single();
        Assert.Equal("Goose", text.Text);
        Assert.Equal(48, text.Center.X);
        Assert.True(text.Center.Y < 32);
        Assert.Equal(11.2, text.FontSize);

        viewModel.GameData.ShowNames = false;
        RecordingMapDrawTarget hidden = new();
        harness.Canvas.RenderMap(hidden);
        Assert.Empty(hidden.Texts);
    }

    [AvaloniaFact]
    public void AllAssetsMissing_FallsBackToNormalMarkersAndStaysClickable()
    {
        Harness harness = CreateHarness(withAppearanceSidecar: false);
        MapDocumentViewModel viewModel = harness.ViewModel;
        viewModel.GameData!.AttachSession(Session(spawns: new[] { new NpcSpawnRow(1, 10, 1, 1) }));
        viewModel.GameData.PreviewMode = true;
        viewModel.GameData.ActiveTool = GameDataTool.Spawn;

        RecordingMapDrawTarget target = new();
        harness.Canvas.RenderMap(target);
        Assert.Equal(1, CountRectangles(target, AvaloniaMapDrawSink.SpawnMarkerFill));
        Assert.Empty(target.Images);

        Point tile = Point(1, 1);
        harness.Window.MouseDown(tile, MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseUp(tile, MouseButton.Left, RawInputModifiers.None);
        Assert.Equal(0, viewModel.GameData.SelectedSpawn);
    }

    [AvaloniaFact]
    public void MissingPart_DrawsTheRemainingArtAPlaceholderAndTheAnchor()
    {
        Harness harness = CreateHarness();
        MapDocumentViewModel viewModel = harness.ViewModel;
        NpcAppearance missingHair = Npc1 with { HairId = 999 };
        viewModel.GameData!.AttachSession(Session(npcs: new Dictionary<int, NpcAppearance> { [1] = missingHair }, spawns: new[] { new NpcSpawnRow(1, 10, 1, 1) }));
        viewModel.GameData.PreviewMode = true;

        RecordingMapDrawTarget target = new();
        harness.Canvas.RenderMap(target);

        Assert.Equal(1, CountRectangles(target, NpcAnchorFill));
        Assert.Equal(1, CountRectangles(target, PlaceholderFill));
        Assert.Equal(2, target.Images.Count);
        Assert.All(target.Images, draw => Assert.Equal(new Rect(0, 0, 32, 32), draw.Source));
    }

    [AvaloniaFact]
    public void PerTab_PreferenceAndEffectiveRenderingAreIndependentWhileSharingOneCatalog()
    {
        Harness harness = CreateHarness(withSecondDocument: true);
        MapDocumentViewModel first = harness.ViewModel;
        MapDocumentViewModel second = harness.SecondDocument!;
        first.GameData!.AttachSession(Session(spawns: new[] { new NpcSpawnRow(1, 10, 1, 1) }));
        second.GameData!.AttachSession(Session(spawns: new[] { new NpcSpawnRow(2, 10, 1, 1) }));
        first.GameData.PreviewMode = true;

        RecordingMapDrawTarget firstTarget = new();
        harness.Canvas.RenderMap(firstTarget);
        Assert.Equal(1, CountRectangles(firstTarget, NpcAnchorFill));

        RecordingMapDrawTarget secondTarget = new();
        harness.Canvas2!.RenderMap(secondTarget);
        Assert.Equal(1, CountRectangles(secondTarget, AvaloniaMapDrawSink.SpawnMarkerFill));

        second.GameData.PreviewMode = true;
        RecordingMapDrawTarget secondPreview = new();
        harness.Canvas2.RenderMap(secondPreview);
        Assert.Equal(1, CountRectangles(secondPreview, NpcAnchorFill));
        Assert.True(first.GameData.PreviewMode);
        Assert.True(second.GameData.PreviewMode);
        Assert.NotNull(harness.Assets.Current.Appearance);
    }

    [AvaloniaFact]
    public async Task Smoke_HermeticAssets_RenderTintedSortedPreviewsToggleBackAndSelectEachSpawn()
    {
        string root = Directory.CreateTempSubdirectory("map-editor-npc-smoke-").FullName;
        try
        {
            string assetDirectory = WriteSmokeAssets(root);
            FakeEditorDialogs dialogs = new();
            var workspace = new WorkspaceViewModel(dialogs, new MapFileStore());
            dialogs.NewMapResult = new NewMapRequest(MapSize, MapSize);
            await workspace.NewAsync();
            MapDocumentViewModel viewModel = workspace.ActiveDocument;
            AssetContextController assets = new(workspace, new AppSettingsStore(Path.Combine(root, "settings.json")));
            Assert.True(assets.TryOpen(assetDirectory));
            Assert.True(assets.Current.AppearanceAvailability.IsAvailable);

            viewModel.Session.Document.SetLayer(1, 1, 2, new MapTileLayer(1, 10));
            viewModel.GameData!.AttachSession(Session(spawns: new[] { new NpcSpawnRow(1, 10, 0, 0), new NpcSpawnRow(2, 10, 2, 1) }));
            MapCanvas canvas = CreateCanvas(viewModel, assets);
            Window window = CreateWindow(canvas);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Bitmap sheet1 = ((AvaloniaSpriteSheetImage)assets.Current.Resolve(new SpriteReference(1, 10)).Image!).Bitmap;
            Bitmap sheet1000 = ((AvaloniaSpriteSheetImage)assets.Current.Resolve(new SpriteReference(1000, 100)).Image!).Bitmap;

            viewModel.GameData.PreviewMode = true;
            RecordingMapDrawTarget preview = new();
            canvas.RenderMap(preview);

            RecordingMapDrawTarget.ImageDraw[] images = preview.Images.ToArray();
            Assert.Equal(5, images.Length);
            Assert.NotSame(sheet1000, images[0].Image);
            Assert.Equal(new Rect(0, 0, 32, 32), images[0].Source);
            Assert.Same(sheet1000, images[1].Image);
            Assert.Equal(new Rect(0, 0, 32, 32), images[1].Source);
            Assert.Same(sheet1, images[2].Image);
            Assert.Equal(new Rect(0, 0, 32, 32), images[2].Source);
            Assert.Same(sheet1000, images[3].Image);
            Assert.Equal(new Rect(32, 0, 32, 32), images[3].Source);
            Assert.Same(sheet1000, images[4].Image);
            Assert.Equal(new Rect(64, 0, 32, 32), images[4].Source);
            Assert.Equal(2, CountRectangles(preview, NpcAnchorFill));

            viewModel.GameData.PreviewMode = false;
            RecordingMapDrawTarget markers = new();
            canvas.RenderMap(markers);
            Assert.Equal(2, CountRectangles(markers, AvaloniaMapDrawSink.SpawnMarkerFill));
            Assert.Single(markers.Images);
            Assert.Same(sheet1, markers.Images[0].Image);
            Assert.Equal(new Rect(0, 0, 32, 32), markers.Images[0].Source);

            viewModel.GameData.ActiveTool = GameDataTool.Spawn;
            Point firstTile = Point(0, 0);
            window.MouseDown(firstTile, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(firstTile, MouseButton.Left, RawInputModifiers.None);
            Assert.Equal(0, viewModel.GameData.SelectedSpawn);

            Point secondTile = Point(2, 1);
            window.MouseDown(secondTile, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(secondTile, MouseButton.Left, RawInputModifiers.None);
            Assert.Equal(1, viewModel.GameData.SelectedSpawn);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    internal static string WriteSmokeAssets(string root)
    {
        string assetDirectory = Path.Combine(root, "assets");
        Directory.CreateDirectory(Path.Combine(assetDirectory, "sheets"));
        File.WriteAllText(Path.Combine(assetDirectory, "manifest.json"), MapManifestJson);
        File.WriteAllText(Path.Combine(assetDirectory, "appearance-manifest.json"), AppearanceManifestJson);
        File.WriteAllBytes(Path.Combine(assetDirectory, "sheets", "1.png"), AssetFixture.PngSheet.Create(64, 64));
        File.WriteAllBytes(Path.Combine(assetDirectory, "sheets", "1000.png"), AssetFixture.PngSheet.Create(96, 64));
        return assetDirectory;
    }

    private static MapCanvas CreateCanvas(MapDocumentViewModel viewModel, AssetContextController assets)
        => new(viewModel, assets)
        {
            Width = CanvasWidth,
            Height = CanvasHeight,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };

    private static Window CreateWindow(MapCanvas canvas)
    {
        Panel host = new() { Children = { canvas } };
        return new Window { Content = host, Width = 800, Height = 600 };
    }

    private static void AssertAnchorTiles(Harness harness, (int X, int Y)[] expected)
    {
        RecordingMapDrawTarget target = new();
        harness.Canvas.RenderMap(target);
        var actual = target.Rectangles
            .Where(rect => rect.Fill is SolidColorBrush brush && brush.Color == NpcAnchorFill)
            .Select(rect => (
                X: (int)(rect.Bounds.Left / Cell),
                Y: (int)(rect.Bounds.Top / Cell)))
            .OrderBy(tile => tile.Y).ThenBy(tile => tile.X)
            .ToArray();
        Assert.Equal(expected.OrderBy(tile => tile.Y).ThenBy(tile => tile.X).ToArray(), actual);
    }

    private static int CountRectangles(RecordingMapDrawTarget target, Color fill)
        => target.Rectangles.Count(rect => rect.Fill is SolidColorBrush brush && brush.Color == fill);

    private static Point Point(int tileX, int tileY)
        => new(tileX * Cell + Cell / 2, tileY * Cell + Cell / 2);

    private static GameDataSyncSession Session(
        Dictionary<int, NpcAppearance>? npcs = null,
        NpcSpawnRow[]? spawns = null,
        WarpRow[]? warps = null)
    {
        int spawnRow = 2;
        int warpRow = 2;
        var data = new RemoteGameData(
            new[] { Map10 },
            npcs ?? new Dictionary<int, NpcAppearance> { [1] = Npc1, [2] = Npc2 },
            (spawns ?? Array.Empty<NpcSpawnRow>()).Select(row => new RemoteRow<NpcSpawnRow>(spawnRow++, row)).ToList(),
            (warps ?? Array.Empty<WarpRow>()).Select(row => new RemoteRow<WarpRow>(warpRow++, row)).ToList());
        return new GameDataSyncSession("sheet", 10, data);
    }

     internal static Harness CreateHarness(bool withAppearanceSidecar = true, bool withSecondDocument = false)
    {
        string root = Directory.CreateTempSubdirectory("map-editor-npc-preview-").FullName;
        string assetDirectory = WriteSmokeAssets(root);
        if (!withAppearanceSidecar)
        {
            File.Delete(Path.Combine(assetDirectory, "appearance-manifest.json"));
        }

        FakeEditorDialogs dialogs = new();
        var workspace = new WorkspaceViewModel(dialogs, new MapFileStore());
        dialogs.NewMapResult = new NewMapRequest(MapSize, MapSize);
        workspace.NewAsync().GetAwaiter().GetResult();
        MapDocumentViewModel viewModel = workspace.ActiveDocument;
        AssetContextController assets = new(workspace, new AppSettingsStore(Path.Combine(root, "settings.json")));
        Assert.True(assets.TryOpen(assetDirectory));
        MapCanvas canvas = CreateCanvas(viewModel, assets);
        Window window = CreateWindow(canvas);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        MapDocumentViewModel? second = null;
        MapCanvas? canvas2 = null;
        if (withSecondDocument)
        {
            workspace.NewAsync().GetAwaiter().GetResult();
            second = workspace.ActiveDocument;
            canvas2 = CreateCanvas(second, assets);
            Window window2 = CreateWindow(canvas2);
            window2.Show();
            Dispatcher.UIThread.RunJobs();
        }

        return new Harness(canvas, viewModel, window, assets, canvas2)
        {
            SecondDocument = second
        };
    }
}
