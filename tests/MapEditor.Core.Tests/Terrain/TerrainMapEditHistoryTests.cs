using System.Linq;
using MapEditor.Core;
using MapEditor.Core.Terrain;
using Xunit;

namespace MapEditor.Core.Tests.Terrain;

public class TerrainMapEditHistoryTests
{
    [Fact]
    public void TerrainComplete_CreatesExactlyOneOrdinaryLayerCommand()
    {
        var (resolver, set) = TerrainMapEditSessionTests.CreateResolver();
        var session = new MapEditSession(MapDocument.Create(4, 1));
        session.BeginTerrainStroke(resolver, set.Id, TerrainEditMode.Paint, 0, 0);
        session.ContinueTerrainStroke(3, 0);

        Assert.True(session.CompleteStroke());
        Assert.Equal(1, session.History.UndoCount);
        Assert.IsType<MapLayerChangesCommand>(session.History.PeekUndo());
        Assert.Equal(1, session.HistoryVersion);
    }

    [Fact]
    public void TerrainUndo_RestoresSelectedDisplacedAndRawOriginalValues()
    {
        var (resolver, set) = TerrainMapEditSessionTests.CreateResolver();
        var document = MapDocument.Create(3, 1);
        document.SetLayer(2, 0, 0, new MapTileLayer(99, 99));
        var before = MapCodec.Encode(document);
        var session = new MapEditSession(document);
        session.BeginTerrainStroke(resolver, set.Id, TerrainEditMode.Paint, 0, 0);
        session.ContinueTerrainStroke(2, 0);
        session.CompleteStroke();

        Assert.True(session.Undo());
        Assert.Equal(before, MapCodec.Encode(document));
    }

    [Fact]
    public void TerrainRedo_ReplaysExactPreviouslyChosenFinalVariants()
    {
        var (resolver, set) = TerrainMapEditSessionTests.CreateResolver();
        var session = new MapEditSession(MapDocument.Create(4, 1));
        session.BeginTerrainStroke(resolver, set.Id, TerrainEditMode.Paint, 0, 0);
        session.ContinueTerrainStroke(3, 0);
        session.CompleteStroke();
        var after = MapCodec.Encode(session.Document);
        session.Undo();

        Assert.True(session.Redo());
        Assert.Equal(after, MapCodec.Encode(session.Document));
    }

    [Fact]
    public void TerrainCommand_SavepointDirtyTransitionsMatchOrdinaryLayerCommands()
    {
        var (resolver, set) = TerrainMapEditSessionTests.CreateResolver();
        var session = new MapEditSession(MapDocument.Create(2, 1));
        session.BeginTerrainStroke(resolver, set.Id, TerrainEditMode.Paint, 0, 0);
        Assert.True(session.IsDirty);
        session.CompleteStroke();
        session.MarkSaved();
        Assert.False(session.IsDirty);
        session.Undo();
        Assert.True(session.IsDirty);
        session.Redo();
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void TerrainCommand_HistoryVersionAndEventAdvanceOnceOnCompleteUndoRedo()
    {
        var (resolver, set) = TerrainMapEditSessionTests.CreateResolver();
        var session = new MapEditSession(MapDocument.Create(2, 1));
        var events = 0;
        session.HistoryChanged += () => events++;
        session.BeginTerrainStroke(resolver, set.Id, TerrainEditMode.Paint, 0, 0);
        Assert.Equal(0, session.HistoryVersion);
        session.CompleteStroke();
        session.Undo();
        session.Redo();
        Assert.Equal(3, session.HistoryVersion);
        Assert.Equal(3, events);
    }

    [Fact]
    public void TerrainCommand_RepeatedNeighborPreviewsStoreOneRowMajorDeltaPerCoordinate()
    {
        var (resolver, set) = TerrainMapEditSessionTests.CreateResolver();
        var session = new MapEditSession(MapDocument.Create(3, 3));
        session.BeginTerrainStroke(resolver, set.Id, TerrainEditMode.Paint, 0, 1);
        session.ContinueTerrainStroke(2, 1);
        session.CompleteStroke();
        var changes = ((MapLayerChangesCommand)session.History.PeekUndo()!).Changes;

        Assert.Equal(changes.Count, Enumerable.Range(0, changes.Count).Select(i => (changes[i].X, changes[i].Y)).Distinct().Count());
        Assert.Equal(Enumerable.Range(0, changes.Count).Select(i => (changes[i].Y, changes[i].X)).OrderBy(p => p.Y).ThenBy(p => p.X), Enumerable.Range(0, changes.Count).Select(i => (changes[i].Y, changes[i].X)));
    }

    [Fact]
    public void TerrainCommand_AccountingUsesFinalCoalescedBufferCapacity()
    {
        var (resolver, set) = TerrainMapEditSessionTests.CreateResolver();
        var session = new MapEditSession(MapDocument.Create(5, 1));
        session.BeginTerrainStroke(resolver, set.Id, TerrainEditMode.Paint, 0, 0);
        session.ContinueTerrainStroke(4, 0);
        session.CompleteStroke();
        var buffer = ((MapLayerChangesCommand)session.History.PeekUndo()!).Changes;
        Assert.Equal(MapEditCommand.BaseCommandBytes + buffer.SegmentCount * MapEditCommand.SegmentBytes + buffer.AllocatedSlotCount * MapEditCommand.LayerSlotBytes, session.RetainedHistoryUsedBytes);
    }

    [Fact]
    public void TerrainCommand_OverCapRemainsAppliedDirtyAndNotUndoable()
    {
        var (resolver, set) = TerrainMapEditSessionTests.CreateResolver();
        var session = new MapEditSession(MapDocument.Create(2, 1), retainedHistoryCapBytes: 0);
        session.BeginTerrainStroke(resolver, set.Id, TerrainEditMode.Paint, 0, 0);
        session.CompleteStroke();
        Assert.NotEqual(default, session.Document[0, 0].GetLayer(0));
        Assert.True(session.IsDirty);
        Assert.False(session.CanUndo);
        Assert.Equal(0, session.RetainedHistoryUsedBytes);
    }

    [Fact]
    public void FailedCanceledAndNetNoOpTerrainStrokesPreserveRedoBytesVersionAndStateIds()
    {
        var (resolver, set) = TerrainMapEditSessionTests.CreateResolver();
        var session = new MapEditSession(MapDocument.Create(2, 1));
        session.SelectedTileLayer = new MapTileLayer(9, 9);
        session.BeginStroke(MapEditTool.Pencil, 1, 0);
        session.CompleteStroke();
        session.Undo();
        var state = (session.HistoryVersion, session.RetainedHistoryUsedBytes, session.CurrentStateId, session.SavedStateId);
        session.BeginTerrainStroke(resolver, set.Id, TerrainEditMode.Paint, 0, 0);
        session.CancelStroke();
        Assert.Equal(state, (session.HistoryVersion, session.RetainedHistoryUsedBytes, session.CurrentStateId, session.SavedStateId));
        Assert.True(session.CanRedo);

        session.Document.SetLayer(0, 0, 0, new MapTileLayer(7, 1));
        var update = session.BeginTerrainStroke(resolver, set.Id, TerrainEditMode.Paint, 0, 0);
        Assert.False(update.Changed);
        Assert.False(session.CompleteStroke());
        Assert.Equal(state, (session.HistoryVersion, session.RetainedHistoryUsedBytes, session.CurrentStateId, session.SavedStateId));
        Assert.True(session.CanRedo);
    }
}
