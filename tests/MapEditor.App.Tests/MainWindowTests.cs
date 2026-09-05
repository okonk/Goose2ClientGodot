using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MapEditor.App.Controls;
using MapEditor.App.Dialogs;
using MapEditor.App.Documents;
using MapEditor.App.Rendering;
using MapEditor.App.Settings;
using MapEditor.App.Tests.Fakes;
using MapEditor.App.ViewModels;
using MapEditor.Core;
using MapEditor.Rendering;
using Xunit;

namespace MapEditor.App.Tests;

internal sealed class MainWindowHarness : IDisposable
{
    public string TempDirectory { get; }

    public FakeEditorDialogs Dialogs { get; } = new();

    public AppSettingsStore Settings { get; }

    public WorkspaceViewModel Workspace { get; }

    public MapDocumentViewModel ViewModel { get; }

    public AssetContextController Assets { get; }

    public MainWindow Window { get; }

    private MainWindowHarness()
    {
        TempDirectory = Directory.CreateTempSubdirectory("map-editor-window-").FullName;
        Settings = new AppSettingsStore(Path.Combine(TempDirectory, "settings.json"));
        Workspace = new WorkspaceViewModel(Dialogs, new MapFileStore());
        ViewModel = Workspace.ActiveDocument;
        Assets = new AssetContextController(Workspace, Settings);
        Window = new MainWindow(Dialogs, Settings, Workspace, Assets);
    }

    public static MainWindowHarness Create()
    {
        var harness = new MainWindowHarness();
        harness.Window.Show();
        Dispatcher.UIThread.RunJobs();
        return harness;
    }

    public void Dispose()
    {
        if (Window.IsVisible)
        {
            Dialogs.DirtyResult = DirtyChoice.Discard;
            Window.Close();
            Dispatcher.UIThread.RunJobs();
        }

        Assets.Dispose();
        Directory.Delete(TempDirectory, recursive: true);
    }
}

public class MainWindowTests : IDisposable
{
    private const string TwoSheetJson = """
        { "tileSize": 32, "sheets": {
          "1": { "10": [0, 0, 32, 32], "11": [32, 0, 32, 32] },
          "2": { "20": [0, 0, 32, 32] }
        } }
        """;

    private const string NonContiguousSheetJson = """
        { "tileSize": 32, "sheets": {
          "2": { "10": [0, 0, 32, 32], "11": [32, 0, 32, 32] },
          "7": { "20": [0, 0, 32, 32], "21": [32, 0, 32, 32], "22": [64, 0, 32, 32] }
        } }
        """;

    private readonly MainWindowHarness _harness = MainWindowHarness.Create();

    public MainWindowTests()
    {
    }

    public void Dispose() => _harness.Dispose();

    private MainWindow Window => _harness.Window;

    private MapDocumentViewModel ViewModel => _harness.ViewModel;

    private T? TryFind<T>(string name) where T : Control
        => Window.FindControl<T>(name);

    private T Find<T>(string name) where T : Control
        => TryFind<T>(name)
           ?? throw new InvalidOperationException($"missing named region {name}");

    private string WriteAssetDirectory(string name, string manifestJson)
    {
        string directory = Path.Combine(_harness.TempDirectory, name);
        Directory.CreateDirectory(Path.Combine(directory, "sheets"));
        File.WriteAllText(Path.Combine(directory, "manifest.json"), manifestJson);
        return directory;
    }

    private string WriteManyFrameAssetDirectory()
    {
        var frames = new StringBuilder();
        for (int i = 0; i < 300; i++)
        {
            if (i > 0)
            {
                frames.Append(',');
            }

            frames.Append($"\"{100 + i}\":[0,0,32,32]");
        }

        return WriteAssetDirectory("assets-many", $"{{ \"tileSize\": 32, \"sheets\": {{ \"1\": {{ {frames} }} }} }}");
    }

    private Point TileCenter(int x = 0, int y = 0)
        => Window.Canvas.TranslatePoint(new Point(x * 32 + 16, y * 32 + 16), Window).Value;

    [AvaloniaFact]
    public void Layout_ContainsNamedMenuToolbarStatusBarAndPanels()
    {
        Assert.NotNull(Find<Menu>("Menu"));
        Assert.NotNull(Find<MenuItem>("FileMenu"));
        Assert.NotNull(Find<MenuItem>("EditMenu"));
        Assert.NotNull(Find<Border>("Toolbar"));
        Assert.NotNull(Find<Border>("StatusBar"));
        Assert.NotNull(Find<Grid>("Body"));
        Assert.NotNull(Find<DockPanel>("LeftPanel"));
        Assert.NotNull(Find<StackPanel>("RightPanel"));
        Assert.NotNull(Find<Border>("CanvasHost"));
        Assert.NotNull(Find<Grid>("PaletteHost"));
        Assert.NotNull(Find<Border>("PaletteBorder"));
        Assert.NotNull(Find<ScrollBar>("PaletteBar"));

        Assert.IsType<MapCanvas>(Find<Border>("CanvasHost").Child);
        Assert.IsType<SpritePaletteControl>(Find<Border>("PaletteBorder").Child);
        Assert.Same(Window.Canvas, Find<Border>("CanvasHost").Child);
        Assert.Same(Window.Palette, Find<Border>("PaletteBorder").Child);
    }

