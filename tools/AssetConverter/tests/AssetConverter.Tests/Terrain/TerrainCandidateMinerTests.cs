using Goose2.AssetConverter.Terrain;
using MapEditor.Core;
using MapEditor.Core.Terrain;

namespace AssetConverter.Tests.Terrain;

public class TerrainCandidateMinerTests
{
    private const int HoldoutModulo = 5;

    private static readonly TerrainGenerationSettings Settings = new(
        HoldoutModulo: HoldoutModulo,
        MinimumMapSupport: 5,
        MinimumRegionSupport: 8,
        MinimumObservationSupport: 64,
        MinimumDiagonalSupport: 32,
        MinimumEightWayAccuracyGain: 0.05,
        MapMemberCompatibility: 0.70,
        ImageOnlyCompatibility: 0.94,
        MinimumClassificationMargin: 0.05,
        MaximumAmbiguity: 0.10,
        EnabledConfidence: 0.90,
        PendingConfidence: 0.45);

    [Fact]
    public void Mine_CardinallyAdjacentCompatibleVariantsFormFamily()
    {
        using var fixture = CreateAdjacentFixture();
        var corpus = TerrainCorpusLoader.Load(fixture.RepoRoot);
        var result = TerrainCandidateMiner.Mine(corpus, new FakeFeatureSource(
            similarities: new[] { (new TerrainGraphicReference(1, 100), new TerrainGraphicReference(1, 101)) },
            Settings.MapMemberCompatibility,
            candidates: new Dictionary<TerrainGraphicReference, TerrainGraphicReference[]>
            {
                [new TerrainGraphicReference(1, 100)] = new[] { new TerrainGraphicReference(1, 101) },
                [new TerrainGraphicReference(1, 101)] = new[] { new TerrainGraphicReference(1, 100) },
            }));

        var family = Assert.Single(result.Families);
        Assert.Equal(
            new[]
            {
                new TerrainGraphicReference(1, 100),
                new TerrainGraphicReference(1, 101),
            },
            family.Members);
        Assert.Empty(result.RootDiagnostics);
    }

    [Fact]
    public void Mine_TransitiveBridgeProducesOneDeterministicComponent()
    {
        using var fixture = CreateBridgeFixture();
        var corpus = TerrainCorpusLoader.Load(fixture.RepoRoot);
        var result = TerrainCandidateMiner.Mine(corpus, new FakeFeatureSource(
            similarities: new[]
            {
                (new TerrainGraphicReference(1, 100), new TerrainGraphicReference(1, 101)),
                (new TerrainGraphicReference(1, 101), new TerrainGraphicReference(1, 102)),
            },
            Settings.MapMemberCompatibility,
            candidates: new Dictionary<TerrainGraphicReference, TerrainGraphicReference[]>
            {
                [new TerrainGraphicReference(1, 100)] = new[] { new TerrainGraphicReference(1, 101) },
                [new TerrainGraphicReference(1, 101)] = new[] { new TerrainGraphicReference(1, 100), new TerrainGraphicReference(1, 102) },
                [new TerrainGraphicReference(1, 102)] = new[] { new TerrainGraphicReference(1, 101) },
            }));

        var family = Assert.Single(result.Families);
        Assert.Equal(
            new[]
            {
                new TerrainGraphicReference(1, 100),
                new TerrainGraphicReference(1, 101),
                new TerrainGraphicReference(1, 102),
            },
            family.Members);
        Assert.Empty(result.RootDiagnostics);
    }

    [Fact]
    public void Mine_DeceptiveLookalikeWithoutAdjacencyDoesNotJoin()
    {
        var map1 = TerrainFixtureBuilder.FindMapFileName(HoldoutModulo, heldOut: false);
        var map2 = TerrainFixtureBuilder.FindMapFileName(HoldoutModulo, heldOut: false, start: 100);
        var map3 = TerrainFixtureBuilder.FindMapFileName(HoldoutModulo, heldOut: false, start: 200);
        var definition = new TerrainFixtureDefinition();
        definition.AddMap(map1, 2, 2,
            new TerrainPlacement(0, 0, 1, 100),
            new TerrainPlacement(0, 1, 1, 101));
        definition.AddMap(map2, 2, 2,
            new TerrainPlacement(1, 0, 1, 100),
            new TerrainPlacement(1, 1, 1, 101));
        definition.AddMap(map3, 1, 1, new TerrainPlacement(0, 0, 1, 103));
        definition.AddSheet(1, 96, 32,
            new TerrainSheetFrame(100, 0, 0, 32, 32),
            new TerrainSheetFrame(101, 32, 0, 32, 32),
            new TerrainSheetFrame(103, 64, 0, 32, 32));

        using var fixture = TerrainFixtureBuilder.Create(definition);
        fixture.WriteInventory(map1, map2, map3);
        var corpus = TerrainCorpusLoader.Load(fixture.RepoRoot);
        var lookalike = new TerrainGraphicReference(1, 103);
        var result = TerrainCandidateMiner.Mine(corpus, new FakeFeatureSource(
            similarities: new[]
            {
                (new TerrainGraphicReference(1, 100), new TerrainGraphicReference(1, 101)),
                (new TerrainGraphicReference(1, 100), lookalike),
            },
            Settings.MapMemberCompatibility,
            candidates: new Dictionary<TerrainGraphicReference, TerrainGraphicReference[]>
            {
                [new TerrainGraphicReference(1, 100)] = new[] { new TerrainGraphicReference(1, 101), lookalike },
                [new TerrainGraphicReference(1, 101)] = new[] { new TerrainGraphicReference(1, 100) },
            }));

        var family = Assert.Single(result.Families);
        Assert.Equal(
            new[]
            {
                new TerrainGraphicReference(1, 100),
                new TerrainGraphicReference(1, 101),
            },
            family.Members);
        var diagnostic = Assert.Single(result.RootDiagnostics);
        Assert.Equal("insufficient-family-members", diagnostic.Code);
        Assert.Equal("Map-observed reference (1,103) did not join a family with at least two members.", diagnostic.Message);
        Assert.Equal(lookalike, diagnostic.Reference);
        Assert.Null(diagnostic.Mask);
    }

