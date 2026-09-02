using System;
using System.IO;
using MapEditor.Rendering;
using MapEditor.Rendering.Tests.Fakes;
using Xunit;

namespace MapEditor.Rendering.Tests;

public class SpriteAssetCacheTests
{
    private const string TwoSheetJson = """
        { "tileSize": 32, "sheets": {
          "1": { "10": [0, 0, 32, 32], "11": [32, 0, 32, 32], "12": [0, 32, 32, 32], "13": [48, 48, 32, 32] },
          "2": { "20": [0, 0, 32, 32], "21": [32, 32, 32, 32] }
        } }
        """;

    private const string EmptySheetJson = """{ "tileSize": 32, "sheets": { "5": {} } }""";

    [Fact]
    public void Open_ParsesManifestBeforeAnySheetLoad()
    {
        using Fixture fixture = new(TwoSheetJson);
        FakeSpriteSheetLoader loader = new();

        SpriteAssetCache cache = SpriteAssetCache.Open(fixture.AssetRoot, loader);

        Assert.Equal(Path.GetFullPath(fixture.AssetRoot), cache.AssetDirectory);
        Assert.Equal(new[] { 1, 2 }, cache.Manifest.SheetIds);
        Assert.Equal(6, cache.Manifest.Frames.Count);
        Assert.Equal(0, loader.CallCount);
        cache.Dispose();
    }

    [Fact]
    public void Resolve_EmptyGraphicDoesNotInspectManifestOrLoad()
    {
        using Fixture fixture = new("""{ "tileSize": 32, "sheets": {} }""");
        FakeSpriteSheetLoader loader = new();
        SpriteAssetCache cache = CreateCache(fixture, SpriteManifest.Parse("""{ "tileSize": 32, "sheets": {} }"""), loader);

        SpriteResolution resolution = cache.Resolve(new SpriteReference(5, 0));

        Assert.Equal(SpriteResolutionStatus.Empty, resolution.Status);
        Assert.Null(resolution.Image);
        Assert.Equal(0, loader.CallCount);
        cache.Dispose();
    }

    [Fact]
    public void Resolve_UnknownSheetAndGraphicDoNotLoad()
    {
        using Fixture fixture = new(TwoSheetJson);
        FakeSpriteSheetLoader loader = new();
        SpriteAssetCache cache = CreateCache(fixture, SpriteManifest.Parse(TwoSheetJson), loader);

        SpriteResolution unknownSheet = cache.Resolve(new SpriteReference(999, 10));
        SpriteResolution unknownGraphic = cache.Resolve(new SpriteReference(1, 999));

        Assert.Equal(SpriteResolutionStatus.UnknownSheet, unknownSheet.Status);
        Assert.Null(unknownSheet.Image);
        Assert.Equal(SpriteResolutionStatus.UnknownGraphic, unknownGraphic.Status);
        Assert.Null(unknownGraphic.Image);
        Assert.Equal(0, loader.CallCount);
        cache.Dispose();
    }

    [Fact]
    public void Resolve_UnknownGraphicInKnownEmptySheetDoesNotLoad()
    {
        using Fixture fixture = new(EmptySheetJson);
        FakeSpriteSheetLoader loader = new();
        SpriteAssetCache cache = CreateCache(fixture, SpriteManifest.Parse(EmptySheetJson), loader);

        SpriteResolution resolution = cache.Resolve(new SpriteReference(5, 7));

        Assert.Equal(SpriteResolutionStatus.UnknownGraphic, resolution.Status);
        Assert.Null(resolution.Image);
        Assert.Equal(0, loader.CallCount);
        cache.Dispose();
    }

    [Fact]
    public void Resolve_FirstKnownFrameLoadsExactNormalizedSheetPath()
    {
        using Fixture fixture = new(TwoSheetJson);
        FakeSpriteSheetLoader loader = new();
        string messyPath = Path.Combine(fixture.Root, "assets", ".", "sprites", "..", "sprites");
        SpriteAssetCache cache = new(messyPath, SpriteManifest.Parse(TwoSheetJson), loader);

        SpriteResolution resolution = cache.Resolve(new SpriteReference(1, 10));

        Assert.Equal(SpriteResolutionStatus.Ready, resolution.Status);
        Assert.Equal(1, loader.CallCount);
        Assert.Equal(Path.Combine(Path.GetFullPath(messyPath), "sheets", "1.png"), loader.LoadedPaths[0]);
        cache.Dispose();
    }

