using Goose2.AssetConverter.Terrain;
using MapEditor.Core.Terrain;

namespace AssetConverter.Tests.Terrain;

public class TerrainModelFitterTests
{
    private const int HoldoutModulo = 5;

    private static readonly TerrainGraphicReference MemberA = new(1, 100);
    private static readonly TerrainGraphicReference MemberB = new(1, 101);

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

    private static readonly IReadOnlyList<TerrainSideCentroid> SideCentroidBank = new[]
    {
        new TerrainSideCentroid(new double[] { 0.1, 0.2 }, new double[] { 0.3, 0.4 }, 1.0, 2.0),
        new TerrainSideCentroid(new double[] { 0.5, 0.6 }, new double[] { 0.7, 0.8 }, 3.0, 4.0),
        new TerrainSideCentroid(new double[] { 0.9, 1.0 }, new double[] { 1.1, 1.2 }, 5.0, 6.0),
        new TerrainSideCentroid(new double[] { 1.3, 1.4 }, new double[] { 1.5, 1.6 }, 7.0, 8.0),
    }.AsReadOnly();

    private static readonly IReadOnlyList<TerrainCornerCentroid> CornerCentroidBank = new[]
    {
        new TerrainCornerCentroid(new double[] { 0.1 }, new double[] { 0.2 }, 1.0, 2.0),
        new TerrainCornerCentroid(new double[] { 0.3 }, new double[] { 0.4 }, 3.0, 4.0),
        new TerrainCornerCentroid(new double[] { 0.5 }, new double[] { 0.6 }, 5.0, 6.0),
        new TerrainCornerCentroid(new double[] { 0.7 }, new double[] { 0.8 }, 7.0, 8.0),
    }.AsReadOnly();

    [Fact]
    public void Fit_FourWayCorpusReconstructsAll16AndSelectsFourWay()
    {
        var members = Enumerable.Range(0, 16).Select(i => new TerrainGraphicReference(1, 100 + i)).ToArray();
        var weighted = new Dictionary<TerrainGraphicReference, Dictionary<int, double>>();
        for (var i = 0; i < 16; i++)
        {
            weighted[members[i]] = new Dictionary<int, double> { [i] = 1.0 };
        }

        var family = BuildFamily(members, weighted);
        var holdout1 = TerrainFixtureBuilder.FindMapFileName(HoldoutModulo, heldOut: true);
        var holdout2 = TerrainFixtureBuilder.FindMapFileName(HoldoutModulo, heldOut: true, start: 100);
        var holdout3 = TerrainFixtureBuilder.FindMapFileName(HoldoutModulo, heldOut: true, start: 200);
        var holdout4 = TerrainFixtureBuilder.FindMapFileName(HoldoutModulo, heldOut: true, start: 300);
        var (corpus, fixture) = BuildCorpus(
            Enumerable.Range(100, 16).ToArray(),
            new[]
            {
                (holdout1, 1, 1, new[] { new TerrainPlacement(0, 0, 1, 100) }),
                (holdout2, 1, 1, new[] { new TerrainPlacement(0, 0, 1, 100) }),
                (holdout3, 1, 1, new[] { new TerrainPlacement(0, 0, 1, 105) }),
                (holdout4, 1, 1, new[] { new TerrainPlacement(0, 0, 1, 115) }),
            },
            new[] { holdout1, holdout2, holdout3, holdout4 });
        using var _ = fixture;

        var features = TerrainFeatureCache.Build(corpus);
        var fit = TerrainModelFitter.Fit(family, Array.Empty<TerrainImageMemberAdmission>(), corpus, features, Settings);

        Assert.Equal(TerrainTopology.FourWay, fit.SelectedTopology);
        Assert.True(fit.HasTrainingObservations);
        Assert.True(fit.HasEligibleHoldoutObservations);
        Assert.Equal(16, fit.FourWay.EmittedVariants.Count);
        Assert.Equal(16, fit.FourWay.Predictors.Count);
        Assert.Equal(47, fit.EightWay.EmittedVariants.Count);
        Assert.Equal(47, fit.EightWay.Predictors.Count);
        for (var i = 0; i < 16; i++)
        {
            Assert.Equal(new[] { members[i] }, fit.FourWay.EmittedVariants[i]);
            Assert.Equal(members[i], fit.FourWay.Predictors[i]);
            Assert.Equal(new[] { members[i] }, fit.EightWay.EmittedVariants[i]);
            Assert.Equal(members[i], fit.EightWay.Predictors[i]);
        }

        Assert.Equal(0.5, fit.FourWay.HoldoutAccuracy, 12);
        Assert.Equal(0.5, fit.EightWay.HoldoutAccuracy, 12);
        Assert.Equal(0.0, fit.EightWayAccuracyGain, 12);
    }

