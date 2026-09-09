using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MapEditor.Core.Terrain;
using Xunit;

namespace MapEditor.Core.Tests.Terrain;

public class TerrainMasksTests
{
    [Fact]
    public void Required_FourWay_ReturnsAll16CardinalMasksInOrder()
    {
        var required = TerrainMasks.Required(TerrainTopology.FourWay);

        Assert.Equal(Enumerable.Range(0, 16).ToList(), required);
        Assert.ThrowsAny<Exception>(() => ((IList<int>)required).Add(16));
    }

    [Fact]
    public void Required_EightWay_ReturnsExactly47NormalizedMasksInOrder()
    {
        var expected = new[]
        {
            0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15,
            19, 23, 27, 31, 38, 39, 46, 47, 55, 63, 76, 77, 78, 79,
            95, 110, 111, 127, 137, 139, 141, 143, 155, 159, 175, 191,
            205, 207, 223, 239, 255
        };

        var required = TerrainMasks.Required(TerrainTopology.EightWay);

        Assert.Equal(expected, required);
        Assert.All(required, mask => Assert.True(TerrainMasks.IsReachable(mask, TerrainTopology.EightWay)));
        Assert.ThrowsAny<Exception>(() => ((IList<int>)required).Add(0));
    }

    [Fact]
    public void Normalize_EightWay_RemovesUnsupportedDiagonalBits()
    {
        var requiredEight = TerrainMasks.Required(TerrainTopology.EightWay).ToHashSet();

        for (var mask = 0; mask < 256; mask++)
        {
            var normalized = TerrainMasks.Normalize(mask, TerrainTopology.EightWay);

            Assert.InRange(normalized, 0, 255);
            Assert.Equal(normalized, TerrainMasks.Normalize(normalized, TerrainTopology.EightWay));
            Assert.Contains(normalized, requiredEight);
            Assert.Equal(mask & 0x0F, normalized & 0x0F);
            Assert.Equal(mask == normalized, TerrainMasks.IsReachable(mask, TerrainTopology.EightWay));
        }

        Assert.Equal(0, TerrainMasks.Normalize(TerrainMasks.NorthEast, TerrainTopology.EightWay));
        Assert.Equal(19, TerrainMasks.Normalize(TerrainMasks.NorthEast | TerrainMasks.North | TerrainMasks.East, TerrainTopology.EightWay));
        Assert.Equal(0, TerrainMasks.Normalize(TerrainMasks.SouthEast, TerrainTopology.EightWay));
        Assert.Equal(38, TerrainMasks.Normalize(TerrainMasks.SouthEast | TerrainMasks.South | TerrainMasks.East, TerrainTopology.EightWay));
        Assert.Equal(76, TerrainMasks.Normalize(TerrainMasks.SouthWest | TerrainMasks.South | TerrainMasks.West, TerrainTopology.EightWay));
        Assert.Equal(137, TerrainMasks.Normalize(TerrainMasks.NorthWest | TerrainMasks.North | TerrainMasks.West, TerrainTopology.EightWay));
        Assert.Equal(255, TerrainMasks.Normalize(255, TerrainTopology.EightWay));
        Assert.Equal(255, TerrainMasks.Normalize(0x1FF, TerrainTopology.EightWay));

        for (var mask = 0; mask < 256; mask++)
        {
            Assert.Equal(mask & 0x0F, TerrainMasks.Normalize(mask, TerrainTopology.FourWay));
            Assert.Equal((mask & 0x0F) == mask, TerrainMasks.IsReachable(mask, TerrainTopology.FourWay));
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => TerrainMasks.Normalize(0, (TerrainTopology)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => TerrainMasks.IsReachable(0, (TerrainTopology)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => TerrainMasks.Required((TerrainTopology)99));
    }

    [Fact]
    public void Create_ReorderedMembersProducesSameId()
    {
        var members = new[]
        {
            new TerrainGraphicReference(2, 5),
            new TerrainGraphicReference(1, 9),
            new TerrainGraphicReference(1, 2)
        };

        var id = TerrainGeneratedId.Create(TerrainTopology.FourWay, members);
        var reordered = TerrainGeneratedId.Create(TerrainTopology.FourWay, members.Reverse().ToArray());

        Assert.Equal(id, reordered);
        Assert.Matches(new Regex(@"^terrain-4-[0-9a-f]{64}$"), id);
        var expected = "terrain-4-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("four-way|1:2,1:9,2:5"))).ToLowerInvariant();
        Assert.Equal(expected, id);
    }

    [Fact]
    public void Create_EmptyOrDuplicateMembersThrows()
    {
        Assert.Throws<ArgumentException>(() => TerrainGeneratedId.Create(TerrainTopology.FourWay, Array.Empty<TerrainGraphicReference>()));
        Assert.Throws<ArgumentException>(() => TerrainGeneratedId.Create(TerrainTopology.EightWay, new[] { new TerrainGraphicReference(1, 1), new TerrainGraphicReference(1, 1) }));
        Assert.Throws<ArgumentOutOfRangeException>(() => TerrainGeneratedId.Create((TerrainTopology)99, new[] { new TerrainGraphicReference(1, 1) }));
    }

    [Fact]
    public void Create_ChangedTopologyOrMemberChangesId()
    {
        var members = new[] { new TerrainGraphicReference(1, 1), new TerrainGraphicReference(1, 2) };
        var four = TerrainGeneratedId.Create(TerrainTopology.FourWay, members);
        var eight = TerrainGeneratedId.Create(TerrainTopology.EightWay, members);

        Assert.NotEqual(four, eight);
        Assert.Matches(new Regex(@"^terrain-8-[0-9a-f]{64}$"), eight);
        Assert.NotEqual(four, TerrainGeneratedId.Create(TerrainTopology.FourWay, new[] { new TerrainGraphicReference(1, 1), new TerrainGraphicReference(1, 3) }));
        Assert.NotEqual(four, TerrainGeneratedId.Create(TerrainTopology.FourWay, new[] { new TerrainGraphicReference(1, 1), new TerrainGraphicReference(1, 2), new TerrainGraphicReference(2, 1) }));
    }
}
