using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MapEditor.Core;

namespace MapEditor.Rendering;

public static class TerrainAssetCatalog
{
    public const string FileName = "terrain-brushes.json";

    public static TerrainCatalogValidationResult Validate(TerrainCatalog catalog, SpriteManifest manifest)
    {
        var core = TerrainCatalogValidator.Validate(catalog);
        var manifestIssues = new List<TerrainValidationIssue>();

        foreach (var reference in catalog.Graphics
            .Select(graphic => graphic.Reference)
            .Distinct()
            .OrderBy(graphic => graphic.Sheet)
            .ThenBy(graphic => graphic.Graphic))
        {
            if (!manifest.TryGetSourceRect(new SpriteReference(reference.Sheet, reference.Graphic), out var rect))
            {
                manifestIssues.Add(new TerrainValidationIssue(
                    TerrainValidationSeverity.Error,
                    TerrainValidationCode.MissingSpriteFrame,
                    $"Graphic ({reference.Sheet}, {reference.Graphic}) is not declared in the sprite manifest.",
                    GraphicReference: reference));
            }
            else if (rect.Width != SpriteManifest.RequiredTileSize || rect.Height != SpriteManifest.RequiredTileSize)
            {
                manifestIssues.Add(new TerrainValidationIssue(
                    TerrainValidationSeverity.Error,
                    TerrainValidationCode.SpriteFrameSizeMismatch,
                    $"Graphic ({reference.Sheet}, {reference.Graphic}) frame is {rect.Width}x{rect.Height}; expected {SpriteManifest.RequiredTileSize}x{SpriteManifest.RequiredTileSize}.",
                    GraphicReference: reference));
            }
        }

        var issues = core.Issues.Concat(manifestIssues).ToList();
        var hasErrors = issues.Any(issue => issue.Severity == TerrainValidationSeverity.Error);
        return new TerrainCatalogValidationResult(issues, hasErrors ? null : core.Index);
    }

    public static TerrainCatalogLoadResult Load(string assetDirectory, SpriteManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(assetDirectory))
        {
            throw new ArgumentException("Asset directory is required.", nameof(assetDirectory));
        }

        if (manifest is null)
        {
            throw new ArgumentNullException(nameof(manifest));
        }

        var sourcePath = Path.Combine(Path.GetFullPath(assetDirectory), FileName);

        if (!Directory.Exists(assetDirectory))
        {
            return TerrainCatalogLoadResult.Unavailable(sourcePath);
        }

        if (Directory.Exists(sourcePath))
        {
            return TerrainCatalogLoadResult.Invalid(
                sourcePath,
                TerrainFileRevision.Missing,
                Array.Empty<TerrainValidationIssue>(),
                $"Terrain path is a directory: {sourcePath}");
        }

        if (!File.Exists(sourcePath))
        {
            return TerrainCatalogLoadResult.ValidEmpty(sourcePath);
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(sourcePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return TerrainCatalogLoadResult.Invalid(
                sourcePath,
                TerrainFileRevision.Missing,
                Array.Empty<TerrainValidationIssue>(),
                $"Failed to read terrain file: {sourcePath}: {ex.Message}");
        }

        var revision = TerrainFileRevision.FromBytes(bytes);

        TerrainCatalog catalog;
        try
        {
            catalog = TerrainCatalogJson.Parse(Encoding.UTF8.GetString(bytes), sourcePath);
        }
        catch (TerrainCatalogFormatException ex)
        {
            return TerrainCatalogLoadResult.Invalid(
                sourcePath,
                revision,
                Array.Empty<TerrainValidationIssue>(),
                ex.Message);
        }

        var validation = Validate(catalog, manifest);
        if (!validation.IsValid)
        {
            var errors = validation.Issues.Count(issue => issue.Severity == TerrainValidationSeverity.Error);
            var first = validation.Issues.First(issue => issue.Severity == TerrainValidationSeverity.Error);
            return TerrainCatalogLoadResult.Invalid(
                sourcePath,
                revision,
                validation.Issues,
                $"Terrain validation failed ({errors} error(s)) in {sourcePath}: {first.Message}");
        }

        return TerrainCatalogLoadResult.Valid(sourcePath, revision, catalog, validation.Index, validation.Issues);
    }
}
