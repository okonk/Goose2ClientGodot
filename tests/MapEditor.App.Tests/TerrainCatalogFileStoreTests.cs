using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MapEditor.App.Terrain;
using MapEditor.Core;
using MapEditor.Rendering;
using Xunit;

namespace MapEditor.App.Tests;

public class TerrainCatalogFileStoreTests
{
    private const string EmptyFileHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
    private const string AssetDirectory = "assets";
    private static readonly string SourcePath = Path.Combine(Path.GetFullPath(AssetDirectory), "terrain-brushes.json");

    private static readonly Guid GrassId = new("00000000-0000-0000-0000-000000000001");
    private static readonly Guid DirtId = new("00000000-0000-0000-0000-000000000002");

    [Fact]
    public void Open_Valid_ReturnsCatalogIndexAndRevisionOfExactBytes()
    {
        var catalog = CreateCatalog((GrassId, "Grass", 1, 10), (DirtId, "Dirt", 1, 20));
        var bytes = Encoding.UTF8.GetBytes(TerrainCatalogJson.Serialize(catalog));
        var operations = CreateOpenOperations(bytes);

        var result = new TerrainCatalogFileStore(operations).Open(AssetDirectory, CreateManifest("{ \"1\": { \"10\": [0, 0, 32, 32], \"20\": [32, 0, 32, 32] } }"));

        Assert.True(result.IsValid);
        Assert.True(result.CanAuthor);
        Assert.True(result.CanPaint);
        Assert.Null(result.Diagnostic);
        Assert.Equal(SourcePath, result.SourcePath);
        Assert.Equal(Hash(bytes), result.Revision.ContentHash);
        Assert.Equal(catalog, result.Catalog);
        Assert.NotNull(result.Index);
        Assert.True(result.Index.TryGetGraphic(new TerrainGraphicReference(1, 10), out _));
    }

    [Fact]
    public void Open_MissingFile_ReturnsValidEmptyWithMissingRevision()
    {
        var operations = CreateOpenOperations(null);

        var result = new TerrainCatalogFileStore(operations).Open(AssetDirectory, CreateManifest("{}"));

        Assert.True(result.IsValid);
        Assert.True(result.CanAuthor);
        Assert.False(result.CanPaint);
        Assert.Null(result.Catalog);
        Assert.Null(result.Index);
        Assert.Empty(result.Issues);
        Assert.Null(result.Diagnostic);
        Assert.Equal(TerrainFileRevision.Missing, result.Revision);
    }

    [Fact]
    public void Open_MissingAssetRoot_ReturnsUnavailable()
    {
        var operations = new FakeFileOperations();

        var result = new TerrainCatalogFileStore(operations).Open(AssetDirectory, CreateManifest("{}"));

        Assert.False(result.IsValid);
        Assert.False(result.CanAuthor);
        Assert.False(result.CanPaint);
        Assert.NotNull(result.Diagnostic);
        Assert.Equal(TerrainFileRevision.Missing, result.Revision);
        Assert.Empty(operations.Events);
    }

    [Fact]
    public void Open_MalformedFile_ReturnsInvalidWithRawRevision()
    {
        var bytes = Encoding.UTF8.GetBytes("{ \"version\": 1,");
        var operations = CreateOpenOperations(bytes);

        var result = new TerrainCatalogFileStore(operations).Open(AssetDirectory, CreateManifest("{}"));

        Assert.False(result.IsValid);
        Assert.True(result.CanAuthor);
        Assert.False(result.CanPaint);
        Assert.Null(result.Catalog);
        Assert.Null(result.Index);
        Assert.Empty(result.Issues);
        Assert.True(result.Revision.Exists);
        Assert.Equal(Hash(bytes), result.Revision.ContentHash);
        Assert.NotNull(result.Diagnostic);
    }

