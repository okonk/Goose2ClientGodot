using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MapEditor.Core.Terrain;

namespace MapEditor.Rendering.Terrain;

public sealed class TerrainAssetCatalog
{
    private const string CatalogFileName = "terrain-brushes.json";

    private const string NoEnabledSetsDiagnostic =
        "Terrain catalog has no enabled terrain sets. Open Edit > Terrain Sets… to enable a complete set.";

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

        result = CompareReference(a.Reference, b.Reference);
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

    public TerrainCatalog Source { get; }
    public TerrainMapResolver Resolver { get; }
    public IReadOnlyList<TerrainSetDefinition> EnabledSets { get; }
    public TerrainGraphicReference? Representative { get; }

    private TerrainAssetCatalog(
        TerrainCatalog source,
        TerrainMapResolver resolver,
        IReadOnlyList<TerrainSetDefinition> enabledSets,
        TerrainGraphicReference? representative)
    {
        Source = source;
        Resolver = resolver;
        EnabledSets = enabledSets is null
            ? throw new ArgumentNullException(nameof(enabledSets))
            : enabledSets.ToList().AsReadOnly();
        Representative = representative;
    }

    public static TerrainAssetLoadResult Load(string assetDirectory, SpriteManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(assetDirectory))
        {
            throw new ArgumentException("Asset directory is required.", nameof(assetDirectory));
        }

        ArgumentNullException.ThrowIfNull(manifest);

        string path = Path.Combine(Path.GetFullPath(assetDirectory), CatalogFileName);
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or UnauthorizedAccessException or IOException)
        {
            return TerrainAssetLoadResult.Unavailable($"Terrain catalog could not be read: {path}: {ex.Message}");
        }

        TerrainCatalog catalog;
        try
        {
            catalog = TerrainCatalogJson.Parse(json, path);
        }
        catch (TerrainCatalogException ex)
        {
            return TerrainAssetLoadResult.Unavailable($"Terrain catalog is invalid ({ex.Error}): {path}: {ex.Message}");
        }

        return Validate(catalog, manifest);
    }

    public static TerrainAssetLoadResult Validate(TerrainCatalog catalog, SpriteManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(manifest);

        List<TerrainValidationIssue> coreIssues = new(TerrainCatalogValidator.Validate(catalog));
        List<TerrainValidationIssue> frameIssues = ValidateEnabledFrames(catalog, manifest);

        List<TerrainValidationIssue> issues = new(coreIssues.Count + frameIssues.Count);
        issues.AddRange(coreIssues);
        issues.AddRange(frameIssues);
        issues.Sort(IssueComparer);
        for (var i = issues.Count - 1; i > 0; i--)
        {
            if (issues[i].Equals(issues[i - 1]))
            {
                issues.RemoveAt(i);
            }
        }

        IReadOnlyList<TerrainValidationIssue> allIssues = issues.AsReadOnly();

        if (coreIssues.Count > 0)
        {
            return new TerrainAssetLoadResult(
                catalog,
                null,
                new TerrainAvailability(
                    false,
                    false,
                    $"Terrain catalog has {coreIssues.Count} validation issue(s); the terrain tool is unavailable."),
                allIssues);
        }

        if (frameIssues.Count > 0)
        {
            return new TerrainAssetLoadResult(
                catalog,
                null,
                new TerrainAvailability(
                    true,
                    false,
                    $"Terrain catalog has {frameIssues.Count} sprite frame issue(s); the terrain tool is unavailable."),
                allIssues);
        }

        List<TerrainSetDefinition> enabledSets = catalog.Sets
            .Where(set => set.Status == TerrainReviewStatus.Enabled)
            .OrderBy(set => set.DisplayName, StringComparer.Ordinal)
            .ThenBy(set => set.Id, StringComparer.Ordinal)
            .ToList();

        TerrainMapResolver resolver = new(catalog);

        if (enabledSets.Count == 0)
        {
            return new TerrainAssetLoadResult(
                catalog,
                new TerrainAssetCatalog(catalog, resolver, enabledSets, null),
                new TerrainAvailability(true, false, NoEnabledSetsDiagnostic),
                allIssues);
        }

        return new TerrainAssetLoadResult(
            catalog,
            new TerrainAssetCatalog(catalog, resolver, enabledSets, FirstMaskZeroVariant(enabledSets[0])),
            new TerrainAvailability(true, true, null),
            allIssues);
    }

    private static List<TerrainValidationIssue> ValidateEnabledFrames(TerrainCatalog catalog, SpriteManifest manifest)
    {
        var issues = new List<TerrainValidationIssue>();

        foreach (var set in catalog.Sets)
        {
            if (set.Status != TerrainReviewStatus.Enabled)
            {
                continue;
            }

            var references = new HashSet<TerrainGraphicReference>();
            foreach (var member in set.Members)
            {
                references.Add(member.Reference);
            }

            foreach (var mask in set.Masks)
            {
                foreach (var variant in mask.Variants)
                {
                    references.Add(variant);
                }
            }

            foreach (var reference in references)
            {
                if (!manifest.TryGetSourceRect(new SpriteReference(reference.Sheet, reference.Graphic), out SpriteSourceRect rect))
                {
                    issues.Add(new TerrainValidationIssue(
                        "terrain-frame-missing",
                        $"Enabled terrain '{set.Id}' reference ({reference.Sheet},{reference.Graphic}) is absent from sprite manifest.",
                        set.Id,
                        null,
                        reference));
                }
                else if (rect.Width != SpriteManifest.RequiredTileSize || rect.Height != SpriteManifest.RequiredTileSize)
                {
                    issues.Add(new TerrainValidationIssue(
                        "terrain-frame-size",
                        $"Enabled terrain '{set.Id}' reference ({reference.Sheet},{reference.Graphic}) is {rect.Width}x{rect.Height}; expected 32x32.",
                        set.Id,
                        null,
                        reference));
                }
            }
        }

        return issues;
    }

    private static TerrainGraphicReference? FirstMaskZeroVariant(TerrainSetDefinition set)
    {
        foreach (var mask in set.Masks)
        {
            if (mask.Mask == 0 && mask.Variants.Count > 0)
            {
                return mask.Variants[0];
            }
        }

        return null;
    }

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

    private static int CompareReference(TerrainGraphicReference? a, TerrainGraphicReference? b)
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

public sealed record TerrainAssetLoadResult
{
    public TerrainCatalog? Source { get; }
    public TerrainAssetCatalog? Runtime { get; }
    public TerrainAvailability Availability { get; }
    public IReadOnlyList<TerrainValidationIssue> Issues { get; }

    public TerrainAssetLoadResult(
        TerrainCatalog? source,
        TerrainAssetCatalog? runtime,
        TerrainAvailability availability,
        IEnumerable<TerrainValidationIssue> issues)
    {
        Source = source;
        Runtime = runtime;
        Availability = availability;
        Issues = issues is null
            ? throw new ArgumentNullException(nameof(issues))
            : issues.ToList().AsReadOnly();
    }

    public static TerrainAssetLoadResult Unavailable(string diagnostic)
    {
        if (string.IsNullOrWhiteSpace(diagnostic))
        {
            throw new ArgumentException("Diagnostic is required.", nameof(diagnostic));
        }

        return new(null, null, new TerrainAvailability(false, false, diagnostic), Array.Empty<TerrainValidationIssue>());
    }
}
