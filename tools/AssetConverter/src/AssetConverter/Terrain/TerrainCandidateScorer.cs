using System.Globalization;
using MapEditor.Core.Terrain;

namespace Goose2.AssetConverter.Terrain;

public sealed record TerrainCandidateScore
{
    public TerrainModelFit Fit { get; }
    public TerrainTopology Topology { get; }
    public TerrainReviewStatus Status { get; }
    public double MaskEntropy { get; }
    public double Completeness { get; }
    public double Ambiguity { get; }
    public double VisualCompatibility { get; }
    public double HoldoutAccuracy { get; }
    public double EightWayAccuracyGain { get; }
    public double SupportScore { get; }
    public double Confidence { get; }
    public TerrainSetMetrics Metrics { get; }
    public IReadOnlyList<TerrainMemberDefinition> FinalMembers { get; }
    public IReadOnlyList<(TerrainGraphicReference Member, double Similarity)> MemberSimilarities { get; }
    public IReadOnlyList<TerrainDiagnostic> Diagnostics { get; }

    internal TerrainCandidateScore(
        TerrainModelFit fit,
        TerrainTopology topology,
        TerrainReviewStatus status,
        double maskEntropy,
        double completeness,
        double ambiguity,
        double visualCompatibility,
        double holdoutAccuracy,
        double eightWayAccuracyGain,
        double supportScore,
        double confidence,
        TerrainSetMetrics metrics,
        IReadOnlyList<TerrainMemberDefinition> finalMembers,
        IReadOnlyList<(TerrainGraphicReference Member, double Similarity)> memberSimilarities,
        IReadOnlyList<TerrainDiagnostic> diagnostics)
    {
        Fit = fit;
        Topology = topology;
        Status = status;
        MaskEntropy = maskEntropy;
        Completeness = completeness;
        Ambiguity = ambiguity;
        VisualCompatibility = visualCompatibility;
        HoldoutAccuracy = holdoutAccuracy;
        EightWayAccuracyGain = eightWayAccuracyGain;
        SupportScore = supportScore;
        Confidence = confidence;
        Metrics = metrics;
        FinalMembers = finalMembers;
        MemberSimilarities = memberSimilarities;
        Diagnostics = diagnostics;
    }
}

public static class TerrainCandidateScorer
{
    private const string CodeNoTraining = "no-training-observations";
    private const string CodeNoHoldout = "no-holdout-observations";
    private const string CodeEightWayInsufficient = "eight-way-evidence-insufficient";
    private const string CodeSupport = "support-below-minimum";
    private const string CodeIncomplete = "incomplete-required-masks";
    private const string CodeAmbiguity = "ambiguity-above-maximum";
    private const string CodeHoldout = "holdout-accuracy-below-enabled";
    private const string CodeConfidenceEnabled = "confidence-below-enabled";
    private const string CodeConfidencePending = "confidence-below-pending";
    private const string CodeMemberFrameInvalid = "member-frame-invalid";

