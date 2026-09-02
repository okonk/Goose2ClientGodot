using MapEditor.Core;
using Xunit;

namespace MapEditor.Core.Tests;

public class MapEditStrokeStorageTests
{
    [Fact]
    public void EditingStroke_VisitedBitmapUsesExactlyCeilingTileCountOver64Words()
    {
        var doc = MapDocument.Create(100, 100);
        var session = new MapEditSession(doc);
        session.SelectedTileLayer = new MapTileLayer(1, 1);

        session.BeginStroke(MapEditTool.Pencil, 0, 0);

        Assert.Equal(157, session.ActiveStroke!.Visited!.WordCount);
        session.CancelStroke();
    }

    [Fact]
    public void Eyedropper_AllocatesNoVisitedBitmapOrDeltaBuffer()
    {
        var doc = MapDocument.Create(100, 100);
        var session = new MapEditSession(doc);

        session.BeginStroke(MapEditTool.Eyedropper, 0, 0);

        var stroke = session.ActiveStroke!;
        Assert.Null(stroke.Visited);
        Assert.Null(stroke.LayerChanges);
        Assert.Null(stroke.FlagsChanges);
        session.CancelStroke();
    }

    [Fact]
    public void NoOpTile_IsMarkedVisitedButAddsNoDelta()
    {
        var doc = MapDocument.Create(5, 5);
        var session = new MapEditSession(doc);
        session.SelectedTileLayer = new MapTileLayer(0, 0);

        session.BeginStroke(MapEditTool.Pencil, 2, 2);

        var stroke = session.ActiveStroke!;
        Assert.True(stroke.Visited!.IsVisited(2 * 5 + 2));
        Assert.Equal(0, stroke.LayerChanges!.Count);
        session.CancelStroke();
    }

    [Fact]
    public void SegmentGrowth_UsesFourThrough4096ThenFixed4096Capacities()
    {
        var buffer = new MapEditChangeBuffer<MapLayerChange>();
        const int total = 10_000;
        for (int i = 0; i < total; i++)
        {
            buffer.Append(new MapLayerChange(i % 100, i / 100, 0, new MapTileLayer(0, 0), new MapTileLayer(i, i)));
        }

        Assert.Equal(total, buffer.Count);
        Assert.Equal(12, buffer.SegmentCount);

        int expectedCapacity = 4;
        int allocated = 0;
        for (int s = 0; s < buffer.SegmentCount; s++)
        {
            Assert.Equal(expectedCapacity, buffer.GetSegmentCapacity(s));
            allocated += expectedCapacity;
            if (expectedCapacity < 4096)
            {
                expectedCapacity *= 2;
            }
        }
        Assert.Equal(allocated, buffer.AllocatedSlotCount);

        for (int s = 0; s < buffer.SegmentCount - 1; s++)
        {
            Assert.Equal(buffer.GetSegmentCapacity(s), buffer.GetSegmentLength(s));
        }
        Assert.Equal(total - (allocated - 4096), buffer.GetSegmentLength(buffer.SegmentCount - 1));
        Assert.Equal(new MapTileLayer(42, 42), buffer[42].After);
    }

    [Fact]
    public void MaximumLayerStroke_HasAtMostTileCountDeltas1003516SlotsAnd254Segments()
    {
        var doc = MapDocument.Create(1000, 1000);
        var session = new MapEditSession(doc);
        session.SelectedTileLayer = new MapTileLayer(1, 1);

        DrawSerpentine(session, 1000, 1000, MapEditTool.Pencil);

        var stroke = session.ActiveStroke!;
        Assert.Equal(15_625, stroke.Visited!.WordCount);
        Assert.True(session.CompleteStroke());

        var buffer = session.History.PeekUndo()!.LayerChanges!;
        Assert.Equal(1_000_000, buffer.Count);
        Assert.True(buffer.Count <= doc.TileCount);
        Assert.InRange(buffer.AllocatedSlotCount, 0, 1_003_516);
        Assert.InRange(buffer.SegmentCount, 0, 254);
    }

