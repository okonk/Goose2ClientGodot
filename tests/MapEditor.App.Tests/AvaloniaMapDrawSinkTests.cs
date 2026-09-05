using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using MapEditor.App.Controls;
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

public class AvaloniaMapDrawSinkTests
{
    private const byte FillAlpha = 0x80;
    private const byte FillRed = 0x11;
    private const byte FillGreen = 0x22;
    private const byte FillBlue = 0x33;
    private const byte StrokeAlpha = 0xFF;
    private const byte StrokeRed = 0xAA;
    private const byte StrokeGreen = 0xBB;
    private const byte StrokeBlue = 0xCC;

    [AvaloniaFact]
    public void DrawSprite_TransfersExactSourceAndDestinationRects()
    {
        using Bitmap bitmap = new(new MemoryStream(AssetFixture.PngSheet.Create(64, 64)));
        using AvaloniaSpriteSheetImage image = new(bitmap);
        RecordingMapDrawTarget target = new();
        AvaloniaMapDrawSink sink = new(target);
        SpriteDrawOperation operation = new(
            2,
            new MapTileCoordinate(3, 4),
            new SpriteReference(1, 7),
            image,
            new SpriteSourceRect(32, 64, 32, 32),
            new RenderRect(96, 128, 32, 32),
            SpriteSampling.NearestNeighbor);

        sink.DrawSprite(operation);

        Assert.Single(target.Images);
        RecordingMapDrawTarget.ImageDraw draw = target.Images[0];
        Assert.Same(bitmap, draw.Image);
        Assert.Equal(new Rect(32, 64, 32, 32), draw.Source);
        Assert.Equal(new Rect(96, 128, 32, 32), draw.Destination);
        Assert.Empty(target.Lines);
        Assert.Empty(target.Rectangles);
    }

    [AvaloniaFact]
    public void DrawSprite_ForeignImageIsRejectedWithoutDrawing()
    {
        CountingSpriteSheetImage foreign = new(32, 32);
        RecordingMapDrawTarget target = new();
        AvaloniaMapDrawSink sink = new(target);
        SpriteDrawOperation operation = new(
            0,
            new MapTileCoordinate(0, 0),
            new SpriteReference(1, 1),
            foreign,
            new SpriteSourceRect(0, 0, 32, 32),
            new RenderRect(0, 0, 32, 32),
            SpriteSampling.NearestNeighbor);

        Assert.Throws<ArgumentException>(() => sink.DrawSprite(operation));

        Assert.Empty(target.Images);
    }

    [AvaloniaFact]
    public void DrawPlaceholder_DrawsFillStrokeAndBothDiagonalsWithExactColors()
    {
        RecordingMapDrawTarget target = new();
        AvaloniaMapDrawSink sink = new(target);
        RenderColor fill = new(FillRed, FillGreen, FillBlue, FillAlpha);
        RenderColor stroke = new(StrokeRed, StrokeGreen, StrokeBlue, StrokeAlpha);
        PlaceholderDrawOperation operation = new(
            1,
            new MapTileCoordinate(1, 2),
            new SpriteReference(1, 5),
            SpriteResolutionStatus.AssetsUnavailable,
            new RenderRect(32, 64, 32, 32),
            fill,
            stroke,
            "unavailable");

        sink.DrawPlaceholder(operation);

        Assert.Single(target.Rectangles);
        RecordingMapDrawTarget.RectangleDraw rectangle = target.Rectangles[0];
        Assert.Equal(new Rect(32, 64, 32, 32), rectangle.Bounds);
        Assert.Equal(Color.FromArgb(FillAlpha, FillRed, FillGreen, FillBlue), Assert.IsType<SolidColorBrush>(rectangle.Fill).Color);
        Pen rectangleStroke = Assert.IsType<Pen>(rectangle.Stroke);
        Assert.Equal(Color.FromArgb(StrokeAlpha, StrokeRed, StrokeGreen, StrokeBlue), Assert.IsType<SolidColorBrush>(rectangleStroke.Brush).Color);
        Assert.Equal(1.0, rectangleStroke.Thickness);

        Assert.Equal(2, target.Lines.Count);
        Assert.Equal(new Point(32, 64), target.Lines[0].Start);
        Assert.Equal(new Point(64, 96), target.Lines[0].End);
        Assert.Equal(new Point(64, 64), target.Lines[1].Start);
        Assert.Equal(new Point(32, 96), target.Lines[1].End);
        foreach (RecordingMapDrawTarget.LineDraw line in target.Lines)
        {
            Assert.Equal(Color.FromArgb(StrokeAlpha, StrokeRed, StrokeGreen, StrokeBlue), Assert.IsType<SolidColorBrush>(line.Pen.Brush).Color);
        }

        Assert.Empty(target.Images);
    }

