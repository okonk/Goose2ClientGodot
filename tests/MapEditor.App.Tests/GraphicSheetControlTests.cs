using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using MapEditor.App.Controls;
using MapEditor.App.Rendering;
using MapEditor.App.Tests.Fakes;
using MapEditor.App.Tests.Fixtures;
using MapEditor.App.ViewModels;
using MapEditor.Rendering;
using Xunit;

namespace MapEditor.App.Tests;

public class GraphicSheetControlTests
{
    private const string SidecarJson = """
        { "version": 1, "sheets": {}, "animations": [] }
        """;

    private const string SingleFrameJson = """
        { "tileSize": 32, "sheets": { "1": { "10": [0, 0, 32, 32] } } }
        """;

    private sealed record Harness(GraphicSheetControl Control, GraphicViewerViewModel ViewModel, Window Window, ScrollViewer? Scroller);

    [AvaloniaFact]
    public void RenderSheet_DrawsFullBitmapAtPixelSizeTimesZoom()
    {
        Harness harness = Create(SingleFrameJson, zoom: 1.0);
        RecordingMapDrawTarget target = new();

        harness.Control.RenderSheet(target);

        Assert.Single(target.Images);
        RecordingMapDrawTarget.ImageDraw image = target.Images[0];
        Assert.Equal(new Rect(0, 0, 64, 64), image.Source);
        Assert.Equal(new Rect(0, 0, 64, 64), image.Destination);
        Assert.Equal(BitmapInterpolationMode.None, RenderOptions.GetBitmapInterpolationMode(harness.Control));

        harness.ViewModel.Zoom = 2.5;
        target.Images.Clear();
        harness.Control.RenderSheet(target);

        Assert.Equal(new Rect(0, 0, 160, 160), target.Images[0].Destination);
    }

    [AvaloniaFact]
    public void RenderSheet_OutlinesEveryFrameAndDrawsDistinctSelectionLast()
    {
        const string json = """
            { "tileSize": 32, "sheets": { "1": {
              "10": [0, 0, 32, 32], "11": [32, 0, 32, 32], "12": [0, 32, 32, 32], "13": [32, 32, 32, 32] } } }
            """;
        Harness harness = Create(json, zoom: 2.0);
        harness.ViewModel.TrySelectFrameAt(16, 16);
        RecordingMapDrawTarget target = new();

        harness.Control.RenderSheet(target);

        Assert.Equal(5, target.Rectangles.Count);
        Assert.Equal(new Rect(0, 0, 64, 64), target.Rectangles[0].Bounds);
        Assert.Equal(new Rect(64, 0, 64, 64), target.Rectangles[1].Bounds);
        Assert.Equal(new Rect(0, 64, 64, 64), target.Rectangles[2].Bounds);
        Assert.Equal(new Rect(64, 64, 64, 64), target.Rectangles[3].Bounds);
        for (int i = 0; i < 4; i++)
        {
            RecordingMapDrawTarget.RectangleDraw outline = target.Rectangles[i];
            Assert.Equal(0x00, Assert.IsType<SolidColorBrush>(outline.Fill).Color.A);
            Pen stroke = Assert.IsType<Pen>(outline.Stroke);
            Assert.Equal(1.0, stroke.Thickness);
            Assert.True(Assert.IsType<SolidColorBrush>(stroke.Brush).Color.A < 0xFF);
        }

        RecordingMapDrawTarget.RectangleDraw selection = target.Rectangles[4];
        Assert.Equal(new Rect(0, 0, 64, 64), selection.Bounds);
        Pen selectionStroke = Assert.IsType<Pen>(selection.Stroke);
        Assert.Equal(2.0, selectionStroke.Thickness);
        Assert.NotEqual(
            Assert.IsType<SolidColorBrush>(Assert.IsType<Pen>(target.Rectangles[0].Stroke).Brush).Color,
            Assert.IsType<SolidColorBrush>(selectionStroke.Brush).Color);
    }

    [AvaloniaFact]
    public void RenderSheet_WithoutSelection_DrawsOnlyFrameOutlines()
    {
        const string json = """
            { "tileSize": 32, "sheets": { "1": { "10": [0, 0, 32, 32], "11": [32, 0, 32, 32] } } }
            """;
        Harness harness = Create(json);
        RecordingMapDrawTarget target = new();

        harness.Control.RenderSheet(target);

        Assert.Equal(2, target.Rectangles.Count);
        Assert.Equal(new Rect(0, 0, 32, 32), target.Rectangles[0].Bounds);
        Assert.Equal(new Rect(32, 0, 32, 32), target.Rectangles[1].Bounds);
    }

