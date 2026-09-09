using MapEditor.Core.Terrain;

namespace Goose2.AssetConverter.Terrain;

public static class TerrainCandidateMiner
{
    private static readonly (int DX, int DY)[] Cardinals =
    [
        (0, -1),
        (1, 0),
        (0, 1),
        (-1, 0),
    ];

    public static readonly TerrainGenerationSettings DefaultSettings = new(
        HoldoutModulo: 5,
        MinimumMapSupport: 5,
        MinimumRegionSupport: 8,
        MinimumObservationSupport: 64,
        MinimumDiagonalSupport: 32,
        MinimumEightWayAccuracyGain: 0.05,
        MapMemberCompatibility: 0.70,
        ImageOnlyCompatibility: 0.94,
        MinimumClassificationMargin: 0.05,
        MaximumAmbiguity: 0.10,
        EnabledConfidence: 0.90,
        PendingConfidence: 0.45);

    public static TerrainCandidateMinerResult Mine(
        TerrainCorpus corpus,
        ITerrainFeatureSource features,
        TerrainGenerationSettings? settings = null,
        ITerrainMapDataReader? mapReader = null)
    {
        settings ??= DefaultSettings;
        var training = new List<TerrainMapDescriptor>();
        foreach (var map in corpus.Maps)
        {
            if (!TerrainHoldout.IsHeldOut(map.Identity, settings.HoldoutModulo))
            {
                training.Add(map);
            }
        }

        // Contract-locked sum order: map identity, then region minimum, then placement row-major.
        training.Sort((a, b) => string.CompareOrdinal(a.Identity, b.Identity));

        var eligible = new HashSet<TerrainGraphicReference>();
        var reader = mapReader is null
            ? new TerrainMapBatchReader(corpus.Root)
            : new TerrainMapBatchReader(corpus.Root, mapReader);
        var occurrences = new Dictionary<(TerrainGraphicReference, TerrainGraphicReference), Dictionary<string, HashSet<(int, int, int, int)>>>();

        foreach (var descriptor in training)
        {
            reader.VisitMap(descriptor, map =>
            {
                var references = new (int Sheet, int Graphic)[map.Width * map.Height];
                for (var y = 0; y < map.Height; y++)
                {
                    for (var x = 0; x < map.Width; x++)
                    {
                        var (sheet, graphic) = map.GetLayer(x, y);
                        if (graphic == 0)
                        {
                            continue;
                        }

                        var reference = new TerrainGraphicReference(sheet, graphic);
                        references[y * map.Width + x] = (sheet, graphic);
                        if (IsEligibleFrame(corpus, reference))
                        {
                            eligible.Add(reference);
                        }
                    }
                }

                for (var y = 0; y < map.Height; y++)
                {
                    for (var x = 0; x < map.Width; x++)
                    {
                        var (sheet, graphic) = references[y * map.Width + x];
                        if (graphic == 0)
                        {
                            continue;
                        }

                        var a = new TerrainGraphicReference(sheet, graphic);
                        if (!eligible.Contains(a))
                        {
                            continue;
                        }

                        for (var i = 0; i < 2; i++)
                        {
                            var (nx, ny) = (x + Cardinals[i].DX, y + Cardinals[i].DY);
                            if (nx < 0 || ny < 0 || nx >= map.Width || ny >= map.Height)
                            {
                                continue;
                            }

                            var (ns, ng) = references[ny * map.Width + nx];
                            if (ng == 0)
                            {
                                continue;
                            }

                            var b = new TerrainGraphicReference(ns, ng);
                            if (!eligible.Contains(b))
                            {
                                continue;
                            }

                            var pair = Order(a, b);
                            var byMap = occurrences.TryGetValue(pair, out var existing)
                                ? existing
                                : occurrences[pair] = new Dictionary<string, HashSet<(int, int, int, int)>>();
                            var set = byMap.TryGetValue(descriptor.Identity, out var existingSet)
                                ? existingSet
                                : byMap[descriptor.Identity] = new HashSet<(int, int, int, int)>();
                            set.Add(CompareCells(x, y, nx, ny) <= 0
                                ? (x, y, nx, ny)
                                : (nx, ny, x, y));
                        }
                    }
                }
            });
        }

        var vertices = eligible.OrderBy(reference => reference.Sheet).ThenBy(reference => reference.Graphic).ToList();
        var pairCandidates = new HashSet<(TerrainGraphicReference, TerrainGraphicReference)>();
        foreach (var vertex in vertices)
        {
            foreach (var other in features.QueryCandidates(vertex))
            {
                if (other == vertex)
                {
                    continue;
                }

                pairCandidates.Add(Order(vertex, other));
            }
        }

        var edges = new Dictionary<TerrainGraphicReference, List<TerrainGraphicReference>>();
        foreach (var pair in pairCandidates
            .OrderBy(pair => pair.Item1.Sheet)
            .ThenBy(pair => pair.Item1.Graphic)
            .ThenBy(pair => pair.Item2.Sheet)
            .ThenBy(pair => pair.Item2.Graphic))
        {
            if (pair.Item1.Sheet != pair.Item2.Sheet)
            {
                continue;
            }

            if (features.Similarity(pair.Item1, pair.Item2) < settings.MapMemberCompatibility)
            {
                continue;
            }

            if (!HasStrictAdjacencyEvidence(occurrences.TryGetValue(pair, out var byMap) ? byMap : null))
            {
                continue;
            }

            var from = edges.TryGetValue(pair.Item1, out var existingFrom)
                ? existingFrom
                : edges[pair.Item1] = new List<TerrainGraphicReference>();
            var to = edges.TryGetValue(pair.Item2, out var existingTo)
                ? existingTo
                : edges[pair.Item2] = new List<TerrainGraphicReference>();
            from.Add(pair.Item2);
            to.Add(pair.Item1);
        }

        var assigned = new HashSet<TerrainGraphicReference>();
        var familyEntries = new List<FamilyFold>();
        foreach (var vertex in vertices)
        {
            if (assigned.Contains(vertex))
            {
                continue;
            }

            var component = new List<TerrainGraphicReference> { vertex };
            var stack = new Stack<TerrainGraphicReference>();
            stack.Push(vertex);
            assigned.Add(vertex);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (edges.TryGetValue(current, out var neighbors))
                {
                    foreach (var neighbor in neighbors)
                    {
                        if (assigned.Add(neighbor))
                        {
                            component.Add(neighbor);
                            stack.Push(neighbor);
                        }
                    }
                }
            }

            component.Sort((a, b) => a.Sheet != b.Sheet ? a.Sheet.CompareTo(b.Sheet) : a.Graphic.CompareTo(b.Graphic));
            if (component.Count < 2)
            {
                continue;
            }

            var members = component.ToArray();
            familyEntries.Add(new FamilyFold(
                new TerrainCandidateFamily(
                    members,
                    mapSupport: 0,
                    regionSupport: 0,
                    observationSupport: 0,
                    diagonalSupport: 0,
                    new Dictionary<TerrainGraphicReference, double>(),
                    new Dictionary<TerrainGraphicReference, Dictionary<int, double>>(),
                    members[0],
                    Array.Empty<TerrainSideCentroid>(),
                    Array.Empty<TerrainCornerCentroid>())));
        }

