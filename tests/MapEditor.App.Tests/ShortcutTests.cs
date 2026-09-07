using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MapEditor.App.Controls;
using MapEditor.App.Dialogs;
using MapEditor.App.ViewModels;
using MapEditor.Core;
using MapEditor.GameData.Connectivity;
using MapEditor.GameData.Replacement;
using MapEditor.GameData.Rows;
using MapEditor.GameData.Sync;
using MapEditor.Rendering;
using Xunit;

namespace MapEditor.App.Tests;

public class ShortcutTests
{
    private static readonly MapReference Map10 = new(10, "Dungeon", "dungeon.bytes");
    private static readonly MapReference Map20 = new(20, "Cave", "cave.bytes");
    private static readonly NpcAppearance Npc1 = new(1, "Goose", 0, 0, new RgbaValue(255, 255, 255, 255), 0, 0, new RgbaValue(255, 255, 255, 255), string.Empty);

    private static void PaintCell(MainWindowHarness harness)
    {
        MainWindow window = harness.Window;
        harness.ViewModel.Brush = new MapTileLayer(1, 1);
        Point tileCenter = window.Canvas.TranslatePoint(new Point(16, 16), window).Value;
        window.MouseDown(tileCenter, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(tileCenter, MouseButton.Left, RawInputModifiers.None);
    }

    private static async Task<MapDocumentViewModel> NewDocumentAsync(MainWindowHarness harness)
    {
        harness.Dialogs.NewMapResult = new NewMapRequest(100, 100);
        await harness.Workspace.NewAsync();
        Dispatcher.UIThread.RunJobs();
        return harness.Workspace.ActiveDocument;
    }

    private static GameDataSyncSession GameDataSession()
    {
        var data = new RemoteGameData(
            new[] { Map10, Map20 },
            new Dictionary<int, NpcAppearance> { [1] = Npc1 },
            new List<RemoteRow<NpcSpawnRow>> { new(2, new NpcSpawnRow(1, 10, 3, 4)) },
            new List<RemoteRow<WarpRow>>());
        return new GameDataSyncSession("sheet", 10, data);
    }

    private static T Control<T>(MainWindowHarness harness, string name) where T : Control
        => harness.Window.FindControl<T>(name)
           ?? throw new InvalidOperationException($"missing control {name}");

    [AvaloniaFact]
    public async Task ControlT_AddsTab()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        MapDocumentViewModel first = harness.ViewModel;
        harness.Dialogs.NewMapResult = new NewMapRequest(100, 100);
        harness.Window.Canvas.Focus();

        harness.Window.KeyPressQwerty(PhysicalKey.T, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, harness.Workspace.Documents.Count);
        MapDocumentViewModel second = harness.Workspace.ActiveDocument;
        Assert.NotSame(first, second);
        Assert.Equal(1, harness.Dialogs.NewMapShown);
    }

    [AvaloniaFact]
    public async Task ControlW_ClosesActiveTab()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        MapDocumentViewModel first = harness.ViewModel;
        MapDocumentViewModel second = await NewDocumentAsync(harness);
        harness.Workspace.Activate(first);
        harness.Window.Canvas.Focus();

        harness.Window.KeyPressQwerty(PhysicalKey.W, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();

        Assert.Single(harness.Workspace.Documents);
        Assert.Same(second, harness.Workspace.ActiveDocument);
    }

    [AvaloniaFact]
    public async Task ControlTab_CyclesForwardWithWrap()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        MapDocumentViewModel first = harness.ViewModel;
        MapDocumentViewModel second = await NewDocumentAsync(harness);
        MapDocumentViewModel third = await NewDocumentAsync(harness);
        harness.Workspace.Activate(first);
        harness.Window.Canvas.Focus();

        harness.Window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(second, harness.Workspace.ActiveDocument);

        harness.Window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(third, harness.Workspace.ActiveDocument);

