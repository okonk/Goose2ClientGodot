using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MapEditor.App.Connectivity;
using MapEditor.App.Controls;
using MapEditor.App.Dialogs;
using MapEditor.App.ViewModels;
using MapEditor.Core;
using MapEditor.GameData.Connectivity;
using MapEditor.GameData.Replacement;
using MapEditor.GameData.Rows;
using MapEditor.GameData.Sync;
using Xunit;

namespace MapEditor.App.Tests;

public class MainWindowGameDataTests
{
    private static readonly MapReference Map10 = new(10, "Dungeon", "dungeon.bytes");
    private static readonly MapReference Map20 = new(20, "Cave", "cave.bytes");
    private static readonly MapReference Map30 = new(30, "Tower", "tower.bytes");
    private static readonly IReadOnlyList<MapReference> Maps = new[] { Map10, Map20, Map30 };
    private static readonly string SheetUrl = "https://docs.google.com/spreadsheets/d/abc123";
    private static readonly NpcAppearance Npc1 = new(1, "Goose", 0, 0, new RgbaValue(255, 255, 255, 255), 0, 0, new RgbaValue(255, 255, 255, 255), string.Empty);
    private static readonly NpcAppearance Npc2 = new(2, "Duck", 0, 0, new RgbaValue(255, 255, 255, 255), 0, 0, new RgbaValue(255, 255, 255, 255), string.Empty);

    private static GameDataSyncSession Session()
    {
        var data = new RemoteGameData(
            Maps,
            new Dictionary<int, NpcAppearance> { [2] = Npc2, [1] = Npc1 },
            new List<RemoteRow<NpcSpawnRow>> { new(2, new NpcSpawnRow(1, 10, 3, 4)), new(3, new NpcSpawnRow(2, 10, 7, 8)) },
            new List<RemoteRow<WarpRow>> { new(2, new WarpRow(10, 5, 6, 7, 8, 9)) });
        return new GameDataSyncSession("sheet", 10, data);
    }

    private sealed class ScriptedConnectivity : IGameDataConnectivity
    {
        public bool IsConnected { get; set; }

        public GameDataSyncCoordinator? Coordinator { get; set; }

        public SpreadsheetReference? RememberedSpreadsheet { get; set; }

        public bool TryRememberSpreadsheet(string? pastedUrl)
        {
            if (!SpreadsheetReferenceParser.TryParse(pastedUrl, out SpreadsheetReference reference))
            {
                return false;
            }

            RememberedSpreadsheet = reference;
            return true;
        }

        public Task ConnectAsync(CancellationToken cancellationToken)
        {
            IsConnected = true;
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken)
        {
            IsConnected = false;
            return Task.CompletedTask;
        }
    }

    private sealed class ScriptedGateway : IGameDataGateway
    {
        private readonly Queue<IReadOnlyList<MapReference>> _maps = new();
        private readonly Queue<RemoteGameData> _data = new();

        public ScriptedGateway EnqueueMaps(IReadOnlyList<MapReference> maps)
        {
            _maps.Enqueue(maps);
            return this;
        }

        public ScriptedGateway EnqueueGameData(RemoteGameData data)
        {
            _data.Enqueue(data);
            return this;
        }

        public Task<IReadOnlyList<MapReference>> ReadMapsAsync(string spreadsheetId, CancellationToken cancellationToken)
            => Task.FromResult(_maps.Dequeue());

        public Task<RemoteGameData> ReadGameDataAsync(string spreadsheetId, int mapId, CancellationToken cancellationToken)
            => Task.FromResult(_data.Dequeue());

        public Task<RemoteOwnedRows> ReadOwnedRowsAsync(string spreadsheetId, int mapId, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task ReplaceOwnedRowsAsync(string spreadsheetId, ReplacementPlan spawnPlan, ReplacementPlan warpPlan, CancellationToken cancellationToken)
            => throw new NotImplementedException();
    }

    private static MenuItem Item(MainWindowHarness harness, string name)
        => harness.Window.FindControl<MenuItem>(name)
           ?? throw new InvalidOperationException($"missing menu item {name}");

    private static T Control<T>(MainWindowHarness harness, string name) where T : Control
        => harness.Window.FindControl<T>(name)
           ?? throw new InvalidOperationException($"missing control {name}");

    [AvaloniaFact]
    public void GameDataMenu_PresentWithDisconnectedDefaults()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();

