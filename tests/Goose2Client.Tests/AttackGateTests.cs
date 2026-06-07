using Goose2Client.Character;
using Xunit;

public class AttackGateTests
{
    [Fact] public void FirstAttackAlwaysAllowed()
        => Assert.True(new AttackGate().TryAttack(0.0, 1000));

    [Fact] public void SecondAttackBlockedWithinWindow()
    {
        var g = new AttackGate();
        Assert.True(g.TryAttack(0.0, 1000));
        Assert.False(g.TryAttack(0.5, 1000));
    }

    [Fact] public void SecondAttackAllowedAfterWindow()
    {
        var g = new AttackGate();
        Assert.True(g.TryAttack(0.0, 1000));
        Assert.True(g.TryAttack(1.0, 1000));
    }

    [Fact] public void ZeroSpeedFallsBackToDefault()
    {
        var g = new AttackGate();
        Assert.True(g.TryAttack(0.0, 0));
        Assert.False(g.TryAttack(0.1, 0));
    }
}