        var rootDiagnostics = new List<TerrainDiagnostic>();
        foreach (var reference in vertices)
        {
            if (familyEntries.Any(entry => entry.Family.Members.Contains(reference)))
            {
                continue;
            }

            rootDiagnostics.Add(new TerrainDiagnostic(
                "insufficient-family-members",
                $"Map-observed reference ({reference.Sheet},{reference.Graphic}) did not join a family with at least two members.",
                reference: reference));
        }

        var regionSeenMaps = new Dictionary<TerrainCandidateFamily, HashSet<string>>();
        foreach (var descriptor in training)
        {
            reader.VisitMap(descriptor, map =>
            {
                foreach (var fold in familyEntries)
                {
                    var family = fold.Family;
                    var mining = TerrainRegionMiner.Mine(map, family.Members);
                    if (mining.Regions.Count == 0)
                    {
                        continue;
                    }

                    var seen = regionSeenMaps.TryGetValue(family, out var existing)
                        ? existing
                        : regionSeenMaps[family] = new HashSet<string>();
                    if (seen.Add(descriptor.Identity))
                    {
                        family.MapSupport++;
                    }

                    family.RegionSupport += mining.Regions.Count;
                    family.ObservationSupport += mining.Regions.Sum(region => region.Placements.Count);
                    family.DiagonalSupport += mining.DiagonalTrials;
                    if (mining.DiagonalTrials > 0)
                    {
                        family.DiagonalMapSupport++;
                    }

                    foreach (var region in mining.Regions)
                    {
                        foreach (var placement in region.Placements)
                        {
                            var weight = placement.Weight;
                            if (!double.IsFinite(weight))
                            {
                                throw new TerrainGenerationException(
                                    TerrainGenerationError.NumericOverflow,
                                    "Nonfinite placement weight while mining terrain candidates.");
                            }

                            family.ReferenceWeights.TryGetValue(placement.Reference, out var total);
                            family.ReferenceWeights[placement.Reference] = total + weight;
                            var masks = family.WeightedMasks.TryGetValue(placement.Reference, out var existingMasks)
                                ? existingMasks
                                : family.WeightedMasks[placement.Reference] = new Dictionary<int, double>();
                            masks.TryGetValue(placement.Mask, out var mass);
                            masks[placement.Mask] = mass + weight;
                            if (features.TryGetFeatures(placement.Reference, out var frameFeatures) && frameFeatures is not null)
                            {
                                fold.Accumulate(frameFeatures, placement.Mask, weight);
                            }
                        }
                    }
                }
            });
        }

