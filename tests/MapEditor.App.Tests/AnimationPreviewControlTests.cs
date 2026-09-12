using System;
using System.IO;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using MapEditor.App.Controls;
using MapEditor.App.Rendering;
using MapEditor.App.Tests.Fakes;
using MapEditor.App.Tests.Fixtures;
using MapEditor.App.ViewModels;
using MapEditor.Rendering;
using Xunit;

namespace MapEditor.App.Tests;

public class AnimationPreviewControlTests : IDisposable
{
    private const double Area = 220;
    private const int Scale = 6;

    private const string ManifestJson = """
        { "tileSize": 32, "sheets": {
          "1": { "10": [0, 0, 16, 24], "11": [0, 0, 32, 32] },
          "2": { "20": [0, 0, 24, 32] } } }
        """;

    private const string AnimationJson = """
        { "version": 1, "sheets": {}, "animations": [
          { "ownerSheet": 1, "id": 1, "fps": 8, "frames": [[1, 10], [2, 20]] } ] }
        """;

    private readonly string _directory = Directory.CreateTempSubdirectory("map-editor-preview-").FullName;

    private sealed record Harness(AnimationPreviewControl Control, GraphicViewerViewModel ViewModel, AssetContext Context, CountingSpriteSheetLoader Loader);

    public void Dispose()
        => Directory.Delete(_directory, recursive: true);

    [AvaloniaFact]
    public void RenderPreview_DrawsCurrentFrameScaledAndCentered()
    {
        Harness harness = Create();
        Arrange(harness);
        RecordingMapDrawTarget target = new();

        harness.Control.RenderPreview(target);

        Assert.Single(target.Images);
        RecordingMapDrawTarget.ImageDraw image = target.Images[0];
        Assert.Equal(new Rect(0, 0, 16, 24), image.Source);
        Assert.Equal(new Rect(62, 38, 16 * Scale, 24 * Scale), image.Destination);
        Assert.Equal(BitmapInterpolationMode.None, RenderOptions.GetBitmapInterpolationMode(harness.Control));
        ISpriteSheetImage cached = harness.Context.Resolve(new SpriteReference(1, 10)).Image!;
        Assert.Same(((AvaloniaSpriteSheetImage)cached).Bitmap, image.Image);
    }

    [AvaloniaFact]
    public void RenderPreview_MixedFrameSizes_UseTheSameScaleFromMaxFrameDimsAndCenterOnBothAxes()
    {
        Harness harness = Create();
        Arrange(harness);

        harness.Control.Measure(new Size(Area, Area));
        Assert.Equal(new Size(Area, Area), harness.Control.DesiredSize);

        var first = new RecordingMapDrawTarget();
        harness.Control.RenderPreview(first);
        Assert.Equal(new Rect(62, 38, 16 * Scale, 24 * Scale), first.Images[0].Destination);

        harness.ViewModel.StepNext();
        var second = new RecordingMapDrawTarget();
        harness.Control.RenderPreview(second);
        Assert.Equal(new Rect(0, 0, 24, 32), second.Images[0].Source);
        Assert.Equal(new Rect(38, 14, 24 * Scale, 32 * Scale), second.Images[0].Destination);

        harness.Control.Measure(new Size(Area, Area));
        Assert.Equal(new Size(Area, Area), harness.Control.DesiredSize);
    }

    [AvaloniaFact]
    public void RenderPreview_CrossSheetFrames_ResolveInOrder()
    {
        Harness harness = Create();
        Arrange(harness);
        RecordingMapDrawTarget target = new();

        harness.Control.RenderPreview(target);
        Assert.Single(harness.Loader.LoadedPaths);
        Assert.EndsWith("sheets/1.png", harness.Loader.LoadedPaths[0]);

        harness.ViewModel.StepNext();
        harness.Control.RenderPreview(target);
        Assert.Equal(2, harness.Loader.LoadedPaths.Count);
        Assert.EndsWith("sheets/2.png", harness.Loader.LoadedPaths[1]);
    }

    [AvaloniaFact]
    public void RenderPreview_MissingSheetImage_SurfacesDiagnosticWithoutThrowing()
    {
        Harness harness = Create(loaderBehavior: path => SpriteSheetLoadResult.Failure(SpriteSheetLoadStatus.NotFound, "no file"));
        Arrange(harness);
        string? seen = null;
        harness.Control.DiagnosticChanged += (_, _) => seen = harness.Control.Diagnostic;
        RecordingMapDrawTarget target = new();

        harness.Control.RenderPreview(target);

        Assert.Empty(target.Images);
        Assert.NotNull(harness.Control.Diagnostic);
        Assert.Equal(harness.Control.Diagnostic, seen);
        Assert.Single(target.Texts);
        Assert.Equal(harness.Control.Diagnostic, target.Texts[0].Text);
        Assert.Equal(new Point(Area / 2.0, Area / 2.0), target.Texts[0].Center);
    }