        harness.Window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(first, harness.Workspace.ActiveDocument);
    }

    [AvaloniaFact]
    public async Task ControlShiftTab_CyclesBackwardWithWrap()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        MapDocumentViewModel first = harness.ViewModel;
        MapDocumentViewModel second = await NewDocumentAsync(harness);
        MapDocumentViewModel third = await NewDocumentAsync(harness);
        harness.Workspace.Activate(third);
        harness.Window.Canvas.Focus();

        harness.Window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.Control | RawInputModifiers.Shift);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(second, harness.Workspace.ActiveDocument);

        harness.Window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.Control | RawInputModifiers.Shift);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(first, harness.Workspace.ActiveDocument);

        harness.Window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.Control | RawInputModifiers.Shift);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(third, harness.Workspace.ActiveDocument);
    }

    [AvaloniaFact]
    public async Task Control3_ActivatesThirdTab()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        MapDocumentViewModel first = harness.ViewModel;
        MapDocumentViewModel second = await NewDocumentAsync(harness);
        MapDocumentViewModel third = await NewDocumentAsync(harness);
        harness.Workspace.Activate(first);
        harness.Window.Canvas.Focus();

        harness.Window.KeyPressQwerty(PhysicalKey.Digit3, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();

        Assert.Same(third, harness.Workspace.ActiveDocument);
    }

    [AvaloniaFact]
    public async Task Control9_ActivatesLastTab()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        MapDocumentViewModel first = harness.ViewModel;
        MapDocumentViewModel second = await NewDocumentAsync(harness);
        MapDocumentViewModel third = await NewDocumentAsync(harness);
        harness.Workspace.Activate(first);
        harness.Window.Canvas.Focus();

        harness.Window.KeyPressQwerty(PhysicalKey.Digit9, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();

        Assert.Same(third, harness.Workspace.ActiveDocument);
    }

    [AvaloniaFact]
    public async Task ControlNumPad3_ActivatesThirdTab()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        MapDocumentViewModel first = harness.ViewModel;
        MapDocumentViewModel second = await NewDocumentAsync(harness);
        MapDocumentViewModel third = await NewDocumentAsync(harness);
        harness.Workspace.Activate(first);
        harness.Window.Canvas.Focus();

        harness.Window.KeyPressQwerty(PhysicalKey.NumPad3, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();

        Assert.Same(third, harness.Workspace.ActiveDocument);
    }

    [AvaloniaFact]
    public async Task Control9_WithTenTabs_ActivatesTheTenthTab()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        MapDocumentViewModel first = harness.ViewModel;
        for (int i = 0; i < 9; i++)
        {
            await NewDocumentAsync(harness);
        }
        MapDocumentViewModel last = harness.Workspace.Documents[9];
        harness.Workspace.Activate(first);
        harness.Window.Canvas.Focus();

        harness.Window.KeyPressQwerty(PhysicalKey.Digit9, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();

        Assert.Same(last, harness.Workspace.ActiveDocument);
    }

    [AvaloniaFact]
    public void ControlTab_WithSingleTab_DoesNothing()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        MapDocumentViewModel only = harness.ViewModel;
        harness.Window.Canvas.Focus();

        harness.Window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();

        Assert.Same(only, harness.Workspace.ActiveDocument);
    }

    [AvaloniaFact]
    public async Task Control5_WithThreeTabs_DoesNothing()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        MapDocumentViewModel first = harness.ViewModel;
        await NewDocumentAsync(harness);
        await NewDocumentAsync(harness);
        harness.Workspace.Activate(first);
        harness.Window.Canvas.Focus();

        harness.Window.KeyPressQwerty(PhysicalKey.Digit5, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();

        Assert.Same(first, harness.Workspace.ActiveDocument);
    }

    [AvaloniaFact]
    public async Task ControlTab_WithFocusInBrushField_StillSwitchesTabs()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        MapDocumentViewModel first = harness.ViewModel;
        MapDocumentViewModel second = await NewDocumentAsync(harness);
        harness.Workspace.Activate(first);
        TextBox graphic = harness.Window.FindControl<TextBox>("BrushGraphic")!;
        graphic.Focus();

        harness.Window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();

        Assert.Same(second, harness.Workspace.ActiveDocument);
    }

    [AvaloniaFact]
    public void ControlN_InvokesNew()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        harness.Window.Canvas.Focus();

        harness.Window.KeyPressQwerty(PhysicalKey.N, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, harness.Dialogs.NewMapShown);
    }

    [AvaloniaFact]
    public void ControlO_InvokesOpen()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        harness.Window.Canvas.Focus();

        harness.Window.KeyPressQwerty(PhysicalKey.O, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, harness.Dialogs.OpenPickShown);
    }

    [AvaloniaFact]
    public void ControlS_InvokesSave()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        string mapPath = Path.Combine(harness.TempDirectory, "shortcut.bytes");
        harness.Dialogs.SavePickResult = mapPath;
        harness.Window.Canvas.Focus();

        harness.Window.KeyPressQwerty(PhysicalKey.S, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, harness.Dialogs.SavePickShown);
        Assert.True(File.Exists(mapPath));
    }

    [AvaloniaFact]
    public void ControlShiftS_InvokesSaveAs()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        harness.Dialogs.SavePickResult = Path.Combine(harness.TempDirectory, "saveas.bytes");
        harness.Window.Canvas.Focus();

        harness.Window.KeyPressQwerty(PhysicalKey.S, RawInputModifiers.Control | RawInputModifiers.Shift);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, harness.Dialogs.SavePickShown);
    }

    [AvaloniaFact]
    public void ControlZ_Undoes()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        PaintCell(harness);
        Assert.True(harness.ViewModel.CanUndo);
        harness.Window.Canvas.Focus();

        harness.Window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control);

        Assert.False(harness.ViewModel.CanUndo);
        Assert.True(harness.ViewModel.CanRedo);
    }

    [AvaloniaFact]
    public void ControlYAndControlShiftZ_BothRedo()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        PaintCell(harness);
        harness.Window.Canvas.Focus();

        harness.Window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control);
        harness.Window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.Control);

        Assert.False(harness.ViewModel.CanRedo);

        PaintCell(harness);
        harness.Window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control);
        harness.Window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control | RawInputModifiers.Shift);

        Assert.False(harness.ViewModel.CanRedo);
    }

    [AvaloniaFact]
    public void UnmodifiedPEIB_SelectTools()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        MainWindow window = harness.Window;

        window.KeyPressQwerty(PhysicalKey.E, RawInputModifiers.None);
        Assert.Equal(MapEditTool.Eraser, harness.ViewModel.ActiveTool);
        Assert.True(window.FindControl<Avalonia.Controls.Primitives.ToggleButton>("EraserTool")!.IsChecked);

        window.KeyPressQwerty(PhysicalKey.I, RawInputModifiers.None);
        Assert.Equal(MapEditTool.Eyedropper, harness.ViewModel.ActiveTool);

        window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.None);
        Assert.Equal(MapEditTool.Blocked, harness.ViewModel.ActiveTool);

        window.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.None);
        Assert.Equal(MapEditTool.Pencil, harness.ViewModel.ActiveTool);
        Assert.True(window.FindControl<Avalonia.Controls.Primitives.ToggleButton>("PencilTool")!.IsChecked);

        window.KeyPressQwerty(PhysicalKey.B, RawInputModifiers.None);
        Assert.Equal(MapEditTool.FloodFill, harness.ViewModel.ActiveTool);

        window.KeyPressQwerty(PhysicalKey.V, RawInputModifiers.None);
        Assert.Equal(MapEditTool.Select, harness.ViewModel.ActiveTool);
        Assert.True(window.FindControl<Avalonia.Controls.Primitives.ToggleButton>("SelectTool")!.IsChecked);

        window.KeyPressQwerty(PhysicalKey.M, RawInputModifiers.None);
        Assert.Equal(MapEditTool.MultiSelect, harness.ViewModel.ActiveTool);
    }

    [AvaloniaFact]
    public void UnmodifiedKeys_ReachFocusedBrushField()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        MainWindow window = harness.Window;
        TextBox graphic = window.FindControl<TextBox>("BrushGraphic")!;
        graphic.Focus();

        window.KeyPressQwerty(PhysicalKey.Minus, RawInputModifiers.None);
        window.KeyTextInput("-");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(100, harness.ViewModel.ZoomPercent);
        Assert.Contains("-", graphic.Text);

        window.KeyPressQwerty(PhysicalKey.E, RawInputModifiers.None);
        window.KeyTextInput("e");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(MapEditTool.Pencil, harness.ViewModel.ActiveTool);
        Assert.Contains("e", graphic.Text);
    }

    [AvaloniaFact]
    public void PlusMinus_ZoomAroundCanvasCenter()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        MapCanvas canvas = harness.Window.Canvas;
        Point center = new(canvas.Bounds.Width / 2, canvas.Bounds.Height / 2);
        RenderPoint worldBefore = canvas.Viewport.ScreenToWorld(new RenderPoint(center.X, center.Y));

        harness.Window.KeyPressQwerty(PhysicalKey.Equal, RawInputModifiers.None);
        Assert.Equal(200, harness.ViewModel.ZoomPercent);
        Assert.Equal(worldBefore, canvas.Viewport.ScreenToWorld(new RenderPoint(center.X, center.Y)));

        harness.Window.KeyPressQwerty(PhysicalKey.Minus, RawInputModifiers.None);
        Assert.Equal(100, harness.ViewModel.ZoomPercent);
    }

    [AvaloniaFact]
    public void SaveDuringDrag_CompletesStrokeOnceBeforeStorage()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        string mapPath = Path.Combine(harness.TempDirectory, "drag-save.bytes");
        harness.Dialogs.SavePickResult = mapPath;
        harness.ViewModel.Brush = new MapTileLayer(3, 9);
        MainWindow window = harness.Window;
        Point tileCenter = window.Canvas.TranslatePoint(new Point(16, 16), window).Value;

        window.MouseDown(tileCenter, MouseButton.Left, RawInputModifiers.None);
        Assert.True(harness.ViewModel.Session.HasActiveStroke);

        window.KeyPressQwerty(PhysicalKey.S, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();

        Assert.False(harness.ViewModel.Session.HasActiveStroke);
        Assert.Equal(new MapTileLayer(3, 9), harness.ViewModel.Session.Document[0, 0].GetLayer(0));
        Assert.True(harness.ViewModel.CanUndo);
        Assert.Equal(1, harness.Dialogs.SavePickShown);
        Assert.True(File.Exists(mapPath));

        Assert.True(harness.ViewModel.Undo());
        Assert.Equal(new MapTileLayer(0, 0), harness.ViewModel.Session.Document[0, 0].GetLayer(0));
    }

    [AvaloniaFact]
    public void UndoDuringDrag_CompletesStrokeBeforeHistoryAction()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        PaintCell(harness);
        harness.ViewModel.Brush = new MapTileLayer(5, 5);
        MainWindow window = harness.Window;
        Point tileCenter = window.Canvas.TranslatePoint(new Point(48, 16), window).Value;

        window.MouseDown(tileCenter, MouseButton.Left, RawInputModifiers.None);
        Assert.True(harness.ViewModel.Session.HasActiveStroke);

        window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();

        Assert.False(harness.ViewModel.Session.HasActiveStroke);
        Assert.Equal(new MapTileLayer(0, 0), harness.ViewModel.Session.Document[1, 0].GetLayer(0));
        Assert.True(harness.ViewModel.CanUndo);

        Assert.True(harness.ViewModel.Undo());
        Assert.Equal(new MapTileLayer(0, 0), harness.ViewModel.Session.Document[0, 0].GetLayer(0));
    }

    [AvaloniaFact]
    public void UndoRedo_FollowsTheDocumentTimelineAcrossDomains()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        MainWindow window = harness.Window;
        MapDocumentViewModel vm = harness.ViewModel;
        vm.GameData!.AttachSession(GameDataSession());
        Dispatcher.UIThread.RunJobs();

        MenuItem undo = Control<MenuItem>(harness, "UndoCommand");
        MenuItem redo = Control<MenuItem>(harness, "RedoCommand");
        StackPanel spawnPanel = Control<StackPanel>(harness, "SpawnProperties");
        TextBox spawnX = Control<TextBox>(harness, "SpawnSourceX");
        TextBox spawnY = Control<TextBox>(harness, "SpawnSourceY");
        Button spawnDelete = Control<Button>(harness, "SpawnDeleteButton");

        PaintCell(harness);
        Assert.Equal(new MapTileLayer(1, 1), vm.Session.Document[0, 0].GetLayer(0));
        Assert.Null(vm.FindSpawnAt(5, 6));
        Assert.True(vm.GameData.ShowSpawnOverlay);
        Assert.False(spawnPanel.IsVisible);
        Assert.Null(spawnX.Text);
        Assert.Null(spawnY.Text);
        Assert.False(spawnDelete.IsEnabled);
        Assert.True(vm.IsDirty);
        Assert.Contains("*", window.Title);
        Assert.True(undo.IsEnabled);
        Assert.False(redo.IsEnabled);

        Control<ToggleButton>(harness, "SpawnTool").IsChecked = true;
        vm.GameData.SelectedNpcId = 1;
        Point tile = window.Canvas.TranslatePoint(new Point(5 * 32 + 16, 6 * 32 + 16), window).Value;
        window.MouseDown(tile, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(tile, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        var spawns = vm.GameData.Session!.Edits.Spawns;
        Assert.Equal(2, spawns.Count);
        Assert.Equal(new NpcSpawnRow(1, 10, 5, 6), spawns[1]);
        Assert.Equal(1, vm.GameData.SelectedSpawn);
        Assert.Equal(1, vm.FindSpawnAt(5, 6));
        Assert.True(vm.GameData.ShowSpawnOverlay);
        Assert.True(spawnPanel.IsVisible);
        Assert.Equal("5", spawnX.Text);
        Assert.Equal("6", spawnY.Text);
        Assert.True(spawnDelete.IsEnabled);
        Assert.Equal(new MapTileLayer(1, 1), vm.Session.Document[0, 0].GetLayer(0));
        Assert.True(vm.IsDirty);
        Assert.Contains("*", window.Title);
        Assert.True(undo.IsEnabled);
        Assert.False(redo.IsEnabled);

        window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();

        Assert.Single(vm.GameData.Session.Edits.Spawns);
        Assert.Null(vm.FindSpawnAt(5, 6));
        Assert.Null(vm.GameData.SelectedSpawn);
        Assert.Equal(new MapTileLayer(1, 1), vm.Session.Document[0, 0].GetLayer(0));
        Assert.True(spawnPanel.IsVisible);
        Assert.Equal(string.Empty, spawnX.Text);
        Assert.Equal(string.Empty, spawnY.Text);
        Assert.False(spawnDelete.IsEnabled);
        Assert.True(vm.IsDirty);
        Assert.Contains("*", window.Title);
        Assert.True(undo.IsEnabled);
        Assert.True(redo.IsEnabled);

        window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new MapTileLayer(0, 0), vm.Session.Document[0, 0].GetLayer(0));
        Assert.Null(vm.FindSpawnAt(5, 6));
        Assert.True(vm.GameData.ShowSpawnOverlay);
        Assert.True(spawnPanel.IsVisible);
        Assert.Equal(string.Empty, spawnX.Text);
        Assert.Equal(string.Empty, spawnY.Text);
        Assert.False(spawnDelete.IsEnabled);
        Assert.False(vm.IsDirty);
        Assert.DoesNotContain("*", window.Title);
        Assert.False(undo.IsEnabled);
        Assert.True(redo.IsEnabled);

        window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new MapTileLayer(1, 1), vm.Session.Document[0, 0].GetLayer(0));
        Assert.Null(vm.FindSpawnAt(5, 6));
        Assert.True(vm.GameData.ShowSpawnOverlay);
        Assert.True(spawnPanel.IsVisible);
        Assert.Equal(string.Empty, spawnX.Text);
        Assert.Equal(string.Empty, spawnY.Text);
        Assert.False(spawnDelete.IsEnabled);
        Assert.True(vm.IsDirty);
        Assert.Contains("*", window.Title);
        Assert.True(undo.IsEnabled);
        Assert.True(redo.IsEnabled);

        window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new MapTileLayer(1, 1), vm.Session.Document[0, 0].GetLayer(0));
        Assert.Equal(2, vm.GameData.Session.Edits.Spawns.Count);
        Assert.Equal(new NpcSpawnRow(1, 10, 5, 6), vm.GameData.Session.Edits.Spawns[1]);
        Assert.Equal(1, vm.FindSpawnAt(5, 6));
        Assert.True(vm.GameData.ShowSpawnOverlay);
        Assert.True(spawnPanel.IsVisible);
        Assert.Equal(string.Empty, spawnX.Text);
        Assert.Equal(string.Empty, spawnY.Text);
        Assert.False(spawnDelete.IsEnabled);
        Assert.True(vm.IsDirty);
        Assert.Contains("*", window.Title);
        Assert.True(undo.IsEnabled);
        Assert.False(redo.IsEnabled);
    }

    [AvaloniaFact]
    public void CopyCutPasteDelete_NotInterceptedFromPickerAndPropertyTextFields()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        MainWindow window = harness.Window;
        MapDocumentViewModel vm = harness.ViewModel;
        vm.GameData!.AttachSession(GameDataSession());
        Dispatcher.UIThread.RunJobs();

        vm.GameData.ActiveTool = GameDataTool.Spawn;
        vm.GameData.SelectedSpawn = 0;
        vm.SelectedX = 2;
        vm.SelectedY = 3;
        Dispatcher.UIThread.RunJobs();

        TextBox searchBox = Control<SearchPickerControl<NpcAppearance>>(harness, "SpawnNpcPicker")
            .GetVisualDescendants().OfType<TextBox>().Single();
        searchBox.Focus();
        Dispatcher.UIThread.RunJobs();

        PressClipboardAndDeleteKeys(window);

        Assert.Null(harness.Workspace.Clipboard.Current);
        Assert.Single(vm.GameData.Session!.Edits.Spawns);
        Assert.Equal(0, vm.GameData.SelectedSpawn);
        Assert.False(vm.PasteMode);
        Assert.True(searchBox.IsFocused);

        window.KeyTextInput("a");
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("a", searchBox.Text);

        TextBox sourceX = Control<TextBox>(harness, "SpawnSourceX");
        sourceX.Focus();
        Dispatcher.UIThread.RunJobs();

        PressClipboardAndDeleteKeys(window);

        Assert.Null(harness.Workspace.Clipboard.Current);
        Assert.Single(vm.GameData.Session.Edits.Spawns);
        Assert.True(sourceX.IsFocused);
    }

    private static void PressClipboardAndDeleteKeys(MainWindow window)
    {
        window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Control);
        window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Control);
        window.KeyPressQwerty(PhysicalKey.V, RawInputModifiers.Control);
        window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }
}
