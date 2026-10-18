using Godot;
using Goose2Client.Map;
using Xunit;

namespace Goose2Client.Tests;

public class AmbienceMathTests
{
    [Fact]
    public void Darkness_Noon_IsStart()
    {
        const long duration = 1_440_000;
        var d = AmbienceMath.Darkness(0f, 0.7f, duration / 2, duration);
        Assert.Equal(0f, d, 5);
    }

    [Fact]
    public void Darkness_Midnight_IsEnd()
    {
        var d = AmbienceMath.Darkness(0f, 0.7f, 0, 1_440_000);
        Assert.Equal(0.7f, d, 5);
    }

    [Fact]
    public void Darkness_StaticCycle_IsStartAlpha()
    {
        var d = AmbienceMath.Darkness(0.7f, 0.7f, 12_345, 0);
        Assert.Equal(0.7f, d, 5);
    }

    [Fact]
    public void Darkness_None_IsZero()
    {
        var d = AmbienceMath.Darkness(0f, 0f, 999_999, 1_440_000);
        Assert.Equal(0f, d, 5);
    }

    [Fact]
    public void ModulateColor_NoDarkness_IsWhite()
    {
        var c = AmbienceMath.ModulateColor(0x090909FF, 0f);
        Assert.Equal(Colors.White, c);
    }

    [Fact]
    public void ModulateColor_FullDark_IsTint()
    {
        var c = AmbienceMath.ModulateColor(0x090909FF, 1f);
        var expected = new Color(9f / 255f, 9f / 255f, 9f / 255f, 1f);
        Assert.Equal(expected, c);
    }

    [Fact]
    public void ModulateColor_IgnoresTintAlpha()
    {
        var a = AmbienceMath.ModulateColor(0x09090900, 0.5f);
        var b = AmbienceMath.ModulateColor(0x090909FF, 0.5f);
        Assert.Equal(a, b);
    }

    [Fact]
    public void TimeOfDay_WrapsElapsed()
    {
        var t = AmbienceMath.TimeOfDay(1_500_000, 1_440_000);
        Assert.Equal(60_000f / 1_440_000f, t, 5);
    }
}
