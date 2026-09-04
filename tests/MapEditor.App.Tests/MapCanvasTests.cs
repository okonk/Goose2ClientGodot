using System;
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
using MapEditor.Rendering;
using Xunit;

namespace MapEditor.App.Tests;

public class MapCanvasTests
{
    private const int CanvasWidth = 300;
    private const int CanvasHeight = 200;
    private const int MapSize = 4;
    private const int Cell = 32;

    private sealed record Harness(MapCanvas Canvas, MainWindowViewModel ViewModel, FakeEditorDialogs Dialogs, Window Window, AssetContextController Assets);

    [AvaloniaFact]
    public async Task LeftPressRelease_PaintsOneCellAsSingleUndoEntry()
    {
        Harness harness = await CreateSmallMapAsync();
        MapEditSession session = harness.ViewModel.Session;
        MapDocument document = session.Document;
        harness.ViewModel.Brush = new MapTileLayer(7, 42);

        harness.Window.MouseDown(new Point(Cell / 2, Cell / 2), MouseButton.Left, RawInputModifiers.None);
        Assert.True(session.HasActiveStroke);
        harness.Window.MouseUp(new Point(Cell / 2, Cell / 2), MouseButton.Left, RawInputModifiers.None);

        Assert.False(session.HasActiveStroke);
        Assert.Equal(new MapTileLayer(7, 42), document[0, 0].GetLayer(0));
        Assert.True(session.CanUndo);
        Assert.False(session.CanRedo);
        Assert.True(harness.ViewModel.Undo());
        Assert.Equal(new MapTileLayer(0, 0), document[0, 0].GetLayer(0));
    }

