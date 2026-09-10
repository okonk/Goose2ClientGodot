using System;
using System.Collections.Generic;
using System.Linq;
using MapEditor.Core;
using MapEditor.Core.Terrain;
using Xunit;

namespace MapEditor.Core.Tests.Terrain;

public class MapLayerChangeAccumulatorTests
{
    private const int Layer = 0;

    private static MapDocument CreateDocument(int width, int height, params (int X, int Y, int Sheet, int Graphic)[] cells)
    {
        var document = MapDocument.Create(width, height);
        foreach (var (x, y, sheet, graphic) in cells)
        {
            document.SetLayer(x, y, Layer, new MapTileLayer(sheet, graphic));
        }

        return document;
    }

    private static TerrainMapPatch CreatePatch(params (int CellIndex, int Sheet, int Graphic)[] cells)
    {
        var patchCells = cells
            .Select(cell => new TerrainPatchCell(cell.CellIndex, new MapTileLayer(cell.Sheet, cell.Graphic)))
            .ToList();
        return new TerrainMapPatch(patchCells);
    }

    private static void AssertGrid(MapDocument document, params (int X, int Y, int Sheet, int Graphic)[] cells)
    {
        for (var y = 0; y < document.Height; y++)
        {
            for (var x = 0; x < document.Width; x++)
            {
                var match = cells.FirstOrDefault(cell => cell.X == x && cell.Y == y);
                var expected = match.Sheet == 0 && match.Graphic == 0
                    ? new MapTileLayer(0, 0)
                    : new MapTileLayer(match.Sheet, match.Graphic);
                Assert.Equal(expected, document.GetTile(y * document.Width + x).GetLayer(Layer));
            }
        }
    }

    [Fact]
    public void Apply_CompletePatchRecordsFirstAndLatestValuesInRowMajorOrder()
    {
        var document = CreateDocument(
            4,
            4,
            (0, 0, 9, 9),
            (3, 0, 8, 8),
            (1, 1, 7, 7));
        var accumulator = new MapLayerChangeAccumulator();

        var changed = accumulator.Apply(document, Layer, CreatePatch(
            (3, 1, 2),
            (5, 1, 3),
            (0, 1, 4)));

        Assert.True(changed);
        Assert.Equal(3, accumulator.Count);
        Assert.True(accumulator.HasNetChanges);
        AssertGrid(
            document,
            (0, 0, 1, 4),
            (3, 0, 1, 2),
            (1, 1, 1, 3));

        var changes = accumulator.BuildChanges(Layer);
        Assert.Equal(3, changes.Count);
        Assert.Equal(
            new[]
            {
                new MapLayerChange(0, 0, Layer, new MapTileLayer(9, 9), new MapTileLayer(1, 4)),
                new MapLayerChange(3, 0, Layer, new MapTileLayer(8, 8), new MapTileLayer(1, 2)),
                new MapLayerChange(1, 1, Layer, new MapTileLayer(7, 7), new MapTileLayer(1, 3))
            },
            Enumerable.Range(0, changes.Count).Select(i => changes[i]).ToArray());
    }

    [Fact]
    public void Apply_RepeatedCoordinateRetainsFirstBeforeAndLatestAfter()
    {
        var document = CreateDocument(4, 4, (2, 1, 6, 6));
        var accumulator = new MapLayerChangeAccumulator();

        accumulator.Apply(document, Layer, CreatePatch((6, 1, 1)));
        accumulator.Apply(document, Layer, CreatePatch((6, 1, 2)));
        accumulator.Apply(document, Layer, CreatePatch((6, 1, 3)));

        Assert.Equal(1, accumulator.Count);
        var changes = accumulator.BuildChanges(Layer);
        Assert.Equal(1, changes.Count);
        Assert.Equal(
            new MapLayerChange(2, 1, Layer, new MapTileLayer(6, 6), new MapTileLayer(1, 3)),
            changes[0]);
    }

    [Fact]
    public void Apply_ReturnsConcretePerCallChangeWhileHasNetChangesTracksGestureNet()
    {
        var document = CreateDocument(4, 4, (0, 0, 5, 5));
        var accumulator = new MapLayerChangeAccumulator();

        Assert.False(accumulator.Apply(document, Layer, CreatePatch((0, 5, 5))));
        Assert.False(accumulator.HasNetChanges);
        Assert.Equal(1, accumulator.Count);

        Assert.True(accumulator.Apply(document, Layer, CreatePatch((0, 1, 1))));
        Assert.True(accumulator.HasNetChanges);

        Assert.True(accumulator.Apply(document, Layer, CreatePatch((0, 5, 5))));
        Assert.False(accumulator.HasNetChanges);
    }

