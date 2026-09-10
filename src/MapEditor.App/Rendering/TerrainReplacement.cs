using System;
using System.Collections.Generic;
using System.Linq;
using MapEditor.App.Terrain;
using MapEditor.App.ViewModels;
using MapEditor.Rendering.Terrain;

namespace MapEditor.App.Rendering;

internal sealed record TerrainOperationWarning(string Scope, string Message, Exception Exception);

internal sealed class TerrainManagerPublicationPlan
{
    internal TerrainManagerPublicationPlan(
        TerrainSetsManagerViewModel manager,
        TerrainDraftAcceptance acceptance,
        IEnumerable<string> properties)
    {
        Manager = manager;
        Acceptance = acceptance;
        Properties = Array.AsReadOnly(properties.ToArray());
    }

    internal TerrainSetsManagerViewModel Manager { get; }
    internal TerrainDraftAcceptance Acceptance { get; }
    internal IReadOnlyList<string> Properties { get; }
    internal bool IsConsumed { get; set; }
}

internal sealed record TerrainReplacementPreparation
{
    public bool ContextMatches { get; }
    public TerrainReplacementPlan? Plan { get; }
    public IReadOnlyList<Exception> CancellationFailures { get; }
    public bool Succeeded => ContextMatches && Plan is not null && CancellationFailures.Count == 0;

    internal TerrainReplacementPreparation(
        bool contextMatches,
        TerrainReplacementPlan? plan,
        IEnumerable<Exception> cancellationFailures)
    {
        ContextMatches = contextMatches;
        Plan = plan;
        CancellationFailures = cancellationFailures.ToList().AsReadOnly();
    }
}

internal enum TerrainReplacementPlanState
{
    Reserved,
    Published,
    Abandoned
}

internal sealed class TerrainReplacementPlan
{
    internal TerrainReplacementPlan(AssetContextController owner)
    {
        Owner = owner;
    }

    internal AssetContextController Owner { get; }
    internal TerrainReplacementPlanState State { get; set; }
    internal AssetContext? Candidate { get; set; }
    internal AssetContext? Replaced { get; set; }
    internal IReadOnlyList<PlannedDocumentAssetState> Documents { get; set; } = Array.Empty<PlannedDocumentAssetState>();
    internal TerrainManagerPublicationPlan? ManagerPublication { get; set; }
}

internal sealed record TerrainPublicationResult
{
    public IReadOnlyList<TerrainOperationWarning> Warnings { get; }

    internal TerrainPublicationResult(IEnumerable<TerrainOperationWarning> warnings)
    {
        Warnings = warnings.ToList().AsReadOnly();
    }
}

internal sealed record PlannedDocumentAssetState(
    MapDocumentViewModel Document,
    MapDocumentAssetState AssetState,
    MapDocumentTerrainState TerrainState);
