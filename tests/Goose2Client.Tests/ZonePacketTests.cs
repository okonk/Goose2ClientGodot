using Goose2Client.Network;
using Goose2Client.Network.Packets;
using Xunit;

namespace Goose2Client.Network.Packets.Tests;

public class ZonePacketTests
{
    [Fact]
    public void ZoneAdd_ParsesHeaderAndConvertsTilesToZeroBased()
    {
        var p = (ZoneAddPacket)new ZoneAddPacket().Parse(new PacketParser("ZADD7,120,4,8000,10,10,11,10,10,11", "ZADD"));

        Assert.Equal(7, p.Id);
        Assert.Equal(120, p.AnimationId);
        Assert.Equal(8000, p.RemainingMs);
        Assert.Equal(new[] { (9, 9), (10, 9), (9, 10) }, p.Tiles);
    }

    [Fact]
    public void ZoneAdd_WithNoTiles_ParsesEmpty()
    {
        var p = (ZoneAddPacket)new ZoneAddPacket().Parse(new PacketParser("ZADD7,120,4,8000", "ZADD"));

        Assert.Empty(p.Tiles);
    }

    [Fact]
    public void ZoneDelete_ParsesId()
    {
        var p = (ZoneDeletePacket)new ZoneDeletePacket().Parse(new PacketParser("ZDEL7", "ZDEL"));

        Assert.Equal(7, p.Id);
    }
}
