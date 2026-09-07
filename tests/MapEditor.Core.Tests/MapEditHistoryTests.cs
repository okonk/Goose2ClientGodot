using System;
using System.Collections.Generic;
using MapEditor.Core;
using Xunit;

namespace MapEditor.Core.Tests;

public class MapEditHistoryTests
{
    private static void PaintLayerCell(MapEditSession session, int x, int y)
    {
        session.SelectedTileLayer = new MapTileLayer(1, 2);
        session.BeginStroke(MapEditTool.Pencil, x, y);
        Assert.True(session.CompleteStroke());
    }

    private static void BlockCell(MapEditSession session, int x, int y)
    {
        Assert.True(session.ApplyBlockedPatch(new MapTileRectangle(x, y, 1, 1), blocked: true));
    }

    private static void PaintLine(MapEditSession session, int cells)
    {
        session.SelectedTileLayer = new MapTileLayer(1, 2);
        session.BeginStroke(MapEditTool.Pencil, 0, 0);
        for (var x = 1; x < cells; x++)
        {
            session.ContinueStroke(x, 0);
        }

        Assert.True(session.CompleteStroke());
    }

    [Fact]
    public void Constructor_Uses64MiBDefaultCap()
    {
        Assert.Equal(64 * 1024 * 1024, MapEditSession.DefaultRetainedHistoryCapBytes);

        var session = new MapEditSession(MapDocument.Create(4, 4));

        Assert.Equal(64 * 1024 * 1024, session.RetainedHistoryCapBytes);
        Assert.Equal(0, session.RetainedHistoryUsedBytes);
    }

    [Fact]
    public void Constructor_RejectsNegativeCap()
    {
        var document = MapDocument.Create(4, 4);

        Assert.Throws<ArgumentOutOfRangeException>(() => new MapEditSession(document, retainedHistoryCapBytes: -1));
        Assert.Equal(new MapTileLayer(0, 0), document[0, 0].GetLayer(0));
    }

    [Fact]
    public void OneLayerCommand_AccountsBaseSegmentAndFourReservedSlots()
    {
        var session = new MapEditSession(MapDocument.Create(4, 4));

        PaintLayerCell(session, 0, 0);

        Assert.Equal(64 + 32 + 4 * 32, session.RetainedHistoryUsedBytes);
    }

    [Fact]
    public void OneFlagsCommand_AccountsBaseSegmentAndFourReservedSlots()
    {
        var session = new MapEditSession(MapDocument.Create(4, 4));

        BlockCell(session, 0, 0);
        Assert.Equal(64 + 32 + 4 * 24, session.RetainedHistoryUsedBytes);
    }

    [Fact]
    public void MultiCellCommand_AccountsBaseSegmentsAndReservedCapacity()
    {
        var layerSession = new MapEditSession(MapDocument.Create(10, 10));
        PaintLine(layerSession, 5);
        Assert.Equal(64 + 2 * 32 + 12 * 32, layerSession.RetainedHistoryUsedBytes);

        var flagsSession = new MapEditSession(MapDocument.Create(10, 10));
        Assert.True(flagsSession.ApplyBlockedPatch(new MapTileRectangle(0, 0, 5, 1), blocked: true));
        Assert.Equal(64 + 2 * 32 + 12 * 24, flagsSession.RetainedHistoryUsedBytes);
    }

    [Fact]
    public void SegmentBoundaryAccounting_ChangesOnlyWhenAReservedSegmentIsAdded()
    {
        var one = new MapEditSession(MapDocument.Create(10, 10));
        PaintLayerCell(one, 0, 0);
        var four = new MapEditSession(MapDocument.Create(10, 10));
        PaintLine(four, 4);

        Assert.Equal(64 + 32 + 4 * 32, one.RetainedHistoryUsedBytes);
        Assert.Equal(one.RetainedHistoryUsedBytes, four.RetainedHistoryUsedBytes);

        var five = new MapEditSession(MapDocument.Create(10, 10));
        PaintLine(five, 5);
        Assert.Equal(64 + 2 * 32 + 12 * 32, five.RetainedHistoryUsedBytes);
    }

    [Fact]
    public void Cap_ExactlyCommandSizeRetainsCommand()
    {
        var session = new MapEditSession(MapDocument.Create(4, 4), retainedHistoryCapBytes: 224);

        PaintLayerCell(session, 0, 0);

        Assert.True(session.CanUndo);
        Assert.Equal(224, session.RetainedHistoryUsedBytes);
    }

