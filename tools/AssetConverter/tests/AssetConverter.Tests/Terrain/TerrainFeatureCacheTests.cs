using Goose2.AssetConverter.Terrain;
using MapEditor.Core.Terrain;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AssetConverter.Tests.Terrain;

public class TerrainFeatureCacheTests
{
    [Fact]
    public void Build_DecodesEachRelevantSheetOnceExtractsEachEligibleFrameOnceAndRetainsNoImages()
    {
        using var fixture = TerrainFixtureBuilder.Create(TerrainFixtureBuilder.StandardDefinition());
        fixture.WriteInventory("Map1.map", "Map2.map");
        var corpus = TerrainCorpusLoader.Load(fixture.RepoRoot);
        var loader = new RecordingSheetImageLoader(path => Image.Load<Rgba32>(path));

        var cache = TerrainFeatureCache.Build(corpus, loader);

        Assert.Equal(
            new[]
            {
                Path.Combine(corpus.Root, "Assets/Sprites/sheets", "1.png"),
                Path.Combine(corpus.Root, "Assets/Sprites/sheets", "2.png"),
            },
            loader.Paths);
        Assert.Equal(2, loader.Images.Count);
        Assert.Equal(
            new[]
            {
                new TerrainGraphicReference(1, 100),
                new TerrainGraphicReference(1, 101),
                new TerrainGraphicReference(2, 200),
            },
            cache.References);
        Assert.All(cache.References, reference => Assert.True(cache.TryGetFeatures(reference, out _)));
        Assert.False(cache.TryGetFeatures(new TerrainGraphicReference(1, 999), out _));
        Assert.Equal(loader.Images.Count, CountDisposed(loader.Images));
    }

    [Fact]
    public void Build_SuccessOrExtractionFailureDisposesEachLoadedImageOnce()
    {
        using var fixture = TerrainFixtureBuilder.Create(TerrainFixtureBuilder.StandardDefinition());
        fixture.WriteInventory("Map1.map", "Map2.map");
        var corpus = TerrainCorpusLoader.Load(fixture.RepoRoot);
        var successLoader = new RecordingSheetImageLoader(path => Image.Load<Rgba32>(path));
        var cache = TerrainFeatureCache.Build(corpus, successLoader);
        Assert.NotEmpty(cache.References);
        Assert.Equal(successLoader.Images.Count, CountDisposed(successLoader.Images));

        var frames = new[]
        {
            new TerrainFrame(new TerrainGraphicReference(1, 100), new TerrainFrameRect(0, 0, 32, 32)),
            new TerrainFrame(new TerrainGraphicReference(2, 200), new TerrainFrameRect(40, 0, 32, 32)),
        };
        var badCorpus = CreateCorpus(fixture.RepoRoot, frames, new[] { 1, 2 });
        var failureLoader = new RecordingSheetImageLoader(_ => new Image<Rgba32>(64, 32));
        var exception = Assert.Throws<TerrainGenerationException>(() => TerrainFeatureCache.Build(badCorpus, failureLoader));
        Assert.Equal(TerrainGenerationError.FrameOutOfBounds, exception.Error);
        Assert.Equal(new TerrainGraphicReference(2, 200), exception.Reference);
        Assert.NotNull(exception.InputPath);
        Assert.Equal(failureLoader.Images.Count, CountDisposed(failureLoader.Images));
    }

    [Fact]
    public void Build_MissingOrCorruptPngThrowsExactCategory()
    {
        using (var missing = TerrainFixtureBuilder.Create(TerrainFixtureBuilder.StandardDefinition()))
        {
            missing.WriteInventory("Map1.map", "Map2.map");
            var corpus = TerrainCorpusLoader.Load(missing.RepoRoot);
            var sheetPath = Path.Combine(corpus.Root, "Assets/Sprites/sheets", "1.png");
            File.Delete(sheetPath);
            var exception = Assert.Throws<TerrainGenerationException>(() => TerrainFeatureCache.Build(corpus));
            Assert.Equal(TerrainGenerationError.SheetNotFound, exception.Error);
            Assert.Equal(sheetPath, exception.InputPath);
        }

        using (var corrupt = TerrainFixtureBuilder.Create(TerrainFixtureBuilder.StandardDefinition()))
        {
            corrupt.WriteInventory("Map1.map", "Map2.map");
            var corpus = TerrainCorpusLoader.Load(corrupt.RepoRoot);
            var sheetPath = Path.Combine(corpus.Root, "Assets/Sprites/sheets", "1.png");
            File.WriteAllBytes(sheetPath, new byte[] { 1, 2, 3, 4 });
            var exception = Assert.Throws<TerrainGenerationException>(() => TerrainFeatureCache.Build(corpus));
            Assert.Equal(TerrainGenerationError.SheetDecodeFailed, exception.Error);
            Assert.Equal(sheetPath, exception.InputPath);
            Assert.IsAssignableFrom<ImageFormatException>(exception.InnerException);
        }
    }

