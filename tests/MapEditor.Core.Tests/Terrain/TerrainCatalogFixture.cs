using System;
using System.Collections.Generic;
using System.Linq;
using MapEditor.Core.Terrain;

namespace MapEditor.Core.Tests.Terrain;

internal static class TerrainCatalogFixture
{
    public static string Fingerprint { get; } = $"sha256:{new string('a', 64)}";

    public static TerrainGenerationSettings ValidSettings() =>
        new(2, 1, 1, 1, 1, 0.1, 0.5, 0.5, 0.1, 0.5, 0.9, 0.9);

    public static TerrainSetMetrics ValidMetrics() =>
        new(1, 1, 1, 0, 0.5, 0.5, 0.5, 0.5, 0.5, 0.0, 0.5);

    public static TerrainGraphicReference CreateReference(int sheet, int graphic) =>
        new(sheet, graphic);

    public static TerrainMemberDefinition CreateMember(
        int sheet,
        int graphic,
        TerrainMemberProvenance provenance = TerrainMemberProvenance.MapObserved) =>
        new(new TerrainGraphicReference(sheet, graphic), provenance);

    public static string CreateId(TerrainTopology topology, params TerrainMemberDefinition[] members) =>
        TerrainGeneratedId.Create(topology, members.Select(member => member.Reference));

    public static IReadOnlyList<TerrainMaskDefinition> CreateFullMasks(
        TerrainTopology topology,
        IEnumerable<TerrainMemberDefinition> members) =>
        TerrainMasks.Required(topology)
            .Select(mask => new TerrainMaskDefinition(mask, members.Select(member => member.Reference)))
            .ToList();

    public static TerrainSetDefinition CreateSet(
        TerrainTopology topology,
        IEnumerable<TerrainMemberDefinition> members,
        TerrainReviewStatus status = TerrainReviewStatus.Enabled,
        string? id = null,
        string? displayName = null,
        TerrainSetMetrics? metrics = null,
        IEnumerable<TerrainMaskDefinition>? masks = null,
        IEnumerable<TerrainDiagnostic> diagnostics = null)
    {
        var memberList = members.ToList();
        return new TerrainSetDefinition(
            id ?? TerrainGeneratedId.Create(topology, memberList.Select(member => member.Reference)),
            displayName ?? "Set",
            status,
            topology,
            metrics ?? ValidMetrics(),
            masks ?? CreateFullMasks(topology, memberList),
            memberList,
            diagnostics ?? Array.Empty<TerrainDiagnostic>());
    }

    public static TerrainCatalog CreateCatalog(params TerrainSetDefinition[] sets) =>
        new(
            TerrainCatalogJson.CurrentSchemaVersion,
            "gen/1.0",
            Fingerprint,
            ValidSettings(),
            sets,
            Array.Empty<TerrainDiagnostic>());

    public static TerrainCatalog CreateCatalog(
        int schemaVersion,
        string generatorVersion,
        string corpusFingerprint,
        TerrainGenerationSettings settings,
        IEnumerable<TerrainDiagnostic> diagnostics,
        params TerrainSetDefinition[] sets) =>
        new(schemaVersion, generatorVersion, corpusFingerprint, settings, sets, diagnostics);
}
