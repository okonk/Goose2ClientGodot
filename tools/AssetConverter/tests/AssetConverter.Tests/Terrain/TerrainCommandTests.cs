using System.Diagnostics;
using Goose2.AssetConverter.Terrain;
using MapEditor.Core.Terrain;

namespace AssetConverter.Tests.Terrain;

public class TerrainCommandTests
{
    [Fact]
    public void Execute_SyntheticRootPrintsLockedSummaryAfterWritingCatalog()
    {
        using var fixture = CreateFixture();
        var catalogPath = Path.GetFullPath(Path.Combine(fixture.RepoRoot, TerrainCatalogGenerator.RelativeOutputPath));
        var output = new CatalogObservingWriter(catalogPath);

        var result = TerrainCommand.Execute(fixture.RepoRoot, output);

        var expected = $"Terrain: {result.Enabled} enabled, {result.Pending} pending, {result.Disabled} disabled -> {catalogPath}{Environment.NewLine}"
            + $"Terrain fingerprint: {result.CorpusFingerprint}{Environment.NewLine}"
            + string.Concat(result.Diagnostics
                .OrderBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.Message, StringComparer.Ordinal)
                .Select(diagnostic => $"  WARN {diagnostic.Code}: {diagnostic.Message}{Environment.NewLine}"));
        Assert.Equal(expected, output.ToString());
        Assert.True(output.CatalogWasReadableBeforeFirstWrite);
        Assert.Empty(TerrainCatalogValidator.Validate(TerrainCatalogJson.Parse(File.ReadAllText(catalogPath))));
    }

    [Fact]
    public async Task TerrainProcess_SyntheticRootExitsZeroAndWritesExpectedPath()
    {
        using var fixture = CreateFixture();

        var process = await RunTerrainProcess(fixture.RepoRoot);

        var expectedPath = Path.GetFullPath(Path.Combine(fixture.RepoRoot, TerrainCatalogGenerator.RelativeOutputPath));
        Assert.Equal(0, process.ExitCode);
        Assert.Equal(string.Empty, process.StandardError);
        Assert.True(File.Exists(expectedPath));
        var catalog = TerrainCatalogJson.Parse(File.ReadAllText(expectedPath));
        var enabled = catalog.Sets.Count(set => set.Status == TerrainReviewStatus.Enabled);
        var pending = catalog.Sets.Count(set => set.Status == TerrainReviewStatus.Pending);
        var disabled = catalog.Sets.Count(set => set.Status == TerrainReviewStatus.Disabled);
        var expectedOutput = $"Terrain: {enabled} enabled, {pending} pending, {disabled} disabled -> {expectedPath}{Environment.NewLine}"
            + $"Terrain fingerprint: {catalog.CorpusFingerprint}{Environment.NewLine}"
            + string.Concat(catalog.Diagnostics
                .OrderBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.Message, StringComparer.Ordinal)
                .Select(diagnostic => $"  WARN {diagnostic.Code}: {diagnostic.Message}{Environment.NewLine}"));
        Assert.Equal(expectedOutput, process.StandardOutput);
    }

    [Fact]
    public async Task TerrainProcess_InvalidRootExitsNonzeroAndPreservesPriorCatalog()
    {
        using var fixture = CreateFixture();
        var generated = TerrainCatalogGenerator.Generate(fixture.RepoRoot);
        var prior = File.ReadAllBytes(generated.OutputPath);
        File.Delete(Path.Combine(fixture.MapsDirectory, TerrainFixtureBuilder.StandardDefinition().Maps[0].FileName));

        var process = await RunTerrainProcess(fixture.RepoRoot);

        Assert.NotEqual(0, process.ExitCode);
        Assert.Equal(string.Empty, process.StandardOutput);
        Assert.NotEmpty(process.StandardError);
        Assert.Equal(prior, File.ReadAllBytes(generated.OutputPath));
    }

    [Fact]
    public async Task TerrainProcess_EquivalentRootsProduceByteIdenticalCatalogs()
    {
        using var first = CreateFixture();
        using var second = CreateFixture();

        var firstProcess = await RunTerrainProcess(first.RepoRoot);
        var secondProcess = await RunTerrainProcess(second.RepoRoot);

        Assert.Equal(0, firstProcess.ExitCode);
        Assert.Equal(0, secondProcess.ExitCode);
        Assert.Equal(
            File.ReadAllBytes(Path.Combine(first.RepoRoot, TerrainCatalogGenerator.RelativeOutputPath)),
            File.ReadAllBytes(Path.Combine(second.RepoRoot, TerrainCatalogGenerator.RelativeOutputPath)));
    }

    [Fact]
    public void Execute_RepeatedSameInputsIsByteIdentical()
    {
        using var fixture = CreateFixture();
        TerrainCommand.Execute(fixture.RepoRoot, TextWriter.Null);
        var path = Path.Combine(fixture.RepoRoot, TerrainCatalogGenerator.RelativeOutputPath);
        var first = File.ReadAllBytes(path);

        TerrainCommand.Execute(fixture.RepoRoot, TextWriter.Null);

        Assert.Equal(first, File.ReadAllBytes(path));
    }

    private static TerrainFixture CreateFixture()
    {
        var definition = TerrainFixtureBuilder.StandardDefinition();
        var fixture = TerrainFixtureBuilder.Create(definition);
        fixture.WriteInventory(definition.Maps.Select(map => map.FileName).ToArray());
        return fixture;
    }

    private static async Task<ProcessResult> RunTerrainProcess(string repoRoot)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(typeof(TerrainCommand).Assembly.Location);
        startInfo.ArgumentList.Add("terrain");
        startInfo.ArgumentList.Add(repoRoot);
        using var process = Process.Start(startInfo)!;
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(process.ExitCode, await standardOutput, await standardError);
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

    private sealed class CatalogObservingWriter(string catalogPath) : StringWriter
    {
        private bool _observed;

        public bool CatalogWasReadableBeforeFirstWrite { get; private set; }

        public override void WriteLine(string? value)
        {
            Observe();
            base.WriteLine(value);
        }

        private void Observe()
        {
            if (_observed)
            {
                return;
            }

            _observed = true;
            using var stream = File.Open(catalogPath, FileMode.Open, FileAccess.Read, FileShare.None);
            CatalogWasReadableBeforeFirstWrite = stream.Length > 0;
        }
    }
}
