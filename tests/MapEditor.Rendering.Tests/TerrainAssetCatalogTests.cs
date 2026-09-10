using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MapEditor.Core.Terrain;
using MapEditor.Rendering;
using MapEditor.Rendering.Tests.Fakes;
using MapEditor.Rendering.Terrain;
using Xunit;

namespace MapEditor.Rendering.Tests;

public class TerrainAssetCatalogTests
{
    private const string Fingerprint =
        "sha256:abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";

    private const string ManifestJson = """
        { "tileSize": 32, "sheets": {
          "1": { "101": [0, 0, 32, 32], "102": [32, 0, 32, 32], "103": [0, 32, 32, 32] },
          "2": { "201": [0, 0, 32, 32] }
        } }
        """;

    private const string PartialManifestJson = """
        { "tileSize": 32, "sheets": { "1": { "101": [0, 0, 32, 32], "102": [32, 0, 16, 16] } } }
        """;

    [Fact]
    public void Load_NullBlankDirectoryAndNullManifestUseExactArguments()
    {
        SpriteManifest manifest = SpriteManifest.Parse(ManifestJson);

        ArgumentException directoryException = Assert.Throws<ArgumentException>(() => TerrainAssetCatalog.Load(null!, manifest));
        Assert.Equal("assetDirectory", directoryException.ParamName);

        ArgumentException blankException = Assert.Throws<ArgumentException>(() => TerrainAssetCatalog.Load("   ", manifest));
        Assert.Equal("assetDirectory", blankException.ParamName);

        ArgumentNullException manifestException = Assert.Throws<ArgumentNullException>(() => TerrainAssetCatalog.Load("/tmp/terrain-assets", null!));
        Assert.Equal("manifest", manifestException.ParamName);
    }

    [Fact]
    public void Validate_NullCatalogOrManifestUsesExactArguments()
    {
        SpriteManifest manifest = SpriteManifest.Parse(ManifestJson);
        TerrainCatalog catalog = CreateCatalog();

        ArgumentNullException catalogException = Assert.Throws<ArgumentNullException>(() => TerrainAssetCatalog.Validate(null!, manifest));
        Assert.Equal("catalog", catalogException.ParamName);

        ArgumentNullException manifestException = Assert.Throws<ArgumentNullException>(() => TerrainAssetCatalog.Validate(catalog, null!));
        Assert.Equal("manifest", manifestException.ParamName);
    }

    [Fact]
    public void Unavailable_BlankDiagnosticIsRejected()
    {
        ArgumentException nullException = Assert.Throws<ArgumentException>(() => TerrainAssetLoadResult.Unavailable(null!));
        Assert.Equal("diagnostic", nullException.ParamName);

        ArgumentException blankException = Assert.Throws<ArgumentException>(() => TerrainAssetLoadResult.Unavailable("  "));
        Assert.Equal("diagnostic", blankException.ParamName);
    }

    [Fact]
    public void Load_MissingUnreadableMalformedOrUnsupported_ReturnsActionablePathAndExactError()
    {
        using Fixture fixture = new();
        SpriteManifest manifest = SpriteManifest.Parse(ManifestJson);
        string path = Path.GetFullPath(Path.Combine(fixture.AssetRoot, "terrain-brushes.json"));

        TerrainAssetLoadResult missing = TerrainAssetCatalog.Load(fixture.AssetRoot, manifest);
        Assert.False(missing.Availability.IsCatalogValid);
        Assert.False(missing.Availability.IsToolAvailable);
        Assert.Null(missing.Source);
        Assert.Null(missing.Runtime);
        Assert.Empty(missing.Issues);
        Assert.Contains(path, missing.Availability.Diagnostic);

        File.WriteAllText(path, "not json");
        TerrainAssetLoadResult malformed = TerrainAssetCatalog.Load(fixture.AssetRoot, manifest);
        Assert.Null(malformed.Source);
        Assert.Null(malformed.Runtime);
        Assert.Contains("MalformedJson", malformed.Availability.Diagnostic);
        Assert.Contains(path, malformed.Availability.Diagnostic);

        string json = TerrainCatalogJson.Serialize(
            CreateCatalog(CreateSet("Grass", TerrainReviewStatus.Enabled, TerrainTopology.FourWay, (1, 101))));
        File.WriteAllText(path, json.Replace("\"schemaVersion\":1", "\"schemaVersion\":2"));
        TerrainAssetLoadResult unsupported = TerrainAssetCatalog.Load(fixture.AssetRoot, manifest);
        Assert.Null(unsupported.Source);
        Assert.Null(unsupported.Runtime);
        Assert.Contains("UnsupportedSchemaVersion", unsupported.Availability.Diagnostic);
        Assert.Contains(path, unsupported.Availability.Diagnostic);

        File.Delete(path);
        Directory.CreateDirectory(path);
        TerrainAssetLoadResult unreadable = TerrainAssetCatalog.Load(fixture.AssetRoot, manifest);
        Assert.Null(unreadable.Source);
        Assert.Null(unreadable.Runtime);
        Assert.Contains(path, unreadable.Availability.Diagnostic);
    }

