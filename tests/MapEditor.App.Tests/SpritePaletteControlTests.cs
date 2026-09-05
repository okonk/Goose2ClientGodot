using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
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

public class SpritePaletteControlTests : IDisposable
{
    private const int CanvasWidth = 300;
    private const int CanvasHeight = 200;
    private const double Cell = SpritePaletteControl.CellSize;

    private readonly string _directory = Directory.CreateTempSubdirectory("map-editor-palette-").FullName;

    private sealed record Harness(
        SpritePaletteControl Palette,
        MapDocumentViewModel ViewModel,
        AssetContextController Assets,
        Window Window,
        ScrollBar Bar,
        CountingSpriteSheetLoader Loader,
        List<SpriteReference> Resolved);

    public void Dispose()
        => Directory.Delete(_directory, recursive: true);

    [AvaloniaFact]
    public async Task Render_DrawsOnlyVisibleRowsOfSelectedSheet()
    {
        Harness harness = await CreateAsync(Manifest((1, 60, 100), (2, 1, 99)));
        RecordingMapDrawTarget target = new();

        harness.Palette.RenderPalette(target);

        Assert.Equal(Enumerable.Range(100, 48).Select(g => new SpriteReference(1, g)), harness.Resolved);
        Assert.Equal(48, target.Rectangles.Count);
        Assert.Equal(96, target.Lines.Count);
        Assert.Empty(target.Images);

        harness.Palette.Offset = 280;
        harness.Resolved.Clear();
        harness.Palette.RenderPalette(target);

        Assert.Equal(Enumerable.Range(116, 44).Select(g => new SpriteReference(1, g)), harness.Resolved);
    }

    [AvaloniaFact]
    public async Task Render_ResolvesFramesInSortedGraphicOrder()
    {
        const string json = """
            { "tileSize": 32, "sheets": {
              "1": { "40": [0, 0, 32, 32], "10": [0, 0, 32, 32], "30": [0, 0, 32, 32], "20": [0, 0, 32, 32] },
              "2": { "99": [0, 0, 32, 32] }
            } }
            """;
        Harness harness = await CreateAsync(json);
        RecordingMapDrawTarget target = new();

        harness.Palette.RenderPalette(target);

        Assert.Equal(new[] { 10, 20, 30, 40 }, harness.Resolved.Select(r => r.Graphic).ToArray());
        Assert.All(harness.Resolved, reference => Assert.Equal(1, reference.Sheet));
    }

    [AvaloniaFact]
    public async Task LeftClick_SelectsExactBrushAfterScrolling()
    {
        Harness harness = await CreateAsync(Manifest((1, 60, 100)));
        byte[] before = MapCodec.Encode(harness.ViewModel.Session.Document);
        harness.Palette.Offset = 88;

        harness.Window.MouseDown(new Point(24, 24), MouseButton.Left, RawInputModifiers.None);

        Assert.Equal(new MapTileLayer(1, 124), harness.ViewModel.Brush);
        Assert.Equal(before, MapCodec.Encode(harness.ViewModel.Session.Document));
        Assert.False(harness.ViewModel.Session.CanUndo);
    }

    [AvaloniaFact]
    public async Task LeftClick_OnEmptyCellKeepsCurrentBrush()
    {
        Harness harness = await CreateAsync(Manifest((1, 61, 100)));
        harness.ViewModel.Brush = new MapTileLayer(1, 100);
        harness.Palette.Offset = 88;

        harness.Window.MouseDown(new Point(180, 168), MouseButton.Left, RawInputModifiers.None);

        Assert.Equal(new MapTileLayer(1, 100), harness.ViewModel.Brush);
    }

