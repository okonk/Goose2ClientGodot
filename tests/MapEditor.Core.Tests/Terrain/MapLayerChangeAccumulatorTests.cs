using System;
using System.Linq;
using MapEditor.Core;
using Xunit;

namespace MapEditor.Core.Tests;

public class MapLayerChangeAccumulatorTests
{
    private static MapTileLayer Tile(int sheet, int graphic) => new(sheet, graphic);

    [Fact]
    public void Record_FirstAndLatestAreTrackedPerCell()
    {
        var document = MapDocument.Create(4, 4);
        document.SetLayer(0, 0, 1, Tile(1, 1));
        document.SetLayer(2, 1, 1, Tile(2, 2));
        var accumulator = new MapLayerChangeAccumulator(document, 1, document.TileCount);

        document.SetLayer(0, 0, 1, Tile(3, 3));
        accumulator.Record(0, Tile(1, 1));
        document.SetLayer(2, 1, 1, Tile(4, 4));
        accumulator.Record(6, Tile(2, 2));

        var changes = accumulator.BuildChanges();
        Assert.Equal(2, changes.Count);
        Assert.Equal(new MapLayerChange(0, 0, 1, Tile(1, 1), Tile(3, 3)), changes[0]);
        Assert.Equal(new MapLayerChange(2, 1, 1, Tile(2, 2), Tile(4, 4)), changes[1]);
    }

    [Fact]
    public void Accumulator_RepeatedCoordinate_RestoresOriginal()
    {
        var document = MapDocument.Create(4, 4);
        document.SetLayer(1, 1, 0, Tile(1, 1));
        var accumulator = new MapLayerChangeAccumulator(document, 0, document.TileCount);

        document.SetLayer(1, 1, 0, Tile(2, 2));
        accumulator.Record(5, Tile(1, 1));
        document.SetLayer(1, 1, 0, Tile(3, 3));
        accumulator.Record(5, Tile(2, 2));

        Assert.Single(accumulator.BuildChanges());
        Assert.Equal(new MapLayerChange(1, 1, 0, Tile(1, 1), Tile(3, 3)), accumulator.BuildChanges()[0]);
    }

    [Fact]
    public void Record_ReturnToOriginalValueRemovesPendingChange()
    {
        var document = MapDocument.Create(4, 4);
        document.SetLayer(1, 1, 0, Tile(1, 1));
        var accumulator = new MapLayerChangeAccumulator(document, 0, document.TileCount);

        document.SetLayer(1, 1, 0, Tile(2, 2));
        accumulator.Record(5, Tile(1, 1));
        Assert.True(accumulator.HasChanges);

        document.SetLayer(1, 1, 0, Tile(1, 1));
        accumulator.Record(5, Tile(2, 2));

        Assert.False(accumulator.HasChanges);
        Assert.Empty(accumulator.BuildChanges());
    }

    [Fact]
    public void Record_NoOpChangeIsNotRecorded()
    {
        var document = MapDocument.Create(4, 4);
        document.SetLayer(1, 1, 0, Tile(1, 1));
        var accumulator = new MapLayerChangeAccumulator(document, 0, document.TileCount);

        accumulator.Record(5, Tile(1, 1));

        Assert.False(accumulator.HasChanges);
    }

    [Fact]
    public void BuildChanges_IsRowMajor()
    {
        var document = MapDocument.Create(4, 4);
        var accumulator = new MapLayerChangeAccumulator(document, 2, document.TileCount);

        foreach (var (index, tile) in new[]
        {
            (13, Tile(1, 1)),
            (1, Tile(2, 2)),
            (7, Tile(3, 3)),
            (0, Tile(4, 4))
        })
        {
            document.SetLayer(index % 4, index / 4, 2, tile);
            accumulator.Record(index, default);
        }

        var changes = accumulator.BuildChanges();
        Assert.Equal(new[] { 0, 1, 7, 13 }, changes.Select(change => change.Y * 4 + change.X).ToArray());
        Assert.All(changes, change => Assert.Equal(2, change.LayerIndex));
        Assert.Equal(Tile(4, 4), changes[0].After);
        Assert.Equal(Tile(2, 2), changes[1].After);
        Assert.Equal(Tile(3, 3), changes[2].After);
        Assert.Equal(Tile(1, 1), changes[3].After);
    }

    [Fact]
    public void Restore_WritesFirstValuesBackToDocument()
    {
        var document = MapDocument.Create(4, 4);
        document.SetLayer(0, 0, 1, Tile(1, 1));
        document.SetLayer(3, 3, 1, Tile(2, 2));
        var accumulator = new MapLayerChangeAccumulator(document, 1, document.TileCount);

        document.SetLayer(0, 0, 1, Tile(3, 3));
        accumulator.Record(0, Tile(1, 1));
        document.SetLayer(3, 3, 1, Tile(4, 4));
        accumulator.Record(15, Tile(2, 2));

        accumulator.Restore();

        Assert.Equal(Tile(1, 1), document[0, 0].GetLayer(1));
        Assert.Equal(Tile(2, 2), document[3, 3].GetLayer(1));
        Assert.False(accumulator.HasChanges);
    }

    [Fact]
    public void RetainedBytes_MatchLayerChangeSlotAccounting()
    {
        var document = MapDocument.Create(4, 4);
        var accumulator = new MapLayerChangeAccumulator(document, 0, document.TileCount);

        Assert.Equal(MapEditCommand.BaseCommandBytes, accumulator.RetainedBytes);

        document.SetLayer(0, 0, 0, Tile(1, 1));
        accumulator.Record(0, default);
        document.SetLayer(1, 0, 0, Tile(2, 2));
        accumulator.Record(1, default);

        Assert.Equal(
            checked(MapEditCommand.BaseCommandBytes + 2 * MapEditCommand.LayerSlotBytes),
            accumulator.RetainedBytes);
    }
}
