namespace Goose2.AssetConverter.Terrain;

public readonly record struct TerrainFeatureBuckets(
    int Sheet,
    int AlphaDecile,
    int DominantPaletteBin1,
    int DominantPaletteBin2,
    ushort HashBand0,
    ushort HashBand1,
    ushort HashBand2,
    ushort HashBand3)
{
    public const int PaletteBinCount = 65;
    public const int AlphaSumDenominator = 1024 * 255;

    public static int ComputeAlphaDecile(long alphaSum)
        => Math.Min(9, (int)(checked(alphaSum * 10) / AlphaSumDenominator));

    public static (int First, int Second) DominantPaletteBins(long[] binSums)
    {
        var first = 0;
        for (var i = 1; i < binSums.Length; i++)
        {
            if (binSums[i] > binSums[first])
            {
                first = i;
            }
        }

        var second = first == 0 ? 1 : 0;
        for (var i = 0; i < binSums.Length; i++)
        {
            if (i == first)
            {
                continue;
            }

            if (binSums[i] > binSums[second])
            {
                second = i;
            }
        }

        return (first, second);
    }

    public static ushort[] HashBands(ulong hash) =>
    [
        (ushort)(hash & 0xFFFF),
        (ushort)((hash >> 16) & 0xFFFF),
        (ushort)((hash >> 32) & 0xFFFF),
        (ushort)((hash >> 48) & 0xFFFF),
    ];

    public bool IsComparable(TerrainFeatureBuckets other)
    {
        if (Sheet != other.Sheet || Math.Abs(AlphaDecile - other.AlphaDecile) > 1)
        {
            return false;
        }

        if (DominantPaletteBin1 != other.DominantPaletteBin1
            && DominantPaletteBin1 != other.DominantPaletteBin2
            && DominantPaletteBin2 != other.DominantPaletteBin1
            && DominantPaletteBin2 != other.DominantPaletteBin2)
        {
            return false;
        }

        var equalBands = (HashBand0 == other.HashBand0 ? 1 : 0)
            + (HashBand1 == other.HashBand1 ? 1 : 0)
            + (HashBand2 == other.HashBand2 ? 1 : 0)
            + (HashBand3 == other.HashBand3 ? 1 : 0);
        return equalBands >= 3;
    }
}
