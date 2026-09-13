using MapEditor.Core;
using MapEditor.Rendering;

namespace MapEditor.App.Terrain;

public sealed record TerrainCatalogSaveResult(
    string OperationId,
    TerrainCatalog Catalog,
    TerrainCatalogIndex Index,
    TerrainFileRevision Revision);
