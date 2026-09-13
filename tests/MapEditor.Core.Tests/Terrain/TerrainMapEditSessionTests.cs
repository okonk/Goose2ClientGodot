using System;
using System.Collections.Generic;
using System.Linq;
using MapEditor.Core;
using Xunit;

namespace MapEditor.Core.Tests;

public class TerrainMapEditSessionTests
{
    private static readonly Guid Grass = TerrainCatalogFixture.Grass;

    private static MapTileLayer Tile(int sheet, int graphic) => new(sheet, graphic);

    private static TerrainMapResolver Resolver()
        => new(TerrainCatalogValidator.Validate(TerrainCatalogFixture.Valid()).Index!);

    private sealed class FailingAfterFirstResolver : ITerrainPatchResolver
    {
        private readonly ITerrainPatchResolver _inner;
        private readonly int _failAfterCalls;
        private int _calls;

        public FailingAfterFirstResolver(ITerrainPatchResolver inner, int failAfterCalls = 1)
        {
            _inner = inner;
            _failAfterCalls = failAfterCalls;
        }

        public int Calls => _calls;

        public Guid? GetLogicalCenter(MapTileLayer graphic) => _inner.GetLogicalCenter(graphic);

        public bool TryResolvePatch(
            MapDocument document,
            int layer,
            IReadOnlyDictionary<int, Guid?> centerOverrides,
            IReadOnlyCollection<int> directlyChangedIndices,
            out TerrainResolvedPatch patch,
            out TerrainResolutionFailure? failure)
        {
            _calls++;
            if (_calls > _failAfterCalls)
            {
                patch = new TerrainResolvedPatch(layer, new SortedDictionary<int, MapTileLayer>());
                failure = new TerrainResolutionFailure(null, 0, 0, "Simulated late failure.");
                return false;
            }

            return _inner.TryResolvePatch(document, layer, centerOverrides, directlyChangedIndices, out patch, out failure);
        }
    }

