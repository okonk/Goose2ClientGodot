using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using MapEditor.Core;
using Xunit;

namespace MapEditor.Core.Tests;

public class MapDocumentTests
{
    [Fact]
    public void Create_Uses100x100DefaultsAndVersions1And10()
    {
        var doc = MapDocument.Create();

        Assert.Equal(100, doc.Width);
        Assert.Equal(100, doc.Height);
        Assert.Equal(10_000, doc.TileCount);
        Assert.Equal((short)1, doc.Version);
        Assert.Equal((short)10, doc.EditorVersion);
    }

    [Fact]
    public void Create_InitializesEveryTileWithZeroFlagsAndFiveEmptyLayers()
    {
        var doc = MapDocument.Create();

        Assert.Equal(10_000, doc.TileCount);
        foreach (var (x, y) in new[] { (0, 0), (50, 50), (99, 99) })
        {
            var tile = doc[x, y];
            Assert.Equal(0, tile.Flags);
            for (var layer = 0; layer < MapDocument.LayerCount; layer++)
            {
                Assert.Equal(new MapTileLayer(0, 0), tile.GetLayer(layer));
            }
        }
    }

    [Fact]
    public void Create_AcceptsMinimumAndMaximumDimensions()
    {
        var small = MapDocument.Create(1, 1);
        Assert.Equal(1, small.TileCount);
        Assert.Equal(0, small[0, 0].Flags);
        Assert.Equal(0, small.GetTile(0).Flags);

        var large = MapDocument.Create(1000, 1000);
        Assert.Equal(1_000_000, large.TileCount);
        Assert.Equal(0, large[999, 999].Flags);
        Assert.Equal(0, large.GetTile(999_999).Flags);
    }

