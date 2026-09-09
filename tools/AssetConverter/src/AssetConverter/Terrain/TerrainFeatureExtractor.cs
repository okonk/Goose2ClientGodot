using MapEditor.Core.Terrain;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Goose2.AssetConverter.Terrain;

public static class TerrainFeatureExtractor
{
    private const int FrameSize = 32;
    private const int CellSize = 4;
    private const int CellCount = 64;
    private const int CellPixelCount = 16;
    private const int CornerRegionPixelCount = 64;
    private const int EdgeSegmentPixelCount = 4;
    private const int ChannelCount = 5;
    private const double ColorDenominator = 65025.0;
    private const double AlphaDenominator = 255.0;
    private const double LumaDenominator = 256.0 * 65025.0;

    public static TerrainImageFeatures Extract(TerrainGraphicReference reference, Image<Rgba32> image, int originX, int originY)
    {
        var cellAlpha = new long[CellCount];
        var cellRed = new long[CellCount];
        var cellGreen = new long[CellCount];
        var cellBlue = new long[CellCount];
        var cellLuma = new long[CellCount];
        var paletteBins = new long[TerrainFeatureBuckets.PaletteBinCount];
        var corners = new long[4, ChannelCount];
        var edges = new long[4, 4, 8, ChannelCount];
        var totalAlpha = 0L;

        for (var y = 0; y < FrameSize; y++)
        {
            for (var x = 0; x < FrameSize; x++)
            {
                var pixel = image[originX + x, originY + y];
                var r = pixel.R;
                var g = pixel.G;
                var b = pixel.B;
                var a = pixel.A;
                var cell = (y / CellSize) * 8 + x / CellSize;

                totalAlpha = checked(totalAlpha + a);
                cellAlpha[cell] = checked(cellAlpha[cell] + a);
                cellRed[cell] = checked(cellRed[cell] + r * a);
                cellGreen[cell] = checked(cellGreen[cell] + g * a);
                cellBlue[cell] = checked(cellBlue[cell] + b * a);
                cellLuma[cell] = checked(cellLuma[cell] + (54 * r + 183 * g + 19 * b) * a);

                paletteBins[(r >> 6) * 16 + (g >> 6) * 4 + (b >> 6)] =
                    checked(paletteBins[(r >> 6) * 16 + (g >> 6) * 4 + (b >> 6)] + a);
                paletteBins[64] = checked(paletteBins[64] + (255 - a));

                var corner = x < 8 && y < 8 ? 0 : x >= 24 && y < 8 ? 1 : x >= 24 && y >= 24 ? 2 : x < 8 && y >= 24 ? 3 : -1;
                if (corner >= 0)
                {
                    AddChannels(corners, corner, r, g, b, a);
                }

                if (y < 4)
                {
                    AddChannels(edges, 0, y, x / 4, r, g, b, a);
                }

                if (x >= 28)
                {
                    AddChannels(edges, 1, 31 - x, y / 4, r, g, b, a);
                }

                if (y >= 28)
                {
                    AddChannels(edges, 2, 31 - y, x / 4, r, g, b, a);
                }

                if (x < 4)
                {
                    AddChannels(edges, 3, x, y / 4, r, g, b, a);
                }
            }
        }

        var alphaCells = new double[CellCount];
        var palette = new double[TerrainFeatureBuckets.PaletteBinCount];
        var perceptualCells = new double[CellCount * ChannelCount];
        for (var i = 0; i < CellCount; i++)
        {
            alphaCells[i] = (double)cellAlpha[i] / (CellPixelCount * AlphaDenominator);
            perceptualCells[i * ChannelCount] = (double)cellRed[i] / (CellPixelCount * ColorDenominator);
            perceptualCells[i * ChannelCount + 1] = (double)cellGreen[i] / (CellPixelCount * ColorDenominator);
            perceptualCells[i * ChannelCount + 2] = (double)cellBlue[i] / (CellPixelCount * ColorDenominator);
            perceptualCells[i * ChannelCount + 3] = (double)cellAlpha[i] / (CellPixelCount * AlphaDenominator);
            perceptualCells[i * ChannelCount + 4] = (double)cellLuma[i] / (CellPixelCount * LumaDenominator);
        }

        for (var i = 0; i < paletteBins.Length; i++)
        {
            palette[i] = (double)paletteBins[i] / (1024.0 * AlphaDenominator);
        }

        var cornerValues = new double[4 * ChannelCount];
        for (var region = 0; region < 4; region++)
        {
            cornerValues[region * ChannelCount] = (double)corners[region, 0] / (CornerRegionPixelCount * ColorDenominator);
            cornerValues[region * ChannelCount + 1] = (double)corners[region, 1] / (CornerRegionPixelCount * ColorDenominator);
            cornerValues[region * ChannelCount + 2] = (double)corners[region, 2] / (CornerRegionPixelCount * ColorDenominator);
            cornerValues[region * ChannelCount + 3] = (double)corners[region, 3] / (CornerRegionPixelCount * AlphaDenominator);
            cornerValues[region * ChannelCount + 4] = (double)corners[region, 4] / (CornerRegionPixelCount * LumaDenominator);
        }

        var edgeValues = new double[4 * 4 * 8 * ChannelCount];
        for (var side = 0; side < 4; side++)
        {
            for (var depth = 0; depth < 4; depth++)
            {
                for (var segment = 0; segment < 8; segment++)
                {
                    var offset = (side * 4 + depth) * 8 * ChannelCount + segment * ChannelCount;
                    edgeValues[offset] = (double)edges[side, depth, segment, 0] / (EdgeSegmentPixelCount * ColorDenominator);
                    edgeValues[offset + 1] = (double)edges[side, depth, segment, 1] / (EdgeSegmentPixelCount * ColorDenominator);
                    edgeValues[offset + 2] = (double)edges[side, depth, segment, 2] / (EdgeSegmentPixelCount * ColorDenominator);
                    edgeValues[offset + 3] = (double)edges[side, depth, segment, 3] / (EdgeSegmentPixelCount * AlphaDenominator);
                    edgeValues[offset + 4] = (double)edges[side, depth, segment, 4] / (EdgeSegmentPixelCount * LumaDenominator);
                }
            }
        }

        var totalLuma = 0L;
        foreach (var value in cellLuma)
        {
            totalLuma = checked(totalLuma + value);
        }

        var hash = 0UL;
        for (var i = 0; i < CellCount; i++)
        {
            if (checked(cellLuma[i] * 64) > totalLuma)
            {
                hash |= 1UL << i;
            }
        }

        var bands = TerrainFeatureBuckets.HashBands(hash);
        var (dominant1, dominant2) = TerrainFeatureBuckets.DominantPaletteBins(paletteBins);
        var buckets = new TerrainFeatureBuckets(
            reference.Sheet,
            TerrainFeatureBuckets.ComputeAlphaDecile(totalAlpha),
            dominant1,
            dominant2,
            bands[0],
            bands[1],
            bands[2],
            bands[3]);

        return new TerrainImageFeatures(reference, alphaCells, palette, perceptualCells, cornerValues, edgeValues, hash, buckets);
    }

