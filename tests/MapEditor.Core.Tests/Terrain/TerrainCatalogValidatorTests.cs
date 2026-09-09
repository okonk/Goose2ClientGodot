using System;
using System.Collections.Generic;
using System.Linq;
using MapEditor.Core.Terrain;
using Xunit;

namespace MapEditor.Core.Tests.Terrain;

public class TerrainCatalogValidatorTests
{
    private static readonly string Fingerprint =
        $"sha256:{new string('a', 64)}";

    private static TerrainGenerationSettings ValidSettings() => new(
        2, 1, 1, 1, 1, 0.1, 0.5, 0.5, 0.1, 0.5, 0.9, 0.9);

    private static TerrainSetMetrics ValidMetrics() => new(1, 1, 1, 0, 0.5, 0.5, 0.5, 0.5, 0.5, 0.0, 0.5);

    private static void AssertIssues(
        IReadOnlyList<TerrainValidationIssue> actual,
        params (string Code, string Message, string? Id, int? Mask, TerrainGraphicReference? Reference)[] expected)
    {
        var actualTuples = actual
            .Select(issue => (issue.Code, issue.Message, issue.TerrainId, issue.Mask, issue.Reference))
            .ToArray();

        Assert.Equal(expected, actualTuples);
    }

    [Fact]
    public void Validate_ProgrammaticSchemaEnumAndEmptyMemberErrorsReturnExactIssuesWithoutThrowing()
    {
        var settings = new TerrainGenerationSettings(
            HoldoutModulo: 1,
            MinimumMapSupport: 2,
            MinimumRegionSupport: 1,
            MinimumObservationSupport: 1,
            MinimumDiagonalSupport: 1,
            MinimumEightWayAccuracyGain: 0.1,
            MapMemberCompatibility: 0.5,
            ImageOnlyCompatibility: 0.2,
            MinimumClassificationMargin: 0.1,
            MaximumAmbiguity: 0.25,
            EnabledConfidence: 0.5,
            PendingConfidence: 0.9);

        var blankSet = new TerrainSetDefinition(
            "",
            "",
            (TerrainReviewStatus)99,
            (TerrainTopology)99,
            new TerrainSetMetrics(-1, 1, 1, 0, 2.0, 0.5, 0.5, 0.5, 0.5, 0.0, 0.5),
            Array.Empty<TerrainMaskDefinition>(),
            Array.Empty<TerrainMemberDefinition>(),
            new[] { new TerrainDiagnostic("", "") });

        var memberSet = new TerrainSetDefinition(
            "terrain-bad",
            "X",
            TerrainReviewStatus.Disabled,
            TerrainTopology.FourWay,
            ValidMetrics(),
            Array.Empty<TerrainMaskDefinition>(),
            new[]
            {
                new TerrainMemberDefinition(new TerrainGraphicReference(1, 0), TerrainMemberProvenance.MapObserved),
                new TerrainMemberDefinition(new TerrainGraphicReference(32768, 5), TerrainMemberProvenance.MapObserved),
                new TerrainMemberDefinition(new TerrainGraphicReference(1, 7), TerrainMemberProvenance.MapObserved),
                new TerrainMemberDefinition(new TerrainGraphicReference(1, 7), (TerrainMemberProvenance)99)
            },
            Array.Empty<TerrainDiagnostic>());

        var catalog = new TerrainCatalog(
            2,
            "",
            "sha256:XYZ",
            settings,
            new[] { blankSet, memberSet },
            new[] { new TerrainDiagnostic("", "") });

        var issues = TerrainCatalogValidator.Validate(catalog);

        AssertIssues(
            issues,
            ("catalog-fingerprint-invalid", "Catalog corpus fingerprint must be 'sha256:' plus 64 lowercase hex digits.", null, null, null),
            ("catalog-generator-version-required", "Catalog generator version is required.", null, null, null),
            ("catalog-schema-version-invalid", "Catalog schema version 2 is not supported; expected 1.", null, null, null),
            ("diagnostic-code-required", "Diagnostic code is required.", null, null, null),
            ("diagnostic-message-required", "Diagnostic message is required.", null, null, null),
            ("setting-out-of-range", "Setting 'holdoutModulo' value 1 is outside >= 2.", null, null, null),
            ("setting-relationship-invalid", "Setting 'imageOnlyCompatibility' must be greater than or equal to setting 'mapMemberCompatibility'.", null, null, null),
            ("setting-relationship-invalid", "Setting 'pendingConfidence' must be less than or equal to setting 'enabledConfidence'.", null, null, null),
            ("diagnostic-code-required", "Diagnostic code is required.", "", null, null),
            ("diagnostic-message-required", "Diagnostic message is required.", "", null, null),
            ("member-required", "Terrain '<blank>' must contain at least one member.", "", null, null),
            ("metric-out-of-range", "Terrain '<blank>' metric 'mapSupport' value -1 is outside >= 0.", "", null, null),
            ("metric-out-of-range", "Terrain '<blank>' metric 'maskEntropy' value 2 is outside [0,1].", "", null, null),
            ("terrain-display-name-required", "Terrain '<blank>' display name is required.", "", null, null),
            ("terrain-id-required", "Terrain ID is required.", "", null, null),
            ("terrain-status-invalid", "Terrain '<blank>' review status value 99 is invalid.", "", null, null),
            ("terrain-topology-invalid", "Terrain '<blank>' topology value 99 is invalid.", "", null, null),
            ("member-graphic-zero", "Terrain 'terrain-bad' member (1,0) uses reserved graphic 0.", "terrain-bad", null, new TerrainGraphicReference(1, 0)),
            ("member-duplicate", "Terrain 'terrain-bad' contains duplicate member (1,7).", "terrain-bad", null, new TerrainGraphicReference(1, 7)),
            ("member-provenance-invalid", "Terrain 'terrain-bad' member (1,7) provenance value 99 is invalid.", "terrain-bad", null, new TerrainGraphicReference(1, 7)),
            ("member-sheet-out-of-range", "Terrain 'terrain-bad' member (32768,5) sheet is outside Int16 range.", "terrain-bad", null, new TerrainGraphicReference(32768, 5)));
    }

