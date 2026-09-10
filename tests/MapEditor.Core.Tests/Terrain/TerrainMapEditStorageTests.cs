using System.Linq;
using MapEditor.Core;
using MapEditor.Core.Terrain;
using Xunit;

namespace MapEditor.Core.Tests.Terrain;

public class TerrainMapEditStorageTests
{
    [Fact]
    public void LongEightWaySerpentine_UsesLinearLocalResolutionWorkAndBoundedPersistentStorage()
    {
        const int width = 200;
        const int height = 200;
        var (resolver, set) = TerrainMapEditSessionTests.CreateResolver(TerrainTopology.EightWay);
        var session = new MapEditSession(MapDocument.Create(width, height));
        session.BeginTerrainStroke(resolver, set.Id, TerrainEditMode.Paint, 0, 0);
        for (var y = 0; y < height; y++)
        {
            for (var step = 0; step < width; step++)
            {
                var x = y % 2 == 0 ? step : width - 1 - step;
                if (x != 0 || y != 0)
                {
                    session.ContinueTerrainStroke(x, y);
                }
            }
        }

        var stroke = session.ActiveTerrainStroke!;
        Assert.Equal(width * height, stroke.IntentCount);
        Assert.Equal(0, stroke.DisplacedOwnerCount);
        Assert.True(stroke.TotalInspectedCellCount <= 9L * width * height);
        Assert.True(stroke.AccumulatedCellCount <= width * height);
        Assert.Equal((width * height + 63) / 64, stroke.Visited.WordCount);
        session.CancelStroke();
        Assert.Null(session.ActiveTerrainStroke);
    }

    [Fact]
    public void TerrainStroke_DisplacedOwnerAndIntentEntriesPersistUntilGestureEnds()
    {
        static TerrainSetDefinition CreateSet(int sheet)
        {
            var masks = TerrainMasks.Required(TerrainTopology.FourWay)
                .Select(mask => new TerrainMaskDefinition(mask, [new TerrainGraphicReference(sheet, mask + 1)]))
                .ToArray();
            return TerrainCatalogFixture.CreateSet(
                TerrainTopology.FourWay,
                masks.Select(mask => TerrainCatalogFixture.CreateMember(sheet, mask.Mask + 1)),
                masks: masks);
        }

        var selected = CreateSet(7);
        var displaced = CreateSet(8);
        var resolver = new TerrainMapResolver(TerrainCatalogFixture.CreateCatalog(selected, displaced));
        var document = MapDocument.Create(2, 1);
        document.SetLayer(0, 0, 0, new MapTileLayer(8, 1));
        var session = new MapEditSession(document);
        session.BeginTerrainStroke(resolver, selected.Id, TerrainEditMode.Paint, 0, 0);
        session.ContinueTerrainStroke(1, 0);
        Assert.Equal(2, session.ActiveTerrainStroke!.IntentCount);
        Assert.Equal(1, session.ActiveTerrainStroke.DisplacedOwnerCount);
        session.CancelStroke();
        Assert.Null(session.ActiveTerrainStroke);
    }

    [Fact]
    public void TerrainComplete_TransfersOnlyBuiltCoalescedBufferAndReleasesPreviewState()
    {
        var (resolver, set) = TerrainMapEditSessionTests.CreateResolver();
        var session = new MapEditSession(MapDocument.Create(5, 1));
        session.BeginTerrainStroke(resolver, set.Id, TerrainEditMode.Paint, 0, 0);
        session.ContinueTerrainStroke(4, 0);
        var distinct = session.ActiveTerrainStroke!.AccumulatedCellCount;

        session.CompleteStroke();

        Assert.Null(session.ActiveTerrainStroke);
        Assert.Equal(distinct, ((MapLayerChangesCommand)session.History.PeekUndo()!).Changes.Count);
    }

    [Fact]
    public void TerrainCancel_ReleasesAllTerrainStateAndRetainsNoCommand()
    {
        var (resolver, set) = TerrainMapEditSessionTests.CreateResolver();
        var session = new MapEditSession(MapDocument.Create(5, 1));
        var before = MapCodec.Encode(session.Document);
        session.BeginTerrainStroke(resolver, set.Id, TerrainEditMode.Paint, 0, 0);
        session.ContinueTerrainStroke(4, 0);
        session.CancelStroke();
        Assert.Null(session.ActiveTerrainStroke);
        Assert.Equal(before, MapCodec.Encode(session.Document));
        Assert.Equal(0, session.History.UndoCount);
        Assert.Equal(0, session.History.RedoCount);
        Assert.Equal(0, session.RetainedHistoryUsedBytes);
    }
}