    [Fact]
    public void Cap_OneByteBelowCommandSizeDropsOversizedCommandButKeepsEditDirty()
    {
        var document = MapDocument.Create(4, 4);
        var session = new MapEditSession(document, retainedHistoryCapBytes: 223);

        PaintLayerCell(session, 0, 0);

        Assert.Equal(new MapTileLayer(1, 2), document[0, 0].GetLayer(0));
        Assert.False(session.CanUndo);
        Assert.True(session.IsDirty);
        Assert.Equal(0, session.RetainedHistoryUsedBytes);
    }

    [Fact]
    public void Cap_ZeroRetainsNoUndoOrRedo()
    {
        var session = new MapEditSession(MapDocument.Create(4, 4), retainedHistoryCapBytes: 0);

        PaintLayerCell(session, 0, 0);

        Assert.False(session.CanUndo);
        Assert.False(session.CanRedo);
        Assert.Equal(0, session.RetainedHistoryUsedBytes);
    }

    [Fact]
    public void CompletingNewCommand_ClearsRedoBeforeCapEviction()
    {
        var session = new MapEditSession(MapDocument.Create(8, 8), retainedHistoryCapBytes: 224);
        PaintLayerCell(session, 0, 0);
        Assert.True(session.Undo());
        Assert.True(session.CanRedo);

        PaintLayerCell(session, 1, 1);

        Assert.False(session.CanRedo);
        Assert.True(session.CanUndo);
        Assert.Equal(1, session.History.UndoCount);
        Assert.Equal(224, session.RetainedHistoryUsedBytes);
    }

    [Fact]
    public void OldestUndoCommandsAreEvictedAndNearestUndoRemains()
    {
        var session = new MapEditSession(MapDocument.Create(8, 8), retainedHistoryCapBytes: 224);
        PaintLayerCell(session, 0, 0);
        PaintLayerCell(session, 1, 1);
        PaintLayerCell(session, 2, 2);

        Assert.Equal(1, session.History.UndoCount);
        Assert.Equal(224, session.RetainedHistoryUsedBytes);

        Assert.True(session.Undo());
        Assert.Equal(new MapTileLayer(1, 2), session.Document[0, 0].GetLayer(0));
        Assert.Equal(new MapTileLayer(1, 2), session.Document[1, 1].GetLayer(0));
        Assert.Equal(new MapTileLayer(0, 0), session.Document[2, 2].GetLayer(0));
    }

    [Fact]
    public void UndoAndRedo_MoveCommandWithoutChangingUsage()
    {
        var session = new MapEditSession(MapDocument.Create(4, 4), retainedHistoryCapBytes: 1024);
        PaintLayerCell(session, 0, 0);
        Assert.Equal(224, session.RetainedHistoryUsedBytes);

        Assert.True(session.Undo());
        Assert.Equal(224, session.RetainedHistoryUsedBytes);

        Assert.True(session.Redo());
        Assert.Equal(224, session.RetainedHistoryUsedBytes);
        Assert.Equal(1, session.History.UndoCount);
        Assert.Equal(0, session.History.RedoCount);
    }

    [Fact]
    public void ReduceCap_EvictsOldestUndoThenFarthestRedo()
    {
        var session = new MapEditSession(MapDocument.Create(8, 8), retainedHistoryCapBytes: 672);
        PaintLayerCell(session, 0, 0);
        PaintLayerCell(session, 1, 1);
        PaintLayerCell(session, 2, 2);
        Assert.True(session.Undo());
        Assert.True(session.Undo());
        Assert.Equal(1, session.History.UndoCount);
        Assert.Equal(2, session.History.RedoCount);

        session.SetRetainedHistoryCap(448);
        Assert.Equal(0, session.History.UndoCount);
        Assert.Equal(2, session.History.RedoCount);
        Assert.Equal(448, session.RetainedHistoryUsedBytes);

        session.SetRetainedHistoryCap(224);
        Assert.Equal(0, session.History.UndoCount);
        Assert.Equal(1, session.History.RedoCount);
        Assert.Equal(224, session.RetainedHistoryUsedBytes);

        Assert.True(session.Redo());
        Assert.Equal(0, session.History.RedoCount);
        Assert.Equal(224, session.RetainedHistoryUsedBytes);

        session.SetRetainedHistoryCap(0);
        Assert.Equal(0, session.History.UndoCount);
        Assert.Equal(0, session.History.RedoCount);
        Assert.Equal(0, session.RetainedHistoryUsedBytes);
    }

    [Fact]
    public void IncreaseCap_DoesNotRestoreEvictedCommands()
    {
        var session = new MapEditSession(MapDocument.Create(8, 8), retainedHistoryCapBytes: 224);
        PaintLayerCell(session, 0, 0);
        PaintLayerCell(session, 1, 1);
        Assert.Equal(1, session.History.UndoCount);

        session.SetRetainedHistoryCap(448);

        Assert.Equal(448, session.RetainedHistoryCapBytes);
        Assert.Equal(1, session.History.UndoCount);
        Assert.Equal(224, session.RetainedHistoryUsedBytes);
    }

