namespace Goose2.AssetConverter.Terrain;

internal static class TerrainAllFinalizer
{
    internal static TerrainGenerationResult Execute(
        string repoRoot,
        IEnumerable<string> successfulMapFileNames,
        Func<string> buildCombinedManifest,
        TextWriter output,
        Func<string, TextWriter, TerrainGenerationResult> runTerrain)
    {
        ArgumentNullException.ThrowIfNull(repoRoot);
        ArgumentNullException.ThrowIfNull(successfulMapFileNames);
        ArgumentNullException.ThrowIfNull(buildCombinedManifest);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(runTerrain);

        var root = Path.GetFullPath(repoRoot);
        var mapsDirectory = Path.Combine(root, TerrainMapInventory.MapsDirectory);
        var names = successfulMapFileNames
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal);
        TerrainMapInventory.Write(mapsDirectory, names);

        var manifestPath = Path.Combine(root, "Assets", "Sprites", "manifest.json");
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        File.WriteAllText(manifestPath, buildCombinedManifest());

        return runTerrain(root, output);
    }
}
