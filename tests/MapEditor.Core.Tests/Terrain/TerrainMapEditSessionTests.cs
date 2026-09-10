using System;
using System.Linq;
using MapEditor.Core;
using MapEditor.Core.Terrain;
using Xunit;

namespace MapEditor.Core.Tests.Terrain;

public class TerrainMapEditSessionTests
{
    internal static (TerrainMapResolver Resolver, TerrainSetDefinition Set) CreateResolver(
        TerrainTopology topology = TerrainTopology.FourWay,
        int sheet = 7,
        int? missingMask = null)
    {
        var masks = TerrainMasks.Required(topology)
            .Where(mask => mask != missingMask)
            .Select(mask => new TerrainMaskDefinition(mask, [new TerrainGraphicReference(sheet, mask + 1)]))
            .ToArray();
        var members = masks.Select(mask => TerrainCatalogFixture.CreateMember(sheet, mask.Mask + 1)).ToArray();
        var set = TerrainCatalogFixture.CreateSet(topology, members, masks: masks);
        return (new TerrainMapResolver(TerrainCatalogFixture.CreateCatalog(set)), set);
    }

    [Fact]
    public void TerrainPaint_InterpolatesSparseSamplesAndPreviewsLocalRepairs()
    {
        var (resolver, set) = CreateResolver();
        var session = new MapEditSession(MapDocument.Create(5, 1));

        Assert.True(session.BeginTerrainStroke(resolver, set.Id, TerrainEditMode.Paint, 0, 0).Changed);
        Assert.True(session.ContinueTerrainStroke(4, 0).Changed);
        Assert.True(session.CompleteStroke());

        Assert.All(Enumerable.Range(0, 5), x => Assert.NotEqual(default, session.Document[x, 0].GetLayer(0)));
    }

    [Fact]
    public void TerrainStroke_NoOpBeginPublishesActiveStrokeWithFalseChanged()
    {
        var (resolver, set) = CreateResolver();
        var document = MapDocument.Create(1, 1);
        document.SetLayer(0, 0, 0, new MapTileLayer(7, 1));
        var session = new MapEditSession(document);

        var update = session.BeginTerrainStroke(resolver, set.Id, TerrainEditMode.Paint, 0, 0);

        Assert.True(update.Succeeded);
        Assert.False(update.Changed);
        Assert.True(session.HasActiveStroke);
        Assert.False(session.CompleteStroke());
    }

