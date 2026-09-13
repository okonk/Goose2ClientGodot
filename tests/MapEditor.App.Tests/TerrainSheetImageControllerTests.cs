using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Headless.XUnit;
using MapEditor.App.Rendering;
using MapEditor.App.Tests.Fakes;
using MapEditor.App.Tests.Fixtures;
using MapEditor.Rendering;
using Xunit;

namespace MapEditor.App.Tests;

public class TerrainSheetImageControllerTests
{
    private const string AssetDirectory = "/assets";

    private sealed record Harness(TerrainSheetImageController Controller, CountingSpriteSheetLoader Loader, List<CountingSpriteSheetImage> Images);

    private static Harness Create(CountingSpriteSheetLoader? loader = null)
    {
        List<CountingSpriteSheetImage> images = new();
        loader ??= new CountingSpriteSheetLoader(path =>
        {
            CountingSpriteSheetImage image = new(64, 64);
            images.Add(image);
            return SpriteSheetLoadResult.Success(image);
        });
        return new Harness(new TerrainSheetImageController(AssetDirectory, loader), loader, images);
    }

    private static string SheetPath(int sheetId) => Path.Combine(AssetDirectory, "sheets", $"{sheetId}.png");

    [Fact]
    public void SelectSheet_LoadsSheetPathAndExposesImage()
    {
        Harness harness = Create();

        harness.Controller.SelectSheet(1);

        Assert.Equal(new[] { SheetPath(1) }, harness.Loader.LoadedPaths);
        Assert.Same(harness.Images[0], harness.Controller.Image);
        Assert.Null(harness.Controller.Diagnostic);
        Assert.Equal(1, harness.Controller.SelectedSheet);
    }

    [Fact]
    public void SelectSheet_DisposesPreviousImageExactlyOnce()
    {
        Harness harness = Create();

        harness.Controller.SelectSheet(1);
        harness.Controller.SelectSheet(2);
        harness.Controller.SelectSheet(1);
        harness.Controller.SelectSheet(1);

        Assert.Equal(3, harness.Loader.CallCount);
        Assert.Equal(new[] { SheetPath(1), SheetPath(2), SheetPath(1) }, harness.Loader.LoadedPaths);
        Assert.Equal(1, harness.Images[0].DisposeCount);
        Assert.Equal(1, harness.Images[1].DisposeCount);
        Assert.Equal(0, harness.Images[2].DisposeCount);
        Assert.Same(harness.Images[2], harness.Controller.Image);
    }

    [Fact]
    public void SelectSheet_FailureReportsPathSpecificDiagnosticAndKeepsSelectionAvailable()
    {
        CountingSpriteSheetLoader loader = new(path => path == SheetPath(3)
            ? SpriteSheetLoadResult.Failure(SpriteSheetLoadStatus.NotFound, $"Sheet file not found: {path}.")
            : SpriteSheetLoadResult.Success(new CountingSpriteSheetImage(64, 64)));
        Harness harness = Create(loader);

        harness.Controller.SelectSheet(3);

        Assert.Null(harness.Controller.Image);
        Assert.Equal($"Sheet file not found: {SheetPath(3)}.", harness.Controller.Diagnostic);
        Assert.Equal(3, harness.Controller.SelectedSheet);

        harness.Controller.SelectSheet(1);

        Assert.NotNull(harness.Controller.Image);
        Assert.Null(harness.Controller.Diagnostic);
        Assert.Equal(1, harness.Controller.SelectedSheet);
    }

    [Fact]
    public void SelectSheet_MalformedSuccessResult_ReportsDiagnosticWithoutImage()
    {
        CountingSpriteSheetLoader loader = new(_ => new SpriteSheetLoadResult(SpriteSheetLoadStatus.Success, null, null));
        Harness harness = Create(loader);

        harness.Controller.SelectSheet(1);

        Assert.Null(harness.Controller.Image);
        Assert.Contains(SheetPath(1), harness.Controller.Diagnostic);
    }

    [Fact]
    public void SelectSheet_Null_ClearsImageWithoutDiagnostic()
    {
        Harness harness = Create();

        harness.Controller.SelectSheet(1);
        harness.Controller.SelectSheet(null);

        Assert.Null(harness.Controller.Image);
        Assert.Null(harness.Controller.Diagnostic);
        Assert.Null(harness.Controller.SelectedSheet);
        Assert.Equal(1, harness.Images[0].DisposeCount);
    }

    [Fact]
    public void ImageChanged_RaisesOnlyOnEffectiveSelectionChange()
    {
        Harness harness = Create();
        int raised = 0;
        harness.Controller.ImageChanged += (_, _) => raised++;

        harness.Controller.SelectSheet(1);
        harness.Controller.SelectSheet(1);
        harness.Controller.SelectSheet(2);

        Assert.Equal(2, raised);
    }

    [Fact]
    public void Dispose_DisposesCurrentImageExactlyOnceAndIsIdempotent()
    {
        Harness harness = Create();

        harness.Controller.SelectSheet(1);
        harness.Controller.Dispose();
        harness.Controller.Dispose();

        Assert.Equal(1, harness.Images[0].DisposeCount);
        Assert.Null(harness.Controller.Image);

        harness.Controller.SelectSheet(2);

        Assert.Equal(1, harness.Loader.CallCount);
        Assert.Null(harness.Controller.Image);
    }

    [AvaloniaFact]
    public void SelectSheet_RealLoader_MissingAndCorruptPaths_ProducePathSpecificDiagnostics()
    {
        using AssetFixture fixture = new();
        fixture.WriteSheet(1, 32, 32);
        fixture.WriteCorruptSheet(2);
        TerrainSheetImageController controller = new(fixture.AssetDirectory, new AvaloniaSpriteSheetLoader());

        controller.SelectSheet(9);

        Assert.Null(controller.Image);
        Assert.Equal($"Sheet file not found: {Path.Combine(fixture.AssetDirectory, "sheets", "9.png")}.", controller.Diagnostic);

        controller.SelectSheet(2);

        Assert.Null(controller.Image);
        Assert.Contains(Path.Combine(fixture.AssetDirectory, "sheets", "2.png"), controller.Diagnostic);

        controller.SelectSheet(1);

        Assert.NotNull(controller.Image);
        Assert.Null(controller.Diagnostic);
        controller.Dispose();
    }
}
