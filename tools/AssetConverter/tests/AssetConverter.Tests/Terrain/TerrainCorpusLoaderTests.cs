using Goose2.AssetConverter.Terrain;
using MapEditor.Core;
using MapEditor.Core.Terrain;

namespace AssetConverter.Tests.Terrain;

public class TerrainCorpusLoaderTests
{
    [Fact]
    public void Load_ReadsOnlyInventoryListedMapsAndIgnoresStaleMap()
    {
        using var fixture = TerrainFixtureBuilder.Create(TerrainFixtureBuilder.StandardDefinition());
        fixture.WriteMap("Map3.map", 2, 2, new TerrainPlacement(0, 0, 9, 999));
        fixture.WriteInventory("Map1.map", "Map2.map");

        var corpus = TerrainCorpusLoader.Load(fixture.RepoRoot);

        Assert.Equal(
            new[] { "Assets/Maps/Map1.map", "Assets/Maps/Map2.map" },
            corpus.Maps.Select(m => m.Identity));
        Assert.DoesNotContain(corpus.Diagnostics, d => d.Message.Contains("Map3", StringComparison.Ordinal));
        Assert.DoesNotContain(corpus.ObservedReferences, r => r.Sheet == 9);

        using var control = TerrainFixtureBuilder.Create(TerrainFixtureBuilder.StandardDefinition());
        control.WriteInventory("Map1.map", "Map2.map");
        Assert.Equal(TerrainCorpusFingerprint.Compute(control.RepoRoot), corpus.Fingerprint);
    }

    [Fact]
    public void Load_ReadsOnlyLayer0AndUsesLockedMapIdentity()
    {
        using var fixture = TerrainFixtureBuilder.Create(def =>
        {
            def.AddMap("Map1.map", 2, 2, new TerrainPlacement(0, 0, 1, 100), new TerrainPlacement(1, 1, 2, 500, Layer: 1));
            def.AddSheet(1, 32, 32, new TerrainSheetFrame(100, 0, 0, 32, 32));
            def.AddSheet(2, 32, 32, new TerrainSheetFrame(500, 0, 0, 32, 32));
        });
        fixture.WriteInventory("Map1.map");

        var corpus = TerrainCorpusLoader.Load(fixture.RepoRoot);

        var descriptor = Assert.Single(corpus.Maps);
        Assert.Equal("Assets/Maps/Map1.map", descriptor.Identity);
        Assert.Equal(2, descriptor.Width);
        Assert.Equal(2, descriptor.Height);
        Assert.Equal(1, descriptor.EligiblePlacements);
        Assert.Contains(new TerrainGraphicReference(1, 100), corpus.ObservedReferences);
        Assert.DoesNotContain(new TerrainGraphicReference(2, 500), corpus.ObservedReferences);
        Assert.Equal(new[] { 1 }, corpus.RelevantSheets);
        Assert.Empty(corpus.Diagnostics);
    }

    [Fact]
    public void Load_GraphicZeroWithNonzeroSheetIsIgnored()
    {
        using var fixture = TerrainFixtureBuilder.Create(def =>
        {
            def.AddMap("Map1.map", 2, 2, new TerrainPlacement(0, 0, 1, 100), new TerrainPlacement(1, 1, 7, 0));
            def.AddSheet(1, 32, 32, new TerrainSheetFrame(100, 0, 0, 32, 32));
            def.AddSheet(7, 32, 32, new TerrainSheetFrame(0, 0, 0, 32, 32));
        });
        fixture.WriteInventory("Map1.map");

        var corpus = TerrainCorpusLoader.Load(fixture.RepoRoot);

        var observed = Assert.Single(corpus.ObservedReferences);
        Assert.Equal(new TerrainGraphicReference(1, 100), observed);
        Assert.Equal(new[] { 1 }, corpus.RelevantSheets);
        Assert.Empty(corpus.Diagnostics);
    }

    [Fact]
    public void Load_MapTrailerIsAcceptedByMapCodec()
    {
        using var fixture = TerrainFixtureBuilder.Create(def =>
        {
            def.AddSheet(1, 32, 32, new TerrainSheetFrame(100, 0, 0, 32, 32));
        });

        var document = MapDocument.Create(2, 2);
        document.SetLayer(0, 0, 0, new MapTileLayer(1, 100));
        var encoded = MapCodec.Encode(document);
        var withTrailer = new byte[encoded.Length + 404];
        Array.Copy(encoded, withTrailer, encoded.Length);
        for (var i = encoded.Length; i < withTrailer.Length; i++)
        {
            withTrailer[i] = 0xAB;
        }

        fixture.WriteMapBytes("Map1.map", withTrailer);
        fixture.WriteInventory("Map1.map");

        var corpus = TerrainCorpusLoader.Load(fixture.RepoRoot);

        var descriptor = Assert.Single(corpus.Maps);
        Assert.Equal(1, descriptor.EligiblePlacements);
    }