    [Fact]
    public void Validate_IncompleteOverlappingPendingAndDisabledAllowsReviewData()
    {
        var pendingMembers = new[]
        {
            new TerrainMemberDefinition(new TerrainGraphicReference(1, 1), TerrainMemberProvenance.MapObserved),
            new TerrainMemberDefinition(new TerrainGraphicReference(1, 2), TerrainMemberProvenance.MapObserved)
        };

        var pending = new TerrainSetDefinition(
            TerrainGeneratedId.Create(TerrainTopology.FourWay, pendingMembers.Select(member => member.Reference)),
            "P",
            TerrainReviewStatus.Pending,
            TerrainTopology.FourWay,
            ValidMetrics(),
            new[] { new TerrainMaskDefinition(0, Array.Empty<TerrainGraphicReference>()) },
            pendingMembers,
            Array.Empty<TerrainDiagnostic>());

        var disabledMembers = new[]
        {
            new TerrainMemberDefinition(new TerrainGraphicReference(1, 1), TerrainMemberProvenance.MapObserved),
            new TerrainMemberDefinition(new TerrainGraphicReference(2, 2), TerrainMemberProvenance.ImageOnly)
        };

        var disabled = new TerrainSetDefinition(
            TerrainGeneratedId.Create(TerrainTopology.EightWay, disabledMembers.Select(member => member.Reference)),
            "D",
            TerrainReviewStatus.Disabled,
            TerrainTopology.EightWay,
            ValidMetrics(),
            new[] { new TerrainMaskDefinition(0, Array.Empty<TerrainGraphicReference>()) },
            disabledMembers,
            Array.Empty<TerrainDiagnostic>());

        var catalog = new TerrainCatalog(
            TerrainCatalogJson.CurrentSchemaVersion,
            "gen/1.0",
            Fingerprint,
            ValidSettings(),
            new[] { pending, disabled },
            Array.Empty<TerrainDiagnostic>());

        Assert.Empty(TerrainCatalogValidator.Validate(catalog));
    }