    [Fact]
    public void Open_ValidationFailure_ReturnsInvalidWithRevisionAndIssues()
    {
        var catalog = CreateCatalog((GrassId, "Grass", 1, 10));
        var bytes = Encoding.UTF8.GetBytes(TerrainCatalogJson.Serialize(catalog));
        var operations = CreateOpenOperations(bytes);

        var result = new TerrainCatalogFileStore(operations).Open(AssetDirectory, CreateManifest("{}"));

        Assert.False(result.IsValid);
        Assert.Null(result.Index);
        Assert.True(result.Revision.Exists);
        Assert.Equal(Hash(bytes), result.Revision.ContentHash);
        Assert.Contains(result.Issues, issue =>
            issue.Code == TerrainValidationCode.MissingSpriteFrame
            && issue.GraphicReference == new TerrainGraphicReference(1, 10));
    }

    [Fact]
    public void ReadRevision_MissingAndEmpty_AreDistinct()
    {
        var missing = new TerrainCatalogFileStore(CreateOpenOperations(null))
            .Open(AssetDirectory, CreateManifest("{}"))
            .Revision;

        var empty = new TerrainCatalogFileStore(CreateOpenOperations(Array.Empty<byte>()))
            .Open(AssetDirectory, CreateManifest("{}"))
            .Revision;

        Assert.Equal(TerrainFileRevision.Missing, missing);
        Assert.False(missing.Exists);
        Assert.True(empty.Exists);
        Assert.Equal(EmptyFileHash, empty.ContentHash);
        Assert.NotEqual(missing, empty);
    }

    [Fact]
    public void PrepareSave_ValidCatalog_ReturnsCanonicalBytesWithoutAnyI_O()
    {
        var catalog = CreateCatalog((GrassId, "Grass", 1, 10), (DirtId, "Dirt", 1, 20));
        var operations = new FakeFileOperations();
        var store = new TerrainCatalogFileStore(operations);
        var expectedRevision = TerrainFileRevision.Missing;

        var prepared = store.PrepareSave(AssetDirectory, catalog, CreateManifest("{ \"1\": { \"10\": [0, 0, 32, 32], \"20\": [32, 0, 32, 32] } }"), expectedRevision);

        Assert.Equal(Encoding.UTF8.GetBytes(TerrainCatalogJson.Serialize(catalog)), prepared.CanonicalBytes);
        Assert.Same(catalog, prepared.Catalog);
        Assert.NotNull(prepared.Index);
        Assert.Equal(SourcePath, prepared.SourcePath);
        Assert.Equal(expectedRevision, prepared.ExpectedRevision);
        Assert.False(string.IsNullOrWhiteSpace(prepared.OperationId));
        Assert.Empty(operations.Events);
    }

    [Fact]
    public void PrepareSave_DeterministicBytes_RegardlessOfInputOrder()
    {
        var first = CreateCatalog((GrassId, "Grass", 1, 10), (DirtId, "Dirt", 1, 20));
        var second = CreateCatalog((DirtId, "Dirt", 1, 20), (GrassId, "Grass", 1, 10));
        var manifest = CreateManifest("{ \"1\": { \"10\": [0, 0, 32, 32], \"20\": [32, 0, 32, 32] } }");
        var store = new TerrainCatalogFileStore(new FakeFileOperations());

        var preparedFirst = store.PrepareSave(AssetDirectory, first, manifest, TerrainFileRevision.Missing);
        var preparedSecond = store.PrepareSave(AssetDirectory, second, manifest, TerrainFileRevision.Missing);

        Assert.Equal(preparedFirst.CanonicalBytes, preparedSecond.CanonicalBytes);
        Assert.NotEqual(preparedFirst.OperationId, preparedSecond.OperationId);
    }

    [Fact]
    public void PrepareSave_InvalidCatalog_ThrowsAndPerformsNoI_O()
    {
        var catalog = CreateCatalog((GrassId, "Grass", 1, 10));
        var operations = new FakeFileOperations();
        var store = new TerrainCatalogFileStore(operations);

        var exception = Assert.Throws<TerrainCatalogValidationException>(
            () => store.PrepareSave(AssetDirectory, catalog, CreateManifest("{}"), TerrainFileRevision.Missing));

        Assert.Contains(exception.Issues, issue => issue.Severity == TerrainValidationSeverity.Error);
        Assert.Empty(operations.Events);
        Assert.Empty(operations.DeletedPaths);
    }

