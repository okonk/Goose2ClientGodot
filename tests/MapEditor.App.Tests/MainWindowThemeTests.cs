using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using MapEditor.App.Dialogs;
using MapEditor.App.Documents;
using MapEditor.App.Rendering;
using MapEditor.App.Settings;
using MapEditor.App.Tests.Fakes;
using MapEditor.App.ViewModels;
using MapEditor.Core;
using Xunit;

namespace MapEditor.App.Tests;

public class MainWindowThemeTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("map-editor-theme-").FullName;
    private readonly FakeEditorDialogs _dialogs = new();
    private readonly AppSettingsStore _settings;
    private AssetContextController? _assets;
    private MainWindow? _window;

    public MainWindowThemeTests()
        => _settings = new AppSettingsStore(Path.Combine(_directory, "settings.json"));

    public void Dispose()
    {
        if (_window is { IsVisible: true } window)
        {
            _dialogs.DirtyResult = DirtyChoice.Discard;
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }

        _assets?.Dispose();
        // The variant is application-wide, so leave it as the app's default for other tests.
        if (Application.Current is { } application)
        {
            application.RequestedThemeVariant = ThemeVariant.Dark;
        }

        Directory.Delete(_directory, recursive: true);
    }

    [AvaloniaFact]
    public void NewWindow_WithoutSettings_StartsDark()
    {
        MainWindow window = CreateWindow();

        Assert.Equal(ThemeVariant.Dark, window.RequestedThemeVariant);
        Assert.True(Item(window, "DarkThemeMenuItem").IsChecked);
        Assert.False(Item(window, "LightThemeMenuItem").IsChecked);
    }

    [AvaloniaFact]
    public void NewWindow_RestoresThePersistedTheme()
    {
        _settings.Save(new AppSettings(null, AppTheme.Light));

        MainWindow window = CreateWindow();

        Assert.Equal(ThemeVariant.Light, window.RequestedThemeVariant);
        Assert.True(Item(window, "LightThemeMenuItem").IsChecked);
    }

    [AvaloniaFact]
    public void NewWindow_UnreadableSettings_StaysDark()
    {
        File.WriteAllText(Path.Combine(_directory, "settings.json"), "{ not json");

        MainWindow window = CreateWindow();

        Assert.Equal(ThemeVariant.Dark, window.RequestedThemeVariant);
    }

    [AvaloniaFact]
    public void ThemeMenu_SelectingLight_AppliesAndPersistsIt()
    {
        MainWindow window = CreateWindow();

        Click(window, "LightThemeMenuItem");

        Assert.Equal(ThemeVariant.Light, window.RequestedThemeVariant);
        Assert.Equal(ThemeVariant.Light, Application.Current?.RequestedThemeVariant);
        Assert.True(Item(window, "LightThemeMenuItem").IsChecked);
        Assert.False(Item(window, "DarkThemeMenuItem").IsChecked);
        Assert.Equal(AppTheme.Light, _settings.Load().Theme);
    }

    [AvaloniaFact]
    public void ThemeMenu_SwitchingBackToDark_PersistsIt()
    {
        MainWindow window = CreateWindow();

        Click(window, "LightThemeMenuItem");
        Click(window, "DarkThemeMenuItem");

        Assert.Equal(ThemeVariant.Dark, window.RequestedThemeVariant);
        Assert.Equal(AppTheme.Dark, _settings.Load().Theme);
    }

    [AvaloniaFact]
    public void ThemeMenu_ReselectingTheActiveTheme_KeepsItChecked()
    {
        MainWindow window = CreateWindow();

        Click(window, "DarkThemeMenuItem");

        Assert.Equal(ThemeVariant.Dark, window.RequestedThemeVariant);
        Assert.True(Item(window, "DarkThemeMenuItem").IsChecked);
    }

    [AvaloniaFact]
    public void ThemeMenu_SwitchingVariant_RepaintsThemedChrome()
    {
        MainWindow window = CreateWindow();
        Border toolbar = window.FindControl<Border>("Toolbar")!;
        Color dark = ((ISolidColorBrush)toolbar.Background!).Color;

        Click(window, "LightThemeMenuItem");

        Color light = ((ISolidColorBrush)toolbar.Background!).Color;
        Assert.NotEqual(dark, light);
        // The light palette's panel surface is the brighter of the two.
        Assert.True(light.R > dark.R);
    }

    private MainWindow CreateWindow()
    {
        var controller = new EditorDocumentController(_dialogs, new MapFileStore());
        var viewModel = new MapDocumentViewModel(controller);
        _assets = new AssetContextController(viewModel, _settings);
        _window = new MainWindow(_dialogs, _settings, viewModel, _assets);
        _window.Show();
        Dispatcher.UIThread.RunJobs();
        return _window;
    }

    private static MenuItem Item(MainWindow window, string name)
        => window.FindControl<MenuItem>(name)
           ?? throw new InvalidOperationException($"missing named region {name}");

    private static void Click(MainWindow window, string name)
    {
        Item(window, name).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }
}
