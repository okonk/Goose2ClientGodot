using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using MapEditor.App.Dialogs;
using MapEditor.App.Settings;
using MapEditor.App.Tests.Fakes;
using MapEditor.Core;
using Xunit;

namespace MapEditor.App.Tests;

public class AppStartupTests : IDisposable
{
    private const string TwoSheetJson = """
        { "tileSize": 32, "sheets": {
          "1": { "10": [0, 0, 32, 32], "11": [32, 0, 32, 32] },
          "2": { "20": [0, 0, 32, 32] }
        } }
        """;

    private readonly string _directory = Directory.CreateTempSubdirectory("map-editor-startup-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string WriteAssetDirectory()
    {
        string directory = Path.Combine(_directory, "assets");
        Directory.CreateDirectory(Path.Combine(directory, "sheets"));
        File.WriteAllText(Path.Combine(directory, "manifest.json"), TwoSheetJson);
        return directory;
    }

    [AvaloniaFact]
    public void ClassicDesktopInitialization_CreatesOneMainWindowWithDirtySessionAndUnavailableContext()
    {
        var app = new App();
        app.Initialize();
        var lifetime = new ClassicDesktopStyleApplicationLifetime();
        app.ApplicationLifetime = lifetime;

        app.OnFrameworkInitializationCompleted();

        var window = Assert.IsType<MainWindow>(lifetime.MainWindow);
        Assert.True(window.ViewModel.Session.IsDirty);
        Assert.Equal("Goose2 Map Editor — Untitled*", window.ViewModel.Title);
        Assert.False(window.Assets.Current.IsAvailable);
    }

    [AvaloniaFact]
    public void ComposedGraph_SharesOneInstanceOfEveryPartWithTheWindow()
    {
        var dialogs = new FakeEditorDialogs();
        var settings = new AppSettingsStore(Path.Combine(_directory, "settings.json"));

        var composed = App.ComposeMainWindow(dialogs, settings);

        Assert.Same(composed.Dialogs, composed.Window.Dialogs);
        Assert.Same(composed.Settings, composed.Window.Settings);
        Assert.Same(composed.ViewModel, composed.Window.ViewModel);
        Assert.Same(composed.Assets, composed.Window.Assets);
        Assert.Same(composed.Controller.Document.Session, composed.Window.ViewModel.Session);
        Assert.True(composed.Window.ViewModel.Session.IsDirty);
        Assert.False(composed.Window.Assets.Current.IsAvailable);
    }

    [AvaloniaFact]
    public void Opened_WithoutUsableContext_OffersAssetPickerOnceAndStaysEditable()
    {
        var dialogs = new FakeEditorDialogs();
        var composed = App.ComposeMainWindow(dialogs, new AppSettingsStore(Path.Combine(_directory, "settings.json")));

        composed.Window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, dialogs.AssetDirectoryPickShown);
        Assert.False(composed.Assets.Current.IsAvailable);

        MapEditSession session = composed.ViewModel.Session;
        session.SelectedTileLayer = new MapTileLayer(1, 1);
        session.BeginStroke(MapEditTool.Pencil, 0, 0);
        Assert.True(session.CompleteStroke());
        Assert.Equal(new MapTileLayer(1, 1), session.Document[0, 0].GetLayer(0));

        dialogs.DirtyResult = DirtyChoice.Discard;
        composed.Window.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.False(composed.Window.IsVisible);
    }

    [AvaloniaFact]
    public void Opened_WithStoredAssetDirectory_LoadsItWithoutOfferingPicker()
    {
        var dialogs = new FakeEditorDialogs();
        string assetDirectory = WriteAssetDirectory();
        string settingsPath = Path.Combine(_directory, "stored.json");
        new AppSettingsStore(settingsPath).Save(new AppSettings(assetDirectory));
        var composed = App.ComposeMainWindow(dialogs, new AppSettingsStore(settingsPath));

        composed.Window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0, dialogs.AssetDirectoryPickShown);
        Assert.True(composed.Assets.Current.IsAvailable);
        Assert.Equal(new[] { 1, 2 }, composed.ViewModel.SheetIds);

        dialogs.DirtyResult = DirtyChoice.Discard;
        composed.Window.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.False(composed.Window.IsVisible);
    }

    [AvaloniaFact]
    public void Opened_WithMalformedSettings_ShowsSettingsErrorAndOffersPickerOnce()
    {
        var dialogs = new FakeEditorDialogs();
        string settingsPath = Path.Combine(_directory, "malformed.json");
        File.WriteAllText(settingsPath, "{ not json");
        var composed = App.ComposeMainWindow(dialogs, new AppSettingsStore(settingsPath));

        composed.Window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, dialogs.AssetDirectoryPickShown);
        Assert.False(composed.Assets.Current.IsAvailable);
        Assert.Contains(dialogs.Errors, error => error.Title == "Settings" && error.Message.Contains(settingsPath));

        dialogs.DirtyResult = DirtyChoice.Discard;
        composed.Window.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.False(composed.Window.IsVisible);
    }
}