    [AvaloniaFact]
    public async Task SparseDrag_InterpolatesThroughPartTwoAsOneCommand()
    {
        Harness harness = await CreateSmallMapAsync();
        MapEditSession session = harness.ViewModel.Session;
        MapDocument document = session.Document;
        harness.ViewModel.Brush = new MapTileLayer(3, 9);

        harness.Window.MouseDown(new Point(Cell / 2, Cell / 2), MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseMove(new Point(3 * Cell + Cell / 2, Cell / 2), RawInputModifiers.None);
        harness.Window.MouseUp(new Point(3 * Cell + Cell / 2, Cell / 2), MouseButton.Left, RawInputModifiers.None);

        Assert.False(session.HasActiveStroke);
        for (int x = 0; x < 4; x++)
        {
            Assert.Equal(new MapTileLayer(3, 9), document[x, 0].GetLayer(0));
        }

        Assert.True(session.CanUndo);
        Assert.True(harness.ViewModel.Undo());
        for (int x = 0; x < 4; x++)
        {
            Assert.Equal(new MapTileLayer(0, 0), document[x, 0].GetLayer(0));
        }
    }

    [AvaloniaFact]
    public async Task ReleaseOutsideCanvas_CompletesStrokeOnce()
    {
        Harness harness = await CreateSmallMapAsync();
        MapEditSession session = harness.ViewModel.Session;
        MapDocument document = session.Document;
        harness.ViewModel.Brush = new MapTileLayer(1, 2);

        harness.Window.MouseDown(new Point(Cell / 2, Cell / 2), MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseMove(new Point(3 * Cell + Cell / 2, Cell / 2), RawInputModifiers.None);
        harness.Window.MouseUp(new Point(CanvasWidth + 100, CanvasHeight + 100), MouseButton.Left, RawInputModifiers.None);

        Assert.False(session.HasActiveStroke);
        for (int x = 0; x < 4; x++)
        {
            Assert.Equal(new MapTileLayer(1, 2), document[x, 0].GetLayer(0));
        }

        Assert.True(session.CanUndo);
        Assert.True(harness.ViewModel.Undo());
        for (int x = 0; x < 4; x++)
        {
            Assert.Equal(new MapTileLayer(0, 0), document[x, 0].GetLayer(0));
        }
    }

    [AvaloniaFact]
    public async Task DragOutsideThenReentry_InterpolatesFromLastValidCellAndNeverLeavesBounds()
    {
        Harness harness = await CreateSmallMapAsync();
        MapEditSession session = harness.ViewModel.Session;
        MapDocument document = session.Document;
        harness.ViewModel.Brush = new MapTileLayer(4, 6);

        harness.Window.MouseDown(new Point(Cell / 2, Cell / 2), MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseMove(new Point(8 * Cell, Cell / 2), RawInputModifiers.None);
        harness.Window.MouseMove(new Point(3 * Cell + Cell / 2, Cell / 2), RawInputModifiers.None);
        harness.Window.MouseUp(new Point(3 * Cell + Cell / 2, Cell / 2), MouseButton.Left, RawInputModifiers.None);

        Assert.False(session.HasActiveStroke);
        for (int x = 0; x < 4; x++)
        {
            Assert.Equal(new MapTileLayer(4, 6), document[x, 0].GetLayer(0));
        }

        for (int x = 0; x < MapSize; x++)
        {
            for (int y = 1; y < MapSize; y++)
            {
                Assert.Equal(new MapTileLayer(0, 0), document[x, y].GetLayer(0));
            }
        }
    }

    [AvaloniaFact]
    public async Task BlockedToggleLoopOverItself_TogglesCrossedCellOnce()
    {
        Harness harness = await CreateSmallMapAsync();
        MapEditSession session = harness.ViewModel.Session;
        MapDocument document = session.Document;
        harness.ViewModel.ActiveTool = MapEditTool.BlockedToggle;

        harness.Window.MouseDown(new Point(Cell / 2, Cell / 2), MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseMove(new Point(2 * Cell - Cell / 2, Cell / 2), RawInputModifiers.None);
        harness.Window.MouseMove(new Point(2 * Cell - Cell / 2, 2 * Cell - Cell / 2), RawInputModifiers.None);
        harness.Window.MouseMove(new Point(Cell / 2, 2 * Cell - Cell / 2), RawInputModifiers.None);
        harness.Window.MouseMove(new Point(Cell / 2, Cell / 2), RawInputModifiers.None);
        harness.Window.MouseUp(new Point(Cell / 2, Cell / 2), MouseButton.Left, RawInputModifiers.None);

        Assert.False(session.HasActiveStroke);
        Assert.True(document[0, 0].IsBlocked);
        Assert.True(document[1, 0].IsBlocked);
        Assert.True(document[1, 1].IsBlocked);
        Assert.True(document[0, 1].IsBlocked);
        Assert.True(session.CanUndo);
    }

    [AvaloniaFact]
    public async Task EyedropperPress_SelectsTileLayerWithoutEditing()
    {
        Harness harness = await CreateSmallMapAsync();
        MapEditSession session = harness.ViewModel.Session;
        MapDocument document = session.Document;
        document.SetLayer(1, 1, 2, new MapTileLayer(9, 9));
        harness.ViewModel.SelectedLayers = (byte)(1 << 2);
        harness.ViewModel.ActiveTool = MapEditTool.Eyedropper;

        harness.Window.MouseDown(new Point(2 * Cell - Cell / 2, 2 * Cell - Cell / 2), MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseUp(new Point(2 * Cell - Cell / 2, 2 * Cell - Cell / 2), MouseButton.Left, RawInputModifiers.None);

        Assert.Equal(new MapTileLayer(9, 9), harness.ViewModel.Brush);
        Assert.Equal(1, harness.ViewModel.SelectedX);
        Assert.Equal(1, harness.ViewModel.SelectedY);
        Assert.False(session.HasActiveStroke);
        Assert.False(session.CanUndo);
    }

    [AvaloniaFact]
    public async Task EscapeDuringStroke_CancelsAndRestores()
    {
        Harness harness = await CreateSmallMapAsync();
        MapEditSession session = harness.ViewModel.Session;
        MapDocument document = session.Document;
        harness.ViewModel.Brush = new MapTileLayer(5, 5);
        harness.Canvas.Focus();

        harness.Window.MouseDown(new Point(Cell / 2, Cell / 2), MouseButton.Left, RawInputModifiers.None);
        Assert.Equal(new MapTileLayer(5, 5), document[0, 0].GetLayer(0));
        harness.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

        Assert.False(session.HasActiveStroke);
        Assert.Equal(new MapTileLayer(0, 0), document[0, 0].GetLayer(0));
        Assert.False(session.CanUndo);
    }

    [AvaloniaFact]
    public async Task UnexpectedCaptureLoss_CompletesEffectiveStrokeOnce()
    {
        Harness harness = await CreateSmallMapAsync();
        MapEditSession session = harness.ViewModel.Session;
        MapDocument document = session.Document;
        harness.ViewModel.Brush = new MapTileLayer(8, 8);

        harness.Window.MouseDown(new Point(Cell / 2, Cell / 2), MouseButton.Left, RawInputModifiers.None);
        Assert.True(session.HasActiveStroke);
        harness.Window.Content = new Border();

        Assert.False(session.HasActiveStroke);
        Assert.Equal(new MapTileLayer(8, 8), document[0, 0].GetLayer(0));
        Assert.True(session.CanUndo);
    }

    [AvaloniaFact]
    public async Task MiddleDrag_PansWithoutEditing()
    {
        Harness harness = await CreateSmallMapAsync();
        MapEditSession session = harness.ViewModel.Session;
        byte[] before = MapCodec.Encode(session.Document);
        bool dirtyBefore = session.IsDirty;

        harness.Window.MouseDown(new Point(100, 100), MouseButton.Middle, RawInputModifiers.None);
        harness.Window.MouseMove(new Point(120, 110), RawInputModifiers.None);
        harness.Window.MouseUp(new Point(120, 110), MouseButton.Middle, RawInputModifiers.None);

        Assert.Equal(new RenderPoint(-20, -10), harness.Canvas.Viewport.WorldOrigin);
        Assert.Equal(before, MapCodec.Encode(session.Document));
        Assert.Equal(dirtyBefore, session.IsDirty);
        Assert.False(session.CanUndo);
        Assert.False(session.CanRedo);
    }

    [AvaloniaFact]
    public async Task SpacePlusLeftDrag_PansWithoutStartingStroke()
    {
        Harness harness = await CreateSmallMapAsync();
        MapEditSession session = harness.ViewModel.Session;
        MapDocument document = session.Document;
        harness.ViewModel.Brush = new MapTileLayer(2, 2);
        harness.Canvas.Focus();

        harness.Window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        harness.Window.MouseDown(new Point(100, 100), MouseButton.Left, RawInputModifiers.None);
        Assert.False(session.HasActiveStroke);
        harness.Window.MouseMove(new Point(130, 100), RawInputModifiers.None);
        harness.Window.MouseUp(new Point(130, 100), MouseButton.Left, RawInputModifiers.None);
        harness.Window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);

        Assert.Equal(new RenderPoint(-30, 0), harness.Canvas.Viewport.WorldOrigin);
        Assert.Equal(new MapTileLayer(0, 0), document[0, 0].GetLayer(0));
        Assert.False(session.HasActiveStroke);
    }

    [AvaloniaFact]
    public async Task WheelZoomsAtCursorAndClampsAtLimits()
    {
        Harness harness = await CreateSmallMapAsync();
        MapCanvas canvas = harness.Canvas;

        RenderPoint anchorWorld = canvas.Viewport.ScreenToWorld(new RenderPoint(100, 100));
        harness.Window.MouseWheel(new Point(100, 100), new Vector(0, 1), RawInputModifiers.None);
        Assert.Equal(MapZoom.Percent200, canvas.Viewport.Zoom);
        Assert.Equal(200, harness.ViewModel.ZoomPercent);
        Assert.Equal(anchorWorld, canvas.Viewport.ScreenToWorld(new RenderPoint(100, 100)));

        harness.Window.MouseWheel(new Point(100, 100), new Vector(0, -1), RawInputModifiers.None);
        Assert.Equal(MapZoom.Percent100, canvas.Viewport.Zoom);
        Assert.Equal(100, harness.ViewModel.ZoomPercent);

        for (int i = 0; i < 5; i++)
        {
            harness.Window.MouseWheel(new Point(100, 100), new Vector(0, 1), RawInputModifiers.None);
        }

        Assert.Equal(MapZoom.Percent400, canvas.Viewport.Zoom);
        Assert.Equal(400, harness.ViewModel.ZoomPercent);

        for (int i = 0; i < 8; i++)
        {
            harness.Window.MouseWheel(new Point(100, 100), new Vector(0, -1), RawInputModifiers.None);
        }

        Assert.Equal(MapZoom.Percent25, canvas.Viewport.Zoom);
        Assert.Equal(25, harness.ViewModel.ZoomPercent);
    }

    [AvaloniaFact]
    public async Task MidStrokeLayerAndBrushChange_AffectsOnlySubsequentStrokes()
    {
        Harness harness = await CreateSmallMapAsync();
        MapEditSession session = harness.ViewModel.Session;
        MapDocument document = session.Document;
        harness.ViewModel.Brush = new MapTileLayer(7, 42);

        harness.Window.MouseDown(new Point(Cell / 2, Cell / 2), MouseButton.Left, RawInputModifiers.None);
        Assert.True(session.HasActiveStroke);

        harness.ViewModel.SelectedLayers = (byte)(1 << 2);
        harness.ViewModel.Brush = new MapTileLayer(9, 9);

        harness.Window.MouseMove(new Point(2 * Cell - Cell / 2, Cell / 2), RawInputModifiers.None);
        harness.Window.MouseUp(new Point(2 * Cell - Cell / 2, Cell / 2), MouseButton.Left, RawInputModifiers.None);

        Assert.False(session.HasActiveStroke);
        Assert.Equal(new MapTileLayer(7, 42), document[0, 0].GetLayer(0));
        Assert.Equal(new MapTileLayer(7, 42), document[1, 0].GetLayer(0));
        Assert.Equal(new MapTileLayer(0, 0), document[0, 0].GetLayer(2));
        Assert.Equal(new MapTileLayer(0, 0), document[1, 0].GetLayer(2));

        harness.Window.MouseDown(new Point(Cell / 2, 2 * Cell - Cell / 2), MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseMove(new Point(2 * Cell - Cell / 2, 2 * Cell - Cell / 2), RawInputModifiers.None);
        harness.Window.MouseUp(new Point(2 * Cell - Cell / 2, 2 * Cell - Cell / 2), MouseButton.Left, RawInputModifiers.None);

        Assert.Equal(new MapTileLayer(9, 9), document[0, 1].GetLayer(2));
        Assert.Equal(new MapTileLayer(9, 9), document[1, 1].GetLayer(2));
        Assert.Equal(new MapTileLayer(0, 0), document[0, 1].GetLayer(0));
        Assert.Equal(new MapTileLayer(0, 0), document[1, 1].GetLayer(0));
    }

    [AvaloniaFact]
    public async Task RenderMapSinkFailure_PropagatesAndLeavesStateIntact()
    {
        Harness harness = await CreateSmallMapAsync();
        MapEditSession session = harness.ViewModel.Session;
        byte[] before = MapCodec.Encode(session.Document);
        bool dirtyBefore = session.IsDirty;

        harness.Window.MouseMove(new Point(2 * Cell - Cell / 2, 2 * Cell - Cell / 2), RawInputModifiers.None);
        harness.Window.MouseDown(new Point(2 * Cell - Cell / 2, 2 * Cell - Cell / 2), MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseUp(new Point(2 * Cell - Cell / 2, 2 * Cell - Cell / 2), MouseButton.Left, RawInputModifiers.None);
        Assert.Equal(1, harness.ViewModel.HoverX);
        Assert.Equal(1, harness.ViewModel.HoverY);
        Assert.Equal(1, harness.ViewModel.SelectedX);
        Assert.Equal(1, harness.ViewModel.SelectedY);

        Assert.Throws<InvalidOperationException>(() => harness.Canvas.RenderMap(new ThrowingMapDrawTarget()));

        Assert.Equal(before, MapCodec.Encode(session.Document));
        Assert.Equal(dirtyBefore, session.IsDirty);
        Assert.False(session.HasActiveStroke);
        Assert.False(session.CanUndo);
        Assert.False(session.CanRedo);
        Assert.Equal(1, harness.ViewModel.HoverX);
        Assert.Equal(1, harness.ViewModel.HoverY);
        Assert.Equal(1, harness.ViewModel.SelectedX);
        Assert.Equal(1, harness.ViewModel.SelectedY);

        RecordingMapDrawTarget healthy = new();
        harness.Canvas.RenderMap(healthy);
        Assert.Single(healthy.Clips);
    }

    [AvaloniaFact]
    public async Task PanAndZoom_NeverChangeDocumentHistoryOrDirty()
    {
        Harness harness = await CreateSmallMapAsync();
        MapEditSession session = harness.ViewModel.Session;
        byte[] before = MapCodec.Encode(session.Document);
        bool dirtyBefore = session.IsDirty;
        bool undoBefore = session.CanUndo;
        bool redoBefore = session.CanRedo;

        harness.Canvas.Focus();
        harness.Window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        harness.Window.MouseDown(new Point(50, 50), MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseMove(new Point(90, 70), RawInputModifiers.None);
        harness.Window.MouseUp(new Point(90, 70), MouseButton.Left, RawInputModifiers.None);
        harness.Window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
        harness.Window.MouseWheel(new Point(50, 50), new Vector(0, 1), RawInputModifiers.None);
        harness.Window.MouseWheel(new Point(50, 50), new Vector(0, -1), RawInputModifiers.None);

        harness.ViewModel.LayerVisibility = (byte)0b11110;
        harness.ViewModel.LayerVisibility = (byte)0b11111;
        harness.ViewModel.ShowGrid = false;
        harness.ViewModel.ShowGrid = true;
        harness.ViewModel.ShowBlocked = true;
        harness.ViewModel.ShowBlocked = false;

        Assert.Equal(before, MapCodec.Encode(session.Document));
        Assert.Equal(dirtyBefore, session.IsDirty);
        Assert.Equal(undoBefore, session.CanUndo);
        Assert.Equal(redoBefore, session.CanRedo);
    }

    [AvaloniaFact]
    public async Task MoveUpdatesHoverAndPressUpdatesSelection()
    {
        Harness harness = await CreateSmallMapAsync();
        MapEditSession session = harness.ViewModel.Session;

        harness.Window.MouseMove(new Point(2 * Cell - Cell / 2, 2 * Cell - Cell / 2), RawInputModifiers.None);
        Assert.Equal(1, harness.ViewModel.HoverX);
        Assert.Equal(1, harness.ViewModel.HoverY);

        harness.Window.MouseMove(new Point(8 * Cell, 8 * Cell), RawInputModifiers.None);
        Assert.Null(harness.ViewModel.HoverX);
        Assert.Null(harness.ViewModel.HoverY);

        harness.Window.MouseDown(new Point(2 * Cell - Cell / 2, 2 * Cell - Cell / 2), MouseButton.Left, RawInputModifiers.None);
        Assert.Equal(1, harness.ViewModel.SelectedX);
        Assert.Equal(1, harness.ViewModel.SelectedY);
        harness.Window.MouseUp(new Point(2 * Cell - Cell / 2, 2 * Cell - Cell / 2), MouseButton.Left, RawInputModifiers.None);
        Assert.False(session.HasActiveStroke);
    }

    [AvaloniaFact]
    public async Task SessionReplacement_ResetsViewportAndZoom()
    {
        Harness harness = await CreateSmallMapAsync();
        MapCanvas canvas = harness.Canvas;

        harness.Window.MouseWheel(new Point(50, 50), new Vector(0, 1), RawInputModifiers.None);
        harness.Window.MouseDown(new Point(50, 50), MouseButton.Middle, RawInputModifiers.None);
        harness.Window.MouseMove(new Point(80, 60), RawInputModifiers.None);
        harness.Window.MouseUp(new Point(80, 60), MouseButton.Middle, RawInputModifiers.None);
        Assert.Equal(MapZoom.Percent200, canvas.Viewport.Zoom);
        Assert.NotEqual(new RenderPoint(0, 0), canvas.Viewport.WorldOrigin);

        harness.Dialogs.NewMapResult = new NewMapRequest(8, 8);
        harness.Dialogs.DirtyResult = DirtyChoice.Discard;
        await harness.ViewModel.NewAsync();

        Assert.Equal(MapZoom.Percent100, canvas.Viewport.Zoom);
        Assert.Equal(new RenderPoint(0, 0), canvas.Viewport.WorldOrigin);
        Assert.Equal(100, harness.ViewModel.ZoomPercent);
        Assert.Null(harness.ViewModel.HoverX);
        Assert.Null(harness.ViewModel.HoverY);
        Assert.Null(harness.ViewModel.SelectedX);
        Assert.Null(harness.ViewModel.SelectedY);
        Assert.Equal(8, harness.ViewModel.MapWidth);
        Assert.Equal(8, harness.ViewModel.MapHeight);
    }

    [AvaloniaFact]
    public async Task FinishInteraction_CommitCompletesAndCancelRestores()
    {
        Harness harness = await CreateSmallMapAsync();
        MapEditSession session = harness.ViewModel.Session;
        MapDocument document = session.Document;
        harness.ViewModel.Brush = new MapTileLayer(6, 6);

        harness.Window.MouseDown(new Point(Cell / 2, Cell / 2), MouseButton.Left, RawInputModifiers.None);
        harness.Canvas.FinishInteraction(commit: true);
        Assert.False(session.HasActiveStroke);
        Assert.Equal(new MapTileLayer(6, 6), document[0, 0].GetLayer(0));
        Assert.True(session.CanUndo);

        harness.Window.MouseDown(new Point(2 * Cell - Cell / 2, Cell / 2), MouseButton.Left, RawInputModifiers.None);
        Assert.Equal(new MapTileLayer(6, 6), document[1, 0].GetLayer(0));
        harness.Canvas.FinishInteraction(commit: false);
        Assert.False(session.HasActiveStroke);
        Assert.Equal(new MapTileLayer(0, 0), document[1, 0].GetLayer(0));
    }

    [AvaloniaFact]
    public async Task HoverMoves_InvalidateOnlyWhenHoveredTileChanges()
    {
        Harness harness = await CreateSmallMapAsync();
        MapCanvas canvas = harness.Canvas;

        int before = canvas.InvalidationCount;
        harness.Window.MouseMove(new Point(Cell / 2, Cell / 2), RawInputModifiers.None);
        Assert.Equal(before + 1, canvas.InvalidationCount);

        harness.Window.MouseMove(new Point(2 * Cell - Cell / 2, Cell / 2), RawInputModifiers.None);
        Assert.Equal(before + 2, canvas.InvalidationCount);

        harness.Window.MouseMove(new Point(2 * Cell - Cell / 4, Cell / 4), RawInputModifiers.None);
        Assert.Equal(before + 2, canvas.InvalidationCount);

        harness.Window.MouseMove(new Point(8 * Cell, 8 * Cell), RawInputModifiers.None);
        Assert.Equal(before + 3, canvas.InvalidationCount);
        Assert.Null(harness.ViewModel.HoverX);
        Assert.Null(harness.ViewModel.HoverY);
    }

    [AvaloniaFact]
    public async Task StrokeActive_ReportsGestureActiveUntilRelease()
    {
        Harness harness = await CreateSmallMapAsync();
        MapCanvas canvas = harness.Canvas;
        Assert.False(canvas.IsGestureActive);

        harness.Window.MouseDown(new Point(Cell / 2, Cell / 2), MouseButton.Left, RawInputModifiers.None);
        Assert.True(canvas.IsGestureActive);

        harness.Window.MouseUp(new Point(Cell / 2, Cell / 2), MouseButton.Left, RawInputModifiers.None);
        Assert.False(canvas.IsGestureActive);
    }

    private sealed class ThrowingMapDrawTarget : IMapDrawTarget
    {
        public void DrawImage(Bitmap bitmap, Rect sourceRect, Rect destinationRect) => throw new InvalidOperationException("sink failure");

        public void DrawLine(Pen pen, Point start, Point end) => throw new InvalidOperationException("sink failure");

        public void DrawRectangle(Brush fill, Pen? stroke, Rect rect) => throw new InvalidOperationException("sink failure");

        public IDisposable PushClip(Rect rect) => throw new InvalidOperationException("sink failure");
    }

    private static async Task<Harness> CreateSmallMapAsync()
    {
        FakeEditorDialogs dialogs = new();
        EditorDocumentController documentController = new(dialogs, new MapFileStore());
        MainWindowViewModel viewModel = new(documentController);
        string settingsPath = Path.Combine(Path.GetTempPath(), "map-editor-canvas-tests", "settings.json");
        AssetContextController assets = new(viewModel, new AppSettingsStore(settingsPath));
        MapCanvas canvas = new(viewModel, assets)
        {
            Width = CanvasWidth,
            Height = CanvasHeight,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        Panel host = new() { Children = { canvas } };
        Window window = new() { Content = host };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        dialogs.NewMapResult = new NewMapRequest(MapSize, MapSize);
        dialogs.DirtyResult = DirtyChoice.Discard;
        await viewModel.NewAsync();
        Dispatcher.UIThread.RunJobs();
        return new Harness(canvas, viewModel, dialogs, window, assets);
    }

    [AvaloniaFact]
    public async Task RenderMap_HonorsLayerVisibilityMask()
    {
        Harness harness = await CreateSmallMapAsync();
        using AssetFixture fixture = new();
        const string json = """
            { "tileSize": 32, "sheets": { "1": { "10": [0, 0, 32, 32], "11": [32, 0, 32, 32] } } }
            """;
        fixture.WriteManifest(json);
        fixture.WriteSheet(1, 64, 32);
        Assert.True(harness.Assets.TryOpen(fixture.AssetDirectory));

        MapDocument document = harness.ViewModel.Session.Document;
        document.SetLayer(0, 0, 0, new MapTileLayer(1, 11));
        document.SetLayer(0, 0, 3, new MapTileLayer(1, 10));

        harness.ViewModel.LayerVisibility = (byte)0b11111;
        RecordingMapDrawTarget allVisible = new();
        harness.Canvas.RenderMap(allVisible);

        harness.ViewModel.LayerVisibility = (byte)0b11110;
        RecordingMapDrawTarget layer0Hidden = new();
        harness.Canvas.RenderMap(layer0Hidden);

        Assert.Equal(
            new[] { new Rect(32, 0, 32, 32), new Rect(0, 0, 32, 32) },
            allVisible.Images.Select(image => image.Source).ToArray());
        Assert.All(allVisible.Images, image => Assert.Equal(new Rect(0, 0, Cell, Cell), image.Destination));

        Assert.Single(layer0Hidden.Images);
        Assert.Equal(new Rect(0, 0, 32, 32), layer0Hidden.Images[0].Source);
        Assert.Equal(new Rect(0, 0, Cell, Cell), layer0Hidden.Images[0].Destination);
    }
}