    [Fact]
    public void Save_NewDestination_WritesExactBytesAndReturnsPreparedResult()
    {
        var catalog = CreateCatalog((GrassId, "Grass", 1, 10));
        var manifest = CreateManifest("{ \"1\": { \"10\": [0, 0, 32, 32] } }");
        var operations = new FakeFileOperations();
        var store = new TerrainCatalogFileStore(operations);
        var prepared = store.PrepareSave(AssetDirectory, catalog, manifest, TerrainFileRevision.Missing);

        var result = store.Save(prepared);

        Assert.Equal(prepared.CanonicalBytes, operations.ReadFileDirect(SourcePath));
        Assert.Equal(prepared.OperationId, result.OperationId);
        Assert.Same(prepared.Catalog, result.Catalog);
        Assert.Same(prepared.Index, result.Index);
        Assert.Equal(Hash(prepared.CanonicalBytes), result.Revision.ContentHash);
        var tempPath = operations.Events.Single(e => e.StartsWith("create:")).Substring("create:".Length);
        Assert.StartsWith(".terrain-", Path.GetFileName(tempPath), StringComparison.Ordinal);
        Assert.EndsWith(".tmp", tempPath, StringComparison.Ordinal);
        Assert.Contains(prepared.OperationId, tempPath);
        Assert.False(operations.FileExists(tempPath));
    }

    [Fact]
    public void Save_FlushesAndMovesBeforeReturningResult()
    {
        var catalog = CreateCatalog((GrassId, "Grass", 1, 10));
        var manifest = CreateManifest("{ \"1\": { \"10\": [0, 0, 32, 32] } }");
        var operations = new FakeFileOperations();
        var store = new TerrainCatalogFileStore(operations);
        var prepared = store.PrepareSave(AssetDirectory, catalog, manifest, TerrainFileRevision.Missing);

        store.Save(prepared);

        var events = operations.Events.Where(e => e is "write" or "flush" or "move").ToList();
        Assert.Equal(new[] { "write", "flush", "move" }, events);
    }

    [Fact]
    public void Save_ExistingDestinationMatchingRevision_ReplacesAtomicallyOnce()
    {
        var catalog = CreateCatalog((GrassId, "Grass", 1, 10));
        var manifest = CreateManifest("{ \"1\": { \"10\": [0, 0, 32, 32] } }");
        var operations = new FakeFileOperations();
        var store = new TerrainCatalogFileStore(operations);
        var original = store.PrepareSave(AssetDirectory, catalog, manifest, TerrainFileRevision.Missing);
        store.Save(original);
        var originalBytes = operations.ReadFileDirect(SourcePath)!;

        var updatedCatalog = CreateCatalog((GrassId, "Grass", 1, 10), (DirtId, "Dirt", 1, 20));
        var updatedManifest = CreateManifest("{ \"1\": { \"10\": [0, 0, 32, 32], \"20\": [32, 0, 32, 32] } }");
        var updated = store.PrepareSave(AssetDirectory, updatedCatalog, updatedManifest, TerrainFileRevision.FromBytes(originalBytes));
        var movesBefore = operations.MoveCalls;

        var result = store.Save(updated);

        Assert.Equal(movesBefore + 1, operations.MoveCalls);
        Assert.Equal(updated.CanonicalBytes, operations.ReadFileDirect(SourcePath));
        Assert.NotEqual(originalBytes, updated.CanonicalBytes);
        Assert.Equal(Hash(updated.CanonicalBytes), result.Revision.ContentHash);
    }

