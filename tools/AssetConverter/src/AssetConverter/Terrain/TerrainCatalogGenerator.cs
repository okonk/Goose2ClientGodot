using MapEditor.Core.Terrain;

namespace Goose2.AssetConverter.Terrain;

public sealed record TerrainGenerationResult(
    string OutputPath,
    string CorpusFingerprint,
    int Enabled,
    int Pending,
    int Disabled,
    IReadOnlyList<TerrainDiagnostic> Diagnostics);

public static class TerrainCatalogGenerator
{
    public const string RelativeOutputPath = TerrainCatalogFileStore.RelativePath;

    public static TerrainGenerationResult Generate(string repoRoot)
        => Generate(repoRoot, BuildCatalog, new TerrainCatalogFileStore());

    internal static TerrainGenerationResult Generate(
        string repoRoot,
        Func<TerrainCorpus, TerrainCatalog> builder,
        ITerrainCatalogStore store)
    {
        ArgumentNullException.ThrowIfNull(repoRoot);
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(store);

        var root = Path.GetFullPath(repoRoot);
        var corpus = TerrainCorpusLoader.Load(root);
        var catalog = builder(corpus);

        var issues = TerrainCatalogValidator.Validate(catalog);
        if (issues.Count > 0)
        {
            throw new TerrainGenerationException(
                TerrainGenerationError.InvalidGeneratedCatalog,
                "Generated terrain catalog failed validation: " + string.Join(" ", issues.Select(issue => issue.Message)));
        }

        var serialized = TerrainCatalogJson.Serialize(catalog);
        store.Write(root, serialized);

        var enabled = 0;
        var pending = 0;
        var disabled = 0;
        foreach (var set in catalog.Sets)
        {
            switch (set.Status)
            {
                case TerrainReviewStatus.Enabled:
                    enabled++;
                    break;
                case TerrainReviewStatus.Pending:
                    pending++;
                    break;
                default:
                    disabled++;
                    break;
            }
        }

        return new TerrainGenerationResult(
            Path.GetFullPath(Path.Combine(root, RelativeOutputPath)),
            corpus.Fingerprint,
            enabled,
            pending,
            disabled,
            catalog.Diagnostics.ToList().AsReadOnly());
    }

    private static TerrainCatalog BuildCatalog(TerrainCorpus corpus)
    {
        var settings = TerrainCandidateMiner.DefaultSettings;
        var features = TerrainFeatureCache.Build(corpus);
        var mined = TerrainCandidateMiner.Mine(corpus, features, settings);
        var classification = TerrainImageMemberClassifier.Classify(corpus, features, mined.Families, settings);
        return TerrainCatalogBuilder.Build(corpus, mined, classification, features, settings);
    }
}
