using System;
using System.Linq;
using MapEditor.Core;
using Xunit;

namespace MapEditor.Core.Tests;

public class MapEditStrokeTests
{
    [Fact]
    public void ContinueStroke_InterpolatesSparseHorizontalSamplesWithoutGaps()
    {
        var doc = MapDocument.Create(20, 1);
        var session = new MapEditSession(doc);
        session.SelectedTileLayer = new MapTileLayer(3, 3);

        session.BeginStroke(MapEditTool.Pencil, 0, 0);
        session.ContinueStroke(19, 0);
        Assert.True(session.CompleteStroke());

        for (var x = 0; x < 20; x++)
        {
            Assert.Equal(new MapTileLayer(3, 3), doc[x, 0].GetLayer(0));
        }
    }

    [Fact]
    public void ContinueStroke_InterpolatesShallowAndSteepSegments()
    {
        var doc = MapDocument.Create(10, 10);
        var session = new MapEditSession(doc);
        session.SelectedTileLayer = new MapTileLayer(4, 4);

        session.BeginStroke(MapEditTool.Pencil, 0, 0);
        session.ContinueStroke(5, 2);
        session.ContinueStroke(7, 5);
        Assert.True(session.CompleteStroke());

        var expected = GridLine.Enumerate(new MapCoordinate(0, 0), new MapCoordinate(5, 2))
            .Concat(GridLine.Enumerate(new MapCoordinate(5, 2), new MapCoordinate(7, 5)))
            .Distinct()
            .ToList();
        foreach (var point in expected)
        {
            Assert.Equal(new MapTileLayer(4, 4), doc[point.X, point.Y].GetLayer(0));
        }

        int paintedCount = 0;
        for (var y = 0; y < 10; y++)
        {
            for (var x = 0; x < 10; x++)
            {
                if (doc[x, y].GetLayer(0) == new MapTileLayer(4, 4))
                {
                    paintedCount++;
                }
            }
        }
        Assert.Equal(expected.Count, paintedCount);
    }

    [Fact]
    public void ContinueStroke_MultipleSegmentsAreOneUndoStep()
    {
        var doc = MapDocument.Create(10, 10);
        var session = new MapEditSession(doc);
        session.SelectedTileLayer = new MapTileLayer(6, 6);

        session.BeginStroke(MapEditTool.Pencil, 0, 0);
        session.ContinueStroke(4, 0);
        session.ContinueStroke(4, 4);
        session.ContinueStroke(0, 4);
        Assert.True(session.CompleteStroke());
        Assert.Equal(1, session.History.UndoCount);

        Assert.True(session.Undo());
        for (var y = 0; y <= 4; y++)
        {
            for (var x = 0; x <= 4; x++)
            {
                Assert.Equal(new MapTileLayer(0, 0), doc[x, y].GetLayer(0));
            }
        }
    }

    [Fact]
    public void ContinueStroke_RepeatedPointsAndOverlappingSegmentsChangeEachCellOnce()
    {
        var doc = MapDocument.Create(10, 10);
        var session = new MapEditSession(doc);
        session.SelectedTileLayer = new MapTileLayer(2, 2);

        session.BeginStroke(MapEditTool.Pencil, 0, 0);
        session.ContinueStroke(5, 0);
        session.ContinueStroke(5, 0);
        session.ContinueStroke(2, 0);
        session.ContinueStroke(5, 0);
        Assert.True(session.CompleteStroke());

        Assert.Equal(6, ((MapLayerChangesCommand)session.History.PeekUndo()!).Changes.Count);
        for (var x = 0; x <= 5; x++)
        {
            Assert.Equal(new MapTileLayer(2, 2), doc[x, 0].GetLayer(0));
        }
    }

    [Fact]
    public void InvalidContinuation_DoesNotMutateOrAdvancePreviousSample()
    {
        var doc = MapDocument.Create(10, 10);
        var session = new MapEditSession(doc);
        session.SelectedTileLayer = new MapTileLayer(4, 4);

        session.BeginStroke(MapEditTool.Pencil, 0, 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => session.ContinueStroke(0, 50));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.ContinueStroke(-1, 0));
        session.ContinueStroke(4, 0);
        Assert.True(session.CompleteStroke());

