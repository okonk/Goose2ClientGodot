using System;
using System.Collections.Generic;
using System.Linq;
using MapEditor.Core;
using Xunit;

namespace MapEditor.Core.Tests;

public class TerrainMapEditStrokeTests
{
    private static readonly Guid Grass = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Water = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    private static MapTileLayer Tile(int sheet, int graphic) => new(sheet, graphic);

    private static TerrainPattern Pattern(Guid? center, params (TerrainPeer Peer, Guid? Value)[] peers)
    {
        var pattern = new TerrainPattern { Center = center };
        foreach (var (peer, value) in peers)
        {
            pattern = peer switch
            {
                TerrainPeer.North => pattern with { North = value },
                TerrainPeer.East => pattern with { East = value },
                TerrainPeer.South => pattern with { South = value },
                TerrainPeer.West => pattern with { West = value },
                TerrainPeer.NorthEast => pattern with { NorthEast = value },
                TerrainPeer.SouthEast => pattern with { SouthEast = value },
                TerrainPeer.SouthWest => pattern with { SouthWest = value },
                _ => pattern with { NorthWest = value }
            };
        }

        return pattern;
    }

    private static TerrainCatalogIndex Index(params (int Sheet, int Graphic, TerrainPattern Pattern)[] graphics)
    {
        var list = graphics.ToList();
        var ids = new List<Guid>();
        foreach (var (_, _, pattern) in list)
        {
            foreach (var value in new[]
            {
                pattern.Center, pattern.North, pattern.East, pattern.South, pattern.West,
                pattern.NorthEast, pattern.SouthEast, pattern.SouthWest, pattern.NorthWest
            })
            {
                if (value is not null && !ids.Contains(value.Value))
                {
                    ids.Add(value.Value);
                }
            }
        }

        var centered = new HashSet<Guid>(list.Where(g => g.Pattern.Center is not null).Select(g => g.Pattern.Center!.Value));
        var filler = 1;
        foreach (var id in ids)
        {
            if (!centered.Contains(id))
            {
                list.Add((9, filler++, Pattern(id)));
            }
        }

        var catalog = new TerrainCatalog(
            ids.Select(id => new TerrainDefinition(id, id.ToString(), null)),
            list
                .Select(graphic => new TerrainGraphicDefinition(
                    new TerrainGraphicReference(graphic.Sheet, graphic.Graphic),
                    graphic.Pattern))
                .ToList());

        var result = TerrainCatalogValidator.Validate(catalog);
        Assert.True(result.IsValid, string.Join("; ", result.Issues.Select(issue => issue.Message)));
        return result.Index!;
    }

    private static (TerrainCatalogIndex Index, TerrainMapResolver Resolver) Catalog()
    {
        var graphics = new List<(int Sheet, int Graphic, TerrainPattern Pattern)>
        {
            (0, 1, Pattern(Grass)),
            (0, 2, Pattern(Grass, (TerrainPeer.West, Water))),
            (0, 3, Pattern(Grass, (TerrainPeer.East, Water))),
            (0, 4, Pattern(Grass, (TerrainPeer.East, Water), (TerrainPeer.South, Water))),
            (0, 7, Pattern(Grass, (TerrainPeer.North, Water))),
            (0, 8, Pattern(Grass, (TerrainPeer.North, Water), (TerrainPeer.South, Water))),
            (0, 11, Pattern(Grass, (TerrainPeer.East, Grass))),
            (0, 12, Pattern(Grass, (TerrainPeer.West, Water), (TerrainPeer.East, Grass))),
            (1, 1, Pattern(Water)),
            (1, 2, Pattern(Water, (TerrainPeer.East, Grass))),
            (1, 3, Pattern(Water, (TerrainPeer.West, Grass))),
            (1, 4, Pattern(Water, (TerrainPeer.South, Grass))),
            (1, 5, Pattern(Water, (TerrainPeer.North, Grass)))
        };

        var index = Index(graphics.ToArray());
        return (index, new TerrainMapResolver(index));
    }

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
            => _inner.TryResolvePatch(document, layer, centerOverrides, directlyChangedIndices, out patch, out failure);

