using Goose2Client.Network.Packets;
using Xunit;

public class StylistPacketsTests
{
    [Fact]
    public void FormatStc_FormatsHairFaceAndBody()
    {
        Assert.Equal("STC12,5,11", StylistPackets.FormatStc(12, 5, 11));
    }
}
