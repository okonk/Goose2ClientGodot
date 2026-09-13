using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace MapEditor.Core;

public sealed record TerrainPatternCandidateGroup(
    TerrainPattern Pattern,
    IReadOnlyList<TerrainGraphicDefinition> Variants);

public sealed class TerrainCatalogIndex
{
    private static readonly IReadOnlyList<TerrainPatternCandidateGroup> EmptyGroups = Array.Empty<TerrainPatternCandidateGroup>();
    private static readonly IReadOnlyList<TerrainGraphicDefinition> EmptyGraphics = Array.Empty<TerrainGraphicDefinition>();

    private readonly Dictionary<Guid, TerrainDefinition> _terrainsById;
    private readonly Dictionary<TerrainGraphicReference, TerrainGraphicDefinition> _graphicsByReference;
    private readonly Dictionary<Guid, ReadOnlyCollection<TerrainPatternCandidateGroup>> _candidatesByCenter;
    private readonly Dictionary<Guid, ReadOnlyCollection<TerrainGraphicDefinition>> _representativesByCenter;
    private readonly Dictionary<Guid, TerrainColor> _displayColorsById;

    internal TerrainCatalogIndex(
        IReadOnlyDictionary<Guid, TerrainDefinition> terrainsById,
        IReadOnlyDictionary<TerrainGraphicReference, TerrainGraphicDefinition> graphicsByReference,
        IReadOnlyDictionary<Guid, IReadOnlyList<TerrainPatternCandidateGroup>> candidatesByCenter,
        IReadOnlyDictionary<Guid, IReadOnlyList<TerrainGraphicDefinition>> representativesByCenter,
        IReadOnlyDictionary<Guid, TerrainColor> displayColorsById)
    {
        _terrainsById = terrainsById.ToDictionary(entry => entry.Key, entry => entry.Value);
        _graphicsByReference = graphicsByReference.ToDictionary(entry => entry.Key, entry => entry.Value);
        _candidatesByCenter = candidatesByCenter.ToDictionary(
            entry => entry.Key,
            entry => new ReadOnlyCollection<TerrainPatternCandidateGroup>(
                entry.Value
                    .Select(group => new TerrainPatternCandidateGroup(
                        group.Pattern,
                        new ReadOnlyCollection<TerrainGraphicDefinition>(group.Variants.ToList())))
                    .ToList()));
        _representativesByCenter = representativesByCenter.ToDictionary(
            entry => entry.Key,
            entry => new ReadOnlyCollection<TerrainGraphicDefinition>(entry.Value.ToList()));
        _displayColorsById = displayColorsById.ToDictionary(entry => entry.Key, entry => entry.Value);
    }

    public bool TryGetTerrain(Guid id, out TerrainDefinition terrain)
    {
        // The BCL annotates Dictionary.TryGetValue's out parameter as nullable.
        var found = _terrainsById.TryGetValue(id, out TerrainDefinition? value);
        terrain = value!;
        return found;
    }

    public bool TryGetGraphic(TerrainGraphicReference reference, out TerrainGraphicDefinition graphic)
    {
        // The BCL annotates Dictionary.TryGetValue's out parameter as nullable.
        var found = _graphicsByReference.TryGetValue(reference, out TerrainGraphicDefinition? value);
        graphic = value!;
        return found;
    }

    public IReadOnlyList<TerrainPatternCandidateGroup> GetCandidates(Guid centerId)
        => _candidatesByCenter.TryGetValue(centerId, out var candidates) ? candidates : EmptyGroups;

    public IReadOnlyList<TerrainGraphicDefinition> GetRepresentatives(Guid centerId)
        => _representativesByCenter.TryGetValue(centerId, out var representatives) ? representatives : EmptyGraphics;

    public TerrainColor GetDisplayColor(Guid terrainId)
    {
        if (!_displayColorsById.TryGetValue(terrainId, out var color))
        {
            throw new KeyNotFoundException($"Unknown terrain {terrainId}.");
        }

        return color;
    }
}
