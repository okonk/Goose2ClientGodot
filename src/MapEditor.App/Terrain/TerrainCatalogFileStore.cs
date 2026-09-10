using System.Runtime.ExceptionServices;
using System.Text;

namespace MapEditor.App.Terrain;

internal sealed class TerrainCatalogFileStore
{
    private const string FileName = "terrain-brushes.json";
    private readonly ITerrainCatalogFileOperations _operations;

    public TerrainCatalogFileStore()
        : this(new FileOperations())
    {
    }

    internal TerrainCatalogFileStore(ITerrainCatalogFileOperations operations)
    {
        _operations = operations ?? throw new ArgumentNullException(nameof(operations));
    }

    public void Write(string assetDirectory, string serializedCatalog)
    {
        if (string.IsNullOrWhiteSpace(assetDirectory))
            throw new ArgumentException("Asset directory is required.", nameof(assetDirectory));
        ArgumentNullException.ThrowIfNull(serializedCatalog);

        string directory = Path.GetFullPath(assetDirectory);
        string destination = Path.Combine(directory, FileName);
        string temporary = Path.Combine(directory, $"{FileName}.tmp-{Guid.NewGuid():N}");
        bool destinationExists = _operations.Exists(destination);
        bool created = false;
        try
        {
            created = true;
            Stream stream = _operations.CreateFile(temporary);
            try
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(serializedCatalog);
                stream.Write(bytes, 0, bytes.Length);
                _operations.FlushToDisk(stream);
            }
            finally
            {
                stream.Dispose();
            }

            if (destinationExists)
                _operations.Replace(temporary, destination);
            else
                _operations.Move(temporary, destination);
            created = false;
        }
        catch (Exception primary)
        {
            if (created)
            {
                try
                {
                    _operations.Delete(temporary);
                }
                catch (Exception cleanup)
                {
                    primary.Data["TerrainCatalogFileStore.CleanupException"] = cleanup;
                }
            }

            ExceptionDispatchInfo.Capture(primary).Throw();
            throw;
        }
    }

    private sealed class FileOperations : ITerrainCatalogFileOperations
    {
        public bool Exists(string path) => File.Exists(path);
        public Stream CreateFile(string path) => new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        public void FlushToDisk(Stream stream) => ((FileStream)stream).Flush(flushToDisk: true);
        public void Replace(string sourcePath, string destinationPath) => File.Move(sourcePath, destinationPath, overwrite: true);
        public void Move(string sourcePath, string destinationPath) => File.Move(sourcePath, destinationPath);
        public void Delete(string path) => File.Delete(path);
    }
}
