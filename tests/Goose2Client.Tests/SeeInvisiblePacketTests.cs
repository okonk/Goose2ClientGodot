using Goose2Client.Network;
using Xunit;

namespace Goose2Client.Network.Packets.Tests;

public class SeeInvisiblePacketTests
{
    [Fact]
    public void Parse_Seen_CanSeeIsTrue()
    {
        var p = (SeeInvisiblePacket)new SeeInvisiblePacket().Parse(new PacketParser("SINVS1", "SINVS"));
        Assert.True(p.CanSee);
    }

    [Fact]
    public void Parse_NotSeen_CanSeeIsFalse()
    {
        var p = (SeeInvisiblePacket)new SeeInvisiblePacket().Parse(new PacketParser("SINVS0", "SINVS"));
        Assert.False(p.CanSee);
    }
}
