using Goose2.AssetConverter.Terrain;

namespace AssetConverter.Tests.Terrain;

public class TerrainAllFinalizerTests
{
    [Fact]
    public void AllFinalizer_ProductionPathWritesSuccessfulInventoryThenManifestThenCallsExactTerrainRunner()
    {
        using var fixture = CreateFixture();
        var mapNames = fixture.Definition.Maps.Select(map => map.FileName).Reverse().ToArray();
        var manifest = "{\"complete\":true}";
        var events = new List<string>();
        var expectedResult = ResultFor(fixture.RepoRoot);

        var result = TerrainAllFinalizer.Execute(
            fixture.RepoRoot,
            mapNames,
            () =>
            {
                events.Add("manifest-built");
                Assert.Equal(
                    TerrainMapInventory.Header + "\n" + string.Join("\n", mapNames.Order(StringComparer.Ordinal)) + "\n",
                    File.ReadAllText(Path.Combine(fixture.MapsDirectory, TerrainMapInventory.FileName)));
                using var inventory = File.Open(
                    Path.Combine(fixture.MapsDirectory, TerrainMapInventory.FileName),
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.None);
                return manifest;
            },
            TextWriter.Null,
            (root, output) =>
            {
                events.Add("terrain");
                Assert.Equal(Path.GetFullPath(fixture.RepoRoot), root);
                Assert.Same(TextWriter.Null, output);
                var manifestPath = Path.Combine(fixture.RepoRoot, "Assets", "Sprites", "manifest.json");
                Assert.Equal(manifest, File.ReadAllText(manifestPath));
                using var manifestStream = File.Open(manifestPath, FileMode.Open, FileAccess.Read, FileShare.None);
                return expectedResult;
            });

        Assert.Same(expectedResult, result);
        Assert.Equal(new[] { "manifest-built", "terrain" }, events);
    }

    [Fact]
    public void AllFinalizer_PreseededStaleMapIsAbsentFromInventoryAndCannotAffectTerrainBytes()
    {
        using var fixture = CreateFixture();
        var names = fixture.Definition.Maps.Select(map => map.FileName).ToArray();
        var manifest = File.ReadAllText(Path.Combine(fixture.RepoRoot, "Assets", "Sprites", "manifest.json"));
        TerrainAllFinalizer.Execute(
            fixture.RepoRoot,
            names,
            () => manifest,
            TextWriter.Null,
            TerrainCommand.Execute);
        var catalogPath = Path.Combine(fixture.RepoRoot, TerrainCatalogGenerator.RelativeOutputPath);
        var expected = File.ReadAllBytes(catalogPath);

        fixture.Fixture.WriteMap("Map999.map", 1, 1, new TerrainPlacement(0, 0, 99, 999));
        TerrainAllFinalizer.Execute(
            fixture.RepoRoot,
            names,
            () => manifest,
            TextWriter.Null,
            TerrainCommand.Execute);

        Assert.DoesNotContain("Map999.map", TerrainMapInventory.Read(fixture.RepoRoot).FileNames);
        Assert.Equal(expected, File.ReadAllBytes(catalogPath));
    }

    [Fact]
    public void AllFinalizer_TerrainFailurePrintsNoSuccessAndPreservesPriorCatalog()
    {
        using var fixture = CreateFixture();
        var names = fixture.Definition.Maps.Select(map => map.FileName).ToArray();
        fixture.Fixture.WriteInventory(names);
        var generated = TerrainCatalogGenerator.Generate(fixture.RepoRoot);
        var prior = File.ReadAllBytes(generated.OutputPath);
        var manifest = File.ReadAllText(Path.Combine(fixture.RepoRoot, "Assets", "Sprites", "manifest.json"));
        fixture.Fixture.WriteMapBytes(names[0], [0xFF, 0xFF, 0xFF, 0xFF]);
        var output = new StringWriter();

        Assert.ThrowsAny<Exception>(() => TerrainAllFinalizer.Execute(
            fixture.RepoRoot,
            names,
            () => manifest,
            output,
            TerrainCommand.Execute));

        Assert.Equal(string.Empty, output.ToString());
        Assert.Equal(prior, File.ReadAllBytes(generated.OutputPath));
    }

    [Fact]
    public void AllCommandDelegatesManifestAndTerrainFinalizationOnlyToTerrainAllFinalizer()
    {
        var programSource = ReadProgramSource();
        Assert.Contains("TerrainAllFinalizer.Execute", programSource);
        Assert.DoesNotContain("File.WriteAllText(manifestPath", programSource);
    }

    [Fact]
    public void AsperetaCommandWritesCanonicalInventory()
    {
        var programSource = ReadProgramSource();
        var marker = "args[0] == \"aspereta\"";
        var start = programSource.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, "aspereta command not found in Program.cs");
        var nextCommand = programSource.IndexOf("args[0] ==", start + marker.Length, StringComparison.Ordinal);
        var blockEnd = nextCommand >= 0 ? nextCommand : programSource.Length;
        var asperetaBlock = programSource[start..blockEnd];
        Assert.Contains("TerrainMapInventory.Write", asperetaBlock);
    }

    private static string ReadProgramSource()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(TerrainAllFinalizerTests).Assembly.Location)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "AssetConverter", "Program.cs")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException("Could not locate the AssetConverter source root from the test assembly location.");
        }

        return File.ReadAllText(Path.Combine(dir.FullName, "src", "AssetConverter", "Program.cs"));
    }

    private static FixtureScope CreateFixture()
    {
        var definition = TerrainFixtureBuilder.StandardDefinition();
        return new FixtureScope(TerrainFixtureBuilder.Create(definition), definition);
    }

    private static TerrainGenerationResult ResultFor(string repoRoot) => new(
        Path.GetFullPath(Path.Combine(repoRoot, TerrainCatalogGenerator.RelativeOutputPath)),
        "sha256:" + new string('0', 64),
        0,
        0,
        0,
        Array.Empty<MapEditor.Core.Terrain.TerrainDiagnostic>());

    private sealed class FixtureScope(TerrainFixture fixture, TerrainFixtureDefinition definition) : IDisposable
    {
        public TerrainFixture Fixture { get; } = fixture;
        public TerrainFixtureDefinition Definition { get; } = definition;
        public string RepoRoot => Fixture.RepoRoot;
        public string MapsDirectory => Fixture.MapsDirectory;
        public void Dispose() => Fixture.Dispose();
    }
}