    [Fact]
    public void Resolve_TwoFramesOnOneSheetLoadOnceAndShareImage()
    {
        using Fixture fixture = new(TwoSheetJson);
        FakeSpriteSheetLoader loader = new();
        SpriteAssetCache cache = CreateCache(fixture, SpriteManifest.Parse(TwoSheetJson), loader);

        SpriteResolution first = cache.Resolve(new SpriteReference(1, 10));
        SpriteResolution second = cache.Resolve(new SpriteReference(1, 11));

        Assert.Equal(SpriteResolutionStatus.Ready, first.Status);
        Assert.Equal(SpriteResolutionStatus.Ready, second.Status);
        Assert.NotNull(first.Image);
        Assert.Same(first.Image, second.Image);
        Assert.Equal(1, loader.CallCount);
        cache.Dispose();
    }

    [Fact]
    public void Resolve_FramesOnDifferentSheetsLoadIndependently()
    {
        using Fixture fixture = new(TwoSheetJson);
        FakeSpriteSheetLoader loader = new();
        SpriteAssetCache cache = CreateCache(fixture, SpriteManifest.Parse(TwoSheetJson), loader);

        SpriteResolution sheetOne = cache.Resolve(new SpriteReference(1, 10));
        SpriteResolution sheetTwo = cache.Resolve(new SpriteReference(2, 20));

        Assert.Equal(SpriteResolutionStatus.Ready, sheetOne.Status);
        Assert.Equal(SpriteResolutionStatus.Ready, sheetTwo.Status);
        Assert.NotNull(sheetOne.Image);
        Assert.NotNull(sheetTwo.Image);
        Assert.NotSame(sheetOne.Image, sheetTwo.Image);
        Assert.Equal(2, loader.CallCount);
        cache.Dispose();
    }

    [Fact]
    public void Resolve_ReturnsManifestSourceRectUnchanged()
    {
        using Fixture fixture = new(TwoSheetJson);
        FakeSpriteSheetLoader loader = new();
        SpriteAssetCache cache = CreateCache(fixture, SpriteManifest.Parse(TwoSheetJson), loader);

        SpriteResolution resolution = cache.Resolve(new SpriteReference(1, 11));

        Assert.Equal(SpriteResolutionStatus.Ready, resolution.Status);
        Assert.Equal(new SpriteSourceRect(32, 0, 32, 32), resolution.SourceRect);
        cache.Dispose();
    }

    [Fact]
    public void Resolve_MissingSheetFileIsNegativelyCached()
    {
        using Fixture fixture = new(TwoSheetJson);
        FakeSpriteSheetLoader loader = new(_ => SpriteSheetLoadResult.Failure(SpriteSheetLoadStatus.NotFound, "no such file"));
        SpriteAssetCache cache = CreateCache(fixture, SpriteManifest.Parse(TwoSheetJson), loader);

        SpriteResolution first = cache.Resolve(new SpriteReference(1, 10));
        SpriteResolution second = cache.Resolve(new SpriteReference(1, 11));

        Assert.Equal(SpriteResolutionStatus.MissingSheetFile, first.Status);
        Assert.Equal(SpriteResolutionStatus.MissingSheetFile, second.Status);
        Assert.Null(first.Image);
        Assert.Null(second.Image);
        Assert.Equal(1, loader.CallCount);
        cache.Dispose();
    }

    [Fact]
    public void Resolve_InvalidAndUnreadableSheetAreNegativelyCachedAsLoadFailure()
    {
        using Fixture fixture = new(TwoSheetJson);
        FakeSpriteSheetLoader loader = new(path => path.EndsWith("1.png")
            ? SpriteSheetLoadResult.Failure(SpriteSheetLoadStatus.InvalidData, "corrupt pixels")
            : SpriteSheetLoadResult.Failure(SpriteSheetLoadStatus.Unreadable, "unreadable bytes"));
        SpriteAssetCache cache = CreateCache(fixture, SpriteManifest.Parse(TwoSheetJson), loader);

        SpriteResolution invalid = cache.Resolve(new SpriteReference(1, 10));
        SpriteResolution invalidAgain = cache.Resolve(new SpriteReference(1, 12));
        SpriteResolution unreadable = cache.Resolve(new SpriteReference(2, 20));

        Assert.Equal(SpriteResolutionStatus.SheetLoadFailed, invalid.Status);
        Assert.Equal(SpriteResolutionStatus.SheetLoadFailed, invalidAgain.Status);
        Assert.Equal(SpriteResolutionStatus.SheetLoadFailed, unreadable.Status);
        Assert.Equal(2, loader.CallCount);
        cache.Dispose();
    }

