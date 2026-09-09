using System;
using System.Collections.Generic;
using System.Linq;
using MapEditor.Core.Terrain;
using Xunit;

namespace MapEditor.Core.Tests.Terrain;

public class TerrainCatalogJsonTests
{
    private static readonly string Fingerprint =
        "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static TerrainGenerationSettings ValidSettings() => new(
        HoldoutModulo: 3,
        MinimumMapSupport: 2,
        MinimumRegionSupport: 1,
        MinimumObservationSupport: 1,
        MinimumDiagonalSupport: 1,
        MinimumEightWayAccuracyGain: 0.05,
        MapMemberCompatibility: 0.5,
        ImageOnlyCompatibility: 0.75,
        MinimumClassificationMargin: 0.02,
        MaximumAmbiguity: 0.25,
        EnabledConfidence: 0.9,
        PendingConfidence: 0.6);

    private static TerrainSetMetrics ValidMetrics() => new(1, 1, 1, 0, 0.5, 0.5, 0.5, 0.5, 0.5, 0.0, 0.5);

    private static TerrainSetDefinition BuildSetA(out string id)
    {
        var members = new List<TerrainMemberDefinition>();
        for (var graphic = 10; graphic <= 26; graphic++)
        {
            members.Add(new TerrainMemberDefinition(new TerrainGraphicReference(1, graphic), TerrainMemberProvenance.MapObserved));
        }

        id = TerrainGeneratedId.Create(TerrainTopology.FourWay, members.Select(member => member.Reference));

        var masks = new List<TerrainMaskDefinition>();
        for (var mask = 0; mask < 15; mask++)
        {
            masks.Add(new TerrainMaskDefinition(mask, new[] { new TerrainGraphicReference(1, 10 + mask) }));
        }

        masks.Add(new TerrainMaskDefinition(15, new[] { new TerrainGraphicReference(1, 26), new TerrainGraphicReference(1, 25) }));

        return new TerrainSetDefinition(
            id,
            "Grass",
            TerrainReviewStatus.Enabled,
            TerrainTopology.FourWay,
            new TerrainSetMetrics(10, 8, 90, 0, 0.5, 1.0, 0.1, 0.95, 0.99, 0.0, 0.95),
            masks.OrderBy(mask => (mask.Mask * 7) % 17).ToList(),
            members.OrderBy(member => (member.Reference.Graphic * 5) % 11).ToList(),
            new[]
            {
                new TerrainDiagnostic("terrain-mask-low-support", "Mask 0x00 has low support.", 0, new TerrainGraphicReference(1, 10)),
                new TerrainDiagnostic("terrain-corpus-note", "Generated from corpus snapshot.")
            });
    }

    private static TerrainSetDefinition BuildSetB(out string id)
    {
        var members = new[]
        {
            new TerrainMemberDefinition(new TerrainGraphicReference(2, 100), TerrainMemberProvenance.ImageOnly),
            new TerrainMemberDefinition(new TerrainGraphicReference(2, 101), TerrainMemberProvenance.ImageOnly),
            new TerrainMemberDefinition(new TerrainGraphicReference(2, 102), TerrainMemberProvenance.MapObserved)
        };

        id = TerrainGeneratedId.Create(TerrainTopology.EightWay, members.Select(member => member.Reference));

        return new TerrainSetDefinition(
            id,
            "Dirt",
            TerrainReviewStatus.Pending,
            TerrainTopology.EightWay,
            new TerrainSetMetrics(0, 0, 3, 0, 0.75, 0.25, 0.5, 0.75, 0.5, -0.25, 0.4),
            new[]
            {
                new TerrainMaskDefinition(3, Array.Empty<TerrainGraphicReference>()),
                new TerrainMaskDefinition(0, new[] { new TerrainGraphicReference(2, 100) })
            },
            new[] { members[2], members[0], members[1] },
            new[]
            {
                new TerrainDiagnostic("terrain-image-only", "Member sourced from image atlas.", null, new TerrainGraphicReference(2, 100)),
                new TerrainDiagnostic("terrain-eight-way-pending", "Awaiting map observations.", 3, new TerrainGraphicReference(2, 101))
            });
    }

    private static TerrainCatalog BuildRepresentativeCatalog()
    {
        var setA = BuildSetA(out var idA);
        var setB = BuildSetB(out var idB);

        return new TerrainCatalog(
            TerrainCatalogJson.CurrentSchemaVersion,
            "asset-converter/0.1.0",
            Fingerprint,
            ValidSettings(),
            new[] { setB, setA },
            new[] { new TerrainDiagnostic("corpus-small", "Corpus below preferred size.") });
    }

    private static string ExpectedSetAJson(string id)
    {
        var masks = new List<string>();
        for (var mask = 0; mask < 15; mask++)
        {
            var graphic = 10 + mask;
            var variant = $"{{\"sheet\":1,\"graphic\":{graphic}" + "}";
            masks.Add($"{{\"mask\":{mask},\"variants\":[{variant}]}}");
        }

        masks.Add("{\"mask\":15,\"variants\":[{\"sheet\":1,\"graphic\":26},{\"sheet\":1,\"graphic\":25}]}");

        var members = string.Join(",", Enumerable.Range(10, 17)
            .Select(graphic => $"{{\"reference\":{{\"sheet\":1,\"graphic\":{graphic}" + "},\"provenance\":\"map-observed\"}"));

        return $"{{\"id\":\"{id}\",\"displayName\":\"Grass\",\"status\":\"enabled\",\"topology\":\"four-way\"," +
               $"\"metrics\":{{\"mapSupport\":10,\"regionSupport\":8,\"observationSupport\":90,\"diagonalSupport\":0," +
               "\"maskEntropy\":0.5,\"completeness\":1,\"ambiguity\":0.1,\"visualCompatibility\":0.95," +
               "\"holdoutAccuracy\":0.99,\"eightWayAccuracyGain\":0,\"confidence\":0.95}," +
               $"\"masks\":[{string.Join(",", masks)}],\"members\":[{members}]," +
               "\"diagnostics\":[{\"code\":\"terrain-corpus-note\",\"message\":\"Generated from corpus snapshot.\",\"mask\":null,\"reference\":null}," +
               "{\"code\":\"terrain-mask-low-support\",\"message\":\"Mask 0x00 has low support.\",\"mask\":0,\"reference\":{\"sheet\":1,\"graphic\":10}}]}";
    }

    private static string ExpectedSetBJson(string id) =>
        $"{{\"id\":\"{id}\",\"displayName\":\"Dirt\",\"status\":\"pending\",\"topology\":\"eight-way\"," +
        "\"metrics\":{\"mapSupport\":0,\"regionSupport\":0,\"observationSupport\":3,\"diagonalSupport\":0," +
        "\"maskEntropy\":0.75,\"completeness\":0.25,\"ambiguity\":0.5,\"visualCompatibility\":0.75," +
        "\"holdoutAccuracy\":0.5,\"eightWayAccuracyGain\":-0.25,\"confidence\":0.4}," +
        "\"masks\":[{\"mask\":0,\"variants\":[{\"sheet\":2,\"graphic\":100}]},{\"mask\":3,\"variants\":[]}]," +
        "\"members\":[{\"reference\":{\"sheet\":2,\"graphic\":100},\"provenance\":\"image-only\"}," +
        "{\"reference\":{\"sheet\":2,\"graphic\":101},\"provenance\":\"image-only\"}," +
        "{\"reference\":{\"sheet\":2,\"graphic\":102},\"provenance\":\"map-observed\"}]," +
        "\"diagnostics\":[{\"code\":\"terrain-eight-way-pending\",\"message\":\"Awaiting map observations.\",\"mask\":3,\"reference\":{\"sheet\":2,\"graphic\":101}}," +
        "{\"code\":\"terrain-image-only\",\"message\":\"Member sourced from image atlas.\",\"mask\":null,\"reference\":{\"sheet\":2,\"graphic\":100}}]}";

    private static string ExpectedLockedJson()
    {
        BuildSetA(out var idA);
        BuildSetB(out var idB);
        var ordered = string.CompareOrdinal(idA, idB) < 0
            ? new[] { (Id: idA, Json: ExpectedSetAJson(idA)), (Id: idB, Json: ExpectedSetBJson(idB)) }
            : new[] { (Id: idB, Json: ExpectedSetBJson(idB)), (Id: idA, Json: ExpectedSetAJson(idA)) };

        var sets = $"[{ordered[0].Json},{ordered[1].Json}]";

        return "{\"schemaVersion\":1,\"generatorVersion\":\"asset-converter/0.1.0\"," +
               $"\"corpusFingerprint\":\"{Fingerprint}\"," +
               "\"settings\":{\"holdoutModulo\":3,\"minimumMapSupport\":2,\"minimumRegionSupport\":1," +
               "\"minimumObservationSupport\":1,\"minimumDiagonalSupport\":1,\"minimumEightWayAccuracyGain\":0.05," +
               "\"mapMemberCompatibility\":0.5,\"imageOnlyCompatibility\":0.75,\"minimumClassificationMargin\":0.02," +
               "\"maximumAmbiguity\":0.25,\"enabledConfidence\":0.9,\"pendingConfidence\":0.6}," +
               "\"diagnostics\":[{\"code\":\"corpus-small\",\"message\":\"Corpus below preferred size.\",\"mask\":null,\"reference\":null}]," +
               "\"sets\":" + sets + "}";
    }

    private static (TerrainCatalogError Error, string SourcePath) ParseError(string json, string path = "catalog.json")
    {
        try
        {
            TerrainCatalogJson.Parse(json, path);
            return (default, null!);
        }
        catch (TerrainCatalogException ex)
        {
            return (ex.Error, ex.SourcePath);
        }
    }

    [Fact]
    public void Constructors_CopyEveryNestedInputCollection()
    {
        var variants = new List<TerrainGraphicReference> { new(1, 10) };
        var masks = new List<TerrainMaskDefinition> { new(0, variants) };
        var members = new List<TerrainMemberDefinition> { new(new TerrainGraphicReference(1, 10), TerrainMemberProvenance.MapObserved) };
        var setDiagnostics = new List<TerrainDiagnostic> { new("set-code", "set-message") };
        var sets = new List<TerrainSetDefinition>
        {
            new("id", "name", TerrainReviewStatus.Pending, TerrainTopology.FourWay, ValidMetrics(), masks, members, setDiagnostics)
        };
        var rootDiagnostics = new List<TerrainDiagnostic> { new("root-code", "root-message") };
        var catalog = new TerrainCatalog(1, "generator", Fingerprint, ValidSettings(), sets, rootDiagnostics);

        variants.Add(new TerrainGraphicReference(9, 9));
        masks.Add(new TerrainMaskDefinition(1, Array.Empty<TerrainGraphicReference>()));
        members.Add(new TerrainMemberDefinition(new TerrainGraphicReference(9, 9), TerrainMemberProvenance.ImageOnly));
        setDiagnostics.Add(new TerrainDiagnostic("extra", "extra"));
        sets.Add(new TerrainSetDefinition("other", "other", TerrainReviewStatus.Disabled, TerrainTopology.EightWay, ValidMetrics(), Array.Empty<TerrainMaskDefinition>(), Array.Empty<TerrainMemberDefinition>(), Array.Empty<TerrainDiagnostic>()));
        rootDiagnostics.Add(new TerrainDiagnostic("extra", "extra"));

        Assert.Single(catalog.Sets);
        Assert.Single(catalog.Diagnostics);
        Assert.Single(catalog.Sets[0].Masks);
        Assert.Single(catalog.Sets[0].Members);
        Assert.Single(catalog.Sets[0].Diagnostics);
        Assert.Single(catalog.Sets[0].Masks[0].Variants);
        Assert.Equal(new TerrainGraphicReference(1, 10), catalog.Sets[0].Masks[0].Variants[0]);

        Assert.NotSame(masks, catalog.Sets[0].Masks);
        Assert.NotSame(members, catalog.Sets[0].Members);
        Assert.NotSame(sets, catalog.Sets);
        Assert.NotSame(variants, catalog.Sets[0].Masks[0].Variants);
        Assert.ThrowsAny<Exception>(() => ((IList<TerrainMaskDefinition>)catalog.Sets[0].Masks).Add(null!));
        Assert.ThrowsAny<Exception>(() => ((IList<TerrainGraphicReference>)catalog.Sets[0].Masks[0].Variants).Add(new TerrainGraphicReference(0, 0)));

        Assert.Throws<ArgumentNullException>(() => new TerrainMaskDefinition(0, null!));
        Assert.Throws<ArgumentNullException>(() => new TerrainSetDefinition(null!, "n", TerrainReviewStatus.Pending, TerrainTopology.FourWay, ValidMetrics(), Array.Empty<TerrainMaskDefinition>(), Array.Empty<TerrainMemberDefinition>(), Array.Empty<TerrainDiagnostic>()));
        Assert.Throws<ArgumentNullException>(() => new TerrainSetDefinition("id", "n", TerrainReviewStatus.Pending, TerrainTopology.FourWay, null!, Array.Empty<TerrainMaskDefinition>(), Array.Empty<TerrainMemberDefinition>(), Array.Empty<TerrainDiagnostic>()));
        Assert.Throws<ArgumentNullException>(() => new TerrainSetDefinition("id", "n", TerrainReviewStatus.Pending, TerrainTopology.FourWay, ValidMetrics(), new List<TerrainMaskDefinition> { null! }, Array.Empty<TerrainMemberDefinition>(), Array.Empty<TerrainDiagnostic>()));
        Assert.Throws<ArgumentNullException>(() => new TerrainCatalog(1, "g", Fingerprint, ValidSettings(), null!, Array.Empty<TerrainDiagnostic>()));
        Assert.Throws<ArgumentNullException>(() => new TerrainCatalog(1, "g", Fingerprint, ValidSettings(), new List<TerrainSetDefinition> { null! }, Array.Empty<TerrainDiagnostic>()));
    }

    [Fact]
    public void Serialize_RepresentativeCatalogMatchesLockedSchemaV1Bytes()
    {
        var catalog = BuildRepresentativeCatalog();

        var json = TerrainCatalogJson.Serialize(catalog);

        Assert.Equal(ExpectedLockedJson() + "\n", json);
        Assert.Equal('{', json[0]);
        Assert.Equal('\n', json[^1]);
        Assert.NotEqual('\n', json[^2]);
    }

    [Fact]
    public void Parse_SerializeCanonicalizesNonSemanticOrderButPreservesVariantOrder()
    {
        var catalog = BuildRepresentativeCatalog();
        var canonical = TerrainCatalogJson.Serialize(catalog);

        var roundTrip = TerrainCatalogJson.Serialize(TerrainCatalogJson.Parse(canonical));
        Assert.Equal(canonical, roundTrip);

        BuildSetA(out var idA);
        BuildSetB(out var idB);

        var minimalSettings =
            "\"settings\":{\"minimumClassificationMargin\":0.1,\"pendingConfidence\":0.5,\"holdoutModulo\":2," +
            "\"imageOnlyCompatibility\":0.3,\"minimumObservationSupport\":1,\"mapMemberCompatibility\":0.2," +
            "\"maximumAmbiguity\":0.4,\"minimumRegionSupport\":1,\"enabledConfidence\":0.8," +
            "\"minimumDiagonalSupport\":1,\"minimumEightWayAccuracyGain\":0.1,\"minimumMapSupport\":1}";
        var minimalMetrics =
            "\"metrics\":{\"confidence\":0.5,\"eightWayAccuracyGain\":0,\"maskEntropy\":0,\"holdoutAccuracy\":0.5," +
            "\"completeness\":0.5,\"mapSupport\":1,\"ambiguity\":0.5,\"regionSupport\":1,\"observationSupport\":1," +
            "\"diagonalSupport\":0,\"visualCompatibility\":0.5}";
        var minimalSet =
            $"\"sets\":[{{\"members\":[{{\"provenance\":\"map-observed\",\"reference\":{{\"graphic\":1,\"sheet\":7}}}}]," +
            $"\"masks\":[{{\"variants\":[{{\"sheet\":7,\"graphic\":1}}],\"mask\":5}}]," +
            $"\"status\":\"pending\",\"diagnostics\":[],\"displayName\":\"S\",\"topology\":\"four-way\"," +
            $"{minimalMetrics},\"id\":\"{TerrainGeneratedId.Create(TerrainTopology.FourWay, new[] { new TerrainGraphicReference(7, 1) })}\"}}]";
        var shuffled =
            $"{{{minimalSet},\"schemaVersion\":1,\"diagnostics\":[],\"generatorVersion\":\"gen/1.0\"," +
            $"\"corpusFingerprint\":\"sha256:{new string('0', 64)}\",{minimalSettings}}}";

        var shuffledCatalog = new TerrainCatalog(
            1,
            "gen/1.0",
            $"sha256:{new string('0', 64)}",
            new TerrainGenerationSettings(2, 1, 1, 1, 1, 0.1, 0.2, 0.3, 0.1, 0.4, 0.8, 0.5),
            new[]
            {
                new TerrainSetDefinition(
                    TerrainGeneratedId.Create(TerrainTopology.FourWay, new[] { new TerrainGraphicReference(7, 1) }),
                    "S",
                    TerrainReviewStatus.Pending,
                    TerrainTopology.FourWay,
                    new TerrainSetMetrics(1, 1, 1, 0, 0.0, 0.5, 0.5, 0.5, 0.5, 0.0, 0.5),
                    new[] { new TerrainMaskDefinition(5, new[] { new TerrainGraphicReference(7, 1) }) },
                    new[] { new TerrainMemberDefinition(new TerrainGraphicReference(7, 1), TerrainMemberProvenance.MapObserved) },
                    Array.Empty<TerrainDiagnostic>())
            },
            Array.Empty<TerrainDiagnostic>());

        Assert.Equal(TerrainCatalogJson.Serialize(shuffledCatalog), TerrainCatalogJson.Serialize(TerrainCatalogJson.Parse(shuffled)));

        var swappedVariants = canonical.Replace(
            "\"variants\":[{\"sheet\":1,\"graphic\":26},{\"sheet\":1,\"graphic\":25}]",
            "\"variants\":[{\"sheet\":1,\"graphic\":25},{\"sheet\":1,\"graphic\":26}]");
        var parsedVariants = TerrainCatalogJson.Serialize(TerrainCatalogJson.Parse(swappedVariants));
        Assert.Equal(swappedVariants, parsedVariants);
        Assert.NotEqual(canonical, parsedVariants);
    }

    [Fact]
    public void Parse_MalformedDuplicateUnknownMissingNullWrongKindAndInvalidEnumReturnExactTerrainCatalogError()
    {
        var baseJson = TerrainCatalogJson.Serialize(BuildRepresentativeCatalog());

        var malformed = ParseError("{\"schemaVersion\":");
        Assert.Equal(TerrainCatalogError.MalformedJson, malformed.Error);
        Assert.Equal("catalog.json", malformed.SourcePath);

        Assert.Equal(TerrainCatalogError.InvalidRoot, ParseError("[1]").Error);
        Assert.Equal(TerrainCatalogError.InvalidRoot, ParseError("\"catalog\"").Error);
        Assert.Equal(TerrainCatalogError.InvalidRoot, ParseError("5").Error);
        Assert.Equal(TerrainCatalogError.InvalidRoot, ParseError("null").Error);

        var duplicated = ParseError(baseJson.Insert(1, "\"schemaVersion\":1,"));
        Assert.Equal(TerrainCatalogError.DuplicateProperty, duplicated.Error);
        Assert.Equal("catalog.json", duplicated.SourcePath);

        var unknown = ParseError(baseJson.Insert(1, "\"bogus\":1,"));
        Assert.Equal(TerrainCatalogError.UnknownProperty, unknown.Error);

        var unknownNested = ParseError(baseJson.Replace("\"provenance\":\"map-observed\"", "\"provenance\":\"map-observed\",\"extra\":1"));
        Assert.Equal(TerrainCatalogError.UnknownProperty, unknownNested.Error);

        var missing = ParseError(baseJson.Replace($"\"corpusFingerprint\":\"{Fingerprint}\",", ""));
        Assert.Equal(TerrainCatalogError.MissingProperty, missing.Error);

        var missingNested = ParseError(baseJson.Replace("\"sheet\":1,", ""));
        Assert.Equal(TerrainCatalogError.MissingProperty, missingNested.Error);

        var nullString = ParseError(baseJson.Replace("\"generatorVersion\":\"asset-converter/0.1.0\"", "\"generatorVersion\":null"));
        Assert.Equal(TerrainCatalogError.NullNotAllowed, nullString.Error);

        var nullArrayElement = ParseError(baseJson.Replace("{\"code\":\"corpus-small\",\"message\":\"Corpus below preferred size.\",\"mask\":null,\"reference\":null}", "null"));
        Assert.Equal(TerrainCatalogError.NullNotAllowed, nullArrayElement.Error);

        var nullForbiddenButAllowedElsewhere = ParseError(baseJson.Replace("\"mask\":0", "\"mask\":null"));
        Assert.Equal(TerrainCatalogError.NullNotAllowed, nullForbiddenButAllowedElsewhere.Error);

        var wrongKindString = ParseError(baseJson.Replace("\"schemaVersion\":1", "\"schemaVersion\":\"1\""));
        Assert.Equal(TerrainCatalogError.InvalidPropertyType, wrongKindString.Error);

        var wrongKindFractionalInteger = ParseError(baseJson.Replace("\"schemaVersion\":1", "\"schemaVersion\":1.5"));
        Assert.Equal(TerrainCatalogError.InvalidPropertyType, wrongKindFractionalInteger.Error);

        var wrongKindDouble = ParseError(baseJson.Replace("\"maskEntropy\":0.5", "\"maskEntropy\":\"0.5\""));
        Assert.Equal(TerrainCatalogError.InvalidPropertyType, wrongKindDouble.Error);

        var invalidEnumStatus = ParseError(baseJson.Replace("\"status\":\"enabled\"", "\"status\":\"Enabled\""));
        Assert.Equal(TerrainCatalogError.InvalidEnum, invalidEnumStatus.Error);

        var invalidEnumTopology = ParseError(baseJson.Replace("\"topology\":\"four-way\"", "\"topology\":\"fourway\""));
        Assert.Equal(TerrainCatalogError.InvalidEnum, invalidEnumTopology.Error);

        var invalidEnumProvenance = ParseError(baseJson.Replace("\"provenance\":\"map-observed\"", "\"provenance\":\"map\""));
        Assert.Equal(TerrainCatalogError.InvalidEnum, invalidEnumProvenance.Error);

        var unsupportedSchema = ParseError(baseJson.Replace("\"schemaVersion\":1", "\"schemaVersion\":2"));
        Assert.Equal(TerrainCatalogError.UnsupportedSchemaVersion, unsupportedSchema.Error);
    }

    [Fact]
    public void Parse_InvalidSettingsRelationshipsOrMetricRangesReturnExactTerrainCatalogError()
    {
        var baseJson = TerrainCatalogJson.Serialize(BuildRepresentativeCatalog());

        var cases = new (string Replacement, string With, TerrainCatalogError Error)[]
        {
            ("\"holdoutModulo\":3", "\"holdoutModulo\":1", TerrainCatalogError.NumberOutOfRange),
            ("\"minimumMapSupport\":2", "\"minimumMapSupport\":0", TerrainCatalogError.NumberOutOfRange),
            ("\"minimumRegionSupport\":1", "\"minimumRegionSupport\":0", TerrainCatalogError.NumberOutOfRange),
            ("\"minimumObservationSupport\":1", "\"minimumObservationSupport\":0", TerrainCatalogError.NumberOutOfRange),
            ("\"minimumDiagonalSupport\":1", "\"minimumDiagonalSupport\":0", TerrainCatalogError.NumberOutOfRange),
            ("\"minimumEightWayAccuracyGain\":0.05", "\"minimumEightWayAccuracyGain\":0", TerrainCatalogError.NumberOutOfRange),
            ("\"minimumEightWayAccuracyGain\":0.05", "\"minimumEightWayAccuracyGain\":1.5", TerrainCatalogError.NumberOutOfRange),
            ("\"minimumClassificationMargin\":0.02", "\"minimumClassificationMargin\":0", TerrainCatalogError.NumberOutOfRange),
            ("\"minimumClassificationMargin\":0.02", "\"minimumClassificationMargin\":1.5", TerrainCatalogError.NumberOutOfRange),
            ("\"mapMemberCompatibility\":0.5", "\"mapMemberCompatibility\":1.5", TerrainCatalogError.NumberOutOfRange),
            ("\"imageOnlyCompatibility\":0.75", "\"imageOnlyCompatibility\":-0.5", TerrainCatalogError.NumberOutOfRange),
            ("\"maximumAmbiguity\":0.25", "\"maximumAmbiguity\":1.5", TerrainCatalogError.NumberOutOfRange),
            ("\"enabledConfidence\":0.9", "\"enabledConfidence\":1.5", TerrainCatalogError.NumberOutOfRange),
            ("\"pendingConfidence\":0.6", "\"pendingConfidence\":-0.5", TerrainCatalogError.NumberOutOfRange),
            ("\"mapMemberCompatibility\":0.5", "\"mapMemberCompatibility\":0.9", TerrainCatalogError.InvalidRelationship),
            ("\"pendingConfidence\":0.6", "\"pendingConfidence\":0.95", TerrainCatalogError.InvalidRelationship),
            ("\"mapSupport\":10", "\"mapSupport\":-1", TerrainCatalogError.NumberOutOfRange),
            ("\"regionSupport\":8", "\"regionSupport\":-1", TerrainCatalogError.NumberOutOfRange),
            ("\"observationSupport\":90", "\"observationSupport\":-1", TerrainCatalogError.NumberOutOfRange),
            ("\"diagonalSupport\":0", "\"diagonalSupport\":-1", TerrainCatalogError.NumberOutOfRange),
            ("\"maskEntropy\":0.5", "\"maskEntropy\":1.5", TerrainCatalogError.NumberOutOfRange),
            ("\"completeness\":1", "\"completeness\":1.5", TerrainCatalogError.NumberOutOfRange),
            ("\"ambiguity\":0.1", "\"ambiguity\":-0.5", TerrainCatalogError.NumberOutOfRange),
            ("\"visualCompatibility\":0.95", "\"visualCompatibility\":1.5", TerrainCatalogError.NumberOutOfRange),
            ("\"holdoutAccuracy\":0.99", "\"holdoutAccuracy\":-0.5", TerrainCatalogError.NumberOutOfRange),
            ("\"confidence\":0.95", "\"confidence\":1.5", TerrainCatalogError.NumberOutOfRange),
            ("\"eightWayAccuracyGain\":0", "\"eightWayAccuracyGain\":1.5", TerrainCatalogError.NumberOutOfRange),
            ("\"eightWayAccuracyGain\":0", "\"eightWayAccuracyGain\":-1.5", TerrainCatalogError.NumberOutOfRange)
        };

        foreach (var (replacement, with, expected) in cases)
        {
            var result = ParseError(baseJson.Replace(replacement, with));
            Assert.True(result.Error == expected, $"{replacement} -> {with}");
            Assert.Equal("catalog.json", result.SourcePath);
        }
    }

    [Fact]
    public void Parse_UnpairedSurrogateEscapeThrowsMalformedJsonWithSourcePath()
    {
        var baseJson = TerrainCatalogJson.Serialize(BuildRepresentativeCatalog());
        var json = baseJson.Replace("\"displayName\":\"Grass\"", "\"displayName\":\"\\ud800\"");

        var error = ParseError(json, "surrogate.json");

        Assert.Equal(TerrainCatalogError.MalformedJson, error.Error);
        Assert.Equal("surrogate.json", error.SourcePath);
    }

    [Fact]
    public void Constructors_NullRequiredStringsThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new TerrainDiagnostic(null!, "m"));
        Assert.Throws<ArgumentNullException>(() => new TerrainDiagnostic("c", null!));
        Assert.Throws<ArgumentNullException>(() => new TerrainValidationIssue(null!, "m"));
        Assert.Throws<ArgumentNullException>(() => new TerrainValidationIssue("c", null!));
        Assert.Throws<ArgumentNullException>(() => new TerrainCatalogException(TerrainCatalogError.MalformedJson, null!, "m"));
    }

    [Fact]
    public void Serialize_NullAndInvalidStructuralValuesThrowTypedErrors()
    {
        Assert.Throws<ArgumentNullException>(() => TerrainCatalogJson.Serialize(null!));

        var catalog = BuildRepresentativeCatalog();
        var badStatus = new TerrainCatalog(
            1,
            "asset-converter/0.1.0",
            Fingerprint,
            ValidSettings(),
            new[]
            {
                new TerrainSetDefinition(
                    "id",
                    "name",
                    (TerrainReviewStatus)99,
                    TerrainTopology.FourWay,
                    ValidMetrics(),
                    Array.Empty<TerrainMaskDefinition>(),
                    new[] { new TerrainMemberDefinition(new TerrainGraphicReference(1, 1), TerrainMemberProvenance.MapObserved) },
                    Array.Empty<TerrainDiagnostic>())
            },
            Array.Empty<TerrainDiagnostic>());
        var statusError = Assert.Throws<TerrainCatalogException>(() => TerrainCatalogJson.Serialize(badStatus));
        Assert.Equal(TerrainCatalogError.InvalidEnum, statusError.Error);
        Assert.Equal("<object>", statusError.SourcePath);

        var badSchema = new TerrainCatalog(
            2,
            "asset-converter/0.1.0",
            Fingerprint,
            ValidSettings(),
            Array.Empty<TerrainSetDefinition>(),
            Array.Empty<TerrainDiagnostic>());
        var schemaError = Assert.Throws<TerrainCatalogException>(() => TerrainCatalogJson.Serialize(badSchema));
        Assert.Equal(TerrainCatalogError.UnsupportedSchemaVersion, schemaError.Error);
        Assert.Equal("<object>", schemaError.SourcePath);

        var badSetting = new TerrainCatalog(
            1,
            "asset-converter/0.1.0",
            Fingerprint,
            new TerrainGenerationSettings(1, 1, 1, 1, 1, 0.1, 0.5, 0.5, 0.1, 0.5, 0.9, 0.5),
            Array.Empty<TerrainSetDefinition>(),
            Array.Empty<TerrainDiagnostic>());
        var settingError = Assert.Throws<TerrainCatalogException>(() => TerrainCatalogJson.Serialize(badSetting));
        Assert.Equal(TerrainCatalogError.NumberOutOfRange, settingError.Error);
        Assert.Equal("<object>", settingError.SourcePath);

        var badRelationship = new TerrainCatalog(
            1,
            "asset-converter/0.1.0",
            Fingerprint,
            new TerrainGenerationSettings(2, 1, 1, 1, 1, 0.1, 0.5, 0.4, 0.1, 0.5, 0.9, 0.5),
            Array.Empty<TerrainSetDefinition>(),
            Array.Empty<TerrainDiagnostic>());
        var relationshipError = Assert.Throws<TerrainCatalogException>(() => TerrainCatalogJson.Serialize(badRelationship));
        Assert.Equal(TerrainCatalogError.InvalidRelationship, relationshipError.Error);
        Assert.Equal("<object>", relationshipError.SourcePath);

        var badMetric = new TerrainCatalog(
            1,
            "asset-converter/0.1.0",
            Fingerprint,
            ValidSettings(),
            new[]
            {
                new TerrainSetDefinition(
                    "id",
                    "name",
                    TerrainReviewStatus.Pending,
                    TerrainTopology.FourWay,
                    new TerrainSetMetrics(-1, 1, 1, 0, 0.5, 0.5, 0.5, 0.5, 0.5, 0.0, 0.5),
                    Array.Empty<TerrainMaskDefinition>(),
                    new[] { new TerrainMemberDefinition(new TerrainGraphicReference(1, 1), TerrainMemberProvenance.MapObserved) },
                    Array.Empty<TerrainDiagnostic>())
            },
            Array.Empty<TerrainDiagnostic>());
        var metricError = Assert.Throws<TerrainCatalogException>(() => TerrainCatalogJson.Serialize(badMetric));
        Assert.Equal(TerrainCatalogError.NumberOutOfRange, metricError.Error);
        Assert.Equal("<object>", metricError.SourcePath);
    }
}
