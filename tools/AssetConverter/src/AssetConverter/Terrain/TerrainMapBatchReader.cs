using MapEditor.Core;

namespace Goose2.AssetConverter.Terrain;

public sealed class TerrainMapBatchReader
{
    private readonly string _mapsDirectory;
    private readonly ITerrainMapDataReader _reader;

    public TerrainMapBatchReader(string corpusRoot)
        : this(corpusRoot, new FileTerrainMapDataReader(Path.Combine(corpusRoot, TerrainMapInventory.MapsDirectory)))
    {
    }

    internal TerrainMapBatchReader(string corpusRoot, ITerrainMapDataReader reader)
    {
        _mapsDirectory = Path.Combine(corpusRoot, TerrainMapInventory.MapsDirectory);
        _reader = reader;
    }

    public void VisitMap(TerrainMapDescriptor descriptor, Action<TerrainDecodedMap> visitor)
    {
        ITerrainMapData data = _reader.Open(descriptor.Identity);
        try
        {
            MapDocument document;
            try
            {
                document = MapCodec.Decode(data.Bytes);
            }
            catch (MapFormatException ex)
            {
                throw new TerrainGenerationException(
                    TerrainGenerationError.MapDecodeFailed,
                    $"Failed to decode map: {Path.Combine(_mapsDirectory, Path.GetFileName(descriptor.Identity))}.",
                    innerException: ex);
            }

            visitor(new TerrainDecodedMap(descriptor.Identity, document));
        }
        finally
        {
            data.Release();
        }
    }
}

public sealed class TerrainDecodedMap
{
    public string Identity { get; }
    public int Width { get; }
    public int Height { get; }

    internal TerrainDecodedMap(string identity, MapDocument document)
    {
        Identity = identity;
        Width = document.Width;
        Height = document.Height;
        _document = document;
    }

    private readonly MapDocument _document;

    public (int Sheet, int Graphic) GetLayer(int x, int y)
    {
        var layer = _document[x, y].GetLayer(0);
        return (layer.Sheet, layer.Graphic);
    }
}

internal sealed class FileTerrainMapDataReader : ITerrainMapDataReader
{
    private readonly string _mapsDirectory;

    public FileTerrainMapDataReader(string mapsDirectory)
    {
        _mapsDirectory = mapsDirectory;
    }

    public ITerrainMapData Open(string mapIdentity)
    {
        var path = Path.Combine(_mapsDirectory, Path.GetFileName(mapIdentity));
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Map file not found.", path);
        }

        try
        {
            return new ArrayTerrainMapData(File.ReadAllBytes(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException($"Failed to read map: {path}.", ex);
        }
    }

    private sealed class ArrayTerrainMapData : ITerrainMapData
    {
        public ArrayTerrainMapData(byte[] bytes)
        {
            Bytes = bytes;
        }

        public byte[] Bytes { get; }

        public void Release()
        {
        }
    }
}