    [Fact]
    public void MaximumToggleStroke_WithLoopsNeverExceedsTileCountDeltasOrBitmapBound()
    {
        var doc = MapDocument.Create(1000, 1000);
        var session = new MapEditSession(doc);

        DrawSerpentine(session, 1000, 1000, MapEditTool.BlockedToggle);
        session.ContinueStroke(0, 0);
        session.ContinueStroke(999, 0);
        session.ContinueStroke(999, 999);
        session.ContinueStroke(0, 999);
        session.ContinueStroke(0, 0);

        var stroke = session.ActiveStroke!;
        Assert.Equal(15_625, stroke.Visited!.WordCount);
        Assert.True(session.CompleteStroke());

        var buffer = session.History.PeekUndo()!.FlagsChanges!;
        Assert.Equal(1_000_000, buffer.Count);
        Assert.True(buffer.Count <= doc.TileCount);
        Assert.InRange(buffer.AllocatedSlotCount, 0, 1_003_516);
        Assert.InRange(buffer.SegmentCount, 0, 254);
    }

    [Fact]
    public void CompleteMaximumStroke_TransfersSameDeltaBufferToCommandWithoutCopy()
    {
        var doc = MapDocument.Create(1000, 1000);
        var session = new MapEditSession(doc);
        session.SelectedTileLayer = new MapTileLayer(1, 1);

        DrawSerpentine(session, 1000, 1000, MapEditTool.Pencil);

        var buffer = session.ActiveStroke!.LayerChanges!;
        int count = buffer.Count;
        int segmentCount = buffer.SegmentCount;
        var segmentRefs = new System.Array[segmentCount];
        var capacities = new int[segmentCount];
        for (int s = 0; s < segmentCount; s++)
        {
            segmentRefs[s] = buffer.GetSegment(s);
            capacities[s] = buffer.GetSegmentCapacity(s);
        }

        Assert.True(session.CompleteStroke());

        var command = session.History.PeekUndo()!;
        Assert.Same(buffer, command.LayerChanges);
        Assert.Equal(count, command.LayerChanges!.Count);
        Assert.Equal(segmentCount, command.LayerChanges.SegmentCount);
        for (int s = 0; s < segmentCount; s++)
        {
            Assert.Same(segmentRefs[s], command.LayerChanges.GetSegment(s));
            Assert.Equal(capacities[s], command.LayerChanges.GetSegmentCapacity(s));
        }
        Assert.Null(session.ActiveStroke);
    }

    [Fact]
    public void CancelMaximumStroke_ReplaysSameBufferAndRetainsNoCommand()
    {
        var doc = MapDocument.Create(1000, 1000);
        var session = new MapEditSession(doc);
        session.SelectedTileLayer = new MapTileLayer(1, 1);

        DrawSerpentine(session, 1000, 1000, MapEditTool.Pencil);
        var buffer = session.ActiveStroke!.LayerChanges!;
        Assert.Equal(1_000_000, buffer.Count);

        session.CancelStroke();

        Assert.Equal(0, session.History.UndoCount);
        Assert.Equal(0, session.History.RedoCount);
        Assert.Null(session.ActiveStroke);

        bool allRestored = true;
        for (int i = 0; i < doc.TileCount; i++)
        {
            if (doc.GetTile(i).GetLayer(0) != new MapTileLayer(0, 0))
            {
                allRestored = false;
                break;
            }
        }
        Assert.True(allRestored);
    }

    private static void DrawSerpentine(MapEditSession session, int width, int height, MapEditTool tool)
    {
        session.BeginStroke(tool, 0, 0);
        for (int y = 0; y < height; y++)
        {
            int endX = y % 2 == 0 ? width - 1 : 0;
            session.ContinueStroke(endX, y);
            if (y + 1 < height)
            {
                session.ContinueStroke(endX, y + 1);
            }
        }
    }
}
