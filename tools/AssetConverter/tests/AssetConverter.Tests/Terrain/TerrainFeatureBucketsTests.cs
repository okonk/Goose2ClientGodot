using Goose2.AssetConverter.Terrain;

namespace AssetConverter.Tests.Terrain;

public class TerrainFeatureBucketsTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(26111, 0)]
    [InlineData(26112, 1)]
    [InlineData(195840, 7)]
    [InlineData(209952, 8)]
    [InlineData(235968, 9)]
    [InlineData(261120, 9)]
    public void AlphaDecile_FloorsMeanAlphaTimesTenAndClampsAtNine(long alphaSum, int expected)
    {
        Assert.Equal(expected, TerrainFeatureBuckets.ComputeAlphaDecile(alphaSum));
    }

    [Fact]
    public void DominantPaletteBins_TakesGreatestMassThenDistinctSecondTieByLowerId()
    {
        var transparent = new long[65];
        transparent[64] = 261120;
        Assert.Equal((64, 0), TerrainFeatureBuckets.DominantPaletteBins(transparent));

        var black = new long[65];
        black[0] = 261120;
        Assert.Equal((0, 1), TerrainFeatureBuckets.DominantPaletteBins(black));

        var white = new long[65];
        white[63] = 261120;
        Assert.Equal((63, 0), TerrainFeatureBuckets.DominantPaletteBins(white));

        var split = new long[65];
        split[0] = 130560;
        split[63] = 130560;
        Assert.Equal((0, 63), TerrainFeatureBuckets.DominantPaletteBins(split));

        var tiedSecond = new long[65];
        tiedSecond[63] = 100;
        tiedSecond[0] = 50;
        tiedSecond[7] = 50;
        Assert.Equal((63, 0), TerrainFeatureBuckets.DominantPaletteBins(tiedSecond));
    }

    [Fact]
    public void HashBands_TakeNumericBitRangesInOrder()
    {
        var bands = TerrainFeatureBuckets.HashBands(0xA5A53C3C1234DEADUL);
        Assert.Equal((ushort)0xDEAD, bands[0]);
        Assert.Equal((ushort)0x1234, bands[1]);
        Assert.Equal((ushort)0x3C3C, bands[2]);
        Assert.Equal((ushort)0xA5A5, bands[3]);
    }

    [Fact]
    public void IsComparable_RequiresSameSheetDecileWithinOneCommonDominantAndThreeBands()
    {
        var baseline = new TerrainFeatureBuckets(1, 5, 3, 9, 0x0001, 0x0002, 0x0003, 0x0004);
        Assert.True(baseline.IsComparable(baseline));
        Assert.False(baseline.IsComparable(new TerrainFeatureBuckets(2, 5, 3, 9, 0x0001, 0x0002, 0x0003, 0x0004)));
        Assert.True(baseline.IsComparable(new TerrainFeatureBuckets(1, 6, 3, 9, 0x0001, 0x0002, 0x0003, 0x0004)));
        Assert.True(baseline.IsComparable(new TerrainFeatureBuckets(1, 4, 3, 9, 0x0001, 0x0002, 0x0003, 0x0004)));
        Assert.False(baseline.IsComparable(new TerrainFeatureBuckets(1, 7, 3, 9, 0x0001, 0x0002, 0x0003, 0x0004)));
        Assert.True(baseline.IsComparable(new TerrainFeatureBuckets(1, 5, 7, 3, 0x0001, 0x0002, 0x0003, 0x0004)));
        Assert.True(baseline.IsComparable(new TerrainFeatureBuckets(1, 5, 7, 9, 0x0001, 0x0002, 0x0003, 0x0004)));
        Assert.False(baseline.IsComparable(new TerrainFeatureBuckets(1, 5, 7, 8, 0x0001, 0x0002, 0x0003, 0x0004)));
        Assert.True(baseline.IsComparable(new TerrainFeatureBuckets(1, 5, 3, 9, 0x0001, 0x0002, 0x0003, 0x0005)));
        Assert.False(baseline.IsComparable(new TerrainFeatureBuckets(1, 5, 3, 9, 0x0001, 0x0002, 0x0005, 0x0006)));
    }
}