        public bool TryResolveStaged(
            MapDocument document,
            int layer,
            IReadOnlyDictionary<int, Guid?> centerOverrides,
            IReadOnlyCollection<(int Index, Guid? Center)> staged,
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

            return _inner.TryResolveStaged(document, layer, centerOverrides, staged, out patch, out failure);
        }
    }

    [Fact]
    public void Begin_PaintsFirstSampleImmediately()
    {
        var (_, resolver) = Catalog();
        var document = MapDocument.Create(5, 5);
        var stroke = new TerrainMapEditStroke(document, resolver, 0, Grass, TerrainEditMode.Paint);

        var result = stroke.Begin(2, 2);

        Assert.Equal(new TerrainEditResult(true, true, null), result);
        Assert.True(stroke.IsActive);
        Assert.Equal(new MapCoordinate(2, 2), stroke.PreviousSample);
        Assert.Equal(Tile(0, 1), document[2, 2].GetLayer(0));
        Assert.True(stroke.TryGetIntent(12, out var intent));
        Assert.Equal(Grass, intent);
    }

    [Fact]
    public void Begin_CapturesResolverTerrainModeAndLayer()
    {
        var (_, resolver) = Catalog();
        var document = MapDocument.Create(3, 3);
        var stroke = new TerrainMapEditStroke(document, resolver, 3, Grass, TerrainEditMode.Erase);

        Assert.Same(resolver, stroke.Resolver);
        Assert.Equal(3, stroke.LayerIndex);
        Assert.Equal(Grass, stroke.TerrainId);
        Assert.Equal(TerrainEditMode.Erase, stroke.Mode);
        Assert.False(stroke.IsActive);

        stroke.Begin(1, 1);
        Assert.True(stroke.IsActive);
    }

    [Fact]
    public void Begin_InvalidCoordinateThrows()
    {
        var (_, resolver) = Catalog();
        var document = MapDocument.Create(3, 3);
        var stroke = new TerrainMapEditStroke(document, resolver, 0, Grass, TerrainEditMode.Paint);

        Assert.Throws<ArgumentOutOfRangeException>(() => stroke.Begin(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => stroke.Begin(3, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => stroke.Begin(0, 3));
        Assert.False(stroke.IsActive);
    }

    [Fact]
    public void Continue_SparseSamples_HasNoGaps()
    {
        var (_, resolver) = Catalog();
        var document = MapDocument.Create(5, 5);
        var stroke = new TerrainMapEditStroke(document, resolver, 0, Grass, TerrainEditMode.Paint);

        stroke.Begin(0, 0);
        var result = stroke.Continue(4, 0);

        Assert.Equal(new TerrainEditResult(true, true, null), result);
        Assert.Equal(new MapCoordinate(4, 0), stroke.PreviousSample);
        for (var x = 0; x < 5; x++)
        {
            Assert.True(stroke.IsVisited(x));
            Assert.NotEqual(default, document[x, 0].GetLayer(0));
            Assert.True(stroke.TryGetIntent(x, out var intent));
            Assert.Equal(Grass, intent);
        }
    }

    [Fact]
    public void Continue_OverlappingSegment_DoesNotRestageVisitedCells()
    {
        var (_, resolver) = Catalog();
        var document = MapDocument.Create(5, 5);
        var stroke = new TerrainMapEditStroke(document, resolver, 0, Grass, TerrainEditMode.Paint);

        stroke.Begin(1, 1);
        stroke.Continue(3, 1);
        var result = stroke.Continue(2, 1);

        Assert.Equal(new TerrainEditResult(true, false, null), result);
        Assert.Equal(new MapCoordinate(2, 1), stroke.PreviousSample);
        Assert.Equal(3, stroke.IntentCount);
        Assert.All(
            new[] { 6, 7, 8 },
            index => Assert.True(stroke.TryGetIntent(index, out var intent) && intent == Grass));
    }

    [Fact]
    public void Continue_RepeatedSample_LeavesDocumentAndIntentUnchanged()
    {
        var (_, resolver) = Catalog();
        var document = MapDocument.Create(5, 5);
        var stroke = new TerrainMapEditStroke(document, resolver, 0, Grass, TerrainEditMode.Paint);

        stroke.Begin(2, 2);
        var snapshot = Enumerable.Range(0, 25).Select(i => document.GetTile(i).GetLayer(0)).ToArray();

        var result = stroke.Continue(2, 2);

        Assert.Equal(new TerrainEditResult(true, false, null), result);
        Assert.Equal(snapshot, Enumerable.Range(0, 25).Select(i => document.GetTile(i).GetLayer(0)).ToArray());
        Assert.Equal(1, stroke.IntentCount);
    }

    [Fact]
    public void Continue_InvalidCoordinate_DoesNotAlterStrokeState()
    {
        var (_, resolver) = Catalog();
        var document = MapDocument.Create(3, 3);
        var stroke = new TerrainMapEditStroke(document, resolver, 0, Grass, TerrainEditMode.Paint);

        stroke.Begin(1, 1);
        var snapshot = Enumerable.Range(0, 9).Select(i => document.GetTile(i).GetLayer(0)).ToArray();

        var result = stroke.Continue(-1, 1);

        Assert.Equal(new TerrainEditResult(true, false, null), result);
        Assert.True(stroke.IsActive);
        Assert.Equal(new MapCoordinate(1, 1), stroke.PreviousSample);
        Assert.Equal(snapshot, Enumerable.Range(0, 9).Select(i => document.GetTile(i).GetLayer(0)).ToArray());
        Assert.Equal(1, stroke.IntentCount);
        Assert.True(stroke.TryGetIntent(4, out var intent));
        Assert.Equal(Grass, intent);
        Assert.False(stroke.IsVisited(0));
    }

    [Fact]
    public void Complete_EmitsOneRowMajorDeltaPerChangedCell()
    {
        var (_, resolver) = Catalog();
        var document = MapDocument.Create(5, 5);
        var stroke = new TerrainMapEditStroke(document, resolver, 0, Grass, TerrainEditMode.Paint);

        stroke.Begin(2, 2);
        stroke.Continue(2, 3);

        var changes = stroke.Complete();

        Assert.False(stroke.IsActive);
        Assert.Equal(2, changes.Count);
        Assert.Equal(new MapLayerChange(2, 2, 0, default, Tile(0, 1)), changes[0]);
        Assert.Equal(new MapLayerChange(2, 3, 0, default, Tile(0, 1)), changes[1]);
    }

    [Fact]
    public void Complete_ZeroDeltaGestureEmitsNoChanges()
    {
        var (_, resolver) = Catalog();
        var document = MapDocument.Create(3, 3);
        var stroke = new TerrainMapEditStroke(document, resolver, 0, Grass, TerrainEditMode.Erase);

        stroke.Begin(1, 1);
        var changes = stroke.Complete();

        Assert.Empty(changes);
        Assert.False(stroke.IsActive);
    }

    [Fact]
    public void Cancel_RestoresPreBeginValues()
    {
        var (_, resolver) = Catalog();
        var document = MapDocument.Create(5, 5);
        var stroke = new TerrainMapEditStroke(document, resolver, 0, Grass, TerrainEditMode.Paint);

        stroke.Begin(2, 2);
        stroke.Continue(2, 3);
        stroke.Cancel();

        Assert.False(stroke.IsActive);
        Assert.All(
            Enumerable.Range(0, 25),
            index => Assert.Equal(default, document.GetTile(index).GetLayer(0)));
    }

    [Fact]
    public void Cancel_RepeatHaloRepairs_RestoresOriginalBeforeValue()
    {
        var (_, resolver) = Catalog();
        var document = MapDocument.Create(3, 3);
        var stroke = new TerrainMapEditStroke(document, resolver, 0, Grass, TerrainEditMode.Paint);

        stroke.Begin(1, 1);
        stroke.Continue(2, 1);
        Assert.Equal(Tile(0, 11), document[1, 1].GetLayer(0));

        stroke.Cancel();

        Assert.Equal(default, document[1, 1].GetLayer(0));
        Assert.Equal(default, document[2, 1].GetLayer(0));
    }

    [Fact]
    public void Complete_RepeatHaloRepairs_StoresSingleDeltaWithOriginalBefore()
    {
        var (_, resolver) = Catalog();
        var document = MapDocument.Create(3, 3);
        var stroke = new TerrainMapEditStroke(document, resolver, 0, Grass, TerrainEditMode.Paint);

        stroke.Begin(1, 1);
        stroke.Continue(2, 1);

        var changes = stroke.Complete();

        Assert.Equal(2, changes.Count);
        Assert.Equal(new MapLayerChange(1, 1, 0, default, Tile(0, 11)), changes[0]);
        Assert.Equal(new MapLayerChange(2, 1, 0, default, Tile(0, 1)), changes[1]);
    }

    [Fact]
    public void Continue_LateResolutionFailure_RestoresWholeStroke()
    {
        var (_, inner) = Catalog();
        var document = MapDocument.Create(5, 5);
        var resolver = new FailingAfterFirstResolver(inner);
        var stroke = new TerrainMapEditStroke(document, resolver, 0, Grass, TerrainEditMode.Paint);

        var begin = stroke.Begin(1, 1);
        Assert.True(begin.IsActive);
        Assert.True(begin.Changed);
        Assert.Equal(Tile(0, 1), document[1, 1].GetLayer(0));

        var result = stroke.Continue(2, 1);

        Assert.Equal(new TerrainEditResult(false, false, new TerrainResolutionFailure(null, 0, 0, "Simulated late failure.")), result);
        Assert.False(stroke.IsActive);
        Assert.Equal(2, resolver.Calls);
        Assert.All(
            Enumerable.Range(0, 25),
            index => Assert.Equal(default, document.GetTile(index).GetLayer(0)));
        Assert.False(stroke.TryGetIntent(5, out _));
        Assert.Throws<InvalidOperationException>(() => stroke.Continue(3, 1));
        Assert.Throws<InvalidOperationException>(() => stroke.Complete());
        Assert.Throws<InvalidOperationException>(() => stroke.Cancel());
    }

    [Fact]
    public void Begin_ResolutionFailure_LeavesDocumentUntouched()
    {
        var (_, inner) = Catalog();
        var document = MapDocument.Create(3, 3);
        var resolver = new FailingAfterFirstResolver(inner, failAfterCalls: 0);
        var stroke = new TerrainMapEditStroke(document, resolver, 0, Grass, TerrainEditMode.Paint);

        var result = stroke.Begin(1, 1);

        Assert.Equal(new TerrainEditResult(false, false, new TerrainResolutionFailure(null, 0, 0, "Simulated late failure.")), result);
        Assert.False(stroke.IsActive);
        Assert.All(
            Enumerable.Range(0, 9),
            index => Assert.Equal(default, document.GetTile(index).GetLayer(0)));
        Assert.Equal(0, stroke.IntentCount);
    }

    [Fact]
    public void Begin_AfterCancel_BehavesLikeFreshStroke()
    {
        var (_, resolver) = Catalog();
        var document = MapDocument.Create(3, 3);
        var stroke = new TerrainMapEditStroke(document, resolver, 0, Grass, TerrainEditMode.Paint);

        stroke.Begin(1, 1);
        stroke.Continue(2, 1);
        stroke.Cancel();

        Assert.False(stroke.IsActive);
        Assert.Equal(0, stroke.IntentCount);
        Assert.All(
            Enumerable.Range(0, 9),
            index => Assert.Equal(default, document.GetTile(index).GetLayer(0)));

        var result = stroke.Begin(0, 0);

        Assert.Equal(new TerrainEditResult(true, true, null), result);
        Assert.Equal(Tile(0, 1), document[0, 0].GetLayer(0));
        Assert.Equal(default, document[1, 1].GetLayer(0));
        Assert.Equal(default, document[2, 1].GetLayer(0));
        Assert.Equal(1, stroke.IntentCount);
        Assert.True(stroke.TryGetIntent(0, out var intent));
        Assert.Equal(Grass, intent);
    }

    [Fact]
    public void Begin_AfterCancel_OnCancelledCell_RepaintsCell()
    {
        var (_, resolver) = Catalog();
        var document = MapDocument.Create(3, 3);
        var stroke = new TerrainMapEditStroke(document, resolver, 0, Grass, TerrainEditMode.Paint);

        stroke.Begin(1, 1);
        stroke.Cancel();

        var result = stroke.Begin(1, 1);

        Assert.Equal(new TerrainEditResult(true, true, null), result);
        Assert.Equal(Tile(0, 1), document[1, 1].GetLayer(0));
        Assert.Equal(1, stroke.IntentCount);
        Assert.True(stroke.TryGetIntent(4, out var intent));
        Assert.Equal(Grass, intent);
    }

    [Fact]
    public void Erase_RecognizedCell_ErasesAndRepairsNeighbors()
    {
        var (_, resolver) = Catalog();
        var document = MapDocument.Create(3, 3);
        document.SetLayer(1, 1, 0, Tile(0, 11));
        document.SetLayer(2, 1, 0, Tile(0, 1));
        var stroke = new TerrainMapEditStroke(document, resolver, 0, Grass, TerrainEditMode.Erase);

        var result = stroke.Begin(2, 1);

        Assert.Equal(new TerrainEditResult(true, true, null), result);
        Assert.Equal(default, document[2, 1].GetLayer(0));
        Assert.Equal(Tile(0, 1), document[1, 1].GetLayer(0));
        Assert.True(stroke.TryGetIntent(5, out var intent));
        Assert.Null(intent);
    }

    [Fact]
    public void Erase_ThenPaint_SameCell_UpdatesIntentAndDocument()
    {
        var (_, resolver) = Catalog();
        var document = MapDocument.Create(3, 3);
        document.SetLayer(1, 1, 0, Tile(0, 1));
        var stroke = new TerrainMapEditStroke(document, resolver, 0, Grass, TerrainEditMode.Paint);

        stroke.Begin(1, 1);
        var eraseStroke = new TerrainMapEditStroke(document, resolver, 0, Grass, TerrainEditMode.Erase);
        eraseStroke.Begin(1, 1);
        Assert.Equal(default, document[1, 1].GetLayer(0));

        var paintStroke = new TerrainMapEditStroke(document, resolver, 0, Grass, TerrainEditMode.Paint);
        paintStroke.Begin(1, 1);
        Assert.Equal(Tile(0, 1), document[1, 1].GetLayer(0));
        var changes = paintStroke.Complete();
        Assert.Equal(new[] { new MapLayerChange(1, 1, 0, default, Tile(0, 1)) }, changes);
        Assert.True(paintStroke.TryGetIntent(4, out var intent));
        Assert.Equal(Grass, intent);
    }

    [Fact]
    public void Erase_UnrecognizedCell_IsNoOpButGestureStaysActive()
    {
        var (_, resolver) = Catalog();
        var document = MapDocument.Create(3, 3);
        document.SetLayer(0, 1, 0, Tile(1, 1));
        document.SetLayer(1, 1, 0, Tile(9, 9));
        var stroke = new TerrainMapEditStroke(document, resolver, 0, Grass, TerrainEditMode.Erase);

        var result = stroke.Begin(1, 1);

        Assert.Equal(new TerrainEditResult(true, false, null), result);
        Assert.Equal(Tile(9, 9), document[1, 1].GetLayer(0));
        Assert.Equal(Tile(1, 1), document[0, 1].GetLayer(0));
        Assert.Equal(0, stroke.IntentCount);

        var continued = stroke.Continue(0, 1);

        Assert.Equal(new TerrainEditResult(true, true, null), continued);
        Assert.Equal(default, document[0, 1].GetLayer(0));
        Assert.Equal(1, stroke.IntentCount);
    }

    [Fact]
    public void LongStroke_PaintsFullMapInOneGesture()
    {
        var catalog = new TerrainCatalog(
            new[] { TerrainCatalogFixture.Terrain(Grass, "Grass") },
            new[] { TerrainCatalogFixture.Graphic(0, 1, TerrainCatalogFixture.Solid(Grass)) });
        var index = TerrainCatalogValidator.Validate(catalog).Index!;
        var resolver = new TerrainMapResolver(index);
        var document = MapDocument.Create(100, 100);
        var stroke = new TerrainMapEditStroke(document, resolver, 0, Grass, TerrainEditMode.Paint);

        Assert.True(stroke.Begin(0, 0).IsActive);
        for (var y = 0; y < 100; y++)
        {
            for (var step = y == 0 ? 1 : 0; step < 100; step++)
            {
                var x = y % 2 == 0 ? step : 99 - step;
                Assert.True(stroke.Continue(x, y).IsActive);
            }

            Assert.True(stroke.IsActive);
        }

        var changes = stroke.Complete();

        Assert.False(stroke.IsActive);
        Assert.Equal(10_000, changes.Count);
        Assert.All(
            Enumerable.Range(0, 10_000),
            tileIndex => Assert.Equal(Tile(0, 1), document.GetTile(tileIndex).GetLayer(0)));
    }
}
