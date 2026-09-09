using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace MapEditor.Core.Terrain;

public static class TerrainCatalogJson
{
    public const int CurrentSchemaVersion = 1;

    private const string ObjectSourcePath = "<object>";

    private static readonly string[] RootProperties =
    {
        "schemaVersion", "generatorVersion", "corpusFingerprint", "settings", "diagnostics", "sets"
    };

    private static readonly string[] SettingProperties =
    {
        "holdoutModulo", "minimumMapSupport", "minimumRegionSupport", "minimumObservationSupport",
        "minimumDiagonalSupport", "minimumEightWayAccuracyGain", "mapMemberCompatibility",
        "imageOnlyCompatibility", "minimumClassificationMargin", "maximumAmbiguity",
        "enabledConfidence", "pendingConfidence"
    };

    private static readonly string[] SetProperties =
    {
        "id", "displayName", "status", "topology", "metrics", "masks", "members", "diagnostics"
    };

    private static readonly string[] MetricProperties =
    {
        "mapSupport", "regionSupport", "observationSupport", "diagonalSupport", "maskEntropy",
        "completeness", "ambiguity", "visualCompatibility", "holdoutAccuracy",
        "eightWayAccuracyGain", "confidence"
    };

    private static readonly string[] MaskProperties = { "mask", "variants" };

    private static readonly string[] MemberProperties = { "reference", "provenance" };

    private static readonly string[] DiagnosticProperties = { "code", "message", "mask", "reference" };

    private static readonly string[] ReferenceProperties = { "sheet", "graphic" };

    private static readonly Comparer<TerrainDiagnostic> DiagnosticComparer = Comparer<TerrainDiagnostic>.Create((a, b) =>
    {
        var result = StringComparer.Ordinal.Compare(a.Code, b.Code);
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

        return StringComparer.Ordinal.Compare(a.Message, b.Message);
    });

