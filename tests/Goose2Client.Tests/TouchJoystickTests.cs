using Godot;
using Goose2Client.UI;
using Xunit;
using static Goose2Client.UI.TouchJoystick;

public class TouchJoystickTests
{
    [Theory]
    [InlineData(0f, 0f, 0)]
    [InlineData(0.2f, 0f, 0)]
    [InlineData(1f, 0f, Right)]
    [InlineData(-1f, 0f, Left)]
    [InlineData(0f, 1f, Down)]
    [InlineData(0f, -1f, Up)]
    [InlineData(1f, 1f, Right | Down)]
    [InlineData(-1f, 1f, Down | Left)]
    [InlineData(-1f, -1f, Left | Up)]
    [InlineData(1f, -1f, Up | Right)]
    public void DirectionMask_MapsOctants(float x, float y, int expected)
        => Assert.Equal(expected, DirectionMask(new Vector2(x, y)));

    [Fact]
    public void DirectionMask_SlightlyOffCardinal_StaysCardinal()
        => Assert.Equal(Right, DirectionMask(Vector2.FromAngle(Mathf.DegToRad(-25f))));

    [Fact]
    public void DirectionMask_NearZeroDegreesFromBelow_IsRight()
        => Assert.Equal(Right, DirectionMask(Vector2.FromAngle(Mathf.DegToRad(355f))));
}
