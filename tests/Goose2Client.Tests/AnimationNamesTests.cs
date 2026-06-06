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

    [Theory]
    [InlineData(3, "no-equip")]
    [InlineData(4, "1hand")]
    [InlineData(5, "staff")]
    [InlineData(6, "2hand")]
    [InlineData(7, "bow")]
    [InlineData(0, "no-equip")]
    public void AttackVariant_maps_body_state(int bodyState, string expected)
        => Assert.Equal(expected, AnimationNames.AttackVariant(bodyState));

    // First candidate = the clip a fully-featured slot (e.g. the Body) should prefer.
    [Theory]
    [InlineData("idle", 3, Direction.Down, "idle-no-equip-down")]
    [InlineData("idle", 4, Direction.Down, "idle-equip-down")]
    [InlineData("walk", 4, Direction.Left, "walk-equip-left")]
    [InlineData("walk", 3, Direction.Left, "walk-no-equip-left")]
    [InlineData("attack", 4, Direction.Down, "attack-1hand-down")]
    [InlineData("attack", 5, Direction.Up, "attack-staff-up")]
    [InlineData("attack", 6, Direction.Right, "attack-2hand-right")]
    [InlineData("attack", 7, Direction.Down, "attack-bow-down")]
    [InlineData("attack", 3, Direction.Down, "attack-no-equip-down")]
    [InlineData("mounted-walk", 4, Direction.Down, "mounted-walk-down")]
    [InlineData("mounted-idle", 4, Direction.Up, "mounted-idle-up")]
    public void Candidates_prefers_specific_clip(string motion, int bodyState, Direction d, string expectedFirst)
        => Assert.Equal(expectedFirst, AnimationNames.Candidates(motion, bodyState, d)[0]);

    [Fact]
    public void Candidates_fall_back_through_generic_for_weapon_slots()
    {
        // A Hands sprite only has idle-equip / walk-equip / attack-<type>; equipped idle must offer
        // idle-equip first, and an attack must offer the weapon clip then degrade to idle.
        var idle = AnimationNames.Candidates("idle", 4, Direction.Down);
        Assert.Equal("idle-equip-down", idle[0]);

        var atk = AnimationNames.Candidates("attack", 4, Direction.Down);
        Assert.Equal("attack-1hand-down", atk[0]);
        Assert.Contains("idle-equip-down", atk);   // graceful fallback for slots lacking an attack clip
    }
}
