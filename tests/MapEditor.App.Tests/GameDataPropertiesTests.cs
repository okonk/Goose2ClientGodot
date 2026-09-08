using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
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

public class GameDataPropertiesTests
{
    private static readonly MapReference Map10 = new(10, "Dungeon", "dungeon.map");
    private static readonly MapReference Map20 = new(20, "Cave", "cave.map");
    private static readonly NpcAppearance Npc1 = new(1, "Goose", 0, 0, new RgbaValue(255, 255, 255, 255), 0, 0, new RgbaValue(255, 255, 255, 255), string.Empty);
    private static readonly NpcAppearance Npc2 = new(2, "Duck", 0, 0, new RgbaValue(255, 255, 255, 255), 0, 0, new RgbaValue(255, 255, 255, 255), string.Empty);

    [AvaloniaFact]
    public void WarpDestinationCoordinates_CommitOnFocusLossAsOneUpdateCommand()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        harness.ViewModel.GameData!.AttachSession(Session());
        Control<ToggleButton>(harness, "WarpTool").IsChecked = true;
        harness.ViewModel.GameData.SelectedWarp = 0;
        Dispatcher.UIThread.RunJobs();

        TextBox x = Control<TextBox>(harness, "WarpDestinationX");
        TextBox y = Control<TextBox>(harness, "WarpDestinationY");
        Assert.Equal("8", x.Text);
        Assert.Equal("9", y.Text);

        x.Focus();
        x.Text = "11";
        y.Text = "12";
        harness.Canvas.Focus();
        Dispatcher.UIThread.RunJobs();

