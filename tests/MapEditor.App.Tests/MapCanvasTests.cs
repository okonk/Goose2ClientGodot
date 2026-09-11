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

    // ARGB literals mirroring the internal MapRenderPalette preview fills (RenderColor is RGBA)
    private static readonly Color BlockPreviewColor = Color.FromArgb(0x60, 0xFF, 0x00, 0x00);
    private static readonly Color UnblockPreviewColor = Color.FromArgb(0x60, 0x00, 0xFF, 0x00);

    private const string TwoSheetJson = """
        { "tileSize": 32, "sheets": {
          "1": { "10": [0, 0, 32, 32], "11": [32, 0, 32, 32] },
          "2": { "20": [0, 0, 32, 32] }
        } }
        """;

    private sealed record Harness(MapCanvas Canvas, MapDocumentViewModel ViewModel, FakeEditorDialogs Dialogs, Window Window, AssetContextController Assets);

    [AvaloniaFact]
    public async Task LeftPressRelease_PaintsOneCellAsSingleUndoEntry()
    {
        Harness harness = CreateSmallMapAsync();
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
        Harness harness = CreateSmallMapAsync();
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
        Harness harness = CreateSmallMapAsync();
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
        Harness harness = CreateSmallMapAsync();
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
        Harness harness = CreateSmallMapAsync();
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
        Harness harness = CreateSmallMapAsync();
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
        Harness harness = CreateSmallMapAsync();
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
        Harness harness = CreateSmallMapAsync();
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
        Harness harness = CreateSmallMapAsync();
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
        Harness harness = CreateSmallMapAsync();
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
        Harness harness = CreateSmallMapAsync();
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
        Harness harness = CreateSmallMapAsync();
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
    public async Task EyedropperPick_SelectsThePickedSheetInThePaletteAndRefreshesIt()
    {
        Harness harness = CreateSmallMapAsync();
        using AssetFixture fixture = new();
        fixture.WriteManifest(TwoSheetJson);
        fixture.WriteSheet(1, 64, 32);
        fixture.WriteSheet(2, 32, 32);
        Assert.True(harness.Assets.TryOpen(fixture.AssetDirectory));

        MapDocument document = harness.ViewModel.Session.Document;
        document.SetLayer(1, 1, 0, new MapTileLayer(2, 20));
        harness.ViewModel.Brush = new MapTileLayer(1, 10);
        harness.ViewModel.ActiveTool = MapEditTool.Eyedropper;
        int paletteRefreshes = 0;
        harness.ViewModel.PaletteInvalidated += () => paletteRefreshes++;
        Point tile = new(2 * Cell - Cell / 2, 2 * Cell - Cell / 2);

        harness.Window.MouseDown(tile, MouseButton.Left, RawInputModifiers.None);

        Assert.Equal(new MapTileLayer(2, 20), harness.ViewModel.Brush);
        Assert.Equal(2, harness.ViewModel.SelectedSheet);
        Assert.True(paletteRefreshes > 0);

        harness.Window.MouseUp(tile, MouseButton.Left, RawInputModifiers.None);

        Assert.Equal(new MapTileLayer(2, 20), harness.ViewModel.Brush);
        Assert.Equal(2, harness.ViewModel.SelectedSheet);
    }

    [AvaloniaFact]
    public async Task EyedropperEscape_RestoresThePaletteSelection()
    {
        Harness harness = CreateSmallMapAsync();
        using AssetFixture fixture = new();
        fixture.WriteManifest(TwoSheetJson);
        fixture.WriteSheet(1, 64, 32);
        fixture.WriteSheet(2, 32, 32);
        Assert.True(harness.Assets.TryOpen(fixture.AssetDirectory));

        MapDocument document = harness.ViewModel.Session.Document;
        document.SetLayer(1, 1, 0, new MapTileLayer(2, 20));
        harness.ViewModel.Brush = new MapTileLayer(1, 10);
        harness.ViewModel.ActiveTool = MapEditTool.Eyedropper;
        harness.Canvas.Focus();
        Point tile = new(2 * Cell - Cell / 2, 2 * Cell - Cell / 2);

        harness.Window.MouseDown(tile, MouseButton.Left, RawInputModifiers.None);
        Assert.Equal(new MapTileLayer(2, 20), harness.ViewModel.Brush);
        Assert.Equal(2, harness.ViewModel.SelectedSheet);

        harness.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

        Assert.Equal(new MapTileLayer(1, 10), harness.ViewModel.Brush);
        Assert.Equal(1, harness.ViewModel.SelectedSheet);
        Assert.False(harness.ViewModel.Session.HasActiveStroke);
    }

    [AvaloniaFact]
    public async Task EyedropperPick_KeepsTheDisplayedSheetWhenThePickedSheetHasNoAssets()
    {
        Harness harness = CreateSmallMapAsync();
        using AssetFixture fixture = new();
        fixture.WriteManifest(TwoSheetJson);
        Assert.True(harness.Assets.TryOpen(fixture.AssetDirectory));

        MapDocument document = harness.ViewModel.Session.Document;
        document.SetLayer(1, 1, 0, new MapTileLayer(9, 9));
        harness.ViewModel.Brush = new MapTileLayer(1, 10);
        harness.ViewModel.ActiveTool = MapEditTool.Eyedropper;

        harness.Window.MouseDown(new Point(2 * Cell - Cell / 2, 2 * Cell - Cell / 2), MouseButton.Left, RawInputModifiers.None);

        Assert.Equal(new MapTileLayer(9, 9), harness.ViewModel.Brush);
        Assert.Equal(1, harness.ViewModel.SelectedSheet);
    }

    [AvaloniaFact]
    public async Task EscapeDuringStroke_CancelsAndRestores()
    {
        Harness harness = CreateSmallMapAsync();
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
        Harness harness = CreateSmallMapAsync();
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
        Harness harness = CreateSmallMapAsync();
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
        Harness harness = CreateSmallMapAsync();
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
        Harness harness = CreateSmallMapAsync();
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
        Harness harness = CreateSmallMapAsync();
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
        Harness harness = CreateSmallMapAsync();
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
        Harness harness = CreateSmallMapAsync();
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
        Harness harness = CreateSmallMapAsync();
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
        Harness harness = CreateSmallMapAsync();
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
        Harness harness = CreateSmallMapAsync();
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
        Harness harness = CreateSmallMapAsync();
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
        Harness harness = CreateSmallMapAsync();
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
        Harness harness = CreateSmallMapAsync();
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
        Harness harness = CreateSmallMapAsync();
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
        Harness harness = CreateSmallMapAsync();
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
        Harness harness = CreateSmallMapAsync();
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
        Harness harness = CreateSmallMapAsync();
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
        Harness harness = CreateSmallMapAsync();
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
        Harness harness = CreateSmallMapAsync();
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
        Harness harness = CreateSmallMapAsync();
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
        Harness harness = CreateSmallMapAsync();
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
        Harness harness = CreateSmallMapAsync();
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
        Harness harness = CreateSmallMapAsync();
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
        Harness harness = CreateSmallMapAsync();
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
        Harness harness = CreateSmallMapAsync();
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
        Harness harness = CreateSmallMapAsync();
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
        return new Harness(canvas, viewModel, dialogs, window, assets);
    }

    [AvaloniaFact]
    public async Task RenderMap_HonorsLayerVisibilityMask()
    {
        Harness harness = CreateSmallMapAsync();
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
