using Xunit;

namespace Goose2Client.Tests;

public class WindowButtonFlagsTests
{
    // Wire order: Combine, Close, Back, Next, OK  → indices 0..4
    // Goose2 enum: Exit=0, Combine=1, Close=2, Back=3, Next=4, OK=5
    // Packet index = (int)button - 1

    [Fact]
    public void IsEnabled_null_returnsFalse()
    {
        Assert.False(WindowButtonFlags.IsEnabled(null, WindowButtons.Close));
    }

    [Fact]
    public void IsEnabled_vendorShop_closeOnly()
    {
        // MKW example: Welcome to my shop!,0,1,0,0,0
        var buttons = new[] { false, true, false, false, false };

        Assert.False(WindowButtonFlags.IsEnabled(buttons, WindowButtons.Combine));
        Assert.True(WindowButtonFlags.IsEnabled(buttons, WindowButtons.Close));
        Assert.False(WindowButtonFlags.IsEnabled(buttons, WindowButtons.Back));
        Assert.False(WindowButtonFlags.IsEnabled(buttons, WindowButtons.Next));
        Assert.False(WindowButtonFlags.IsEnabled(buttons, WindowButtons.OK));
    }

    [Fact]
    public void IsEnabled_bankPaging_backAndNext()
    {
        var buttons = new[] { false, false, true, true, false };

        Assert.False(WindowButtonFlags.IsEnabled(buttons, WindowButtons.Close));
        Assert.True(WindowButtonFlags.IsEnabled(buttons, WindowButtons.Back));
        Assert.True(WindowButtonFlags.IsEnabled(buttons, WindowButtons.Next));
    }

    [Fact]
    public void IsEnabled_questDialog_closeBackNext()
    {
        var buttons = new[] { false, true, true, true, false };

        Assert.True(WindowButtonFlags.IsEnabled(buttons, WindowButtons.Close));
        Assert.True(WindowButtonFlags.IsEnabled(buttons, WindowButtons.Back));
        Assert.True(WindowButtonFlags.IsEnabled(buttons, WindowButtons.Next));
    }

    [Fact]
    public void IsEnabled_exit_hasNoPacketSlot()
    {
        var buttons = new[] { true, true, true, true, true };
        Assert.False(WindowButtonFlags.IsEnabled(buttons, WindowButtons.Exit));
    }

    [Fact]
    public void IsEnabled_shortArray_outOfRange_returnsFalse()
    {
        var buttons = new[] { false, true }; // only Combine + Close
        Assert.True(WindowButtonFlags.IsEnabled(buttons, WindowButtons.Close));
        Assert.False(WindowButtonFlags.IsEnabled(buttons, WindowButtons.Back));
        Assert.False(WindowButtonFlags.IsEnabled(buttons, WindowButtons.Next));
    }

    [Fact]
    public void IsEnabled_ok_usesLastSlot()
    {
        var buttons = new[] { false, false, false, false, true };
        Assert.True(WindowButtonFlags.IsEnabled(buttons, WindowButtons.OK));
    }
}
