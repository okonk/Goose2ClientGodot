using System;
using System.Collections.Generic;
using System.Linq;
using MapEditor.Core;
using Xunit;

namespace MapEditor.Core.Tests;

public class TerrainMapResolverTests
{
    private static readonly Guid Grass = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Water = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    private static readonly (TerrainPeer Peer, Guid Id)[] Orientations =
    [
        (TerrainPeer.North, new Guid("11111111-0000-0000-0000-000000000001")),
        (TerrainPeer.East, new Guid("22222222-0000-0000-0000-000000000002")),
        (TerrainPeer.South, new Guid("33333333-0000-0000-0000-000000000003")),
        (TerrainPeer.West, new Guid("44444444-0000-0000-0000-000000000004")),
        (TerrainPeer.NorthEast, new Guid("55555555-0000-0000-0000-000000000005")),
        (TerrainPeer.SouthEast, new Guid("66666666-0000-0000-0000-000000000006")),
        (TerrainPeer.SouthWest, new Guid("77777777-0000-0000-0000-000000000007")),
        (TerrainPeer.NorthWest, new Guid("88888888-0000-0000-0000-000000000008"))
    ];

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

    private static (int Dx, int Dy) OffsetOf(TerrainPeer peer)
        => peer switch
        {
            TerrainPeer.North => (0, -1),
            TerrainPeer.East => (1, 0),
            TerrainPeer.South => (0, 1),
            TerrainPeer.West => (-1, 0),
            TerrainPeer.NorthEast => (1, -1),
            TerrainPeer.SouthEast => (1, 1),
            TerrainPeer.SouthWest => (-1, 1),
            _ => (-1, -1)
        };

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

    private static (TerrainCatalogIndex Index, TerrainMapResolver Resolver) OrientationCatalog()
    {
        var graphics = new List<(int Sheet, int Graphic, TerrainPattern Pattern)>
        {
            (0, 1, Pattern(Grass)),
            (1, 1, Pattern(Water)),
            (0, 10, Pattern(Grass,
                (TerrainPeer.North, Orientations[0].Id),
                (TerrainPeer.East, Orientations[1].Id),
                (TerrainPeer.South, Orientations[2].Id),
                (TerrainPeer.West, Orientations[3].Id),
                (TerrainPeer.NorthEast, Orientations[4].Id),
                (TerrainPeer.SouthEast, Orientations[5].Id),
                (TerrainPeer.SouthWest, Orientations[6].Id),
                (TerrainPeer.NorthWest, Orientations[7].Id))),
            (0, 20, Pattern(Grass,
                (TerrainPeer.North, Water), (TerrainPeer.East, Water), (TerrainPeer.South, Water), (TerrainPeer.West, Water),
                (TerrainPeer.NorthEast, Water), (TerrainPeer.SouthEast, Water), (TerrainPeer.SouthWest, Water), (TerrainPeer.NorthWest, Water))),
            (0, 21, Pattern(Grass, (TerrainPeer.East, Water), (TerrainPeer.South, Water))),
            (0, 22, Pattern(Grass, (TerrainPeer.West, Water), (TerrainPeer.East, Water))),
            (0, 23, Pattern(Grass, (TerrainPeer.North, Water), (TerrainPeer.South, Water)))
        };

        for (var i = 0; i < Orientations.Length; i++)
        {
            var (peer, id) = Orientations[i];
            graphics.Add((0, 2 + i, Pattern(Grass, (peer, id))));
            graphics.Add((1, 2 + i, Pattern(id)));
        }

        var index = Index(graphics.ToArray());
        return (index, new TerrainMapResolver(index));
    }

    [Fact]
    public void GetLogicalCenter_DefaultSheetZeroAndUnrecognizedGraphicsReturnNull()
    {
        var resolver = new TerrainMapResolver(Index((0, 1, Pattern(Grass))));

        Assert.Null(resolver.GetLogicalCenter(default));
        Assert.Null(resolver.GetLogicalCenter(Tile(3, 0)));
        Assert.Null(resolver.GetLogicalCenter(Tile(9, 9)));
    }

    [Fact]
    public void GetLogicalCenter_RecognizedGraphicReturnsItsCenter()
    {
        var resolver = new TerrainMapResolver(Index((0, 1, Pattern(Grass, (TerrainPeer.North, Water)))));

        Assert.Equal(Grass, resolver.GetLogicalCenter(Tile(0, 1)));
    }

