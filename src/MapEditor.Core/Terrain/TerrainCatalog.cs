using System;
using System.Collections.Generic;

namespace MapEditor.Core.Terrain;

public enum TerrainTopology
{
    FourWay,
    EightWay
}

public enum TerrainReviewStatus
{
    Enabled,
    Pending,
    Disabled
}

public enum TerrainMemberProvenance
{
    MapObserved,
    ImageOnly
}

public readonly record struct TerrainGraphicReference(int Sheet, int Graphic);

public sealed record TerrainGenerationSettings(
    int HoldoutModulo,
    int MinimumMapSupport,
    int MinimumRegionSupport,
    int MinimumObservationSupport,
    int MinimumDiagonalSupport,
    double MinimumEightWayAccuracyGain,
    double MapMemberCompatibility,
    double ImageOnlyCompatibility,
    double MinimumClassificationMargin,
    double MaximumAmbiguity,
    double EnabledConfidence,
    double PendingConfidence);

public sealed record TerrainSetMetrics(
    int MapSupport,
    int RegionSupport,
    int ObservationSupport,
    int DiagonalSupport,
    double MaskEntropy,
    double Completeness,
    double Ambiguity,
    double VisualCompatibility,
    double HoldoutAccuracy,
    double EightWayAccuracyGain,
    double Confidence);

public sealed record TerrainMaskDefinition
{
    public int Mask { get; }
    public IReadOnlyList<TerrainGraphicReference> Variants { get; }

    public TerrainMaskDefinition(int mask, IEnumerable<TerrainGraphicReference> variants)
    {
        Mask = mask;
        Variants = TerrainCollections.AsReadOnly(variants, nameof(variants));
    }
}

public sealed record TerrainMemberDefinition
{
    public TerrainGraphicReference Reference { get; }
    public TerrainMemberProvenance Provenance { get; }

    public TerrainMemberDefinition(TerrainGraphicReference reference, TerrainMemberProvenance provenance)
    {
        Reference = reference;
        Provenance = provenance;
    }
}

public sealed record TerrainDiagnostic
{
    public string Code { get; }
    public string Message { get; }
    public int? Mask { get; }
    public TerrainGraphicReference? Reference { get; }

    public TerrainDiagnostic(string code, string message, int? mask = null, TerrainGraphicReference? reference = null)
    {
        Code = code ?? throw new ArgumentNullException(nameof(code));
        Message = message ?? throw new ArgumentNullException(nameof(message));
        Mask = mask;
        Reference = reference;
    }
}

public sealed record TerrainSetDefinition
{
    public string Id { get; }
    public string DisplayName { get; }
    public TerrainReviewStatus Status { get; }
    public TerrainTopology Topology { get; }
    public TerrainSetMetrics Metrics { get; }
    public IReadOnlyList<TerrainMaskDefinition> Masks { get; }
    public IReadOnlyList<TerrainMemberDefinition> Members { get; }
    public IReadOnlyList<TerrainDiagnostic> Diagnostics { get; }

    public TerrainSetDefinition(
        string id,
        string displayName,
        TerrainReviewStatus status,
        TerrainTopology topology,
        TerrainSetMetrics metrics,
        IEnumerable<TerrainMaskDefinition> masks,
        IEnumerable<TerrainMemberDefinition> members,
        IEnumerable<TerrainDiagnostic> diagnostics)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        Status = status;
        Topology = topology;
        Metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        Masks = TerrainCollections.AsReadOnly(masks, nameof(masks));
        Members = TerrainCollections.AsReadOnly(members, nameof(members));
        Diagnostics = TerrainCollections.AsReadOnly(diagnostics, nameof(diagnostics));
    }
}

public sealed record TerrainCatalog
{
    public int SchemaVersion { get; }
    public string GeneratorVersion { get; }
    public string CorpusFingerprint { get; }
    public TerrainGenerationSettings Settings { get; }
    public IReadOnlyList<TerrainSetDefinition> Sets { get; }
    public IReadOnlyList<TerrainDiagnostic> Diagnostics { get; }

    public TerrainCatalog(
        int schemaVersion,
        string generatorVersion,
        string corpusFingerprint,
        TerrainGenerationSettings settings,
        IEnumerable<TerrainSetDefinition> sets,
        IEnumerable<TerrainDiagnostic> diagnostics)
    {
        SchemaVersion = schemaVersion;
        GeneratorVersion = generatorVersion ?? throw new ArgumentNullException(nameof(generatorVersion));
        CorpusFingerprint = corpusFingerprint ?? throw new ArgumentNullException(nameof(corpusFingerprint));
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        Sets = TerrainCollections.AsReadOnly(sets, nameof(sets));
        Diagnostics = TerrainCollections.AsReadOnly(diagnostics, nameof(diagnostics));
    }
}

internal static class TerrainCollections
{
    public static IReadOnlyList<T> AsReadOnly<T>(IEnumerable<T> source, string parameterName)
    {
        if (source is null)
        {
            throw new ArgumentNullException(parameterName);
        }

        var items = new List<T>();
        foreach (var item in source)
        {
            if (item is null)
            {
                throw new ArgumentNullException(parameterName);
            }

            items.Add(item);
        }

        return Array.AsReadOnly(items.ToArray());
    }
}