    [Fact]
    public void Fit_DiagonalDistinctCorpus_Top1EightWayBeatsFourWayAlthoughFourWayEmittedListsAreSupersets()
    {
        var family = BuildFamily(
            new[] { MemberA, MemberB },
            new Dictionary<TerrainGraphicReference, Dictionary<int, double>>
            {
                [MemberA] = new() { [15] = 1.0, [19] = 1.0, [55] = 1.0 },
                [MemberB] = new() { [15] = 1.0, [39] = 1.0 },
            },
            diagonalSupport: 40,
            diagonalMapSupport: 6);

        var holdout = TerrainFixtureBuilder.FindMapFileName(HoldoutModulo, heldOut: true);
        var (corpus, fixture) = BuildCorpus(
            new[] { 100, 101 },
            new[] { (holdout, 7, 3, BlockerHoldout(swapped: false)) },
            new[] { holdout });
        using var _ = fixture;

        var fit = TerrainModelFitter.Fit(family, Array.Empty<TerrainImageMemberAdmission>(), corpus, new NullFeatureSource(), Settings);

        var evidence = new (int Graphic, int RawMask, double Weight)[]
        {
            (100, 38, 0.125),
            (101, 76, 0.125),
            (100, 19, 0.125),
            (101, 137, 0.125),
            (101, 4, 0.1),
            (101, 39, 0.1),
            (100, 76, 0.1),
            (101, 19, 0.1),
            (100, 137, 0.1),
        };
        var naiveFour = NaiveMembershipHitRate(fit.FourWay, evidence);
        var naiveEight = NaiveMembershipHitRate(fit.EightWay, evidence);
        Assert.Equal(0.225, naiveFour, 12);
        Assert.Equal(0.0, naiveEight, 12);
        Assert.True(naiveFour > naiveEight);

        Assert.Equal(new[] { MemberA }, fit.FourWay.EmittedVariants[3]);
        Assert.Equal(new[] { MemberB }, fit.FourWay.EmittedVariants[7]);
        Assert.Empty(fit.FourWay.EmittedVariants[15]);
        Assert.Equal(new[] { MemberA, MemberB }, fit.EightWay.EmittedVariants[15]);
        Assert.Empty(fit.EightWay.EmittedVariants[19]);
        Assert.Empty(fit.EightWay.EmittedVariants[39]);

        Assert.Equal(0.125, fit.FourWay.HoldoutAccuracy, 12);
        Assert.Equal(0.225, fit.EightWay.HoldoutAccuracy, 12);
        Assert.True(fit.EightWayAccuracyGain >= Settings.MinimumEightWayAccuracyGain);
        Assert.Equal(TerrainTopology.EightWay, fit.SelectedTopology);
    }