    [Fact]
    public void Resolve_UsesAllEightExpectedCoordinates()
    {
        var (index, resolver) = OrientationCatalog();
        var document = MapDocument.Create(5, 5);
        var offsets = new[] { (0, -1), (1, 0), (0, 1), (-1, 0), (1, -1), (1, 1), (-1, 1), (-1, -1) };
        for (var i = 0; i < offsets.Length; i++)
        {
            var (dx, dy) = offsets[i];
            document.SetLayer(2 + dx, 2 + dy, 0, Tile(1, 2 + i));
        }

        var patch = Resolve(resolver, document, 0, (2, 2, Grass));

        Assert.Equal(9, patch.Count);
        Assert.Equal(Tile(0, 10), patch.Changes[12]);
        Assert.Equal(Tile(1, 2), patch.Changes[7]);
        Assert.Equal(Tile(1, 3), patch.Changes[13]);
        Assert.Equal(Tile(1, 4), patch.Changes[17]);
        Assert.Equal(Tile(1, 5), patch.Changes[11]);
        Assert.Equal(Tile(1, 6), patch.Changes[8]);
        Assert.Equal(Tile(1, 7), patch.Changes[18]);
        Assert.Equal(Tile(1, 8), patch.Changes[16]);
        Assert.Equal(Tile(1, 9), patch.Changes[6]);
        Assert.True(index.TryGetGraphic(new TerrainGraphicReference(0, 10), out var graphic));
        Assert.Equal(Pattern(Grass,
            (TerrainPeer.North, Orientations[0].Id),
            (TerrainPeer.East, Orientations[1].Id),
            (TerrainPeer.South, Orientations[2].Id),
            (TerrainPeer.West, Orientations[3].Id),
            (TerrainPeer.NorthEast, Orientations[4].Id),
            (TerrainPeer.SouthEast, Orientations[5].Id),
            (TerrainPeer.SouthWest, Orientations[6].Id),
            (TerrainPeer.NorthWest, Orientations[7].Id)), graphic.Pattern);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void Resolve_SingleNeighborSelectsMatchingOrientation(int direction)
    {
        var (index, resolver) = OrientationCatalog();
        var document = MapDocument.Create(5, 5);
        var (dx, dy) = OffsetOf(Orientations[direction].Peer);
        document.SetLayer(2 + dx, 2 + dy, 0, Tile(1, 2 + direction));

        var patch = Resolve(resolver, document, 0, (2, 2, Grass));

        Assert.Equal(Tile(0, 2 + direction), patch.Changes[12]);
    }

    [Fact]
    public void Resolve_BoundaryClipsNeighborsToNone()
    {
        var (index, resolver) = OrientationCatalog();
        var water = Tile(1, 1);

        var corner = MapDocument.Create(3, 3);
        corner.SetLayer(1, 0, 0, water);
        corner.SetLayer(0, 1, 0, water);
        Assert.Equal(Tile(0, 21), Resolve(resolver, corner, 0, (0, 0, Grass)).Changes[0]);

        var topEdge = MapDocument.Create(3, 3);
        topEdge.SetLayer(0, 0, 0, water);
        topEdge.SetLayer(2, 0, 0, water);
        Assert.Equal(Tile(0, 22), Resolve(resolver, topEdge, 0, (1, 0, Grass)).Changes[1]);

        var bottomEdge = MapDocument.Create(3, 3);
        bottomEdge.SetLayer(0, 2, 0, water);
        bottomEdge.SetLayer(2, 2, 0, water);
        Assert.Equal(Tile(0, 22), Resolve(resolver, bottomEdge, 0, (1, 2, Grass)).Changes[7]);

        var rightEdge = MapDocument.Create(3, 3);
        rightEdge.SetLayer(2, 0, 0, water);
        rightEdge.SetLayer(2, 2, 0, water);
        Assert.Equal(Tile(0, 23), Resolve(resolver, rightEdge, 0, (2, 1, Grass)).Changes[5]);
    }

    [Fact]
    public void Resolve_ExactTransitionBeatsWrongNameAndGeneric()
    {
        var resolver = new TerrainMapResolver(Index(
            (0, 1, Pattern(Grass, (TerrainPeer.North, Water))),
            (0, 2, Pattern(Grass, (TerrainPeer.North, Grass))),
            (0, 3, Pattern(Grass)),
            (1, 1, Pattern(Water))));
        var document = MapDocument.Create(3, 3);
        document.SetLayer(1, 0, 0, Tile(1, 1));

        var patch = Resolve(resolver, document, 0, (1, 1, Grass));

        Assert.Equal(Tile(0, 1), patch.Changes[4]);
    }

    [Fact]
    public void Resolve_FallsBackToGenericWhenNoExactPatternExists()
    {
        var resolver = new TerrainMapResolver(Index(
            (0, 1, Pattern(Grass, (TerrainPeer.North, Water))),
            (0, 2, Pattern(Grass)),
            (1, 1, Pattern(Water))));
        var document = MapDocument.Create(3, 3);
        document.SetLayer(2, 0, 0, Tile(1, 1));

        var patch = Resolve(resolver, document, 0, (1, 1, Grass));

        Assert.Equal(Tile(0, 2), patch.Changes[4]);
    }

    [Fact]
    public void Resolve_TreatsEmptyAndUnrecognizedNeighborsAsNone()
    {
        var resolver = new TerrainMapResolver(Index(
            (0, 1, Pattern(Grass)),
            (0, 2, Pattern(Grass, (TerrainPeer.North, Water))),
            (1, 1, Pattern(Water))));
        var document = MapDocument.Create(3, 3);
        document.SetLayer(0, 0, 0, Tile(9, 9));

        var patch = Resolve(resolver, document, 0, (1, 1, Grass));

        Assert.Equal(Tile(0, 1), patch.Changes[4]);
    }

    [Fact]
    public void Resolve_OverridesOverrideDocumentCenters()
    {
        var resolver = new TerrainMapResolver(Index(
            (0, 1, Pattern(Grass, (TerrainPeer.North, Grass))),
            (0, 2, Pattern(Grass, (TerrainPeer.North, Water))),
            (1, 1, Pattern(Water))));
        var document = MapDocument.Create(3, 3);
        document.SetLayer(1, 0, 0, Tile(1, 1));

        var patch = Resolve(resolver, document, 0, (1, 1, Grass), (1, 0, Grass));

        Assert.Equal(Tile(0, 1), patch.Changes[4]);
    }

    [Fact]
    public void Resolve_TiedDistinctPatternsAreStableAcrossCatalogOrder()
    {
        var graphics = new (int Sheet, int Graphic, TerrainPattern Pattern)[]
        {
            (0, 1, Pattern(Grass, (TerrainPeer.North, Water))),
            (0, 2, Pattern(Grass, (TerrainPeer.East, Water))),
            (1, 1, Pattern(Water))
        };

        var document = MapDocument.Create(3, 3);
        document.SetLayer(1, 0, 0, Tile(1, 1));
        document.SetLayer(2, 1, 0, Tile(1, 1));

        var baseline = Resolve(new TerrainMapResolver(Index(graphics)), document, 0, (1, 1, Grass));
        var shuffled = Resolve(new TerrainMapResolver(Index(graphics.Reverse().ToArray())), document, 0, (1, 1, Grass));

        Assert.Equal(baseline.Changes, shuffled.Changes);
        Assert.Contains(baseline.Changes[4], new[] { Tile(0, 1), Tile(0, 2) });
    }

    [Fact]
    public void Resolve_DuplicateVariants_PreservesPatternChoice()
    {
        var index = Index(
            (0, 1, Pattern(Grass, (TerrainPeer.North, Water))),
            (0, 2, Pattern(Grass, (TerrainPeer.North, Water))),
            (1, 1, Pattern(Grass, (TerrainPeer.East, Water))),
            (2, 1, Pattern(Water)));
        var resolver = new TerrainMapResolver(index);
        var water = Tile(2, 1);
        var patterns = new HashSet<TerrainPattern>();

        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                var document = MapDocument.Create(10, 5);
                document.SetLayer(x + 1, y, 0, water);
                document.SetLayer(x + 2, y + 1, 0, water);
                var patch = Resolve(resolver, document, 0, (x + 1, y + 1, Grass));
                var selected = patch.Changes[(y + 1) * 10 + x + 1];
                Assert.True(index.TryGetGraphic(new TerrainGraphicReference(selected.Sheet, selected.Graphic), out var graphic));
                patterns.Add(graphic.Pattern);
            }
        }

