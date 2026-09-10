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
using MapEditor.Core.Terrain;
using MapEditor.Rendering;
using MapEditor.Rendering.Terrain;
using Xunit;

namespace MapEditor.App.Tests;

public class MapCanvasTests
{
    private const int CanvasWidth = 300;
    private const int CanvasHeight = 200;
    private const int MapSize = 4;
    private const int Cell = 32;

    // ARGB literals mirroring the internal MapRenderPalette preview fills (RenderColor is RGBA)
    private static readonly Color BlockPreviewColor = Color.FromArgb(0x60, 0xFF, 0x00, 0x00);
    private static readonly Color UnblockPreviewColor = Color.FromArgb(0x60, 0x00, 0xFF, 0x00);

    private sealed class Harness : IDisposable
    {
        private bool _disposed;

        internal Harness(MapCanvas canvas, MapDocumentViewModel viewModel, WorkspaceViewModel workspace,
            FakeEditorDialogs dialogs, Window window, AssetContextController assets)
        {
            Canvas = canvas;
            ViewModel = viewModel;
            Workspace = workspace;
            Dialogs = dialogs;
            Window = window;
            Assets = assets;
        }

        internal MapCanvas Canvas { get; }
        internal WorkspaceViewModel Workspace { get; }
        internal MapDocumentViewModel ViewModel { get; }
        internal FakeEditorDialogs Dialogs { get; }
        internal Window Window { get; }
        internal AssetContextController Assets { get; }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Window.Content = null;
            Canvas.Dispose();
            Window.Close();
            Dispatcher.UIThread.RunJobs();
            Assets.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task LeftPressRelease_PaintsOneCellAsSingleUndoEntry()
    {
        using Harness harness = CreateSmallMapAsync();
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
        using Harness harness = CreateSmallMapAsync();
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
        using Harness harness = CreateSmallMapAsync();
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
        using Harness harness = CreateSmallMapAsync();
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
    public async Task BlockedDrag_BlocksTheRectangleAsOneUndoEntry()
    {
        using Harness harness = CreateSmallMapAsync();
        harness.ViewModel.ActiveTool = MapEditTool.Blocked;
        MapDocument document = harness.ViewModel.Session.Document;

        harness.Window.MouseDown(new Point(Cell / 2, Cell / 2), MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseMove(new Point(Cell * 2 + Cell / 2, Cell * 2 + Cell / 2), RawInputModifiers.None);
        harness.Window.MouseUp(new Point(Cell * 2 + Cell / 2, Cell * 2 + Cell / 2), MouseButton.Left, RawInputModifiers.None);

        for (int y = 0; y <= 2; y++)
            for (int x = 0; x <= 2; x++)
                Assert.True(document[x, y].IsBlocked);

        Assert.False(document[3, 3].IsBlocked);
        Assert.True(harness.ViewModel.ShowBlocked);
        Assert.True(harness.ViewModel.Undo());
        Assert.False(document[0, 0].IsBlocked);
        Assert.False(harness.ViewModel.CanUndo);
    }

    [AvaloniaFact]
    public async Task BlockedShiftDrag_ClearsInsteadOfSetting()
    {
        using Harness harness = CreateSmallMapAsync();
        harness.ViewModel.ActiveTool = MapEditTool.Blocked;
        MapDocument document = harness.ViewModel.Session.Document;
        for (int y = 0; y < MapSize; y++)
            for (int x = 0; x < MapSize; x++)
                document.SetFlags(x, y, MapDocument.BlockedFlag);

        harness.Window.MouseDown(new Point(Cell / 2, Cell / 2), MouseButton.Left, RawInputModifiers.Shift);
        harness.Window.MouseMove(new Point(Cell * 2 + Cell / 2, Cell * 2 + Cell / 2), RawInputModifiers.Shift);
        harness.Window.MouseUp(new Point(Cell * 2 + Cell / 2, Cell * 2 + Cell / 2), MouseButton.Left, RawInputModifiers.Shift);

        for (int y = 0; y <= 2; y++)
            for (int x = 0; x <= 2; x++)
                Assert.False(document[x, y].IsBlocked);

        Assert.True(document[3, 3].IsBlocked);
        Assert.True(harness.ViewModel.ShowBlocked);
        Assert.True(harness.ViewModel.Undo());
        Assert.True(document[0, 0].IsBlocked);
        Assert.False(harness.ViewModel.CanUndo);
    }

    [AvaloniaFact]
    public async Task BlockedDragEscape_AppliesNothing()
    {
        using Harness harness = CreateSmallMapAsync();
        harness.ViewModel.ActiveTool = MapEditTool.Blocked;
        MapDocument document = harness.ViewModel.Session.Document;

        harness.Window.MouseDown(new Point(Cell / 2, Cell / 2), MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseMove(new Point(Cell * 2 + Cell / 2, Cell * 2 + Cell / 2), RawInputModifiers.None);
        harness.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        harness.Window.MouseUp(new Point(Cell * 2 + Cell / 2, Cell * 2 + Cell / 2), MouseButton.Left, RawInputModifiers.None);

        for (int y = 0; y < MapSize; y++)
            for (int x = 0; x < MapSize; x++)
                Assert.False(document[x, y].IsBlocked);

        Assert.False(harness.ViewModel.CanUndo);
    }

    [AvaloniaFact]
    public async Task BlockedDragCaptureLost_AppliesNothing()
    {
        using Harness harness = CreateSmallMapAsync();
        harness.ViewModel.ActiveTool = MapEditTool.Blocked;
        MapDocument document = harness.ViewModel.Session.Document;

        harness.Window.MouseDown(new Point(Cell / 2, Cell / 2), MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseMove(new Point(Cell * 2 + Cell / 2, Cell * 2 + Cell / 2), RawInputModifiers.None);
        Assert.Equal(9, CountRectanglesWithFill(harness, BlockPreviewColor));
        harness.Window.Content = new Border();

        for (int y = 0; y < MapSize; y++)
            for (int x = 0; x < MapSize; x++)
                Assert.False(document[x, y].IsBlocked);

        Assert.False(harness.ViewModel.CanUndo);
        Assert.Equal(0, CountRectanglesWithFill(harness, BlockPreviewColor));
    }

    [AvaloniaFact]
    public async Task FinishInteractionCommit_AppliesTheBlockRectangle()
    {
        using Harness harness = CreateSmallMapAsync();
        harness.ViewModel.ActiveTool = MapEditTool.Blocked;
        MapDocument document = harness.ViewModel.Session.Document;

        harness.Window.MouseDown(new Point(Cell / 2, Cell / 2), MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseMove(new Point(Cell * 2 + Cell / 2, Cell * 2 + Cell / 2), RawInputModifiers.None);
        harness.Canvas.FinishInteraction(commit: true);

        for (int y = 0; y <= 2; y++)
            for (int x = 0; x <= 2; x++)
                Assert.True(document[x, y].IsBlocked);

        Assert.False(document[3, 3].IsBlocked);
        Assert.True(harness.ViewModel.CanUndo);
        Assert.True(harness.ViewModel.Undo());
        Assert.False(document[0, 0].IsBlocked);
        Assert.False(harness.ViewModel.CanUndo);
    }

    [AvaloniaFact]
    public async Task BlockedDragShiftReleasedMidDrag_StillClears()
    {
        using Harness harness = CreateSmallMapAsync();
        harness.ViewModel.ActiveTool = MapEditTool.Blocked;
        MapDocument document = harness.ViewModel.Session.Document;
        for (int y = 0; y < MapSize; y++)
            for (int x = 0; x < MapSize; x++)
                document.SetFlags(x, y, MapDocument.BlockedFlag);

        harness.Window.MouseDown(new Point(Cell / 2, Cell / 2), MouseButton.Left, RawInputModifiers.Shift);
        harness.Window.MouseMove(new Point(Cell * 2 + Cell / 2, Cell * 2 + Cell / 2), RawInputModifiers.None);
        harness.Window.MouseUp(new Point(Cell * 2 + Cell / 2, Cell * 2 + Cell / 2), MouseButton.Left, RawInputModifiers.None);

        for (int y = 0; y <= 2; y++)
            for (int x = 0; x <= 2; x++)
                Assert.False(document[x, y].IsBlocked);

        Assert.True(document[3, 3].IsBlocked);
        Assert.True(harness.ViewModel.CanUndo);
    }

    [AvaloniaFact]
    public async Task BlockPreview_VanishesAfterCommitAndAfterCancel()
    {
        using Harness harness = CreateSmallMapAsync();
        harness.ViewModel.ActiveTool = MapEditTool.Blocked;
        MapDocument document = harness.ViewModel.Session.Document;

        for (int y = 0; y <= 2; y++)
            for (int x = 0; x <= 2; x++)
                document.SetFlags(x, y, MapDocument.BlockedFlag);

        harness.Window.MouseDown(new Point(Cell / 2, Cell / 2), MouseButton.Left, RawInputModifiers.Shift);
        harness.Window.MouseMove(new Point(Cell * 2 + Cell / 2, Cell * 2 + Cell / 2), RawInputModifiers.Shift);
        Assert.Equal(9, CountRectanglesWithFill(harness, UnblockPreviewColor));
        harness.Window.MouseUp(new Point(Cell * 2 + Cell / 2, Cell * 2 + Cell / 2), MouseButton.Left, RawInputModifiers.Shift);
        Assert.Equal(0, CountRectanglesWithFill(harness, UnblockPreviewColor));
        Assert.False(document[0, 0].IsBlocked);
        Assert.True(harness.ViewModel.CanUndo);

        for (int y = 1; y <= 3; y++)
            for (int x = 1; x <= 3; x++)
                document.SetFlags(x, y, MapDocument.BlockedFlag);

        harness.Window.MouseDown(new Point(Cell + Cell / 2, Cell + Cell / 2), MouseButton.Left, RawInputModifiers.Shift);
        harness.Window.MouseMove(new Point(3 * Cell + Cell / 2, 3 * Cell + Cell / 2), RawInputModifiers.Shift);
        Assert.Equal(9, CountRectanglesWithFill(harness, UnblockPreviewColor));
        harness.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.Shift);
        Assert.Equal(0, CountRectanglesWithFill(harness, UnblockPreviewColor));
        Assert.True(document[1, 1].IsBlocked);

        Assert.True(harness.ViewModel.Undo());
        Assert.True(document[0, 0].IsBlocked);
        Assert.False(harness.ViewModel.CanUndo);
    }

    [AvaloniaFact]
    public async Task EyedropperPress_SelectsTileLayerWithoutEditing()
    {
        using Harness harness = CreateSmallMapAsync();
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
        using Harness harness = CreateSmallMapAsync();
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
        using Harness harness = CreateSmallMapAsync();
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
        using Harness harness = CreateSmallMapAsync();
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
        using Harness harness = CreateSmallMapAsync();
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
        using Harness harness = CreateSmallMapAsync();
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
        using Harness harness = CreateSmallMapAsync();
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
        using Harness harness = CreateSmallMapAsync();
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
        using Harness harness = CreateSmallMapAsync();
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
        using Harness harness = CreateSmallMapAsync();
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
    public async Task FinishInteraction_CommitCompletesAndCancelRestores()
    {
        using Harness harness = CreateSmallMapAsync();
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
        using Harness harness = CreateSmallMapAsync();
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
        using Harness harness = CreateSmallMapAsync();
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
        using Harness harness = CreateSmallMapAsync();
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
        using Harness harness = CreateSmallMapAsync();
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
        using Harness harness = CreateSmallMapAsync();
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
        using Harness harness = CreateSmallMapAsync();
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
    public async Task MultiSelectTool_ClickWithoutMove_SetsSingleTileRectangle()
    {
        using Harness harness = CreateSmallMapAsync();
        harness.ViewModel.ActiveTool = MapEditTool.MultiSelect;
        Point p = new(Cell + Cell / 2, Cell + Cell / 2);
        harness.Window.MouseDown(p, MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseUp(p, MouseButton.Left, RawInputModifiers.None);

        Assert.Equal(new MapTileRectangle(1, 1, 1, 1), harness.ViewModel.SelectionRectangle);
        Assert.False(harness.ViewModel.Session.HasActiveStroke);
        Assert.False(harness.ViewModel.Session.CanUndo);
    }

    [AvaloniaFact]
    public async Task PasteMode_IsOneShot_SecondClickRunsActiveTool()
    {
        using Harness harness = CreateSmallMapAsync();
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
        using Harness harness = CreateSmallMapAsync();
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
        using Harness harness = CreateSmallMapAsync();
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
        using Harness harness = CreateSmallMapAsync();
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
        using Harness harness = CreateSmallMapAsync();
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
        using Harness harness = CreateSmallMapAsync();
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
        using Harness harness = CreateSmallMapAsync();
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

    [AvaloniaFact]
    public async Task CompletedGesture_CreatesTimelineEntry_RatherThanBypassingCoordination()
    {
        using Harness harness = CreateSmallMapAsync();
        MapDocument document = harness.ViewModel.Session.Document;
        DocumentEditTimeline timeline = harness.ViewModel.Timeline;
        harness.ViewModel.Brush = new MapTileLayer(7, 42);

        Assert.False(timeline.CanUndo);

        harness.Window.MouseDown(new Point(Cell / 2, Cell / 2), MouseButton.Left, RawInputModifiers.None);
        Assert.False(timeline.CanUndo);
        harness.Window.MouseUp(new Point(Cell / 2, Cell / 2), MouseButton.Left, RawInputModifiers.None);

        Assert.Equal(new MapTileLayer(7, 42), document[0, 0].GetLayer(0));
        Assert.True(timeline.CanUndo);

        Assert.True(timeline.Undo());
        Assert.Equal(new MapTileLayer(0, 0), document[0, 0].GetLayer(0));
        Assert.True(timeline.CanRedo);

        Assert.True(timeline.Redo());
        Assert.Equal(new MapTileLayer(7, 42), document[0, 0].GetLayer(0));
        Assert.False(timeline.CanRedo);
        Assert.True(timeline.CanUndo);
    }

    [AvaloniaFact]
    public async Task CanceledGesture_CreatesNoTimelineEntry_AndRestoresThroughCoordination()
    {
        using Harness harness = CreateSmallMapAsync();
        MapEditSession session = harness.ViewModel.Session;
        MapDocument document = session.Document;
        DocumentEditTimeline timeline = harness.ViewModel.Timeline;
        harness.ViewModel.Brush = new MapTileLayer(5, 5);
        long versionBefore = session.HistoryVersion;

        harness.Window.MouseDown(new Point(Cell / 2, Cell / 2), MouseButton.Left, RawInputModifiers.None);
        Assert.Equal(new MapTileLayer(5, 5), document[0, 0].GetLayer(0));
        harness.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

        Assert.Equal(new MapTileLayer(0, 0), document[0, 0].GetLayer(0));
        Assert.Equal(versionBefore, session.HistoryVersion);
        Assert.False(timeline.CanUndo);
        Assert.False(timeline.CanRedo);
    }

    [AvaloniaFact]
    public async Task BlockedDragCommitAndEscape_CoordinateThroughTheTimeline()
    {
        using Harness harness = CreateSmallMapAsync();
        harness.ViewModel.ActiveTool = MapEditTool.Blocked;
        MapDocument document = harness.ViewModel.Session.Document;
        DocumentEditTimeline timeline = harness.ViewModel.Timeline;

        harness.Window.MouseDown(new Point(Cell / 2, Cell / 2), MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseMove(new Point(Cell * 2 + Cell / 2, Cell * 2 + Cell / 2), RawInputModifiers.None);
        harness.Window.MouseUp(new Point(Cell * 2 + Cell / 2, Cell * 2 + Cell / 2), MouseButton.Left, RawInputModifiers.None);
        Assert.True(document[0, 0].IsBlocked);
        Assert.True(timeline.CanUndo);

        Assert.True(timeline.Undo());
        Assert.False(document[0, 0].IsBlocked);

        harness.Window.MouseDown(new Point(Cell / 2, Cell / 2), MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseMove(new Point(Cell * 2 + Cell / 2, Cell * 2 + Cell / 2), RawInputModifiers.None);
        harness.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.False(document[0, 0].IsBlocked);
        Assert.True(timeline.CanRedo);
        Assert.False(timeline.CanUndo);
    }

    [AvaloniaFact]
    public void TerrainToolbarPaletteAndShortcutActivation_ImmediatelyRoutesMapPressThroughDedicatedTerrainApi()
    {
        foreach (string path in new[] { "toolbar", "palette", "shortcut" })
        {
            using MainWindowHarness harness = MainWindowHarness.Create();
            PublishTerrain(harness.Assets, ("Grass", 10));
            string id = harness.Assets.Current.Terrain.Runtime!.EnabledSets[0].Id;
            if (path == "toolbar")
            {
                harness.Window.FindControl<ToggleButton>("TerrainTool")!.IsChecked = true;
            }
            else if (path == "palette")
            {
                harness.ViewModel.SelectedTerrainId = id;
            }
            else
            {
                harness.Window.Canvas.Focus();
                harness.Window.KeyPressQwerty(PhysicalKey.T, RawInputModifiers.None);
            }

            Point point = harness.Window.Canvas.TranslatePoint(new Point(16, 16), harness.Window).Value;
            harness.Window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
            Assert.True(harness.ViewModel.Session.HasActiveStroke);
            harness.Window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
            Assert.Equal(new MapTileLayer(1, 10), harness.ViewModel.Session.Document[0, 0].GetLayer(0));
        }
    }

    [AvaloniaFact]
    public void TerrainPaint_PressPreviewsNeighborRepairAndReleaseCreatesOneUndoEntry()
    {
        using Harness harness = CreateSmallMapAsync();
        PublishTerrain(harness, ("Grass", 10));
        MapEditSession session = harness.ViewModel.Session;
        long version = session.HistoryVersion;

        harness.Window.MouseDown(new Point(16, 16), MouseButton.Left, RawInputModifiers.None);
        Assert.Equal(new MapTileLayer(1, 10), session.Document[0, 0].GetLayer(0));
        Assert.Equal(version, session.HistoryVersion);
        harness.Window.MouseUp(new Point(16, 16), MouseButton.Left, RawInputModifiers.None);

        Assert.Equal(version + 1, session.HistoryVersion);
        Assert.True(session.CanUndo);
        Assert.True(harness.ViewModel.Undo());
        Assert.False(session.CanUndo);
    }

    [AvaloniaFact]
    public void TerrainErase_SparseDragUsesPartTwoInterpolationAndOneCommand()
    {
        using Harness harness = CreateSmallMapAsync();
        PublishTerrain(harness, ("Grass", 10));
        for (int x = 0; x < 4; x++)
        {
            harness.ViewModel.Session.Document.SetLayer(x, 0, 0, new MapTileLayer(1, 10));
        }
        harness.ViewModel.TerrainMode = TerrainEditMode.Erase;
        long version = harness.ViewModel.Session.HistoryVersion;

        harness.Window.MouseDown(new Point(16, 16), MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseMove(new Point(3 * Cell + 16, 16), RawInputModifiers.None);
        harness.Window.MouseUp(new Point(3 * Cell + 16, 16), MouseButton.Left, RawInputModifiers.None);

        Assert.All(Enumerable.Range(0, 4), x => Assert.Equal(default, harness.ViewModel.Session.Document[x, 0].GetLayer(0)));
        Assert.Equal(version + 1, harness.ViewModel.Session.HistoryVersion);
    }

    [AvaloniaFact]
    public void TerrainEscape_RestoresExactBytesAndHistoryVersion()
    {
        using Harness harness = CreateSmallMapAsync();
        PublishTerrain(harness, ("Grass", 10));
        byte[] before = MapCodec.Encode(harness.ViewModel.Session.Document);
        long version = harness.ViewModel.Session.HistoryVersion;
        harness.Canvas.Focus();

        harness.Window.MouseDown(new Point(16, 16), MouseButton.Left, RawInputModifiers.None);
        harness.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

        Assert.Equal(before, MapCodec.Encode(harness.ViewModel.Session.Document));
        Assert.Equal(version, harness.ViewModel.Session.HistoryVersion);
        Assert.False(harness.ViewModel.Session.CanUndo);
    }

    [AvaloniaFact]
    public void TerrainCaptureLoss_CancelsWhileManualCaptureLossStillCommits()
    {
        using Harness terrain = CreateSmallMapAsync();
        PublishTerrain(terrain, ("Grass", 10));
        byte[] before = MapCodec.Encode(terrain.ViewModel.Session.Document);
        terrain.Window.MouseDown(new Point(16, 16), MouseButton.Left, RawInputModifiers.None);
        terrain.Window.Content = new Border();
        Assert.Equal(before, MapCodec.Encode(terrain.ViewModel.Session.Document));
        Assert.False(terrain.ViewModel.Session.CanUndo);

        using Harness manual = CreateSmallMapAsync();
        manual.ViewModel.Brush = new MapTileLayer(2, 20);
        manual.Window.MouseDown(new Point(16, 16), MouseButton.Left, RawInputModifiers.None);
        manual.Window.Content = new Border();
        Assert.Equal(new MapTileLayer(2, 20), manual.ViewModel.Session.Document[0, 0].GetLayer(0));
        Assert.True(manual.ViewModel.Session.CanUndo);
    }

    [AvaloniaFact]
    public void TerrainGesture_CapturesLayerSelectionModeAndTerrainAtPress()
    {
        using Harness harness = CreateSmallMapAsync();
        PublishTerrain(harness, ("Grass", 10), ("Water", 20));
        harness.ViewModel.SelectedLayers = 1;

        harness.Window.MouseDown(new Point(16, 16), MouseButton.Left, RawInputModifiers.None);
        harness.ViewModel.SelectedLayers = 0b10000;
        harness.Window.MouseMove(new Point(Cell + 16, 16), RawInputModifiers.None);
        harness.Window.MouseUp(new Point(Cell + 16, 16), MouseButton.Left, RawInputModifiers.None);

        Assert.Equal(new MapTileLayer(1, 10), harness.ViewModel.Session.Document[1, 0].GetLayer(0));
        Assert.Equal(default, harness.ViewModel.Session.Document[1, 0].GetLayer(4));
    }

    [AvaloniaFact]
    public void TerrainPaint_SelectedLayers10101ChangesOnlyTopLayerFour()
    {
        using Harness harness = CreateSmallMapAsync();
        PublishTerrain(harness, ("Grass", 10));
        harness.ViewModel.SelectedLayers = 0b10101;

        harness.Window.MouseDown(new Point(16, 16), MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseUp(new Point(16, 16), MouseButton.Left, RawInputModifiers.None);

        for (int layer = 0; layer < MapDocument.LayerCount; layer++)
        {
            Assert.Equal(layer == 4 ? new MapTileLayer(1, 10) : default,
                harness.ViewModel.Session.Document[0, 0].GetLayer(layer));
        }
    }

    [AvaloniaFact]
    public void TerrainOutsideThenReentryContinuesFromLastValidCellAndOutsideReleaseCommits()
    {
        using Harness harness = CreateSmallMapAsync();
        PublishTerrain(harness, ("Grass", 10));

        harness.Window.MouseDown(new Point(16, 16), MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseMove(new Point(CanvasWidth + 50, 16), RawInputModifiers.None);
        harness.Window.MouseMove(new Point(3 * Cell + 16, 16), RawInputModifiers.None);
        harness.Window.MouseUp(new Point(CanvasWidth + 50, CanvasHeight + 50), MouseButton.Left, RawInputModifiers.None);

        Assert.All(Enumerable.Range(0, 4), x => Assert.Equal(new MapTileLayer(1, 10), harness.ViewModel.Session.Document[x, 0].GetLayer(0)));
        Assert.Equal(1, harness.ViewModel.Session.HistoryVersion);
    }

    [AvaloniaFact]
    public void TerrainSelectionModeTabOrToolChangeCancelsBeforeStateChange()
    {
        using Harness harness = CreateSmallMapAsync();
        PublishTerrain(harness, ("Grass", 10), ("Water", 20));
        MapDocumentViewModel viewModel = harness.ViewModel;
        string second = harness.Assets.Current.Terrain.Runtime!.EnabledSets[1].Id;

        void Preview()
        {
            harness.Window.MouseDown(new Point(16, 16), MouseButton.Left, RawInputModifiers.None);
            Assert.NotEqual(default, viewModel.Session.Document[0, 0].GetLayer(0));
        }

        Preview();
        viewModel.SelectedTerrainId = second;
        Assert.Equal(default, viewModel.Session.Document[0, 0].GetLayer(0));
        Preview();
        viewModel.TerrainMode = TerrainEditMode.Erase;
        Assert.Equal(default, viewModel.Session.Document[0, 0].GetLayer(0));
        viewModel.TerrainMode = TerrainEditMode.Paint;
        Preview();
        viewModel.PaletteMode = AssetPaletteMode.Tiles;
        Assert.Equal(default, viewModel.Session.Document[0, 0].GetLayer(0));
        viewModel.PaletteMode = AssetPaletteMode.Terrain;
        Preview();
        viewModel.ActiveTool = MapEditTool.Eraser;
        Assert.Equal(default, viewModel.Session.Document[0, 0].GetLayer(0));
        Assert.False(viewModel.Session.CanUndo);
    }

    [AvaloniaFact]
    public void CatalogReplacement_CancelsEveryLiveCanvasBeforeNewResolverIsVisible()
    {
        using Harness harness = CreateSmallMapAsync();
        PublishTerrain(harness, ("Grass", 10));
        AssetContext old = harness.Assets.Current;
        harness.Dialogs.NewMapResult = new NewMapRequest(4, 4);
        harness.Workspace.NewAsync().GetAwaiter().GetResult();
        MapDocumentViewModel second = harness.Workspace.ActiveDocument;
        using MapCanvas secondCanvas = new(second, harness.Assets);
        byte[] before = MapCodec.Encode(harness.ViewModel.Session.Document);
        long version = harness.ViewModel.Session.HistoryVersion;
        bool callbackSawOld = false;
        bool observerSawAllCommitted = false;
        using IDisposable registration = harness.Assets.RegisterTerrainGestureCancellation(() =>
        {
            callbackSawOld = ReferenceEquals(old, harness.Assets.Current);
            Assert.Equal(before, MapCodec.Encode(harness.ViewModel.Session.Document));
        });
        harness.Window.MouseDown(new Point(16, 16), MouseButton.Left, RawInputModifiers.None);

        TerrainAssetLoadResult replacement = TerrainTestData.Result(("Water", 20));
        string replacementId = replacement.Runtime!.EnabledSets[0].Id;
        harness.ViewModel.PropertyChanged += (_, _) => observerSawAllCommitted |=
            harness.Workspace.Documents.All(document => document.SelectedTerrainId == replacementId);
        TerrainReplacementPlan plan = harness.Assets.PrepareTerrainReplacement(old, replacement).Plan!;
        harness.Assets.PublishTerrain(plan);

        Assert.True(callbackSawOld);
        Assert.True(observerSawAllCommitted);
        Assert.Equal(before, MapCodec.Encode(harness.ViewModel.Session.Document));
        Assert.Equal(version, harness.ViewModel.Session.HistoryVersion);
        Assert.NotSame(old, harness.Assets.Current);
    }

    [AvaloniaFact]
    public void MapCanvas_DetachReattachRemainsRegisteredAndFunctional()
    {
        using Harness harness = CreateSmallMapAsync();
        PublishTerrain(harness, ("Grass", 10));
        Panel host = Assert.IsAssignableFrom<Panel>(((Grid)harness.Window.Content!).Children[0]);
        host.Children.Remove(harness.Canvas);
        Dispatcher.UIThread.RunJobs();
        host.Children.Add(harness.Canvas);
        Dispatcher.UIThread.RunJobs();

        harness.Window.MouseDown(new Point(16, 16), MouseButton.Left, RawInputModifiers.None);
        TerrainReplacementPlan plan = harness.Assets.PrepareTerrainReplacement(
            harness.Assets.Current, TerrainTestData.Result(("Water", 20))).Plan!;
        harness.Assets.PublishTerrain(plan);
        Assert.Equal(default, harness.ViewModel.Session.Document[0, 0].GetLayer(0));

        harness.ViewModel.SelectedTerrainId = harness.Assets.Current.Terrain.Runtime!.EnabledSets[0].Id;
        harness.Window.MouseDown(new Point(16, 16), MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseUp(new Point(16, 16), MouseButton.Left, RawInputModifiers.None);
        Assert.Equal(new MapTileLayer(1, 20), harness.ViewModel.Session.Document[0, 0].GetLayer(0));
    }

    [AvaloniaFact]
    public async Task CloseDocumentAndWindow_DisposeViewsAndLaterReplacementNeverCallsThem()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        PublishTerrain(harness.Assets, ("Grass", 10));
        MapCanvas removedCanvas = harness.Canvas;
        MapDocumentViewModel removed = harness.ViewModel;
        await NewDocument(harness);
        harness.Dialogs.DirtyResult = DirtyChoice.Discard;
        await harness.Workspace.CloseAsync(removed);
        int invalidations = removedCanvas.InvalidationCount;

        PublishTerrain(harness.Assets, ("Water", 20));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(invalidations, removedCanvas.InvalidationCount);
        Assert.False(harness.Window.HasViewFor(removed));
        harness.Window.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.False(harness.Window.IsVisible);
    }

    [AvaloniaFact]
    public void MapCanvas_DisposeIsIdempotentCancelsTerrainAndIgnoresLaterViewModelOrControllerCallbacks()
    {
        using Harness harness = CreateSmallMapAsync();
        PublishTerrain(harness, ("Grass", 10));
        byte[] before = MapCodec.Encode(harness.ViewModel.Session.Document);
        harness.Window.MouseDown(new Point(16, 16), MouseButton.Left, RawInputModifiers.None);

        harness.Canvas.Dispose();
        harness.Canvas.Dispose();
        int invalidations = harness.Canvas.InvalidationCount;
        harness.ViewModel.Refresh(EditorRefresh.Canvas);
        PublishTerrain(harness, ("Water", 20));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(before, MapCodec.Encode(harness.ViewModel.Session.Document));
        Assert.Equal(invalidations, harness.Canvas.InvalidationCount);
        Assert.False(harness.ViewModel.Session.HasActiveStroke);
    }

    private static void PublishTerrain(Harness harness, params (string Name, int Graphic)[] definitions)
    {
        PublishTerrain(harness.Assets, definitions);
        harness.ViewModel.ActiveTool = MapEditTool.Terrain;
    }

    private static void PublishTerrain(AssetContextController assets, params (string Name, int Graphic)[] definitions)
    {
        TerrainReplacementPlan plan = assets.PrepareTerrainReplacement(
            assets.Current, TerrainTestData.Result(definitions)).Plan!;
        assets.PublishTerrain(plan);
    }

    private static async Task NewDocument(MainWindowHarness harness)
    {
        harness.Dialogs.NewMapResult = new NewMapRequest(4, 4);
        await harness.Workspace.NewAsync();
        Dispatcher.UIThread.RunJobs();
    }

    private static int CountRectanglesWithFill(Harness harness, Color fill)
    {
        RecordingMapDrawTarget target = new();
        harness.Canvas.RenderMap(target);
        return target.Rectangles.Count(r => r.Fill is SolidColorBrush brush && brush.Color == fill);
    }

    private sealed class ThrowingMapDrawTarget : IMapDrawTarget
    {
        public void DrawImage(Bitmap bitmap, Rect sourceRect, Rect destinationRect) => throw new InvalidOperationException("sink failure");

        public void DrawLine(Pen pen, Point start, Point end) => throw new InvalidOperationException("sink failure");

        public void DrawRectangle(Brush fill, Pen? stroke, Rect rect) => throw new InvalidOperationException("sink failure");

        public void DrawText(string text, Point center, double fontSize, Brush fill, Brush? stroke) => throw new InvalidOperationException("sink failure");

        public IDisposable PushClip(Rect rect) => throw new InvalidOperationException("sink failure");
    }

    private static Harness CreateSmallMapAsync()
    {
        FakeEditorDialogs dialogs = new();
        var workspace = new WorkspaceViewModel(dialogs, new MapFileStore());
        dialogs.NewMapResult = new NewMapRequest(MapSize, MapSize);
        // FakeEditorDialogs answers synchronously, so this completes before the next line
        workspace.NewAsync().GetAwaiter().GetResult();
        MapDocumentViewModel viewModel = workspace.ActiveDocument;
        string settingsPath = Path.Combine(Path.GetTempPath(), "map-editor-canvas-tests", "settings.json");
        AssetContextController assets = new(workspace, new AppSettingsStore(settingsPath));
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
        return new Harness(canvas, viewModel, workspace, dialogs, window, assets);
    }

    [AvaloniaFact]
    public async Task RenderMap_HonorsLayerVisibilityMask()
    {
        using Harness harness = CreateSmallMapAsync();
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
