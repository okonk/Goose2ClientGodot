using MapEditor.Core;
using MapEditor.Rendering;

namespace MapEditor.App.Terrain;

public sealed record TerrainCatalogPreparedSave(
    string OperationId,
    byte[] CanonicalBytes,
    TerrainCatalog Catalog,
    TerrainCatalogIndex Index,
    string SourcePath,
    TerrainFileRevision ExpectedRevision);
