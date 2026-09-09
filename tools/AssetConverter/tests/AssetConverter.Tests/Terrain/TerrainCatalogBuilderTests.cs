using Goose2.AssetConverter.Terrain;
using MapEditor.Core.Terrain;

namespace AssetConverter.Tests.Terrain;

public class TerrainCatalogBuilderTests
{
    private const int HoldoutModulo = 5;

    private const string ValidFingerprint =
        "sha256:" + "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

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
    public void Build_StableIdIgnoresEditableAndObservationOrder()
    {
        var map1 = TerrainFixtureBuilder.FindMapFileName(HoldoutModulo, heldOut: false);
        var map2 = TerrainFixtureBuilder.FindMapFileName(HoldoutModulo, heldOut: false, start: 100);
        var holdout = TerrainFixtureBuilder.FindMapFileName(HoldoutModulo, heldOut: true);

        var first = RunPipeline(map1, map2, holdout);
        var second = RunPipeline(map2, map1, holdout);

        var setA = Assert.Single(first.Sets);
        var setB = Assert.Single(second.Sets);
        Assert.Equal(setA.Id, setB.Id);
        Assert.Equal(setA.Topology, setB.Topology);
        Assert.Equal(setA.Members.Select(member => member.Reference).ToArray(), setB.Members.Select(member => member.Reference).ToArray());

        var members = setA.Members.Select(member => member.Reference).ToArray();
        Assert.Equal(TerrainGeneratedId.Create(setA.Topology, members), setA.Id);
        Assert.Equal(setA.Id, TerrainGeneratedId.Create(setA.Topology, members.Reverse().ToArray()));

        var hash = setA.Id.AsSpan(setA.Id.Length - 64, 8).ToString();
        var lowestSheet = members.Min(member => member.Sheet);
        Assert.Equal($"Generated {lowestSheet}-{hash}", setA.DisplayName);
    }

    [Fact]
    public void Build_OverlappingEnabledCandidatesDeterministicallyDemotesLoserAndValidates()
    {
        var winnerMembers = Enumerable.Range(100, 16).Select(g => new TerrainGraphicReference(1, g)).ToArray();
        var shared = new TerrainGraphicReference(1, 115);
        var loserOnly = new TerrainGraphicReference(1, 116);

        var winnerId = TerrainGeneratedId.Create(TerrainTopology.FourWay, winnerMembers);
        var loserId = TerrainGeneratedId.Create(TerrainTopology.FourWay, new[] { shared, loserOnly });

        var winnerMasks = Enumerable.Range(0, 16)
            .Select(mask => new TerrainMaskDefinition(mask, new[] { winnerMembers[mask] }))
            .ToList();
        var winner = new TerrainSetDefinition(
            winnerId,
            "Generated 1-winner",
            TerrainReviewStatus.Enabled,
            TerrainTopology.FourWay,
            new TerrainSetMetrics(10, 20, 100, 0, 0.5, 1.0, 0.0, 0.9, 1.0, 0.0, 0.95),
            winnerMasks,
            winnerMembers.Select(member => new TerrainMemberDefinition(member, TerrainMemberProvenance.MapObserved)),
            Array.Empty<TerrainDiagnostic>());

        var loser = new TerrainSetDefinition(
            loserId,
            "Generated 1-loser",
            TerrainReviewStatus.Enabled,
            TerrainTopology.FourWay,
            new TerrainSetMetrics(100, 20, 100, 0, 0.5, 0.5, 0.1, 0.9, 0.9, 0.0, 0.90),
            Enumerable.Range(0, 16).Select(mask => new TerrainMaskDefinition(mask, Array.Empty<TerrainGraphicReference>())),
            new[]
            {
                new TerrainMemberDefinition(shared, TerrainMemberProvenance.MapObserved),
                new TerrainMemberDefinition(loserOnly, TerrainMemberProvenance.MapObserved),
            },
            Array.Empty<TerrainDiagnostic>());

        var resolved = TerrainCatalogBuilder.ResolveOverlaps(
            new[] { winner, loser },
            new[] { 0.95, 0.90 });

        Assert.Equal(TerrainReviewStatus.Enabled, resolved[0].Status);
        Assert.Equal(winnerId, resolved[0].Id);
        Assert.Equal(TerrainReviewStatus.Pending, resolved[1].Status);

        var conflict = Assert.Single(resolved[1].Diagnostics);
        Assert.Equal("enabled-member-conflict", conflict.Code);
        Assert.Equal($"Demoted from enabled because (1,115) is owned by higher-ranked terrain '{winnerId}'.", conflict.Message);
        Assert.Equal(shared, conflict.Reference);
        Assert.Null(conflict.Mask);

        var catalog = new TerrainCatalog(
            TerrainCatalogJson.CurrentSchemaVersion,
            "terrain-v1",
            ValidFingerprint,
            Settings,
            resolved,
            Array.Empty<TerrainDiagnostic>());
        Assert.Empty(TerrainCatalogValidator.Validate(catalog));
    }