    public static double Similarity(TerrainImageFeatures a, TerrainImageFeatures b)
    {
        var (alpha, palette, perceptual, corner, edge) = ComponentDistances(a, b);
        var similarity = 0.15 * (1.0 - alpha)
            + 0.20 * (1.0 - palette)
            + 0.25 * (1.0 - perceptual)
            + 0.15 * (1.0 - corner)
            + 0.25 * (1.0 - edge);
        return Math.Clamp(similarity, 0.0, 1.0);
    }

    internal static (double Alpha, double Palette, double Perceptual, double Corner, double Edge) ComponentDistances(
        TerrainImageFeatures a,
        TerrainImageFeatures b)
    {
        return (
            Math.Clamp(Distance(a.AlphaCells, b.AlphaCells) / 64.0, 0.0, 1.0),
            Math.Clamp(Distance(a.Palette, b.Palette) / 2.0, 0.0, 1.0),
            Math.Clamp(Distance(a.PerceptualCells, b.PerceptualCells) / 320.0, 0.0, 1.0),
            Math.Clamp(Distance(a.Corners, b.Corners) / 20.0, 0.0, 1.0),
            Math.Clamp(Distance(a.Edges, b.Edges) / 640.0, 0.0, 1.0));
    }

    private static double Distance(double[] a, double[] b)
    {
        var sum = 0.0;
        for (var i = 0; i < a.Length; i++)
        {
            sum += Math.Abs(a[i] - b[i]);
        }

        return sum;
    }

    private static void AddChannels(long[,] target, int region, int r, int g, int b, int a)
    {
        target[region, 0] = checked(target[region, 0] + r * a);
        target[region, 1] = checked(target[region, 1] + g * a);
        target[region, 2] = checked(target[region, 2] + b * a);
        target[region, 3] = checked(target[region, 3] + a);
        target[region, 4] = checked(target[region, 4] + (54 * r + 183 * g + 19 * b) * a);
    }

    private static void AddChannels(long[,,,] target, int side, int depth, int segment, int r, int g, int b, int a)
    {
        target[side, depth, segment, 0] = checked(target[side, depth, segment, 0] + r * a);
        target[side, depth, segment, 1] = checked(target[side, depth, segment, 1] + g * a);
        target[side, depth, segment, 2] = checked(target[side, depth, segment, 2] + b * a);
        target[side, depth, segment, 3] = checked(target[side, depth, segment, 3] + a);
        target[side, depth, segment, 4] = checked(target[side, depth, segment, 4] + (54 * r + 183 * g + 19 * b) * a);
    }
}