    [Fact]
    public void BeginStroke_TerrainRequiresDedicatedApi()
    {
        var session = new MapEditSession(MapDocument.Create(1, 1));
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => session.BeginStroke(MapEditTool.Terrain, 0, 0));
        Assert.Equal("tool", exception.ParamName);
        Assert.False(session.HasActiveStroke);
    }

    [Fact]
    public void ContinueTerrainStroke_MissingMaskRestoresPreBeginBytesClearsStrokeAndReturnsExactFailure()
    {
        var (resolver, set) = CreateResolver(missingMask: TerrainMasks.East);
        var document = MapDocument.Create(2, 1);
        var session = new MapEditSession(document);
        var before = MapCodec.Encode(document);

        Assert.True(session.BeginTerrainStroke(resolver, set.Id, TerrainEditMode.Paint, 0, 0).Succeeded);
        var update = session.ContinueTerrainStroke(1, 0);

        Assert.False(update.Succeeded);
        Assert.False(update.Changed);
        Assert.Equal(new TerrainEditFailure(set.Id, TerrainMasks.East), update.Failure);
        Assert.Equal(before, MapCodec.Encode(document));
        Assert.False(session.HasActiveStroke);
        Assert.Equal(0, session.HistoryVersion);
    }

    [Fact]
    public void BeginTerrainStroke_CapturesResolverTerrainModeAndTopLayerForWholeGesture()
    {
        var (resolver, set) = CreateResolver(sheet: 7);
        var session = new MapEditSession(MapDocument.Create(2, 1));
        session.SelectedLayers = 1 << 3;
        session.BeginTerrainStroke(resolver, set.Id, TerrainEditMode.Paint, 0, 0);
        session.SelectedLayers = 1;
        session.ContinueTerrainStroke(1, 0);
        session.CompleteStroke();
        Assert.All(Enumerable.Range(0, 2), x => Assert.Equal(7, session.Document[x, 0].GetLayer(3).Sheet));
        Assert.All(Enumerable.Range(0, 2), x => Assert.Equal(default, session.Document[x, 0].GetLayer(0)));
    }

    [Fact]
    public void TerrainErase_InterpolatesAndOnlyErasesSelectedMembers()
    {
        var (resolver, set) = CreateResolver();
        var document = MapDocument.Create(3, 1);
        document.SetLayer(0, 0, 0, new MapTileLayer(7, 3));
        document.SetLayer(1, 0, 0, new MapTileLayer(90, 90));
        document.SetLayer(2, 0, 0, new MapTileLayer(7, 9));
        var session = new MapEditSession(document);
        session.BeginTerrainStroke(resolver, set.Id, TerrainEditMode.Erase, 0, 0);
        session.ContinueTerrainStroke(2, 0);
        session.CompleteStroke();
        Assert.Equal(default, document[0, 0].GetLayer(0));
        Assert.Equal(new MapTileLayer(90, 90), document[1, 0].GetLayer(0));
        Assert.Equal(default, document[2, 0].GetLayer(0));
    }

    [Fact]
    public void TerrainStroke_OverlappingSegmentsVisitDirectCellsOnce()
    {
        var (resolver, set) = CreateResolver();
        var session = new MapEditSession(MapDocument.Create(5, 1));
        session.BeginTerrainStroke(resolver, set.Id, TerrainEditMode.Paint, 0, 0);
        session.ContinueTerrainStroke(4, 0);
        var update = session.ContinueTerrainStroke(0, 0);
        Assert.True(update.Succeeded);
        Assert.False(update.Changed);
        Assert.True(session.HasActiveStroke);
        Assert.Equal(5, session.ActiveTerrainStroke!.IntentCount);
        Assert.Equal(5, Enumerable.Range(0, 5).Count(i => session.ActiveTerrainStroke.Visited.IsVisited(i)));
        session.CancelStroke();
    }

    [Fact]
    public void TerrainStroke_ChangingSelectedLayerAfterBeginStillUsesCapturedLayer()
    {
        BeginTerrainStroke_CapturesResolverTerrainModeAndTopLayerForWholeGesture();
    }

    [Fact]
    public void TerrainStroke_ChangesOnlyCapturedLayerAndPreservesFlagsAndOtherLayers()
    {
        var (resolver, set) = CreateResolver();
        var document = MapDocument.Create(2, 1);
        document.SetFlags(0, 0, 37);
        document.SetLayer(0, 0, 4, new MapTileLayer(8, 8));
        var session = new MapEditSession(document);
        session.SelectedLayers = 1 << 2;
        session.BeginTerrainStroke(resolver, set.Id, TerrainEditMode.Paint, 0, 0);
        session.CompleteStroke();
        Assert.Equal(37, document[0, 0].Flags);
        Assert.Equal(new MapTileLayer(8, 8), document[0, 0].GetLayer(4));
        Assert.NotEqual(default, document[0, 0].GetLayer(2));
    }

    [Fact]
    public void TerrainStroke_ChangingCallerSelectionIdOrPaintEraseModeAfterBeginCannotAffectContinuation()
    {
        var (resolver, set) = CreateResolver();
        var session = new MapEditSession(MapDocument.Create(2, 1));
        var id = set.Id;
        var mode = TerrainEditMode.Paint;
        session.BeginTerrainStroke(resolver, id, mode, 0, 0);
        id = "other";
        mode = TerrainEditMode.Erase;
        session.ContinueTerrainStroke(1, 0);
        session.CompleteStroke();
        Assert.All(Enumerable.Range(0, 2), x => Assert.NotEqual(default, session.Document[x, 0].GetLayer(0)));
    }

    [Fact]
    public void TerrainStroke_OneByOneAndMapEdgeBeginUseMaskZeroAndClipNeighbors()
    {
        var (resolver, set) = CreateResolver();
        var session = new MapEditSession(MapDocument.Create(1, 1));
        var update = session.BeginTerrainStroke(resolver, set.Id, TerrainEditMode.Paint, 0, 0);
        Assert.True(update.Succeeded);
        Assert.Equal(new MapTileLayer(7, 1), session.Document[0, 0].GetLayer(0));
        session.CancelStroke();
    }

    [Fact]
    public void TerrainStroke_BeginOutOfBoundsUnknownNonEnabledNullOrBlankIdNullResolverOrInvalidModeThrowsWithoutMutation()
    {
        var (_, set) = CreateResolver();
        var pendingMembers = new[] { TerrainCatalogFixture.CreateMember(8, 1) };
        var pending = TerrainCatalogFixture.CreateSet(
            TerrainTopology.FourWay,
            pendingMembers,
            TerrainReviewStatus.Pending,
            masks: TerrainCatalogFixture.CreateFullMasks(TerrainTopology.FourWay, pendingMembers));
        var resolver = new TerrainMapResolver(TerrainCatalogFixture.CreateCatalog(set, pending));
        var session = new MapEditSession(MapDocument.Create(2, 2));
        var before = MapCodec.Encode(session.Document);
        Assert.Equal("resolver", Assert.Throws<ArgumentNullException>(() => session.BeginTerrainStroke(null!, set.Id, TerrainEditMode.Paint, 0, 0)).ParamName);
        Assert.Equal("terrainId", Assert.Throws<ArgumentNullException>(() => session.BeginTerrainStroke(resolver, null!, TerrainEditMode.Paint, 0, 0)).ParamName);
        Assert.Equal("terrainId", Assert.Throws<ArgumentException>(() => session.BeginTerrainStroke(resolver, " ", TerrainEditMode.Paint, 0, 0)).ParamName);
        Assert.Equal("mode", Assert.Throws<ArgumentOutOfRangeException>(() => session.BeginTerrainStroke(resolver, set.Id, (TerrainEditMode)20, 0, 0)).ParamName);
        Assert.Equal("x", Assert.Throws<ArgumentOutOfRangeException>(() => session.BeginTerrainStroke(resolver, set.Id, TerrainEditMode.Paint, -1, 3)).ParamName);
        Assert.Equal("y", Assert.Throws<ArgumentOutOfRangeException>(() => session.BeginTerrainStroke(resolver, set.Id, TerrainEditMode.Paint, 0, 2)).ParamName);
        Assert.Equal("terrainId", Assert.Throws<ArgumentException>(() => session.BeginTerrainStroke(resolver, set.Id.ToUpperInvariant(), TerrainEditMode.Paint, 0, 0)).ParamName);
        Assert.Equal("terrainId", Assert.Throws<ArgumentException>(() => session.BeginTerrainStroke(resolver, "terrain-does-not-exist", TerrainEditMode.Paint, 0, 0)).ParamName);
        Assert.Equal("terrainId", Assert.Throws<ArgumentException>(() => session.BeginTerrainStroke(resolver, pending.Id, TerrainEditMode.Paint, 0, 0)).ParamName);
        Assert.Equal(before, MapCodec.Encode(session.Document));
        Assert.False(session.HasActiveStroke);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ManualAndTerrainBeginApisRejectWhileEitherStrokeKindIsActive(bool activeTerrain, bool beginTerrain)
    {
        var (resolver, set) = CreateResolver();
        var session = new MapEditSession(MapDocument.Create(2, 1));
        if (activeTerrain)
        {
            session.BeginTerrainStroke(resolver, set.Id, TerrainEditMode.Paint, 0, 0);
        }
        else
        {
            session.BeginStroke(MapEditTool.Pencil, 0, 0);
        }

        Assert.Throws<InvalidOperationException>(() =>
        {
            if (beginTerrain)
            {
                session.BeginTerrainStroke(resolver, set.Id, TerrainEditMode.Paint, 1, 0);
            }
            else
            {
                session.BeginStroke(MapEditTool.Pencil, 1, 0);
            }
        });
        session.CancelStroke();
    }

    [Fact]
    public void ContinueStroke_WhileTerrainActiveThrowsWithoutPreviewOrSampleChange()
    {
        var (resolver, set) = CreateResolver();
        var session = new MapEditSession(MapDocument.Create(2, 1));
        session.BeginTerrainStroke(resolver, set.Id, TerrainEditMode.Paint, 0, 0);
        var before = MapCodec.Encode(session.Document);
        var sample = session.ActiveTerrainStroke!.PreviousSample;
        Assert.Throws<InvalidOperationException>(() => session.ContinueStroke(1, 0));
        Assert.Equal(before, MapCodec.Encode(session.Document));
        Assert.Equal(sample, session.ActiveTerrainStroke!.PreviousSample);
        session.CancelStroke();
    }

    [Fact]
    public void ContinueTerrainStroke_WhileManualActiveThrowsWithoutManualMutationOrSampleChange()
    {
        var (resolver, _) = CreateResolver();
        var session = new MapEditSession(MapDocument.Create(2, 1));
        session.SelectedTileLayer = new MapTileLayer(1, 1);
        session.BeginStroke(MapEditTool.Pencil, 0, 0);
        var sample = session.ActiveStroke!.PreviousSample;
        var before = MapCodec.Encode(session.Document);
        Assert.Throws<InvalidOperationException>(() => session.ContinueTerrainStroke(1, 0));
        Assert.Equal(before, MapCodec.Encode(session.Document));
        Assert.Equal(sample, session.ActiveStroke!.PreviousSample);
        session.CancelStroke();
    }

    [Fact]
    public void ContinueTerrainStroke_OutOfBoundsPreservesExactPreviewActiveStatePriorSampleHistoryAndIds()
    {
        var (resolver, set) = CreateResolver();
        var session = new MapEditSession(MapDocument.Create(3, 2));
        session.BeginTerrainStroke(resolver, set.Id, TerrainEditMode.Paint, 0, 0);
        var stroke = session.ActiveTerrainStroke!;
        var bytes = MapCodec.Encode(session.Document);
        var state = (stroke.PreviousSample, stroke.IntentCount, stroke.DisplacedOwnerCount, stroke.AccumulatedCellCount,
            stroke.TotalInspectedCellCount, session.HistoryVersion, session.RetainedHistoryUsedBytes,
            session.History.UndoCount, session.History.RedoCount, session.CurrentStateId, session.SavedStateId, session.IsDirty);
        Assert.Equal("x", Assert.Throws<ArgumentOutOfRangeException>(() => session.ContinueTerrainStroke(-1, 0)).ParamName);
        Assert.Equal("y", Assert.Throws<ArgumentOutOfRangeException>(() => session.ContinueTerrainStroke(0, 2)).ParamName);
        Assert.Equal(bytes, MapCodec.Encode(session.Document));
        Assert.Same(stroke, session.ActiveTerrainStroke);
        Assert.Equal(state, (stroke.PreviousSample, stroke.IntentCount, stroke.DisplacedOwnerCount, stroke.AccumulatedCellCount,
            stroke.TotalInspectedCellCount, session.HistoryVersion, session.RetainedHistoryUsedBytes,
            session.History.UndoCount, session.History.RedoCount, session.CurrentStateId, session.SavedStateId, session.IsDirty));
        session.CancelStroke();
    }

    [Fact]
    public void BeginTerrainStroke_MissingMaskReturnsFailureWithoutPublication()
    {
        var (resolver, set) = CreateResolver(missingMask: 0);
        var session = new MapEditSession(MapDocument.Create(1, 1));
        var before = MapCodec.Encode(session.Document);
        var update = session.BeginTerrainStroke(resolver, set.Id, TerrainEditMode.Paint, 0, 0);
        Assert.Equal(new TerrainStrokeUpdate(false, false, new TerrainEditFailure(set.Id, 0)), update);
        Assert.False(session.HasActiveStroke);
        Assert.Equal(before, MapCodec.Encode(session.Document));
    }

    [Fact]
    public void CompleteAndCancelDispatchBothKindsAndWithoutActiveStillThrow()
    {
        var (resolver, set) = CreateResolver();
        var session = new MapEditSession(MapDocument.Create(2, 1));
        session.BeginTerrainStroke(resolver, set.Id, TerrainEditMode.Paint, 0, 0);
        session.CancelStroke();
        Assert.Throws<InvalidOperationException>(() => session.CancelStroke());
        session.SelectedTileLayer = new MapTileLayer(1, 1);
        session.BeginStroke(MapEditTool.Pencil, 0, 0);
        Assert.True(session.CompleteStroke());
        Assert.Throws<InvalidOperationException>(() => session.CompleteStroke());
    }

    [Fact]
    public void TerrainCancel_RestoresExactBytesAndPriorDirtyRedoState()
    {
        var (resolver, set) = CreateResolver();
        var session = new MapEditSession(MapDocument.Create(2, 1));
        session.SelectedTileLayer = new MapTileLayer(2, 2);
        session.BeginStroke(MapEditTool.Pencil, 1, 0);
        session.CompleteStroke();
        session.Undo();
        var before = MapCodec.Encode(session.Document);
        var state = (session.HistoryVersion, session.RetainedHistoryUsedBytes, session.CurrentStateId, session.SavedStateId, session.IsDirty);
        session.BeginTerrainStroke(resolver, set.Id, TerrainEditMode.Paint, 0, 0);
        session.CancelStroke();
        Assert.Equal(before, MapCodec.Encode(session.Document));
        Assert.Equal(state, (session.HistoryVersion, session.RetainedHistoryUsedBytes, session.CurrentStateId, session.SavedStateId, session.IsDirty));
        Assert.True(session.CanRedo);
    }
}
