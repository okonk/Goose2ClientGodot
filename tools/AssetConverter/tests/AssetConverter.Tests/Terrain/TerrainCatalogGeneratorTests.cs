using Goose2.AssetConverter.Terrain;
using MapEditor.Core.Terrain;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AssetConverter.Tests.Terrain;

public class TerrainCatalogGeneratorTests
{
    [Fact]
    public void Generate_SyntheticFourWayPipelineWritesParseableValidatedCatalog()
    {
        using var fixture = CreateFourWayFixture(out _);
        var result = TerrainCatalogGenerator.Generate(fixture.RepoRoot);
        var expectedPath = Path.GetFullPath(Path.Combine(fixture.RepoRoot, TerrainCatalogGenerator.RelativeOutputPath));
        Assert.Equal(expectedPath, result.OutputPath);
        Assert.True(File.Exists(expectedPath));

        var catalog = TerrainCatalogJson.Parse(File.ReadAllText(expectedPath));
        Assert.Empty(TerrainCatalogValidator.Validate(catalog));
        Assert.Equal(result.CorpusFingerprint, catalog.CorpusFingerprint);
        Assert.Empty(result.Diagnostics);

        var set = Assert.Single(catalog.Sets);
        Assert.Equal(TerrainTopology.FourWay, set.Topology);
        Assert.Equal(0, result.Enabled);
        Assert.Equal(1, result.Pending);
        Assert.Equal(0, result.Disabled);
    }

    [Fact]
    public void Generate_IntegratedEightWayPipelineSelectsEightWayByTop1Gain()
    {
        using var fixture = CreateEightWayFixture();
        var result = TerrainCatalogGenerator.Generate(fixture.RepoRoot);
        var catalog = TerrainCatalogJson.Parse(File.ReadAllText(result.OutputPath));
        var set = Assert.Single(catalog.Sets);
        Assert.Equal(TerrainTopology.EightWay, set.Topology);
        Assert.True(set.Metrics.EightWayAccuracyGain >= 0.05);
        Assert.True(set.Metrics.DiagonalSupport >= 32);
    }

    [Fact]
    public void Generate_ImageOnlyMemberIsConsideredAndRejectedWithDiagnostic()
    {
        using var fixture = CreateImageOnlyFixture();
        var result = TerrainCatalogGenerator.Generate(fixture.RepoRoot);
        var catalog = TerrainCatalogJson.Parse(File.ReadAllText(result.OutputPath));
        var set = Assert.Single(catalog.Sets);

        Assert.Equal(
            new[] { new TerrainGraphicReference(1, 100), new TerrainGraphicReference(1, 101) },
            set.Members.Select(member => member.Reference));
        Assert.All(set.Members, member => Assert.Equal(TerrainMemberProvenance.MapObserved, member.Provenance));
        Assert.DoesNotContain(set.Members, member => member.Reference == new TerrainGraphicReference(1, 102));
        Assert.Contains(set.Diagnostics, diagnostic =>
            diagnostic.Code == "image-only-ambiguous"
            && diagnostic.Message == "Rejected image-only (1,102): compatibility 1.000000, owner margin 1.000000.");
    }

    [Fact]
    public void Generate_IntegratedImageOnlyPipelinePersistsProvenanceMaskAndAdmissionEvidence()
    {
        using var fixture = CreateImageOnlyAdmittedFixture();
        var result = TerrainCatalogGenerator.Generate(fixture.RepoRoot);
        var catalog = TerrainCatalogJson.Parse(File.ReadAllText(result.OutputPath));
        var set = Assert.Single(catalog.Sets);

        var imageMember = set.Members.Single(member => member.Reference == new TerrainGraphicReference(1, 102));
        Assert.Equal(TerrainMemberProvenance.ImageOnly, imageMember.Provenance);

        var maskFive = set.Masks.Single(entry => entry.Mask == 5);
        Assert.Contains(new TerrainGraphicReference(1, 102), maskFive.Variants);

        var admitted = set.Diagnostics.Single(diagnostic => diagnostic.Code == "image-only-member-admitted");
        Assert.Equal(
            "Admitted image-only (1,102) at mask 0x05: compatibility 1.000000, owner margin 1.000000.",
            admitted.Message);
        Assert.Equal(5, admitted.Mask);
        Assert.Equal(new TerrainGraphicReference(1, 102), admitted.Reference);
    }