    [Fact]
    public void Validate_CompleteEnabledSetWithExactFrames_BuildsResolverAndSortedPaletteSets()
    {
        SpriteManifest manifest = SpriteManifest.Parse(ManifestJson);
        TerrainSetDefinition alpha = CreateSet("Alpha", TerrainReviewStatus.Enabled, TerrainTopology.FourWay, (1, 101), (1, 102));
        TerrainSetDefinition beta = CreateSet("Beta", TerrainReviewStatus.Enabled, TerrainTopology.FourWay, (2, 201));
        TerrainSetDefinition gamma = CreateSet("Gamma", TerrainReviewStatus.Enabled, TerrainTopology.EightWay, (1, 103));
        TerrainCatalog catalog = CreateCatalog(gamma, beta, alpha);

        TerrainAssetLoadResult result = TerrainAssetCatalog.Validate(catalog, manifest);

        Assert.True(result.Availability.IsCatalogValid);
        Assert.True(result.Availability.IsToolAvailable);
        Assert.Null(result.Availability.Diagnostic);
        Assert.Empty(result.Issues);
        Assert.Same(catalog, result.Source);
        TerrainAssetCatalog runtime = result.Runtime!;
        Assert.Same(catalog, runtime.Source);
        Assert.NotNull(runtime.Resolver);
        Assert.Equal(new[] { alpha, beta, gamma }, runtime.EnabledSets);
    }

    [Fact]
    public void Validate_RepresentativeIsFirstMaskZeroVariant()
    {
        SpriteManifest manifest = SpriteManifest.Parse(ManifestJson);
        TerrainSetDefinition grass = CreateSet("Grass", TerrainReviewStatus.Enabled, TerrainTopology.FourWay, (1, 102), (1, 101));

        TerrainAssetLoadResult result = TerrainAssetCatalog.Validate(CreateCatalog(grass), manifest);

        Assert.Equal(new TerrainGraphicReference(1, 102), result.Runtime!.Representative);

        TerrainAssetLoadResult empty = TerrainAssetCatalog.Validate(CreateCatalog(), manifest);
        Assert.Null(empty.Runtime!.Representative);
    }

    [Fact]
    public void Validate_ValidCatalogWithNoEnabledSets_IsValidButToolUnavailable()
    {
        SpriteManifest manifest = SpriteManifest.Parse(ManifestJson);
        TerrainCatalog catalog = CreateCatalog(
            CreateSet("Pending", TerrainReviewStatus.Pending, TerrainTopology.FourWay, (1, 101)),
            CreateSet("Disabled", TerrainReviewStatus.Disabled, TerrainTopology.FourWay, (2, 201)));

        TerrainAssetLoadResult result = TerrainAssetCatalog.Validate(catalog, manifest);

        Assert.True(result.Availability.IsCatalogValid);
        Assert.False(result.Availability.IsToolAvailable);
        Assert.Equal(
            "Terrain catalog has no enabled terrain sets. Open Edit > Terrain Sets… to enable a complete set.",
            result.Availability.Diagnostic);
        Assert.Empty(result.Issues);
        Assert.Same(catalog, result.Source);
        TerrainAssetCatalog runtime = result.Runtime!;
        Assert.Empty(runtime.EnabledSets);
        Assert.NotNull(runtime.Resolver);
        Assert.Null(runtime.Representative);
    }

