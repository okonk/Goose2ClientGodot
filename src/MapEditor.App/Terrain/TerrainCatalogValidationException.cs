using System;
using System.Linq;
using MapEditor.Core;

namespace MapEditor.App.Terrain;

public sealed class TerrainCatalogValidationException : Exception
{
    public IReadOnlyList<TerrainValidationIssue> Issues { get; }

    public TerrainCatalogValidationException(IReadOnlyList<TerrainValidationIssue> issues)
        : base($"Terrain catalog validation failed ({issues.Count(issue => issue.Severity == TerrainValidationSeverity.Error)} error(s)).")
    {
        Issues = issues;
    }
}
