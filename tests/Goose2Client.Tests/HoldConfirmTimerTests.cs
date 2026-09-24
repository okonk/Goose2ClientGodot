using Goose2Client;
using Xunit;

public class HoldConfirmTimerTests
{
    [Fact] public void NoHeldKeyNeverConfirms()
    {
        var t = new HoldConfirmTimer(0.5);
        for (int i = 0; i < 100; i++)
            Assert.False(t.Tick(null, 0.1));
    }

    [Fact] public void ConfirmsAfterDelay()
    {
        var t = new HoldConfirmTimer(0.5);
        Assert.False(t.Tick("Hotkey1", 0.2));
        Assert.False(t.Tick("Hotkey1", 0.2));
        Assert.True(t.Tick("Hotkey1", 0.2));
    }

    [Fact] public void ConfirmsAtExactlyTheDelay()
    {
        var t = new HoldConfirmTimer(0.5);
        Assert.False(t.Tick("Hotkey1", 0.4));
        Assert.True(t.Tick("Hotkey1", 0.1));
    }

    [Fact] public void ReleaseResetsTheHold()
    {
        var t = new HoldConfirmTimer(0.5);
        Assert.False(t.Tick("Hotkey1", 0.4));
        Assert.False(t.Tick(null, 0.4));
        Assert.False(t.Tick("Hotkey1", 0.4));
        Assert.True(t.Tick("Hotkey1", 0.2));
    }

    [Fact] public void SwitchingHeldKeyResetsTheHold()
    {
        var t = new HoldConfirmTimer(0.5);
        Assert.False(t.Tick("Hotkey1", 0.4));
        Assert.False(t.Tick("Hotkey2", 0.4));
        Assert.True(t.Tick("Hotkey2", 0.2));
    }

    [Fact] public void ZeroDelayConfirmsOnFirstTick()
    {
        var t = new HoldConfirmTimer(0.0);
        Assert.True(t.Tick("Hotkey1", 0.0));
    }
}
