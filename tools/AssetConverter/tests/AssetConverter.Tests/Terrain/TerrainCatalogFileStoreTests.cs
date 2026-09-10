using System.Text;
using Goose2.AssetConverter.Terrain;

namespace AssetConverter.Tests.Terrain;

public class TerrainCatalogFileStoreTests
{
    private const string Catalog = "{\"tileSize\":32,\"note\":\"café\"}";

    [Fact]
    public void Write_UsesSiblingCreateNewAndDurableFlushBeforeReplace()
    {
        var root = NewRepoRoot();
        try
        {
            var expected = Encoding.UTF8.GetBytes(Catalog);
            var destination = DestinationPath(root);
            var prior = new byte[] { 1, 2, 3 };
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllBytes(destination, prior);

            var operations = new FakeCatalogFileOperations();
            new TerrainCatalogFileStore(operations).Write(root, Catalog);

            Assert.Equal(expected, File.ReadAllBytes(destination));
            var written = operations.LastStream!.Written.ToArray();
            Assert.Equal(expected, written);
            Assert.False(written.Length >= 3 && written[0] == 0xEF && written[1] == 0xBB && written[2] == 0xBF);

            var create = operations.Calls.Single(c => c.Op == "CreateFile");
            Assert.Equal(Path.GetDirectoryName(destination), Path.GetDirectoryName(create.Path));
            Assert.Matches(@"^terrain-brushes\.json\.tmp-[0-9a-f]{32}$", Path.GetFileName(create.Path));

            Assert.Equal(
                new[] { "CreateFile", "FlushToDisk", "Exists", "Replace" },
                operations.Calls.Select(c => c.Op).ToArray());
            Assert.DoesNotContain(operations.Calls, c => c.Op == "Delete");
            Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(destination)!, "*.tmp-*"));

