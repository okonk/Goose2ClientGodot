using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using MapEditor.App.Controls;
using MapEditor.App.Dialogs;
using MapEditor.Core;
using MapEditor.Rendering;
using Xunit;

namespace MapEditor.App.Tests;

public class ShortcutTests
{
    private static void PaintCell(MainWindowHarness harness)
    {
        MainWindow window = harness.Window;
        harness.ViewModel.Brush = new MapTileLayer(1, 1);
        Point tileCenter = window.Canvas.TranslatePoint(new Point(16, 16), window).Value;
        window.MouseDown(tileCenter, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(tileCenter, MouseButton.Left, RawInputModifiers.None);
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
}
