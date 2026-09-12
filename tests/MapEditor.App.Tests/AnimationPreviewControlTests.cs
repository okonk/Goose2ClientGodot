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
    public void RenderPreview_DrawsCurrentFrameFromCacheAtNativeSize()
    {
        Harness harness = Create();
        RecordingMapDrawTarget target = new();

        harness.Control.RenderPreview(target);

        Assert.Single(target.Images);
        RecordingMapDrawTarget.ImageDraw image = target.Images[0];
        Assert.Equal(new Rect(0, 0, 16, 24), image.Source);
        Assert.Equal(new Rect(8, 8, 16, 24), image.Destination);
        Assert.Equal(BitmapInterpolationMode.None, RenderOptions.GetBitmapInterpolationMode(harness.Control));
        ISpriteSheetImage cached = harness.Context.Resolve(new SpriteReference(1, 10)).Image!;
        Assert.Same(((AvaloniaSpriteSheetImage)cached).Bitmap, image.Image);
    }

    [AvaloniaFact]
    public void RenderPreview_MixedFrameSizes_AnchorsBottomCenterInStableArea()
    {
        Harness harness = Create();
        RecordingMapDrawTarget target = new();

        harness.Control.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Assert.Equal(new Size(32, 32), harness.Control.DesiredSize);

        harness.Control.RenderPreview(target);
        Assert.Equal(new Rect(8, 8, 16, 24), target.Images[0].Destination);

        harness.ViewModel.StepNext();
        target.Images.Clear();
        harness.Control.RenderPreview(target);
        Assert.Equal(new Rect(0, 0, 24, 32), target.Images[0].Source);
        Assert.Equal(new Rect(4, 0, 24, 32), target.Images[0].Destination);

        harness.Control.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Assert.Equal(new Size(32, 32), harness.Control.DesiredSize);
    }

    [AvaloniaFact]
    public void RenderPreview_CrossSheetFrames_ResolveInOrder()
    {
        Harness harness = Create();
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
        string? seen = null;
        harness.Control.DiagnosticChanged += (_, _) => seen = harness.Control.Diagnostic;
        RecordingMapDrawTarget target = new();

        harness.Control.RenderPreview(target);

        Assert.Empty(target.Images);
        Assert.NotNull(harness.Control.Diagnostic);
        Assert.Equal(harness.Control.Diagnostic, seen);
        Assert.Single(target.Texts);
        Assert.Equal(harness.Control.Diagnostic, target.Texts[0].Text);
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
        RecordingMapDrawTarget target = new();

        harness.Control.RenderPreview(target);

        Assert.Empty(target.Images);
        Assert.Empty(target.Texts);
        Assert.Null(harness.Control.Diagnostic);
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