    [Fact]
    public void Resolve_FrameOutsideSheetIsCachedButOtherFrameCanResolve()
    {
        using Fixture fixture = new(TwoSheetJson);
        FakeSpriteSheetLoader loader = new();
        SpriteAssetCache cache = CreateCache(fixture, SpriteManifest.Parse(TwoSheetJson), loader);

        SpriteResolution outside = cache.Resolve(new SpriteReference(1, 13));
        SpriteResolution inside = cache.Resolve(new SpriteReference(1, 12));
        SpriteResolution outsideAgain = cache.Resolve(new SpriteReference(1, 13));

        Assert.Equal(SpriteResolutionStatus.FrameOutsideSheet, outside.Status);
        Assert.Null(outside.Image);
        Assert.Equal(SpriteResolutionStatus.Ready, inside.Status);
        Assert.NotNull(inside.Image);
        Assert.Equal(SpriteResolutionStatus.FrameOutsideSheet, outsideAgain.Status);
        Assert.Equal(1, loader.CallCount);
        cache.Dispose();
    }

    [Fact]
    public void Resolve_ExactRightAndBottomBoundsAreAccepted()
    {
        using Fixture fixture = new(TwoSheetJson);
        FakeSpriteSheetLoader loader = new();
        SpriteAssetCache cache = CreateCache(fixture, SpriteManifest.Parse(TwoSheetJson), loader);

        SpriteResolution resolution = cache.Resolve(new SpriteReference(2, 21));

        Assert.Equal(SpriteResolutionStatus.Ready, resolution.Status);
        Assert.NotNull(resolution.Image);
        Assert.Equal(new SpriteSourceRect(32, 32, 32, 32), resolution.SourceRect);
        cache.Dispose();
    }

    [Fact]
    public void Resolve_InvalidImageDimensionsDisposesImageAndCachesFailure()
    {
        using Fixture fixture = new(TwoSheetJson);
        FakeSpriteSheetImage badImage = new(0, 64);
        FakeSpriteSheetLoader loader = new(_ => SpriteSheetLoadResult.Success(badImage));
        SpriteAssetCache cache = CreateCache(fixture, SpriteManifest.Parse(TwoSheetJson), loader);

        SpriteResolution first = cache.Resolve(new SpriteReference(1, 10));
        SpriteResolution second = cache.Resolve(new SpriteReference(1, 11));

        Assert.Equal(SpriteResolutionStatus.SheetLoadFailed, first.Status);
        Assert.Equal(SpriteResolutionStatus.SheetLoadFailed, second.Status);
        Assert.Equal(1, loader.CallCount);
        Assert.Equal(1, badImage.DisposeCount);
        cache.Dispose();
    }

    [Fact]
    public void Resolve_InvalidImageDimensionsDoNotTouchDisposedImage()
    {
        using Fixture fixture = new(TwoSheetJson);
        DisposedThrowingImage image = new(0, 0);
        FakeSpriteSheetLoader loader = new(_ => SpriteSheetLoadResult.Success(image));
        SpriteAssetCache cache = CreateCache(fixture, SpriteManifest.Parse(TwoSheetJson), loader);

        SpriteResolution resolution = cache.Resolve(new SpriteReference(1, 10));

        Assert.Equal(SpriteResolutionStatus.SheetLoadFailed, resolution.Status);
        Assert.Equal(1, image.DisposeCount);
        cache.Dispose();
    }

