using MapEditor.Core.Terrain;

namespace Goose2.AssetConverter.Terrain;

public sealed record TerrainMapDescriptor(string Identity, int Width, int Height, int EligiblePlacements);

public sealed class TerrainCorpus
{
    public string Fingerprint { get; }
    public IReadOnlyList<TerrainMapDescriptor> Maps { get; }
    public TerrainFrameIndex FrameIndex { get; }
    public IReadOnlyCollection<TerrainGraphicReference> ObservedReferences { get; }
    public IReadOnlyList<int> RelevantSheets { get; }
    public IReadOnlyList<TerrainDiagnostic> Diagnostics { get; }

    internal TerrainCorpus(
        string fingerprint,
        IReadOnlyList<TerrainMapDescriptor> maps,
        TerrainFrameIndex frameIndex,
        IReadOnlyCollection<TerrainGraphicReference> observedReferences,
        IReadOnlyList<int> relevantSheets,
        IReadOnlyList<TerrainDiagnostic> diagnostics)
    {
        Fingerprint = fingerprint;
        Maps = maps;
        FrameIndex = frameIndex;
        ObservedReferences = observedReferences.OrderBy(r => r.Sheet).ThenBy(r => r.Graphic).ToList().AsReadOnly();
        RelevantSheets = relevantSheets;
        Diagnostics = diagnostics;
    }
}
