using MapEditor.Core.Terrain;

namespace Goose2.AssetConverter.Terrain;

public sealed record TerrainModelFitTopology
{
    public TerrainTopology Topology { get; }
    public IReadOnlyDictionary<int, IReadOnlyList<TerrainGraphicReference>> EmittedVariants { get; }
    public IReadOnlyDictionary<int, TerrainGraphicReference?> Predictors { get; }
    public double HoldoutAccuracy { get; }
    public int TiedMemberCount { get; }

    internal TerrainModelFitTopology(
        TerrainTopology topology,
        IReadOnlyDictionary<int, IReadOnlyList<TerrainGraphicReference>> emittedVariants,
        IReadOnlyDictionary<int, TerrainGraphicReference?> predictors,
        double holdoutAccuracy,
        int tiedMemberCount)
    {
        Topology = topology;
        EmittedVariants = emittedVariants;
        Predictors = predictors;
        HoldoutAccuracy = holdoutAccuracy;
        TiedMemberCount = tiedMemberCount;
    }
}

public sealed record TerrainModelFit
{
    public TerrainCandidateFamily Family { get; }
    public TerrainTopology SelectedTopology { get; }
    public TerrainModelFitTopology FourWay { get; }
    public TerrainModelFitTopology EightWay { get; }
    public double EightWayAccuracyGain { get; }
    public bool HasTrainingObservations { get; }
    public bool HasEligibleHoldoutObservations { get; }
    public int DiagonalSupport { get; }
    public int DiagonalMapSupport { get; }

    internal TerrainModelFit(
        TerrainCandidateFamily family,
        TerrainTopology selectedTopology,
        TerrainModelFitTopology fourWay,
        TerrainModelFitTopology eightWay,
        double eightWayAccuracyGain,
        bool hasTrainingObservations,
        bool hasEligibleHoldoutObservations,
        int diagonalSupport,
        int diagonalMapSupport)
    {
        Family = family;
        SelectedTopology = selectedTopology;
        FourWay = fourWay;
        EightWay = eightWay;
        EightWayAccuracyGain = eightWayAccuracyGain;
        HasTrainingObservations = hasTrainingObservations;
        HasEligibleHoldoutObservations = hasEligibleHoldoutObservations;
        DiagonalSupport = diagonalSupport;
        DiagonalMapSupport = diagonalMapSupport;
    }
}

public static class TerrainModelFitter
{
    public static TerrainModelFit Fit(
        TerrainCandidateFamily family,
        IReadOnlyList<TerrainImageMemberAdmission> familyAdmissions,
        TerrainCorpus corpus,
        ITerrainFeatureSource features,
        TerrainGenerationSettings settings,
        ITerrainMapDataReader? mapReader = null)
    {
        ArgumentNullException.ThrowIfNull(family);
        ArgumentNullException.ThrowIfNull(familyAdmissions);
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(settings);

        var fourWay = FitTopology(family, familyAdmissions, TerrainTopology.FourWay);
        var eightWay = FitTopology(family, familyAdmissions, TerrainTopology.EightWay);
        var holdout = EvaluateHoldout(family, corpus, settings, fourWay.Predictors, eightWay.Predictors, mapReader);

        var four = new TerrainModelFitTopology(
            TerrainTopology.FourWay,
            fourWay.Emitted,
            fourWay.Predictors,
            holdout.FourWayAccuracy,
            fourWay.TiedMembers);
        var eight = new TerrainModelFitTopology(
            TerrainTopology.EightWay,
            eightWay.Emitted,
            eightWay.Predictors,
            holdout.EightWayAccuracy,
            eightWay.TiedMembers);

        var gain = eight.HoldoutAccuracy - four.HoldoutAccuracy;
        if (!double.IsFinite(gain))
        {
            throw new TerrainGenerationException(
                TerrainGenerationError.NumericOverflow,
                "Nonfinite eight-way accuracy gain while fitting terrain model.");
        }

        var selected = family.DiagonalSupport >= settings.MinimumDiagonalSupport
            && family.DiagonalMapSupport >= settings.MinimumMapSupport
            && gain >= settings.MinimumEightWayAccuracyGain
            ? TerrainTopology.EightWay
            : TerrainTopology.FourWay;

        return new TerrainModelFit(
            family,
            selected,
            four,
            eight,
            gain,
            HasTrainingObservations(family),
            holdout.HasEligible,
            family.DiagonalSupport,
            family.DiagonalMapSupport);
    }