    [Fact]
    public void Build_PendingAlternativesMayOverlap()
    {
        var shared = new TerrainGraphicReference(1, 115);
        var aOnly = new TerrainGraphicReference(1, 116);
        var bOnly = new TerrainGraphicReference(1, 117);

        var aId = TerrainGeneratedId.Create(TerrainTopology.FourWay, new[] { shared, aOnly });
        var bId = TerrainGeneratedId.Create(TerrainTopology.FourWay, new[] { shared, bOnly });

        TerrainSetDefinition Pending(string id, TerrainGraphicReference extra) => new(
            id,
            "Generated 1-pending",
            TerrainReviewStatus.Pending,
            TerrainTopology.FourWay,
            new TerrainSetMetrics(10, 20, 100, 0, 0.5, 0.5, 0.1, 0.9, 0.9, 0.0, 0.50),
            Enumerable.Range(0, 16).Select(mask => new TerrainMaskDefinition(mask, Array.Empty<TerrainGraphicReference>())),
            new[]
            {
                new TerrainMemberDefinition(shared, TerrainMemberProvenance.MapObserved),
                new TerrainMemberDefinition(extra, TerrainMemberProvenance.MapObserved),
            },
            new[] { new TerrainDiagnostic("support-below-minimum", "Support 10/20/100 is below required 5/8/64.") });

        var a = Pending(aId, aOnly);
        var b = Pending(bId, bOnly);

        var resolved = TerrainCatalogBuilder.ResolveOverlaps(new[] { a, b }, new[] { 0.50, 0.50 });

        Assert.Equal(TerrainReviewStatus.Pending, resolved[0].Status);
        Assert.Equal(TerrainReviewStatus.Pending, resolved[1].Status);
        Assert.DoesNotContain(resolved[0].Diagnostics, diagnostic => diagnostic.Code == "enabled-member-conflict");
        Assert.DoesNotContain(resolved[1].Diagnostics, diagnostic => diagnostic.Code == "enabled-member-conflict");

        var catalog = new TerrainCatalog(
            TerrainCatalogJson.CurrentSchemaVersion,
            "terrain-v1",
            ValidFingerprint,
            Settings,
            resolved,
            Array.Empty<TerrainDiagnostic>());
        Assert.Empty(TerrainCatalogValidator.Validate(catalog));
    }

    [Fact]
    public void Build_DiagnosticsAreDeduplicatedOrderedAndUseInvariantFixedValues()
    {
        var memberA = new TerrainGraphicReference(1, 100);
        var memberB = new TerrainGraphicReference(1, 101);
        var memberC = new TerrainGraphicReference(1, 102);

        var weighted = new Dictionary<TerrainGraphicReference, Dictionary<int, double>>
        {
            [memberA] = new() { [15] = 1.0 },
        };
        var referenceWeights = new Dictionary<TerrainGraphicReference, double> { [memberA] = 1.0 };
        var family = new TerrainCandidateFamily(
            new[] { memberA },
            mapSupport: 1,
            regionSupport: 1,
            observationSupport: 1,
            diagonalSupport: 0,
            referenceWeights,
            weighted,
            memberA,
            Array.Empty<TerrainSideCentroid>(),
            Array.Empty<TerrainCornerCentroid>());

        var admission = new TerrainImageMemberAdmission(family, memberB, 19, 0.987654321, 0.123456789);
        var setDiag = new TerrainDiagnostic("image-only-ambiguous", "Rejected image-only (1,102): compatibility 0.940000, owner margin 0.010000.", reference: memberC);

        var frames = new[]
        {
            new TerrainFrame(memberA, new TerrainFrameRect(0, 0, 32, 32)),
            new TerrainFrame(memberB, new TerrainFrameRect(32, 0, 32, 32)),
            new TerrainFrame(memberC, new TerrainFrameRect(64, 0, 32, 32)),
        };
        var bySheet = new Dictionary<int, IReadOnlyList<TerrainFrame>> { [1] = frames };
        var frameIndex = new TerrainFrameIndex(32, new[] { 1 }, frames, bySheet);

        var rootA = new TerrainDiagnostic("aaa-code", "root a");
        var rootM = new TerrainDiagnostic("mmm-code", "root m", mask: 5, reference: memberA);
        var rootZ = new TerrainDiagnostic("zzz-code", "root z");

        var corpus = new TerrainCorpus(
            "root",
            ValidFingerprint,
            Array.Empty<TerrainMapDescriptor>(),
            frameIndex,
            new[] { memberA, memberB },
            new[] { 1 },
            new[] { rootZ, rootZ });
        var mined = new TerrainCandidateMinerResult(
            new[] { family },
            new[] { memberA, memberB },
            new[] { rootZ, rootA });
        var classification = new TerrainImageMemberClassificationResult(
            new[] { admission },
            new[] { (family, setDiag), (family, setDiag) },
            new[] { rootA, rootM });

        var catalog = TerrainCatalogBuilder.Build(corpus, mined, classification, new FixedSimilaritySource(0.0), Settings);

        Assert.Equal(3, catalog.Diagnostics.Count);
        Assert.Equal(new[] { "aaa-code", "mmm-code", "zzz-code" }, catalog.Diagnostics.Select(diagnostic => diagnostic.Code).ToArray());
        Assert.Equal(rootM, catalog.Diagnostics[1]);

        var set = Assert.Single(catalog.Sets);
        Assert.Equal(
            new[]
            {
                "confidence-below-pending",
                "holdout-accuracy-below-enabled",
                "image-only-ambiguous",
                "image-only-member-admitted",
                "incomplete-required-masks",
                "no-holdout-observations",
                "support-below-minimum",
            },
            set.Diagnostics.Select(diagnostic => diagnostic.Code).ToArray());

        Assert.Equal(1, set.Diagnostics.Count(diagnostic => diagnostic.Code == "image-only-ambiguous"));

        var admitted = set.Diagnostics.Single(diagnostic => diagnostic.Code == "image-only-member-admitted");
        Assert.Equal(
            "Admitted image-only (1,101) at mask 0x03: compatibility 0.987654, owner margin 0.123457.",
            admitted.Message);
        Assert.Equal(3, admitted.Mask);
        Assert.Equal(memberB, admitted.Reference);
    }

