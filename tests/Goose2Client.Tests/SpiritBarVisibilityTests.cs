using Goose2Client.UI;
using Xunit;

namespace Goose2Client.Tests
{
    public class SpiritBarVisibilityTests
    {
        [Fact] public void NoSnf_Hides_EvenWhenLatchedOptionOnAndSpExists()
            => Assert.False(SpiritBarVisibility.ShouldShow(false, true, true, 500));

        [Fact] public void OptionOff_Hides_EvenWhenLatchedAndSpExists()
            => Assert.False(SpiritBarVisibility.ShouldShow(true, false, true, 500));

        [Fact] public void OptionOn_NotLatched_ZeroMaxSp_Hidden()   // default state
            => Assert.False(SpiritBarVisibility.ShouldShow(true, true, false, 0));

        [Fact] public void OptionOn_NotLatched_NonZeroMaxSp_Shown()
            => Assert.True(SpiritBarVisibility.ShouldShow(true, true, false, 1));

        // Adversarial: "keep it visible forever after" — latch must win even when
        // MaxSP later drops back to 0 (a wrong impl keying on live MaxSP only fails here).
        [Fact] public void OptionOn_Latched_ZeroMaxSp_StillShown()
            => Assert.True(SpiritBarVisibility.ShouldShow(true, true, true, 0));

        [Fact] public void OptionOn_Latched_NonZeroMaxSp_Shown()
            => Assert.True(SpiritBarVisibility.ShouldShow(true, true, true, 250));
    }
}