    [AvaloniaFact]
    public void DesiredSize_TracksImageAndZoom()
    {
        Harness harness = Create(SingleFrameJson);

        harness.Control.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Assert.Equal(new Size(64, 64), harness.Control.DesiredSize);

        harness.ViewModel.Zoom = 3.0;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new Size(192, 192), harness.Control.DesiredSize);

        harness.Control.Image = new AvaloniaSpriteSheetImage(new Bitmap(new MemoryStream(AssetFixture.PngSheet.Create(32, 48))));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new Size(96, 144), harness.Control.DesiredSize);
    }

    [AvaloniaFact]
    public void LeftClick_ConvertsThroughZoomAndSelectsFrame()
    {
        const string json = """
            { "tileSize": 32, "sheets": { "1": { "10": [0, 0, 32, 32], "11": [32, 0, 32, 32] } } }
            """;
        Harness harness = Create(json, zoom: 2.0);

        harness.Window.MouseDown(new Point(64, 8), MouseButton.Left, RawInputModifiers.None);

        Assert.NotNull(harness.ViewModel.SelectedFrame);
        Assert.Equal(11, harness.ViewModel.SelectedFrame.Value.Reference.Graphic);

        harness.Window.MouseDown(new Point(100, 100), MouseButton.Left, RawInputModifiers.None);

        Assert.Equal(11, harness.ViewModel.SelectedFrame.Value.Reference.Graphic);
    }

    [AvaloniaFact]
    public void CtrlWheel_ZoomsBoundedAndPlainWheel_LeavesScrollingToParent()
    {
        Harness harness = Create(SingleFrameJson, zoom: 4.0, inScrollViewer: true);
        ScrollViewer scroller = harness.Scroller!;
        scroller.Offset = new Vector(0, 100);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new Vector(0, 100), scroller.Offset);

        harness.Window.MouseWheel(new Point(50, 50), new Vector(0, -1), RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();

        Assert.True(harness.ViewModel.Zoom < 4.0);
        Assert.Equal(new Vector(0, 100), scroller.Offset);

        double zoomAfterCtrl = harness.ViewModel.Zoom;
        harness.Window.MouseWheel(new Point(50, 50), new Vector(0, 1), RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(zoomAfterCtrl, harness.ViewModel.Zoom);
        Assert.NotEqual(new Vector(0, 100), scroller.Offset);

        for (int i = 0; i < 30; i++)
        {
            harness.Window.MouseWheel(new Point(50, 50), new Vector(0, -1), RawInputModifiers.Control);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.Equal(GraphicViewerViewModel.MinZoom, harness.ViewModel.Zoom);
    }

    [AvaloniaFact]
    public void NoImageAndNoSheet_RenderAndInput_DoNotThrow()
    {
        GraphicViewerViewModel viewModel = new();
        GraphicSheetControl control = new(viewModel);

        control.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Assert.Equal(new Size(0, 0), control.DesiredSize);

        control.Width = 200;
        control.Height = 200;
        Window window = new() { Content = control, Width = 200, Height = 200 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        RecordingMapDrawTarget target = new();
        control.RenderSheet(target);
        Assert.Empty(target.Images);
        Assert.Empty(target.Rectangles);

        window.MouseDown(new Point(10, 10), MouseButton.Left, RawInputModifiers.None);
        window.MouseWheel(new Point(10, 10), new Vector(0, 1), RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();

        Assert.Null(viewModel.SelectedFrame);
    }

    private static Harness Create(string manifestJson, double zoom = 1.0, bool inScrollViewer = false)
    {
        SpriteManifest manifest = SpriteManifest.Parse(manifestJson);
        GraphicAssetCatalog catalog = GraphicAssetCatalog.Create(manifest, GraphicAnimationManifest.Parse(SidecarJson));
        GraphicViewerViewModel viewModel = new(manifest, catalog);
        viewModel.TrySelectSheet(manifest.SheetIds[0]);
        GraphicSheetControl control = new(viewModel);
        control.Image = new AvaloniaSpriteSheetImage(new Bitmap(new MemoryStream(AssetFixture.PngSheet.Create(64, 64))));
        viewModel.Zoom = zoom;
        Window window = new();
        ScrollViewer? scroller = null;
        if (inScrollViewer)
        {
            scroller = new ScrollViewer
            {
                Width = 100,
                Height = 100,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Visible,
                VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
                Content = control
            };
            window.Content = scroller;
            window.Width = 120;
            window.Height = 120;
        }
        else
        {
            window.Content = control;
            window.Width = 200;
            window.Height = 200;
        }

        window.Show();
        Dispatcher.UIThread.RunJobs();
        return new Harness(control, viewModel, window, scroller);
    }
}