    [Fact]
    public void Resolve_MalformedLoaderResultThrowsAndDisposesImproperImage()
    {
        using Fixture fixture = new(TwoSheetJson);
        SpriteManifest manifest = SpriteManifest.Parse(TwoSheetJson);

        FakeSpriteSheetLoader nullImageOnSuccess = new(_ => new SpriteSheetLoadResult(SpriteSheetLoadStatus.Success, null, null));
        using SpriteAssetCache nullImageCache = CreateCache(fixture, manifest, nullImageOnSuccess);
        Assert.Throws<InvalidOperationException>(() => nullImageCache.Resolve(new SpriteReference(1, 10)));
        Assert.Equal(1, nullImageOnSuccess.CallCount);

        FakeSpriteSheetImage strayImage = new(64, 64);
        FakeSpriteSheetLoader imageOnFailure = new(_ => new SpriteSheetLoadResult(SpriteSheetLoadStatus.Unreadable, strayImage, "bad"));
        using SpriteAssetCache imageOnFailureCache = CreateCache(fixture, manifest, imageOnFailure);
        Assert.Throws<InvalidOperationException>(() => imageOnFailureCache.Resolve(new SpriteReference(1, 10)));
        Assert.True(strayImage.Disposed);

        FakeSpriteSheetImage diagnosedSuccess = new(64, 64);
        FakeSpriteSheetLoader diagnosedSuccessLoader = new(_ => new SpriteSheetLoadResult(SpriteSheetLoadStatus.Success, diagnosedSuccess, "unexpected"));
        using SpriteAssetCache diagnosedSuccessCache = CreateCache(fixture, manifest, diagnosedSuccessLoader);
        Assert.Throws<InvalidOperationException>(() => diagnosedSuccessCache.Resolve(new SpriteReference(1, 10)));
        Assert.True(diagnosedSuccess.Disposed);

        FakeSpriteSheetLoader emptyDiagnostic = new(_ => new SpriteSheetLoadResult(SpriteSheetLoadStatus.NotFound, null, ""));
        using SpriteAssetCache emptyDiagnosticCache = CreateCache(fixture, manifest, emptyDiagnostic);
        Assert.Throws<InvalidOperationException>(() => emptyDiagnosticCache.Resolve(new SpriteReference(1, 10)));
    }

    [Fact]
    public void Resolve_DiagnosticsContainPathAndReference()
    {
        using Fixture fixture = new(TwoSheetJson);
        FakeSpriteSheetLoader loader = new(_ => SpriteSheetLoadResult.Failure(SpriteSheetLoadStatus.NotFound, "no such file"));
        SpriteAssetCache cache = CreateCache(fixture, SpriteManifest.Parse(TwoSheetJson), loader);
        string sheetPath = Path.Combine(fixture.AssetRoot, "sheets", "1.png");

        SpriteResolution missing = cache.Resolve(new SpriteReference(1, 10));
        SpriteResolution unknownSheet = cache.Resolve(new SpriteReference(999, 10));
        SpriteResolution unknownGraphic = cache.Resolve(new SpriteReference(1, 999));

        AssertSheetDiagnostic(missing, sheetPath, "1", "10");
        AssertSheetDiagnostic(unknownSheet, Path.Combine(fixture.AssetRoot, "sheets", "999.png"), "999", "10");
        AssertSheetDiagnostic(unknownGraphic, sheetPath, "1", "999");

        SpriteAssetCache loadingCache = CreateCache(fixture, SpriteManifest.Parse(TwoSheetJson), new FakeSpriteSheetLoader());
        SpriteResolution outside = loadingCache.Resolve(new SpriteReference(1, 13));
        AssertSheetDiagnostic(outside, sheetPath, "1", "13");
        loadingCache.Dispose();
        cache.Dispose();
    }

    [Fact]
    public void Dispose_DisposesEachSuccessfulSheetExactlyOnce()
    {
        using Fixture fixture = new(TwoSheetJson);
        FakeSpriteSheetLoader loader = new();
        SpriteAssetCache cache = CreateCache(fixture, SpriteManifest.Parse(TwoSheetJson), loader);

        SpriteResolution first = cache.Resolve(new SpriteReference(1, 10));
        SpriteResolution second = cache.Resolve(new SpriteReference(1, 11));
        SpriteResolution third = cache.Resolve(new SpriteReference(1, 12));
        SpriteResolution fourth = cache.Resolve(new SpriteReference(2, 20));
        SpriteResolution fifth = cache.Resolve(new SpriteReference(2, 21));

        FakeSpriteSheetImage sheetOne = (FakeSpriteSheetImage)first.Image!;
        FakeSpriteSheetImage sheetTwo = (FakeSpriteSheetImage)fourth.Image!;
        Assert.Same(sheetOne, second.Image);
        Assert.Same(sheetOne, third.Image);
        Assert.Same(sheetTwo, fifth.Image);

        cache.Dispose();

        Assert.Equal(1, sheetOne.DisposeCount);
        Assert.Equal(1, sheetTwo.DisposeCount);
    }