    private static (IReadOnlyDictionary<int, IReadOnlyList<TerrainGraphicReference>> Emitted,
                    IReadOnlyDictionary<int, TerrainGraphicReference?> Predictors,
                    int TiedMembers) FitTopology(
        TerrainCandidateFamily family,
        IReadOnlyList<TerrainImageMemberAdmission> familyAdmissions,
        TerrainTopology topology)
    {
        var required = TerrainMasks.Required(topology);
        var index = new Dictionary<int, int>(required.Count);
        for (var i = 0; i < required.Count; i++)
        {
            index[required[i]] = i;
        }

        var memberWeights = new double[family.Members.Count][];
        for (var i = 0; i < family.Members.Count; i++)
        {
            var values = new double[required.Count];
            if (family.WeightedMasks.TryGetValue(family.Members[i], out var masks))
            {
                foreach (var entry in masks)
                {
                    var position = index[TerrainMasks.Normalize(entry.Key, topology)];
                    values[position] += entry.Value;
                    if (!double.IsFinite(values[position]))
                    {
                        throw new TerrainGenerationException(
                            TerrainGenerationError.NumericOverflow,
                            "Nonfinite mask weight while fitting terrain model.");
                    }
                }
            }

            memberWeights[i] = values;
        }

        var observedLists = new List<TerrainGraphicReference>[required.Count];
        var imageLists = new List<TerrainGraphicReference>[required.Count];
        for (var i = 0; i < required.Count; i++)
        {
            observedLists[i] = new List<TerrainGraphicReference>();
            imageLists[i] = new List<TerrainGraphicReference>();
        }

        var tiedMembers = 0;
        for (var i = 0; i < family.Members.Count; i++)
        {
            var values = memberWeights[i];
            var best = 0;
            for (var m = 1; m < values.Length; m++)
            {
                if (values[m] > values[best])
                {
                    best = m;
                }
            }

            var ties = 0;
            for (var m = 0; m < values.Length; m++)
            {
                if (values[m] == values[best])
                {
                    ties++;
                }
            }

            if (ties > 1)
            {
                tiedMembers++;
            }

            observedLists[best].Add(family.Members[i]);
        }

        foreach (var admission in familyAdmissions)
        {
            imageLists[index[TerrainMasks.Normalize(admission.Mask, topology)]].Add(admission.Reference);
        }

        var emitted = new Dictionary<int, IReadOnlyList<TerrainGraphicReference>>(required.Count);
        for (var i = 0; i < required.Count; i++)
        {
            var list = new List<TerrainGraphicReference>(observedLists[i].Count + imageLists[i].Count);
            list.AddRange(observedLists[i].OrderBy(reference => reference.Sheet).ThenBy(reference => reference.Graphic));
            list.AddRange(imageLists[i].OrderBy(reference => reference.Sheet).ThenBy(reference => reference.Graphic));
            emitted[required[i]] = list.AsReadOnly();
        }

        var predictors = new Dictionary<int, TerrainGraphicReference?>(required.Count);
        for (var i = 0; i < required.Count; i++)
        {
            var best = (TerrainGraphicReference?)null;
            var bestWeight = 0.0;
            var hasMass = false;
            for (var m = 0; m < family.Members.Count; m++)
            {
                var weight = memberWeights[m][i];
                if (weight > 0.0)
                {
                    hasMass = true;
                }

                if (best is null
                    || weight > bestWeight
                    || (weight == bestWeight && CompareReferences(family.Members[m], best.Value) < 0))
                {
                    best = family.Members[m];
                    bestWeight = weight;
                }
            }

            predictors[required[i]] = hasMass ? best : null;
        }

        return (emitted.AsReadOnly(), predictors.AsReadOnly(), tiedMembers);
    }

    private static (double FourWayAccuracy, double EightWayAccuracy, bool HasEligible) EvaluateHoldout(
        TerrainCandidateFamily family,
        TerrainCorpus corpus,
        TerrainGenerationSettings settings,
        IReadOnlyDictionary<int, TerrainGraphicReference?> fourWayPredictors,
        IReadOnlyDictionary<int, TerrainGraphicReference?> eightWayPredictors,
        ITerrainMapDataReader? mapReader)
    {
        var heldOut = new List<TerrainMapDescriptor>();
        foreach (var map in corpus.Maps)
        {
            if (TerrainHoldout.IsHeldOut(map.Identity, settings.HoldoutModulo))
            {
                heldOut.Add(map);
            }
        }

        // Contract-locked sum order: map identity, then region minimum, then placement row-major.
        heldOut.Sort((a, b) => string.CompareOrdinal(a.Identity, b.Identity));

        var reader = mapReader is null
            ? new TerrainMapBatchReader(corpus.Root)
            : new TerrainMapBatchReader(corpus.Root, mapReader);

        var fourCorrect = 0.0;
        var fourTotal = 0.0;
        var eightCorrect = 0.0;
        var eightTotal = 0.0;

        foreach (var descriptor in heldOut)
        {
            reader.VisitMap(descriptor, map =>
            {
                var mining = TerrainRegionMiner.Mine(map, family.Members);
                foreach (var region in mining.Regions)
                {
                    foreach (var placement in region.Placements)
                    {
                        var weight = placement.Weight;
                        if (!double.IsFinite(weight))
                        {
                            throw new TerrainGenerationException(
                                TerrainGenerationError.NumericOverflow,
                                "Nonfinite holdout placement weight while fitting terrain model.");
                        }

                        fourTotal += weight;
                        eightTotal += weight;
                        if (fourWayPredictors[TerrainMasks.Normalize(placement.Mask, TerrainTopology.FourWay)] == placement.Reference)
                        {
                            fourCorrect += weight;
                        }

                        if (eightWayPredictors[TerrainMasks.Normalize(placement.Mask, TerrainTopology.EightWay)] == placement.Reference)
                        {
                            eightCorrect += weight;
                        }
                    }
                }
            });
        }

        var fourAccuracy = fourTotal > 0.0 ? fourCorrect / fourTotal : 0.0;
        var eightAccuracy = eightTotal > 0.0 ? eightCorrect / eightTotal : 0.0;
        if (!double.IsFinite(fourAccuracy) || !double.IsFinite(eightAccuracy))
        {
            throw new TerrainGenerationException(
                TerrainGenerationError.NumericOverflow,
                "Nonfinite holdout accuracy while fitting terrain model.");
        }

        return (fourAccuracy, eightAccuracy, fourTotal > 0.0);
    }

    private static bool HasTrainingObservations(TerrainCandidateFamily family)
    {
        foreach (var member in family.Members)
        {
            if (family.WeightedMasks.TryGetValue(member, out var masks) && masks.Count > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static int CompareReferences(TerrainGraphicReference a, TerrainGraphicReference b)
        => a.Sheet != b.Sheet ? a.Sheet.CompareTo(b.Sheet) : a.Graphic.CompareTo(b.Graphic);
}
