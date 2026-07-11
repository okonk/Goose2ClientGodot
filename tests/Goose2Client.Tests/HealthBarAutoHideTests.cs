using Goose2Client.Character;
using Xunit;

public class HealthBarAutoHideTests
{
    [Fact]
    public void InitiallyVisible()
    {
        var hide = new HealthBarAutoHide();
        Assert.True(hide.Visible);
    }

    [Fact]
    public void FullVitals_HidesAfterDelay()
    {
        var hide = new HealthBarAutoHide();

        // At t=10, both vitals go full -> schedule hide at 12
        hide.OnVitalsChanged(1f, 1f, 10.0);
        Assert.True(hide.Visible);

        // Just before the 2s delay expires -> still visible
        Assert.True(hide.Tick(11.9));

        // At exactly the delay -> hides
        Assert.False(hide.Tick(12.0));
    }

    [Fact]
    public void DamagedVitals_PersistsVisible()
    {
        var hide = new HealthBarAutoHide();

        // HP damaged -> no hide scheduled
        hide.OnVitalsChanged(0.5f, 1f, 10.0);
        Assert.True(hide.Visible);

        // Even at t=100, still visible (never scheduled a hide)
        Assert.True(hide.Tick(100.0));
    }

    [Fact]
    public void PartialMana_CancelsHide()
    {
        var hide = new HealthBarAutoHide();

        // Both full at t=10 -> hide at 12
        hide.OnVitalsChanged(1f, 1f, 10.0);
        Assert.True(hide.Visible);

        // At t=11, HP full but MP partial -> cancel hide
        hide.OnVitalsChanged(1f, 0.7f, 11.0);
        Assert.True(hide.Visible);

        // At t=12, no hide happened because it was cancelled
        Assert.True(hide.Tick(12.0));
    }

    [Fact]
    public void AfterHidden_VitalsChange_ShowsAgain()
    {
        var hide = new HealthBarAutoHide();

        // Both full at t=10 -> hide at 12
        hide.OnVitalsChanged(1f, 1f, 10.0);

        // At t=12, hidden
        Assert.False(hide.Tick(12.0));
        Assert.False(hide.Visible);

        // HP drops to 0.9 -> show again immediately
        hide.OnVitalsChanged(0.9f, 1f, 12.0);
        Assert.True(hide.Visible);
    }

    [Fact]
    public void BothZero_StillVisible()
    {
        var hide = new HealthBarAutoHide();

        // Both at 0 -> no hide (0 < 1)
        hide.OnVitalsChanged(0f, 0f, 10.0);
        Assert.True(hide.Visible);
        Assert.True(hide.Tick(12.0));
    }

    [Fact]
    public void ExactlyOneHP_OneMP_Hides()
    {
        var hide = new HealthBarAutoHide();

        // hpPercent=1.0 and mpPercent=1.0 are both >= 1 -> hide scheduled
        hide.OnVitalsChanged(1f, 1f, 10.0);
        Assert.True(hide.Visible);
        Assert.False(hide.Tick(12.0));
    }

    [Fact]
    public void HideDelayIsTwoSeconds()
    {
        var hide = new HealthBarAutoHide();
        hide.OnVitalsChanged(1f, 1f, 0.0);

        Assert.True(hide.Tick(1.999));
        Assert.False(hide.Tick(2.0));
    }

    [Fact]
    public void HideDelaySeconds_IsPublicDouble()
    {
        Assert.Equal(2.0, HealthBarAutoHide.HideDelaySeconds);
    }
}
