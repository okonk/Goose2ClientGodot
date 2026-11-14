using Goose2Client.Network.Packets;
using Xunit;

public class StylistPacketsTests
{
    [Fact]
    public void FormatStc_FormatsHairAndFace()
    {
        Assert.Equal("STC12,5", StylistPackets.FormatStc(12, 5));
    }
}
