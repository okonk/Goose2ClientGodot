using Goose2.AssetConverter.Aspereta;
using Goose2.AssetConverter.Maps;

namespace AssetConverter.Tests;

public class MapConversionResultTests
{
    [Fact]
    public void MapCopyConvert_ReturnsOnlySuccessfulSortedOutputFileNames()
    {
        using var directories = new TemporaryDirectories();
        File.WriteAllBytes(Path.Combine(directories.Source, "Zed.map"), [1]);
        File.WriteAllBytes(Path.Combine(directories.Source, "Alpha.map"), [2]);
        File.WriteAllBytes(Path.Combine(directories.Source, "Blocked.map"), [3]);
        Directory.CreateDirectory(Path.Combine(directories.Output, "Mlocked.map"));

        var result = MapCopyConverter.Convert(directories.Source, directories.Output);

        Assert.Equal(2, result.Copied);
        Assert.Single(result.Failures);
        Assert.Contains("Blocked.map", result.Failures[0]);
        Assert.Equal(new[] { "Med.map", "Mlpha.map" }, result.OutputFileNames);
        Assert.DoesNotContain("Mlocked.map", result.OutputFileNames);
        using var alpha = File.Open(Path.Combine(directories.Output, "Mlpha.map"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var zed = File.Open(Path.Combine(directories.Output, "Med.map"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Fact]
    public void AsperetaConvert_ReturnsOnlySuccessfulSortedOutputFileNames()
    {
        using var directories = new TemporaryDirectories();
        WriteAsperetaMap(Path.Combine(directories.Source, "Map2.map"));
        File.WriteAllBytes(Path.Combine(directories.Source, "Map1.map"), [0, 0, 0, 0, 0]);

        var result = AsperetaMapConverter.Convert(directories.Source, directories.Output, Array.Empty<MappingRow>());

        Assert.Equal(1, result.Converted);
        Assert.Single(result.Failures);
        Assert.Empty(result.Warnings);
        Assert.Equal(new[] { "Map10002.map" }, result.OutputFileNames);
        Assert.DoesNotContain("Map10001.map", result.OutputFileNames);
        using var completed = File.Open(Path.Combine(directories.Output, "Map10002.map"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var failed = File.Open(Path.Combine(directories.Output, "Map10001.map"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    private static void WriteAsperetaMap(string path)
    {
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write((short)1);
        writer.Write((short)1);
        for (var i = 0; i < 100 * 100; i++)
        {
            writer.Write((byte)0);
            for (var layer = 0; layer < 4; layer++)
            {
                writer.Write(0);
            }
        }
    }

    private sealed class TemporaryDirectories : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "ac_map_results_" + Guid.NewGuid().ToString("N"));

        public TemporaryDirectories()
        {
            Source = Path.Combine(_root, "source");
            Output = Path.Combine(_root, "output");
            Directory.CreateDirectory(Source);
            Directory.CreateDirectory(Output);
        }

        public string Source { get; }
        public string Output { get; }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }
}
