using System.IO;

namespace MapEditor.App.Terrain;

internal interface ITerrainCatalogFileOperations
{
    bool Exists(string path);
    Stream CreateFile(string path);
    void FlushToDisk(Stream stream);
    void Replace(string sourcePath, string destinationPath);
    void Move(string sourcePath, string destinationPath);
    void Delete(string path);
}