    [Fact]
    public void Mine_DifferentSheetsNeverJoin()
    {
        var map1 = TerrainFixtureBuilder.FindMapFileName(HoldoutModulo, heldOut: false);
        var map2 = TerrainFixtureBuilder.FindMapFileName(HoldoutModulo, heldOut: false, start: 100);
        var definition = new TerrainFixtureDefinition();
        definition.AddMap(map1, 2, 2,
            new TerrainPlacement(0, 0, 1, 100),
            new TerrainPlacement(0, 1, 1, 101),
            new TerrainPlacement(1, 0, 2, 100),
            new TerrainPlacement(1, 1, 2, 101));
        definition.AddMap(map2, 2, 2,
            new TerrainPlacement(0, 0, 2, 100),
            new TerrainPlacement(0, 1, 2, 101),
            new TerrainPlacement(1, 0, 1, 100),
            new TerrainPlacement(1, 1, 1, 101));
        definition.AddSheet(1, 64, 32,
            new TerrainSheetFrame(100, 0, 0, 32, 32),
            new TerrainSheetFrame(101, 32, 0, 32, 32));
        definition.AddSheet(2, 64, 32,
            new TerrainSheetFrame(100, 0, 0, 32, 32),
            new TerrainSheetFrame(101, 32, 0, 32, 32));

        using var fixture = TerrainFixtureBuilder.Create(definition);
        fixture.WriteInventory(map1, map2);
        var corpus = TerrainCorpusLoader.Load(fixture.RepoRoot);
        var result = TerrainCandidateMiner.Mine(corpus, new FakeFeatureSource(
            similarities: new[]
            {
                (new TerrainGraphicReference(1, 100), new TerrainGraphicReference(1, 101)),
                (new TerrainGraphicReference(2, 100), new TerrainGraphicReference(2, 101)),
                (new TerrainGraphicReference(1, 100), new TerrainGraphicReference(2, 100)),
                (new TerrainGraphicReference(1, 101), new TerrainGraphicReference(2, 101)),
            },
            Settings.MapMemberCompatibility,
            candidates: new Dictionary<TerrainGraphicReference, TerrainGraphicReference[]>
            {
                [new TerrainGraphicReference(1, 100)] = new[] { new TerrainGraphicReference(1, 101), new TerrainGraphicReference(2, 100) },
                [new TerrainGraphicReference(1, 101)] = new[] { new TerrainGraphicReference(1, 100), new TerrainGraphicReference(2, 101) },
                [new TerrainGraphicReference(2, 100)] = new[] { new TerrainGraphicReference(2, 101), new TerrainGraphicReference(1, 100) },
                [new TerrainGraphicReference(2, 101)] = new[] { new TerrainGraphicReference(2, 100), new TerrainGraphicReference(1, 101) },
            }));

        Assert.Equal(2, result.Families.Count);
        Assert.Equal(new[] { new TerrainGraphicReference(1, 100), new TerrainGraphicReference(1, 101) }, result.Families[0].Members);
        Assert.Equal(new[] { new TerrainGraphicReference(2, 100), new TerrainGraphicReference(2, 101) }, result.Families[1].Members);
        Assert.Empty(result.RootDiagnostics);
    }

    [Fact]
    public void Mine_BelowCompatibilitySimilarityNeverFormsAnEdge()
    {
        using var fixture = CreateAdjacentFixture();
        var corpus = TerrainCorpusLoader.Load(fixture.RepoRoot);
        var result = TerrainCandidateMiner.Mine(corpus, new FakeFeatureSource(
            similarities: new[] { (new TerrainGraphicReference(1, 100), new TerrainGraphicReference(1, 101)) },
            0.69,
            candidates: new Dictionary<TerrainGraphicReference, TerrainGraphicReference[]>
            {
                [new TerrainGraphicReference(1, 100)] = new[] { new TerrainGraphicReference(1, 101) },
                [new TerrainGraphicReference(1, 101)] = new[] { new TerrainGraphicReference(1, 100) },
            }));

        Assert.Empty(result.Families);
        Assert.Equal(2, result.RootDiagnostics.Count);
        Assert.All(result.RootDiagnostics, diagnostic => Assert.Equal("insufficient-family-members", diagnostic.Code));
    }

