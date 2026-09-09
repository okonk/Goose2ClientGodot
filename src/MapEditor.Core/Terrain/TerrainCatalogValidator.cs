using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MapEditor.Core.Terrain;

public readonly record struct TerrainValidationIssue
{
    public string Code { get; }
    public string Message { get; }
    public string? TerrainId { get; }
    public int? Mask { get; }
    public TerrainGraphicReference? Reference { get; }

    public TerrainValidationIssue(string code, string message, string? terrainId = null, int? mask = null, TerrainGraphicReference? reference = null)
    {
        Code = code ?? throw new ArgumentNullException(nameof(code));
        Message = message ?? throw new ArgumentNullException(nameof(message));
        TerrainId = terrainId;
        Mask = mask;
        Reference = reference;
    }
}

public static class TerrainCatalogValidator
{
    private static readonly Comparer<TerrainValidationIssue> IssueComparer = Comparer<TerrainValidationIssue>.Create((a, b) =>
    {
        var result = StringComparer.Ordinal.Compare(a.TerrainId, b.TerrainId);
        if (result != 0)
        {
            return result;
        }

        result = CompareNullable(a.Mask, b.Mask);
        if (result != 0)
        {
            return result;
        }

        result = CompareNullableReference(a.Reference, b.Reference);
        if (result != 0)
        {
            return result;
        }

        result = StringComparer.Ordinal.Compare(a.Code, b.Code);
        if (result != 0)
        {
            return result;
        }

        return StringComparer.Ordinal.Compare(a.Message, b.Message);
    });

    public static IReadOnlyList<TerrainValidationIssue> Validate(TerrainCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var issues = new List<TerrainValidationIssue>();
        ValidateCatalog(catalog, issues);
        issues.Sort(IssueComparer);
        for (var i = issues.Count - 1; i > 0; i--)
        {
            if (issues[i].Equals(issues[i - 1]))
            {
                issues.RemoveAt(i);
            }
        }

        return Array.AsReadOnly(issues.ToArray());
    }

    private static void ValidateCatalog(TerrainCatalog catalog, List<TerrainValidationIssue> issues)
    {
        if (catalog.SchemaVersion != TerrainCatalogJson.CurrentSchemaVersion)
        {
            issues.Add(new TerrainValidationIssue(
                "catalog-schema-version-invalid",
                $"Catalog schema version {catalog.SchemaVersion} is not supported; expected 1."));
        }

        if (IsBlank(catalog.GeneratorVersion))
        {
            issues.Add(new TerrainValidationIssue("catalog-generator-version-required", "Catalog generator version is required."));
        }

        if (!IsValidFingerprint(catalog.CorpusFingerprint))
        {
            issues.Add(new TerrainValidationIssue(
                "catalog-fingerprint-invalid",
                "Catalog corpus fingerprint must be 'sha256:' plus 64 lowercase hex digits."));
        }

        ValidateSettings(catalog.Settings, issues);

        var idCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var set in catalog.Sets)
        {
            if (!IsBlank(set.Id))
            {
                idCounts[set.Id] = idCounts.TryGetValue(set.Id, out var count) ? count + 1 : 1;
            }
        }

        var ownerIds = new Dictionary<TerrainGraphicReference, List<string>>();
        foreach (var set in catalog.Sets)
        {
            if (set.Status != TerrainReviewStatus.Enabled)
            {
                continue;
            }

            foreach (var reference in DistinctReferences(set.Members))
            {
                if (!ownerIds.TryGetValue(reference, out var owners))
                {
                    ownerIds[reference] = owners = new List<string>();
                }

                owners.Add(set.Id);
            }
        }

        foreach (var set in catalog.Sets)
        {
            ValidateSet(set, idCounts, ownerIds, issues);
        }