    [Fact]
    public void Dispose_DoesNotDisposeLoaderOrFailedResults()
    {
        using Fixture fixture = new(TwoSheetJson);
        FakeSpriteSheetLoader loader = new(path => path.EndsWith("1.png")
            ? SpriteSheetLoadResult.Failure(SpriteSheetLoadStatus.NotFound, "no such file")
            : SpriteSheetLoadResult.Success(new FakeSpriteSheetImage(64, 64)));
        SpriteAssetCache cache = CreateCache(fixture, SpriteManifest.Parse(TwoSheetJson), loader);

        SpriteResolution failed = cache.Resolve(new SpriteReference(1, 10));
        SpriteResolution ready = cache.Resolve(new SpriteReference(2, 20));
        FakeSpriteSheetImage owned = (FakeSpriteSheetImage)ready.Image!;

        Assert.Equal(SpriteResolutionStatus.MissingSheetFile, failed.Status);
        Assert.Null(failed.Image);

        cache.Dispose();

        Assert.Equal(1, owned.DisposeCount);

        int callCount = loader.CallCount;
        SpriteSheetLoadResult afterDispose = loader.Load("x");
        Assert.Equal(callCount + 1, loader.CallCount);
        Assert.Equal(SpriteSheetLoadStatus.Success, afterDispose.Status);
        Assert.NotNull(afterDispose.Image);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        using Fixture fixture = new(TwoSheetJson);
        FakeSpriteSheetLoader loader = new();
        SpriteAssetCache cache = CreateCache(fixture, SpriteManifest.Parse(TwoSheetJson), loader);
        SpriteResolution resolution = cache.Resolve(new SpriteReference(1, 10));
        FakeSpriteSheetImage image = (FakeSpriteSheetImage)resolution.Image!;

        cache.Dispose();
        cache.Dispose();

        Assert.Equal(1, image.DisposeCount);
    }

    [Fact]
    public void Dispose_RethrowsFirstFailureAfterDisposingRemainingImages()
    {
        using Fixture fixture = new(TwoSheetJson);
        FakeSpriteSheetLoader loader = new(path => SpriteSheetLoadResult.Success(
            path.EndsWith("1.png") ? new FakeSpriteSheetImage(64, 64) { ThrowOnDispose = true } : new FakeSpriteSheetImage(64, 64)));
        SpriteAssetCache cache = CreateCache(fixture, SpriteManifest.Parse(TwoSheetJson), loader);
        SpriteResolution first = cache.Resolve(new SpriteReference(1, 10));
        SpriteResolution second = cache.Resolve(new SpriteReference(2, 20));
        FakeSpriteSheetImage failing = (FakeSpriteSheetImage)first.Image!;
        FakeSpriteSheetImage surviving = (FakeSpriteSheetImage)second.Image!;

        Assert.Throws<InvalidOperationException>(() => cache.Dispose());

        Assert.Equal(1, failing.DisposeCount);
        Assert.Equal(1, surviving.DisposeCount);
        cache.Dispose();
    }

    [Fact]
    public void Resolve_AfterDisposeThrowsObjectDisposedExceptionWithoutLoaderCall()
    {
        using Fixture fixture = new(TwoSheetJson);
        FakeSpriteSheetLoader loader = new();
        SpriteAssetCache cache = CreateCache(fixture, SpriteManifest.Parse(TwoSheetJson), loader);

        SpriteResolution resolution = cache.Resolve(new SpriteReference(1, 10));
        int callCount = loader.CallCount;
        cache.Dispose();

        Assert.Throws<ObjectDisposedException>(() => cache.Resolve(new SpriteReference(1, 11)));
        Assert.Equal(callCount, loader.CallCount);
    }

    [Fact]
    public void FailedOpenDoesNotCreateCacheOrInvokeLoader()
    {
        using Fixture fixture = new(null);
        FakeSpriteSheetLoader loader = new();

        SpriteManifestException ex = Assert.Throws<SpriteManifestException>(() => SpriteAssetCache.Open(fixture.AssetRoot, loader));

        Assert.Equal(SpriteManifestError.ManifestNotFound, ex.Error);
        Assert.Equal(0, loader.CallCount);
    }