        Assert.Contains(Pattern(Grass, (TerrainPeer.North, Water)), patterns);
        Assert.Contains(Pattern(Grass, (TerrainPeer.East, Water)), patterns);
    }

    [Fact]
    public void Resolve_CoordinatesMapToRowMajorIndices()
    {
        var resolver = new TerrainMapResolver(Index((0, 1, Pattern(Grass))));
        var document = MapDocument.Create(5, 5);

        var patch = Resolve(resolver, document, 3, (1, 2, Grass), (4, 0, Grass));

        Assert.Equal(3, patch.Layer);
        Assert.Equal(2, patch.Count);
        Assert.Equal(Tile(0, 1), patch.Changes[11]);
        Assert.Equal(Tile(0, 1), patch.Changes[4]);
    }

    [Fact]
    public void Resolve_ShuffledCatalogOrderProducesIdenticalPatch()
    {
        var graphics = new (int Sheet, int Graphic, TerrainPattern Pattern)[]
        {
            (0, 1, Pattern(Grass)),
            (0, 2, Pattern(Grass, (TerrainPeer.North, Water))),
            (0, 3, Pattern(Grass, (TerrainPeer.North, Grass))),
            (0, 4, Pattern(Grass, (TerrainPeer.East, Water))),
            (0, 5, Pattern(Grass, (TerrainPeer.South, Water))),
            (0, 6, Pattern(Grass, (TerrainPeer.West, Water))),
            (0, 7, Pattern(Grass, (TerrainPeer.NorthEast, Water))),
            (0, 8, Pattern(Grass, (TerrainPeer.SouthEast, Water))),
            (0, 9, Pattern(Grass, (TerrainPeer.SouthWest, Water))),
            (0, 10, Pattern(Grass, (TerrainPeer.NorthWest, Water))),
            (1, 1, Pattern(Water))
        };

        var document = MapDocument.Create(5, 5);
        var water = Tile(1, 1);
        document.SetLayer(2, 1, 0, water);
        document.SetLayer(3, 2, 0, water);
        document.SetLayer(1, 3, 0, water);
        document.SetLayer(4, 2, 0, water);

        var baseline = Resolve(new TerrainMapResolver(Index(graphics)), document, 0, (2, 2, Grass), (3, 2, Grass), (2, 3, Grass));
        var shuffled = Resolve(new TerrainMapResolver(Index(graphics.Reverse().ToArray())), document, 0, (2, 2, Grass), (3, 2, Grass), (2, 3, Grass));

        Assert.Equal(6, baseline.Count);
        Assert.Equal(baseline.Changes, shuffled.Changes);
    }

    [Fact]
    public void Resolve_NeverChoosesHigherScoringWrongCenter()
    {
        var grass = new TerrainDefinition(Grass, "Grass", null);
        var water = new TerrainDefinition(Water, "Water", null);
        var correct = new TerrainGraphicDefinition(new TerrainGraphicReference(0, 1), Pattern(Grass, (TerrainPeer.North, Water)));
        var wrong = new TerrainGraphicDefinition(new TerrainGraphicReference(1, 1), Pattern(Water, (TerrainPeer.North, Water)));
        var waterGraphic = new TerrainGraphicDefinition(new TerrainGraphicReference(2, 1), Pattern(Water));
        var index = new TerrainCatalogIndex(
            new Dictionary<Guid, TerrainDefinition> { [Grass] = grass, [Water] = water },
            new Dictionary<TerrainGraphicReference, TerrainGraphicDefinition>
            {
                [correct.Reference] = correct,
                [wrong.Reference] = wrong,
                [waterGraphic.Reference] = waterGraphic
            },
            new Dictionary<Guid, IReadOnlyList<TerrainPatternCandidateGroup>>
            {
                [Grass] =
                [
                    new(correct.Pattern, new[] { correct }),
                    new(wrong.Pattern, new[] { wrong })
                ],
                [Water] = [ new(waterGraphic.Pattern, new[] { waterGraphic }) ]
            },
            new Dictionary<Guid, IReadOnlyList<TerrainGraphicDefinition>> { [Grass] = new[] { correct }, [Water] = new[] { waterGraphic } },
            new Dictionary<Guid, TerrainColor> { [Grass] = grass.DisplayColor, [Water] = water.DisplayColor });

        var resolver = new TerrainMapResolver(index);
        var desired = Pattern(Grass, (TerrainPeer.North, Water));

        var x = -1;
        var y = -1;
        for (var yy = 1; yy < 32 && x < 0; yy++)
        {
            for (var xx = 0; xx < 32; xx++)
            {
                if (TerrainStableHash.HashPattern(Grass, xx, yy, desired) % 2 == 1)
                {
                    x = xx;
                    y = yy;
                    break;
                }
            }
        }

        Assert.True(x >= 0);
        var document = MapDocument.Create(32, 32);
        document.SetLayer(x, y - 1, 0, Tile(1, 1));

        var patch = Resolve(resolver, document, 0, (x, y, Grass));

        Assert.Equal(Tile(0, 1), patch.Changes[y * 32 + x]);
        Assert.Equal(default, document[x, y].GetLayer(0));
    }

    [Fact]
    public void Resolve_UnknownRequestedCenterFailsWithoutMutatingDocument()
    {
        var resolver = new TerrainMapResolver(Index((0, 1, Pattern(Grass))));
        var document = MapDocument.Create(3, 3);
        var unknown = Guid.NewGuid();

        var ok = resolver.TryResolvePatch(
            document,
            0,
            new Dictionary<int, Guid?> { [4] = unknown },
            new[] { 4 },
            out var patch,
            out var failure);

        Assert.False(ok);
        Assert.Equal(unknown, failure!.TerrainId);
        Assert.Equal(1, failure.X);
        Assert.Equal(1, failure.Y);
        Assert.False(string.IsNullOrWhiteSpace(failure.Message));
        Assert.Empty(patch.Changes);
        Assert.Equal(default, document[1, 1].GetLayer(0));
    }

    [Fact]
    public void Resolve_EmptyCandidatePoolFailsWithoutMutatingDocument()
    {
        var bare = new TerrainDefinition(Guid.Parse("cccccccc-0000-0000-0000-000000000009"), "Bare", null);
        var grass = new TerrainDefinition(Grass, "Grass", null);
        var grassGraphic = new TerrainGraphicDefinition(new TerrainGraphicReference(0, 1), Pattern(Grass));
        var index = new TerrainCatalogIndex(
            new Dictionary<Guid, TerrainDefinition> { [Grass] = grass, [bare.Id] = bare },
            new Dictionary<TerrainGraphicReference, TerrainGraphicDefinition> { [grassGraphic.Reference] = grassGraphic },
            new Dictionary<Guid, IReadOnlyList<TerrainPatternCandidateGroup>>
            {
                [Grass] = [ new(grassGraphic.Pattern, new[] { grassGraphic }) ],
                [bare.Id] = []
            },
            new Dictionary<Guid, IReadOnlyList<TerrainGraphicDefinition>> { [Grass] = new[] { grassGraphic }, [bare.Id] = [] },
            new Dictionary<Guid, TerrainColor> { [Grass] = grass.DisplayColor, [bare.Id] = bare.DisplayColor });
        var resolver = new TerrainMapResolver(index);
        var document = MapDocument.Create(3, 3);

        var ok = resolver.TryResolvePatch(
            document,
            0,
            new Dictionary<int, Guid?> { [4] = bare.Id },
            new[] { 4 },
            out var patch,
            out var failure);

        Assert.False(ok);
        Assert.Equal(bare.Id, failure!.TerrainId);
        Assert.Equal(1, failure.X);
        Assert.Equal(1, failure.Y);
        Assert.Empty(patch.Changes);
        Assert.Equal(default, document[1, 1].GetLayer(0));
    }

    [Fact]
    public void Resolve_MissingRequestedCenterFails()
    {
        var resolver = new TerrainMapResolver(Index((0, 1, Pattern(Grass))));
        var document = MapDocument.Create(3, 3);

        var ok = resolver.TryResolvePatch(
            document,
            0,
            new Dictionary<int, Guid?>(),
            new[] { 4 },
            out var patch,
            out var failure);

        Assert.False(ok);
        Assert.Null(failure!.TerrainId);
        Assert.Equal(1, failure.X);
        Assert.Equal(1, failure.Y);
        Assert.Empty(patch.Changes);
    }
}
