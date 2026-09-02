using System;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using MapEditor.Core;
using Xunit;

namespace MapEditor.Core.Tests;

public class MapFileStoreTests
{
    private static string CreateTempDirectory()
    {
        return Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())).FullName;
    }

    private static MapDocument CreateDocument()
    {
        var document = MapDocument.Create(4, 3);
        document.SetFlags(0, 0, MapDocument.BlockedFlag);
        document.SetLayer(1, 2, 3, new MapTileLayer(7, 42));
        return document;
    }

    private static string HashFile(string path)
    {
        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    }

    private static bool IsStoreTempName(string name)
    {
        return name.StartsWith(".map-", StringComparison.Ordinal) && name.EndsWith(".tmp", StringComparison.Ordinal);
    }

    private static void AssertNoStoreTemps(string directory, string[] before)
    {
        var after = Directory.EnumerateFiles(directory).Where(IsStoreTempName).ToArray();
        Assert.Empty(after);
        Assert.Equal(before, after);
    }

    [Fact]
    public void Open_ReturnsDecodedDocumentAndRevisionOfExactBytes()
    {
        var directory = CreateTempDirectory();
        try
        {
            var document = CreateDocument();
            var bytes = MapCodec.Encode(document);
            var path = Path.Combine(directory, "map.dat");
            File.WriteAllBytes(path, bytes);

            var opened = new MapFileStore().Open(path);

            Assert.Equal(document.Width, opened.Document.Width);
            Assert.Equal(document.Height, opened.Document.Height);
            Assert.Equal(document.TileCount, opened.Document.TileCount);
            Assert.Equal(document.Version, opened.Document.Version);
            Assert.Equal(document.EditorVersion, opened.Document.EditorVersion);
            for (var i = 0; i < document.TileCount; i++)
            {
                Assert.Equal(document.GetTile(i), opened.Document.GetTile(i));
            }
            Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), opened.Revision.ContentHash);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Open_InvalidFileThrowsWithoutReturningState()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "map.dat");
            File.WriteAllBytes(path, new byte[] { 1, 2, 3 });

            Assert.Throws<MapFormatException>(() => new MapFileStore().Open(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Save_NewPathWritesDecodableExactBytesAndReturnsMatchingRevision()
    {
        var directory = CreateTempDirectory();
        try
        {
            var document = CreateDocument();
            var path = Path.Combine(directory, "map.dat");
            var expectedBytes = MapCodec.Encode(document);

            var revision = new MapFileStore().Save(path, document);

            var onDisk = File.ReadAllBytes(path);
            Assert.Equal(expectedBytes, onDisk);
            Assert.Equal(revision.ContentHash, Convert.ToHexString(SHA256.HashData(onDisk)));
            var decoded = MapCodec.Decode(onDisk);
            Assert.Equal(document.Width, decoded.Width);
            Assert.Equal(document.Height, decoded.Height);
            for (var i = 0; i < document.TileCount; i++)
            {
                Assert.Equal(document.GetTile(i), decoded.GetTile(i));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Save_ExistingPathUsesSingleLocalReplacementAndLeavesNoTemp()
    {
        var directory = CreateTempDirectory();
        try
        {
            var first = CreateDocument();
            var path = Path.Combine(directory, "map.dat");
            var firstBytes = MapCodec.Encode(first);
            var store = new MapFileStore();
            var revision = store.Save(path, first);

            var second = CreateDocument();
            second.SetFlags(3, 2, MapDocument.BlockedFlag);
            var secondBytes = MapCodec.Encode(second);
            var calls = 0;
            var before = Directory.EnumerateFiles(directory).Where(IsStoreTempName).ToArray();
            store = new MapFileStore((temp, destination) =>
            {
                calls++;
                Assert.Equal(Path.GetFullPath(directory), Path.GetDirectoryName(destination));
                Assert.Equal(path, destination);
                File.Move(temp, destination, overwrite: true);
            });

            var newRevision = store.Save(path, second, revision);

            Assert.Equal(1, calls);
            Assert.Equal(secondBytes, File.ReadAllBytes(path));
            Assert.Equal(Convert.ToHexString(SHA256.HashData(secondBytes)), newRevision.ContentHash);
            AssertNoStoreTemps(directory, before);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Save_ReplacementReceivesAdjacentTempAndNormalizedDestination()
    {
        var directory = CreateTempDirectory();
        try
        {
            var document = CreateDocument();
            var path = Path.Combine(directory, "map.dat");
            var calls = 0;
            var store = new MapFileStore((temp, destination) =>
            {
                calls++;
                Assert.Equal(Path.GetFullPath(directory), Path.GetDirectoryName(temp));
                Assert.Equal(Path.GetFullPath(directory), Path.GetDirectoryName(destination));
                Assert.Equal(Path.GetFullPath(path), destination);
                File.Move(temp, destination, overwrite: true);
            });

            store.Save(path, document);

            Assert.Equal(1, calls);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Save_ReturnedRevisionAllowsNextGuardedSave()
    {
        var directory = CreateTempDirectory();
        try
        {
            var document = CreateDocument();
            var path = Path.Combine(directory, "map.dat");
            var store = new MapFileStore();
            var revision = store.Save(path, document);

            var newRevision = store.Save(path, document, revision);

            Assert.Equal(revision, newRevision);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Save_ValidationFailureLeavesDestinationUntouchedAndCreatesNoTemp()
    {
        var directory = CreateTempDirectory();
        try
        {
            var valid = CreateDocument();
            var path = Path.Combine(directory, "map.dat");
            var store = new MapFileStore();
            var revision = store.Save(path, valid);
            var originalBytes = File.ReadAllBytes(path);
            var before = Directory.EnumerateFiles(directory).Where(IsStoreTempName).ToArray();

            var invalid = CreateDocument();
            invalid.SetLayer(0, 0, 0, new MapTileLayer(short.MaxValue + 1, 0));

            Assert.Throws<MapValidationException>(() => store.Save(path, invalid, revision));
            Assert.Equal(originalBytes, File.ReadAllBytes(path));
            AssertNoStoreTemps(directory, before);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Save_InjectedPreMoveFailureLeavesDestinationUntouchedAndCleansTemp()
    {
        var directory = CreateTempDirectory();
        try
        {
            var first = CreateDocument();
            var path = Path.Combine(directory, "map.dat");
            var store = new MapFileStore();
            var revision = store.Save(path, first);
            var originalBytes = File.ReadAllBytes(path);
            var before = Directory.EnumerateFiles(directory).Where(IsStoreTempName).ToArray();
            var expectedBytes = MapCodec.Encode(first);

            var failingStore = new MapFileStore((temp, destination) =>
            {
                Assert.Equal(expectedBytes, File.ReadAllBytes(temp));
                throw new IOException("injected pre-move failure");
            });

            Assert.Throws<IOException>(() => failingStore.Save(path, first, revision));
            Assert.Equal(originalBytes, File.ReadAllBytes(path));
            AssertNoStoreTemps(directory, before);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Save_ReplaceRunsAfterTempStreamIsClosed()
    {
        var directory = CreateTempDirectory();
        try
        {
            var document = CreateDocument();
            var path = Path.Combine(directory, "map.dat");
            var store = new MapFileStore((temp, destination) =>
            {
                using var exclusive = new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.None);
                File.Move(temp, destination, overwrite: true);
            });

            store.Save(path, document);

            Assert.Equal(MapCodec.Encode(document), File.ReadAllBytes(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Save_ExpectedRevisionDetectsChangedContentEvenWhenLengthAndTimestampMatch()
    {
        var directory = CreateTempDirectory();
        try
        {
            var document = CreateDocument();
            var path = Path.Combine(directory, "map.dat");
            var store = new MapFileStore();
            var revision = store.Save(path, document);

            var external = CreateDocument();
            external.SetFlags(2, 1, MapDocument.BlockedFlag);
            var externalBytes = MapCodec.Encode(external);
            Assert.Equal(MapCodec.Encode(document).Length, externalBytes.Length);
            File.WriteAllBytes(path, externalBytes);
            File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path) - TimeSpan.FromMinutes(5));

            var exception = Assert.Throws<MapExternalChangeException>(() => store.Save(path, document, revision));
            Assert.Equal(revision, exception.ExpectedRevision);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(externalBytes)), exception.ActualRevision!.Value.ContentHash);
            Assert.Equal(externalBytes, File.ReadAllBytes(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Save_ExpectedRevisionTreatsDeletedDestinationAsExternalChange()
    {
        var directory = CreateTempDirectory();
        try
        {
            var document = CreateDocument();
            var path = Path.Combine(directory, "map.dat");
            var store = new MapFileStore();
            var revision = store.Save(path, document);
            File.Delete(path);

            var exception = Assert.Throws<MapExternalChangeException>(() => store.Save(path, document, revision));

            Assert.Equal(revision, exception.ExpectedRevision);
            Assert.Null(exception.ActualRevision);
            Assert.Equal(Path.GetFullPath(path), exception.Path);
            Assert.False(File.Exists(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void Save_CleanupFailureDoesNotMaskPrimaryFailure()
    {
        const UnixFileMode readExecute = UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead
            | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
        const UnixFileMode writable = readExecute | UnixFileMode.UserWrite;
        var directory = CreateTempDirectory();
        try
        {
            var document = CreateDocument();
            var path = Path.Combine(directory, "map.dat");
            var store = new MapFileStore();
            var revision = store.Save(path, document);
            var originalBytes = File.ReadAllBytes(path);
            var primary = new IOException("injected replacement failure");
            var called = false;

            var failingStore = new MapFileStore((temp, destination) =>
            {
                called = true;
                // A non-writable directory makes the store's post-failure File.Delete(temp) fail with a permission error.
                File.SetUnixFileMode(directory, readExecute);
                throw primary;
            });

            var thrown = Assert.Throws<IOException>(() => failingStore.Save(path, document, revision));

            Assert.True(called);
            Assert.Same(primary, thrown);
            Assert.Equal(originalBytes, File.ReadAllBytes(path));
        }
        finally
        {
            File.SetUnixFileMode(directory, writable);
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void Save_CleanupFailureOnSuccessPathIsSurfaced()
    {
        const UnixFileMode readExecute = UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead
            | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
        const UnixFileMode writable = readExecute | UnixFileMode.UserWrite;
        var directory = CreateTempDirectory();
        try
        {
            var first = CreateDocument();
            var path = Path.Combine(directory, "map.dat");
            var store = new MapFileStore();
            store.Save(path, first);

            var second = CreateDocument();
            second.SetFlags(3, 2, MapDocument.BlockedFlag);
            var secondBytes = MapCodec.Encode(second);

            var failingStore = new MapFileStore((temp, destination) =>
            {
                File.Move(temp, destination, overwrite: true);
                File.WriteAllText(temp, "leftover");
                File.SetUnixFileMode(directory, readExecute);
            });

            Assert.Throws<UnauthorizedAccessException>(() => failingStore.Save(path, second));

            Assert.Equal(secondBytes, File.ReadAllBytes(path));
        }
        finally
        {
            File.SetUnixFileMode(directory, writable);
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Save_ExpectedRevisionAllowsSameContentReplacement()
    {
        var directory = CreateTempDirectory();
        try
        {
            var document = CreateDocument();
            var path = Path.Combine(directory, "map.dat");
            var store = new MapFileStore();
            var revision = store.Save(path, document);
            var bytes = File.ReadAllBytes(path);
            File.Delete(path);
            File.WriteAllBytes(path, bytes);

            var newRevision = store.Save(path, document, revision);

            Assert.Equal(revision, newRevision);
            Assert.Equal(bytes, File.ReadAllBytes(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Save_WithoutExpectedRevisionImplementsSaveAsOverwrite()
    {
        var directory = CreateTempDirectory();
        try
        {
            var first = CreateDocument();
            var path = Path.Combine(directory, "map.dat");
            var firstBytes = MapCodec.Encode(first);
            File.WriteAllBytes(path, firstBytes);

            var second = CreateDocument();
            second.SetFlags(3, 2, MapDocument.BlockedFlag);
            var secondBytes = MapCodec.Encode(second);
            var revision = new MapFileStore().Save(path, second);

            Assert.Equal(secondBytes, File.ReadAllBytes(path));
            Assert.Equal(Convert.ToHexString(SHA256.HashData(secondBytes)), revision.ContentHash);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ExternalChangeFailure_CleansTempAndDoesNotReturnNewRevision()
    {
        var directory = CreateTempDirectory();
        try
        {
            var document = CreateDocument();
            var path = Path.Combine(directory, "map.dat");
            var store = new MapFileStore();
            var revision = store.Save(path, document);
            var before = Directory.EnumerateFiles(directory).Where(IsStoreTempName).ToArray();

            var external = CreateDocument();
            external.SetFlags(0, 1, MapDocument.BlockedFlag);
            File.WriteAllBytes(path, MapCodec.Encode(external));

            Assert.Throws<MapExternalChangeException>(() => store.Save(path, document, revision));
            AssertNoStoreTemps(directory, before);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
