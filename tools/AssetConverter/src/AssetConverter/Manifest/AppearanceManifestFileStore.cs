using System.Text;

namespace Goose2.AssetConverter.Manifest;

public interface IAppearanceManifestFileOperations
{
    bool Exists(string path);
    Stream CreateFile(string path);
    void Replace(string source, string destination);
    void Move(string source, string destination);
    void Delete(string path);
}

public static class AppearanceManifestFileStore
{
    public const string RelativePath = "Assets/Sprites/appearance-manifest.json";

    public static void Write(string outRoot, string json, IAppearanceManifestFileOperations? ops = null)
    {
        var fileOps = ops ?? new SystemFileOperations();
        string destination = Path.Combine(outRoot, RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        string temp = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = fileOps.CreateFile(temp))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush();
            }
        }
        catch
        {
            fileOps.Delete(temp);
            throw;
        }

        try
        {
            if (fileOps.Exists(destination))
                fileOps.Replace(temp, destination);
            else
                fileOps.Move(temp, destination);
        }
        catch
        {
            fileOps.Delete(temp);
            throw;
        }
    }

    private sealed class SystemFileOperations : IAppearanceManifestFileOperations
    {
        public bool Exists(string path) => File.Exists(path);
        public Stream CreateFile(string path) => File.Create(path);
        public void Replace(string source, string destination) => File.Move(source, destination, overwrite: true);
        public void Move(string source, string destination) => File.Move(source, destination);
        public void Delete(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