        foreach (var diagnostic in catalog.Diagnostics)
        {
            ValidateDiagnostic(diagnostic, null, issues);
        }
    }

    private static void ValidateSettings(TerrainGenerationSettings settings, List<TerrainValidationIssue> issues)
    {
        if (settings is null)
        {
            return;
        }

        AddSettingRangeIssue(issues, "holdoutModulo", settings.HoldoutModulo, settings.HoldoutModulo >= 2, ">= 2");
        AddSettingRangeIssue(issues, "minimumMapSupport", settings.MinimumMapSupport, settings.MinimumMapSupport >= 1, ">= 1");
        AddSettingRangeIssue(issues, "minimumRegionSupport", settings.MinimumRegionSupport, settings.MinimumRegionSupport >= 1, ">= 1");
        AddSettingRangeIssue(issues, "minimumObservationSupport", settings.MinimumObservationSupport, settings.MinimumObservationSupport >= 1, ">= 1");
        AddSettingRangeIssue(issues, "minimumDiagonalSupport", settings.MinimumDiagonalSupport, settings.MinimumDiagonalSupport >= 1, ">= 1");
        AddSettingRangeIssue(issues, "minimumEightWayAccuracyGain", settings.MinimumEightWayAccuracyGain, Within(settings.MinimumEightWayAccuracyGain, 0, 1, openLow: true), "(0,1]");
        AddSettingRangeIssue(issues, "mapMemberCompatibility", settings.MapMemberCompatibility, Within01(settings.MapMemberCompatibility), "[0,1]");
        AddSettingRangeIssue(issues, "imageOnlyCompatibility", settings.ImageOnlyCompatibility, Within01(settings.ImageOnlyCompatibility), "[0,1]");
        AddSettingRangeIssue(issues, "minimumClassificationMargin", settings.MinimumClassificationMargin, Within(settings.MinimumClassificationMargin, 0, 1, openLow: true), "(0,1]");
        AddSettingRangeIssue(issues, "maximumAmbiguity", settings.MaximumAmbiguity, Within01(settings.MaximumAmbiguity), "[0,1]");
        AddSettingRangeIssue(issues, "enabledConfidence", settings.EnabledConfidence, Within01(settings.EnabledConfidence), "[0,1]");
        AddSettingRangeIssue(issues, "pendingConfidence", settings.PendingConfidence, Within01(settings.PendingConfidence), "[0,1]");

        if (settings.ImageOnlyCompatibility < settings.MapMemberCompatibility)
        {
            issues.Add(new TerrainValidationIssue(
                "setting-relationship-invalid",
                "Setting 'imageOnlyCompatibility' must be greater than or equal to setting 'mapMemberCompatibility'."));
        }

        if (settings.PendingConfidence > settings.EnabledConfidence)
        {
            issues.Add(new TerrainValidationIssue(
                "setting-relationship-invalid",
                "Setting 'pendingConfidence' must be less than or equal to setting 'enabledConfidence'."));
        }
    }

    private static void ValidateSet(
        TerrainSetDefinition set,
        Dictionary<string, int> idCounts,
        Dictionary<TerrainGraphicReference, List<string>> ownerIds,
        List<TerrainValidationIssue> issues)
    {
        var id = set.Id;
        var idText = IsBlank(id) ? "<blank>" : id;

        if (IsBlank(id))
        {
            issues.Add(new TerrainValidationIssue("terrain-id-required", "Terrain ID is required.", id));
        }

        if (!IsBlank(id) && idCounts.TryGetValue(id, out var idCount) && idCount > 1)
        {
            issues.Add(new TerrainValidationIssue("terrain-id-duplicate", $"Terrain ID '{id}' occurs more than once.", id));
        }

        if (IsBlank(set.DisplayName))
        {
            issues.Add(new TerrainValidationIssue("terrain-display-name-required", $"Terrain '{idText}' display name is required.", id));
        }

        var topologyValid = set.Topology is TerrainTopology.FourWay or TerrainTopology.EightWay;
        if (!topologyValid)
        {
            issues.Add(new TerrainValidationIssue(
                "terrain-topology-invalid",
                $"Terrain '{idText}' topology value {(int)set.Topology} is invalid.",
                id));
        }

        var statusValid = set.Status is TerrainReviewStatus.Enabled or TerrainReviewStatus.Pending or TerrainReviewStatus.Disabled;
        if (!statusValid)
        {
            issues.Add(new TerrainValidationIssue(
                "terrain-status-invalid",
                $"Terrain '{idText}' review status value {(int)set.Status} is invalid.",
                id));
        }

        ValidateMetrics(set.Metrics, id, idText, issues);

        var members = set.Members;
        if (members.Count == 0)
        {
            issues.Add(new TerrainValidationIssue("member-required", $"Terrain '{idText}' must contain at least one member.", id));
        }

        var memberCounts = new Dictionary<TerrainGraphicReference, int>();
        foreach (var member in members)
        {
            memberCounts[member.Reference] = memberCounts.TryGetValue(member.Reference, out var count) ? count + 1 : 1;

            if (member.Provenance is not (TerrainMemberProvenance.MapObserved or TerrainMemberProvenance.ImageOnly))
            {
                issues.Add(new TerrainValidationIssue(
                    "member-provenance-invalid",
                    $"Terrain '{idText}' member {FormatReference(member.Reference)} provenance value {(int)member.Provenance} is invalid.",
                    id,
                    null,
                    member.Reference));
            }

            if (member.Reference.Graphic == 0)
            {
                issues.Add(new TerrainValidationIssue(
                    "member-graphic-zero",
                    $"Terrain '{idText}' member {FormatReference(member.Reference)} uses reserved graphic 0.",
                    id,
                    null,
                    member.Reference));
            }

            if (member.Reference.Sheet < short.MinValue || member.Reference.Sheet > short.MaxValue)
            {
                issues.Add(new TerrainValidationIssue(
                    "member-sheet-out-of-range",
                    $"Terrain '{idText}' member {FormatReference(member.Reference)} sheet is outside Int16 range.",
                    id,
                    null,
                    member.Reference));
            }
        }

        foreach (var entry in memberCounts)
        {
            if (entry.Value > 1)
            {
                issues.Add(new TerrainValidationIssue(
                    "member-duplicate",
                    $"Terrain '{idText}' contains duplicate member {FormatReference(entry.Key)}.",
                    id,
                    null,
                    entry.Key));
            }
        }

        var membersValidForIdentity = members.Count > 0 && memberCounts.Values.All(count => count == 1);
        if (topologyValid && membersValidForIdentity)
        {
            var expected = TerrainGeneratedId.Create(set.Topology, members.Select(member => member.Reference));
            if (string.CompareOrdinal(id, expected) != 0)
            {
                issues.Add(new TerrainValidationIssue(
                    "terrain-id-mismatch",
                    $"Terrain '{idText}' does not match generated ID '{expected}'.",
                    id));
            }
        }

        var maskCounts = new Dictionary<int, int>();
        var variantCountsByMask = new Dictionary<int, int>();
        var memberSet = new HashSet<TerrainGraphicReference>(members.Select(member => member.Reference));
        foreach (var mask in set.Masks)
        {
            maskCounts[mask.Mask] = maskCounts.TryGetValue(mask.Mask, out var maskCount) ? maskCount + 1 : 1;
            variantCountsByMask[mask.Mask] = variantCountsByMask.TryGetValue(mask.Mask, out var variantCount)
                ? variantCount + mask.Variants.Count
                : mask.Variants.Count;

            if (topologyValid && !TerrainMasks.IsReachable(mask.Mask, set.Topology))
            {
                issues.Add(new TerrainValidationIssue(
                    "mask-unreachable",
                    $"Terrain '{idText}' mask {FormatMask(mask.Mask)} is not reachable for {TopologyWire(set.Topology)}.",
                    id,
                    mask.Mask));
            }

            var variantCounts = new Dictionary<TerrainGraphicReference, int>();
            foreach (var variant in mask.Variants)
            {
                variantCounts[variant] = variantCounts.TryGetValue(variant, out var variantOccurrences) ? variantOccurrences + 1 : 1;

                if (!memberSet.Contains(variant))
                {
                    issues.Add(new TerrainValidationIssue(
                        "variant-not-member",
                        $"Terrain '{idText}' mask {FormatMask(mask.Mask)} variant {FormatReference(variant)} is not a member.",
                        id,
                        mask.Mask,
                        variant));
                }
            }

            foreach (var entry in variantCounts)
            {
                if (entry.Value > 1)
                {
                    issues.Add(new TerrainValidationIssue(
                        "variant-duplicate",
                        $"Terrain '{idText}' mask {FormatMask(mask.Mask)} contains duplicate variant {FormatReference(entry.Key)}.",
                        id,
                        mask.Mask,
                        entry.Key));
                }
            }
        }

        foreach (var entry in maskCounts)
        {
            if (entry.Value > 1)
            {
                issues.Add(new TerrainValidationIssue(
                    "mask-duplicate",
                    $"Terrain '{idText}' contains duplicate mask {FormatMask(entry.Key)}.",
                    id,
                    entry.Key));
            }
        }

        if (set.Status == TerrainReviewStatus.Enabled)
        {
            var used = new HashSet<TerrainGraphicReference>();
            foreach (var mask in set.Masks)
            {
                foreach (var variant in mask.Variants)
                {
                    used.Add(variant);
                }
            }

            foreach (var reference in DistinctReferences(members))
            {
                if (!used.Contains(reference))
                {
                    issues.Add(new TerrainValidationIssue(
                        "enabled-member-unused",
                        $"Enabled terrain '{idText}' member {FormatReference(reference)} is unused.",
                        id,
                        null,
                        reference));
                }
            }

            if (topologyValid)
            {
                foreach (var required in TerrainMasks.Required(set.Topology))
                {
                    if (!maskCounts.ContainsKey(required))
                    {
                        issues.Add(new TerrainValidationIssue(
                            "enabled-mask-missing",
                            $"Enabled terrain '{idText}' is missing mask {FormatMask(required)}.",
                            id,
                            required));
                    }
                    else if (variantCountsByMask[required] == 0)
                    {
                        issues.Add(new TerrainValidationIssue(
                            "enabled-mask-empty",
                            $"Enabled terrain '{idText}' mask {FormatMask(required)} has no variants.",
                            id,
                            required));
                    }
                }
            }

            foreach (var reference in DistinctReferences(members))
            {
                if (ownerIds.TryGetValue(reference, out var owners) && owners.Count >= 2)
                {
                    var quoted = string.Join(
                        ", ",
                        owners.OrderBy(value => value, StringComparer.Ordinal).Select(value => $"'{value}'"));
                    issues.Add(new TerrainValidationIssue(
                        "enabled-member-conflict",
                        $"Enabled terrain '{idText}' member {FormatReference(reference)} is shared by [{quoted}].",
                        id,
                        null,
                        reference));
                }
            }
        }

        foreach (var diagnostic in set.Diagnostics)
        {
            ValidateDiagnostic(diagnostic, id, issues);
        }
    }

    private static void ValidateMetrics(TerrainSetMetrics metrics, string id, string idText, List<TerrainValidationIssue> issues)
    {
        if (metrics is null)
        {
            return;
        }

        AddMetricRangeIssue(issues, id, idText, "mapSupport", metrics.MapSupport, metrics.MapSupport >= 0, ">= 0");
        AddMetricRangeIssue(issues, id, idText, "regionSupport", metrics.RegionSupport, metrics.RegionSupport >= 0, ">= 0");
        AddMetricRangeIssue(issues, id, idText, "observationSupport", metrics.ObservationSupport, metrics.ObservationSupport >= 0, ">= 0");
        AddMetricRangeIssue(issues, id, idText, "diagonalSupport", metrics.DiagonalSupport, metrics.DiagonalSupport >= 0, ">= 0");
        AddMetricRangeIssue(issues, id, idText, "maskEntropy", metrics.MaskEntropy, Within01(metrics.MaskEntropy), "[0,1]");
        AddMetricRangeIssue(issues, id, idText, "completeness", metrics.Completeness, Within01(metrics.Completeness), "[0,1]");
        AddMetricRangeIssue(issues, id, idText, "ambiguity", metrics.Ambiguity, Within01(metrics.Ambiguity), "[0,1]");
        AddMetricRangeIssue(issues, id, idText, "visualCompatibility", metrics.VisualCompatibility, Within01(metrics.VisualCompatibility), "[0,1]");
        AddMetricRangeIssue(issues, id, idText, "holdoutAccuracy", metrics.HoldoutAccuracy, Within01(metrics.HoldoutAccuracy), "[0,1]");
        AddMetricRangeIssue(issues, id, idText, "eightWayAccuracyGain", metrics.EightWayAccuracyGain, metrics.EightWayAccuracyGain >= -1 && metrics.EightWayAccuracyGain <= 1, "[-1,1]");
        AddMetricRangeIssue(issues, id, idText, "confidence", metrics.Confidence, Within01(metrics.Confidence), "[0,1]");
    }

    private static void ValidateDiagnostic(TerrainDiagnostic diagnostic, string? terrainId, List<TerrainValidationIssue> issues)
    {
        if (IsBlank(diagnostic.Code))
        {
            issues.Add(new TerrainValidationIssue("diagnostic-code-required", "Diagnostic code is required.", terrainId));
        }

        if (IsBlank(diagnostic.Message))
        {
            issues.Add(new TerrainValidationIssue("diagnostic-message-required", "Diagnostic message is required.", terrainId));
        }
    }

    private static void AddSettingRangeIssue(List<TerrainValidationIssue> issues, string name, double value, bool valid, string range)
    {
        if (!valid)
        {
            issues.Add(new TerrainValidationIssue(
                "setting-out-of-range",
                $"Setting '{name}' value {FormatValue(value)} is outside {range}."));
        }
    }

    private static void AddMetricRangeIssue(
        List<TerrainValidationIssue> issues,
        string id,
        string idText,
        string name,
        double value,
        bool valid,
        string range)
    {
        if (!valid)
        {
            issues.Add(new TerrainValidationIssue(
                "metric-out-of-range",
                $"Terrain '{idText}' metric '{name}' value {FormatValue(value)} is outside {range}.",
                id));
        }
    }

    private static IEnumerable<TerrainGraphicReference> DistinctReferences(IReadOnlyList<TerrainMemberDefinition> members) =>
        members.Select(member => member.Reference).Distinct();

    private static bool IsBlank(string? value) => string.IsNullOrWhiteSpace(value);

    private static bool IsValidFingerprint(string? fingerprint)
    {
        if (fingerprint is null || fingerprint.Length != 71 || !fingerprint.StartsWith("sha256:", StringComparison.Ordinal))
        {
            return false;
        }

        for (var i = 7; i < fingerprint.Length; i++)
        {
            var c = fingerprint[i];
            if ((c < '0' || c > '9') && (c < 'a' || c > 'f'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Within01(double value) => !double.IsNaN(value) && value >= 0 && value <= 1;

    private static bool Within(double value, double low, double high, bool openLow) =>
        !double.IsNaN(value) && (openLow ? value > low : value >= low) && value <= high;

    private static string FormatValue(double value) => value.ToString("G17", CultureInfo.InvariantCulture);

    private static string FormatReference(TerrainGraphicReference reference) =>
        $"({reference.Sheet},{reference.Graphic})";

    private static string FormatMask(int mask) => "0x" + mask.ToString("X2", CultureInfo.InvariantCulture);

    private static string TopologyWire(TerrainTopology topology) => topology switch
    {
        TerrainTopology.FourWay => "four-way",
        TerrainTopology.EightWay => "eight-way",
        _ => ((int)topology).ToString(CultureInfo.InvariantCulture)
    };

    private static int CompareNullable(int? a, int? b)
    {
        if (a == b)
        {
            return 0;
        }

        if (a is null)
        {
            return -1;
        }

        if (b is null)
        {
            return 1;
        }

        return a.Value.CompareTo(b.Value);
    }

    private static int CompareNullableReference(TerrainGraphicReference? a, TerrainGraphicReference? b)
    {
        if (a == b)
        {
            return 0;
        }

        if (a is null)
        {
            return -1;
        }

        if (b is null)
        {
            return 1;
        }

        var result = a.Value.Sheet.CompareTo(b.Value.Sheet);
        if (result != 0)
        {
            return result;
        }

        return a.Value.Graphic.CompareTo(b.Value.Graphic);
    }
}
