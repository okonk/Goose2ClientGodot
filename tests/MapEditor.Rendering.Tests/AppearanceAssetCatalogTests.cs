using System;
using System.IO;
using MapEditor.Rendering;
using MapEditor.Rendering.Tests.Fakes;
using Xunit;

namespace MapEditor.Rendering.Tests;

public class AppearanceAssetCatalogTests
{
    private const string MapManifestJson = """
        { "tileSize": 32, "sheets": {
          "1000": { "108760": [0, 0, 48, 64], "108761": [16, 0, 48, 64] },
          "1001": { "108900": [0, 0, 48, 64] },
          "1002": { "109000": [0, 0, 128, 64] }
        } }
        """;

    private const string AppearanceManifestJson = """
        { "version": 1, "parts": {
          "Body": { "1": { "noEquip": [1000, 108760], "equip": [1001, 108900] } },
          "Hair": { "2": { "equip": [1000, 108761] } },
          "Eyes": { "7": { "noEquip": [1000, 108760] } },
          "Chest": { "5": { "equip": [1002, 109000] } },
          "Legs": { "3": { "noEquip": [1000, 99999] } },
          "Feet": { "4": { "noEquip": [999, 1] } },
          "Hand": { "6": { "noEquip": [1000, 0] } }
        } }
        """;

    [Fact]
    public void TryResolve_IdleBodyStateResolvesNoEquipFrame()
    {
        using Fixture fixture = new(MapManifestJson, AppearanceManifestJson);
        FakeSpriteSheetLoader loader = new();
        AppearanceAssetCatalog catalog = CreateCatalog(fixture, loader);

        Assert.True(catalog.TryResolve(AppearancePartKind.Body, 1, 3, out SpriteResolution resolution));

        Assert.Equal(SpriteResolutionStatus.Ready, resolution.Status);
        Assert.Equal(new SpriteReference(1000, 108760), resolution.Reference);
        Assert.NotNull(resolution.Image);
        Assert.Null(resolution.Diagnostic);
        Assert.Equal(1, loader.CallCount);
        catalog.Cache.Dispose();
    }

    [Fact]
    public void TryResolve_OtherBodyStatesResolveEquipFrame()
    {
        using Fixture fixture = new(MapManifestJson, AppearanceManifestJson);
        FakeSpriteSheetLoader loader = new();
        AppearanceAssetCatalog catalog = CreateCatalog(fixture, loader);

        Assert.True(catalog.TryResolve(AppearancePartKind.Body, 1, 0, out SpriteResolution resolution));

        Assert.Equal(SpriteResolutionStatus.Ready, resolution.Status);
        Assert.Equal(new SpriteReference(1001, 108900), resolution.Reference);
        Assert.NotNull(resolution.Image);
        catalog.Cache.Dispose();
    }

    [Fact]
    public void TryResolve_MissingPreferredVariantFallsBackToTheOther()
    {
        using Fixture fixture = new(MapManifestJson, AppearanceManifestJson);
        FakeSpriteSheetLoader loader = new();
        AppearanceAssetCatalog catalog = CreateCatalog(fixture, loader);

        Assert.True(catalog.TryResolve(AppearancePartKind.Hair, 2, 3, out SpriteResolution resolution));

        Assert.Equal(SpriteResolutionStatus.Ready, resolution.Status);
        Assert.Equal(new SpriteReference(1000, 108761), resolution.Reference);
        catalog.Cache.Dispose();
    }

    [Fact]
    public void TryResolve_UnknownSemanticIdReportsDistinctDiagnostic()
    {
        using Fixture fixture = new(MapManifestJson, AppearanceManifestJson);
        FakeSpriteSheetLoader loader = new();
        AppearanceAssetCatalog catalog = CreateCatalog(fixture, loader);

        Assert.False(catalog.TryResolve(AppearancePartKind.Body, 99, 3, out SpriteResolution missing));

        Assert.Equal(SpriteResolutionStatus.UnknownGraphic, missing.Status);
        Assert.Null(missing.Image);
        Assert.NotNull(missing.Diagnostic);
        Assert.Contains("Body", missing.Diagnostic);
        Assert.Contains("99", missing.Diagnostic);
        Assert.Contains("appearance manifest", missing.Diagnostic);
        Assert.Equal(0, loader.CallCount);
        catalog.Cache.Dispose();
    }