    [Fact]
    public void SetNegativeCap_ThrowsWithoutChangingCapUsageStacksDocumentOrDirty()
    {
        var document = MapDocument.Create(8, 8);
        var session = new MapEditSession(document, retainedHistoryCapBytes: 448);
        PaintLayerCell(session, 0, 0);
        PaintLayerCell(session, 1, 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => session.SetRetainedHistoryCap(-1));

        Assert.Equal(448, session.RetainedHistoryCapBytes);
        Assert.Equal(448, session.RetainedHistoryUsedBytes);
        Assert.Equal(2, session.History.UndoCount);
        Assert.True(session.IsDirty);
        Assert.Equal(new MapTileLayer(1, 2), document[0, 0].GetLayer(0));
        Assert.Equal(new MapTileLayer(1, 2), document[1, 1].GetLayer(0));
    }

    [Fact]
    public void NoOpEyedropperAndCanceledStroke_ConsumeNoRetainedHistoryBytes()
    {
        var session = new MapEditSession(MapDocument.Create(4, 4));
        session.SelectedTileLayer = new MapTileLayer(0, 0);
        session.BeginStroke(MapEditTool.Pencil, 0, 0);
        Assert.False(session.CompleteStroke());
        Assert.Equal(0, session.RetainedHistoryUsedBytes);

        session.BeginStroke(MapEditTool.Eyedropper, 0, 0);
        Assert.False(session.CompleteStroke());
        Assert.Equal(0, session.RetainedHistoryUsedBytes);

        session.SelectedTileLayer = new MapTileLayer(1, 2);
        session.BeginStroke(MapEditTool.Pencil, 0, 0);
        session.CancelStroke();
        Assert.Equal(0, session.RetainedHistoryUsedBytes);
        Assert.False(session.IsDirty);
        Assert.Equal(new MapTileLayer(0, 0), session.Document[0, 0].GetLayer(0));
    }

    [Fact]
    public void ActiveStrokeBitmapAndDeltas_AreExcludedFromRetainedHistoryUsage()
    {
        var session = new MapEditSession(MapDocument.Create(10, 10));
        session.SelectedTileLayer = new MapTileLayer(1, 2);
        session.BeginStroke(MapEditTool.Pencil, 0, 0);
        for (var x = 1; x < 6; x++)
        {
            session.ContinueStroke(x, 0);
        }

        Assert.True(session.HasActiveStroke);
        Assert.Equal(0, session.RetainedHistoryUsedBytes);

        Assert.True(session.CompleteStroke());
        Assert.Equal(64 + 2 * 32 + 12 * 32, session.RetainedHistoryUsedBytes);
    }

    [Fact]
    public void UsageNeverExceedsCapAcrossMixedLayerAndFlagsCommands()
    {
        long[] caps = { 0, 1, 191, 192, 193, 223, 224, 225, 300, 448, 512, 640, 1000 };
        foreach (var cap in caps)
        {
            var session = new MapEditSession(MapDocument.Create(16, 16), retainedHistoryCapBytes: cap);
            for (var i = 0; i < 16; i++)
            {
                if (i % 2 == 0)
                {
                    BlockCell(session, i, 0);
                }
                else
                {
                    PaintLayerCell(session, i, 1);
                }

                Assert.True(session.RetainedHistoryUsedBytes <= cap);
                if (i % 4 == 3 && session.CanUndo)
                {
                    session.Undo();
                    Assert.True(session.RetainedHistoryUsedBytes <= cap);
                }
            }
        }
    }

    [Fact]
    public void EvictingHistoryWhileCurrentlySaved_RemainsClean()
    {
        var session = new MapEditSession(MapDocument.Create(8, 8));
        PaintLayerCell(session, 0, 0);
        Assert.True(session.Undo());
        PaintLayerCell(session, 1, 1);
        Assert.True(session.Undo());
        session.MarkSaved();
        Assert.False(session.IsDirty);

        session.SetRetainedHistoryCap(0);

        Assert.False(session.IsDirty);
        Assert.False(session.CanUndo);
        Assert.False(session.CanRedo);
        Assert.Equal(0, session.RetainedHistoryUsedBytes);
        Assert.Equal(0, session.CurrentStateId);
        Assert.Equal(0, session.SavedStateId);
    }

    [Fact]
    public void EvictingCommandThatCouldReachSavedState_RemainsDirty()
    {
        var session = new MapEditSession(MapDocument.Create(8, 8));
        PaintLayerCell(session, 0, 0);
        session.MarkSaved();
        Assert.False(session.IsDirty);
        Assert.True(session.Undo());
        Assert.True(session.IsDirty);

        session.SetRetainedHistoryCap(0);

        Assert.True(session.IsDirty);
        Assert.False(session.CanUndo);
        Assert.Equal(1, session.SavedStateId);
        Assert.Equal(0, session.CurrentStateId);
    }

