using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
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

    [AvaloniaFact]
    public async Task SelectTool_Click_SetsSelectionWithoutStrokeOrHistory()
    {
        Harness harness = await CreateSmallMapAsync();
        harness.ViewModel.ActiveTool = MapEditTool.Select;
        Point p = new(Cell / 2, Cell / 2);
        harness.Window.MouseDown(p, MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseUp(p, MouseButton.Left, RawInputModifiers.None);

        Assert.Equal(0, harness.ViewModel.SelectedX);
        Assert.Equal(0, harness.ViewModel.SelectedY);
        Assert.False(harness.ViewModel.Session.HasActiveStroke);
        Assert.False(harness.ViewModel.Session.CanUndo);
    }

    [AvaloniaFact]
    public async Task FloodFillTool_Click_FillsRegionAsOneUndoableCommand()
    {
        Harness harness = await CreateSmallMapAsync();
        MapDocument document = harness.ViewModel.Session.Document;
        // the harness map is 4x4 (MapSize = 4); ring = all perimeter cells with (1,1),
        // interior = the 4 cells (1,1),(1,2),(2,1),(2,2)
        for (int x = 0; x < MapSize; x++)
            for (int y = 0; y < MapSize; y++)
                if (x == 0 || x == MapSize - 1 || y == 0 || y == MapSize - 1)
                    document.SetLayer(x, y, 0, new MapTileLayer(1, 1));

        harness.ViewModel.Brush = new MapTileLayer(9, 9);
        harness.ViewModel.ActiveTool = MapEditTool.FloodFill;
        Point p = new(Cell + Cell / 2, Cell + Cell / 2);
        harness.Window.MouseDown(p, MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseUp(p, MouseButton.Left, RawInputModifiers.None);

        Assert.Equal(new MapTileLayer(9, 9), document[1, 1].GetLayer(0));
        Assert.Equal(new MapTileLayer(9, 9), document[2, 2].GetLayer(0));
        Assert.Equal(new MapTileLayer(1, 1), document[0, 0].GetLayer(0));
        Assert.True(harness.ViewModel.Session.CanUndo);
        harness.ViewModel.Undo();
        Assert.Equal(new MapTileLayer(0, 0), document[1, 1].GetLayer(0));
    }

    [AvaloniaFact]
    public async Task MultiSelectTool_Drag_SetsSelectionRectangle()
    {
        Harness harness = await CreateSmallMapAsync();
        harness.ViewModel.ActiveTool = MapEditTool.MultiSelect;
        Point start = new(Cell / 2, Cell / 2);
        Point end = new(3 * Cell + Cell / 2, 2 * Cell + Cell / 2);
        harness.Window.MouseDown(start, MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseMove(end, RawInputModifiers.None);
        harness.Window.MouseUp(end, MouseButton.Left, RawInputModifiers.None);

        Assert.Equal(new MapTileRectangle(0, 0, 4, 3), harness.ViewModel.SelectionRectangle);
        Assert.False(harness.ViewModel.Session.HasActiveStroke);
        Assert.False(harness.ViewModel.Session.CanUndo);
    }

    [AvaloniaFact]
    public async Task MultiSelectTool_DragOutsideMap_ClampsToMapBounds()
    {
        Harness harness = await CreateSmallMapAsync();
        harness.ViewModel.ActiveTool = MapEditTool.MultiSelect;
        Point start = new(Cell / 2, Cell / 2);
        Point outside = new(3 * Cell + Cell / 2 + 400, 3 * Cell + Cell / 2 + 400);
        harness.Window.MouseDown(start, MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseMove(outside, RawInputModifiers.None);
        harness.Window.MouseUp(outside, MouseButton.Left, RawInputModifiers.None);

        // the 4x4 map fully covered — the out-of-bounds drag endpoint clamps to the map edge
        Assert.Equal(new MapTileRectangle(0, 0, 4, 4), harness.ViewModel.SelectionRectangle);
    }

    [AvaloniaFact]
    public async Task PasteMode_IsOneShot_SecondClickRunsActiveTool()
    {
        Harness harness = await CreateSmallMapAsync();
        MapDocument document = harness.ViewModel.Session.Document;
        document.SetLayer(0, 0, 0, new MapTileLayer(7, 7));
        document.SetLayer(1, 0, 0, new MapTileLayer(7, 7));
        document.SetLayer(0, 1, 0, new MapTileLayer(7, 7));
        document.SetLayer(1, 1, 0, new MapTileLayer(7, 7));

        harness.ViewModel.SelectionRectangle = new MapTileRectangle(0, 0, 2, 2);
        harness.ViewModel.CopySelection();
        harness.ViewModel.BeginPasteMode();
        Assert.True(harness.ViewModel.PasteMode);

        Point target = new(2 * Cell + Cell / 2, 2 * Cell + Cell / 2);
        harness.Window.MouseDown(target, MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseUp(target, MouseButton.Left, RawInputModifiers.None);

        Assert.False(harness.ViewModel.PasteMode);
        Assert.Equal(new MapTileLayer(7, 7), document[2, 2].GetLayer(0));
        Assert.True(harness.ViewModel.Session.CanUndo);

        // paste mode is one-shot: the second click runs the active tool (Pencil). With the
        // brush set to the pasted value the pencil stroke has zero deltas, so no second
        // history entry appears — a second paste would have pushed one
        harness.ViewModel.Brush = new MapTileLayer(7, 7);
        harness.Window.MouseDown(target, MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseUp(target, MouseButton.Left, RawInputModifiers.None);
        Assert.True(harness.ViewModel.Undo());
        Assert.False(harness.ViewModel.CanUndo);
    }

    [AvaloniaFact]
    public async Task PasteMode_EscapeCancelsWithoutPasting()
    {
        Harness harness = await CreateSmallMapAsync();
        MapDocument document = harness.ViewModel.Session.Document;
        document.SetLayer(0, 0, 0, new MapTileLayer(7, 7));
        harness.ViewModel.SelectionRectangle = new MapTileRectangle(0, 0, 1, 1);
        harness.ViewModel.CopySelection();
        harness.ViewModel.BeginPasteMode();

        harness.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.False(harness.ViewModel.PasteMode);
        Assert.False(harness.ViewModel.Session.CanUndo);
    }

    [AvaloniaFact]
    public async Task PasteMode_EscapeWithBrushFieldFocused_Cancels()
    {
        Harness harness = await CreateSmallMapAsync();
        MapDocument document = harness.ViewModel.Session.Document;
        document.SetLayer(0, 0, 0, new MapTileLayer(7, 7));
        harness.ViewModel.SelectionRectangle = new MapTileRectangle(0, 0, 1, 1);
        harness.ViewModel.CopySelection();
        harness.ViewModel.BeginPasteMode();

        harness.Window.FindControl<TextBox>("BrushGraphic").Focus();
        harness.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

        Assert.False(harness.ViewModel.PasteMode);
        Assert.False(harness.ViewModel.Session.CanUndo);
    }

    [AvaloniaFact]
    public async Task PasteMode_ClickOutsideMapCancelsWithoutPasting()
    {
        Harness harness = await CreateSmallMapAsync();
        MapDocument document = harness.ViewModel.Session.Document;
        document.SetLayer(0, 0, 0, new MapTileLayer(7, 7));
        harness.ViewModel.SelectionRectangle = new MapTileRectangle(0, 0, 1, 1);
        harness.ViewModel.CopySelection();
        harness.ViewModel.BeginPasteMode();

        Point outside = new(4 * Cell + Cell / 2, 4 * Cell + Cell / 2); // past the 4x4 map
        harness.Window.MouseDown(outside, MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseUp(outside, MouseButton.Left, RawInputModifiers.None);
        Assert.False(harness.ViewModel.PasteMode);
        Assert.False(harness.ViewModel.Session.CanUndo);
    }

    [AvaloniaFact]
    public async Task PasteMode_UndoMenuCancelsBeforeNextClick()
    {
        Harness harness = await CreateSmallMapAsync();
        MapDocument document = harness.ViewModel.Session.Document;

        // paint a cell before entering paste mode so the Undo menu item is enabled
        harness.ViewModel.Brush = new MapTileLayer(9, 9);
        Point paint = new(Cell / 2, Cell / 2);
        harness.Window.MouseDown(paint, MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseUp(paint, MouseButton.Left, RawInputModifiers.None);
        Assert.True(harness.ViewModel.CanUndo);

        document.SetLayer(1, 0, 0, new MapTileLayer(7, 7));
        harness.ViewModel.SelectionRectangle = new MapTileRectangle(1, 0, 1, 1);
        harness.ViewModel.CopySelection();
        harness.ViewModel.BeginPasteMode();

        harness.Window.FindControl<MenuItem>("UndoCommand").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.False(harness.ViewModel.PasteMode);
        Assert.Equal(new MapTileLayer(0, 0), document[0, 0].GetLayer(0));

        // the next click runs the active tool, not a paste
        harness.ViewModel.Brush = new MapTileLayer(4, 4);
        harness.ViewModel.ActiveTool = MapEditTool.Pencil;
        Point p = new(Cell + Cell / 2, Cell + Cell / 2);
        harness.Window.MouseDown(p, MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseUp(p, MouseButton.Left, RawInputModifiers.None);

        Assert.Equal(new MapTileLayer(4, 4), document[1, 1].GetLayer(0));
    }

    [AvaloniaFact]
    public async Task PasteMode_ClickOnNonCanvasControl_CancelsWithoutPasting()
    {
        Harness harness = await CreateSmallMapAsync();
        MapDocument document = harness.ViewModel.Session.Document;
        document.SetLayer(0, 0, 0, new MapTileLayer(7, 7));
        harness.ViewModel.SelectionRectangle = new MapTileRectangle(0, 0, 1, 1);
        harness.ViewModel.CopySelection();
        harness.ViewModel.BeginPasteMode();

        // the status bar is a plain control with no other side effects
        Point status = harness.Window.FindControl<Border>("StatusBar").TranslatePoint(new Point(5, 5), harness.Window).Value;
        harness.Window.MouseDown(status, MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseUp(status, MouseButton.Left, RawInputModifiers.None);

        Assert.False(harness.ViewModel.PasteMode);
        Assert.False(harness.ViewModel.Session.CanUndo);
    }

    [AvaloniaFact]
    public async Task PasteMode_ClickOnToolToggle_CancelsWithoutPasting()
    {
        Harness harness = await CreateSmallMapAsync();
        MapDocument document = harness.ViewModel.Session.Document;
        document.SetLayer(0, 0, 0, new MapTileLayer(7, 7));
        harness.ViewModel.SelectionRectangle = new MapTileRectangle(0, 0, 1, 1);
        harness.ViewModel.CopySelection();
        harness.ViewModel.BeginPasteMode();

        // ToggleButton marks PointerPressed handled, so only the tunneling window handler sees this press
        Point eraser = harness.Window.FindControl<Control>("EraserTool").TranslatePoint(new Point(5, 5), harness.Window).Value;
        harness.Window.MouseDown(eraser, MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseUp(eraser, MouseButton.Left, RawInputModifiers.None);

        Assert.False(harness.ViewModel.PasteMode);
        Assert.Equal(MapEditTool.Eraser, harness.ViewModel.ActiveTool);
        Assert.False(harness.ViewModel.Session.CanUndo);
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

        MenuItem undoCommand = new() { Name = "UndoCommand", Header = "Undo" };
        undoCommand.Click += (s, e) =>
        {
            canvas.FinishInteraction(commit: true);
            viewModel.Undo();
        };
        Menu menu = new() { Items = { new MenuItem { Header = "Edit", Items = { undoCommand } } } };
        ToggleButton eraserTool = new() { Name = "EraserTool", Content = "Eraser", Tag = "Eraser" };
        eraserTool.IsCheckedChanged += (s, e) =>
        {
            if (eraserTool.IsChecked == true && eraserTool.Tag is string name && Enum.TryParse(name, out MapEditTool tool))
            {
                viewModel.ActiveTool = tool;
            }
        };
        Border statusBar = new() { Name = "StatusBar", Width = 160, Height = 24 };
        TextBox brushGraphic = new() { Name = "BrushGraphic", Width = 64, Height = 24 };
        StackPanel chrome = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        chrome.Children.Add(menu);
        chrome.Children.Add(eraserTool);
        chrome.Children.Add(statusBar);
        chrome.Children.Add(brushGraphic);

        Grid root = new();
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        Grid.SetRow(host, 0);
        Grid.SetRow(chrome, 1);
        root.Children.Add(host);
        root.Children.Add(chrome);

        Window window = new() { Content = root, Width = 800, Height = 600 };
        NameScope nameScope = new();
        NameScope.SetNameScope(window, nameScope);
        nameScope.Register("UndoCommand", undoCommand);
        nameScope.Register("EraserTool", eraserTool);
        nameScope.Register("StatusBar", statusBar);
        nameScope.Register("BrushGraphic", brushGraphic);
        window.AddHandler(InputElement.PointerPressedEvent, (s, e) =>
        {
            if (viewModel.PasteMode && e.Source is not MapCanvas)
            {
                viewModel.CancelPasteMode();
            }
        }, RoutingStrategies.Tunnel);
        window.AddHandler(InputElement.KeyDownEvent, (s, e) =>
        {
            if (e.Key == Key.Escape)
            {
                canvas.FinishInteraction(commit: false);
                viewModel.CancelPasteMode();
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);
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
