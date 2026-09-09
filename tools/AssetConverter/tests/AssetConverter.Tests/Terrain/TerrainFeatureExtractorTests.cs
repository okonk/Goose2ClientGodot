using Goose2.AssetConverter.Terrain;
using MapEditor.Core.Terrain;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AssetConverter.Tests.Terrain;

public class TerrainFeatureExtractorTests
{
    private static readonly TerrainGraphicReference Reference = new(1, 7);

    [Fact]
    public void Extract_TransparentNoiseBlackWhiteAndSplitMatchLockedVectorsAndHashes()
    {
        using (var transparentImage = CreateImage((x, y) => new Rgba32(9, 8, 7, 0)))
        {
            var transparent = TerrainFeatureExtractor.Extract(Reference, transparentImage, 0, 0);
            Assert.Equal(new double[64], transparent.AlphaCells);
            var transparentPalette = new double[65];
            transparentPalette[64] = 1.0;
            Assert.Equal(transparentPalette, transparent.Palette);
            Assert.Equal(new double[320], transparent.PerceptualCells);
            Assert.Equal(new double[20], transparent.Corners);
            Assert.Equal(new double[640], transparent.Edges);
            Assert.Equal(0UL, transparent.PerceptualHash);
            Assert.Equal(1, transparent.Buckets.Sheet);
            Assert.Equal(0, transparent.Buckets.AlphaDecile);
            Assert.Equal(64, transparent.Buckets.DominantPaletteBin1);
            Assert.Equal(0, transparent.Buckets.DominantPaletteBin2);
            Assert.Equal(0, transparent.Buckets.HashBand0);
            Assert.Equal(0, transparent.Buckets.HashBand1);
            Assert.Equal(0, transparent.Buckets.HashBand2);
            Assert.Equal(0, transparent.Buckets.HashBand3);
        }

        using (var blackImage = CreateImage((x, y) => new Rgba32(0, 0, 0, 255)))
        {
            var black = TerrainFeatureExtractor.Extract(Reference, blackImage, 0, 0);
            var blackChannels = Channels(0, 0, 0, 255);
            Assert.Equal(Enumerable.Repeat(1.0, 64).ToArray(), black.AlphaCells);
            var blackPalette = new double[65];
            blackPalette[0] = 1.0;
            Assert.Equal(blackPalette, black.Palette);
            Assert.Equal(Repeat(blackChannels, 64), black.PerceptualCells);
            Assert.Equal(Repeat(blackChannels, 4), black.Corners);
            Assert.Equal(Repeat(blackChannels, 128), black.Edges);
            Assert.Equal(0UL, black.PerceptualHash);
            Assert.Equal(9, black.Buckets.AlphaDecile);
            Assert.Equal(0, black.Buckets.DominantPaletteBin1);
            Assert.Equal(1, black.Buckets.DominantPaletteBin2);
        }

        using (var whiteImage = CreateImage((x, y) => new Rgba32(255, 255, 255, 255)))
        {
            var white = TerrainFeatureExtractor.Extract(Reference, whiteImage, 0, 0);
            var whiteChannels = Channels(255, 255, 255, 255);
            Assert.Equal(Enumerable.Repeat(1.0, 64).ToArray(), white.AlphaCells);
            var whitePalette = new double[65];
            whitePalette[63] = 1.0;
            Assert.Equal(whitePalette, white.Palette);
            Assert.Equal(Repeat(whiteChannels, 64), white.PerceptualCells);
            Assert.Equal(Repeat(whiteChannels, 4), white.Corners);
            Assert.Equal(Repeat(whiteChannels, 128), white.Edges);
            Assert.Equal(0UL, white.PerceptualHash);
            Assert.Equal(9, white.Buckets.AlphaDecile);
            Assert.Equal(63, white.Buckets.DominantPaletteBin1);
            Assert.Equal(0, white.Buckets.DominantPaletteBin2);
        }

        using (var splitImage = CreateImage((x, y) => x < 16 ? new Rgba32(0, 0, 0, 255) : new Rgba32(255, 255, 255, 255)))
        {
            var split = TerrainFeatureExtractor.Extract(Reference, splitImage, 0, 0);
            var blackChannels = Channels(0, 0, 0, 255);
            var whiteChannels = Channels(255, 255, 255, 255);
            var splitPalette = new double[65];
            splitPalette[0] = 0.5;
            splitPalette[63] = 0.5;
            Assert.Equal(splitPalette, split.Palette);

            var splitPerceptual = new double[320];
            for (var cy = 0; cy < 8; cy++)
            {
                for (var cx = 0; cx < 8; cx++)
                {
                    CopyChannels(splitPerceptual, (cy * 8 + cx) * 5, cx < 4 ? blackChannels : whiteChannels);
                }
            }

            Assert.Equal(splitPerceptual, split.PerceptualCells);

            var splitCorners = new double[20];
            CopyChannels(splitCorners, 0, blackChannels);
            CopyChannels(splitCorners, 5, whiteChannels);
            CopyChannels(splitCorners, 10, whiteChannels);
            CopyChannels(splitCorners, 15, blackChannels);
            Assert.Equal(splitCorners, split.Corners);

            var splitEdges = new double[640];
            for (var d = 0; d < 4; d++)
            {
                for (var s = 0; s < 8; s++)
                {
                    CopyEdge(splitEdges, 0, d, s, s < 4 ? blackChannels : whiteChannels);
                    CopyEdge(splitEdges, 1, d, s, whiteChannels);
                    CopyEdge(splitEdges, 2, d, s, s < 4 ? blackChannels : whiteChannels);
                    CopyEdge(splitEdges, 3, d, s, blackChannels);
                }
            }

            Assert.Equal(splitEdges, split.Edges);
            Assert.Equal(0xF0F0F0F0F0F0F0F0UL, split.PerceptualHash);
            Assert.Equal(9, split.Buckets.AlphaDecile);
            Assert.Equal(0, split.Buckets.DominantPaletteBin1);
            Assert.Equal(63, split.Buckets.DominantPaletteBin2);
            Assert.Equal((ushort)0xF0F0, split.Buckets.HashBand0);
            Assert.Equal((ushort)0xF0F0, split.Buckets.HashBand1);
            Assert.Equal((ushort)0xF0F0, split.Buckets.HashBand2);
            Assert.Equal((ushort)0xF0F0, split.Buckets.HashBand3);
        }
    }