    [Fact]
    public void Weighting_HugeRegionAndTinyMapEachContributeOneMapMass()
    {
        var huge = TerrainFixtureBuilder.FindMapFileName(HoldoutModulo, heldOut: false);
        var tiny1 = TerrainFixtureBuilder.FindMapFileName(HoldoutModulo, heldOut: false, start: 100);
        var tiny2 = TerrainFixtureBuilder.FindMapFileName(HoldoutModulo, heldOut: false, start: 200);
        var tiny3 = TerrainFixtureBuilder.FindMapFileName(HoldoutModulo, heldOut: false, start: 300);
        var holdout = TerrainFixtureBuilder.FindMapFileName(HoldoutModulo, heldOut: true);

        var definition = new TerrainFixtureDefinition();
        definition.AddMap(huge, 3, 3,
            new TerrainPlacement(1, 1, 1, 100),
            new TerrainPlacement(1, 0, 1, 101),
            new TerrainPlacement(2, 1, 1, 101),
            new TerrainPlacement(1, 2, 1, 101),
            new TerrainPlacement(0, 1, 1, 101));
        definition.AddMap(tiny1, 1, 1, new TerrainPlacement(0, 0, 1, 100));
        definition.AddMap(tiny2, 2, 1,
            new TerrainPlacement(0, 0, 1, 100),
            new TerrainPlacement(1, 0, 1, 101));
        definition.AddMap(tiny3, 1, 1, new TerrainPlacement(0, 0, 1, 100));
        definition.AddMap(holdout, 1, 1, new TerrainPlacement(0, 0, 1, 100));
        definition.AddSheet(1, 64, 32,
            new TerrainSheetFrame(100, 0, 0, 32, 32),
            new TerrainSheetFrame(101, 32, 0, 32, 32));

        using var fixture = TerrainFixtureBuilder.Create(definition);
        fixture.WriteInventory(huge, tiny1, tiny2, tiny3, holdout);
        var corpus = TerrainCorpusLoader.Load(fixture.RepoRoot);
        var result = TerrainCandidateMiner.Mine(corpus, new FakeFeatureSource(
            similarities: new[] { (new TerrainGraphicReference(1, 100), new TerrainGraphicReference(1, 101)) },
            Settings.MapMemberCompatibility,
            candidates: new Dictionary<TerrainGraphicReference, TerrainGraphicReference[]>
            {
                [new TerrainGraphicReference(1, 100)] = new[] { new TerrainGraphicReference(1, 101) },
                [new TerrainGraphicReference(1, 101)] = new[] { new TerrainGraphicReference(1, 100) },
            }));

        var family = Assert.Single(result.Families);
        Assert.Equal(4, family.MapSupport);
        Assert.Equal(4, family.RegionSupport);
        Assert.Equal(9, family.ObservationSupport);
        Assert.Equal(4, family.DiagonalSupport);
        Assert.Equal(2.7, family.ReferenceWeights[new TerrainGraphicReference(1, 100)]);
        Assert.Equal(1.3, family.ReferenceWeights[new TerrainGraphicReference(1, 101)]);

        var masks = family.WeightedMasks[new TerrainGraphicReference(1, 100)];
        Assert.Equal(0.2, masks[TerrainMasks.North | TerrainMasks.East | TerrainMasks.South | TerrainMasks.West]);
        Assert.Equal(0.5, masks[TerrainMasks.East]);
        Assert.Equal(2.0, masks[0]);
    }

    [Fact]
    public void Weighting_DisconnectedRegionsSplitMapMassEqually()
    {
        var map1 = TerrainFixtureBuilder.FindMapFileName(HoldoutModulo, heldOut: false);
        var map2 = TerrainFixtureBuilder.FindMapFileName(HoldoutModulo, heldOut: false, start: 100);

        var definition = new TerrainFixtureDefinition();
        definition.AddMap(map1, 5, 1,
            new TerrainPlacement(0, 0, 1, 100),
            new TerrainPlacement(1, 0, 1, 101),
            new TerrainPlacement(3, 0, 1, 100),
            new TerrainPlacement(4, 0, 1, 101));
        definition.AddMap(map2, 1, 2,
            new TerrainPlacement(0, 0, 1, 100),
            new TerrainPlacement(0, 1, 1, 101));
        definition.AddSheet(1, 64, 32,
            new TerrainSheetFrame(100, 0, 0, 32, 32),
            new TerrainSheetFrame(101, 32, 0, 32, 32));

        using var fixture = TerrainFixtureBuilder.Create(definition);
        fixture.WriteInventory(map1, map2);
        var corpus = TerrainCorpusLoader.Load(fixture.RepoRoot);
        var result = TerrainCandidateMiner.Mine(corpus, new FakeFeatureSource(
            similarities: new[] { (new TerrainGraphicReference(1, 100), new TerrainGraphicReference(1, 101)) },
            Settings.MapMemberCompatibility,
            candidates: new Dictionary<TerrainGraphicReference, TerrainGraphicReference[]>
            {
                [new TerrainGraphicReference(1, 100)] = new[] { new TerrainGraphicReference(1, 101) },
                [new TerrainGraphicReference(1, 101)] = new[] { new TerrainGraphicReference(1, 100) },
            }));

        var family = Assert.Single(result.Families);
        Assert.Equal(2, family.MapSupport);
        Assert.Equal(3, family.RegionSupport);
        Assert.Equal(6, family.ObservationSupport);
        Assert.Equal(0, family.DiagonalSupport);
        Assert.Equal(1.0, family.ReferenceWeights[new TerrainGraphicReference(1, 100)]);
        Assert.Equal(1.0, family.ReferenceWeights[new TerrainGraphicReference(1, 101)]);

        var weighted = family.WeightedMasks[new TerrainGraphicReference(1, 100)];
        Assert.Equal(0.5, weighted[TerrainMasks.South]);
        Assert.Equal(0.5, weighted[TerrainMasks.East]);
    }

