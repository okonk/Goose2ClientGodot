using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MapEditor.App.Connectivity;
using MapEditor.App.Dialogs;
using MapEditor.App.Tests.Fakes;
using MapEditor.App.ViewModels;
using MapEditor.Core;
using MapEditor.GameData.Connectivity;
using MapEditor.GameData.Replacement;
using MapEditor.GameData.Rows;
using MapEditor.GameData.Sync;
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
        Assert.False(harness.ViewModel.Session.IsDirty);

        var counter = new ClosingCounter(harness.Window);
        harness.Window.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.False(harness.Window.IsVisible);
        Assert.Equal(0, harness.Dialogs.DirtyShown);
        Assert.Equal(2, counter.Count);
    }

    [AvaloniaFact]
    public void Close_DirtyDocument_Discard_PromptsOnceThenCloses()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        harness.ViewModel.Brush = new MapTileLayer(1, 2);
        harness.ViewModel.Session.BeginStroke(MapEditTool.Pencil, 0, 0);
        Assert.True(harness.ViewModel.Session.CompleteStroke());
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
        harness.ViewModel.Brush = new MapTileLayer(1, 2);
        harness.ViewModel.Session.BeginStroke(MapEditTool.Pencil, 0, 0);
        Assert.True(harness.ViewModel.Session.CompleteStroke());
        string mapPath = Path.Combine(harness.TempDirectory, "closed-saved.map");
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
        harness.ViewModel.Brush = new MapTileLayer(1, 2);
        harness.ViewModel.Session.BeginStroke(MapEditTool.Pencil, 0, 0);
        Assert.True(harness.ViewModel.Session.CompleteStroke());
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
        harness.ViewModel.Brush = new MapTileLayer(1, 2);
        harness.ViewModel.Session.BeginStroke(MapEditTool.Pencil, 0, 0);
        Assert.True(harness.ViewModel.Session.CompleteStroke());
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
        harness.ViewModel.Brush = new MapTileLayer(1, 2);
        harness.ViewModel.Session.BeginStroke(MapEditTool.Pencil, 0, 0);
        Assert.True(harness.ViewModel.Session.CompleteStroke());
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
        harness.ViewModel.Brush = new MapTileLayer(1, 2);
        harness.ViewModel.Session.BeginStroke(MapEditTool.Pencil, 0, 0);
        Assert.True(harness.ViewModel.Session.CompleteStroke());
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
        harness.ViewModel.Brush = new MapTileLayer(1, 2);
        harness.ViewModel.Session.BeginStroke(MapEditTool.Pencil, 0, 0);
        Assert.True(harness.ViewModel.Session.CompleteStroke());
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
        string mapPath = Path.Combine(harness.TempDirectory, "drag-close.map");
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

        Assert.True(harness.ViewModel.Session.CanUndo);
        Assert.True(harness.ViewModel.Undo());
        Assert.False(harness.ViewModel.Session.CanUndo);
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
        harness.ViewModel.Brush = new MapTileLayer(1, 2);
        harness.ViewModel.Session.BeginStroke(MapEditTool.Pencil, 0, 0);
        Assert.True(harness.ViewModel.Session.CompleteStroke());
        harness.Dialogs.DirtyResult = DirtyChoice.Cancel;

        harness.Window.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.True(harness.Window.IsVisible);
        Assert.False(harness.Assets.Current.IsDisposed);
        Assert.Empty(harness.Assets.Current.GetFrames(1));
    }

    [AvaloniaFact]
    public void Close_WhileAssetPickerPending_IsRefusedUntilThePickerCompletes()
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

        Assert.True(harness.Window.IsVisible);

        gate.SetResult(assets);
        Dispatcher.UIThread.RunJobs();

        Assert.True(harness.Assets.Current.IsAvailable);
        Assert.Empty(harness.Dialogs.Errors);

        harness.Window.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.False(harness.Window.IsVisible);
        Assert.True(harness.Assets.Current.IsDisposed);
        Assert.Empty(harness.Dialogs.Errors);
    }

    private static void MakeDirty(MapDocumentViewModel document)
    {
        document.Brush = new MapTileLayer(1, 2);
        document.Session.BeginStroke(MapEditTool.Pencil, 0, 0);
        Assert.True(document.Session.CompleteStroke());
    }

    [AvaloniaFact]
    public async Task Close_TwoDirtyTabs_PromptsTwice()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        harness.Dialogs.NewMapResult = new NewMapRequest(100, 100);
        await harness.Workspace.NewAsync();
        Dispatcher.UIThread.RunJobs();
        MapDocumentViewModel second = harness.Workspace.ActiveDocument;
        MakeDirty(harness.ViewModel);
        MakeDirty(second);
        harness.Dialogs.DirtyResult = DirtyChoice.Discard;

        var counter = new ClosingCounter(harness.Window);
        harness.Window.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.False(harness.Window.IsVisible);
        Assert.Equal(2, harness.Dialogs.DirtyShown);
        Assert.Equal(2, counter.Count);
    }

    [AvaloniaFact]
    public async Task Close_MixedDirtyAndCleanTabs_PromptsOnlyDirtyInOrder()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        harness.Dialogs.NewMapResult = new NewMapRequest(100, 100);
        await harness.Workspace.NewAsync();
        Dispatcher.UIThread.RunJobs();
        await harness.Workspace.NewAsync();
        Dispatcher.UIThread.RunJobs();
        MapDocumentViewModel third = harness.Workspace.ActiveDocument;
        MapDocumentViewModel first = harness.ViewModel;
        MakeDirty(first);
        MakeDirty(third);
        var firstPrompt = new TaskCompletionSource<DirtyChoice>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondPrompt = new TaskCompletionSource<DirtyChoice>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Dialogs.DirtyGates = new Queue<TaskCompletionSource<DirtyChoice>>(new[] { firstPrompt, secondPrompt });

        var counter = new ClosingCounter(harness.Window);
        harness.Window.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, harness.Dialogs.DirtyShown);
        Assert.Same(first, harness.Window.DataContext);

        firstPrompt.SetResult(DirtyChoice.Discard);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, harness.Dialogs.DirtyShown);
        Assert.Same(third, harness.Window.DataContext);

        secondPrompt.SetResult(DirtyChoice.Discard);
        Dispatcher.UIThread.RunJobs();

        Assert.False(harness.Window.IsVisible);
        Assert.Equal(2, harness.Dialogs.DirtyShown);
        Assert.Equal(2, counter.Count);
    }

    [AvaloniaFact]
    public async Task Close_CancelOnSecondTab_AbortsQuitAndLeavesItActive()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        harness.Dialogs.NewMapResult = new NewMapRequest(100, 100);
        await harness.Workspace.NewAsync();
        Dispatcher.UIThread.RunJobs();
        MapDocumentViewModel first = harness.ViewModel;
        MapDocumentViewModel second = harness.Workspace.ActiveDocument;
        MakeDirty(first);
        MakeDirty(second);
        var firstPrompt = new TaskCompletionSource<DirtyChoice>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondPrompt = new TaskCompletionSource<DirtyChoice>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Dialogs.DirtyGates = new Queue<TaskCompletionSource<DirtyChoice>>(new[] { firstPrompt, secondPrompt });

        var counter = new ClosingCounter(harness.Window);
        harness.Window.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, harness.Dialogs.DirtyShown);
        Assert.Same(first, harness.Workspace.ActiveDocument);
        Assert.Same(first, harness.Window.DataContext);

        firstPrompt.SetResult(DirtyChoice.Discard);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, harness.Dialogs.DirtyShown);
        Assert.Same(second, harness.Workspace.ActiveDocument);

        secondPrompt.SetResult(DirtyChoice.Cancel);
        Dispatcher.UIThread.RunJobs();

        Assert.True(harness.Window.IsVisible);
        Assert.Single(harness.Workspace.Documents);
        Assert.Same(second, harness.Workspace.Documents[0]);
        Assert.Same(second, harness.Workspace.ActiveDocument);
        Assert.Same(second, harness.Window.DataContext);
        Assert.True(second.Session.IsDirty);
        Assert.Equal(1, counter.Count);
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

    private static readonly MapReference SheetMap10 = new(10, "Dungeon", "dungeon.map");
    private static readonly MapReference SheetMap20 = new(20, "Cave", "cave.map");
    private static readonly IReadOnlyList<MapReference> SheetMaps = new[] { SheetMap10, SheetMap20 };
    private static readonly string SheetUrl = "https://docs.google.com/spreadsheets/d/abc123";
    private static readonly NpcAppearance SheetNpc1 = new(1, "Goose", 0, 0, new RgbaValue(255, 255, 255, 255), 0, 0, new RgbaValue(255, 255, 255, 255), string.Empty);

    private static RemoteGameData SheetData()
        => new(
            SheetMaps,
            new Dictionary<int, NpcAppearance> { [1] = SheetNpc1 },
            new List<RemoteRow<NpcSpawnRow>> { new(2, new NpcSpawnRow(1, 10, 3, 4)) },
            new List<RemoteRow<WarpRow>>());

    [AvaloniaFact]
    public async Task Close_DirtySheetOnly_PushSucceeds_ClosesWithoutMapPrompt()
    {
        var rig = new SheetCloseRig();
        using MainWindowHarness harness = MainWindowHarness.Create(rig.Connectivity);
        rig.Dialogs = harness.Dialogs;
        MapDocumentViewModel doc = harness.ViewModel;
        await rig.PullAsync(harness.Workspace, doc);
        doc.GameData!.Session.Edits.AddSpawn(new NpcSpawnRow(1, 10, 5, 5));
        harness.Dialogs.SheetDirtyResult = SheetDirtyChoice.Push;

        harness.Window.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.False(harness.Window.IsVisible);
        Assert.Equal(1, harness.Dialogs.SheetDirtyShown);
        Assert.Equal(0, harness.Dialogs.DirtyShown);
        Assert.Equal(new[] { "ReadOwnedRowsAsync", "ReplaceOwnedRowsAsync" }, rig.Gateway.Calls.Select(call => call.Method));
    }

    [AvaloniaFact]
    public async Task Close_DirtySheetOnly_Discard_ClosesWithoutPushing()
    {
        var rig = new SheetCloseRig();
        using MainWindowHarness harness = MainWindowHarness.Create(rig.Connectivity);
        rig.Dialogs = harness.Dialogs;
        MapDocumentViewModel doc = harness.ViewModel;
        await rig.PullAsync(harness.Workspace, doc);
        doc.GameData!.Session.Edits.AddSpawn(new NpcSpawnRow(1, 10, 5, 5));
        harness.Dialogs.SheetDirtyResult = SheetDirtyChoice.Discard;

        harness.Window.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.False(harness.Window.IsVisible);
        Assert.Equal(1, harness.Dialogs.SheetDirtyShown);
        Assert.Equal(0, harness.Dialogs.DirtyShown);
        Assert.Empty(rig.Gateway.Calls);
        Assert.Null(doc.Document.Path);
    }

    [AvaloniaFact]
    public async Task Close_DirtySheetOnly_Cancel_StaysOpenAndDirty()
    {
        var rig = new SheetCloseRig();
        using MainWindowHarness harness = MainWindowHarness.Create(rig.Connectivity);
        rig.Dialogs = harness.Dialogs;
        MapDocumentViewModel doc = harness.ViewModel;
        await rig.PullAsync(harness.Workspace, doc);
        doc.GameData!.Session.Edits.AddSpawn(new NpcSpawnRow(1, 10, 5, 5));
        harness.Dialogs.SheetDirtyResult = SheetDirtyChoice.Cancel;

        harness.Window.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.True(harness.Window.IsVisible);
        Assert.Equal(1, harness.Dialogs.SheetDirtyShown);
        Assert.Equal(0, harness.Dialogs.DirtyShown);
        Assert.True(doc.GameData.IsDirty);
        Assert.Empty(rig.Gateway.Calls);
        harness.Dialogs.SheetDirtyResult = SheetDirtyChoice.Discard;
    }

    [AvaloniaFact]
    public async Task Close_DirtySheetAndMap_PromptsSheetBeforeMap()
    {
        var rig = new SheetCloseRig();
        using MainWindowHarness harness = MainWindowHarness.Create(rig.Connectivity);
        rig.Dialogs = harness.Dialogs;
        MapDocumentViewModel doc = harness.ViewModel;
        await rig.PullAsync(harness.Workspace, doc);
        doc.GameData!.Session.Edits.AddSpawn(new NpcSpawnRow(1, 10, 5, 5));
        MakeDirty(doc);
        var sheetGate = new TaskCompletionSource<SheetDirtyChoice>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Dialogs.SheetDirtyGate = sheetGate;

        harness.Window.Close();
        await Until(() => harness.Dialogs.SheetDirtyShown == 1);
        Assert.Equal(0, harness.Dialogs.DirtyShown);

        sheetGate.SetResult(SheetDirtyChoice.Discard);
        harness.Dialogs.DirtyResult = DirtyChoice.Discard;
        Dispatcher.UIThread.RunJobs();

        Assert.False(harness.Window.IsVisible);
        Assert.Equal(1, harness.Dialogs.DirtyShown);
    }

    [AvaloniaFact]
    public async Task Close_DirtySheetAndMap_PushSucceedsThenSaveCanceled_KeepsOpenSheetCleanMapDirty()
    {
        var rig = new SheetCloseRig();
        using MainWindowHarness harness = MainWindowHarness.Create(rig.Connectivity);
        rig.Dialogs = harness.Dialogs;
        MapDocumentViewModel doc = harness.ViewModel;
        await rig.PullAsync(harness.Workspace, doc);
        harness.Dialogs.SavePickResult = Path.Combine(harness.TempDirectory, "sheet-close-saved.map");
        await doc.SaveAsAsync();
        doc.GameData!.Session.Edits.AddSpawn(new NpcSpawnRow(1, 10, 5, 5));
        MakeDirty(doc);
        string path = doc.Document.Path!;
        MapFileRevision revision = doc.Document.Revision!.Value;
        harness.Dialogs.SheetDirtyResult = SheetDirtyChoice.Push;
        harness.Dialogs.DirtyResult = DirtyChoice.Cancel;

        harness.Window.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.True(harness.Window.IsVisible);
        Assert.False(doc.GameData.IsDirty);
        Assert.True(doc.Session.IsDirty);
        Assert.Equal(path, doc.Document.Path);
        Assert.Equal(revision, doc.Document.Revision);
        Assert.Equal(1, harness.Dialogs.DirtyShown);
        harness.Dialogs.DirtyResult = DirtyChoice.Discard;
    }

    [AvaloniaFact]
    public async Task Close_DirtySheetAndMap_PushFails_KeepsOpenAndDirtyWithoutMapPrompt()
    {
        var rig = new SheetCloseRig();
        using MainWindowHarness harness = MainWindowHarness.Create(rig.Connectivity);
        rig.Dialogs = harness.Dialogs;
        MapDocumentViewModel doc = harness.ViewModel;
        await rig.PullAsync(harness.Workspace, doc);
        doc.GameData!.Session.Edits.AddSpawn(new NpcSpawnRow(999, 10, 5, 5));
        MakeDirty(doc);
        harness.Dialogs.SheetDirtyResult = SheetDirtyChoice.Push;
        harness.Dialogs.DirtyResult = DirtyChoice.Discard;

        harness.Window.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.True(harness.Window.IsVisible);
        Assert.True(doc.GameData.IsDirty);
        Assert.True(doc.Session.IsDirty);
        Assert.Equal(0, harness.Dialogs.DirtyShown);
        Assert.Single(harness.Dialogs.Errors);
        harness.Dialogs.SheetDirtyResult = SheetDirtyChoice.Discard;
        harness.Dialogs.DirtyResult = DirtyChoice.Discard;
    }

    [AvaloniaFact]
    public async Task Close_DuplicateClosingWhileSheetPromptPending_SinglePromptAndNoRecursion()
    {
        var rig = new SheetCloseRig();
        using MainWindowHarness harness = MainWindowHarness.Create(rig.Connectivity);
        rig.Dialogs = harness.Dialogs;
        MapDocumentViewModel doc = harness.ViewModel;
        await rig.PullAsync(harness.Workspace, doc);
        doc.GameData!.Session.Edits.AddSpawn(new NpcSpawnRow(1, 10, 5, 5));
        var gate = new TaskCompletionSource<SheetDirtyChoice>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Dialogs.SheetDirtyGate = gate;

        var counter = new ClosingCounter(harness.Window);
        harness.Window.Close();
        harness.Window.Close();
        harness.Window.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.True(harness.Window.IsVisible);
        Assert.Equal(1, harness.Dialogs.SheetDirtyShown);
        Assert.Equal(3, counter.Count);

        gate.SetResult(SheetDirtyChoice.Discard);
        Dispatcher.UIThread.RunJobs();

        Assert.False(harness.Window.IsVisible);
        Assert.Equal(4, counter.Count);
    }

    [AvaloniaFact]
    public async Task Close_WhileTabCloseSheetPromptPending_IsRefusedUntilThePromptCompletes()
    {
        var rig = new SheetCloseRig();
        using MainWindowHarness harness = MainWindowHarness.Create(rig.Connectivity);
        rig.Dialogs = harness.Dialogs;
        MapDocumentViewModel doc = harness.ViewModel;
        await rig.PullAsync(harness.Workspace, doc);
        doc.GameData!.Session.Edits.AddSpawn(new NpcSpawnRow(1, 10, 5, 5));
        DocumentGameDataState sheetState = doc.GameData!;
        var gate = new TaskCompletionSource<SheetDirtyChoice>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Dialogs.SheetDirtyGate = gate;

        ListBox tabStrip = harness.Window.FindControl<ListBox>("TabStrip")
                         ?? throw new InvalidOperationException("missing TabStrip");
        ListBoxItem tab = tabStrip.ContainerFromItem(doc) as ListBoxItem
                         ?? throw new InvalidOperationException("missing tab container");
        Button close = tab.GetVisualDescendants().OfType<Button>().Single(button => button.Classes.Contains("tabClose"));
        close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, harness.Dialogs.SheetDirtyShown);

        harness.Window.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.True(harness.Window.IsVisible);
        Assert.Equal(1, harness.Dialogs.SheetDirtyShown);

        gate.SetResult(SheetDirtyChoice.Discard);
        Dispatcher.UIThread.RunJobs();

        Assert.True(harness.Window.IsVisible);
        Assert.DoesNotContain(doc, harness.Workspace.Documents);
        Assert.True(sheetState.IsDirty);
    }

    private static async Task Until(Func<bool> condition)
    {
        for (int i = 0; i < 1000 && !condition(); i++)
        {
            await Task.Delay(1);
        }
    }

    private sealed class SheetCloseRig
    {
        public FakeEditorDialogs Dialogs { get; set; }

        public RecordingGateway Gateway { get; } = new();

        public GameDataSyncCoordinator Coordinator { get; }

        public ScriptedConnectivity Connectivity { get; }

        public SheetCloseRig(FakeEditorDialogs? dialogs = null)
        {
            Dialogs = dialogs ?? new FakeEditorDialogs();
            Coordinator = new GameDataSyncCoordinator(Gateway, (_, _) => Task.CompletedTask);
            Connectivity = new ScriptedConnectivity { IsConnected = true, Coordinator = Coordinator };
        }

        public async Task PullAsync(WorkspaceViewModel workspace, MapDocumentViewModel document)
        {
            Dialogs.SpreadsheetUrlResult = SheetUrl;
            Dialogs.MapConfirmationResult = SheetMap10;
            Assert.True(await workspace.Commands.PullAsync(document));
        }
    }

    private sealed class RecordingGateway : IGameDataGateway
    {
        public sealed record Call(string Method);

        public List<Call> Calls { get; } = new();

        public Task<IReadOnlyList<MapReference>> ReadMapsAsync(string spreadsheetId, CancellationToken cancellationToken)
            => Task.FromResult(SheetMaps);

        public Task<RemoteGameData> ReadGameDataAsync(string spreadsheetId, int mapId, CancellationToken cancellationToken)
            => Task.FromResult(SheetData());

        public Task<RemoteOwnedRows> ReadOwnedRowsAsync(string spreadsheetId, int mapId, CancellationToken cancellationToken)
        {
            Calls.Add(new Call("ReadOwnedRowsAsync"));
            return Task.FromResult(new RemoteOwnedRows(
                new List<RemoteRow<NpcSpawnRow>> { new(2, new NpcSpawnRow(1, 10, 3, 4)) },
                new List<RemoteRow<WarpRow>>()));
        }

        public Task ReplaceOwnedRowsAsync(string spreadsheetId, ReplacementPlan spawnPlan, ReplacementPlan warpPlan, CancellationToken cancellationToken)
        {
            Calls.Add(new Call("ReplaceOwnedRowsAsync"));
            return Task.CompletedTask;
        }
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

        public Task ConnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DisconnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
