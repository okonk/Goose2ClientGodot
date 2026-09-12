using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using MapEditor.App;
using MapEditor.App.Dialogs;
using MapEditor.App.Rendering;
using MapEditor.App.Settings;
using MapEditor.App.Tests.Fakes;
using MapEditor.App.Tests.Fixtures;
using MapEditor.App.ViewModels;
using MapEditor.Core;
using MapEditor.Rendering;
using Xunit;

namespace MapEditor.App.Tests;

internal sealed class GraphicViewerWindowHarness : IDisposable
{
    public string TempDirectory { get; }

    public AppSettingsStore Settings { get; }

    public WorkspaceViewModel Workspace { get; }

    public AssetContextController Assets { get; }

    public ISpriteSheetLoader Loader { get; }

    public ManualPlaybackClock Clock { get; }

    public GraphicViewerWindow Window { get; }

    public GraphicViewerViewModel ViewModel => Window.ViewModel;

    private GraphicViewerWindowHarness(ISpriteSheetLoader? windowLoader)
    {
        TempDirectory = Directory.CreateTempSubdirectory("map-editor-viewer-").FullName;
        Settings = new AppSettingsStore(Path.Combine(TempDirectory, "settings.json"));
        Workspace = new WorkspaceViewModel(new FakeEditorDialogs(), new MapFileStore());
        Assets = new AssetContextController(Workspace, Settings, path => AssetContext.Create(path, new AvaloniaSpriteSheetLoader()));
        Loader = windowLoader ?? new SpySpriteSheetLoader();
        Clock = new ManualPlaybackClock();
        Window = new GraphicViewerWindow(Assets, Loader, Clock);
    }

    public static GraphicViewerWindowHarness Create(ISpriteSheetLoader? windowLoader = null)
    {
        var harness = new GraphicViewerWindowHarness(windowLoader);
        harness.Window.Show();
        Dispatcher.UIThread.RunJobs();
        return harness;
    }

    public void Dispose()
    {
        if (Window.IsVisible)
        {
            Window.Close();
            Dispatcher.UIThread.RunJobs();
        }

        Assets.Dispose();
        Directory.Delete(TempDirectory, recursive: true);
    }
}

public sealed class SpySpriteSheetLoader : ISpriteSheetLoader
{
    private readonly int _width;
    private readonly int _height;

    public SpySpriteSheetLoader(int width = 64, int height = 32)
    {
        _width = width;
        _height = height;
    }

    public List<string> LoadedPaths { get; } = new();

    public SpriteSheetLoadResult Load(string path)
    {
        LoadedPaths.Add(path);
        return SpriteSheetLoadResult.Success(new AvaloniaSpriteSheetImage(new Bitmap(new MemoryStream(AssetFixture.PngSheet.Create(_width, _height)))));
    }
}

public class GraphicViewerWindowTests
{
    private const string ManifestA = """
        { "tileSize": 32, "sheets": {
          "1": { "10": [0, 0, 32, 32], "11": [32, 0, 32, 32] },
          "2": { "20": [0, 0, 32, 32] } } }
        """;

    private const string AnimationA = """
        { "version": 1,
          "sheets": { "1": { "categories": [ { "name": "Body", "id": 1 } ] } },
          "animations": [
            { "ownerSheet": 1, "id": 10, "fps": 8, "frames": [[1, 10], [1, 11]] },
            { "ownerSheet": 1, "id": 11, "fps": 4, "frames": [[1, 10], [1, 11]] } ] }
        """;

    private const string InvalidAnimationA = """
        { "version": 1,
          "sheets": { "1": { "categories": [ { "name": "Body", "id": 1 } ] } },
          "animations": [ { "ownerSheet": 1, "id": 10, "fps": 8, "frames": [[1, 99]] } ] }
        """;

    private const string ManifestB = """
        { "tileSize": 32, "sheets": { "5": { "50": [0, 0, 32, 32], "51": [32, 0, 32, 32] } } }
        """;