    [Fact]
    public void Supports_UseTrainingRawMapsRegionsPlacementsAndCornerTrialsOnly()
    {
        var train1 = TerrainFixtureBuilder.FindMapFileName(HoldoutModulo, heldOut: false);
        var train2 = TerrainFixtureBuilder.FindMapFileName(HoldoutModulo, heldOut: false, start: 100);
        var holdout = TerrainFixtureBuilder.FindMapFileName(HoldoutModulo, heldOut: true);

        var definition = new TerrainFixtureDefinition();
        definition.AddMap(train1, 3, 1,
            new TerrainPlacement(0, 0, 1, 100),
            new TerrainPlacement(1, 0, 1, 101),
            new TerrainPlacement(2, 0, 1, 100));
        definition.AddMap(train2, 2, 1,
            new TerrainPlacement(0, 0, 1, 100),
            new TerrainPlacement(1, 0, 1, 101));
        definition.AddMap(holdout, 2, 1,
            new TerrainPlacement(0, 0, 1, 100),
            new TerrainPlacement(1, 0, 1, 101));
        definition.AddSheet(1, 64, 32,
            new TerrainSheetFrame(100, 0, 0, 32, 32),
            new TerrainSheetFrame(101, 32, 0, 32, 32));

        using var fixture = TerrainFixtureBuilder.Create(definition);
        fixture.WriteInventory(train1, train2, holdout);
        var corpus = TerrainCorpusLoader.Load(fixture.RepoRoot);
        Assert.Contains(new TerrainGraphicReference(1, 101), corpus.ObservedReferences);
        var result = TerrainCandidateMiner.Mine(corpus, new FakeFeatureSource(
            similarities: new[] { (new TerrainGraphicReference(1, 100), new TerrainGraphicReference(1, 101)) },
            Settings.MapMemberCompatibility,
            candidates: new Dictionary<TerrainGraphicReference, TerrainGraphicReference[]>
            {
                [new TerrainGraphicReference(1, 100)] = new[] { new TerrainGraphicReference(1, 101) },
                [new TerrainGraphicReference(1, 101)] = new[] { new TerrainGraphicReference(1, 100) },
            }));

        var family = Assert.Single(result.Families);
        Assert.Equal(2, family.MapSupport);
        Assert.Equal(2, family.RegionSupport);
        Assert.Equal(5, family.ObservationSupport);
        Assert.Equal(0, family.DiagonalSupport);
        Assert.Equal(1.0 / 3.0 + 1.0 / 3.0 + 1.0 / 2.0, family.ReferenceWeights[new TerrainGraphicReference(1, 100)]);
        Assert.Equal(1.0 / 3.0 + 1.0 / 2.0, family.ReferenceWeights[new TerrainGraphicReference(1, 101)]);
        Assert.Equal(1.0 / 3.0 + 1.0 / 2.0, family.WeightedMasks[new TerrainGraphicReference(1, 100)][TerrainMasks.East]);
        Assert.Equal(1.0 / 3.0, family.WeightedMasks[new TerrainGraphicReference(1, 100)][TerrainMasks.West]);
    }

    [Fact]
    public void Mine_InputAndMapOrderPermutationProducesEquivalentCandidates()
    {
        var mapA = TerrainFixtureBuilder.FindMapFileName(HoldoutModulo, heldOut: false);
        var mapB = TerrainFixtureBuilder.FindMapFileName(HoldoutModulo, heldOut: false, start: 100);

        TerrainCandidateMinerResult MineWithInventoryOrder(string first, string second)
        {
            var definition = new TerrainFixtureDefinition();
            definition.AddMap(mapA, 2, 2,
                new TerrainPlacement(0, 0, 1, 100),
                new TerrainPlacement(1, 1, 1, 101));
            definition.AddMap(mapB, 1, 2,
                new TerrainPlacement(0, 0, 1, 101),
                new TerrainPlacement(0, 1, 1, 100));
            definition.AddSheet(1, 64, 32,
                new TerrainSheetFrame(100, 0, 0, 32, 32),
                new TerrainSheetFrame(101, 32, 0, 32, 32));
            using var fixture = TerrainFixtureBuilder.Create(definition);
            fixture.WriteInventory(first, second);
            var corpus = TerrainCorpusLoader.Load(fixture.RepoRoot);
            var cache = TerrainFeatureCache.Build(corpus);
            return TerrainCandidateMiner.Mine(corpus, cache);
        }

        var first = MineWithInventoryOrder(mapA, mapB);
        var second = MineWithInventoryOrder(mapB, mapA);
        AssertFamiliesEqual(first, second);
        Assert.Equal(first.RootDiagnostics, second.RootDiagnostics);
    }

