using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
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

public class TerrainThumbnailControlTests : IDisposable
{
    private static readonly Guid GrassId = new("11111111-1111-1111-1111-111111111111");

    private readonly string _tempDirectory = Directory.CreateTempSubdirectory("terrain-thumbnail-").FullName;
    private readonly AppSettingsStore _settings;
    private readonly AssetContextController _controller;

    public TerrainThumbnailControlTests()
    {
        _settings = new(Path.Combine(_tempDirectory, "settings.json"));
        _controller = new(new WorkspaceViewModel(new FakeEditorDialogs(), new MapFileStore()), _settings);
    }

    public void Dispose()
    {
        _controller.Dispose();
        Directory.Delete(_tempDirectory, recursive: true);
    }

    private static Window Attach(TerrainThumbnailControl control)
    {
        var window = new Window
        {
            Width = 100,
            Height = 100,
            Content = control
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private string WriteAssetDirectory(string name, string terrainJson)
    {
        string directory = Path.Combine(_tempDirectory, name);
        Directory.CreateDirectory(Path.Combine(directory, "sheets"));
        File.WriteAllText(Path.Combine(directory, "manifest.json"), AssetFixture.ManifestJson);
        File.WriteAllBytes(Path.Combine(directory, "sheets", "1.png"), AssetFixture.PngSheet.Create(64, 64));
        File.WriteAllBytes(Path.Combine(directory, "sheets", "2.png"), AssetFixture.PngSheet.Create(64, 64));
        File.WriteAllText(Path.Combine(directory, TerrainAssetCatalog.FileName), terrainJson);
        return directory;
    }

    private void OpenAssets(string name, string terrainJson)
    {
        Assert.True(_controller.TryOpen(WriteAssetDirectory(name, terrainJson)));
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void NoRepresentative_DrawsPlaceholderWithoutBitmap()
    {
        TerrainThumbnailControl control = new(new TerrainChoice(GrassId, "Grass", new TerrainColor(1, 2, 3), null));
        Window window = Attach(control);

        var target = new RecordingMapDrawTarget();
        control.RenderPalette(target);

        Assert.Empty(target.Images);
        Assert.Single(target.Rectangles);
        Assert.Equal(2, target.Lines.Count);
        window.Close();
    }

    [AvaloniaFact]
    public void Representative_DrawsTheResolvedGraphicAndOwnsNoBitmap()
    {
        OpenAssets("valid", AssetFixture.TerrainCatalogJson);
        TerrainChoice choice = new(GrassId, "Grass", new TerrainColor(1, 2, 3),
            new TerrainGraphicDefinition(new TerrainGraphicReference(1, 10), new TerrainPattern(Center: GrassId)));
        TerrainThumbnailControl control = new(choice);
        control.Assets = _controller;
        Window window = Attach(control);

        var target = new RecordingMapDrawTarget();
        control.RenderPalette(target);

        RecordingMapDrawTarget.ImageDraw draw = Assert.Single(target.Images);
        Assert.Empty(target.Rectangles);
        Assert.Empty(target.Lines);
        SpriteResolution resolution = _controller.Current.Resolve(new SpriteReference(1, 10));
        AvaloniaSpriteSheetImage image = Assert.IsType<AvaloniaSpriteSheetImage>(resolution.Image);
        Assert.Same(image.Bitmap, draw.Image);
        Assert.Equal(new Rect(resolution.SourceRect.X, resolution.SourceRect.Y, resolution.SourceRect.Width, resolution.SourceRect.Height), draw.Source);
        Assert.Equal(new Rect(4, 4, 32, 32), draw.Destination);
        window.Close();
    }

    [AvaloniaFact]
    public void PublicationRedraws_WithoutReloading()
    {
        OpenAssets("valid", AssetFixture.TerrainCatalogJson);
        TerrainChoice choice = new(GrassId, "Grass", new TerrainColor(1, 2, 3),
            new TerrainGraphicDefinition(new TerrainGraphicReference(1, 10), new TerrainPattern(Center: GrassId)));
        TerrainThumbnailControl control = new(choice);
        control.Assets = _controller;
        Window window = Attach(control);

        OpenAssets("valid-b", AssetFixture.TerrainCatalogJson);

        var target = new RecordingMapDrawTarget();
        control.RenderPalette(target);
        Assert.Single(target.Images);
        window.Close();
    }

    [AvaloniaFact]
    public void DetachedControlStopsTrackingPublications()
    {
        OpenAssets("valid", AssetFixture.TerrainCatalogJson);
        TerrainChoice choice = new(GrassId, "Grass", new TerrainColor(1, 2, 3),
            new TerrainGraphicDefinition(new TerrainGraphicReference(1, 10), new TerrainPattern(Center: GrassId)));
        TerrainThumbnailControl control = new(choice);
        control.Assets = _controller;
        Window window = Attach(control);
        window.Content = null;
        Dispatcher.UIThread.RunJobs();

        OpenAssets("valid-c", AssetFixture.TerrainCatalogJson);

        var target = new RecordingMapDrawTarget();
        control.RenderPalette(target);
        Assert.Single(target.Images);
        window.Close();
    }
}