            File.Delete(destination);
            var moveOperations = new FakeCatalogFileOperations();
            new TerrainCatalogFileStore(moveOperations).Write(root, Catalog);
            Assert.Equal(
                new[] { "CreateFile", "FlushToDisk", "Exists", "Move" },
                moveOperations.Calls.Select(c => c.Op).ToArray());
            Assert.Equal(expected, File.ReadAllBytes(destination));
            Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(destination)!, "*.tmp-*"));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Theory]
    [InlineData("Write")]
    [InlineData("FlushToDisk")]
    [InlineData("Dispose")]
    [InlineData("Replace")]
    public void Write_WriteFlushDisposeOrReplaceFailurePreservesPriorBytesAndDeletesTemp(string failurePoint)
    {
        var root = NewRepoRoot();
        try
        {
            var destination = DestinationPath(root);
            var prior = new byte[] { 9, 8, 7, 6, 5, 4, 3, 2, 1 };
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllBytes(destination, prior);

            var operations = new FakeCatalogFileOperations();
            var primary = new IOException("injected " + failurePoint + " failure");
            switch (failurePoint)
            {
                case "Write":
                    operations.StreamWriteException = primary;
                    break;
                case "Dispose":
                    operations.StreamDisposeException = primary;
                    break;
                default:
                    operations.FailAt(failurePoint, primary);
                    break;
            }

            var thrown = Assert.Throws<IOException>(() => new TerrainCatalogFileStore(operations).Write(root, Catalog));

            Assert.Same(primary, thrown);
            Assert.Empty(thrown.Data);
            Assert.Equal(prior, File.ReadAllBytes(destination));
            Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(destination)!, "*.tmp-*"));

            var temp = operations.Calls.Single(c => c.Op == "CreateFile").Path;
            var deleteCalls = operations.Calls.Where(c => c.Op == "Delete").ToList();
            Assert.Single(deleteCalls);
            Assert.Equal(temp, deleteCalls[0].Path);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void Write_MoveFailureWithoutPriorCatalogLeavesNoDestinationOrTemp()
    {
        var root = NewRepoRoot();
        try
        {
            var destination = DestinationPath(root);
            var operations = new FakeCatalogFileOperations();
            var primary = new IOException("injected move failure");
            operations.FailAt("Move", primary);

            var thrown = Assert.Throws<IOException>(() => new TerrainCatalogFileStore(operations).Write(root, Catalog));

            Assert.Same(primary, thrown);
            Assert.False(File.Exists(destination));
            Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(destination)!, "*.tmp-*"));
            Assert.Contains(operations.Calls, c => c.Op == "Move");
            Assert.DoesNotContain(operations.Calls, c => c.Op == "Replace");

            var temp = operations.Calls.Single(c => c.Op == "CreateFile").Path;
            var deleteCalls = operations.Calls.Where(c => c.Op == "Delete").ToList();
            Assert.Single(deleteCalls);
            Assert.Equal(temp, deleteCalls[0].Path);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void Write_DeleteFailureRethrowsPrimaryExceptionAndRecordsCleanupFailure()
    {
        var root = NewRepoRoot();
        try
        {
            var destination = DestinationPath(root);
            var prior = new byte[] { 4, 2 };
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllBytes(destination, prior);

            var operations = new FakeCatalogFileOperations();
            var primary = new IOException("injected replace failure");
            var cleanup = new IOException("injected delete failure");
            operations.FailAt("Replace", primary);
            operations.FailAt("Delete", cleanup);

            var thrown = Assert.Throws<IOException>(() => new TerrainCatalogFileStore(operations).Write(root, Catalog));

            Assert.Same(primary, thrown);
            Assert.Same(cleanup, thrown.Data[TerrainCatalogFileStore.CleanupExceptionKey]!);
            Assert.NotNull(thrown.StackTrace);
            Assert.Contains("ThrowIfFailing", thrown.StackTrace!);
            Assert.Equal(prior, File.ReadAllBytes(destination));

            var temp = operations.Calls.Single(c => c.Op == "CreateFile").Path;
            Assert.True(File.Exists(temp));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void Write_ProductionStoreWritesCatalogAndLeavesNoTempFile()
    {
        var root = NewRepoRoot();
        try
        {
            var destination = DestinationPath(root);
            new TerrainCatalogFileStore().Write(root, Catalog);
            Assert.Equal(Encoding.UTF8.GetBytes(Catalog), File.ReadAllBytes(destination));
            Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(destination)!, "*.tmp-*"));

            var second = "{\"tileSize\":64}";
            new TerrainCatalogFileStore().Write(root, second);
            Assert.Equal(Encoding.UTF8.GetBytes(second), File.ReadAllBytes(destination));
            Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(destination)!, "*.tmp-*"));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void SystemFileOperations_FlushToDiskRequiresFileStream()
    {
        var operations = new TerrainCatalogFileStore.SystemFileOperations();
        using var stream = new MemoryStream();
        Assert.Throws<ArgumentException>(() => operations.FlushToDisk(stream));
    }

    private static string NewRepoRoot()
        => Path.Combine(Path.GetTempPath(), "ac_terrain_catalog_" + Guid.NewGuid().ToString("N"));

    private static string DestinationPath(string repoRoot)
        => Path.Combine(repoRoot, TerrainCatalogFileStore.RelativePath);

    private static void Cleanup(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class FakeCatalogFileOperations : ITerrainCatalogFileOperations
    {
        private readonly Dictionary<string, Exception> _failures = new();

        public List<(string Op, string Path)> Calls { get; } = new();
        public RecordingStream? LastStream { get; private set; }
        public Exception? StreamWriteException { get; set; }
        public Exception? StreamDisposeException { get; set; }

        public void FailAt(string operation, Exception exception) => _failures[operation] = exception;

        private void ThrowIfFailing(string operation)
        {
            if (_failures.TryGetValue(operation, out var exception))
            {
                throw exception;
            }
        }

        public bool Exists(string path)
        {
            Calls.Add(("Exists", path));
            ThrowIfFailing("Exists");
            return File.Exists(path);
        }

        public Stream CreateFile(string path)
        {
            Calls.Add(("CreateFile", path));
            ThrowIfFailing("CreateFile");
            var stream = new RecordingStream(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                WriteException = StreamWriteException,
                DisposeException = StreamDisposeException
            };
            LastStream = stream;
            return stream;
        }

        public void FlushToDisk(Stream stream)
        {
            Calls.Add(("FlushToDisk", string.Empty));
            ThrowIfFailing("FlushToDisk");
            stream.Flush();
        }

        public void Replace(string sourcePath, string destinationPath)
        {
            Calls.Add(("Replace", destinationPath));
            ThrowIfFailing("Replace");
            File.Move(sourcePath, destinationPath, overwrite: true);
        }

        public void Move(string sourcePath, string destinationPath)
        {
            Calls.Add(("Move", destinationPath));
            ThrowIfFailing("Move");
            File.Move(sourcePath, destinationPath);
        }

        public void Delete(string path)
        {
            Calls.Add(("Delete", path));
            ThrowIfFailing("Delete");
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private sealed class RecordingStream : Stream
    {
        private readonly Stream _inner;
        private readonly List<byte> _written = new();

        public RecordingStream(Stream inner)
        {
            _inner = inner;
        }

        public Exception? WriteException { get; set; }
        public Exception? DisposeException { get; set; }
        public IReadOnlyList<byte> Written => _written;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _inner.Length;

        public override void Flush() => _inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (WriteException != null)
            {
                throw WriteException;
            }

            _inner.Write(buffer, offset, count);
            _written.AddRange(buffer.AsSpan(offset, count));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
                if (DisposeException != null)
                {
                    throw DisposeException;
                }
            }

            base.Dispose(disposing);
        }
    }
}
