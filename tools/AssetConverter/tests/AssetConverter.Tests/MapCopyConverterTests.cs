using Goose2.AssetConverter.Maps;
using Xunit;

namespace AssetConverter.Tests;

public class MapCopyConverterTests
{
    [Fact]
    public void Convert_CopiesMapFilesUsingUnityNameRule()
    {
        var source = Path.Combine(Path.GetTempPath(), "maps_src_" + Guid.NewGuid().ToString("N"));
        var output = Path.Combine(Path.GetTempPath(), "maps_out_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(source);
            File.WriteAllBytes(Path.Combine(source, "Map100.map"), new byte[] { 1, 2, 3 });

            var result = MapCopyConverter.Convert(source, output);

            Assert.Equal(1, result.Copied);
            var outPath = Path.Combine(output, "Map100.bytes");
            Assert.True(File.Exists(outPath));
            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(outPath));
        }
        finally
        {
            if (Directory.Exists(source)) Directory.Delete(source, recursive: true);
            if (Directory.Exists(output)) Directory.Delete(output, recursive: true);
        }
    }

    [Fact]
    public void Convert_CopiesMultipleMapFiles()
    {
        var source = Path.Combine(Path.GetTempPath(), "maps_src_" + Guid.NewGuid().ToString("N"));
        var output = Path.Combine(Path.GetTempPath(), "maps_out_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(source);
            File.WriteAllBytes(Path.Combine(source, "Map100.map"), new byte[] { 1, 2, 3 });
            File.WriteAllBytes(Path.Combine(source, "Map101.map"), new byte[] { 4, 5, 6 });
            File.WriteAllBytes(Path.Combine(source, "Map10.map"), new byte[] { 7, 8, 9 });

            var result = MapCopyConverter.Convert(source, output);

            Assert.Equal(3, result.Copied);
            Assert.Empty(result.Failures);
            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(output, "Map100.bytes")));
            Assert.Equal(new byte[] { 4, 5, 6 }, File.ReadAllBytes(Path.Combine(output, "Map101.bytes")));
            Assert.Equal(new byte[] { 7, 8, 9 }, File.ReadAllBytes(Path.Combine(output, "Map10.bytes")));
        }
        finally
        {
            if (Directory.Exists(source)) Directory.Delete(source, recursive: true);
            if (Directory.Exists(output)) Directory.Delete(output, recursive: true);
        }
    }

    [Fact]
    public void Convert_OverwritesExistingOutput()
    {
        var source = Path.Combine(Path.GetTempPath(), "maps_src_" + Guid.NewGuid().ToString("N"));
        var output = Path.Combine(Path.GetTempPath(), "maps_out_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(output);
            File.WriteAllBytes(Path.Combine(source, "Map100.map"), new byte[] { 1, 2, 3 });
            // Pre-existing output with different content
            File.WriteAllBytes(Path.Combine(output, "Map100.bytes"), new byte[] { 9, 9, 9 });

            var result = MapCopyConverter.Convert(source, output);

            Assert.Equal(1, result.Copied);
            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(output, "Map100.bytes")));
        }
        finally
        {
            if (Directory.Exists(source)) Directory.Delete(source, recursive: true);
            if (Directory.Exists(output)) Directory.Delete(output, recursive: true);
        }
    }

    [Fact]
    public void Convert_CreatesOutputDirectoryIfNeeded()
    {
        var source = Path.Combine(Path.GetTempPath(), "maps_src_" + Guid.NewGuid().ToString("N"));
        var output = Path.Combine(Path.GetTempPath(), "maps_out_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(source);
            File.WriteAllBytes(Path.Combine(source, "Map100.map"), new byte[] { 1, 2, 3 });

            var result = MapCopyConverter.Convert(source, output);

            Assert.Equal(1, result.Copied);
            Assert.True(Directory.Exists(output));
            Assert.True(File.Exists(Path.Combine(output, "Map100.bytes")));
        }
        finally
        {
            if (Directory.Exists(source)) Directory.Delete(source, recursive: true);
            if (Directory.Exists(output)) Directory.Delete(output, recursive: true);
        }
    }

    [Fact]
    public void Convert_SkipsNonMapFiles()
    {
        var source = Path.Combine(Path.GetTempPath(), "maps_src_" + Guid.NewGuid().ToString("N"));
        var output = Path.Combine(Path.GetTempPath(), "maps_out_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(source);
            File.WriteAllBytes(Path.Combine(source, "Map100.map"), new byte[] { 1, 2, 3 });
            File.WriteAllText(Path.Combine(source, "readme.txt"), "not a map");

            var result = MapCopyConverter.Convert(source, output);

            Assert.Equal(1, result.Copied);
            Assert.Empty(result.Failures);
            Assert.False(File.Exists(Path.Combine(output, "readme.txt")));
        }
        finally
        {
            if (Directory.Exists(source)) Directory.Delete(source, recursive: true);
            if (Directory.Exists(output)) Directory.Delete(output, recursive: true);
        }
    }

    [Fact]
    public void Convert_RecordsFailureForInvalidBasename()
    {
        var source = Path.Combine(Path.GetTempPath(), "maps_src_" + Guid.NewGuid().ToString("N"));
        var output = Path.Combine(Path.GetTempPath(), "maps_out_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(source);
            File.WriteAllBytes(Path.Combine(source, "Map100.map"), new byte[] { 1, 2, 3 });
            // Single-character basename before extension — Substring(1) would produce empty string
            File.WriteAllBytes(Path.Combine(source, "M.map"), new byte[] { 4, 5, 6 });

            var result = MapCopyConverter.Convert(source, output);

            Assert.Equal(1, result.Copied);
            Assert.Single(result.Failures);
            Assert.Contains("M.map", result.Failures[0]);
            Assert.True(File.Exists(Path.Combine(output, "Map100.bytes")));
            Assert.False(File.Exists(Path.Combine(output, "M.bytes")));
        }
        finally
        {
            if (Directory.Exists(source)) Directory.Delete(source, recursive: true);
            if (Directory.Exists(output)) Directory.Delete(output, recursive: true);
        }
    }

    [Fact]
    public void Convert_ReturnsZeroWhenNoMapFiles()
    {
        var source = Path.Combine(Path.GetTempPath(), "maps_src_" + Guid.NewGuid().ToString("N"));
        var output = Path.Combine(Path.GetTempPath(), "maps_out_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(source);

            var result = MapCopyConverter.Convert(source, output);

            Assert.Equal(0, result.Copied);
            Assert.Empty(result.Failures);
        }
        finally
        {
            if (Directory.Exists(source)) Directory.Delete(source, recursive: true);
            if (Directory.Exists(output)) Directory.Delete(output, recursive: true);
        }
    }
}
