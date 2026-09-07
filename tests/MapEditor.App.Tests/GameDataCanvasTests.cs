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
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MapEditor.App.Controls;
using MapEditor.App.Dialogs;
using MapEditor.App.Documents;
using MapEditor.App.Rendering;
using MapEditor.App.Settings;
using MapEditor.App.Tests.Fakes;
using MapEditor.App.ViewModels;
using MapEditor.Core;
using MapEditor.GameData.Connectivity;
using MapEditor.GameData.Replacement;
using MapEditor.GameData.Rows;
using MapEditor.GameData.Sync;
using Xunit;

namespace MapEditor.App.Tests;

public class GameDataCanvasTests
{
    private const int CanvasWidth = 300;
    private const int CanvasHeight = 200;
    private const int MapSize = 4;
    private const int Cell = 32;

    private static readonly MapReference Map10 = new(10, "Dungeon", "dungeon.bytes");
    private static readonly MapReference Map20 = new(20, "Cave", "cave.bytes");
    private static readonly NpcAppearance Npc1 = new(1, "Goose", 0, 0, new RgbaValue(255, 255, 255, 255), 0, 0, new RgbaValue(255, 255, 255, 255), string.Empty);
    private static readonly NpcAppearance Npc2 = new(2, "Duck", 0, 0, new RgbaValue(255, 255, 255, 255), 0, 0, new RgbaValue(255, 255, 255, 255), string.Empty);

    private sealed record Harness(MapCanvas Canvas, MapDocumentViewModel ViewModel, Window Window, List<ErrorPresentation> Errors);

