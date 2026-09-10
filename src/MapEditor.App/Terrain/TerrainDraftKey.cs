namespace MapEditor.App.Terrain;

internal readonly record struct TerrainDraftKey(int Value);

internal sealed record TerrainDraftMutationResult
{
    public bool Succeeded { get; }
    public TerrainDraftKey Key { get; }
    public IReadOnlyList<MapEditor.Core.Terrain.TerrainValidationIssue> Issues { get; }

    internal TerrainDraftMutationResult(bool succeeded, TerrainDraftKey key, IEnumerable<MapEditor.Core.Terrain.TerrainValidationIssue> issues)
    {
        Succeeded = succeeded;
        Key = key;
        Issues = Array.AsReadOnly(issues.ToArray());
    }
}