    [Fact]
    public void Mine_SingleMapOccurrencesSharingACellDoNotFormAnEdge()
    {
        var map1 = TerrainFixtureBuilder.FindMapFileName(HoldoutModulo, heldOut: false);
        var definition = new TerrainFixtureDefinition();
        definition.AddMap(map1, 3, 1,
            new TerrainPlacement(0, 0, 1, 100),
            new TerrainPlacement(1, 0, 1, 101),
            new TerrainPlacement(2, 0, 1, 100));
        definition.AddSheet(1, 64, 32,
            new TerrainSheetFrame(100, 0, 0, 32, 32),
            new TerrainSheetFrame(101, 32, 0, 32, 32));

        using var fixture = TerrainFixtureBuilder.Create(definition);
        fixture.WriteInventory(map1);
        var corpus = TerrainCorpusLoader.Load(fixture.RepoRoot);
        var result = TerrainCandidateMiner.Mine(corpus, new FakeFeatureSource(
            similarities: new[] { (new TerrainGraphicReference(1, 100), new TerrainGraphicReference(1, 101)) },
            Settings.MapMemberCompatibility,
            candidates: new Dictionary<TerrainGraphicReference, TerrainGraphicReference[]>
            {
                [new TerrainGraphicReference(1, 100)] = new[] { new TerrainGraphicReference(1, 101) },
                [new TerrainGraphicReference(1, 101)] = new[] { new TerrainGraphicReference(1, 100) },
            }));

        Assert.Empty(result.Families);
        var diagnostics = result.RootDiagnostics;
        Assert.Equal(2, diagnostics.Count);
        Assert.All(diagnostics, diagnostic => Assert.Equal("insufficient-family-members", diagnostic.Code));
        Assert.Contains(diagnostics, diagnostic => diagnostic.Reference == new TerrainGraphicReference(1, 100));
        Assert.Contains(diagnostics, diagnostic => diagnostic.Reference == new TerrainGraphicReference(1, 101));
    }

    [Fact]
    public void Mine_SingleMapDisjointOccurrencesFormEdge()
    {
        var map1 = TerrainFixtureBuilder.FindMapFileName(HoldoutModulo, heldOut: false);
        var definition = new TerrainFixtureDefinition();
        definition.AddMap(map1, 4, 1,
            new TerrainPlacement(0, 0, 1, 100),
            new TerrainPlacement(1, 0, 1, 101),
            new TerrainPlacement(2, 0, 1, 100),
            new TerrainPlacement(3, 0, 1, 101));
        definition.AddSheet(1, 64, 32,
            new TerrainSheetFrame(100, 0, 0, 32, 32),
            new TerrainSheetFrame(101, 32, 0, 32, 32));

        using var fixture = TerrainFixtureBuilder.Create(definition);
        fixture.WriteInventory(map1);
        var corpus = TerrainCorpusLoader.Load(fixture.RepoRoot);
        var result = TerrainCandidateMiner.Mine(corpus, new FakeFeatureSource(
            similarities: new[] { (new TerrainGraphicReference(1, 100), new TerrainGraphicReference(1, 101)) },
            Settings.MapMemberCompatibility,
            candidates: new Dictionary<TerrainGraphicReference, TerrainGraphicReference[]>
            {
                [new TerrainGraphicReference(1, 100)] = new[] { new TerrainGraphicReference(1, 101) },
                [new TerrainGraphicReference(1, 101)] = new[] { new TerrainGraphicReference(1, 100) },
            }));

        var family = Assert.Single(result.Families);
        Assert.Equal(
            new[]
            {
                new TerrainGraphicReference(1, 100),
                new TerrainGraphicReference(1, 101),
            },
            family.Members);
        Assert.Empty(result.RootDiagnostics);
    }

    [Fact]
    public void Mine_NorthEastNeighborsCountOneDiagonalTrialInNorthEastSlot()
    {
        var map1 = TerrainFixtureBuilder.FindMapFileName(HoldoutModulo, heldOut: false);
        var map2 = TerrainFixtureBuilder.FindMapFileName(HoldoutModulo, heldOut: false, start: 100);
        var definition = new TerrainFixtureDefinition();
        definition.AddMap(map1, 3, 2,
            new TerrainPlacement(1, 1, 1, 100),
            new TerrainPlacement(1, 0, 1, 101),
            new TerrainPlacement(2, 1, 1, 101));
        definition.AddMap(map2,
            2,
            1,
            new TerrainPlacement(0, 0, 1, 100),
            new TerrainPlacement(1, 0, 1, 101));
        definition.AddSheet(1, 64, 32,
            new TerrainSheetFrame(100, 0, 0, 32, 32),
            new TerrainSheetFrame(101, 32, 0, 32, 32));

        using var fixture = TerrainFixtureBuilder.Create(definition);
        fixture.WriteInventory(map1, map2);
        var corpus = TerrainCorpusLoader.Load(fixture.RepoRoot);
        var slot = new double[5] { 0.1, 0.2, 0.3, 1.0, 0.4 };
        var result = TerrainCandidateMiner.Mine(corpus, new FakeFeatureSource(
            similarities: new[] { (new TerrainGraphicReference(1, 100), new TerrainGraphicReference(1, 101)) },
            Settings.MapMemberCompatibility,
            candidates: new Dictionary<TerrainGraphicReference, TerrainGraphicReference[]>
            {
                [new TerrainGraphicReference(1, 100)] = new[] { new TerrainGraphicReference(1, 101) },
                [new TerrainGraphicReference(1, 101)] = new[] { new TerrainGraphicReference(1, 100) },
            },
            features: new[]
            {
                BuildFeatures(new TerrainGraphicReference(1, 100), 1, slot),
                BuildFeatures(new TerrainGraphicReference(1, 101), 1, slot),
            }));

        var family = Assert.Single(result.Families);
        Assert.Equal(1, family.DiagonalSupport);
        var centroid = family.CornerCentroids[1];
        Assert.Null(centroid.Present);
        Assert.Equal(0.0, centroid.PresentWeight);
        Assert.Equal(slot, centroid.Absent);
        Assert.Equal(1.0 / 3.0, centroid.AbsentWeight);
        foreach (var corner in new[] { 0, 2, 3 })
        {
            Assert.Null(family.CornerCentroids[corner].Present);
            Assert.Null(family.CornerCentroids[corner].Absent);
            Assert.Equal(0.0, family.CornerCentroids[corner].PresentWeight);
            Assert.Equal(0.0, family.CornerCentroids[corner].AbsentWeight);
        }
    }