    [Fact]
    public void Validate_IncompleteOrOverlappingEnabledReturnsExactOrderedIssues()
    {
        var e1Members = new[]
        {
            new TerrainMemberDefinition(new TerrainGraphicReference(1, 1), TerrainMemberProvenance.MapObserved),
            new TerrainMemberDefinition(new TerrainGraphicReference(1, 2), TerrainMemberProvenance.MapObserved),
            new TerrainMemberDefinition(new TerrainGraphicReference(1, 3), TerrainMemberProvenance.MapObserved)
        };

        var idE1 = TerrainGeneratedId.Create(TerrainTopology.FourWay, e1Members.Select(member => member.Reference));

        var e1 = new TerrainSetDefinition(
            idE1,
            "E1",
            TerrainReviewStatus.Enabled,
            TerrainTopology.FourWay,
            ValidMetrics(),
            new[]
            {
                new TerrainMaskDefinition(0, new[] { new TerrainGraphicReference(1, 1) }),
                new TerrainMaskDefinition(1, Array.Empty<TerrainGraphicReference>())
            },
            e1Members,
            Array.Empty<TerrainDiagnostic>());

        var e2Members = new[]
        {
            new TerrainMemberDefinition(new TerrainGraphicReference(1, 1), TerrainMemberProvenance.MapObserved),
            new TerrainMemberDefinition(new TerrainGraphicReference(1, 4), TerrainMemberProvenance.MapObserved)
        };

        var idE2 = TerrainGeneratedId.Create(TerrainTopology.FourWay, e2Members.Select(member => member.Reference));

        var e2 = new TerrainSetDefinition(
            idE2,
            "E2",
            TerrainReviewStatus.Enabled,
            TerrainTopology.FourWay,
            ValidMetrics(),
            new[] { new TerrainMaskDefinition(0, new[] { new TerrainGraphicReference(1, 1) }) },
            e2Members,
            Array.Empty<TerrainDiagnostic>());

        var duplicate = new TerrainSetDefinition(
            idE1,
            "Dup",
            TerrainReviewStatus.Disabled,
            TerrainTopology.FourWay,
            ValidMetrics(),
            Array.Empty<TerrainMaskDefinition>(),
            e1Members,
            Array.Empty<TerrainDiagnostic>());

        var catalog = new TerrainCatalog(
            TerrainCatalogJson.CurrentSchemaVersion,
            "gen/1.0",
            Fingerprint,
            ValidSettings(),
            new[] { e1, e2, duplicate },
            Array.Empty<TerrainDiagnostic>());

        var (minId, maxId) = string.CompareOrdinal(idE1, idE2) < 0 ? (idE1, idE2) : (idE2, idE1);
        var shared = $"['{minId}', '{maxId}']";

        var expected = new List<(string, string, string?, int?, TerrainGraphicReference?)>
        {
            ("terrain-id-duplicate", $"Terrain ID '{idE1}' occurs more than once.", idE1, null, null)
        };

        foreach (var (setId, isE1) in new[] { (setId: idE1, isE1: true), (setId: idE2, isE1: false) }.OrderBy(entry => entry.setId, StringComparer.Ordinal))
        {
            expected.Add(("enabled-member-conflict", $"Enabled terrain '{setId}' member (1,1) is shared by {shared}.", setId, null, new TerrainGraphicReference(1, 1)));

            if (isE1)
            {
                expected.Add(("enabled-member-unused", $"Enabled terrain '{setId}' member (1,2) is unused.", setId, null, new TerrainGraphicReference(1, 2)));
                expected.Add(("enabled-member-unused", $"Enabled terrain '{setId}' member (1,3) is unused.", setId, null, new TerrainGraphicReference(1, 3)));
                expected.Add(("enabled-mask-empty", $"Enabled terrain '{setId}' mask 0x01 has no variants.", setId, 1, null));
                for (var mask = 2; mask < 16; mask++)
                {
                    expected.Add(("enabled-mask-missing", $"Enabled terrain '{setId}' is missing mask 0x{mask:X2}.", setId, mask, null));
                }
            }
            else
            {
                expected.Add(("enabled-member-unused", $"Enabled terrain '{setId}' member (1,4) is unused.", setId, null, new TerrainGraphicReference(1, 4)));
                for (var mask = 1; mask < 16; mask++)
                {
                    expected.Add(("enabled-mask-missing", $"Enabled terrain '{setId}' is missing mask 0x{mask:X2}.", setId, mask, null));
                }
            }
        }

        AssertIssues(TerrainCatalogValidator.Validate(catalog), expected.ToArray());
    }