    [Fact]
    public void Validate_EnabledMissingFrameOrNon32Frame_ReturnsExactIssuesAndNoRuntime()
    {
        SpriteManifest manifest = SpriteManifest.Parse(PartialManifestJson);
        TerrainSetDefinition grass = CreateSet("Grass", TerrainReviewStatus.Enabled, TerrainTopology.FourWay, (1, 101), (1, 102), (1, 103));
        TerrainCatalog catalog = CreateCatalog(grass);

        TerrainAssetLoadResult result = TerrainAssetCatalog.Validate(catalog, manifest);

        Assert.True(result.Availability.IsCatalogValid);
        Assert.False(result.Availability.IsToolAvailable);
        Assert.NotNull(result.Availability.Diagnostic);
        Assert.Same(catalog, result.Source);
        Assert.Null(result.Runtime);
        Assert.Equal(new[]
        {
            new TerrainValidationIssue(
                "terrain-frame-size",
                $"Enabled terrain '{grass.Id}' reference (1,102) is 16x16; expected 32x32.",
                grass.Id,
                null,
                new TerrainGraphicReference(1, 102)),
            new TerrainValidationIssue(
                "terrain-frame-missing",
                $"Enabled terrain '{grass.Id}' reference (1,103) is absent from sprite manifest.",
                grass.Id,
                null,
                new TerrainGraphicReference(1, 103))
        }, result.Issues);
    }

    [Fact]
    public void Validate_PendingAndDisabledUnresolvedFrames_RemainReviewable()
    {
        SpriteManifest manifest = SpriteManifest.Parse("""{ "tileSize": 32, "sheets": {} }""");
        TerrainCatalog catalog = CreateCatalog(
            CreateSet("Pending", TerrainReviewStatus.Pending, TerrainTopology.FourWay, (9, 999)),
            CreateSet("Disabled", TerrainReviewStatus.Disabled, TerrainTopology.FourWay, (9, 998)));

        TerrainAssetLoadResult result = TerrainAssetCatalog.Validate(catalog, manifest);

        Assert.True(result.Availability.IsCatalogValid);
        Assert.False(result.Availability.IsToolAvailable);
        Assert.Empty(result.Issues);
        Assert.Same(catalog, result.Source);
        Assert.NotNull(result.Runtime);
    }

    [Fact]
    public void Validate_DuplicateEnabledMembershipReturnsNoRuntime()
    {
        SpriteManifest manifest = SpriteManifest.Parse(ManifestJson);
        TerrainCatalog catalog = CreateCatalog(
            CreateSet("Alpha", TerrainReviewStatus.Enabled, TerrainTopology.FourWay, (1, 101), (1, 102)),
            CreateSet("Beta", TerrainReviewStatus.Enabled, TerrainTopology.FourWay, (1, 101), (1, 103)));

        TerrainAssetLoadResult result = TerrainAssetCatalog.Validate(catalog, manifest);

        Assert.False(result.Availability.IsCatalogValid);
        Assert.False(result.Availability.IsToolAvailable);
        Assert.NotNull(result.Availability.Diagnostic);
        Assert.Null(result.Runtime);
        Assert.Contains(result.Issues, issue => issue.Code == "enabled-member-conflict");
        Assert.DoesNotContain(result.Issues, issue => issue.Code is "terrain-frame-missing" or "terrain-frame-size");
    }