    [AvaloniaFact]
    public void Layout_ContainsFileEditToolbarCommands()
    {
        foreach (string name in new[] { "NewCommand", "OpenCommand", "SaveCommand", "SaveAsCommand", "ExitCommand" })
        {
            Assert.NotNull(Find<MenuItem>(name));
        }

        foreach (string name in new[] { "UndoCommand", "RedoCommand" })
        {
            Assert.NotNull(Find<MenuItem>(name));
        }

        foreach (string name in new[] { "NewButton", "OpenButton", "SaveButton", "UndoButton", "RedoButton", "ZoomInButton", "ZoomOutButton" })
        {
            Assert.Null(TryFind<Button>(name));
        }

        Assert.NotNull(Find<MenuItem>("ViewMenu"));
        Assert.NotNull(Find<MenuItem>("GridMenuItem"));
        Assert.NotNull(Find<MenuItem>("BlockedMenuItem"));
    }

    [AvaloniaFact]
    public void Layout_ContainsSevenToolTogglesWithPencilActive()
    {
        Assert.True(Find<ToggleButton>("PencilTool").IsChecked);
        Assert.False(Find<ToggleButton>("EraserTool").IsChecked);
        Assert.False(Find<ToggleButton>("EyedropperTool").IsChecked);
        Assert.False(Find<ToggleButton>("BlockedTool").IsChecked);
        Assert.False(Find<ToggleButton>("SelectTool").IsChecked);
        Assert.False(Find<ToggleButton>("MultiSelectTool").IsChecked);
        Assert.False(Find<ToggleButton>("FloodFillTool").IsChecked);
        Assert.Equal(MapEditTool.Pencil, ViewModel.ActiveTool);

        Assert.Equal(
            new[] { "SelectTool", "MultiSelectTool", "EyedropperTool", "PencilTool", "EraserTool", "FloodFillTool", "BlockedTool" },
            Find<Border>("Toolbar").GetVisualDescendants().OfType<ToggleButton>().Select(toggle => toggle.Name).ToArray());

        Find<ToggleButton>("EraserTool").IsChecked = true;
        Assert.Equal(MapEditTool.Eraser, ViewModel.ActiveTool);
        Assert.False(Find<ToggleButton>("PencilTool").IsChecked);
        Assert.True(Find<ToggleButton>("EraserTool").IsChecked);

        Find<ToggleButton>("PencilTool").IsChecked = true;
        Find<ToggleButton>("PencilTool").IsChecked = false;
        Assert.True(Find<ToggleButton>("PencilTool").IsChecked == true);
        Assert.Equal(MapEditTool.Pencil, ViewModel.ActiveTool);
    }

    [AvaloniaFact]
    public void Toolbar_SwitchingAwayFromMultiSelect_ClearsSelectionRectangle()
    {
        Find<ToggleButton>("MultiSelectTool").IsChecked = true;
        ViewModel.SelectionRectangle = new MapTileRectangle(0, 0, 2, 2);

        Find<ToggleButton>("PencilTool").IsChecked = true;

        Assert.Null(ViewModel.SelectionRectangle);
    }

