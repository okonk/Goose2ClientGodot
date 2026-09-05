using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using MapEditor.App.Dialogs;
using MapEditor.App.Documents;
using MapEditor.App.Tests.Fakes;
using MapEditor.App.ViewModels;
using MapEditor.Core;
using Xunit;

namespace MapEditor.App.Tests;

public class MainWindowViewModelTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("map-editor-vm-").FullName;
    private readonly FakeEditorDialogs _dialogs = new();
    private readonly EditorDocumentController _controller;
    private readonly MainWindowViewModel _viewModel;

    public MainWindowViewModelTests()
    {
        _controller = new EditorDocumentController(_dialogs, new MapFileStore());
        _viewModel = new MainWindowViewModel(_controller);
    }

    public void Dispose() => Directory.Delete(_directory, true);

    private string MapPath(string name) => Path.Combine(_directory, name);

    private List<string> RaisedProperties(Action action)
    {
        var raised = new List<string>();
        PropertyChangedEventHandler handler = (sender, e) => raised.Add(e.PropertyName ?? string.Empty);
        _viewModel.PropertyChanged += handler;
        try
        {
            action();
        }
        finally
        {
            _viewModel.PropertyChanged -= handler;
        }

        return raised;
    }

    private void SeedClipboard(int size = 3, MapTileLayer? tile = null)
    {
        tile ??= new MapTileLayer(7, 7);
        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                _viewModel.Session.Document.SetLayer(x, y, 0, tile.Value);
            }
        }

        _viewModel.SelectionRectangle = new MapTileRectangle(0, 0, size, size);
        _viewModel.CopySelection();
    }

    [Fact]
    public void InitialState_ReflectsNewCleanDocument()
    {
        Assert.Equal("Goose2 Map Editor — Untitled", _viewModel.Title);
        Assert.Same(_controller.Document.Session, _viewModel.Session);
        Assert.Equal(MapEditTool.Pencil, _viewModel.ActiveTool);
        Assert.Equal((byte)1, _viewModel.SelectedLayers);
        Assert.Equal(new MapTileLayer(0, 0), _viewModel.Brush);
        Assert.Empty(_viewModel.SheetIds);
        Assert.Equal(0, _viewModel.SelectedSheet);
        Assert.Equal((byte)0b11111, _viewModel.LayerVisibility);
        Assert.True(_viewModel.ShowGrid);
        Assert.False(_viewModel.ShowBlocked);
        Assert.Null(_viewModel.HoverX);
        Assert.Null(_viewModel.HoverY);
        Assert.Null(_viewModel.SelectedX);
        Assert.Null(_viewModel.SelectedY);
        Assert.Equal(100, _viewModel.ZoomPercent);
        Assert.Equal(100, _viewModel.MapWidth);
        Assert.Equal(100, _viewModel.MapHeight);
        Assert.False(_viewModel.CanUndo);
        Assert.False(_viewModel.CanRedo);
        Assert.False(_viewModel.CanSave);
    }

    [Fact]
    public void ActiveTool_ValidValue_RaisesOnlyActiveTool()
    {
        var raised = RaisedProperties(() => _viewModel.ActiveTool = MapEditTool.Eraser);

        Assert.Equal(MapEditTool.Eraser, _viewModel.ActiveTool);
        Assert.Equal(new[] { nameof(MainWindowViewModel.ActiveTool) }, raised);
    }

    [Fact]
    public void ActiveTool_InvalidValue_ThrowsWithoutRaising()
    {
        var raised = new List<string>();
        _viewModel.PropertyChanged += (sender, e) => raised.Add(e.PropertyName ?? string.Empty);

        Assert.Throws<ArgumentOutOfRangeException>(() => _viewModel.ActiveTool = (MapEditTool)99);

        Assert.Empty(raised);
        Assert.Equal(MapEditTool.Pencil, _viewModel.ActiveTool);
    }

    [Fact]
    public void ActiveTool_SwitchingAwayFromMultiSelect_ClearsSelectionRectangle()
    {
        _viewModel.ActiveTool = MapEditTool.MultiSelect;
        _viewModel.SelectionRectangle = new MapTileRectangle(1, 2, 3, 4);

        _viewModel.ActiveTool = MapEditTool.Pencil;

        Assert.Null(_viewModel.SelectionRectangle);
    }

    [Fact]
    public void ActiveTool_SwitchingToMultiSelect_KeepsSelectionRectangle()
    {
        _viewModel.ActiveTool = MapEditTool.MultiSelect;
        _viewModel.SelectionRectangle = new MapTileRectangle(1, 2, 3, 4);

        _viewModel.ActiveTool = MapEditTool.MultiSelect;

        Assert.Equal(new MapTileRectangle(1, 2, 3, 4), _viewModel.SelectionRectangle);
    }

    [Fact]
    public void SelectedLayers_ValidValue_WritesToSessionAndRaisesOnlySelectedLayers()
    {
        var raised = RaisedProperties(() => _viewModel.SelectedLayers = (byte)0b01000);

        Assert.Equal((byte)0b01000, _controller.Document.Session.SelectedLayers);
        Assert.Equal(new[] { nameof(MainWindowViewModel.SelectedLayers) }, raised);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(32)]
    public void SelectedLayers_OutOfRange_ThrowsWithoutMutatingSession(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _viewModel.SelectedLayers = (byte)value);
        Assert.Equal((byte)1, _controller.Document.Session.SelectedLayers);
    }

    [Fact]
    public void Brush_ValidValue_WritesToSessionAndRaisesOnlyBrush()
    {
        var raised = RaisedProperties(() => _viewModel.Brush = new MapTileLayer(7, -2));

        Assert.Equal(new MapTileLayer(7, -2), _controller.Document.Session.SelectedTileLayer);
        Assert.Equal(new[] { nameof(MainWindowViewModel.Brush) }, raised);
    }

    [Fact]
    public void SelectedSheet_ValidValue_RaisesOnlySelectedSheet()
    {
        var raised = RaisedProperties(() => _viewModel.SelectedSheet = 2);

        Assert.Equal(2, _viewModel.SelectedSheet);
        Assert.Equal(new[] { nameof(MainWindowViewModel.SelectedSheet) }, raised);
    }

    [Fact]
    public void SetSheetIds_RaisesOnlySheetIds()
    {
        var raised = RaisedProperties(() => _viewModel.SetSheetIds(new[] { 3, 7 }));

        Assert.Equal(new[] { 3, 7 }, _viewModel.SheetIds);
        Assert.Equal(new[] { nameof(MainWindowViewModel.SheetIds) }, raised);
    }

    [Fact]
    public void LayerVisibility_RaisesOnlyChangedProperty()
    {
        var raised = RaisedProperties(() => _viewModel.LayerVisibility = (byte)0b11011);

        Assert.Equal((byte)0b11011, _viewModel.LayerVisibility);
        Assert.Equal(new[] { nameof(MainWindowViewModel.LayerVisibility) }, raised);
    }

    [Fact]
    public void LayerVisibility_OutOfRange_Throws()
    {
        int canvasInvalidations = 0;
        _viewModel.CanvasInvalidated += () => canvasInvalidations++;

        Assert.Throws<ArgumentOutOfRangeException>(() => _viewModel.LayerVisibility = (byte)0b100000);

        Assert.Equal((byte)0b11111, _viewModel.LayerVisibility);
        Assert.Equal(0, canvasInvalidations);
    }

    [Fact]
    public void GridAndBlockedOptions_RaiseOnlyTheirOwnProperties()
    {
        var raised = RaisedProperties(() =>
        {
            _viewModel.ShowGrid = false;
            _viewModel.ShowBlocked = true;
        });

        Assert.False(_viewModel.ShowGrid);
        Assert.True(_viewModel.ShowBlocked);
        Assert.Equal(new[] { nameof(MainWindowViewModel.ShowGrid), nameof(MainWindowViewModel.ShowBlocked) }, raised);
    }

    [Fact]
    public void HoverCoordinates_RaiseOnlyTheirOwnProperties()
    {
        var raised = RaisedProperties(() =>
        {
            _viewModel.HoverX = 5;
            _viewModel.HoverY = 6;
        });

        Assert.Equal(new[] { nameof(MainWindowViewModel.HoverX), nameof(MainWindowViewModel.HoverY) }, raised);
    }

    [Fact]
    public void ZoomPercent_ValidValue_RaisesOnlyZoomPercent()
    {
        var raised = RaisedProperties(() => _viewModel.ZoomPercent = 200);

        Assert.Equal(200, _viewModel.ZoomPercent);
        Assert.Equal(new[] { nameof(MainWindowViewModel.ZoomPercent) }, raised);
    }

    [Theory]
    [InlineData(24)]
    [InlineData(401)]
    public void ZoomPercent_OutOfRange_Throws(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _viewModel.ZoomPercent = value);
        Assert.Equal(100, _viewModel.ZoomPercent);
    }

    [Fact]
    public async Task Title_StarFollowsDirtyState()
    {
        MapEditSession session = _viewModel.Session;
        session.SelectedTileLayer = new MapTileLayer(1, 1);
        session.BeginStroke(MapEditTool.Pencil, 0, 0);
        Assert.True(session.CompleteStroke());
        _viewModel.Refresh(EditorRefresh.Title);
        Assert.EndsWith("*", _viewModel.Title);

        _dialogs.SavePickResult = MapPath("titled.bytes");

        await _viewModel.SaveAsAsync();
        Assert.Equal("Goose2 Map Editor — titled.bytes", _viewModel.Title);

        session = _viewModel.Session;
        session.SelectedTileLayer = new MapTileLayer(1, 1);
        session.BeginStroke(MapEditTool.Pencil, 1, 0);
        Assert.True(session.CompleteStroke());
        _viewModel.Refresh(EditorRefresh.Title);
        Assert.Equal("Goose2 Map Editor — titled.bytes*", _viewModel.Title);
    }

    [Fact]
    public async Task Commands_RefreshAfterSaveAndEdit()
    {
        _dialogs.SavePickResult = MapPath("commands.bytes");

        await _viewModel.SaveAsAsync();
        Assert.False(_viewModel.CanSave);
        Assert.False(_viewModel.CanUndo);

        MapEditSession session = _viewModel.Session;
        session.SelectedTileLayer = new MapTileLayer(1, 1);
        session.BeginStroke(MapEditTool.Pencil, 0, 0);
        Assert.True(session.CompleteStroke());
        _viewModel.Refresh(EditorRefresh.Commands);
        Assert.True(_viewModel.CanSave);
        Assert.True(_viewModel.CanUndo);
    }

    [Fact]
    public void Undo_ThroughViewModel_RepaintsCanvasAndUpdatesCommands()
    {
        MapEditSession session = _viewModel.Session;
        session.SelectedTileLayer = new MapTileLayer(1, 1);
        session.BeginStroke(MapEditTool.Pencil, 0, 0);
        Assert.True(session.CompleteStroke());
        _viewModel.Refresh(EditorRefresh.Commands);

        int canvasInvalidations = 0;
        _viewModel.CanvasInvalidated += () => canvasInvalidations++;

        Assert.True(_viewModel.Undo());

        Assert.False(_viewModel.CanUndo);
        Assert.True(_viewModel.CanRedo);
        Assert.Equal(1, canvasInvalidations);
    }

    [Fact]
    public void DisplayToggles_RepaintCanvasOnlyOnChange()
    {
        int canvasInvalidations = 0;
        _viewModel.CanvasInvalidated += () => canvasInvalidations++;

        _viewModel.LayerVisibility = (byte)0b11110;
        Assert.Equal(1, canvasInvalidations);

        _viewModel.LayerVisibility = (byte)0b11110;
        Assert.Equal(1, canvasInvalidations);

        _viewModel.ShowGrid = false;
        _viewModel.ShowBlocked = true;
        Assert.Equal(3, canvasInvalidations);
    }

    [Fact]
    public void Refresh_TitleWithoutChange_RaisesNothing()
    {
        var raised = RaisedProperties(() => _viewModel.Refresh(EditorRefresh.Title));

        Assert.Empty(raised);
    }

    [Fact]
    public void Refresh_Palette_InvokesPaletteInvalidationOnly()
    {
        int paletteInvalidations = 0;
        int canvasInvalidations = 0;
        _viewModel.PaletteInvalidated += () => paletteInvalidations++;
        _viewModel.CanvasInvalidated += () => canvasInvalidations++;

        var raised = RaisedProperties(() => _viewModel.Refresh(EditorRefresh.Palette));

        Assert.Equal(1, paletteInvalidations);
        Assert.Equal(0, canvasInvalidations);
        Assert.Empty(raised);
    }

    [Fact]
    public async Task New_ReplacesDocument_ResetsHoverSelectionDimensionsAndRepaints()
    {
        _viewModel.HoverX = 10;
        _viewModel.HoverY = 11;
        _viewModel.SelectedX = 12;
        _viewModel.SelectedY = 13;

        int canvasInvalidations = 0;
        _viewModel.CanvasInvalidated += () => canvasInvalidations++;

        _dialogs.NewMapResult = new NewMapRequest(30, 40);
        _dialogs.DirtyResult = DirtyChoice.Discard;

        await _viewModel.NewAsync();

        Assert.Equal(30, _viewModel.MapWidth);
        Assert.Equal(40, _viewModel.MapHeight);
        Assert.Null(_viewModel.HoverX);
        Assert.Null(_viewModel.HoverY);
        Assert.Null(_viewModel.SelectedX);
        Assert.Null(_viewModel.SelectedY);
        Assert.Equal("Goose2 Map Editor — Untitled", _viewModel.Title);
        Assert.Equal(1, canvasInvalidations);
    }

    [Fact]
    public async Task New_ReplacesDocument_RaisesBrushAndSelectedLayersWithSessionResetValues()
    {
        _viewModel.SelectedLayers = (byte)(1 << 2);
        _viewModel.Brush = new MapTileLayer(9, 9);

        _dialogs.NewMapResult = new NewMapRequest(10, 10);
        _dialogs.DirtyResult = DirtyChoice.Discard;

        var raised = new List<string>();
        _viewModel.PropertyChanged += (sender, e) => raised.Add(e.PropertyName ?? string.Empty);

        await _viewModel.NewAsync();

        Assert.Contains(nameof(MainWindowViewModel.SelectedLayers), raised);
        Assert.Contains(nameof(MainWindowViewModel.Brush), raised);
        Assert.Equal((byte)1, _viewModel.SelectedLayers);
        Assert.Equal(new MapTileLayer(0, 0), _viewModel.Brush);
    }

    [Fact]
    public async Task Open_UpdatesTitleDimensionsAndCommands()
    {
        string path = MapPath("opened.bytes");
        new MapFileStore().Save(path, MapDocument.Create(22, 33));

        _dialogs.OpenPickResult = path;
        _dialogs.DirtyResult = DirtyChoice.Discard;

        await _viewModel.OpenAsync();

        Assert.Equal("Goose2 Map Editor — opened.bytes", _viewModel.Title);
        Assert.Equal(22, _viewModel.MapWidth);
        Assert.Equal(33, _viewModel.MapHeight);
        Assert.False(_viewModel.CanSave);
    }

    [Fact]
    public async Task RequestClose_DelegatesToController()
    {
        MapEditSession session = _viewModel.Session;
        session.SelectedTileLayer = new MapTileLayer(1, 1);
        session.BeginStroke(MapEditTool.Pencil, 0, 0);
        Assert.True(session.CompleteStroke());
        _dialogs.DirtyResult = DirtyChoice.Discard;

        Assert.True(await _viewModel.RequestCloseAsync());
        Assert.Equal(1, _dialogs.DirtyShown);
    }

    [Fact]
    public void CopySelection_CapturesOnlySelectedLayers()
    {
        // the stroke captures the brush at BeginStroke time, so set the brush first
        _viewModel.SelectedLayers = 0b01001; // layers 0 and 3
        _viewModel.Brush = new MapTileLayer(5, 5);
        _viewModel.Session.BeginStroke(MapEditTool.Pencil, 0, 0);
        _viewModel.Session.ContinueStroke(1, 0);
        _viewModel.Session.CompleteStroke();
        _viewModel.SelectionRectangle = new MapTileRectangle(0, 0, 2, 1);
        _viewModel.CopySelection();

        var clip = _viewModel.Clipboard!;
        Assert.Equal(2, clip.Width);
        Assert.Equal(1, clip.Height);
        Assert.NotNull(clip.Layers[0]);
        Assert.Null(clip.Layers[1]);
        Assert.Equal(new MapTileLayer(5, 5), clip.Layers[3]![0]);
    }

    [Fact]
    public void ApplyPasteAt_WritesEachCapturedLayerToItsCorrespondingLayer()
    {
        _viewModel.SelectedLayers = 0b01001;
        for (int x = 0; x < 3; x++)
        {
            _viewModel.Session.Document.SetLayer(x, 0, 0, new MapTileLayer(5, 5));
            _viewModel.Session.Document.SetLayer(x, 0, 3, new MapTileLayer(6, 6));
        }

        _viewModel.SelectionRectangle = new MapTileRectangle(0, 0, 3, 1);
        _viewModel.CopySelection();
        _viewModel.BeginPasteMode();
        _viewModel.ApplyPasteAt(5, 2);

        for (int x = 5; x < 8; x++)
        {
            Assert.Equal(new MapTileLayer(5, 5), _viewModel.Session.Document[x, 2].GetLayer(0));
            Assert.Equal(new MapTileLayer(6, 6), _viewModel.Session.Document[x, 2].GetLayer(3));
            Assert.Equal(new MapTileLayer(0, 0), _viewModel.Session.Document[x, 2].GetLayer(1));
            Assert.Equal(new MapTileLayer(0, 0), _viewModel.Session.Document[x, 2].GetLayer(2));
            Assert.Equal(new MapTileLayer(0, 0), _viewModel.Session.Document[x, 2].GetLayer(4));
        }

        Assert.False(_viewModel.PasteMode);
        Assert.True(_viewModel.CanUndo);
    }

    [Fact]
    public void ApplyPasteAt_EdgeOrigin_ClipsToDocumentBounds()
    {
        SeedClipboard();
        _viewModel.BeginPasteMode();
        Assert.True(_viewModel.PasteMode);
        _viewModel.ApplyPasteAt(98, 98);

        Assert.Equal(new MapTileLayer(7, 7), _viewModel.Session.Document[98, 98].GetLayer(0));
        Assert.Equal(new MapTileLayer(7, 7), _viewModel.Session.Document[99, 99].GetLayer(0));
        Assert.Equal(new MapTileLayer(0, 0), _viewModel.Session.Document[97, 97].GetLayer(0));
        Assert.False(_viewModel.PasteMode);
        Assert.True(_viewModel.CanUndo);
        Assert.True(_viewModel.Undo());
    }

    [Fact]
    public void ApplyPasteAt_NegativeOrigin_ClipsSourceAndDestination()
    {
        // distinct values per source cell so the assertion proves which source cell landed
        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 3; x++)
            {
                _viewModel.Session.Document.SetLayer(x, y, 0, new MapTileLayer(x + 1, y + 1));
            }
        }

        _viewModel.SelectedLayers = 0b00001;
        _viewModel.SelectionRectangle = new MapTileRectangle(0, 0, 3, 3);
        _viewModel.CopySelection();
        _viewModel.BeginPasteMode();
        _viewModel.ApplyPasteAt(-2, -2);

        Assert.Equal(new MapTileLayer(3, 3), _viewModel.Session.Document[0, 0].GetLayer(0));
        Assert.False(_viewModel.PasteMode);
        Assert.True(_viewModel.CanUndo);
    }

    [Fact]
    public void ApplyPasteAt_FullyOutOfBounds_ChangesNothing()
    {
        // 100x100 default document, 3x3 clipboard; origin at x == Width puts the
        // whole ghost past the right edge (clip width computes to 0)
        SeedClipboard();
        _viewModel.BeginPasteMode();
        _viewModel.ApplyPasteAt(100, 0);
        Assert.False(_viewModel.PasteMode);
        Assert.False(_viewModel.CanUndo);
    }

    [Fact]
    public void PasteMode_CancelsWhenLayerSelectionChanges()
    {
        SeedClipboard();
        _viewModel.BeginPasteMode();
        _viewModel.SelectedLayers = 1 << 2;
        Assert.False(_viewModel.PasteMode);
    }

    [Fact]
    public async Task NewDocument_ClearsClipboardPasteAndSelectionRectangle()
    {
        SeedClipboard();
        _viewModel.BeginPasteMode();
        _dialogs.NewMapResult = new NewMapRequest(10, 10);
        _dialogs.DirtyResult = DirtyChoice.Discard;
        await _viewModel.NewAsync();
        Assert.Null(_viewModel.Clipboard);
        Assert.Null(_viewModel.SelectionRectangle);
        Assert.False(_viewModel.PasteMode);
    }

    private async Task New4x4Async()
    {
        _dialogs.NewMapResult = new NewMapRequest(4, 4);
        _dialogs.DirtyResult = DirtyChoice.Discard;
        await _viewModel.NewAsync();
    }

    [Fact]
    public async Task ResizeMap_ShiftsSelectionAndRefreshesCachedDimensions()
    {
        await New4x4Async();
        _viewModel.SelectedX = 0;
        _viewModel.SelectedY = 0;

        _viewModel.ResizeMap(new MapTileRectangle(-2, -2, 6, 6));

        Assert.Equal(6, _viewModel.MapWidth);
        Assert.Equal(6, _viewModel.MapHeight);
        Assert.Equal(2, _viewModel.SelectedX);
        Assert.Equal(2, _viewModel.SelectedY);
    }

    [Fact]
    public async Task ResizeMap_SelectionCroppedAway_IsClearedNotClamped()
    {
        await New4x4Async();
        _viewModel.SelectedX = 3;
        _viewModel.SelectedY = 1;

        _viewModel.ResizeMap(new MapTileRectangle(0, 0, 2, 4));

        Assert.Null(_viewModel.SelectedX);
        Assert.Null(_viewModel.SelectedY);
    }

    [Fact]
    public async Task ResizeMap_CancelsPasteMode()
    {
        await New4x4Async();
        _viewModel.SelectionRectangle = new MapTileRectangle(0, 0, 2, 2);
        _viewModel.CopySelection();
        _viewModel.BeginPasteMode();
        Assert.True(_viewModel.PasteMode);

        _viewModel.ResizeMap(new MapTileRectangle(0, 0, 4, 6));

        Assert.False(_viewModel.PasteMode);
    }

    [Fact]
    public async Task ResizeMap_PartiallyCroppedSelection_KeepsTheSurvivingPart()
    {
        await New4x4Async();
        _viewModel.SelectionRectangle = new MapTileRectangle(1, 1, 3, 3);

        _viewModel.ResizeMap(new MapTileRectangle(0, 0, 3, 3));

        Assert.Equal(new MapTileRectangle(1, 1, 2, 2), _viewModel.SelectionRectangle);
    }

    [Fact]
    public async Task UndoOfAResize_RestoresDimensionsAndUnshiftsTheSelection()
    {
        await New4x4Async();
        _viewModel.SelectedX = 0;
        _viewModel.SelectedY = 0;

        _viewModel.ResizeMap(new MapTileRectangle(-2, -2, 6, 6));
        Assert.Equal(6, _viewModel.MapWidth);
        Assert.Equal(2, _viewModel.SelectedX);
        Assert.Equal(2, _viewModel.SelectedY);

        Assert.True(_viewModel.Undo());

        Assert.Equal(4, _viewModel.MapWidth);
        Assert.Equal(4, _viewModel.MapHeight);
        Assert.Equal(0, _viewModel.SelectedX);
        Assert.Equal(0, _viewModel.SelectedY);
    }

    [Fact]
    public async Task RedoOfAResize_ReappliesDimensionsAndShift()
    {
        await New4x4Async();
        _viewModel.SelectedX = 0;
        _viewModel.SelectedY = 0;

        _viewModel.ResizeMap(new MapTileRectangle(-2, -2, 6, 6));
        Assert.True(_viewModel.Undo());
        Assert.True(_viewModel.Redo());

        Assert.Equal(6, _viewModel.MapWidth);
        Assert.Equal(6, _viewModel.MapHeight);
        Assert.Equal(2, _viewModel.SelectedX);
        Assert.Equal(2, _viewModel.SelectedY);
    }

    [Fact]
    public async Task DocumentReplacement_UnsubscribesTheOldSessionResizeHandler()
    {
        await New4x4Async();
        MapEditSession first = _viewModel.Session;
        first.ApplyResize(new MapTileRectangle(0, 0, 4, 6));
        Assert.Equal(6, _viewModel.MapHeight);

        _dialogs.NewMapResult = new NewMapRequest(8, 8);
        await _viewModel.NewAsync();
        Assert.Equal(8, _viewModel.MapWidth);
        Assert.Equal(8, _viewModel.MapHeight);

        first.ApplyResize(new MapTileRectangle(0, 0, 6, 6));

        Assert.Equal(8, _viewModel.MapWidth);
        Assert.Equal(8, _viewModel.MapHeight);
    }
}