    [AvaloniaFact]
    public async Task SpawnTool_ClickEmptyTile_AddsSpawnWithSelectedNpcAndSelectsIt()
    {
        Harness harness = CreateHarness();
        MapDocumentViewModel viewModel = harness.ViewModel;
        viewModel.GameData!.AttachSession(Session(spawns: new[] { new NpcSpawnRow(1, 10, 0, 0) }));
        viewModel.GameData.ActiveTool = GameDataTool.Spawn;
        viewModel.GameData.SelectedNpcId = 2;

        Point tile = Point(2, 2);
        harness.Window.MouseDown(tile, MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseUp(tile, MouseButton.Left, RawInputModifiers.None);

        var spawns = viewModel.GameData.Session!.Edits.Spawns;
        Assert.Equal(2, spawns.Count);
        Assert.Equal(new NpcSpawnRow(2, 10, 2, 2), spawns[1]);
        Assert.Equal(1, viewModel.GameData.SelectedSpawn);
        Assert.True(viewModel.Timeline.CanUndo);
        Assert.True(viewModel.Undo());
        Assert.Single(viewModel.GameData.Session.Edits.Spawns);
        Assert.Null(viewModel.GameData.SelectedSpawn);
    }

    [AvaloniaFact]
    public async Task SpawnTool_ClickEmptyTile_WithoutSelectedNpc_SurfacesErrorWithoutMutation()
    {
        Harness harness = CreateHarness();
        MapDocumentViewModel viewModel = harness.ViewModel;
        viewModel.GameData!.AttachSession(Session(spawns: new[] { new NpcSpawnRow(1, 10, 0, 0) }));
        viewModel.GameData.ActiveTool = GameDataTool.Spawn;

        Point tile = Point(2, 2);
        harness.Window.MouseDown(tile, MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseUp(tile, MouseButton.Left, RawInputModifiers.None);

        Assert.Single(viewModel.GameData.Session!.Edits.Spawns);
        Assert.Null(viewModel.GameData.SelectedSpawn);
        var error = Assert.Single(harness.Errors);
        Assert.Contains("Select an NPC", error.Message);
        Assert.False(viewModel.Timeline.CanUndo);
    }

    [AvaloniaFact]
    public void ActiveTool_Spawn_ShowsSpawnOverlayAndHidesWarpOverlay()
    {
        Harness harness = CreateHarness();
        DocumentGameDataState gameData = harness.ViewModel.GameData!;
        gameData.ShowSpawnOverlay = false;
        gameData.ShowWarpOverlay = true;

        gameData.ActiveTool = GameDataTool.Spawn;

        Assert.True(gameData.ShowSpawnOverlay);
        Assert.False(gameData.ShowWarpOverlay);
    }

    [AvaloniaFact]
    public void ActiveTool_Warp_ShowsWarpOverlayAndHidesSpawnOverlay()
    {
        Harness harness = CreateHarness();
        DocumentGameDataState gameData = harness.ViewModel.GameData!;
        gameData.ShowSpawnOverlay = true;
        gameData.ShowWarpOverlay = false;

        gameData.ActiveTool = GameDataTool.Warp;

        Assert.False(gameData.ShowSpawnOverlay);
        Assert.True(gameData.ShowWarpOverlay);
    }

    [AvaloniaFact]
    public async Task SpawnTool_ClickOccupiedTile_SelectsWithoutAdding()
    {
        Harness harness = CreateHarness();
        MapDocumentViewModel viewModel = harness.ViewModel;
        viewModel.GameData!.AttachSession(Session(spawns: new[] { new NpcSpawnRow(1, 10, 0, 0), new NpcSpawnRow(2, 10, 2, 2) }));
        viewModel.GameData.ActiveTool = GameDataTool.Spawn;

        Point tile = Point(0, 0);
        harness.Window.MouseDown(tile, MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseUp(tile, MouseButton.Left, RawInputModifiers.None);

        Assert.Equal(0, viewModel.GameData.SelectedSpawn);
        Assert.Equal(2, viewModel.GameData.Session!.Edits.Spawns.Count);
        Assert.False(viewModel.Timeline.CanUndo);
    }

    [AvaloniaFact]
    public async Task SpawnTool_DuplicateSpawnClick_SelectsIndexZero()
    {
        Harness harness = CreateHarness();
        MapDocumentViewModel viewModel = harness.ViewModel;
        viewModel.GameData!.AttachSession(Session(spawns: new[] { new NpcSpawnRow(1, 10, 1, 1), new NpcSpawnRow(2, 10, 1, 1) }));
        viewModel.GameData.ActiveTool = GameDataTool.Spawn;

        Point tile = Point(1, 1);
        harness.Window.MouseDown(tile, MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseUp(tile, MouseButton.Left, RawInputModifiers.None);

        Assert.Equal(0, viewModel.GameData.SelectedSpawn);
        Assert.Equal(2, viewModel.GameData.Session!.Edits.Spawns.Count);
        Assert.False(viewModel.Timeline.CanUndo);
    }

    [AvaloniaFact]
    public async Task DragSpawnAcrossTiles_ReleaseCommitsOneMoveCommand()
    {
        Harness harness = CreateHarness();
        MapDocumentViewModel viewModel = harness.ViewModel;
        viewModel.GameData!.AttachSession(Session(spawns: new[] { new NpcSpawnRow(1, 10, 0, 0) }));
        viewModel.GameData.ActiveTool = GameDataTool.Spawn;

        harness.Window.MouseDown(Point(0, 0), MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseMove(Point(1, 0), RawInputModifiers.None);
        harness.Window.MouseMove(Point(1, 1), RawInputModifiers.None);
        RecordingMapDrawTarget preview = new();
        harness.Canvas.RenderMap(preview);
        Rect previewMarker = MarkerRectangle(preview, AvaloniaMapDrawSink.SpawnMarkerFill);
        Assert.Equal(new Rect(Cell, Cell, Cell, Cell), previewMarker);

        harness.Window.MouseUp(Point(1, 1), MouseButton.Left, RawInputModifiers.None);

        NpcSpawnRow spawn = viewModel.GameData.Session!.Edits.Spawns[0];
        Assert.Equal((1, 1), (spawn.MapX, spawn.MapY));
        Assert.True(viewModel.Timeline.CanUndo);
        Assert.True(viewModel.Undo());
        spawn = viewModel.GameData.Session.Edits.Spawns[0];
        Assert.Equal((0, 0), (spawn.MapX, spawn.MapY));
        Assert.False(viewModel.Timeline.CanUndo);
    }

    [AvaloniaFact]
    public async Task DragSpawnOutsideAndReenter_CommitsToLastValidTileAsOneCommand()
    {
        Harness harness = CreateHarness();
        MapDocumentViewModel viewModel = harness.ViewModel;
        viewModel.GameData!.AttachSession(Session(spawns: new[] { new NpcSpawnRow(1, 10, 0, 0) }));
        viewModel.GameData.ActiveTool = GameDataTool.Spawn;

        harness.Window.MouseDown(Point(0, 0), MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseMove(Point(2, 0), RawInputModifiers.None);
        harness.Window.MouseMove(new Point(8 * Cell, Cell / 2), RawInputModifiers.None);
        harness.Window.MouseMove(Point(3, 0), RawInputModifiers.None);
        harness.Window.MouseUp(Point(3, 0), MouseButton.Left, RawInputModifiers.None);

        NpcSpawnRow spawn = viewModel.GameData.Session!.Edits.Spawns[0];
        Assert.Equal((3, 0), (spawn.MapX, spawn.MapY));
        Assert.True(viewModel.Timeline.CanUndo);
        Assert.True(viewModel.Undo());
        spawn = viewModel.GameData.Session.Edits.Spawns[0];
        Assert.Equal((0, 0), (spawn.MapX, spawn.MapY));
        Assert.False(viewModel.Timeline.CanUndo);
    }

    [AvaloniaFact]
    public async Task DragSpawnEscape_CancelsWithoutHistory()
    {
        Harness harness = CreateHarness();
        MapDocumentViewModel viewModel = harness.ViewModel;
        viewModel.GameData!.AttachSession(Session(spawns: new[] { new NpcSpawnRow(1, 10, 0, 0) }));
        viewModel.GameData.ActiveTool = GameDataTool.Spawn;

        harness.Window.MouseDown(Point(0, 0), MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseMove(Point(1, 1), RawInputModifiers.None);
        harness.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        harness.Window.MouseUp(Point(1, 1), MouseButton.Left, RawInputModifiers.None);

        NpcSpawnRow spawn = viewModel.GameData.Session!.Edits.Spawns[0];
        Assert.Equal((0, 0), (spawn.MapX, spawn.MapY));
        Assert.False(viewModel.Timeline.CanUndo);
    }

    [AvaloniaFact]
    public async Task DragSpawnCaptureLost_CancelsWithoutHistory()
    {
        Harness harness = CreateHarness();
        MapDocumentViewModel viewModel = harness.ViewModel;
        viewModel.GameData!.AttachSession(Session(spawns: new[] { new NpcSpawnRow(1, 10, 0, 0) }));
        viewModel.GameData.ActiveTool = GameDataTool.Spawn;

        harness.Window.MouseDown(Point(0, 0), MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseMove(Point(1, 1), RawInputModifiers.None);
        harness.Window.Content = new Border();

        NpcSpawnRow spawn = viewModel.GameData.Session!.Edits.Spawns[0];
        Assert.Equal((0, 0), (spawn.MapX, spawn.MapY));
        Assert.False(viewModel.Timeline.CanUndo);
    }

    [AvaloniaFact]
    public async Task DragWarpAcrossTiles_ReleaseCommitsOneMoveCommand()
    {
        Harness harness = CreateHarness();
        MapDocumentViewModel viewModel = harness.ViewModel;
        viewModel.GameData!.AttachSession(Session(warps: new[] { new WarpRow(10, 0, 0, 20, 5, 6) }));
        viewModel.GameData.ActiveTool = GameDataTool.Warp;

        harness.Window.MouseDown(Point(0, 0), MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseMove(Point(2, 1), RawInputModifiers.None);
        harness.Window.MouseUp(Point(2, 1), MouseButton.Left, RawInputModifiers.None);

        WarpRow warp = viewModel.GameData.Session!.Edits.Warps[0];
        Assert.Equal((2, 1), (warp.MapX, warp.MapY));
        Assert.Equal((5, 6), (warp.WarpX, warp.WarpY));
        Assert.True(viewModel.Timeline.CanUndo);
        Assert.True(viewModel.Undo());
        warp = viewModel.GameData.Session.Edits.Warps[0];
        Assert.Equal((0, 0), (warp.MapX, warp.MapY));
        Assert.False(viewModel.Timeline.CanUndo);
    }

    [AvaloniaFact]
    public async Task WarpTool_ClickEmptyTile_CreatesWarpWithSelectedMapAndPendingDestination()
    {
        Harness harness = CreateHarness();
        MapDocumentViewModel viewModel = harness.ViewModel;
        viewModel.GameData!.AttachSession(Session());
        viewModel.GameData.ActiveTool = GameDataTool.Warp;
        viewModel.GameData.SelectedDestinationMapId = 20;
        viewModel.GameData.PendingDestinationX = 5;
        viewModel.GameData.PendingDestinationY = 6;

        Point tile = Point(1, 1);
        harness.Window.MouseDown(tile, MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseUp(tile, MouseButton.Left, RawInputModifiers.None);

        var warps = viewModel.GameData.Session!.Edits.Warps;
        Assert.Single(warps);
        Assert.Equal(new WarpRow(10, 1, 1, 20, 5, 6), warps[0]);
        Assert.Equal(0, viewModel.GameData.SelectedWarp);
    }

    [AvaloniaFact]
    public async Task WarpTool_ClickEmptyTile_WithoutDestinationMap_SurfacesErrorWithoutMutation()
    {
        Harness harness = CreateHarness();
        MapDocumentViewModel viewModel = harness.ViewModel;
        viewModel.GameData!.AttachSession(Session());
        viewModel.GameData.ActiveTool = GameDataTool.Warp;
        viewModel.GameData.PendingDestinationX = 5;
        viewModel.GameData.PendingDestinationY = 6;

        Point tile = Point(1, 1);
        harness.Window.MouseDown(tile, MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseUp(tile, MouseButton.Left, RawInputModifiers.None);

        Assert.Empty(viewModel.GameData.Session!.Edits.Warps);
        Assert.Null(viewModel.GameData.SelectedWarp);
        var error = Assert.Single(harness.Errors);
        Assert.Contains("destination map", error.Message);
    }

    [AvaloniaFact]
    public async Task WarpTool_DragOntoAnotherSource_SurfacesValidationWithoutMutation()
    {
        Harness harness = CreateHarness();
        MapDocumentViewModel viewModel = harness.ViewModel;
        viewModel.GameData!.AttachSession(Session(warps: new[]
        {
            new WarpRow(10, 0, 0, 20, 5, 6),
            new WarpRow(10, 2, 2, 20, 7, 8)
        }));
        viewModel.GameData.ActiveTool = GameDataTool.Warp;

        harness.Window.MouseDown(Point(0, 0), MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseMove(Point(2, 2), RawInputModifiers.None);
        harness.Window.MouseUp(Point(2, 2), MouseButton.Left, RawInputModifiers.None);

        WarpRow[] warps = viewModel.GameData.Session!.Edits.Warps.ToArray();
        Assert.Equal((0, 0), (warps[0].MapX, warps[0].MapY));
        Assert.Equal((2, 2), (warps[1].MapX, warps[1].MapY));
        Assert.Single(harness.Errors);
        Assert.False(viewModel.Timeline.CanUndo);
    }

    [AvaloniaFact]
    public async Task WarpTool_DuplicateSourceAdd_SurfacesValidationWithoutMutation()
    {
        Harness harness = CreateHarness();
        MapDocumentViewModel viewModel = harness.ViewModel;
        viewModel.GameData!.AttachSession(Session(warps: new[] { new WarpRow(10, 1, 1, 20, 5, 6) }));
        viewModel.GameData.SelectedDestinationMapId = 20;
        viewModel.GameData.PendingDestinationX = 7;
        viewModel.GameData.PendingDestinationY = 8;

        viewModel.AddWarpAt(1, 1);

        Assert.Single(viewModel.GameData.Session!.Edits.Warps);
        Assert.Single(harness.Errors);
        Assert.False(viewModel.Timeline.CanUndo);
    }

    [AvaloniaFact]
    public async Task WarpTool_ClickEmptyTile_WithoutDestinationCoordinates_SurfacesErrorWithoutMutation()
    {
        Harness harness = CreateHarness();
        MapDocumentViewModel viewModel = harness.ViewModel;
        viewModel.GameData!.AttachSession(Session());
        viewModel.GameData.ActiveTool = GameDataTool.Warp;
        viewModel.GameData.SelectedDestinationMapId = 20;

        viewModel.AddWarpAt(1, 1);

        Assert.Empty(viewModel.GameData.Session!.Edits.Warps);
        var error = Assert.Single(harness.Errors);
        Assert.Contains("destination coordinates", error.Message);
        Assert.False(viewModel.Timeline.CanUndo);
    }

    [AvaloniaFact]
    public async Task FinishInteraction_CommitsAPendingGameDataDragOnce()
    {
        Harness harness = CreateHarness();
        MapDocumentViewModel viewModel = harness.ViewModel;
        viewModel.GameData!.AttachSession(Session(spawns: new[] { new NpcSpawnRow(1, 10, 0, 0) }));
        viewModel.GameData.ActiveTool = GameDataTool.Spawn;

        harness.Window.MouseDown(Point(0, 0), MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseMove(Point(1, 1), RawInputModifiers.None);
        harness.Canvas.FinishInteraction(commit: true);
        harness.Canvas.FinishInteraction(commit: true);

        NpcSpawnRow spawn = viewModel.GameData.Session!.Edits.Spawns[0];
        Assert.Equal((1, 1), (spawn.MapX, spawn.MapY));
        Assert.True(viewModel.Timeline.CanUndo);
        Assert.True(viewModel.Undo());
        Assert.False(viewModel.Timeline.CanUndo);
    }

    [AvaloniaFact]
    public async Task FinishInteraction_Cancel_DiscardsAPendingGameDataDrag()
    {
        Harness harness = CreateHarness();
        MapDocumentViewModel viewModel = harness.ViewModel;
        viewModel.GameData!.AttachSession(Session(spawns: new[] { new NpcSpawnRow(1, 10, 0, 0) }));
        viewModel.GameData.ActiveTool = GameDataTool.Spawn;

        harness.Window.MouseDown(Point(0, 0), MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseMove(Point(1, 1), RawInputModifiers.None);
        harness.Canvas.FinishInteraction(commit: false);

        NpcSpawnRow spawn = viewModel.GameData.Session!.Edits.Spawns[0];
        Assert.Equal((0, 0), (spawn.MapX, spawn.MapY));
        Assert.False(viewModel.Timeline.CanUndo);
    }

    [AvaloniaFact]
    public async Task DragSpawnDeleteMidDrag_CancelsTheDragWithoutMovingAnotherOccurrence()
    {
        Harness harness = CreateHarness();
        MapDocumentViewModel viewModel = harness.ViewModel;
        viewModel.GameData!.AttachSession(Session(spawns: new[]
        {
            new NpcSpawnRow(1, 10, 0, 0),
            new NpcSpawnRow(2, 10, 2, 0)
        }));
        viewModel.GameData.ActiveTool = GameDataTool.Spawn;

        harness.Window.MouseDown(Point(0, 0), MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseMove(Point(1, 0), RawInputModifiers.None);
        harness.Window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);
        harness.Window.MouseUp(Point(1, 0), MouseButton.Left, RawInputModifiers.None);

        NpcSpawnRow[] spawns = viewModel.GameData.Session!.Edits.Spawns.ToArray();
        Assert.Single(spawns);
        Assert.Equal(new NpcSpawnRow(2, 10, 2, 0), spawns[0]);
        Assert.Null(viewModel.GameData.SelectedSpawn);
        Assert.True(viewModel.Timeline.CanUndo);
        Assert.True(viewModel.Undo());
        spawns = viewModel.GameData.Session.Edits.Spawns.ToArray();
        Assert.Equal(2, spawns.Length);
        Assert.Equal((2, 0), (spawns[1].MapX, spawns[1].MapY));
        Assert.False(viewModel.Timeline.CanUndo);
    }

    [AvaloniaFact]
    public async Task SpawnOverlayOff_HidesMarkersWithoutDisablingHitOrEditState()
    {
        Harness harness = CreateHarness();
        MapDocumentViewModel viewModel = harness.ViewModel;
        viewModel.GameData!.AttachSession(Session(spawns: new[] { new NpcSpawnRow(1, 10, 1, 1) }));
        viewModel.GameData.ActiveTool = GameDataTool.Spawn;

        viewModel.GameData.ShowSpawnOverlay = false;
        Assert.Equal(0, CountMarkerRectangles(harness, AvaloniaMapDrawSink.SpawnMarkerFill));

        Point tile = Point(1, 1);
        harness.Window.MouseDown(tile, MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseUp(tile, MouseButton.Left, RawInputModifiers.None);
        Assert.Equal(0, viewModel.GameData.SelectedSpawn);

        viewModel.GameData.ShowSpawnOverlay = true;
        Assert.Equal(1, CountMarkerRectangles(harness, AvaloniaMapDrawSink.SpawnMarkerFill));
    }

    [AvaloniaFact]
    public async Task WarpOverlayOff_HidesMarkersWithoutDisablingHitOrEditState()
    {
        Harness harness = CreateHarness();
        MapDocumentViewModel viewModel = harness.ViewModel;
        viewModel.GameData!.AttachSession(Session(warps: new[] { new WarpRow(10, 1, 1, 20, 5, 6) }));
        viewModel.GameData.ActiveTool = GameDataTool.Warp;

        viewModel.GameData.ShowWarpOverlay = false;
        Assert.Equal(0, CountMarkerRectangles(harness, AvaloniaMapDrawSink.WarpMarkerFill));

        Point tile = Point(1, 1);
        harness.Window.MouseDown(tile, MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseUp(tile, MouseButton.Left, RawInputModifiers.None);
        Assert.Equal(0, viewModel.GameData.SelectedWarp);

        viewModel.GameData.ShowWarpOverlay = true;
        Assert.Equal(1, CountMarkerRectangles(harness, AvaloniaMapDrawSink.WarpMarkerFill));
    }

    [AvaloniaFact]
    public async Task PreviewMode_StillShowsSelectableMarkers()
    {
        Harness harness = CreateHarness();
        MapDocumentViewModel viewModel = harness.ViewModel;
        viewModel.GameData!.AttachSession(Session(spawns: new[] { new NpcSpawnRow(1, 10, 1, 1) }));
        viewModel.GameData.ActiveTool = GameDataTool.Spawn;
        viewModel.GameData.PreviewMode = true;

        Assert.Equal(1, CountMarkerRectangles(harness, AvaloniaMapDrawSink.SpawnMarkerFill));

        Point tile = Point(1, 1);
        harness.Window.MouseDown(tile, MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseUp(tile, MouseButton.Left, RawInputModifiers.None);
        Assert.Equal(0, viewModel.GameData.SelectedSpawn);
    }

    private static int CountMarkerRectangles(Harness harness, Color fill)
    {
        RecordingMapDrawTarget target = new();
        harness.Canvas.RenderMap(target);
        return target.Rectangles.Count(rect => rect.Fill is SolidColorBrush brush && brush.Color == fill);
    }

    private static Rect MarkerRectangle(RecordingMapDrawTarget target, Color fill)
        => target.Rectangles.Single(rect => rect.Fill is SolidColorBrush brush && brush.Color == fill).Bounds;

    private static Point Point(int tileX, int tileY)
        => new(tileX * Cell + Cell / 2, tileY * Cell + Cell / 2);

    private static GameDataSyncSession Session(
        NpcSpawnRow[]? spawns = null,
        WarpRow[]? warps = null)
    {
        int spawnRow = 2;
        int warpRow = 2;
        var data = new RemoteGameData(
            new[] { Map10, Map20 },
            new Dictionary<int, NpcAppearance> { [1] = Npc1, [2] = Npc2 },
            (spawns ?? Array.Empty<NpcSpawnRow>()).Select(row => new RemoteRow<NpcSpawnRow>(spawnRow++, row)).ToList(),
            (warps ?? Array.Empty<WarpRow>()).Select(row => new RemoteRow<WarpRow>(warpRow++, row)).ToList());
        return new GameDataSyncSession("sheet", 10, data);
    }

    private static Harness CreateHarness()
    {
        FakeEditorDialogs dialogs = new();
        var workspace = new WorkspaceViewModel(dialogs, new MapFileStore());
        dialogs.NewMapResult = new NewMapRequest(MapSize, MapSize);
        workspace.NewAsync().GetAwaiter().GetResult();
        MapDocumentViewModel viewModel = workspace.ActiveDocument;
        string settingsPath = Path.Combine(Path.GetTempPath(), "map-editor-game-data-canvas-tests", "settings.json");
        AssetContextController assets = new(workspace, new AppSettingsStore(settingsPath));
        MapCanvas canvas = new(viewModel, assets)
        {
            Width = CanvasWidth,
            Height = CanvasHeight,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        Panel host = new() { Children = { canvas } };
        Window window = new() { Content = host, Width = 800, Height = 600 };
        window.AddHandler(InputElement.KeyDownEvent, (s, e) =>
        {
            if (e.Key == Key.Escape)
            {
                canvas.FinishInteraction(commit: false);
                e.Handled = true;
            }
            else if (e.Key == Key.Delete)
            {
                viewModel.RemoveSelectedGameData();
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);
        var errors = new List<ErrorPresentation>();
        viewModel.GameDataError += error => errors.Add(error);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return new Harness(canvas, viewModel, window, errors);
    }
}