    [Fact]
    public void Validate_VariantFrameExistsButMemberFrameDoesNot_IsRejected()
    {
        SpriteManifest manifest = SpriteManifest.Parse(PartialManifestJson);
        TerrainGraphicReference[] references =
        {
            new(1, 101),
            new(1, 104)
        };
        string id = TerrainGeneratedId.Create(TerrainTopology.FourWay, references);
        List<TerrainMaskDefinition> masks = new();
        foreach (int mask in TerrainMasks.Required(TerrainTopology.FourWay))
        {
            masks.Add(new TerrainMaskDefinition(mask, mask == 5 ? references : new[] { references[0] }));
        }

        TerrainSetDefinition set = new(
            id,
            "Grass",
            TerrainReviewStatus.Enabled,
            TerrainTopology.FourWay,
            Metrics(),
            masks,
            references.Select(reference => new TerrainMemberDefinition(reference, TerrainMemberProvenance.MapObserved)),
            Array.Empty<TerrainDiagnostic>());

        TerrainAssetLoadResult result = TerrainAssetCatalog.Validate(CreateCatalog(set), manifest);

        Assert.True(result.Availability.IsCatalogValid);
        Assert.False(result.Availability.IsToolAvailable);
        Assert.Null(result.Runtime);
        Assert.Equal(new[]
        {
            new TerrainValidationIssue(
                "terrain-frame-missing",
                $"Enabled terrain '{id}' reference (1,104) is absent from sprite manifest.",
                id,
                null,
                references[1])
        }, result.Issues);
    }

    [Fact]
    public void Load_DoesNotInvokeSpriteSheetLoader()
    {
        using Fixture fixture = new();
        File.WriteAllText(Path.Combine(fixture.AssetRoot, "manifest.json"), ManifestJson);
        FakeSpriteSheetLoader loader = new();
        using SpriteAssetCache cache = SpriteAssetCache.Open(fixture.AssetRoot, loader);
        Assert.Equal(0, loader.CallCount);

        TerrainCatalog catalog = CreateCatalog(
            CreateSet("Grass", TerrainReviewStatus.Enabled, TerrainTopology.FourWay, (1, 101), (1, 102)));
        File.WriteAllText(
            Path.Combine(fixture.AssetRoot, "terrain-brushes.json"),
            TerrainCatalogJson.Serialize(catalog));

        TerrainAssetLoadResult result = TerrainAssetCatalog.Load(fixture.AssetRoot, cache.Manifest!);

        Assert.True(result.Availability.IsCatalogValid);
        Assert.True(result.Availability.IsToolAvailable);
        Assert.NotNull(result.Runtime);
        Assert.Equal(0, loader.CallCount);
    }

    private static TerrainGenerationSettings Settings() =>
        new(2, 1, 1, 1, 1, 0.1, 0.5, 0.5, 0.1, 0.5, 0.9, 0.9);

    private static TerrainSetMetrics Metrics() =>
        new(1, 1, 1, 1, 0.0, 1.0, 0.0, 1.0, 1.0, 0.0, 1.0);

    private static TerrainSetDefinition CreateSet(
        string displayName,
        TerrainReviewStatus status,
        TerrainTopology topology,
        params (int Sheet, int Graphic)[] references)
    {
        List<TerrainGraphicReference> memberReferences = references
            .Select(reference => new TerrainGraphicReference(reference.Sheet, reference.Graphic))
            .ToList();
        string id = TerrainGeneratedId.Create(topology, memberReferences);
        List<TerrainMaskDefinition> masks = new();
        foreach (int mask in TerrainMasks.Required(topology))
        {
            masks.Add(new TerrainMaskDefinition(mask, mask == 0 ? memberReferences : new[] { memberReferences[0] }));
        }

        return new TerrainSetDefinition(
            id,
            displayName,
            status,
            topology,
            Metrics(),
            masks,
            memberReferences.Select(reference => new TerrainMemberDefinition(reference, TerrainMemberProvenance.MapObserved)),
            Array.Empty<TerrainDiagnostic>());
    }

    private static TerrainCatalog CreateCatalog(params TerrainSetDefinition[] sets) =>
        new(
            TerrainCatalogJson.CurrentSchemaVersion,
            "test-generator",
            Fingerprint,
            Settings(),
            sets,
            Array.Empty<TerrainDiagnostic>());

    private sealed class Fixture : IDisposable
    {
        public Fixture()
        {
            AssetRoot = Path.Combine(Path.GetTempPath(), "terrain-asset-catalog-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(AssetRoot);
        }

        public string AssetRoot { get; }

        public void Dispose()
            => Directory.Delete(AssetRoot, recursive: true);
    }
}