        foreach (var fold in familyEntries)
        {
            var family = fold.Family;
            ComputeMedoid(family, features);
            var sideCentroids = new TerrainSideCentroid[4];
            for (var side = 0; side < 4; side++)
            {
                sideCentroids[side] = new TerrainSideCentroid(
                    Finish(fold.SideSums[side].Connected, fold.SideWeights[side].Connected),
                    Finish(fold.SideSums[side].Disconnected, fold.SideWeights[side].Disconnected),
                    fold.SideWeights[side].Connected,
                    fold.SideWeights[side].Disconnected);
            }

            var cornerCentroids = new TerrainCornerCentroid[4];
            for (var corner = 0; corner < 4; corner++)
            {
                cornerCentroids[corner] = new TerrainCornerCentroid(
                    Finish(fold.CornerSums[corner].Present, fold.CornerWeights[corner].Present),
                    Finish(fold.CornerSums[corner].Absent, fold.CornerWeights[corner].Absent),
                    fold.CornerWeights[corner].Present,
                    fold.CornerWeights[corner].Absent);
            }

            family.SideCentroids = sideCentroids;
            family.CornerCentroids = cornerCentroids;
        }

        var families = familyEntries.Select(fold => fold.Family).ToList();
        return new TerrainCandidateMinerResult(
            families,
            eligible.OrderBy(reference => reference.Sheet).ThenBy(reference => reference.Graphic).ToList().AsReadOnly(),
            DeduplicateAndOrder(rootDiagnostics));
    }

    private static bool IsEligibleFrame(TerrainCorpus corpus, TerrainGraphicReference reference)
    {
        if (reference.Graphic == 0)
        {
            return false;
        }

        return corpus.FrameIndex.TryGetRect(reference, out var rect)
               && rect.Width == TerrainCorpusLoader.RequiredTileSize
               && rect.Height == TerrainCorpusLoader.RequiredTileSize;
    }

    private static void ComputeMedoid(TerrainCandidateFamily family, ITerrainFeatureSource features)
    {
        double? best = null;
        var bestReference = family.Members[0];
        foreach (var reference in family.Members)
        {
            var score = 0.0;
            foreach (var other in family.Members)
            {
                if (other == reference)
                {
                    continue;
                }

                score += family.ReferenceWeights[other] * (1.0 - features.Similarity(reference, other));
            }

            if (!double.IsFinite(score))
            {
                throw new TerrainGenerationException(
                    TerrainGenerationError.NumericOverflow,
                    "Nonfinite medoid score while mining terrain candidates.");
            }

            if (best is null || score < best.Value || (score == best.Value && CompareReferences(reference, bestReference) < 0))
            {
                best = score;
                bestReference = reference;
            }
        }

        family.Medoid = bestReference;
    }

    private sealed class FamilyFold
    {
        public FamilyFold(TerrainCandidateFamily family)
        {
            Family = family;
        }

        public TerrainCandidateFamily Family { get; }

        public (double[] Connected, double[] Disconnected)[] SideSums { get; } = new (double[], double[])[4];
        public (double Connected, double Disconnected)[] SideWeights { get; } = new (double, double)[4];
        public (double[] Present, double[] Absent)[] CornerSums { get; } = new (double[], double[])[4];
        public (double Present, double Absent)[] CornerWeights { get; } = new (double, double)[4];

        public void Accumulate(TerrainImageFeatures features, int mask, double weight)
        {
            for (var side = 0; side < 4; side++)
            {
                var connected = (mask & (1 << side)) != 0;
                var sum = connected
                    ? SideSums[side].Connected ??= new double[160]
                    : SideSums[side].Disconnected ??= new double[160];
                var offset = side * 160;
                for (var i = 0; i < 160; i++)
                {
                    sum[i] += features.Edges[offset + i] * weight;
                }

                SideWeights[side] = connected
                    ? (SideWeights[side].Connected + weight, SideWeights[side].Disconnected)
                    : (SideWeights[side].Connected, SideWeights[side].Disconnected + weight);
            }

            for (var corner = 0; corner < 4; corner++)
            {
                var northBit = corner is 0 or 1 ? TerrainMasks.North : TerrainMasks.South;
                var eastBit = corner is 1 or 2 ? TerrainMasks.East : TerrainMasks.West;
                if ((mask & (northBit | eastBit)) != (northBit | eastBit))
                {
                    continue;
                }

                var present = (mask & cornerBit(corner)) != 0;
                var sum = present
                    ? CornerSums[corner].Present ??= new double[5]
                    : CornerSums[corner].Absent ??= new double[5];
                var offset = corner * 5;
                for (var i = 0; i < 5; i++)
                {
                    sum[i] += features.Corners[offset + i] * weight;
                }

                CornerWeights[corner] = present
                    ? (CornerWeights[corner].Present + weight, CornerWeights[corner].Absent)
                    : (CornerWeights[corner].Present, CornerWeights[corner].Absent + weight);
            }
        }
    }

    private static double[]? Finish(double[]? sum, double weight)
    {
        if (weight == 0.0 || sum is null)
        {
            return null;
        }

        var values = new double[sum.Length];
        for (var i = 0; i < sum.Length; i++)
        {
            values[i] = sum[i] / weight;
            if (!double.IsFinite(values[i]))
            {
                throw new TerrainGenerationException(
                    TerrainGenerationError.NumericOverflow,
                    "Nonfinite centroid value while mining terrain candidates.");
            }
        }

        return values;
    }

    private static int cornerBit(int corner)
    {
        return corner switch
        {
            0 => TerrainMasks.NorthWest,
            1 => TerrainMasks.NorthEast,
            2 => TerrainMasks.SouthEast,
            _ => TerrainMasks.SouthWest,
        };
    }

    private static bool HasStrictAdjacencyEvidence(Dictionary<string, HashSet<(int, int, int, int)>>? byMap)
    {
        if (byMap is null)
        {
            return false;
        }

        if (byMap.Count >= 2)
        {
            return true;
        }

        foreach (var set in byMap.Values)
        {
            var list = set.ToList();
            for (var i = 0; i < list.Count; i++)
            {
                for (var j = i + 1; j < list.Count; j++)
                {
                    if (AreDisjoint(list[i], list[j]))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool AreDisjoint((int, int, int, int) a, (int, int, int, int) b)
    {
        return (a.Item1 != b.Item1 || a.Item2 != b.Item2)
            && (a.Item1 != b.Item3 || a.Item2 != b.Item4)
            && (a.Item3 != b.Item1 || a.Item4 != b.Item2)
            && (a.Item3 != b.Item3 || a.Item4 != b.Item4);
    }

    private static int CompareCells(int x1, int y1, int x2, int y2)
        => x1 != x2 ? x1.CompareTo(x2) : y1.CompareTo(y2);

    private static (TerrainGraphicReference, TerrainGraphicReference) Order(TerrainGraphicReference a, TerrainGraphicReference b)
        => CompareReferences(a, b) <= 0 ? (a, b) : (b, a);

    private static int CompareReferences(TerrainGraphicReference a, TerrainGraphicReference b)
        => a.Sheet != b.Sheet ? a.Sheet.CompareTo(b.Sheet) : a.Graphic.CompareTo(b.Graphic);

    private static IReadOnlyList<TerrainDiagnostic> DeduplicateAndOrder(List<TerrainDiagnostic> diagnostics)
    {
        return diagnostics
            .GroupBy(diagnostic => (diagnostic.Code, diagnostic.Message, diagnostic.Mask, diagnostic.Reference))
            .Select(group => group.First())
            .OrderBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Mask.HasValue)
            .ThenBy(diagnostic => diagnostic.Mask ?? 0)
            .ThenBy(diagnostic => diagnostic.Reference.HasValue)
            .ThenBy(diagnostic => diagnostic.Reference?.Sheet ?? 0)
            .ThenBy(diagnostic => diagnostic.Reference?.Graphic ?? 0)
            .ThenBy(diagnostic => diagnostic.Message, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();
    }
}