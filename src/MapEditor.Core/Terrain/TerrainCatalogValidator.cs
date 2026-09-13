using System;
using System.Collections.Generic;
using System.Linq;

namespace MapEditor.Core;

public static class TerrainCatalogValidator
{
    private static readonly TerrainPeer[] PeerSlots =
    {
        TerrainPeer.North, TerrainPeer.East, TerrainPeer.South, TerrainPeer.West,
        TerrainPeer.NorthEast, TerrainPeer.SouthEast, TerrainPeer.SouthWest, TerrainPeer.NorthWest
    };

    private static readonly TerrainPeer[] Clockwise =
    {
        TerrainPeer.North, TerrainPeer.NorthEast, TerrainPeer.East, TerrainPeer.SouthEast,
        TerrainPeer.South, TerrainPeer.SouthWest, TerrainPeer.West, TerrainPeer.NorthWest
    };

    private static readonly Comparer<TerrainGraphicReference?> ReferenceComparer =
        Comparer<TerrainGraphicReference?>.Create((x, y) =>
        {
            if (x is null || y is null)
            {
                return x is null ? (y is null ? 0 : -1) : 1;
            }

            var sheet = x.Value.Sheet.CompareTo(y.Value.Sheet);
            return sheet != 0 ? sheet : x.Value.Graphic.CompareTo(y.Value.Graphic);
        });

    private static readonly Comparer<TerrainPattern> PatternComparer = Comparer<TerrainPattern>.Create((a, b) =>
    {
        foreach (var peer in PeerSlots)
        {
            var result = Comparer<Guid?>.Default.Compare(a.Get(peer), b.Get(peer));
            if (result != 0)
            {
                return result;
            }
        }

        return 0;
    });