    public static TerrainCandidateScore Score(
        TerrainModelFit fit,
        IReadOnlyList<TerrainImageMemberAdmission> familyAdmissions,
        TerrainCorpus corpus,
        ITerrainFeatureSource features,
        TerrainGenerationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(fit);
        ArgumentNullException.ThrowIfNull(familyAdmissions);
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(settings);

        var family = fit.Family;
        var topology = fit.SelectedTopology;
        var selected = topology == TerrainTopology.EightWay ? fit.EightWay : fit.FourWay;
        var required = TerrainMasks.Required(topology);

        var maskMass = new Dictionary<int, double>(required.Count);
        var totalMass = 0.0;
        foreach (var masks in family.WeightedMasks.Values)
        {
            foreach (var (raw, weight) in masks)
            {
                var mask = TerrainMasks.Normalize(raw, topology);
                maskMass[mask] = maskMass.GetValueOrDefault(mask) + weight;
                totalMass += weight;
            }
        }

        var entropy = 0.0;
        if (totalMass > 0.0)
        {
            foreach (var weight in maskMass.Values)
            {
                if (weight > 0.0)
                {
                    var p = weight / totalMass;
                    entropy -= p * Math.Log(p);
                }
            }

            entropy /= Math.Log(required.Count);
        }

        var nonempty = 0;
        foreach (var mask in required)
        {
            if (selected.EmittedVariants[mask].Count > 0)
            {
                nonempty++;
            }
        }

        var completeness = (double)nonempty / required.Count;

        var referenceMass = 0.0;
        var ambiguityNumerator = 0.0;
        foreach (var masks in family.WeightedMasks.Values)
        {
            var total = 0.0;
            var byMask = new Dictionary<int, double>();
            foreach (var (raw, weight) in masks)
            {
                total += weight;
                var mask = TerrainMasks.Normalize(raw, topology);
                byMask[mask] = byMask.GetValueOrDefault(mask) + weight;
            }

            if (total <= 0.0)
            {
                continue;
            }

            var modal = 0.0;
            foreach (var weight in byMask.Values)
            {
                if (weight > modal)
                {
                    modal = weight;
                }
            }

            referenceMass += total;
            ambiguityNumerator += total - modal;
        }

        var ambiguity = referenceMass > 0.0 ? ambiguityNumerator / referenceMass : 1.0;

        var finalMembers = new List<TerrainMemberDefinition>(family.Members.Count);
        foreach (var member in family.Members.OrderBy(r => r.Sheet).ThenBy(r => r.Graphic))
        {
            finalMembers.Add(new TerrainMemberDefinition(member, TerrainMemberProvenance.MapObserved));
        }

        var admitted = familyAdmissions
            .Where(admission => admission.Family == family)
            .OrderBy(admission => admission.Reference.Sheet)
            .ThenBy(admission => admission.Reference.Graphic)
            .ToList();
        foreach (var reference in admitted.Select(admission => admission.Reference))
        {
            finalMembers.Add(new TerrainMemberDefinition(reference, TerrainMemberProvenance.ImageOnly));
        }

        var memberSimilarities = new List<(TerrainGraphicReference Member, double Similarity)>(finalMembers.Count);
        foreach (var member in finalMembers)
        {
            memberSimilarities.Add((member.Reference, features.Similarity(member.Reference, family.Medoid)));
        }

        var visual = finalMembers.Count == 0
            ? 0.0
            : memberSimilarities.Aggregate(0.0, (sum, entry) => sum + entry.Similarity) / finalMembers.Count;

        var holdout = selected.HoldoutAccuracy;
        var support = Math.Min(
            Math.Min(1.0, family.MapSupport / (double)settings.MinimumMapSupport),
            Math.Min(Math.Min(1.0, family.RegionSupport / (double)settings.MinimumRegionSupport),
                Math.Min(1.0, family.ObservationSupport / (double)settings.MinimumObservationSupport)));
        var confidence = 0.20 * support + 0.15 * entropy + 0.25 * completeness + 0.15 * (1.0 - ambiguity) + 0.10 * visual + 0.15 * holdout;

        var diagnostics = new List<TerrainDiagnostic>();
        var framesValid = true;
        foreach (var member in finalMembers)
        {
            if (!corpus.FrameIndex.TryGetRect(member.Reference, out var rect)
                || rect.Width != TerrainCorpusLoader.RequiredTileSize
                || rect.Height != TerrainCorpusLoader.RequiredTileSize)
            {
                framesValid = false;
                diagnostics.Add(new TerrainDiagnostic(
                    CodeMemberFrameInvalid,
                    $"Member frame ({member.Reference.Sheet},{member.Reference.Graphic}) is missing or not a {TerrainCorpusLoader.RequiredTileSize}x{TerrainCorpusLoader.RequiredTileSize} tile.",
                    reference: member.Reference));
            }
        }

        var mapOk = family.MapSupport >= settings.MinimumMapSupport;
        var regionOk = family.RegionSupport >= settings.MinimumRegionSupport;
        var observationOk = family.ObservationSupport >= settings.MinimumObservationSupport;
        if (!mapOk || !regionOk || !observationOk)
        {
            diagnostics.Add(new TerrainDiagnostic(CodeSupport,
                $"Support {family.MapSupport}/{family.RegionSupport}/{family.ObservationSupport} is below required {settings.MinimumMapSupport}/{settings.MinimumRegionSupport}/{settings.MinimumObservationSupport}."));
        }

        if (completeness < 1.0)
        {
            diagnostics.Add(new TerrainDiagnostic(CodeIncomplete,
                $"Completeness {Format(completeness)} leaves {required.Count - nonempty} required masks without variants."));
        }

        if (ambiguity > settings.MaximumAmbiguity)
        {
            diagnostics.Add(new TerrainDiagnostic(CodeAmbiguity,
                $"Ambiguity {Format(ambiguity)} exceeds maximum {Format(settings.MaximumAmbiguity)}."));
        }

        if (holdout < settings.EnabledConfidence)
        {
            diagnostics.Add(new TerrainDiagnostic(CodeHoldout,
                $"Holdout accuracy {Format(holdout)} is below enabled threshold {Format(settings.EnabledConfidence)}."));
        }

        var enabled = mapOk && regionOk && observationOk
            && completeness == 1.0
            && ambiguity <= settings.MaximumAmbiguity
            && holdout >= settings.EnabledConfidence
            && confidence >= settings.EnabledConfidence
            && framesValid;
        var pending = !enabled
            && (confidence >= settings.PendingConfidence || (completeness < 1.0 && mapOk && regionOk && observationOk));
        var status = enabled
            ? TerrainReviewStatus.Enabled
            : pending ? TerrainReviewStatus.Pending : TerrainReviewStatus.Disabled;

        if (status == TerrainReviewStatus.Pending && confidence < settings.EnabledConfidence)
        {
            diagnostics.Add(new TerrainDiagnostic(CodeConfidenceEnabled,
                $"Confidence {Format(confidence)} is below enabled threshold {Format(settings.EnabledConfidence)}."));
        }

        if (status == TerrainReviewStatus.Disabled && confidence < settings.PendingConfidence)
        {
            diagnostics.Add(new TerrainDiagnostic(CodeConfidencePending,
                $"Confidence {Format(confidence)} is below pending threshold {Format(settings.PendingConfidence)}."));
        }

        if (!fit.HasTrainingObservations)
        {
            diagnostics.Add(new TerrainDiagnostic(CodeNoTraining, "No training observations were available."));
        }

        if (!fit.HasEligibleHoldoutObservations)
        {
            diagnostics.Add(new TerrainDiagnostic(CodeNoHoldout, "No held-out observations were available; holdout accuracy is 0.000000."));
        }

        if (fit.DiagonalSupport > 0 && topology == TerrainTopology.FourWay)
        {
            diagnostics.Add(new TerrainDiagnostic(CodeEightWayInsufficient,
                $"Eight-way not selected: diagonal support {fit.DiagonalSupport}, diagonal maps {fit.DiagonalMapSupport}, four-way accuracy {Format(fit.FourWay.HoldoutAccuracy)}, eight-way accuracy {Format(fit.EightWay.HoldoutAccuracy)}, required gain {Format(settings.MinimumEightWayAccuracyGain)}."));
        }

        var ordered = DeduplicateAndOrder(diagnostics);
        var metrics = new TerrainSetMetrics(
            family.MapSupport,
            family.RegionSupport,
            family.ObservationSupport,
            fit.DiagonalSupport,
            Round(entropy),
            Round(completeness),
            Round(ambiguity),
            Round(visual),
            Round(holdout),
            Round(fit.EightWayAccuracyGain),
            Round(confidence));

        return new TerrainCandidateScore(
            fit,
            topology,
            status,
            entropy,
            completeness,
            ambiguity,
            visual,
            holdout,
            fit.EightWayAccuracyGain,
            support,
            confidence,
            metrics,
            finalMembers.AsReadOnly(),
            memberSimilarities.AsReadOnly(),
            ordered);
    }

    private static string Format(double value) => value.ToString("F6", CultureInfo.InvariantCulture);

    private static double Round(double value) => Math.Round(value, 6, MidpointRounding.ToEven);

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