        WarpRow warp = harness.ViewModel.GameData.Session!.Edits.Warps[0];
        Assert.Equal((11, 12), (warp.WarpX, warp.WarpY));
        Assert.True(harness.ViewModel.Timeline.CanUndo);
        Assert.True(harness.ViewModel.Undo());
        warp = harness.ViewModel.GameData.Session.Edits.Warps[0];
        Assert.Equal((8, 9), (warp.WarpX, warp.WarpY));
    }

    [AvaloniaFact]
    public void WarpDestinationCoordinates_NegativeText_RetainsFocusAndErrorWithoutMutation()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        harness.ViewModel.GameData!.AttachSession(Session());
        Control<ToggleButton>(harness, "WarpTool").IsChecked = true;
        harness.ViewModel.GameData.SelectedWarp = 0;
        Dispatcher.UIThread.RunJobs();

        TextBox x = Control<TextBox>(harness, "WarpDestinationX");
        TextBox y = Control<TextBox>(harness, "WarpDestinationY");

        x.Focus();
        x.Text = "-1";
        y.Focus();
        Dispatcher.UIThread.RunJobs();

        Assert.True(x.IsFocused);
        Assert.NotNull(x.BorderBrush);
        WarpRow warp = harness.ViewModel.GameData.Session!.Edits.Warps[0];
        Assert.Equal((8, 9), (warp.WarpX, warp.WarpY));
        Assert.False(harness.ViewModel.Timeline.CanUndo);
    }

    [AvaloniaFact]
    public void SpawnNpcPicker_CommitsForTheSelectedSpawn()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        harness.ViewModel.GameData!.AttachSession(Session());
        harness.ViewModel.GameData.SelectedSpawn = 0;
        Dispatcher.UIThread.RunJobs();

        SearchPickerControl<NpcAppearance> picker = Control<SearchPickerControl<NpcAppearance>>(harness, "SpawnNpcPicker");
        picker.SelectedItem = Npc2;
        Dispatcher.UIThread.RunJobs();

        NpcSpawnRow spawn = harness.ViewModel.GameData.Session!.Edits.Spawns[0];
        Assert.Equal(2, spawn.NpcId);
        Assert.Equal(2, harness.ViewModel.GameData.SelectedNpcId);
        Assert.True(harness.ViewModel.Timeline.CanUndo);
        Assert.True(harness.ViewModel.Undo());
        Assert.Equal(1, harness.ViewModel.GameData.Session.Edits.Spawns[0].NpcId);
    }

    [AvaloniaFact]
    public void WarpDestinationPicker_CommitsForTheSelectedWarp()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        harness.ViewModel.GameData!.AttachSession(Session());
        harness.ViewModel.GameData.SelectedWarp = 0;
        Dispatcher.UIThread.RunJobs();

        SearchPickerControl<MapReference> picker = Control<SearchPickerControl<MapReference>>(harness, "WarpDestinationPicker");
        picker.SelectedItem = Map20;
        Dispatcher.UIThread.RunJobs();

        WarpRow warp = harness.ViewModel.GameData.Session!.Edits.Warps[0];
        Assert.Equal(20, warp.WarpId);
        Assert.Equal(20, harness.ViewModel.GameData.SelectedDestinationMapId);
        Assert.True(harness.ViewModel.Timeline.CanUndo);
        Assert.True(harness.ViewModel.Undo());
        Assert.Equal(7, harness.ViewModel.GameData.Session.Edits.Warps[0].WarpId);
    }

    [AvaloniaFact]
    public void KeyboardDelete_RemovesTheSelectedWarpOccurrence()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        harness.ViewModel.GameData!.AttachSession(Session());
        harness.ViewModel.GameData.SelectedWarp = 0;
        Dispatcher.UIThread.RunJobs();

        harness.Window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(harness.ViewModel.GameData.Session!.Edits.Warps);
        Assert.Null(harness.ViewModel.GameData.SelectedWarp);
    }

    [AvaloniaFact]
    public void KeyboardDelete_RemovesTheSelectedSpawnOccurrence()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        harness.ViewModel.GameData!.AttachSession(Session());
        harness.ViewModel.GameData.SelectedSpawn = 0;
        Dispatcher.UIThread.RunJobs();

        harness.Window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        var spawns = harness.ViewModel.GameData.Session!.Edits.Spawns;
        Assert.Single(spawns);
        Assert.Equal(2, spawns[0].NpcId);
        Assert.Null(harness.ViewModel.GameData.SelectedSpawn);
    }

    [AvaloniaFact]
    public void UseSelectedTile_EnabledForExactlyOneMatchingTabAndCopiesTheDestination()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        harness.ViewModel.GameData!.AttachSession(Session());
        Dispatcher.UIThread.RunJobs();
        SearchPickerControl<MapReference> picker = Control<SearchPickerControl<MapReference>>(harness, "WarpDestinationPicker");
        Button useSelected = Control<Button>(harness, "WarpUseSelectedTileButton");

        Assert.False(useSelected.IsEnabled);

        picker.SelectedItem = Map10;
        Dispatcher.UIThread.RunJobs();
        Assert.False(useSelected.IsEnabled);

        harness.ViewModel.SelectedX = 3;
        harness.ViewModel.SelectedY = 4;
        Dispatcher.UIThread.RunJobs();
        Assert.True(useSelected.IsEnabled);

        useSelected.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("3", Control<TextBox>(harness, "WarpDestinationX").Text);
        Assert.Equal("4", Control<TextBox>(harness, "WarpDestinationY").Text);
    }

    [AvaloniaFact]
    public async Task UseSelectedTile_DisabledForAmbiguousMatch()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        MapDocumentViewModel first = harness.ViewModel;
        first.GameData!.AttachSession(Session());

        harness.Dialogs.NewMapResult = new NewMapRequest(100, 100);
        await harness.Workspace.NewAsync();
        MapDocumentViewModel second = harness.Workspace.ActiveDocument;
        second.GameData!.AttachSession(Session());
        Dispatcher.UIThread.RunJobs();

        SearchPickerControl<MapReference> picker = Control<SearchPickerControl<MapReference>>(harness, "WarpDestinationPicker");
        Button useSelected = Control<Button>(harness, "WarpUseSelectedTileButton");
        picker.SelectedItem = Map10;
        first.SelectedX = 1;
        first.SelectedY = 1;
        second.SelectedX = 2;
        second.SelectedY = 2;
        Dispatcher.UIThread.RunJobs();

        Assert.False(useSelected.IsEnabled);

        second.SelectedX = null;
        Dispatcher.UIThread.RunJobs();
        Assert.True(useSelected.IsEnabled);

        useSelected.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("1", Control<TextBox>(harness, "WarpDestinationX").Text);
        Assert.Equal("1", Control<TextBox>(harness, "WarpDestinationY").Text);
    }

    [AvaloniaFact]
    public async Task UseSelectedTile_IgnoresDifferentSpreadsheetTabs()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        MapDocumentViewModel first = harness.ViewModel;
        first.GameData!.AttachSession(Session());

        harness.Dialogs.NewMapResult = new NewMapRequest(100, 100);
        await harness.Workspace.NewAsync();
        MapDocumentViewModel second = harness.Workspace.ActiveDocument;
        second.GameData!.AttachSession(SessionWithSpreadsheet("other"));
        Dispatcher.UIThread.RunJobs();

        SearchPickerControl<MapReference> picker = Control<SearchPickerControl<MapReference>>(harness, "WarpDestinationPicker");
        Button useSelected = Control<Button>(harness, "WarpUseSelectedTileButton");
        picker.SelectedItem = Map10;
        first.SelectedX = 1;
        first.SelectedY = 1;
        Dispatcher.UIThread.RunJobs();

        Assert.False(useSelected.IsEnabled);

        second.SelectedX = 2;
        second.SelectedY = 2;
        Dispatcher.UIThread.RunJobs();
        Assert.True(useSelected.IsEnabled);

        useSelected.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("2", Control<TextBox>(harness, "WarpDestinationX").Text);
        Assert.Equal("2", Control<TextBox>(harness, "WarpDestinationY").Text);
    }

    [AvaloniaFact]
    public void SessionReplacement_ClearsThePickerWhileTheToolIsActive()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        harness.ViewModel.GameData!.AttachSession(SessionWithSpawn(3, 4));
        harness.ViewModel.GameData.SelectedSpawn = 0;
        Dispatcher.UIThread.RunJobs();

        Control<ToggleButton>(harness, "SpawnTool").IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Npc1, Control<SearchPickerControl<NpcAppearance>>(harness, "SpawnNpcPicker").SelectedItem);

        harness.ViewModel.GameData.AttachSession(SessionWithSpawn(9, 9));
        Dispatcher.UIThread.RunJobs();

        Assert.Null(harness.ViewModel.GameData.SelectedSpawn);
        Assert.True(Control<StackPanel>(harness, "SpawnProperties").IsVisible);
        Assert.True(Control<SearchPickerControl<NpcAppearance>>(harness, "SpawnNpcPicker").SelectedItem == default);
    }

    private static T Control<T>(MainWindowHarness harness, string name) where T : Control
        => harness.Window.FindControl<T>(name)
           ?? throw new InvalidOperationException($"missing control {name}");

    private static GameDataSyncSession Session()
    {
        var data = new RemoteGameData(
            new[] { Map10, new MapReference(20, "Cave", "cave.map"), new MapReference(30, "Tower", "tower.map") },
            new Dictionary<int, NpcAppearance> { [1] = Npc1, [2] = Npc2 },
            new List<RemoteRow<NpcSpawnRow>> { new(2, new NpcSpawnRow(1, 10, 3, 4)), new(3, new NpcSpawnRow(2, 10, 7, 8)) },
            new List<RemoteRow<WarpRow>> { new(2, new WarpRow(10, 5, 6, 7, 8, 9)) });
        return new GameDataSyncSession("sheet", 10, data);
    }

    private static GameDataSyncSession SessionWithSpawn(int x, int y)
    {
        var data = new RemoteGameData(
            new[] { Map10 },
            new Dictionary<int, NpcAppearance> { [1] = Npc1 },
            new List<RemoteRow<NpcSpawnRow>> { new(2, new NpcSpawnRow(1, 10, x, y)) },
            new List<RemoteRow<WarpRow>>());
        return new GameDataSyncSession("sheet", 10, data);
    }

    private static GameDataSyncSession SessionWithSpreadsheet(string spreadsheetId)
    {
        var data = new RemoteGameData(
            new[] { Map10 },
            new Dictionary<int, NpcAppearance> { [1] = Npc1 },
            new List<RemoteRow<NpcSpawnRow>>(),
            new List<RemoteRow<WarpRow>>());
        return new GameDataSyncSession(spreadsheetId, 10, data);
    }
}
