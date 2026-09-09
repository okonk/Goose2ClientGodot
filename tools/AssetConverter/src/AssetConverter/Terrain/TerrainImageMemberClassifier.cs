using System.Globalization;
using MapEditor.Core.Terrain;

namespace Goose2.AssetConverter.Terrain;

public sealed record TerrainImageMemberAdmission(
    TerrainCandidateFamily Family,
    TerrainGraphicReference Reference,
    int Mask,
    double Compatibility,
    double OwnerMargin);

public sealed record TerrainImageMemberClassificationResult(
    IReadOnlyList<TerrainImageMemberAdmission> Admissions,
    IReadOnlyList<(TerrainCandidateFamily Family, TerrainDiagnostic Diagnostic)> SetDiagnostics,
    IReadOnlyList<TerrainDiagnostic> RootDiagnostics);

public static class TerrainImageMemberClassifier
{
    private const string CodeAmbiguous = "image-only-ambiguous";
    private const string CodeCentroidsMissing = "image-only-centroids-missing";
    private const string CodeHeldOutOnly = "heldout-only-member-excluded";

    public static TerrainImageMemberClassificationResult Classify(
        TerrainCorpus corpus,
        ITerrainFeatureSource features,
        IReadOnlyList<TerrainCandidateFamily> families,
        TerrainGenerationSettings settings)
    {
        var familyMembers = new HashSet<TerrainGraphicReference>();
        foreach (var family in families)
        {
            foreach (var member in family.Members)
            {
                familyMembers.Add(member);
            }
        }

        var heldOutObserved = new HashSet<TerrainGraphicReference>();
        var reader = new TerrainMapBatchReader(corpus.Root);
        foreach (var map in corpus.Maps)
        {
            if (!TerrainHoldout.IsHeldOut(map.Identity, settings.HoldoutModulo))
            {
                continue;
            }

            reader.VisitMap(map, decoded =>
            {
                for (var y = 0; y < decoded.Height; y++)
                {
                    for (var x = 0; x < decoded.Width; x++)
                    {
                        var (sheet, graphic) = decoded.GetLayer(x, y);
                        if (graphic != 0)
                        {
                            heldOutObserved.Add(new TerrainGraphicReference(sheet, graphic));
                        }
                    }
                }
            });
        }

        var setDiagnostics = new List<(TerrainCandidateFamily Family, TerrainDiagnostic Diagnostic)>();
        var rootDiagnostics = new List<TerrainDiagnostic>();
        var trainingObserved = new HashSet<TerrainGraphicReference>();
        foreach (var map in corpus.Maps)
        {
            if (TerrainHoldout.IsHeldOut(map.Identity, settings.HoldoutModulo))
            {
                continue;
            }

            reader.VisitMap(map, decoded =>
            {
                for (var y = 0; y < decoded.Height; y++)
                {
                    for (var x = 0; x < decoded.Width; x++)
                    {
                        var (sheet, graphic) = decoded.GetLayer(x, y);
                        if (graphic != 0)
                        {
                            trainingObserved.Add(new TerrainGraphicReference(sheet, graphic));
                        }
                    }
                }
            });
        }

        foreach (var reference in heldOutObserved.OrderBy(r => r.Sheet).ThenBy(r => r.Graphic))
        {
            if (familyMembers.Contains(reference) || trainingObserved.Contains(reference))
            {
                continue;
            }

            if (!corpus.FrameIndex.TryGetRect(reference, out var rect)
                || rect.Width != TerrainCorpusLoader.RequiredTileSize
                || rect.Height != TerrainCorpusLoader.RequiredTileSize)
            {
                continue;
            }

            rootDiagnostics.Add(new TerrainDiagnostic(
                CodeHeldOutOnly,
                $"Map-observed reference ({reference.Sheet},{reference.Graphic}) has no training-family owner and was not considered image-only.",
                reference: reference));
        }

        // Snapshot: owner scores are computed against the pre-admission family models only.
        var snapshot = families
            .Select(family => new FamilySnapshot(
                family,
                family.Medoid,
                family.SideCentroids,
                family.CornerCentroids,
                family.SideCentroids.Any(side => side.Connected is null || side.Disconnected is null)))
            .ToList();

        var observed = new HashSet<TerrainGraphicReference>(corpus.ObservedReferences);
        var universe = new HashSet<TerrainGraphicReference>();
        foreach (var entry in snapshot)
        {
            var sheet = entry.Family.Members[0].Sheet;
            foreach (var member in entry.Family.Members)
            {
                foreach (var candidate in features.QueryCandidates(member))
                {
                    if (candidate.Graphic == 0 || candidate.Sheet != sheet || observed.Contains(candidate))
                    {
                        continue;
                    }

                    if (!corpus.FrameIndex.TryGetRect(candidate, out var rect)
                        || rect.Width != TerrainCorpusLoader.RequiredTileSize
                        || rect.Height != TerrainCorpusLoader.RequiredTileSize)
                    {
                        continue;
                    }

                    universe.Add(candidate);
                }
            }
        }

        var admissions = new List<TerrainImageMemberAdmission>();
        var centroidsMissing = new HashSet<TerrainCandidateFamily>();
        foreach (var candidate in universe.OrderBy(r => r.Sheet).ThenBy(r => r.Graphic))
        {
            if (!features.TryGetFeatures(candidate, out var candidateFeatures) || candidateFeatures is null)
            {
                continue;
            }

            var sameSheet = new List<(FamilySnapshot Family, double Score)>();
            foreach (var entry in snapshot)
            {
                if (entry.Family.Members[0].Sheet != candidate.Sheet)
                {
                    continue;
                }

                sameSheet.Add((entry, features.Similarity(candidate, entry.Medoid)));
            }

            if (sameSheet.Count == 0)
            {
                continue;
            }

            var bestOverall = sameSheet.Max(entry => entry.Score);
            if (bestOverall < settings.ImageOnlyCompatibility)
            {
                continue;
            }

            foreach (var entry in sameSheet.Where(entry => entry.Score == bestOverall && entry.Family.MissingSideCentroids))
            {
                centroidsMissing.Add(entry.Family.Family);
            }

            var eligible = sameSheet.Where(entry => !entry.Family.MissingSideCentroids).ToList();
            if (eligible.Count == 0)
            {
                continue;
            }

            var bestScore = eligible.Max(entry => entry.Score);
            if (bestScore < settings.ImageOnlyCompatibility)
            {
                continue;
            }

            var tied = eligible.Where(entry => entry.Score == bestScore).ToList();
            if (tied.Count > 1)
            {
                foreach (var entry in tied)
                {
                    setDiagnostics.Add((entry.Family.Family, Ambiguous(candidate, bestScore, 0.0)));
                }

                continue;
            }

            var best = tied[0].Family;

            var runnerUp = eligible.Count > 1 ? eligible.Where(entry => entry.Family != best).Max(entry => entry.Score) : 0.0;
            var margin = bestScore - runnerUp;
            if (eligible.Count > 1 && margin < settings.MinimumClassificationMargin)
            {
                setDiagnostics.Add((best.Family, Ambiguous(candidate, bestScore, margin)));
                continue;
            }

            if (!TryClassifyMask(candidateFeatures, best, settings, out var mask))
            {
                setDiagnostics.Add((best.Family, Ambiguous(candidate, bestScore, margin)));
                continue;
            }

            mask = TerrainMasks.Normalize(mask, TerrainTopology.EightWay);
            admissions.Add(new TerrainImageMemberAdmission(best.Family, candidate, mask, bestScore, margin));
        }

        foreach (var family in centroidsMissing)
        {
            setDiagnostics.Add((family, new TerrainDiagnostic(CodeCentroidsMissing,
                "Image-only expansion skipped because connected/disconnected edge centroids are incomplete.")));
        }

        var familyIndex = new Dictionary<TerrainCandidateFamily, int>();
        for (var i = 0; i < families.Count; i++)
        {
            familyIndex[families[i]] = i;
        }

        return new TerrainImageMemberClassificationResult(
            admissions.AsReadOnly(),
            DeduplicateAndOrder(setDiagnostics, entry => familyIndex[entry.Family]),
            DeduplicateAndOrder(rootDiagnostics));
    }