    [AvaloniaFact]
    public void DrawCellOverlay_DrawsFillAndStrokeExactlyAsReceived()
    {
        RecordingMapDrawTarget target = new();
        AvaloniaMapDrawSink sink = new(target);
        RenderColor fill = new(0x00, 0x00, 0x60, 0x40);
        RenderColor stroke = new(0xFF, 0xFF, 0xFF, 0x00);
        CellOverlayDrawOperation operation = new(
            CellOverlayKind.Blocked,
            new MapTileCoordinate(2, 3),
            new RenderRect(64, 96, 32, 32),
            fill,
            stroke);

        sink.DrawCellOverlay(operation);

        Assert.Single(target.Rectangles);
        RecordingMapDrawTarget.RectangleDraw rectangle = target.Rectangles[0];
        Assert.Equal(new Rect(64, 96, 32, 32), rectangle.Bounds);
        Assert.Equal(Color.FromArgb(0x40, 0x00, 0x00, 0x60), Assert.IsType<SolidColorBrush>(rectangle.Fill).Color);
        Assert.Equal(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), Assert.IsType<SolidColorBrush>(Assert.IsType<Pen>(rectangle.Stroke).Brush).Color);
        Assert.Empty(target.Lines);
        Assert.Empty(target.Images);
    }

    [AvaloniaFact]
    public void DrawGridLine_DrawsLineWithExactColorAndEndpoints()
    {
        RecordingMapDrawTarget target = new();
        AvaloniaMapDrawSink sink = new(target);
        GridLineDrawOperation operation = new(new RenderPoint(0, 32), new RenderPoint(300, 32), new RenderColor(0xFF, 0xFF, 0x30, 0xFF));

        sink.DrawGridLine(operation);

        Assert.Single(target.Lines);
        RecordingMapDrawTarget.LineDraw line = target.Lines[0];
        Assert.Equal(new Point(0, 32), line.Start);
        Assert.Equal(new Point(300, 32), line.End);
        Assert.Equal(Color.FromArgb(0xFF, 0xFF, 0xFF, 0x30), Assert.IsType<SolidColorBrush>(line.Pen.Brush).Color);
        Assert.Empty(target.Rectangles);
        Assert.Empty(target.Images);
    }

    [AvaloniaFact]
    public void CanvasRender_PushesClipOfBoundsSize()
    {
        (MapCanvas canvas, _, _) = CreateCanvas();
        Dispatcher.UIThread.RunJobs();
        RecordingMapDrawTarget target = new();

        canvas.RenderMap(target);

        Assert.Single(target.Clips);
        Assert.Equal(new Rect(canvas.Bounds.Size), target.Clips[0]);
    }

    [AvaloniaFact]
    public void CanvasRender_UsesNearestNeighborBitmapInterpolation()
    {
        (MapCanvas canvas, _, _) = CreateCanvas();

        Assert.Equal(BitmapInterpolationMode.None, RenderOptions.GetBitmapInterpolationMode(canvas));
    }

    private static (MapCanvas Canvas, MapDocumentViewModel ViewModel, Window Window) CreateCanvas()
    {
        FakeEditorDialogs dialogs = new();
        EditorDocumentController documentController = new(dialogs, new MapFileStore(),
            new EditorDocument(new MapEditSession(MapDocument.Create(), initiallyDirty: false), null, null));
        MapDocumentViewModel viewModel = new(documentController, new SharedTileClipboard());
        string settingsPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "map-editor-sink-tests", "settings.json");
        AssetContextController assets = new(viewModel, new AppSettingsStore(settingsPath));
        MapCanvas canvas = new(viewModel, assets) { Width = 300, Height = 200 };
        Panel host = new() { Children = { canvas } };
        Window window = new() { Content = host };
        window.Show();
        return (canvas, viewModel, window);
    }
}