    public static TerrainCatalog Parse(string json, string sourcePath = "<memory>")
    {
        if (json is null)
        {
            throw new ArgumentNullException(nameof(json));
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new TerrainCatalogException(TerrainCatalogError.MalformedJson, sourcePath, "The catalog is not valid JSON.", ex);
        }

        using (document)
        {
            try
            {
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    throw Fail(TerrainCatalogError.InvalidRoot, sourcePath, "The catalog root must be a JSON object.");
                }

                return ParseCatalog(document.RootElement, sourcePath);
            }
            catch (InvalidOperationException ex)
            {
                // JsonDocument decodes strings lazily; an unpaired surrogate escape surfaces from GetString() here.
                throw new TerrainCatalogException(TerrainCatalogError.MalformedJson, sourcePath, "The catalog contains an unpaired surrogate escape.", ex);
            }
        }
    }

    public static string Serialize(TerrainCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ValidateStructural(catalog);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteCatalog(writer, catalog);
        }

        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    private static TerrainCatalog ParseCatalog(JsonElement element, string path)
    {
        var properties = EnumerateProperties(element, path, RootProperties);
        var schemaVersion = ParseInt(properties, "schemaVersion", path);
        if (schemaVersion != CurrentSchemaVersion)
        {
            throw Fail(TerrainCatalogError.UnsupportedSchemaVersion, path, $"Unsupported schema version {schemaVersion}; expected {CurrentSchemaVersion}.");
        }

        var generatorVersion = ParseString(properties, "generatorVersion", path);
        var corpusFingerprint = ParseString(properties, "corpusFingerprint", path);
        var settings = ParseSettings(properties["settings"], path);
        var diagnostics = ParseArray(properties["diagnostics"], path, ParseDiagnostic);
        var sets = ParseArray(properties["sets"], path, ParseSet);

        return new TerrainCatalog(schemaVersion, generatorVersion, corpusFingerprint, settings, sets, diagnostics);
    }

    private static TerrainGenerationSettings ParseSettings(JsonElement element, string path)
    {
        RequireObject(element, path, "settings");
        var properties = EnumerateProperties(element, path, SettingProperties);
        var holdoutModulo = ParseInt(properties, "holdoutModulo", path);
        if (holdoutModulo < 2)
        {
            throw Fail(TerrainCatalogError.NumberOutOfRange, path, "Setting 'holdoutModulo' must be >= 2.");
        }

        var minimumMapSupport = ParseInt(properties, "minimumMapSupport", path);
        if (minimumMapSupport < 1)
        {
            throw Fail(TerrainCatalogError.NumberOutOfRange, path, "Setting 'minimumMapSupport' must be >= 1.");
        }

        var minimumRegionSupport = ParseInt(properties, "minimumRegionSupport", path);
        if (minimumRegionSupport < 1)
        {
            throw Fail(TerrainCatalogError.NumberOutOfRange, path, "Setting 'minimumRegionSupport' must be >= 1.");
        }

        var minimumObservationSupport = ParseInt(properties, "minimumObservationSupport", path);
        if (minimumObservationSupport < 1)
        {
            throw Fail(TerrainCatalogError.NumberOutOfRange, path, "Setting 'minimumObservationSupport' must be >= 1.");
        }

        var minimumDiagonalSupport = ParseInt(properties, "minimumDiagonalSupport", path);
        if (minimumDiagonalSupport < 1)
        {
            throw Fail(TerrainCatalogError.NumberOutOfRange, path, "Setting 'minimumDiagonalSupport' must be >= 1.");
        }

        var minimumEightWayAccuracyGain = ParseDouble(properties, "minimumEightWayAccuracyGain", path);
        if (!Within(minimumEightWayAccuracyGain, 0, 1, openLow: true))
        {
            throw Fail(TerrainCatalogError.NumberOutOfRange, path, "Setting 'minimumEightWayAccuracyGain' must be in (0,1].");
        }

        var mapMemberCompatibility = ParseDouble(properties, "mapMemberCompatibility", path);
        if (!Within01(mapMemberCompatibility))
        {
            throw Fail(TerrainCatalogError.NumberOutOfRange, path, "Setting 'mapMemberCompatibility' must be in [0,1].");
        }

        var imageOnlyCompatibility = ParseDouble(properties, "imageOnlyCompatibility", path);
        if (!Within01(imageOnlyCompatibility))
        {
            throw Fail(TerrainCatalogError.NumberOutOfRange, path, "Setting 'imageOnlyCompatibility' must be in [0,1].");
        }

        var minimumClassificationMargin = ParseDouble(properties, "minimumClassificationMargin", path);
        if (!Within(minimumClassificationMargin, 0, 1, openLow: true))
        {
            throw Fail(TerrainCatalogError.NumberOutOfRange, path, "Setting 'minimumClassificationMargin' must be in (0,1].");
        }

        var maximumAmbiguity = ParseDouble(properties, "maximumAmbiguity", path);
        if (!Within01(maximumAmbiguity))
        {
            throw Fail(TerrainCatalogError.NumberOutOfRange, path, "Setting 'maximumAmbiguity' must be in [0,1].");
        }

        var enabledConfidence = ParseDouble(properties, "enabledConfidence", path);
        if (!Within01(enabledConfidence))
        {
            throw Fail(TerrainCatalogError.NumberOutOfRange, path, "Setting 'enabledConfidence' must be in [0,1].");
        }

        var pendingConfidence = ParseDouble(properties, "pendingConfidence", path);
        if (!Within01(pendingConfidence))
        {
            throw Fail(TerrainCatalogError.NumberOutOfRange, path, "Setting 'pendingConfidence' must be in [0,1].");
        }

        if (imageOnlyCompatibility < mapMemberCompatibility)
        {
            throw Fail(TerrainCatalogError.InvalidRelationship, path, "Setting 'imageOnlyCompatibility' must be >= setting 'mapMemberCompatibility'.");
        }

        if (pendingConfidence > enabledConfidence)
        {
            throw Fail(TerrainCatalogError.InvalidRelationship, path, "Setting 'pendingConfidence' must be <= setting 'enabledConfidence'.");
        }

        return new TerrainGenerationSettings(
            holdoutModulo,
            minimumMapSupport,
            minimumRegionSupport,
            minimumObservationSupport,
            minimumDiagonalSupport,
            minimumEightWayAccuracyGain,
            mapMemberCompatibility,
            imageOnlyCompatibility,
            minimumClassificationMargin,
            maximumAmbiguity,
            enabledConfidence,
            pendingConfidence);
    }

    private static TerrainSetDefinition ParseSet(JsonElement element, string path)
    {
        RequireObject(element, path, "set");
        var properties = EnumerateProperties(element, path, SetProperties);
        var id = ParseString(properties, "id", path);
        var displayName = ParseString(properties, "displayName", path);
        var status = ParseStatus(properties, "status", path);
        var topology = ParseTopology(properties, "topology", path);
        var metrics = ParseMetrics(properties["metrics"], path);
        var masks = ParseArray(properties["masks"], path, ParseMask);
        var members = ParseArray(properties["members"], path, ParseMember);
        var diagnostics = ParseArray(properties["diagnostics"], path, ParseDiagnostic);

        return new TerrainSetDefinition(id, displayName, status, topology, metrics, masks, members, diagnostics);
    }

    private static TerrainSetMetrics ParseMetrics(JsonElement element, string path)
    {
        RequireObject(element, path, "metrics");
        var properties = EnumerateProperties(element, path, MetricProperties);
        var mapSupport = ParseInt(properties, "mapSupport", path);
        if (mapSupport < 0)
        {
            throw Fail(TerrainCatalogError.NumberOutOfRange, path, "Metric 'mapSupport' must be >= 0.");
        }

        var regionSupport = ParseInt(properties, "regionSupport", path);
        if (regionSupport < 0)
        {
            throw Fail(TerrainCatalogError.NumberOutOfRange, path, "Metric 'regionSupport' must be >= 0.");
        }

        var observationSupport = ParseInt(properties, "observationSupport", path);
        if (observationSupport < 0)
        {
            throw Fail(TerrainCatalogError.NumberOutOfRange, path, "Metric 'observationSupport' must be >= 0.");
        }

        var diagonalSupport = ParseInt(properties, "diagonalSupport", path);
        if (diagonalSupport < 0)
        {
            throw Fail(TerrainCatalogError.NumberOutOfRange, path, "Metric 'diagonalSupport' must be >= 0.");
        }

        var maskEntropy = ParseDouble(properties, "maskEntropy", path);
        if (!Within01(maskEntropy))
        {
            throw Fail(TerrainCatalogError.NumberOutOfRange, path, "Metric 'maskEntropy' must be in [0,1].");
        }

        var completeness = ParseDouble(properties, "completeness", path);
        if (!Within01(completeness))
        {
            throw Fail(TerrainCatalogError.NumberOutOfRange, path, "Metric 'completeness' must be in [0,1].");
        }

        var ambiguity = ParseDouble(properties, "ambiguity", path);
        if (!Within01(ambiguity))
        {
            throw Fail(TerrainCatalogError.NumberOutOfRange, path, "Metric 'ambiguity' must be in [0,1].");
        }

        var visualCompatibility = ParseDouble(properties, "visualCompatibility", path);
        if (!Within01(visualCompatibility))
        {
            throw Fail(TerrainCatalogError.NumberOutOfRange, path, "Metric 'visualCompatibility' must be in [0,1].");
        }

        var holdoutAccuracy = ParseDouble(properties, "holdoutAccuracy", path);
        if (!Within01(holdoutAccuracy))
        {
            throw Fail(TerrainCatalogError.NumberOutOfRange, path, "Metric 'holdoutAccuracy' must be in [0,1].");
        }

        var eightWayAccuracyGain = ParseDouble(properties, "eightWayAccuracyGain", path);
        if (eightWayAccuracyGain < -1 || eightWayAccuracyGain > 1)
        {
            throw Fail(TerrainCatalogError.NumberOutOfRange, path, "Metric 'eightWayAccuracyGain' must be in [-1,1].");
        }

        var confidence = ParseDouble(properties, "confidence", path);
        if (!Within01(confidence))
        {
            throw Fail(TerrainCatalogError.NumberOutOfRange, path, "Metric 'confidence' must be in [0,1].");
        }

        return new TerrainSetMetrics(
            mapSupport,
            regionSupport,
            observationSupport,
            diagonalSupport,
            maskEntropy,
            completeness,
            ambiguity,
            visualCompatibility,
            holdoutAccuracy,
            eightWayAccuracyGain,
            confidence);
    }

    private static TerrainMaskDefinition ParseMask(JsonElement element, string path)
    {
        RequireObject(element, path, "mask");
        var properties = EnumerateProperties(element, path, MaskProperties);
        var mask = ParseInt(properties, "mask", path);
        var variants = ParseArray(properties["variants"], path, ParseReference);

        return new TerrainMaskDefinition(mask, variants);
    }

    private static TerrainMemberDefinition ParseMember(JsonElement element, string path)
    {
        RequireObject(element, path, "member");
        var properties = EnumerateProperties(element, path, MemberProperties);
        var reference = ParseReference(properties["reference"], path);
        var provenance = ParseProvenance(properties, "provenance", path);

        return new TerrainMemberDefinition(reference, provenance);
    }

    private static TerrainDiagnostic ParseDiagnostic(JsonElement element, string path)
    {
        RequireObject(element, path, "diagnostic");
        var properties = EnumerateProperties(element, path, DiagnosticProperties);
        var code = ParseString(properties, "code", path);
        var message = ParseString(properties, "message", path);
        var mask = ParseNullableInt(properties, "mask", path);
        var reference = ParseNullableReference(properties["reference"], path);

        return new TerrainDiagnostic(code, message, mask, reference);
    }

    private static TerrainGraphicReference ParseReference(JsonElement element, string path)
    {
        RequireObject(element, path, "reference");
        var properties = EnumerateProperties(element, path, ReferenceProperties);
        var sheet = ParseInt(properties, "sheet", path);
        var graphic = ParseInt(properties, "graphic", path);

        return new TerrainGraphicReference(sheet, graphic);
    }

    private static TerrainReviewStatus ParseStatus(Dictionary<string, JsonElement> properties, string name, string path)
    {
        var value = ParseString(properties, name, path);
        switch (value)
        {
            case "enabled":
                return TerrainReviewStatus.Enabled;
            case "pending":
                return TerrainReviewStatus.Pending;
            case "disabled":
                return TerrainReviewStatus.Disabled;
            default:
                throw Fail(TerrainCatalogError.InvalidEnum, path, $"Property '{name}' has invalid value '{value}'.");
        }
    }

    private static TerrainTopology ParseTopology(Dictionary<string, JsonElement> properties, string name, string path)
    {
        var value = ParseString(properties, name, path);
        switch (value)
        {
            case "four-way":
                return TerrainTopology.FourWay;
            case "eight-way":
                return TerrainTopology.EightWay;
            default:
                throw Fail(TerrainCatalogError.InvalidEnum, path, $"Property '{name}' has invalid value '{value}'.");
        }
    }

    private static TerrainMemberProvenance ParseProvenance(Dictionary<string, JsonElement> properties, string name, string path)
    {
        var value = ParseString(properties, name, path);
        switch (value)
        {
            case "map-observed":
                return TerrainMemberProvenance.MapObserved;
            case "image-only":
                return TerrainMemberProvenance.ImageOnly;
            default:
                throw Fail(TerrainCatalogError.InvalidEnum, path, $"Property '{name}' has invalid value '{value}'.");
        }
    }

    private static Dictionary<string, JsonElement> EnumerateProperties(
        JsonElement element,
        string path,
        IReadOnlyList<string> canonicalNames)
    {
        var known = new HashSet<string>(canonicalNames, StringComparer.Ordinal);
        var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        foreach (var property in element.EnumerateObject())
        {
            if (!known.Contains(property.Name))
            {
                throw Fail(TerrainCatalogError.UnknownProperty, path, $"Unknown property '{property.Name}'.");
            }

            if (!properties.TryAdd(property.Name, property.Value))
            {
                throw Fail(TerrainCatalogError.DuplicateProperty, path, $"Duplicate property '{property.Name}'.");
            }
        }

        foreach (var name in canonicalNames)
        {
            if (!properties.ContainsKey(name))
            {
                throw Fail(TerrainCatalogError.MissingProperty, path, $"Missing property '{name}'.");
            }
        }

        return properties;
    }

    private static string ParseString(Dictionary<string, JsonElement> properties, string name, string path)
    {
        var value = properties[name];
        if (value.ValueKind == JsonValueKind.Null)
        {
            throw Fail(TerrainCatalogError.NullNotAllowed, path, $"Property '{name}' must not be null.");
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw Fail(TerrainCatalogError.InvalidPropertyType, path, $"Property '{name}' must be a string.");
        }

        return value.GetString()!;
    }

    private static int ParseInt(Dictionary<string, JsonElement> properties, string name, string path)
    {
        var value = properties[name];
        if (value.ValueKind == JsonValueKind.Null)
        {
            throw Fail(TerrainCatalogError.NullNotAllowed, path, $"Property '{name}' must not be null.");
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result))
        {
            throw Fail(TerrainCatalogError.InvalidPropertyType, path, $"Property '{name}' must be an integer.");
        }

        return result;
    }

    private static double ParseDouble(Dictionary<string, JsonElement> properties, string name, string path)
    {
        var value = properties[name];
        if (value.ValueKind == JsonValueKind.Null)
        {
            throw Fail(TerrainCatalogError.NullNotAllowed, path, $"Property '{name}' must not be null.");
        }

        if (value.ValueKind != JsonValueKind.Number)
        {
            throw Fail(TerrainCatalogError.InvalidPropertyType, path, $"Property '{name}' must be a number.");
        }

        return value.GetDouble();
    }

    private static int? ParseNullableInt(Dictionary<string, JsonElement> properties, string name, string path)
    {
        var value = properties[name];
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return ParseInt(properties, name, path);
    }

    private static TerrainGraphicReference? ParseNullableReference(JsonElement element, string path)
    {
        if (element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return ParseReference(element, path);
    }

    private static T[] ParseArray<T>(JsonElement element, string path, Func<JsonElement, string, T> parseItem)
    {
        if (element.ValueKind == JsonValueKind.Null)
        {
            throw Fail(TerrainCatalogError.NullNotAllowed, path, "Property must not be null.");
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            throw Fail(TerrainCatalogError.InvalidPropertyType, path, "Property must be an array.");
        }

        var items = new List<T>();
        foreach (var item in element.EnumerateArray())
        {
            items.Add(parseItem(item, path));
        }

        return items.ToArray();
    }

    private static void RequireObject(JsonElement element, string path, string what)
    {
        if (element.ValueKind == JsonValueKind.Null)
        {
            throw Fail(TerrainCatalogError.NullNotAllowed, path, $"{what} must not be null.");
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Fail(TerrainCatalogError.InvalidPropertyType, path, $"{what} must be an object.");
        }
    }

    private static void ValidateStructural(TerrainCatalog catalog)
    {
        if (catalog.SchemaVersion != CurrentSchemaVersion)
        {
            throw Fail(TerrainCatalogError.UnsupportedSchemaVersion, ObjectSourcePath, $"Unsupported schema version {catalog.SchemaVersion}; expected {CurrentSchemaVersion}.");
        }

        var settings = catalog.Settings;
        if (settings.HoldoutModulo < 2 || settings.MinimumMapSupport < 1 || settings.MinimumRegionSupport < 1 ||
            settings.MinimumObservationSupport < 1 || settings.MinimumDiagonalSupport < 1)
        {
            throw Fail(TerrainCatalogError.NumberOutOfRange, ObjectSourcePath, "A setting is outside its allowed range.");
        }

        if (!Within(settings.MinimumEightWayAccuracyGain, 0, 1, openLow: true) ||
            !Within(settings.MinimumClassificationMargin, 0, 1, openLow: true) ||
            !Within01(settings.MapMemberCompatibility) ||
            !Within01(settings.ImageOnlyCompatibility) ||
            !Within01(settings.MaximumAmbiguity) ||
            !Within01(settings.EnabledConfidence) ||
            !Within01(settings.PendingConfidence))
        {
            throw Fail(TerrainCatalogError.NumberOutOfRange, ObjectSourcePath, "A setting is outside its allowed range.");
        }

        if (settings.ImageOnlyCompatibility < settings.MapMemberCompatibility)
        {
            throw Fail(TerrainCatalogError.InvalidRelationship, ObjectSourcePath, "Setting 'imageOnlyCompatibility' must be >= setting 'mapMemberCompatibility'.");
        }

        if (settings.PendingConfidence > settings.EnabledConfidence)
        {
            throw Fail(TerrainCatalogError.InvalidRelationship, ObjectSourcePath, "Setting 'pendingConfidence' must be <= setting 'enabledConfidence'.");
        }

        foreach (var set in catalog.Sets)
        {
            if (set.Status is not (TerrainReviewStatus.Enabled or TerrainReviewStatus.Pending or TerrainReviewStatus.Disabled) ||
                set.Topology is not (TerrainTopology.FourWay or TerrainTopology.EightWay))
            {
                throw Fail(TerrainCatalogError.InvalidEnum, ObjectSourcePath, "A set enum value is not supported.");
            }

            var metrics = set.Metrics;
            if (metrics.MapSupport < 0 || metrics.RegionSupport < 0 || metrics.ObservationSupport < 0 || metrics.DiagonalSupport < 0 ||
                !Within01(metrics.MaskEntropy) || !Within01(metrics.Completeness) || !Within01(metrics.Ambiguity) ||
                !Within01(metrics.VisualCompatibility) || !Within01(metrics.HoldoutAccuracy) ||
                metrics.EightWayAccuracyGain < -1 || metrics.EightWayAccuracyGain > 1 ||
                !Within01(metrics.Confidence))
            {
                throw Fail(TerrainCatalogError.NumberOutOfRange, ObjectSourcePath, "A metric is outside its allowed range.");
            }
        }
    }

    private static void WriteCatalog(Utf8JsonWriter writer, TerrainCatalog catalog)
    {
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", catalog.SchemaVersion);
        writer.WriteString("generatorVersion", catalog.GeneratorVersion);
        writer.WriteString("corpusFingerprint", catalog.CorpusFingerprint);
        WriteSettings(writer, catalog.Settings);
        WriteDiagnostics(writer, "diagnostics", catalog.Diagnostics);
        writer.WritePropertyName("sets");
        writer.WriteStartArray();
        foreach (var set in catalog.Sets.OrderBy(value => value.Id, StringComparer.Ordinal))
        {
            WriteSet(writer, set);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteSettings(Utf8JsonWriter writer, TerrainGenerationSettings settings)
    {
        writer.WritePropertyName("settings");
        writer.WriteStartObject();
        writer.WriteNumber("holdoutModulo", settings.HoldoutModulo);
        writer.WriteNumber("minimumMapSupport", settings.MinimumMapSupport);
        writer.WriteNumber("minimumRegionSupport", settings.MinimumRegionSupport);
        writer.WriteNumber("minimumObservationSupport", settings.MinimumObservationSupport);
        writer.WriteNumber("minimumDiagonalSupport", settings.MinimumDiagonalSupport);
        writer.WriteNumber("minimumEightWayAccuracyGain", settings.MinimumEightWayAccuracyGain);
        writer.WriteNumber("mapMemberCompatibility", settings.MapMemberCompatibility);
        writer.WriteNumber("imageOnlyCompatibility", settings.ImageOnlyCompatibility);
        writer.WriteNumber("minimumClassificationMargin", settings.MinimumClassificationMargin);
        writer.WriteNumber("maximumAmbiguity", settings.MaximumAmbiguity);
        writer.WriteNumber("enabledConfidence", settings.EnabledConfidence);
        writer.WriteNumber("pendingConfidence", settings.PendingConfidence);
        writer.WriteEndObject();
    }

    private static void WriteSet(Utf8JsonWriter writer, TerrainSetDefinition set)
    {
        writer.WriteStartObject();
        writer.WriteString("id", set.Id);
        writer.WriteString("displayName", set.DisplayName);
        writer.WriteString("status", StatusWire(set.Status));
        writer.WriteString("topology", TopologyWire(set.Topology));
        WriteMetrics(writer, set.Metrics);
        writer.WritePropertyName("masks");
        writer.WriteStartArray();
        foreach (var mask in set.Masks.OrderBy(value => value.Mask))
        {
            WriteMask(writer, mask);
        }

        writer.WriteEndArray();
        writer.WritePropertyName("members");
        writer.WriteStartArray();
        foreach (var member in set.Members
                     .OrderBy(value => value.Reference.Sheet)
                     .ThenBy(value => value.Reference.Graphic))
        {
            WriteMember(writer, member);
        }

        writer.WriteEndArray();
        WriteDiagnostics(writer, "diagnostics", set.Diagnostics);
        writer.WriteEndObject();
    }

    private static void WriteMetrics(Utf8JsonWriter writer, TerrainSetMetrics metrics)
    {
        writer.WritePropertyName("metrics");
        writer.WriteStartObject();
        writer.WriteNumber("mapSupport", metrics.MapSupport);
        writer.WriteNumber("regionSupport", metrics.RegionSupport);
        writer.WriteNumber("observationSupport", metrics.ObservationSupport);
        writer.WriteNumber("diagonalSupport", metrics.DiagonalSupport);
        writer.WriteNumber("maskEntropy", metrics.MaskEntropy);
        writer.WriteNumber("completeness", metrics.Completeness);
        writer.WriteNumber("ambiguity", metrics.Ambiguity);
        writer.WriteNumber("visualCompatibility", metrics.VisualCompatibility);
        writer.WriteNumber("holdoutAccuracy", metrics.HoldoutAccuracy);
        writer.WriteNumber("eightWayAccuracyGain", metrics.EightWayAccuracyGain);
        writer.WriteNumber("confidence", metrics.Confidence);
        writer.WriteEndObject();
    }

    private static void WriteMask(Utf8JsonWriter writer, TerrainMaskDefinition mask)
    {
        writer.WriteStartObject();
        writer.WriteNumber("mask", mask.Mask);
        writer.WritePropertyName("variants");
        writer.WriteStartArray();
        foreach (var variant in mask.Variants)
        {
            WriteReference(writer, variant);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteMember(Utf8JsonWriter writer, TerrainMemberDefinition member)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("reference");
        WriteReference(writer, member.Reference);
        writer.WriteString("provenance", ProvenanceWire(member.Provenance));
        writer.WriteEndObject();
    }

    private static void WriteReference(Utf8JsonWriter writer, TerrainGraphicReference reference)
    {
        writer.WriteStartObject();
        writer.WriteNumber("sheet", reference.Sheet);
        writer.WriteNumber("graphic", reference.Graphic);
        writer.WriteEndObject();
    }

    private static void WriteDiagnostics(Utf8JsonWriter writer, string name, IReadOnlyList<TerrainDiagnostic> diagnostics)
    {
        writer.WritePropertyName(name);
        writer.WriteStartArray();
        foreach (var diagnostic in diagnostics.OrderBy(value => value, DiagnosticComparer))
        {
            writer.WriteStartObject();
            writer.WriteString("code", diagnostic.Code);
            writer.WriteString("message", diagnostic.Message);
            if (diagnostic.Mask is null)
            {
                writer.WriteNull("mask");
            }
            else
            {
                writer.WriteNumber("mask", diagnostic.Mask.Value);
            }

            if (diagnostic.Reference is null)
            {
                writer.WriteNull("reference");
            }
            else
            {
                writer.WritePropertyName("reference");
                WriteReference(writer, diagnostic.Reference.Value);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static string StatusWire(TerrainReviewStatus status) => status switch
    {
        TerrainReviewStatus.Enabled => "enabled",
        TerrainReviewStatus.Pending => "pending",
        TerrainReviewStatus.Disabled => "disabled",
        _ => throw Fail(TerrainCatalogError.InvalidEnum, ObjectSourcePath, "Review status is not supported.")
    };

    private static string TopologyWire(TerrainTopology topology) => topology switch
    {
        TerrainTopology.FourWay => "four-way",
        TerrainTopology.EightWay => "eight-way",
        _ => throw Fail(TerrainCatalogError.InvalidEnum, ObjectSourcePath, "Topology is not supported.")
    };

    private static string ProvenanceWire(TerrainMemberProvenance provenance) => provenance switch
    {
        TerrainMemberProvenance.MapObserved => "map-observed",
        TerrainMemberProvenance.ImageOnly => "image-only",
        _ => throw Fail(TerrainCatalogError.InvalidEnum, ObjectSourcePath, "Provenance is not supported.")
    };

    private static bool Within01(double value) => !double.IsNaN(value) && value >= 0 && value <= 1;

    private static bool Within(double value, double low, double high, bool openLow) =>
        !double.IsNaN(value) && (openLow ? value > low : value >= low) && value <= high;

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

    private static TerrainCatalogException Fail(TerrainCatalogError error, string path, string message) =>
        new(error, path, message);
}
