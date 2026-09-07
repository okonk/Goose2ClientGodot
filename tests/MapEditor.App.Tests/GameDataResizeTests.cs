using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MapEditor.App.Dialogs;
using MapEditor.App.Documents;
using MapEditor.App.Tests.Fakes;
using MapEditor.App.ViewModels;
using MapEditor.Core;
using MapEditor.GameData.Connectivity;
using MapEditor.GameData.Replacement;
using MapEditor.GameData.Rows;
using MapEditor.GameData.Sync;
using Xunit;

namespace MapEditor.App.Tests;

public class GameDataResizeTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("map-editor-resize-").FullName;

    private static readonly MapReference Map10 = new(10, "Dungeon", "dungeon.bytes");
    private static readonly MapReference Map20 = new(20, "Cave", "cave.bytes");
    private static readonly IReadOnlyList<MapReference> Maps = new[] { Map10, Map20 };
    private static readonly NpcAppearance Npc1 = new(1, "Goose", 0, 0, new RgbaValue(255, 255, 255, 255), 0, 0, new RgbaValue(255, 255, 255, 255), string.Empty);

    public void Dispose() => Directory.Delete(_directory, true);

    private static GameDataSyncSession Session(IReadOnlyList<NpcSpawnRow> spawns, IReadOnlyList<WarpRow> warps)
    {
        var data = new RemoteGameData(
            Maps,
            new Dictionary<int, NpcAppearance> { [1] = Npc1 },
            spawns.Select((row, index) => new RemoteRow<NpcSpawnRow>(index + 2, row)).ToList(),
            warps.Select((row, index) => new RemoteRow<WarpRow>(index + 2, row)).ToList());
        return new GameDataSyncSession("sheet", 10, data);
    }

    private MapDocumentViewModel CreateViewModel(int width, int height, GameDataSyncSession? session)
    {
        var dialogs = new FakeEditorDialogs();
        var workspace = new WorkspaceViewModel(dialogs, new MapFileStore());
        var commands = workspace.Commands;
        var controller = new EditorDocumentController(dialogs, new MapFileStore(),
            new EditorDocument(new MapEditSession(MapDocument.Create(width, height), initiallyDirty: false), null, null));
        var viewModel = new MapDocumentViewModel(controller, new SharedTileClipboard());
        viewModel.AttachGameData(new DocumentGameDataState(viewModel, commands));
        if (session is not null)
        {
            viewModel.GameData!.AttachSession(session);
        }

        return viewModel;
    }

    private static void FillDocument(MapDocumentViewModel viewModel, int width, int height)
    {
        MapDocument document = viewModel.Session.Document;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                document.SetLayer(x, y, 0, new MapTileLayer(1, 1));
            }
        }
    }

    [Fact]
    public void Plan_CropEastSouth_ShiftsNothingAndDropsOutOfBoundsRows()
    {
        var viewModel = CreateViewModel(10, 8, Session(
            new[]
            {
                new NpcSpawnRow(1, 10, 1, 1),
                new NpcSpawnRow(1, 10, 7, 5),
                new NpcSpawnRow(2, 10, 8, 0),
                new NpcSpawnRow(2, 10, 3, 6),
                new NpcSpawnRow(2, 10, 9, 7)
            },
            new[]
            {
                new WarpRow(10, 2, 2, 10, 5, 5),
                new WarpRow(10, 2, 3, 10, 9, 5),
                new WarpRow(10, 4, 4, 20, 9, 7),
                new WarpRow(10, 8, 4, 10, 1, 1)
            }));
        FillDocument(viewModel, 10, 8);

        MapResizePlan plan = viewModel.PlanResize(new MapTileRectangle(0, 0, 8, 6));

        Assert.Equal(32, plan.CroppedTiles);
        Assert.Equal(3, plan.CroppedSpawns);
        Assert.Equal(1, plan.CroppedWarps);
        Assert.Equal(1, plan.InboundWarps);
        Assert.True(plan.HasPulledData);
        Assert.Equal(
            new[] { new NpcSpawnRow(1, 10, 1, 1), new NpcSpawnRow(1, 10, 7, 5) },
            plan.NewSpawns);
        Assert.Equal(
            new[]
            {
                new WarpRow(10, 2, 2, 10, 5, 5),
                new WarpRow(10, 2, 3, 10, 9, 5),
                new WarpRow(10, 4, 4, 20, 9, 7)
            },
            plan.NewWarps);
    }

    [Fact]
    public void Plan_GrowWestNorth_ShiftsEveryRowByTheForwardOffset()
    {
        var viewModel = CreateViewModel(10, 8, Session(
            new[] { new NpcSpawnRow(1, 10, 0, 0), new NpcSpawnRow(1, 10, 9, 7) },
            new[] { new WarpRow(10, 0, 0, 10, 9, 7) }));

        MapResizePlan plan = viewModel.PlanResize(new MapTileRectangle(-2, -1, 14, 10));

        Assert.Equal(0, plan.CroppedTiles);
        Assert.Equal(0, plan.CroppedSpawns);
        Assert.Equal(0, plan.CroppedWarps);
        Assert.Equal(0, plan.InboundWarps);
        Assert.Equal(
            new[] { new NpcSpawnRow(1, 10, 2, 1), new NpcSpawnRow(1, 10, 11, 8) },
            plan.NewSpawns);
        Assert.Equal(new[] { new WarpRow(10, 2, 1, 10, 11, 8) }, plan.NewWarps);
    }

    [Fact]
    public void Plan_WhollyDisplacedWindow_CropsEverythingAndKeepsNothing()
    {
        var viewModel = CreateViewModel(10, 8, Session(
            new[] { new NpcSpawnRow(1, 10, 3, 4) },
            new[] { new WarpRow(10, 5, 6, 10, 7, 8) }));
        FillDocument(viewModel, 10, 8);

        MapResizePlan plan = viewModel.PlanResize(new MapTileRectangle(10, 0, 5, 8));

        Assert.Equal(80, plan.CroppedTiles);
        Assert.Equal(1, plan.CroppedSpawns);
        Assert.Equal(1, plan.CroppedWarps);
        Assert.Equal(0, plan.InboundWarps);
        Assert.Empty(plan.NewSpawns);
        Assert.Empty(plan.NewWarps);
    }

    [Fact]
    public void Plan_BoundaryCoordinates_KeepRowsOnTheFarEdge()
    {
        var viewModel = CreateViewModel(10, 8, Session(
            new[] { new NpcSpawnRow(1, 10, 9, 7), new NpcSpawnRow(1, 10, 0, 0) },
            new[] { new WarpRow(10, 9, 7, 10, 0, 0) }));

        MapResizePlan plan = viewModel.PlanResize(new MapTileRectangle(0, 0, 10, 8));

        Assert.Equal(0, plan.CroppedSpawns);
        Assert.Equal(0, plan.CroppedWarps);
        Assert.Equal(
            new[] { new NpcSpawnRow(1, 10, 9, 7), new NpcSpawnRow(1, 10, 0, 0) },
            plan.NewSpawns);
        Assert.Equal(new[] { new WarpRow(10, 9, 7, 10, 0, 0) }, plan.NewWarps);
    }

    [Fact]
    public void Plan_DuplicateRows_PreservesOrderAndKeepsAllCopies()
    {
        var viewModel = CreateViewModel(10, 8, Session(
            new[]
            {
                new NpcSpawnRow(1, 10, 1, 1),
                new NpcSpawnRow(2, 10, 1, 1),
                new NpcSpawnRow(1, 10, 1, 1),
                new NpcSpawnRow(2, 10, 9, 9)
            },
            new[]
            {
                new WarpRow(10, 2, 2, 10, 3, 3),
                new WarpRow(10, 2, 2, 20, 3, 3)
            }));

        MapResizePlan plan = viewModel.PlanResize(new MapTileRectangle(0, 0, 5, 5));

        Assert.Equal(1, plan.CroppedSpawns);
        Assert.Equal(0, plan.CroppedWarps);
        Assert.Equal(
            new[]
            {
                new NpcSpawnRow(1, 10, 1, 1),
                new NpcSpawnRow(2, 10, 1, 1),
                new NpcSpawnRow(1, 10, 1, 1)
            },
            plan.NewSpawns);
        Assert.Equal(
            new[]
            {
                new WarpRow(10, 2, 2, 10, 3, 3),
                new WarpRow(10, 2, 2, 20, 3, 3)
            },
            plan.NewWarps);
    }

    [Fact]
    public void Plan_NonSelfDestination_NeverShiftsAndIsNotInbound()
    {
        var viewModel = CreateViewModel(10, 8, Session(
            Array.Empty<NpcSpawnRow>(),
            new[]
            {
                new WarpRow(10, 2, 2, 20, 9, 7),
                new WarpRow(10, 3, 3, 30, 9, 7)
            }));

        MapResizePlan plan = viewModel.PlanResize(new MapTileRectangle(0, 0, 8, 6));

        Assert.Equal(0, plan.InboundWarps);
        Assert.Equal(
            new[]
            {
                new WarpRow(10, 2, 2, 20, 9, 7),
                new WarpRow(10, 3, 3, 30, 9, 7)
            },
            plan.NewWarps);
    }

    [Fact]
    public void Plan_UnpulledDocument_ReportsNoSheetImpact()
    {
        var viewModel = CreateViewModel(10, 8, session: null);

        MapResizePlan plan = viewModel.PlanResize(new MapTileRectangle(0, 0, 8, 6));

        Assert.False(plan.HasPulledData);
        Assert.Equal(0, plan.CroppedSpawns);
        Assert.Equal(0, plan.CroppedWarps);
        Assert.Equal(0, plan.InboundWarps);
        Assert.Empty(plan.NewSpawns);
        Assert.Empty(plan.NewWarps);
    }

    [Fact]
    public void Resize_UnpulledTab_PerformsMapOnlyResizeWithNoSheetEdit()
    {
        var viewModel = CreateViewModel(10, 8, session: null);
        FillDocument(viewModel, 10, 8);

        viewModel.ResizeMap(new MapTileRectangle(0, 0, 8, 6));

        Assert.Equal(8, viewModel.Session.Document.Width);
        Assert.Equal(6, viewModel.Session.Document.Height);
        Assert.False(viewModel.GameData!.HasSession);
        Assert.False(viewModel.SheetSession.IsDirty);
        Assert.True(viewModel.Session.IsDirty);
    }

    [Fact]
    public void Resize_IdentityWindow_IsANoOp()
    {
        var viewModel = CreateViewModel(10, 8, Session(
            new[] { new NpcSpawnRow(1, 10, 3, 4) },
            new[] { new WarpRow(10, 5, 6, 10, 7, 8) }));

        viewModel.ResizeMap(new MapTileRectangle(0, 0, 10, 8));

        Assert.Equal(10, viewModel.Session.Document.Width);
        Assert.Equal(8, viewModel.Session.Document.Height);
        Assert.False(viewModel.Session.IsDirty);
        Assert.False(viewModel.SheetSession.IsDirty);
        Assert.Equal(new[] { new NpcSpawnRow(1, 10, 3, 4) }, viewModel.SheetSession.Spawns);
        Assert.Equal(new[] { new WarpRow(10, 5, 6, 10, 7, 8) }, viewModel.SheetSession.Warps);
    }

    [AvaloniaFact]
    public void ResizeDialog_PulledState_ShowsTileSpawnWarpAndInboundCounts()
    {
        var viewModel = CreateViewModel(10, 8, Session(
            new[] { new NpcSpawnRow(1, 10, 3, 3), new NpcSpawnRow(1, 10, 9, 9) },
            new[] { new WarpRow(10, 3, 3, 10, 9, 5), new WarpRow(10, 4, 4, 20, 9, 7) }));
        FillDocument(viewModel, 10, 8);

        var dialog = new ResizeMapDialog(viewModel.Session.Document, viewModel.PlanResize);
        dialog.WestBox.Text = "-2";
        dialog.WestBox.RaiseEvent(new TextChangedEventArgs(TextBox.TextChangedEvent));
        dialog.EastBox.Text = "-3";
        dialog.EastBox.RaiseEvent(new TextChangedEventArgs(TextBox.TextChangedEvent));
        dialog.NorthBox.Text = "-2";
        dialog.NorthBox.RaiseEvent(new TextChangedEventArgs(TextBox.TextChangedEvent));
        dialog.SouthBox.Text = "-2";
        dialog.SouthBox.RaiseEvent(new TextChangedEventArgs(TextBox.TextChangedEvent));

        Assert.Equal(new MapTileRectangle(2, 2, 5, 4), dialog.TryBuildWindow());
        Assert.True(dialog.DiscardText.IsVisible);
        Assert.Contains("60", dialog.DiscardText.Text);
        Assert.True(dialog.SheetText.IsVisible);
        Assert.Contains("1 spawn", dialog.SheetText.Text);
        Assert.True(dialog.InboundText.IsVisible);
        Assert.Contains("1 inbound", dialog.InboundText.Text);
    }

    [AvaloniaFact]
    public async Task ResizeMenu_Confirm_UndoRedo_RestoresAndReappliesEverything()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        MapDocumentViewModel doc = harness.ViewModel;
        doc.GameData!.AttachSession(MenuSession());
        doc.Session.Document.SetLayer(0, 0, 0, new MapTileLayer(3, 3));
        doc.Session.Document.SetLayer(95, 95, 0, new MapTileLayer(4, 4));
        doc.SelectedX = 5;
        doc.SelectedY = 5;

        Assert.False(doc.Session.IsDirty);
        Assert.False(doc.SheetSession.IsDirty);

        harness.Dialogs.ResizeMapResult = new MapTileRectangle(-2, -1, 92, 91);
        Item(harness, "ResizeCommand").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(92, doc.Session.Document.Width);
        Assert.Equal(91, doc.Session.Document.Height);
        Assert.True(doc.Session.IsDirty);
        Assert.True(doc.SheetSession.IsDirty);
        Assert.Equal(new MapTileLayer(3, 3), doc.Session.Document[2, 1].GetLayer(0));
        Assert.Equal(new MapTileLayer(0, 0), doc.Session.Document[0, 0].GetLayer(0));
        Assert.Equal(
            new[] { new NpcSpawnRow(1, 10, 7, 6), new NpcSpawnRow(2, 10, 7, 6) },
            doc.SheetSession.Spawns);
        Assert.Equal(
            new[]
            {
                new WarpRow(10, 7, 7, 10, 97, 96),
                new WarpRow(10, 7, 7, 10, 97, 96),
                new WarpRow(10, 9, 8, 20, 99, 99)
            },
            doc.SheetSession.Warps);
        Assert.Equal(7, doc.SelectedX);
        Assert.Equal(6, doc.SelectedY);

        Assert.True(doc.Undo());

        Assert.Equal(100, doc.Session.Document.Width);
        Assert.Equal(100, doc.Session.Document.Height);
        Assert.False(doc.Session.IsDirty);
        Assert.False(doc.SheetSession.IsDirty);
        Assert.Equal(new MapTileLayer(3, 3), doc.Session.Document[0, 0].GetLayer(0));
        Assert.Equal(new MapTileLayer(4, 4), doc.Session.Document[95, 95].GetLayer(0));
        Assert.Equal(
            new[] { new NpcSpawnRow(1, 10, 5, 5), new NpcSpawnRow(2, 10, 5, 5), new NpcSpawnRow(1, 10, 95, 95) },
            doc.SheetSession.Spawns);
        Assert.Equal(
            new[]
            {
                new WarpRow(10, 5, 6, 10, 95, 95),
                new WarpRow(10, 5, 6, 10, 95, 95),
                new WarpRow(10, 7, 7, 20, 99, 99),
                new WarpRow(10, 95, 5, 10, 1, 1)
            },
            doc.SheetSession.Warps);
        Assert.Equal(5, doc.SelectedX);
        Assert.Equal(5, doc.SelectedY);

        Assert.True(doc.Redo());

        Assert.Equal(92, doc.Session.Document.Width);
        Assert.Equal(91, doc.Session.Document.Height);
        Assert.True(doc.Session.IsDirty);
        Assert.True(doc.SheetSession.IsDirty);
        Assert.Equal(new MapTileLayer(3, 3), doc.Session.Document[2, 1].GetLayer(0));
        Assert.Equal(
            new[] { new NpcSpawnRow(1, 10, 7, 6), new NpcSpawnRow(2, 10, 7, 6) },
            doc.SheetSession.Spawns);
        Assert.Equal(
            new[]
            {
                new WarpRow(10, 7, 7, 10, 97, 96),
                new WarpRow(10, 7, 7, 10, 97, 96),
                new WarpRow(10, 9, 8, 20, 99, 99)
            },
            doc.SheetSession.Warps);
        Assert.Equal(7, doc.SelectedX);
        Assert.Equal(6, doc.SelectedY);
    }

    [AvaloniaFact]
    public async Task ResizeMenu_Cancel_ChangesNothing()
    {
        using MainWindowHarness harness = MainWindowHarness.Create();
        MapDocumentViewModel doc = harness.ViewModel;
        doc.GameData!.AttachSession(MenuSession());
        doc.Session.Document.SetLayer(0, 0, 0, new MapTileLayer(3, 3));

        harness.Dialogs.ResizeMapResult = null;
        Item(harness, "ResizeCommand").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(100, doc.Session.Document.Width);
        Assert.Equal(100, doc.Session.Document.Height);
        Assert.False(doc.Session.IsDirty);
        Assert.False(doc.SheetSession.IsDirty);
        Assert.Equal(
            new[] { new NpcSpawnRow(1, 10, 5, 5), new NpcSpawnRow(2, 10, 5, 5), new NpcSpawnRow(1, 10, 95, 95) },
            doc.SheetSession.Spawns);
        Assert.Equal(1, harness.Dialogs.ResizeMapShown);
        Assert.NotNull(harness.Dialogs.LastResizePlan);
    }

    private static MenuItem Item(MainWindowHarness harness, string name)
        => harness.Window.FindControl<MenuItem>(name)
           ?? throw new InvalidOperationException($"missing menu item {name}");

    private static GameDataSyncSession MenuSession()
    {
        var npcs = new Dictionary<int, NpcAppearance>
        {
            [1] = Npc1,
            [2] = new NpcAppearance(2, "Duck", 0, 0, new RgbaValue(255, 255, 255, 255), 0, 0, new RgbaValue(255, 255, 255, 255), string.Empty)
        };
        var spawns = new List<RemoteRow<NpcSpawnRow>>
        {
            new(2, new NpcSpawnRow(1, 10, 5, 5)),
            new(3, new NpcSpawnRow(2, 10, 5, 5)),
            new(4, new NpcSpawnRow(1, 10, 95, 95))
        };
        var warps = new List<RemoteRow<WarpRow>>
        {
            new(2, new WarpRow(10, 5, 6, 10, 95, 95)),
            new(3, new WarpRow(10, 5, 6, 10, 95, 95)),
            new(4, new WarpRow(10, 7, 7, 20, 99, 99)),
            new(5, new WarpRow(10, 95, 5, 10, 1, 1))
        };
        return new GameDataSyncSession("sheet", 10, new RemoteGameData(Maps, npcs, spawns, warps));
    }
}
