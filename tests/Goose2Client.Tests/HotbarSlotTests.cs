using Goose2Client.UI;
using Xunit;

namespace Goose2Client.Tests;

public class HotbarSlotTests
{
    [Theory]
    [InlineData(0, "1")]
    [InlineData(1, "2")]
    [InlineData(8, "9")]
    [InlineData(9, "0")]
    public void SlotLabel_maps_index_to_hotkey_digit(int index, string expected)
    {
        Assert.Equal(expected, HotbarSlot.SlotLabel(index));
    }
}
