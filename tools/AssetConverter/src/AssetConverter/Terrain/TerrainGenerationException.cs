using MapEditor.Core.Terrain;

namespace Goose2.AssetConverter.Terrain;

public enum TerrainGenerationError
{
    MapInventoryNotFound,
    InvalidMapInventory,
    MapNotFound,
    MapReadFailed,
    MapDecodeFailed,
    ManifestNotFound,
    ManifestReadFailed,
    ManifestMalformed,
    UnsupportedTileSize,
    SheetNotFound,
    SheetReadFailed,
    SheetDecodeFailed,
    FrameOutOfBounds,
    NoMaps,
    NoEligiblePlacements,
    NumericOverflow,
    InvalidGeneratedCatalog
}

public sealed class TerrainGenerationException : InvalidOperationException
{
    public TerrainGenerationError Error { get; }
    public string? InputPath { get; }
    public TerrainGraphicReference? Reference { get; }

    public TerrainGenerationException(
        TerrainGenerationError error,
        string message,
        string? inputPath = null,
        TerrainGraphicReference? reference = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Error = error;
        InputPath = inputPath;
        Reference = reference;
    }
}