    [AvaloniaFact]
    public void Layout_ContainsNamedLayerListAndViewToggles()
    {
        string[] names = { "Ground", "Below Entities", "Entities", "Above Entities", "Roof" };
        for (int layer = 0; layer < MapDocument.LayerCount; layer++)
        {
            Assert.NotNull(Find<Border>($"Layer{layer}Row"));
            Assert.True(Find<CheckBox>($"Layer{layer}VisibleCheck").IsChecked == true);
            Assert.Equal($"{names[layer]} ({layer})", Find<TextBlock>($"Layer{layer}Label").Text);
        }

        Assert.Equal((byte)1, ViewModel.SelectedLayers);
        Assert.NotEqual(Brushes.Transparent, Find<Border>("Layer0Row").Background);
        Assert.Equal(Brushes.Transparent, Find<Border>("Layer3Row").Background);

        Point row3 = Find<Border>("Layer3Row").TranslatePoint(new Point(10, 5), Window).Value;
        Window.MouseDown(row3, MouseButton.Left, RawInputModifiers.None);
        Window.MouseUp(row3, MouseButton.Left, RawInputModifiers.None);
        Assert.Equal((byte)0b01000, ViewModel.SelectedLayers);
        Assert.NotEqual(Brushes.Transparent, Find<Border>("Layer3Row").Background);
        Assert.Equal(Brushes.Transparent, Find<Border>("Layer0Row").Background);

        Point row1 = Find<Border>("Layer1Row").TranslatePoint(new Point(10, 5), Window).Value;
        Window.MouseDown(row1, MouseButton.Left, RawInputModifiers.None);
        Window.MouseUp(row1, MouseButton.Left, RawInputModifiers.None);
        Assert.Equal((byte)0b00010, ViewModel.SelectedLayers);

        Find<CheckBox>("Layer2VisibleCheck").IsChecked = false;
        Assert.Equal((byte)0b11011, ViewModel.LayerVisibility);

        Point check2 = Find<CheckBox>("Layer2VisibleCheck").TranslatePoint(new Point(5, 5), Window).Value;
        Window.MouseDown(check2, MouseButton.Left, RawInputModifiers.None);
        Window.MouseUp(check2, MouseButton.Left, RawInputModifiers.None);
        Assert.True(Find<CheckBox>("Layer2VisibleCheck").IsChecked == true);
        Assert.Equal((byte)0b11111, ViewModel.LayerVisibility);
        Assert.Equal((byte)0b00010, ViewModel.SelectedLayers);

        Find<MenuItem>("ViewMenu").IsSubMenuOpen = true;
        Dispatcher.UIThread.RunJobs();
        Point gridPoint = Find<MenuItem>("GridMenuItem").TranslatePoint(new Point(5, 5), Window).Value;
        Window.MouseDown(gridPoint, MouseButton.Left, RawInputModifiers.None);
        Window.MouseUp(gridPoint, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.False(Find<MenuItem>("GridMenuItem").IsChecked == true);
        Assert.False(ViewModel.ShowGrid);

        Find<MenuItem>("ViewMenu").IsSubMenuOpen = true;
        Dispatcher.UIThread.RunJobs();
        Point blockedPoint = Find<MenuItem>("BlockedMenuItem").TranslatePoint(new Point(5, 5), Window).Value;
        Window.MouseDown(blockedPoint, MouseButton.Left, RawInputModifiers.None);
        Window.MouseUp(blockedPoint, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.True(Find<MenuItem>("BlockedMenuItem").IsChecked == true);
        Assert.True(ViewModel.ShowBlocked);
    }

    [AvaloniaFact]
    public void StatusFields_ShowDefaultsAndFollowHoverAndZoom()
    {
        Assert.Equal("—", Find<TextBlock>("HoverText").Text);
        Assert.Equal("100%", Find<TextBlock>("ZoomText").Text);
        Assert.Equal("100 × 100", Find<TextBlock>("SizeText").Text);

        Point tileCenter = TileCenter();
        Window.MouseMove(tileCenter, RawInputModifiers.None);
        Assert.Equal("0, 0", Find<TextBlock>("HoverText").Text);

        Window.MouseWheel(tileCenter, new Vector(0, 1), RawInputModifiers.None);
        Assert.Equal("200%", Find<TextBlock>("ZoomText").Text);
    }

    [AvaloniaFact]
    public void SelectionReadout_ReflectsFreshDocumentAfterEditAndUndo()
    {
        Assert.Equal("—", Find<TextBlock>("SelectedText").Text);
        Assert.Equal("blocked: no", Find<TextBlock>("BlockedText").Text);
        for (int layer = 0; layer < MapDocument.LayerCount; layer++)
        {
            Assert.Equal($"L{layer} 0/0", Find<TextBlock>($"Layer{layer}Ref").Text);
        }

        ViewModel.Brush = new MapTileLayer(7, 42);
        ViewModel.SelectedLayers = 1 << 2;
        Point tileCenter = TileCenter(1, 1);
        Window.MouseDown(tileCenter, MouseButton.Left, RawInputModifiers.None);
        Window.MouseUp(tileCenter, MouseButton.Left, RawInputModifiers.None);

        Assert.Equal("1, 1", Find<TextBlock>("SelectedText").Text);
        Assert.Equal("blocked: no", Find<TextBlock>("BlockedText").Text);
        Assert.Equal("L2 7/42", Find<TextBlock>("Layer2Ref").Text);
        Assert.Equal("L0 0/0", Find<TextBlock>("Layer0Ref").Text);

        ViewModel.Undo();

        Assert.Equal("L2 0/0", Find<TextBlock>("Layer2Ref").Text);

        ViewModel.ActiveTool = MapEditTool.Blocked;
        Window.MouseDown(tileCenter, MouseButton.Left, RawInputModifiers.None);
        Window.MouseUp(tileCenter, MouseButton.Left, RawInputModifiers.None);
        Assert.Equal("blocked: yes", Find<TextBlock>("BlockedText").Text);
    }

    [AvaloniaFact]
    public void PaletteScrollBar_IsHiddenUntilTheGridOverflows()
    {
        ScrollBar bar = Find<ScrollBar>("PaletteBar");

        Assert.False(bar.IsVisible);

        _harness.Dialogs.AssetDirectoryPickResult = WriteManyFrameAssetDirectory();
        Find<Button>("LoadAssetsButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.True(bar.Maximum > 0);
        Assert.True(bar.IsVisible);
    }

    [AvaloniaFact]
    public void PaletteScrollBar_IsSynchronizedWithControlViewportAndExtent()
    {
        SpritePaletteControl palette = Window.Palette;
        ScrollBar bar = Find<ScrollBar>("PaletteBar");

        _harness.Dialogs.AssetDirectoryPickResult = WriteManyFrameAssetDirectory();
        Find<Button>("LoadAssetsButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.True(_harness.Assets.Current.IsAvailable);
        Assert.True(palette.ViewportHeight > 0);
        Assert.Equal(palette.ViewportHeight, bar.ViewportSize);
        Assert.Equal(Math.Max(0, palette.ExtentHeight - palette.ViewportHeight), bar.Maximum);
        Assert.True(bar.Maximum > 0);

        double barTarget = Math.Min(100, bar.Maximum);
        bar.Value = barTarget;
        Assert.Equal(barTarget, palette.Offset);

        double offsetTarget = Math.Min(50, bar.Maximum);
        palette.Offset = offsetTarget;
        Assert.Equal(offsetTarget, bar.Value);
    }

    [AvaloniaFact]
    public async Task CommandEnablement_FollowsSessionState()
    {
        MenuItem undo = Find<MenuItem>("UndoCommand");
        MenuItem redo = Find<MenuItem>("RedoCommand");
        MenuItem save = Find<MenuItem>("SaveCommand");

        Assert.False(undo.IsEnabled);
        Assert.False(redo.IsEnabled);
        Assert.False(save.IsEnabled);

        ViewModel.Brush = new MapTileLayer(1, 1);
        Point tileCenter = TileCenter();
        Window.MouseDown(tileCenter, MouseButton.Left, RawInputModifiers.None);
        Window.MouseUp(tileCenter, MouseButton.Left, RawInputModifiers.None);

        Assert.True(undo.IsEnabled);
        Assert.True(save.IsEnabled);
        Assert.False(redo.IsEnabled);

        ViewModel.Undo();

        Assert.False(undo.IsEnabled);
        Assert.True(redo.IsEnabled);

        _harness.Dialogs.SavePickResult = Path.Combine(_harness.TempDirectory, "saved.bytes");
        await ViewModel.SaveAsync();

        Assert.False(save.IsEnabled);
    }

    [AvaloniaFact]
    public void HotKeys_UseControlOnThisPlatformAndMatchHandling()
    {
        Assert.False(OperatingSystem.IsMacOS());

        Assert.Equal(new KeyGesture(Key.N, KeyModifiers.Control), (KeyGesture)Find<MenuItem>("NewCommand").HotKey!);
        Assert.Equal(new KeyGesture(Key.O, KeyModifiers.Control), (KeyGesture)Find<MenuItem>("OpenCommand").HotKey!);
        Assert.Equal(new KeyGesture(Key.S, KeyModifiers.Control), (KeyGesture)Find<MenuItem>("SaveCommand").HotKey!);
        Assert.Equal(new KeyGesture(Key.S, KeyModifiers.Control | KeyModifiers.Shift), (KeyGesture)Find<MenuItem>("SaveAsCommand").HotKey!);
        Assert.Equal(new KeyGesture(Key.Z, KeyModifiers.Control), (KeyGesture)Find<MenuItem>("UndoCommand").HotKey!);
        Assert.Equal(new KeyGesture(Key.Y, KeyModifiers.Control), (KeyGesture)Find<MenuItem>("RedoCommand").HotKey!);

        Assert.Equal(KeyModifiers.Meta, MainWindow.BuildShortcut(Key.N, KeyModifiers.None, isMacOs: true).KeyModifiers);
        Assert.Equal(KeyModifiers.Meta | KeyModifiers.Shift, MainWindow.BuildShortcut(Key.Z, KeyModifiers.Shift, isMacOs: true).KeyModifiers);
        Assert.Equal(KeyModifiers.Control, MainWindow.BuildShortcut(Key.N, KeyModifiers.None, isMacOs: false).KeyModifiers);
        Assert.Equal(KeyModifiers.Control | KeyModifiers.Shift, MainWindow.BuildShortcut(Key.S, KeyModifiers.Shift, isMacOs: false).KeyModifiers);
    }

    [AvaloniaFact]
    public void ResizeMenuItem_ShowsTheDialogAndAppliesTheReturnedWindow()
    {
        _harness.Dialogs.ResizeMapResult = new MapTileRectangle(0, 0, 6, 6);
        Find<MenuItem>("ResizeCommand").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, _harness.Dialogs.ResizeMapShown);
        Assert.Equal(6, ViewModel.MapWidth);
        Assert.Equal(6, ViewModel.MapHeight);
    }

    [AvaloniaFact]
    public void PlusMinusKeys_ZoomAroundCanvasCenter()
    {
        MapCanvas canvas = Window.Canvas;
        Point center = new(canvas.Bounds.Width / 2, canvas.Bounds.Height / 2);
        RenderPoint worldBefore = canvas.Viewport.ScreenToWorld(new RenderPoint(center.X, center.Y));

        Window.KeyPressQwerty(PhysicalKey.Equal, RawInputModifiers.None);
        Assert.Equal(200, ViewModel.ZoomPercent);
        Assert.Equal(worldBefore, canvas.Viewport.ScreenToWorld(new RenderPoint(center.X, center.Y)));

        Window.KeyPressQwerty(PhysicalKey.Minus, RawInputModifiers.None);
        Assert.Equal(100, ViewModel.ZoomPercent);
    }

    [AvaloniaFact]
    public void UnmodifiedVMBX_SelectToolToggles()
    {
        Window.KeyPressQwerty(PhysicalKey.V, RawInputModifiers.None);
        Assert.Equal(MapEditTool.Select, ViewModel.ActiveTool);
        Assert.True(Find<ToggleButton>("SelectTool").IsChecked);

        Window.KeyPressQwerty(PhysicalKey.M, RawInputModifiers.None);
        Assert.Equal(MapEditTool.MultiSelect, ViewModel.ActiveTool);
        Assert.True(Find<ToggleButton>("MultiSelectTool").IsChecked);

        Window.KeyPressQwerty(PhysicalKey.B, RawInputModifiers.None);
        Assert.Equal(MapEditTool.FloodFill, ViewModel.ActiveTool);
        Assert.True(Find<ToggleButton>("FloodFillTool").IsChecked);

        Window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.None);
        Assert.Equal(MapEditTool.Blocked, ViewModel.ActiveTool);
        Assert.True(Find<ToggleButton>("BlockedTool").IsChecked);
    }

    [AvaloniaFact]
    public void CtrlC_PopulatesClipboardAndCtrlV_EntersPasteMode()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        harness.ViewModel.Session.Document.SetLayer(0, 0, 0, new MapTileLayer(7, 7));
        harness.ViewModel.SelectionRectangle = new MapTileRectangle(0, 0, 1, 1);

        harness.Window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Control);
        Assert.NotNull(harness.ViewModel.Clipboard);

        harness.Window.KeyPressQwerty(PhysicalKey.V, RawInputModifiers.Control);
        Assert.True(harness.ViewModel.PasteMode);
    }