    [AvaloniaFact]
    public void RenderPreview_NeverDisposesResolutionImage()
    {
        CountingSpriteSheetImage sheet1 = new(64, 64);
        CountingSpriteSheetImage sheet2 = new(64, 64);
        Harness harness = Create(loaderBehavior: path =>
            path.EndsWith("1.png")
                ? SpriteSheetLoadResult.Success(sheet1)
                : SpriteSheetLoadResult.Success(sheet2));
        Arrange(harness);
        RecordingMapDrawTarget target = new();

        harness.Control.RenderPreview(target);
        harness.ViewModel.StepNext();
        harness.Control.RenderPreview(target);

        Assert.Equal(0, sheet1.DisposeCount);
        Assert.Equal(0, sheet2.DisposeCount);

        harness.Context.Dispose();

        Assert.Equal(1, sheet1.DisposeCount);
        Assert.Equal(1, sheet2.DisposeCount);
    }

    [AvaloniaFact]
    public void RenderPreview_WithoutCurrentFrame_DrawsNothingAndClearsDiagnostic()
    {
        Harness harness = Create(selectAnimation: false);
        Arrange(harness);
        RecordingMapDrawTarget target = new();

        harness.Control.RenderPreview(target);

        Assert.Empty(target.Images);
        Assert.Empty(target.Texts);
        Assert.Null(harness.Control.Diagnostic);
    }

    [AvaloniaFact]
    public void Assets_RetractedToTheReplacementContext_KeepsRenderingWithTheNewContext()
    {
        Harness first = Create();
        string secondDirectory = Path.Combine(_directory, Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(secondDirectory);
        File.WriteAllText(Path.Combine(secondDirectory, "manifest.json"), ManifestJson);
        File.WriteAllText(Path.Combine(secondDirectory, GraphicAnimationManifest.FileName), AnimationJson);
        CountingSpriteSheetLoader secondLoader = new(_ =>
            SpriteSheetLoadResult.Success(new AvaloniaSpriteSheetImage(new Bitmap(new MemoryStream(AssetFixture.PngSheet.Create(64, 64))))));
        AssetContext second = AssetContext.Create(secondDirectory, secondLoader);
        GraphicAssetCatalog secondCatalog = GraphicAssetCatalog.Create(second.Cache.Manifest!, GraphicAnimationManifest.Load(secondDirectory));
        first.ViewModel.Bind(second.Cache.Manifest!, secondCatalog);
        first.ViewModel.SelectAnimation(secondCatalog.GetAnimations(new SpriteReference(1, 10))[0]);

        first.Control.Assets = second;
        first.Context.Dispose();

        Arrange(first);
        RecordingMapDrawTarget target = new();
        first.Control.RenderPreview(target);

        Assert.Same(second, first.Control.Assets);
        Assert.Single(target.Images);
        Assert.Equal(new Rect(0, 0, 16, 24), target.Images[0].Source);
        Assert.Single(secondLoader.LoadedPaths);
    }

    private static void Arrange(Harness harness)
    {
        harness.Control.Measure(new Size(Area, Area));
        harness.Control.Arrange(new Rect(0, 0, Area, Area));
    }

    private Harness Create(Func<string, SpriteSheetLoadResult>? loaderBehavior = null, bool selectAnimation = true)
    {
        string assetDirectory = Path.Combine(_directory, Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(assetDirectory);
        File.WriteAllText(Path.Combine(assetDirectory, "manifest.json"), ManifestJson);
        File.WriteAllText(Path.Combine(assetDirectory, GraphicAnimationManifest.FileName), AnimationJson);
        CountingSpriteSheetLoader loader = new(loaderBehavior ?? (path =>
            SpriteSheetLoadResult.Success(new AvaloniaSpriteSheetImage(new Bitmap(new MemoryStream(AssetFixture.PngSheet.Create(64, 64)))))));
        AssetContext context = AssetContext.Create(assetDirectory, loader);
        SpriteManifest manifest = context.Cache.Manifest!;
        GraphicAssetCatalog catalog = GraphicAssetCatalog.Create(manifest, GraphicAnimationManifest.Load(assetDirectory));
        GraphicViewerViewModel viewModel = new(manifest, catalog);
        if (selectAnimation)
        {
            viewModel.SelectAnimation(catalog.GetAnimations(new SpriteReference(1, 10))[0]);
        }

        return new Harness(new AnimationPreviewControl(viewModel, context), viewModel, context, loader);
    }
}
