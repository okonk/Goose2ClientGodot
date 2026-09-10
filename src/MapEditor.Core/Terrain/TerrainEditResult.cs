namespace MapEditor.Core.Terrain;

public enum TerrainEditMode
{
    Paint,
    Erase
}

public sealed record TerrainEditFailure(string TerrainId, int Mask);

public readonly record struct TerrainStrokeUpdate(
    bool Succeeded,
    bool Changed,
    TerrainEditFailure? Failure);