    [AvaloniaFact]
    public async Task WheelScrollsAndClampsAtBothEnds()
    {
        Harness harness = await CreateAsync(Manifest((1, 120, 100)));
        harness.Palette.Offset = 340;

        Assert.Equal(340, harness.Bar.Value);
        Assert.Equal(340, harness.Bar.Maximum);
        Assert.Equal(200, harness.Bar.ViewportSize);

        harness.Window.MouseWheel(new Point(24, 24), new Vector(0, 1), RawInputModifiers.None);
        Assert.Equal(232, harness.Palette.Offset);
        Assert.Equal(232, harness.Bar.Value);

        for (int i = 0; i < 5; i++)
        {
            harness.Window.MouseWheel(new Point(24, 24), new Vector(0, -1), RawInputModifiers.None);
        }

        Assert.Equal(340, harness.Palette.Offset);
        Assert.Equal(340, harness.Bar.Value);

        for (int i = 0; i < 10; i++)
        {
            harness.Window.MouseWheel(new Point(24, 24), new Vector(0, 1), RawInputModifiers.None);
        }

        Assert.Equal(0, harness.Palette.Offset);
        Assert.Equal(0, harness.Bar.Value);
    }

    [AvaloniaFact]
    public async Task ScrollBarValue_UpdatesOffset()
    {
        Harness harness = await CreateAsync(Manifest((1, 60, 100)));

        harness.Bar.Value = 50;

        Assert.Equal(50, harness.Palette.Offset);
    }

    [AvaloniaFact]
    public async Task SheetChange_ClampsOffsetAndExtent()
    {
        Harness harness = await CreateAsync(Manifest((1, 60, 100), (2, 12, 200)));
        harness.Palette.Offset = 280;

        harness.ViewModel.SelectedSheet = 2;

        Assert.Equal(0, harness.Palette.Offset);
        Assert.Equal(72, harness.Palette.ExtentHeight);
        Assert.Equal(12, harness.Palette.FrameCount);
    }

