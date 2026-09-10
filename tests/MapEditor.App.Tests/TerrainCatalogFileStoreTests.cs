using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MapEditor.App.Terrain;
using Xunit;

namespace MapEditor.App.Tests;

public sealed class TerrainCatalogFileStoreTests
{
    [Fact]
    public void Store_NullBlankArgumentsUseExactParameterNames()
    {
        Assert.Equal("operations", Assert.Throws<ArgumentNullException>(() => new TerrainCatalogFileStore(null!)).ParamName);
        TerrainCatalogFileStore store = new(new FakeOperations());
        Assert.Equal("assetDirectory", Assert.Throws<ArgumentException>(() => store.Write(" ", "x")).ParamName);
        Assert.Equal("serializedCatalog", Assert.Throws<ArgumentNullException>(() => store.Write(".", null!)).ParamName);
    }

    [Fact]
    public void Write_UsesUniqueSiblingCreateNewUtf8NoBomAndFlushTrueBeforeDisposeAndReplacement()
    {
        var operations = new FakeOperations { DestinationExists = true };
        new TerrainCatalogFileStore(operations).Write(".", "å");

        Assert.Equal(new[] { "Exists", "Create", "Write", "Flush", "Dispose", "Replace" }, operations.Calls);
        Assert.Equal(Encoding.UTF8.GetBytes("å"), operations.Destination);
        Assert.False(operations.Destination.Take(Encoding.UTF8.GetPreamble().Length).SequenceEqual(Encoding.UTF8.GetPreamble()));
        Assert.StartsWith(Path.GetFullPath("."), operations.CreatedPath, StringComparison.Ordinal);
        Assert.Contains("terrain-brushes.json.tmp-", operations.CreatedPath);
    }

    [Theory]
    [InlineData("Create")]
    [InlineData("Write")]
    [InlineData("Flush")]
    [InlineData("Dispose")]
    [InlineData("Replace")]
    [InlineData("Move")]
    public void Write_CreateWriteFlushDisposeReplaceOrMoveFailurePreservesPriorBytesAndDeletesOwnTemp(string phase)
    {
        var primary = new IOException(phase);
        var operations = new FakeOperations { DestinationExists = phase != "Move", FailurePhase = phase, Failure = primary };
        byte[] before = operations.Destination.ToArray();

        Exception thrown = Assert.ThrowsAny<Exception>(() => new TerrainCatalogFileStore(operations).Write(".", "new"));

        Assert.Same(primary, thrown);
        Assert.Equal(before, operations.Destination);
        Assert.Equal(new[] { operations.CreatedPath }, operations.DeletedPaths);
    }

    [Fact]
    public void Write_PreexistingStaleTempsRemainUntouchedOnSuccessAndFailure()
    {
        string directory = Path.GetFullPath(".");
        string staleA = Path.Combine(directory, "terrain-brushes.json.tmp-stale-a");
        string staleB = Path.Combine(directory, "terrain-brushes.json.tmp-stale-b");
        var operations = new FakeOperations { DestinationExists = true };
        operations.Files[staleA] = new byte[] { 1, 2 };
        operations.Files[staleB] = new byte[] { 3, 4 };
        var before = operations.Files.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());

        new TerrainCatalogFileStore(operations).Write(directory, "ok");
        operations.FailurePhase = "Flush";
        operations.Failure = new IOException("failure");
        Assert.Throws<IOException>(() => new TerrainCatalogFileStore(operations).Write(directory, "bad"));

        Assert.Equal(before[staleA], operations.Files[staleA]);
        Assert.Equal(before[staleB], operations.Files[staleB]);
        Assert.DoesNotContain(staleA, operations.DeletedPaths);
        Assert.DoesNotContain(staleB, operations.DeletedPaths);
        Assert.DoesNotContain(operations.Replacements, paths => paths.Source == staleA || paths.Source == staleB || paths.Destination == staleA || paths.Destination == staleB);
        Assert.DoesNotContain(operations.Moves, paths => paths.Source == staleA || paths.Source == staleB || paths.Destination == staleA || paths.Destination == staleB);
    }

    [Fact]
    public void Write_CleanupFailureRethrowsPrimaryInstanceAndRecordsCleanupException()
    {
        var primary = new IOException("write");
        var cleanup = new UnauthorizedAccessException("cleanup");
        var operations = new FakeOperations { FailurePhase = "Write", Failure = primary, CleanupFailure = cleanup };

        Exception thrown = Assert.Throws<IOException>(() => new TerrainCatalogFileStore(operations).Write(".", "bad"));

        Assert.Same(primary, thrown);
        Assert.Same(cleanup, thrown.Data["TerrainCatalogFileStore.CleanupException"]);
    }

    private sealed class FakeOperations : ITerrainCatalogFileOperations
    {
        internal readonly List<string> Calls = new();
        internal readonly Dictionary<string, byte[]> Files = new();
        internal readonly List<string> DeletedPaths = new();
        internal readonly List<(string Source, string Destination)> Replacements = new();
        internal readonly List<(string Source, string Destination)> Moves = new();
        internal bool DestinationExists;
        internal string? FailurePhase;
        internal Exception Failure = new IOException();
        internal Exception? CleanupFailure;
        internal byte[] Destination = Encoding.UTF8.GetBytes("old");
        internal string CreatedPath = "";
        private RecordingStream? _stream;

        public bool Exists(string path) { Calls.Add("Exists"); return DestinationExists; }
        public Stream CreateFile(string path)
        {
            Calls.Add("Create");
            CreatedPath = path;
            _stream = new RecordingStream(Calls, FailurePhase, Failure);
            if (FailurePhase == "Create") throw Failure;
            return _stream;
        }
        public void FlushToDisk(Stream stream) { Calls.Add("Flush"); if (FailurePhase == "Flush") throw Failure; }
        public void Replace(string sourcePath, string destinationPath) { Calls.Add("Replace"); Replacements.Add((sourcePath, destinationPath)); if (FailurePhase == "Replace") throw Failure; Destination = _stream!.ToArray(); Files.Remove(sourcePath); }
        public void Move(string sourcePath, string destinationPath) { Calls.Add("Move"); Moves.Add((sourcePath, destinationPath)); if (FailurePhase == "Move") throw Failure; Destination = _stream!.ToArray(); Files.Remove(sourcePath); }
        public void Delete(string path) { Calls.Add("Delete"); DeletedPaths.Add(path); if (CleanupFailure is not null) throw CleanupFailure; Files.Remove(path); }

        private sealed class RecordingStream : MemoryStream
        {
            private readonly List<string> _calls;
            private readonly string? _phase;
            private readonly Exception _failure;
            internal RecordingStream(List<string> calls, string? phase, Exception failure) { _calls = calls; _phase = phase; _failure = failure; }
            public override void Write(byte[] buffer, int offset, int count) { _calls.Add("Write"); if (_phase == "Write") throw _failure; base.Write(buffer, offset, count); }
            protected override void Dispose(bool disposing) { _calls.Add("Dispose"); if (_phase == "Dispose") throw _failure; base.Dispose(disposing); }
        }
    }
}