    private static bool TryClassifyMask(
        TerrainImageFeatures candidate,
        FamilySnapshot family,
        TerrainGenerationSettings settings,
        out int mask)
    {
        var raw = 0;
        for (var side = 0; side < 4; side++)
        {
            var centroid = family.SideCentroids[side];
            var descriptor = SideDescriptor(candidate.Edges, side);
            var connected = SimilarityTo(descriptor, centroid.Connected!);
            var disconnected = SimilarityTo(descriptor, centroid.Disconnected!);
            var difference = Math.Abs(connected - disconnected);
            if (difference < settings.MinimumClassificationMargin)
            {
                return Fail(out mask);
            }

            if (connected > disconnected)
            {
                raw |= 1 << side;
            }
        }

        for (var corner = 0; corner < 4; corner++)
        {
            var northBit = corner is 0 or 1 ? TerrainMasks.North : TerrainMasks.South;
            var eastBit = corner is 1 or 2 ? TerrainMasks.East : TerrainMasks.West;
            if ((raw & (northBit | eastBit)) != (northBit | eastBit))
            {
                continue;
            }

            var centroid = family.CornerCentroids[corner];
            if (centroid.Present is null || centroid.Absent is null)
            {
                return Fail(out mask);
            }

            var descriptor = CornerDescriptor(candidate.Corners, corner);
            var present = SimilarityTo(descriptor, centroid.Present);
            var absent = SimilarityTo(descriptor, centroid.Absent);
            var difference = Math.Abs(present - absent);
            if (difference < settings.MinimumClassificationMargin)
            {
                return Fail(out mask);
            }

            if (present > absent)
            {
                raw |= corner switch
                {
                    0 => TerrainMasks.NorthWest,
                    1 => TerrainMasks.NorthEast,
                    2 => TerrainMasks.SouthEast,
                    _ => TerrainMasks.SouthWest,
                };
            }
        }

        mask = raw;
        return true;
    }