    [Fact]
    public void Extract_TransparentRgbNoiseProducesIdenticalFeatures()
    {
        using var calm = CreateImage((x, y) => new Rgba32(255, 0, 255, 0));
        using var noisy = CreateImage(
            (x, y) => new Rgba32(
                (byte)((x * 7 + y * 13) % 251),
                (byte)(x ^ y),
                (byte)((x + y * 31) % 253),
                0));
        var calmFeatures = TerrainFeatureExtractor.Extract(Reference, calm, 0, 0);
        var noisyFeatures = TerrainFeatureExtractor.Extract(Reference, noisy, 0, 0);
        Assert.Equal(calmFeatures.AlphaCells, noisyFeatures.AlphaCells);
        Assert.Equal(calmFeatures.Palette, noisyFeatures.Palette);
        Assert.Equal(calmFeatures.PerceptualCells, noisyFeatures.PerceptualCells);
        Assert.Equal(calmFeatures.Corners, noisyFeatures.Corners);
        Assert.Equal(calmFeatures.Edges, noisyFeatures.Edges);
        Assert.Equal(calmFeatures.PerceptualHash, noisyFeatures.PerceptualHash);
        Assert.Equal(calmFeatures.Buckets, noisyFeatures.Buckets);
    }

    [Fact]
    public void Extract_DirectionalBordersAndCornersUseLockedOrientation()
    {
        using var image = CreateImage(BorderPixel);
        var features = TerrainFeatureExtractor.Extract(Reference, image, 0, 0);

        var expectedCorners = new double[20];
        CopyChannels(expectedCorners, 0, Channels(1, 2, 3, 255));
        CopyChannels(expectedCorners, 5, Channels(4, 5, 6, 255));
        CopyChannels(expectedCorners, 10, Channels(7, 8, 9, 255));
        CopyChannels(expectedCorners, 15, Channels(10, 11, 12, 255));
        Assert.Equal(expectedCorners, features.Corners);

        var north = Channels(100, 110, 120, 255);
        var east = Channels(190, 200, 210, 255);
        var south = Channels(130, 140, 150, 255);
        var west = Channels(160, 170, 180, 255);
        var northwest = Channels(1, 2, 3, 255);
        var northeast = Channels(4, 5, 6, 255);
        var southeast = Channels(7, 8, 9, 255);
        var southwest = Channels(10, 11, 12, 255);
        var expectedEdges = new double[640];
        for (var d = 0; d < 4; d++)
        {
            for (var s = 0; s < 8; s++)
            {
                CopyEdge(expectedEdges, 0, d, s, s <= 1 ? northwest : s >= 6 ? northeast : north);
                CopyEdge(expectedEdges, 1, d, s, s <= 1 ? northeast : s >= 6 ? southeast : east);
                CopyEdge(expectedEdges, 2, d, s, s <= 1 ? southwest : s >= 6 ? southeast : south);
                CopyEdge(expectedEdges, 3, d, s, s <= 1 ? northwest : s >= 6 ? southwest : west);
            }
        }

        Assert.Equal(expectedEdges, features.Edges);
    }

