using Goose2Client.Network;
using Goose2Client.Network.Packets;
using Xunit;

namespace Goose2Client.Network.Packets.Tests;

public class EnchantPacketsTests
{
    [Fact]
    public void Ewp_ParsesHeaderAndLines()
    {
        var p = (EnchantPreviewPacket)new EnchantPreviewPacket().Parse(new PacketParser("EWP12,5,3,7,1|Line A|Line B", "EWP"));
        Assert.Equal(12, p.WindowId);
        Assert.Equal(5, p.ItemSlot);
        Assert.Equal(3, p.EssenceSlot);
        Assert.Equal(7, p.ProtectionSlot);
        Assert.True(p.CanEnchant);
        Assert.Equal(new[] { "Line A", "Line B" }, p.Lines);
    }

    [Fact]
    public void Ewp_LineContainingCommasStaysWhole()
    {
        var p = (EnchantPreviewPacket)new EnchantPreviewPacket().Parse(new PacketParser("EWP1,5,3,7,1|1,2,3", "EWP"));
        Assert.Equal(new[] { "1,2,3" }, p.Lines);
    }

    [Fact]
    public void Ewp_NoLinesYieldsEmptyArray()
    {
        var p = (EnchantPreviewPacket)new EnchantPreviewPacket().Parse(new PacketParser("EWP1,5,3,7,0", "EWP"));
        Assert.Empty(p.Lines);
    }

    [Fact]
    public void Ewp_ParsesCanEnchantFlag()
    {
        var disabled = (EnchantPreviewPacket)new EnchantPreviewPacket().Parse(new PacketParser("EWP1,5,3,7,0", "EWP"));
        Assert.False(disabled.CanEnchant);

        var enabled = (EnchantPreviewPacket)new EnchantPreviewPacket().Parse(new PacketParser("EWP1,5,3,7,1", "EWP"));
        Assert.True(enabled.CanEnchant);
    }

    [Fact]
    public void FormatEws_FormatsSlots()
    {
        Assert.Equal("EWS5,0,7", EnchantPreviewPacket.FormatEws(5, 0, 7));
    }

    [Fact]
    public void FormatEwc_FormatsSlots()
    {
        Assert.Equal("EWC5,0,7", EnchantPreviewPacket.FormatEwc(5, 0, 7));
    }
}
