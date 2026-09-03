using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using MapEditor.App.Dialogs;
using MapEditor.Core;
using Xunit;

namespace MapEditor.App.Tests;

public class MainWindowCloseTests
{
    private sealed class ClosingCounter
    {
        public ClosingCounter(MainWindow window)
        {
            window.Closing += (sender, e) => Count++;
        }

        public int Count { get; private set; }
    }

    [AvaloniaFact]
    public void Close_CleanDocument_ClosesWithoutPrompt()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        string mapPath = Path.Combine(harness.TempDirectory, "clean.bytes");
        new MapFileStore().Save(mapPath, MapDocument.Create(10, 10));
        harness.Dialogs.OpenPickResult = mapPath;
        harness.Dialogs.DirtyResult = DirtyChoice.Discard;
        harness.Window.FindControl<Avalonia.Controls.Button>("OpenButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.False(harness.ViewModel.Session.IsDirty);

        var counter = new ClosingCounter(harness.Window);
        harness.Window.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.False(harness.Window.IsVisible);
        Assert.Equal(1, harness.Dialogs.DirtyShown);
        Assert.Equal(2, counter.Count);
    }

    [AvaloniaFact]
    public void Close_DirtyDocument_Discard_PromptsOnceThenCloses()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        harness.Dialogs.DirtyResult = DirtyChoice.Discard;