    [Fact]
    public void Fit_CanonicalBlobCorpusWithMultiMapDiagonalSupportSelectsEightWayAndAll47()
    {
        var required = TerrainMasks.Required(TerrainTopology.EightWay).ToList();
        var members = required.Select((mask, i) => new TerrainGraphicReference(1, 100 + i)).ToArray();
        var weighted = new Dictionary<TerrainGraphicReference, Dictionary<int, double>>();
        for (var i = 0; i < required.Count; i++)
        {
            weighted[members[i]] = new Dictionary<int, double> { [required[i]] = 1.0 };
        }

        var family = BuildFamily(members, weighted, diagonalSupport: 47, diagonalMapSupport: 5);
        int Graphic(int mask) => 100 + required.IndexOf(mask);
        TerrainPlacement[] Blob() => new[]
        {
            new TerrainPlacement(0, 0, 1, Graphic(38)),
            new TerrainPlacement(1, 0, 1, Graphic(76)),
            new TerrainPlacement(0, 1, 1, Graphic(19)),
            new TerrainPlacement(1, 1, 1, Graphic(137)),
        };

        var holdout1 = TerrainFixtureBuilder.FindMapFileName(HoldoutModulo, heldOut: true);
        var holdout2 = TerrainFixtureBuilder.FindMapFileName(HoldoutModulo, heldOut: true, start: 100);
        var (corpus, fixture) = BuildCorpus(
            Enumerable.Range(100, 47).ToArray(),
            new[]
            {
                (holdout1, 2, 2, Blob()),
                (holdout2, 2, 2, Blob()),
            },
            new[] { holdout1, holdout2 });
        using var _ = fixture;

        var fit = TerrainModelFitter.Fit(family, Array.Empty<TerrainImageMemberAdmission>(), corpus, new NullFeatureSource(), Settings);

        Assert.Equal(TerrainTopology.EightWay, fit.SelectedTopology);
        Assert.Equal(47, fit.EightWay.EmittedVariants.Count);
        Assert.Equal(47, fit.EightWay.Predictors.Count);
        for (var i = 0; i < required.Count; i++)
        {
            Assert.Equal(new[] { members[i] }, fit.EightWay.EmittedVariants[required[i]]);
            Assert.Equal(members[i], fit.EightWay.Predictors[required[i]]);
        }

        Assert.Equal(1.0, fit.EightWay.HoldoutAccuracy, 12);
        Assert.Equal(0.0, fit.FourWay.HoldoutAccuracy, 12);
        Assert.Equal(1.0, fit.EightWayAccuracyGain, 12);
        Assert.Equal(16, fit.FourWay.EmittedVariants.Count);
        Assert.Equal(2, fit.FourWay.EmittedVariants[3].Count);
        Assert.Contains(new TerrainGraphicReference(1, 103), fit.FourWay.EmittedVariants[3]);
        Assert.Contains(new TerrainGraphicReference(1, 116), fit.FourWay.EmittedVariants[3]);
    }

    [Fact]
    public void Fit_SparseOrSingleMapCornersStayFourWayWithExactEvidenceDiagnostic()
    {
        var family = BuildFamily(
            new[] { MemberA, MemberB },
            new Dictionary<TerrainGraphicReference, Dictionary<int, double>>
            {
                [MemberA] = new() { [15] = 1.0, [19] = 1.0, [55] = 1.0 },
                [MemberB] = new() { [15] = 1.0, [39] = 1.0 },
            },
            diagonalSupport: 4,
            diagonalMapSupport: 1);

        var holdout = TerrainFixtureBuilder.FindMapFileName(HoldoutModulo, heldOut: true);
        var (corpus, fixture) = BuildCorpus(
            new[] { 100, 101 },
            new[] { (holdout, 7, 3, BlockerHoldout(swapped: false)) },
            new[] { holdout });
        using var _ = fixture;

        var fit = TerrainModelFitter.Fit(family, Array.Empty<TerrainImageMemberAdmission>(), corpus, new NullFeatureSource(), Settings);

        Assert.Equal(TerrainTopology.FourWay, fit.SelectedTopology);
        Assert.Equal(0.125, fit.FourWay.HoldoutAccuracy, 12);
        Assert.Equal(0.225, fit.EightWay.HoldoutAccuracy, 12);
        Assert.Equal(0.1, fit.EightWayAccuracyGain, 12);
        Assert.True(fit.EightWayAccuracyGain >= Settings.MinimumEightWayAccuracyGain);
        Assert.True(fit.DiagonalSupport > 0);
        Assert.True(fit.DiagonalSupport < Settings.MinimumDiagonalSupport);
        Assert.True(fit.DiagonalMapSupport < Settings.MinimumMapSupport);
        Assert.Equal(4, fit.DiagonalSupport);
        Assert.Equal(1, fit.DiagonalMapSupport);
    }