    [Fact]
    public void SeparateCachesRetryPriorFailureAndOwnSeparateImages()
    {
        using Fixture fixture = new(TwoSheetJson);
        SpriteManifest manifest = SpriteManifest.Parse(TwoSheetJson);

        FakeSpriteSheetLoader failingLoader = new(_ => SpriteSheetLoadResult.Failure(SpriteSheetLoadStatus.NotFound, "no such file"));
        SpriteAssetCache failingCache = CreateCache(fixture, manifest, failingLoader);
        SpriteResolution failed = failingCache.Resolve(new SpriteReference(1, 10));
        Assert.Equal(SpriteResolutionStatus.MissingSheetFile, failed.Status);
        failingCache.Dispose();

        FakeSpriteSheetLoader succeedingLoader = new();
        SpriteAssetCache succeedingCache = CreateCache(fixture, manifest, succeedingLoader);
        SpriteResolution ready = succeedingCache.Resolve(new SpriteReference(1, 10));
        Assert.Equal(SpriteResolutionStatus.Ready, ready.Status);
        Assert.NotNull(ready.Image);

        Assert.Equal(1, failingLoader.CallCount);
        Assert.Equal(1, succeedingLoader.CallCount);
        succeedingCache.Dispose();
    }

    [Fact]
    public void DisposingOldCacheDoesNotAffectImageOwnedByNewCache()
    {
        using Fixture fixture = new(TwoSheetJson);
        SpriteManifest manifest = SpriteManifest.Parse(TwoSheetJson);

        SpriteAssetCache oldCache = CreateCache(fixture, manifest, new FakeSpriteSheetLoader());
        SpriteAssetCache newCache = CreateCache(fixture, manifest, new FakeSpriteSheetLoader());
        FakeSpriteSheetImage oldImage = (FakeSpriteSheetImage)oldCache.Resolve(new SpriteReference(1, 10)).Image!;
        FakeSpriteSheetImage newImage = (FakeSpriteSheetImage)newCache.Resolve(new SpriteReference(1, 10)).Image!;

        Assert.NotSame(oldImage, newImage);

        oldCache.Dispose();

        Assert.True(oldImage.Disposed);
        Assert.False(newImage.Disposed);
        newCache.Dispose();
        Assert.True(newImage.Disposed);
    }

    private sealed class DisposedThrowingImage : ISpriteSheetImage
    {
        private readonly int _width;
        private readonly int _height;

        public DisposedThrowingImage(int width, int height)
        {
            _width = width;
            _height = height;
        }

        public int PixelWidth => Disposed ? throw new ObjectDisposedException(nameof(DisposedThrowingImage)) : _width;
        public int PixelHeight => Disposed ? throw new ObjectDisposedException(nameof(DisposedThrowingImage)) : _height;
        public int DisposeCount { get; private set; }
        public bool Disposed => DisposeCount > 0;

        public void Dispose() => DisposeCount++;
    }

    private static SpriteAssetCache CreateCache(Fixture fixture, SpriteManifest manifest, FakeSpriteSheetLoader loader)
        => new(fixture.AssetRoot, manifest, loader);

    private static void AssertSheetDiagnostic(SpriteResolution resolution, string sheetPath, string sheet, string graphic)
    {
        Assert.NotNull(resolution.Diagnostic);
        Assert.Contains(sheetPath, resolution.Diagnostic);
        Assert.Contains($"sheet {sheet}", resolution.Diagnostic);
        Assert.Contains($"graphic {graphic}", resolution.Diagnostic);
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture(string? manifestJson)
        {
            Root = Path.Combine(Path.GetTempPath(), "sprite-asset-cache-tests-" + Guid.NewGuid().ToString("N"));
            AssetRoot = Path.Combine(Root, "assets", "sprites");
            Directory.CreateDirectory(AssetRoot);
            if (manifestJson is not null)
            {
                File.WriteAllText(Path.Combine(AssetRoot, "manifest.json"), manifestJson);
            }
        }

        public string Root { get; }
        public string AssetRoot { get; }

        public void Dispose()
            => Directory.Delete(Root, recursive: true);
    }
}
