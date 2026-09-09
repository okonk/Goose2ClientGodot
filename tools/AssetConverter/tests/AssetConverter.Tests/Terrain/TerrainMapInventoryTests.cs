using System.Text;
using Goose2.AssetConverter.Terrain;

namespace AssetConverter.Tests.Terrain;

public class TerrainMapInventoryTests
{
    [Fact]
    public void Inventory_WriteProducesCanonicalSiblingReplacementWithoutStaleEntries()
    {
        var (root, maps) = NewRepo();
        try
        {
            File.WriteAllBytes(Path.Combine(maps, "Map1.map"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(maps, "Map2.map"), new byte[] { 2 });
            File.WriteAllBytes(Path.Combine(maps, "Map3.map"), new byte[] { 3 });

            TerrainMapInventory.Write(maps, new[] { "Map2.map", "Map1.map" });

            var path = Path.Combine(maps, TerrainMapInventory.FileName);
            Assert.Equal(
                Encoding.UTF8.GetBytes("terrain-map-inputs-v1\nMap1.map\nMap2.map\n"),
                File.ReadAllBytes(path));
            Assert.Empty(Directory.EnumerateFiles(maps, "*.tmp-*"));

            var inventory = TerrainMapInventory.Read(root);
            Assert.Equal(new[] { "Map1.map", "Map2.map" }, inventory.FileNames);
            Assert.Equal(new[] { "Assets/Maps/Map1.map", "Assets/Maps/Map2.map" }, inventory.MapIdentities);

            TerrainMapInventory.Write(maps, new[] { "Map1.map" });
            Assert.Equal("terrain-map-inputs-v1\nMap1.map\n", File.ReadAllText(path));
            Assert.Empty(Directory.EnumerateFiles(maps, "*.tmp-*"));

            var prior = File.ReadAllBytes(path);
            var failingWrite = new RecordingInventoryFileOperations { FailOnWrite = true };
            Assert.Throws<IOException>(() => TerrainMapInventory.Write(maps, new[] { "Map1.map" }, failingWrite));
            Assert.Equal(prior, File.ReadAllBytes(path));
            Assert.Empty(Directory.EnumerateFiles(maps, "*.tmp-*"));

            var failingReplace = new RecordingInventoryFileOperations { FailOnReplace = true };
            Assert.Throws<IOException>(() => TerrainMapInventory.Write(maps, new[] { "Map1.map" }, failingReplace));
            Assert.Equal(prior, File.ReadAllBytes(path));
            Assert.Empty(Directory.EnumerateFiles(maps, "*.tmp-*"));

            File.Delete(path);
            var failingMove = new RecordingInventoryFileOperations { FailOnMove = true };
            Assert.Throws<IOException>(() => TerrainMapInventory.Write(maps, new[] { "Map1.map" }, failingMove));
            Assert.False(File.Exists(path));
            Assert.Empty(Directory.EnumerateFiles(maps, "*.tmp-*"));

            var failingCleanup = new RecordingInventoryFileOperations { FailOnWrite = true, FailOnDelete = true };
            var primary = Assert.Throws<IOException>(
                () => TerrainMapInventory.Write(maps, new[] { "Map1.map" }, failingCleanup));
            Assert.Equal("injected write failure", primary.Message);
            Assert.Single(Directory.EnumerateFiles(maps, "*.tmp-*"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Inventory_MalformedDuplicateUnsortedTraversalOrMissingEntryThrowsExactCategory()
    {
        var (root, maps) = NewRepo();
        try
        {
            File.WriteAllBytes(Path.Combine(maps, "Map1.map"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(maps, "Map2.map"), new byte[] { 2 });
            var inventoryPath = Path.Combine(maps, TerrainMapInventory.FileName);

            var duplicate = Assert.Throws<TerrainGenerationException>(
                () => TerrainMapInventory.Write(maps, new[] { "Map1.map", "Map2.map", "Map1.map" }));
            Assert.Equal(TerrainGenerationError.InvalidMapInventory, duplicate.Error);

            var empty = Assert.Throws<TerrainGenerationException>(
                () => TerrainMapInventory.Write(maps, Array.Empty<string>()));
            Assert.Equal(TerrainGenerationError.InvalidMapInventory, empty.Error);

            var missingEntry = Assert.Throws<TerrainGenerationException>(
                () => TerrainMapInventory.Write(maps, new[] { "Missing.map" }));
            Assert.Equal(TerrainGenerationError.MapNotFound, missingEntry.Error);

            var notFound = Assert.Throws<TerrainGenerationException>(() => TerrainMapInventory.Read(root));
            Assert.Equal(TerrainGenerationError.MapInventoryNotFound, notFound.Error);

            TerrainMapInventory.Write(maps, new[] { "Map2.map", "Map1.map" });
            Assert.Equal("terrain-map-inputs-v1\nMap1.map\nMap2.map\n", File.ReadAllText(inventoryPath));

            void ExpectRead(TerrainGenerationError error, string content)
            {
                File.WriteAllText(inventoryPath, content);
                var exception = Assert.Throws<TerrainGenerationException>(() => TerrainMapInventory.Read(root));
                Assert.Equal(error, exception.Error);
            }

            ExpectRead(TerrainGenerationError.InvalidMapInventory, "terrain-map-inputs-v1\r\nMap1.map\r\n");
            ExpectRead(TerrainGenerationError.InvalidMapInventory, "\uFEFFterrain-map-inputs-v1\nMap1.map\n");
            ExpectRead(TerrainGenerationError.InvalidMapInventory, "Map1.map\n");
            ExpectRead(TerrainGenerationError.InvalidMapInventory, "terrain-map-inputs-v1\n");
            ExpectRead(TerrainGenerationError.InvalidMapInventory, "terrain-map-inputs-v1\nMap2.map\nMap1.map\n");
            ExpectRead(TerrainGenerationError.InvalidMapInventory, "terrain-map-inputs-v1\nMap1.map\nMap1.map\n");
            ExpectRead(TerrainGenerationError.InvalidMapInventory, "terrain-map-inputs-v1\nBad.txt\n");
            ExpectRead(TerrainGenerationError.MapNotFound, "terrain-map-inputs-v1\nMissing.map\n");

            File.WriteAllText(inventoryPath, "terrain-map-inputs-v1\nMap1.map\n");
            var readBack = TerrainMapInventory.Read(root);
            Assert.Equal(new[] { "Map1.map" }, readBack.FileNames);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Inventory_InvalidUtf8BytesThrowInvalidMapInventory()
    {
        var (root, maps) = NewRepo();
        try
        {
            File.WriteAllBytes(Path.Combine(maps, "Map1.map"), new byte[] { 1 });
            var valid = Encoding.UTF8.GetBytes("terrain-map-inputs-v1\nMap1.map\n");
            var corrupt = new byte[valid.Length + 1];
            Array.Copy(valid, corrupt, valid.Length);
            corrupt[^1] = 0xFF;
            File.WriteAllBytes(Path.Combine(maps, TerrainMapInventory.FileName), corrupt);

            var exception = Assert.Throws<TerrainGenerationException>(() => TerrainMapInventory.Read(root));
            Assert.Equal(TerrainGenerationError.InvalidMapInventory, exception.Error);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("Map1.txt")]
    [InlineData("sub/Map1.map")]
    [InlineData("sub\\Map1.map")]
    [InlineData("/Map1.map")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("")]
    public void Inventory_WriteRejectsMalformedNames(string name)
    {
        var (root, maps) = NewRepo();
        try
        {
            File.WriteAllBytes(Path.Combine(maps, "Map1.map"), new byte[] { 1 });
            var exception = Assert.Throws<TerrainGenerationException>(
                () => TerrainMapInventory.Write(maps, new[] { name }));
            Assert.Equal(TerrainGenerationError.InvalidMapInventory, exception.Error);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static (string Root, string Maps) NewRepo()
    {
        var root = Path.Combine(Path.GetTempPath(), "ac_terrain_inv_" + Guid.NewGuid().ToString("N"));
        var maps = Path.Combine(root, "Assets", "Maps");
        Directory.CreateDirectory(maps);
        return (root, maps);
    }

    private sealed class RecordingInventoryFileOperations : ITerrainMapInventoryFileOperations
    {
        public bool FailOnWrite { get; init; }
        public bool FailOnReplace { get; init; }
        public bool FailOnMove { get; init; }
        public bool FailOnDelete { get; init; }

        public bool Exists(string path) => File.Exists(path);

        public Stream CreateFile(string path) => new FailingStream(File.Create(path), FailOnWrite);

        public void Replace(string source, string destination)
        {
            if (FailOnReplace)
            {
                throw new IOException("injected replace failure");
            }

            File.Move(source, destination, overwrite: true);
        }

        public void Move(string source, string destination)
        {
            if (FailOnMove)
            {
                throw new IOException("injected move failure");
            }

            File.Move(source, destination);
        }

        public void Delete(string path)
        {
            if (FailOnDelete)
            {
                throw new IOException("injected delete failure");
            }

            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private sealed class FailingStream(Stream inner, bool fail) : Stream
    {
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
            if (fail)
            {
                throw new IOException("injected flush failure");
            }

            inner.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (fail)
            {
                throw new IOException("injected write failure");
            }

            inner.Write(buffer, offset, count);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
