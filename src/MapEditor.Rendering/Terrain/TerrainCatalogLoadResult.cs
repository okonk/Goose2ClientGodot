using System;
using System.Collections.Generic;
using MapEditor.Core;

namespace MapEditor.Rendering;

public sealed class TerrainCatalogLoadResult
{
    public string SourcePath { get; }
    public TerrainFileRevision Revision { get; }
    public TerrainCatalog? Catalog { get; }
    public TerrainCatalogIndex? Index { get; }
    public IReadOnlyList<TerrainValidationIssue> Issues { get; }
    public bool IsValid { get; }
    public bool CanAuthor { get; }
    public bool CanPaint { get; }
    public string? Diagnostic { get; }

    private TerrainCatalogLoadResult(
        string sourcePath,
        TerrainFileRevision revision,
        TerrainCatalog? catalog,
        TerrainCatalogIndex? index,
        IReadOnlyList<TerrainValidationIssue> issues,
        bool isValid,
        bool canAuthor,
        bool canPaint,
        string? diagnostic)
    {
        SourcePath = sourcePath;
        Revision = revision;
        Catalog = catalog;
        Index = index;
        Issues = issues;
        IsValid = isValid;
        CanAuthor = canAuthor;
        CanPaint = canPaint;
        Diagnostic = diagnostic;
    }

    public static TerrainCatalogLoadResult Unavailable(string sourcePath)
        => new(
            sourcePath,
            TerrainFileRevision.Missing,
            null,
            null,
            Array.Empty<TerrainValidationIssue>(),
            isValid: false,
            canAuthor: false,
            canPaint: false,
            $"Asset root unavailable: {sourcePath}");

    public static TerrainCatalogLoadResult ValidEmpty(string sourcePath)
        => new(
            sourcePath,
            TerrainFileRevision.Missing,
            null,
            null,
            Array.Empty<TerrainValidationIssue>(),
            isValid: true,
            canAuthor: true,
            canPaint: false,
            null);

    public static TerrainCatalogLoadResult Invalid(
        string sourcePath,
        TerrainFileRevision revision,
        IReadOnlyList<TerrainValidationIssue> issues,
        string diagnostic)
        => new(
            sourcePath,
            revision,
            null,
            null,
            issues,
            isValid: false,
            canAuthor: true,
            canPaint: false,
            diagnostic);

    public static TerrainCatalogLoadResult Valid(
        string sourcePath,
        TerrainFileRevision revision,
        TerrainCatalog catalog,
        TerrainCatalogIndex? index,
        IReadOnlyList<TerrainValidationIssue> issues)
        => new(
            sourcePath,
            revision,
            catalog,
            index,
            issues,
            isValid: true,
            canAuthor: true,
            canPaint: catalog.Graphics.Count > 0,
            null);
}
