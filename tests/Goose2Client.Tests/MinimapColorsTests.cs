using System.Collections.Generic;
using Godot;
using Goose2Client.Map;
using MapEditor.Core;
using Xunit;

public class MinimapColorsTests
{
    private static Dictionary<(int, int), Color> _colors;

    private static Color? Provider(int sheet, int graphic)
        => _colors.TryGetValue((sheet, graphic), out var c) ? c : null;

    private static MapTile Tile(params (int layer, int sheet, int graphic)[] layers)
    {
        var map = MapDocument.Create(1, 1);
        foreach (var (layer, sheet, graphic) in layers)
            map.SetLayer(0, 0, layer, new MapTileLayer(sheet, graphic));
        return map[0, 0];
    }

    [Fact]
    public void PickColor_ReturnsTopmostOccupiedLayer()
    {
        _colors = new Dictionary<(int, int), Color>
        {
            [(1, 10)] = new Color(1, 0, 0),
            [(2, 20)] = new Color(0, 1, 0),
        };
        var tile = Tile((0, 1, 10), (4, 2, 20));
        Assert.Equal(new Color(0, 1, 0), MinimapColors.PickColor(tile, Provider));
    }

    [Fact]
    public void PickColor_Layer3BeatsLayer2()
    {
        _colors = new Dictionary<(int, int), Color>
        {
            [(2, 10)] = new Color(1, 0, 0),
            [(3, 20)] = new Color(0, 1, 0),
        };
        var tile = Tile((2, 2, 10), (3, 3, 20));
        Assert.Equal(new Color(0, 1, 0), MinimapColors.PickColor(tile, Provider));
    }

    [Fact]
    public void PickColor_Layer2BeatsLayer0()
    {
        _colors = new Dictionary<(int, int), Color>
        {
            [(1, 10)] = new Color(1, 0, 0),
            [(2, 20)] = new Color(0, 1, 0),
        };
        var tile = Tile((0, 1, 10), (2, 2, 20));
        Assert.Equal(new Color(0, 1, 0), MinimapColors.PickColor(tile, Provider));
    }

    [Fact]
    public void PickColor_IgnoresLayer1()
    {
        _colors = new Dictionary<(int, int), Color> { [(3, 30)] = new Color(0, 0, 1) };
        var tile = Tile((1, 3, 30));
        Assert.Null(MinimapColors.PickColor(tile, Provider));
    }

    [Fact]
    public void PickColor_FallsThroughWhenTopLayerHasNoOpaquePixels()
    {
        _colors = new Dictionary<(int, int), Color> { [(4, 40)] = new Color(0, 1, 0) };
        var tile = Tile((4, 2, 20), (3, 4, 40));
        Assert.Equal(new Color(0, 1, 0), MinimapColors.PickColor(tile, Provider));
    }

    [Fact]
    public void PickColor_ReturnsNullWhenAllLayersEmpty()
    {
        _colors = new Dictionary<(int, int), Color>();
        var tile = Tile();
        Assert.Null(MinimapColors.PickColor(tile, Provider));
    }

    [Fact]
    public void Build_ProducesOneColorPerTile()
    {
        _colors = new Dictionary<(int, int), Color>
        {
            [(1, 1)] = new Color(1, 0, 0),
            [(2, 2)] = new Color(0, 1, 0),
        };
        var map = MapDocument.Create(3, 2);
        map.SetLayer(0, 0, 0, new MapTileLayer(1, 1));
        map.SetLayer(1, 0, 2, new MapTileLayer(2, 2));
        map.SetLayer(0, 1, 4, new MapTileLayer(2, 2));
        var colors = MinimapColors.Build(map, Provider);
        Assert.Equal(6, colors.Length);
        Assert.Equal(new Color(1, 0, 0), colors[0]);
        Assert.Equal(new Color(0, 1, 0), colors[1]);
        Assert.Equal(Colors.Black, colors[2]);
        Assert.Equal(new Color(0, 1, 0), colors[3]);
        Assert.Equal(Colors.Black, colors[4]);
        Assert.Equal(Colors.Black, colors[5]);
    }
}