    [Fact]
    public void TryResolve_UnknownFrameReportsCacheDiagnosticDistinctFromMissingPart()
    {
        using Fixture fixture = new(MapManifestJson, AppearanceManifestJson);
        FakeSpriteSheetLoader loader = new();
        AppearanceAssetCatalog catalog = CreateCatalog(fixture, loader);

        Assert.False(catalog.TryResolve(AppearancePartKind.Legs, 3, 3, out SpriteResolution unknownFrame));
        Assert.False(catalog.TryResolve(AppearancePartKind.Body, 99, 3, out SpriteResolution missingPart));

        Assert.Equal(SpriteResolutionStatus.UnknownGraphic, unknownFrame.Status);
        Assert.Null(unknownFrame.Image);
        Assert.NotNull(unknownFrame.Diagnostic);
        Assert.Contains("99999", unknownFrame.Diagnostic);
        Assert.Contains("sheets", unknownFrame.Diagnostic);
        Assert.NotEqual(missingPart.Diagnostic, unknownFrame.Diagnostic);
        Assert.Equal(0, loader.CallCount);
        catalog.Cache.Dispose();
    }

    [Fact]
    public void TryResolve_UnknownSheetReportsCacheDiagnostic()
    {
        using Fixture fixture = new(MapManifestJson, AppearanceManifestJson);
        FakeSpriteSheetLoader loader = new();
        AppearanceAssetCatalog catalog = CreateCatalog(fixture, loader);

        Assert.False(catalog.TryResolve(AppearancePartKind.Feet, 4, 3, out SpriteResolution resolution));

        Assert.Equal(SpriteResolutionStatus.UnknownSheet, resolution.Status);
        Assert.Null(resolution.Image);
        Assert.NotNull(resolution.Diagnostic);
        Assert.Contains("999", resolution.Diagnostic);
        Assert.Equal(0, loader.CallCount);
        catalog.Cache.Dispose();
    }

    [Fact]
    public void TryResolve_MissingSheetFileReportsCacheDiagnostic()
    {
        using Fixture fixture = new(MapManifestJson, AppearanceManifestJson);
        FakeSpriteSheetLoader loader = new(_ => SpriteSheetLoadResult.Failure(SpriteSheetLoadStatus.NotFound, "no such file"));
        AppearanceAssetCatalog catalog = CreateCatalog(fixture, loader);

        Assert.False(catalog.TryResolve(AppearancePartKind.Body, 1, 3, out SpriteResolution resolution));

        Assert.Equal(SpriteResolutionStatus.MissingSheetFile, resolution.Status);
        Assert.Null(resolution.Image);
        Assert.NotNull(resolution.Diagnostic);
        Assert.Equal(1, loader.CallCount);
        catalog.Cache.Dispose();
    }

    [Fact]
    public void TryResolve_SheetDecodeFailureReportsCacheDiagnostic()
    {
        using Fixture fixture = new(MapManifestJson, AppearanceManifestJson);
        FakeSpriteSheetLoader loader = new(_ => SpriteSheetLoadResult.Failure(SpriteSheetLoadStatus.InvalidData, "corrupt pixels"));
        AppearanceAssetCatalog catalog = CreateCatalog(fixture, loader);

        Assert.False(catalog.TryResolve(AppearancePartKind.Body, 1, 3, out SpriteResolution resolution));

        Assert.Equal(SpriteResolutionStatus.SheetLoadFailed, resolution.Status);
        Assert.Null(resolution.Image);
        Assert.NotNull(resolution.Diagnostic);
        catalog.Cache.Dispose();
    }

    [Fact]
    public void TryResolve_OutOfBoundsFrameReportsCacheDiagnostic()
    {
        using Fixture fixture = new(MapManifestJson, AppearanceManifestJson);
        FakeSpriteSheetLoader loader = new();
        AppearanceAssetCatalog catalog = CreateCatalog(fixture, loader);

        Assert.False(catalog.TryResolve(AppearancePartKind.Chest, 5, 0, out SpriteResolution resolution));

        Assert.Equal(SpriteResolutionStatus.FrameOutsideSheet, resolution.Status);
        Assert.Null(resolution.Image);
        Assert.NotNull(resolution.Diagnostic);
        Assert.Equal(1, loader.CallCount);
        catalog.Cache.Dispose();
    }

