using System;
using System.Globalization;
using MapEditor.Core;
using Xunit;

namespace MapEditor.Core.Tests;

public class TerrainStableHashTests
{
    private static readonly Guid Id1 = new("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Id2 = new("01234567-89ab-cdef-0123-456789abcdef");
    private static readonly Guid Id3 = new("ffffffff-ffff-ffff-ffff-ffffffffffff");

    [Fact]
    public void PatternDomain_AllNullPeers_MatchesGoldenVector()
    {
        var hash = TerrainStableHash.HashPattern(Id1, 0, 0, new TerrainPattern(Center: Id1));

        Assert.Equal(0x10AD570CEC1F1968UL, hash);
    }

    [Fact]
    public void PatternDomain_NegativeCoordinatesAndMixedPeers_MatchesGoldenVector()
    {
        var desired = new TerrainPattern(Center: Id2, North: Id1, South: Id3, West: Id2, SouthEast: Id1, NorthWest: Id3);
        var hash = TerrainStableHash.HashPattern(Id2, -1, 42, desired);

        Assert.Equal(0x9E98B8B54B27BAA4UL, hash);
    }

    [Fact]
    public void VariantDomain_AllId1_MatchesGoldenVector()
    {
        var desired = new TerrainPattern(Center: Id1, North: Id1, East: Id1, South: Id1, West: Id1, NorthEast: Id1, SouthEast: Id1, SouthWest: Id1, NorthWest: Id1);
        var hash = TerrainStableHash.HashVariant(Id1, 7, 9, desired, desired);

        Assert.Equal(0x94351B02ED09FACDUL, hash);
    }

    [Fact]
    public void PatternAndVariantDomains_DifferForSameInput()
    {
        var pattern = new TerrainPattern(Center: Id1, North: Id2);

        Assert.NotEqual(
            TerrainStableHash.HashPattern(Id1, 0, 0, pattern),
            TerrainStableHash.HashVariant(Id1, 0, 0, pattern, pattern));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    public void GoldenVectors_AreCultureIndependent(string cultureName)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(cultureName);

            Assert.Equal(0x10AD570CEC1F1968UL, TerrainStableHash.HashPattern(Id1, 0, 0, new TerrainPattern(Center: Id1)));

            var desired = new TerrainPattern(Center: Id2, North: Id1, South: Id3, West: Id2, SouthEast: Id1, NorthWest: Id3);
            Assert.Equal(0x9E98B8B54B27BAA4UL, TerrainStableHash.HashPattern(Id2, -1, 42, desired));

            var allId1 = new TerrainPattern(Center: Id1, North: Id1, East: Id1, South: Id1, West: Id1, NorthEast: Id1, SouthEast: Id1, SouthWest: Id1, NorthWest: Id1);
            Assert.Equal(0x94351B02ED09FACDUL, TerrainStableHash.HashVariant(Id1, 7, 9, allId1, allId1));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