    [Fact]
    public void OversizedEditAfterSavedState_IsDirtyAndNotUndoable()
    {
        var document = MapDocument.Create(4, 4);
        var session = new MapEditSession(document, retainedHistoryCapBytes: 223);
        PaintLayerCell(session, 0, 0);
        session.MarkSaved();

        PaintLayerCell(session, 1, 1);

        Assert.True(session.IsDirty);
        Assert.False(session.CanUndo);
        Assert.Equal(0, session.RetainedHistoryUsedBytes);
        Assert.Equal(new MapTileLayer(1, 2), document[1, 1].GetLayer(0));
        Assert.Equal(2, session.CurrentStateId);
    }

    [Fact]
    public void ReducingCapDoesNotChangeCurrentDocumentOrStateIdentity()
    {
        var document = MapDocument.Create(8, 8);
        var session = new MapEditSession(document, retainedHistoryCapBytes: 1024);
        PaintLayerCell(session, 0, 0);
        PaintLayerCell(session, 1, 1);
        Assert.Equal(2, session.CurrentStateId);
        Assert.Equal(0, session.SavedStateId);

        session.SetRetainedHistoryCap(100);

        Assert.Equal(2, session.CurrentStateId);
        Assert.Equal(0, session.SavedStateId);
        Assert.Equal(new MapTileLayer(1, 2), document[0, 0].GetLayer(0));
        Assert.Equal(new MapTileLayer(1, 2), document[1, 1].GetLayer(0));
        Assert.Equal(0, session.RetainedHistoryUsedBytes);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void SetCapWhileStrokeActive_ThrowsWithoutEviction()
    {
        var session = new MapEditSession(MapDocument.Create(8, 8), retainedHistoryCapBytes: 224);
        PaintLayerCell(session, 0, 0);
        Assert.Equal(224, session.RetainedHistoryUsedBytes);
        session.SelectedTileLayer = new MapTileLayer(3, 4);
        session.BeginStroke(MapEditTool.Pencil, 1, 1);

        Assert.Throws<InvalidOperationException>(() => session.SetRetainedHistoryCap(0));

        Assert.Equal(224, session.RetainedHistoryCapBytes);
        Assert.Equal(224, session.RetainedHistoryUsedBytes);
        Assert.Equal(1, session.History.UndoCount);

        Assert.True(session.CompleteStroke());
        Assert.Equal(224, session.RetainedHistoryCapBytes);
        Assert.Equal(224, session.RetainedHistoryUsedBytes);
        Assert.Equal(1, session.History.UndoCount);
    }

    [Fact]
    public void HistoryVersionAndHistoryChanged_AdvanceExactlyOncePerEffectiveCommandUndoRedoClear()
    {
        var session = new MapEditSession(MapDocument.Create(4, 4));
        var events = new List<long>();
        session.HistoryChanged += () => events.Add(session.HistoryVersion);

        Assert.Equal(0, session.HistoryVersion);
        Assert.Empty(events);

        PaintLayerCell(session, 0, 0);
        Assert.Equal(1, session.HistoryVersion);

        Assert.True(session.Undo());
        Assert.Equal(2, session.HistoryVersion);

        Assert.True(session.Redo());
        Assert.Equal(3, session.HistoryVersion);

        session.SelectedTileLayer = new MapTileLayer(1, 2);
        session.BeginStroke(MapEditTool.Pencil, 0, 0);
        Assert.False(session.CompleteStroke());
        Assert.Equal(3, session.HistoryVersion);

        session.DiscardRedo();
        Assert.Equal(3, session.HistoryVersion);

        session.ClearHistory();
        Assert.Equal(4, session.HistoryVersion);
        Assert.False(session.CanUndo);
        Assert.False(session.CanRedo);

        session.ClearHistory();
        Assert.Equal(4, session.HistoryVersion);

        Assert.Equal(new long[] { 1, 2, 3, 4 }, events);
    }

    [Fact]
    public void HistoryVersion_KeepsAdvancingWhenCapEvictsThePushedCommand()
    {
        var session = new MapEditSession(MapDocument.Create(4, 4), retainedHistoryCapBytes: 223);
        int events = 0;
        session.HistoryChanged += () => events++;

        PaintLayerCell(session, 0, 0);

        Assert.Equal(1, session.HistoryVersion);
        Assert.Equal(1, events);
        Assert.False(session.CanUndo);
        Assert.True(session.IsDirty);
    }
}
