using MapEditor.Core.Terrain;

namespace Goose2.AssetConverter.Terrain;

public interface ITerrainFeatureSource
{
    IReadOnlyList<TerrainGraphicReference> QueryCandidates(TerrainGraphicReference reference);

    double Similarity(TerrainGraphicReference a, TerrainGraphicReference b);

    bool TryGetFeatures(TerrainGraphicReference reference, out TerrainImageFeatures? features);
}

public sealed record TerrainSideCentroid(
    double[]? Connected,
    double[]? Disconnected,
    double ConnectedWeight,
    double DisconnectedWeight);

public sealed record TerrainCornerCentroid(
    double[]? Present,
    double[]? Absent,
    double PresentWeight,
    double AbsentWeight);

public sealed class TerrainCandidateFamily
{
    public IReadOnlyList<TerrainGraphicReference> Members { get; }
    public int MapSupport { get; internal set; }
    public int RegionSupport { get; internal set; }
    public int ObservationSupport { get; internal set; }
    public int DiagonalSupport { get; internal set; }
    public Dictionary<TerrainGraphicReference, double> ReferenceWeights { get; internal set; }
    public Dictionary<TerrainGraphicReference, Dictionary<int, double>> WeightedMasks { get; internal set; }
    public TerrainGraphicReference Medoid { get; internal set; }
    public IReadOnlyList<TerrainSideCentroid> SideCentroids { get; internal set; }
    public IReadOnlyList<TerrainCornerCentroid> CornerCentroids { get; internal set; }

    internal TerrainCandidateFamily(
        IReadOnlyList<TerrainGraphicReference> members,
        int mapSupport,
        int regionSupport,
        int observationSupport,
        int diagonalSupport,
        Dictionary<TerrainGraphicReference, double> referenceWeights,
        Dictionary<TerrainGraphicReference, Dictionary<int, double>> weightedMasks,
        TerrainGraphicReference medoid,
        IReadOnlyList<TerrainSideCentroid> sideCentroids,
        IReadOnlyList<TerrainCornerCentroid> cornerCentroids)
    {
        Members = members;
        MapSupport = mapSupport;
        RegionSupport = regionSupport;
        ObservationSupport = observationSupport;
        DiagonalSupport = diagonalSupport;
        ReferenceWeights = referenceWeights;
        WeightedMasks = weightedMasks;
        Medoid = medoid;
        SideCentroids = sideCentroids;
        CornerCentroids = cornerCentroids;
    }
}

public sealed record TerrainCandidateMinerResult(
    IReadOnlyList<TerrainCandidateFamily> Families,
    IReadOnlyCollection<TerrainGraphicReference> TrainingObserved,
    IReadOnlyList<TerrainDiagnostic> RootDiagnostics);