    [Fact]
    public void Fit_NoisyVariantsShareEmittedMaskButTop1TieUsesLowerReference()
    {
        var memberC = new TerrainGraphicReference(1, 102);
        var family = BuildFamily(
            new[] { MemberA, MemberB, memberC },
            new Dictionary<TerrainGraphicReference, Dictionary<int, double>>
            {
                [MemberA] = new() { [15] = 0.6, [7] = 0.4 },
                [MemberB] = new() { [15] = 0.7, [7] = 0.3 },
                [memberC] = new() { [15] = 0.7 },
            });

        var holdout = TerrainFixtureBuilder.FindMapFileName(HoldoutModulo, heldOut: true);
        var (corpus, fixture) = BuildCorpus(
            new[] { 100, 101, 102 },
            new[]
            {
                (holdout, 3, 3, new[]
                {
                    new TerrainPlacement(0, 0, 1, 100),
                    new TerrainPlacement(1, 0, 1, 102),
                    new TerrainPlacement(2, 0, 1, 100),
                    new TerrainPlacement(0, 1, 1, 102),
                    new TerrainPlacement(1, 1, 1, 101),
                    new TerrainPlacement(2, 1, 1, 100),
                    new TerrainPlacement(0, 2, 1, 101),
                    new TerrainPlacement(1, 2, 1, 102),
                    new TerrainPlacement(2, 2, 1, 100),
                }),
            },
            new[] { holdout });
        using var _ = fixture;

        var fit = TerrainModelFitter.Fit(family, Array.Empty<TerrainImageMemberAdmission>(), corpus, new NullFeatureSource(), Settings);

        Assert.Equal(TerrainTopology.FourWay, fit.SelectedTopology);
        Assert.Equal(new[] { MemberA, MemberB, memberC }, fit.FourWay.EmittedVariants[15]);
        Assert.Empty(fit.FourWay.EmittedVariants[7]);
        Assert.Equal(MemberB, fit.FourWay.Predictors[15]);
        Assert.Equal(MemberA, fit.FourWay.Predictors[7]);
        Assert.Equal(1.0 / 9.0, fit.FourWay.HoldoutAccuracy, 12);
        Assert.Equal(0.0, fit.EightWay.HoldoutAccuracy, 12);
    }

    [Fact]
    public void Fit_NoTrainingOrNoHoldoutUsesZeroAccuracyAndExactDiagnostic()
    {
        var holdout1 = TerrainFixtureBuilder.FindMapFileName(HoldoutModulo, heldOut: true);
        var (corpusNoTraining, fixtureNoTraining) = BuildCorpus(
            new[] { 100, 101 },
            new[] { (holdout1, 1, 1, new[] { new TerrainPlacement(0, 0, 1, 100) }) },
            new[] { holdout1 });
        using var _ = fixtureNoTraining;

        var noTraining = TerrainModelFitter.Fit(
            BuildFamily(new[] { MemberA, MemberB }, null),
            Array.Empty<TerrainImageMemberAdmission>(),
            corpusNoTraining,
            new NullFeatureSource(),
            Settings);
        Assert.False(noTraining.HasTrainingObservations);
        Assert.True(noTraining.HasEligibleHoldoutObservations);
        Assert.Equal(TerrainTopology.FourWay, noTraining.SelectedTopology);
        Assert.Equal(0.0, noTraining.FourWay.HoldoutAccuracy, 12);
        Assert.Equal(0.0, noTraining.EightWay.HoldoutAccuracy, 12);
        Assert.Equal(0.0, noTraining.EightWayAccuracyGain, 12);
        Assert.Equal(new[] { MemberA, MemberB }, noTraining.FourWay.EmittedVariants[0]);
        Assert.All(noTraining.FourWay.Predictors.Values, value => Assert.Null(value));
        Assert.All(noTraining.EightWay.Predictors.Values, value => Assert.Null(value));

        var holdout2 = TerrainFixtureBuilder.FindMapFileName(HoldoutModulo, heldOut: true, start: 100);
        var (corpusNoHoldout, fixtureNoHoldout) = BuildCorpus(
            new[] { 100, 101, 105 },
            new[] { (holdout2, 1, 1, new[] { new TerrainPlacement(0, 0, 1, 105) }) },
            new[] { holdout2 });
        using var _fixtureNoHoldout = fixtureNoHoldout;

        var noHoldout = TerrainModelFitter.Fit(
            BuildFamily(
                new[] { MemberA, MemberB },
                new Dictionary<TerrainGraphicReference, Dictionary<int, double>>
                {
                    [MemberA] = new() { [15] = 1.0 },
                    [MemberB] = new() { [15] = 1.0 },
                }),
            Array.Empty<TerrainImageMemberAdmission>(),
            corpusNoHoldout,
            new NullFeatureSource(),
            Settings);
        Assert.True(noHoldout.HasTrainingObservations);
        Assert.False(noHoldout.HasEligibleHoldoutObservations);
        Assert.Equal(TerrainTopology.FourWay, noHoldout.SelectedTopology);
        Assert.Equal(0.0, noHoldout.FourWay.HoldoutAccuracy, 12);
        Assert.Equal(0.0, noHoldout.EightWay.HoldoutAccuracy, 12);
        Assert.Equal(0.0, noHoldout.EightWayAccuracyGain, 12);
        Assert.Equal(MemberA, noHoldout.FourWay.Predictors[15]);
        Assert.Equal(MemberA, noHoldout.EightWay.Predictors[15]);
        Assert.Equal(new[] { MemberA, MemberB }, noHoldout.FourWay.EmittedVariants[15]);
    }