    private const string AnimationB = """
        { "version": 1,
          "sheets": { "5": { "categories": [ { "name": "Tiles" } ] } },
          "animations": [ { "ownerSheet": 5, "id": 10, "fps": 8, "frames": [[5, 50], [5, 51]] } ] }
        """;

    private static T Find<T>(GraphicViewerWindowHarness harness, string name) where T : Control
        => harness.Window.FindControl<T>(name)
           ?? throw new InvalidOperationException($"missing named control {name}");

    private static string WriteAssetDirectory(GraphicViewerWindowHarness harness, string name, string manifestJson, string animationJson)
    {
        string directory = Path.Combine(harness.TempDirectory, name);
        Directory.CreateDirectory(Path.Combine(directory, "sheets"));
        File.WriteAllText(Path.Combine(directory, "manifest.json"), manifestJson);
        File.WriteAllText(Path.Combine(directory, GraphicAnimationManifest.FileName), animationJson);
        return directory;
    }

    private static void WritePng(string directory, int sheetId)
        => File.WriteAllBytes(Path.Combine(directory, "sheets", $"{sheetId}.png"), AssetFixture.PngSheet.Create(64, 32));

    private static void SetSheet(GraphicViewerWindowHarness harness, string text)
    {
        TextBox field = Find<TextBox>(harness, "SheetField");
        field.Text = text;
        field.RaiseEvent(new RoutedEventArgs(InputElement.LostFocusEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static GraphicViewerWindowHarness OpenAssetsA(ISpriteSheetLoader? windowLoader = null)
    {
        var harness = GraphicViewerWindowHarness.Create(windowLoader);
        string directory = WriteAssetDirectory(harness, "assets-a", ManifestA, AnimationA);
        WritePng(directory, 1);
        WritePng(directory, 2);
        Assert.True(harness.Assets.TryOpen(directory));
        Dispatcher.UIThread.RunJobs();
        return harness;
    }

    [AvaloniaFact]
    public void ManualClock_RaisesTicksOnlyWhileRunning()
    {
        var clock = new ManualPlaybackClock();
        int ticks = 0;
        clock.Tick += (_, _) => ticks++;

        clock.Start();
        Assert.True(clock.IsRunning);
        clock.Advance();
        clock.Stop();
        clock.Advance();

        Assert.False(clock.IsRunning);
        Assert.Equal(1, ticks);
    }

    [AvaloniaFact]
    public void ManualClock_DisposePreventsFurtherTicksAndStarts()
    {
        var clock = new ManualPlaybackClock();
        int ticks = 0;
        clock.Tick += (_, _) => ticks++;

        clock.Start();
        clock.Dispose();
        clock.Advance();
        clock.Start();
        clock.Advance();

        Assert.Equal(0, ticks);
        Assert.False(clock.IsRunning);
        Assert.Equal(1, clock.DisposeCount);
    }

    [AvaloniaFact]
    public void DispatcherClock_DisposeStopsTheTimer()
    {
        var clock = new DispatcherPlaybackClock();

        clock.Start();
        Assert.True(clock.IsRunning);

        clock.Dispose();
        Assert.False(clock.IsRunning);
    }

    [AvaloniaFact]
    public void InitialValidState_LoadsExactlyOneFullSheet()
    {
        using var harness = OpenAssetsA();
        var loader = (SpySpriteSheetLoader)harness.Loader;

        Assert.NotNull(harness.Window.SheetImage);
        Assert.Same(harness.Window.SheetImage, harness.Window.SheetControl.Image);
        Assert.Single(loader.LoadedPaths);
        Assert.EndsWith("sheets/1.png", loader.LoadedPaths[0]);
        Assert.False(harness.Clock.IsRunning);
        Assert.Equal("1", Find<TextBox>(harness, "SheetField").Text);
        Assert.Same(harness.ViewModel.Mappings, Find<ItemsControl>(harness, "MappingsList").ItemsSource);
        Assert.Single(harness.ViewModel.Mappings);
        Assert.Equal("available", Find<TextBlock>(harness, "AvailabilityText").Text);
    }

    [AvaloniaFact]
    public void SheetChange_StopsPlayback_DisposesOldImageAndLoadsOneReplacement()
    {
        using var harness = OpenAssetsA();
        var loader = (SpySpriteSheetLoader)harness.Loader;
        AvaloniaSpriteSheetImage first = harness.Window.SheetImage!;

        harness.ViewModel.TrySelectFrameAt(16, 16);
        Assert.True(harness.Clock.IsRunning);

        SetSheet(harness, "2");

        Assert.Equal(2, harness.ViewModel.SelectedSheetId);
        Assert.False(harness.Clock.IsRunning);
        Assert.Equal(1, first.DisposeCount);
        Assert.NotNull(harness.Window.SheetImage);
        Assert.NotSame(first, harness.Window.SheetImage);
        Assert.Same(harness.Window.SheetImage, harness.Window.SheetControl.Image);
        Assert.Equal(2, loader.LoadedPaths.Count);
        Assert.EndsWith("sheets/2.png", loader.LoadedPaths[1]);
    }

    [AvaloniaFact]
    public void ToolbarControls_PropagateToTheViewModel()
    {
        using var harness = OpenAssetsA();

        Find<ComboBox>(harness, "CategoryCombo").SelectedItem = GraphicViewerCategoryFilter.Body;
        Assert.Equal(GraphicViewerCategoryFilter.Body, harness.ViewModel.Category);

        Find<ComboBox>(harness, "ZoomCombo").SelectedItem = 2.0;
        Assert.Equal(2.0, harness.ViewModel.Zoom);

        harness.ViewModel.Zoom = 4.0;
        Find<Button>(harness, "Percent100Button").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1.0, harness.ViewModel.Zoom);

        double viewportWidth = harness.Window.SheetScroll.Viewport.Width;
        double viewportHeight = harness.Window.SheetScroll.Viewport.Height;
        Assert.True(viewportWidth > 0 && viewportHeight > 0);
        harness.ViewModel.Zoom = 4.0;
        Find<Button>(harness, "FitButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(
            Math.Clamp(Math.Min((viewportWidth - 32) / 64.0, (viewportHeight - 32) / 32.0), GraphicViewerViewModel.MinZoom, GraphicViewerViewModel.MaxZoom),
            harness.ViewModel.Zoom);
    }

    [AvaloniaFact]
    public void Fit_UsesTheRenderedSheetSizeNotTheFrameExtents()
    {
        using var harness = OpenAssetsA(new SpySpriteSheetLoader(96, 48));
        double viewportWidth = harness.Window.SheetScroll.Viewport.Width;
        double viewportHeight = harness.Window.SheetScroll.Viewport.Height;
        Assert.True(viewportWidth > 0 && viewportHeight > 0);

        harness.ViewModel.Zoom = 4.0;
        Find<Button>(harness, "FitButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        double expected = Math.Clamp(
            Math.Min((viewportWidth - 32) / 96.0, (viewportHeight - 32) / 48.0),
            GraphicViewerViewModel.MinZoom,
            GraphicViewerViewModel.MaxZoom);
        Assert.Equal(expected, harness.ViewModel.Zoom);
        Assert.True(harness.Window.SheetScroll.Extent.Width <= viewportWidth);
        Assert.True(harness.Window.SheetScroll.Extent.Height <= viewportHeight);
    }

    [AvaloniaFact]
    public void Fit_WithoutALoadedSheetImageIsANoOp()
    {
        using var harness = GraphicViewerWindowHarness.Create();
        Assert.Null(harness.Window.SheetImage);

        harness.ViewModel.Zoom = 4.0;
        Find<Button>(harness, "FitButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal(4.0, harness.ViewModel.Zoom);
    }

    [AvaloniaFact]
    public void AnimationSelectorAndPlaybackButtons_PropagateToTheViewModel()
    {
        using var harness = OpenAssetsA();
        harness.ViewModel.TrySelectFrameAt(16, 16);
        ComboBox animations = Find<ComboBox>(harness, "AnimationCombo");
        Assert.Equal(2, animations.Items.Count);

        animations.SelectedItem = harness.ViewModel.MatchingAnimations[1];
        Assert.Same(harness.ViewModel.MatchingAnimations[1], harness.ViewModel.SelectedAnimation);

        Find<Button>(harness, "NextButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, harness.ViewModel.FrameIndex);
        Find<Button>(harness, "NextButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(0, harness.ViewModel.FrameIndex);
        Find<Button>(harness, "PreviousButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, harness.ViewModel.FrameIndex);

        Find<Button>(harness, "PlayPauseButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(harness.ViewModel.IsPlaying);
        Find<Button>(harness, "PlayPauseButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(harness.ViewModel.IsPlaying);
    }

    [AvaloniaFact]
    public void SelectingGraphicWithMatchingAnimation_StartsTheClockWithoutPressingPlay()
    {
        using var harness = OpenAssetsA();
        Assert.False(harness.ViewModel.IsPlaying);
        Assert.False(harness.Clock.IsRunning);

        Assert.True(harness.ViewModel.TrySelectFrameAt(16, 16));

        Assert.True(harness.ViewModel.IsPlaying);
        Assert.True(harness.Clock.IsRunning);
        Assert.Equal(TimeSpan.FromMilliseconds(125), harness.Clock.Interval);
        Assert.Equal("10", Find<TextBlock>(harness, "GraphicIdText").Text);
        Assert.Contains("0, 0", Find<TextBlock>(harness, "SourceRectText").Text);
        Assert.Equal("1/2", Find<TextBlock>(harness, "FramePositionText").Text);
    }

    [AvaloniaFact]
    public void PlaybackPropertyChanges_SynchronizeClockStartStopAndInterval()
    {
        using var harness = OpenAssetsA();
        harness.ViewModel.TrySelectFrameAt(16, 16);
        Assert.True(harness.Clock.IsRunning);
        Assert.Equal(TimeSpan.FromMilliseconds(125), harness.Clock.Interval);

        harness.ViewModel.Pause();
        Assert.False(harness.Clock.IsRunning);

        harness.ViewModel.Play();
        Assert.True(harness.Clock.IsRunning);

        harness.ViewModel.SelectAnimation(harness.ViewModel.MatchingAnimations[1]);
        Assert.True(harness.Clock.IsRunning);
        Assert.Equal(TimeSpan.FromMilliseconds(250), harness.Clock.Interval);

        SetSheet(harness, "2");
        Assert.False(harness.Clock.IsRunning);
        Assert.False(harness.ViewModel.IsPlaying);
    }

    [AvaloniaFact]
    public void ClockTicks_WrapTheFramePosition()
    {
        using var harness = OpenAssetsA();
        harness.ViewModel.TrySelectFrameAt(16, 16);
        SpriteReference firstFrame = harness.ViewModel.CurrentFrame!.Value;

        harness.Clock.Advance();
        Assert.Equal(1, harness.ViewModel.FrameIndex);
        Assert.NotEqual(firstFrame, harness.ViewModel.CurrentFrame);

        harness.Clock.Advance();
        Assert.Equal(0, harness.ViewModel.FrameIndex);
        Assert.Equal(firstFrame, harness.ViewModel.CurrentFrame);

        harness.Clock.Advance();
        Assert.Equal(1, harness.ViewModel.FrameIndex);
    }

    [AvaloniaFact]
    public void MissingSheetPng_KeepsNavigationEnabledAndShowsTheLoaderDiagnostic()
    {
        using var harness = GraphicViewerWindowHarness.Create(windowLoader: new AvaloniaSpriteSheetLoader());
        string directory = WriteAssetDirectory(harness, "assets-missing", ManifestA, AnimationA);
        WritePng(directory, 2);
        Assert.True(harness.Assets.TryOpen(directory));
        Dispatcher.UIThread.RunJobs();

        TextBlock diagnostic = Find<TextBlock>(harness, "LoadDiagnosticText");
        Assert.Contains("not found", diagnostic.Text);
        Assert.Null(harness.Window.SheetImage);
        Assert.Null(harness.Window.SheetControl.Image);

        SetSheet(harness, "2");

        Assert.Equal(2, harness.ViewModel.SelectedSheetId);
        Assert.NotNull(harness.Window.SheetImage);
        Assert.Same(harness.Window.SheetImage, harness.Window.SheetControl.Image);
        Assert.Equal("—", diagnostic.Text);
    }

    [AvaloniaFact]
    public void CorruptSheetPng_ShowsTheLoaderDiagnostic()
    {
        using var harness = GraphicViewerWindowHarness.Create(windowLoader: new AvaloniaSpriteSheetLoader());
        string directory = WriteAssetDirectory(harness, "assets-corrupt", ManifestA, AnimationA);
        File.WriteAllText(Path.Combine(directory, "sheets", "1.png"), "this is not a png");
        Assert.True(harness.Assets.TryOpen(directory));
        Dispatcher.UIThread.RunJobs();

        Assert.Contains("not a decodable PNG", Find<TextBlock>(harness, "LoadDiagnosticText").Text);
        Assert.Null(harness.Window.SheetImage);
    }

    [AvaloniaFact]
    public void PreviewResolutionFailure_SurfacesTheDiagnosticInline()
    {
        using var harness = GraphicViewerWindowHarness.Create(windowLoader: new AvaloniaSpriteSheetLoader());
        string directory = WriteAssetDirectory(harness, "assets-nopng", ManifestA, AnimationA);
        Assert.True(harness.Assets.TryOpen(directory));
        Dispatcher.UIThread.RunJobs();

        harness.ViewModel.TrySelectFrameAt(16, 16);
        var target = new RecordingMapDrawTarget();
        harness.Window.PreviewControl.RenderPreview(target);
        Dispatcher.UIThread.RunJobs();

        TextBlock diagnostic = Find<TextBlock>(harness, "PreviewDiagnosticText");
        Assert.True(diagnostic.IsVisible);
        Assert.Equal(harness.Window.PreviewControl.Diagnostic, diagnostic.Text);
        Assert.Contains("not found", diagnostic.Text);
    }

    [AvaloniaFact]
    public void AssetReplacement_StopsClearsDisposesAndRendersWithTheNewContext()
    {
        using var harness = GraphicViewerWindowHarness.Create();
        var loader = (SpySpriteSheetLoader)harness.Loader;
        string directoryA = WriteAssetDirectory(harness, "assets-a", ManifestA, AnimationA);
        WritePng(directoryA, 1);
        Assert.True(harness.Assets.TryOpen(directoryA));
        Dispatcher.UIThread.RunJobs();

        AssetContext oldContext = harness.Assets.Current;
        AvaloniaSpriteSheetImage oldSheet = harness.Window.SheetImage!;
        harness.ViewModel.TrySelectFrameAt(16, 16);
        Assert.True(harness.Clock.IsRunning);

        string directoryB = WriteAssetDirectory(harness, "assets-b", ManifestB, AnimationB);
        WritePng(directoryB, 5);
        Assert.True(harness.Assets.TryOpen(directoryB));
        Dispatcher.UIThread.RunJobs();

        Assert.True(oldContext.IsDisposed);
        Assert.False(harness.Clock.IsRunning);
        Assert.Equal(1, oldSheet.DisposeCount);
        Assert.Equal(new[] { 5 }, harness.ViewModel.Sheets);
        Assert.NotNull(harness.Window.SheetImage);
        Assert.NotSame(oldSheet, harness.Window.SheetImage);
        Assert.Same(harness.Window.SheetImage, harness.Window.SheetControl.Image);
        Assert.EndsWith("sheets/5.png", loader.LoadedPaths[^1]);
        Assert.Same(harness.Assets.Current, harness.Window.PreviewControl.Assets);

        Assert.True(harness.ViewModel.TrySelectFrameAt(16, 16));
        Assert.True(harness.ViewModel.IsPlaying);
        var target = new RecordingMapDrawTarget();
        harness.Window.PreviewControl.RenderPreview(target);
        var image = Assert.Single(target.Images);
        Assert.Equal(new Rect(0, 0, 32, 32), image.Source);
    }

    [AvaloniaFact]
    public void InvalidReplacementMetadata_LeavesTheWindowOpenAndUnavailable()
    {
        using var harness = GraphicViewerWindowHarness.Create();
        var loader = (SpySpriteSheetLoader)harness.Loader;
        string directoryA = WriteAssetDirectory(harness, "assets-a", ManifestA, AnimationA);
        WritePng(directoryA, 1);
        Assert.True(harness.Assets.TryOpen(directoryA));
        Dispatcher.UIThread.RunJobs();

        AvaloniaSpriteSheetImage oldSheet = harness.Window.SheetImage!;
        int loadsBefore = loader.LoadedPaths.Count;

        string directoryB = WriteAssetDirectory(harness, "assets-b", ManifestA, InvalidAnimationA);
        WritePng(directoryB, 1);
        Assert.True(harness.Assets.TryOpen(directoryB));
        Dispatcher.UIThread.RunJobs();

        Assert.True(harness.Window.IsVisible);
        Assert.False(harness.ViewModel.IsAvailable);
        Assert.False(harness.Assets.Current.GraphicViewerAvailability.IsAvailable);
        Assert.Contains("missing from the sprite manifest", Find<TextBlock>(harness, "AvailabilityText").Text);
        Assert.Equal(1, oldSheet.DisposeCount);
        Assert.Equal(loadsBefore, loader.LoadedPaths.Count);
    }

    [AvaloniaFact]
    public void UnmodifiedSpace_TogglesPlayback_UnlessTheSheetFieldIsFocused()
    {
        using var harness = OpenAssetsA();
        harness.Window.SheetControl.Focus();
        harness.ViewModel.TrySelectFrameAt(16, 16);
        Assert.True(harness.Clock.IsRunning);

        harness.Window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        Assert.False(harness.ViewModel.IsPlaying);
        Assert.False(harness.Clock.IsRunning);

        harness.Window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        Assert.True(harness.ViewModel.IsPlaying);
        Assert.True(harness.Clock.IsRunning);

        Find<TextBox>(harness, "SheetField").Focus();
        harness.Window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        Assert.True(harness.ViewModel.IsPlaying);
        Assert.True(harness.Clock.IsRunning);
    }

    [AvaloniaFact]
    public void Close_StopsAndDisposesTheClockDetachesControlsAndDisposesTheSheetOnce()
    {
        using var harness = OpenAssetsA();
        var loader = (SpySpriteSheetLoader)harness.Loader;
        AvaloniaSpriteSheetImage sheet = harness.Window.SheetImage!;
        int loadsBefore = loader.LoadedPaths.Count;
        harness.ViewModel.TrySelectFrameAt(16, 16);
        Assert.True(harness.Clock.IsRunning);

        harness.Window.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.False(harness.Window.IsVisible);
        Assert.False(harness.Clock.IsRunning);
        Assert.Equal(1, harness.Clock.DisposeCount);
        Assert.Null(Find<Border>(harness, "SheetHost").Child);
        Assert.Null(Find<Border>(harness, "PreviewHost").Child);
        Assert.Equal(1, sheet.DisposeCount);

        string directoryB = WriteAssetDirectory(harness, "assets-b", ManifestB, AnimationB);
        WritePng(directoryB, 5);
        Assert.True(harness.Assets.TryOpen(directoryB));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(loadsBefore, loader.LoadedPaths.Count);
    }
}