    [Fact]
    public void Mine_SouthWestNeighborsCountOneDiagonalTrialInSouthWestSlot()
    {
        var map1 = TerrainFixtureBuilder.FindMapFileName(HoldoutModulo, heldOut: false);
        var map2 = TerrainFixtureBuilder.FindMapFileName(HoldoutModulo, heldOut: false, start: 100);
        var definition = new TerrainFixtureDefinition();
        definition.AddMap(map1, 3, 3,
            new TerrainPlacement(1, 1, 1, 100),
            new TerrainPlacement(0, 1, 1, 101),
            new TerrainPlacement(1, 2, 1, 101));
        definition.AddMap(map2,
            2,
            1,
            new TerrainPlacement(0, 0, 1, 100),
            new TerrainPlacement(1, 0, 1, 101));
        definition.AddSheet(1, 64, 32,
            new TerrainSheetFrame(100, 0, 0, 32, 32),
            new TerrainSheetFrame(101, 32, 0, 32, 32));

        using var fixture = TerrainFixtureBuilder.Create(definition);
        fixture.WriteInventory(map1, map2);
        var corpus = TerrainCorpusLoader.Load(fixture.RepoRoot);
        var slot = new double[5] { 0.5, 0.25, 0.125, 1.0, 0.0625 };
        var result = TerrainCandidateMiner.Mine(corpus, new FakeFeatureSource(
            similarities: new[] { (new TerrainGraphicReference(1, 100), new TerrainGraphicReference(1, 101)) },
            Settings.MapMemberCompatibility,
            candidates: new Dictionary<TerrainGraphicReference, TerrainGraphicReference[]>
            {
                [new TerrainGraphicReference(1, 100)] = new[] { new TerrainGraphicReference(1, 101) },
                [new TerrainGraphicReference(1, 101)] = new[] { new TerrainGraphicReference(1, 100) },
            },
            features: new[]
            {
                BuildFeatures(new TerrainGraphicReference(1, 100), 3, slot),
                BuildFeatures(new TerrainGraphicReference(1, 101), 3, slot),
            }));

        var family = Assert.Single(result.Families);
        Assert.Equal(1, family.DiagonalSupport);
        var centroid = family.CornerCentroids[3];
        Assert.Null(centroid.Present);
        Assert.Equal(0.0, centroid.PresentWeight);
        Assert.Equal(slot, centroid.Absent);
        Assert.Equal(1.0 / 3.0, centroid.AbsentWeight);
        foreach (var corner in new[] { 0, 1, 2 })
        {
            Assert.Null(family.CornerCentroids[corner].Present);
            Assert.Null(family.CornerCentroids[corner].Absent);
            Assert.Equal(0.0, family.CornerCentroids[corner].PresentWeight);
            Assert.Equal(0.0, family.CornerCentroids[corner].AbsentWeight);
        }
    }

    [Fact]
    public void Mine_ReleasesEachDecodedMapBeforeReadingNextAcrossEveryPass()
    {
        var mapA = TerrainFixtureBuilder.FindMapFileName(HoldoutModulo, heldOut: false);
        var mapB = TerrainFixtureBuilder.FindMapFileName(HoldoutModulo, heldOut: false, start: 100);
        var definition = new TerrainFixtureDefinition();
        definition.AddMap(mapA, 2, 2,
            new TerrainPlacement(0, 0, 1, 100),
            new TerrainPlacement(0, 1, 1, 101));
        definition.AddMap(mapB, 1, 2,
            new TerrainPlacement(0, 0, 1, 101),
            new TerrainPlacement(0, 1, 1, 100));
        definition.AddSheet(1, 64, 32,
            new TerrainSheetFrame(100, 0, 0, 32, 32),
            new TerrainSheetFrame(101, 32, 0, 32, 32));

        using var fixture = TerrainFixtureBuilder.Create(definition);
        fixture.WriteInventory(mapA, mapB);
        var corpus = TerrainCorpusLoader.Load(fixture.RepoRoot);
        var reader = new FakeMapDataReader(corpus.Root);
        var cache = TerrainFeatureCache.Build(corpus);
        var result = TerrainCandidateMiner.Mine(corpus, cache, Settings, reader);

        Assert.Single(result.Families);
        var trainingMaps = corpus.Maps.Count(descriptor => !TerrainHoldout.IsHeldOut(descriptor.Identity, HoldoutModulo));
        Assert.Equal(2 * trainingMaps, reader.Opens);
        Assert.Equal(1, reader.MaxConcurrent);
        Assert.Equal(0, reader.Concurrent);
    }