    [Fact]
    public void Save_NewlyAppearedDestination_ThrowsExternalChangeAndLeavesDestinationIntact()
    {
        var catalog = CreateCatalog((GrassId, "Grass", 1, 10));
        var manifest = CreateManifest("{ \"1\": { \"10\": [0, 0, 32, 32] } }");
        var operations = new FakeFileOperations();
        var store = new TerrainCatalogFileStore(operations);
        var prepared = store.PrepareSave(AssetDirectory, catalog, manifest, TerrainFileRevision.Missing);
        var externalBytes = Encoding.UTF8.GetBytes("{ \"external\": true }");
        operations.WriteFile(SourcePath, externalBytes);

        var exception = Assert.Throws<TerrainExternalChangeException>(() => store.Save(prepared));

        Assert.Equal(SourcePath, exception.Path);
        Assert.Equal(TerrainFileRevision.Missing, exception.ExpectedRevision);
        Assert.Equal(Hash(externalBytes), exception.ActualRevision!.Value.ContentHash);
        Assert.Equal(externalBytes, operations.ReadFileDirect(SourcePath));
        Assert.False(operations.FileExists(TempPathOf(operations)));
    }

    [Fact]
    public void Save_ChangedRevision_LeavesDestinationUntouched()
    {
        var catalog = CreateCatalog((GrassId, "Grass", 1, 10));
        var manifest = CreateManifest("{ \"1\": { \"10\": [0, 0, 32, 32] } }");
        var operations = new FakeFileOperations();
        var store = new TerrainCatalogFileStore(operations);
        var original = store.PrepareSave(AssetDirectory, catalog, manifest, TerrainFileRevision.Missing);
        store.Save(original);
        var originalBytes = operations.ReadFileDirect(SourcePath)!;

        var updatedCatalog = CreateCatalog((GrassId, "Grass", 1, 10), (DirtId, "Dirt", 1, 20));
        var updatedManifest = CreateManifest("{ \"1\": { \"10\": [0, 0, 32, 32], \"20\": [32, 0, 32, 32] } }");
        var updated = store.PrepareSave(AssetDirectory, updatedCatalog, updatedManifest, TerrainFileRevision.FromBytes(originalBytes));
        var externalBytes = Encoding.UTF8.GetBytes(TerrainCatalogJson.Serialize(CreateCatalog((DirtId, "Dirt", 1, 20))));
        operations.WriteFile(SourcePath, externalBytes);

        var exception = Assert.Throws<TerrainExternalChangeException>(() => store.Save(updated));

        Assert.Equal(Hash(originalBytes), exception.ExpectedRevision.ContentHash);
        Assert.Equal(Hash(externalBytes), exception.ActualRevision!.Value.ContentHash);
        Assert.Equal(externalBytes, operations.ReadFileDirect(SourcePath));
        Assert.False(operations.FileExists(TempPathOf(operations)));
    }

    [Fact]
    public void Save_DestinationDeletedAfterPrepare_ThrowsExternalChangeWithMissingActual()
    {
        var catalog = CreateCatalog((GrassId, "Grass", 1, 10));
        var manifest = CreateManifest("{ \"1\": { \"10\": [0, 0, 32, 32] } }");
        var operations = new FakeFileOperations();
        var store = new TerrainCatalogFileStore(operations);
        var original = store.PrepareSave(AssetDirectory, catalog, manifest, TerrainFileRevision.Missing);
        store.Save(original);
        var revision = TerrainFileRevision.FromBytes(operations.ReadFileDirect(SourcePath)!);
        operations.Delete(SourcePath);

        var prepared = store.PrepareSave(AssetDirectory, catalog, manifest, revision);

        var exception = Assert.Throws<TerrainExternalChangeException>(() => store.Save(prepared));

        Assert.Equal(revision, exception.ExpectedRevision);
        Assert.Equal(TerrainFileRevision.Missing, exception.ActualRevision);
    }

