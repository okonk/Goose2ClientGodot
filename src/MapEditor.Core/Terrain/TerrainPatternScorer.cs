using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MapEditor.Core;

public readonly record struct TerrainPatternSelection(
    TerrainPattern Pattern,
    TerrainGraphicDefinition Variant,
    int Score,
    int TiedPatternCount,
    int VariantCount);

public static class TerrainPatternScorer
{
    private static readonly (TerrainPeer Peer, int Weight)[] PeerWeights =
    [
        (TerrainPeer.North, 2),
        (TerrainPeer.East, 2),
        (TerrainPeer.South, 2),
        (TerrainPeer.West, 2),
        (TerrainPeer.NorthEast, 1),
        (TerrainPeer.SouthEast, 1),
        (TerrainPeer.SouthWest, 1),
        (TerrainPeer.NorthWest, 1)
    ];

    private static readonly TerrainPeer[] SortOrder =
    [
        TerrainPeer.Center,
        TerrainPeer.North,
        TerrainPeer.East,
        TerrainPeer.South,
        TerrainPeer.West,
        TerrainPeer.NorthEast,
        TerrainPeer.SouthEast,
        TerrainPeer.SouthWest,
        TerrainPeer.NorthWest
    ];

    public static int ScorePeer(Guid? desired, Guid? candidate, int weight)
    {
        if (desired is null)
        {
            return candidate is null ? weight : -weight;
        }

        if (candidate is null)
        {
            return 0;
        }

        return desired == candidate ? weight : -weight;
    }

    public static int Score(TerrainPattern desired, TerrainPattern candidate)
    {
        var score = 0;
        foreach (var (peer, weight) in PeerWeights)
        {
            score += ScorePeer(desired.Get(peer), candidate.Get(peer), weight);
        }

        return score;
    }

    public static TerrainPatternSelection Select(TerrainPattern desired, int x, int y, IReadOnlyList<TerrainPatternCandidateGroup> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (desired.Center is null)
        {
            throw new ArgumentException("Desired pattern must have a center terrain.", nameof(desired));
        }

        if (candidates.Count == 0)
        {
            throw new ArgumentException("No candidate patterns.", nameof(candidates));
        }

        var centerId = desired.Center.Value;
        var merged = new Dictionary<TerrainPattern, List<TerrainGraphicDefinition>>();
        foreach (var group in candidates)
        {
            if (!merged.TryGetValue(group.Pattern, out var mergedVariants))
            {
                mergedVariants = [];
                merged[group.Pattern] = mergedVariants;
            }

            mergedVariants.AddRange(group.Variants);
        }

        var best = int.MinValue;
        var tied = new List<(TerrainPattern Pattern, IReadOnlyList<TerrainGraphicDefinition> Variants)>();
        foreach (var (pattern, mergedVariants) in merged)
        {
            var score = Score(desired, pattern);
            if (score > best)
            {
                best = score;
                tied.Clear();
            }

            if (score == best)
            {
                tied.Add((pattern, mergedVariants));
            }
        }

        tied.Sort(static (a, b) => ComparePatterns(a.Pattern, b.Pattern));
        var patternHash = TerrainStableHash.HashPattern(centerId, x, y, desired);
        var selected = tied[(int)(patternHash % (ulong)tied.Count)];

        var variants = selected.Variants.Distinct().ToList();
        if (variants.Count == 0)
        {
            throw new InvalidOperationException("Selected pattern has no variants.");
        }

        variants.Sort(static (a, b) =>
            a.Reference.Sheet != b.Reference.Sheet
                ? a.Reference.Sheet.CompareTo(b.Reference.Sheet)
                : a.Reference.Graphic.CompareTo(b.Reference.Graphic));
        var variantHash = TerrainStableHash.HashVariant(centerId, x, y, desired, selected.Pattern);
        var variant = variants[(int)(variantHash % (ulong)variants.Count)];

        return new TerrainPatternSelection(selected.Pattern, variant, best, tied.Count, variants.Count);
    }

    private static int ComparePatterns(TerrainPattern a, TerrainPattern b)
    {
        foreach (var peer in SortOrder)
        {
            var comparison = ComparePeer(a.Get(peer), b.Get(peer));
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }

    private static int ComparePeer(Guid? a, Guid? b)
    {
        if (a is null)
        {
            return b is null ? 0 : -1;
        }

        if (b is null)
        {
            return 1;
        }

        return string.CompareOrdinal(
            a.Value.ToString("N", CultureInfo.InvariantCulture),
            b.Value.ToString("N", CultureInfo.InvariantCulture));
    }
}
