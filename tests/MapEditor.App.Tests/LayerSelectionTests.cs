using MapEditor.App;
using Xunit;

namespace MapEditor.App.Tests;

public class LayerSelectionTests
{
    [Fact]
    public void Plain_SingleSelectsClickedLayer()
    {
        Assert.Equal((byte)0b00100, LayerSelection.Plain(2));
    }

    [Fact]
    public void Toggle_AddsAndRemovesLayer()
    {
        Assert.Equal((byte)0b01100, LayerSelection.Toggle(0b00100, 3));
        Assert.Equal((byte)0b00000, LayerSelection.Toggle(0b00100, 2));
    }

    [Fact]
    public void Toggle_OffLastSelectedLayer_SingleSelectsIt()
    {
        Assert.Equal((byte)0b00100, LayerSelection.Toggle(0b00100, 2, keepNonEmpty: true));
    }

    [Fact]
    public void Range_SelectsInclusiveSpanFromAnchor()
    {
        Assert.Equal((byte)0b01110, LayerSelection.Range(1, 3));
        Assert.Equal((byte)0b01110, LayerSelection.Range(3, 1));
    }

    [Fact]
    public void Apply_Plain_SingleSelectsAndReanchors()
    {
        Assert.Equal(((byte)0b00010, 1), LayerSelection.Apply(0b01000, 2, 1, LayerClickMode.Plain));
    }

    [Fact]
    public void Apply_Toggle_UpdatesMaskAndReanchors()
    {
        Assert.Equal(((byte)0b00110, 1), LayerSelection.Apply(0b00100, 0, 1, LayerClickMode.Toggle));
    }

    [Fact]
    public void Apply_Toggle_OffLastSelectedLayer_KeepsNonEmpty()
    {
        Assert.Equal(((byte)0b00001, 0), LayerSelection.Apply(0b00001, 0, 0, LayerClickMode.Toggle));
    }

    [Fact]
    public void Apply_Range_KeepsAnchorForExtension()
    {
        var (first, anchor) = LayerSelection.Apply(0b00001, 0, 3, LayerClickMode.Range);
        Assert.Equal((byte)0b01111, first);
        Assert.Equal(0, anchor);
        var (second, _) = LayerSelection.Apply(first, anchor, 4, LayerClickMode.Range);
        Assert.Equal((byte)0b11111, second);
    }
}