    [Fact]
    public void Save_LeavesForeignTempFilesUntouched()
    {
        var catalog = CreateCatalog((GrassId, "Grass", 1, 10));
        var manifest = CreateManifest("{ \"1\": { \"10\": [0, 0, 32, 32] } }");
        var operations = new FakeFileOperations();
        var store = new TerrainCatalogFileStore(operations);
        var foreignTemp = Path.Combine(Path.GetDirectoryName(SourcePath)!, ".terrain-0123456789abcdef0123456789abcdef.tmp");
        var foreignBytes = new byte[] { 1, 2, 3 };
        operations.WriteFile(foreignTemp, foreignBytes);

        var prepared = store.PrepareSave(AssetDirectory, catalog, manifest, TerrainFileRevision.Missing);
        var result = store.Save(prepared);
        Assert.Equal(foreignBytes, operations.ReadFileDirect(foreignTemp));

        operations.WriteFile(SourcePath, Encoding.UTF8.GetBytes("{ \"external\": true }"));
        var conflictPrepared = store.PrepareSave(AssetDirectory, catalog, manifest, result.Revision);
        Assert.Throws<TerrainExternalChangeException>(() => store.Save(conflictPrepared));
        Assert.Equal(foreignBytes, operations.ReadFileDirect(foreignTemp));
    }

    [Theory]
    [InlineData("create")]
    [InlineData("write")]
    [InlineData("flush")]
    [InlineData("dispose")]
    [InlineData("move")]
    public void Save_InjectedFailure_ThrowsPrimaryLeavesDestinationIntactAndCleansTemp(string failure)
    {
        var catalog = CreateCatalog((GrassId, "Grass", 1, 10));
        var manifest = CreateManifest("{ \"1\": { \"10\": [0, 0, 32, 32] } }");
        var operations = new FakeFileOperations();
        var store = new TerrainCatalogFileStore(operations);
        var original = store.PrepareSave(AssetDirectory, catalog, manifest, TerrainFileRevision.Missing);
        store.Save(original);
        var originalBytes = operations.ReadFileDirect(SourcePath)!;
        var updatedCatalog = CreateCatalog((GrassId, "Grass", 1, 10), (DirtId, "Dirt", 1, 20));
        var updatedManifest = CreateManifest("{ \"1\": { \"10\": [0, 0, 32, 32], \"20\": [32, 0, 32, 32] } }");
        var prepared = store.PrepareSave(AssetDirectory, updatedCatalog, updatedManifest, TerrainFileRevision.FromBytes(originalBytes));
        var primary = new IOException($"injected {failure} failure");
        switch (failure)
        {
            case "create": operations.CreateFailure = primary; break;
            case "write": operations.WriteFailure = primary; break;
            case "flush": operations.FlushFailure = primary; break;
            case "dispose": operations.DisposeFailure = primary; break;
            case "move": operations.MoveFailure = primary; break;
        }

        var thrown = Assert.Throws<IOException>(() => store.Save(prepared));

        Assert.Same(primary, thrown);
        Assert.Equal(originalBytes, operations.ReadFileDirect(SourcePath));
        Assert.False(operations.FileExists(TempPathOf(operations)));
    }

    [Fact]
    public void Save_CleanupDeleteFailure_DoesNotMaskPrimaryException()
    {
        var catalog = CreateCatalog((GrassId, "Grass", 1, 10));
        var manifest = CreateManifest("{ \"1\": { \"10\": [0, 0, 32, 32] } }");
        var operations = new FakeFileOperations();
        var store = new TerrainCatalogFileStore(operations);
        var original = store.PrepareSave(AssetDirectory, catalog, manifest, TerrainFileRevision.Missing);
        store.Save(original);
        var originalBytes = operations.ReadFileDirect(SourcePath)!;
        var prepared = store.PrepareSave(AssetDirectory, catalog, manifest, TerrainFileRevision.FromBytes(originalBytes));
        var primary = new IOException("injected move failure");
        operations.MoveFailure = primary;
        operations.DeleteFailure = new UnauthorizedAccessException("injected delete failure");

        var thrown = Assert.Throws<IOException>(() => store.Save(prepared));

        Assert.Same(primary, thrown);
        Assert.Equal(originalBytes, operations.ReadFileDirect(SourcePath));
    }

