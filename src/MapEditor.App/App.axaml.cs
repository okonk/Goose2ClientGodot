using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Google.Apis.Auth.OAuth2;
using MapEditor.App.Connectivity;
using MapEditor.App.Dialogs;
using MapEditor.App.Documents;
using MapEditor.App.Rendering;
using MapEditor.App.Settings;
using MapEditor.App.Terrain;
using MapEditor.App.ViewModels;
using MapEditor.Core;
using MapEditor.GameData.Connectivity;
using MapEditor.GameData.Google.Auth;

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

    internal static ComposedEditor ComposeMainWindow(
        IEditorDialogs? dialogs = null,
        AppSettingsStore? settings = null,
        Func<GoogleConnection>? connectionFactory = null,
        Func<UserCredential, IGameDataGateway>? gatewayFactory = null,
        Func<AssetContext, TerrainEditorController>? terrainEditorFactory = null)
    {
        AppSettingsStore store = settings ?? new AppSettingsStore(SettingsPathResolver.Resolve());
        IEditorDialogs surface = dialogs ?? new EditorDialogsProxy();
        var connectivity = new GameDataConnectivity(store, connectionFactory, gatewayFactory);
        var workspace = new WorkspaceViewModel(surface, new MapFileStore(), new GameDataConnectivityBridge(connectivity));
        var assets = new AssetContextController(workspace, store);
        terrainEditorFactory ??= CreateTerrainEditorFactory(assets, surface);
        var window = new MainWindow(surface, store, workspace, assets, terrainEditorFactory);
        if (surface is EditorDialogsProxy proxy)
        {
            proxy.Target = new AvaloniaEditorDialogs(window);
        }

        return new ComposedEditor(window, surface, store, workspace, assets, connectivity);
    }

    private static Func<AssetContext, TerrainEditorController> CreateTerrainEditorFactory(
        AssetContextController assets,
        IEditorDialogs dialogs)
    {
        var store = new TerrainCatalogFileStore();
        return context => new TerrainEditorController(context, store, assets, dialogs, assets.Gate);
    }
}

internal sealed record ComposedEditor(
    MainWindow Window,
    IEditorDialogs Dialogs,
    AppSettingsStore Settings,
    WorkspaceViewModel Workspace,
    AssetContextController Assets,
    GameDataConnectivity Connectivity);
