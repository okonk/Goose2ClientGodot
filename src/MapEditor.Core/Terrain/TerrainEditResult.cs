using System;

namespace MapEditor.Core;

public enum TerrainEditMode
{
    Paint,
    Erase
}

public sealed record TerrainResolutionFailure(
    Guid? TerrainId,
    int X,
    int Y,
    string Message);

public readonly record struct TerrainEditResult(
    bool IsActive,
    bool Changed,
    TerrainResolutionFailure? Failure)
{
    public bool Succeeded => Failure is null;
}
