using System.Globalization;
using MapEditor.Core.Terrain;

namespace Goose2.AssetConverter.Terrain;

public static class TerrainCatalogBuilder
{
    private const string GeneratorVersion = "terrain-v1";
    private const string CodeAdmitted = "image-only-member-admitted";
    private const string CodeConflict = "enabled-member-conflict";

    private static readonly HashSet<string> ExplanationCodes = new(StringComparer.Ordinal)
    {
        "no-training-observations",
        "no-holdout-observations",
        "eight-way-evidence-insufficient",
        "support-below-minimum",
        "incomplete-required-masks",
        "ambiguity-above-maximum",
        "holdout-accuracy-below-enabled",
        "confidence-below-enabled",
        "confidence-below-pending",
        "enabled-member-conflict",
        "member-frame-invalid",
    };

    public static TerrainCatalog Build(
        TerrainCorpus corpus,
        TerrainCandidateMinerResult mined,
        TerrainImageMemberClassificationResult classification,
        ITerrainFeatureSource features,
        TerrainGenerationSettings settings,
        ITerrainMapDataReader? mapReader = null)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentNullException.ThrowIfNull(mined);
        ArgumentNullException.ThrowIfNull(classification);
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(settings);

        var sets = new List<TerrainSetDefinition>(mined.Families.Count);
        var confidences = new List<double>(mined.Families.Count);
        for (var i = 0; i < mined.Families.Count; i++)
        {
            var family = mined.Families[i];
            var familyAdmissions = classification.Admissions
                .Where(admission => admission.Family == family)
                .ToList();
            var fit = TerrainModelFitter.Fit(family, familyAdmissions, corpus, features, settings, mapReader);
            var score = TerrainCandidateScorer.Score(fit, familyAdmissions, corpus, features, settings);

            var id = TerrainGeneratedId.Create(score.Topology, score.FinalMembers.Select(member => member.Reference));
            var displayName = BuildDisplayName(id, score.FinalMembers);
            var masks = BuildMasks(fit, score.Topology);
            var diagnostics = BuildSetDiagnostics(score, family, familyAdmissions, classification);

            sets.Add(new TerrainSetDefinition(id, displayName, score.Status, score.Topology, score.Metrics, masks, score.FinalMembers, diagnostics));
            confidences.Add(score.Confidence);
        }

        sets = ResolveOverlaps(sets, confidences).ToList();
        AssertExplanations(sets);

        var orderedSets = sets.OrderBy(set => set.Id, StringComparer.Ordinal).ToList();
        var rootDiagnostics = DeduplicateAndOrder(
            corpus.Diagnostics
                .Concat(mined.RootDiagnostics)
                .Concat(classification.RootDiagnostics)
                .ToList());

        var catalog = new TerrainCatalog(
            TerrainCatalogJson.CurrentSchemaVersion,
            GeneratorVersion,
            corpus.Fingerprint,
            settings,
            orderedSets,
            rootDiagnostics);

        var issues = TerrainCatalogValidator.Validate(catalog);
        if (issues.Count > 0)
        {
            throw new TerrainGenerationException(
                TerrainGenerationError.InvalidGeneratedCatalog,
                "Generated terrain catalog failed validation: " + string.Join(" ", issues.Select(issue => issue.Message)));
        }