    [Fact]
    public void Fit_HoldoutAssignmentAndWeightedAccuracyAreStableAcrossOrderAndHugeMaps()
    {
        var family = BuildFamily(
            new[] { MemberA, MemberB },
            new Dictionary<TerrainGraphicReference, Dictionary<int, double>>
            {
                [MemberA] = new() { [15] = 1.0 },
                [MemberB] = new() { [15] = 1.0 },
            });

        var huge = TerrainFixtureBuilder.FindMapFileName(HoldoutModulo, heldOut: true);
        var tiny = TerrainFixtureBuilder.FindMapFileName(HoldoutModulo, heldOut: true, start: 100);

        var hugePlacements = new List<TerrainPlacement>();
        for (var y = 0; y < 50; y++)
        {
            for (var x = 0; x < 50; x++)
            {
                hugePlacements.Add(new TerrainPlacement(x, y, 1, 100));
            }
        }

        var (corpus, fixture) = BuildCorpus(
            new[] { 100, 101 },
            new[]
            {
                (huge, 50, 50, hugePlacements.ToArray()),
                (tiny, 1, 1, new[] { new TerrainPlacement(0, 0, 1, 101) }),
            },
            new[] { huge, tiny });
        using var _ = fixture;

        var reversed = new TerrainCorpus(
            corpus.Root,
            corpus.Fingerprint,
            corpus.Maps.Reverse().ToList(),
            corpus.FrameIndex,
            corpus.ObservedReferences,
            corpus.RelevantSheets,
            corpus.Diagnostics);
        Assert.NotEqual(corpus.Maps[0].Identity, reversed.Maps[0].Identity);

        var first = TerrainModelFitter.Fit(family, Array.Empty<TerrainImageMemberAdmission>(), corpus, new NullFeatureSource(), Settings);
        var second = TerrainModelFitter.Fit(family, Array.Empty<TerrainImageMemberAdmission>(), reversed, new NullFeatureSource(), Settings);

        Assert.Equal(first.FourWay.HoldoutAccuracy, second.FourWay.HoldoutAccuracy);
        Assert.Equal(first.EightWay.HoldoutAccuracy, second.EightWay.HoldoutAccuracy);
        Assert.Equal(first.FourWay.EmittedVariants, second.FourWay.EmittedVariants);
        Assert.Equal(first.EightWay.EmittedVariants, second.EightWay.EmittedVariants);
        Assert.Equal(first.FourWay.Predictors, second.FourWay.Predictors);
        Assert.Equal(first.EightWay.Predictors, second.EightWay.Predictors);
        Assert.True(Math.Abs(first.FourWay.HoldoutAccuracy - 0.4608) < 1e-12);
        Assert.Equal(0.0, first.EightWay.HoldoutAccuracy, 12);
        Assert.Equal(TerrainTopology.FourWay, first.SelectedTopology);
    }