    [Fact]
    public void Build_OutOfBoundsOrOverflowingManifestRectThrowsReferenceCategory()
    {
        using var fixture = TerrainFixtureBuilder.Create(TerrainFixtureBuilder.StandardDefinition());
        var root = fixture.RepoRoot;

        var outOfBounds = CreateCorpus(
            root,
            new[] { new TerrainFrame(new TerrainGraphicReference(1, 100), new TerrainFrameRect(40, 0, 32, 32)) },
            new[] { 1 });
        var boundsLoader = new RecordingSheetImageLoader(_ => new Image<Rgba32>(64, 32));
        var boundsException = Assert.Throws<TerrainGenerationException>(() => TerrainFeatureCache.Build(outOfBounds, boundsLoader));
        Assert.Equal(TerrainGenerationError.FrameOutOfBounds, boundsException.Error);
        Assert.Equal(new TerrainGraphicReference(1, 100), boundsException.Reference);
        Assert.NotNull(boundsException.InputPath);

        var overflowing = CreateCorpus(
            root,
            new[] { new TerrainFrame(new TerrainGraphicReference(1, 100), new TerrainFrameRect(int.MaxValue, 0, 32, 32)) },
            new[] { 1 });
        var overflowLoader = new RecordingSheetImageLoader(_ => new Image<Rgba32>(32, 32));
        var overflowException = Assert.Throws<TerrainGenerationException>(() => TerrainFeatureCache.Build(overflowing, overflowLoader));
        Assert.Equal(TerrainGenerationError.FrameOutOfBounds, overflowException.Error);
        Assert.Equal(new TerrainGraphicReference(1, 100), overflowException.Reference);
        Assert.NotNull(overflowException.InputPath);
    }

    [Fact]
    public void QueryCandidates_UsesExactSameSheetDecilePaletteAndThreeBandRulesInNumericOrder()
    {
        var cache = BuildQueryCorpus();
        Assert.Equal(
            new[]
            {
                new TerrainGraphicReference(1, 11),
                new TerrainGraphicReference(1, 12),
                new TerrainGraphicReference(1, 14),
                new TerrainGraphicReference(1, 100),
            },
            cache.QueryCandidates(new TerrainGraphicReference(1, 10)));
        Assert.Equal(4, cache.ComparisonCount);

        Assert.Equal(
            new[]
            {
                new TerrainGraphicReference(1, 10),
                new TerrainGraphicReference(1, 12),
                new TerrainGraphicReference(1, 14),
                new TerrainGraphicReference(1, 100),
            },
            cache.QueryCandidates(new TerrainGraphicReference(1, 11)));
        Assert.Equal(8, cache.ComparisonCount);

        Assert.Empty(cache.QueryCandidates(new TerrainGraphicReference(1, 999)));
        Assert.Equal(8, cache.ComparisonCount);

        cache.Similarity(new TerrainGraphicReference(1, 10), new TerrainGraphicReference(1, 11));
        cache.Similarity(new TerrainGraphicReference(1, 10), new TerrainGraphicReference(1, 11));
        Assert.Equal(2, cache.SimilarityCount);
    }

    [Fact]
    public void QueryCandidates_DifferentSheetOrTwoMatchingBandsIsNeverCompared()
    {
        var cache = BuildQueryCorpus();
        var candidates = cache.QueryCandidates(new TerrainGraphicReference(1, 10));
        Assert.DoesNotContain(new TerrainGraphicReference(2, 10), candidates);
        Assert.DoesNotContain(new TerrainGraphicReference(1, 16), candidates);
        Assert.Equal(4, cache.ComparisonCount);
    }

    [Fact]
    public void Build_ImageSharpLoaderMatchesDirectExtractor()
    {
        using var fixture = TerrainFixtureBuilder.Create(TerrainFixtureBuilder.StandardDefinition());
        fixture.WriteInventory("Map1.map", "Map2.map");
        var corpus = TerrainCorpusLoader.Load(fixture.RepoRoot);
        var cache = TerrainFeatureCache.Build(corpus);

        foreach (var reference in cache.References)
        {
            Assert.True(corpus.FrameIndex.TryGetRect(reference, out var rect));
            var sheetPath = Path.Combine(corpus.Root, "Assets/Sprites/sheets", reference.Sheet + ".png");
            using var image = Image.Load<Rgba32>(sheetPath);
            var expected = TerrainFeatureExtractor.Extract(reference, image, rect.X, rect.Y);
            Assert.True(cache.TryGetFeatures(reference, out var actual));
            Assert.Equal(expected.AlphaCells, actual.AlphaCells);
            Assert.Equal(expected.Palette, actual.Palette);
            Assert.Equal(expected.PerceptualCells, actual.PerceptualCells);
            Assert.Equal(expected.Corners, actual.Corners);
            Assert.Equal(expected.Edges, actual.Edges);
            Assert.Equal(expected.PerceptualHash, actual.PerceptualHash);
            Assert.Equal(expected.Buckets, actual.Buckets);
        }
    }

