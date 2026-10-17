using Goose2Client.Network;
using Goose2Client.Network.Packets;
using Xunit;

namespace Goose2Client.Network.Packets.Tests;

public class MapWarpPacketTests
{
    [Fact]
    public void Mwp_ConvertsOneBasedToZeroBased()
    {
        var p = (MapWarpPacket)new MapWarpPacket().Parse(new PacketParser("MWP3,7", "MWP"));
        Assert.Equal(2, p.X);
        Assert.Equal(6, p.Y);
    }

    [Fact]
    public void Mwp_IgnoresTrailingFields()
    {
        var p = (MapWarpPacket)new MapWarpPacket().Parse(new PacketParser("MWP3,7,9,extra", "MWP"));
        Assert.Equal(2, p.X);
        Assert.Equal(6, p.Y);
    }
}
