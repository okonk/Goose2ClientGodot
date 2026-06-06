using Xunit;

namespace Goose2Client.Tests;

public class HelpersTests
{
    [Fact]
    public void StackSplit_singleItem_returnsOne()
    {
        Assert.Equal(1, Helpers.StackSplit(1, false, false));
    }

    [Fact]
    public void StackSplit_ctrl_returnsOne()
    {
        Assert.Equal(1, Helpers.StackSplit(10, true, false));
    }

    [Fact]
    public void StackSplit_shift_returnsHalf()
    {
        Assert.Equal(5, Helpers.StackSplit(10, false, true));
    }

    [Fact]
    public void StackSplit_noModifier_returnsFull()
    {
        Assert.Equal(10, Helpers.StackSplit(10, false, false));
    }
}