    [Fact]
    public void Fit_ChangingHeldoutLabelsChangesAccuracyButNotFittedPredictorsMappingsCentroidsOrMembers()
    {
        var familyA = BuildFamily(
            new[] { MemberA, MemberB },
            new Dictionary<TerrainGraphicReference, Dictionary<int, double>>
            {
                [MemberA] = new() { [15] = 1.0, [19] = 1.0, [55] = 1.0 },
                [MemberB] = new() { [15] = 1.0, [39] = 1.0 },
            },
            diagonalSupport: 40,
            diagonalMapSupport: 6);
        var familyB = BuildFamily(
            new[] { MemberA, MemberB },
            new Dictionary<TerrainGraphicReference, Dictionary<int, double>>
            {
                [MemberA] = new() { [15] = 1.0, [19] = 1.0, [55] = 1.0 },
                [MemberB] = new() { [15] = 1.0, [39] = 1.0 },
            },
            diagonalSupport: 40,
            diagonalMapSupport: 6);

        var holdout = TerrainFixtureBuilder.FindMapFileName(HoldoutModulo, heldOut: true);
        var (corpusA, fixtureA) = BuildCorpus(
            new[] { 100, 101 },
            new[] { (holdout, 7, 3, BlockerHoldout(swapped: false)) },
            new[] { holdout });
        using var _fixtureA = fixtureA;
        var (corpusB, fixtureB) = BuildCorpus(
            new[] { 100, 101 },
            new[] { (holdout, 7, 3, BlockerHoldout(swapped: true)) },
            new[] { holdout });
        using var _fixtureB = fixtureB;

        var fitA = TerrainModelFitter.Fit(familyA, Array.Empty<TerrainImageMemberAdmission>(), corpusA, new NullFeatureSource(), Settings);
        var fitB = TerrainModelFitter.Fit(familyB, Array.Empty<TerrainImageMemberAdmission>(), corpusB, new NullFeatureSource(), Settings);

        Assert.Equal(0.125, fitA.FourWay.HoldoutAccuracy, 12);
        Assert.Equal(0.225, fitA.EightWay.HoldoutAccuracy, 12);
        Assert.Equal(0.1, fitB.FourWay.HoldoutAccuracy, 12);
        Assert.Equal(0.0, fitB.EightWay.HoldoutAccuracy, 12);
        Assert.NotEqual(fitA.FourWay.HoldoutAccuracy, fitB.FourWay.HoldoutAccuracy);
        Assert.NotEqual(fitA.EightWay.HoldoutAccuracy, fitB.EightWay.HoldoutAccuracy);

        Assert.Equal(fitA.FourWay.EmittedVariants, fitB.FourWay.EmittedVariants);
        Assert.Equal(fitA.FourWay.Predictors, fitB.FourWay.Predictors);
        Assert.Equal(fitA.EightWay.EmittedVariants, fitB.EightWay.EmittedVariants);
        Assert.Equal(fitA.EightWay.Predictors, fitB.EightWay.Predictors);
        Assert.Equal(fitA.Family.Members, fitB.Family.Members);
        Assert.Equal(fitA.Family.Medoid, fitB.Family.Medoid);
        Assert.Equal(fitA.Family.SideCentroids.Count, fitB.Family.SideCentroids.Count);
        for (var i = 0; i < fitA.Family.SideCentroids.Count; i++)
        {
            Assert.Equal(fitA.Family.SideCentroids[i].Connected, fitB.Family.SideCentroids[i].Connected);
            Assert.Equal(fitA.Family.SideCentroids[i].Disconnected, fitB.Family.SideCentroids[i].Disconnected);
        }

        Assert.Equal(fitA.Family.CornerCentroids.Count, fitB.Family.CornerCentroids.Count);
        for (var i = 0; i < fitA.Family.CornerCentroids.Count; i++)
        {
            Assert.Equal(fitA.Family.CornerCentroids[i].Present, fitB.Family.CornerCentroids[i].Present);
            Assert.Equal(fitA.Family.CornerCentroids[i].Absent, fitB.Family.CornerCentroids[i].Absent);
        }
    }