    private static TerrainCatalog RunPipeline(string first, string second, string holdout)
    {
        var definition = new TerrainFixtureDefinition();
        definition.AddMap(first, 2, 2,
            new TerrainPlacement(0, 0, 1, 100),
            new TerrainPlacement(0, 1, 1, 101));
        definition.AddMap(second, 2, 2,
            new TerrainPlacement(1, 0, 1, 100),
            new TerrainPlacement(1, 1, 1, 101));
        definition.AddMap(holdout, 1, 1, new TerrainPlacement(0, 0, 1, 100));
        definition.AddSheet(1, 64, 32,
            new TerrainSheetFrame(100, 0, 0, 32, 32),
            new TerrainSheetFrame(101, 32, 0, 32, 32));
        using var fixture = TerrainFixtureBuilder.Create(definition);
        fixture.WriteInventory(first, second, holdout);
        var corpus = TerrainCorpusLoader.Load(fixture.RepoRoot);
        var features = new FakeFeatureSource(
            similarities: new[] { (new TerrainGraphicReference(1, 100), new TerrainGraphicReference(1, 101)) },
            Settings.MapMemberCompatibility,
            candidates: new Dictionary<TerrainGraphicReference, TerrainGraphicReference[]>
            {
                [new TerrainGraphicReference(1, 100)] = new[] { new TerrainGraphicReference(1, 101) },
                [new TerrainGraphicReference(1, 101)] = new[] { new TerrainGraphicReference(1, 100) },
            });
        var mined = TerrainCandidateMiner.Mine(corpus, features, Settings);
        var classification = TerrainImageMemberClassifier.Classify(corpus, features, mined.Families, Settings);
        return TerrainCatalogBuilder.Build(corpus, mined, classification, features, Settings);
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

    private sealed class FakeFeatureSource : ITerrainFeatureSource
    {
        private readonly Dictionary<(TerrainGraphicReference, TerrainGraphicReference), double> _similarities = new();
        private readonly Dictionary<TerrainGraphicReference, TerrainGraphicReference[]> _candidates;

        public FakeFeatureSource(
            (TerrainGraphicReference, TerrainGraphicReference)[] similarities,
            double value,
            Dictionary<TerrainGraphicReference, TerrainGraphicReference[]>? candidates = null)
        {
            foreach (var (a, b) in similarities)
            {
                _similarities[(a, b)] = value;
                _similarities[(b, a)] = value;
            }

            _candidates = candidates ?? new Dictionary<TerrainGraphicReference, TerrainGraphicReference[]>();
        }

        public IReadOnlyList<TerrainGraphicReference> QueryCandidates(TerrainGraphicReference reference)
            => _candidates.TryGetValue(reference, out var candidates) ? candidates : Array.Empty<TerrainGraphicReference>();

        public double Similarity(TerrainGraphicReference a, TerrainGraphicReference b)
            => _similarities.TryGetValue((a, b), out var value) ? value : 0.0;

        public bool TryGetFeatures(TerrainGraphicReference reference, out TerrainImageFeatures? features)
        {
            features = null;
            return false;
        }
    }
}
