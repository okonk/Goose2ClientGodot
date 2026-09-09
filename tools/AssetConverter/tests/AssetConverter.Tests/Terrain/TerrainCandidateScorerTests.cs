using System.Globalization;
using Goose2.AssetConverter.Terrain;
using MapEditor.Core.Terrain;

namespace AssetConverter.Tests.Terrain;

public class TerrainCandidateScorerTests
{
    private static readonly TerrainGenerationSettings Settings = new(
        HoldoutModulo: 5,
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

    private static readonly HashSet<string> StatusExplanationCodes = new()
    {
        "support-below-minimum",
        "incomplete-required-masks",
        "ambiguity-above-maximum",
        "holdout-accuracy-below-enabled",
        "confidence-below-enabled",
        "confidence-below-pending",
        "member-frame-invalid",
    };

    [Fact]
    public void Metrics_HandCalculatedEntropyAmbiguityMedoidVisualSupportConfidenceMatch()
    {
        var a = new TerrainGraphicReference(1, 100);
        var b = new TerrainGraphicReference(1, 101);
        var c = new TerrainGraphicReference(1, 102);
        var d = new TerrainGraphicReference(1, 103);

        var weighted = new Dictionary<TerrainGraphicReference, Dictionary<int, double>>
        {
            [a] = new() { [15] = 3.0, [7] = 1.0 },
            [b] = new() { [3] = 2.0, [19] = 2.0 },
            [c] = new() { [15] = 1.0 },
        };
        var family = BuildFamily(new[] { a, b, c }, weighted, mapSupport: 10, regionSupport: 20, observationSupport: 50);

        var emitted = new Dictionary<int, IReadOnlyList<TerrainGraphicReference>>();
        for (var i = 0; i < 16; i++)
        {
            emitted[i] = Array.Empty<TerrainGraphicReference>();
        }

        emitted[3] = new[] { b };
        emitted[7] = new[] { a };
        emitted[15] = new[] { a, c };
        var fit = BuildFit(family, TerrainTopology.FourWay, emitted, fourHoldout: 0.8, eightHoldout: 0.85, hasTraining: true, hasHoldout: true);

        var admissions = new[]
        {
            new TerrainImageMemberAdmission(family, d, 15, 0.99, 0.1),
        };
        var corpus = BuildCorpus(new[] { 100, 101, 102, 103 });
        var score = TerrainCandidateScorer.Score(fit, admissions, corpus, new GraphicSimilaritySource(), Settings);

        var p15 = 4.0 / 9.0;
        var p7 = 1.0 / 9.0;
        var p3 = 4.0 / 9.0;
        var expectedEntropy = -((p15 * Math.Log(p15) + p7 * Math.Log(p7)) + p3 * Math.Log(p3)) / Math.Log(16.0);
        Assert.Equal(expectedEntropy, score.MaskEntropy);
        Assert.Equal(3.0 / 16.0, score.Completeness);
        Assert.Equal(1.0 / 9.0, score.Ambiguity);

        var sA = 1.0 - 0.1 * 0.0;
        var sB = 1.0 - 0.1 * 1.0;
        var sC = 1.0 - 0.1 * 2.0;
        var sD = 1.0 - 0.1 * 3.0;
        var expectedVisual = ((((0.0 + sA) + sB) + sC) + sD) / 4.0;
        var expectedSupport = 50.0 / 64.0;
        var expectedGain = 0.85 - 0.8;
        var expectedConfidence = 0.20 * expectedSupport + 0.15 * expectedEntropy + 0.25 * (3.0 / 16.0) + 0.15 * (1.0 - 1.0 / 9.0) + 0.10 * expectedVisual + 0.15 * 0.8;

        Assert.Equal(expectedVisual, score.VisualCompatibility);
        Assert.Equal(0.8, score.HoldoutAccuracy);
        Assert.Equal(expectedGain, score.EightWayAccuracyGain);
        Assert.Equal(expectedSupport, score.SupportScore);
        Assert.Equal(expectedConfidence, score.Confidence);
        Assert.Equal(TerrainTopology.FourWay, score.Topology);
        Assert.Equal(TerrainReviewStatus.Pending, score.Status);

        Assert.Equal(Math.Round(expectedEntropy, 6, MidpointRounding.ToEven), score.Metrics.MaskEntropy);
        Assert.Equal(Math.Round(3.0 / 16.0, 6, MidpointRounding.ToEven), score.Metrics.Completeness);
        Assert.Equal(Math.Round(1.0 / 9.0, 6, MidpointRounding.ToEven), score.Metrics.Ambiguity);
        Assert.Equal(Math.Round(expectedVisual, 6, MidpointRounding.ToEven), score.Metrics.VisualCompatibility);
        Assert.Equal(Math.Round(0.8, 6, MidpointRounding.ToEven), score.Metrics.HoldoutAccuracy);
        Assert.Equal(Math.Round(expectedGain, 6, MidpointRounding.ToEven), score.Metrics.EightWayAccuracyGain);
        Assert.Equal(Math.Round(expectedConfidence, 6, MidpointRounding.ToEven), score.Metrics.Confidence);
        Assert.Equal(10, score.Metrics.MapSupport);
        Assert.Equal(20, score.Metrics.RegionSupport);
        Assert.Equal(50, score.Metrics.ObservationSupport);

        Assert.Equal(new[] { a, b, c, d }, score.FinalMembers.Select(member => member.Reference).ToArray());
        Assert.Equal(TerrainMemberProvenance.MapObserved, score.FinalMembers[0].Provenance);
        Assert.Equal(TerrainMemberProvenance.MapObserved, score.FinalMembers[2].Provenance);
        Assert.Equal(TerrainMemberProvenance.ImageOnly, score.FinalMembers[3].Provenance);
        Assert.Equal(new[] { sA, sB, sC, sD }, score.MemberSimilarities.Select(entry => entry.Similarity).ToArray());
    }

    [Fact]
    public void Metrics_EmptyPopulationsUseLockedZeroOrOneValuesWithoutNaN()
    {
        var family = BuildFamily(
            Array.Empty<TerrainGraphicReference>(),
            new Dictionary<TerrainGraphicReference, Dictionary<int, double>>(),
            mapSupport: 0,
            regionSupport: 0,
            observationSupport: 0);
        var emitted = new Dictionary<int, IReadOnlyList<TerrainGraphicReference>>();
        for (var i = 0; i < 16; i++)
        {
            emitted[i] = Array.Empty<TerrainGraphicReference>();
        }

        var fit = BuildFit(family, TerrainTopology.FourWay, emitted, fourHoldout: 0.0, eightHoldout: 0.0, hasTraining: false, hasHoldout: false);
        var corpus = BuildCorpus(new[] { 100 });
        var score = TerrainCandidateScorer.Score(fit, Array.Empty<TerrainImageMemberAdmission>(), corpus, new FixedSimilaritySource(0.0), Settings);

        Assert.Equal(0.0, score.MaskEntropy);
        Assert.Equal(0.0, score.Completeness);
        Assert.Equal(1.0, score.Ambiguity);
        Assert.Equal(0.0, score.VisualCompatibility);
        Assert.Equal(0.0, score.HoldoutAccuracy);
        Assert.Equal(0.0, score.EightWayAccuracyGain);
        Assert.Equal(0.0, score.SupportScore);
        Assert.Equal(0.0, score.Confidence);
        Assert.Equal(TerrainReviewStatus.Disabled, score.Status);
        Assert.Empty(score.FinalMembers);
        Assert.Empty(score.MemberSimilarities);

        foreach (var value in new[]
                 {
                     score.MaskEntropy, score.Completeness, score.Ambiguity, score.VisualCompatibility,
                     score.HoldoutAccuracy, score.EightWayAccuracyGain, score.SupportScore, score.Confidence,
                     score.Metrics.MaskEntropy, score.Metrics.Completeness, score.Metrics.Ambiguity,
                     score.Metrics.VisualCompatibility, score.Metrics.HoldoutAccuracy, score.Metrics.EightWayAccuracyGain,
                     score.Metrics.Confidence,
                 })
        {
            Assert.True(double.IsFinite(value));
        }
    }

    [Fact]
    public void Score_CompleteHighConfidenceCandidateIsEnabled()
    {
        var (family, fit) = CompleteFamily();
        var corpus = BuildCorpus(Enumerable.Range(100, 16).ToArray());
        var score = TerrainCandidateScorer.Score(fit, Array.Empty<TerrainImageMemberAdmission>(), corpus, new FixedSimilaritySource(1.0), Settings);

        Assert.Equal(TerrainReviewStatus.Enabled, score.Status);
        Assert.Equal(TerrainTopology.FourWay, score.Topology);
        Assert.Equal(1.0, score.MaskEntropy);
        Assert.Equal(1.0, score.Completeness);
        Assert.Equal(0.0, score.Ambiguity);
        Assert.Equal(1.0, score.VisualCompatibility);
        Assert.Equal(1.0, score.HoldoutAccuracy);
        Assert.Equal(1.0, score.SupportScore);
        Assert.True(score.Confidence >= Settings.EnabledConfidence);
        Assert.Empty(score.Diagnostics);
        Assert.Equal(16, score.FinalMembers.Count);
        Assert.All(score.FinalMembers, member => Assert.Equal(TerrainMemberProvenance.MapObserved, member.Provenance));
        Assert.Equal(family.Members.OrderBy(r => r.Sheet).ThenBy(r => r.Graphic).ToArray(), score.FinalMembers.Select(member => member.Reference).ToArray());
    }

    [Fact]
    public void Score_IncompleteCandidateIsPendingEvenWhenOtherMetricsAreHigh()
    {
        var members = CompleteMembers();
        var family = BuildFamily(members, CompleteWeighted(members), mapSupport: 10, regionSupport: 20, observationSupport: 100);
        var emitted = CompleteFourWayEmitted(members);
        emitted[15] = Array.Empty<TerrainGraphicReference>();
        var fit = BuildFit(family, TerrainTopology.FourWay, emitted, fourHoldout: 1.0, eightHoldout: 1.0, hasTraining: true, hasHoldout: true);
        var corpus = BuildCorpus(Enumerable.Range(100, 16).ToArray());
        var score = TerrainCandidateScorer.Score(fit, Array.Empty<TerrainImageMemberAdmission>(), corpus, new FixedSimilaritySource(1.0), Settings);

        Assert.Equal(TerrainReviewStatus.Pending, score.Status);
        Assert.Equal(15.0 / 16.0, score.Completeness);
        Assert.Equal(1.0, score.MaskEntropy);
        Assert.Equal(0.0, score.Ambiguity);
        Assert.True(score.Confidence >= Settings.EnabledConfidence);

        var diagnostics = score.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Message)).ToArray();
        Assert.Single(diagnostics);
        Assert.Equal("incomplete-required-masks", diagnostics[0].Code);
        Assert.Equal("Completeness 0.937500 leaves 1 required masks without variants.", diagnostics[0].Message);
    }

    [Fact]
    public void Score_BelowPendingThresholdIsDisabled()
    {
        var family = BuildFamily(
            Array.Empty<TerrainGraphicReference>(),
            new Dictionary<TerrainGraphicReference, Dictionary<int, double>>(),
            mapSupport: 0,
            regionSupport: 0,
            observationSupport: 0);
        var emitted = new Dictionary<int, IReadOnlyList<TerrainGraphicReference>>();
        for (var i = 0; i < 16; i++)
        {
            emitted[i] = Array.Empty<TerrainGraphicReference>();
        }

        var fit = BuildFit(family, TerrainTopology.FourWay, emitted, fourHoldout: 0.0, eightHoldout: 0.0, hasTraining: false, hasHoldout: false);
        var corpus = BuildCorpus(new[] { 100 });
        var score = TerrainCandidateScorer.Score(fit, Array.Empty<TerrainImageMemberAdmission>(), corpus, new FixedSimilaritySource(0.0), Settings);

        Assert.Equal(TerrainReviewStatus.Disabled, score.Status);
        Assert.Equal(new[]
        {
            ("ambiguity-above-maximum", "Ambiguity 1.000000 exceeds maximum 0.100000."),
            ("confidence-below-pending", "Confidence 0.000000 is below pending threshold 0.450000."),
            ("holdout-accuracy-below-enabled", "Holdout accuracy 0.000000 is below enabled threshold 0.900000."),
            ("incomplete-required-masks", "Completeness 0.000000 leaves 16 required masks without variants."),
            ("no-holdout-observations", "No held-out observations were available; holdout accuracy is 0.000000."),
            ("no-training-observations", "No training observations were available."),
            ("support-below-minimum", "Support 0/0/0 is below required 5/8/64."),
        }, score.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Message)).ToArray());
    }

    [Fact]
    public void Score_EveryPendingAndDisabledSetHasStatusExplanation()
    {
        var (familyA, fitA) = CompleteFamily(mapSupport: 1);
        var scoreA = TerrainCandidateScorer.Score(
            fitA,
            Array.Empty<TerrainImageMemberAdmission>(),
            BuildCorpus(Enumerable.Range(100, 16).ToArray()),
            new FixedSimilaritySource(1.0),
            Settings);
        Assert.Equal(TerrainReviewStatus.Pending, scoreA.Status);
        var diagnosticsA = scoreA.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Message)).ToArray();
        Assert.Contains(("support-below-minimum", "Support 1/20/100 is below required 5/8/64."), diagnosticsA);
        var expectedConfidenceA = 0.20 * (1.0 / 5.0) + 0.15 * 1.0 + 0.25 * 1.0 + 0.15 * (1.0 - 0.0) + 0.10 * 1.0 + 0.15 * 1.0;
        Assert.Contains(
            ("confidence-below-enabled", $"Confidence {Format(expectedConfidenceA)} is below enabled threshold {Format(Settings.EnabledConfidence)}."),
            diagnosticsA);

        var memberB = new TerrainGraphicReference(1, 100);
        var familyB = BuildFamily(
            new[] { memberB },
            new Dictionary<TerrainGraphicReference, Dictionary<int, double>>
            {
                [memberB] = new() { [15] = 0.5, [7] = 0.5 },
            },
            mapSupport: 10,
            regionSupport: 20,
            observationSupport: 100);
        var emittedB = new Dictionary<int, IReadOnlyList<TerrainGraphicReference>>();
        for (var i = 0; i < 16; i++)
        {
            emittedB[i] = Array.Empty<TerrainGraphicReference>();
        }

        emittedB[15] = new[] { memberB };
        emittedB[7] = new[] { memberB };
        var fitB = BuildFit(familyB, TerrainTopology.FourWay, emittedB, fourHoldout: 1.0, eightHoldout: 1.0, hasTraining: true, hasHoldout: true);
        var scoreB = TerrainCandidateScorer.Score(
            fitB,
            Array.Empty<TerrainImageMemberAdmission>(),
            BuildCorpus(new[] { 100 }),
            new FixedSimilaritySource(1.0),
            Settings);
        Assert.Equal(TerrainReviewStatus.Pending, scoreB.Status);
        var diagnosticsB = scoreB.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Message)).ToArray();
        Assert.Contains(("incomplete-required-masks", "Completeness 0.125000 leaves 14 required masks without variants."), diagnosticsB);
        Assert.Contains(("ambiguity-above-maximum", "Ambiguity 0.500000 exceeds maximum 0.100000."), diagnosticsB);
        var expectedConfidenceB = 0.20 * 1.0 + 0.15 * 0.25 + 0.25 * (2.0 / 16.0) + 0.15 * (1.0 - 0.5) + 0.10 * 1.0 + 0.15 * 1.0;
        Assert.Contains(
            ("confidence-below-enabled", $"Confidence {Format(expectedConfidenceB)} is below enabled threshold {Format(Settings.EnabledConfidence)}."),
            diagnosticsB);

        var (_, fitC) = CompleteFamily(holdout: 0.5);
        var scoreC = TerrainCandidateScorer.Score(
            fitC,
            Array.Empty<TerrainImageMemberAdmission>(),
            BuildCorpus(Enumerable.Range(100, 16).ToArray()),
            new FixedSimilaritySource(1.0),
            Settings);
        Assert.Equal(TerrainReviewStatus.Pending, scoreC.Status);
        Assert.True(scoreC.Confidence >= Settings.EnabledConfidence);
        var diagnosticsC = scoreC.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Message)).ToArray();
        Assert.Single(diagnosticsC);
        Assert.Equal(("holdout-accuracy-below-enabled", "Holdout accuracy 0.500000 is below enabled threshold 0.900000."), diagnosticsC[0]);

        var familyD = BuildFamily(
            Array.Empty<TerrainGraphicReference>(),
            new Dictionary<TerrainGraphicReference, Dictionary<int, double>>(),
            mapSupport: 0,
            regionSupport: 0,
            observationSupport: 0);
        var emittedD = new Dictionary<int, IReadOnlyList<TerrainGraphicReference>>();
        for (var i = 0; i < 16; i++)
        {
            emittedD[i] = Array.Empty<TerrainGraphicReference>();
        }

        var fitD = BuildFit(familyD, TerrainTopology.FourWay, emittedD, fourHoldout: 0.0, eightHoldout: 0.0, hasTraining: false, hasHoldout: false);
        var scoreD = TerrainCandidateScorer.Score(
            fitD,
            Array.Empty<TerrainImageMemberAdmission>(),
            BuildCorpus(new[] { 100 }),
            new FixedSimilaritySource(0.0),
            Settings);
        Assert.Equal(TerrainReviewStatus.Disabled, scoreD.Status);
        var diagnosticsD = scoreD.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Message)).ToArray();
        Assert.Contains(("confidence-below-pending", "Confidence 0.000000 is below pending threshold 0.450000."), diagnosticsD);

        var (_, fitE) = CompleteFamily();
        var scoreE = TerrainCandidateScorer.Score(
            fitE,
            Array.Empty<TerrainImageMemberAdmission>(),
            BuildCorpus(Enumerable.Range(100, 14).ToArray()),
            new FixedSimilaritySource(1.0),
            Settings);
        Assert.Equal(TerrainReviewStatus.Pending, scoreE.Status);
        var diagnosticsE = scoreE.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Message)).ToArray();
        Assert.Contains(("member-frame-invalid", "Member frame (1,114) is missing or not a 32x32 tile."), diagnosticsE);
        Assert.Contains(("member-frame-invalid", "Member frame (1,115) is missing or not a 32x32 tile."), diagnosticsE);
        Assert.Equal(new TerrainGraphicReference(1, 114), scoreE.Diagnostics.First(diagnostic => diagnostic.Reference is not null).Reference);

        foreach (var score in new[] { scoreA, scoreB, scoreC, scoreD, scoreE })
        {
            Assert.True(
                score.Status is TerrainReviewStatus.Pending or TerrainReviewStatus.Disabled,
                "shape must be pending or disabled");
            Assert.True(
                score.Diagnostics.Any(diagnostic => StatusExplanationCodes.Contains(diagnostic.Code)),
                "every pending/disabled set must carry at least one status-explanation code");
        }
    }

    [Fact]
    public void Score_EightWayEvidenceInsufficientEmitsExactDiagnostic()
    {
        var (_, fit) = CompleteFamily(diagonalSupport: 4, diagonalMapSupport: 1);
        var score = TerrainCandidateScorer.Score(
            fit,
            Array.Empty<TerrainImageMemberAdmission>(),
            BuildCorpus(Enumerable.Range(100, 16).ToArray()),
            new FixedSimilaritySource(1.0),
            Settings);
        var diagnostics = score.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Message)).ToArray();
        Assert.Contains(
            ("eight-way-evidence-insufficient",
                $"Eight-way not selected: diagonal support 4, diagonal maps 1, four-way accuracy {Format(1.0)}, eight-way accuracy {Format(1.0)}, required gain {Format(Settings.MinimumEightWayAccuracyGain)}."),
            diagnostics);
    }

    private static string Format(double value) => value.ToString("F6", CultureInfo.InvariantCulture);

    private static TerrainGraphicReference[] CompleteMembers()
        => Enumerable.Range(0, 16).Select(i => new TerrainGraphicReference(1, 100 + i)).ToArray();

    private static Dictionary<TerrainGraphicReference, Dictionary<int, double>> CompleteWeighted(TerrainGraphicReference[] members)
    {
        var weighted = new Dictionary<TerrainGraphicReference, Dictionary<int, double>>();
        for (var i = 0; i < members.Length; i++)
        {
            weighted[members[i]] = new Dictionary<int, double> { [i] = 1.0 };
        }

        return weighted;
    }

    private static Dictionary<int, IReadOnlyList<TerrainGraphicReference>> CompleteFourWayEmitted(TerrainGraphicReference[] members)
    {
        var emitted = new Dictionary<int, IReadOnlyList<TerrainGraphicReference>>();
        for (var i = 0; i < 16; i++)
        {
            emitted[i] = new[] { members[i] };
        }

        return emitted;
    }

    private static (TerrainCandidateFamily Family, TerrainModelFit Fit) CompleteFamily(
        int mapSupport = 10,
        int regionSupport = 20,
        int observationSupport = 100,
        double holdout = 1.0,
        int diagonalSupport = 0,
        int diagonalMapSupport = 0)
    {
        var members = CompleteMembers();
        var family = BuildFamily(members, CompleteWeighted(members), mapSupport, regionSupport, observationSupport, diagonalSupport, diagonalMapSupport);
        var fit = BuildFit(family, TerrainTopology.FourWay, CompleteFourWayEmitted(members), holdout, holdout, hasTraining: true, hasHoldout: true, diagonalSupport, diagonalMapSupport);
        return (family, fit);
    }

    private static TerrainCandidateFamily BuildFamily(
        TerrainGraphicReference[] members,
        Dictionary<TerrainGraphicReference, Dictionary<int, double>> weightedMasks,
        int mapSupport,
        int regionSupport,
        int observationSupport,
        int diagonalSupport = 0,
        int diagonalMapSupport = 0)
    {
        var referenceWeights = new Dictionary<TerrainGraphicReference, double>();
        foreach (var entry in weightedMasks)
        {
            var total = 0.0;
            foreach (var weight in entry.Value.Values)
            {
                total += weight;
            }

            referenceWeights[entry.Key] = total;
        }

        var family = new TerrainCandidateFamily(
            members,
            mapSupport,
            regionSupport,
            observationSupport,
            diagonalSupport: diagonalSupport,
            referenceWeights,
            weightedMasks,
            members.Length > 0 ? members[0] : new TerrainGraphicReference(1, 100),
            SideCentroidBank,
            CornerCentroidBank);
        family.DiagonalMapSupport = diagonalMapSupport;
        return family;
    }

    private static TerrainModelFit BuildFit(
        TerrainCandidateFamily family,
        TerrainTopology selected,
        Dictionary<int, IReadOnlyList<TerrainGraphicReference>> fourWayEmitted,
        double fourHoldout,
        double eightHoldout,
        bool hasTraining,
        bool hasHoldout,
        int diagonalSupport = 0,
        int diagonalMapSupport = 0)
    {
        var fourWay = new TerrainModelFitTopology(
            TerrainTopology.FourWay,
            fourWayEmitted,
            new Dictionary<int, TerrainGraphicReference?>(),
            fourHoldout,
            0);
        var eightWay = new TerrainModelFitTopology(
            TerrainTopology.EightWay,
            new Dictionary<int, IReadOnlyList<TerrainGraphicReference>>(),
            new Dictionary<int, TerrainGraphicReference?>(),
            eightHoldout,
            0);
        return new TerrainModelFit(
            family,
            selected,
            fourWay,
            eightWay,
            eightHoldout - fourHoldout,
            hasTraining,
            hasHoldout,
            diagonalSupport: diagonalSupport,
            diagonalMapSupport: diagonalMapSupport);
    }

    private static TerrainCorpus BuildCorpus(int[] graphics)
    {
        var frames = graphics
            .Select(graphic => new TerrainFrame(new TerrainGraphicReference(1, graphic), new TerrainFrameRect((graphic - 100) * 32, 0, 32, 32)))
            .ToList();
        var bySheet = new Dictionary<int, IReadOnlyList<TerrainFrame>> { [1] = frames };
        var frameIndex = new TerrainFrameIndex(32, new[] { 1 }, frames, bySheet);
        var observed = frames.Select(frame => frame.Reference).ToList();
        return new TerrainCorpus(
            "root",
            "fingerprint",
            Array.Empty<TerrainMapDescriptor>(),
            frameIndex,
            observed,
            new[] { 1 },
            Array.Empty<TerrainDiagnostic>());
    }

    private sealed class FixedSimilaritySource : ITerrainFeatureSource
    {
        private readonly double _value;

        public FixedSimilaritySource(double value) => _value = value;

        public IReadOnlyList<TerrainGraphicReference> QueryCandidates(TerrainGraphicReference reference)
            => Array.Empty<TerrainGraphicReference>();

        public double Similarity(TerrainGraphicReference a, TerrainGraphicReference b) => _value;

        public bool TryGetFeatures(TerrainGraphicReference reference, out TerrainImageFeatures? features)
        {
            features = null;
            return false;
        }
    }

    private sealed class GraphicSimilaritySource : ITerrainFeatureSource
    {
        public IReadOnlyList<TerrainGraphicReference> QueryCandidates(TerrainGraphicReference reference)
            => Array.Empty<TerrainGraphicReference>();

        public double Similarity(TerrainGraphicReference a, TerrainGraphicReference b) => 1.0 - 0.1 * (a.Graphic - 100.0);

        public bool TryGetFeatures(TerrainGraphicReference reference, out TerrainImageFeatures? features)
        {
            features = null;
            return false;
        }
    }
}