    [Fact]
    public void Generate_EquivalentRootsAndRepeatedRunsAreByteIdentical()
    {
        using var fixture = CreateFourWayFixture(out _);
        var first = TerrainCatalogGenerator.Generate(fixture.RepoRoot);
        var bytes = File.ReadAllBytes(first.OutputPath);
        var second = TerrainCatalogGenerator.Generate(fixture.RepoRoot);
        Assert.Equal(bytes, File.ReadAllBytes(second.OutputPath));

        var copyRoot = Path.Combine(Path.GetTempPath(), "ac_terrain_copy_" + Guid.NewGuid().ToString("N"));
        try
        {
            CopyTree(fixture.RepoRoot, copyRoot);
            var third = TerrainCatalogGenerator.Generate(copyRoot);
            Assert.Equal(bytes, File.ReadAllBytes(third.OutputPath));
            Assert.Equal(first.CorpusFingerprint, third.CorpusFingerprint);
        }
        finally
        {
            if (Directory.Exists(copyRoot))
            {
                Directory.Delete(copyRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void Generate_RelevantMutationChangesFingerprintAndOutputWhileIrrelevantMutationDoesNot()
    {
        using var fixture = CreateFourWayFixture(out var train1);
        var first = TerrainCatalogGenerator.Generate(fixture.RepoRoot);
        var bytes = File.ReadAllBytes(first.OutputPath);

        File.WriteAllText(Path.Combine(fixture.RepoRoot, "notes.txt"), "irrelevant");
        var second = TerrainCatalogGenerator.Generate(fixture.RepoRoot);
        Assert.Equal(first.CorpusFingerprint, second.CorpusFingerprint);
        Assert.Equal(bytes, File.ReadAllBytes(second.OutputPath));

        fixture.WriteMap(train1, 2, 1, new TerrainPlacement(0, 0, 1, 101));
        var third = TerrainCatalogGenerator.Generate(fixture.RepoRoot);
        Assert.NotEqual(first.CorpusFingerprint, third.CorpusFingerprint);
        Assert.NotEqual(bytes, File.ReadAllBytes(third.OutputPath));
    }

    [Fact]
    public void Generate_InputFailurePreservesPriorCatalogWithoutCreatingTemp()
    {
        using var fixture = CreateFourWayFixture(out var train1);
        var first = TerrainCatalogGenerator.Generate(fixture.RepoRoot);
        var prior = File.ReadAllBytes(first.OutputPath);

        fixture.WriteMapBytes(train1, new byte[] { 0xFF, 0xFF, 0xFF, 0xFF });
        var ex = Assert.Throws<TerrainGenerationException>(() => TerrainCatalogGenerator.Generate(fixture.RepoRoot));
        Assert.Equal(TerrainGenerationError.MapDecodeFailed, ex.Error);

        Assert.Equal(prior, File.ReadAllBytes(first.OutputPath));
        var destinationDirectory = Path.GetDirectoryName(first.OutputPath)!;
        Assert.Empty(Directory.GetFiles(destinationDirectory, "terrain-brushes.json.tmp-*"));
    }

    [Fact]
    public void Generate_InvalidInjectedBuilderOutputIsNeverPublished()
    {
        using var fixture = CreateFourWayFixture(out _);
        var first = TerrainCatalogGenerator.Generate(fixture.RepoRoot);
        var prior = File.ReadAllBytes(first.OutputPath);

        var store = new CountingStore();
        var invalid = BuildInvalidCatalog();
        var ex = Assert.Throws<TerrainGenerationException>(
            () => TerrainCatalogGenerator.Generate(fixture.RepoRoot, _ => invalid, store));
        Assert.Equal(TerrainGenerationError.InvalidGeneratedCatalog, ex.Error);
        Assert.Empty(store.Writes);
        Assert.Equal(prior, File.ReadAllBytes(first.OutputPath));
    }

    [Fact]
    public void Generate_InjectedBuilderAndStoreAreCalledOnceAndGeneratorOwnsOnlyOrchestration()
    {
        using var fixture = CreateFourWayFixture(out _);
        var catalog = new TerrainCatalog(
            1,
            "terrain-v1",
            "sha256:" + new string('0', 64),
            TerrainCandidateMiner.DefaultSettings,
            Array.Empty<TerrainSetDefinition>(),
            Array.Empty<TerrainDiagnostic>());
        var calls = 0;
        var store = new CountingStore();

        var result = TerrainCatalogGenerator.Generate(
            fixture.RepoRoot,
            _ =>
            {
                calls++;
                return catalog;
            },
            store);

        Assert.Equal(1, calls);
        var write = Assert.Single(store.Writes);
        Assert.Equal(TerrainCatalogJson.Serialize(catalog), write.Serialized);
        Assert.Equal(0, result.Enabled + result.Pending + result.Disabled);
        Assert.Equal(TerrainCorpusLoader.Load(fixture.RepoRoot).Fingerprint, result.CorpusFingerprint);
        Assert.Equal(Path.GetFullPath(Path.Combine(fixture.RepoRoot, TerrainCatalogGenerator.RelativeOutputPath)), result.OutputPath);
    }

    private static TerrainFixture CreateFourWayFixture(out string train1)
    {
        train1 = TerrainFixtureBuilder.FindMapFileName(5, heldOut: false);
        var train2 = TerrainFixtureBuilder.FindMapFileName(5, heldOut: false, start: 1000);
        var holdout = TerrainFixtureBuilder.FindMapFileName(5, heldOut: true, start: 2000);
        var definition = new TerrainFixtureDefinition();
        definition.AddMap(train1, 2, 1, new TerrainPlacement(0, 0, 1, 100), new TerrainPlacement(1, 0, 1, 101));
        definition.AddMap(train2, 2, 1, new TerrainPlacement(0, 0, 1, 100), new TerrainPlacement(1, 0, 1, 101));
        definition.AddMap(holdout, 2, 1, new TerrainPlacement(0, 0, 1, 100), new TerrainPlacement(1, 0, 1, 101));
        definition.AddSheet(1, 64, 32, new TerrainSheetFrame(100, 0, 0, 32, 32), new TerrainSheetFrame(101, 32, 0, 32, 32));
        var fixture = TerrainFixtureBuilder.Create(definition);
        WriteSolidSheet(fixture, 1,
        [
            (0, new Rgba32(10, 20, 30, 255)),
            (32, new Rgba32(10, 20, 30, 255)),
        ]);
        fixture.WriteInventory(train1, train2, holdout);
        return fixture;
    }

    private static TerrainFixture CreateEightWayFixture()
    {
        var trains = new[] { 1, 1000, 2000, 3000, 4000 }
            .Select(start => TerrainFixtureBuilder.FindMapFileName(5, heldOut: false, start: start))
            .ToArray();
        var holdout = TerrainFixtureBuilder.FindMapFileName(5, heldOut: true, start: 5000);
        var definition = new TerrainFixtureDefinition();
        foreach (var name in trains)
        {
            definition.AddMap(name, 10, 6, TrainingPlacements());
        }

        definition.AddMap(holdout, 10, 6,
            new TerrainPlacement(5, 0, 1, 101),
            new TerrainPlacement(5, 1, 1, 101),
            new TerrainPlacement(6, 1, 1, 101));
        definition.AddSheet(1, 64, 32, new TerrainSheetFrame(100, 0, 0, 32, 32), new TerrainSheetFrame(101, 32, 0, 32, 32));
        var fixture = TerrainFixtureBuilder.Create(definition);
        WriteSolidSheet(fixture, 1,
        [
            (0, new Rgba32(10, 20, 30, 255)),
            (32, new Rgba32(10, 20, 30, 255)),
        ]);
        fixture.WriteInventory(trains.Concat(new[] { holdout }).ToArray());
        return fixture;
    }

    private static TerrainPlacement[] TrainingPlacements() =>
    [
        new(0, 0, 1, 101),
        new(1, 0, 1, 101),
        new(0, 1, 1, 100),
        new(1, 1, 1, 100),
        new(5, 0, 1, 101),
        new(5, 1, 1, 101),
        new(6, 1, 1, 101),
        new(0, 4, 1, 101),
        new(1, 4, 1, 101),
        new(2, 4, 1, 101),
        new(3, 4, 1, 101),
        new(0, 5, 1, 100),
        new(1, 5, 1, 100),
        new(2, 5, 1, 100),
        new(3, 5, 1, 100),
    ];

    private static TerrainFixture CreateImageOnlyFixture()
    {
        var t1 = TerrainFixtureBuilder.FindMapFileName(5, heldOut: false);
        var t2 = TerrainFixtureBuilder.FindMapFileName(5, heldOut: false, start: 1000);
        var definition = new TerrainFixtureDefinition();
        definition.AddMap(t1, 2, 2,
            new TerrainPlacement(0, 0, 1, 100),
            new TerrainPlacement(1, 0, 1, 100),
            new TerrainPlacement(0, 1, 1, 101));
        definition.AddMap(t2, 2, 2,
            new TerrainPlacement(0, 0, 1, 100),
            new TerrainPlacement(0, 1, 1, 101));
        definition.AddSheet(1, 96, 32,
            new TerrainSheetFrame(100, 0, 0, 32, 32),
            new TerrainSheetFrame(101, 32, 0, 32, 32),
            new TerrainSheetFrame(102, 64, 0, 32, 32));
        var fixture = TerrainFixtureBuilder.Create(definition);
        WriteSolidSheet(fixture, 1,
        [
            (0, new Rgba32(0, 255, 0, 255)),
            (32, new Rgba32(63, 192, 63, 255)),
            (64, new Rgba32(0, 255, 0, 255)),
        ]);
        fixture.WriteInventory(t1, t2);
        return fixture;
    }

    private static void WriteSolidSheet(TerrainFixture fixture, int sheet, (int X, Rgba32 Color)[] frames)
    {
        using var image = new Image<Rgba32>(frames.Length * 32, 32);
        foreach (var (x, color) in frames)
        {
            for (var y = 0; y < 32; y++)
            {
                for (var i = 0; i < 32; i++)
                {
                    image[x + i, y] = color;
                }
            }
        }

        image.SaveAsPng(Path.Combine(fixture.SheetsDirectory, sheet + ".png"));
    }

    private static TerrainFixture CreateImageOnlyAdmittedFixture()
    {
        var t1 = TerrainFixtureBuilder.FindMapFileName(5, heldOut: false);
        var t2 = TerrainFixtureBuilder.FindMapFileName(5, heldOut: false, start: 1000);
        var placements = new[]
        {
            new TerrainPlacement(0, 0, 1, 101),
            new TerrainPlacement(0, 1, 1, 100),
            new TerrainPlacement(0, 2, 1, 100),
            new TerrainPlacement(0, 3, 1, 100),
            new TerrainPlacement(0, 4, 1, 100),
            new TerrainPlacement(0, 5, 1, 100),
            new TerrainPlacement(0, 6, 1, 100),
            new TerrainPlacement(0, 7, 1, 100),
            new TerrainPlacement(0, 8, 1, 101),
            new TerrainPlacement(2, 0, 1, 101),
            new TerrainPlacement(2, 1, 1, 100),
            new TerrainPlacement(2, 2, 1, 100),
            new TerrainPlacement(2, 3, 1, 100),
            new TerrainPlacement(2, 4, 1, 100),
            new TerrainPlacement(2, 5, 1, 100),
            new TerrainPlacement(2, 6, 1, 100),
            new TerrainPlacement(2, 7, 1, 100),
            new TerrainPlacement(2, 8, 1, 101),
            new TerrainPlacement(3, 0, 1, 101),
            new TerrainPlacement(4, 0, 1, 101),
        };
        var definition = new TerrainFixtureDefinition();
        definition.AddMap(t1, 5, 9, placements);
        definition.AddMap(t2, 5, 9, placements);
        definition.AddSheet(1, 96, 32,
            new TerrainSheetFrame(100, 0, 0, 32, 32),
            new TerrainSheetFrame(101, 32, 0, 32, 32),
            new TerrainSheetFrame(102, 64, 0, 32, 32));
        var fixture = TerrainFixtureBuilder.Create(definition);
        using var image = new Image<Rgba32>(96, 32);
        WriteRingFrame(image, 0, new Rgba32(0, 255, 0, 255), new Rgba32(0, 0, 0, 255));
        WriteRingFrame(image, 32, new Rgba32(0, 255, 0, 255), new Rgba32(180, 0, 0, 255));
        WriteRingFrame(image, 64, new Rgba32(0, 255, 0, 255), new Rgba32(0, 0, 0, 255));
        image.SaveAsPng(Path.Combine(fixture.SheetsDirectory, "1.png"));
        fixture.WriteInventory(t1, t2);
        return fixture;
    }

    private static void WriteRingFrame(Image<Rgba32> image, int x, Rgba32 interior, Rgba32 ring)
    {
        for (var y = 0; y < 32; y++)
        {
            for (var i = 0; i < 32; i++)
            {
                var border = y < 4 || y >= 28 || i < 4 || i >= 28;
                image[x + i, y] = border ? ring : interior;
            }
        }
    }

    private static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }

        foreach (var directory in Directory.GetDirectories(source))
        {
            CopyTree(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }

    private static TerrainCatalog BuildInvalidCatalog()
    {
        var reference = new TerrainGraphicReference(1, 100);
        var set = new TerrainSetDefinition(
            TerrainGeneratedId.Create(TerrainTopology.EightWay, new[] { reference }),
            "Generated 1-01234567",
            TerrainReviewStatus.Disabled,
            TerrainTopology.EightWay,
            new TerrainSetMetrics(0, 0, 0, 0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0),
            new[] { new TerrainMaskDefinition(16, Array.Empty<TerrainGraphicReference>()) },
            new[] { new TerrainMemberDefinition(reference, TerrainMemberProvenance.MapObserved) },
            Array.Empty<TerrainDiagnostic>());
        return new TerrainCatalog(
            1,
            "terrain-v1",
            "sha256:" + new string('0', 64),
            TerrainCandidateMiner.DefaultSettings,
            new[] { set },
            Array.Empty<TerrainDiagnostic>());
    }

    private sealed class CountingStore : ITerrainCatalogStore
    {
        public List<(string RepoRoot, string Serialized)> Writes { get; } = new();

        public void Write(string repoRoot, string serializedCatalog)
            => Writes.Add((repoRoot, serializedCatalog));
    }
}
