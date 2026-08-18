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
    public void Candidates_mounted_does_not_fall_back_to_foot_walk()
    {
        // Hands/shields have no mounted sheets. Candidates must not offer walk/idle, or
        // ResolveClip would show foot-walk weapon art while mounted (Unity uses Blank).
        var walk = AnimationNames.Candidates("mounted-walk", 4, Direction.Down);
        Assert.Equal(new[] { "mounted-walk-down" }, walk);

        var idle = AnimationNames.Candidates("mounted-idle", 4, Direction.Left);
        Assert.Equal(new[] { "mounted-idle-left" }, idle);
    }

    [Fact]
    public void Candidates_fall_back_through_generic_for_weapon_slots()
    {
        // Hands sheets only have idle-equip / walk-equip / attack-<type>. Equipped idle offers
        // idle-equip first. Attack stays in the attack family only — no idle fallback (Unity Blank).
        var idle = AnimationNames.Candidates("idle", 4, Direction.Down);
        Assert.Equal("idle-equip-down", idle[0]);

        var atk = AnimationNames.Candidates("attack", 4, Direction.Down);
        Assert.Equal("attack-1hand-down", atk[0]);
        Assert.DoesNotContain("idle-equip-down", atk);
        Assert.DoesNotContain("idle-down", atk);
        Assert.Equal(new[] { "attack-1hand-down", "attack-down", "attack-no-equip-down" }, atk);

        // Staff prefers attack-staff, still offers attack-1hand for mixed hand sheets.
        var staff = AnimationNames.Candidates("attack", 5, Direction.Down);
        Assert.Equal(new[] { "attack-staff-down", "attack-1hand-down", "attack-down", "attack-no-equip-down" }, staff);
    }

    [Fact]
    public void Candidates_attack_unarmed_does_not_fall_back_to_idle()
    {
        var atk = AnimationNames.Candidates("attack", 3, Direction.Down);
        Assert.Equal(new[] { "attack-no-equip-down", "attack-down" }, atk);
    }

    [Fact]
    public void Candidates_cast_is_cast_only_no_idle_fallback()
    {
        // Missing cast clip → ResolveClip blanks the slot (Unity Blank), not idle.
        Assert.Equal(new[] { "cast-down" }, AnimationNames.Candidates("cast", 4, Direction.Down));
        Assert.Equal(new[] { "cast-left" }, AnimationNames.Candidates("cast", 3, Direction.Left));
    }

    [Fact]
    public void Candidates_unarmed_idle_still_offers_equip_fallback_for_weapon_sheets()
    {
        // Hands weapon graphics only have idle-equip-*; without this trailing candidate,
        // ResolveClip returns null, Offset stays 0, and the weapon floats at the feet.
        var idle = AnimationNames.Candidates("idle", 3, Direction.Down);
        Assert.Equal("idle-no-equip-down", idle[0]);
        Assert.Contains("idle-equip-down", idle);
        Assert.Equal("idle-equip-down", idle[^1]);
    }
}
