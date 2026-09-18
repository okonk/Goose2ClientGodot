using Goose2Client.Network;
using Goose2Client.Network.Packets;
using Xunit;

namespace Goose2Client.Network.Packets.Tests;

public class CustomWindowPacketsTests
{
    [Fact]
    public void Cwg_ParsesEquippedIdAndPose()
    {
        var p = (CustomWindowGraphicPacket)new CustomWindowGraphicPacket().Parse(new PacketParser("CWG777,6", "CWG"));
        Assert.Equal(777, p.EquippedId);
        Assert.Equal(6, p.Pose);
    }

    [Fact]
    public void Cwg_ParsesEmptyEquipped()
    {
        var p = (CustomWindowGraphicPacket)new CustomWindowGraphicPacket().Parse(new PacketParser("CWG0,2", "CWG"));
        Assert.Equal(0, p.EquippedId);
        Assert.Equal(2, p.Pose);
    }

    [Fact]
    public void FormatCws_FormatsSlots()
    {
        Assert.Equal("CWS5,6", CustomWindowGraphicPacket.FormatCws(5, 6));
        Assert.Equal("CWS0,3", CustomWindowGraphicPacket.FormatCws(0, 3));
    }

    [Fact]
    public void FormatCwc_FormatsSlotsColorAndName()
    {
        Assert.Equal("CWC5,6,10,20,30,40,My Sword", CustomWindowGraphicPacket.FormatCwc(5, 6, 10, 20, 30, 40, "My Sword"));
    }
}
