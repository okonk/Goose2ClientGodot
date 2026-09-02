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

    [Fact]
    public void InitialState_ReflectsNewDirtyDocument()
    {
        Assert.Equal("Goose2 Map Editor — Untitled*", _viewModel.Title);
        Assert.Same(_controller.Document.Session, _viewModel.Session);
        Assert.Equal(MapEditTool.Pencil, _viewModel.ActiveTool);
        Assert.Equal(0, _viewModel.ActiveLayer);
        Assert.Equal(new MapTileLayer(0, 0), _viewModel.Brush);
        Assert.Empty(_viewModel.SheetIds);
        Assert.Equal(0, _viewModel.SelectedSheet);
        Assert.True(_viewModel.Layer0Visible);
        Assert.True(_viewModel.Layer1Visible);
        Assert.True(_viewModel.Layer2Visible);
        Assert.True(_viewModel.Layer3Visible);
        Assert.True(_viewModel.Layer4Visible);
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
        Assert.True(_viewModel.CanSave);
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
    public void ActiveLayer_ValidValue_WritesToSessionAndRaisesOnlyActiveLayer()
    {
        var raised = RaisedProperties(() => _viewModel.ActiveLayer = 3);

        Assert.Equal(3, _controller.Document.Session.ActiveLayer);
        Assert.Equal(new[] { nameof(MainWindowViewModel.ActiveLayer) }, raised);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public void ActiveLayer_OutOfRange_ThrowsWithoutMutatingSession(int layer)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _viewModel.ActiveLayer = layer);
        Assert.Equal(0, _controller.Document.Session.ActiveLayer);
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
        var raised = RaisedProperties(() => _viewModel.Layer2Visible = false);

        Assert.False(_viewModel.Layer2Visible);
        Assert.Equal(new[] { nameof(MainWindowViewModel.Layer2Visible) }, raised);
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
        Assert.EndsWith("*", _viewModel.Title);

        _dialogs.SavePickResult = MapPath("titled.bytes");

        await _viewModel.SaveAsAsync();
        Assert.Equal("Goose2 Map Editor — titled.bytes", _viewModel.Title);

        MapEditSession session = _viewModel.Session;
        session.SelectedTileLayer = new MapTileLayer(1, 1);
        session.BeginStroke(MapEditTool.Pencil, 0, 0);
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
        Assert.Equal("Goose2 Map Editor — Untitled*", _viewModel.Title);
        Assert.Equal(1, canvasInvalidations);
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
        _dialogs.DirtyResult = DirtyChoice.Discard;

        Assert.True(await _viewModel.RequestCloseAsync());
        Assert.Equal(1, _dialogs.DirtyShown);
    }
}