    [Fact]
    public void Similarity_UsesLockedComponentDistancesAndIsSymmetricBounded()
    {
        var a = CreateFeatures(new double[64], new double[65], new double[320], new double[20], new double[640], 0UL);
        var bAlpha = Enumerable.Repeat(0.5, 64).ToArray();
        var bPalette = new double[65];
        bPalette[0] = 1.0;
        var bPerceptual = new double[320];
        bPerceptual[0] = 1.0;
        var bCorner = new double[20];
        bCorner[0] = 1.0;
        var bEdge = new double[640];
        bEdge[0] = 1.0;
        var b = CreateFeatures(bAlpha, bPalette, bPerceptual, bCorner, bEdge, 0UL);

        var (alpha, palette, perceptual, corner, edge) = TerrainFeatureExtractor.ComponentDistances(a, b);
        Assert.Equal(0.5, alpha);
        Assert.Equal(0.5, palette);
        Assert.Equal(1.0 / 320.0, perceptual);
        Assert.Equal(1.0 / 20.0, corner);
        Assert.Equal(1.0 / 640.0, edge);

        var expected = 0.15 * 0.5
            + 0.20 * 0.5
            + 0.25 * (1.0 - 1.0 / 320.0)
            + 0.15 * (1.0 - 1.0 / 20.0)
            + 0.25 * (1.0 - 1.0 / 640.0);
        Assert.Equal(expected, TerrainFeatureExtractor.Similarity(a, b));
        Assert.Equal(TerrainFeatureExtractor.Similarity(a, b), TerrainFeatureExtractor.Similarity(b, a));
        Assert.Equal(1.0, TerrainFeatureExtractor.Similarity(a, a));

        var clampedPalette = new double[65];
        clampedPalette[0] = 3.0;
        var clamped = CreateFeatures(new double[64], clampedPalette, new double[320], new double[20], new double[640], 0UL);
        Assert.Equal(1.0, TerrainFeatureExtractor.ComponentDistances(a, clamped).Palette);
    }

    private static Rgba32 BorderPixel(int x, int y)
    {
        if (y < 8)
        {
            if (x < 8)
            {
                return new Rgba32(1, 2, 3, 255);
            }

            if (x >= 24)
            {
                return new Rgba32(4, 5, 6, 255);
            }
        }
        else if (y >= 24)
        {
            if (x < 8)
            {
                return new Rgba32(10, 11, 12, 255);
            }

            if (x >= 24)
            {
                return new Rgba32(7, 8, 9, 255);
            }
        }

        if (y < 4)
        {
            return new Rgba32(100, 110, 120, 255);
        }

        if (y >= 28)
        {
            return new Rgba32(130, 140, 150, 255);
        }

        if (x < 4)
        {
            return new Rgba32(160, 170, 180, 255);
        }

        if (x >= 28)
        {
            return new Rgba32(190, 200, 210, 255);
        }

        return new Rgba32(0, 0, 0, 255);
    }

    private static TerrainImageFeatures CreateFeatures(
        double[] alpha,
        double[] palette,
        double[] perceptual,
        double[] corners,
        double[] edges,
        ulong hash)
    {
        var bands = TerrainFeatureBuckets.HashBands(hash);
        return new TerrainImageFeatures(
            Reference,
            alpha,
            palette,
            perceptual,
            corners,
            edges,
            hash,
            new TerrainFeatureBuckets(1, 9, 0, 1, bands[0], bands[1], bands[2], bands[3]));
    }

    private static double[] Channels(int r, int g, int b, int a) =>
    [
        (double)r * a / 65025.0,
        (double)g * a / 65025.0,
        (double)b * a / 65025.0,
        (double)a / 255.0,
        (double)(54 * r + 183 * g + 19 * b) * a / 16646400.0,
    ];

    private static double[] Repeat(double[] channels, int regions)
    {
        var values = new double[regions * channels.Length];
        for (var i = 0; i < regions; i++)
        {
            CopyChannels(values, i * channels.Length, channels);
        }

        return values;
    }

    private static void CopyChannels(double[] target, int offset, double[] channels)
    {
        for (var c = 0; c < channels.Length; c++)
        {
            target[offset + c] = channels[c];
        }
    }

    private static void CopyEdge(double[] edges, int side, int depth, int segment, double[] channels)
        => CopyChannels(edges, side * 160 + depth * 40 + segment * 5, channels);

    private static Image<Rgba32> CreateImage(Func<int, int, Rgba32> pixel)
    {
        var image = new Image<Rgba32>(32, 32);
        for (var y = 0; y < 32; y++)
        {
            for (var x = 0; x < 32; x++)
            {
                image[x, y] = pixel(x, y);
            }
        }

        return image;
    }
}
