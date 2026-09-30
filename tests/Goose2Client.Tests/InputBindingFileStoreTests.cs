using System;
using System.IO;
using System.Text;
using Goose2Client.InputBindings;
using Xunit;

namespace Goose2Client.Tests;

public class InputBindingFileStoreTests : IDisposable
{
    private readonly string _directory;
    private readonly string _filePath;
    private readonly InputBindingFileStore _store;

    public InputBindingFileStoreTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "input-binding-store-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _filePath = Path.Combine(_directory, "input-bindings.json");
        _store = new InputBindingFileStore(_filePath);
    }

    public void Dispose()
    {
        _store.FailBeforePublish = null;
        _store.FailDuringRollback = null;
        _store.FailDuringCleanup = null;
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch
        {
        }
    }

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    private string[] FilesInDirectory() => Directory.GetFiles(_directory);

    [Fact]
    public void Read_MissingFile_ReturnsNull_DistinctFromEmptyFile()
    {
        Assert.Null(_store.Read());

        File.WriteAllBytes(_filePath, Array.Empty<byte>());
        Assert.Empty(_store.Read()!);
    }

    [Fact]
    public void Read_ExistingFile_ReturnsExactBytesWithoutInterpreting()
    {
        var bytes = new byte[] { 0x01, 0x02, 0xFF, 0x7B, 0x00 };
        File.WriteAllBytes(_filePath, bytes);

        Assert.Equal(bytes, _store.Read());
    }

    [Fact]
    public void Constructor_RejectsRelativePath()
    {
        Assert.Throws<ArgumentException>(() => new InputBindingFileStore("relative/input-bindings.json"));
    }

    [Fact]
    public void Save_FirstTime_CreatesDestinationAndLeavesNoTempFile()
    {
        var content = Utf8("{\"version\":1,\"actions\":{}}");
        var transaction = _store.Save(content);

        Assert.True(transaction.Complete());
        Assert.Equal(content, _store.Read());
        Assert.Equal(new[] { _filePath }, FilesInDirectory());
    }

    [Fact]
    public void Save_ExistingFile_PublishesNewBytesAndLeavesNoTempOrBackupAfterComplete()
    {
        var first = _store.Save(Utf8("A"));
        Assert.True(first.Complete());

        var second = _store.Save(Utf8("B"));
        Assert.True(second.Complete());

        Assert.Equal(Utf8("B"), _store.Read());
        Assert.Equal(new[] { _filePath }, FilesInDirectory());
    }

    [Fact]
    public void Save_FailureBeforeRename_PreservesOldBytesAndCleansTemp()
    {
        var first = _store.Save(Utf8("A"));
        Assert.True(first.Complete());

        _store.FailBeforePublish = _ => throw new IOException("simulated write failure");

        Assert.Throws<IOException>(() => _store.Save(Utf8("B")));

        Assert.Equal(Utf8("A"), _store.Read());
        Assert.Equal(new[] { _filePath }, FilesInDirectory());
    }

    [Fact]
    public void Rollback_ExistingDestination_RestoresOldBytes()
    {
        var first = _store.Save(Utf8("A"));
        Assert.True(first.Complete());

        var second = _store.Save(Utf8("B"));
        Assert.True(second.Rollback());

        Assert.Equal(Utf8("A"), _store.Read());
        Assert.Equal(new[] { _filePath }, FilesInDirectory());
        Assert.True(second.Rollback());
    }

    [Fact]
    public void Rollback_MissingDestination_DeletesPublishedFile()
    {
        var transaction = _store.Save(Utf8("A"));

        Assert.True(transaction.Rollback());

        Assert.Null(_store.Read());
        Assert.Empty(FilesInDirectory());
        Assert.True(transaction.Rollback());
    }

    [Fact]
    public void Rollback_Failure_ReturnsFalseAndLeavesPublishedBytes()
    {
        var first = _store.Save(Utf8("A"));
        Assert.True(first.Complete());

        var second = _store.Save(Utf8("B"));
        _store.FailDuringRollback = _ => throw new IOException("simulated rollback failure");

        Assert.False(second.Rollback());
        Assert.Equal(Utf8("B"), _store.Read());

        _store.FailDuringRollback = null;
        Assert.True(second.Rollback());
        Assert.Equal(Utf8("A"), _store.Read());
    }

    [Fact]
    public void Complete_CleanupFailure_LeavesDestinationIntactAndReportsSeparately()
    {
        var first = _store.Save(Utf8("A"));
        Assert.True(first.Complete());

        var second = _store.Save(Utf8("B"));
        _store.FailDuringCleanup = _ => throw new IOException("simulated cleanup failure");

        Assert.False(second.Complete());
        Assert.Equal(Utf8("B"), _store.Read());
        Assert.NotEmpty(FilesInDirectory());

        _store.FailDuringCleanup = null;
        Assert.True(second.Complete());
        Assert.Equal(Utf8("B"), _store.Read());
        Assert.Equal(new[] { _filePath }, FilesInDirectory());
    }
}
