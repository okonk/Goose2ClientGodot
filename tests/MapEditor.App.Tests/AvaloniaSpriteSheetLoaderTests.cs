using System;
using System.IO;
using System.Text;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using MapEditor.App.Rendering;
using MapEditor.App.Tests.Fixtures;
using MapEditor.Rendering;
using Xunit;

namespace MapEditor.App.Tests;

public class AvaloniaSpriteSheetLoaderTests
{
    [AvaloniaFact]
    public void Load_MissingPathReportsNotFoundWithoutImage()
    {
        using AssetFixture fixture = new();
        AvaloniaSpriteSheetLoader loader = new();

        SpriteSheetLoadResult result = loader.Load(Path.Combine(fixture.AssetDirectory, "sheets", "99.png"));

        Assert.Equal(SpriteSheetLoadStatus.NotFound, result.Status);
        Assert.Null(result.Image);
        Assert.False(string.IsNullOrEmpty(result.Diagnostic));
    }

    [AvaloniaFact]
    public void Load_CorruptPngReportsInvalidDataWithoutImage()
    {
        using AssetFixture fixture = new();
        fixture.WriteCorruptSheet(1);
        AvaloniaSpriteSheetLoader loader = new();

        SpriteSheetLoadResult result = loader.Load(Path.Combine(fixture.AssetDirectory, "sheets", "1.png"));

        Assert.Equal(SpriteSheetLoadStatus.InvalidData, result.Status);
        Assert.Null(result.Image);
        Assert.False(string.IsNullOrEmpty(result.Diagnostic));
    }

    [AvaloniaFact]
    public void Load_DirectoryPathReportsUnreadableWithoutImage()
    {
        using AssetFixture fixture = new();
        AvaloniaSpriteSheetLoader loader = new();

        SpriteSheetLoadResult result = loader.Load(Path.Combine(fixture.AssetDirectory, "sheets"));

        Assert.Equal(SpriteSheetLoadStatus.Unreadable, result.Status);
        Assert.Null(result.Image);
        Assert.False(string.IsNullOrEmpty(result.Diagnostic));
    }

    [AvaloniaFact]
    public void Load_ValidPngTransfersImageWithExactDimensions()
    {
        using AssetFixture fixture = new();
        fixture.WriteSheet(1, 64, 48);
        AvaloniaSpriteSheetLoader loader = new();

        SpriteSheetLoadResult result = loader.Load(Path.Combine(fixture.AssetDirectory, "sheets", "1.png"));

        Assert.Equal(SpriteSheetLoadStatus.Success, result.Status);
        Assert.Null(result.Diagnostic);
        AvaloniaSpriteSheetImage image = Assert.IsType<AvaloniaSpriteSheetImage>(result.Image);
        Assert.Equal(64, image.PixelWidth);
        Assert.Equal(48, image.PixelHeight);
        image.Dispose();
    }

    [AvaloniaFact]
    public void Load_SuccessImageIsDisposedExactlyOnceWithTheCache()
    {
        using AssetFixture fixture = new();
        fixture.WriteManifest("""{ "tileSize": 32, "sheets": { "1": { "1": [0, 0, 32, 32] } } }""");
        fixture.WriteSheet(1, 32, 32);
        SpriteAssetCache cache = SpriteAssetCache.Open(fixture.AssetDirectory, new AvaloniaSpriteSheetLoader());

        SpriteResolution resolution = cache.Resolve(new SpriteReference(1, 1));
        Assert.Equal(SpriteResolutionStatus.Ready, resolution.Status);
        AvaloniaSpriteSheetImage image = Assert.IsType<AvaloniaSpriteSheetImage>(resolution.Image);

        cache.Dispose();
        cache.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _ = image.PixelWidth);
    }

    [AvaloniaFact]
    public void Load_InvalidPngDimensionsAreRejectedByCacheAsLoadFailure()
    {
        using AssetFixture fixture = new();
        fixture.WriteManifest("""{ "tileSize": 32, "sheets": { "1": { "1": [0, 0, 32, 32] } } }""");
        fixture.WriteCorruptSheet(1);
        SpriteAssetCache cache = SpriteAssetCache.Open(fixture.AssetDirectory, new AvaloniaSpriteSheetLoader());

        SpriteResolution first = cache.Resolve(new SpriteReference(1, 1));

        Assert.Equal(SpriteResolutionStatus.SheetLoadFailed, first.Status);
        Assert.Null(first.Image);
        cache.Dispose();
    }
}
