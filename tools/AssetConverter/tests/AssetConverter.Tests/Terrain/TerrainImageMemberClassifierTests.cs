using System.Globalization;
using Goose2.AssetConverter.Terrain;
using MapEditor.Core.Terrain;

namespace AssetConverter.Tests.Terrain;

public class TerrainImageMemberClassifierTests
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
    public void Classify_MissingVariantIsAdmittedWithNormalizedMaskAndReviewEvidence()
    {
        var family = CreateCompleteFamily(
            new[] { new TerrainGraphicReference(1, 100), new TerrainGraphicReference(1, 101) },
            new TerrainGraphicReference(1, 100));
        var source = new FakeFeatureSource(
            features: new[]
            {
                CandidateFeatures(new TerrainGraphicReference(1, 102), sides: new[] { SolidSide(0, 0, 204) }),
            },
            similarities: new[]
            {
                (new TerrainGraphicReference(1, 102), new TerrainGraphicReference(1, 100), 0.95),
                (new TerrainGraphicReference(1, 102), new TerrainGraphicReference(1, 101), 0.95),
            },
            candidates: new[]
            {
                (new TerrainGraphicReference(1, 100), new[] { new TerrainGraphicReference(1, 102) }),
                (new TerrainGraphicReference(1, 101), new[] { new TerrainGraphicReference(1, 102) }),
            });

        var result = TerrainImageMemberClassifier.Classify(CreateClassifierCorpus(), source, new[] { family }, Settings);

        var admission = Assert.Single(result.Admissions);
        Assert.Equal(new TerrainGraphicReference(1, 102), admission.Reference);
        Assert.Equal(0x0F, admission.Mask);
        Assert.Equal(0.95, admission.Compatibility, 6);
        Assert.Equal(0.95, admission.OwnerMargin, 6);
        Assert.Equal(admission.Mask, TerrainMasks.Normalize(admission.Mask, TerrainTopology.EightWay));

        // image-only-member-admitted is emitted by the topology pass with the selected emitted mask; the classifier returns only the admission.
        Assert.Empty(result.SetDiagnostics);
        Assert.Empty(result.RootDiagnostics);
    }
    [Fact]
    public void Classify_GraphicZeroAndEveryMapObservedReferenceAreNeverImageOnly()
    {
        var family = CreateCompleteFamily(
            new[] { new TerrainGraphicReference(1, 100), new TerrainGraphicReference(1, 101) },
            new TerrainGraphicReference(1, 100));
        var source = new FakeFeatureSource(
            features: new[]
            {
                CandidateFeatures(new TerrainGraphicReference(1, 0), sides: new[] { SolidSide(0, 0, 204) }),
                CandidateFeatures(new TerrainGraphicReference(1, 102), sides: new[] { SolidSide(0, 0, 204) }),
            },
            similarities: new[]
            {
                (new TerrainGraphicReference(1, 0), new TerrainGraphicReference(1, 100), 1.0),
                (new TerrainGraphicReference(1, 0), new TerrainGraphicReference(1, 101), 1.0),
                (new TerrainGraphicReference(1, 102), new TerrainGraphicReference(1, 100), 0.95),
                (new TerrainGraphicReference(1, 102), new TerrainGraphicReference(1, 101), 0.95),
            },
            candidates: new[]
            {
                (new TerrainGraphicReference(1, 100), new[] { new TerrainGraphicReference(1, 0), new TerrainGraphicReference(1, 102) }),
                (new TerrainGraphicReference(1, 101), new[] { new TerrainGraphicReference(1, 0), new TerrainGraphicReference(1, 102) }),
            });

        var corpus = CreateClassifierCorpus();
        var result = TerrainImageMemberClassifier.Classify(corpus, source, new[] { family }, Settings);

        Assert.Equal(new[] { new TerrainGraphicReference(1, 102) }, result.Admissions.Select(admission => admission.Reference).ToArray());
        Assert.DoesNotContain(result.Admissions, admission => admission.Reference.Graphic == 0);
        Assert.DoesNotContain(result.Admissions, admission => corpus.ObservedReferences.Contains(admission.Reference));
    }

    [Fact]
    public void Classify_HoldoutObservedReferenceWithoutTrainingOwnerIsExcludedNotRelabeled()
    {
        var train = TerrainFixtureBuilder.FindMapFileName(HoldoutModulo, heldOut: false);
        var holdout = TerrainFixtureBuilder.FindMapFileName(HoldoutModulo, heldOut: true);
        var definition = new TerrainFixtureDefinition();
        definition.AddMap(train, 2, 2,
            new TerrainPlacement(0, 0, 1, 100),
            new TerrainPlacement(1, 0, 1, 101));
        definition.AddMap(holdout, 1, 1, new TerrainPlacement(0, 0, 1, 300));
        definition.AddSheet(1, 128, 32,
            new TerrainSheetFrame(100, 0, 0, 32, 32),
            new TerrainSheetFrame(101, 32, 0, 32, 32),
            new TerrainSheetFrame(102, 64, 0, 32, 32),
            new TerrainSheetFrame(300, 96, 0, 32, 32));

        using var fixture = TerrainFixtureBuilder.Create(definition);
        fixture.WriteInventory(train, holdout);
        var corpus = TerrainCorpusLoader.Load(fixture.RepoRoot);
        var family = CreateCompleteFamily(
            new[] { new TerrainGraphicReference(1, 100), new TerrainGraphicReference(1, 101) },
            new TerrainGraphicReference(1, 100));
        var source = new FakeFeatureSource(
            features: new[]
            {
                CandidateFeatures(new TerrainGraphicReference(1, 102), sides: new[] { SolidSide(0, 0, 204) }),
            },
            similarities: new[]
            {
                (new TerrainGraphicReference(1, 102), new TerrainGraphicReference(1, 100), 0.95),
                (new TerrainGraphicReference(1, 102), new TerrainGraphicReference(1, 101), 0.95),
            },
            candidates: new[]
            {
                (new TerrainGraphicReference(1, 100), new[] { new TerrainGraphicReference(1, 102) }),
                (new TerrainGraphicReference(1, 101), new[] { new TerrainGraphicReference(1, 102) }),
            });

        var result = TerrainImageMemberClassifier.Classify(corpus, source, new[] { family }, Settings);

        Assert.DoesNotContain(result.Admissions, admission => new TerrainGraphicReference(1, 300) == admission.Reference);
        var diagnostic = Assert.Single(result.RootDiagnostics, d => d.Code == "heldout-only-member-excluded");
        Assert.Equal("Map-observed reference (1,300) has no training-family owner and was not considered image-only.", diagnostic.Message);
        Assert.Equal(new TerrainGraphicReference(1, 300), diagnostic.Reference);
        Assert.Null(diagnostic.Mask);
    }

    [Fact]
    public void Classify_OnlyOneFamilyUsesZeroRunnerUpAndStillRequiresStrictThreshold()
    {
        var family = CreateCompleteFamily(
            new[] { new TerrainGraphicReference(1, 100), new TerrainGraphicReference(1, 101) },
            new TerrainGraphicReference(1, 100));
        var source = new FakeFeatureSource(
            features: new[]
            {
                CandidateFeatures(new TerrainGraphicReference(1, 102), sides: new[] { SolidSide(0, 0, 204) }),
            },
            similarities: new[]
            {
                (new TerrainGraphicReference(1, 102), new TerrainGraphicReference(1, 100), 0.944912),
                (new TerrainGraphicReference(1, 102), new TerrainGraphicReference(1, 101), 0.944912),
            },
            candidates: new[]
            {
                (new TerrainGraphicReference(1, 100), new[] { new TerrainGraphicReference(1, 102) }),
                (new TerrainGraphicReference(1, 101), new[] { new TerrainGraphicReference(1, 102) }),
            });

        var result = TerrainImageMemberClassifier.Classify(CreateClassifierCorpus(), source, new[] { family }, Settings);
        var admission = Assert.Single(result.Admissions);
        Assert.Equal(new TerrainGraphicReference(1, 102), admission.Reference);
        Assert.Equal(0.944912, admission.Compatibility, 6);
        Assert.Equal(0.944912, admission.OwnerMargin, 6);
    }

    [Fact]
    public void Classify_MissingCentroidCannotOwnAndEmitsExactDiagnostic()
    {
        var family = CreateMissingCentroidFamily(
            new[] { new TerrainGraphicReference(1, 100), new TerrainGraphicReference(1, 101) },
            new TerrainGraphicReference(1, 100));
        var source = new FakeFeatureSource(
            features: new[]
            {
                CandidateFeatures(new TerrainGraphicReference(1, 102), sides: new[] { SolidSide(0, 0, 204) }),
            },
            similarities: new[]
            {
                (new TerrainGraphicReference(1, 102), new TerrainGraphicReference(1, 100), 1.0),
                (new TerrainGraphicReference(1, 102), new TerrainGraphicReference(1, 101), 1.0),
            },
            candidates: new[]
            {
                (new TerrainGraphicReference(1, 100), new[] { new TerrainGraphicReference(1, 102) }),
                (new TerrainGraphicReference(1, 101), new[] { new TerrainGraphicReference(1, 102) }),
            });

        var result = TerrainImageMemberClassifier.Classify(CreateClassifierCorpus(), source, new[] { family }, Settings);
        Assert.Empty(result.Admissions);
        Assert.Empty(result.RootDiagnostics);
        var entry = Assert.Single(result.SetDiagnostics);
        Assert.Equal(family, entry.Family);
        Assert.Equal("image-only-centroids-missing", entry.Diagnostic.Code);
        Assert.Equal("Image-only expansion skipped because connected/disconnected edge centroids are incomplete.", entry.Diagnostic.Message);
        Assert.Null(entry.Diagnostic.Reference);
        Assert.Null(entry.Diagnostic.Mask);
    }

    [Fact]
    public void Classify_IncompleteFamilyIsIneligibleOwnerAndCompleteFamilyWinsWithZeroRunnerUp()
    {
        var complete = CreateCompleteFamily(
            new[] { new TerrainGraphicReference(1, 100), new TerrainGraphicReference(1, 101) },
            new TerrainGraphicReference(1, 100));
        var incomplete = CreateMissingCentroidFamily(
            new[] { new TerrainGraphicReference(1, 110), new TerrainGraphicReference(1, 111) },
            new TerrainGraphicReference(1, 110));
        var source = new FakeFeatureSource(
            features: new[]
            {
                CandidateFeatures(new TerrainGraphicReference(1, 102), sides: new[] { SolidSide(0, 0, 204) }),
            },
            similarities: new[]
            {
                (new TerrainGraphicReference(1, 102), new TerrainGraphicReference(1, 110), 1.0),
                (new TerrainGraphicReference(1, 102), new TerrainGraphicReference(1, 111), 1.0),
                (new TerrainGraphicReference(1, 102), new TerrainGraphicReference(1, 100), 0.95),
                (new TerrainGraphicReference(1, 102), new TerrainGraphicReference(1, 101), 0.95),
            },
            candidates: new[]
            {
                (new TerrainGraphicReference(1, 100), new[] { new TerrainGraphicReference(1, 102) }),
                (new TerrainGraphicReference(1, 101), new[] { new TerrainGraphicReference(1, 102) }),
                (new TerrainGraphicReference(1, 110), new[] { new TerrainGraphicReference(1, 102) }),
                (new TerrainGraphicReference(1, 111), new[] { new TerrainGraphicReference(1, 102) }),
            });

        var result = TerrainImageMemberClassifier.Classify(CreateClassifierCorpus(), source, new[] { complete, incomplete }, Settings);

        var admission = Assert.Single(result.Admissions);
        Assert.Equal(complete, admission.Family);
        Assert.Equal(new TerrainGraphicReference(1, 102), admission.Reference);
        Assert.Equal(0.95, admission.Compatibility, 6);
        Assert.Equal(0.95, admission.OwnerMargin, 6);
        var missing = Assert.Single(result.SetDiagnostics);
        Assert.Equal(incomplete, missing.Family);
        Assert.Equal("image-only-centroids-missing", missing.Diagnostic.Code);
        Assert.Equal("Image-only expansion skipped because connected/disconnected edge centroids are incomplete.", missing.Diagnostic.Message);
        Assert.Empty(result.RootDiagnostics);
    }

    [Fact]
    public void Classify_NoOwnerExactTieNearTieOrSideCornerTieIsRejectedDeterministically()
    {
        var familyA = CreateCompleteFamily(
            new[] { new TerrainGraphicReference(1, 100), new TerrainGraphicReference(1, 101) },
            new TerrainGraphicReference(1, 100));
        var familyB = CreateCompleteFamily(
            new[] { new TerrainGraphicReference(1, 110), new TerrainGraphicReference(1, 111) },
            new TerrainGraphicReference(1, 101));
        var source = new FakeFeatureSource(
            features: new[]
            {
                CandidateFeatures(new TerrainGraphicReference(1, 102), sides: new[] { SolidSide(0, 0, 204) }),
                CandidateFeatures(new TerrainGraphicReference(1, 103), sides: new[] { SolidSide(0, 0, 255) }),
                CandidateFeatures(new TerrainGraphicReference(1, 104), sides: new[] { SolidSide(0, 0, 180) }),
                CandidateFeatures(new TerrainGraphicReference(1, 105), sides: new[] { SideTie }),
                CandidateFeatures(new TerrainGraphicReference(1, 106), corners: CornerTie, sides: new[] { SolidSide(0, 0, 204) }),
            },
            similarities: new[]
            {
                (new TerrainGraphicReference(1, 102), new TerrainGraphicReference(1, 100), 1.0),
                (new TerrainGraphicReference(1, 102), new TerrainGraphicReference(1, 101), 1.0),
                (new TerrainGraphicReference(1, 103), new TerrainGraphicReference(1, 100), 0.944912),
                (new TerrainGraphicReference(1, 103), new TerrainGraphicReference(1, 101), 0.944912),
                (new TerrainGraphicReference(1, 104), new TerrainGraphicReference(1, 100), 0.997802),
                (new TerrainGraphicReference(1, 104), new TerrainGraphicReference(1, 101), 0.995604),
                (new TerrainGraphicReference(1, 105), new TerrainGraphicReference(1, 100), 0.99),
                (new TerrainGraphicReference(1, 105), new TerrainGraphicReference(1, 101), 0.94),
                (new TerrainGraphicReference(1, 106), new TerrainGraphicReference(1, 100), 0.99),
                (new TerrainGraphicReference(1, 106), new TerrainGraphicReference(1, 101), 0.94),
            },
            candidates: new[]
            {
                (new TerrainGraphicReference(1, 100), new[] { new TerrainGraphicReference(1, 102), new TerrainGraphicReference(1, 103), new TerrainGraphicReference(1, 104), new TerrainGraphicReference(1, 105), new TerrainGraphicReference(1, 106) }),
                (new TerrainGraphicReference(1, 101), new[] { new TerrainGraphicReference(1, 102), new TerrainGraphicReference(1, 103), new TerrainGraphicReference(1, 104), new TerrainGraphicReference(1, 105), new TerrainGraphicReference(1, 106) }),
            });

        var result = TerrainImageMemberClassifier.Classify(CreateClassifierCorpus(), source, new[] { familyA, familyB }, Settings);

        Assert.Empty(result.Admissions);
        Assert.Empty(result.RootDiagnostics);
        var exactTie = result.SetDiagnostics.Where(entry => new TerrainGraphicReference(1, 102) == entry.Diagnostic.Reference).ToArray();
        Assert.Equal(2, exactTie.Length);
        Assert.Contains(exactTie, entry => entry.Family == familyA);
        Assert.Contains(exactTie, entry => entry.Family == familyB);
        Assert.All(exactTie, entry =>
        {
            Assert.Equal("image-only-ambiguous", entry.Diagnostic.Code);
            Assert.Equal("Rejected image-only (1,102): compatibility 1.000000, owner margin 0.000000.", entry.Diagnostic.Message);
        });

        var nearTie = Assert.Single(result.SetDiagnostics, entry => new TerrainGraphicReference(1, 104) == entry.Diagnostic.Reference);
        Assert.Equal(familyA, nearTie.Family);
        Assert.Equal("image-only-ambiguous", nearTie.Diagnostic.Code);
        Assert.Equal("Rejected image-only (1,104): compatibility 0.997802, owner margin 0.002198.", nearTie.Diagnostic.Message);

        var sideTie = Assert.Single(result.SetDiagnostics, entry => new TerrainGraphicReference(1, 105) == entry.Diagnostic.Reference);
        Assert.Equal(familyA, sideTie.Family);
        Assert.Equal("image-only-ambiguous", sideTie.Diagnostic.Code);
        Assert.Equal("Rejected image-only (1,105): compatibility 0.990000, owner margin 0.050000.", sideTie.Diagnostic.Message);

        var cornerTie = Assert.Single(result.SetDiagnostics, entry => new TerrainGraphicReference(1, 106) == entry.Diagnostic.Reference);
        Assert.Equal(familyA, cornerTie.Family);
        Assert.Equal("image-only-ambiguous", cornerTie.Diagnostic.Code);
        Assert.Equal("Rejected image-only (1,106): compatibility 0.990000, owner margin 0.050000.", cornerTie.Diagnostic.Message);
    }

    [Fact]
    public void Classify_AdmissionsUseSnapshotSoFamilyIterationCannotChangeOwner()
    {
        var familyA = CreateCompleteFamily(
            new[] { new TerrainGraphicReference(1, 100), new TerrainGraphicReference(1, 101) },
            new TerrainGraphicReference(1, 100));
        var familyB = CreateCompleteFamily(
            new[] { new TerrainGraphicReference(2, 200), new TerrainGraphicReference(2, 201) },
            new TerrainGraphicReference(2, 200));

        var source = new FakeFeatureSource(
            features: new[]
            {
                CandidateFeatures(new TerrainGraphicReference(1, 102), sides: new[] { SolidSide(0, 0, 204) }),
                CandidateFeatures(new TerrainGraphicReference(2, 202), sides: new[] { SolidSide(0, 0, 204) }),
            },
            similarities: new[]
            {
                (new TerrainGraphicReference(1, 102), new TerrainGraphicReference(1, 100), 1.0),
                (new TerrainGraphicReference(1, 102), new TerrainGraphicReference(1, 101), 1.0),
                (new TerrainGraphicReference(1, 102), new TerrainGraphicReference(2, 200), 0.749060),
                (new TerrainGraphicReference(1, 102), new TerrainGraphicReference(2, 201), 0.749060),
                (new TerrainGraphicReference(2, 202), new TerrainGraphicReference(2, 200), 1.0),
                (new TerrainGraphicReference(2, 202), new TerrainGraphicReference(2, 201), 1.0),
                (new TerrainGraphicReference(2, 202), new TerrainGraphicReference(1, 100), 0.749060),
                (new TerrainGraphicReference(2, 202), new TerrainGraphicReference(1, 101), 0.749060),
            },
            candidates: new[]
            {
                (new TerrainGraphicReference(1, 100), new[] { new TerrainGraphicReference(1, 102), new TerrainGraphicReference(2, 202) }),
                (new TerrainGraphicReference(1, 101), new[] { new TerrainGraphicReference(1, 102), new TerrainGraphicReference(2, 202) }),
                (new TerrainGraphicReference(2, 200), new[] { new TerrainGraphicReference(1, 102), new TerrainGraphicReference(2, 202) }),
                (new TerrainGraphicReference(2, 201), new[] { new TerrainGraphicReference(1, 102), new TerrainGraphicReference(2, 202) }),
            });

        var result = TerrainImageMemberClassifier.Classify(CreateClassifierCorpus(), source, new[] { familyA, familyB }, Settings);

        Assert.Equal(2, result.Admissions.Count);
        var admissionA = Assert.Single(result.Admissions, admission => new TerrainGraphicReference(1, 102) == admission.Reference);
        var admissionB = Assert.Single(result.Admissions, admission => new TerrainGraphicReference(2, 202) == admission.Reference);
        Assert.Equal(1.0, admissionA.Compatibility, 6);
        Assert.Equal(1.0, admissionA.OwnerMargin, 6);
        Assert.Equal(1.0, admissionB.Compatibility, 6);
        Assert.Equal(1.0, admissionB.OwnerMargin, 6);
        Assert.Empty(result.SetDiagnostics);
        Assert.Empty(result.RootDiagnostics);
    }

    private static TerrainCandidateFamily CreateCompleteFamily(
        TerrainGraphicReference[] members,
        TerrainGraphicReference medoid)
    {
        var side = new TerrainSideCentroid(SolidSide(0, 0, 204), SolidSide(0, 0, 0), 1.0, 1.0);
        var corner = new TerrainCornerCentroid(
            new double[5] { 0.5, 0.5, 0.5, 1.0, 0.5 },
            new double[5] { 0.1, 0.1, 0.1, 1.0, 0.1 },
            1.0,
            1.0);
        return new TerrainCandidateFamily(
            members,
            mapSupport: 2,
            regionSupport: 2,
            observationSupport: 4,
            diagonalSupport: 2,
            new Dictionary<TerrainGraphicReference, double>
            {
                [members[0]] = 1.0,
                [members[1]] = 1.0,
            },
            new Dictionary<TerrainGraphicReference, Dictionary<int, double>>(),
            medoid,
            new[] { side, side, side, side },
            new[] { corner, corner, corner, corner });
    }

    private static TerrainCandidateFamily CreateMissingCentroidFamily(
        TerrainGraphicReference[] members,
        TerrainGraphicReference medoid)
    {
        var side = new TerrainSideCentroid(null, null, 0.0, 0.0);
        return new TerrainCandidateFamily(
            members,
            mapSupport: 2,
            regionSupport: 2,
            observationSupport: 4,
            diagonalSupport: 2,
            new Dictionary<TerrainGraphicReference, double>
            {
                [members[0]] = 1.0,
                [members[1]] = 1.0,
            },
            new Dictionary<TerrainGraphicReference, Dictionary<int, double>>(),
            medoid,
            new[] { side, side, side, side },
            Array.Empty<TerrainCornerCentroid>());
    }

    private static TerrainCorpus CreateClassifierCorpus()
    {
        var frames = new[]
        {
            new TerrainFrame(new TerrainGraphicReference(1, 0), new TerrainFrameRect(288, 0, 32, 32)),
            new TerrainFrame(new TerrainGraphicReference(1, 100), new TerrainFrameRect(0, 0, 32, 32)),
            new TerrainFrame(new TerrainGraphicReference(1, 101), new TerrainFrameRect(32, 0, 32, 32)),
            new TerrainFrame(new TerrainGraphicReference(1, 102), new TerrainFrameRect(64, 0, 32, 32)),
            new TerrainFrame(new TerrainGraphicReference(1, 103), new TerrainFrameRect(96, 0, 32, 32)),
            new TerrainFrame(new TerrainGraphicReference(1, 104), new TerrainFrameRect(128, 0, 32, 32)),
            new TerrainFrame(new TerrainGraphicReference(1, 105), new TerrainFrameRect(160, 0, 32, 32)),
            new TerrainFrame(new TerrainGraphicReference(1, 106), new TerrainFrameRect(192, 0, 32, 32)),
            new TerrainFrame(new TerrainGraphicReference(1, 110), new TerrainFrameRect(224, 0, 32, 32)),
            new TerrainFrame(new TerrainGraphicReference(1, 111), new TerrainFrameRect(256, 0, 32, 32)),
            new TerrainFrame(new TerrainGraphicReference(2, 200), new TerrainFrameRect(0, 0, 32, 32)),
            new TerrainFrame(new TerrainGraphicReference(2, 201), new TerrainFrameRect(32, 0, 32, 32)),
            new TerrainFrame(new TerrainGraphicReference(2, 202), new TerrainFrameRect(64, 0, 32, 32)),
        };
        var bySheet = new Dictionary<int, IReadOnlyList<TerrainFrame>>
        {
            [1] = frames.Where(frame => frame.Reference.Sheet == 1).OrderBy(frame => frame.Reference.Graphic).ToList().AsReadOnly(),
            [2] = frames.Where(frame => frame.Reference.Sheet == 2).OrderBy(frame => frame.Reference.Graphic).ToList().AsReadOnly(),
        };
        var frameIndex = new TerrainFrameIndex(
            32,
            new[] { 1, 2 }.AsReadOnly(),
            frames.OrderBy(frame => frame.Reference.Sheet).ThenBy(frame => frame.Reference.Graphic).ToList().AsReadOnly(),
            bySheet);
        var observed = new[]
        {
            new TerrainGraphicReference(1, 100),
            new TerrainGraphicReference(1, 101),
            new TerrainGraphicReference(1, 110),
            new TerrainGraphicReference(1, 111),
            new TerrainGraphicReference(2, 200),
            new TerrainGraphicReference(2, 201),
        };
        return new TerrainCorpus(
            "/ac-terrain-classifier-root",
            "test-fingerprint",
            Array.Empty<TerrainMapDescriptor>(),
            frameIndex,
            observed,
            new[] { 1, 2 },
            Array.Empty<TerrainDiagnostic>());
    }

    private static double[] SolidSide(int r, int g, int b)
    {
        var luma = (54 * r + 183 * g + 19 * b) / 65025.0;
        var values = new double[160];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = (i % 5) switch
            {
                0 => r / 255.0,
                1 => g / 255.0,
                2 => b / 255.0,
                3 => 1.0,
                _ => luma,
            };
        }

        return values;
    }

    private static readonly double[] SideTie = BuildSideTie();

    private static double[] BuildSideTie()
    {
        var luma = 19 * 204 / 65025.0 / 2.0;
        var values = new double[160];
        for (var i = 0; i < values.Length; i++)
        {
            var channel = i % 5;
            values[i] = channel switch
            {
                2 => 0.4,
                3 => 1.0,
                4 => luma,
                _ => 0.0,
            };
        }

        return values;
    }

    private static readonly double[] CornerTie =
    [
        0.3, 0.3, 0.3, 1.0, 0.3,
        0.3, 0.3, 0.3, 1.0, 0.3,
        0.3, 0.3, 0.3, 1.0, 0.3,
        0.3, 0.3, 0.3, 1.0, 0.3,
    ];

    private static TerrainImageFeatures CandidateFeatures(TerrainGraphicReference reference, double[]? corners = null, params double[][] sides)
    {
        var edges = new double[TerrainImageFeatures.EdgeValueCount];
        for (var side = 0; side < 4; side++)
        {
            Array.Copy(sides[side % sides.Length], 0, edges, side * 160, 160);
        }

        var cornerValues = corners ?? new double[20];
        if (corners is null)
        {
            for (var corner = 0; corner < 4; corner++)
            {
                cornerValues[corner * 5 + 3] = 1.0;
            }
        }

        return new TerrainImageFeatures(
            reference,
            new double[64],
            new double[65],
            new double[320],
            cornerValues,
            edges,
            0UL,
            new TerrainFeatureBuckets(reference.Sheet, 9, 0, 1, 0, 0, 0, 0));
    }

    private sealed class FakeFeatureSource : ITerrainFeatureSource
    {
        public FakeFeatureSource(
            TerrainImageFeatures[] features,
            (TerrainGraphicReference, TerrainGraphicReference, double)[] similarities,
            (TerrainGraphicReference, TerrainGraphicReference[])[] candidates)
        {
            _features = features.ToDictionary(feature => feature.Reference);
            foreach (var (a, b, value) in similarities)
            {
                _similarities[(a, b)] = value;
                _similarities[(b, a)] = value;
            }

            _candidates = candidates.ToDictionary(entry => entry.Item1, entry => entry.Item2);
        }

        private readonly Dictionary<TerrainGraphicReference, TerrainImageFeatures> _features;
        private readonly Dictionary<(TerrainGraphicReference, TerrainGraphicReference), double> _similarities = new();
        private readonly Dictionary<TerrainGraphicReference, TerrainGraphicReference[]> _candidates;

        public IReadOnlyList<TerrainGraphicReference> QueryCandidates(TerrainGraphicReference reference)
            => _candidates.TryGetValue(reference, out var candidates) ? candidates : Array.Empty<TerrainGraphicReference>();

        public double Similarity(TerrainGraphicReference a, TerrainGraphicReference b)
            => _similarities.TryGetValue((a, b), out var value) ? value : 0.0;

        public bool TryGetFeatures(TerrainGraphicReference reference, out TerrainImageFeatures? features)
            => _features.TryGetValue(reference, out features);
    }
}