    [Fact]
    public void Apply_InvalidDuplicateOrOutOfBoundsPatchThrowsBeforeAnyWrite()
    {
        var document = CreateDocument(4, 4, (0, 0, 5, 5));
        var duplicate = new MapLayerChangeAccumulator();
        Assert.Throws<ArgumentOutOfRangeException>(
            () => duplicate.Apply(document, Layer, CreatePatch((0, 1, 1), (0, 2, 2))));
        Assert.Equal(0, duplicate.Count);
        AssertGrid(document, (0, 0, 5, 5));

        var outOfBounds = new MapLayerChangeAccumulator();
        Assert.Throws<ArgumentOutOfRangeException>(
            () => outOfBounds.Apply(document, Layer, CreatePatch((0, 1, 1), (16, 2, 2))));
        Assert.Equal(0, outOfBounds.Count);
        AssertGrid(document, (0, 0, 5, 5));

        var negative = new MapLayerChangeAccumulator();
        Assert.Throws<ArgumentOutOfRangeException>(
            () => negative.Apply(document, Layer, CreatePatch((0, 1, 1), (-1, 2, 2))));
        Assert.Equal(0, negative.Count);
        AssertGrid(document, (0, 0, 5, 5));
    }

    [Fact]
    public void Restore_ReinstatesEveryFirstValueWithoutTouchingFlagsOrOtherLayers()
    {
        var document = CreateDocument(4, 4, (0, 0, 5, 5), (3, 2, 4, 4));
        document.SetFlags(0, 0, MapDocument.BlockedFlag);
        document.SetLayer(0, 0, 2, new MapTileLayer(3, 3));
        document.SetLayer(3, 2, 2, new MapTileLayer(3, 3));
        var accumulator = new MapLayerChangeAccumulator();

        accumulator.Apply(document, Layer, CreatePatch((0, 1, 1), (11, 1, 2)));
        AssertGrid(document, (0, 0, 1, 1), (3, 2, 1, 2));

        accumulator.Restore(document, Layer);

        AssertGrid(document, (0, 0, 5, 5), (3, 2, 4, 4));
        Assert.Equal(MapDocument.BlockedFlag, document.GetTile(0).Flags);
        Assert.Equal(new MapTileLayer(3, 3), document.GetTile(0).GetLayer(2));
        Assert.Equal(new MapTileLayer(3, 3), document.GetTile(11).GetLayer(2));
        Assert.Equal(2, accumulator.Count);
        Assert.True(accumulator.HasNetChanges);
    }

    [Fact]
    public void BuildChanges_EmitsOneNetDeltaPerCoordinateAndDoesNotMutateState()
    {
        var document = CreateDocument(4, 4, (0, 0, 5, 5), (1, 0, 6, 6));
        var accumulator = new MapLayerChangeAccumulator();
        accumulator.Apply(document, Layer, CreatePatch((0, 1, 1), (1, 6, 6)));

        var first = accumulator.BuildChanges(Layer);
        Assert.Equal(1, first.Count);
        Assert.Equal(
            new MapLayerChange(0, 0, Layer, new MapTileLayer(5, 5), new MapTileLayer(1, 1)),
            first[0]);

        AssertGrid(document, (0, 0, 1, 1), (1, 0, 6, 6));
        Assert.Equal(2, accumulator.Count);
        Assert.True(accumulator.HasNetChanges);

        var second = accumulator.BuildChanges(Layer);
        Assert.Equal(1, second.Count);
        Assert.Equal(first[0], second[0]);
    }

    [Fact]
    public void BuildChanges_ApplyAwayThenBackOmitsNetZeroAndPreservesRedoEligibility()
    {
        var document = CreateDocument(4, 4, (0, 0, 5, 5), (1, 0, 6, 6));
        var accumulator = new MapLayerChangeAccumulator();

        accumulator.Apply(document, Layer, CreatePatch((0, 1, 1), (1, 1, 2)));
        accumulator.Apply(document, Layer, CreatePatch((0, 5, 5)));

        Assert.Equal(2, accumulator.Count);
        Assert.True(accumulator.HasNetChanges);
        var changes = accumulator.BuildChanges(Layer);
        Assert.Equal(1, changes.Count);
        Assert.Equal(
            new MapLayerChange(1, 0, Layer, new MapTileLayer(6, 6), new MapTileLayer(1, 2)),
            changes[0]);

        accumulator.Restore(document, Layer);
        AssertGrid(document, (0, 0, 5, 5), (1, 0, 6, 6));
        foreach (var change in Enumerable.Range(0, changes.Count).Select(i => changes[i]))
        {
            document.SetLayer(change.X, change.Y, change.LayerIndex, change.After);
        }
        AssertGrid(document, (0, 0, 5, 5), (1, 0, 1, 2));
    }
}