    [Fact]
    public void Load_MissingManifestReferenceAndUndersizedOrOversizedFrameAreDiagnosedAndExcluded()
    {
        using var fixture = TerrainFixtureBuilder.Create(def =>
        {
            def.AddMap("Map1.map", 4, 4,
                new TerrainPlacement(0, 0, 1, 100),
                new TerrainPlacement(1, 0, 1, 200),
                new TerrainPlacement(2, 0, 2, 300),
                new TerrainPlacement(3, 0, 2, 400));
            def.AddSheet(1, 32, 32, new TerrainSheetFrame(100, 0, 0, 32, 32));
            def.AddSheet(2, 96, 32, new TerrainSheetFrame(300, 0, 0, 16, 32), new TerrainSheetFrame(400, 0, 0, 64, 32));
        });
        fixture.WriteInventory("Map1.map");

        var corpus = TerrainCorpusLoader.Load(fixture.RepoRoot);

        var descriptor = Assert.Single(corpus.Maps);
        Assert.Equal(1, descriptor.EligiblePlacements);
        Assert.Equal(new[] { 1 }, corpus.RelevantSheets);

        var missing = Assert.Single(
            corpus.Diagnostics,
            d => d.Code == TerrainCorpusLoader.DiagnosticCodeManifestReferenceMissing);
        Assert.Equal(new TerrainGraphicReference(1, 200), missing.Reference);

        Assert.Single(
            corpus.Diagnostics,
            d => d.Code == TerrainCorpusLoader.DiagnosticCodeFrameSizeMismatch
                 && d.Reference == new TerrainGraphicReference(2, 300));
        Assert.Single(
            corpus.Diagnostics,
            d => d.Code == TerrainCorpusLoader.DiagnosticCodeFrameSizeMismatch
                 && d.Reference == new TerrainGraphicReference(2, 400));

        Assert.Contains(new TerrainGraphicReference(1, 200), corpus.ObservedReferences);
        Assert.Contains(new TerrainGraphicReference(2, 300), corpus.ObservedReferences);
        Assert.Contains(new TerrainGraphicReference(2, 400), corpus.ObservedReferences);
    }

    [Theory]
    [InlineData("{\"tileSize\":32,\"tileSize\":32,\"sheets\":{}}")]
    [InlineData("{\"tileSize\":32,\"sheets\":{},\"sheets\":{}}")]
    [InlineData("{\"tileSize\":32}")]
    [InlineData("{\"sheets\":{}}")]
    [InlineData("{\"tileSize\":32,\"sheets\":{},\"extra\":1}")]
    [InlineData("{\"tileSize\":\"32\",\"sheets\":{}}")]
    [InlineData("{\"tileSize\":32,\"sheets\":{\"1\":{\"100\":[0,0,32,32]},\"1\":{\"200\":[0,0,32,32]}}}")]
    [InlineData("{\"tileSize\":32,\"sheets\":{\"1\":{\"100\":[0,0,32,32]},\"01\":{\"200\":[0,0,32,32]}}}")]
    [InlineData("{\"tileSize\":32,\"sheets\":{\"1\":{\"100\":[0,0,32,32],\"01\":[0,0,32,32]}}}")]
    [InlineData("{\"tileSize\":32,\"sheets\":{\"1\":{\"+100\":[0,0,32,32]}}}")]
    [InlineData("{\"tileSize\":32,\"sheets\":{\"1\":{\"100\":[0,0,32]}}}")]
    [InlineData("{\"tileSize\":32,\"sheets\":{\"1\":{\"100\":[0,0,32,32,0]}}}")]
    [InlineData("{\"tileSize\":32,\"sheets\":{\"1\":{\"100\":[-1,0,32,32]}}}")]
    [InlineData("{\"tileSize\":32,\"sheets\":{\"1\":{\"100\":[0,0,0,32]}}}")]
    [InlineData("not json")]
    public void Load_MalformedOrDuplicateAliasManifestThrowsManifestMalformed(string json)
    {
        using var fixture = TerrainFixtureBuilder.Create(def =>
        {
            def.AddMap("Map1.map", 2, 2, new TerrainPlacement(0, 0, 1, 100));
            def.AddSheet(1, 32, 32, new TerrainSheetFrame(100, 0, 0, 32, 32));
        });
        fixture.WriteInventory("Map1.map");
        fixture.WriteManifestRaw(json);

        var exception = Assert.Throws<TerrainGenerationException>(() => TerrainCorpusLoader.Load(fixture.RepoRoot));
        Assert.Equal(TerrainGenerationError.ManifestMalformed, exception.Error);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(64)]
    public void Load_UnsupportedTileSizeThrowsUnsupportedTileSize(int tileSize)
    {
        using var fixture = TerrainFixtureBuilder.Create(def =>
        {
            def.AddMap("Map1.map", 2, 2, new TerrainPlacement(0, 0, 1, 100));
            def.AddSheet(1, 32, 32, new TerrainSheetFrame(100, 0, 0, 32, 32));
        });
        fixture.WriteInventory("Map1.map");
        fixture.WriteManifestRaw($"{{\"tileSize\":{tileSize},\"sheets\":{{}}}}");

        var exception = Assert.Throws<TerrainGenerationException>(() => TerrainCorpusLoader.Load(fixture.RepoRoot));
        Assert.Equal(TerrainGenerationError.UnsupportedTileSize, exception.Error);
    }

