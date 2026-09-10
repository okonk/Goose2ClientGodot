using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace MapEditor.Core.Terrain;

internal sealed class TerrainRuntimeSet
{
    private readonly int[] _maskSlots;
    private readonly TerrainGraphicReference[][] _variants;

    internal string Id { get; }
    internal TerrainTopology Topology { get; }

    internal TerrainRuntimeSet(string id, TerrainTopology topology, int[] maskSlots, TerrainGraphicReference[][] variants)
    {
        Id = id;
        Topology = topology;
        _maskSlots = maskSlots;
        _variants = variants;
    }

    internal bool TryGetVariants(int normalizedMask, out TerrainGraphicReference[] variants)
    {
        var index = _maskSlots[normalizedMask];
        if (index < 0)
        {
            variants = Array.Empty<TerrainGraphicReference>();
            return false;
        }

        variants = _variants[index];
        return true;
    }
}

internal readonly record struct TerrainVariantLookup(
    bool Succeeded,
    TerrainGraphicReference Variant,
    int NormalizedMask,
    TerrainEditFailure? Failure);

public sealed class TerrainMapResolver
{
    private static readonly TerrainGenerationSettings ProjectionSettings =
        new(2, 1, 1, 1, 1, 0.1, 0.5, 0.5, 0.1, 0.5, 0.9, 0.9);

    private const string ProjectionFingerprint =
        "sha256:0000000000000000000000000000000000000000000000000000000000000000";

    private readonly Dictionary<string, TerrainRuntimeSet> _byId;
    private readonly Dictionary<TerrainGraphicReference, TerrainRuntimeSet> _owners;

    public TerrainMapResolver(TerrainCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        TerrainValidationIssue? statusIssue = null;
        foreach (var issue in TerrainCatalogValidator.Validate(catalog))
        {
            if (issue.Code == "terrain-status-invalid")
            {
                statusIssue = issue;
                break;
            }
        }

        if (statusIssue is { } first)
        {
            throw new ArgumentException(
                $"Enabled terrain catalog is invalid: {first.Code}: {first.Message}",
                nameof(catalog));
        }

        var enabled = catalog.Sets
            .Where(set => set.Status == TerrainReviewStatus.Enabled)
            .ToList();

        // Neutral metadata: catalog-level issues are not runtime lookup inputs.
        var projection = new TerrainCatalog(
            TerrainCatalogJson.CurrentSchemaVersion,
            "resolver",
            ProjectionFingerprint,
            ProjectionSettings,
            enabled,
            Array.Empty<TerrainDiagnostic>());
        TerrainValidationIssue? firstIssue = null;
        foreach (var issue in TerrainCatalogValidator.Validate(projection))
        {
            if (issue.Code != "enabled-mask-missing" && issue.Code != "enabled-mask-empty")
            {
                firstIssue = issue;
                break;
            }
        }

        if (firstIssue is { } rejected)
        {
            throw new ArgumentException(
                $"Enabled terrain catalog is invalid: {rejected.Code}: {rejected.Message}",
                nameof(catalog));
        }

        var byId = new Dictionary<string, TerrainRuntimeSet>(StringComparer.Ordinal);
        var owners = new Dictionary<TerrainGraphicReference, TerrainRuntimeSet>();
        foreach (var definition in enabled)
        {
            BuildSet(definition, byId, owners);
        }

        _byId = byId;
        _owners = owners;
    }

    internal bool TryGetEnabledTerrain(string terrainId, out TerrainRuntimeSet? terrain)
    {
        if (terrainId is not null && _byId.TryGetValue(terrainId, out terrain))
        {
            return true;
        }

        terrain = null;
        return false;
    }

    internal bool TryGetOwner(TerrainGraphicReference reference, out TerrainRuntimeSet? terrain)
    {
        if (_owners.TryGetValue(reference, out terrain))
        {
            return true;
        }

        terrain = null;
        return false;
    }

    internal TerrainVariantLookup ResolveVariant(TerrainRuntimeSet terrain, int rawMask, int x, int y)
    {
        var normalized = TerrainMasks.Normalize(rawMask, terrain.Topology);
        if (!terrain.TryGetVariants(normalized, out var variants) || variants.Length == 0)
        {
            return new TerrainVariantLookup(
                false,
                default,
                normalized,
                new TerrainEditFailure(terrain.Id, normalized));
        }

        var index = (int)(ComputeVariantIndex(terrain.Id, x, y, normalized) % (ulong)variants.Length);
        return new TerrainVariantLookup(true, variants[index], normalized, null);
    }

    private static void BuildSet(
        TerrainSetDefinition definition,
        Dictionary<string, TerrainRuntimeSet> byId,
        Dictionary<TerrainGraphicReference, TerrainRuntimeSet> owners)
    {
        var slotCount = definition.Topology == TerrainTopology.FourWay ? 16 : 256;
        var slots = new int[slotCount];
        Array.Fill(slots, -1);
        var variantLists = new List<TerrainGraphicReference[]>();
        foreach (var mask in definition.Masks)
        {
            var normalized = TerrainMasks.Normalize(mask.Mask, definition.Topology);
            if (slots[normalized] < 0)
            {
                slots[normalized] = variantLists.Count;
                variantLists.Add(mask.Variants.ToArray());
            }
        }

        var set = new TerrainRuntimeSet(definition.Id, definition.Topology, slots, variantLists.ToArray());
        byId[definition.Id] = set;
        foreach (var member in definition.Members)
        {
            owners[member.Reference] = set;
        }
    }

    private static ulong ComputeVariantIndex(string terrainId, int x, int y, int normalizedMask)
    {
        var text = terrainId
            + "\n" + x.ToString(CultureInfo.InvariantCulture)
            + "\n" + y.ToString(CultureInfo.InvariantCulture)
            + "\n" + normalizedMask.ToString(CultureInfo.InvariantCulture);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        var value = 0UL;
        for (var i = 0; i < 8; i++)
        {
            value = (value << 8) | digest[i];
        }

        return value;
    }
}
