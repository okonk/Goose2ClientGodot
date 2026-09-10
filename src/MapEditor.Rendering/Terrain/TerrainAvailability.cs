namespace MapEditor.Rendering.Terrain;

public readonly record struct TerrainAvailability(
    bool IsCatalogValid,
    bool IsToolAvailable,
    string? Diagnostic);