    [AvaloniaFact]
    public void CtrlC_WithoutSelectionRectangle_DoesNothing()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        harness.Window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Control);
        Assert.Null(harness.ViewModel.Clipboard);
    }

    [AvaloniaFact]
    public void CtrlC_WithBrushFieldFocused_KeepsNativeBehavior()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        harness.ViewModel.SelectionRectangle = new MapTileRectangle(0, 0, 1, 1);
        harness.Window.FindControl<TextBox>("BrushGraphic").Focus();

        harness.Window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Control);
        Assert.Null(harness.ViewModel.Clipboard);
    }

    [AvaloniaFact]
    public void DeleteKey_ClearsTilesOnSelectedLayers()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        harness.ViewModel.SelectedLayers = 0b01001;
        harness.ViewModel.Session.Document.SetLayer(0, 0, 0, new MapTileLayer(7, 7));
        harness.ViewModel.Session.Document.SetLayer(0, 0, 1, new MapTileLayer(8, 8));
        harness.ViewModel.SelectionRectangle = new MapTileRectangle(0, 0, 1, 1);

        harness.Window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);

        Assert.Equal(new MapTileLayer(0, 0), harness.ViewModel.Session.Document[0, 0].GetLayer(0));
        Assert.Equal(new MapTileLayer(8, 8), harness.ViewModel.Session.Document[0, 0].GetLayer(1));
        Assert.True(harness.ViewModel.CanUndo);
    }

    [AvaloniaFact]
    public void DeleteKey_WithoutSelectionRectangle_DoesNothing()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        harness.ViewModel.Session.Document.SetLayer(0, 0, 0, new MapTileLayer(7, 7));

        harness.Window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);

        Assert.Equal(new MapTileLayer(7, 7), harness.ViewModel.Session.Document[0, 0].GetLayer(0));
        Assert.False(harness.ViewModel.CanUndo);
    }

    [AvaloniaFact]
    public void DeleteKey_WithBrushFieldFocused_KeepsNativeBehavior()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        harness.ViewModel.Session.Document.SetLayer(0, 0, 0, new MapTileLayer(7, 7));
        harness.ViewModel.SelectionRectangle = new MapTileRectangle(0, 0, 1, 1);
        harness.Window.FindControl<TextBox>("BrushGraphic").Focus();

        harness.Window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);

        Assert.Equal(new MapTileLayer(7, 7), harness.ViewModel.Session.Document[0, 0].GetLayer(0));
        Assert.False(harness.ViewModel.CanUndo);
    }

    [AvaloniaFact]
    public void CtrlX_CopiesSelectionAndClearsTiles()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        harness.ViewModel.Session.Document.SetLayer(0, 0, 0, new MapTileLayer(7, 7));
        harness.ViewModel.SelectionRectangle = new MapTileRectangle(0, 0, 1, 1);

        harness.Window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Control);

        Assert.NotNull(harness.ViewModel.Clipboard);
        Assert.Equal(new MapTileLayer(0, 0), harness.ViewModel.Session.Document[0, 0].GetLayer(0));
        Assert.True(harness.ViewModel.CanUndo);
    }

    [AvaloniaFact]
    public void CtrlX_WithoutSelectionRectangle_DoesNothing()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        harness.ViewModel.Session.Document.SetLayer(0, 0, 0, new MapTileLayer(7, 7));

        harness.Window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Control);

        Assert.Null(harness.ViewModel.Clipboard);
        Assert.Equal(new MapTileLayer(7, 7), harness.ViewModel.Session.Document[0, 0].GetLayer(0));
        Assert.False(harness.ViewModel.CanUndo);
    }

    [AvaloniaFact]
    public void CtrlX_WithBrushFieldFocused_KeepsNativeBehavior()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        harness.ViewModel.Session.Document.SetLayer(0, 0, 0, new MapTileLayer(7, 7));
        harness.ViewModel.SelectionRectangle = new MapTileRectangle(0, 0, 1, 1);
        harness.Window.FindControl<TextBox>("BrushGraphic").Focus();

        harness.Window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Control);

        Assert.Null(harness.ViewModel.Clipboard);
        Assert.Equal(new MapTileLayer(7, 7), harness.ViewModel.Session.Document[0, 0].GetLayer(0));
        Assert.False(harness.ViewModel.CanUndo);
    }

    [AvaloniaFact]
    public void Escape_CancelsPasteMode()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        harness.ViewModel.Session.Document.SetLayer(0, 0, 0, new MapTileLayer(7, 7));
        harness.ViewModel.SelectionRectangle = new MapTileRectangle(0, 0, 1, 1);
        harness.ViewModel.CopySelection();
        harness.ViewModel.BeginPasteMode();
        Assert.True(harness.ViewModel.PasteMode);

        harness.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

        Assert.False(harness.ViewModel.PasteMode);
        Assert.False(harness.ViewModel.Session.CanUndo);
    }

    [AvaloniaFact]
    public void Escape_WithBrushFieldFocused_CancelsPasteMode()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        harness.ViewModel.Session.Document.SetLayer(0, 0, 0, new MapTileLayer(7, 7));
        harness.ViewModel.SelectionRectangle = new MapTileRectangle(0, 0, 1, 1);
        harness.ViewModel.CopySelection();
        harness.ViewModel.BeginPasteMode();
        harness.Window.FindControl<TextBox>("BrushGraphic").Focus();

        harness.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

        Assert.False(harness.ViewModel.PasteMode);
        Assert.False(harness.ViewModel.Session.CanUndo);
    }

    [AvaloniaFact]
    public void Escape_WithCanvasFocused_CancelsPasteMode()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        harness.ViewModel.Session.Document.SetLayer(0, 0, 0, new MapTileLayer(7, 7));
        harness.ViewModel.SelectionRectangle = new MapTileRectangle(0, 0, 1, 1);
        harness.ViewModel.CopySelection();
        harness.ViewModel.BeginPasteMode();
        harness.Window.Canvas.Focus();

        harness.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

        Assert.False(harness.ViewModel.PasteMode);
        Assert.False(harness.ViewModel.Session.CanUndo);
    }

    [AvaloniaFact]
    public async Task Title_TracksDirtyState()
    {
        Assert.Equal("Goose2 Map Editor — Untitled", Window.Title);

        _harness.ViewModel.Brush = new MapTileLayer(1, 2);
        _harness.ViewModel.Session.BeginStroke(MapEditTool.Pencil, 0, 0);
        Assert.True(_harness.ViewModel.Session.CompleteStroke());
        _harness.ViewModel.Refresh(EditorRefresh.Title);
        Assert.Equal("Goose2 Map Editor — Untitled*", Window.Title);

        _harness.Dialogs.SavePickResult = Path.Combine(_harness.TempDirectory, "titled.bytes");
        await ViewModel.SaveAsync();

        Assert.Equal("Goose2 Map Editor — titled.bytes", Window.Title);
    }

    [AvaloniaFact]
    public void BrushFields_ValidateSignedIntAndTrackBrush()
    {
        TextBox sheet = Find<TextBox>("BrushSheet");
        TextBox graphic = Find<TextBox>("BrushGraphic");
        TextBlock validation = Find<TextBlock>("BrushValidationError");

        Assert.Equal("0", sheet.Text);
        Assert.Equal("0", graphic.Text);
        Assert.False(validation.IsVisible);

        sheet.Text = "abc";
        Assert.True(validation.IsVisible);
        Assert.Equal(new MapTileLayer(0, 0), ViewModel.Brush);

        sheet.Text = "3";
        graphic.Text = "-12";
        Assert.False(validation.IsVisible);
        Assert.Equal(new MapTileLayer(3, -12), ViewModel.Brush);

        ViewModel.Brush = new MapTileLayer(9, 21);
        Assert.Equal("9", sheet.Text);
        Assert.Equal("21", graphic.Text);
    }

    [AvaloniaFact]
    public void Startup_HostsTheInitialCanvasAndPalette()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();

        Assert.NotNull(harness.Window.FindControl<Border>("CanvasHost")!.Child);
        Assert.IsType<MapCanvas>(harness.Window.FindControl<Border>("CanvasHost")!.Child);
        Assert.Same(harness.Window.Canvas, harness.Window.FindControl<Border>("CanvasHost")!.Child);

        Assert.NotNull(harness.Window.FindControl<Border>("PaletteBorder")!.Child);
        Assert.IsType<SpritePaletteControl>(harness.Window.FindControl<Border>("PaletteBorder")!.Child);
        Assert.Same(harness.Window.Palette, harness.Window.FindControl<Border>("PaletteBorder")!.Child);
    }

    [AvaloniaFact]
    public async Task Activate_SwitchingDocuments_KeepsEachViewportIndependent()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        MapDocumentViewModel first = harness.ViewModel;
        harness.Window.Canvas.ZoomStep(zoomIn: true);
        Assert.Equal(200, first.ZoomPercent);

        harness.Dialogs.NewMapResult = new NewMapRequest(100, 100);
        await harness.Workspace.NewAsync();
        MapDocumentViewModel second = harness.Workspace.ActiveDocument;
        Assert.Equal(100, second.ZoomPercent);

        harness.Workspace.Activate(first);
        Assert.Equal(200, first.ZoomPercent);

        harness.Workspace.Activate(second);
        Assert.Equal(100, second.ZoomPercent);
    }

    [AvaloniaFact]
    public async Task Activate_MidStroke_CommitsTheStroke()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        MapDocumentViewModel first = harness.ViewModel;
        first.Brush = new MapTileLayer(3, 9);
        Point tileCenter = harness.Window.Canvas.TranslatePoint(new Point(16, 16), harness.Window).Value;
        harness.Window.MouseDown(tileCenter, MouseButton.Left, RawInputModifiers.None);
        Assert.True(first.Session.HasActiveStroke);

        harness.Dialogs.NewMapResult = new NewMapRequest(100, 100);
        await harness.Workspace.NewAsync();

        Assert.False(first.Session.HasActiveStroke);
        Assert.True(first.CanUndo);
        Assert.True(first.Session.IsDirty);
    }

    [AvaloniaFact]
    public async Task Activate_DeactivatedCanvas_StopsTrackingHover()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        MapDocumentViewModel first = harness.ViewModel;
        Point firstTile = harness.Window.Canvas.TranslatePoint(new Point(48, 16), harness.Window).Value;
        harness.Window.MouseMove(firstTile, RawInputModifiers.None);
        Assert.Equal(1, first.HoverX);
        Assert.Equal(0, first.HoverY);

        harness.Dialogs.NewMapResult = new NewMapRequest(100, 100);
        await harness.Workspace.NewAsync();

        Point otherTile = harness.Window.Canvas.TranslatePoint(new Point(16, 16), harness.Window).Value;
        harness.Window.MouseMove(otherTile, RawInputModifiers.None);

        Assert.Equal(1, first.HoverX);
        Assert.Equal(0, first.HoverY);
    }

    [AvaloniaFact]
    public async Task SpaceLatch_IsResetWhenTheDocumentIsSwitchedAway()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        MapDocumentViewModel first = harness.ViewModel;
        MapCanvas firstCanvas = harness.Window.Canvas;
        firstCanvas.Focus();
        harness.Window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);

        harness.Dialogs.NewMapResult = new NewMapRequest(100, 100);
        await harness.Workspace.NewAsync();
        MapDocumentViewModel second = harness.Workspace.ActiveDocument;
        harness.Window.Canvas.Focus();
        harness.Window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);

        harness.Workspace.Activate(first);
        first.Brush = new MapTileLayer(2, 2);
        Point tile = harness.Window.Canvas.TranslatePoint(new Point(16, 16), harness.Window).Value;
        harness.Window.MouseDown(tile, MouseButton.Left, RawInputModifiers.None);
        Assert.True(first.Session.HasActiveStroke);
        harness.Window.MouseUp(tile, MouseButton.Left, RawInputModifiers.None);

        Assert.Equal(new MapTileLayer(2, 2), first.Session.Document[0, 0].GetLayer(0));
        Assert.Equal(new RenderPoint(0, 0), firstCanvas.Viewport.WorldOrigin);
        Assert.Equal(MapZoom.Percent100, firstCanvas.Viewport.Zoom);
    }

    [AvaloniaFact]
    public async Task Move_ActiveDocument_KeepsItsViewsRegistered()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        MapDocumentViewModel first = harness.ViewModel;
        MapCanvas firstCanvas = harness.Window.Canvas;
        SpritePaletteControl firstPalette = harness.Window.Palette;
        ViewportTransform viewport = firstCanvas.Viewport;

        harness.Dialogs.NewMapResult = new NewMapRequest(100, 100);
        await harness.Workspace.NewAsync();

        harness.Workspace.Move(0, 1);
        MapDocumentViewModel second = harness.Workspace.ActiveDocument;
        harness.Workspace.Activate(second);
        harness.Workspace.Activate(first);

        Assert.Same(firstCanvas, harness.Window.Canvas);
        Assert.Same(firstPalette, harness.Window.Palette);
        Assert.Equal(viewport.WorldOrigin, firstCanvas.Viewport.WorldOrigin);
        Assert.Equal(viewport.Zoom, firstCanvas.Viewport.Zoom);
    }

    [AvaloniaFact]
    public async Task Move_InactiveDocument_KeepsItsViewsRegistered()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        harness.Dialogs.NewMapResult = new NewMapRequest(100, 100);
        await harness.Workspace.NewAsync();
        MapDocumentViewModel second = harness.Workspace.ActiveDocument;
        MapCanvas secondCanvas = harness.Window.Canvas;
        SpritePaletteControl secondPalette = harness.Window.Palette;
        ViewportTransform viewport = secondCanvas.Viewport;

        harness.Workspace.Move(1, 0);
        harness.Workspace.Activate(second);

        Assert.Same(secondCanvas, harness.Window.Canvas);
        Assert.Same(secondPalette, harness.Window.Palette);
        Assert.Equal(viewport.WorldOrigin, secondCanvas.Viewport.WorldOrigin);
        Assert.Equal(viewport.Zoom, secondCanvas.Viewport.Zoom);
    }

    [AvaloniaFact]
    public async Task Activate_RebindsChrome()
    {
        MapDocumentViewModel first = ViewModel;
        first.Brush = new MapTileLayer(3, 5);
        first.Session.BeginStroke(MapEditTool.Pencil, 0, 0);
        Assert.True(first.Session.CompleteStroke());
        first.Refresh(EditorRefresh.Title);

        _harness.Dialogs.NewMapResult = new NewMapRequest(100, 100);
        await _harness.Workspace.NewAsync();
        MapDocumentViewModel second = _harness.Workspace.ActiveDocument;

        first.ActiveTool = MapEditTool.Eraser;
        first.SelectedLayers = (byte)0b00100;
        second.ActiveTool = MapEditTool.Select;
        second.Brush = new MapTileLayer(9, 1);
        second.SelectedLayers = (byte)0b00010;

        _harness.Workspace.Activate(first);
        Assert.True(Find<ToggleButton>("EraserTool").IsChecked);
        Assert.False(Find<ToggleButton>("SelectTool").IsChecked);
        Assert.Equal("3", Find<TextBox>("BrushSheet").Text);
        Assert.Equal("5", Find<TextBox>("BrushGraphic").Text);
        Assert.NotEqual(Brushes.Transparent, Find<Border>("Layer2Row").Background);
        Assert.Equal(Brushes.Transparent, Find<Border>("Layer1Row").Background);
        Assert.Equal(first.Title, Window.Title);

        _harness.Workspace.Activate(second);
        Assert.True(Find<ToggleButton>("SelectTool").IsChecked);
        Assert.False(Find<ToggleButton>("EraserTool").IsChecked);
        Assert.Equal("9", Find<TextBox>("BrushSheet").Text);
        Assert.Equal("1", Find<TextBox>("BrushGraphic").Text);
        Assert.NotEqual(Brushes.Transparent, Find<Border>("Layer1Row").Background);
        Assert.Equal(Brushes.Transparent, Find<Border>("Layer2Row").Background);
        Assert.Equal(second.Title, Window.Title);
    }

    [AvaloniaFact]
    public async Task PaletteBar_AfterSwitch_ScrollsOnlyTheActivePalette()
    {
        MapDocumentViewModel first = ViewModel;
        SpritePaletteControl firstPalette = Window.Palette;
        _harness.Dialogs.AssetDirectoryPickResult = WriteManyFrameAssetDirectory();
        Find<Button>("LoadAssetsButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        ScrollBar bar = Find<ScrollBar>("PaletteBar");
        Assert.True(bar.IsVisible);
        bar.Value = 100;
        Assert.Equal(100, firstPalette.Offset);

        _harness.Dialogs.NewMapResult = new NewMapRequest(100, 100);
        await _harness.Workspace.NewAsync();
        SpritePaletteControl secondPalette = Window.Palette;
        Dispatcher.UIThread.RunJobs();

        bar.Value = 50;
        Assert.Equal(50, secondPalette.Offset);
        Assert.Equal(100, firstPalette.Offset);
    }

    [AvaloniaFact]
    public async Task CloseDocument_UnhostsAndDropsItsViews()
    {
        MapDocumentViewModel first = ViewModel;
        MapCanvas firstCanvas = Window.Canvas;

        _harness.Dialogs.NewMapResult = new NewMapRequest(100, 100);
        await _harness.Workspace.NewAsync();
        MapDocumentViewModel second = _harness.Workspace.ActiveDocument;
        MapCanvas secondCanvas = Window.Canvas;
        SpritePaletteControl secondPalette = Window.Palette;
        Assert.True(Window.HasViewFor(first));
        Assert.NotSame(firstCanvas, secondCanvas);

        Assert.True(await _harness.Workspace.CloseAsync(first));

        Assert.False(Window.HasViewFor(first));
        Assert.Same(secondCanvas, Find<Border>("CanvasHost").Child);
        Assert.Same(secondPalette, Find<Border>("PaletteBorder").Child);
    }

    [AvaloniaFact]
    public async Task LoadAssetsButton_PublishesContextAndUpdatesLeftPanel()
    {
        _harness.Dialogs.AssetDirectoryPickResult = WriteAssetDirectory("assets-good", TwoSheetJson);
        Find<Button>("LoadAssetsButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.True(_harness.Assets.Current.IsAvailable);
        Assert.Equal(Path.GetFullPath(_harness.Dialogs.AssetDirectoryPickResult), Find<TextBlock>("AssetDirectoryText").Text);
        Assert.Equal(2, Find<ComboBox>("SheetCombo").Items.Count);
        Assert.Equal(1, ViewModel.SelectedSheet);
    }

    [AvaloniaFact]
    public async Task LoadAssetsButton_BadManifest_ShowsLoadAssetsErrorAndKeepsOldContext()
    {
        string oldDirectory = WriteAssetDirectory("assets-old", TwoSheetJson);
        _harness.Dialogs.AssetDirectoryPickResult = oldDirectory;
        Find<Button>("LoadAssetsButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.True(_harness.Assets.Current.IsAvailable);

        string emptyDirectory = Path.Combine(_harness.TempDirectory, "assets-empty");
        Directory.CreateDirectory(emptyDirectory);
        _harness.Dialogs.AssetDirectoryPickResult = emptyDirectory;
        Find<Button>("LoadAssetsButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Single(_harness.Dialogs.Errors);
        Assert.Equal("Load assets", _harness.Dialogs.Errors[0].Title);
        Assert.Contains(emptyDirectory, _harness.Dialogs.Errors[0].Message);
        Assert.True(_harness.Assets.Current.IsAvailable);
        Assert.Equal(Path.GetFullPath(oldDirectory), _harness.Assets.Current.Cache.AssetDirectory);
    }

    [AvaloniaFact]
    public void SheetCombo_BindsSelectedItemBySheetId_NotListPosition()
    {
        _harness.Dialogs.AssetDirectoryPickResult = WriteAssetDirectory("assets-noncontiguous", NonContiguousSheetJson);
        Find<Button>("LoadAssetsButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        ComboBox combo = Find<ComboBox>("SheetCombo");
        Assert.Equal(2, combo.SelectedItem);
        Assert.Equal(2, ViewModel.SelectedSheet);
        Assert.Equal(2, Window.Palette.FrameCount);

        combo.SelectedItem = 7;
        Assert.Equal(7, ViewModel.SelectedSheet);
        Assert.Equal(3, Window.Palette.FrameCount);

        ViewModel.SelectedSheet = 2;
        Assert.Equal(2, combo.SelectedItem);
        Assert.Equal(2, Window.Palette.FrameCount);
    }

    [AvaloniaFact]
    public async Task LoadAssetsButton_UnexpectedExceptionAtWindowBoundary_ShowsLastErrorDialogAndStaysUsable()
    {
        EditorDocument before = _harness.ViewModel.Document;
        _harness.Dialogs.PickAssetDirectoryException = new InvalidOperationException("pick failed");

        Find<Button>("LoadAssetsButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        ErrorPresentation lastResort = Assert.Single(_harness.Dialogs.Errors, error => error.Title == "Error");
        Assert.Contains("pick failed", lastResort.Message);
        Assert.Same(before, _harness.ViewModel.Document);
        Assert.True(Window.IsVisible);
        Assert.False(_harness.Assets.Current.IsAvailable);
    }

    [AvaloniaFact]
    public async Task LoadAssetsButton_PickerCancelled_LeavesUnavailableContext()
    {
        Find<Button>("LoadAssetsButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, _harness.Dialogs.AssetDirectoryPickShown);
        Assert.Empty(_harness.Dialogs.Errors);
        Assert.False(_harness.Assets.Current.IsAvailable);
    }
}
