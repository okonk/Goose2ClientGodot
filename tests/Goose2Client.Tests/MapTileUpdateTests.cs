using System;
using System.Collections.Generic;
using Goose2Client;
using MapEditor.Core;
using Xunit;

public class MapTileUpdateTests
{
    private static MapDocument Seed(int x, int y)
    {
        var map = MapDocument.Create(4, 4);
        map.SetFlags(x, y, 1);
        for (int layer = 0; layer < MapDocument.LayerCount; layer++)
            map.SetLayer(x, y, layer, new MapTileLayer(layer + 1, 100 + layer));
        return map;
    }

    private static ReadOnlySpan<int> Packet(params (int Graphic, int Sheet)[] layers)
    {
        var values = new int[MapDocument.LayerCount * 2];
        for (int i = 0; i < layers.Length; i++)
        {
            values[i * 2] = layers[i].Graphic;
            values[i * 2 + 1] = layers[i].Sheet;
        }
        return values;
    }

    [Fact]
    public void Apply_ReplacesFlagsAndExactlyFiveLayers()
    {
        var map = Seed(1, 2);
        var neighbor = map[0, 0];
        var redraws = new List<int>();
        var packet = Packet((200, 2), (300, 3), (400, 4), (500, 5), (600, 6));

        MapTileUpdate.Apply(map, 1, 2, 7, packet, redraws.Add);

        var tile = map[1, 2];
        Assert.Equal(7, tile.Flags);
        for (int layer = 0; layer < MapDocument.LayerCount; layer++)
            Assert.Equal(new MapTileLayer(layer + 2, (layer + 2) * 100), tile.GetLayer(layer));
        Assert.Equal(neighbor, map[0, 0]);
        Assert.Equal(new List<int> { 0, 1, 2, 3, 4 }, redraws);
    }

    [Fact]
    public void Apply_SheetZeroClearsGraphic()
    {
        var map = Seed(1, 2);
        var redraws = new List<int>();
        var packet = Packet((999, 0), (300, 3), (400, 4), (500, 5), (600, 6));

        MapTileUpdate.Apply(map, 1, 2, 1, packet, redraws.Add);

        Assert.Equal(new MapTileLayer(0, 0), map[1, 2].GetLayer(0));
        Assert.Single(redraws, 0);
    }

    [Fact]
    public void Apply_UnchangedNormalizedLayersRequestNoRedraw()
    {
        var map = MapDocument.Create(4, 4);
        map.SetFlags(1, 2, 1);
        map.SetLayer(1, 2, 0, new MapTileLayer(0, 0));
        for (int layer = 1; layer < MapDocument.LayerCount; layer++)
            map.SetLayer(1, 2, layer, new MapTileLayer(layer + 1, (layer + 1) * 100));
        var redraws = new List<int>();
        var packet = Packet((5, 0), (200, 2), (300, 3), (400, 4), (500, 5));

        MapTileUpdate.Apply(map, 1, 2, 9, packet, redraws.Add);

        Assert.Equal(9, map[1, 2].Flags);
        Assert.Empty(redraws);
    }

    [Fact]
    public void Apply_MutatesBeforeEachRedrawCallback()
    {
        var map = Seed(1, 2);
        var packet = Packet((200, 2), (300, 3), (400, 4), (500, 5), (600, 6));

        MapTileUpdate.Apply(map, 1, 2, 3, packet, layer =>
        {
            var tile = map[1, 2];
            Assert.Equal(3, tile.Flags);
            Assert.Equal(new MapTileLayer(layer + 2, (layer + 2) * 100), tile.GetLayer(layer));
        });
    }
}
