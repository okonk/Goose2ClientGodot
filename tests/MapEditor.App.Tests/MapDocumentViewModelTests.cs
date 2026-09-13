using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using MapEditor.App.Dialogs;
using MapEditor.App.Documents;
using MapEditor.App.Rendering;
using MapEditor.App.Tests.Fakes;
using MapEditor.App.ViewModels;
using MapEditor.Core;
using MapEditor.GameData.Editing;
using MapEditor.GameData.Rows;
using MapEditor.Rendering;
using Xunit;

namespace MapEditor.App.Tests;

public class MapDocumentViewModelTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("map-editor-vm-").FullName;
    private readonly FakeEditorDialogs _dialogs = new();
    private readonly EditorDocumentController _controller;
    private readonly MapDocumentViewModel _viewModel;

    public MapDocumentViewModelTests()
    {
        _controller = new EditorDocumentController(_dialogs, new MapFileStore(), InitialDocument());
        _viewModel = new MapDocumentViewModel(_controller, new SharedTileClipboard());
    }

    public void Dispose() => Directory.Delete(_directory, true);

    private string MapPath(string name) => Path.Combine(_directory, name);

    private static EditorDocument InitialDocument()
        => new(new MapEditSession(MapDocument.Create(), initiallyDirty: false), null, null);

    private MapDocumentViewModel Create4x4ViewModel()
        => new(new EditorDocumentController(_dialogs, new MapFileStore(),
            new EditorDocument(new MapEditSession(MapDocument.Create(4, 4), initiallyDirty: false), null, null)),
            new SharedTileClipboard());

    private static readonly Guid GrassId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid WaterId = new("22222222-2222-2222-2222-222222222222");

    private static TerrainCatalogLoadResult ValidTerrainCatalog(params Guid[] terrainIds)
    {
        var definitions = new List<TerrainDefinition>();
        var graphics = new List<TerrainGraphicDefinition>();
        int graphic = 10;
        foreach (Guid id in terrainIds)
        {
            definitions.Add(new TerrainDefinition(id, $"Terrain {graphic}", null));
            graphics.Add(new TerrainGraphicDefinition(new TerrainGraphicReference(1, graphic), new TerrainPattern(Center: id)));
            graphic += 2;
        }

        var catalog = new TerrainCatalog(definitions, graphics);
        TerrainCatalogValidationResult validation = TerrainCatalogValidator.Validate(catalog);
        return TerrainCatalogLoadResult.Valid(
            "terrain-brushes.json",
            new TerrainFileRevision(true, "unit-test"),
            catalog,
            validation.Index,
            validation.Issues);
    }

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
        Assert.Equal(new[] { nameof(MapDocumentViewModel.ActiveTool) }, raised);
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
        Assert.Equal(new[] { nameof(MapDocumentViewModel.SelectedLayers) }, raised);
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
        Assert.Equal(new[] { nameof(MapDocumentViewModel.Brush) }, raised);
    }

    [Fact]
    public void SelectedSheet_ValidValue_RaisesOnlySelectedSheet()
    {
        var raised = RaisedProperties(() => _viewModel.SelectedSheet = 2);

        Assert.Equal(2, _viewModel.SelectedSheet);
        Assert.Equal(new[] { nameof(MapDocumentViewModel.SelectedSheet) }, raised);
    }

    [Fact]
    public void SetSheetIds_RaisesOnlySheetIds()
    {
        var raised = RaisedProperties(() => _viewModel.SetSheetIds(new[] { 3, 7 }));

        Assert.Equal(new[] { 3, 7 }, _viewModel.SheetIds);
        Assert.Equal(new[] { nameof(MapDocumentViewModel.SheetIds) }, raised);
    }

    [Fact]
    public void LayerVisibility_RaisesOnlyChangedProperty()
    {
        var raised = RaisedProperties(() => _viewModel.LayerVisibility = (byte)0b11011);

        Assert.Equal((byte)0b11011, _viewModel.LayerVisibility);
        Assert.Equal(new[] { nameof(MapDocumentViewModel.LayerVisibility) }, raised);
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
        Assert.Equal(new[] { nameof(MapDocumentViewModel.ShowGrid), nameof(MapDocumentViewModel.ShowBlocked) }, raised);
    }

    [Fact]
    public void HoverCoordinates_RaiseOnlyTheirOwnProperties()
    {
        var raised = RaisedProperties(() =>
        {
            _viewModel.HoverX = 5;
            _viewModel.HoverY = 6;
        });

        Assert.Equal(new[] { nameof(MapDocumentViewModel.HoverX), nameof(MapDocumentViewModel.HoverY) }, raised);
    }

    [Fact]
    public void ZoomPercent_ValidValue_RaisesOnlyZoomPercent()
    {
        var raised = RaisedProperties(() => _viewModel.ZoomPercent = 200);

        Assert.Equal(200, _viewModel.ZoomPercent);
        Assert.Equal(new[] { nameof(MapDocumentViewModel.ZoomPercent) }, raised);
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

        _dialogs.SavePickResult = MapPath("titled.map");

        await _viewModel.SaveAsAsync();
        Assert.Equal("Goose2 Map Editor — titled.map", _viewModel.Title);

        session = _viewModel.Session;
        session.SelectedTileLayer = new MapTileLayer(1, 1);
        session.BeginStroke(MapEditTool.Pencil, 1, 0);
        Assert.True(session.CompleteStroke());
        _viewModel.Refresh(EditorRefresh.Title);
        Assert.Equal("Goose2 Map Editor — titled.map*", _viewModel.Title);
    }

    [Fact]
    public async Task Commands_RefreshAfterSaveAndEdit()
    {
        _dialogs.SavePickResult = MapPath("commands.map");

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
    public async Task ConfirmClose_DelegatesToController()
    {
        MapEditSession session = _viewModel.Session;
        session.SelectedTileLayer = new MapTileLayer(1, 1);
        session.BeginStroke(MapEditTool.Pencil, 0, 0);
        Assert.True(session.CompleteStroke());
        _dialogs.DirtyResult = DirtyChoice.Discard;

        Assert.True(await _viewModel.ConfirmCloseAsync());
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
    public void ApplyPasteAt_SingleSourceLayer_PastesToCurrentlySelectedLayer()
    {
        _viewModel.SelectedLayers = 0b00001;
        for (int x = 0; x < 3; x++)
        {
            _viewModel.Session.Document.SetLayer(x, 0, 0, new MapTileLayer(5, 5));
        }

        _viewModel.SelectionRectangle = new MapTileRectangle(0, 0, 3, 1);
        _viewModel.CopySelection();
        _viewModel.SelectedLayers = 0b00100;
        _viewModel.BeginPasteMode();
        _viewModel.ApplyPasteAt(5, 2);

        for (int x = 5; x < 8; x++)
        {
            Assert.Equal(new MapTileLayer(5, 5), _viewModel.Session.Document[x, 2].GetLayer(2));
            Assert.Equal(new MapTileLayer(0, 0), _viewModel.Session.Document[x, 2].GetLayer(0));
        }
    }

    [Fact]
    public void ApplyPasteAt_SingleSourceLayer_MultiTarget_PastesToTopMostSelected()
    {
        _viewModel.SelectedLayers = 0b00001;
        for (int x = 0; x < 3; x++)
        {
            _viewModel.Session.Document.SetLayer(x, 0, 0, new MapTileLayer(5, 5));
        }

        _viewModel.SelectionRectangle = new MapTileRectangle(0, 0, 3, 1);
        _viewModel.CopySelection();
        _viewModel.SelectedLayers = 0b10001; // layers 0 and 4
        _viewModel.BeginPasteMode();
        _viewModel.ApplyPasteAt(5, 2);

        for (int x = 5; x < 8; x++)
        {
            Assert.Equal(new MapTileLayer(5, 5), _viewModel.Session.Document[x, 2].GetLayer(4));
            Assert.Equal(new MapTileLayer(0, 0), _viewModel.Session.Document[x, 2].GetLayer(0));
        }
    }

    [Fact]
    public void ApplyPasteAt_MultiSource_SingleTarget_PastesTopMostSourceToSelected()
    {
        for (int x = 0; x < 3; x++)
        {
            _viewModel.Session.Document.SetLayer(x, 0, 0, new MapTileLayer(5, 5));
            _viewModel.Session.Document.SetLayer(x, 0, 3, new MapTileLayer(6, 6));
        }

        _viewModel.SelectedLayers = 0b01001; // layers 0 and 3
        _viewModel.SelectionRectangle = new MapTileRectangle(0, 0, 3, 1);
        _viewModel.CopySelection();
        _viewModel.SelectedLayers = 0b00100;
        _viewModel.BeginPasteMode();
        _viewModel.ApplyPasteAt(5, 2);

        for (int x = 5; x < 8; x++)
        {
            Assert.Equal(new MapTileLayer(6, 6), _viewModel.Session.Document[x, 2].GetLayer(2));
        }
    }

    [Fact]
    public void ApplyPasteAt_MultiSource_DifferentMultiTarget_PairsTopAlignedAndDropsUnpaired()
    {
        for (int x = 0; x < 3; x++)
        {
            _viewModel.Session.Document.SetLayer(x, 0, 0, new MapTileLayer(1, 1));
            _viewModel.Session.Document.SetLayer(x, 0, 1, new MapTileLayer(2, 2));
            _viewModel.Session.Document.SetLayer(x, 0, 3, new MapTileLayer(3, 3));
        }

        _viewModel.SelectedLayers = 0b01011; // layers 0, 1, 3
        _viewModel.SelectionRectangle = new MapTileRectangle(0, 0, 3, 1);
        _viewModel.CopySelection();
        _viewModel.SelectedLayers = 0b10100; // layers 2, 4
        _viewModel.BeginPasteMode();
        _viewModel.ApplyPasteAt(5, 2);

        for (int x = 5; x < 8; x++)
        {
            Assert.Equal(new MapTileLayer(3, 3), _viewModel.Session.Document[x, 2].GetLayer(4));
            Assert.Equal(new MapTileLayer(2, 2), _viewModel.Session.Document[x, 2].GetLayer(2));
            Assert.Equal(new MapTileLayer(0, 0), _viewModel.Session.Document[x, 2].GetLayer(0));
        }
    }

    [Fact]
    public void ApplyPasteAt_MultiSource_MoreTargetsThanSources_ExtraTargetsGetNothing()
    {
        for (int x = 0; x < 3; x++)
        {
            _viewModel.Session.Document.SetLayer(x, 0, 0, new MapTileLayer(5, 5));
            _viewModel.Session.Document.SetLayer(x, 0, 3, new MapTileLayer(6, 6));
        }

        _viewModel.SelectedLayers = 0b01001; // layers 0, 3
        _viewModel.SelectionRectangle = new MapTileRectangle(0, 0, 3, 1);
        _viewModel.CopySelection();
        _viewModel.SelectedLayers = 0b01110; // layers 1, 2, 3
        _viewModel.BeginPasteMode();
        _viewModel.ApplyPasteAt(5, 2);

        for (int x = 5; x < 8; x++)
        {
            Assert.Equal(new MapTileLayer(6, 6), _viewModel.Session.Document[x, 2].GetLayer(3));
            Assert.Equal(new MapTileLayer(5, 5), _viewModel.Session.Document[x, 2].GetLayer(2));
            Assert.Equal(new MapTileLayer(0, 0), _viewModel.Session.Document[x, 2].GetLayer(1));
        }
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
    public void DeleteSelection_ClearsTilesOnSelectedLayersOnly()
    {
        _viewModel.SelectedLayers = 0b01001; // layers 0 and 3
        for (int x = 0; x < 3; x++)
        {
            _viewModel.Session.Document.SetLayer(x, 0, 0, new MapTileLayer(5, 5));
            _viewModel.Session.Document.SetLayer(x, 0, 1, new MapTileLayer(6, 6));
            _viewModel.Session.Document.SetLayer(x, 0, 3, new MapTileLayer(7, 7));
        }

        _viewModel.SelectionRectangle = new MapTileRectangle(0, 0, 3, 1);
        _viewModel.DeleteSelection();

        for (int x = 0; x < 3; x++)
        {
            Assert.Equal(new MapTileLayer(0, 0), _viewModel.Session.Document[x, 0].GetLayer(0));
            Assert.Equal(new MapTileLayer(0, 0), _viewModel.Session.Document[x, 0].GetLayer(3));
            Assert.Equal(new MapTileLayer(6, 6), _viewModel.Session.Document[x, 0].GetLayer(1));
        }

        Assert.Equal(new MapTileRectangle(0, 0, 3, 1), _viewModel.SelectionRectangle);
        Assert.True(_viewModel.CanUndo);
    }

    [Fact]
    public void DeleteSelection_UndoRestoresTiles()
    {
        _viewModel.SelectedLayers = 0b01001;
        _viewModel.Session.Document.SetLayer(0, 0, 0, new MapTileLayer(5, 5));
        _viewModel.Session.Document.SetLayer(0, 0, 3, new MapTileLayer(7, 7));

        _viewModel.SelectionRectangle = new MapTileRectangle(0, 0, 1, 1);
        _viewModel.DeleteSelection();
        Assert.True(_viewModel.Undo());

        Assert.Equal(new MapTileLayer(5, 5), _viewModel.Session.Document[0, 0].GetLayer(0));
        Assert.Equal(new MapTileLayer(7, 7), _viewModel.Session.Document[0, 0].GetLayer(3));
    }

    [Fact]
    public void DeleteSelection_WithoutSelectionRectangle_DoesNothing()
    {
        _viewModel.Session.Document.SetLayer(0, 0, 0, new MapTileLayer(5, 5));

        _viewModel.DeleteSelection();

        Assert.Equal(new MapTileLayer(5, 5), _viewModel.Session.Document[0, 0].GetLayer(0));
        Assert.False(_viewModel.CanUndo);
    }

    [Fact]
    public void DeleteSelection_WhenSelectionAlreadyEmpty_DoesNotCreateUndoEntry()
    {
        _viewModel.SelectionRectangle = new MapTileRectangle(0, 0, 2, 2);

        _viewModel.DeleteSelection();

        Assert.False(_viewModel.CanUndo);
    }

    [Fact]
    public void DeleteSelection_CancelsPasteMode()
    {
        SeedClipboard();
        _viewModel.BeginPasteMode();
        _viewModel.DeleteSelection();
        Assert.False(_viewModel.PasteMode);
    }

    [Fact]
    public void CutSelection_CopiesTilesThenClearsThem()
    {
        _viewModel.SelectedLayers = 0b01001; // layers 0 and 3
        for (int x = 0; x < 3; x++)
        {
            _viewModel.Session.Document.SetLayer(x, 0, 0, new MapTileLayer(5, 5));
            _viewModel.Session.Document.SetLayer(x, 0, 3, new MapTileLayer(6, 6));
        }

        _viewModel.SelectionRectangle = new MapTileRectangle(0, 0, 3, 1);
        _viewModel.CutSelection();

        for (int x = 0; x < 3; x++)
        {
            Assert.Equal(new MapTileLayer(0, 0), _viewModel.Session.Document[x, 0].GetLayer(0));
            Assert.Equal(new MapTileLayer(0, 0), _viewModel.Session.Document[x, 0].GetLayer(3));
        }

        var clip = _viewModel.Clipboard!;
        Assert.Equal(3, clip.Width);
        Assert.Equal(1, clip.Height);
        Assert.Equal(new MapTileLayer(5, 5), clip.Layers[0]![0]);
        Assert.Equal(new MapTileLayer(6, 6), clip.Layers[3]![0]);
        Assert.Null(clip.Layers[1]);
        Assert.Null(clip.Layers[2]);
        Assert.True(_viewModel.CanUndo);
    }

    [Fact]
    public void CutSelection_WithoutSelectionRectangle_DoesNothing()
    {
        _viewModel.Session.Document.SetLayer(0, 0, 0, new MapTileLayer(5, 5));

        _viewModel.CutSelection();

        Assert.Null(_viewModel.Clipboard);
        Assert.Equal(new MapTileLayer(5, 5), _viewModel.Session.Document[0, 0].GetLayer(0));
        Assert.False(_viewModel.CanUndo);
    }

    [Fact]
    public void Refresh_Commands_DoesNotClearClipboard()
    {
        SeedClipboard();

        _viewModel.Refresh(EditorRefresh.Commands | EditorRefresh.Title);

        Assert.NotNull(_viewModel.Clipboard);
    }

    [Fact]
    public void ResizeMap_ShiftsSelectionAndRefreshesCachedDimensions()
    {
        MapDocumentViewModel viewModel = Create4x4ViewModel();
        viewModel.SelectedX = 0;
        viewModel.SelectedY = 0;

        viewModel.ResizeMap(new MapTileRectangle(-2, -2, 6, 6));

        Assert.Equal(6, viewModel.MapWidth);
        Assert.Equal(6, viewModel.MapHeight);
        Assert.Equal(2, viewModel.SelectedX);
        Assert.Equal(2, viewModel.SelectedY);
    }

    [Fact]
    public void ResizeMap_SelectionCroppedAway_IsClearedNotClamped()
    {
        MapDocumentViewModel viewModel = Create4x4ViewModel();
        viewModel.SelectedX = 3;
        viewModel.SelectedY = 1;

        viewModel.ResizeMap(new MapTileRectangle(0, 0, 2, 4));

        Assert.Null(viewModel.SelectedX);
        Assert.Null(viewModel.SelectedY);
    }

    [Fact]
    public void ResizeMap_CancelsPasteMode()
    {
        MapDocumentViewModel viewModel = Create4x4ViewModel();
        viewModel.SelectionRectangle = new MapTileRectangle(0, 0, 2, 2);
        viewModel.CopySelection();
        viewModel.BeginPasteMode();
        Assert.True(viewModel.PasteMode);

        viewModel.ResizeMap(new MapTileRectangle(0, 0, 4, 6));

        Assert.False(viewModel.PasteMode);
    }

    [Fact]
    public void ResizeMap_PartiallyCroppedSelection_KeepsTheSurvivingPart()
    {
        MapDocumentViewModel viewModel = Create4x4ViewModel();
        viewModel.SelectionRectangle = new MapTileRectangle(1, 1, 3, 3);

        viewModel.ResizeMap(new MapTileRectangle(0, 0, 3, 3));

        Assert.Equal(new MapTileRectangle(1, 1, 2, 2), viewModel.SelectionRectangle);
    }

    [Fact]
    public void UndoOfAResize_RestoresDimensionsAndUnshiftsTheSelection()
    {
        MapDocumentViewModel viewModel = Create4x4ViewModel();
        viewModel.SelectedX = 0;
        viewModel.SelectedY = 0;

        viewModel.ResizeMap(new MapTileRectangle(-2, -2, 6, 6));
        Assert.Equal(6, viewModel.MapWidth);
        Assert.Equal(2, viewModel.SelectedX);
        Assert.Equal(2, viewModel.SelectedY);

        Assert.True(viewModel.Undo());

        Assert.Equal(4, viewModel.MapWidth);
        Assert.Equal(4, viewModel.MapHeight);
        Assert.Equal(0, viewModel.SelectedX);
        Assert.Equal(0, viewModel.SelectedY);
    }

    [Fact]
    public void RedoOfAResize_ReappliesDimensionsAndShift()
    {
        MapDocumentViewModel viewModel = Create4x4ViewModel();
        viewModel.SelectedX = 0;
        viewModel.SelectedY = 0;

        viewModel.ResizeMap(new MapTileRectangle(-2, -2, 6, 6));
        Assert.True(viewModel.Undo());
        Assert.True(viewModel.Redo());

        Assert.Equal(6, viewModel.MapWidth);
        Assert.Equal(6, viewModel.MapHeight);
        Assert.Equal(2, viewModel.SelectedX);
        Assert.Equal(2, viewModel.SelectedY);
    }

    [Fact]
    public void LayerAnchorIsIndependentPerViewModel()
    {
        MapDocumentViewModel other = new(new EditorDocumentController(new FakeEditorDialogs(), new MapFileStore(), InitialDocument()), new SharedTileClipboard());

        _viewModel.LayerAnchor = 2;

        Assert.Equal(2, _viewModel.LayerAnchor);
        Assert.Equal(0, other.LayerAnchor);
    }

    [Fact]
    public void AttachSheetSession_ReplacesTimelineSubscriptionTargetAndCarriesMapEntries()
    {
        var stale = new SheetEditSession(Array.Empty<NpcSpawnRow>(), Array.Empty<WarpRow>());
        _viewModel.AttachSheetSession(stale);
        stale.AddSpawn(new NpcSpawnRow(1, 10, 5, 6));
        MapEditSession session = _viewModel.Session;
        session.SelectedTileLayer = new MapTileLayer(1, 2);
        session.BeginStroke(MapEditTool.Pencil, 0, 0);
        Assert.True(session.CompleteStroke());

        var fresh = new SheetEditSession(Array.Empty<NpcSpawnRow>(), Array.Empty<WarpRow>());
        _viewModel.AttachSheetSession(fresh);

        Assert.Same(fresh, _viewModel.SheetSession);
        Assert.Empty(fresh.Spawns);
        Assert.Single(stale.Spawns);

        // The pre-swap map edit survives the swap; the pre-swap sheet edit does not.
        Assert.True(_viewModel.Timeline.CanUndo);
        Assert.False(_viewModel.Timeline.CanRedo);
        Assert.True(_viewModel.Timeline.Undo());
        Assert.Equal(new MapTileLayer(0, 0), session.Document[0, 0].GetLayer(0));
        Assert.Single(stale.Spawns);

        Assert.True(_viewModel.Timeline.Redo());
        Assert.Equal(new MapTileLayer(1, 2), session.Document[0, 0].GetLayer(0));

        // The old session is fully detached: its edits are no longer recorded.
        stale.AddSpawn(new NpcSpawnRow(2, 10, 7, 8));
        Assert.True(_viewModel.Timeline.CanUndo);
        Assert.True(_viewModel.Timeline.Undo());
        Assert.Equal(new MapTileLayer(0, 0), session.Document[0, 0].GetLayer(0));
        Assert.Equal(2, stale.Spawns.Count);
        Assert.False(_viewModel.Timeline.CanUndo);
        Assert.True(_viewModel.Timeline.Redo());
        Assert.Equal(new MapTileLayer(1, 2), session.Document[0, 0].GetLayer(0));

        // Both domains record and undo through the new timeline.
        fresh.AddSpawn(new NpcSpawnRow(3, 20, 9, 10));
        session.SelectedTileLayer = new MapTileLayer(4, 5);
        session.BeginStroke(MapEditTool.Pencil, 1, 1);
        Assert.True(session.CompleteStroke());
        Assert.True(_viewModel.Timeline.Undo());
        Assert.Equal(new MapTileLayer(0, 0), session.Document[1, 1].GetLayer(0));
        Assert.True(_viewModel.Timeline.Undo());
        Assert.Empty(fresh.Spawns);
        Assert.Equal(2, stale.Spawns.Count);
    }

    [Fact]
    public void AttachSheetSession_MapHistorySurvivesPullAndInterleavesWithNewSheetEdits()
    {
        MapDocumentViewModel doc = Create4x4ViewModel();
        MapEditSession session = doc.Session;
        session.SelectedTileLayer = new MapTileLayer(1, 2);
        session.BeginStroke(MapEditTool.Pencil, 0, 0);
        Assert.True(session.CompleteStroke());

        var pulled = new SheetEditSession(Array.Empty<NpcSpawnRow>(), Array.Empty<WarpRow>());
        doc.AttachSheetSession(pulled);

        Assert.True(doc.Undo());
        Assert.Equal(new MapTileLayer(0, 0), session.Document[0, 0].GetLayer(0));

        pulled.AddSpawn(new NpcSpawnRow(1, 10, 1, 1));
        session.SelectedTileLayer = new MapTileLayer(3, 4);
        session.BeginStroke(MapEditTool.Pencil, 2, 2);
        Assert.True(session.CompleteStroke());

        Assert.True(doc.Undo());
        Assert.Equal(new MapTileLayer(0, 0), session.Document[2, 2].GetLayer(0));
        Assert.Single(pulled.Spawns);
        Assert.True(doc.Undo());
        Assert.Empty(pulled.Spawns);
        Assert.Equal(new MapTileLayer(0, 0), session.Document[0, 0].GetLayer(0));

        Assert.True(doc.Redo());
        Assert.Single(pulled.Spawns);
        Assert.Equal(new MapTileLayer(0, 0), session.Document[2, 2].GetLayer(0));
        Assert.True(doc.Redo());
        Assert.Equal(new MapTileLayer(3, 4), session.Document[2, 2].GetLayer(0));
    }

    [Fact]
    public void AttachSheetSession_DiscardsCompoundEntriesButKeepsEarlierMapEntries()
    {
        MapDocumentViewModel doc = Create4x4ViewModel();
        var oldSheet = new SheetEditSession(Array.Empty<NpcSpawnRow>(), Array.Empty<WarpRow>());
        doc.AttachSheetSession(oldSheet);
        MapEditSession session = doc.Session;
        session.SelectedTileLayer = new MapTileLayer(1, 2);
        session.BeginStroke(MapEditTool.Pencil, 0, 0);
        Assert.True(session.CompleteStroke());

        var window = new MapTileRectangle(0, 0, 3, 3);
        Assert.True(doc.Timeline.ApplyCompound(
            map => map.ApplyResize(window),
            sheet => sheet.ReplaceAll(Array.Empty<NpcSpawnRow>(), new[] { new WarpRow(10, 0, 0, 10, 1, 1) })));
        Assert.Equal(3, session.Document.Width);
        Assert.Single(oldSheet.Warps);

        var fresh = new SheetEditSession(Array.Empty<NpcSpawnRow>(), Array.Empty<WarpRow>());
        doc.AttachSheetSession(fresh);

        // The compound entry is discarded with the old sheet session; only the earlier map edit remains.
        Assert.Empty(fresh.Warps);
        Assert.Single(oldSheet.Warps);
        Assert.True(doc.Timeline.CanUndo);
        Assert.False(doc.Timeline.CanRedo);

        Assert.True(doc.Undo());
        Assert.Equal(4, session.Document.Width);
        Assert.Equal(4, session.Document.Height);
        Assert.Equal(new MapTileLayer(1, 2), session.Document[0, 0].GetLayer(0));
        Assert.Single(oldSheet.Warps);
        Assert.False(doc.Timeline.CanUndo);

        Assert.True(doc.Redo());
        Assert.Equal(3, session.Document.Width);
        Assert.Equal(3, session.Document.Height);
        Assert.Equal(new MapTileLayer(1, 2), session.Document[0, 0].GetLayer(0));
    }

    [Fact]
    public void InitialState_TerrainSelectionIsNullWithEmptyChoicesAndNoAvailability()
    {
        Assert.Null(_viewModel.SelectedTerrainId);
        Assert.Empty(_viewModel.Terrains);
        Assert.Null(_viewModel.TerrainAvailability);
    }

    [Fact]
    public void SelectTerrain_ValidId_StoresSelectionAndActivatesTerrainTool()
    {
        _viewModel.SetTerrain(ValidTerrainCatalog(GrassId, WaterId));

        var raised = RaisedProperties(() => _viewModel.SelectTerrain(GrassId));

        Assert.Equal(GrassId, _viewModel.SelectedTerrainId);
        Assert.Equal(MapEditTool.Terrain, _viewModel.ActiveTool);
        Assert.Equal(
            new[] { nameof(MapDocumentViewModel.SelectedTerrainId), nameof(MapDocumentViewModel.ActiveTool) },
            raised);
    }

    [Fact]
    public void SelectTerrain_SecondSelection_KeepsTerrainToolActive()
    {
        _viewModel.SetTerrain(ValidTerrainCatalog(GrassId, WaterId));
        _viewModel.SelectTerrain(GrassId);

        _viewModel.SelectTerrain(WaterId);

        Assert.Equal(WaterId, _viewModel.SelectedTerrainId);
        Assert.Equal(MapEditTool.Terrain, _viewModel.ActiveTool);
    }

    [Fact]
    public void SelectTerrain_IdAbsentFromIndex_ThrowsWithoutChangingSelectionOrTool()
    {
        _viewModel.SetTerrain(ValidTerrainCatalog(GrassId));
        var raised = new List<string>();
        _viewModel.PropertyChanged += (sender, e) => raised.Add(e.PropertyName ?? string.Empty);

        Assert.Throws<ArgumentException>(() => _viewModel.SelectTerrain(WaterId));

        Assert.Null(_viewModel.SelectedTerrainId);
        Assert.Equal(MapEditTool.Pencil, _viewModel.ActiveTool);
        Assert.Empty(raised);
    }

    [Fact]
    public void SelectTerrain_WithoutTerrain_ThrowsWithoutChangingSelectionOrTool()
    {
        Assert.Throws<ArgumentException>(() => _viewModel.SelectTerrain(GrassId));

        Assert.Null(_viewModel.SelectedTerrainId);
        Assert.Equal(MapEditTool.Pencil, _viewModel.ActiveTool);
    }

    [Fact]
    public void SelectTerrain_IsIndependentPerDocument()
    {
        MapDocumentViewModel other = new(new EditorDocumentController(_dialogs, new MapFileStore(), InitialDocument()), new SharedTileClipboard());
        _viewModel.SetTerrain(ValidTerrainCatalog(GrassId, WaterId));
        other.SetTerrain(ValidTerrainCatalog(GrassId, WaterId));

        _viewModel.SelectTerrain(GrassId);
        other.SelectTerrain(WaterId);

        Assert.Equal(GrassId, _viewModel.SelectedTerrainId);
        Assert.Equal(WaterId, other.SelectedTerrainId);
        Assert.Equal(MapEditTool.Terrain, _viewModel.ActiveTool);
        Assert.Equal(MapEditTool.Terrain, other.ActiveTool);
    }

    [Fact]
    public void ActiveTool_Terrain_WithoutSelection_ThrowsBeforeChangingAnyField()
    {
        _viewModel.SetTerrain(ValidTerrainCatalog(GrassId));
        var raised = new List<string>();
        _viewModel.PropertyChanged += (sender, e) => raised.Add(e.PropertyName ?? string.Empty);

        Assert.Throws<InvalidOperationException>(() => _viewModel.ActiveTool = MapEditTool.Terrain);

        Assert.Equal(MapEditTool.Pencil, _viewModel.ActiveTool);
        Assert.Null(_viewModel.SelectedTerrainId);
        Assert.Empty(raised);
    }

    [Fact]
    public void ActiveTool_Terrain_WithSelection_SetsTool()
    {
        _viewModel.SetTerrain(ValidTerrainCatalog(GrassId));
        _viewModel.SelectTerrain(GrassId);
        _viewModel.ActiveTool = MapEditTool.Eraser;

        var raised = RaisedProperties(() => _viewModel.ActiveTool = MapEditTool.Terrain);

        Assert.Equal(MapEditTool.Terrain, _viewModel.ActiveTool);
        Assert.Equal(new[] { nameof(MapDocumentViewModel.ActiveTool) }, raised);
    }

    [Fact]
    public void ApplyTerrainReconciliation_WithChoices_LeavesSelectionNull()
    {
        _viewModel.SetTerrain(ValidTerrainCatalog(GrassId, WaterId));

        TerrainDocumentReconciliation plan = _viewModel.PrepareTerrainReconciliation(ValidTerrainCatalog(GrassId, WaterId));
        _viewModel.ApplyTerrainReconciliation(plan);

        Assert.Null(_viewModel.SelectedTerrainId);
        Assert.Equal(MapEditTool.Pencil, _viewModel.ActiveTool);
        Assert.Equal(2, _viewModel.Terrains.Count);
        Assert.True(_viewModel.TerrainAvailability!.IsValid);
    }

    [Fact]
    public void ApplyTerrainReconciliation_SelectionSurvivesPublication()
    {
        _viewModel.SetTerrain(ValidTerrainCatalog(GrassId, WaterId));
        _viewModel.SelectTerrain(GrassId);

        TerrainDocumentReconciliation plan = _viewModel.PrepareTerrainReconciliation(ValidTerrainCatalog(GrassId, WaterId));
        _viewModel.ApplyTerrainReconciliation(plan);

        Assert.Equal(GrassId, _viewModel.SelectedTerrainId);
        Assert.Equal(MapEditTool.Terrain, _viewModel.ActiveTool);
    }

    [Fact]
    public void ApplyTerrainReconciliation_RemovedSelectedId_FallsBackOnlyFromTerrain()
    {
        _viewModel.SetTerrain(ValidTerrainCatalog(GrassId, WaterId));
        _viewModel.SelectTerrain(GrassId);
        Assert.Equal(MapEditTool.Terrain, _viewModel.ActiveTool);

        TerrainDocumentReconciliation plan = _viewModel.PrepareTerrainReconciliation(ValidTerrainCatalog(WaterId));
        _viewModel.ApplyTerrainReconciliation(plan);

        Assert.Null(_viewModel.SelectedTerrainId);
        Assert.Equal(MapEditTool.Pencil, _viewModel.ActiveTool);
    }

    [Fact]
    public void ApplyTerrainReconciliation_RemovedSelectedId_PreservesNonTerrainTool()
    {
        _viewModel.SetTerrain(ValidTerrainCatalog(GrassId, WaterId));
        _viewModel.SelectTerrain(GrassId);
        _viewModel.ActiveTool = MapEditTool.Eraser;

        TerrainDocumentReconciliation plan = _viewModel.PrepareTerrainReconciliation(ValidTerrainCatalog(WaterId));
        _viewModel.ApplyTerrainReconciliation(plan);

        Assert.Null(_viewModel.SelectedTerrainId);
        Assert.Equal(MapEditTool.Eraser, _viewModel.ActiveTool);
    }

    [Fact]
    public void ApplyTerrainReconciliation_PreservesBytesHistoryAndDirtyState()
    {
        MapEditSession session = _viewModel.Session;
        session.SelectedTileLayer = new MapTileLayer(3, 4);
        session.BeginStroke(MapEditTool.Pencil, 0, 0);
        Assert.True(session.CompleteStroke());
        _viewModel.Refresh(EditorRefresh.Commands);
        byte[] bytesBefore = MapCodec.Encode(session.Document);
        bool canUndoBefore = _viewModel.CanUndo;
        bool canRedoBefore = _viewModel.CanRedo;
        bool dirtyBefore = _viewModel.IsDirty;

        TerrainDocumentReconciliation plan = _viewModel.PrepareTerrainReconciliation(ValidTerrainCatalog(GrassId, WaterId));
        _viewModel.ApplyTerrainReconciliation(plan);
        _viewModel.NotifyTerrainReconciliation(plan, new PublicationNotificationErrors(8));

        Assert.Equal(bytesBefore, MapCodec.Encode(session.Document));
        Assert.Equal(canUndoBefore, _viewModel.CanUndo);
        Assert.Equal(canRedoBefore, _viewModel.CanRedo);
        Assert.Equal(dirtyBefore, _viewModel.IsDirty);
    }

    [Fact]
    public void NotifyTerrainReconciliation_RaisesExactChangedPropertiesAndInvalidatesCanvasAndPalette()
    {
        var raised = new List<string>();
        int canvas = 0;
        int palette = 0;
        _viewModel.PropertyChanged += (sender, e) => raised.Add(e.PropertyName ?? string.Empty);
        _viewModel.CanvasInvalidated += () => canvas++;
        _viewModel.PaletteInvalidated += () => palette++;
        TerrainDocumentReconciliation plan = _viewModel.PrepareTerrainReconciliation(ValidTerrainCatalog(GrassId, WaterId));
        _viewModel.ApplyTerrainReconciliation(plan);

        _viewModel.NotifyTerrainReconciliation(plan, new PublicationNotificationErrors(8));

        Assert.Equal(
            new[] { nameof(MapDocumentViewModel.TerrainAvailability), nameof(MapDocumentViewModel.Terrains) },
            raised);
        Assert.Equal(1, canvas);
        Assert.Equal(1, palette);
    }

    [Fact]
    public void NotifyTerrainReconciliation_SelectionAndToolFallback_RaiseExactProperties()
    {
        _viewModel.SetTerrain(ValidTerrainCatalog(GrassId, WaterId));
        _viewModel.SelectTerrain(GrassId);
        var raised = new List<string>();
        _viewModel.PropertyChanged += (sender, e) => raised.Add(e.PropertyName ?? string.Empty);
        TerrainDocumentReconciliation plan = _viewModel.PrepareTerrainReconciliation(ValidTerrainCatalog(WaterId));
        _viewModel.ApplyTerrainReconciliation(plan);

        _viewModel.NotifyTerrainReconciliation(plan, new PublicationNotificationErrors(8));

        Assert.Equal(
            new[]
            {
                nameof(MapDocumentViewModel.TerrainAvailability),
                nameof(MapDocumentViewModel.Terrains),
                nameof(MapDocumentViewModel.SelectedTerrainId),
                nameof(MapDocumentViewModel.ActiveTool)
            },
            raised);
    }

    [Fact]
    public void NotifyTerrainReconciliation_NoStateChange_RaisesNoProperties()
    {
        TerrainCatalogLoadResult terrain = ValidTerrainCatalog(GrassId);
        _viewModel.SetTerrain(terrain);
        var raised = new List<string>();
        _viewModel.PropertyChanged += (sender, e) => raised.Add(e.PropertyName ?? string.Empty);
        int canvas = 0;
        _viewModel.CanvasInvalidated += () => canvas++;
        TerrainDocumentReconciliation plan = _viewModel.PrepareTerrainReconciliation(terrain);
        _viewModel.ApplyTerrainReconciliation(plan);

        _viewModel.NotifyTerrainReconciliation(plan, new PublicationNotificationErrors(8));

        Assert.Empty(raised);
        Assert.Equal(1, canvas);
    }

    [Fact]
    public void NotifyTerrainReconciliation_ThrowingPropertyHandler_RecordsFailureAndNotifiesRemainingHandlers()
    {
        _viewModel.SetTerrain(ValidTerrainCatalog(GrassId));
        int remaining = 0;
        _viewModel.PropertyChanged += (_, _) => throw new InvalidOperationException("first handler failure");
        _viewModel.PropertyChanged += (_, _) => remaining++;
        var errors = new PublicationNotificationErrors(4);
        TerrainDocumentReconciliation plan = _viewModel.PrepareTerrainReconciliation(ValidTerrainCatalog(GrassId, WaterId));
        _viewModel.ApplyTerrainReconciliation(plan);

        _viewModel.NotifyTerrainReconciliation(plan, errors);

        Assert.Equal(2, remaining);
        Assert.Equal(1, errors.Count);
        Assert.IsType<InvalidOperationException>(errors[0]);
    }

    [Fact]
    public void ApplyTerrainReconciliation_RemovedGraphics_DoesNotMutateMapCells()
    {
        MapEditSession session = _viewModel.Session;
        session.SelectedTileLayer = new MapTileLayer(5, 5);
        session.BeginStroke(MapEditTool.Pencil, 0, 0);
        Assert.True(session.CompleteStroke());
        _viewModel.Refresh(EditorRefresh.Commands);
        _viewModel.SetTerrain(ValidTerrainCatalog(GrassId, WaterId));
        _viewModel.SelectTerrain(GrassId);
        byte[] bytesBefore = MapCodec.Encode(session.Document);
        bool canUndoBefore = _viewModel.CanUndo;

        var catalog = new TerrainCatalog(
            new[]
            {
                new TerrainDefinition(GrassId, "Grass", null),
                new TerrainDefinition(WaterId, "Water", null)
            },
            new[] { new TerrainGraphicDefinition(new TerrainGraphicReference(1, 10), new TerrainPattern(Center: GrassId)) });
        TerrainCatalogValidationResult validation = TerrainCatalogValidator.Validate(catalog);
        Assert.False(validation.IsValid);
        var removed = TerrainCatalogLoadResult.Invalid(
            "terrain-brushes.json",
            new TerrainFileRevision(true, "removed-graphics"),
            validation.Issues,
            "Terrain validation failed");
        TerrainDocumentReconciliation plan = _viewModel.PrepareTerrainReconciliation(removed);
        _viewModel.ApplyTerrainReconciliation(plan);
        _viewModel.NotifyTerrainReconciliation(plan, new PublicationNotificationErrors(8));

        Assert.Equal(bytesBefore, MapCodec.Encode(session.Document));
        Assert.Equal(canUndoBefore, _viewModel.CanUndo);
        Assert.Null(_viewModel.SelectedTerrainId);
        Assert.Equal(MapEditTool.Pencil, _viewModel.ActiveTool);
        Assert.Empty(_viewModel.Terrains);
        Assert.False(_viewModel.TerrainAvailability!.IsValid);
    }

    [Fact]
    public void ApplyTerrainReconciliation_MalformedTerrain_DisablesOnlyTerrainProperties()
    {
        _viewModel.SetTerrain(ValidTerrainCatalog(GrassId));
        _viewModel.SelectTerrain(GrassId);
        var malformed = TerrainCatalogLoadResult.Invalid(
            "terrain-brushes.json",
            new TerrainFileRevision(true, "malformed"),
            new[] { new TerrainValidationIssue(TerrainValidationSeverity.Error, TerrainValidationCode.MissingSpriteFrame, "boom") },
            "Malformed JSON");

        TerrainDocumentReconciliation plan = _viewModel.PrepareTerrainReconciliation(malformed);
        _viewModel.ApplyTerrainReconciliation(plan);
        _viewModel.NotifyTerrainReconciliation(plan, new PublicationNotificationErrors(8));

        Assert.Null(_viewModel.SelectedTerrainId);
        Assert.Empty(_viewModel.Terrains);
        Assert.False(_viewModel.TerrainAvailability!.IsValid);
        Assert.Equal("Malformed JSON", _viewModel.TerrainAvailability.Diagnostic);
        Assert.Equal(MapEditTool.Pencil, _viewModel.ActiveTool);
        Assert.Throws<InvalidOperationException>(() => _viewModel.ActiveTool = MapEditTool.Terrain);
        _viewModel.ActiveTool = MapEditTool.Eraser;
        Assert.Equal(MapEditTool.Eraser, _viewModel.ActiveTool);
        _viewModel.Brush = new MapTileLayer(9, 9);
        Assert.Equal(new MapTileLayer(9, 9), _viewModel.Brush);
    }
}