    [Fact]
    public void BeginTerrainStroke_InvalidCoordinate_ThrowsWithoutActiveGesture()
    {
        var session = new MapEditSession(MapDocument.Create(3, 3));

        Assert.Throws<ArgumentOutOfRangeException>(() => session.BeginTerrainStroke(Resolver(), Grass, TerrainEditMode.Paint, -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.BeginTerrainStroke(Resolver(), Grass, TerrainEditMode.Paint, 3, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.BeginTerrainStroke(Resolver(), Grass, TerrainEditMode.Paint, 0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.BeginTerrainStroke(Resolver(), Grass, TerrainEditMode.Paint, 0, 3));
        Assert.False(session.HasActiveStroke);
    }

    [Fact]
    public void BeginTerrainStroke_UndefinedMode_ThrowsWithoutActiveGesture()
    {
        var session = new MapEditSession(MapDocument.Create(3, 3));

        Assert.Throws<ArgumentOutOfRangeException>(() => session.BeginTerrainStroke(Resolver(), Grass, (TerrainEditMode)99, 0, 0));
        Assert.False(session.HasActiveStroke);
        Assert.Null(session.ActiveTerrainStroke);
        Assert.False(session.IsDirty);

        session.SelectedTileLayer = new MapTileLayer(1, 1);
        session.BeginStroke(MapEditTool.Pencil, 0, 0);
        Assert.True(session.CompleteStroke());
    }

    [Fact]
    public void BeginStroke_WithTerrainTool_Throws()
    {
        var session = new MapEditSession(MapDocument.Create(3, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.BeginStroke(MapEditTool.Terrain, 0, 0));
        Assert.False(session.HasActiveStroke);
    }

    [Fact]
    public void BeginTerrainStroke_DuringManualStroke_Throws()
    {
        var session = new MapEditSession(MapDocument.Create(3, 3));
        session.SelectedTileLayer = new MapTileLayer(1, 1);
        session.BeginStroke(MapEditTool.Pencil, 0, 0);

        Assert.Throws<InvalidOperationException>(() => session.BeginTerrainStroke(Resolver(), Grass, TerrainEditMode.Paint, 0, 0));
        Assert.Null(session.ActiveTerrainStroke);
    }

    [Fact]
    public void BeginStroke_DuringTerrainStroke_Throws()
    {
        var session = new MapEditSession(MapDocument.Create(3, 3));
        var result = session.BeginTerrainStroke(Resolver(), Grass, TerrainEditMode.Paint, 0, 0);
        Assert.True(result.IsActive);

        Assert.Throws<InvalidOperationException>(() => session.BeginStroke(MapEditTool.Pencil, 1, 0));
        Assert.True(session.HasActiveStroke);
    }

    [Fact]
    public void BeginTerrainStroke_WithMultiLayerSelection_EditsOnlyTopmostLayer()
    {
        var session = new MapEditSession(MapDocument.Create(3, 3));
        session.SelectedLayers = 0b01001;

        var result = session.BeginTerrainStroke(Resolver(), Grass, TerrainEditMode.Paint, 1, 1);
        Assert.True(result.IsActive);
        Assert.True(session.CompleteStroke());

        Assert.NotEqual(default, session.Document[1, 1].GetLayer(3));
        Assert.Equal(default, session.Document[1, 1].GetLayer(0));
        Assert.Equal(default, session.Document[1, 1].GetLayer(1));
        Assert.Equal(default, session.Document[1, 1].GetLayer(2));
    }

    [Fact]
    public void BeginTerrainStroke_EraseOnUnrecognizedCell_IsActiveNoOpThenContinuesIntoTerrain()
    {
        var session = new MapEditSession(MapDocument.Create(3, 3));
        session.Document.SetLayer(1, 1, 0, Tile(5, 5));
        session.Document.SetLayer(2, 1, 0, Tile(0, 1));

        var result = session.BeginTerrainStroke(Resolver(), Grass, TerrainEditMode.Erase, 1, 1);
        Assert.Equal(new TerrainEditResult(true, false, null), result);
        Assert.True(session.HasActiveStroke);
        Assert.Equal(Tile(5, 5), session.Document[1, 1].GetLayer(0));

        var continued = session.ContinueTerrainStroke(2, 1);
        Assert.Equal(new TerrainEditResult(true, true, null), continued);
        Assert.Equal(default, session.Document[2, 1].GetLayer(0));
        Assert.Equal(Tile(5, 5), session.Document[1, 1].GetLayer(0));

        Assert.True(session.CompleteStroke());
        Assert.Equal(1, session.History.UndoCount);
    }

    [Fact]
    public void BeginTerrainStroke_UnknownTerrainId_LeavesNoActiveGesture()
    {
        var session = new MapEditSession(MapDocument.Create(3, 3));
        var unknown = Guid.NewGuid();

        var result = session.BeginTerrainStroke(Resolver(), unknown, TerrainEditMode.Paint, 1, 1);

        Assert.False(result.IsActive);
        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failure);
        Assert.False(session.HasActiveStroke);
        Assert.All(
            Enumerable.Range(0, 9),
            index => Assert.Equal(default, session.Document.GetTile(index).GetLayer(0)));
        Assert.Equal(0, session.History.UndoCount);
    }

    [Fact]
    public void ContinueTerrainStroke_WithNoActiveTerrainStroke_Throws()
    {
        var session = new MapEditSession(MapDocument.Create(3, 3));
        Assert.Throws<InvalidOperationException>(() => session.ContinueTerrainStroke(0, 0));
    }

    [Fact]
    public void CompleteTerrainStroke_ManyCells_RaisesOneHistoryEvent()
    {
        var session = new MapEditSession(MapDocument.Create(5, 5));
        int events = 0;
        session.HistoryChanged += () => events++;

        var result = session.BeginTerrainStroke(Resolver(), Grass, TerrainEditMode.Paint, 0, 0);
        Assert.True(result.IsActive);
        session.ContinueTerrainStroke(4, 0);
        session.ContinueTerrainStroke(4, 4);

        Assert.True(session.CompleteStroke());
        Assert.Equal(1, events);
        Assert.Equal(1, session.History.UndoCount);
    }

    [Fact]
    public void CancelTerrainStroke_PreservesRedoAndDirtyState()
    {
        var session = new MapEditSession(MapDocument.Create(4, 4));
        session.SelectedTileLayer = new MapTileLayer(1, 1);
        session.BeginStroke(MapEditTool.Pencil, 0, 0);
        Assert.True(session.CompleteStroke());
        session.BeginStroke(MapEditTool.Pencil, 1, 1);
        Assert.True(session.CompleteStroke());
        Assert.True(session.Undo());
        Assert.True(session.CanRedo);

        byte[] tiles = MapCodec.Encode(session.Document);
        long version = session.HistoryVersion;
        int stateId = session.CurrentStateId;
        bool dirty = session.IsDirty;

        var result = session.BeginTerrainStroke(Resolver(), Grass, TerrainEditMode.Paint, 2, 2);
        Assert.True(result.IsActive);
        Assert.True(session.IsDirty);

        session.CancelStroke();

        Assert.False(session.HasActiveStroke);
        Assert.Equal(tiles, MapCodec.Encode(session.Document));
        Assert.Equal(version, session.HistoryVersion);
        Assert.Equal(stateId, session.CurrentStateId);
        Assert.Equal(dirty, session.IsDirty);
        Assert.True(session.CanRedo);
    }

    [Fact]
    public void NoOpTerrainStroke_PreservesRedoAndStateId()
    {
        var session = new MapEditSession(MapDocument.Create(4, 4));
        session.SelectedTileLayer = new MapTileLayer(1, 1);
        session.BeginStroke(MapEditTool.Pencil, 0, 0);
        Assert.True(session.CompleteStroke());
        session.BeginStroke(MapEditTool.Pencil, 1, 1);
        Assert.True(session.CompleteStroke());
        Assert.True(session.Undo());
        Assert.True(session.CanRedo);

        int stateId = session.CurrentStateId;
        long version = session.HistoryVersion;

        var result = session.BeginTerrainStroke(Resolver(), Grass, TerrainEditMode.Erase, 2, 2);
        Assert.True(result.IsActive);
        Assert.False(session.CompleteStroke());

        Assert.False(session.HasActiveStroke);
        Assert.Equal(stateId, session.CurrentStateId);
        Assert.Equal(version, session.HistoryVersion);
        Assert.True(session.CanRedo);
    }

    [Fact]
    public void TerrainFailure_PreservesRedoAndRestoresDocument()
    {
        var session = new MapEditSession(MapDocument.Create(4, 4));
        session.SelectedTileLayer = new MapTileLayer(1, 1);
        session.BeginStroke(MapEditTool.Pencil, 0, 0);
        Assert.True(session.CompleteStroke());
        session.BeginStroke(MapEditTool.Pencil, 1, 1);
        Assert.True(session.CompleteStroke());
        Assert.True(session.Undo());
        Assert.True(session.CanRedo);

        byte[] tiles = MapCodec.Encode(session.Document);
        long version = session.HistoryVersion;
        int stateId = session.CurrentStateId;
        var resolver = new FailingAfterFirstResolver(Resolver());

        var begin = session.BeginTerrainStroke(resolver, Grass, TerrainEditMode.Paint, 2, 2);
        Assert.True(begin.IsActive);
        Assert.True(begin.Changed);

        var result = session.ContinueTerrainStroke(3, 2);

        Assert.False(result.IsActive);
        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failure);
        Assert.False(session.HasActiveStroke);
        Assert.Equal(tiles, MapCodec.Encode(session.Document));
        Assert.Equal(version, session.HistoryVersion);
        Assert.Equal(stateId, session.CurrentStateId);
        Assert.True(session.CanRedo);
        Assert.Equal(2, resolver.Calls);
    }

    [Fact]
    public void TerrainPreview_MakesDocumentDirtyAndCancelRestoresClean()
    {
        var session = new MapEditSession(MapDocument.Create(4, 4));
        Assert.False(session.IsDirty);

        var result = session.BeginTerrainStroke(Resolver(), Grass, TerrainEditMode.Paint, 0, 0);
        Assert.True(result.IsActive);
        Assert.True(session.IsDirty);

        session.CancelStroke();
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void TerrainEdit_UndoRedoTransitionsAroundSavepoint()
    {
        var session = new MapEditSession(MapDocument.Create(4, 4));
        Assert.False(session.IsDirty);

        var result = session.BeginTerrainStroke(Resolver(), Grass, TerrainEditMode.Paint, 0, 0);
        Assert.True(result.IsActive);
        Assert.True(session.CompleteStroke());
        Assert.True(session.IsDirty);

        session.MarkSaved();
        Assert.False(session.IsDirty);

        Assert.True(session.Undo());
        Assert.True(session.IsDirty);
        Assert.All(
            Enumerable.Range(0, 16),
            index => Assert.Equal(default, session.Document.GetTile(index).GetLayer(0)));

        Assert.True(session.Redo());
        Assert.False(session.IsDirty);
        Assert.NotEqual(default, session.Document[0, 0].GetLayer(0));
    }

    [Fact]
    public void TerrainCommit_RespectsRetainedHistoryCap()
    {
        var session = new MapEditSession(MapDocument.Create(4, 4), retainedHistoryCapBytes: 224);

        var first = session.BeginTerrainStroke(Resolver(), Grass, TerrainEditMode.Paint, 0, 0);
        Assert.True(first.IsActive);
        Assert.True(session.CompleteStroke());
        Assert.Equal(1, session.History.UndoCount);

        var second = session.BeginTerrainStroke(Resolver(), Grass, TerrainEditMode.Paint, 1, 1);
        Assert.True(second.IsActive);
        Assert.True(session.CompleteStroke());
        Assert.Equal(1, session.History.UndoCount);
        Assert.Equal(224, session.RetainedHistoryUsedBytes);
    }

    [Fact]
    public void TerrainCommit_UndoRestoresExactAndRedoRestoresExactVariant()
    {
        var session = new MapEditSession(MapDocument.Create(5, 5));
        var result = session.BeginTerrainStroke(Resolver(), Grass, TerrainEditMode.Paint, 0, 0);
        Assert.True(result.IsActive);
        session.ContinueTerrainStroke(4, 0);
        Assert.True(session.CompleteStroke());

        byte[] after = MapCodec.Encode(session.Document);
        Assert.NotEqual(default, session.Document[0, 0].GetLayer(0));

        Assert.True(session.Undo());
        Assert.All(
            Enumerable.Range(0, 25),
            index => Assert.Equal(default, session.Document.GetTile(index).GetLayer(0)));

        Assert.True(session.Redo());
        Assert.Equal(after, MapCodec.Encode(session.Document));
        Assert.False(session.CanRedo);
    }

    [Fact]
    public void DiscardRedo_WithActiveTerrainStroke_ThrowsWithoutChangingHistory()
    {
        var session = new MapEditSession(MapDocument.Create(4, 4));
        session.SelectedTileLayer = new MapTileLayer(1, 1);
        session.BeginStroke(MapEditTool.Pencil, 0, 0);
        Assert.True(session.CompleteStroke());
        session.BeginStroke(MapEditTool.Pencil, 1, 1);
        Assert.True(session.CompleteStroke());
        Assert.True(session.Undo());
        Assert.True(session.CanRedo);

        long version = session.HistoryVersion;
        var result = session.BeginTerrainStroke(Resolver(), Grass, TerrainEditMode.Paint, 2, 2);
        Assert.True(result.IsActive);

        Assert.Throws<InvalidOperationException>(() => session.DiscardRedo());
        Assert.Equal(version, session.HistoryVersion);
        Assert.True(session.History.RedoCount > 0);
    }

    [Fact]
    public void ClearHistory_WithActiveTerrainStroke_ThrowsWithoutChangingHistory()
    {
        var session = new MapEditSession(MapDocument.Create(4, 4));
        session.SelectedTileLayer = new MapTileLayer(1, 1);
        session.BeginStroke(MapEditTool.Pencil, 0, 0);
        Assert.True(session.CompleteStroke());
        session.BeginStroke(MapEditTool.Pencil, 1, 1);
        Assert.True(session.CompleteStroke());
        Assert.True(session.Undo());
        Assert.True(session.CanUndo);

        long version = session.HistoryVersion;
        var result = session.BeginTerrainStroke(Resolver(), Grass, TerrainEditMode.Paint, 2, 2);
        Assert.True(result.IsActive);

        Assert.Throws<InvalidOperationException>(() => session.ClearHistory());
        Assert.Equal(version, session.HistoryVersion);
        Assert.True(session.History.RedoCount > 0);
    }

    [Fact]
    public void MutationApis_WithActiveTerrainStroke_Throw()
    {
        var session = new MapEditSession(MapDocument.Create(5, 5));
        var result = session.BeginTerrainStroke(Resolver(), Grass, TerrainEditMode.Paint, 0, 0);
        Assert.True(result.IsActive);

        Assert.Throws<InvalidOperationException>(() => session.ApplyFloodFill(1, 1));
        MapTileLayer[]?[] patch = new MapTileLayer[MapDocument.LayerCount][];
        patch[0] = new MapTileLayer[] { new(1, 1) };
        Assert.Throws<InvalidOperationException>(() => session.ApplyLayerPatch(0, 0, 1, 1, patch));
        Assert.Throws<InvalidOperationException>(() => session.ApplyBlockedPatch(new MapTileRectangle(0, 0, 1, 1), blocked: true));
        Assert.Throws<InvalidOperationException>(() => session.ApplyResize(new MapTileRectangle(0, 0, 2, 2)));
        Assert.Throws<InvalidOperationException>(() => session.Undo());
        Assert.Throws<InvalidOperationException>(() => session.Redo());
        Assert.Throws<InvalidOperationException>(() => session.MarkSaved());
        Assert.Throws<InvalidOperationException>(() => session.SetRetainedHistoryCap(1024));

        Assert.False(session.CanUndo);
        Assert.False(session.CanRedo);
    }
}