    [Fact]
    public void Mine_ChangingOnlyHeldoutMasksAndAdjacencyDoesNotChangeTrainingOrAdmissions()
    {
        var train = TerrainFixtureBuilder.FindMapFileName(HoldoutModulo, heldOut: false);
        var holdout = TerrainFixtureBuilder.FindMapFileName(HoldoutModulo, heldOut: true);

        (TerrainCandidateMinerResult, TerrainImageMemberClassificationResult) MineWithHoldoutLayout(TerrainPlacement[] holdoutPlacements)
        {
            var definition = new TerrainFixtureDefinition();
            definition.AddMap(train, 2, 2,
                new TerrainPlacement(0, 0, 1, 100),
                new TerrainPlacement(1, 1, 1, 101));
            definition.AddMap(holdout, 2, 2, holdoutPlacements);
            definition.AddSheet(1, 64, 32,
                new TerrainSheetFrame(100, 0, 0, 32, 32),
                new TerrainSheetFrame(101, 32, 0, 32, 32));
            using var fixture = TerrainFixtureBuilder.Create(definition);
            fixture.WriteInventory(train, holdout);
            var corpus = TerrainCorpusLoader.Load(fixture.RepoRoot);
            var cache = TerrainFeatureCache.Build(corpus);
            var mined = TerrainCandidateMiner.Mine(corpus, cache);
            var classified = TerrainImageMemberClassifier.Classify(corpus, cache, mined.Families, Settings);
            return (mined, classified);
        }

        var first = MineWithHoldoutLayout(new[]
        {
            new TerrainPlacement(0, 0, 1, 100),
            new TerrainPlacement(1, 1, 1, 101),
        });
        var second = MineWithHoldoutLayout(new[]
        {
            new TerrainPlacement(0, 0, 1, 101),
            new TerrainPlacement(1, 0, 1, 100),
        });

        AssertFamiliesEqual(first.Item1, second.Item1);
        Assert.Equal(first.Item1.RootDiagnostics, second.Item1.RootDiagnostics);
        Assert.Equal(first.Item2.Admissions, second.Item2.Admissions);
        Assert.Equal(first.Item2.SetDiagnostics, second.Item2.SetDiagnostics);
        Assert.Equal(first.Item2.RootDiagnostics, second.Item2.RootDiagnostics);
    }

    private static TerrainFixture CreateAdjacentFixture()
    {
        var map1 = TerrainFixtureBuilder.FindMapFileName(HoldoutModulo, heldOut: false);
        var map2 = TerrainFixtureBuilder.FindMapFileName(HoldoutModulo, heldOut: false, start: 100);
        var definition = new TerrainFixtureDefinition();
        definition.AddMap(map1, 2, 2,
            new TerrainPlacement(0, 0, 1, 100),
            new TerrainPlacement(0, 1, 1, 101));
        definition.AddMap(map2, 2, 2,
            new TerrainPlacement(1, 0, 1, 100),
            new TerrainPlacement(1, 1, 1, 101));
        definition.AddSheet(1, 64, 32,
            new TerrainSheetFrame(100, 0, 0, 32, 32),
            new TerrainSheetFrame(101, 32, 0, 32, 32));
        var fixture = TerrainFixtureBuilder.Create(definition);
        fixture.WriteInventory(map1, map2);
        return fixture;
    }

    private static TerrainFixture CreateBridgeFixture()
    {
        var map1 = TerrainFixtureBuilder.FindMapFileName(HoldoutModulo, heldOut: false);
        var map2 = TerrainFixtureBuilder.FindMapFileName(HoldoutModulo, heldOut: false, start: 100);
        var definition = new TerrainFixtureDefinition();
        definition.AddMap(map1, 3, 1,
            new TerrainPlacement(0, 0, 1, 100),
            new TerrainPlacement(1, 0, 1, 101),
            new TerrainPlacement(2, 0, 1, 102));
        definition.AddMap(map2, 3, 1,
            new TerrainPlacement(0, 0, 1, 102),
            new TerrainPlacement(1, 0, 1, 101),
            new TerrainPlacement(2, 0, 1, 100));
        definition.AddSheet(1, 96, 32,
            new TerrainSheetFrame(100, 0, 0, 32, 32),
            new TerrainSheetFrame(101, 32, 0, 32, 32),
            new TerrainSheetFrame(102, 64, 0, 32, 32));
        var fixture = TerrainFixtureBuilder.Create(definition);
        fixture.WriteInventory(map1, map2);
        return fixture;
    }

    private static void AssertFamiliesEqual(TerrainCandidateMinerResult left, TerrainCandidateMinerResult right)
    {
        Assert.Equal(left.Families.Count, right.Families.Count);
        for (var i = 0; i < left.Families.Count; i++)
        {
            var a = left.Families[i];
            var b = right.Families[i];
            Assert.Equal(a.Members, b.Members);
            Assert.Equal(a.MapSupport, b.MapSupport);
            Assert.Equal(a.RegionSupport, b.RegionSupport);
            Assert.Equal(a.ObservationSupport, b.ObservationSupport);
            Assert.Equal(a.DiagonalSupport, b.DiagonalSupport);
            Assert.Equal(a.ReferenceWeights, b.ReferenceWeights);
            Assert.Equal(a.WeightedMasks, b.WeightedMasks);
            Assert.Equal(a.Medoid, b.Medoid);
            AssertEqualCentroids(a.SideCentroids, b.SideCentroids);
            AssertEqualCentroids(a.CornerCentroids, b.CornerCentroids);
        }
    }

