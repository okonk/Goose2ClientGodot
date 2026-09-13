using MapEditor.App.Rendering;
using MapEditor.Rendering;

namespace MapEditor.App.Terrain;

internal enum TerrainLoadedPublicationKind
{
    ValidReload,
    ConfirmedMalformedReload
}

internal interface ITerrainCatalogPublisher
{
    ITerrainCatalogSavePublication PrepareSave(
        TerrainOperationLease operation,
        AssetContext expectedContext,
        TerrainCatalogPreparedSave preparedSave);

    ITerrainCatalogLoadedPublication PrepareLoaded(
        TerrainOperationLease operation,
        AssetContext expectedContext,
        TerrainCatalogLoadResult loadedReplacement,
        TerrainLoadedPublicationKind kind);
}

internal interface ITerrainCatalogSavePublication : IDisposable
{
    void Commit(TerrainCatalogSaveResult durableReplacement);
}

internal interface ITerrainCatalogLoadedPublication : IDisposable
{
    void Commit();
}