    private static TerrainFeatureCache BuildQueryCorpus()
    {
        var graphics = new[] { 10, 11, 12, 13, 14, 15, 16, 100 };
        var frames = graphics
            .Select((graphic, index) => new TerrainFrame(
                new TerrainGraphicReference(1, graphic),
                new TerrainFrameRect(index * 32, 0, 32, 32)))
            .Append(new TerrainFrame(new TerrainGraphicReference(2, 10), new TerrainFrameRect(0, 0, 32, 32)))
            .ToArray();
        var corpus = CreateCorpus("/ac-terrain-query-root", frames, new[] { 1, 2 });
        var loader = new RecordingSheetImageLoader(
            path => path.EndsWith("1.png", StringComparison.Ordinal)
                ? CreateQuerySheet(graphics)
                : CreateQuerySheet(new[] { 10 }));
        return TerrainFeatureCache.Build(corpus, loader);
    }

    private static Image<Rgba32> CreateQuerySheet(int[] graphics)
    {
        var image = new Image<Rgba32>(32 * graphics.Length, 32);
        for (var i = 0; i < graphics.Length; i++)
        {
            for (var y = 0; y < 32; y++)
            {
                for (var x = 0; x < 32; x++)
                {
                    image[i * 32 + x, y] = QueryPixel(graphics[i], x, y);
                }
            }
        }

        return image;
    }

    private static Rgba32 QueryPixel(int graphic, int x, int y) => graphic switch
    {
        12 => new Rgba32(255, 255, 255, 255),
        13 => x < 16 ? new Rgba32(255, 0, 0, 255) : new Rgba32(0, 255, 0, 255),
        14 => new Rgba32(0, 0, 0, 204),
        15 => new Rgba32(0, 0, 0, 180),
        16 => y < 16 ? new Rgba32(0, 0, 0, 255) : new Rgba32(255, 255, 255, 255),
        _ => new Rgba32(0, 0, 0, 255),
    };

    private static TerrainCorpus CreateCorpus(string root, IReadOnlyList<TerrainFrame> frames, IReadOnlyList<int> relevantSheets)
    {
        var bySheet = new Dictionary<int, IReadOnlyList<TerrainFrame>>();
        foreach (var group in frames.GroupBy(frame => frame.Reference.Sheet))
        {
            bySheet[group.Key] = group.OrderBy(frame => frame.Reference.Graphic).ToList();
        }
        var frameIndex = new TerrainFrameIndex(
            32,
            bySheet.Keys.OrderBy(sheet => sheet).ToList().AsReadOnly(),
            frames
                .OrderBy(frame => frame.Reference.Sheet)
                .ThenBy(frame => frame.Reference.Graphic)
                .ToList()
                .AsReadOnly(),
            bySheet);
        var observed = frames.Select(frame => frame.Reference).Distinct().ToList().AsReadOnly();
        return new TerrainCorpus(
            root,
            "test-fingerprint",
            new[] { new TerrainMapDescriptor("Assets/Maps/Map1.map", 2, 2, frames.Count) },
            frameIndex,
            observed,
            relevantSheets,
            Array.Empty<TerrainDiagnostic>());
    }

    private sealed class RecordingSheetImageLoader : ITerrainSheetImageLoader
    {
        private readonly Func<string, Image<Rgba32>> _factory;

        public RecordingSheetImageLoader(Func<string, Image<Rgba32>> factory)
        {
            _factory = factory;
        }

        public List<string> Paths { get; } = new();

        public List<Image<Rgba32>> Images { get; } = new();

        public Image<Rgba32> Load(string path)
        {
            var image = _factory(path);
            Paths.Add(path);
            Images.Add(image);
            return image;
        }
    }

    private static int CountDisposed(IEnumerable<Image<Rgba32>> images)
    {
        var count = 0;
        foreach (var image in images)
        {
            try
            {
                _ = image[0, 0];
            }
            catch (ObjectDisposedException)
            {
                count++;
            }
        }

        return count;
    }
}