    private static void AssertEqualCentroids(IReadOnlyList<TerrainSideCentroid> left, IReadOnlyList<TerrainSideCentroid> right)
    {
        Assert.Equal(left.Count, right.Count);
        for (var i = 0; i < left.Count; i++)
        {
            Assert.Equal(left[i].ConnectedWeight, right[i].ConnectedWeight);
            Assert.Equal(left[i].DisconnectedWeight, right[i].DisconnectedWeight);
            AssertVectorsEqual(left[i].Connected, right[i].Connected);
            AssertVectorsEqual(left[i].Disconnected, right[i].Disconnected);
        }
    }

    private static void AssertEqualCentroids(IReadOnlyList<TerrainCornerCentroid> left, IReadOnlyList<TerrainCornerCentroid> right)
    {
        Assert.Equal(left.Count, right.Count);
        for (var i = 0; i < left.Count; i++)
        {
            Assert.Equal(left[i].PresentWeight, right[i].PresentWeight);
            Assert.Equal(left[i].AbsentWeight, right[i].AbsentWeight);
            AssertVectorsEqual(left[i].Present, right[i].Present);
            AssertVectorsEqual(left[i].Absent, right[i].Absent);
        }
    }

    private static void AssertVectorsEqual(double[]? left, double[]? right)
    {
        if (left is null || right is null)
        {
            Assert.Equal(left is null, right is null);
            return;
        }

        Assert.Equal(left.Length, right.Length);
        for (var i = 0; i < left.Length; i++)
        {
            Assert.Equal(left[i], right[i]);
        }
    }

    private static TerrainImageFeatures BuildFeatures(TerrainGraphicReference reference, int cornerSlot, double[] slotValues)
    {
        var corners = new double[20];
        Array.Copy(slotValues, 0, corners, cornerSlot * 5, 5);
        return new TerrainImageFeatures(
            reference,
            new double[64],
            new double[65],
            new double[320],
            corners,
            new double[640],
            0UL,
            new TerrainFeatureBuckets(reference.Sheet, 9, 0, 1, 0, 0, 0, 0));
    }

    private sealed class FakeFeatureSource : ITerrainFeatureSource
    {
        private readonly Dictionary<(TerrainGraphicReference, TerrainGraphicReference), double> _similarities = new();
        private readonly Dictionary<TerrainGraphicReference, TerrainImageFeatures> _features = new();

        public FakeFeatureSource(
            (TerrainGraphicReference, TerrainGraphicReference)[] similarities,
            double value,
            Dictionary<TerrainGraphicReference, TerrainGraphicReference[]>? candidates = null,
            TerrainImageFeatures[]? features = null)
        {
            foreach (var (a, b) in similarities)
            {
                _similarities[(a, b)] = value;
                _similarities[(b, a)] = value;
            }

            if (features is not null)
            {
                foreach (var feature in features)
                {
                    _features[feature.Reference] = feature;
                }
            }

            _candidates = candidates ?? new Dictionary<TerrainGraphicReference, TerrainGraphicReference[]>();
        }

        private readonly Dictionary<TerrainGraphicReference, TerrainGraphicReference[]> _candidates;

        public IReadOnlyList<TerrainGraphicReference> QueryCandidates(TerrainGraphicReference reference)
            => _candidates.TryGetValue(reference, out var candidates) ? candidates : Array.Empty<TerrainGraphicReference>();

        public double Similarity(TerrainGraphicReference a, TerrainGraphicReference b)
            => _similarities.TryGetValue((a, b), out var value) ? value : 0.0;

        public bool TryGetFeatures(TerrainGraphicReference reference, out TerrainImageFeatures? features)
            => _features.TryGetValue(reference, out features);
    }

    private sealed class FakeMapDataReader : ITerrainMapDataReader
    {
        public FakeMapDataReader(string repoRoot)
        {
            _mapsDirectory = Path.Combine(repoRoot, TerrainMapInventory.MapsDirectory);
        }

        private readonly string _mapsDirectory;

        public int Opens { get; private set; }
        public int Concurrent { get; private set; }
        public int MaxConcurrent { get; private set; }

        public ITerrainMapData Open(string mapIdentity)
        {
            Opens++;
            Concurrent++;
            MaxConcurrent = Math.Max(MaxConcurrent, Concurrent);
            var path = Path.Combine(_mapsDirectory, Path.GetFileName(mapIdentity));
            return new Data(this, File.ReadAllBytes(path));
        }

        private sealed class Data : ITerrainMapData
        {
            private readonly FakeMapDataReader _reader;

            public Data(FakeMapDataReader reader, byte[] bytes)
            {
                _reader = reader;
                Bytes = bytes;
            }

            public byte[] Bytes { get; }

            public void Release()
            {
                _reader.Concurrent--;
            }
        }
    }
}
