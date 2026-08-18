using Goose2Client.Character;
using Xunit;

public class CharacterMotionTests
{
    [Fact]
    public void State_attack_lock_overrides_moving()
        => Assert.Equal("attack", CharacterMotion.State(isMoving: true, lockedMotion: "attack", isMounted: false));

    [Fact]
    public void State_cast_lock_returns_cast()
        => Assert.Equal("cast", CharacterMotion.State(isMoving: true, lockedMotion: "cast", isMounted: false));

    [Fact]
    public void State_walk_when_moving_and_not_locked()
        => Assert.Equal("walk", CharacterMotion.State(isMoving: true, lockedMotion: null, isMounted: false));

    [Fact]
    public void State_idle_when_still()
        => Assert.Equal("idle", CharacterMotion.State(isMoving: false, lockedMotion: null, isMounted: false));

    [Fact] // a mounted rider uses the mounted-* clips for walk/idle (the mount slot itself passes isMounted:false)
    public void State_mounted_walk_and_idle()
    {
        Assert.Equal("mounted-walk", CharacterMotion.State(isMoving: true, lockedMotion: null, isMounted: true));
        Assert.Equal("mounted-idle", CharacterMotion.State(isMoving: false, lockedMotion: null, isMounted: true));
    }

    [Fact] // attack still wins even when mounted
    public void State_attack_lock_overrides_mounted()
        => Assert.Equal("attack", CharacterMotion.State(isMoving: true, lockedMotion: "attack", isMounted: true));

    [Fact]
    public void PixelsPerSecond_scales_inversely_with_moveSpeed()
    {
        // Unity: speed = 1000 / MoveSpeed (world units = tiles); px = units * 32.
        Assert.Equal(32f * (1000f / 250f), CharacterMotion.PixelsPerSecond(250), 3);
    }

    [Fact]
    public void PixelsPerSecond_guards_zero_movespeed()
        => Assert.True(CharacterMotion.PixelsPerSecond(0) > 0);

    [Fact]
    public void ShouldPlayIdleAfterStep_only_when_not_chained()
    {
        Assert.False(CharacterMotion.ShouldPlayIdleAfterStep(chainedNextStep: true));
        Assert.True(CharacterMotion.ShouldPlayIdleAfterStep(chainedNextStep: false));
    }

    [Fact]
    public void RemainingStepBudget_carries_leftover_past_tile()
    {
        // 20px budget, 12px left to target → 8px leftover for the next tile.
        Assert.Equal(8f, CharacterMotion.RemainingStepBudget(20f, 12f), 3);
        Assert.Equal(0f, CharacterMotion.RemainingStepBudget(10f, 12f), 3);
        Assert.Equal(0f, CharacterMotion.RemainingStepBudget(0f, 5f), 3);
        Assert.Equal(5f, CharacterMotion.RemainingStepBudget(5f, 0f), 3);
    }
}
