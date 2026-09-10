namespace Goose2.AssetConverter.Terrain;

public static class TerrainCommand
{
    public static TerrainGenerationResult Execute(string repoRoot, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);

        var result = TerrainCatalogGenerator.Generate(repoRoot);
        output.WriteLine($"Terrain: {result.Enabled} enabled, {result.Pending} pending, {result.Disabled} disabled -> {result.OutputPath}");
        output.WriteLine($"Terrain fingerprint: {result.CorpusFingerprint}");
        foreach (var diagnostic in result.Diagnostics
                     .OrderBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
                     .ThenBy(diagnostic => diagnostic.Message, StringComparer.Ordinal))
        {
            output.WriteLine($"  WARN {diagnostic.Code}: {diagnostic.Message}");
        }

        return result;
    }
}