        return catalog;
    }

    internal static IReadOnlyList<TerrainSetDefinition> ResolveOverlaps(
        IReadOnlyList<TerrainSetDefinition> sets,
        IReadOnlyList<double> confidences)
    {
        var enabled = new List<int>();
        for (var i = 0; i < sets.Count; i++)
        {
            if (sets[i].Status == TerrainReviewStatus.Enabled)
            {
                enabled.Add(i);
            }
        }

        enabled.Sort((a, b) =>
        {
            var result = confidences[b].CompareTo(confidences[a]);
            if (result != 0)
            {
                return result;
            }

            result = sets[b].Metrics.MapSupport.CompareTo(sets[a].Metrics.MapSupport);
            if (result != 0)
            {
                return result;
            }

            result = sets[b].Metrics.RegionSupport.CompareTo(sets[a].Metrics.RegionSupport);
            if (result != 0)
            {
                return result;
            }

            result = sets[b].Metrics.ObservationSupport.CompareTo(sets[a].Metrics.ObservationSupport);
            if (result != 0)
            {
                return result;
            }

            return string.CompareOrdinal(sets[a].Id, sets[b].Id);
        });

        var resolved = new List<TerrainSetDefinition>(sets);
        var claimed = new Dictionary<TerrainGraphicReference, string>();
        foreach (var index in enabled)
        {
            var set = resolved[index];
            var conflicts = new List<TerrainGraphicReference>();
            foreach (var member in set.Members)
            {
                if (claimed.TryGetValue(member.Reference, out var owner) && owner != set.Id)
                {
                    conflicts.Add(member.Reference);
                }
            }

            if (conflicts.Count == 0)
            {
                foreach (var member in set.Members)
                {
                    if (!claimed.ContainsKey(member.Reference))
                    {
                        claimed[member.Reference] = set.Id;
                    }
                }

                continue;
            }

            var diagnostics = new List<TerrainDiagnostic>(set.Diagnostics);
            foreach (var reference in conflicts)
            {
                diagnostics.Add(new TerrainDiagnostic(
                    CodeConflict,
                    $"Demoted from enabled because ({reference.Sheet},{reference.Graphic}) is owned by higher-ranked terrain '{claimed[reference]}'.",
                    reference: reference));
            }

            resolved[index] = new TerrainSetDefinition(
                set.Id,
                set.DisplayName,
                TerrainReviewStatus.Pending,
                set.Topology,
                set.Metrics,
                set.Masks,
                set.Members,
                DeduplicateAndOrder(diagnostics));
        }

        return resolved.AsReadOnly();
    }

    private static string BuildDisplayName(string id, IReadOnlyList<TerrainMemberDefinition> members)
    {
        var lowestSheet = members.Min(member => member.Reference.Sheet);
        var hash = id.AsSpan(id.Length - 64, 8);
        return $"Generated {lowestSheet}-{hash}";
    }

    private static IReadOnlyList<TerrainMaskDefinition> BuildMasks(TerrainModelFit fit, TerrainTopology topology)
    {
        var selected = topology == TerrainTopology.EightWay ? fit.EightWay : fit.FourWay;
        var required = TerrainMasks.Required(topology);
        var masks = new List<TerrainMaskDefinition>(required.Count);
        foreach (var mask in required.OrderBy(value => value))
        {
            masks.Add(new TerrainMaskDefinition(mask, selected.EmittedVariants[mask]));
        }

        return masks.AsReadOnly();
    }

    private static IReadOnlyList<TerrainDiagnostic> BuildSetDiagnostics(
        TerrainCandidateScore score,
        TerrainCandidateFamily family,
        IReadOnlyList<TerrainImageMemberAdmission> familyAdmissions,
        TerrainImageMemberClassificationResult classification)
    {
        var diagnostics = new List<TerrainDiagnostic>(score.Diagnostics);
        foreach (var (memberFamily, diagnostic) in classification.SetDiagnostics)
        {
            if (memberFamily == family)
            {
                diagnostics.Add(diagnostic);
            }
        }

        foreach (var admission in familyAdmissions)
        {
            var mask = TerrainMasks.Normalize(admission.Mask, score.Topology);
            diagnostics.Add(new TerrainDiagnostic(
                CodeAdmitted,
                $"Admitted image-only ({admission.Reference.Sheet},{admission.Reference.Graphic}) at mask 0x{mask.ToString("X2", CultureInfo.InvariantCulture)}: compatibility {Format(admission.Compatibility)}, owner margin {Format(admission.OwnerMargin)}.",
                mask: mask,
                reference: admission.Reference));
        }

        return DeduplicateAndOrder(diagnostics);
    }

    private static void AssertExplanations(IReadOnlyList<TerrainSetDefinition> sets)
    {
        foreach (var set in sets)
        {
            if (set.Status == TerrainReviewStatus.Enabled)
            {
                continue;
            }

            if (!set.Diagnostics.Any(diagnostic => ExplanationCodes.Contains(diagnostic.Code)))
            {
                throw new TerrainGenerationException(
                    TerrainGenerationError.InvalidGeneratedCatalog,
                    $"Terrain '{set.Id}' is {set.Status} but carries no status-explanation diagnostic.");
            }
        }
    }

    private static string Format(double value) => value.ToString("F6", CultureInfo.InvariantCulture);

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