        Assert.NotNull(Item(harness, "GameDataMenu"));
        Assert.Equal("Connect", Item(harness, "ConnectCommand").Header);
        Assert.False(Item(harness, "PullCommand").IsEnabled);
        Assert.False(Item(harness, "PushCommand").IsEnabled);
        Assert.True(Item(harness, "SpawnOverlayMenuItem").IsChecked == true);
        Assert.True(Item(harness, "WarpOverlayMenuItem").IsChecked == true);
        Assert.False(Item(harness, "PreviewMenuItem").IsChecked == true);
        Assert.False(Control<TextBlock>(harness, "PreviewStatusText").IsVisible);
    }

    [AvaloniaFact]
    public void ConnectCommand_SwapsLabelAndPullEnablement()
    {
        var connectivity = new ScriptedConnectivity();
        using MainWindowHarness harness = MainWindowHarness.Create(connectivity);
        MenuItem connect = Item(harness, "ConnectCommand");
        MenuItem pull = Item(harness, "PullCommand");

        connect.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.True(connectivity.IsConnected);
        Assert.Equal("Disconnect", connect.Header);
        Assert.True(pull.IsEnabled);

        connect.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.False(connectivity.IsConnected);
        Assert.Equal("Connect", connect.Header);
        Assert.False(pull.IsEnabled);
    }

    [AvaloniaFact]
    public void PushCommand_EnabledOnlyWhenConnectedWithAPulledSession()
    {
        var connectivity = new ScriptedConnectivity { IsConnected = true };
        using MainWindowHarness harness = MainWindowHarness.Create(connectivity);
        MenuItem push = Item(harness, "PushCommand");

        Assert.False(push.IsEnabled);

        harness.ViewModel.GameData!.AttachSession(Session());
        Dispatcher.UIThread.RunJobs();

        Assert.True(push.IsEnabled);

        connectivity.IsConnected = true;
        harness.Workspace.Commands.DisconnectAsync().GetAwaiter().GetResult();
        Dispatcher.UIThread.RunJobs();

        Assert.False(connectivity.IsConnected);
        Assert.False(push.IsEnabled);
    }

    [AvaloniaFact]
    public async Task OverlayToggles_ArePerTabAndRestoredAfterSwitch()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        MapDocumentViewModel first = harness.ViewModel;
        MenuItem spawnOverlay = Item(harness, "SpawnOverlayMenuItem");
        MenuItem warpOverlay = Item(harness, "WarpOverlayMenuItem");

        spawnOverlay.IsChecked = false;
        warpOverlay.IsChecked = false;
        Assert.False(first.GameData!.ShowSpawnOverlay);
        Assert.False(first.GameData.ShowWarpOverlay);

        harness.Dialogs.NewMapResult = new NewMapRequest(100, 100);
        await harness.Workspace.NewAsync();
        MapDocumentViewModel second = harness.Workspace.ActiveDocument;
        Assert.True(second.GameData!.ShowSpawnOverlay);
        Assert.True(spawnOverlay.IsChecked == true);
        Assert.True(warpOverlay.IsChecked == true);

        harness.Workspace.Activate(first);
        Dispatcher.UIThread.RunJobs();
        Assert.False(spawnOverlay.IsChecked == true);
        Assert.False(warpOverlay.IsChecked == true);
        Assert.False(first.GameData.ShowSpawnOverlay);