        for (var x = 0; x <= 4; x++)
        {
            Assert.Equal(new MapTileLayer(4, 4), doc[x, 0].GetLayer(0));
        }
        Assert.Equal(new MapTileLayer(0, 0), doc[0, 1].GetLayer(0));
        Assert.Equal(5, ((MapLayerChangesCommand)session.History.PeekUndo()!).Changes.Count);
    }

    [Fact]
    public void CancelStroke_RestoresEveryChangedValueAndKeepsUndoRedo()
    {
        var doc = MapDocument.Create(8, 8);
        var session = new MapEditSession(doc);
        session.SelectedTileLayer = new MapTileLayer(1, 1);

        session.BeginStroke(MapEditTool.Pencil, 0, 0);
        Assert.True(session.CompleteStroke());

        session.SelectedTileLayer = new MapTileLayer(7, 7);
        session.BeginStroke(MapEditTool.Pencil, 1, 0);
        session.ContinueStroke(3, 0);
        session.CancelStroke();

        for (var x = 1; x <= 3; x++)
        {
            Assert.Equal(new MapTileLayer(0, 0), doc[x, 0].GetLayer(0));
        }
        Assert.Equal(new MapTileLayer(1, 1), doc[0, 0].GetLayer(0));
        Assert.True(session.CanUndo);
        Assert.True(session.Undo());
        Assert.Equal(new MapTileLayer(0, 0), doc[0, 0].GetLayer(0));
        Assert.True(session.CanRedo);
        Assert.True(session.Redo());
        Assert.Equal(new MapTileLayer(1, 1), doc[0, 0].GetLayer(0));
    }

    [Fact]
    public void PendingEffectiveStroke_IsDirtyAndCancelRestoresPriorDirtyState()
    {
        var doc = MapDocument.Create(8, 8);
        var session = new MapEditSession(doc);
        session.SelectedTileLayer = new MapTileLayer(1, 1);

        Assert.False(session.IsDirty);
        session.BeginStroke(MapEditTool.Pencil, 0, 0);
        Assert.True(session.IsDirty);
        session.CancelStroke();
        Assert.False(session.IsDirty);

        var dirtySession = new MapEditSession(MapDocument.Create(8, 8), initiallyDirty: true);
        dirtySession.SelectedTileLayer = new MapTileLayer(2, 2);
        dirtySession.BeginStroke(MapEditTool.Pencil, 1, 1);
        Assert.True(dirtySession.IsDirty);
        dirtySession.CancelStroke();
        Assert.True(dirtySession.IsDirty);
    }

    [Fact]
    public void NoOpStroke_DoesNotClearRedo()
    {
        var doc = MapDocument.Create(8, 8);
        var session = new MapEditSession(doc);
        session.SelectedTileLayer = new MapTileLayer(1, 1);

        session.BeginStroke(MapEditTool.Pencil, 0, 0);
        Assert.True(session.CompleteStroke());
        session.BeginStroke(MapEditTool.Pencil, 1, 1);
        Assert.True(session.CompleteStroke());
        Assert.True(session.Undo());
        Assert.True(session.CanRedo);

        session.SelectedTileLayer = new MapTileLayer(0, 0);
        session.BeginStroke(MapEditTool.Pencil, 2, 2);
        Assert.False(session.CompleteStroke());

        Assert.True(session.CanRedo);
        Assert.Equal(1, session.History.RedoCount);
    }

    [Fact]
    public void BeginWhileActiveAndHistoryOperationsWhileActiveThrowWithoutMutation()
    {
        var doc = MapDocument.Create(8, 8);
        var session = new MapEditSession(doc);
        session.SelectedTileLayer = new MapTileLayer(1, 1);

        session.BeginStroke(MapEditTool.Pencil, 0, 0);
        Assert.Throws<InvalidOperationException>(() => session.BeginStroke(MapEditTool.Pencil, 1, 1));
        Assert.Throws<InvalidOperationException>(() => session.Undo());
        Assert.Throws<InvalidOperationException>(() => session.Redo());
        Assert.Throws<InvalidOperationException>(() => session.MarkSaved());
        Assert.False(session.CanUndo);
        Assert.False(session.CanRedo);

        Assert.True(session.CompleteStroke());
        Assert.Equal(new MapTileLayer(1, 1), doc[0, 0].GetLayer(0));
        Assert.Equal(new MapTileLayer(0, 0), doc[1, 1].GetLayer(0));
        Assert.True(session.CanUndo);
    }

    [Fact]
    public void StrokeLifecycleMethodsWithoutActiveStrokeThrow()
    {
        var session = new MapEditSession(MapDocument.Create(8, 8));

        Assert.Throws<InvalidOperationException>(() => session.ContinueStroke(0, 0));
        Assert.Throws<InvalidOperationException>(() => session.CompleteStroke());
        Assert.Throws<InvalidOperationException>(() => session.CancelStroke());
    }
}