    [Fact]
    public void Validate_IdMismatchDuplicateMaskAndDuplicateVariantReturnExactIssues()
    {
        var members = new[]
        {
            new TerrainMemberDefinition(new TerrainGraphicReference(1, 1), TerrainMemberProvenance.MapObserved),
            new TerrainMemberDefinition(new TerrainGraphicReference(1, 2), TerrainMemberProvenance.MapObserved)
        };

        var expectedId = TerrainGeneratedId.Create(TerrainTopology.FourWay, members.Select(member => member.Reference));

        var set = new TerrainSetDefinition(
            "terrain-wrong",
            "A",
            TerrainReviewStatus.Pending,
            TerrainTopology.FourWay,
            ValidMetrics(),
            new[]
            {
                new TerrainMaskDefinition(0, new[] { new TerrainGraphicReference(1, 1), new TerrainGraphicReference(1, 1) }),
                new TerrainMaskDefinition(0, new[] { new TerrainGraphicReference(1, 2) })
            },
            members,
            Array.Empty<TerrainDiagnostic>());

        var catalog = new TerrainCatalog(
            TerrainCatalogJson.CurrentSchemaVersion,
            "gen/1.0",
            Fingerprint,
            ValidSettings(),
            new[] { set },
            Array.Empty<TerrainDiagnostic>());

        AssertIssues(
            TerrainCatalogValidator.Validate(catalog),
            ("terrain-id-mismatch", $"Terrain 'terrain-wrong' does not match generated ID '{expectedId}'.", "terrain-wrong", null, null),
            ("mask-duplicate", "Terrain 'terrain-wrong' contains duplicate mask 0x00.", "terrain-wrong", 0, null),
            ("variant-duplicate", "Terrain 'terrain-wrong' mask 0x00 contains duplicate variant (1,1).", "terrain-wrong", 0, new TerrainGraphicReference(1, 1)));
    }

    [Fact]
    public void Validate_VariantNotInMembersAndUnreachableDiagonalAreNotSilentlyAccepted()
    {
        var members = new[]
        {
            new TerrainMemberDefinition(new TerrainGraphicReference(1, 1), TerrainMemberProvenance.MapObserved),
            new TerrainMemberDefinition(new TerrainGraphicReference(1, 2), TerrainMemberProvenance.MapObserved)
        };

        var id = TerrainGeneratedId.Create(TerrainTopology.EightWay, members.Select(member => member.Reference));

        var set = new TerrainSetDefinition(
            id,
            "A",
            TerrainReviewStatus.Pending,
            TerrainTopology.EightWay,
            ValidMetrics(),
            new[]
            {
                new TerrainMaskDefinition(16, new[] { new TerrainGraphicReference(1, 1) }),
                new TerrainMaskDefinition(3, new[] { new TerrainGraphicReference(1, 2), new TerrainGraphicReference(1, 9) })
            },
            members,
            Array.Empty<TerrainDiagnostic>());

        var catalog = new TerrainCatalog(
            TerrainCatalogJson.CurrentSchemaVersion,
            "gen/1.0",
            Fingerprint,
            ValidSettings(),
            new[] { set },
            Array.Empty<TerrainDiagnostic>());

        AssertIssues(
            TerrainCatalogValidator.Validate(catalog),
            ("variant-not-member", $"Terrain '{id}' mask 0x03 variant (1,9) is not a member.", id, 3, new TerrainGraphicReference(1, 9)),
            ("mask-unreachable", $"Terrain '{id}' mask 0x10 is not reachable for eight-way.", id, 16, null));
    }
}
