using Goose2Client;
using Goose2Client.Character;
using Xunit;

public class AnimationNamesTests
{
    [Theory]
    [InlineData(Direction.Up, "up")]
    [InlineData(Direction.Right, "right")]
    [InlineData(Direction.Down, "down")]
    [InlineData(Direction.Left, "left")]
    public void DirectionString_maps_each_direction(Direction d, string expected)
        => Assert.Equal(expected, AnimationNames.DirectionString(d));

    [Fact]
    public void Clip_combines_state_and_direction()
        => Assert.Equal("walk-down", AnimationNames.Clip("walk", Direction.Down));
}
