using System.IO;

namespace MapEditor.App.Terrain;

public interface ITerrainCatalogFileOperations
{
    bool DirectoryExists(string path);

    bool FileExists(string path);

    byte[] ReadFile(string path);

    Stream CreateNew(string path);

    void FlushToDisk(Stream stream);

    void AtomicOverwrite(string source, string destination);

    void Delete(string path);
}

public sealed class TerrainCatalogFileOperations : ITerrainCatalogFileOperations
{
    public bool DirectoryExists(string path)
        => Directory.Exists(path);

    public bool FileExists(string path)
        => File.Exists(path);

    public byte[] ReadFile(string path)
        => File.ReadAllBytes(path);

    public Stream CreateNew(string path)
        => new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);

    public void FlushToDisk(Stream stream)
    {
        ((FileStream)stream).Flush(flushToDisk: true);
    }

    public void AtomicOverwrite(string source, string destination)
        => File.Move(source, destination, overwrite: true);

    public void Delete(string path)
        => File.Delete(path);
}