    public static TerrainCatalogValidationResult Validate(TerrainCatalog catalog)
    {
        var issues = new List<TerrainValidationIssue>();
        var terrainIds = new HashSet<Guid>();
        var terrainsById = new Dictionary<Guid, TerrainDefinition>();

        foreach (var terrain in catalog.Terrains)
        {
            if (terrain.Id == Guid.Empty)
            {
                issues.Add(Error(TerrainValidationCode.EmptyTerrainId, $"Terrain '{terrain.Name}' has an empty ID."));
                continue;
            }

            if (terrainIds.Add(terrain.Id))
            {
                terrainsById[terrain.Id] = terrain;
            }
        }

        foreach (var group in catalog.Terrains
            .Where(terrain => terrain.Id != Guid.Empty)
            .GroupBy(terrain => terrain.Id))
        {
            if (group.Count() > 1)
            {
                foreach (var terrain in group)
                {
                    issues.Add(Error(
                        TerrainValidationCode.DuplicateTerrainId,
                        $"Terrain ID {terrain.Id} is used by {group.Count()} terrains.",
                        terrain.Id));
                }
            }
        }

        foreach (var group in catalog.Terrains
            .GroupBy(terrain => terrain.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (group.Count() > 1)
            {
                foreach (var terrain in group)
                {
                    issues.Add(Error(
                        TerrainValidationCode.DuplicateTerrainName,
                        $"Terrain name '{group.Key}' is used by {group.Count()} terrains.",
                        terrain.Id));
                }
            }
        }

        var referenceCounts = catalog.Graphics
            .GroupBy(graphic => graphic.Reference)
            .ToDictionary(group => group.Key, group => group.Count());

        foreach (var (reference, count) in referenceCounts)
        {
            if (count > 1)
            {
                issues.Add(Error(
                    TerrainValidationCode.DuplicateGraphicReference,
                    $"Graphic reference ({reference.Sheet}, {reference.Graphic}) is used by {count} graphics.",
                    reference: reference));
            }
        }

        foreach (var graphic in catalog.Graphics)
        {
            var reference = graphic.Reference;
            if (reference.Graphic == 0)
            {
                issues.Add(Error(
                    TerrainValidationCode.InvalidGraphicNumber,
                    $"Graphic ({reference.Sheet}, {reference.Graphic}) has an invalid graphic number.",
                    reference: reference));
            }

            if (reference.Sheet < short.MinValue || reference.Sheet > short.MaxValue)
            {
                issues.Add(Error(
                    TerrainValidationCode.InvalidSheetNumber,
                    $"Graphic ({reference.Sheet}, {reference.Graphic}) has a sheet outside the short range.",
                    reference: reference));
            }

            var center = graphic.Pattern.Center;
            if (center is null)
            {
                issues.Add(new TerrainValidationIssue(
                    TerrainValidationSeverity.Error,
                    TerrainValidationCode.CenterlessGraphic,
                    $"Graphic ({reference.Sheet}, {reference.Graphic}) has no center terrain.",
                    GraphicReference: reference,
                    Peer: TerrainPeer.Center));
            }
            else if (!terrainIds.Contains(center.Value))
            {
                issues.Add(new TerrainValidationIssue(
                    TerrainValidationSeverity.Error,
                    TerrainValidationCode.UnknownPeer,
                    $"Graphic ({reference.Sheet}, {reference.Graphic}) references unknown terrain {center.Value} at center.",
                    TerrainId: center.Value,
                    GraphicReference: reference,
                    Peer: TerrainPeer.Center));
            }

            foreach (var peer in PeerSlots)
            {
                var value = graphic.Pattern.Get(peer);
                if (value is not null && !terrainIds.Contains(value.Value))
                {
                    issues.Add(new TerrainValidationIssue(
                        TerrainValidationSeverity.Error,
                        TerrainValidationCode.UnknownPeer,
                        $"Graphic ({reference.Sheet}, {reference.Graphic}) references unknown terrain {value.Value} at {peer}.",
                        TerrainId: value.Value,
                        GraphicReference: reference,
                        Peer: peer));
                }
            }
        }

        var centeredCounts = catalog.Graphics
            .Where(graphic => graphic.Pattern.Center is not null)
            .GroupBy(graphic => graphic.Pattern.Center!.Value)
            .ToDictionary(group => group.Key, group => group.Count());

        foreach (var terrain in catalog.Terrains)
        {
            if (terrain.Id == Guid.Empty)
            {
                continue;
            }

            if (!centeredCounts.TryGetValue(terrain.Id, out var centeredCount) || centeredCount == 0)
            {
                issues.Add(Error(
                    TerrainValidationCode.MissingCenteredGraphic,
                    $"Terrain '{terrain.Name}' ({terrain.Id}) has no centered graphic.",
                    terrain.Id));
            }
        }

        AddCoverageWarnings(issues, catalog, terrainsById);

        var hasErrors = issues.Any(issue => issue.Severity == TerrainValidationSeverity.Error);
        return new TerrainCatalogValidationResult(
            OrderIssues(issues),
            hasErrors ? null : BuildIndex(catalog));
    }

    private static TerrainValidationIssue Error(
        TerrainValidationCode code,
        string message,
        Guid? terrainId = null,
        TerrainGraphicReference? reference = null)
        => new(TerrainValidationSeverity.Error, code, message, terrainId, reference);

    // Bounded coverage matrix: the all-center interior pattern plus, per related other
    // terrain or None, isolated + straight/convex/concave each rotated four ways.
    private static void AddCoverageWarnings(
        List<TerrainValidationIssue> issues,
        TerrainCatalog catalog,
        IReadOnlyDictionary<Guid, TerrainDefinition> terrainsById)
    {
        var presentPatterns = new HashSet<TerrainPattern>(catalog.Graphics.Select(graphic => graphic.Pattern));
        var edges = new HashSet<(Guid, Guid)>();
        foreach (var graphic in catalog.Graphics)
        {
            var center = graphic.Pattern.Center;
            if (center is null)
            {
                continue;
            }

            foreach (var peer in PeerSlots)
            {
                var value = graphic.Pattern.Get(peer);
                if (value is not null && value != center)
                {
                    edges.Add((center.Value, value.Value));
                    edges.Add((value.Value, center.Value));
                }
            }
        }

        foreach (var terrain in catalog.Terrains)
        {
            var center = terrain.Id;
            if (!presentPatterns.Contains(BuildPattern(center, new Dictionary<TerrainPeer, Guid?>())))
            {
                issues.Add(CoverageWarning(terrain, "interior", "itself"));
            }

            foreach (var other in RelatedTerrains(center, terrainsById, edges))
            {
                foreach (var (label, pattern) in CoveragePairPatterns(center, other))
                {
                    if (!presentPatterns.Contains(pattern))
                    {
                        issues.Add(CoverageWarning(terrain, label, other is null ? "none" : NameOf(other.Value, terrainsById)));
                    }
                }
            }
        }
    }

    private static IEnumerable<Guid?> RelatedTerrains(
        Guid center,
        IReadOnlyDictionary<Guid, TerrainDefinition> terrainsById,
        HashSet<(Guid, Guid)> edges)
    {
        yield return null;
        foreach (var other in terrainsById.Keys
            .Where(id => id != center && edges.Contains((center, id)))
            .OrderBy(id => id))
        {
            yield return other;
        }
    }

    private static IEnumerable<(string Label, TerrainPattern Pattern)> CoveragePairPatterns(Guid center, Guid? other)
    {
        var all = new Dictionary<TerrainPeer, Guid?>();
        foreach (var peer in PeerSlots)
        {
            all[peer] = other;
        }

        yield return ("isolated", BuildPattern(center, all));

        foreach (var (label, pattern) in RotatedPatterns(center, "straight",
            (TerrainPeer.North, other), (TerrainPeer.NorthEast, other), (TerrainPeer.NorthWest, other)))
        {
            yield return (label, pattern);
        }

        foreach (var (label, pattern) in RotatedPatterns(center, "convex",
            (TerrainPeer.North, other), (TerrainPeer.NorthEast, other), (TerrainPeer.East, other)))
        {
            yield return (label, pattern);
        }

        foreach (var (label, pattern) in RotatedPatterns(center, "concave",
            (TerrainPeer.NorthEast, other)))
        {
            yield return (label, pattern);
        }
    }

    private static IEnumerable<(string Label, TerrainPattern Pattern)> RotatedPatterns(
        Guid center,
        string kind,
        params (TerrainPeer Peer, Guid? Value)[] marked)
    {
        var directions = new[] { "north", "east", "south", "west" };
        for (var step = 0; step < 4; step++)
        {
            var values = new Dictionary<TerrainPeer, Guid?>();
            foreach (var (peer, value) in marked)
            {
                values[RotateClockwise(peer, step * 2)] = value;
            }

            yield return ($"{kind} {directions[step]}", BuildPattern(center, values));
        }
    }

    private static TerrainPeer RotateClockwise(TerrainPeer peer, int steps)
        => Clockwise[(Array.IndexOf(Clockwise, peer) + steps) % Clockwise.Length];

    private static TerrainPattern BuildPattern(Guid center, IDictionary<TerrainPeer, Guid?> marked)
    {
        var values = new Guid?[PeerSlots.Length];
        for (var i = 0; i < PeerSlots.Length; i++)
        {
            values[i] = marked.TryGetValue(PeerSlots[i], out var value) ? value : center;
        }

        return new TerrainPattern(
            Center: center,
            North: values[0],
            East: values[1],
            South: values[2],
            West: values[3],
            NorthEast: values[4],
            SouthEast: values[5],
            SouthWest: values[6],
            NorthWest: values[7]);
    }

    private static TerrainValidationIssue CoverageWarning(TerrainDefinition terrain, string patternLabel, string otherLabel)
        => new(
            TerrainValidationSeverity.Warning,
            TerrainValidationCode.MissingCoveragePattern,
            $"Terrain '{terrain.Name}' is missing coverage pattern '{patternLabel}' against '{otherLabel}'.",
            TerrainId: terrain.Id);

    private static string NameOf(Guid id, IReadOnlyDictionary<Guid, TerrainDefinition> terrainsById)
        => terrainsById.TryGetValue(id, out var terrain) ? terrain.Name : id.ToString();

    private static IReadOnlyList<TerrainValidationIssue> OrderIssues(List<TerrainValidationIssue> issues)
        => issues
            .OrderBy(issue => issue.Severity)
            .ThenBy(issue => issue.Code)
            .ThenBy(issue => issue.TerrainId, Comparer<Guid?>.Default)
            .ThenBy(issue => issue.GraphicReference, ReferenceComparer)
            .ThenBy(issue => issue.Peer, Comparer<TerrainPeer?>.Default)
            .ToList();

    private static TerrainCatalogIndex BuildIndex(TerrainCatalog catalog)
    {
        var terrainsById = catalog.Terrains.ToDictionary(terrain => terrain.Id);
        var graphicsByReference = catalog.Graphics.ToDictionary(graphic => graphic.Reference);
        var displayColorsById = terrainsById.ToDictionary(terrain => terrain.Key, terrain => terrain.Value.DisplayColor);
        var candidatesByCenter = new Dictionary<Guid, IReadOnlyList<TerrainPatternCandidateGroup>>();
        var representativesByCenter = new Dictionary<Guid, IReadOnlyList<TerrainGraphicDefinition>>();

        foreach (var terrain in catalog.Terrains)
        {
            var center = terrain.Id;
            var graphics = catalog.Graphics.Where(graphic => graphic.Pattern.Center == center).ToList();
            candidatesByCenter[center] = graphics
                .GroupBy(graphic => graphic.Pattern)
                .Select(group => new TerrainPatternCandidateGroup(
                    group.Key,
                    group
                        .OrderBy(graphic => graphic.Reference.Sheet)
                        .ThenBy(graphic => graphic.Reference.Graphic)
                        .ToList()))
                .OrderBy(group => group.Pattern, PatternComparer)
                .ToList();
            representativesByCenter[center] = graphics
                .OrderByDescending(graphic => SameCenterPeerCount(graphic))
                .ThenBy(graphic => graphic.Reference.Sheet)
                .ThenBy(graphic => graphic.Reference.Graphic)
                .ToList();
        }

        return new TerrainCatalogIndex(
            terrainsById,
            graphicsByReference,
            candidatesByCenter,
            representativesByCenter,
            displayColorsById);
    }

    private static int SameCenterPeerCount(TerrainGraphicDefinition graphic)
    {
        var center = graphic.Pattern.Center!.Value;
        var count = 0;
        foreach (var peer in PeerSlots)
        {
            if (graphic.Pattern.Get(peer) == center)
            {
                count++;
            }
        }

        return count;
    }
}