    [Fact]
    public void Save_RealFile_NewDestination_WritesExactBytesAndLeavesNoTemp()
    {
        var directory = CreateTempDirectory();
        try
        {
            var catalog = CreateCatalog((GrassId, "Grass", 1, 10));
            var manifest = CreateManifest("{ \"1\": { \"10\": [0, 0, 32, 32] } }");
            var store = new TerrainCatalogFileStore();
            var prepared = store.PrepareSave(directory, catalog, manifest, TerrainFileRevision.Missing);

            var result = store.Save(prepared);

            var path = Path.Combine(directory, TerrainAssetCatalog.FileName);
            Assert.Equal(prepared.CanonicalBytes, File.ReadAllBytes(path));
            Assert.Equal(Hash(prepared.CanonicalBytes), result.Revision.ContentHash);
            Assert.DoesNotContain(Directory.EnumerateFiles(directory), name =>
                Path.GetFileName(name).StartsWith(".terrain-", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Save_RealFile_SecondSaveWithReturnedRevision_ReplacesDestination()
    {
        var directory = CreateTempDirectory();
        try
        {
            var manifest = CreateManifest("{ \"1\": { \"10\": [0, 0, 32, 32], \"20\": [32, 0, 32, 32] } }");
            var store = new TerrainCatalogFileStore();
            var path = Path.Combine(directory, TerrainAssetCatalog.FileName);
            var first = store.PrepareSave(directory, CreateCatalog((GrassId, "Grass", 1, 10)), manifest, TerrainFileRevision.Missing);
            var firstResult = store.Save(first);

            var second = store.PrepareSave(
                directory,
                CreateCatalog((GrassId, "Grass", 1, 10), (DirtId, "Dirt", 1, 20)),
                manifest,
                firstResult.Revision);
            var secondResult = store.Save(second);

            Assert.Equal(second.CanonicalBytes, File.ReadAllBytes(path));
            Assert.Equal(Hash(second.CanonicalBytes), secondResult.Revision.ContentHash);
            var reopened = store.Open(directory, manifest);
            Assert.True(reopened.IsValid);
            Assert.Equal(secondResult.Revision, reopened.Revision);
            Assert.Equal(second.Catalog, reopened.Catalog);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Save_RealFile_ChangedDestination_ThrowsAndLeavesBytesAndNoTemp()
    {
        var directory = CreateTempDirectory();
        try
        {
            var manifest = CreateManifest("{ \"1\": { \"10\": [0, 0, 32, 32], \"20\": [32, 0, 32, 32] } }");
            var store = new TerrainCatalogFileStore();
            var path = Path.Combine(directory, TerrainAssetCatalog.FileName);
            var first = store.PrepareSave(directory, CreateCatalog((GrassId, "Grass", 1, 10)), manifest, TerrainFileRevision.Missing);
            store.Save(first);
            var originalBytes = File.ReadAllBytes(path);
            var before = Directory.EnumerateFiles(directory).ToArray();

            var externalBytes = Encoding.UTF8.GetBytes(TerrainCatalogJson.Serialize(CreateCatalog((DirtId, "Dirt", 1, 20))));
            File.WriteAllBytes(path, externalBytes);
            var prepared = store.PrepareSave(
                directory,
                CreateCatalog((GrassId, "Grass", 1, 10), (DirtId, "Dirt", 1, 20)),
                manifest,
                TerrainFileRevision.FromBytes(originalBytes));

            Assert.Throws<TerrainExternalChangeException>(() => store.Save(prepared));

            Assert.Equal(externalBytes, File.ReadAllBytes(path));
            Assert.DoesNotContain(Directory.EnumerateFiles(directory), name =>
                Path.GetFileName(name).StartsWith(".terrain-", StringComparison.Ordinal));
            Assert.Equal(before.Length, Directory.EnumerateFiles(directory).Count());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static FakeFileOperations CreateOpenOperations(byte[]? fileBytes)
    {
        var operations = new FakeFileOperations();
        operations.AddDirectory(AssetDirectory);
        if (fileBytes is not null)
        {
            operations.WriteFile(SourcePath, fileBytes);
        }
        return operations;
    }

    private static string TempPathOf(FakeFileOperations operations)
        => operations.Events.Last(e => e.StartsWith("create:")).Substring("create:".Length);

    private static string Hash(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static TerrainCatalog CreateCatalog(params (Guid Id, string Name, int Sheet, int Graphic)[] entries)
    {
        var terrains = new List<TerrainDefinition>();
        var graphics = new List<TerrainGraphicDefinition>();
        foreach (var (id, name, sheet, graphic) in entries)
        {
            terrains.Add(new TerrainDefinition(id, name, null));
            graphics.Add(new TerrainGraphicDefinition(new TerrainGraphicReference(sheet, graphic), new TerrainPattern(Center: id)));
        }

        return new TerrainCatalog(terrains, graphics);
    }

    private static SpriteManifest CreateManifest(string sheetsJson)
        => SpriteManifest.Parse($"{{ \"tileSize\": 32, \"sheets\": {sheetsJson} }}");

    private static string CreateTempDirectory()
        => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())).FullName;

    private sealed class FakeFileOperations : ITerrainCatalogFileOperations
    {
        private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);
        private readonly HashSet<string> _directories = new(StringComparer.Ordinal);

        public List<string> Events { get; } = new();
        public List<string> DeletedPaths { get; } = new();
        public int MoveCalls { get; private set; }
        public Exception? CreateFailure;
        public Exception? WriteFailure;
        public Exception? FlushFailure;
        public Exception? DisposeFailure;
        public Exception? MoveFailure;
        public Exception? DeleteFailure;

        public void AddDirectory(string path)
        {
            _directories.Add(path);
        }

        public void WriteFile(string path, byte[] bytes)
        {
            _files[path] = bytes;
        }

        public byte[]? ReadFileDirect(string path)
            => _files.TryGetValue(path, out var bytes) ? bytes : null;

        public void CommitFile(string path, byte[] bytes)
        {
            _files[path] = bytes;
        }

        public bool DirectoryExists(string path)
            => _directories.Contains(path);

        public bool FileExists(string path)
            => _files.ContainsKey(path);

        public byte[] ReadFile(string path)
        {
            Events.Add($"read:{path}");
            return _files[path];
        }

        public Stream CreateNew(string path)
        {
            Events.Add($"create:{path}");
            if (CreateFailure is { } failure)
            {
                throw failure;
            }
            _files[path] = Array.Empty<byte>();
            return new RecordingStream(this, path);
        }

        public void FlushToDisk(Stream stream)
        {
            Events.Add("flush");
            if (FlushFailure is { } failure)
            {
                throw failure;
            }
        }

        public void AtomicOverwrite(string source, string destination)
        {
            Events.Add("move");
            MoveCalls++;
            if (MoveFailure is { } failure)
            {
                throw failure;
            }
            _files[destination] = _files[source];
            _files.Remove(source);
        }

        public void Delete(string path)
        {
            DeletedPaths.Add(path);
            if (DeleteFailure is { } failure)
            {
                throw failure;
            }
            _files.Remove(path);
        }
    }

    private sealed class RecordingStream : Stream
    {
        private readonly FakeFileOperations _operations;
        private readonly string _path;
        private readonly MemoryStream _buffer = new();

        internal RecordingStream(FakeFileOperations operations, string path)
        {
            _operations = operations;
            _path = path;
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin)
            => throw new NotSupportedException();

        public override void SetLength(long value)
            => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (_operations.WriteFailure is { } failure)
            {
                throw failure;
            }
            _operations.Events.Add("write");
            _buffer.Write(buffer, offset, count);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_operations.DisposeFailure is { } failure)
                {
                    throw failure;
                }
                _operations.CommitFile(_path, _buffer.ToArray());
            }
            base.Dispose(disposing);
        }
    }
}
