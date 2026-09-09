using System.Runtime.ExceptionServices;
using System.Text;

namespace Goose2.AssetConverter.Terrain;

public interface ITerrainMapInventoryFileOperations
{
    bool Exists(string path);
    Stream CreateFile(string path);
    void Replace(string source, string destination);
    void Move(string source, string destination);
    void Delete(string path);
}

public sealed class TerrainMapInventory
{
    public const string Header = "terrain-map-inputs-v1";
    public const string FileName = Header + ".txt";
    public const string MapsDirectory = "Assets/Maps";
    public const string RelativePath = MapsDirectory + "/" + FileName;

    public IReadOnlyList<string> FileNames { get; }
    public IReadOnlyList<string> MapIdentities { get; }

    internal byte[] Content { get; }

    internal TerrainMapInventory(IReadOnlyList<string> fileNames, byte[] content)
    {
        FileNames = fileNames;
        MapIdentities = fileNames.Select(name => IdentityFor(name)).ToList();
        Content = content;
    }

    public static string IdentityFor(string fileName) => MapsDirectory + "/" + fileName;

    public static TerrainMapInventory Read(string repoRoot)
    {
        var path = Path.GetFullPath(Path.Combine(repoRoot, RelativePath));
        if (!File.Exists(path))
        {
            throw new TerrainGenerationException(
                TerrainGenerationError.MapInventoryNotFound,
                $"Map inventory not found: {path}.",
                path);
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new TerrainGenerationException(
                TerrainGenerationError.InvalidMapInventory,
                $"Failed to read map inventory: {path}.",
                path,
                innerException: ex);
        }

        return Parse(bytes, path);
    }

    internal static TerrainMapInventory Parse(byte[] bytes, string path)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            throw Invalid(path, "inventory must not contain a UTF-8 BOM.");
        }

        string text;
        try
        {
            text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (ArgumentException ex)
        {
            throw new TerrainGenerationException(
                TerrainGenerationError.InvalidMapInventory,
                $"Map inventory is not valid UTF-8: {path}.",
                path,
                innerException: ex);
        }

        if (text.Length == 0 || text.Contains('\r') || !text.EndsWith("\n", StringComparison.Ordinal))
        {
            throw Invalid(path, "inventory must be LF-terminated and end with a newline.");
        }

        var lines = text.Split('\n');
        if (lines[0] != Header || lines.Length < 3 || lines[^1] != "")
        {
            throw Invalid(path, "inventory must start with the header line and list at least one map.");
        }

        var fileNames = new List<string>(lines.Length - 2);
        for (var i = 1; i < lines.Length - 1; i++)
        {
            var name = lines[i];
            if (!TryValidateFileName(name))
            {
                throw Invalid(path, $"invalid map file name '{name}'.");
            }

            if (fileNames.Count > 0 && string.CompareOrdinal(fileNames[^1], name) >= 0)
            {
                throw Invalid(path, $"map file names must be ordinal-sorted and unique; '{name}' breaks the order.");
            }

            fileNames.Add(name);
        }

        var mapsDirectory = Path.GetDirectoryName(path)!;
        foreach (var name in fileNames)
        {
            var mapPath = Path.GetFullPath(Path.Combine(mapsDirectory, name));
            if (!File.Exists(mapPath))
            {
                throw new TerrainGenerationException(
                    TerrainGenerationError.MapNotFound,
                    $"Listed map file does not exist: {mapPath}.",
                    mapPath);
            }
        }

        return new TerrainMapInventory(fileNames.AsReadOnly(), bytes);
    }

    public static void Write(
        string mapsDirectory,
        IEnumerable<string> outputFileNames,
        ITerrainMapInventoryFileOperations? operations = null)
    {
        var fileOps = operations ?? new SystemFileOperations();
        var names = outputFileNames?.ToList() ?? throw new ArgumentNullException(nameof(outputFileNames));
        if (names.Count == 0)
        {
            throw Invalid(null, "inventory must list at least one map.");
        }

        foreach (var name in names)
        {
            if (!TryValidateFileName(name))
            {
                throw Invalid(null, $"invalid map file name '{name}'.");
            }
        }

        var sorted = names.Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToList();
        if (sorted.Count != names.Count)
        {
            throw Invalid(null, "map file names must be unique.");
        }

        foreach (var name in sorted)
        {
            var mapPath = Path.GetFullPath(Path.Combine(mapsDirectory, name));
            if (!File.Exists(mapPath))
            {
                throw new TerrainGenerationException(
                    TerrainGenerationError.MapNotFound,
                    $"Listed map file does not exist: {mapPath}.",
                    mapPath);
            }
        }

        var builder = new StringBuilder();
        builder.Append(Header).Append('\n');
        foreach (var name in sorted)
        {
            builder.Append(name).Append('\n');
        }

        var destination = Path.GetFullPath(Path.Combine(mapsDirectory, FileName));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temp = destination + ".tmp-" + Guid.NewGuid().ToString("N");

        try
        {
            using (var stream = fileOps.CreateFile(temp))
            {
                var bytes = Encoding.UTF8.GetBytes(builder.ToString());
                stream.Write(bytes, 0, bytes.Length);
                if (stream is FileStream fileStream)
                {
                    fileStream.Flush(flushToDisk: true);
                }
                else
                {
                    stream.Flush();
                }
            }
        }
        catch (Exception primary)
        {
            RethrowAfterCleanup(fileOps, temp, primary);
        }

        try
        {
            if (fileOps.Exists(destination))
            {
                fileOps.Replace(temp, destination);
            }
            else
            {
                fileOps.Move(temp, destination);
            }
        }
        catch (Exception primary)
        {
            RethrowAfterCleanup(fileOps, temp, primary);
        }
    }

    private static void RethrowAfterCleanup(
        ITerrainMapInventoryFileOperations fileOps,
        string temp,
        Exception primary)
    {
        try
        {
            fileOps.Delete(temp);
        }
        catch
        {
            ExceptionDispatchInfo.Capture(primary).Throw();
        }

        ExceptionDispatchInfo.Capture(primary).Throw();
    }

    internal static bool TryValidateFileName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length < 5)
        {
            return false;
        }

        if (!name.EndsWith(".map", StringComparison.Ordinal))
        {
            return false;
        }

        if (name == "." || name == "..")
        {
            return false;
        }

        return name.AsSpan().IndexOfAny('/', '\\') < 0 && !Path.IsPathRooted(name);
    }

    private static TerrainGenerationException Invalid(string? path, string detail)
        => new(TerrainGenerationError.InvalidMapInventory, $"Invalid map inventory: {detail}", path);

    private sealed class SystemFileOperations : ITerrainMapInventoryFileOperations
    {
        public bool Exists(string path) => File.Exists(path);

        public Stream CreateFile(string path) => File.Create(path);

        public void Replace(string source, string destination) => File.Move(source, destination, overwrite: true);

        public void Move(string source, string destination) => File.Move(source, destination);

        public void Delete(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