        harness.Workspace.Activate(second);
        Dispatcher.UIThread.RunJobs();
        Assert.True(spawnOverlay.IsChecked == true);
        Assert.True(warpOverlay.IsChecked == true);
    }

    [AvaloniaFact]
    public async Task PreviewToggle_IsPerTab_KeepsSpawnOverlayEnabledAndShowsUnavailableStatus()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        MapDocumentViewModel first = harness.ViewModel;
        MenuItem preview = Item(harness, "PreviewMenuItem");
        MenuItem spawnOverlay = Item(harness, "SpawnOverlayMenuItem");
        TextBlock status = Control<TextBlock>(harness, "PreviewStatusText");

        preview.IsChecked = true;
        Assert.True(first.GameData!.PreviewMode);
        Assert.True(preview.IsChecked == true);
        Assert.True(status.IsVisible);
        Assert.StartsWith("Art preview unavailable:", status.Text);
        Assert.True(spawnOverlay.IsChecked == true);
        Assert.True(spawnOverlay.IsEnabled);

        harness.Dialogs.NewMapResult = new NewMapRequest(100, 100);
        await harness.Workspace.NewAsync();
        MapDocumentViewModel second = harness.Workspace.ActiveDocument;
        Assert.False(second.GameData!.PreviewMode);
        Assert.False(status.IsVisible);
        Assert.False(preview.IsChecked == true);

        harness.Workspace.Activate(first);
        Dispatcher.UIThread.RunJobs();
        Assert.True(preview.IsChecked == true);
        Assert.True(status.IsVisible);
        Assert.StartsWith("Art preview unavailable:", status.Text);
    }

    [AvaloniaFact]
    public void PreviewToggle_WithAvailableAppearanceAssets_ShowsActiveStatus()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        string assetDirectory = WriteAppearanceAssetDirectory(harness);
        Assert.True(harness.Assets.TryOpen(assetDirectory));
        harness.ViewModel.GameData!.AttachSession(Session());
        Dispatcher.UIThread.RunJobs();
        MenuItem preview = Item(harness, "PreviewMenuItem");
        TextBlock status = Control<TextBlock>(harness, "PreviewStatusText");

        preview.IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        Assert.True(harness.ViewModel.GameData.PreviewMode);
        Assert.True(status.IsVisible);
        Assert.Equal("Art preview active", status.Text);

        preview.IsChecked = false;
        Dispatcher.UIThread.RunJobs();
        Assert.False(status.IsVisible);
    }

    [AvaloniaFact]
    public void PreviewToggle_DoesNotMutateRowsHistoryDirtySelectionOrSyncCommands()
    {
        var gateway = new ScriptedGateway().EnqueueMaps(Maps).EnqueueMaps(Maps).EnqueueGameData(SessionData());
        var connectivity = new ScriptedConnectivity
        {
            IsConnected = true,
            Coordinator = new GameDataSyncCoordinator(gateway, (_, _) => Task.CompletedTask)
        };
        using MainWindowHarness harness = MainWindowHarness.Create(connectivity);
        harness.Dialogs.SpreadsheetUrlResult = SheetUrl;
        harness.Dialogs.MapConfirmationResult = Map10;
        Item(harness, "PullCommand").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        MapDocumentViewModel document = harness.ViewModel;
        document.GameData!.SelectedNpcId = 2;
        document.AddSpawnAt(3, 2);
        NpcSpawnRow[] rows = document.GameData.Session!.Edits.Spawns.ToArray();
        bool canUndo = document.CanUndo;
        bool canRedo = document.CanRedo;
        bool dirty = document.IsDirty;
        bool canSave = document.CanSave;
        bool canPull = Item(harness, "PullCommand").IsEnabled;
        bool canPush = Item(harness, "PushCommand").IsEnabled;
        int? selectedSpawn = document.GameData.SelectedSpawn;
        TileClipboard? clipboard = document.Clipboard;

        MenuItem preview = Item(harness, "PreviewMenuItem");
        preview.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        preview.IsChecked = false;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(rows, document.GameData.Session.Edits.Spawns.ToArray());
        Assert.Equal(canUndo, document.CanUndo);
        Assert.Equal(canRedo, document.CanRedo);
        Assert.Equal(dirty, document.IsDirty);
        Assert.Equal(canSave, document.CanSave);
        Assert.Equal(canPull, Item(harness, "PullCommand").IsEnabled);
        Assert.Equal(canPush, Item(harness, "PushCommand").IsEnabled);
        Assert.Equal(selectedSpawn, document.GameData.SelectedSpawn);
        Assert.Same(clipboard, document.Clipboard);
        Assert.False(document.GameData.PreviewMode);
    }

    private static string WriteAppearanceAssetDirectory(MainWindowHarness harness)
    {
        string directory = Path.Combine(harness.TempDirectory, "appearance-assets");
        Directory.CreateDirectory(Path.Combine(directory, "sheets"));
        File.WriteAllText(Path.Combine(directory, "manifest.json"), """
            { "tileSize": 32, "sheets": { "1": { "10": [0, 0, 32, 32] } } }
            """);
        File.WriteAllText(Path.Combine(directory, "appearance-manifest.json"), """
            { "version": 1, "parts": {
              "Body": { "1": { "noEquip": [1, 10] } },
              "Hair": {}, "Eyes": {}, "Chest": {}, "Helm": {}, "Legs": {}, "Feet": {}, "Hand": {}
            } }
            """);
        File.WriteAllBytes(Path.Combine(directory, "sheets", "1.png"), MapEditor.App.Tests.Fixtures.AssetFixture.PngSheet.Create(32, 32));
        return directory;
    }

    [AvaloniaFact]
    public void SpawnTool_SwapsTheRightPanelToSpawnProperties()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        StackPanel mapPanel = Control<StackPanel>(harness, "RightPanel");
        StackPanel spawnPanel = Control<StackPanel>(harness, "SpawnProperties");
        StackPanel warpPanel = Control<StackPanel>(harness, "WarpProperties");

        Assert.True(mapPanel.IsVisible);
        Assert.False(spawnPanel.IsVisible);
        Assert.False(warpPanel.IsVisible);

        Control<ToggleButton>(harness, "SpawnTool").IsChecked = true;

        Assert.True(spawnPanel.IsVisible);
        Assert.False(mapPanel.IsVisible);
        Assert.False(warpPanel.IsVisible);
        Assert.IsType<SearchPickerControl<NpcAppearance>>(Control<Control>(harness, "SpawnNpcPicker"));
        Assert.False(Control<ToggleButton>(harness, "PencilTool").IsChecked == true);
    }

    [AvaloniaFact]
    public void WarpTool_SwapsTheRightPanelToWarpProperties()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        StackPanel mapPanel = Control<StackPanel>(harness, "RightPanel");
        StackPanel spawnPanel = Control<StackPanel>(harness, "SpawnProperties");
        StackPanel warpPanel = Control<StackPanel>(harness, "WarpProperties");

        Control<ToggleButton>(harness, "WarpTool").IsChecked = true;

        Assert.True(warpPanel.IsVisible);
        Assert.False(mapPanel.IsVisible);
        Assert.False(spawnPanel.IsVisible);
        Assert.IsType<SearchPickerControl<MapReference>>(Control<Control>(harness, "WarpDestinationPicker"));
        Assert.NotNull(Control<TextBox>(harness, "WarpDestinationX"));
        Assert.NotNull(Control<TextBox>(harness, "WarpDestinationY"));
        Assert.NotNull(Control<Button>(harness, "WarpUseSelectedTileButton"));
    }

    [AvaloniaFact]
    public void GameTools_AreExclusiveWithTheMapTools()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        ToggleButton spawn = Control<ToggleButton>(harness, "SpawnTool");
        ToggleButton warp = Control<ToggleButton>(harness, "WarpTool");
        ToggleButton pencil = Control<ToggleButton>(harness, "PencilTool");

        spawn.IsChecked = true;
        Assert.Equal(GameDataTool.Spawn, harness.ViewModel.GameData!.ActiveTool);
        Assert.False(pencil.IsChecked == true);

        pencil.IsChecked = true;
        Assert.Equal(MapEditTool.Pencil, harness.ViewModel.ActiveTool);
        Assert.Equal(GameDataTool.None, harness.ViewModel.GameData.ActiveTool);
        Assert.True(pencil.IsChecked == true);
        Assert.False(spawn.IsChecked == true);
        Assert.True(Control<StackPanel>(harness, "RightPanel").IsVisible);

        warp.IsChecked = true;
        spawn.IsChecked = true;
        Assert.True(spawn.IsChecked == true);
        Assert.False(warp.IsChecked == true);
        Assert.Equal(GameDataTool.Spawn, harness.ViewModel.GameData.ActiveTool);
        Assert.True(Control<StackPanel>(harness, "SpawnProperties").IsVisible);
    }

    [AvaloniaFact]
    public void UseSelectedTile_CopiesTheCanvasSelectionIntoTheWarpDestination()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        harness.ViewModel.GameData!.AttachSession(Session());
        Dispatcher.UIThread.RunJobs();
        Control<SearchPickerControl<MapReference>>(harness, "WarpDestinationPicker").SelectedItem = Map10;
        Button useSelected = Control<Button>(harness, "WarpUseSelectedTileButton");

        Assert.False(useSelected.IsEnabled);

        harness.ViewModel.SelectedX = 3;
        harness.ViewModel.SelectedY = 4;
        Dispatcher.UIThread.RunJobs();
        Assert.True(useSelected.IsEnabled);

        useSelected.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal("3", Control<TextBox>(harness, "WarpDestinationX").Text);
        Assert.Equal("4", Control<TextBox>(harness, "WarpDestinationY").Text);
    }

    [AvaloniaFact]
    public void RightPanel_HasNoGlobalSpawnOrWarpList()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();

        Assert.Null(harness.Window.FindControl<Control>("SpawnList"));
        Assert.Null(harness.Window.FindControl<Control>("WarpList"));
    }

    [AvaloniaFact]
    public async Task PerTab_ToolAndSelection_AreRestoredAfterSwitch()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        MapDocumentViewModel first = harness.ViewModel;
        first.GameData!.AttachSession(Session());
        Control<ToggleButton>(harness, "SpawnTool").IsChecked = true;

        harness.Dialogs.NewMapResult = new NewMapRequest(100, 100);
        await harness.Workspace.NewAsync();
        MapDocumentViewModel second = harness.Workspace.ActiveDocument;
        Control<ToggleButton>(harness, "WarpTool").IsChecked = true;
        Assert.True(Control<StackPanel>(harness, "WarpProperties").IsVisible);

        harness.Workspace.Activate(first);
        Dispatcher.UIThread.RunJobs();
        Assert.True(Control<ToggleButton>(harness, "SpawnTool").IsChecked == true);
        Assert.False(Control<ToggleButton>(harness, "WarpTool").IsChecked == true);
        Assert.True(Control<StackPanel>(harness, "SpawnProperties").IsVisible);
        Assert.False(Control<StackPanel>(harness, "WarpProperties").IsVisible);

        harness.Workspace.Activate(second);
        Dispatcher.UIThread.RunJobs();
        Assert.True(Control<ToggleButton>(harness, "WarpTool").IsChecked == true);
        Assert.False(Control<ToggleButton>(harness, "SpawnTool").IsChecked == true);
        Assert.True(Control<StackPanel>(harness, "WarpProperties").IsVisible);
    }

    [AvaloniaFact]
    public void SelectedSpawn_WithAnOrdinaryTool_KeepsTheMapPanel()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        harness.ViewModel.GameData!.AttachSession(Session());

        harness.ViewModel.GameData.SelectedSpawn = 0;
        Dispatcher.UIThread.RunJobs();

        Assert.True(Control<StackPanel>(harness, "RightPanel").IsVisible);
        Assert.False(Control<StackPanel>(harness, "SpawnProperties").IsVisible);
        Assert.False(Control<StackPanel>(harness, "WarpProperties").IsVisible);

        Control<ToggleButton>(harness, "SpawnTool").IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        Assert.False(Control<StackPanel>(harness, "RightPanel").IsVisible);
        Assert.True(Control<StackPanel>(harness, "SpawnProperties").IsVisible);
    }

    [AvaloniaFact]
    public void SelectedSpawn_SyncsTheNpcPickerToTheSpawnNpc()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        harness.ViewModel.GameData!.AttachSession(Session());
        Control<ToggleButton>(harness, "SpawnTool").IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        harness.ViewModel.GameData.SelectedSpawn = 0;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(Npc1, Control<SearchPickerControl<NpcAppearance>>(harness, "SpawnNpcPicker").SelectedItem);
    }

    [AvaloniaFact]
    public void SelectedWarp_SyncsTheDestinationPickerToTheWarpMap()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        var data = new RemoteGameData(
            Maps,
            new Dictionary<int, NpcAppearance> { [1] = Npc1 },
            new List<RemoteRow<NpcSpawnRow>>(),
            new List<RemoteRow<WarpRow>> { new(2, new WarpRow(10, 5, 6, 20, 8, 9)) });
        harness.ViewModel.GameData!.AttachSession(new GameDataSyncSession("sheet", 10, data));
        Control<ToggleButton>(harness, "WarpTool").IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        harness.ViewModel.GameData.SelectedWarp = 0;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(Map20, Control<SearchPickerControl<MapReference>>(harness, "WarpDestinationPicker").SelectedItem);
    }

    [AvaloniaFact]
    public void SpawnDelete_RemovesTheSelectedRowAndRestoresTheMapPanel()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        harness.ViewModel.GameData!.AttachSession(Session());
        harness.ViewModel.GameData.SelectedSpawn = 1;
        Dispatcher.UIThread.RunJobs();

        harness.Window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        var spawns = harness.ViewModel.GameData.Session!.Edits.Spawns;
        Assert.Single(spawns);
        Assert.Equal(1, spawns[0].NpcId);
        Assert.Null(harness.ViewModel.GameData.SelectedSpawn);
        Assert.True(Control<StackPanel>(harness, "RightPanel").IsVisible);
    }

    [AvaloniaFact]
    public async Task RightPanel_Picker_UsesTheActiveDocumentRowAfterSwitch()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        MapDocumentViewModel first = harness.ViewModel;
        first.GameData!.AttachSession(SessionWithSpawn(3, 4));
        first.GameData.SelectedSpawn = 0;
        Control<ToggleButton>(harness, "SpawnTool").IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Npc1, Control<SearchPickerControl<NpcAppearance>>(harness, "SpawnNpcPicker").SelectedItem);

        harness.Dialogs.NewMapResult = new NewMapRequest(100, 100);
        await harness.Workspace.NewAsync();
        MapDocumentViewModel second = harness.Workspace.ActiveDocument;
        second.GameData!.AttachSession(SessionWithSpawn(40, 50));
        second.GameData.SelectedSpawn = 0;
        Dispatcher.UIThread.RunJobs();
        Assert.True(Control<SearchPickerControl<NpcAppearance>>(harness, "SpawnNpcPicker").SelectedItem == default);

        harness.Workspace.Activate(first);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Npc1, Control<SearchPickerControl<NpcAppearance>>(harness, "SpawnNpcPicker").SelectedItem);
    }

    [AvaloniaFact]
    public void CommandRunning_PreventsDuplicateGameDataCommands()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Dialogs.ShowErrorGate = gate.Task;
        MenuItem connect = Item(harness, "ConnectCommand");

        connect.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Single(harness.Dialogs.Errors);

        connect.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Single(harness.Dialogs.Errors);

        gate.SetResult(true);
        Dispatcher.UIThread.RunJobs();

        connect.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, harness.Dialogs.Errors.Count);
    }

    [AvaloniaFact]
    public void Pull_ThroughTheMenu_AttachesTheSessionAndPopulatesThePickers()
    {
        var gateway = new ScriptedGateway().EnqueueMaps(Maps).EnqueueMaps(Maps).EnqueueGameData(SessionData());
        var connectivity = new ScriptedConnectivity
        {
            IsConnected = true,
            Coordinator = new GameDataSyncCoordinator(gateway, (_, _) => Task.CompletedTask)
        };
        using MainWindowHarness harness = MainWindowHarness.Create(connectivity);
        harness.Dialogs.SpreadsheetUrlResult = SheetUrl;
        harness.Dialogs.MapConfirmationResult = Map10;

        Item(harness, "PullCommand").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(harness.Dialogs.Errors);
        Assert.Equal(1, harness.Dialogs.SpreadsheetUrlShown);
        Assert.Equal(1, harness.Dialogs.MapConfirmationShown);
        Assert.True(harness.ViewModel.GameData!.HasSession);
        Assert.True(Item(harness, "PushCommand").IsEnabled);

        Control<ToggleButton>(harness, "SpawnTool").IsChecked = true;
        var npcPicker = Control<SearchPickerControl<NpcAppearance>>(harness, "SpawnNpcPicker");
        Assert.Equal(2, npcPicker.Items.Count);
        Assert.All(npcPicker.Items, npc => Assert.Contains(npc.NpcName, new[] { "Goose", "Duck" }));
        var destinationPicker = Control<SearchPickerControl<MapReference>>(harness, "WarpDestinationPicker");
        Assert.Equal(3, destinationPicker.Items.Count);
    }

    private static GameDataSyncSession SessionWithSpawn(int x, int y)
    {
        var data = new RemoteGameData(
            Maps,
            new Dictionary<int, NpcAppearance> { [1] = Npc1 },
            new List<RemoteRow<NpcSpawnRow>> { new(2, new NpcSpawnRow(1, 10, x, y)) },
            new List<RemoteRow<WarpRow>>());
        return new GameDataSyncSession("sheet", 10, data);
    }

    private static RemoteGameData SessionData()
    {
        var data = new RemoteGameData(
            Maps,
            new Dictionary<int, NpcAppearance> { [2] = Npc2, [1] = Npc1 },
            new List<RemoteRow<NpcSpawnRow>> { new(2, new NpcSpawnRow(1, 10, 3, 4)), new(3, new NpcSpawnRow(2, 10, 7, 8)) },
            new List<RemoteRow<WarpRow>> { new(2, new WarpRow(10, 5, 6, 7, 8, 9)) });
        return data;
    }
}
