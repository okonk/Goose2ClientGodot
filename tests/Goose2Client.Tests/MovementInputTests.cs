using Goose2Client.Character;
using Xunit;

namespace Goose2Client.Tests;

public class MovementInputTests
{
    [Fact]
    public void Resolve_no_keys_returns_null()
    {
        bool wasMovingVertical = false;
        var result = MovementInput.Resolve(false, false, false, false, ref wasMovingVertical);
        Assert.Null(result);
    }

    [Theory]
    [InlineData(true, false, false, false, Direction.Up)]
    [InlineData(false, true, false, false, Direction.Down)]
    [InlineData(false, false, true, false, Direction.Left)]
    [InlineData(false, false, false, true, Direction.Right)]
    public void Resolve_single_key_returns_direction(bool up, bool down, bool left, bool right, Direction expected)
    {
        bool wasMovingVertical = false;
        var result = MovementInput.Resolve(up, down, left, right, ref wasMovingVertical);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Resolve_held_diagonal_alternates_axis()
    {
        bool wasMovingVertical = true;

        var d1 = MovementInput.Resolve(true, false, false, true, ref wasMovingVertical);
        Assert.Equal(Direction.Right, d1);
        Assert.False(wasMovingVertical);

        var d2 = MovementInput.Resolve(true, false, false, true, ref wasMovingVertical);
        Assert.Equal(Direction.Up, d2);
        Assert.True(wasMovingVertical);
    }
}
