using System.Runtime.ExceptionServices;
using System.Text;

namespace Goose2.AssetConverter.Terrain;

internal interface ITerrainCatalogStore
{
    void Write(string repoRoot, string serializedCatalog);
}

internal interface ITerrainCatalogFileOperations
{
    bool Exists(string path);
    Stream CreateFile(string path);
    void FlushToDisk(Stream stream);
    void Replace(string sourcePath, string destinationPath);
    void Move(string sourcePath, string destinationPath);
    void Delete(string path);
}

internal sealed class TerrainCatalogFileStore : ITerrainCatalogStore
{
    public const string RelativePath = "Assets/Sprites/terrain-brushes.json";
    internal const string CleanupExceptionKey = "TerrainCatalogFileStore.CleanupException";

    private readonly ITerrainCatalogFileOperations _operations;

    public TerrainCatalogFileStore()
        : this(new SystemFileOperations())
    {
    }

    internal TerrainCatalogFileStore(ITerrainCatalogFileOperations operations)
    {
        _operations = operations;
    }

    public void Write(string repoRoot, string serializedCatalog)
    {
        var destination = Path.Combine(repoRoot, RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temp = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        var bytes = Encoding.UTF8.GetBytes(serializedCatalog);

        try
        {
            using (var stream = _operations.CreateFile(temp))
            {
                stream.Write(bytes, 0, bytes.Length);
                _operations.FlushToDisk(stream);
            }
        }
        catch (Exception primary)
        {
            RethrowAfterCleanup(primary, temp);
        }

        try
        {
            if (_operations.Exists(destination))
            {
                _operations.Replace(temp, destination);
            }
            else
            {
                _operations.Move(temp, destination);
            }
        }
        catch (Exception primary)
        {
            RethrowAfterCleanup(primary, temp);
        }
    }

    private void RethrowAfterCleanup(Exception primary, string temp)
    {
        try
        {
            _operations.Delete(temp);
        }
        catch (Exception cleanupException)
        {
            primary.Data[CleanupExceptionKey] = cleanupException;
            ExceptionDispatchInfo.Capture(primary).Throw();
        }

        ExceptionDispatchInfo.Capture(primary).Throw();
    }

    internal sealed class SystemFileOperations : ITerrainCatalogFileOperations
    {
        public bool Exists(string path) => File.Exists(path);

        public Stream CreateFile(string path)
            => new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);

        public void FlushToDisk(Stream stream)
        {
            if (stream is not FileStream fileStream)
            {
                throw new ArgumentException("The stream must be a FileStream.", nameof(stream));
            }

            fileStream.Flush(flushToDisk: true);
        }

        public void Replace(string sourcePath, string destinationPath)
            => File.Move(sourcePath, destinationPath, overwrite: true);

        public void Move(string sourcePath, string destinationPath)
            => File.Move(sourcePath, destinationPath);

        public void Delete(string path) => File.Delete(path);
    }
}