    [Fact]
    public void Load_MalformedMissingOrUnreadableMapThrowsExactCategory()
    {
        using var fixture = TerrainFixtureBuilder.Create(def =>
        {
            def.AddMap("Map1.map", 2, 2, new TerrainPlacement(0, 0, 1, 100));
            def.AddSheet(1, 32, 32, new TerrainSheetFrame(100, 0, 0, 32, 32));
        });

        fixture.WriteInventoryRaw("terrain-map-inputs-v1\nMissing.map\n");
        var missing = Assert.Throws<TerrainGenerationException>(() => TerrainCorpusLoader.Load(fixture.RepoRoot));
        Assert.Equal(TerrainGenerationError.MapNotFound, missing.Error);

        fixture.WriteInventory("Map1.map");
        var mapPath = Path.Combine(fixture.MapsDirectory, "Map1.map");
        File.SetUnixFileMode(mapPath, (UnixFileMode)0);
        var unreadable = Assert.Throws<TerrainGenerationException>(() => TerrainCorpusLoader.Load(fixture.RepoRoot));
        Assert.Equal(TerrainGenerationError.MapReadFailed, unreadable.Error);
        File.SetUnixFileMode(mapPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        fixture.WriteMapBytes("Map1.map", new byte[] { 1, 2, 3, 4 });
        var truncated = Assert.Throws<TerrainGenerationException>(() => TerrainCorpusLoader.Load(fixture.RepoRoot));
        Assert.Equal(TerrainGenerationError.MapDecodeFailed, truncated.Error);

        var garbage = new byte[MapCodec.HeaderSize + MapCodec.BytesPerTile * 4];
        for (var i = 0; i < garbage.Length; i++)
        {
            garbage[i] = 0xFF;
        }

        fixture.WriteMapBytes("Map1.map", garbage);
        var malformed = Assert.Throws<TerrainGenerationException>(() => TerrainCorpusLoader.Load(fixture.RepoRoot));
        Assert.Equal(TerrainGenerationError.MapDecodeFailed, malformed.Error);
    }

    [Fact]
    public void Load_MissingRelevantPngThrowsSheetNotFound()
    {
        using var fixture = TerrainFixtureBuilder.Create(def =>
        {
            def.AddMap("Map1.map", 2, 2, new TerrainPlacement(0, 0, 1, 100));
            def.SetManifestJson("{\"tileSize\":32,\"sheets\":{\"1\":{\"100\":[0,0,32,32]}}}");
        });
        fixture.WriteInventory("Map1.map");

        var exception = Assert.Throws<TerrainGenerationException>(() => TerrainCorpusLoader.Load(fixture.RepoRoot));
        Assert.Equal(TerrainGenerationError.SheetNotFound, exception.Error);
    }

    [Fact]
    public void Load_FrameRectExceedingSheetImageThrowsFrameOutOfBounds()
    {
        using var fixture = TerrainFixtureBuilder.Create(def =>
        {
            def.AddMap("Map1.map", 2, 2, new TerrainPlacement(0, 0, 1, 100));
            def.AddSheet(1, 32, 32, new TerrainSheetFrame(100, 0, 0, 32, 32));
            def.SetManifestJson("{\"tileSize\":32,\"sheets\":{\"1\":{\"100\":[32,0,32,32]}}}");
        });
        fixture.WriteInventory("Map1.map");

        var exception = Assert.Throws<TerrainGenerationException>(() => TerrainCorpusLoader.Load(fixture.RepoRoot));
        Assert.Equal(TerrainGenerationError.FrameOutOfBounds, exception.Error);
        Assert.Equal(new TerrainGraphicReference(1, 100), exception.Reference);
    }

    [Fact]
    public void Load_UndecodableSheetPngThrowsSheetDecodeFailed()
    {
        using var fixture = TerrainFixtureBuilder.Create(def =>
        {
            def.AddMap("Map1.map", 2, 2, new TerrainPlacement(0, 0, 1, 100));
            def.AddSheet(1, 32, 32, new TerrainSheetFrame(100, 0, 0, 32, 32));
        });
        fixture.WriteInventory("Map1.map");
        File.WriteAllBytes(Path.Combine(fixture.SheetsDirectory, "1.png"), new byte[] { 1, 2, 3, 4 });

        var exception = Assert.Throws<TerrainGenerationException>(() => TerrainCorpusLoader.Load(fixture.RepoRoot));
        Assert.Equal(TerrainGenerationError.SheetDecodeFailed, exception.Error);
    }

    [Fact]
    public void Load_FrameRectCheckedOverflowThrowsNumericOverflow()
    {
        using var fixture = TerrainFixtureBuilder.Create(def =>
        {
            def.AddMap("Map1.map", 2, 2, new TerrainPlacement(0, 0, 1, 100));
            def.SetManifestJson("{\"tileSize\":32,\"sheets\":{\"1\":{\"100\":[2147483647,0,2,2]}}}");
        });
        fixture.WriteInventory("Map1.map");

        var exception = Assert.Throws<TerrainGenerationException>(() => TerrainCorpusLoader.Load(fixture.RepoRoot));
        Assert.Equal(TerrainGenerationError.NumericOverflow, exception.Error);
    }

    [Fact]
    public void Load_NoMapsOrNoEligiblePlacementsThrows()
    {
        using var fixture = TerrainFixtureBuilder.Create(def =>
        {
            def.AddSheet(1, 32, 32, new TerrainSheetFrame(100, 0, 0, 32, 32));
        });

        var empty = new TerrainMapInventory(Array.Empty<string>(), Array.Empty<byte>());
        var noMaps = Assert.Throws<TerrainGenerationException>(
            () => TerrainCorpusLoader.Load(empty, fixture.RepoRoot));
        Assert.Equal(TerrainGenerationError.NoMaps, noMaps.Error);

        using var fixture2 = TerrainFixtureBuilder.Create(def =>
        {
            def.AddMap("Map1.map", 2, 2, new TerrainPlacement(0, 0, 1, 100));
            def.SetManifestJson("{\"tileSize\":32,\"sheets\":{}}");
        });
        fixture2.WriteInventory("Map1.map");

        var none = Assert.Throws<TerrainGenerationException>(() => TerrainCorpusLoader.Load(fixture2.RepoRoot));
        Assert.Equal(TerrainGenerationError.NoEligiblePlacements, none.Error);
    }

    [Fact]
    public void Load_ProcessesAtMostOneMapByteArrayAndDocumentAtATime()
    {
        using var fixture = TerrainFixtureBuilder.Create(TerrainFixtureBuilder.StandardDefinition());
        fixture.WriteInventory("Map1.map", "Map2.map");
        var reader = new RecordingMapDataReader(Path.Combine(fixture.RepoRoot, "Assets", "Maps"));
        var inventory = TerrainMapInventory.Read(fixture.RepoRoot);

        var corpus = TerrainCorpusLoader.Load(inventory, fixture.RepoRoot, reader);

        Assert.Equal(2, reader.OpenCount);
        Assert.Equal(2, reader.ReleaseCount);
        Assert.Equal(1, reader.MaxConcurrent);
        Assert.Equal(2, corpus.Maps.Count);
    }

    private sealed class RecordingMapDataReader : ITerrainMapDataReader
    {
        private readonly string _mapsDirectory;
        private int _active;

        public RecordingMapDataReader(string mapsDirectory)
        {
            _mapsDirectory = mapsDirectory;
        }

        public int OpenCount { get; private set; }
        public int ReleaseCount { get; private set; }
        public int MaxConcurrent { get; private set; }

        public ITerrainMapData Open(string mapIdentity)
        {
            OpenCount++;
            _active++;
            MaxConcurrent = Math.Max(MaxConcurrent, _active);
            var path = Path.Combine(_mapsDirectory, Path.GetFileName(mapIdentity));
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Map file not found.", path);
            }

            return new RecordingMapData(File.ReadAllBytes(path), () =>
            {
                ReleaseCount++;
                _active--;
            });
        }

        private sealed class RecordingMapData : ITerrainMapData
        {
            private readonly Action _release;

            public byte[] Bytes { get; }

            public RecordingMapData(byte[] bytes, Action release)
            {
                Bytes = bytes;
                _release = release;
            }

            public void Release() => _release();
        }
    }
}