    private static TerrainCandidateFamily BuildFamily(
        TerrainGraphicReference[] members,
        Dictionary<TerrainGraphicReference, Dictionary<int, double>>? weightedMasks,
        int diagonalSupport = 0,
        int diagonalMapSupport = 0)
    {
        weightedMasks ??= new Dictionary<TerrainGraphicReference, Dictionary<int, double>>();
        var referenceWeights = new Dictionary<TerrainGraphicReference, double>();
        var observationSupport = 0;
        foreach (var entry in weightedMasks)
        {
            var total = 0.0;
            foreach (var weight in entry.Value.Values)
            {
                total += weight;
            }

            referenceWeights[entry.Key] = total;
            observationSupport += entry.Value.Count;
        }

        var family = new TerrainCandidateFamily(
            members,
            mapSupport: 1,
            regionSupport: 0,
            observationSupport: observationSupport,
            diagonalSupport: diagonalSupport,
            referenceWeights,
            weightedMasks,
            members[0],
            SideCentroidBank,
            CornerCentroidBank);
        family.DiagonalMapSupport = diagonalMapSupport;
        return family;
    }

    private static (TerrainCorpus Corpus, TerrainFixture Fixture) BuildCorpus(
        int[] sheetGraphics,
        (string Name, int Width, int Height, TerrainPlacement[] Placements)[] maps,
        string[] inventory)
    {
        var definition = new TerrainFixtureDefinition();
        foreach (var map in maps)
        {
            definition.AddMap(map.Name, map.Width, map.Height, map.Placements);
        }

        var frames = sheetGraphics
            .Select((graphic, i) => new TerrainSheetFrame(graphic, i * 32, 0, 32, 32))
            .ToArray();
        definition.AddSheet(1, sheetGraphics.Length * 32, 32, frames);

        var fixture = TerrainFixtureBuilder.Create(definition);
        fixture.WriteInventory(inventory);
        return (TerrainCorpusLoader.Load(fixture.RepoRoot), fixture);
    }

    private static TerrainPlacement[] BlockerHoldout(bool swapped)
    {
        var cell11 = swapped ? MemberB.Graphic : MemberA.Graphic;
        var cell51 = swapped ? MemberA.Graphic : MemberB.Graphic;
        return new[]
        {
            new TerrainPlacement(1, 0, 1, MemberA.Graphic),
            new TerrainPlacement(2, 0, 1, MemberB.Graphic),
            new TerrainPlacement(1, 1, 1, cell11),
            new TerrainPlacement(2, 1, 1, MemberB.Graphic),
            new TerrainPlacement(5, 0, 1, MemberB.Graphic),
            new TerrainPlacement(5, 1, 1, cell51),
            new TerrainPlacement(6, 1, 1, MemberA.Graphic),
            new TerrainPlacement(5, 2, 1, MemberB.Graphic),
            new TerrainPlacement(6, 2, 1, MemberA.Graphic),
        };
    }

    private static double NaiveMembershipHitRate(
        TerrainModelFitTopology topology,
        (int Graphic, int RawMask, double Weight)[] placements)
    {
        var hits = 0.0;
        foreach (var (graphic, rawMask, weight) in placements)
        {
            var mask = TerrainMasks.Normalize(rawMask, topology.Topology);
            if (topology.EmittedVariants[mask].Contains(new TerrainGraphicReference(1, graphic)))
            {
                hits += weight;
            }
        }

        return hits;
    }

    private sealed class NullFeatureSource : ITerrainFeatureSource
    {
        public IReadOnlyList<TerrainGraphicReference> QueryCandidates(TerrainGraphicReference reference)
            => Array.Empty<TerrainGraphicReference>();

        public double Similarity(TerrainGraphicReference a, TerrainGraphicReference b) => 0.0;

        public bool TryGetFeatures(TerrainGraphicReference reference, out TerrainImageFeatures? features)
        {
            features = null;
            return false;
        }
    }
}
