using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using MapEditor.App.Connectivity;
using MapEditor.App.Dialogs;
using MapEditor.App.Settings;
using MapEditor.App.Tests.Fakes;
using MapEditor.Core;
using MapEditor.GameData.Google.Auth;
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
    public void ClassicDesktopInitialization_CreatesOneMainWindowWithCleanSessionAndUnavailableContext()
    {
        var app = new App();
        app.Initialize();
        var lifetime = new ClassicDesktopStyleApplicationLifetime();
        app.ApplicationLifetime = lifetime;

        app.OnFrameworkInitializationCompleted();

        var window = Assert.IsType<MainWindow>(lifetime.MainWindow);
        Assert.False(window.Workspace.ActiveDocument.Session.IsDirty);
        Assert.Equal("Goose2 Map Editor — Untitled", window.Workspace.ActiveDocument.Title);
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
        Assert.Same(composed.Workspace.ActiveDocument, composed.Window.Workspace.ActiveDocument);
        Assert.Same(composed.Assets, composed.Window.Assets);
        Assert.False(composed.Window.Workspace.ActiveDocument.Session.IsDirty);
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

        MapEditSession session = composed.Workspace.ActiveDocument.Session;
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
    public void Opened_AssetPickerThrows_ShowsLastErrorDialogAndStaysUsable()
    {
        var dialogs = new FakeEditorDialogs();
        dialogs.PickAssetDirectoryException = new InvalidOperationException("pick failed");
        var composed = App.ComposeMainWindow(dialogs, new AppSettingsStore(Path.Combine(_directory, "startup-pick.json")));

        composed.Window.Show();
        Dispatcher.UIThread.RunJobs();

        ErrorPresentation lastResort = Assert.Single(dialogs.Errors, error => error.Title == "Error");
        Assert.Contains("pick failed", lastResort.Message);
        Assert.True(composed.Window.IsVisible);
        Assert.False(composed.Assets.Current.IsAvailable);

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
        Assert.Equal(new[] { 1, 2 }, composed.Workspace.ActiveDocument.SheetIds);

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

    [AvaloniaFact]
    public void ComposedGraph_ExposesOneSharedConnectivityReadingTheSharedSettingsStore()
    {
        var dialogs = new FakeEditorDialogs();
        string settingsPath = Path.Combine(_directory, "shared-connectivity.json");
        var settings = new AppSettingsStore(settingsPath);
        settings.Save(new AppSettings(null, AppTheme.Dark, "https://docs.google.com/spreadsheets/d/shared123"));

        var composed = App.ComposeMainWindow(dialogs, settings);

        Assert.NotNull(composed.Connectivity);
        Assert.Same(composed.Connectivity, composed.Connectivity);
        Assert.Same(settings, composed.Settings);
        Assert.Same(settings, composed.Window.Settings);
        Assert.Same(composed.Workspace.ActiveDocument, composed.Window.Workspace.ActiveDocument);
        Assert.Same(composed.Assets, composed.Window.Assets);
        var remembered = composed.Connectivity.RememberedSpreadsheet;
        Assert.True(remembered.HasValue);
        Assert.Equal("shared123", remembered.Value.Id);
        Assert.Equal("https://docs.google.com/spreadsheets/d/shared123", remembered.Value.CanonicalUrl);
    }

    [AvaloniaFact]
    public void ComposedGraph_InjectsTheComposedConnectivityIntoTheWorkspace()
    {
        var dialogs = new FakeEditorDialogs();
        string settingsPath = Path.Combine(_directory, "workspace-connectivity.json");
        var settings = new AppSettingsStore(settingsPath);
        settings.Save(new AppSettings(null, AppTheme.Dark, "https://docs.google.com/spreadsheets/d/injected1"));

        var composed = App.ComposeMainWindow(dialogs, settings);

        var workspaceConnectivity = composed.Workspace.Connectivity;
        Assert.NotNull(workspaceConnectivity);
        var remembered = workspaceConnectivity.RememberedSpreadsheet;
        Assert.True(remembered.HasValue);
        Assert.Equal("injected1", remembered.Value.Id);
        Assert.True(workspaceConnectivity.TryRememberSpreadsheet("https://docs.google.com/spreadsheets/d/injected2"));
        Assert.Equal("https://docs.google.com/spreadsheets/d/injected2", composed.Connectivity.RememberedSpreadsheet!.Value.CanonicalUrl);
        Assert.NotNull(composed.Workspace.Commands);
        Assert.NotNull(composed.Workspace.ActiveDocument.GameData);
        Assert.False(composed.Workspace.ActiveDocument.GameData.HasSession);
    }

    [AvaloniaFact]
    public void ComposedGraph_WithRecordingFactories_StartsWithZeroConnectivityCalls()
    {
        var dialogs = new FakeEditorDialogs();
        var settings = new AppSettingsStore(Path.Combine(_directory, "lazy-connectivity.json"));
        int connectionCreations = 0;

        var composed = App.ComposeMainWindow(
            dialogs,
            settings,
            () =>
            {
                connectionCreations++;
                return new GoogleConnection(new GoogleOAuthClientConfig(null, Path.Combine(_directory, "publish")));
            },
            _ => throw new InvalidOperationException("gateway must not be created at startup"));

        Assert.NotNull(composed.Connectivity);
        Assert.Equal(0, connectionCreations);
        Assert.False(composed.Connectivity.IsConnected);
        Assert.Null(composed.Connectivity.Coordinator);
    }

    [AvaloniaFact]
    public void Connectivity_RetrievesRememberedSpreadsheetAndStoresCanonicalFormOnlyForValidInput()
    {
        string settingsPath = Path.Combine(_directory, "remembered.json");
        var store = new AppSettingsStore(settingsPath);
        store.Save(new AppSettings(null, AppTheme.Dark, "https://docs.google.com/spreadsheets/d/abc123"));

        var connectivity = new GameDataConnectivity(store);

        var remembered = connectivity.RememberedSpreadsheet;
        Assert.True(remembered.HasValue);
        Assert.Equal("abc123", remembered.Value.Id);
        Assert.Equal("https://docs.google.com/spreadsheets/d/abc123", remembered.Value.CanonicalUrl);

        Assert.True(connectivity.TryRememberSpreadsheet("https://docs.google.com/spreadsheets/d/abc123/edit#gid=0?usp=sharing"));
        Assert.Equal("https://docs.google.com/spreadsheets/d/abc123", store.Load().SpreadsheetUrl);

        string before = File.ReadAllText(settingsPath);
        Assert.False(connectivity.TryRememberSpreadsheet("not a spreadsheet url"));
        Assert.False(connectivity.TryRememberSpreadsheet(null));
        Assert.Equal(before, File.ReadAllText(settingsPath));
    }

    [AvaloniaFact]
    public async Task ComposedGraph_WithoutOAuthClientJson_StartsUpAndReportsOnlyOnConnect()
    {
        var dialogs = new FakeEditorDialogs();
        var settings = new AppSettingsStore(Path.Combine(_directory, "no-oauth.json"));

        var composed = App.ComposeMainWindow(dialogs, settings);

        Assert.NotNull(composed.Connectivity);
        Assert.False(composed.Connectivity.IsConnected);

        var lazy = new GameDataConnectivity(
            settings,
            () => throw new GoogleOAuthClientConfigurationException(
                "Google OAuth client JSON was not found at " + Path.Combine(_directory, "google-oauth-client.json")),
            _ => throw new InvalidOperationException("gateway must not be created without a successful connect"));
        Assert.False(lazy.IsConnected);
        Assert.Null(lazy.Coordinator);
        await Assert.ThrowsAsync<GoogleOAuthClientConfigurationException>(() => lazy.ConnectAsync());
    }
}
