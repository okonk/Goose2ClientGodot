using MapEditor.Core.Terrain;
using MapEditor.Rendering;

namespace MapEditor.App.Terrain;

internal sealed record TerrainSetDraft
{
    internal TerrainDraftKey Key { get; }
    internal string Id { get; }
    internal string DisplayName { get; }
    internal TerrainReviewStatus Status { get; }
    internal TerrainTopology Topology { get; }
    internal TerrainSetMetrics Metrics { get; }
    internal IReadOnlyList<TerrainMaskDraft> Masks { get; }
    internal IReadOnlyList<TerrainMaskDraft> OrphanMasks { get; }
    internal IReadOnlyList<TerrainMemberDefinition> Members { get; }
    internal IReadOnlyList<TerrainDiagnostic> Diagnostics { get; }
    internal IReadOnlyList<TerrainValidationIssue> Issues { get; }
    internal bool CanEnable { get; }
    internal TerrainGraphicReference? Representative => Masks.SelectMany(mask => mask.Variants).Select(variant => (TerrainGraphicReference?)variant.Reference).FirstOrDefault();

    internal TerrainSetDraft(
        TerrainDraftKey key,
        TerrainSetDefinition definition,
        IEnumerable<TerrainMaskDefinition> orphans,
        SpriteManifest manifest,
        IEnumerable<TerrainValidationIssue> issues,
        bool canEnable)
    {
        Key = key;
        Id = definition.Id;
        DisplayName = definition.DisplayName;
        Status = definition.Status;
        Topology = definition.Topology;
        Metrics = definition.Metrics;
        var masks = new List<TerrainMaskDefinition>();
        if (definition.Topology is TerrainTopology.FourWay or TerrainTopology.EightWay)
        {
            foreach (int required in TerrainMasks.Required(definition.Topology))
                masks.Add(definition.Masks.FirstOrDefault(mask => mask.Mask == required) ?? new TerrainMaskDefinition(required, Array.Empty<TerrainGraphicReference>()));
            masks.AddRange(definition.Masks.Where((mask, index) =>
                !TerrainMasks.IsReachable(mask.Mask, definition.Topology) || definition.Masks.Take(index).Any(previous => previous.Mask == mask.Mask)));
        }
        else
        {
            masks.AddRange(definition.Masks);
        }
        Masks = Array.AsReadOnly(masks.Select(mask => new TerrainMaskDraft(mask.Mask, mask.Variants, manifest)).ToArray());
        OrphanMasks = Array.AsReadOnly(orphans.Select(mask => new TerrainMaskDraft(mask.Mask, mask.Variants, manifest)).ToArray());
        Members = Array.AsReadOnly(definition.Members.Select(member => new TerrainMemberDefinition(member.Reference, member.Provenance)).ToArray());
        Diagnostics = Array.AsReadOnly(definition.Diagnostics.Select(diagnostic => new TerrainDiagnostic(diagnostic.Code, diagnostic.Message, diagnostic.Mask, diagnostic.Reference)).ToArray());
        Issues = Array.AsReadOnly(issues.ToArray());
        CanEnable = canEnable;
    }
}
