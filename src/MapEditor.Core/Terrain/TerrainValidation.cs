using System;
using System.Collections.Generic;
using System.Linq;

namespace MapEditor.Core;

public enum TerrainValidationSeverity
{
    Error,
    Warning
}

public enum TerrainValidationCode
{
    EmptyTerrainId,
    DuplicateTerrainId,
    DuplicateTerrainName,
    CenterlessGraphic,
    UnknownPeer,
    MissingCenteredGraphic,
    DuplicateGraphicReference,
    InvalidGraphicNumber,
    InvalidSheetNumber,
    MissingCoveragePattern,
    MissingSpriteFrame,
    SpriteFrameSizeMismatch
}

public sealed record TerrainValidationIssue(
    TerrainValidationSeverity Severity,
    TerrainValidationCode Code,
    string Message,
    Guid? TerrainId = null,
    TerrainGraphicReference? GraphicReference = null,
    TerrainPeer? Peer = null);

public sealed record TerrainCatalogValidationResult(
    IReadOnlyList<TerrainValidationIssue> Issues,
    TerrainCatalogIndex? Index)
{
    public bool IsValid { get; } = Issues.All(issue => issue.Severity != TerrainValidationSeverity.Error);
}
