namespace Goose2.AssetConverter.Terrain;

public static class TerrainCorpusFingerprint
{
    public static string Compute(string repoRoot)
        => TerrainCorpusLoader.Load(repoRoot).Fingerprint;
}
