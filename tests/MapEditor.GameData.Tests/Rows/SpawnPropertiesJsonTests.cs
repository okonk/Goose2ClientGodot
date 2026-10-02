using MapEditor.GameData.Rows;
using Xunit;

namespace MapEditor.GameData.Tests.Rows;

public class SpawnPropertiesJsonTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{}")]
    [InlineData("{\"facing\":1}")]
    public void TryRead_NoCanMove_ReturnsDefault(string properties)
    {
        Assert.True(SpawnPropertiesJson.TryRead(properties, out var value));
        Assert.Equal(SpawnMoveOverride.Default, value);
    }

    [Fact]
    public void TryRead_CanMoveTrue_ReturnsMovable()
    {
        Assert.True(SpawnPropertiesJson.TryRead("{\"canMove\":true}", out var value));
        Assert.Equal(SpawnMoveOverride.Movable, value);
    }

    [Fact]
    public void TryRead_CanMoveFalse_ReturnsStationary()
    {
        Assert.True(SpawnPropertiesJson.TryRead("{\"canMove\":false}", out var value));
        Assert.Equal(SpawnMoveOverride.Stationary, value);
    }

    [Theory]
    [InlineData("{\"canMove\":")]
    [InlineData("[1,2]")]
    [InlineData("{\"canMove\":1}")]
    [InlineData("{\"canMove\":\"yes\"}")]
    [InlineData("{\"canMove\":null}")]
    public void TryRead_Invalid_ReturnsFalse(string properties)
    {
        Assert.False(SpawnPropertiesJson.TryRead(properties, out var value));
        Assert.Equal(SpawnMoveOverride.Default, value);
    }

    [Fact]
    public void TryWrite_DefaultRemovesCanMove()
    {
        Assert.True(SpawnPropertiesJson.TryWrite("{\"canMove\":true,\"foo\":1}", SpawnMoveOverride.Default, out var result));
        Assert.Equal("{\"foo\":1}", result);
    }

    [Fact]
    public void TryWrite_MovableAppendsCanMoveLast()
    {
        Assert.True(SpawnPropertiesJson.TryWrite("{\"foo\": 1}", SpawnMoveOverride.Movable, out var result));
        Assert.Equal("{\"foo\":1,\"canMove\":true}", result);
    }

    [Fact]
    public void TryWrite_DefaultOnCanMoveOnly_ReturnsEmpty()
    {
        Assert.True(SpawnPropertiesJson.TryWrite("{\"canMove\":true}", SpawnMoveOverride.Default, out var result));
        Assert.Equal("", result);
        Assert.NotEqual("{}", result);
    }

    [Fact]
    public void TryWrite_EmptyInput_Default_ReturnsEmpty()
    {
        Assert.True(SpawnPropertiesJson.TryWrite("", SpawnMoveOverride.Default, out var result));
        Assert.Equal("", result);
    }

    [Fact]
    public void TryWrite_EmptyInput_Movable_ReturnsCanMoveTrue()
    {
        Assert.True(SpawnPropertiesJson.TryWrite("", SpawnMoveOverride.Movable, out var result));
        Assert.Equal("{\"canMove\":true}", result);
    }

    [Fact]
    public void TryWrite_EmptyInput_Stationary_ReturnsCanMoveFalse()
    {
        Assert.True(SpawnPropertiesJson.TryWrite("", SpawnMoveOverride.Stationary, out var result));
        Assert.Equal("{\"canMove\":false}", result);
    }

    [Fact]
    public void TryWrite_ReplacesCanMoveInPlace()
    {
        Assert.True(SpawnPropertiesJson.TryWrite("{\"canMove\":true,\"foo\":1}", SpawnMoveOverride.Stationary, out var result));
        Assert.Equal("{\"canMove\":false,\"foo\":1}", result);
    }

    [Fact]
    public void TryWrite_MalformedInput_ReturnsFalse()
    {
        Assert.False(SpawnPropertiesJson.TryWrite("{\"canMove\":", SpawnMoveOverride.Movable, out var result));
        Assert.Equal("", result);
    }

    [Theory]
    [InlineData("", SpawnMoveOverride.Default)]
    [InlineData("", SpawnMoveOverride.Movable)]
    [InlineData("", SpawnMoveOverride.Stationary)]
    [InlineData("{\"foo\":1}", SpawnMoveOverride.Default)]
    [InlineData("{\"foo\":1}", SpawnMoveOverride.Movable)]
    [InlineData("{\"foo\":1}", SpawnMoveOverride.Stationary)]
    public void RoundTrip_WriteThenRead_ReturnsSameValue(string baseProperties, SpawnMoveOverride value)
    {
        Assert.True(SpawnPropertiesJson.TryWrite(baseProperties, value, out var written));
        Assert.True(SpawnPropertiesJson.TryRead(written, out var read));
        Assert.Equal(value, read);
    }
}