    [Fact]
    public void Create_RejectsEachDimensionOutside1Through1000()
    {
        foreach (var value in new[] { 0, -1, 1001, int.MaxValue })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => MapDocument.Create(value, 100));
            Assert.Throws<ArgumentOutOfRangeException>(() => MapDocument.Create(100, value));
        }
    }

    [Fact]
    public void Tiles_AreCompactInlineValueStorage()
    {
        Assert.True(typeof(MapTile).IsValueType);
        Assert.True(typeof(MapTileLayer).IsValueType);
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<MapTile>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<MapTileLayer>());

        var tileFields = typeof(MapTile).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.Equal(6, tileFields.Length);
        Assert.Equal(5, tileFields.Count(f => f.FieldType == typeof(MapTileLayer)));
        Assert.Equal(1, tileFields.Count(f => f.FieldType == typeof(int)));
        Assert.DoesNotContain(tileFields, f => f.FieldType.IsArray || !f.FieldType.IsValueType);

        var docFields = typeof(MapDocument).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var arrayFields = docFields.Where(f => f.FieldType.IsArray).ToList();
        Assert.Single(arrayFields);
        Assert.Equal(typeof(MapTile[]), arrayFields[0].FieldType);
    }

    [Fact]
    public void Coordinates_AreRowMajor()
    {
        var doc = MapDocument.Create(2, 2);
        doc.SetFlags(0, 0, 1);
        doc.SetFlags(1, 0, 2);
        doc.SetFlags(0, 1, 4);
        doc.SetFlags(1, 1, 8);

        Assert.Equal(1, doc.GetTile(0).Flags);
        Assert.Equal(2, doc.GetTile(1).Flags);
        Assert.Equal(4, doc.GetTile(2).Flags);
        Assert.Equal(8, doc.GetTile(3).Flags);
    }

    [Fact]
    public void SetFlags_ChangesOnlyFlagsAtTarget()
    {
        var doc = MapDocument.Create(2, 2);
        SeedAllLayers(doc);

        var newFlags = unchecked((int)0x80000003);
        doc.SetFlags(1, 1, newFlags);

        Assert.Equal(newFlags, doc[1, 1].Flags);
        Assert.True(doc[1, 1].IsBlocked);
        for (var layer = 0; layer < MapDocument.LayerCount; layer++)
        {
            Assert.Equal(new MapTileLayer(layer, layer * 10), doc[1, 1].GetLayer(layer));
        }

        Assert.Equal(0, doc[0, 0].Flags);
        Assert.Equal(0, doc[1, 0].Flags);
        Assert.Equal(0, doc[0, 1].Flags);
        for (var layer = 0; layer < MapDocument.LayerCount; layer++)
        {
            Assert.Equal(new MapTileLayer(layer, layer * 10), doc[0, 0].GetLayer(layer));
        }
    }

    [Fact]
    public void SetLayer_ChangesOnlySelectedLayerAtTarget()
    {
        var doc = MapDocument.Create(2, 2);
        SeedAllLayers(doc);
        doc.SetFlags(1, 1, 7);

        var replacement = new MapTileLayer(42, 7);
        doc.SetLayer(1, 1, 3, replacement);

        Assert.Equal(7, doc[1, 1].Flags);
        Assert.Equal(replacement, doc[1, 1].GetLayer(3));
        Assert.Equal(new MapTileLayer(0, 0), doc[1, 1].GetLayer(0));
        Assert.Equal(new MapTileLayer(1, 10), doc[1, 1].GetLayer(1));
        Assert.Equal(new MapTileLayer(2, 20), doc[1, 1].GetLayer(2));
        Assert.Equal(new MapTileLayer(4, 40), doc[1, 1].GetLayer(4));

        Assert.Equal(0, doc[0, 0].Flags);
        Assert.Equal(new MapTileLayer(3, 30), doc[0, 0].GetLayer(3));
        Assert.Equal(new MapTileLayer(3, 30), doc[1, 0].GetLayer(3));
        Assert.Equal(new MapTileLayer(3, 30), doc[0, 1].GetLayer(3));
    }

    [Fact]
    public void SetFlagsAndSetLayer_PersistAcrossFreshValueReads()
    {
        var doc = MapDocument.Create(2, 2);
        SeedAllLayers(doc);

        doc.SetFlags(1, 0, 9);
        Assert.Equal(9, doc[1, 0].Flags);
        Assert.Equal(9, doc.GetTile(1).Flags);

        doc.SetLayer(0, 1, 2, new MapTileLayer(5, 6));
        Assert.Equal(new MapTileLayer(5, 6), doc[0, 1].GetLayer(2));
        Assert.Equal(new MapTileLayer(5, 6), doc.GetTile(2).GetLayer(2));

        Assert.Equal(9, doc[1, 0].Flags);
        Assert.Equal(new MapTileLayer(2, 20), doc[1, 0].GetLayer(2));
    }

    [Fact]
    public void MapTile_DerivesBlockedAndRoof()
    {
        var doc = MapDocument.Create(1, 1);
        Assert.Equal(2, MapDocument.BlockedFlag);

        doc.SetFlags(0, 0, MapDocument.BlockedFlag);
        Assert.True(doc[0, 0].IsBlocked);

        doc.SetFlags(0, 0, 1 | 4 | 8);
        Assert.False(doc[0, 0].IsBlocked);

        Assert.False(doc[0, 0].IsRoof);
        doc.SetLayer(0, 0, 4, new MapTileLayer(0, 1));
        Assert.True(doc[0, 0].IsRoof);
        doc.SetLayer(0, 0, 4, new MapTileLayer(0, 0));
        Assert.False(doc[0, 0].IsRoof);
    }

    [Fact]
    public void CoordinatesRowMajorIndexesAndLayers_RejectOutOfRangeWithoutMutation()
    {
        var doc = MapDocument.Create(2, 2);
        SeedAllLayers(doc);

        Assert.Throws<ArgumentOutOfRangeException>(() => doc[-1, 0]);
        Assert.Throws<ArgumentOutOfRangeException>(() => doc[2, 0]);
        Assert.Throws<ArgumentOutOfRangeException>(() => doc[0, -1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => doc[0, 2]);
        Assert.Throws<ArgumentOutOfRangeException>(() => doc.GetTile(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => doc.GetTile(doc.TileCount));
        Assert.Throws<ArgumentOutOfRangeException>(() => doc.SetFlags(2, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => doc.SetFlags(0, -1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => doc.SetLayer(0, 0, -1, default));
        Assert.Throws<ArgumentOutOfRangeException>(() => doc.SetLayer(0, 0, MapDocument.LayerCount, default));

        Assert.Equal(0, doc[0, 0].Flags);
        Assert.Equal(new MapTileLayer(0, 0), doc[0, 0].GetLayer(0));
        Assert.Equal(new MapTileLayer(4, 40), doc[1, 1].GetLayer(4));
    }

    private static void SeedAllLayers(MapDocument doc)
    {
        for (var y = 0; y < doc.Height; y++)
        {
            for (var x = 0; x < doc.Width; x++)
            {
                for (var layer = 0; layer < MapDocument.LayerCount; layer++)
                {
                    doc.SetLayer(x, y, layer, new MapTileLayer(layer, layer * 10));
                }
            }
        }
    }
}
