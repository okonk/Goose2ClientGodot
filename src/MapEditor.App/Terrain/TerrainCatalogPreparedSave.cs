using System;
using MapEditor.Core;
using MapEditor.Rendering;

namespace MapEditor.App.Terrain;

public sealed class TerrainCatalogPreparedSave
{
    private readonly byte[] _canonicalBytes;

    public string OperationId { get; }
    public byte[] CanonicalBytes => _canonicalBytes.ToArray();
    public TerrainCatalog Catalog { get; }
    public TerrainCatalogIndex Index { get; }
    public string SourcePath { get; }
    public TerrainFileRevision ExpectedRevision { get; }

    internal TerrainCatalogPreparedSave(
        string operationId,
        byte[] canonicalBytes,
        TerrainCatalog catalog,
        TerrainCatalogIndex index,
        string sourcePath,
        TerrainFileRevision expectedRevision)
    {
        OperationId = operationId;
        _canonicalBytes = canonicalBytes.ToArray();
        Catalog = catalog;
        Index = index;
        SourcePath = sourcePath;
        ExpectedRevision = expectedRevision;
    }
}