    [AvaloniaFact]
    public async Task Resize_RecomputesColumnsAndClampsOffset()
    {
        Harness harness = await CreateAsync(Manifest((1, 60, 100)));
        harness.Palette.Offset = 280;

        harness.Palette.Height = 240;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(48, harness.Palette.Offset);

        harness.Palette.Width = 100;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, harness.Palette.Columns);
        Assert.Equal(1080, harness.Palette.ExtentHeight);
        Assert.Equal(48, harness.Palette.Offset);
    }

    [AvaloniaFact]
    public async Task SheetChangeAndSelection_NeverMutateDocument()
    {
        Harness harness = await CreateAsync(Manifest((1, 60, 100), (2, 12, 200)));
        MapEditSession session = harness.ViewModel.Session;
        byte[] before = MapCodec.Encode(session.Document);
        bool dirtyBefore = session.IsDirty;
        bool undoBefore = session.CanUndo;
        bool redoBefore = session.CanRedo;

        harness.ViewModel.SelectedSheet = 2;
        harness.Window.MouseDown(new Point(24, 24), MouseButton.Left, RawInputModifiers.None);

        Assert.Equal(new MapTileLayer(2, 200), harness.ViewModel.Brush);
        Assert.Equal(before, MapCodec.Encode(session.Document));
        Assert.Equal(dirtyBefore, session.IsDirty);
        Assert.Equal(undoBefore, session.CanUndo);
        Assert.Equal(redoBefore, session.CanRedo);
    }

    [AvaloniaFact]
    public async Task UnavailableContext_RendersNothingAndResolvesNothing()
    {
        Harness harness = await CreateAsync(null);
        RecordingMapDrawTarget target = new();

        harness.Palette.RenderPalette(target);

        Assert.Empty(harness.Resolved);
        Assert.Empty(target.Images);
        Assert.Empty(target.Rectangles);
        Assert.Empty(target.Lines);
        Assert.Equal(0, harness.Palette.ExtentHeight);
        Assert.Equal(0, harness.Palette.FrameCount);
        Assert.Single(target.Clips);
        Assert.Equal(new Rect(0, 0, CanvasWidth, CanvasHeight), target.Clips[0]);
    }

    [AvaloniaFact]
    public async Task MissingSheetImage_DrawsConspicuousPlaceholderPerFrame()
    {
        Harness harness = await CreateAsync(
            Manifest((1, 2, 5)),
            loaderBehavior: path => SpriteSheetLoadResult.Failure(SpriteSheetLoadStatus.NotFound, "no file"));
        RecordingMapDrawTarget target = new();

        harness.Palette.RenderPalette(target);

        Assert.Equal(2, target.Rectangles.Count);
        Assert.Equal(new Rect(2, 2, 32, 32), target.Rectangles[0].Bounds);
        Assert.Equal(new Rect(38, 2, 32, 32), target.Rectangles[1].Bounds);
        foreach (RecordingMapDrawTarget.RectangleDraw rectangle in target.Rectangles)
        {
            Assert.Equal(Color.FromArgb(0xFF, 0x00, 0xFF, 0xCC), Assert.IsType<SolidColorBrush>(rectangle.Fill).Color);
            Pen stroke = Assert.IsType<Pen>(rectangle.Stroke);
            Assert.Equal(1.0, stroke.Thickness);
            Assert.Equal(Color.FromArgb(0xFF, 0xFF, 0x00, 0xFF), Assert.IsType<SolidColorBrush>(stroke.Brush).Color);
        }

        Assert.Equal(4, target.Lines.Count);
        Assert.Equal(new Point(2, 2), target.Lines[0].Start);
        Assert.Equal(new Point(34, 34), target.Lines[0].End);
        Assert.Equal(new Point(34, 2), target.Lines[1].Start);
        Assert.Equal(new Point(2, 34), target.Lines[1].End);
        Assert.Empty(target.Images);
    }

    [AvaloniaFact]
    public async Task ReadyFrame_DrawsImageWithExactSourceAndDestination()
    {
        using AssetFixture fixture = new();
        const string json = """
            { "tileSize": 32, "sheets": { "1": { "5": [16, 8, 32, 32] } } }
            """;
        Harness harness = await CreateAsync(
            json,
            sheetLoader: new AvaloniaSpriteSheetLoader(),
            prepareAssets: directory =>
            {
                Directory.CreateDirectory(Path.Combine(directory, "sheets"));
                File.WriteAllBytes(Path.Combine(directory, "sheets", "1.png"), AssetFixture.PngSheet.Create(64, 64));
            });
        RecordingMapDrawTarget target = new();

        harness.Palette.RenderPalette(target);

        Assert.Single(target.Images);
        RecordingMapDrawTarget.ImageDraw image = target.Images[0];
        Assert.Equal(new Rect(16, 8, 32, 32), image.Source);
        Assert.Equal(new Rect(2, 2, 32, 32), image.Destination);
        Assert.Equal(BitmapInterpolationMode.None, RenderOptions.GetBitmapInterpolationMode(harness.Palette));
    }

    [AvaloniaFact]
    public async Task Render_DrawsSelectionBoxAroundBrushFrame()
    {
        Harness harness = await CreateAsync(Manifest((1, 60, 100)));
        harness.ViewModel.Brush = new MapTileLayer(1, 105);
        RecordingMapDrawTarget target = new();

        harness.Palette.RenderPalette(target);

        RecordingMapDrawTarget.RectangleDraw selection = target.Rectangles[^1];
        Assert.Equal(new Rect(181, 1, 34, 34), selection.Bounds);
        SolidColorBrush fill = Assert.IsType<SolidColorBrush>(selection.Fill);
        Assert.Equal(0x00, fill.Color.A);
        Pen stroke = Assert.IsType<Pen>(selection.Stroke);
        Assert.Equal(2.0, stroke.Thickness);
        Assert.Equal(Color.FromArgb(0xFF, 0x33, 0x99, 0xFF), Assert.IsType<SolidColorBrush>(stroke.Brush).Color);
    }

    [AvaloniaFact]
    public async Task Render_OmitsSelectionBoxWhenBrushIsOnAnotherSheet()
    {
        Harness harness = await CreateAsync(Manifest((1, 60, 100), (2, 1, 99)));
        harness.ViewModel.Brush = new MapTileLayer(2, 99);
        RecordingMapDrawTarget target = new();

        harness.Palette.RenderPalette(target);

        Assert.Equal(48, target.Rectangles.Count);
        Assert.All(target.Rectangles, rectangle => Assert.NotNull(rectangle.Fill));
    }

    [AvaloniaFact]
    public async Task LargePalette_RenderCallsAreViewportBoundedAndSheetLoaderCalledOnce()
    {
        const int frameCount = 20000;
        Harness harness = await CreateAsync(Manifest((1, frameCount, 1)));
        int columns = harness.Palette.Columns;
        int bound = columns * ((int)Math.Ceiling(CanvasHeight / Cell) + 2);
        double maxOffset = harness.Palette.ExtentHeight - CanvasHeight;
        RecordingMapDrawTarget target = new();

        foreach (double offset in new[] { 0.0, maxOffset / 2, maxOffset })
        {
            harness.Palette.Offset = offset;
            harness.Resolved.Clear();
            harness.Palette.RenderPalette(target);
            Assert.True(
                harness.Resolved.Count <= bound,
                $"render at {offset} resolved {harness.Resolved.Count} frames, bound is {bound}");
        }

        Assert.Equal(1, harness.Loader.CallCount);
        Assert.Empty(harness.Palette.GetVisualChildren());
    }

    private async Task<Harness> CreateAsync(
        string? manifestJson,
        ISpriteSheetLoader? sheetLoader = null,
        Func<string, SpriteSheetLoadResult>? loaderBehavior = null,
        Action<string>? prepareAssets = null)
    {
        FakeEditorDialogs dialogs = new();
        EditorDocumentController documentController = new(dialogs, new MapFileStore());
        MapDocumentViewModel viewModel = new(documentController, new SharedTileClipboard());
        string settingsPath = Path.Combine(_directory, "settings.json");
        CountingSpriteSheetLoader countingLoader = new(
            loaderBehavior ?? (path => SpriteSheetLoadResult.Success(new CountingSpriteSheetImage(64, 64))));
        ISpriteSheetLoader loader = sheetLoader ?? countingLoader;
        AssetContextController assets = new(viewModel, new AppSettingsStore(settingsPath), path => AssetContext.Create(path, loader));
        List<SpriteReference> resolved = new();
        SpritePaletteControl palette = new(viewModel, assets, (context, reference) =>
        {
            resolved.Add(reference);
            return context.Resolve(reference);
        })
        {
            Width = CanvasWidth,
            Height = CanvasHeight,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        ScrollBar bar = new() { Orientation = Orientation.Vertical };
        Grid host = new();
        host.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(CanvasWidth, GridUnitType.Pixel)));
        host.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        Grid.SetColumn(palette, 0);
        Grid.SetColumn(bar, 1);
        host.Children.Add(palette);
        host.Children.Add(bar);
        Window window = new() { Content = host };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        if (manifestJson is not null)
        {
            string assetDirectory = Path.Combine(_directory, Guid.NewGuid().ToString("n"));
            Directory.CreateDirectory(assetDirectory);
            File.WriteAllText(Path.Combine(assetDirectory, "manifest.json"), manifestJson);
            prepareAssets?.Invoke(assetDirectory);
            Assert.True(assets.TryOpen(assetDirectory));
            palette.BindScrollBar(bar);
            Dispatcher.UIThread.RunJobs();
        }

        return new Harness(palette, viewModel, assets, window, bar, countingLoader, resolved);
    }

    private static string Manifest(params (int Sheet, int FrameCount, int StartGraphic)[] sheets)
    {
        StringBuilder json = new("""{ "tileSize": 32, "sheets": { """);
        for (int s = 0; s < sheets.Length; s++)
        {
            (int sheet, int frameCount, int startGraphic) = sheets[s];
            if (s > 0)
            {
                json.Append(',');
            }

            json.Append('"').Append(sheet).Append("\": { ");
            for (int i = 0; i < frameCount; i++)
            {
                if (i > 0)
                {
                    json.Append(',');
                }

                json.Append('"').Append(startGraphic + i).Append("\":[0,0,32,32]");
            }

            json.Append("} ");
        }

        json.Append("} }");
        return json.ToString();
    }
}