        var counter = new ClosingCounter(harness.Window);
        harness.Window.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.False(harness.Window.IsVisible);
        Assert.Equal(1, harness.Dialogs.DirtyShown);
        Assert.Equal(2, counter.Count);
    }

    [AvaloniaFact]
    public void Close_DirtyDocument_Save_SavesThenCloses()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        string mapPath = Path.Combine(harness.TempDirectory, "closed-saved.bytes");
        harness.Dialogs.DirtyResult = DirtyChoice.Save;
        harness.Dialogs.SavePickResult = mapPath;

        var counter = new ClosingCounter(harness.Window);
        harness.Window.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.False(harness.Window.IsVisible);
        Assert.Equal(1, harness.Dialogs.DirtyShown);
        Assert.Equal(1, harness.Dialogs.SavePickShown);
        Assert.True(File.Exists(mapPath));
        Assert.False(harness.ViewModel.Session.IsDirty);
        Assert.Equal(2, counter.Count);
    }

    [AvaloniaFact]
    public void Close_DirtyDocument_Cancel_StaysOpen()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        harness.Dialogs.DirtyResult = DirtyChoice.Cancel;

        var counter = new ClosingCounter(harness.Window);
        harness.Window.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.True(harness.Window.IsVisible);
        Assert.Equal(1, harness.Dialogs.DirtyShown);
        Assert.True(harness.ViewModel.Session.IsDirty);
        Assert.Equal(1, counter.Count);
    }

    [AvaloniaFact]
    public void Close_DirtyDocument_SavePickerCancelled_StaysOpenAndDirty()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        harness.Dialogs.DirtyResult = DirtyChoice.Save;
        harness.Dialogs.SavePickResult = null;

        var counter = new ClosingCounter(harness.Window);
        harness.Window.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.True(harness.Window.IsVisible);
        Assert.Equal(1, harness.Dialogs.DirtyShown);
        Assert.Equal(1, harness.Dialogs.SavePickShown);
        Assert.True(harness.ViewModel.Session.IsDirty);
        Assert.Equal(1, counter.Count);
    }

    [AvaloniaFact]
    public void Close_DirtyDocument_SaveFails_ShowsErrorAndStaysOpen()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        harness.Dialogs.DirtyResult = DirtyChoice.Save;
        harness.Dialogs.PickSaveException = new IOException("disk full");

        var counter = new ClosingCounter(harness.Window);
        harness.Window.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.True(harness.Window.IsVisible);
        Assert.True(harness.ViewModel.Session.IsDirty);
        Assert.Single(harness.Dialogs.Errors);
        Assert.Equal("Save map", harness.Dialogs.Errors[0].Title);
        Assert.Equal(1, counter.Count);
    }

    [AvaloniaFact]
    public void Close_DialogFails_ShowsCloseErrorAndStaysOpen()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        harness.Dialogs.ShowDirtyException = new InvalidOperationException("dialog down");

        var counter = new ClosingCounter(harness.Window);
        harness.Window.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.True(harness.Window.IsVisible);
        Assert.Equal(1, harness.Dialogs.DirtyShown);
        Assert.Single(harness.Dialogs.Errors);
        Assert.Equal("Close", harness.Dialogs.Errors[0].Title);
        Assert.Equal(1, counter.Count);
    }

    [AvaloniaFact]
    public void Close_DuplicateClosingWhilePromptPending_SinglePromptAndNoRecursion()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        var gate = new TaskCompletionSource<DirtyChoice>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Dialogs.DirtyGate = gate;

        var counter = new ClosingCounter(harness.Window);
        harness.Window.Close();
        harness.Window.Close();
        harness.Window.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.True(harness.Window.IsVisible);
        Assert.Equal(1, harness.Dialogs.DirtyShown);
        Assert.Equal(3, counter.Count);

        gate.SetResult(DirtyChoice.Discard);
        Dispatcher.UIThread.RunJobs();

        Assert.False(harness.Window.IsVisible);
        Assert.Equal(4, counter.Count);
    }

    [AvaloniaFact]
    public void Close_DragInProgress_Save_CommitsStrokeOnceAndSavesCompletedStroke()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        string mapPath = Path.Combine(harness.TempDirectory, "drag-close.bytes");
        harness.Dialogs.DirtyResult = DirtyChoice.Save;
        harness.Dialogs.SavePickResult = mapPath;
        harness.ViewModel.Brush = new MapTileLayer(4, 6);
        Point tileCenter = harness.Window.Canvas.TranslatePoint(new Point(16, 16), harness.Window).Value;
        harness.Window.MouseDown(tileCenter, MouseButton.Left, RawInputModifiers.None);
        Assert.True(harness.ViewModel.Session.HasActiveStroke);

        harness.Window.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.False(harness.Window.IsVisible);
        Assert.False(harness.ViewModel.Session.HasActiveStroke);
        Assert.Empty(harness.Dialogs.Errors);
        Assert.Equal(1, harness.Dialogs.SavePickShown);
        Assert.True(File.Exists(mapPath));
        Assert.Equal(new MapTileLayer(4, 6), new MapFileStore().Open(mapPath).Document[0, 0].GetLayer(0));

        Assert.True(harness.ViewModel.CanUndo);
        Assert.True(harness.ViewModel.Undo());
        Assert.False(harness.ViewModel.CanUndo);
        Assert.Equal(new MapTileLayer(0, 0), harness.ViewModel.Session.Document[0, 0].GetLayer(0));
    }

    [AvaloniaFact]
    public void Close_Approved_DisposesAssetContext()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        harness.Dialogs.DirtyResult = DirtyChoice.Discard;

        harness.Window.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.False(harness.Window.IsVisible);
        Assert.True(harness.Assets.Current.IsDisposed);
    }

    [AvaloniaFact]
    public void Close_Canceled_KeepsAssetContextUsable()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        harness.Dialogs.DirtyResult = DirtyChoice.Cancel;

        harness.Window.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.True(harness.Window.IsVisible);
        Assert.False(harness.Assets.Current.IsDisposed);
        Assert.Empty(harness.Assets.Current.GetFrames(1));
    }

    [AvaloniaFact]
    public void Close_WhileAssetPickerPending_CompletingAfterClose_DoesNotPublishContext()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        string assets = WriteAssetDirectory(harness.TempDirectory);
        var gate = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Dialogs.AssetDirectoryPickGate = gate;
        harness.Dialogs.DirtyResult = DirtyChoice.Discard;

        harness.Window.FindControl<Avalonia.Controls.Button>("LoadAssetsButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, harness.Dialogs.AssetDirectoryPickShown);
        Assert.Empty(harness.ViewModel.SheetIds);

        harness.Window.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.False(harness.Window.IsVisible);
        Assert.True(harness.Assets.Current.IsDisposed);

        gate.SetResult(assets);
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(harness.ViewModel.SheetIds);
        Assert.Equal(0, harness.ViewModel.SelectedSheet);
        Assert.True(harness.Assets.Current.IsDisposed);
        Assert.Empty(harness.Dialogs.Errors);
    }

    private static string WriteAssetDirectory(string root)
    {
        string directory = Path.Combine(root, "assets-close");
        Directory.CreateDirectory(Path.Combine(directory, "sheets"));
        File.WriteAllText(Path.Combine(directory, "manifest.json"),
            """{ "tileSize": 32, "sheets": { "1": { "10": [0, 0, 32, 32] } } }""");
        return directory;
    }

    [AvaloniaFact]
    public void Close_AfterApprovedClose_SecondClosingIsNotCancelledAgain()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        harness.Dialogs.DirtyResult = DirtyChoice.Discard;

        var counter = new ClosingCounter(harness.Window);
        harness.Window.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.False(harness.Window.IsVisible);
        Assert.Equal(2, counter.Count);
    }
}
