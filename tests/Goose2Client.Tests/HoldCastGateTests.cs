using Goose2Client;
using Xunit;

public class HoldCastGateTests
{
    [Fact] public void NoHeldKeyNeverFires()
    {
        var g = new HoldCastGate(0.3);
        for (int i = 0; i < 100; i++)
            Assert.False(g.Update(null, 0.1));
    }

    [Fact] public void FiresAfterTheDelay()
    {
        var g = new HoldCastGate(0.5);
        Assert.False(g.Update("Hotkey1", 0.2));
        Assert.False(g.Update("Hotkey1", 0.2));
        Assert.True(g.Update("Hotkey1", 0.2));
    }

    [Fact] public void FiresAtExactlyTheDelay()
    {
        var g = new HoldCastGate(0.5);
        Assert.False(g.Update("Hotkey1", 0.4));
        Assert.True(g.Update("Hotkey1", 0.1));
    }

    [Fact] public void ZeroDelayFiresOnFirstTick()
    {
        var g = new HoldCastGate(0.0);
        Assert.True(g.Update("Hotkey1", 0.0));
    }

    [Fact] public void HoldingPastTheDelayKeepsRepeatingWithoutRefiring()
    {
        var g = new HoldCastGate(0.3);
        Assert.True(g.Update("Hotkey1", 0.3));
        Assert.True(g.IsRepeating);
        for (int i = 0; i < 100; i++)
            Assert.False(g.Update("Hotkey1", 0.1));
        Assert.True(g.IsRepeating);
    }

    [Fact] public void ReleaseResetsTheHold()
    {
        var g = new HoldCastGate(0.5);
        Assert.False(g.Update("Hotkey1", 0.4));
        Assert.False(g.Update(null, 0.4));
        Assert.False(g.Update("Hotkey1", 0.4));
        Assert.True(g.Update("Hotkey1", 0.2));
    }

    [Fact] public void ANewPressAfterAReleasedHoldStartsFromZero()
    {
        var g = new HoldCastGate(0.3);
        Assert.True(g.Update("Hotkey1", 2.0));
        Assert.False(g.Update(null, 0.016));
        Assert.False(g.Update("Hotkey1", 0.016));
        Assert.False(g.IsRepeating);
        Assert.False(g.Update("Hotkey1", 0.016));
        Assert.True(g.Update("Hotkey1", 0.3));
    }

    [Fact] public void SwitchingHeldKeyRestartsTheHold()
    {
        var g = new HoldCastGate(0.5);
        Assert.False(g.Update("Hotkey1", 0.4));
        Assert.False(g.Update("Hotkey2", 0.4));
        Assert.True(g.Update("Hotkey2", 0.2));
    }

    [Fact] public void SwitchingHeldKeyClearsRepeating()
    {
        var g = new HoldCastGate(0.3);
        Assert.True(g.Update("Hotkey1", 0.3));
        Assert.True(g.IsRepeating);
        Assert.False(g.Update("Hotkey2", 0.1));
        Assert.False(g.IsRepeating);
    }

    [Fact] public void ASpentPressNeverFiresWhileItStaysDown()
    {
        var g = new HoldCastGate(0.3);
        Assert.False(g.Update("Hotkey1", 0.1));
        g.Spend();
        for (int i = 0; i < 100; i++)
            Assert.False(g.Update("Hotkey1", 0.1));
        Assert.False(g.IsRepeating);
    }

    [Fact] public void ARepeatingPressStopsRepeatingOnceSpent()
    {
        var g = new HoldCastGate(0.3);
        Assert.True(g.Update("Hotkey1", 0.3));
        g.Spend();
        Assert.False(g.IsRepeating);
        Assert.False(g.Update("Hotkey1", 0.3));
    }

    [Fact] public void ReleasingASpentPressAllowsAFreshOne()
    {
        var g = new HoldCastGate(0.3);
        Assert.False(g.Update("Hotkey1", 0.1));
        g.Spend();
        Assert.False(g.Update(null, 0.1));
        Assert.False(g.Update("Hotkey1", 0.2));
        Assert.True(g.Update("Hotkey1", 0.1));
    }

    [Fact] public void SpendingWithNoPressDownIsHarmless()
    {
        var g = new HoldCastGate(0.3);
        g.Spend();
        Assert.False(g.Update("Hotkey1", 0.2));
        Assert.True(g.Update("Hotkey1", 0.1));
    }
}
