using Goose2Client.Network;
using Goose2Client.Network.Packets;
using Xunit;

namespace Goose2Client.Network.Packets.Tests;

public class SpellCooldownPacketTests
{
    [Fact]
    public void Cdr_ParsesSlotAndRemainingMs()
    {
        var p = (SpellCooldownPacket)new SpellCooldownPacket().Parse(new PacketParser("CDR4,1500", "CDR"));
        Assert.Equal(3, p.SlotNumber);
        Assert.Equal(1500, p.RemainingMilliseconds);
    }

    [Fact]
    public void Cdr_ParsesZeroRemaining()
    {
        var p = (SpellCooldownPacket)new SpellCooldownPacket().Parse(new PacketParser("CDR1,0", "CDR"));
        Assert.Equal(0, p.SlotNumber);
        Assert.Equal(0, p.RemainingMilliseconds);
    }
}
