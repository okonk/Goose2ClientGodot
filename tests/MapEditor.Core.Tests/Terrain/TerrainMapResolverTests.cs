using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MapEditor.Core.Terrain;
using Xunit;

namespace MapEditor.Core.Tests.Terrain;

public class TerrainMapResolverTests
{
    private static TerrainMemberDefinition[] Members() =>
    [
        TerrainCatalogFixture.CreateMember(1, 1),
        TerrainCatalogFixture.CreateMember(1, 2)
    ];

    private static TerrainRuntimeSet GetTerrain(TerrainMapResolver resolver, string id)
    {
        Assert.True(resolver.TryGetEnabledTerrain(id, out var terrain));
        return terrain;
    }

    private static IReadOnlyList<TerrainMaskDefinition> FourWayMasksWithZero(
        IEnumerable<TerrainGraphicReference> zeroVariants,
        params TerrainMemberDefinition[] members)
    {
        var masks = TerrainCatalogFixture.CreateFullMasks(TerrainTopology.FourWay, members).ToList();
        masks[0] = new TerrainMaskDefinition(0, zeroVariants);
        return masks;
    }

    [Fact]
    public void Constructor_NullCatalogThrowsExactArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => new TerrainMapResolver(null!));
        Assert.Equal("catalog", ex.ParamName);
    }

    [Fact]
    public void Constructor_IndexesOnlyEnabledSetsWithOrdinalIds()
    {
        var members = Members();
        var enabled = TerrainCatalogFixture.CreateSet(TerrainTopology.FourWay, members);
        var pending = TerrainCatalogFixture.CreateSet(TerrainTopology.FourWay, members, TerrainReviewStatus.Pending);
        var disabled = TerrainCatalogFixture.CreateSet(
            TerrainTopology.EightWay,
            [TerrainCatalogFixture.CreateMember(2, 1)],
            TerrainReviewStatus.Disabled);

        Assert.Equal(enabled.Id, pending.Id);

        var resolver = new TerrainMapResolver(TerrainCatalogFixture.CreateCatalog(enabled, pending, disabled));

        Assert.True(resolver.TryGetEnabledTerrain(enabled.Id, out var terrain));
        Assert.Equal(enabled.Id, terrain.Id);
        Assert.Equal(TerrainTopology.FourWay, terrain.Topology);
        Assert.False(resolver.TryGetEnabledTerrain(enabled.Id.ToUpperInvariant(), out _));
        Assert.False(resolver.TryGetEnabledTerrain(disabled.Id, out _));
        Assert.False(resolver.TryGetEnabledTerrain(null!, out _));

        Assert.True(resolver.TryGetOwner(new TerrainGraphicReference(1, 1), out var owner));
        Assert.Same(terrain, owner);
        Assert.False(resolver.TryGetOwner(new TerrainGraphicReference(2, 1), out _));
    }

    [Fact]
    public void Constructor_PendingDisabledAndTheirOverlapsOrDefectsDoNotParticipate()
    {
        var members = Members();
        var enabled = TerrainCatalogFixture.CreateSet(TerrainTopology.FourWay, members);

        var pending = TerrainCatalogFixture.CreateSet(
            TerrainTopology.FourWay,
            members,
            TerrainReviewStatus.Pending,
            metrics: new TerrainSetMetrics(-1, 1, 1, 0, 2.0, 0.5, 0.5, 0.5, 0.5, 0.0, 0.5),
            masks: [new TerrainMaskDefinition(0, Array.Empty<TerrainGraphicReference>())]);

        var disabled = TerrainCatalogFixture.CreateSet(
            TerrainTopology.FourWay,
            [
                TerrainCatalogFixture.CreateMember(1, 0),
                TerrainCatalogFixture.CreateMember(1, 2, (TerrainMemberProvenance)99)
            ],
            TerrainReviewStatus.Disabled,
            displayName: " ");

        var resolver = new TerrainMapResolver(TerrainCatalogFixture.CreateCatalog(enabled, pending, disabled));

        Assert.True(resolver.TryGetEnabledTerrain(enabled.Id, out var terrain));
        Assert.True(resolver.TryGetOwner(new TerrainGraphicReference(1, 1), out var owner));
        Assert.Same(terrain, owner);
        Assert.False(resolver.TryGetOwner(new TerrainGraphicReference(1, 0), out _));
    }

    [Theory]
    [InlineData("invalid-topology")]
    [InlineData("invalid-status")]
    [InlineData("blank-id")]
    [InlineData("duplicate-enabled-id")]
    [InlineData("id-mismatch")]
    [InlineData("blank-display-name")]
    [InlineData("invalid-metric")]
    [InlineData("invalid-diagnostic")]
    [InlineData("empty-members")]
    [InlineData("member-graphic-zero")]
    [InlineData("member-sheet-out-of-range")]
    [InlineData("duplicate-member")]
    [InlineData("member-provenance-invalid")]
    [InlineData("duplicate-mask")]
    [InlineData("unreachable-mask")]
    [InlineData("duplicate-variant")]
    [InlineData("variant-not-member")]
    [InlineData("variant-graphic-zero")]
    [InlineData("unused-member")]
    [InlineData("cross-enabled-membership")]
    public void Constructor_EachEnabledRuntimeDefectThrowsFirstExactIssue(string defect)
    {
        var members = Members();
        var id = TerrainCatalogFixture.CreateId(TerrainTopology.FourWay, members);

        TerrainCatalog catalog;
        (string Code, string Message) expected;

        switch (defect)
        {
            case "invalid-topology":
                catalog = TerrainCatalogFixture.CreateCatalog(TerrainCatalogFixture.CreateSet(
                    (TerrainTopology)99,
                    members,
                    id: "terrain-t",
                    masks: Array.Empty<TerrainMaskDefinition>()));
                expected = ("terrain-topology-invalid", "Terrain 'terrain-t' topology value 99 is invalid.");
                break;
            case "invalid-status":
                catalog = TerrainCatalogFixture.CreateCatalog(TerrainCatalogFixture.CreateSet(
                    TerrainTopology.FourWay,
                    members,
                    (TerrainReviewStatus)99));
                expected = ("terrain-status-invalid", $"Terrain '{id}' review status value 99 is invalid.");
                break;
            case "blank-id":
                catalog = TerrainCatalogFixture.CreateCatalog(TerrainCatalogFixture.CreateSet(
                    TerrainTopology.FourWay,
                    members,
                    id: ""));
                expected = ("terrain-id-mismatch", $"Terrain '<blank>' does not match generated ID '{id}'.");
                break;
            case "duplicate-enabled-id":
                catalog = TerrainCatalogFixture.CreateCatalog(
                    TerrainCatalogFixture.CreateSet(TerrainTopology.FourWay, members, displayName: "A"),
                    TerrainCatalogFixture.CreateSet(TerrainTopology.FourWay, members, displayName: "B"));
                expected = ("terrain-id-duplicate", $"Terrain ID '{id}' occurs more than once.");
                break;
            case "id-mismatch":
                catalog = TerrainCatalogFixture.CreateCatalog(TerrainCatalogFixture.CreateSet(
                    TerrainTopology.FourWay,
                    members,
                    id: "terrain-wrong"));
                expected = ("terrain-id-mismatch", $"Terrain 'terrain-wrong' does not match generated ID '{id}'.");
                break;
            case "blank-display-name":
                catalog = TerrainCatalogFixture.CreateCatalog(TerrainCatalogFixture.CreateSet(
                    TerrainTopology.FourWay,
                    members,
                    displayName: ""));
                expected = ("terrain-display-name-required", $"Terrain '{id}' display name is required.");
                break;
            case "invalid-metric":
                catalog = TerrainCatalogFixture.CreateCatalog(TerrainCatalogFixture.CreateSet(
                    TerrainTopology.FourWay,
                    members,
                    metrics: new TerrainSetMetrics(-1, 1, 1, 0, 0.5, 0.5, 0.5, 0.5, 0.5, 0.0, 0.5)));
                expected = ("metric-out-of-range", $"Terrain '{id}' metric 'mapSupport' value -1 is outside >= 0.");
                break;
            case "invalid-diagnostic":
                catalog = TerrainCatalogFixture.CreateCatalog(TerrainCatalogFixture.CreateSet(
                    TerrainTopology.FourWay,
                    members,
                    diagnostics: [new TerrainDiagnostic("", "note")]));
                expected = ("diagnostic-code-required", "Diagnostic code is required.");
                break;
            case "empty-members":
                catalog = TerrainCatalogFixture.CreateCatalog(TerrainCatalogFixture.CreateSet(
                    TerrainTopology.FourWay,
                    Array.Empty<TerrainMemberDefinition>(),
                    id: "terrain-e"));
                expected = ("member-required", "Terrain 'terrain-e' must contain at least one member.");
                break;
            case "member-graphic-zero":
            {
                TerrainMemberDefinition[] zeroMembers =
                [
                    TerrainCatalogFixture.CreateMember(0, 0),
                    TerrainCatalogFixture.CreateMember(1, 2)
                ];
                catalog = TerrainCatalogFixture.CreateCatalog(TerrainCatalogFixture.CreateSet(TerrainTopology.FourWay, zeroMembers));
                expected = ("member-graphic-zero",
                    $"Terrain '{TerrainCatalogFixture.CreateId(TerrainTopology.FourWay, zeroMembers)}' member (0,0) uses reserved graphic 0.");
                break;
            }
            case "member-sheet-out-of-range":
            {
                TerrainMemberDefinition[] sheetMembers =
                [
                    TerrainCatalogFixture.CreateMember(32768, 5),
                    TerrainCatalogFixture.CreateMember(1, 2)
                ];
                catalog = TerrainCatalogFixture.CreateCatalog(TerrainCatalogFixture.CreateSet(TerrainTopology.FourWay, sheetMembers));
                expected = ("member-sheet-out-of-range",
                    $"Terrain '{TerrainCatalogFixture.CreateId(TerrainTopology.FourWay, sheetMembers)}' member (32768,5) sheet is outside Int16 range.");
                break;
            }
            case "duplicate-member":
                catalog = TerrainCatalogFixture.CreateCatalog(TerrainCatalogFixture.CreateSet(
                    TerrainTopology.FourWay,
                    [
                        TerrainCatalogFixture.CreateMember(1, 1),
                        TerrainCatalogFixture.CreateMember(1, 1),
                        TerrainCatalogFixture.CreateMember(1, 2)
                    ],
                    id: "terrain-dup",
                    masks: TerrainCatalogFixture.CreateFullMasks(
                        TerrainTopology.FourWay,
                        [TerrainCatalogFixture.CreateMember(1, 1), TerrainCatalogFixture.CreateMember(1, 2)])));
                expected = ("member-duplicate", "Terrain 'terrain-dup' contains duplicate member (1,1).");
                break;
            case "member-provenance-invalid":
            {
                TerrainMemberDefinition[] provenanceMembers =
                [
                    TerrainCatalogFixture.CreateMember(1, 1),
                    TerrainCatalogFixture.CreateMember(1, 2, (TerrainMemberProvenance)99)
                ];
                catalog = TerrainCatalogFixture.CreateCatalog(TerrainCatalogFixture.CreateSet(TerrainTopology.FourWay, provenanceMembers));
                expected = ("member-provenance-invalid",
                    $"Terrain '{TerrainCatalogFixture.CreateId(TerrainTopology.FourWay, provenanceMembers)}' member (1,2) provenance value 99 is invalid.");
                break;
            }
            case "duplicate-mask":
                catalog = TerrainCatalogFixture.CreateCatalog(TerrainCatalogFixture.CreateSet(
                    TerrainTopology.FourWay,
                    members,
                    masks: TerrainCatalogFixture.CreateFullMasks(TerrainTopology.FourWay, members)
                        .Append(new TerrainMaskDefinition(0, members.Select(member => member.Reference)))));
                expected = ("mask-duplicate", $"Terrain '{id}' contains duplicate mask 0x00.");
                break;
            case "unreachable-mask":
            {
                var eightMasks = TerrainCatalogFixture.CreateFullMasks(TerrainTopology.EightWay, members)
                    .Append(new TerrainMaskDefinition(16, [new TerrainGraphicReference(1, 1)]));
                catalog = TerrainCatalogFixture.CreateCatalog(TerrainCatalogFixture.CreateSet(TerrainTopology.EightWay, members, masks: eightMasks));
                expected = ("mask-unreachable",
                    $"Terrain '{TerrainCatalogFixture.CreateId(TerrainTopology.EightWay, members)}' mask 0x10 is not reachable for eight-way.");
                break;
            }
            case "duplicate-variant":
                catalog = TerrainCatalogFixture.CreateCatalog(TerrainCatalogFixture.CreateSet(
                    TerrainTopology.FourWay,
                    members,
                    masks: FourWayMasksWithZero(
                        [new TerrainGraphicReference(1, 1), new TerrainGraphicReference(1, 1)],
                        members)));
                expected = ("variant-duplicate", $"Terrain '{id}' mask 0x00 contains duplicate variant (1,1).");
                break;
            case "variant-not-member":
                catalog = TerrainCatalogFixture.CreateCatalog(TerrainCatalogFixture.CreateSet(
                    TerrainTopology.FourWay,
                    members,
                    masks: FourWayMasksWithZero([new TerrainGraphicReference(1, 9)], members)));
                expected = ("variant-not-member", $"Terrain '{id}' mask 0x00 variant (1,9) is not a member.");
                break;
            case "variant-graphic-zero":
                catalog = TerrainCatalogFixture.CreateCatalog(TerrainCatalogFixture.CreateSet(
                    TerrainTopology.FourWay,
                    members,
                    masks: FourWayMasksWithZero([new TerrainGraphicReference(1, 0)], members)));
                expected = ("variant-not-member", $"Terrain '{id}' mask 0x00 variant (1,0) is not a member.");
                break;
            case "unused-member":
                catalog = TerrainCatalogFixture.CreateCatalog(TerrainCatalogFixture.CreateSet(
                    TerrainTopology.FourWay,
                    members,
                    masks: TerrainMasks.Required(TerrainTopology.FourWay)
                        .Select(mask => new TerrainMaskDefinition(mask, [new TerrainGraphicReference(1, 1)]))));
                expected = ("enabled-member-unused", $"Enabled terrain '{id}' member (1,2) is unused.");
                break;
            default:
            {
                TerrainMemberDefinition[] aMembers =
                [
                    TerrainCatalogFixture.CreateMember(1, 1),
                    TerrainCatalogFixture.CreateMember(1, 2)
                ];
                TerrainMemberDefinition[] bMembers =
                [
                    TerrainCatalogFixture.CreateMember(1, 1),
                    TerrainCatalogFixture.CreateMember(1, 3)
                ];
                var a = TerrainCatalogFixture.CreateSet(TerrainTopology.FourWay, aMembers);
                var b = TerrainCatalogFixture.CreateSet(TerrainTopology.FourWay, bMembers);
                var (minId, maxId) = string.CompareOrdinal(a.Id, b.Id) < 0 ? (a.Id, b.Id) : (b.Id, a.Id);
                catalog = TerrainCatalogFixture.CreateCatalog(a, b);
                expected = ("enabled-member-conflict",
                    $"Enabled terrain '{minId}' member (1,1) is shared by ['{minId}', '{maxId}'].");
                break;
            }
        }


        var ex = Assert.Throws<ArgumentException>(() => new TerrainMapResolver(catalog));
        Assert.Equal("catalog", ex.ParamName);
        Assert.StartsWith($"Enabled terrain catalog is invalid: {expected.Code}: {expected.Message}", ex.Message);
    }

    [Fact]
    public void Constructor_MissingOrEmptyRequiredMaskIsTheOnlyAcceptedEnabledIssue()
    {
        var members = Members();
        var masks = TerrainCatalogFixture.CreateFullMasks(TerrainTopology.FourWay, members).ToList();
        masks[0] = new TerrainMaskDefinition(0, Array.Empty<TerrainGraphicReference>());
        masks.RemoveAt(1);

        var set = TerrainCatalogFixture.CreateSet(TerrainTopology.FourWay, members, masks: masks);
        var catalog = TerrainCatalogFixture.CreateCatalog(set);

        Assert.All(
            TerrainCatalogValidator.Validate(catalog),
            issue => Assert.Contains(issue.Code, new[] { "enabled-mask-missing", "enabled-mask-empty" }));

        var resolver = new TerrainMapResolver(catalog);
        var terrain = GetTerrain(resolver, set.Id);

        var empty = resolver.ResolveVariant(terrain, 0, 1, 1);
        Assert.False(empty.Succeeded);
        Assert.Equal(0, empty.NormalizedMask);

        var missing = resolver.ResolveVariant(terrain, 1, 1, 1);
        Assert.False(missing.Succeeded);
        Assert.Equal(1, missing.NormalizedMask);

        var ok = resolver.ResolveVariant(terrain, 2, 1, 1);
        Assert.True(ok.Succeeded);
    }

    [Fact]
    public void Constructor_NoEnabledSetsBuildsEmptyResolver()
    {
        var pending = TerrainCatalogFixture.CreateSet(
            TerrainTopology.FourWay,
            Members(),
            TerrainReviewStatus.Pending);
        var disabled = TerrainCatalogFixture.CreateSet(
            TerrainTopology.EightWay,
            [TerrainCatalogFixture.CreateMember(2, 1)],
            TerrainReviewStatus.Disabled);

        var resolver = new TerrainMapResolver(TerrainCatalogFixture.CreateCatalog(pending, disabled));

        Assert.False(resolver.TryGetEnabledTerrain(pending.Id, out _));
        Assert.False(resolver.TryGetEnabledTerrain(disabled.Id, out _));
        Assert.False(resolver.TryGetOwner(new TerrainGraphicReference(1, 1), out _));
        Assert.False(resolver.TryGetOwner(new TerrainGraphicReference(2, 1), out _));
    }

    [Fact]
    public void Constructor_CatalogMetadataDoesNotAffectRuntimeIndex()
    {
        var set = TerrainCatalogFixture.CreateSet(TerrainTopology.FourWay, Members());

        var catalog = TerrainCatalogFixture.CreateCatalog(
            2,
            "",
            "sha256:XYZ",
            new TerrainGenerationSettings(1, 1, 1, 1, 1, 0.1, 0.5, 0.2, 0.1, 0.5, 0.9, 0.9),
            [new TerrainDiagnostic("", "")],
            set);

        var resolver = new TerrainMapResolver(catalog);
        var terrain = GetTerrain(resolver, set.Id);

        var result = resolver.ResolveVariant(terrain, 0, 3, 4);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void OwnerLookup_GraphicZeroNeverHasOwner()
    {
        var set = TerrainCatalogFixture.CreateSet(TerrainTopology.FourWay, Members());
        var resolver = new TerrainMapResolver(TerrainCatalogFixture.CreateCatalog(set));

        Assert.False(resolver.TryGetOwner(new TerrainGraphicReference(0, 0), out _));
        Assert.False(resolver.TryGetOwner(new TerrainGraphicReference(1, 0), out _));
        Assert.False(resolver.TryGetOwner(new TerrainGraphicReference(0, 1), out _));

        Assert.True(resolver.TryGetOwner(new TerrainGraphicReference(1, 1), out var owner));
        Assert.Equal(set.Id, owner.Id);
    }

    [Fact]
    public void ResolveVariant_ResolvesAll16And47RequiredMasks()
    {
        var four = TerrainCatalogFixture.CreateSet(TerrainTopology.FourWay, Members());
        TerrainMemberDefinition[] eightMembers =
        [
            TerrainCatalogFixture.CreateMember(2, 1),
            TerrainCatalogFixture.CreateMember(2, 2)
        ];
        var eight = TerrainCatalogFixture.CreateSet(TerrainTopology.EightWay, eightMembers);

        var resolver = new TerrainMapResolver(TerrainCatalogFixture.CreateCatalog(four, eight));
        var fourTerrain = GetTerrain(resolver, four.Id);
        var eightTerrain = GetTerrain(resolver, eight.Id);

        var coordinates = new[] { (0, 0), (7, 3), (-2, 9) };
        foreach (var mask in TerrainMasks.Required(TerrainTopology.FourWay))
        {
            foreach (var (x, y) in coordinates)
            {
                var result = resolver.ResolveVariant(fourTerrain, mask, x, y);
                Assert.True(result.Succeeded);
                Assert.Equal(mask, result.NormalizedMask);
                Assert.Contains(result.Variant, four.Members.Select(member => member.Reference));
            }
        }

        foreach (var mask in TerrainMasks.Required(TerrainTopology.EightWay))
        {
            foreach (var (x, y) in coordinates)
            {
                var result = resolver.ResolveVariant(eightTerrain, mask, x, y);
                Assert.True(result.Succeeded);
                Assert.Equal(mask, result.NormalizedMask);
                Assert.Contains(result.Variant, eight.Members.Select(member => member.Reference));
            }
        }
    }

    [Fact]
    public void ResolveVariant_All256RawMasksUsePartOneNormalization()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");

            var set = TerrainCatalogFixture.CreateSet(TerrainTopology.FourWay, Members());
            var eight = TerrainCatalogFixture.CreateSet(
                TerrainTopology.EightWay,
                [TerrainCatalogFixture.CreateMember(2, 1), TerrainCatalogFixture.CreateMember(2, 2)]);

            var resolver = new TerrainMapResolver(TerrainCatalogFixture.CreateCatalog(set, eight));
            var fourTerrain = GetTerrain(resolver, set.Id);
            var eightTerrain = GetTerrain(resolver, eight.Id);

            for (var raw = 0; raw < 256; raw++)
            {
                var fourResult = resolver.ResolveVariant(fourTerrain, raw, 5, -3);
                Assert.True(fourResult.Succeeded);
                Assert.Equal(TerrainMasks.Normalize(raw, TerrainTopology.FourWay), fourResult.NormalizedMask);

                var eightResult = resolver.ResolveVariant(eightTerrain, raw, 5, -3);
                Assert.True(eightResult.Succeeded);
                Assert.Equal(TerrainMasks.Normalize(raw, TerrainTopology.EightWay), eightResult.NormalizedMask);
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void ResolveVariant_FixedSha256VectorsMatchUnderNonDefaultCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");

            TerrainMemberDefinition[] members =
            [
                TerrainCatalogFixture.CreateMember(3, 41),
                TerrainCatalogFixture.CreateMember(3, 42)
            ];

            var four = TerrainCatalogFixture.CreateSet(TerrainTopology.FourWay, members);
            var eight = TerrainCatalogFixture.CreateSet(TerrainTopology.EightWay, members);

            Assert.Equal("terrain-4-06a9f0d53153180e867606aef016cbdc5bb62ccb75aebfb7960bcc42d0c0db01", four.Id);
            Assert.Equal("terrain-8-accdd79bb38de5a2e8c4a23e8551083c730208103f3af79b151469ce248cb36a", eight.Id);

            Assert.Equal(
                "be7ed729f90ad5bf63eabe775a8f53124a955c578b9cc201849f002beb332b17",
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{four.Id}\n0\n0\n0"))).ToLowerInvariant());
            Assert.Equal(
                "7fe370c9840ec4f35511d2b85aa27bc3cb3814e880de29ab1fc74eab19be33dc",
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{four.Id}\n17\n29\n5"))).ToLowerInvariant());
            Assert.Equal(
                "6cee359acc546225fe4edf5648cb10e1a3ab251c749f2da6381a2b54114a51fd",
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{four.Id}\n-7\n11\n15"))).ToLowerInvariant());
            Assert.Equal(
                "85630f849fc9c9075ba222ecc63474c053df35983c4060af52980ad78c0b8488",
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{eight.Id}\n17\n29\n19"))).ToLowerInvariant());

            var fourResolver = new TerrainMapResolver(TerrainCatalogFixture.CreateCatalog(four));
            var eightResolver = new TerrainMapResolver(TerrainCatalogFixture.CreateCatalog(eight));
            var fourTerrain = GetTerrain(fourResolver, four.Id);
            var eightTerrain = GetTerrain(eightResolver, eight.Id);

            var second = new TerrainGraphicReference(3, 42);

            var zero = fourResolver.ResolveVariant(fourTerrain, 0, 0, 0);
            Assert.True(zero.Succeeded);
            Assert.Equal(0, zero.NormalizedMask);
            Assert.Equal(second, zero.Variant);

            var cardinal = fourResolver.ResolveVariant(fourTerrain, 0x15, 17, 29);
            Assert.True(cardinal.Succeeded);
            Assert.Equal(5, cardinal.NormalizedMask);
            Assert.Equal(second, cardinal.Variant);

            var negative = fourResolver.ResolveVariant(fourTerrain, 0xFF, -7, 11);
            Assert.True(negative.Succeeded);
            Assert.Equal(15, negative.NormalizedMask);
            Assert.Equal(second, negative.Variant);

            var diagonal = eightResolver.ResolveVariant(eightTerrain, 0x13, 17, 29);
            Assert.True(diagonal.Succeeded);
            Assert.Equal(19, diagonal.NormalizedMask);
            Assert.Equal(second, diagonal.Variant);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void ResolveVariant_IsStableAcrossInstancesAndMultipleCoordinates()
    {
        var set = TerrainCatalogFixture.CreateSet(TerrainTopology.FourWay, Members());
        var catalog = TerrainCatalogFixture.CreateCatalog(set);

        var first = new TerrainMapResolver(catalog);
        var second = new TerrainMapResolver(catalog);
        var firstTerrain = GetTerrain(first, set.Id);
        var secondTerrain = GetTerrain(second, set.Id);

        foreach (var mask in new[] { 0, 7, 15 })
        {
            foreach (var (x, y) in new[] { (0, 0), (5, 5), (-3, 10), (1000, -1000) })
            {
                Assert.Equal(
                    first.ResolveVariant(firstTerrain, mask, x, y),
                    second.ResolveVariant(secondTerrain, mask, x, y));
            }
        }
    }

    [Fact]
    public void ResolveVariant_VariantOrderIsSignificant()
    {
        TerrainMemberDefinition[] members =
        [
            TerrainCatalogFixture.CreateMember(3, 41),
            TerrainCatalogFixture.CreateMember(3, 42)
        ];

        var forward = TerrainCatalogFixture.CreateSet(TerrainTopology.FourWay, members);
        var backward = TerrainCatalogFixture.CreateSet(
            TerrainTopology.FourWay,
            members,
            masks: TerrainCatalogFixture.CreateFullMasks(TerrainTopology.FourWay, members)
                .Select(mask => new TerrainMaskDefinition(mask.Mask, mask.Variants.Reverse())));

        Assert.Equal(forward.Id, backward.Id);

        var forwardResolver = new TerrainMapResolver(TerrainCatalogFixture.CreateCatalog(forward));
        var backwardResolver = new TerrainMapResolver(TerrainCatalogFixture.CreateCatalog(backward));
        var forwardTerrain = GetTerrain(forwardResolver, forward.Id);
        var backwardTerrain = GetTerrain(backwardResolver, backward.Id);

        var forwardVariants = forward.Members.Select(member => member.Reference).ToArray();
        var backwardVariants = forwardVariants.Reverse().ToArray();

        var seen = new HashSet<int>();
        for (var x = 0; x < 64; x++)
        {
            var forwardResult = forwardResolver.ResolveVariant(forwardTerrain, 0, x, 0);
            Assert.True(forwardResult.Succeeded);

            var backwardResult = backwardResolver.ResolveVariant(backwardTerrain, 0, x, 0);
            Assert.True(backwardResult.Succeeded);

            var forwardIndex = Array.IndexOf(forwardVariants, forwardResult.Variant);
            var backwardIndex = Array.IndexOf(backwardVariants, backwardResult.Variant);
            Assert.Equal(forwardIndex, backwardIndex);
            seen.Add(forwardIndex);
        }

        Assert.Equal(new HashSet<int> { 0, 1 }, seen);
    }

    [Fact]
    public void ResolveVariant_DisplayNameCatalogOrderAndReviewOnlyDataAreIrrelevant()
    {
        var members = Members();
        var setA = TerrainCatalogFixture.CreateSet(
            TerrainTopology.FourWay,
            members,
            displayName: "A",
            metrics: new TerrainSetMetrics(5, 5, 5, 0, 0.2, 0.2, 0.2, 0.2, 0.2, 0.0, 0.2),
            diagnostics: [new TerrainDiagnostic("info", "note")]);
        var setB = TerrainCatalogFixture.CreateSet(
            TerrainTopology.FourWay,
            members,
            displayName: "B",
            metrics: new TerrainSetMetrics(9, 9, 9, 0, 0.9, 0.9, 0.9, 0.9, 0.9, 0.0, 0.9),
            diagnostics: [new TerrainDiagnostic("other", "different")]);
        var other = TerrainCatalogFixture.CreateSet(
            TerrainTopology.EightWay,
            [TerrainCatalogFixture.CreateMember(2, 1), TerrainCatalogFixture.CreateMember(2, 2)]);

        Assert.Equal(setA.Id, setB.Id);

        var first = new TerrainMapResolver(TerrainCatalogFixture.CreateCatalog(setA, other));
        var second = new TerrainMapResolver(TerrainCatalogFixture.CreateCatalog(other, setB));
        var firstTerrain = GetTerrain(first, setA.Id);
        var secondTerrain = GetTerrain(second, setB.Id);

        foreach (var mask in new[] { 0, 3, 11, 15 })
        {
            foreach (var (x, y) in new[] { (0, 0), (4, -4) })
            {
                Assert.Equal(
                    first.ResolveVariant(firstTerrain, mask, x, y),
                    second.ResolveVariant(secondTerrain, mask, x, y));
            }
        }
    }

    [Fact]
    public void ResolveVariant_MissingOrEmptyMappingReturnsExactFailureWithoutThrowing()
    {
        var members = Members();
        var masks = TerrainCatalogFixture.CreateFullMasks(TerrainTopology.FourWay, members).ToList();
        masks[0] = new TerrainMaskDefinition(0, Array.Empty<TerrainGraphicReference>());
        masks.RemoveAt(1);

        var set = TerrainCatalogFixture.CreateSet(TerrainTopology.FourWay, members, masks: masks);
        var resolver = new TerrainMapResolver(TerrainCatalogFixture.CreateCatalog(set));
        var terrain = GetTerrain(resolver, set.Id);

        var empty = resolver.ResolveVariant(terrain, 0, 1, 1);
        Assert.False(empty.Succeeded);
        Assert.Equal(default, empty.Variant);
        Assert.Equal(0, empty.NormalizedMask);
        Assert.Equal(new TerrainEditFailure(set.Id, 0), empty.Failure);

        var missing = resolver.ResolveVariant(terrain, 0x11, 1, 1);
        Assert.False(missing.Succeeded);
        Assert.Equal(default, missing.Variant);
        Assert.Equal(1, missing.NormalizedMask);
        Assert.Equal(new TerrainEditFailure(set.Id, 1), missing.Failure);

        var ok = resolver.ResolveVariant(terrain, 5, 1, 1);
        Assert.True(ok.Succeeded);
        Assert.Null(ok.Failure);
    }
}
