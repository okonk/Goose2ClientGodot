using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MapEditor.App.Dialogs;
using MapEditor.App.Documents;
using MapEditor.App.Rendering;
using MapEditor.App.Settings;
using MapEditor.App.ViewModels;
using MapEditor.Core;

namespace MapEditor.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = ComposeMainWindow().Window;
        }

        base.OnFrameworkInitializationCompleted();
    }

    internal static ComposedEditor ComposeMainWindow(IEditorDialogs? dialogs = null, AppSettingsStore? settings = null)
    {
        AppSettingsStore store = settings ?? new AppSettingsStore(SettingsPathResolver.Resolve());
        IEditorDialogs surface = dialogs ?? new EditorDialogsProxy();
        var controller = new EditorDocumentController(surface, new MapFileStore());
        var viewModel = new MapDocumentViewModel(controller);
        var assets = new AssetContextController(viewModel, store);
        var window = new MainWindow(surface, store, viewModel, assets);
        if (surface is EditorDialogsProxy proxy)
        {
            proxy.Target = new AvaloniaEditorDialogs(window);
        }

        return new ComposedEditor(window, surface, store, controller, viewModel, assets);
    }
}

internal sealed record ComposedEditor(
    MainWindow Window,
    IEditorDialogs Dialogs,
    AppSettingsStore Settings,
    EditorDocumentController Controller,
    MapDocumentViewModel ViewModel,
    AssetContextController Assets);
