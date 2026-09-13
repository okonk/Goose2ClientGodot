using System;
using System.Collections.Generic;
using System.Linq;
using MapEditor.Core;
using Xunit;

namespace MapEditor.Core.Tests;

public class TerrainStrokeResolverTests
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

    private static TerrainResolvedPatch Resolve(
        TerrainMapResolver resolver,
        MapDocument document,
        int layer,
        params (int X, int Y, Guid? Center)[] intents)
    {
        var overrides = new Dictionary<int, Guid?>();
        var indices = new List<int>();
        foreach (var (x, y, center) in intents)
        {
            var index = y * document.Width + x;
            overrides[index] = center;
            indices.Add(index);
        }

        Assert.True(
            resolver.TryResolvePatch(document, layer, overrides, indices, out var patch, out var failure),
            failure?.Message);

        return patch;
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

    [Fact]
    public void ResolvePatch_SinglePaint_ChangesOnlyThePaintedCell()
    {
        var (_, resolver) = Catalog();
        var document = MapDocument.Create(3, 3);

        var patch = Resolve(resolver, document, 0, (1, 1, Grass));

        Assert.Single(patch.Changes);
        Assert.Equal(Tile(0, 1), patch.Changes[4]);
    }

    [Fact]
    public void ResolvePatch_AdjacentTransitions_RepairsBothSides()
    {
        var (_, resolver) = Catalog();
        var document = MapDocument.Create(3, 3);
        document.SetLayer(0, 1, 0, Tile(0, 1));
        document.SetLayer(1, 1, 0, Tile(1, 1));

        var patch = Resolve(resolver, document, 0, (0, 1, Water), (1, 1, Grass));

        Assert.Equal(2, patch.Count);
        Assert.Equal(Tile(1, 2), patch.Changes[3]);
        Assert.Equal(Tile(0, 2), patch.Changes[4]);
    }

    [Fact]
    public void ResolvePatch_BoundaryRepairsBothSides()
    {
        var (_, resolver) = Catalog();
        var document = MapDocument.Create(3, 3);
        document.SetLayer(0, 1, 0, Tile(1, 1));
        document.SetLayer(2, 1, 0, Tile(0, 1));

        var patch = Resolve(resolver, document, 0, (1, 1, Grass));

        Assert.Equal(3, patch.Count);
        Assert.Equal(Tile(1, 2), patch.Changes[3]);
        Assert.Equal(Tile(0, 12), patch.Changes[4]);
        Assert.Equal(Tile(0, 1), patch.Changes[5]);
    }

    [Fact]
    public void ResolvePatch_CornerPaint_ClipsHaloToMapEdge()
    {
        var (_, resolver) = Catalog();
        var document = MapDocument.Create(3, 3);
        document.SetLayer(1, 0, 0, Tile(1, 1));
        document.SetLayer(0, 1, 0, Tile(1, 1));

        var patch = Resolve(resolver, document, 0, (0, 0, Grass));

        Assert.Equal(3, patch.Count);
        Assert.Equal(Tile(0, 4), patch.Changes[0]);
        Assert.Equal(Tile(1, 3), patch.Changes[1]);
        Assert.Equal(Tile(1, 5), patch.Changes[3]);
    }

    [Fact]
    public void ResolvePatch_MapEdgePaint_ClipsHaloToMapEdge()
    {
        var (_, resolver) = Catalog();
        var document = MapDocument.Create(3, 3);
        document.SetLayer(2, 0, 0, Tile(1, 1));
        document.SetLayer(2, 2, 0, Tile(1, 1));

        var patch = Resolve(resolver, document, 0, (2, 1, Grass));

        Assert.Equal(3, patch.Count);
        Assert.Equal(Tile(0, 8), patch.Changes[5]);
        Assert.Equal(Tile(1, 4), patch.Changes[2]);
        Assert.Equal(Tile(1, 5), patch.Changes[8]);
    }

    [Fact]
    public void ResolvePatch_MultipleDirectCells_AreAllResolvedAndSortedRowMajor()
    {
        var resolver = new TerrainMapResolver(Index((0, 1, Pattern(Grass)), (1, 1, Pattern(Water))));
        var document = MapDocument.Create(5, 5);

        var patch = Resolve(resolver, document, 0, (4, 0, Grass), (1, 2, Grass), (1, 1, Grass));

        Assert.Equal(3, patch.Count);
        Assert.Equal(Tile(0, 1), patch.Changes[4]);
        Assert.Equal(Tile(0, 1), patch.Changes[6]);
        Assert.Equal(Tile(0, 1), patch.Changes[11]);
        Assert.Equal(new[] { 4, 6, 11 }, patch.Changes.Keys);
    }

    [Fact]
    public void ResolvePatch_PaintReplacesUnrecognizedDirectCell()
    {
        var (_, resolver) = Catalog();
        var document = MapDocument.Create(3, 3);
        document.SetLayer(1, 1, 0, Tile(9, 9));

        var patch = Resolve(resolver, document, 0, (1, 1, Grass));

        Assert.Single(patch.Changes);
        Assert.Equal(Tile(0, 1), patch.Changes[4]);
    }

    [Fact]
    public void ResolvePatch_UnrecognizedHalo_IsExcluded()
    {
        var (_, resolver) = Catalog();
        var document = MapDocument.Create(3, 3);
        document.SetLayer(1, 1, 0, Tile(9, 9));
        document.SetLayer(2, 1, 0, Tile(8, 8));

        var patch = Resolve(resolver, document, 0, (1, 1, Grass));

        Assert.Single(patch.Changes);
        Assert.Equal(Tile(0, 1), patch.Changes[4]);
        Assert.False(patch.Changes.ContainsKey(5));
    }

    [Fact]
    public void ResolvePatch_EraseRecognizedCell_EmitsDefaultAndRepairsNeighbors()
    {
        var (_, resolver) = Catalog();
        var document = MapDocument.Create(3, 3);
        document.SetLayer(1, 1, 0, Tile(0, 11));
        document.SetLayer(2, 1, 0, Tile(0, 1));

        var patch = Resolve(resolver, document, 0, (2, 1, null));

        Assert.Equal(2, patch.Count);
        Assert.Equal(default, patch.Changes[5]);
        Assert.Equal(Tile(0, 1), patch.Changes[4]);
    }

    [Fact]
    public void ResolvePatch_EraseEmptyOrUnrecognizedCell_ProducesNoChanges()
    {
        var (_, resolver) = Catalog();

        var empty = MapDocument.Create(3, 3);
        empty.SetLayer(0, 1, 0, Tile(1, 1));
        var emptyPatch = Resolve(resolver, empty, 0, (1, 1, null));
        Assert.Empty(emptyPatch.Changes);

        var manual = MapDocument.Create(3, 3);
        manual.SetLayer(1, 1, 0, Tile(9, 9));
        manual.SetLayer(0, 1, 0, Tile(1, 1));
        var manualPatch = Resolve(resolver, manual, 0, (1, 1, null));
        Assert.Empty(manualPatch.Changes);
    }

    [Fact]
    public void ResolvePatch_LateFailure_ReturnsNoChanges()
    {
        var bareId = Guid.Parse("cccccccc-0000-0000-0000-000000000009");
        var grass = new TerrainDefinition(Grass, "Grass", null);
        var bare = new TerrainDefinition(bareId, "Bare", null);
        var grassGraphic = new TerrainGraphicDefinition(new TerrainGraphicReference(0, 1), Pattern(Grass));
        var index = new TerrainCatalogIndex(
            new Dictionary<Guid, TerrainDefinition> { [Grass] = grass, [bareId] = bare },
            new Dictionary<TerrainGraphicReference, TerrainGraphicDefinition> { [grassGraphic.Reference] = grassGraphic },
            new Dictionary<Guid, IReadOnlyList<TerrainPatternCandidateGroup>>
            {
                [Grass] = [ new(grassGraphic.Pattern, new[] { grassGraphic }) ],
                [bareId] = []
            },
            new Dictionary<Guid, IReadOnlyList<TerrainGraphicDefinition>> { [Grass] = new[] { grassGraphic }, [bareId] = [] },
            new Dictionary<Guid, TerrainColor> { [Grass] = grass.DisplayColor, [bareId] = bare.DisplayColor });
        var resolver = new TerrainMapResolver(index);
        var document = MapDocument.Create(3, 3);
        document.SetLayer(0, 1, 0, Tile(0, 1));

        var ok = resolver.TryResolvePatch(
            document,
            0,
            new Dictionary<int, Guid?> { [4] = Grass, [5] = bareId },
            new[] { 4, 5 },
            out var patch,
            out var failure);

        Assert.False(ok);
        Assert.Equal(bareId, failure!.TerrainId);
        Assert.Equal(2, failure.X);
        Assert.Equal(1, failure.Y);
        Assert.Empty(patch.Changes);
        Assert.Equal(Tile(0, 1), document[0, 1].GetLayer(0));
        Assert.Equal(default, document[1, 1].GetLayer(0));
    }

    [Fact]
    public void ResolvePatch_UnknownTerrainIdLaterInOrder_RejectsWholeRequest()
    {
        var (_, resolver) = Catalog();
        var document = MapDocument.Create(3, 3);
        document.SetLayer(1, 1, 0, Tile(0, 1));
        var unknown = Guid.NewGuid();

        var ok = resolver.TryResolvePatch(
            document,
            0,
            new Dictionary<int, Guid?> { [4] = Grass, [8] = unknown },
            new[] { 4, 8 },
            out var patch,
            out var failure);

        Assert.False(ok);
        Assert.Equal(unknown, failure!.TerrainId);
        Assert.Equal(2, failure.X);
        Assert.Equal(2, failure.Y);
        Assert.Empty(patch.Changes);
        Assert.Equal(Tile(0, 1), document[1, 1].GetLayer(0));

        var haloOnlyOk = resolver.TryResolvePatch(
            document,
            0,
            new Dictionary<int, Guid?> { [4] = Grass, [8] = unknown },
            new[] { 4 },
            out var haloPatch,
            out var haloFailure);

        Assert.False(haloOnlyOk);
        Assert.Equal(unknown, haloFailure!.TerrainId);
        Assert.Empty(haloPatch.Changes);
        Assert.Equal(Tile(0, 1), document[1, 1].GetLayer(0));
    }
}