    [Fact]
    public void TryResolve_EmptyGraphicReportsEmpty()
    {
        using Fixture fixture = new(MapManifestJson, AppearanceManifestJson);
        FakeSpriteSheetLoader loader = new();
        AppearanceAssetCatalog catalog = CreateCatalog(fixture, loader);

        Assert.False(catalog.TryResolve(AppearancePartKind.Hand, 6, 3, out SpriteResolution resolution));

        Assert.Equal(SpriteResolutionStatus.Empty, resolution.Status);
        Assert.Null(resolution.Image);
        Assert.Equal(0, loader.CallCount);
        catalog.Cache.Dispose();
    }

    [Fact]
    public void TryResolve_UnavailableCacheReportsAssetsUnavailable()
    {
        SpriteAssetCache cache = SpriteAssetCache.CreateUnavailable();
        AppearanceAssetCatalog catalog = new(AppearanceManifest.Parse(AppearanceManifestJson), cache);

        Assert.False(catalog.TryResolve(AppearancePartKind.Body, 1, 3, out SpriteResolution resolution));

        Assert.Equal(SpriteResolutionStatus.AssetsUnavailable, resolution.Status);
        Assert.Null(resolution.Image);
        Assert.NotNull(resolution.Diagnostic);
        cache.Dispose();
    }

    [Fact]
    public void TryResolve_RepeatedReferencesShareOneSheetAndDisposeItOnce()
    {
        using Fixture fixture = new(MapManifestJson, AppearanceManifestJson);
        FakeSpriteSheetLoader loader = new();
        AppearanceAssetCatalog catalog = CreateCatalog(fixture, loader);

        Assert.True(catalog.TryResolve(AppearancePartKind.Body, 1, 3, out SpriteResolution body));
        Assert.True(catalog.TryResolve(AppearancePartKind.Eyes, 7, 3, out SpriteResolution eyes));

        Assert.Same(body.Image, eyes.Image);
        Assert.Equal(1, loader.CallCount);
        FakeSpriteSheetImage image = (FakeSpriteSheetImage)body.Image!;

        catalog.Cache.Dispose();

        Assert.Equal(1, image.DisposeCount);
    }

    [Fact]
    public void TryResolve_AfterCacheDisposeThrowsObjectDisposedException()
    {
        using Fixture fixture = new(MapManifestJson, AppearanceManifestJson);
        FakeSpriteSheetLoader loader = new();
        AppearanceAssetCatalog catalog = CreateCatalog(fixture, loader);
        catalog.TryResolve(AppearancePartKind.Body, 1, 3, out _);
        int callCount = loader.CallCount;
        catalog.Cache.Dispose();

        Assert.Throws<ObjectDisposedException>(() => catalog.TryResolve(AppearancePartKind.Body, 1, 3, out _));
        Assert.Equal(callCount, loader.CallCount);
    }

    [Fact]
    public void Constructor_RejectsNullArguments()
    {
        SpriteAssetCache cache = SpriteAssetCache.CreateUnavailable();
        AppearanceManifest manifest = AppearanceManifest.Parse(AppearanceManifestJson);

        Assert.Throws<ArgumentNullException>(() => new AppearanceAssetCatalog(null!, cache));
        Assert.Throws<ArgumentNullException>(() => new AppearanceAssetCatalog(manifest, null!));

        cache.Dispose();
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture(string mapManifestJson, string appearanceManifestJson)
        {
            AssetRoot = Path.Combine(Path.GetTempPath(), "appearance-asset-catalog-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(AssetRoot);
            File.WriteAllText(Path.Combine(AssetRoot, "manifest.json"), mapManifestJson);
            File.WriteAllText(Path.Combine(AssetRoot, "appearance-manifest.json"), appearanceManifestJson);
        }

        public string AssetRoot { get; }

        public void Dispose()
            => Directory.Delete(AssetRoot, recursive: true);
    }

    private static AppearanceAssetCatalog CreateCatalog(Fixture fixture, FakeSpriteSheetLoader loader)
        => new(
            AppearanceManifest.Parse(AppearanceManifestJson),
            new SpriteAssetCache(fixture.AssetRoot, SpriteManifest.Parse(MapManifestJson), loader));
}