    private static bool Fail(out int mask)
    {
        mask = 0;
        return false;
    }

    private static double SimilarityTo(double[] descriptor, double[] centroid)
    {
        var sum = 0.0;
        for (var i = 0; i < descriptor.Length; i++)
        {
            sum += Math.Abs(descriptor[i] - centroid[i]);
        }

        return 1.0 - sum / descriptor.Length;
    }

    private static double[] SideDescriptor(double[] edges, int side)
    {
        var values = new double[160];
        Array.Copy(edges, side * 160, values, 0, 160);
        return values;
    }

    private static double[] CornerDescriptor(double[] corners, int corner)
    {
        var values = new double[5];
        Array.Copy(corners, corner * 5, values, 0, 5);
        return values;
    }

    private static TerrainDiagnostic Ambiguous(TerrainGraphicReference reference, double score, double margin)
        => new(CodeAmbiguous,
            $"Rejected image-only ({reference.Sheet},{reference.Graphic}): compatibility {Format(score)}, owner margin {Format(margin)}.",
            reference: reference);

    private static string Format(double value) => value.ToString("F6", CultureInfo.InvariantCulture);

    private static IReadOnlyList<(TerrainCandidateFamily Family, TerrainDiagnostic Diagnostic)> DeduplicateAndOrder(
        List<(TerrainCandidateFamily Family, TerrainDiagnostic Diagnostic)> entries,
        Func<(TerrainCandidateFamily Family, TerrainDiagnostic Diagnostic), int> familyKey)
    {
        return entries
            .GroupBy(entry => (entry.Family, entry.Diagnostic.Code, entry.Diagnostic.Message, entry.Diagnostic.Mask, entry.Diagnostic.Reference))
            .Select(group => group.First())
            .OrderBy(entry => familyKey(entry))
            .ThenBy(entry => entry.Diagnostic.Code, StringComparer.Ordinal)
            .ThenBy(entry => entry.Diagnostic.Mask.HasValue)
            .ThenBy(entry => entry.Diagnostic.Mask ?? 0)
            .ThenBy(entry => entry.Diagnostic.Reference.HasValue)
            .ThenBy(entry => entry.Diagnostic.Reference?.Sheet ?? 0)
            .ThenBy(entry => entry.Diagnostic.Reference?.Graphic ?? 0)
            .ThenBy(entry => entry.Diagnostic.Message, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();
    }

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

    private sealed class FamilySnapshot
    {
        public FamilySnapshot(
            TerrainCandidateFamily family,
            TerrainGraphicReference medoid,
            IReadOnlyList<TerrainSideCentroid> sideCentroids,
            IReadOnlyList<TerrainCornerCentroid> cornerCentroids,
            bool missingSideCentroids)
        {
            Family = family;
            Medoid = medoid;
            SideCentroids = sideCentroids;
            CornerCentroids = cornerCentroids;
            MissingSideCentroids = missingSideCentroids;
        }

        public TerrainCandidateFamily Family { get; }
        public TerrainGraphicReference Medoid { get; }
        public IReadOnlyList<TerrainSideCentroid> SideCentroids { get; }
        public IReadOnlyList<TerrainCornerCentroid> CornerCentroids { get; }
        public bool MissingSideCentroids { get; }
    }
}
