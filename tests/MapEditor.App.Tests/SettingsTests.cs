using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MapEditor.App.Settings;
using Xunit;

namespace MapEditor.App.Tests;

public class SettingsTests
{
    [Fact]
    public void ResolveWindows_UsesAppDataFolder()
    {
        var path = SettingsPathResolver.ResolveWindows(@"C:\Users\agent\AppData\Roaming");

        Assert.Equal(Path.Combine(@"C:\Users\agent\AppData\Roaming", "Goose2MapEditor", "settings.json"), path);
    }

    [Fact]
    public void ResolveMacOs_UsesApplicationSupportFolder()
    {
        var path = SettingsPathResolver.ResolveMacOs("/Users/agent/Library/Application Support");

        Assert.Equal(Path.Combine("/Users/agent/Library/Application Support", "Goose2MapEditor", "settings.json"), path);
    }

    [Fact]
    public void ResolveLinux_AbsoluteXdgConfigHome_IsUsed()
    {
        var path = SettingsPathResolver.ResolveLinux("/home/agent/.local/config", "/home/agent");

        Assert.Equal(Path.Combine("/home/agent/.local/config", "goose2-map-editor", "settings.json"), path);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("relative/config")]
    public void ResolveLinux_MissingOrRelativeXdgConfigHome_FallsBackToDotConfig(string? xdgConfigHome)
    {
        var path = SettingsPathResolver.ResolveLinux(xdgConfigHome, "/home/agent");

        Assert.Equal(Path.Combine("/home/agent", ".config", "goose2-map-editor", "settings.json"), path);
    }

    [Fact]
    public void Load_MissingFile_IsUnset()
    {
        var directory = CreateTempDirectory();
        try
        {
            var store = new AppSettingsStore(Path.Combine(directory, "settings.json"));

            Assert.Equal(new AppSettings(null), store.Load());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData("/data/assets")]
    [InlineData(null)]
    public void SaveThenLoad_RoundTripsAssetDirectory(string? assetDirectory)
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            var store = new AppSettingsStore(path);
            store.Save(new AppSettings(assetDirectory));

            Assert.Equal(new AppSettings(assetDirectory), store.Load());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Save_CreatesMissingDirectory()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "nested", "deeper", "settings.json");

            new AppSettingsStore(path).Save(new AppSettings("/data/assets"));

            Assert.True(File.Exists(path));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Save_Success_LeavesNoTempFilesBehind()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            var store = new AppSettingsStore(path);
            store.Save(new AppSettings("/a"));
            store.Save(new AppSettings("/b"));

            var files = Directory.GetFiles(directory);
            Assert.Equal(new[] { path }, files);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("{\"assetDirectory\": 42}")]
    [InlineData("{\"assetDirectory\": \"relative/path\"}")]
    [InlineData("[1, 2]")]
    [InlineData("\"just a string\"")]
    public void Load_Malformed_ThrowsTypedAndLeavesFileUntouched(string contents)
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            File.WriteAllText(path, contents);
            var store = new AppSettingsStore(path);

            var exception = Assert.Throws<AppSettingsException>(() => store.Load());

            Assert.Equal(path, exception.Path);
            Assert.Equal(contents, File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Save_TempWriteFailure_PreservesOldSettingsAndCleansOnlyTemp()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            var oldJson = "{\"assetDirectory\":\"/old\"}";
            var operations = new FakeFileOperations();
            operations.Files[path] = oldJson;
            operations.WriteFailure = new IOException("disk full");
            var store = new AppSettingsStore(path, operations);

            var exception = Assert.Throws<AppSettingsException>(() => store.Save(new AppSettings("/new")));

            Assert.IsType<IOException>(exception.InnerException);
            Assert.Equal(oldJson, operations.Files[path]);
            var deleted = operations.Deleted.Single();
            Assert.NotEqual(path, deleted);
            Assert.Equal(directory, Path.GetDirectoryName(deleted));
            Assert.Empty(Directory.GetFiles(directory, "settings-*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Save_PreReplacementFailure_PreservesOldSettingsAndCreatesNoTemp()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            var oldJson = "{\"assetDirectory\":\"/old\"}";
            var operations = new FakeFileOperations();
            operations.Files[path] = oldJson;
            operations.CreateDirectoryFailure = new UnauthorizedAccessException("read-only");
            var store = new AppSettingsStore(path, operations);

            Assert.Throws<AppSettingsException>(() => store.Save(new AppSettings("/new")));

            Assert.Equal(oldJson, operations.Files[path]);
            Assert.Empty(operations.Deleted);
            Assert.Single(operations.Files);
            Assert.Empty(Directory.GetFiles(directory, "settings-*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Save_ReplacementFailure_IsTypedWithInnerExceptionAndCleansTemp()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            var oldJson = "{\"assetDirectory\":\"/old\"}";
            var operations = new FakeFileOperations();
            operations.Files[path] = oldJson;
            var failure = new IOException("rename failed");
            operations.MoveFailure = failure;
            var store = new AppSettingsStore(path, operations);

            var exception = Assert.Throws<AppSettingsException>(() => store.Save(new AppSettings("/new")));

            Assert.Same(failure, exception.InnerException);
            Assert.Equal(path, exception.Path);
            Assert.Equal(oldJson, operations.Files[path]);
            var deleted = operations.Deleted.Single();
            Assert.NotEqual(path, deleted);
            Assert.Equal(directory, Path.GetDirectoryName(deleted));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Save_Twice_UsesDistinctTempPaths()
    {
        var directory = CreateTempDirectory();
        try
        {
            var operations = new FakeFileOperations();
            var store = new AppSettingsStore(Path.Combine(directory, "settings.json"), operations);
            store.Save(new AppSettings("/a"));
            store.Save(new AppSettings("/b"));

            Assert.Equal(2, operations.Written.Count);
            Assert.NotEqual(operations.Written[0], operations.Written[1]);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static string CreateTempDirectory()
    {
        return Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())).FullName;
    }

    private sealed class FakeFileOperations : ISettingsFileOperations
    {
        public Dictionary<string, string> Files { get; } = new();

        public List<string> Written { get; } = new();

        public List<string> Deleted { get; } = new();

        public Exception? CreateDirectoryFailure { get; set; }

        public Exception? WriteFailure { get; set; }

        public Exception? MoveFailure { get; set; }

        public bool FileExists(string path)
        {
            return Files.ContainsKey(path);
        }

        public string ReadAllText(string path)
        {
            return Files[path];
        }

        public void CreateDirectory(string directory)
        {
            if (CreateDirectoryFailure is { } exception)
            {
                throw exception;
            }
        }

        public void WriteAllText(string path, string contents)
        {
            Files[path] = contents;
            Written.Add(path);
            if (WriteFailure is { } exception)
            {
                throw exception;
            }
        }

        public void Move(string source, string destination)
        {
            if (MoveFailure is { } exception)
            {
                throw exception;
            }

            Files[destination] = Files[source];
            Files.Remove(source);
        }

        public void DeleteFile(string path)
        {
            Deleted.Add(path);
            Files.Remove(path);
        }
    }
}
